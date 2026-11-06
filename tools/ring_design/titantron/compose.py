"""Композитор: (гімік, роль, щабель, варіант) → ролик (шари з доріжками в бітах). Детермінований, без випадковості.

Ролик «ключові кадри до introBeats + модифікатори назавжди»: удар (hitBeat) віддається режисурі виходу для піро,
світла й камери. Тексти — слоти (nick/full/tag), кольори — слоти `$…` (kits.json), прозорість — кроки ghost/dim/mid.
Усе, що тут малюється, програвач відтворює лише `translate/scale/rotate/opacity` + заливкою токеном (без blur).
"""
import math

from .common import ALPHA, KITS, TIERS
from . import timeline as T

G, D, M = ALPHA["ghost"], ALPHA["dim"], ALPHA["mid"]
PW, PH, PY = 1.70, 0.36, 0.03          # плита: ширина, висота, центр y (одиниці = висота кадру)
OFFX, OFFY = 0.022, 0.030              # «друкований» зсув задньої плити
SLANT = 12                              # нахил плити й тексту (як виноски B04)


LOOK = {  # плита й ім'я за архетипом: висота плити, кегль імені, нахил плити за роллю
    "monster": {"ph": 0.40, "name": 0.33, "rot": {"hero": 0, "villain": 0, "rebel": 0}},
    "flyer": {"ph": 0.34, "name": 0.30, "rot": {"hero": -5, "villain": 5, "rebel": -3}},
    "technician": {"ph": 0.36, "name": 0.30, "rot": {"hero": 0, "villain": 0, "rebel": 0}},
    "showman": {"ph": 0.38, "name": 0.31, "rot": {"hero": 0, "villain": 0, "rebel": 0}},
}


def look(c):
    lk = LOOK[c.g]
    return lk["ph"], lk["name"], lk["rot"][c.role] + (-2 if c.role == "rebel" and c.g != "flyer" else 0)


def K(b, v, e="l"):
    return [round(b, 3), round(v, 4) if isinstance(v, float) else v, e]


def tr(layer, prop, *keys):
    layer.setdefault("tr", {})[prop] = [k if k[2] != "l" else k[:2] for k in keys]


class C:
    def __init__(self, gimmick, role, tier, variant):
        self.g, self.role, self.tier, self.v = gimmick, role, tier, variant
        t = KITS["tiers"][tier]
        self.H = t["hitBeat"]
        self.rank = TIERS.index(tier)
        self.flip = -1 if variant & 1 else 1
        self.alt = (variant >> 1) & 1
        self.layers, self.events = [], []

    def ok(self, minimum):
        return self.rank >= TIERS.index(minimum)

    def add(self, id, k, minimum="gym", **kw):
        if not self.ok(minimum):
            return None
        layer = {"id": id, "k": k}
        layer.update(kw)
        self.layers.append(layer)
        return layer


def snap_on(b, v, off=0.0):
    """Миттєве вмикання в біті b."""
    return [K(0, off), K(b, v, "s")]


# ----------------------------------------------------------------- спільне ядро: плита, ім'я, підписи

ENTRY = {  # як плита й ім'я з'являються на ударі
    "slam": "падіння з небес/знизу з відскоком",
    "fly": "виліт збоку вздовж нахилу",
    "wipe": "шторка зліва направо",
    "pop": "виринання з масштабу",
}


