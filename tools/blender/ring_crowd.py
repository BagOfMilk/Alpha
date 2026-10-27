# Натовп арен «Рингу» (ітерація 2, docs/TRPG_DESIGN.md §11.0.3): фанати й їхні анімації — власна генерація.
#
#   "C:\Program Files\Blender Foundation\Blender 4.5\blender.exe" -b --python tools/blender/ring_crowd.py -- <репо> [--look]
#
# Власник 27.10.2026: «Немає фанатів, до речі вони повинні займати різні місця», «в фанів теж мають бути анімації».
#
# Фанат = людина MPFB (тіло-проксі proxy741, шкіра за расою й віком; 8 культур, обидві статі, вік від підлітка до
# 60+) у сучасному одязі з бібліотеки MakeHuman — лише CC0: повсякденні комплекти з джинсами, футболки й поло,
# карго, джинсові шорти, кросівки, 23 зачіски; бейсболка — своя. Футболки перефарбовуються з екземпляра (колір
# об'єкта; у грі — MaterialPropertyBlock): мерч промоушену — найчастіше чорний. Рівні: LOD0 (~5 тис. трикутників,
# перші ряди) і LOD1 (~0,9 тис.).
#
# Анімації — пози кодом на скелеті набору (основа — alpha_anims.py: IK, полюси, пальці), петлі по 2 с: сидячи —
# спокій, розмова, оплески, руки вгору, «бу», вказує на ринг, знімає на телефон, подався вперед; стоячи — спокій,
# оплески, руки вгору, кулак угору, стрибки, табличка над головою, скандування ритмом, «бу»; біля огорожі — б'є по
# ній, тягнеться до борця. Assets/Art/Animations/CROWD_Anims.fbx (Unity бере як Humanoid).
#
# Виходи: Assets/Art/Crowd/crowd_fans.fbx (фанати зі скелетами, LOD0/LOD1), crowd_manifest.txt;
# Logs/crowd/crowd_lib.blend — бібліотека запечених поз (фанат × кліп × фаза) для знімків арен (ring_venues.py
# ставить їх екземплярами); у гру не йде: у грі анімує скелет (LOD0) і запечені вершинні текстури (LOD1, етап Unity).
# Детерміновано: розкид — хеш від імені, без random.
import bpy, bmesh, math, os, sys
from mathutils import Vector, Matrix, Quaternion

_args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
REPO = _args[0] if _args else os.getcwd()
LOOK = "--look" in _args
_src = open(os.path.join(REPO, "tools", "blender", "alpha_anims.py"), encoding="utf-8").read()
exec(_src.rsplit("\nmain()", 1)[0])          # Rig, Pose, apply, bake, write_action, strip_constraints, export, breathe
R = REPO
KIT = os.path.join(REPO, "Assets", "Art", "Characters", "Kit")
OUT_DIR = os.path.join(REPO, "Assets", "Art", "Crowd")
ANIM_FBX = os.path.join(REPO, "Assets", "Art", "Animations", "CROWD_Anims.fbx")
LIB_BLEND = os.path.join(REPO, "Logs", "crowd", "crowd_lib.blend")
LOOK_DIR = os.path.join(REPO, "Logs", "crowd")
FABRIC = os.path.join(REPO, "Assets", "ThirdParty", "CC0", "PolyHaven", "Textures", "fabric_pattern_07")

def h01(s, salt=0):
    x = 2166136261
    for ch in (str(s) + "#" + str(salt)):
        x = ((x ^ ord(ch)) * 16777619) & 0xFFFFFFFF
    x = ((x ^ (x >> 13)) * 1274126177) & 0xFFFFFFFF
    return ((x ^ (x >> 16)) & 0xFFFF) / 65535.0

# ---------------------------------------------------------------- пози

SEAT_Z = 0.45                    # висота сидіння (стілець і крісло трибуни)
SIT_PELVIS_Z = 0.56              # кістка таза сидячи (над сидінням — товщина стегна)
L = 2.0                          # довжина кожної петлі (с)
G = {"sit_dz": -0.4}             # зсув таза сидячи (рахується від спокою ригу)

def wave(t, hz, ph=0.0):
    return math.sin(2 * math.pi * (hz * t + ph))

def pulse(t, hz, width=0.35):
    """Імпульси 0..1 з частотою hz (петля, якщо hz × L — ціле)."""
    u = (t * hz) % 1.0
    return smooth(1.0 - min(1.0, abs(u - 0.5) / width)) if width else 0.0

def sit(t, lean=0.0, **kw):
    p = Pose(pelvis=Vector((0, 0.07, G["sit_dz"])), pelvis_pitch=-8.0 + lean * 0.25, pitch=lean + breathe(t, L, 1.0),
             ankles=(Vector((0.13, -0.40, 0.071)), Vector((-0.13, -0.41, 0.071))),      # гомілки майже вертикально
             knees=(Vector((0.16, -1.3, 0.62)), Vector((-0.16, -1.3, 0.62))), feet_yaw=(6.0, -6.0),
             fist={"l": 0.3, "r": 0.3}, forced=set())
    p.hands = {"l": (lambda S: Vector((0.16, -0.3, SIT_PELVIS_Z + 0.11)), FWD + DOWN * 0.3, RIGHT),
               "r": (lambda S: Vector((-0.16, -0.3, SIT_PELVIS_Z + 0.11)), FWD + DOWN * 0.3, LEFT)}
    p.elbows = {"l": lambda S: S["l"] + Vector((0.12, 0.25, -0.35)), "r": lambda S: S["r"] + Vector((-0.12, 0.25, -0.35))}
    for k, v in kw.items():
        setattr(p, k, v)
    return p

def stand(t, bounce=0.0, **kw):
    b = bounce * abs(wave(t, 1.5))
    p = Pose(pelvis=Vector((0, 0, -0.01 - b)), pitch=breathe(t, L, 1.0) + 2.0, fist={"l": 0.3, "r": 0.3}, forced=set())
    for k, v in kw.items():
        setattr(p, k, v)
    return p

CLAP_FORE = Vector((0, -0.5, 0.85)).normalized()
CLAP_UP = Vector((0, 0.8, 0.25)).normalized()

