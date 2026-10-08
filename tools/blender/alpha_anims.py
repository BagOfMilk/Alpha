# Власні бойові кліпи на скелеті набору персонажів (Поправка №18: власна генерація — права наші, без атрибуції).
# Власник 08.10.2026: «лук стріляє кліпом пістоля… Це погано і так не повинно буть». У бібліотеці UAL (CC0)
# немає лука, рушниці до плеча, списа, удару сокирою згори й здачі навколішки — ці пози задано тут кодом:
# цілі зап'ясть і щиколоток (IK з полюсами ліктів і колін), поворот таза, тулуба й голови, стиск пальців.
# Кожен кадр розв'язується в Blender і запікається в FK; усі кліпи — один FBX у Assets/Art/Animations
# (Unity бере його як Humanoid, як і UAL).
#
#   "C:\Program Files\Blender Foundation\Blender 4.5\blender.exe" -b --python tools/blender/alpha_anims.py -- <репо> [--look]
#
# --look — ще й кадри кожного кліпу збоку й спереду в Logs/anims (огляд до експорту).
#
# Осі ригу набору: постать дивиться в −Y, ліва рука — +X, вгору — +Z (метри). Зброя тримається так, як її
# змодельовано в alpha_wardrobe.py: центр долоні = зап'ястя + передпліччя × 0,07, зброя виходить з кулака в
# бік великого пальця («up» кисті), лук — плечима вздовж «up», спиною до цілі вздовж «fore».
import bpy, math, os, sys
from mathutils import Vector, Matrix, Quaternion

R = sys.argv[sys.argv.index("--") + 1] if "--" in sys.argv else os.getcwd()
LOOK = "--look" in sys.argv
FPS = 30
OUT_FBX = os.path.join(R, "Assets", "Art", "Animations", "ALPHA_Weapons.fbx")
LOOK_DIR = os.path.join(R, "Logs", "anims")

FWD, BACK = Vector((0, -1, 0)), Vector((0, 1, 0))
LEFT, RIGHT = Vector((1, 0, 0)), Vector((-1, 0, 0))
UP, DOWN = Vector((0, 0, 1)), Vector((0, 0, -1))

# ---------------------------------------------------------------- скелет

def load_rig():
    bpy.ops.wm.read_homefile(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=os.path.join(R, "Assets", "Art", "Characters", "Kit", "kit_m.fbx"),
                             primary_bone_axis='Y', secondary_bone_axis='X', automatic_bone_orientation=False,
                             ignore_leaf_bones=False)
    arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
    arm.name = "Armature"
    for o in bpy.data.objects:
        if o.type == 'MESH':
            o.hide_viewport = True          # без сіток розв'язування кадру — миттєве
    bpy.context.view_layer.objects.active = arm
    for pb in arm.pose.bones:
        pb.rotation_mode = 'QUATERNION'
    return arm

def rest(arm, name):
    return arm.data.bones[name].matrix_local.copy()

def head_rest(arm, name):
    return arm.data.bones[name].head_local.copy()

