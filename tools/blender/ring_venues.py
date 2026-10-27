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
# Ітерація 2: матеріали з власними процедурними текстурами (tools/blender/ring_textures.py → Assets/Art/Ring/Textures):
# розгортка в метрах світу (плитка набору), колір текстури × відтінок матеріалу, нормаль і шорсткість; фаски на великих
# коробках; наклейки (афіші, емблема на полотні, скотч, бруд); LOD для стільців і крісел (_LOD0 — повні, _LOD1 — дві
# площини: Unity сам збирає LODGroup за суфіксами); бюджет граней і сіток перевіряється і валить генератор.
# Ітерація 1 — лише колір (власна робота, без чужих текстур). Розміри рингів — як у справжніх: інді 16–18 футів,
# стандарт 20; килимки навколо рингу; вихідна арка ~2,4 м; ферма світла на стійках (інді) і підвісна коробка
# 32–40 футів (більші шоу).

import bpy, bmesh, math, os, sys
from mathutils import Vector, Matrix, Quaternion

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
REPO = args[0] if args else os.getcwd()
LOOK = "--look" in args
SHOTS = next((a.split("=", 1)[1].split(",") for a in args if a.startswith("--shots=")), None)   # лише ці знімки
VIDEO = next((a.split("=", 1)[1] for a in args if a.startswith("--video=")), None)              # відео сцени світла
CHECK = "--check" in args or "--check-raw" in args                                              # перевірка колізій
CHECK_RAW = "--check-raw" in args                                                               # без розв'язання (звіт)
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
    "Hall_Floor": ((0.42, 0.26, 0.14), 0.0, 0.35, 0), "Hall_Floor_Line": ((0.26, 0.15, 0.08), 0.0, 0.5, 0),
    "Brick_Warm": ((0.46, 0.21, 0.14), 0.0, 0.9, 0), "Plaster_Cream": ((0.74, 0.68, 0.57), 0.0, 0.9, 0),
    "Wainscot": ((0.22, 0.12, 0.07), 0.0, 0.55, 0), "Ceiling_Hall": ((0.30, 0.24, 0.18), 0.0, 0.8, 0),
    "Balcony_Fascia": ((0.16, 0.09, 0.05), 0.0, 0.5, 0), "Carpet_Black": ((0.05, 0.05, 0.06), 0.0, 1.0, 0),
    "Arena_Floor": ((0.22, 0.23, 0.26), 0.0, 0.6, 0), "Concourse": ((0.45, 0.45, 0.46), 0.0, 0.9, 0),
    "LED_Ribbon": ((0.95, 0.55, 0.10), 0.0, 0.3, 4.0), "Aisle_Step": ((0.52, 0.52, 0.53), 0.0, 0.9, 0),
    "Aisle_Nose": ((0.85, 0.70, 0.10), 0.0, 0.6, 0), "Post_Cap": ((0.75, 0.76, 0.78), 1.0, 0.3, 0),
    "Fire_Red": ((0.70, 0.05, 0.04), 0.3, 0.4, 0), "Cup_White": ((0.92, 0.92, 0.9), 0.0, 0.5, 0),
    "Panel_Grey": ((0.55, 0.57, 0.58), 0.6, 0.5, 0), "Cable_Black": ((0.03, 0.03, 0.03), 0.0, 0.6, 0),
}

TEX_DIR = os.path.join(REPO, "Assets", "Art", "Ring", "Textures")
CC0_DIR = os.path.join(REPO, "Assets", "ThirdParty", "CC0", "PolyHaven", "Textures")

# Текстурні набори: ім'я → карти, плитка (м), середній колір карти кольору. Колір матеріалу = карта × (відтінок /
# середнє): фото-текстура лишає свою фактуру (волокна, плями, шви), а колір бере з матеріалу — одна текстура на
# багато матеріалів. Спершу CC0 з Poly Haven (дозвіл власника 27.10.2026, docs/ASSETS.md), процедурні з
# ring_textures.py — для того, чого там немає (пластик, LED, дошки лав).
SETS = {}

def _cc0(name, tile, avg, diff=True):
    d = os.path.join(CC0_DIR, name)
    if os.path.exists(os.path.join(d, name + "_nor_gl_2k.jpg")):
        SETS[name] = {"diff": os.path.join(d, name + "_diff_2k.jpg") if diff else None,
                      "nor": os.path.join(d, name + "_nor_gl_2k.jpg"), "rough": os.path.join(d, name + "_rough_2k.jpg"),
                      "tile": tile, "avg": avg if diff else (1.0, 1.0, 1.0)}

for _n, _t, _a in (("wood_floor", 1.7, (0.5, 0.38, 0.26)), ("wood_floor_worn", 2.0, (0.54, 0.35, 0.18)),
                   ("red_brick", 1.4, (0.56, 0.4, 0.31)), ("concrete_block_wall", 2.0, (0.27, 0.24, 0.2)),
                   ("smooth_concrete_floor", 2.0, (0.33, 0.24, 0.16)), ("concrete_wall_006", 2.0, (0.36, 0.32, 0.27)),
                   ("plastered_wall_04", 3.2, (0.55, 0.54, 0.53)), ("velour_velvet", 0.6, (0.68, 0.18, 0.28)),
                   ("fabric_leather_02", 0.5, (0.51, 0.3, 0.1)), ("rubber_tiles", 2.0, (0.13, 0.13, 0.14)),
                   ("dirty_carpet", 0.6, (0.21, 0.18, 0.14)), ("metal_plate", 0.5, (0.24, 0.2, 0.11)),
                   ("painted_metal_shutter", 2.0, (0.5, 0.51, 0.52)), ("metal_plate_02", 2.0, (0.33, 0.28, 0.24)),
                   ("worn_tile_floor", 2.0, (0.29, 0.27, 0.23)), ("black_painted_planks", 1.6, (0.1, 0.11, 0.13))):
    _cc0(_n, _t, _a)
_cc0("fabric_pattern_07", 0.4, None, diff=False)                     # лише нормаль і шорсткість (переплетення)
try:
    for line in open(os.path.join(TEX_DIR, "textures_manifest.txt"), encoding="utf-8"):
        f_ = line.split()
        if len(f_) == 3 and not line.startswith("#") and f_[2] not in ("decal", "led") and f_[0] not in SETS:
            if os.path.exists(os.path.join(TEX_DIR, f_[0] + "_albedo.png")):
                SETS[f_[0]] = {"diff": os.path.join(TEX_DIR, f_[0] + "_albedo.png"),
                               "nor": os.path.join(TEX_DIR, f_[0] + "_normal.png"),
                               "rough": os.path.join(TEX_DIR, f_[0] + "_rough.png"),
                               "tile": float(f_[2]), "avg": (0.9, 0.9, 0.9)}
except OSError:
    pass

# матеріал → набір (перший наявний з переліку); NATIVE — природний колір самої фото-текстури (цегла, паркет)
KIT_RULES = [
    (("wood_floor", "wood_strip"), ("Gym_Floor",)),
    (("wood_floor_worn", "wood_board"), ("Hall_Floor",)),
    (("wood_board",), ("Bleacher_Wood", "Bench_Wood", "Bar_Wood", "Bar_Top", "Wainscot", "Balcony_Fascia", "Door_Wood", "Kendo")),
    (("concrete_block_wall", "block_wall"), ("Block_Wall", "Wall_Band_Blue")),
    (("red_brick", "brick"), ("Brick_Warm", "Brick_Dark")),
    (("smooth_concrete_floor", "panel_floor"), ("Arena_Floor", "Concourse", "Floor_Dark")),
    (("concrete_wall_006", "concrete"), ("Concrete", "Tier_Concrete", "Aisle_Step", "Wall_Grey", "Wall_Dark", "Ceiling",
                                         "Tier_Front")),
    (("plastered_wall_04", "plaster"), ("Plaster_Cream", "Ceiling_Hall")),
    (("velour_velvet", "cloth"), ("Drape_", "Cloth_", "Banner_", "Backdrop_")),
    (("fabric_pattern_07", "canvas"), ("Canvas_White", "Canvas_Grey", "Shirt_", "Towel_White", "Speaker_Grille")),
    (("fabric_leather_02", "vinyl"), ("Pad_", "Apron_", "Vinyl_Black", "Speaker_Black")),
    (("rubber_tiles", "vinyl"), ("Mat_",)),
    (("dirty_carpet", "carpet"), ("Carpet_",)),
    (("metal_plate", "metal_brushed"), ("Steps_Steel", "Steps_Black")),
    (("metal_plate_02", "metal_brushed"), ("Post_Steel", "Truss_Alu", "Barricade_Steel", "Bike_Rack", "Railing_Steel",
                                           "Ladder_Alu", "Case_Edge", "Post_Cap", "Trash_Grey", "Steel_Beam", "Panel_Grey")),
    (("painted_metal_shutter", "metal_painted"), ("Post_Black", "Truss_Black", "Chair_Frame", "Table_Leg", "Locker_",
                                                  "Door_Grey", "Light_Body", "Bleacher_Frame", "Case_Black", "Truck_",
                                                  "Trash_Green", "Camera_Body", "Monitor", "Hoop_Rim", "Scoreboard",
                                                  "Screen_Frame", "Fire_Red")),
    (("worn_tile_floor", "tile_floor"), ("Floor_Tile",)),
    (("black_painted_planks", "panel_floor"), ("Stage_Black", "Ramp_Black")),
    (("plastic",), ("Chair_Seat_", "Seat_", "Table_Top", "Cooler_", "Hoop_Board", "Cup_White")),
    (("led_pixels",), ("LED_Screen", "LED_Side", "LED_Ribbon")),
]
NATIVE = {"Gym_Floor": 1.25, "Hall_Floor": 1.0, "Brick_Warm": 1.0, "Brick_Dark": 0.6}   # множник природного кольору
NATIVE_SETS = {"wood_floor", "wood_floor_worn", "red_brick", "wood_strip", "brick"}    # набори в природному кольорі
FACTOR_SCALE = 4.0             # множник у кольорі вершин зберігається поділеним на 4 (колір вершин — 0..1)

def lum(c):
    return 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2]
KIT_SCALE = {}                                                                          # множник плитки
KIT_METAL = {"metal_plate": 0.9, "metal_plate_02": 0.9, "metal_brushed": 0.9, "painted_metal_shutter": 0.2,
             "metal_painted": 0.25}

def kit(name):
    for sets, prefixes in KIT_RULES:
        if any(name == p_ or (p_.endswith("_") and name.startswith(p_)) for p_ in prefixes):
            for kset in sets:
                if kset in SETS:
                    return kset
    return None

def kit_factor(name):
    """Множник карти: для природних наборів — сталий (колір фото × NATIVE); для решти береться ЛИШЕ яскравість фото
    (фактура), а колір — з матеріалу: множник = відтінок / середня яскравість карти."""
    kset = kit(name)
    if kset in NATIVE_SETS:
        return (NATIVE.get(name, 1.0),) * 3
    la = max(0.02, lum(SETS[kset]["avg"]))
    return tuple(min(FACTOR_SCALE, c / la) for c in MATS[name][0])

TILE = {k: v["tile"] for k, v in SETS.items()}

# фаски: матеріал → ширина (м); лише на коробках, де найменший розмір > 3 фаски
BEVEL = {"Canvas_White": 0.04, "Canvas_Grey": 0.04, "Steps_Steel": 0.015, "Steps_Black": 0.015, "Table_Top": 0.012,
         "Speaker_Black": 0.02, "Case_Black": 0.02, "Locker_Grey": 0.01, "Locker_Blue": 0.01, "Bar_Wood": 0.02,
         "Bar_Top": 0.015, "Stage_Black": 0.02, "Barricade_Cover": 0.0, "Bench_Wood": 0.01, "Cooler_Blue": 0.02,
         "Cooler_White": 0.02, "Pad_Red": 0.03, "Pad_Blue": 0.03, "Pad_Black": 0.03, "Pad_Yellow": 0.03,
         "Vinyl_Black": 0.03, "Ramp_Black": 0.02, "Monitor": 0.006, "Camera_Body": 0.01, "Truck_White": 0.08,
         "Truck_Cab": 0.1, "Balcony_Fascia": 0.02, "Wainscot": 0.01}

def tex_node(m, path, non_color):
    t = m.node_tree.nodes.new("ShaderNodeTexImage")
    t.image = bpy.data.images.load(path, check_existing=True)
    if non_color:
        t.image.colorspace_settings.name = 'Non-Color'
    return t

def decal_material(name):
    """Наклейка: Decal_<x> → Textures/decal_<x>.png з прозорістю; Art_<x> → Textures/art_<x>.png (графіка без слів —
    банери, фартух, табло; написи поверх — реєстр текстів)."""
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    b = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    fn = ("decal_" + name[6:].lower()) if name.startswith("Decal_") else ("art_" + name[4:].lower())
    t = tex_node(m, os.path.join(TEX_DIR, fn + ".png"), False)
    nt.links.new(t.outputs["Color"], b.inputs["Base Color"])
    nt.links.new(t.outputs["Alpha"], b.inputs["Alpha"])
    b.inputs["Roughness"].default_value = 0.8
    try:
        m.surface_render_method = 'BLENDED'
    except (AttributeError, TypeError):
        m.blend_method = 'BLEND'
    m.diffuse_color = (1, 1, 1, 1)
    return m

def build_textured(m, kset, factor=None, rough=1.0, metal=0.0, emit=0.0, attr=None):
    """Вузли PBR з набору: колір = карта × множник (сталий або колір вершин attr × 4), шорсткість = карта × rough
    (або × A кольору вершин × 1,7), нормаль. Набір без карти кольору — лише множник (відтінок)."""
    info = SETS[kset]
    nt = m.node_tree
    b = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    ca = None
    mul = nt.nodes.new("ShaderNodeMix")
    mul.data_type = 'RGBA'
    mul.blend_type = 'MULTIPLY'
    mul.inputs["Factor"].default_value = 1.0
    if info["diff"]:
        alb = tex_node(m, info["diff"], False)
        if kset in NATIVE_SETS:
            nt.links.new(alb.outputs["Color"], mul.inputs[6])
        else:
            bw = nt.nodes.new("ShaderNodeRGBToBW")                      # лише яскравість фото — колір з матеріалу
            nt.links.new(alb.outputs["Color"], bw.inputs[0])
            nt.links.new(bw.outputs[0], mul.inputs[6])
    else:
        mul.inputs[6].default_value = (1, 1, 1, 1)
    if attr:
        ca = nt.nodes.new("ShaderNodeVertexColor")
        ca.layer_name = attr
        fac = nt.nodes.new("ShaderNodeVectorMath")
        fac.operation = 'SCALE'
        nt.links.new(ca.outputs["Color"], fac.inputs[0])
        fac.inputs["Scale"].default_value = FACTOR_SCALE
        nt.links.new(fac.outputs[0], mul.inputs[7])
    else:
        mul.inputs[7].default_value = (*factor, 1)
    nt.links.new(mul.outputs[2], b.inputs["Base Color"])
    rg = tex_node(m, info["rough"], True)
    rm = nt.nodes.new("ShaderNodeMath")
    rm.operation = 'MULTIPLY'
    rm.use_clamp = True
    nt.links.new(rg.outputs["Color"], rm.inputs[0])
    if attr:
        r1 = nt.nodes.new("ShaderNodeMath")
        r1.operation = 'MULTIPLY'
        nt.links.new(ca.outputs["Alpha"], r1.inputs[0])
        r1.inputs[1].default_value = 1.7
        nt.links.new(r1.outputs[0], rm.inputs[1])
    else:
        rm.inputs[1].default_value = rough
    nt.links.new(rm.outputs[0], b.inputs["Roughness"])
    nm = tex_node(m, info["nor"], True)
    nn = nt.nodes.new("ShaderNodeNormalMap")
    nt.links.new(nm.outputs["Color"], nn.inputs["Color"])
    nt.links.new(nn.outputs["Normal"], b.inputs["Normal"])
    b.inputs["Metallic"].default_value = metal
    if emit:
        for key in ("Emission Color", "Emission"):
            if key in b.inputs:
                nt.links.new(mul.outputs[2], b.inputs[key])
                break
        if "Emission Strength" in b.inputs:
            b.inputs["Emission Strength"].default_value = emit

def material(name):
    m = bpy.data.materials.get(name)
    if m is not None:
        return m
    if name.startswith(("Decal_", "Art_")):
        return decal_material(name)
    if name == "LED_Content":
        m = bpy.data.materials.new(name)
        m.use_nodes = True
        nt = m.node_tree
        b = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
        t = tex_node(m, os.path.join(TEX_DIR, "led_logo.png"), False)
        mk = SETS.get("led_pixels")
        mul = nt.nodes.new("ShaderNodeMix")
        mul.data_type = 'RGBA'
        mul.blend_type = 'MULTIPLY'
        mul.inputs["Factor"].default_value = 1.0
        nt.links.new(t.outputs["Color"], mul.inputs[6])
        if mk:                                            # решітка пікселів поверх заставки
            px = tex_node(m, mk["diff"], False)
            mp = nt.nodes.new("ShaderNodeMapping")
            tc = nt.nodes.new("ShaderNodeTexCoord")
            mp.inputs["Scale"].default_value = (60.0, 34.0, 1.0)
            nt.links.new(tc.outputs["UV"], mp.inputs["Vector"])
            nt.links.new(mp.outputs["Vector"], px.inputs["Vector"])
            nt.links.new(px.outputs["Color"], mul.inputs[7])
        else:
            mul.inputs[7].default_value = (1, 1, 1, 1)
        nt.links.new(mul.outputs[2], b.inputs["Base Color"])
        for key in ("Emission Color", "Emission"):
            if key in b.inputs:
                nt.links.new(mul.outputs[2], b.inputs[key])
                break
        b.inputs["Emission Strength"].default_value = 5.0
        return m
    color, metal, rough, emit = MATS[name]
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    kset = kit(name)
    if kset:
        build_textured(m, kset, factor=kit_factor(name), rough=rough / 0.6, metal=metal, emit=emit)
        m.diffuse_color = (*color, 1)
        m["kit"] = kset
        return m
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
    """Набір геометрії однієї групи: на кожен матеріал — один об'єкт «<група>_<матеріал>» (мало викликів малювання).
    lod=True — ще й спрощений рівень (level = 1 під час запису): об'єкти «…_LOD0» і «…_LOD1» під порожнім «<група>»."""
    def __init__(self, group, parent, lod=False):
        self.group, self.parent, self.lod = group, parent, lod
        self.bms, self.bms1 = {}, {}
        self.level = 0

    def bm(self, mat):
        d = self.bms1 if self.level else self.bms
        if mat not in d:
            d[mat] = bmesh.new()
        return d[mat]

    def decal(self, mat, c, right, up):
        """Наклейка-площина: центр c, півосі right і up (вектори), розгортка 0..1, лицем у бік right × up."""
        bm = self.bm(mat)
        c, right, up = Vector(c), Vector(right), Vector(up)
        vs = [bm.verts.new(c - right - up), bm.verts.new(c + right - up), bm.verts.new(c + right + up), bm.verts.new(c - right + up)]
        f = bm.faces.new(vs)
        uv = bm.loops.layers.uv.verify()
        for loop, t in zip(f.loops, ((0, 0), (1, 0), (1, 1), (0, 1))):
            loop[uv].uv = t

    def _targets(self, mat):
        """Куди писати геометрію: bmesh матеріалу і, якщо відкрито предмет (with item(...)), — ще й bmesh предмета."""
        out = [self.bm(mat)]
        if ITEM_STACK and not self.level and not mat.startswith(("Decal_", "Art_")):
            out.append(ITEMS.setdefault(ITEM_STACK[0], bmesh.new()))
        return out

    def box(self, mat, c, s, rz=0.0, rx=0.0, bevel=None, segs=1):
        for bm in self._targets(mat):
            self._box(bm, mat, c, s, rz, rx, bevel, segs)

    def _box(self, bm, mat, c, s, rz, rx, bevel, segs):
        bw = BEVEL.get(mat, 0.0) if bevel is None else bevel
        if bw and min(s) > 3 * bw and not self.level:
            # фаска: коробка будується в окремому bmesh і доливається в загальний
            tb = bmesh.new()
            bmesh.ops.create_cube(tb, size=1.0)
            bmesh.ops.scale(tb, vec=s, verts=list(tb.verts))
            bmesh.ops.bevel(tb, geom=list(tb.edges), offset=bw, segments=segs, affect='EDGES', profile=0.5)
            if rx:
                bmesh.ops.rotate(tb, cent=(0, 0, 0), matrix=Matrix.Rotation(rx, 3, 'X'), verts=list(tb.verts))
            if rz:
                bmesh.ops.rotate(tb, cent=(0, 0, 0), matrix=Matrix.Rotation(rz, 3, 'Z'), verts=list(tb.verts))
            bmesh.ops.translate(tb, vec=c, verts=list(tb.verts))
            me = bpy.data.meshes.new("_bevel")
            tb.to_mesh(me)
            tb.free()
            bm.from_mesh(me)
            bpy.data.meshes.remove(me)
            return
        r = bmesh.ops.create_cube(bm, size=1.0)
        bmesh.ops.scale(bm, vec=s, verts=r["verts"])
        if rx:
            bmesh.ops.rotate(bm, cent=(0, 0, 0), matrix=Matrix.Rotation(rx, 3, 'X'), verts=r["verts"])
        if rz:
            bmesh.ops.rotate(bm, cent=(0, 0, 0), matrix=Matrix.Rotation(rz, 3, 'Z'), verts=r["verts"])
        bmesh.ops.translate(bm, vec=c, verts=r["verts"])

    def cyl(self, mat, p0, p1, r0, r1=None, segs=12):
        for bm in self._targets(mat):
            self._cyl(bm, p0, p1, r0, r1, segs)

    def _cyl(self, bm, p0, p1, r0, r1, segs):
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
        for bm in self._targets(mat):
            vs = [bm.verts.new(p) for p in pts]
            bm.faces.new(vs)

    def lathe(self, mat, base, profile, segs=16, axis=(0, 0, 1)):
        """Тіло обертання: профіль [(радіус, висота)] навколо осі axis з точки base (PAR, бак, абажур)."""
        base, ax = Vector(base), Vector(axis).normalized()
        rot = ax.to_track_quat('Z', 'Y').to_matrix()
        for bm in self._targets(mat):
            rings = []
            for (r, h) in profile:
                ring = []
                for k in range(segs):
                    a = 2 * math.pi * k / segs
                    ring.append(bm.verts.new(base + rot @ Vector((r * math.cos(a), r * math.sin(a), h))))
                rings.append(ring)
            for i in range(len(rings) - 1):
                for k in range(segs):
                    a, b = rings[i][k], rings[i][(k + 1) % segs]
                    c, d = rings[i + 1][(k + 1) % segs], rings[i + 1][k]
                    try:
                        bm.faces.new((a, b, c, d))
                    except ValueError:
                        pass

    def finish(self, smooth=()):
        out = []
        has_lod = bool(self.bms1)
        parent = self.parent
        if has_lod:
            parent = empty(self.group, self.parent)
        for level, d in ((0, self.bms), (1, self.bms1)):
            for mat, bm in d.items():
                if not bm.verts:
                    bm.free()
                    continue
                world_uv(bm, mat)
                name = self.group + "_" + mat + (("_LOD%d" % level) if has_lod else "")
                me = bpy.data.meshes.new(name)
                bm.to_mesh(me)
                bm.free()
                ob = bpy.data.objects.new(name, me)
                COL.objects.link(ob)
                ob.parent = parent
                me.materials.append(material(mat))
                for p in me.polygons:
                    p.use_smooth = mat in smooth
                ob["lod"] = level if has_lod else -1
                ob["group"] = self.group
                out.append(ob)
        self.bms, self.bms1 = {}, {}
        return out

