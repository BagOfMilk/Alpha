"""Ескіз титулу «рівня AA» поверх справжнього кадру гри (ТЗ B01 v2).

Світло й камера тут — ІМІТАЦІЯ (у грі — реальне світло й камера, див. B01 §«Сцена»); UI — за токенами й шрифтами Статуту.
Запуск (з tools/):  python -m ring_design.title_mock [menu|save|press|compare|assets] [вихід.png]
"""
import math
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

from .titantron import render as R
from .titantron.common import BRAND, COL, KITS, ROOT, utf8

W, H = 1920, 1080
K = H / 900.0  # еталон 900 → 1080
SRC = ROOT / "docs/ring/design/ref/title_before_2026-11-06.webp"


def col(name):
    return COL[name]


def txt(text, fkey, px, fill, shadow=None, sdx=0, sdy=0, outline=None, ow=0, slant=0, tracking=0.0):
    im = R._text_raw(text, fkey, int(px * 2), fill, shadow, int(sdx * 2), int(sdy * 2), outline, int(ow * 2), slant, tracking)
    return im.resize((max(1, im.width // 2), max(1, im.height // 2)), Image.LANCZOS)


def put(base, im, x, y, anchor="lt", alpha=1.0):
    if alpha < 1.0:
        im = im.copy()
        im.putalpha(im.getchannel("A").point(lambda v: int(v * alpha)))
    if anchor == "ct":
        x -= im.width // 2
    elif anchor == "rt":
        x -= im.width
    elif anchor == "lm":
        y -= im.height // 2
    elif anchor == "cm":
        x -= im.width // 2
        y -= im.height // 2
    R._paste(base, im, x + im.width / 2, y + im.height / 2)


def trapezoid(w, h, fill, a=1.0, slant=12):
    ss = 2
    m = Image.new("L", (w * ss, h * ss), 0)
    off = math.tan(math.radians(slant)) * h * ss
    ImageDraw.Draw(m).polygon([(0, 0), (w * ss, 0), (w * ss - off, h * ss), (0, h * ss)], fill=255)
    m = m.resize((w, h), Image.LANCZOS)
    im = Image.new("RGBA", (w, h), fill + (255,))
    im.putalpha(m.point(lambda v: int(v * a)))
    return im


# ---------------------------------------------------------------- сцена: рамка, світло, колір

def scene():
    src = Image.open(SRC).convert("RGB")
    ImageDraw.Draw(src).rectangle([24, 510, 312, 722], fill=(26, 24, 31))   # стара UI-картка запечена в знімку — прибираємо
    # ноутбук на столі (реквізит прибрати з кадру титулу): замальовуємо кольором стільниці з розтушованим краєм
    box = (480, 540, 556, 604)
    desk = src.crop((430, 585, 476, 610)).resize((1, 1), Image.BOX).getpixel((0, 0))
    patch = Image.new("RGB", (box[2] - box[0], box[3] - box[1]), desk)
    m = Image.new("L", patch.size, 0)
    ImageDraw.Draw(m).rectangle([6, 6, patch.width - 7, patch.height - 7], fill=255)
    src.paste(patch, box[:2], m.filter(ImageFilter.GaussianBlur(4)))
    src = src.crop((0, 6, src.width, src.height))                            # верхні рядки знімка — чорна смуга
    s = W / src.width * 1.20                      # камера ближче; ринг зміщено у праву третину
    big = src.resize((round(src.width * s), round(src.height * s)), Image.LANCZOS)
    a = np.asarray(big.crop((0, 0, W, H))).astype(np.float32) / 255.0
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
    cx, cy = 0.62 * W, 0.42 * H
    r = np.sqrt(((xx - cx) / (0.85 * H)) ** 2 + ((yy - cy) / (0.80 * H)) ** 2)
    vig = 1.0 - 0.80 * np.clip((r - 0.30) / 0.80, 0, 1) ** 1.3
    spot = 1.0 + 0.34 * np.exp(-(r / 0.40) ** 2)
    a = a * 0.74 * vig[..., None] * spot[..., None]
    lum = a.mean(axis=2, keepdims=True)
    a = a + (1.0 - lum) * np.array([0.0, 0.004, 0.028], np.float32)   # тіні холодніші
    # колірні плями: кобальт (герой) ліворуч від борців, кармін (лиходій) праворуч
    for (px, py, rad, name, k) in ((0.47 * W, 0.62 * H, 0.30 * H, "cobalt", 0.11), (0.93 * W, 0.50 * H, 0.38 * H, "crimson", 0.18)):
        d = np.sqrt(((xx - px) / rad) ** 2 + ((yy - py) / rad) ** 2)
        g = np.exp(-(d ** 2)) * k
        a = a + g[..., None] * (np.array(col(name), np.float32) / 255.0)
    img = Image.fromarray((np.clip(a, 0, 1) * 255).astype(np.uint8)).convert("RGBA")
    # плоскі світлові конуси (афішна мова, без blur)
    cone = Image.new("L", (W, H), 0)
    d = ImageDraw.Draw(cone)
    for apex, base_c, half in ((0.50, 0.57, 0.050), (0.64, 0.64, 0.055), (0.78, 0.71, 0.050)):
        d.polygon([(apex * W - 14, -10), (apex * W + 14, -10), ((base_c + half) * W, 0.66 * H), ((base_c - half) * W, 0.66 * H)], fill=255)
    fall = np.clip(1.0 - yy / (0.70 * H), 0, 1) ** 1.2
    cm = (np.asarray(cone).astype(np.float32) / 255.0) * fall * 0.15
    lay = Image.new("RGBA", (W, H), col("paper") + (255,))
    lay.putalpha(Image.fromarray((cm * 255).astype(np.uint8)))
    img.alpha_composite(lay)
    # ліва скрима під UI (гладкий спад — це не «скло», а підкладка читабельності, роль Scrim)
    t = np.clip(xx / (0.56 * W), 0, 1)
    al = (1.0 - (t * t * (3 - 2 * t))) * 0.93
    sc = Image.new("RGBA", (W, H), col("ink0") + (255,))
    sc.putalpha(Image.fromarray((al * 255).astype(np.uint8)))
    img.alpha_composite(sc)
    return img


def dust(img, seed=5):
    rng = np.random.default_rng(seed)
    d = ImageDraw.Draw(img, "RGBA")
    for _ in range(26):
        x, y = rng.uniform(0.40, 0.98) * W, rng.uniform(0.05, 0.95) * H
        if 0.46 * W < x < 0.76 * W and 0.10 * H < y < 0.64 * H:
            continue   # не накривати борців і рефері
        s = rng.uniform(3, 7) * K
        d.polygon([(x, y - s), (x + s, y), (x, y + s), (x - s, y)], fill=col("paper") + (int(255 * 0.28),))


# ---------------------------------------------------------------- UI

def lockup(img, x, y, k=1.0):
    seal_d, frame_w, gap = int(118 * k), int(590 * k), int(20 * k)
    frame_h = round(frame_w * 320 / 1040)
    seal = Image.open(BRAND / "emblem_seal_1024.png").convert("RGBA").resize((seal_d, seal_d), Image.LANCZOS)
    frame = Image.open(BRAND / "rope_frame_1040.png").convert("RGBA").resize((frame_w, frame_h), Image.LANCZOS)
    put(img, seal, x, y + (frame_h - seal_d) // 2)
    fx = x + seal_d + gap
    put(img, frame, fx, y)
    word = txt("РИНГ", "display900", 196 * k, col("paper"), shadow=col("crimson"), sdx=7 * k, sdy=9 * k,
               outline=col("ink0"), ow=7 * k, slant=12, tracking=0.02)
    put(img, word, fx + frame_w // 2, y + frame_h // 2, "cm")
    tag = txt("МІСЦЕВА ФЕДЕРАЦІЯ · ОДИН БОРЕЦЬ · СЕЗОН ЗА СЕЗОНОМ", "display700", 22 * k, col("dim"), tracking=0.14)
    put(img, tag, x, y + frame_h + int(30 * k))
    return frame_h


def menu_item(label, kind, w=540, h=72, hint=None):
    """kind: focus (золото, головна дія) | normal | disabled | exit."""
    k = K / 1.2
    fills = {"focus": (col("gold"), 1.0), "normal": (col("ink1"), 0.88), "disabled": (col("ink1"), 0.62), "exit": (col("ink1"), 0.88)}
    stripe = {"focus": col("crimson"), "normal": col("dim"), "disabled": col("ash"), "exit": col("signal")}[kind]
    f, a = fills[kind]
    im = trapezoid(w, h, f, a)
    ImageDraw.Draw(im).rectangle([0, 0, 11, h], fill=stripe + (255,))
    tcol = {"focus": col("ink0"), "normal": col("paper"), "disabled": col("ash"), "exit": col("paper")}[kind]
    t = txt(label, "display800", 41, tcol, tracking=0.05)
    put(im, t, 40, h // 2, "lm")
    if hint:
        hc = col("ink0") if kind == "focus" else col("dim")
        ht = txt(hint, "text", 18, hc, tracking=0.02)
        put(im, ht, w - 70, h // 2, "rt" if False else "lm") if False else put(im, ht, w - 70 - ht.width, h // 2, "lm")
    return im


def footer(img):
    # safe-area (XAG/TV): суттєве не ближче 5 % до краю — 96 px з боків, 54 px зверху/знизу @1080p
    hint = txt("СТРІЛКИ — ВИБІР   ·   ENTER — ПІДТВЕРДИТИ", "text", 18, col("dim"), tracking=0.06)   # на корені меню «назад» немає (XAG: B/Esc нічого)
    put(img, hint, 96, H - 54 - hint.height - 8)
    ver = txt("ВЕРСІЯ 0.1 · ЗБІРКА ДЛЯ ПОКАЗУ", "text", 18, col("dim"), tracking=0.08)
    put(img, ver, W - 96, H - 54 - ver.height - 8, "rt")


def make(mode="menu"):
    img = scene()
    dust(img)
    if mode in ("menu", "save"):
        lockup(img, 96, 96, 1.0)
        if mode == "save":
            items = [("Продовжити", "focus", "СЕЗОН 1 · ТИЖДЕНЬ 3"), ("Нова кар'єра", "normal", None), ("Налаштування", "normal", None),
                     ("Титри", "normal", None), ("Вийти", "exit", None)]
        else:
            items = [("Нова кар'єра", "focus", "ENTER"), ("Налаштування", "normal", None), ("Титри", "normal", None), ("Вийти", "exit", None)]
        y = 940 - (len(items) * 72 + (len(items) - 1) * 12)
        for label, kind, hint in items:
            im = menu_item(label.upper(), kind, w=560 if kind == "focus" else 540, hint=hint)
            put(img, im, 96 + (30 if kind == "focus" else 0), y)
            y += 72 + 12
    else:  # press: двоступінчастий старт — логотип у центрі-ліворуч і запрошення (на Android — «Торкнись екрана»)
        fh = lockup(img, 96, 300, 1.06)
        prompt = txt("НАТИСНИ БУДЬ-ЯКУ КЛАВІШУ", "display800", 34, col("paper"), tracking=0.14)
        py = 300 + fh + 120
        put(img, prompt, 96, py)
        d = ImageDraw.Draw(img, "RGBA")
        d.rectangle([96, py + 52, 96 + prompt.width, py + 56], fill=col("crimson") + (255,))
    footer(img)
    return img.convert("RGB")


def export_assets():
    """Готовий оверлей для гри — ОДНА квадра на весь екран (віньєтка + ліва скрима під UI), колір ink0; ті самі формули, що в ескізі."""
    w, h = 1024, 576
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    r = np.sqrt(((xx - 0.62 * w) / (0.85 * h)) ** 2 + ((yy - 0.42 * h) / (0.80 * h)) ** 2)
    av = 0.80 * np.clip((r - 0.30) / 0.80, 0, 1) ** 1.3
    t = np.clip(xx / (0.56 * w), 0, 1)
    asc = (1.0 - (t * t * (3 - 2 * t))) * 0.93
    a = 1.0 - (1.0 - av) * (1.0 - asc)
    im = Image.new("RGBA", (w, h), col("ink0") + (255,))
    im.putalpha(Image.fromarray((a * 255).astype(np.uint8)))
    im.save(BRAND / "title_overlay.png", optimize=True)
    return float(a[int(0.42 * h), int(0.62 * w)]), float(a[0, 0]), float(a[h // 2, 0]), float(a[h // 2, int(0.6 * w)])


def compare(out):
    before = Image.open(SRC).convert("RGB").resize((960, 540), Image.LANCZOS)
    after = make("menu").resize((960, 540), Image.LANCZOS)
    cv = Image.new("RGB", (1920 + 18, 540 + 60), (32, 30, 38))
    cv.paste(before, (6, 54))
    cv.paste(after, (972, 54))
    d = ImageDraw.Draw(cv)
    f = R._font("text", 22)
    d.text((10, 14), "ЗАРАЗ — кадр збірки 06.11.2026", font=f, fill=col("paper"))
    d.text((976, 14), "ТЗ B01 v2 — ескіз (світло й камера — імітація)", font=f, fill=col("gold"))
    cv.save(out, quality=90) if out.lower().endswith(".jpg") else cv.save(out)


if __name__ == "__main__":
    utf8()
    mode = sys.argv[1] if len(sys.argv) > 1 else "menu"
    out = sys.argv[2] if len(sys.argv) > 2 else "title_%s.png" % mode
    if mode == "assets":
        print("оверлей α: центр рингу %.2f, лівий верхній кут %.2f, лівий край %.2f, права частина %.2f" % export_assets())
    elif mode == "compare":
        compare(out)
    else:
        make(mode).save(out, quality=88) if out.lower().endswith(".jpg") else make(mode).save(out)
    print("ok", out)
