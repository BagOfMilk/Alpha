# Генератор моделей світу Alpha для Blender (трек V, Поправка №18).
#
# Запуск у Blender (Scripting або MCP):
#   exec(open(r"<repo>/tools/blender/alpha_kit.py", encoding="utf-8").read())
#   build_all()            # усі будівлі, споруди й реквізит
#   export_all(r"<repo>/Assets/Art")
#
# Геометрія будується кодом і детермінована (жодного random: розкид — хеш від індексу),
# тож перезбирання дає той самий результат. Текстури — Poly Haven (CC0): матеріал частини
# береться за назвою з PH_MATERIALS, якщо його вже імпортовано в .blend (import_asset),
# інакше — сірий заступник з тією ж назвою частини.
# Масштаб — 1 одиниця = 1 м, Z угору, фасад (двері) дивиться в -Y (docs/ART_BIBLE.md §2).
# Кожна будівля має п'ять стадій будівництва — дочірні порожні об'єкти stage1..stage5
# (риштування → стіни → дах), щоб гра перемикала їх, а не масштабувала модель.

import bpy, bmesh, math
from mathutils import Vector, Matrix

# Частина моделі -> (матеріал Poly Haven, реальний розмір текстури в м для UV).
PH_MATERIALS = {
    "roof":       ("reed_roof_04",       2.5),
    "shingle":    ("roof_planks",        1.5),
    "walls":      ("white_stucco",       2.0),
    "logs":       ("weathered_planks",   2.0),
    "timber":     ("dark_wooden_planks", 2.0),
    "foundation": ("stone_wall_04",      1.7),
    "cloth":      ("fabric_pattern_05",  0.5),
    "metal":      ("rust_coarse_01",     2.2),
    "glass":      ("M_Window",           1.0),
    "bark":       ("M_FirBark",          1.5),   # кора з fir_tree_01 (Poly Haven)
    "needles":    ("M_FirTwig",          1.0),   # картки хвої: UV 0..1 на картку, альфа-зріз
    "canvas":     ("kit_linen",          1.2),   # нейтральний льон набору (Textures/Kit), колір — у матеріалі
    "bedroll":    ("kit_wool",           0.8),
}

# Частини, у яких UV задає сам генератор (картки), а не трипланарна проєкція.
CARD_PARTS = {"needles", "grass"}

COL = "Alpha_Buildings"

# ---------------------------------------------------------------- службове

def _hash01(i, salt=0):
    x = (i * 374761393 + salt * 668265263 + 1442695041) & 0xFFFFFFFF
    x = ((x ^ (x >> 13)) * 1274126177) & 0xFFFFFFFF
    return ((x ^ (x >> 16)) & 0xFFFF) / 65535.0

def collection(name=COL):
    col = bpy.data.collections.get(name)
    if col is None:
        col = bpy.data.collections.new(name)
        bpy.context.scene.collection.children.link(col)
    return col

def _material(name):
    m = bpy.data.materials.get(name)
    if m is None:
        m = bpy.data.materials.new(name)
        m.use_nodes = True
        # Свіжа сцена без текстур Poly Haven: колір — білий множник, текстуру за назвою матеріалу дає Unity.
        b0 = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
        b0.inputs["Base Color"].default_value = (1, 1, 1, 1)
        if name == "M_Window":
            b = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
            b.inputs["Base Color"].default_value = (0.02, 0.025, 0.03, 1)
            b.inputs["Roughness"].default_value = 0.25
    return m

def _remove_tree(name):
    ob = bpy.data.objects.get(name)
    if ob is None:
        return
    for c in list(ob.children_recursive):
        bpy.data.objects.remove(c, do_unlink=True)
    bpy.data.objects.remove(ob, do_unlink=True)

def _empty(name, parent=None, col=None):
    e = bpy.data.objects.new(name, None)
    (col or collection()).objects.link(e)
    e.parent = parent
    return e

class Mesh:
    """Накопичувач геометрії однієї частини (одного матеріалу)."""
    def __init__(self):
        self.bm = bmesh.new()

    def box(self, c, s, rz=0.0):
        r = bmesh.ops.create_cube(self.bm, size=1.0)
        bmesh.ops.scale(self.bm, vec=s, verts=r["verts"])
        if rz:
            bmesh.ops.rotate(self.bm, cent=(0, 0, 0), matrix=Matrix.Rotation(rz, 3, 'Z'), verts=r["verts"])
        bmesh.ops.translate(self.bm, vec=c, verts=r["verts"])
        return r["verts"]

    def cyl(self, p0, p1, r0, r1=None, segs=10, caps=True):
        p0, p1 = Vector(p0), Vector(p1)
        d = p1 - p0
        res = bmesh.ops.create_cone(self.bm, cap_ends=caps, segments=segs, radius1=r0,
                                    radius2=r0 if r1 is None else r1, depth=d.length)
        rot = d.to_track_quat('Z', 'Y').to_matrix().to_4x4()
        bmesh.ops.transform(self.bm, matrix=rot, verts=res["verts"])
        bmesh.ops.translate(self.bm, vec=p0 + d / 2, verts=res["verts"])
        return res["verts"]

    def poly(self, pts):
        vs = [self.bm.verts.new(p) for p in pts]
        return self.bm.faces.new(vs)

    def pyramid(self, cx, cy, z, hw, hd, rise, ridge=0.0, sides=4):
        """Шатровий/чотирисхилий дах (ridge > 0 — гребінь уздовж X) або n-гранний шатро."""
        if sides != 4:
            ring = [self.bm.verts.new((cx + hw * math.cos(2 * math.pi * k / sides + math.pi / sides),
                                       cy + hw * math.sin(2 * math.pi * k / sides + math.pi / sides), z))
                    for k in range(sides)]
            top = self.bm.verts.new((cx, cy, z + rise))
            for k in range(sides):
                self.bm.faces.new((ring[k], ring[(k + 1) % sides], top))
            return
        v = [self.bm.verts.new(p) for p in [(cx - hw, cy - hd, z), (cx + hw, cy - hd, z), (cx + hw, cy + hd, z),
                                            (cx - hw, cy + hd, z), (cx - ridge, cy, z + rise), (cx + ridge, cy, z + rise)]]
        if ridge > 1e-4:
            self.bm.faces.new((v[0], v[1], v[5], v[4])); self.bm.faces.new((v[2], v[3], v[4], v[5]))
            self.bm.faces.new((v[1], v[2], v[5])); self.bm.faces.new((v[3], v[0], v[4]))
        else:
            top = v[4]
            for a, b in ((0, 1), (1, 2), (2, 3), (3, 0)):
                self.bm.faces.new((v[a], v[b], top))

    def gable(self, cx, cy, z, hw, hd, rise):
        """Двосхилий дах: гребінь уздовж X, схили в ±Y."""
        v = [self.bm.verts.new(p) for p in [(cx - hw, cy - hd, z), (cx + hw, cy - hd, z), (cx + hw, cy + hd, z),
                                            (cx - hw, cy + hd, z), (cx - hw, cy, z + rise), (cx + hw, cy, z + rise)]]
        self.bm.faces.new((v[0], v[1], v[5], v[4])); self.bm.faces.new((v[2], v[3], v[4], v[5]))

    def to_object(self, name, parent, part, col=None):
        if not self.bm.verts:
            self.bm.free(); return None
        me = bpy.data.meshes.new(name)
        self.bm.to_mesh(me); self.bm.free()
        ob = bpy.data.objects.new(name, me)
        (col or collection()).objects.link(ob)
        ob.parent = parent
        ob["part"] = part
        return ob

