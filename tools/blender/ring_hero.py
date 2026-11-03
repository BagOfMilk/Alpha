# Героїчні предмети «Рингу» (ітерація 2+, власник 27.10.2026: «А чо все такое лоу поли?» → «И А и Б»):
# предмети, які камера бачить зблизька, будуються ДВІЧІ — детальна модель (болти, різьба, шви, рифлення, складки)
# і легка ігрова; деталь запікається з першої на другу картами (нормаль, AO, зони матеріалів), а колір, знос
# (сколи фарби на ребрах, затерті верхівки рифлення, бруд у западинах) складається з цих карт кодом.
# Так роблять ігри рівня AA: геометрія легка (Android), деталь — у текстурах.
#
#   "C:\Program Files\Blender Foundation\Blender 4.5\blender.exe" -b --python tools/blender/ring_hero.py -- <корінь репо> [ім'я…] [--preview]
#
# Виходи:
#   Assets/Art/Ring/Hero/hero.fbx — легкі моделі «hero_<предмет>» (лицем як у генераторі арен, Z угору);
#   Assets/Art/Ring/Hero/Textures/hero_<предмет>_normal.png (OpenGL), hero_<предмет>_<варіант>_albedo.png (sRGB),
#   hero_<предмет>_<варіант>_mask.png (R — металевість, G — AO, A — гладкість: метал/гладкість і оклюзія URP Lit
#   читають ту саму текстуру);
#   Assets/Art/Ring/Hero/hero_manifest.txt — предмети, трикутники, текстури;
#   Logs/hero/hero_lib.blend — легкі сітки для генератора арен (ring_venues.py ставить їх замість коробок);
#   Logs/hero/<предмет>_preview.png (--preview) — легка модель із запеченими текстурами поруч із детальною.
# Детерміновано: шум — з фіксованого зерна, розкладка — кодом.

import bpy, bmesh, math, os, sys, time
import numpy as np
from mathutils import Vector, Matrix

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
REPO = args[0] if args else os.getcwd()
WANT = [a for a in args[1:] if not a.startswith("--")]
PREVIEW = "--preview" in args
OUT = os.path.join(REPO, "Assets", "Art", "Ring", "Hero")
TEX = os.path.join(OUT, "Textures")
LOGS = os.path.join(REPO, "Logs", "hero")

# ---------------------------------------------------------------- зони матеріалів
# зона: (колір лінійний, металевість, шорсткість, знос, дрібна деталь)
#   знос: chip — фарба по металу (сколи на ребрах відкривають сталь); scuff — вініл (ребра світлішають і матовіють);
#         polish — метал (ребра й верхівки затерті до блиску); none
#   деталь: peel — «апельсинова кірка» порошкової фарби; grain — зерно вінілу; brushed — шліфований метал;
#           spangle — оцинковка; weave — стрічка; none
ZONES = {
    "paint":   ((0.03, 0.03, 0.032), 0.0, 0.42, "chip", "peel"),
    "steel":   ((0.52, 0.53, 0.55), 1.0, 0.38, "polish", "brushed"),
    "galv":    ((0.58, 0.58, 0.57), 1.0, 0.45, "polish", "spangle"),
    "plate":   ((0.50, 0.51, 0.52), 1.0, 0.42, "polish", "spangle"),   # рифлений лист (варіант може пофарбувати)
    "vinyl":   ((0.80, 0.80, 0.80), 0.0, 0.42, "scuff", "grain"),      # світлий: колір дає відтінок арени
    "thread":  ((0.70, 0.70, 0.68), 0.0, 0.8, "none", "none"),
    "strap":   ((0.025, 0.025, 0.025), 0.0, 0.85, "none", "weave"),
    "rubber":  ((0.02, 0.02, 0.02), 0.0, 0.88, "none", "none"),
    "weld":    ((0.20, 0.19, 0.18), 1.0, 0.55, "none", "none"),
    # нові зони — лише в кінець (колір-ідентифікатор зони залежить від її номера)
    "duratex": ((0.03, 0.03, 0.032), 0.0, 0.72, "scuff", "peel"),        # фактурна фарба корпусів колонок
    "grille":  ((0.035, 0.035, 0.04), 0.7, 0.5, "polish", "none"),
    "chrome":  ((0.90, 0.90, 0.90), 1.0, 0.1, "polish", "none"),
    "hdpe":    ((0.80, 0.80, 0.78), 0.0, 0.55, "scuff", "grain"),        # литий пластик стільниці
    "wood":    ((0.30, 0.17, 0.08), 0.0, 0.5, "scuff", "grain"),
    "brass":   ((0.85, 0.62, 0.25), 1.0, 0.25, "polish", "brushed"),
    "alu":     ((0.60, 0.61, 0.63), 1.0, 0.35, "polish", "brushed"),
    "plastic": ((0.02, 0.02, 0.022), 0.0, 0.5, "scuff", "none"),
    "seat":    ((0.80, 0.80, 0.80), 0.0, 0.5, "scuff", "grain"),         # колір дає екземпляр (маска B)
    "glass":   ((0.01, 0.01, 0.012), 0.0, 0.05, "none", "none"),
    "canvas":  ((0.66, 0.66, 0.64), 0.0, 0.85, "grime", "grain"),       # полотно рингу: не біле, а затерте
}
TINT_ZONES = {"vinyl", "seat"}       # маска B = 1: тут колір множиться на колір екземпляра/арени
ZONE_NAMES = list(ZONES)
STEEL_BARE = (0.50, 0.50, 0.52)

