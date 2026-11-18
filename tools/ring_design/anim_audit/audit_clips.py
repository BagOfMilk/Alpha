"""Аудит усіх кліпів «Рингу» з запеченого npz (fbxanim.py bake): плавність, ковзання, проникнення, «удар у повітря».
python -I audit_clips.py <male.npz> <manifest.txt> <events.txt> <out_prefix>
Пише <out>.csv (рядок на кліп) і <out>.md (найгірші за кожною міркою)."""
import sys, math, csv, ctypes
import numpy as np

try:
    k = ctypes.windll.kernel32
    k.SetPriorityClass(k.GetCurrentProcess(), 0x40)
except Exception:
    pass

npz, manifest, events, out = sys.argv[1:5]
D = np.load(npz, allow_pickle=True)
BONES = [str(b) for b in D["bones"]]
BI = {b: i for i, b in enumerate(BONES)}
FPS = float(D["fps"])
DT = 1.0 / FPS

MAN = {}
for line in open(manifest, encoding="utf-8"):
    if not line.strip() or line.startswith("#"):
        continue
    f = line.split()
    MAN[f[0]] = dict(role=f[1], L=float(f[2]), loop=f[3] == "1", off=np.array([float(f[4]), float(f[5]), float(f[6])]),
                     yaw=float(f[7]), n0=f[8], n1=f[9])
EV = {}
for line in open(events, encoding="utf-8"):
    if not line.strip() or line.startswith("#"):
        continue
    f = line.split()
    if len(f) < 3:
        continue
    EV.setdefault(f[0], []).append((f[1], float(f[2]), f[3] if len(f) > 3 else "-", f[4] if len(f) > 4 else "-"))


def P(c):
    return D["pos/" + c]


# ---------------------------------------------------------------- капсули
def segs_of(p):
    """p [F, B, 3] → список (ім'я, a [F,3], b [F,3], r)."""
    g = lambda n: p[:, BI[n]]
    head = g("head")
    top = head + (head - g("neck_01")) / np.maximum(np.linalg.norm(head - g("neck_01"), axis=1, keepdims=True), 1e-6) * 0.16
    s = [("pelvis", g("pelvis"), g("spine_01"), 0.12), ("spine_01", g("spine_01"), g("spine_02"), 0.12),
         ("spine_02", g("spine_02"), g("spine_03"), 0.12), ("spine_03", g("spine_03"), g("neck_01"), 0.11),
         ("neck_01", g("neck_01"), head, 0.05), ("head", head, top, 0.09)]
    for sd in "lr":
        s += [("upperarm_" + sd, g("upperarm_" + sd), g("lowerarm_" + sd), 0.05),
              ("lowerarm_" + sd, g("lowerarm_" + sd), g("hand_" + sd), 0.04),
              ("hand_" + sd, g("hand_" + sd), g("middle_02_" + sd), 0.035),
              ("thigh_" + sd, g("thigh_" + sd), g("calf_" + sd), 0.075),
              ("calf_" + sd, g("calf_" + sd), g("foot_" + sd), 0.055),
              ("foot_" + sd, g("foot_" + sd), g("ball_" + sd), 0.04)]
    return s


def segdist(a0, a1, b0, b1):
    """Найменша відстань між відрізками, векторно по кадрах."""
    d1, d2, r = a1 - a0, b1 - b0, a0 - b0
    a = np.einsum("ij,ij->i", d1, d1)
    e = np.einsum("ij,ij->i", d2, d2)
    f = np.einsum("ij,ij->i", d2, r)
    c = np.einsum("ij,ij->i", d1, r)
    b = np.einsum("ij,ij->i", d1, d2)
    den = a * e - b * b
    s = np.where(den > 1e-9, np.clip((b * f - c * e) / np.where(den > 1e-9, den, 1), 0, 1), 0.0)
    t = np.where(e > 1e-9, (b * s + f) / np.where(e > 1e-9, e, 1), 0.0)
    t0 = np.clip(t, 0, 1)
    s = np.where(t < 0, np.clip(-c / np.where(a > 1e-9, a, 1), 0, 1), np.where(t > 1, np.clip((b - c) / np.where(a > 1e-9, a, 1), 0, 1), s))
    t = t0
    pa = a0 + d1 * s[:, None]
    pb = b0 + d2 * t[:, None]
    return np.linalg.norm(pa - pb, axis=1)