class Rig:
    """Довідка ригу: рамки хвату кистей, осі пальців, IK-цілі."""
    def __init__(self, arm):
        self.arm = arm
        b = arm.data.bones
        self.grip0 = {}
        for s in "lr":
            fore = (b["hand_" + s].head_local - b["lowerarm_" + s].head_local).normalized()
            th = b["thumb_01_" + s].head_local - b["hand_" + s].head_local
            up = (th - fore * th.dot(fore)).normalized()
            self.grip0[s] = (fore, up)                       # рамка хвату в спокої (як _grip у гардеробі)
        # Пальці: шарнір — лінія кісточок (вказівний → мізинець), знак — щоб кінчик ішов до долоні.
        self.finger_axis = {}
        for s in "lr":
            hinge = (b["pinky_01_" + s].head_local - b["index_01_" + s].head_local).normalized()
            palm = b["thumb_01_" + s].head_local
            tip, base = b["index_02_" + s].tail_local, b["index_01_" + s].head_local
            sign = 1.0
            q = Quaternion(hinge, math.radians(60))
            if ((base + q @ (tip - base)) - palm).length > (tip - palm).length:
                sign = -1.0
            for f in ("index", "middle", "ring", "pinky"):
                for k in ("01", "02", "03"):
                    n = f"{f}_{k}_{s}"
                    if n in b:
                        self.finger_axis[n] = (b[n].matrix_local.to_3x3().inverted() @ (hinge * sign)).normalized()
            for k in ("01", "02", "03"):
                n = f"thumb_{k}_{s}"
                if n in b:
                    self.finger_axis[n] = (b[n].matrix_local.to_3x3().inverted() @ (hinge * sign * 0.5)).normalized()
        self.empties = {}
        for n in ("wrist_l", "wrist_r", "elbow_l", "elbow_r", "ankle_l", "ankle_r", "knee_l", "knee_r"):
            e = bpy.data.objects.new("ik_" + n, None)
            e.rotation_mode = 'QUATERNION'
            bpy.context.scene.collection.objects.link(e)
            self.empties[n] = e
        pb = arm.pose.bones
        self.iks = {}
        for s in "lr":
            c = pb["lowerarm_" + s].constraints.new('IK')
            c.target = self.empties["wrist_" + s]; c.pole_target = self.empties["elbow_" + s]; c.chain_count = 2
            self.iks["arm_" + s] = c
            c = pb["hand_" + s].constraints.new('COPY_ROTATION')
            c.target = self.empties["wrist_" + s]
            c = pb["calf_" + s].constraints.new('IK')
            c.target = self.empties["ankle_" + s]; c.pole_target = self.empties["knee_" + s]; c.chain_count = 2
            self.iks["leg_" + s] = c
            c = pb["foot_" + s].constraints.new('COPY_ROTATION')
            c.target = self.empties["ankle_" + s]
        self._tune_poles()

    def _tune_poles(self):
        """Кут полюса залежить від крену кісток — підбираємо той, з яким лікоть/коліно стає ближче до полюса."""
        arm = self.arm
        for key, mid, wrist, pole, tgt, pl in (
                ("arm_l", "lowerarm_l", "wrist_l", "elbow_l", Vector((0.25, -0.35, 1.2)), Vector((0.6, 0.1, 1.0))),
                ("arm_r", "lowerarm_r", "wrist_r", "elbow_r", Vector((-0.25, -0.35, 1.2)), Vector((-0.6, 0.1, 1.0))),
                ("leg_l", "calf_l", "ankle_l", "knee_l", Vector((0.15, 0.0, 0.25)), Vector((0.15, -0.8, 0.5))),
                ("leg_r", "calf_r", "ankle_r", "knee_r", Vector((-0.15, 0.0, 0.25)), Vector((-0.15, -0.8, 0.5)))):
            self.reset()
            self.empties[wrist].location = tgt
            self.empties[pole].location = pl
            for e in ("wrist_l", "wrist_r", "ankle_l", "ankle_r"):
                if e != wrist:
                    self.empties[e].location = self._rest_end(e)
            best = None
            for deg in (0, 90, -90, 180):
                self.iks[key].pole_angle = math.radians(deg)
                bpy.context.view_layer.update()
                d = (arm.pose.bones[mid].head - pl).length
                if best is None or d < best[0]:
                    best = (d, deg)
            self.iks[key].pole_angle = math.radians(best[1])
            print(f"[полюс] {key}: {best[1]}°", flush=True)

    def _rest_end(self, e):
        bone = {"wrist_l": "hand_l", "wrist_r": "hand_r", "ankle_l": "foot_l", "ankle_r": "foot_r"}[e]
        return head_rest(self.arm, bone)

    def reset(self):
        for pb in self.arm.pose.bones:
            pb.rotation_quaternion = Quaternion()
            pb.location = Vector()
        for e in ("wrist_l", "wrist_r", "ankle_l", "ankle_r"):
            bone = {"wrist_l": "hand_l", "wrist_r": "hand_r", "ankle_l": "foot_l", "ankle_r": "foot_r"}[e]
            self.empties[e].location = head_rest(self.arm, bone)
            self.empties[e].rotation_quaternion = rest(self.arm, bone).to_quaternion()

# ---------------------------------------------------------------- поза кадру

def ortho(fore_hint, up):
    up = up.normalized()
    f = fore_hint - up * fore_hint.dot(up)
    if f.length < 1e-4:
        f = Vector((0, -up.z, up.y)) if abs(up.x) < 0.9 else Vector((0, 1, 0))
    return f.normalized(), up

def knuckles(axis):
    """Кисть, з якої зброя виходить уздовж axis (великий палець): кісточки — axis, повернута на 90° у
    площині замаху (вісь X). Тримати меч угору — кісточки вперед; рушницю вперед — кісточки вниз."""
    a = axis.normalized()
    return ortho(Vector((a.x * 0.0, -a.z, a.y)), a)