def zone_id(i):
    """Колір-ідентифікатор зони для запікання (лінійний, рознесений по кубу)."""
    return ((i % 3) * 0.45 + 0.05, ((i // 3) % 3) * 0.45 + 0.05, ((i // 9) % 3) * 0.45 + 0.05)

def zone_material(z):
    name = "HZ_" + z
    m = bpy.data.materials.get(name)
    if m:
        return m
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    col, metal, rough = ZONES[z][0], ZONES[z][1], ZONES[z][2]
    b.inputs["Base Color"].default_value = (*col, 1)
    b.inputs["Metallic"].default_value = metal
    b.inputs["Roughness"].default_value = rough
    for key in ("Emission Color", "Emission"):
        if key in b.inputs:
            b.inputs[key].default_value = (*zone_id(ZONE_NAMES.index(z)), 1)
            break
    b.inputs["Emission Strength"].default_value = 0.0      # вмикається лише на час запікання зон
    return m

# ---------------------------------------------------------------- геометрія (bmesh)

def _xf(bm, verts, M):
    bmesh.ops.transform(bm, matrix=M, verts=verts)

def frame(p0, p1):
    """Матриця, що ставить вісь Z уздовж p0→p1 з початком у p0."""
    p0, p1 = Vector(p0), Vector(p1)
    d = p1 - p0
    q = d.normalized().to_track_quat('Z', 'Y')
    return Matrix.Translation(p0) @ q.to_matrix().to_4x4(), d.length

def cyl(bm, p0, p1, r, segs=24, r1=None, caps=True):
    M, L = frame(p0, p1)
    res = bmesh.ops.create_cone(bm, cap_ends=caps, segments=segs, radius1=r, radius2=r if r1 is None else r1, depth=L)
    _xf(bm, res["verts"], M @ Matrix.Translation((0, 0, L / 2)))
    return res["verts"]

def lathe(bm, p0, p1, profile, segs=32):
    """Тіло обертання: профіль [(радіус, частка 0..1 довжини)] уздовж p0→p1, торці закриті."""
    M, L = frame(p0, p1)
    rings = []
    for r, t in profile:
        rings.append([bm.verts.new(M @ Vector((r * math.cos(2 * math.pi * k / segs), r * math.sin(2 * math.pi * k / segs), t * L)))
                      for k in range(segs)])
    for a, b in zip(rings, rings[1:]):
        for k in range(segs):
            bm.faces.new((a[k], a[(k + 1) % segs], b[(k + 1) % segs], b[k]))
    bm.faces.new(list(reversed(rings[0])))
    bm.faces.new(rings[-1])
    return [v for r_ in rings for v in r_]

def box(bm, c, s, M=None):
    res = bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=s, verts=res["verts"])
    bmesh.ops.translate(bm, vec=c, verts=res["verts"])
    if M is not None:
        _xf(bm, res["verts"], M)
    return res["verts"]

def rbox(bm, c, s, r, cuts=6, M=None, bulge=None):
    """Заокруглена коробка: густий куб, вершини стягнуті до округлого тіла (радіус r); bulge(p) → зсув."""
    tb = bmesh.new()
    bmesh.ops.create_cube(tb, size=2.0)
    bmesh.ops.subdivide_edges(tb, edges=list(tb.edges), cuts=cuts, use_grid_fill=True)
    half = Vector(s) / 2
    for v in tb.verts:
        p = Vector((v.co.x * half.x, v.co.y * half.y, v.co.z * half.z))
        q = Vector((max(-half.x + r, min(half.x - r, p.x)), max(-half.y + r, min(half.y - r, p.y)),
                    max(-half.z + r, min(half.z - r, p.z))))
        d = p - q
        v.co = q + (d.normalized() * r if d.length > 1e-9 else d)
        if bulge:
            v.co += bulge(v.co, half)
        v.co += Vector(c)
    if M is not None:
        bmesh.ops.transform(tb, matrix=M, verts=list(tb.verts))
    _merge(bm, tb)

def _merge(dst, src):
    me = bpy.data.meshes.new("_t")
    src.to_mesh(me)
    src.free()
    dst.from_mesh(me)
    bpy.data.meshes.remove(me)

def torus(bm, c, axis, R, r, segs=32, rsegs=10):
    M, _ = frame(Vector(c), Vector(c) + Vector(axis))
    rings = []
    for i in range(segs):
        a = 2 * math.pi * i / segs
        ctr = Vector((R * math.cos(a), R * math.sin(a), 0))
        out = Vector((math.cos(a), math.sin(a), 0))
        rings.append([bm.verts.new(M @ (ctr + out * r * math.cos(2 * math.pi * j / rsegs)
                                        + Vector((0, 0, r * math.sin(2 * math.pi * j / rsegs))))) for j in range(rsegs)])
    for i in range(segs):
        a, b = rings[i], rings[(i + 1) % segs]
        for j in range(rsegs):
            bm.faces.new((a[j], b[j], b[(j + 1) % rsegs], a[(j + 1) % rsegs]))

def tube(bm, pts, r, segs=12, closed=False):
    """Трубка вздовж ламаної з плавними кільцями (кадр паралельного перенесення)."""
    pts = [Vector(p) for p in pts]
    n = len(pts)
    tang = []
    for i in range(n):
        if closed:
            t = pts[(i + 1) % n] - pts[i - 1]
        else:
            t = pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]
        tang.append(t.normalized())
    up = Vector((0, 0, 1)) if abs(tang[0].z) < 0.9 else Vector((1, 0, 0))
    nrm = tang[0].cross(up).normalized()
    rings = []
    for i in range(n):
        if i:
            nrm = (nrm - tang[i] * nrm.dot(tang[i])).normalized()
        bin_ = tang[i].cross(nrm)
        rings.append([bm.verts.new(pts[i] + (nrm * math.cos(2 * math.pi * k / segs) + bin_ * math.sin(2 * math.pi * k / segs)) * r)
                      for k in range(segs)])
    m = n if closed else n - 1
    for i in range(m):
        a, b = rings[i], rings[(i + 1) % n]
        for k in range(segs):
            bm.faces.new((a[k], a[(k + 1) % segs], b[(k + 1) % segs], b[k]))
    if not closed:
        bm.faces.new(list(reversed(rings[0])))
        bm.faces.new(rings[-1])

def thread(bm, p0, p1, r, pitch=0.003, depth=0.0008, segs=24):
    """Різьба: циліндр, радіус якого біжить гвинтом (фаза залежить і від кута, і від висоти)."""
    M, L = frame(p0, p1)
    steps = max(4, int(L / pitch * 6))
    rings = []
    for i in range(steps + 1):
        z = L * i / steps
        ring = []
        for k in range(segs):
            a = 2 * math.pi * k / segs
            rr = r - depth * 0.5 + depth * 0.5 * math.sin(2 * math.pi * z / pitch + a)
            ring.append(bm.verts.new(M @ Vector((rr * math.cos(a), rr * math.sin(a), z))))
        rings.append(ring)
    for a, b in zip(rings, rings[1:]):
        for k in range(segs):
            bm.faces.new((a[k], a[(k + 1) % segs], b[(k + 1) % segs], b[k]))
    bm.faces.new(list(reversed(rings[0])))
    bm.faces.new(rings[-1])

def hexbolt(bm, c, axis, r=0.008, h=0.006):
    c, ax = Vector(c), Vector(axis).normalized()
    cyl(bm, c, c + ax * h, r, segs=6)
    cyl(bm, c + ax * h, c + ax * (h + 0.002), r * 0.75, segs=16)      # шайба-фаска верхівки

_BLOB = None

def _blob():
    """Шаблон краплини: сфера 8×5 (вершини, грані) — ставиться прямими bm.verts.new (bmesh.ops на великій сітці
    щоразу обходять її всю, і тисячі краплин коштували хвилини)."""
    global _BLOB
    if _BLOB is None:
        U, V = 8, 5
        vs = [(0.0, 0.0, -1.0)]
        for j in range(1, V):
            t = math.pi * j / V - math.pi / 2
            for i in range(U):
                a = 2 * math.pi * i / U
                vs.append((math.cos(t) * math.cos(a), math.cos(t) * math.sin(a), math.sin(t)))
        vs.append((0.0, 0.0, 1.0))
        fs = []
        top = len(vs) - 1
        for i in range(U):
            fs.append((0, 1 + (i + 1) % U, 1 + i))
        for j in range(V - 2):
            for i in range(U):
                a0, a1 = 1 + j * U + i, 1 + j * U + (i + 1) % U
                fs.append((a0, a1, a1 + U, a0 + U))
        base = 1 + (V - 2) * U
        for i in range(U):
            fs.append((base + i, base + (i + 1) % U, top))
        _BLOB = (vs, fs)
    return _BLOB

def weld(bm, pts, r=0.003):
    """Зварний шов: ланцюжок приплюснутих краплин уздовж ламаної (дає хвилясту нормаль, як справжній валик)."""
    vs0, fs0 = _blob()
    pts = [Vector(p) for p in pts]
    for a, b in zip(pts, pts[1:]):
        L = (b - a).length
        n = max(1, int(L / (r * 0.9)))
        for i in range(n + 1):
            p = a.lerp(b, i / n)
            vv = [bm.verts.new((p.x + x * r, p.y + y * r, p.z + z * r * 0.7)) for (x, y, z) in vs0]
            for f in fs0:
                bm.faces.new([vv[k] for k in f])

def lugs(bm, origin, u, v, nrm, su, sv, pitch=0.03, L=0.026, Wd=0.007, H=0.0016):
    """Рифлення рифленого листа: видовжені ромби впоперемінку ±45°, плаский верх."""
    origin, u, v, nrm = Vector(origin), Vector(u).normalized(), Vector(v).normalized(), Vector(nrm).normalized()
    nu, nv = int(su / pitch), int(sv / pitch)
    for i in range(nu):
        for j in range(nv):
            cu = (i + 0.5) * pitch + (pitch * 0.5 if j % 2 else 0) - su / 2
            if abs(cu) > su / 2 - pitch * 0.4:
                continue
            cv = (j + 0.5) * pitch - sv / 2
            a = math.radians(45 if (i + j) % 2 else -45)
            du = u * math.cos(a) + v * math.sin(a)
            dv = -u * math.sin(a) + v * math.cos(a)
            c = origin + u * cu + v * cv
            base = [c + du * L / 2, c + dv * Wd / 2, c - du * L / 2, c - dv * Wd / 2]
            top = [c + du * L * 0.36 + nrm * H, c + dv * Wd * 0.2 + nrm * H, c - du * L * 0.36 + nrm * H, c - dv * Wd * 0.2 + nrm * H]
            bv = [bm.verts.new(p) for p in base]
            tv = [bm.verts.new(p) for p in top]
            bm.faces.new(tv)
            for k in range(4):
                bm.faces.new((bv[k], bv[(k + 1) % 4], tv[(k + 1) % 4], tv[k]))

# ---------------------------------------------------------------- об'єкти

HIGH = None
LOWC = None

def coll(name):
    c = bpy.data.collections.get(name)
    if c is None:
        c = bpy.data.collections.new(name)
        bpy.context.scene.collection.children.link(c)
    return c

def high(name, bm, zone, bevel=0.0, segs=3, subsurf=0, smooth=True):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for p in me.polygons:
        p.use_smooth = smooth
    o = bpy.data.objects.new(name, me)
    HIGH.objects.link(o)
    me.materials.append(zone_material(zone))
    if bevel:
        md = o.modifiers.new("bevel", 'BEVEL')
        md.width, md.segments, md.limit_method = bevel, segs, 'ANGLE'
        md.angle_limit = math.radians(35)
        md.harden_normals = False
    if subsurf:
        md = o.modifiers.new("sub", 'SUBSURF')
        md.levels = md.render_levels = subsurf
    return o

def low(name, bm, bevel=0.0):
    if bevel:
        bmesh.ops.bevel(bm, geom=[e for e in bm.edges if e.calc_face_angle(0) > math.radians(35)], offset=bevel,
                        segments=1, affect='EDGES', profile=0.5)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bmesh.ops.triangulate(bm, faces=list(bm.faces))                # та сама тріангуляція, що й у рушії (MikkTSpace)
    me = bpy.data.meshes.new("hero_" + name)
    bm.to_mesh(me)
    bm.free()
    for p in me.polygons:
        p.use_smooth = True
    o = bpy.data.objects.new("hero_" + name, me)
    LOWC.objects.link(o)
    return o

# ---------------------------------------------------------------- предмети
# Осі предметів — як у ring_venues.build_ring: стійка й подушка — +X від центру рингу назовні по діагоналі кута,
# нуль стійки — на рівні мату; сходи — +X від рингу назовні, нуль — біля фартуха на підлозі, ширина вздовж Y.

ROPES = (0.48, 0.86, 1.24)

def build_post():
    """Стійка: фарбована труба з ковпаком і зварним швом, три хомути з вушками, натяжні муфти з різьбою й оком."""
    bm = bmesh.new()
    cyl(bm, (0, 0, -0.1), (0, 0, 1.5), 0.06, segs=48)
    lathe(bm, (0, 0, 1.495), (0, 0, 1.54), [(0.068, 0), (0.07, 0.15), (0.07, 0.55), (0.06, 0.85), (0.035, 1.0)], segs=48)
    for h in ROPES:
        cyl(bm, (0, 0, h - 0.036), (0, 0, h + 0.036), 0.074, segs=48)
        box(bm, (-0.095, 0, h), (0.05, 0.016, 0.056))                      # вушко хомута
    high("post_paint", bm, "paint", bevel=0.003, segs=3)
    bm = bmesh.new()
    weld(bm, [(0.0605, 0, 0.2), (0.0605, 0, 1.45)], r=0.0025)                # поздовжній шов труби (ззаду)
    for h in ROPES:
        for dz in (-0.036, 0.036):
            pts = [Vector((0.061 * math.cos(a), 0.061 * math.sin(a), h + dz)) for a in np.linspace(0, 2 * math.pi, 40)]
            weld(bm, pts, r=0.0022)
    high("post_weld", bm, "weld")
    bm = bmesh.new()
    for h in ROPES:
        for s in (-1, 1):
            hexbolt(bm, (-0.105, s * 0.008, h), (0, s, 0), r=0.007, h=0.005)   # палець вушка
        hexbolt(bm, (0.0, 0.074, h), (0, 1, 0), r=0.008, h=0.006)            # стяжний болт хомута
        # муфта: дві щоки, торці, різьбові шпильки, око
        for s in (-1, 1):
            box(bm, (-0.185, s * 0.013, h), (0.09, 0.006, 0.016))
        cyl(bm, (-0.138, 0, h), (-0.142, 0, h), 0.012, segs=24)
        cyl(bm, (-0.228, 0, h), (-0.232, 0, h), 0.012, segs=24)
        torus(bm, (-0.11, 0, h), (0, 1, 0), 0.012, 0.0045)                     # скоба до вушка
        torus(bm, (-0.27, 0, h), (0, 1, 0), 0.016, 0.005)                      # око під канат
    high("post_galv", bm, "galv", bevel=0.0008, segs=2)
    bm = bmesh.new()
    for h in ROPES:
        thread(bm, (-0.142, 0, h), (-0.12, 0, h), 0.0065)
        thread(bm, (-0.232, 0, h), (-0.255, 0, h), 0.0065)
    high("post_thread", bm, "steel")
    # легка
    bm = bmesh.new()
    cyl(bm, (0, 0, -0.1), (0, 0, 1.5), 0.0605, segs=16)
    lathe(bm, (0, 0, 1.495), (0, 0, 1.54), [(0.07, 0), (0.07, 0.55), (0.04, 1.0)], segs=16)
    for h in ROPES:
        cyl(bm, (0, 0, h - 0.037), (0, 0, h + 0.037), 0.075, segs=16)
        box(bm, (-0.098, 0, h), (0.056, 0.02, 0.058))
        box(bm, (-0.185, 0, h), (0.096, 0.034, 0.026))
        box(bm, (-0.27, 0, h), (0.045, 0.012, 0.045))
    return low("ring_post", bm)

PAD = (0.12, 0.24, 0.26)

def build_pad():
    """Подушка кута: пухка вінілова подушка з кантом і строчкою по краях, ремені на липучках ззаду."""
    sx, sy, sz = PAD
    def puff(p, half):                                                    # лице (−X) і боки трохи надуті
        fy = max(0.0, 1 - (p.y / half.y) ** 2)
        fz = max(0.0, 1 - (p.z / half.z) ** 2)
        return Vector((-0.014 * fy * fz if p.x < 0 else 0.004 * fy * fz, 0, 0))
    bm = bmesh.new()
    rbox(bm, (0, 0, 0), PAD, 0.03, cuts=14, bulge=puff)
    high("pad_vinyl", bm, "vinyl", subsurf=1)
    bm = bmesh.new()
    for xf in (-sx / 2 + 0.012, sx / 2 - 0.012):                          # кант по периметру лиця і спинки
        pts = []
        r_ = 0.03
        for k in range(48):
            a = 2 * math.pi * k / 48
            cy_ = (sy / 2 - r_) * (1 if math.cos(a) > 0 else -1)
            cz_ = (sz / 2 - r_) * (1 if math.sin(a) > 0 else -1)
            pts.append(Vector((xf - (0.004 if xf < 0 else -0.004), cy_ + r_ * math.cos(a), cz_ + r_ * math.sin(a))))
        tube(bm, pts, 0.0045, segs=8, closed=True)
    high("pad_piping", bm, "vinyl")
    bm = bmesh.new()                                                      # строчка — короткі стібки біля канта
    for yy, zz in [(y, sz / 2 - 0.016) for y in np.arange(-sy / 2 + 0.03, sy / 2 - 0.03, 0.009)] + \
                  [(y, -sz / 2 + 0.016) for y in np.arange(-sy / 2 + 0.03, sy / 2 - 0.03, 0.009)] + \
                  [(sy / 2 - 0.016, z) for z in np.arange(-sz / 2 + 0.03, sz / 2 - 0.03, 0.009)] + \
                  [(-sy / 2 + 0.016, z) for z in np.arange(-sz / 2 + 0.03, sz / 2 - 0.03, 0.009)]:
        p = Vector((-sx / 2 - 0.005, yy, zz))
        d = Vector((0, 1, 0)) if abs(abs(zz) - (sz / 2 - 0.016)) < 1e-6 else Vector((0, 0, 1))
        cyl(bm, p - d * 0.0028, p + d * 0.0028, 0.0011, segs=6)
    high("pad_thread", bm, "thread")
    bm = bmesh.new()                                                      # ремені на спинці (до стійки)
    for zz in (-0.07, 0.07):
        box(bm, (sx / 2 + 0.003, 0, zz), (0.004, sy + 0.08, 0.038))
        box(bm, (sx / 2 + 0.006, sy / 2 + 0.02, zz), (0.004, 0.05, 0.036))   # липучка внахлест
    high("pad_strap", bm, "strap", bevel=0.0012, segs=2)
    # легка
    bm = bmesh.new()
    rbox(bm, (0, 0, 0), (sx + 0.004, sy + 0.004, sz + 0.004), 0.032, cuts=5, bulge=puff)
    for zz in (-0.07, 0.07):
        box(bm, (sx / 2 + 0.005, 0, zz), (0.008, sy + 0.08, 0.04))
    return low("ring_pad", bm)

def steps_profile(mz, D=0.42):
    tops = [mz * (k + 1) / 3 - 0.04 for k in range(3)]
    return tops, [(0, 0), (3 * D, 0), (3 * D, tops[0]), (2 * D, tops[0]), (2 * D, tops[1]), (D, tops[1]), (D, tops[2]), (0, tops[2])]

def build_steps(mz):
    """Сталеві сходи: рифлені проступи з загнутим носком, гладкі підступенки з ребрами, бокові косоури з отвором-
    ручкою, зварні шви на стиках, гумові ніжки."""
    W, D = 0.95, 0.42
    tops, prof = steps_profile(mz, D)
    bm = bmesh.new()
    zprev = 0.02
    for k in range(3):
        x0, x1, z = D * (2 - k), D * (3 - k), tops[k]
        box(bm, ((x0 + x1) / 2 + 0.006, 0, z - 0.003), (x1 - x0 + 0.012, W - 0.012, 0.006))      # проступ
        box(bm, (x1 + 0.009, 0, z - 0.022), (0.006, W - 0.012, 0.044))                            # носок
        lugs(bm, ((x0 + x1) / 2, 0, z), (1, 0, 0), (0, 1, 0), (0, 0, 1), x1 - x0 - 0.02, W - 0.06)
        box(bm, (x1 + 0.003, 0, (zprev + z - 0.044) / 2), (0.005, W - 0.012, z - 0.044 - zprev))  # підступенок
        zprev = z
    box(bm, (-0.003, 0, tops[2] / 2), (0.006, W - 0.012, tops[2]))                                # задня стінка
    high("steps_plate", bm, "plate", bevel=0.0015, segs=2)
    bm = bmesh.new()
    for s in (-1, 1):                                                     # косоури з отвором-ручкою
        vs = [bm.verts.new((x, s * (W / 2 - 0.003), z)) for (x, z) in prof]
        f = bm.faces.new(vs if s > 0 else list(reversed(vs)))
        ext = bmesh.ops.extrude_face_region(bm, geom=[f])
        bmesh.ops.translate(bm, vec=(0, -s * 0.006, 0), verts=[e for e in ext["geom"] if isinstance(e, bmesh.types.BMVert)])
    for k in range(3):                                                    # штамповані ребра підступенків
        x1, z = D * (3 - k), tops[k]
        zp = tops[k - 1] if k else 0.02
        for t in (0.35, 0.7):
            zz = zp + (z - 0.044 - zp) * t
            cyl(bm, (x1 + 0.0055, -W / 2 + 0.05, zz), (x1 + 0.0055, W / 2 - 0.05, zz), 0.004, segs=10)
    high("steps_frame", bm, "paint", bevel=0.002, segs=3)
    bm = bmesh.new()                                                      # ручки: заглиблені овальні чаші в косоурах
    for s in (-1, 1):
        for xc in (0.42 * 1.5, 0.42 * 2.4):
            zc = 0.18 if xc > 0.8 else 0.4
            pts = [Vector((xc + 0.07 * math.cos(a), s * (W / 2 + 0.0005), zc + 0.025 * math.sin(a))) for a in np.linspace(0, 2 * math.pi, 33)[:-1]]
            tube(bm, pts, 0.004, segs=8, closed=True)
            rbox(bm, (xc, s * (W / 2 - 0.004), zc), (0.13, 0.008, 0.04), 0.018, cuts=6)
    high("steps_handle", bm, "rubber", subsurf=0)
    bm = bmesh.new()
    zprev = 0.02
    for k in range(3):
        x0, x1, z = D * (2 - k), D * (3 - k), tops[k]
        for s in (-1, 1):
            weld(bm, [(x1 + 0.006, s * (W / 2 - 0.007), zprev), (x1 + 0.006, s * (W / 2 - 0.007), z - 0.006)], r=0.0024)
            weld(bm, [(x0 + 0.01, s * (W / 2 - 0.007), z + 0.001), (x1, s * (W / 2 - 0.007), z + 0.001)], r=0.0024)
        zprev = z
    high("steps_weld", bm, "weld")
    bm = bmesh.new()
    for x in (0.04, 3 * D - 0.04):
        for s in (-1, 1):
            cyl(bm, (x, s * (W / 2 - 0.04), 0.0), (x, s * (W / 2 - 0.04), 0.022), 0.022, segs=24)
    high("steps_feet", bm, "rubber", bevel=0.003, segs=3)
    # легка: суцільний профіль сходів, видавлений на ширину
    bm = bmesh.new()
    vs = [bm.verts.new((x, -W / 2, z)) for (x, z) in prof]
    f = bm.faces.new(list(reversed(vs)))
    ext = bmesh.ops.extrude_face_region(bm, geom=[f])
    bmesh.ops.translate(bm, vec=(0, W, 0), verts=[e for e in ext["geom"] if isinstance(e, bmesh.types.BMVert)])
    for v in bm.verts:
        if v.co.z < 0.01:
            v.co.z = 0.022
    for x in (0.04, 3 * D - 0.04):
        for s in (-1, 1):
            cyl(bm, (x, s * (W / 2 - 0.04), 0.0), (x, s * (W / 2 - 0.04), 0.024), 0.023, segs=8)
    return low("ring_steps_%03d" % round(mz * 100), bm, bevel=0.004)

def perforated(bm, cx, cz, w, h, y, pitch=0.0075, hr=0.0026, depth=0.004):
    """Перфорована решітка в площині XZ на глибині y (лице — у −Y): клітинки з восьмикутними отворами-трубками
    (позаду отворів — темна задня стінка: AO і зони роблять їх чорними)."""
    nx, nz = int(w / pitch), int(h / pitch)
    x0, z0 = cx - nx * pitch / 2, cz - nz * pitch / 2
    oc = [(math.cos(math.radians(247.5 + 45 * k)), math.sin(math.radians(247.5 + 45 * k))) for k in range(8)]
    sq = [(-1, -1), (1, -1), (1, 1), (-1, 1)]
    for i in range(nx):
        for j in range(nz):
            ux = x0 + (i + 0.5) * pitch
            uz = z0 + (j + 0.5) * pitch
            C = [bm.verts.new((ux + a * pitch / 2, y, uz + b * pitch / 2)) for (a, b) in sq]
            O = [bm.verts.new((ux + a * hr, y, uz + b * hr)) for (a, b) in oc]
            B = [bm.verts.new((ux + a * hr, y + depth, uz + b * hr)) for (a, b) in oc]
            for k in range(4):
                bm.faces.new((C[k], C[(k + 1) % 4], O[2 * k + 1], O[2 * k]))
                bm.faces.new((C[(k + 1) % 4], O[(2 * k + 2) % 8], O[2 * k + 1]))
            for k in range(8):
                bm.faces.new((O[k], O[(k + 1) % 8], B[(k + 1) % 8], B[k]))

def build_speaker(w, d, h, name):
    """Пасивна колонка: корпус у фактурній фарбі, перфорована решітка в рамці, кутники, утоплені ручки з боків,
    роз'єми ззаду, чашки під стек зверху, ніжки знизу. Нуль — центр корпусу, лице — у −Y."""
    bm = bmesh.new()
    rbox(bm, (0, 0, 0), (w, d, h), 0.012, cuts=8)
    high(name + "_body", bm, "duratex")
    bm = bmesh.new()
    perforated(bm, 0, 0, w - 0.07, h - 0.07, -d / 2 - 0.004)
    high(name + "_grille", bm, "grille", smooth=False)
    bm = bmesh.new()
    box(bm, (0, -d / 2 + 0.03, 0), (w - 0.06, 0.004, h - 0.06))                   # задня стінка за решіткою
    for (z, r) in ((-h * 0.15, min(w, h) * 0.36), (h * 0.33, min(w, h) * 0.12)):  # дифузор і рупор
        lathe(bm, (0, -d / 2 + 0.028, z), (0, -d / 2 + 0.008, z), [(r, 0), (r * 0.9, 0.4), (r * 0.3, 0.9), (0.0, 1.0)], segs=40)
    for s_ in (-1, 1):                                                             # рамка решітки
        box(bm, (s_ * (w / 2 - 0.02), -d / 2 - 0.002, 0), (0.025, 0.012, h - 0.03))
        box(bm, (0, -d / 2 - 0.002, s_ * (h / 2 - 0.02)), (w - 0.03, 0.012, 0.025))
    high(name + "_inner", bm, "plastic", bevel=0.002, segs=2)
    bm = bmesh.new()
    for sx in (-1, 1):                                                             # кутники
        for sy in (-1, 1):
            for sz in (-1, 1):
                p = Vector((sx * (w / 2 - 0.012), sy * (d / 2 - 0.012), sz * (h / 2 - 0.012)))
                res = bmesh.ops.create_icosphere(bm, subdivisions=3, radius=0.03)
                bmesh.ops.translate(bm, vec=p, verts=res["verts"])
    high(name + "_corners", bm, "paint")
    bm = bmesh.new()
    for s_ in (-1, 1):                                                             # ручки-чашки з боків
        rbox(bm, (s_ * (w / 2 + 0.002), 0.0, h * 0.25), (0.012, min(0.2, d * 0.5), 0.07), 0.005, cuts=4)
    for x in (-0.06, 0.06):                                                        # роз'єми ззаду
        cyl(bm, (x, d / 2, -h * 0.25), (x, d / 2 + 0.012, -h * 0.25), 0.014, segs=24)
    box(bm, (0, d / 2 + 0.002, -h * 0.25), (min(0.22, w * 0.6), 0.004, 0.08))
    if h > 0.6:
        for sx in (-1, 1):
            for sy in (-1, 1):
                cyl(bm, (sx * w * 0.3, sy * d * 0.3, -h / 2 - 0.012), (sx * w * 0.3, sy * d * 0.3, -h / 2), 0.025, segs=24)   # ніжки
                torus(bm, (sx * w * 0.3, sy * d * 0.3, h / 2 + 0.001), (0, 0, 1), 0.03, 0.004)              # чашки під стек
    high(name + "_hw", bm, "rubber", bevel=0.001, segs=2)
    bm = bmesh.new()
    rbox(bm, (0, 0, 0), (w + 0.008, d + 0.008, h + 0.006), 0.016, cuts=1)
    return low(name, bm)

def build_stanchion():
    """Стійка огорожі: важка хромована база-купол з гумовим кільцем, стовпчик, ковпак з гніздами під гачки канату."""
    hh = 0.95
    T = hh + 0.09
    bm = bmesh.new()
    lathe(bm, (0, 0, 0.004), (0, 0, T), [(0.168, 0.0), (0.168, 0.02 / T), (0.15, 0.03 / T), (0.09, 0.045 / T),
                                        (0.045, 0.062 / T), (0.03, 0.075 / T), (0.026, 0.09 / T), (0.026, (hh - 0.01) / T),
                                        (0.034, hh / T), (0.042, (hh + 0.02) / T), (0.042, (hh + 0.06) / T),
                                        (0.03, (hh + 0.08) / T), (0.0, 1.0)], segs=64)
    high("st_chrome", bm, "chrome", subsurf=1)
    bm = bmesh.new()
    torus(bm, (0, 0, 0.006), (0, 0, 1), 0.16, 0.007, segs=64)
    for a in range(4):                                                            # гнізда під гачки
        ang = a * math.pi / 2
        rbox(bm, (0.043 * math.cos(ang), 0.043 * math.sin(ang), hh + 0.04), (0.012, 0.012, 0.03), 0.004, cuts=3)
    high("st_rubber", bm, "rubber")
    bm = bmesh.new()
    T2 = hh + 0.092
    lathe(bm, (0, 0, 0.0), (0, 0, T2), [(0.17, 0.0), (0.17, 0.024 / T2), (0.1, 0.045 / T2), (0.035, 0.07 / T2),
                                       (0.028, 0.09 / T2), (0.028, hh / T2), (0.044, (hh + 0.02) / T2),
                                       (0.044, (hh + 0.06) / T2), (0.0, 1.0)], segs=20)
    return low("stanchion", bm)

def build_table(L=1.83, W=0.76, H=0.74):
    """Розкладний стіл: литий пластик стільниці з ребрами знизу, ноги П-рамами з розпірками, кронштейни, болти,
    гумові ковпачки. Нуль — підлога під центром, стільниця — на висоті H (центр плити)."""
    bm = bmesh.new()
    rbox(bm, (0, 0, H), (L, W, 0.045), 0.016, cuts=10)
    high("tb_top", bm, "hdpe")
    bm = bmesh.new()
    for k in range(-5, 6):                                                        # ребра жорсткості під стільницею
        box(bm, (k * L / 12, 0, H - 0.026), (0.012, W - 0.1, 0.012))
    high("tb_ribs", bm, "hdpe", bevel=0.003, segs=2)
    bm = bmesh.new()
    for sx in (-1, 1):
        x = sx * (L / 2 - 0.09)
        for sy in (-1, 1):
            tube(bm, [(x, sy * (W / 2 - 0.07), 0.02), (x, sy * (W / 2 - 0.07), H - 0.025)], 0.0125, segs=16)
        tube(bm, [(x, -(W / 2 - 0.07), 0.12), (x, W / 2 - 0.07, 0.12)], 0.009, segs=12)            # нижня поперечка
        tube(bm, [(x, -(W / 2 - 0.07), H - 0.04), (x, W / 2 - 0.07, H - 0.04)], 0.011, segs=12)     # верхня (шарнір)
        tube(bm, [(x, 0, 0.12), (x - sx * 0.35, 0, H - 0.035)], 0.008, segs=12)                     # розпірка до центру
    tube(bm, [(-L / 2 + 0.44, 0, H - 0.035), (L / 2 - 0.44, 0, H - 0.035)], 0.008, segs=12)
    high("tb_legs", bm, "paint")
    bm = bmesh.new()
    for sx in (-1, 1):
        x = sx * (L / 2 - 0.09)
        for sy in (-1, 1):
            cyl(bm, (x, sy * (W / 2 - 0.07), 0.0), (x, sy * (W / 2 - 0.07), 0.035), 0.016, segs=20)        # ковпачки
            box(bm, (x, sy * (W / 2 - 0.07), H - 0.03), (0.05, 0.035, 0.012))                               # кронштейн
            hexbolt(bm, (x + sx * 0.0125, sy * (W / 2 - 0.07), H - 0.04), (sx, 0, 0), r=0.006, h=0.003)
    high("tb_hw", bm, "rubber", bevel=0.001, segs=2)
    bm = bmesh.new()
    rbox(bm, (0, 0, H), (L + 0.004, W + 0.004, 0.049), 0.018, cuts=1)
    for sx in (-1, 1):
        x = sx * (L / 2 - 0.09)
        for sy in (-1, 1):
            cyl(bm, (x, sy * (W / 2 - 0.07), 0.0), (x, sy * (W / 2 - 0.07), H - 0.02), 0.016, segs=6)
        cyl(bm, (x, -(W / 2 - 0.07), 0.12), (x, W / 2 - 0.07, 0.12), 0.011, segs=5)
        cyl(bm, (x, 0, 0.12), (x - sx * 0.35, 0, H - 0.035), 0.01, segs=5)
    cyl(bm, (-L / 2 + 0.44, 0, H - 0.035), (L / 2 - 0.44, 0, H - 0.035), 0.01, segs=5)
    return low("folding_table", bm)

def build_bell():
    """Гонг рингу: латунна чаша на дерев'яній основі, центральна гайка, гвинти, молоточок на стійці."""
    bm = bmesh.new()
    lathe(bm, (0, 0, 0), (0, 0, 0.052), [(0.088, 0), (0.092, 0.15), (0.092, 0.75), (0.084, 1.0), (0.0, 1.0)], segs=64)
    high("bl_wood", bm, "wood", bevel=0.002, segs=3)
    bm = bmesh.new()
    lathe(bm, (0, 0, 0.05), (0, 0, 0.175), [(0.13, 0), (0.134, 0.04), (0.128, 0.1), (0.11, 0.35), (0.08, 0.65), (0.05, 0.9),
                                            (0.02, 1.0), (0.0, 1.0)], segs=96)
    torus(bm, (0, 0, 0.065), (0, 0, 1), 0.128, 0.004, segs=96)                    # бортик чаші
    high("bl_brass", bm, "brass", subsurf=1)
    bm = bmesh.new()
    cyl(bm, (0.15, 0, 0.0), (0.15, 0, 0.21), 0.009, segs=16)                       # стійка молоточка
    high("bl_handle", bm, "wood")
    bm = bmesh.new()
    cyl(bm, (0, 0, 0.17), (0, 0, 0.19), 0.016, segs=6)                             # гайка
    cyl(bm, (0.15, -0.035, 0.215), (0.15, 0.035, 0.215), 0.017, segs=24)           # головка молоточка
    for a in range(3):
        ang = a * 2 * math.pi / 3 + 0.3
        hexbolt(bm, (0.07 * math.cos(ang), 0.07 * math.sin(ang), 0.052), (0, 0, 1), r=0.005, h=0.002)
    high("bl_steel", bm, "steel", bevel=0.001, segs=2)
    bm = bmesh.new()
    lathe(bm, (0, 0, 0), (0, 0, 0.054), [(0.093, 0), (0.093, 1.0), (0.0, 1.0)], segs=16)
    lathe(bm, (0, 0, 0.05), (0, 0, 0.192), [(0.136, 0), (0.12, 0.25), (0.08, 0.55), (0.04, 0.85), (0.0, 1.0)], segs=20)
    cyl(bm, (0.15, 0, 0.0), (0.15, 0, 0.21), 0.011, segs=6)
    cyl(bm, (0.15, -0.037, 0.215), (0.15, 0.037, 0.215), 0.019, segs=8)
    return low("ring_bell", bm)

LID = lambda: Matrix.Translation((0, 0.14, 0.145)) @ Matrix.Rotation(math.radians(-15), 4, 'X') @ Matrix.Translation((0, -0.14, -0.145))

def build_laptop():
    """Ноутбук: алюмінієвий корпус, клавіатура (окремі клавіші), тачпад, шарнір, кришка з рамкою і склом екрана
    (зображення малює генератор арени окремо — воно світиться). Нуль — стіл під центром основи, лице — у −Y."""
    bm = bmesh.new()
    rbox(bm, (0, 0, 0.011), (0.34, 0.24, 0.018), 0.006, cuts=8)
    rbox(bm, (0, 0.14, 0.145), (0.34, 0.012, 0.22), 0.005, cuts=8, M=LID())
    high("lt_alu", bm, "alu")
    bm = bmesh.new()
    kw, gap = 0.0175, 0.0025
    for r in range(6):
        n = 14 if r < 5 else 9
        for c in range(n):
            wide = r == 5 and c == 4
            x = -0.13 + c * (kw + gap) + (0.02 if r == 5 else 0.0) + (kw * 1.5 + gap * 1.5 if r == 5 and c > 4 else 0.0)
            box(bm, (x + (kw * 1.5 + gap * 1.5 if wide else 0.0), 0.07 - r * (kw + gap), 0.0205), (kw * 4 + gap * 3 if wide else kw, kw, 0.002))
    rbox(bm, (0, -0.075, 0.0202), (0.11, 0.065, 0.001), 0.006, cuts=3)             # тачпад
    cyl(bm, (-0.15, 0.118, 0.022), (0.15, 0.118, 0.022), 0.006, segs=16)            # шарнір
    high("lt_keys", bm, "plastic", bevel=0.0006, segs=2)
    bm = bmesh.new()
    rbox(bm, (0, 0.1325, 0.15), (0.31, 0.002, 0.19), 0.003, cuts=3, M=LID())
    high("lt_glass", bm, "glass")
    bm = bmesh.new()
    rbox(bm, (0, 0, 0.0115), (0.342, 0.242, 0.021), 0.007, cuts=1)
    rbox(bm, (0, 0.14, 0.145), (0.342, 0.014, 0.222), 0.006, cuts=1, M=LID())
    return low("laptop", bm)

CHAIR_BACK = [(0.17 + 0.012 * math.sin(math.pi * k / 10), 0.64 + 0.22 * k / 10) for k in range(11)]

def _shell_back(bm, prof, nx, width, thick, flip_front=False):
    """Вигнута спинка стільця: оболонка з товщиною (лице, спина, торці)."""
    rows = []
    for i in range(nx + 1):
        u = i / nx
        x = (u - 0.5) * width
        dy = 0.025 * (1 - (2 * u - 1) ** 2)
        rows.append([Vector((x, y_ + dy, z_)) for (y_, z_) in prof])
    n = len(prof)
    F = [[bm.verts.new(p) for p in row] for row in rows]
    B = [[bm.verts.new(p + Vector((0, thick, 0))) for p in row] for row in rows]
    for i in range(nx):
        for j in range(n - 1):
            bm.faces.new((F[i][j], F[i][j + 1], F[i + 1][j + 1], F[i + 1][j]))
            bm.faces.new((B[i][j], B[i + 1][j], B[i + 1][j + 1], B[i][j + 1]))
    for i in range(nx):
        bm.faces.new((F[i][0], F[i + 1][0], B[i + 1][0], B[i][0]))
        bm.faces.new((F[i][n - 1], B[i][n - 1], B[i + 1][n - 1], F[i + 1][n - 1]))
    for j in range(n - 1):
        bm.faces.new((F[0][j], B[0][j], B[0][j + 1], F[0][j + 1]))
        bm.faces.new((F[nx][j], F[nx][j + 1], B[nx][j + 1], B[nx][j]))

def build_chair():
    """Розкладний стілець (габарити — як ring_props.build_chair_folding: на ньому сидять пози натовпу): гнуті труби,
    заклепки шарнірів зі зварними стиками, литі сидіння й спинка, ребра під сидінням, ковпачки ніг."""
    r = 0.011
    bm = bmesh.new()
    for sx in (-1, 1):
        x = 0.19 * sx
        tube(bm, [(x, 0.21, 0.03), (x, 0.195, 0.24), (x, 0.18, 0.44), (x, 0.188, 0.62), (x, 0.195, 0.78), (x, 0.215, 0.86)], r, segs=16)
        tube(bm, [(x, -0.21, 0.03), (x, -0.185, 0.17), (x, -0.16, 0.30), (x, -0.13, 0.425)], r, segs=16)
        tube(bm, [(x * 0.97, -0.19, 0.425), (x * 0.97, 0.17, 0.425)], r * 0.9, segs=16)
    tube(bm, [(-0.19, -0.19, 0.09), (0.19, -0.19, 0.09)], r * 0.85, segs=16)
    tube(bm, [(-0.19, 0.205, 0.12), (0.19, 0.205, 0.12)], r * 0.85, segs=16)
    tube(bm, [(-0.19, 0.19, 0.62), (0.19, 0.19, 0.62)], r * 0.8, segs=16)
    high("ch_frame", bm, "paint")
    bm = bmesh.new()
    for sx in (-1, 1):
        x = 0.19 * sx
        for (y, z) in ((0.18, 0.44), (-0.13, 0.425), (0.19, 0.62)):
            cyl(bm, (x + sx * 0.008, y, z), (x + sx * 0.0135, y, z), 0.0065, segs=16)      # заклепки
        for y in (0.21, -0.21):
            rbox(bm, (x, y, 0.016), (0.026, 0.03, 0.032), 0.008, cuts=4)                   # ковпачки
    high("ch_hw", bm, "rubber")
    bm = bmesh.new()
    for sx in (-1, 1):
        x = 0.19 * sx
        for (y, z) in ((0.18, 0.44), (-0.13, 0.425), (0.19, 0.62)):
            weld(bm, [(x - sx * 0.006, y - 0.008, z + 0.008), (x - sx * 0.006, y + 0.008, z + 0.008)], r=0.0022)
    high("ch_weld", bm, "weld")
    bm = bmesh.new()
    rbox(bm, (0, -0.01, 0.445), (0.40, 0.39, 0.035), 0.013, cuts=10,
         bulge=lambda p, half: Vector((0, 0, -0.004 * max(0.0, 1 - (p.x / half.x) ** 2) * max(0.0, 1 - (p.y / half.y) ** 2)
                                       if p.z > 0 else 0.0)))
    for k in range(-3, 4):
        box(bm, (k * 0.05, -0.01, 0.424), (0.008, 0.33, 0.01))                    # ребра під сидінням
    _shell_back(bm, CHAIR_BACK, 16, 0.354, 0.012)                       # між стійками рами (труби по x = ±0,19)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    high("ch_seat", bm, "seat", subsurf=1)
    # легка — як LOD0 ring_props (трубки 6 граней): силует і пози натовпу не змінюються
    bm = bmesh.new()
    for sx in (-1, 1):
        x = 0.19 * sx
        tube(bm, [(x, 0.21, 0.03), (x, 0.18, 0.44), (x, 0.195, 0.78), (x, 0.215, 0.86)], r, segs=6)
        tube(bm, [(x, -0.21, 0.03), (x, -0.16, 0.30), (x, -0.13, 0.425)], r, segs=6)
        tube(bm, [(x * 0.97, -0.19, 0.425), (x * 0.97, 0.17, 0.425)], r, segs=6)
        for y in (0.21, -0.21):
            box(bm, (x, y, 0.012), (0.03, 0.034, 0.024))
    tube(bm, [(-0.19, -0.19, 0.09), (0.19, -0.19, 0.09)], r, segs=6)
    tube(bm, [(-0.19, 0.205, 0.12), (0.19, 0.205, 0.12)], r, segs=6)
    tube(bm, [(-0.19, 0.19, 0.62), (0.19, 0.19, 0.62)], r, segs=6)
    rbox(bm, (0, -0.01, 0.445), (0.40, 0.39, 0.037), 0.013, cuts=1)          # габарити — як у ring_props (колізії)
    _shell_back(bm, [(y_ - 0.001, z_) for (y_, z_) in CHAIR_BACK[::2]], 8, 0.356, 0.014)
    return low("chair_folding", bm)

def _h(i, salt):
    x = (i * 374761393 + salt * 668265263 + 1442695041) & 0xFFFFFFFF
    x = ((x ^ (x >> 13)) * 1274126177) & 0xFFFFFFFF
    return ((x ^ (x >> 16)) & 0xFFFF) / 65535.0

def build_canvas(ft):
    """Полотно рингу: верх платформи з прогином до центру, складками від кутів (натяг до стійок) і випадковими
    зморшками; край загнутий на подушку. Легка — плаский верх із заокругленим краєм. Нуль — рівень мату."""
    hp = ft * 0.3048 / 2
    creases = []
    for k in range(4):                                                     # від кутів до центру
        cx, cy = (1 if k in (0, 3) else -1), (1 if k < 2 else -1)
        for j in range(3):
            a = math.atan2(-cy, -cx) + (j - 1) * 0.22 + (_h(k * 3 + j, 1) - 0.5) * 0.1
            creases.append((cx * (hp - 0.25), cy * (hp - 0.25), a, 0.5 + 0.5 * _h(k * 3 + j, 2), 0.0035, 0.05))
    for k in range(14):                                                    # випадкові зморшки
        creases.append(((_h(k, 3) - 0.5) * hp * 1.6, (_h(k, 4) - 0.5) * hp * 1.6, _h(k, 5) * math.pi, 0.3 + 0.6 * _h(k, 6),
                        (0.0015 + 0.002 * _h(k, 7)) * (1 if _h(k, 8) > 0.35 else -1), 0.035 + 0.04 * _h(k, 9)))
    def wrinkle(p, half):
        if p.z < half.z - 0.002:
            return Vector((0, 0, 0))
        x, y = p.x, p.y
        edge = min(1.0, max(0.0, (hp - max(abs(x), abs(y))) / 0.12))
        z = -0.006 * (1 - (x / hp) ** 2) * (1 - (y / hp) ** 2)
        for (cx, cy, a, L, amp, w) in creases:
            dx, dy = x - cx, y - cy
            par = dx * math.cos(a) + dy * math.sin(a)
            perp = -dx * math.sin(a) + dy * math.cos(a)
            if 0 <= par <= L * 1.5 or amp < 0.003:
                z += amp * math.exp(-(perp / w) ** 2) * math.exp(-((par - L * 0.5) / L) ** 2)
        z += 0.0008 * math.sin(x * 7.3 + y * 2.1) * math.sin(y * 6.1 - x * 1.7)
        return Vector((0, 0, z * edge))
    bm = bmesh.new()
    rbox(bm, (0, 0, -0.06), (2 * hp, 2 * hp, 0.12), 0.045, cuts=200, bulge=wrinkle)
    high("canvas_top", bm, "canvas")
    bm = bmesh.new()
    rbox(bm, (0, 0, -0.06), (2 * hp + 0.004, 2 * hp + 0.004, 0.124), 0.047, cuts=4)
    bm.normal_update()
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.normal.z < -0.9], context='FACES')   # низ не видно: текстура — верху
    return low("ring_canvas_%d" % ft, bm)

