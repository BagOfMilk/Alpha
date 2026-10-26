# Ринг і арена для «Рингу» (етап 1, docs/TRPG_DESIGN.md §4.4, §11.1).
#
#   "C:\Program Files\Blender Foundation\Blender 4.5\blender.exe" -b --python tools/blender/ring_arena.py -- <корінь репо> [--look]
#
# Геометрія будується кодом, детерміновано (без random), 1 одиниця = 1 м, Z угору, вхід (рампа й завіса) — у +Y.
# Канатів у моделі немає: їх будує гра між якорями anchor_rope_<кут>_<1..3> (канат мусить прогинатися від відскоку).
# Решта якорів — точки для анімацій: середина канатів, верх стійки, край канту, сходи, завіса.
# Розміри — як у справжнього рингу на 20 футів: бійцівський квадрат 6,1 м, мат на висоті 1,2 м, канати на 0,55 / 0,95 /
# 1,35 м над матом. Матеріали — лише колір (без чужих текстур), ліцензія — власна робота.

import bpy, bmesh, math, os, sys
from mathutils import Vector, Matrix

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
REPO = args[0] if args else os.getcwd()
LOOK = "--look" in args

COL = "Ring_Arena"

# ---- розміри (м)
MAT_Z = 1.20            # верх мату
HALF = 3.05             # половина бійцівського квадрата (усередині канатів)
POST = 3.20             # центри стійок
APRON = 3.60            # край канту
ROPES = (0.55, 0.95, 1.35)
POST_TOP = MAT_Z + 1.55
FLOOR_MATS = 5.6        # половина килима навколо рингу
BARRIER = 7.4           # половина огорожі
CORNERS = {"NE": (1, 1), "NW": (-1, 1), "SE": (1, -1), "SW": (-1, -1)}
SIDES = {"N": (0, 1), "S": (0, -1), "E": (1, 0), "W": (-1, 0)}

MATERIALS = {
    # назва: (колір RGB, металевість, шорсткість)
    "Ring_Canvas":   ((0.86, 0.86, 0.84), 0.0, 0.85),
    "Ring_Apron":    ((0.05, 0.05, 0.06), 0.0, 0.7),
    "Ring_Logo":     ((0.62, 0.08, 0.08), 0.0, 0.7),
    "Ring_Post":     ((0.22, 0.22, 0.24), 0.9, 0.35),
    "Ring_Pad":      ((0.55, 0.06, 0.06), 0.0, 0.55),
    "Ring_Steps":    ((0.45, 0.46, 0.48), 0.9, 0.4),
    "Ring_FloorMat": ((0.06, 0.09, 0.20), 0.0, 0.9),
    "Arena_Floor":   ((0.035, 0.035, 0.04), 0.0, 0.95),
    "Barrier":       ((0.10, 0.10, 0.11), 0.7, 0.5),
    "Barrier_Pad":   ((0.04, 0.04, 0.05), 0.0, 0.8),
    "Table":         ((0.08, 0.08, 0.09), 0.0, 0.6),
    "Monitor":       ((0.02, 0.02, 0.025), 0.2, 0.3),
    "Stage":         ((0.07, 0.07, 0.08), 0.0, 0.7),
    "Ramp":          ((0.12, 0.12, 0.13), 0.3, 0.6),
    "Curtain":       ((0.09, 0.03, 0.10), 0.0, 0.9),
    "Truss":         ((0.30, 0.30, 0.32), 0.9, 0.4),
}

def collection():
    col = bpy.data.collections.get(COL)
    if col is None:
        col = bpy.data.collections.new(COL)
        bpy.context.scene.collection.children.link(col)
    return col

def material(name):
    m = bpy.data.materials.get(name)
    if m is not None:
        return m
    color, metal, rough = MATERIALS[name]
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    b.inputs["Base Color"].default_value = (*color, 1)
    b.inputs["Metallic"].default_value = metal
    b.inputs["Roughness"].default_value = rough
    m.diffuse_color = (*color, 1)
    return m

def empty(name, parent, loc=(0, 0, 0)):
    e = bpy.data.objects.new(name, None)
    collection().objects.link(e)
    e.parent = parent
    e.location = loc
    e.empty_display_size = 0.15
    return e

