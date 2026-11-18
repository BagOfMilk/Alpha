"""Дотик тіл біля міток пари: найменший проміжок між БУДЬ-ЯКИМИ капсулами A і B у вікні ±W с від мітки
(незалежно від того, які кістки записані в мітці). + — тіла не торкаються; − — заходять одне в одне.
python -I touch.py <npz> <manifest> <events> [--w 0.12] [--only Pref1,Pref2]"""
import sys
import numpy as np

npz, manifest, events = sys.argv[1:4]
args = sys.argv[4:]
W = float(args[args.index("--w") + 1]) if "--w" in args else 0.12
ONLY = args[args.index("--only") + 1].split(",") if "--only" in args else None
D = np.load(npz, allow_pickle=True)
BONES = [str(b) for b in D["bones"]]
BI = {b: i for i, b in enumerate(BONES)}
FPS = float(D["fps"])
MAN = {}
for line in open(manifest, encoding="utf-8"):
    if line.strip() and not line.startswith("#"):
        f = line.split()
        MAN[f[0]] = dict(role=f[1], off=np.array([float(f[4]), float(f[5]), float(f[6])]), yaw=float(f[7]))
EV = {}
for line in open(events, encoding="utf-8"):
    if line.strip() and not line.startswith("#"):
        f = line.split()
        if len(f) >= 3:
            EV.setdefault(f[0], []).append((f[1], float(f[2])))


def Rz(deg):
    c, s = np.cos(np.radians(deg)), np.sin(np.radians(deg))
    return np.array([[c, -s, 0], [s, c, 0], [0, 0, 1]])


def segs_of(p):
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
    return np.linalg.norm(a0 + d1 * s[:, None] - (b0 + d2 * t[:, None]), axis=1)


rows = []
for c in [str(x) for x in D["clips"]]:
    if not c.endswith("_A") or MAN.get(c, {}).get("role") != "A":
        continue
    base = c[:-2]
    if ONLY and not any(base.startswith(o) for o in ONLY):
        continue
    if base + "_B" not in MAN or ("pos/" + base + "_B") not in D:
        continue
    ev = [e for e in EV.get(base, []) + EV.get(c, []) if e[0] in ("contact", "grab", "sell", "hit", "impact", "catch")]
    if not ev:
        continue
    mb = MAN[base + "_B"]
    pa = D["pos/" + c]
    pb = D["pos/" + base + "_B"] @ Rz(mb["yaw"]).T + mb["off"]
    n = min(len(pa), len(pb))
    pa, pb = pa[:n], pb[:n]
    SA, SB = segs_of(pa), segs_of(pb)
    gap = np.full(n, 9.0)
    who = np.empty(n, dtype=object)
    for na, a0, a1, r1 in SA:
        for nb, b0, b1, r2 in SB:
            g = segdist(a0, a1, b0, b1) - r1 - r2
            m = g < gap
            gap = np.where(m, g, gap)
            who[m] = na + "→" + nb
    out = []
    for e, t in ev:
        f0, f1 = max(0, int(round((t - W) * FPS))), min(n - 1, int(round((t + W) * FPS)))
        i = f0 + int(np.argmin(gap[f0:f1 + 1]))
        out.append((e, t, gap[i], i / FPS, who[i]))
    worst = max(o[2] for o in out)
    rows.append((worst, c, out))

rows.sort(key=lambda r: -r[0])
for worst, c, out in rows:
    tag = "НЕ ТОРКАЄ" if worst > 0.03 else ("ок" if worst > -0.06 else "ГЛИБОКО")
    print("%-34s %-9s " % (c, tag) + "; ".join("%s@%.2f %+.0fсм (%.2fс %s)" % (e, t, g * 100, ti, w) for e, t, g, ti, w in out))
