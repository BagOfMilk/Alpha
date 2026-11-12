"""Дзвони рингу «Рингу»: модальний синтез сталевого/латунного купола, удар арени, сталева конструкція.

Запуск (з теки tools):  python -m ring_audio.ring_bells --out <тека>
Усе детерміновано (фіксовані seed), лише numpy; вихід — моно WAV 48 кГц 16 біт.
"""
import argparse
import json
import os

import numpy as np

from .loudness import integrated_loudness, true_peak_db
from .mix import to_pcm16, write_wav
from .synth import SR, filt, fast_len

FAMILY = "дзвони рингу"


# ---------------------------------------------------------------- будівельні блоки
def modal(f0, ratios, amps, tau0, n, detune=(1.0, 2.5), tau_pow=0.55, seed=1, extra=0):
    """Сума експоненційно згасаючих мод; кожна мода — пара близьких частот (биття).

    tau0 — час спаду основної моди на -60 дБ (T60), с; вищі моди спадають швидше.
    extra — кількість додаткових слабких негармонійних мод (густота металу)."""
    rng = np.random.default_rng(seed)
    ratios, amps = list(ratios), list(amps)
    for _ in range(extra):
        ratios.append(rng.uniform(1.4, 11.0))
        amps.append(rng.uniform(0.08, 0.22))
    t = np.arange(n) / SR
    y = np.zeros(n)
    for r, a in zip(ratios, amps):
        f = f0 * r
        if f > 15000:
            continue
        tau = tau0 / 6.9 * r ** (-tau_pow)
        d = rng.uniform(*detune)
        ph = rng.uniform(0, 2 * np.pi, 2)
        env = np.exp(-t / tau)
        y += 0.5 * a * env * (np.sin(2 * np.pi * (f - d / 2) * t + ph[0]) + np.sin(2 * np.pi * (f + d / 2) * t + ph[1]))
    return y


def strike_click(n, seed, ms=4.0, hp=2500.0, tau_ms=1.1, level=1.0):
    """Різкий клац металевого молотка: короткий шум, відфільтрований вгору."""
    m = int(ms * SR / 1000)
    rng = np.random.default_rng(seed)
    z = rng.standard_normal(m + 400)
    z = filt(z, "hp", hp, 0.8, order=2)[:m]
    z = z * np.exp(-np.arange(m) / SR / (tau_ms / 1000))
    out = np.zeros(n)
    out[:m] = level * z / max(np.abs(z).max(), 1e-9)
    return out


def body_thump(n, f, tau, level):
    """Тиск удару на нижніх модах: коротка синусоїда зі спадом висоти."""
    t = np.arange(n) / SR
    fr = f * (1.0 + 0.35 * np.exp(-t / 0.02))
    ph = 2 * np.pi * np.cumsum(fr) / SR
    return level * np.sin(ph) * np.exp(-t / tau)


def room_ir(rt60, seed, early, brightness=6500.0):
    """Моно-імпульс великого залу: ранні відбиття + шумовий хвіст, що темнішає."""
    rng = np.random.default_rng(seed)
    n = int(rt60 * 1.1 * SR)
    t = np.arange(n) / SR
    nz = rng.standard_normal(n)
    bright = filt(nz, "lp", brightness)
    dark = filt(nz, "lp", 1500)
    w = np.clip(t / (rt60 * 0.8), 0, 1)
    ir = (bright * (1 - w) + dark * w) * 10 ** (-3.0 * t / rt60) * (1 - np.exp(-t / 0.012)) * 0.5
    for dt, g in early:
        i = int(dt * SR)
        if i < n:
            ir[i] += g
    return ir / np.sqrt((ir ** 2).sum())


def apply_room(dry, ir, wet):
    n = len(dry)
    N = fast_len(n + len(ir))
    w = np.fft.irfft(np.fft.rfft(dry, N) * np.fft.rfft(ir, N), N)[:n]
    rd = np.sqrt(np.mean(dry ** 2) + 1e-12)
    rw = np.sqrt(np.mean(w ** 2) + 1e-12)
    return dry + wet * w * (rd / rw) * 0.9


