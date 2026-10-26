# Анімації рингу (етап 1, docs/TRPG_DESIGN.md §11.2): власна генерація, парні прийоми на двох постатях одразу.
#
#   "C:\Program Files\Blender Foundation\Blender 4.5\blender.exe" -b --python tools/blender/ring_anims.py -- <репо> [--look] [--only Ім'я,…]
#
# Основа — tools/blender/alpha_anims.py (пози кодом, IK, запікання у FK). Тут додано:
#   * дві постаті (A — нападник у нулі, лицем у −Y; B — навпроти, лицем до A) у спільній сцені;
#   * контакти: ціль руки однієї постаті — кістка другої (розв'язання в кілька проходів, щоб обидві сходились);
#   * повний поворот таза (падіння на спину, підйом над головою), вільні щиколотки (лежачи, навколішки);
#   * звіт якості на кожен кліп: ковзання опорної стопи, найбільше взаємне проникнення тіл, найгірше зап'ястя;
#   * --look — контактні листи (дві постаті, збоку й на три чверті) у Logs/ring_anims.
# Кліпи: «<Прийом>_A» і «<Прийом>_B» — для пари, «<Рух>» — для одного. Усі — один FBX
# Assets/Art/Animations/RING_Moves.fbx; зсув і поворот B відносно A на старті — у ring_anims_manifest.txt.
# Референси — будова прийомів реального реслінгу; жодних кривих чужих ігор (docs/TRPG_DECISIONS.md, 26.10.2026).
import bpy, math, os, sys
from mathutils import Vector, Matrix, Quaternion

_args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
REPO = _args[0] if _args else os.getcwd()
_src = open(os.path.join(REPO, "tools", "blender", "alpha_anims.py"), encoding="utf-8").read()
exec(_src.rsplit("\nmain()", 1)[0])          # Rig, Pose, track, smooth, lerp, breathe, write_action, … без запуску

LOOK = "--look" in _args
ONLY = None
if "--only" in _args:
    ONLY = set(_args[_args.index("--only") + 1].split(","))
OUT_FBX = os.path.join(REPO, "Assets", "Art", "Animations", "RING_Moves.fbx")
MANIFEST = os.path.join(REPO, "Assets", "Art", "Animations", "ring_anims_manifest.txt")
LOOK_DIR = os.path.join(REPO, "Logs", "ring_anims")

GAP = 0.62          # відстань між тазами в зчепі (м)

# ---------------------------------------------------------------- дві постаті

def load_pair():
    bpy.ops.wm.read_homefile(use_empty=True)
    arms = []
    for tag in ("A", "B"):
        before = set(bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=os.path.join(REPO, "Assets", "Art", "Characters", "Kit", "kit_m.fbx"),
                                 primary_bone_axis='Y', secondary_bone_axis='X', automatic_bone_orientation=False,
                                 ignore_leaf_bones=False)
        new = [o for o in bpy.data.objects if o not in before]
        arm = next(o for o in new if o.type == 'ARMATURE')
        arm.name = "Arm_" + tag
        for o in new:
            o["pair"] = tag
            if o.type == 'MESH':
                o.hide_viewport = True
        for pb in arm.pose.bones:
            pb.rotation_mode = 'QUATERNION'
        arms.append(arm)
    bpy.context.view_layer.objects.active = arms[0]
    return arms

def place(arm, offset, yaw_deg):
    arm.matrix_world = Matrix.Translation(offset) @ Matrix.Rotation(math.radians(yaw_deg), 4, 'Z')
    bpy.context.view_layer.update()

# ---------------------------------------------------------------- поза у світі постаті

class RPose(Pose):
    """Поза з повним поворотом таза (pelvis_q — поверх yaw/pitch, у просторі ригу)."""
    def __init__(self, **kw):
        self.pelvis_q = None
        super().__init__(**kw)

