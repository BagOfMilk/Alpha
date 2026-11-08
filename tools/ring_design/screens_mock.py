"""Ескізи екранів кар'єри в напрямі А «чистий AA» — підсумок вечора й створення борця (B03 v2). Каркас, що й у хабу тижня і HUD матчу:
стрічка гравця | табличка в центрі | стрічка суперника вгорі; афіша/квитанція на папері — єдина паперова поверхня; золота плита внизу праворуч; канати над рядом.

Кадри сцени — з білду (docs/ring/design/ref/result_before_2026-11-06.webp, create_before_2026-11-06.webp): стару панель допомальовано темрявою,
світло й контури — імітація. UI — лише токени й шрифти Статуту (кольори костюма — дані гри CareerLooks, не палітра інтерфейсу, COL-07).
Запуск (з tools/):  python -m ring_design.screens_mock result|create|result_states|create_states|result_ba|create_ba [вихід.jpg]
"""
import math
import sys

import numpy as np
from PIL import Image

from .brutal_mock import TAN, H, W, col
from .hub_mock import (ASH, COB, COBT, CRIM, DIM, GOLD, INK, INK1, INK2, INK3, PAPER, ROSE, SH, SIG, Hub, doll, label, meter, ropes,
                       strap)
from .titantron import render as R
from .titantron.common import ROOT, utf8

REF = ROOT / "docs/ring/design/ref"
SLATE, FROST = col("slate"), col("frost")


# ---------------------------------------------------------------- сцена

def _smooth(t):
    return t * t * (3 - 2 * t)


def grade(img, spot, rims, left_scrim=None, right_scrim=None, bottom=(780, 0.6, 280)):
    """Світло рампи, віньєтка, кольорові контури сторін, темрява там, де стояла стара панель, темніший низ — усе імітація."""
    f = np.asarray(img).astype(np.float32) / 255.0
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
    ink = np.array(INK, np.float32) / 255.0
    lum = f @ np.array([0.2126, 0.7152, 0.0722], np.float32)
    f = f ** 1.12
    sh = (0.40 * (1 - lum) ** 2)[..., None]
    f = f * (1 - sh) + ink * sh
    cx, cy = spot
    r = np.sqrt(((xx - cx) / (0.62 * W)) ** 2 + ((yy - cy) / (0.74 * H)) ** 2)
    v = 1.0 - 0.80 * np.clip((r - 0.30) / 0.80, 0, 1) ** 1.25
    rf = np.sqrt(((xx - cx) / (0.34 * W)) ** 2 + ((yy - cy) / (0.50 * H)) ** 2)
    f = f * (0.55 * v)[..., None] * (1.0 + 1.7 * np.exp(-(rf / 0.95) ** 2))[..., None]
    f[..., 0] *= 1.0 + 0.08 * np.exp(-(rf / 1.0) ** 2)
    for name, edge, k in rims:
        t = np.clip(1.0 - np.abs(xx / W - edge) / 0.30, 0, 1) ** 1.6
        tint = np.array(col(name), np.float32) / 255.0
        f = 1 - (1 - f) * (1 - (t * k)[..., None] * tint)
    if left_scrim:
        x0, x1, a = left_scrim
        t = _smooth(np.clip((x1 - xx) / (x1 - x0), 0, 1)) * a
        f = f * (1 - t[..., None]) + ink * t[..., None]
    if right_scrim:
        x0, x1, a = right_scrim
        t = _smooth(np.clip((xx - x0) / (x1 - x0), 0, 1)) * a
        f = f * (1 - t[..., None]) + ink * t[..., None]
    y0, a, ln = bottom
    t = (np.clip((yy - y0) / float(ln), 0, 1) ** 1.2)[..., None] * a
    f = f * (1 - t) + ink * 0.06 * t
    return Image.fromarray((np.clip(f, 0, 1) * 255).astype(np.uint8))


def scene_result():
    src = Image.open(REF / "result_before_2026-11-06.webp").convert("RGB")          # 1600×900
    big = src.resize((2112, 1188), Image.LANCZOS)
    cv = Image.new("RGB", (W, H), INK)
    cv.paste(big, (-190, -40))                                                       # переможець і суддя між колонками
    return grade(cv, (900, 520), (("cobalt", 0.0, 0.24), ("cobalt", 1.0, 0.20)), left_scrim=(400, 650, 1.0), bottom=(790, 0.55, 280))


def scene_create():
    src = Image.open(REF / "create_before_2026-11-06.webp").convert("RGB")
    big = src.resize((W, H), Image.LANCZOS)
    cv = Image.new("RGB", (W, H), INK)
    cv.paste(big, (0, 0))
    return grade(cv, (950, 430), (("cobalt", 0.0, 0.26), ("crimson", 1.0, 0.30)), right_scrim=(1120, 1320, 1.0), bottom=(690, 0.93, 130))


# ---------------------------------------------------------------- прості форми й вимір тексту

