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

# ---------------------------------------------------------------- криві без зупинок
# track() з alpha_anims — smoothstep на кожному відрізку: швидкість падає до нуля на КОЖНОМУ ключі, рух «зупиняється»,
# як у робота. Тут — монотонний кубічний сплайн Ерміта (Фріч — Карлсон): швидкість безперервна через ключі, без
# перельотів між ними; зупинка — лише там, де рух справді змінює напрям або ключ повторено (затримка), і на краях.

def _mono_tangents(ts, vs):
    n = len(ts)
    d = [(vs[i + 1] - vs[i]) / (ts[i + 1] - ts[i]) if ts[i + 1] > ts[i] else 0.0 for i in range(n - 1)]
    m = [0.0] * n
    for i in range(1, n - 1):
        if d[i - 1] * d[i] <= 0.0:
            m[i] = 0.0
        else:
            h0, h1 = ts[i] - ts[i - 1], ts[i + 1] - ts[i]
            w1, w2 = 2 * h1 + h0, h1 + 2 * h0
            m[i] = (w1 + w2) / (w1 / d[i - 1] + w2 / d[i])
    return m

def _hermite_scalar(t, ts, vs):
    if t <= ts[0]:
        return vs[0]
    if t >= ts[-1]:
        return vs[-1]
    m = _mono_tangents(ts, vs)
    for i in range(len(ts) - 1):
        if t <= ts[i + 1]:
            h = ts[i + 1] - ts[i]
            if h <= 0:
                return vs[i + 1]
            u = (t - ts[i]) / h
            h00 = 2 * u ** 3 - 3 * u ** 2 + 1
            h10 = u ** 3 - 2 * u ** 2 + u
            h01 = -2 * u ** 3 + 3 * u ** 2
            h11 = u ** 3 - u ** 2
            return h00 * vs[i] + h10 * h * m[i] + h01 * vs[i + 1] + h11 * h * m[i + 1]
    return vs[-1]

def track(t, keys):
    """Ключі [(час, значення)] — число, Vector або кортеж; монотонний кубічний сплайн (див. вище)."""
    ts = [k[0] for k in keys]
    v0 = keys[0][1]
    if isinstance(v0, (int, float)):
        return _hermite_scalar(t, ts, [float(k[1]) for k in keys])
    if isinstance(v0, tuple):
        return tuple(_hermite_scalar(t, ts, [k[1][c] for k in keys]) for c in range(len(v0)))
    return Vector([_hermite_scalar(t, ts, [k[1][c] for k in keys]) for c in range(len(v0))])

LOOK = "--look" in _args
STRICT = "--strict" in _args
REELS = "--reels" in _args
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

LOCKUP_APPROACH = 0.3      # кожен проходить назустріч (м): тази зійдуться на ~0,58 м

LOCKUP_HIT = 0.56          # с — груди в груди (мітка contact)

def lockup_side(t, L, partner_tag, sign=1.0):
    """Зчеп «комір — лікоть». Карта кінцівок у фазі тримання (обидва дзеркально):
         ліва кисть — потилиця суперника, рука ВИТЯГНУТА (лікоть донизу-вбік);
         права кисть — його лівий лікоть знизу, лікоть притиснутий до свого боку;
         голова — вухо до вуха з суперником (праворуч від його голови); стопи — розкрок, ліва попереду.
    Фази: передчуття (присід, плечі назад) → два кроки назустріч → удар грудьми з віддачею → боротьба: пара
    хитається в протифазі (sign: A тисне, коли B подається), вага перекочується, переступання, стискання рук."""
    d = LOCKUP_APPROACH
    antic = track(t, [(0.0, 0.0), (0.16, 1.0), (0.3, 0.0)])                       # 2 передчуття
    approach = track(t, [(0.0, 0.0), (0.2, 0.0), (0.52, 1.0)])
    jolt = hit_spring(t, LOCKUP_HIT, w=16.0, z=0.35)                              # віддача від удару грудьми
    fight = smooth((t - 0.8) / 0.4) if t > 0.8 else 0.0
    ph = 2 * math.pi * max(0.0, t - 0.8) / 1.1
    push = sign * math.sin(ph) * fight                                            # +1 — тисне вперед
    shift = -sign * math.sin(ph + 0.9) * fight                                    # вага: ліва/права нога (пара разом)
    p = stance(t, L, sway=0.0)
    # кроки: ліва (передня) далеко, права підтягується; у боротьбі — ще по кроку-перестановці
    p.ankles = steps(t, [(0.0, (0.2, -0.105)), (0.2, (0.2, -0.105)), (0.4, (0.2, -0.105 - d)),
                         (1.35, (0.2, -0.105 - d)), (1.55, (0.22, -0.105 - d - 0.06 * sign))],
                        [(0.0, (-0.2, 0.075)), (0.32, (-0.2, 0.075)), (0.54, (-0.2, 0.075 - d * 0.9)),
                         (1.8, (-0.2, 0.075 - d * 0.9)), (2.0, (-0.23, 0.04 - d * 0.9 - 0.04 * sign))])
    # 1 стискання й 11 асиметрія: присід перед кроком, таз відведений, плечі вперед; вага ходить з ноги на ногу
    p.pelvis = p.pelvis + Vector((0.04 * smooth(approach) + 0.012 * shift,
                                  -d * approach + 0.05 * smooth(approach) + 0.04 * antic - 0.015 * push + 0.012 * max(0.0, jolt),
                                  -0.04 * antic - 0.06 * approach - 0.015 * abs(push)))
    p.pitch = p.pitch - 6.0 * antic + 20.0 * smooth(approach) + 2.0 * push - 2.5 * jolt
    p.yaw = 8.0 * smooth(approach) + 3.0 * push
    p.roll = 10.0 * smooth(approach) + 2.0 * shift                                 # плече вбік — голови не стикаються
    p.head_pitch = p.head_pitch + 4.0 * smooth(approach) + 4.0 * jolt
    p.head_yaw = 34.0 * smooth(approach) - 3.0 * push                              # вухо до вуха
    reach = smooth((t - 0.32) / 0.42)                                              # хватка доходить після віддачі
    p.forced = set()
    base_l, base_r = Vector((-0.07, -0.27, -0.2)), Vector((0.07, -0.27, -0.2))
    arc = math.sin(math.pi * reach)                                                # 7 дуга: кисть іде вгору й назовні
    def hand_l(S):
        tgt = P(S, "neck_01", 0.5, Vector((0.025, -0.03, 0.0))) or (S["l"] + base_l)
        return lerp(S["l"] + base_l, tgt, reach) + Vector((0.08, 0.0, 0.1)) * arc
    def hand_r(S):
        tgt = P(S, "lowerarm_l", 0.05, Vector((0.0, 0.0, -0.035))) or (S["r"] + base_r)
        return lerp(S["r"] + base_r, tgt, reach) + Vector((-0.06, 0.0, -0.05)) * arc
    _, fl0, ul0 = p.hands["l"]
    _, fr0, ur0 = p.hands["r"]
    p.hands = {"l": (hand_l, fl0.lerp(FWD + DOWN * 0.4, reach), ul0.lerp(RIGHT + DOWN * 0.3, reach)),
               "r": (hand_r, fr0.lerp(FWD + LEFT * 0.5, reach), ur0.lerp(UP + FWD * 0.3, reach))}
    grip = 0.15 * fight * (0.5 + 0.5 * math.sin(ph * 2))                           # 8 пальці стискають у такт
    p.fist = {"l": 0.25 + 0.35 * reach + grip, "r": 0.25 + 0.5 * reach + grip}
    el_l, el_r = p.elbows["l"], p.elbows["r"]
    # витягнута ліва: лікоть униз-назовні, майже прямий; права: лікоть до свого боку
    p.elbows = {"l": lambda S: el_l(S).lerp(S["l"] + Vector((0.16, -0.32, -0.3)), reach),
                "r": lambda S: el_r(S).lerp(S["r"] + Vector((-0.38, -0.05, -0.32)), reach)}
    return p