def hand_rotation(rig, side, fore, up):
    """Світовий поворот кисті, з яким рамка хвату (fore0, up0) спокою стає (fore, up)."""
    f0, u0 = rig.grip0[side]
    f1, u1 = ortho(fore, up)
    b0 = Matrix((f0, u0, f0.cross(u0))).transposed()
    b1 = Matrix((f1, u1, f1.cross(u1))).transposed()
    rw = b1 @ b0.transposed()
    return (rw @ rest(rig.arm, "hand_" + side).to_3x3()).to_quaternion()

def wrist_for_grip(grip, fore):
    """Зап'ястя, з яким центр долоні опиняється в grip."""
    return grip - fore.normalized() * 0.07

class Pose:
    """Опис кадру: усе, чого не задано, лишається в спокої."""
    def __init__(self, **kw):
        self.pelvis = Vector()          # зсув таза (м)
        self.pelvis_yaw = 0.0           # поворот таза навколо вертикалі (°, + — ліворуч)
        self.pelvis_pitch = 0.0         # нахил таза вперед (°)
        self.yaw = 0.0                  # поворот тулуба поверх таза (°, розподіл по хребцях)
        self.pitch = 0.0                # нахил тулуба вперед (°)
        self.roll = 0.0                 # нахил убік (°, + — ліворуч)
        self.head_yaw = None            # None — дивитись уперед (−Y), скільки б не повернувся тулуб
        self.head_pitch = 0.0           # кивок (°, + — униз)
        self.feet = (0.16, 0.0)         # (половина ширини, винос лівої ноги вперед)
        self.feet_yaw = (8.0, -8.0)     # розворот носків (лівий, правий; °)
        self.knees = None               # власні цілі колін (полюси)
        self.ankles = None              # власні цілі щиколоток (l, r) і повороти стоп
        self.foot_rot = None
        self.hands = {}                 # side -> (grip|callable, fore, up)
        self.elbows = {}                # side -> полюс ліктя (точка або callable)
        self.fist = {"l": 0.35, "r": 0.35}
        for k, v in kw.items():
            setattr(self, k, v)

def apply(rig, p):
    arm, pb = rig.arm, rig.arm.pose.bones
    rig.reset()

    def local_rot(bone, axis_world, deg):
        ax = (rest(arm, bone).to_3x3().inverted() @ axis_world).normalized()
        return Quaternion(ax, math.radians(deg))

    # таз
    pel = pb["pelvis"]
    pel.location = rest(arm, "pelvis").to_3x3().inverted() @ p.pelvis
    pel.rotation_quaternion = local_rot("pelvis", UP, p.pelvis_yaw) @ local_rot("pelvis", RIGHT, p.pelvis_pitch)
    # тулуб: поворот, нахил і крен розкладено 30/30/40 по хребцях
    for bone, w in (("spine_01", 0.3), ("spine_02", 0.3), ("spine_03", 0.4)):
        pb[bone].rotation_quaternion = (local_rot(bone, UP, p.yaw * w) @ local_rot(bone, RIGHT, p.pitch * w)
                                        @ local_rot(bone, FWD, -p.roll * w))
    # голова: дивиться вперед (−Y) — компенсує поворот і нахил тулуба й таза
    total_yaw = p.pelvis_yaw + p.yaw
    hy = -total_yaw if p.head_yaw is None else p.head_yaw - total_yaw
    hp = p.head_pitch - p.pitch * 0.7 - p.pelvis_pitch
    for bone, w in (("neck_01", 0.45), ("head", 0.55)):
        if bone in pb:
            pb[bone].rotation_quaternion = local_rot(bone, UP, hy * w) @ local_rot(bone, RIGHT, hp * w)
    # пальці
    for name, ax in rig.finger_axis.items():
        side = name[-1]
        k = name.split("_")[1]
        amount = p.fist[side] * (70 if k == "01" else 85 if k == "02" else 60)
        if name.startswith("thumb"):
            amount *= 0.5
        pb[name].rotation_quaternion = Quaternion(ax, math.radians(amount))
    bpy.context.view_layer.update()

    # ноги: щиколотки на землі, коліна — уперед і трохи назовні
    half, split = p.feet
    yl, yr = math.radians(p.feet_yaw[0]), math.radians(p.feet_yaw[1])
    ank = p.ankles or (Vector((half, -split / 2 - 0.015, 0.071)), Vector((-half, split / 2 - 0.015, 0.071)))
    rots = p.foot_rot or (Quaternion(UP, yl), Quaternion(UP, yr))
    for s, a, q in (("l", ank[0], rots[0]), ("r", ank[1], rots[1])):
        e = rig.empties["ankle_" + s]
        e.location = a
        e.rotation_quaternion = q @ rest(arm, "foot_" + s).to_quaternion()
        knee = (p.knees[0 if s == "l" else 1] if p.knees else
                a + Vector((0.08 if s == "l" else -0.08, -0.9, 0.45)))
        rig.empties["knee_" + s].location = knee

    # руки: точки можуть залежати від уже повернутих плечей
    S = {s: pb["upperarm_" + s].head.copy() for s in "lr"}
    S["head"] = pb["head"].head.copy() if "head" in pb else pb["neck_01"].tail.copy()
    S["chest"] = pb["spine_03"].head.copy()
    for s in "lr":
        if s in p.hands:
            grip, fore, up = p.hands[s]
            grip = grip(S) if callable(grip) else grip
            fore = fore(S) if callable(fore) else fore
            up = up(S) if callable(up) else up
            f, u = ortho(fore, up)
            rig.empties["wrist_" + s].location = wrist_for_grip(grip, f)
            rig.empties["wrist_" + s].rotation_quaternion = hand_rotation(rig, s, f, u)
        else:
            # опущена рука вздовж тіла
            sh = S[s]
            side = 1 if s == "l" else -1
            g = sh + Vector((0.07 * side, -0.05, -0.5))
            f, u = ortho(DOWN + FWD * 0.2, FWD + Vector((0.3 * -side, 0, 0)))
            rig.empties["wrist_" + s].location = wrist_for_grip(g, f)
            rig.empties["wrist_" + s].rotation_quaternion = hand_rotation(rig, s, f, u)
        el = p.elbows.get(s)
        if el is None:
            el = S[s] + Vector((0.35 * (1 if s == "l" else -1), 0.25, -0.35))
        rig.empties["elbow_" + s].location = el(S) if callable(el) else el
    bpy.context.view_layer.update()

