"""Ескізи двох варіантів стилю меню (Налаштування + Завантаження) для вибору власником — запит «Persona 5 + Persona 3 Reload».

Беремо ПРИНЦИПИ стилю, а не асети. Кольори — лише токени `docs/ring/design/brand_tokens.json`, шрифти — лише Статуту.
  А — «енергія»: мова кутів і колажу (діагональний клин, напівтон, плашки під кутом), палітра зсунута в холодне (кобальт).
  Б — «чистий AA + вода»: спокійна сітка й повітря, холодний «водяний» шар (брижі й віддзеркалення кобальтом),
      різкість — лише в акцентах (пластина фокусу, удар заголовка, діагональний перехід).
Запуск (з tools/):  python -m ring_design.menus_mock [вихідна тека]
"""
import math
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

from .title_mock import H, K, W, col, put, scene, trapezoid, txt
from .titantron.common import BRAND, ROOT, SPRITES, TOKENS, utf8

A_GHOST = TOKENS["alpha"]["tronGhost"]
A_DIM = TOKENS["alpha"]["tronDim"]
A_PANEL = TOKENS["alpha"]["panel"]
S = TOKENS["type"]["sizes"]
SLANT = TOKENS["space"]["slantDeg"] if "slantDeg" in TOKENS.get("space", {}) else 12


def px(v):
    return int(round(v * K))


def layer(fill, a):
    im = Image.new("RGBA", (W, H), col(fill) + (0,))
    return im, a


def poly(img, pts, fill, a=1.0):
    lay = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    ImageDraw.Draw(lay).polygon(pts, fill=col(fill) + (int(255 * a),))
    img.alpha_composite(lay)


def rect(img, box, fill, a=1.0):
    lay = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    ImageDraw.Draw(lay).rectangle(box, fill=col(fill) + (int(255 * a),))
    img.alpha_composite(lay)


def tinted(path, rgb, a, size=None):
    im = Image.open(path).convert("RGBA")
    if size:
        im = im.resize(size, Image.LANCZOS)
    m = im.getchannel("A").point(lambda v: int(v * a))
    out = Image.new("RGBA", im.size, rgb + (255,))
    out.putalpha(m)
    return out


def ink_bg(dark=0.55):
    img = scene()
    rect(img, (0, 0, W, H), "ink0", dark)
    return img


def water(img, y0, y1, seed=3, a=A_GHOST, amp=1.0):
    """Брижі: тонкі горизонтальні відблиски кобальтом/cobaltText — у грі це одна текстура, що повільно пливе (PERF-01)."""
    rng = np.random.default_rng(seed)
    d = ImageDraw.Draw(img, "RGBA")
    rows = int((y1 - y0) / px(9))
    for i in range(rows):
        y = y0 + i * px(9) + rng.uniform(-2, 2)
        t = i / max(1, rows - 1)
        x = rng.uniform(0, W * 0.2)
        while x < W:
            seg = rng.uniform(60, 260) * K * (0.6 + t)
            name = "cobaltText" if rng.random() < 0.35 else "cobalt"
            al = a * (0.35 + 0.65 * t) * amp
            d.line([(x, y), (x + seg, y)], fill=col(name) + (int(255 * al),), width=max(1, px(2 if t > 0.6 else 1)))
            x += seg + rng.uniform(40, 220) * K


