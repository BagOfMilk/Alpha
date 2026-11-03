# Анімації рингу, партія 1: рух борця й рефері (docs/TRPG_DESIGN.md §11.2; власник 29.10.2026 — «Спочатку анімації руху
# на ринзі + суддя»). Виконується всередині tools/blender/ring_anims.py (exec): бере звідти RPose, stance, steps,
# track, smooth, lerp, off_for, REST, напрямки й FPS — і додає кліпи до CLIPS / CLIP_NODES / CLIP_EVENTS.
#
# Бій покроковий, поле — зони (§4.4), тож рух — це переходи між зонами й очікування ходу. Переміщення справжнє:
# таз проходить шлях, опорна стопа стоїть на маті (Unity-гуманоїд бере з таза root motion), а зсув і поворот кліпу
# пишуться в ring_anims_motion.txt — гра складає прохід як «N циклів ходьби» під відстань між зонами.

# ---------------------------------------------------------------- рамка руху

def Rz(deg):
    return Matrix.Rotation(math.radians(deg), 3, 'Z')

STANCE_FOOT = {"l": Vector((0.2, -0.105, 0.071)), "r": Vector((-0.2, 0.075, 0.071))}
STANCE_FOOT_YAW = {"l": 10.0, "r": -14.0}
REF_FOOT = {"l": Vector((0.12, -0.01, 0.071)), "r": Vector((-0.12, -0.01, 0.071))}
REF_FOOT_YAW = {"l": 8.0, "r": -8.0}

def at(path, u, off):
    pos, yaw = path(u)
    return Vector((pos[0], pos[1], 0.0)) + Rz(yaw) @ off, yaw

def foot_plan(t, keys):
    """Ключі [(час, u, зсув стопи в рамці, поворот носка °)]: між різними точками — крок з підйомом, інакше стоїть.
    Повертає (точка в просторі ригу, поворот навколо вертикалі °). Шлях — глобальний PATH поточного кліпу."""
    # u — частка шляху або ("w", точка, поворот °) — точка в просторі ригу (стопа-опора, навколо якої крутиться тіло)
    pts = [(tt,) + ((u[1] + off, u[2]) if isinstance(u, tuple) else at(PATH[0], u, off)) + (fy,) for tt, u, off, fy in keys]
    if t <= pts[0][0]:
        return pts[0][1], pts[0][2] + pts[0][3]
    for (t0, a, ya, fa), (t1, b, yb, fb) in zip(pts, pts[1:]):
        if t <= t1:
            r = (t - t0) / (t1 - t0) if t1 > t0 else 1.0
            k = step_h(r)
            d = (b - a).length
            lift = step_v(r) * min(0.12, 0.05 + d * 0.2) if d > 1e-4 else 0.0
            return a + (b - a) * k + Vector((0, 0, lift)), (ya + fa) + ((yb + fb) - (ya + fa)) * k
    return pts[-1][1], pts[-1][2] + pts[-1][3]

PATH = [None]

STEP_RUN = [False]

def step_h(r):
    """Горизонталь кроку: стопа рушає після відриву (12 %) і стає на місце до торкання мату (88 %). У бігу — майже
    рівномірно: швидка стопа обганяє таз лише перед самим приземленням."""
    if STEP_RUN[0]:
        return min(1.0, max(0.0, (r - 0.07) / 0.86))           # рівномірно, лише поки стопа над матом (без ковзання)
    return smooth(min(1.0, max(0.0, (r - 0.12) / 0.76)))

def step_v(r):
    return math.sin(math.pi * min(1.0, max(0.0, r)))

def orient(p, pos, yaw):
    """Поза, задана «у себе» (обличчям у −Y, у нулі), — у рамку (pos, yaw): таз, руки й лікті повертаються разом."""
    R = Rz(yaw)
    p.pelvis = Vector((pos[0], pos[1], 0.0)) + R @ p.pelvis
    # таз крутиться навколо своєї голівки, стопи — навколо нуля ригу: голівка не в нулі (жіночий набір — на кілька см)
    # зсувала таз відносно стоп після повороту, стегно відрізнялось від стійки на 4–5° (30.10.2026)
    hr = REST.get("pelvis")
    if hr is not None:
        h = Vector((hr.x, hr.y, 0.0))
        p.pelvis = p.pelvis + (R @ h - h)
    p.pelvis_yaw = p.pelvis_yaw + yaw
    p.head_yaw = yaw + (p.head_yaw or 0.0)
    hands = {}
    for s, (g, fore, up) in p.hands.items():
        def g2(S, g=g, s=s):
            v = g(S) if callable(g) else g
            return S[s] + R @ (v - S[s])
        rot = (lambda v: (lambda S, v=v: R @ (v(S) if callable(v) else v)) if v is not None else None)
        hands[s] = (g2, rot(fore), rot(up))
    p.hands = hands
    el = {}
    for s, e in p.elbows.items():
        el[s] = (lambda S, e=e, s=s: S[s] + R @ ((e(S) if callable(e) else e) - S[s]))
    p.elbows = el
    return p

def set_feet(p, fl, fr, knee_fwd=None):
    """Стопи з foot_plan і коліна над носком (полюс повернутий разом зі стопою)."""
    (al, yl), (ar, yr) = fl, fr
    p.ankles = (al, ar)
    p.foot_rot = (Quaternion(UP, math.radians(yl)), Quaternion(UP, math.radians(yr)))
    p.knees = (al + Rz(yl - STANCE_FOOT_YAW["l"]) @ Vector((0.08, -0.9, 0.45)),
               ar + Rz(yr - STANCE_FOOT_YAW["r"]) @ Vector((-0.08, -0.9, 0.45)))
    return p

def locomote(t, L, base, path, keys_l, keys_r, prog, bob=0.012, run=False):
    """Кліп руху: base(t, L) — поза «у себе», path(u) — рамка, prog(t) — u таза, ключі стоп — foot_plan."""
    PATH[0] = path
    STEP_RUN[0] = run
    u = prog(t)
    pos, yaw = path(u)
    p = base(t, L)
    fl, fr = foot_plan(t, keys_l), foot_plan(t, keys_r)
    air = max(fl[0].z, fr[0].z) - 0.071
    p.pelvis = p.pelvis + Vector((0, 0, -bob * min(1.0, air / 0.08)))
    orient(p, pos, yaw)
    return set_feet(p, fl, fr)

