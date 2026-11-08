# Автоматична перевірка бібліотеки кліпів: шов циклу, стрибки (телепорт), стопи під матом, ковзання стоп у локомоції.
#   python tools/blender/ring_cast/clips_qa.py <тека кліпів> [вихід.json]
# Показує найгірші кліпи. Числа — у масштабі скелета кліпів (зріст ~1,75 / ~1,55 м).
import json, os, sys
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from clips_lib import *

CLIPS = sys.argv[1]
OUT = sys.argv[2] if len(sys.argv) > 2 else None
FPS = 30.0
report = {}
for sex in ("m", "f"):
    d = np.load(os.path.join(CLIPS, "clips_%s.npz" % sex))
    meta = json.load(open(os.path.join(CLIPS, "clips_%s.json" % sex), encoding="utf-8"))
    names = [str(x) for x in d["kit_names"]]
    sk = Skeleton(names, d["parents"], d["rest"])
    rows = []
    for c, m in meta.items():
        q = d["q/" + c].astype(np.float64); h = d["h/" + c].astype(np.float64); r = d["r/" + c].astype(np.float64)
        F = q.shape[0]
        wq, wp = sk.fk(q, h)
        # світ: додаємо корінь
        rz = q_axis_angle(np.array([0, 0, 1.0]), r[:, 2])
        W = np.zeros_like(wp)
        for j in range(wp.shape[1]):
            W[:, j] = q_rot(rz, wp[:, j]) + np.concatenate([r[:, :2], np.zeros((F, 1))], axis=1)
        v = np.linalg.norm(W[1:] - W[:-1], axis=-1) * FPS if F > 1 else np.zeros((1, wp.shape[1]))
        hands = [sk.i("hand_l"), sk.i("hand_r")]
        feet = [sk.i("foot_l"), sk.i("foot_r")]
        hand_speed = float(v[:, hands].max())
        # ривок: зміна швидкості кисті за кадр (телепорт дає піку на одному кадрі)
        if F > 3:
            acc = np.linalg.norm(W[2:] - 2 * W[1:-1] + W[:-2], axis=-1) * FPS * FPS
            jerk_hand = float(acc[:, hands + feet].max())
        else:
            jerk_hand = 0.0
        ang = 0.0
        ang_j = ""
        if F > 1:
            big = [sk.i(n) for n in names if not n.startswith(("thumb", "index", "middle", "ring", "pinky"))]
            # світове обертання: саме воно тягне шкіру; локальні обміни скручуванням між сусідніми кістками шкіри не ламають
            dq = np.abs(np.sum(wq[1:][:, big] * wq[:-1][:, big], axis=-1))
            A = np.degrees(2 * np.arccos(np.minimum(dq, 1.0)))
            i = np.unravel_index(np.argmax(A), A.shape)
            ang = float(A[i])
            ang_j = names[big[i[1]]] + "@" + str(int(i[0]))
        seam = float(np.max(np.linalg.norm(wp[0] - wp[-1], axis=-1))) if m["loop"] else 0.0
        floor = float(wp[:, feet, 2].min() - min(sk.rest_p[feet[0], 2], sk.rest_p[feet[1], 2]))
        # ковзання: в опорі стопа нерухома у світі. Кліп із кореневим рухом: корінь уже в W, очікується 0; кліп на місці: стрічка йде зі швидкістю кліпа, очікується вона.
        slip_p90 = slip_ratio = 0.0
        spd = float(m.get("speed") or 0.0)
        driven = False
        if m["loop"] and spd > 0.2 and F > 8:
            dur = (F - 1) / FPS
            driven = float(np.linalg.norm(r[-1, :2] - r[0, :2])) / max(dur, 1e-3) > 0.3 * spd
            errs, feet_v = [], []
            for f in [sk.i("ball_l"), sk.i("ball_r")]:
                z = W[:, f, 2]
                mask = z < z.min() + 0.010
                # найдовший суцільний відрізок опори
                best = (0, 0); st = None
                for i in range(F + 1):
                    if i < F and mask[i]:
                        st = i if st is None else st
                    elif st is not None:
                        if i - st > best[1] - best[0]:
                            best = (st, i)
                        st = None
                a0, a1 = best
                n = a1 - a0
                if n < 4:
                    continue
                a0 += n // 5; a1 -= n // 5
                vel = (W[a0 + 1:a1, f, :2] - W[a0:a1 - 1, f, :2]) * FPS
                if not len(vel):
                    continue
                v = np.linalg.norm(np.median(vel, axis=0))
                feet_v.append(float(v))
                errs.append(abs(float(v) - (0.0 if driven else spd)))
            if errs:
                slip_p90 = float(max(errs))
                slip_ratio = float(np.mean(feet_v) / spd)
        rows.append(dict(clip=c, slip_p90=round(slip_p90, 3), slip_ratio=round(slip_ratio, 2), speed=round(spd, 3), driven=driven, F=F, hand_speed=round(hand_speed, 2), jerk=round(jerk_hand, 1), max_joint_deg_per_frame=round(ang, 1), seam=round(seam, 4), foot_below_rest=round(floor, 3), status=m["status"], joint=ang_j))
    report[sex] = rows
    print("==", sex, len(rows), "кліпів")
    print("  шви циклів: найбільший %.4f м" % max(x["seam"] for x in rows))
    for key, thr, label in (("max_joint_deg_per_frame", 25.0, "обертання суглоба за кадр, °"), ("jerk", 900.0, "прискорення кисті/стопи, м/с²")):
        bad = sorted([x for x in rows if x[key] > thr], key=lambda x: -x[key])
        print("  %s > %s: %d" % (label, thr, len(bad)), [(x["clip"], x[key], x["joint"]) for x in bad[:10]])
    lp = [x for x in rows if x["speed"] > 0.2 and x["slip_ratio"] > 0]
    if lp:
        print("  ковзання стопи в опорі (м/с, найгірша стопа): циклів руху %d; найгірші" % len(lp), [(x["clip"], x["slip_p90"], "швидк " + str(x["speed"]), "корінь" if x["driven"] else "на місці") for x in sorted(lp, key=lambda x: -x["slip_p90"])[:14]])
        print("  медіана %.3f м/с" % float(np.median([x["slip_p90"] for x in lp])))
    low = sorted(rows, key=lambda x: x["foot_below_rest"])[:6]
    print("  стопа нижче спокою, м:", [(x["clip"], x["foot_below_rest"]) for x in low])
if OUT:
    json.dump(report, open(OUT, "w", encoding="utf-8"), ensure_ascii=False)
