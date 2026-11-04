# Кліпи виходу борця для «Рингу» (docs/ring/gdd.md; порядок робіт власника 04.11.2026: «Вихід борця: залізання на ринг…»,
# «жести промо й залізання на ринг»): ходьба до рингу, підйом трьома сходинками на фартух, лазка між канатами в ринг,
# поза в куті з піднятими руками. Один борець, той самий скелет набору, ті самі IK-хелпери й звіт якості, що в кліпах
# RING_Moves*.fbx (tools/blender/ring_anims.py не змінюється — підвантажується текстом, як у ring_anims_promo.py).
#
#   "C:\Program Files\Blender Foundation\Blender 4.5\blender.exe" -b --python tools/blender/ring_anims_entrance.py -- <репо> [--female] [--look] [--frames N] [--strict] [--only Кліп,…]
#
# Виходи: Assets/Art/Animations/RING_Entrance.fbx (+ _F), ring_entrance[_f]_manifest.txt, ring_entrance[_f]_motion.txt,
#         Logs/ring_entrance[_f]/*.png (аркуші кадрів зі сходами, фартухом і канатами), report.md.
#
# Системи координат кліпів (простір ригу, Z вгору, борець дивиться в −Y):
#   * Entrance_Walk_Loop, Entrance_Corner_Pose — на місці, висота підлоги 0.
#   * Entrance_Climb_Steps — початок = точка якоря сходів на підлозі (anchor_steps_*), стоїть за 0,2 м до першої
#     сходинки: сходинки 0,42 м завглибшки, перша починається за 0,14 м попереду; верхи на 0,293 / 0,627 / 0,960 м; край
#     фартуха (висота 1,0) — за 1,50 м. Кінець кліпу — на фартусі, таз на 1,0 м вище старту.
#   * Entrance_Through_Ropes — початок і кінець на висоті фартуха/мату (z = 0 = MatY); площина канатів — y = 0, борець
#     починається ззовні (y > 0, боком до канатів), закінчується всередині рингу (y ≈ −0,68). Канати: z = +0,48 / +0,86 / +1,24.
import os, sys

_RA = open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "ring_anims.py"), encoding="utf-8").read()
exec(_RA.rsplit("\nmain()", 1)[0])             # усі хелпери, RPose, Rig, quality … без запуску main()

OUT_FBX = os.path.join(REPO, "Assets", "Art", "Animations", "RING_Entrance%s.fbx" % SFX)
MANIFEST = os.path.join(REPO, "Assets", "Art", "Animations", "ring_entrance%s_manifest.txt" % SFX.lower())
MOTION = os.path.join(REPO, "Assets", "Art", "Animations", "ring_entrance%s_motion.txt" % SFX.lower())
LOOK_DIR = os.path.join(REPO, "Logs", "ring_entrance" + SFX.lower())

# ---------------------------------------------------------------- допоміжне

def bump(t, a, b, c, d):
    """0 до a, плавно до 1 на [a, b], тримає до c, плавно до 0 на [c, d]."""
    if t <= a or t >= d:
        return 0.0
    if t < b:
        return smooth((t - a) / (b - a))
    if t <= c:
        return 1.0
    return 1.0 - smooth((t - c) / (d - c))

def clamp01(x):
    return max(0.0, min(1.0, x))

def sinp(s):
    return math.sin(math.pi * clamp01(s))

def arms_at_side(p, fwd_l=0.0, fwd_r=0.0, lift=0.0):
    """Руки вздовж тіла; fwd_* — винос кисті вперед (м) для розмаху, lift — підйом кисті на повному розмаху (м)."""
    k = BODY["arm"]
    p.hands = {"l": (lambda S: S["l"] + Vector((0.07, -0.03 - fwd_l, -0.5 + lift * abs(fwd_l) / 0.3)) * k, DOWN + FWD * 0.15, FWD + RIGHT * 0.3),
               "r": (lambda S: S["r"] + Vector((-0.07, -0.03 - fwd_r, -0.5 + lift * abs(fwd_r) / 0.3)) * k, DOWN + FWD * 0.15, FWD + LEFT * 0.3)}
    p.elbows = {"l": lambda S: S["l"] + Vector((0.24, 0.28, -0.3)), "r": lambda S: S["r"] + Vector((-0.24, 0.28, -0.3))}
    p.fist = {"l": 0.3, "r": 0.3}
    p.forced = set()
    return p

FOOT_REL = Vector((-0.02, 0.124, 0.063))        # щиколотка відносно м'яча стопи (кулі) у спокої

def tip_ankle(ball, yaw, tilt):
    """Щиколотка стопи, повернутої на yaw і піднятої п'ятою на tilt° навколо м'яча ball."""
    return ball + Rz(yaw) @ (Matrix.Rotation(math.radians(tilt), 3, 'X') @ FOOT_REL)

def ball_of(ankle_flat, yaw):
    return ankle_flat - Rz(yaw) @ FOOT_REL

def legs(p, l, r, knee_l=None, knee_r=None):
    """l, r = (щиколотка, поворот °, нахил п'яти °). Коліна — над носком, з пів-поворотом стопи (або задані)."""
    p.ankles = (l[0], r[0])
    p.foot_rot = tuple(Quaternion(UP, math.radians(d[1])) @ Quaternion(LEFT, math.radians(d[2])) for d in (l, r))
    p.knees = (knee_l if knee_l is not None else l[0] + Rz(l[1] * 0.5) @ Vector((0.08, -0.9, 0.45)),
               knee_r if knee_r is not None else r[0] + Rz(r[1] * 0.5) @ Vector((-0.08, -0.9, 0.45)))
    return p

