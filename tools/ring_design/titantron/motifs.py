"""Мотиви екрана виходу: білі маски RGBA (альфа = форма), колір задає програвач (tint з токена).

Без слів у текстурах (правило 27.10) і без випадковості: «шум» — детермінований LCG. Запуск:
    python -I -m ring_design.titantron.motifs      (з tools/)  → Assets/Art/Ring/Titantron/sprites/*.png + sprites.json
"""
import json
import math

from PIL import Image, ImageDraw

from .common import ART, SPRITES, utf8

SS = 2  # надвибірка маски: краї гладкі


def _mask(w, h):
    return Image.new("L", (w * SS, h * SS), 0)


def _finish(mask, w, h):
    m = mask.resize((w, h), Image.LANCZOS)
    img = Image.new("RGBA", (w, h), (255, 255, 255, 0))
    img.putalpha(m)
    return img


def _lcg(seed):
    s = seed & 0xFFFFFFFF
    while True:
        s = (1664525 * s + 1013904223) & 0xFFFFFFFF
        yield s / 4294967296.0


def rays(n, frac, size=1024):
    m = _mask(size, size)
    d = ImageDraw.Draw(m)
    c = size * SS / 2
    big = size * SS
    step = 2 * math.pi / n
    for i in range(n):
        a0, a1 = i * step, i * step + step * frac
        d.polygon([(c, c), (c + big * math.cos(a0), c + big * math.sin(a0)),
                   (c + big * math.cos(a1), c + big * math.sin(a1))], fill=255)
    return _finish(m, size, size)


def speed(size=1024, count=84):
    m = _mask(size, size)
    d = ImageDraw.Draw(m)
    r = _lcg(7)
    S = size * SS
    c = S / 2
    for i in range(count):
        a = i * 2 * math.pi / count + (next(r) - .5) * 0.03
        r0 = (0.16 + 0.22 * next(r)) * S
        r1 = S * 0.75
        wd = (0.004 + 0.010 * next(r)) * S
        ux, uy = math.cos(a), math.sin(a)
        px, py = -uy, ux
        d.polygon([(c + ux * r0, c + uy * r0),
                   (c + ux * r1 + px * wd, c + uy * r1 + py * wd),
                   (c + ux * r1 - px * wd, c + uy * r1 - py * wd)], fill=255)
    return _finish(m, size, size)


def crack(size=1024):
    m = _mask(size, size)
    d = ImageDraw.Draw(m)
    r = _lcg(11)
    S = size * SS
    c = S / 2

    def branch(x, y, ang, length, wdt, depth):
        segs = 7 if depth == 0 else 4
        for k in range(segs):
            ang += (next(r) - .5) * 0.9
            step = length / segs * (0.7 + 0.6 * next(r))
            nx, ny = x + math.cos(ang) * step, y + math.sin(ang) * step
            w = max(3, wdt * (1 - k / (segs + 1)))
            d.line([(x, y), (nx, ny)], fill=255, width=int(w))
            d.ellipse([nx - w / 2, ny - w / 2, nx + w / 2, ny + w / 2], fill=255)
            x, y = nx, ny
            if depth < 2 and k in (2, 4) and next(r) < 0.7:
                side = 1 if next(r) < .5 else -1
                branch(x, y, ang + side * (0.5 + next(r) * 0.5), length * 0.45, wdt * 0.55, depth + 1)

    for i in range(8):
        branch(c, c, i * 2 * math.pi / 8 + (next(r) - .5) * 0.4, S * 0.46, S * 0.016, 0)
    return _finish(m, size, size)


BOLTS = {
    "bolt1": [(0.60, 0.00), (0.22, 0.46), (0.46, 0.46), (0.30, 1.00), (0.82, 0.38), (0.56, 0.38), (0.80, 0.00)],
    "bolt2": [(0.56, 0.00), (0.30, 0.34), (0.48, 0.34), (0.26, 0.68), (0.42, 0.68), (0.20, 1.00),
              (0.74, 0.54), (0.56, 0.54), (0.74, 0.30), (0.60, 0.30), (0.80, 0.00)],
}


def bolt(pts, w=512, h=1024):
    m = _mask(w, h)
    ImageDraw.Draw(m).polygon([(x * w * SS, y * h * SS) for x, y in pts], fill=255)
    return _finish(m, w, h)