def apply_w(rig, p, partner=None):
    """apply() з alpha_anims, але для ригу будь-де у світі: цілі IK задаються в просторі ригу й переводяться у світ.
    partner — словник «кістка → голова кістки партнера» у просторі ЦЬОГО ригу (для контактів)."""
    arm, pb = rig.arm, rig.arm.pose.bones
    W = arm.matrix_world.copy()
    Wq = W.to_quaternion()
    rig.reset()

    def local_rot(bone, axis_world, deg):
        ax = (rest(arm, bone).to_3x3().inverted() @ axis_world).normalized()
        return Quaternion(ax, math.radians(deg))

    pel = pb["pelvis"]
    pel.location = rest(arm, "pelvis").to_3x3().inverted() @ p.pelvis
    # Нахил (pitch) у цьому скелеті: обертання навколо LEFT з додатним кутом — уперед (замір 26.10.2026: з RIGHT
    # «стійка» відкидалась назад — голова на 9 см позаду таза). Те саме для голови й таза.
    q = local_rot("pelvis", UP, p.pelvis_yaw) @ local_rot("pelvis", LEFT, p.pelvis_pitch)
    if p.pelvis_q is not None:
        r3 = rest(arm, "pelvis").to_3x3()
        q = (r3.inverted() @ p.pelvis_q.to_matrix() @ r3).to_quaternion() @ q
    pel.rotation_quaternion = q
    for bone, w in (("spine_01", 0.3), ("spine_02", 0.3), ("spine_03", 0.4)):
        pb[bone].rotation_quaternion = (local_rot(bone, UP, p.yaw * w) @ local_rot(bone, LEFT, p.pitch * w)
                                        @ local_rot(bone, FWD, -p.roll * w))
    total_yaw = p.pelvis_yaw + p.yaw
    hy = -total_yaw if p.head_yaw is None else p.head_yaw - total_yaw
    hp = p.head_pitch - p.pitch * 0.7 - p.pelvis_pitch
    for bone, w in (("neck_01", 0.45), ("head", 0.55)):
        if bone in pb:
            pb[bone].rotation_quaternion = local_rot(bone, UP, hy * w) @ local_rot(bone, LEFT, hp * w)
    for name, ax in rig.finger_axis.items():
        side = name[-1]
        k = name.split("_")[1]
        amount = p.fist[side] * (70 if k == "01" else 85 if k == "02" else 60)
        if name.startswith("thumb"):
            amount *= 0.5
        pb[name].rotation_quaternion = Quaternion(ax, math.radians(amount))
    bpy.context.view_layer.update()

    half, split = p.feet
    yl, yr = math.radians(p.feet_yaw[0]), math.radians(p.feet_yaw[1])
    ank = p.ankles or (Vector((half, -split / 2 - 0.015, 0.071)), Vector((-half, split / 2 - 0.015, 0.071)))
    rots = p.foot_rot or (Quaternion(UP, yl), Quaternion(UP, yr))
    for s, a, qf in (("l", ank[0], rots[0]), ("r", ank[1], rots[1])):
        e = rig.empties["ankle_" + s]
        e.location = W @ a
        e.rotation_quaternion = Wq @ qf @ rest(arm, "foot_" + s).to_quaternion()
        knee = (p.knees[0 if s == "l" else 1] if p.knees else
                a + Vector((0.08 if s == "l" else -0.08, -0.9, 0.45)))
        rig.empties["knee_" + s].location = W @ knee

    S = {s: pb["upperarm_" + s].head.copy() for s in "lr"}
    S["head"] = pb["head"].head.copy() if "head" in pb else pb["neck_01"].tail.copy()
    S["chest"] = pb["spine_03"].head.copy()
    S["pelvis"] = pb["pelvis"].head.copy()
    S["P"] = partner or {}
    straight = {}
    for s in "lr":
        rig.iks["hand_" + s].influence = 1.0 if (s in p.hands and s in p.forced) else 0.0
        if s in p.hands:
            grip, fore, up = p.hands[s]
            grip = grip(S) if callable(grip) else grip
            fore = fore(S) if callable(fore) else fore
            up = up(S) if callable(up) else up
            if fore is None:
                straight[s] = (grip, up)
                fore = knuckles(up)[0]
            f, u = ortho(fore, up)
            e = rig.empties["wrist_" + s]
            wl = W @ wrist_for_grip(grip, f)
            wl.z = max(wl.z, 0.05)              # кисть не йде крізь мат
            e.location = wl
            e.rotation_quaternion = Wq @ hand_rotation(rig, s, f, u)
        else:
            sh = S[s]
            side = 1 if s == "l" else -1
            g = sh + Vector((0.07 * side, -0.05, -0.5))
            f, u = ortho(DOWN + FWD * 0.2, FWD + Vector((0.3 * -side, 0, 0)))
            e = rig.empties["wrist_" + s]
            e.location = W @ wrist_for_grip(g, f)
            e.rotation_quaternion = Wq @ hand_rotation(rig, s, f, u)
        el = p.elbows.get(s)
        if el is None:
            el = S[s] + Vector((0.35 * (1 if s == "l" else -1), 0.25, -0.35))
        rig.empties["elbow_" + s].location = W @ (el(S) if callable(el) else el)
    bpy.context.view_layer.update()
    for _ in range(2):
        if not straight:
            break
        for s, (grip, up) in straight.items():
            f, u = ortho(pb["hand_" + s].head - pb["lowerarm_" + s].head, up)
            rig.empties["wrist_" + s].location = W @ wrist_for_grip(grip, f)
            rig.empties["wrist_" + s].rotation_quaternion = Wq @ hand_rotation(rig, s, f, u)
        bpy.context.view_layer.update()

CONTACT_BONES = ("pelvis", "spine_01", "spine_02", "spine_03", "neck_01", "head", "upperarm_l", "upperarm_r",
                 "lowerarm_l", "lowerarm_r", "hand_l", "hand_r", "thigh_l", "thigh_r", "calf_l", "calf_r",
                 "foot_l", "foot_r")

def heads_in(rig_from, rig_to):
    """Голови й хвости кісток rig_from у просторі rig_to (для контактів)."""
    W = rig_to.arm.matrix_world.inverted() @ rig_from.arm.matrix_world
    pb = rig_from.arm.pose.bones
    d = {}
    for n in CONTACT_BONES:
        d[n] = W @ pb[n].head
        d[n + ".tail"] = W @ pb[n].tail
    return d

def P(S, bone, along=0.0, off=None):
    """Точка на кістці партнера: along — частка від голови до хвоста; off — зсув у просторі свого ригу."""
    h = S["P"].get(bone)
    if h is None:
        return None
    t = S["P"].get(bone + ".tail", h)
    pt = h + (t - h) * along
    return pt + (off or Vector())

# ---------------------------------------------------------------- шаги

def feet_track(t, side_keys):
    """Ступні з ключів [(час, позиція)] для однієї ноги: між різними позиціями — дуга з підйомом 8–14 см,
    на однакових — стоїть. Повертає (позиція, чи стоїть)."""
    keys = side_keys
    if t <= keys[0][0]:
        return keys[0][1], True
    for (t0, a), (t1, b) in zip(keys, keys[1:]):
        if t <= t1:
            if (b - a).length < 1e-4:
                return a, True
            k = smooth((t - t0) / (t1 - t0)) if t1 > t0 else 1.0
            lift = math.sin(math.pi * k) * min(0.14, 0.06 + (b - a).length * 0.2)
            return a + (b - a) * k + Vector((0, 0, lift)), False
    return keys[-1][1], True

def foot(x, y):
    return Vector((x, y, 0.071))

def steps(t, left, right):
    """Ступні з ключів кроків [(час, (x, y))] для лівої й правої: між різними точками — крок з підйомом."""
    la = [(tt, foot(*xy)) for tt, xy in left]
    ra = [(tt, foot(*xy)) for tt, xy in right]
    return feet_track(t, la)[0], feet_track(t, ra)[0]

# ---------------------------------------------------------------- кліпи

# Борцівська стійка: коліна зігнуті, тулуб уперед, руки перед грудьми, вага перекочується з ноги на ногу.
def stance(t, L, sway=1.0):
    sh = math.sin(2 * math.pi * t / L)
    return RPose(feet=(0.2, 0.18), feet_yaw=(10.0, -14.0),
                 pelvis=Vector((0.025 * sh * sway, 0.0, -0.09 + 0.008 * math.sin(4 * math.pi * t / L))),
                 pitch=16.0 + breathe(t, L, 1.2), roll=-2.0 * sh * sway, head_pitch=-10.0,
                 hands={"l": (lambda S: S["l"] + Vector((-0.07, -0.27, -0.2)), FWD * 0.4 + UP, RIGHT + FWD * 0.3),
                        "r": (lambda S: S["r"] + Vector((0.07, -0.27, -0.2)), FWD * 0.4 + UP, LEFT + FWD * 0.3)},
                 elbows={"l": lambda S: S["l"] + Vector((0.22, -0.1, -0.45)),
                         "r": lambda S: S["r"] + Vector((-0.22, -0.1, -0.45))},
                 fist={"l": 0.25, "r": 0.25}, forced=set())

