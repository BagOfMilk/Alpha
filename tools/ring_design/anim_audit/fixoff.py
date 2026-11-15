"""Виправлений зсув пари (маніфест, простір A): куди поставити B, щоб ударна частина A торкнулася цілі B на мітці contact
(проміжок −2 см). Рахує по горизонталі (z зсуву завжди 0), ітераціями з перевіркою; далі весь кліп до удару — чи не
проходять тіла одне крізь одне, і чи удар приходить спереду (кут «B лицем»).
python -I -X utf8 fixoff.py <npz> <manifest> <events> [--target -0.02] [--search] [--write <копія маніфесту>]"""
import sys
import numpy as np

npz, manifest, events = sys.argv[1:4]
args = sys.argv[4:]
TARGET = float(args[args.index("--target") + 1]) if "--target" in args else -0.02
WRITE = args[args.index("--write") + 1] if "--write" in args else None   # копія маніфесту з новими зсувами (лише для огляду)
NEW = {}
W = 0.12
D = np.load(npz, allow_pickle=True)
BONES = [str(b) for b in D["bones"]]
BI = {b: i for i, b in enumerate(BONES)}
FPS = float(D["fps"])
MAN, EV = {}, {}
for line in open(manifest, encoding="utf-8"):
    if line.strip() and not line.startswith("#"):
        f = line.split()
        MAN[f[0]] = dict(off=np.array([float(f[4]), float(f[5]), float(f[6])]), yaw=float(f[7]))
for line in open(events, encoding="utf-8"):
    if line.strip() and not line.startswith("#"):
        f = line.split()
        if len(f) >= 3 and f[1] == "contact":
            EV.setdefault(f[0], []).append(float(f[2]))

SH = ["upperarm_l", "upperarm_r"]
ARM = ["lowerarm_l", "lowerarm_r", "upperarm_l", "upperarm_r"]
CHEST = ["spine_02", "spine_03"]
NECK = ["spine_03", "neck_01", "head"]
# Ударна частина A → ціль B (чесний дотик прийому, а не будь-яка частина тіла)
STRIKE = {
    "Center_ShoulderBlock": (SH, CHEST), "Center_ShoulderBlock_Heavy": (SH, CHEST), "Center_ShoulderTackle": (SH, CHEST),
    "Center_RunningElbow": (["lowerarm_l", "lowerarm_r"], NECK),
    "Center_Clothesline": (ARM, NECK), "Center_Lariat_Left": (ARM, NECK), "Center_Lariat_Heavy": (ARM, NECK),
    "Center_RunningKnee": (["thigh_l", "thigh_r", "calf_l", "calf_r"], ["spine_01", "spine_02", "spine_03"]),
    "Center_Headbutt": (["head"], ["head"]), "Center_ShoulderBlock_NoSell": (SH, CHEST),
    "Center_Clothesline_Flip": (ARM, NECK), "Counter_Clothesline_Flip": (ARM, NECK),
    "Charge_ShoulderBlock": (SH, CHEST), "Charge_ShoulderBlock_Heavy": (SH, CHEST), "Charge_ShoulderBlock_NoSell": (SH, CHEST),
    "Charge_ShoulderTackle": (SH, CHEST), "Counter_ShoulderBlock": (SH, CHEST),
    "Counter_Clothesline": (ARM, NECK), "Counter_Lariat_Left": (ARM, NECK), "Counter_BigBoot": (["foot_r", "calf_r"], CHEST),
}
# gap генератора (ring_anims_center.py CENTER, ring_anims_combat.py COUNTERS / Chg) — від таза A до таза B уздовж Y
# у мить удару; зсув B = (bx, K − gap), тож новий gap = старий gap + y_старий − y_новий, новий bx = x_новий.
GAP = {"Center_ShoulderBlock": .42, "Center_Clothesline": .5, "Center_Clothesline_Flip": .5, "Center_Lariat_Left": .5,
       "Center_RunningElbow": .5, "Center_RunningKnee": .85, "Center_ShoulderBlock_NoSell": .75, "Center_Headbutt": .7,
       "Center_ShoulderBlock_Heavy": .5, "Center_Lariat_Heavy": .5, "Center_ShoulderTackle": .42,
       "Counter_Clothesline": .47, "Counter_Clothesline_Flip": .45, "Counter_ShoulderBlock": .38, "Counter_BigBoot": .98,
       "Counter_Lariat_Left": .49, "Charge_ShoulderBlock": .4, "Charge_ShoulderBlock_Heavy": .42,
       "Charge_ShoulderTackle": .4, "Charge_ShoulderBlock_NoSell": .72}
