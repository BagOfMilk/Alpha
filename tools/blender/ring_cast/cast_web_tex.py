# Текстури станів пошкоджень для сторінки: стисле WEBP поруч із GLB.
#   python cast_web_tex.py <текстури> <дані сторінки> <id>
import os, sys
from PIL import Image

TEX, WEB, CID = sys.argv[1:4]
for lvl in (1, 2):
    for kind in ("albedo", "orm"):
        src = os.path.join(TEX, "%s_body_%s_d%d.png" % (CID, kind, lvl))
        Image.open(src).convert("RGB").save(os.path.join(WEB, "%s_%s_d%d.webp" % (CID, kind, lvl)), quality=86, method=4)
print("ok", CID)
