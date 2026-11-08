# -*- coding: utf-8 -*-
"""Реквізит рестлерів «Рингу»: пояс, стілець, синай, кейс, мікрофон, бак і розпилювач спрею.

  "C:/Program Files/Blender Foundation/Blender 4.5/blender.exe" -b --factory-startup --python tools/blender/ring_cast/cast_props.py -- <репо> [предмети…]

Геометрія — кодом, без випадковості (генератор текстур має фіксоване зерно). Видає:
  ArtSource/Ring/Cast/props.blend
  Assets/Art/Ring/Cast/Props/<ім'я>.fbx + Textures/<ім'я>_{albedo,normal,orm}.png + props.json
  tools/ring_preview/data/props.glb (вузли Prop_belt, Prop_chair, Prop_kendo, Prop_case, Prop_mic, Prop_spray_tank, Prop_spray_gun)

КОНТРАКТ ХВАТУ (простір Blender, Z вгору; у GLB/FBX осі конвертує експортер): початок координат предмета — центр кулака
на ручці; +Z — уздовж ручки (у бік великого пальця); +Y — від хвату до основної маси предмета; X = Y × Z.
Пояс — виняток: прямий уздовж +Z, центр центральної пластини в початку, щоб головний виконавець зігнув його через плече.
Текстури — PBR 1024: albedo (sRGB), normal (OpenGL, Y+), orm (R — AO, G — шорсткість, B — метал). Слів у текстурах немає.
Кольори — з docs/ring/design/brand_tokens.json.
"""
import bpy, bmesh, sys, os, json, math, time
import numpy as np
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

ARGV = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
REPO = ARGV[0] if ARGV else os.getcwd()
ONLY = ARGV[1:]
OUT = os.path.join(REPO, "Assets", "Art", "Ring", "Cast", "Props")
TEXD = os.path.join(OUT, "Textures")
BLEND = os.path.join(REPO, "ArtSource", "Ring", "Cast", "props.blend")
GLB = os.path.join(REPO, "tools", "ring_preview", "data", "props.glb")
MICSRC = os.path.join(REPO, "Assets", "Art", "Ring", "Props", "mic.fbx")
TOK = json.load(open(os.path.join(REPO, "docs", "ring", "design", "brand_tokens.json"), encoding="utf-8"))
N = 1024
T0 = time.time()


def log(*a):
    print("[props %5.1fs]" % (time.time() - T0), *a, flush=True)


def C(name, k=1.0):
    h = TOK["colors"][name].lstrip("#")
    return np.array([int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4)]) * k


