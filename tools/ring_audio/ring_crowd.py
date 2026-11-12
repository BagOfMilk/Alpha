"""Натовп і сигнали QTE для «Рингу» — три нові варіанти на кожен відхилений слот.

Слоти: crowd_shouting (петля), qte_prompt, qte_miss. Усе мономіксом, 48 кГц / 16 біт.
Гул натовпу — обробка двох CC0-записів з репозиторію (crowd_shouting.ogg, applause_hall.wav);
сигнали QTE — власний синтез (numpy). Детерміновано: фіксовані seed.

Запуск з теки tools:
    python -m ring_audio.ring_crowd --out <тека>
OGG читає службовий пакет soundfile (у гру не йде). Якщо його поставлено не глобально, а в окрему теку
(pip install --target <тека> soundfile), шлях до неї — у змінній оточення RING_AUDIO_PYLIB.
"""
import argparse
import os
import sys
from pathlib import Path

import numpy as np

_PYLIB = os.environ.get("RING_AUDIO_PYLIB")
if _PYLIB and Path(_PYLIB).exists():
    sys.path.insert(0, _PYLIB)

from . import loudness as L
from .mix import limit, make_ir, to_pcm16, write_wav
from .synth import SR, biquad, fade, filt, lfilter_fft, noise, sweep_filter

REPO = Path(__file__).resolve().parents[2]
CC0 = REPO / "Assets" / "ThirdParty" / "CC0" / "Audio" / "Ring"


