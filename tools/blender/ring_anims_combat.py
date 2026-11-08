# Новий бій «усе в центрі рингу» (ТЗ docs/ring/ANIM_BRIEF_COMBAT_CENTER.md, сесія «Ринг», 08.11.2026). Виконується
# всередині ring_anims.py (exec) після ring_anims_ref.py: бере ті самі пози (stance, lying, body, arms_on_mat …).
#
# Власник 08.11.2026: «хочу шоб наша гра гралась більше як xcom але трохи динамічніше через анімації прийомів, можна в
# сіквенції додавать QTE але шоб персонажі не бігали по рингу як зараз. Утримання та прийоми навіть прижки с канатів
# мають бути по центру ринга, не с краю. Дивися на офф ігри WWE як приклад анімацій»; про генерацію: «Роби нехай генерує».
#
# Правила (ТЗ §1): таз кожного з пари — не далі 0,5 м від точки старту (охоронець — LIMITS["drift"] для CENTER_RULE);
# бігу й ходьби немає; кліпи починаються й закінчуються у вузлах (stance, lying_back, lying_front, kneel_up, pinned_*,
# armbar_*, leglock_*), тож ланцюг стикується без ходьби. Таймінги — docs/research/RING_ANIM_REFERENCES.md §8.1.
# Мітки: contact (хіт-стоп і такт режисера), sell (перший кадр продажу), qte_open/qte_close (видовищне QTE), kickout,
# count_N. Ефектний хвіст «*_Hype» до qte_close — той самий кадр, що й у звичайного (режисер перемикає без змішування).

def _ph(t, L, period):
    """Фаза періодичного руху, що ціле число разів вкладається в петлю L (стик петлі без ривка)."""
    n = max(1, round(L / period))
    return 2 * math.pi * t * n / L


def _win(t, a, b, c, d):
    """Вікно 0→1→0: росте на [a, b], тримається до c, спадає до d (повільний вхід і вихід)."""
    return track(t, [(-99.0, 0.0), (a, 0.0), (b, 1.0), (c, 1.0), (d, 0.0), (99.0, 0.0)])


# ================================================================ вузол «на колінах» (kneel_up)
# Обидва коліна на маті, тулуб майже рівно, руки на стегнах. Сюди приходить нападник після кікауту, після переможного
# рахунку й після больового; звідси — Kneel_To_Stance (на ноги) або Kneel_Disbelief (руки до голови, §8.1 №8).

def kneel_up(t=0.0, L=2.4, lean=0.0):
    tz = BODY["thigh"] * 0.96 + 0.07
    p = RPose(forced=set())
    p.pelvis = off_for(Vector((0.0, 0.02, tz)))
    p.pitch = 12.0 + 30.0 * lean + breathe(t, L, 1.0)
    p.head_pitch = -8.0
    tip = Quaternion(RIGHT, math.radians(-75.0))
    p.ankles = (Vector((0.17, 0.42 * BODY["leg"], 0.11)), Vector((-0.17, 0.42 * BODY["leg"], 0.11)))
    p.foot_rot = (tip, tip)
    p.knees = (Vector((0.18, -0.5, 0.0)), Vector((-0.18, -0.5, 0.0)))
    p.hands = {"l": (lambda S: S["pelvis"] + Vector((0.13, -0.2, -0.04)), FWD + DOWN * 0.6, UP + FWD * 0.2),
               "r": (lambda S: S["pelvis"] + Vector((-0.13, -0.2, -0.04)), FWD + DOWN * 0.6, UP + FWD * 0.2)}
    p.elbows = {"l": lambda S: S["l"] + Vector((0.3, 0.15, -0.25)), "r": lambda S: S["r"] + Vector((-0.3, 0.15, -0.25))}
    p.fist = {"l": 0.3, "r": 0.3}
    return p


KNEEL_RISE_LEN = 1.4

