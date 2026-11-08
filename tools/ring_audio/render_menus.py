"""Рендер музики й SFX меню «Рингу» (Вхід, Завантаження, Налаштування) — брутальний напрям.

Запуск з каталогу tools:
    python -m ring_audio.render_menus [--out DIR] [--stems DIR] [--only NAME|sfx]

Аранжування (послідовності нот, патерни барабанів) — тут; інструменти — synth.py,
дані лейтмотиву — motif.py, мікс — mix.py, гучність — loudness.py.
Напрям (власник, 07–08.11.2026): брутально, без радості; мі мінор / фрігійський,
силові акорди, важкі барабани, перевантажені гітари й бас, низькі духові, метал.
"""
import argparse
import sys
import time
from pathlib import Path

import numpy as np

from . import loudness as L
from . import mix as X
from . import motif as M
from . import synth as S
from .synth import SR

DEFAULT_OUT = Path(__file__).resolve().parents[2] / "Assets" / "Art" / "Ring" / "Audio" / "Menu"


def vq(v):
    return round(min(max(v, 0.1), 1.0) * 10) / 10


# ---------------------------------------------------------------- прості обгортки над сеткою
def gtr(sg, bar, step, notes, dur_steps, vel, mute=0.09, cab=4300.0, g=0.5):
    """Двохдоріжкова гітара: два зерна, ліве/праве."""
    d = sg.dur(dur_steps)
    for seed, pan in ((1, -0.85), (2, 0.85)):
        sg.put("gtr", S.chug(tuple(notes), d, vq(vel), seed, mute, cab), bar, step, gain=g, pan=pan)


def kick_(sg, bar, step, v=1.0, g=0.95):
    sg.put("drums", S.kick(vq(v)), bar, step, gain=g)


def snare_(sg, bar, step, v=1.0, g=0.85):
    sg.put("drums", S.snare(vq(v)), bar, step, gain=g, pan=0.05)


def tom_(sg, bar, step, midi, v=1.0, g=0.8):
    pan = {35: -0.25, 40: 0.0, 47: 0.3}.get(midi, 0.0)
    sg.put("drums", S.tom(midi, vq(v)), bar, step, gain=g, pan=pan)


def hat_(sg, bar, step, v=0.5, g=0.5, open_=False):
    sg.put("drums", S.hat_dull(vq(v), open_), bar, step, gain=g, pan=0.3)


def metal_(sg, bar, step, f, dur, v=0.8, seed=1, tau=0.8, g=0.6, pan=0.0):
    sg.put("fx", S.metal(float(f), dur, vq(v), seed, tau), bar, step, gain=g, pan=pan)


def segments(bar):
    """Відрізки такту з однаковим акордом: [(початок, кінець, акорд)]."""
    out = []
    for s in range(16):
        c = M.chord_at(bar, s)
        if out and out[-1][2] == c:
            out[-1][1] = s + 1
        else:
            out.append([s, s + 1, c])
    return [tuple(x) for x in out]


def accents(bar):
    return [[0, 3, 6, 10], [0, 3, 6, 8, 12], [0, 3, 6, 8, 11], [0, 3, 6, 10, 14]][bar % 4]


def riff(sg, bar, vs=1.0, ghost=True, cab=4300.0, g=0.5, acc_only=False):
    """Тихі долонею приглушені 16-ті на корені + акценти, що йдуть за гармонією."""
    acc = accents(bar)
    for s in range(16):
        ch = M.chord_at(bar, s)
        if s in acc:
            stab = ch == "Bb" and s >= 14
            gtr(sg, bar, s, M.power(ch), 2, 0.95 * vs, mute=0.45 if stab else 0.13, cab=cab, g=g)
        elif ghost and not acc_only and not (bar % 4 == 3 and s == 15):
            gtr(sg, bar, s, M.power(ch, wide=False), 1, 0.42 * vs, mute=0.07, cab=cab, g=g)


def play_bass(sg, bar, vel=0.9, dirt=1.0, g=0.9, acc_only=None):
    acc = acc_only if acc_only is not None else accents(bar)
    for i, s in enumerate(acc):
        nxt = acc[i + 1] if i + 1 < len(acc) else 16
        ch = M.chord_at(bar, s)
        sg.put("bass", S.bass_dist(M.ROOTS[ch], sg.dur(nxt - s), vq(vel), dirt), bar, s, gain=g)


