# Текстури арен «Рингу» (ітерація 2, docs/TRPG_DESIGN.md §11.0.2) — власна процедурна генерація, без чужих зображень.
#
#   "C:\Program Files\Blender Foundation\Blender 4.5\blender.exe" -b --python tools/blender/ring_textures.py -- <корінь репо> [набір ...]
#
# Кожен набір — тайлова плитка (шви не видно при повторі: шум будується через FFT, тому він періодичний) у трьох
# картах: <набір>_albedo.png (колір), <набір>_normal.png (нормаль, OpenGL, Y угору — як у Unity), <набір>_rough.png
# (шорсткість). Більшість наборів — сірі «деталі», колір дає матеріал (множення на відтінок): одна текстура
# обслуговує багато матеріалів — мало пам'яті на Android. Кольорові — цегла (палітра в самій текстурі).
# Наклейки (decal_*) — RGBA з прозорістю: афіші, емблема на полотні рингу, хрести зі скотчу, бруд.
# Розміри: 2048 — підлога й полотно рингу (ключові), 1024 — решта, 512 — дрібне. Випадковість — numpy з
# фіксованим зерном: той самий запуск дає ті самі пікселі.
# Виходи: Assets/Art/Ring/Textures/*.png і textures_manifest.txt (набір, розмір, плитка в метрах).

import bpy, os, sys, math
import numpy as np

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
REPO = args[0] if args else os.getcwd()
WANT = [a for a in args[1:] if not a.startswith("--")]
OUT = os.path.join(REPO, "Assets", "Art", "Ring", "Textures")
sys.path.insert(0, os.path.join(REPO, "tools", "blender"))
sys.path.insert(0, os.path.join(REPO, "tools", "ring_design"))
import ring_brand as B              # палітра — токени арт-директора (Статут вигляду COL-01)
import emblem_geometry as G         # геометрія знака (Статут вигляду ARENA-03): одна на PIL, numpy і SVG


def _c(name):
    r, g, b = B.rgb(name)
    return np.array((r / 255.0, g / 255.0, b / 255.0), np.float32)

# ---------------------------------------------------------------- шум і допоміжне

def noise(n, period, seed, ax=1.0, ay=1.0):
    """Періодичний шум n × n зі збуреннями розміром ~period пікселів; ax/ay > 1 — витягнуто вздовж x/y.
    Середнє 0, відхилення 1."""
    rs = np.random.RandomState(seed)
    w = rs.standard_normal((n, n)).astype(np.float32)
    F = np.fft.rfft2(w)
    fy = np.fft.fftfreq(n)[:, None]
    fx = np.fft.rfftfreq(n)[None, :]
    r = np.sqrt((fx * ax) ** 2 + (fy * ay) ** 2) * period
    out = np.fft.irfft2(F * np.exp(-(r ** 2)), s=(n, n)).astype(np.float32)
    out -= out.mean()
    return out / (out.std() + 1e-8)

def fbm(n, period, seed, octaves=4, gain=0.5, ax=1.0, ay=1.0):
    acc = np.zeros((n, n), np.float32)
    amp, tot = 1.0, 0.0
    for k in range(octaves):
        acc += amp * noise(n, max(0.7, period / (2 ** k)), seed + 101 * k, ax, ay)
        tot += amp
        amp *= gain
    return acc / tot

def hash2(a, b, salt=0):
    """Детермінований хеш цілих масивів → 0..1."""
    x = (a.astype(np.int64) * 374761393 + b.astype(np.int64) * 668265263 + salt * 2246822519 + 1442695041) & 0xFFFFFFFF
    x = ((x ^ (x >> 13)) * 1274126177) & 0xFFFFFFFF
    return ((x ^ (x >> 16)) & 0xFFFF).astype(np.float32) / 65535.0

def grid(n):
    v, u = np.mgrid[0:n, 0:n].astype(np.float32)
    return u, v                                   # u — стовпець (x), v — рядок знизу вгору (y)