# предмет: (будівник, розмір текстури, варіанти {ім'я: {зона: колір}}, дальність променя)
ASSETS = {
    "ring_post": (build_post, 1024, {"black": {}, "steel": {"paint": (0.30, 0.31, 0.33)}}, 0.02),
    "ring_pad": (build_pad, 512, {"neutral": {}}, 0.02),
    "ring_steps_100": (lambda: build_steps(1.0), 2048, {"steel": {}, "black": {"plate": "paint"}}, 0.03),
    "ring_steps_110": (lambda: build_steps(1.1), 2048, {"steel": {}, "black": {"plate": "paint"}}, 0.03),
    "ring_steps_120": (lambda: build_steps(1.2), 2048, {"steel": {}, "black": {"plate": "paint"}}, 0.03),
    "speaker_stack": (lambda: build_speaker(0.9, 0.7, 0.88, "speaker_stack"), 2048, {"black": {}}, 0.04),
    "speaker_top": (lambda: build_speaker(0.42, 0.38, 0.65, "speaker_top"), 1024, {"black": {}}, 0.04),
    "speaker_la": (lambda: build_speaker(1.1, 0.55, 0.38, "speaker_la"), 1024, {"black": {}}, 0.04),
    "stanchion": (build_stanchion, 512, {"chrome": {}}, 0.02),
    "folding_table": (build_table, 2048, {"white": {}}, 0.03),
    "ring_bell": (build_bell, 512, {"brass": {}}, 0.02),
    "laptop": (build_laptop, 1024, {"silver": {}}, 0.012),
    "chair_folding": (build_chair, 512, {"frame": {"paint": (0.06, 0.06, 0.065)}}, 0.02),
    "ring_canvas_16": (lambda: build_canvas(16), 2048, {"white": {}, "grey": {"canvas": (0.42, 0.43, 0.44)}}, 0.02),
    "ring_canvas_18": (lambda: build_canvas(18), 2048, {"white": {}, "grey": {"canvas": (0.42, 0.43, 0.44)}}, 0.02),
    "ring_canvas_20": (lambda: build_canvas(20), 2048, {"white": {}, "grey": {"canvas": (0.42, 0.43, 0.44)}}, 0.02),
}