def clip_stance(t, L):
    return stance(t, L)

# --- зчеп «комір — лікоть»: крок назустріч, ліва рука на шию партнера, права — на його лікоть; боротьба за позицію.
LOCKUP_LEN = 2.4

LOCKUP_APPROACH = 0.28     # кожен проходить назустріч (м): тази зійдуться на ~0,62 м

def lockup_side(t, L, partner_tag):
    approach = track(t, [(0.0, 0.0), (0.2, 0.15), (0.6, 1.0)])
    push = math.sin(2 * math.pi * max(0.0, t - 0.9) / 1.5) if t > 0.9 else 0.0
    p = stance(t, L, sway=0.0)
    d = LOCKUP_APPROACH
    # Два кроки назустріч: ліва (передня) — далеко, права — підтягується; далі тиснуть, переступаючи.
    p.ankles = steps(t, [(0.0, (0.2, -0.105)), (0.12, (0.2, -0.105)), (0.38, (0.2, -0.105 - d))],
                        [(0.0, (-0.2, 0.075)), (0.3, (-0.2, 0.075)), (0.58, (-0.2, 0.075 - d * 0.9)),
                         (1.5, (-0.2, 0.075 - d * 0.9)), (1.7, (-0.22, 0.03 - d * 0.9))])
    # таз відведений назад (нахил уперед — плечима, не стегнами), ноги не перехрещуються
    p.pelvis = Vector((0.04 * smooth(approach), -d * approach + 0.05 * smooth(approach) - 0.025 * push,
                       -0.13 - 0.02 * approach))
    p.pitch = 18.0 + 20.0 * smooth(approach) + 3.0 * push
    p.yaw = 8.0 * smooth(approach) + 5.0 * push
    p.roll = 4.0 * push
    p.head_pitch = -6.0
    p.head_yaw = 28.0 * smooth(approach)       # голова — збоку від голови партнера
    p.roll += 6.0 * smooth(approach)           # плече вбік — голови не стикаються
    reach = smooth((t - 0.32) / 0.32)
    p.forced = set()
    base_l = Vector((-0.07, -0.27, -0.2))
    base_r = Vector((0.07, -0.27, -0.2))
    p.hands = {
        # ліва — на потилицю партнера
        "l": (lambda S: lerp(S["l"] + base_l, P(S, "neck_01", 0.7, Vector((0.0, -0.07, 0.0))) or (S["l"] + base_l), reach),
              FWD + DOWN * 0.4, RIGHT + DOWN * 0.3),
        # права — тримає лікоть партнера (його ліву руку, що лежить на моїй шиї)
        "r": (lambda S: lerp(S["r"] + base_r, P(S, "lowerarm_l", 0.05, Vector((0.0, 0.0, -0.03))) or (S["r"] + base_r), reach),
              FWD + LEFT * 0.5, UP + FWD * 0.3),
    }
    p.fist = {"l": 0.25 + 0.35 * reach, "r": 0.25 + 0.5 * reach}
    p.elbows = {"l": lambda S: S["l"] + Vector((0.12, -0.25, -0.35)), "r": lambda S: S["r"] + Vector((-0.38, -0.05, -0.32))}
    return p

def clip_lockup_a(t, L, partner=None):
    return lockup_side(t, L, "B")

def clip_lockup_b(t, L, partner=None):
    return lockup_side(max(0.0, t - 0.04), L, "A")

# --- рамка тіла: точки пози спокою, повернуті разом із тазом (лежати, падати, бути піднятим)
REST = {}

def init_rest(arm):
    b = arm.data.bones
    REST["pelvis"] = b["pelvis"].head_local.copy()
    for s in "lr":
        REST["ankle_" + s] = b["foot_" + s].head_local.copy()
        REST["knee_" + s] = b["calf_" + s].head_local.copy()
        REST["hip_" + s] = b["thigh_" + s].head_local.copy()

def xf(off, q, pt):
    """Точка пози спокою pt після зсуву таза off і повороту тіла q навколо таза (простір ригу)."""
    pr = REST["pelvis"]
    return pr + off + q @ (pt - pr)

def off_for(abs_pelvis):
    return abs_pelvis - REST["pelvis"]

LIE_Z = 0.12                     # висота центру таза над матом, коли лежить на спині
Q_BACK = Quaternion(LEFT, math.radians(-90.0))     # лежить на спині, голова — у +Y ригу

def body(off, q, knee_bend=0.0, spread=0.0):
    """Поза «тіло цілком у рамці q»: ноги прямі (або злегка зігнуті), руки вздовж тіла, кисті не силові."""
    p = RPose()
    p.pelvis = off
    p.pelvis_q = q
    ka = xf(off, q, REST["ankle_l"] + Vector((spread, -0.25 * knee_bend, 0.35 * knee_bend)))
    kb = xf(off, q, REST["ankle_r"] + Vector((-spread, -0.25 * knee_bend, 0.35 * knee_bend)))
    p.ankles = (ka, kb)
    p.foot_rot = (q, q)
    p.knees = (xf(off, q, REST["knee_l"] + Vector((0.03, -0.6, 0.0))), xf(off, q, REST["knee_r"] + Vector((-0.03, -0.6, 0.0))))
    p.forced = set()
    p.head_yaw = 0.0
    return p

def arms_on_mat(p, q, lift_l=0.0, lift_r=0.0):
    """Руки розкинуті на маті під ~45° (лежачи на спині), lift — рука відривається від мату."""
    p.hands = {
        "l": (lambda S: S["l"] + q @ Vector((0.42, 0.05 - 0.4 * lift_l, -0.32 + 0.3 * lift_l)), q @ DOWN, q @ FWD),
        "r": (lambda S: S["r"] + q @ Vector((-0.42, 0.05 - 0.4 * lift_r, -0.32 + 0.3 * lift_r)), q @ DOWN, q @ FWD),
    }
    p.elbows = {"l": lambda S: S["l"] + q @ Vector((0.4, 0.1, -0.1)), "r": lambda S: S["r"] + q @ Vector((-0.4, 0.1, -0.1))}
    p.fist = {"l": 0.15, "r": 0.15}
    return p