PLANT_LOG = []          # на кожен кадр: (ліва стопа стоїть?, права стопа стоїть?) — для перевірки ковзання

HIP_DX = 0.109
LEG_LEN = None

def limit_pelvis(px, py, pz, ankles, margin=0.99):
    """Таз не вище, ніж дозволяє довжина ноги до кожної стопи (інакше IK тягне стопу з місця)."""
    leg = (BODY["thigh"] + BODY["calf"]) * margin
    for s, a in zip((1.0, -1.0), ankles):
        dx, dy = (px + s * HIP_DX) - a.x, (py + 0.01) - a.y
        h2 = leg * leg - dx * dx - dy * dy
        pz = min(pz, a.z + math.sqrt(max(0.0, h2)) + 0.005)
    return pz

# ---------------------------------------------------------------- ходьба до рингу

WALK_LEN, WALK_STEP = 1.0, 0.5

def clip_walk(t, L):
    """Впевнена хода: груди вперед, підборіддя вгору, руки розмахують широко й високо, плечі в протифазі до стегон,
    таз переходить на опорну ногу. Два кроки на цикл (1 цикл ≈ 1 м), корінь рухається як у Ref_Walk_Loop."""
    half = WALK_STEP / 2
    s_ = WALK_STEP * 2
    path = lambda u: ((0.0, -s_ * u), 0.0)
    fl = Vector((0.11, -half, 0.071)); fr = Vector((-0.11, half, 0.071))
    kr = [(0.0, 0.0, fr, -6.0), (0.04 * L, 0.0, fr, -6.0), (0.46 * L, 0.5, Vector((-0.11, -half, 0.071)), -6.0),
          (L, 0.5, Vector((-0.11, -half, 0.071)), -6.0)]
    kl = [(0.0, 0.0, fl, 6.0), (0.54 * L, 0.0, fl, 6.0), (0.96 * L, 1.0, fl, 6.0)]
    sw = math.sin(2 * math.pi * t / L)
    def base(t_, L_):
        p = ref_stand(t_, L_, sway=0.0)
        p.pelvis = p.pelvis + Vector((0.020 * sw, 0, -0.03))
        p.pitch = -2.0 + 1.0 * math.sin(4 * math.pi * t_ / L_ + 0.5)
        p.pelvis_yaw = 5.0 * sw                               # права стегно вперед — ліве плече вперед
        p.yaw = -11.0 * sw
        p.roll = -2.5 * sw
        p.head_pitch = -3.0
        p.head_yaw = -3.0 * sw
        arms_at_side(p, 0.30 * sw, -0.30 * sw, lift=0.10)
        p.elbows = {"l": lambda S: S["l"] + Vector((0.22, 0.30 - 0.15 * max(0, sw), -0.3)),
                    "r": lambda S: S["r"] + Vector((-0.22, 0.30 - 0.15 * max(0, -sw), -0.3))}
        return p
    PATH[0] = path
    p = locomote(t, L, base, path, kl, kr, lambda t_: t_ / L, bob=0.025)
    p.knees = (p.ankles[0] + Vector((0.05, -0.9, 0.45)), p.ankles[1] + Vector((-0.05, -0.9, 0.45)))
    return p

# ---------------------------------------------------------------- у куті: руки вгору до публіки

CORNER_LEN = 3.0

def clip_corner(t, L):
    """Стоїть у куті, руки піднято вгору й врозтіч, відкриті долоні до залу; піднімає груди, хитає плечима, руки
    «качають» по черзі, голова веде погляд по залу. Петля: усе — гармоніки циклу."""
    ph = 2 * math.pi * t / L
    p = RPose(forced=set())
    p.pelvis = Vector((0.016 * math.sin(ph), 0.0, -0.035 - 0.010 * math.sin(2 * ph + 0.3)))
    p.pitch = -4.0 + 1.0 * math.sin(2 * ph)
    p.roll = -2.0 * math.sin(ph)
    p.yaw = 4.0 * math.sin(ph + 0.8)
    p.head_pitch = -7.0 + 1.5 * math.sin(2 * ph + 0.4)
    p.head_yaw = 18.0 * math.sin(ph + 0.3)
    p.ankles = (Vector((0.15, -0.01, 0.071)), Vector((-0.15, -0.01, 0.071)))
    p.foot_rot = (Quaternion(UP, math.radians(14)), Quaternion(UP, math.radians(-14)))
    p.knees = (p.ankles[0] + Vector((0.12, -0.9, 0.45)), p.ankles[1] + Vector((-0.12, -0.9, 0.45)))
    k = BODY["arm"]
    pump_l = 0.045 * math.sin(2 * ph)
    pump_r = 0.045 * math.sin(2 * ph + math.pi)
    p.hands = {"l": (lambda S: S["l"] + Vector((0.20, -0.06, 0.45 + pump_l)) * k, UP + LEFT * 0.35 + FWD * 0.1, FWD),
               "r": (lambda S: S["r"] + Vector((-0.20, -0.06, 0.45 + pump_r)) * k, UP + RIGHT * 0.35 + FWD * 0.1, FWD)}
    p.elbows = {"l": lambda S: S["l"] + Vector((0.34, 0.12, -0.02 + pump_l)), "r": lambda S: S["r"] + Vector((-0.34, 0.12, -0.02 + pump_r))}
    p.fist = {"l": 0.12 + 0.10 * math.sin(ph), "r": 0.12 - 0.10 * math.sin(ph)}
    PLANT_LOG.append((True, True))
    return p