def _apply_mods(ob):
    with bpy.context.temp_override(object=ob, active_object=ob, selected_objects=[ob]):
        for m in list(ob.modifiers):
            bpy.ops.object.modifier_apply(modifier=m.name)

def _solidify(ob, t, offset=-1.0, bevel=0.0):
    so = ob.modifiers.new("thick", 'SOLIDIFY'); so.thickness = t; so.offset = offset
    if bevel:
        bv = ob.modifiers.new("soft", 'BEVEL'); bv.width = bevel; bv.segments = 2
    _apply_mods(ob)

def _cut(ob, boxes):
    """boxes: [(центр, розмір)] — отвори дверей і вікон."""
    if ob is None or not boxes:
        return
    cutters = []
    for c, s in boxes:
        m = Mesh(); m.box(c, s)
        cutters.append(m.to_object(ob.name + "_cut", None, "cut"))
    for c in cutters:
        mod = ob.modifiers.new("cut", 'BOOLEAN'); mod.operation = 'DIFFERENCE'; mod.solver = 'EXACT'; mod.object = c
    _apply_mods(ob)
    for c in cutters:
        bpy.data.objects.remove(c, do_unlink=True)

def finish(ob):
    """UV у реальному масштабі текстури (трипланарна проєкція у світових координатах) + матеріал."""
    if ob is None:
        return
    part = ob.get("part", "timber")
    mat_name, tex = PH_MATERIALS.get(part, ("M_" + part, 1.0))
    mat = bpy.data.materials.get(mat_name) or _material(mat_name)
    ob.data.materials.clear(); ob.data.materials.append(mat)
    if part in CARD_PARTS:
        return
    bm = bmesh.new(); bm.from_mesh(ob.data)
    uv = bm.loops.layers.uv.verify()
    mw = ob.matrix_world
    for f in bm.faces:
        n = f.normal
        ax = max(range(3), key=lambda k: abs(n[k]))
        for l in f.loops:
            co = mw @ l.vert.co
            u, v = {0: (co.y, co.z), 1: (co.x, co.z), 2: (co.x, co.y)}[ax]
            l[uv].uv = (u / tex, v / tex)
    bm.to_mesh(ob.data); bm.free()
    smooth = part in ("logs",)
    for p in ob.data.polygons:
        p.use_smooth = smooth

# ---------------------------------------------------------------- деталі

def _door(trim, x, y, base, w=1.0, h=2.0, face=-1):
    trim.box((x, y - face * 0.04, base + h / 2), (w - 0.06, 0.06, h))
    trim.box((x, y + face * 0.08, base + h + 0.08), (w + 0.3, 0.2, 0.16))
    for s in (-1, 1):
        trim.box((x + s * (w / 2 + 0.06), y + face * 0.08, base + h / 2), (0.12, 0.2, h + 0.1))
    for k in (0.3, 0.7):  # поперечки на полотні
        trim.box((x, y + face * 0.0, base + h * k), (w - 0.1, 0.1, 0.1))

def _window(trim, glass, x, y, zc, w=0.75, h=0.75, face=-1, shutters=True):
    glass.box((x, y - face * 0.12, zc), (w, 0.02, h))
    trim.box((x, y + face * 0.06, zc - h / 2 - 0.04), (w + 0.2, 0.14, 0.08))
    trim.box((x, y + face * 0.06, zc + h / 2 + 0.04), (w + 0.2, 0.12, 0.08))
    trim.box((x, y + face * 0.06, zc), (0.06, 0.06, h))
    if shutters:
        for s in (-1, 1):
            trim.box((x + s * (w / 2 + 0.25), y + face * 0.1, zc), (0.45, 0.04, h + 0.05))

def _window_x(trim, glass, x, y, zc, face, w=0.75, h=0.75):
    """Вікно в бічній стіні (нормаль ±X)."""
    glass.box((x - face * 0.12, y, zc), (0.02, w, h))
    trim.box((x + face * 0.06, y, zc - h / 2 - 0.04), (0.14, w + 0.2, 0.08))
    trim.box((x + face * 0.06, y, zc + h / 2 + 0.04), (0.12, w + 0.2, 0.08))

def _log_walls(m, w, d, base, h, r=0.14, salt=0):
    n = max(2, int(h / (2 * r * 0.92)))
    for i in range(n):
        z = base + r + i * 2 * r * 0.92
        ext = 0.26 + 0.08 * _hash01(i, salt)
        rr = r * (0.94 + 0.12 * _hash01(i, salt + 7))
        if i % 2 == 0:
            for y in (-d / 2, d / 2):
                m.cyl((-w / 2 - ext, y, z), (w / 2 + ext, y, z), rr)
        else:
            for x in (-w / 2, w / 2):
                m.cyl((x, -d / 2 - ext, z), (x, d / 2 + ext, z), rr)
    return base + r + (n - 1) * 2 * r * 0.92 + r

def _plaster_walls(m, w, d, base, h, t=0.3):
    m.box((0, -d / 2 + t / 2, base + h / 2), (w, t, h))
    m.box((0, d / 2 - t / 2, base + h / 2), (w, t, h))
    m.box((-w / 2 + t / 2, 0, base + h / 2), (t, d - 2 * t, h))
    m.box((w / 2 - t / 2, 0, base + h / 2), (t, d - 2 * t, h))

def _gable_ends(m, w, d, z, rise, inset=0.0):
    """Трикутні фронтони під двосхилим дахом (на торцях ±X)."""
    for x in (-w / 2 + inset, w / 2 - inset):
        f = m.poly([(x, -d / 2, z), (x, d / 2, z), (x, 0, z + rise * 0.98)])
        ext = bmesh.ops.extrude_face_region(m.bm, geom=[f])
        nv = [e for e in ext["geom"] if isinstance(e, bmesh.types.BMVert)]
        bmesh.ops.translate(m.bm, vec=(0.12 if x > 0 else -0.12, 0, 0), verts=nv)

def _porch(timber, roofm, w, d, base, h, depth=1.6, roof_part=None):
    """Ґанок уздовж фасаду: стовпи, поміст, односхилий дашок."""
    y0 = -d / 2 - depth
    timber.box((0, -d / 2 - depth / 2, base - 0.05), (w * 0.8, depth, 0.1))
    n = max(2, int(w * 0.8 / 1.8) + 1)
    for i in range(n):
        x = -w * 0.4 + i * (w * 0.8) / (n - 1)
        timber.box((x, y0 + 0.12, base + h / 2), (0.16, 0.16, h))
    timber.box((0, y0 + 0.12, base + h), (w * 0.8 + 0.2, 0.18, 0.18))
    roofm.poly([(-w * 0.45, y0 - 0.25, base + h + 0.05), (w * 0.45, y0 - 0.25, base + h + 0.05),
                (w * 0.45, -d / 2, base + h + 0.7), (-w * 0.45, -d / 2, base + h + 0.7)])