# ---------------------------------------------------------------- час

def smooth(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)

def track(t, keys):
    """Ключі [(час, значення)], згладжена інтерполяція; значення — число, Vector або кортеж."""
    if t <= keys[0][0]:
        return keys[0][1]
    for (t0, a), (t1, b) in zip(keys, keys[1:]):
        if t <= t1:
            k = smooth((t - t0) / (t1 - t0)) if t1 > t0 else 1.0
            return lerp(a, b, k)
    return keys[-1][1]

def lerp(a, b, k):
    if isinstance(a, tuple):
        return tuple(lerp(x, y, k) for x, y in zip(a, b))
    return a + (b - a) * k

def breathe(t, length, amp=1.0):
    return amp * math.sin(2 * math.pi * t / length)

# ---------------------------------------------------------------- кліпи

def stance_ready(split=0.22, half=0.15, crouch=0.035):
    return dict(feet=(half, split), pelvis=Vector((0, 0.0, -crouch)), feet_yaw=(6.0, -18.0))

# --- лук (у лівій руці; права тягне тятиву)

BOW_AIM_YAW = -72.0   # тулуб боком до цілі: ліве плече вперед

def bow_rest_hands():
    return {
        "l": (lambda S: S["l"] + Vector((0.10, -0.16, -0.50)),
              DOWN * 0.6 + FWD * 0.8, FWD * 0.55 + UP * 0.83),
        "r": (lambda S: S["r"] + Vector((-0.07, -0.06, -0.52)), DOWN + FWD * 0.2, FWD + LEFT * 0.3),
    }

def bow_drawn(draw, hold_l=0.47):
    """Ліва рука вперед до цілі, лук вертикально (легкий нахил), права — на відстані draw позаду руків'я."""
    def grip_l(S):
        return S["l"] + FWD * hold_l + UP * 0.03
    return {
        "l": (grip_l, FWD + RIGHT * 0.05, UP + RIGHT * 0.12),
        "r": (lambda S: grip_l(S) + BACK * draw + UP * 0.07 + RIGHT * 0.015, FWD + LEFT * 0.5, UP),
    }

def bow_draw_elbows(S):
    return {"l": S["l"] + Vector((0.25, 0.0, -0.35)), "r": S["r"] + Vector((-0.15, 0.45, 0.10))}

def clip_bow_idle(t, L):
    return Pose(**stance_ready(0.12, 0.15, 0.02), hands=bow_rest_hands(), pitch=breathe(t, L, 0.8),
                yaw=-8.0, fist={"l": 0.9, "r": 0.25})