# ---------------------------------------------------------------- підйом трьома сходинками на фартух

TREAD_Y = [-0.35, -0.77, -1.19]                 # центри сходинок (м від якоря; вперед = −Y)
TREAD_Z = [0.293, 0.627, 0.960]                 # верхи сходинок
TREAD_D = 0.42
EDGE_Y = -1.50                                  # край фартуха (зазор 0,1 м від останньої сходинки — як у моделі арени)
APRON_Z = 1.0
CLIMB_LEN = 4.9

def env_h(y):
    """Висота опори під точкою на відстані y від якоря (сходи + фартух)."""
    if y > TREAD_Y[0] + TREAD_D / 2:
        return 0.0
    for yc, z in zip(TREAD_Y, TREAD_Z):
        if y > yc - TREAD_D / 2:
            return z
    return TREAD_Z[-1] if y > EDGE_Y else APRON_Z

class Seq:
    """Стопа: початок + кроки [(відрив, приземлення, ціль, поворот, підйом дуги)]; повертає (точка, поворот, стоїть?, s)."""
    def __init__(self, p0, y0, events):
        self.p0, self.y0, self.ev = p0, y0, events
    def at(self, t):
        pos, yaw = self.p0, self.y0
        for off, land, tgt, ty, lift in self.ev:
            if t <= off:
                return pos, yaw, True, None
            if t < land:
                s = (t - off) / (land - off)
                h = smooth(clamp01((s - 0.16) / 0.80))
                v = smooth(clamp01(s / 0.55))
                q = Vector((pos.x + (tgt.x - pos.x) * h, pos.y + (tgt.y - pos.y) * h, pos.z + (tgt.z - pos.z) * v + lift * sinp(s)))
                return q, yaw + (ty - yaw) * h, False, s
            pos, yaw = tgt, ty
        return pos, yaw, True, None

A_ = lambda x, yc, z: Vector((x, yc + 0.06, z + 0.071))          # щиколотка на сходинці/фартуху
# Крок «приставним кроком» (риз сходинки 0,33 м — завищий для чергування ніг): ліва ставиться на сходинку, права — поруч,
# і так на кожну; на фартух — так само. Час: відрив, приземлення.
CL_STEPS = [(0.40 + 0.48 * i, 0.86 + 0.48 * i) for i in range(8)]
CL_TGT = [A_(0.11, TREAD_Y[0], TREAD_Z[0]), A_(-0.11, TREAD_Y[0], TREAD_Z[0]),
          A_(0.11, TREAD_Y[1], TREAD_Z[1]), A_(-0.11, TREAD_Y[1], TREAD_Z[1]),
          A_(0.11, TREAD_Y[2], TREAD_Z[2]), A_(-0.11, TREAD_Y[2], TREAD_Z[2]),
          Vector((0.12, -1.62, 1.071)), Vector((-0.12, -1.62, 1.071))]
CL_YAW = [6.0, -6.0, 6.0, -6.0, 6.0, -6.0, 8.0, -8.0]
CL_L = Seq(Vector((0.12, 0.20, 0.071)), 8.0, [(o, l, CL_TGT[i], CL_YAW[i], 0.10) for i, (o, l) in enumerate(CL_STEPS) if i % 2 == 0])
CL_R = Seq(Vector((-0.12, 0.20, 0.071)), -8.0, [(o, l, CL_TGT[i], CL_YAW[i], 0.10) for i, (o, l) in enumerate(CL_STEPS) if i % 2 == 1])

def climb_pelvis(t):
    """Таз (x, y): на початку кроку — над опорною стопою, до приземлення — посередині між нею й новою; висота — з ніг."""
    xy = lambda a: Vector((a.x * 0.6, a.y - 0.02, 0.0))
    keys = [(0.0, Vector((0.0, 0.207, 0.0))), (0.28, Vector((-0.03, 0.20, 0.0)))]
    floor_r = Vector((-0.12, 0.20, 0.071))
    for i, (o, l) in enumerate(CL_STEPS):
        sup = floor_r if i == 0 else CL_TGT[i - 1]
        keys.append((o + 0.25, xy(sup)))
        keys.append((l - 0.04, (xy(sup) + xy(CL_TGT[i])) * 0.5))
    keys += [(4.45, Vector((0.0, -1.613, 0.0))), (CLIMB_LEN, Vector((0.0, -1.613, 0.0)))]
    return track(t, keys)