def _chimney(stone, x, y, z0, z1, s=0.6):
    stone.box((x, y, (z0 + z1) / 2), (s, s, z1 - z0))
    stone.box((x, y, z1 + 0.06), (s + 0.12, s + 0.12, 0.12))

def _rafters(timber, w, d, z, rise, kind="hip", over=0.5, step=0.9):
    """Голі крокви — стадія 4 (стіни є, даху ще немає)."""
    hw, hd = w / 2 + over * 0.6, d / 2 + over * 0.6
    n = max(2, int(w / step))
    for i in range(n + 1):
        x = -w / 2 + i * w / n
        for s in (-1, 1):
            timber.cyl((x, s * hd, z), (x, 0, z + rise), 0.06, segs=6)
    timber.cyl((-w / 2, 0, z + rise), (w / 2, 0, z + rise), 0.08, segs=6)

def _scaffold(timber, w, d, base, h):
    """Риштування навколо стін — стадії 2–3."""
    pts = [(-w / 2 - 0.6, -d / 2 - 0.6), (w / 2 + 0.6, -d / 2 - 0.6), (w / 2 + 0.6, d / 2 + 0.6), (-w / 2 - 0.6, d / 2 + 0.6)]
    for (x, y) in pts:
        timber.cyl((x, y, 0), (x, y, base + h + 0.4), 0.05, segs=6)
    for k in (0.45, 0.9):
        z = base + h * k
        for a, b in ((0, 1), (1, 2), (2, 3), (3, 0)):
            timber.cyl((*pts[a], z), (*pts[b], z), 0.04, segs=6)
    timber.box((0, -d / 2 - 0.6, base + h * 0.45 + 0.05), (w + 1.2, 0.5, 0.05))

def _bisect_copy(src, zmax, name, parent):
    """Копія частини, обрізана по висоті zmax (стадії будівництва)."""
    me = src.data.copy()
    ob = bpy.data.objects.new(name, me)
    collection().objects.link(ob)
    ob.parent = parent
    ob["part"] = src.get("part")
    bm = bmesh.new(); bm.from_mesh(me)
    geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
    bmesh.ops.bisect_plane(bm, geom=geom, plane_co=(0, 0, zmax), plane_no=(0, 0, 1), clear_outer=True)
    bm.to_mesh(me); bm.free()
    return ob

# ---------------------------------------------------------------- будівлі

def house(name, w=6.0, d=4.5, h=2.4, style="plaster", roof="hip", rise=2.6, over=0.7,
          doors=(0.0,), windows=(-1.8, 1.8), side_windows=False, porch=False, chimney=None,
          roof_part="roof", origin=(0.0, 0.0), extra=None):
    """Будинок: цоколь, стіни (мазанка / зруб), дах (чотирисхилий / двосхилий), двері, вікна,
    за бажанням ґанок і комин; extra(root, base, top) — дорисувати особливе (вивіска, прибудова)."""
    _remove_tree(name)
    root = _empty(name)
    stages = [_empty(f"{name}.stage{k}", root) for k in range(1, 6)]
    base = 0.35
    full = _empty(f"{name}.parts", None)  # тимчасовий батько повних частин

    found = Mesh(); found.box((0, 0, base / 2), (w + 0.3, d + 0.3, base))
    walls, timber, trim, glass, roofm, stone = Mesh(), Mesh(), Mesh(), Mesh(), Mesh(), Mesh()
    if style == "plaster":
        _plaster_walls(walls, w, d, base, h)
        for (sx, sy, x, y) in ((w + 0.1, 0.16, 0, -d / 2), (w + 0.1, 0.16, 0, d / 2),
                               (0.16, d + 0.1, -w / 2, 0), (0.16, d + 0.1, w / 2, 0)):
            timber.box((x, y, base + h - 0.08), (sx, sy, 0.16))
        top = base + h
    else:
        top = _log_walls(walls, w, d, base, h, salt=len(name))
    cuts = [((x, -d / 2, base + 1.0), (1.0, 1.2, 2.0)) for x in doors]
    cuts += [((x, -d / 2, base + 1.35), (0.75, 1.2, 0.75)) for x in windows]
    if side_windows:
        cuts += [((sx * w / 2, 0, base + 1.35), (1.2, 0.75, 0.75)) for sx in (-1, 1)]
    for x in doors:
        _door(trim, x, -d / 2, base)
    for x in windows:
        _window(trim, glass, x, -d / 2, base + 1.35)
    if side_windows:
        for sx in (-1, 1):
            _window_x(trim, glass, sx * w / 2, 0, base + 1.35, sx)

    hw, hd = w / 2 + over, d / 2 + over
    if roof == "hip":
        roofm.pyramid(0, 0, top, hw, hd, rise, ridge=max(0.0, hw - hd))
    else:
        roofm.gable(0, 0, top, hw, hd, rise)
        _gable_ends(walls if style == "plaster" else timber, w, d, top, rise * (d / 2) / hd)
    if porch:
        _porch(timber, roofm, w, d, base, h)
    if chimney is not None:
        cx, cy = chimney
        _chimney(stone, cx, cy, top - 0.5, top + rise * 0.85)

    parts = {}
    parts["foundation"] = found.to_object(f"{name}.foundation", full, "foundation")
    parts["walls"] = walls.to_object(f"{name}.walls", full, "walls" if style == "plaster" else "logs")
    _cut(parts["walls"], cuts)
    parts["timber"] = timber.to_object(f"{name}.timber", full, "timber")
    parts["trim"] = trim.to_object(f"{name}.trim", full, "timber")
    parts["glass"] = glass.to_object(f"{name}.glass", full, "glass")
    parts["stone"] = stone.to_object(f"{name}.chimney", full, "foundation")
    r = roofm.to_object(f"{name}.roof", full, roof_part)
    _solidify(r, 0.32 if roof_part == "roof" else 0.12, bevel=0.06 if roof_part == "roof" else 0.0)
    parts["roof"] = r
    if extra:
        for k, ob in (extra(full, base, top) or {}).items():
            parts[k] = ob
    _make_stages(name, stages, parts, w, d, base, top, rise, h)
    bpy.data.objects.remove(full, do_unlink=True)
    root.location = (origin[0], origin[1], 0)
    return root