def finish(y, seconds, tail_fade_ms=120.0):
    n = int(seconds * SR)
    y = y[:n]
    if len(y) < n:
        y = np.concatenate([y, np.zeros(n - len(y))])
    k = int(tail_fade_ms * SR / 1000)
    y[-k:] *= 0.5 * (1 + np.cos(np.linspace(0, np.pi, k)))
    a = int(0.00015 * SR)
    y[:a] *= np.linspace(0, 1, a)
    return y


def normalise(y, tp=-1.5):
    y = y / max(np.abs(y).max(), 1e-9)
    y = np.tanh(2.2 * y) / np.tanh(2.2)  # м'яке притискання піка атаки: тіло звуку гучніше
    return y * 10 ** ((tp - true_peak_db(y)) / 20)


# ---------------------------------------------------------------- дзвони (початок і кінець матчу)
def dome_bell(f0, ratios, amps, tau0, seconds, seed, click=1.0, thump=0.3, thump_f=None,
              rt60=1.8, wet=0.35, detune=(0.6, 2.4), tau_pow=0.55, hp=2500.0, lp=None):
    n = int((seconds + 0.2) * SR)
    y = modal(f0, ratios, amps, tau0, n, detune, tau_pow, seed, extra=7)
    y = y / np.abs(y).max()
    y += strike_click(n, seed + 10, hp=hp, level=0.55 * click)
    y += body_thump(n, thump_f or f0 * 0.5, 0.045, thump)
    y = filt(y, "hp", 110, 0.7)
    if lp:
        y = filt(y, "lp", lp, 0.7)
    ir = room_ir(rt60, seed + 20, [(0.011, 0.5), (0.019, 0.35), (0.031, 0.3), (0.047, 0.22), (0.073, 0.15)])
    y = apply_room(y, ir, wet)
    return finish(y, seconds)


def bell_01_v1():
    # сталевий купол, чистий «ринговий» удар, помірний зал
    r = [1.0, 2.09, 3.02, 4.27, 5.41, 6.83, 8.2, 9.9]
    a = [1.0, 0.8, 0.65, 0.5, 0.42, 0.3, 0.2, 0.12]
    return dome_bell(640, r, a, 1.5, 2.2, 101, click=1.0, thump=0.35, rt60=1.8, wet=0.35)


def bell_01_v2():
    # важка латунь, нижча основна, густіші низькі моди, великий зал
    r = [1.0, 1.52, 2.17, 2.9, 3.97, 5.14, 6.6, 8.4]
    a = [1.0, 0.7, 0.75, 0.55, 0.45, 0.35, 0.22, 0.14]
    return dome_bell(480, r, a, 1.9, 2.4, 202, click=0.8, thump=0.55, thump_f=190, rt60=2.4, wet=0.45,
                     detune=(0.8, 3.0), tau_pow=0.5, lp=9000)


def bell_01_v3():
    # різкий удар молотка по тонкому куполу: сильний клац, багато верхніх мод, суха кімната
    r = [1.0, 2.14, 2.99, 4.1, 5.35, 6.7, 8.1, 9.4, 10.9]
    a = [0.9, 1.0, 0.8, 0.7, 0.6, 0.5, 0.4, 0.3, 0.2]
    return dome_bell(790, r, a, 1.2, 1.9, 303, click=1.7, thump=0.2, rt60=1.5, wet=0.22, hp=3500.0,
                     tau_pow=0.65)


def bell_02_v1():
    r = [1.0, 2.11, 3.04, 4.3, 5.5, 6.9, 8.3]
    a = [1.0, 0.85, 0.65, 0.5, 0.4, 0.28, 0.18]
    return dome_bell(700, r, a, 0.75, 1.15, 404, click=1.1, thump=0.3, rt60=1.1, wet=0.28)


def bell_02_v2():
    r = [1.0, 1.58, 2.24, 3.08, 4.1, 5.3, 6.7]
    a = [1.0, 0.75, 0.8, 0.55, 0.42, 0.3, 0.18]
    return dome_bell(540, r, a, 0.62, 1.05, 505, click=0.9, thump=0.5, thump_f=210, rt60=1.0, wet=0.25,
                     detune=(1.2, 3.2), tau_pow=0.7, lp=7500)