def clip_kneel_to_stance(t, L):
    """З колін на ноги без кроку з місця: ліва стопа виходить уперед (коліно ще на маті), руки на ліве коліно, поштовх,
    права стопа підтягується до стійки. Таз не відходить від місця (≤ 0,3 м)."""
    lk = BODY["leg"]
    k0 = kneel_up(t, L)
    st = stance(t, L, sway=0.0)
    tz = BODY["thigh"] * 0.96 + 0.07
    fl0, fr0 = k0.ankles
    fl1 = foot(0.2, -0.24 * lk)                                 # ліва — уперед, на всю стопу
    up = smooth((t - 0.45) / 0.6)                              # підйом тазом
    p = RPose(forced=set())
    lean = _win(t, 0.25, 0.55, 0.7, 1.2)                        # нахил уперед над лівим коліном перед поштовхом
    pel_kn = Vector((0.0, 0.02 - 0.06 * smooth(t / 0.4), tz))
    pel_st = REST["pelvis"] + st.pelvis
    p.pelvis = off_for(lerp(pel_kn, pel_st, up) + Vector((0.0, -0.05 * lean, -0.03 * lean)))
    p.pitch = lerp(12.0, st.pitch, up) + 22.0 * lean
    p.head_pitch = lerp(-8.0, st.head_pitch, up) - 8.0 * lean
    sl = feet_track(t, [(0.0, fl0), (0.08, fl0), (0.42, fl1), (1.1, fl1), (1.32, foot(0.2, -0.105))])[0]
    sr = feet_track(t, [(0.0, fr0), (0.7, fr0), (1.05, foot(-0.2, 0.075)), (L, foot(-0.2, 0.075))])[0]
    p.ankles = (sl, sr)
    tip = Quaternion(RIGHT, math.radians(-75.0))
    rl = tip.slerp(Quaternion(UP, math.radians(10.0)), smooth((t - 0.1) / 0.3))
    rr = tip.slerp(Quaternion(UP, math.radians(-14.0)), smooth((t - 0.72) / 0.3))
    p.foot_rot = (rl, rr)
    kl = lerp(Vector((0.18, -0.5, 0.0)), sl + Vector((0.22, -0.9, 0.45)), smooth((t - 0.1) / 0.3))
    kr = lerp(Vector((-0.18, -0.5, 0.0)), sr + Vector((-0.22, -0.9, 0.45)), smooth((t - 0.7) / 0.35))
    p.knees = (kl, kr)
    # руки: зі стегон — на ліве коліно (поштовх), далі — у стійку
    on_knee = _win(t, 0.3, 0.55, 0.75, 1.05)
    kh = k0.hands
    sh = st.hands
    def hand(s_):
        def g(S):
            base = lerp(kh[s_][0](S), sh[s_][0](S), up)
            knee = S["pelvis"] + Vector((0.16 if s_ == "l" else 0.06, -0.38 * lk, -0.12))
            return lerp(base, knee, on_knee)
        return (g, (FWD + DOWN * 0.6).lerp(sh[s_][1], up), (UP + FWD * 0.2).lerp(sh[s_][2], up))
    p.hands = {"l": hand("l"), "r": hand("r")}
    p.elbows = {s_: (lambda S, s_=s_: k0.elbows[s_](S).lerp(st.elbows[s_](S), up)) for s_ in "lr"}
    p.fist = {"l": 0.3 - 0.05 * up, "r": 0.3 - 0.05 * up}
    return p


DISBELIEF_LEN = 2.4

def clip_kneel_disbelief(t, L):
    """На колінах: руки до голови («як він вирвався?», §8.1 №8: ~2,5 с), погляд на рефері, назад до стегон."""
    p = kneel_up(t, L)
    k = _win(t, 0.15, 0.55, 1.75, 2.2)
    shake = math.sin(2 * math.pi * (t - 0.55) / 0.55) * k if 0.55 < t < 1.75 else 0.0
    p.pitch = 12.0 - 10.0 * k
    p.head_pitch = -8.0 - 14.0 * k
    p.head_yaw = 35.0 * k + 8.0 * shake
    p.roll = 4.0 * shake
    kh = p.hands
    top = {"l": lambda S: S["head"] + Vector((0.11, 0.03, 0.08)), "r": lambda S: S["head"] + Vector((-0.11, 0.03, 0.08))}
    p.hands = {s_: (lambda S, s_=s_: lerp(kh[s_][0](S), top[s_](S), k) + Vector((0.15 * (1 if s_ == "l" else -1), -0.22, 0.0)) * math.sin(math.pi * k) * 0.7,
                    (FWD + DOWN * 0.6).lerp(UP + BACK * 0.3, k), (UP + FWD * 0.2).lerp(RIGHT if s_ == "l" else LEFT, k)) for s_ in "lr"}
    ke = p.elbows
    p.elbows = {s_: (lambda S, s_=s_: ke[s_](S).lerp(S[s_] + Vector((0.4 * (1 if s_ == "l" else -1), -0.1, 0.25)), k)) for s_ in "lr"}
    p.fist = {"l": 0.3 - 0.15 * k, "r": 0.3 - 0.15 * k}
    return p


