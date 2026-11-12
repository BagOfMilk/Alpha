"""Інструменти «живого» рок-гурту для музики меню «Рингу»: гітара (Karplus-Strong + дисторшн + кабінет 4x12),
бас, акустичні барабани. Власний синтез, чистий numpy. Петлі циклічні (шов безшовний за побудовою).

Гітара: розширений Karplus-Strong (шум медіатора, палм-мют = швидше згасання + додаткове згладжування),
пауер-акорди з 2-3 струн, асиметричний двоступеневий м'який обмежувач на передискретизації x2,
кабінет 4x12 як нульофазна АЧХ на шині.
Барабани: бочка (свіп + клац), малий (мода корпусу + пружини + кільце), хет/тарілки (банк металевих мод + шум).
"""
from functools import lru_cache

import numpy as np

from . import loudness as L
from . import mix as X
from . import synth as S
from .synth import SR


def hz(m):
    return 440.0 * 2.0 ** ((m - 69.0) / 12.0)


def _rng(seed):
    return np.random.default_rng(seed)


# ------------------------------------------------------------------ DSP
def _even(x):
    return x if len(x) % 2 == 0 else np.concatenate([x, [0.0]])


def up2(x):
    x = _even(np.asarray(x, dtype=float))
    n = len(x)
    Xf = np.fft.rfft(x)
    Y = np.zeros(n + 1, dtype=complex)
    Y[: len(Xf)] = Xf
    return np.fft.irfft(Y, 2 * n) * 2.0


