"""Метал за рингом: удар у сталевий бар'єр, кидок у бар'єр, удар складаним сталевим стільцем (власна генерація, numpy).

Запуск (з теки tools):  python -m ring_audio.ring_outside_metal --out <тека>
Фізика: оцинкована трубчаста рама ~1,1 м, секції зчеплені гаками, стоять на бетоні. Удар збуджує негармонійні моди
труб і листа (200 Гц-4 кГц), що швидко згасають (рама важка й перешкоджена); брязкіт з'єднань - нерегулярні пачки
високих мод 2-7 кГц за 20-300 мс; низький стук секції об бетон 60-200 Гц. Стілець - ребриста сталева пластина
(моди 300 Гц-6 кГц), яку тіло гасить за 30-120 мс. Плоть - примітиви ring_impacts (body, meat, slap).
Детерміновано (seed від id), моно, 48 кГц / 16 біт.
"""
import argparse
import os
import zlib

import numpy as np

from .loudness import clip_loudness, true_peak_db
from .ring_impacts import C, N, bn, body, env, finish, meat, pk, sheet, spectrogram, write_wav
from .synth import SR, filt

FAMILY = "ring_outside_metal"


# ---------------------------------------------------------------- примітиви металу
def modes(c, t=0.0, g=1.0, flo=300, fhi=4000, count=24, tau=(0.004, 0.03), att=0.0006, dur=0.4,
          kick=0.6, tilt=0.0):
    """негармонійна модальна пластина/труба: багато мод (лог-рівномірно), короткі tau, плюс шумовий імпульс удару.
    Багато мод + короткі tau = лязк, а не тон."""
    r = np.random.default_rng(c.sd())
    n = int(dur * SR)
    tt = np.arange(n) / SR
    x = np.zeros(n)
    fs = np.exp(r.uniform(np.log(flo), np.log(fhi), count))
    for f in fs:
        tk = r.uniform(*tau) * (1.0 + tilt * np.log(fhi / f))
        tk = min(tk, 0.03)
        if f < 1500:
            tk *= 0.7
        a = r.uniform(0.3, 1.0) * (f / 1000.0) ** -0.1 * (0.6 if tk > 0.02 else 1.0)
        x += a * np.exp(-tt / tk) * np.sin(2 * np.pi * f * tt + r.uniform(0, 6.28))
    x = pk(x)
    imp = bn(n, c.sd(), flo * 0.7, fhi * 1.5, 1) * np.exp(-tt / 0.0025)
    x = x + kick * pk(imp)
    x *= 1 - np.exp(-tt / att)
    c.add(pk(x), t, g)


def clatter(c, t0, t1, count, g=0.3, lo=1800, hi=7500, dur=0.09, decay=0.5, tau=(0.002, 0.009), modes_n=6):
    """нерегулярні пачки високих мод (гаки/з'єднання): проміжки експоненційні, без рівної періодичності"""
    r = np.random.default_rng(c.sd())
    n = int(dur * SR)
    tt = np.arange(n) / SR
    u = np.sort(r.uniform(0, 1, count) ** r.uniform(1.3, 2.0))
    ts = t0 + u * (t1 - t0)
    for t in ts:
        x = np.zeros(n)
        for f in np.exp(r.uniform(np.log(lo), np.log(hi), modes_n)):
            x += r.uniform(0.3, 1) * np.exp(-tt / r.uniform(*tau)) * np.sin(2 * np.pi * f * tt + r.uniform(0, 6.28))
        x = pk(x) + 0.8 * pk(bn(n, c.sd(), lo, hi + 1500, 1)) * np.exp(-tt / 0.003)
        x = x[:int(n * r.uniform(0.5, 1.0))]
        tt_ = tt[:len(x)]
        x *= 1 - np.exp(-tt_ / 0.0004)
        c.add(pk(x), float(t), g * r.uniform(0.25, 1.0) * np.exp(-(t - t0) / max(t1 - t0, 0.05) * decay))


