"""music_loading: екран завантаження. Розріджений груув без соло, легший за login: барабани + бас + приглушені чаги."""
from . import ring_music_kit as K
from .ring_music_common import rf, play_events, cfg_default

LIGHT = dict(gains=dict(drums=1.0, bass=0.95, gtr=0.55, lead=0.6), sends=dict(drums=0.22, gtr=0.08, lead=0.1, bass=0.0))


def _bass(b, bar, notes, grit=0.7, T=0.9, g=0.9):
    for step, midi, dur, vel in notes:
        b.bs(bar, step, midi, dur, vel=vel, g=g, grit=grit, T=T)


# =========================================================== v1: сухий ґруув з примарними нотами, ля, 84 BPM
def v1():
    b = K.Band(84, 8, seed=111)
    A = 45
    r1 = [(0, 33, 2, .95), (3, 33, 1, .75), (6, 36, 2, .85), (8, 38, 2, .85), (10, 40, 3, .8), (14, 36, 2, .7)]
    r2 = [(0, 33, 2, .95), (3, 33, 1, .75), (6, 31, 2, .8), (8, 33, 2, .85), (11, 40, 2, .85), (14, 38, 2, .7)]
    for bar in range(8):
        _bass(b, bar, r1 if bar % 2 == 0 else r2)
        evs = [(2, A, 0.6, "g"), (6, A, 0.6, "g"), (10, A, 0.6, "g"), (14, A, 0.6, "g")]
        if bar % 2 == 1:
            evs += [(11, A, 0.8, "x"), (3, A, 0.8, "x")]
        if bar % 4 == 3:
            evs += [(8, A, 2, "d")]
        play_events(b, bar, evs, mode="crunch", bass=False, g=0.42, gvel=0.85)
        b.pat(bar, dict(K="9..5..7...5.9...", S="....8..G....8.G.", H="5353535353535353", G="..3....3..3....3"))
        if bar == 0:
            b.hit("C", bar, 0, 0.7)
    b.fill(7, 12, "half", 0.7)
    return b, cfg_default(key="Am", sections=[], **LIGHT), dict(label="Варіант 1 — сухий ґруув з примарними нотами", bpm=84)


# =========================================================== v2: томи й повільний бас, соль, 96 BPM
def v2():
    b = K.Band(96, 8, seed=222)
    G = 43
    bl = [[(0, 31, 6, .95), (8, 31, 4, .85), (12, 34, 3, .85)], [(0, 31, 6, .95), (8, 36, 4, .85), (12, 34, 3, .8)],
          [(0, 31, 6, .95), (8, 31, 4, .85), (12, 34, 3, .85)], [(0, 29, 6, .95), (8, 31, 4, .85), (12, 34, 3, .8)]]
    for bar in range(8):
        _bass(b, bar, bl[bar % 4], grit=0.9, T=1.5)
        evs = [(0, G, 0.8, "X"), (10, G, 0.8, "x")]
        if bar % 2 == 1:
            evs += [(14, G + 3, 1, "x")]
        play_events(b, bar, evs, mode="mid", bass=False, g=0.45, gvel=0.9)
        b.pat(bar, dict(K="9...5...9...5.5.", X="....7.......7...", T3="..6.......6...6.", T2="......4.........",
                        H="5.5.5.5.5.5.5.5."))
        if bar == 0:
            b.hit("C", bar, 0, 0.6)
    b.fill(7, 10, "half", 0.7)
    return b, cfg_default(key="Gm", sections=[], rt60=0.45, **LIGHT), dict(label="Варіант 2 — томи й повільний бас", bpm=96)


# =========================================================== v3: панк-пульс, фа-дієз мінор, 150 BPM
def v3():
    b = K.Band(150, 12, seed=333)
    Fs = 42
    prog = [Fs, Fs, 45, 40]  # F#, F#, A, E
    for bar in range(12):
        root = prog[bar % 4]
        r8 = "x.x.x.x.x.x.x.x."
        evs = rf(r8, root)
        if bar % 4 == 3:
            evs = rf("X.x.x.x.", root) + [(8, 47, 4, "d"), (12, 49, 4, "d")]
        play_events(b, bar, evs, mode="mid", g=0.42, gvel=0.9, gb=0.85, bgrit=0.7, bT=0.6)
        if bar < 4:
            b.pat(bar, dict(K="9...5...9...5...", S="........7.......", H="7.5.7.5.7.5.7.5."))
        elif bar < 8:
            b.pat(bar, dict(K="9...9...9...9...", S="....9.......9...", H="7.5.7.5.7.5.7.o."))
        else:
            b.pat(bar, dict(K="9..59...9.5.9...", S="....9.......9.5.", H="7.5.7.5.7.5.7.5."))
        if bar in (0, 4, 8):
            b.hit("C", bar, 0, 0.7)
    b.fill(3, 12, "snare", 0.6)
    b.fill(7, 12, "16", 0.7)
    b.fill(11, 12, "16", 0.7)
    return b, cfg_default(key="F#m", sections=[], rt60=0.4, **LIGHT), dict(label="Варіант 3 — швидкий панк-пульс, легший", bpm=150)


BUILDERS = {"music_loading.v1": v1, "music_loading.v2": v2, "music_loading.v3": v3}
