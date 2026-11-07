"""Ескізи «брутального конструктивізму» (пропозиція арт-директора): титул, HUD матчу, виноски.

Конструктивістська афіша: клин, коло, прямокутники, гігантські обрізані літери, діагональ 12°, обводка й жорстка тінь, спот-колір на дуотоні.
Кадр сцени й світло в ескізах — ІМІТАЦІЯ на готовому кадрі збірки; UI — лише токени й шрифти Статуту.
Запуск (з tools/):  python -m ring_design.brutal_mock [title|hud|callouts|all] [тека-виводу]
"""
import math
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

from .title_mock import prepared_source
from .titantron import render as R
from .titantron.common import ALPHA, BRAND, COL, ROOT, SPRITES, utf8

W, H = 1920, 1080
TAN = math.tan(math.radians(12))


def col(n):
    return COL[n]


INK, INK1, INK3, PAPER, DIM, ASH = col("ink0"), col("ink1"), col("ink3"), col("paper"), col("dim"), col("ash")
GOLD, CRIM, SIG, COB, COBT, ROSE, FROST, SLATE = (col(n) for n in ("gold", "crimson", "signal", "cobalt", "cobaltText", "rose", "frost", "slate"))


# ---------------------------------------------------------------- геометрія

def _isect(a1, b1, a2, b2):
    x1, y1 = a1
    x2, y2 = b1
    x3, y3 = a2
    x4, y4 = b2
    den = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4)
    if abs(den) < 1e-9:
        return b1
    px = ((x1 * y2 - y1 * x2) * (x3 - x4) - (x1 - x2) * (x3 * y4 - y3 * x4)) / den
    py = ((x1 * y2 - y1 * x2) * (y3 - y4) - (y1 - y2) * (x3 * y4 - y3 * x4)) / den
    return (px, py)


def offset_poly(pts, d):
    """Зовнішній зсув опуклого багатокутника на d (гострі кути без заокруглення)."""
    n = len(pts)
    area = sum(pts[i][0] * pts[(i + 1) % n][1] - pts[(i + 1) % n][0] * pts[i][1] for i in range(n)) / 2
    sg = 1 if area > 0 else -1

    def nrm(p, q):
        dx, dy = q[0] - p[0], q[1] - p[1]
        ln = math.hypot(dx, dy)
        return (sg * dy / ln, -sg * dx / ln)

    out = []
    for i in range(n):
        p0, p1, p2 = pts[i - 1], pts[i], pts[(i + 1) % n]
        n1, n2 = nrm(p0, p1), nrm(p1, p2)
        a1 = (p0[0] + n1[0] * d, p0[1] + n1[1] * d)
        b1 = (p1[0] + n1[0] * d, p1[1] + n1[1] * d)
        a2 = (p1[0] + n2[0] * d, p1[1] + n2[1] * d)
        b2 = (p2[0] + n2[0] * d, p2[1] + n2[1] * d)
        out.append(_isect(a1, b1, a2, b2))
    return out