class Mesh:
    def __init__(self):
        self.bm = bmesh.new()

    def box(self, c, s, rz=0.0):
        r = bmesh.ops.create_cube(self.bm, size=1.0)
        bmesh.ops.scale(self.bm, vec=s, verts=r["verts"])
        if rz:
            bmesh.ops.rotate(self.bm, cent=(0, 0, 0), matrix=Matrix.Rotation(rz, 3, 'Z'), verts=r["verts"])
        bmesh.ops.translate(self.bm, vec=c, verts=r["verts"])

    def cyl(self, p0, p1, r0, r1=None, segs=16):
        p0, p1 = Vector(p0), Vector(p1)
        d = p1 - p0
        res = bmesh.ops.create_cone(self.bm, cap_ends=True, segments=segs, radius1=r0,
                                    radius2=r0 if r1 is None else r1, depth=d.length)
        rot = d.to_track_quat('Z', 'Y').to_matrix().to_4x4()
        bmesh.ops.transform(self.bm, matrix=rot, verts=res["verts"])
        bmesh.ops.translate(self.bm, vec=p0 + d / 2, verts=res["verts"])

    def to_object(self, name, parent, mat, smooth=False, bevel=0.0):
        if not self.bm.verts:
            self.bm.free()
            return None
        me = bpy.data.meshes.new(name)
        self.bm.to_mesh(me)
        self.bm.free()
        ob = bpy.data.objects.new(name, me)
        collection().objects.link(ob)
        ob.parent = parent
        me.materials.append(material(mat))
        for p in me.polygons:
            p.use_smooth = smooth
        if bevel > 0:
            mod = ob.modifiers.new("bevel", 'BEVEL')
            mod.width = bevel
            mod.segments = 2
            mod.limit_method = 'ANGLE'
        uv_world(ob)
        return ob

def uv_world(ob, tex=1.0):
    """UV у світових метрах за домінантною віссю нормалі — щоб текстуру, якщо колись додамо, не розтягувало."""
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    uv = bm.loops.layers.uv.verify()
    for f in bm.faces:
        n = f.normal
        ax = max(range(3), key=lambda k: abs(n[k]))
        for l in f.loops:
            co = l.vert.co
            u, v = {0: (co.y, co.z), 1: (co.x, co.z), 2: (co.x, co.y)}[ax]
            l[uv].uv = (u / tex, v / tex)
    bm.to_mesh(ob.data)
    bm.free()

# ------------------------------------------------------------------ ринг

def build_ring(root):
    ring = empty("ring", root)

    # Платформа: мат (трохи заокруглений край), кант — та сама плита, спідниця по периметру.
    m = Mesh()
    m.box((0, 0, MAT_Z - 0.06), (APRON * 2, APRON * 2, 0.12))
    m.to_object("canvas", ring, "Ring_Canvas", bevel=0.03)

    m = Mesh()
    for sx, sy in SIDES.values():
        if sx:
            m.box((sx * (APRON - 0.01), 0, (MAT_Z - 0.12) / 2), (0.02, APRON * 2, MAT_Z - 0.12))
        else:
            m.box((0, sy * (APRON - 0.01), (MAT_Z - 0.12) / 2), (APRON * 2, 0.02, MAT_Z - 0.12))
    m.to_object("apron_skirt", ring, "Ring_Apron")

    # Смуга-логотип на спідниці з боку камери (−Y) і з боку рампи (+Y) — пляма кольору, без тексту.
    m = Mesh()
    for sy in (-1, 1):
        m.box((0, sy * (APRON + 0.004), MAT_Z * 0.55), (APRON * 1.1, 0.01, 0.32))
    m.to_object("apron_logo", ring, "Ring_Logo")

    # Стійки, стяжки канатів і подушки на кожному куті.
    posts = Mesh()
    pads = Mesh()
    for name, (cx, cy) in CORNERS.items():
        px, py = cx * POST, cy * POST
        posts.cyl((px, py, 0.0), (px, py, POST_TOP), 0.065)
        posts.cyl((px, py, POST_TOP), (px, py, POST_TOP + 0.04), 0.075)
        diag = math.atan2(cy, cx)
        for i, h in enumerate(ROPES):
            z = MAT_Z + h
            # подушка на стяжці — з боку рингу, повернута до центру (стійку не обхоплює)
            off = 0.17
            pads.box((px - cx * off, py - cy * off, z), (0.12, 0.24, 0.26), rz=diag)
            empty("anchor_rope_%s_%d" % (name, i + 1), root, (px - cx * 0.08, py - cy * 0.08, z))
        # верх стійки — точка, звідки стрибають (стоять на другому канаті, тримаючись за верх)
        empty("anchor_corner_top_%s" % name, root, (cx * (POST - 0.35), cy * (POST - 0.35), MAT_Z + ROPES[1]))
        empty("anchor_corner_%s" % name, root, (cx * (HALF - 0.45), cy * (HALF - 0.45), MAT_Z))
    posts.to_object("posts", ring, "Ring_Post", smooth=True)
    pads.to_object("turnbuckle_pads", ring, "Ring_Pad", bevel=0.04)

    # Якорі середини канатів (відскок), канту, центру, закочування.
    empty("anchor_center", root, (0, 0, MAT_Z))
    for name, (sx, sy) in SIDES.items():
        for i, h in enumerate(ROPES):
            empty("anchor_rope_mid_%s_%d" % (name, i + 1), root, (sx * POST, sy * POST, MAT_Z + h))
        empty("anchor_rebound_%s" % name, root, (sx * (HALF - 0.35), sy * (HALF - 0.35), MAT_Z))
        empty("anchor_apron_%s" % name, root, (sx * (APRON - 0.2), sy * (APRON - 0.2), MAT_Z))
        empty("anchor_floor_%s" % name, root, (sx * (APRON + 0.7), sy * (APRON + 0.7), 0.0))

    # Сталеві сходи на двох кутах (NE — бік рампи, SW — навпроти).
    steps = Mesh()
    for corner in ("NE", "SW"):
        cx, cy = CORNERS[corner]
        diag = math.atan2(cy, cx)
        d = Vector((cx, cy, 0)).normalized()
        base = Vector((cx * (APRON + 0.15), cy * (APRON + 0.15), 0))
        for k in range(3):
            h = MAT_Z * (k + 1) / 3 - 0.05
            depth = 0.42 * (3 - k)
            c = base + d * (depth / 2)
            steps.box((c.x, c.y, h / 2), (depth, 1.0, h), rz=diag)
        empty("anchor_steps_%s" % corner, root, tuple(base + d * 1.4))
    steps.to_object("steel_steps", ring, "Ring_Steps", bevel=0.02)
    return ring

