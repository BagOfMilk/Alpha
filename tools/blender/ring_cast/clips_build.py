# Збирає бібліотеку кліпів на спільному скелеті: переносить UAL і зброю на скелет набору, знімає зміщення в окрему криву кореня,
# замикає цикли, узгоджує фазу кроку, додає власні кліпи й позначає, звідки що. Виходи — npz + json для Blender (clips_write.py) і сторінки.
#
#   python tools/blender/ring_cast/clips_build.py <кеш npz> <тека виходу>
import json, os, sys
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from clips_lib import *
import cast_spec as CS
import clips_authored as AUTH

REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
ANIM = os.path.join(REPO, "Assets", "Art", "Animations")
FPS = 30.0

# Кліпи UAL і зброї, які мають сенс для реслінгу. Решта (пістолет, лук, ферма, плавання, зомбі…) у бібліотеку не йде; перелік — у CAST_ANIM.md.
UAL_KEEP = {
    "Idle_Loop": "idle", "Idle_Talking_Loop": "idle", "Idle_FoldArms_Loop": "idle", "Idle_TalkingPhone_Loop": "idle", "Idle_No_Loop": "taunt",
    "Yes": "taunt", "Dance_Loop": "taunt", "Walk_Loop": "locomotion", "Walk_Formal_Loop": "locomotion",
    "Jog_Fwd_Loop": "locomotion", "Sprint_Loop": "locomotion", "Crouch_Idle_Loop": "guard", "Crouch_Fwd_Loop": "guard",
    "Fixing_Kneeling": "guard", "Sitting_Enter": "sit", "Sitting_Idle_Loop": "sit", "Sitting_Exit": "sit", "Sitting_Talking_Loop": "sit",
    "Hit_Chest": "react", "Hit_Head": "react", "Punch_Enter": "strike", "Punch_Jab": "strike", "Punch_Cross": "strike",
    "Roll": "move", "Jump_Start": "air", "Jump_Loop": "air", "Jump_Land": "air",
    "NinjaJump_Start": "air", "NinjaJump_Idle_Loop": "air", "NinjaJump_Land": "air", "ClimbUp_1m_RM": "corner", "LayToIdle": "getup",
    "Push_Loop": "strike", "Interact": "misc", "PickUp_Table": "misc", "Slide_Start": "move", "Slide_Loop": "move", "Slide_Exit": "move",
    "OverhandThrow": "weapon",
}
WEAPON_KEEP = {}

CAT_PREFIX = [("Ref_", "ref"), ("Ring_Stance", "stance"), ("Idle_", "idle"), ("Move_", "move"), ("Run_", "run"), ("Rope_", "rope"), ("Whip_", "whip"),
              ("Center_", "center"), ("Lockup", "lockup"), ("Chain_", "chain"), ("Bump_", "bump"), ("GetUp", "getup"), ("Cover", "pin"), ("Bodyslam", "throw"),
              ("Forearm", "strike"), ("Entrance_", "entrance"), ("Promo_", "promo")]

KIT_BONES = [k for k in CS.BONE_MAP]     # без Root


def load_npz(path):
    d = np.load(path)
    names = [str(x) for x in d["names"]]
    parents = d["parents"].tolist()
    rest = d["rest"].astype(np.float64)
    clips = {k[5:]: d[k] for k in d.files if k.startswith("clip/")}
    return names, parents, rest, clips


def order_children_first(names, parents):
    """Порядок: батько раніше дитини, Root викинуто."""
    out = []
    idx = {n: i for i, n in enumerate(names)}
    def add(n):
        if n in out or n == "Root" or n == "root":
            return
        p = parents[idx[n]]
        if p >= 0 and names[p] not in ("Root", "root"):
            add(names[p])
        out.append(n)
    for n in names:
        add(n)
    return out


