"""Звірка розміщення пари: де стоїть таз A і B на кадрі 0 у даних npz (без зсуву маніфесту)."""
import sys
import numpy as np

D = np.load(sys.argv[1], allow_pickle=True)
BONES = [str(b) for b in D["bones"]]
P = BONES.index("pelvis")
H = BONES.index("head")
for base in sys.argv[2].split(","):
    for s in "AB":
        k = "pos/%s_%s" % (base, s)
        if k not in D:
            print(k, "немає")
            continue
        p = D[k]
        f0, fe = p[0], p[-1]
        print("%-34s таз0=(%.2f %.2f %.2f) голова0=(%.2f %.2f %.2f)  таз_кінець=(%.2f %.2f %.2f)" % (
            k[4:], *f0[P], *f0[H], *fe[P]))
