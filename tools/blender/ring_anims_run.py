# Біг на ринзі — 15 варіантів одного параметричного циклу (власник 29.10.2026: «Біг … Усіх варіант по 15»,
# «Подивись як саме в реслінгу»). Виконується всередині tools/blender/ring_anims.py (exec) після ring_anims_move.py.
#
# Цикл (ключові пози бігового циклу аніматорів, своїми словами): КОНТАКТ — стопа ставиться трохи попереду таза;
# НИЗ — опорне коліно амортизує, таз найнижче; ПРОХІД — махова п'ята підтягується, коліно йде вперед;
# ВІДШТОВХУВАННЯ — опорна нога випрямлена позаду, коротка фаза польоту. Руки — у протифазі, лікті ~90°;
# таз і плечі скручуються назустріч (перекриття — ring_anims.overlap).
# Біг у реслінгу (основи тренування, своїми словами): ринг малий — від каната до каната ~3 кроки; корпус майже
# прямий (нахил 5–8°, не як у спринтера), кроки важкі, коротка робота рук. Удар об канати — боком (ring_anims_ropes).

class Run:
    """Параметри варіанту бігу."""
    def __init__(self, name, L, stride, duty=0.25, lean=7.0, bounce=0.03, sink=0.06, kick=0.3, knee=0.16,
                 arms=0.17, elbow=0.0, twist=7.0, head=-4.0, width=0.1, limp=0.0, heavy=0.0, fist=0.55):
        self.__dict__.update({k: v for k, v in locals().items() if k != "self"})

# Манера за статтю (власник 29.10.2026: «різні люди по різному це роблять, чоловіки та дівчата»): у жінок вужча
# постановка стоп (майже по одній лінії), більше обертання таза й менше — плечей, лікті ближче до тіла, коротша
# амплітуда рук, легший відскок угору.
STYLE = {"width": 0.6, "pelvis_twist": 1.5, "shoulder_twist": 0.7, "arms": 0.8, "elbow_in": 0.06, "bounce": 1.15}     if globals().get("FEMALE") else {"width": 1.0, "pelvis_twist": 1.0, "shoulder_twist": 1.0, "arms": 1.0, "elbow_in": 0.0, "bounce": 1.0}