def lying(x=0.0, y=0.0, yaw=0.0, sit=0.0, knee_bend=0.15):
    """Лежить на спині, таз у (x, y) простору ригу; yaw — поворот лінії тіла; sit — підняти плечі (0..1)."""
    q = Quaternion(UP, math.radians(yaw)) @ Q_BACK
    off = off_for(Vector((x, y, LIE_Z)))
    p = body(off, q, knee_bend=knee_bend)
    p.pitch = 55.0 * sit                         # тулуб над тазом угору (сісти / вирватись)
    p.head_pitch = -30.0 - 15.0 * sit            # голова не завалюється назад
    return arms_on_mat(p, q, lift_l=sit, lift_r=sit * 0.6)

# --- захват голови: A, після зчепу, переходить за бік B і затискає голову під праву пахву; B зігнутий, руки на руці A.
HEADLOCK_LEN = 2.2

def clip_headlock_a(t, L, partner=None):
    k = track(t, [(0.0, 0.0), (0.7, 1.0)])
    sq = 0.5 + 0.5 * math.sin(2 * math.pi * max(0.0, t - 0.7) / 0.75)
    p = stance(t, L, sway=0.2)
    p.ankles = steps(t, [(0.0, (0.2, -0.105)), (0.2, (0.2, -0.105)), (0.5, (0.26, -0.36))],
                        [(0.0, (-0.2, 0.075)), (0.4, (-0.2, 0.075)), (0.72, (-0.02, -0.1))])
    p.pelvis = Vector((0.16 * k, -0.12 * k, -0.12))
    p.pelvis_yaw = 75.0 * k
    p.yaw = 12.0 * k
    p.pitch = 20.0 - 4.0 * k
    p.head_yaw = None
    p.forced = set()
    p.hands = {
        # права обвиває шию B; ліва тягне свою праву на себе (замок)
        "r": (lambda S: P(S, "neck_01", 0.3, Vector((0.0, -0.04, -0.02 + 0.02 * sq))) or S["r"], DOWN + RIGHT * 0.4, BACK),
        "l": (lambda S: P(S, "head", 0.3, Vector((0.08, 0.0, 0.1))) or S["l"], RIGHT + DOWN * 0.5, UP),
    }
    p.elbows = {"r": lambda S: S["r"] + Vector((-0.35, -0.25, 0.1)), "l": lambda S: S["l"] + Vector((0.3, -0.3, -0.2))}
    p.fist = {"l": 0.7, "r": 0.8}
    return p

def clip_headlock_b(t, L, partner=None):
    k = track(t, [(0.0, 0.0), (0.7, 1.0)])
    sq = 0.5 + 0.5 * math.sin(2 * math.pi * max(0.0, t - 0.7) / 0.75)
    p = stance(t, L, sway=0.0)
    # B зігнутий уперед (голова під пахвою A), таз відведений назад, ноги широко для опору
    p.ankles = steps(t, [(0.0, (0.2, -0.105)), (0.3, (0.2, -0.105)), (0.6, (0.28, 0.02))],
                        [(0.0, (-0.2, 0.075)), (0.45, (-0.2, 0.075)), (0.75, (-0.24, 0.24))])
    p.pelvis = Vector((0.06 * k, 0.26 * k, -0.2 - 0.04 * sq))
    p.pitch = 18.0 + 60.0 * k
    p.head_pitch = 15.0 * k
    p.head_yaw = -20.0 * k
    p.forced = set()
    p.hands = {
        # обидві руки чіпляються за руку A, що тримає голову
        "l": (lambda S: P(S, "lowerarm_r", 0.35, Vector((0.0, 0.0, -0.04))) or S["l"], FWD, UP),
        "r": (lambda S: P(S, "lowerarm_r", 0.75, Vector((0.0, 0.0, -0.04))) or S["r"], FWD, UP),
    }
    p.fist = {"l": 0.55, "r": 0.55}
    return p

# --- бодислем: A підхоплює B (права між ніг під стегно, ліва — на плече), піднімає горизонтально на груди,
# тримає мить і кидає перед собою на спину. Таз B задано у світі A, тіло — рамкою.
SLAM_LEN = 2.8

def slam_k(t):
    # 0 — стоять; 1 — підхопив; 2 — горизонтально на грудях; 3 — тримає; 4 — на маті
    return track(t, [(0.0, 0.0), (0.45, 1.0), (1.05, 2.0), (1.4, 3.0), (1.68, 4.0), (2.8, 4.0)])

def a_to_b(v):
    """Точка зі світу A у простір B (B розвернутий на 180° і стоїть на відстані GAP перед A)."""
    return Vector((-v.x, -(v.y + GAP), v.z))

def clip_slam_b(t, L, partner=None):
    k = slam_k(t)
    pr = REST["pelvis"]
    stand_abs = a_to_b(Vector((0.0, -GAP, pr.z)))                 # = pr
    tilt_abs = a_to_b(Vector((-0.12, -0.46, pr.z + 0.12)))         # підхоплений — трохи вгору й правіше від голови A
    carry_abs = a_to_b(Vector((0.08, -0.5, 1.05)))                # горизонтально на передпліччях перед грудьми A
    mat_abs = a_to_b(Vector((0.0, -0.86, LIE_Z)))                 # на маті перед A
    q_carry = Quaternion(UP, math.radians(-90.0)) @ Q_BACK        # голова — праворуч від A, спина вниз
    if k < 1.0:
        kk = smooth(k)
        pel, q = lerp(stand_abs, tilt_abs, kk), Quaternion(LEFT, math.radians(10.0 * kk))
    elif k < 2.0:
        kk = smooth(k - 1.0)
        pel, q = lerp(tilt_abs, carry_abs, kk), Quaternion(LEFT, math.radians(10.0)).slerp(q_carry, kk)
    elif k < 3.0:
        pel, q = carry_abs + Vector((0, 0, 0.02 * math.sin(math.pi * (k - 2.0)))), q_carry
    else:
        kk = (k - 3.0)
        fall = kk * kk                                             # падіння прискорюється
        pel, q = lerp(carry_abs, mat_abs, fall), q_carry
    p = body(off_for(pel), q, knee_bend=0.25)
    if k < 1.0:                                                    # спочатку стоїть ногами на маті
        kk = smooth(k)
        p.ankles = (lerp(foot(0.16, -0.015), p.ankles[0], kk * 0.2), lerp(foot(-0.16, -0.015), p.ankles[1], kk * 0.2))
        p.foot_rot = (Quaternion(), Quaternion())
    elif k < 2.0:
        kk = smooth(k - 1.0)
        p.ankles = (lerp(foot(0.16, -0.015), p.ankles[0], 0.2 + 0.8 * kk), lerp(foot(-0.16, -0.015), p.ankles[1], 0.2 + 0.8 * kk))
    p.head_pitch = -20.0 if k >= 2.0 else 10.0
    land = smooth((k - 3.85) / 0.15) if k > 3.0 else 0.0
    if k < 3.6:
        # руки тримаються за спину й плечі A
        p.hands = {"l": (lambda S: P(S, "spine_03", 0.6, Vector((0.12, 0.08, 0.0))) or S["l"], FWD, UP),
                   "r": (lambda S: P(S, "upperarm_l", 0.2, Vector((0.0, 0.06, 0.04))) or S["r"], FWD, UP)}
        p.fist = {"l": 0.5, "r": 0.5}
    else:
        arms_on_mat(p, q, lift_l=0.3 * (1 - land), lift_r=0.3 * (1 - land))
    return p