def make_skeleton(names, parents, rest):
    order = [n for n in order_children_first(names, parents) if n in CS.BONE_MAP]
    idx = {n: i for i, n in enumerate(names)}
    par = []
    for n in order:
        p = parents[idx[n]]
        pn = names[p] if p >= 0 else None
        par.append(order.index(pn) if pn in order else -1)
    return Skeleton(order, par, np.stack([rest[idx[n]] for n in order])), [idx[n] for n in order]


def world_tracks(frames, ids):
    f = frames[:, ids].astype(np.float64)
    return q_continuous(q_from_mat(f)), f[:, :, :3, 3]


def retarget_same_topology(frames, src_names, src_rest, sk):
    """Той самий скелет набору з іншою позою спокою: передаємо світову зміну обертання відносно спокою."""
    idx = {n: i for i, n in enumerate(src_names)}
    ids = [idx[n] for n in sk.names]
    wq, wp = world_tracks(frames, ids)
    srq = q_from_mat(src_rest[ids])
    delta = q_mul(q_inv(srq), sk.rest_q)
    wq2 = q_mul(wq, delta)
    ratio = sk.rest_p[0, 2] / max(src_rest[ids[0], 2, 3], 1e-6)
    return wq2, wp[:, 0] * ratio


def ual_rev(sk, ual_names):
    """Наша кістка -> кістка UAL: UAL1 має імена DEF-*, UAL2 — майже як набір, лише Head з великої."""
    if "DEF-hips" in ual_names:
        return {v: k for k, v in CS.UAL_MAP.items()}
    return {n: ("Head" if n == "head" else n) for n in sk.names}


def ual_reference(sk, ual_names, ual_rest, tpose_frame):
    """Опорна пара поз: UAL A_TPose і поза нашого скелета, де кожна кістка дивиться туди ж, куди в UAL."""
    rev = ual_rev(sk, ual_names)
    idx = {n: i for i, n in enumerate(ual_names)}
    uq = q_from_mat(tpose_frame.astype(np.float64))
    up = tpose_frame[:, :3, 3].astype(np.float64)
    J = len(sk.names)
    wq = np.zeros((J, 4))
    src_q = np.zeros((J, 4))
    for j, n in enumerate(sk.names):
        u = idx[rev[n]]
        src_q[j] = uq[u]
        p = sk.parents[j]
        base = sk.rest_local_q[j] if p < 0 else None
        cur = sk.rest_q[j] if p < 0 else q_mul(wq[p], sk.rest_local_q[j])
        # дитина для напрямку кістки
        kids = [k for k in range(J) if sk.parents[k] == j]
        kid = None
        for k in kids:
            nm = sk.names[k]
            if nm.startswith(("spine", "neck", "head", "lowerarm", "hand", "calf", "foot", "ball", "upperarm")) or nm.endswith(("_02_l", "_03_l", "_02_r", "_03_r")):
                kid = k
                break
        if kid is None and kids:
            kid = kids[0]
        if kid is not None and n not in ("pelvis",):
            u_kid = idx[rev[sk.names[kid]]]
            dir_src = up[u_kid] - up[u]
            ax = bone_axis_local(sk, j, kid)
            cur = aim(cur[None], ax[None], dir_src[None])[0]
        wq[j] = cur
    delta = q_mul(q_inv(src_q), wq)     # W_tgt = W_src * delta
    return delta


def retarget_ual(frames, ual_names, ual_rest, sk, delta):
    rev = ual_rev(sk, ual_names)
    idx = {n: i for i, n in enumerate(ual_names)}
    ids = [idx[rev[n]] for n in sk.names]
    f = frames[:, ids].astype(np.float64)
    wq = q_continuous(q_from_mat(f))
    wq2 = q_mul(wq, delta)
    ratio = sk.rest_p[0, 2] / max(ual_rest[idx[rev["pelvis"]], 2, 3], 1e-6)
    return wq2, f[:, 0, :3, 3] * ratio


