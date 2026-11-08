# Перетин тіл у парних кліпах: капсули таза-тулуба, голови, стегон і гомілок двох борців, розташованих так, як їх ставить сторінка й гра.
#   python tools/blender/ring_cast/pair_check.py <тека кліпів> [вихід.json]
# Показує найгірші кліпи окремо: «кінець» (останні 10 кадрів, борець уже лежить) і «рух» (решта). Легке перекриття в захопленнях очікуване.
import json, os, sys
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from clips_lib import *

CLIPS = sys.argv[1]
OUT = sys.argv[2] if len(sys.argv) > 2 else None
CAPS = {"tulub": ("pelvis", "neck_01", 0.125), "golova": ("head", "head", 0.105),
        "stehno_l": ("thigh_l", "calf_l", 0.075), "stehno_r": ("thigh_r", "calf_r", 0.075),
        "homilka_l": ("calf_l", "foot_l", 0.05), "homilka_r": ("calf_r", "foot_r", 0.05)}


def seg_dist(p1, q1, p2, q2):
    """Найменша відстань між двома відрізками (масиви F,3)."""
    d1 = q1 - p1; d2 = q2 - p2; r = p1 - p2
    a = np.sum(d1 * d1, -1); e = np.sum(d2 * d2, -1); f = np.sum(d2 * r, -1); c = np.sum(d1 * r, -1); b = np.sum(d1 * d2, -1)
    den = np.maximum(a * e - b * b, 1e-9)
    s = np.clip((b * f - c * e) / den, 0, 1)
    t = np.where(e > 1e-9, (b * s + f) / np.maximum(e, 1e-9), 0.0)
    t = np.clip(t, 0, 1)
    s = np.where(a > 1e-9, np.clip((b * t - c) / np.maximum(a, 1e-9), 0, 1), 0.0)
    return np.linalg.norm((p1 + d1 * s[..., None]) - (p2 + d2 * t[..., None]), axis=-1)


report = {}
for sex in ("m", "f"):
    d = np.load(os.path.join(CLIPS, "clips_%s.npz" % sex))
    meta = json.load(open(os.path.join(CLIPS, "clips_%s.json" % sex), encoding="utf-8"))
    names = [str(x) for x in d["kit_names"]]
    sk = Skeleton(names, d["parents"], d["rest"])

    def world(c, x0, y0, yaw0):
        q = d["q/" + c].astype(np.float64); h = d["h/" + c].astype(np.float64); r = d["r/" + c].astype(np.float64)
        F = q.shape[0]
        wq, wp = sk.fk(q, h)
        rz = q_axis_angle(np.array([0, 0, 1.0]), r[:, 2] + yaw0)
        rb = q_axis_angle(np.array([0, 0, 1.0]), np.full(F, yaw0))
        root = q_rot(rb, np.concatenate([r[:, :2], np.zeros((F, 1))], axis=1)) + np.array([x0, y0, 0.0])
        W = np.zeros_like(wp)
        for j in range(wp.shape[1]):
            W[:, j] = q_rot(rz, wp[:, j]) + root
        return W, r

    rows = []
    for c, m in meta.items():
        if m.get("role") != "A":
            continue
        base = m["base"]
        cb = base + "_B"
        if cb not in meta:
            continue
        off = m.get("offset") or [0, 0, 0]
        rot = np.radians(m.get("rot") or 0.0)
        Wa, ra = world(c, 0, 0, 0)
        Wb, rb = world(cb, off[0], off[1], rot)
        F = min(len(Wa), len(Wb))
        worst_end = (0.0, "", 0); worst_move = (0.0, "", 0)
        for na, (a0, a1, ra_) in CAPS.items():
            for nb, (b0, b1, rb_) in CAPS.items():
                A0 = Wa[:F, sk.i(a0)]; A1 = Wa[:F, sk.i(a1)]; B0 = Wb[:F, sk.i(b0)]; B1 = Wb[:F, sk.i(b1)]
                dist = seg_dist(A0, A1, B0, B1)
                pen = (ra_ + rb_) - dist
                tail = max(F - 10, 1)
                i = int(np.argmax(pen[tail:])) + tail
                if pen[i] > worst_end[0]:
                    worst_end = (float(pen[i]), na + "/" + nb, i)
                j = int(np.argmax(pen[:tail]))
                if pen[j] > worst_move[0]:
                    worst_move = (float(pen[j]), na + "/" + nb, j)
        rows.append(dict(clip=base, F=F, end=round(worst_end[0], 3), end_at=worst_end[1], end_f=worst_end[2], move=round(worst_move[0], 3), move_at=worst_move[1], move_f=worst_move[2],
                         down_b=round(float(Wb[F - 1, 0, 2]), 2), down_a=round(float(Wa[F - 1, 0, 2]), 2), net_a=[round(float(x), 2) for x in ra[-1, :2]], net_b=[round(float(x), 2) for x in Wb[F - 1, 0, :2]]))
    report[sex] = rows
    print("==", sex, "пар", len(rows))
    print(" кінець кліпа, перетин ≥ 4 см:", [(x["clip"], x["end"], x["end_at"]) for x in sorted(rows, key=lambda x: -x["end"]) if x["end"] >= 0.04])
    print(" під час руху, перетин ≥ 12 см:", [(x["clip"], x["move"], x["move_at"], x["move_f"]) for x in sorted(rows, key=lambda x: -x["move"]) if x["move"] >= 0.12][:40])
if OUT:
    json.dump(report, open(OUT, "w", encoding="utf-8"), ensure_ascii=False)