def normal_from_height(h, strength):
    dx = (np.roll(h, -1, axis=1) - np.roll(h, 1, axis=1)) * 0.5 * strength
    dy = (np.roll(h, -1, axis=0) - np.roll(h, 1, axis=0)) * 0.5 * strength
    nz = np.ones_like(h)
    l = np.sqrt(dx * dx + dy * dy + nz * nz)
    return np.stack([0.5 - 0.5 * dx / l, 0.5 - 0.5 * dy / l, 0.5 + 0.5 * nz / l], axis=-1)

def smooth(x, a, b):
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3 - 2 * t)

def gray(a):
    return np.repeat(a[..., None], 3, axis=-1)

def save(name, arr, alpha=None, non_color=False):
    n = arr.shape[0]
    img = bpy.data.images.new(name, n, n, alpha=alpha is not None, float_buffer=False)
    if non_color:
        img.colorspace_settings.name = 'Non-Color'
    rgba = np.ones((n, n, 4), np.float32)
    rgba[..., :3] = np.clip(arr, 0, 1)
    if alpha is not None:
        rgba[..., 3] = np.clip(alpha, 0, 1)
    img.pixels.foreach_set(rgba.ravel())
    img.filepath_raw = os.path.join(OUT, name + ".png")
    img.file_format = 'PNG'
    img.save()
    bpy.data.images.remove(img)

def emit(name, albedo, height, rough, strength):
    save(name + "_albedo", albedo)
    save(name + "_normal", normal_from_height(height, strength), non_color=True)
    save(name + "_rough", gray(rough), non_color=True)

def edge_mask(frac, width):
    """Близькість до краю клітинки (0 — край, 1 — далі за width) для дробової частини координати."""
    d = np.minimum(frac, 1.0 - frac)
    return smooth(d, 0.0, width)

# ---------------------------------------------------------------- набори: (функція, розмір, плитка в метрах)

def t_wood_strip(n):
    """Паркет спортзалу: вузькі смуги клену ~5,7 см, довжини кратні плитці, шви, волокна, лак з подряпинами."""
    u, v = grid(n)
    rows = 36
    rowf = v * rows / n
    row = np.floor(rowf)
    k = 2 + np.floor(hash2(row, row, 3) * 4)             # у ряду 2..5 дощок на плитку
    off = hash2(row, row, 5)
    pf = (u / n + off) * k
    plank = np.floor(pf)
    seam = np.minimum(edge_mask(rowf - row, 0.05), edge_mask(pf - plank, 0.004 * k))
    hv = hash2(row * 7 + 1, plank, 9)
    g1 = noise(n, 3.0, 11, ax=14.0) * 0.6 + noise(n, 1.0, 12, ax=6.0) * 0.4
    g2 = noise(n, 3.0, 13, ax=14.0) * 0.6 + noise(n, 1.0, 14, ax=6.0) * 0.4
    grain = np.where(hv > 0.5, g1, g2)
    lum = 0.86 + 0.12 * (hv - 0.5) + 0.035 * grain + 0.03 * fbm(n, 90, 15, 3)
    lum *= 0.82 + 0.18 * seam
    scratch = np.clip(noise(n, 0.8, 16, ax=40.0) * noise(n, 40, 17), 0, None)
    rough = np.clip(0.3 + 0.15 * scratch + 0.06 * fbm(n, 60, 18, 3) + 0.35 * (1 - seam), 0, 1)
    height = 0.2 * grain + 2.0 * seam
    return gray(np.clip(lum, 0, 1)), height, rough, 1.5