ADJ = {("pelvis", "spine_01"), ("spine_01", "spine_02"), ("spine_02", "spine_03"), ("spine_03", "neck_01"), ("neck_01", "head")}
for sd in "lr":
    ADJ |= {("upperarm_" + sd, "lowerarm_" + sd), ("lowerarm_" + sd, "hand_" + sd), ("thigh_" + sd, "calf_" + sd),
            ("calf_" + sd, "foot_" + sd), ("spine_03", "upperarm_" + sd), ("pelvis", "thigh_" + sd), ("spine_01", "thigh_" + sd)}
SELF_PAIRS = []
TORSO = ["pelvis", "spine_01", "spine_02", "spine_03", "head"]
for sd in "lr":
    for t in TORSO:
        SELF_PAIRS.append(("lowerarm_" + sd, t))
        SELF_PAIRS.append(("hand_" + sd, t))
SELF_PAIRS += [("thigh_l", "thigh_r"), ("calf_l", "calf_r"), ("thigh_l", "calf_r"), ("thigh_r", "calf_l"),
               ("foot_l", "foot_r"), ("lowerarm_l", "lowerarm_r"), ("upperarm_l", "spine_01"), ("upperarm_r", "spine_01")]


def self_pen(p):
    S = {n: (a, b, r) for n, a, b, r in segs_of(p)}
    best = (0.0, "", 0)
    for x, y in SELF_PAIRS:
        a0, a1, ra = S[x]
        b0, b1, rb = S[y]
        d = segdist(a0, a1, b0, b1)
        dep = (ra + rb) - d
        i = int(np.argmax(dep))
        if dep[i] > best[0]:
            best = (float(dep[i]), x + "×" + y, i)
    return best


def pair_pen(pa, pb):
    SA, SB = segs_of(pa), segs_of(pb)
    best = (0.0, "", 0)
    for n1, a0, a1, r1 in SA:
        for n2, b0, b1, r2 in SB:
            d = segdist(a0, a1, b0, b1)
            dep = (r1 + r2) - d
            i = int(np.argmax(dep))
            if dep[i] > best[0]:
                best = (float(dep[i]), n1 + "×" + n2, i)
    return best


def seg_gap(pa, pb, boneA, boneB, f):
    SA = {n: (a, b, r) for n, a, b, r in segs_of(pa[f:f + 1])}
    SB = {n: (a, b, r) for n, a, b, r in segs_of(pb[f:f + 1])}
    if boneA not in SA or boneB not in SB:
        return None
    a0, a1, r1 = SA[boneA]
    b0, b1, r2 = SB[boneB]
    return float(segdist(a0, a1, b0, b1)[0] - r1 - r2)


def Rz(deg):
    c, s = math.cos(math.radians(deg)), math.sin(math.radians(deg))
    return np.array([[c, -s, 0], [s, c, 0], [0, 0, 1]])


# ---------------------------------------------------------------- мірки однієї постаті
KEY = ["pelvis", "spine_03", "head", "hand_l", "hand_r", "lowerarm_l", "lowerarm_r", "foot_l", "foot_r", "calf_l", "calf_r"]


