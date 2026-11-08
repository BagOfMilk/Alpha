# Текстури персонажа: альбедо, нормалі, ORM для тіла й одягу, атлас волосся/очей, стани пошкоджень.
#   python tools/blender/ring_cast/cast_paint.py <репо> <id> <тека роботи> <вихід текстур> [--size 2048]
# Усе детерміновано (зерно від id). Малюємо в просторі тіла: кожен тексель знає свою точку на тілі, тож лінії чіткі, а шви UV не видно.
import json, os, sys, zlib
import numpy as np
from PIL import Image

REPO, CID, WORK, OUT = sys.argv[1:5]
SIZE = int(sys.argv[sys.argv.index("--size") + 1]) if "--size" in sys.argv else 2048
sys.path.insert(0, os.path.join(REPO, "tools", "blender", "ring_cast"))
import cast_spec as CS
import cast_shapes as SH
import cast_outfits as OF

SPEC = CS.CAST[CID]
SEED = zlib.crc32(CID.encode())
rng = np.random.default_rng(SEED)
D = np.load(os.path.join(WORK, CID, "paint.npz"))
J = json.load(open(os.path.join(WORK, CID, "paint.json"), encoding="utf-8"))
B = J["bones"]
KT = os.path.join(REPO, "Assets", "Art", "Textures", "Kit")
os.makedirs(OUT, exist_ok=True)


# райдужка за персонажем: у вихідній текстурі набору вона червона
EYE_IRIS = {"stalevyi": (0.26, 0.38, 0.50), "dzhura": (0.30, 0.17, 0.08), "baron": (0.17, 0.10, 0.05), "hammer": (0.40, 0.56, 0.70), "komet": (0.24, 0.13, 0.06), "vdova": (0.13, 0.08, 0.05), "novachka": (0.30, 0.37, 0.20), "lina": (0.26, 0.15, 0.07)}
BROW_COLOR = {"hammer": 0x5A3F22}
HAIR_COLOR = {"stalevyi": 0x4A3321, "baron": 0x1C1512, "hammer": 0xB08A55, "komet": 0x17110E, "vdova": 0x101014, "novachka": 0xA9782F, "lina": 0x241A16, "dzhura": 0x17110E}


def raster(tri_uv, attrs, size):
    """Растеризує трикутники в UV. attrs — {ім'я: масив (T,3,k) або (T,3)}. Повертає mask і {ім'я: (H,W,k)}."""
    H = W = size
    mask = np.zeros((H, W), dtype=bool)
    out = {}
    for n, a in attrs.items():
        k = a.shape[2] if a.ndim == 3 else 1
        out[n] = np.zeros((H, W, k), dtype=np.float32)
    px = tri_uv[:, :, 0] * W - 0.5
    py = (1.0 - tri_uv[:, :, 1]) * H - 0.5
    T = tri_uv.shape[0]
    x0 = np.clip(np.floor(px.min(1)).astype(int) - 1, 0, W - 1)
    x1 = np.clip(np.ceil(px.max(1)).astype(int) + 1, 0, W - 1)
    y0 = np.clip(np.floor(py.min(1)).astype(int) - 1, 0, H - 1)
    y1 = np.clip(np.ceil(py.max(1)).astype(int) + 1, 0, H - 1)
    for t in range(T):
        ax, ay, bx, by, cx, cy = px[t, 0], py[t, 0], px[t, 1], py[t, 1], px[t, 2], py[t, 2]
        d = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy)
        if abs(d) < 1e-9:
            continue
        ys, xs = np.mgrid[y0[t]:y1[t] + 1, x0[t]:x1[t] + 1]
        w0 = ((by - cy) * (xs - cx) + (cx - bx) * (ys - cy)) / d
        w1 = ((cy - ay) * (xs - cx) + (ax - cx) * (ys - cy)) / d
        w2 = 1.0 - w0 - w1
        eps = -0.02
        m = (w0 >= eps) & (w1 >= eps) & (w2 >= eps)
        if not m.any():
            continue
        yy, xx = ys[m], xs[m]
        mask[yy, xx] = True
        for n, a in attrs.items():
            v = a[t] if a.ndim == 2 else a[t]
            if a.ndim == 2:
                out[n][yy, xx, 0] = w0[m] * v[0] + w1[m] * v[1] + w2[m] * v[2]
            else:
                out[n][yy, xx] = w0[m][:, None] * v[0] + w1[m][:, None] * v[1] + w2[m][:, None] * v[2]
    return mask, out