def walk_keys(L, first, lead, trail, u_end=1.0, feet=STANCE_FOOT, fyaw=STANCE_FOOT_YAW):
    """Крок «ведуча → приставна» (борцівська стійка): ведуча стопа йде в [lead], приставна — в [trail] (частки L)."""
    def k(s, span):
        return [(0.0, 0.0, feet[s], fyaw[s]), (span[0] * L, 0.0, feet[s], fyaw[s]), (span[1] * L, u_end, feet[s], fyaw[s])]
    other = "r" if first == "l" else "l"
    keys = {first: k(first, lead), other: k(other, trail)}
    return keys["l"], keys["r"]

# ---------------------------------------------------------------- борець: ходьба в стійці, обхід, повороти

WALK_LEN, WALK_STRIDE = 1.0, 0.36
CIRCLE_R = 1.6             # м — радіус обходу (суперник у центрі кола, перед борцем)

def walk_line(dy):
    return lambda u: ((0.0, dy * u), 0.0)

def clip_walk_fwd(t, L):
    kl, kr = walk_keys(L, "l", (0.04, 0.42), (0.5, 0.9))
    return locomote(t, L, lambda t_, L_: stance(t_, L_, sway=0.0), walk_line(-WALK_STRIDE), kl, kr, lambda t_: t_ / L)

def clip_walk_back(t, L):
    kl, kr = walk_keys(L, "r", (0.04, 0.42), (0.5, 0.9))
    return locomote(t, L, lambda t_, L_: stance(t_, L_, sway=0.0), walk_line(WALK_STRIDE), kl, kr, lambda t_: t_ / L)

def circle(sign):
    """Дуга навколо суперника (центр — CIRCLE_R перед борцем); sign +1 — ліворуч (+X), обличчям до центру."""
    phi = -sign * math.degrees(WALK_STRIDE / CIRCLE_R)
    def path(u):
        a = math.radians(phi * u)
        return (-CIRCLE_R * math.sin(a), -CIRCLE_R + CIRCLE_R * math.cos(a)), phi * u
    return path

def clip_circle_l(t, L):
    kl, kr = walk_keys(L, "l", (0.04, 0.42), (0.5, 0.9))
    return locomote(t, L, lambda t_, L_: stance(t_, L_, sway=0.0), circle(+1), kl, kr, lambda t_: t_ / L)

def clip_circle_r(t, L):
    kl, kr = walk_keys(L, "r", (0.04, 0.42), (0.5, 0.9))
    return locomote(t, L, lambda t_, L_: stance(t_, L_, sway=0.0), circle(-1), kl, kr, lambda t_: t_ / L)

TURN_LEN = 1.1

def turn_path(deg):
    return lambda u: ((0.0, 0.0), deg * u)

def clip_turn(deg):
    first = "l" if deg > 0 else "r"
    def f(t, L):
        if abs(deg) <= 90:
            kl, kr = walk_keys(L, first, (0.08, 0.45), (0.42, 0.8))
            prog = lambda t_: smooth(t_ / (0.82 * L))
        else:
            # пів-оберту ведучою, приставна — на весь, ведуча доставляється
            o = "r" if first == "l" else "l"
            kf = [(0.0, 0.0, STANCE_FOOT[first], STANCE_FOOT_YAW[first]), (0.06 * L, 0.0, STANCE_FOOT[first], STANCE_FOOT_YAW[first]),
                  (0.32 * L, 0.5, STANCE_FOOT[first], STANCE_FOOT_YAW[first]), (0.6 * L, 0.5, STANCE_FOOT[first], STANCE_FOOT_YAW[first]),
                  (0.86 * L, 1.0, STANCE_FOOT[first], STANCE_FOOT_YAW[first])]
            ko = [(0.0, 0.0, STANCE_FOOT[o], STANCE_FOOT_YAW[o]), (0.3 * L, 0.0, STANCE_FOOT[o], STANCE_FOOT_YAW[o]),
                  (0.62 * L, 1.0, STANCE_FOOT[o], STANCE_FOOT_YAW[o])]
            kl, kr = (kf, ko) if first == "l" else (ko, kf)
            prog = lambda t_: smooth(t_ / (0.88 * L))
        return locomote(t, L, lambda t_, L_: stance(t_, L_, sway=0.0), turn_path(deg), kl, kr, prog)
    return f

# ---------------------------------------------------------------- борець: очікування ходу за станом

def idle_base(t, L):
    return stance(t, L, sway=0.6)

def clip_idle_tired(t, L):
    # важке дихання: плечі ходять, тулуб нижче, руки опущені, голова вниз
    b = math.sin(2 * math.pi * t / L * 2)          # два вдихи за цикл
    p = stance(t, L, sway=0.3)
    p.pelvis = p.pelvis + Vector((0, 0.02, -0.03))
    p.pitch = 26.0 + 3.0 * b
    p.head_pitch = 14.0 - 4.0 * b
    p.hands = {"l": (lambda S: S["l"] + Vector((0.0, -0.12, -0.42 + 0.02 * b)), DOWN + FWD * 0.4, FWD),
               "r": (lambda S: S["r"] + Vector((0.0, -0.12, -0.42 + 0.02 * b)), DOWN + FWD * 0.4, FWD)}
    p.elbows = {"l": lambda S: S["l"] + Vector((0.3, 0.1, -0.3)), "r": lambda S: S["r"] + Vector((-0.3, 0.1, -0.3))}
    p.fist = {"l": 0.15, "r": 0.15}
    return p

def clip_idle_hurt_head(t, L):
    # ліва долоня на лобі, похитується, голова хилиться
    w = math.sin(2 * math.pi * t / L)
    p = stance(t, L, sway=0.8)
    p.roll = p.roll + 3.0 * w
    p.head_pitch = 6.0 + 4.0 * math.sin(4 * math.pi * t / L)
    p.head_yaw = 10.0 * w
    p.hands = {"l": (lambda S: S["head"] + Vector((0.02, -0.12, 0.05)), LEFT * 0.3 + UP, BACK),
               "r": p.hands["r"]}
    p.elbows = {"l": lambda S: S["l"] + Vector((0.35, -0.2, -0.05)), "r": p.elbows["r"]}
    p.fist = {"l": 0.05, "r": 0.3}
    return p