def solo(p, name, ev_times):
    F = p.shape[0]
    r = {}
    pel = p[:, BI["pelvis"]]
    step = np.linalg.norm(np.diff(pel[:, :2], axis=0), axis=1)
    tp = 0.0
    for i in range(len(step)):
        nb = np.concatenate([step[max(0, i - 3):i], step[i + 1:i + 4]])
        if len(nb) and step[i] > 0.1 and step[i] > 1.8 * np.median(nb):
            tp = max(tp, float(step[i] - np.median(nb)))
    r["teleport_m"] = tp
    # ривки: прискорення з одиничним піком (поп)
    worst = (0.0, "", 0.0)
    pops = 0
    if F >= 5:
        for kb in KEY:
            x = p[:, BI[kb]]
            a = np.linalg.norm(x[2:] - 2 * x[1:-1] + x[:-2], axis=1) * FPS * FPS
            for i in range(len(a)):
                lo, hi = max(0, i - 5), min(len(a), i + 6)
                med = np.median(np.delete(a[lo:hi], i - lo)) if hi - lo > 1 else 0.0
                t = (i + 1) * DT
                near_ev = any(abs(t - te) < 0.12 for te in ev_times)
                if a[i] > 40.0 and a[i] > 5.0 * max(med, 1.0) and not near_ev:
                    pops += 1
                    if a[i] > worst[0]:
                        worst = (float(a[i]), kb, t)
    r["pops"] = pops
    r["pop_worst"] = worst[0]
    r["pop_bone"] = worst[1]
    r["pop_t"] = worst[2]
    # ковзання опорної стопи
    slide = (0.0, "", 0.0)
    for sd in "lr":
        ball = p[:, BI["ball_" + sd]]
        ft = p[:, BI["foot_" + sd]]
        gnd = (ball[:, 2] < 0.035) & (ft[:, 2] < 0.11)
        i = 0
        while i < F:
            if gnd[i]:
                j = i
                while j + 1 < F and gnd[j + 1]:
                    j += 1
                if (j - i + 1) * DT >= 0.2:
                    d = np.linalg.norm(ball[i:j + 1, :2] - ball[i, :2], axis=1).max()
                    if d > slide[0]:
                        slide = (float(d), sd, i * DT)
                i = j + 1
            else:
                i += 1
    r["slide_m"], r["slide_side"], r["slide_t"] = slide
    # під мат
    mins = {"hand": min(p[:, BI["hand_l"], 2].min(), p[:, BI["hand_r"], 2].min()) - 0.025,
            "head": p[:, BI["head"], 2].min() - 0.085, "pelvis": p[:, BI["pelvis"], 2].min() - 0.085,
            "knee": min(p[:, BI["calf_l"], 2].min(), p[:, BI["calf_r"], 2].min()) - 0.045,
            "ball": min(p[:, BI["ball_l"], 2].min(), p[:, BI["ball_r"], 2].min()) + 0.005}
    k = min(mins, key=mins.get)
    r["under_mat_m"] = float(-mins[k]) if mins[k] < 0 else 0.0
    r["under_mat_part"] = k if mins[k] < 0 else ""
    # самопроникнення
    dep, pr, i = self_pen(p)
    r["self_pen_m"], r["self_pen_pair"], r["self_pen_t"] = dep, pr, i * DT
    # мертві паузи (нічого не рухається)
    sp = np.linalg.norm(np.diff(p[:, [BI[b] for b in KEY]], axis=0), axis=2).max(axis=1) * FPS
    still = sp < 0.03
    run = best_run = 0
    for s_ in still:
        run = run + 1 if s_ else 0
        best_run = max(best_run, run)
    r["freeze_s"] = best_run * DT
    # жвавість: середня швидкість кінцівок (м/с)
    r["energy"] = float(np.linalg.norm(np.diff(p[:, [BI[b] for b in KEY]], axis=0), axis=2).mean() * FPS) if F > 1 else 0.0
    # петля: стик
    if MAN.get(name, {}).get("loop"):
        rel0 = (p[0] - p[0, BI["pelvis"]])[1:]
        rel1 = (p[-1] - p[-1, BI["pelvis"]])[1:]
        r["loop_seam_m"] = float(np.linalg.norm(rel0 - rel1, axis=1).max())
        v0 = p[1] - p[0]
        v1 = p[-1] - p[-2]
        r["loop_vel_jump"] = float(np.linalg.norm((v0 - v1)[[BI[b] for b in KEY]], axis=1).max() * FPS)
    else:
        r["loop_seam_m"] = r["loop_vel_jump"] = 0.0
    # зависання в повітрі (без опори) — для одиночного кліпа
    low = p[:, :, 2].min(axis=1)
    air = low > 0.12
    run = best = 0
    for s_ in air:
        run = run + 1 if s_ else 0
        best = max(best, run)
    r["air_s"] = best * DT
    return r