def run_pose(t, R):
    """Поза бігу в час t для варіанту R. Шлях — уздовж −Y зі швидкістю stride/L; петля замкнена."""
    L, v = R.L, R.stride / R.L
    ST = STYLE
    u = (t / L) % 1.0
    p = RPose(forced=set())
    # таз нижче після контакту кожної ноги (двічі за цикл), вище у фазі польоту; кульгання — глибше на хвору
    k = math.cos(2 * math.pi * 2 * (u - R.duty * 0.5))
    dz = -R.sink - R.bounce * ST["bounce"] * 0.5 * k - R.limp * 0.04 * max(0.0, math.sin(2 * math.pi * u)) - 0.02 * R.heavy
    p.pelvis = Vector((0.0, -v * t, dz))
    p.pitch = R.lean + 4.0 * R.heavy
    c = math.cos(2 * math.pi * u)
    p.pelvis_yaw = R.twist * ST["pelvis_twist"] * c         # таз за опорною ногою
    p.yaw = -R.twist * 1.6 * ST["shoulder_twist"] * c         # плечі — назустріч тазу
    p.roll = 2.5 * math.sin(2 * math.pi * u) + 6.0 * R.limp * math.sin(2 * math.pi * u)
    p.head_pitch = R.head
    p.head_yaw = 0.0
    span = v * R.duty * L
    ahead, behind = span * 0.4, span * 0.6         # ставиться ближче під таз, відривається далеко позаду
    def foot_at(ph, side):
        x = R.width * ST["width"] * (1 if side == "l" else -1)
        t0 = t - ph * L                              # момент контакту цієї ноги
        if ph < R.duty:                              # опора: стопа нерухома у світі
            kk = ph / R.duty
            heel = 0.09 * smooth(max(0.0, kk - 0.55) / 0.45)        # поштовх: п'ята йде вгору, ступня на носку
            return Vector((x, -v * t0 - ahead, 0.071 + heel)), -10.0 + 45.0 * kk, False
        s = (ph - R.duty) / (1 - R.duty)             # мах: від відриву позаду до наступного контакту попереду
        y0 = -v * t0 - ahead                         # відрив там, де стояла (стопа не ковзає)
        y1 = -v * (t0 + L) - ahead
        back = math.sin(math.pi * min(1.0, s / 0.7)) if s < 0.7 else 0.0
        kick = R.kick * (1 - 0.5 * R.heavy) * (1 - (R.limp if side == "l" else 0.0))
        sh = min(1.0, s / 0.85)                      # стопа виходить на точку контакту раніше, ніж торкнеться мату
        yy = y0 + (y1 - y0) * smooth(sh) + 0.14 * back * (1 - 0.4 * R.heavy)   # п'ята позаду під сідницями
        z = 0.071 + 0.09 * (1 - smooth(min(1.0, s / 0.2))) + kick * back * 0.55 + 0.06 * math.sin(math.pi * s)
        return Vector((x, yy, z)), 40.0 * back - 15.0 * smooth(max(0.0, s - 0.7) / 0.3), True
    ankles, rots, knees = [], [], []
    for side, ph in (("l", u), ("r", (u + 0.5) % 1.0)):
        a, pitch, swing = foot_at(ph, side)
        # махова стопа не далі, ніж дістає нога (від кульшового суглоба): інакше IK «не дістає», нога пряма й смикається
        hip = REST["hip_" + side] + p.pelvis
        leg = (REST["hip_" + side] - REST["ankle_" + side]).length * 0.97
        if swing and (a - hip).length > leg:
            a = hip + (a - hip).normalized() * leg
            a.z = max(a.z, 0.071)
        ankles.append(a)
        yaw = 5.0 if side == "l" else -5.0
        rots.append(Quaternion(UP, math.radians(yaw)) @ Quaternion(LEFT, math.radians(-pitch)))
        knees.append(a + Vector((0.06 if side == "l" else -0.06, -(0.9 + (R.knee * 2.0 if swing else 0.0)), 0.45)))
    p.ankles, p.foot_rot, p.knees = tuple(ankles), tuple(rots), tuple(knees)
    # руки: протифаза до ніг, лікті ~90°, коротка робота
    sw = R.arms * ST["arms"]
    def hand(side):
        sgn = 1 if side == "l" else -1
        ph = math.cos(2 * math.pi * u) * (-sgn)      # +1 — рука вперед
        return lambda S: S[side] + Vector((0.1 * sgn, -0.06 - sw * ph, -0.32 + 0.1 * max(0.0, ph) + R.elbow))   # кисті вздовж тіла, не поперек грудей
    p.hands = {s_: (hand(s_), FWD + UP * 0.5, LEFT if s_ == "l" else RIGHT) for s_ in "lr"}
    ei = ST["elbow_in"]
    p.elbows = {"l": lambda S: S["l"] + Vector((0.14 - ei, 0.32, -0.28)), "r": lambda S: S["r"] + Vector((-0.14 + ei, 0.32, -0.28))}
    p.fist = {"l": R.fist, "r": R.fist}
    return p

RUNS = [
    Run("Run_Jog_Loop", 0.8, 2.0, duty=0.29, lean=4.0, bounce=0.025, sink=0.04, kick=0.2, knee=0.1, arms=0.12, twist=5.0),
    Run("Run_Loop", 0.66, 2.6),
    Run("Run_Sprint_Loop", 0.58, 3.1, duty=0.21, lean=12.0, bounce=0.035, sink=0.07, kick=0.45, knee=0.24, arms=0.19, twist=9.0),
    Run("Run_Tired_Loop", 0.76, 2.0, duty=0.29, lean=14.0, bounce=0.02, sink=0.08, kick=0.18, knee=0.08, arms=0.08, elbow=-0.06, head=12.0, fist=0.2),
    Run("Run_Limp_Loop", 0.86, 1.8, duty=0.30, lean=8.0, bounce=0.03, sink=0.06, kick=0.18, knee=0.08, arms=0.1, limp=1.0),
    Run("Run_Heavy_Loop", 0.76, 2.3, duty=0.22, lean=8.0, bounce=0.02, sink=0.09, kick=0.18, knee=0.1, arms=0.14, twist=11.0, heavy=1.0),
    Run("Run_Light_Loop", 0.6, 2.7, duty=0.22, lean=8.0, bounce=0.045, sink=0.05, kick=0.42, knee=0.22, arms=0.2, twist=6.0, fist=0.3),
    Run("Run_Technic_Loop", 0.66, 2.5, duty=0.30, lean=6.0, bounce=0.03, sink=0.06, kick=0.3, knee=0.16, arms=0.16, twist=5.0),
    Run("Run_Angry_Loop", 0.6, 2.8, duty=0.22, lean=14.0, bounce=0.03, sink=0.08, kick=0.38, knee=0.2, arms=0.24, twist=11.0, head=-12.0, fist=0.9),
    Run("Run_Showoff_Loop", 0.7, 2.4, duty=0.21, lean=2.0, bounce=0.045, sink=0.05, kick=0.3, knee=0.18, arms=0.24, twist=9.0, head=-14.0),
    Run("Run_Low_Loop", 0.64, 2.6, duty=0.30, lean=18.0, bounce=0.02, sink=0.12, kick=0.3, knee=0.16, arms=0.16, twist=7.0, head=-16.0),
    Run("Run_Hurt_Body_Loop", 0.8, 2.0, duty=0.29, lean=14.0, bounce=0.02, sink=0.07, kick=0.18, knee=0.08, arms=0.06, twist=3.0, fist=0.2),
]
RUN_LOOPS = {r.name: r for r in RUNS}

