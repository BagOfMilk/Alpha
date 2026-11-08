"""Інструменти процедурного синтезу для музики меню «Рингу».

Усе чистий numpy, без сторонніх бібліотек. Кожен інструмент повертає МОНО-масив
float64 (пік ~ 1 * vel). Результати кешуються (lru_cache), тому масиви повертаються
спільними: ЗМІНЮВАТИ їх на місці не можна (завжди множити з створенням нового).

Фільтри — біквади, що застосовуються через FFT (частотна характеристика
H(e^jw) = B(z)/A(z)); для змінного зрізу (wah, атака духових) — перемішування
кількох фіксованих фільтрів за кривою зрізу (sweep_filter).
"""
from functools import lru_cache

import numpy as np

SR = 48000


def mtof(m):
    return 440.0 * 2.0 ** ((np.asarray(m, dtype=float) - 69.0) / 12.0)


def q(x, step=0.01):
    """Округлення параметра для кешу."""
    return round(round(x / step) * step, 6)


def fast_len(n):
    """Найближче 7-гладке число >= n (швидкий розмір FFT)."""
    n = int(n)
    while True:
        m = n
        for p in (2, 3, 5, 7):
            while m % p == 0:
                m //= p
        if m == 1:
            return n
        n += 1


# ---------------------------------------------------------------- фільтри
def biquad(kind, fc, qf=0.7071, sr=SR):
    fc = min(max(float(fc), 10.0), sr * 0.46)
    w0 = 2 * np.pi * fc / sr
    c, s = np.cos(w0), np.sin(w0)
    al = s / (2 * qf)
    if kind == "lp":
        b = [(1 - c) / 2, 1 - c, (1 - c) / 2]
    elif kind == "hp":
        b = [(1 + c) / 2, -(1 + c), (1 + c) / 2]
    elif kind == "bp":  # постійний пік 0 дБ
        b = [al, 0.0, -al]
    else:
        raise ValueError(kind)
    a = [1 + al, -2 * c, 1 - al]
    return np.array(b) / a[0], np.array(a) / a[0]