def world_uv(bm, mat):
    """Розгортка в метрах світу: грань проєктується на площину за найбільшою складовою нормалі, ділиться на плитку
    текстурного набору — плитка однакового розміру на всіх об'єктах, швів між коробками не видно. Наклейки — свої 0..1."""
    if mat.startswith(("Decal_", "Art_")) or mat == "LED_Content":
        return
    kset = kit(mat)
    tile = TILE.get(kset, 1.0) * KIT_SCALE.get(mat, 1.0) if kset else 1.0
    uv = bm.loops.layers.uv.verify()
    bm.normal_update()
    for f in bm.faces:
        n = f.normal
        ax, ay, az = abs(n.x), abs(n.y), abs(n.z)
        for loop in f.loops:
            co = loop.vert.co
            if az >= ax and az >= ay:
                loop[uv].uv = (co.x / tile, co.y / tile)
            elif ax >= ay:
                loop[uv].uv = (co.y / tile, co.z / tile)
            else:
                loop[uv].uv = (co.x / tile, co.z / tile)

def rot2(v, yaw):
    c, s = math.cos(yaw), math.sin(yaw)
    return Vector((v[0] * c - v[1] * s, v[0] * s + v[1] * c, v[2]))

def at(pos, yaw, local):
    """Точка local (простір речі, лицем у −Y) у світі для речі в pos з курсом yaw (рад)."""
    return Vector(pos) + rot2(local, yaw)

SEATS = []           # (x, y, z, курс°, сидить/стоїть) — місця для натовпу
LIGHTS = []          # (позиція, напрям, колір, кут°, par|head) — прилади; джерела лише для огляду, у грі — сцени світла
AREAS = []           # (позиція, розмір, колір, вати) — плафони; лише для огляду
LENS_COLOR = {"Light_Lens": (1.0, 0.9, 0.75), "Light_Lens_Blue": (0.35, 0.5, 1.0), "Light_Lens_Red": (1.0, 0.3, 0.25)}

def seat_mark(pos, yaw_rad, z, kind="сидить"):
    SEATS.append((pos[0], pos[1], z, math.degrees(yaw_rad), kind))

# ---------------------------------------------------------------- реквізит

ITEMS = {}           # предмет → bmesh (світові координати) — для перевірки «реквізит ↔ реквізит» (ring_collide.py)
ITEM_STACK = []      # відкритий предмет (вкладені виклики пишуть у зовнішній)
_ITEM_N = [0]

class item:
    """with item("стіл хронометриста"): уся геометрія всередині — один предмет для перевірки колізій."""
    def __init__(self, name):
        _ITEM_N[0] += 1
        self.name = "%s#%d" % (name, _ITEM_N[0])
    def __enter__(self):
        if not ITEM_STACK:
            ITEM_STACK.append(self.name)
            self.own = True
        else:
            self.own = False
        return self
    def __exit__(self, *a):
        if self.own:
            ITEM_STACK.pop()

def itemize(fn, name=None):
    """Обгортка функції реквізиту: кожен виклик — окремий предмет."""
    def w(*a, **k):
        with item(name or fn.__name__):
            return fn(*a, **k)
    w.__name__ = fn.__name__
    return w

# Тексти — лише текстом з ключами перекладу (власник 27.10.2026: «Эти надписи должны быть текстом, что бы на
# другие языки переводить»): у текстурах слів немає; кожен напис — запис тут → venue_<id>_texts.txt (якір для гри) і
# ring_strings_uk.txt (рядки); у знімках огляду Blender ставить ті самі рядки шрифтом Fixel, у FBX текст не йде.
TEXTS = []           # dict(key, text, pos, right, up, size, color, align, emit, width)

def label(key, text, pos, right, up=(0, 0, 1), size=0.2, color=(0.95, 0.95, 0.95), align="CENTER", emit=0.0, width=0.0,
          font="display"):
    TEXTS.append(dict(key=key, text=text, pos=Vector(pos), right=Vector(right).normalized(), up=Vector(up).normalized(),
                      size=size, color=color, align=align, emit=emit, width=width, font=font))

def wall_label(key, text, pos, inward, size, **kw):
    """Напис на стіні/щиті: inward — нормаль на глядача; читається зліва направо, коли дивишся на поверхню."""
    n = Vector(inward).normalized()
    right = Vector((-n.y, n.x, 0)) if abs(n.z) < 0.9 else Vector((1, 0, 0))
    label(key, text, Vector(pos) + n * 0.006, right, n.cross(right) if abs(n.z) > 0.9 else (0, 0, 1), size, **kw)

STRINGS_FILE = os.path.join(OUT, "ring_strings_uk.txt")

def write_texts(vid):
    with open(os.path.join(OUT, "venue_%s_texts.txt" % vid), "w", encoding="utf-8", newline="\n") as f:
        f.write("# Написи арени (рушій малює текст сам за ключем — переклад у ring_strings_<мова>.txt): ключ x y z "
                "вправо(x y z) вгору(x y z) висота_літер r g b вирівнювання світіння макс_ширина шрифт\n")
        for t in TEXTS:
            f.write("%s %.3f %.3f %.3f %.3f %.3f %.3f %.3f %.3f %.3f %.3f %.3f %.3f %.3f %s %.1f %.2f %s\n" % (
                t["key"], *t["pos"], *t["right"], *t["up"], t["size"], *t["color"], t["align"], t["emit"], t["width"], t["font"]))
    known = {}
    if os.path.exists(STRINGS_FILE):
        for line in open(STRINGS_FILE, encoding="utf-8"):
            if "=" in line and not line.startswith("#"):
                k, v = line.rstrip("\n").split("=", 1)
                known[k] = v
    for t in TEXTS:
        known[t["key"]] = t["text"]
    with open(STRINGS_FILE, "w", encoding="utf-8", newline="\n") as f:
        f.write("# Рядки арен «Рингу», українська (мова за замовчуванням). ключ=текст; переклад — ring_strings_<код мови>.txt\n")
        for k in sorted(known):
            f.write("%s=%s\n" % (k, known[k]))

FONTS = {"display": os.path.join(REPO, "Assets", "ThirdParty", "Fonts", "Fixel", "FixelDisplay-SemiBold.ttf"),
         "text": os.path.join(REPO, "Assets", "ThirdParty", "Fonts", "Fixel", "FixelText-Bold.ttf")}

def preview_texts(scene):
    """Знімки: написи — об'єкти тексту Blender (поза коренем арени, тож у FBX не йдуть)."""
    made = []
    for k, t in enumerate(TEXTS):
        cu = bpy.data.curves.new("txt%d" % k, 'FONT')
        cu.body = t["text"]
        try:
            cu.font = bpy.data.fonts.load(FONTS.get(t["font"], FONTS["display"]), check_existing=True)
        except RuntimeError:
            pass
        cu.size = t["size"] / 0.72                        # висота великої літери ≈ 0,72 кегля
        cu.align_x = t["align"]
        cu.align_y = 'CENTER'
        o = bpy.data.objects.new("txt%d" % k, cu)
        scene.collection.objects.link(o)
        r, u = t["right"], t["up"]
        n = r.cross(u)
        o.matrix_world = Matrix((r, u, n)).transposed().to_4x4()
        o.location = t["pos"]
        m = bpy.data.materials.new("TextMat%d" % k)
        m.use_nodes = True
        b = next(nd for nd in m.node_tree.nodes if nd.type == "BSDF_PRINCIPLED")
        b.inputs["Base Color"].default_value = (*t["color"], 1)
        b.inputs["Roughness"].default_value = 0.6
        if t["emit"]:
            for key in ("Emission Color", "Emission"):
                if key in b.inputs:
                    b.inputs[key].default_value = (*t["color"], 1)
                    break
            b.inputs["Emission Strength"].default_value = t["emit"]
        cu.materials.append(m)
        if t["width"]:
            bpy.context.view_layer.update()
            w = o.dimensions.x
            if w > t["width"]:
                o.scale = (t["width"] / w,) * 3
        made.append(o)
    return made

PLACE = []           # (модель, (x, y, z), курс рад, колір сидіння) — екземпляри реквізиту (ring_props.py)
STAFF = set()        # індекси PLACE службових стільців

def folding_chair(P, pos, yaw, seat="Chair_Seat_Black", crowd=True):
    """Розкладний стілець, лицем у напрямку yaw (рад, 0 — лицем у −Y): екземпляр моделі chair_folding."""
    z = pos[2] if len(pos) > 2 else 0.0
    PLACE.append(("chair_folding", (pos[0], pos[1], z), yaw, MATS[seat][0]))
    if not crowd:
        STAFF.add(len(PLACE) - 1)                       # службовий стілець (хронометрист, коментатори, гримерки)
    if crowd:
        seat_mark(pos, yaw, z + 0.45)

CHAIR_HALF = (0.225, 0.24)       # півширина й півглибина стільця з трубками (м), запас 1,5 см — у chair_free

def _obb_overlap(c1, y1, c2, y2, h, pad=0.015):
    """Перетин двох прямокутників (центр, курс) з півосями h — теорема про розділову вісь."""
    axes = []
    for yaw in (y1, y2):
        axes += [Vector((math.cos(yaw), math.sin(yaw))), Vector((-math.sin(yaw), math.cos(yaw)))]
    d = Vector((c2[0] - c1[0], c2[1] - c1[1]))
    for ax in axes:
        def rad(yaw):
            u, v = Vector((math.cos(yaw), math.sin(yaw))), Vector((-math.sin(yaw), math.cos(yaw)))
            return abs(ax.dot(u)) * (h[0] + pad) + abs(ax.dot(v)) * (h[1] + pad)
        if abs(d.dot(ax)) > rad(y1) + rad(y2):
            return False
    return True

def chair_free(pos, yaw):
    """Чи вільне місце для стільця: жоден уже поставлений стілець (PLACE) його не перетинає."""
    for (pm, pp, py, pc) in PLACE:
        if pm != "chair_folding" or abs(pp[0] - pos[0]) > 0.8 or abs(pp[1] - pos[1]) > 0.8 or abs(pp[2] - (pos[2] if len(pos) > 2 else 0)) > 0.3:
            continue
        if _obb_overlap(pos, yaw, pp, py, CHAIR_HALF):
            return False
    return True

def stand_free(x, y, z, min_d=0.55):
    for s_ in SEATS:
        if s_[4] == "стоїть" and abs(s_[2] - z) < 0.3 and (s_[0] - x) ** 2 + (s_[1] - y) ** 2 < min_d * min_d:
            return False
    return True