def smear(c, t=0.0, g=1.0, flo=300, fhi=4000, count=14, tau=(0.002, 0.008), att=0.0005, dur=0.2,
          kick=0.8, glide=0.12, fm=0.04):
    """лязк без ліній: мало коротких мод (tau <= 10 мс), кожна зі ковзанням частоти (глайд) і шумовою фазовою
    модуляцією, тож на спектрограмі це пляма, а не горизонтальна лінія; основа - смуговий шум з обвідною."""
    r = np.random.default_rng(c.sd())
    n = int(dur * SR)
    tt = np.arange(n) / SR
    x = np.zeros(n)
    fs = np.exp(np.sort(r.uniform(np.log(flo), np.log(fhi), count)) + r.uniform(-0.12, 0.12, count))
    for f in fs:
        tk = min(r.uniform(*tau), 0.010)
        g_ = 1.0 + r.uniform(-glide, glide) * (1 - np.exp(-tt / 0.01))
        ph = 2 * np.pi * np.cumsum(f * g_) / SR
        ph = ph + fm * 2 * np.pi * np.cumsum(filt(N(n, c.sd()), "lp", 300, 0.7, 1)) / SR * f / 40.0
        x += r.uniform(0.3, 1.0) * np.exp(-tt / tk) * np.sin(ph + r.uniform(0, 6.28))
    x = pk(x)
    nz = pk(bn(n, c.sd(), flo * 0.8, fhi * 1.3, 2)) * np.exp(-tt / 0.006)
    imp = pk(bn(n, c.sd(), flo * 0.7, fhi * 1.5, 1)) * np.exp(-tt / 0.0025)
    y = 0.55 * x + 0.8 * nz + kick * imp
    y *= 1 - np.exp(-tt / att)
    c.add(pk(y), t, g)


def scatter(c, t0, t1, count, g=0.3, lo=1800, hi=7000, dur=0.05, decay=0.5):
    """розсип дрібних брязкань: кожне - короткий шумовий клік у випадковій вузькій (1/3 окт.) смузі, без мод"""
    r = np.random.default_rng(c.sd())
    u = np.sort(r.uniform(0, 1, count) ** r.uniform(1.3, 2.0))
    for t in t0 + u * (t1 - t0):
        n = int(dur * r.uniform(0.5, 1.0) * SR)
        fc = float(np.exp(r.uniform(np.log(lo), np.log(hi))))
        x = bn(n, c.sd(), fc * 0.75, fc * 1.35, 2) * env(n, 0.3, r.uniform(1.5, 5.0))
        c.add(pk(x), float(t), g * r.uniform(0.25, 1.0) * np.exp(-(t - t0) / max(t1 - t0, 0.05) * decay))


def touch(c, t, g, lo, hi, ms, tau):
    """один фізичний дотик: смуговий шум lo-hi Гц, тривалість ms, затухання tau (мс); кожен - свій"""
    n = int(ms / 1000 * SR)
    c.add(pk(bn(n, c.sd(), lo, hi, 2)) * env(n, 0.3, tau), t, g)


def jumble(c, t0, t1, count, g=0.4, lo=500, hi=6500, decay=0.6, ms=(12, 45), mixed=0.25):
    """гуркіт металу: нерегулярні дотики, у кожного своя смуга (центр і ширина), довжина, затухання й рівень;
    частина - глухі (нижчі смуги); жодного повторюваного набору частот."""
    r = np.random.default_rng(c.sd())
    u = np.sort(r.uniform(0, 1, count) ** r.uniform(1.2, 1.8))
    for t in t0 + u * (t1 - t0):
        fc = float(np.exp(r.uniform(np.log(lo), np.log(hi))))
        if r.uniform() < mixed:
            fc = float(np.exp(r.uniform(np.log(200), np.log(900))))
        w = r.uniform(1.25, 2.2)
        d = r.uniform(*ms)
        touch(c, float(t), g * r.uniform(0.3, 1.0) * np.exp(-(t - t0) / max(t1 - t0, 0.05) * decay),
              fc / w, min(fc * w, 9000), d, d * r.uniform(0.12, 0.3))