# ================================================================ утримання (P1-1)
# Пара — як у Cover: A у нулі обличчям у −Y, B лежить поперек перед ним (зсув (0,12; −0,6), 90°), голова B — праворуч
# від A. A стає навколішки біля B і лягає грудьми на його груди, ліва рука чіпляє ближню ногу. На кожному «раз / два /
# три» A лежить на B (вузол pinned_*); петля утримання довша за весь рахунок (6,4 с > 5,5 с від накриття до «три»).
# Кікаут — три гілки: на «раз» (вибух), на «два» (звичайний), «на два з половиною» (в останню мить, ледве плечем).

PIN_OFF = Vector((0.12, -0.6, 0))
PIN_HOLD_LEN = 6.4           # петля: 4 цикли тиску по 1,6 с (= крок рахунку рефері)


PIN_KNEE_Y = 0.06            # коліна A — на маті позаду від боку B (бік B — y ≈ −0,42), стегна похилі вперед


def pin_a(thrown=0.0, press=0.0, breath=0.0, lift=0.0):
    """A лежить грудьми впоперек грудей B: коліна на маті, стегна похилі (~42° від вертикалі), тулуб горизонтально;
    ліва рука чіпляє ближню ногу B під коліном, права — через дальнє плече B, долоня на маті.
    thrown — підкинуло (кікаут): тулуб і таз ідуть угору-назад на коліна; press — тисне вагою; lift — плечі B підняли A."""
    tl = BODY["thigh"] * 0.97
    th = math.radians(42.0 - 34.0 * thrown - 4.0 * lift)
    kx = -0.12
    ky = PIN_KNEE_Y - 0.3 * (1.0 - BODY["leg"])      # коротші ноги (жіночий набір) — коліна ближче до B, груди ті самі
    abs_pel = Vector((kx, ky - tl * math.sin(th), 0.085 + tl * math.cos(th) - 0.015 * press + 0.04 * lift))
    p = RPose()
    p.pelvis = off_for(abs_pel)
    p.pelvis_q = Quaternion(LEFT, math.radians(55.0 - 42.0 * thrown - 10.0 * lift))
    p.pitch = 37.0 + 1.5 * breath - 30.0 * thrown - 22.0 * lift + 2.0 * press
    p.roll = -4.0
    ay = ky + BODY["calf"] * 0.97
    p.ankles = (Vector((kx + 0.16, ay, 0.11)), Vector((kx - 0.16, ay, 0.11)))
    tip = Quaternion(RIGHT, math.radians(-75.0))
    p.foot_rot = (tip, tip)
    p.knees = (Vector((kx + 0.17, -0.7, 0.0)), Vector((kx - 0.17, -0.7, 0.0)))
    p.forced = set()
    def hook(S):                                   # ліва — під коліном B (ближня нога), передпліччя під гомілкою
        k_ = P(S, "calf_l", 0.1, Vector((0.0, 0.0, -0.07)))
        return (k_ or S["l"] + Vector((0.3, 0.05, -0.3)))
    def far(S):                                    # права — через дальнє плече B, долоня на маті за ним (біля шиї)
        sh = P(S, "upperarm_r", 0.0)
        if sh is None:
            return S["r"] + Vector((-0.1, -0.3, -0.35))
        return Vector((sh.x - 0.12, sh.y - 0.15, 0.055 + 0.01 * press))
    free_l = lambda S: S["l"] + Vector((0.2, 0.0, -0.35))
    free_r = lambda S: S["r"] + Vector((-0.22, 0.0, -0.3))
    # відпускання — не крізь B: ліва спершу виходить з-під коліна до себе, права спершу вгору над грудьми B
    out_ = smooth(min(1.0, max(thrown / 0.45, 1.4 * lift)))     # руки знімаються вже тоді, коли B лише піднімає плечі
    go_ = smooth(max(0.0, (thrown - 0.3) / 0.7))
    def hl(S):
        return (hook(S) + Vector((0.0, 0.2 * out_, 0.06 * out_))).lerp(free_l(S), go_)
    def hr(S):
        return (far(S) + Vector((0.0, 0.16 * out_, 0.42 * out_))).lerp(free_r(S), go_)
    p.hands = {"l": (hl, LEFT + DOWN * 0.3, DOWN * 0.3 + BACK),
               "r": (hr, FWD + DOWN * 0.4, UP)}
    p.elbows = {"l": lambda S: S["l"] + Vector((0.4, 0.15, -0.05)), "r": lambda S: S["r"] + Vector((-0.4, 0.0, 0.3))}
    p.head_pitch = -5.0 - 5.0 * thrown
    p.head_yaw = 30.0 - 55.0 * thrown                 # обличчям до рефері біля голови B
    p.fist = {"l": 0.6, "r": 0.15}
    return p


