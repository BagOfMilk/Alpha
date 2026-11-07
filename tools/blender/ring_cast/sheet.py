# Контактний аркуш із кадрів для перевірки очима: python sheet.py <тека> <префікс> <вихід.jpg> [стовпчики] [ширина]
import glob, os, sys
from PIL import Image, ImageDraw

d, pre, out = sys.argv[1:4]
cols = int(sys.argv[4]) if len(sys.argv) > 4 else 8
w = int(sys.argv[5]) if len(sys.argv) > 5 else 240
fs = sorted(glob.glob(os.path.join(d, pre + "*.jpg")))
if not fs:
    sys.exit("немає кадрів")
im0 = Image.open(fs[0])
h = int(w * im0.height / im0.width)
rows = (len(fs) + cols - 1) // cols
S = Image.new("RGB", (cols * w, rows * h), (18, 18, 22))
D = ImageDraw.Draw(S)
for i, f in enumerate(fs):
    x, y = (i % cols) * w, (i // cols) * h
    S.paste(Image.open(f).convert("RGB").resize((w, h)), (x, y))
    D.text((x + 3, y + 3), os.path.basename(f)[len(pre):-4][:38], fill=(255, 220, 120))
S.save(out, quality=90)
print(out, len(fs), S.size)