# ---------------------------------------------------------------- читання
def _resample(x, sr_in):
    if sr_in == SR:
        return x
    n_out = int(round(len(x) * SR / sr_in))
    X = np.fft.rfft(x)
    Y = np.zeros(n_out // 2 + 1, dtype=complex)
    k = min(len(X), len(Y))
    Y[:k] = X[:k]
    return np.fft.irfft(Y, n_out) * (n_out / len(x))


def load_mono(path):
    import soundfile as sf

    x, sr = sf.read(str(path), always_2d=True)
    return _resample(x.mean(axis=1), sr)


# ---------------------------------------------------------------- очищення гулу
def _stft(x, N=2048, hop=512):
    w = np.sqrt(np.hanning(N + 1)[:-1])
    pad = np.concatenate([np.zeros(N), x, np.zeros(N + hop)])
    nf = (len(pad) - N) // hop + 1
    idx = np.arange(N)[None, :] + hop * np.arange(nf)[:, None]
    return np.fft.rfft(pad[idx] * w, axis=1), w, len(x)


def _istft(X, w, n, N=2048, hop=512):
    fr = np.fft.irfft(X, N, axis=1) * w
    out = np.zeros(N + hop * (len(fr) - 1))
    for i, f in enumerate(fr):
        out[i * hop:i * hop + N] += f
    return out[N:N + n] / 2.0  # sqrt-hann^2 = hann; сума при 75 % перекритті = 2


def spectral_clamp(x, c_full=1.6, c_low=1.25, low_hz=260.0):
    """Спектральний вирівнювач: у кожній комірці час-частота амплітуда не може перевищити
    c * (медіана часу для цієї частоти). Прибирає ревіння низу, виття й різкі піки,
    не вирізаючи шматків (без розривів). На низу (до low_hz) обмеження суворіше."""
    X, w, n = _stft(x)
    mag = np.abs(X)
    ref = np.median(mag, axis=0)
    ref = np.convolve(ref, np.ones(9) / 9, mode="same")  # згладити за частотою
    fr = np.fft.rfftfreq(2048, 1 / SR)
    c = np.where(fr < low_hz, c_low, c_full)
    g = np.minimum(1.0, c * ref[None, :] / (mag + 1e-12))
    g = np.minimum.reduce([g, np.roll(g, 1, 0), np.roll(g, -1, 0)])  # мін. по 3 кадрах
    g = (g + np.roll(g, 1, 0) + np.roll(g, -1, 0)) / 3.0
    return _istft(X * g, w, n)


def band_track(x, edges, step=0.25):
    """Енергія (дБ) по смугах і кадрах step с — для звіту про вади."""
    n = int(step * SR)
    out = []
    for i in range(len(x) // n):
        s = x[i * n:(i + 1) * n] * np.hanning(n)
        S = np.abs(np.fft.rfft(s)) ** 2
        fr = np.fft.rfftfreq(n, 1 / SR)
        out.append([10 * np.log10(S[(fr >= a) & (fr < b)].sum() + 1e-12) for a, b in edges])
    return np.array(out)


def circ_smooth_rms(x, win_s):
    n = len(x)
    k = int(win_s * SR)
    box = np.zeros(n)
    box[:k] = 1.0 / k
    box = np.roll(box, -k // 2)
    e = np.fft.irfft(np.fft.rfft(x * x) * np.fft.rfft(box), n)
    return np.sqrt(np.maximum(e, 1e-12))


def leveler(x, win_s=1.2, power=0.8, lim=(0.5, 2.0)):
    """Повільне вирівнювання (циклічне): гул не «гуляє»."""
    env = circ_smooth_rms(x, win_s)
    g = np.clip((np.median(env) / env) ** power, *lim)
    return x * g


def make_loop(y, xf_s):
    """Безшовна петля: хвіст (природне продовження) вплавляється в початок рівно-потужним кросфейдом."""
    xf = int(xf_s * SR)
    L_ = len(y) - xf
    out = y[:L_].copy()
    t = np.linspace(0, np.pi / 2, xf)
    out[:xf] = y[L_:L_ + xf] * np.cos(t) + y[:xf] * np.sin(t)
    return out


def circ_add(dst, src, at):
    n = len(dst)
    idx = (at + np.arange(len(src))) % n
    np.add.at(dst, idx, src)


def conv_tail(x, ir):
    N = int(2 ** np.ceil(np.log2(len(x) + len(ir))))
    return np.fft.irfft(np.fft.rfft(x, N) * np.fft.rfft(ir, N), N)[:len(x) + len(ir)]


def mud_cut(y, k=0.5, fc=350.0, qf=0.6):
    b, a = biquad("bp", fc, qf)
    return y - k * lfilter_fft(b, a, y)


# ---------------------------------------------------------------- родина «crowd_shouting»
def _crowd_base(src, hp, lp, lp_order, mud, c_full, c_low):
    y = spectral_clamp(src, c_full, c_low)
    y = filt(y, "hp", hp, 0.7071, order=2)
    y = mud_cut(y, mud)
    y = filt(y, "lp", lp, 0.7071, order=lp_order)
    return y


def _finish_loop(y, target_lufs, tp_max=-1.0):
    g = 10 ** ((target_lufs - L.integrated_loudness(y)) / 20.0)
    y = y * g
    for _ in range(3):
        y = limit(y[:, None], ceil_db=-3.0)[:, 0]
        d = target_lufs - L.integrated_loudness(y)
        if abs(d) < 0.15:
            break
        y = y * 10 ** (d / 20.0)
    return y


def crowd_v1(src):
    """Спокійний гомін до матчу: приглушений, без верхів, дуже рівний."""
    y = _crowd_base(src, hp=170, lp=2300, lp_order=2, mud=0.6, c_full=1.35, c_low=1.1)
    y = make_loop(y, 1.5)
    y = leveler(y, win_s=1.5, power=0.95, lim=(0.4, 2.5))
    return _finish_loop(y, -26.0)


def crowd_v2(src):
    """Рівний гул арени для матчу: повний середній діапазон, без низу й каламуті."""
    y = _crowd_base(src, hp=160, lp=5500, lp_order=1, mud=0.55, c_full=1.6, c_low=1.2)
    y = make_loop(y, 1.5)
    y = leveler(y, win_s=1.2, power=0.85)
    return _finish_loop(y, -23.5)


def crowd_v3(src, applause):
    """Живіша арена: v2 + далекі вигуки (сніпети самого гулу, змінений тон, у реверб) + шар оплесків."""
    y = _crowd_base(src, hp=150, lp=6500, lp_order=1, mud=0.5, c_full=1.8, c_low=1.2)
    y = make_loop(y, 1.5)
    n = len(y)
    rng = np.random.default_rng(303)

    # шар далеких оплесків: шматок оплесків, очищений, у петлю тієї ж довжини
    xf = int(1.5 * SR)
    a0 = int(4.0 * SR)
    ap = applause[a0:a0 + n + xf]
    ap = spectral_clamp(ap, 1.5, 1.2)
    ap = filt(ap, "hp", 400, 0.7071, order=2)
    ap = filt(ap, "lp", 5500, 0.7071, order=1)
    ap = make_loop(ap, 1.5)
    ap = ap * (np.sqrt(np.mean(y ** 2)) / np.sqrt(np.mean(ap ** 2))) * 10 ** (-9.5 / 20)

    # далекі вигуки: вибрати вікна з найбільшою часткою 700-2500 Гц, пересунути тон, віддалити
    ir = make_ir(0.9, seed=11, predelay=0.02)[:, 0]
    seg = int(0.8 * SR)
    hop = int(0.4 * SR)
    sc = []
    for i in range(0, n - seg, hop):
        s = y[i:i + seg] * np.hanning(seg)
        S = np.abs(np.fft.rfft(s)) ** 2
        fr = np.fft.rfftfreq(seg, 1 / SR)
        sc.append((S[(fr > 700) & (fr < 2500)].sum() / S.sum(), i))
    sc.sort(reverse=True)
    picks = []
    for _, i in sc:
        if all(abs(i - j) > 4 * SR for j in picks):
            picks.append(i)
        if len(picks) == 6:
            break
    shouts = np.zeros(n)
    slots = np.linspace(2.5, 23.5, 6) * SR + rng.integers(-SR // 2, SR // 2, 6)
    for k, i in enumerate(picks):
        s = y[i:i + seg]
        rate = [0.92, 1.07, 0.96, 1.1, 0.9, 1.03][k]
        s = np.interp(np.arange(0, len(s) - 1, rate), np.arange(len(s)), s)
        s = filt(s, "hp", 500, 0.7071, order=2)
        s = filt(s, "lp", 2800, 0.7071, order=2)
        s = fade(s * np.hanning(len(s)) ** 0.5, 60, 120)
        wet = conv_tail(s, ir)
        wet *= 0.55 / (np.abs(wet).max() + 1e-9) * 0.5
        circ_add(shouts, wet * np.sqrt(np.mean(y ** 2)) * 10 ** (-1.0 / 20) * 6.0, int(slots[k]))

    # повільні «хвилі» ажіотажу: цілі періоди за петлю -> безшовно
    tt = np.arange(n) / n
    swell = 1 + 0.12 * np.sin(2 * np.pi * 3 * tt + 0.7) + 0.08 * np.sin(2 * np.pi * 7 * tt + 2.1)
    z = (y + ap + shouts) * swell
    z = leveler(z, win_s=1.0, power=0.6, lim=(0.6, 1.6))
    return _finish_loop(z, -22.5), picks


# ---------------------------------------------------------------- QTE: допоміжне
def _t(n):
    return np.arange(n) / SR


def _exp(n, tau):
    return np.exp(-_t(n) / tau)


def _tone_drop(n, f0, f1, tau_f):
    t = _t(n)
    f = f1 + (f0 - f1) * np.exp(-t / tau_f)
    return np.sin(2 * np.pi * np.cumsum(f) / SR)


def _membrane(n, f0, ratios=(1.0, 1.59, 2.14, 2.65), amps=(1.0, 0.5, 0.3, 0.18), tau=0.06, drop=1.35, seed=1):
    """Мембрана: кілька неточно гармонічних мод зі спадом висоти й різним згасанням."""
    t = _t(n)
    y = np.zeros(n)
    for r, a, k in zip(ratios, amps, range(4)):
        f = f0 * r * (1 + (drop - 1) * np.exp(-t / 0.03))
        ph = np.random.default_rng(seed + k).random() * 2 * np.pi
        y += a * np.sin(2 * np.pi * np.cumsum(f) / SR + ph) * np.exp(-t / (tau / (1 + 0.7 * k)))
    return y


def _end(y, ms=10):
    y = filt(y, "hp", 55, 0.7071, order=2)  # без підлоги нижче 55 Гц: її не відтворять динаміки, лише «гуде»
    return fade(y, 0.0, ms)


def _tp_ok(y):
    return L.true_peak_db(y)


def _set_loudness(y, lufs, tp_max=-1.2):
    """Підігнати гучність кліпу до LUFS, але з обмеженням істинного піку."""
    y = y * 10 ** ((lufs - L.clip_loudness(y)) / 20.0)
    for _ in range(40):
        tp = _tp_ok(y)
        if tp <= tp_max:
            break
        y = y * 10 ** ((tp_max - tp - 0.05) / 20.0)
    return y


def _soft_clip(x, drive):
    return np.tanh(x * drive) / np.tanh(drive)


# ---------------------------------------------------------------- QTE: сигнал появи (qte_prompt)
def prompt_v1():
    """Сухий удар великого барабана з клацом шкіри."""
    n = int(0.30 * SR)
    drum = _membrane(n, 96.0, tau=0.04, drop=1.5, seed=5)
    drum = _soft_clip(drum, 1.8)
    skin = filt(noise(n, 11), "bp", 2600, 0.8, order=1) * _exp(n, 0.004) * 1.4  # клац шкіри
    skin += filt(noise(n, 12), "hp", 900, 0.7071, order=1) * _exp(n, 0.012) * 0.4
    thump = _tone_drop(n, 150, 78, 0.02) * _exp(n, 0.03)
    y = drum * 0.9 + skin + thump * 0.8
    y = filt(y, "lp", 7000, 0.7071, order=1)
    return _end(y, 12)


def prompt_v2():
    """Ляпас ременя: подвійний тріск і низький підбій."""
    n = int(0.26 * SR)
    rng_noise = noise(n, 21)
    crack1 = filt(rng_noise, "bp", 3200, 0.9, order=1) * _exp(n, 0.0028)
    d = int(0.0045 * SR)
    crack2 = np.zeros(n)
    crack2[d:] = (filt(noise(n, 22), "bp", 2000, 0.9, order=1) * _exp(n, 0.004))[:n - d]
    flesh = filt(noise(n, 23), "lp", 900, 0.7071, order=2) * _exp(n, 0.02)  # м'яка шкіряна подушка
    boom = _tone_drop(n, 140, 80, 0.02) * _exp(n, 0.026)
    y = crack1 * 1.3 + crack2 * 0.8 + flesh * 1.1 + boom * 1.1
    y = filt(y, "lp", 6500, 0.7071, order=1)
    y = _soft_clip(y, 1.4)
    return _end(y, 10)


def prompt_v3():
    """Короткий наростаючий шум у різкий клац (дерево/сталь) і різкий обрив."""
    rise_n = int(0.17 * SR)
    n = int(0.26 * SR)
    t = np.arange(rise_n) / rise_n
    fc = 500 * (3600 / 500) ** t
    nz = noise(rise_n, 31)
    r = sweep_filter(nz, fc, "bp", qf=0.9, K=7)
    r *= (0.12 + 0.88 * t ** 2.2)  # починається одразу (не з нуля), росте
    r = r / (np.abs(r).max() + 1e-9) * 0.55
    y = np.zeros(n)
    y[:rise_n] = r
    k = int(0.09 * SR)
    # клац: коротка пачка + затухаючі невисокі неточні резонанси «дошки»
    tt = _t(n - rise_n)
    clk = filt(noise(n - rise_n, 32), "hp", 700, 0.7071, order=1) * np.exp(-tt / 0.0025)
    body = (np.sin(2 * np.pi * 410 * tt) * 0.4 + np.sin(2 * np.pi * 1130 * tt + 1) * 0.25
            + np.sin(2 * np.pi * 2310 * tt + 2) * 0.1) * np.exp(-tt / 0.008)
    thud = _tone_drop(n - rise_n, 160, 85, 0.012) * np.exp(-tt / 0.022) * 0.5
    y[rise_n:] += clk * 0.8 + body * 0.6 + thud
    y = filt(y, "lp", 7000, 0.7071, order=1)
    return _end(y, 12), k


# ---------------------------------------------------------------- QTE: пропуск (qte_miss)
def miss_v1():
    """Глухий удар у живіт: приглушений бас і короткий спадний подих."""
    n = int(0.50 * SR)
    t = _t(n)
    punch = _tone_drop(n, 150, 68, 0.04) * np.exp(-t / 0.07)
    body = filt(noise(n, 41), "lp", 260, 0.7071, order=2) * np.exp(-t / 0.05) * 2.2
    air = sweep_filter(noise(n, 42), 1500 * (260 / 1500) ** (t / t[-1]), "lp", qf=0.7071, K=6)
    air *= np.exp(-t / 0.16) * (1 - np.exp(-t / 0.012)) * 0.55
    y = punch * 1.0 + body + air
    y = filt(y, "lp", 1800, 0.7071, order=1)
    y = _soft_clip(y, 1.5)
    return _end(y, 25)


def miss_v2():
    """Здутий спуск: шумовий свист повітря, що падає зверху вниз, під приглушеним клацом."""
    n = int(0.55 * SR)
    t = _t(n)
    u = t / t[-1]
    fc = 2600 * (200 / 2600) ** (u ** 0.8)
    hiss = sweep_filter(noise(n, 51), fc, "bp", qf=1.1, K=9)
    env = np.exp(-t / 0.2) * (1 - np.exp(-t / 0.01))
    hiss = hiss * env
    hiss /= np.abs(hiss).max() + 1e-9
    thud = filt(noise(n, 52), "lp", 180, 0.7071, order=2) * np.exp(-t / 0.035) * 2.0
    thud += _tone_drop(n, 120, 72, 0.025) * np.exp(-t / 0.05) * 0.8
    y = hiss * 0.6 + thud
    y = filt(y, "lp", 4000, 0.7071, order=1)
    return _end(y, 30)


def miss_v3():
    """Важкий мішок падає на килим: глухий шльоп і шурхіт тканини, що стихає."""
    n = int(0.58 * SR)
    t = _t(n)
    slap = filt(noise(n, 61), "lp", 700, 0.7071, order=2) * np.exp(-t / 0.028) * (1 - np.exp(-t / 0.002)) * 2.0
    slap += filt(noise(n, 65), "bp", 1400, 0.9, order=1) * np.exp(-t / 0.006) * 0.35
    low = _tone_drop(n, 120, 66, 0.03) * np.exp(-t / 0.06)
    rng = np.random.default_rng(62)
    shuf = filt(noise(n, 63), "bp", 650, 0.5, order=1)
    gate = np.zeros(n)
    for c, a in ((0.10, 0.5), (0.18, 0.35), (0.27, 0.25), (0.36, 0.14)):
        i = int(c * SR + rng.integers(-300, 300))
        m = n - i
        gate[i:] += a * np.exp(-_t(m) / 0.035) * (1 - np.exp(-_t(m) / 0.008))
    y = slap + low * 0.9 + shuf * gate * 0.7
    y = filt(y, "lp", 2400, 0.7071, order=1)
    y = _soft_clip(y, 1.3)
    return _end(y, 30)


# ---------------------------------------------------------------- вивід, спектрограми
def lead_silence_ms(y, thr=0.01):
    idx = np.nonzero(np.abs(y) > thr)[0]
    return 1000.0 * (idx[0] if len(idx) else len(y)) / SR


def spec_data(y, short):
    N, hop = (1024, 64) if short else (4096, 1024)
    w = np.hanning(N)
    nf = max(1, (len(y) - N) // hop + 1)
    idx = np.arange(N)[None, :] + hop * np.arange(nf)[:, None]
    yy = np.concatenate([y, np.zeros(N)])
    S = np.abs(np.fft.rfft(yy[idx] * w, axis=1)).T
    S = 20 * np.log10(S / (N / 4) + 1e-9)
    fr = np.fft.rfftfreq(N, 1 / SR)
    m = (fr >= 30) & (fr <= 16000)
    return S[m], fr[m], hop / SR


def draw(ax_w, ax_s, y, name, short):
    S, fr, dt = spec_data(y, short)
    t = np.arange(len(y)) / SR
    step = max(1, len(y) // 4000)
    ax_w.plot(t[::step], y[::step], lw=0.5, color="#222")
    ax_w.set_xlim(0, len(y) / SR)
    ax_w.set_ylim(-1, 1)
    ax_w.set_title(name, fontsize=8)
    ax_w.tick_params(labelsize=6)
    tx = np.arange(S.shape[1]) * dt
    ax_s.pcolormesh(tx, fr, S, vmin=-100, vmax=-20, cmap="magma", shading="auto")
    ax_s.set_yscale("log")
    ax_s.set_ylim(30, 16000)
    ax_s.set_xlim(0, len(y) / SR)
    ax_s.tick_params(labelsize=6)


def save_spec(y, path, name, short):
    import matplotlib

    matplotlib.use("Agg")
    import matplotlib.pyplot as plt

    fig, (a, b) = plt.subplots(2, 1, figsize=(9, 5), gridspec_kw={"height_ratios": [1, 3]})
    draw(a, b, y, name, short)
    fig.tight_layout()
    fig.savefig(path, dpi=110)
    plt.close(fig)


def save_sheet(items, path):
    import matplotlib

    matplotlib.use("Agg")
    import matplotlib.pyplot as plt

    fig = plt.figure(figsize=(17, 11))
    gs = fig.add_gridspec(6, 3, height_ratios=[1, 3, 1, 3, 1, 3])
    slots = ["crowd_shouting", "qte_prompt", "qte_miss"]
    for r, slot in enumerate(slots):
        for c in range(3):
            iid, y = items[f"{slot}.v{c + 1}"]
            a = fig.add_subplot(gs[2 * r, c])
            b = fig.add_subplot(gs[2 * r + 1, c])
            draw(a, b, y, iid, slot != "crowd_shouting")
    fig.tight_layout()
    fig.savefig(path, dpi=75)
    plt.close(fig)


LABELS = {
    "crowd_shouting.v1": "Варіант 1 — спокійний гомін до матчу",
    "crowd_shouting.v2": "Варіант 2 — рівний гул арени",
    "crowd_shouting.v3": "Варіант 3 — жива арена з оплесками",
    "qte_prompt.v1": "Варіант 1 — сухий барабан і клац шкіри",
    "qte_prompt.v2": "Варіант 2 — ляпас ременя з підбоєм",
    "qte_prompt.v3": "Варіант 3 — наростаючий шум у клац",
    "qte_miss.v1": "Варіант 1 — глухий удар у живіт",
    "qte_miss.v2": "Варіант 2 — здутий спуск повітря",
    "qte_miss.v3": "Варіант 3 — мішок падає на килим",
}
DESIGNS = {
    "crowd_shouting.v1": "Той самий запис натовпу, але спектрально вирівняний (жодна частота не вища за 1,35 від медіани), низ до 170 Гц зрізано, каламуть 350 Гц приглушено, верх обрізано на 2,3 кГц; тихий рівний гомін без вигуків і ревіння, петля без шва.",
    "crowd_shouting.v2": "Повний середній діапазон без низького реву й каламуті, піки та виття притиснуті спектральним вирівнювачем і повільним вирівнюванням гучності; постійний живий гул арени, що не гуляє, безшовна петля.",
    "crowd_shouting.v3": "Основа v2, плюс шар очищених оплесків і шість далеких вигуків (змінений тон, у реверб) на фіксованих місцях та повільні хвилі ажіотажу; жвавіше, але без важкого низу й постійно.",
    "qte_prompt.v1": "Сухий удар великого барабана (кілька неточних мод мембрани, швидкий спад) з коротким клацом шкіри на атаці; жодних тонів чи біпів.",
    "qte_prompt.v2": "Подвійний тріск ременя по тілу (два шумові клацки з проміжком 4,5 мс) з м'якою шкіряною подушкою і низьким підбоєм 95-48 Гц; різкий, але без верхів.",
    "qte_prompt.v3": "Шум, що наростає за 0,17 с смугою 0,5-3,6 кГц, і різкий обрив у клац дошки (короткий шум і неточні резонанси) з коротким глухим підбоєм; сигнал-попередження без мелодійності.",
    "qte_miss.v1": "Приглушений удар: бас 150-42 Гц, низьколужений шум і короткий спадний подих, усе зрізано на 1,8 кГц; важко, без комічності.",
    "qte_miss.v2": "Нетональний шумовий свист повітря, що падає від 2,6 кГц до 200 Гц за 0,55 с, поверх приглушеного глухого стуку; відчуття здутого шансу, без вау-вау.",
    "qte_miss.v3": "Глухий шльоп важкого мішка по килиму з низьким підбоєм і чотирма затихаючими шурхотами тканини; сухо і похмуро.",
}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    out = Path(ap.parse_args().out)
    (out / "audio").mkdir(parents=True, exist_ok=True)
    (out / "spec").mkdir(parents=True, exist_ok=True)

    src = load_mono(CC0 / "crowd_shouting.ogg")
    applause = load_mono(CC0 / "applause_hall.wav")

    sigs = {}
    sigs["crowd_shouting.v1"] = crowd_v1(src)
    sigs["crowd_shouting.v2"] = crowd_v2(src)
    v3, picks = crowd_v3(src, applause)
    sigs["crowd_shouting.v3"] = v3
    sigs["qte_prompt.v1"] = _set_loudness(prompt_v1(), -15.0)
    sigs["qte_prompt.v2"] = _set_loudness(prompt_v2(), -15.0)
    p3, _ = prompt_v3()
    sigs["qte_prompt.v3"] = _set_loudness(p3, -15.0)
    sigs["qte_miss.v1"] = _set_loudness(miss_v1(), -20.0)
    sigs["qte_miss.v2"] = _set_loudness(miss_v2(), -20.0)
    sigs["qte_miss.v3"] = _set_loudness(miss_v3(), -20.0)

    items = {}
    rows = []
    for iid, y in sigs.items():
        slot = iid.split(".")[0]
        short = slot != "crowd_shouting"
        y = np.asarray(y, dtype=float)
        pcm = to_pcm16(y, dither=not short)[:]
        write_wav(out / "audio" / f"{iid}.wav", pcm)
        save_spec(y, out / "spec" / f"{iid}.png", iid, short)
        items[iid] = (iid, y)
        lu = L.integrated_loudness(y) if not short else L.clip_loudness(y)
        tp = L.true_peak_db(y)
        info = f"{iid}: {len(y) / SR:6.2f} с  {lu:6.1f} LUFS  {tp:5.2f} dBTP  тиша на початку {lead_silence_ms(y):.1f} мс"
        if not short:
            d = abs(y[0] - y[-1])
            info += f"  шов jump={d:.4f} (макс. сусідня різниця {np.abs(np.diff(y)).max():.4f})"
            tr = band_track(y, [(0, 150), (150, 500), (500, 2500), (2500, 8000)])
            info += "  розкид смуг (sigma dB): " + " ".join(f"{s:.1f}" for s in tr.std(axis=0))
        print(info)
        rows.append(iid)
    save_sheet(items, out / "spec" / "_crowd.png")
    # Контрольний звіт про вихідник
    tr = band_track(src, [(0, 150), (150, 500), (500, 2500), (2500, 8000)])
    print("вихідник: розкид смуг (sigma dB): " + " ".join(f"{s:.1f}" for s in tr.std(axis=0)))
    print("вигуки v3 з позицій (с):", [round(i / SR, 1) for i in picks])


if __name__ == "__main__":
    main()