def clip_bow_aim(t, L):
    hands = bow_drawn(0.42)
    p = Pose(**stance_ready(0.30, 0.17, 0.03), pelvis_yaw=-28.0, yaw=BOW_AIM_YAW + 28.0 + breathe(t, L, 0.8),
             pitch=breathe(t, L, 0.6), hands=hands, fist={"l": 0.95, "r": 0.55})
    p.elbows = {"l": lambda S: S["l"] + Vector((0.25, 0.0, -0.35)), "r": lambda S: S["r"] + Vector((-0.15, 0.45, 0.10))}
    return p

BOW_SHOOT_LEN = 1.6
BOW_RELEASE = 0.86

def clip_bow_shoot(t, L):
    rest_h = bow_rest_hands()
    k_raise = track(t, [(0.0, 0.0), (0.32, 1.0), (1.10, 1.0), (1.55, 0.0)])
    draw = track(t, [(0.0, 0.05), (0.32, 0.12), (0.70, 0.66), (BOW_RELEASE, 0.66), (BOW_RELEASE + 0.05, 0.76),
                     (1.10, 0.74), (1.45, 0.3)])
    follow = track(t, [(0.0, 0.0), (BOW_RELEASE, 0.0), (BOW_RELEASE + 0.06, 1.0), (1.10, 0.6), (1.5, 0.0)])
    drawn = bow_drawn(draw, 0.47 + 0.03 * follow)

    def blend(side):
        g0, f0, u0 = rest_h[side]
        g1, f1, u1 = drawn[side]
        return (lambda S: lerp(g0(S), g1(S), k_raise), lerp(f0, f1, k_raise), lerp(u0, u1, k_raise))

    p = Pose(**stance_ready(lerp(0.12, 0.30, k_raise), lerp(0.15, 0.17, k_raise), 0.03),
             pelvis_yaw=-28.0 * k_raise, yaw=lerp(-8.0, BOW_AIM_YAW + 28.0, k_raise),
             hands={"l": blend("l"), "r": blend("r")},
             fist={"l": 0.95, "r": lerp(0.3, 0.6, k_raise) * (1 - 0.6 * follow)})
    p.elbows = {
        "l": lambda S: S["l"] + lerp(Vector((0.35, 0.25, -0.35)), Vector((0.25, 0.0, -0.35)), k_raise),
        "r": lambda S: S["r"] + lerp(Vector((-0.35, 0.25, -0.35)), Vector((-0.15, 0.45, 0.10)), k_raise),
    }
    return p

# --- рушниця (у правій руці за шийку ложі; ліва — під цівкою)

RIFLE_YAW = -45.0

def rifle_hands(k_aim, recoil=0.0):
    """k_aim 0 — навскіс перед тілом (готовність), 1 — приклад у плечі, ствол до цілі."""
    def barrel(S):
        low = (FWD * 0.75 + UP * 0.45 + LEFT * 0.35).normalized()
        aim = (FWD + UP * 0.09 * recoil).normalized()
        return lerp(low, aim, k_aim).normalized()

    def grip_r(S):
        low = S["r"] + Vector((0.06, -0.24, -0.40))
        pocket = S["r"] + FWD * 0.10 + LEFT * 0.04 + UP * 0.09 + BACK * 0.035 * recoil
        aim = pocket + barrel(S) * 0.38 + UP * 0.03
        return lerp(low, aim, k_aim)

    def fore_r(S):
        return knuckles(barrel(S))[0]

    def grip_l(S):
        b = barrel(S)
        f = knuckles(b)[0]                       # «униз» рушниці
        return grip_r(S) + b * lerp(0.36, 0.27, k_aim) - f * 0.045 + f * 0.03

    return {
        "r": (grip_r, fore_r, barrel),
        "l": (grip_l, lambda S: RIGHT - barrel(S) * RIGHT.dot(barrel(S)), barrel),
    }

def rifle_pose(t, k_aim, recoil, L=None):
    p = Pose(**stance_ready(lerp(0.16, 0.26, k_aim), 0.16, 0.03),
             pelvis_yaw=-20.0 * k_aim, yaw=lerp(-15.0, RIFLE_YAW + 20.0, k_aim) + 2.0 * recoil,
             pitch=lerp(0.0, 9.0, k_aim) - 5.0 * recoil, head_pitch=lerp(0.0, 22.0, k_aim),
             roll=lerp(0.0, -6.0, k_aim),
             hands=rifle_hands(k_aim, recoil), fist={"l": 0.7, "r": 0.8})
    p.elbows = {"r": lambda S: S["r"] + lerp(Vector((-0.30, 0.20, -0.30)), Vector((-0.35, 0.05, -0.12)), k_aim),
                "l": lambda S: S["l"] + lerp(Vector((0.20, 0.10, -0.40)), Vector((0.15, -0.05, -0.45)), k_aim)}
    return p