def concrete(c, t=0.0, g=1.0, lo=55, hi=190, tau=55.0, dur=0.35):
    """стук рами/ніжок об бетон: низький глухий, короткий + ледь чутний клік контакту"""
    n = int(dur * SR)
    c.add(pk(bn(n, c.sd(), lo, hi, 3)) * env(n, 1.2, tau), t, g)
    m = int(0.03 * SR)
    c.add(pk(bn(m, c.sd(), 400, 2500, 1)) * env(m, 0.3, 4.0), t, g * 0.35)


def scrape(c, t, dur, g=0.3, lo=250, hi=2800):
    """скрегіт ніжок по бетону: шум з нерівною, ривковою обвідною (стик-сліп)"""
    n = int(dur * SR)
    x = bn(n, c.sd(), lo, hi, 2)
    m = np.abs(filt(N(n, c.sd()), "lp", 38, 0.7, 2))
    m = m / max(m.max(), 1e-9)
    m = np.where(m > 0.35, m, 0.08)
    e = np.sin(np.pi * np.linspace(0, 1, n)) ** 0.5 * np.linspace(1.0, 0.5, n)
    c.add(pk(x) * m * e, t, g)


def cloth(c, t, g=0.4, dur=0.12):
    """тканинний чохол на каркасі: приглушений удар без дзвону"""
    n = int(dur * SR)
    c.add(pk(bn(n, c.sd(), 200, 1800, 2)) * env(n, 1.0, 22.0), t, g)


# ---------------------------------------------------------------- рецепти
R = {}


def rec(slot, dur, level, target, labels):
    def deco(f):
        R[slot] = (dur, level, target, labels, f)
        return f
    return deco


@rec("barrier_hit", 0.7, -4.0, -14.0, [
    ("Варіант 1 — спиною в м'який чохол", "Широкий ляпас спини й важкий м'ясний удар; бар'єр у тканинному чохлі, тож рама дає лише приглушений стук 150-900 Гц і кілька тихих брязкань гаків пізніше. Найм'якший метал із трьох, тіло домінує."),
    ("Варіант 2 — боком у голу сталь", "Важчий ляпас плечем/боком із м'ясним низом 80-250 Гц і одразу сухий лязк голої труби й сітки: розмазані короткі моди 400 Гц-3,6 кГц (до ~30 мс, без тривалих ліній), далі нерегулярний розсип коротких брязкань гаків 2-6 кГц."),
    ("Варіант 3 — удар зі зсувом секції", "Важкий удар тілом, глухий стук секції об бетон 60-190 Гц і за 60-90 мс короткий скрегіт ніжки, коли секція проїхала; метал приглушений, акцент на басі й терті."),
])
def _bh(v, c):
    if v == 1:
        body(c, 0.0, 1.0, 1.0, 1.2, 0.35, 1200, 5500, 5.0, 70, 260, 55.0)
        cloth(c, 0.004, 0.55)
        smear(c, 0.006, 0.28, 150, 900, 8, (0.003, 0.010), dur=0.25, kick=0.4)
        concrete(c, 0.01, 0.45, 60, 180, 45)
        scatter(c, 0.06, 0.45, 4, 0.14, 1800, 5200, decay=0.9)
    elif v == 2:
        body(c, 0.0, 1.0, 1.2, 0.9, 0.3, 1500, 6000, 4.0, 80, 260, 50.0)
        meat(c, 0.002, 1.5, 60, 200, 55.0)
        smear(c, 0.003, 0.75, 400, 3600, 14, (0.003, 0.009), dur=0.22, kick=0.7)
        smear(c, 0.05, 0.3, 700, 3000, 8, (0.003, 0.008), dur=0.14, kick=0.5)
        concrete(c, 0.008, 0.5, 65, 190, 45)
        scatter(c, 0.04, 0.4, 12, 0.35, 2200, 6500, decay=0.7)
    else:
        body(c, 0.0, 1.0, 0.85, 1.25, 0.3, 1400, 5000, 5.0, 50, 260, 30.0)
        smear(c, 0.005, 0.38, 250, 2200, 8, (0.003, 0.010), dur=0.25, kick=0.5)
        concrete(c, 0.004, 1.0, 50, 260, 30)
        scrape(c, 0.07, 0.18, 0.4, 300, 2600)
        concrete(c, 0.27, 0.5, 90, 320, 28)
        scatter(c, 0.08, 0.5, 5, 0.18, 2000, 6000, decay=0.8)