def clip_lockup_a(t, L, partner=None):
    return lockup_side(t, L, "B", 1.0)

def clip_lockup_b(t, L, partner=None):
    return lockup_side(max(0.0, t - 0.04), L, "A", -1.0)

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
    # «продаж» захвату: на кожному стисканні A коліна просідають і тіло смикається; між ними — викручується,
    # шукаючи вихід (тулуб і таз ходять у протифазі), голова тисне з-під пахви
    fight = smooth((t - 0.75) / 0.4) if t > 0.75 else 0.0
    w = math.sin(2 * math.pi * max(0.0, t - 0.7) / 0.9)
    jolt = hit_spring(t % 0.75 + 0.7, 0.7, w=18.0, z=0.4) * fight if t > 0.7 else 0.0
    p.pelvis = Vector((0.06 * k + 0.04 * w * fight, 0.26 * k, -0.2 - 0.06 * sq * fight - 0.02 * jolt))
    p.pitch = 18.0 + 60.0 * k + 6.0 * jolt
    p.roll = 9.0 * w * fight
    p.yaw = -7.0 * w * fight
    p.head_pitch = 15.0 * k + 6.0 * jolt
    p.head_yaw = -20.0 * k + 8.0 * w * fight
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
    # Карта тіла B у підйомі: горизонтально, обличчям угору, притиснутий до грудей A на висоті його грудей; голова —
    # ліворуч від A, ноги — праворуч; спина лежить на передпліччях A. Падає прямо перед A, на спину.
    carry_abs = a_to_b(Vector((0.05, -0.34, 1.2)))
    mat_abs = a_to_b(Vector((0.0, -0.98, LIE_Z)))                 # на маті перед A, ноги B — повз стопи A
    q_carry = Quaternion(UP, math.radians(-90.0)) @ Q_BACK        # голова — праворуч від A, спина вниз
    if k < 1.0:
        kk = smooth(k)
        pel, q = lerp(stand_abs, tilt_abs, kk), Quaternion(LEFT, math.radians(28.0 * kk))   # перевисає через плече A
    elif k < 2.0:
        kk = smooth(k - 1.0)
        pel, q = lerp(tilt_abs, carry_abs, kk), Quaternion(LEFT, math.radians(28.0)).slerp(q_carry, kk)
    elif k < 3.0:
        pel, q = carry_abs + Vector((0, 0, 0.02 * math.sin(math.pi * (k - 2.0)))), q_carry
    else:
        kk = (k - 3.0)
        fall = kk * kk                                             # падіння прискорюється
        pel, q = lerp(carry_abs, mat_abs, fall), q_carry
    T_LAND = 1.68
    sell = smooth((t - T_LAND - 0.15) / 0.4) if t > T_LAND else 0.0
    writhe = math.sin(2 * math.pi * max(0.0, t - T_LAND) / 1.1)
    air = math.sin(2 * math.pi * t / 0.5) if 1.0 < k < 3.6 else 0.0
    p = body(off_for(pel), q, knee_bend=0.25 + 0.12 * air + 0.45 * sell * (0.6 + 0.4 * writhe))
    bang = hit_spring(t, T_LAND, w=16.0, z=0.3)
    if k >= 3.9:
        p.pelvis = p.pelvis + q @ Vector((0.0, 0.0, -0.03 * max(0.0, -bang)))          # підкидає від мату
        p.pitch = -16.0 * bang + 10.0 * sell                                          # прогин, потім згортається
        p.roll = 12.0 * writhe * sell
    if k < 1.0:                                                    # спочатку стоїть ногами на маті
        kk = smooth(k)
        p.ankles = (lerp(foot(0.16, -0.015), p.ankles[0], kk * 0.2), lerp(foot(-0.16, -0.015), p.ankles[1], kk * 0.2))
        p.foot_rot = (Quaternion(), Quaternion())
    elif k < 1.35:
        kk = smooth((k - 1.0) / 0.35)                               # стопи відриваються одразу, ноги не волочаться
        p.ankles = (lerp(foot(0.16, -0.015), p.ankles[0], 0.2 + 0.8 * kk), lerp(foot(-0.16, -0.015), p.ankles[1], 0.2 + 0.8 * kk))
    land = smooth((k - 3.85) / 0.15) if k > 3.0 else 0.0
    # удар об мат: підборіддя до грудей (голова не б'ється об мат), потім піднімає голову, корчачись
    p.head_pitch = (-20.0 if k >= 2.0 else 10.0) + (40.0 * abs(bang) + 25.0 * land if k >= 3.6 else 0.0) + 18.0 * sell
    p.head_yaw = 18.0 * writhe * sell
    if k < 1.4:
        # ще стоїть: руки на плечах A; з відривом від мату плавно відпускає й руки звисають
        on_a = {"l": lambda S: P(S, "spine_03", 0.6, Vector((0.12, 0.08, 0.0))) or S["l"],
                "r": lambda S: P(S, "upperarm_l", 0.2, Vector((0.0, 0.06, 0.04))) or S["r"]}
        let = smooth((k - 0.8) / 0.6) if k > 0.8 else 0.0
        hang = {"l": S_off((0.0, 0.06, -0.42), "l"), "r": S_off((0.0, -0.06, -0.42), "r")}
        p.hands = {s_: (lambda S, s_=s_: lerp(on_a[s_](S), hang[s_](S), let), FWD.lerp(DOWN, let), UP.lerp(FWD, let))
                   for s_ in "lr"}
        p.fist = {"l": 0.5 - 0.4 * let, "r": 0.5 - 0.4 * let}
    else:
        # у повітрі руки звисають і теліпаються (вторинна дія); перед ударом об мат плавно розкидаються під 45° і
        # б'ють по мату — без перескоку кисті (власник: «Руки не повинні телепортуватись»)
        fl = math.sin(2 * math.pi * t / 0.45)
        hang = {"l": (lambda S: S["l"] + Vector((0.08 * fl, 0.06, -0.42)), DOWN, FWD),
                "r": (lambda S: S["r"] + Vector((-0.08 * fl, -0.06, -0.42)), DOWN, FWD)}
        arms_on_mat(p, q, lift_l=0.3 * (1 - land) + 0.5 * sell * max(0.0, writhe), lift_r=0.3 * (1 - land) + 0.6 * sell)
        w = smooth((k - 3.1) / 0.9) if k > 3.1 else 0.0
        mat_h, mat_e = p.hands, p.elbows
        p.hands = {s_: (lambda S, s_=s_: lerp(hang[s_][0](S), mat_h[s_][0](S), w), hang[s_][1].lerp(mat_h[s_][1], w),
                        hang[s_][2].lerp(mat_h[s_][2], w)) for s_ in "lr"}
        if w < 1.0:
            p.elbows = {s_: (lambda S, s_=s_: (S[s_] + Vector((0.3 if s_ == "l" else -0.3, 0.0, -0.2))).lerp(mat_e[s_](S), w))
                        for s_ in "lr"}
        p.fist = {"l": 0.1 + 0.05 * w, "r": 0.1 + 0.05 * w}
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
    p.pitch = 18.0 + 22.0 * scoop - 34.0 * lift + 34.0 * slam      # з вагою на руках — відхиляється назад
    p.yaw = -10.0 * lift
    p.head_pitch = -5.0
    p.forced = set()
    under = smooth(k - 1.0) if k > 1.0 else 0.0                   # з підйомом кисті переходять під тіло B
    grab = smooth(min(1.0, t / 0.32))                             # 2 передчуття: руки йдуть до хватки, а не стрибають
    g0 = {"l": Vector((-0.07, -0.27, -0.2)), "r": Vector((0.07, -0.27, -0.2))}
    p.hands = {
        # права — між ніг B під стегно, ліва — за плече B, у підйомі — під лопатки (B лежить на передпліччях)
        "r": (lambda S: lerp(S["r"] + g0["r"], P(S, "thigh_r", 0.3, Vector((0.0, 0.0, -0.07 - 0.03 * under))) or S["r"], grab), FWD, UP),
        "l": (lambda S: lerp(S["l"] + g0["l"], P(S, "spine_03", 0.5, Vector((0.0, 0.0, 0.08 - 0.18 * under))) or S["l"], grab), FWD + UP * 0.3, UP),
    }
    rel = smooth((slam - 0.35) / 0.5) if slam > 0.35 else 0.0      # відпускає B плавно, кисті йдуть за ним униз
    if rel > 0.0:
        held = p.hands
        free = {"r": S_off((0.05, -0.35, -0.35), "r"), "l": S_off((-0.05, -0.35, -0.35), "l")}
        p.hands = {s_: (lambda S, s_=s_: lerp(held[s_][0](S), free[s_](S), rel), held[s_][1].lerp(DOWN, rel),
                        held[s_][2].lerp(FWD, rel)) for s_ in "lr"}
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
    pinned = smooth((t - 0.6) / 0.3) if t > 0.6 else 0.0
    grow = min(1.0, max(0.0, (t - 0.6) / 1.4))                      # чим ближче «три», тим відчайдушніше
    w = math.sin(2 * math.pi * t / (0.7 - 0.25 * grow))
    struggle = pinned * (1 - kick) * (0.4 + 0.6 * grow)
    p = lying(sit=0.45 * kick + 0.08 * struggle * max(0.0, w), knee_bend=0.2 + 0.3 * kick + 0.3 * struggle * (0.5 + 0.5 * w))
    p.roll = 8.0 * struggle * w
    p.head_yaw = 25.0 * struggle * math.sin(2 * math.pi * t / 0.9)
    arms_on_mat(p, Quaternion(UP, 0.0) @ Q_BACK, lift_l=0.45 * kick + 0.4 * struggle * max(0.0, w),
                lift_r=0.27 * kick + 0.4 * struggle * max(0.0, -w))
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

