# Сторінки контактних аркушів для огляду всіх кліпів: один кліп — один рядок кадрів.
#   python sheet_pages.py <тека кадрів> <префікс> <тека виходу> [рядків на сторінку] [ширина кадру]
# Імена кадрів: <префікс><номер кліпа>_<кліп>__<номер кадру>.jpg
import glob, os, re, sys
from PIL import Image, ImageDraw

d, pre, out = sys.argv[1:4]
rows = int(sys.argv[4]) if len(sys.argv) > 4 else 8
w = int(sys.argv[5]) if len(sys.argv) > 5 else 240
os.makedirs(out, exist_ok=True)
clips = {}
for f in sorted(glob.glob(os.path.join(d, pre + "*.jpg"))):
    m = re.match(r"(\d+)_(.+)__(\d+)\.jpg$", os.path.basename(f)[len(pre):])
    if m:
        clips.setdefault((int(m.group(1)), m.group(2)), []).append(f)
keys = sorted(clips)
if not keys:
    sys.exit("немає кадрів")
cols = max(len(v) for v in clips.values())
im0 = Image.open(clips[keys[0]][0])
h = int(w * im0.height / im0.width)
index = []
for p in range(0, len(keys), rows):
    page = keys[p:p + rows]
    S = Image.new("RGB", (cols * w, len(page) * h), (18, 18, 22))
    D = ImageDraw.Draw(S)
    for r, k in enumerate(page):
        for c, f in enumerate(clips[k]):
            S.paste(Image.open(f).convert("RGB").resize((w, h)), (c * w, r * h))
        D.rectangle((0, r * h, 8 + 7 * len(k[1]), r * h + 14), fill=(0, 0, 0))
        D.text((3, r * h + 2), k[1], fill=(255, 220, 120))
    name = "%s%03d.jpg" % (pre, p // rows)
    S.save(os.path.join(out, name), quality=88)
    index.append((name, [k[1] for k in page]))
with open(os.path.join(out, pre + "index.txt"), "w", encoding="utf-8") as f:
    for name, names in index:
        f.write(name + ": " + ", ".join(names) + "\n")
print("сторінок", len(index), "кліпів", len(keys), "кадрів у рядку", cols)