def flame(w=1024, h=512):
    m = _mask(w, h)
    d = ImageDraw.Draw(m)
    r = _lcg(23)
    n = 7
    for i in range(n):
        cx = (i + 0.5) * w / n * SS + (next(r) - .5) * 30
        height = (0.50 + 0.45 * next(r)) * h * SS
        width = w / n * SS * 1.25
        bend = (next(r) - .5) * 0.5 * width
        left, right = [], []
        steps = 24
        for k in range(steps + 1):
            t = k / steps
            half = width / 2 * (1 - t) ** 0.8 * (1 + 0.15 * math.sin(t * 7))
            off = bend * math.sin(t * math.pi * 0.9) * t
            y = h * SS - t * height
            left.append((cx - half + off, y))
            right.append((cx + half + off, y))
        d.polygon(left + right[::-1], fill=255)
    d.rectangle([0, h * SS - 6 * SS, w * SS, h * SS], fill=255)
    return _finish(m, w, h)


def halftone_r(w=1024, h=512, pitch=26):
    m = _mask(w, h)
    d = ImageDraw.Draw(m)
    for j in range(-1, int(h / (pitch * 0.5)) + 2):
        y = j * pitch * 0.5
        for i in range(-1, int(w / pitch) + 2):
            x = i * pitch + (pitch * 0.5 if j % 2 else 0)
            g = 1.0 - (0.62 * x / w + 0.72 * (1 - y / h))
            g = max(0.0, min(1.0, g * 1.15))
            rad = pitch * 0.5 * g ** 0.8 * 1.05
            if rad > 0.8:
                d.ellipse([(x - rad) * SS, (y - rad) * SS, (x + rad) * SS, (y + rad) * SS], fill=255)
    return _finish(m, w, h)