RAD = {"pelvis": .12, "spine_01": .12, "spine_02": .12, "spine_03": .11, "neck_01": .05, "head": .09}
for sd in "lr":
    RAD.update({"upperarm_" + sd: .05, "lowerarm_" + sd: .04, "hand_" + sd: .035, "thigh_" + sd: .075, "calf_" + sd: .055, "foot_" + sd: .04})
CHILD = {"pelvis": "spine_01", "spine_01": "spine_02", "spine_02": "spine_03", "spine_03": "neck_01", "neck_01": "head"}
for sd in "lr":
    CHILD.update({"upperarm_" + sd: "lowerarm_" + sd, "lowerarm_" + sd: "hand_" + sd, "thigh_" + sd: "calf_" + sd,
                  "calf_" + sd: "foot_" + sd, "foot_" + sd: "ball_" + sd})


def seg(p, b):
    if b == "head":
        h, n = p[:, BI["head"]], p[:, BI["neck_01"]]
        d = h - n
        return h, h + d / np.maximum(np.linalg.norm(d, axis=1, keepdims=True), 1e-6) * 0.16
    return p[:, BI[b]], p[:, BI[CHILD[b]]]


def closest(a0, a1, b0, b1):
    d1, d2, r = a1 - a0, b1 - b0, a0 - b0
    a = (d1 * d1).sum(1); e = (d2 * d2).sum(1); f = (d2 * r).sum(1); c = (d1 * r).sum(1); b = (d1 * d2).sum(1)
    den = a * e - b * b
    s = np.where(den > 1e-9, np.clip((b * f - c * e) / np.where(den > 1e-9, den, 1), 0, 1), 0.0)
    t = np.where(e > 1e-9, (b * s + f) / np.where(e > 1e-9, e, 1), 0.0)
    t0 = np.clip(t, 0, 1)
    s = np.where(t < 0, np.clip(-c / np.where(a > 1e-9, a, 1), 0, 1), np.where(t > 1, np.clip((b - c) / np.where(a > 1e-9, a, 1), 0, 1), s))
    pa = a0 + d1 * s[:, None]
    pb = b0 + d2 * t0[:, None]
    return pa, pb


def Rz(deg):
    c, s = np.cos(np.radians(deg)), np.sin(np.radians(deg))
    return np.array([[c, -s, 0], [s, c, 0], [0, 0, 1]])


def measure(pa, pbl, off, yaw, ab, bb, f0, f1):
    pb = pbl @ Rz(yaw).T + off
    best = (9.0, None, None, "", 0)
    for x in ab:
        A0, A1 = seg(pa, x)
        for y in bb:
            B0, B1 = seg(pb, y)
            qa, qb = closest(A0[f0:f1 + 1], A1[f0:f1 + 1], B0[f0:f1 + 1], B1[f0:f1 + 1])
            g = np.linalg.norm(qa - qb, axis=1) - RAD[x] - RAD[y]
            i = int(np.argmin(g))
            if g[i] < best[0]:
                best = (float(g[i]), qa[i], qb[i], x + "→" + y, f0 + i)
    return best


ALL = ["pelvis", "spine_01", "spine_02", "spine_03", "neck_01", "head"] + [b + "_" + sd for sd in "lr" for b in
                                                                          ("upperarm", "lowerarm", "thigh", "calf", "foot")]
TORSO = ["pelvis", "spine_01", "spine_02", "spine_03"]
SEARCH = "--search" in args   # шукати ще й поворот B навколо точки удару (зсув + поворот), щоб розбіг не йшов крізь B


def prehit(pa, pbl, off, yaw, fc):
    """До удару (кадри 0…fc−3): найглибший проміжок між будь-якими частинами, хто з ким, мить; і скільки кадрів < −3 см."""
    pb = pbl @ Rz(yaw).T + off
    e = max(1, fc - 2)
    gmin = np.full(e, 9.0)
    gname = [""] * e
    for x in ALL:
        A0, A1 = seg(pa[:e], x)
        for y in ALL:
            B0, B1 = seg(pb[:e], y)
            qa, qb = closest(A0, A1, B0, B1)
            g = np.linalg.norm(qa - qb, axis=1) - RAD[x] - RAD[y]
            m = g < gmin
            gmin = np.where(m, g, gmin)
            for i in np.nonzero(m)[0]:
                gname[i] = x + "→" + y
    k = int(np.argmin(gmin))
    return float(gmin[k]), gname[k], k, int((gmin < -0.03).sum())