def _make_stages(name, stages, parts, w, d, base, top, rise, h):
    """stage1 — розмічений цоколь; 2 — третина стін + риштування; 3 — дві третини + риштування;
    4 — стіни повністю + голі крокви; 5 — готова будівля."""
    s1, s2, s3, s4, s5 = stages
    for k, ob in parts.items():
        if ob is None:
            continue
        ob.parent = s5
        finish(ob)
    f = parts["foundation"]
    for idx, st in enumerate((s1, s2, s3, s4)):
        c = bpy.data.objects.new(f"{name}.s{idx + 1}.foundation", f.data.copy())
        collection().objects.link(c); c.parent = st
    wall = parts.get("walls")
    for st, frac, tag in ((s2, 0.35, "s2"), (s3, 0.7, "s3")):
        if wall is not None:
            finish(_bisect_copy(wall, base + (top - base) * frac, f"{name}.{tag}.walls", st))
        sc = Mesh(); _scaffold(sc, w, d, base, top - base)
        finish(sc.to_object(f"{name}.{tag}.scaffold", st, "timber"))
    for k in ("walls", "timber", "trim", "glass"):
        ob = parts.get(k)
        if ob is not None:
            c = bpy.data.objects.new(f"{name}.s4.{k}", ob.data.copy()); collection().objects.link(c); c.parent = s4
    rf = Mesh(); _rafters(rf, w, d, top, rise)
    finish(rf.to_object(f"{name}.s4.rafters", s4, "timber"))
    stakes = Mesh()
    for (x, y) in ((-w / 2, -d / 2), (w / 2, -d / 2), (w / 2, d / 2), (-w / 2, d / 2)):
        stakes.cyl((x, y, 0), (x, y, 0.8), 0.04, 0.02, segs=6)
    finish(stakes.to_object(f"{name}.s1.stakes", s1, "timber"))

def watchtower(name="watch", origin=(0, 0), s=3.0, hplat=4.5):
    """Сторожа: вежа на чотирьох стовпах, поміст з поручнями, шатровий дах, драбина."""
    _remove_tree(name)
    root = _empty(name); stages = [_empty(f"{name}.stage{k}", root) for k in range(1, 6)]
    full = _empty(f"{name}.parts")
    found, logs, timber, roofm = Mesh(), Mesh(), Mesh(), Mesh()
    for (x, y) in ((-s / 2, -s / 2), (s / 2, -s / 2), (s / 2, s / 2), (-s / 2, s / 2)):
        found.box((x, y, 0.2), (0.6, 0.6, 0.4))
        logs.cyl((x, y, 0.2), (x * 0.85, y * 0.85, hplat + 2.4), 0.16, 0.13)
    for z in (1.6, 3.1):  # розпірки
        for a, b in (((-1, -1), (1, -1)), ((1, -1), (1, 1)), ((1, 1), (-1, 1)), ((-1, 1), (-1, -1))):
            logs.cyl((a[0] * s / 2 * 0.95, a[1] * s / 2 * 0.95, z), (b[0] * s / 2 * 0.95, b[1] * s / 2 * 0.95, z + 0.8), 0.07, segs=8)
    timber.box((0, 0, hplat), (s + 0.8, s + 0.8, 0.15))
    for side in range(4):
        rz = side * math.pi / 2
        m = Matrix.Rotation(rz, 3, 'Z')
        c = m @ Vector((0, -(s + 0.8) / 2 + 0.05, hplat + 0.55))
        timber.box(tuple(c), (s + 0.8, 0.08, 0.08) if side % 2 == 0 else (0.08, s + 0.8, 0.08))
        c2 = m @ Vector((0, -(s + 0.8) / 2 + 0.05, hplat + 1.0))
        timber.box(tuple(c2), (s + 0.8, 0.1, 0.1) if side % 2 == 0 else (0.1, s + 0.8, 0.1))
    for k in range(int(hplat / 0.35)):  # драбина
        timber.box((0, -s / 2 - 0.55, 0.3 + k * 0.35), (0.6, 0.06, 0.05))
    for x in (-0.3, 0.3):
        timber.box((x, -s / 2 - 0.55, hplat / 2 + 0.2), (0.07, 0.07, hplat + 0.4))
    roofm.pyramid(0, 0, hplat + 2.4, s / 2 + 0.7, s / 2 + 0.7, 1.6)
    parts = {"foundation": found.to_object(f"{name}.foundation", full, "foundation"),
             "walls": logs.to_object(f"{name}.posts", full, "logs"),
             "timber": timber.to_object(f"{name}.timber", full, "timber"),
             "roof": roofm.to_object(f"{name}.roof", full, "shingle")}
    _solidify(parts["roof"], 0.1)
    _make_stages(name, stages, parts, s, s, 0.4, hplat + 2.4, 1.6, hplat)
    bpy.data.objects.remove(full, do_unlink=True)
    root.location = (origin[0], origin[1], 0)
    return root

def church(name="temple", origin=(0, 0)):
    """Храм: дерев'яна тризрубна церква — три зруби, ярусні шатрові верхи з маківками."""
    _remove_tree(name)
    root = _empty(name); stages = [_empty(f"{name}.stage{k}", root) for k in range(1, 6)]
    full = _empty(f"{name}.parts")
    found, logs, roofm, trim, glass, metal = Mesh(), Mesh(), Mesh(), Mesh(), Mesh(), Mesh()
    base = 0.4
    cells = [(-3.6, 3.0, 4.0), (0.0, 4.4, 5.0), (3.4, 3.0, 4.0)]  # (x, розмір, висота стін)
    for i, (x, s, h) in enumerate(cells):
        found.box((x, 0, base / 2), (s + 0.3, s + 0.3, base))
        m = Mesh(); top = _log_walls(m, s, s, base, h, r=0.15, salt=31 + i)
        bmesh.ops.translate(m.bm, vec=(x, 0, 0), verts=m.bm.verts[:])
        me = bpy.data.meshes.new("tmp"); m.bm.to_mesh(me); logs.bm.from_mesh(me); bpy.data.meshes.remove(me); m.bm.free()
        # опасання (піддашшя) і ярусні верхи
        roofm.pyramid(x, 0, top, s / 2 + 0.9, s / 2 + 0.9, 0.9)
        z = top + 0.6
        for tier, k in enumerate((0.8, 0.55)):
            ts = s * k
            m2 = Mesh(); _log_walls(m2, ts, ts, z, 1.0 + tier * 0.2, r=0.12, salt=50 + i * 3 + tier)
            bmesh.ops.translate(m2.bm, vec=(x, 0, 0), verts=m2.bm.verts[:])
            me = bpy.data.meshes.new("tmp"); m2.bm.to_mesh(me); logs.bm.from_mesh(me); bpy.data.meshes.remove(me); m2.bm.free()
            z2 = z + 1.0 + tier * 0.2
            roofm.pyramid(x, 0, z2, ts / 2 + 0.5, ts / 2 + 0.5, 0.7 if tier == 0 else 1.6)
            z = z2 + 0.5
        # маківка й хрест
        metal.cyl((x, 0, z + 1.0), (x, 0, z + 1.5), 0.05, segs=6)
        metal.cyl((x, 0, z + 1.1), (x, 0, z + 1.9), 0.06, segs=6)
        metal.box((x, 0, z + 1.7), (0.5, 0.06, 0.06))
    _door(trim, 0.0, -2.2, base, w=1.1, h=2.2)
    for x in (-3.6, 3.4):
        _window(trim, glass, x, -1.5, base + 1.8, w=0.5, h=0.8, shutters=False)
    parts = {"foundation": found.to_object(f"{name}.foundation", full, "foundation"),
             "walls": logs.to_object(f"{name}.logs", full, "logs"),
             "roof": roofm.to_object(f"{name}.roof", full, "shingle"),
             "trim": trim.to_object(f"{name}.trim", full, "timber"),
             "glass": glass.to_object(f"{name}.glass", full, "glass"),
             "metal": metal.to_object(f"{name}.cross", full, "metal")}
    _cut(parts["walls"], [((0.0, -2.2, base + 1.1), (1.1, 1.2, 2.2))])
    _solidify(parts["roof"], 0.1)
    _make_stages(name, stages, parts, 10.0, 4.4, base, base + 5.0, 3.0, 5.0)
    bpy.data.objects.remove(full, do_unlink=True)
    root.location = (origin[0], origin[1], 0)
    return root