@rec("barrier_slam", 1.2, -3.0, -14.0, [
    ("Варіант 1 — кидок усім тілом, гуркіт секцій", "Важкий удар тіла й одразу гучний металевий гуркіт кількох секцій (широка суміш мод 200 Гц-4 кГц), потім нерівний гуркіт секцій-дотиків (шум різних смуг і довжин, не дзвін) з паузами, що тягнеться майже на весь файл: останній дотик вище −20 дБ від піку закінчується на ≈0,8 с."),
    ("Варіант 2 — зсув бар'єра по бетону", "Удар тілом, глухий стук рами, а за ним довгий скрегіт ніжок по бетону з ривками (бар'єр їде під вагою) і нижчий вторинний стук, коли секція зупиняється."),
    ("Варіант 3 — промах предметом, нападник у бар'єр", "Менш м'ясний і сухіший: плечем у раму без плоті суперника, сухий лязк і подвійний рух секції (удар, відскок, повторний дотик) із 4-5 нерівними дотиками гаків і ніжок (різні смуги, тривалості й рівні; два щільно, пауза, ще два); тіло глухіше в басі. За спектром найтемніший з трьох (центр ≈390 Гц проти ≈540 і ≈580)."),
])
def _bs(v, c):
    if v == 1:
        body(c, 0.0, 1.0, 0.9, 1.3, 0.35, 1200, 5500, 5.0, 60, 250, 70.0)
        concrete(c, 0.0, 0.9, 55, 170, 80)
        smear(c, 0.004, 0.8, 200, 4000, 18, (0.003, 0.010), dur=0.5, kick=0.6)
        smear(c, 0.03, 0.5, 300, 3000, 12, (0.003, 0.010), dur=0.35, kick=0.3)
        smear(c, 0.075, 0.35, 450, 3600, 10, (0.003, 0.010), dur=0.3, kick=0.3)
        jumble(c, 0.04, 0.85, 16, 0.55, 600, 7000, decay=0.6)
    elif v == 2:
        body(c, 0.0, 1.0, 0.8, 1.25, 0.3, 1200, 5000, 5.0, 60, 240, 65.0)
        concrete(c, 0.0, 0.8, 55, 170, 70)
        modes(c, 0.004, 0.45, 200, 2800, 20, (0.012, 0.05), dur=0.5, kick=0.45)
        scrape(c, 0.1, 0.55, 0.55, 220, 2600)
        scrape(c, 0.42, 0.25, 0.35, 250, 2000)
        concrete(c, 0.72, 0.5, 60, 200, 55)
        clatter(c, 0.05, 0.9, 14, 0.3, 1800, 6500, decay=0.5)
    else:
        body(c, 0.0, 0.55, 0.5, 1.0, 0.25, 1200, 4500, 4.0, 70, 240, 50.0)
        concrete(c, 0.0, 0.7, 60, 180, 60)
        smear(c, 0.002, 1.0, 350, 4500, 16, (0.003, 0.010), dur=0.3, kick=0.9)
        smear(c, 0.14, 0.55, 300, 3200, 12, (0.003, 0.009), dur=0.2, kick=0.5)
        concrete(c, 0.14, 0.45, 60, 180, 50)
        smear(c, 0.31, 0.3, 400, 3000, 8, (0.003, 0.008), dur=0.14, kick=0.3)
        # двічі-тричі металом об метал нерівними групами: 2 дотики щільно, пауза, ще один-два, дрібний хвіст
        touch(c, 0.42, 0.40, 1200, 4500, 38, 7)
        touch(c, 0.463, 0.35, 250, 800, 42, 10)
        touch(c, 0.60, 0.22, 700, 2600, 26, 5)
        touch(c, 0.64, 0.16, 2500, 7000, 14, 3)
        touch(c, 0.83, 0.10, 300, 1000, 30, 6)


