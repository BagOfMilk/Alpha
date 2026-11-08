"""Гучність за ITU-R BS.1770-4 (LUFS) і істинний пік (dBTP). Чистий numpy.

Запуск самоперевірки:  python -m ring_audio.loudness
"""
import numpy as np

from .synth import lfilter_fft

SR = 48000
# Стандартні коефіцієнти K-зважування для 48 кГц (BS.1770-4, табл. 1-2)
_B1 = np.array([1.53512485958697, -2.69169618940638, 1.19839281085285])
_A1 = np.array([1.0, -1.69065929318241, 0.73248077421585])
_B2 = np.array([1.0, -2.0, 1.0])
_A2 = np.array([1.0, -1.99004745483398, 0.99007225036621])


def _as2d(x):
    x = np.asarray(x, dtype=float)
    return x[:, None] if x.ndim == 1 else x


def k_weight(x):
    """x: (n,) або (n,ch)."""
    x = _as2d(x)
    out = np.empty_like(x)
    for c in range(x.shape[1]):
        y = lfilter_fft(_B1, _A1, x[:, c], tail=SR)
        out[:, c] = lfilter_fft(_B2, _A2, y, tail=SR)
    return out


def integrated_loudness(x):
    """Інтегральна гучність, LUFS (400 мс блоки, перекриття 75 %, абсолютний
    гейт -70 LUFS, відносний -10 LU). Усі канали з вагою 1 (стерео/моно)."""
    y = k_weight(x)
    B, H = int(0.4 * SR), int(0.1 * SR)
    if len(y) < B:
        return clip_loudness(x)
    cs = np.concatenate([np.zeros((1, y.shape[1])), np.cumsum(y * y, axis=0)])
    starts = np.arange(0, len(y) - B + 1, H)
    z = ((cs[starts + B] - cs[starts]) / B).sum(axis=1)
    lb = -0.691 + 10 * np.log10(z + 1e-20)
    ok = lb > -70.0
    if not ok.any():
        return -70.0
    thr = -0.691 + 10 * np.log10(z[ok].mean()) - 10.0
    ok2 = ok & (lb > thr)
    if not ok2.any():
        return -70.0
    return float(-0.691 + 10 * np.log10(z[ok2].mean()))


def clip_loudness(x):
    """Гучність усього кліпу як одного вікна (для коротких SFX < 400 мс), LUFS."""
    y = k_weight(x)
    z = (y * y).mean(axis=0).sum()
    return float(-0.691 + 10 * np.log10(z + 1e-20))


def momentary_max(x):
    """Макс. миттєва гучність (400 мс, кроком 100 мс; короткі кліпи доповнюються нулями)."""
    y = k_weight(x)
    B, H = int(0.4 * SR), int(0.1 * SR)
    if len(y) < B:
        y = np.concatenate([y, np.zeros((B - len(y), y.shape[1]))])
    cs = np.concatenate([np.zeros((1, y.shape[1])), np.cumsum(y * y, axis=0)])
    starts = np.arange(0, len(y) - B + 1, H)
    z = ((cs[starts + B] - cs[starts]) / B).sum(axis=1)
    return float((-0.691 + 10 * np.log10(z + 1e-20)).max())


def true_peak_db(x, oversample=4):
    """Істинний пік: 4x передискретизація доповненням нулями у спектрі, dBTP."""
    x = _as2d(x)
    best = 0.0
    n = len(x)
    for c in range(x.shape[1]):
        X = np.fft.rfft(x[:, c])
        y = np.fft.irfft(X, n=n * oversample) * oversample
        best = max(best, float(np.max(np.abs(y))), float(np.max(np.abs(x[:, c]))))
    return float(20 * np.log10(best + 1e-12))


def sample_peak_db(x):
    return float(20 * np.log10(np.max(np.abs(x)) + 1e-12))


def self_test():
    t = np.arange(SR * 5) / SR
    full = np.sin(2 * np.pi * 1000 * t)
    res = {
        "1 кГц, 0 dBFS, 1 канал": integrated_loudness(full),
        "1 кГц, 0 dBFS, стерео (однакові канали)": integrated_loudness(np.stack([full, full], 1)),
        "1 кГц, -20 dBFS, стерео": integrated_loudness(np.stack([full, full], 1) * 0.1),
        "1 кГц, -20 dBFS, 1 канал": integrated_loudness(full * 0.1),
        "істинний пік синуса 997 Гц, 0 dBFS (дБ)": true_peak_db(np.sin(2 * np.pi * 997 * t)),
    }
    return res


if __name__ == "__main__":
    for k, v in self_test().items():
        print(f"{k}: {v:.3f}")