def lfilter_fft(b, a, x, circular=False, tail=0):
    """IIR-фільтр у частотній області. circular=True — циклічний (для петель)."""
    x = np.asarray(x, dtype=float)
    n = len(x)
    N = n if circular else fast_len(n + tail + 2048)
    X = np.fft.rfft(x, N)
    w = 2 * np.pi * np.arange(N // 2 + 1) / N
    z = np.exp(-1j * w)
    P = np.polynomial.polynomial.polyval
    H = P(z, b) / P(z, a)
    return np.fft.irfft(X * H, N)[:n]


def filt(x, kind, fc, qf=0.7071, order=1, circular=False):
    b, a = biquad(kind, fc, qf)
    y = x
    for _ in range(order):
        y = lfilter_fft(b, a, y, circular=circular)
    return y


def sweep_filter(x, fc, kind="lp", qf=0.8, K=7):
    """Фільтр зі змінним зрізом fc(t) (масив тієї ж довжини)."""
    fc = np.clip(fc, 30.0, SR * 0.44)
    lo, hi = float(fc.min()), float(fc.max())
    if hi / lo < 1.06:
        return filt(x, kind, (lo * hi) ** 0.5, qf)
    cuts = np.geomspace(lo, hi, K)
    ys = np.stack([filt(x, kind, c, qf) for c in cuts])
    pos = (np.log(fc) - np.log(lo)) / (np.log(hi) - np.log(lo)) * (K - 1)
    i = np.minimum(pos.astype(int), K - 2)
    f = pos - i
    idx = np.arange(len(x))
    return ys[i, idx] * (1 - f) + ys[i + 1, idx] * f


# ---------------------------------------------------------------- осцилятори
def saw(freq, n, phase0=0.0):
    """Обмежений за смугою пилок (polyBLEP). freq — число або масив довжини n."""
    f = np.broadcast_to(np.asarray(freq, dtype=float), (n,))
    ph = phase0 + np.cumsum(f) / SR
    t = ph % 1.0
    dt = f / SR
    y = 2 * t - 1
    m = t < dt
    x = t[m] / dt[m]
    y[m] -= (x + x - x * x - 1)
    m = t > 1 - dt
    x = (t[m] - 1) / dt[m]
    y[m] -= (x * x + x + x + 1)
    return y


def pulse(freq, n, width=0.3, phase0=0.0):
    return saw(freq, n, phase0) - saw(freq, n, phase0 + width)


def noise(n, seed):
    return np.random.default_rng(seed).standard_normal(n)


# ---------------------------------------------------------------- допоміжне
def fade(y, fin_ms=2.0, fout_ms=6.0):
    y = np.array(y, dtype=float)
    a = int(fin_ms * SR / 1000)
    b = int(fout_ms * SR / 1000)
    if 0 < a < len(y):
        y[:a] *= np.sin(np.linspace(0, np.pi / 2, a)) ** 2
    if 0 < b < len(y):
        y[-b:] *= np.cos(np.linspace(0, np.pi / 2, b)) ** 2
    return y


def damp(y, hold_n, rel):
    """Після hold_n експоненційне приглушення (відпускання клавіші)."""
    y = np.array(y, dtype=float)
    if hold_n < len(y):
        k = np.arange(len(y) - hold_n)
        y[hold_n:] *= np.exp(-k / (rel * SR))
    return y


def norm(y, peak=1.0):
    m = np.max(np.abs(y))
    return y * (peak / m) if m > 1e-12 else y


def _t(n):
    return np.arange(n) / SR


# ---------------------------------------------------------------- бас
@lru_cache(maxsize=None)
def slap_bass(midi, dur, vel=1.0, kind="note"):
    """Слеп-бас: адитивний щипок (гармоніки згасають швидше за основу)
    + транзієнт удару великим пальцем / «поп» зверху. kind: note|slap|pop|ghost."""
    f = float(mtof(midi))
    tau0 = {"note": 0.30, "slap": 0.24, "pop": 0.10, "ghost": 0.035}[kind]
    n = int((dur + 0.12) * SR)
    if kind == "ghost":
        n = int(0.14 * SR)
    t = _t(n)
    nh = int(min(16, (0.42 * SR) // f))
    y = np.zeros(n)
    for h in range(1, nh + 1):
        amp = {1: 1.0, 2: 0.95}.get(h, 1.0 / h ** 0.95)
        if kind in ("slap", "pop"):
            amp *= 1.0 + 0.04 * h
        y += amp * np.sin(2 * np.pi * h * f * t) * np.exp(-t / (tau0 / (1 + 0.30 * (h - 1))))
    bright = {"note": 0.8, "slap": 1.5, "pop": 1.9, "ghost": 0.4}[kind]
    fc = 380 + 2600 * bright * np.exp(-t / 0.05)
    y = sweep_filter(y, fc, "lp", 0.9, K=5)
    # транзієнт
    tn = int(0.012 * SR)
    click = filt(noise(tn, 11 + int(midi)), "bp", 1800 if kind != "ghost" else 700, 1.2)
    click *= np.exp(-_t(tn) / 0.003)
    amp_click = {"note": 0.18, "slap": 0.55, "pop": 0.5, "ghost": 0.3}[kind]
    y[:tn] += amp_click * click * np.max(np.abs(y)) / max(np.max(np.abs(click)), 1e-9)
    y = np.tanh(1.3 * y / np.max(np.abs(y)))
    if kind != "ghost":
        y = damp(y, int(dur * SR), 0.035)
    return fade(norm(y) * vel, 1.0, 8.0)


@lru_cache(maxsize=None)
def round_bass(midi, dur, vel=0.8):
    """М'який круглий бас (синус + трохи другої гармоніки) для спокійної версії."""
    f = float(mtof(midi))
    n = int((dur + 0.25) * SR)
    t = _t(n)
    y = np.sin(2 * np.pi * f * t) + 0.35 * np.sin(4 * np.pi * f * t) * np.exp(-t / 0.4)
    y += 0.12 * np.sin(6 * np.pi * f * t) * np.exp(-t / 0.2)
    y *= np.minimum(t / 0.012, 1.0) * np.exp(-t / 1.2)
    y = damp(y, int(dur * SR), 0.12)
    return fade(norm(y) * vel, 3.0, 10.0)


# ---------------------------------------------------------------- клавіші
@lru_cache(maxsize=None)
def rhodes(midi, dur, vel=0.8, bright=1.0, trem=0.0):
    """Електропіаніно: FM-«дзвоник» 1:14 (швидко згасає) на тілі 1:1."""
    f = float(mtof(midi))
    rel = 0.16
    n = int((dur + rel + 0.3) * SR)
    t = _t(n)
    tau = min(max(1.3 * (261.63 / f) ** 0.3, 0.5), 2.2)
    k = np.clip(600.0 / f, 0.2, 1.4)
    bell = 3.0 * bright * (0.4 + 0.6 * vel) * k * np.exp(-t / 0.03)
    if 14 * f > 16000:
        bell *= 0.0
    body = (0.45 + 1.1 * vel) * bright * np.exp(-t / 0.42)
    ph = 2 * np.pi * f * t
    y = np.sin(ph + bell * np.sin(14 * ph) + body * np.sin(ph))
    y += 0.12 * np.sin(2 * ph) * np.exp(-t / 0.5)
    y *= np.exp(-t / tau) * np.minimum(t / 0.002, 1.0)
    if trem > 0:
        y *= 1 - trem * 0.5 * (1 - np.cos(2 * np.pi * 4.6 * t))
    y = damp(y, int(dur * SR), 0.13)
    y = filt(y, "lp", 8500)
    return fade(norm(y) * vel, 1.0, 12.0)


# ---------------------------------------------------------------- духові
@lru_cache(maxsize=None)
def brass(midis, dur, vel=0.8, bright=1.0, scoop=0.6, vib=True):
    """Секція духових: 4 розстроєні пилки на ноту, швидка обвідна зрізу
    (яскрава атака -> тепліше), легке «підтягування» тону знизу."""
    n = int((dur + 0.14) * SR)
    t = _t(n)
    acc = np.zeros(n)
    scoop_r = 2.0 ** (-scoop / 12.0 * np.exp(-t / 0.04))
    vibr = 1.0
    if vib and dur > 0.4:
        vibr = 2.0 ** (0.0035 * np.sin(2 * np.pi * 5.4 * t) * np.clip((t - 0.25) / 0.3, 0, 1) / 1.0)
    for i, m in enumerate(midis):
        f = float(mtof(m))
        for j, c in enumerate((-10, -3, 4, 11)):
            acc += saw(f * 2.0 ** (c / 1200.0) * scoop_r * vibr, n, phase0=0.13 * (i * 4 + j) % 1.0)
    fc = 750 + 2900 * bright * np.exp(-t / 0.075) + 450 * bright
    y = sweep_filter(acc, fc, "lp", 1.0, K=6)
    a = int(0.014 * SR)
    env = np.ones(n)
    env[:a] = np.sin(np.linspace(0, np.pi / 2, a)) ** 1.5
    y *= env * (1.0 + 0.25 * np.exp(-t / 0.06))
    y = damp(y, int(dur * SR), 0.045)
    y = np.tanh(0.9 * y / np.max(np.abs(y)))
    return fade(norm(y) * vel, 1.0, 8.0)


@lru_cache(maxsize=None)
def pad(midis, dur, vel=0.5):
    """Теплий падь: розстроєні пилки, повільна атака, низький зріз."""
    n = int((dur + 0.8) * SR)
    t = _t(n)
    acc = np.zeros(n)
    for i, m in enumerate(midis):
        f = float(mtof(m))
        for j, c in enumerate((-7, 0, 8)):
            acc += saw(f * 2.0 ** (c / 1200.0), n, phase0=0.21 * (i * 3 + j) % 1.0)
    y = filt(acc, "lp", 1100, 0.7, order=2)
    a = int(0.45 * SR)
    env = np.ones(n)
    env[:a] = np.sin(np.linspace(0, np.pi / 2, a)) ** 2
    env *= (1 + 0.08 * np.sin(2 * np.pi * 0.23 * t))
    y *= env
    y = damp(y, int(dur * SR), 0.35)
    return fade(norm(y) * vel, 5.0, 40.0)


# ---------------------------------------------------------------- гітара
@lru_cache(maxsize=None)
def guitar(midis, dur=0.07, vel=0.8, wah=1.0):
    """Фанк-«чоп»: імпульсна хвиля, смуговий фільтр, що відкривається (wah)."""
    n = int((dur + 0.07) * SR)
    t = _t(n)
    acc = np.zeros(n)
    for i, m in enumerate(midis):
        acc += pulse(float(mtof(m)), n, 0.32, phase0=0.1 * i)
    fc = 520 + 2100 * wah * (1 - np.exp(-t / 0.035))
    y = 3.0 * sweep_filter(acc, fc, "bp", 2.4, K=6)
    y += 0.25 * filt(acc, "lp", 1800)
    y *= np.minimum(t / 0.002, 1.0) * np.exp(-t / 0.055)
    y = damp(y, int(dur * SR), 0.015)
    y = np.tanh(1.2 * y / np.max(np.abs(y)))
    return fade(norm(y) * vel, 0.8, 6.0)


# ---------------------------------------------------------------- барабани
@lru_cache(maxsize=None)
def kick(vel=1.0, soft=False):
    n = int(0.38 * SR)
    t = _t(n)
    f = 46 + 115 * np.exp(-t / 0.026)
    ph = 2 * np.pi * np.cumsum(f) / SR
    y = np.sin(ph) * np.exp(-t / 0.12)
    cl = filt(noise(n, 5), "hp", 1800) * np.exp(-t / 0.0016)
    y = y + (0.05 if soft else 0.22) * cl
    y = np.tanh(1.6 * y)
    if soft:
        y = filt(y, "lp", 2200)
    return fade(norm(y) * vel, 0.3, 8.0)


@lru_cache(maxsize=None)
def snare(vel=1.0, ghost=False):
    n = int(0.32 * SR)
    t = _t(n)
    tone = np.sin(2 * np.pi * 185 * t) * np.exp(-t / 0.045) + 0.5 * np.sin(2 * np.pi * 330 * t) * np.exp(-t / 0.03)
    nz = filt(filt(noise(n, 21 + int(ghost)), "hp", 1400), "lp", 9500)
    y = 0.8 * tone + nz * np.exp(-t / (0.035 if ghost else 0.11))
    y = np.tanh(1.3 * y)
    if ghost:
        y = filt(y, "lp", 5500)
    return fade(norm(y) * vel * (0.55 if ghost else 1.0), 0.3, 12.0)


@lru_cache(maxsize=None)
def hat(vel=1.0, open_=False, soft=False):
    n = int((0.45 if open_ else 0.09) * SR)
    t = _t(n)
    y = filt(filt(noise(n, 31 + int(open_) * 7), "hp", 6800, 0.9), "lp", 15000)
    y *= np.exp(-t / (0.16 if open_ else 0.022)) * np.minimum(t / 0.0008, 1.0)
    if soft:
        y = filt(y, "lp", 7000)
    return fade(norm(y) * vel, 0.2, 6.0)


@lru_cache(maxsize=None)
def rim(vel=1.0):
    n = int(0.07 * SR)
    t = _t(n)
    y = np.sin(2 * np.pi * 1750 * t) * np.exp(-t / 0.005) + 0.7 * np.sin(2 * np.pi * 520 * t) * np.exp(-t / 0.012)
    y += 0.5 * filt(noise(n, 41), "bp", 2800, 1.5) * np.exp(-t / 0.006)
    return fade(norm(y) * vel, 0.2, 6.0)


@lru_cache(maxsize=None)
def brush(vel=1.0, dur=0.3):
    n = int(dur * SR)
    t = _t(n)
    y = filt(filt(noise(n, 51), "bp", 3200, 0.8), "lp", 6000)
    env = np.minimum(t / 0.045, 1.0) * np.exp(-t / 0.11)
    return fade(norm(y * env) * vel, 5.0, 20.0)


@lru_cache(maxsize=None)
def tom(midi, vel=1.0):
    f = float(mtof(midi))
    n = int(0.4 * SR)
    t = _t(n)
    fr = f * (1 + 0.5 * np.exp(-t / 0.03))
    y = np.sin(2 * np.pi * np.cumsum(fr) / SR) * np.exp(-t / 0.13)
    y += 0.25 * filt(noise(n, 61), "bp", 1500, 1.0) * np.exp(-t / 0.01)
    return fade(norm(np.tanh(1.3 * y)) * vel, 0.3, 12.0)


@lru_cache(maxsize=None)
def crash(vel=1.0):
    n = int(2.4 * SR)
    t = _t(n)
    y = filt(filt(noise(n, 71), "hp", 3200), "lp", 15000)
    y *= (1 - np.exp(-t / 0.006)) * np.exp(-t / 0.55)
    return fade(norm(y) * vel, 0.3, 80.0)


def vinyl(n, seed=3, rate=9.0, level=1.0):
    """Тріск вінілу: рідкі клацання + ледь чутний шум. Довжина n, детермінований."""
    rng = np.random.default_rng(seed)
    y = np.zeros(n)
    cnt = int(rate * n / SR)
    pos = rng.integers(0, n - 200, cnt)
    amp = rng.exponential(1.0, cnt) ** 1.6
    amp = amp / amp.max()
    for p, a in zip(pos, amp):
        k = rng.integers(20, 90)
        seg = rng.standard_normal(k) * np.exp(-np.arange(k) / (k / 4.0))
        y[p:p + k] += a * seg * (1 if rng.random() > 0.5 else -1)
    y = filt(y, "hp", 1500)
    hiss = filt(rng.standard_normal(n), "hp", 3500) * 0.03
    return (0.6 * y / max(np.max(np.abs(y)), 1e-9) * 0.5 + hiss) * level


# ---------------------------------------------------------------- для SFX
@lru_cache(maxsize=None)
def wood_click(freq=1900, vel=1.0, dur=0.03):
    """Сухий дерев'яний/маллет клац."""
    n = int(dur * SR)
    t = _t(n)
    y = np.sin(2 * np.pi * freq * t) * np.exp(-t / 0.004)
    y += 0.5 * np.sin(2 * np.pi * freq * 2.76 * t) * np.exp(-t / 0.002)
    y += 0.3 * filt(noise(n, 81), "bp", freq * 1.4, 1.5) * np.exp(-t / 0.002)
    return fade(norm(y) * vel, 0.1, 4.0)


@lru_cache(maxsize=None)
def swish(dur=0.15, f0=900, f1=5000, vel=1.0, seed=91, peak_at=0.45):
    """Шумовий «свист»: смуговий фільтр ковзає f0 -> f1, обвідна «гойдалка»."""
    n = int(dur * SR)
    t = _t(n)
    x = t / t[-1]
    fc = f0 * (f1 / f0) ** x
    y = sweep_filter(noise(n, seed), fc, "bp", 1.6, K=8)
    env = np.where(x < peak_at, (x / peak_at) ** 1.5, ((1 - x) / (1 - peak_at)) ** 1.2)
    return fade(norm(y * env) * vel, 1.0, 10.0)