class Cv:
    def __init__(self, bg=INK, w=W, h=H, ss=2, img=None):
        self.w, self.h, self.ss = w, h, ss
        if img is not None:
            self.im = img.resize((w * ss, h * ss), Image.LANCZOS).convert("RGBA")
        else:
            self.im = Image.new("RGBA", (w * ss, h * ss), tuple(bg) + (255,))

    def _fill_poly(self, P, color, a=1.0):
        ss = self.ss
        Q = [(x * ss, y * ss) for x, y in P]
        xs, ys = [q[0] for q in Q], [q[1] for q in Q]
        x0, y0 = max(int(min(xs)) - 2, 0), max(int(min(ys)) - 2, 0)
        x1, y1 = min(int(max(xs)) + 3, self.im.width), min(int(max(ys)) + 3, self.im.height)
        if x1 <= x0 or y1 <= y0:
            return
        m = Image.new("L", (x1 - x0, y1 - y0), 0)
        ImageDraw.Draw(m).polygon([(qx - x0, qy - y0) for qx, qy in Q], fill=255)
        lay = Image.new("RGBA", m.size, tuple(color) + (255,))
        lay.putalpha(m.point(lambda v: int(v * a)) if a < 1 else m)
        self.im.alpha_composite(lay, (x0, y0))

    def poly(self, pts, fill, outline=None, ow=0, shadow=None, sh=(0, 0), a=1.0):
        base = offset_poly(pts, ow) if (outline is not None and ow) else pts
        if shadow is not None:
            self._fill_poly([(x + sh[0], y + sh[1]) for x, y in base], shadow, a)
        if outline is not None and ow:
            self._fill_poly(base, outline, a)
        self._fill_poly(pts, fill, a)

    def rect(self, x, y, w, h, fill, **kw):
        self.poly([(x, y), (x + w, y), (x + w, y + h), (x, y + h)], fill, **kw)

    def slab(self, x, y, w, h, fill, slant="r", **kw):
        """Плита з нахиленим краєм (12°): slant = r (правий), l (лівий), both (паралелограм)."""
        o = TAN * h
        if slant == "r":
            pts = [(x, y), (x + w, y), (x + w - o, y + h), (x, y + h)]
        elif slant == "l":
            pts = [(x + o, y), (x + w, y), (x + w, y + h), (x, y + h)]
        else:
            pts = [(x + o, y), (x + w, y), (x + w - o, y + h), (x, y + h)]
        self.poly(pts, fill, **kw)
        return pts

    def circle(self, cx, cy, r, fill=None, outline=None, ow=0):
        ss = self.ss
        d = ImageDraw.Draw(self.im)
        if outline is not None and ow:
            d.ellipse([(cx - r - ow) * ss, (cy - r - ow) * ss, (cx + r + ow) * ss, (cy + r + ow) * ss], fill=tuple(outline) + (255,))
        if fill is not None:
            d.ellipse([(cx - r) * ss, (cy - r) * ss, (cx + r) * ss, (cy + r) * ss], fill=tuple(fill) + (255,))

    def circle_img(self, img, cx, cy, r):
        ss = self.ss
        big = img.resize((self.w * ss, self.h * ss), Image.LANCZOS).convert("RGBA")
        m = Image.new("L", big.size, 0)
        ImageDraw.Draw(m).ellipse([(cx - r) * ss, (cy - r) * ss, (cx + r) * ss, (cy + r) * ss], fill=255)
        big.putalpha(m)
        self.im.alpha_composite(big)

    def sprite(self, name, cx, cy, w, h, tint=None, a=1.0, rot=0):
        ss = self.ss
        im = R._sprite(name).resize((int(w * ss), int(h * ss)), Image.BICUBIC)
        if tint is not None:
            im = R._tint(im, tint, a)
        if rot:
            im = im.rotate(rot, expand=True, resample=Image.BICUBIC)
        R._paste(self.im, im, cx * ss, cy * ss)

    def image(self, im, x, y):
        R._paste(self.im, im, (x + im.width / 2 / self.ss) * self.ss, (y + im.height / 2 / self.ss) * self.ss)

    def text(self, s, x, y, fkey, px, fill, anchor="lt", slant=0, rot=0, shadow=None, sdx=0, sdy=0, outline=None, ow=0,
             tracking=0.0, a=1.0, maxw=None):
        ss = self.ss
        args = dict(shadow=shadow, sdx=int(sdx * ss), sdy=int(sdy * ss), outline=outline, ow=int(ow * ss), slant=slant, tracking=tracking)
        im = R._text_raw(s, fkey, int(px * ss), fill, args["shadow"], args["sdx"], args["sdy"], args["outline"], args["ow"], slant, tracking)
        if maxw and im.width > maxw * ss:
            k = maxw * ss / im.width
            im = R._text_raw(s, fkey, max(8, int(px * ss * k)), fill, args["shadow"], args["sdx"], args["sdy"], args["outline"], args["ow"], slant, tracking)
        if rot:
            im = im.rotate(rot, expand=True, resample=Image.BICUBIC)
        if a < 1:
            im = im.copy()
            im.putalpha(im.getchannel("A").point(lambda v: int(v * a)))
        w, h = im.size
        ax = {"l": 0, "c": w / 2, "r": w}[anchor[0]]
        ay = {"t": 0, "m": h / 2, "b": h}[anchor[1]]
        R._paste(self.im, im, x * ss - ax + w / 2, y * ss - ay + h / 2)
        return im.width / ss, im.height / ss

    def done(self, size=None):
        out = self.im.resize((self.w, self.h), Image.LANCZOS).convert("RGB")
        return out if size is None else out.resize(size, Image.LANCZOS)