# --- удар передпліччям (будова прийому — docs/research/RING_ANIM_REFERENCES.md §4–5): A — замах (правий лікоть назад,
# крок лівою вперед) → контакт передпліччям поперек грудей B → супровід → повернення в стійку; B — стоїть → удар
# (груди й голова відкидаються) → «продаж»: крок назад, руки розлітаються → повернення в стійку. Обидва — від стійки
# до стійки, тож удар ставиться між будь-якими кліпами зі стійкою. Кадр контакту — мітка в ring_anims_events.txt.
STRIKE_LEN = 2.0          # с — з повним «продажем» і поверненням у стійку
STRIKE_HIT = 0.42          # с — контакт (мітка «contact»)
STRIKE_GAP = 0.8           # м — тази на старті

def S_off(v, s):
    """Ціль кисті: точка плеча s + зсув v (простір ригу)."""
    v = Vector(v)
    return lambda S: S[s] + v

def _phase(t, keys):
    """Ваги фаз [(час, (w0, w1, …))] — лінійно між ключами, з м'яким входом і виходом."""
    if t <= keys[0][0]:
        return keys[0][1]
    for (t0, a), (t1, b) in zip(keys, keys[1:]):
        if t <= t1:
            k = smooth((t - t0) / (t1 - t0)) if t1 > t0 else 1.0
            return tuple(x + (y - x) * k for x, y in zip(a, b))
    return keys[-1][1]

def clip_forearm_a(t, L, partner=None):
    p = stance(t, L, sway=0.0)
    # крок лівою вперед під удар і назад у стійку
    p.ankles = steps(t, [(0.0, (0.2, -0.105)), (0.14, (0.2, -0.105)), (0.34, (0.18, -0.32)), (0.85, (0.18, -0.32)),
                         (1.12, (0.2, -0.105))],
                        [(0.0, (-0.2, 0.075)), (0.3, (-0.2, 0.075)), (0.46, (-0.21, 0.0)), (1.02, (-0.21, 0.0)),
                         (1.3, (-0.2, 0.075))])                         # задня підтягується за вагою
    fwd = track(t, [(0.0, 0.0), (0.16, 0.0), (0.4, 1.0), (0.85, 1.0), (1.18, 0.0)])
    # замах (+1) → удар (−1) → супровід → стійка: тулуб повертається навколо вертикалі
    wind = track(t, [(0.0, 0.0), (0.16, 0.0), (0.32, 1.0), (STRIKE_HIT, -0.7), (0.58, -0.9), (1.25, 0.0)])
    p.pelvis = p.pelvis + Vector((0.02 * wind, -0.1 * fwd, -0.02 * fwd))
    p.yaw = -24.0 * wind
    p.pitch = 16.0 + 8.0 * fwd
    p.head_pitch = -12.0
    p.head_yaw = 10.0 * wind
    w_up, w_hit, w_fol = _phase(t, [(0.0, (0, 0, 0)), (0.12, (0, 0, 0)), (0.27, (1, 0, 0)), (STRIKE_HIT, (0, 1, 0)),
                                    (0.6, (0, 0, 1)), (1.25, (0, 0, 0))])
    guard_r = Vector((0.07, -0.27, -0.2))
    def hand_r(S):
        g = S["r"] + guard_r
        up = S["r"] + Vector((-0.2, 0.06, 0.04))                        # лікоть назад-угору, кулак біля вуха
        hit = P(S, "spine_03", 0.7, Vector((0.16, 0.06, 0.02))) or (S["r"] + Vector((0.1, -0.55, -0.05)))
        fol = S["r"] + Vector((0.26, -0.36, -0.14))                     # рука пройшла крізь ціль і пішла далі
        rest = max(0.0, 1.0 - w_up - w_hit - w_fol)
        return g * rest + up * w_up + hit * w_hit + fol * w_fol
    act = min(1.0, w_up + w_hit + w_fol)                                 # ліва рука й лікті — як у стійці, поза ударом
    p.forced = set()
    _, dir_r, up_r = p.hands["r"]
    p.hands = {"r": (hand_r, dir_r.lerp(FWD + LEFT * 0.6, act), up_r.lerp(UP, act)), "l": p.hands["l"]}
    el_r = p.elbows["r"]
    p.elbows = {"r": lambda S: el_r(S).lerp(S["r"] + Vector((-0.28 + 0.3 * w_hit + 0.36 * w_fol, 0.12 * w_up - 0.18 * w_hit,
                                                                 -0.3 + 0.18 * w_up)), act),
                "l": p.elbows["l"]}
    p.fist = {"l": 0.25, "r": 0.25 + 0.6 * act}
    return p

def hit_spring(t, t_hit, w=15.0, z=0.32, delay=0.0):
    """Реакція на удар (docs/research/RING_ANIM_REFERENCES.md §2, §5): пружний поштовх у мить контакту, що гасне з
    перехльостом — тіло відкидає, воно повертається трохи далі за рівновагу й заспокоюється. 0 до контакту, пік ≈ 1."""
    u = t - t_hit - delay
    if u <= 0.0:
        return 0.0
    return math.exp(-z * w * u) * math.sin(w * u) / 0.62

