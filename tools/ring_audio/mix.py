"""Секвенування на сітці 1/16 зі свінгом, гуманізація, панорама, реверб, стискання, ліміт.

Петля будується ЦИКЛІЧНО: ноти, що виходять за кінець такту петлі, загортаються на її
початок; усі лінійні фільтри та реверб — циклічна згортка довжиною петлі. Тому шов
петлі безшовний за побудовою (хвости репризу природно «в'їжджають» у початок).
"""
import numpy as np

from . import loudness as L
from .synth import SR, filt, fast_len

SMOOTH_PRIMES = (2, 3, 5, 7, 11, 13)


def _smooth(n):
    m = n
    for p in SMOOTH_PRIMES:
        while m % p == 0:
            m //= p
    return m == 1


def loop_length(ideal):
    """Найближча до ideal довжина, кратна 480 і гладка (швидкий FFT)."""
    base = int(round(ideal / 480.0))
    for d in range(0, 400):
        for s in (d, -d):
            n = (base + s) * 480
            if n > 0 and _smooth(n):
                return n
    return base * 480


def pan_gains(pan):
    ang = (pan + 1.0) * np.pi / 4.0
    return float(np.cos(ang)), float(np.sin(ang))


class Song:
    JIT_MS = {"drums": 1.0, "bass": 1.6, "keys": 3.0, "brass": 2.0, "guitar": 2.5, "pad": 0.0, "fx": 0.0}

    def __init__(self, bpm, bars, swing=0.0, seed=1, groups=("drums", "bass", "keys", "brass", "guitar", "pad", "fx")):
        ideal = bars * 16 * 60.0 / bpm / 4.0 * SR
        self.n = loop_length(ideal)
        self.bars = bars
        self.step = self.n / (bars * 16)
        self.bpm_nominal = bpm
        self.bpm = 60.0 * SR / (self.step * 4.0)
        self.swing = swing
        self.rng = np.random.default_rng(seed)
        self.bufs = {g: np.zeros((self.n, 2)) for g in groups}

    @property
    def seconds(self):
        return self.n / SR

    def dur(self, steps):
        """Тривалість у секундах (округлена для кешу)."""
        return round(steps * self.step / SR, 2)

    def pos(self, bar, step):
        p = (bar * 16 + step) * self.step
        if self.swing and abs(step - round(step)) < 1e-9 and int(round(step)) % 2 == 1:
            p += self.swing * self.step
        return p

    def put(self, group, sig, bar, step, gain=1.0, pan=0.0, vel_jit=0.06, ms_off=0.0):
        sig = np.asarray(sig)
        jit = self.JIT_MS.get(group, 0.0)
        off = 0.0
        if jit:
            off = float(np.clip(self.rng.normal(0, jit), -3 * jit, 3 * jit))
        off = (off + ms_off) * SR / 1000.0
        g = gain * (1.0 + self.rng.uniform(-vel_jit, vel_jit)) if vel_jit else gain
        l, r = pan_gains(pan)
        # нормуємо так, щоб центр (pan=0) мав коефіцієнт 1
        k = 1.0 / pan_gains(0.0)[0]
        start = int(round(self.pos(bar, step) + off)) % self.n
        buf = self.bufs[group]
        i = 0
        total = len(sig)
        while i < total:
            c = min(total - i, self.n - start)
            seg = sig[i:i + c] * g * k
            buf[start:start + c, 0] += seg * l
            buf[start:start + c, 1] += seg * r
            i += c
            start = 0

    # -------------------------------------------------------------- мікс
    def mixdown(self, gains, sends, ir, bus_filters=None, hp_hz=20.0):
        """Повертає (master, stems). stems — по групах (сухе + власний реверб)."""
        bus_filters = bus_filters or {}
        stems = {}
        for g, buf in self.bufs.items():
            x = buf * gains.get(g, 1.0)
            for kind, fc, qf in bus_filters.get(g, ()):
                x = np.stack([filt(x[:, c], kind, fc, qf, circular=True) for c in range(2)], axis=1)
            s = sends.get(g, 0.0)
            if s > 0:
                x = x + reverb_circ(x.mean(axis=1) * s, ir)
            stems[g] = np.stack([filt(x[:, c], "hp", hp_hz, 0.7071, circular=True) for c in range(2)], axis=1)
        master = sum(stems.values())
        return master, stems


# ---------------------------------------------------------------- реверб
def make_ir(rt60=1.4, seed=7, predelay=0.014):
    """Стерео-імпульс: згасаючий шум, що темнішає з часом."""
    rng = np.random.default_rng(seed)
    n = int((rt60 * 1.15 + predelay) * SR)
    t = np.arange(n) / SR
    pd = int(predelay * SR)
    ir = np.zeros((n, 2))
    for ch in range(2):
        nz = rng.standard_normal(n)
        bright = filt(nz, "lp", 9000)
        dark = filt(nz, "lp", 2000)
        w = np.clip(t / (rt60 * 0.75), 0, 1)
        s = bright * (1 - w) + dark * w
        s *= 10 ** (-3.0 * t / rt60) * (1 - np.exp(-t / 0.006))
        ir[pd:, ch] = s[: n - pd]
    ir /= np.sqrt((ir ** 2).sum() / 2.0)
    return ir


def reverb_circ(mono, ir):
    """Циклічна згортка (довжина = довжина петлі) -> стерео."""
    n = len(mono)
    X = np.fft.rfft(mono)
    out = np.empty((n, 2))
    for c in range(2):
        h = ir[:n, c]
        out[:, c] = np.fft.irfft(X * np.fft.rfft(h, n), n)
    return out