def clap_hands(p, gap_fn, lift=0.0, fwd=0.32):
    def grip(side):
        sgn = 1 if side == "l" else -1
        return lambda S: S["chest"] + Vector((sgn * (0.03 + gap_fn() / 2), -fwd, -0.04 + lift))
    p.hands = {"l": (grip("l"), CLAP_FORE, CLAP_UP), "r": (grip("r"), CLAP_FORE, CLAP_UP)}
    p.forced = {"l", "r"}
    p.fist = {"l": 0.05, "r": 0.05}
    p.elbows = {"l": lambda S: S["l"] + Vector((0.16, 0.1, -0.35)), "r": lambda S: S["r"] + Vector((-0.16, 0.1, -0.35))}
    return p

def arms_up(p, t, hz=1.0, spread=0.06, height=0.5, amp=0.06, fist=0.85, ph=0.0):
    a = amp * wave(t, hz, ph)
    p.hands = {"l": (lambda S: S["l"] + Vector((spread, -0.06, height + a)), UP, RIGHT),
               "r": (lambda S: S["r"] + Vector((-spread, -0.06, height - a)), UP, LEFT)}
    p.forced = set()
    p.fist = {"l": fist, "r": fist}
    p.elbows = {"l": lambda S: S["l"] + Vector((0.22, 0.15, 0.1)), "r": lambda S: S["r"] + Vector((-0.22, 0.15, 0.1))}
    return p

def boo(p, t):
    k = pulse(t, 1.5)
    p.hands = {s: (lambda S, s=s: S[s] + Vector(((0.08 if s == "l" else -0.08), -0.38, -0.02 - 0.08 * k)), FWD, DOWN)
               for s in "lr"}
    p.forced = {"l", "r"}
    p.fist = {"l": 0.95, "r": 0.95}
    p.head_pitch = -4.0
    return p

def keep_left_on_thigh(p, sitting):
    if sitting:
        p.hands["l"] = (lambda S: Vector((0.16, -0.3, SIT_PELVIS_Z + 0.11)), FWD + DOWN * 0.3, RIGHT)
    else:
        p.hands.pop("l", None)
    return p

# --- сидячи
def c_sit_idle(t, L_):
    return sit(t, head_yaw=12.0 * wave(t, 0.5), head_pitch=-2.0)

def c_sit_talk(t, L_):
    p = sit(t, head_yaw=-38.0 + 8.0 * wave(t, 1.0), roll=-3.0)
    g = Vector((0.05 * wave(t, 1.5), 0.0, 0.05 * wave(t, 1.5, 0.25)))
    p.hands["r"] = (lambda S: S["r"] + Vector((-0.02, -0.32, -0.18)) + g, FWD + UP * 0.3, LEFT)
    p.fist["r"] = 0.15
    return p

def c_sit_clap(t, L_):
    return clap_hands(sit(t, lean=6.0, head_pitch=-3.0), lambda: 0.14 * (0.5 + 0.5 * math.cos(2 * math.pi * 2.5 * t)))

def c_sit_cheer(t, L_):
    return arms_up(sit(t, lean=-4.0, head_pitch=-8.0), t, hz=1.0)

def c_sit_boo(t, L_):
    return boo(sit(t, lean=8.0), t)

def c_sit_point(t, L_):
    p = sit(t, lean=10.0, head_pitch=-4.0)
    p.hands["r"] = (lambda S: S["r"] + Vector((-0.04, -0.56, 0.1 + 0.03 * wave(t, 1.0))), FWD + UP * 0.15, UP)
    p.forced = {"r"}
    p.fist["r"] = 0.55
    return p

def c_sit_phone(t, L_):
    p = sit(t, head_pitch=6.0)
    p.hands["r"] = (lambda S: S["r"] + Vector((0.1, -0.3, 0.18 + 0.01 * wave(t, 0.5))), UP, BACK)
    p.forced = {"r"}
    p.fist["r"] = 0.5
    return p

def c_sit_lean(t, L_):
    p = sit(t, lean=26.0, head_pitch=-22.0)
    p.hands = {"l": (lambda S: Vector((0.05, -0.42, SIT_PELVIS_Z + 0.06)), DOWN + FWD * 0.3, BACK),
               "r": (lambda S: Vector((-0.05, -0.42, SIT_PELVIS_Z + 0.06)), DOWN + FWD * 0.3, BACK)}
    p.fist = {"l": 0.6, "r": 0.6}
    return p

# --- стоячи
def c_stand_idle(t, L_):
    return stand(t, head_yaw=10.0 * wave(t, 0.5))

def c_stand_clap(t, L_):
    return clap_hands(stand(t, bounce=0.01), lambda: 0.14 * (0.5 + 0.5 * math.cos(2 * math.pi * 2.5 * t)), lift=0.05)

def c_stand_cheer(t, L_):
    return arms_up(stand(t, bounce=0.03, head_pitch=-10.0), t, hz=1.5, amp=0.08)

def c_stand_fistpump(t, L_):
    p = stand(t, bounce=0.02, head_pitch=-8.0)
    k = pulse(t, 1.5, 0.3)
    p.hands = {"r": (lambda S: S["r"] + Vector((-0.06, -0.12, 0.2 + 0.3 * k)), UP, LEFT)}
    p.fist = {"l": 0.4, "r": 0.95}
    p.elbows = {"r": lambda S: S["r"] + Vector((-0.4, -0.1, 0.0))}
    return p

def c_stand_jump(t, L_):
    h = 0.11 * max(0.0, wave(t, 1.5))
    p = stand(t, head_pitch=-10.0)
    p.pelvis = Vector((0, 0, h - 0.02))
    p.ankles = (Vector((0.16, -0.015, 0.071 + h * 0.9)), Vector((-0.16, -0.015, 0.071 + h * 0.9)))
    return arms_up(p, t, hz=1.5, amp=0.06, height=0.45)

def c_stand_sign(t, L_):
    a = 0.04 * wave(t, 1.0)
    p = stand(t, head_pitch=-6.0)
    p.hands = {"l": (lambda S: S["l"] + Vector((0.12, -0.12, 0.48 + a)), UP, BACK),
               "r": (lambda S: S["r"] + Vector((-0.12, -0.12, 0.48 - a)), UP, BACK)}
    p.forced = {"l", "r"}
    p.fist = {"l": 0.7, "r": 0.7}
    p.elbows = {"l": lambda S: S["l"] + Vector((0.4, 0.1, 0.05)), "r": lambda S: S["r"] + Vector((-0.4, 0.1, 0.05))}
    return p

CHANT = (0.0, 0.5, 1.0, 1.25, 1.5)                # «та-та, та-та-та»: два повільні й три швидкі оплески