def t_wood_board(n):
    """Широкі дошки ~14 см (стара зала, лави, трибуни, бар): глибші волокна, знос, темніші шви."""
    u, v = grid(n)
    rows = 14
    rowf = v * rows / n
    row = np.floor(rowf)
    k = 1 + np.floor(hash2(row, row, 23) * 2)
    pf = (u / n + hash2(row, row, 25)) * k
    plank = np.floor(pf)
    seam = np.minimum(edge_mask(rowf - row, 0.035), edge_mask(pf - plank, 0.003 * k))
    hv = hash2(row * 3 + 2, plank, 29)
    grain = noise(n, 5.0, 31, ax=10.0) * 0.6 + noise(n, 1.4, 32, ax=8.0) * 0.4
    rings = np.sin((v / n * rows * 9.0 + noise(n, 20, 33, ax=4.0) * 1.5) * math.pi) * 0.5
    wear = fbm(n, 120, 34, 3)
    lum = 0.84 + 0.14 * (hv - 0.5) + 0.05 * grain + 0.04 * rings + 0.05 * wear
    lum *= 0.75 + 0.25 * seam
    rough = np.clip(0.45 + 0.12 * wear + 0.08 * grain + 0.3 * (1 - seam), 0, 1)
    height = 0.45 * grain + 0.15 * rings + 2.5 * seam
    return gray(np.clip(lum, 0, 1)), height, rough, 1.6

def t_block_wall(n):
    """Пофарбований бетонний блок 40 × 20 см, перев'язка в пів блока, заглиблені шви, пори."""
    u, v = grid(n)
    rowf = v * 8 / n
    row = np.floor(rowf)
    cf = u * 4 / n + 0.5 * (row % 2)
    blk = np.floor(cf)
    m = np.minimum(edge_mask(rowf - row, 0.05), edge_mask(cf - blk, 0.025))
    pores = np.clip(noise(n, 0.9, 41) - 1.6, 0, None)
    hv = hash2(row, blk, 43)
    lum = 0.9 + 0.04 * (hv - 0.5) + 0.03 * fbm(n, 30, 44, 3) - 0.25 * pores
    lum = lum * (0.88 + 0.12 * m)
    height = 2.0 * m - 1.2 * pores + 0.25 * fbm(n, 4, 45, 2)
    rough = np.clip(0.75 + 0.1 * (1 - m) + 0.05 * fbm(n, 20, 46, 2), 0, 1)
    return gray(np.clip(lum, 0, 1)), height, rough, 2.0

BRICKS = np.array([(0.45, 0.20, 0.13), (0.52, 0.24, 0.15), (0.38, 0.17, 0.12), (0.48, 0.27, 0.18),
                   (0.33, 0.15, 0.11), (0.56, 0.30, 0.20)], np.float32)

def t_brick(n):
    """Цегла 21,5 × 6,5 см з розчином 1 см (кольорова): палітра цеглин, сколи, розчин світліший і заглиблений."""
    u, v = grid(n)
    rowf = v * 24 / n
    row = np.floor(rowf)
    cf = u * 8 / n + 0.5 * (row % 2)
    blk = np.floor(cf)
    chip = 0.03 * fbm(n, 3, 51, 2)
    m = np.minimum(edge_mask(rowf - row + chip, 0.14), edge_mask(cf - blk + chip * 0.3, 0.045))
    hv = hash2(row, blk, 53)
    idx = np.minimum((hv * len(BRICKS)).astype(np.int32), len(BRICKS) - 1)
    col = BRICKS[idx] * (0.9 + 0.18 * fbm(n, 6, 54, 3))[..., None]
    col *= (1.0 - 0.25 * smooth(fbm(n, 200, 55, 2), 0.3, 1.5))[..., None]       # сажа плямами
    mortar = np.array((0.60, 0.58, 0.54), np.float32) * (0.9 + 0.1 * noise(n, 1.2, 56))[..., None]
    alb = col * m[..., None] + mortar * (1 - m[..., None])
    height = 2.2 * m + 0.35 * fbm(n, 2.5, 57, 2)
    rough = np.clip(0.8 + 0.1 * (1 - m) + 0.05 * noise(n, 2, 58), 0, 1)
    return alb, height, rough, 2.5

def t_concrete(n):
    u, v = grid(n)
    base = fbm(n, 160, 61, 5)
    pores = np.clip(noise(n, 0.8, 62) - 1.8, 0, None)
    stains = smooth(fbm(n, 300, 63, 3), 0.4, 1.6)
    lum = 0.9 + 0.05 * base - 0.18 * stains - 0.3 * pores
    rough = np.clip(0.82 + 0.06 * base + 0.08 * pores - 0.12 * stains, 0, 1)
    height = 0.4 * fbm(n, 5, 64, 3) - 1.2 * pores
    return gray(np.clip(lum, 0, 1)), height, rough, 1.2

