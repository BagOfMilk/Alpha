"""Перегляд очима: розкадрування ролика, контактні аркуші «гімік × роль × щабель», анімовані GIF.

    cd tools && python -m ring_design.titantron.preview film <гімік> <роль> <щабель> [варіант] [запис] [вихід.png]
    cd tools && python -m ring_design.titantron.preview sheet <щабель> [варіант] [вихід.png]
    cd tools && python -m ring_design.titantron.preview gif <гімік> <роль> <щабель> [варіант] [запис] [вихід.gif]
"""
import json
import sys

from PIL import Image, ImageDraw, ImageFont

from . import compose, render
from .common import KITS, ROOT, W, H, slot_map, utf8

UI = json.loads((ROOT / "Assets/Resources/Ring/ui_uk.json").read_text(encoding="utf-8"))
REC = {r["id"]: r for r in KITS["sampleRecipes"]}


def texts_for(rec):
    path_key = {"hero": "path.face", "villain": "path.heel", "rebel": "path.rebel"}[rec["role"]]
    tag = "%s · %s" % (UI["gimmick." + rec["gimmick"]], UI[path_key])
    return {"nick": rec["nick"], "full": rec["full"], "tag": tag}


def default_rec(gimmick, role):
    for r in KITS["sampleRecipes"]:
        if r["gimmick"] == gimmick and r["role"] == role:
            return r
    base = next(r for r in KITS["sampleRecipes"] if r["gimmick"] == gimmick)
    return dict(base, role=role)


def frame(tl, rec, tier, beat, size):
    slots = slot_map(tl["role"], tier, rec.get("player", False))
    b = render.quantize(beat, tl["bpm"], tl["fps"])
    return render.render(tl, b, texts_for(rec), slots, tier, size)


def _label_font(px):
    return ImageFont.truetype(str(ROOT / KITS["fonts"]["text"]), px)


def film_beats(tl):
    H_ = tl["hitBeat"]
    return [0.0, H_ - 1.0, H_ - 0.5, H_ - 0.15, H_, H_ + 0.12, H_ + 0.3, H_ + 0.6, H_ + 1.0, H_ + 1.6, H_ + 2.6, H_ + 6.1]


def film(tl, rec, tier, out, cols=4, size=(512, 256)):
    beats = film_beats(tl)
    rows = (len(beats) + cols - 1) // cols
    pad = 6
    cv = Image.new("RGB", (cols * (size[0] + pad) + pad, rows * (size[1] + pad + 18) + pad), (40, 40, 48))
    d = ImageDraw.Draw(cv)
    f = _label_font(14)
    for i, b in enumerate(beats):
        im = frame(tl, rec, tier, b, size)
        x = pad + (i % cols) * (size[0] + pad)
        y = pad + (i // cols) * (size[1] + pad + 18)
        cv.paste(im, (x, y + 18))
        d.text((x + 2, y + 1), "біт %.2f  (%.2f с)" % (b, b * 60 / tl["bpm"]), font=f, fill=(230, 230, 230))
    cv.save(out)
    return cv


def sheet(tier, variant, out, size=(320, 160), cols=4):
    combos = [(g, r) for g in KITS["gimmicks"] for r in KITS["roles"]]
    pad = 6
    rows = (len(combos) + cols - 1) // cols
    cv = Image.new("RGB", (cols * (size[0] + pad) + pad, rows * (size[1] + pad + 16) + pad), (40, 40, 48))
    d = ImageDraw.Draw(cv)
    f = _label_font(13)
    for i, (g, r) in enumerate(combos):
        rec = default_rec(g, r)
        tl = compose.compose(g, r, tier, variant)
        b = tl["hitBeat"] + 2.6
        im = frame(tl, rec, tier, b, size)
        x = pad + (i % cols) * (size[0] + pad)
        y = pad + (i // cols) * (size[1] + pad + 16)
        cv.paste(im, (x, y + 16))
        d.text((x + 2, y), "%s · %s · %s · v%d" % (g, r, tier, variant), font=f, fill=(230, 230, 230))
    cv.save(out)


def gif(tl, rec, tier, out, size=(512, 256), fps=15, beats_after_hit=8.0):
    total = tl["hitBeat"] + beats_after_hit
    n = int(total * 60 / tl["bpm"] * fps)
    frames = []
    for i in range(n):
        b = i / fps * tl["bpm"] / 60
        frames.append(frame(tl, rec, tier, b, size).quantize(colors=96, method=Image.MEDIANCUT))
    frames[0].save(out, save_all=True, append_images=frames[1:], duration=int(1000 / fps), loop=0, optimize=False, disposal=1)


if __name__ == "__main__":
    utf8()
    cmd = sys.argv[1]
    if cmd == "film":
        g, role, tier = sys.argv[2:5]
        v = int(sys.argv[5]) if len(sys.argv) > 5 else 0
        rec = REC[sys.argv[6]] if len(sys.argv) > 6 else default_rec(g, role)
        rec = dict(rec, gimmick=g, role=role)
        out = sys.argv[7] if len(sys.argv) > 7 else "film.png"
        film(compose.compose(g, role, tier, v), rec, tier, out)
    elif cmd == "sheet":
        sheet(sys.argv[2], int(sys.argv[3]) if len(sys.argv) > 3 else 0, sys.argv[4] if len(sys.argv) > 4 else "sheet.png")
    elif cmd == "gif":
        g, role, tier = sys.argv[2:5]
        v = int(sys.argv[5]) if len(sys.argv) > 5 else 0
        rec = REC[sys.argv[6]] if len(sys.argv) > 6 else default_rec(g, role)
        rec = dict(rec, gimmick=g, role=role)
        gif(compose.compose(g, role, tier, v), rec, tier, sys.argv[7] if len(sys.argv) > 7 else "out.gif")
    print("ok")
