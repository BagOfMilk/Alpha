"""Інструменти процедурного синтезу для музики меню «Рингу» (похмурий, брутальний напрям).

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


# ---------------------------------------------------------------- допоміжне для інструментів
def _ph(seed, i):
    """Детермінована початкова фаза."""
    return float(np.random.default_rng(int(seed) * 131 + int(i)).random())


def _env_attack(n, att_s):
    a = max(int(att_s * SR), 1)
    env = np.ones(n)
    a = min(a, n)
    env[:a] = np.sin(np.linspace(0, np.pi / 2, a)) ** 2
    return env


# ---------------------------------------------------------------- бас
@lru_cache(maxsize=None)
def bass_dist(midi, dur, vel=0.9, dirt=1.0):
    """Брудний бас: пилка+квадрат через вейвшейпер і НЧ-фільтр,
    подвоєний чистим синусом-субом на октаву нижче (і чистою основою)."""
    f = float(mtof(midi))
    n = int((dur + 0.12) * SR)
    t = _t(n)
    raw = 0.6 * saw(f, n) + 0.4 * pulse(f, n, 0.5)
    raw = filt(raw, "lp", 900, 0.7)
    raw = raw / max(np.max(np.abs(raw)), 1e-9)
    dist = np.tanh((1.5 + 5.5 * dirt) * raw)
    dist = filt(filt(dist, "lp", 1500, 0.7), "hp", 45, 0.7)
    dist = dist / max(np.max(np.abs(dist)), 1e-9)
    sub = np.sin(2 * np.pi * f * 0.5 * t) + 0.5 * np.sin(2 * np.pi * f * t)
    sub = sub / max(np.max(np.abs(sub)), 1e-9)
    y = (0.55 * dirt + 0.12) * dist + 0.85 * sub
    y *= np.minimum(t / 0.004, 1.0)
    y = damp(y, int(dur * SR), 0.035)
    return fade(norm(y) * vel, 1.0, 10.0)


# ---------------------------------------------------------------- гітари / лід / духові
@lru_cache(maxsize=None)
def chug(midis, dur, vel=0.8, seed=1, mute=0.09, cab=3500.0):
    """Гітара через перевантаження: стос розстроєних пилок -> жорсткий вейвшейпер ->
    «кабінет» (HP 90 Гц, LP cab). mute = стала згасання долоні (None = тривалий акорд)."""
    n = int((dur + 0.08) * SR)
    t = _t(n)
    acc = np.zeros(n)
    sh = (int(seed) % 5 - 2) * 1.5
    for i, m in enumerate(midis):
        f = float(mtof(m))
        for j, c in enumerate((-11, -2, 6, 13)):
            acc += saw(f * 2.0 ** ((c + sh) / 1200.0), n, phase0=_ph(seed, i * 4 + j))
    acc = filt(acc, "lp", 2800, 0.7)
    acc = acc / max(np.max(np.abs(acc)), 1e-9)
    if mute:
        env = np.minimum(t / 0.0015, 1.0) * np.exp(-t / mute)
    else:
        env = np.minimum(t / 0.006, 1.0) * np.exp(-t / 3.0)
    y = np.tanh(7.0 * acc * env)
    tn = int(0.01 * SR)
    pick = filt(noise(tn, 300 + int(seed)), "bp", 2300, 1.0) * np.exp(-_t(tn) / 0.003)
    y[:tn] += 0.22 * pick / max(np.max(np.abs(pick)), 1e-9)
    y = filt(y, "hp", 90, 0.7, order=2)
    y = filt(y, "lp", cab, 0.7, order=2)
    y = damp(y, int(dur * SR), 0.018 if mute else 0.06)
    return fade(norm(y) * vel, 0.5, 8.0)


@lru_cache(maxsize=None)
def lead(midi, dur, vel=0.8, seed=1, vib=True):
    """Низький перевантажений лід: дві розстроєні пилки + імпульсна, жорсткий кліп, темний кабінет."""
    f = float(mtof(midi))
    n = int((dur + 0.12) * SR)
    t = _t(n)
    vr = 1.0
    if vib:
        vr = 2.0 ** (0.0035 * np.sin(2 * np.pi * 5.4 * t) * np.clip((t - 0.2) / 0.3, 0, 1))
    sh = (int(seed) % 3 - 1) * 2.0
    y = saw(f * 2 ** ((-7 + sh) / 1200) * vr, n, _ph(seed, 1)) + saw(f * 2 ** ((8 + sh) / 1200) * vr, n, _ph(seed, 2))
    y = y + 0.7 * pulse(f * vr, n, 0.4, _ph(seed, 3))
    y = filt(y, "lp", 3000, 0.7)
    y = np.tanh(5.0 * y / max(np.max(np.abs(y)), 1e-9))
    y = filt(filt(y, "hp", 110, 0.7), "lp", 2600, 0.7, order=2)
    y *= np.minimum(t / 0.008, 1.0)
    y = damp(y, int(dur * SR), 0.05)
    return fade(norm(y) * vel, 1.0, 10.0)


@lru_cache(maxsize=None)
def horn(midis, dur, vel=0.8, att=0.07, seed=1, dark=1.0):
    """Низькі духові: 4 розстроєні пилки на ноту, повільна атака, темний зріз, що трохи відкривається."""
    n = int((dur + 0.2) * SR)
    t = _t(n)
    vib = 2.0 ** (0.003 * np.sin(2 * np.pi * 5.2 * t) * np.clip((t - 0.3) / 0.4, 0, 1))
    acc = np.zeros(n)
    for i, m in enumerate(midis):
        f = float(mtof(m))
        for j, c in enumerate((-12, -4, 5, 13)):
            acc += saw(f * 2.0 ** (c / 1200.0) * vib, n, phase0=_ph(seed, i * 4 + j))
    fc = 320 + 850 * dark * (1 - np.exp(-t / max(att * 1.5, 0.02)))
    y = sweep_filter(acc, fc, "lp", 0.9, K=6)
    y *= _env_attack(n, att)
    y = np.tanh(1.3 * y / max(np.max(np.abs(y)), 1e-9))
    y = damp(y, int(dur * SR), 0.09)
    return fade(norm(y) * vel, 2.0, 15.0)


@lru_cache(maxsize=None)
def pad_dark(midis, dur, vel=0.5, att=1.2, rel=0.8, cut=650.0):
    """Темний падь: повільна атака і повільне відпускання, низький зріз."""
    n = int((dur + rel * 3) * SR)
    acc = np.zeros(n)
    for i, m in enumerate(midis):
        f = float(mtof(m))
        for j, c in enumerate((-9, 0, 8)):
            acc += saw(f * 2.0 ** (c / 1200.0), n, phase0=_ph(7, i * 3 + j))
    y = filt(acc, "lp", cut, 0.7, order=2)
    y *= _env_attack(n, att)
    y = damp(y, int(dur * SR), rel)
    return fade(norm(y) * vel, 5.0, 60.0)


@lru_cache(maxsize=None)
def low_bell(midi, dur, vel=0.8, bright=1.0):
    """Низький розстроєний дзвін/піаніно: неспівзвучні гармоніки, пари +-7 центів, удар молоточка."""
    f = float(mtof(midi))
    n = int((dur + 0.5) * SR)
    t = _t(n)
    y = np.zeros(n)
    parts = [(1.0, 1.0, 1.8), (2.0, 0.55, 1.1), (3.01, 0.32, 0.7), (4.17, 0.2 * bright, 0.42), (5.43, 0.12 * bright, 0.25)]
    for k, (r, a, tau) in enumerate(parts):
        for c in (-7, 7):
            if f * r * 2 ** (c / 1200) < 9000:
                y += a * np.sin(2 * np.pi * f * r * 2 ** (c / 1200) * t + 0.7 * k) * np.exp(-t / tau)
    tn = int(0.012 * SR)
    ham = filt(noise(tn, 400 + int(midi)), "lp", 1800) * np.exp(-_t(tn) / 0.003)
    y[:tn] += 0.9 * ham / max(np.max(np.abs(ham)), 1e-9)
    y *= np.minimum(t / 0.0015, 1.0)
    y = filt(y, "lp", 2600 + 1500 * bright, 0.7)
    y = damp(y, int(dur * SR), 0.5)
    return fade(norm(y) * vel, 0.5, 20.0)


# ---------------------------------------------------------------- барабани та удари
@lru_cache(maxsize=None)
def kick(vel=1.0, soft=False):
    """Бочка: довгий суб + жорсткий клац."""
    n = int(0.5 * SR)
    t = _t(n)
    f = 41.0 + 110.0 * np.exp(-t / 0.028)
    ph = 2 * np.pi * np.cumsum(f) / SR
    y = np.sin(ph) * np.exp(-t / 0.20)
    cl = filt(noise(n, 5), "hp", 1800) * np.exp(-t / 0.0018)
    th = filt(noise(n, 6), "bp", 1100, 1.0) * np.exp(-t / 0.008)
    y = y + (0.07 if soft else 0.32) * cl + (0.0 if soft else 0.22) * th
    y = np.tanh(1.9 * y)
    if soft:
        y = filt(y, "lp", 1400)
    return fade(norm(y) * vel, 0.3, 15.0)


@lru_cache(maxsize=None)
def snare(vel=1.0, ghost=False):
    """Товстий малий із коротким «гейтованим» приміщенням."""
    n = int(0.32 * SR)
    t = _t(n)
    f = 165.0 + 60.0 * np.exp(-t / 0.02)
    tone = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t / 0.07)
    tone += 0.5 * np.sin(2 * np.pi * 92 * t) * np.exp(-t / 0.05)
    nz = filt(filt(noise(n, 21), "hp", 1400), "lp", 9500) * np.exp(-t / 0.085)
    room = filt(filt(noise(n, 22), "hp", 420), "lp", 4200) * np.exp(-t / 0.11)
    room *= (t < 0.17) * np.minimum(t / 0.004, 1.0)
    room = fade(room, 0.1, 12.0)
    y = 0.9 * tone + 1.1 * nz + 0.5 * room
    y = np.tanh(1.7 * y)
    if ghost:
        y = filt(y, "lp", 4500)
    return fade(norm(y) * vel * (0.5 if ghost else 1.0), 0.3, 14.0)


@lru_cache(maxsize=None)
def tom(midi, vel=1.0):
    """Підлоговий том: низький, з довгим тілом і ударом палички."""
    f = float(mtof(midi))
    n = int(0.55 * SR)
    t = _t(n)
    fr = f * (1 + 0.55 * np.exp(-t / 0.035))
    y = np.sin(2 * np.pi * np.cumsum(fr) / SR) * np.exp(-t / 0.20)
    y += 0.2 * np.sin(2 * np.pi * f * 1.52 * t) * np.exp(-t / 0.09)
    y += 0.3 * filt(noise(n, 61), "bp", 1400, 1.0) * np.exp(-t / 0.009)
    return fade(norm(np.tanh(1.6 * y)) * vel, 0.3, 18.0)


@lru_cache(maxsize=None)
def crash(vel=1.0, length=3.2):
    n = int(length * SR)
    t = _t(n)
    y = filt(filt(noise(n, 71), "hp", 2600), "lp", 12500)
    y *= (1 - np.exp(-t / 0.006)) * np.exp(-t / 0.95)
    return fade(norm(y) * vel, 0.3, 120.0)


@lru_cache(maxsize=None)
def hat_dull(vel=1.0, open_=False):
    """Глухий хет-«тик»."""
    n = int((0.3 if open_ else 0.07) * SR)
    t = _t(n)
    y = filt(filt(noise(n, 31 + int(open_) * 7), "hp", 5200, 0.9), "lp", 9500)
    y *= np.exp(-t / (0.11 if open_ else 0.014)) * np.minimum(t / 0.0008, 1.0)
    return fade(norm(y) * vel, 0.2, 6.0)


@lru_cache(maxsize=None)
def metal(freq, dur, vel=1.0, seed=1, tau=0.5):
    """Металевий удар (ковадло/ланцюг): неспівзвучні парціали з власним згасанням і биттям."""
    n = int((dur + 0.1) * SR)
    t = _t(n)
    ratios = (1.0, 1.59, 2.32, 2.97, 4.12, 5.44, 7.01)
    amps = (1.0, 0.8, 0.65, 0.5, 0.38, 0.26, 0.16)
    y = np.zeros(n)
    rng = np.random.default_rng(900 + int(seed))
    for k, (r, a) in enumerate(zip(ratios, amps)):
        fr = freq * r * (1 + 0.004 * rng.standard_normal())
        if fr > 11000:
            continue
        tk = tau / (1 + 0.55 * k)
        y += a * np.sin(2 * np.pi * fr * t + rng.random() * 6.28) * np.exp(-t / tk)
        y += 0.5 * a * np.sin(2 * np.pi * fr * 1.007 * t + rng.random() * 6.28) * np.exp(-t / tk)
    tn = int(0.02 * SR)
    st = filt(noise(tn, 700 + int(seed)), "bp", 2400, 0.9) * np.exp(-_t(tn) / 0.004)
    y[:tn] += 1.1 * st / max(np.max(np.abs(st)), 1e-9)
    y *= np.minimum(t / 0.0008, 1.0)
    y = filt(y, "lp", 7000, 0.7)
    y = damp(y, int(dur * SR), 0.2)
    return fade(norm(y) * vel, 0.3, 40.0)


@lru_cache(maxsize=None)
def sub_drop(dur=1.6, f0=78.0, f1=27.0, vel=1.0):
    n = int(dur * SR)
    t = _t(n)
    fr = f1 + (f0 - f1) * np.exp(-t / (dur * 0.28))
    y = np.sin(2 * np.pi * np.cumsum(fr) / SR) * np.exp(-t / (dur * 0.45))
    y = np.tanh(1.4 * y)
    return fade(norm(y) * vel, 2.0, 80.0)


@lru_cache(maxsize=None)
def rev_swell(dur=1.0, vel=1.0, seed=92, f0=250.0, f1=5200.0):
    """Зворотний «крештоп»/наростання: шум з фільтром, що росте, і наростаючою гучністю; обривається в ударі."""
    n = int(dur * SR)
    t = _t(n)
    x = t / t[-1]
    fc = f0 * (f1 / f0) ** x
    y = sweep_filter(noise(n, seed), fc, "bp", 0.9, K=8)
    y *= x ** 2.4
    return fade(norm(y) * vel, 20.0, 1.5)


@lru_cache(maxsize=None)
def whoosh(dur=0.2, f0=3000.0, f1=500.0, vel=1.0, seed=93, peak_at=0.15):
    n = int(dur * SR)
    t = _t(n)
    x = t / t[-1]
    fc = f0 * (f1 / f0) ** x
    y = sweep_filter(noise(n, seed), fc, "bp", 1.3, K=8)
    env = np.where(x < peak_at, (x / peak_at) ** 1.3, ((1 - x) / (1 - peak_at)) ** 1.5)
    return fade(norm(y * env) * vel, 1.0, 10.0)


@lru_cache(maxsize=None)
def thump(f0=110.0, f1=44.0, dur=0.2, tau=0.07, drive=1.6, vel=1.0):
    n = int(dur * SR)
    t = _t(n)
    f = f1 + (f0 - f1) * np.exp(-t / 0.022)
    y = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t / tau)
    y = np.tanh(drive * y)
    return fade(norm(y) * vel, 0.3, 12.0)


def tape(n, seed=3, level=1.0):
    """Легкий стрічковий шум: приглушене шипіння з повільним «диханням». Довжина n."""
    rng = np.random.default_rng(seed)
    x = rng.standard_normal(n)
    y = filt(filt(x, "hp", 700, 0.7), "lp", 6500, 0.7)
    tt = np.arange(n) / SR
    y *= 1.0 + 0.25 * np.sin(2 * np.pi * 0.11 * tt + 1.0)
    return y / max(np.max(np.abs(y)), 1e-9) * 0.05 * level


# ---------------------------------------------------------------- прості кубики для SFX
@lru_cache(maxsize=None)
def steel_click(freq=3100.0, vel=1.0, dur=0.03, body=420.0):
    """Сухий сталевий «тик»: неспівзвучні парціали + шум-удар + короткий глухий низ."""
    n = int(dur * SR)
    t = _t(n)
    y = np.sin(2 * np.pi * freq * t) * np.exp(-t / 0.004)
    y += 0.6 * np.sin(2 * np.pi * freq * 1.78 * t + 1.0) * np.exp(-t / 0.0025)
    y += 0.5 * np.sin(2 * np.pi * freq * 2.91 * t + 2.0) * np.exp(-t / 0.0015)
    y += 0.5 * filt(noise(n, 81), "bp", freq * 1.2, 1.2) * np.exp(-t / 0.0015)
    y += 0.8 * np.sin(2 * np.pi * body * t) * np.exp(-t / 0.006)
    return fade(norm(y) * vel, 0.1, 5.0)