# ---------------------------------------------------------------- фото сцени

def _frame(src, zoom, ring_orig, place):
    s = W / src.width * zoom
    big = src.resize((round(src.width * s), round(src.height * s)), Image.LANCZOS)
    cv = Image.new("RGB", (W, H), INK)
    cv.paste(big, (round(place[0] - ring_orig[0] * s), round(place[1] - ring_orig[1] * s)))
    return cv


def _grade(img, cx, cy, dark=0.80, vig=0.80):
    a = np.asarray(img).astype(np.float32) / 255.0
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
    r = np.sqrt(((xx - cx) / (0.85 * H)) ** 2 + ((yy - cy) / (0.80 * H)) ** 2)
    v = 1.0 - vig * np.clip((r - 0.30) / 0.80, 0, 1) ** 1.3
    spot = 1.0 + 0.30 * np.exp(-(r / 0.40) ** 2)
    a = a * dark * v[..., None] * spot[..., None]
    return np.clip(a, 0, 1)


def photo_duotone(src, zoom, ring_orig, place):
    a = _grade(_frame(src, zoom, ring_orig, place), place[0], place[1], dark=1.0, vig=0.35)
    g = a @ np.array([0.2126, 0.7152, 0.0722], np.float32)
    g = np.clip((g - 0.04) / 0.92, 0, 1)
    g = g * g * (3 - 2 * g)
    ink, paper = np.array(INK, np.float32) / 255, np.array(PAPER, np.float32) / 255
    duo = ink + (paper - ink) * g[..., None]
    R_, G_, B_ = a[..., 0], a[..., 1], a[..., 2]
    red = np.clip((R_ - np.maximum(G_, B_) - 0.20) / 0.22, 0, 1)
    yel = np.clip((np.minimum(R_, G_) - B_ - 0.22) / 0.22, 0, 1)
    blu = np.clip((B_ - np.maximum(R_, G_) - 0.08) / 0.16, 0, 1)
    m = np.clip(red + yel + blu, 0, 1)[..., None]
    mean = a.mean(axis=2, keepdims=True)
    boosted = np.clip((a - mean) * 1.7 + mean * 1.25, 0, 1)
    duo = duo * (1 - m) + boosted * m
    return Image.fromarray((duo * 255).astype(np.uint8))


def photo_natural(src, zoom, ring_orig, place):
    a = _grade(_frame(src, zoom, ring_orig, place), place[0], place[1])
    return Image.fromarray((a * 255).astype(np.uint8))


# ---------------------------------------------------------------- елементи

def glyph(cv, kind, cx, cy, s, color):
    """Конструктивістська тріада й хрест: клин, коло, квадрат, X."""
    if kind == "wedge":
        cv.poly([(cx - s, cy + s * 0.8), (cx + s, cy), (cx - s, cy - s * 0.8)], color)
    elif kind == "disc":
        cv.circle(cx, cy, s * 0.85, fill=color)
    elif kind == "block":
        cv.rect(cx - s * 0.8, cy - s * 0.8, s * 1.6, s * 1.6, color)
    else:
        cv.poly([(cx - s, cy - s * 0.45), (cx - s * 0.45, cy - s), (cx + s, cy + s * 0.45), (cx + s * 0.45, cy + s)], color)
        cv.poly([(cx + s, cy - s * 0.45), (cx + s * 0.45, cy - s), (cx - s, cy + s * 0.45), (cx - s * 0.45, cy + s)], color)


def menu_item(cv, x, y, w, h, label, kind, focus=False):
    if focus:
        x += 44
    fill, ol = (GOLD, INK) if focus else (INK, PAPER)
    cv.slab(x, y, w, h, fill, "r", outline=ol, ow=6 if focus else 5, shadow=INK, sh=(12, 12))
    box_fill = INK if focus else PAPER
    cv.rect(x, y, h, h, box_fill)
    glyph(cv, kind, x + h / 2, y + h / 2, h * 0.26, GOLD if focus else INK)
    cv.text(label, x + h + 26, y + h / 2, "display900", 46, INK if focus else PAPER, anchor="lm", tracking=0.03)
    if focus:
        cv.poly([(x + w - 6, y + h / 2 - 30), (x + w + 46, y + h / 2), (x + w - 6, y + h / 2 + 30)], CRIM, outline=INK, ow=5)


