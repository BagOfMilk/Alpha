"""Ескіз екрана тижня (хаб кар'єри), напрям А «чистий AA»: той самий каркас, що й у HUD матчу.

Каркас: угорі — стрічка гравця | табличка тижня | стрічка суперника (як «Раунд» у матчі); внизу — ряд дій і золота плита завершення
(як у HUD матчу). Нове лише для хабу: афіша-«білет» вечора (папір), метри травм, лампочки дій тижня.
Кадр сцени — з білду (docs/ring/design/ref/week_before_2026-11-06.webp): стару панель праворуч «дописано» стіною, світло — імітація.
UI — лише токени й шрифти Статуту. Запуск (з tools/):  python -m ring_design.hub_mock [вихід.jpg]
"""
import sys

import numpy as np
from PIL import Image

from .brutal_mock import TAN, Cv, H, W, col
from .titantron import render as R
from .titantron.common import ROOT, utf8

SRC = ROOT / "docs/ring/design/ref/week_before_2026-11-06.webp"
INK, INK1, INK2, INK3 = col("ink0"), col("ink1"), col("ink2"), col("ink3")
PAPER, DIM, ASH = col("paper"), col("dim"), col("ash")
GOLD, CRIM, SIG, COB, COBT, ROSE = col("gold"), col("crimson"), col("signal"), col("cobalt"), col("cobaltText"), col("rose")
SH = (7, 7)                                                  # shadowOffset 6 @900 → ≈7 @1080


class Hub(Cv):
    """Cv, у якому текст якориться за справжнім (тісним) габаритом літер, а не за кеглевою коробкою."""

    def text(self, s, x, y, fkey, px, fill, anchor="lm", slant=0, shadow=None, sdx=0, sdy=0, tracking=0.0, maxw=None, a=1.0):
        ss = self.ss
        im = R._text_raw(s, fkey, int(px * ss), fill, shadow, int(sdx * ss), int(sdy * ss), None, 0, slant, tracking)
        bb = im.getbbox()
        if maxw and bb and (bb[2] - bb[0]) > maxw * ss:
            k = maxw * ss / (bb[2] - bb[0])
            im = R._text_raw(s, fkey, max(8, int(px * ss * k)), fill, shadow, int(sdx * ss), int(sdy * ss), None, 0, slant, tracking)
            bb = im.getbbox()
        im = im.crop(bb)
        if a < 1:
            im = im.copy()
            im.putalpha(im.getchannel("A").point(lambda v: int(v * a)))
        w, h = im.size
        ax = {"l": 0, "c": w / 2, "r": w}[anchor[0]]
        ay = {"t": 0, "m": h / 2, "b": h}[anchor[1]]
        R._paste(self.im, im, x * ss - ax + w / 2, y * ss - ay + h / 2)
        return w / ss, h / ss


# ---------------------------------------------------------------- сцена