def clip_slam_a(t, L, partner=None):
    k = slam_k(t)
    p = stance(t, L, sway=0.0)
    # крок лівою під суперника, широка стійка для підйому
    p.ankles = steps(t, [(0.0, (0.2, -0.105)), (0.12, (0.2, -0.105)), (0.4, (0.24, -0.22))],
                        [(0.0, (-0.2, 0.075)), (1.4, (-0.2, 0.075)), (1.62, (-0.18, -0.12))])
    scoop = smooth(k) if k < 1.0 else 1.0
    lift = smooth(k - 1.0) if 1.0 <= k < 2.0 else (1.0 if k >= 2.0 else 0.0)
    slam = smooth((k - 3.0) / 1.0) if k > 3.0 else 0.0
    p.pelvis = Vector((0.1 * scoop * (1 - lift), -0.06 * scoop, -0.1 - 0.2 * scoop + 0.18 * lift - 0.22 * slam))
    p.roll = 12.0 * scoop * (1 - lift)          # голова — ліворуч, під руку B, а не в його груди
    p.pitch = 18.0 + 22.0 * scoop - 32.0 * lift + 34.0 * slam
    p.yaw = -10.0 * lift
    p.head_pitch = -5.0
    p.forced = set()
    p.hands = {
        # права — під стегно B (між ніг), ліва — на верх спини / плече
        "r": (lambda S: P(S, "thigh_r", 0.25, Vector((0.0, 0.0, -0.07))) or S["r"], FWD, UP),
        "l": (lambda S: P(S, "spine_03", 0.6, Vector((0.0, 0.0, 0.08))) or S["l"], FWD + UP * 0.3, UP),
    }
    if slam > 0.6:
        p.hands = {"r": (lambda S: S["r"] + Vector((0.05, -0.35, -0.35)), DOWN, FWD),
                   "l": (lambda S: S["l"] + Vector((-0.05, -0.35, -0.35)), DOWN, FWD)}
    p.fist = {"l": 0.5, "r": 0.5}
    p.elbows = {"l": lambda S: S["l"] + Vector((0.4, 0.05, -0.3)), "r": lambda S: S["r"] + Vector((-0.4, 0.0, -0.4))}
    return p

# --- утримання: B лежить поперек перед A (голова ліворуч від A); A стає навколішки й лягає грудьми на груди B,
# підхоплює ближню ногу. Рефері рахує; на «два» B вириває плече — A відкидає.
COVER_LEN = 3.0

def cover_k(t):
    return track(t, [(0.0, 0.0), (0.6, 1.0), (2.0, 1.0), (2.3, 2.0), (3.0, 2.0)])

def clip_cover_b(t, L, partner=None):
    k = cover_k(t)
    kick = smooth(k - 1.0) if k > 1.0 else 0.0
    p = lying(sit=0.45 * kick, knee_bend=0.2 + 0.3 * kick)
    return p

def clip_cover_a(t, L, partner=None):
    k = cover_k(t)
    lay = smooth(min(1.0, k))
    thrown = smooth(k - 1.0) if k > 1.0 else 0.0
    if lay < 0.35:
        p = stance(t, L, sway=0.0)
        kk = lay / 0.35
        p.pelvis = lerp(p.pelvis, Vector((0.0, -0.1, -0.45)), kk)
        p.pitch = 30.0 * kk + 18.0
    else:
        kk = smooth((lay - 0.35) / 0.65)
        # навколішки поруч із B, тулуб падає грудьми на B
        q = Quaternion(LEFT, math.radians(82.0 * kk + 20.0 * (1 - kk) - 45.0 * thrown))
        abs_pel = Vector((-0.05 * kk, -0.27 * kk + 0.15 * thrown, 0.62 - 0.34 * kk + 0.24 * thrown))
        p = RPose()
        p.pelvis = off_for(abs_pel)
        p.pelvis_q = q
        p.pitch = 25.0 * kk
        # навколішки: коліна позаду таза на маті, гомілки ще далі назад
        p.ankles = (Vector((0.17, 0.42, 0.09)), Vector((-0.17, 0.42, 0.09)))
        tip = Quaternion(RIGHT, math.radians(-75.0))
        p.foot_rot = (tip, tip)
        p.knees = (Vector((0.18, 0.1, -0.25)), Vector((-0.18, 0.1, -0.25)))
        p.forced = set()
        p.hands = {
            "l": (lambda S: P(S, "spine_03", 0.8, Vector((0.0, -0.16, 0.14))) or S["l"], DOWN, FWD),
            "r": (lambda S: P(S, "thigh_l", 0.6, Vector((0.0, 0.0, 0.07))) or S["r"], DOWN + FWD * 0.3, FWD),
        }
        p.elbows = {"l": lambda S: S["l"] + Vector((0.3, -0.2, 0.35)), "r": lambda S: S["r"] + Vector((-0.3, 0.0, 0.2))}
        p.head_pitch = 25.0
    return p