def core(c, style, plate_mod, enter=(0.0, -0.95)):
    """Плита з «друкованим» зсувом (задня плита) і плита: вхід залежить від стилю (slam/fly/wipe/pop)."""
    H, f = c.H, c.flip
    ex, ey = enter
    ph_, _, rot = look(c)
    wipe = style == "wipe"
    ox, oy = (0.0, 0.0) if wipe else (OFFX, OFFY)
    specs = [("plate_back", "$role" if wipe else "$accent", PW + (0.05 if wipe else 0.0), ph_ + (0.05 if wipe else 0.0), ox, oy),
             ("plate", "$ground2" if wipe else "$role", PW, ph_, 0.0, 0.0)]
    out = []
    for pid, tint, w, h, dx, dy in specs:
        L = c.add(pid, "plate", w=w, h=h, slant=SLANT, tint=tint, rot=rot, x=dx, y=PY + dy)
        if style == "slam":
            if ex:
                tr(L, "x", K(0, ex + dx), K(H - 0.2, ex + dx, "s"), K(H + 0.12, dx, "o3"))
            tr(L, "y", K(0, ey + dy), K(H - 0.2, ey + dy, "s"), K(H + 0.12, PY + dy, "ob"))
            tr(L, "s", K(0, 1.35), K(H - 0.2, 1.35, "s"), K(H + 0.12, 1.0, "o3"))
            tr(L, "a", K(0, 0), K(H - 0.2, 1.0, "s"))
        elif style == "fly":
            tr(L, "x", K(0, -1.9 * f + dx), K(H - 0.15, -1.9 * f + dx, "s"), K(H + 0.30, dx, "o3"))
            tr(L, "a", K(0, 0), K(H - 0.15, 1.0, "s"))
        elif wipe:
            tr(L, "clip", K(0, 0.0), K(H, 0.0, "s"), K(H + 0.5, 1.0, "o3"))
        else:
            tr(L, "s", K(0, 0.2), K(H + 0.05, 0.2, "s"), K(H + 0.45, 1.0, "ob"))
            tr(L, "a", K(0, 0), K(H + 0.05, 1.0, "s"))
        L["mod"] = {"pulse": plate_mod}
        out.append(L)
    return out


def add_texts(c, style, name_size=None):
    H, f = c.H, c.flip
    gym = c.tier == "gym"
    ph_, nsz, rot = look(c)
    name_size = name_size or nsz
    name_spec = {"slot": "nick", "font": "display900", "size": name_size, "fit": 1.50,
                 "fill": "$paper" if style == "wipe" else "$onRole",
                 "shadow": ["$ground" if style == "wipe" else "$shadow", 0.012, 0.016], "slant": SLANT, "tracking": 0.01}
    name = c.add("name", "text", text=name_spec, y=PY - 0.004, rot=rot)
    # вхід імені — з тим самим стилем, що й плита, з легким запізненням
    d = 0.04
    if style == "slam":
        tr(name, "y", K(0, -0.95 + PY), K(H - 0.2 + d, -0.95 + PY, "s"), K(H + 0.14 + d, PY - 0.004, "ob"))
        tr(name, "s", K(0, 1.6), K(H - 0.2 + d, 1.6), K(H + 0.14 + d, 1.0, "o3"))
        tr(name, "a", K(0, 0), K(H - 0.2 + d, 1.0, "s"))
    elif style == "fly":
        tr(name, "x", K(0, -1.9 * f), K(H - 0.15 + d, -1.9 * f, "s"), K(H + 0.30 + d, 0.0, "o3"))
        tr(name, "a", K(0, 0), K(H - 0.15 + d, 1.0, "s"))
    elif style == "wipe":
        tr(name, "clip", K(0, 0.0), K(H + 0.15, 0.0, "s"), K(H + 0.65, 1.0, "o3"))
    else:
        tr(name, "s", K(0, 0.2), K(H + 0.05 + d, 0.2, "s"), K(H + 0.45 + d, 1.0, "ob"))
        tr(name, "a", K(0, 0), K(H + 0.05 + d, 1.0, "s"))
    name["mod"] = {"pulse": [1, 0.03, 0.28, H + 1.0]} if style in ("slam", "pop") else {"pulse": [1, 0.012, 0.3, H + 1.0]}
    # підписи
    ksp = {"slot": "full", "font": "display700", "size": 0.072 if not gym else 0.075, "fit": 1.5, "fill": "$paper",
           "shadow": ["$ground", 0.006, 0.008], "tracking": 0.22, "hand": True}
    kick = c.add("kicker", "text", text=ksp, y=PY - ph_ / 2 - 0.105)
    if style == "wipe":
        steps = [K(0, 0.0), K(H + 0.6, 0.0, "s")] + [K(H + 0.6 + 0.1 * i, i / 6.0, "s") for i in range(1, 7)]
        tr(kick, "clip", *steps)
    else:
        ky = PY - ph_ / 2 - 0.105
        tr(kick, "a", K(0, 0), K(H + 0.55, 0.0), K(H + 1.0, 1.0, "o3"))
        tr(kick, "y", K(0, ky + 0.055), K(H + 0.55, ky + 0.055, "s"), K(H + 1.0, ky, "o3"))
    tsp = {"slot": "tag", "font": "display700", "size": 0.066, "fit": 1.5, "fill": "$cool", "tracking": 0.30, "hand": True}
    tag = c.add("tag", "text", text=tsp, y=PY + ph_ / 2 + OFFY + 0.045)
    tr(tag, "a", K(0, 0), K(H + 1.0, 0.0), K(H + 1.4, 1.0, "o3"))
    return name