def smooth(a, e0, e1):
    t = np.clip((a - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def hash01(i, salt=0):
    x = (int(i) * 2654435761 + salt * 40503 + 1013904223) & 0xFFFFFFFF
    x ^= x >> 15
    x = (x * 2246822519) & 0xFFFFFFFF
    x ^= x >> 13
    return (x & 0xFFFFFF) / float(0x1000000)


# ============================================================ шум і малювання (періодичні, тому плитки бесшовні)

def pnoise(h, w, rng, slope=1.5, kv=1.0):
    """Періодичний фрактальний шум (нульове середнє, одиничне σ). kv>1 витягує риски вздовж осі 0 (V)."""
    fy = np.fft.fftfreq(h)[:, None] * kv
    fx = np.fft.fftfreq(w)[None, :]
    f = np.sqrt(fx * fx + fy * fy)
    f[0, 0] = 1.0
    spec = (rng.normal(size=(h, w)) + 1j * rng.normal(size=(h, w))) / f ** slope
    spec[0, 0] = 0.0
    n = np.fft.ifft2(spec).real
    return (n - n.mean()) / (n.std() + 1e-9)


def boxblur(a, r):
    if r < 1:
        return a
    k = 2 * r + 1
    out = a
    for ax in (0, 1):
        p = np.pad(out, [(r, r) if i == ax else (0, 0) for i in range(out.ndim)], mode="edge")
        c = np.cumsum(p, axis=ax, dtype=np.float64)
        z = np.zeros_like(np.take(c, [0], axis=ax))
        c = np.concatenate([z, c], axis=ax)
        hi = np.take(c, range(k, c.shape[ax]), axis=ax)
        lo = np.take(c, range(0, c.shape[ax] - k), axis=ax)
        out = (hi - lo) / k
    return out


def scratches(h, w, rng, n, lmin, lmax, ang=None, spread=0.5):
    """Тороїдальні подряпини: карта 0..1. ang — основний напрямок (рад), spread — розкид."""
    m = np.zeros((h, w))
    for _ in range(n):
        x, y = rng.uniform(0, w), rng.uniform(0, h)
        a = (rng.uniform(0, math.pi) if ang is None else ang + rng.normal() * spread)
        L = rng.uniform(lmin, lmax)
        t = np.arange(0, L, 0.7)
        xs = ((x + np.cos(a) * t) % w).astype(int)
        ys = ((y + np.sin(a) * t) % h).astype(int)
        m[ys, xs] = np.maximum(m[ys, xs], rng.uniform(0.35, 1.0))
    return boxblur(m, 1) * 2.2


def dashes(h, w, y0, thick, period, duty, x_from=0, x_to=None):
    """Строчка стібків: маска (h,w) зі штрихами вздовж осі U на рядках y0..y0+thick."""
    m = np.zeros((h, w))
    x_to = w if x_to is None else x_to
    xs = np.arange(w)
    on = ((xs % period) < period * duty) & (xs >= x_from) & (xs < x_to)
    for k in range(thick):
        r = y0 + k
        if 0 <= r < h:
            m[r, on] = 1.0 if 0 < k < thick - 1 or thick < 3 else 0.6
    return boxblur(m, 1) * 1.6


# ============================================================ малярі матеріалів: повертають словник карт регіону
# a — albedo sRGB (h,w,3); r — шорсткість; m — метал; hg — рельєф у мм; aw/rw/mw — стан «затерто»; wk — множник затертості.

def P_steel(h, w, ppm, rng, tint=(0.60, 0.62, 0.66), rust=0.0, grime=0.25, rough0=0.36, streak=14.0, scr=140):
    g = pnoise(h, w, rng, 0.75, kv=streak)
    fine = pnoise(h, w, rng, 0.5)
    mott = pnoise(h, w, rng, 1.8)
    tint = np.array(tint)
    a = tint[None, None, :] * (1 + 0.045 * g[..., None] + 0.025 * fine[..., None])
    dirt = np.clip(0.35 + 0.14 * mott, 0, 1) * grime
    a = a * (1 - dirt[..., None])
    S = scratches(h, w, rng, scr, 14, 70, ang=math.pi / 2, spread=0.45)
    a = a + (S[..., None] * 0.16)
    r = rough0 + 0.07 * g + 0.10 * np.clip(mott, -1, 2) * 0.5 + 0.05 * S * -1
    mt = np.full((h, w), 0.96)
    hg = 0.020 * g + 0.012 * fine - 0.05 * S
    if rust > 0:
        rm = smooth(mott * 0.7 + 0.8 * pnoise(h, w, rng, 1.0) - 1.25 + rust * 1.6, 0.0, 0.8)
        rc = np.array([0.33, 0.17, 0.085]) * (1 + 0.25 * fine[..., None])
        a = a * (1 - rm[..., None]) + rc * rm[..., None]
        r = r * (1 - rm) + (0.78 + 0.1 * fine) * rm
        mt = mt * (1 - rm) + 0.15 * rm
        hg = hg - 0.10 * rm * (1 + 0.5 * fine)
    return dict(a=a, r=r, m=mt, hg=hg, aw=np.broadcast_to(np.array([0.80, 0.81, 0.83]), (h, w, 3)).copy(),
                rw=np.full((h, w), 0.17), mw=np.full((h, w), 1.0), wk=1.0)


def P_enamel(h, w, ppm, rng, color=(0.2, 0.2, 0.25), metal_under=(0.62, 0.63, 0.66), chips=0.5, rough0=0.42, gloss_var=0.12, peel=0.03,
             streaks=0.0):
    mott = pnoise(h, w, rng, 2.2)
    fine = pnoise(h, w, rng, 0.45)
    low = pnoise(h, w, rng, 1.2)
    col = np.array(color)
    a = col[None, None, :] * (1 + 0.07 * mott[..., None] + 0.03 * fine[..., None])
    if streaks > 0:
        st = pnoise(h, w, rng, 1.3, kv=18)
        a = a * (1 - streaks * np.clip(0.4 + 0.3 * st, 0, 1)[..., None] * 0.6)
    r = rough0 + gloss_var * low + 0.04 * fine
    mt = np.zeros((h, w))
    hg = peel * fine + peel * 0.7 * mott
    S = scratches(h, w, rng, 70, 10, 60)
    a = a + (S[..., None] * 0.12)
    cn = pnoise(h, w, rng, 1.15) * 0.85 + 0.45 * mott
    cm = smooth(cn, 2.55 - chips * 0.9, 3.05 - chips * 0.8) * (chips > 0)
    mu = np.array(metal_under)
    a = a * (1 - cm[..., None]) + (mu * (0.9 + 0.1 * fine[..., None])) * cm[..., None]
    r = r * (1 - cm) + 0.34 * cm
    mt = mt * (1 - cm) + 0.9 * cm
    hg = hg - 0.12 * cm
    return dict(a=a, r=r, m=mt, hg=hg, aw=np.broadcast_to(np.array(metal_under), (h, w, 3)).copy(),
                rw=np.full((h, w), 0.30), mw=np.full((h, w), 0.9), wk=1.0)


def P_rubber(h, w, ppm, rng, color=(0.045, 0.045, 0.05), grain=0.05):
    fine = pnoise(h, w, rng, 0.35)
    mott = pnoise(h, w, rng, 2.0)
    a = np.array(color)[None, None, :] * (1 + 0.25 * mott[..., None] + 0.1 * fine[..., None])
    S = scratches(h, w, rng, 40, 8, 40)
    a = a + S[..., None] * 0.06
    r = 0.82 + 0.08 * mott - 0.10 * S
    hg = grain * fine * 6 + 0.05 * mott
    return dict(a=a, r=r, m=np.zeros((h, w)), hg=hg, aw=np.broadcast_to(np.array([0.13, 0.12, 0.12]), (h, w, 3)).copy(),
                rw=np.full((h, w), 0.92), mw=np.zeros((h, w)), wk=1.1)


def P_leather(h, w, ppm, rng, color=(0.085, 0.05, 0.035), cell=4.0, bump=0.08, rows=(), cols=(), thread=(0.80, 0.70, 0.45),
              edge_light=0.0, period=11, duty=0.62, rough0=0.55):
    """Зерниста шкіра: комірчастий рельєф (різниця гаусіанів), строчки стібків на рядках rows/колонках cols (у пікселях)."""
    n1 = pnoise(h, w, rng, 0.0 + 0.2)
    n1 = boxblur(n1, max(1, int(cell * 0.35)))
    n2 = boxblur(n1, max(2, int(cell * 0.9)))
    dog = n1 - n2
    dog = dog / (dog.std() + 1e-9)
    cellh = smooth(dog, -0.6, 1.2)
    mott = pnoise(h, w, rng, 2.2)
    a = np.array(color)[None, None, :] * (0.75 + 0.35 * cellh[..., None] + 0.12 * mott[..., None])
    r = rough0 + 0.18 * (1 - cellh) + 0.05 * mott
    hg = bump * 10 * cellh
    thr = np.zeros((h, w))
    for y0 in rows:
        thr = np.maximum(thr, dashes(h, w, int(y0), 3, period, duty))
    for x0 in cols:
        thr = np.maximum(thr, dashes(w, h, int(x0), 3, period, duty).T)
    tcol = np.array(thread)
    a = a * (1 - thr[..., None]) + tcol * (0.85 + 0.15 * cellh[..., None]) * thr[..., None]
    hg = hg + 0.25 * thr
    # борозна біля строчки
    if len(rows) or len(cols):
        g = np.zeros((h, w))
        for y0 in rows:
            for dy in (-3, 5):
                yy = int(y0) + dy
                if 0 <= yy < h:
                    g[yy, :] = 1
        for x0 in cols:
            for dx in (-3, 5):
                xx = int(x0) + dx
                if 0 <= xx < w:
                    g[:, xx] = 1
        hg = hg - 0.10 * boxblur(g, 1) * 2
    S = scratches(h, w, rng, 60, 8, 50)
    a = a + S[..., None] * 0.045
    if edge_light > 0:
        e = np.zeros((h, w))
        e[:3, :] = 1
        e[-3:, :] = 1
        a = a + e[..., None] * edge_light
    r = r - 0.2 * S
    return dict(a=a, r=r, m=np.zeros((h, w)), hg=hg,
                aw=np.clip(np.array(color)[None, None, :] * 2.6 + 0.03, 0, 1) * np.ones((h, w, 1)), rw=np.full((h, w), 0.5),
                mw=np.zeros((h, w)), wk=1.0)


def P_bamboo(h, w, ppm, rng, slats=4):
    g = pnoise(h, w, rng, 0.8, kv=60.0)
    fib = pnoise(h, w, rng, 0.4, kv=30.0)
    mott = pnoise(h, w, rng, 2.4, kv=4.0)
    slw = w / slats
    sidx = (np.arange(w) // slw).astype(int)
    lx = (np.arange(w) % slw)
    base_l = np.array([0.80, 0.66, 0.37])
    base_d = np.array([0.66, 0.47, 0.22])
    t = np.clip(0.5 + 0.22 * g + 0.10 * mott, 0, 1)
    # кожна планка має свій відтінок
    shade = np.array([hash01(i, 5) for i in range(slats)])
    a = base_d[None, None, :] * (1 - t[..., None]) + base_l[None, None, :] * t[..., None]
    a = a * (0.9 + 0.2 * shade[sidx][None, :, None])
    a = a * (1 + 0.06 * fib[..., None])
    seam = (lx < 2.2) | (lx > slw - 2.2)
    sm = np.broadcast_to(seam[None, :], (h, w)).astype(float)
    sm = boxblur(sm, 1) * 1.6
    sm = np.clip(sm, 0, 1)
    a = a * (1 - 0.78 * sm[..., None])
    hg = 0.06 * fib + 0.03 * g - 0.22 * sm
    # вузли-кільця
    for s in range(slats):
        for k in range(2):
            y = int(hash01(s * 7 + k, 11) * h)
            hgt = 5
            band = np.zeros((h, w))
            for dy in range(hgt):
                band[(y + dy) % h, int(s * slw):int((s + 1) * slw)] = 1
            band = boxblur(band, 1)
            a = a * (1 - 0.14 * band[..., None])
            hg = hg + 0.10 * band
    r = 0.52 + 0.10 * mott - 0.1 * sm
    r = np.clip(r + 0.10 * sm, 0.3, 0.8)
    S = scratches(h, w, rng, 60, 20, 90, ang=math.pi / 2, spread=0.15)
    a = a + S[..., None] * 0.06
    return dict(a=a, r=r, m=np.zeros((h, w)), hg=hg, aw=np.broadcast_to(np.array([0.90, 0.80, 0.52]), (h, w, 3)).copy(),
                rw=np.full((h, w), 0.45), mw=np.zeros((h, w)), wk=0.8)


def P_gold(h, w, ppm, rng, tint=(0.93, 0.74, 0.24), tarnish=0.35, brushed=0.0):
    mott = pnoise(h, w, rng, 2.2)
    fine = pnoise(h, w, rng, 0.5)
    t = np.array(tint)
    a = t[None, None, :] * (1 + 0.05 * mott[..., None] + 0.025 * fine[..., None])
    tm = smooth(mott + 0.6 * pnoise(h, w, rng, 1.0), 0.8, 2.0) * tarnish
    a = a * (1 - tm[..., None] * 0.22) + np.array([0.30, 0.20, 0.06]) * (tm[..., None] * 0.14)
    S = scratches(h, w, rng, 120, 10, 70)
    a = a + S[..., None] * 0.11
    r = 0.22 + 0.10 * mott + 0.35 * tm - 0.10 * S
    mt = np.full((h, w), 1.0) - 0.4 * tm
    hg = 0.02 * fine - 0.05 * S
    return dict(a=a, r=r, m=mt, hg=hg, aw=np.broadcast_to(np.array([0.99, 0.88, 0.50]), (h, w, 3)).copy(),
                rw=np.full((h, w), 0.12), mw=np.full((h, w), 1.0), wk=1.0)


def P_plastic(h, w, ppm, rng, color=(0.05, 0.05, 0.065), grain=0.07, rough0=0.52):
    fine = pnoise(h, w, rng, 0.4)
    mott = pnoise(h, w, rng, 2.2)
    a = np.array(color)[None, None, :] * (1 + 0.15 * mott[..., None])
    S = scratches(h, w, rng, 90, 8, 50)
    a = a + S[..., None] * 0.10
    r = rough0 + 0.08 * mott + 0.12 * fine * 0.5 - 0.18 * S
    hg = grain * 3 * fine
    return dict(a=a, r=r, m=np.zeros((h, w)), hg=hg,
                aw=np.clip(np.array(color)[None, None, :] * 1.5 + 0.075, 0, 1) * np.ones((h, w, 1)), rw=np.full((h, w), 0.62),
                mw=np.zeros((h, w)), wk=1.0)


def star_poly(cx, cy, R, r, n=8, rot=math.pi / 2):
    pts = []
    for i in range(2 * n):
        a = rot + math.pi * i / n
        rr = R if i % 2 == 0 else r
        pts.append((cx + rr * math.cos(a), cy + rr * math.sin(a)))
    return pts


def in_poly(X, Y, pts):
    """Маска точок усередині багатокутника (парність перетинів), векторизовано."""
    inside = np.zeros(X.shape, bool)
    n = len(pts)
    j = n - 1
    for i in range(n):
        xi, yi = pts[i]
        xj, yj = pts[j]
        cond = ((yi > Y) != (yj > Y)) & (X < (xj - xi) * (Y - yi) / (yj - yi + 1e-12) + xi)
        inside ^= cond
        j = i
    return inside


def edge_dist(X, Y, pts):
    """Відстань до ламаної замкненого багатокутника (мін. по відрізках)."""
    best = np.full(X.shape, 1e9)
    n = len(pts)
    for i in range(n):
        x0, y0 = pts[i]
        x1, y1 = pts[(i + 1) % n]
        dx, dy = x1 - x0, y1 - y0
        L2 = dx * dx + dy * dy + 1e-12
        t = np.clip(((X - x0) * dx + (Y - y0) * dy) / L2, 0, 1)
        d = np.hypot(X - (x0 + t * dx), Y - (y0 + t * dy))
        best = np.minimum(best, d)
    return best


def P_belt_center(h, w, ppm, rng, hz=0.12, hx=0.10, star_R=0.058, disc_R=0.074, ring_R=0.083, **kw):
    """Центральна пластина: u ← z (−hz..hz), v ← x (−hx..hx). Усе малюється за світовими координатами,
    тому емаль і зірка точно лягають на геометрію."""
    d = P_gold(h, w, ppm, rng, tarnish=0.4)
    zz = ((np.arange(w) + 0.5) / ppm - hz)[None, :] * np.ones((h, 1))
    xx = ((np.arange(h) + 0.5) / ppm - hx)[:, None] * np.ones((1, w))
    rr = np.hypot(zz, xx)
    ang = np.arctan2(xx, zz)
    # гравіювання «сонце» між кільцем і краєм
    sun = (0.5 + 0.5 * np.cos(ang * 48)) * smooth(rr, ring_R + 0.002, ring_R + 0.006)
    d["hg"] = d["hg"] + 0.07 * sun - 0.06 * (1 - smooth(np.abs(rr - (ring_R + 0.011)), 0.0, 0.0015))
    d["a"] = d["a"] * (1 - 0.10 * sun[..., None])
    # емалевий диск
    disc = 1 - smooth(rr, disc_R - 0.0008, disc_R)
    ink = C("ink0") * (1.0 + 0.5 * pnoise(h, w, rng, 2.4)[..., None] * 0.08)
    d["a"] = d["a"] * (1 - disc[..., None]) + ink * disc[..., None]
    d["r"] = d["r"] * (1 - disc) + 0.16 * disc
    d["m"] = d["m"] * (1 - disc)
    d["aw"] = d["aw"] * (1 - disc[..., None]) + np.array([0.28, 0.27, 0.32]) * disc[..., None]
    # золоті риски (з печатки), під зіркою
    for off in (-0.0255, 0.0, 0.0255):
        ln = (np.abs(xx - off) < 0.0012) & (np.abs(zz) < disc_R * 0.88)
        ln = boxblur(ln.astype(float), 1)
        gc = C("gold") * 0.98
        d["a"] = d["a"] * (1 - ln[..., None] * disc[..., None]) + gc * (ln * disc)[..., None]
        d["m"] = d["m"] + 0.9 * ln * disc
        d["r"] = d["r"] * (1 - ln * disc) + 0.25 * ln * disc
    # зірка (8 променів): кармін, темна кромка
    pts = star_poly(0.0, 0.0, star_R, star_R * 0.4545)
    st = in_poly(zz, xx, pts).astype(float)
    dist = edge_dist(zz, xx, pts)
    stb = boxblur(st, 1)
    rim = np.clip(1 - dist / 0.0016, 0, 1) * stb
    crim = C("crimson") * 1.05
    d["a"] = d["a"] * (1 - stb[..., None]) + crim * stb[..., None]
    d["a"] = d["a"] * (1 - 0.6 * rim[..., None])
    d["r"] = d["r"] * (1 - stb) + 0.20 * stb
    d["m"] = d["m"] * (1 - stb)
    d["aw"] = d["aw"] * (1 - stb[..., None]) + C("crimson")[None, None, :] * 1.1 * stb[..., None]
    # золотий центр
    cc = 1 - smooth(rr, 0.0118, 0.0124)
    d["a"] = d["a"] * (1 - cc[..., None]) + C("gold") * cc[..., None]
    d["r"] = d["r"] * (1 - cc) + 0.2 * cc
    d["m"] = d["m"] * (1 - cc) + 1.0 * cc
    return d


def P_belt_side(h, w, ppm, rng, hz=0.05, hx=0.0375, **kw):
    """Бічна пластина: золото з кармінною емаллю-картушем і малою золотою зіркою."""
    d = P_gold(h, w, ppm, rng, tarnish=0.4)
    zz = ((np.arange(w) + 0.5) / ppm - hz)[None, :] * np.ones((h, 1))
    xx = ((np.arange(h) + 0.5) / ppm - hx)[:, None] * np.ones((1, w))
    # картуш: закруглений прямокутник
    q = (np.abs(zz) / (hz - 0.012)) ** 6 + (np.abs(xx) / (hx - 0.011)) ** 6
    m_ = 1 - smooth(q, 0.96, 1.0)
    crim = C("crimson")
    d["a"] = d["a"] * (1 - m_[..., None]) + crim * m_[..., None] * (1 + 0.04 * pnoise(h, w, rng, 2.0)[..., None])
    d["r"] = d["r"] * (1 - m_) + 0.2 * m_
    d["m"] = d["m"] * (1 - m_)
    d["aw"] = d["aw"] * (1 - m_[..., None]) + crim * 1.1 * m_[..., None]
    pts = star_poly(0.0, 0.0, 0.0165, 0.0075)
    st = boxblur(in_poly(zz, xx, pts).astype(float), 1)
    d["a"] = d["a"] * (1 - st[..., None]) + C("gold") * st[..., None]
    d["m"] = d["m"] * (1 - st) + st
    d["r"] = d["r"] * (1 - st) + 0.22 * st
    return d


def P_badge(h, w, ppm, rng, R=0.0225, **kw):
    """Круглий значок на кейсі (печатка без слів): чорна емаль, золотий обідок, кармінна зірка. Центр — центр регіону."""
    d = P_gold(h, w, ppm, rng, tarnish=0.25)
    c = (np.arange(w) + 0.5) / ppm - w / ppm / 2
    zz = c[None, :] * np.ones((h, 1))
    xx = ((np.arange(h) + 0.5) / ppm - h / ppm / 2)[:, None] * np.ones((1, w))
    rr = np.hypot(zz, xx)
    disc = 1 - smooth(rr, R * 0.80, R * 0.82)
    d["a"] = d["a"] * (1 - disc[..., None]) + C("ink0") * disc[..., None]
    d["r"] = d["r"] * (1 - disc) + 0.18 * disc
    d["m"] = d["m"] * (1 - disc)
    pts = star_poly(0.0, 0.0, R * 0.62, R * 0.62 * 0.4545)
    st = boxblur(in_poly(zz, xx, pts).astype(float), 1)
    d["a"] = d["a"] * (1 - st[..., None]) + C("crimson") * 1.05 * st[..., None]
    d["m"] = d["m"] * (1 - st)
    d["r"] = d["r"] * (1 - st) + 0.2 * st
    cc = 1 - smooth(rr, R * 0.11, R * 0.13)
    d["a"] = d["a"] * (1 - cc[..., None]) + C("gold") * cc[..., None]
    d["m"] = d["m"] * (1 - cc) + cc
    return d


def P_hazard(h, w, ppm, rng, period=0.034, **kw):
    """Діагональні смуги попередження: золото й чорнуватий чорнильний, потерті. u — по колу бака, 12 періодів."""
    mott = pnoise(h, w, rng, 2.0)
    fine = pnoise(h, w, rng, 0.45)
    X, Y = np.meshgrid(np.arange(w), np.arange(h))
    per = w / 8.0
    ph = ((X + Y) % per) / per
    st = smooth(ph, 0.46, 0.5) - smooth(ph, 0.96, 1.0)
    g = C("gold") * 0.95
    k = C("ink1") * 1.2
    a = g[None, None, :] * st[..., None] + k[None, None, :] * (1 - st[..., None])
    a = a * (1 + 0.08 * mott[..., None])
    S = scratches(h, w, rng, 110, 12, 60)
    a = a + S[..., None] * 0.14
    r = 0.42 + 0.12 * mott - 0.12 * S
    hg = 0.03 * fine
    return dict(a=a, r=r, m=np.zeros((h, w)), hg=hg, aw=np.broadcast_to(np.array([0.60, 0.60, 0.62]), (h, w, 3)).copy(),
                rw=np.full((h, w), 0.32), mw=np.full((h, w), 0.9), wk=1.0)


def P_dial(h, w, ppm, rng, R=0.0165, **kw):
    """Циферблат манометра без цифр: чорне поле, поділки по дузі 270°, кармінна зона, стрілка, золотий центр."""
    c = (np.arange(w) + 0.5) / ppm - w / ppm / 2
    zz = c[None, :] * np.ones((h, 1))
    xx = ((np.arange(h) + 0.5) / ppm - h / ppm / 2)[:, None] * np.ones((1, w))
    rr = np.hypot(zz, xx)
    ang = np.arctan2(xx, zz)
    face = np.broadcast_to(C("paper")[None, None, :] * 0.93, (h, w, 3)).copy()
    mott = pnoise(h, w, rng, 2.0)
    face = face * (1 + 0.03 * mott[..., None])
    deg = (np.degrees(ang) + 360) % 360
    # шкала від 225° за годинниковою до -45° (дуга 270°)
    t = ((225 - deg) % 360) / 270.0
    onarc = (t >= 0) & (t <= 1.0) & (rr > R * 0.62) & (rr < R * 0.92)
    for i in range(0, 11):
        da = np.abs(((t * 10.0) - i))
        major = (i % 5 == 0)
        wid = 0.06 if major else 0.035
        mk = onarc & (da < wid) & (rr > R * (0.60 if major else 0.72))
        face[mk] = C("ink0") * 1.1
    zone = (t > 0.78) & (t <= 1.0) & (rr > R * 0.50) & (rr < R * 0.60)
    face[zone] = C("crimson")
    # стрілка на 0.62
    na = math.radians(225 - 0.62 * 270.0)
    dx, dy = math.cos(na), math.sin(na)
    pr = zz * dx + xx * dy
    pe = np.abs(-zz * dy + xx * dx)
    nd = (pr > -R * 0.15) & (pr < R * 0.74) & (pe < R * 0.045 * (1 - 0.7 * np.clip(pr / (R * 0.74), 0, 1)))
    face[nd] = C("signal")
    cc = rr < R * 0.10
    face[cc] = C("ink0")
    inside = rr < R * 0.97
    bez = (rr >= R * 0.94) & (rr < R)
    a = np.broadcast_to(np.array([0.55, 0.57, 0.6]), (h, w, 3)).copy()
    a[inside] = face[inside]
    a[bez] = np.array([0.7, 0.72, 0.75])
    r = np.full((h, w), 0.5)
    r[inside] = 0.28
    m = np.zeros((h, w))
    m[~inside] = 1.0
    hg = np.zeros((h, w))
    hg[bez] = 0.15
    return dict(a=a, r=r, m=m, hg=hg, aw=a.copy(), rw=r.copy(), mw=m.copy(), wk=0.2)


# ============================================================ атлас

class Region:
    def __init__(s, name, x0, y0, x1, y1, m, painter, seed, **kw):
        s.name, s.x0, s.y0, s.x1, s.y1, s.m = name, x0, y0, x1, y1, m
        s.painter, s.seed, s.kw = painter, seed, kw
        s.w, s.h = x1 - x0, y1 - y0
        s.ppm = s.w / m

    def uv(s, a, b, inset=1.5):
        return ((s.x0 + inset + a * (s.w - 2 * inset)) / N, (s.y0 + inset + b * (s.h - 2 * inset)) / N)


class Atlas:
    def __init__(s, regions):
        s.reg = {r.name: r for r in regions}
        s.a = np.full((N, N, 3), 0.25)
        s.r = np.full((N, N), 0.6)
        s.m = np.zeros((N, N))
        s.hg = np.zeros((N, N))
        s.aw = np.full((N, N, 3), 0.5)
        s.rw = np.full((N, N), 0.4)
        s.mw = np.zeros((N, N))
        s.wk = np.ones((N, N))
        s.pxs = np.full((N, N), 1.0)          # пікселів на мм: поправка нахилу нормалі
        for r in regions:
            rng = np.random.default_rng(r.seed)
            d = r.painter(r.h, r.w, r.ppm, rng, **r.kw)
            sl = (slice(r.y0, r.y1), slice(r.x0, r.x1))
            s.a[sl] = np.clip(d["a"], 0, 1)
            s.r[sl] = np.clip(d["r"], 0.04, 1)
            s.m[sl] = np.clip(d["m"], 0, 1)
            s.hg[sl] = d["hg"]
            s.aw[sl] = np.clip(d["aw"], 0, 1)
            s.rw[sl] = d["rw"]
            s.mw[sl] = d["mw"]
            s.wk[sl] = d["wk"]
            s.pxs[sl] = r.ppm / 1000.0


# ============================================================ геометрія: проста структура сіток

class Mesh:
    def __init__(s):
        s.V, s.F, s.UV, s.BEV = [], [], [], []

    def add(s, V, F, UV, BEV=None):
        o = len(s.V)
        s.V.extend([tuple(v) for v in V])
        for i, f in enumerate(F):
            uvi = UV[i]
            packed = isinstance(uvi, tuple) and uvi[0] == "pack"
            pts = uvi[2] if packed else uvi
            f2, u2 = [], []
            for k, idx in enumerate(f):
                if f2 and f2[-1] == idx + o:
                    continue
                f2.append(idx + o)
                u2.append(pts[k])
            if len(f2) > 1 and f2[0] == f2[-1]:
                f2.pop()
                u2.pop()
            if len(f2) < 3:
                continue
            s.F.append(tuple(f2))
            s.UV.append(("pack", uvi[1], u2) if packed else u2)
            s.BEV.append(bool(BEV[i]) if BEV else False)


def face_normal(P):
    n = Vector((0, 0, 0))
    for i in range(len(P)):
        a, b = P[i], P[(i + 1) % len(P)]
        n += Vector(((a.y - b.y) * (a.z + b.z), (a.z - b.z) * (a.x + b.x), (a.x - b.x) * (a.y + b.y)))
    return n.normalized() if n.length > 1e-12 else Vector((0, 0, 1))


def box_uv(region, m=None, grain=2, salt=0):
    """Кубічна проєкція у локальних осях частини; координати в метрах — розкладку в регіоні робить пакувальник (pack_uvs),
    тому жодні дві грані не перекриваються в UV (інакше запечені AO й затертість брешуть)."""
    def fn(P, idx):
        n = face_normal(P)
        ax = max(range(3), key=lambda k: abs(n[k]))
        pair = [k for k in range(3) if k != ax]
        if grain in pair:
            vax = grain
            uax = [k for k in pair if k != grain][0]
        else:
            uax, vax = pair
        return ("pack", region, [(p[uax], p[vax]) for p in P])
    return fn


def planar_uv(region, origin, ex, ey, su, sv, mirror_u=False):
    """Прозора (декальна) проєкція: u=(p−o)·ex*su, v=(p−o)·ey*sv — без відступів, точне накладання малюнка."""
    ex, ey, origin = Vector(ex), Vector(ey), Vector(origin)

    def fn(P, idx):
        out = []
        for p in P:
            q = p - origin
            a = q.dot(ex) * su
            if mirror_u:
                a = abs(q.dot(ex)) * su
            out.append(region.uv(a, q.dot(ey) * sv, inset=0.5))
        return out
    return fn


def apply_uv(V, F, fn):
    UV = []
    for i, f in enumerate(F):
        UV.append(fn([Vector(V[k]) for k in f], i))
    return UV


def ring_circle(c, u, v, segs):
    c, u, v = Vector(c), Vector(u), Vector(v)
    return [c + u * math.cos(2 * math.pi * j / segs) + v * math.sin(2 * math.pi * j / segs) for j in range(segs)]


def basis(axis):
    """Ортонормований базис (e1, e2) для осі, e1×e2 = вісь."""
    a = Vector(axis).normalized()
    ref = Vector((0, 0, 1)) if abs(a.z) < 0.9 else Vector((1, 0, 0))
    e1 = ref.cross(a).normalized()
    e2 = a.cross(e1).normalized()
    # e1×e2 = a ?
    if e1.cross(e2).dot(a) < 0:
        e2 = -e2
    return e1, e2


def loft(rings, closed=False, caps=(False, False), poles=None):
    """rings — список кілець (список точок однакової довжини) або однієї точки-полюса (Vector). Повертає V, F, ring_index_of_face."""
    V = []
    idx = []
    segs = max(len(r) for r in rings if not isinstance(r, Vector))
    for r in rings:
        if isinstance(r, Vector):
            V.append(r)
            idx.append([len(V) - 1] * segs)
        else:
            idx.append(list(range(len(V), len(V) + len(r))))
            V.extend(r)
    F, FR = [], []
    nr = len(rings)
    pairs = [(i, i + 1) for i in range(nr - 1)] + ([(nr - 1, 0)] if closed else [])
    for (i, k) in pairs:
        for j in range(segs):
            j1 = (j + 1) % segs
            F.append((idx[i][j], idx[i][j1], idx[k][j1], idx[k][j]))
            FR.append(i)
    if caps[0]:
        F.append(tuple(reversed(idx[0])))
        FR.append(-1)
    if caps[1]:
        F.append(tuple(idx[-1]))
        FR.append(-2)
    return V, F, FR


def revolve(profile, c, axis, segs, closed=False):
    """Тіло обертання. profile — [(r, h)] проти годинникової стрілки для суцільного тіла (низ→край→верх).
    Повертає V, F, ring_indices, (e1,e2)."""
    e1, e2 = basis(axis)
    a = Vector(axis).normalized()
    c = Vector(c)
    rings = []
    for (r, h) in profile:
        if r < 1e-7:
            rings.append(c + a * h)
        else:
            rings.append(ring_circle(c + a * h, e1 * r, e2 * r, segs))
    V, F, FR = loft(rings, closed=closed)
    return V, F, FR


def cyl_uv(region, V, F, FR, profile_len, segs, turns=1.0, vm=None, salt=0, rmax=None):
    """Суцільна (без відступів) розгортка: u — кут (turns обертів = ширина регіону), v — накопичена довжина вздовж профілю / vm.
    Якщо довжина більша за vm, v стискається. Для поверхонь, що єдині у своєму регіоні."""
    vm = vm or region.m
    tot = profile_len[-1]
    k = min(1.0, 0.98 / (tot / vm)) if tot > 0 else 1.0
    UV = []
    for fi, f in enumerate(F):
        i = FR[fi]
        if i < 0:
            UV.append(None)
            continue
        j = fi % segs
        u0, u1 = j / segs * turns, (j + 1) / segs * turns
        v0, v1 = profile_len[i] * k / vm, profile_len[i + 1] * k / vm
        UV.append([region.uv(u0, v0), region.uv(u1, v0), region.uv(u1, v1), region.uv(u0, v1)])
    return UV


def cyl_pack_uv(region, F, FR, profile_len, rad, segs):
    """Розгортка в метрах (для пакувальника): трапеція кожної грані за радіусами кілець rad[i]."""
    UV = []
    for fi, f in enumerate(F):
        i = FR[fi]
        if i < 0:
            UV.append(None)
            continue
        ai = math.pi * rad[i] / segs
        ak = math.pi * rad[i + 1] / segs
        v0, v1 = profile_len[i], profile_len[i + 1]
        UV.append(("pack", region, [(-ai, v0), (ai, v0), (ak, v1), (-ak, v1)]))
    return UV


def cap_uv(region, V, f, cen, e1, e2, m):
    return ("pack", region, [((Vector(V[k]) - cen).dot(e1), (Vector(V[k]) - cen).dot(e2)) for k in f])


def poly_len(pts):
    L = [0.0]
    for i in range(1, len(pts)):
        L.append(L[-1] + (pts[i] - pts[i - 1]).length)
    return L


def part_loft_tube(M, pts, radii, segs, region, caps=(True, True), vm=None, turns=None, wear=False, salt=0):
    """Труба вздовж ламаної точок pts із радіусами radii (число чи список). Кільця перпендикулярні дотичній (мінімальне скручування).
    turns=None — розкладку робить пакувальник; turns=число — суцільна розгортка (регіон — лише для цієї труби)."""
    pts = [Vector(p) for p in pts]
    n = len(pts)
    if not isinstance(radii, (list, tuple)):
        radii = [radii] * n
    tang = [(pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]).normalized() for i in range(n)]
    e1, e2 = basis(tang[0])
    rings = []
    prev = tang[0]
    for i in range(n):
        t = tang[i]
        axis = prev.cross(t)
        if axis.length > 1e-7:
            ang = math.asin(max(-1, min(1, axis.length)))
            rot = Matrix.Rotation(ang, 3, axis.normalized())
            e1 = rot @ e1
            e2 = rot @ e2
        e1 = (e1 - t * e1.dot(t)).normalized()
        e2 = t.cross(e1).normalized()
        if e1.cross(e2).dot(t) < 0:
            e2 = -e2
        rings.append(ring_circle(pts[i], e1 * radii[i], e2 * radii[i], segs))
        prev = t
    V, F, FR = loft(rings, caps=caps)
    pl = poly_len(pts)
    if turns is None:
        UV = cyl_pack_uv(region, F, FR, pl, radii, segs)
    else:
        UV = cyl_uv(region, V, F, FR, pl, segs, turns=turns, vm=vm, salt=salt)
    for fi, f in enumerate(F):
        if UV[fi] is None:
            c = pts[0] if FR[fi] == -1 else pts[-1]
            UV[fi] = cap_uv(region, V, f, c, e1, e2, 1.0)
    M.add(V, F, UV, [wear] * len(F))


def part_revolve(M, profile, c, axis, segs, region, turns=1.0, vm=None, closed=False, wear_rows=(), salt=0, cont=False):
    """Тіло обертання. cont=True — суцільна розгортка (регіон тільки для цієї частини), інакше — пакувальник."""
    V, F, FR = revolve(profile, c, axis, segs, closed=closed)
    pl = [0.0]
    for i in range(1, len(profile)):
        pl.append(pl[-1] + math.hypot(profile[i][0] - profile[i - 1][0], profile[i][1] - profile[i - 1][1]))
    if closed:
        pl.append(pl[-1] + math.hypot(profile[0][0] - profile[-1][0], profile[0][1] - profile[-1][1]))
    if cont:
        UV = cyl_uv(region, V, F, FR, pl, segs, turns=turns, vm=vm, salt=salt)
    else:
        rad = [r for (r, h) in profile] + ([profile[0][0]] if closed else [])
        UV = cyl_pack_uv(region, F, FR, pl, rad, segs)
    BEV = [FR[i] in wear_rows for i in range(len(F))]
    M.add(V, F, UV, BEV)


def bmesh_part(M, bm, uvfn, bevlayer):
    bm.verts.ensure_lookup_table()
    bm.verts.index_update()
    V = [tuple(v.co) for v in bm.verts]
    F = [tuple(v.index for v in f.verts) for f in bm.faces]
    BEV = [bool(f[bevlayer]) for f in bm.faces]
    UV = apply_uv(V, F, uvfn)
    M.add(V, F, UV, BEV)
    bm.free()


def rbox(M, center, size, region, bev=0.003, seg=2, m=None, grain=2, rot=None, wear=True, uvfn=None, salt=0):
    bm = bmesh.new()
    lay = bm.faces.layers.int.new("bev")
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * size[0], v.co.y * size[1], v.co.z * size[2]))
    if bev > 0:
        r = bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bev, segments=seg, profile=0.5, affect="EDGES", clamp_overlap=True)
        if wear:
            for f in r["faces"]:
                f[lay] = 1
    if rot is not None:
        bmesh.ops.transform(bm, matrix=Matrix(rot), verts=bm.verts)
    bmesh.ops.translate(bm, vec=Vector(center), verts=bm.verts)
    # інверсна поворотна проєкція: uv у локальних осях (до обертання) не потрібна — брати світові після зсуву мало значення
    bmesh_part(M, bm, uvfn or box_uv(region, m, grain, salt), lay)


