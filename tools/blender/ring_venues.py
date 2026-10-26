# Три арени «Рингу» (етап 1, docs/TRPG_DESIGN.md §4.4, §11.1) — від інді до середнього промоушену, без корпорацій.
#
#   "C:\Program Files\Blender Foundation\Blender 4.5\blender.exe" -b --python tools/blender/ring_venues.py -- <корінь репо> [gym hall arena] [--look]
#
#   gym   — шкільний спортзал, інді, ~200 глядачів: ринг 16 футів, розкладні стільці, висувні трибуни, ферма на стійках,
#           вихід через арку з завісою, роздягальня з шафками;
#   hall  — клубна зала з балконом, регіональний промоушен, ~700: ринг 18 футів, сцена з невеликим LED-екраном, сходи
#           і прохід між огорожами, U-подібний балкон, бар, коробка світла над рингом, гримерки, «горила», інтерв'ю;
#   arena — палац спорту, середній промоушен, ~3000: ринг 20 футів, трибуни двома ярусами, LED-стіна, рампа зі світлом,
#           стіл коментаторів, операторський кран, підвісна коробка світла, закулісся з роздягальнями, лікарем,
#           кейтерингом, інтерв'ю й рампою завантаження з вантажівкою.
#
# Геометрія кодом, детерміновано (без random: розкид — хеш від індексу), 1 одиниця = 1 м, Z угору.
# Ринг — у нулі; вхід борців у кожній арені — свій, шлях від завіси до рингу — якорями anchor_path_NN.
# Канатів немає: їх будує гра між якорями anchor_rope_<кут>_<1..3> (канат прогинається від відскоку).
# Стіни (wall_*) і стелі (ceiling*) — окремі об'єкти: ізометрична камера ховає ті, що між нею і рингом.
# Однакові дрібниці (стільці, сидіння, сходинки) злито в один об'єкт на матеріал — мало викликів малювання.
# Виходи: Assets/Art/Ring/venue_<id>.fbx, venue_<id>_anchors.txt (якорі), venue_<id>_seats.txt (місця для натовпу:
# x y z курс°), venue_<id>_info.txt (розмір рингу, висота мату, кольори канатів, місткість).
# Матеріали — лише колір (власна робота, без чужих текстур). Розміри рингів — як у справжніх: інді 16–18 футів,
# стандарт 20; килимки навколо рингу; вихідна арка ~2,4 м; ферма світла на стійках (інді) і підвісна коробка
# 32–40 футів (більші шоу).

import bpy, bmesh, math, os, sys
from mathutils import Vector, Matrix

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
REPO = args[0] if args else os.getcwd()
LOOK = "--look" in args
WANT = [a for a in args[1:] if not a.startswith("--")] or ["gym", "hall", "arena"]
OUT = os.path.join(REPO, "Assets", "Art", "Ring")
LOOK_DIR = os.path.join(REPO, "Logs", "venues")

def h01(i, salt=0):
    x = (i * 374761393 + salt * 668265263 + 1442695041) & 0xFFFFFFFF
    x = ((x ^ (x >> 13)) * 1274126177) & 0xFFFFFFFF
    return ((x ^ (x >> 16)) & 0xFFFF) / 65535.0

# ---------------------------------------------------------------- матеріали

# назва: (колір RGB, металевість, шорсткість, світіння)
MATS = {
    "Canvas_White": ((0.88, 0.88, 0.86), 0.0, 0.85, 0), "Canvas_Grey": ((0.70, 0.71, 0.72), 0.0, 0.85, 0),
    "Apron_Black": ((0.04, 0.04, 0.05), 0.0, 0.7, 0), "Apron_Blue": ((0.05, 0.10, 0.30), 0.0, 0.7, 0),
    "Apron_Band_Red": ((0.62, 0.07, 0.07), 0.0, 0.6, 0), "Apron_Band_Gold": ((0.72, 0.55, 0.16), 0.4, 0.45, 0),
    "Apron_Band_White": ((0.85, 0.85, 0.85), 0.0, 0.6, 0),
    "Post_Steel": ((0.22, 0.22, 0.24), 0.9, 0.35, 0), "Post_Black": ((0.05, 0.05, 0.05), 0.6, 0.4, 0),
    "Pad_Red": ((0.55, 0.06, 0.06), 0.0, 0.55, 0), "Pad_Blue": ((0.06, 0.12, 0.45), 0.0, 0.55, 0),
    "Pad_Black": ((0.03, 0.03, 0.03), 0.0, 0.55, 0), "Pad_Yellow": ((0.75, 0.62, 0.08), 0.0, 0.55, 0),
    "Steps_Steel": ((0.45, 0.46, 0.48), 0.9, 0.4, 0), "Steps_Black": ((0.06, 0.06, 0.07), 0.6, 0.5, 0),
    "Mat_Blue": ((0.06, 0.10, 0.25), 0.0, 0.9, 0), "Mat_Black": ((0.03, 0.03, 0.035), 0.0, 0.9, 0),
    "Mat_Red": ((0.35, 0.04, 0.04), 0.0, 0.9, 0),
    "Gym_Floor": ((0.74, 0.53, 0.32), 0.0, 0.35, 0), "Gym_Line_White": ((0.92, 0.92, 0.9), 0.0, 0.4, 0),
    "Gym_Line_Red": ((0.65, 0.08, 0.08), 0.0, 0.4, 0), "Gym_Paint_Blue": ((0.12, 0.22, 0.48), 0.0, 0.4, 0),
    "Block_Wall": ((0.80, 0.78, 0.72), 0.0, 0.9, 0), "Wall_Band_Blue": ((0.14, 0.25, 0.52), 0.0, 0.8, 0),
    "Brick_Dark": ((0.28, 0.14, 0.11), 0.0, 0.9, 0), "Wall_Dark": ((0.10, 0.10, 0.11), 0.0, 0.9, 0),
    "Wall_Grey": ((0.42, 0.43, 0.45), 0.0, 0.9, 0), "Concrete": ((0.48, 0.48, 0.47), 0.0, 0.95, 0),
    "Floor_Dark": ((0.07, 0.07, 0.08), 0.0, 0.8, 0), "Floor_Tile": ((0.62, 0.62, 0.58), 0.0, 0.6, 0),
    "Carpet_Red": ((0.32, 0.06, 0.07), 0.0, 1.0, 0), "Ceiling": ((0.25, 0.25, 0.26), 0.0, 0.9, 0),
    "Steel_Beam": ((0.30, 0.31, 0.33), 0.8, 0.5, 0), "Ceiling_Light": ((0.95, 0.95, 0.88), 0.0, 0.5, 3.0),
    "Bleacher_Wood": ((0.66, 0.47, 0.28), 0.0, 0.5, 0), "Bleacher_Frame": ((0.30, 0.30, 0.32), 0.8, 0.5, 0),
    "Chair_Frame": ((0.18, 0.18, 0.19), 0.8, 0.45, 0), "Chair_Seat_Black": ((0.05, 0.05, 0.06), 0.0, 0.6, 0),
    "Chair_Seat_Grey": ((0.45, 0.45, 0.47), 0.0, 0.6, 0), "Chair_Seat_Red": ((0.45, 0.05, 0.06), 0.0, 0.6, 0),
    "Seat_Red": ((0.50, 0.06, 0.07), 0.0, 0.6, 0), "Seat_Blue": ((0.07, 0.14, 0.42), 0.0, 0.6, 0),
    "Seat_Grey": ((0.32, 0.33, 0.35), 0.0, 0.6, 0),
    "Tier_Concrete": ((0.36, 0.36, 0.37), 0.0, 0.95, 0), "Tier_Front": ((0.08, 0.08, 0.09), 0.0, 0.8, 0),
    "Table_Top": ((0.88, 0.88, 0.85), 0.0, 0.5, 0), "Table_Leg": ((0.25, 0.25, 0.26), 0.8, 0.4, 0),
    "Cloth_Black": ((0.03, 0.03, 0.035), 0.0, 0.95, 0), "Cloth_Red": ((0.45, 0.04, 0.05), 0.0, 0.95, 0),
    "Cloth_Blue": ((0.06, 0.10, 0.32), 0.0, 0.95, 0),
    "Barricade_Steel": ((0.55, 0.56, 0.58), 0.9, 0.35, 0), "Barricade_Cover": ((0.04, 0.04, 0.05), 0.0, 0.8, 0),
    "Bike_Rack": ((0.62, 0.63, 0.65), 0.9, 0.3, 0),
    "Truss_Alu": ((0.72, 0.73, 0.75), 0.9, 0.3, 0), "Truss_Black": ((0.06, 0.06, 0.06), 0.7, 0.4, 0),
    "Light_Body": ((0.06, 0.06, 0.06), 0.5, 0.5, 0), "Light_Lens": ((1.0, 0.92, 0.75), 0.0, 0.2, 6.0),
    "Light_Lens_Blue": ((0.4, 0.55, 1.0), 0.0, 0.2, 6.0), "Light_Lens_Red": ((1.0, 0.25, 0.2), 0.0, 0.2, 6.0),
    "Speaker_Black": ((0.03, 0.03, 0.03), 0.0, 0.7, 0), "Speaker_Grille": ((0.10, 0.10, 0.11), 0.4, 0.6, 0),
    "Drape_Black": ((0.02, 0.02, 0.025), 0.0, 1.0, 0), "Drape_Red": ((0.35, 0.03, 0.04), 0.0, 1.0, 0),
    "LED_Screen": ((0.12, 0.30, 0.85), 0.0, 0.3, 4.0), "LED_Side": ((0.75, 0.15, 0.55), 0.0, 0.3, 3.0),
    "LED_Strip": ((0.85, 0.85, 1.0), 0.0, 0.3, 5.0), "Screen_Frame": ((0.05, 0.05, 0.05), 0.5, 0.5, 0),
    "Stage_Black": ((0.05, 0.05, 0.06), 0.0, 0.7, 0), "Ramp_Black": ((0.07, 0.07, 0.08), 0.2, 0.6, 0),
    "Locker_Grey": ((0.48, 0.52, 0.56), 0.7, 0.45, 0), "Locker_Blue": ((0.15, 0.28, 0.52), 0.7, 0.45, 0),
    "Bench_Wood": ((0.60, 0.42, 0.24), 0.0, 0.6, 0), "Mirror": ((0.85, 0.88, 0.92), 1.0, 0.05, 0),
    "Mirror_Bulb": ((1.0, 0.95, 0.8), 0.0, 0.3, 4.0),
    "Door_Grey": ((0.40, 0.42, 0.44), 0.7, 0.5, 0), "Door_Wood": ((0.45, 0.30, 0.18), 0.0, 0.6, 0),
    "Exit_Sign": ((0.1, 0.9, 0.3), 0.0, 0.4, 5.0), "Glass": ((0.12, 0.18, 0.26), 0.2, 0.1, 0),
    "Hoop_Board": ((0.92, 0.92, 0.92), 0.0, 0.3, 0), "Hoop_Rim": ((0.85, 0.35, 0.05), 0.8, 0.4, 0),
    "Scoreboard": ((0.03, 0.03, 0.03), 0.0, 0.6, 0), "Digits_Red": ((1.0, 0.1, 0.05), 0.0, 0.3, 5.0),
    "Bar_Wood": ((0.30, 0.17, 0.09), 0.0, 0.5, 0), "Bar_Top": ((0.12, 0.08, 0.05), 0.2, 0.2, 0),
    "Bottle": ((0.15, 0.35, 0.18), 0.0, 0.15, 0), "Shelf_Light": ((1.0, 0.7, 0.4), 0.0, 0.3, 3.0),
    "Railing_Steel": ((0.20, 0.20, 0.21), 0.9, 0.35, 0),
    "Camera_Body": ((0.08, 0.08, 0.09), 0.4, 0.5, 0), "Camera_Lens": ((0.02, 0.02, 0.02), 0.6, 0.2, 0),
    "Monitor": ((0.03, 0.03, 0.035), 0.3, 0.4, 0), "Monitor_Screen": ((0.25, 0.45, 0.7), 0.0, 0.2, 2.5),
    "Bell_Brass": ((0.80, 0.60, 0.20), 1.0, 0.25, 0), "Trash_Green": ((0.12, 0.28, 0.14), 0.7, 0.5, 0),
    "Trash_Grey": ((0.35, 0.36, 0.38), 0.8, 0.4, 0), "Ladder_Alu": ((0.75, 0.76, 0.78), 0.9, 0.35, 0),
    "Kendo": ((0.82, 0.70, 0.45), 0.0, 0.6, 0), "Cooler_Blue": ((0.10, 0.30, 0.65), 0.0, 0.5, 0),
    "Cooler_White": ((0.90, 0.90, 0.88), 0.0, 0.5, 0), "Shirt_A": ((0.08, 0.08, 0.08), 0.0, 0.9, 0),
    "Shirt_B": ((0.75, 0.10, 0.10), 0.0, 0.9, 0), "Shirt_C": ((0.85, 0.85, 0.85), 0.0, 0.9, 0),
    "Cardboard": ((0.55, 0.42, 0.28), 0.0, 0.9, 0), "Banner_Red": ((0.55, 0.05, 0.06), 0.0, 0.9, 0),
    "Banner_Black": ((0.04, 0.04, 0.045), 0.0, 0.9, 0), "Banner_Gold": ((0.70, 0.52, 0.15), 0.3, 0.6, 0),
    "Backdrop_White": ((0.90, 0.90, 0.92), 0.0, 0.8, 0), "Backdrop_Logo": ((0.55, 0.06, 0.08), 0.0, 0.8, 0),
    "Vinyl_Black": ((0.05, 0.05, 0.05), 0.0, 0.4, 0), "Towel_White": ((0.92, 0.92, 0.9), 0.0, 1.0, 0),
    "Case_Black": ((0.06, 0.06, 0.07), 0.3, 0.6, 0), "Case_Edge": ((0.65, 0.66, 0.68), 0.9, 0.3, 0),
    "Truck_White": ((0.88, 0.88, 0.88), 0.2, 0.4, 0), "Truck_Cab": ((0.55, 0.06, 0.06), 0.4, 0.4, 0),
    "Tire": ((0.03, 0.03, 0.03), 0.0, 0.9, 0), "Food": ((0.70, 0.45, 0.20), 0.0, 0.7, 0),
    "Water_Blue": ((0.35, 0.60, 0.90), 0.0, 0.1, 0), "Plant_Green": ((0.15, 0.40, 0.15), 0.0, 0.8, 0),
    "Flag_Gold": ((0.85, 0.65, 0.15), 0.3, 0.5, 0),
}