def clip_idle_hurt_body(t, L):
    # права рука на ребрах ліворуч, згорблений, дихає уривками
    b = math.sin(2 * math.pi * t / L * 2)
    p = stance(t, L, sway=0.4)
    p.pitch = 30.0 + 2.0 * b
    p.roll = -6.0
    p.yaw = 8.0
    p.head_pitch = 4.0
    p.hands = {"r": (lambda S: S["chest"] + Vector((0.1, -0.15, -0.12)), LEFT + FWD * 0.2, UP),
               "l": (lambda S: S["l"] + Vector((-0.02, -0.25, -0.3)), FWD * 0.4 + UP, RIGHT)}
    p.elbows = {"r": lambda S: S["r"] + Vector((-0.2, -0.15, -0.35)), "l": p.elbows["l"]}
    p.fist = {"l": 0.3, "r": 0.1}
    return p

def clip_idle_hurt_legs(t, L):
    # вага на правій, ліве коліно зігнуте й «береже» ногу, ліва рука на стегні
    w = math.sin(2 * math.pi * t / L)
    p = stance(t, L, sway=0.0)
    p.pelvis = p.pelvis + Vector((-0.05 + 0.01 * w, 0.02, -0.02))
    p.roll = -5.0
    p.pitch = 20.0
    p.ankles = (foot(0.2, -0.105) + Vector((0, 0.02, 0.0)), foot(-0.2, 0.075))
    p.foot_rot = (Quaternion(UP, math.radians(10)) @ Quaternion(LEFT, math.radians(-12)), Quaternion(UP, math.radians(-14)))
    p.ankles = (p.ankles[0] + Vector((0, 0, 0.02)), p.ankles[1])                 # носок, п'ята над матом
    p.hands = {"l": (lambda S: S["pelvis"] + Vector((0.15, -0.12, -0.12)), DOWN, FWD), "r": p.hands["r"]}
    p.elbows = {"l": lambda S: S["l"] + Vector((0.3, 0.05, -0.3)), "r": p.elbows["r"]}
    p.fist = {"l": 0.1, "r": 0.3}
    return p

def clip_idle_hype(t, L):
    # на хвилі публіки: випростаний, правий кулак угору в такт, голова водить по трибунах
    k = math.sin(2 * math.pi * t / L * 2)
    p = stance(t, L, sway=0.5)
    p.pitch = 6.0
    p.pelvis = p.pelvis + Vector((0, 0, 0.04))
    p.head_pitch = -14.0
    p.head_yaw = 35.0 * math.sin(2 * math.pi * t / L)
    pump = 0.5 + 0.5 * k
    p.hands = {"r": (lambda S: S["r"] + Vector((-0.12, -0.05, 0.35 + 0.2 * pump)), UP, FWD),
               "l": (lambda S: S["l"] + Vector((0.05, -0.2, -0.32)), FWD * 0.4 + UP, RIGHT)}
    p.elbows = {"r": lambda S: S["r"] + Vector((-0.4, 0.0, 0.05)), "l": lambda S: S["l"] + Vector((0.3, 0.0, -0.35))}
    p.fist = {"l": 0.3, "r": 0.9}
    return p

# ---------------------------------------------------------------- рефері

def ref_stand(t, L, sway=1.0):
    """Рефері: рівно, ноги на ширині стегон, руки вздовж тіла, легке перенесення ваги."""
    sh = math.sin(2 * math.pi * t / L)
    p = RPose(feet=(0.12, 0.0), feet_yaw=(8.0, -8.0),
              pelvis=Vector((0.015 * sh * sway, 0.0, -0.01)), pitch=4.0 + breathe(t, L, 0.6), roll=-1.0 * sh * sway,
              head_pitch=-2.0, forced=set())
    p.ankles = (REF_FOOT["l"].copy(), REF_FOOT["r"].copy())
    p.hands = {"l": (lambda S: S["l"] + Vector((0.06, -0.03, -0.52)), DOWN + FWD * 0.15, FWD + RIGHT * 0.3),
               "r": (lambda S: S["r"] + Vector((-0.06, -0.03, -0.52)), DOWN + FWD * 0.15, FWD + LEFT * 0.3)}
    p.elbows = {"l": lambda S: S["l"] + Vector((0.2, 0.3, -0.3)), "r": lambda S: S["r"] + Vector((-0.2, 0.3, -0.3))}
    p.fist = {"l": 0.3, "r": 0.3}
    return p

def clip_ref_idle(t, L):
    p = ref_stand(t, L)
    p.head_yaw = 28.0 * math.sin(2 * math.pi * t / L) + 8.0 * math.sin(6 * math.pi * t / L)   # стежить за обома
    p.yaw = 6.0 * math.sin(2 * math.pi * t / L)
    return p

REF_WALK_LEN, REF_STEP = 1.0, 0.5

def clip_ref_walk(t, L):
    # звичайна хода: дві кроки на цикл, стопи по черзі обганяють одна одну, руки в протифазі до ніг
    half = REF_STEP / 2
    s_ = REF_STEP * 2
    path = lambda u: ((0.0, -s_ * u), 0.0)
    fl = Vector((0.11, -half, 0.071)); fr = Vector((-0.11, half, 0.071))
    # u тут — у частках циклу; права йде з «позаду» на «попереду» наступного кроку
    kr = [(0.0, 0.0, fr, -6.0), (0.04 * L, 0.0, fr, -6.0), (0.46 * L, 0.5, Vector((-0.11, -half, 0.071)), -6.0)]
    kl = [(0.0, 0.0, fl, 6.0), (0.54 * L, 0.0, fl, 6.0), (0.96 * L, 1.0, Vector((0.11, -half, 0.071)) + Vector((0, 0, 0)), 6.0)]
    # ліва стоїть у точці (u=0, попереду); на кінці — та сама відносна точка при u=1
    kl[-1] = (0.96 * L, 1.0, fl, 6.0)
    kr.append((L, 0.5, Vector((-0.11, -half, 0.071)), -6.0))
    swing = math.sin(2 * math.pi * t / L)
    def base(t_, L_):
        p = ref_stand(t_, L_, sway=0.0)
        p.pelvis = p.pelvis + Vector((0, 0, -0.04))
        p.pitch = 6.0
        p.yaw = 6.0 * swing
        p.hands = {"l": (lambda S: S["l"] + Vector((0.06, -0.03 - 0.18 * swing, -0.5)), DOWN + FWD * 0.15, FWD + RIGHT * 0.3),
                   "r": (lambda S: S["r"] + Vector((-0.06, -0.03 + 0.18 * swing, -0.5)), DOWN + FWD * 0.15, FWD + LEFT * 0.3)}
        return p
    PATH[0] = path
    p = locomote(t, L, base, path, kl, kr, lambda t_: t_ / L, bob=0.02)
    p.knees = (p.ankles[0] + Vector((0.05, -0.9, 0.45)), p.ankles[1] + Vector((-0.05, -0.9, 0.45)))
    return p