# ---------------------------------------------------------------- запікання

def setup_cycles():
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'
    try:
        prefs = bpy.context.preferences.addons["cycles"].preferences
        for dt in ("OPTIX", "CUDA"):
            try:
                prefs.compute_device_type = dt
            except TypeError:
                continue
            prefs.get_devices()
            devs = [d for d in prefs.devices if d.type == dt]
            if devs:
                for d in prefs.devices:
                    d.use = d.type == dt
                sc.cycles.device = 'GPU'
                print("[hero] Cycles на", dt, flush=True)
                break
    except Exception as e:
        print("[hero] GPU недоступний:", e, flush=True)
    sc.cycles.samples = 96
    if sc.world is None:
        sc.world = bpy.data.worlds.new("W")
    sc.world.light_settings.distance = 0.25          # дальність AO, м

def unwrap(o, S):
    bpy.ops.object.select_all(action='DESELECT')
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(55), island_margin=6.0 / S, area_weight=0.0, scale_to_bounds=False)
    bpy.ops.uv.select_all(action='SELECT')
    try:
        bpy.ops.uv.pack_islands(rotate=True, margin=6.0 / S, shape_method='CONCAVE')
    except TypeError:
        bpy.ops.uv.pack_islands(rotate=True, margin=6.0 / S)
    bpy.ops.object.mode_set(mode='OBJECT')