# --- падіння на спину («прийняти удар») і підйом — одна постать.
BUMP_LEN = 1.3

def clip_bump_back(t, L):
    # присідає назад → сідниці й спина торкаються мату одночасно → руки б'ють по мату під 45°, підборіддя до грудей
    th = track(t, [(0.0, 0.0), (0.18, 12.0), (0.42, 55.0), (0.56, 90.0), (1.3, 90.0)])
    y = track(t, [(0.0, 0.0), (0.2, 0.08), (0.45, 0.3), (0.56, 0.4), (1.3, 0.4)])
    z = track(t, [(0.0, REST["pelvis"].z), (0.22, 0.66), (0.44, 0.3), (0.56, LIE_Z), (1.3, LIE_Z)])
    slap = track(t, [(0.0, 0.0), (0.48, 0.0), (0.57, 1.0), (1.0, 0.65)])
    bounce = math.sin(math.pi * smooth((t - 0.56) / 0.22)) * 0.03 if 0.56 < t < 0.78 else 0.0
    q = Quaternion(LEFT, math.radians(-th))
    abs_pel = Vector((0.0, y, z + bounce))
    p = body(off_for(abs_pel), q, knee_bend=0.45 * smooth(th / 90.0))
    planted = 1.0 - smooth((th - 45.0) / 40.0)
    p.ankles = (lerp(p.ankles[0], foot(0.16, -0.015), planted), lerp(p.ankles[1], foot(-0.16, -0.015), planted))
    p.foot_rot = (Quaternion().slerp(q, 1 - planted), Quaternion().slerp(q, 1 - planted))
    p.knees = (lerp(p.knees[0], Vector((0.2, -0.6, 0.5)), planted), lerp(p.knees[1], Vector((-0.2, -0.6, 0.5)), planted))
    p.pitch = 20.0 * (1 - smooth(th / 80.0))      # спершу зігнутий, при ударі — рівно
    p.head_pitch = 35.0 * smooth(th / 90.0)       # підборіддя до грудей — голова не б'ється об мат
    arms_on_mat(p, q, lift_l=1.0 - slap, lift_r=1.0 - slap)
    guard = 1.0 - smooth((th - 30.0) / 40.0)
    if guard > 0:
        mat_l, mat_r = p.hands["l"], p.hands["r"]
        g = guard
        p.hands = {
            "l": (lambda S: lerp(mat_l[0](S), S["l"] + Vector((-0.07, -0.27, -0.2)), g), FWD * 0.4 + UP, RIGHT),
            "r": (lambda S: lerp(mat_r[0](S), S["r"] + Vector((0.07, -0.27, -0.2)), g), FWD * 0.4 + UP, LEFT),
        }
    return p

GETUP_LEN = 2.6

def clip_getup_back(t, L):
    # лежить → сідає → перекочується на праве коліно, ліва нога спереду → встає в стійку
    k = track(t, [(0.0, 0.0), (0.7, 1.0), (1.5, 2.0), (2.3, 3.0), (2.6, 3.0)])
    pr = REST["pelvis"]
    if k <= 1.0:
        p = lying(sit=smooth(k) * 1.0, knee_bend=0.15 + 0.5 * smooth(k))
        return p
    if k <= 2.0:
        kk = smooth(k - 1.0)
        # сидить → коліно: таз піднімається, тіло обертається вертикально, руки — на мат за собою
        q = Quaternion(LEFT, math.radians(-90.0 * (1 - kk) + 0.0))
        abs_pel = Vector((0.0, lerp(0.0, 0.1, kk), lerp(LIE_Z, 0.55, kk)))
        p = RPose()
        p.pelvis = off_for(abs_pel)
        p.pelvis_q = q
        p.pitch = lerp(55.0, 30.0, kk)
        p.ankles = (lerp(Vector((0.18, -0.45, 0.071)), foot(0.2, -0.25), kk), lerp(Vector((-0.16, -0.55, 0.071)), Vector((-0.18, 0.45, 0.09)), kk))
        tip = Quaternion(RIGHT, math.radians(-70.0 * kk))
        p.foot_rot = (Quaternion(), tip)
        p.knees = (Vector((0.22, -0.6, 0.6)), lerp(Vector((-0.2, -0.6, 0.6)), Vector((-0.2, -0.3, 0.1)), kk))
        p.forced = set()
        p.hands = {"l": (lambda S: S["l"] + Vector((0.15, -0.35 * kk + 0.25 * (1 - kk), -0.6 * (1 - kk) - 0.4 * kk)), DOWN, FWD),
                   "r": (lambda S: S["r"] + Vector((-0.2, 0.25 * (1 - kk), -0.55)), DOWN, FWD)}
        return p
    kk = smooth(k - 2.0)
    p = stance(t, L, sway=0.0)
    p.pelvis = lerp(off_for(Vector((0.0, 0.1, 0.55))), p.pelvis, kk)
    p.pitch = lerp(30.0, p.pitch, kk)
    p.ankles = (feet_track(kk, [(0.0, foot(0.2, -0.25)), (0.5, foot(0.2, -0.25)), (1.0, foot(0.2, -0.105))])[0],
                feet_track(kk, [(0.0, Vector((-0.18, 0.45, 0.09))), (0.5, foot(-0.2, 0.075)), (1.0, foot(-0.2, 0.075))])[0])
    tip = Quaternion(RIGHT, math.radians(-70.0 * (1 - kk)))
    p.foot_rot = (Quaternion(UP, math.radians(10)), tip)
    return p

# ім'я, довжина, функція A, функція B (None — одна постать), петля, зсув B (м, у просторі A), поворот B (°)
CLIPS = [
    ("Ring_Stance_Loop", 2.4, clip_stance, None, True, None, 0),
    ("Lockup", LOCKUP_LEN, clip_lockup_a, clip_lockup_b, False, Vector((0, -0.62 - 2 * LOCKUP_APPROACH, 0)), 180),
    ("Headlock", HEADLOCK_LEN, clip_headlock_a, clip_headlock_b, False, Vector((0, -GAP, 0)), 180),
    ("Bodyslam", SLAM_LEN, clip_slam_a, clip_slam_b, False, Vector((0, -GAP, 0)), 180),
    # B лежить поперек перед A: голова — ліворуч від A (світ −X), груди — під грудьми A
    ("Cover", COVER_LEN, clip_cover_a, clip_cover_b, False, Vector((0.12, -0.6, 0)), 90),
    ("Bump_Back", BUMP_LEN, clip_bump_back, None, False, None, 0),
    ("GetUp_Back", GETUP_LEN, clip_getup_back, None, False, None, 0),
]