def flash(c, a, b_off=0.3):
    H = c.H
    fl = c.add("flash", "rect", w=2.4, h=1.3, tint="$flash", fixed=True)
    tr(fl, "a", K(0, 0), K(H, a, "s"), K(H + b_off, 0.0, "o2"))
    return fl


def finish(c, shake_amp, shake_dur):
    H = c.H
    seal = c.add("seal", "sprite", "hall", sprite="brand_seal", w=0.17, h=0.17, x=0.88 * c.flip, y=0.37, fixed=True)
    if seal:
        tr(seal, "a", K(0, 0), K(H + 1.2, 0.0), K(H + 1.8, M, "o2"))
    if KITS["tiers"][c.tier]["scan"]:
        c.add("scan", "sprite", sprite="scan", tile=0.0156, w=2.2, h=1.2, tint="$ground", a=G, fixed=True)
    c.events.append({"b": H, "t": "hit"})
    if shake_amp:
        c.events.append({"b": H, "t": "shake", "amp": shake_amp, "dur": shake_dur})


# ----------------------------------------------------------------- архетипи

def monster(c):
    H, f = c.H, c.flip
    ang = {"hero": -12, "villain": 12, "rebel": -12}[c.role] * f
    c.add("bg", "rect", w=2.4, h=1.3, tint="$ground", fixed=True)
    ht = c.add("halftone", "sprite", "hall", sprite="halftone_r", w=2.0, h=1.0, tint="$role", a=D, flipx=(f < 0))
    if ht:
        tr(ht, "a", K(0, 0), K(H, D, "o2"))
        tr(ht, "s", K(0, 1.0), K(H + 0.8, 1.14, "o2"))
    s1 = c.add("stripe_a", "rect", w=3.4, h=0.34, y=-0.30, rot=ang, tint="$role", a=M)
    tr(s1, "x", K(0, -3.0 * f), K(H - 0.01, -3.0 * f, "s"), K(H + 0.40, 0.0, "o3"))
    s1["mod"] = {"sway": [0.02, 0.0, 8, 0.0]}
    s2 = c.add("stripe_b", "rect", "hall", w=3.4, h=0.16, y=0.34, rot=ang, tint="$paper", a=G)
    if s2:
        tr(s2, "x", K(0, 3.0 * f), K(H - 0.01, 3.0 * f, "s"), K(H + 0.45, 0.0, "o3"))
        s2["mod"] = {"sway": [-0.02, 0.0, 8, 0.0]}
    if c.alt:
        for i, sx_ in enumerate((-1, 1)):
            bo = c.add("bolt%d" % i, "sprite", "hall", sprite="bolt2" if f < 0 else "bolt1", w=0.52, h=1.04, x=0.66 * sx_, y=0.0, rot=9 * sx_,
                       tint="$roleText", flipx=(sx_ < 0))
            if bo:
                tr(bo, "a", K(0, 0), K(H, D, "s"), K(H + 0.25, 0.0, "s"), K(H + 0.33, D, "s"))
                tr(bo, "s", K(0, 0.6), K(H, 0.6, "s"), K(H + 0.5, 1.0, "o3"))
    cr = c.add("crack", "sprite", "arena", sprite="crack", w=1.15, h=1.15, tint="$paper", flipx=(f < 0)) if not c.alt else None
    if cr:
        tr(cr, "a", K(0, 0), K(H, G, "s"))
        tr(cr, "s", K(0, 0.55), K(H, 0.55, "s"), K(H + 0.6, 1.0, "o3"))
    rg = c.add("shock", "ring", "hall", w=1.0, h=1.0, t=0.035, tint="$paper")
    if rg:
        tr(rg, "s", K(0, 0.15), K(H, 0.15, "s"), K(H + 1.1, 2.2, "o3"))
        tr(rg, "a", K(0, 0), K(H, M, "s"), K(H + 1.1, 0.0))
    if c.role == "villain":
        fl = c.add("flames", "sprite", "arena", sprite="flame", w=2.0, h=0.6, y=0.5, tint="$roleText", a=D, flipx=(f < 0))
        if fl:
            tr(fl, "a", K(0, 0), K(H, D, "s"))
            tr(fl, "sy", K(0, 0.0), K(H, 0.0, "s"), K(H + 0.7, 1.0, "o3"))
            tr(fl, "y", K(0, 0.5), K(H, 0.5, "s"), K(H + 0.7, 0.2, "o3"))
            fl["mod"] = {"sway": [0.012, 0.0, 2, 0.0]}
    for i, tb in enumerate([H - 1.0, H - 0.5] if c.ok("arena") else [H - 0.5]):
        bar = c.add("tick%d" % i, "rect", w=0.014, h=1.3, tint="$paper")
        tr(bar, "a", K(0, 0), K(tb, M, "s"), K(tb + 0.08, 0.0, "s"))
    enter = {"villain": (0.0, -0.95), "hero": (0.0, 0.95), "rebel": (1.9 * f, 0.0)}[c.role]
    core(c, "slam", [1, 0.025, 0.28, H + 1.0], enter)
    add_texts(c, "slam")
    flash(c, G)
    finish(c, 0.014, 1.2)