def sustain(sg, bar, vel=0.5, g=0.35):
    for a, b, ch in segments(bar):
        gtr(sg, bar, a, M.power(ch), b - a, vel, mute=None, g=g)


def play_motif(sg, first_bar, lead_shifts=(-12,), lead_vel=(0.85,), horn_shifts=(-12,), horn_vel=(0.7,),
               stabs=True, lead_g=0.55, horn_g=0.5):
    for b, step, midi, ln in M.motif_notes():
        d = sg.dur(ln * 0.92)
        bar = first_bar + b
        for sh, v in zip(lead_shifts, lead_vel):
            for seed, pan in ((1, -0.12), (2, 0.12)):
                sg.put("lead", S.lead(midi + sh, d, vq(v), seed), bar, step, gain=lead_g * 0.7, pan=pan)
        for sh, v in zip(horn_shifts, horn_vel):
            if midi + sh >= 33:
                sg.put("brass", S.horn((midi + sh,), d, vq(v), 0.04, 1), bar, step, gain=horn_g)
    if stabs:
        s0, s1 = M.MOTIF_STABS[4]
        for st, ln in ((s0, 4), (s1, 2)):
            sg.put("brass", S.horn(M.power("E"), sg.dur(ln * 0.9), 0.9, 0.03, 2), first_bar + 4, st, gain=horn_g * 1.3)
            gtr(sg, first_bar + 4, st, M.power("E"), ln, 0.95, mute=0.3, g=0.45)