def c_stand_chant(t, L_):
    def gap():
        d = min(min(abs(t - c), abs(t - c - L), abs(t - c + L)) for c in CHANT)
        return 0.015 + 0.15 * smooth(d / 0.2)
    return clap_hands(stand(t, bounce=0.015, head_pitch=-6.0), gap, lift=0.12)

def c_stand_boo(t, L_):
    return boo(stand(t, pitch=6.0), t)

def c_rail_pound(t, L_):
    k = abs(wave(t, 1.0))
    p = stand(t, pitch=16.0, head_pitch=-12.0)
    p.hands = {"l": (lambda S: Vector((0.2, -0.4, 1.06 + 0.12 * k)), FWD, UP), "r": (lambda S: Vector((-0.2, -0.4, 1.06 + 0.12 * (1 - k))), FWD, UP)}
    p.forced = {"l", "r"}
    p.fist = {"l": 0.95, "r": 0.95}
    return p

def c_rail_reach(t, L_):
    p = stand(t, pitch=22.0, head_pitch=-14.0)
    p.hands = {"r": (lambda S: S["r"] + Vector((-0.05, -0.54, -0.1 + 0.04 * wave(t, 1.0))), FWD + DOWN * 0.2, UP),
               "l": (lambda S: Vector((0.2, -0.38, 1.08)), FWD, UP)}
    p.forced = {"r", "l"}
    p.fist = {"l": 0.9, "r": 0.0}
    return p

# --- оператори з недорогими камерами (власник 27.10.2026: «додай шо люди з недорогими камерами ходять»)
CAM_GRIP = lambda S: (S["l"] + S["r"]) / 2 + Vector((0, -0.34, -0.12))

def cam_hands(p, grip_fn, low=0.0):
    """Обидві руки тримають рукоятку стабілізатора/камеру перед собою: лікті при тілі."""
    p.hands = {"l": (lambda S: grip_fn(S) + Vector((0.06, 0.02, low)), FWD + UP * 0.6, RIGHT),
               "r": (lambda S: grip_fn(S) + Vector((-0.06, 0.02, low)), FWD + UP * 0.6, LEFT)}
    p.forced = {"l", "r"}
    p.fist = {"l": 0.85, "r": 0.85}
    p.elbows = {"l": lambda S: S["l"] + Vector((0.18, 0.1, -0.35)), "r": lambda S: S["r"] + Vector((-0.18, 0.1, -0.35))}
    return p

def c_cam_walk(t, L_):
    """Іде зі стабілізатором: крок на місці (гра рухає постать уздовж шляху), камера тримається рівно."""
    ph = 2 * math.pi * t / L_ * 2          # два кроки за петлю
    yl, yr = -0.2 * math.sin(ph), 0.2 * math.sin(ph)
    zl = 0.071 + 0.07 * max(0.0, math.cos(ph))
    zr = 0.071 + 0.07 * max(0.0, -math.cos(ph))
    p = stand(t, pitch=6.0, head_pitch=4.0)
    p.pelvis = Vector((0, 0, -0.02 - 0.015 * abs(math.cos(ph))))
    p.ankles = (Vector((0.12, yl, zl)), Vector((-0.12, yr, zr)))
    return cam_hands(p, CAM_GRIP)

def c_cam_stand(t, L_):
    p = stand(t, pitch=4.0, head_pitch=6.0, yaw=8.0 * wave(t, 0.5))
    return cam_hands(p, lambda S: (S["l"] + S["r"]) / 2 + Vector((0, -0.36, 0.02)))

def c_cam_kneel(t, L_):
    """Знімає з коліна (нижній ракурс): праве коліно на підлозі, ліва нога попереду."""
    p = Pose(pelvis=Vector((0, 0.05, -0.42)), pitch=10.0 + breathe(t, L_, 1.0), head_pitch=8.0,
             ankles=(Vector((0.15, -0.42, 0.071)), Vector((-0.13, 0.4, 0.1))),
             foot_rot=(Quaternion(UP, math.radians(6)), Quaternion(LEFT, math.radians(-70))),
             knees=(Vector((0.2, -1.2, 0.6)), Vector((-0.14, -0.9, 0.0))), fist={"l": 0.85, "r": 0.85}, forced=set())
    return cam_hands(p, lambda S: (S["l"] + S["r"]) / 2 + Vector((0, -0.32, -0.05)))

CLIPS_C = [
    ("Cam_Walk_Gimbal_Loop", c_cam_walk, "stand"), ("Cam_Stand_Film_Loop", c_cam_stand, "stand"),
    ("Cam_Kneel_Film_Loop", c_cam_kneel, "stand"),
    ("Sit_Idle_Loop", c_sit_idle, "sit"), ("Sit_Talk_Loop", c_sit_talk, "sit"), ("Sit_Clap_Loop", c_sit_clap, "sit"),
    ("Sit_Cheer_Loop", c_sit_cheer, "sit"), ("Sit_Boo_Loop", c_sit_boo, "sit"), ("Sit_Point_Loop", c_sit_point, "sit"),
    ("Sit_Phone_Loop", c_sit_phone, "sit"), ("Sit_Lean_Loop", c_sit_lean, "sit"),
    ("Stand_Idle_Loop", c_stand_idle, "stand"), ("Stand_Clap_Loop", c_stand_clap, "stand"),
    ("Stand_Cheer_Loop", c_stand_cheer, "stand"), ("Stand_FistPump_Loop", c_stand_fistpump, "stand"),
    ("Stand_Jump_Loop", c_stand_jump, "stand"), ("Stand_Sign_Loop", c_stand_sign, "stand"),
    ("Stand_Chant_Loop", c_stand_chant, "stand"), ("Stand_Boo_Loop", c_stand_boo, "stand"),
    ("Rail_Pound_Loop", c_rail_pound, "stand"), ("Rail_Reach_Loop", c_rail_reach, "stand"),
]
PHASES = (0.0, 0.5)

def reach_report(rig, name, fn):
    """Найгірший недотяг IK за кліп (см): ціль зап'ястя/щиколотки проти кістки."""
    pb = rig.arm.pose.bones
    worst = (0.0, "")
    for f in range(0, int(L * FPS) + 1, 3):
        apply(rig, fn(f / FPS, L))
        for s in "lr":
            for e, b in (("wrist_" + s, "hand_" + s), ("ankle_" + s, "foot_" + s)):
                d = (rig.empties[e].location - rig.arm.matrix_world @ pb[b].head).length
                if d > worst[0]:
                    worst = (d, f"{b}, кадр {f}")
    return worst[0] * 100.0, worst[1]