def dilate(img, mask, iters=10):
    """Розтягує кольори за межі островів UV (щоб на швах не було чорного). img (H,W,k)."""
    img = img.copy()
    m = mask.copy()
    for _ in range(iters):
        acc = np.zeros_like(img, dtype=np.float32)
        cnt = np.zeros(m.shape, dtype=np.float32)
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            mm = np.roll(np.roll(m, dy, 0), dx, 1)
            ii = np.roll(np.roll(img, dy, 0), dx, 1)
            acc += ii * mm[..., None]
            cnt += mm
        new = (~m) & (cnt > 0)
        img[new] = acc[new] / cnt[new][:, None]
        m = m | new
    return img


def noise2(size, cell, seed):
    """Плавний шум у просторі текстури, 0..1."""
    r = np.random.default_rng(seed)
    n = max(2, int(size / cell))
    g = (r.random((n, n)) * 255).astype(np.uint8)
    im = Image.fromarray(g).resize((size, size), Image.BICUBIC)
    return np.asarray(im, dtype=np.float32) / 255.0


def height_to_normal(h, px_m):
    """Висота в метрах -> дотична нормаль OpenGL (Y угору по V). Вихід 0..1."""
    dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) / (2 * px_m)
    dy = (np.roll(h, 1, 0) - np.roll(h, -1, 0)) / (2 * px_m)   # рядок росте вниз, V — угору
    n = np.stack([-dx, -dy, np.ones_like(h)], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return n * 0.5 + 0.5


def save_rgb(a, path, q=None):
    a8 = (np.clip(a, 0, 1) * 255 + 0.5).astype(np.uint8)
    Image.fromarray(a8, "RGB").save(path)


def fabric(fab, P, x_u, seed_n):
    """Дрібний рельєф (м) і зміна кольору (множник) для тканини. P (N,3); x_u — допоміжний шум 0..1 (N,)."""
    x, y, z = P[:, 0], P[:, 1], P[:, 2]
    if fab == "lycra":
        return 0.00006 * np.sin((x + y) * 900.0) * np.sin(z * 900.0), 1.0 + 0.03 * (x_u - 0.5)
    if fab == "leather":
        g = np.sin(x * 520 + 3 * np.sin(z * 230)) * np.sin(z * 480 + 3 * np.sin(y * 260))
        return 0.00012 * g, 0.97 + 0.08 * (x_u - 0.5) + 0.02 * g
    if fab == "neoprene":
        r = np.sin(z * 620.0)
        return 0.00028 * r, 0.96 + 0.06 * (r * 0.5 + 0.5)
    if fab == "knit":
        r = np.sin(z * 760.0) * np.sin((x + y) * 760.0)
        return 0.00012 * r, 0.96 + 0.06 * (r * 0.5 + 0.5)
    if fab == "tape":
        r = np.sin(z * 1300) * 0.5 + np.sin((x + y) * 1300) * 0.5
        wr = np.sin(z * 150 + 4 * x_u) * 0.5 + 0.5
        return 0.00016 * r + 0.0003 * wr, 0.92 + 0.08 * r * 0.5 - 0.10 * (x_u > 0.68)
    if fab == "canvas":
        r = np.sin(z * 1100) * np.sin((x + y) * 1100)
        return 0.00012 * r, 0.96 + 0.05 * r
    if fab == "metal":
        return 0.00002 * np.sin(x * 2200), 1.0 + 0.02 * (x_u - 0.5)
    if fab == "hair":
        a = np.arctan2(x, -(y + 0.045))
        g = np.sin(a * 240.0 + 1.6 * np.sin(z * 110.0) + 2.0 * x_u) * 0.5 + 0.5
        return 0.00014 * g, 0.92 + 0.14 * g * (0.5 + 0.5 * x_u)
    if fab == "lace":
        r = np.sin((z + x) * 700) * np.sin((z - x) * 700)
        return 0.0002 * r, 0.9 + 0.1 * r
    return 0.0, 1.0


def paint_body():
    size = SIZE
    tri = D["tris"]
    attrs = dict(P=D["P"][tri], arm=D["arm"][tri], leg=D["leg"][tri], head=D["head"][tri])
    mask, R = raster(D["tri_uv"], attrs, size)
    ys, xs = np.nonzero(mask)
    P = R["P"][ys, xs].astype(np.float64)
    F = dict(arm=R["arm"][ys, xs, 0].astype(np.float64), leg=R["leg"][ys, xs, 0].astype(np.float64), head=R["head"][ys, xs, 0].astype(np.float64))
    G = OF.Fields(P, F, B, SPEC["sex"], soft=0.0016)
    n = len(P)
    print("texels", n, flush=True)
    # база шкіри
    sk = Image.open(os.path.join(KT, "skin_%s_%s.png" % (SPEC["sex"], SPEC["culture"]))).convert("RGB")
    sk = np.asarray(sk.resize((size, size), Image.LANCZOS), dtype=np.float32) / 255.0
    col = sk[ys, xs].astype(np.float64)
    low = noise2(size, 90, SEED + 1)[ys, xs]
    mid = noise2(size, 14, SEED + 2)[ys, xs]
    hi = noise2(size, 2.2, SEED + 3)[ys, xs]
    col *= (0.965 + 0.07 * low)[:, None]
    col *= (0.985 + 0.03 * mid)[:, None]
    # рум'янець на суглобах
    red = np.array([0.76, 0.28, 0.20])
    def near(bone, r):
        q = P - np.asarray(B[bone]["head"])
        return np.exp(-np.sum(q * q, axis=1) / (r * r))
    flush = sum(near(b, 0.05) for b in ("calf_l", "calf_r", "lowerarm_l", "lowerarm_r")) + sum(near(b, 0.045) for b in ("index_01_l", "index_01_r", "middle_01_l", "middle_01_r"))
    col = col * (1 - 0.14 * np.clip(flush, 0, 1)[:, None]) + red * col * 1.15 * 0.14 * np.clip(flush, 0, 1)[:, None]
    # рельєф
    H = SH.definition_height(P, F, B, SPEC["shape"])
    cav = np.clip(-H / 0.003, 0, 1)
    col *= (1.0 - 0.20 * cav)[:, None]
    Hdetail = 0.00006 * (hi - 0.5) + 0.00012 * (mid - 0.5)
    H = H + Hdetail
    rough = np.full(n, 0.56) - 0.05 * (low - 0.5)
    metal = np.zeros(n)
    # стерня чоловікам і лисим
    if SPEC["sex"] == "m":
        hd = np.clip(F["head"] - 0.5, 0, 0.5) * 2
        jaw = hd * OF.Fields(P, F, B, "m", soft=0.03).st(G.A["head"][2] - 0.012 - G.z) * (G.y < G.A["head"][1] + 0.015)
        col *= (1.0 - 0.22 * jaw * (0.6 + 0.4 * hi))[:, None]
        if not SPEC.get("hair"):
            scalp = hd * G.st(G.z - (G.A["head"][2] + 0.075), 0.012)
            col *= (1.0 - 0.10 * scalp * (0.7 + 0.3 * hi))[:, None]
    # скальп під зачіскою: у колір волосся, щоб крізь пасма не світилася шкіра
    if SPEC.get("hair"):
        hc = np.array(OF.rgb(HAIR_COLOR[CID]))
        hd = np.clip(F["head"] * 1.5 - 0.5, 0, 1)
        # передня межа трохи вище краю зачіски: смуги фарби на лобі не видно
        hz_line = max(G.hz + 0.072, J["face"].get("hairline_z", 0.0) + 0.007)
        scalp = np.clip(np.maximum((P[:, 2] - hz_line) / 0.008, ((P[:, 1] - (G.hy + 0.030)) / 0.012) * np.clip((P[:, 2] - (G.hz - 0.055)) / 0.015, 0, 1)), 0, 1) * hd
        col = col * (1 - (scalp * 0.92)[:, None]) + (hc * (0.8 + 0.4 * hi[:, None])) * (scalp * 0.92)[:, None]
    tone = SKIN_TONE.get(CID)
    # брови: там, де були смужки набору; у Джури їх закриває маска
    front = (P[:, 1] < G.hy - 0.015) & (F["head"] > 0.5)
    fb = J["face"].get("brow")
    if fb:
        ax = np.abs(P[:, 0])
        zc = np.interp(ax, fb["ax"], fb["z"])
        hw = np.interp(ax, fb["ax"], fb["half"])
        u = np.clip((ax - fb["ax0"]) / max(fb["ax1"] - fb["ax0"], 1e-4), 0, 1)
        # товще до перенісся, тонше до скроні
        half = np.minimum(hw, 0.0052 if SPEC["sex"] == "m" else 0.0036) * (1.0 - 0.55 * u ** 1.6) + 0.0007
        bm = np.clip((half - np.abs(P[:, 2] - zc)) / 0.0016 + 0.5, 0, 1) * np.clip((ax - fb["ax0"]) / 0.002, 0, 1) * np.clip((fb["ax1"] - ax) / 0.004, 0, 1) * front
        # волоски йдуть косо, контраст малий
        bm = bm * (0.95 + 0.05 * np.sin(P[:, 0] * 3400.0 * np.sign(P[:, 0]) + P[:, 2] * 2200.0)) * (0.90 if SPEC["sex"] == "m" else 0.80)
        bc = np.array(OF.rgb(BROW_COLOR.get(CID, HAIR_COLOR[CID]))) * 0.8
        col = col * (1 - bm[:, None]) + bc[None, :] * bm[:, None]
    # лінія повіки замість смужки вій
    for segs in J["face"].get("lids", []):
        sg = np.array(segs)
        if len(sg) < 3:
            continue
        cz = sg[:, [1, 3]].mean()
        cx = sg[:, [0, 2]].mean()
        near_eye = np.nonzero(front & (np.abs(P[:, 0] - cx) < 0.05) & (np.abs(P[:, 2] - cz) < 0.03))[0]
        if not len(near_eye):
            continue
        p = P[near_eye][:, [0, 2]]
        dmin = np.full(len(p), 1.0)
        dlow = np.full(len(p), 1.0)
        for s in sg:
            a0 = s[:2]; ab = s[2:] - s[:2]
            t = np.clip(np.sum((p - a0) * ab, axis=1) / max(np.dot(ab, ab), 1e-9), 0, 1)
            d = np.linalg.norm(p - (a0 + t[:, None] * ab), axis=1)
            if (s[1] + s[3]) * 0.5 >= cz - 0.0005:
                dmin = np.minimum(dmin, d)
            else:
                dlow = np.minimum(dlow, d)
        w_up = 0.0018 if SPEC["sex"] == "m" else 0.0024
        lm = np.maximum(np.clip((w_up - dmin) / 0.0012 + 0.5, 0, 1) * 0.86, np.clip((0.0009 - dlow) / 0.0010 + 0.5, 0, 1) * 0.28)
        lc = np.array([0.045, 0.030, 0.028])
        col[near_eye] = col[near_eye] * (1 - lm[:, None]) + lc[None, :] * lm[:, None]
    if tone:
        col = col * np.array(tone)
    # шари
    layers = OF.layers(CID, G)
    gear_mask = np.zeros(n)
    tape_mask = np.zeros(n)
    leather_mask = np.zeros(n)
    fab_seed = noise2(size, 6, SEED + 5)[ys, xs]
    for L in layers:
        m = np.clip(L["m"], 0, 1) * L.get("mix", 1.0)
        c = np.array(L["color"], dtype=np.float64)
        if L["fab"] == "ink":
            speck = 0.86 + 0.14 * hi
            col = col * (1 - (m * speck)[:, None]) + (col * c * 1.2 + c * 0.25) * (m * speck)[:, None]
            continue
        dh, mult = fabric(L["fab"], P, fab_seed, 0)
        cc = c[None, :] * (mult if np.isscalar(mult) else mult[:, None])
        col = col * (1 - m[:, None]) + cc * m[:, None]
        if L.get("rough") is not None:
            rough = rough * (1 - m) + L["rough"] * m
        metal = metal * (1 - m) + L.get("metal", 0.0) * m
        H = H * (1 - m) + (H * 0.2 + (dh if not np.isscalar(dh) else dh)) * m
        if L.get("thick"):
            H += L["thick"] * 0.12 * m
        gear_mask = np.maximum(gear_mask, m)
        if L["fab"] == "tape":
            tape_mask = np.maximum(tape_mask, m)
        if L["fab"] in ("leather", "canvas"):
            leather_mask = np.maximum(leather_mask, m)
        if L.get("seam", True):
            edge = m * (1 - m) * 4.0
            col *= (1.0 - 0.45 * edge)[:, None]
            H -= 0.0009 * edge
    skin_only = 1.0 - gear_mask
    # потертості на тейпі й взутті (база)
    scuff = np.clip((noise2(size, 18, SEED + 6)[ys, xs] - 0.56) * 6, 0, 1) * (0.5 + 0.5 * hi)
    grime = np.array([0.30, 0.27, 0.22])
    col = col * (1 - (scuff * tape_mask * 0.55)[:, None]) + grime * (scuff * tape_mask * 0.55)[:, None]
    fray = np.clip((noise2(size, 5, SEED + 7)[ys, xs] - 0.6) * 7, 0, 1)
    col = col * (1 - (fray * tape_mask * 0.25)[:, None]) + (col * 1.25) * (fray * tape_mask * 0.25)[:, None]
    H += 0.00025 * fray * tape_mask
    wear = np.clip((noise2(size, 10, SEED + 8)[ys, xs] - 0.58) * 5, 0, 1) * np.clip((G.z_ankle + 0.05 - P[:, 2]) / 0.06 + 0.4, 0, 1) * leather_mask
    col = col * (1 - (wear * 0.5)[:, None]) + (col * 0.6 + 0.28) * (wear * 0.5)[:, None]
    rough = rough + 0.25 * wear

    def assemble(c, r):
        A = np.zeros((size, size, 3), dtype=np.float32)
        A[ys, xs] = c
        return dilate(A, mask, 14)

    # стани: 0 — чистий із легким блиском, 1 — піт і синці, 2 — кров, садна, брудний тейп
    sweat_zone = np.clip(F["head"] * 0.6 + G.torso * 0.9 + F["arm"] * 0.8 + F["leg"] * 0.35, 0, 1) * skin_only
    drops = np.clip((noise2(size, 3.2, SEED + 9)[ys, xs] - 0.6) * 9, 0, 1)
    def bruise(center, r, amt):
        d2 = np.sum(((P - np.asarray(center)) / r) ** 2, axis=1)
        return np.clip(np.exp(-d2) * (0.75 + 0.5 * low) * amt, 0, 1) * skin_only
    A = G.A
    hz = A["head"][2]
    ribs = [(0.13, A["spine_03"][1] - 0.08, G.z_chest - 0.12), (-0.11, A["spine_03"][1] - 0.09, G.z_chest - 0.15)]
    br_list = [((0.042, A["head"][1] - 0.09, hz + 0.03), (0.03, 0.03, 0.025)), ((-0.05, A["head"][1] - 0.075, hz - 0.03), (0.03, 0.04, 0.03)),
               (A["upperarm_l"].tolist(), (0.07, 0.07, 0.07)), ((ribs[0]), (0.07, 0.08, 0.09)), ((ribs[1]), (0.08, 0.08, 0.08))]
    results = {}
    for lvl in (0, 1, 2):
        c = col.copy()
        r = rough.copy()
        if lvl >= 0:
            r = r - 0.06 * sweat_zone
        if lvl >= 1:
            r = r - 0.22 * sweat_zone - 0.14 * drops * sweat_zone
            flush2 = G.torso * skin_only * (0.5 + 0.5 * low)
            c = c * (1 - (0.10 * flush2)[:, None]) + (c * np.array([1.18, 0.92, 0.88])) * (0.10 * flush2)[:, None]
            b = np.zeros(n)
            for ct, rd in br_list[:3]:
                b = np.maximum(b, bruise(ct, rd, 0.55))
            pur = np.array([0.30, 0.16, 0.30])
            c = c * (1 - (b * 0.6)[:, None]) + (c * pur * 1.6) * (b * 0.6)[:, None]
            c = c * (1 - (scuff * tape_mask * 0.4)[:, None]) + grime * 0.7 * (scuff * tape_mask * 0.4)[:, None]
        if lvl >= 2:
            b = np.zeros(n)
            for ct, rd in br_list:
                b = np.maximum(b, bruise(ct, rd, 1.0))
            pur = np.array([0.24, 0.11, 0.25])
            yel = np.array([0.55, 0.50, 0.24])
            ring = np.clip(b * 2.2, 0, 1) * (1 - np.clip(b * 1.5, 0, 1))
            c = c * (1 - (b * 0.72)[:, None]) + (c * pur * 1.6) * (b * 0.72)[:, None]
            c = c * (1 - (ring * 0.3)[:, None]) + (c * yel * 1.5) * (ring * 0.3)[:, None]
            # кров: розсічення на лобі, струмок униз
            cut_x = 0.022
            top_z = hz + 0.085
            xoff = (P[:, 0] - cut_x) + 0.006 * np.sin(P[:, 2] * 90)
            fr = (P[:, 1] < A["head"][1] - 0.02) * np.clip(F["head"] * 2 - 0.8, 0, 1)
            stream = np.exp(-(xoff / 0.0055) ** 2) * np.clip((top_z - P[:, 2]) / 0.01, 0, 1) * np.clip((P[:, 2] - (hz - 0.09)) / 0.05, 0, 1)
            gash = np.exp(-((P[:, 0] - cut_x) / 0.028) ** 2 - ((P[:, 2] - top_z) / 0.005) ** 2) * 1.2
            blood = np.clip((stream * (0.65 + 0.35 * mid) + gash) * fr * (1.0 - G.st(0.0, 1)), 0, 1)
            smear = np.clip((noise2(size, 22, SEED + 10)[ys, xs] - 0.62) * 7, 0, 1) * np.clip(G.torso * (P[:, 1] < A["spine_03"][1]) + 0.0, 0, 1) * np.clip((G.z_chest + 0.1 - P[:, 2]) / 0.2, 0, 1) * 0.55
            blood = np.clip(np.maximum(blood, smear), 0, 1)
            rd = np.array([0.26, 0.02, 0.02])
            c = c * (1 - (blood * 0.86)[:, None]) + rd * (blood * 0.86)[:, None]
            r = r * (1 - blood) + 0.2 * blood
            # садна
            abr = np.clip((noise2(size, 4.0, SEED + 11)[ys, xs] - 0.64) * 8, 0, 1) * (bruise((0.17, A["pelvis"][1], G.z_chest), (0.12, 0.16, 0.2), 1.0) + bruise(A["lowerarm_l"].tolist(), (0.08, 0.08, 0.08), 1.0))
            c = c * (1 - (np.clip(abr, 0, 1) * 0.45)[:, None]) + np.array([0.46, 0.16, 0.12]) * (np.clip(abr, 0, 1) * 0.45)[:, None]
            # брудний розірваний тейп, подряпане взуття
            c = c * (1 - (np.clip(scuff + fray, 0, 1) * tape_mask * 0.55)[:, None]) + (grime * 0.45) * (np.clip(scuff + fray, 0, 1) * tape_mask * 0.55)[:, None]
            c = c * (1 - (wear * 0.5)[:, None]) + (c * 0.7 + 0.22) * (wear * 0.5)[:, None]
        albedo = assemble(c, r)
        O = np.zeros((size, size, 3), dtype=np.float32)
        O[..., 0] = 1.0
        O[ys, xs, 1] = np.clip(r, 0.05, 1.0)
        O[ys, xs, 2] = np.clip(metal, 0, 1)
        O = dilate(O, mask, 14)
        results[lvl] = (albedo, O)
    tag = ""
    save_rgb(results[0][0], os.path.join(OUT, "%s_body_albedo.png" % CID))
    save_rgb(results[0][1], os.path.join(OUT, "%s_body_orm.png" % CID))
    for lvl in (1, 2):
        save_rgb(results[lvl][0], os.path.join(OUT, "%s_body_albedo_d%d.png" % (CID, lvl)))
        save_rgb(results[lvl][1], os.path.join(OUT, "%s_body_orm_d%d.png" % (CID, lvl)))
    Hh = np.zeros((size, size), dtype=np.float32)
    Hh[ys, xs] = H
    Hh = dilate(Hh[..., None], mask, 14)[..., 0]
    nrm = height_to_normal(Hh, 1.75 * 0.55 / size)
    save_rgb(nrm, os.path.join(OUT, "%s_body_normal.png" % CID))
    print("body done", flush=True)


# Поправка тону шкіри: у наборі деякі тіла надто бліді.
SKIN_TONE = {"vdova": (0.86, 0.76, 0.66), "lina": (0.96, 0.90, 0.84), "hammer": (0.96, 0.90, 0.86)}

# Кольори одягу: pid -> (основний, кайма, шорсткість, метал каймі)
GEAR = {
    "baron": {1: (0x7B2A85, 0xD4A429, 0.34), 2: (0x7B2A85, 0xD4A429, 0.34), 3: (0xD4A429, 0x8E6A12, 0.28)},
    "vdova": {1: (0x1A1220, 0x6E4B7B, 0.36), 2: (0x1A1220, 0x6E4B7B, 0.36)},
    "novachka": {1: (0x1F4FA8, 0xE8E8E8, 0.62)},
    "lina": {1: (0x24262C, 0x3C3F48, 0.58), 2: (0x1A1C22, 0x2A2C33, 0.52)},
    "dzhura": {3: (0xD4A429, 0xE8E8E8, 0.4)},
    "komet": {3: (0xE8BA3C, 0xF2E9D2, 0.3)},
}


def paint_gear():
    size = SIZE
    if len(D["gear_tri_uv"]) == 0:
        # лише заглушка: одного пікселя досить
        Image.new("RGB", (4, 4), (128, 128, 128)).save(os.path.join(OUT, "%s_gear_albedo.png" % CID))
        Image.new("RGB", (4, 4), (128, 128, 255)).save(os.path.join(OUT, "%s_gear_normal.png" % CID))
        Image.new("RGB", (4, 4), (255, 150, 0)).save(os.path.join(OUT, "%s_gear_orm.png" % CID))
        return
    pid = np.repeat(D["gear_tri_pid"][:, None], 3, 1).astype(np.float32)
    mask, R = raster(D["gear_tri_uv"], dict(P=D["gear_tri_P"], edge=D["gear_tri_edge"], pid=pid), size)
    ys, xs = np.nonzero(mask)
    P = R["P"][ys, xs].astype(np.float64)
    e = R["edge"][ys, xs, 0].astype(np.float64)
    ids = np.rint(R["pid"][ys, xs, 0]).astype(int)
    n = len(P)
    tab = GEAR[CID]
    col = np.zeros((n, 3))
    rough = np.zeros(n)
    metal = np.zeros(n)
    H = np.zeros(n)
    low = noise2(size, 60, SEED + 21)[ys, xs]
    hi = noise2(size, 2.2, SEED + 22)[ys, xs]
    for p, (c0, c1, r0) in tab.items():
        m = ids == p
        if not m.any():
            continue
        base = np.array(OF.rgb(c0))
        trim = np.array(OF.rgb(c1))
        width = 0.028 if p != 3 else 0.0
        t = np.clip((width - e[m]) / 0.004 + 0.5, 0, 1) if width else np.zeros(m.sum())
        # складки як тінь у кольорі
        folds = 0.5 + 0.5 * np.sin(P[m, 0] * 46 + P[m, 1] * 38 + 2.0 * np.sin(P[m, 2] * 6))
        shade = 0.92 + 0.1 * folds
        cc = base[None, :] * (shade * (0.96 + 0.08 * low[m]))[:, None]
        cc = cc * (1 - t[:, None]) + trim[None, :] * t[:, None]
        # шов уздовж каймі
        seam = np.exp(-((e[m] - width) / 0.002) ** 2) if width else 0
        cc = cc * (1 - 0.35 * np.clip(seam, 0, 1))[:, None] if width else cc
        col[m] = cc
        rough[m] = r0 * (1 - t) + 0.3 * t
        metal[m] = 0.85 * t * (1 if c1 == 0xD4A429 else 0) + (0.7 if p == 3 and c0 == 0xD4A429 else 0)
        weave = np.sin(P[m, 2] * 900) * np.sin((P[m, 0] + P[m, 1]) * 900)
        H[m] = 0.0035 * folds + 0.00008 * weave
    A = np.zeros((size, size, 3), dtype=np.float32)
    A[ys, xs] = col
    A = dilate(A, mask, 12)
    save_rgb(A, os.path.join(OUT, "%s_gear_albedo.png" % CID))
    O = np.zeros((size, size, 3), dtype=np.float32)
    O[..., 0] = 1.0
    O[ys, xs, 1] = rough
    O[ys, xs, 2] = metal
    O = dilate(O, mask, 12)
    save_rgb(O, os.path.join(OUT, "%s_gear_orm.png" % CID))
    Hh = np.zeros((size, size), dtype=np.float32)
    Hh[ys, xs] = H
    Hh = dilate(Hh[..., None], mask, 12)[..., 0]
    save_rgb(height_to_normal(Hh, 1.2 / size), os.path.join(OUT, "%s_gear_normal.png" % CID))
    print("gear done", n, flush=True)


def recolor_eye(path, iris):
    """Очі набору: дві райдужки на одній текстурі. Зберігає малюнок райдужки, міняє лише колір; білок трохи чистіший."""
    a = np.asarray(Image.open(path).convert("RGB"), dtype=np.float32) / 255.0
    H, W = a.shape[:2]
    yy, xx = np.mgrid[0:H, 0:W]
    xx = (xx + 0.5) / W; yy = (yy + 0.5) / H
    out = a.copy()
    # білок: менше рожевого, світліший
    lum = a.mean(-1, keepdims=True)
    out = np.clip(lum * np.array([1.03, 1.0, 0.98]) * 1.10 * 0.65 + a * 0.35 * 1.10, 0, 1)
    tint = np.array(iris, dtype=np.float32)
    for cx, cy in ((0.707, 0.297), (0.293, 0.708)):
        d = np.hypot(xx - cx, yy - cy) / 0.118
        m = np.clip((1.0 - d) / 0.07, 0, 1)[..., None]
        det = a[..., 0] / max(float(a[..., 0][d < 1.0].max()), 1e-3)
        ring = np.clip((d - 0.86) / 0.14, 0, 1)    # темніший обідок
        pupil = np.clip((d - 0.27) / 0.05, 0, 1)
        c = tint[None, None, :] * (0.55 + 1.45 * det)[..., None] * (1.0 - 0.45 * ring)[..., None] * pupil[..., None]
        out = out * (1 - m) + np.clip(c, 0, 1) * m
    return out




def paint_alpha():
    S = 2048
    A = np.zeros((S, S, 4), dtype=np.float32)
    A[..., :3] = 0.04
    def put(rect, img):
        x0 = int(rect[0] * S); w = int(rect[2] * S)
        y1 = S - int(rect[1] * S); h = int(rect[3] * S)
        im = img.resize((w, h), Image.LANCZOS)
        a = np.asarray(im, dtype=np.float32) / 255.0
        A[y1 - h:y1, x0:x0 + w] = a
    import cast_garments_rects as RX
    col = np.array(OF.rgb(HAIR_COLOR[CID]))
    def tinted(path, tint=col, gain=1.0):
        im = Image.open(path).convert("RGBA")
        a = np.asarray(im, dtype=np.float32) / 255.0
        lum = a[..., :3].mean(-1, keepdims=True)
        lum = lum / max(float(np.percentile(lum[a[..., 3] > 0.2], 80)) if (a[..., 3] > 0.2).any() else 1.0, 1e-3)
        rgb = np.clip(tint[None, None, :] * lum * gain, 0, 1)
        out = np.concatenate([rgb, a[..., 3:]], -1)
        return Image.fromarray((out * 255).astype(np.uint8), "RGBA")
    hair = SPEC.get("hair")
    if hair:
        put(RX.RECT_HAIR, tinted(os.path.join(KT, "hairN_%s.png" % hair.replace("hair_", ""))))
    beard = SPEC.get("beard")
    if beard:
        src = {"beard_full": "hairN__full.png", "beard_short": "hairN__short.png", "moustache": "hairN_ache.png"}[beard]
        put(RX.RECT_BEARD, tinted(os.path.join(KT, src)))
    eye = recolor_eye(os.path.join(KT, "brown_eye.png"), EYE_IRIS[CID])
    ea = np.concatenate([eye, np.ones(eye.shape[:2] + (1,))], -1)
    put(RX.RECT_EYE, Image.fromarray((ea * 255).astype(np.uint8), "RGBA"))
    Image.fromarray((np.clip(A, 0, 1) * 255).astype(np.uint8), "RGBA").save(os.path.join(OUT, "%s_alpha_albedo.png" % CID))
    print("alpha done", flush=True)


paint_alpha()
paint_gear()
paint_body()
print("PAINT OK", CID, flush=True)