def px_per_m(o, S):
    me = o.data
    uv = me.uv_layers.active.data
    wa = ua = 0.0
    for p in me.polygons:
        wa += p.area
        pts = [uv[i].uv for i in p.loop_indices]
        a = 0.0
        for i in range(1, len(pts) - 1):
            a += abs((pts[i] - pts[0]).cross(pts[i + 1] - pts[0])) / 2
        ua += a
    return S * math.sqrt(ua / max(wa, 1e-9))

def bake(lo, his, S, kind, ray, img_name):
    img = bpy.data.images.new(img_name, S, S, alpha=False, float_buffer=True)
    img.colorspace_settings.name = 'Non-Color'
    m = lo.data.materials[0] if lo.data.materials else None
    if m is None:
        m = bpy.data.materials.new("BAKE_" + lo.name)
        m.use_nodes = True
        lo.data.materials.append(m)
    nt = m.node_tree
    node = nt.nodes.get("bake_img") or nt.nodes.new("ShaderNodeTexImage")
    node.name = "bake_img"
    node.image = img
    nt.nodes.active = node
    bpy.ops.object.select_all(action='DESELECT')
    for h in his:
        h.select_set(True)
    lo.select_set(True)
    bpy.context.view_layer.objects.active = lo
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'                                   # огляд попереднього предмета міг перемкнути на EEVEE
    sc.render.bake.margin = 8
    sc.render.bake.margin_type = 'EXTEND'
    kw = dict(type=kind, use_selected_to_active=True, cage_extrusion=ray * 0.5, max_ray_distance=ray, margin=8, use_clear=True)
    if kind == 'NORMAL':
        kw["normal_space"] = 'TANGENT'
    bpy.ops.object.bake(**kw)
    a = np.empty(S * S * 4, dtype=np.float32)
    img.pixels.foreach_get(a)
    bpy.data.images.remove(img)
    return a.reshape(S, S, 4)