def t_plaster(n):
    base = fbm(n, 90, 71, 4)
    trowel = noise(n, 25, 72, ax=3.0, ay=1.0) * 0.5 + noise(n, 25, 73, ax=1.0, ay=3.0) * 0.5
    lum = 0.93 + 0.035 * base + 0.02 * trowel
    rough = np.clip(0.85 + 0.05 * base, 0, 1)
    height = 0.6 * trowel + 0.25 * fbm(n, 2, 74, 2)
    return gray(np.clip(lum, 0, 1)), height, rough, 0.9

def t_canvas(n):
    """Полотно рингу: дрібне переплетення, знос і бруд плямами, сліди взуття (дрібні дуги шуму)."""
    u, v = grid(n)
    weave = np.sin(u * math.pi / 2.0) * np.sin(v * math.pi / 2.0)
    wear = fbm(n, 140, 81, 4)
    scuff = np.clip(noise(n, 6, 82, ax=3.0) - 1.2, 0, None) + np.clip(noise(n, 6, 83, ay=3.0) - 1.3, 0, None)
    lum = 0.93 + 0.025 * weave - 0.07 * smooth(wear, 0.2, 1.5) - 0.12 * np.clip(scuff, 0, 1)
    rough = np.clip(0.85 + 0.05 * weave - 0.1 * np.clip(scuff, 0, 1), 0, 1)
    height = 0.8 * weave + 0.3 * fbm(n, 8, 84, 2)
    return gray(np.clip(lum, 0, 1)), height, rough, 0.8

def t_vinyl(n):
    """Вініл (подушки кутів, килимки, кушетка): дрібна шкіряна зернистість, легкий блиск, потертості."""
    grain = noise(n, 1.4, 91) * 0.6 + noise(n, 4.0, 92) * 0.4
    cells = -np.abs(noise(n, 2.5, 93))
    wear = smooth(fbm(n, 80, 94, 3), 0.6, 1.8)
    lum = 0.9 + 0.03 * grain + 0.08 * wear
    rough = np.clip(0.42 + 0.08 * grain + 0.25 * wear, 0, 1)
    height = 0.5 * grain + 0.7 * cells
    return gray(np.clip(lum, 0, 1)), height, rough, 1.0

def t_cloth(n):
    """Тканина (завіси, скатерки, банери): вертикальні смуги ворсу, переплетення, пил унизу не тайлиться — без нього."""
    u, v = grid(n)
    streak = noise(n, 3.0, 101, ax=1.0, ay=18.0)
    weave = np.sin(u * math.pi / 1.5) * 0.5 + np.sin(v * math.pi / 1.5) * 0.5
    lum = 0.9 + 0.06 * streak + 0.02 * weave + 0.03 * fbm(n, 60, 102, 3)
    rough = np.clip(0.95 + 0.03 * streak, 0, 1)
    height = 0.6 * streak + 0.4 * weave
    return gray(np.clip(lum, 0, 1)), height, rough, 0.7

def t_metal_brushed(n):
    """Шліфований метал (ферми, стійки, огорожі): штрихи вздовж x, подряпини, жирні плями."""
    brush = noise(n, 0.9, 111, ax=60.0) * 0.7 + noise(n, 3.0, 112, ax=30.0) * 0.3
    scratch = np.clip(noise(n, 0.7, 113, ax=25.0, ay=0.5) - 2.0, 0, None)
    smudge = fbm(n, 100, 114, 3)
    lum = 0.88 + 0.06 * brush + 0.15 * scratch - 0.05 * smooth(smudge, 0.4, 1.6)
    rough = np.clip(0.33 + 0.06 * brush + 0.12 * smooth(smudge, 0.2, 1.6) - 0.1 * np.clip(scratch, 0, 1), 0, 1)
    height = 0.3 * brush - 0.6 * scratch
    return gray(np.clip(lum, 0, 1)), height, rough, 0.6

