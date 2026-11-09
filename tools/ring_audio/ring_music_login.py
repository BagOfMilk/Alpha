"""music_login: головна тема титулу. Три різні ідеї — дроп-D нюметал-ґруув, арена-хардрок, повільний слаадж-тиск."""
from . import ring_music_kit as K
from .ring_music_common import rf, play_events, cfg_default


# =========================================================== v1: дроп-D, синкопований ґруув, 116 BPM
def v1():
    b = K.Band(116, 24, seed=101)
    D = 38
    a = rf("X.xg..x.X.xx..x.", D)
    hook1 = rf("X.xg..x.X.xx", D) + [(12, 41, 2, "r"), (14, 43, 2, "r")]
    hook2 = rf("X.xg..x.", D) + [(8, 46, 3, "r"), (11, 45, 2, "r"), (14, D, 1, "x"), (15, D, 1, "x")]
    chorus = {
        0: [(0, D, 6, "r"), (6, D, 1, "x"), (7, D, 1, "x"), (8, D, 4, "r"), (12, 41, 4, "r")],
        1: [(0, D, 6, "r"), (6, D, 1, "x"), (7, D, 1, "x"), (8, 43, 4, "r"), (12, 41, 4, "r")],
        2: [(0, D, 6, "r"), (6, D, 1, "x"), (7, D, 1, "x"), (8, D, 4, "r"), (12, 41, 4, "r")],
        3: [(0, 46, 6, "r"), (6, 46, 1, "x"), (7, 46, 1, "x"), (8, 45, 4, "r"), (12, 48, 4, "r")],
    }
    brk = ["X..X..x.........", "X..X..x.........", "X..X..x.X.......", "X..X..X.X.X.X.xx"]
    drums_a = dict(K="9.55..6.9.55..7.", S="........9.......", G="..3.......3.....", H="7.5.7.5.7.5.7.5.")
    drums_b = dict(K="9.....5.9.5.....", S="....9.......9...", H="7.5.7.5.7.5.7.o.")
    for bar in range(24):
        if bar < 8 or 12 <= bar < 16 or bar >= 22:
            evs = a if bar % 2 == 0 else (hook1 if (bar // 2) % 2 == 0 else hook2)
            if bar == 23:
                evs = rf("X.xg..x.X.xx", D) + [(12, D, 1, "X"), (13, D, 1, "x"), (14, D, 1, "X"), (15, D, 1, "X")]
            play_events(b, bar, evs, mode="hi")
            dr = dict(drums_a)
            if bar in (4, 5, 6, 7, 12, 13, 14, 15):
                dr["H"] = "7.5.7.5.7.5.7.o."
            b.pat(bar, dr)
            if bar in (0, 4, 12):
                b.hit("C", bar, 0, 1.0)
        elif 8 <= bar < 12 or 20 <= bar < 22:
            cb = chorus[(bar - 8) % 4 if bar < 12 else (bar - 20) * 3 % 4]
            play_events(b, bar, cb, mode="hi", gb=0.95)
            b.pat(bar, drums_b)
            b.hit("C", bar, 0, 1.0 if bar % 2 == 0 else 0.7)
            if bar % 2 == 0:
                b.hit("C", bar, 8, 0.8)
        else:  # 16-19 брейкдаун
            r = brk[bar - 16]
            evs = rf(r, D)
            play_events(b, bar, evs, mode="hi")
            b.gt(bar, 0, (D, D + 7, D + 12), 16, vel=0.4, mode="hi", g=0.3, T=3.0)
            b.bs(bar, 0, D, 16, 0.7, g=0.8, grit=0.9, T=3.0)
            b.pat(bar, dict(K=r.replace("x", "7").replace("X", "9").replace(".", "."), S="........8.......", T3=("6..7" + "." * 12)))
            if bar == 16:
                b.hit("C", bar, 0, 1.0)
    b.fill(7, 8, "16")
    b.fill(15, 10, "down")
    b.fill(19, 8, "16", 1.0)
    b.fill(23, 8, "snare", 1.0)
    cfg = cfg_default(key="Dm (дроп-D)", sections=[])
    return b, cfg, dict(label="Варіант 1 — дроп-D, синкопований ґруув", bpm=116)


# =========================================================== v2: арена-хардрок у мі, 126 BPM
def v2():
    b = K.Band(126, 24, seed=202)
    E = 40
    v_a = rf("X.x.x.x.X.x.x.x.", E)
    v_b = rf("X.x.x.x.", E) + [(8, 43, 2, "d"), (10, 45, 2, "d"), (12, 46, 1, "d"), (13, 45, 1, "d"), (14, 43, 2, "d")]
    pre = rf("XxxxXxxxXxxxXxxX", E)
    ch = lambda r2: [(0, r2, 6, "r"), (6, r2, 2, "d"), (8, r2, 6, "r"), (14, r2, 2, "x")]
    prog = [E, 43, 45, 43]
    bridge = [(0, E, 4, "r"), (4, E, 1, "x"), (6, E, 1, "x"), (8, 46, 4, "r"), (12, 45, 2, "d"), (14, 43, 2, "d")]
    dr_v = dict(K="9...5.5.9...5.5.", S="....9.......9...", H="7.5.7.5.7.5.7.o.")
    dr_v2 = dict(K="9...5.5.9..5.5.9", S="....9.......9...", R="7.5.7.5.7.5.7.5.")
    dr_ch = dict(K="9...9...9...9...", S="....9.......9...", H="9.5.9.5.9.5.9.5.")
    for bar in range(24):
        if bar < 8:
            play_events(b, bar, v_a if bar % 2 == 0 else v_b, mode="hi", gb=0.9)
            b.pat(bar, dr_v if bar < 4 else dr_v2)
            if bar in (0, 4):
                b.hit("C", bar, 0, 1.0)
        elif bar < 10:
            play_events(b, bar, pre, mode="hi")
            b.pat(bar, dict(K="9.5.9.5.9.5.9.5.", S="....9.......9.9.", H="7.7.7.7.7.7.7.7."))
        elif bar < 18:
            play_events(b, bar, ch(prog[(bar - 10) % 4]), mode="hi", gb=0.95)
            b.pat(bar, dr_ch)
            b.hit("C", bar, 0, 1.0)
            if bar % 2 == 0:
                b.hit("C", bar, 8, 0.8)
        elif bar < 22:
            play_events(b, bar, bridge if bar % 2 == 0 else v_b, mode="hi")
            b.pat(bar, dict(K="9..5.5..9..5.5..", S="........9.......", H="5...5...5...5...", T3="....6.......6..."))
        else:
            play_events(b, bar, v_a if bar == 22 else rf("X.x.X.x.X.xxXxxX", E), mode="hi")
            b.pat(bar, dr_v)
    b.fill(9, 8, "16")
    b.fill(17, 10, "down")
    b.fill(21, 8, "16")
    b.fill(23, 8, "snare")
    cfg = cfg_default(key="Em", sections=[])
    return b, cfg, dict(label="Варіант 2 — арена-хардрок у мі", bpm=126)


# =========================================================== v3: слаадж-тиск, дроп-C, 100 BPM
def v3():
    b = K.Band(100, 24, seed=303)
    C = 36
    h1 = [(0, C, 5, "r"), (6, C, 1, "x"), (7, C, 1, "x"), (8, C + 1, 4, "r"), (12, C, 4, "r")]
    h2 = [(0, C, 5, "r"), (6, C, 1, "x"), (7, C, 1, "x"), (8, C + 3, 4, "r"), (12, C + 1, 4, "r")]
    h3 = [(0, C + 6, 6, "r"), (6, C + 6, 1, "x"), (8, C + 5, 4, "r"), (12, C + 1, 4, "r")]
    gal = rf("XxxxXxxxXxxxXxxx", C)
    dr = dict(K="9...9.5.......5.", S="........9.......", H="5...5...5...5...", T3="......6.......7.")
    dr2 = dict(K="9.5.9.5.9.5.9.5.", S="........9.......", H="7...7...7...7...")
    for bar in range(24):
        if bar < 8 or 20 <= bar:
            ev = h1 if bar % 4 == 0 else h2 if bar % 4 == 1 else h1 if bar % 4 == 2 else h3
            play_events(b, bar, ev, mode="hi", gb=1.0, bgrit=1.2, bT=1.4)
            b.pat(bar, dr)
            if bar % 4 == 0:
                b.hit("C", bar, 0, 1.0)
        elif bar < 12:
            play_events(b, bar, gal, mode="hi", bgrit=1.3)
            b.pat(bar, dict(K="9.9.9.9.9.9.9.9.", S="....9.......9...", H="7.7.7.7.7.7.7.7."))
            if bar == 8:
                b.hit("C", bar, 0, 1.0)
        elif bar < 16:
            ev = [(0, C, 8, "r"), (8, C + 1, 8, "r")] if bar % 2 == 0 else [(0, C + 3, 8, "r"), (8, C + 1, 4, "r"), (12, C, 4, "r")]
            play_events(b, bar, ev, mode="hi", gb=1.0, bgrit=1.2, bT=1.6)
            b.pat(bar, dict(K="9.......9.......", S="........9.......", R="5...5...5...5..."))
            b.hit("C", bar, 0, 1.0)
        else:
            ev = rf("X..........x....", C) if bar % 2 == 0 else rf("X.......X.......", C)
            if bar == 19:
                ev = rf("X.X.X.XxXxXxXxXX", C)
            play_events(b, bar, ev, mode="hi", gb=1.0, bgrit=1.3)
            b.gt(bar, 0, (C, C + 7, C + 12), 16, vel=0.4, mode="hi", g=0.3, T=3.0)
            b.bs(bar, 0, C + 12, 16, 0.7, g=0.9, grit=1.0, T=3.0)
            b.pat(bar, dict(K=("9" + "." * 15) if bar < 19 else "9.9.9.9.9.9.9.9.", S=("........9......." if bar < 19 else "....9.......9.9."),
                            T3="9......." + "........" if bar < 19 else "......9......."))
            if bar == 16:
                b.hit("C", bar, 0, 1.0)
    b.fill(7, 8, "half")
    b.fill(11, 8, "16")
    b.fill(15, 8, "half")
    b.fill(19, 10, "down")
    b.fill(23, 8, "half")
    cfg = cfg_default(key="Cm (дроп-C)", sections=[], rt60=0.7, gains=dict(drums=1.0, bass=0.9, gtr=0.9, lead=0.8))
    return b, cfg, dict(label="Варіант 3 — слаадж-тиск у дроп-C", bpm=100)


BUILDERS = {"music_login.v1": v1, "music_login.v2": v2, "music_login.v3": v3}