# Стан «на колінах рахує»: обидва коліна на маті, сидить майже на п'ятах, тулуб уперед.
KNEEL_PEL = Vector((0.0, 0.2, 0.47))

def ref_kneel(t, L, lean=0.0):
    p = RPose(forced=set())
    p.pelvis = off_for(KNEEL_PEL + Vector((0, -0.06 * lean, 0.0)))
    p.pitch = 28.0 + 50.0 * lean
    p.head_pitch = -8.0
    tip = Quaternion(LEFT, math.radians(-60.0))
    p.ankles = (Vector((0.13, 0.52, 0.1)), Vector((-0.13, 0.52, 0.1)))
    p.foot_rot = (tip, tip)
    p.knees = (Vector((0.16, -0.6, 0.0)), Vector((-0.16, -0.6, 0.0)))
    p.hands = {"l": (lambda S: S["l"] + Vector((0.04, -0.16, -0.42)), DOWN + FWD * 0.3, FWD),
               "r": (lambda S: S["r"] + Vector((-0.04, -0.16, -0.42)), DOWN + FWD * 0.3, FWD)}
    p.elbows = {"l": lambda S: S["l"] + Vector((0.25, 0.1, -0.3)), "r": lambda S: S["r"] + Vector((-0.25, 0.1, -0.3))}
    p.fist = {"l": 0.2, "r": 0.2}
    return p

KNEEL_LEN = 1.2

def kneel_blend(t, L, k):
    """k: 0 — стоїть, 1 — на колінах. Таз опускається, ноги йдуть у присід і на коліна (спершу права)."""
    st = ref_stand(t, L, sway=0.0)
    kn = ref_kneel(t, L)
    abs_st = REST["pelvis"] + st.pelvis
    abs_kn = REST["pelvis"] + kn.pelvis
    p = RPose(forced=set())
    # таз іде дугою: спершу вниз-уперед (присід), потім назад на п'яти
    mid = Vector((0.0, -0.05, 0.62))
    a = smooth(min(1.0, k * 1.6)); b = smooth(max(0.0, k * 1.6 - 0.6) / 1.0)
    abs_p = lerp(lerp(abs_st, mid, a), abs_kn, b)
    p.pelvis = off_for(abs_p)
    p.pitch = lerp(st.pitch, 40.0, a) * (1 - b) + kn.pitch * b
    p.head_pitch = lerp(st.head_pitch, kn.head_pitch, k)
    kr = min(1.0, k * 1.7)                         # права нога — перша
    kl = max(0.0, (k - 0.35) / 0.65)
    tip = Quaternion(LEFT, math.radians(-60.0))
    p.ankles = (lerp(REF_FOOT["l"], kn.ankles[0], step_h(kl)) + Vector((0, 0, 0.06 * step_v(kl))),
                lerp(REF_FOOT["r"], kn.ankles[1], step_h(kr)) + Vector((0, 0, 0.06 * step_v(kr))))
    p.foot_rot = (Quaternion(UP, math.radians(8)).slerp(tip, kl), Quaternion(UP, math.radians(-8)).slerp(tip, kr))
    p.knees = (lerp(REF_FOOT["l"] + Vector((0.08, -0.9, 0.45)), kn.knees[0], kl),
               lerp(REF_FOOT["r"] + Vector((-0.08, -0.9, 0.45)), kn.knees[1], kr))
    p.hands = {s: (lambda S, s=s: lerp(st.hands[s][0](S), kn.hands[s][0](S), k), DOWN + FWD * 0.2, FWD) for s in "lr"}
    p.elbows = {s: (lambda S, s=s: lerp(st.elbows[s](S), kn.elbows[s](S), k)) for s in "lr"}
    p.fist = {"l": 0.25, "r": 0.25}
    return p

def clip_ref_kneel_down(t, L):
    return kneel_blend(t, L, smooth(min(1.0, t / (0.85 * L))))

def clip_ref_kneel_up(t, L):
    return kneel_blend(t, L, 1.0 - smooth(min(1.0, t / (0.85 * L))))

# Рахунок утримання: долоня з висоти голови — по мату перед собою; між ударами рука знов угору.
COUNT_BEAT = 0.95          # с між ударами (темп рефері)
COUNT_FIRST = 1.05         # с — перший удар: спершу рефері опускається й спирається

def slap(t, times, hold_last=False):
    """0..1 — висота руки (1 — угорі, 0 — на маті) для ударів у моменти times."""
    h = 0.0
    for i, tc in enumerate(times):
        up0, top = tc - 0.6, tc - 0.3          # удар долонею — 0,3 с, кисть не перескакує
        if up0 <= t <= tc:
            h = smooth((t - up0) / (top - up0)) if t < top else 1.0 - ((t - top) / (tc - top)) ** 2
        elif tc < t < tc + 0.18:
            h = 0.0                                  # долоня на маті
    return h

def count_pose(t, L, times, show_two=False):
    # нахил тазом уперед (не лише тулубом), ліва долоня спирається на мат — плече опускається на досяжну висоту
    down = track(t, [(0.0, 0.0), (0.45, 1.0), (L - 0.35, 1.0), (L, 0.6)])
    p = ref_kneel(t, L, lean=0.6)
    p.pelvis_pitch = 26.0 * down
    p.pitch = 62.0 + 8.0 * down
    h = slap(t, times)
    rest_w = 1.0 - smooth((t - (times[0] - 0.75)) / 0.25)        # до першого замаху рука — на стегні
    last = times[-1]
    def hand_r(S):
        mat = Vector((S["r"].x - 0.06, S["r"].y - 0.12, 0.05))      # мат під правим плечем
        knee = S["pelvis"] + Vector((-0.13, -0.2, -0.12))            # долоня на правому стегні
        if rest_w > 1e-3:
            return lerp(lerp(mat, S["r"] + Vector((-0.08, -0.12, 0.42)), h), knee, rest_w)
        up = S["r"] + Vector((-0.08, -0.12, 0.42))
        if show_two and t > last + 0.18:
            k = smooth((t - last - 0.18) / 0.35)
            return lerp(mat + Vector((0, 0, 0.02)), S["r"] + Vector((-0.1, -0.35, 0.2)), k)
        return lerp(mat, up, h)
    p.hands = {"r": (hand_r, (DOWN * (1 - h) + (UP + FWD * 0.4) * h), FWD),
               "l": (lambda S: lerp(S["l"] + Vector((0.05, -0.2, -0.36)),
                                    Vector((S["l"].x + 0.04, S["l"].y - 0.08, 0.05)), down), DOWN + FWD * 0.3, FWD)}
    p.elbows = {"r": lambda S: S["r"] + Vector((-0.35, 0.05, 0.0)), "l": lambda S: S["l"] + Vector((0.3, 0.1, -0.3))}
    p.fist = {"l": 0.15, "r": 0.0 if h < 0.5 else 0.1}
    p.head_pitch = -20.0 * down
    p.head_yaw = -20.0                                # дивиться на плечі борця під ним
    return p