# ---------------------------------------------------------------- титул

def title():
    src = prepared_source()
    cx, cy, r = 1250, 530, 470
    ph = photo_duotone(src, 1.30, (645, 294), (cx, cy))
    cv = Cv(INK)
    # друкарська растрова пляма під клином
    cv.sprite("halftone_r", 760, 800, 1500, 750, tint=CRIM, a=ALPHA["dim"])
    # діагональні лінії-«креслення» — позаду кола, вилазять із нього в куток
    cv.poly([(900, 462), (1920, 20), (1920, 58), (900, 490)], PAPER)
    cv.poly([(900, 490), (1920, 58), (1920, 72), (900, 500)], CRIM)
    # коло-вікно: зсунуте кармінове коло (недодрук) і паперове кільце
    cv.circle(cx + 20, cy + 20, r, fill=CRIM)
    cv.circle_img(ph, cx, cy, r)
    cv.circle(cx, cy, r + 14, outline=PAPER, ow=0)
    d = ImageDraw.Draw(cv.im)
    d.ellipse([(cx - r - 14) * 2, (cy - r - 14) * 2, (cx + r + 14) * 2, (cy + r + 14) * 2], outline=tuple(PAPER) + (255,), width=28)
    # кармінний клин, що встромляється в коло
    cv.poly([(0, 110), (900, 470), (0, 1080)], CRIM)
    # слово
    cv.text("РИНГ", 24, 6, "display900", 420, PAPER, slant=12, rot=0, shadow=INK, sdx=16, sdy=16, tracking=0.0)
    # підпис-смуга
    cv.slab(40, 420, 800, 62, INK, "r", outline=PAPER, ow=5, shadow=INK, sh=(12, 12))
    cv.text("МІСЦЕВА ФЕДЕРАЦІЯ · ОДИН БОРЕЦЬ · СЕЗОН ЗА СЕЗОНОМ", 66, 451, "display700", 27, PAPER, anchor="lm", tracking=0.10)
    # меню
    items = [("НОВА КАР'ЄРА", "wedge", True), ("НАЛАШТУВАННЯ", "disc", False), ("ТИТРИ", "block", False), ("ВИЙТИ", "cross", False)]
    y = 560
    for label, kind, focus in items:
        menu_item(cv, 40, y, 640, 80, label, kind, focus)
        y += 92
    # печатка-штамп на стику кола й клина
    cv.sprite("brand_seal", 835, 905, 190, 190)
    # мікротекст уздовж краю
    cv.text("ВЕРСІЯ 0.1 · ЗБІРКА ДЛЯ ПОКАЗУ", 1882, 540, "text", 20, DIM, anchor="cm", rot=90, tracking=0.12)
    cv.text("СТРІЛКИ — ВИБІР   ·   ENTER — ПІДТВЕРДИТИ", 40, 1000, "text", 20, PAPER, anchor="lm", tracking=0.08)
    return cv.done()


# ---------------------------------------------------------------- HUD матчу

def meter(cv, x, y, n, filled, color, w=17, h=22, gap=3, mirror=False):
    for i in range(n):
        xx = x + (n - 1 - i) * (w + gap) if mirror else x + i * (w + gap)
        on = i < filled
        cv.rect(xx, y, w, h, color if on else INK3)