def t_metal_painted(n):
    """Фарбований метал (шафки, двері, рами стільців, кейси): «апельсинова шкірка», сколи до металу."""
    peel = noise(n, 3.0, 121)
    chips = smooth(noise(n, 4.0, 122) * 0.6 + noise(n, 40, 123) * 0.6, 1.5, 1.9)
    lum = 0.92 + 0.02 * peel - 0.35 * chips + 0.02 * fbm(n, 70, 124, 3)
    rough = np.clip(0.42 + 0.04 * peel + 0.25 * chips, 0, 1)
    height = 0.3 * peel - 1.5 * chips
    return gray(np.clip(lum, 0, 1)), height, rough, 0.8

def t_plastic(n):
    grain = noise(n, 1.2, 131)
    lum = 0.92 + 0.02 * grain + 0.03 * fbm(n, 40, 132, 3)
    rough = np.clip(0.55 + 0.06 * grain + 0.06 * fbm(n, 30, 133, 2), 0, 1)
    return gray(np.clip(lum, 0, 1)), 0.5 * grain, rough, 0.6

def t_carpet(n):
    fib = noise(n, 0.8, 141) * 0.7 + noise(n, 2.0, 142) * 0.3
    wear = smooth(fbm(n, 90, 143, 3), 0.3, 1.6)
    lum = 0.88 + 0.07 * fib - 0.12 * wear
    rough = np.clip(0.97 - 0.03 * wear, 0, 1)
    return gray(np.clip(lum, 0, 1)), 1.0 * fib, rough, 1.4

def t_tile_floor(n):
    """Плитка 30 × 30 см (закулісся): шви, плями на плитках, крапчастий тераццо."""
    u, v = grid(n)
    cf, rf = u * 4 / n, v * 4 / n
    m = np.minimum(edge_mask(cf - np.floor(cf), 0.012), edge_mask(rf - np.floor(rf), 0.012))
    hv = hash2(np.floor(rf), np.floor(cf), 151)
    speck = np.clip(noise(n, 1.0, 152) - 1.2, 0, None)
    lum = (0.9 + 0.06 * (hv - 0.5) - 0.12 * speck + 0.03 * fbm(n, 50, 153, 3)) * (0.7 + 0.3 * m)
    rough = np.clip(0.35 + 0.1 * fbm(n, 60, 154, 3) + 0.4 * (1 - m), 0, 1)
    return gray(np.clip(lum, 0, 1)), 2.0 * m, rough, 1.6

def t_panel_floor(n):
    """Настил арени: щити 1,2 × 2,4 м (плитка 4,8 м), шви, смуги скотчу, подряпини від кейсів."""
    u, v = grid(n)
    cf, rf = u * 4 / n, v * 2 / n
    m = np.minimum(edge_mask(cf - np.floor(cf), 0.004), edge_mask(rf - np.floor(rf), 0.002))
    hv = hash2(np.floor(rf), np.floor(cf), 161)
    scr = np.clip(noise(n, 0.8, 162, ax=30.0) - 1.6, 0, None)
    tape = ((np.abs(cf - np.floor(cf) - 0.5) < 0.012) & (hash2(np.floor(rf), np.floor(cf), 163) > 0.6)).astype(np.float32)
    lum = (0.88 + 0.06 * (hv - 0.5) + 0.03 * fbm(n, 80, 164, 3) + 0.15 * scr) * (0.75 + 0.25 * m)
    lum = lum * (1 - tape) + 0.98 * tape
    rough = np.clip(0.55 + 0.1 * fbm(n, 50, 165, 2) + 0.3 * (1 - m) - 0.15 * tape, 0, 1)
    return gray(np.clip(lum, 0, 1)), 1.6 * m + 0.4 * tape, rough, 1.2

def t_led_pixels(n):
    """Світлодіодний екран: решітка пікселів з кроком ~6 мм — маска світіння."""
    u, v = grid(n)
    pu, pv = (u % 6.0) - 2.5, (v % 6.0) - 2.5
    dot = 1.0 - smooth(np.sqrt(pu * pu + pv * pv), 1.4, 2.6)
    lum = 0.12 + 0.88 * dot
    return gray(lum), dot, np.full((n, n), 0.3, np.float32), 0.5