def clip_climb(t, L):
    """Підйом приставним кроком: ліва на сходинку, права поруч, і так на три сходинки й на фартух. Таз над опорною
    стопою, корпус нахилений уперед, погляд на ринг, руки розмахують проти ніг."""
    fl, yl, pl, sl = CL_L.at(t)
    fr, yr, pr, sr = CL_R.at(t)
    PLANT_LOG.append((pl, pr))
    pel = climb_pelvis(t)
    # висота таза: піднімається лише на кроках правої (задня нога відштовхується й відривається), між ними — тримається
    levels = [0.905, 0.364 + 0.80, 0.698 + 0.80, 1.031 + 0.80, 1.071 + 0.842]
    zk = [(0.0, 0.913), (0.28, 0.905)]
    for n_, k in enumerate((1, 3, 5, 7)):
        zk.append((CL_STEPS[k][0], levels[n_]))
        zk.append((CL_STEPS[k][1], levels[n_ + 1]))
    zk.append((CLIMB_LEN, 1.913))
    pz = limit_pelvis(pel.x, pel.y, track(t, zk), (fl, fr), margin=0.975)
    p = RPose(forced=set())
    p.pelvis = Vector((pel.x, pel.y, pz)) - REST["pelvis"]
    lean = bump(t, 0.30, 0.80, 3.9, 4.6)
    p.pitch = 3.0 + 12.0 * lean + 1.2 * math.sin(2 * math.pi * t / 0.72)
    swing_r = sinp(sr) if sr is not None else 0.0
    swing_l = sinp(sl) if sl is not None else 0.0
    d_ = swing_r - swing_l
    p.yaw = -6.0 * d_
    p.roll = -2.0 * d_
    p.head_pitch = -2.0 - 7.0 * lean
    arms_at_side(p, 0.16 * d_, -0.16 * d_, lift=0.05)
    legs(p, (fl, yl, 0.0), (fr, yr, 0.0))
    return p

# ---------------------------------------------------------------- лазка між середнім і верхнім канатом в ринг

THROUGH_LEN = 3.8
ROPE_Z = (0.48, 0.86, 1.24)
ROPE_R = 0.02
TIP = 40.0                                      # підйом п'ят навшпиньки (°): без нього таз не дістає висоти проходу (+7 см)
BALL_L = ball_of(Vector((0.15, 0.06, 0.071)), 80.0)
BALL_R = ball_of(Vector((-0.15, 0.06, 0.071)), -80.0)

TH_PEL = [(0.00, 0.0, 0.15, 0.913), (0.50, 0.0, 0.15, 0.913), (0.95, -0.03, 0.12, 0.965), (1.25, -0.04, 0.02, 0.985),
          (1.50, -0.03, -0.12, 0.975), (1.80, -0.02, -0.30, 0.90), (2.15, 0.0, -0.46, 0.86), (2.55, 0.0, -0.60, 0.86),
          (3.05, 0.0, -0.71, 0.905), (3.80, 0.0, -0.713, 0.913)]
TH_PITCH = [(0.0, 3.0), (0.5, 6.0), (0.95, 38.0), (1.25, 72.0), (1.50, 85.0), (1.80, 70.0), (2.15, 42.0), (2.55, 18.0),
            (3.05, 6.0), (3.8, 3.0)]
# ведуча (ліва) нога: з апрона вперед, коліно першим у проріз між середнім і верхнім канатом, потім стопа вниз у ринг.
# Ключі: (час, щиколотка, поворот °, нахил п'яти °); на носку щиколотка — з tip_ankle (м'яч стоїть).
TH_L = [(0.00, Vector((0.15, 0.06, 0.071)), 80.0, 0.0), (0.60, Vector((0.15, 0.06, 0.071)), 80.0, 0.0),
        (0.85, tip_ankle(BALL_L, 80.0, TIP), 80.0, TIP),
        (1.10, Vector((0.15, -0.25, 0.60)), 50.0, 5.0), (1.40, Vector((0.15, -0.55, 0.50)), 25.0, 0.0),
        (1.80, Vector((0.15, -0.78, 0.22)), 12.0, 0.0), (2.05, Vector((0.12, -0.72, 0.071)), 8.0, 0.0),
        (3.80, Vector((0.12, -0.72, 0.071)), 8.0, 0.0)]
# хвостова (права): стоїть навшпиньки під канатами, тягнеться низько під нижнім канатом, ставиться в ринг
TH_R = [(0.00, Vector((-0.15, 0.06, 0.071)), -80.0, 0.0), (0.60, Vector((-0.15, 0.06, 0.071)), -80.0, 0.0),
        (0.95, tip_ankle(BALL_R, -80.0, TIP), -80.0, TIP), (1.70, tip_ankle(BALL_R, -80.0, TIP), -80.0, TIP),
        (1.88, Vector((-0.15, 0.00, 0.20)), -60.0, 8.0), (2.15, Vector((-0.15, -0.40, 0.20)), -30.0, 0.0),
        (2.45, Vector((-0.15, -0.70, 0.14)), -12.0, 0.0), (2.70, Vector((-0.12, -0.72, 0.071)), -8.0, 0.0),
        (3.80, Vector((-0.12, -0.72, 0.071)), -8.0, 0.0)]

def _foot(t, keys, ball, yaw0, tip_until):
    """Щиколотка, поворот і нахил: поки стопа на носку (до відриву) — з м'яча, щоб куля не їздила; далі — по ключах."""
    tilt = track(t, [(k[0], k[3]) for k in keys])
    yaw = track(t, [(k[0], k[2]) for k in keys])
    if t <= tip_until:
        return tip_ankle(ball, yaw0, tilt), yaw0, tilt
    return track(t, [(k[0], k[1]) for k in keys]), yaw, tilt