def ground_clip(sk, wq, hips, lq=None):
    """Знімає плавання стоп після перенесення: мінімум висоти стопи має збігатись зі спокоєм."""
    wp = world_recompute(sk, wq, hips)
    foot = min(wp[:, sk.i("foot_l"), 2].min(), wp[:, sk.i("foot_r"), 2].min())
    rest_foot = min(sk.rest_p[sk.i("foot_l"), 2], sk.rest_p[sk.i("foot_r"), 2])
    h = hips.copy()
    h[:, 2] += rest_foot - foot
    return h


def yaw_of(q_pelvis, rest_q):
    """Рискання таза відносно спокою: куди дивиться ліво-права вісь таза в плані."""
    rel = q_mul(q_pelvis, q_inv(rest_q))
    x = q_rot(rel, np.array([1.0, 0, 0]))
    return np.unwrap(np.arctan2(x[:, 1], x[:, 0]))


def lowpass(x, frames):
    if frames < 2:
        return x.copy()
    k = np.hanning(frames + 2)[1:-1]
    k /= k.sum()
    pad = frames
    if x.ndim == 1:
        y = np.pad(x, pad, mode="edge")
        return np.convolve(y, k, mode="same")[pad:-pad]
    return np.stack([lowpass(x[:, i], frames) for i in range(x.shape[1])], axis=1)


def extract_root(sk, wq, hips, loop, name):
    """Повертає (in-place wq, in-place hips, root (F,3): x, y, yaw). Світова поза: Root(t) * pose(t) = вихідна поза."""
    F = len(hips)
    p = hips[:, :2]
    yaw = yaw_of(wq[:, 0], sk.rest_q[0])
    net = p[-1] - p[0]
    net_yaw = yaw[-1] - yaw[0]
    root = np.zeros((F, 3))
    if loop:
        ts = np.linspace(0.0, 1.0, F)[:, None]
        root[:, :2] = net[None] * ts
        root[:, 2] = net_yaw * ts[:, 0] if abs(net_yaw) > np.radians(4) else 0.0
    else:
        if np.linalg.norm(net) > 0.02 or np.ptp(p, axis=0).max() > 0.12:
            win = max(3, int(0.22 * FPS))
            s = lowpass(p - p[0], win)
            # точні кінці: нуль на старті, повний зсув наприкінці
            s = s - s[0]
            s = s + (net - s[-1])[None] * np.linspace(0, 1, F)[:, None]
            root[:, :2] = s
        if abs(net_yaw) > np.radians(10):
            s = lowpass(yaw - yaw[0], max(3, int(0.22 * FPS)))
            s = s - s[0]
            s = s + (net_yaw - s[-1]) * np.linspace(0, 1, F)
            root[:, 2] = s
    # Знімаємо корінь із пози
    rz = q_axis_angle(np.array([0, 0, 1.0]), -root[:, 2])
    wq2 = wq.copy()
    for j in range(wq.shape[1]):
        wq2[:, j] = q_mul(rz, wq[:, j])
    d = np.concatenate([p - root[:, :2], np.zeros((F, 1))], axis=1)
    d = d - np.array([p[0, 0], p[0, 1], 0.0])
    d = q_rot(rz, d) + np.array([p[0, 0], p[0, 1], 0.0])
    h2 = hips.copy()
    h2[:, :2] = d[:, :2]
    return wq2, h2, root