def side_slab(cv, left, name, kicker, state, dmg, imp, tokens, head):
    sw, hh, bh = 492, 56, 71
    tot = hh + bh
    o = TAN * tot
    ox = 0 if left else W - sw
    if left:
        full = [(0, 0), (sw, 0), (sw - o, tot), (0, tot)]
        hd = [(0, 0), (sw, 0), (sw - TAN * hh, hh), (0, hh)]
        tx = 24
    else:
        full = [(W, 0), (W - sw, 0), (W - sw + o, tot), (W, tot)]
        hd = [(W, 0), (W - sw, 0), (W - sw + TAN * hh, hh), (W, hh)]
        tx = W - sw + 24 + o
    cv.poly(full, INK, outline=PAPER, ow=5, shadow=INK, sh=(10, 10))
    cv.poly(hd, head)
    cv.text(name, tx, 9, "display900", 48, PAPER, slant=12, maxw=300)
    if kicker:
        cv.text(kicker, tx + 2, 52 + 0, "text", 18, PAPER, anchor="lb", tracking=0.10, a=0.0)
    cv.text(state, ox + sw - 22 - (o if left else 0) if left else W - 22, 30, "text", 18, PAPER, anchor="rm", tracking=0.08, a=0.92)
    base = ox + (24 if left else 24 + o)
    for row, (label, n, val, color) in enumerate((("УШКОДЖЕННЯ", 12, dmg, SIG), ("ІМПУЛЬС", 10, imp, GOLD))):
        yy = hh + 8 + row * 31
        cv.text(label, base, yy + 11, "text", 18, DIM, anchor="lm", tracking=0.06)
        meter(cv, base + 162, yy, n, val, color, w=14 if n == 12 else 16, gap=3)
        cv.text("%d/%d" % (val, n), base + 162 + n * ((14 if n == 12 else 16) + 3) + 10, yy + 11, "display900", 24, PAPER, anchor="lm")
    # жетони реверсу — «наклейка» під плитою
    tab_x = (0 if left else W - 214)
    cv.slab(tab_x, tot + 2, 214, 36, INK, "r" if left else "l", outline=PAPER, ow=4, shadow=INK, sh=(8, 8))
    cv.text("РЕВЕРС", tab_x + (22 if left else 22 + TAN * 36), tot + 21, "text", 18, PAPER, anchor="lm", tracking=0.10)
    for i in range(tokens):
        cv.circle(tab_x + (128 if left else 128 + 14) + i * 30, tot + 21, 10, fill=PAPER, outline=INK, ow=0)