def t_sport_floor(n):
    """Спортивне ПВХ-покриття (як у референсі «СК на Відрадному»): дрібна крапчастість, легкі плями зносу, шви рулонів
    по 1,5 м, напівглянець з потертостями від взуття."""
    u, v = grid(n)
    speck = noise(n, 0.7, 401) * 0.5 + noise(n, 1.6, 402) * 0.5
    blot = fbm(n, 220, 403, 4)
    rf = u * 2 / n
    seam = edge_mask(rf - np.floor(rf), 0.0025)
    scuff = np.clip(noise(n, 5, 404, ax=6.0) * noise(n, 60, 405) - 0.9, 0, None)
    lum = (0.9 + 0.035 * speck + 0.04 * blot - 0.12 * np.clip(scuff, 0, 1)) * (0.9 + 0.1 * seam)
    rough = np.clip(0.42 + 0.06 * speck + 0.25 * np.clip(scuff, 0, 1) + 0.25 * (1 - seam), 0, 1)
    height = 0.25 * speck + 1.2 * seam
    return gray(np.clip(lum, 0, 1)), height, rough, 0.6

SETS = {
    "sport_floor": (t_sport_floor, 2048, 3.0),
    "wood_strip": (t_wood_strip, 2048, 2.0), "wood_board": (t_wood_board, 2048, 2.0),
    "block_wall": (t_block_wall, 1024, 1.6), "brick": (t_brick, 1024, 1.8), "concrete": (t_concrete, 1024, 3.0),
    "plaster": (t_plaster, 1024, 2.0), "canvas": (t_canvas, 2048, 2.0), "vinyl": (t_vinyl, 1024, 1.0),
    "cloth": (t_cloth, 1024, 1.0), "metal_brushed": (t_metal_brushed, 1024, 1.0),
    "metal_painted": (t_metal_painted, 1024, 1.0), "plastic": (t_plastic, 512, 0.5), "carpet": (t_carpet, 1024, 1.0),
    "tile_floor": (t_tile_floor, 1024, 1.2), "panel_floor": (t_panel_floor, 2048, 4.8), "led_pixels": (t_led_pixels, 512, 0.5),
}

# ---------------------------------------------------------------- наклейки (RGBA, власний дизайн, без брендів)

PALETTES = [((0.08, 0.08, 0.1), (0.85, 0.12, 0.1), (0.95, 0.8, 0.2)), ((0.95, 0.92, 0.85), (0.1, 0.1, 0.12), (0.8, 0.1, 0.12)),
            ((0.1, 0.18, 0.45), (0.95, 0.95, 0.95), (0.95, 0.6, 0.1)), ((0.2, 0.05, 0.08), (0.95, 0.85, 0.6), (0.3, 0.75, 0.8))]

def d_poster(n, k):
    """Афіша шоу: тло, блок заголовка, силует борця (коло голови + тулуб), рядки «тексту» смугами, рамка."""
    bg, fg, acc = (np.array(c, np.float32) for c in PALETTES[k % len(PALETTES)])
    u, v = grid(n)
    x, y = u / n, v / n
    img = np.ones((n, n, 3), np.float32) * bg
    img *= (0.9 + 0.1 * fbm(n, 60, 201 + k, 3))[..., None]
    def put(mask, col):
        img[mask] = col
    put((y > 0.78) & (y < 0.93) & (x > 0.08) & (x < 0.92), acc)
    for j in range(3):                                       # «букви» заголовка — блоки
        for i in range(6):
            x0 = 0.12 + i * 0.13
            put((y > 0.81) & (y < 0.9) & (x > x0) & (x < x0 + 0.09) & ((i + j + k) % 5 != 0), fg)
    cx = 0.5 + 0.08 * ((k % 3) - 1)
    head = (x - cx) ** 2 + ((y - 0.62) * 1.0) ** 2 < 0.07 ** 2
    torso = (np.abs(x - cx) < 0.17 - (0.55 - y) * 0.25) & (y > 0.3) & (y < 0.55)
    arms = (np.abs(y - 0.5 - 0.1 * np.abs(x - cx)) < 0.03) & (np.abs(x - cx) < 0.3)
    put(head | torso | arms, fg * 0.25 + bg * 0.1)
    for j in range(4):
        w = 0.6 - 0.1 * j
        put((np.abs(y - (0.22 - 0.045 * j)) < 0.012) & (np.abs(x - 0.5) < w / 2), fg)
    border = (x < 0.03) | (x > 0.97) | (y < 0.02) | (y > 0.98)
    put(border, acc)
    alpha = np.ones((n, n), np.float32)
    return img, alpha