def close_loop(lq, hips, frames=8):
    """Перший кадр = останній: похибку шва розподіляємо на останні кадри."""
    F = lq.shape[0]
    n = min(frames, F // 2)
    if n < 2:
        return lq, hips
    w = smoothstep(np.linspace(0, 1, n + 1)[1:])
    lq2 = lq.copy()
    h2 = hips.copy()
    for k, wt in enumerate(w):
        f = F - n + k
        lq2[f] = q_slerp(lq[f], lq[0], np.full(lq.shape[1:2] + (1,), wt))
        h2[f] = hips[f] + (hips[0] - hips[-1]) * wt
    lq2[-1] = lq[0]
    h2[-1] = hips[0]
    return lq2, h2


def foot_stance(sk, wq, hips):
    wp = world_recompute(sk, wq, hips)
    return wp[:, sk.i("foot_l")], wp[:, sk.i("foot_r")]


def phase_align(sk, lq, hips, root):
    """Циклічно зсуває цикл ходьби/бігу так, щоб кадр 0 був приземленням лівої стопи. Повертає нові масиви, фази стоп і швидкість."""
    wq, _ = sk.fk(lq, hips)
    fl, fr = foot_stance(sk, wq, hips)
    F = lq.shape[0]
    n = F - 1
    if n < 8:
        return lq, hips, root, None
    def plant(z):
        z = z[:n]
        lo = z.min()
        hi = z.max()
        if hi - lo < 0.015:
            return None
        th = lo + 0.25 * (hi - lo)
        down = z < th
        for i in range(n):
            if down[i] and not down[i - 1]:
                return i
        return int(np.argmin(z))
    pl = plant(fl[:, 2])
    pr = plant(fr[:, 2])
    if pl is None or pr is None:
        return lq, hips, root, None
    def roll(a):
        return np.concatenate([a[pl:n], a[:pl], a[pl:pl + 1]], axis=0)
    # стопа в опорі: швидкість назад у просторі in-place кліпу
    lq2, h2 = roll(lq), roll(hips)
    wq2, _ = sk.fk(lq2, h2)
    fl2, fr2 = foot_stance(sk, wq2, h2)
    dt = 1.0 / FPS
    v = (fl2[1:] - fl2[:-1]) / dt
    zmin = fl2[:, 2].min()
    # опора: найдовший суцільний відрізок, де стопа на маті; швидкість — зсув стопи назад (+Y) за цей відрізок
    speed_foot = 0.0
    for tol in (0.008, 0.014, 0.022, 0.035):
        low = fl2[:, 2] < zmin + tol
        best = (0, 0); i = 0
        while i < len(low):
            if low[i]:
                j = i
                while j + 1 < len(low) and low[j + 1]:
                    j += 1
                if j - i > best[1] - best[0]:
                    best = (i, j)
                i = j + 1
            else:
                i += 1
        if best[1] - best[0] >= 4:
            a0, a1 = best
            speed_foot = float((fl2[a1, 1] - fl2[a0, 1]) / ((a1 - a0) * dt))
            break
    if speed_foot <= 0.0:
        # у спринті на мат стає лише носок: той самий вимір за кісткою носка
        ball = world_recompute(sk, wq2, h2)[:, sk.i("ball_l")]
        zb = ball[:, 2].min()
        low = ball[:, 2] < zb + 0.012
        best = (0, 0); i = 0
        while i < len(low):
            if low[i]:
                j = i
                while j + 1 < len(low) and low[j + 1]:
                    j += 1
                if j - i > best[1] - best[0]:
                    best = (i, j)
                i = j + 1
            else:
                i += 1
        if best[1] - best[0] >= 2:
            speed_foot = float((ball[best[1], 1] - ball[best[0], 1]) / ((best[1] - best[0]) * dt))
    # права стопа: найнижча точка відносно лівої
    pmin_l = int(np.argmin(fl[:n, 2])); pmin_r = int(np.argmin(fr[:n, 2]))
    phase_r = ((pmin_r - pmin_l) % n) / float(n)
    return lq2, h2, root, dict(phase_left=0.0, phase_right=round(phase_r, 3), stance_foot_speed=round(speed_foot, 3))


def read_manifest(path):
    out = {}
    if not os.path.exists(path):
        return out
    for line in open(path, encoding="utf-8"):
        if line.startswith("#") or not line.strip():
            continue
        t = line.split()
        out[t[0]] = dict(role=t[1], length=float(t[2]), loop=t[3] == "1", offset=[float(t[4]), float(t[5]), float(t[6])], rot=float(t[7]),
                         node_from=None if t[8] == "-" else t[8], node_to=None if t[9] == "-" else t[9])
    return out


def read_events(path):
    out = {}
    if not os.path.exists(path):
        return out
    for line in open(path, encoding="utf-8"):
        if line.startswith("#") or not line.strip():
            continue
        t = line.split()
        out.setdefault(t[0], []).append(dict(event=t[1], t=float(t[2]), a=t[3] if len(t) > 3 else None, b=t[4] if len(t) > 4 else None))
    return out


def category(name):
    for pre, c in CAT_PREFIX:
        if name.startswith(pre):
            return c
    return "misc"


def build_gender(cache, sex, outdir):
    sfx = "_F" if sex == "f" else ""
    names, parents, rest, moves = load_npz(os.path.join(cache, "RING_Moves%s.npz" % sfx))
    sk, ids = make_skeleton(names, parents, rest)
    print("skeleton", sex, len(sk.names), "hips", sk.rest_p[0].round(3), flush=True)
    low = "_f" if sex == "f" else ""
    man = read_manifest(os.path.join(ANIM, "ring_anims%s_manifest.txt" % low))
    man.update(read_manifest(os.path.join(ANIM, "ring_entrance%s_manifest.txt" % low)))
    man.update(read_manifest(os.path.join(ANIM, "ring_promo%s_manifest.txt" % low)))
    events = read_events(os.path.join(ANIM, "ring_anims%s_events.txt" % low))

    raw = []   # (ім'я, світові q, таз, джерело, категорія)
    for src, file in (("RING_Moves", "RING_Moves%s" % sfx), ("RING_Entrance", "RING_Entrance%s" % sfx), ("RING_Promo", "RING_Promo%s" % sfx)):
        n2, p2, r2, clips = load_npz(os.path.join(cache, file + ".npz"))
        idx = {n: i for i, n in enumerate(n2)}
        same = np.abs(r2[[idx[x] for x in sk.names]] - sk.rest).max() < 1e-3
        for c, fr in clips.items():
            if same:
                wq, wp = world_tracks(fr, [idx[x] for x in sk.names])
                raw.append((c, wq, wp[:, 0], src, category(c)))
            else:
                wq, h = retarget_same_topology(fr, n2, r2, sk)
                raw.append((c, wq, h, src, category(c)))
    # Зброя (набір) і UAL
    n2, p2, r2, clips = load_npz(os.path.join(cache, "ALPHA_Weapons.npz"))
    for c, cat in WEAPON_KEEP.items():
        wq, h = retarget_same_topology(clips[c], n2, r2, sk)
        raw.append(("W_" + c, wq, h, "ALPHA_Weapons", cat))
    for file in ("UAL1_Standard", "UAL2_Standard"):
        n2, p2, r2, clips = load_npz(os.path.join(cache, file + ".npz"))
        delta = ual_reference(sk, n2, r2, clips["A_TPose"][0])
        for c, fr in clips.items():
            if c in UAL_KEEP:
                wq, h = retarget_ual(fr, n2, r2, sk, delta)
                h = ground_clip(sk, wq, h)
                raw.append(("U_" + c, wq, h, "UAL (Quaternius, CC0)", UAL_KEEP[c]))

    lib = {}
    meta = {}
    TWIST_JOINTS = [sk.i(n) for n in ("hand_l", "hand_r", "foot_l", "foot_r")]
    CHAINS = [Chain(sk, "upperarm_" + sd, "lowerarm_" + sd, "hand_" + sd) for sd in "lr"] + [Chain(sk, "thigh_" + sd, "calf_" + sd, "foot_" + sd) for sd in "lr"]
    TOE_JOINTS = [sk.i("ball_l"), sk.i("ball_r")]
    TWIST_PAIRS = []
    for sd in "lr":
        TWIST_PAIRS += [(sk.i("upperarm_" + sd), sk.i("lowerarm_" + sd)), (sk.i("lowerarm_" + sd), sk.i("hand_" + sd)), (sk.i("hand_" + sd), sk.i("middle_01_" + sd)),
                        (sk.i("thigh_" + sd), sk.i("calf_" + sd)), (sk.i("calf_" + sd), sk.i("foot_" + sd))]
    despiked_total = [0]
    for name, wq, hips, src, cat in raw:
        info = man.get(name, {})
        base = name[:-2] if name.endswith(("_A", "_B")) else name
        loop = bool(info.get("loop")) or name.endswith("_Loop")
        wq2, h2, root = extract_root(sk, wq, hips, loop, name)
        wq3 = fix_elbows(sk, wq2, h2, CHAINS)
        wp2 = world_recompute(sk, wq3, h2)
        wq3 = untwist(sk, wq3, wp2, TWIST_PAIRS, loop=loop)
        wq3 = fix_steps(wq3, TWIST_JOINTS + TOE_JOINTS)
        if np.abs(wq3 - wq2).max() > 1e-4:
            despiked_total[0] += 1
        lq = sk.to_local(wq3)
        lq = q_continuous(lq)
        ph = None
        if loop:
            lq, h2 = close_loop(lq, h2)
            if any(k in name for k in ("Walk", "Run", "Jog", "Sprint", "Crouch_Fwd", "Backpedal", "Circle")):
                lq, h2, root, ph = phase_align(sk, lq, h2, root)
                if ph:
                    lq, belt, frep = clean_feet(sk, lq, h2, root)
                    ph["feet"] = frep
                    if belt:
                        ph["stance_foot_speed"] = round(belt, 3)
        F = lq.shape[0]
        dur = (F - 1) / FPS
        netd = root[-1, :2]
        m = dict(name=name, frames=int(F), duration=round(dur, 4), loop=loop, source=src, category=cat, status="ready",
                 role=info.get("role") if info.get("role") in ("A", "B") else None, base=base if info.get("role") in ("A", "B") else None,
                 offset=info.get("offset"), rot=info.get("rot"), node_from=info.get("node_from"), node_to=info.get("node_to"),
                 net=[round(float(netd[0]), 4), round(float(netd[1]), 4), round(float(root[-1, 2]), 4)],
                 speed=round(float(np.linalg.norm(netd)) / dur, 3) if dur > 0 else 0.0)
        if ph:
            m["phase"] = ph
            if m["speed"] < 0.05 and ph["stance_foot_speed"] > 0.2:
                m["speed"] = ph["stance_foot_speed"]
                m["speed_from"] = "стопа в опорі"
        ev = events.get(base) or events.get(name)
        if ev:
            m["events"] = ev
        if name.startswith("Promo_"):
            m["prop"] = "mic"
        lib[name] = (lq, h2, root)
        meta[name] = m
    print("base clips", len(lib), "зі згладженими сплесками", despiked_total[0], flush=True)

    # Власні кліпи й пози поверх кліпів
    AUTH.add_all(sk, lib, meta, sex, FPS)
    print("with authored", len(lib), flush=True)

    # Запис
    arrays = {"names": np.array([CS.PREFIX + CS.BONE_MAP[n] for n in sk.names]), "parents": sk.parents,
              "rest": sk.rest.astype(np.float32), "kit_names": np.array(sk.names)}
    for name, (lq, h, root) in lib.items():
        arrays["q/" + name] = lq.astype(np.float32)
        arrays["h/" + name] = h.astype(np.float32)
        arrays["r/" + name] = root.astype(np.float32)
    np.savez_compressed(os.path.join(outdir, "clips_%s.npz" % sex), **arrays)
    json.dump(meta, open(os.path.join(outdir, "clips_%s.json" % sex), "w", encoding="utf-8"), ensure_ascii=False, indent=0)
    print("written", sex, flush=True)
    return meta


if __name__ == "__main__":
    cache, outdir = sys.argv[1], sys.argv[2]
    os.makedirs(outdir, exist_ok=True)
    which = sys.argv[3:] or ["m", "f"]
    for s in which:
        build_gender(cache, s, outdir)
