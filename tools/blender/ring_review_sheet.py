# Листи кадрів огляду (tools/blender/ring_review.py): Logs/review<_f>/<кліп>/<ракурс>_<кадр>.png → <кліп>.png
# (рядок ракурсу 0 — збоку, 1 — спереду; номер кадру від початку кліпу).
import sys, os, glob, re
from PIL import Image, ImageDraw
root = sys.argv[1]
for d in sorted(glob.glob(os.path.join(root, "*", ""))):
    fs = sorted(glob.glob(os.path.join(d, "*.png")))
    if not fs:
        continue
    by = {}
    for f in fs:
        a, k = re.findall(r"(\d)_(\d+)\.png$", f)[0]
        by.setdefault(int(k), {})[int(a)] = f
    ks = sorted(by)
    cols = min(len(ks), 12)
    rows = (len(ks) + 11) // 12
    S = Image.new("RGB", (240 * cols, 480 * rows), "white")
    for i, k in enumerate(ks):
        for a, f in by[k].items():
            x, y = (i % 12) * 240, ((i // 12) * 2 + a) * 240
            S.paste(Image.open(f), (x, y))
            ImageDraw.Draw(S).text((x + 3, y + 3), str(k), fill=(200, 0, 0))
    S.save(d.rstrip(os.sep + "/") + ".png")
    print(os.path.basename(d.rstrip(os.sep + "/")), len(ks))