COUNT3_TIMES = [COUNT_FIRST + i * COUNT_BEAT for i in range(3)]
COUNT2_TIMES = [COUNT_FIRST + i * COUNT_BEAT for i in range(2)]
COUNT3_LEN = COUNT3_TIMES[-1] + 0.9
COUNT2_LEN = COUNT2_TIMES[-1] + 1.3

def clip_ref_count3(t, L):
    return count_pose(t, L, COUNT3_TIMES)

def clip_ref_count2(t, L):
    return count_pose(t, L, COUNT2_TIMES, show_two=True)

def clip_ref_warn(t, L):
    # погрозити пальцем: права рука вперед на рівні обличчя, кисть похитується; крок до борця і назад
    p = ref_stand(t, L, sway=0.0)
    k = track(t, [(0.0, 0.0), (0.3, 1.0), (1.3, 1.0), (1.7, 0.0)])
    wag = math.sin(2 * math.pi * (t - 0.3) / 0.36) * k if 0.3 < t < 1.3 else 0.0
    p.pitch = 4.0 + 6.0 * k
    p.head_pitch = -6.0
    st_r = p.hands["r"]
    p.hands = {"r": (lambda S: lerp(st_r[0](S), S["r"] + Vector((-0.05 + 0.04 * wag, -0.48, 0.12)), k),
                     DOWN.lerp(FWD + UP * 0.6, k), FWD.lerp(LEFT, k)), "l": p.hands["l"]}
    el_r = p.elbows["r"]
    p.elbows = {"r": lambda S: el_r(S).lerp(S["r"] + Vector((-0.3, -0.1, -0.2)), k), "l": p.elbows["l"]}
    p.fist = {"l": 0.3, "r": 0.3 + 0.4 * k}
    return p

def clip_ref_call_bell(t, L):
    # гонг: обидві руки схрещуються над головою двічі, голова — до столу хронометриста
    p = ref_stand(t, L, sway=0.0)
    k = track(t, [(0.0, 0.0), (0.35, 1.0), (1.7, 1.0), (2.25, 0.0)])
    x = 0.5 + 0.5 * math.cos(2 * math.pi * (t - 0.35) / 0.65) if 0.35 < t < 1.65 else 1.0
    p.head_yaw = -30.0 * k
    p.head_pitch = -10.0
    st = p.hands
    fore = DOWN * (1 - k) + UP * k + FWD * 0.5
    out = math.sin(math.pi * min(1.0, k)) if t < 0.35 or t > 1.65 else 0.0   # підйом і спуск — дугою назовні, не крізь груди
    p.hands = {"l": (lambda S: lerp(st["l"][0](S), S["l"] + Vector((-0.12 - 0.1 * x, -0.12, 0.45)), k) + Vector((0.22 * out, -0.1 * out, 0.0)), fore, FWD + UP * 0.2),
               "r": (lambda S: lerp(st["r"][0](S), S["r"] + Vector((0.12 + 0.1 * x, -0.12, 0.45)), k) + Vector((-0.22 * out, -0.1 * out, 0.0)), fore, FWD + UP * 0.2)}
    el = p.elbows
    p.elbows = {"l": lambda S: el["l"](S).lerp(S["l"] + Vector((0.35, 0.0, 0.2)), k),
                "r": lambda S: el["r"](S).lerp(S["r"] + Vector((-0.35, 0.0, 0.2)), k)}
    p.fist = {"l": 0.3 - 0.25 * k, "r": 0.3 - 0.25 * k}
    return p

def clip_ref_ask_submit(t, L):
    # на колінах біля голови борця: нахилився, рука на плечі, питає; голова кивком «ну?»
    p = ref_kneel(t, L, lean=0.6 + 0.2 * math.sin(2 * math.pi * t / L))
    p.head_pitch = 18.0 + 6.0 * math.sin(4 * math.pi * t / L)
    p.head_yaw = -12.0
    p.hands = {"r": (lambda S: S["r"] + Vector((-0.02, -0.45, -0.32)), DOWN, FWD),
               "l": (lambda S: S["l"] + Vector((0.05, -0.25, -0.36)), DOWN + FWD * 0.3, FWD)}
    return p

# ---------------------------------------------------------------- реєстр партії

# ім'я, довжина, A, B, петля, зсув B, поворот B
MOVE_CLIPS = [
    ("Idle_Tired_Loop", 2.4, clip_idle_tired, None, True, None, 0),
    ("Idle_Hurt_Head_Loop", 2.6, clip_idle_hurt_head, None, True, None, 0),
    ("Idle_Hurt_Body_Loop", 2.4, clip_idle_hurt_body, None, True, None, 0),
    ("Idle_Hurt_Legs_Loop", 2.6, clip_idle_hurt_legs, None, True, None, 0),
    ("Idle_Hype_Loop", 2.0, clip_idle_hype, None, True, None, 0),
    ("Move_Walk_Fwd_Loop", WALK_LEN, clip_walk_fwd, None, True, None, 0),
    ("Move_Walk_Back_Loop", WALK_LEN, clip_walk_back, None, True, None, 0),
    ("Move_Circle_L_Loop", WALK_LEN, clip_circle_l, None, True, None, 0),
    ("Move_Circle_R_Loop", WALK_LEN, clip_circle_r, None, True, None, 0),
    ("Move_Turn_L90", TURN_LEN, clip_turn(90), None, False, None, 0),
    ("Move_Turn_R90", TURN_LEN, clip_turn(-90), None, False, None, 0),
    ("Move_Turn_180", 1.5, clip_turn(180), None, False, None, 0),
    ("Ref_Idle_Loop", 3.2, clip_ref_idle, None, True, None, 0),
    ("Ref_Walk_Loop", REF_WALK_LEN, clip_ref_walk, None, True, None, 0),
    ("Ref_Kneel_Down", KNEEL_LEN, clip_ref_kneel_down, None, False, None, 0),
    ("Ref_Kneel_Up", KNEEL_LEN, clip_ref_kneel_up, None, False, None, 0),
    ("Ref_Count_Pin_3", COUNT3_LEN, clip_ref_count3, None, False, None, 0),
    ("Ref_Count_Pin_2", COUNT2_LEN, clip_ref_count2, None, False, None, 0),
    ("Ref_Warn", 1.9, clip_ref_warn, None, False, None, 0),
    ("Ref_Call_Bell", 2.5, clip_ref_call_bell, None, False, None, 0),
    ("Ref_Ask_Submit_Loop", 2.0, clip_ref_ask_submit, None, True, None, 0),
]