def flyer(c):
    H, f = c.H, c.flip
    ang = {"hero": -12, "villain": 12, "rebel": -12}[c.role] * f
    c.add("bg", "rect", w=2.4, h=1.3, tint="$ground", fixed=True)
    rays = c.add("rays", "sprite", "hall", sprite="rays12w" if c.alt else "rays24", w=2.0, h=2.0, tint="$role")
    if rays:
        tr(rays, "a", K(0, 0), K(H, G, "o2"))
        tr(rays, "s", K(0, 0.25), K(H, 1.0, "o3"))
        rays["mod"] = {"spin": [2.5 * f, 0]}
    bands = [("band_a", -0.31, 0.20, "$role", M, H - 0.60), ("band_b", 0.07, 0.05, "$paper", 1.0, H - 0.45),
             ("band_c", 0.36, 0.14, "$roleText", D, H - 0.30)]
    for i, (bid, y, h, tint, a, t0) in enumerate(bands):
        mn = "gym" if i == 0 else "hall"
        b = c.add(bid, "rect", mn, w=3.4, h=h, y=y, rot=ang, tint=tint, a=a)
        if b:
            tr(b, "x", K(0, -2.9 * f), K(t0, -2.9 * f, "s"), K(t0 + 0.5, 0.0, "o3"))
            b["mod"] = {"sway": [0.015 * (1 if i % 2 else -1), 0.0, 4, 0.0]}
    sp = c.add("speed", "sprite", "hall", sprite="speed", w=1.8, h=1.8, tint="$paper")
    if sp:
        tr(sp, "a", K(0, 0), K(H, M, "s"), K(H + 0.9, 0.0))
        tr(sp, "s", K(0, 0.7), K(H, 0.7, "s"), K(H + 0.9, 1.25, "o3"))
    # іскри: вилітають з-за плити
    for i, deg in enumerate([20, 70, 130, 200, 250, 320]):
        rad = 0.62 + 0.12 * (i % 3)
        rx, ry = math.cos(math.radians(deg)) * rad * 1.25, math.sin(math.radians(deg)) * rad * 0.62
        s = c.add("spark%d" % i, "rect", "arena", w=0.026, h=0.026, rot=45, tint="$paper", y=PY)
        if s:
            tr(s, "x", K(0, 0.0), K(H, 0.0, "s"), K(H + 0.7, rx, "o3"))
            tr(s, "y", K(0, PY), K(H, PY, "s"), K(H + 0.7, PY + ry, "o3"))
            tr(s, "a", K(0, 0), K(H, 1.0, "s"), K(H + 0.7, 0.0))
    core(c, "fly", [1, 0.012, 0.3, H + 1.0])
    # хвости імені (ghost) — між плитою й ім'ям
    for i, (dly, a) in enumerate([(0.10, M), (0.20, D)]):
        spec = {"slot": "nick", "font": "display900", "size": look(c)[1], "fit": 1.50, "fill": "$roleText", "slant": SLANT,
                "tracking": 0.01}
        g = c.add("trail%d" % i, "text", "hall", text=spec, y=PY - 0.004, rot=look(c)[2])
        if g:
            tr(g, "x", K(0, -1.9 * f), K(H - 0.15 + dly, -1.9 * f, "s"), K(H + 0.30 + dly, 0.0, "o3"))
            tr(g, "a", K(0, 0), K(H - 0.15 + dly, a, "s"), K(H + 0.45 + dly, 0.0))
    add_texts(c, "fly")
    ch = c.add("chevrons", "sprite", "arena", sprite="chevron", tile=0.12, w=2.2, h=0.12, y=0.43, tint="$roleText",
               flipx=(f < 0))
    if ch:
        tr(ch, "a", K(0, 0), K(H + 0.3, 0.0), K(H + 0.8, D, "o2"))
        ch["mod"] = {"drift": [0.5 * f, 0.0, 0]}
    flash(c, G)
    finish(c, 0.006, 0.8)


