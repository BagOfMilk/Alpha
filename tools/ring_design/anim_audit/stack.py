"""Склеїти аркуші по N у стовпчик: python -I stack.py <src_dir> <out_dir> <n> <clip1,clip2,...>"""
import os
import sys
from PIL import Image

src, out, n, clips = sys.argv[1], sys.argv[2], int(sys.argv[3]), sys.argv[4].split(",")
os.makedirs(out, exist_ok=True)
for k in range(0, len(clips), n):
    ims = [Image.open(os.path.join(src, c + ".png")) for c in clips[k:k + n] if os.path.exists(os.path.join(src, c + ".png"))]
    if not ims:
        continue
    w = max(i.width for i in ims)
    h = sum(i.height for i in ims)
    st = Image.new("RGB", (w, h), (255, 255, 255))
    y = 0
    for i in ims:
        st.paste(i, (0, y))
        y += i.height
    st.save(os.path.join(out, "stack_%02d.png" % (k // n)))
    print("stack_%02d" % (k // n), clips[k:k + n])