def chair_rows(P, origin, yaw_unused, rows, per_row, pitch=0.56, row_depth=1.0, gap_every=0, gap=1.0,
               seat="Chair_Seat_Black", skip=None, standing=2, focus=(0.0, 0.0)):
    """Ряди стільців навколо рингу: origin — середина першого ряду; ряди відходять ВІД рингу (focus).
    Кожен стілець дивиться на ринг, з невеликим розкидом місця (±7 см) і кута (±12°): люди не сидять рівно.
    За останнім рядом — стоячі місця (standing рядів), лише позначки для натовпу."""
    o = Vector((origin[0], origin[1], 0.0))
    out = Vector((o.x - focus[0], o.y - focus[1], 0.0))
    out = out.normalized() if out.length > 1e-4 else Vector((0, -1, 0))
    tang = Vector((-out.y, out.x, 0.0))
    salt = int(abs(o.x) * 7 + abs(o.y) * 13)
    width = per_row * pitch + ((per_row - 1) // gap_every if gap_every else 0) * (gap - pitch)
    for r in range(rows + standing):
        x = -width / 2 + pitch / 2
        for k in range(per_row):
            if gap_every and k and k % gap_every == 0:
                x += gap - pitch
            i = r * 1000 + k + salt * 7919
            for jit in (1.0, 0.6, 0.3, 0.0):                       # розкид — лише якщо не налізе на сусіда
                pos = o + tang * (x + (h01(i, 1) - 0.5) * 0.14 * jit) + out * (r * row_depth + (h01(i, 2) - 0.5) * 0.06 * jit)
                d = Vector((focus[0] - pos.x, focus[1] - pos.y, 0.0))
                yaw = math.atan2(d.x, -d.y) + math.radians((h01(i, 3) - 0.5) * 24.0 * jit)
                if r >= rows or chair_free((pos.x, pos.y, 0.0), yaw):
                    break
            else:
                pos = None
            if pos is not None and not (skip and skip(pos)):
                if r < rows:
                    folding_chair(P, (pos.x, pos.y, 0.0), yaw, seat)
                elif h01(i, 4) < 0.8 and stand_free(pos.x, pos.y, 0.0):
                    SEATS.append((pos.x, pos.y, 0.0, math.degrees(yaw), "стоїть"))
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
    P.box("Monitor", f((0, 0, z + 0.022)), (0.34, 0.24, 0.02), rz=yaw)
    P.box("Monitor", f((0, 0.14, z + 0.145)), (0.34, 0.015, 0.22), rz=yaw, rx=math.radians(-15))
    P.box("Monitor_Screen", f((0, 0.13, z + 0.145)), (0.31, 0.003, 0.19), rz=yaw, rx=math.radians(-15))

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
    z = pos[2] if len(pos) > 2 else 0.0
    P.cyl(mat, (x, y, z), (x, y, z + 0.85), 0.28, 0.3, segs=16)
    P.cyl(mat, (x, y, z + 0.85), (x, y, z + 0.92), 0.31, segs=16)

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
    LIGHTS.append((pos + d * 0.2, d, LENS_COLOR[lens], 28.0, "par"))

def moving_head(P, pos, aim):
    pos, aim = Vector(pos), Vector(aim)
    P.box("Light_Body", pos + Vector((0, 0, -0.08)), (0.32, 0.26, 0.14))
    d = (aim - pos).normalized()
    head = pos + Vector((0, 0, -0.3))
    P.cyl("Light_Body", head - d * 0.16, head + d * 0.14, 0.13, segs=12)
    P.cyl("Light_Lens", head + d * 0.14, head + d * 0.15, 0.1, segs=12)
    LIGHTS.append((head + d * 0.2, d, (1.0, 0.95, 0.9), 18.0, "head"))

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
        P.box("Bar_Top", f((x, 0.25, 0.78)), (1.15, 0.5, 0.04), rz=yaw)
        if bulbs:
            for b in range(5):
                P.cyl("Mirror_Bulb", f((x - 0.4 + 0.2 * b, 0.02, 2.0)), f((x - 0.4 + 0.2 * b, 0.06, 2.0)), 0.03, segs=6)
        folding_chair(P, at(origin, yaw, (x, 0.8, 0)), yaw, "Chair_Seat_Black", crowd=False)   # лицем до дзеркала

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
    if mat in ("LED_Screen", "LED_Side"):       # контент — заставка сцени світла (ring_show.py), розгортка 0..1 на екран
        P.decal("LED_Content", f((0, -0.012, 0)), rot2((w / 2, 0, 0), yaw), Vector((0, 0, h / 2)))

def barricade_run(P, p0, p1, h=1.1, cover="Barricade_Cover"):
    """Огорожа біля рингу секціями по ~2 м: рама з труб (поручень, низ, стійки), опорні п'яти назад (до глядачів),
    м'який валик зверху, тканинний банер з емблемою на боці до рингу."""
    p0, p1 = Vector(p0), Vector(p1)
    d = p1 - p0
    L = d.length
    if L < 0.2:
        return
    a = math.atan2(d.y, d.x)
    t = d.normalized()
    n_ = Vector((-t.y, t.x, 0))                      # нормаль: бік від рингу — туди, де далі від центру
    if (p0 + d / 2 + n_).length < (p0 + d / 2).length:
        n_ = -n_
    n = max(1, round(L / 2.0))
    seg = L / n
    for k in range(n):
        q0 = p0 + d * (k / n) + t * (0.16 if k == 0 else 0.03)
        q1 = p0 + d * ((k + 1) / n) - t * (0.16 if k == n - 1 else 0.03)
        c = (q0 + q1) / 2
        for zz, r in ((h - 0.06, 0.024), (0.12, 0.022), (h * 0.55, 0.016)):
            P.cyl("Barricade_Steel", (q0.x, q0.y, zz), (q1.x, q1.y, zz), r, segs=8)
        for q in (q0, q1):
            P.cyl("Barricade_Steel", (q.x, q.y, 0.0), (q.x, q.y, h - 0.04), 0.026, segs=8)
            f = q + n_ * 0.3                          # п'ята-підпора назад
            P.cyl("Barricade_Steel", (q.x, q.y, 0.75), (f.x, f.y, 0.02), 0.018, segs=6)
            P.box("Barricade_Steel", (f.x, f.y, 0.01), (0.12, 0.12, 0.02), rz=a)
        P.cyl(cover, (q0.x, q0.y, h + 0.02), (q1.x, q1.y, h + 0.02), 0.085, segs=12)   # м'який валик
        cb = c - n_ * 0.035
        P.box(cover, (cb.x, cb.y, h * 0.52), (seg - 0.1, 0.012, h * 0.78), rz=a)        # банер
        dc = cb - n_ * 0.008
        P.decal("Decal_Logo", (dc.x, dc.y, h * 0.52), Vector((n_.y, -n_.x, 0)) * 0.28, Vector((0, 0, 0.28)))   # лицем до рингу

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

# ---------------------------------------------------------------- дрібні деталі (ітерація 2)

def wall_decal(P, mat, pos, inward, w, h):
    """Наклейка на стіну: pos — центр (на стіні), inward — нормаль у зал."""
    n = Vector(inward).normalized()
    right = Vector((n.y, -n.x, 0)) * (w / 2)
    c = Vector(pos) + n * 0.012
    P.decal(mat, c, -right, Vector((0, 0, h / 2)))

def floor_decal(P, mat, pos, size, yaw=0.0, z=0.0):
    right = rot2((size / 2, 0, 0), yaw)
    up = rot2((0, size / 2, 0), yaw)
    P.decal(mat, (pos[0], pos[1], z + 0.006), right, up)

def poster_row(P, a, b, z, inward, n, salt=0, w=0.6, h=0.9):
    a, b = Vector(a), Vector(b)
    for k in range(n):
        u = (k + 0.5) / n + (h01(k, 60 + salt) - 0.5) * 0.3 / n
        p = a + (b - a) * u
        wall_decal(P, "Decal_Poster_%d" % int(h01(k, 61 + salt) * 3.99), (p.x, p.y, z + (h01(k, 62 + salt) - 0.5) * 0.25),
                   inward, w, h)

def extinguisher(P, pos, inward):
    n = Vector(inward).normalized()
    c = Vector(pos) + n * 0.12
    P.cyl("Fire_Red", (c.x, c.y, 0.75), (c.x, c.y, 1.25), 0.08, segs=10)
    P.cyl("Light_Body", (c.x, c.y, 1.25), (c.x, c.y, 1.33), 0.03, segs=6)
    P.box("Panel_Grey", (c.x - n.x * 0.1, c.y - n.y * 0.1, 1.05), (0.2 if abs(n.y) > 0.5 else 0.02, 0.02 if abs(n.y) > 0.5 else 0.2, 0.08))

def wall_panel(P, pos, inward, w=0.5, h=0.7, z=1.6):
    """Електрощиток / решітка вентиляції на стіні."""
    n = Vector(inward).normalized()
    c = Vector(pos) + n * 0.06
    P.box("Panel_Grey", (c.x, c.y, z), (w if abs(n.y) > 0.5 else 0.12, 0.12 if abs(n.y) > 0.5 else w, h))
    for k in range(4):
        q = c + n * 0.062
        P.box("Light_Body", (q.x, q.y, z - h / 2 + (k + 1) * h / 5), (w * 0.8 if abs(n.y) > 0.5 else 0.005, 0.005 if abs(n.y) > 0.5 else w * 0.8, 0.02))

def cable(P, pts, r=0.012):
    """Кабель по підлозі: ламана з невеликим провисанням між точками, прихоплена скотчем."""
    pts = [Vector((p[0], p[1], p[2] if len(p) > 2 else r)) for p in pts]
    for a, b in zip(pts, pts[1:]):
        m_ = (a + b) / 2 + Vector((0.08 * h01(int(a.x * 10), 70), 0.08 * h01(int(a.y * 10), 71), 0))
        P.cyl("Cable_Black", a, m_, r, segs=5)
        P.cyl("Cable_Black", m_, b, r, segs=5)
        P.box("Panel_Grey", (m_.x, m_.y, 0.004), (0.06, 0.06, 0.004), rz=math.atan2(b.y - a.y, b.x - a.x))

def litter(P, cx, cy, w, d, n, salt=0):
    """Стаканчики й програмки на підлозі — не під стільцями й не там, де стоять або сидять люди."""
    for k in range(n):
        x = cx + (h01(k, 80 + salt) - 0.5) * w
        y = cy + (h01(k, 81 + salt) - 0.5) * d
        if any(abs(pp_[0] - x) < 0.45 and abs(pp_[1] - y) < 0.45 for (_, pp_, _, _) in PLACE) or \
           any(abs(s_[0] - x) < 0.5 and abs(s_[1] - y) < 0.5 for s_ in SEATS):
            continue
        if h01(k, 82 + salt) < 0.6:
            if h01(k, 83 + salt) < 0.5:
                P.cyl("Cup_White", (x, y, 0.0), (x, y, 0.11), 0.035, 0.045, segs=8)
            else:
                a = h01(k, 84 + salt) * math.pi
                P.cyl("Cup_White", (x, y, 0.04), (x + 0.11 * math.cos(a), y + 0.11 * math.sin(a), 0.04), 0.04, 0.03, segs=8)
        else:
            P.box("Backdrop_White", (x, y, 0.003), (0.15, 0.21, 0.003), rz=h01(k, 85 + salt) * math.pi)

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
    for sx, sy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        hor = sy != 0
        c = (sx * (half_plat + 0.012), sy * (half_plat + 0.012))
        P.box("Apron_Band_White", (c[0], c[1], mz - 0.03), (half_plat * 2 + 0.04 if hor else 0.025, 0.025 if hor else half_plat * 2 + 0.04, 0.06))
        P.box("Apron_Black", (c[0], c[1], 0.06), (half_plat * 2 + 0.04 if hor else 0.03, 0.03 if hor else half_plat * 2 + 0.04, 0.12))
        c = (sx * (half_plat + 0.008), sy * (half_plat + 0.008))
        P.box(spec["band"], (c[0], c[1], mz * 0.6), (1.2 if hor else 0.012, 0.012 if hor else 1.2, mz * 0.5))   # логотип
    corners = {"NE": (1, 1), "NW": (-1, 1), "SE": (1, -1), "SW": (-1, -1)}
    pads = spec["pads"]
    for i_c, (name, (cx, cy)) in enumerate(corners.items()):
        px, py = cx * post, cy * post
        P.cyl(spec["post"], (px, py, mz - 0.1), (px, py, post_top), 0.06, segs=12)
        P.cyl(spec["post"], (px, py, 0.0), (px, py, mz - 0.1), 0.05, segs=8)       # нога під матом
        P.cyl("Post_Cap", (px, py, post_top), (px, py, post_top + 0.03), 0.07, segs=12)
        for h_ in (0.48, 0.86, 1.24):
            P.cyl("Post_Cap", (px, py, mz + h_), (px - cx * 0.1, py - cy * 0.1, mz + h_), 0.022, segs=6)   # стяжка
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
    floor_decal(P, "Decal_Logo", (0, 0), min(2.6, inner * 1.1), 0.0, mz)
    for sx, sy in ((1, 0), (-1, 0), (0, 1), (0, -1)):         # фартух: графіка без слів + назва текстом
        nrm = Vector((sx, sy, 0))
        c_ = nrm * (half_plat + 0.036)
        rgt = Vector((-sy, sx, 0))
        P.decal("Art_Apron", (c_.x, c_.y, mz * 0.52), rgt * (half_plat * 0.96), Vector((0, 0, mz * 0.24)))
        for u in (-0.6, 0.6):
            p_ = c_ + rgt * (half_plat * u) + nrm * 0.002
            label("ring.apron.brand", "РИНГ", (p_.x, p_.y, mz * 0.52), rgt, (0, 0, 1), mz * 0.16, (0.95, 0.95, 0.95), "CENTER", 0.0,
                  half_plat * 0.7)
    for sx, sy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        n = Vector((sx, sy, 0))
        c = n * (half_plat + 0.03)
        P.decal("Decal_Logo", (c.x, c.y, mz * 0.6), Vector((-sy, sx, 0)) * 0.3, Vector((0, 0, 0.3)))
    # килимки навколо рингу
    mats_half = half_plat + spec.get("mats_extra", 1.3)
    for k in range(6):
        a = k * math.pi / 3 + 0.4
        floor_decal(P, "Decal_Grime", ((half_plat + 0.6) * math.cos(a), (half_plat + 0.6) * math.sin(a)), 1.4, a, 0.03)
    _saved = ITEM_STACK[:]
    ITEM_STACK.clear()                                   # килимки — підлога: на них стоять, це не колізія
    P.box(spec["mats"], (0, 0, 0.015), (mats_half * 2, mats_half * 2, 0.03))
    ITEM_STACK[:] = _saved
    for sx, sy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        hor = sy != 0
        P.box(spec["band"], (sx * (mats_half - 0.05), sy * (mats_half - 0.05), 0.031),
              (mats_half * 2 if hor else 0.06, 0.06 if hor else mats_half * 2, 0.002))
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
            AREAS.append(((X0 + 3 + i * 6.0, Y0 + 3.5 + j * 6.0, H - 0.7), 1.2, (1.0, 0.98, 0.92), 260.0))
    ceil.finish()

    # --- спортзальне: висувні трибуни біля північної стіни, щити, табло
    bl = Parts("bleachers", root)
    rows, rd, rr = 6, 0.95, 0.42
    for k in range(rows):
        y = Y1 - 0.6 - (rows - 1 - k) * rd
        z = 0.45 + k * rr - 0.025                                                               # центр дошки; верх — 0,45 + k·підйом
        bl.box("Bleacher_Wood", (0, y - rd / 2 + 0.2, z), (26.0, 0.32, 0.05))
        bl.box("Bleacher_Wood", (0, y - rd / 2 - 0.16, z - 0.445), (26.0, 0.36, 0.04))       # підніжка попереду: верх = сидіння − 0,45
        bl.box("Bleacher_Frame", (0, y, z / 2), (26.0, 0.05, z))
        for s in range(46):
            seat_mark((-12.6 + s * 0.56, y - rd / 2 + 0.2), 0.0, z + 0.025, "лава")              # лицем на південь, до рингу
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

    # --- дрібниці: афіші, вогнегасники, щитки, годинник, вентиляція під стелею, кабелі, сміття, скотч
    wd = {k_: Parts("wall_%s_decor" % k_, root) for k_ in "NSEW"}
    poster_row(wd["E"], (X1 - 0.02, -11.0, 0), (X1 - 0.02, 12.0, 0), 1.8, (-1, 0, 0), 9, salt=1)
    poster_row(wd["S"], (-6.0, Y0 + 0.02, 0), (12.0, Y0 + 0.02, 0), 1.8, (0, 1, 0), 6, salt=2)
    for k_, pos, n_ in (("E", (X1, -6.5), (-1, 0, 0)), ("E", (X1, 8.5), (-1, 0, 0)), ("N", (-14.0, Y1), (0, -1, 0)), ("W", (X0, -3.0), (1, 0, 0))):
        extinguisher(wd[k_], (pos[0], pos[1], 0), n_)
    wall_panel(wd["N"], (-15.5, Y1, 0), (0, -1, 0))
    wall_panel(wd["E"], (X1, 11.5, 0), (-1, 0, 0), 0.8, 0.5, 2.6)
    for P_ in wd.values():
        P_.finish()
    dt = Parts("details", root)
    dt.cyl("Scoreboard", (8.0, Y1 - 0.05, 5.0), (8.0, Y1 - 0.12, 5.0), 0.3, segs=16)
    dt.cyl("Backdrop_White", (8.0, Y1 - 0.121, 5.0), (8.0, Y1 - 0.125, 5.0), 0.26, segs=16)
    cable(dt, [(1.8, -r["mats_half"] + 0.55), (0.6, -r["half_plat"] - 0.3), (-r["half_plat"] - 0.3, -r["half_plat"] - 0.3)])
    cable(dt, [(2.4, -r["mats_half"] + 0.4), (1.0, -r["mats_half"] - 3.0), (0.2, -r["mats_half"] - 6.0)])
    floor_decal(dt, "Decal_Tape", (X0 + 2.2, 0), 0.5)
    floor_decal(dt, "Decal_Tape", (-r["mats_half"] - 0.6, 0), 0.4)
    for k in range(3):
        floor_decal(dt, "Decal_Grime", (X1 - 2.0, -9.5 + 3.0 * k), 2.0, k)
    dt.finish()
    ce = Parts("ceiling_ducts", root)
    for sy in (-1, 1):
        ce.cyl("Steel_Beam", (X0 + 1.0, 1.0 + sy * 7.0, H - 1.2), (X1 - 1.0, 1.0 + sy * 7.0, H - 1.2), 0.3, segs=12)
        for k in range(6):
            x = X0 + 4.0 + k * 6.0
            ce.box("Panel_Grey", (x, 1.0 + sy * 7.0, H - 1.55), (0.6, 0.4, 0.12))
    ce.finish()

    # --- місця: розкладні стільці з трьох боків, прохід від завіси
    ch = Parts("chairs", root, lod=True)
    m = r["mats_half"]
    qt = m + 0.7                                                                               # штативи світла на кутах
    trees = [Vector((sx * qt, sy * qt, 0)) for sx in (-1, 1) for sy in (-1, 1)]
    no_tree = lambda p_: any((Vector((p_.x, p_.y, 0)) - t_).length < 1.0 for t_ in trees)
    no_cam = lambda p_: no_tree(p_) or (abs(p_.x) < 1.4 and p_.y < -m - 5.0)                   # поміст камери
    chair_rows(ch, (0, -m - 1.0, 0), 0.0, 4, 22, gap_every=11, gap=1.2, skip=no_cam)             # південь, лицем на північ
    side = lambda p_: no_tree(p_) or abs(p_.y) > m + 0.5                                      # кути — за південним/північним
    chair_rows(ch, (m + 1.0, 0, 0), math.pi / 2, 4, 18, gap_every=9, gap=1.2, skip=side)         # схід
    chair_rows(ch, (-m - 1.0, 0, 0), -math.pi / 2, 5, 18, gap_every=9, gap=3.0, skip=side)       # захід, прохід посередині
    chair_rows(ch, (0, m + 1.0, 0), math.pi, 2, 18, gap_every=9, gap=1.2, standing=0, skip=no_tree)   # північ
    ch.finish()

    lit = Parts("details", root)                                                               # сміття — після стільців
    litter(lit, 0, -m - 3.5, 12, 5, 30, salt=1)
    litter(lit, m + 3, 0, 4, 10, 20, salt=2)
    litter(lit, X1 - 2.5, -6.0, 3, 8, 20, salt=3)
    lit.finish()

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

    # --- світло: дешево, по-клубному — чотири штативи з двома прожекторами на кутах (світло дає ще й зал)
    lt = Parts("lighting", root)
    q = r["mats_half"] + 0.7
    for cx, cy_ in ((1, 1), (-1, 1), (1, -1), (-1, -1)):
        light_tree(lt, (cx * q, cy_ * q, 0), (0, 0, r["mat_z"] + 0.5), h=2.8, n=2)
    lt.finish()

    # --- біля рингу: стіл хронометриста й ведучого, дзвін, ноутбук, мікрофон, камера на стояку
    rs = Parts("ringside", root)
    ty = -r["mats_half"] + 0.55
    folding_table(rs, (1.8, ty, 0), math.pi, cloth="Cloth_Black")
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
    pb = Parts("details_backstage", root)
    poster_row(pb, (X0 - 3.5, 2.0, 0), (X0 - 3.5, 11.0, 0), 1.6, (1, 0, 0), 5, salt=21)
    extinguisher(pb, (X0 - 3.5, -4.5, 0), (1, 0, 0))
    pb.finish()
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
                capacity=len(SEATS), ambient=0.35)

def cams(root, items):
    for name, pos in items:
        empty("anchor_" + name, root, pos)

# ---------------------------------------------------------------- 2. клубна зала з балконом (регіональний)

def quad_n(P, mat, pts, normal):
    """Чотирикутник з лицем у бік normal (Unity відкидає зворотні грані — порядок вершин важливий)."""
    a, b, c = Vector(pts[0]), Vector(pts[1]), Vector(pts[2])
    if (b - a).cross(c - b).dot(Vector(normal)) < 0:
        pts = list(reversed(pts))
    P.quad(mat, pts)

def standing_area(cx, cy, w, d, n, focus=(0.0, 0.0), z=0.0, salt=0):
    """Стоячі місця: n позначок у прямокутнику w × d, кожна лицем до рингу з розкидом."""
    placed = 0
    for k in range(n * 6):                                         # не ближче 0,55 м одне до одного
        if placed >= n:
            break
        x = cx + (h01(k, 11 + salt) - 0.5) * w
        y = cy + (h01(k, 12 + salt) - 0.5) * d
        if not stand_free(x, y, z):
            continue
        yaw = math.atan2(focus[0] - x, -(focus[1] - y)) + math.radians((h01(k, 13 + salt) - 0.5) * 30.0)
        SEATS.append((x, y, z, math.degrees(yaw), "стоїть"))
        placed += 1

def chair_line(P, p0, p1, z, pitch=0.55, seat="Chair_Seat_Red", focus=(0.0, 0.0), salt=0):
    """Ряд стільців на балконі; розкид — лише якщо стілець не налізе на сусіда."""
    """Ряд розкладних стільців на висоті z від p0 до p1, кожен лицем до рингу з розкидом (балкони)."""
    p0, p1 = Vector((p0[0], p0[1], 0)), Vector((p1[0], p1[1], 0))
    L = (p1 - p0).length
    n = max(1, int(L / pitch))
    tang = (p1 - p0).normalized()
    side = Vector((-tang.y, tang.x, 0))
    for k in range(n):
        i = k + salt * 977
        for jit in (1.0, 0.5, 0.0):
            pos = p0 + tang * ((k + 0.5) * L / n + (h01(i, 21) - 0.5) * 0.1 * jit) + side * ((h01(i, 22) - 0.5) * 0.1 * jit)
            yaw = math.atan2(focus[0] - pos.x, -(focus[1] - pos.y)) + math.radians((h01(i, 23) - 0.5) * 20.0 * jit)
            if chair_free((pos.x, pos.y, z), yaw):
                folding_chair(P, (pos.x, pos.y, z), yaw, seat)
                break                                      # не влазить без перетину — місця немає

def hall_wall(P, a, b, H, inward, windows=(), pil_step=4.0):
    """Стіна старої зали: цегла внизу, тиньк угорі, дерев'яна панель, пілястри, карниз, високі вікна."""
    a, b = Vector((a[0], a[1], 0)), Vector((b[0], b[1], 0))
    d = b - a
    L = d.length
    t = d.normalized()
    n = Vector(inward)
    yaw = math.atan2(t.y, t.x)
    mid = (a + b) / 2
    th = 0.4
    c = mid - n * (th / 2)
    P.box("Brick_Warm", (c.x, c.y, 2.25), (L + th * 2 if abs(t.x) > 0.5 else th, th if abs(t.x) > 0.5 else L + th * 2, 4.5))
    P.box("Plaster_Cream", (c.x, c.y, 4.5 + (H - 4.5) / 2), (L + th * 2 if abs(t.x) > 0.5 else th, th if abs(t.x) > 0.5 else L + th * 2, H - 4.5))
    w = mid + n * 0.02
    P.box("Wainscot", (w.x, w.y, 0.6), (L if abs(t.x) > 0.5 else 0.04, 0.04 if abs(t.x) > 0.5 else L, 1.2))
    w = mid + n * 0.04
    P.box("Wainscot", (w.x, w.y, 1.22), (L if abs(t.x) > 0.5 else 0.08, 0.08 if abs(t.x) > 0.5 else L, 0.06))
    P.box("Wainscot", (w.x, w.y, 4.5), (L if abs(t.x) > 0.5 else 0.12, 0.12 if abs(t.x) > 0.5 else L, 0.18))       # пояс
    P.box("Wainscot", (w.x, w.y, H - 0.4), (L if abs(t.x) > 0.5 else 0.2, 0.2 if abs(t.x) > 0.5 else L, 0.35))     # карниз
    k = 0
    s = pil_step / 2
    while s < L:
        p = a + t * s + n * 0.12
        P.box("Brick_Warm", (p.x, p.y, H / 2), (0.5 if abs(t.x) > 0.5 else 0.24, 0.24 if abs(t.x) > 0.5 else 0.5, H))
        s += pil_step
        k += 1
    for (s0, z0, ww, hh) in windows:
        p = a + t * s0 + n * 0.03
        P.box("Wainscot", (p.x, p.y, z0 + hh / 2), (ww + 0.2 if abs(t.x) > 0.5 else 0.06, 0.06 if abs(t.x) > 0.5 else ww + 0.2, hh + 0.2))
        p = a + t * s0 + n * 0.07
        P.box("Glass", (p.x, p.y, z0 + hh / 2), (ww if abs(t.x) > 0.5 else 0.03, 0.03 if abs(t.x) > 0.5 else ww, hh))
        P.box("Wainscot", (p.x, p.y, z0 + hh / 2), (0.06 if abs(t.x) > 0.5 else 0.035, 0.035 if abs(t.x) > 0.5 else 0.06, hh))
        P.box("Wainscot", (p.x, p.y, z0 + hh * 0.62), (ww if abs(t.x) > 0.5 else 0.035, 0.035 if abs(t.x) > 0.5 else ww, 0.05))