def clip_through(t, L):
    """З фартуха в ринг: стоїть боком до канатів навшпиньки, руки на верхньому канаті; згинається в стегнах майже до
    горизонталі, голова й спина йдуть під верхній канат, коліно ведучої ноги — в проріз, хвостова нога тягнеться низько
    під нижнім. Таз проходить площину канатів на ~0,98 м."""
    pel = track(t, [(k[0], Vector(k[1:])) for k in TH_PEL])
    pitch = track(t, TH_PITCH)
    al, yl, tl_ = _foot(t, TH_L, BALL_L, 80.0, 0.85)
    ar, yr, tr_ = _foot(t, TH_R, BALL_R, -80.0, 1.70)
    pl, pr = t < 0.84 or t >= 2.05, t < 1.72 or t >= 2.70
    PLANT_LOG.append((pl, pr))
    pz = limit_pelvis(pel.x, pel.y, pel.z, (al, ar))
    p = RPose(forced=set())
    p.pelvis = Vector((pel.x, pel.y, pz)) - REST["pelvis"]
    p.pelvis_pitch = 0.55 * pitch
    p.pitch = 0.45 * pitch
    duck = bump(t, 0.9, 1.2, 1.7, 2.1)
    p.head_pitch = -4.0 + 20.0 * duck
    p.head_yaw = None
    # коліна: ведуче — вперед на рівні стегна (в проріз), решта — над носком
    kw = bump(t, 0.95, 1.15, 1.65, 1.95)
    hip = Vector((pel.x, pel.y, pz))
    kl_def = al + Rz(yl * 0.5) @ Vector((0.08, -0.9, 0.45))
    kl = kl_def.lerp(hip + Vector((0.12, -0.55, 0.02)), kw)
    legs(p, (al, yl, tl_), (ar, yr, tr_), knee_l=kl)
    # руки: обидві тримаються за верхній канат, потім відпускають і висять
    k = BODY["arm"]
    hold = bump(t, 0.30, 0.55, 0.72, 1.05)
    rope_l, rope_r = Vector((0.45, 0.0, ROPE_Z[2])), Vector((-0.45, 0.0, ROPE_Z[2]))
    def hand(side, rope, sx):
        def g(S):
            hang = S[side] + Vector((sx * (0.07 + 0.03 * duck), -0.03 - 0.30 * duck, -0.5 + 0.12 * duck)) * k
            g_ = hang.lerp(rope, hold)
            d_ = g_ - S[side]
            if d_.length > 0.46 * k:                              # рука не повністю випрямлена: інакше ліктьовий полюс перекидається
                g_ = S[side] + d_.normalized() * 0.46 * k
            return g_
        return g
    p.hands = {"l": (hand("l", rope_l, 1.0), DOWN * (1 - hold) + FWD * 0.2 + RIGHT * 0.0 * hold + LEFT * 0.0, FWD + RIGHT * 0.3),
               "r": (hand("r", rope_r, -1.0), DOWN * (1 - hold) + FWD * 0.2, FWD + LEFT * 0.3)}
    ew = max(hold, duck)                                          # руки в роботі — лікті вбік, спокій — вздовж тулуба
    wide, calm = Vector((0.40, 0.25, -0.15)), Vector((0.24, 0.28, -0.30))
    p.elbows = {"l": lambda S: S["l"] + calm.lerp(wide, ew), "r": lambda S: S["r"] + (lambda v: Vector((-v.x, v.y, v.z)))(calm.lerp(wide, ew))}
    p.fist = {"l": 0.3 + 0.5 * hold, "r": 0.3 + 0.5 * hold}
    return p

# ---------------------------------------------------------------- перевірки зібраного кліпу

def plant_slide(wa, log):
    """Ковзання опорної стопи в будь-якій висоті: м'яч стопи (хвіст кістки) стоїть, поки стопа за планом на опорі."""
    worst, where = 0.0, ""
    for si, s in enumerate(("foot_l", "foot_r")):
        anchor = None
        for fi, ((_, w), pl) in enumerate(zip(wa, log)):
            if pl[si]:
                h = w[s][1]
                if anchor is None:
                    anchor = h.copy()
                d = (h - anchor).length
                if d > worst:
                    worst, where = d, "%s, кадр %d" % (s, fi)
            else:
                anchor = None
    return worst * 100.0, where

def env_pen(wa):
    """Сходи: стопа й коліно не заходять у сходинку (м'яч і носок — підошва ≈ на 1 см нижче точки кістки)."""
    worst, where = 0.0, ""
    for fi, (_, w) in enumerate(wa):
        for s in ("l", "r"):
            pts = [(w["foot_" + s][0], 0.071), (w["foot_" + s][1], 0.012), (w["calf_" + s][0], -0.055)]
            for pt, drop in pts:
                v = env_h(pt.y) - (pt.z - drop)
                if v > worst:
                    worst, where = v, "%s, кадр %d" % (s, fi)
    return worst * 100.0, where

CORE = {"pelvis": 0.11, "spine_01": 0.10, "spine_02": 0.10, "spine_03": 0.10, "neck_01": 0.06, "head": 0.10}
LIMB = {"upperarm_l": 0.05, "upperarm_r": 0.05, "thigh_l": 0.07, "thigh_r": 0.07, "calf_l": 0.05, "calf_r": 0.05,
        "foot_l": 0.04, "foot_r": 0.04}

def seg2d(p, a, b):
    """Відстань від точки p до відрізка ab у площині YZ (канат іде вздовж X)."""
    p, a, b = Vector((p[0], p[1])), Vector((a[1], a[2])), Vector((b[1], b[2]))
    d = b - a
    t = 0.0 if d.length < 1e-9 else clamp01((p - a).dot(d) / d.dot(d))
    return (p - (a + d * t)).length