# ---------------------------------------------------------------- запікання пари і якість

def solve_frame(rigA, rigB, fa, fb, t, L):
    if fb is None:
        apply_w(rigA, fa(t, L))
        return
    # кілька проходів: контакти кожного — по кістках партнера з попереднього проходу
    apply_w(rigB, fb(t, L, None))
    for _ in range(2):
        apply_w(rigA, fa(t, L, heads_in(rigB, rigA)), heads_in(rigB, rigA))
        apply_w(rigB, fb(t, L, heads_in(rigA, rigB)), heads_in(rigA, rigB))
    apply_w(rigA, fa(t, L, heads_in(rigB, rigA)), heads_in(rigB, rigA))

def capture_w(rig):
    W = rig.arm.matrix_world
    return {pb.name: pb.matrix.copy() for pb in rig.arm.pose.bones}, {
        n: (W @ rig.arm.pose.bones[n].head, W @ rig.arm.pose.bones[n].tail) for n in CONTACT_BONES}

RADIUS = {"pelvis": 0.15, "spine_01": 0.15, "spine_02": 0.16, "spine_03": 0.16, "neck_01": 0.06, "head": 0.1,
          "upperarm_l": 0.055, "upperarm_r": 0.055, "lowerarm_l": 0.045, "lowerarm_r": 0.045, "hand_l": 0.04,
          "hand_r": 0.04, "thigh_l": 0.08, "thigh_r": 0.08, "calf_l": 0.06, "calf_r": 0.06, "foot_l": 0.045, "foot_r": 0.045}

def seg_dist(p1, q1, p2, q2):
    d1, d2, r = q1 - p1, q2 - p2, p1 - p2
    a, e, f = d1.dot(d1), d2.dot(d2), d2.dot(r)
    if a < 1e-9 and e < 1e-9:
        return (p1 - p2).length
    c = d1.dot(r)
    b = d1.dot(d2)
    den = a * e - b * b
    s = max(0.0, min(1.0, (b * f - c * e) / den)) if den > 1e-9 else 0.0
    t = (b * s + f) / e if e > 1e-9 else 0.0
    if t < 0.0:
        t, s = 0.0, max(0.0, min(1.0, -c / a)) if a > 1e-9 else 0.0
    elif t > 1.0:
        t, s = 1.0, max(0.0, min(1.0, (b - c) / a)) if a > 1e-9 else 0.0
    return ((p1 + d1 * s) - (p2 + d2 * t)).length

HAND_BONES = {"hand_l", "hand_r", "lowerarm_l", "lowerarm_r"}

def quality(name, framesA, framesB):
    """Ковзання стопи на опорі (см), найбільше проникнення тіл (см; руки-контакти не рахуються), найгірше зап'ястя."""
    slide = 0.0
    for frames in (framesA, framesB):
        if not frames:
            continue
        for s in ("foot_l", "foot_r"):
            anchor = None
            for _, w in frames:
                h = w[s][0]
                if h.z < 0.095 and w["pelvis"][0].z > 0.6:
                    if anchor is None:
                        anchor = h.copy()
                    slide = max(slide, (Vector((h.x, h.y, 0)) - Vector((anchor.x, anchor.y, 0))).length)
                else:
                    anchor = None
    pen = 0.0
    worst = ""
    if framesB:
        for fi, ((_, wa), (_, wb)) in enumerate(zip(framesA, framesB)):
            for na in CONTACT_BONES:
                for nb in CONTACT_BONES:
                    if na in HAND_BONES or nb in HAND_BONES:
                        continue
                    d = seg_dist(wa[na][0], wa[na][1], wb[nb][0], wb[nb][1])
                    v = RADIUS[na] + RADIUS[nb] - d
                    if v > pen:
                        pen, worst = v, f"A.{na} × B.{nb} на кадрі {fi}"
    if worst:
        print(f"   [проникнення] {name}: {pen * 100:.1f} см — {worst}", flush=True)
    return slide * 100.0, max(0.0, pen) * 100.0