def prism(M, poly, y0, y1, region, uvfn, bev=0.0, wear=False, seg=1, rot_axes=None):
    """Призма з багатокутника (z,x) → вісь Y від y0 до y1. poly — [(z, x)]. bev фаскує верхнє ребро."""
    bm = bmesh.new()
    lay = bm.faces.layers.int.new("bev")
    lo = [bm.verts.new((x, y0, z)) for (z, x) in poly]
    hi = [bm.verts.new((x, y1, z)) for (z, x) in poly]
    n = len(poly)
    # нормалі назовні: сторони (lo[i], lo[i+1], hi[i+1], hi[i]) — залежить від орієнтації; виправимо recalc
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((lo[i], lo[j], hi[j], hi[i]))
    bm.faces.new(lo)
    bm.faces.new(list(reversed(hi)))
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    if bev > 0:
        top_edges = [e for e in bm.edges if all(abs(v.co.y - y1) < 1e-7 for v in e.verts)]
        r = bmesh.ops.bevel(bm, geom=top_edges, offset=bev, segments=seg, profile=0.5, affect="EDGES", clamp_overlap=True)
        if wear:
            for f in r["faces"]:
                f[lay] = 1
    bmesh_part(M, bm, uvfn, lay)


def poly_ring(M, outer, inner, y0, y1, region, uvfn, wear=False):
    """Рамка між двома багатокутниками однакової кількості точок (z,x), вісь Y."""
    n = len(outer)
    V = [(x, y0, z) for (z, x) in outer] + [(x, y1, z) for (z, x) in outer] + [(x, y0, z) for (z, x) in inner] + [(x, y1, z) for (z, x) in inner]
    F = []
    for i in range(n):
        j = (i + 1) % n
        ob0, ob1, ot0, ot1 = i, j, n + i, n + j
        ib0, ib1, it0, it1 = 2 * n + i, 2 * n + j, 3 * n + i, 3 * n + j
        F.append((ob0, ob1, ot1, ot0))        # зовнішня стінка
        F.append((it0, it1, ib1, ib0))        # внутрішня стінка
        F.append((ot0, ot1, it1, it0))        # верх
        F.append((ib0, ib1, ob1, ob0))        # низ
    # орієнтація: зробимо через bmesh recalc
    bm = bmesh.new()
    lay = bm.faces.layers.int.new("bev")
    vs = [bm.verts.new(v) for v in V]
    for f in F:
        try:
            bm.faces.new([vs[k] for k in f])
        except ValueError:
            pass
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    if wear:
        for f in bm.faces:
            f[lay] = 1
    bmesh_part(M, bm, uvfn, lay)


