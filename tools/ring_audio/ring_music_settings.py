"""music_settings: налаштування. Спокійніший важкий фон: не бадьорий і не зловісний. Без соло-блиску."""
from . import ring_music_kit as K
from .ring_music_common import play_events, cfg_default

SOFT = dict(gains=dict(drums=0.9, bass=0.9, gtr=0.85, lead=0.7), sends=dict(drums=0.28, gtr=0.12, lead=0.16, bass=0.0))


def chord(b, bar, step, notes, dur, vel=0.8, mode="crunch", mute=False, T=None, g=0.45):
    b.gt(bar, step, notes, dur, mute=mute, vel=vel, mode=mode, g=g, pan=0.7, T=T)


# =========================================================== v1: повільний блюз 12/8, ля мінор, 66 BPM
def v1():
    b = K.Band(66, 12, seed=1111, res=12, hum=3.0)
    Am = (45, 52, 55, 60)
    Dm = (50, 57, 60, 65)
    F = (41, 48, 53, 57)
    E7 = (40, 47, 50, 56)
    form = [Am, Am, Am, Am, Dm, Dm, Am, Am, F, E7, Am, E7]
    roots = [45, 45, 45, 45, 50, 50, 45, 45, 41, 40, 45, 40]
    fifths = [52, 52, 52, 52, 57, 57, 52, 52, 48, 47, 52, 47]
    for bar in range(12):
        ch = form[bar]
        chord(b, bar, 0, ch, 5, 0.82)
        chord(b, bar, 6, ch[:3], 2, 0.55, mute=True)
        chord(b, bar, 8, ch[:3], 1, 0.5, mute=True)
        chord(b, bar, 9, ch, 3, 0.7)
        r, f5 = K.bass_reg(roots[bar], 28, 41), fifths[bar] - 12
        b.bs(bar, 0, r, 3, 0.9, g=0.9, grit=0.6, T=1.1)
        b.bs(bar, 3, r, 2, 0.7, g=0.9, grit=0.6, T=1.1)
        b.bs(bar, 6, f5 if f5 < 50 else r, 3, 0.8, g=0.9, grit=0.6, T=1.1)
        b.bs(bar, 9, r + (2 if bar % 4 == 3 else 0), 3, 0.75, g=0.9, grit=0.6, T=1.1)
        b.pat(bar, dict(K="8.....5.....", X="...6.....6..", R="7..4.6" + "7..4.6"), vscale=0.9)
        if bar in (0, 4, 8):
            b.hit("C", bar, 0, 0.5)
    # м'який філ у проході
    for j, inst in enumerate(("T2", "T3", "T3")):
        b.hit(inst, 11, 8 + j * 1.33, 0.45 + 0.1 * j)
    # рідкі лінії соло-гітари (довгі ноти, легкий кранч)
    for bar, step, m, ln in ((3, 6, 69, 3), (3, 9, 67, 3), (7, 0, 64, 6), (7, 6, 67, 6), (11, 0, 71, 4), (11, 4, 69, 8)):
        b.solo_gt(bar, step, (m,), ln, vel=0.75, mode="crunch", pan=-0.15, g=0.5, T=1.6)
    return b, cfg_default(key="Am (12-тактовий блюз, 12/8)", sections=[], rt60=0.9, **SOFT), dict(label="Варіант 1 — повільний блюз 12/8", bpm=66)


# =========================================================== v2: слаадж-колискова, ре, 56 BPM
def v2():
    b = K.Band(56, 12, seed=2222, hum=3.5)
    D = 38
    prog = [D, D, 34, 34, 36, 36, D, D, 34, 36, D, D]  # D D Bb Bb C C D D Bb C D D
    for bar in range(12):
        r = prog[bar]
        b.gt(bar, 0, (r, r + 7, r + 12), 14, vel=0.8, mode="crunch", g=0.5, pan=0.75, T=3.2)
        b.gt(bar, 14, (r, r + 7), 2, mute=True, vel=0.45, mode="crunch", g=0.4, pan=0.75)
        b.bs(bar, 0, K.bass_reg(r, 28, 41), 14, 0.9, g=0.95, grit=0.5, T=3.0)
        b.pat(bar, dict(K="8.........5....." if bar % 2 == 0 else "8.............5.", S="........6.......",
                        R="5...4...5...4..." if bar % 4 < 2 else "4...4...4...4..."))
        if bar % 4 == 0:
            b.hit("C", bar, 0, 0.45)
        if bar % 4 == 3:
            b.hit("T3", bar, 12, 0.45)
            b.hit("T3", bar, 14, 0.4)
    # висока нота-«стогін» ліда раз на 4 такти
    for bar, m in ((2, 74), (6, 72), (10, 69)):
        b.solo_gt(bar, 4, (m,), 12, vel=0.6, mode="crunch", pan=-0.2, g=0.4, T=2.2)
    return b, cfg_default(key="Dm", sections=[], rt60=1.1, **SOFT), dict(label="Варіант 2 — слаадж-колискова", bpm=56)


# =========================================================== v3: повільна рок-балада, до мінор, 72 BPM
def v3():
    b = K.Band(72, 16, seed=3333, hum=3.0)
    C, Ab, Bb = 48, 44, 46
    prog = [C, C, Ab, Bb] * 4
    mel = {  # (бар, крок, MIDI, довжина)
        4: [(0, 67, 6), (8, 63, 6)], 5: [(0, 63, 6), (8, 62, 6)], 6: [(0, 60, 8), (10, 62, 5)], 7: [(0, 58, 10)],
        12: [(0, 70, 4), (4, 67, 4), (8, 63, 6)], 13: [(0, 67, 6), (8, 63, 6)], 14: [(0, 65, 6), (8, 63, 4), (12, 60, 4)], 15: [(0, 60, 14)],
    }
    for bar in range(16):
        r = prog[bar]
        b.gt(bar, 0, (r, r + 7, r + 12), 6, vel=0.85, mode="crunch", g=0.5, pan=0.75, T=1.8)
        b.gt(bar, 6, (r, r + 7), 2, mute=True, vel=0.55, mode="crunch", g=0.42, pan=0.75)
        b.gt(bar, 8, (r, r + 7, r + 12), 6, vel=0.8, mode="crunch", g=0.5, pan=0.75, T=1.8)
        b.gt(bar, 14, (r, r + 7), 2, mute=True, vel=0.5, mode="crunch", g=0.42, pan=0.75)
        rb = K.bass_reg(r, 29, 43)
        b.bs(bar, 0, rb, 6, 0.9, g=0.95, grit=0.6, T=1.2)
        b.bs(bar, 8, rb, 5, 0.8, g=0.95, grit=0.6, T=1.2)
        b.pat(bar, dict(K="8.....5.7.5.....", X="....6.......6...", H="3.3.3.3.3.3.3.3."), vscale=0.9)
        if bar % 4 == 0:
            b.hit("C", bar, 0, 0.45)
        for step, m, ln in mel.get(bar, []):
            b.solo_gt(bar, step, (m,), ln, vel=0.8, mode="crunch", pan=-0.1, g=0.55, T=2.0)
    b.fill(15, 12, "half", 0.45)
    return b, cfg_default(key="Cm", sections=[], rt60=0.9, **SOFT), dict(label="Варіант 3 — повільна рок-балада з простою мелодією", bpm=72)


BUILDERS = {"music_settings.v1": v1, "music_settings.v2": v2, "music_settings.v3": v3}