def rope_pen(wa):
    """Канати z = 0.48 / 0.86 / 1.24 над фартухом: торс і голова — не глибше 2 см у жоден; кінцівки — не глибше
    ніж на 2 см у верхній і нижній, а в середній — на 8 см (канат прогинається під стегном/коліном, його тисне нога).
    Кисті й передпліччя з перевірки виключені (тримають канат)."""
    core, limb, cw, lw = 0.0, 0.0, "", ""
    for fi, (_, w) in enumerate(wa):
        for ri, rz in enumerate(ROPE_Z):
            for n, r in CORE.items():
                v = r + ROPE_R - seg2d(Vector((0.0, 0.0, rz)), w[n][0], w[n][1])
                if v > core:
                    core, cw = v, "%s × канат %d, кадр %d" % (n, ri + 1, fi)
            for n, r in LIMB.items():
                v = r + ROPE_R - seg2d(Vector((0.0, 0.0, rz)), w[n][0], w[n][1]) - (0.08 if ri == 1 else 0.0)
                if v > limb:
                    limb, lw = v, "%s × канат %d, кадр %d" % (n, ri + 1, fi)
    return core * 100.0, cw, limb * 100.0, lw

# ---------------------------------------------------------------- огляд: аркуші кадрів зі сходами / фартухом / канатами

def make_env(kind):
    """Макет оточення для огляду: [(об'єкт, колір)]."""
    objs = []
    def box(c, size, color):
        bpy.ops.mesh.primitive_cube_add(size=1.0, location=c)
        o = bpy.context.active_object
        o.scale = size
        o.color = color
        objs.append(o)
    def rope(z, color=(0.9, 0.9, 0.9, 1)):
        bpy.ops.mesh.primitive_cylinder_add(radius=ROPE_R, depth=2.6, location=(0, 0, z), rotation=(0, math.pi / 2, 0))
        o = bpy.context.active_object
        o.color = (0.9, 0.2, 0.2, 1)
        objs.append(o)
    floor = (0.35, 0.35, 0.38, 1)
    if kind == "climb":
        box((0, 0.5, -0.02), (1.6, 1.4, 0.04), floor)
        for yc, z in zip(TREAD_Y, TREAD_Z):
            box((0, yc, z / 2), (0.95, TREAD_D, z), (0.55, 0.55, 0.6, 1))
        box((0, EDGE_Y - 1.0, APRON_Z - 0.06), (1.6, 2.0, 0.12), (0.2, 0.3, 0.7, 1))
        box((0, EDGE_Y - 1.0, APRON_Z / 2 - 0.06), (1.6, 2.0, APRON_Z - 0.12), (0.12, 0.12, 0.14, 1))
    elif kind == "ropes":
        box((0, 0.0, -0.08), (2.6, 3.4, 0.16), (0.2, 0.3, 0.7, 1))
        box((0, 0.11, -0.5), (2.6, 0.22, 0.84), (0.12, 0.12, 0.14, 1))
        for rz in ROPE_Z:
            rope(rz)
    return objs

def look_entrance(name, arm, seq, kind, ortho, K):
    import numpy as np
    scene = bpy.context.scene
    try:
        scene.render.engine = 'BLENDER_WORKBENCH'
    except TypeError:
        pass
    scene.display.shading.light = 'STUDIO'
    scene.display.shading.color_type = 'OBJECT'
    W, H = 300, 340
    scene.render.resolution_x, scene.render.resolution_y = W, H
    cam = bpy.data.objects.get("look_cam")
    if cam is None:
        cam = bpy.data.objects.new("look_cam", bpy.data.cameras.new("look_cam"))
        scene.collection.objects.link(cam)
    scene.camera = cam
    cam.data.type = 'ORTHO'
    cam.data.ortho_scale = ortho
    env = make_env(kind)
    for o in bpy.data.objects:
        if o.type != 'MESH' or o in env:
            continue
        show = ("body" in o.name or "low-poly" in o.name or "trousers" in o.name)
        o.hide_viewport = not show
        o.hide_render = not show
        o.color = (0.8, 0.45, 0.25, 1)
    if arm.animation_data:
        arm.animation_data.action = None
    for pb in arm.pose.bones:
        for c in pb.constraints:
            c.mute = True
    bones = arm.data.bones
    def set_pose(cap):
        for pb in arm.pose.bones:
            b = bones[pb.name]
            m = cap[pb.name]
            base = (cap[b.parent.name] @ b.parent.matrix_local.inverted() @ b.matrix_local) if b.parent else b.matrix_local
            loc, rot, _ = (base.inverted() @ m).decompose()
            pb.rotation_quaternion = rot
            pb.location = loc if pb.name == "pelvis" else Vector()
    n = len(seq)
    idx = [round((n - 1) * i / (K - 1)) for i in range(K)]
    views = [Vector((-5.0, 0.0, 0.0)), Vector((3.4, -3.6, 0.4))]          # збоку (з боку лівої руки) і на три чверті
    tiles = []
    for d in views:
        row = []
        for i in idx:
            set_pose(seq[i])
            bpy.context.view_layer.update()
            pv = seq[i]["pelvis"].translation
            ctr = Vector((0.0, pv.y, pv.z + 0.1))
            if kind == "ropes":
                ctr = Vector((0.0, pv.y * 0.6 - 0.1, 1.0))
            cam.location = ctr + d
            cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
            path = os.path.join(LOOK_DIR, "_tile.png")
            scene.render.filepath = path
            bpy.ops.render.render(write_still=True)
            img = bpy.data.images.load(path, check_existing=False)
            px = np.array(img.pixels[:], dtype=np.float32).reshape(H, W, 4)
            bpy.data.images.remove(img)
            row.append(px)
        COLS = 8
        while len(row) % COLS:
            row.append(np.zeros_like(row[0]))
        for r0 in range(0, len(row), COLS):
            tiles.append(np.concatenate(row[r0:r0 + COLS], axis=1))
    sheet = np.concatenate(tiles[::-1], axis=0)
    a_ = sheet[..., 3:4]
    sheet = np.concatenate([sheet[..., :3] * a_ + 0.85 * (1 - a_), np.ones_like(a_)], axis=-1)
    out = bpy.data.images.new("sheet", width=sheet.shape[1], height=sheet.shape[0], alpha=True)
    out.pixels = sheet.ravel().tolist()
    fp = os.path.join(LOOK_DIR, name + ".png")
    out.filepath_raw = fp
    out.file_format = 'PNG'
    out.save()
    bpy.data.images.remove(out)
    for o in env:
        bpy.data.objects.remove(o, do_unlink=True)
    for pb in arm.pose.bones:
        for c in pb.constraints:
            c.mute = False
    print("[огляд] " + fp, flush=True)