def disc_poly(r, n=24):
    return [(r * math.cos(2 * math.pi * i / n), r * math.sin(2 * math.pi * i / n)) for i in range(n)]


def sx(poly, kz, kx):
    return [(z * kz, x * kx) for (z, x) in poly]


def octagon(hz, hx, cut):
    return [(-hz + cut, -hx), (hz - cut, -hx), (hz, -hx + cut), (hz, hx - cut), (hz - cut, hx), (-hz + cut, hx), (-hz, hx - cut), (-hz, -hx + cut)]


# ============================================================ збирання об'єкта, растеризація, AO, запікання

def pack_uvs(M, name):
    """Розкладає грані з ('pack', регіон, метри) у прямокутники регіону без перекриття (поличковий пакувальник).
    Щільність — як у регіоні (px/м); якщо не вміщається, зменшується."""
    groups = {}
    for i, u in enumerate(M.UV):
        if isinstance(u, tuple) and u[0] == "pack":
            groups.setdefault(u[1].name, []).append(i)
    for gname, idxs in groups.items():
        reg = M.UV[idxs[0]][1]
        items = []
        for i in idxs:
            pts = M.UV[i][2]
            us = [p[0] for p in pts]
            vs = [p[1] for p in pts]
            items.append((i, min(us), min(vs), max(us) - min(us), max(vs) - min(vs)))
        items.sort(key=lambda t: -t[4])
        dens = reg.ppm
        pad = 2.0
        placed = None
        for attempt in range(60):
            x, y, rowh = 0.0, 0.0, 0.0
            pl = []
            ok = True
            for (i, u0, v0, w, h) in items:
                wp, hp = w * dens + 2 * pad, h * dens + 2 * pad
                if wp > reg.w:
                    ok = False
                    break
                if x + wp > reg.w:
                    x, y, rowh = 0.0, y + rowh, 0.0
                if y + hp > reg.h:
                    ok = False
                    break
                pl.append((i, u0, v0, x + pad, y + pad))
                x += wp
                rowh = max(rowh, hp)
            if ok:
                placed = pl
                break
            dens *= 0.94
        if placed is None:
            raise RuntimeError("регіон %s/%s переповнений" % (name, gname))
        log("  пакування %s/%s: %.0f з %.0f px/м, граней %d" % (name, gname, dens, reg.ppm, len(items)))
        for (i, u0, v0, px, py) in placed:
            pts = M.UV[i][2]
            M.UV[i] = [((reg.x0 + px + (p[0] - u0) * dens) / N, (reg.y0 + py + (p[1] - v0) * dens) / N) for p in pts]


def make_object(name, M):
    pack_uvs(M, name)
    V = np.array(M.V)
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata([tuple(v) for v in V], [], M.F)
    uvl = mesh.uv_layers.new(name="UVMap")
    flat = []
    for uv in M.UV:
        for p in uv:
            flat.extend(p)
    uvl.data.foreach_set("uv", flat)
    mesh.update()
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bev = bm.faces.layers.int.new("bev")
    for f, b in zip(bm.faces, M.BEV):
        f[bev] = 1 if b else 0
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.edges.ensure_lookup_table()
    for e in bm.edges:
        if len(e.link_faces) == 2:
            e.smooth = e.calc_face_angle(0.0) < math.radians(52)
        else:
            e.smooth = True
    for f in bm.faces:
        f.smooth = True
    bmesh.ops.triangulate(bm, faces=list(bm.faces), quad_method="BEAUTY", ngon_method="EAR_CLIP")
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def mesh_arrays(obj):
    mesh = obj.data
    mesh.calc_loop_triangles()
    nl = len(mesh.loops)
    uv = np.zeros(nl * 2)
    mesh.uv_layers[0].data.foreach_get("uv", uv)
    uv = uv.reshape(-1, 2)
    co = np.zeros(len(mesh.vertices) * 3)
    mesh.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    try:
        cn = np.zeros(nl * 3)
        mesh.corner_normals.foreach_get("vector", cn)
        cn = cn.reshape(-1, 3)
    except Exception:
        mesh.calc_normals_split()
        cn = np.array([tuple(l.normal) for l in mesh.loops])
    tris = np.array([t.loops[:] for t in mesh.loop_triangles])
    tv = np.array([t.vertices[:] for t in mesh.loop_triangles])
    tpoly = np.array([t.polygon_index for t in mesh.loop_triangles])
    return co, cn, uv, tris, tv, tpoly


def raster(uvT, attrs, R):
    """Растеризує трикутники в UV-просторі. uvT (T,3,2), attrs (T,3,K). Повертає покриття (R,R) і карту (R,R,K); рядок 0 — v=0."""
    T, _, K = attrs.shape
    out = np.zeros((R, R, K), dtype=np.float32)
    cov = np.zeros((R, R), bool)
    P = uvT * R
    for t in range(T):
        p = P[t]
        x0 = int(max(0, math.floor(p[:, 0].min() - 1)))
        x1 = int(min(R - 1, math.ceil(p[:, 0].max() + 1)))
        y0 = int(max(0, math.floor(p[:, 1].min() - 1)))
        y1 = int(min(R - 1, math.ceil(p[:, 1].max() + 1)))
        if x1 < x0 or y1 < y0:
            continue
        d = (p[1, 1] - p[2, 1]) * (p[0, 0] - p[2, 0]) + (p[2, 0] - p[1, 0]) * (p[0, 1] - p[2, 1])
        if abs(d) < 1e-9:
            continue
        xs = np.arange(x0, x1 + 1) + 0.5
        ys = np.arange(y0, y1 + 1) + 0.5
        X, Y = np.meshgrid(xs, ys)
        l0 = ((p[1, 1] - p[2, 1]) * (X - p[2, 0]) + (p[2, 0] - p[1, 0]) * (Y - p[2, 1])) / d
        l1 = ((p[2, 1] - p[0, 1]) * (X - p[2, 0]) + (p[0, 0] - p[2, 0]) * (Y - p[2, 1])) / d
        l2 = 1 - l0 - l1
        eps = 0.03
        m = (l0 >= -eps) & (l1 >= -eps) & (l2 >= -eps)
        if not m.any():
            continue
        val = (l0[..., None] * attrs[t, 0] + l1[..., None] * attrs[t, 1] + l2[..., None] * attrs[t, 2])
        sub = out[y0:y1 + 1, x0:x1 + 1]
        sub[m] = val[m]
        cov[y0:y1 + 1, x0:x1 + 1] |= m
    return cov, out


def dilate(arr, cov, it=10):
    """Розширює заповнені пікселі на сусідні (бордюр проти швів mip)."""
    arr = arr.copy()
    cov = cov.copy()
    for _ in range(it):
        acc = np.zeros_like(arr)
        cnt = np.zeros(cov.shape)
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)):
            c2 = np.roll(np.roll(cov, dy, 0), dx, 1)
            a2 = np.roll(np.roll(arr, dy, 0), dx, 1)
            acc += a2 * c2[..., None] if arr.ndim == 3 else a2 * c2
            cnt += c2
        new = (~cov) & (cnt > 0)
        if not new.any():
            break
        if arr.ndim == 3:
            arr[new] = acc[new] / cnt[new][:, None]
        else:
            arr[new] = acc[new] / cnt[new]
        cov = cov | new
    return arr