def pin_drop(t, L, t_land):
    """Зі стійки — на коліна й грудьми на B: присід-передчуття, падіння, стопи відриваються й ідуть назад."""
    st = stance(t, L, sway=0.0)
    pn = pin_a()
    w = track(t, [(0.0, 0.0), (0.14, 0.06), (t_land, 1.0), (L + 1.0, 1.0)])
    crouch = _win(t, 0.0, 0.14, 0.16, 0.3)
    p = blend_pose(_filled(st), _filled(pn), w)
    p.pelvis = p.pelvis + Vector((0.0, 0.0, -0.08 * crouch))
    p.pitch = p.pitch + 10.0 * crouch
    sl, sr = st.ankles or (foot(0.2, -0.105), foot(-0.2, 0.075))
    p.ankles = (feet_track(t, [(0.0, foot(0.2, -0.105)), (0.12, foot(0.2, -0.105)), (t_land - 0.04, pn.ankles[0]), (L + 1, pn.ankles[0])])[0],
                feet_track(t, [(0.0, foot(-0.2, 0.075)), (0.16, foot(-0.2, 0.075)), (t_land, pn.ankles[1]), (L + 1, pn.ankles[1])])[0])
    p.knees = (lerp(p.ankles[0] + Vector((0.22, -0.9, 0.45)), pn.knees[0], smooth(w)),
               lerp(p.ankles[1] + Vector((-0.22, -0.9, 0.45)), pn.knees[1], smooth(w)))
    return p


def pin_b(pinned=1.0, struggle=0.0, w=0.0, kick=0.0, sit_side=0.0, legs=0.0, tuck=1.0):
    """B лежить під A: struggle — борсається (0..1), w — фаза (−1..1); kick — плече вгору (кікаут); sit_side — одне
    плече (ліве) відривається від мату; legs — ноги б'ють угору."""
    p = lying(sit=0.45 * kick + 0.06 * struggle * max(0.0, w),
              knee_bend=0.15 + 0.05 * pinned + 0.3 * kick + 0.25 * struggle * (0.5 + 0.5 * w) + 0.35 * legs)
    kb_r = 0.15 + 0.07 * struggle * (0.5 + 0.5 * w) + 0.2 * kick + 0.4 * legs
    kb_l = 0.15 + 0.35 * pinned + 0.2 * kick + 0.2 * struggle * (0.5 - 0.5 * w) + 0.1 * legs    # ближнє коліно — у гаку A
    off_b = off_for(Vector((0.0, 0.0, LIE_Z)))
    p.ankles = (xf(off_b, Q_BACK, REST["ankle_l"] + Vector((0.0, -0.25 * kb_l, 0.35 * kb_l))),
                xf(off_b, Q_BACK, REST["ankle_r"] + Vector((0.0, -0.25 * kb_r, 0.35 * kb_r))))
    p.roll = 8.0 * struggle * w - 18.0 * sit_side
    p.head_yaw = 25.0 * struggle * w
    q = Q_BACK
    arms_on_mat(p, q, lift_l=0.0, lift_r=0.27 * kick + 0.35 * struggle * max(0.0, -w) + 0.25 * sit_side)
    # ліва (бік A) — уздовж тіла, під колінами A не лежить (розкинута — коліна A на ній, 3 см по сітках)
    # ліва (бік A): розкинута на маті лежала б під колінами A — тому, поки A падає, B підтягує її вздовж тіла (tuck),
    # а після кікауту чи рахунку — знов розкидає (вузол lying_back)
    spread_l, elb_l = p.hands["l"][0], p.elbows["l"]
    tk_ = smooth(tuck)
    tucked = lambda S: S["l"] + q @ (Vector((0.08 + 0.1 * sit_side, 0.3 + 0.04 * kick, -0.26)) * BODY["arm"])
    p.hands = dict(p.hands)
    p.hands["l"] = (lambda S: spread_l(S).lerp(tucked(S), tk_) + Vector((0.0, 0.0, 0.08 * math.sin(math.pi * tk_))), q @ DOWN, q @ FWD)
    p.elbows = dict(p.elbows)
    p.elbows["l"] = lambda S: elb_l(S).lerp(S["l"] + q @ Vector((0.3, 0.1, -0.1)), tk_)
    return p