def clip_rifle_idle(t, L):
    p = rifle_pose(t, 0.0, 0.0)
    p.pitch += breathe(t, L, 0.8)
    return p

def clip_rifle_aim(t, L):
    p = rifle_pose(t, 1.0, 0.0)
    p.pitch += breathe(t, L, 0.5)
    return p

RIFLE_SHOOT_LEN = 1.3
RIFLE_FIRE = 0.55

def clip_rifle_shoot(t, L):
    k = track(t, [(0.0, 0.0), (0.38, 1.0), (0.98, 1.0), (1.3, 0.0)])
    recoil = track(t, [(0.0, 0.0), (RIFLE_FIRE, 0.0), (RIFLE_FIRE + 0.05, 1.0), (0.85, 0.0)])
    return rifle_pose(t, k, recoil)

# --- спис (права рука за 0,55 м від п'яти древка, ліва — попереду)

def spear_hands(reach, lift=0.38):
    def axis(S):
        return (FWD + UP * lift).normalized()

    def grip_r(S):
        return S["r"] + Vector((0.05, 0.10, -0.42)) + FWD * reach

    def grip_l(S):
        return grip_r(S) + axis(S) * 0.48

    return {"r": (grip_r, lambda S: knuckles(axis(S))[0], axis),
            "l": (grip_l, lambda S: knuckles(axis(S))[0], axis)}

def spear_pose(reach, lift, lunge, twist):
    p = Pose(**stance_ready(0.36, 0.17, 0.05 + 0.06 * lunge), pelvis_yaw=-18.0, yaw=-12.0 + twist,
             pitch=4.0 + 10.0 * lunge, hands=spear_hands(reach, lift), fist={"l": 0.8, "r": 0.85})
    p.pelvis = p.pelvis + FWD * 0.10 * lunge
    p.elbows = {"r": lambda S: S["r"] + Vector((-0.30, 0.30, -0.30)), "l": lambda S: S["l"] + Vector((0.30, 0.0, -0.40))}
    return p

def clip_spear_idle(t, L):
    p = spear_pose(0.0, 0.38, 0.0, 0.0)
    p.pitch += breathe(t, L, 0.8)
    return p

SPEAR_LEN = 1.0
SPEAR_HIT = 0.42

def clip_spear_thrust(t, L):
    reach = track(t, [(0.0, 0.0), (0.24, -0.16), (SPEAR_HIT, 0.42), (0.55, 0.42), (1.0, 0.0)])
    lift = track(t, [(0.0, 0.38), (0.24, 0.30), (SPEAR_HIT, 0.10), (0.55, 0.10), (1.0, 0.38)])
    lunge = track(t, [(0.0, 0.0), (0.24, -0.2), (SPEAR_HIT, 1.0), (0.55, 1.0), (1.0, 0.0)])
    twist = track(t, [(0.0, 0.0), (0.24, -14.0), (SPEAR_HIT, 10.0), (0.55, 10.0), (1.0, 0.0)])
    return spear_pose(reach, lift, lunge, twist)

# --- сокира (одноручна, права рука; удар згори)

def axe_hands(phase, back=0.0):
    """phase: 0 — стійка, 1 — замах за голову, 2 — удар перед собою, 3 — дотягнутий донизу; back — повернення
    з 3 просто в стійку (без проходу через замах)."""
    pts = [(Vector((-0.06, -0.24, -0.38)), (FWD * 0.5 + UP * 0.86)),
           (Vector((0.05, 0.12, 0.40)), (BACK * 0.95 + UP * 0.30)),
           (Vector((0.14, -0.52, -0.20)), (FWD * 0.70 + DOWN * 0.70)),
           (Vector((0.16, -0.42, -0.48)), (FWD * 0.25 + DOWN * 0.97))]
    i = min(int(phase), 2)
    k = phase - i
    off = lerp(lerp(pts[i][0], pts[i + 1][0], k), pts[0][0], back)
    ax = lerp(lerp(pts[i][1], pts[i + 1][1], k).normalized(), pts[0][1].normalized(), back).normalized()
    return {"r": (lambda S: S["r"] + off, knuckles(ax)[0], ax),
            "l": (lambda S: S["l"] + Vector((-0.02, -0.26, -0.20)), DOWN + FWD, FWD + RIGHT * 0.6)}