def tri(cv, cx, cy, up=True, s=9, color=INK):
    pts = [(cx - s, cy + s * 0.7), (cx + s, cy + s * 0.7), (cx, cy - s * 0.9)] if up else [(cx - s, cy - s * 0.7), (cx + s, cy - s * 0.7), (cx, cy + s * 0.9)]
    cv._fill_poly(pts, color)


def tri_h(cv, cx, cy, right=True, s=11, color=PAPER):
    sg = 1 if right else -1
    cv._fill_poly([(cx - sg * s * 0.6, cy - s), (cx + sg * s * 0.8, cy), (cx - sg * s * 0.6, cy + s)], color)


def thick_line(cv, p, q, t, color):
    dx, dy = q[0] - p[0], q[1] - p[1]
    ln = math.hypot(dx, dy)
    nx, ny = -dy / ln * t / 2, dx / ln * t / 2
    cv._fill_poly([(p[0] + nx, p[1] + ny), (q[0] + nx, q[1] + ny), (q[0] - nx, q[1] - ny), (p[0] - nx, p[1] - ny)], color)


def check(cv, cx, cy, color=INK):
    thick_line(cv, (cx - 10, cy), (cx - 3, cy + 8), 5, color)
    thick_line(cv, (cx - 3, cy + 8), (cx + 11, cy - 9), 5, color)


def arrow_r(cv, x, y, w=28, color=INK):
    cv.rect(x, y - 2, w - 10, 4, color)
    cv._fill_poly([(x + w - 14, y - 9), (x + w, y), (x + w - 14, y + 9)], color)


def wrap(text, px, maxw, fkey="text"):
    f = R._font(fkey, px)
    words, lines, cur = text.split(), [], ""
    for w_ in words:
        t = (cur + " " + w_).strip()
        if f.getlength(t) <= maxw or not cur:
            cur = t
        else:
            lines.append(cur)
            cur = w_
    if cur:
        lines.append(cur)
    return lines