def build_anims():
    arm = load_rig()
    rig = Rig(arm)
    G["sit_dz"] = SIT_PELVIS_Z - head_rest(arm, "pelvis").z
    report = []
    samples = []
    for name, fn, kind in CLIPS_C:
        cm, where = reach_report(rig, name, fn)
        report.append((name, cm, where))
        samples.append((name, bake(rig, name, L, fn)))
    strip_constraints(rig)
    actions = [write_action(arm, name, s) for name, s in samples]
    global OUT_FBX
    OUT_FBX = ANIM_FBX
    export(arm, actions)
    for name, cm, where in report:
        print("[натовп] %-22s IK не дістав %.1f см (%s)%s" % (name, cm, where, "  ← ПЕРЕВІРИТИ" if cm > 3.0 else ""), flush=True)
    names = [a.name for a in actions]
    return arm, {a.name: a for a in actions}, report

# ---------------------------------------------------------------- фанати

# Фанати збираються MPFB (як персонажі Alpha — tools/blender/alpha_people.py): тіло-проксі proxy741 (легке, для
# натовпу), шкіра за расою й віком, сучасний одяг і зачіски з бібліотеки MakeHuman — лише CC0 (перевірка ліцензії
# кожного асета — asset_license з alpha_people.py; AGPL і CC BY відкидаються). Риг — game_engine, той самий, що
# в кліпів натовпу.
_people = open(os.path.join(REPO, "tools", "blender", "alpha_people.py"), encoding="utf-8").read()
exec(_people.split("# ---------------------------------------------------------------- кольори епохи")[0])

CULTURES = {"ukrainian": (0.0, 0.0, 1.0), "nordic": (0.0, 0.0, 1.0), "mediterranean": (0.05, 0.05, 0.9),
            "middle_eastern": (0.1, 0.15, 0.75), "west_african": (1.0, 0.0, 0.0), "east_asian": (0.0, 1.0, 0.0),
            "south_asian": (0.3, 0.35, 0.35), "latin": (0.2, 0.15, 0.65)}         # частки (африканська, азійська, європейська)
HAIR = {"m": ("short01", "short02", "short03", "short04", "cortu_short_messy_hair", "faydaen_hair_1", "afro01",
              "cortu_straight_bangs", None),
        "f": ("long01", "ponytail01", "bob01", "bob02", "braid01", "toigo_blunt_bob", "toigo_curled_under_bob",
              "culturalibre_hair_05", "culturalibre_hair_06", "afro01", "toigo_inverted_bob_with_bangs")}
# Вбрання: (верх, низ, взуття). Повний комплект (casualsuit) — одна сітка; футболки — перефарбовуються з екземпляра
# (мерч промоушену: найчастіше чорні), комплекти лишають свої кольори.
OUTFITS = {"m": [("male_casualsuit01",), ("male_casualsuit02",), ("male_casualsuit03",), ("male_casualsuit04",),
                 ("male_casualsuit05",), ("male_casualsuit06",), ("elvs_crude_t-shirt_male", "cortu_cargo_pants"),
                 ("toigo_basic_tucked_t-shirt", "cortu_cargo_pants"), ("namuhekam_male_polo_shirt", "toigo_wool_pants"),
                 ("elvs_crude_t-shirt_male", "cortu_jeans_shorts"), ("toigo_fisherman_sweater", "cortu_cargo_pants")],
           "f": [("female_casualsuit01",), ("female_casualsuit02",), ("female_sportsuit01",),
                 ("joepal_crude_t-shirt_female", "cortu_cargo_pants"), ("joepal_crude_t-shirt_female", "cortu_jeans_shorts"),
                 ("toigo_keyhole_tank_top", "cortu_cargo_pants"), ("toigo_turtleneck_halter_top", "toigo_wool_pants"),
                 ("toigo_basic_tucked_t-shirt", "cortu_jeans_shorts")]}
SHOES = ("shoes01", "shoes02", "shoes03", "shoes04", "shoes05", "shoes06")
RECOLOR_TOPS = {"elvs_crude_t-shirt_male", "toigo_basic_tucked_t-shirt", "joepal_crude_t-shirt_female",
                "namuhekam_male_polo_shirt", "toigo_keyhole_tank_top"}
TRIS = {"proxy": 1500, "suit": 2400, "top": 1200, "bottom": 1000, "shoes": 1000, "hair": 1000}   # шкарпетки й гомілки не губляться
N_FANS = 32