# ---------------------------------------------------------------- прогін
rows = []
clips = [str(c) for c in D["clips"]]
for c in clips:
    p = P(c)
    m = MAN.get(c, {})
    base = c[:-2] if c.endswith(("_A", "_B")) and m.get("role") in ("A", "B") else c
    ev = EV.get(base, []) + EV.get(c, [])
    r = dict(clip=c, role=m.get("role", "?"), L=m.get("L", 0), loop=int(m.get("loop", False)), n0=m.get("n0", ""), n1=m.get("n1", ""))
    r.update(solo(p, c, [e[1] for e in ev]))
    r["pair_pen_m"] = 0.0
    r["pair_pen_pair"] = ""
    r["pair_pen_t"] = 0.0
    r["air_free_s"] = r["air_s"]
    r["contact_gaps"] = ""
    if m.get("role") == "A" and base + "_B" in MAN and ("pos/" + base + "_B") in D:
        mb = MAN[base + "_B"]
        pb = P(base + "_B") @ Rz(mb["yaw"]).T + mb["off"]
        pa = p
        n = min(len(pa), len(pb))
        pa, pb = pa[:n], pb[:n]
        dep, pr, i = pair_pen(pa, pb)
        r["pair_pen_m"], r["pair_pen_pair"], r["pair_pen_t"] = dep, pr, i * DT
        gaps = []
        for e, t, ba, bb in ev:
            f = min(n - 1, int(round(t * FPS)))
            if ba != "-" and bb != "-" and not bb.startswith("rope"):
                g = seg_gap(pa, pb, ba, bb, f)
                if g is not None:
                    gaps.append("%s@%.2f %s→%s %+.0fсм" % (e, t, ba, bb, g * 100))
        r["contact_gaps"] = "; ".join(gaps)
        # обидва в повітрі без опори одне на одному
        SA, SB = segs_of(pa), segs_of(pb)
        lowA, lowB = pa[:, :, 2].min(axis=1), pb[:, :, 2].min(axis=1)
        dmin = np.full(n, 9.0)
        for _, a0, a1, r1 in SA:
            for _, b0, b1, r2 in SB:
                dmin = np.minimum(dmin, segdist(a0, a1, b0, b1) - r1 - r2)
        freeA = (lowA > 0.12) & (dmin > 0.04)
        run = best = 0
        for s_ in freeA:
            run = run + 1 if s_ else 0
            best = max(best, run)
        r["air_free_s"] = best * DT
        r["pair_min_gap_m"] = float(dmin.min())
    rows.append(r)

keys = sorted({k for r in rows for k in r}, key=lambda k: (k != "clip", k))
with open(out + ".csv", "w", newline="", encoding="utf-8") as f:
    w = csv.DictWriter(f, fieldnames=keys)
    w.writeheader()
    for r in rows:
        w.writerow(r)


def top(key, n=25, thr=0.0, fmt="%.3f", extra=()):
    rs = sorted([r for r in rows if r.get(key, 0) and r[key] > thr], key=lambda r: -r[key])
    lines = ["", "## %s (> %s): %d кліпів" % (key, thr, len(rs))]
    for r in rs[:n]:
        lines.append("- %-38s %s  %s" % (r["clip"], fmt % r[key], "  ".join(str(r.get(x, "")) for x in extra)))
    return lines


md = ["# Аудит кліпів: %s (%d кліпів)" % (npz, len(rows))]
md += top("teleport_m", thr=0.05, extra=())
md += top("pops", thr=0, fmt="%d", extra=("pop_worst", "pop_bone", "pop_t"))
md += top("slide_m", thr=0.02, extra=("slide_side", "slide_t"))
md += top("under_mat_m", thr=0.01, extra=("under_mat_part",))
md += top("self_pen_m", thr=0.03, extra=("self_pen_pair", "self_pen_t"))
md += top("pair_pen_m", thr=0.03, extra=("pair_pen_pair", "pair_pen_t"))
md += top("air_free_s", thr=0.25, fmt="%.2f", extra=("role",))
md += top("freeze_s", thr=0.4, fmt="%.2f", extra=("loop",))
md += top("loop_seam_m", thr=0.01, extra=("loop_vel_jump",))
md += top("loop_vel_jump", thr=0.3, fmt="%.2f", extra=("loop_seam_m",))
gaps = [r for r in rows if r.get("contact_gaps")]
md += ["", "## Відстань у мітках контакту (кістка A → кістка B, поверхня капсул; + — не дістає)"]
for r in gaps:
    md.append("- %-34s %s" % (r["clip"], r["contact_gaps"]))
lo = sorted([r for r in rows if r.get("loop")], key=lambda r: r["energy"])
md += ["", "## Жвавість петель (середня швидкість кінцівок, м/с; менше — «манекен»)"]
for r in lo:
    md.append("- %-34s %.3f" % (r["clip"], r["energy"]))
open(out + ".md", "w", encoding="utf-8").write("\n".join(md))
print("ok", len(rows))