# ---------------------------------------------------------------- шум і складання

RNG = np.random.default_rng(20261027)

def noise(S, feat_px, beta=1.0):
    """Безшовний шум (FFT): особливості розміру ~feat_px пікселів, вихід ~[-1, 1]."""
    w = RNG.standard_normal((S, S)).astype(np.float32)
    fy = np.fft.fftfreq(S)[:, None]
    fx = np.fft.fftfreq(S)[None, :]
    f = np.sqrt(fx * fx + fy * fy) + 1e-6
    f0 = 1.0 / max(feat_px, 1.0)
    filt = np.exp(-(f / f0) ** 2) * (f / f0 + 0.05) ** (-beta * 0.5)
    n = np.real(np.fft.ifft2(np.fft.fft2(w) * filt))
    n -= n.mean()
    return (n / (np.abs(n).max() + 1e-9)).astype(np.float32)

def blur(a, r=1):
    out = a.copy()
    for _ in range(r):
        out = (out + np.roll(out, 1, 0) + np.roll(out, -1, 0) + np.roll(out, 1, 1) + np.roll(out, -1, 1)) / 5
    return out

def height_normal(h, strength):
    dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * strength
    dy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * strength
    n = np.stack([-dx, -dy, np.ones_like(h)], -1)
    return n / np.linalg.norm(n, axis=-1, keepdims=True)