def seal_layers(n, scale=1.0):
    """Печатка «Рингу» шарами (колір, альфа): золоте кільце, чорне поле, три канати, зірка з обідком, цятка.
    БЕЗ планки (стара планка читалась як «в'їзд заборонено»). scale < 1 — менша печатка в кадрі. Геометрія — emblem_geometry."""
    u, v = grid(n)
    x = (u / n * 2 - 1) * 100.0 / scale
    y = (v / n * 2 - 1) * 100.0 / scale
    r = np.sqrt(x * x + y * y)
    a = np.arctan2(y, x)
    col = np.zeros((n, n, 3), np.float32)
    alpha = np.zeros((n, n), np.float32)

    def paint(mask, color, al=1.0):
        col[mask] = color
        alpha[mask] = al

    outer, inner = G.RING_R + G.RING_W / 2, G.RING_R - G.RING_W / 2
    paint(r < outer, _c("gold"))
    paint(r < inner, _c("ink0"))
    for yy, x0, x1 in G.ROPES:
        paint((np.abs(y - (yy - 100.0)) < G.ROPE_W / 2) & (x >= x0 - 100.0) & (x <= x1 - 100.0) & (r < inner), _c("gold"))
    rho = G.star_radius_numpy(a, G.STAR_OUT, G.STAR_IN, G.STAR_POINTS)
    paint(r < rho + G.STAR_OUTLINE / 2, _c("ink0"))
    paint(r < rho - G.STAR_OUTLINE / 2, _c("crimson"))
    paint(r < G.DOT_R, _c("gold"))
    return col, alpha

def d_logo(n):
    """Печатка промоушену на полотні рингу: золоте кільце, чорне поле, канати, зірка (на червоному мату «в'їзд заборонено»
    утворювала планка + зірка кольору мату — тепер чорне поле тримає зірку видимою)."""
    col, alpha = seal_layers(n)
    alpha = alpha * 0.92
    alpha *= np.clip(0.80 + 0.20 * fbm(n, 30, 211, 3), 0, 1)        # фарба злегка стерта
    return col, alpha

def d_tape(n):
    u, v = grid(n)
    x, y = u / n - 0.5, v / n - 0.5
    a = (np.abs(x - y) < 0.06) | (np.abs(x + y) < 0.06)
    a &= (np.abs(x) < 0.42) & (np.abs(y) < 0.42)
    alpha = a.astype(np.float32) * np.clip(0.85 + 0.15 * noise(n, 2, 221), 0, 1)
    col = np.ones((n, n, 3), np.float32) * np.array((0.95, 0.85, 0.15), np.float32)
    return col, alpha

def d_grime(n):
    u, v = grid(n)
    x, y = u / n * 2 - 1, v / n * 2 - 1
    r = np.sqrt(x * x + y * y)
    alpha = np.clip((1 - r) * 1.2, 0, 1) * smooth(fbm(n, 40, 231, 4), -0.6, 1.2) * 0.6
    col = np.ones((n, n, 3), np.float32) * np.array((0.12, 0.1, 0.08), np.float32)
    return col, alpha

# ---------------------------------------------------------------- контент LED-екранів (сцени світла, ring_show.py)