def market(name="market", origin=(0, 0)):
    """Ринок: три ятки — стовпи, прилавок, полотняний навіс, ящики з товаром."""
    _remove_tree(name)
    root = _empty(name); stages = [_empty(f"{name}.stage{k}", root) for k in range(1, 6)]
    full = _empty(f"{name}.parts")
    found, timber, cloth = Mesh(), Mesh(), Mesh()
    found.box((0, 0, 0.05), (9.5, 5.0, 0.1))
    for i, x in enumerate((-3.0, 0.0, 3.0)):
        rz = (i - 1) * 0.12
        for (px, py, ph) in ((-1.1, -0.9, 2.3), (1.1, -0.9, 2.3), (-1.1, 0.9, 2.7), (1.1, 0.9, 2.7)):
            timber.box((x + px, py, ph / 2), (0.12, 0.12, ph))
        timber.box((x, -0.7, 0.9), (2.3, 0.6, 0.08))      # прилавок
        timber.box((x, -0.7, 0.45), (2.2, 0.5, 0.9))
        for k in range(3):                                   # ящики
            timber.box((x - 0.7 + k * 0.7, 0.4, 0.25), (0.5, 0.45, 0.45 + 0.1 * _hash01(i * 3 + k)))
        f = cloth.poly([(x - 1.3, -1.2, 2.25), (x + 1.3, -1.2, 2.25), (x + 1.3, 1.1, 2.75), (x - 1.3, 1.1, 2.75)])
    parts = {"foundation": found.to_object(f"{name}.ground", full, "foundation"),
             "timber": timber.to_object(f"{name}.stalls", full, "timber"),
             "roof": cloth.to_object(f"{name}.awnings", full, "cloth")}
    _solidify(parts["roof"], 0.02)
    _make_stages(name, stages, parts, 9.0, 3.0, 0.1, 2.7, 0.5, 2.6)
    bpy.data.objects.remove(full, do_unlink=True)
    root.location = (origin[0], origin[1], 0)
    return root

def watermill(name="watermill", origin=(0, 0)):
    def wheel(full, base, top):
        m = Mesh(); R, wdt = 2.0, 0.8
        x0 = 3.3
        for side in (-wdt / 2, wdt / 2):
            ring = []
            for k in range(24):
                a = 2 * math.pi * k / 24
                ring.append((x0 + side, R * math.cos(a), 1.6 + R * math.sin(a)))
            for k in range(24):
                m.cyl(ring[k], ring[(k + 1) % 24], 0.06, segs=6)
        for k in range(12):
            a = 2 * math.pi * k / 12
            m.cyl((x0, 0, 1.6), (x0, R * math.cos(a), 1.6 + R * math.sin(a)), 0.05, segs=6)
            m.box((x0, (R + 0.05) * math.cos(a), 1.6 + (R + 0.05) * math.sin(a)), (wdt + 0.1, 0.06, 0.45), rz=0)
        m.cyl((x0 - 0.8, 0, 1.6), (x0 + 0.6, 0, 1.6), 0.12, segs=10)
        ch = Mesh()
        ch.box((x0, 1.5, 3.9), (0.7, 3.0, 0.1)); ch.box((x0 - 0.35, 1.5, 4.05), (0.06, 3.0, 0.3)); ch.box((x0 + 0.35, 1.5, 4.05), (0.06, 3.0, 0.3))
        for y in (0.5, 2.5):
            ch.box((x0, y, 2.0), (0.12, 0.12, 3.8))
        return {"wheel": m.to_object(f"{name}.wheel", full, "timber"), "chute": ch.to_object(f"{name}.chute", full, "timber")}
    return house(name, w=5.0, d=5.0, h=3.0, style="logs", roof="gable", rise=2.4, doors=(-0.8,), windows=(1.2,),
                 roof_part="shingle", origin=origin, extra=wheel)

def palisade(name="palisade", origin=(0, 0), length=12.0, gate=True):
    """Частокіл: загострені колоди, поперечні ригелі, ворота з надбрамною перекладиною."""
    _remove_tree(name)
    root = _empty(name); s5 = _empty(f"{name}.stage5", root)
    logs, timber = Mesh(), Mesh()
    n = int(length / 0.32)
    for i in range(n):
        x = -length / 2 + i * 0.32 + 0.16
        if gate and abs(x) < 1.6:
            continue
        h = 3.0 + 0.35 * _hash01(i, 3)
        logs.cyl((x, 0, -0.3), (x, 0, h), 0.15, segs=8)
        logs.cyl((x, 0, h), (x, 0, h + 0.45), 0.15, 0.01, segs=8)
    for z in (0.9, 2.3):
        for sgn in (-1, 1):
            a, b = (sgn * length / 2, sgn * (1.6 if gate else 0))
            timber.box(((a + b) / 2, 0.2, z), (abs(a - b), 0.12, 0.18))
    if gate:
        for x in (-1.7, 1.7):
            logs.cyl((x, 0, -0.3), (x, 0, 4.6), 0.22, segs=10)
        timber.box((0, 0, 4.3), (4.0, 0.3, 0.3))
        for sgn in (-1, 1):  # стулки воріт, прочинені
            timber.box((sgn * 0.8, -0.35, 1.5), (1.5, 0.12, 2.8), rz=sgn * 0.35)
    for ob in (logs.to_object(f"{name}.logs", s5, "logs"), timber.to_object(f"{name}.timber", s5, "timber")):
        finish(ob)
    root.location = (origin[0], origin[1], 0)
    return root

# ---------------------------------------------------------------- реквізит

