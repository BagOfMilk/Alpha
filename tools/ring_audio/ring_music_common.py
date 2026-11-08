"""Спільна «мова» для композицій музики меню: ріфи як списки подій, гра гурту по тактах."""
from . import ring_music_kit as K


def rf(rhythm, root, kind_map=None):
    """Рядок ритму -> події чагів. x — чаг, X — акцент, g — глуха нота."""
    return [(i, root, 0.9 if ch != "g" else 0.6, ch) for i, ch in enumerate(rhythm) if ch in "xXg"]


def play_events(b, bar, evs, mode="hi", bass=True, g=0.5, gb=0.9, bgrit=1.0, bT=0.9, gvel=1.0, lead_mode="mid",
                bdur_k=0.95, pan=0.8, bass_from=None, ring_T=None):
    for step, root, dur, kind in evs:
        if kind in "xX":
            vel = (1.0 if kind == "X" else 0.8) * gvel
            b.gt(bar, step, (root, root + 7), dur, mute=True, vel=min(vel, 1.0), mode=mode, g=g, pan=pan)
        elif kind == "g":
            b.gt(bar, step, (root,), dur, mute=True, vel=0.45 * gvel, mode=mode, g=g * 0.9, pan=pan)
        elif kind == "r":
            b.gt(bar, step, (root, root + 7, root + 12), dur, vel=0.95 * gvel, mode=mode, g=g, pan=pan, T=ring_T)
        elif kind == "d":
            b.gt(bar, step, (root, root + 7), dur, vel=0.88 * gvel, mode=mode, g=g, pan=pan, T=ring_T)
        elif kind == "s":
            b.gt(bar, step, (root,), dur, vel=0.85 * gvel, mode=mode, g=g, pan=pan, T=ring_T)
        elif kind == "l":
            b.solo_gt(bar, step, (root,), dur, vel=0.9, mode=lead_mode)
        if bass and kind in "xXrd":
            br = K.bass_reg(bass_from if bass_from else root)
            bd = dur * (bdur_k if kind in "rd" else 1.0)
            b.bs(bar, step, br, bd, vel=0.95 if kind in "XR" else 0.82, g=gb, grit=bgrit, T=bT)


def cfg_default(**kw):
    c = dict(gains=dict(drums=1.0, bass=0.8, gtr=0.9, lead=0.8), sends=dict(drums=0.2, gtr=0.06, lead=0.1, bass=0.0),
             rt60=0.55, comp=(-17.0, 2.0, 12.0, 120.0), sat=1.3)
    c.update(kw)
    return c