def material(name):
    m = bpy.data.materials.get(name)
    if m is not None:
        return m
    color, metal, rough, emit = MATS[name]
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    b.inputs["Base Color"].default_value = (*color, 1)
    b.inputs["Metallic"].default_value = metal
    b.inputs["Roughness"].default_value = rough
    if emit:
        for key in ("Emission Color", "Emission"):
            if key in b.inputs:
                b.inputs[key].default_value = (*color, 1)
                break
        if "Emission Strength" in b.inputs:
            b.inputs["Emission Strength"].default_value = emit
    m.diffuse_color = (*color, 1)
    return m

# ---------------------------------------------------------------- геометрія

COL = None

def collection(name):
    col = bpy.data.collections.get(name)
    if col is None:
        col = bpy.data.collections.new(name)
        bpy.context.scene.collection.children.link(col)
    return col

def empty(name, parent, loc=(0, 0, 0), yaw=0.0):
    e = bpy.data.objects.new(name, None)
    COL.objects.link(e)
    e.parent = parent
    e.location = loc
    e.rotation_euler = (0, 0, math.radians(yaw))
    e.empty_display_size = 0.2
    return e

class Parts:
    """Набір геометрії однієї групи: на кожен матеріал — один об'єкт «<група>_<матеріал>» (мало викликів малювання)."""
    def __init__(self, group, parent):
        self.group, self.parent = group, parent
        self.bms = {}

    def bm(self, mat):
        if mat not in self.bms:
            self.bms[mat] = bmesh.new()
        return self.bms[mat]

    def box(self, mat, c, s, rz=0.0, rx=0.0):
        bm = self.bm(mat)
        r = bmesh.ops.create_cube(bm, size=1.0)
        bmesh.ops.scale(bm, vec=s, verts=r["verts"])
        if rx:
            bmesh.ops.rotate(bm, cent=(0, 0, 0), matrix=Matrix.Rotation(rx, 3, 'X'), verts=r["verts"])
        if rz:
            bmesh.ops.rotate(bm, cent=(0, 0, 0), matrix=Matrix.Rotation(rz, 3, 'Z'), verts=r["verts"])
        bmesh.ops.translate(bm, vec=c, verts=r["verts"])

    def cyl(self, mat, p0, p1, r0, r1=None, segs=12):
        bm = self.bm(mat)
        p0, p1 = Vector(p0), Vector(p1)
        d = p1 - p0
        if d.length < 1e-6:
            return
        res = bmesh.ops.create_cone(bm, cap_ends=True, segments=segs, radius1=r0,
                                    radius2=r0 if r1 is None else r1, depth=d.length)
        rot = d.to_track_quat('Z', 'Y').to_matrix().to_4x4()
        bmesh.ops.transform(bm, matrix=rot, verts=res["verts"])
        bmesh.ops.translate(bm, vec=p0 + d / 2, verts=res["verts"])

    def quad(self, mat, pts):
        bm = self.bm(mat)
        vs = [bm.verts.new(p) for p in pts]
        bm.faces.new(vs)

    def finish(self, smooth=()):
        out = []
        for mat, bm in self.bms.items():
            if not bm.verts:
                bm.free()
                continue
            me = bpy.data.meshes.new(self.group + "_" + mat)
            bm.to_mesh(me)
            bm.free()
            ob = bpy.data.objects.new(self.group + "_" + mat, me)
            COL.objects.link(ob)
            ob.parent = self.parent
            me.materials.append(material(mat))
            for p in me.polygons:
                p.use_smooth = mat in smooth
            out.append(ob)
        self.bms = {}
        return out

def rot2(v, yaw):
    c, s = math.cos(yaw), math.sin(yaw)
    return Vector((v[0] * c - v[1] * s, v[0] * s + v[1] * c, v[2]))

def at(pos, yaw, local):
    """Точка local (простір речі, лицем у −Y) у світі для речі в pos з курсом yaw (рад)."""
    return Vector(pos) + rot2(local, yaw)

SEATS = []           # (x, y, z, курс°) — місця для натовпу

def seat_mark(pos, yaw_rad, z):
    SEATS.append((pos[0], pos[1], z, math.degrees(yaw_rad)))

# ---------------------------------------------------------------- реквізит

def folding_chair(P, pos, yaw, seat="Chair_Seat_Black", crowd=True):
    """Розкладний стілець, лицем у напрямку yaw (рад, 0 — лицем у −Y)."""
    f = lambda v: at(pos, yaw, v)
    P.box(seat, f((0, 0, 0.45)), (0.42, 0.40, 0.03), rz=yaw)
    P.box(seat, f((0, 0.19, 0.74)), (0.42, 0.03, 0.30), rz=yaw)
    for x in (-0.19, 0.19):
        P.box("Chair_Frame", f((x, -0.17, 0.225)), (0.022, 0.022, 0.45), rz=yaw)
        P.box("Chair_Frame", f((x, 0.2, 0.45)), (0.022, 0.022, 0.9), rz=yaw)
    P.box("Chair_Frame", f((0, -0.17, 0.08)), (0.38, 0.018, 0.018), rz=yaw)
    if crowd:
        seat_mark(pos, yaw, 0.45)