def room(x, rt60=0.25, wet=0.2, seed=5):
    """Нециклічний короткий реверб для SFX (повертає довшу довжину)."""
    ir = make_ir(rt60, seed, predelay=0.004)
    x = np.asarray(x)
    N = fast_len(len(x) + len(ir))
    X = np.fft.rfft(x, N)
    w = np.stack([np.fft.irfft(X * np.fft.rfft(ir[:, c], N), N) for c in range(2)], axis=1)
    out = np.zeros((len(x) + len(ir), 2))
    out[: len(x), 0] += x
    out[: len(x), 1] += x
    out += wet * w[: len(out)]
    return out


# ---------------------------------------------------------------- динаміка
def compress(x, thr_db=-18.0, ratio=2.5, att_ms=15.0, rel_ms=140.0, block=96, circular=True):
    """Піковий компресор по блоках (лінковане стерео)."""
    n = len(x)
    xx = np.concatenate([x, x]) if circular else x
    m = np.abs(xx).max(axis=1)
    nb = len(m) // block
    lv = m[: nb * block].reshape(nb, block).max(axis=1)
    lv_db = 20 * np.log10(lv + 1e-9)
    target = -np.maximum(lv_db - thr_db, 0.0) * (1 - 1.0 / ratio)
    ta = 1 - np.exp(-block / (att_ms * SR / 1000.0))
    tr = 1 - np.exp(-block / (rel_ms * SR / 1000.0))
    g = np.empty(nb)
    cur = 0.0
    for i in range(nb):
        t = target[i]
        cur += (t - cur) * (ta if t < cur else tr)
        g[i] = cur
    gain = 10 ** (g / 20.0)
    gs = np.interp(np.arange(len(xx)), (np.arange(nb) + 0.5) * block, gain)
    out = xx * gs[:, None]
    return out[n:] if circular else out


def limit(x, ceil_db=-1.5, look_ms=3.0, block=48, circular=True):
    """Лімітер з випередженням: мін. по вікну + згладжування того ж розміру
    (гарантує, що підсилення <= потрібного в кожному блоці)."""
    n = len(x)
    W = int(look_ms * SR / 1000.0 / block) + 1
    P = (2 * W + 2) * block
    if circular:
        xx = np.concatenate([x[-P:], x, x[:P]])
        off = P
    else:
        xx = np.concatenate([np.zeros((P, x.shape[1])), x, np.zeros((P, x.shape[1]))])
        off = P
    m = np.abs(xx).max(axis=1)
    nb = len(m) // block
    pk = m[: nb * block].reshape(nb, block).max(axis=1)
    req = np.minimum(1.0, 10 ** (ceil_db / 20.0) / (pk + 1e-12))
    pad = np.pad(req, (W, W), mode="edge")
    gmin = np.min(np.stack([pad[i:i + nb] for i in range(2 * W + 1)]), axis=0)
    pad2 = np.pad(gmin, (W, W), mode="edge")
    gs_b = np.mean(np.stack([pad2[i:i + nb] for i in range(2 * W + 1)]), axis=0)
    gain = np.interp(np.arange(len(xx)), (np.arange(nb) + 0.5) * block, gs_b)
    return (xx * gain[:, None])[off:off + n]


def master_chain(master, target_lufs=-16.0, tp_max=-1.0, comp=(-16.0, 2.2, 18.0, 160.0), lufs_tol=0.1, max_iter=14):
    """Стискання -> підбір підсилення до цілі LUFS -> лімітер до істинного піку.
    Повертає (audio, gain_db_total, info)."""
    y = compress(master, *comp)
    g_db = target_lufs - L.integrated_loudness(y)
    ceil = tp_max - 0.6
    best = None
    for it in range(max_iter):
        z = limit(y * 10 ** (g_db / 20.0), ceil_db=ceil)
        lufs = L.integrated_loudness(z)
        tp = L.true_peak_db(z)
        ok = abs(lufs - target_lufs) <= lufs_tol and tp <= tp_max - 0.05
        if best is None or ok:
            best = (z, g_db, lufs, tp)
        if ok:
            break
        if tp > tp_max - 0.05:
            ceil -= (tp - (tp_max - 0.05)) + 0.02
        g_db += target_lufs - lufs
    z, g_db, lufs, tp = best
    return z, g_db, {"lufs": lufs, "tp": tp, "iter": it + 1}


def to_pcm16(x, seed=17, dither=True):
    x = np.asarray(x, dtype=float)
    if dither:
        rng = np.random.default_rng(seed)
        d = (rng.random(x.shape) - rng.random(x.shape)) / 32768.0
        x = x + d
    return np.clip(np.round(x * 32767.0), -32768, 32767).astype(np.int16)


def write_wav(path, pcm, sr=SR):
    import wave

    pcm = np.asarray(pcm)
    ch = 1 if pcm.ndim == 1 else pcm.shape[1]
    with wave.open(str(path), "wb") as w:
        w.setnchannels(ch)
        w.setsampwidth(2)
        w.setframerate(sr)
        w.writeframes(pcm.astype("<i2").tobytes())


def seam_report(x):
    """x: (n,ch) float. Розрив на шві vs типова різниця сусідніх семплів."""
    x = np.asarray(x, dtype=float)
    d = np.abs(np.diff(x, axis=0)).max(axis=1)
    jump = float(np.abs(x[0] - x[-1]).max())
    k = int(0.01 * SR)
    rms = lambda a: float(np.sqrt(np.mean(a ** 2)))
    return {
        "jump": jump,
        "median_adj": float(np.median(d)),
        "p99_adj": float(np.percentile(d, 99)),
        "max_adj": float(d.max()),
        "rms_end10": rms(x[-k:]),
        "rms_start10": rms(x[:k]),
    }