def pendant(P, pos, z_top, drop=2.0):
    x, y = pos[0], pos[1]
    P.cyl("Light_Body", (x, y, z_top), (x, y, z_top - drop), 0.008, segs=4)
    P.cyl("Light_Body", (x, y, z_top - drop), (x, y, z_top - drop - 0.25), 0.05, 0.3, segs=12)
    P.cyl("Mirror_Bulb", (x, y, z_top - drop - 0.22), (x, y, z_top - drop - 0.3), 0.09, segs=8)
    AREAS.append(((x, y, z_top - drop - 0.35), 0.4, (1.0, 0.78, 0.5), 60.0))

def build_hall(root):
    spec = dict(size_ft=18, mat_z=1.1, canvas="Canvas_White", apron="Apron_Black", band="Apron_Band_Red",
                post="Post_Black", pads=("Pad_Black",), steps=("NE", "SW"), steps_mat="Steps_Black",
                mats="Mat_Black", mats_extra=1.3)
    r = build_ring(root, spec)
    X0, X1, Y0, Y1, H = -17.0, 17.0, -13.0, 16.0, 10.0
    m = r["mats_half"]
    q = m + 0.9                                     # огорожа
    BZ, BD = 3.6, 3.4                               # балкон: висота підлоги, глибина від стіни
    SY0, SH = 11.0, 1.0                             # сцена: передній край, висота

    # --- підлога: старий паркет дошками, навколо рингу — гумове покриття під огорожею
    sh = Parts("hall", root)
    sh.box("Hall_Floor", (0, (Y0 + Y1) / 2, -0.05), (X1 - X0, Y1 - Y0, 0.1))
    x = X0 + 0.6
    while x < X1:
        sh.box("Hall_Floor_Line", (x, (Y0 + Y1) / 2, 0.001), (0.02, Y1 - Y0, 0.002))
        x += 0.6
    sh.box("Carpet_Black", (0, 0, 0.004), (2 * q + 1.6, 2 * q + 1.6, 0.008))
    sh.finish()

    # --- стіни: цегла внизу, кремовий тиньк угорі, пілястри, карниз; високі вікна над балконом на сході й заході
    win = [(s_, 5.6, 1.4, 2.6) for s_ in (4.0, 8.0, 12.0, 16.0, 20.0)]
    walls = {n_: Parts("wall_" + n_, root) for n_ in ("N", "S", "E", "W")}
    hall_wall(walls["S"], (X0, Y0), (X1, Y0), H, (0, 1, 0))
    hall_wall(walls["E"], (X1, Y0), (X1, Y1), H, (-1, 0, 0), windows=win)
    hall_wall(walls["W"], (X0, Y0), (X0, Y1), H, (1, 0, 0), windows=win)
    hall_wall(walls["N"], (X0, Y1), (X1, Y1), H, (0, -1, 0), pil_step=34.0)
    for P_, pos, yaw in ((walls["E"], (X1 - 0.05, -9.0, 0), -math.pi / 2), (walls["W"], (X0 + 0.05, -9.0, 0), math.pi / 2),
                         (walls["S"], (-6.0, Y0 + 0.05, 0), 0.0), (walls["S"], (6.0, Y0 + 0.05, 0), 0.0)):
        door(P_, pos, yaw, w=1.6, mat="Door_Wood", sign=True)
    for P_ in walls.values():
        P_.finish()
    # стеля: дерев'яна обшивка, лучкові ферми через зал
    ceil = Parts("ceiling", root)
    ceil.box("Ceiling_Hall", (0, (Y0 + Y1) / 2, H + 1.25), (X1 - X0, Y1 - Y0, 0.1))
    for k in range(7):
        y = Y0 + 2.0 + k * (Y1 - Y0 - 4.0) / 6
        prev = None
        for j in range(13):
            u = j / 12
            p = Vector((X0 + u * (X1 - X0), y, H - 0.6 + 1.7 * math.sin(math.pi * u)))
            if prev is not None:
                ceil.cyl("Steel_Beam", prev, p, 0.07, segs=6)
                ceil.cyl("Steel_Beam", (prev.x, y, H - 0.6), (p.x, y, H - 0.6), 0.05, segs=6)
                ceil.cyl("Steel_Beam", (p.x, y, H - 0.6), p, 0.03, segs=4)
            prev = p
    ceil.finish()

    # --- сцена-портал на півночі: портал з банером, задник, завіса виходу, екран, бокові екрани, колонки
    st = Parts("stage", root)
    st.box("Stage_Black", (0, (SY0 + Y1) / 2, SH / 2), (16.0, Y1 - SY0, SH))
    st.box("Cloth_Black", (0, SY0 - 0.01, SH / 2), (16.0, 0.02, SH))                            # спідниця сцени
    st.box("LED_Strip", (0, SY0 + 0.02, SH + 0.01), (16.0, 0.04, 0.02))                          # світла смуга по краю
    for sx in (-1, 1):
        st.box("Stage_Black", (sx * 7.6, SY0 + 0.5, SH + 3.4), (0.8, 1.0, 6.8))                  # стовпи порталу
        st.box("Banner_Red", (sx * 7.6, SY0 - 0.01, SH + 3.4), (0.6, 0.02, 6.4))
        drape(st, (sx * 7.2, SY0 + 1.2, SH), (sx * 7.2, Y1 - 0.4, SH), 6.0, "Drape_Black")         # куліси
    st.box("Stage_Black", (0, SY0 + 0.5, SH + 7.2), (16.0, 1.0, 1.2))                             # перемичка порталу
    st.box("Banner_Gold", (0, SY0 - 0.01, SH + 7.2), (9.0, 0.02, 0.8))
    st.box("Banner_Black", (0, SY0 - 0.02, SH + 7.2), (8.4, 0.02, 0.55))
    drape(st, (-7.2, Y1 - 0.3, SH), (-1.3, Y1 - 0.3, SH), 6.4, "Drape_Black")                     # задник
    drape(st, (1.3, Y1 - 0.3, SH), (7.2, Y1 - 0.3, SH), 6.4, "Drape_Black")
    st.box("Stage_Black", (0, Y1 - 0.3, SH + 2.8 + 1.8), (2.6, 0.2, 3.6))
    st.finish()
    ent = Parts("entrance", root)
    for sx in (-1, 1):                                                                           # завіса виходу
        drape(ent, (sx * 1.25, Y1 - 0.45, SH), (sx * 0.02, Y1 - 0.45, SH), 2.8, "Drape_Red")
        ent.box("LED_Side", (sx * 1.55, Y1 - 0.5, SH + 1.4), (0.2, 0.05, 2.8))                    # світлові стовпи обабіч
    ent.box("Truss_Black", (0, Y1 - 0.5, SH + 2.9), (3.3, 0.3, 0.25))
    led_wall(ent, (0, Y1 - 0.6, SH + 4.6), 4.0, 2.25)
    for sx in (-1, 1):
        led_wall(ent, (sx * 4.6, Y1 - 0.6, SH + 3.6), 2.0, 3.6, mat="LED_Side")
        speaker_stack(ent, (sx * 9.0, SY0 - 0.6, 0), 0.0, n=3)                                    # колонки на підлозі
        light_tree(ent, (sx * 5.8, SY0 + 0.8, SH), (0, 4.0, 1.2), h=2.2, n=3)
    stairs(ent, (0, SY0, 0), 0.0, SH, 3.0, 4)                                                    # сходи зі сцени
    ent.box("Carpet_Red", (0, (SY0 - 1.2 + q) / 2, 0.006), (2.2, SY0 - 1.2 - q, 0.012))           # доріжка до рингу
    barricade_run(ent, (-1.6, SY0 - 1.3, 0), (-1.6, q, 0))
    barricade_run(ent, (1.6, SY0 - 1.3, 0), (1.6, q, 0))
    ent.finish()
    empty("anchor_curtain", root, (0, Y1 - 0.8, SH), yaw=180)
    path_anchors(root, [(0, Y1 - 1.4, SH), (0, SY0 + 0.4, SH), (0, SY0 - 1.4, 0), (0, q + 0.4, 0),
                        (r["half_plat"] + 0.9, r["half_plat"] - 0.2, 0)])

    # --- огорожа навколо рингу (прохід з півночі, вихід хронометриста з півдня)
    bar = Parts("barricade", root)
    barricade_run(bar, (-q, q, 0), (-1.6, q, 0))
    barricade_run(bar, (1.6, q, 0), (q, q, 0))
    barricade_run(bar, (q, q, 0), (q, -q, 0))
    barricade_run(bar, (q, -q, 0), (1.0, -q, 0))
    barricade_run(bar, (-1.0, -q, 0), (-q, -q, 0))
    barricade_run(bar, (-q, -q, 0), (-q, q, 0))
    bar.finish()

    # --- біля рингу: коментатори (схід), хронометрист (захід), поміст камери з режисером (південь)
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
    rs.box("Stage_Black", (-5.5, Y0 + 4.4, 0.6), (2.0, 2.0, 1.2))                               # поміст камери
    railing(rs, (-6.5, Y0 + 5.4, 1.2), (-4.5, Y0 + 5.4, 1.2))
    hd_camera(rs, (-5.5, Y0 + 4.4, 0), 0.0, 1.2)
    rs.finish()
    empty("anchor_commentary", root, (cx + 0.6, -1.2, 0), yaw=-90)
    empty("anchor_timekeeper", root, (-cx - 0.6, -1.0, 0), yaw=90)
    empty("anchor_bell", root, (-cx, -1.5, 0.78))
    empty("anchor_hardcam", root, (-5.5, Y0 + 4.4, 2.6))
    for i, (x, y) in enumerate(((q - 0.4, -q + 0.4), (-q + 0.4, -q + 0.4), (q - 0.4, q - 0.4), (-q + 0.4, q - 0.4))):
        empty("anchor_handheld_%d" % (i + 1), root, (x, y, 1.6))

    # --- партер: стільці рядами за огорожею з трьох боків, лицем до рингу, позаду — стоячі
    ch = Parts("chairs", root, lod=True)
    cols = []                                                                                  # колони балкона (як нижче)
    for (a_, b_) in (((X0 + BD, Y0 + BD), (X1 - BD, Y0 + BD)), ((X1 - BD, Y0 + BD), (X1 - BD, SY0 - 1.0)),
                     ((X0 + BD, Y0 + BD), (X0 + BD, SY0 - 1.0))):
        a_, b_ = Vector((a_[0], a_[1], 0)), Vector((b_[0], b_[1], 0))
        nc_ = max(1, int((b_ - a_).length / 4.0))
        cols += [a_ + (b_ - a_) * (j_ / nc_) for j_ in range(nc_ + 1)]
    no_col = lambda p_: any((Vector((p_.x, p_.y, 0)) - c_).length < 0.5 for c_ in cols)
    cam_skip = lambda p_: (abs(p_.x + 5.5) < 1.6 and p_.y < Y0 + 5.8) or no_col(p_)
    chair_rows(ch, (0, -q - 1.0, 0), 0.0, 5, 24, gap_every=12, gap=1.3, skip=cam_skip, standing=2, focus=(0, 0))
    side_h = lambda p_: no_col(p_) or p_.y < -q - 0.6                                         # кут — за південним блоком
    chair_rows(ch, (q + 1.15, -0.5, 0), 0.0, 6, 22, gap_every=11, gap=1.3, standing=1, focus=(0, 0), skip=side_h)
    chair_rows(ch, (-q - 1.15, -0.5, 0), 0.0, 6, 22, gap_every=11, gap=1.3, standing=1, focus=(0, 0), skip=side_h)
    ch.finish()
    for sx in (-1, 1):                                                                           # стоячі біля сцени
        standing_area(sx * 5.4, (q + SY0) / 2 + 0.3, 6.0, SY0 - q - 1.6, 45, salt=sx + 5)

    # --- балкон буквою П: тонка плита на колонах, дерев'яний фасад з банерами, три ряди на сходинках
    bc = Parts("balcony", root)
    bch = Parts("balcony_chairs", root, lod=True)
    EY1 = SY0 - 1.0
    fx0, fx1, fy0 = X0 + BD, X1 - BD, Y0 + BD
    slabs = [((0, Y0 + BD / 2), (X1 - X0, BD)),
             ((X1 - BD / 2, (fy0 + EY1) / 2), (BD, EY1 - fy0)),
             ((X0 + BD / 2, (fy0 + EY1) / 2), (BD, EY1 - fy0))]
    for (c, s) in slabs:
        bc.box("Concrete", (c[0], c[1], BZ - 0.12), (s[0], s[1], 0.24))
    fronts = [((fx0, fy0), (fx1, fy0)), ((fx1, fy0), (fx1, EY1)), ((fx0, fy0), (fx0, EY1))]
    for k, (a, b) in enumerate(fronts):
        a, b = Vector((a[0], a[1], 0)), Vector((b[0], b[1], 0))
        c = (a + b) / 2
        L = (b - a).length
        horiz = abs(b.x - a.x) > abs(b.y - a.y)
        bc.box("Balcony_Fascia", (c.x, c.y, BZ), (L + 0.1 if horiz else 0.12, 0.12 if horiz else L + 0.1, 0.8))
        bc.box("Wainscot", (c.x, c.y, BZ + 0.43), (L + 0.2 if horiz else 0.22, 0.22 if horiz else L + 0.2, 0.06))
        railing(bc, (a.x, a.y, BZ + 0.45), (b.x, b.y, BZ + 0.45), h=0.6)
        # банери, перекинуті через фасад
        nb = int(L / 3.2)
        for j in range(nb):
            p = a + (b - a) * ((j + 0.5) / nb)
            off = Vector((0, -0.08, 0)) if horiz else Vector((0.08 if b.x < 0 else -0.08, 0, 0))
            p = p + off
            mat = ("Banner_Red", "Banner_Black", "Banner_Gold", "Backdrop_White")[(j + k) % 4]
            bc.box(mat, (p.x, p.y, BZ - 0.55), (1.8 if horiz else 0.02, 0.02 if horiz else 1.8, 1.1))
        nc = max(1, int(L / 4.0))
        for j in range(nc + 1):
            p = a + (b - a) * (j / nc)
            bc.cyl("Steel_Beam", (p.x, p.y, 0), (p.x, p.y, BZ - 0.24), 0.09, segs=10)
    # сходинки (три ряди: від фасаду до стіни, кожен на 0,32 вище)
    rows = (0.55, 1.5, 2.45)
    for i, d in enumerate(rows):
        z = BZ + 0.32 * i
        if i:
            bc.box("Concrete", (0, Y0 + (BD - d + 0.45) / 2, z - 0.16), (X1 - X0, BD - d + 0.45, 0.32))
            for sx in (-1, 1):
                w_ = BD - d + 0.45
                bc.box("Concrete", (sx * (X1 - w_ / 2), (fy0 + EY1) / 2, z - 0.16), (w_, EY1 - fy0, 0.32))
        chair_line(bch, (fx0 + 0.8, fy0 - d), (fx1 - 0.8, fy0 - d), z, seat="Chair_Seat_Red", salt=i)
        chair_line(bch, (fx1 + d, fy0 + 0.8), (fx1 + d, EY1 - 0.4), z, seat="Chair_Seat_Red", salt=10 + i)
        chair_line(bch, (fx0 - d, fy0 + 0.8), (fx0 - d, EY1 - 0.4), z, seat="Chair_Seat_Red", salt=20 + i)
    # сходи на балкон біля сцени, вздовж бокових стін
    for sx in (-1, 1):
        n_ = 12
        for k in range(n_):
            h = BZ * (k + 1) / n_
            bc.box("Concrete", (sx * (X1 - 0.8), EY1 + 3.6 - (k + 0.5) * 0.3, h / 2), (1.4, 0.3, h))
        railing(bc, (sx * (X1 - 1.5), EY1 + 3.6, 0.0), (sx * (X1 - 1.5), EY1, BZ), h=0.95)
    bc.finish()
    bch.finish()

    # --- бар під балконом на сході: стійка, полиці з пляшками й підсвіткою, табурети
    br = Parts("bar", root)
    br.box("Bar_Wood", (X1 - 1.8, 2.0, 0.55), (0.7, 7.0, 1.1))
    br.box("Bar_Top", (X1 - 1.8, 2.0, 1.12), (0.9, 7.2, 0.05))
    br.box("Brick_Warm", (X1 - 1.45, 2.0, 0.1), (0.05, 7.0, 0.2))
    for k in range(3):
        br.box("Bar_Wood", (X1 - 0.35, 2.0, 1.2 + 0.45 * k), (0.35, 7.0, 0.04))
        for j in range(24):
            br.cyl(("Bottle", "Bar_Top", "Water_Blue")[(j + k) % 3], (X1 - 0.35, -1.2 + j * 0.28, 1.22 + 0.45 * k),
                   (X1 - 0.35, -1.2 + j * 0.28, 1.5 + 0.45 * k), 0.035, segs=6)
    br.box("Shelf_Light", (X1 - 0.2, 2.0, 2.65), (0.04, 7.0, 0.06))
    br.box("Shelf_Light", (X1 - 1.8, 2.0, BZ - 0.3), (0.6, 7.0, 0.04))
    for k in range(6):
        br.cyl("Chair_Frame", (X1 - 2.6, -0.8 + k * 1.1, 0), (X1 - 2.6, -0.8 + k * 1.1, 0.75), 0.03, segs=6)
        br.cyl("Chair_Seat_Black", (X1 - 2.6, -0.8 + k * 1.1, 0.75), (X1 - 2.6, -0.8 + k * 1.1, 0.8), 0.19, segs=12)
    for k in range(3):                                                                            # високі столики
        p = (X1 - 4.4 + 0.3 * h01(k, 31), -2.0 + 2.6 * k)
        br.cyl("Table_Leg", (p[0], p[1], 0), (p[0], p[1], 1.05), 0.03, segs=6)
        br.cyl("Bar_Top", (p[0], p[1], 1.05), (p[0], p[1], 1.09), 0.32, segs=12)
    br.finish()
    empty("anchor_bar", root, (X1 - 2.8, 2.0, 0), yaw=-90)
    standing_area(X1 - 3.4, 2.0, 2.4, 7.0, 25, salt=40)

    # --- дрібниці: афіші, вогнегасники, щитки, кабелі, сміття біля бару, скотч на сцені, бруд
    wd = {k_: Parts("wall_%s_decor" % k_, root) for k_ in "SEW"}
    poster_row(wd["W"], (X0 + 0.02, -11.0, 0), (X0 + 0.02, -2.0, 0), 1.7, (1, 0, 0), 4, salt=11)
    poster_row(wd["S"], (-12.0, Y0 + 0.02, 0), (12.0, Y0 + 0.02, 0), 1.7, (0, 1, 0), 8, salt=12)
    for k_, pos, n_ in (("E", (X1, -6.0), (-1, 0, 0)), ("W", (X0, -6.0), (1, 0, 0)), ("S", (-10.0, Y0), (0, 1, 0)), ("S", (10.0, Y0), (0, 1, 0))):
        extinguisher(wd[k_], (pos[0], pos[1], 0), n_)
    wall_panel(wd["W"], (X0, 9.0, 0), (1, 0, 0))
    for P_ in wd.values():
        P_.finish()
    dt = Parts("details", root)
    cable(dt, [(cx, -1.2), (r["half_plat"] + 0.3, -2.5), (2.0, -r["half_plat"] - 0.3)])
    cable(dt, [(-5.5, Y0 + 4.4), (-3.0, -q - 0.6), (-cx, -1.0)])
    litter(dt, X1 - 3.4, 2.0, 3, 7, 30, salt=4)
    litter(dt, 0, -q - 3.5, 12, 4, 25, salt=5)
    for x in (-4.0, 0.0, 4.0):
        floor_decal(dt, "Decal_Tape", (x, SY0 + 1.5), 0.45, z=SH)
    for k in range(4):
        floor_decal(dt, "Decal_Grime", (X1 - 2.8, -1.0 + 2.0 * k), 2.2, k)
    dt.finish()

    # --- мерч під балконом на заході
    mc = Parts("merch", root)
    for k in range(3):
        folding_table(mc, (X0 + 1.4, 1.0 + 2.0 * k, 0), -math.pi / 2, cloth=("Cloth_Black", "Cloth_Red")[k % 2])
        merch_rack(mc, (X0 + 0.5, 1.0 + 2.0 * k, 0), -math.pi / 2)
    mc.box("Banner_Gold", (X0 + 0.06, 3.0, 2.6), (0.02, 5.8, 0.6))
    mc.finish()
    empty("anchor_merch", root, (X0 + 2.2, 3.0, 0), yaw=-90)

    # --- світло: коробка над рингом 32 × 32 фути (пари й голови), ферма над сценою, теплі підвіси над залом
    lt = Parts("lighting", root)
    hb = 6.6
    b = 9.75 / 2
    pts = [(b, b, hb), (-b, b, hb), (-b, -b, hb), (b, -b, hb)]
    for k in range(4):
        truss_beam(lt, pts[k], pts[(k + 1) % 4])
        lt.cyl("Steel_Beam", pts[k], (pts[k][0], pts[k][1], H - 0.6), 0.012, segs=4)
        a, c = Vector(pts[k]), Vector(pts[(k + 1) % 4])
        for j in range(1, 6):
            p = a + (c - a) * (j / 6) - Vector((0, 0, 0.25))
            if j % 2:
                par_can(lt, p, (0, 0, r["mat_z"]))
            else:
                moving_head(lt, p, (0, 0, r["mat_z"]))
    truss_beam(lt, (-8.0, SY0 - 1.5, 7.4), (8.0, SY0 - 1.5, 7.4))
    for k in range(7):
        par_can(lt, (-6.0 + 2.0 * k, SY0 - 1.5, 7.1), (-3.0 + 1.0 * k, Y1 - 1.0, SH + 1.5),
                ("Light_Lens", "Light_Lens_Blue", "Light_Lens_Red")[k % 3])
    for i in range(4):
        for j in range(3):
            x = -9.0 + 6.0 * i
            y = -9.0 + 7.0 * j
            if abs(x) < 6 and abs(y) < 6:
                continue
            pendant(lt, (x, y), H - 0.6, drop=2.2)
    lt.finish()

    # --- зброя
    wp = Parts("weapons", root)
    chair_stack(wp, (X0 + 0.5, -3.0, 0), -math.pi / 2)
    folding_table(wp, (X0 + 0.7, -6.0, 0), math.pi / 2)
    ladder(wp, (-q - 0.6, q + 0.4, 0), (-q - 0.1, q + 0.4, 3.0))
    trash_can(wp, (q - 0.45, -q + 0.45, 0), "Trash_Grey")                                     # усередині огорожі
    kendo_bin(wp, (-cx, -2.4, 0))
    wp.box("Table_Top", (0, -r["half_plat"] + 0.4, 0.12), (1.83, 0.76, 0.04))
    wp.finish()
    empty("anchor_weapon_table", root, (X0 + 0.9, -6.0, 0))
    empty("anchor_weapon_chairs", root, (X0 + 0.9, -3.0, 0))
    empty("anchor_weapon_ladder", root, (-q - 0.4, q + 0.4, 0))
    empty("anchor_weapon_trash", root, (q - 0.45, -q + 0.45, 0))
    empty("anchor_weapon_kendo", root, (-cx, -2.4, 0))

    # --- закулісся за північною стіною: коридор, дві гримерки, «горила», інтерв'ю, кейтеринг, лікар
    BY0, BY1 = Y1 + 0.4, Y1 + 12.0
    bk = Parts("backstage", root)
    bk.box("Floor_Tile", (0, (BY0 + BY1) / 2, -0.05), (X1 - X0, BY1 - BY0, 0.1))
    bkw = Parts("wall_backstage", root)
    bkw.box("Wall_Grey", (0, BY1 + 0.15, 1.6), (X1 - X0, 0.3, 3.2))
    bkw.box("Wall_Grey", (X0 - 0.15, (BY0 + BY1) / 2, 1.6), (0.3, BY1 - BY0, 3.2))
    bkw.box("Wall_Grey", (X1 + 0.15, (BY0 + BY1) / 2, 1.6), (0.3, BY1 - BY0, 3.2))
    bkw.box("Wall_Grey", ((X0 - 1.0) / 2 - 1.0, BY0 + 3.6, 1.6), (-1.0 - X0 - 2.0, 0.2, 3.2))
    bkw.box("Wall_Grey", ((X1 + 1.0) / 2 + 1.0, BY0 + 3.6, 1.6), (X1 - 1.0 - 2.0, 0.2, 3.2))
    bkw.box("Wall_Grey", (0, (BY0 + 3.6 + BY1) / 2, 1.6), (0.2, BY1 - BY0 - 3.6, 3.2))
    bkw.finish()
    for sx in (-1, 1):
        door(bk, (sx * 5.0, BY0 + 3.6, 0), 0.0, mat="Door_Grey")
        mirror_wall(bk, (sx * 9.0, BY1 - 0.2, 0), math.pi, 4)
        bench(bk, (sx * 8.0, BY0 + 6.5, 0), 0.0, 3.0)
        lockers(bk, (sx * (X1 - 0.3), BY0 + 7.5, 0), sx * math.pi / 2 + (math.pi if sx > 0 else 0), 8, "Locker_Grey")
    for k in range(6):
        bk.box(("Shirt_A", "Shirt_B", "Shirt_C")[k % 3], (-3.0 - 0.25 * k, BY1 - 2.0, 1.2), (0.05, 0.5, 0.9))
    massage_table(bk, (4.0, BY1 - 2.5, 0), 0.0)
    folding_table(bk, (2.6, BY0 + 1.5, 0), math.pi / 2, length=1.83, cloth="Cloth_Black")
    for k in range(2):
        monitor(bk, (2.6, BY0 + 1.0 + k * 0.9, 0), math.pi / 2, z=0.76)
    step_and_repeat(bk, (X1 - 2.5, BY0 + 1.7, 0), -math.pi / 2, 2.8, 2.3)
    for k in range(2):
        light_tree(bk, (X1 - 5.0, BY0 + 0.8 + 1.8 * k, 0), (X1 - 2.5, BY0 + 1.7, 1.6), h=1.8, n=2)
    for k in range(2):
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
    for k in range(4):
        AREAS.append(((-12.0 + 8.0 * k, (BY0 + BY1) / 2, 3.1), 1.2, (1.0, 0.95, 0.88), 150.0))
    pb = Parts("details_backstage", root)
    poster_row(pb, (-14.0, BY0 + 3.48, 0), (-3.0, BY0 + 3.48, 0), 1.6, (0, -1, 0), 5, salt=22)
    poster_row(pb, (-14.0, BY1 - 0.02, 0), (-12.0, BY1 - 0.02, 0), 1.6, (0, -1, 0), 1, salt=23)
    extinguisher(pb, (6.0, BY0 + 3.5, 0), (0, -1, 0))
    pb.finish()
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
                ropes=("Black", "Black", "Black"), capacity=len(SEATS), ambient=0.12)