def halftone(img, box, name, a):
    x0, y0, x1, y1 = box
    d = ImageDraw.Draw(img, "RGBA")
    step = px(14)
    for yi, y in enumerate(range(y0, y1, step)):
        for x in range(x0 + (step // 2 if yi % 2 else 0), x1, step):
            t = (x - x0) / max(1, x1 - x0)
            r = step * 0.42 * (1 - t) + 1
            d.ellipse([x - r, y - r, x + r, y + r], fill=col(name) + (int(255 * a),))


def plate(img, x, y, w, h, fill, a=1.0, stripe=None, stripe_w=10, slant=SLANT, outline=None, ow=0, shadow=False, rot=0.0):
    p = trapezoid(px(w), px(h), col(fill), a, slant)
    if stripe:
        ImageDraw.Draw(p).rectangle([0, 0, px(stripe_w) - 1, px(h)], fill=col(stripe) + (255,))
    if outline:
        m = p.getchannel("A")
        o = Image.new("RGBA", (p.width + 2 * px(ow), p.height + 2 * px(ow)), col(outline) + (0,))
        big = m.resize((o.width, o.height))
        o.putalpha(big)
        o.alpha_composite(p, (px(ow), px(ow)))
        p = o
    if rot:
        p = p.rotate(rot, resample=Image.BICUBIC, expand=True)
    if shadow:
        sh = Image.new("RGBA", p.size, col("ink0") + (255,))
        sh.putalpha(p.getchannel("A"))
        put(img, sh, px(x) + px(10), px(y) + px(10))
    put(img, p, px(x), px(y))
    return p


def label(img, text, x, y, size, fill="paper", font="display800", slant=0, shadow=None, sd=(0, 0), track=0.05, anchor="lt"):
    im = txt(text, font, px(size), col(fill), col(shadow) if shadow else None, px(sd[0]), px(sd[1]), slant=slant, tracking=track)
    put(img, im, px(x), px(y), anchor)
    return im


def segbar(img, x, y, n, filled, cell=22, h=16, gap=5, on="gold", off="ink3"):
    d = ImageDraw.Draw(img, "RGBA")
    off_k = math.tan(math.radians(SLANT)) * px(h)
    for i in range(n):
        cx = px(x) + i * (px(cell) + px(gap))
        name = on if i < filled else off
        d.polygon([(cx + off_k, px(y)), (cx + px(cell) + off_k, px(y)), (cx + px(cell), px(y + h)), (cx, px(y + h))], fill=col(name) + (255,))


def footer(img, text):
    label(img, text, 80, 900 - 45 - 15, 15, fill="dim", font="text", track=0.02)


# ======================================================================= варіант Б

def settings_b():
    img = ink_bg(0.62)
    water(img, px(560), H, seed=4)
    label(img, "НАЛАШТУВАННЯ", 80, 58, 62, fill="paper", font="display900", slant=SLANT, shadow="crimson", sd=(6, 6))
    tabs = ["ГРАФІКА", "ЗВУК", "КЕРУВАННЯ", "ДОСТУПНІСТЬ", "МОВА"]
    x = 80
    for i, t in enumerate(tabs):
        active = i == 1
        im = label(img, t, x, 160, 24, fill="paper" if active else "dim", font="display800", track=0.08)
        w = im.width / K
        if active:
            rect(img, (px(x), px(196), px(x + w), px(200)), "gold")
        x += w + 34
    label(img, "Q · E", x + 6, 166, 15, fill="dim", font="text")
    rows = [("ЗАГАЛЬНА", 8, None), ("МУЗИКА", 6, None), ("ЕФЕКТИ", 7, "focus"), ("ГОЛОС", 8, None), ("У ФОНІ", None, "ВИМКНЕНО")]
    y = 236
    for name, val, mode in rows:
        focus = mode == "focus"
        xo = 80 + (25 if focus else 0)
        plate(img, xo, y, 760 + (17 if focus else 0), 60, "gold" if focus else "ink1", 1.0 if focus else A_PANEL,
              stripe="crimson" if focus else "dim")
        label(img, name, xo + 33, y + 13, 30, fill="ink0" if focus else "paper", font="display800")
        if val is not None:
            segbar(img, xo + 330, y + 22, 10, val, on="ink0" if focus else "gold", off="ink2" if not focus else "dim")
            label(img, f"{val * 10} %", xo + 640, y + 16, 22, fill="ink0" if focus else "paper", font="display800")
        else:
            label(img, "‹ " + mode + " ›", xo + 330, y + 16, 22, fill="paper", font="display800")
        y += 70
    label(img, "Тестовий звук грає, поки змінюєш гучність.", 80, y + 6, 17, fill="dim", font="text", track=0.0)
    # кнопки дій — праворуч унизу, головна дія золотом (COL-02)
    bx = 1600 - 80
    for t, prim in (("ЗАСТОСУВАТИ", True), ("СКАСУВАТИ", False), ("ЗА ЗАМОВЧУВАННЯМ", False)):
        im = txt(t, "display800", px(24), col("ink0" if prim else "paper"), tracking=0.06)
        w = im.width / K + 60
        bx -= w
        plate(img, bx, 760, w, 52, "gold" if prim else "ink2", 1.0, stripe=None)
        put(img, im, px(bx + 26), px(760 + 13))
        bx -= 12
    footer(img, "Q/E — вкладка · ↑/↓ — пункт · ←/→ — значення · Enter — застосувати · Esc — назад")
    return img


def loading_b():
    img = Image.new("RGBA", (W, H), col("ink0") + (255,))
    horizon = px(560)
    water(img, horizon, H, seed=9, a=A_DIM * 0.8)
    seal = Image.open(BRAND / "emblem_seal_1024.png").convert("RGBA").resize((px(170), px(170)), Image.LANCZOS)
    sx, sy = px(1600 / 2) - seal.width // 2, horizon - seal.height - px(26)
    put(img, seal, sx, sy)
    refl = seal.transpose(Image.FLIP_TOP_BOTTOM)
    arr = np.asarray(refl).copy()
    hh = arr.shape[0]
    fade = np.linspace(A_DIM, 0, hh)[:, None]
    arr[..., 3] = (arr[..., 3] * fade).astype(np.uint8)
    rows = []
    for yy in range(hh):   # хвиляста підкладка віддзеркалення (у грі — зсув UV у шейдері чи кадрах спрайта)
        rows.append(np.roll(arr[yy], int(4 * K * math.sin(yy / (6 * K))), axis=0))
    refl = Image.fromarray(np.stack(rows))
    put(img, refl, sx, horizon + px(26))
    label(img, "ДОРОГА ДО АРЕНИ", 800, 120, 44, fill="paper", font="display900", slant=SLANT, shadow="crimson", sd=(5, 5), anchor="ct")
    # прогрес — канати рингу: дві кармінні й біла посередині, золото біжить середнім канатом
    x0, x1, yc = 470, 1130, 600 + 120
    d = ImageDraw.Draw(img, "RGBA")
    for dy, name in ((-14, "crimson"), (0, "paper"), (14, "crimson")):
        d.line([(px(x0), px(yc + dy)), (px(x1), px(yc + dy))], fill=col(name) + (int(255 * (0.55 if name == "paper" else 0.8)),), width=px(4))
    prog = 0.62
    d.line([(px(x0), px(yc)), (px(x0 + (x1 - x0) * prog), px(yc))], fill=col("gold") + (255,), width=px(6))
    for xx in (x0 - 14, x1 + 4):
        d.rectangle([px(xx), px(yc - 22), px(xx + 10), px(yc + 22)], fill=col("gold") + (255,))
    label(img, "62 %", x1 + 30, yc - 20, 34, fill="gold", font="display900", slant=SLANT)
    label(img, "ЕТАП 3 / 4", x0, yc + 34, 18, fill="dim", font="display800", track=0.12)
    label(img, "Збираємо натовп", x0 + 120, yc + 34, 17, fill="paper", font="text", track=0.0)
    # порада — паперова картка (єдина паперова поверхня, як афіша B03)
    plate(img, 470, 790, 660, 74, "paper", 1.0, stripe="crimson", slant=0)
    label(img, "ПОРАДА", 500, 800, 18, fill="crimson", font="display900", track=0.12)
    label(img, "Промо перед шоу піднімає ажіотаж: на старті матчу +1.", 500, 828, 17, fill="ink0", font="text", track=0.0)
    return img


# ======================================================================= варіант А

def settings_a():
    img = ink_bg(0.70)
    poly(img, [(0, px(120)), (px(900), 0), (px(1180), 0), (0, px(640))], "cobalt", 0.85)
    poly(img, [(px(1500), H), (W, px(420)), (W, H)], "crimson", 0.9)
    halftone(img, (px(900), 0, W, px(500)), "cobaltText", A_DIM)
    # колаж заголовка: літери на вирізках різного розміру (шрифти Статуту, без обертання тексту — запобіжник читабельності)
    x = 70
    for i, ch in enumerate("НАЛАШТУВАННЯ"):
        size = (78, 60, 70, 56, 74)[i % 5]
        fill_bg = ("paper", "ink0", "gold", "paper", "crimson")[i % 5]
        fg = {"paper": "ink0", "ink0": "paper", "gold": "ink0", "crimson": "paper"}[fill_bg]
        im = txt(ch, "display900", px(size), col(fg))
        bw, bh = im.width + px(14), im.height + px(14)
        yo = (0, 10, -6, 14, 2)[i % 5]
        plate(img, x, 50 + yo, bw / K, bh / K, fill_bg, 1.0, slant=SLANT, shadow=True)
        put(img, im, px(x) + px(6), px(50 + yo) + px(6))
        x += bw / K - 4
    rows = [("ЗАГАЛЬНА", 8, None), ("МУЗИКА", 6, None), ("ЕФЕКТИ", 7, "focus"), ("ГОЛОС", 8, None), ("У ФОНІ", None, "ВИМКНЕНО")]
    y = 240
    for i, (name, val, mode) in enumerate(rows):
        focus = mode == "focus"
        xo = 100 + i * 18 + (40 if focus else 0)   # сходинка по діагоналі
        plate(img, xo, y, 780, 60, "gold" if focus else "ink0", 1.0, outline="paper", ow=3 if focus else 2, shadow=True, slant=20)
        label(img, name, xo + 26, y + 11, 32, fill="ink0" if focus else "paper", font="display900", slant=SLANT)
        if val is not None:
            segbar(img, xo + 330, y + 22, 10, val, on="crimson" if focus else "gold", off="ink3")
        else:
            label(img, mode, xo + 330, y + 14, 24, fill="gold", font="display900", slant=SLANT)
        y += 74
    star = tinted(SPRITES / "star8.png", col("gold"), 1.0, (px(120), px(120)))
    put(img, star, px(1520), px(700))
    label(img, "OK!", 1555, 738, 40, fill="ink0", font="display900", slant=SLANT)
    footer(img, "Q/E — вкладка · ↑/↓ — пункт · ←/→ — значення · Enter — застосувати · Esc — назад")
    return img


def loading_a():
    img = Image.new("RGBA", (W, H), col("ink0") + (255,))
    poly(img, [(0, px(300)), (W, px(80)), (W, px(560)), (0, px(860))], "cobalt", 0.9)
    halftone(img, (0, px(560), W, H), "crimson", A_DIM)
    rays = tinted(SPRITES / "rays12w.png", col("paper"), A_GHOST * 1.6, (px(700), px(700)))
    put(img, rays, px(800) - rays.width // 2, px(380) - rays.height // 2)
    seal = Image.open(BRAND / "emblem_seal_1024.png").convert("RGBA").resize((px(220), px(220)), Image.LANCZOS)
    put(img, seal, px(800) - seal.width // 2, px(380) - seal.height // 2)
    x = 330
    for i, ch in enumerate("ЗАВАНТАЖЕННЯ"):
        size = (64, 50, 58, 46)[i % 4]
        fill_bg = ("paper", "gold", "ink0", "crimson")[i % 4]
        fg = {"paper": "ink0", "ink0": "paper", "gold": "ink0", "crimson": "paper"}[fill_bg]
        im = txt(ch, "display900", px(size), col(fg))
        bw, bh = im.width + px(12), im.height + px(12)
        plate(img, x, 640 + (0, 8, -4, 10)[i % 4], bw / K, bh / K, fill_bg, 1.0, shadow=True)
        put(img, im, px(x) + px(6), px(640 + (0, 8, -4, 10)[i % 4]) + px(6))
        x += bw / K - 2
    # прогрес-«блискавка»: рвані клини
    xs = 330
    for i in range(14):
        on = i < 9
        plate(img, xs + i * 66, 760, 58, 30, "gold" if on else "ink3", 1.0, slant=30, outline="paper" if on else None, ow=2)
    label(img, "62%", 1290, 748, 50, fill="gold", font="display900", slant=SLANT, shadow="crimson", sd=(5, 5))
    return img


def sheet(left, right, title, out):
    w2, h2 = W // 2, H // 2
    pad = 24
    hdr = 64
    sh = Image.new("RGBA", (w2 * 2 + pad * 3, h2 + pad * 2 + hdr), col("ink1") + (255,))
    t = txt(title, "display800", 34, col("paper"), tracking=0.04)
    sh.alpha_composite(t, (pad, 18))
    sh.alpha_composite(left.resize((w2, h2), Image.LANCZOS), (pad, hdr + pad))
    sh.alpha_composite(right.resize((w2, h2), Image.LANCZOS), (w2 + pad * 2, hdr + pad))
    sh.convert("RGB").save(out, quality=90)
    print("→", out)


def main():
    utf8()
    out = Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT / "docs/ring/design/ref"
    out.mkdir(parents=True, exist_ok=True)
    sheet(settings_a(), loading_a(), "ВАРІАНТ А · ЕНЕРГІЯ: КУТИ, КОЛАЖ, НАПІВТОН (ПАЛІТРА ХОЛОДНА)", out / "menus_variant_A.jpg")
    sheet(settings_b(), loading_b(), "ВАРІАНТ Б · ЧИСТИЙ AA + ВОДА: СПОКІЙНА СІТКА, РІЗКІСТЬ ЛИШЕ В АКЦЕНТАХ", out / "menus_variant_B.jpg")


if __name__ == "__main__":
    main()