@rec("weapon_chair", 0.7, -3.0, -14.0, [
    ("Варіант 1 — плаский удар листом сидіння", "Класичний ЛЯСК: широкосмуговий сталевий лист (300 Гц-6 кГц) об спину, розмите шумове дзвеніння згасає за ~30 мс, під ним ляпас по шкірі й глухий удар тіла, а далі нерівні дотики ніжок: два високі й два глухі (200-900 Гц) різної довжини."),
    ("Варіант 2 — удар ребром, із тріском", "Вузький жорсткий удар ребром: сухіший і грубіший за варіант 1. Різкий тріск контакту й розмитий лязк труби; ляпас по шкірі мінімальний; далі нерівні дотики ніжок: короткі високі й два глухі, 200-800 Гц. Низу не менше, а більше (нижче 250 Гц 0,58 енергії проти 0,39 у варіанті 1), хвіст трохи довший (до ≈0,34 с проти ≈0,30 с)."),
    ("Варіант 3 — удар, відскок і падіння стільця на бетон", "Плаский лясак по спині, а за ~0,19 с стілець падає на бетон: найважчий глухий дотик ніжок і листа, далі 3 відскоки з інтервалами, що скорочуються (~115, 70, 40 мс), кожен тихіший і з іншою смугою."),
])
def _wc(v, c):
    def legs(t, g, n_=8):
        smear(c, t, g * 0.5, 800, 3000, 6, (0.002, 0.007), dur=0.1, kick=0.4)
        scatter(c, t, t + 0.1, n_, g * 0.5, 1800, 6500, decay=0.6, dur=0.05)
    def settle(t0, t1, g):
        scatter(c, t0, t1, 7, g, 1500, 5500, decay=1.2, dur=0.05)
        concrete(c, t0 + 0.02, g * 0.5, 80, 240, 30)
    if v == 1:
        body(c, 0.0, 0.9, 1.1, 1.0, 0.3, 1500, 7000, 4.0, 80, 270, 40.0)
        smear(c, 0.0, 1.0, 300, 6000, 18, (0.003, 0.009), dur=0.2, kick=1.0)
        touch(c, 0.048, 0.45, 1400, 6000, 30, 7)
        touch(c, 0.083, 0.55, 200, 700, 36, 9)
        touch(c, 0.176, 0.22, 2800, 8000, 9, 2.5)
        touch(c, 0.19, 0.38, 300, 900, 40, 10)
        touch(c, 0.29, 0.10, 1200, 4000, 16, 4)
    elif v == 2:
        body(c, 0.0, 0.55, 0.7, 0.6, 0.2, 2000, 8000, 3.5, 100, 300, 30.0)
        smear(c, 0.0, 1.0, 900, 6500, 16, (0.002, 0.007), dur=0.16, kick=1.3)
        m = int(0.012 * SR)
        c.add(pk(bn(m, c.sd(), 3000, 9000, 1)) * env(m, 0.2, 1.8), 0.0, 0.7)
        meat(c, 0.0, 0.8, 60, 200, 40.0)
        touch(c, 0.055, 0.4, 3000, 8000, 7, 2)
        touch(c, 0.082, 0.55, 250, 800, 36, 9)
        touch(c, 0.19, 0.28, 1000, 4000, 22, 6)
        touch(c, 0.212, 0.18, 3500, 9000, 5, 1.6)
        touch(c, 0.31, 0.30, 200, 600, 40, 10)
        smear(c, 0.004, 0.3, 300, 1200, 6, (0.004, 0.010), dur=0.14, kick=0.3)
    else:
        body(c, 0.0, 0.85, 1.0, 1.0, 0.3, 1500, 6500, 4.0, 80, 260, 40.0)
        smear(c, 0.0, 0.85, 300, 6000, 16, (0.003, 0.009), dur=0.2, kick=0.9)
        legs(0.012, 0.5, 6)
        # справжній відскок: перший дотик найважчий і глухий (ніжки й лист об бетон), далі інтервали ~x0.6, -5 дБ кожен
        concrete(c, 0.19, 0.9, 60, 240, 38)
        touch(c, 0.192, 0.55, 300, 2600, 40, 9)
        touch(c, 0.192, 0.3, 1500, 6000, 18, 4)
        touch(c, 0.305, 0.26, 500, 3200, 26, 6)
        concrete(c, 0.307, 0.22, 100, 380, 22)
        touch(c, 0.374, 0.11, 900, 4800, 16, 4)
        touch(c, 0.412, 0.10, 2200, 7000, 9, 2.5)