def down2(x):
    n = len(x) // 2
    Xf = np.fft.rfft(x)
    return np.fft.irfft(Xf[: n // 2 + 1], n) / 2.0


def f1(x, kind, fc, q=0.7071, sr=SR, order=1):
    b, a = S.biquad(kind, fc, q, sr=sr)
    y = x
    for _ in range(order):
        y = S.lfilter_fft(b, a, y, circular=False, tail=int(sr * 0.05))
    return y


def env_exp(n, tau, att=0.0008):
    t = np.arange(n) / SR
    e = np.exp(-t / tau)
    if att > 0:
        a = int(att * SR)
        e[:a] *= np.linspace(0, 1, a)
    return e


def norm(x, peak=1.0):
    m = float(np.max(np.abs(x))) + 1e-12
    return x * (peak / m)


# ------------------------------------------------------------------ Karplus-Strong
def ks(f, dur, T=1.5, a=0.5, passes=1, seed=1, exc_lp=0.5, bright=1.0):
    """Струна: f Гц, dur с, T — стала згасання амплітуди (с), a — вага згладжування (0.5 = класика)."""
    rng = _rng(seed)
    Lh = max(int(SR / f - 0.5 * passes), 8)
    fa = SR / (Lh + 0.5 * passes)
    nz = rng.standard_normal(Lh)
    k = exc_lp ** np.arange(12)
    k /= k.sum()
    ex = np.convolve(np.concatenate([nz[-11:], nz]), k, mode="valid")[: Lh]
    ex = ex - ex.mean()
    # яскравість збудження: змішування з незгладженим шумом
    ex = ex * (1 - 0.5 * (1 - bright)) + 0.0
    cycles = int(dur * SR / Lh) + 3
    d = np.exp(-1.0 / (fa * T))
    out = np.empty(cycles * Lh)
    buf = ex.copy()
    for c in range(cycles):
        out[c * Lh:(c + 1) * Lh] = buf
        for _ in range(passes):
            buf = a * buf + (1 - a) * np.roll(buf, 1)
        buf = buf * d
    ratio = f / fa
    npos = int(dur * SR)
    pos = np.arange(npos) * ratio
    pos = np.minimum(pos, len(out) - 1.001)
    y = np.interp(pos, np.arange(len(out)), out)
    return y


# ------------------------------------------------------------------ гітара
def distort(x, g1=7.0, g2=4.0, bias=0.15, hp=110.0, lp1=5200.0, lp2=7000.0):
    x2 = up2(x)
    sr2 = 2 * SR
    x2 = f1(x2, "hp", hp, 0.7071, sr2)
    x2 = norm(x2)
    y = np.tanh(g1 * x2 + bias) - np.tanh(bias)
    y = f1(y, "lp", lp1, 0.7071, sr2)
    y = f1(y, "hp", 160.0, 0.7071, sr2)
    y = np.tanh(g2 * y / (np.max(np.abs(y)) + 1e-9) + 0.5 * bias) - np.tanh(0.5 * bias)
    y = f1(y, "lp", lp2, 0.7071, sr2, order=2)
    return down2(y)


MODES = {  # режими підсилювача: (g1, g2, hp, lp1, lp2) ; 0 — чисто
    "hi": (9.0, 5.0, 120.0, 5200.0, 7000.0),
    "mid": (5.0, 3.0, 100.0, 5000.0, 6500.0),
    "crunch": (2.4, 1.6, 90.0, 4800.0, 6000.0),
    "clean": (1.0, 0.0, 70.0, 4500.0, 5500.0),
}


@lru_cache(maxsize=6000)
def gtr_note(notes, dur, mute, vel, seed, mode="hi", cents=0.0, T=None, pick=1.0):
    """notes — кортеж MIDI (одна нота = соло/чаг, 2-3 = пауер-акорд). Повертає МОНО (пік ~ vel)."""
    rng = _rng(seed)
    n = int(dur * SR)
    Tm = T if T else (0.12 if mute else 2.2)
    tot = np.zeros(n)
    for i, m in enumerate(notes):
        det = cents + rng.uniform(-3.5, 3.5)
        f = hz(m + det / 100.0)
        ss = ks(f, dur + 0.03, T=Tm, a=0.5, passes=2 if mute else 1, seed=seed * 31 + i, exc_lp=0.55)
        w = 1.0 if i == 0 else 0.8
        tot += ss[:n] * w
    # шум медіатора
    pk = rng.standard_normal(int(0.012 * SR))
    pk = f1(pk, "hp", 1800.0)
    pk = f1(pk, "lp", 5200.0)
    pk *= np.exp(-np.arange(len(pk)) / (0.0035 * SR))
    tot[: len(pk)] += pk * (0.55 if mute else 0.3) * pick * (np.max(np.abs(tot)) + 1e-9)
    if mute:
        tot = f1(tot, "lp", 3200.0)
    tot = norm(tot)
    g1, g2, hp, lp1, lp2 = MODES[mode]
    if g2 > 0:
        y = distort(tot, g1, g2, hp=hp, lp1=lp1, lp2=lp2)
    else:
        x2 = tot
        y = np.tanh(1.6 * x2) / np.tanh(1.6)
        y = f1(y, "hp", hp)
        y = f1(y, "lp", lp1)
    y = y[:n]
    fo = min(int(0.03 * SR), n)
    y[-fo:] *= np.cos(np.linspace(0, np.pi / 2, fo)) ** 2
    y[: int(0.0006 * SR)] *= np.linspace(0, 1, int(0.0006 * SR))
    peak = np.max(np.abs(y)) + 1e-9
    return y / peak * (0.35 + 0.65 * vel) * 0.9


CAB_4X12 = [(20, -60), (45, -42), (70, -16), (90, -3), (110, +3), (130, +2), (180, -1), (300, -3), (450, -6),
            (600, -5), (900, -2), (1500, 0), (2200, +3), (3200, +4), (4200, +2), (5000, -4), (6000, -14),
            (8000, -30), (12000, -46), (24000, -60)]
CAB_COMBO = [(20, -60), (60, -40), (90, -12), (140, +2), (250, +1), (500, -2), (1000, -1), (2000, +1), (3000, +2),
             (4000, 0), (5500, -8), (7500, -26), (12000, -45), (24000, -60)]


def cab_bus(x, pts):
    n = len(x)
    fs = np.array([p[0] for p in pts], dtype=float)
    db = np.array([p[1] for p in pts], dtype=float)
    f = np.fft.rfftfreq(n, 1.0 / SR)
    mag = 10 ** (np.interp(np.log10(np.maximum(f, 1.0)), np.log10(fs), db) / 20.0)
    return np.fft.irfft(np.fft.rfft(x, axis=0) * mag[:, None], n, axis=0)


# ------------------------------------------------------------------ бас
@lru_cache(maxsize=2000)
def bass_note(midi, dur, vel, seed, grit=1.0, T=0.9):
    n = int(dur * SR)
    f = hz(midi)
    y = ks(f, dur + 0.03, T=T, a=0.5, passes=1, seed=seed, exc_lp=0.78)[:n]
    y = f1(y, "hp", 30.0)
    y = norm(y)
    ng = np.arange(int(0.006 * SR))
    nz = f1(_rng(seed + 5).standard_normal(len(ng)), "lp", 2500.0) * np.exp(-ng / (0.002 * SR))
    y[: len(nz)] += nz * 0.25
    dirty = np.tanh(3.0 * grit * y)
    dirty = f1(dirty, "bp", 900.0, 0.8)
    out = y * 0.8 + dirty * 0.55 * min(grit, 1.5)
    out = f1(out, "lp", 3600.0)
    fo = min(int(0.025 * SR), n)
    out[-fo:] *= np.cos(np.linspace(0, np.pi / 2, fo)) ** 2
    out = norm(out) * (0.35 + 0.65 * vel) * 0.9
    return out


# ------------------------------------------------------------------ барабани
@lru_cache(maxsize=500)
def kick(vel=1.0, seed=0, size=1.0, click=1.0, f_end=57.0, tau=0.11):
    n = int(0.55 * SR)
    t = np.arange(n) / SR
    fe = f_end * size
    fr = fe + (fe * 2.9) * np.exp(-t / 0.022)
    ph = 2 * np.pi * np.cumsum(fr) / SR
    body = np.sin(ph) * env_exp(n, tau * size, 0.0007)
    shell = np.sin(2 * np.pi * 96 * t) * env_exp(n, 0.05) * 0.25
    rng = _rng(100 + seed)
    nz = rng.standard_normal(n)
    clk = f1(nz, "bp", 3200.0, 0.8) * env_exp(n, 0.0045, 0.0002) * 0.9 * click * (0.4 + 0.6 * vel)
    thud = f1(nz, "lp", 600.0) * env_exp(n, 0.015) * 0.25
    y = body * 1.1 + shell + clk + thud
    y = np.tanh(1.5 * y) / np.tanh(1.5)
    y = f1(y, "hp", 32.0)
    return norm(y) * (0.45 + 0.55 * vel)


@lru_cache(maxsize=500)
def snare(vel=1.0, seed=0, ring=1.0, crack=1.0, pitch=190.0, wires=1.0, ghost=False):
    n = int(0.6 * SR)
    t = np.arange(n) / SR
    rng = _rng(200 + seed)
    f0 = pitch * (1 + 0.12 * np.exp(-t / 0.012))
    ph = 2 * np.pi * np.cumsum(f0) / SR
    m1 = np.sin(ph) * env_exp(n, 0.075, 0.0004)
    m2 = np.sin(2 * np.pi * pitch * 1.78 * t + 0.4) * env_exp(n, 0.05, 0.0004) * 0.5
    m3 = np.sin(2 * np.pi * pitch * 1.47 * t + 1.1) * env_exp(n, 0.2 * ring, 0.0004) * 0.18 * ring
    nz = rng.standard_normal(n)
    w = f1(nz, "hp", 2200.0)
    w = f1(w, "lp", 9500.0)
    w = w * (env_exp(n, 0.07, 0.0004) * 0.7 + env_exp(n, 0.19, 0.0004) * 0.3) * wires
    st = f1(nz[::-1].copy(), "hp", 4500.0) * env_exp(n, 0.0022, 0.0001) * crack
    shell = f1(nz, "bp", 420.0, 1.5) * env_exp(n, 0.03) * 0.4
    y = 0.9 * m1 + 0.8 * m2 + m3 + 0.85 * w + 0.7 * st + shell
    y = np.tanh(1.2 * y)
    v = (0.28 + 0.72 * vel)
    if ghost:
        y = y * 0.55
    return norm(y) * v


@lru_cache(maxsize=500)
def tom(f0=110.0, vel=1.0, seed=0, tau=0.26):
    n = int(0.8 * SR)
    t = np.arange(n) / SR
    fr = f0 * (1 + 0.38 * np.exp(-t / 0.035))
    ph = 2 * np.pi * np.cumsum(fr) / SR
    y = np.sin(ph) * env_exp(n, tau, 0.0007) + 0.35 * np.sin(1.62 * ph + 0.7) * env_exp(n, tau * 0.45, 0.0007)
    nz = _rng(300 + seed).standard_normal(n)
    y += f1(nz, "bp", 2400.0, 0.9) * env_exp(n, 0.006) * 0.35 * (0.5 + vel)
    y = np.tanh(1.3 * y)
    return norm(f1(y, "hp", 40.0)) * (0.4 + 0.6 * vel)


def _metal(seed, fmin, fmax, nmodes, tau0, slope=0.35, n=None, amp_slope=-0.3):
    rng = _rng(seed)
    f = np.exp(rng.uniform(np.log(fmin), np.log(fmax), nmodes))
    ph = rng.uniform(0, 2 * np.pi, nmodes)
    t = np.arange(n) / SR
    y = np.zeros(n)
    for fi, pi in zip(f, ph):
        tau = tau0 * (fi / 1000.0) ** (-slope)
        a = (fi / 1000.0) ** amp_slope
        y += a * np.sin(2 * np.pi * fi * t + pi) * np.exp(-t / tau)
    return y


@lru_cache(maxsize=300)
def hat(vel=0.7, seed=0, open_=False, pedal=False):
    n = int((0.55 if open_ else 0.16) * SR)
    t = np.arange(n) / SR
    tau = 0.2 if open_ else (0.022 if not pedal else 0.012)
    m = _metal(400 + seed, 3200, 11500, 14, tau * 1.6, slope=0.0, n=n)
    nz = _rng(410 + seed).standard_normal(n)
    nz = f1(nz, "hp", 6500.0)
    y = (m * 0.07 + nz * 0.5) * np.exp(-t / tau)
    y[: int(0.0005 * SR)] *= np.linspace(0, 1, int(0.0005 * SR))
    y = f1(y, "hp", 4800.0)
    y = f1(y, "lp", 14500.0)
    fo = int(0.01 * SR)
    y[-fo:] *= np.cos(np.linspace(0, np.pi / 2, fo)) ** 2
    return norm(y) * (0.25 + 0.75 * vel)


@lru_cache(maxsize=120)
def crash(vel=1.0, seed=0, length=3.4, tau=1.5):
    n = int(length * SR)
    t = np.arange(n) / SR
    m = _metal(500 + seed, 380, 13000, 70, tau, slope=0.45, n=n)
    nz = _rng(510 + seed).standard_normal(n)
    nz = f1(nz, "hp", 2800.0)
    wash = nz * (np.exp(-t / (tau * 0.55)) * 0.7)
    hit = nz * np.exp(-t / 0.012) * 1.4
    y = m * 0.1 + wash + hit
    y[: int(0.0004 * SR)] *= np.linspace(0, 1, int(0.0004 * SR))
    y = f1(y, "hp", 1400.0)
    y = f1(y, "lp", 15500.0)
    fo = int(0.15 * SR)
    y[-fo:] *= np.cos(np.linspace(0, np.pi / 2, fo)) ** 2
    return norm(y) * (0.4 + 0.6 * vel)


@lru_cache(maxsize=120)
def ride(vel=0.6, seed=0, bell=False):
    n = int(1.6 * SR)
    t = np.arange(n) / SR
    m = _metal(600 + seed, 900, 9500, 28, 0.55, slope=0.25, n=n)
    nz = f1(_rng(610 + seed).standard_normal(n), "hp", 5500.0) * np.exp(-t / 0.18) * 0.35
    ping = (np.sin(2 * np.pi * 2650 * t) + 0.6 * np.sin(2 * np.pi * 3790 * t + 1.0)) * np.exp(-t / (0.5 if bell else 0.08))
    y = m * 0.08 + nz + ping * (0.5 if bell else 0.18)
    y = f1(y, "hp", 1200.0)
    y = f1(y, "lp", 13000.0)
    fo = int(0.08 * SR)
    y[-fo:] *= np.cos(np.linspace(0, np.pi / 2, fo)) ** 2
    return norm(y) * (0.3 + 0.7 * vel)


@lru_cache(maxsize=60)
def rim(vel=0.7, seed=0):
    n = int(0.12 * SR)
    t = np.arange(n) / SR
    nz = _rng(700 + seed).standard_normal(n)
    y = np.sin(2 * np.pi * 470 * t) * np.exp(-t / 0.012) + 0.8 * f1(nz, "bp", 1700.0, 1.2) * np.exp(-t / 0.01)
    y += 0.6 * np.sin(2 * np.pi * 980 * t + 0.5) * np.exp(-t / 0.006)
    y[: 40] *= np.linspace(0, 1, 40)
    return norm(y) * (0.4 + 0.6 * vel)


# ------------------------------------------------------------------ гурт (секвенсер)
DRUM_GAIN = dict(K=0.8, S=0.82, H=0.30, O=0.30, C=0.38, R=0.30, T1=0.7, T2=0.7, T3=0.75, X=0.6, G=0.62)
DRUM_PAN = dict(K=0.0, S=0.03, H=-0.35, O=-0.35, C=0.45, R=0.4, T1=-0.3, T2=0.05, T3=0.4, X=0.0, G=0.03)
TOM_F = dict(T1=190.0, T2=140.0, T3=98.0)


class Band:
    """Усі позиції — у шістнадцятих такту (res=16) або в кроках сітки res (12 для 12/8)."""

    def __init__(self, bpm, bars, seed, res=16, hum=2.2):
        self.sg = X.Song(bpm, bars, swing=0.0, seed=seed, groups=("drums", "bass", "gtr", "lead"))
        self.res = res
        self.rng = np.random.default_rng(seed + 1000)
        self.hum = hum
        self.cnt = {}
        self.bars = bars

    def st(self, s):
        return s * 16.0 / self.res

    def sec(self, steps):
        """Тривалість у секундах для довжини в кроках сітки (округлена для кешу)."""
        return round(steps * 16.0 / self.res * self.sg.step / SR, 2)

    def jit(self, extra=0.0, sd=None):
        sd = self.hum if sd is None else sd
        return float(np.clip(self.rng.normal(0, sd), -3 * sd, 3 * sd)) + extra

    # --- барабани
    def hit(self, inst, bar, step, vel=0.8, **kw):
        sg = self.sg
        c = self.cnt.get(inst, 0)
        self.cnt[inst] = c + 1
        var = c % 4
        v = round(vel, 1)
        lay = 0.0
        if inst == "K":
            sig = kick(v, var, **kw)
        elif inst == "S":
            lay = 3.0
            sig = snare(v, var, **kw)
        elif inst in ("H", "O"):
            sig = hat(v, var, open_=(inst == "O"), **kw)
        elif inst == "C":
            sig = crash(v, var % 2, **kw)
        elif inst == "R":
            sig = ride(v, var, **kw)
        elif inst in TOM_F:
            sig = tom(TOM_F[inst] * kw.pop("scale", 1.0), v, var, **kw)
        elif inst == "X":
            sig = rim(v, var)
        elif inst == "G":
            sig = snare(v, var, ghost=True, ring=0.3, crack=0.4)
        else:
            raise ValueError(inst)
        sg.put("drums", sig, bar, self.st(step), gain=DRUM_GAIN[inst], pan=DRUM_PAN[inst], vel_jit=0.05,
               ms_off=self.jit(lay, 1.6 if inst in "KS" else self.hum))

    def pat(self, bar, spec, vscale=1.0, **kw):
        """spec: {'K': '9..5....', ...} — символ = крок сітки; цифра 1-9 = гучність/9."""
        for inst, s in spec.items():
            for i, ch in enumerate(s):
                if ch in ".- ":
                    continue
                if ch.isdigit():
                    v = int(ch) / 9.0 * vscale
                    self.hit(inst, bar, i, min(v, 1.0))
                elif ch == "o":
                    self.hit("O", bar, i, 0.7 * vscale)

    def fill(self, bar, start, kind="16", vscale=1.0):
        """Філ томами від кроку start до кінця такту (res=16)."""
        seqs = {
            "16": ["S", "S", "T1", "T1", "T2", "T2", "T3", "T3"],
            "down": ["T1", "T1", "T2", "T2", "T3", "T3", "K", "K"],
            "snare": ["S"] * 8,
            "half": ["T1", "T2", "T3", "T3"],
        }[kind]
        steps = 16 - start
        cnt = len(seqs)
        for j, inst in enumerate(seqs):
            pos = start + j * steps / cnt
            v = (0.55 + 0.4 * j / cnt) * vscale
            self.hit(inst, bar, pos, min(v, 1.0))
        self.hit("C", bar + 1 if bar + 1 < self.bars else 0, 0, 1.0 * vscale)

    # --- гітара
    def gt(self, bar, step, notes, dur, mute=False, vel=0.85, mode="hi", pan=0.8, g=0.5, T=None, group="gtr", pick=1.0):
        """Дві доріжки L/R з різним мікротаймінгом і строєм. dur — у кроках сітки."""
        sec = self.sec(dur)
        for side, seed, cents in ((-1, 11, -4.0), (1, 22, 5.0)):
            sig = gtr_note(tuple(notes), sec, mute, round(vel, 1), seed + int(notes[0]), mode, cents, T, pick)
            self.sg.put(group, sig, bar, self.st(step), gain=g, pan=side * pan, vel_jit=0.06,
                        ms_off=self.jit(0.0, 2.0))

    def solo_gt(self, bar, step, notes, dur, vel=0.85, mode="mid", pan=0.2, g=0.55, T=None, group="lead"):
        sec = self.sec(dur)
        sig = gtr_note(tuple(notes), sec, False, round(vel, 1), 33 + int(notes[0]), mode, 0.0, T, 1.0)
        self.sg.put(group, sig, bar, self.st(step), gain=g, pan=pan, vel_jit=0.05, ms_off=self.jit(0.0, 2.0))

    # --- бас
    def bs(self, bar, step, midi, dur, vel=0.85, g=0.9, grit=1.0, T=0.9):
        sec = self.sec(dur)
        sig = bass_note(int(midi), sec, round(vel, 1), 7 + int(midi), grit, T)
        self.sg.put("bass", sig, bar, self.st(step), gain=g, pan=0.0, vel_jit=0.05, ms_off=self.jit(0.0, 1.5))


def parse(s):
    """'x.xx..' -> позиції кроків."""
    return [i for i, ch in enumerate(s) if ch not in ". -"]


def bass_reg(root, lo=30, hi=43):
    r = root
    while r > hi:
        r -= 12
    while r < lo:
        r += 12
    return r


# ------------------------------------------------------------------ мастер
def render(band, cfg):
    """cfg: gains, sends, rt60, cab(список), comp, bass_lp, sat, sections[(a,b,label)], key."""
    sg = band.sg
    for gname in ("gtr", "lead"):
        sg.bufs[gname] = cab_bus(sg.bufs[gname], cfg.get("cab", CAB_4X12))
    ir = X.make_ir(cfg.get("rt60", 0.55), seed=9, predelay=0.008)
    bus = {"bass": [("lp", cfg.get("bass_lp", 3800.0), 0.7071)], "drums": [("hp", 28.0, 0.7071)]}
    master, stems = sg.mixdown(cfg["gains"], cfg["sends"], ir, bus_filters=bus, hp_hz=24.0)
    master = master * (10 ** (-22.0 / 20.0) / np.sqrt(np.mean(master ** 2)))
    k = cfg.get("sat", 1.3)
    master = np.tanh(k * master) / k
    y, g_db, info = X.master_chain(master, -16.0, -1.0, cfg.get("comp", (-17.0, 2.0, 12.0, 120.0)))
    pcm = X.to_pcm16(y)
    yq = pcm.astype(float) / 32768.0
    return dict(pcm=pcm, yq=yq, seconds=sg.n / SR, bpm=sg.bpm, lufs=L.integrated_loudness(yq),
                tp=L.true_peak_db(yq), seam=X.seam_report(yq), stems=stems, bars=sg.bars)