def clip_forearm_b(t, L, partner=None):
    # Реакція того, кого б'ють, у три шари: (1) пружний поштовх тулуба й голови проти удару — голова з запізненням,
    # «хлист»; (2) хитання: два кроки назад, коліна просідають; (3) «продаж»: зігнувся, права долоня на грудях, ліва
    # рука шукає рівновагу, вирівнюється й повертається в стійку.
    p = stance(t, L, sway=0.0)
    th = STRIKE_HIT
    spr = hit_spring(t, th, delay=0.035)                                # віддача — на кадр після контакту
    whip = hit_spring(t, th, w=13.0, z=0.28, delay=0.07)                # голова відстає від грудей
    sell = track(t, [(0.0, 0.0), (th + 0.3, 0.0), (0.95, 1.0), (1.45, 1.0), (1.9, 0.0)])
    back = track(t, [(0.0, 0.0), (th, 0.0), (th + 0.22, 0.55), (0.95, 1.0), (1.4, 1.0), (1.95, 0.0)])
    dip = track(t, [(0.0, 0.0), (th, 0.0), (th + 0.12, 1.0), (0.8, 0.5), (1.45, 0.4), (1.9, 0.0)])
    # кроки: права назад одразу від удару, ліва доганяє; на відновленні — так само вперед
    p.ankles = steps(t, [(0.0, (0.2, -0.105)), (th + 0.2, (0.2, -0.105)), (0.78, (0.22, 0.18)), (1.5, (0.22, 0.18)),
                         (1.8, (0.2, -0.105))],
                        [(0.0, (-0.2, 0.075)), (th + 0.02, (-0.2, 0.075)), (th + 0.26, (-0.24, 0.42)), (1.38, (-0.24, 0.42)),
                         (1.68, (-0.2, 0.075))])
    p.pelvis = p.pelvis + Vector((-0.03 * spr, 0.3 * back + 0.05 * spr, -0.07 * dip))
    p.pitch = 16.0 - 34.0 * spr + 22.0 * sell
    p.roll = -9.0 * spr - 4.0 * sell
    p.yaw = -20.0 * spr + 8.0 * sell
    p.head_pitch = -10.0 - 34.0 * whip + 14.0 * sell
    p.head_yaw = -16.0 * whip - 6.0 * sell
    p.forced = set()
    fly = max(0.0, spr)
    guard_l, guard_r = Vector((-0.07, -0.27, -0.2)), Vector((0.07, -0.27, -0.2))
    def hand_l(S):
        h = S["l"] + guard_l + Vector((0.17, 0.08, 0.01)) * fly                       # рука відлітає вбік
        bal = S["l"] + Vector((0.3, -0.12, -0.28))                                    # шукає рівновагу
        return lerp(h, bal, sell)
    def hand_r(S):
        h = S["r"] + guard_r + Vector((-0.13, 0.1, -0.04)) * fly
        chest = S["chest"] + Vector((0.05, -0.17, 0.08))                             # долоня на грудях, де влучило
        return lerp(h, chest, sell)
    p.hands = {"l": (hand_l, FWD * 0.4 + UP, RIGHT),
               "r": (hand_r, (FWD * 0.4 + UP) * (1 - sell) + (LEFT + BACK * 0.3) * sell, LEFT * (1 - sell) + UP * sell)}
    el_l, el_r = p.elbows["l"], p.elbows["r"]
    p.elbows = {"l": lambda S: el_l(S).lerp(S["l"] + Vector((0.35, 0.05, -0.3)), max(fly, sell)),
                "r": lambda S: el_r(S).lerp(S["r"] + Vector((-0.3, -0.05, -0.35)), max(fly, sell))}
    p.fist = {"l": 0.25 - 0.2 * fly, "r": 0.25 - 0.2 * max(fly, sell)}
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
    ("Forearm_Strike", STRIKE_LEN, clip_forearm_a, clip_forearm_b, False, Vector((0, -STRIKE_GAP, 0)), 180),
]

# Мітки подій у кліпах (гра читає їх із ring_anims_events.txt): контакт — кадр удару (звук, шкода, камера, реакція
# натовпу); <кліп> <подія> <час, с> <кістка A> <кістка B>.
CLIP_EVENTS = {
    "Forearm_Strike": [("contact", STRIKE_HIT, "lowerarm_r", "spine_03")],
    "Lockup": [("contact", LOCKUP_HIT, "spine_03", "spine_03")],
}
EVENTS = os.path.join(REPO, "Assets", "Art", "Animations", "ring_anims_events.txt")

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

def reach_error(rig, tag):
    """Наскільки IK не дістав до цілей зап'ясть і щиколоток: (м, кінцівка)."""
    W = rig.arm.matrix_world
    pb = rig.arm.pose.bones
    best = (0.0, "")
    for s in "lr":
        for empty, bone in (("wrist_" + s, "hand_" + s), ("ankle_" + s, "foot_" + s)):
            e = (rig.empties[empty].location - W @ pb[bone].head).length
            if e > best[0]:
                best = (e, f"{tag}.{bone}")
    return best

# ---------------------------------------------------------------- канонічні пози-вузли
# Кліп починається й закінчується в одній з поз-вузлів — тоді кліпи стикуються без змішування. Вузол не залежить від
# місця на рингу й повороту (горизонтальне положення таза і поворот навколо вертикалі беруться з кліпу).

NODE_POSES = {
    "stance": lambda: stance(0.0, 2.4),
    "lying_back": lambda: lying(),
}
NODES = {}             # вузол → базис кісток (лок. повороти) + таз у просторі ригу
BLEND_S = 0.2          # довжина доведення до вузла на краях кліпу (с)

def cap_basis(arm, cap):
    """Локальні повороти кісток (і зсув таза) з матриць пози у просторі ригу."""
    bones = arm.data.bones
    out = {}
    for pb in arm.pose.bones:
        b = bones[pb.name]
        m = cap[pb.name]
        base = (cap[b.parent.name] @ b.parent.matrix_local.inverted() @ b.matrix_local) if b.parent else b.matrix_local
        loc, rot, _ = (base.inverted() @ m).decompose()
        out[pb.name] = (loc, rot)
    return out

def swing_twist_z(q):
    """q = T(навколо Z світу) @ S: повертає (T, S)."""
    p = q.conjugated()
    proj = Vector((p.x, p.y, p.z)).dot(UP) * UP
    tw = Quaternion((p.w, proj.x, proj.y, proj.z))
    if tw.magnitude < 1e-8:
        tw = Quaternion()
    tw.normalize()
    T = tw.conjugated()
    S = T.inverted() @ q
    return T, S

def capture_node(rig, name):
    place(rig.arm, Vector(), 0)
    apply_w(rig, NODE_POSES[name]())
    cap = {pb.name: pb.matrix.copy() for pb in rig.arm.pose.bones}
    NODES[name] = (cap_basis(rig.arm, cap), cap["pelvis"].copy())

LIMB_BONES = {"upperarm_l", "upperarm_r", "lowerarm_l", "lowerarm_r", "hand_l", "hand_r",
              "thigh_l", "thigh_r", "calf_l", "calf_r", "foot_l", "foot_r", "ball_l", "ball_r"}

def set_basis(rig, basis, keep_limbs=False):
    """Поставити локальні повороти. keep_limbs — руки й ноги не чіпати: їх наново розв'яже IK до цілей кліпу."""
    for pb in rig.arm.pose.bones:
        for c in pb.constraints:
            c.mute = not keep_limbs
    for pb in rig.arm.pose.bones:
        if keep_limbs and pb.name in LIMB_BONES:
            continue
        loc, rot = basis[pb.name]
        pb.rotation_quaternion = rot
        pb.location = loc if pb.name == "pelvis" else Vector()
    bpy.context.view_layer.update()