COVER_ENTER_LEN = 1.0
COVER_LAND = 0.5             # с — груди A на грудях B (мітка contact)

def clip_cover_enter_a(t, L, partner=None):
    p = pin_drop(t, L, COVER_LAND)
    pr = _win(t, COVER_LAND - 0.05, COVER_LAND + 0.06, COVER_LAND + 0.1, COVER_LAND + 0.35)
    p.pelvis = p.pelvis + Vector((0.0, 0.0, -0.02 * pr))
    # гак: ліва спершу лягає біля коліна B зі свого боку, потім заходить під нього, поки B підтягує коліно
    g0, f0, u0 = p.hands["l"]
    side = 1.0 - smooth((t - 0.42) / 0.3)
    p.hands = dict(p.hands)
    p.hands["l"] = (lambda S: g0(S) + Vector((0.0, 0.14, 0.07)) * side, f0, u0)
    return p

def clip_cover_enter_b(t, L, partner=None):
    hit = hit_spring(t, COVER_LAND, w=16.0, z=0.35) if t > COVER_LAND else 0.0
    p = pin_b(pinned=smooth((t - 0.42) / 0.35), struggle=0.0, tuck=smooth((t - 0.06) / 0.34))
    p.pitch = p.pitch + 5.0 * abs(hit)                         # вагою на груди — плечі й голова здригаються
    p.head_pitch = p.head_pitch + 10.0 * abs(hit)
    return p


def clip_cover_hold_a(t, L, partner=None):
    pr = 0.5 - 0.5 * math.cos(_ph(t, L, 1.6))                  # тисне вагою в такт рахунку
    return pin_a(press=pr, breath=math.sin(_ph(t, L, 3.2)))

def clip_cover_hold_b(t, L, partner=None):
    env = 0.55 + 0.45 * math.sin(_ph(t, L, 3.2))               # хвилі відчаю: то борсається, то затихає
    w = math.sin(_ph(t, L, 0.8))
    return pin_b(struggle=env * smoothstep_edges(t, L), w=w)


def smoothstep_edges(t, L, e=0.35):
    """1 у середині петлі, 0 на краях — щоб петля стикувалась точно з вузлом pinned_* (накриття, кікаути)."""
    return smooth(min(1.0, t / e)) * smooth(min(1.0, (L - t) / e))


class Kick:
    """Кікаут з утримання. t_k — мить, коли плече відривається (мітка kickout); power — сила (на «раз» — вибух);
    lift — наскільки піднімає A; side — одне плече; legs — ноги б'ють угору; slow — темп."""
    def __init__(self, name, t_k, power, legs, side, slow, L):
        self.name, self.t_k, self.power, self.legs, self.side, self.slow, self.L = name, t_k, power, legs, side, slow, L


KICKS = [
    Kick("Cover_Kickout_1", 0.18, 1.25, 1.0, 0.0, 0.8, 2.4),          # на «раз»: вибух, ноги вгору, A відлітає
    Kick("Cover_Kickout_2", 0.25, 1.0, 0.5, 0.3, 1.0, 2.4),           # на «два»: звичайний
    Kick("Cover_Kickout_2_Half", 0.32, 0.55, 0.0, 1.0, 1.35, 2.6),    # «два з половиною»: в останню мить, одним плечем
]


def kick_a(K):
    def f(t, L, partner=None):
        tk = K.t_k
        thrown = smooth((t - tk + 0.06) / (0.38 * K.slow)) * min(1.0, K.power)
        carried = _win(t, tk - 0.14 * K.slow, tk + 0.04, tk + 0.12 * K.slow, tk + 0.5 * K.slow) * min(1.0, K.power)
        p = pin_a(thrown=thrown, lift=carried * (1.0 - 0.6 * thrown))
        rise = smooth((t - (tk + 0.45 * K.slow)) / (0.7 * K.slow))
        if rise > 0.0:
            p = blend_pose(_filled(p), _filled(kneel_up(t, L)), rise)
        return p
    return f