MOVE_NODE_POSES = {
    "ref_stand": lambda: ref_stand(0.0, 3.2),
    "ref_kneel": lambda: ref_kneel(0.0, 1.0),
}

MOVE_CLIP_NODES = {
    "Move_Walk_Fwd_Loop": {"A": ("stance", "stance")},
    "Move_Walk_Back_Loop": {"A": ("stance", "stance")},
    "Move_Circle_L_Loop": {"A": ("stance", "stance")},
    "Move_Circle_R_Loop": {"A": ("stance", "stance")},
    "Move_Turn_L90": {"A": ("stance", "stance")},
    "Move_Turn_R90": {"A": ("stance", "stance")},
    "Move_Turn_180": {"A": ("stance", "stance")},
    "Ref_Kneel_Down": {"A": ("ref_stand", "ref_kneel")},
    "Ref_Kneel_Up": {"A": ("ref_kneel", "ref_stand")},
    "Ref_Warn": {"A": ("ref_stand", "ref_stand")},
    "Ref_Call_Bell": {"A": ("ref_stand", "ref_stand")},
}

MOVE_EVENTS = {
    "Ref_Count_Pin_3": [("count_%d" % (i + 1), tc, "hand_r", "-") for i, tc in enumerate(COUNT3_TIMES)],
    "Ref_Count_Pin_2": [("count_%d" % (i + 1), tc, "hand_r", "-") for i, tc in enumerate(COUNT2_TIMES)]
                       + [("kickout", COUNT2_TIMES[-1] + 0.2, "-", "-")],
    "Ref_Call_Bell": [("bell", 0.5, "-", "-")],
}
MOVE_CONTACTS = {}

# ================================================================ друга половина: біг, канати, кут, рефері

RUN_PH = [0.0]
RUN_LEN, RUN_STRIDE = 0.66, 2.6          # два кроки за цикл, ~3,9 м/с
RUN_FOOT = {"l": Vector((0.1, 0.0, 0.071)), "r": Vector((-0.1, 0.0, 0.071))}

def run_base(t, L):
    """Поза бігу «у себе»: випростаний, нахил уперед, лікті під 90°, руки в протифазі до ніг (фаза — RUN_PH[0])."""
    c = math.cos(2 * math.pi * RUN_PH[0])
    p = RPose(forced=set(), pitch=14.0, head_pitch=-8.0)
    p.pelvis = Vector((0.0, 0.0, -0.05 - 0.02 * math.cos(4 * math.pi * (RUN_PH[0] - 0.08))))
    p.yaw = -8.0 * c
    p.hands = {"l": (lambda S: S["l"] + Vector((0.06, 0.2 * c - 0.08, -0.26 + 0.06 * max(0.0, -c))), FWD + UP * 0.4, RIGHT),
               "r": (lambda S: S["r"] + Vector((-0.06, -0.2 * c - 0.08, -0.26 + 0.06 * max(0.0, c))), FWD + UP * 0.4, LEFT)}
    p.elbows = {"l": lambda S: S["l"] + Vector((0.12, 0.3, -0.3)), "r": lambda S: S["r"] + Vector((-0.12, 0.3, -0.3))}
    p.fist = {"l": 0.55, "r": 0.55}
    return p

def clip_run_loop(t, L):
    # стопа стоїть 16 % циклу (від «попереду таза» до «позаду»), далі — політ; права — у протифазі
    RUN_PH[0] = t / L
    kl = [(0.0, 0.08, RUN_FOOT["l"], 5.0), (0.16 * L, 0.08, RUN_FOOT["l"], 5.0), (L, 1.08, RUN_FOOT["l"], 5.0)]
    kr = [(-0.34 * L, -0.42, RUN_FOOT["r"], -5.0), (0.5 * L, 0.58, RUN_FOOT["r"], -5.0), (0.66 * L, 0.58, RUN_FOOT["r"], -5.0),
          (1.5 * L, 1.58, RUN_FOOT["r"], -5.0)]
    return locomote(t, L, run_base, lambda u: ((0.0, -RUN_STRIDE * u), 0.0), kl, kr, lambda t_: t_ / L, bob=0.0, run=True)

def lerp_hand(a, b, k):
    """Змішати дві цілі кисті (grip, fore, up): k = 1 — a, 0 — b."""
    ev = lambda v, S: v(S) if callable(v) else v
    return (lambda S: lerp(ev(b[0], S), ev(a[0], S), k), lambda S: lerp(ev(b[1], S), ev(a[1], S), k),
            lambda S: lerp(ev(b[2], S), ev(a[2], S), k))

# --- відскок від канатів. Борець у стійці обличчям до суперника; канати — за спиною на ROPE_D. Розворот, три кроки
# бігу до канатів, перед канатами — ще пів-оберту, спиною в канати (верхній — під лопатками, середній — під поперек),
# канати прогинаються на ROPE_SINK, руки чіпляються за верхній, відштовхування — і біг назад до суперника. Гра ставить
# старт кліпу на ROPE_D від лінії каната (ring_anims_motion.txt); прогин рахує її фізика канатів (мітки rope_hit).
ROPE_D = 1.8
ROPE_SINK = 0.28
ROPE_H = (0.48, 0.86, 1.24)
REB_LEN = 1.75
REB_HIT, REB_OFF = 0.98, 1.16           # с — контакт спиною і відрив від канатів

def reb_path(t):
    y = track(t, [(0.0, 0.0), (0.22, 0.06), (0.52, 0.75), (0.8, 1.55), (REB_HIT, ROPE_D + 0.02),
                  (1.06, ROPE_D + ROPE_SINK - 0.18), (REB_OFF, ROPE_D), (1.45, 0.85), (REB_LEN, -0.35)])
    yaw = track(t, [(0.0, 0.0), (0.3, 180.0), (0.66, 180.0), (0.97, 360.0), (REB_LEN, 360.0)])   # розворот 0,3 с
    return (0.0, y), yaw

