"""Аркуші кліпів без Blender: капсульні постаті з npz.
python -I render_clips.py <npz> <manifest> <outdir> <clip1,clip2,...> [--frames 10] [--at t1,t2] [--views side,q34,front,top]
Пара: для X_A додається X_B зі зсувом/поворотом з маніфесту. Рядок аркуша — вид, стовпець — мить."""
import sys, math, os, ctypes
import numpy as np
from PIL import Image, ImageDraw, ImageFont

try:
    k = ctypes.windll.kernel32
    k.SetPriorityClass(k.GetCurrentProcess(), 0x40)
except Exception:
    pass

npz, manifest, outdir, clips = sys.argv[1:5]
args = sys.argv[5:]
NF = int(args[args.index("--frames") + 1]) if "--frames" in args else 10
AT = [float(x) for x in args[args.index("--at") + 1].split(",")] if "--at" in args else None
VIEWS = args[args.index("--views") + 1].split(",") if "--views" in args else ["side", "q34", "front"]
CW, CH = (int(v) for v in args[args.index("--cell") + 1].split("x")) if "--cell" in args else (230, 300)
D = np.load(npz, allow_pickle=True)
BONES = [str(b) for b in D["bones"]]
BI = {b: i for i, b in enumerate(BONES)}
FPS = float(D["fps"])
MAN = {}
for line in open(manifest, encoding="utf-8"):
    if line.strip() and not line.startswith("#"):
        f = line.split()
        MAN[f[0]] = dict(role=f[1], L=float(f[2]), off=np.array([float(f[4]), float(f[5]), float(f[6])]), yaw=float(f[7]))
font = ImageFont.truetype("C:/Windows/Fonts/arial.ttf", 14)
fontb = ImageFont.truetype("C:/Windows/Fonts/arialbd.ttf", 18)


def Rz(deg):
    c, s = math.cos(math.radians(deg)), math.sin(math.radians(deg))
    return np.array([[c, -s, 0], [s, c, 0], [0, 0, 1]])


def segs(p):
    g = lambda n: p[BI[n]]
    head = g("head")
    nd = head - g("neck_01")
    top = head + nd / max(np.linalg.norm(nd), 1e-6) * 0.16
    s = [(g("pelvis"), g("spine_01"), 0.12, "t"), (g("spine_01"), g("spine_02"), 0.12, "t"), (g("spine_02"), g("spine_03"), 0.12, "t"),
         (g("spine_03"), g("neck_01"), 0.11, "t"), (g("neck_01"), head, 0.05, "t"), (head, top, 0.095, "h")]
    for sd in "lr":
        s += [(g("clavicle_" + sd), g("upperarm_" + sd), 0.045, sd), (g("upperarm_" + sd), g("lowerarm_" + sd), 0.05, sd),
              (g("lowerarm_" + sd), g("hand_" + sd), 0.04, sd), (g("hand_" + sd), g("middle_02_" + sd), 0.035, sd + "h"),
              (g("pelvis"), g("thigh_" + sd), 0.08, "t"), (g("thigh_" + sd), g("calf_" + sd), 0.075, sd),
              (g("calf_" + sd), g("foot_" + sd), 0.055, sd), (g("foot_" + sd), g("ball_" + sd), 0.04, sd)]
    return s


VIEW = {"side": (90, 8), "q34": (40, 18), "front": (0, 6), "top": (30, 60), "back34": (220, 18), "side2": (270, 8)}
COL = {"A": {"t": (70, 110, 215), "h": (110, 150, 240), "l": (40, 200, 255), "r": (20, 70, 170), "lh": (40, 220, 255), "rh": (20, 60, 150)},
       "B": {"t": (205, 70, 140), "h": (235, 110, 170), "l": (255, 150, 60), "r": (150, 40, 90), "lh": (255, 170, 80), "rh": (140, 30, 80)}}