def kick_b(K):
    def f(t, L, partner=None):
        tk = K.t_k
        kick = _win(t, tk - 0.14 * K.slow, tk + 0.04, tk + 0.12 * K.slow, tk + 0.7 * K.slow) * min(1.0, K.power)
        side = _win(t, tk - 0.2 * K.slow, tk, tk + 0.25, tk + 0.8) * K.side
        legs = _win(t, tk - 0.12, tk + 0.05, tk + 0.12, tk + 0.55) * K.legs
        pinned = 1.0 - smooth((t - tk) / 0.6)
        # після кікауту — лежить, важко дихає (продаж утримання), голова до рефері
        sell = smooth((t - tk - 0.5) / 0.6) * (1.0 - smooth((t - (L - 0.5)) / 0.4))
        tuck = 1.0 - smooth((t - (tk + 0.5 * K.slow)) / 0.6)
        p = pin_b(pinned=pinned, struggle=0.3 * sell, w=math.sin(2 * math.pi * t / 0.9), kick=kick, sit_side=side, legs=legs,
                  tuck=tuck)
        p.head_yaw = (p.head_yaw or 0.0) + 20.0 * sell
        return p
    return f


RELEASE_LEN = 1.6

def clip_cover_release_a(t, L, partner=None):
    """Після «три»: A ще мить лежить, потім відштовхується від B і стає навколішки (далі — Kneel_To_Stance)."""
    up = smooth((t - 0.35) / 0.8)
    p = pin_a(thrown=0.6 * up)
    if up > 0.4:
        p = blend_pose(_filled(p), _filled(kneel_up(t, L)), smooth((up - 0.4) / 0.6))
    return p

def clip_cover_release_b(t, L, partner=None):
    return pin_b(pinned=1.0 - smooth((t - 0.3) / 0.9), tuck=1.0 - smooth((t - 0.85) / 0.6))   # нерухомо: переможений


# ================================================================ рахунок рефері з кроком 1,6 с (P1-1, §8.1 №8–10)
# Наявні Ref_Count_Pin_3/2 (крок 0,95 с) лишаються для старого режисера. Нові — з тим самим жестом, крок 1,6 с:
# від накриття до «три» ≈ 5,5 с (референс 5,6 с). Кікаут «на два з половиною» — рука вже йде на «три» й зупиняється
# над матом за 0,3 с до удару (§8.1 №9).
SLOW_BEAT = 1.6
SLOW3 = [COUNT_FIRST + i * SLOW_BEAT for i in range(3)]

def slap_partial(t, tc, stop=0.3):
    """Висота руки, що пішла на удар у мить tc, але завмерла за stop с до мату (0 — мат, 1 — угорі)."""
    up0, top = tc - 0.6, tc - 0.3
    if t < up0:
        return 0.0
    if t < top:
        return smooth((t - up0) / (top - up0))
    stop_t = tc - stop * 0.5
    if t < stop_t:
        return 1.0 - ((t - top) / (tc - top)) ** 2
    return 1.0 - ((stop_t - top) / (tc - top)) ** 2

def clip_ref_count3_slow(t, L):
    return count_pose(t, L, SLOW3)

def clip_ref_count2_slow(t, L):
    return count_pose(t, L, SLOW3[:2], show_two=True)

def clip_ref_count1_slow(t, L):
    return count_pose(t, L, SLOW3[:1], show_two=True)

def clip_ref_count2half(t, L):
    p = count_pose(t, L, SLOW3[:2])
    h3 = slap_partial(t, SLOW3[2])
    if t > SLOW3[2] - 0.62:
        hr = p.hands["r"]
        def g(S, hr=hr):
            mat = Vector((S["r"].x - 0.06, S["r"].y - 0.12, 0.05))
            top = S["r"] + Vector((-0.08, -0.12, 0.42))
            stopped = smooth((t - (SLOW3[2] - 0.15)) / 0.35)
            return lerp(lerp(mat, top, h3), S["r"] + Vector((-0.1, -0.35, 0.2)), stopped)   # завмер — і «два пальці»
        p.hands = dict(p.hands)
        p.hands["r"] = (g, DOWN * (1 - h3) + (UP + FWD * 0.4) * h3, FWD)
    return p