def tear_edge(x, y, w, h, o, torn=True):
    """Нижній край паперу: рваний (щабель «спортзал») або прямий."""
    if torn:
        edge = [(x + w - i * 24, y + h + (5 if i % 2 else -3)) for i in range(0, int(w // 24) + 1)]
    else:
        edge = [(x + w, y + h)]
    if h > 150:
        bh = 120
        return [(x + TAN * bh, y), (x + w, y)] + edge + [(x, y + h), (x, y + bh)]
    return [(x + o, y), (x + w, y)] + edge + [(x, y + h)]


def cta(cv, lines, x=1496, y=892, w=328, h=134, px=48, key="ENTER"):
    """Золота плита головної дії — завжди внизу праворуч (м'язова пам'ять: хаб, підсумок, створення, матч)."""
    cv.slab(x, y, w, h, GOLD, "l", shadow=INK, sh=SH)
    n = len(lines)
    for i, ln in enumerate(lines):
        cv.text(ln, x + 54, y + (h - 26) / 2 + (i - (n - 1) / 2) * px * 0.92, "display900", px, INK, anchor="lm")
    cv.text(key, x + w - 36, y + h - 17, "text", 18, INK, anchor="rm", tracking=0.14)


# ---------------------------------------------------------------- спільні стрічки

def opp_strap(cv, nick, real, accent, role, gimmick, feud=(0, 4), x=1332, y=54):
    w, h = 492, 127
    strap(cv, x, y, w, h, accent, False)
    cv.rect(x + w - 8, y, 8, h, accent)
    rx = x + w - 34
    cv.text(nick, rx, y + 36, "display900", 50, PAPER, anchor="rm")
    tw, _ = label(cv, role[0], x + 64, y + 26, "lm", role[1])
    label(cv, "·  " + gimmick, x + 64 + tw + 10, y + 26, "lm", DIM)
    label(cv, real, rx, y + 78, "rm", DIM)
    f, n = feud
    cv.text("%d/%d" % (f, n), rx, y + 107, "display900", 26, PAPER, anchor="rm")
    meter(cv, rx - 56 - n * 36 + 4, y + 100, n, f, SIG, w=32)
    label(cv, "ВОРОЖНЕЧА", rx - 56 - n * 36 - 12, y + 107, "rm")


def player_strap(cv, x=96, y=54, accent=COB, role=("ГЕРОЙ", COBT), name="НОВАЧОК", gimmick="МОНСТР", head=0, torso=0):
    w, h = 492, 127
    strap(cv, x, y, w, h, accent, True)
    cv.rect(x, y, 8, h, accent)
    cv.text(name, x + 30, y + 36, "display900", 50, PAPER, anchor="lm")
    tw, _ = label(cv, role[0], x + w - 40, y + 26, "rm", role[1])
    label(cv, gimmick + "  ·", x + w - 40 - tw - 10, y + 26, "rm", DIM)
    for i, (nm, v) in enumerate((("ГОЛОВА", head), ("КОРПУС", torso))):
        yy = y + 78 + i * 29
        label(cv, nm, x + 30, yy)
        meter(cv, x + 150, yy - 7, 3, v, SIG)
        cv.text("%d/3" % v, x + 150 + 3 * 26 + 12, yy, "display900", 26, PAPER, anchor="lm")
    doll(cv, x + 396, y + 50, head=head, torso=torso)


# ---------------------------------------------------------------- підсумок вечора

def outcome_plate(cv, rounds=4, hype=10, method="УТРИМАННЯ"):
    """Центр угорі — те саме місце, що «Раунд» у матчі й «Тиждень» у хабі: спосіб перемоги, раунди, ажіотаж (10/10 — золото, COL-02)."""
    x, y, w, h = 750, 54, 420, 84
    cv.slab(x, y, w, h, PAPER, "both", shadow=INK, sh=SH)
    cv.text(method, x + 52, y + 42, "display800", 27, INK, anchor="lm", tracking=0.05)
    cv.text(str(rounds), x + 232, y + 42, "display900", 74, INK, anchor="lm", slant=12)
    label(cv, "РАУНДІВ", x + 284, y + 42, "lm", INK)
    cv.slab(x + 20, y + 98, w - 40, 44, INK1, "both", shadow=INK, sh=(5, 5))
    label(cv, "АЖІОТАЖ", x + 46, y + 120)
    meter(cv, x + 160, y + 113, 10, hype, GOLD if hype >= 10 else PAPER, w=11, h=15, gap=4)
    cv.text("%d/10" % hype, x + 318, y + 120, "display900", 26, PAPER, anchor="lm")


def verdict_plate(cv, kind="win", x=96, y=195):
    """Слово-подія результату (T5 у спокійнішому вигляді): перемога — золото з кармінною тінню (як стингер); поразка — холодна, без свята (FX-07)."""
    w, h = 492, 104
    if kind == "win":
        cv.slab(x, y, w, h, GOLD, "r", shadow=CRIM, sh=SH)
        cv.text("ПЕРЕМОГА", x + 30, y + h / 2, "display900", 84, INK, anchor="lm")
    elif kind == "loss":
        cv.slab(x, y, w, h, SLATE, "r", shadow=INK, sh=SH)
        cv.text("ПОРАЗКА", x + 30, y + h / 2, "display900", 84, FROST, anchor="lm")
    else:
        cv.slab(x, y, w, h, INK2, "r", outline=PAPER, ow=3, shadow=INK, sh=SH)
        cv.text("НІЧИЯ", x + 30, y + h / 2, "display900", 84, PAPER, anchor="lm")


def _marker(cv, x, y, good=True):
    cv.rect(x, y - 6, 12, 12, GOLD if good else SIG, outline=INK, ow=2)


def receipt(cv, x=96, y=318, torn=True, week=1, fee=30, fee_sub="ставка 20 + бонуси 10", rows=None, injury=None):
    """Квитанція вечора — єдина паперова поверхня екрана. Дельти: маркер (золото — перевага, сигнал — втрата) + стрілка-форма ▲/▼ + числа."""
    w = 492
    rows = rows or [("delta", "ПОПУЛЯРНІСТЬ", 3, 4), ("delta", "ДОВІРА БУКЕРА", 2, 3), ("plus", "ОЧКИ РОЗВИТКУ", "+4", "усього 6"), ("ok", "ПЛАН БУКЕРА", "виконано")]
    h = 172 + 54 * len(rows) + 8
    o = TAN * h
    cv.poly(tear_edge(x, y, w, h, o, torn), PAPER, shadow=INK, sh=SH)
    cv.text("ПІДСУМОК ВЕЧОРА", x + 56, y + 36, "display800", 30, INK, anchor="lm", tracking=0.06)
    label(cv, "ТИЖДЕНЬ %d" % week, x + w - 36, y + 36, "rm", ASH)
    _marker(cv, x + 56, y + 90, fee >= 0)
    label(cv, "ГОНОРАР", x + 80, y + 82, "lm", ASH)
    cw, _ = cv.text(str(fee), x + w - 36, y + 94, "display900", 54, INK, anchor="rm")
    cv.text("₴", x + w - 36 - cw - 6, y + 94, "text", 30, INK, anchor="rm")
    cv.text(fee_sub, x + 80, y + 112, "text", 18, INK, anchor="lm", maxw=w - 200)
    cv.rect(x + 56, y + 142, w - 92, 2, INK)
    for i, r in enumerate(rows):
        yy = y + 190 + i * 54
        kind = r[0]
        if kind == "delta":
            _, nm, a, b = r
            good = b >= a
            _marker(cv, x + 56, yy, good)
            label(cv, nm, x + 80, yy, "lm", ASH)
            cx = x + w - 36
            tri(cv, cx - 10, yy, up=good, s=10, color=INK)
            w1, _ = cv.text(str(b), cx - 34, yy, "display900", 38, INK, anchor="rm")
            arrow_r(cv, cx - 34 - w1 - 14 - 28, yy, 28, INK)
            cv.text(str(a), cx - 34 - w1 - 14 - 28 - 10, yy, "display900", 38, INK, anchor="rm")
        elif kind == "plus":
            _, nm, val, sub = r
            _marker(cv, x + 56, yy, True)
            label(cv, nm, x + 80, yy - 9, "lm", ASH)
            cv.text(sub, x + 80, yy + 14, "text", 18, INK, anchor="lm")
            cv.text(val, x + w - 36, yy, "display900", 38, INK, anchor="rm")
        else:
            nm, val = r[1], r[2]
            done = r[3] if len(r) > 3 else True
            _marker(cv, x + 56, yy, done)
            label(cv, nm, x + 80, yy, "lm", ASH)
            tw, _ = cv.text(val, x + w - 36, yy, "text", 18, INK, anchor="rm")
            if done:
                check(cv, x + w - 36 - tw - 22, yy, INK)
            else:
                thick_line(cv, (x + w - 36 - tw - 32, yy - 9), (x + w - 36 - tw - 14, yy + 9), 5, INK)
                thick_line(cv, (x + w - 36 - tw - 32, yy + 9), (x + w - 36 - tw - 14, yy - 9), 5, INK)
    if injury:   # наліпка — та сама мова, що в хабі: сигнал із текстом `Bg` (4,9:1)
        cv.slab(x + 44, y + h - 8, 420, 46, SIG, "r", outline=INK, ow=3, shadow=INK, sh=(5, 5))
        cv.text(injury, x + 66, y + h + 15, "display800", 25, INK, anchor="lm", tracking=0.04)
    return h


PERK_KINDS = {"perk": ("ПЕРК", None), "move": ("ПРИЙОМ", GOLD), "attr": ("ХАРАКТЕРИСТИКА", PAPER)}


def perk_card(cv, x, y, name, cost, desc, state="normal", w=492, kind="perk", path=COB):
    """Картка пропозиції: скіс ліворуч, смужка на зовнішньому краї — тип пропозиції (перк — колір шляху, прийом — `Gold`, характеристика — `Text`)
    + слово-мітка типу; вартість — золотий диск із числом."""
    focus, off = state == "focus", state == "off"
    tag, stripe = PERK_KINDS[kind]
    stripe = stripe or path
    lines = wrap(desc, 18, w - 130)
    h = 82 + 25 * len(lines) + 12 + (26 if off else 0)
    cv.slab(x, y, w, h, GOLD if focus else INK1, "l", shadow=INK, sh=SH)
    if not focus:
        cv.rect(x + w - 7, y, 7, h, ASH if off else stripe)
    tc = INK if focus else (ASH if off else PAPER)
    label(cv, tag, x + 44, y + 20, "lm", INK if focus else (ASH if off else DIM))
    cv.text(name.upper(), x + 44, y + 52, "display800", 27, tc, anchor="lm", tracking=0.02)
    cx, cy = x + w - 64, y + 40
    cv.circle(cx, cy, 20, fill=INK if focus else (ASH if off else GOLD))
    cv.text(str(cost), cx, cy, "display900", 26, GOLD if focus else INK, anchor="cm")
    for i, ln in enumerate(lines):
        cv.text(ln, x + 44, y + 90 + i * 25, "text", 18, INK if focus else (ASH if off else DIM), anchor="lm")
    if off:
        cv.text("бракує очок", x + 44, y + h - 22, "text", 18, SIG, anchor="lm")
    return h


def tab(cv, x, y, text, num, gold=False, w=188):
    cv.slab(x, y, w, 46, INK1, "r", outline=INK3, ow=3, shadow=INK, sh=(5, 5))
    if gold:
        cv.rect(x, y, 6, 46, GOLD)
    cv.text(text, x + 24, y + 23, "display800", 24, PAPER, anchor="lm", tracking=0.04)
    cv.text(str(num), x + w - 40, y + 23, "display900", 30, GOLD if gold else DIM, anchor="rm")


def dev_panel(cv, x=1332, y=195, points=6, total=19, cards=None):
    w, hh = 492, 108
    cv.slab(x, y, w, hh, INK1, "l", shadow=INK, sh=SH)
    cv.rect(x + w - 8, y, 8, hh, GOLD)
    cv.text("РОЗВИТОК", x + 52, y + 38, "display900", 40, PAPER, anchor="lm")
    nw, _ = cv.text(str(points), x + w - 40, y + 38, "display900", 58, GOLD, anchor="rm")
    label(cv, "ОЧКІВ", x + w - 40 - nw - 14, y + 44, "rm")
    label(cv, "РАДИМО ЗАРАЗ", x + 52, y + 84)
    cards = cards or [("Друге дихання", 1, "+1 до «Вирватися».", "normal", "perk"), ("Тик у вічі", 2, "Новий прийом для дії «Брудний прийом»: свій кліп, свій ажіотаж.", "normal", "move")]
    yy = y + hh + 16
    for nm, cost, desc, st, kd in cards:
        yy += perk_card(cv, x, yy, nm, cost, desc, st, kind=kd) + 16
    tab(cv, x + 30, yy + 4, "УСЕ", total)


def bottom_row(cv, lines, px=48, note=None):
    ropes(cv, 804)
    cta(cv, lines, px=px)
    if note:
        cv.text(note, 96, 866, "text", 18, DIM, anchor="lm")


def result(kind="win"):
    cv = Hub(img=scene_result())
    player_strap(cv)
    outcome_plate(cv)
    opp_strap(cv, "КОЛІБРІ", "МАТЕО РАМОС", COB, ("ГЕРОЙ", COBT), "ЛЕТУН")
    verdict_plate(cv, kind)
    receipt(cv)
    dev_panel(cv)
    bottom_row(cv, ["ДАЛІ"], px=84)
    return cv.done()


# ---------------------------------------------------------------- створення борця

def title_plate(cv):
    x, y, w, h = 750, 54, 420, 84
    cv.slab(x, y, w, h, PAPER, "both", shadow=INK, sh=SH)
    cv.text("СТВОРЕННЯ БОРЦЯ", x + w / 2, y + 36, "display900", 42, INK, anchor="cm")
    label(cv, "НОВА КАР'ЄРА · СЕЗОН 1", x + w / 2, y + 66, "cm", ASH)


def name_strap(cv, name="НОВАЧОК", role=("ГЕРОЙ", COBT), gimmick="МОНСТР", accent=COB, caret=True, x=96, y=54, placeholder="введіть ім'я"):
    """Стрічка гравця — і є поле вводу імені (було: біле поле, єдиний світлий елемент): `PanelRaised`, підкреслення й каретка `Gold`."""
    w, h = 492, 127
    strap(cv, x, y, w, h, accent, True)
    cv.rect(x, y, 8, h, accent)
    label(cv, "ІМ'Я НА РИНГУ", x + 30, y + 24, "lm", DIM)
    tw, _ = label(cv, role[0], x + w - 40, y + 24, "rm", role[1])
    label(cv, gimmick + "  ·", x + w - 40 - tw - 10, y + 24, "rm", DIM)
    fx, fy, fw, fh = x + 30, y + 42, w - 30 - 54, 64
    cv.rect(fx, fy, fw, fh, INK2)
    cv.rect(fx, fy + fh - 3, fw, 3, GOLD)
    if name:
        nw, _ = cv.text(name, fx + 18, fy + fh / 2 - 2, "display900", 50, PAPER, anchor="lm")
    else:
        nw = -6
        cv.text(placeholder, fx + 30, fy + fh / 2, "text", 24, DIM, anchor="lm")
    if caret:
        cv.rect(fx + 18 + nw + 6, fy + 12, 3, fh - 26, GOLD)


COSTUMES = [(66, 121, 208), (200, 36, 36), (46, 160, 70), (222, 175, 38), (130, 64, 190), (235, 235, 230), (40, 38, 46), (232, 112, 28)]   # дані гри (CareerLooks), знято з кадру збірки


def carousel(cv, x, y, w, name, value):
    cv.slab(x, y, w, 64, INK2, "r")
    cv.rect(x, y, 56, 64, INK3)
    tri_h(cv, x + 28, y + 32, right=False, s=11, color=PAPER)
    cv.rect(x + w - 56 - TAN * 64, y, 56, 64, INK3)
    tri_h(cv, x + w - 28 - TAN * 64, y + 32, right=True, s=11, color=PAPER)
    label(cv, name, x + 74, y + 21, "lm", DIM)
    cv.text(value, x + 74, y + 45, "text", 18, PAPER, anchor="lm", maxw=w - 190)


def look_plate(cv, x=96, y=195, sel=0):
    w, h = 492, 300
    cv.slab(x, y, w, h, INK1, "r", shadow=INK, sh=SH)
    cv.rect(x, y, 8, h, COB)
    cv.text("ВИГЛЯД", x + 30, y + 36, "display900", 34, PAPER, anchor="lm")
    label(cv, "КОЛІР КОСТЮМА", x + 30, y + 80)
    for i, c in enumerate(COSTUMES):
        sx = x + 30 + i * 52
        if i == sel:
            cv.rect(sx, y + 100, 44, 44, c, outline=PAPER, ow=3, shadow=INK, sh=(4, 4))
        else:
            cv.rect(sx, y + 100, 44, 44, c, outline=INK3, ow=2)
    carousel(cv, x + 30, y + 160, w - 30 - 56, "ТІЛО", "чоловік, українець(-ка)")
    carousel(cv, x + 30, y + 160 + 76, w - 30 - 56, "ЗАЧІСКА", "коротка")


ATTRS = [("СИЛА", 2), ("СПРИТНІСТЬ", -1), ("ТЕХНІКА", 0), ("ВИТРИВАЛІСТЬ", 3), ("ХАРИЗМА", 0)]


ROLE_DESC = {"ГЕРОЙ": "Ажіотаж за чесну боротьбу, витримані утримання й «продати» прийом. Брудний прийом коштує −2.",
             "ЛИХОДІЙ": "Ажіотаж за брудні прийоми без покарання й за дражніння. Чиста поразка коштує менше."}


def build_bill(cv, x=1332, y=54, gimmick="МОНСТР", role=("ГЕРОЙ", COB, PAPER), attrs=ATTRS, free="ВИТРИВАЛІСТЬ", torn=True):
    """Афіша «Твій борець» — збірка в реальному часі + вибір вільного бала: рядок-характеристика натискається, вибраний — золотий блок."""
    w = 492
    desc = wrap(ROLE_DESC[role[0]], 18, w - 112)
    ex = 25 * len(desc) + 18
    h = 70 + 112 + 40 + 5 * 52 + 22 + ex
    o = TAN * h
    cv.poly(tear_edge(x, y, w, h, o, torn), PAPER, shadow=INK, sh=SH)
    cv.text("ТВІЙ БОРЕЦЬ", x + 56, y + 36, "display800", 30, INK, anchor="lm", tracking=0.06)
    cv.text(gimmick, x + 56, y + 108, "display900", 64, INK, anchor="lm")
    rt, rc, rtext = role
    cw = 150
    cv.slab(x + w - 36 - cw, y + 82, cw, 52, rc, "r")
    cv.text(rt, x + w - 36 - cw + 26, y + 108, "display800", 28, rtext, anchor="lm", tracking=0.06)
    for i, ln in enumerate(desc):
        cv.text(ln, x + 56, y + 162 + i * 25, "text", 18, INK, anchor="lm")
    cv.rect(x + 56, y + 150 + ex, w - 92, 2, INK)
    label(cv, "ВІЛЬНИЙ БАЛ +1", x + 56, y + 182 + ex, "lm", ASH)
    label(cv, "оберіть рядок", x + w - 36, y + 182 + ex, "rm", ASH)
    for i, (nm, v) in enumerate(attrs):
        yy = y + 226 + ex + i * 52
        sel = nm == free
        if sel:
            cv.rect(x + 40, yy - 22, w - 76 - TAN * 12, 44, INK, a=0.18)
            tri_h(cv, x + 52, yy, right=True, s=9, color=INK)
        label(cv, nm, x + 72, yy, "lm", INK if sel else ASH)
        bx = x + 252
        cv.rect(bx + 34, yy - 14, 2, 28, INK)
        seg_x = bx + 2
        neg_pts = [(seg_x + 3, yy - 9), (seg_x + 29, yy - 9), (seg_x + 26, yy + 9), (seg_x, yy + 9)]
        cv._fill_poly(neg_pts, SIG if v < 0 else INK, 1.0 if v < 0 else 0.18)
        for k in range(4):
            px_ = bx + 44 + k * 32
            on = (k < v)
            gold = sel and on and k == v - 1
            pts = [(px_ + 3, yy - 9), (px_ + 29, yy - 9), (px_ + 26, yy + 9), (px_, yy + 9)]
            if gold:
                cv.poly(pts, GOLD, outline=INK, ow=2)
            else:
                cv._fill_poly(pts, INK, 1.0 if on else 0.18)
        cv.text(("+%d" % v) if v > 0 else ("−%d" % -v if v < 0 else "0"), x + w - 36, yy, "display900", 32, INK, anchor="rm")
    return h


def choice_tile(cv, x, y, w, h, key, title, lines, selected=False, focus=False, accent=GOLD, sel_fill=GOLD, sel_text=INK, key_fill=GOLD, mark=None):
    """Плитка вибору (гімік/роль): вибрана — заливка + позначка ✓; у фокусі — зсув 8; невибрана — смужка на лівому краї."""
    if focus:
        y -= 8
    fill = sel_fill if selected else INK1
    tc = sel_text if selected else PAPER
    cv.slab(x, y, w, h, fill, "r", shadow=INK, sh=SH)
    if not selected:
        cv.rect(x, y, 7, h, accent)
    kx = x + w - 52
    cv.rect(kx, y + 12, 38, 38, INK if selected else key_fill)
    cv.text(key, kx + 19, y + 31, "display900", 28, key_fill if selected else INK, anchor="cm")
    cv.text(title, x + 24, y + 30, "display800", 27, tc, anchor="lm", tracking=0.02)
    for i, (ln, neg) in enumerate(lines):
        cv.text(ln, x + 24, y + 68 + i * 24, "text", 18, tc if selected else (SIG if neg else DIM), anchor="lm", maxw=w - 44)
    if mark:
        cv.text(mark, x + w - 40 - TAN * 40, y + h - 30, "display900", 64, tc, anchor="rm", a=0.18)
    if selected:
        check(cv, x + w - 30, y + h - 24, sel_text)


GIMMICKS = [("МОНСТР", [("Сила +2", False), ("Спритність −1", True), ("Витривалість +2", False)]),
            ("ЛЕТУН", [("Спритність +3", False), ("Витривалість −1", True)]),
            ("ТЕХНІК", [("Техніка +3", False)]),
            ("ШОУМЕН", [("Сила −1", True), ("Харизма +3", False)])]


def create_dock(cv, gimmick=0, role=0, focus=None):
    y0 = 838
    ropes(cv, 804)
    cv.text("ГІМІК", 96, y0 + 22, "display900", 32, PAPER, anchor="lm", tracking=0.03)
    cv.text("Не продає, а ламає. Сила й витривалість; слабкість — спритність.", 236, y0 + 22, "text", 18, PAPER, anchor="lm", maxw=740)
    cv.text("РОЛЬ", 96 + 4 * 230 + 30, y0 + 22, "display900", 32, PAPER, anchor="lm", tracking=0.03)
    for i, (t, ls) in enumerate(GIMMICKS):
        choice_tile(cv, 96 + i * 230, 892, 220, 134, str(i + 1), t, ls, selected=(i == gimmick), focus=(focus == i))
    choice_tile(cv, 96 + 4 * 230 + 30, 892, 220, 134, "5", "ГЕРОЙ", [], selected=(role == 0), accent=COB, sel_fill=COB, sel_text=PAPER, key_fill=PAPER)
    choice_tile(cv, 96 + 5 * 230 + 30, 892, 220, 134, "6", "ЛИХОДІЙ", [], selected=(role == 1), accent=CRIM, sel_fill=CRIM, sel_text=PAPER, key_fill=PAPER)
    cta(cv, ["ПОЧАТИ", "КАР'ЄРУ"], x=1496 + 30 - 30 + 0, y=892)


def create():
    cv = Hub(img=scene_create())
    name_strap(cv)
    look_plate(cv)
    bh = build_bill(cv)
    tab(cv, 1332 + 30, 54 + bh + 30, "ПРИЙОМИ", 10, w=240)
    create_dock(cv)
    return cv.done()


# ---------------------------------------------------------------- сторінки станів і «до/після»

def result_states():
    cv = Hub(w=1920, h=1180)
    cv.text("ПІДСУМОК ВЕЧОРА · СТАНИ", 96, 70, "display900", 40, PAPER, anchor="lm", tracking=0.03)
    label(cv, "ескіз B03 v2 · слово-подія 492×104, квитанція 492×(180+54·рядків), картка перка 492×(66+25·рядків) @1080p", 96, 112, "lm", DIM)
    for i, (k, cap) in enumerate((("win", "перемога: золото + кармінна тінь"), ("loss", "поразка: холодна пластина, без свята"), ("draw", "нічия: нейтральна"))):
        x = 96 + i * 580
        verdict_plate(cv, k, x, 150)
        label(cv, cap, x, 150 + 104 + 40, "lm", DIM)
    # квитанції: виграш і поразка з травмою
    receipt(cv, 96, 340, torn=True, week=1)
    label(cv, "виграш (спортзал: рваний край)", 96, 340 + 396 + 40, "lm", DIM)
    receipt(cv, 680, 340, torn=False, week=2, fee=10, fee_sub="ставка 20 − штраф 10",
            rows=[("delta", "ПОПУЛЯРНІСТЬ", 5, 4), ("delta", "ДОВІРА БУКЕРА", 4, 3), ("plus", "ОЧКИ РОЗВИТКУ", "+1", "усього 7"), ("ok", "ПЛАН БУКЕРА", "не виконано", False)],
            injury="ТРАВМА · КОРПУС · 4 ТИЖ.")
    label(cv, "поразка з травмою (Палац: чистий край)", 680, 340 + 396 + 40 + 26, "lm", DIM)
    # картки перків
    h1 = perk_card(cv, 1264, 340, "Друге дихання", 1, "+1 до «Вирватися».", "normal", kind="perk")
    label(cv, "перк: смужка кольору шляху + мітка «ПЕРК»", 1264, 340 + h1 + 26, "lm", DIM)
    h2 = perk_card(cv, 1264, 340 + 190, "Тик у вічі", 2, "Новий прийом для дії «Брудний прийом»: свій кліп, свій ажіотаж.", "focus", kind="move")
    label(cv, "прийом у фокусі: золота заливка", 1264, 340 + 190 + h2 + 26, "lm", DIM)
    h3 = perk_card(cv, 1264, 340 + 420, "Сила", 4, "+1 до характеристики.", "off", kind="attr")
    label(cv, "характеристика, недоступна: бракує очок", 1264, 340 + 420 + h3 + 26, "lm", DIM)
    return cv.done()


def create_states():
    cv = Hub(w=1920, h=1200)
    cv.text("СТВОРЕННЯ БОРЦЯ · СТАНИ", 96, 70, "display900", 40, PAPER, anchor="lm", tracking=0.03)
    label(cv, "ескіз B03 v2 · плитки вибору 220×134, стрічка-поле 492×127; кольори костюма — дані гри, не палітра інтерфейсу", 96, 112, "lm", DIM)
    caps = ["вибрано: заливка + галочка", "не вибрано: смужка", "у фокусі: зсув 8", "роль вибрана: герой", "роль не вибрана: лиходій"]
    choice_tile(cv, 96, 168, 220, 134, "1", "МОНСТР", GIMMICKS[0][1], selected=True)
    choice_tile(cv, 96 + 360, 168, 220, 134, "2", "ЛЕТУН", GIMMICKS[1][1])
    choice_tile(cv, 96 + 720, 168, 220, 134, "3", "ТЕХНІК", GIMMICKS[2][1], focus=True)
    choice_tile(cv, 96 + 1080, 168, 220, 134, "5", "ГЕРОЙ", [], selected=True, accent=COB, sel_fill=COB, sel_text=PAPER, key_fill=PAPER)
    choice_tile(cv, 96 + 1440, 168, 220, 134, "6", "ЛИХОДІЙ", [], accent=CRIM, sel_fill=CRIM, sel_text=PAPER, key_fill=PAPER)
    for i, c in enumerate(caps):
        label(cv, c, 96 + i * 360, 334, "lm", DIM)
    name_strap(cv, "НОВАЧОК", x=96, y=400)
    label(cv, "герой: кобальт, ім'я введено", 96, 400 + 127 + 36, "lm", DIM)
    name_strap(cv, "ЧОРНИЙ ВОРОН", role=("ЛИХОДІЙ", SIG), gimmick="ТЕХНІК", accent=CRIM, x=696, y=400)
    label(cv, "лиходій: кармін і слово ролі — наживо", 696, 400 + 127 + 36, "lm", DIM)
    name_strap(cv, "", x=1296, y=400)
    label(cv, "порожньо: заповнювач із ключа", 1296, 400 + 127 + 36, "lm", DIM)
    # афіша зі збіркою (Технік · Лиходій, вільний бал у Силу; Палац: чистий край) і «Вигляд» з іншим костюмом
    h1 = build_bill(cv, 96, 640, gimmick="ТЕХНІК", role=("ЛИХОДІЙ", CRIM, PAPER),
                    attrs=[("СИЛА", 1), ("СПРИТНІСТЬ", 0), ("ТЕХНІКА", 3), ("ВИТРИВАЛІСТЬ", 0), ("ХАРИЗМА", 0)], free="СИЛА", torn=False)
    label(cv, "афіша збірки: Технік · Лиходій, вільний бал у «Силу» (Палац: чистий край)", 96, 640 + h1 + 40, "lm", DIM)
    look_plate(cv, 696, 640, sel=1)
    label(cv, "«Вигляд»: вибрано червоний костюм (кольори — дані гри)", 696, 640 + 300 + 40, "lm", DIM)
    return cv.done()


def before_after(before, after, cap_b, cap_a, size=(1600, 900)):
    old = Image.open(REF / before).convert("RGB").resize(size, Image.LANCZOS)
    new = after.resize(size, Image.LANCZOS)
    base = Image.new("RGB", (size[0], size[1] * 2 + 200), INK)
    base.paste(old, (0, 100))
    base.paste(new, (0, size[1] + 200))
    cv = Hub(img=base, w=size[0], h=size[1] * 2 + 200)
    cv.text(cap_b, 40, 50, "display800", 34, PAPER, anchor="lm", tracking=0.05)
    cv.text(cap_a, 40, size[1] + 150, "display800", 34, PAPER, anchor="lm", tracking=0.05)
    return cv.done()


if __name__ == "__main__":
    utf8()
    mode = sys.argv[1] if len(sys.argv) > 1 else "result"
    out = sys.argv[2] if len(sys.argv) > 2 else mode + ".jpg"
    if mode == "result_ba":
        img = before_after("result_before_2026-11-06.webp", result(), "ДО · ЗБІРКА 06.11.2026, КАДР 106", "ПІСЛЯ · ЕСКІЗ B03 V2 (СВІТЛО Й КАДР БОРЦІВ — ІМІТАЦІЯ)")
    elif mode == "create_ba":
        img = before_after("create_before_2026-11-06.webp", create(), "ДО · ЗБІРКА 06.11.2026, КАДР 012", "ПІСЛЯ · ЕСКІЗ B03 V2 (СВІТЛО Й КАДР БОРЦІВ — ІМІТАЦІЯ)")
    else:
        img = {"result": result, "create": create, "result_states": result_states, "create_states": create_states}[mode]()
    img.save(out, quality=90)
    print("ok", out)