def reb_keys():
    """Ключі стоп (час, u = час шляху, зсув, носок): біг по черзі, обидві стоять під час прогину канатів."""
    F, st = RUN_FOOT, STANCE_FOOT
    kl = [(0.0, 0.0, st["l"], STANCE_FOOT_YAW["l"]), (0.08, 0.0, st["l"], STANCE_FOOT_YAW["l"]),
          (0.36, 0.38, F["l"], 4.0), (0.44, 0.38, F["l"], 4.0),
          (0.66, 0.7, F["l"], 4.0), (0.72, 0.7, F["l"], 4.0),
          (0.96, 1.0, F["l"], 4.0), (1.14, 1.0, F["l"], 4.0),
          (1.38, 1.42, F["l"], 4.0), (1.45, 1.42, F["l"], 4.0), (REB_LEN, 1.8, F["l"], 4.0)]
    kr = [(0.0, 0.0, st["r"], STANCE_FOOT_YAW["r"]), (0.02, 0.0, st["r"], STANCE_FOOT_YAW["r"]),
          (0.22, 0.24, F["r"], -4.0), (0.3, 0.24, F["r"], -4.0),
          (0.52, 0.55, F["r"], -4.0), (0.58, 0.55, F["r"], -4.0),
          (0.82, 0.84, F["r"], -4.0), (0.9, 0.84, F["r"], -4.0),
          (1.0, 1.02, F["r"], -4.0), (1.16, 1.02, F["r"], -4.0),
          (1.27, 1.28, F["r"], -4.0), (1.33, 1.28, F["r"], -4.0), (1.56, 1.6, F["r"], -4.0), (1.62, 1.6, F["r"], -4.0),
          (2.1, 2.2, F["r"], -4.0)]
    return kl, kr

def clip_rope_rebound(t, L):
    RUN_PH[0] = t / RUN_LEN
    kl, kr = reb_keys()
    on = track(t, [(0.0, 0.0), (0.78, 0.0), (REB_HIT, 1.0), (REB_OFF, 1.0), (1.4, 0.0), (L, 0.0)])   # спиною на канатах
    st = track(t, [(0.0, 1.0), (0.28, 0.0), (L, 0.0)])                                              # зі стійки в біг
    def base(t_, L_):
        r = run_base(t_, L_)
        s_ = stance(t_, L_, sway=0.0)
        r_el, r_fist = dict(r.elbows), dict(r.fist)
        p = r
        p.pelvis = lerp(r.pelvis, s_.pelvis, st)
        p.pitch = lerp(lerp(r.pitch, s_.pitch, st), -6.0, on)          # спиною в канати — тулуб відкинутий
        p.yaw = lerp(r.yaw, s_.yaw, st) * (1 - on)
        p.head_pitch = lerp(lerp(r.head_pitch, s_.head_pitch, st), -4.0, on)
        hands = {}
        for s in "lr":
            h = lerp_hand(s_.hands[s], r.hands[s], st)
            side = 1 if s == "l" else -1
            def g(S, h=h, s=s, side=side):
                tgt = S[s] + Vector((0.3 * side, 0.1, 0.0))
                tgt.z = ROPE_H[2] - 0.02                          # долоня на верхньому канаті збоку
                return lerp(h[0](S), tgt, on)
            hands[s] = (g, h[1], h[2])
        p.hands = hands
        p.elbows = {s: (lambda S, s=s: s_.elbows[s](S).lerp(r_el[s](S), 1 - st)) for s in "lr"}
        p.fist = {s: lerp(s_.fist[s], r_fist[s], 1 - st) for s in "lr"}
        return p
    return locomote(t, L, base, reb_path, kl, kr, lambda t_: t_, bob=0.0, run=True)

# --- кут: задки в кут, спиною в подушку, руки на верхній канат по обидва боки.
CORNER_IN = 1.3

def corner_lean(t, L, k=1.0):
    p = stance(t, L, sway=0.0)
    b = breathe(t, L, 1.0)
    p.pelvis = lerp(p.pelvis, Vector((0.0, 0.06, -0.04)), k)
    p.pitch = lerp(p.pitch, -4.0 + b, k)
    p.head_pitch = lerp(p.head_pitch, -6.0, k)
    base = p.hands
    hands = {}
    for s in "lr":
        side = 1 if s == "l" else -1
        def g(S, s=s, side=side):
            # канати йдуть від кута під 45° уперед-убік: долоня на верхньому, збоку від плеча
            tgt = Vector((S[s].x + 0.32 * side, S[s].y + 0.14, ROPE_H[2] - 0.02))
            arc = Vector((0.22 * side, -0.08, 0.06)) * math.sin(math.pi * k)
            return lerp(base[s][0](S), tgt, k) + arc
        hands[s] = (g, base[s][1] * (1 - k) + (DOWN + LEFT * 0.3 * side) * k, base[s][2] * (1 - k) + FWD * k)
    p.hands = hands
    el = p.elbows
    p.elbows = {"l": lambda S: el["l"](S).lerp(S["l"] + Vector((0.3, 0.15, -0.15)), k) + Vector((0.2, 0, 0)) * math.sin(math.pi * k),
                "r": lambda S: el["r"](S).lerp(S["r"] + Vector((-0.3, 0.15, -0.15)), k) + Vector((-0.2, 0, 0)) * math.sin(math.pi * k)}
    p.fist = {"l": lerp(0.25, 0.4, k), "r": lerp(0.25, 0.4, k)}
    return p

def clip_corner_lean(t, L):
    return corner_lean(t, L)

def clip_corner_enter(t, L):
    k = smooth(max(0.0, (t - 0.7) / 0.45))
    kl, kr = walk_keys(L, "r", (0.04, 0.32), (0.36, 0.66))
    return locomote(t, L, lambda t_, L_: corner_lean(t_, L_, k), lambda u: ((0.0, 0.42 * u), 0.0), kl, kr,
                    lambda t_: smooth(min(1.0, t_ / 0.72)))

def clip_corner_exit(t, L):
    k = 1.0 - smooth(min(1.0, t / 0.4))
    kl, kr = walk_keys(L, "l", (0.36, 0.66), (0.66, 0.95))
    return locomote(t, L, lambda t_, L_: corner_lean(t_, L_, k), lambda u: ((0.0, -0.42 * u), 0.0), kl, kr,
                    lambda t_: smooth(min(1.0, max(0.0, (t_ - 0.3) / 0.7))))

# --- рефері рахує за рингом: стоїть біля канатів, права рука рубає вниз на кожен рахунок (петля — два рахунки).
OUT_BEAT = 1.0