def scene():
    """Кадр кімнати з білду: прибрано «Development Build», стару панель праворуч замінено стіною (шафки), світло й контури — імітація."""
    a = np.asarray(Image.open(SRC).convert("RGB")).copy()          # 1600×900
    a[872:900, 1470:1600] = a[842:870, 1470:1600]                    # збірковий напис
    strip = a[:, 872:956]                                            # одна чиста шафка без людей
    pp = np.concatenate([strip, strip[:, ::-1]], axis=1)
    right = np.tile(pp, (1, 8, 1))[:, :a.shape[1] + 120 - 1092]
    ref = a[:, 1092 - 40:1092].astype(np.float32).mean(axis=(1, 2))
    got = right[:, :40].astype(np.float32).mean(axis=(1, 2))
    gain = np.convolve(np.clip(ref / np.maximum(got, 1.0), 0.6, 1.8), np.ones(25) / 25, mode="same")
    right = np.clip(right.astype(np.float32) * gain[:, None, None], 0, 255).astype(np.uint8)
    # плавний шов: 36 px кросфейд між оригіналом і дописаною стіною
    n = 36
    wgt = np.linspace(0, 1, n)[None, :, None]
    seam = a[:, 1092 - n:1092].astype(np.float32) * (1 - wgt) + right[:, :n].astype(np.float32) * wgt
    a = np.concatenate([a[:, :1092 - n], seam.astype(np.uint8), right[:, n:]], axis=1)   # 1600+120 × 900
    big = Image.fromarray(a).resize((int(a.shape[1] * 1.2), int(900 * 1.2)), Image.LANCZOS)
    dx, dy = -38, -140                                               # пара борців по центру, ноги вище ряду дій
    cv = Image.new("RGB", (W, H), INK)
    cv.paste(big, (dx, dy))
    floor = big.crop((0, big.height - 140, W + 40, big.height)).transpose(Image.FLIP_TOP_BOTTOM)   # дзеркало підлоги
    cv.paste(floor.crop((38, 0, 38 + W, 140)), (0, H - 140))
    f = np.asarray(cv).astype(np.float32) / 255.0
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
    lum = f @ np.array([0.2126, 0.7152, 0.0722], np.float32)
    f = f ** 1.12
    ink = np.array(INK, np.float32) / 255.0
    sh = (0.40 * (1 - lum) ** 2)[..., None]
    f = f * (1 - sh) + ink * sh
    r = np.sqrt(((xx - 0.50 * W) / (0.62 * W)) ** 2 + ((yy - 0.52 * H) / (0.74 * H)) ** 2)
    v = 1.0 - 0.80 * np.clip((r - 0.30) / 0.80, 0, 1) ** 1.25
    rf = np.sqrt(((xx - 0.50 * W) / (0.34 * W)) ** 2 + ((yy - 0.50 * H) / (0.50 * H)) ** 2)
    f = f * (0.52 * v)[..., None] * (1.0 + 1.9 * np.exp(-(rf / 0.95) ** 2))[..., None]
    f[..., 0] *= 1.0 + 0.10 * np.exp(-(rf / 1.0) ** 2)               # тепле світло рампи
    # кольорові контури сторін: кобальт ліворуч, кармін праворуч (імітація світла)
    for edge, name, k, span in ((0.0, "cobalt", 0.26, 0.30), (1.0, "crimson", 0.30, 0.30)):
        t = np.clip(1.0 - np.abs(xx / W - edge) / span, 0, 1) ** 1.6
        tint = np.array(col(name), np.float32) / 255.0
        f = 1 - (1 - f) * (1 - (t * k)[..., None] * tint)
    # низ темніший — ряд дій має тло
    t = (np.clip((yy - 770) / 280.0, 0, 1) ** 1.2)[..., None]
    f = f * (1 - 0.72 * t) + ink * 0.06 * t
    return Image.fromarray((np.clip(f, 0, 1) * 255).astype(np.uint8))


# ---------------------------------------------------------------- елементи

def seg(cv, x, y, w, h, color):
    o = TAN * h
    cv._fill_poly([(x + o, y), (x + w + o, y), (x + w, y + h), (x, y + h)], color)


def meter(cv, x, y, n, filled, color, w=22, h=15, gap=4):
    for i in range(n):
        seg(cv, x + i * (w + gap), y, w, h, color if i < filled else INK3)
    return n * (w + gap) - gap


def money(cv, x, y, val, px, fill=PAPER):
    """₴ є лише у Fixel (TYPE-03): знак — text, цифри — display."""
    sw, _ = cv.text("₴", x, y, "text", px * 0.55, fill, anchor="lm")
    cw, _ = cv.text(str(val), x + sw + 6, y, "display900", px, fill, anchor="lm")
    return sw + 6 + cw


def strap(cv, x, y, w, h, accent, mine):
    """Стрічка сторони: гравець ліворуч (правий край скошений), суперник праворуч (лівий)."""
    return cv.slab(x, y, w, h, INK1, "r" if mine else "l", outline=accent, ow=4, shadow=INK, sh=SH)


def label(cv, s, x, y, anchor="lm", fill=DIM, px=18):
    return cv.text(s, x, y, "text", px, fill, anchor=anchor, tracking=0.14)