def hud():
    src = prepared_source()
    ph = photo_natural(src, 1.10, (645, 294), (960, 520))
    cv = Cv(img=ph)
    # верхні плити
    side_slab(cv, True, "НОВАЧОК", None, "СТОЇТЬ · ЦЕНТР", 1, 1, 2, COB)
    side_slab(cv, False, "КОЛІБРІ", None, "СТОЇТЬ · ЦЕНТР", 2, 3, 2, CRIM)
    # центр: раунд і ажіотаж
    cv.poly([(770, 0), (1150, 0), (1118, 124), (802, 124)], INK, outline=PAPER, ow=5, shadow=INK, sh=(10, 10))
    cv.text("РАУНД", 826, 18, "text", 18, DIM, tracking=0.12)
    cv.text("02", 822, 36, "display900", 78, PAPER, slant=12)
    cv.text("/20", 936, 82, "display900", 30, DIM, anchor="lm")
    cv.text("АЖІОТАЖ", 998, 22, "text", 18, DIM, tracking=0.12)
    for i in range(10):
        cv.rect(996 + (i % 5) * 22, 46 + (i // 5) * 30, 18, 24, PAPER if i < 4 else INK3)
    cv.slab(790, 128, 340, 34, INK, "both", outline=PAPER, ow=4, shadow=INK, sh=(8, 8))
    cv.text("ЗАЛ ГУДЕ · РЕФЕРІ СТЕЖИТЬ", 960, 146, "text", 18, PAPER, anchor="cm", tracking=0.08)
    # журнал
    cv.slab(40, 636, 640, 160, INK, "r", outline=PAPER, ow=5, shadow=INK, sh=(10, 10))
    cv.text("ЖУРНАЛ", 62, 652, "text", 18, DIM, tracking=0.14)
    lines = ["P2 Колібрі: Встати — частково (7–9)", "P2 Колібрі: Удар — успіх (10+) · ушкодження +2", "Колібрі: імпульс +1", "P2 Колібрі завершує хід"]
    for i, ln in enumerate(lines):
        cv.text(ln, 62, 684 + i * 27, "text", 19, PAPER if i == 3 else DIM, maxw=570)
    # рядок ходу — кармінний клин
    cv.slab(40, 806, 560, 44, CRIM, "r", outline=INK, ow=4, shadow=INK, sh=(8, 8))
    cv.text("ХІД: МАТЕО «КОЛІБРІ» РАМОС", 62, 828, "display800", 26, PAPER, anchor="lm", tracking=0.04)
    cv.text("НАВЕДИ НА ДІЮ — ПОБАЧИШ ШАНСИ", 650, 836, "text", 20, PAPER, anchor="lm", tracking=0.10, shadow=INK, sdx=3, sdy=3)
    # панель дій: ключ-цифра, назва в два рядки, смуга шансів (промах / частково / успіх), ціна
    def wrap2(nm):
        nm = nm.upper()
        if len(nm) <= 15 or " " not in nm:
            return [nm]
        mid = len(nm) / 2
        best = min((abs(i - mid), i) for i, ch in enumerate(nm) if ch == " ")[1]
        return [nm[:best], nm[best + 1:]]

    acts = [("1", "Удар передпліччям", 1, (22, 50, 28)), ("2", "Низький удар ногою", 1, (26, 48, 26)), ("3", "Кидок через стегно", 2, (34, 44, 22)),
            ("4", "Затягнути до канатів", 1, (30, 46, 24)), ("5", "Затягнути в кут", 1, (28, 48, 24)), ("6", "Таран плечем", 1, (24, 50, 26)),
            ("7", "Підйом і удар об килим", 2, (38, 42, 20))]
    x0, y0, bw, bh, gap = 96, 866, 240, 86, 8
    for i, (k, nm, cost, ch) in enumerate(acts):
        x = x0 + i * (bw + gap)
        cv.rect(x, y0, bw, bh, INK, outline=PAPER, ow=4, shadow=INK, sh=(8, 8))
        cv.rect(x, y0, 54, bh, PAPER)
        cv.text(k, x + 27, y0 + bh / 2, "display900", 46, INK, anchor="cm")
        for li, ln in enumerate(wrap2(nm)):
            cv.text(ln, x + 64, y0 + 19 + li * 26, "display800", 24, PAPER, anchor="lm", maxw=bw - 84)
        bx, bwid = x + 64, bw - 74
        tot = sum(ch)
        cv.rect(bx, y0 + bh - 20, bwid * ch[0] / tot, 10, SIG)
        cv.rect(bx + bwid * ch[0] / tot, y0 + bh - 20, bwid * ch[1] / tot, 10, PAPER)
        cv.rect(bx + bwid * (ch[0] + ch[1]) / tot, y0 + bh - 20, bwid * ch[2] / tot, 10, GOLD)
        for c in range(cost):
            cv.circle(x + bw - 14 - c * 20, y0 + 16, 6, fill=GOLD)
    row2 = [("8", "Бульдозер", 2), ("9", "Вийти з рингу", 1), ("0", "Відновитися", 1)]
    y1 = y0 + bh + 10
    h2 = 62
    for i, (k, nm, cost) in enumerate(row2):
        x = x0 + i * (bw + gap)
        foc = i == 0
        cv.rect(x, y1, bw, h2, GOLD if foc else INK, outline=INK if foc else PAPER, ow=4, shadow=INK, sh=(8, 8))
        cv.rect(x, y1, 54, h2, INK if foc else PAPER)
        cv.text(k, x + 27, y1 + h2 / 2, "display900", 40, GOLD if foc else INK, anchor="cm")
        cv.text(nm.upper(), x + 64, y1 + h2 / 2, "display800", 22, INK if foc else PAPER, anchor="lm", maxw=bw - 100)
    cv.slab(x0 + 3 * (bw + gap), y1, 330, h2, GOLD, "r", outline=INK, ow=4, shadow=INK, sh=(8, 8))
    cv.text("ЗАВЕРШИТИ ХІД", x0 + 3 * (bw + gap) + 30, y1 + h2 / 2, "display900", 34, INK, anchor="lm", tracking=0.03)
    # виноска суперника (темна пластина із сигнал-обводкою; FX-02)
    callout(cv, "rival", 1600, 470, 0.72)
    return cv.done()


# ---------------------------------------------------------------- виноски

def callout(cv, kind, cx, cy, s=1.0, text=None, cap=None):
    def P(dx, dy):
        return (cx + dx * s, cy + dy * s)

    if kind in ("player", "rival", "two", "def"):
        pts = [P(-380, -70), P(420, -96), P(390, 84), P(-410, 104)]
        fill, ol, tc = {"player": (GOLD, INK, INK), "rival": (INK, SIG, PAPER), "two": (CRIM, INK, PAPER), "def": (SLATE, FROST, FROST)}[kind]
        cv.poly(pts, fill, outline=ol, ow=int(7 * s), shadow=INK if kind != "rival" else INK, sh=(14 * s, 14 * s))
        t = text or "ЧИСТО!"
        cv.text(t, cx + 8 * s, cy - 2 * s, "display900", 150 * s, tc, anchor="cm", slant=12, rot=2.5, maxw=700 * s,
                shadow=None)
        cap = cap if cap is not None else ("ТИ · УДАР · 10" if kind == "player" else "СУПЕРНИК · УДАР · 10")
        cpts = [P(-400, 124), P(120, 120), P(96, 176), P(-424, 180)]
        cv.poly(cpts, INK, outline=SIG if kind == "rival" else PAPER, ow=int(5 * s), shadow=INK, sh=(10 * s, 10 * s))
        cv.text(cap, cx - 380 * s, cy + 150 * s, "display800", 30 * s, SIG if kind == "rival" else PAPER, anchor="lm", tracking=0.08)
    elif kind == "finisher":
        cv.sprite("star8", cx, cy, 900 * s, 900 * s, tint=CRIM, a=1.0, rot=8)
        cv.sprite("star8", cx + 10 * s, cy + 12 * s, 640 * s, 640 * s, tint=INK, a=1.0, rot=-14)
        cv.text(text or "ФІНІШЕР!", cx, cy, "display900", 230 * s, PAPER, anchor="cm", slant=12, rot=2, shadow=CRIM, sdx=16 * s, sdy=16 * s,
                outline=INK, ow=8 * s)
    elif kind == "win":
        cv.rect(cx - 1100, cy - 150 * s, 2200, 300 * s, GOLD, outline=INK, ow=8)
        cv.rect(cx - 1100, cy - 190 * s, 2200, 22, CRIM)
        cv.rect(cx - 1100, cy + 168 * s, 2200, 22, CRIM)
        cv.text(text or "ПЕРЕМОГА", cx, cy, "display900", 200 * s, INK, anchor="cm", slant=12, tracking=0.02)
    else:
        raise ValueError(kind)


def callouts():
    cells = [("player", "ЧИСТО!", None), ("rival", "ЧИСТО!", None), ("two", "НА ДВА!", "РАЗ… ДВА…"),
             ("finisher", "ФІНІШЕР!", None), ("win", "ПЕРЕМОГА", None), ("def", "ПОРАЗКА", "СЕЗОН 1 · ТИЖДЕНЬ 1")]
    sheet = Image.new("RGB", (W, H), (36, 34, 42))
    cw, ch = 954, 354
    for i, (kind, t, c) in enumerate(cells):
        cv = Cv(INK, w=cw, h=ch)
        cv.sprite("halftone_r", cw * 0.30, ch * 0.78, cw * 1.2, ch * 0.9, tint=INK3, a=0.55)
        callout(cv, kind, cw / 2, ch / 2 - 6, 0.62 if kind not in ("finisher", "win") else 0.50, text=t, cap=c if kind in ("two", "def") else None)
        sheet.paste(cv.done(), (6 + (i % 2) * (cw + 6), 6 + (i // 2) * (ch + 6)))
    return sheet


if __name__ == "__main__":
    utf8()
    what = sys.argv[1] if len(sys.argv) > 1 else "all"
    out = Path(sys.argv[2]) if len(sys.argv) > 2 else Path(".")
    out.mkdir(parents=True, exist_ok=True)
    if what in ("title", "all"):
        title().save(out / "brutal_title.jpg", quality=90)
    if what in ("hud", "all"):
        hud().save(out / "brutal_hud.jpg", quality=90)
    if what in ("callouts", "all"):
        callouts().save(out / "brutal_callouts.jpg", quality=90)
    print("ok", what)