# ---------------------------------------------------------------- 3. палац спорту (середній промоушен)

class Bowl:
    """Чаша трибун: заокруглений прямокутник навколо підлоги (півосі ax, ay, центр cy, радіус кута rc),
    відкритий на півночі (там сцена) — кінці на y = top. t — довжина вздовж осьової лінії від сходу через південь
    на захід; точка з відступом d — на зовнішній нормалі."""
    def __init__(self, ax, ay, cy, rc, top):
        self.ax, self.ay, self.cy, self.rc, self.top = ax, ay, cy, rc, top
        self.L1 = top - (cy - ay + rc)
        self.La = rc * math.pi / 2
        self.L2 = 2 * (ax - rc)
        self.total = 2 * self.L1 + 2 * self.La + self.L2

    def at(self, t, d):
        ax, ay, cy, rc = self.ax, self.ay, self.cy, self.rc
        L1, La, L2 = self.L1, self.La, self.L2
        if t <= L1:
            p, n = Vector((ax, self.top - t)), Vector((1, 0))
        elif t <= L1 + La:
            a = -(t - L1) / rc
            n = Vector((math.cos(a), math.sin(a)))
            p = Vector((ax - rc, cy - ay + rc)) + n * rc
        elif t <= L1 + La + L2:
            p, n = Vector((ax - rc - (t - L1 - La), cy - ay)), Vector((0, -1))
        elif t <= L1 + 2 * La + L2:
            a = -math.pi / 2 - (t - L1 - La - L2) / rc
            n = Vector((math.cos(a), math.sin(a)))
            p = Vector((-ax + rc, cy - ay + rc)) + n * rc
        else:
            p, n = Vector((-ax, cy - ay + rc + (t - L1 - 2 * La - L2))), Vector((-1, 0))
        q = p + n * d
        return Vector((q.x, q.y, 0.0)), Vector((n.x, n.y, 0.0))

    def samples(self, extra=()):
        L1, La, L2 = self.L1, self.La, self.L2
        ts = set()
        for (t0, t1, step) in ((0, L1, 1.0), (L1, L1 + La, La / 12), (L1 + La, L1 + La + L2, 1.0),
                               (L1 + La + L2, L1 + 2 * La + L2, La / 12), (L1 + 2 * La + L2, self.total, 1.0)):
            n = max(1, round((t1 - t0) / step))
            for k in range(n + 1):
                ts.add(round(t0 + (t1 - t0) * k / n, 4))
        for t in extra:
            if 0 < t < self.total:
                ts.add(round(t, 4))
        return sorted(ts)

    def length(self, t0, t1, d, step=0.05):
        n = max(2, int((t1 - t0) / step))
        pts = [self.at(t0 + (t1 - t0) * k / n, d)[0] for k in range(n + 1)]
        acc = [0.0]
        for k in range(n):
            acc.append(acc[-1] + (pts[k + 1] - pts[k]).length)
        return pts, acc

def bowl_tier(B, P, walls, d0, z0, rows, depth, rise, seat_mat, aisles, voms=(), vom_rows=0, front_mat="Tier_Front",
              back_h=None, fascia=None):
    """Ярус чаші: сходинки (проступ + підступ) суцільною стрічкою, крісла між проходами, сходи в проходах,
    вомиторії (тунелі) у перших vom_rows рядах. back_h — висота задньої стіни над останнім рядом (окремі wall_*)."""
    SP = Parts(P.group + "_seats", P.parent, lod=True)
    ts = B.samples([v[0] for v in voms] + [v[1] for v in voms])
    in_vom = lambda t, r_: r_ < vom_rows and any(a <= t <= b for (a, b) in voms)
    for r_ in range(rows):
        di, do = d0 + r_ * depth, d0 + (r_ + 1) * depth
        if r_ == rows - 1 and back_h:
            do += 0.12                                # остання сходинка ширша: спинки крісел не в задній стіні
        z = z0 + r_ * rise
        zl = 0.0 if r_ == 0 else z - rise
        mat_r = front_mat if r_ == 0 else "Tier_Concrete"
        if r_ == 0 and fascia:
            mat_r = None
        for k in range(len(ts) - 1):
            t0, t1 = ts[k], ts[k + 1]
            if in_vom((t0 + t1) / 2, r_):
                continue
            a0, n0 = B.at(t0, di)
            a1, n1 = B.at(t1, di)
            b0, _ = B.at(t0, do)
            b1, _ = B.at(t1, do)
            quad_n(P, "Tier_Concrete", [(a0.x, a0.y, z), (a1.x, a1.y, z), (b1.x, b1.y, z), (b0.x, b0.y, z)], (0, 0, 1))
            nin = -(n0 + n1)
            if mat_r:
                quad_n(P, mat_r, [(a0.x, a0.y, zl), (a1.x, a1.y, zl), (a1.x, a1.y, z), (a0.x, a0.y, z)], nin)
            elif fascia:
                zf = max(zl, z - fascia[1])
                if zf > zl:
                    quad_n(P, "Tier_Front", [(a0.x, a0.y, zl), (a1.x, a1.y, zl), (a1.x, a1.y, zf), (a0.x, a0.y, zf)], nin)
                quad_n(P, fascia[0], [(a0.x, a0.y, zf), (a1.x, a1.y, zf), (a1.x, a1.y, z), (a0.x, a0.y, z)], nin)
            if r_ == rows - 1 and back_h:
                side = "S" if (n0.y + n1.y) < -1.414 else ("E" if n0.x + n1.x > 0 else "W")
                quad_n(walls[side], "Wall_Dark", [(b0.x, b0.y, z), (b1.x, b1.y, z), (b1.x, b1.y, z + back_h), (b0.x, b0.y, z + back_h)], nin)
                quad_n(P, "Tier_Front", [(b0.x, b0.y, z), (b1.x, b1.y, z), (b1.x, b1.y, z + 1.0), (b0.x, b0.y, z + 1.0)], nin)
    # вомиторії: підлога тунелю, стіни сходинками, портал під першим цілим рядом
    for (va, vb) in voms:
        if not vom_rows:
            continue
        dv = d0 + vom_rows * depth
        zv = z0 + (vom_rows - 1) * rise
        p0, n0 = B.at(va, d0)
        p1, _ = B.at(vb, d0)
        q0, _ = B.at(va, dv)
        q1, _ = B.at(vb, dv)
        quad_n(P, "Concourse", [(p0.x, p0.y, 0.01), (p1.x, p1.y, 0.01), (q1.x, q1.y, 0.01), (q0.x, q0.y, 0.01)], (0, 0, 1))
        quad_n(P, "Floor_Dark", [(q0.x, q0.y, 0.0), (q1.x, q1.y, 0.0), (q1.x, q1.y, zv), (q0.x, q0.y, zv)], -n0)
        for (tv, sgn) in ((va, 1), (vb, -1)):
            tan = (B.at(tv + 0.01, d0)[0] - B.at(tv, d0)[0]).normalized()
            for r_ in range(vom_rows):
                di, do = d0 + r_ * depth, d0 + (r_ + 1) * depth
                z = z0 + r_ * rise
                c0, _ = B.at(tv, di)
                c1, _ = B.at(tv, do)
                quad_n(P, "Tier_Front", [(c0.x, c0.y, 0.0), (c1.x, c1.y, 0.0), (c1.x, c1.y, z), (c0.x, c0.y, z)], tan * sgn)
            c0, _ = B.at(tv, d0)
            c1, _ = B.at(tv, dv)
            P.cyl("Railing_Steel", (c0.x, c0.y, z0 + 1.0), (c1.x, c1.y, zv + 1.0), 0.025, segs=6)
    # проходи: проміжна сходинка на кожному ряду
    for ta in aisles:
        for r_ in range(rows):
            if in_vom(ta, r_):
                continue
            di = d0 + r_ * depth
            z = z0 + r_ * rise
            c, n = B.at(ta, di + depth * 0.75)
            yaw = math.atan2(n.y, n.x)
            P.box("Aisle_Step", (c.x, c.y, z + rise / 4), (depth / 2, 1.0, rise / 2), rz=yaw)
            P.box("Aisle_Nose", (c.x - n.x * depth / 4, c.y - n.y * depth / 4, z + rise / 2 + 0.002), (0.05, 1.0, 0.006), rz=yaw)
    # крісла: між проходами й вомиторіями, крок 0,52 м за справжньою довжиною ряду
    cuts = sorted([(ta - 0.6, ta + 0.6) for ta in aisles])
    for r_ in range(rows):
        dseat = d0 + r_ * depth + depth - 0.3         # сидіння в глибині сходинки: ноги — на своїй сходинці
        z = z0 + r_ * rise
        blocked = list(cuts) + ([(a - 0.3, b + 0.3) for (a, b) in voms] if r_ < vom_rows else [])
        blocked.sort()
        spans, cur = [], 0.15
        for (a, b) in blocked:
            if a > cur:
                spans.append((cur, a))
            cur = max(cur, b)
        if cur < B.total - 0.15:
            spans.append((cur, B.total - 0.15))
        for (ta, tb) in spans:
            pts, acc = B.length(ta, tb, dseat)
            L = acc[-1]
            n = int(L / 0.56)
            if n < 1:
                continue
            j = 0
            for k in range(n):
                s = (k + 0.5) * L / n
                while j < len(acc) - 2 and acc[j + 1] < s:
                    j += 1
                u = (s - acc[j]) / max(1e-6, acc[j + 1] - acc[j])
                p = pts[j].lerp(pts[j + 1], u)
                tt = ta + (tb - ta) * (j + u) / (len(pts) - 1)
                _, nn = B.at(tt, dseat)
                stadium_seat(SP, (p.x, p.y, z), math.atan2(-nn.x, nn.y), seat_mat)
    SP.finish()
    return d0 + rows * depth + (0.12 if back_h else 0.0), z0 + (rows - 1) * rise

def stadium_seat(P, pos, yaw, mat):
    PLACE.append(("seat_stadium", (pos[0], pos[1], pos[2]), yaw, MATS[mat][0]))
    seat_mark(pos, yaw, pos[2] + 0.44)

def lod_seat(P, f, mat, zs, yb, zb, hb):
    """Далекий рівень сидіння: дві площини (сидіння і спинка лицем уперед) — 2 грані замість 18–42."""
    P.level = 1
    w = 0.22
    P.quad(mat, [f((-w, -0.2, zs)), f((w, -0.2, zs)), f((w, 0.2, zs)), f((-w, 0.2, zs))])
    P.quad(mat, [f((-w, yb, zb - hb / 2)), f((-w, yb, zb + hb / 2)), f((w, yb, zb + hb / 2)), f((w, yb, zb - hb / 2))])
    P.level = 0