def player_strap(cv, x=96, y=54, accent=COB, role=("ГЕРОЙ", COBT), name="НОВАЧОК", gimmick="МОНСТР", head=2, torso=1):
    """Стрічка гравця: колір обводки й слово ролі — за роллю (герой/лиходій/бунтар), позиція ліворуч — «це ти»."""
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


def doll(cv, cx, top, head, torso):
    """Лялька травм: уражена зона — сигнал (форма + колір; рівень — у метрі поруч)."""
    cv.circle(cx, top + 9, 9, fill=SIG if head else INK3)
    cv._fill_poly([(cx - 13, top + 21), (cx + 13, top + 21), (cx + 9, top + 47), (cx - 9, top + 47)], SIG if torso else INK3)
    for sx in (-1, 1):
        cv._fill_poly([(cx + sx * 15, top + 22), (cx + sx * 20, top + 24), (cx + sx * 19, top + 46), (cx + sx * 14, top + 45)], INK3)
        cv._fill_poly([(cx + sx * 1, top + 49), (cx + sx * 9, top + 49), (cx + sx * 8, top + 74), (cx + sx * 2, top + 74)], INK3)


def resources_plate(cv):
    x, y, w, h = 96, 195, 492, 136
    cv.slab(x, y, w, h, INK1, "r", shadow=INK, sh=SH)
    cv.rect(x, y, 8, h, COB)
    mw = money(cv, x + 30, y + 34, 32, 52)
    label(cv, "ГРОШІ", x + 30 + mw + 16, y + 40)
    for i, (nm, n, f, val) in enumerate((("ПОПУЛЯРНІСТЬ", 10, 4, "4/10"), ("ДОВІРА БУКЕРА", 5, 3, "3/5"))):
        yy = y + 80 + i * 29
        label(cv, nm, x + 30, yy)
        bw = 12 if n == 10 else 26
        meter(cv, x + 232, yy - 7, n, f, PAPER, w=bw, h=15, gap=4)
        cv.text(val, x + 232 + n * (bw + 4) + 12, yy, "display900", 26, PAPER, anchor="lm")


def week_plate(cv):
    x, y, w, h = 750, 54, 420, 84
    cv.slab(x, y, w, h, PAPER, "both", shadow=INK, sh=SH)
    cv.text("ТИЖДЕНЬ", x + 52, y + 43, "display800", 27, INK, anchor="lm", tracking=0.05)
    cv.text("2", x + 168, y + 42, "display900", 74, INK, anchor="lm", slant=12)
    cv.text("/4", x + 212, y + 50, "display900", 32, ASH, anchor="lm")
    label(cv, "СЕЗОН 1", x + 270, y + 28, "lm", INK)
    for i in range(4):
        ox = x + 270 + i * 26
        o = TAN * 20
        pts = [(ox + o, y + 46), (ox + 16 + o, y + 46), (ox + 16, y + 66), (ox, y + 66)]
        if i < 2:
            cv._fill_poly(pts, INK)
        else:
            cv.poly(pts, PAPER, outline=INK, ow=2)


def opponent_strap(cv):
    x, y, w, h = 1332, 54, 492, 127
    strap(cv, x, y, w, h, CRIM, False)
    cv.rect(x + w - 8, y, 8, h, CRIM)
    rx = x + w - 34
    cv.text("ВДОВА", rx, y + 36, "display900", 50, PAPER, anchor="rm")
    tw, _ = label(cv, "ЛИХОДІЙ", x + 64, y + 26, "lm", SIG)
    label(cv, "·  ТЕХНІК", x + 64 + tw + 10, y + 26, "lm", DIM)
    label(cv, "МЕЙ ЛІН", rx, y + 78, "rm", DIM)
    cv.text("0/4", rx, y + 107, "display900", 26, PAPER, anchor="rm")
    meter(cv, rx - 56 - 4 * 36 + 4, y + 100, 4, 0, SIG, w=32)
    label(cv, "ВОРОЖНЕЧА", rx - 56 - 4 * 36 - 12, y + 107, "rm")