def bell_02_v3():
    r = [1.0, 2.07, 2.96, 4.18, 5.62, 7.0, 8.7, 10.4]
    a = [0.7, 1.0, 0.9, 0.85, 0.7, 0.55, 0.4, 0.25]
    return dome_bell(860, r, a, 0.85, 1.1, 606, click=1.5, thump=0.2, rt60=0.9, wet=0.2, hp=3200.0,
                     detune=(1.5, 4.5), tau_pow=0.8)


# ---------------------------------------------------------------- вихід борця
def gong_v1():
    seconds = 2.9
    n = int((seconds + 0.2) * SR)
    r = [1.0, 2.06, 2.43, 3.17, 4.26, 5.52, 7.1, 9.0]
    a = [1.0, 0.7, 0.8, 0.6, 0.5, 0.35, 0.22, 0.12]
    y = modal(340, r, a, 2.6, n, (0.4, 1.8), 0.45, 707)
    y = y / np.abs(y).max()
    y += strike_click(n, 717, hp=1800, tau_ms=1.8, level=0.6)
    y += body_thump(n, 140, 0.07, 0.6)
    ir = room_ir(2.8, 727, [(0.016, 0.5), (0.033, 0.4), (0.061, 0.35), (0.11, 0.3), (0.19, 0.22)], 5500.0)
    y = apply_room(y, ir, 0.7)
    return finish(y, seconds, 300.0)


def gong_v2():
    seconds = 2.4
    n = int((seconds + 0.2) * SR)
    t = np.arange(n) / SR
    f = 44.0 + 80.0 * np.exp(-t / 0.05)
    boom = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t / 0.28)
    boom = np.tanh(2.2 * boom) * 0.9
    nz = np.random.default_rng(808).standard_normal(n)
    burst = np.zeros(n)
    for fc, tau in ((6000, 0.03), (2200, 0.07), (700, 0.14)):
        burst += filt(nz, "lp", fc, 0.7) * np.exp(-t / tau) * (0.5 if fc > 3000 else 0.8)
    burst = filt(burst, "hp", 90, 0.7)
    clack = modal(1150, [1.0, 1.77, 2.63, 3.4, 4.9, 6.1], [1, .8, .7, .6, .45, .3], 0.4, n, (5, 25), 0.3, 818)
    y = boom + 0.55 * burst / np.abs(burst).max() + 0.4 * clack / np.abs(clack).max()
    y += strike_click(n, 828, ms=3, hp=3000, level=0.7)
    ir = room_ir(2.2, 838, [(0.02, 0.5), (0.045, 0.4), (0.09, 0.3), (0.16, 0.25)], 2600.0)
    y = apply_room(y, ir, 0.6)
    y = filt(y, "hp", 38, 0.7)
    return finish(y, seconds, 250.0)


def gong_v3():
    seconds = 2.7
    n = int((seconds + 0.1) * SR)
    r = [1.0, 2.756, 5.404, 8.933, 13.34]
    a = [1.0, 0.85, 0.6, 0.4, 0.2]
    y = modal(215, r, a, 0.95, n, (0.8, 2.2), 0.5, 909)
    y += 0.4 * modal(1650, [1.0, 1.43, 2.1, 2.9], [1, .8, .6, .4], 0.5, n, (3, 12), 0.5, 919)
    y = y / np.abs(y).max()
    y += strike_click(n, 929, ms=5, hp=1500, tau_ms=1.6, level=0.7)
    y += body_thump(n, 90, 0.05, 0.55)
    out = y.copy()
    for dly, g in ((0.23, 0.38), (0.47, 0.26), (0.79, 0.16)):
        i = int(dly * SR)
        out[i:] += g * filt(y[:n - i], "lp", 2800 * (1 - 0.3 * dly), 0.7)
    ir = room_ir(2.2, 939, [(0.014, 0.4), (0.03, 0.3), (0.058, 0.25)], 5000.0)
    y = apply_room(out, ir, 0.5)
    return finish(y, seconds, 250.0)