def _limit_lookahead(y, ceil, k=1.0):
    """плавний ліміталер без сатурації (форма хвилі не гнеться, плоских вершин немає): огинаюча піків з lookahead 3 мс
    і спадом ~25 мс, згладжена вікном Ганна; підсилення лише зменшується; k<1 - м'якше за жорстку стелю: вершини не вирівнюються в плато"""
    a = np.abs(y)
    w = int(0.003 * SR)
    pkm = np.max(np.lib.stride_tricks.sliding_window_view(np.pad(a, (w, w)), 2 * w + 1), axis=1)
    rel = np.exp(-1.0 / (0.025 * SR))
    for i in range(len(pkm) - 2, -1, -1):
        pkm[i] = max(pkm[i], pkm[i + 1] * rel)
    sm = np.hanning(2 * w + 1)
    pkm = np.convolve(np.pad(pkm, (w, w), mode="edge"), sm / sm.sum(), "valid")
    pkm = np.maximum(pkm, a)
    return y * np.minimum(1.0, ceil / np.maximum(pkm, 1e-9)) ** k


# ---------------------------------------------------------------- рендер
def render(slot, v):
    dur, level, target, labels, f = R[slot]
    c = C(dur, zlib.crc32(f"{slot}.v{v}".encode()) % 100000)
    f(v, c)
    ya = np.abs(c.y)
    last = np.nonzero(ya > ya.max() * 10 ** (-52 / 20))[0][-1]
    y = c.y[:min(len(c.y), last + int(0.05 * SR))].copy()
    y = finish(y, level)
    # вирівнювання гучності до цілі слота: підсилення + м'яке обмеження піків, стеля -1.15 dBTP
    for _ in range(12):
        d = target - clip_loudness(y)
        if abs(d) < 0.25:
            break
        y = y * 10 ** (d / 20)
        if d > 0:
            y = _limit_lookahead(y, 0.8, 0.5) if (slot, v) == ("barrier_slam", 3) else np.tanh(y / 0.85) * 0.85
        tp = true_peak_db(y)
        if tp > -1.1 or d > 0:
            y = y * 10 ** ((-1.15 - tp) / 20)
    return y, labels[v - 1][0]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--only", default="")
    a = ap.parse_args()
    ad, sd = os.path.join(a.out, "audio"), os.path.join(a.out, "spec")
    os.makedirs(ad, exist_ok=True)
    os.makedirs(sd, exist_ok=True)
    ids = []
    for slot in R:
        if a.only and slot not in a.only.split(","):
            continue
        for v in (1, 2, 3):
            y, label = render(slot, v)
            i = f"{slot}.v{v}"
            write_wav(os.path.join(ad, i + ".wav"), y)
            spectrogram(y, os.path.join(sd, i + ".png"), f"{i}  {label}")
            tp = true_peak_db(y)
            lu = clip_loudness(y)
            ids.append(i)
            lead = int(np.argmax(np.abs(y) > 0.01 * np.max(np.abs(y)))) / SR * 1000
            print(f"{i:18s} {len(y)/SR:5.2f}s  {lu:6.1f} LUFS(clip)  {tp:5.2f} dBTP  lead {lead:.1f} ms")
    if not a.only:
        sheet(ids, sd, os.path.join(sd, "_ring_outside_metal.png"), cols=3)


if __name__ == "__main__":
    main()