def to_srgb(c):
    c = np.clip(c, 0, 1)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * np.power(c, 1 / 2.4) - 0.055)

def save_png(arr, path, srgb):
    S = arr.shape[0]
    img = bpy.data.images.new(os.path.basename(path), S, S, alpha=True, float_buffer=False)
    img.colorspace_settings.name = 'sRGB' if srgb else 'Non-Color'
    rgba = np.ones((S, S, 4), np.float32)
    rgba[..., :arr.shape[2]] = arr
    img.pixels.foreach_set(np.clip(rgba, 0, 1).ravel())
    img.filepath_raw = path
    img.file_format = 'PNG'
    img.save()
    bpy.data.images.remove(img)

def compose(name, S, nrm, ao, idmap, ppm, variants):
    """Складає з запечених карт нормаль із дрібною деталлю і для кожного варіанта — колір і маску."""
    global RNG
    RNG = np.random.default_rng(sum(ord(c_) * (k_ + 1) for k_, c_ in enumerate(name)))   # шум предмета не залежить від черги
    pal = np.array([zone_id(i) for i in range(len(ZONE_NAMES))], np.float32)
    d = ((idmap[..., None, :3] - pal[None, None]) ** 2).sum(-1)
    zi = d.argmin(-1)
    n = nrm[..., :3] * 2 - 1
    # кривизна з нормалі: опуклі ребра > 0, западини < 0
    curv = (np.roll(n[..., 0], -1, 1) - np.roll(n[..., 0], 1, 1)) + (np.roll(n[..., 1], -1, 0) - np.roll(n[..., 1], 1, 0))
    curv = blur(curv, max(1, int(round(ppm * 0.0015))))             # ширина ребра ~1,5–3 мм
    edge = np.clip(curv * 3.0, 0, 1)
    cav = np.clip((0.9 - ao) / 0.55, 0, 1)
    mm = lambda m_: ppm * m_                       # метри → пікселі
    n_wear = noise(S, mm(0.012))
    n_low = noise(S, mm(0.08))
    n_dirt = noise(S, mm(0.03))
    # дрібна деталь у нормаль
    det = np.zeros((S, S), np.float32)
    strength = np.zeros((S, S), np.float32)
    peel = noise(S, max(1.2, mm(0.004)))
    grain = noise(S, max(1.0, mm(0.0012)))
    spang = np.round(noise(S, max(2.0, mm(0.015))) * 3) / 3 * 0.6 + noise(S, max(1.0, mm(0.002))) * 0.4
    brush = blur(np.repeat(noise(S, 1.5)[:, :1], S, 1) * 0.3 + noise(S, 1.0) * 0.1, 1)
    weave = (np.sin(np.arange(S)[None, :] * 2 * math.pi / max(3, mm(0.002))) * np.sin(np.arange(S)[:, None] * 2 * math.pi / max(3, mm(0.002))))
    for i, z in enumerate(ZONE_NAMES):
        sel = zi == i
        kind = ZONES[z][4]
        src, st = {"peel": (peel, 0.12), "grain": (grain, 0.1), "spangle": (spang, 0.2), "brushed": (brush, 0.3),
                   "weave": (weave.astype(np.float32), 0.6)}.get(kind, (None, 0.0))
        if src is not None:
            det[sel] = src[sel]
            strength[sel] = st
    dn = height_normal(det, 1.0)
    dn[..., :2] *= strength[..., None]
    out_n = np.concatenate([n[..., :2] + dn[..., :2], n[..., 2:3]], -1)
    out_n /= np.linalg.norm(out_n, axis=-1, keepdims=True)
    save_png(out_n * 0.5 + 0.5, os.path.join(TEX, "hero_%s_normal.png" % name), False)
    for var, over in variants.items():
        alb = np.zeros((S, S, 3), np.float32)
        metal = np.zeros((S, S), np.float32)
        rough = np.zeros((S, S), np.float32)
        for i, z in enumerate(ZONE_NAMES):
            sel = zi == i
            if not sel.any():
                continue
            col, met, rgh, wear, _ = ZONES[z]
            ov = over.get(z)
            if isinstance(ov, str):                                       # зона «перефарбована» в іншу
                col, met, rgh, wear, _ = ZONES[ov]
            elif ov is not None:
                col = ov
            c = np.array(col, np.float32)[None, :] * (1 + 0.07 * n_low[sel])[:, None]
            m_ = np.full(sel.sum(), met, np.float32)
            r_ = np.full(sel.sum(), rgh, np.float32) + 0.05 * n_low[sel]
            e = edge[sel]
            if wear == "chip":                                            # сколи фарби: сталь на ребрах
                chip = np.clip((e * 1.3 + n_wear[sel] * 0.55 - 0.55) * 4, 0, 1)
                rim = np.clip((e * 1.3 + n_wear[sel] * 0.55 - 0.42) * 4, 0, 1) - chip
                c = c * (1 - chip[:, None]) + np.array(STEEL_BARE)[None] * chip[:, None]
                c = c + 0.05 * rim[:, None]
                m_ = m_ * (1 - chip) + chip
                r_ = r_ * (1 - chip) + 0.35 * chip
            elif wear == "scuff":
                s_ = np.clip(e * 1.2 + n_wear[sel] * 0.3 - 0.2, 0, 1)
                c = c * (1 + 0.25 * s_[:, None])
                r_ = r_ + 0.25 * s_
            elif wear == "grime":                                         # затерті плями, бруд від ніг
                spots = np.clip((n_wear[sel] * 0.9 + n_low[sel] * 0.7 - 0.3) * 2.5, 0, 1)
                c = c * (1 - 0.16 * np.clip(n_dirt[sel] + 0.4, 0, 1))[:, None] * (1 - 0.22 * spots)[:, None]
                r_ = r_ - 0.08 * spots
            elif wear == "polish":
                p_ = np.clip(e * 1.5, 0, 1)
                c = c * (1 + 0.25 * p_[:, None])
                r_ = r_ - 0.18 * p_
            g = cav[sel] * (0.6 + 0.4 * np.clip(n_dirt[sel] + 0.5, 0, 1))  # бруд у западинах
            c = c * (1 - 0.45 * g[:, None])
            r_ = r_ + 0.2 * g
            c = c * (0.55 + 0.45 * ao[sel])[:, None]                      # частина AO — у колір (мобільні)
            alb[sel] = c
            metal[sel] = m_
            rough[sel] = np.clip(r_, 0.05, 1)
        save_png(to_srgb(alb), os.path.join(TEX, "hero_%s_%s_albedo.png" % (name, var)), True)
        tint = np.isin(zi, [ZONE_NAMES.index(z_) for z_ in TINT_ZONES]).astype(np.float32)
        save_png(np.stack([metal, ao, tint, 1 - rough], -1), os.path.join(TEX, "hero_%s_%s_mask.png" % (name, var)), False)

# ---------------------------------------------------------------- матеріал з текстур (огляд і генератор арен)