def fill(sg, bar, start, vol=1.0):
    seq = [47, 40, 40, 35, 35, 35, 35, 35]
    for i, s in enumerate(range(start, 16)):
        if s >= 14:
            snare_(sg, bar, s, 0.8 + 0.1 * (s - 14), 0.9)
        elif (s - start) % 2 == 0:
            tom_(sg, bar, s, seq[((s - start) // 2) % len(seq)], (0.7 + 0.04 * i) * vol)
        else:
            tom_(sg, bar, s, 40, 0.55 * vol)


def periodic_tone(n, f, harm=((1, 1.0),), lfo_cycles=2, depth=0.2, detune_cycles=0, phase=0.0):
    """Строго періодичний за довжиною петлі тон (цілі цикли) — безшовний дрон."""
    T = n / SR
    t = np.arange(n) / SR
    f1 = round(f * T) / T
    y = np.zeros(n)
    for k, a in harm:
        y += a * np.sin(2 * np.pi * f1 * k * t + phase)
        if detune_cycles:
            y += 0.7 * a * np.sin(2 * np.pi * (f1 + detune_cycles / T) * k * t + phase + 1.3)
    y *= 1.0 + depth * np.sin(2 * np.pi * lfo_cycles * t / T)
    return y


# ======================================================================= ВХІД
def login_section(bar):
    return "A" if bar < 8 else "B" if bar < 16 else "C" if bar < 24 else "D"


def login_drums(sg, bar):
    sec = login_section(bar)
    odd = bar % 2
    fill_from = {7: 12, 15: 8, 23: 8, 31: 12}.get(bar, 16)
    if sec == "C":
        # зривка: лише підлогові томи, метал, суб-дропи
        if bar < 23:
            for s, m, v in ((0, 40, 0.6), (6, 47, 0.4), (10, 35, 0.5)):
                tom_(sg, bar, s, m, v)
            if odd:
                tom_(sg, bar, 14, 40, 0.45)
        else:
            fill(sg, bar, 8, 1.0)
        return
    if sec == "D":
        kicks = [[0, 3, 6, 10], [0, 3, 6, 10, 13, 14]][odd]
    else:
        kicks = [[0, 3, 10], [0, 6, 10, 13]][odd]
    for s in kicks:
        if s < fill_from:
            kick_(sg, bar, s, 1.0 if s == 0 else 0.9)
    if 8 < fill_from:
        snare_(sg, bar, 8, 1.0)
    if sec == "D" and odd and 12 < fill_from:
        snare_(sg, bar, 12, 0.5, 0.7)
    hv = {"A": 0.45, "B": 0.55, "D": 0.7}[sec]
    for s in range(2, min(fill_from, 16), 4 if sec == "A" else 2):
        hat_(sg, bar, s, hv, 0.6, open_=(sec == "D" and s == 14 and odd))
    if fill_from < 16:
        fill(sg, bar, fill_from)
    # крештоп лише в гучних місцях
    if bar == 24:
        sg.put("drums", S.crash(1.0), bar, 0, gain=0.55, pan=-0.2)
    if bar in (0, 8):
        sg.put("drums", S.crash(0.7), bar, 0, gain=0.4, pan=-0.2)


def login_build():
    sg = X.Song(96, 32, seed=11)
    for bar in range(32):
        sec = login_section(bar)
        login_drums(sg, bar)
        if sec == "A":
            riff(sg, bar, 1.0, g=0.6)
            play_bass(sg, bar, 0.95)
        elif sec == "B":
            riff(sg, bar, 0.85)
            sustain(sg, bar, 0.5, 0.3)
            play_bass(sg, bar, 0.9)
        elif sec == "C":
            if bar % 2 == 0 or bar == 23:
                riff(sg, bar, 0.7, ghost=False, cab=650.0, g=0.4, acc_only=True)
            play_bass(sg, bar, 0.4, dirt=0.3, g=0.7, acc_only=[0])
        else:
            riff(sg, bar, 1.0, g=0.55)
            sustain(sg, bar, 0.6, 0.35)
            play_bass(sg, bar, 1.0)
    # мелодія
    play_motif(sg, 8, lead_shifts=(-12, 0), lead_vel=(0.85, 0.5), lead_g=0.55)  # B: тихий дубль на октаву вище для розбірливості
    for b, step, midi, ln in M.motif_notes():  # C: розстроєний низький дзвін
        sg.put("keys", S.low_bell(midi - 12, sg.dur(ln * 1.6), 0.85), 16 + b, step, gain=0.5, pan=0.0)
    for b, step, midi, ln in M.motif_notes():  # C: дзвін на октаву вище, ледь чутно (для розбірливості)
        sg.put("keys", S.low_bell(midi, sg.dur(ln * 1.2), 0.45, 0.7), 16 + b, step, gain=0.24, pan=0.25)
    play_motif(sg, 24, lead_shifts=(-12, 0), lead_vel=(0.9, 0.7), horn_shifts=(-12, -24), horn_vel=(0.8, 0.5),
               lead_g=0.6, horn_g=0.55)  # D: октави
    # C: низькі духові під дзвоном
    sg.put("brass", S.horn(M.power("E"), sg.dur(14 * 16 * 0.0 + 4 * 16 * 0.97), 0.5, 0.9, 3), 16, 0, gain=0.28)
    sg.put("brass", S.horn(M.power("E"), sg.dur(3 * 16 * 0.97), 0.5, 0.9, 4), 20, 0, gain=0.28)
    sg.put("brass", S.horn(M.power("F"), sg.dur(16 * 0.95), 0.55, 0.6, 5), 23, 0, gain=0.28)
    # C: суб-дропи і метал
    for bar, v in ((16, 1.0), (20, 0.9)):
        sg.put("fx", S.sub_drop(2.4, 78.0, 27.0, v), bar, 0, gain=0.6)
    for bar, step, f, v, seed in ((16, 0, 160, 0.85, 1), (17, 10, 233, 0.5, 2), (18, 0, 160, 0.8, 3), (19, 10, 233, 0.5, 4),
                                  (20, 0, 160, 0.8, 5), (21, 10, 233, 0.5, 6), (22, 0, 160, 0.8, 7), (22, 8, 233, 0.7, 8)):
        metal_(sg, bar, step, f, 2.2, v, seed, 0.9, 0.4, pan=-0.3 if seed % 2 else 0.3)
    # зворотні наростання перед кожним входом
    for bar, st, ln in ((7, 8, 8), (15, 8, 8), (23, 0, 16), (31, 8, 8)):
        sg.put("fx", S.rev_swell(sg.dur(ln), 1.0), bar, st, gain=0.5)
    cfg = dict(
        key="Em", rt60=1.5,
        gains=dict(drums=0.5, bass=0.45, gtr=1.0, lead=1.2, brass=1.2, keys=0.75, pad=0.0, fx=0.5),
        sends=dict(drums=0.05, gtr=0.05, lead=0.12, brass=0.18, keys=0.35, fx=0.3),
        bus=dict(drums=[("hp", 28, 0.7)]),
        comp=(-9.0, 1.8, 20.0, 180.0), sat=1.0,
        sections=[(i * 8, (i + 1) * 8, "ABCD"[i]) for i in range(4)],
    )
    return sg, cfg


# ======================================================================= ЗАВАНТАЖЕННЯ
def loading_build():
    sg = X.Song(96, 16, seed=21)
    n = sg.n
    for bar in range(16):
        for s in (0, 2, 8, 10):  # серцебиття: «буп-буп» двічі на такт
            kick_(sg, bar, s, 1.0 if s % 8 == 0 else 0.65, g=0.8 if s % 8 == 0 else 0.6)
        for s in range(0, 16, 2):  # тікання хета
            hat_(sg, bar, s, 0.8 if s % 4 == 0 else 0.55, 0.55)
        cab = 800.0 if bar < 8 else 1100.0
        acc = accents(bar)
        for s in range(16):
            ch = M.chord_at(bar, s)
            if s in acc and bar % 2 == 0:
                gtr(sg, bar, s, M.power(ch), 2, 0.7, mute=0.13, cab=cab, g=0.35)
            elif bar >= 8 and s % 2 == 0 and s not in acc:
                gtr(sg, bar, s, M.power(ch, wide=False), 1, 0.35, mute=0.07, cab=cab, g=0.3)
        if bar % 2 == 0:
            sg.put("bass", S.bass_dist(M.ROOTS[M.chord_at(bar, 0)], sg.dur(14), 0.7, 0.35), bar, 0, gain=0.7)
    # суб-дрон: строго періодичний
    d = periodic_tone(n, 41.2, harm=((1, 1.0), (2, 0.35)), lfo_cycles=3, depth=0.25, detune_cycles=3)
    d = d / np.max(np.abs(d)) * 0.5
    sg.bufs["pad"][:, 0] += d
    sg.bufs["pad"][:, 1] += d
    # фрагменти лейтмотиву: половинки тактів 1–4 (такти 1–2 та 3–4 по черзі), темний низький синт
    for first, halves in ((0, (0, 1)), (6, (2, 3)), (8, (0, 1)), (14, (2, 3))):
        for k, hb in enumerate(halves):
            for step, midi, ln in M.MOTIF[hb]:
                sg.put("lead", S.horn((midi - 12,), sg.dur(ln * 0.95), 0.6, 0.06, 7, 0.6), first + k, step, gain=0.5)
    # рідкісний металевий «тик» вдалині і наростання перед швом
    for bar in (3, 7, 11):
        metal_(sg, bar, 12, 233, 1.2, 0.4, bar, 0.5, 0.5, pan=0.4)
    sg.put("fx", S.rev_swell(sg.dur(12), 0.7, 95), 15, 4, gain=0.35)
    cfg = dict(
        key="Em", rt60=1.8,
        gains=dict(drums=0.7, bass=0.7, gtr=1.7, lead=1.4, brass=0.8, keys=0.9, pad=0.3, fx=1.0),
        sends=dict(drums=0.04, gtr=0.1, lead=0.3, brass=0.2, keys=0.3, fx=0.4),
        bus=dict(),
        comp=(-18.0, 2.4, 10.0, 140.0), sat=1.0,
        sections=[(0, 8, "1-8"), (8, 16, "9-16")],
    )
    return sg, cfg


# ======================================================================= НАЛАШТУВАННЯ
def settings_build():
    sg = X.Song(70, 16, seed=31)
    n = sg.n
    # низький дрон мі з повільним рухом (біття + дихання)
    d = periodic_tone(n, 41.2, harm=((1, 1.0), (2, 0.5), (3, 0.18)), lfo_cycles=2, depth=0.3, detune_cycles=4)
    d2 = periodic_tone(n, 82.4, harm=((1, 0.5), (2, 0.2)), lfo_cycles=3, depth=0.4, detune_cycles=2, phase=0.8)
    d = (d / np.max(np.abs(d))) * 0.55 + (d2 / np.max(np.abs(d2))) * 0.22
    sg.bufs["pad"][:, 0] += d
    sg.bufs["pad"][:, 1] += d
    # падь: Е-Н (мі-сі) і тихе ♭2 (фа), що розв'язується у мі
    sg.put("pad", S.pad_dark((52, 59), sg.dur(8 * 16 * 0.97), 0.5, 3.0, 3.0, 600.0), 0, 0, gain=0.8)
    sg.put("pad", S.pad_dark((52, 59), sg.dur(8 * 16 * 0.97), 0.5, 3.0, 3.0, 600.0), 8, 0, gain=0.8)
    for bar in (6, 14):
        sg.put("pad", S.pad_dark((65,), sg.dur(2 * 16 * 0.95), 0.4, 3.2, 2.2, 520.0), bar, 0, gain=0.7, pan=0.2)
        sg.put("pad", S.pad_dark((64, 71), sg.dur(1.6 * 16), 0.45, 2.0, 2.5, 560.0), (bar + 2) % 16, 0, gain=0.7)
    # низьке піаніно/дзвін: мотив тактів 1–4, розтягнутий до 8 тактів; удруге — рідше
    for pas, base in ((0, 0), (1, 8)):
        for b, step, midi, ln in M.motif_notes(range(4)):
            if pas == 1 and ln < 3:
                continue
            g_ = 2 * (16 * b + step)
            bar, st = base + g_ // 16, g_ % 16
            v = 0.8 if pas == 0 else 0.55
            sg.put("keys", S.low_bell(midi - 12, sg.dur(2 * ln * 0.9), v, 0.6), bar, st, gain=0.5, pan=-0.1)
    # далекі металеві удари з довгим реверберем
    for bar, st, f, v in ((1, 0, 160, 0.6), (5, 8, 233, 0.45), (9, 4, 160, 0.55), (12, 12, 233, 0.4)):
        metal_(sg, bar, st, f, 3.5, v, bar, 1.6, 0.6, pan=0.35 if bar % 2 else -0.35)
    # ледь чутне серцебиття
    for bar in range(16):
        for s in (0, 3, 8, 11):
            sg.put("drums", S.kick(0.5, True), bar, s, gain=0.22 if s % 8 == 0 else 0.15)
    # стрічковий шум (у групі без реверба, щоб не розмазувався)
    sg.bufs["bass"][:, 0] += S.tape(n, 3, 1.0)
    sg.bufs["bass"][:, 1] += S.tape(n, 4, 1.0)
    cfg = dict(
        key="Em", rt60=4.5,
        gains=dict(drums=1.0, bass=0.9, gtr=1.0, lead=1.0, brass=1.0, keys=1.6, pad=0.5, fx=1.0),
        sends=dict(keys=0.55, pad=0.12, fx=0.9, drums=0.1),
        bus=dict(),
        comp=(-18.0, 1.5, 40.0, 300.0), sat=1.0,
        sections=[(0, 8, "1-8 перший прохід"), (8, 16, "9-16 рідше")],
    )
    return sg, cfg


TRACKS = {
    "music_login": login_build,
    "music_loading": loading_build,
    "music_settings": settings_build,
}


def render_track(name, builder, stems_dir=None):
    t0 = time.time()
    sg, cfg = builder()
    ir = X.make_ir(cfg["rt60"], seed=7)
    master, stems = sg.mixdown(cfg["gains"], cfg["sends"], ir, bus_filters=cfg.get("bus"))
    if cfg.get("master_lp"):
        master = np.stack([S.filt(master[:, c], "lp", cfg["master_lp"], 0.7, circular=True) for c in range(2)], axis=1)
    # вхідний рівень до м'якого обмеження: RMS ≈ -22 дБFS, щоб насичення не плющило динаміку
    master = master * (10 ** (-22.0 / 20.0) / np.sqrt(np.mean(master ** 2)))
    if cfg.get("sat"):
        k = cfg["sat"]
        master = np.tanh(k * master) / k
    y, g_db, info = X.master_chain(master, -16.0, -1.0, cfg["comp"])
    pcm = X.to_pcm16(y)
    yq = pcm.astype(float) / 32768.0
    bar_n = sg.n / sg.bars

    def sec_rms(sig):
        out = []
        for a, b, lab in cfg["sections"]:
            seg = sig[int(a * bar_n):int(b * bar_n)]
            out.append((lab, 10 * np.log10(np.mean(seg ** 2) + 1e-12)))
        return out

    bar_n_ = bar_n
    sec_stems = {}
    for k_, v_ in stems.items():
        sec_stems[k_] = [10 * np.log10(np.mean(v_[int(a_ * bar_n_):int(b_ * bar_n_)] ** 2) + 1e-12) for a_, b_, _l in cfg["sections"]]
    crest = float(L.sample_peak_db(yq) - 10 * np.log10(np.mean(yq ** 2) + 1e-12) / 1.0)
    res = dict(
        name=name, seconds=sg.n / SR, samples=sg.n, bpm=sg.bpm, bars=sg.bars, key=cfg["key"],
        lufs=L.integrated_loudness(yq), tp=L.true_peak_db(yq), seam=X.seam_report(yq),
        peak=L.sample_peak_db(yq), pcm=pcm, loop="yes", gain_db=g_db,
        stems={k: v for k, v in stems.items()}, gain_lin=10 ** (g_db / 20.0),
        rms={k: float(np.sqrt(np.mean(v ** 2))) for k, v in stems.items()}, took=time.time() - t0,
        sec_pre=sec_rms(master), sec_stems=sec_stems, sec_post=sec_rms(yq), crest=crest, yq=yq,
    )
    return res


# ======================================================================= SFX
def cut(x, sec, fade_ms=20.0):
    n = int(sec * SR)
    y = np.array(x[:n], dtype=float)
    if len(y) < n:
        y = np.concatenate([y, np.zeros(n - len(y))])
    return S.fade(y, 0.2, fade_ms)


def at(total, items):
    out = np.zeros(int(total * SR))
    for sig, start, g in items:
        i = int(start * SR)
        seg = np.asarray(sig) * g
        e = min(len(out), i + len(seg))
        out[i:e] += seg[: e - i]
    return out


def finish(x, lufs, tail_room=None, length=None, out_ms=12.0, cap_db=-1.6):
    """Нормалізація SFX до цілі гучності кліпу (dual-mono) з обмеженням піку."""
    x = np.asarray(x, dtype=float)
    if tail_room:
        r = X.room(x, tail_room[0], tail_room[1])
        x = r.mean(axis=1)
    if length:
        x = x[: int(length * SR)]
    x = S.filt(x, "hp", 30.0)
    x = S.fade(x, 0.5, out_ms)
    g = 10 ** ((lufs - L.clip_loudness(np.stack([x, x], 1))) / 20.0)
    x = x * g
    pk = L.true_peak_db(x)
    if pk > cap_db:
        x = x * 10 ** ((cap_db - pk) / 20.0)
    return x


def mid_knock(f, dur, seed, drive=3.0, lp=2200.0):
    """Середня «плоть» удару: перевантажений тон + шум, щоб SFX чули й на малих динаміках (200-2000 Гц)."""
    n = int(dur * SR)
    t = np.arange(n) / SR
    tone = np.sin(2 * np.pi * f * t) + 0.5 * np.sin(2 * np.pi * 2.01 * f * t)
    nz = S.filt(S.noise(n, seed), "lp", lp, 0.7)
    y = np.tanh(drive * (tone * np.exp(-t / (dur * 0.35)) + 0.7 * nz * np.exp(-t / (dur * 0.2))))
    y = S.filt(y, "hp", 150.0, 0.7)
    return S.fade(y, 0.2, 8.0)


def sfx_all():
    o = {}
    # ui_move: сухий сталевий тик (храповик каната), не солодкий
    o["ui_move"] = finish(at(0.06, [(S.steel_click(3100, 1.0, 0.05, 380), 0, 1.0)]), -17.0, out_ms=15)
    # ui_confirm: важкий удар кулаком + низький бум
    o["ui_confirm"] = finish(at(0.2, [(S.thump(118, 42, 0.2, 0.09, 2.4), 0, 1.0), (S.thump(62, 38, 0.2, 0.14, 1.4), 0, 0.7),
                                      (S.steel_click(1300, 0.8, 0.03, 200), 0, 0.45),
                                      (mid_knock(260, 0.14, 21), 0, 0.9)]), -13.0, out_ms=45)
    # ui_back: приглушений низхідний глухий удар
    back = S.thump(88, 34, 0.16, 0.07, 1.3)
    o["ui_back"] = finish(at(0.15, [(S.filt(back, "lp", 420, 0.7, order=2), 0, 0.8),
                                    (S.filt(mid_knock(190, 0.12, 22, 2.5, 1100.0), "lp", 900, 0.7), 0, 0.9)]), -15.0, out_ms=45)
    # ui_error: різкий перевантажений дзижчак (кластер мі/фа) + металевий брязк
    n = int(0.3 * SR)
    t = np.arange(n) / SR
    buzz = sum(S.saw(float(S.mtof(m)), n, 0.17 * i) for i, m in enumerate((40, 41, 52, 53, 64, 65)))
    buzz = buzz * (0.62 + 0.38 * np.sign(np.sin(2 * np.pi * 27 * t)))
    buzz = np.tanh(4.0 * buzz / np.max(np.abs(buzz)))
    buzz = S.filt(buzz, "lp", 2300, 0.7) * np.minimum(t / 0.004, 1.0) * np.exp(-t / 0.4)
    o["ui_error"] = finish(at(0.3, [(buzz / np.max(np.abs(buzz)), 0, 0.9), (S.metal(410.0, 0.25, 1.0, 11, 0.12), 0, 0.8),
                                    (S.thump(70, 42, 0.2, 0.08, 1.8), 0, 0.7)]), -13.5, out_ms=70)
    # ui_toggle: сталева засувка / храповик
    o["ui_toggle"] = finish(at(0.1, [(S.steel_click(2100, 0.8, 0.03, 300), 0, 0.6), (S.steel_click(1700, 0.8, 0.03, 260), 0.032, 0.7),
                                     (S.steel_click(1150, 1.0, 0.04, 190), 0.065, 1.0)]), -16.5, out_ms=18)
    # ui_slider: сухий низький тик
    nn = int(0.04 * SR)
    tt = np.arange(nn) / SR
    tick = np.sin(2 * np.pi * 190 * tt) * np.exp(-tt / 0.008) + 0.4 * S.filt(S.noise(nn, 88), "lp", 1600) * np.exp(-tt / 0.003)
    o["ui_slider"] = finish(at(0.04, [(tick, 0, 1.0)]), -18.0, out_ms=12)
    # ui_tab: короткий свист, що закінчується ударом
    o["ui_tab"] = finish(at(0.16, [(S.whoosh(0.1, 2600, 600, 1.0, 93, 0.6), 0, 0.7),
                                   (S.thump(100, 40, 0.1, 0.05, 1.9), 0.085, 1.0), (S.steel_click(1400, 0.7, 0.03, 220), 0.085, 0.4),
                                   (mid_knock(240, 0.08, 23), 0.085, 0.8)]),
                         -14.5, out_ms=35)
    # ui_transition: зворотний тарілочний наліт у великий удар + свист
    o["ui_transition"] = finish(at(0.45, [(S.rev_swell(0.17, 1.0, 96, 400, 6500), 0, 0.7),
                                          (S.thump(82, 32, 0.29, 0.12, 2.3), 0.17, 1.0),
                                          (S.metal(150.0, 0.25, 1.0, 3, 0.18), 0.17, 0.45),
                                          (S.whoosh(0.28, 2300, 300, 1.0, 94, 0.08), 0.17, 0.55),
                                          (mid_knock(220, 0.22, 24), 0.17, 0.9)]), -12.5, out_ms=100)
    # ui_load_done: низький металевий гонг + тіло силового акорду мі (не яскраво)
    chord = S.chug(M.power("E"), 0.5, 0.9, 1, None, 2100.0)
    o["ui_load_done"] = finish(at(0.7, [(S.metal(165.0, 0.65, 1.0, 21, 0.75), 0, 0.9), (chord, 0, 0.8),
                                        (S.thump(70, 40, 0.3, 0.12, 1.6), 0, 0.6)]), -14.0, (0.3, 0.1), length=0.7, out_ms=230)
    # ui_login_ok: перші 4 ноти лейтмотиву (Е Е G F) на низькому лід/духових над ударом
    step = 0.125
    items = [(S.thump(100, 40, 0.3, 0.12, 2.2), 0, 0.9), (S.metal(160.0, 0.5, 1.0, 5, 0.3), 0, 0.4)]
    for st, midi, ln in M.MOTIF[0][:4]:
        d = ln * step
        d_ = d + (0.3 if st == 6 else 0.04)
        items.append((cut(S.lead(midi - 12, d_, 0.9, 1), d_ + 0.1, 70), st * step, 0.7))
        items.append((cut(S.horn((midi - 12,), d_, 0.8, 0.03, 1), d_ + 0.1, 70), st * step, 0.6))
    items.append((S.chug(M.power("E"), 0.45, 0.9, 1, 0.5, 2400.0), 0, 0.5))
    x = at(1.6, items)
    o["ui_login_ok"] = finish(x, -13.0, (0.35, 0.1), length=1.3, out_ms=220)
    return o


# ======================================================================= main
def main(argv=None):
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=str(DEFAULT_OUT))
    ap.add_argument("--stems", default=None)
    ap.add_argument("--only", default=None)
    a = ap.parse_args(argv)
    out = Path(a.out)
    out.mkdir(parents=True, exist_ok=True)
    manifest = []
    for name, builder in TRACKS.items():
        if a.only and a.only != name:
            continue
        r = render_track(name, builder, a.stems)
        X.write_wav(out / f"{name}.wav", r["pcm"])
        if a.stems:
            sd = Path(a.stems)
            sd.mkdir(parents=True, exist_ok=True)
            for g, v in r["stems"].items():
                if np.max(np.abs(v)) < 1e-6:
                    continue
                X.write_wav(sd / f"{name}_{g}.wav", X.to_pcm16(np.clip(v * r["gain_lin"], -1, 1)))
        s = r["seam"]
        print(f"{name}: {r['seconds']:.3f} s, {r['bpm']:.3f} BPM, LUFS {r['lufs']:.2f}, TP {r['tp']:.2f} dBTP, "
              f"crest {r['crest']:.1f} дБ, шов jump={s['jump']:.5f} (медіана {s['median_adj']:.5f}, p99 {s['p99_adj']:.5f}), "
              f"RMS10мс кінець/початок {s['rms_end10']:.4f}/{s['rms_start10']:.4f}; {r['took']:.0f} с")
        print("   секції RMS до мастер-ланцюга (дБ): " + ", ".join(f"{l}={v:.1f}" for l, v in r["sec_pre"]))
        print("   секції RMS у файлі (дБFS):        " + ", ".join(f"{l}={v:.1f}" for l, v in r["sec_post"]))
        for k_, v_ in r["sec_stems"].items():
            print(f"   стем {k_}: " + ", ".join(f"{x:.1f}" for x in v_))
        print("   RMS стемів: " + ", ".join(f"{k}={v:.3f}" for k, v in r["rms"].items()))
        manifest.append((name, r["seconds"], r["samples"], f"{r['bpm']:.2f}", r["bars"], r["key"], r["lufs"], r["tp"], "yes"))
    if not a.only or a.only == "sfx" or a.only.startswith("ui_"):
        for name, x in sfx_all().items():
            if a.only and a.only not in ("sfx", name):
                continue
            pcm = X.to_pcm16(x, dither=False)
            xq = pcm.astype(float) / 32768.0
            stereo = np.stack([xq, xq], axis=1)
            lufs = L.clip_loudness(stereo)
            tp = L.true_peak_db(xq)
            X.write_wav(out / f"{name}.wav", pcm)
            print(f"{name}: {len(x) / SR * 1000:.0f} мс, пік {L.sample_peak_db(xq):.1f} dBFS, TP {tp:.2f}, "
                  f"гучність кліпу {lufs:.1f} LUFS, мом.макс {L.momentary_max(stereo):.1f}")
            manifest.append((name, len(x) / SR, len(x), "-", "-", "Em", lufs, tp, "no"))
    if not a.only:
        lines = ["# name | seconds | samples | bpm | bars | key | LUFS | dBTP | loop=whole-clip",
                 "# LUFS: для music_* — інтегральна BS.1770-4; для ui_* — гучність усього кліпу (dual-mono), див. README"]
        for n, sec, smp, bpm, bars, key, lufs, tp, loop in manifest:
            lines.append(f"{n} | {sec:.3f} | {smp} | {bpm} | {bars} | {key} | {lufs:.2f} | {tp:.2f} | loop={loop}")
        (out / "menu_audio_manifest.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")
        (out / "LICENSE.md").write_text(LICENSE_TEXT, encoding="utf-8")


LICENSE_TEXT = """# Ліцензія аудіо меню

Усі файли цієї теки згенеровано власним процедурним синтезом
(`tools/ring_audio`, чистий Python/numpy) для гри «Ринг».

- Сторонніх семплів, лупів, пресетів чи записів немає.
- Мелодії та гармонія оригінальні; цитат і парафраз існуючих творів немає.
- Атрибуція не потрібна. Файли вільно публікувати, змінювати і продавати разом із грою.
- Відтворити файли можна командою `python -m ring_audio.render_menus` з теки `tools`
  (результат детермінований).
"""

if __name__ == "__main__":
    sys.exit(main())