def bake_ao(obj, cov512, pos512, nrm512, rays=14, dist=0.12):
    mesh = obj.data
    co = np.zeros(len(mesh.vertices) * 3)
    mesh.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    mesh.calc_loop_triangles()
    tv = [tuple(t.vertices) for t in mesh.loop_triangles]
    bvh = BVHTree.FromPolygons([tuple(c) for c in co], tv)
    ys, xs = np.nonzero(cov512)
    ao = np.ones(cov512.shape, dtype=np.float32)
    k = np.arange(rays) + 0.5
    cth = np.sqrt(1 - k / rays)
    sth = np.sqrt(k / rays)
    phi = k * 2.399963
    lx, ly, lz = sth * np.cos(phi), sth * np.sin(phi), cth
    for y, x in zip(ys, xs):
        n = nrm512[y, x]
        ln = np.linalg.norm(n)
        if ln < 1e-6:
            continue
        n = n / ln
        hlp = np.array([0, 0, 1.0]) if abs(n[2]) < 0.9 else np.array([1.0, 0, 0])
        t = np.cross(hlp, n)
        t /= np.linalg.norm(t)
        b = np.cross(n, t)
        o = Vector(pos512[y, x] + n * 0.0007)
        dirs = lx[:, None] * t[None, :] + ly[:, None] * b[None, :] + lz[:, None] * n[None, :]
        acc = 0.0
        for d in dirs:
            hit = bvh.ray_cast(o, Vector(d), dist)
            if hit[0] is not None:
                acc += 1.0 - (hit[3] / dist) * 0.7
        ao[y, x] = 1.0 - acc / rays
    return ao


def save_png(path, arr):
    h, w = arr.shape[:2]
    if arr.ndim == 2:
        arr = np.stack([arr] * 3, -1)
    rgba = np.ones((h, w, 4), dtype=np.float32)
    rgba[..., :3] = np.clip(arr, 0, 1)
    img = bpy.data.images.new(os.path.basename(path) + "_tmp", width=w, height=h, alpha=False)
    img.colorspace_settings.name = "Non-Color"
    img.pixels.foreach_set(rgba.ravel())
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)


def bake_textures(name, atlas, obj):
    co, cn, uv, tris, tv, tpoly = mesh_arrays(obj)
    mesh = obj.data
    T = len(tris)
    uvT = uv[tris]                           # (T,3,2)
    bev_flag = np.array([mesh.attributes["bev"].data[p].value if "bev" in mesh.attributes else 0 for p in tpoly], dtype=np.float32) \
        if "bev" in mesh.attributes else np.zeros(T, np.float32)
    wear_attr = np.repeat(bev_flag[:, None, None], 3, axis=1)
    cov, wmap = raster(uvT, wear_attr, N)
    wear_raw = wmap[..., 0]
    # AO на 512
    R2 = 512
    posT = co[tv]
    nrmT = cn[tris]
    attrs = np.concatenate([posT, nrmT], axis=2).astype(np.float32)
    cov2, m2 = raster(uvT, attrs, R2)
    t0 = time.time()
    ao2 = bake_ao(obj, cov2, m2[..., :3], m2[..., 3:6])
    log(name, "AO за %.1f с, текселів %d" % (time.time() - t0, int(cov2.sum())))
    ao = np.repeat(np.repeat(ao2, 2, 0), 2, 1)
    ao = boxblur(ao, 1)
    ao = dilate(ao[..., None], cov, 12)[..., 0]
    wear_raw = dilate(wear_raw[..., None], cov, 12)[..., 0]
    # затертість: м'яка маска від фасок + шум
    edge = boxblur(boxblur(boxblur(wear_raw, 2), 2), 1)
    rng = np.random.default_rng(1234)
    nz = pnoise(N, N, rng, 1.3)
    w = smooth(edge * 1.55 + 0.32 * nz * np.clip(edge * 4, 0, 1) - 0.10, 0.28, 0.62) * np.clip(atlas.wk, 0, 2)
    w = np.clip(w, 0, 0.92)
    cav = np.clip((1 - ao), 0, 1)
    alb = atlas.a * (1 - w[..., None]) + atlas.aw * w[..., None]
    alb = alb * (1 - 0.55 * cav[..., None] ** 1.3)
    rough = atlas.r * (1 - w) + atlas.rw * w
    rough = np.clip(rough + 0.12 * cav, 0.04, 1)
    metal = atlas.m * (1 - w) + atlas.mw * w
    hg = atlas.hg - 0.10 * w * (1.0 + 0.3 * nz)
    # нормаль із рельєфу (мм) з урахуванням щільності пікселів
    hg = boxblur(hg, 0) if False else hg
    gy, gx = np.gradient(hg)
    sxn = -gx * atlas.pxs * 1.0
    syn = -gy * atlas.pxs * 1.0
    nrm = np.stack([sxn, syn, np.ones_like(sxn)], -1)
    nrm /= np.linalg.norm(nrm, axis=-1, keepdims=True)
    nrm = nrm * 0.5 + 0.5
    # заповнення без покриття (відступи)
    alb = dilate(alb, cov, 12)
    nrm_d = dilate(nrm, cov, 12)
    rough_d = dilate(rough[..., None], cov, 12)[..., 0]
    metal_d = dilate(metal[..., None], cov, 12)[..., 0]
    ao_d = ao
    orm = np.stack([np.clip(ao_d, 0, 1), rough_d, metal_d], -1)
    os.makedirs(TEXD, exist_ok=True)
    save_png(os.path.join(TEXD, name + "_albedo.png"), alb)
    save_png(os.path.join(TEXD, name + "_normal.png"), nrm_d)
    save_png(os.path.join(TEXD, name + "_orm.png"), orm)
    return dict(albedo="Textures/%s_albedo.png" % name, normal="Textures/%s_normal.png" % name, orm="Textures/%s_orm.png" % name)