# ------------------------------------------------------------------ арена

def build_arena(root):
    arena = empty("arena", root)

    m = Mesh()
    m.box((0, 4, -0.01), (44, 52, 0.02))
    m.to_object("arena_floor", arena, "Arena_Floor")

    m = Mesh()
    m.box((0, 0, 0.012), (FLOOR_MATS * 2, FLOOR_MATS * 2, 0.024))
    m.to_object("floor_mats", arena, "Ring_FloorMat", bevel=0.01)

    # Огорожа по периметру з проходом до рампи (+Y) — металеві стійки й м'яка накладка зверху.
    frame = Mesh()
    pad = Mesh()
    gap = 2.4
    H = 1.1
    for sx, sy in SIDES.values():
        n = 8
        for k in range(n):
            a = -BARRIER + (2 * BARRIER) * (k + 0.5) / n
            if sy == 1 and abs(a) < gap / 2 + 0.6:
                continue
            seg = 2 * BARRIER / n - 0.06
            if sx:
                c, s = (sx * BARRIER, a, H / 2), (0.08, seg, H)
            else:
                c, s = (a, sy * BARRIER, H / 2), (seg, 0.08, H)
            frame.box(c, s)
            pc = (c[0], c[1], H + 0.06)
            ps = (s[0] + 0.12, s[1], 0.12) if sx else (s[0], s[1] + 0.12, 0.12)
            pad.box(pc, ps)
    frame.to_object("barrier", arena, "Barrier", bevel=0.01)
    pad.to_object("barrier_pad", arena, "Barrier_Pad", bevel=0.03)

    # Стіл коментаторів біля рингу з боку −X: стіл, два монітори.
    table = Mesh()
    mon = Mesh()
    tx = -(APRON + (BARRIER - APRON) / 2 + 0.4)
    table.box((tx, 0, 0.375), (0.8, 2.4, 0.75))
    for y in (-0.6, 0.6):
        mon.box((tx + 0.15, y, 0.92), (0.05, 0.5, 0.32))
    table.to_object("announce_table", arena, "Table", bevel=0.01)
    mon.to_object("monitors", arena, "Monitor")
    empty("anchor_announce_table", root, (tx, 0, 0.75))

    # Рампа від огорожі до сцени і сцена із завісою — звідси виходять борці.
    ramp = Mesh()
    ramp_y0, ramp_y1 = BARRIER, BARRIER + 11
    ramp.box((0, (ramp_y0 + ramp_y1) / 2, 0.15), (2.4, ramp_y1 - ramp_y0, 0.3))
    ramp.to_object("ramp", arena, "Ramp", bevel=0.02)

    stage = Mesh()
    sy0 = ramp_y1
    stage.box((0, sy0 + 2.5, 0.5), (12, 5, 1.0))
    stage.to_object("stage", arena, "Stage", bevel=0.03)

    cur = Mesh()
    # Завіса з проходом посередині: дві широкі панелі й низька арка зверху.
    cy = sy0 + 4.6
    cur.box((-3.6, cy, 1.0 + 3.0), (4.8, 0.1, 6.0))
    cur.box((3.6, cy, 1.0 + 3.0), (4.8, 0.1, 6.0))
    cur.box((0, cy, 1.0 + 5.0), (2.4, 0.1, 2.0))
    cur.to_object("curtain", arena, "Curtain")
    empty("anchor_curtain", root, (0, cy - 0.5, 1.0))
    empty("anchor_ramp_bottom", root, (0, ramp_y0 + 0.5, 0.0))
    empty("anchor_ramp_top", root, (0, ramp_y1 - 0.3, 0.3))

    # Ферма над рингом (світло ставить гра) — квадрат з балок.
    truss = Mesh()
    tz = 9.0
    for sx, sy in SIDES.values():
        if sx:
            truss.box((sx * 5.0, 0, tz), (0.35, 10.35, 0.35))
        else:
            truss.box((0, sy * 5.0, tz), (10.35, 0.35, 0.35))
    for cx, cy in CORNERS.values():
        truss.cyl((cx * 5.0, cy * 5.0, tz), (cx * 5.0, cy * 5.0, tz + 3.0), 0.03, segs=6)
    truss.to_object("truss", arena, "Truss")
    empty("anchor_truss_center", root, (0, 0, tz))
    return arena