def unmute(rig):
    for pb in rig.arm.pose.bones:
        for c in pb.constraints:
            c.mute = False

def node_gap(arm, cap, node):
    """Наскільки поза відрізняється від вузла: найбільший кут суглоба (°), нахил таза (°), висота таза (см)."""
    nb, npel = NODES[node]
    cb = cap_basis(arm, cap)
    worst = 0.0
    for n, (loc, rot) in cb.items():
        if n == "pelvis" or n.startswith(("thumb", "index", "middle", "ring", "pinky")):
            continue
        worst = max(worst, qangle(rot, nb[n][1]))
    _, s_clip = swing_twist_z(cap["pelvis"].to_quaternion())
    _, s_node = swing_twist_z(npel.to_quaternion())
    tilt = qangle(s_clip, s_node)
    dz = abs(cap["pelvis"].translation.z - npel.translation.z) * 100.0
    return worst, tilt, dz

def blend_to_node(rig, cap, node, w):
    """Поза кліпу, доведена до вузла з вагою w (1 — точно вузол). Таз: горизонталь і курс — з кліпу."""
    arm = rig.arm
    nb, npel = NODES[node]
    cb = cap_basis(arm, cap)
    out = {}
    for n, (loc, rot) in cb.items():
        out[n] = (loc, rot) if n in LIMB_BONES else (loc, rot.slerp(nb[n][1], w))
    T, s_clip = swing_twist_z(cap["pelvis"].to_quaternion())
    _, s_node = swing_twist_z(npel.to_quaternion())
    q = T @ s_clip.slerp(s_node, w)
    t = cap["pelvis"].translation.copy()
    t.z = t.z + (npel.translation.z - t.z) * w
    m = Matrix.Translation(t) @ q.to_matrix().to_4x4()
    b = arm.data.bones["pelvis"]
    loc, rot, _ = (b.matrix_local.inverted() @ m).decompose()
    out["pelvis"] = (loc, rot)
    set_basis(rig, out, keep_limbs=True)

# Вузли кліпів: роль → (з вузла, у вузол); None — поза не канонічна (перехідна, тримає партнера тощо).
# Вузли, які задає кінець кліпу (ланцюжки): кліп → роль → ім'я вузла. Зсув пари наступного кліпу береться з кінця цього.
CLIP_DEFINES = {"Lockup": {"A": "tied_A", "B": "tied_B"}}
PAIR_AT = {}           # вузол-пара → (зсув B відносно A у просторі A, поворот B °)

CLIP_NODES = {
    "Ring_Stance_Loop": {"A": ("stance", "stance")},
    "Lockup": {"A": ("stance", None), "B": ("stance", None)},
    "Headlock": {"A": ("tied_A", None), "B": ("tied_B", None)},
    "Bodyslam": {"A": ("stance", None), "B": ("stance", "lying_back")},
    "Cover": {"A": ("stance", None), "B": ("lying_back", None)},
    "Bump_Back": {"A": ("stance", "lying_back")},
    "GetUp_Back": {"A": ("lying_back", "stance")},
    "Forearm_Strike": {"A": ("stance", "stance"), "B": ("stance", "stance")},
}
# Задумані контакти тіл (A.кістка, B.кістка): у проникнення не рахуються, у звіті — окремо.
CLIP_CONTACTS = {
    "Headlock": {("upperarm_r", "head"), ("upperarm_r", "neck_01"), ("spine_03", "head"), ("spine_02", "head")},
    "Cover": {("spine_03", "spine_02"), ("spine_03", "spine_03"), ("spine_02", "spine_02"), ("spine_02", "spine_03"),
              ("upperarm_l", "spine_03"), ("upperarm_l", "spine_02"), ("spine_03", "spine_01"), ("spine_02", "spine_01")},
    "Bodyslam": {("spine_03", "thigh_l"), ("spine_03", "thigh_r"), ("spine_02", "pelvis")},
    "Forearm_Strike": {("lowerarm_r", "spine_03"), ("hand_r", "spine_03"), ("lowerarm_r", "spine_02"), ("hand_r", "spine_02"),
                       ("lowerarm_r", "upperarm_l"), ("hand_r", "upperarm_l"), ("lowerarm_r", "upperarm_r"),
                       ("hand_r", "upperarm_r")},
}

# Партія 1 (рух борця й рефері) — окремий файл, ті самі хелпери.
exec(open(os.path.join(REPO, "tools", "blender", "ring_anims_move.py"), encoding="utf-8").read())
CLIPS += MOVE_CLIPS
CLIP_NODES.update(MOVE_CLIP_NODES)
CLIP_EVENTS.update(MOVE_EVENTS)
NODE_POSES.update(MOVE_NODE_POSES)
CLIP_CONTACTS.update(MOVE_CONTACTS)
MOTION = os.path.join(REPO, "Assets", "Art", "Animations", "ring_anims_motion.txt")

# ---------------------------------------------------------------- якість