def technician(c):
    H, f = c.H, c.flip
    c.add("bg", "rect", w=2.4, h=1.3, tint="$ground", fixed=True)
    gr = c.add("grid", "sprite", sprite="halftone_flat" if c.alt else "grid", tile=0.125 if c.alt else 0.25, w=2.2, h=1.2, tint="$cool")
    tr(gr, "a", K(0, 0), K(0.5, G, "s"), K(0.75, 0.0, "s"), K(1.0, G, "s"))
    gr["mod"] = {"drift": [0.02 * f, 0.0, 0]}
    for i, y in enumerate((-0.375, 0.375)):
        r = c.add("rule%d" % i, "rect", w=0.0, h=0.008, y=y, tint="$roleText", a=M)
        tr(r, "w", K(0, 0.0), K(H - 1.2 + 0.4 * i, 0.0, "s"), K(H - 0.2 + 0.4 * i, 2.3, "o3"))
    tk = c.add("ticks", "sprite", "hall", sprite="ticks", tile=0.094, w=2.2, h=0.094, y=-0.445, tint="$cool", a=D)
    if tk:
        tk["mod"] = {"drift": [-0.06 * f, 0.0, 0]}
    for i, (sx_, sy_, rot) in enumerate([(-1, -1, 0), (1, -1, 90), (1, 1, 180), (-1, 1, 270)]):
        br = c.add("bracket%d" % i, "sprite", "arena", sprite="bracket", w=0.16, h=0.16, x=0.90 * sx_, y=0.40 * sy_,
                   rot=rot, tint="$roleText", fixed=True)
        if br:
            tr(br, "a", K(0, 0), K(H - 0.5, 0.0), K(H, M, "o2"))
    sb = c.add("scanbar", "rect", "hall", w=0.012, h=1.2, tint="$roleText", a=M, fixed=True)
    if sb:
        sb["mod"] = {"sweep": [4, H, -1.1, 1.1]}
    core(c, "wipe", [4, 0.008, 0.3, H + 1.0])
    cur = c.add("cursor", "rect", "hall", w=0.008, h=0.30, y=PY, tint="$roleText")
    if cur:
        tr(cur, "x", K(0, -0.78), K(H + 0.15, -0.78, "s"), K(H + 0.65, 0.78, "o3"))
        tr(cur, "a", K(0, 0), K(H + 0.15, 1.0, "s"), K(H + 0.66, 0.0, "s"))
    add_texts(c, "wipe")
    flash(c, G)
    finish(c, 0.0, 0.1)