def chair_rows(P, origin, yaw, rows, per_row, pitch=0.52, row_depth=0.9, gap_every=0, gap=1.0, seat="Chair_Seat_Black",
               skip=None):
    """Ряди стільців: origin — центр першого ряду, лицем yaw; ряди відходять назад (+Y речі)."""
    for r in range(rows):
        width = per_row * pitch + (per_row // gap_every if gap_every else 0) * (gap - pitch)
        x0 = -width / 2 + pitch / 2
        x = x0
        for k in range(per_row):
            if gap_every and k and k % gap_every == 0:
                x += gap - pitch
            pos = at(origin, yaw, (x, r * row_depth, 0))
            if not (skip and skip(pos)):
                folding_chair(P, (pos.x, pos.y, 0.0), yaw, seat)
            x += pitch

def folding_table(P, pos, yaw, length=1.83, width=0.76, cloth=None, h=0.74):
    f = lambda v: at(pos, yaw, v)
    P.box("Table_Top", f((0, 0, h)), (length, width, 0.04), rz=yaw)
    for sx in (-1, 1):
        for sy in (-1, 1):
            P.box("Table_Leg", f((sx * (length / 2 - 0.08), sy * (width / 2 - 0.06), h / 2)), (0.03, 0.03, h), rz=yaw)
    if cloth:
        P.box(cloth, f((0, 0, h + 0.022)), (length + 0.04, width + 0.04, 0.005), rz=yaw)
        P.box(cloth, f((0, -width / 2 - 0.02, h / 2 + 0.05)), (length + 0.04, 0.01, h - 0.05), rz=yaw)

def monitor(P, pos, yaw, w=0.55, h=0.33, z=0.0):
    f = lambda v: at(pos, yaw, v)
    P.box("Monitor", f((0, 0, z + h / 2 + 0.12)), (w, 0.04, h), rz=yaw)
    P.box("Monitor_Screen", f((0, -0.021, z + h / 2 + 0.12)), (w - 0.04, 0.003, h - 0.04), rz=yaw)
    P.box("Monitor", f((0, 0.03, z + 0.06)), (0.05, 0.05, 0.12), rz=yaw)
    P.box("Monitor", f((0, 0.03, z + 0.005)), (0.22, 0.16, 0.01), rz=yaw)

def laptop(P, pos, yaw, z):
    f = lambda v: at(pos, yaw, v)
    P.box("Monitor", f((0, 0, z + 0.01)), (0.34, 0.24, 0.02), rz=yaw)
    P.box("Monitor", f((0, 0.12, z + 0.12)), (0.34, 0.015, 0.22), rz=yaw, rx=math.radians(-15))
    P.box("Monitor_Screen", f((0, 0.11, z + 0.12)), (0.31, 0.003, 0.19), rz=yaw, rx=math.radians(-15))

def ring_bell(P, pos, z):
    P.cyl("Table_Leg", (pos[0], pos[1], z), (pos[0], pos[1], z + 0.05), 0.09, segs=16)
    P.cyl("Bell_Brass", (pos[0], pos[1], z + 0.05), (pos[0], pos[1], z + 0.17), 0.13, 0.05, segs=20)
    P.cyl("Table_Leg", (pos[0] + 0.15, pos[1], z), (pos[0] + 0.15, pos[1], z + 0.28), 0.012)

def mic_stand(P, pos, h=1.5):
    x, y = pos[0], pos[1]
    P.cyl("Table_Leg", (x, y, 0.0), (x, y, 0.02), 0.14, segs=16)
    P.cyl("Table_Leg", (x, y, 0.02), (x, y, h), 0.012)
    P.cyl("Monitor", (x, y, h), (x, y - 0.12, h + 0.06), 0.02)

def trash_can(P, pos, mat="Trash_Green"):
    x, y = pos[0], pos[1]
    P.cyl(mat, (x, y, 0.0), (x, y, 0.85), 0.28, 0.3, segs=16)
    P.cyl(mat, (x, y, 0.85), (x, y, 0.92), 0.31, segs=16)

def ladder(P, base, top):
    base, top = Vector(base), Vector(top)
    d = top - base
    side = d.cross(Vector((0, 0, 1)))
    side = (side.normalized() if side.length > 1e-4 else Vector((1, 0, 0))) * 0.22
    P.cyl("Ladder_Alu", base + side, top + side, 0.025, segs=8)
    P.cyl("Ladder_Alu", base - side, top - side, 0.025, segs=8)
    n = max(2, int(d.length / 0.3))
    for k in range(1, n):
        q = base + d * (k / n)
        P.cyl("Ladder_Alu", q - side, q + side, 0.015, segs=6)

def kendo_bin(P, pos):
    x, y = pos[0], pos[1]
    P.cyl("Trash_Grey", (x, y, 0.0), (x, y, 0.55), 0.18, segs=12)
    for k in range(4):
        a = k * math.pi / 2
        P.cyl("Kendo", (x + 0.06 * math.cos(a), y + 0.06 * math.sin(a), 0.1),
              (x + 0.12 * math.cos(a), y + 0.12 * math.sin(a), 1.15), 0.016, segs=6)

def chair_stack(P, pos, yaw, n=8):
    """Складені розкладні стільці, сперті на стіну."""
    for k in range(n):
        p = at(pos, yaw, (0, -0.05 * k, 0))
        P.box("Chair_Seat_Grey", (p.x, p.y, 0.48), (0.42, 0.03, 0.9), rz=yaw, rx=math.radians(-12))

def cooler(P, pos, yaw, mat="Cooler_Blue"):
    f = lambda v: at(pos, yaw, v)
    P.box(mat, f((0, 0, 0.2)), (0.6, 0.4, 0.38), rz=yaw)
    P.box("Cooler_White", f((0, 0, 0.405)), (0.62, 0.42, 0.04), rz=yaw)

def water_cooler(P, pos):
    x, y = pos[0], pos[1]
    P.box("Cooler_White", (x, y, 0.5), (0.32, 0.32, 1.0))
    P.cyl("Water_Blue", (x, y, 1.0), (x, y, 1.45), 0.14, segs=12)

def speaker_tripod(P, pos, yaw, h=2.2):
    x, y = pos[0], pos[1]
    for k in range(3):
        a = k * 2 * math.pi / 3
        P.cyl("Light_Body", (x + 0.45 * math.cos(a), y + 0.45 * math.sin(a), 0.0), (x, y, 0.9), 0.015, segs=6)
    P.cyl("Light_Body", (x, y, 0.9), (x, y, h - 0.35), 0.02, segs=8)
    f = lambda v: at(pos, yaw, v)
    P.box("Speaker_Black", f((0, 0, h)), (0.42, 0.38, 0.65), rz=yaw)
    P.box("Speaker_Grille", f((0, -0.195, h)), (0.36, 0.01, 0.58), rz=yaw)

def speaker_stack(P, pos, yaw, n=2):
    f = lambda v: at(pos, yaw, v)
    for k in range(n):
        P.box("Speaker_Black", f((0, 0, 0.45 + 0.9 * k)), (0.9, 0.7, 0.88), rz=yaw)
        P.box("Speaker_Grille", f((0, -0.355, 0.45 + 0.9 * k)), (0.8, 0.01, 0.78), rz=yaw)

def line_array(P, pos, yaw, top, n=6):
    f = lambda v: at(pos, yaw, v)
    for k in range(n):
        z = top - 0.42 * k
        P.box("Speaker_Black", f((0, 0.0 + 0.04 * k, z)), (1.1, 0.55, 0.38), rz=yaw, rx=math.radians(-3 * k))
    P.cyl("Light_Body", f((0, 0, top + 0.2)), f((0, 0, top + 3.0)), 0.015, segs=6)

def par_can(P, pos, aim, lens="Light_Lens"):
    """Прожектор-банка в pos, світить на точку aim."""
    pos, aim = Vector(pos), Vector(aim)
    d = (aim - pos).normalized()
    P.cyl("Light_Body", pos - d * 0.12, pos + d * 0.14, 0.11, segs=10)
    P.cyl(lens, pos + d * 0.14, pos + d * 0.15, 0.09, segs=10)

def moving_head(P, pos, aim):
    pos, aim = Vector(pos), Vector(aim)
    P.box("Light_Body", pos + Vector((0, 0, -0.08)), (0.32, 0.26, 0.14))
    d = (aim - pos).normalized()
    head = pos + Vector((0, 0, -0.3))
    P.cyl("Light_Body", head - d * 0.16, head + d * 0.14, 0.13, segs=12)
    P.cyl("Light_Lens", head + d * 0.14, head + d * 0.15, 0.1, segs=12)

def truss_beam(P, p0, p1, size=0.29, mat="Truss_Alu"):
    """Квадратна ферма: чотири пояси й розкоси."""
    p0, p1 = Vector(p0), Vector(p1)
    d = p1 - p0
    if d.length < 1e-4:
        return
    a = d.normalized()
    u = a.cross(Vector((0, 0, 1)))
    if u.length < 1e-3:
        u = a.cross(Vector((1, 0, 0)))
    u.normalize()
    v = a.cross(u).normalized()
    h = size / 2
    corners = [u * h + v * h, -u * h + v * h, -u * h - v * h, u * h - v * h]
    for c in corners:
        P.cyl(mat, p0 + c, p1 + c, 0.024, segs=6)
    n = max(1, int(d.length / 0.5))
    for k in range(n):
        q0 = p0 + d * (k / n)
        q1 = p0 + d * ((k + 1) / n)
        for i in range(4):
            c0, c1 = corners[i], corners[(i + 1) % 4]
            P.cyl(mat, q0 + c0, q1 + c1, 0.012, segs=4)

def ladder_truss(P, p0, p1, h=0.3, mat="Truss_Alu"):
    """Драбинчаста ферма (два пояси і щаблі) — дешевий інді-варіант."""
    p0, p1 = Vector(p0), Vector(p1)
    d = p1 - p0
    up = Vector((0, 0, h / 2))
    P.cyl(mat, p0 + up, p1 + up, 0.024, segs=6)
    P.cyl(mat, p0 - up, p1 - up, 0.024, segs=6)
    n = max(1, int(d.length / 0.3))
    for k in range(n + 1):
        q = p0 + d * (k / n)
        P.cyl(mat, q - up, q + up, 0.012, segs=4)

def windup_stand(P, pos, h):
    x, y = pos[0], pos[1]
    for k in range(4):
        a = k * math.pi / 2 + math.pi / 4
        P.cyl("Truss_Black", (x + 0.8 * math.cos(a), y + 0.8 * math.sin(a), 0.03), (x, y, 0.5), 0.025, segs=6)
    P.cyl("Truss_Black", (x, y, 0.0), (x, y, h), 0.06, segs=10)
    P.box("Truss_Black", (x, y, 0.3), (0.3, 0.3, 0.25))

def light_tree(P, pos, aim, h=3.0, n=4):
    x, y = pos[0], pos[1]
    for k in range(3):
        a = k * 2 * math.pi / 3
        P.cyl("Light_Body", (x + 0.6 * math.cos(a), y + 0.6 * math.sin(a), 0.0), (x, y, 1.0), 0.02, segs=6)
    P.cyl("Light_Body", (x, y, 0.0), (x, y, h), 0.03, segs=8)
    side = (Vector(aim) - Vector((x, y, h))).cross(Vector((0, 0, 1)))
    side = side.normalized() if side.length > 1e-4 else Vector((1, 0, 0))
    P.cyl("Light_Body", Vector((x, y, h)) - side * 0.7, Vector((x, y, h)) + side * 0.7, 0.02, segs=6)
    for k in range(n):
        p = Vector((x, y, h + 0.15)) + side * (-0.6 + 1.2 * k / (n - 1))
        par_can(P, p, aim)

def hd_camera(P, pos, yaw, z, tripod=True):
    x, y = pos[0], pos[1]
    if tripod:
        for k in range(3):
            a = k * 2 * math.pi / 3
            P.cyl("Light_Body", (x + 0.45 * math.cos(a), y + 0.45 * math.sin(a), z), (x, y, z + 1.25), 0.016, segs=6)
    f = lambda v: at((x, y, 0), yaw, v)
    P.box("Camera_Body", f((0, 0.05, z + 1.4)), (0.22, 0.5, 0.26), rz=yaw)
    P.cyl("Camera_Lens", f((0, -0.2, z + 1.42)), f((0, -0.42, z + 1.42)), 0.07, segs=12)
    P.box("Monitor", f((0.16, 0.12, z + 1.5)), (0.03, 0.18, 0.12), rz=yaw)

def jib(P, base, aim):
    base = Vector(base)
    P.box("Camera_Body", base + Vector((0, 0, 0.15)), (1.2, 1.2, 0.3))
    P.cyl("Light_Body", base + Vector((0, 0, 0.3)), base + Vector((0, 0, 2.2)), 0.08, segs=10)
    d = (Vector(aim) - base)
    d.z = 0
    d.normalize()
    pivot = base + Vector((0, 0, 2.3))
    tip = pivot + d * 5.5 + Vector((0, 0, 1.2))
    tail = pivot - d * 1.8 - Vector((0, 0, 0.3))
    P.cyl("Truss_Black", tail, tip, 0.07, 0.04, segs=8)
    P.box("Case_Black", tail - Vector((0, 0, 0.2)), (0.5, 0.5, 0.5))
    P.box("Camera_Body", tip - Vector((0, 0, 0.25)), (0.25, 0.45, 0.28))

def door(P, pos, yaw, w=0.95, h=2.1, mat="Door_Grey", sign=False):
    f = lambda v: at(pos, yaw, v)
    P.box(mat, f((0, 0, h / 2)), (w, 0.06, h), rz=yaw)
    P.box("Table_Leg", f((w * 0.38, -0.05, 1.0)), (0.12, 0.03, 0.03), rz=yaw)
    if sign:
        P.box("Exit_Sign", f((0, -0.06, h + 0.3)), (0.45, 0.03, 0.18), rz=yaw)

def lockers(P, origin, yaw, n, mat="Locker_Grey"):
    for k in range(n):
        f = lambda v: at(origin, yaw, v)
        x = (k - (n - 1) / 2) * 0.4
        P.box(mat, f((x, 0, 0.95)), (0.38, 0.45, 1.8), rz=yaw)
        P.box("Table_Leg", f((x + 0.12, -0.23, 1.1)), (0.03, 0.01, 0.12), rz=yaw)
        for v_ in (1.55, 1.6, 1.65):
            P.box("Table_Leg", f((x, -0.226, v_)), (0.2, 0.004, 0.015), rz=yaw)

def bench(P, pos, yaw, length=2.4):
    f = lambda v: at(pos, yaw, v)
    P.box("Bench_Wood", f((0, 0, 0.44)), (length, 0.32, 0.05), rz=yaw)
    for x in (-length / 2 + 0.15, length / 2 - 0.15):
        P.box("Table_Leg", f((x, 0, 0.21)), (0.05, 0.28, 0.42), rz=yaw)

def mirror_wall(P, origin, yaw, n, bulbs=True):
    f = lambda v: at(origin, yaw, v)
    for k in range(n):
        x = (k - (n - 1) / 2) * 1.2
        P.box("Mirror", f((x, 0, 1.55)), (1.0, 0.02, 0.8), rz=yaw)
        P.box("Bar_Top", f((x, -0.25, 0.78)), (1.15, 0.5, 0.04), rz=yaw)
        if bulbs:
            for b in range(5):
                P.cyl("Mirror_Bulb", f((x - 0.4 + 0.2 * b, -0.02, 2.0)), f((x - 0.4 + 0.2 * b, -0.06, 2.0)), 0.03, segs=6)
        folding_chair(P, at(origin, yaw, (x, -0.75, 0)), yaw + math.pi, "Chair_Seat_Black", crowd=False)

def massage_table(P, pos, yaw):
    f = lambda v: at(pos, yaw, v)
    P.box("Vinyl_Black", f((0, 0, 0.72)), (1.9, 0.7, 0.1), rz=yaw)
    for sx in (-0.8, 0.8):
        P.box("Table_Leg", f((sx, 0, 0.35)), (0.05, 0.6, 0.7), rz=yaw)
    P.box("Towel_White", f((0.6, 0, 0.785)), (0.4, 0.5, 0.03), rz=yaw)

def road_case(P, pos, yaw, s=(1.2, 0.6, 0.8)):
    f = lambda v: at(pos, yaw, v)
    P.box("Case_Black", f((0, 0, s[2] / 2 + 0.08)), s, rz=yaw)
    P.box("Case_Edge", f((0, 0, s[2] + 0.08)), (s[0] + 0.02, s[1] + 0.02, 0.03), rz=yaw)
    P.box("Case_Edge", f((0, 0, 0.1)), (s[0] + 0.02, s[1] + 0.02, 0.03), rz=yaw)
    for sx in (-1, 1):
        for sy in (-1, 1):
            P.cyl("Tire", f((sx * (s[0] / 2 - 0.1), sy * (s[1] / 2 - 0.1), 0.04)), f((sx * (s[0] / 2 - 0.1), sy * (s[1] / 2 - 0.1), 0.0)), 0.05, segs=8)

def merch_rack(P, pos, yaw):
    f = lambda v: at(pos, yaw, v)
    P.cyl("Table_Leg", f((-0.7, 0, 0)), f((-0.7, 0, 1.6)), 0.02, segs=6)
    P.cyl("Table_Leg", f((0.7, 0, 0)), f((0.7, 0, 1.6)), 0.02, segs=6)
    P.cyl("Table_Leg", f((-0.7, 0, 1.6)), f((0.7, 0, 1.6)), 0.015, segs=6)
    for k in range(8):
        P.box(("Shirt_A", "Shirt_B", "Shirt_C")[k % 3], f((-0.6 + 0.17 * k, 0, 1.25)), (0.04, 0.5, 0.7), rz=yaw)

def step_and_repeat(P, pos, yaw, w=3.0, h=2.4):
    f = lambda v: at(pos, yaw, v)
    P.box("Backdrop_White", f((0, 0, h / 2 + 0.05)), (w, 0.05, h), rz=yaw)
    for i in range(5):
        for j in range(4):
            if (i + j) % 2 == 0:
                P.box("Backdrop_Logo", f((-w / 2 + 0.3 + i * (w - 0.6) / 4, -0.03, 0.4 + j * (h - 0.6) / 3)),
                      (0.42, 0.01, 0.22), rz=yaw)
    for sx in (-1, 1):
        P.cyl("Light_Body", f((sx * (w / 2 + 0.1), 0.1, 0)), f((sx * (w / 2 + 0.1), 0.1, h + 0.1)), 0.02, segs=6)

def led_wall(P, center, w, h, yaw=0.0, mat="LED_Screen"):
    f = lambda v: at(center, yaw, v)
    P.box("Screen_Frame", f((0, 0.06, 0)), (w + 0.2, 0.1, h + 0.2), rz=yaw)
    P.box(mat, f((0, 0.0, 0)), (w, 0.02, h), rz=yaw)

def barricade_run(P, p0, p1, h=1.1, cover="Barricade_Cover"):
    """Сталева огорожа з м'якою накладкою зверху, секціями по ~2 м."""
    p0, p1 = Vector(p0), Vector(p1)
    d = p1 - p0
    L = d.length
    if L < 0.2:
        return
    a = math.atan2(d.y, d.x)
    n = max(1, round(L / 2.0))
    seg = L / n
    for k in range(n):
        c = p0 + d * ((k + 0.5) / n)
        P.box("Barricade_Steel", (c.x, c.y, h / 2), (seg - 0.06, 0.08, h - 0.1), rz=a)
        P.box("Barricade_Steel", (c.x, c.y, 0.03), (seg - 0.06, 0.7, 0.04), rz=a)       # опорна плита
        P.box(cover, (c.x, c.y, h - 0.02), (seg - 0.04, 0.22, 0.14), rz=a)
        P.box(cover, (c.x, c.y, h * 0.55), (seg - 0.06, 0.1, h * 0.7), rz=a)           # банер-обтяжка

def bike_rack(P, p0, p1, h=1.05):
    p0, p1 = Vector(p0), Vector(p1)
    d = p1 - p0
    n = max(1, round(d.length / 2.3))
    for k in range(n):
        q0 = p0 + d * (k / n)
        q1 = p0 + d * ((k + 1) / n)
        P.cyl("Bike_Rack", (q0.x, q0.y, h), (q1.x, q1.y, h), 0.02, segs=6)
        P.cyl("Bike_Rack", (q0.x, q0.y, 0.12), (q1.x, q1.y, 0.12), 0.02, segs=6)
        for j in range(9):
            q = q0 + (q1 - q0) * (j / 8)
            P.cyl("Bike_Rack", (q.x, q.y, 0.12), (q.x, q.y, h), 0.01, segs=4)

def drape(P, p0, p1, h, mat="Drape_Black", folds=True):
    """Тканина від p0 до p1 заввишки h; складки — зигзагом."""
    p0, p1 = Vector(p0), Vector(p1)
    d = p1 - p0
    n = max(1, int(d.length / 0.3))
    side = Vector((-d.y, d.x, 0)).normalized()
    for k in range(n):
        a = p0 + d * (k / n) + side * (0.05 if (folds and k % 2) else 0.0)
        b = p0 + d * ((k + 1) / n) + side * (0.05 if (folds and (k + 1) % 2) else 0.0)
        P.quad(mat, [(a.x, a.y, 0.0), (b.x, b.y, 0.0), (b.x, b.y, h), (a.x, a.y, h)])
        P.quad(mat, [(a.x, a.y, h), (b.x, b.y, h), (b.x, b.y, 0.0), (a.x, a.y, 0.0)])

def arch_curtain(P, pos, yaw, w=2.44, h=2.6):
    """Вихідна арка: ферма над входом і дві половини завіси (як у справжніх інді-шоу, 8 футів)."""
    f = lambda v: at(pos, yaw, v)
    truss_beam(P, f((-w / 2 - 0.2, 0, 0)), f((-w / 2 - 0.2, 0, h + 0.3)), 0.25)
    truss_beam(P, f((w / 2 + 0.2, 0, 0)), f((w / 2 + 0.2, 0, h + 0.3)), 0.25)
    truss_beam(P, f((-w / 2 - 0.2, 0, h + 0.3)), f((w / 2 + 0.2, 0, h + 0.3)), 0.25)
    drape(P, f((-w / 2, 0.05, 0)), f((-0.05, 0.05, 0)), h, "Drape_Black")
    drape(P, f((0.05, 0.05, 0)), f((w / 2, 0.05, 0)), h, "Drape_Black")
    for sx in (-1, 1):
        par_can(P, f((sx * (w / 2 + 0.2), -0.25, h + 0.3)), f((sx * 1.5, -6.0, 0)))

def stairs(P, base, yaw, rise, width, steps, mat="Stage_Black"):
    """Сходи вгору від base (низ першої сходинки) у напрямку −Y речі."""
    run = 0.3
    for k in range(steps):
        h = rise * (k + 1) / steps
        P.box(mat, at(base, yaw, (0, -(steps - k - 0.5) * run, h / 2)), (width, run, h), rz=yaw)

def railing(P, p0, p1, h=1.05):
    p0, p1 = Vector(p0), Vector(p1)
    d = p1 - p0
    P.cyl("Railing_Steel", p0 + Vector((0, 0, h)), p1 + Vector((0, 0, h)), 0.025, segs=6)
    P.cyl("Railing_Steel", p0 + Vector((0, 0, h * 0.5)), p1 + Vector((0, 0, h * 0.5)), 0.015, segs=6)
    n = max(1, int(d.length / 1.2))
    for k in range(n + 1):
        q = p0 + d * (k / n)
        P.cyl("Railing_Steel", q, q + Vector((0, 0, h)), 0.02, segs=6)

def exit_sign(P, pos, yaw):
    P.box("Exit_Sign", at(pos, yaw, (0, 0, 0)), (0.45, 0.04, 0.18), rz=yaw)

# ---------------------------------------------------------------- ринг

def build_ring(root, spec):
    """Ринг за spec: size_ft (16/18/20), mat_z, кольори. Якорі — як у генераторі анімацій (anchor_*)."""
    ring = empty("ring", root)
    P = Parts("ring", ring)
    half_plat = spec["size_ft"] * 0.3048 / 2       # платформа (ринг міряють за платформою)
    post = half_plat - 0.22                         # стійки — трохи всередині краю платформи
    inner = post - 0.18                             # бійцівський квадрат (до канатів)
    mz = spec["mat_z"]
    ropes = (0.48, 0.86, 1.24)
    post_top = mz + 1.5
    P.box(spec["canvas"], (0, 0, mz - 0.06), (half_plat * 2, half_plat * 2, 0.12))
    for sx, sy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        if sx:
            P.box(spec["apron"], (sx * (half_plat - 0.01), 0, (mz - 0.12) / 2), (0.02, half_plat * 2, mz - 0.12))
            P.box(spec["band"], (sx * (half_plat + 0.003), 0, mz * 0.6), (0.01, half_plat * 1.6, 0.26))
        else:
            P.box(spec["apron"], (0, sy * (half_plat - 0.01), (mz - 0.12) / 2), (half_plat * 2, 0.02, mz - 0.12))
            P.box(spec["band"], (0, sy * (half_plat + 0.003), mz * 0.6), (half_plat * 1.6, 0.01, 0.26))
    corners = {"NE": (1, 1), "NW": (-1, 1), "SE": (1, -1), "SW": (-1, -1)}
    pads = spec["pads"]
    for i_c, (name, (cx, cy)) in enumerate(corners.items()):
        px, py = cx * post, cy * post
        P.cyl(spec["post"], (px, py, mz - 0.1), (px, py, post_top), 0.06, segs=12)
        P.cyl(spec["post"], (px, py, 0.0), (px, py, mz - 0.1), 0.05, segs=8)       # нога під матом
        diag = math.atan2(cy, cx)
        for i, h in enumerate(ropes):
            z = mz + h
            pad = pads[(i_c + i) % len(pads)] if spec.get("mixed_pads") else pads[i_c % len(pads)]
            P.box(pad, (px - cx * 0.16, py - cy * 0.16, z), (0.12, 0.24, 0.26), rz=diag)
            empty("anchor_rope_%s_%d" % (name, i + 1), root, (px - cx * 0.08, py - cy * 0.08, z))
        empty("anchor_corner_top_%s" % name, root, (cx * (post - 0.35), cy * (post - 0.35), mz + ropes[1]))
        empty("anchor_corner_%s" % name, root, (cx * (inner - 0.4), cy * (inner - 0.4), mz))
    empty("anchor_center", root, (0, 0, mz))
    for name, (sx, sy) in {"N": (0, 1), "S": (0, -1), "E": (1, 0), "W": (-1, 0)}.items():
        for i, h in enumerate(ropes):
            empty("anchor_rope_mid_%s_%d" % (name, i + 1), root, (sx * post, sy * post, mz + h))
        empty("anchor_rebound_%s" % name, root, (sx * (inner - 0.3), sy * (inner - 0.3), mz))
        empty("anchor_apron_%s" % name, root, (sx * (half_plat - 0.15), sy * (half_plat - 0.15), mz))
        empty("anchor_floor_%s" % name, root, (sx * (half_plat + 0.8), sy * (half_plat + 0.8), 0.0))
        empty("anchor_under_ring_%s" % name, root, (sx * (half_plat - 0.3), sy * (half_plat - 0.3), 0.0))
    # сталеві сходи на двох кутах
    for corner in spec["steps"]:
        cx, cy = corners[corner]
        d = Vector((cx, cy, 0)).normalized()
        diag = math.atan2(cy, cx)
        base = Vector((cx * (half_plat + 0.1), cy * (half_plat + 0.1), 0))
        for k in range(3):
            h = mz * (k + 1) / 3 - 0.04
            depth = 0.42 * (3 - k)
            c = base + d * (depth / 2)
            P.box(spec["steps_mat"], (c.x, c.y, h / 2), (depth, 0.95, h), rz=diag)
        empty("anchor_steps_%s" % corner, root, tuple(base + d * 1.4))
    # килимки навколо рингу
    mats_half = half_plat + spec.get("mats_extra", 1.3)
    P.box(spec["mats"], (0, 0, 0.015), (mats_half * 2, mats_half * 2, 0.03))
    P.finish()
    return {"half_plat": half_plat, "post": post, "inner": inner, "mat_z": mz, "mats_half": mats_half}

def path_anchors(root, pts):
    for i, p in enumerate(pts):
        empty("anchor_path_%02d" % i, root, (p[0], p[1], p[2] if len(p) > 2 else 0.0))

# ---------------------------------------------------------------- 1. спортзал (інді)

def build_gym(root):
    spec = dict(size_ft=16, mat_z=1.0, canvas="Canvas_Grey", apron="Apron_Blue", band="Apron_Band_White",
                post="Post_Steel", pads=("Pad_Red", "Pad_Blue", "Pad_Yellow"), mixed_pads=True, steps=("NW", "SE"),
                steps_mat="Steps_Steel", mats="Mat_Blue", mats_extra=1.22)
    r = build_ring(root, spec)
    X0, X1, Y0, Y1, H = -18.0, 18.0, -12.0, 14.0, 8.0

    # --- оболонка
    shell = Parts("gym", root)
    shell.box("Gym_Floor", ((X0 + X1) / 2, (Y0 + Y1) / 2, -0.05), (X1 - X0, Y1 - Y0, 0.1))
    # розмітка баскетбольного майданчика (смуги на 2 мм над підлогою)
    cy = 1.0
    CW, CL = 15.24, 28.65
    for sy in (-1, 1):
        shell.box("Gym_Line_White", (0, cy + sy * CW / 2, 0.002), (CL, 0.05, 0.004))
    for sx in (-1, 1):
        shell.box("Gym_Line_White", (sx * CL / 2, cy, 0.002), (0.05, CW, 0.004))
        shell.box("Gym_Paint_Blue", (sx * (CL / 2 - 2.9), cy, 0.001), (5.8, 4.9, 0.002))     # «тришкова» зона
    shell.box("Gym_Line_White", (0, cy, 0.002), (0.05, CW, 0.004))
    for k in range(48):
        a = 2 * math.pi * k / 48
        shell.box("Gym_Line_Red", (1.8 * math.cos(a), cy + 1.8 * math.sin(a), 0.003), (0.25, 0.05, 0.004), rz=a + math.pi / 2)
    shell.finish()

    walls = {}
    t = 0.3
    def wall(name, c, s, band_side):
        P = Parts("wall_" + name, root)
        P.box("Block_Wall", c, s)
        bc = Vector(c) + Vector(band_side) * (t / 2 + 0.005)
        bs = (s[0] if s[0] > s[1] else 0.01, s[1] if s[1] > s[0] else 0.01, 1.3)
        P.box("Wall_Band_Blue", (bc.x, bc.y, 0.65), bs)
        walls[name] = P
        return P
    wall("N", (0, Y1 + t / 2, H / 2), (X1 - X0 + 2 * t, t, H), (0, -1, 0))
    wall("S", (0, Y0 - t / 2, H / 2), (X1 - X0 + 2 * t, t, H), (0, 1, 0))
    wall("E", (X1 + t / 2, (Y0 + Y1) / 2, H / 2), (t, Y1 - Y0, H), (-1, 0, 0))
    # західна стіна з прорізом дверей до роздягальні (y ∈ [−1, 1])
    PW = wall("W", (X0 - t / 2, (Y0 - 1.0) / 2, H / 2), (t, -1.0 - Y0, H), (1, 0, 0))
    PW.box("Block_Wall", (X0 - t / 2, (Y1 + 1.0) / 2, H / 2), (t, Y1 - 1.0, H))
    PW.box("Block_Wall", (X0 - t / 2, 0, 2.4 + (H - 2.4) / 2), (t, 2.0, H - 2.4))
    # вікна високо на південній стіні, двері, табличка «вихід»
    walls["S"].box("Glass", (0, Y0 + 0.01, 6.2), (24.0, 0.04, 1.4))
    for x in (-10, -3, 4, 11):
        walls["S"].box("Steel_Beam", (x, Y0 + 0.03, 6.2), (0.1, 0.05, 1.45))
    door(walls["E"], (X1 - 0.03, -4.0, 0), -math.pi / 2, w=1.8, sign=True)            # вхід глядачів (подвійні)
    door(walls["E"], (X1 - 0.03, 6.0, 0), -math.pi / 2, sign=True)
    door(walls["N"], (-12.0, Y1 - 0.03, 0), math.pi, sign=True)
    exit_sign(PW, (X0 + 0.05, 0, 2.7), math.pi / 2)
    for P in walls.values():
        P.finish()

    ceil = Parts("ceiling", root)
    ceil.box("Ceiling", ((X0 + X1) / 2, (Y0 + Y1) / 2, H + 0.05), (X1 - X0, Y1 - Y0, 0.1))
    for k in range(7):
        x = X0 + 2.0 + k * (X1 - X0 - 4) / 6
        ceil.box("Steel_Beam", (x, (Y0 + Y1) / 2, H - 0.3), (0.25, Y1 - Y0, 0.5))
    for i in range(6):
        for j in range(4):
            ceil.box("Ceiling_Light", (X0 + 3 + i * 6.0, Y0 + 3.5 + j * 6.0, H - 0.6), (1.2, 0.5, 0.12))
    ceil.finish()

    # --- спортзальне: висувні трибуни біля північної стіни, щити, табло
    bl = Parts("bleachers", root)
    rows, rd, rr = 6, 0.75, 0.42
    for k in range(rows):
        y = Y1 - 0.6 - (rows - 1 - k) * rd
        z = 0.42 + k * rr
        bl.box("Bleacher_Wood", (0, y - rd / 2 + 0.2, z), (26.0, 0.32, 0.05))
        bl.box("Bleacher_Wood", (0, y - rd / 2 + 0.55, z - 0.2), (26.0, 0.25, 0.04))         # підніжка
        bl.box("Bleacher_Frame", (0, y, z / 2), (26.0, 0.05, z))
        for s in range(52):
            seat_mark((-12.75 + s * 0.5, y - rd / 2 + 0.2), math.pi, z)                         # лицем на південь
    for x in (-13, -6.5, 0, 6.5, 13):
        bl.box("Bleacher_Frame", (x, Y1 - 0.6 - rows * rd / 2, 0.02), (0.08, rows * rd, 0.04))
    bl.finish()
    hp = Parts("hoops", root)
    for sx in (-1, 1):
        x = sx * (CL / 2 - 1.2)
        hp.box("Hoop_Board", (x, cy, 3.5), (0.05, 1.8, 1.05))
        hp.cyl("Hoop_Rim", (x - sx * 0.08, cy, 3.05), (x - sx * 0.52, cy, 3.05), 0.012, segs=6)
        hp.cyl("Steel_Beam", (x, cy, 4.0), (x + sx * 1.0, cy, H - 0.2), 0.05, segs=8)
    hp.box("Scoreboard", (0, Y1 - 0.08, 6.0), (5.0, 0.15, 1.6))
    for i in range(4):
        hp.box("Digits_Red", (-1.8 + 1.2 * i, Y1 - 0.17, 6.1), (0.5, 0.02, 0.7))
    hp.box("Banner_Red", (X0 + 0.04, -6.0, 4.2), (0.02, 6.0, 1.5))                             # банер промоушену
    hp.box("Banner_Gold", (X0 + 0.05, -6.0, 4.2), (0.02, 5.6, 0.25))
    hp.finish()

    # --- місця: розкладні стільці з трьох боків, прохід від завіси
    ch = Parts("chairs", root)
    m = r["mats_half"]
    chair_rows(ch, (0, -m - 1.0, 0), 0.0, 4, 22, gap_every=11, gap=1.2)                          # південь, лицем на північ
    chair_rows(ch, (m + 1.0, 0, 0), math.pi / 2, 4, 18, gap_every=9, gap=1.2)                     # схід
    chair_rows(ch, (-m - 1.0, 0, 0), -math.pi / 2, 5, 18, gap_every=9, gap=2.2)                   # захід, прохід посередині
    chair_rows(ch, (0, m + 1.0, 0), math.pi, 2, 18, gap_every=9, gap=1.2)                         # північ, перед трибунами
    ch.finish()

    # --- вихід: арка з завісою біля дверей роздягальні, прохід між стільцями до рингу
    ent = Parts("entrance", root)
    arch_curtain(ent, (X0 + 2.2, 0, 0), -math.pi / 2)
    light_tree(ent, (X0 + 2.5, 3.2, 0), (-6, 0, 1.5), h=3.0)
    light_tree(ent, (X0 + 2.5, -3.2, 0), (-6, 0, 1.5), h=3.0)
    speaker_tripod(ent, (X0 + 3.0, 4.5, 0), -math.pi / 2)
    speaker_tripod(ent, (X0 + 3.0, -4.5, 0), -math.pi / 2)
    bike_rack(ent, (X0 + 3.4, 1.1, 0), (-m - 0.2, 1.1, 0))
    bike_rack(ent, (X0 + 3.4, -1.1, 0), (-m - 0.2, -1.1, 0))
    ent.finish()
    empty("anchor_curtain", root, (X0 + 2.0, 0, 0), yaw=-90)
    path_anchors(root, [(X0 + 2.6, 0), (X0 + 6.0, 0), (-8.0, 0), (-m - 0.3, 0), ("steps",)][:-1] +
                 [(-r["half_plat"] - 0.9, r["half_plat"] - 0.2)])

    # --- світло: драбинчаста ферма на чотирьох стійках над рингом
    lt = Parts("lighting", root)
    hz = 4.6
    q = r["mats_half"] + 0.6
    for cx, cy_ in ((1, 1), (-1, 1), (1, -1), (-1, -1)):
        windup_stand(lt, (cx * q, cy_ * q, 0), hz)
    pts = [(q, q, hz), (-q, q, hz), (-q, -q, hz), (q, -q, hz)]
    for k in range(4):
        ladder_truss(lt, pts[k], pts[(k + 1) % 4])
    for k in range(4):
        a, b = Vector(pts[k]), Vector(pts[(k + 1) % 4])
        for j in range(1, 4):
            par_can(lt, a + (b - a) * (j / 4) - Vector((0, 0, 0.3)), (0, 0, r["mat_z"]))
    lt.finish()

    # --- біля рингу: стіл хронометриста й ведучого, дзвін, ноутбук, мікрофон, камера на стояку
    rs = Parts("ringside", root)
    ty = -r["mats_half"] + 0.55
    folding_table(rs, (1.8, ty, 0), 0.0, cloth="Cloth_Black")
    ring_bell(rs, (1.3, ty, 0), 0.78)
    laptop(rs, (2.2, ty, 0), math.pi, 0.76)
    for x in (1.3, 2.3):
        folding_chair(rs, (x, ty - 0.6, 0), math.pi, "Chair_Seat_Black", crowd=False)
    mic_stand(rs, (0.7, ty - 0.3, 0))
    rs.box("Case_Black", (0, -m - 6.3, 0.2), (1.2, 1.2, 0.4))                                    # підставка камери
    hd_camera(rs, (0, -m - 6.3, 0), 0.0, 0.4)
    rs.finish()
    empty("anchor_timekeeper", root, (1.8, ty - 0.6, 0), yaw=180)
    empty("anchor_bell", root, (1.3, ty, 0.78))
    empty("anchor_hardcam", root, (0, -m - 6.3, 1.8), yaw=0)
    for i, (x, y) in enumerate(((m - 0.3, -m + 0.3), (-m + 0.3, -m + 0.3), (m - 0.3, m - 0.3), (-m + 0.3, m - 0.3))):
        empty("anchor_handheld_%d" % (i + 1), root, (x, y, 1.6))

    # --- торгівля: мерч, квитки, перекус біля входу глядачів (схід)
    mc = Parts("merch", root)
    for k, y in enumerate((-9.5, -7.0)):
        folding_table(mc, (X1 - 1.2, y, 0), math.pi / 2, cloth=("Cloth_Red", "Cloth_Black")[k])
        merch_rack(mc, (X1 - 0.5, y, 0), math.pi / 2)
    folding_table(mc, (X1 - 1.2, -1.6, 0), math.pi / 2, cloth="Cloth_Blue")                    # квитки
    folding_table(mc, (X1 - 1.2, 9.0, 0), math.pi / 2, cloth="Cloth_Black")                    # перекус
    for k in range(3):
        cooler(mc, (X1 - 0.6, 8.2 + 0.7 * k, 0), math.pi / 2, ("Cooler_Blue", "Cooler_White")[k % 2])
    trash_can(mc, (X1 - 0.6, 11.0, 0))
    mc.finish()
    empty("anchor_merch", root, (X1 - 2.0, -8.2, 0), yaw=90)
    empty("anchor_tickets", root, (X1 - 2.0, -1.6, 0), yaw=90)

    # --- зброя: стіл під рингом, стос стільців, драбина, бак, кендо-палиці
    wp = Parts("weapons", root)
    folding_table(wp, (X0 + 0.7, -6.5, 0), math.pi / 2, h=0.74)
    chair_stack(wp, (X0 + 0.5, 6.0, 0), -math.pi / 2)
    ladder(wp, (X0 + 0.9, 9.0, 0.0), (X0 + 0.25, 9.0, 3.2))
    trash_can(wp, (-r["half_plat"] - 1.0, -r["half_plat"] - 0.9, 0), "Trash_Grey")
    kendo_bin(wp, (3.0, ty, 0))
    wp.box("Table_Top", (0, r["half_plat"] - 0.4, 0.12), (1.83, 0.76, 0.04))                   # стіл під рингом (складений)
    wp.finish()
    empty("anchor_weapon_table", root, (X0 + 0.9, -6.5, 0))
    empty("anchor_weapon_chairs", root, (X0 + 0.9, 6.0, 0))
    empty("anchor_weapon_ladder", root, (X0 + 1.2, 9.0, 0))
    empty("anchor_weapon_trash", root, (-r["half_plat"] - 1.0, -r["half_plat"] - 0.9, 0))
    empty("anchor_weapon_kendo", root, (3.0, ty, 0))

    # --- закулісся: коридор і роздягальня за західною стіною
    BX0, BX1 = X0 - 14.0, X0
    bk = Parts("backstage", root)
    bk.box("Floor_Tile", ((BX0 + BX1) / 2, 0, -0.05), (BX1 - BX0, 24.0, 0.1))
    bkw = Parts("wall_backstage", root)
    bkw.box("Block_Wall", ((BX0 + BX1) / 2, 12.15, 1.5), (BX1 - BX0, 0.3, 3.0))
    bkw.box("Block_Wall", ((BX0 + BX1) / 2, -12.15, 1.5), (BX1 - BX0, 0.3, 3.0))
    bkw.box("Block_Wall", (BX0 - 0.15, 0, 1.5), (0.3, 24.3, 3.0))
    bkw.box("Block_Wall", (X0 - 3.6, 6.5, 1.5), (0.2, 11.0, 3.0))                              # коридор | роздягальня
    bkw.box("Block_Wall", (X0 - 3.6, -7.0, 1.5), (0.2, 10.0, 3.0))
    bkw.finish()
    door(bk, (X0 - 3.6, -1.5, 0), math.pi / 2, mat="Door_Grey")
    # роздягальня: шафки вздовж стін, лави посередині, дзеркала, кулер, стіл, кушетка лікаря
    lockers(bk, (BX0 + 0.3, 6.0, 0), -math.pi / 2, 14, "Locker_Blue")
    lockers(bk, ((BX0 + X0 - 3.6) / 2, 11.75, 0), math.pi, 18, "Locker_Blue")
    bench(bk, ((BX0 + X0 - 3.6) / 2, 6.5, 0), 0.0, 4.0)
    bench(bk, ((BX0 + X0 - 3.6) / 2, 3.5, 0), 0.0, 4.0)
    mirror_wall(bk, ((BX0 + X0 - 3.6) / 2, -11.75, 0), 0.0, 4, bulbs=False)
    water_cooler(bk, (X0 - 4.1, 10.5, 0))
    folding_table(bk, (BX0 + 1.5, -4.0, 0), math.pi / 2, cloth=None)
    massage_table(bk, (BX0 + 3.5, -8.0, 0), 0.0)
    # коридор (між роздягальнею й залом): «горила» — стіл з монітором біля завіси
    folding_table(bk, (X0 - 1.6, 2.5, 0), math.pi / 2)
    monitor(bk, (X0 - 1.6, 2.5, 0), math.pi / 2, z=0.76)
    water_cooler(bk, (X0 - 0.6, -4.0, 0))
    for k in range(4):
        road_case(bk, (X0 - 1.2, -9.5 + 1.0 * k, 0), math.pi / 2, (0.9, 0.6, 0.7))
    bk.finish()
    bkc = Parts("ceiling_backstage", root)
    bkc.box("Ceiling", ((BX0 + BX1) / 2, 0, 3.05), (BX1 - BX0, 24.0, 0.1))
    for k in range(5):
        bkc.box("Ceiling_Light", ((BX0 + BX1) / 2, -9.0 + 4.5 * k, 2.95), (1.2, 0.3, 0.08))
    bkc.finish()
    empty("anchor_backstage_gorilla", root, (X0 - 1.2, 1.2, 0), yaw=-90)
    empty("anchor_backstage_lockers", root, ((BX0 + X0 - 3.6) / 2, 9.0, 0))
    empty("anchor_backstage_bench", root, ((BX0 + X0 - 3.6) / 2, 5.0, 0))
    empty("anchor_backstage_mirror", root, ((BX0 + X0 - 3.6) / 2, -10.5, 0))
    empty("anchor_backstage_trainer", root, (BX0 + 3.5, -7.0, 0))
    empty("anchor_backstage_interview", root, (X0 - 1.8, -6.5, 0))
    empty("anchor_backstage_exit", root, (BX0 + 0.6, -10.5, 0))
    cams(root, [("cam_entrance", (-4.0, -3.0, 2.2)), ("cam_crowd_low", (0.0, -4.0, 0.6)),
                ("cam_high_wide", (10.0, -10.0, 7.0)), ("cam_backstage", (X0 - 6.0, 0.0, 2.6))])
    return dict(id="gym", title="Шкільний спортзал (інді)", ring=r, spec=spec, ropes=("Red", "White", "Blue"),
                capacity=len(SEATS))

def cams(root, items):
    for name, pos in items:
        empty("anchor_" + name, root, pos)

# ---------------------------------------------------------------- 2. клубна зала з балконом (регіональний)

def build_hall(root):
    spec = dict(size_ft=18, mat_z=1.1, canvas="Canvas_White", apron="Apron_Black", band="Apron_Band_Red",
                post="Post_Black", pads=("Pad_Black",), steps=("NE", "SW"), steps_mat="Steps_Black",
                mats="Mat_Black", mats_extra=1.3)
    r = build_ring(root, spec)
    X0, X1, Y0, Y1, H = -17.0, 17.0, -13.0, 16.0, 10.0
    m = r["mats_half"]

    sh = Parts("hall", root)
    sh.box("Floor_Dark", (0, (Y0 + Y1) / 2, -0.05), (X1 - X0, Y1 - Y0, 0.1))
    sh.finish()
    walls = {n_: Parts("wall_" + n_, root) for n_ in ("N", "S", "E", "W")}
    t = 0.4
    walls["S"].box("Brick_Dark", (0, Y0 - t / 2, H / 2), (X1 - X0 + 2 * t, t, H))
    walls["E"].box("Brick_Dark", (X1 + t / 2, (Y0 + Y1) / 2, H / 2), (t, Y1 - Y0, H))
    walls["W"].box("Brick_Dark", (X0 - t / 2, (Y0 + Y1) / 2, H / 2), (t, Y1 - Y0, H))
    # північна стіна (за сценою) з проходом у закулісся посередині (x ∈ [−1.2, 1.2], до 2,8 м)
    walls["N"].box("Brick_Dark", ((X0 - 1.2) / 2, Y1 + t / 2, H / 2), (-1.2 - X0 + t, t, H))
    walls["N"].box("Brick_Dark", ((X1 + 1.2) / 2, Y1 + t / 2, H / 2), (X1 - 1.2 + t, t, H))
    walls["N"].box("Brick_Dark", (0, Y1 + t / 2, 2.8 + (H - 2.8) / 2), (2.4, t, H - 2.8))
    for P_, pos, yaw in ((walls["E"], (X1 - 0.05, -8.0, 0), -math.pi / 2), (walls["W"], (X0 + 0.05, -8.0, 0), math.pi / 2),
                         (walls["S"], (-6.0, Y0 + 0.05, 0), 0.0), (walls["S"], (6.0, Y0 + 0.05, 0), 0.0)):
        door(P_, pos, yaw, w=1.6, mat="Door_Wood", sign=True)
    for P_ in walls.values():
        P_.finish()
    ceil = Parts("ceiling", root)
    ceil.box("Ceiling", (0, (Y0 + Y1) / 2, H + 0.05), (X1 - X0, Y1 - Y0, 0.1))
    for k in range(8):
        ceil.box("Steel_Beam", (X0 + 2 + k * (X1 - X0 - 4) / 7, (Y0 + Y1) / 2, H - 0.25), (0.3, Y1 - Y0, 0.45))
    ceil.finish()

    # --- сцена з виходом: завіса, LED-екран над нею, банери, колонки, сходи вниз, прохід між огорожами
    st = Parts("stage", root)
    SY0, SH = 11.0, 1.2
    st.box("Stage_Black", (0, (SY0 + Y1) / 2, SH / 2), (16.0, Y1 - SY0, SH))
    drape(st, (-8.0, Y1 - 0.2, SH), (-1.2, Y1 - 0.2, SH), 6.0)
    drape(st, (1.2, Y1 - 0.2, SH), (8.0, Y1 - 0.2, SH), 6.0)
    for sx in (-1, 1):                                                                          # куліси
        drape(st, (sx * 8.0, SY0 + 0.2, SH), (sx * 8.0, Y1 - 0.2, SH), 6.0)
    st.finish()
    for P_ in (st,):
        pass
    ent = Parts("entrance", root)
    # завіса виходу посередині, над нею невеликий екран 4 × 2,25 м
    drape(ent, (-1.2, Y1 - 0.25, SH), (0.0, Y1 - 0.25, SH), 2.8, "Drape_Red")
    drape(ent, (0.0, Y1 - 0.25, SH), (1.2, Y1 - 0.25, SH), 2.8, "Drape_Red")
    led_wall(ent, (0, Y1 - 0.45, SH + 4.2), 4.0, 2.25)
    for sx in (-1, 1):
        ent.box("Banner_Black", (sx * 4.5, Y1 - 0.35, SH + 3.4), (1.4, 0.03, 4.2))
        ent.box("Banner_Gold", (sx * 4.5, Y1 - 0.37, SH + 3.4), (1.2, 0.02, 0.3))
        speaker_stack(ent, (sx * 7.2, SY0 + 0.8, SH), 0.0, n=2)
        light_tree(ent, (sx * 5.5, SY0 + 0.6, SH), (0, 2.0, 1.2), h=2.4)
    stairs(ent, (0, SY0, 0), 0.0, SH, 2.4, 4)                                                   # сходи зі сцени
    barricade_run(ent, (-1.6, SY0 - 1.3, 0), (-1.6, m + 0.2, 0))
    barricade_run(ent, (1.6, SY0 - 1.3, 0), (1.6, m + 0.2, 0))
    ent.finish()
    empty("anchor_curtain", root, (0, Y1 - 0.6, SH), yaw=180)
    path_anchors(root, [(0, Y1 - 1.2, SH), (0, SY0 + 0.4, SH), (0, SY0 - 1.4, 0), (0, m + 0.6, 0),
                        (r["half_plat"] + 0.9, r["half_plat"] - 0.2, 0)])

    # --- огорожа навколо рингу (прохід з півночі й вихід хронометриста з півдня)
    bar = Parts("barricade", root)
    q = m + 0.9
    barricade_run(bar, (-q, q, 0), (-1.6, q, 0))
    barricade_run(bar, (1.6, q, 0), (q, q, 0))
    barricade_run(bar, (q, q, 0), (q, -q, 0))
    barricade_run(bar, (q, -q, 0), (1.0, -q, 0))
    barricade_run(bar, (-1.0, -q, 0), (-q, -q, 0))
    barricade_run(bar, (-q, -q, 0), (-q, q, 0))
    bar.finish()

    # --- біля рингу: коментатори (схід), хронометрист (захід), камера на помості (південь), ручні камери
    rs = Parts("ringside", root)
    cx = r["half_plat"] + 1.0
    folding_table(rs, (cx, -1.2, 0), -math.pi / 2, length=2.4, cloth="Cloth_Black")
    for k in range(2):
        folding_chair(rs, (cx + 0.6, -1.8 + 1.2 * k, 0), -math.pi / 2, crowd=False)
        monitor(rs, (cx - 0.15, -1.8 + 1.2 * k, 0), math.pi / 2, z=0.76)
    folding_table(rs, (-cx, -1.0, 0), math.pi / 2, cloth="Cloth_Black")
    ring_bell(rs, (-cx, -1.5, 0), 0.78)
    laptop(rs, (-cx, -0.5, 0), -math.pi / 2, 0.76)
    folding_chair(rs, (-cx - 0.6, -1.0, 0), math.pi / 2, crowd=False)
    rs.box("Stage_Black", (0, Y0 + 2.2, 0.6), (2.0, 2.0, 1.2))                                   # поміст камери
    railing(rs, (-1.0, Y0 + 3.2, 1.2), (1.0, Y0 + 3.2, 1.2))
    hd_camera(rs, (0, Y0 + 2.2, 0), 0.0, 1.2)
    folding_table(rs, (3.0, Y0 + 2.0, 0), 0.0, length=2.4, cloth="Cloth_Black")                 # стіл режисера трансляції
    for k in range(3):
        monitor(rs, (2.2 + 0.8 * k, Y0 + 2.2, 0), math.pi, z=0.76)
    rs.finish()
    empty("anchor_commentary", root, (cx + 0.6, -1.2, 0), yaw=-90)
    empty("anchor_timekeeper", root, (-cx - 0.6, -1.0, 0), yaw=90)
    empty("anchor_bell", root, (-cx, -1.5, 0.78))
    empty("anchor_hardcam", root, (0, Y0 + 2.2, 2.6))
    for i, (x, y) in enumerate(((q - 0.4, -q + 0.4), (-q + 0.4, -q + 0.4), (q - 0.4, q - 0.4), (-q + 0.4, q - 0.4))):
        empty("anchor_handheld_%d" % (i + 1), root, (x, y, 1.6))

    # --- партер: стільці рядами за огорожею, біля сцени — стоячі місця
    ch = Parts("chairs", root)
    chair_rows(ch, (0, -q - 1.0, 0), 0.0, 7, 26, gap_every=13, gap=1.3)
    chair_rows(ch, (q + 1.0, -1.0, 0), math.pi / 2, 6, 20, gap_every=10, gap=1.3)
    chair_rows(ch, (-q - 1.0, -1.0, 0), -math.pi / 2, 6, 20, gap_every=10, gap=1.3)
    ch.finish()
    for k in range(60):                                                                         # стоячі біля сцени
        x = -9.0 + (k % 12) * 1.5 + 0.3 * h01(k, 1)
        y = SY0 - 2.5 - (k // 12) * 0.9 + 0.3 * h01(k, 2)
        if abs(x) < 2.2:
            continue
        SEATS.append((x, y, 0.0, 180.0))

    # --- балкон буквою П уздовж півдня, сходу й заходу, з поруччям, колонами й двома рядами стільців
    bc = Parts("balcony", root)
    BZ, BD = 4.0, 3.2
    bc.box("Concrete", (0, Y0 + BD / 2, BZ - 0.15), (X1 - X0, BD, 0.3))
    bc.box("Concrete", (X1 - BD / 2, (Y0 + BD + SY0 - 1.0) / 2, BZ - 0.15), (BD, SY0 - 1.0 - Y0 - BD, 0.3))
    bc.box("Concrete", (X0 + BD / 2, (Y0 + BD + SY0 - 1.0) / 2, BZ - 0.15), (BD, SY0 - 1.0 - Y0 - BD, 0.3))
    railing(bc, (X0 + BD, Y0 + BD, BZ), (X1 - BD, Y0 + BD, BZ))
    railing(bc, (X1 - BD, Y0 + BD, BZ), (X1 - BD, SY0 - 1.0, BZ))
    railing(bc, (X0 + BD, Y0 + BD, BZ), (X0 + BD, SY0 - 1.0, BZ))
    for x in range(-12, 13, 4):
        bc.cyl("Steel_Beam", (x, Y0 + BD - 0.2, 0), (x, Y0 + BD - 0.2, BZ - 0.3), 0.12, segs=10)
    for y in range(-6, 10, 4):
        for sx in (-1, 1):
            bc.cyl("Steel_Beam", (sx * (X1 - BD + 0.2), y, 0), (sx * (X1 - BD + 0.2), y, BZ - 0.3), 0.12, segs=10)
    for sx in (-1, 1):                                                                          # сходи на балкон
        for k in range(14):
            h = BZ * (k + 1) / 14
            bc.box("Concrete", (sx * (X1 - 1.0), Y0 + BD + 0.2 + k * 0.28 + 0.14, h / 2), (1.6, 0.28, h))
    bc.finish()
    bch = Parts("balcony_chairs", root)
    for r_ in range(2):
        for k in range(50):
            x = -12.5 + k * 0.52
            folding_chair(bch, (x, Y0 + BD - 0.8 - r_ * 1.0, BZ), 0.0, "Chair_Seat_Red")
            SEATS[-1] = (SEATS[-1][0], SEATS[-1][1], BZ + 0.45, SEATS[-1][3])
        for sx in (-1, 1):
            for k in range(26):
                y = Y0 + BD + 0.6 + k * 0.52
                folding_chair(bch, (sx * (X1 - BD + 0.8 + r_ * 1.0), y, BZ), sx * math.pi / 2, "Chair_Seat_Red")
                SEATS[-1] = (SEATS[-1][0], SEATS[-1][1], BZ + 0.45, SEATS[-1][3])
    # стільці балкона стоять на балконі: геометрію піднято
    bch_obs = bch.finish()
    for o in bch_obs:
        o.location.z = BZ

    # --- бар уздовж сходу під балконом
    br = Parts("bar", root)
    br.box("Bar_Wood", (X1 - 1.6, 4.0, 0.55), (0.7, 7.0, 1.1))
    br.box("Bar_Top", (X1 - 1.6, 4.0, 1.12), (0.9, 7.2, 0.05))
    for k in range(3):
        br.box("Bar_Wood", (X1 - 0.3, 4.0, 1.2 + 0.45 * k), (0.35, 7.0, 0.04))
        for j in range(24):
            br.cyl("Bottle", (X1 - 0.3, 0.8 + j * 0.28, 1.22 + 0.45 * k), (X1 - 0.3, 0.8 + j * 0.28, 1.5 + 0.45 * k), 0.035, segs=6)
    br.box("Shelf_Light", (X1 - 0.14, 4.0, 2.7), (0.04, 7.0, 0.06))
    for k in range(6):
        br.cyl("Chair_Frame", (X1 - 2.4, 1.2 + k * 1.1, 0), (X1 - 2.4, 1.2 + k * 1.1, 0.75), 0.03, segs=6)
        br.cyl("Chair_Seat_Black", (X1 - 2.4, 1.2 + k * 1.1, 0.75), (X1 - 2.4, 1.2 + k * 1.1, 0.8), 0.19, segs=12)
    br.finish()
    empty("anchor_bar", root, (X1 - 2.6, 4.0, 0), yaw=-90)

    # --- мерч під балконом на заході
    mc = Parts("merch", root)
    for k in range(3):
        folding_table(mc, (X0 + 1.2, 2.0 + 2.0 * k, 0), -math.pi / 2, cloth="Cloth_Black")
        merch_rack(mc, (X0 + 0.5, 2.0 + 2.0 * k, 0), -math.pi / 2)
    mc.finish()
    empty("anchor_merch", root, (X0 + 2.0, 4.0, 0), yaw=-90)

    # --- світло: підвісна коробка 32 × 32 фути над рингом, фронтальна ферма над сценою
    lt = Parts("lighting", root)
    hb = 6.5
    b = 9.75 / 2
    pts = [(b, b, hb), (-b, b, hb), (-b, -b, hb), (b, -b, hb)]
    for k in range(4):
        truss_beam(lt, pts[k], pts[(k + 1) % 4])
        lt.cyl("Steel_Beam", pts[k], (pts[k][0], pts[k][1], H - 0.3), 0.012, segs=4)            # підвіс
        a, c = Vector(pts[k]), Vector(pts[(k + 1) % 4])
        for j in range(1, 6):
            p = a + (c - a) * (j / 6) - Vector((0, 0, 0.25))
            if j % 2:
                par_can(lt, p, (0, 0, r["mat_z"]))
            else:
                moving_head(lt, p, (0, 0, r["mat_z"]))
    truss_beam(lt, (-8.0, SY0 - 1.0, 7.0), (8.0, SY0 - 1.0, 7.0))
    for k in range(7):
        par_can(lt, (-6.0 + 2.0 * k, SY0 - 1.0, 6.7), (0, SY0 + 2.0, SH + 1.0),
                ("Light_Lens", "Light_Lens_Blue", "Light_Lens_Red")[k % 3])
    lt.finish()

    # --- зброя
    wp = Parts("weapons", root)
    chair_stack(wp, (X0 + 0.5, -3.0, 0), -math.pi / 2)
    folding_table(wp, (X0 + 0.7, -6.0, 0), math.pi / 2)
    ladder(wp, (-q - 0.6, q + 0.4, 0), (-q - 0.1, q + 0.4, 3.0))
    trash_can(wp, (q + 0.5, -q - 0.5, 0), "Trash_Grey")
    kendo_bin(wp, (-cx, -2.4, 0))
    wp.box("Table_Top", (0, -r["half_plat"] + 0.4, 0.12), (1.83, 0.76, 0.04))
    wp.finish()
    empty("anchor_weapon_table", root, (X0 + 0.9, -6.0, 0))
    empty("anchor_weapon_chairs", root, (X0 + 0.9, -3.0, 0))
    empty("anchor_weapon_ladder", root, (-q - 0.4, q + 0.4, 0))
    empty("anchor_weapon_trash", root, (q + 0.5, -q - 0.5, 0))
    empty("anchor_weapon_kendo", root, (-cx, -2.4, 0))

    # --- закулісся за північною стіною: коридор, дві гримерки, «горила», інтерв'ю, кейтеринг, лікар
    BY0, BY1 = Y1 + 0.4, Y1 + 12.0
    bk = Parts("backstage", root)
    bk.box("Floor_Tile", (0, (BY0 + BY1) / 2, -0.05), (X1 - X0, BY1 - BY0, 0.1))
    bkw = Parts("wall_backstage", root)
    bkw.box("Wall_Grey", (0, BY1 + 0.15, 1.6), (X1 - X0, 0.3, 3.2))
    bkw.box("Wall_Grey", (X0 - 0.15, (BY0 + BY1) / 2, 1.6), (0.3, BY1 - BY0, 3.2))
    bkw.box("Wall_Grey", (X1 + 0.15, (BY0 + BY1) / 2, 1.6), (0.3, BY1 - BY0, 3.2))
    bkw.box("Wall_Grey", ((X0 - 1.0) / 2 - 1.0, BY0 + 3.6, 1.6), (-1.0 - X0 - 2.0, 0.2, 3.2))       # коридор | гримерки
    bkw.box("Wall_Grey", ((X1 + 1.0) / 2 + 1.0, BY0 + 3.6, 1.6), (X1 - 1.0 - 2.0, 0.2, 3.2))
    bkw.box("Wall_Grey", (0, (BY0 + 3.6 + BY1) / 2, 1.6), (0.2, BY1 - BY0 - 3.6, 3.2))
    bkw.finish()
    for sx in (-1, 1):
        door(bk, (sx * 5.0, BY0 + 3.6, 0), 0.0, mat="Door_Grey")
        mirror_wall(bk, (sx * 9.0, BY1 - 0.2, 0), math.pi, 4)
        bench(bk, (sx * 8.0, BY0 + 6.5, 0), 0.0, 3.0)
        lockers(bk, (sx * (X1 - 0.3), BY0 + 7.5, 0), sx * math.pi / 2 + (math.pi if sx > 0 else 0), 8, "Locker_Grey")
    for k in range(6):                                                                           # вішаки з костюмами
        bk.box(("Shirt_A", "Shirt_B", "Shirt_C")[k % 3], (-3.0 - 0.25 * k, BY1 - 2.0, 1.2), (0.05, 0.5, 0.9))
    massage_table(bk, (4.0, BY1 - 2.5, 0), 0.0)
    # «горила» одразу за проходом зі сцени: стіл з моніторами, гарнітура
    folding_table(bk, (2.6, BY0 + 1.5, 0), math.pi / 2, length=1.83, cloth="Cloth_Black")
    for k in range(2):
        monitor(bk, (2.6, BY0 + 1.0 + k * 0.9, 0), math.pi / 2, z=0.76)
    step_and_repeat(bk, (X1 - 2.5, BY0 + 1.7, 0), -math.pi / 2, 2.8, 2.3)                       # інтерв'ю
    for k in range(2):
        light_tree(bk, (X1 - 5.0, BY0 + 0.8 + 1.8 * k, 0), (X1 - 2.5, BY0 + 1.7, 1.6), h=1.8, n=2)
    for k in range(2):                                                                           # кейтеринг
        folding_table(bk, (X0 + 2.0 + 2.0 * k, BY0 + 1.8, 0), 0.0, cloth="Cloth_Blue")
        for j in range(4):
            bk.box("Food", (X0 + 1.4 + 2.0 * k + 0.4 * j, BY0 + 1.8, 0.8), (0.3, 0.4, 0.06))
    water_cooler(bk, (X0 + 6.0, BY0 + 0.6, 0))
    for k in range(5):
        road_case(bk, (X1 - 7.0 - 1.3 * k, BY0 + 0.5, 0), 0.0)
    bk.finish()
    bkc = Parts("ceiling_backstage", root)
    bkc.box("Ceiling", (0, (BY0 + BY1) / 2, 3.25), (X1 - X0, BY1 - BY0, 0.1))
    bkc.finish()
    empty("anchor_backstage_gorilla", root, (1.6, BY0 + 1.4, 0), yaw=-90)
    empty("anchor_backstage_lockers", root, (-(X1 - 1.0), BY0 + 7.5, 0))
    empty("anchor_backstage_bench", root, (-8.0, BY0 + 6.0, 0))
    empty("anchor_backstage_mirror", root, (-9.0, BY1 - 1.0, 0))
    empty("anchor_backstage_trainer", root, (4.0, BY1 - 3.3, 0))
    empty("anchor_backstage_interview", root, (X1 - 3.3, BY0 + 1.7, 0), yaw=-90)
    empty("anchor_backstage_catering", root, (X0 + 3.0, BY0 + 1.0, 0))
    empty("anchor_backstage_exit", root, (X0 + 0.6, BY1 - 0.6, 0))
    cams(root, [("cam_entrance", (0.0, 3.0, 2.4)), ("cam_crowd_low", (0.0, -5.5, 0.6)),
                ("cam_high_wide", (12.0, -11.0, 8.0)), ("cam_balcony", (0.0, Y0 + 1.5, BZ + 1.6)),
                ("cam_backstage", (0.0, BY0 + 2.0, 2.6))])
    return dict(id="hall", title="Клубна зала з балконом (регіональний)", ring=r, spec=spec,
                ropes=("Black", "Black", "Black"), capacity=len(SEATS))

# ---------------------------------------------------------------- 3. палац спорту (середній промоушен)

def tier(P, side, start, rows, rise, depth, length, z0, seat_mat, aisle_every=20, skip=None, back_wall=True):
    """Ярус трибуни вздовж боку side ('S','E','W','N'): start — відстань від центру до першого ряду."""
    for r_ in range(rows):
        d = start + r_ * depth
        z = z0 + r_ * rise
        n = int(length / 0.5)
        if side in ("S", "N"):
            sgn = -1 if side == "S" else 1
            P.box("Tier_Concrete", (0, sgn * (d + depth / 2), z / 2), (length, depth, z))
            yaw = 0.0 if side == "S" else math.pi
            for k in range(n):
                x = -length / 2 + 0.25 + k * 0.5
                if (k % aisle_every) in (0, 1) or (skip and skip(x, r_)):
                    continue
                stadium_seat(P, (x, sgn * (d + 0.35), z), yaw, seat_mat)
        else:
            sgn = 1 if side == "E" else -1
            P.box("Tier_Concrete", (sgn * (d + depth / 2), 0, z / 2), (depth, length, z))
            yaw = math.pi / 2 if side == "E" else -math.pi / 2
            for k in range(n):
                y = -length / 2 + 0.25 + k * 0.5
                if (k % aisle_every) in (0, 1) or (skip and skip(y, r_)):
                    continue
                stadium_seat(P, (sgn * (d + 0.35), y, z), yaw, seat_mat)
    # стінка перед першим рядом
    if side in ("S", "N"):
        sgn = -1 if side == "S" else 1
        P.box("Tier_Front", (0, sgn * (start - 0.05), z0 / 2 + 0.5), (length, 0.1, z0 + 1.0))
    else:
        sgn = 1 if side == "E" else -1
        P.box("Tier_Front", (sgn * (start - 0.05), 0, z0 / 2 + 0.5), (0.1, length, z0 + 1.0))

def stadium_seat(P, pos, yaw, mat):
    f = lambda v: at(pos, yaw, v)
    P.box(mat, f((0, 0, 0.44)), (0.44, 0.42, 0.06), rz=yaw)
    P.box(mat, f((0, 0.2, 0.7)), (0.44, 0.05, 0.45), rz=yaw)
    P.box("Chair_Frame", f((0, 0.05, 0.22)), (0.06, 0.06, 0.44), rz=yaw)
    seat_mark(pos, yaw, pos[2] + 0.44)

def build_arena(root):
    spec = dict(size_ft=20, mat_z=1.2, canvas="Canvas_White", apron="Apron_Black", band="Apron_Band_Gold",
                post="Post_Black", pads=("Pad_Black",), steps=("NE", "SW"), steps_mat="Steps_Black",
                mats="Mat_Black", mats_extra=1.4)
    r = build_ring(root, spec)
    m = r["mats_half"]
    FX, FY0, FY1 = 22.0, -14.0, 20.0         # підлога арени
    H = 22.0

    sh = Parts("arena", root)
    sh.box("Floor_Dark", (0, (FY0 + FY1) / 2, -0.05), (2 * FX + 40, FY1 - FY0 + 40, 0.1))
    sh.finish()

    # --- трибуни: нижній ярус з трьох боків (південь, схід, захід), верхній — на сході й заході
    lower = Parts("tiers_lower", root)
    vom = lambda u, r_: abs(u) < 1.6 and r_ < 6                                                  # вомиторії (проходи)
    tier(lower, "S", -FY0, 14, 0.42, 0.85, 2 * FX, 1.1, "Seat_Red", skip=lambda x, r_: vom(x, r_))
    tier(lower, "E", FX, 14, 0.42, 0.85, FY1 - FY0 - 6, 1.1, "Seat_Red", skip=lambda y, r_: vom(y - 2, r_))
    tier(lower, "W", FX, 14, 0.42, 0.85, FY1 - FY0 - 6, 1.1, "Seat_Red", skip=lambda y, r_: vom(y - 2, r_))
    lower.finish()
    upper = Parts("tiers_upper", root)
    tier(upper, "E", FX + 14 * 0.85 + 2.5, 9, 0.5, 0.9, FY1 - FY0 - 4, 8.8, "Seat_Blue", aisle_every=18)
    tier(upper, "W", FX + 14 * 0.85 + 2.5, 9, 0.5, 0.9, FY1 - FY0 - 4, 8.8, "Seat_Blue", aisle_every=18)
    tier(upper, "S", -FY0 + 14 * 0.85 + 2.5, 9, 0.5, 0.9, 2 * FX, 8.8, "Seat_Blue", aisle_every=18)
    upper.finish()
    # стіни й дах арени (окремо — ізометрична камера ховає ближні)
    OX, OY0, OY1 = FX + 26.0, FY0 - 26.0, FY1 + 16.0
    for name, c, s in (("S", (0, OY0, H / 2), (2 * OX, 0.5, H)), ("E", (OX, (OY0 + OY1) / 2, H / 2), (0.5, OY1 - OY0, H)),
                       ("W", (-OX, (OY0 + OY1) / 2, H / 2), (0.5, OY1 - OY0, H)), ("N", (0, OY1, H / 2), (2 * OX, 0.5, H))):
        P_ = Parts("wall_" + name, root)
        P_.box("Wall_Dark", c, s)
        P_.finish()
    ro = Parts("ceiling", root)
    ro.box("Ceiling", (0, (OY0 + OY1) / 2, H + 0.1), (2 * OX, OY1 - OY0, 0.2))
    for k in range(9):
        y = OY0 + 4 + k * (OY1 - OY0 - 8) / 8
        ro.box("Steel_Beam", (0, y, H - 1.2), (2 * OX, 0.5, 2.2))
    ro.finish()
    # банери на стінах, табличка «вихід» над вомиторіями
    dec = Parts("decor", root)
    for k in range(6):
        dec.box(("Banner_Red", "Banner_Black", "Banner_Gold")[k % 3], (-15.0 + 6.0 * k, OY0 + 0.3, 16.0), (3.0, 0.05, 4.5))
    for sx in (-1, 1):
        for y in (2.0,):
            exit_sign(dec, (sx * (FX - 0.1), y, 2.6), sx * math.pi / 2)
    exit_sign(dec, (0, FY0 + 0.1, 2.6), 0.0)
    dec.finish()

    # --- вихідна сцена на півночі: LED-стіна 12 × 6,5 м, два бічні екрани, рамна ферма зі світлом, рампа
    st = Parts("stage", root)
    SY0, SY1, SH = FY1 - 6.0, FY1, 1.5
    st.box("Stage_Black", (0, (SY0 + SY1) / 2, SH / 2), (24.0, SY1 - SY0, SH))
    st.finish()
    ent = Parts("entrance", root)
    led_wall(ent, (-3.75, SY1 - 0.4, SH + 3.5), 4.5, 6.5)
    led_wall(ent, (3.75, SY1 - 0.4, SH + 3.5), 4.5, 6.5)
    led_wall(ent, (0, SY1 - 0.4, SH + 5.3), 3.0, 2.9)                                           # над проходом
    drape(ent, (-1.5, SY1 - 0.3, SH), (1.5, SY1 - 0.3, SH), 3.6, "Drape_Black")
    for sx in (-1, 1):
        led_wall(ent, (sx * 9.5, SY1 - 1.6, SH + 3.0), 3.0, 5.0, yaw=sx * math.radians(-20), mat="LED_Side")
        truss_beam(ent, (sx * 11.5, SY0 + 0.5, SH), (sx * 11.5, SY0 + 0.5, SH + 8.5), 0.4)
        speaker_stack(ent, (sx * 11.0, SY0 + 2.0, SH), 0.0, n=3)
    truss_beam(ent, (-11.5, SY0 + 0.5, SH + 8.5), (11.5, SY0 + 0.5, SH + 8.5), 0.4)
    for k in range(9):
        moving_head(ent, (-9.0 + 2.25 * k, SY0 + 0.5, SH + 8.1), (0, SY0 - 4.0, SH))
    # рампа зі сцени до рингу, підсвічені краї, огорожа вздовж
    RY0 = m + 0.9
    ramp_len = SY0 - RY0
    ent.box("Ramp_Black", (0, (SY0 + RY0) / 2, SH / 2), (3.0, ramp_len, SH))
    for sx in (-1, 1):
        ent.box("LED_Strip", (sx * 1.52, (SY0 + RY0) / 2, SH + 0.005), (0.05, ramp_len, 0.02))
        barricade_run(ent, (sx * 2.6, SY0 - 0.2, 0), (sx * 2.6, RY0, 0))
    stairs(ent, (0, RY0, 0), math.pi, SH, 2.6, 5, "Ramp_Black")                                # з рампи на підлогу біля рингу
    for k in range(6):
        empty("anchor_pyro_%d" % (k + 1), root, (-10.0 + 4.0 * k, SY0 + 0.3, SH))
    ent.finish()
    empty("anchor_curtain", root, (0, SY1 - 0.8, SH), yaw=180)
    path_anchors(root, [(0, SY1 - 1.4, SH), (0, SY0 - 0.2, SH), (0, (SY0 + RY0) / 2, SH),
                        (0, RY0 - 0.3 - 1.5, 0), (r["half_plat"] + 0.9, r["half_plat"] - 0.2, 0)])

    # --- огорожа навколо рингу з проходом під рампу
    bar = Parts("barricade", root)
    q = m + 1.0
    barricade_run(bar, (-q, q, 0), (-2.6, q, 0))
    barricade_run(bar, (2.6, q, 0), (q, q, 0))
    barricade_run(bar, (q, q, 0), (q, -q, 0))
    barricade_run(bar, (q, -q, 0), (-q, -q, 0))
    barricade_run(bar, (-q, -q, 0), (-q, q, 0))
    bar.finish()

    # --- біля рингу: стіл коментаторів з передньою панеллю, хронометрист, поміст камери, кран, режисерський пульт
    rs = Parts("ringside", root)
    cx = q + 1.3
    rs.box("Table_Top", (cx, 0, 0.76), (1.0, 3.6, 0.05))
    rs.box("Banner_Black", (cx - 0.5, 0, 0.38), (0.04, 3.6, 0.76))
    rs.box("Banner_Gold", (cx - 0.53, 0, 0.55), (0.02, 3.0, 0.15))
    for k in range(3):
        folding_chair(rs, (cx + 0.75, -1.2 + 1.2 * k, 0), -math.pi / 2, crowd=False)
        monitor(rs, (cx - 0.25, -1.2 + 1.2 * k, 0), math.pi / 2, z=0.79)
    rs.box("Backdrop_White", (cx + 1.6, 0, 1.4), (0.05, 4.0, 2.8))
    folding_table(rs, (-q + 0.6, -1.0, 0), math.pi / 2, cloth="Cloth_Black")                     # хронометрист усередині огорожі
    ring_bell(rs, (-q + 0.6, -1.5, 0), 0.78)
    laptop(rs, (-q + 0.6, -0.5, 0), -math.pi / 2, 0.76)
    folding_chair(rs, (-q + 0.05, -1.0, 0), math.pi / 2, crowd=False)
    rs.box("Stage_Black", (0, FY0 + 3.0, 1.1), (3.4, 2.4, 2.2))                                 # поміст основної камери
    railing(rs, (-1.7, FY0 + 4.2, 2.2), (1.7, FY0 + 4.2, 2.2))
    hd_camera(rs, (-0.7, FY0 + 3.0, 0), 0.0, 2.2)
    hd_camera(rs, (0.7, FY0 + 3.0, 0), 0.0, 2.2)
    jib(rs, (q + 3.5, -q - 3.5, 0), (0, 0, 1.5))
    rs.box("Stage_Black", (0, FY0 + 0.9, 0.3), (6.0, 1.6, 0.6))                                  # режисерський пульт
    folding_table(rs, (0, FY0 + 0.9, 0.6), 0.0, length=5.0, cloth="Cloth_Black")
    for k in range(5):
        monitor(rs, (-2.0 + k, FY0 + 1.1, 0), math.pi, z=1.36)
    rs.finish()
    empty("anchor_commentary", root, (cx + 0.75, 0, 0), yaw=-90)
    empty("anchor_timekeeper", root, (-q + 0.05, -1.0, 0), yaw=90)
    empty("anchor_bell", root, (-q + 0.6, -1.5, 0.78))
    empty("anchor_hardcam", root, (0, FY0 + 3.0, 3.6))
    empty("anchor_jib", root, (q + 3.5, -q - 3.5, 3.5))
    for i, (x, y) in enumerate(((q - 0.4, -q + 0.4), (-q + 0.4, -q + 0.4), (q - 0.4, q - 0.4), (-q + 0.4, q - 0.4))):
        empty("anchor_handheld_%d" % (i + 1), root, (x, y, 1.6))

    # --- стільці на підлозі арени (ближні до рингу ряди) — за огорожею з трьох боків
    ch = Parts("chairs", root)
    chair_rows(ch, (0, -q - 1.2, 0), 0.0, 6, 34, gap_every=17, gap=1.4, seat="Chair_Seat_Black",
               skip=lambda p_: abs(p_.x) < 2.2 and p_.y < FY0 + 5.0)
    chair_rows(ch, (q + 3.2, -2.0, 0), math.pi / 2, 5, 22, gap_every=11, gap=1.4, seat="Chair_Seat_Black",
               skip=lambda p_: p_.x > FX - 1.0)
    chair_rows(ch, (-q - 1.2, -2.0, 0), -math.pi / 2, 6, 22, gap_every=11, gap=1.4, seat="Chair_Seat_Black",
               skip=lambda p_: p_.x < -FX + 1.0)
    ch.finish()

    # --- підвісне світло: коробка 40 × 40 футів, лінійні масиви по кутах, відеокуб над рингом
    lt = Parts("lighting", root)
    hb = 10.0
    b = 12.2 / 2
    pts = [(b, b, hb), (-b, b, hb), (-b, -b, hb), (b, -b, hb)]
    for k in range(4):
        truss_beam(lt, pts[k], pts[(k + 1) % 4], 0.45)
        lt.cyl("Steel_Beam", pts[k], (pts[k][0], pts[k][1], H - 2.3), 0.014, segs=4)
        a, c = Vector(pts[k]), Vector(pts[(k + 1) % 4])
        for j in range(1, 8):
            p = a + (c - a) * (j / 8) - Vector((0, 0, 0.35))
            if j % 2:
                moving_head(lt, p, (0, 0, r["mat_z"]))
            else:
                par_can(lt, p, (0, 0, r["mat_z"]))
        line_array(lt, (pts[k][0] * 1.25, pts[k][1] * 1.25, 0), math.atan2(-pts[k][1], -pts[k][0]) + math.pi / 2, 13.0)
    for k in range(4):                                                                            # відеокуб
        a = k * math.pi / 2
        c = Vector((1.6 * math.cos(a), 1.6 * math.sin(a), 15.0))
        lt.box("Screen_Frame", c, (3.2 if k % 2 else 0.1, 0.1 if k % 2 else 3.2, 2.0))
        lt.box("LED_Screen", c * 1.02 + Vector((0, 0, -0.3)), (3.0 if k % 2 else 0.02, 0.02 if k % 2 else 3.0, 1.8))
    lt.cyl("Steel_Beam", (0, 0, 16.0), (0, 0, H - 2.3), 0.05, segs=6)
    truss_beam(lt, (-6.0, (SY0 + RY0) / 2, 9.0), (6.0, (SY0 + RY0) / 2, 9.0), 0.4)
    for k in range(5):
        moving_head(lt, (-4.0 + 2.0 * k, (SY0 + RY0) / 2, 8.6), (0, (SY0 + RY0) / 2, SH))
    lt.finish()

    # --- зброя
    wp = Parts("weapons", root)
    chair_stack(wp, (-q - 0.6, q + 0.8, 0), math.pi, 10)
    folding_table(wp, (q + 0.8, q + 0.6, 0), 0.0)
    ladder(wp, (-q - 0.2, -q - 0.6, 0), (-q - 0.2, -q - 0.05, 3.4))
    trash_can(wp, (q + 0.5, -q - 0.5, 0), "Trash_Grey")
    kendo_bin(wp, (-q + 0.6, -2.4, 0))
    wp.box("Table_Top", (0, -r["half_plat"] + 0.4, 0.12), (1.83, 0.76, 0.04))
    wp.finish()
    empty("anchor_weapon_table", root, (q + 0.8, q + 0.6, 0))
    empty("anchor_weapon_chairs", root, (-q - 0.6, q + 0.8, 0))
    empty("anchor_weapon_ladder", root, (-q - 0.2, -q - 0.8, 0))
    empty("anchor_weapon_trash", root, (q + 0.5, -q - 0.5, 0))
    empty("anchor_weapon_kendo", root, (-q + 0.6, -2.4, 0))

    # --- закулісся за сценою: тунель, «горила», роздягальні, лікар, інтерв'ю, кейтеринг, рампа завантаження
    BY0, BY1 = SY1 + 0.2, SY1 + 15.0
    BX = 30.0
    bk = Parts("backstage", root)
    bk.box("Concrete", (0, (BY0 + BY1) / 2, -0.05), (2 * BX, BY1 - BY0, 0.1))
    bk.box("Ramp_Black", (0, BY0 + 2.0, SH / 2), (3.0, 4.0, SH))                                 # тунель на рівні сцени
    stairs(bk, (0, BY0 + 4.0, 0), math.pi, SH, 3.0, 5, "Ramp_Black")
    bkw = Parts("wall_backstage", root)
    bkw.box("Wall_Grey", (0, BY1 + 0.15, 2.0), (2 * BX, 0.3, 4.0))
    for sx in (-1, 1):
        bkw.box("Wall_Grey", (sx * BX, (BY0 + BY1) / 2, 2.0), (0.3, BY1 - BY0, 4.0))
        bkw.box("Wall_Grey", (sx * 1.7, BY0 + 2.0, 1.4 + SH / 2), (0.2, 4.0, 2.8))              # стінки тунелю
        bkw.box("Wall_Grey", (sx * 16.0, BY0 + 9.0, 2.0), (0.25, 12.0, 4.0))
        bkw.box("Wall_Grey", (sx * 23.0, BY0 + 6.0, 2.0), (14.0, 0.25, 4.0))
    bkw.finish()
    # «горила»: пульт з моніторами одразу за виходом
    folding_table(bk, (2.8, BY0 + 5.5, 0), math.pi / 2, length=2.4, cloth="Cloth_Black")
    for k in range(3):
        monitor(bk, (2.8, BY0 + 4.8 + 0.7 * k, 0), math.pi / 2, z=0.76)
    for k in range(2):
        folding_chair(bk, (3.5, BY0 + 5.0 + k, 0), -math.pi / 2, crowd=False)
    # роздягальні (схід / захід за перегородками)
    for sx in (-1, 1):
        lockers(bk, (sx * (BX - 0.3), BY0 + 10.5, 0), (-math.pi / 2 if sx > 0 else math.pi / 2), 14, "Locker_Grey")
        bench(bk, (sx * 22.5, BY0 + 10.0, 0), 0.0, 5.0)
        bench(bk, (sx * 22.5, BY0 + 12.0, 0), 0.0, 5.0)
        mirror_wall(bk, (sx * 20.0, BY1 - 0.2, 0), math.pi, 5)
        door(bk, (sx * 18.0, BY0 + 6.0, 0), 0.0)
    # лікар: дві кушетки, полиці, рушники
    for k in range(2):
        massage_table(bk, (-10.0 + 3.0 * k, BY0 + 11.5, 0), math.pi / 2)
    bk.box("Shelf_Light", (-12.0, BY1 - 0.3, 1.8), (3.0, 0.3, 0.05))
    bk.box("Bench_Wood", (-12.0, BY1 - 0.3, 1.2), (3.0, 0.4, 0.04))
    # інтерв'ю: банер з логотипами, два світлові стояки, камера
    step_and_repeat(bk, (9.0, BY1 - 0.5, 0), math.pi, 4.0, 2.6)
    for sx in (-1, 1):
        light_tree(bk, (9.0 + sx * 2.5, BY1 - 4.0, 0), (9.0, BY1 - 0.5, 1.7), h=2.0, n=2)
    hd_camera(bk, (9.0, BY1 - 4.5, 0), math.pi, 0.0)
    # кейтеринг
    for k in range(3):
        folding_table(bk, (-6.0 + 2.2 * k, BY0 + 7.0, 0), 0.0, cloth="Cloth_Blue")
        for j in range(4):
            bk.box("Food", (-6.6 + 2.2 * k + 0.4 * j, BY0 + 7.0, 0.8), (0.3, 0.4, 0.06))
    for k in range(8):
        folding_chair(bk, (-6.5 + 1.0 * k, BY0 + 8.3, 0), math.pi, crowd=False)
    # рампа завантаження з вантажівкою й кейсами
    bk.box("Concrete", (BX + 6.0, BY0 + 8.0, 0.6), (12.0, 10.0, 1.2))
    bk.box("Truck_White", (BX + 7.5, BY0 + 6.5, 2.2), (2.5, 7.5, 2.6))
    bk.box("Truck_Cab", (BX + 7.5, BY0 + 1.6, 1.6), (2.4, 2.2, 2.2))
    for (tx, ty) in ((BX + 6.4, BY0 + 1.8), (BX + 8.6, BY0 + 1.8), (BX + 6.4, BY0 + 8.5), (BX + 8.6, BY0 + 8.5)):
        bk.cyl("Tire", (tx - 0.15, ty, 0.5), (tx + 0.15, ty, 0.5), 0.5, segs=12)
    for k in range(8):
        road_case(bk, (20.0 + 1.3 * (k % 4), BY0 + 1.2 + 1.0 * (k // 4), 0), 0.0)
    bk.finish()
    bkc = Parts("ceiling_backstage", root)
    bkc.box("Ceiling", (0, (BY0 + BY1) / 2, 4.05), (2 * BX, BY1 - BY0, 0.1))
    bkc.finish()
    empty("anchor_backstage_gorilla", root, (2.0, BY0 + 5.5, 0), yaw=-90)
    empty("anchor_backstage_lockers", root, (-(BX - 1.2), BY0 + 10.5, 0))
    empty("anchor_backstage_bench", root, (-22.5, BY0 + 11.0, 0))
    empty("anchor_backstage_mirror", root, (-20.0, BY1 - 1.0, 0))
    empty("anchor_backstage_trainer", root, (-8.5, BY0 + 10.5, 0))
    empty("anchor_backstage_interview", root, (9.0, BY1 - 1.6, 0), yaw=180)
    empty("anchor_backstage_catering", root, (-3.8, BY0 + 6.0, 0))
    empty("anchor_backstage_loading", root, (BX + 3.0, BY0 + 6.0, 1.2))
    empty("anchor_backstage_exit", root, (BX - 0.6, BY1 - 0.6, 0))
    cams(root, [("cam_entrance", (0.0, 4.0, 2.8)), ("cam_crowd_low", (0.0, -6.0, 0.6)),
                ("cam_high_wide", (22.0, -24.0, 16.0)), ("cam_ramp", (4.0, SY0 - 2.0, 3.5)),
                ("cam_backstage", (0.0, BY0 + 6.0, 3.0))])
    return dict(id="arena", title="Палац спорту (середній промоушен)", ring=r, spec=spec,
                ropes=("Black", "Black", "Gold"), capacity=len(SEATS))

# ---------------------------------------------------------------- огляд і експорт

def look(root, info):
    os.makedirs(LOOK_DIR, exist_ok=True)
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
    scene.render.image_settings.file_format = 'PNG'
    cam = bpy.data.objects.new("look", bpy.data.cameras.new("look"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    # канати лише для знімка
    obs = {o.name: o for o in root.children_recursive}
    P = Parts("preview_ropes", None)
    order = ("NE", "NW", "SW", "SE")
    for i in range(3):
        for k in range(4):
            a = obs["anchor_rope_%s_%d" % (order[k], i + 1)].matrix_world.translation
            b = obs["anchor_rope_%s_%d" % (order[(k + 1) % 4], i + 1)].matrix_world.translation
            P.cyl("Pad_Red" if info["id"] == "gym" and i == 0 else ("Apron_Band_White" if info["id"] == "gym" and i == 1 else
                  ("Pad_Blue" if info["id"] == "gym" else ("Apron_Band_Gold" if info["id"] == "arena" and i == 2 else "Pad_Black"))),
                  a, b, 0.022, segs=8)
    ropes = P.finish()
    def hide(names, state):
        for o in root.children_recursive:
            if any(o.name.startswith(n) for n in names):
                o.hide_render = state
    ext = {"gym": 18, "hall": 17, "arena": 48}[info["id"]]
    shots = {
        "overview": ((ext * 0.95, -ext * 1.15, ext * 0.95), (0, 0, 1.0), 28, ["ceiling", "wall_S", "wall_E"]),
        "entrance": (None, None, 32, ["ceiling", "wall_S", "wall_E"]),
        "ringside": ((5.5, -7.5, 1.7), (0, 0, 1.6), 32, ["ceiling", "wall_S"]),
        "crowd": ((0.0, 1.0, 2.4), (0, -12.0, 3.0), 30, ["ceiling"]),
        "backstage": (None, None, 30, ["ceiling", "wall_"]),
    }
    curtain = obs["anchor_curtain"].matrix_world.translation
    gor = obs["anchor_backstage_gorilla"].matrix_world.translation
    for name, (pos, target, lens, hidden) in shots.items():
        if name == "entrance":
            pos = (curtain.x * 0.1 + 6.0, curtain.y * 0.1 - 6.0, 4.5)
            target = (curtain.x, curtain.y, 2.0)
        if name == "backstage":
            pos = (gor.x + 9.0, gor.y - 12.0, 14.0)
            target = (gor.x - 2.0, gor.y + 1.0, 0.0)
        hide(hidden, True)
        cam.location = pos
        d = Vector(target) - Vector(pos)
        cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
        cam.data.lens = lens
        cam.data.clip_end = 400
        scene.render.filepath = os.path.join(LOOK_DIR, "%s_%s.png" % (info["id"], name))
        bpy.ops.render.render(write_still=True)
        hide(hidden, False)
    for o in ropes:
        bpy.data.objects.remove(o, do_unlink=True)
    bpy.data.objects.remove(cam, do_unlink=True)

def export(root, info):
    os.makedirs(OUT, exist_ok=True)
    bpy.ops.object.select_all(action='DESELECT')
    for o in [root] + list(root.children_recursive):
        o.select_set(True)
    path = os.path.join(OUT, "venue_%s.fbx" % info["id"])
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, apply_unit_scale=True,
                             apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y',
                             object_types={'EMPTY', 'MESH'}, use_mesh_modifiers=True, mesh_smooth_type='FACE',
                             path_mode='RELATIVE', embed_textures=False, bake_space_transform=False)
    lines = ["# Якорі арени «%s» (tools/blender/ring_venues.py). <ім'я> <x> <y> <z> <курс°>; метри, Z угору." % info["title"]]
    for o in sorted(root.children_recursive, key=lambda o: o.name):
        if o.type == 'EMPTY' and o.name.startswith("anchor_"):
            p = o.matrix_world.translation
            lines.append("%s %.3f %.3f %.3f %.0f" % (o.name, p.x, p.y, p.z, math.degrees(o.rotation_euler.z)))
    with open(os.path.join(OUT, "venue_%s_anchors.txt" % info["id"]), "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")
    with open(os.path.join(OUT, "venue_%s_seats.txt" % info["id"]), "w", encoding="utf-8", newline="\n") as f:
        f.write("# Місця для натовпу: x y z курс° (лицем до рингу). Разом: %d\n" % len(SEATS))
        for s_ in SEATS:
            f.write("%.2f %.2f %.2f %.0f\n" % s_)
    r = info["ring"]
    tris = sum(len(o.data.polygons) for o in root.children_recursive if o.type == 'MESH')
    objs = sum(1 for o in root.children_recursive if o.type == 'MESH')
    with open(os.path.join(OUT, "venue_%s_info.txt" % info["id"]), "w", encoding="utf-8", newline="\n") as f:
        f.write("назва %s\n" % info["title"])
        f.write("ринг_фути %d\nплатформа_половина_м %.3f\nмат_висота_м %.3f\nстійки_м %.3f\nвсередині_канатів_м %.3f\n"
                % (info["spec"]["size_ft"], r["half_plat"], r["mat_z"], r["post"], r["inner"]))
        f.write("канати %s\nмісткість %d\nоб'єктів_сітки %d\nграней %d\n" % (" ".join(info["ropes"]), len(SEATS), objs, tris))
    return path, len(lines) - 1, objs, tris

BUILDERS = {"gym": build_gym, "hall": build_hall, "arena": build_arena}

for vid in WANT:
    bpy.ops.wm.read_homefile(use_empty=True)
    SEATS.clear()
    COL = collection("Venue_" + vid)
    root = empty("venue_" + vid, None)
    info = BUILDERS[vid](root)
    path, n_anchors, objs, tris = export(root, info)
    print("VENUE %s: %s — якорів %d, місць %d, об'єктів %d, граней %d" % (vid, path, n_anchors, len(SEATS), objs, tris), flush=True)
    if LOOK:
        look(root, info)
        print("VENUE %s: огляд готовий" % vid, flush=True)