def make_material(name):
    mat = bpy.data.materials.new("Prop_%s_mat" % name)
    mat.use_nodes = True
    nt = mat.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])

    def tex(fn, cs):
        img = bpy.data.images.load(os.path.join(TEXD, name + fn), check_existing=False)
        img.colorspace_settings.name = cs
        n = nt.nodes.new("ShaderNodeTexImage")
        n.image = img
        return n
    ta = tex("_albedo.png", "sRGB")
    tn = tex("_normal.png", "Non-Color")
    to = tex("_orm.png", "Non-Color")
    nm = nt.nodes.new("ShaderNodeNormalMap")
    sep = nt.nodes.new("ShaderNodeSeparateColor")
    nt.links.new(ta.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(tn.outputs["Color"], nm.inputs["Color"])
    nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
    nt.links.new(to.outputs["Color"], sep.inputs["Color"])
    nt.links.new(sep.outputs["Green"], bsdf.inputs["Roughness"])
    nt.links.new(sep.outputs["Blue"], bsdf.inputs["Metallic"])
    # AO для glTF — через групу «glTF Material Output»
    grp = bpy.data.node_groups.get("glTF Material Output")
    if grp is None:
        grp = bpy.data.node_groups.new("glTF Material Output", "ShaderNodeTree")
        grp.interface.new_socket("Occlusion", in_out="INPUT", socket_type="NodeSocketFloat")
    g = nt.nodes.new("ShaderNodeGroup")
    g.node_tree = grp
    nt.links.new(sep.outputs["Red"], g.inputs["Occlusion"])
    return mat


# ============================================================ предмети

# --------------------------------------------------------- пояс чемпіона
def build_belt():
    regs = [
        Region("strap", 0, 874, 1024, 1024, 1.25 / 2 * 1.0, P_leather, 11, color=(0.075, 0.045, 0.032), cell=3.5, rows=(14, 136), period=12,
               thread=tuple(C("gold") * 0.95), edge_light=0.0),
        Region("plateC", 0, 0, 600, 500, 0.24, P_belt_center, 12),
        Region("plateS", 610, 0, 860, 190, 0.10, P_belt_side, 13),
        Region("gold", 620, 200, 1020, 600, 0.4, P_gold, 14),
    ]
    A = Atlas(regs)
    R = A.reg
    M = Mesh()
    L = 1.25
    W = 0.085
    T = 0.008
    NS = 48
    # --- ремінь: суперелліпс у перетині, 48 сегментів уздовж +Z (центральна вершина z=0)
    npts = 10
    ring_pts = []
    for j in range(npts):
        a = 2 * math.pi * j / npts + math.pi / npts
        c, s = math.cos(a), math.sin(a)
        ex = 0.25
        x = W / 2 * math.copysign(abs(c) ** ex, c)
        y = T / 2 * math.copysign(abs(s) ** ex, s)
        ring_pts.append((x, y))
    rings = []
    for k in range(NS + 1):
        z = -L / 2 + L * k / NS
        rings.append([Vector((x, y, z)) for (x, y) in ring_pts])
    V, F, FR = loft(rings, caps=(True, True))
    # ориентація: кільце йде проти годинникової, якщо дивитись з +Z? перевіримо recalc нижче (bmesh)
    reg = R["strap"]

    def strap_uv(P, idx):
        n = face_normal(P)
        out = []
        for p in P:
            u = abs(p.z) / (L / 2)
            if abs(n.y) > 0.55:
                out.append(reg.uv(u, p.x / W + 0.5, inset=0.5))
            else:
                out.append(reg.uv(u, 0.5, inset=0.5))
        return out
    UV = apply_uv(V, F, strap_uv)
    M.add(V, F, UV, [False] * len(F))
    # --- пластини: низ пластини над ремінем (y = T/2 + 0.0002), щоб відрізнити вершини пластин від ременя
    y0 = T / 2 + 0.0002
    rC, rS, rG = R["plateC"], R["plateS"], R["gold"]
    side_g = box_uv(rG, 0.4, grain=2)

    def decal_or_box(decal):
        def fn(P, idx):
            return decal(P, idx) if face_normal(P).y > 0.9 else side_g(P, idx)
        return fn

    hz, hx = 0.12, 0.10
    dcpl = planar_uv(rC, (-hx, 0, -hz), (0, 0, 1), (1, 0, 0), 1 / 0.24, 1 / 0.20)
    dfn = decal_or_box(dcpl)
    prism(M, octagon(hz, hx, 0.035), y0, y0 + 0.0042, rG, dfn, bev=0.0012, wear=True, seg=1)
    poly_ring(M, octagon(hz - 0.0015, hx - 0.0015, 0.034), octagon(hz - 0.012, hx - 0.012, 0.032), y0 + 0.0042, y0 + 0.0068, rG, dfn, wear=True)
    nseg = 24
    ring_prof = [(0.0742, 0.0), (0.0832, 0.0), (0.0832, 0.0033), (0.0742, 0.0033)]
    Vr, Fr, _ = revolve(ring_prof, (0, y0 + 0.0042, 0), (0, 1, 0), nseg, closed=True)
    M.add(Vr, Fr, apply_uv(Vr, Fr, dfn), [True] * len(Fr))
    disc_prof = [(0.0, 0.0), (0.0745, 0.0), (0.0745, 0.0014), (0.0, 0.0014)]
    Vd, Fd, _ = revolve(disc_prof, (0, y0 + 0.0042, 0), (0, 1, 0), nseg)
    M.add(Vd, Fd, apply_uv(Vd, Fd, dfn), [False] * len(Fd))
    star = star_poly(0, 0, 0.058, 0.058 * 0.4545)
    prism(M, star, y0 + 0.0054, y0 + 0.0072, rG, dfn, bev=0.0004, wear=True)
    dome = [(0.0, 0.0), (0.0125, 0.0), (0.0118, 0.0022), (0.0085, 0.0038), (0.0, 0.0042)]
    Vc, Fc, _ = revolve(dome, (0, y0 + 0.0072, 0), (0, 1, 0), 20)
    M.add(Vc, Fc, apply_uv(Vc, Fc, dfn), [True] * len(Fc))
    for (zz, xx) in [(-0.105, -0.075), (-0.105, 0.075), (0.105, -0.075), (0.105, 0.075), (0.0, -0.093), (0.0, 0.093), (-0.058, -0.093), (-0.058, 0.093), (0.058, -0.093), (0.058, 0.093)]:
        rivet(M, (xx, y0 + 0.0042, zz), 0.0044, rG)
    for sgn in (-1, 1):
        for zc0 in (0.2, 0.322):
            zc = sgn * zc0
            sp = planar_uv(rS, (-0.0375, 0, zc - 0.05), (0, 0, 1), (1, 0, 0), 1 / 0.10, 1 / 0.075)
            fn = decal_or_box(sp)
            prism(M, [(z + zc, x) for (z, x) in octagon(0.05, 0.0375, 0.014)], y0, y0 + 0.0040, rG, fn, bev=0.0010, wear=True, seg=1)
            outr = [(z + zc, x) for (z, x) in octagon(0.05 - 0.0012, 0.0375 - 0.0012, 0.0135)]
            innr = [(z + zc, x) for (z, x) in octagon(0.05 - 0.010, 0.0375 - 0.010, 0.012)]
            poly_ring(M, outr, innr, y0 + 0.0040, y0 + 0.0060, rG, fn, wear=True)
            rivet(M, (0.0, y0 + 0.0040, zc - 0.037), 0.0032, rG)
            rivet(M, (0.0, y0 + 0.0040, zc + 0.037), 0.0032, rG)
    return M, A, dict(
        grip="Прямий уздовж +Z; початок — центр центральної пластини; верх ременя й пластин дивиться у +Y (внутрішній бік — −Y).",
        extra=dict(belt_straight=True, length_m=L, strap_width_m=W, strap_thickness_m=T, strap_segments=NS,
                   strap_y_range=[-T / 2, T / 2], plate_vertex_rule="вершина належить пластині, якщо y > %.4f" % (T / 2 + 0.0001),
                   plates=[dict(id="center", z=0.0, length_m=0.24, width_m=0.20),
                           dict(id="L1", z=-0.20, length_m=0.10, width_m=0.075), dict(id="L2", z=-0.322, length_m=0.10, width_m=0.075),
                           dict(id="R1", z=0.20, length_m=0.10, width_m=0.075), dict(id="R2", z=0.322, length_m=0.10, width_m=0.075)]))


def rivet(M, c, r, region):
    prof = [(0.0, 0.0), (r, 0.0), (r * 0.82, r * 0.55), (0.0, r * 0.8)]
    V, F, FR = revolve(prof, c, (0, 1, 0), 6)
    fn = box_uv(region, 0.4)
    M.add(V, F, apply_uv(V, F, fn), [True] * len(F))


# --------------------------------------------------------- допоміжне: перенос у «грип-кадр» зі стільця/кейса/бака (природний Z вгору)
def to_grip_zup(V, origin):
    """природні (x ширина, y товщина, z вгору) → контракт: x_g=−(y−oy), y_g=−(z−oz), z_g=x−ox."""
    out = []
    for (x, y, z) in V:
        out.append((-(y - origin[1]), -(z - origin[2]), x - origin[0]))
    return out


def transform_mesh(M, origin):
    M.V = to_grip_zup(M.V, origin)
    # повороти зберігають орієнтацію (det=+1), грані не змінюються


# --------------------------------------------------------- спільні допоміжні
def bend(pts, n=3):
    """Згладжена ламана (Catmull–Rom), n точок на сегмент."""
    P = [Vector(p) for p in pts]
    P2 = [P[0]] + P + [P[-1]]
    out = []
    for i in range(1, len(P2) - 2):
        for t in np.linspace(0, 1, n, endpoint=False):
            a, b, c, d = P2[i - 1], P2[i], P2[i + 1], P2[i + 2]
            out.append(0.5 * ((2 * b) + (-a + c) * t + (2 * a - 5 * b + 4 * c - d) * t * t + (-a + 3 * b - 3 * c + d) * t ** 3))
    out.append(P2[-2])
    return out


# --------------------------------------------------------- стілець
def build_chair():
    regs = [
        Region("steel", 0, 512, 768, 1024, 0.75, P_steel, 21, rust=0.22, grime=0.30, scr=170),
        Region("enamel", 0, 0, 1024, 512, 1.0, P_enamel, 22, color=tuple(C("ink3") * 1.05), chips=0.65, rough0=0.40),
        Region("rubber", 768, 768, 1024, 1024, 0.25, P_rubber, 23),
        Region("brass", 768, 512, 1024, 768, 0.25, P_steel, 24, tint=(0.62, 0.62, 0.64), rust=0.0, grime=0.5, rough0=0.3, scr=60),
    ]
    A = Atlas(regs)
    R = A.reg
    M = Mesh()
    xt, yr, yf, zt, r = 0.205, 0.011, 0.034, 0.829, 0.0105
    # задня рама: ноги й стійки спинки — одна труба з верхньою поперечиною (її центр — хват)
    frame = [(-xt, yr, 0.026), (-xt, yr, 0.30), (-xt, yr + 0.003, 0.62), (-xt, yr + 0.005, 0.790), (-xt + 0.016, yr + 0.005, zt - 0.003),
             (-0.150, yr + 0.005, zt), (0.0, yr + 0.005, zt), (0.150, yr + 0.005, zt), (xt - 0.016, yr + 0.005, zt - 0.003),
             (xt, yr + 0.005, 0.790), (xt, yr + 0.003, 0.62), (xt, yr, 0.30), (xt, yr, 0.026)]
    part_loft_tube(M, bend(frame, 3), r, 10, R["steel"], vm=0.5, salt=3)
    part_loft_tube(M, [(-xt, yr, 0.12), (0, yr, 0.12), (xt, yr, 0.12)], r * 0.85, 8, R["steel"], salt=5)
    part_loft_tube(M, [(-xt, yr + 0.004, 0.55), (0, yr + 0.004, 0.55), (xt, yr + 0.004, 0.55)], r * 0.8, 8, R["steel"], salt=6)
    for s in (-1, 1):
        leg = [(s * xt, yf, 0.026), (s * xt, yf, 0.25), (s * xt, yf + 0.001, 0.45), (s * xt, yf + 0.003, 0.50)]
        part_loft_tube(M, bend(leg, 2), r, 10, R["steel"], salt=7 + s)
    part_loft_tube(M, [(-xt, yf, 0.15), (0, yf, 0.15), (xt, yf, 0.15)], r * 0.85, 8, R["steel"], salt=9)
    # гумові ковпачки ніг
    cap = [(0.0, 0.0), (0.0128, 0.0), (0.0134, 0.005), (0.0130, 0.026), (0.0, 0.0315)]
    for s in (-1, 1):
        for y in (yr, yf):
            part_revolve(M, cap, (s * xt, y, 0.0), (0, 0, 1), 10, R["rubber"], turns=1.0, vm=0.06, wear_rows=(1,), salt=s + 2)
    # заклепки шарнірів (зовнішній бік боковин)
    hr = [(0.0, 0.0), (0.0085, 0.0), (0.0068, 0.0046), (0.0, 0.0058)]
    for s in (-1, 1):
        for y in (yf, yr + 0.002):
            part_revolve(M, hr, (s * (xt + 0.0108), y, 0.44), (s, 0, 0), 8, R["brass"], turns=1.0, vm=0.05, wear_rows=(1,), salt=4)
    # спинка і складене сидіння: штамповані сталеві піддони з відбортовкою і ребрами
    ey = yr + 0.0135
    for (zc, hz_, ribs) in ((0.65, 0.30, 3), (0.255, 0.37, 5)):
        rbox(M, (0, ey, zc), (0.33, 0.007, hz_), R["enamel"], bev=0.0035, seg=2, m=0.5, salt=11)
        for sgn in (-1, 1):
            rbox(M, (sgn * 0.1625, ey + 0.0065, zc), (0.007, 0.006, hz_ - 0.004), R["enamel"], bev=0.0, m=0.5, salt=12)
            rbox(M, (0, ey + 0.0065, zc + sgn * (hz_ / 2 - 0.0035)), (0.33, 0.006, 0.007), R["enamel"], bev=0.0, m=0.5, salt=13)
        for k in range(ribs):
            xr = -0.10 + 0.20 * k / (ribs - 1)
            rbox(M, (xr, ey + 0.0058, zc), (0.012, 0.0045, hz_ - 0.05), R["enamel"], bev=0.0, m=0.5, salt=14 + k)
    for s in (-1, 1):
        rbox(M, (s * 0.183, yr + 0.0125, 0.44), (0.030, 0.011, 0.07), R["steel"], bev=0.002, seg=1, m=0.5, salt=30)
    return M, A, dict(
        grip="Хват — центр верхньої поперечини спинки; поперечина вздовж Z, тіло стільця (складеного) вздовж +Y, до ніг.",
        extra=dict(folded=True, orientation="висить вниз від хвату: ніжки на y≈0.84, передня площина піддонів дивиться в −X"),
        xf=("zup", (0.0, yr + 0.005, zt)))


# --------------------------------------------------------- кендо-синай
def build_kendo():
    regs = [
        Region("bamboo", 0, 512, 512, 1024, 0.5, P_bamboo, 31),
        Region("leather", 512, 512, 1024, 1024, 0.10, P_leather, 32, color=(0.07, 0.04, 0.03), cell=3.6, bump=0.035, cols=(12, 192), period=10,
               thread=tuple(C("paper") * 0.85)),
        Region("cord", 0, 256, 512, 512, 0.12, P_leather, 33, color=tuple(C("paper") * 0.55), cell=2.2, bump=0.05, rough0=0.7),
        Region("rubber", 512, 256, 1024, 512, 0.20, P_rubber, 34, color=(0.055, 0.052, 0.055)),
        Region("tip", 0, 0, 512, 256, 0.10, P_leather, 35, color=(0.085, 0.05, 0.034), cell=3.4, rows=(6,), period=9),
    ]
    A = Atlas(regs)
    R = A.reg
    M = Mesh()
    z_grip = 0.09
    sleeve = [(0.0, 0.0), (0.0140, 0.0), (0.0158, 0.003), (0.0160, 0.010), (0.0156, 0.020), (0.0152, 0.12), (0.0156, 0.22), (0.0166, 0.250), (0.0170, 0.264), (0.0, 0.2645)]
    part_revolve(M, sleeve, (0, 0, 0), (0, 0, 1), 16, R["leather"], turns=0.4, vm=0.2645, salt=2, cont=True)
    tsuba = [(0.0, 0.2650), (0.0412, 0.2650), (0.0446, 0.2670), (0.0452, 0.2710), (0.0432, 0.2740), (0.0300, 0.2762), (0.0150, 0.2780), (0.0, 0.2785)]
    part_revolve(M, tsuba, (0, 0, 0), (0, 0, 1), 28, R["rubber"], turns=1.0, vm=0.20, wear_rows=(2, 3), salt=3)
    z0, z1 = 0.2780, 1.0500

    def rad(z):
        return 0.0127 - (0.0127 - 0.0108) * (z - z0) / (z1 - z0)
    zs = np.linspace(z0, z1, 10)
    part_loft_tube(M, [(0, 0, z) for z in zs], [rad(z) for z in zs], 16, R["bamboo"], caps=(True, False), vm=0.5, turns=1.0, salt=4)
    zn = 0.78
    nk = [(0.0, zn - 0.012), (rad(zn) + 0.0011, zn - 0.012), (rad(zn) + 0.0016, zn - 0.008), (rad(zn) + 0.0016, zn + 0.008), (rad(zn) + 0.0011, zn + 0.012), (0.0, zn + 0.012)]
    part_revolve(M, nk, (0, 0, 0), (0, 0, 1), 16, R["tip"], salt=5)
    zt0 = 1.0400
    rt = rad(1.04) + 0.0013
    capp = [(0.0, zt0), (rt, zt0), (rt + 0.0004, zt0 + 0.012), (rt + 0.0002, zt0 + 0.07), (rt - 0.002, zt0 + 0.0775), (rt - 0.006, zt0 + 0.0800), (0.0, zt0 + 0.0805)]
    part_revolve(M, capp, (0, 0, 0), (0, 0, 1), 16, R["tip"], wear_rows=(4, 5), salt=6)
    zz = np.linspace(0.2785, 1.052, 12)
    cp = [(0, rad(z) + 0.0013 + (0.0020 if abs(z - zn) < 0.02 else 0.0), z) for z in zz]
    part_loft_tube(M, cp, 0.0017, 6, R["cord"], caps=(True, True), salt=7)
    part_revolve(M, [(0.0, 0.0), (0.0045, 0.0), (0.0048, 0.004), (0.0036, 0.008), (0.0, 0.0085)], (0, rad(0.3) + 0.0013, 0.279), (0, 0, 1), 8, R["cord"], turns=1.0, vm=0.12, salt=8)
    return M, A, dict(
        grip="Хват — нижня частина шкіряного рукава (0.09 м від торця рукояті); +Z від рукояті до кінчика, шнур цуру — з боку +Y.",
        extra=dict(grip_z_from_butt_m=z_grip, length_m=1.12, tsuru_side="+Y", tip_description="кінець саківи"),
        shift=(0, 0, z_grip), tip=(0, 0, 1.12 - z_grip))


# --------------------------------------------------------- кейс
def build_case():
    regs = [
        Region("body", 0, 512, 512, 1024, 0.5, P_plastic, 41, color=(0.045, 0.045, 0.058), grain=0.07, rough0=0.58),
        Region("alu", 512, 512, 1024, 1024, 0.5, P_steel, 42, tint=(0.74, 0.76, 0.80), rust=0.0, grime=0.20, rough0=0.30, scr=210),
        Region("rubber", 0, 256, 256, 512, 0.25, P_rubber, 43),
        Region("badge", 512, 256, 768, 512, 0.0512, P_badge, 44),
        Region("dark", 256, 256, 512, 512, 0.25, P_plastic, 45, color=(0.025, 0.025, 0.03)),
        Region("lock", 768, 256, 1024, 512, 0.25, P_steel, 46, tint=(0.50, 0.51, 0.54), rust=0.0, grime=0.5, scr=100),
    ]
    A = Atlas(regs)
    R = A.reg
    M = Mesh()
    Wd, Th, Ht = 0.46, 0.11, 0.34
    zs = 0.168
    rbox(M, (0, 0, zs / 2), (Wd, Th, zs), R["body"], bev=0.011, seg=2, m=0.5, salt=1)
    rbox(M, (0, 0, zs + 0.004 + (Ht - zs - 0.004) / 2), (Wd, Th, Ht - zs - 0.004), R["body"], bev=0.011, seg=2, m=0.5, salt=2)
    rbox(M, (0, 0, zs + 0.002), (Wd - 0.014, Th - 0.014, 0.0062), R["dark"], bev=0.0, m=0.25, wear=False, salt=3)      # ущільнювач у шві
    rv = [(0.0, 0.0), (0.0042, 0.0), (0.0036, 0.0018), (0.0, 0.0022)]
    for sxn in (-1, 1):
        for cz in (0.026, Ht - 0.026):
            cx = sxn * (Wd / 2 - 0.021)
            rbox(M, (cx, 0, cz), (0.050, Th + 0.003, 0.050), R["alu"], bev=0.0045, seg=1, m=0.5, salt=4)
            for sy in (-1,):
                part_revolve(M, rv, (cx - sxn * 0.010, sy * (Th / 2 + 0.0015), cz + (0.010 if cz < 0.1 else -0.010)), (0, sy, 0), 6, R["lock"], turns=1.0, vm=0.05, salt=5)
    for sxn in (-1, 1):
        rbox(M, (sxn * 0.165, Th / 2 + 0.0025, zs + 0.002), (0.060, 0.005, 0.085), R["alu"], bev=0.0015, seg=1, m=0.5, salt=8)
        part_loft_tube(M, [(sxn * 0.165 - 0.03, Th / 2 + 0.006, zs + 0.002), (sxn * 0.165 + 0.03, Th / 2 + 0.006, zs + 0.002)], 0.0045, 8, R["lock"], salt=9)
    yf = -(Th / 2)
    for sxn in (-1, 1):
        cx = sxn * 0.150
        rbox(M, (cx, yf - 0.003, zs + 0.044), (0.046, 0.006, 0.052), R["alu"], bev=0.0022, seg=1, m=0.5, salt=10)
        rbox(M, (cx, yf - 0.003, zs - 0.032), (0.046, 0.006, 0.058), R["alu"], bev=0.0022, seg=1, m=0.5, salt=11)
        rbox(M, (cx, yf - 0.0085, zs + 0.0), (0.030, 0.006, 0.075), R["lock"], bev=0.0018, seg=1, m=0.25, salt=12)
    rbox(M, (0, yf - 0.0035, zs + 0.004), (0.060, 0.007, 0.056), R["alu"], bev=0.003, seg=1, m=0.5, salt=14)
    part_revolve(M, [(0.0, 0.0), (0.0105, 0.0), (0.0105, 0.0022), (0.0, 0.0022)], (0, yf - 0.0070, zs + 0.004), (0, -1, 0), 12, R["lock"], turns=1.0, vm=0.06, wear_rows=(1, 2), salt=15)
    rbox(M, (0, yf - 0.0094, zs + 0.004), (0.0022, 0.0010, 0.011), R["dark"], bev=0.0, m=0.25, wear=False, salt=16)
    # значок-печатка на кришці
    rb = R["badge"]
    bz = Ht - 0.056
    bdisc = [(0.0, 0.0), (0.0259, 0.0), (0.0259, 0.0018), (0.0, 0.0018)]
    Vb, Fb, _ = revolve(bdisc, (0, yf, bz), (0, -1, 0), 16)
    ytop = yf - 0.0018

    def bad_uv(P, idx):
        if all(abs(p.y - ytop) < 1e-6 for p in P):
            return [rb.uv(0.5 + (p.x) / 0.05175, 0.5 + (p.z - bz) / 0.05175, inset=0.5) for p in P]
        return [rb.uv(0.02, 0.02) for p in P]
    M.add(Vb, Fb, apply_uv(Vb, Fb, bad_uv), [False] * len(Fb))
    # ручка: два кріплення, дужка і гумовий хват
    hc = Ht + 0.056
    for sxn in (-1, 1):
        rbox(M, (sxn * 0.095, 0, Ht + 0.004), (0.050, 0.036, 0.010), R["alu"], bev=0.003, seg=1, m=0.5, salt=20)
        part_loft_tube(M, [(sxn * 0.095, 0, Ht + 0.006), (sxn * 0.092, 0, Ht + 0.040), (sxn * 0.075, 0, hc - 0.002)], 0.0065, 8, R["lock"], salt=21)
    grip = [(0.0, -0.075), (0.0140, -0.075), (0.0176, -0.064), (0.0172, 0.0), (0.0176, 0.064), (0.0140, 0.075), (0.0, 0.075)]
    part_revolve(M, grip, (0, 0, hc), (1, 0, 0), 10, R["rubber"], turns=1.0, vm=0.25, wear_rows=(1, 4), salt=23)
    part_loft_tube(M, [(-0.075, 0, hc), (0.075, 0, hc)], 0.0074, 8, R["lock"], caps=(False, False), salt=22)
    ft = [(0.0, 0.0), (0.0125, 0.0), (0.0105, 0.0070), (0.0, 0.0072)]
    for sxn in (-1, 1):
        for syn in (-1, 1):
            part_revolve(M, ft, (sxn * 0.175, syn * 0.030, -0.0065), (0, 0, 1), 6, R["rubber"], turns=1.0, vm=0.05, salt=24)
    return M, A, dict(
        grip="Хват — центр гумової накладки ручки; ручка горизонтальна вздовж Z, тіло кейса вниз уздовж +Y; передня сторона з замками дивиться в +X.",
        extra=dict(handle_length_m=0.15, orientation="замки — на стороні +X, петлі — на −X"), xf=("zup", (0.0, 0.0, hc)))


# --------------------------------------------------------- спрей: бак
def build_spray_tank():
    regs = [
        Region("paint", 0, 512, 512, 1024, 0.5, P_enamel, 51, color=tuple(C("crimson") * 0.98), chips=0.55, rough0=0.38, gloss_var=0.14, streaks=0.35),
        Region("hazard", 512, 512, 1024, 1024, 0.4, P_hazard, 52),
        Region("steel", 0, 256, 512, 512, 0.5, P_steel, 53, tint=(0.58, 0.60, 0.63), rust=0.15, grime=0.4, scr=120),
        Region("brass", 512, 256, 1024, 512, 0.5, P_gold, 54, tint=(0.74, 0.54, 0.21), tarnish=0.65),
        Region("rubber", 0, 0, 512, 256, 0.25, P_rubber, 55),
        Region("dial", 512, 0, 768, 256, 0.045, P_dial, 56),
    ]
    A = Atlas(regs)
    R = A.reg
    M = Mesh()
    segs = 18
    pr = [(0.0, 0.0), (0.050, 0.0), (0.052, 0.002), (0.052, 0.010), (0.0560, 0.020), (0.0600, 0.050),
          (0.0600, 0.205), (0.0590, 0.222), (0.0540, 0.240), (0.0450, 0.252), (0.0320, 0.258), (0.0285, 0.262), (0.0, 0.262)]
    part_revolve(M, pr, (0, 0, 0), (0, 0, 1), segs, R["paint"], turns=1.0, vm=0.30, wear_rows=(1, 2, 3, 10), salt=1, cont=True)
    # пояс із діагональними смугами
    bandp = [(0.0609, 0.092), (0.0609, 0.1400)]
    Vb, Fb, FRb = revolve(bandp, (0, 0, 0), (0, 0, 1), segs)
    rh = R["hazard"]
    UVb = []
    for fi, f in enumerate(Fb):
        i = FRb[fi]
        j = fi % segs
        b0, b1 = (bandp[i][1] - 0.092) / 0.048, (bandp[i + 1][1] - 0.092) / 0.048
        UVb.append([rh.uv(j / segs, b0), rh.uv((j + 1) / segs, b0), rh.uv((j + 1) / segs, b1), rh.uv(j / segs, b1)])
    M.add(Vb, Fb, UVb, [False] * len(Fb))
    ck = [(0.0, 0.254), (0.0340, 0.254), (0.0350, 0.2560), (0.0350, 0.2640), (0.0300, 0.2665), (0.0, 0.2665)]
    part_revolve(M, ck, (0, 0, 0), (0, 0, 1), 10, R["steel"], turns=1.0, vm=0.2, wear_rows=(1, 3), salt=2)
    vb = [(0.0, 0.2660), (0.0210, 0.2660), (0.0215, 0.2700), (0.0180, 0.2720), (0.0180, 0.2820), (0.0205, 0.2840), (0.0140, 0.2885), (0.0, 0.2885)]
    part_revolve(M, vb, (0, 0, 0), (0, 0, 1), 10, R["brass"], wear_rows=(2, 4, 5), salt=3)
    fit = [(0.0, 0.0), (0.0105, 0.0), (0.0105, 0.012), (0.0125, 0.014), (0.0125, 0.0215), (0.0105, 0.0235), (0.0105, 0.030), (0.0, 0.030)]
    part_revolve(M, fit, (0, 0.012, 0.277), (0, 1, 0), 10, R["brass"], turns=1.0, vm=0.1, wear_rows=(3, 5), salt=4)
    barb = [(0.0, 0.0), (0.0068, 0.0), (0.0082, 0.003), (0.0070, 0.006), (0.0082, 0.009), (0.0070, 0.012), (0.0, 0.012)]
    part_revolve(M, barb, (0, 0.042, 0.277), (0, 1, 0), 8, R["steel"], turns=1.0, vm=0.06, salt=5)
    hose = bend([(0, 0.052, 0.277), (0, 0.068, 0.277), (0, 0.082, 0.268), (0, 0.090, 0.246), (0, 0.092, 0.222)], 2)
    part_loft_tube(M, hose, 0.0092, 6, R["rubber"], caps=(True, True), vm=0.25, salt=6)
    gz = 0.2735
    gb = [(0.0, 0.0), (0.0190, 0.0), (0.0190, 0.0140), (0.0172, 0.0160), (0.0, 0.0160)]
    part_revolve(M, gb, (0, -0.0155, gz), (0, -1, 0), 16, R["steel"], turns=1.0, vm=0.1, wear_rows=(2, 3), salt=7)
    rd = R["dial"]
    ydial = -0.0155 - 0.0161
    Vq, Fq, _ = revolve([(0.0172, 0.0), (0.0, 0.0)], (0, ydial, gz), (0, -1, 0), 16)
    UVq = [[rd.uv(0.5 + p.x / 0.045, 0.5 + (p.z - gz) / 0.045, inset=0.5) for p in [Vector(Vq[k]) for k in f]] for f in Fq]
    M.add(Vq, Fq, UVq, [False] * len(Fq))
    hz = 0.2985
    for sgn in (-1, 1):
        part_loft_tube(M, [(sgn * 0.0345, 0, 0.262), (sgn * 0.040, 0, 0.275), (sgn * 0.046, 0, 0.288), (sgn * 0.040, 0, hz - 0.002), (sgn * 0.032, 0, hz)], 0.0072, 8, R["steel"], salt=8)
    part_loft_tube(M, [(-0.032, 0, hz), (0.032, 0, hz)], 0.0072, 8, R["steel"], caps=(False, False), salt=9)
    gr = [(0.0, -0.040), (0.0105, -0.040), (0.0120, -0.036), (0.0130, -0.020), (0.0125, 0.0), (0.0130, 0.020), (0.0120, 0.036), (0.0105, 0.040), (0.0, 0.040)]
    part_revolve(M, gr, (0, 0, hz), (1, 0, 0), 10, R["rubber"], turns=1.0, vm=0.25, wear_rows=(1, 7), salt=10)
    for sgn in (-1, 1):
        part_loft_tube(M, [(sgn * 0.052, 0.0, 0.238), (sgn * 0.044, 0.0, 0.252), (sgn * 0.0345, 0, 0.262)], 0.0050, 6, R["steel"], salt=11)
    return M, A, dict(
        grip="Хват — центр гумової накладки верхньої ручки; ручка вздовж Z, тіло бака вниз уздовж +Y.",
        extra=dict(body_diameter_m=0.12, hose_attach="штуцер із шлангом на стороні −X; манометр дивиться в +X"), xf=("zup", (0.0, 0.0, hz)))


# --------------------------------------------------------- спрей: розпилювач
def build_spray_gun():
    regs = [
        Region("body", 0, 512, 512, 1024, 0.25, P_plastic, 61, color=tuple(C("ink2") * 0.62), grain=0.03, rough0=0.46),
        Region("brass", 512, 512, 1024, 1024, 0.25, P_gold, 62, tint=(0.74, 0.54, 0.21), tarnish=0.55),
        Region("accent", 0, 256, 512, 512, 0.25, P_enamel, 63, color=tuple(C("crimson") * 0.98), chips=0.4),
        Region("rubber", 512, 256, 1024, 512, 0.25, P_rubber, 64),
        Region("steel", 0, 0, 512, 256, 0.25, P_steel, 65, tint=(0.62, 0.64, 0.68), rust=0.0, grime=0.35, scr=90),
    ]
    A = Atlas(regs)
    R = A.reg
    M = Mesh()
    zb = 0.072
    prof = [(-0.048, 0.0150, 0.0185, -0.002), (-0.040, 0.0170, 0.0210, -0.001), (-0.020, 0.0175, 0.0225, 0.001), (0.0, 0.0175, 0.0230, 0.004),
            (0.020, 0.0172, 0.0222, 0.006), (0.040, 0.0180, 0.0230, 0.008), (0.056, 0.0185, 0.0230, 0.010)]
    rings = [ring_circle((0, cy, z), (rx, 0, 0), (0, ry, 0), 14) for (z, rx, ry, cy) in prof]
    V, F, FR = loft(rings, caps=(True, False))
    pl = [0.0]
    for i in range(1, len(prof)):
        pl.append(pl[-1] + (prof[i][0] - prof[i - 1][0]))
    UV = cyl_pack_uv(R["body"], F, FR, pl, [(rx + ry) / 2 for (z, rx, ry, cy) in prof], 14)
    for fi, f in enumerate(F):
        if UV[fi] is None:
            UV[fi] = cap_uv(R["body"], V, f, Vector((0, 0, -0.048)), Vector((1, 0, 0)), Vector((0, 1, 0)), 1.0)
    M.add(V, F, UV, [False] * len(F))
    # гумова накладка рукояті: три рифлені бандажі
    for k in range(3):
        z = -0.030 + k * 0.026
        rr = [ring_circle((0, 0.0045, z + dz), (rx, 0, 0), (0, ry, 0), 12) for (dz, rx, ry) in ((0.0, 0.0184, 0.0232), (0.009, 0.0192, 0.0240), (0.018, 0.0184, 0.0232))]
        Vr, Fr, FRr = loft(rr, caps=(False, False))
        M.add(Vr, Fr, cyl_pack_uv(R["rubber"], Fr, FRr, [0.0, 0.009, 0.018], [0.021, 0.022, 0.021], 12), [True] * len(Fr))
    head = [(0.0, -0.040), (0.0150, -0.040), (0.0192, -0.034), (0.0200, -0.026), (0.0200, 0.030), (0.0182, 0.042), (0.0126, 0.050), (0.0108, 0.056), (0.0, 0.056)]
    part_revolve(M, head, (0, 0, zb), (0, 1, 0), 20, R["body"], turns=1.0, vm=0.12, wear_rows=(1, 2, 5, 6), salt=2)
    barrel = [(0.0, 0.054), (0.0108, 0.054), (0.0108, 0.118), (0.0122, 0.121), (0.0122, 0.124), (0.0, 0.124)]
    part_revolve(M, barrel, (0, 0, zb), (0, 1, 0), 14, R["steel"], turns=1.0, vm=0.1, wear_rows=(2, 3), salt=3)
    nozzle = [(0.0, 0.122), (0.0150, 0.122), (0.0158, 0.126), (0.0158, 0.130), (0.0148, 0.132), (0.0158, 0.134), (0.0158, 0.142), (0.0142, 0.146), (0.0090, 0.158), (0.0050, 0.164), (0.0040, 0.1655), (0.0016, 0.1655)]
    part_revolve(M, nozzle, (0, 0, zb), (0, 1, 0), 16, R["brass"], turns=1.0, vm=0.1, wear_rows=(1, 2, 4, 5, 6), salt=4)
    part_revolve(M, [(0.0, 0.0), (0.0075, 0.0), (0.0075, 0.0035), (0.0045, 0.0052), (0.0, 0.0054)], (0, -0.0485, zb), (0, -1, 0), 8, R["steel"], wear_rows=(1, 2), salt=5)
    # курок: сплющена зігнута труба (кармін)
    tp = [Vector((0, y, z)) for (y, z) in [(0.020, 0.060), (0.026, 0.046), (0.0285, 0.032), (0.0265, 0.018), (0.0220, 0.008), (0.0170, 0.003)]]
    part_loft_tube(M, tp, 0.0045, 8, R["accent"], salt=7, wear=True)
    rbox(M, (0, 0.0215, 0.062), (0.034, 0.005, 0.005), R["steel"], bev=0.0, m=0.25, salt=8)
    guard = [(0.026, 0.0545), (0.040, 0.0505), (0.050, 0.042), (0.0545, 0.030), (0.0545, 0.016), (0.050, 0.008), (0.040, 0.0055), (0.0295, 0.0052)]
    part_loft_tube(M, [(0, y, z) for (y, z) in guard], 0.0028, 6, R["body"], salt=9)
    bz = -0.052
    part_revolve(M, [(0.0, 0.0), (0.0090, 0.0), (0.0095, 0.004), (0.0090, 0.012), (0.0070, 0.014), (0.0, 0.014)], (0, -0.001, bz), (0, 0, -1), 10, R["steel"], wear_rows=(1, 2), salt=10)
    part_loft_tube(M, [(0, -0.001, bz - 0.012), (0, -0.001, bz - 0.030), (0, -0.002, bz - 0.050), (0, -0.0045, bz - 0.070)], 0.0075, 8, R["rubber"], caps=(True, True), vm=0.2, salt=11)
    return M, A, dict(
        grip="Хват — центр пістолетної рукояті; рукоять вздовж Z (вгору до ствола), сопло вперед уздовж +Y; курок — з боку +Y.",
        extra=dict(length_m=0.20, hose_stub="гумовий обрізок униз із низу рукояті (−Z)", nozzle_axis="+Y"), tip=(0.0, 0.1655, zb))


BUILDERS = [("belt", build_belt), ("chair", build_chair), ("kendo", build_kendo), ("case", build_case),
            ("spray_tank", build_spray_tank), ("spray_gun", build_spray_gun)]


def bbox(obj):
    co = np.array([tuple(v.co) for v in obj.data.vertices])
    return co.min(0), co.max(0)


def build_mic(meta, objs):
    """Наявний мікрофон: беремо LOD0 з Assets/Art/Ring/Props/mic.fbx без кабелю, початок — Mic_Grip."""
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=MICSRC)
    new = [o for o in bpy.data.objects if o not in before]
    lod0 = next(o for o in new if o.type == "MESH" and o.name.endswith("LOD0"))
    for o in new:
        if o is not lod0:
            bpy.data.objects.remove(o)
    lod0.parent = None
    lod0.matrix_world = Matrix.Identity(4)
    bm = bmesh.new()
    bm.from_mesh(lod0.data)
    cab = [i for i, m in enumerate(lod0.data.materials) if m.name.startswith("Mic_Cable")]
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.material_index in cab], context="FACES")
    for v in [v for v in bm.verts if not v.link_faces]:
        bm.verts.remove(v)
    bm.to_mesh(lod0.data)
    bm.free()
    me = lod0.data
    uvl = me.uv_layers.active
    gi = [i for i, m in enumerate(me.materials) if m.name.startswith("Mic_Grille")]
    for poly in me.polygons:
        if poly.material_index not in gi:
            continue
        cos = [me.vertices[me.loops[li].vertex_index].co for li in poly.loop_indices]
        angs = [math.atan2(c.y, c.x) if math.hypot(c.x, c.y) > 1e-6 else None for c in cos]
        ref = next(a for a in angs if a is not None)
        for k, li in enumerate(poly.loop_indices):
            a = angs[k] if angs[k] is not None else ref
            while a - ref > math.pi:
                a -= 2 * math.pi
            while a - ref < -math.pi:
                a += 2 * math.pi
            uvl.data[li].uv = ((a / (2 * math.pi) + 0.5) * 3.0, (cos[k].z - 0.07) * 14.0)
    lod0.name = "Prop_mic"
    lod0.data.name = "Prop_mic"
    # текстури решітки — поруч у Textures/
    import shutil
    os.makedirs(TEXD, exist_ok=True)
    for fn in ("mic_grille_diff.png", "mic_grille_nor.png"):
        shutil.copyfile(os.path.join(os.path.dirname(MICSRC), fn), os.path.join(TEXD, fn))
    for img in bpy.data.images:
        if img.name.startswith("mic_grille"):
            base = img.name.split(".png")[0] + ".png"
            img.filepath = os.path.join(TEXD, base)
    lo, hi = bbox(lod0)
    objs["mic"] = lod0
    meta["mic"] = dict(file="mic.fbx", node="Prop_mic", size_m=[round(float(h - l), 4) for l, h in zip(lo, hi)], bbox_min=[round(float(v), 4) for v in lo],
                       bbox_max=[round(float(v), 4) for v in hi], triangles=sum(len(p.vertices) - 2 for p in lod0.data.polygons),
                       textures=dict(grille_albedo="Textures/mic_grille_diff.png", grille_normal="Textures/mic_grille_nor.png"),
                       grip="Хват — центр рукояті (Mic_Grip у mic.fbx); рукоять вздовж Z, решітка в бік +Z; кнопка — на стороні −Y.",
                       source="Assets/Art/Ring/Props/mic.fbx (LOD0 без кабелю)", length_m=round(float(hi[2] - lo[2]), 4), tip_m=[0.0, 0.0, round(float(hi[2]), 4)])