def bill(cv, x=1332, y=195, torn=True, rematch=False, threat=True, fee=20):
    """Афіша вечора — єдина «паперова» поверхня екрана (теза Статуту: афіша). Рваний край — щабель «спортзал», чистий — «Палац»."""
    w = 492
    h = 186 + (29 if rematch else 0)
    o = TAN * h
    if torn:
        edge = [(x + w - i * 24, y + h + (5 if i % 2 else -3)) for i in range(0, int(w // 24) + 1)]
    else:
        edge = [(x + w, y + h)]
    cv.poly([(x + o, y), (x + w, y)] + edge + [(x, y + h)], PAPER, shadow=INK, sh=SH)
    cv.text("ЩОТИЖНЕВЕ ШОУ", x + 56, y + 32, "display800", 30, INK, anchor="lm", tracking=0.06)
    label(cv, "СЕРЕДИНА КАРТКИ", x + 56, y + 66, "lm", ASH)
    label(cv, "ГОНОРАР", x + w - 36, y + 30, "rm", ASH)
    cw, _ = cv.text(str(fee), x + w - 36, y + 66, "display900", 54, INK, anchor="rm")
    cv.text("₴", x + w - 36 - cw - 6, y + 66, "text", 30, INK, anchor="rm")
    cv.rect(x + 56, y + 92, w - 92, 2, INK)
    cv.text("План букера: виграй будь-як · умов немає", x + 56, y + 116, "text", 18, INK, anchor="lm", maxw=w - 92)
    lines = [("Промо тижня: ажіотаж на старті +1", 145)]
    if rematch:
        lines.append(("Реванш: ти знаєш його слабкість", 174))
    for t, dy in lines:
        cv.rect(x + 56, y + dy - 6, 11, 11, GOLD, outline=INK, ow=2)
        cv.text(t, x + 80, y + dy, "text", 18, INK, anchor="lm", maxw=w - 120)
    if threat:   # наліпка, що свисає з афіші (сигнал = загроза гравцеві)
        cv.slab(x + 44, y + h - 8, 346, 46, SIG, "r", outline=INK, ow=3, shadow=INK, sh=(5, 5))
        cv.text("У ФОРМІ · СИЛЬНІШИЙ НА 1", x + 66, y + h + 15, "display800", 25, INK, anchor="lm", tracking=0.04)   # туш на сигналі 4,9:1 (папір дав би 3,1)


def versus(cv):
    cv.text("ПРОТИ", 960, 470, "display900", 70, PAPER, anchor="cm", slant=12, shadow=INK, sdx=6, sdy=6)


def tile(cv, x, y, w, h, key, title, effect, chance=None, focus=False, off=None):
    """Плитка дії. focus — золота заливка зі зсувом на 8 (фокус = заливка + зсув); off — причина недоступності (Disabled + причина)."""
    if focus:
        y -= 8
    fill = GOLD if focus else INK1
    ink_t = INK if focus else (ASH if off else PAPER)
    sub = INK if focus else (ASH if off else DIM)
    cv.slab(x, y, w, h, fill, "r", shadow=INK, sh=SH)
    if not focus:
        cv.rect(x, y, 7, h, ASH if off else GOLD)
    kx = x + w - 52
    cv.rect(kx, y + 12, 38, 38, INK if focus else (ASH if off else GOLD))
    cv.text(key, kx + 19, y + 31, "display900", 28, GOLD if focus else INK, anchor="cm")
    lines = title.upper().split("|")
    for i, ln in enumerate(lines):
        cv.text(ln, x + 24, y + 30 + i * 28, "display800", 27, ink_t, anchor="lm", tracking=0.02)
    ey = y + 68 + 28 * (len(lines) - 1)
    parts = effect.split("|")
    cv.text(parts[0], x + 24, ey, "text", 18, sub, anchor="lm", maxw=w - 44)
    if len(parts) > 1:
        cv.text(parts[1], x + 24, ey + 25, "text", 18, INK if focus else SIG, anchor="lm", maxw=w - 44)
    if off:
        cv.text(off, x + 24, ey + 25, "text", 18, SIG, anchor="lm", maxw=w - 44)
    if chance:
        tot = sum(chance)
        if focus:
            cv.slab(x + 10, y + 86, w - 34, h - 92, INK1, "r")
        bx, bw = x + 24, w - 76
        by = y + (96 if focus else 98)
        xs = [bx, bx + bw * chance[0] / tot, bx + bw * (chance[0] + chance[1]) / tot]
        cv.rect(xs[0], by, bw * chance[0] / tot - 2, 9, SIG)
        cv.rect(xs[1], by, bw * chance[1] / tot - 2, 9, PAPER)
        cv.rect(xs[2], by, bw * chance[2] / tot, 9, GOLD)
        if focus:
            cv.text("10+ 17 % · 7–9 42 %", x + 21, by + 22, "text", 18, PAPER, anchor="lm", maxw=w - 62)


def ropes(cv, y):
    """Три линви (знак гри) — лінія підлоги для ряду дій: папір, кармін, папір; кінці — подушки кутових стійок."""
    for i, c in enumerate((PAPER, CRIM, PAPER)):
        cv.rect(96, y + i * 7, 1728, 3, c, a=0.0 + 1.0) if False else cv._fill_poly([(96, y + i * 7), (1824, y + i * 7), (1824, y + i * 7 + 3), (96, y + i * 7 + 3)], c, 0.58 if c == PAPER else 0.9)
    for x in (96, 1816):
        cv.rect(x - 4, y - 5, 12, 27, PAPER)
        cv.rect(x - 4, y + 6, 12, 5, CRIM)


def dock(cv):
    y0 = 838
    ropes(cv, 804)
    cv.text("ДІЇ ТИЖНЯ", 96, y0 + 22, "display900", 32, PAPER, anchor="lm", tracking=0.03)
    for i in range(3):
        cv.circle(96 + 192 + i * 30, y0 + 22, 10, fill=GOLD if i < 2 else INK3)
    label(cv, "лишилось 2 з 3", 96 + 192 + 3 * 30 + 6, y0 + 22, "lm", DIM)
    cv.text("Промо: ворожнеча +1 · ажіотаж на старті +1", 624, y0 + 22, "text", 18, PAPER, anchor="lm", maxw=420)
    # вкладки «за запитом» (акордеон): Розвиток із золотим числом — витратити очки = дія гравця
    for k, (nm, n, gold) in enumerate((("РОЗВИТОК", 6, True), ("ВИТРАТИ", 2, False))):
        tx = 1466 - 2 * 188 - 12 + k * 200
        cv.slab(tx, y0, 188, 46, INK1, "r", outline=INK3, ow=3, shadow=INK, sh=(5, 5))
        if gold:
            cv.rect(tx, y0, 6, 46, GOLD)
        cv.text(nm, tx + 24, y0 + 23, "display800", 24, PAPER, anchor="lm", tracking=0.04)
        cv.text(str(n), tx + 188 - 40, y0 + 23, "display900", 30, GOLD if gold else DIM, anchor="rm")
    acts = [("1", "Тренування", "+1 розвиток", None, False),
            ("2", "Тренування|з тренером", "+2 розвитку · −₴10", None, False),
            ("3", "Відновлення", "ушкодження −2", None, False),
            ("4", "Промо", "ворожнеча, ажіотаж", (41, 42, 17), True),
            ("5", "Закулісся", "довіра, чутка", (41, 42, 17), False),
            ("6", "Підробіток", "+₴40|ризик травми 17 %", None, False)]
    for i, (k, t, e, ch, fc) in enumerate(acts):
        tile(cv, 96 + i * 230, 892, 220, 134, k, t, e, ch, fc)
    # головна дія: золота плита (як «Завершити хід» у матчі)
    cv.slab(1496, 892, 328, 134, GOLD, "l", shadow=INK, sh=SH)
    cv.text("ПЕРЕЙТИ", 1496 + 54, 892 + 38, "display900", 48, INK, anchor="lm")
    cv.text("ДО ВЕЧОРА", 1496 + 54, 892 + 82, "display900", 48, INK, anchor="lm")
    cv.text("ENTER", 1496 + 328 - 36, 892 + 117, "text", 18, INK, anchor="rm", tracking=0.14)


def hub():
    cv = Hub(img=scene())
    player_strap(cv)
    resources_plate(cv)
    week_plate(cv)
    opponent_strap(cv)
    bill(cv)
    versus(cv)
    dock(cv)
    return cv.done()


def states():
    """Сторінка станів: плитка дії, стрічка за роллю, афіша за щаблем арени."""
    cv = Hub(w=1920, h=1010)
    cv.text("ХАБ ТИЖНЯ · СТАНИ", 96, 70, "display900", 40, PAPER, anchor="lm", tracking=0.03)
    label(cv, "ескіз B03 v2 · плитки 220×134, стрічки 492×127, афіша 492×186 @1080p (еталон 900: ×0,83)", 96, 112, "lm", DIM)
    # ряд 1: плитки
    cap = ["звичайна", "фокус", "з ризиком", "недоступно + причина", "ліміт вичерпано"]
    tiles = [dict(key="1", title="Тренування", effect="+1 розвиток"),
             dict(key="4", title="Промо", effect="ворожнеча, ажіотаж", chance=(41, 42, 17), focus=True),
             dict(key="5", title="Закулісся", effect="довіра, чутка", chance=(41, 42, 17)),
             dict(key="2", title="Тренування|з тренером", effect="+2 розвитку · −₴10", off="бракує ₴10"),
             dict(key="6", title="Підробіток", effect="+₴40", off="дії вичерпано")]
    for i, t in enumerate(tiles):
        x = 96 + i * 360
        tile(cv, x, 168, 220, 134, t["key"], t["title"], t["effect"], t.get("chance"), t.get("focus", False), t.get("off"))
        label(cv, cap[i], x, 330, "lm", DIM)
    # ряд 2: стрічки за роллю
    roles = [("ГЕРОЙ", COBT, COB, "кобальт: герой"), ("ЛИХОДІЙ", SIG, CRIM, "кармін/сигнал: лиходій"), ("БУНТАР", ROSE, ROSE, "рожевий: бунтар")]
    for i, (rt, rc, ac, cp) in enumerate(roles):
        x = 96 + i * 540
        player_strap(cv, x, 380, ac, (rt, rc), "НОВАЧОК", "МОНСТР", head=2 if i == 0 else 0, torso=1 if i == 0 else 0)
        label(cv, cp + (" · травми" if i == 0 else " · здоровий"), x, 380 + 127 + 36, "lm", DIM)
    # ряд 3: афіші
    bill(cv, 96, 600, torn=True, rematch=False, threat=True, fee=20)
    label(cv, "спортзал: рваний край, загроза «У формі»", 96, 600 + 186 + 80, "lm", DIM)
    bill(cv, 700, 600, torn=False, rematch=True, threat=False, fee=2400)
    label(cv, "Палац: чистий край, «Реванш» — перевага (золотий маркер)", 700, 600 + 215 + 40, "lm", DIM)
    return cv.done()


def before_after():
    """Дві кадри одна під одною: збірка до і ескіз після, зі смугами-підписами."""
    old = Image.open(SRC).convert("RGB").resize((1600, 900), Image.LANCZOS)
    new = hub().resize((1600, 900), Image.LANCZOS)
    base = Image.new("RGB", (1600, 2000), INK)
    base.paste(old, (0, 100))
    base.paste(new, (0, 1100))
    cv = Hub(img=base, w=1600, h=2000)
    cv.text("ДО · ЗБІРКА 06.11.2026, КАДР 122", 40, 50, "display800", 34, PAPER, anchor="lm", tracking=0.05)
    cv.text("ПІСЛЯ · ЕСКІЗ B03 V2 (СВІТЛО Й КАДР БОРЦІВ — ІМІТАЦІЯ)", 40, 1050, "display800", 34, PAPER, anchor="lm", tracking=0.05)
    return cv.done()


if __name__ == "__main__":
    utf8()
    mode = sys.argv[1] if len(sys.argv) > 1 else "hub"
    out = sys.argv[2] if len(sys.argv) > 2 else mode + ".jpg"
    {"hub": hub, "states": states, "before_after": before_after}[mode]().save(out, quality=90)
    print("ok", out)