def facing(pa, pbl, off, yaw, fc, sp):
    """Кут між «уперед» B (носки стоп) і напрямком від цілі B до ударної точки A у мить удару, °."""
    pb = pbl[fc] @ Rz(yaw).T + off
    fw = (pb[BI["ball_l"]] + pb[BI["ball_r"]]) / 2 - (pb[BI["foot_l"]] + pb[BI["foot_r"]]) / 2
    d = sp[0] - sp[1]
    fw, d = fw[:2], d[:2]
    if np.linalg.norm(fw) < 1e-6 or np.linalg.norm(d) < 1e-6:
        return 0.0
    c = np.dot(fw, d) / np.linalg.norm(fw) / np.linalg.norm(d)
    return float(np.degrees(np.arccos(np.clip(c, -1, 1))))


def solve(pa, pbl, off, yaw, ab, bb, f0, f1):
    g = measure(pa, pbl, off, yaw, ab, bb, f0, f1)
    for _ in range(5):
        if abs(g[0] - TARGET) < 0.005:
            return off, g, ""
        u = g[2] - g[1]
        uh = np.array([u[0], u[1], 0.0])
        nu = np.linalg.norm(u)
        if nu < 1e-6 or np.linalg.norm(uh) / nu < 0.35:
            return off, g, " (промах по висоті)"
        uh /= np.linalg.norm(uh)
        off = off - uh * (g[0] - TARGET) / max(np.linalg.norm(u[:2]) / nu, 0.35)
        g = measure(pa, pbl, off, yaw, ab, bb, f0, f1)
    return off, g, ""


print("| кліп | bx було → стало | gap було → стало | поворот B | дотик було → стало | частини @ мить | B лицем, ° | до удару: найглибше, кадрів < −3 см |")
print("|---|---|---|---|---|---|---|---|")
for base, (ab, bb) in STRIKE.items():
    a, b = "pos/" + base + "_A", "pos/" + base + "_B"
    if a not in D or b not in D or base + "_B" not in MAN or base not in EV:
        continue
    pa, pbl = D[a], D[b]
    n = min(len(pa), len(pbl))
    pa, pbl = pa[:n], pbl[:n]
    t = EV[base][0]
    # Дотик — рівно в задану мить удару (мітка ±0,04 с): B починає бамп раніше за удар (lead у генераторі), тож
    # пік його прискорення — це приземлення, а не удар; мітка = t_hit генератора.
    f0, f1 = max(0, int(round((t - 0.04) * FPS))), min(n - 1, int(round((t + 0.04) * FPS)))
    m = MAN[base + "_B"]
    off0, yaw0 = m["off"].copy(), m["yaw"]
    g0 = measure(pa, pbl, off0, yaw0, ab, bb, max(0, int(round((t - W) * FPS))), min(n - 1, int(round((t + W) * FPS))))
    best = None
    for dy in (range(-60, 61, 5) if SEARCH else [0]):
        yaw = yaw0 + dy
        # поворот розміщення B навколо вертикалі через ударну точку A (мить старого дотику) — зберігає взаємне положення
        piv = g0[1]
        o = (off0 - piv) @ Rz(dy).T + piv
        o[2] = off0[2]
        o, g, note = solve(pa, pbl, o, yaw, ab, bb, f0, f1)
        if abs(g[0] - TARGET) > 0.01:
            continue
        fc = g[4]
        ph = prehit(pa, pbl, o, yaw, fc)
        fa = facing(pa, pbl, o, yaw, fc, (g[1], g[2]))
        ok = fa <= 50
        key = (ok, round(min(ph[0], 0.0), 2), -abs(dy))
        if best is None or key > best[0]:
            best = (key, dy, o, yaw, g, ph, fa, note)
    if best is None:
        print("| %s | %.2f | %.2f | %.0f | %+.0f см | розв'язку немає (рука/плече не дістає цілі ні за якого зсуву) |  |  |" % (
            base, off0[0], GAP.get(base, float("nan")), yaw0, g0[0] * 100))
        continue
    _, dy, o, yaw, g, ph, fa, note = best
    NEW[base + "_B"] = (o, yaw)
    g_old = GAP.get(base, float("nan"))
    print("| %s | %.2f → %.2f | %.2f → %.2f | %.0f° → %.0f° | %+.0f → %+.0f см | %s @ %.2f с | %.0f | %+.0f см %s @ %.2f с, %d |%s" % (
        base, off0[0], o[0], g_old, g_old + off0[1] - o[1], yaw0, yaw, g0[0] * 100, g[0] * 100, g[3], g[4] / FPS, fa,
        ph[0] * 100, ph[1], ph[2] / FPS, ph[3], note))

if WRITE:
    with open(WRITE, "w", encoding="utf-8", newline="\n") as w:
        for line in open(manifest, encoding="utf-8"):
            f = line.split()
            if f and not line.startswith("#") and f[0] in NEW:
                o, yw = NEW[f[0]]
                f[4:8] = ["%.3f" % o[0], "%.3f" % o[1], "%.3f" % o[2], "%.1f" % yw]
                line = " ".join(f) + "\n"
            w.write(line)