def halftone_flat(size=256, pitch=32, rad=7):
    m = _mask(size, size)
    d = ImageDraw.Draw(m)
    for j in range(size // (pitch // 2)):
        y = j * pitch // 2
        for i in range(size // pitch):
            x = i * pitch + (pitch // 2 if j % 2 else 0)
            for ox in (-size, 0, size):
                for oy in (-size, 0, size):
                    d.ellipse([(x + ox - rad) * SS, (y + oy - rad) * SS, (x + ox + rad) * SS, (y + oy + rad) * SS], fill=255)
    return _finish(m, size, size)


def grid(size=128, line=3):
    m = _mask(size, size)
    d = ImageDraw.Draw(m)
    d.rectangle([0, 0, size * SS, line * SS - 1], fill=255)
    d.rectangle([0, 0, line * SS - 1, size * SS], fill=255)
    return _finish(m, size, size)


def ticks(w=1024, h=48):
    m = _mask(w, h)
    d = ImageDraw.Draw(m)
    d.rectangle([0, (h - 4) * SS, w * SS, h * SS], fill=255)
    for x in range(0, w, 16):
        t = 40 if x % 256 == 0 else 26 if x % 64 == 0 else 13
        d.rectangle([x * SS, (h - 4 - t) * SS, (x + 3) * SS, (h - 4) * SS], fill=255)
    return _finish(m, w, h)


def bulbs(lit, w=1024, h=96):
    m = _mask(w, h)
    d = ImageDraw.Draw(m)
    for i in range(16):
        cx, cy = (i + 0.5) * 64, 48
        r, v = (20, 255) if i % 2 == lit else (8, 90)
        d.ellipse([(cx - r) * SS, (cy - r) * SS, (cx + r) * SS, (cy + r) * SS], fill=v)
    return _finish(m, w, h)


def cone(w=512, h=1024):
    m = _mask(w, h)
    ImageDraw.Draw(m).polygon([(226 * SS, 0), (286 * SS, 0), (506 * SS, h * SS), (6 * SS, h * SS)], fill=255)
    return _finish(m, w, h)


def torn(w=1024, h=128):
    m = _mask(w, h)
    r = _lcg(31)
    pts = [(0, 0), (w, 0)]
    x = w
    while x > 0:
        pts.append((x, h * (0.55 + 0.4 * next(r))))
        x -= 14 + 22 * next(r)
    pts.append((0, h * 0.8))
    ImageDraw.Draw(m).polygon([(px * SS, py * SS) for px, py in pts], fill=255)
    return _finish(m, w, h)


def chevron(w=170, h=256):
    m = _mask(w, h)
    ImageDraw.Draw(m).polygon([(x * SS, y * SS) for x, y in
                               [(10, 0), (70, 0), (130, 128), (70, 256), (10, 256), (70, 128)]], fill=255)
    return _finish(m, w, h)


def star8(size=512):
    m = _mask(size, size)
    c = size * SS / 2
    pts = []
    for i in range(16):
        rad = (250 if i % 2 == 0 else 95) * SS
        a = -math.pi / 2 + i * math.pi / 8
        pts.append((c + rad * math.cos(a), c + rad * math.sin(a)))
    ImageDraw.Draw(m).polygon(pts, fill=255)
    return _finish(m, size, size)


def scan(size=8):
    img = Image.new("RGBA", (size, size), (255, 255, 255, 0))
    px = img.load()
    for x in range(size):
        px[x, 0] = (255, 255, 255, 255)
        px[x, 1] = (255, 255, 255, 255)
    return img


def curtain(w=256, h=64):
    img = Image.new("RGBA", (w, h), (255, 255, 255, 0))
    px = img.load()
    for x in range(w):
        a = 255 if (x // 32) % 2 == 0 else 160
        for y in range(h):
            px[x, y] = (255, 255, 255, a)
    return img


def bracket(size=128, th=14):
    m = _mask(size, size)
    d = ImageDraw.Draw(m)
    d.rectangle([0, 0, size * SS, th * SS], fill=255)
    d.rectangle([0, 0, th * SS, size * SS], fill=255)
    return _finish(m, size, size)


def confetti(size=256, count=26):
    m = _mask(size, size)
    d = ImageDraw.Draw(m)
    r = _lcg(41)
    for _ in range(count):
        cx, cy = next(r) * size, next(r) * size
        w, h = 7 + 6 * next(r), 14 + 10 * next(r)
        a = next(r) * math.pi
        ca, sa = math.cos(a), math.sin(a)
        for ox in (-size, 0, size):
            for oy in (-size, 0, size):
                pts = [(cx + ox + dx * ca - dy * sa, cy + oy + dx * sa + dy * ca)
                       for dx, dy in ((-w / 2, -h / 2), (w / 2, -h / 2), (w / 2, h / 2), (-w / 2, h / 2))]
                d.polygon([(px * SS, py * SS) for px, py in pts], fill=255)
    return _finish(m, size, size)


# id → (генератор, плитка?)
CATALOG = {
    "rays24": (lambda: rays(24, 0.5), False),
    "rays12w": (lambda: rays(12, 0.5), False),
    "speed": (speed, False),
    "crack": (crack, False),
    "bolt1": (lambda: bolt(BOLTS["bolt1"]), False),
    "bolt2": (lambda: bolt(BOLTS["bolt2"]), False),
    "flame": (flame, False),
    "halftone_r": (halftone_r, False),
    "halftone_flat": (halftone_flat, True),
    "grid": (grid, True),
    "ticks": (ticks, True),
    "bulbs_a": (lambda: bulbs(0), False),
    "bulbs_b": (lambda: bulbs(1), False),
    "cone": (cone, False),
    "torn": (torn, False),
    "chevron": (chevron, True),
    "star8": (star8, False),
    "scan": (scan, True),
    "bracket": (bracket, False),
    "curtain": (curtain, True),
    "confetti": (confetti, True),
}


def build_all():
    SPRITES.mkdir(parents=True, exist_ok=True)
    index = {}
    for sid, (fn, tile) in CATALOG.items():
        img = fn()
        img.save(SPRITES / (sid + ".png"), optimize=True)
        index[sid] = {"w": img.width, "h": img.height, "tile": tile}
    (ART / "sprites.json").write_text(json.dumps(index, ensure_ascii=False, indent=1), encoding="utf-8")
    return index


if __name__ == "__main__":
    utf8()
    idx = build_all()
    print("спрайтів:", len(idx))
    for k, v in idx.items():
        print(" ", k, v["w"], "x", v["h"], "tile" if v["tile"] else "")