def showman(c):
    H, f = c.H, c.flip
    c.add("bg", "rect", w=2.4, h=1.3, tint="$ground", fixed=True)
    big = "rays12w" if c.alt else "star8"
    st = c.add("star", "sprite", "hall", sprite=big, w=1.5 if not c.alt else 2.0, h=1.5 if not c.alt else 2.0, tint="$role", a=D)
    if st:
        tr(st, "s", K(0, 0.0), K(H, 0.05, "s"), K(H + 0.6, 1.0, "ob"))
        st["mod"] = {"spin": [4.0 * f, H]}
    for i, sx_ in enumerate((-1, 1)):
        co = c.add("cone%d" % i, "sprite", "hall", sprite="cone", w=0.5, h=1.0, x=0.78 * sx_, y=0.0, rot=-18 * sx_,
                   tint="$paper", a=G)
        if co:
            co["mod"] = {"rock": [10, 4, 0.0 if i == 0 else 0.5]}
    cf = c.add("confetti_a", "sprite", "arena", sprite="confetti", tile=0.5, w=2.2, h=1.2, tint="$paper", a=G)
    if cf:
        tr(cf, "a", K(0, 0), K(H + 0.3, 0.0), K(H + 0.8, G, "o2"))
        cf["mod"] = {"drift": [0.0, 0.25, 0]}
    cf2 = c.add("confetti_b", "sprite", "arena", sprite="confetti", tile=0.4, w=2.2, h=1.2, tint="$roleText", a=G)
    if cf2:
        tr(cf2, "a", K(0, 0), K(H + 0.3, 0.0), K(H + 0.8, G, "o2"))
        cf2["mod"] = {"drift": [0.04, 0.38, 0]}
    core(c, "pop", [1, 0.02, 0.3, H + 1.0])
    add_texts(c, "pop")
    for i, sx_ in enumerate((-1, 1)):
        cu = c.add("curtain%d" % i, "sprite", sprite="curtain", tile=0.125, w=1.05, h=1.3, x=0.52 * sx_, tint="$role")
        keys = [K(0, 0.52 * sx_)]
        t0 = H - 1.0
        keys.append(K(t0, 0.52 * sx_, "s"))
        for j in range(8):  # барабанний дріб: дрібне тремтіння перед відкриттям
            keys.append(K(t0 + 0.125 * (j + 1), 0.52 * sx_ + (0.010 if j % 2 == 0 else -0.010) * sx_, "s"))
        keys.append(K(H + 0.45, 1.6 * sx_, "o3"))
        tr(cu, "x", *keys)
    rows = [("top", -0.44, 0), ("bot", 0.44, 1)] if c.ok("hall") else [("top", -0.44, 0)]
    for nm, y, par in rows:
        a = c.add("bulbs_%s_a" % nm, "sprite", sprite="bulbs_a", w=2.0, h=0.1875, y=y, tint="$paper", fixed=True)
        a["mod"] = {"blink": [par, 0]}
        b = c.add("bulbs_%s_b" % nm, "sprite", sprite="bulbs_b", w=2.0, h=0.1875, y=y, tint="$roleText", fixed=True)
        b["mod"] = {"blink": [1 - par, 0]}
    flash(c, G)
    finish(c, 0.008, 0.8)


BUILDERS = {"monster": monster, "flyer": flyer, "technician": technician, "showman": showman}


def rebel_glitch(c):
    """Бунтар: «збій» — копія імені зі зсувом, що спалахує двічі за такт; у Палаці ще й рваний край зверху."""
    if c.role != "rebel" or not c.ok("hall"):
        return
    H = c.H
    if c.ok("arena") and c.g != "technician":
        tn = {"id": "torn", "k": "sprite", "sprite": "torn", "w": 2.0, "h": 0.125, "y": -0.4375, "tint": "$role", "a": M,
              "flipx": c.flip < 0}
        tr(tn, "a", K(0, 0), K(H, M, "s"))
        tr(tn, "y", K(0, -0.7), K(H, -0.7, "s"), K(H + 0.3, -0.4375, "o3"))
        c.layers.insert(1, tn)
    spec = {"slot": "nick", "font": "display900", "size": look(c)[1], "fit": 1.50, "fill": "$paper", "slant": SLANT,
            "tracking": 0.01}
    g = {"id": "glitch", "k": "text", "text": spec, "x": 0.016, "y": PY - 0.012, "a": M, "rot": look(c)[2],
         "mod": {"flick": [2, 0.14, H + 1.0]}}
    # одразу над ім'ям (перед підписами): знайдемо індекс імені
    idx = next(i for i, l in enumerate(c.layers) if l["id"] == "name")
    c.layers.insert(idx, g)


def compose(gimmick, role, tier, variant=0):
    c = C(gimmick, role, tier, variant)
    BUILDERS[gimmick](c)
    rebel_glitch(c)
    t = KITS["tiers"][tier]
    tl = {"v": 1, "gimmick": gimmick, "role": role, "tier": tier, "variant": variant,
          "bpm": KITS["gimmicks"][gimmick]["bpm"], "fps": t["fps"], "hitBeat": c.H,
          "layers": c.layers, "events": c.events}
    tl["introBeats"] = math.ceil(T.intro_end(tl) * 2) / 2
    return tl