def export_all(meta, objs):
    vl = bpy.context.view_layer
    names = list(objs.keys())
    for name in names:
        for o in bpy.data.objects:
            o.select_set(False)
        o = objs[name]
        o.select_set(True)
        vl.objects.active = o
        bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, name + ".fbx"), use_selection=True, axis_forward="-Z", axis_up="Y",
                                 apply_scale_options="FBX_SCALE_UNITS", add_leaf_bones=False, bake_anim=False, path_mode="RELATIVE",
                                 mesh_smooth_type="OFF", object_types={"MESH"}, use_mesh_modifiers=True)
    for o in bpy.data.objects:
        o.select_set(False)
    for name in names:
        objs[name].select_set(True)
    vl.objects.active = objs[names[0]]
    bpy.ops.export_scene.gltf(filepath=GLB, export_format="GLB", use_selection=True, export_image_format="WEBP", export_yup=True,
                              export_apply=True, export_texcoords=True, export_normals=True, export_materials="EXPORT", export_cameras=False, export_lights=False)
    bpy.ops.wm.save_as_mainfile(filepath=BLEND, compress=True, relative_remap=True)


def write_json(meta):
    doc = dict(
        version=1,
        grip_contract=("Початок координат предмета — центр кулака на ручці. +Z — уздовж ручки (у бік великого пальця). +Y — від хвату до основної маси предмета. "
                       "X = Y × Z. Координати — у просторі Blender props.blend (Z вгору). Експортери конвертують осі: у FBX (forward −Z, up Y) і GLB (+Y вгору) "
                       "вісь Blender Z стає Y, Y стає −Z. Пояс — виняток: прямий уздовж +Z, центр центральної пластини в початку, верх (пластини) дивиться в +Y."),
        frame_blender="Z вгору, метри",
        gltf_mapping="gltf(x, y, z) = blender(x, z, −y)",
        textures="1024², PBR: *_albedo.png (sRGB), *_normal.png (OpenGL, Y+), *_orm.png (R — AO, G — шорсткість, B — метал)",
        props=meta)
    json.dump(doc, open(os.path.join(OUT, "props.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=2)


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    os.makedirs(OUT, exist_ok=True)
    os.makedirs(os.path.dirname(BLEND), exist_ok=True)
    os.makedirs(os.path.dirname(GLB), exist_ok=True)
    meta, objs = {}, {}
    for name, fn in BUILDERS:
        if ONLY and name not in ONLY:
            continue
        t0 = time.time()
        M, A, info = fn()
        if "xf" in info:
            transform_mesh(M, info["xf"][1])
        if "shift" in info:
            sh = info["shift"]
            M.V = [(x - sh[0], y - sh[1], z - sh[2]) for (x, y, z) in M.V]
        obj = make_object("Prop_" + name, M)
        tex = bake_textures(name, A, obj)
        obj.data.materials.append(make_material(name))
        lo, hi = bbox(obj)
        tris = len(obj.data.polygons)
        d = dict(file="%s.fbx" % name, node="Prop_" + name, size_m=[round(float(h - l), 4) for l, h in zip(lo, hi)], bbox_min=[round(float(v), 4) for v in lo],
                 bbox_max=[round(float(v), 4) for v in hi], triangles=tris, textures=tex, grip=info["grip"])
        d.update(info.get("extra", {}))
        if "tip" in info:
            d["tip_m"] = [round(v, 4) for v in info["tip"]]
        meta[name] = d
        objs[name] = obj
        log(name, "готово за %.1f с: %d тр., розмір %s" % (time.time() - t0, tris, d["size_m"]))
    if not ONLY or "mic" in ONLY:
        build_mic(meta, objs)
        log("mic", meta["mic"]["size_m"], meta["mic"]["triangles"])
    order = ["belt", "chair", "kendo", "case", "mic", "spray_tank", "spray_gun"]
    meta = {k: meta[k] for k in order if k in meta}
    objs = {k: objs[k] for k in order if k in objs}
    export_all(meta, objs)
    write_json(meta)
    log("усе готово")


if __name__ == "__main__":
    main()
