"""Зал здіймається — слот crowd_surge (3 варіанти), реакція залу на склейку за рингом.

Матеріал: лише CC0 з репозиторію (crowd_shouting.ogg, applause_hall.wav), обробка власна.
Мономікс 48 кГц / 16 біт. Детерміновано (фіксовані seed, без випадковості часу).

Запуск з теки tools:
    python -m ring_audio.ring_crowd_surge --out <тека>
OGG читає службовий пакет soundfile; якщо він у окремій теці — шлях у RING_AUDIO_PYLIB.
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
from .mix import make_ir, to_pcm16, write_wav
from .ring_crowd import (CC0, band_track, conv_tail, draw, load_mono, lead_silence_ms,
                         leveler, mud_cut, spectral_clamp)
from .synth import SR, fade, filt, noise, sweep_filter

SLOT = "crowd_surge"
TARGET_LUFS = -20.0


# ---------------------------------------------------------------- вибір шматків гулу
def clean_source(src):
    """Прибрати низький рев і каламуть: вирівнювач, hp 150, виріз 200-500 Гц."""
    y = spectral_clamp(src, 1.5, 1.1)
    y = filt(y, "hp", 150, 0.7071, order=2)
    y = mud_cut(y, 0.78, fc=340.0, qf=0.55)
    return y


def pick_windows(raw, dur, count, min_gap):
    """Вікна dur с з найменшою часткою низу в СИРОМУ записі (рев) і найменшим спектральним
    потоком у середині; вікна не ближче min_gap с одне до одного."""
    n = int(dur * SR)
    hop = int(0.25 * SR)
    fr = np.fft.rfftfreq(2048, 1 / SR)
    lo = fr < 180
    mid = (fr > 500) & (fr < 2500)
    w = np.hanning(2048)
    scored = []
    for i in range(int(2 * SR), len(raw) - n - int(2 * SR), hop):
        seg = raw[i:i + n]
        idx = np.arange(0, n - 2048, 1024)
        S = np.abs(np.fft.rfft(np.stack([seg[j:j + 2048] * w for j in idx]), axis=1)) ** 2
        low_share = S[:, lo].sum() / S.sum()
        m = np.log(S[:, mid].sum(axis=1) + 1e-12)
        flux = np.abs(np.diff(m)).mean()
        peaky = np.std(m)
        scored.append((low_share * 4.0 + flux + 0.5 * peaky, i))
    scored.sort()
    picks = []
    for _, i in scored:
        if all(abs(i - j) > min_gap * SR for j in picks):
            picks.append(i)
        if len(picks) == count:
            break
    return picks


def layers(clean, starts, n, gains, shifts_ms):
    """2-4 зсунуті копії з різних місць запису: густота без фазового гребеня."""
    out = np.zeros(n)
    for s, g, d in zip(starts, gains, shifts_ms):
        o = int(d * SR / 1000)
        seg = clean[s + o:s + o + n]
        out[:len(seg)] += g * seg
    return leveler(out, win_s=0.35, power=0.9, lim=(0.4, 2.5))  # огинаюча — наша, не записова


# ---------------------------------------------------------------- огинаюча і колір
def env_curve(n, att, hold, dec, floor, att_pow=1.6, dec_pow=1.3):
    """floor -> пік за att, тримається hold, спад за dec до нуля."""
    t = np.arange(n) / SR
    u = np.clip(t / att, 0, 1)
    rise = floor + (1 - floor) * u ** att_pow
    v = np.clip((t - att - hold) / dec, 0, 1)
    return rise * (1 - v) ** dec_pow


def formant_lift(y, env, g1=0.5, g2=0.35):
    """Відкриті голосні на піку: смуги 500-1800 Гц додаються пропорційно огинаючій."""
    b1 = filt(y, "bp", 520, 1.3, order=1)
    b2 = filt(y, "bp", 1000, 1.5, order=1)
    b3 = filt(y, "bp", 1800, 1.2, order=1)
    return y + env ** 2 * (g1 * b1 + g2 * b2 + 0.2 * b3)


def vary_color(y, env, f_lo, f_hi):
    """Яскравість рухається разом із піком: зріз lp біжить від f_lo до f_hi."""
    fc = f_lo + (f_hi - f_lo) * np.clip(env, 0, 1)
    return sweep_filter(y, fc, "lp", qf=0.7071, K=7)


def scale_to(y, lufs, tp_max=-1.2):
    y = y * 10 ** ((lufs - L.clip_loudness(y)) / 20.0)
    for _ in range(60):
        tp = L.true_peak_db(y)
        if tp <= tp_max:
            break
        y = y * 10 ** ((tp_max - tp - 0.05) / 20.0)
    return y


def finalize(y, lufs, tail_ms=12, head_ms=2):
    y = filt(y, "hp", 140, 0.7071, order=1)  # страховка: низ не повертається
    pk = np.abs(y).max()
    y = np.tanh(y / (0.55 * pk)) * 0.55 * pk  # м'яко пригладити поодинокі піки: щільний натовп, не удар
    y = filt(y, "hp", 150, 0.7071, order=2)  # знизу прибрати продукти спотворення
    y = fade(y, head_ms, tail_ms)
    return scale_to(y, lufs)


# ---------------------------------------------------------------- три ідеї
def surge_a(clean, picks):
    """(а) Зал коротко ахає й стихає: швидкий підйом, короткий пік, спад ~1,1 с. 1,5 с."""
    n = int(1.5 * SR)
    base = layers(clean, picks[0:3], n, [1.0, 0.7, 0.5], [0, 170, 410])
    env = env_curve(n, att=0.28, hold=0.05, dec=1.1, floor=0.14, att_pow=1.4, dec_pow=1.6)
    y = formant_lift(base, env, 0.6, 0.35)
    y = vary_color(y, env, 1900, 4600) * env
    # короткий «вдих»: тихий нетональний шум, що піднімається за частотою, під самим початком
    t = np.arange(n) / SR
    u = np.clip(t / 0.32, 0, 1)
    br = sweep_filter(noise(n, 4101), 700 * (3200 / 700) ** u, "bp", qf=0.9, K=7)
    br *= u ** 1.5 * np.exp(-np.clip(t - 0.32, 0, None) / 0.12)
    br = br / (np.abs(br).max() + 1e-9) * np.sqrt(np.mean(y ** 2)) * 0.9
    return finalize(y + br, TARGET_LUFS, tail_ms=40)


def surge_b(clean, picks, applause):
    """(б) Рев здіймається різко, далекі вигуки, тримається ~1 с і спадає 0,75 с. 2,1 с.
    Пік — у перші 0,3 с (гра ставить звук через 0,35 с після контакту: зал має встати на склейці, а не після неї)."""
    n = int(2.1 * SR)
    base = layers(clean, picks[3:7], n, [1.0, 0.8, 0.6, 0.45], [0, 90, 260, 530])
    env = env_curve(n, att=0.25, hold=0.85, dec=0.75, floor=0.12, att_pow=1.0, dec_pow=1.6)
    t = np.arange(n) / SR
    wob = 1 + 0.07 * np.sin(2 * np.pi * 1.7 * t + 0.4) + 0.05 * np.sin(2 * np.pi * 3.1 * t + 2.0)
    env = env * wob
    y = formant_lift(base, np.clip(env, 0, 1), 0.45, 0.4)
    y = vary_color(y, env, 2400, 5200) * env
    ir = make_ir(1.0, seed=23, predelay=0.025)[:, 0]
    seg = int(0.55 * SR)
    shout = np.zeros(n + len(ir))
    rms = np.sqrt(np.mean(y ** 2))
    for k, (at, rate, g) in enumerate(((0.36, 1.12, 0.9), (0.74, 1.2, 0.8), (1.12, 1.08, 0.55))):
        s0 = picks[7 + k]
        s = clean[s0:s0 + int(seg * rate) + 8]
        s = np.interp(np.arange(0, seg) * rate, np.arange(len(s)), s)
        s = filt(s, "hp", 650, 0.7071, order=2)
        s = filt(s, "lp", 3200, 0.7071, order=2)
        s = fade(s * np.hanning(len(s)) ** 0.6, 40, 140)
        wet = conv_tail(s, ir)
        wet *= g * rms * 0.9 / (np.sqrt(np.mean(wet ** 2)) + 1e-9)
        i = int(at * SR)
        shout[i:i + len(wet)] += wet[:len(shout) - i]
    y = y + shout[:n]
    ap = applause[int(6.0 * SR):int(6.0 * SR) + n]
    ap = spectral_clamp(ap, 1.4, 1.1)
    ap = filt(ap, "hp", 600, 0.7071, order=2)
    ap = filt(ap, "lp", 5000, 0.7071, order=1)
    ap = ap * env * (rms / (np.sqrt(np.mean(ap ** 2)) + 1e-9)) * 10 ** (-17 / 20)
    # загальний спад: ревербераційні хвости вигуків не обриваються на межі файлу
    t = np.arange(n) / SR
    rel = np.cos(0.5 * np.pi * np.clip((t - 1.3) / 0.75, 0, 1)) ** 2
    return finalize((y + ap) * rel, TARGET_LUFS, tail_ms=110)


def surge_c(clean, picks):
    """(в) Глухий напружений гомін: наростає повільніше за інші (пік на 0,8 с), тримається й стихає за 0,6 с. 1,6 с.
    Початок з −10 дБ (чутно одразу після склейки); смуга вужча й темніша (≈380 Гц–2,6 кГц) — інший колір, ніж v1/v2."""
    n = int(1.6 * SR)
    base = layers(clean, picks[10:13], n, [1.0, 0.75, 0.55], [0, 230, 120])
    t = np.arange(n) / SR
    env = env_curve(n, att=0.8, hold=0.18, dec=0.6, floor=0.32, att_pow=1.6, dec_pow=1.5)
    late = layers(clean, picks[13:14], n, [0.8], [60])
    late *= np.clip((t - 0.3) / 0.5, 0, 1) ** 1.2
    y = formant_lift(base + late, env, 0.35, 0.55)
    y = vary_color(y, env, 1100, 2600) * env
    y = filt(y, "hp", 380, 0.7071, order=2)
    return finalize(y, TARGET_LUFS)


LABELS = {
    f"{SLOT}.v1": "Варіант 1 — зал ахає й стихає",
    f"{SLOT}.v2": "Варіант 2 — рев здіймається, тримається й спадає",
    f"{SLOT}.v3": "Варіант 3 — глухий гомін наростає й стихає",
}
DESIGNS = {
    f"{SLOT}.v1": "Три зсунуті копії очищеного гулу (без низу й каламуті) зі швидким підйомом 0,28 с, коротким піком і спадом ~1,1 с; на піку відкриваються голосні (смуги 500-1800 Гц) і росте яскравість, на початку тихий нетональний «вдих». Один короткий ах і видих залу.",
    f"{SLOT}.v2": "Чотири шари гулу з різних місць запису: різкий підйом 0,25 с, утримання ~0,85 с з неперіодичним диханням, три далекі вигуки (шматки гулу вищим тоном, у реверб залу) і лише тінь далеких оплесків; плавний спад 0,75 с. Найдовший і найгустіший.",
    f"{SLOT}.v3": "Глухий напружений гомін у вужчій і темнішій смузі (≈380 Гц–2,6 кГц): наростає повільніше за інші — від −10 дБ до піку за 0,8 с, густішає через четвертий шар, тримається й стихає за 0,6 с. Найстриманіший і найнапруженіший.",
}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    out = Path(ap.parse_args().out)
    (out / "audio").mkdir(parents=True, exist_ok=True)
    (out / "spec").mkdir(parents=True, exist_ok=True)

    raw = load_mono(CC0 / "crowd_shouting.ogg")
    applause = load_mono(CC0 / "applause_hall.wav")
    clean = clean_source(raw)
    picks = pick_windows(raw, 2.6, 14, 0.9)
    if len(picks) < 14:
        picks = (picks * 3)[:14]
    print("вікна гулу (с):", [round(i / SR, 1) for i in picks])

    sigs = {
        f"{SLOT}.v1": surge_a(clean, picks),
        f"{SLOT}.v2": surge_b(clean, picks, applause),
        f"{SLOT}.v3": surge_c(clean, picks),
    }
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt

    for iid, y in sigs.items():
        write_wav(out / "audio" / f"{iid}.wav", to_pcm16(y, dither=False))
        fig, (a, b) = plt.subplots(2, 1, figsize=(9, 5), gridspec_kw={"height_ratios": [1, 3]})
        draw(a, b, y, iid, True)
        fig.tight_layout()
        fig.savefig(out / "spec" / f"{iid}.png", dpi=110)
        plt.close(fig)
        tr = band_track(y, [(0, 150), (150, 500), (500, 2500), (2500, 8000)], step=0.2)
        print(f"{iid}: {len(y) / SR:.2f} с  clip {L.clip_loudness(y):.1f} LU  int {L.integrated_loudness(y):.1f} LUFS  "
              f"{L.true_peak_db(y):.2f} dBTP  тиша {lead_silence_ms(y):.1f} мс  кінець {abs(y[-1]):.5f}  "
              f"макс дБ по смугах: " + " ".join(f"{v:.0f}" for v in tr.max(axis=0)))

    fig = plt.figure(figsize=(17, 6))
    gs = fig.add_gridspec(2, 3, height_ratios=[1, 3])
    for c, (iid, y) in enumerate(sigs.items()):
        a = fig.add_subplot(gs[0, c])
        b = fig.add_subplot(gs[1, c])
        draw(a, b, y, iid, True)
    fig.tight_layout()
    fig.savefig(out / "spec" / "_crowd_surge.png", dpi=75)
    plt.close(fig)


if __name__ == "__main__":
    main()