def fan_specs():
    """32 фанати: стать навпереміну, 8 культур, вік від підлітка до 60+, різна статура; детерміновано."""
    out = []
    names = list(CULTURES)
    for i in range(N_FANS):
        sex = "m" if i % 2 == 0 else "f"
        culture = names[(i // 2) % len(names)]
        tag = "fan_%s_%s_%d" % (sex, culture, i // 16)
        race = CULTURES[culture]
        age = 0.32 + 0.6 * h01(tag, 11) ** 1.4
        dom = max(range(3), key=lambda k: race[k])
        rname = ("african", "asian", "caucasian")[dom]
        stage = "young" if age < 0.55 else "middleage" if age < 0.8 else "old"
        skin = "%s_%s_%s" % (stage, rname, "male" if sex == "m" else "female")
        outfit = OUTFITS[sex][int(h01(tag, 12) * len(OUTFITS[sex])) % len(OUTFITS[sex])]
        hair = HAIR[sex][int(h01(tag, 13) * len(HAIR[sex])) % len(HAIR[sex])]
        if culture == "west_african" and h01(tag, 14) < 0.5:
            hair = "afro01"
        if i == 21:
            hair = "cortu_shaggy_green_hair"                          # один фрик із зеленим волоссям — реслінг-публіка
        spec = dict(g=1.0 if sex == "m" else 0.0, age=age, mus=0.3 + 0.4 * h01(tag, 15), w=0.3 + 0.5 * h01(tag, 16),
                    h=0.3 + 0.5 * h01(tag, 17), race=race, skin=skin, proxy="proxy741",
                    clothes=list(outfit) + [SHOES[int(h01(tag, 18) * len(SHOES)) % len(SHOES)]], hair=hair,
                    brows="eyebrow%03d" % (1 + int(h01(tag, 19) * 12) % 12), cap=h01(tag, 20) < 0.18)
        out.append((tag, spec))
    return out

def fan_materials():
    """Fan_Cap — кепка (колір за випадковим числом об'єкта); верх-футболки перефарбовуються у recolor_top()."""
    m = bpy.data.materials.get("Fan_Cap") or bpy.data.materials.new("Fan_Cap")
    m.use_fake_user = True                     # без користувачів MPFB прибирає «сиріт»
    m.use_nodes = True
    nt = m.node_tree
    b = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    oi = nt.nodes.new("ShaderNodeObjectInfo")
    cr = nt.nodes.new("ShaderNodeValToRGB")
    cr.color_ramp.interpolation = 'CONSTANT'
    pal = [(0.04, 0.04, 0.05), (0.5, 0.05, 0.06), (0.07, 0.12, 0.35), (0.8, 0.8, 0.78), (0.55, 0.42, 0.12)]
    els = cr.color_ramp.elements
    while len(els) < len(pal):
        els.new(0.5)
    for i, c in enumerate(pal):
        els[i].position = i / len(pal)
        els[i].color = (*c, 1)
    fr = nt.nodes.new("ShaderNodeMath")
    fr.operation = 'FRACT'
    mu = nt.nodes.new("ShaderNodeMath")
    mu.operation = 'MULTIPLY'
    mu.inputs[1].default_value = 11.0
    nt.links.new(oi.outputs["Random"], mu.inputs[0])
    nt.links.new(mu.outputs[0], fr.inputs[0])
    nt.links.new(fr.outputs[0], cr.inputs[0])
    nt.links.new(cr.outputs[0], b.inputs["Base Color"])
    b.inputs["Roughness"].default_value = 0.8

def recolor_top(m):
    """Футболка/поло: знебарвлена текстура × колір об'єкта (екземпляра) / середня яскравість — колір дає гра."""
    nt = m.node_tree
    b = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    t = next((l.from_node for l in nt.links if l.to_node == b and l.to_socket.name == "Base Color" and l.from_node.type == 'TEX_IMAGE'), None)
    if t is None:
        t = next((n for n in nt.nodes if n.type == 'TEX_IMAGE'), None)
    bw = nt.nodes.new("ShaderNodeRGBToBW")
    oi = nt.nodes.new("ShaderNodeObjectInfo")
    mul = nt.nodes.new("ShaderNodeMix")
    mul.data_type = 'RGBA'
    mul.blend_type = 'MULTIPLY'
    mul.inputs["Factor"].default_value = 1.0
    sc = nt.nodes.new("ShaderNodeMath")
    sc.operation = 'MULTIPLY'
    sc.inputs[1].default_value = 1.6
    if t is not None:
        nt.links.new(t.outputs["Color"], bw.inputs[0])
        nt.links.new(bw.outputs[0], sc.inputs[0])
        nt.links.new(sc.outputs[0], mul.inputs[6])
    nt.links.new(oi.outputs["Color"], mul.inputs[7])
    nt.links.new(mul.outputs[2], b.inputs["Base Color"])
    m["recolor"] = True

def decimate_to(o, tris_target):
    tris = sum(len(p.vertices) - 2 for p in o.data.polygons)
    if tris <= tris_target:
        return
    d = o.modifiers.new("dec", 'DECIMATE')
    d.ratio = tris_target / tris
    o.modifiers.move(len(o.modifiers) - 1, 0)
    with bpy.context.temp_override(object=o, active_object=o):
        bpy.ops.object.modifier_apply(modifier="dec")

def apply_masks(o):
    """Маски MPFB (тіло під одягом не малюється) — запекти в сітку: менше трикутників і жодного «просвічування»."""
    for md in [md for md in o.modifiers if md.type == 'MASK']:
        o.modifiers.move(o.modifiers.find(md.name), 0)
        with bpy.context.temp_override(object=o, active_object=o):
            bpy.ops.object.modifier_apply(modifier=md.name)

def make_cap(arm, head_obj):
    """Бейсболка: купол і козирок, жорстко на кістці head (у бібліотеці CC0 бейсболки немає)."""
    bm = bmesh.new()
    pb = arm.pose.bones["head"]
    hh = arm.matrix_world @ pb.head
    bb = [head_obj.matrix_world @ Vector(c) for c in head_obj.bound_box]
    near = [v for v in bb]
    zmax = max(v.z for v in near)
    cx, cy = hh.x, hh.y
    rx, ry = 0.095, 0.105
    res = bmesh.ops.create_uvsphere(bm, u_segments=14, v_segments=7, radius=1.0)
    for v in res["verts"]:
        if v.co.z < 0:
            v.co.z = 0
        v.co = Vector((cx + v.co.x * rx, cy + 0.01 + v.co.y * ry, zmax - 0.085 + v.co.z * 0.1))
    br = bmesh.ops.create_circle(bm, cap_ends=True, segments=12, radius=1.0)
    for v in br["verts"]:
        y = v.co.y
        v.co = Vector((cx + v.co.x * rx * 0.85, cy - ry * 0.55 + min(0.0, y) * 0.11, zmax - 0.08))
    me = bpy.data.meshes.new("cap")
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new("cap", me)
    bpy.context.scene.collection.objects.link(ob)
    me.materials.append(bpy.data.materials["Fan_Cap"])
    vg = ob.vertex_groups.new(name="head")
    vg.add(list(range(len(me.vertices))), 1.0, 'REPLACE')
    ob.parent = arm
    ob.matrix_parent_inverse = arm.matrix_world.inverted()
    md = ob.modifiers.new("Armature", 'ARMATURE')
    md.object = arm
    return ob

def join(objs, name):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.hide_set(False)
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    with bpy.context.temp_override(active_object=objs[0], selected_editable_objects=objs, selected_objects=objs):
        bpy.ops.object.join()
    objs[0].name = name
    objs[0].data.name = name
    return objs[0]

def build_fan(tag, spec):
    """Фанат MPFB: (арматура, LOD0, LOD1)."""
    bald = spec["hair"] is None
    rig = make_person(tag, dict(spec, hair=spec["hair"] or "short04"))       # лисий: зачіску MPFB знімаємо одразу
    if bald:
        for o in list(rig.children):
            if o.name.endswith(".short04"):
                bpy.data.objects.remove(o, do_unlink=True)
    base = next(o for o in rig.children if o.name.endswith(".basemesh"))
    skin = base.data.materials[0] if base.data.materials else None
    keep = []
    for o in list(rig.children):
        if o.type != 'MESH' or o is base:
            continue
        apply_masks(o)
        short = o.name.split(".", 1)[-1]
        if short.startswith("proxy"):
            if skin is not None:
                o.data.materials.clear()
                o.data.materials.append(skin)
            decimate_to(o, TRIS["proxy"])
        elif short in RECOLOR_TOPS:
            for m in o.data.materials:
                recolor_top(m)
            decimate_to(o, TRIS["top"])
        elif short.startswith(("male_casualsuit", "female_casualsuit", "female_sportsuit")):
            decimate_to(o, TRIS["suit"])
        elif short.startswith("shoes"):
            decimate_to(o, TRIS["shoes"])
        elif short.startswith(("cortu_cargo", "cortu_jeans", "toigo_wool", "toigo_harem")):
            decimate_to(o, TRIS["bottom"])
        elif short.startswith(("toigo_", "joepal", "elvs", "namuhekam")):
            decimate_to(o, TRIS["top"])
        elif short.startswith(("eyebrow", "low-poly")):
            pass
        else:
            decimate_to(o, TRIS["hair"])                              # зачіска
            for m in o.data.materials:                                # без блиску карти «spec»: зблизька пасма біліли
                if m and m.use_nodes:
                    bs = next((n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
                    if bs:
                        for l in list(bs.inputs["Roughness"].links):
                            m.node_tree.links.remove(l)
                        bs.inputs["Roughness"].default_value = 0.7
                        if "Specular IOR Level" in bs.inputs:
                            for l in list(bs.inputs["Specular IOR Level"].links):
                                m.node_tree.links.remove(l)
                            bs.inputs["Specular IOR Level"].default_value = 0.25
        keep.append(o)
    if spec.get("cap"):
        head = next((o for o in keep if o.name.split(".", 1)[-1].startswith("proxy")), keep[0])
        keep.append(make_cap(rig, head))
    bpy.data.objects.remove(base, do_unlink=True)
    lod0 = join(keep, tag + "_LOD0")
    lod1 = lod0.copy()
    lod1.data = lod0.data.copy()
    lod1.name = tag + "_LOD1"
    lod1.data.name = tag + "_LOD1"
    bpy.context.scene.collection.objects.link(lod1)
    decimate_to(lod1, 900)
    rig.name = tag
    for pb in rig.pose.bones:
        pb.rotation_mode = 'QUATERNION'
    return rig, lod0, lod1

def set_action(arm, action):
    ad = arm.animation_data_create()
    ad.action = action
    if action is not None and hasattr(ad, "action_slot") and getattr(action, "slots", None):
        ad.action_slot = action.slots[0]

def bake_pose(arm, mesh_obj, action, frame, kind, name):
    """Поза кадру → нерухома сітка; сидячі — таз на висоті SIT_PELVIS_Z, стоячі — стопи на підлозі."""
    set_action(arm, action)
    bpy.context.scene.frame_set(int(frame))
    dg = bpy.context.evaluated_depsgraph_get()
    ev = mesh_obj.evaluated_get(dg)
    me = bpy.data.meshes.new_from_object(ev, preserve_all_data_layers=False, depsgraph=dg)
    me.transform(mesh_obj.matrix_world)
    if kind == "sit":
        pz = (arm.matrix_world @ arm.pose.bones["pelvis"].head).z
        dz = SIT_PELVIS_Z - pz
        low = min(v.co.z for v in me.vertices) + dz          # довгі ноги: стопи не в підлогу — таз трохи вище
        if low < 0.004:
            dz += 0.004 - low
    else:
        dz = -min(v.co.z for v in me.vertices) + 0.005
    me.transform(Matrix.Translation((0, 0, dz)))
    me.name = name
    return me

def add_props(me, arm, clip):
    """Телефон у правій руці (Sit_Phone) і табличка над головою (Stand_Sign) — частина запеченої сітки."""
    if clip.startswith("Cam_"):
        add_camera(me, arm, clip)
        return
    if clip not in ("Sit_Phone_Loop", "Stand_Sign_Loop"):
        return
    pb = arm.pose.bones
    W = arm.matrix_world
    bm = bmesh.new()
    bm.from_mesh(me)
    if clip == "Sit_Phone_Loop":
        hd = W @ pb["hand_r"].head
        tip = W @ pb["middle_01_r"].head
        f = (tip - hd).normalized()
        c = hd + f * 0.09 + Vector((0, -0.02, 0))
        r = bmesh.ops.create_cube(bm, size=1.0)
        bmesh.ops.scale(bm, vec=(0.07, 0.01, 0.14), verts=r["verts"])
        bmesh.ops.translate(bm, vec=c, verts=r["verts"])
        for v in r["verts"]:
            for fc in v.link_faces:
                fc.material_index = len(me.materials)
        mat = bpy.data.materials.get("Fan_Phone") or bpy.data.materials.new("Fan_Phone")
    else:
        a, b = W @ pb["hand_l"].head, W @ pb["hand_r"].head
        mid = (a + b) / 2
        right = (a - b)
        right.z = 0
        w = max(0.55, right.length + 0.12)
        right = right.normalized() * (w / 2)
        up = Vector((0, 0, 0.22))
        c = mid + Vector((0, 0.0, 0.16))
        vs = [bm.verts.new(c - right - up), bm.verts.new(c + right - up), bm.verts.new(c + right + up), bm.verts.new(c - right + up)]
        fc = bm.faces.new(vs)
        uv = bm.loops.layers.uv.verify()
        for loop, tt in zip(fc.loops, ((1, 0), (0, 0), (0, 1), (1, 1))):
            loop[uv].uv = tt
        fc.material_index = len(me.materials)
        fc.normal_update()
        vs2 = [bm.verts.new(v.co - fc.normal * 0.004) for v in reversed(vs)]
        fc2 = bm.faces.new(vs2)
        for loop, tt in zip(fc2.loops, ((0, 1), (1, 1), (1, 0), (0, 0))):
            loop[uv].uv = tt
        fc2.material_index = len(me.materials)
        mat = bpy.data.materials.get("Fan_Sign") or sign_material()
    bm.to_mesh(me)
    bm.free()
    me.materials.append(mat)

def add_camera(me, arm, clip):
    """Недорога камера в руках: бездзеркалка (корпус, об'єктив, бленда, монітор) на ручному стабілізаторі
    (рукоятка, кільце, підвіс) — розташована між кистями, дивиться вперед постаті."""
    pb, W = arm.pose.bones, arm.matrix_world
    a, b = W @ pb["hand_l"].head, W @ pb["hand_r"].head
    mid = (a + b) / 2
    fwd = (W.to_3x3() @ Vector((0, -1, 0))).normalized()
    right = fwd.cross(Vector((0, 0, 1))).normalized()
    bm = bmesh.new()
    bm.from_mesh(me)
    k0 = len(me.materials)
    def box(c, s, mat_i):
        r = bmesh.ops.create_cube(bm, size=1.0)
        M = Matrix((right, fwd, Vector((0, 0, 1)))).transposed()
        for v in r["verts"]:
            v.co = c + M @ Vector((v.co.x * s[0], v.co.y * s[1], v.co.z * s[2]))
        for f in {f for v in r["verts"] for f in v.link_faces}:
            f.material_index = k0 + mat_i
    def cyl(p0, p1, rad, mat_i, segs=10):
        d = p1 - p0
        r = bmesh.ops.create_cone(bm, cap_ends=True, segments=segs, radius1=rad, radius2=rad, depth=d.length)
        bmesh.ops.transform(bm, matrix=d.to_track_quat('Z', 'Y').to_matrix().to_4x4(), verts=r["verts"])
        bmesh.ops.translate(bm, vec=p0 + d / 2, verts=r["verts"])
        for f in {f for v in r["verts"] for f in v.link_faces}:
            f.material_index = k0 + mat_i
    up = Vector((0, 0, 1))
    if clip == "Cam_Walk_Gimbal_Loop":
        cyl(mid - up * 0.04, mid + up * 0.12, 0.018, 0)                # рукоятка
        cyl(mid + up * 0.12, mid + up * 0.2 + fwd * 0.05, 0.012, 0)    # підвіс
        cam = mid + up * 0.24 + fwd * 0.08
    else:
        cam = mid + up * 0.06 + fwd * 0.05
    box(cam, (0.13, 0.08, 0.09), 0)
    cyl(cam + fwd * 0.04, cam + fwd * 0.13, 0.034, 1, 14)
    cyl(cam + fwd * 0.13, cam + fwd * 0.16, 0.042, 0, 14)
    box(cam - fwd * 0.045 + up * 0.0, (0.07, 0.006, 0.05), 2)          # монітор ззаду
    bm.to_mesh(me)
    bm.free()
    for nm, col, em in (("Cam_Body", (0.04, 0.04, 0.045), 0.0), ("Cam_Glass", (0.02, 0.02, 0.03), 0.0),
                        ("Cam_Screen", (0.3, 0.5, 0.8), 1.5)):
        m = bpy.data.materials.get(nm)
        if m is None:
            m = bpy.data.materials.new(nm)
            m.use_nodes = True
            bs = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
            bs.inputs["Base Color"].default_value = (*col, 1)
            bs.inputs["Roughness"].default_value = 0.35
            if em:
                bs.inputs["Emission Strength"].default_value = em
                for key in ("Emission Color", "Emission"):
                    if key in bs.inputs:
                        bs.inputs[key].default_value = (*col, 1)
                        break
        me.materials.append(m)

def sign_material():
    m = bpy.data.materials.new("Fan_Sign")
    m.use_nodes = True
    nt = m.node_tree
    b = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    p = os.path.join(REPO, "Assets", "Art", "Ring", "Textures", "decal_poster_1.png")
    if os.path.exists(p):
        t = nt.nodes.new("ShaderNodeTexImage")
        t.image = bpy.data.images.load(p, check_existing=True)
        nt.links.new(t.outputs["Color"], b.inputs["Base Color"])
    return m

def gather_textures(fans):
    """Текстури фанатів (CC0-бібліотека MakeHuman у теці MPFB, поза репозиторієм) — копія в Assets/Art/Crowd/Textures,
    зменшена до 1024 (Android), шляхи в матеріалах — на копії: FBX посилається на файли репозиторію."""
    out = os.path.join(OUT_DIR, "Textures")
    os.makedirs(out, exist_ok=True)
    seen = {}
    for arm, l0, l1 in fans:
        for m in l0.data.materials:
            if not m or not m.use_nodes:
                continue
            for n in m.node_tree.nodes:
                if n.type != 'TEX_IMAGE' or not n.image or n.image.name in seen:
                    continue
                img = n.image
                src = bpy.path.abspath(img.filepath)
                if os.path.abspath(src).startswith(os.path.abspath(REPO)):
                    continue
                name = os.path.splitext(os.path.basename(src))[0] + ".png"
                dst = os.path.join(out, name)
                try:
                    if max(img.size) > 1024:
                        img.scale(1024, 1024 * img.size[1] // max(1, img.size[0]))
                    img.filepath_raw = dst
                    img.file_format = 'PNG'
                    img.save()
                    seen[img.name] = (src, dst)
                except RuntimeError as e:
                    print("[натовп] текстура %s: %s" % (src, e), flush=True)
    with open(os.path.join(out, "SOURCES.txt"), "w", encoding="utf-8", newline="\n") as f:
        f.write("# Текстури фанатів: бібліотека MakeHuman (MPFB), лише CC0 (asset_license у tools/blender/alpha_people.py).\n")
        for k, (src, dst) in sorted(seen.items()):
            f.write("%s <- %s\n" % (os.path.basename(dst), src.split("mpfb")[-1].replace("\\", "/")))
    print("[натовп] текстур скопійовано: %d" % len(seen), flush=True)

def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    os.makedirs(LOOK_DIR, exist_ok=True)
    arm_anim, actions, report = build_anims()
    for o in list(bpy.data.objects):                 # риг анімацій більше не потрібен; дії лишаються
        bpy.data.objects.remove(o, do_unlink=True)
    fan_materials()
    acts = dict(actions)
    fans = []
    for tag, spec in fan_specs():
        arm, l0, l1 = build_fan(tag, spec)
        fans.append((arm, l0, l1))
        print("[натовп] %s: %s, LOD0 %d трик." % (tag, " + ".join(spec["clothes"]),
                                                  sum(len(q.vertices) - 2 for q in l0.data.polygons)), flush=True)
    # бібліотека поз: фанат × кліп × фаза, обидва рівні
    lib = []
    for arm, l0, l1 in fans:
        for name, fn, kind in CLIPS_C:
            act = next((a for k_, a in acts.items() if k_.endswith(name)), None)
            if act is None:
                continue
            n = int(act.frame_range[1])
            for k, ph in enumerate(PHASES):
                f = round(ph * n)
                for lvl, ob in ((0, l0), (1, l1)):
                    me = bake_pose(arm, ob, act, f, kind, "pose_%s_%s_%d_L%d" % (arm.name, name, k, lvl))
                    add_props(me, arm, name)
                    me.use_fake_user = True
                    lib.append(me)
        set_action(arm, None)
    print("[натовп] фанатів %d, запечених поз %d" % (len(fans), len(lib)), flush=True)
    # експорт фанатів у гру (скелети + LOD0/LOD1, поза спокою)
    for arm, l0, l1 in fans:
        for pb in arm.pose.bones:
            pb.rotation_quaternion = Quaternion()
            pb.location = Vector()
    bpy.context.view_layer.update()
    gather_textures(fans)
    bpy.ops.object.select_all(action='DESELECT')
    for arm, l0, l1 in fans:
        for o in (arm, l0, l1):
            o.select_set(True)
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT_DIR, "crowd_fans.fbx"), use_selection=True, object_types={'ARMATURE', 'MESH'},
                             apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y',
                             add_leaf_bones=False, primary_bone_axis='Y', secondary_bone_axis='X', bake_anim=False,
                             path_mode='RELATIVE', mesh_smooth_type='FACE')
    with open(os.path.join(OUT_DIR, "crowd_manifest.txt"), "w", encoding="utf-8", newline="\n") as f:
        f.write("# Натовп (tools/blender/ring_crowd.py). fan <ім'я> <LOD0 трик.> <LOD1 трик.>; clip <ім'я> <сидячи|стоячи>\n")
        for arm, l0, l1 in fans:
            f.write("fan %s %d %d\n" % (arm.name, sum(len(p.vertices) - 2 for p in l0.data.polygons),
                                        sum(len(p.vertices) - 2 for p in l1.data.polygons)))
        for name, fn, kind in CLIPS_C:
            cm = next(c for n_, c, w in report if n_ == name)
            f.write("clip %s %s ik_cm %.1f\n" % (name, "сидячи" if kind == "sit" else "стоячи", cm))
    if LOOK:
        look_crowd(fans, actions_named=acts)
    # бібліотека для знімків арен: запечені пози + скелети фанатів (колекція FanRigs — для відео з живим натовпом)
    rigs = bpy.data.collections.new("FanRigs")
    rigs.use_fake_user = True
    keep = set()
    for arm, l0, l1 in fans:
        for o in (arm, l0, l1):
            for c in list(o.users_collection):
                c.objects.unlink(o)
            rigs.objects.link(o)
            keep.add(o.name)
    for o in list(bpy.data.objects):
        if o.name not in keep:
            bpy.data.objects.remove(o, do_unlink=True)
    os.makedirs(os.path.dirname(LIB_BLEND), exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=LIB_BLEND, compress=True)
    print("[натовп] бібліотека поз → %s" % LIB_BLEND, flush=True)

def look_crowd(fans, actions_named):
    """Контактні листи: шеренга всіх фанатів (Stand_Idle) і всі кліпи на одному сидячому/стоячому фанаті."""
    scene = bpy.context.scene
    try:
        scene.render.engine = 'BLENDER_EEVEE_NEXT'
    except TypeError:
        scene.render.engine = 'BLENDER_EEVEE'
    w = bpy.data.worlds.new("w")
    w.use_nodes = True
    next(n for n in w.node_tree.nodes if n.type == "BACKGROUND").inputs[1].default_value = 1.0
    scene.world = w
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", 'SUN'))
    sun.data.energy = 3.0
    sun.rotation_euler = (math.radians(50), 0, math.radians(30))
    scene.collection.objects.link(sun)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    scene.render.resolution_x, scene.render.resolution_y = 1800, 700
    col = bpy.data.collections.new("look")
    scene.collection.children.link(col)
    obs = []
    for i, (arm, l0, l1) in enumerate(fans):
        me = bpy.data.meshes.get("pose_%s_Stand_Idle_Loop_0_L0" % arm.name)
        o = bpy.data.objects.new("lk%d" % i, me)
        col.objects.link(o)
        o.location = ((i % 16) * 0.7 - 5.25, (i // 16) * 1.2, 0)
        o.color = (*[(0.05, 0.05, 0.06), (0.6, 0.08, 0.08), (0.9, 0.9, 0.88), (0.1, 0.2, 0.5)][i % 4], 1)
        obs.append(o)
    for arm, l0, l1 in fans:
        for o in (arm, l0, l1):
            o.hide_render = True
    cam.location = (0, -9.5, 1.6)
    cam.rotation_euler = (math.radians(88), 0, 0)
    cam.data.lens = 30
    scene.render.filepath = os.path.join(LOOK_DIR, "fans_lineup.png")
    bpy.ops.render.render(write_still=True)
    for o in obs:
        bpy.data.objects.remove(o, do_unlink=True)
    # кліпи: фанат 0, фаза 0 — по одному кадру на кліп, сидячі на стільцях-коробках
    arm0 = fans[0][0].name
    for i, (name, fn, kind) in enumerate(CLIPS_C):
        me = bpy.data.meshes.get("pose_%s_%s_0_L0" % (arm0, name))
        o = bpy.data.objects.new("cl%d" % i, me)
        col.objects.link(o)
        o.location = (i * 0.75 - 6.4, 0, 0)
        o.color = (0.6, 0.08, 0.08, 1)
        if kind == "sit":
            bm = bmesh.new()
            bmesh.ops.create_cube(bm, size=1.0)
            bmesh.ops.scale(bm, vec=(0.42, 0.4, SEAT_Z), verts=list(bm.verts))
            bmesh.ops.translate(bm, vec=(i * 0.75 - 6.4, 0, SEAT_Z / 2), verts=list(bm.verts))
            m2 = bpy.data.meshes.new("seatbox")
            bm.to_mesh(m2)
            bm.free()
            s2 = bpy.data.objects.new("sb%d" % i, m2)
            col.objects.link(s2)
    for view, loc, rot in (("front", (0, -10.0, 1.2), (math.radians(88), 0, 0)),
                           ("side", (-12.0, -6.0, 1.6), (math.radians(84), 0, math.radians(-63)))):
        cam.location = loc
        cam.rotation_euler = rot
        scene.render.filepath = os.path.join(LOOK_DIR, "clips_%s.png" % view)
        bpy.ops.render.render(write_still=True)
    print("[натовп] огляд → %s" % LOOK_DIR, flush=True)

main()