# ---------------------------------------------------------------- кліпи

ENTRANCE_CLIPS = [
    # ім'я, довжина, поза, петля, оточення для огляду, масштаб огляду
    ("Entrance_Walk_Loop", WALK_LEN, clip_walk, True, None, 2.4),
    ("Entrance_Climb_Steps", CLIMB_LEN, clip_climb, False, "climb", 3.2),
    ("Entrance_Through_Ropes", THROUGH_LEN, clip_through, False, "ropes", 2.8),
    ("Entrance_Corner_Pose", CORNER_LEN, clip_corner, True, None, 2.4),
]

def entrance_main():
    load_pair()
    armA = bpy.data.objects["Arm_A"]
    for o in list(bpy.data.objects):
        if o.get("pair") == "B":
            bpy.data.objects.remove(o, do_unlink=True)
    armA.matrix_world = Matrix.Identity(4)
    bpy.context.view_layer.update()
    rig = Rig(armA)
    init_rest(armA)
    unmute(rig)
    report = ["# Звіт якості кліпів виходу (tools/blender/ring_anims_entrance.py)", "",
              "Пороги: ковзання стопи ≤ %.1f см (на підлозі — перевірка рингу, на сходах/навшпиньки — за планом опори, по м'ячу стопи), "
              "самопроникнення ≤ %.1f см, під матом %.0f, IK не дістав ≤ %.1f см, кисть ≤ %.0f°, лікоть у тулубі ≤ %.0f см; "
              "канати: торс і голова ≤ 2 см у жоден, кінцівки ≤ 2 см у верхній/нижній і ≤ 8 см у середній (прогинається); "
              "сходи: стопа/коліно в сходинці ≤ 2 см." % (
                  LIMITS["slide"], LIMITS["self"], LIMITS["under"], LIMITS["reach"], LIMITS["wrist"], LIMITS["elbow_in"]), "",
              "| Кліп | Ковзання (рингу) | Ковзання опори | Само | Під матом | IK | Ривок | Кисть | Лікоть | Оточення | Стан |",
              "|---|---|---|---|---|---|---|---|---|---|---|"]
    manifest = ["# Кліпи виходу (tools/blender/ring_anims_entrance.py). <кліп> <роль A|B|-> <довжина с> <петля 0|1> "
                "<зсув B: x y z (м, простір A)> <поворот B °> <з вузла> <у вузол>"]
    motion = ["# Root motion кліпів виходу (tools/blender/ring_anims_entrance.py): <кліп> <dx dy м, простір ригу: −Y — уперед> "
              "<поворот ° (+ ліворуч)> <швидкість м/с>"]
    samples, failed, looks = [], [], []
    for name, length, fa, loop, kind, ortho in ENTRANCE_CLIPS:
        if ONLY and name not in ONLY:
            continue
        place(armA, Vector(), 0)
        PLANT_LOG.clear()
        sa, sb, wa, wb, q, gaps = bake_clip(rig, rig, name, length, lambda t, L, fa=fa: fa(t, L), None)
        slide2, sw2 = plant_slide(wa, PLANT_LOG[-len(wa):]) if len(PLANT_LOG) >= len(wa) else (0.0, "")
        envs = "—"
        extra_bad = []
        if kind == "climb":
            ep, ew = env_pen(wa)
            envs = "сходи %.1f см %s" % (ep, ew)
            if ep > 2.0:
                extra_bad.append("стопа/коліно у сходинці")
        if kind == "ropes":
            cp, cw, lp, lw = rope_pen(wa)
            envs = "торс %.1f см %s · кінцівки %.1f см %s" % (cp, cw, lp, lw)
            if cp > 2.0 or lp > 2.0:
                extra_bad.append("канат")
        if slide2 > LIMITS["slide"]:
            extra_bad.append("ковзання опори")
        if "--jerk" in _args:                                         # розбір ривків: найгірші кістки й кадри
            bs = [cap_basis(armA, c_) for c_ in sa]
            top = []
            for n_ in bs[0]:
                if n_.startswith(("thumb", "index", "middle", "ring", "pinky")):
                    continue
                st = [qangle(bs[i][n_][1], bs[i + 1][n_][1]) for i in range(len(bs) - 1)]
                for i in range(len(st) - 1):
                    top.append((abs(st[i + 1] - st[i]), n_, i + 1, st[i], st[i + 1]))
            top.sort(reverse=True)
            print("JERK", name, [(round(a_, 1), n_, f_, round(s0, 1), round(s1, 1)) for a_, n_, f_, s0, s1 in top[:8]], flush=True)
        if "--dbgarm" in _args:
            for f_ in range(int(_args[_args.index("--dbgarm") + 1]), int(_args[_args.index("--dbgarm") + 2])):
                w_ = wa[f_][1]
                r3 = lambda v: "(%.2f,%.2f,%.2f)" % tuple(v)
                print("DBGARM", name, f_, "sh", r3(w_["upperarm_l"][0]), "elb", r3(w_["lowerarm_l"][0]), "hand", r3(w_["hand_l"][0]), "spine3", r3(w_["spine_03"][0]), flush=True)
        if "--dbgleg" in _args:
            for f_ in range(int(_args[_args.index("--dbgleg") + 1]), int(_args[_args.index("--dbgleg") + 2])):
                w_ = wa[f_][1]
                row = []
                for s_ in "lr":
                    th, ca = w_["thigh_" + s_], w_["calf_" + s_]
                    kn = math.degrees((th[0] - th[1]).angle(ca[1] - ca[0]))
                    row.append("%s knee=%.0f° hip-ank=%.3f ank=(%.2f,%.2f,%.2f)" % (s_, kn, (th[0] - ca[1]).length, ca[1].x, ca[1].y, ca[1].z))
                print("DBGLEG", name, f_, "pel=%.3f,%.3f,%.3f" % tuple(w_["pelvis"][0]), " | ".join(row), flush=True)
        bad = fails(q) + extra_bad
        state = "OK" if not bad else "ПОНАД ПОРІГ: " + ", ".join(bad)
        if q["jerk"][0] > JERK_WARN:
            state += " · ривок %.1f (%s)" % q["jerk"]
        line = "| %s | %.1f %s | %.1f %s | %.1f %s | %.1f %s | %.1f %s | %.1f %s | %.0f° | %.1f см | %s | %s |" % (
            name, q["slide"][0], q["slide"][1], slide2, sw2, q["self"][0], q["self"][1], q["under"][0], q["under"][1],
            q["reach"][0], q["reach"][1], q["jerk"][0], q["jerk"][1], q["wrist"][0], q["elbow_in"][0], envs, state)
        print("[кліп] " + line, flush=True)
        report.append(line)
        if bad:
            failed.append(name)
        m0, m1 = sa[0]["pelvis"], sa[-1]["pelvis"]
        d = m1.translation - m0.translation
        dq = swing_twist_z(m0.to_quaternion())[0].inverted() @ swing_twist_z(m1.to_quaternion())[0]
        dyaw = math.degrees(dq.angle) * (1 if dq.z >= 0 else -1)
        motion.append("%s %.3f %.3f %.1f %.3f" % (name, d.x, d.y, dyaw, math.hypot(d.x, d.y) / length))
        motion.append("# %s: таз стартує на z=%.3f, закінчується на z=%.3f (dz=%.3f), позиція таза старт (%.3f, %.3f) → кінець (%.3f, %.3f)" % (
            name, m0.translation.z, m1.translation.z, d.z, m0.translation.x, m0.translation.y, m1.translation.x, m1.translation.y))
        manifest.append("%s - %.3f %d 0 0 0 0 - -" % (name, length, int(loop)))
        samples.append((name, sa, armA))
        looks.append((name, sa, kind, ortho))
    strip_constraints(rig)
    actions = {n: write_action(armA, n, s_) for n, s_, arm in samples}
    for n, act in actions.items():
        if n in ("Entrance_Walk_Loop", "Entrance_Corner_Pose"):               # на ходьбі й у куті — перекриття; на сходах і канатах геометрію не чіпаємо
            overlap(act, n, True)
    if LOOK:
        os.makedirs(LOOK_DIR, exist_ok=True)
        K = int(_args[_args.index("--frames") + 1]) if "--frames" in _args else 16
        for n, sa, kind, ortho in looks:
            look_entrance(n, armA, sa, kind, ortho, K)
    for o in list(bpy.data.objects):
        if o.type == 'MESH':
            bpy.data.objects.remove(o, do_unlink=True)
    place(armA, Vector(), 0)
    export_ring(armA)
    if not ONLY:
        with open(MANIFEST, "w", encoding="utf-8", newline="\n") as fh:
            fh.write("\n".join(manifest) + "\n")
        with open(MOTION, "w", encoding="utf-8", newline="\n") as fh:
            fh.write("\n".join(motion) + "\n")
    os.makedirs(LOOK_DIR, exist_ok=True)
    with open(os.path.join(LOOK_DIR, "report.md"), "w", encoding="utf-8", newline="\n") as fh:
        fh.write("\n".join(report) + "\n")
    print("[експорт] " + OUT_FBX, flush=True)
    print("[якість] " + ("понад поріг: " + ", ".join(failed) if failed else "усі кліпи в межах порогів"), flush=True)
    if failed and STRICT:
        sys.stdout.flush()
        os._exit(3)

entrance_main()