def blend_pose(a, b, k):
    """Лінійне змішування двох RPose (k = 0 — a, 1 — b): таз, кути, стопи, кисті."""
    p = RPose(forced=set())
    p.pelvis = lerp(a.pelvis, b.pelvis, k)
    for f in ("pitch", "yaw", "roll", "pelvis_yaw", "head_pitch"):
        setattr(p, f, getattr(a, f) * (1 - k) + getattr(b, f) * k)
    p.head_yaw = (a.head_yaw or 0.0) * (1 - k) + (b.head_yaw or 0.0) * k
    fa = a.ankles or (foot(0.2, -0.105), foot(-0.2, 0.075))
    fb = b.ankles or (foot(0.2, -0.105), foot(-0.2, 0.075))
    p.ankles = (lerp(fa[0], fb[0], k), lerp(fa[1], fb[1], k))
    ra = a.foot_rot or (Quaternion(), Quaternion())
    rb = b.foot_rot or (Quaternion(), Quaternion())
    p.foot_rot = (ra[0].slerp(rb[0], k), ra[1].slerp(rb[1], k))
    if a.knees and b.knees:
        p.knees = (lerp(a.knees[0], b.knees[0], k), lerp(a.knees[1], b.knees[1], k))
    p.hands = {s: lerp_hand(b.hands[s], a.hands[s], k) for s in "lr"}
    ea, eb = a.elbows, b.elbows
    p.elbows = {s: (lambda S, s=s: ea[s](S).lerp(eb[s](S), k)) for s in "lr"}
    p.fist = {s: a.fist[s] * (1 - k) + b.fist[s] * k for s in "lr"}
    return p

# старт, стоп — через деформацію часу циклу: усі положення стоп — функції одного «часу бігу», тож прискорення й
# гальмування не дають ковзання; розворот замінено короткими кроками перед канатами (ще 3 варіанти, разом 15)
def clip_run_start(t, L):
    T = 0.6 * L
    tau = t * t / (2 * T) if t < T else t - T / 2       # швидкість росте від нуля
    return run_pose(tau, RUN_LOOPS["Run_Loop"])         # з місця: стопи — з циклу, без ковзання

def clip_run_stop(t, L):
    T = 0.7 * L
    tt = min(t, T)
    tau = tt - tt * tt / (2 * T)                         # швидкість падає до нуля
    p = run_pose(tau, RUN_LOOPS["Run_Loop"])
    k = smooth(max(0.0, (t - T) / (L - T)))
    if k > 0:                                            # верх тіла — у стійку, стопи там, де стали
        st = stance(t, L, sway=0.0)
        st.pelvis = Vector((p.pelvis.x, p.pelvis.y, st.pelvis.z))
        q = blend_pose(p, st, k)
        q.ankles, q.foot_rot, q.knees = p.ankles, p.foot_rot, p.knees
        return q
    return p

RUNS.append(Run("Run_Choppy_Loop", 0.5, 1.6, duty=0.27, lean=6.0, bounce=0.02, sink=0.08, kick=0.18, knee=0.1, arms=0.12))
RUNS.append(Run("Run_Backpedal_Loop", 0.7, -1.4, duty=0.32, lean=-2.0, bounce=0.02, sink=0.07, kick=0.12, knee=0.06, arms=0.1))
RUN_LOOPS.update({r.name: r for r in RUNS[-2:]})

MOVE_CLIPS += [(r.name, r.L, (lambda t, L, r=r: run_pose(t, r)), None, True, None, 0) for r in RUNS]
MOVE_CLIPS += [("Run_Start", 0.9, clip_run_start, None, False, None, 0),
               ("Run_Stop", 0.9, clip_run_stop, None, False, None, 0)]
# вузли старту/стопу не задані: доведення до стійки рухало стопи (ковзання); перехід зі стійки змішує гра