SLOTS = [
    ("bell_01", "v1", bell_01_v1, "Варіант 1 — сталевий купол, чистий удар"),
    ("bell_01", "v2", bell_01_v2, "Варіант 2 — важка латунь, зал"),
    ("bell_01", "v3", bell_01_v3, "Варіант 3 — різкий клац молотка"),
    ("bell_02", "v1", bell_02_v1, "Варіант 1 — короткий сталевий «дзинь»"),
    ("bell_02", "v2", bell_02_v2, "Варіант 2 — притлумлена латунь"),
    ("bell_02", "v3", bell_02_v3, "Варіант 3 — блискучий клангер"),
    ("gong_01", "v1", gong_v1, "Варіант 1 — великий дзвін, відлуння арени"),
    ("gong_01", "v2", gong_v2, "Варіант 2 — удар арени, бум і клац"),
    ("gong_01", "v3", gong_v3, "Варіант 3 — удар по сталевій фермі"),
]


# ---------------------------------------------------------------- спектрограми
def _db(y):
    nfft, hop = 2048, 256
    win = np.hanning(nfft)
    fr = 1 + (len(y) - nfft) // hop
    idx = np.arange(nfft)[None, :] + hop * np.arange(fr)[:, None]
    S = np.abs(np.fft.rfft(y[idx] * win, axis=1)) / (nfft / 4)
    fq = np.fft.rfftfreq(nfft, 1 / SR)
    sel = (fq >= 30) & (fq <= 16000)
    tt = (np.arange(fr) * hop + nfft / 2) / SR
    return tt, fq[sel], 20 * np.log10(S[:, sel] + 1e-7)


def spectrogram_png(y, ident, path):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    tt, fq, db = _db(y)
    fig, (a1, a2) = plt.subplots(2, 1, figsize=(9, 4.6), gridspec_kw={"height_ratios": [1, 3]}, sharex=True)
    a1.plot(np.arange(len(y)) / SR, y, lw=0.4, color="k")
    a1.set_ylim(-1, 1)
    a1.set_title(ident, fontsize=10)
    a2.pcolormesh(tt, fq, db.T, vmin=-100, vmax=-10, cmap="magma", shading="auto")
    a2.set_yscale("log")
    a2.set_ylim(30, 16000)
    a2.set_ylabel("Hz")
    a2.set_xlabel("s")
    fig.tight_layout()
    fig.savefig(path, dpi=80)
    plt.close(fig)


def contact_sheet(items, path):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    fig, axs = plt.subplots(3, 3, figsize=(18, 11))
    for ax, (ident, y) in zip(axs.flat, items):
        tt, fq, db = _db(y)
        ax.pcolormesh(tt, fq, db.T, vmin=-100, vmax=-10, cmap="magma", shading="auto")
        ax.set_yscale("log")
        ax.set_ylim(30, 16000)
        ax.set_title(ident)
    fig.tight_layout()
    fig.savefig(path, dpi=60)
    plt.close(fig)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    args = ap.parse_args()
    ad = os.path.join(args.out, "audio")
    sd = os.path.join(args.out, "spec")
    os.makedirs(ad, exist_ok=True)
    os.makedirs(sd, exist_ok=True)
    meta, items = [], []
    for slot, v, fn, label in SLOTS:
        ident = f"{slot}.{v}"
        y = normalise(fn())
        wav = os.path.join(ad, ident + ".wav")
        write_wav(wav, to_pcm16(y, dither=False))
        png = os.path.join(sd, ident + ".png")
        spectrogram_png(y, ident, png)
        lead = int(np.argmax(np.abs(y) > 0.01)) / SR * 1000
        info = dict(id=ident, slot=slot, label=label, seconds=round(len(y) / SR, 3),
                    lufs=round(integrated_loudness(y), 2), peak_dbtp=round(true_peak_db(y), 2),
                    channels=1, file=os.path.abspath(wav), spectrogram=os.path.abspath(png),
                    lead_ms=round(lead, 2), end_abs=float(np.abs(y[-1])))
        meta.append(info)
        items.append((ident, y))
        print(json.dumps(info, ensure_ascii=False))
    contact_sheet(items, os.path.join(sd, "_bells.png"))
    with open(os.path.join(args.out, "bells_meta.json"), "w", encoding="utf-8") as f:
        json.dump(meta, f, ensure_ascii=False, indent=1)


if __name__ == "__main__":
    main()