def clip_ref_count_outside(t, L):
    p = ref_stand(t, L, sway=0.3)
    p.head_pitch = 10.0
    p.pitch = 8.0
    c = (t % OUT_BEAT) / OUT_BEAT
    h = (1.0 - smooth(c / 0.3)) if c < 0.3 else (smooth((c - 0.45) / 0.55) if c > 0.45 else 0.0)
    p.hands = {"r": (lambda S: S["r"] + Vector((-0.08, -0.36 - 0.05 * h, -0.08 + 0.38 * h)), FWD + UP * (0.2 + 0.8 * h), LEFT),
               "l": (lambda S: S["l"] + Vector((0.06, -0.28, -0.28)), FWD + DOWN * 0.3, UP)}      # ліва — на канаті
    p.elbows = {"r": lambda S: S["r"] + Vector((-0.32, 0.0, -0.2)), "l": lambda S: S["l"] + Vector((0.3, 0.05, -0.3))}
    p.fist = {"l": 0.5, "r": 0.1}
    return p

# --- рефері піднімає руку переможця (парний): A — рефері, B — переможець праворуч від нього (−X), обидва — до камери.
RAISE_LEN = 2.6
RAISE_K = [(0.0, 0.0), (0.35, 0.0), (0.75, 0.55), (1.15, 1.0), (2.1, 1.0), (RAISE_LEN, 0.92)]

def clip_ref_raise_a(t, L, partner=None):
    p = ref_stand(t, L, sway=0.0)
    k = track(t, RAISE_K)
    grab = smooth(min(1.0, max(0.0, (t - 0.3) / 0.25)))
    st_r = p.hands["r"]
    def hand_r(S):
        wrist = P(S, "hand_l", 0.0, Vector((0.0, 0.0, 0.03)))
        return lerp(st_r[0](S), wrist, grab) if wrist is not None else st_r[0](S)
    f0 = st_r[1]
    p.hands = {"r": (hand_r, f0 * (1 - k) + (UP + FWD * 0.3) * k, st_r[2] * (1 - grab) + RIGHT * grab), "l": p.hands["l"]}
    el_r = p.elbows["r"]
    p.elbows = {"r": lambda S: el_r(S).lerp(S["r"] + Vector((-0.1, 0.25, 0.1)), k), "l": p.elbows["l"]}
    p.fist = {"l": 0.3, "r": 0.3 + 0.5 * grab}
    p.head_yaw = -25.0 * k                              # дивиться на переможця
    return p

def clip_ref_raise_b(t, L, partner=None):
    p = stance(t, L, sway=0.0)
    k = track(t, RAISE_K)
    up = smooth(min(1.0, t / 0.6))                      # випростується
    p.pelvis = lerp(p.pelvis, Vector((0, 0, -0.02)), up)
    p.pitch = lerp(16.0, 2.0, up)
    p.head_pitch = -16.0 * k
    st = p.hands
    p.hands = {"l": (lambda S: lerp(st["l"][0](S), S["l"] + Vector((0.04, -0.06, 0.48)), k), st["l"][1] * (1 - k) + UP * k, st["l"][2] * (1 - k) + FWD * k),
               "r": (lambda S: lerp(st["r"][0](S), S["r"] + Vector((-0.15, -0.15, 0.4)), k), st["r"][1] * (1 - k) + UP * k, st["r"][2] * (1 - k) + FWD * k)}
    el = p.elbows
    p.elbows = {"l": lambda S: el["l"](S).lerp(S["l"] + Vector((0.3, 0.05, -0.15 + 0.2 * k)), k),
                "r": lambda S: el["r"](S).lerp(S["r"] + Vector((-0.4, 0.0, 0.0)), k)}
    p.fist = {"l": lerp(0.25, 0.8, k), "r": lerp(0.25, 0.9, k)}
    return p

MOVE_CLIPS += [
    ("Move_Run_Loop", RUN_LEN, clip_run_loop, None, True, None, 0),
    ("Move_Rope_Rebound", REB_LEN, clip_rope_rebound, None, False, None, 0),
    ("Move_Corner_Enter", CORNER_IN, clip_corner_enter, None, False, None, 0),
    ("Move_Corner_Lean_Loop", 3.0, clip_corner_lean, None, True, None, 0),
    ("Move_Corner_Exit", CORNER_IN, clip_corner_exit, None, False, None, 0),
    ("Ref_Count_Outside_Loop", 2 * OUT_BEAT, clip_ref_count_outside, None, True, None, 0),
    ("Ref_Raise_Hand", RAISE_LEN, clip_ref_raise_a, clip_ref_raise_b, False,
     Vector((-0.75, 0.0, 0.0)), 0),   # відстань масштабується за руками набору (BODY["arm"], ARM_SCALED)
]
MOVE_NODE_POSES["corner_lean"] = lambda: corner_lean(0.0, 3.0)
MOVE_CLIP_NODES.update({
    "Move_Rope_Rebound": {"A": ("stance", None)},
    "Move_Corner_Enter": {"A": ("stance", "corner_lean")},
    "Move_Corner_Exit": {"A": ("corner_lean", "stance")},
    "Ref_Raise_Hand": {"A": ("ref_stand", None), "B": ("stance", None)},
})
MOVE_EVENTS.update({
    # мітки для фізики канатів у грі: поле «кістка B» = "rope<1..3>:<прогин м>"
    "Move_Rope_Rebound": [("rope_hit", REB_HIT, "spine_02", "rope2:%.2f" % ROPE_SINK),
                          ("rope_hit", REB_HIT + 0.02, "spine_03", "rope3:%.2f" % (ROPE_SINK * 0.8)),
                          ("rope_release", REB_OFF, "-", "-")],
    "Move_Corner_Enter": [("rope_lean", 1.05, "spine_03", "corner")],
    "Ref_Count_Outside_Loop": [("count", 0.3, "hand_r", "-"), ("count", 0.3 + OUT_BEAT, "hand_r", "-")],
    "Ref_Raise_Hand": [("raise", 1.15, "hand_r", "hand_l")],
})
# рефері й переможець стоять пліч-о-пліч і тримаються за руки — плече до плеча торкаються
MOVE_CONTACTS.update({"Ref_Raise_Hand": {("hand_r", "hand_l"), ("lowerarm_r", "hand_l"), ("hand_r", "lowerarm_l"),
                                         ("upperarm_r", "upperarm_l"), ("lowerarm_r", "lowerarm_l"), ("upperarm_r", "lowerarm_l")}})