def build_arena(root):
    spec = dict(size_ft=20, mat_z=1.2, canvas="Canvas_White", apron="Apron_Black", band="Apron_Band_Gold",
                post="Post_Black", pads=("Pad_Black",), steps=("NE", "SW"), steps_mat="Steps_Black",
                mats="Mat_Black", mats_extra=1.4)
    r = build_ring(root, spec)
    m = r["mats_half"]
    q = m + 1.0
    FX, FY0, CY, TOP = 20.0, -14.0, 1.0, 18.0           # підлога: ±FX, від FY0; центр чаші CY; кінці чаші на TOP
    SY0, SY1, SH = 16.0, 23.0, 1.5                       # сцена
    H = 24.0
    B = Bowl(FX, CY - FY0, CY, 6.0, TOP)

    # --- підлога арени: темний настил, доріжки до вомиторіїв, смуга навколо огорожі
    sh = Parts("arena", root)
    sh.box("Arena_Floor", (0, (FY0 + SY1) / 2, -0.05), (2 * FX + 60, SY1 - FY0 + 60, 0.1))
    sh.box("Carpet_Red", (0, (FY0 - q) / 2, 0.005), (2.0, -q - FY0, 0.01))
    sh.box("Carpet_Red", ((q + FX) / 2, CY, 0.005), (FX - q, 2.0, 0.01))
    sh.box("Carpet_Red", (-(q + FX) / 2, CY, 0.005), (FX - q, 2.0, 0.01))
    for sx, sy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        sh.box("Carpet_Black", (sx * (q + 0.6), sy * (q + 0.6), 0.004),
               (1.2 if sx else 2 * q + 2.4, 1.2 if sy else 2 * q + 2.4, 0.008))
    sh.finish()

    # --- чаша: нижній ярус (16 рядів, вомиторії), кільцевий хол, світлодіодний пояс, верхній ярус (12 рядів)
    walls = {n_: Parts("wall_" + n_, root) for n_ in ("S", "E", "W", "N")}
    aisles = []
    for (t0, t1, step) in ((0, B.L1, 6.5), (B.L1 + B.La + 1.5, B.L1 + B.La + B.L2 - 1.5, 6.0),
                           (B.total - B.L1, B.total, 6.5)):
        n = max(1, round((t1 - t0) / step))
        for k in range(1, n):
            aisles.append(t0 + (t1 - t0) * k / n)
    aisles += [B.L1 + B.La / 2, B.total - B.L1 - B.La / 2]
    voms = [(B.L1 * 0.45 - 1.5, B.L1 * 0.45 + 1.5), (B.total / 2 - 1.5, B.total / 2 + 1.5),
            (B.total - B.L1 * 0.45 - 1.5, B.total - B.L1 * 0.45 + 1.5)]
    aisles = [a for a in aisles if not any(v0 - 2.0 < a < v1 + 2.0 for (v0, v1) in voms)]
    lower = Parts("tiers_lower", root)
    lo_d, lo_z = bowl_tier(B, lower, walls, 0.0, 1.2, 13, 0.95, 0.4, "Seat_Red", aisles, voms, vom_rows=6)
    lower.finish()
    con = Parts("concourse", root)
    cz = lo_z + 0.35
    ts = B.samples()
    for k in range(len(ts) - 1):
        a0, n0 = B.at(ts[k], lo_d)
        a1, n1 = B.at(ts[k + 1], lo_d)
        b0, _ = B.at(ts[k], lo_d + 2.6)
        b1, _ = B.at(ts[k + 1], lo_d + 2.6)
        quad_n(con, "Concourse", [(a0.x, a0.y, cz), (a1.x, a1.y, cz), (b1.x, b1.y, cz), (b0.x, b0.y, cz)], (0, 0, 1))
        quad_n(con, "Tier_Front", [(a0.x, a0.y, lo_z), (a1.x, a1.y, lo_z), (a1.x, a1.y, cz), (a0.x, a0.y, cz)], -(n0 + n1))
        if k % 3 == 0:
            con.cyl("Railing_Steel", (a0.x, a0.y, cz), (a0.x, a0.y, cz + 1.0), 0.02, segs=6)
        con.cyl("Railing_Steel", (a0.x, a0.y, cz + 1.0), (a1.x, a1.y, cz + 1.0), 0.025, segs=6)
    con.finish()
    upper = Parts("tiers_upper", root)
    up_d, up_z = bowl_tier(B, upper, walls, lo_d + 2.6, cz + 1.4, 9, 1.0, 0.58, "Seat_Blue", aisles,
                           back_h=H - (cz + 1.4 + 8 * 0.58), fascia=("LED_Ribbon", 0.9))
    upper.finish()
    # торці чаші біля сцени: стіни-кулісні щити від підлоги до даху
    for sx in (-1, 1):
        x0, x1 = sx * FX, sx * (FX + up_d)
        walls["N"].box("Wall_Dark", ((x0 + x1) / 2, TOP + 0.2, H / 2), (abs(x1 - x0), 0.4, H))
        walls["N"].box("Drape_Black", (sx * (FX + 0.5 - 4.0), TOP - 0.05, 4.5), (8.0, 0.05, 9.0))
    walls["N"].box("Wall_Dark", (0, SY1 + 0.3, H / 2), (2 * (FX + up_d), 0.5, H))
    for P_ in walls.values():
        P_.finish()

    # --- дах: стеля, головні балки, технічний місток над рингом
    ro = Parts("ceiling", root)
    ro.box("Ceiling", (0, CY, H + 0.1), (2 * (FX + up_d) + 2, 2 * (CY - FY0 + up_d) + 10, 0.2))
    for k in range(9):
        y = FY0 - up_d + 3 + k * (SY1 - FY0 + up_d - 6) / 8
        ro.box("Steel_Beam", (0, y, H - 1.0), (2 * (FX + up_d), 0.5, 1.8))
    ro.finish()
    dec = Parts("wall_S_banners", root)                                                           # банери чемпіонів під дахом (ховаються зі стіною)
    for k in range(7):
        dec.box(("Banner_Red", "Banner_Black", "Banner_Gold")[k % 3], (-15.0 + 5.0 * k, FY0 - up_d + 0.6, H - 4.5), (2.6, 0.05, 4.0))
        dec.box("Banner_Gold", (-15.0 + 5.0 * k, FY0 - up_d + 0.57, H - 3.0), (2.0, 0.02, 0.4))
    dec.finish()
    dec = Parts("decor", root)
    for v in voms:
        p, n = B.at((v[0] + v[1]) / 2, 0.0)
        exit_sign(dec, (p.x - n.x * 0.05, p.y - n.y * 0.05, 2.6), math.atan2(-n.x, n.y) + math.pi)
    dec.finish()

    # --- вихідна сцена на півночі: LED-стіна з проходом, бічні екрани під кутом, рамна ферма з головами, рампа
    st = Parts("stage", root)
    st.box("Stage_Black", (0, (SY0 + SY1) / 2, SH / 2), (24.0, SY1 - SY0, SH))
    st.box("LED_Strip", (0, SY0 - 0.01, SH - 0.05), (24.0, 0.02, 0.06))
    for sx in (-1, 1):                                                                           # маскувальні щити по боках
        drape(st, (sx * 12.5, SY0, 0), (sx * 12.5, SY1, 0), 9.0, "Drape_Black", folds=True)
    st.finish()
    ent = Parts("entrance", root)
    led_wall(ent, (-4.0, SY1 - 0.6, SH + 3.6), 5.0, 6.6)
    led_wall(ent, (4.0, SY1 - 0.6, SH + 3.6), 5.0, 6.6)
    led_wall(ent, (0, SY1 - 0.6, SH + 5.4), 3.0, 3.0)
    drape(ent, (-1.5, SY1 - 0.5, SH), (1.5, SY1 - 0.5, SH), 3.8, "Drape_Black")
    for sx in (-1, 1):
        ent.box("LED_Strip", (sx * 1.55, SY1 - 0.55, SH + 1.9), (0.08, 0.05, 3.8))
        led_wall(ent, (sx * 9.6, SY1 - 2.2, SH + 3.2), 3.2, 5.2, yaw=sx * math.radians(-22), mat="LED_Side")
        truss_beam(ent, (sx * 11.6, SY0 + 0.6, SH), (sx * 11.6, SY0 + 0.6, SH + 8.6), 0.4)
        speaker_stack(ent, (sx * 11.0, SY0 + 2.2, SH), 0.0, n=3)
    truss_beam(ent, (-11.6, SY0 + 0.6, SH + 8.6), (11.6, SY0 + 0.6, SH + 8.6), 0.4)
    for k in range(9):
        moving_head(ent, (-9.0 + 2.25 * k, SY0 + 0.6, SH + 8.2), (-4.0 + k, SY0 - 6.0, SH))
    RY0 = q
    ramp_len = SY0 - RY0 - 1.6
    rc_ = SY0 - ramp_len / 2
    ent.box("Ramp_Black", (0, rc_, SH / 2), (3.0, ramp_len, SH))
    for sx in (-1, 1):
        ent.box("LED_Strip", (sx * 1.52, rc_, SH + 0.005), (0.05, ramp_len, 0.02))
        ent.box("LED_Strip", (sx * 1.505, rc_, SH - 0.1), (0.01, ramp_len, 0.04))
        ent.box("Cloth_Black", (sx * 1.51, rc_, SH / 2), (0.01, ramp_len, SH))
        barricade_run(ent, (sx * 2.6, SY0 - 0.2, 0), (sx * 2.6, RY0, 0))
    stairs(ent, (0, SY0 - ramp_len, 0), math.pi, SH, 3.0, 5, "Ramp_Black")
    for k in range(6):
        empty("anchor_pyro_%d" % (k + 1), root, (-10.0 + 4.0 * k, SY0 + 0.3, SH))
        ent.box("Case_Black", (-10.0 + 4.0 * k, SY0 + 0.3, SH + 0.12), (0.3, 0.3, 0.24))
    ent.finish()
    empty("anchor_curtain", root, (0, SY1 - 1.0, SH), yaw=180)
    path_anchors(root, [(0, SY1 - 1.6, SH), (0, SY0 - 0.2, SH), (0, rc_, SH),
                        (0, SY0 - ramp_len - 1.8, 0), (r["half_plat"] + 0.9, r["half_plat"] - 0.2, 0)])

    # --- огорожа навколо рингу з проходом під рампу
    bar = Parts("barricade", root)
    barricade_run(bar, (-q, q, 0), (-2.6, q, 0))
    barricade_run(bar, (2.6, q, 0), (q, q, 0))
    barricade_run(bar, (q, q, 0), (q, -q, 0))
    barricade_run(bar, (q, -q, 0), (-q, -q, 0))
    barricade_run(bar, (-q, -q, 0), (-q, q, 0))
    bar.finish()

    # --- біля рингу: стіл коментаторів з панеллю, хронометрист, поміст камери, кран, режисерський пульт
    rs = Parts("ringside", root)
    cx = q + 1.3
    rs.box("Table_Top", (cx, 0, 0.76), (1.0, 3.6, 0.05))
    rs.box("Banner_Black", (cx - 0.5, 0, 0.38), (0.04, 3.6, 0.76))
    rs.box("Banner_Gold", (cx - 0.53, 0, 0.55), (0.02, 3.0, 0.15))
    rs.box("LED_Strip", (cx - 0.53, 0, 0.1), (0.02, 3.6, 0.03))
    for k in range(3):
        folding_chair(rs, (cx + 0.75, -1.2 + 1.2 * k, 0), -math.pi / 2, crowd=False)
        monitor(rs, (cx - 0.25, -1.2 + 1.2 * k, 0), math.pi / 2, z=0.79)
    folding_table(rs, (-q + 1.25, -1.0, 0), -math.pi / 2, cloth="Cloth_Black")
    ring_bell(rs, (-q + 1.25, -1.5, 0), 0.78)
    laptop(rs, (-q + 1.25, -0.5, 0), -math.pi / 2, 0.76)
    folding_chair(rs, (-q + 0.62, -1.0, 0), math.pi / 2, crowd=False)
    rs.box("Stage_Black", (6.5, FY0 + 2.4, 1.1), (3.4, 2.4, 2.2))                                # поміст основної камери
    railing(rs, (4.8, FY0 + 3.6, 2.2), (8.2, FY0 + 3.6, 2.2))
    hd_camera(rs, (5.8, FY0 + 2.4, 0), 0.0, 2.2)
    hd_camera(rs, (7.2, FY0 + 2.4, 0), 0.0, 2.2)
    jib(rs, (q + 3.5, -q - 3.5, 0), (0, 0, 1.5))
    rs.box("Stage_Black", (-8.5, FY0 + 1.3, 0.3), (6.0, 1.8, 0.6))                                # режисерський пульт
    folding_table(rs, (-8.5, FY0 + 1.3, 0.6), 0.0, length=5.0, cloth="Cloth_Black")
    for k in range(5):
        monitor(rs, (-10.5 + k, FY0 + 1.5, 0), math.pi, z=1.36)
    rs.finish()
    empty("anchor_commentary", root, (cx + 0.75, 0, 0), yaw=-90)
    empty("anchor_timekeeper", root, (-q + 0.62, -1.0, 0), yaw=90)
    empty("anchor_bell", root, (-q + 1.25, -1.5, 0.78))
    empty("anchor_hardcam", root, (6.5, FY0 + 2.4, 3.6))
    empty("anchor_jib", root, (q + 3.5, -q - 3.5, 3.5))
    for i, (x, y) in enumerate(((q - 0.4, -q + 0.4), (-q + 0.4, -q + 0.4), (q - 0.4, q - 0.4), (-q + 0.4, q - 0.4))):
        empty("anchor_handheld_%d" % (i + 1), root, (x, y, 1.6))

    # --- стільці на підлозі арени: три блоки, лицем до рингу з розкидом, проходи до вомиторіїв
    ch = Parts("chairs", root, lod=True)
    jib_at = Vector((q + 3.5, -q - 3.5, 0))
    skip_s = lambda p_: ((abs(p_.x - 6.5) < 2.3 and p_.y < FY0 + 4.2) or (abs(p_.x + 8.5) < 3.4 and p_.y < FY0 + 3.0)
                         or (Vector((p_.x, p_.y, 0)) - jib_at).length < 2.4)
    skip_e = lambda p_: p_.x > FX - 1.0 or (Vector((p_.x, p_.y, 0)) - jib_at).length < 2.4 or p_.y > SY0 - 1.5 or p_.y < -q - 0.8
    skip_w = lambda p_: p_.x < -FX + 1.0 or p_.y > SY0 - 1.5 or p_.y < -q - 0.8
    chair_rows(ch, (0, -q - 1.4, 0), 0.0, 7, 36, gap_every=18, gap=2.0, skip=skip_s, standing=0, focus=(0, 0))
    chair_rows(ch, (q + 3.4, CY, 0), 0.0, 10, 36, gap_every=18, gap=2.0, skip=skip_e, standing=0, focus=(0, 0))
    chair_rows(ch, (-q - 1.6, CY, 0), 0.0, 12, 36, gap_every=18, gap=2.0, skip=skip_w, standing=0, focus=(0, 0))
    ch.finish()

    # --- дрібниці: кабелі від столів, скотч на сцені й рампі, бруд біля вомиторіїв, сміття на підлозі
    dt = Parts("details", root)
    cable(dt, [(cx, 0), (q + 0.4, -2.0), (r["half_plat"] + 0.3, -r["half_plat"] - 0.3)])
    cable(dt, [(6.5, FY0 + 2.4), (4.0, -q - 1.0), (q + 0.5, -q - 0.3)])
    cable(dt, [(-8.5, FY0 + 1.3), (-5.0, -q - 2.0), (-q - 0.4, -1.0)])
    for x in (-8.0, -4.0, 0.0, 4.0, 8.0):
        floor_decal(dt, "Decal_Tape", (x, SY0 + 1.2), 0.5, z=SH)
    for v in voms:
        p_, n_ = B.at((v[0] + v[1]) / 2, -1.2)
        floor_decal(dt, "Decal_Grime", (p_.x, p_.y), 2.6, 0.3)
    litter(dt, 0, -q - 4.0, 20, 6, 40, salt=6)
    litter(dt, q + 7.0, 1.0, 8, 16, 30, salt=7)
    litter(dt, -q - 7.0, 1.0, 8, 16, 30, salt=8)
    dt.finish()

    # --- підвісне світло: коробка 40 × 40 футів, лінійні масиви по кутах, відеокуб над рингом, ферма над рампою
    lt = Parts("lighting", root)
    hb = 10.0
    b = 12.2 / 2
    pts = [(b, b, hb), (-b, b, hb), (-b, -b, hb), (b, -b, hb)]
    for k in range(4):
        truss_beam(lt, pts[k], pts[(k + 1) % 4], 0.45)
        lt.cyl("Steel_Beam", pts[k], (pts[k][0], pts[k][1], H - 1.9), 0.014, segs=4)
        a, c = Vector(pts[k]), Vector(pts[(k + 1) % 4])
        for j in range(1, 8):
            p = a + (c - a) * (j / 8) - Vector((0, 0, 0.35))
            if j % 2:
                moving_head(lt, p, (0, 0, r["mat_z"]))
            else:
                par_can(lt, p, (0, 0, r["mat_z"]))
        line_array(lt, (pts[k][0] * 1.35, pts[k][1] * 1.35, 0), math.atan2(-pts[k][1], -pts[k][0]) + math.pi / 2, 13.0)
    for k in range(4):
        a = k * math.pi / 2
        c = Vector((1.6 * math.cos(a), 1.6 * math.sin(a), 15.0))
        lt.box("Screen_Frame", c, (3.2 if k % 2 else 0.1, 0.1 if k % 2 else 3.2, 2.0))
        lt.box("LED_Screen", c * 1.02 + Vector((0, 0, -0.3)), (3.0 if k % 2 else 0.02, 0.02 if k % 2 else 3.0, 1.8))
    lt.cyl("Steel_Beam", (0, 0, 16.0), (0, 0, H - 1.9), 0.05, segs=6)
    truss_beam(lt, (-6.0, rc_, 9.0), (6.0, rc_, 9.0), 0.4)
    for k in range(5):
        moving_head(lt, (-4.0 + 2.0 * k, rc_, 8.6), (0, rc_, SH))
    # «будинкове» світло над трибунами — тьмяне, щоб трибуни читались, а ринг лишався найяскравішим
    for k in range(10):
        t = B.total * (k + 0.5) / 10
        p, n = B.at(t, lo_d + 1.3)
        AREAS.append(((p.x, p.y, H - 2.5), 6.0, (0.85, 0.9, 1.0), 2500.0))
    lt.finish()

    # --- зброя
    wp = Parts("weapons", root)
    chair_stack(wp, (-q + 0.5, q - 0.35, 0), math.pi, 10)                                     # усередині огорожі
    folding_table(wp, (q + 0.8, q + 0.6, 0), 0.0)
    ladder(wp, (-q - 0.2, -q - 0.6, 0), (-q - 0.2, -q - 0.05, 3.4))
    trash_can(wp, (q + 0.5, -q - 0.5, 0), "Trash_Grey")
    kendo_bin(wp, (-q + 0.6, -2.4, 0))
    wp.box("Table_Top", (0, -r["half_plat"] + 0.4, 0.12), (1.83, 0.76, 0.04))
    wp.finish()
    empty("anchor_weapon_table", root, (q + 0.8, q + 0.6, 0))
    empty("anchor_weapon_chairs", root, (-q + 0.5, q - 0.35, 0))
    empty("anchor_weapon_ladder", root, (-q - 0.2, -q - 0.8, 0))
    empty("anchor_weapon_trash", root, (q + 0.5, -q - 0.5, 0))
    empty("anchor_weapon_kendo", root, (-q + 0.6, -2.4, 0))

    # --- закулісся за сценою: тунель, «горила», роздягальні, лікар, інтерв'ю, кейтеринг, рампа завантаження
    BY0, BY1 = SY1 + 0.6, SY1 + 15.4
    BX = 30.0
    bk = Parts("backstage", root)
    bk.box("Concrete", (0, (BY0 + BY1) / 2, -0.05), (2 * BX, BY1 - BY0, 0.1))
    bk.box("Ramp_Black", (0, BY0 + 2.0, SH / 2), (3.0, 4.0, SH))
    stairs(bk, (0, BY0 + 4.0, 0), math.pi, SH, 3.0, 5, "Ramp_Black")
    bkw = Parts("wall_backstage", root)
    bkw.box("Wall_Grey", (0, BY1 + 0.15, 2.0), (2 * BX, 0.3, 4.0))
    for sx in (-1, 1):
        bkw.box("Wall_Grey", (sx * BX, (BY0 + BY1) / 2, 2.0), (0.3, BY1 - BY0, 4.0))
        bkw.box("Wall_Grey", (sx * 1.7, BY0 + 2.0, 1.4 + SH / 2), (0.2, 4.0, 2.8))
        bkw.box("Wall_Grey", (sx * 16.0, BY0 + 9.0, 2.0), (0.25, 12.0, 4.0))
        bkw.box("Wall_Grey", (sx * 23.0, BY0 + 6.0, 2.0), (14.0, 0.25, 4.0))
    bkw.finish()
    folding_table(bk, (2.8, BY0 + 5.5, 0), math.pi / 2, length=2.4, cloth="Cloth_Black")
    for k in range(3):
        monitor(bk, (2.8, BY0 + 4.8 + 0.7 * k, 0), math.pi / 2, z=0.76)
    for k in range(2):
        folding_chair(bk, (3.5, BY0 + 5.0 + k, 0), -math.pi / 2, crowd=False)
    for sx in (-1, 1):
        lockers(bk, (sx * (BX - 0.3), BY0 + 10.5, 0), (-math.pi / 2 if sx > 0 else math.pi / 2), 14, "Locker_Grey")
        bench(bk, (sx * 22.5, BY0 + 10.0, 0), 0.0, 5.0)
        bench(bk, (sx * 22.5, BY0 + 12.0, 0), 0.0, 5.0)
        mirror_wall(bk, (sx * 20.0, BY1 - 0.2, 0), math.pi, 5)
        door(bk, (sx * 18.0, BY0 + 6.0, 0), 0.0)
    for k in range(2):
        massage_table(bk, (-10.0 + 3.0 * k, BY0 + 11.5, 0), math.pi / 2)
    bk.box("Shelf_Light", (-12.0, BY1 - 0.3, 1.8), (3.0, 0.3, 0.05))
    bk.box("Bench_Wood", (-12.0, BY1 - 0.3, 1.2), (3.0, 0.4, 0.04))
    step_and_repeat(bk, (9.0, BY1 - 0.5, 0), math.pi, 4.0, 2.6)
    for sx in (-1, 1):
        light_tree(bk, (9.0 + sx * 2.5, BY1 - 4.0, 0), (9.0, BY1 - 0.5, 1.7), h=2.0, n=2)
    hd_camera(bk, (9.0, BY1 - 4.5, 0), math.pi, 0.0)
    for k in range(3):
        folding_table(bk, (-6.0 + 2.2 * k, BY0 + 7.0, 0), 0.0, cloth="Cloth_Blue")
        for j in range(4):
            bk.box("Food", (-6.6 + 2.2 * k + 0.4 * j, BY0 + 7.0, 0.8), (0.3, 0.4, 0.06))
    for k in range(8):
        folding_chair(bk, (-6.5 + 1.0 * k, BY0 + 8.3, 0), math.pi, crowd=False)
    bk.box("Concrete", (BX + 6.0, BY0 + 8.0, 0.6), (12.0, 10.0, 1.2))
    bk.box("Truck_White", (BX + 7.5, BY0 + 6.5, 2.2), (2.5, 7.5, 2.6))
    bk.box("Truck_Cab", (BX + 7.5, BY0 + 1.6, 1.6), (2.4, 2.2, 2.2))
    for (tx, ty) in ((BX + 6.4, BY0 + 1.8), (BX + 8.6, BY0 + 1.8), (BX + 6.4, BY0 + 8.5), (BX + 8.6, BY0 + 8.5)):
        bk.cyl("Tire", (tx - 0.15, ty, 0.5), (tx + 0.15, ty, 0.5), 0.5, segs=12)
    for k in range(8):
        road_case(bk, (20.0 + 1.3 * (k % 4), BY0 + 1.2 + 1.0 * (k // 4), 0), 0.0)
    for k in range(6):                                                                            # кейси й за кулісами сцени
        road_case(bk, ((14.5 + 1.4 * (k % 3)) * (1 if k < 3 else -1), SY0 + 2.0 - BY0 + BY0, 0), 0.0)
    bk.finish()
    bkc = Parts("ceiling_backstage", root)
    bkc.box("Ceiling", (0, (BY0 + BY1) / 2, 4.05), (2 * BX, BY1 - BY0, 0.1))
    bkc.finish()
    for k in range(5):
        AREAS.append(((-20.0 + 10.0 * k, (BY0 + BY1) / 2, 3.9), 1.5, (1.0, 0.95, 0.9), 250.0))
    pb = Parts("details_backstage", root)
    poster_row(pb, (4.0, BY0 + 6.13, 0), (15.0, BY0 + 6.13, 0), 1.6, (0, -1, 0), 5, salt=24)
    poster_row(pb, (-15.0, BY0 + 6.13, 0), (-4.0, BY0 + 6.13, 0), 1.6, (0, -1, 0), 5, salt=25)
    for x in (-15.5, 15.5):
        extinguisher(pb, (x, BY0 + 6.1, 0), (0, -1, 0))
        wall_panel(pb, (x * 0.6, BY1, 0), (0, -1, 0))
    pb.finish()
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
                ropes=("Black", "Black", "Gold"), capacity=len(SEATS), ambient=0.1)

# ---------------------------------------------------------------- огляд і експорт

def set_engine(scene, names):
    for n in names:
        try:
            scene.render.engine = n
            return n
        except TypeError:
            continue
    return None

def add_lights(info):
    made = []
    for k, (pos, d, color, cone) in enumerate(LIGHTS):
        L = bpy.data.lights.new("spot%d" % k, 'SPOT')
        L.energy = 2500.0 if cone < 20 else 1500.0
        L.color = color
        L.spot_size = math.radians(cone)
        L.spot_blend = 0.4
        L.shadow_soft_size = 0.1
        o = bpy.data.objects.new("spot%d" % k, L)
        bpy.context.scene.collection.objects.link(o)
        o.location = pos
        o.rotation_euler = Vector(d).to_track_quat('-Z', 'Y').to_euler()
        made.append(o)
    for k, (pos, size, color, watts) in enumerate(AREAS):
        L = bpy.data.lights.new("area%d" % k, 'AREA')
        L.energy = watts
        L.color = color
        L.size = size
        L.use_shadow = False
        o = bpy.data.objects.new("area%d" % k, L)
        bpy.context.scene.collection.objects.link(o)
        o.location = pos
        made.append(o)
    w = bpy.data.worlds.new("look_world")
    w.use_nodes = True
    bg = next(n for n in w.node_tree.nodes if n.type == "BACKGROUND")
    bg.inputs[0].default_value = (0.55, 0.6, 0.7, 1)
    bg.inputs[1].default_value = info.get("ambient", 0.2)
    bpy.context.scene.world = w
    return made

def glare(scene):
    """Відблиск яскравих лінз, екранів та іскор (композитор): світло читається як світло."""
    try:
        scene.use_nodes = True
        nt = scene.node_tree
        rl = next((n for n in nt.nodes if n.type == 'R_LAYERS'), None) or nt.nodes.new("CompositorNodeRLayers")
        comp = next((n for n in nt.nodes if n.type == 'COMPOSITE'), None) or nt.nodes.new("CompositorNodeComposite")
        g = nt.nodes.new("CompositorNodeGlare")
        for attr, val in (("glare_type", 'FOG_GLOW'), ("quality", 'MEDIUM'), ("threshold", 1.2), ("size", 7), ("mix", -0.6)):
            try:
                setattr(g, attr, val)
            except (AttributeError, TypeError):
                pass
        for attr, val in (("Threshold", 1.2), ("Size", 0.6), ("Strength", 0.35)):
            if attr in g.inputs:
                g.inputs[attr].default_value = val
        nt.links.new(rl.outputs["Image"], g.inputs[0])
        nt.links.new(g.outputs[0], comp.inputs[0])
    except Exception as e:                                  # композитор — лише прикраса знімка
        print("glare: %s" % e, flush=True)

VIDEO_S = next((float(a.split("=", 1)[1]) for a in args if a.startswith("--video-s=")), 7.0)
VIDEO_FPS = 30
ANIM_RADIUS = {"gym": 9.0, "hall": 9.5, "arena": 13.0}       # ближче до рингу — живі фанати зі скелетом, далі — пози

def live_fans(scene):
    """Ближні фанати — справжні скелети з кліпами (зсув фази — NLA), далекі лишаються запеченими позами."""
    with bpy.data.libraries.load(CROWD_LIB, link=False) as (src, dst):
        dst.collections = [c for c in src.collections if c == "FanRigs"]
        dst.actions = [a for a in src.actions if a.endswith("_Loop")]      # кліпи натовпу (лежать з фіктивним користувачем)
    rigs = bpy.data.collections.get("FanRigs")
    if rigs is None:
        return 0
    proto = {}
    for o in rigs.objects:
        if o.type == 'ARMATURE':
            proto.setdefault(o.name, {})["arm"] = o
        elif o.name.endswith("_LOD0"):
            proto.setdefault(o.name[:-5], {})["mesh"] = o
    col = bpy.data.collections.new("LiveCrowd")
    scene.collection.children.link(col)
    dz_cache = {}
    n = 0
    R = ANIM_RADIUS.get(scene.get("venue", ""), 10.0)
    cand = [o for o in bpy.data.objects if o.type == 'MESH' and "crowd" in o.keys()]
    print("VIDEO кандидатів %d, прототипів %d (%s)" % (len(cand), len(proto), list(proto)[:2]), flush=True)
    for o in cand:
        f_ = o["crowd"].split()
        x, y, z, yaw, fan, clip, ph = float(f_[0]), float(f_[1]), float(f_[2]), float(f_[3]), f_[4], f_[5], int(f_[6])
        if math.hypot(x, y) > R or fan not in proto or "mesh" not in proto[fan]:
            continue
        act = next((a for a in bpy.data.actions if a.name.endswith(clip)), None)
        if act is None:
            continue
        pa, pm = proto[fan]["arm"], proto[fan]["mesh"]
        arm = pa.copy()
        col.objects.link(arm)
        me = pm.copy()
        col.objects.link(me)
        me.parent = arm
        for md in me.modifiers:
            if md.type == 'ARMATURE':
                md.object = arm
        ad = arm.animation_data_create()
        ad.action = None
        tr = ad.nla_tracks.new()
        st = tr.strips.new(clip, 1, act)
        st.repeat = 20.0
        st.frame_start = 1 - int(ph * 30 + 17 * h01(n, 77)) % 60
        if hasattr(st, "action_slot") and getattr(act, "slots", None):
            st.action_slot = act.slots[0]
        key = (fan, clip)
        if key not in dz_cache:                               # як у запечених поз: таз на сидінні / стопи на підлозі
            arm.location = (0, 0, 0)
            arm.rotation_euler = (0, 0, 0)
            scene.frame_set(1)
            bpy.context.view_layer.update()
            dg = bpy.context.evaluated_depsgraph_get()
            ev = me.evaluated_get(dg)
            low = min((ev.matrix_world @ v.co).z for v in ev.data.vertices)
            if clip.startswith("Sit_"):                       # як bake_pose у ring_crowd.py
                dz = 0.56 - (arm.matrix_world @ arm.pose.bones["pelvis"].head).z
                dz_cache[key] = dz + max(0.0, 0.004 - (low + dz))
            else:
                dz_cache[key] = -low + 0.005
        arm.location = (x, y, z + dz_cache[key])
        arm.rotation_euler = (0, 0, math.radians(yaw))
        me.color = o.color
        me.matrix_parent_inverse = arm.matrix_world.inverted() @ arm.matrix_world   # одиничне: сітка в просторі скелета
        o.hide_render = True
        n += 1
    return n

def video(root, info, scene, show, cam, hide):
    """Відео 7 с: сцена світла VIDEO, погоня голів по такту, іскри частинками, ближній натовп живий, камера пливе."""
    c = next(c_ for c_ in CUES.get(info["id"], ()) if c_["name"] == VIDEO)
    scene["venue"] = info["id"]
    nlive = live_fans(scene)
    print("VIDEO %s/%s: живих фанатів %d" % (info["id"], VIDEO, nlive), flush=True)
    scene.render.fps = VIDEO_FPS
    scene.frame_start, scene.frame_end = 1, int(VIDEO_S * VIDEO_FPS)
    beat_frames = VIDEO_FPS * 60.0 / max(60, c["bpm"] or 120)
    for f in range(scene.frame_start, scene.frame_end + 1):
        beat = int((f - 1) / beat_frames)
        sub = ((f - 1) % beat_frames) / beat_frames
        show.apply(c, beat=beat)
        for o, pos, d, color, kind in show.spots:
            L = o.data
            if kind == "par" and c["bpm"]:                        # PAR дихають у такт: удар — спалах, далі спад
                L.energy *= 0.55 + 0.9 * (1.0 - sub) ** 2
            L.keyframe_insert("energy", frame=f)
            L.keyframe_insert("color", frame=f)
            o.keyframe_insert("rotation_euler", frame=f)
        if f % 6 == 1:
            print("VIDEO ключі %d/%d" % (f, scene.frame_end), flush=True)
    # іскри — частинки (у відео летять), статичні іскри знімків ховаємо
    for name in ("sparklers", "pyro"):
        for o in show.fx.get(name, ()):
            if o.type == 'MESH':
                o.hide_render = True
    particle_sparks(scene, show, c)
    # камера: від кута рингу до виходу, повільно
    curtain = show.obs["anchor_curtain"].matrix_world.translation
    hp = info["ring"]["half_plat"]
    p0 = Vector((hp + 4.0, -hp - 5.0, 2.4))
    p1 = Vector((hp + 1.8, -hp - 2.4, 3.2))
    for f, p in ((scene.frame_start, p0), (scene.frame_end, p1)):
        cam.location = p
        tgt = curtain.lerp(Vector((0, 0, 1.4)), 0.45) + Vector((0, 0, 1.0))
        cam.rotation_euler = (tgt - p).to_track_quat('-Z', 'Y').to_euler()
        cam.keyframe_insert("location", frame=f)
        cam.keyframe_insert("rotation_euler", frame=f)
    cam.data.lens = 26
    hide(["ceiling"], True)
    scene.render.resolution_x, scene.render.resolution_y = 1280, 720
    for fr in sorted({scene.frame_start + (scene.frame_end - scene.frame_start) // 3, scene.frame_end - 2}):   # контрольні кадри
        scene.frame_set(fr)
        scene.render.image_settings.file_format = 'PNG'
        scene.render.filepath = os.path.join(LOOK_DIR, "%s_%s_videoframe_%03d.png" % (info["id"], VIDEO, fr))
        bpy.ops.render.render(write_still=True)
    scene.render.image_settings.file_format = 'FFMPEG'
    scene.render.ffmpeg.format = 'MPEG4'
    scene.render.ffmpeg.codec = 'H264'
    scene.render.ffmpeg.constant_rate_factor = 'HIGH'
    scene.render.filepath = os.path.join(LOOK_DIR, "%s_%s_video.mp4" % (info["id"], VIDEO))
    try:
        scene.eevee.taa_render_samples = 16
    except AttributeError:
        pass
    bpy.ops.render.render(animation=True)
    print("VIDEO готове: %s" % scene.render.filepath, flush=True)

def particle_sparks(scene, show, c):
    """Холодні іскри / піро як системи частинок (гравітація, час життя) — у грі те саме дає Particle System."""
    spots = []
    if "sparklers" in c["fx"]:
        cp = show.obs["anchor_curtain"].matrix_world.translation
        spots += [(cp + Vector((sx, -1.4, 0.1)), 5.5, 0.9, 1500) for sx in (-2.2, 2.2)]
    if "pyro" in c["fx"]:
        spots += [(o.matrix_world.translation + Vector((0, 0, 0.3)), 10.0, 1.4, 2500)
                  for nm, o in show.obs.items() if nm.startswith("anchor_pyro_")]
    if not spots:
        return
    bm = bmesh.new()
    r = bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=(0.012, 0.012, 0.09), verts=r["verts"])
    me = bpy.data.meshes.new("spark")
    bm.to_mesh(me)
    bm.free()
    spark = bpy.data.objects.new("spark", me)
    scene.collection.objects.link(spark)
    me.materials.append(show._emit_mat("FX_Spark", SPARK_COLOR, 40.0))
    spark.hide_render = True
    for j, (base, v0, life, count) in enumerate(spots):
        bm = bmesh.new()
        bmesh.ops.create_circle(bm, cap_ends=True, segments=8, radius=0.08)
        em = bpy.data.meshes.new("emitter%d" % j)
        bm.to_mesh(em)
        bm.free()
        eo = bpy.data.objects.new("emitter%d" % j, em)
        scene.collection.objects.link(eo)
        eo.location = base
        ps = eo.modifiers.new("sparks", 'PARTICLE_SYSTEM').particle_system
        st = ps.settings
        st.count = count
        st.frame_start, st.frame_end = 1, scene.frame_end
        st.lifetime = int(life * VIDEO_FPS)
        st.lifetime_random = 0.5
        st.normal_factor = v0
        st.factor_random = v0 * 0.35
        st.render_type = 'OBJECT'
        st.instance_object = spark
        st.particle_size = 1.0
        st.size_random = 0.5
        st.use_rotations = True
        st.rotation_mode = 'VEL'
        st.emit_from = 'FACE'
        eo.hide_render = False
        em.materials.append(show._emit_mat("FX_Spark", SPARK_COLOR, 40.0))

def look(root, info):
    os.makedirs(LOOK_DIR, exist_ok=True)
    scene = bpy.context.scene
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
            col = info.get("ropes", ("Black",) * 3)[i]
            P.cyl({"White": "Apron_Band_White", "Gold": "Apron_Band_Gold", "Red": "Pad_Red", "Blue": "Pad_Blue"}.get(col, "Pad_Black"),
                  a, b, 0.024, segs=10)
            for u in (0.2, 0.5, 0.8):                     # обмотка скотчем
                c_ = a.lerp(b, u)
                dd = (b - a).normalized() * 0.09
                P.cyl("Tape_Silver" if col != "White" else "Canvas_White", c_ - dd, c_ + dd, 0.0265, segs=10)
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
    shots["overview_flat"] = shots["overview"]
    hp = info["ring"]["half_plat"]
    shots["closeup"] = ((hp + 2.6, -hp - 3.2, 1.9), (hp * 0.2, -hp * 0.4, 0.9), 35, ["ceiling", "wall_S"])
    shots["overview_west"] = ((-ext * 0.95, -ext * 1.15, ext * 0.95), (0, 0, 1.0), 28, ["ceiling", "wall_S", "wall_W"])
    show = Show(root, info, scene)
    texts = preview_texts(scene)
    place_crowd(scene)
    lib = props_library()
    icol = bpy.data.collections.new("Instances")
    scene.collection.children.link(icol)
    for k, (pm, pp, py, pc) in enumerate(PLACE):
        o = bpy.data.objects.new("inst_%s_%d" % (pm, k), lib[pm][0])
        icol.objects.link(o)
        o.location = pp
        o.rotation_euler = (0, 0, py)
        o.color = (*pc, 1)
    lit = set_engine(scene, ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE"))
    if lit:
        for attr, val in (("taa_render_samples", 32), ("use_shadows", True), ("volumetric_tile_size", '4'),
                          ("volumetric_samples", 64), ("volumetric_end", 140.0), ("use_volumetric_shadows", True),
                          ("volumetric_light_clamp", 0.0)):
            try:
                setattr(scene.eevee, attr, val)
            except (AttributeError, TypeError):
                pass
        scene.view_settings.view_transform = 'AgX' if 'AgX' in [i.identifier for i in bpy.types.ColorManagedViewSettings.bl_rna.properties["view_transform"].enum_items] else 'Filmic'
        glare(scene)
    if VIDEO:
        video(root, info, scene, show, cam, hide)
        show.remove()
        return
    plan = []                                            # (файл, знімок, сцена світла)
    for c in CUES.get(info["id"], [cue("match", shots=tuple(shots))]):
        for sh in c["shots"]:
            plan.append(("%s_%s_%s" % (info["id"], c["name"], sh), sh, c))
    plan.append(("%s_overview_flat" % info["id"], "overview_flat", None))
    for fname, name, c in plan:
        if SHOTS and name not in SHOTS and (c is None or c["name"] not in SHOTS):
            continue
        pos, target, lens, hidden = shots[name]
        if c is not None:
            show.apply(c)
        set_engine(scene, ("BLENDER_WORKBENCH",) if (name == "overview_flat" or not lit) else ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE"))
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
        suffix = ("_a%02d" % round(crowd_target(info["id"])[1] * 100)) if (ATT_ARG is not None or PEOPLE_ARG is not None) else ""
        scene.render.filepath = os.path.join(LOOK_DIR, fname + suffix + ".png")
        bpy.ops.render.render(write_still=True)
        hide(hidden, False)
    show.remove()
    for o in ropes:
        bpy.data.objects.remove(o, do_unlink=True)
    bpy.data.objects.remove(cam, do_unlink=True)

# Злиття перед експортом: нерухомі групи — в «static», декор стіни — у стіну, дрібні групи — у свою кімнату; усередині
# цілі матеріали одного текстурного набору стають ОДНИМ матеріалом «Kit_<набір>», а відтінок (RGB) і множник
# шорсткості (A) переїжджають у колір вершин «tint». Так сітка = ціль × набір: на Android — десяток викликів малювання
# замість сотні. У Unity — один простий шейдер: альбедо × колір вершин (ітерація 3, сцена рингу).
# Світні матеріали (екрани, лінзи) і матеріали без набору лишаються як є. Групи з LOD не зливаються.
MERGE_STATIC = {"gym", "hall", "arena", "details", "ringside", "barricade", "merch", "bar", "bleachers", "hoops", "decor",
                "stage", "concourse", "tiers_lower", "tiers_upper", "balcony"}
MERGE_INTO = {"details_backstage": "backstage", "ceiling_ducts": "ceiling"}
def merge_target(group):
    if group in MERGE_STATIC:
        return "static"
    if group.startswith("wall_") and len(group) > 6 and group[5] in "NSEW" and group[6] == "_":
        return group[:6]
    return MERGE_INTO.get(group, group)

def kit_material(kset):
    name = "Kit_" + kset
    m = bpy.data.materials.get(name)
    if m is not None:
        return m
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    build_textured(m, kset, metal=KIT_METAL.get(kset, 0.0), attr="tint")
    m["kit"] = kset
    return m

def merge_static(root):
    buckets = {}
    for o in list(root.children_recursive):
        if o.type != 'MESH' or o.get("lod", -1) != -1 or not o.matrix_world.is_identity:
            continue
        mat = o.active_material.name
        kset = kit(mat) if mat in MATS and not MATS[mat][3] else None
        key = (merge_target(o.get("group", "")), ("Kit_" + kset) if kset else mat)
        buckets.setdefault(key, []).append((o, mat))
    for (tgt, mname), items in buckets.items():
        name = tgt + "_" + mname
        if len(items) == 1 and items[0][0].name == name and not mname.startswith("Kit_"):
            continue
        bm = bmesh.new()
        spans = []
        for o, mat in items:
            n0 = len(bm.verts)
            bm.from_mesh(o.data)
            spans.append((n0, len(bm.verts), mat))
        if mname.startswith("Kit_"):
            # відтінок пишемо після злиття, по шматках: from_mesh не переносить шари кольору наступних сіток
            layer = bm.verts.layers.float_color.new("tint")
            bm.verts.ensure_lookup_table()
            for n0, n1, mat in spans:
                color, metal, rough, emit = MATS[mat]
                f3 = kit_factor(mat)
                val = Vector((*(min(1.0, c / FACTOR_SCALE) for c in f3), rough / 0.6 / 1.7))   # RGB — множник/4, A — шорсткість/1,7
                for i in range(n0, n1):
                    bm.verts[i][layer] = val
        me = bpy.data.meshes.new(name + "_m")
        bm.to_mesh(me)
        bm.free()
        for o, _ in items:
            old = o.data
            bpy.data.objects.remove(o, do_unlink=True)
            if old.users == 0:
                bpy.data.meshes.remove(old)
        ob = bpy.data.objects.new(name, me)
        COL.objects.link(ob)
        ob.parent = root
        me.materials.append(kit_material(mname[4:]) if mname.startswith("Kit_") else material(mname))
        ob["lod"] = -1
        ob["group"] = tgt

def export(root, info):
    merge_static(root)
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
    with open(os.path.join(OUT, "venue_%s_props.txt" % info["id"]), "w", encoding="utf-8", newline="\n") as f:
        f.write("# Екземпляри реквізиту (Assets/Art/Ring/Props/props.fbx): модель x y z курс° r g b (колір сидіння). Разом: %d\n"
                % len(PLACE))
        for (pm, pp, py, pc) in PLACE:
            f.write("%s %.3f %.3f %.3f %.1f %.3f %.3f %.3f\n" % (pm, pp[0], pp[1], pp[2], math.degrees(py), *pc))
    with open(os.path.join(OUT, "venue_%s_seats.txt" % info["id"]), "w", encoding="utf-8", newline="\n") as f:
        sit = sum(1 for s_ in SEATS if s_[4] != "стоїть")
        f.write("# Місця для натовпу: x y z курс° сидить|стоїть (лицем до рингу). Разом: %d (сидячих %d, стоячих %d)\n"
                % (len(SEATS), sit, len(SEATS) - sit))
        for s_ in SEATS:
            f.write("%.2f %.2f %.2f %.0f %s\n" % s_)
    r = info["ring"]
    meshes = [o for o in root.children_recursive if o.type == 'MESH']
    near = [o for o in meshes if o.get("lod", -1) != 1]                 # видно зблизька (LOD0 і без LOD)
    far = [o for o in meshes if o.get("lod", -1) != 0]                  # видно здалеку (LOD1 і без LOD)
    faces = lambda obs: sum(len(o.data.polygons) for o in obs)
    trisf = lambda obs: sum(len(p.vertices) - 2 for o in obs for p in o.data.polygons)
    tris = faces(near)
    objs = len(near)
    kits = sorted({o.active_material.get("kit") for o in meshes if o.active_material and o.active_material.get("kit")})
    tex_bytes = 0
    for k_ in kits:
        size = 512 if k_ in ("plastic", "led_pixels") else 2048 if k_ in SETS and "CC0" in SETS[k_]["nor"] or k_ in (
            "wood_strip", "wood_board", "canvas", "panel_floor") else 1024
        tex_bytes += 3 * size * size * 0.445 * 1.33                      # три карти, ASTC 6×6 = 3,56 біт/піксель, мипи +33 %
    budget_f, budget_o = BUDGET[info["id"]]
    over = []
    if tris > budget_f:
        over.append("граней %d > %d" % (tris, budget_f))
    if objs > budget_o:
        over.append("сіток %d > %d" % (objs, budget_o))
    with open(os.path.join(OUT, "venue_%s_info.txt" % info["id"]), "w", encoding="utf-8", newline="\n") as f:
        f.write("назва %s\n" % info["title"])
        f.write("ринг_фути %d\nплатформа_половина_м %.3f\nмат_висота_м %.3f\nстійки_м %.3f\nвсередині_канатів_м %.3f\n"
                % (info["spec"]["size_ft"], r["half_plat"], r["mat_z"], r["post"], r["inner"]))
        f.write("канати %s\nмісткість %d\nоб'єктів_сітки %d\nграней %d\n" % (" ".join(info["ropes"]), len(SEATS), objs, tris))
        f.write("трикутників_зблизька %d\nтрикутників_здалеку %d\nбюджет_граней %d\nбюджет_сіток %d\n"
                % (trisf(near), trisf(far), budget_f, budget_o))
        f.write("текстурні_набори %s\nпам'ять_текстур_ASTC_МБ %.1f\n" % (" ".join(kits), tex_bytes / 1e6))
        lib = props_library()
        inst = {}
        for (pm, _, _, _) in PLACE:
            inst[pm] = inst.get(pm, 0) + 1
        for pm, n in sorted(inst.items()):
            t0 = sum(len(q.vertices) - 2 for q in lib[pm][0].polygons)
            t1 = sum(len(q.vertices) - 2 for q in lib[pm][1].polygons)
            f.write("екземпляри %s %d (LOD0 %d трик., LOD1 %d трик.; усі LOD0 — %d тис.)\n" % (pm, n, t0, t1, n * t0 // 1000))
    if over:
        raise SystemExit("БЮДЖЕТ %s перевищено: %s" % (info["id"], "; ".join(over)))
    return path, len(lines) - 1, objs, tris

for _fn in ("folding_table", "monitor", "laptop", "ring_bell", "mic_stand", "trash_can", "ladder", "kendo_bin", "chair_stack",
            "cooler", "water_cooler", "speaker_tripod", "speaker_stack", "line_array", "light_tree", "hd_camera", "jib",
            "lockers", "bench", "massage_table", "road_case", "merch_rack", "step_and_repeat", "barricade_run",
            "bike_rack", "arch_curtain", "extinguisher", "build_ring"):
    globals()[_fn] = itemize(globals()[_fn])

exec(open(os.path.join(REPO, "tools", "blender", "ring_props.py"), encoding="utf-8").read())
exec(open(os.path.join(REPO, "tools", "blender", "ring_show.py"), encoding="utf-8").read())
exec(open(os.path.join(REPO, "tools", "blender", "ring_collide.py"), encoding="utf-8").read())
CHECK_FAILS = 0

# ---------------------------------------------------------------- натовп (ring_crowd.py)
# Хто де: відвідуваність 0..1 → компанії по 2–5 сусідніх місць (одна компанія — одні кольори мерчу), перші ряди
# густіші, пусті місця «дірками»; на стоячих — щільно. Що робить: суміш кліпів за настроєм залу; частина сидячих
# підводиться. Детерміновано (хеш місця). Файл venue_<id>_crowd.txt — для гри (Unity ставить фанатів екземплярами).
CROWD_MANIFEST = os.path.join(REPO, "Assets", "Art", "Crowd", "crowd_manifest.txt")
CROWD_LIB = os.path.join(REPO, "Logs", "crowd", "crowd_lib.blend")
ATTENDANCE = {"gym": 0.82, "hall": 0.86, "arena": 0.74}
MOOD_SIT = [("Sit_Idle_Loop", 26), ("Sit_Clap_Loop", 20), ("Sit_Talk_Loop", 12), ("Sit_Cheer_Loop", 9), ("Sit_Point_Loop", 6),
            ("Sit_Phone_Loop", 9), ("Sit_Lean_Loop", 9), ("Sit_Boo_Loop", 9)]
MOOD_STAND = [("Stand_Idle_Loop", 16), ("Stand_Clap_Loop", 20), ("Stand_Cheer_Loop", 16), ("Stand_FistPump_Loop", 10),
              ("Stand_Jump_Loop", 7), ("Stand_Sign_Loop", 7), ("Stand_Chant_Loop", 14), ("Stand_Boo_Loop", 10)]
MERCH = [((0.025, 0.025, 0.03), 44), ((0.85, 0.85, 0.83), 12), ((0.25, 0.25, 0.27), 10), ((0.55, 0.05, 0.05), 10),
         ((0.05, 0.08, 0.25), 8), ((0.1, 0.25, 0.7), 5), ((0.75, 0.55, 0.1), 4), ((0.08, 0.35, 0.12), 4), ((0.3, 0.08, 0.4), 3)]
LOD1_FROM = 15.0               # м від центру рингу — далі фанат LOD1

def pick(table, u):
    tot = sum(w for _, w in table)
    acc = 0.0
    for v, w in table:
        acc += w / tot
        if u <= acc:
            return v
    return table[-1][0]

def crowd_fans():
    try:
        return [l.split()[1] for l in open(CROWD_MANIFEST, encoding="utf-8") if l.startswith("fan ")]
    except OSError:
        return []

CAMOPS = []          # оператори: (x, y, z, кліп) — з якорів anchor_camop_* (лицем до рингу)

# Відвідуваність — змінна шоу (власник 27.10.2026: «Натовп це плаваюча та рандомізована одиниця, їх від шоу к шоу
# може бути різна кількісь… зараз головне шоб це динамічно було і реально відображало скількі людей на шоу»).
# Вхід: частка (--attendance=0.6) або кількість людей (--people=300) і зерно шоу (--seed=7): та сама пара — та сама
# розсадка. Механіку (скільки прийде) гра визначить сама; генератор лише чесно показує задане число:
#   * компанії по 2–5 сусідніх місць заповнюються від найбажаніших — ближче до рингу й центру сектора, з розкидом
#     від зерна; лави трибун трохи менш бажані за стільці, стоячі — лише коли сидячих не лишилось (майже аншлаг);
#   * на малому шоу задні ряди й стоячі порожні, на аншлагу заповнені стоячі;
#   * колізії не зменшують число: знятого фаната заміняє наступний із запасу (наступне найкраще вільне місце).
ATT_ARG = next((float(a_.split("=", 1)[1]) for a_ in args if a_.startswith("--attendance=")), None)
PEOPLE_ARG = next((int(a_.split("=", 1)[1]) for a_ in args if a_.startswith("--people=")), None)
SEED_ARG = next((int(a_.split("=", 1)[1]) for a_ in args if a_.startswith("--seed=")), 0)
CROWD_RESERVE = []   # запас (наступні найкращі вільні місця) — для заміни знятих перевіркою колізій

def crowd_target(vid):
    cap = len(SEATS)
    if PEOPLE_ARG is not None:
        return max(0, min(cap, PEOPLE_ARG)), PEOPLE_ARG / max(1, cap)
    att = ATTENDANCE.get(vid, 0.8) if ATT_ARG is None else ATT_ARG
    return int(round(att * cap)), att

def _fan_at(i, seat, color, standing, fans, seed):
    x, y, z, yaw, kind = seat
    fan = fans[int(h01(i * 31 + seed, 36) * len(fans)) % len(fans)]
    clip = pick(MOOD_STAND if standing else MOOD_SIT, h01(i * 17 + seed, 37))
    col = color if h01(i + seed, 38) < 0.7 else pick(MERCH, h01(i + seed, 39))
    zz = z if kind == "стоїть" else z - 0.45
    if standing and kind != "стоїть":                                # підвівся перед своїм стільцем, у прохід
        a_ = math.radians(yaw)
        x, y = x + 0.36 * math.sin(a_), y - 0.36 * math.cos(a_)
    return (x, y, zz, yaw, fan, clip, int(h01(i + seed, 40) * 2), col)

def assign_crowd(vid):
    """[(x, y, z, курс°, фанат, кліп, фаза, колір футболки)]: оператори + люди на найкращих місцях до цільового числа."""
    fans = crowd_fans()
    if not fans:
        return []
    seed = SEED_ARG
    target, att = crowd_target(vid)
    out = []
    for i, (x, y, z, clip) in enumerate(CAMOPS):                     # оператори — у чорному, як команда шоу
        yaw = math.degrees(math.atan2(-x, y)) if clip != "Cam_Walk_Gimbal_Loop" else (90.0 if x < 0 else -90.0)
        out.append((x, y, z, yaw, fans[(i * 7 + 3) % len(fans)], clip, i % 2, (0.025, 0.025, 0.03)))
    groups, k, gi = [], 0, 0                                         # компанії сусідніх місць
    while k < len(SEATS):
        g = 2 + int(h01(gi * 13 + seed, 31) * 4)
        idx = [j for j in range(k, min(len(SEATS), k + g)) if SEATS[j][4] == SEATS[k][4]]
        x0 = sum(SEATS[j][0] for j in idx) / len(idx)
        y0 = sum(SEATS[j][1] for j in idx) / len(idx)
        kind = SEATS[idx[0]][4]
        want = -math.hypot(x0, y0) / 10.0 + (h01(gi * 7 + seed, 41) - 0.5) * 0.5
        if kind == "лава":
            want -= 0.12
        if kind == "стоїть":
            want -= 3.0                                              # стоять, лише коли сидячих не лишилось
        groups.append((want, gi, idx))
        k += len(idx)
        gi += 1
    groups.sort(key=lambda g_: -g_[0])
    people, reserve = [], []
    for want, gi, idx in groups:
        color = pick(MERCH, h01(gi + seed, 33))
        excited = h01(gi * 3 + seed, 34) < 0.16
        for j in idx:
            if h01(j * 5 + seed, 35) > 0.93 and att < 0.95:            # «дірка» в компанії
                continue
            kind = SEATS[j][4]
            standing = kind == "стоїть" or (excited and kind != "лава")
            (people if len(people) < target else reserve).append(_fan_at(j, SEATS[j], color, standing, fans, seed))
    CROWD_RESERVE[:] = reserve
    print("CROWD %s: відвідуваність %.0f %%, людей %d з %d місць, зерно %d" % (vid, att * 100, len(people), len(SEATS), seed),
          flush=True)
    return out + people

CROWD = []

def write_crowd(vid, keep=False):
    if not keep:
        CROWD[:] = assign_crowd(vid)
    with open(os.path.join(OUT, "venue_%s_crowd.txt" % vid), "w", encoding="utf-8", newline="\n") as f:
        tg, att_ = crowd_target(vid)
        f.write("# Натовп (відвідуваність %.0f %%, зерно %d — приклад одного шоу; число людей задає гра): x y z курс° фанат "
                "кліп фаза r g b. Разом: %d (з них операторів %d) з %d місць\n"
                % (att_ * 100, SEED_ARG, len(CROWD), len(CAMOPS), len(SEATS)))
        for c in CROWD:
            f.write("%.2f %.2f %.2f %.0f %s %s %d %.3f %.3f %.3f\n" % (c[0], c[1], c[2], c[3], c[4], c[5], c[6], *c[7]))

def place_crowd(scene):
    """Знімки: фанати — пов'язані копії запечених поз з бібліотеки (ring_crowd.py)."""
    if not CROWD or not os.path.exists(CROWD_LIB):
        return []
    want = {"pose_%s_%s_%d_L%d" % (c[4], c[5], c[6], lvl) for c in CROWD for lvl in (0, 1)}
    with bpy.data.libraries.load(CROWD_LIB, link=False) as (src, dst):
        dst.meshes = [n for n in src.meshes if n in want]
    col = bpy.data.collections.new("Crowd")
    scene.collection.children.link(col)
    made = []
    for k, (x, y, z, yaw, fan, clip, ph, colr) in enumerate(CROWD):
        lvl = 1 if math.hypot(x, y) > LOD1_FROM else 0
        me = bpy.data.meshes.get("pose_%s_%s_%d_L%d" % (fan, clip, ph, lvl))
        if me is None:
            continue
        o = bpy.data.objects.new("fan%d" % k, me)
        col.objects.link(o)
        o.location = (x, y, z)
        o.rotation_euler = (0, 0, math.radians(yaw))
        o.color = (*colr, 1)
        o["crowd"] = "%.3f %.3f %.3f %.1f %s %s %d" % (x, y, z, yaw, fan, clip, ph)
        made.append(o)
    return made

BUILDERS = {"gym": build_gym, "hall": build_hall, "arena": build_arena}
BUDGET = {"gym": (120000, 150), "hall": (120000, 150), "arena": (250000, 150)}     # граней зблизька, сіток (§11.0)
exec(open(os.path.join(REPO, "tools", "blender", "ring_gym.py"), encoding="utf-8").read())   # спортзал за референсом

for vid in WANT:
    bpy.ops.wm.read_homefile(use_empty=True)
    SEATS.clear()
    CROWD.clear()
    CAMOPS.clear()
    LIGHTS.clear()
    AREAS.clear()
    PLACE.clear()
    STAFF.clear()
    TEXTS.clear()
    for _bm in ITEMS.values():
        _bm.free()
    ITEMS.clear()
    PROPS.clear()
    if vid == WANT[0]:
        _pp, _pt = export_props(os.path.join(REPO, "Assets", "Art", "Ring", "Props"))
        print("PROPS %s %s" % (_pp, _pt), flush=True)
    COL = collection("Venue_" + vid)
    root = empty("venue_" + vid, None)
    info = BUILDERS[vid](root)
    write_crowd(vid)
    write_cues(vid)
    write_texts(vid)
    path, n_anchors, objs, tris = export(root, info)
    print("VENUE %s: %s — якорів %d, місць %d, об'єктів %d, граней %d" % (vid, path, n_anchors, len(SEATS), objs, tris), flush=True)
    if CHECK:
        for pass_ in range(5):
            total_, bad_fans = check_collisions(root, info)
            if not bad_fans or pass_ == 4 or CHECK_RAW:
                break
            for i_ in sorted(bad_fans, reverse=True):
                c_ = CROWD[i_]
                calm = "Sit_Idle_Loop" if c_[5].startswith("Sit_") else "Stand_Idle_Loop"
                if pass_ == 0 and c_[5] != calm:
                    CROWD[i_] = c_[:5] + (calm,) + c_[6:]
                elif CROWD_RESERVE and not c_[5].startswith("Cam_"):
                    CROWD[i_] = CROWD_RESERVE.pop(0)                   # те саме число людей: наступне найкраще місце
                else:
                    CROWD.pop(i_)
            print("CHECK %s: прохід %d — фанатів змінено/знято: %d" % (vid, pass_ + 1, len(bad_fans)), flush=True)
            write_crowd(vid, keep=True)
        CHECK_FAILS += total_
    if LOOK:
        look(root, info)
        print("VENUE %s: огляд готовий" % vid, flush=True)
if CHECK and CHECK_FAILS:
    raise SystemExit("КОЛІЗІЇ: %d (звіти — Assets/Art/Ring/venue_*_collisions.txt)" % CHECK_FAILS)