# ================================================================ реєстр
for _nm, _ln, _fa, _fb, _loop in [
    ("Cover_Enter", COVER_ENTER_LEN, clip_cover_enter_a, clip_cover_enter_b, False),
    ("Cover_Hold_Loop", PIN_HOLD_LEN, clip_cover_hold_a, clip_cover_hold_b, True),
    ("Cover_Release", RELEASE_LEN, clip_cover_release_a, clip_cover_release_b, False),
] + [(K.name, K.L, kick_a(K), kick_b(K), False) for K in KICKS]:
    MOVE_CLIPS.append((_nm, _ln, _fa, _fb, _loop, PIN_OFF.copy(), 90))
    CENTER_RULE.add(_nm)
    MOVE_CONTACTS[_nm] = CLIP_CONTACTS["Cover"] | {("thigh_l", "upperarm_l"), ("thigh_r", "upperarm_l")}

CLIP_DEFINES["Cover_Enter"] = {"A": "pinned_A", "B": "pinned_B"}
KEEP_OFFSET.add("Cover_Enter")
MOVE_CLIP_NODES.update({
    "Cover_Enter": {"A": ("stance", "pinned_A"), "B": ("lying_back", "pinned_B")},
    "Cover_Hold_Loop": {"A": ("pinned_A", "pinned_A"), "B": ("pinned_B", "pinned_B")},
    "Cover_Release": {"A": ("pinned_A", "kneel_up"), "B": ("pinned_B", "lying_back")},
})
for K in KICKS:
    MOVE_CLIP_NODES[K.name] = {"A": ("pinned_A", "kneel_up"), "B": ("pinned_B", "lying_back")}
MOVE_EVENTS.update({
    "Cover_Enter": [("contact", COVER_LAND, "spine_03", "spine_03")],
    "Cover_Release": [],
})
for K in KICKS:
    MOVE_EVENTS[K.name] = [("kickout", K.t_k, "-", "upperarm_l"), ("sell", K.t_k + 0.5 * K.slow, "-", "spine_03")]
COVER_SHIFT = set()          # зсув пари для жіночого набору (як у Cover) не потрібен: рука B не лежить під колінами A

MOVE_NODE_POSES["kneel_up"] = lambda: kneel_up(0.0, 2.4)
for _nm, _ln, _fn in [("Kneel_To_Stance", KNEEL_RISE_LEN, clip_kneel_to_stance),
                      ("Kneel_Disbelief", DISBELIEF_LEN, clip_kneel_disbelief),
                      ("Ref_Count_Pin_3_Slow", SLOW3[2] + 0.9, clip_ref_count3_slow),
                      ("Ref_Count_Pin_2_Slow", SLOW3[1] + 1.3, clip_ref_count2_slow),
                      ("Ref_Count_Pin_1_Slow", SLOW3[0] + 1.3, clip_ref_count1_slow),
                      ("Ref_Count_Pin_2_Half", SLOW3[2] + 1.0, clip_ref_count2half)]:
    MOVE_CLIPS.append((_nm, _ln, _fn, None, False, None, 0))
    CENTER_RULE.add(_nm)
MOVE_CLIP_NODES.update({"Kneel_To_Stance": {"A": ("kneel_up", "stance")},
                        "Kneel_Disbelief": {"A": ("kneel_up", "kneel_up")}})
MOVE_EVENTS.update({
    "Ref_Count_Pin_3_Slow": [("count_%d" % (i + 1), tc, "hand_r", "-") for i, tc in enumerate(SLOW3)],
    "Ref_Count_Pin_2_Slow": [("count_%d" % (i + 1), tc, "hand_r", "-") for i, tc in enumerate(SLOW3[:2])]
                            + [("kickout", SLOW3[1] + 0.2, "-", "-")],
    "Ref_Count_Pin_1_Slow": [("count_1", SLOW3[0], "hand_r", "-"), ("kickout", SLOW3[0] + 0.2, "-", "-")],
    "Ref_Count_Pin_2_Half": [("count_%d" % (i + 1), tc, "hand_r", "-") for i, tc in enumerate(SLOW3[:2])]
                            + [("kickout", SLOW3[2] - 0.3, "-", "-")],
})