def tent(col=None, out=None):
    """Намет героя: полотняна «двосхилка» з провислими схилами, задня стінка, передні поли підв'язані
    навстіж, жердини, розтяжки з кілками й скатка всередині. Замість рожевого намету Kenney
    (власник 07.10.2026). Вхід — у −Y, як двері будинків."""
    col = col or collection("Alpha_Props")
    name = "prop_tent"
    _remove_tree(name)
    root = _empty(name, col=col)
    L, W, H, z0 = 2.6, 2.3, 1.75, 0.04
    cv = Mesh()
    nu, nv = 4, 6
    for side in (-1, 1):
        grid = []
        for i in range(nu + 1):
            u = i / nu                                     # 0 — гребінь, 1 — земля
            row = []
            for j in range(nv + 1):
                v = j / nv
                y = -L / 2 - 0.12 + (L + 0.24) * v
                sag = 0.07 * math.sin(math.pi * u) * math.sin(math.pi * v)
                x = side * (W / 2 * u - sag * 0.6)
                z = H + (z0 - H) * u - sag
                row.append(cv.bm.verts.new((x, y, z)))
            grid.append(row)
        for i in range(nu):
            for j in range(nv):
                q = (grid[i][j], grid[i + 1][j], grid[i + 1][j + 1], grid[i][j + 1])
                cv.bm.faces.new(q if side > 0 else q[::-1])
    cv.poly([(-W / 2, L / 2, z0), (W / 2, L / 2, z0), (0, L / 2, H)])          # задня стінка
    for side in (-1, 1):                                                       # поли входу, відкинуті вбік
        cv.poly([(side * 0.05, -L / 2 - 0.12, H - 0.05), (side * W / 2, -L / 2 - 0.12, z0),
                 (side * (W / 2 + 0.35), -L / 2 - 0.45, 0.25)][::side])
    canvas = cv.to_object(f"{name}.canvas", root, "canvas", col)
    finish(canvas)
    _solidify(canvas, 0.015, offset=0.0)
    m = canvas.data.materials[0]
    b = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    b.inputs["Base Color"].default_value = (0.78, 0.71, 0.56, 1)               # небілене полотно
    tm = Mesh()
    for y in (-L / 2, L / 2):
        tm.cyl((0, y, 0), (0, y, H + 0.14), 0.035, segs=8)                   # стояки
    tm.cyl((0, -L / 2 - 0.15, H + 0.03), (0, L / 2 + 0.15, H + 0.03), 0.03, segs=8)   # гребінь
    for (x, y) in ((0, -L / 2 - 1.0), (0, L / 2 + 1.0), (-W / 2 - 0.5, -L / 2 + 0.2), (W / 2 + 0.5, -L / 2 + 0.2),
                   (-W / 2 - 0.5, L / 2 - 0.2), (W / 2 + 0.5, L / 2 - 0.2)):
        tm.cyl((x, y, -0.1), (x, y, 0.22), 0.025, 0.012, segs=6)              # кілки
    for (a, b2) in (((0, -L / 2, H + 0.1), (0, -L / 2 - 1.0, 0.18)), ((0, L / 2, H + 0.1), (0, L / 2 + 1.0, 0.18)),
                    ((-W / 2 + 0.05, -L / 2 + 0.2, z0 + 0.1), (-W / 2 - 0.5, -L / 2 + 0.2, 0.18)),
                    ((W / 2 - 0.05, -L / 2 + 0.2, z0 + 0.1), (W / 2 + 0.5, -L / 2 + 0.2, 0.18)),
                    ((-W / 2 + 0.05, L / 2 - 0.2, z0 + 0.1), (-W / 2 - 0.5, L / 2 - 0.2, 0.18)),
                    ((W / 2 - 0.05, L / 2 - 0.2, z0 + 0.1), (W / 2 + 0.5, L / 2 - 0.2, 0.18))):
        tm.cyl(a, b2, 0.008, segs=4)                                          # розтяжки
    finish(tm.to_object(f"{name}.poles", root, "timber", col))
    bd = Mesh()
    bd.cyl((-0.35, -0.2, 0.14), (-0.35, 0.9, 0.14), 0.14, segs=12)           # скатка
    bd.box((0.25, 0.3, 0.03), (0.9, 1.7, 0.05))                               # підстилка
    finish(bd.to_object(f"{name}.bedroll", root, "bedroll", col))
    if out is not None:
        out[name] = root
    return root