def led_logo(n):
    """Заставка промоушену: печатка (та сама, що на мату й банерах) на темно-червоному градієнті з променями."""
    u, v = grid(n)
    x, y = u / n * 2 - 1, v / n * 2 - 1
    r = np.sqrt(x * x + y * y)
    a = np.arctan2(y, x)
    rays = 0.5 + 0.5 * np.cos(a * 12)
    bg = np.stack([0.35 + 0.3 * (1 - r), 0.02 + 0.03 * rays * (1 - r), 0.05 + 0.04 * rays], -1)
    col, al = seal_layers(n, scale=0.66)
    img = bg * (1 - al[..., None]) + col * al[..., None]
    return np.clip(img, 0, 1)

def led_fire(n):
    """Полум'я в плоскому стилі афіш (Статут ARENA-04): три шари язиків — кармін, сигнал, золото — на чорному."""
    u, v = grid(n)
    x, y = u / n, v / n                                  # y — знизу вгору, 0 = низ кадру
    img = np.zeros((n, n, 3), np.float32) + _c("ink0")
    for base, amp, freq, phase, name in ((0.40, 0.42, 5.0, 0.00, "crimson"), (0.30, 0.34, 7.0, 0.35, "signal"), (0.18, 0.26, 9.0, 0.70, "gold")):
        tri = np.abs(((x * freq + phase) % 1.0) - 0.5) * 2.0       # 0 — вершина язика, 1 — западина
        h = base + amp * (1.0 - tri) ** 1.4
        img[y < h] = _c(name)
    return img

def led_waves(n):
    u, v = grid(n)
    x, y = u / n, v / n
    w = 0.5 + 0.5 * np.sin((x * 6 + 0.6 * np.sin(y * 9)) * math.pi)
    return np.stack([0.05 + 0.15 * w, 0.2 + 0.5 * w, 0.6 + 0.4 * w], -1)

def led_name(n):
    """Заставка виходу борця: «ім'я» блоками й смуги кольору борця (без тексту — гра підставить свій)."""
    u, v = grid(n)
    x, y = u / n, v / n
    img = np.zeros((n, n, 3), np.float32) + np.array((0.02, 0.02, 0.03))
    stripes = (np.abs(((x + y * 0.5) * 8) % 1.0 - 0.5) < 0.12)
    img[stripes] = (0.6, 0.05, 0.05)
    blocks = (y > 0.42) & (y < 0.58) & (x > 0.12) & (x < 0.88) & (((x - 0.12) * 12 % 1.0) < 0.82)
    img[blocks] = (0.95, 0.95, 0.95)
    return img

LED = {"led_logo": (led_logo, 512), "led_fire": (led_fire, 512), "led_waves": (led_waves, 512), "led_name": (led_name, 512)}

DECALS = {"decal_poster_%d" % k: (lambda n, k=k: d_poster(n, k), 512) for k in range(4)}
DECALS.update({"decal_logo": (d_logo, 1024), "decal_tape": (d_tape, 256), "decal_grime": (d_grime, 512)})

# ---------------------------------------------------------------- запуск

os.makedirs(OUT, exist_ok=True)
manifest = ["# Текстури арен (tools/blender/ring_textures.py). <набір> <розмір px> <плитка м>; карти _albedo/_normal/_rough."]
for name, (fn, size, tile) in SETS.items():
    manifest.append("%s %d %.2f" % (name, size, tile))
    if WANT and name not in WANT:
        continue
    alb, h, rough, strength = fn(size)
    emit(name, alb, h, rough, strength * size / 1024.0)
    print("TEX %s %d" % (name, size), flush=True)
for name, (fn, size) in DECALS.items():
    manifest.append("%s %d decal" % (name, size))
    if WANT and name not in WANT:
        continue
    col, alpha = fn(size)
    save(name, col, alpha)
    print("TEX %s %d" % (name, size), flush=True)
for name, (fn, size) in LED.items():
    manifest.append("%s %d led" % (name, size))
    if WANT and name not in WANT:
        continue
    save(name, fn(size))
    print("TEX %s %d" % (name, size), flush=True)
with open(os.path.join(OUT, "textures_manifest.txt"), "w", encoding="utf-8", newline="\n") as f:
    f.write("\n".join(manifest) + "\n")