RADIUS = {"pelvis": 0.13, "spine_01": 0.13, "spine_02": 0.13, "spine_03": 0.13, "neck_01": 0.06, "head": 0.1,
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

# Найменший просвіт суглоба над матом (м): лежачи, суглоб хребта ~5 см над матом, таза ~6, центр голови ~7.
CLEAR = {"pelvis": 0.06, "spine_01": 0.05, "spine_02": 0.05, "spine_03": 0.05, "neck_01": 0.05, "head": 0.07,
         "upperarm_l": 0.04, "upperarm_r": 0.04, "lowerarm_l": 0.035, "lowerarm_r": 0.035, "hand_l": 0.02, "hand_r": 0.02,
         "thigh_l": 0.06, "thigh_r": 0.06, "calf_l": 0.045, "calf_r": 0.045, "foot_l": 0.03, "foot_r": 0.03}

HAND_BONES = {"hand_l", "hand_r", "lowerarm_l", "lowerarm_r"}
SELF_PAIRS = [(arm, body) for arm in ("lowerarm_l", "lowerarm_r", "hand_l", "hand_r")
              for body in ("pelvis", "spine_01", "spine_02")]

# Пороги §4.1 плану (docs/TRPG_DESIGN.md §11.2): у см, градусах; ривок — лише у звіт.
LIMITS = {"slide": 1.5, "pen": 3.0, "self": 2.0, "under": 0.0, "reach": 3.0, "node_deg": 3.0, "node_cm": 2.0,
          "hand_jump": 25.0}                                        # см за кадр (30 к/с) — 7,5 м/с
JERK_WARN = 6.0        # °/кадр² — пік кутового прискорення кістки

def quality(name, framesA, framesB, bases):
    q = {}
    # ковзання опорної стопи — лише стоячи
    slide, slide_at = 0.0, ""
    for who, frames in (("A", framesA), ("B", framesB)):
        if not frames:
            continue
        for s in ("foot_l", "foot_r"):
            anchor = None
            for fi, (_, w) in enumerate(frames):
                h = w[s][0]
                if h.z < 0.095 and w["pelvis"][0].z > 0.6:
                    if anchor is None:
                        anchor = h.copy()
                    d_ = (Vector((h.x, h.y, 0)) - Vector((anchor.x, anchor.y, 0))).length
                    if d_ > slide:
                        slide, slide_at = d_, "%s.%s, кадр %d" % (who, s, fi)
                else:
                    anchor = None
    q["slide"] = (slide * 100.0, slide_at)
    # проникнення двох тіл (без задуманих контактів) і найщільніший контакт
    allowed = CLIP_CONTACTS.get(name, set())
    pen, where, touch, twhere = 0.0, "", 0.0, ""
    if framesB:
        for fi, ((_, wa), (_, wb)) in enumerate(zip(framesA, framesB)):
            for na in CONTACT_BONES:
                for nb in CONTACT_BONES:
                    if na in HAND_BONES or nb in HAND_BONES:
                        continue
                    d = seg_dist(wa[na][0], wa[na][1], wb[nb][0], wb[nb][1])
                    v = RADIUS[na] + RADIUS[nb] - d
                    if (na, nb) in allowed:
                        if v > touch:
                            touch, twhere = v, f"A.{na} × B.{nb}, кадр {fi}"
                    elif v > pen:
                        pen, where = v, f"A.{na} × B.{nb}, кадр {fi}"
    q["pen"] = (max(0.0, pen) * 100.0, where)
    q["touch"] = (max(0.0, touch) * 100.0, twhere)
    # самопроникнення (передпліччя й кисті крізь свій тулуб)
    selfp, swhere = 0.0, ""
    for tag, frames in (("A", framesA), ("B", framesB)):
        for fi, (_, w) in enumerate(frames or ()):
            for a_, b_ in SELF_PAIRS:
                v = RADIUS[a_] + RADIUS[b_] - seg_dist(w[a_][0], w[a_][1], w[b_][0], w[b_][1])
                if v > selfp:
                    selfp, swhere = v, f"{tag}.{a_} × {tag}.{b_}, кадр {fi}"
    q["self"] = (max(0.0, selfp) * 100.0, swhere)
    # кістки під матом (суглоб нижче половини свого радіуса — поверхня тіла під матом)
    under, uwhere = 0.0, ""
    for tag, frames in (("A", framesA), ("B", framesB)):
        for fi, (_, w) in enumerate(frames or ()):
            for n in CONTACT_BONES:
                v = CLEAR[n] - w[n][0].z - 0.01
                if v > under:
                    under, uwhere = v, f"{tag}.{n}, кадр {fi}"
    q["under"] = (under * 100.0, uwhere)
    # телепорт кисті (власник 29.10.2026: «Руки не повинні телепортуватись»): переміщення кисті за кадр понад межу —
    # швидше за будь-який удар; ловить кисть, що перескакує до цілі або між цілями
    jump, jwh = 0.0, ""
    for tag, frames in (("A", framesA), ("B", framesB)):
        fr = frames or ()
        for fi in range(1, len(fr)):
            for hb in ("hand_l", "hand_r"):
                d_ = (fr[fi][1][hb][0] - fr[fi - 1][1][hb][0]).length
                if d_ > jump:
                    jump, jwh = d_, f"{tag}.{hb}, кадр {fi}"
    q["hand_jump"] = (jump * 100.0, jwh)
    # ривки: пік кутового прискорення локального повороту кістки
    jerk, jwhere = 0.0, ""
    for tag, seq in bases:
        for n in ("pelvis", "spine_02", "spine_03", "head", "upperarm_l", "upperarm_r", "lowerarm_l", "lowerarm_r",
                  "thigh_l", "thigh_r", "calf_l", "calf_r"):
            steps_ = [qangle(seq[i][n][1], seq[i + 1][n][1]) for i in range(len(seq) - 1)]
            for i in range(len(steps_) - 1):
                v = abs(steps_[i + 1] - steps_[i])
                if v > jerk:
                    jerk, jwhere = v, f"{tag}.{n}, кадр {i + 1}"
    q["jerk"] = (jerk, jwhere)
    return q

def qangle(a, b):
    """Кут між поворотами (°) без стрибка через знак кватерніона (q і −q — той самий поворот)."""
    d = math.degrees(a.rotation_difference(b).angle)
    return min(d, 360.0 - d)

def fails(q):
    out = []
    for k in ("slide", "pen", "self", "under", "reach", "hand_jump"):
        if k in q and q[k][0] > LIMITS[k] + 1e-6:
            out.append(k)
    if "node" in q and (q["node"][0] > LIMITS["node_deg"] or q["node"][1] > LIMITS["node_cm"]):
        out.append("node")
    return out

# ---------------------------------------------------------------- перекриття (ітерація 1 «як в АА», 29.10.2026)
# Власник: «делай тогда в блендере анимации такие как в АА играх». Живе тіло не рухається всіма ланками разом: хребет,
# шия й голова, а в кліпах без хватки — і руки, відстають від таза на частки кадру, тож рух «тече» по ланцюгу (перекриття,
# гасіння інерції). Зсув лише кривих повороту верхніх ланок: ноги, таз і стопи не змінюються — опора й ковзання ті самі.
LAG_BODY = {"spine_02": 0.7, "spine_03": 1.4, "neck_01": 2.2, "head": 3.0}
LAG_ARMS = {"clavicle_l": 1.2, "clavicle_r": 1.2, "upperarm_l": 1.8, "upperarm_r": 1.8,
            "lowerarm_l": 2.8, "lowerarm_r": 2.8, "hand_l": 3.8, "hand_r": 3.8}
# кліпи, де руки тримають партнера, канат чи мат — руки без зсуву, інакше хватка «пливе»
HOLDS = ("Lockup", "Headlock", "Bodyslam", "Cover", "Forearm", "Raise", "Rope", "Corner", "Count_Pin", "Ask")

def overlap(act, clip, loop):
    import re
    lags = dict(LAG_BODY)
    if not any(h in clip for h in HOLDS):
        lags.update(LAG_ARMS)
    f0, f1 = act.frame_range
    span = f1 - f0
    for fc in act.fcurves:
        m = re.match(r'pose\.bones\["(.+)"\]\.rotation_quaternion', fc.data_path)
        if not m or m.group(1) not in lags:
            continue
        lag = lags[m.group(1)]
        ev = lambda fr: fc.evaluate(f0 + ((fr - f0) % span) if loop else max(f0, fr))
        vals = [ev(k.co[0] - lag) for k in fc.keyframe_points]
        for k, v in zip(fc.keyframe_points, vals):
            k.co[1] = k.handle_left[1] = k.handle_right[1] = v
        fc.update()

# ---------------------------------------------------------------- кліпи з лабораторії (tools/blender/ring_lab.py)
# Парні прийоми, поставлені в живому Blender ключовими позами з посадкою кистей на шкіру і перевіркою по сітці
# (власник 29.10.2026: керований риг). Файл tools/blender/lab_clips/<кліп>.json замінює дію з формули.
LAB_DIR = os.path.join(REPO, "tools", "blender", "lab_clips")

def load_lab_clips(arm, actions):
    import json
    done = set()
    if not os.path.isdir(LAB_DIR):
        return done
    for fn in sorted(os.listdir(LAB_DIR)):
        if not fn.endswith(".json"):
            continue
        clip = fn[:-5]
        data = json.load(open(os.path.join(LAB_DIR, fn), encoding="utf-8"))
        for role in ("A", "B"):
            name = f"{clip}_{role}"
            if name not in actions:
                continue
            act = bpy.data.actions.new(name + "_lab")
            act.use_fake_user = True
            arm.animation_data_create()
            arm.animation_data.action = act
            prev = {}
            for f, fr in enumerate(data[role]):
                for pb in arm.pose.bones:
                    if pb.name not in fr:
                        continue
                    loc, rot = fr[pb.name]
                    q = Quaternion(rot)
                    if pb.name in prev and prev[pb.name].dot(q) < 0:
                        q.negate()
                    prev[pb.name] = q
                    pb.rotation_quaternion = q
                    pb.keyframe_insert("rotation_quaternion", frame=f)
                    if pb.name == "pelvis":
                        pb.location = Vector(loc)
                        pb.keyframe_insert("location", frame=f)
            for fc in act.fcurves:
                for kp in fc.keyframe_points:
                    kp.interpolation = 'LINEAR'
            act.frame_range = (0, len(data[role]) - 1)
            old = actions[name]
            bpy.data.actions.remove(old)
            act.name = name
            actions[name] = act
        done.add(clip)
        print(f"[лабораторія] {clip}: {len(data['A'])} кадрів з {fn}", flush=True)
    return done

# ---------------------------------------------------------------- запуск

def bake_clip(rigA, rigB, name, length, fa, fb):
    armA, armB = rigA.arm, rigB.arm
    nodes = CLIP_NODES.get(name, {})
    frames = int(round(length * FPS))
    nblend = max(1, int(round(BLEND_S * FPS)))
    sa, sb, wa, wb, ba, bb = [], [], [], [], [], []
    reach = (0.0, "")
    gaps = []
    for f in range(frames + 1):
        solve_frame(rigA, rigB, fa, fb, f / FPS, length)
        for r_ in (reach_error(rigA, "A"), reach_error(rigB, "B") if fb is not None else (0.0, "")):
            if r_[0] > reach[0]:
                reach = (r_[0], f"{r_[1]}, кадр {f}")
        for tag, rig in (("A", rigA), ("B", rigB)):
            if tag == "B" and fb is None:
                continue
            start, end = nodes.get(tag, (None, None))
            cap = {pb.name: pb.matrix.copy() for pb in rig.arm.pose.bones}
            w = 0.0
            node = None
            if start and f < nblend:
                node, w = start, 1.0 - smooth(f / nblend)
            if end and f > frames - nblend:
                node, w = end, smooth((f - (frames - nblend)) / nblend)
            if f == 0 and start:
                gaps.append((tag, "старт", start) + node_gap(rig.arm, cap, start))
            if f == frames and end:
                gaps.append((tag, "кінець", end) + node_gap(rig.arm, cap, end))
            if node and w > 1e-4:
                blend_to_node(rig, cap, node, w)
        ca, w1 = capture_w(rigA)
        move_twist_to_forearm(armA, ca)
        sa.append(ca); wa.append((ca, w1)); ba.append(cap_basis(armA, ca))
        if fb is not None:
            cb, w2 = capture_w(rigB)
            move_twist_to_forearm(armB, cb)
            sb.append(cb); wb.append((cb, w2)); bb.append(cap_basis(armB, cb))
        unmute(rigA)
        unmute(rigB)
    q = quality(name, wa, wb, [("A", ba)] + ([("B", bb)] if fb is not None else []))
    q["reach"] = (reach[0] * 100.0, reach[1])
    # після доведення поза на краях — точно вузол; залишкова різниця — для контролю
    post = []
    for tag, arm, seq in (("A", armA, sa), ("B", armB, sb)):
        if not seq:
            continue
        start, end = nodes.get(tag, (None, None))
        if start:
            post.append(node_gap(arm, seq[0], start))
        if end:
            post.append(node_gap(arm, seq[-1], end))
    if post:
        q["node"] = (max(p_[0] for p_ in post), max(p_[2] for p_ in post))
    return sa, sb, wa, wb, q, gaps

def main():
    armA, armB = load_pair()
    rigA, rigB = Rig(armA), Rig(armB)
    init_rest(armA)
    for n in NODE_POSES:
        capture_node(rigA, n)
    unmute(rigA)
    report = ["# Звіт якості анімацій рингу (tools/blender/ring_anims.py)", "",
              "Пороги: ковзання стопи ≤ %.1f см, проникнення ≤ %.1f см, самопроникнення ≤ %.1f см, під матом %.0f, "
              "IK не дістав ≤ %.1f см, вузол ≤ %.0f° / %.0f см; ривок > %.0f °/кадр² — попередження."
              % (LIMITS["slide"], LIMITS["pen"], LIMITS["self"], LIMITS["under"], LIMITS["reach"], LIMITS["node_deg"],
                 LIMITS["node_cm"], JERK_WARN), "",
              "| Кліп | Ковзання | Проникнення | Контакт | Само | Під матом | IK | Вузол | Ривок | Стан |",
              "|---|---|---|---|---|---|---|---|---|---|"]
    samples = []
    reel_specs = []
    failed = []
    motion = ["# Root motion кліпів однієї постаті (tools/blender/ring_anims.py): <кліп> <dx dy м, простір ригу: −Y — уперед> "
              "<поворот ° (+ ліворуч)> <швидкість м/с>"]
    manifest = ["# Кліпи рингу (tools/blender/ring_anims.py). <кліп> <роль A|B|-> <довжина с> <петля 0|1> "
                "<зсув B: x y z (м, простір A)> <поворот B °> <з вузла> <у вузол>"]
    for name, length, fa, fb, loop, offset, yaw in CLIPS:
        if ONLY and name not in ONLY:
            continue
        start_b = CLIP_NODES.get(name, {}).get("B", (None, None))[0]
        if fb is not None and start_b in PAIR_AT:
            offset, yaw = PAIR_AT[start_b]                         # ланцюжок: пара стоїть так, як закінчила попередній
        missing = [n_ for r_ in CLIP_NODES.get(name, {}).values() for n_ in r_ if n_ and n_ not in NODES]
        if missing:
            print(f"   [вузол] {name}: немає вузлів {missing} (запустіть разом з кліпом, що їх задає) — пропущено", flush=True)
            continue
        place(armA, Vector(), 0)
        place(armB, offset if fb is not None else Vector((0, 50, 0)), yaw if fb is not None else 0)
        sa, sb, wa, wb, q, gaps = bake_clip(rigA, rigB, name, length, fa, fb)
        for role, node_name in CLIP_DEFINES.get(name, {}).items():
            seq, arm_ = (sa, armA) if role == "A" else (sb, armB)
            if seq:
                NODES[node_name] = (cap_basis(arm_, seq[-1]), seq[-1]["pelvis"].copy())
        if fb is not None and name in CLIP_DEFINES:
            # пара наприкінці: B відносно A — у просторі кореня A (корінь = проєкція таза на мат)
            pa, pbv = wa[-1][1]["pelvis"][0], wb[-1][1]["pelvis"][0]
            rel = Vector((pbv.x - pa.x, pbv.y - pa.y, 0.0))
            for node_name in CLIP_DEFINES[name].values():
                PAIR_AT[node_name] = (rel, yaw)
        for tag, edge, node, deg, tilt, dz in gaps:
            print(f"   [вузол] {name} {tag} {edge} «{node}»: до доведення — суглоб {deg:.1f}°, нахил таза {tilt:.1f}°, "
                  f"таз {dz:.1f} см", flush=True)
        bad = fails(q)
        state = "OK" if not bad else "ПОНАД ПОРІГ: " + ", ".join(bad)
        if q["hand_jump"][0] > LIMITS["hand_jump"]:
            state += f" · кисть стрибнула {q['hand_jump'][0]:.0f} см ({q['hand_jump'][1]})"
        if q["jerk"][0] > JERK_WARN:
            state += f" · ривок {q['jerk'][0]:.1f} ({q['jerk'][1]})"
        node_s = f"{q['node'][0]:.1f}° / {q['node'][1]:.1f} см" if "node" in q else "—"
        line = (f"| {name} | {q['slide'][0]:.1f} {q['slide'][1]} | {q['pen'][0]:.1f} {q['pen'][1]} | {q['touch'][0]:.1f} {q['touch'][1]} | "
                f"{q['self'][0]:.1f} {q['self'][1]} | {q['under'][0]:.1f} {q['under'][1]} | {q['reach'][0]:.1f} {q['reach'][1]} | {node_s} | "
                f"{q['jerk'][0]:.1f} {q['jerk'][1]} | {state} |")
        print("[кліп] " + line, flush=True)
        report.append(line)
        if bad:
            failed.append(name)
        nodes = CLIP_NODES.get(name, {})
        if fb is None:
            # root motion: зсув і поворот таза від першого до останнього кадру (простір ригу, A у нулі)
            m0, m1 = sa[0]["pelvis"], sa[-1]["pelvis"]
            d = m1.translation - m0.translation
            dq = swing_twist_z(m0.to_quaternion())[0].inverted() @ swing_twist_z(m1.to_quaternion())[0]
            dyaw = math.degrees(dq.angle) * (1 if dq.z >= 0 else -1)
            motion.append(f"{name} {d.x:.3f} {d.y:.3f} {dyaw:.1f} {math.hypot(d.x, d.y) / length:.3f}")
            samples.append((name, sa, armA))
            na = nodes.get("A", (None, None))
            manifest.append(f"{name} - {length:.3f} {int(loop)} 0 0 0 0 {na[0] or '-'} {na[1] or '-'}")
            reel_specs.append((name, name, None, None, 0))
        else:
            samples.append((name + "_A", sa, armA))
            samples.append((name + "_B", sb, armB))
            o = offset
            for role in ("A", "B"):
                nr = nodes.get(role, (None, None))
                manifest.append(f"{name}_{role} {role} {length:.3f} {int(loop)} {o.x:.3f} {o.y:.3f} {o.z:.3f} {yaw} "
                                f"{nr[0] or '-'} {nr[1] or '-'}")
            reel_specs.append((name, name + "_A", name + "_B", offset, yaw))
        if LOOK:
            look_pair(name, armA, armB if fb is not None else None, sa, sb, length)
    strip_constraints(rigA)
    strip_constraints(rigB)
    # Усі дії — на риг A: експорт іде з нього, а дія пам'ятає слот ригу, на якому її записано (Blender 4.4+).
    # Дії B, записані на риг B, у FBX виходили позою спокою — жертва в грі не рухалась (знайдено 29.10.2026).
    # Пози в просторі ригу, скелети однакові — тож запис на A дає ту саму дію.
    actions = {n: write_action(armA, n, s_) for n, s_, arm in samples}
    loop_of = {c[0]: c[4] for c in CLIPS}
    lab = load_lab_clips(armA, actions)
    for n, act in actions.items():
        base = n[:-2] if n.endswith(("_A", "_B")) else n
        if base not in lab:                       # кліпи з лабораторії вже з перекриттям і посадкою кистей
            overlap(act, base, loop_of.get(base, False))
    if REELS:
        for name, an, bn, offset, yaw in reel_specs:
            reel(name, armA, armB, actions[an], actions.get(bn) if bn else None, offset, yaw)
    # Експорт — лише риг A з усіма діями: кістки однакові, дії B грають на будь-якому скелеті набору.
    for o in list(bpy.data.objects):
        if o.get("pair") == "B":
            bpy.data.objects.remove(o, do_unlink=True)
    place(armA, Vector(), 0)
    export_ring(armA)
    with open(MANIFEST, "w", encoding="utf-8", newline="\n") as fh:
        fh.write("\n".join(manifest) + "\n")
    if not ONLY:
        with open(MOTION, "w", encoding="utf-8", newline="\n") as fh:
            fh.write("\n".join(motion) + "\n")
    with open(EVENTS, "w", encoding="utf-8", newline="\n") as fh:
        fh.write("# Мітки подій кліпів рингу (tools/blender/ring_anims.py): <кліп> <подія> <час с> <кістка A> <кістка B>\n")
        for name, *_ in CLIPS:
            if ONLY and name not in ONLY:
                continue
            for ev, tt, ba, bb in CLIP_EVENTS.get(name, []):
                fh.write(f"{name} {ev} {tt:.3f} {ba} {bb}\n")
    os.makedirs(LOOK_DIR, exist_ok=True)
    with open(os.path.join(LOOK_DIR, "report.md"), "w", encoding="utf-8", newline="\n") as fh:
        fh.write("\n".join(report) + "\n")
    print("[експорт] " + OUT_FBX, flush=True)
    if failed:
        print("[якість] понад поріг: " + ", ".join(failed), flush=True)
        if STRICT:
            sys.stdout.flush()
            os._exit(3)
    else:
        print("[якість] усі кліпи в межах порогів", flush=True)

def reel(name, armA, armB, actA, actB, offset, yaw):
    """Ролик кліпу (mp4, H.264): A — червоний, B — синій, камера на три чверті, рівень підлоги видно."""
    scene = bpy.context.scene
    try:
        scene.render.engine = 'BLENDER_WORKBENCH'
    except TypeError:
        pass
    scene.display.shading.light = 'STUDIO'
    scene.display.shading.color_type = 'OBJECT'
    scene.display.shading.show_shadows = True
    scene.render.resolution_x, scene.render.resolution_y = 640, 480
    scene.render.fps = FPS
    floor = bpy.data.objects.get("reel_floor")
    if floor is None:
        me = bpy.data.meshes.new("reel_floor")
        me.from_pydata([(-4, -4, 0), (4, -4, 0), (4, 4, 0), (-4, 4, 0)], [], [(0, 1, 2, 3)])
        floor = bpy.data.objects.new("reel_floor", me)
        scene.collection.objects.link(floor)
        floor.color = (0.82, 0.82, 0.8, 1)
    cam = bpy.data.objects.get("reel_cam")
    if cam is None:
        cam = bpy.data.objects.new("reel_cam", bpy.data.cameras.new("reel_cam"))
        scene.collection.objects.link(cam)
    scene.camera = cam
    cam.data.lens = 35
    for o in bpy.data.objects:
        if o.type != 'MESH' or o is floor:
            continue
        tag = o.get("pair")
        show = ("body" in o.name or "low-poly" in o.name or "trousers" in o.name) and (tag == "A" or actB is not None)
        o.hide_viewport = not show
        o.hide_render = not show
        o.color = (0.8, 0.25, 0.2, 1) if tag == "A" else (0.25, 0.4, 0.85, 1)
    place(armA, Vector(), 0)
    armA.animation_data.action = actA
    mid = Vector((0, 0, 0.9))
    if actB is not None:
        place(armB, offset, yaw)
        armB.animation_data_create()
        armB.animation_data.action = actB
        mid = Vector((offset.x * 0.5, offset.y * 0.5, 0.9))
    else:
        place(armB, Vector((0, 50, 0)), 0)
    d = Vector((3.4, -3.8, 1.9))
    cam.location = mid + d
    cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
    scene.frame_start = 0
    scene.frame_end = int(actA.frame_range[1])
    scene.render.image_settings.file_format = 'FFMPEG'
    scene.render.ffmpeg.format = 'MPEG4'
    scene.render.ffmpeg.codec = 'H264'
    scene.render.ffmpeg.constant_rate_factor = 'MEDIUM'
    out = os.path.join(LOOK_DIR, "reels")
    os.makedirs(out, exist_ok=True)
    scene.render.filepath = os.path.join(out, name + ".mp4")
    bpy.ops.render.render(animation=True)
    scene.render.image_settings.file_format = 'PNG'
    print(f"[ролик] {name} → {scene.render.filepath}", flush=True)

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
            c_ = sa[i]["pelvis"].translation if armB is None else Vector()
            cam.location = Vector(loc) + Vector((c_.x, c_.y + 0.3, 0.0))
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