def props(origin=(0, 0)):
    """Реквізит села й укриття бою: бочки, ящики, віз, колодязь, ковадло, дрова, вогнище, лава,
    мішки, сіно, стяг; укриття — тин (половинне) і кам'яна брила (повне). По одному примірнику."""
    col = collection("Alpha_Props")
    out = {}
    def mk(pname, build, part_meshes):
        _remove_tree(pname)
        root = _empty(pname, col=col)
        for part, m in part_meshes.items():
            finish(m.to_object(f"{pname}.{part}", root, part, col))
        out[pname] = root
        return root
    # бочка
    b, mt = Mesh(), Mesh()
    b.cyl((0, 0, 0), (0, 0, 0.45), 0.28, 0.33, segs=16); b.cyl((0, 0, 0.45), (0, 0, 0.9), 0.33, 0.28, segs=16)
    for z in (0.12, 0.45, 0.78):
        mt.cyl((0, 0, z - 0.025), (0, 0, z + 0.025), 0.34 if z == 0.45 else 0.305, segs=16)
    mk("prop_barrel", None, {"logs": b, "metal": mt})
    # ящик
    c = Mesh(); c.box((0, 0, 0.3), (0.6, 0.6, 0.6))
    for z in (0.05, 0.55):
        c.box((0, -0.31, z), (0.62, 0.03, 0.08)); c.box((0, 0.31, z), (0.62, 0.03, 0.08))
    mk("prop_crate", None, {"timber": c})
    # віз
    cart, iron = Mesh(), Mesh()
    cart.box((0, 0, 0.75), (2.2, 1.2, 0.08))
    for y in (-0.6, 0.6):
        cart.box((0, y, 0.95), (2.2, 0.06, 0.4))
    for x in (-1.1, 1.1):
        cart.box((x, 0, 0.95), (0.06, 1.2, 0.4))
    cart.cyl((1.1, -0.15, 0.6), (2.8, -0.15, 0.55), 0.04, segs=6); cart.cyl((1.1, 0.15, 0.6), (2.8, 0.15, 0.55), 0.04, segs=6)
    for (x, y) in ((-0.6, -0.72), (-0.6, 0.72), (0.7, -0.72), (0.7, 0.72)):
        cart.cyl((x, y - 0.05, 0.45), (x, y + 0.05, 0.45), 0.45, segs=16)
        iron.cyl((x, y - 0.06, 0.45), (x, y + 0.06, 0.45), 0.47, segs=16, caps=False)
    mk("prop_cart", None, {"timber": cart, "metal": iron})
    # колодязь із журавлем-воротом
    wst, wt = Mesh(), Mesh()
    for k in range(10):
        a = 2 * math.pi * k / 10
        wst.box((0.75 * math.cos(a), 0.75 * math.sin(a), 0.4), (0.45, 0.3, 0.8), rz=a + math.pi / 2)
    for x in (-0.8, 0.8):
        wt.box((x, 0, 1.2), (0.12, 0.12, 1.6))
    wt.cyl((-0.9, 0, 1.75), (0.9, 0, 1.75), 0.08, segs=8); wt.pyramid(0, 0, 2.0, 1.1, 0.9, 0.6)
    mk("prop_well", None, {"foundation": wst, "timber": wt})
    # ковадло на колоді
    an, st = Mesh(), Mesh()
    st.cyl((0, 0, 0), (0, 0, 0.55), 0.3, segs=12)
    an.box((0, 0, 0.65), (0.5, 0.2, 0.18)); an.cyl((0.25, 0, 0.7), (0.5, 0, 0.7), 0.08, 0.01, segs=8); an.box((0, 0, 0.58), (0.22, 0.16, 0.1))
    mk("prop_anvil", None, {"logs": st, "metal": an})
    # дровітня
    wd = Mesh()
    for i in range(18):
        r, c_ = divmod(i, 6)
        wd.cyl((-0.6, -0.75 + c_ * 0.27 + (r % 2) * 0.13, 0.13 + r * 0.24), (0.6, -0.75 + c_ * 0.27 + (r % 2) * 0.13, 0.13 + r * 0.24), 0.12, segs=8)
    mk("prop_woodpile", None, {"logs": wd})
    # вогнище
    fs, fl = Mesh(), Mesh()
    for k in range(9):
        a = 2 * math.pi * k / 9
        fs.box((0.55 * math.cos(a), 0.55 * math.sin(a), 0.1), (0.28, 0.22, 0.2), rz=a)
    for k in range(4):
        a = math.pi * k / 4
        fl.cyl((-0.4 * math.cos(a), -0.4 * math.sin(a), 0.05), (0.4 * math.cos(a), 0.4 * math.sin(a), 0.35), 0.06, segs=6)
    mk("prop_campfire", None, {"foundation": fs, "logs": fl})
    # лава
    bn = Mesh(); bn.box((0, 0, 0.45), (1.8, 0.35, 0.08))
    for x in (-0.75, 0.75):
        bn.box((x, 0, 0.22), (0.08, 0.3, 0.44))
    mk("prop_bench", None, {"timber": bn})
    # стяг
    fg, fc = Mesh(), Mesh()
    fg.cyl((0, 0, 0), (0, 0, 3.2), 0.05, segs=8); fg.cyl((0, -0.05, 3.0), (0, 0.9, 3.0), 0.03, segs=6)
    fc.poly([(0, 0.0, 3.0), (0, 0.9, 3.0), (0, 0.9, 1.9), (0, 0.0, 1.9)])
    mk("prop_banner", None, {"timber": fg, "cloth": fc})
    tent(col, out)
    # мішки й сіно
    sk = Mesh()
    for k in range(3):
        sk.cyl((k * 0.42 - 0.42, 0, 0), (k * 0.42 - 0.42, 0, 0.55), 0.2, 0.15, segs=10)
    mk("prop_sacks", None, {"cloth": sk})
    hy = Mesh(); hy.box((0, 0, 0.45), (1.2, 0.8, 0.9))
    mk("prop_hay", None, {"roof": hy})
    # укриття бою: тин (половинне, ~1 м) і брила (повне, ~1.8 м); півот — центр клітинки
    wf = Mesh()
    for k in range(5):
        x = -0.45 + k * 0.225
        wf.cyl((x, 0, -0.1), (x, 0, 1.05), 0.03, segs=6)
    for z in range(6):
        wf.box((0, 0.02 * (1 if z % 2 else -1), 0.12 + z * 0.16), (1.0, 0.04, 0.1))
    mk("cover_half_wattle", None, {"logs": wf})
    rk = Mesh(); rk.box((0, 0, 0.9), (0.95, 0.8, 1.8))
    mk("cover_full_rock", None, {"foundation": rk})
    rob = bpy.data.objects.get("cover_full_rock.foundation")
    if rob:
        bv = rob.modifiers.new("soft", 'BEVEL'); bv.width = 0.18; bv.segments = 3
        _apply_mods(rob); finish(rob)
    for i, (k, root) in enumerate(out.items()):
        root.location = (origin[0] + (i % 7) * 3.0, origin[1] + (i // 7) * 3.0, 0)
    return out


# ---------------------------------------------------------------- природа

def tree_materials(tex_dir):
    """Матеріали кори й хвої з текстур fir_tree_01 (Poly Haven, CC0), вивантажених у Assets/Art/Textures."""
    import os
    def img(n):
        p = os.path.join(tex_dir, n)
        im = bpy.data.images.get(n) or bpy.data.images.load(p, check_existing=True)
        return im
    def mat(name, diff, nor, alpha=False):
        m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
        m.use_nodes = True; m.use_fake_user = True
        nt = m.node_tree; nt.nodes.clear()
        out = nt.nodes.new("ShaderNodeOutputMaterial"); b = nt.nodes.new("ShaderNodeBsdfPrincipled")
        nt.links.new(b.outputs[0], out.inputs[0])
        t = nt.nodes.new("ShaderNodeTexImage"); t.image = img(diff)
        nt.links.new(t.outputs["Color"], b.inputs["Base Color"])
        if alpha:
            nt.links.new(t.outputs["Alpha"], b.inputs["Alpha"])
            m.blend_method = 'CLIP' if hasattr(m, "blend_method") else None
            m.use_backface_culling = False
        tn = nt.nodes.new("ShaderNodeTexImage"); tn.image = img(nor); tn.image.colorspace_settings.name = 'Non-Color'
        nm = nt.nodes.new("ShaderNodeNormalMap"); nt.links.new(tn.outputs["Color"], nm.inputs["Color"])
        nt.links.new(nm.outputs["Normal"], b.inputs["Normal"])
        b.inputs["Roughness"].default_value = 0.85
        return m
    mat("M_FirBark", "fir_bark_diff.png", "fir_bark_nor_gl.png")
    mat("M_FirTwig", "fir_twig_rgba.png", "fir_twig_nor_gl.png", alpha=True)

def _card(m, uv_layer, p0, dirv, side, length, width, droop):
    """Картка гілки: смуга з двох сегментів, що провисає; обидва боки (дубль із розвернутими нормалями)."""
    dirv = Vector(dirv).normalized(); side = Vector(side).normalized()
    p0 = Vector(p0)
    mid = p0 + dirv * (length * 0.5) + Vector((0, 0, -droop * 0.35))
    end = p0 + dirv * length + Vector((0, 0, -droop))
    rows = [(p0, 0.0, width * 0.35), (mid, 0.5, width), (end, 1.0, width * 0.55)]
    verts = []
    for c, v, wd in rows:
        verts.append((m.bm.verts.new(c - side * wd / 2), (0.0, v)))
        verts.append((m.bm.verts.new(c + side * wd / 2), (1.0, v)))
    for flip in (False, True):
        for r in range(2):
            a, b, c, d = verts[r * 2], verts[r * 2 + 1], verts[r * 2 + 3], verts[r * 2 + 2]
            quad = (a, b, c, d) if not flip else (d, c, b, a)
            if flip:  # окремі вершини для зворотного боку, щоб нормалі не усереднювались
                quad = tuple((m.bm.verts.new(q[0].co), q[1]) for q in quad)
            f = m.bm.faces.new([q[0] for q in quad])
            for loop, q in zip(f.loops, quad):
                loop[uv_layer].uv = q[1]

def conifer(name, height=12.0, radius=3.0, whorls=11, per=7, origin=(0, 0), salt=0, col_name="Alpha_Nature", z_start=0.18):
    """Ялина/смерека: стовбур (кора) і яруси карток хвої, що коротшають до верхівки; детерміновано."""
    col = collection(col_name)
    _remove_tree(name)
    root = _empty(name, col=col)
    trunk, ndl = Mesh(), Mesh()
    trunk.cyl((0, 0, -0.2), (0, 0, height * 0.92), 0.06 * height ** 0.9 / 2, 0.03, segs=10)
    uv = ndl.bm.loops.layers.uv.verify()
    z0 = height * z_start
    for i in range(whorls):
        t = i / max(1, whorls - 1)
        z = z0 + (height * 0.95 - z0) * t
        L = radius * (1.0 - t) ** 0.85 + 0.35
        n = max(4, int(per * (1.0 - 0.45 * t)))
        for k in range(n):
            a = 2 * math.pi * (k + 0.5 * (i % 2) + 0.3 * _hash01(i * 13 + k, salt)) / n
            d = Vector((math.cos(a), math.sin(a), 0.15 - 0.25 * t))
            sidev = Vector((-math.sin(a), math.cos(a), 0))
            wd = 1.0 + 1.0 * (1 - t)
            _card(ndl, uv, (0, 0, z), d, sidev, L, wd, droop=0.25 * L)
            _card(ndl, uv, (0, 0, z + 0.05), d, Vector((0, 0, 1)), L * 0.9, wd * 0.8, droop=0.25 * L)
            # другий, коротший шар між ярусами — щільніший силует
            d2 = Vector((math.cos(a + math.pi / n), math.sin(a + math.pi / n), 0.05 - 0.2 * t))
            _card(ndl, uv, (0, 0, z + (height * 0.95 - z0) / max(1, whorls - 1) * 0.5), d2,
                  Vector((-d2.y, d2.x, 0)), L * 0.75, wd * 0.8, droop=0.2 * L)
    _card(ndl, uv, (0, 0, height * 0.9), (0, 0, 1), (1, 0, 0), height * 0.12, 0.5, 0.0)
    finish(trunk.to_object(f"{name}.trunk", root, "bark", col))
    finish(ndl.to_object(f"{name}.needles", root, "needles", col))
    root.location = (origin[0], origin[1], 0)
    return root

def nature(origin=(0, 0)):
    """Набір дерев: велика ялиця, середня, струнка сосна, підріст."""
    out = [conifer("tree_fir_large", 13.0, 3.2, 16, 11, (origin[0], origin[1]), 1),
           conifer("tree_fir_medium", 9.0, 2.4, 13, 10, (origin[0] + 8, origin[1]), 2),
           conifer("tree_pine_tall", 15.0, 2.2, 9, 9, (origin[0] + 16, origin[1]), 3, z_start=0.55),
           conifer("tree_sapling", 3.0, 1.0, 7, 8, (origin[0] + 22, origin[1]), 4, z_start=0.08)]
    return out

# ---------------------------------------------------------------- набір гри

def build_all():
    """Усі будівлі з DefaultBuildings + млин, частокіл, житлові хати і реквізит."""
    collection()
    out = []
    out.append(watchtower("watch", origin=(-24, 0)))
    out.append(house("storehouse", w=6.0, d=4.0, h=2.2, style="logs", roof="gable", rise=2.0, doors=(0.0,), windows=(),
                     origin=(-16, 0)))
    out.append(house("council_hall", w=10.0, d=6.0, h=2.8, style="plaster", roof="hip", rise=3.2, doors=(0.0,),
                     windows=(-3.6, -1.8, 1.8, 3.6), side_windows=True, porch=True, origin=(-4, 0)))
    out.append(house("infirmary", w=7.0, d=5.0, h=2.5, style="plaster", roof="hip", rise=2.8, doors=(-1.0,),
                     windows=(-2.6, 1.0, 2.6), porch=True, chimney=(2.2, 1.2), origin=(8, 0)))
    out.append(house("workshop", w=6.0, d=5.0, h=2.5, style="logs", roof="gable", rise=2.2, doors=(-1.2,), windows=(1.4,),
                     chimney=(2.4, 1.6), roof_part="shingle", origin=(18, 0)))
    out.append(market("market", origin=(-24, 12)))
    out.append(house("tavern", w=9.0, d=6.0, h=3.0, style="logs", roof="gable", rise=3.4, doors=(-1.5,),
                     windows=(-3.3, 0.5, 2.5), side_windows=True, porch=True, chimney=(3.4, 1.8), origin=(-12, 12)))
    out.append(church("temple", origin=(2, 12)))
    out.append(house("fortifications", w=4.0, d=4.0, h=4.6, style="logs", roof="hip", rise=2.0, doors=(0.0,),
                     windows=(), side_windows=True, roof_part="shingle", origin=(14, 12)))
    out.append(house("armory", w=6.0, d=4.5, h=2.4, style="logs", roof="gable", rise=2.1, doors=(0.0,), windows=(-1.8,),
                     roof_part="shingle", origin=(22, 12)))
    out.append(watermill("watermill", origin=(-24, 24)))
    out.append(palisade("palisade", origin=(-8, 24)))
    out.append(house("hut_a", w=6.0, d=4.5, style="plaster", origin=(6, 24)))
    out.append(house("hut_b", w=5.5, d=4.2, style="logs", windows=(-1.6, 1.6), origin=(14, 24)))
    out.append(house("hut_c", w=6.5, d=4.8, style="plaster", roof="gable", rise=2.6, doors=(1.2,), windows=(-1.6,),
                     chimney=(-1.8, 1.0), origin=(22, 24)))
    props(origin=(-24, 36))
    return [o.name for o in out]

def save_textures(art_dir):
    """Вивантажує текстури Poly Haven з .blend у <art_dir>/Textures (карти висоти — ні: Unity їх не бере)."""
    import os
    out = os.path.join(art_dir, "Textures"); os.makedirs(out, exist_ok=True)
    for img in bpy.data.images:
        if img.source != 'FILE' or "Displacement" in img.name:
            continue
        if img.packed_file is not None:
            data = img.packed_file.data
            path = os.path.join(out, img.name + (".png" if data[1:4] == b"PNG" else ".jpg"))
            with open(path, "wb") as f:
                f.write(data)
            img.filepath = path
            img.unpack(method='USE_ORIGINAL')

def export_all(art_dir):
    """FBX кожної будівлі й реквізиту в <art_dir>/Models/<id>.fbx; текстури — спільні, у <art_dir>/Textures
    (save_textures їх туди вивантажує), FBX посилається на них відносним шляхом. Корінь — у нулі (позиція набору в .blend на експорт не впливає)."""
    import os
    res = []
    for col_name, sub in ((COL, "Models"), ("Alpha_Props", "Models"), ("Alpha_Nature", "Models")):
        col = bpy.data.collections.get(col_name)
        if col is None:
            continue
        os.makedirs(os.path.join(art_dir, sub), exist_ok=True)
        roots = [o for o in col.objects if o.parent is None and o.type == 'EMPTY']
        for root in roots:
            loc = root.location.copy(); root.location = (0, 0, 0)
            bpy.ops.object.select_all(action='DESELECT')
            for o in [root] + list(root.children_recursive):
                o.hide_set(False); o.select_set(True)
            path = os.path.join(art_dir, sub, root.name + ".fbx")
            # Без bake_space_transform: на ланцюжку «корінь → стадія → сітка» він повертав сітки двічі, і в
            # Unity будівлі лежали на боці — «брили й напівбудови» (власник 07.10.2026). Осі конвертує
            # Unity при імпорті (ArtImportSettings: bakeAxisConversion).
            bpy.ops.export_scene.fbx(filepath=path, use_selection=True, apply_unit_scale=True,
                                     apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y',
                                     object_types={'EMPTY', 'MESH'}, use_mesh_modifiers=True, mesh_smooth_type='FACE',
                                     path_mode='RELATIVE', embed_textures=False, bake_space_transform=False)
            root.location = loc
            res.append(path)
    return res