def axe_pose(phase, lean, back=0.0):
    yaw = lerp(-10.0 + 14.0 * min(phase, 1.0) - 22.0 * max(0.0, phase - 1.0), -10.0, back)
    p = Pose(**stance_ready(0.24, 0.16, 0.045 + 0.05 * max(0.0, lean)), yaw=yaw,
             pitch=6.0 + 16.0 * lean, hands=axe_hands(phase, back), fist={"l": 0.85, "r": 0.9})
    ez = lerp(0.05 * phase - 0.25, -0.25, back)
    p.elbows = {"r": lambda S: S["r"] + Vector((-0.40, 0.10, ez)), "l": lambda S: S["l"] + Vector((0.3, 0.1, -0.4))}
    return p

def clip_axe_idle(t, L):
    p = axe_pose(0.0, 0.0)
    p.pitch += breathe(t, L, 0.8)
    return p

AXE_LEN = 1.1
AXE_HIT = 0.52

def clip_axe_chop(t, L):
    phase = track(t, [(0.0, 0.0), (0.36, 1.0), (AXE_HIT, 2.0), (0.66, 3.0)])
    back = track(t, [(0.0, 0.0), (0.70, 0.0), (1.1, 1.0)])
    lean = track(t, [(0.0, 0.0), (0.36, -0.35), (AXE_HIT, 1.0), (0.70, 1.0), (1.1, 0.0)])
    return axe_pose(phase, lean, back)

# --- здача: навколішки, руки вгору, голова опущена

def clip_surrender(t, L):
    tremble = 0.006 * math.sin(2 * math.pi * t * 6 / L)
    kz = 0.06
    knees_l, knees_r = Vector((0.13, -0.10, kz)), Vector((-0.13, -0.10, kz))
    ank_l, ank_r = knees_l + BACK * 0.42 + Vector((0, 0, 0.02)), knees_r + BACK * 0.42 + Vector((0, 0, 0.02))
    toe_back = Quaternion(LEFT, math.radians(-110))
    p = Pose(pelvis=Vector((0, -0.07, -0.42)), pitch=10.0 + breathe(t, L, 1.0), head_pitch=22.0,
             ankles=(ank_l, ank_r), foot_rot=(toe_back, toe_back),
             knees=(knees_l + FWD * 0.6 + UP * 0.2, knees_r + FWD * 0.6 + UP * 0.2),
             fist={"l": 0.05, "r": 0.05})
    p.hands = {
        "l": (lambda S: S["l"] + Vector((0.12, -0.10, 0.36 + tremble)), UP + FWD * 0.15, RIGHT),
        "r": (lambda S: S["r"] + Vector((-0.12, -0.10, 0.36 - tremble)), UP + FWD * 0.15, LEFT),
    }
    p.elbows = {"l": lambda S: S["l"] + Vector((0.45, 0.05, -0.05)), "r": lambda S: S["r"] + Vector((-0.45, 0.05, -0.05))}
    return p

CLIPS = [
    # ім'я, довжина (с), функція, петля
    ("Bow_Idle_Loop", 2.0, clip_bow_idle, True),
    ("Bow_Aim_Loop", 2.0, clip_bow_aim, True),
    ("Bow_Shoot", BOW_SHOOT_LEN, clip_bow_shoot, False),
    ("Rifle_Idle_Loop", 2.0, clip_rifle_idle, True),
    ("Rifle_Aim_Loop", 2.0, clip_rifle_aim, True),
    ("Rifle_Shoot", RIFLE_SHOOT_LEN, clip_rifle_shoot, False),
    ("Spear_Idle_Loop", 2.0, clip_spear_idle, True),
    ("Spear_Thrust", SPEAR_LEN, clip_spear_thrust, False),
    ("Axe_Idle_Loop", 2.0, clip_axe_idle, True),
    ("Axe_Chop", AXE_LEN, clip_axe_chop, False),
    ("Surrender_Loop", 2.0, clip_surrender, True),
]

# ---------------------------------------------------------------- запікання

def capture(rig):
    return {pb.name: pb.matrix.copy() for pb in rig.arm.pose.bones}

def bake(rig, name, length, fn):
    frames = int(round(length * FPS))
    samples = []
    for f in range(frames + 1):
        apply(rig, fn(f / FPS, length))
        samples.append(capture(rig))
    return samples