def hero_material(name, var, tint=None):
    """Матеріал легкої моделі з запечених текстур; tint — множник кольору (для нейтральних вінілових подушок)."""
    mn = "Hero_%s_%s" % (name, var) + ("" if tint is None else "_t")
    m = bpy.data.materials.get(mn)
    if m:
        return m
    m = bpy.data.materials.new(mn)
    m.use_nodes = True
    nt = m.node_tree
    b = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    def tex(fn, cs):
        t = nt.nodes.new("ShaderNodeTexImage")
        t.image = bpy.data.images.load(os.path.join(TEX, fn), check_existing=True)
        t.image.colorspace_settings.name = cs
        return t
    ta = tex("hero_%s_%s_albedo.png" % (name, var), 'sRGB')
    tm = tex("hero_%s_%s_mask.png" % (name, var), 'Non-Color')
    tn = tex("hero_%s_normal.png" % name, 'Non-Color')
    col = ta.outputs["Color"]
    if tint is not None:
        mix = nt.nodes.new("ShaderNodeMix")
        mix.data_type, mix.blend_type = 'RGBA', 'MULTIPLY'
        mix.inputs["Factor"].default_value = 1.0
        mix.inputs[7].default_value = (*tint, 1)
        nt.links.new(col, mix.inputs[6])
        col = mix.outputs[2]
    nt.links.new(col, b.inputs["Base Color"])
    sep = nt.nodes.new("ShaderNodeSeparateColor")
    nt.links.new(tm.outputs["Color"], sep.inputs["Color"])
    nt.links.new(sep.outputs[0], b.inputs["Metallic"])
    inv = nt.nodes.new("ShaderNodeMath")
    inv.operation = 'SUBTRACT'
    inv.inputs[0].default_value = 1.0
    nt.links.new(tm.outputs["Alpha"], inv.inputs[1])
    nt.links.new(inv.outputs[0], b.inputs["Roughness"])
    nm = nt.nodes.new("ShaderNodeNormalMap")
    nt.links.new(tn.outputs["Color"], nm.inputs["Color"])
    nt.links.new(nm.outputs["Normal"], b.inputs["Normal"])
    return m

# ---------------------------------------------------------------- огляд

def preview(name, lo, his, variants):
    sc = bpy.context.scene
    for o in list(bpy.data.objects):
        if o.type in ('CAMERA', 'LIGHT'):
            bpy.data.objects.remove(o)
    var = list(variants)[0]
    show = []
    for k, (vn, _) in enumerate(variants.items()):
        o = lo.copy()
        o.data = lo.data.copy()
        o.data.materials.clear()
        o.data.materials.append(hero_material(name, vn, tint=(0.08, 0.1, 0.4) if name == "ring_pad" else None))
        bpy.context.scene.collection.objects.link(o)
        show.append(o)
    bb = [lo.matrix_world @ Vector(c) for c in lo.bound_box]
    mn = Vector((min(p.x for p in bb), min(p.y for p in bb), min(p.z for p in bb)))
    mx = Vector((max(p.x for p in bb), max(p.y for p in bb), max(p.z for p in bb)))
    size = max(mx - mn)
    step = (mx.y - mn.y) + size * 0.25
    for h in his:
        h.location.y -= step
        for s_ in h.material_slots:
            pass
    for k, o in enumerate(show):
        o.location.y += step * k
    lo_y, hi_y = mn.y - step, mx.y + step * (len(show) - 1)
    ctr = Vector(((mn.x + mx.x) / 2, (lo_y + hi_y) / 2, (mn.z + mx.z) / 2))
    rad = (Vector((mx.x - mn.x, hi_y - lo_y, mx.z - mn.z))).length / 2
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    sc.collection.objects.link(cam)
    eye = ctr + Vector((-1.0, -0.35, 0.45)).normalized() * rad / math.sin(math.radians(15))
    cam.location = eye
    cam.rotation_euler = (ctr - eye).to_track_quat('-Z', 'Y').to_euler()
    cam.data.lens = 50
    sc.camera = cam
    for (loc, en, sz) in (((-2, -3, 3), 300, 2), ((3, -1, 2), 120, 3), ((0, 4, 3), 200, 2)):
        L = bpy.data.objects.new("l", bpy.data.lights.new("l", 'AREA'))
        L.data.energy, L.data.size = en * size, sz * size
        L.location = ctr + Vector(loc) * size
        L.rotation_euler = (ctr - L.location).to_track_quat('-Z', 'Y').to_euler()
        sc.collection.objects.link(L)
    sc.world.use_nodes = True
    bg = next(n for n in sc.world.node_tree.nodes if n.type == "BACKGROUND")
    bg.inputs[0].default_value = (0.05, 0.05, 0.06, 1)
    for h in his:
        for ms in h.material_slots:
            b = next(n for n in ms.material.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
            b.inputs["Emission Strength"].default_value = 0.0
    sc.render.engine = 'BLENDER_EEVEE_NEXT'
    sc.render.resolution_x, sc.render.resolution_y = 1600, 900
    sc.view_settings.view_transform = 'AgX'
    os.makedirs(LOGS, exist_ok=True)
    sc.render.filepath = os.path.join(LOGS, name + "_preview.png")
    bpy.ops.render.render(write_still=True)
    for o in show:
        bpy.data.objects.remove(o)
    for h in his:
        h.location.y += step

# ---------------------------------------------------------------- головний цикл

def main():
    global HIGH, LOWC
    os.makedirs(TEX, exist_ok=True)
    os.makedirs(LOGS, exist_ok=True)
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o)
    setup_cycles()
    names = WANT or list(ASSETS)
    lows = {}
    manifest = []
    lib_path = os.path.join(LOGS, "hero_lib.blend")
    if os.path.exists(lib_path):                      # уже запечені — лишаються в бібліотеці
        with bpy.data.libraries.load(lib_path) as (src, dst):
            dst.meshes = [m for m in src.meshes if m[5:] in ASSETS and m[5:] not in names]
        for me in dst.meshes:
            if me is not None:
                lows[me.name[5:]] = me
    for name in names:
        builder, S, variants, ray = ASSETS[name]
        HIGH = coll("high_" + name)
        LOWC = coll("low_" + name)
        t0 = time.time()
        lo = builder()
        print("[hero]   побудова %.0f с" % (time.time() - t0), flush=True)
        his = list(HIGH.objects)
        unwrap(lo, S)
        ppm = px_per_m(lo, S)
        print("[hero] %s: детальна %d трик., легка %d трик., %d px/м" % (
            name, sum(len(p.vertices) - 2 for h in his for p in h.evaluated_get(bpy.context.evaluated_depsgraph_get()).data.polygons),
            sum(len(p.vertices) - 2 for p in lo.data.polygons), ppm), flush=True)
        t0 = time.time()
        nrm = bake(lo, his, S, 'NORMAL', ray, "n")
        print("[hero]   нормаль %.0f с" % (time.time() - t0), flush=True)
        ao = bake(lo, his, S, 'AO', ray, "ao")[..., 0]
        for h in his:
            for ms in h.material_slots:
                next(n for n in ms.material.node_tree.nodes if n.type == "BSDF_PRINCIPLED").inputs["Emission Strength"].default_value = 1.0
        ids = bake(lo, his, S, 'EMIT', ray, "id")
        for h in his:
            for ms in h.material_slots:
                next(n for n in ms.material.node_tree.nodes if n.type == "BSDF_PRINCIPLED").inputs["Emission Strength"].default_value = 0.0
        print("[hero]   AO і зони %.0f с" % (time.time() - t0), flush=True)
        compose(name, S, nrm, ao, ids, ppm, variants)
        print("[hero]   складання %.0f с" % (time.time() - t0), flush=True)
        lo.data.materials.clear()
        if PREVIEW:
            preview(name, lo, his, variants)
        lows[name] = me_ = lo.data
        me_.use_fake_user = True
        bpy.data.objects.remove(lo)                                   # не заважає огляду наступного предмета
        manifest.append("hero %s %d %d %s" % (name, sum(len(p.vertices) - 2 for p in me_.polygons), S, " ".join(variants)))
        for h in his:
            bpy.data.objects.remove(h)
    # бібліотека легких сіток і FBX
    for nm, me in lows.items():
        me.name = "hero_" + nm
        me.use_fake_user = True
    bpy.data.libraries.write(lib_path, set(lows.values()), fake_user=True)
    col = coll("export")
    obs = []
    for k, (nm, me) in enumerate(sorted(lows.items())):
        o = bpy.data.objects.new("hero_" + nm, me)
        col.objects.link(o)
        o.location = (k * 2.0, 0, 0)
        obs.append(o)
    bpy.ops.object.select_all(action='DESELECT')
    for o in obs:
        o.select_set(True)
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, "hero.fbx"), use_selection=True, apply_unit_scale=True,
                             apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y', object_types={'MESH'},
                             mesh_smooth_type='OFF', use_tspace=True, path_mode='RELATIVE', embed_textures=False,
                             bake_space_transform=False)
    old = {}
    mf = os.path.join(OUT, "hero_manifest.txt")
    if os.path.exists(mf):
        for line in open(mf, encoding="utf-8"):
            if line.startswith("hero "):
                old[line.split()[1]] = line.rstrip("\n")
    for line in manifest:
        old[line.split()[1]] = line
    with open(mf, "w", encoding="utf-8", newline="\n") as f:
        f.write("# Героїчні предмети (tools/blender/ring_hero.py): hero <ім'я> <трикутників> <розмір текстур> <варіанти…>\n")
        for k in sorted(old):
            f.write(old[k] + "\n")
    print("[hero] готово:", ", ".join(names), flush=True)

main()