def main():
    armA, armB = load_pair()
    rigA, rigB = Rig(armA), Rig(armB)
    init_rest(armA)
    report = []
    samples = []
    manifest = ["# Кліпи рингу (tools/blender/ring_anims.py). <кліп> <пара A|B|-> <довжина с> <петля 0|1> "
                "<зсув B: x y z (м, простір A)> <поворот B °>"]
    for name, length, fa, fb, loop, offset, yaw in CLIPS:
        if ONLY and name not in ONLY:
            continue
        place(armA, Vector(), 0)
        if fb is not None:
            place(armB, offset, yaw)
        else:
            place(armB, Vector((0, 50, 0)), 0)
        frames = int(round(length * FPS))
        sa, sb, wa, wb = [], [], [], []
        for f in range(frames + 1):
            solve_frame(rigA, rigB, fa, fb, f / FPS, length)
            ca, w1 = capture_w(rigA)
            move_twist_to_forearm(armA, ca)
            sa.append(ca); wa.append((ca, w1))
            if fb is not None:
                cb, w2 = capture_w(rigB)
                move_twist_to_forearm(armB, cb)
                sb.append(cb); wb.append((cb, w2))
        slide, pen = quality(name, wa, wb)
        for tag, frames in (("A", wa), ("B", wb)):
            for i in (0, len(frames) // 3, 2 * len(frames) // 3, len(frames) - 1) if frames else ():
                w = frames[i][1]
                f2 = lambda v: f"({v.x:+.2f},{v.y:+.2f},{v.z:+.2f})"
                print(f"   [пози] {name} {tag} кадр {i}: голова {f2(w['head'][0])} груди {f2(w['spine_03'][0])} "
                      f"таз {f2(w['pelvis'][0])} стопи {f2(w['foot_l'][0])} {f2(w['foot_r'][0])}", flush=True)
        worst = max((wrist_numbers(armA, c, "r") for c in sa), key=lambda w: (abs(w[0]), w[1], abs(w[2])))
        line = f"[кліп] {name}: {length:.2f} с; ковзання стопи {slide:.1f} см; проникнення {pen:.1f} см; " \
               f"зап'ястя A(r) {worst}"
        print(line, flush=True)
        report.append(line)
        if fb is None:
            samples.append((name, sa, armA))
            manifest.append(f"{name} - {length:.3f} {int(loop)} 0 0 0 0")
        else:
            samples.append((name + "_A", sa, armA))
            samples.append((name + "_B", sb, armB))
            o = offset
            manifest.append(f"{name}_A A {length:.3f} {int(loop)} {o.x:.3f} {o.y:.3f} {o.z:.3f} {yaw}")
            manifest.append(f"{name}_B B {length:.3f} {int(loop)} {o.x:.3f} {o.y:.3f} {o.z:.3f} {yaw}")
        if LOOK:
            look_pair(name, armA, armB if fb is not None else None, sa, sb, length)
    strip_constraints(rigA)
    strip_constraints(rigB)
    actions = [write_action(arm, n, s) for n, s, arm in samples]
    # Експорт — лише риг A з усіма діями: кістки однакові, дії B грають на будь-якому скелеті набору.
    for o in list(bpy.data.objects):
        if o.get("pair") == "B":
            bpy.data.objects.remove(o, do_unlink=True)
    place(armA, Vector(), 0)
    global OUT_FBX_PATH
    export_ring(armA)
    with open(MANIFEST, "w", encoding="utf-8", newline="\n") as fh:
        fh.write("\n".join(manifest) + "\n")
    os.makedirs(LOOK_DIR, exist_ok=True)
    with open(os.path.join(LOOK_DIR, "report.txt"), "w", encoding="utf-8", newline="\n") as fh:
        fh.write("\n".join(report) + "\n")
    print("[експорт] " + OUT_FBX, flush=True)

def export_ring(arm):
    for o in bpy.context.selected_objects:
        o.select_set(False)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    arm.animation_data.action = None
    for pb in arm.pose.bones:
        pb.rotation_quaternion = Quaternion()
        pb.location = Vector()
    bpy.context.scene.frame_set(0)
    bpy.context.view_layer.update()
    bpy.context.scene.render.fps = FPS
    os.makedirs(os.path.dirname(OUT_FBX), exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=OUT_FBX, use_selection=True, object_types={'ARMATURE'},
                             apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y',
                             add_leaf_bones=False, primary_bone_axis='Y', secondary_bone_axis='X',
                             bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
                             bake_anim_use_all_bones=True, bake_anim_force_startend_keying=True,
                             bake_anim_step=1.0, bake_anim_simplify_factor=0.0)

# ---------------------------------------------------------------- огляд

def look_pair(name, armA, armB, sa, sb, length):
    """Контактний лист: 6 моментів × 2 ракурси, A — червоний, B — синій. Пози ставляться прямо з запечених матриць."""
    import numpy as np
    scene = bpy.context.scene
    try:
        scene.render.engine = 'BLENDER_WORKBENCH'
    except TypeError:
        pass
    scene.display.shading.light = 'STUDIO'
    scene.display.shading.color_type = 'OBJECT'
    W, H = 260, 300
    scene.render.resolution_x, scene.render.resolution_y = W, H
    cam = bpy.data.objects.get("look_cam")
    if cam is None:
        cam = bpy.data.objects.new("look_cam", bpy.data.cameras.new("look_cam"))
        scene.collection.objects.link(cam)
    scene.camera = cam
    cam.data.type = 'ORTHO'
    cam.data.ortho_scale = 3.2
    for o in bpy.data.objects:
        if o.type != 'MESH':
            continue
        tag = o.get("pair")
        show = ("body" in o.name or "low-poly" in o.name or "trousers" in o.name) and (tag == "A" or armB is not None)
        o.hide_viewport = not show
        o.hide_render = not show
        o.color = (0.8, 0.25, 0.2, 1) if tag == "A" else (0.25, 0.4, 0.85, 1)
    # Пози — з запечених матриць (обмеження IK ще висять, тож вимикаємо їх на час огляду).
    def set_pose(arm, cap):
        for pb in arm.pose.bones:
            for c in pb.constraints:
                c.mute = True
        bones = arm.data.bones
        for pb in arm.pose.bones:
            b = bones[pb.name]
            m = cap[pb.name]
            base = (cap[b.parent.name] @ b.parent.matrix_local.inverted() @ b.matrix_local) if b.parent else b.matrix_local
            loc, rot, _ = (base.inverted() @ m).decompose()
            pb.rotation_quaternion = rot
            pb.location = loc if pb.name == "pelvis" else Vector()
    n = len(sa)
    idx = [round((n - 1) * i / 5) for i in range(6)]
    tiles = []
    mid = Vector((0, -0.3, 1.0))
    views = (((4.0, -0.3, 1.1), (math.pi / 2, 0, math.pi / 2)),
             ((3.0, -3.3, 1.6), (math.radians(72), 0, math.radians(42))))
    for vi, (loc, rot) in enumerate(views):
        row = []
        for i in idx:
            set_pose(armA, sa[i])
            if armB is not None:
                set_pose(armB, sb[i])
            bpy.context.view_layer.update()
            cam.location = loc
            cam.rotation_euler = rot
            path = os.path.join(LOOK_DIR, "_tile.png")
            scene.render.filepath = path
            bpy.ops.render.render(write_still=True)
            img = bpy.data.images.load(path, check_existing=False)
            px = np.array(img.pixels[:], dtype=np.float32).reshape(H, W, 4)
            bpy.data.images.remove(img)
            row.append(px)
        tiles.append(np.concatenate(row, axis=1))
    sheet = np.concatenate(tiles[::-1], axis=0)
    out = bpy.data.images.new("sheet", width=sheet.shape[1], height=sheet.shape[0], alpha=True)
    out.pixels = sheet.ravel().tolist()
    os.makedirs(LOOK_DIR, exist_ok=True)
    out.filepath_raw = os.path.join(LOOK_DIR, name + ".png")
    out.file_format = 'PNG'
    out.save()
    bpy.data.images.remove(out)
    for arm in (armA, armB):
        if arm is None:
            continue
        for pb in arm.pose.bones:
            for c in pb.constraints:
                c.mute = False
    print(f"[огляд] {name} → {os.path.join(LOOK_DIR, name + '.png')}", flush=True)

main()
