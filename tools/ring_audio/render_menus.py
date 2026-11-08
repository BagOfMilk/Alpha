"""Рендер музики й SFX меню «Рингу» (Вхід, Завантаження, Налаштування).

Запуск з каталогу tools:
    python -m ring_audio.render_menus [--out DIR] [--stems DIR] [--only NAME]

Аранжування (послідовності нот, патерни барабанів) — тут; інструменти — synth.py,
дані лейтмотиву — motif.py, мікс — mix.py, гучність — loudness.py.
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


def play_chord(sg, group, fn, notes, bar, step, strum_ms=5.0, gain=1.0, pan=0.0):
    for i, n in enumerate(notes):
        sg.put(group, fn(n), bar, step, gain=gain, pan=pan, ms_off=i * strum_ms)


# ======================================================================= ВХІД
def login_section(bar):
    return "A" if bar < 8 else "B" if bar < 16 else "C" if bar < 24 else "D"


def login_drums(sg, bar):
    sec = login_section(bar)
    odd = bar % 2
    kicks = [[0, 6, 10], [0, 3, 6, 10]][odd] + ([14] if sec == "D" and odd else [])
    ghosts = [[7, 15], [9, 14]][odd]
    opens = {"A": [], "B": [14] if odd else [], "C": [6, 14], "D": [6, 14]}[sec]
    hv = [1.0, 0.38, 0.7, 0.38] * 4
    fill_from = {7: 12, 15: 10, 23: 8, 31: 8}.get(bar, 16)
    breakdown = bar == 22
    if breakdown:
        kicks, ghosts = [], []
    for s in range(16):
        if s >= fill_from:
            continue
        if s in opens:
            sg.put("drums", S.hat(0.8, True), bar, s, gain=0.55, pan=0.3)
        else:
            v = hv[s] * (0.3 if breakdown or bar == 23 else 0.62)
            sg.put("drums", S.hat(vq(v)), bar, s, gain=1.0, pan=0.3)
        if s in kicks:
            sg.put("drums", S.kick(1.0), bar, s, gain=0.95)
        if s in (4, 12):
            if breakdown:
                sg.put("drums", S.rim(0.8), bar, s, gain=0.7, pan=-0.1)
            else:
                sg.put("drums", S.snare(1.0), bar, s, gain=0.85)
        if s in ghosts:
            sg.put("drums", S.snare(1.0, True), bar, s, gain=0.45, pan=-0.1)
    # філи
    if bar == 7:
        for s, v in zip((12, 13, 14, 15), (0.45, 0.6, 0.75, 0.95)):
            sg.put("drums", S.snare(1.0, True), bar, s, gain=v)
    elif bar == 15:
        for s, m, v in zip((10, 11, 12, 13, 14, 15), (64, 64, 60, 60, 55, 55), (0.5, 0.6, 0.65, 0.75, 0.85, 0.95)):
            sg.put("drums", S.tom(m, 1.0), bar, s, gain=v, pan=(-0.3 if m > 60 else 0.3 if m < 58 else 0))
        sg.put("drums", S.kick(1.0), bar, 14, gain=0.8)
    elif bar == 23:
        for s, v in zip((8, 9, 10, 11), (0.35, 0.45, 0.55, 0.7)):
            sg.put("drums", S.snare(1.0, True), bar, s, gain=v + 0.2)
        for s, m, v in zip((12, 13, 14, 15), (64, 60, 57, 53), (0.8, 0.85, 0.9, 1.0)):
            sg.put("drums", S.tom(m, 1.0), bar, s, gain=v, pan=0.35 - 0.2 * (s - 12))
        sg.put("drums", S.kick(1.0), bar, 8, gain=0.8)
        sg.put("drums", S.kick(1.0), bar, 12, gain=0.7)
    elif bar == 31:
        for s in (8, 10):
            sg.put("drums", S.kick(1.0), bar, s, gain=0.9)
        for s, v in zip((10, 12, 13, 14), (0.8, 0.85, 0.9, 1.0)):
            sg.put("drums", S.snare(1.0), bar, s, gain=v * 0.8)
        sg.put("drums", S.hat(0.8, True), bar, 15, gain=0.6, pan=0.3)
        sg.put("drums", S.kick(1.0), bar, 15, gain=0.9)
    if bar == 24:
        sg.put("drums", S.crash(0.8), bar, 0, gain=0.5, pan=0.2)


def bass_notes(bar, sec):
    """Список (крок, MIDI, довжина, вид) для такту басу."""
    k = bar % 4
    if bar == 31:  # хроматичний хід до F
        return [(0, 36, 2, "note"), (3, 36, 1, "ghost"), (4, 48, 1, "pop"), (6, 36, 1, "note"), (8, 36, 1, "note"),
                (9, 37, 1, "note"), (10, 38, 1, "note"), (11, 39, 1, "note"), (12, 40, 1, "note"),
                (13, 41, 1, "note"), (14, 53, 1, "pop"), (15, 41, 1, "ghost")]
    if bar == 22:
        return [(0, 34, 4, "note"), (8, 39, 4, "note")]
    if bar == 23:
        return [(0, 36, 3, "note"), (6, 36, 1, "ghost"), (8, 36, 3, "note")]
    if k in (0, 1):
        r, th = (41, 3) if k == 0 else (37, 4)
        return [(0, r, 2, "note"), (3, r, 1, "ghost"), (4, r + 12, 1, "pop"), (6, r, 1, "note"),
                (8, r + 7, 2, "note"), (10, r + th, 1, "note"), (11, r + 12, 1, "pop"), (12, r, 2, "note"),
                (15, r, 1, "ghost")]
    if k == 2:
        return [(0, 34, 2, "note"), (3, 34, 1, "ghost"), (4, 46, 1, "pop"), (6, 34, 1, "note"),
                (8, 39, 2, "note"), (10, 39, 1, "ghost"), (11, 51, 1, "pop"), (12, 46, 2, "note"),
                (14, 37, 2, "note")]
    return [(0, 36, 2, "note"), (3, 36, 1, "ghost"), (4, 48, 1, "pop"), (6, 36, 1, "note"),
            (8, 39, 2, "note"), (10, 40, 2, "note"), (12, 43, 2, "note"), (14, 41, 2, "note")]


def play_bass(sg, bar, sec, notes):
    for step, midi, ln, kind in notes:
        if kind == "note" and step == 0 and sec in ("B", "D"):
            kind = "slap"
        v = {"note": 0.9, "slap": 0.95, "pop": 0.7, "ghost": 0.55}[kind]
        sg.put("bass", S.slap_bass(midi, sg.dur(ln * 0.9), vq(v), kind), bar, step, gain=1.0)


def play_motif(sg, first_bar, fn_notes, bars=range(8), stabs=None):
    """fn_notes(bar_abs, step, midi, ln)."""
    for b, step, midi, ln in M.motif_notes(bars):
        fn_notes(first_bar + b, step, midi, ln)


def brass_motif(sg, first_bar, vel=0.85):
    def f(bar, step, midi, ln):
        ch = M.harmonise(midi)
        d = sg.dur(ln * (0.95 if ln >= 4 else 0.8))
        v = vq(vel if ln >= 3 else vel - 0.1)
        sg.put("brass", S.brass(ch, d, v), bar, step, gain=1.0)
    play_motif(sg, first_bar, f)
    for b, steps in M.MOTIF_STABS.items():
        ch = M.fold(M.CHORDS[M.chord_at(b, 0)], 60, 77)
        for st in steps:
            sg.put("brass", S.brass(ch, sg.dur(1.4), vq(vel), 1.1), first_bar + b, st, gain=1.0)


def login_build():
    sg = X.Song(116, 32, swing=0.09, seed=1160)
    for bar in range(32):
        sec = login_section(bar)
        login_drums(sg, bar)
        play_bass(sg, bar, sec, bass_notes(bar, sec))
        # --- Rhodes-акомпанемент (A, B, пів-обсягу в C)
        if sec in ("A", "B", "C") and bar not in (22, 23):
            hits = [(0, 3, 0.75), (6, 2, 0.65), (10, 2, 0.7), (14, 2, 0.55)] if sec != "C" else [(0, 4, 0.5), (10, 3, 0.45)]
            for st, ln, v in hits:
                notes = M.CHORDS[M.chord_at(bar, st)]
                if sec == "C":
                    notes = notes[1:4]
                play_chord(sg, "keys", lambda n, v=v, ln=ln: S.rhodes(n, sg.dur(ln * 0.9), vq(v), 1.0, 0.0),
                           notes, bar, st, gain=0.75 if sec != "C" else 0.6, pan=-0.3)
        if bar in (22, 23):
            notes = M.CHORDS[M.chord_at(bar, 0)][1:4]
            play_chord(sg, "keys", lambda n: S.rhodes(n, sg.dur(10), 0.5, 0.8, 0.2), notes, bar, 0, gain=0.6, pan=-0.3)
        # --- гітара: офбіт-чопи
        chop_v = {"A": 0.7 if bar >= 4 else 0.0, "B": 0.75, "C": 0.55, "D": 0.85}[sec]
        if chop_v and bar not in (7, 15, 23):
            pat = [(2, 1.0), (3, 0.45), (6, 0.9), (10, 1.0), (11, 0.45), (14, 0.85)]
            if sec == "C":
                pat = [(2, 1.0), (10, 1.0)]
            for st, v in pat:
                if sec != "A" and bar % 2 == 0 and st == 14:
                    continue
                ch = M.fold(M.CHORDS[M.chord_at(bar, st)][1:], 62, 77)[:3]
                sg.put("guitar", S.guitar(ch, 0.065, vq(v * chop_v)), bar, st, gain=1.0, pan=0.35)
    # --- B: тема на духових (такти 8-15)
    brass_motif(sg, 8, 0.8)
    # --- C: тема на Rhodes октавою нижче + гітара-відповідь (16-23)
    def lead_c(bar, step, midi, ln):
        sg.put("keys", S.rhodes(midi - 12, sg.dur(ln * 0.95), 0.8 if ln >= 3 else 0.7, 1.0, 0.12), bar, step, gain=0.95, pan=-0.1)
    play_motif(sg, 16, lead_c)
    for b in (0, 1, 2, 7):
        ch = M.fold(M.CHORDS[M.chord_at(16 + b, 6)], 65, 82)
        hi = ch[-1], ch[-2]
        for i, n in enumerate(hi):
            sg.put("guitar", S.guitar((n,), 0.09, 0.8, 1.2), 16 + b, 6 + i, gain=0.9, pan=0.35)
    for st in (2, 6):  # такт 5 теми: гітарні відповіді-стаби
        sg.put("guitar", S.guitar(M.fold(M.CHORDS["Fm9"][1:], 62, 77)[:3], 0.07, 0.8, 1.2), 16 + 4, st, gain=0.9, pan=0.35)
    # --- D: повний склад (24-31)
    brass_motif(sg, 24, 0.9)

    def lead_d(bar, step, midi, ln):
        for o, g in ((0, 0.5), (-12, 0.6)):
            sg.put("keys", S.rhodes(midi + o, sg.dur(ln * 0.95), 0.7, 1.0, 0.0), bar, step, gain=g, pan=-0.15 if o else 0.15)
    play_motif(sg, 24, lead_d)
    cfg = dict(
        gains={"drums": 0.62, "bass": 0.55, "keys": 0.70, "brass": 1.10, "guitar": 1.10, "pad": 0.0, "fx": 0.0},
        sends={"drums": 0.05, "bass": 0.0, "keys": 0.22, "brass": 0.16, "guitar": 0.12},
        rt60=1.5, comp=(-7.0, 1.5, 20.0, 200.0), key=M.KEY_NAME,
    )
    return sg, cfg


# ============================================================== ЗАВАНТАЖЕННЯ
def loading_build():
    sg = X.Song(116, 16, swing=0.08, seed=1161)
    for bar in range(16):
        odd = bar % 2
        # барабани: легкі
        for s in range(16):
            if s % 2 == 0:
                sg.put("drums", S.hat(vq(0.42 if s % 4 == 0 else 0.3), False, True), bar, s, gain=0.7, pan=0.25)
            elif s in (3, 11):
                sg.put("drums", S.hat(0.2, False, True), bar, s, gain=0.5, pan=0.25)
        for s in (4, 12):
            sg.put("drums", S.rim(0.7), bar, s, gain=0.55, pan=-0.1)
        sg.put("drums", S.kick(0.8, True), bar, 0, gain=0.85)
        if odd:
            sg.put("drums", S.kick(0.8, True), bar, 10, gain=0.6)
        # бас: корені + кілька попів
        k = bar % 4
        r = {0: 41, 1: 37, 2: 34, 3: 36}[k]
        notes = [(0, r, 3, "note"), (6, r, 1, "note"), (8, r if k != 2 else 39, 2, "note"), (12, (r if k != 2 else 39) + 12, 1, "pop")]
        for st, m, ln, kind in notes:
            sg.put("bass", S.slap_bass(m, sg.dur(ln * 0.9), 0.7 if kind == "note" else 0.5, kind), bar, st, gain=0.9)
        # пад + м'який чорд
        ch = M.CHORDS[M.chord_at(bar, 0)]
        if k in (0, 2):
            sg.put("pad", S.pad(tuple(M.fold(ch, 48, 70)), sg.dur(30), 0.5), bar, 0, gain=0.7, vel_jit=0)
        play_chord(sg, "keys", lambda n: S.rhodes(n, sg.dur(5), 0.5, 0.8, 0.12), ch[1:], bar, 0, gain=0.5, pan=-0.3, strum_ms=7)
        if bar % 4 == 2:
            ch2 = M.CHORDS[M.chord_at(bar, 8)]
            play_chord(sg, "keys", lambda n: S.rhodes(n, sg.dur(5), 0.5, 0.8, 0.12), ch2[1:], bar, 8, gain=0.5, pan=-0.3, strum_ms=7)
        # дуже тихі чопи на офбітах
        for st in (6, 14):
            g3 = M.fold(M.CHORDS[M.chord_at(bar, st)][1:], 62, 77)[:3]
            sg.put("guitar", S.guitar(g3, 0.06, 0.5), bar, st, gain=0.55, pan=0.35)

    # уривки теми на Rhodes: перші половини тактів 1-4 / 5-8, другі половини — наступні 4 такти
    def frag(first_bar, motif_bars, half):
        for b in motif_bars:
            for step, midi, ln in M.MOTIF[b]:
                if (step < 8) == half:
                    sg.put("keys", S.rhodes(midi - 12, sg.dur(ln * 0.95), 0.6, 0.9, 0.1),
                           first_bar + (b % 4), step, gain=0.8, pan=-0.1)
    frag(0, range(0, 4), True)
    frag(4, range(0, 4), False)
    frag(8, range(4, 8), True)
    frag(12, range(4, 8), False)
    cfg = dict(
        gains={"drums": 1.2, "bass": 0.7, "keys": 0.8, "brass": 0.0, "guitar": 0.5, "pad": 0.45, "fx": 0.0},
        sends={"drums": 0.05, "keys": 0.25, "pad": 0.2, "guitar": 0.12},
        rt60=1.3, comp=(-16.0, 2.2, 20.0, 180.0), key=M.KEY_NAME,
    )
    return sg, cfg


# ============================================================== НАЛАШТУВАННЯ
def settings_build():
    sg = X.Song(84, 16, swing=0.14, seed=1162)
    for bar in range(16):
        cn = M.chord_at_slow(bar, 0)
        ch = M.CHORDS[cn]
        root = M.ROOTS[cn] + 12 if M.ROOTS[cn] < 38 else M.ROOTS[cn]
        # бас
        sg.put("bass", S.round_bass(root, sg.dur(6), 0.8), bar, 0, gain=0.9)
        sg.put("bass", S.round_bass(root + 7, sg.dur(3), 0.6), bar, 10, gain=0.8)
        # Rhodes-акомпанемент
        play_chord(sg, "keys", lambda n: S.rhodes(n, sg.dur(6), 0.45, 0.6, 0.35), ch[1:], bar, 0, gain=0.55, pan=-0.3, strum_ms=11)
        play_chord(sg, "keys", lambda n: S.rhodes(n, sg.dur(3), 0.35, 0.6, 0.35), ch[2:], bar, 10, gain=0.4, pan=-0.3, strum_ms=11)
        # падь на 2 такти
        if bar % 2 == 0:
            sg.put("pad", S.pad(tuple(M.fold(ch, 46, 68)), sg.dur(30), 0.5), bar, 0, gain=0.8, vel_jit=0)
        # ударні: half-time, щітка, приглушені
        sg.put("drums", S.kick(0.8, True), bar, 0, gain=0.8)
        if bar % 2 == 1:
            sg.put("drums", S.kick(0.7, True), bar, 10, gain=0.6)
        sg.put("drums", S.brush(0.8, 0.32), bar, 8, gain=0.7, pan=-0.1)
        sg.put("drums", S.rim(0.5), bar, 8, gain=0.25, pan=-0.1)
        for s in range(0, 16, 2):
            sg.put("drums", S.hat(vq(0.4 if s % 4 == 0 else 0.25), False, True), bar, s, gain=0.6, pan=0.25)

    # тема: такти 1-4 лейтмотиву, розтягнуті вдвічі (аугментація) на 8 тактів
    def stretched(first_bar, vel, bright, skip_short):
        for b, step, midi, ln in M.motif_notes(range(4)):
            if skip_short and ln <= 1:
                continue
            gs = (b * 16 + step) * 2
            sg.put("keys", S.rhodes(midi - 12, sg.dur(ln * 2 * 0.95), vel, bright, 0.35),
                   first_bar + gs // 16, gs % 16, gain=0.85, pan=0.1)
    stretched(0, 0.6, 0.7, False)
    stretched(8, 0.5, 0.5, True)
    sg.bufs["fx"][:, 0] = S.vinyl(sg.n, 3, 9.0, 0.12)
    sg.bufs["fx"][:, 1] = S.vinyl(sg.n, 4, 9.0, 0.12)
    cfg = dict(
        gains={"drums": 0.6, "bass": 0.9, "keys": 0.9, "brass": 0.0, "guitar": 0.0, "pad": 0.55, "fx": 1.0},
        sends={"drums": 0.08, "keys": 0.3, "pad": 0.25},
        rt60=1.8, comp=(-20.0, 2.0, 25.0, 250.0), key=M.KEY_NAME,
        bus={"drums": [("lp", 4200, 0.7)], "keys": [("lp", 6500, 0.7)], "pad": [("lp", 3500, 0.7)]},
        master_lp=6800, sat=1.4,
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
    if cfg.get("sat"):
        k = cfg["sat"]
        master = np.tanh(k * master) / k
    y, g_db, info = X.master_chain(master, -16.0, -1.0, cfg["comp"])
    # стеми: ті самі підсилення (стискання/ліміт — нелінійні, тож наближено)
    pcm = X.to_pcm16(y)
    yq = pcm.astype(float) / 32768.0
    res = dict(
        name=name, seconds=sg.n / SR, samples=sg.n, bpm=sg.bpm, bars=sg.bars, key=cfg["key"],
        lufs=L.integrated_loudness(yq), tp=L.true_peak_db(yq), seam=X.seam_report(yq),
        peak=L.sample_peak_db(yq), pcm=pcm, loop="yes", gain_db=g_db,
        stems={k: v for k, v in stems.items()}, gain_lin=10 ** (g_db / 20.0),
        rms={k: float(np.sqrt(np.mean(v ** 2))) for k, v in stems.items()}, took=time.time() - t0,
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


def thump(f0=95, f1=48, dur=0.16, tau=0.05):
    n = int(dur * SR)
    t = np.arange(n) / SR
    f = f1 + (f0 - f1) * np.exp(-t / 0.025)
    return S.fade(np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t / tau), 0.3, 15)


def finish(x, peak_db, tail_room=None, length=None, out_ms=12.0):
    x = np.asarray(x, dtype=float)
    if tail_room:
        r = X.room(x, tail_room[0], tail_room[1])
        x = r.mean(axis=1) * 1.0
    if length:
        x = x[: int(length * SR)]
    x = S.filt(x, "hp", 30.0)
    x = S.fade(x, 0.5, out_ms)
    x = x * (10 ** (peak_db / 20.0) / (np.max(np.abs(x)) + 1e-12))
    tp = L.true_peak_db(x)
    if tp > -1.0:
        x *= 10 ** ((-1.0 - tp) / 20.0)
    return x


def sfx_all():
    o = {}
    o["ui_move"] = finish(at(0.075, [(S.wood_click(2600, 0.8), 0, 0.5), (cut(S.rhodes(84, 0.03, 0.6, 0.6), 0.07, 25), 0, 1.0)]), -8.5)
    o["ui_confirm"] = finish(at(0.24, [(cut(S.brass((53,), 0.1, 0.9, 0.7, 0.8), 0.2, 40), 0, 1.0),
                                       (thump(), 0, 1.1), (S.wood_click(1100, 0.8), 0, 0.5)]), -3.5)
    o["ui_back"] = finish(at(0.16, [(cut(S.rhodes(72, 0.05, 0.7), 0.09, 30), 0, 1.0),
                                    (cut(S.rhodes(68, 0.06, 0.55), 0.1, 45), 0.06, 0.9)]), -8)
    err = cut(S.brass((65, 66), 0.25, 0.95, 1.0, 0.3, False), 0.32, 70)
    tt = np.arange(len(err)) / SR
    err = err * (0.65 + 0.35 * np.sign(np.sin(2 * np.pi * 26 * tt)) * 0.6 + 0.0)
    err = S.filt(err, "lp", 3200)
    o["ui_error"] = finish(at(0.34, [(err, 0, 1.0), (thump(70, 45, 0.2, 0.08), 0, 1.0)]), -2.5)
    o["ui_toggle"] = finish(at(0.12, [(S.wood_click(1500, 0.7), 0, 0.5), (cut(S.rhodes(72, 0.02, 0.5), 0.06, 20), 0, 0.8),
                                      (S.wood_click(2300, 0.9), 0.05, 0.5), (cut(S.rhodes(80, 0.02, 0.5), 0.06, 25), 0.05, 0.8)]), -9)
    o["ui_slider"] = finish(at(0.05, [(cut(S.rhodes(84, 0.015, 0.6, 0.6), 0.045, 20), 0, 1.0), (S.wood_click(3000, 0.4, 0.02), 0, 0.3)]), -10)
    o["ui_tab"] = finish(at(0.17, [(S.swish(0.15, 1200, 6000, 1.0, 91, 0.4), 0, 1.0), (S.wood_click(2200, 0.5), 0, 0.5)]), -6)
    o["ui_transition"] = finish(at(0.5, [(S.swish(0.42, 300, 6500, 1.0, 92, 0.7), 0, 0.9),
                                         (cut(S.brass((53, 60, 65), 0.14, 0.9, 1.0, 0.6, False), 0.3, 80), 0.2, 0.9),
                                         (thump(80, 45, 0.2, 0.07), 0.2, 0.6)]), -3, (0.18, 0.12), length=0.5, out_ms=110)
    arp = [(cut(S.rhodes(m, 0.1, 0.65), 0.25, 80), i * 0.07, 0.9) for i, m in enumerate((65, 68, 72, 75, 79))]
    arp.append((cut(S.brass((53, 56, 60, 63), 0.2, 0.55, 0.8, 0.4, False), 0.4, 120), 0.3, 0.7))
    o["ui_load_done"] = finish(at(0.75, arp), -5, (0.3, 0.12), length=0.8, out_ms=200)
    t_ = [(0.0, 72, 0.36), (0.388, 75, 0.12), (0.517, 77, 0.26), (1.034, 80, 0.4)]
    items = []
    for st, n, d in t_:
        items.append((cut(S.brass(M.harmonise(n, (0, 2)), d, 0.8, 1.0, 0.5, False), d + 0.08, 50), st, 0.85))
        items.append((cut(S.rhodes(n, d, 0.7), d + 0.2, 80), st, 0.6))
    o["ui_login_ok"] = finish(at(1.6, items), -4, (0.35, 0.14))
    # обрізати хвіст до ~1.38 с
    x = o["ui_login_ok"]
    n = int(1.38 * SR)
    o["ui_login_ok"] = S.fade(x[:n], 0.1, 140)
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
        print(f"{name}: {r['seconds']:.2f} s, {r['bpm']:.3f} BPM, LUFS {r['lufs']:.2f}, TP {r['tp']:.2f} dBTP, "
              f"шов jump={s['jump']:.5f} (медіана різниць {s['median_adj']:.5f}, p99 {s['p99_adj']:.5f}), "
              f"RMS10мс кінець/початок {s['rms_end10']:.4f}/{s['rms_start10']:.4f}; "
              f"RMS стемів " + ", ".join(f"{k}={v:.3f}" for k, v in r["rms"].items()) + f"; {r['took']:.0f} с")
        manifest.append((name, r["seconds"], r["samples"], f"{r['bpm']:.2f}", r["bars"], "Fm", r["lufs"], r["tp"], "yes"))
    if not a.only or a.only.startswith("ui_"):
        for name, x in sfx_all().items():
            if a.only and a.only != name:
                continue
            pcm = X.to_pcm16(x, dither=False)
            xq = pcm.astype(float) / 32768.0
            stereo = np.stack([xq, xq], axis=1)
            lufs = L.clip_loudness(stereo)
            tp = L.true_peak_db(xq)
            X.write_wav(out / f"{name}.wav", pcm)
            print(f"{name}: {len(x) / SR * 1000:.0f} мс, пік {L.sample_peak_db(xq):.1f} dBFS, TP {tp:.2f}, "
                  f"гучність кліпу {lufs:.1f} LUFS, мом.макс {L.momentary_max(stereo):.1f}")
            manifest.append((name, len(x) / SR, len(x), "-", "-", "Fm", lufs, tp, "no"))
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