def proj(v, az, el):
    a, e = math.radians(az), math.radians(el)
    # камера дивиться з напрямку az (0 — спереду постаті A, тобто з −Y), піднята на el
    x = v[0] * math.cos(a) + v[1] * math.sin(a)
    d = -v[0] * math.sin(a) + v[1] * math.cos(a)        # глибина: більше — далі
    y = v[2] * math.cos(e) - d * math.sin(e)
    dep = d * math.cos(e) + v[2] * math.sin(e)
    return x, y, dep


def draw_cell(figs, az, el, center, scale, label):
    im = Image.new("RGB", (CW, CH), (238, 236, 230))
    d = ImageDraw.Draw(im)
    cx, cy = center

    def S(v):
        x, y, dp = proj(v - cx, az, el)
        return CW / 2 + x * scale, CH * 0.86 - (y - cy) * scale, dp

    # мат: сітка 0,5 м
    for gx in np.arange(-2, 2.01, 0.5):
        a, b = S(np.array([gx, -2, 0]) + [cx[0], cx[1], 0]), S(np.array([gx, 2, 0]) + [cx[0], cx[1], 0])
        d.line((a[0], a[1], b[0], b[1]), fill=(205, 200, 190), width=1)
        a, b = S(np.array([-2, gx, 0]) + [cx[0], cx[1], 0]), S(np.array([2, gx, 0]) + [cx[0], cx[1], 0])
        d.line((a[0], a[1], b[0], b[1]), fill=(205, 200, 190), width=1)
    items = []
    for tag, p in figs:
        for a, b, r, part in segs(p):
            pa, pb = S(a), S(b)
            items.append(((pa[2] + pb[2]) / 2, pa, pb, r, COL[tag][part]))
    items.sort(key=lambda it: -it[0])
    for _, pa, pb, r, c in items:
        w = max(2, int(2 * r * scale))
        d.line((pa[0], pa[1], pb[0], pb[1]), fill=c, width=w)
        for q in (pa, pb):
            d.ellipse((q[0] - w / 2, q[1] - w / 2, q[0] + w / 2, q[1] + w / 2), fill=c)
    d.text((4, 2), label, fill=(0, 0, 0), font=font)
    return im


def render(clip):
    figs_all = []
    m = MAN.get(clip, {})
    pa = D["pos/" + clip]
    figs_all.append(("A", pa))
    base = clip[:-2] if clip.endswith("_A") else None
    if base and (base + "_B") in MAN and ("pos/" + base + "_B") in D:
        mb = MAN[base + "_B"]
        pb = D["pos/" + base + "_B"] @ Rz(mb["yaw"]).T + mb["off"]
        figs_all.append(("B", pb))
    F = min(len(f[1]) for f in figs_all)
    L = (F - 1) / FPS
    ts = AT if AT else [L * i / (NF - 1) for i in range(NF)]
    # центр і масштаб — за всім кліпом
    allp = np.concatenate([f[1][:F].reshape(-1, 3) for f in figs_all])
    cx = np.array([np.median(allp[:, 0]), np.median(allp[:, 1]), 0.0])
    span = max(np.ptp(allp[:, 0]), np.ptp(allp[:, 1]), 1.6)
    scale = min(CW / (span + 0.4), CH * 0.82 / 2.1)
    rows = []
    for vn in VIEWS:
        az, el = VIEW[vn]
        cells = []
        for t in ts:
            f = min(F - 1, int(round(t * FPS)))
            figs = [(tag, p[f]) for tag, p in figs_all]
            cells.append(draw_cell(figs, az, el, (cx, 0.0), scale, "%.2fс %s" % (t, vn)))
        rows.append(cells)
    W = CW * len(ts)
    sheet = Image.new("RGB", (W, CH * len(VIEWS) + 28), (20, 20, 20))
    ImageDraw.Draw(sheet).text((6, 4), "%s   L=%.2f  %s" % (clip, L, "пара A+B" if len(figs_all) > 1 else ""), fill=(255, 230, 0), font=fontb)
    for r_, cells in enumerate(rows):
        for c_, im in enumerate(cells):
            sheet.paste(im, (c_ * CW, 28 + r_ * CH))
    os.makedirs(outdir, exist_ok=True)
    sheet.save(os.path.join(outdir, clip + ".png"))


for c in clips.split(","):
    render(c)
print("ok")