# ------------------------------------------------------------------ збирання, огляд, експорт

def build():
    bpy.ops.wm.read_homefile(use_empty=True)
    root = empty("ring_arena", None)
    collection().objects.link(root) if root.name not in collection().objects else None
    build_ring(root)
    build_arena(root)
    return root

def preview_ropes(root):
    """Канати лише для знімка (у FBX їх немає — будує гра): прямі циліндри між якорями сусідніх кутів."""
    obs = {o.name: o for o in root.children_recursive}
    m = Mesh()
    order = ("NE", "NW", "SW", "SE")
    for i in range(3):
        for k in range(4):
            a = obs["anchor_rope_%s_%d" % (order[k], i + 1)].matrix_world.translation
            b = obs["anchor_rope_%s_%d" % (order[(k + 1) % 4], i + 1)].matrix_world.translation
            m.cyl(a, b, 0.022, segs=8)
    MATERIALS.setdefault("Rope_Preview", ((0.75, 0.05, 0.05), 0.0, 0.5))
    return m.to_object("preview_ropes", None, "Rope_Preview", smooth=True)

def look(root, out_dir):
    """Знімки для огляду: ізометрія (як камера гри) і крупно кут рингу."""
    os.makedirs(out_dir, exist_ok=True)
    ropes = preview_ropes(root)
    scene = bpy.context.scene
    try:
        scene.render.engine = 'BLENDER_WORKBENCH'
    except TypeError:
        pass
    scene.display.shading.light = 'STUDIO'
    scene.display.shading.color_type = 'MATERIAL'
    scene.display.shading.show_shadows = True
    scene.display.shading.show_cavity = True
    scene.render.resolution_x, scene.render.resolution_y = 1600, 900
    cam_data = bpy.data.cameras.new("look")
    cam = bpy.data.objects.new("look", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    shots = {
        "ring_iso": ((14, -14, 13), (0, 0, 1.0), 35),
        "ring_corner": ((6.5, -6.5, 3.4), (2.6, -2.6, 1.6), 40),
        "ring_entrance": ((0, -16, 6), (0, 8, 1.5), 30),
    }
    for name, (pos, target, lens) in shots.items():
        cam.location = pos
        d = Vector(target) - Vector(pos)
        cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
        cam_data.lens = lens
        scene.render.filepath = os.path.join(out_dir, name + ".png")
        bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam, do_unlink=True)
    bpy.data.objects.remove(ropes, do_unlink=True)

def export(root, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.object.select_all(action='DESELECT')
    for o in [root] + list(root.children_recursive):
        o.select_set(True)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, apply_unit_scale=True,
                             apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y',
                             object_types={'EMPTY', 'MESH'}, use_mesh_modifiers=True, mesh_smooth_type='FACE',
                             path_mode='RELATIVE', embed_textures=False, bake_space_transform=False)

def anchors_manifest(root, path):
    """Перелік якорів із позиціями (м, Z угору) — ядро гри звіряє з ним свої зони (охоронець)."""
    lines = ["# Якорі рингу (tools/blender/ring_arena.py). <ім'я> <x> <y> <z>, метри, Z угору, вхід у +Y."]
    for o in sorted(root.children_recursive, key=lambda o: o.name):
        if o.type == 'EMPTY' and o.name.startswith("anchor_"):
            p = o.matrix_world.translation
            lines.append("%s %.3f %.3f %.3f" % (o.name, p.x, p.y, p.z))
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")
    return len(lines) - 1

root = build()
art = os.path.join(REPO, "Assets", "Art", "Ring")
export(root, os.path.join(art, "ring_arena.fbx"))
n = anchors_manifest(root, os.path.join(art, "ring_anchors.txt"))
print("RING: exported, anchors:", n, flush=True)
if LOOK:
    look(root, os.path.join(REPO, "Logs", "ring"))
    print("RING: look rendered", flush=True)