def write_action(arm, name, samples):
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    arm.animation_data_create()
    arm.animation_data.action = act
    bones = arm.data.bones
    prev = {}
    for f, cap in enumerate(samples):
        for pb in arm.pose.bones:
            b = bones[pb.name]
            m = cap[pb.name]
            if b.parent:
                base = cap[b.parent.name] @ b.parent.matrix_local.inverted() @ b.matrix_local
            else:
                base = b.matrix_local
            basis = base.inverted() @ m
            loc, rot, _ = basis.decompose()
            if pb.name in prev and prev[pb.name].dot(rot) < 0:
                rot.negate()
            prev[pb.name] = rot
            pb.rotation_quaternion = rot
            pb.keyframe_insert("rotation_quaternion", frame=f)
            if pb.name in ("pelvis",):
                pb.location = loc
                pb.keyframe_insert("location", frame=f)
    for fc in act.fcurves:
        for kp in fc.keyframe_points:
            kp.interpolation = 'LINEAR'
    act.frame_range = (0, len(samples) - 1)
    return act

def strip_constraints(rig):
    for pb in rig.arm.pose.bones:
        for c in list(pb.constraints):
            pb.constraints.remove(c)
    for e in rig.empties.values():
        bpy.data.objects.remove(e, do_unlink=True)

def export(arm, actions):
    for o in bpy.context.selected_objects:
        o.select_set(False)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    arm.animation_data.action = actions[0]
    bpy.context.scene.render.fps = FPS
    os.makedirs(os.path.dirname(OUT_FBX), exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=OUT_FBX, use_selection=True, object_types={'ARMATURE'},
                             apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y',
                             add_leaf_bones=False, primary_bone_axis='Y', secondary_bone_axis='X',
                             bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
                             bake_anim_use_all_bones=True, bake_anim_force_startend_keying=True,
                             bake_anim_step=1.0, bake_anim_simplify_factor=0.0)

# ---------------------------------------------------------------- огляд (--look)

LOOK_PARTS = ("body_", "low-poly", "shirt", "trousers", "boots")
WEAPON_OF = {"Bow": "wpn_bow", "Rifle": "wpn_musket", "Spear": "wpn_spear", "Axe": "wpn_axe"}

def look(arm, actions):
    scene = bpy.context.scene
    try:
        scene.render.engine = 'BLENDER_WORKBENCH'
    except TypeError:
        pass
    scene.display.shading.light = 'STUDIO'
    scene.display.shading.color_type = 'OBJECT'
    scene.render.resolution_x, scene.render.resolution_y = 220, 300
    scene.render.film_transparent = False
    world = bpy.data.worlds.new("w"); scene.world = world
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    scene.collection.objects.link(cam); scene.camera = cam
    cam.data.type = 'ORTHO'; cam.data.ortho_scale = 2.6
    for a in actions:
        weapon = WEAPON_OF.get(a.name.split("_")[0])
        for o in bpy.data.objects:
            if o.type != 'MESH':
                continue
            short = o.name.split(".", 1)[-1]
            show = short.startswith(LOOK_PARTS) or short == weapon
            o.hide_viewport = not show; o.hide_render = not show
            o.color = (0.75, 0.2, 0.15, 1) if short == weapon else (0.85, 0.7, 0.6, 1) if short.startswith(("body_", "low-poly")) else (0.45, 0.5, 0.6, 1)
        arm.animation_data.action = a
        n = int(a.frame_range[1])
        out = os.path.join(LOOK_DIR, a.name); os.makedirs(out, exist_ok=True)
        loop = a.name.endswith("_Loop")
        for i in range(8):
            f = round(n * i / (8 if loop else 7))
            scene.frame_set(f)
            for view, loc, rot in (("side", (3.0, -0.4, 1.0), (math.pi / 2, 0, math.pi / 2)),
                                   ("front", (0.0, -3.5, 1.0), (math.pi / 2, 0, 0))):
                cam.location = loc; cam.rotation_euler = rot
                scene.render.filepath = os.path.join(out, f"{i}_{view}.png")
                bpy.ops.render.render(write_still=True)
        print(f"[огляд] {a.name}: {n} кадрів → {out}", flush=True)

# ---------------------------------------------------------------- запуск

def main():
    arm = load_rig()
    rig = Rig(arm)
    samples = [(name, length, bake(rig, name, length, fn)) for name, length, fn, loop in CLIPS]
    strip_constraints(rig)
    actions = [write_action(arm, name, s) for name, length, s in samples]
    for name, length, s in samples:
        print(f"[кліп] {name}: {length:.2f} с, {len(s)} кадрів", flush=True)
    export(arm, actions)
    print("[експорт] " + OUT_FBX, flush=True)
    if LOOK:
        look(arm, actions)

main()
