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

# P1-1b: рахунок зупиняється — рука завмирає, рефері випрямляється й показує пальцями, скільки було («раз» / «два»;
# «два» — і на «два з половиною», і при підкупі). Вхід і вихід — через вузол ref_kneel: Ref_Kneel_Down → рахунок →
# Ref_Kneel_Up сходяться без змішування (раніше кліп стартував уже в нахилі — стрибок тулуба ~34° на склейці).
REF_IN, REF_OUT = 0.45, 0.5
REF_SIGN = Vector((-0.17, -0.2, 0.36))       # кисть жесту від правого плеча: вище плеча, трохи вперед і вбік
REF_SIGN_EL = Vector((-0.32, -0.1, 0.0))     # лікоть — убік на висоті плеча: передпліччя вгору, пальці вгору


def _ref_node():
    return _filled(ref_kneel(0.0, 1.0))


def _ref_sign(p, t, t_stop, n):
    """Від t_stop: тулуб наполовину до вузла (випрямляється, ліва долоня з мату), права кисть угору — n пальців."""
    k = smooth((t - t_stop) / 0.35)
    if k <= 1e-4:
        return p
    hr, er = p.hands["r"], p.elbows["r"]
    q = blend_pose(_filled(p), _ref_node(), 0.6 * k)
    q.hands = dict(q.hands)
    q.elbows = dict(q.elbows)
    q.hands["r"] = lerp_hand((lambda S: S["r"] + REF_SIGN, UP, FWD), hr, k)
    q.elbows["r"] = lambda S: er(S).lerp(S["r"] + REF_SIGN_EL, k)
    f0 = q.fist["r"]
    sign = {"thumb": 1.6, "thumb_tuck": 1.0, "index": 0.0,     # великий — через долоню до безіменного
             "middle": 0.0 if n >= 2 else 1.0, "ring": 1.0, "pinky": 1.0}
    q.fingers = {"r": {f_: (0.0 if f_ == "thumb_tuck" else f0) * (1 - k) + v * k for f_, v in sign.items()}}
    return q


def _ref_edges(p, t, L):
    """Початок і кінець кліпу рахунку — точно вузол ref_kneel (пальці — теж до кулака вузла)."""
    w = max(1.0 - smooth(t / REF_IN), smooth((t - (L - REF_OUT)) / REF_OUT))
    if w <= 1e-4:
        return p
    fo = getattr(p, "fingers", None)
    n0 = _ref_node()
    q = blend_pose(_filled(p), n0, w)
    if fo:
        q.fingers = {s_: {f_: v * (1 - w) + (0.0 if f_ == "thumb_tuck" else n0.fist[s_]) * w for f_, v in d_.items()}
                     for s_, d_ in fo.items()}
    return q


def clip_ref_count3_slow(t, L):
    return _ref_edges(count_pose(t, L, SLOW3), t, L)

def clip_ref_count2_slow(t, L):
    return _ref_edges(_ref_sign(count_pose(t, L, SLOW3[:2], show_two=True), t, SLOW3[1] + 0.18, 2), t, L)

def clip_ref_count1_slow(t, L):
    return _ref_edges(_ref_sign(count_pose(t, L, SLOW3[:1], show_two=True), t, SLOW3[0] + 0.18, 1), t, L)

HALF_KICK = SLOW3[2] - 0.1         # «два з половиною»: плече відривається за 0,1 с до «три» (мітка kickout рефері)
HALF_STOP = 0.08                    # рука завмирає за 0,04 с до мату-удару — ~12 см над матом (h ≈ 0,25)


def clip_ref_count2half(t, L):
    p = count_pose(t, L, SLOW3[:2])
    h3 = slap_partial(t, SLOW3[2], stop=HALF_STOP)
    if t > SLOW3[2] - 0.62:
        hr = p.hands["r"]
        def g(S, hr=hr):
            mat = Vector((S["r"].x - 0.06, S["r"].y - 0.12, 0.05))
            top = S["r"] + Vector((-0.08, -0.12, 0.42))
            return lerp(mat, top, h3)                                # третій замах — завмирає над матом
        p.hands = dict(p.hands)
        p.hands["r"] = (g, DOWN * (1 - h3) + (UP + FWD * 0.4) * h3, FWD)
    return _ref_edges(_ref_sign(p, t, SLOW3[2] - HALF_STOP * 0.5 + 0.12, 2), t, L)   # мить завмер — тоді «два»


# ================================================================ больовий на ногу (P1-2, leg_lock)
# Пара: A у нулі обличчям у −Y; B лежить на спині ногами до A (зсув (0; −1,05), 180°). A на колінах (як вузол kneel_up),
# права нога B піднята під ~40°, щиколотка — біля грудей A під лівою пахвою: ліве передпліччя A під ахілом, права кисть —
# на стопі B (викручує). Тягне — A відхиляється назад і тягне щиколотку на себе. Жертва викручується, б'є долонею по
# мату (здача), тягнеться рукою за голову по мату — до каната (сам канат — окремий план, Rope_Grab_Back).
LL_OFF = Vector((0.0, -1.15, 0.0))
LL_YAW = 180
LL_LOOP = 3.2


def _to_b(v):
    """Точка простору A (пари) → простір ригу B (B повернутий на 180° і зсунутий на LL_OFF)."""
    return Vector((-(v.x - LL_OFF.x), -(v.y - LL_OFF.y), v.z))


def _from_b(v):
    return Vector((-v.x + LL_OFF.x, -v.y + LL_OFF.y, v.z))


def ll_ankle(crank=0.0, bent=0.0, lift=1.0):
    """Щиколотка правої ноги B у просторі A. lift 0 — на маті (як у вузлі lying_back), 1 — у захваті; crank — A тягне
    на себе (кут ноги росте); bent — коліно B зігнуте (неповний захват, 7–9)."""
    hip = Vector((0.09, LL_OFF.y + 0.05, 0.12))
    lg = 0.84 * BODY["leg"] * (1.0 - 0.18 * bent)
    ang = math.radians(34.0 + 8.0 * crank + 10.0 * bent)
    a1 = hip + Vector((0.0, lg * math.cos(ang), lg * math.sin(ang)))
    if lift >= 1.0:
        return a1
    off_b = off_for(Vector((0.0, 0.0, LIE_Z)))
    a0 = _from_b(xf(off_b, Q_BACK, REST["ankle_r"] + Vector((0.0, -0.25 * 0.15, 0.35 * 0.15))))
    k = smooth(lift)
    return a0.lerp(a1, k) + Vector((0.0, -0.06, 0.08)) * math.sin(math.pi * k)


def ll_a(crank=0.0, grip=1.0, t=0.0, L=2.4):
    """A на колінах обіймає гомілку B: нахилений уперед над щиколоткою, на тязі — відхиляється назад."""
    p = kneel_up(t, L)
    p.pitch = 38.0 - 26.0 * crank + breathe(t, L, 1.0)
    p.pelvis = p.pelvis + Vector((0.0, 0.02 * crank, 0.0))
    p.yaw = -6.0
    p.head_pitch = 5.0 - 12.0 * crank
    p.head_yaw = -10.0
    kh = dict(p.hands)
    def under(S):                                       # ліва — передпліччям під ахілом, кисть з того боку гомілки
        c = P(S, "calf_r", 0.84, Vector((-0.06, 0.0, -0.065)))
        return c or S["l"] + Vector((-0.1, -0.3, -0.2))
    def over(S):                                        # права — на підошві B, викручує стопу
        f = P(S, "foot_r", 0.45, Vector((0.0, 0.085, 0.0)))
        return f or S["r"] + Vector((0.15, -0.3, -0.1))
    g_ = smooth(grip)
    p.hands = {"l": (lambda S: kh["l"][0](S).lerp(under(S), g_), RIGHT + DOWN * 0.2, UP),
               "r": (lambda S: kh["r"][0](S).lerp(over(S), g_), FWD + LEFT * 0.4, UP + BACK * 0.3)}
    p.elbows = {"l": lambda S: S["l"] + Vector((0.35, 0.1, -0.3)), "r": lambda S: S["r"] + Vector((-0.35, 0.15, -0.2))}
    p.fist = {"l": 0.3 + 0.5 * g_, "r": 0.3 + 0.4 * g_}
    return p


def sweep_l(S, q, k, end=(0.2, 0.04, 0.6), lift=0.12):
    """Ліва рука лежачого на спині — від розкинутої на маті (arms_on_mat) за голову дугою по колу над матом (навпростець
    передпліччя провалювалось під мат через плече). k — 0..1; end — кінцева точка в просторі тіла q (частка руки)."""
    a0, r0 = math.atan2(-0.32, 0.42), math.hypot(0.42, -0.32)
    a1, r1 = math.atan2(end[2], end[0]), math.hypot(end[0], end[2])
    a, r = a0 + (a1 - a0) * k, r0 + (r1 - r0) * k
    yq = 0.05 + (end[1] - 0.05) * k - lift * math.sin(math.pi * k)          # −y тіла — угору від мату
    return S["l"] + q @ (Vector((r * math.cos(a), yq, r * math.sin(a))) * BODY["arm"])


def ll_b(crank=0.0, writhe=0.0, w=0.0, bent=0.0, lift=1.0, sit=0.0, tap=0.0, reach=0.0, held=1.0):
    """B на спині, права нога в захваті. writhe/w — викручується (фаза −1..1); sit — плечі вгору (крик);
    tap — права долоня над матом (1 — угорі, 0 — на маті); reach — ліва рука тягнеться за голову по мату;
    held — 0: вільна нога й руки як у вузлі lying_back, 1 — як у захваті."""
    p = lying(sit=0.0, knee_bend=0.15)
    q = Q_BACK
    off_b = off_for(Vector((0.0, 0.0, LIE_Z)))
    hd = smooth(held)
    kl = 0.15 + hd * (0.05 + 0.35 * writhe * (0.5 + 0.5 * w))           # вільна нога — згинається, впирається
    al = xf(off_b, q, REST["ankle_l"] + Vector((0.12 * hd, -0.25 * kl, 0.35 * kl)))
    p.ankles = (al, _to_b(ll_ankle(crank, bent, lift)))
    p.knees = (xf(off_b, q, REST["knee_l"] + Vector((0.03 + 0.17 * hd, -0.6, 0.0))),
               xf(off_b, q, REST["knee_r"] + Vector((-0.03 - 0.02 * lift, -0.6, 0.1 * lift))))
    p.pitch = 26.0 * sit + 6.0 * writhe * max(0.0, w)
    p.roll = 12.0 * writhe * w - 10.0 * reach
    p.head_pitch = -30.0 - 25.0 * sit
    p.head_yaw = 30.0 * writhe * w + 35.0 * reach
    arms_on_mat(p, q, lift_l=0.0, lift_r=0.0)
    hl, hr = p.hands["l"], p.hands["r"]
    ak = BODY["arm"]
    # права: ближче до стегна, долонею на маті; tap — б'є по мату (1 — угорі)
    slap = lambda S: S["r"] + q @ (Vector((-0.24, 0.02 - 0.3 * tap, -0.42)) * ak)
    # ліва: за голову по мату дугою (sweep_l), рука витягнута
    rk = hd * 0.6 + 0.4 * min(1.0, 3.0 * tap)
    rs = smooth(reach)
    p.hands = {"r": (lambda S: hr[0](S).lerp(slap(S), rk), q @ DOWN, q @ FWD),
               "l": ((lambda S: sweep_l(S, q, rs)) if rs > 0.0 else hl[0], q @ DOWN, (q @ FWD).lerp(q @ UP, rs))}
    if rs > 0.0:
        el0 = p.elbows["l"]
        p.elbows = dict(p.elbows)
        p.elbows["l"] = lambda S: el0(S).lerp(S["l"] + Vector((0.4, 0.1, 0.35)), rs)
    p.fist = {"l": 0.15 - 0.1 * reach, "r": 0.15 + hd * 0.35 * writhe * (0.5 - 0.5 * w)}
    return p


LL_ENTER_LEN = 1.8
LL_LOCK = 1.35          # с — захват зафіксовано (мітка contact)

def clip_ll_enter_a(t, L, partner=None):
    """Зі стійки на коліна біля ніг B (права, ліва) → нахил, обидві руки на щиколотку B на маті → підводиться з ногою
    B до грудей → захват під пахвою (мітка contact)."""
    st = stance(t, L, sway=0.0)
    kn = ll_a(crank=0.3, grip=0.0, t=t, L=L)
    fin = ll_a(crank=0.3, grip=1.0, t=t, L=L)
    kneel = smooth((t - 0.26) / 0.5)
    p = blend_pose(_filled(st), _filled(kn), kneel)
    bend = _win(t, 0.42, 0.74, 0.86, 1.3)                                # нахил до щиколотки на маті
    p.pitch = p.pitch + 50.0 * bend
    p.head_pitch = p.head_pitch + 15.0 * bend
    fr0, fl0 = foot(-0.2, 0.075), foot(0.2, -0.105)
    kl_, kr_ = kn.ankles
    # стопи по черзі: права назад — на одне коліно (ліва стоїть), потім ліва (обидві в повітрі — підскок, огляд 08.11.2026)
    p.ankles = (feet_track(t, [(0.0, fl0), (0.5, fl0), (0.78, kl_), (L + 1, kl_)])[0],
                feet_track(t, [(0.0, fr0), (0.04, fr0), (0.32, kr_), (L + 1, kr_)])[0])
    tip = Quaternion(RIGHT, math.radians(-75.0))
    p.foot_rot = (Quaternion(UP, math.radians(10.0)).slerp(tip, smooth((t - 0.52) / 0.3)),
                  Quaternion(UP, math.radians(-14.0)).slerp(tip, smooth((t - 0.08) / 0.3)))
    # ліве коліно, поки A на одному коліні, — назовні, не над стопою B (гомілка A входила в неї на 5 см)
    p.knees = (lerp(p.ankles[0] + Vector((0.6, -0.55, 0.45)), kn.knees[0], smooth((t - 0.5) / 0.3)),
               lerp(p.ankles[1] + Vector((-0.22, -0.9, 0.45)), kn.knees[1], smooth((t - 0.04) / 0.3)))
    grab = smooth((t - 0.5) / 0.28)
    hold = smooth((t - 1.0) / (LL_LOCK - 1.0))
    def hand(s_):
        base = p.hands[s_]
        def g(S):
            ank = P(S, "foot_r", 0.0)
            if ank is None:
                return base[0](S)
            on = ank + Vector((0.06 if s_ == "l" else -0.06, 0.05, 0.03))   # з боків щиколотки
            return base[0](S).lerp(on, grab).lerp(fin.hands[s_][0](S), hold)
        return (g, fin.hands[s_][1], fin.hands[s_][2])
    p.hands = {s_: hand(s_) for s_ in "lr"}
    p.elbows = {s_: (lambda S, s_=s_: kn.elbows[s_](S).lerp(fin.elbows[s_](S), grab)) for s_ in "lr"}
    p.fist = {"l": 0.3 + 0.5 * grab, "r": 0.3 + 0.4 * grab}
    return p

def clip_ll_enter_b(t, L, partner=None):
    lift = smooth((t - 0.8) / (LL_LOCK - 0.85))
    lock = hit_spring(t, LL_LOCK, w=14.0, z=0.4)
    wr = smooth((t - LL_LOCK) / 0.35)
    p = ll_b(crank=0.3 * lift + 0.12 * abs(lock), writhe=0.6 * wr, w=math.sin(2 * math.pi * (t - L) / 0.8) * wr, lift=lift,
             held=smooth((t - 0.95) / 0.6))
    p.head_pitch = p.head_pitch - 15.0 * abs(lock)
    return p


def clip_ll_hold_a(t, L, partner=None):
    cr = 0.5 - 0.5 * math.cos(_ph(t, L, 1.6))
    return ll_a(crank=0.3 + 0.4 * cr, t=t, L=L)

def clip_ll_hold_b(t, L, partner=None):
    cr = 0.5 - 0.5 * math.cos(_ph(t, L, 1.6))
    env = smoothstep_edges(t, L)
    return ll_b(crank=0.3 + 0.4 * cr, writhe=0.6 + 0.3 * env * math.sin(_ph(t, L, 3.2)), w=math.sin(_ph(t, L, 0.8)),
                sit=0.25 * cr)


# --- решта родини: дотиск (QTE) і його ефектний хвіст, здача, вихід, «до каната», провал входу, неповний захват.
# Усі, крім провалу, стикуються з вузлами leglock_A/leglock_B (crank 0,3, writhe 0,6, w 0).
LL_N = dict(crank=0.3, writhe=0.6)

def ll_node_b(t, L, **kw):
    a = dict(LL_N)
    a.update(kw)
    return ll_b(**a)


class Crank:
    """Дотиск у больовому: QTE до QTE_CLOSE, пік (contact) у t_pk, тримає до t_hold, назад у вузол до L. Ефектний
    хвіст — сильніше, довше на піку, нападник відхиляється далі й задирає голову."""
    def __init__(self, name, L, t_pk, t_hold, peak, sell):
        self.name, self.L, self.t_pk, self.t_hold, self.peak, self.sell = name, L, t_pk, t_hold, peak, sell


QTE_OPEN, QTE_CLOSE = 0.15, 0.75
LL_CRANKS = [Crank("LegLock_Crank", 1.5, 0.95, 1.05, 1.25, 0.6), Crank("LegLock_Crank_Hype", 1.95, 1.0, 1.45, 1.6, 0.9)]

def crank_curve(t, C):
    """Сила тягу: 0,3 → підготовка 0,12 (подався вперед) до QTE_CLOSE → ривок до піку → тримає → 0,3. До QTE_CLOSE
    обидва хвости однакові (режисер перемикає без змішування)."""
    return track(t, [(0.0, 0.3), (0.3, 0.3), (QTE_CLOSE, 0.12), (C.t_pk, C.peak), (C.t_hold, C.peak * 0.95), (C.L, 0.3),
                     (C.L + 1.0, 0.3)])

def crank_a(C):
    def f(t, L, partner=None):
        cr = crank_curve(t, C)
        p = ll_a(crank=cr, t=t, L=L)
        p.pitch = p.pitch - breathe(t, L, 1.0)          # дихання залежить від L — без нього пози обох хвостів на qte_close однакові
        roar = _win(t, C.t_pk, C.t_pk + 0.12, C.t_hold, C.L - 0.1) * max(0.0, C.peak - 1.3) * 3.0   # ефектний: голова вгору
        p.head_pitch = p.head_pitch - 25.0 * roar
        return p
    return f

def crank_b(C):
    def f(t, L, partner=None):
        cr = crank_curve(t, C)
        sl = _win(t, C.t_pk - 0.05, C.t_pk + 0.1, C.t_hold, C.L - 0.05) * C.sell
        hit = hit_spring(t, C.t_pk, w=13.0, z=0.4)
        env = smooth(t / 0.3) * smooth((L - t) / 0.3)
        return ll_node_b(t, L, crank=cr, writhe=0.6 + 0.3 * sl * env, w=math.sin(2 * math.pi * t / 0.8) * env,
                         sit=0.55 * sl + 0.1 * abs(hit))
    return f


LL_TAP_LEN = 2.9
LL_TAPS = (0.48, 0.8, 1.12)

def tap_height(t, taps, up=0.16):
    """Долоня над матом для серії ударів по мату: підйом за up с до кожного удару, удар — у мить зі списку."""
    h = 0.0
    for tt in taps:
        if tt - up <= t <= tt:
            h = max(h, math.sin(math.pi * (t - (tt - up)) / up))
    return max(0.0, min(1.0, h))

def clip_ll_tap_a(t, L, partner=None):
    """Тримає й тягне, поки B стукає; після третього удару відпускає ногу й стає рівно на колінах (kneel_up)."""
    cr = track(t, [(0.0, 0.3), (0.4, 0.75), (1.3, 0.75), (1.7, 0.3), (L, 0.3)])
    rel = smooth((t - 1.45) / 0.9)
    p = ll_a(crank=cr, grip=1.0 - smooth((t - 1.45) / 0.5), t=t, L=L)
    return blend_pose(_filled(p), _filled(kneel_up(t, L)), rel)

def clip_ll_tap_b(t, L, partner=None):
    cr = track(t, [(0.0, 0.3), (0.4, 0.75), (1.3, 0.75), (1.7, 0.3), (L, 0.3)])
    lift = 1.0 - smooth((t - 1.55) / 0.85)
    held = 1.0 - smooth((t - 1.9) / 0.8)
    pain = _win(t, 0.2, 0.45, 1.3, 2.0)
    return ll_b(crank=cr, writhe=(0.6 + 0.2 * pain) * held, w=math.sin(2 * math.pi * t / 0.7) * smooth(t / 0.3) * held,
                lift=lift, sit=0.35 * pain, tap=tap_height(t, LL_TAPS), held=held)


LL_ESC_LEN = 2.2

def clip_ll_escape_a(t, L, partner=None):
    """Нога вислизає: A ще тягне — хват розкривається — відкидається назад на колінах (сам продає) — рівно (kneel_up)."""
    slip = smooth((t - 0.38) / 0.22)
    back = hit_spring(t, 0.5, w=9.0, z=0.45)
    p = ll_a(crank=0.3 + 0.25 * smooth(t / 0.35), grip=1.0 - slip, t=t, L=L)
    p = blend_pose(_filled(p), _filled(kneel_up(t, L)), smooth((t - 0.6) / 1.2))
    p.pitch = p.pitch - 22.0 * back
    p.head_pitch = p.head_pitch - 10.0 * back
    return p

def clip_ll_escape_b(t, L, partner=None):
    """Згинає коліно й висмикує ногу до себе, хапається за коліно, потім лягає (вузол lying_back)."""
    pull = smooth((t - 0.25) / 0.4)
    down = smooth((t - 1.2) / 0.8)
    hip = Vector((0.09, LL_OFF.y + 0.05, 0.12))
    held_ank = ll_ankle(0.3 + 0.25 * smooth(t / 0.35), 0.0, 1.0)
    tuck = hip + Vector((0.0, 0.32 * BODY["leg"], 0.42 * BODY["leg"]))      # коліно до грудей, стопа в повітрі
    mat = ll_ankle(0.0, 0.0, 0.0)
    ank = held_ank.lerp(tuck, pull).lerp(mat, down)
    clutch = _win(t, 0.55, 0.85, 1.25, 1.75)
    p = ll_b(crank=0.3, writhe=0.6 * (1.0 - pull), lift=1.0, held=1.0 - down, sit=0.45 * clutch)
    p.ankles = (p.ankles[0], _to_b(ank))
    q = Q_BACK
    if clutch > 0.0:                                    # обидві долоні — на гомілку під правим коліном
        hl, hr = p.hands["l"], p.hands["r"]
        hip_b = xf(off_for(Vector((0.0, 0.0, LIE_Z))), q, REST["hip_r"])
        ank_b = _to_b(ank)
        d = ank_b - hip_b
        dl = max(1e-4, d.length)
        tl, cl = BODY["thigh"], BODY["calf"]
        a_ = (tl * tl - cl * cl + dl * dl) / (2.0 * dl)
        h_ = math.sqrt(max(0.0, tl * tl - a_ * a_))
        dn = d / dl
        pole = UP - dn * UP.dot(dn)
        kn = hip_b + dn * a_ + pole.normalized() * h_          # коліно (аналітично, згин угору)
        shin = kn.lerp(ank_b, 0.25) + Vector((0.0, 0.04, 0.0))  # гомілка трохи нижче коліна, з боку тулуба
        ax = (kn - ank_b).normalized()                          # великий палець — уздовж гомілки до коліна
        fl, ul, fr_, ur = hl[1], hl[2], hr[1], hr[2]
        p.hands = {"l": (lambda S: hl[0](S).lerp(shin + Vector((0.07, 0.0, 0.0)), clutch), fl.lerp(RIGHT + q @ DOWN * 0.3, clutch),
                         ul.lerp(ax, clutch)),
                   "r": (lambda S: hr[0](S).lerp(shin + Vector((-0.08, 0.0, 0.0)), clutch), fr_.lerp(LEFT + q @ DOWN * 0.3, clutch),
                         ur.lerp(ax, clutch))}
        p.fist = {"l": 0.15 + 0.4 * clutch, "r": 0.15 + 0.4 * clutch}
    return p


LL_REACH_LEN = 2.9

def clip_ll_reach_a(t, L, partner=None):
    return ll_a(crank=track(t, [(0.0, 0.3), (0.5, 0.3), (1.1, 0.8), (2.0, 0.8), (2.6, 0.3), (L, 0.3)]), t=t, L=L)

def clip_ll_reach_b(t, L, partner=None):
    """Тягнеться лівою рукою за голову по мату — до каната (сам канат — Rope_Grab_Back склейкою), потім рука падає."""
    rc = track(t, [(0.0, 0.0), (0.3, 0.0), (1.1, 1.0), (2.05, 1.0), (2.7, 0.0), (L, 0.0)])
    cr = track(t, [(0.0, 0.3), (0.5, 0.3), (1.1, 0.8), (2.0, 0.8), (2.6, 0.3), (L, 0.3)])
    claw = _win(t, 1.1, 1.3, 1.85, 2.05) * math.sin(2 * math.pi * (t - 1.1) / 0.35)
    env = smooth(t / 0.3) * smooth((L - t) / 0.3)
    p = ll_b(crank=cr, writhe=0.6, w=-0.4 * env * smooth(rc), reach=rc)
    p.fist = {"l": 0.3 + 0.25 * claw, "r": p.fist["r"]}
    return p


LL_FAIL_LEN = 1.8

def clip_ll_fail_a(t, L, partner=None):
    """≤ 6: стає на коліна й тягнеться до щиколотки — B відштовхує стопою в плече — A відкидається й лишається на колінах."""
    p = clip_ll_enter_a(min(t, 0.8), LL_ENTER_LEN)
    hit = hit_spring(t, 0.92, w=10.0, z=0.4) * (1.0 - smooth((t - 1.2) / 0.35))
    rec = smooth((t - 0.95) / 0.6)
    p = blend_pose(_filled(p), _filled(kneel_up(t, L)), rec)
    p.pitch = p.pitch - 24.0 * hit
    p.head_pitch = p.head_pitch - 12.0 * hit
    p.roll = p.roll + 6.0 * hit
    return p

def clip_ll_fail_b(t, L, partner=None):
    """Підтягує праву ногу й штовхає стопою A в ліве плече (мітка contact), нога повертається на мат."""
    hip = Vector((0.09, LL_OFF.y + 0.05, 0.12))
    mat = ll_ankle(0.0, 0.0, 0.0)
    chamber = hip + Vector((0.0, 0.3 * BODY["leg"], 0.4 * BODY["leg"]))
    ch = smooth((t - 0.5) / 0.3)
    ext = _win(t, 0.78, 0.92, 1.0, 1.3)
    back = smooth((t - 1.2) / 0.5)
    p = ll_b(crank=0.0, writhe=0.0, lift=0.0, held=0.0, sit=0.3 * _win(t, 0.5, 0.8, 1.0, 1.5))
    target = Vector((0.2, -0.44, 0.6))                                  # ліве плече A, нахиленого до щиколотки (повз голову)
    a_ = mat.lerp(chamber, ch).lerp(target, ext).lerp(mat, back)
    p.ankles = (p.ankles[0], _to_b(a_))
    return p


def clip_ll_partial_a(t, L, partner=None):
    cr = 0.5 - 0.5 * math.cos(_ph(t, L, 1.6))
    return ll_a(crank=0.3 + 0.2 * cr, t=t, L=L)

def clip_ll_partial_b(t, L, partner=None):
    """7–9: захват неповний — B згинає коліно й тримає його зігнутим, A тягне, але нога не випрямляється."""
    cr = 0.5 - 0.5 * math.cos(_ph(t, L, 1.6))
    env = smoothstep_edges(t, L, 0.5)
    return ll_node_b(t, L, crank=0.3 + 0.2 * cr, bent=0.9 * env, writhe=0.6 + 0.3 * env, w=math.sin(_ph(t, L, 0.8)) * env)


# --- «дотягнувся до каната»: окремий крупний план (рішення сесії «Ринг» 08.11.2026 — пара лишається в центрі, рука на
# канаті — склейкою). Жертва одна, на спині (Rope_Grab_Back) або на животі (Rope_Grab_Front); нижній канат — лінія
# вздовж X ригу на rope_grab_y() від таза (за головою), висота ROPE_H[0]. Мітка rope_grab — долоня на канаті.
ROPE_GRAB_LEN = 2.2
ROPE_GRAB_T = 1.05

def rope_grab_y():
    return 0.5 + 0.4 * BODY["arm"]

def rope_grab_front_y():
    """Лежачи на животі канат перед головою (від 0 ригу; голова до −Y): плече ≈ −0,92, рука вперед-угору ≈ 0,5."""
    return FRONT_END_Y - 0.47 - 0.5 * BODY["arm"]

def clip_rope_grab_back(t, L):
    p = lying(knee_bend=0.15)
    q = Q_BACK
    rc = smooth((t - 0.15) / 0.6)
    up = smooth((t - 0.7) / (ROPE_GRAB_T - 0.7))
    pull = smooth((t - ROPE_GRAB_T - 0.1) / 0.5)
    p.roll = -10.0 * rc
    p.pitch = 14.0 * pull
    p.head_pitch = -30.0 - 25.0 * rc
    p.head_yaw = 30.0 * rc
    hl = p.hands["l"]
    rope = lambda S: Vector((S["l"].x - 0.02, rope_grab_y(), ROPE_H[0] + 0.02 - 0.03 * pull))
    p.hands = dict(p.hands)
    p.hands["l"] = (lambda S: sweep_l(S, q, rc, end=(0.16, 0.04, 0.58)).lerp(rope(S), up), (q @ DOWN).lerp(UP, up),
                    (q @ FWD).lerp(BACK, up))
    p.elbows = dict(p.elbows)
    el0 = p.elbows["l"]
    p.elbows["l"] = lambda S: el0(S).lerp(S["l"] + Vector((0.4, 0.1, 0.35)), rc)
    p.fist = {"l": 0.15 + 0.75 * smooth((t - ROPE_GRAB_T + 0.05) / 0.15), "r": 0.15}
    return p


def clip_rope_grab_front(t, L):
    from_ = lying_front()
    p = lying_front(push=0.3 * smooth((t - ROPE_GRAB_T - 0.1) / 0.5))
    q = Q_FRONT
    rc = smooth((t - 0.15) / 0.6)
    up = smooth((t - 0.7) / (ROPE_GRAB_T - 0.7))
    hl = from_.hands["l"]
    fwd = lambda S: S["l"] + q @ (Vector((0.1, 0.0, 0.58)) * BODY["arm"])
    rope = lambda S: Vector((S["l"].x - 0.02, rope_grab_front_y(), ROPE_H[0] + 0.02))
    arc = Vector((0.0, 0.0, 0.1 * math.sin(math.pi * rc)))
    p.hands = dict(p.hands)
    p.hands["l"] = (lambda S: (hl[0](S).lerp(fwd(S), rc) + arc).lerp(rope(S), up), q @ UP, DOWN)
    p.fist = {"l": 0.15 + 0.75 * smooth((t - ROPE_GRAB_T + 0.05) / 0.15), "r": 0.15}
    p.head_yaw = 75.0 * (1.0 - 0.6 * rc)
    return p


# ================================================================ больовий на лікоть (P1-2, armbar)
# Референс (навчальні матеріали й записи матчів, своїми словами): після викруту руки нападник тягне руку суперника
# донизу — той падає грудьми на мат; нападник стає на коліна збоку від його плеча й тримає випрямлену руку вгору:
# одна кисть на зап'ясті, друга — над ліктем; дотиск — відхиляється назад, рука жертви йде до нього, плече відривається
# від мату. Жертва стукає вільною долонею по мату або тягнеться вільною рукою вперед — до каната (Rope_Grab_Front
# склейкою). Карта кінцівок у фазі тримання:
#   A — обидва коліна на маті, тулуб рівно; ліва кисть — зап'ястя B з боку A; права — середина передпліччя B
#       (на плечі над ліктем кисть опинялась на рівні таза A — передпліччя A входило в живіт); лікті назовні.
#   B — на животі, тіло впоперек перед A (голова праворуч від A); права рука випрямлена догори над плечем;
#       ліва долоня — на маті біля голови; ноги — на маті, у болю коліна розходяться жабкою.
AB_OFF = Vector((0.92, -0.55, 0.0))     # B на животі: хребет — на 0,55 м перед A, праве плече — навпроти A
AB_YAW = -90
AB_LOOP = 3.2
AB_REACH = 0.66                          # м — кисть від плеча (розв'язувач тримає ≤ 0,97 руки — рука пряма)

def _ab_to_b(v):
    """Точка простору A → простір B (B = R(+90°)·(A − зсув))."""
    return Vector((-(v.y - AB_OFF.y), v.x - AB_OFF.x, v.z))

def ab_dir(crank=0.0):
    """Напрям утримуваної руки B (простір B): угору, з дотиском — до A (у бік −X ригу B)."""
    a = math.radians(6.0 + 12.0 * crank)
    return Vector((-math.sin(a), 0.0, math.cos(a)))

def ab_a(crank=0.0, grip=1.0, t=0.0, L=2.4):
    """A на колінах (вузол kneel_up) тримає руку B: дотиск — відхиляється назад."""
    p = kneel_up(t, L)
    p.pitch = 8.0 - 16.0 * crank + breathe(t, L, 1.0)
    p.pelvis = p.pelvis + Vector((0.0, 0.03 * crank, 0.0))
    p.head_pitch = 10.0 - 10.0 * crank
    p.head_yaw = 0.0
    kh = p.hands
    g = smooth(grip)
    def on(s_, bone, along):
        def f(S):
            pt = P(S, bone, along, Vector((0.0, 0.045, 0.0)))
            return kh[s_][0](S) if pt is None else kh[s_][0](S).lerp(pt, g)
        return f
    p.hands = {"l": (on("l", "lowerarm_r", 0.88), kh["l"][1].lerp(FWD, g), kh["l"][2].lerp(UP, g)),
               "r": (on("r", "lowerarm_r", 0.5), kh["r"][1].lerp(FWD, g), kh["r"][2].lerp(UP, g))}
    ke = p.elbows
    p.elbows = {"l": lambda S: ke["l"](S).lerp(S["l"] + Vector((0.4, 0.04, -0.18)), g),
                "r": lambda S: ke["r"](S).lerp(S["r"] + Vector((-0.44, 0.04, -0.16)), g)}
    p.fist = {"l": 0.3 + 0.5 * g, "r": 0.3 + 0.4 * g}
    return p

def ab_b(crank=0.0, writhe=0.0, w=0.0, bent=0.0, held=1.0, tap=0.0, reach=0.0, sit=0.0):
    """B на животі, права рука в захваті. held — 0: вузол lying_front, 1 — рука вгорі; bent — лікоть зігнутий (7–9);
    tap — ліва долоня над матом (б'є); reach — ліва рука вперед по мату (до каната); sit — голова й плечі від мату."""
    hd = smooth(held)
    roll = -(6.0 + 10.0 * crank) * hd
    fl = 0.35 * writhe * (0.5 + 0.5 * w)
    fr = 0.35 * writhe * (0.5 - 0.5 * w)
    p = lying_front(push=0.25 * sit, roll=roll, frog=(fl, fr))
    p.ankles = tuple(a_ + Vector((0.0, 0.0, 0.012 * hd)) for a_ in p.ankles)
    q = Q_FRONT @ Quaternion(UP, math.radians(roll))
    d = ab_dir(crank)
    ln = AB_REACH * (1.0 - 0.3 * bent)
    hr, hl = p.hands["r"], p.hands["l"]
    up = lambda S: S["r"] + d * ln
    p.hands = dict(p.hands)
    p.hands["r"] = (lambda S: hr[0](S).lerp(up(S), hd), hr[1].lerp(d, hd), hr[2].lerp(FWD, hd))
    er = p.elbows["r"]
    p.elbows = dict(p.elbows)
    p.elbows["r"] = lambda S: er(S).lerp(S["r"] + Vector((-0.25, 0.3, 0.25)), hd)
    rs = smooth(reach)
    ak = BODY["arm"]
    fwd = lambda S: S["l"] + q @ (Vector((0.1, 0.0, 0.58)) * ak)
    lift = Vector((0.0, 0.0, 0.16 * tap + 0.1 * math.sin(math.pi * rs)))
    p.hands["l"] = (lambda S: hl[0](S).lerp(fwd(S), rs) + lift, hl[1], hl[2])
    p.fist = {"l": 0.15, "r": 0.15 + 0.15 * hd}
    p.head_pitch = p.head_pitch - 12.0 * sit
    return p

AB_N = dict(crank=0.3, writhe=0.5)

def ab_node_b(**kw):
    a = dict(AB_N)
    a.update(kw)
    return ab_b(**a)


# --- переведення на мат: старт — поза викруту руки (Chain_ArmWringer, склейкою; вузла в ланки чейну немає):
# A стоїть, обидві кисті на зап'ясті B; B зігнутий уперед, права рука випрямлена до A. Ривок донизу — B падає грудьми
# на мат (Bump_Front), A йде на коліна й підхоплює руку вгору; захват фіксується (мітка contact).
AB_ENTER_LEN = 2.1
AB_T0 = 0.35            # с — ривок, B починає падати
AB_LAND = AB_T0 + 0.58  # с — груди B на маті (мітка sell)
AB_LOCK = 1.55          # с — A на колінах, рука B пряма (мітка contact)
AB_W = [(0.0, (0.25, -0.4, 1.0)), (AB_T0, (0.25, -0.4, 1.0)), (0.65, (0.12, -0.35, 1.08)),
        (AB_LAND, (0.03, -0.3, 0.88)), (1.4, (0.0, -0.33, 0.83))]

def ab_wrist(t):
    """Кисть B у переведенні (простір A): A тримає її й веде донизу-до себе."""
    return Vector(tuple(track(t, [(tt, w_[i]) for tt, w_ in AB_W]) for i in range(3)))

def ab_start_b(t, L):
    """Поза B до ривка: як у викруті — зігнутий уперед, права рука до A."""
    p = clip_bump_front(0.0, L)
    p.pitch = p.pitch + 18.0
    p.head_pitch = 20.0
    p.head_yaw = -25.0
    return p

def _ab_right_on_path(p, t, k_end=0.0):
    """Права кисть B — на шляху ab_wrist (у руках A), лікоть назовні-вгору."""
    hr = p.hands["r"]
    wb = _ab_to_b(ab_wrist(t))
    p.hands = dict(p.hands)
    p.hands["r"] = (lambda S: wb, lambda S: (wb - S["r"]).normalized() if (wb - S["r"]).length > 1e-3 else UP, UP)
    p.elbows = dict(p.elbows)
    p.elbows["r"] = lambda S: S["r"] + Vector((-0.3, 0.05, 0.25))
    p.fist = {"l": p.fist["l"], "r": 0.3}
    return p

def clip_ab_enter_b(t, L, partner=None):
    tb = max(0.0, t - AB_T0)
    if t <= AB_T0:
        p = ab_start_b(t, L)
    else:
        p = clip_bump_front(tb, L)
        lean = 1.0 - smooth(tb / 0.35)
        p.pitch = p.pitch + 18.0 * lean
        p.head_pitch = p.head_pitch + 35.0 * lean
    p = _ab_right_on_path(_filled(p), t)
    return blend_pose(_filled(p), _filled(ab_node_b()), smooth((t - 1.2) / 0.6))

def clip_ab_enter_a(t, L, partner=None):
    """Стоїть, повернутий до руки B (обидві кисті на зап'ясті) → тягне донизу → на коліна збоку від плеча B →
    права кисть переходить на плече над ліктем (захват)."""
    st = stance(t, L, sway=0.0)
    turn = 1.0 - smooth((t - 0.5) / 0.7)
    st.yaw = 25.0 * turn
    st.pitch = st.pitch + 8.0 * turn + 6.0 * _win(t, 0.3, 0.6, 0.8, 1.1)
    kn = ab_a(crank=0.3, grip=1.0, t=t, L=L)
    kneel = smooth((t - 0.85) / 0.6)
    p = blend_pose(_filled(st), _filled(kn), kneel)
    fl0, fr0 = foot(0.2, -0.105), foot(-0.2, 0.075)
    kl_, kr_ = kn.ankles
    p.ankles = (feet_track(t, [(0.0, fl0), (1.0, fl0), (1.28, kl_), (L + 1, kl_)])[0],          # стопи по черзі:
                feet_track(t, [(0.0, fr0), (0.62, fr0), (0.9, kr_), (L + 1, kr_)])[0])          # права, потім ліва
    tip = Quaternion(RIGHT, math.radians(-75.0))
    p.foot_rot = (Quaternion(UP, math.radians(10.0)).slerp(tip, smooth((t - 1.02) / 0.3)),
                  Quaternion(UP, math.radians(-14.0)).slerp(tip, smooth((t - 0.66) / 0.3)))
    p.knees = (lerp(p.ankles[0] + Vector((0.22, -0.9, 0.45)), kn.knees[0], smooth((t - 1.0) / 0.3)),
               lerp(p.ankles[1] + Vector((-0.22, -0.9, 0.45)), kn.knees[1], smooth((t - 0.62) / 0.3)))
    move_r = smooth((t - 1.1) / 0.4)                     # права — з кисті B на середину передпліччя
    def hand(s_):
        def g(S):
            pw = P(S, "lowerarm_r", 0.88, Vector((0.0, 0.045, 0.0)))
            if pw is None:
                return st.hands[s_][0](S)
            if s_ == "l":
                return pw
            ph = P(S, "hand_r", 0.35, Vector((0.0, 0.045, 0.0)))
            pe = P(S, "lowerarm_r", 0.5, Vector((0.0, 0.045, 0.0)))
            return ph.lerp(pe, move_r)
        return g
    p.hands = {s_: (hand(s_), FWD + LEFT * 0.3 * turn, UP) for s_ in "lr"}
    p.fist = {"l": 0.8, "r": 0.7}
    return p


def clip_ab_hold_a(t, L, partner=None):
    cr = 0.5 - 0.5 * math.cos(_ph(t, L, 1.6))
    return ab_a(crank=0.3 + 0.4 * cr, t=t, L=L)

def clip_ab_hold_b(t, L, partner=None):
    cr = 0.5 - 0.5 * math.cos(_ph(t, L, 1.6))
    env = smoothstep_edges(t, L)
    return ab_node_b(crank=0.3 + 0.4 * cr, writhe=0.5 + 0.3 * env * math.sin(_ph(t, L, 3.2)),
                     w=math.sin(_ph(t, L, 0.8)) * env, sit=0.3 * cr)


AB_CRANKS = [Crank("Armbar_Crank", 1.5, 0.95, 1.05, 1.25, 0.6), Crank("Armbar_Crank_Hype", 1.95, 1.0, 1.45, 1.6, 0.9)]

def ab_crank_a(C):
    def f(t, L, partner=None):
        p = ab_a(crank=crank_curve(t, C), t=t, L=L)
        p.pitch = p.pitch - breathe(t, L, 1.0)          # як у LegLock_Crank: однакова поза обох хвостів на qte_close
        roar = _win(t, C.t_pk, C.t_pk + 0.12, C.t_hold, C.L - 0.1) * max(0.0, C.peak - 1.3) * 3.0
        p.head_pitch = p.head_pitch - 25.0 * roar
        return p
    return f

def ab_crank_b(C):
    def f(t, L, partner=None):
        cr = crank_curve(t, C)
        sl = _win(t, C.t_pk - 0.05, C.t_pk + 0.1, C.t_hold, C.L - 0.05) * C.sell
        env = smooth(t / 0.3) * smooth((L - t) / 0.3)
        return ab_node_b(crank=cr, writhe=0.5 + 0.3 * sl * env, w=math.sin(2 * math.pi * t / 0.8) * env,
                         sit=0.7 * sl + 0.15 * abs(hit_spring(t, C.t_pk, w=13.0, z=0.4)))
    return f


AB_TAP_LEN = 2.9
AB_TAPS = (0.48, 0.8, 1.12)

def clip_ab_tap_a(t, L, partner=None):
    """Тримає й дотискає, поки B стукає; після третього удару відпускає й лишається на колінах (kneel_up)."""
    cr = track(t, [(0.0, 0.3), (0.4, 0.75), (1.3, 0.75), (1.7, 0.3), (L, 0.3)])
    p = ab_a(crank=cr, grip=1.0 - smooth((t - 1.45) / 0.5), t=t, L=L)
    return blend_pose(_filled(p), _filled(kneel_up(t, L)), smooth((t - 1.45) / 0.9))

def clip_ab_tap_b(t, L, partner=None):
    cr = track(t, [(0.0, 0.3), (0.4, 0.75), (1.3, 0.75), (1.7, 0.3), (L, 0.3)])
    held = 1.0 - smooth((t - 1.55) / 0.85)
    pain = _win(t, 0.2, 0.45, 1.3, 2.0)
    return ab_b(crank=cr, writhe=(0.5 + 0.2 * pain) * held, w=math.sin(2 * math.pi * t / 0.7) * smooth(t / 0.3) * held,
                held=held, tap=tap_height(t, AB_TAPS), sit=0.4 * pain)


AB_ESC_LEN = 2.4

def clip_ab_escape_a(t, L, partner=None):
    """Рука вислизає: хват розкривається — A відкидається назад на колінах — рівно (kneel_up)."""
    slip = smooth((t - 0.3) / 0.2)
    back = hit_spring(t, 0.42, w=9.0, z=0.45) * (1.0 - smooth((t - 1.5) / 0.5))
    p = ab_a(crank=0.3 + 0.2 * smooth(t / 0.3), grip=1.0 - slip, t=t, L=L)
    p = blend_pose(_filled(p), _filled(kneel_up(t, L)), smooth((t - 0.5) / 1.2))
    p.pitch = p.pitch - 22.0 * back
    p.head_pitch = p.head_pitch - 10.0 * back
    return p

def clip_ab_escape_b(t, L, partner=None):
    """Висмикує руку донизу до голови, ліва кисть хапається за праве передпліччя (продаж — лікоть), потім лежить."""
    yank = smooth((t - 0.22) / 0.4)
    clutch = _win(t, 0.6, 0.9, 1.5, 2.0)
    p = ab_b(crank=0.3, writhe=0.5 * (1.0 - smooth((t - 0.8) / 0.8)), held=1.0 - yank, sit=0.5 * clutch)
    if clutch > 0.0:
        q = Q_FRONT
        hl = p.hands["l"]
        ak = BODY["arm"]
        fore = lambda S: S["r"] + q @ (Vector((0.02, 0.0, 0.46)) * ak) + Vector((0.0, 0.0, 0.12))  # праве зап'ястя перед головою
        p.hands = dict(p.hands)
        p.hands["l"] = (lambda S: hl[0](S).lerp(fore(S), clutch), hl[1], hl[2])
        el = p.elbows["l"]
        p.elbows = dict(p.elbows)
        p.elbows["l"] = lambda S: el(S).lerp(S["l"] + q @ Vector((0.1, 0.0, 0.4)) + Vector((0.0, 0.0, 0.3)), clutch)
        p.fist = {"l": 0.15 + 0.5 * clutch, "r": p.fist["r"]}
    return p


AB_REACH_LEN = 2.9

def clip_ab_reach_a(t, L, partner=None):
    return ab_a(crank=track(t, [(0.0, 0.3), (0.5, 0.3), (1.1, 0.8), (2.0, 0.8), (2.6, 0.3), (L, 0.3)]), t=t, L=L)

def clip_ab_reach_b(t, L, partner=None):
    """Тягнеться лівою вперед по мату — до каната (сам канат — Rope_Grab_Front склейкою), потім рука падає."""
    rc = track(t, [(0.0, 0.0), (0.3, 0.0), (1.1, 1.0), (2.05, 1.0), (2.7, 0.0), (L, 0.0)])
    cr = track(t, [(0.0, 0.3), (0.5, 0.3), (1.1, 0.8), (2.0, 0.8), (2.6, 0.3), (L, 0.3)])
    claw = _win(t, 1.1, 1.3, 1.85, 2.05) * math.sin(2 * math.pi * (t - 1.1) / 0.35)
    p = ab_node_b(crank=cr, reach=rc, sit=0.3 * smooth(rc))
    p.fist = {"l": 0.3 + 0.25 * claw, "r": p.fist["r"]}
    return p


AB_FAIL_LEN = 1.6

def clip_ab_fail_a(t, L, partner=None):
    """≤ 6: ривок не вдався — B висмикує руку; кисті A зісковзують, A хитається вперед і повертається в стійку."""
    st = stance(t, L, sway=0.0)
    turn = 1.0 - smooth((t - 0.5) / 0.8)
    st.yaw = 25.0 * turn
    stum = hit_spring(t, 0.5, w=10.0, z=0.45) * (1.0 - smooth((t - 1.1) / 0.4))
    st.pitch = st.pitch + 8.0 * turn + 10.0 * stum
    slip = smooth((t - 0.42) / 0.16)
    sh = st.hands
    def hand(s_):
        def g(S):
            pw = P(S, "lowerarm_r", 0.88, Vector((0.0, 0.045, 0.0))) if s_ == "l" else P(S, "hand_r", 0.35, Vector((0.0, 0.045, 0.0)))
            return sh[s_][0](S) if pw is None else pw.lerp(sh[s_][0](S), slip)
        return g
    st.hands = {s_: (hand(s_), sh[s_][1], sh[s_][2]) for s_ in "lr"}
    return st

def clip_ab_fail_b(t, L, partner=None):
    """Висмикує руку до себе й угору, випрямляється в стійку."""
    yank = smooth((t - 0.35) / 0.25)
    p = ab_start_b(t, L)
    p = _ab_right_on_path(_filled(p), 0.0)
    wb = _ab_to_b(ab_wrist(0.0))
    hr = p.hands["r"]
    pull = lambda S: S["r"] + Vector((-0.08, -0.22, -0.18))          # кисть до своїх грудей
    p.hands["r"] = (lambda S: wb.lerp(pull(S), yank), hr[1], hr[2])
    return blend_pose(_filled(p), _filled(stance(t, L, sway=0.0)), smooth((t - 0.55) / 0.8))


def clip_ab_partial_a(t, L, partner=None):
    cr = 0.5 - 0.5 * math.cos(_ph(t, L, 1.6))
    return ab_a(crank=0.3 + 0.2 * cr, t=t, L=L)

def clip_ab_partial_b(t, L, partner=None):
    """7–9: захват неповний — B згинає лікоть і тримає його зігнутим, A тягне, але рука не випрямляється."""
    cr = 0.5 - 0.5 * math.cos(_ph(t, L, 1.6))
    env = smoothstep_edges(t, L, 0.5)
    return ab_node_b(crank=0.3 + 0.2 * cr, bent=0.9 * env, writhe=0.5 + 0.3 * env, w=math.sin(_ph(t, L, 0.8)) * env)


# ================================================================ фінішер «Метеор» (P1-3, meteor)
# Склейка (ТЗ §1.2, §5.4): кут — Top_Rope_Perch_Loop (є); Air_Meteor — сальто назад над центром; Land_Meteor — грудьми
# на лежачу жертву в центрі. Пара Land — як у Cover (B = PIN_OFF, 90°): A падає грудьми впоперек грудей B і за 0,5 с
# від удару підтягує коліна в накриття (вузол pinned_*, далі Cover_Hold_Loop). Air — одиночний, у тому самому просторі
# A, що й Land: його кінцева поза — стартова поза Land (висота, швидкість, кут оберту), тож режисер ставить обидва
# однаково. Ступені: 10+ — Land_Meteor; 7–9 — Land_Meteor_Partial (влучив боком: обидва продають ребра); ≤6 —
# Land_Meteor_Miss (B відкотився на ≤ 0,5 м, A б'ється об мат і продає груди).
# Референс (записи матчів, своїми словами): з кута — стрибок уперед, сальто назад у групуванні (оберт швидкий), перед
# приземленням тіло розкривається (оберт повільнішає), руки вперед-врозліт; груди б'ють першими, ноги ляскають слідом.

MET_X = -0.15            # таз A над грудьми B (груди B — x ≈ −0,2 простору A)
MET_Y0 = -0.05           # таз A на стику Air → Land (над місцем приземлення)
MET_Z0 = 1.45            # висота таза A на стику: 1,0–1,5 м над жертвою (§1.2.3)
MET_V0 = 1.34            # м/с униз на стику
MET_W = 50.0             # °/с — оберт на стику (тіло вже розкрите)
MET_TH0 = 250.0          # кут сальто на стику (270 — лицем донизу горизонтально)
AIR_LEN = 1.0            # §1.2.2: у фінішері ~1,0 с
AIR_HYPE_LEN = 1.4       # ефектний: +0,4 с і гвинт навколо осі тіла
LAND_LEN = 1.4
PARTIAL_LEN = 2.4
MISS_LEN = 3.0          # ≤6: удар об мат, рачки, на коліна з руками на грудях
MISS_ROLL = 0.48         # м: відкочується жертва (≤ 0,5)
MISS_OFF = Vector((0.0, -0.05, 0.0))  # промах: B лежить уздовж польоту A (180°) під місцем приземлення й котиться
MISS_YAW = 180                           #   вбік, ліворуч від A, — за правилом середини пари (≤ 0,5 м) інакше не вийти


def met_hit_time(z_hit):
    return (-MET_V0 + math.sqrt(MET_V0 ** 2 + 2.0 * 9.81 * (MET_Z0 - z_hit))) / 9.81


MET_ZHIT, MET_VX = 0.44, 0.45              # 10+: груди на грудях B
MET_HIT = met_hit_time(MET_ZHIT)           # ≈ 0,35 с
MET_ZHIT_P, MET_VX_P = 0.38, 0.2           # 7–9: недолетів — ребра на ближньому боці B
MET_HIT_P = met_hit_time(MET_ZHIT_P)
MET_ZHIT_M, MET_VX_M = FRONT_Z + 0.02, 0.0  # ≤6: на мат
MET_HIT_M = met_hit_time(MET_ZHIT_M)

# пози рук у повітрі: кисть від плеча (частка руки) і полюс ліктя — у рамці тіла (x — ліво, y — назад, z — угору)
MET_WING = (Vector((0.55, 0.15, 0.6)), Vector((0.3, 0.25, -0.2)))      # крила (щойно відірвався від кута)
MET_TUCK = (Vector((-0.05, -0.55, -0.5)), Vector((0.4, 0.0, -0.1)))    # групування: кисті до гомілок
MET_OPEN = (Vector((0.62, -0.25, 0.5)), Vector((0.35, 0.25, -0.15)))   # розкритий: врозліт до голови (−y тіла — груди)
MET_SIDE = (Vector((0.05, -0.55, 0.5)), Vector((0.12, 0.35, -0.3)))   # промах: руки вперед-униз, вузько, лікті
                                                                        #   назад (ловить мат, не зачіпає B поруч)


def met_arms(p, q, ww, wt, wo, side=0.0):
    ak = BODY["arm"]
    p.hands, p.elbows = {}, {}
    for s_, sg in (("l", 1.0), ("r", -1.0)):
        hv, ev = Vector(), Vector()
        op_h = MET_OPEN[0].lerp(MET_SIDE[0], side)
        op_e = MET_OPEN[1].lerp(MET_SIDE[1], side)
        for w_, (h_, e_) in ((ww, MET_WING), (wt, MET_TUCK), (wo, (op_h, op_e))):
            hv += Vector((h_.x * sg, h_.y, h_.z)) * w_
            ev += Vector((e_.x * sg, e_.y, e_.z)) * w_
        f = (q @ hv).normalized() if hv.length > 1e-4 else q @ FWD
        u = q @ (Vector((0.0, -1.0, 0.0)) * ww + Vector((0.0, 0.0, 1.0)) * (wt + wo))
        u = u - f * u.dot(f)
        u = u.normalized() if u.length > 1e-4 else q @ UP
        p.hands[s_] = (lambda S, s_=s_, hv=hv: S[s_] + q @ (hv * ak), f, u)
        p.elbows[s_] = (lambda S, s_=s_, ev=ev: S[s_] + q @ ev)
    p.forced = set()
    return p


def met_air_pose(t, L, twist=0.0):
    """Сальто назад над центром: балістична дуга (вершина на 0,3 L), оберт найшвидший у групуванні посередині,
    розкривається до кінця — на стику швидкість, висота й кут оберту ті самі, що на старті Land."""
    u = t / L
    ta = 0.3 * L
    g_ = MET_V0 / (L - ta)
    za = MET_Z0 + 0.5 * g_ * (L - ta) ** 2
    z = za - 0.5 * g_ * (t - ta) ** 2
    y = MET_Y0 + 0.45 * (1.0 - u)
    A_ = 2.0 * (MET_TH0 - 10.0 - MET_W * L) / L
    th = 10.0 + MET_W * t + A_ * (t / 2.0 - L * math.sin(2.0 * math.pi * u) / (4.0 * math.pi))
    tuck = math.sin(math.pi * u) ** 2
    q = Quaternion(LEFT, math.radians(-th))
    if twist:
        q = q @ Quaternion(UP, math.radians(twist))
    p = body(off_for(Vector((MET_X, y, z))), q, knee_bend=0.1 + 0.65 * tuck)
    p.pitch = 22.0 * tuck
    p.head_pitch = -20.0 * (1.0 - smooth(u / 0.3)) + 25.0 * tuck - 30.0 * smooth((u - 0.6) / 0.35)
    ww = 1.0 - smooth(u / 0.3)
    wo = smooth((u - 0.55) / 0.35)
    wt = max(0.0, 1.0 - ww - wo)
    met_arms(p, q, ww, wt, wo)
    p.fist = {"l": 0.2 + 0.5 * wt, "r": 0.2 + 0.5 * wt}
    return p


def clip_air_meteor(t, L):
    return met_air_pose(t, L)

def clip_air_meteor_hype(t, L):
    return met_air_pose(t, L, twist=360.0 * smooth((t / L - 0.2) / 0.6))


def met_fall_pose(t, vx, side=0.0):
    """Падіння від стику Air → Land: балістика (g), оберт MET_W, тіло розкрите."""
    z = MET_Z0 - MET_V0 * t - 4.905 * t * t
    th = MET_TH0 + MET_W * t
    q = Quaternion(LEFT, math.radians(-th))
    p = body(off_for(Vector((MET_X, MET_Y0 - vx * t, z))), q, knee_bend=0.1)
    p.head_pitch = -30.0
    met_arms(p, q, 0.0, 0.0, 1.0, side=side)
    p.fist = {"l": 0.2, "r": 0.2}
    return p


def met_floor(y_pel, push=0.0, roll=0.0, frog=(0.0, 0.0)):
    """lying_front, зсунутий до MET_X; стопи — на маті (з push таз вище, ноги похило вниз)."""
    p = lying_front(y=y_pel, push=push, roll=roll, frog=frog)
    dx = Vector((MET_X, 0.0, 0.0))
    p.pelvis = p.pelvis + dx
    p.ankles = tuple(Vector((a_.x + MET_X, a_.y, min(a_.z, 0.12) if push > 0 else a_.z)) for a_ in p.ankles)
    p.knees = tuple(k_ + dx for k_ in p.knees)
    return p


def met_splash(y_pel, hip_dy=-0.1):
    """Грудьми на грудях B: таз за ближнім боком B, тулуб похило вгору на B; ліва кисть — на дальньому стегні B
    (обхопив), права — на маті за дальнім плечем B (як у накритті)."""
    p = met_floor(y_pel, push=1.2)
    p.pelvis = p.pelvis + Vector((0.0, 0.0, 0.07))
    p.pitch = p.pitch - 8.0
    def hip(S):
        pt = P(S, "pelvis", 0.0, Vector((-0.02, hip_dy, 0.22)))
        return pt if pt is not None else Vector((0.15, -0.7, 0.26))
    def far(S):
        sh = P(S, "upperarm_r", 0.0)
        return Vector((sh.x - 0.12, sh.y - 0.15, 0.06)) if sh is not None else Vector((-0.4, -0.93, 0.06))
    p.hands = {"l": (hip, FWD + DOWN * 0.15, RIGHT), "r": (far, FWD + DOWN * 0.4, LEFT)}
    p.elbows = {"l": lambda S: S["l"] + Vector((0.38, 0.1, 0.1)), "r": lambda S: S["r"] + Vector((-0.35, 0.05, 0.3))}
    p.head_yaw = 60.0
    return p


def _hand_off(p, side, v):
    g, f, u = p.hands[side]
    ev = lambda v_, S: v_(S) if callable(v_) else v_
    p.hands = dict(p.hands)
    p.hands[side] = (lambda S: ev(g, S) + v, f, u)
    return p


MET_LH0, MET_LH = 0.1, 0.3      # ліва кисть A: починає йти до B за 0,1 с до удару, приходить за 0,3 с


def _met_sweep(p, t, T):
    """Ліва кисть A перед ударом іде вгору й накриває B згори — не ковзає по піднятих колінах B."""
    u = (t - (T - MET_LH0)) / MET_LH
    h = 0.08 * math.sin(math.pi * u) ** 2 if 0.0 < u < 1.0 else 0.0
    return _hand_arc(p, "l", h) if h > 1e-4 else p


def _met_lhand(p, t, T, vx, sp, fall=None):
    """Ліва кисть і лікоть A — від пози польоту до місця на B плавно (нульова швидкість на прибутті), а не за 0,1 с
    разом із тілом: у парах обмежувач ривка шару оживлення згладжує й руки і дотягував різкий захід із проскоком у B."""
    wl = smooth((t - (T - MET_LH0)) / MET_LH)
    if wl <= 0.0:
        return p
    f_ = (fall or met_fall_pose)(min(t, T), vx)
    p.hands = dict(p.hands)
    p.hands["l"] = lerp_hand(sp.hands["l"], f_.hands["l"], wl)
    el_f, el_s = f_.elbows["l"], sp.elbows["l"]
    p.elbows = dict(p.elbows)
    p.elbows["l"] = lambda S: el_f(S).lerp(el_s(S), wl)
    return p


def _hand_arc(p, side, h):
    g, f, u = p.hands[side]
    ev = lambda v, S: v(S) if callable(v) else v
    p.hands = dict(p.hands)
    p.hands[side] = (lambda S: ev(g, S) + Vector((0.0, 0.0, h)), f, u)
    return p


def clip_land_meteor_a(t, L, partner=None, fall=None):
    fall = fall or met_fall_pose
    T = MET_HIT
    sp = met_splash(MET_Y0 - MET_VX * T - 0.04, hip_dy=-0.12)
    if t <= T:
        return _met_sweep(_met_lhand(fall(t, MET_VX), t, T, MET_VX, sp, fall), t, T)
    f0 = fall(T, MET_VX)
    p = blend_pose(_filled(f0), _filled(sp), smooth((t - T) / 0.1))      # груди зупинились, ноги ляскають об мат
    p = _met_lhand(p, t, T, MET_VX, sp, fall)
    hs = hit_spring(t, T, w=18.0, z=0.4)
    p.pelvis = p.pelvis + Vector((0.0, 0.0, -0.015 * hs))
    wp = smooth((t - T - 0.08) / 0.4)                                     # накриття ≤ 0,5 с від удару (§5.4)
    if wp > 0.0:
        pn = pin_a()
        ank_sp = sp.ankles
        p = blend_pose(_filled(p), _filled(pn), wp)
        # коліна — під себе по черзі (права, потім ліва), стопа відривається від мату (без ковзання)
        p.ankles = (feet_track(t, [(0.0, ank_sp[0]), (T + 0.2, ank_sp[0]), (T + 0.46, pn.ankles[0]), (L + 1, pn.ankles[0])])[0],
                    feet_track(t, [(0.0, ank_sp[1]), (T + 0.08, ank_sp[1]), (T + 0.34, pn.ankles[1]), (L + 1, pn.ankles[1])])[0])
        arc = math.sin(math.pi * wp)
        p = _hand_off(p, "l", Vector((0.0, 0.12, 0.09)) * arc)             # ліва — зі стегна B ближнім боком під коліно
        p.pelvis = p.pelvis + Vector((0.0, 0.0, 0.04 * arc))              # підтягуючи коліна, трохи піднімається з B
    return _met_sweep(p, t, T)


def clip_land_meteor_b(t, L, partner=None):
    T = MET_HIT
    hit = hit_spring(t, T, w=14.0, z=0.3)
    p = pin_b(pinned=smooth((t - T - 0.08) / 0.4), tuck=smooth(t / 0.3), legs=0.25 * max(0.0, hit))
    look = smooth(t / 0.2) * (1.0 - smooth((t - T + 0.08) / 0.1))       # бачить, що летить: голова вгору
    p.pitch = p.pitch + 6.0 * abs(hit)
    p.head_pitch = p.head_pitch + 22.0 * look - 12.0 * max(0.0, hit)
    p = _hand_arc(p, "r", 0.12 * max(0.0, hit))                           # дальня рука здригається від удару
    return p


def _met_rib_elbow(S):
    """Лікоть руки на своїх ребрах — позаду вздовж боку в осях тулуба (у світі «позаду» при нахиленому тулубі лягало на
    лінію плече–зап'ястя: полюс вироджувався, лікоть ішов убік-уперед, пальці — в тулуб)."""
    fwd, lr = _body_ax(S)
    down = (S["pelvis"] - S["chest"]).normalized()
    return S["l"] + lr * 0.12 - fwd * 0.28 + down * 0.18


def _met_rib(S):
    fwd, lr = _body_ax(S)
    return S["chest"] + (S["pelvis"] - S["chest"]) * 0.12 + lr * 0.21 + fwd * 0.06


def clip_land_meteor_partial_a(t, L, partner=None):
    T = MET_HIT_P
    sp = met_splash(MET_Y0 - MET_VX_P * T - 0.02, hip_dy=-0.12)
    if t <= T:
        return _met_sweep(_met_lhand(met_fall_pose(t, MET_VX_P), t, T, MET_VX_P, sp), t, T)
    f0 = met_fall_pose(T, MET_VX_P)
    p = blend_pose(_filled(f0), _filled(sp), smooth((t - T) / 0.1))
    p = _met_lhand(p, t, T, MET_VX_P, sp)
    hs = hit_spring(t, T, w=18.0, z=0.4)
    p.pelvis = p.pelvis + Vector((0.0, 0.0, -0.015 * hs))
    w1 = smooth((t - T - 0.1) / 0.45)                                     # підтягує коліна, відштовхується від B
    w2 = smooth((t - T - 0.6) / 0.7)                                      # на коліна
    ank_sp = sp.ankles
    if w1 > 0.0:
        p1 = pin_a(thrown=0.55)
        p = blend_pose(_filled(p), _filled(p1), w1)
        p = _met_lhand(p, t, T, MET_VX_P, sp)                           # ліва — на стегні B: від нього й відштовхується
        p.pelvis = p.pelvis + Vector((0.0, 0.0, 0.07 * math.sin(math.pi * w1)))
    if w2 > 0.0:
        lean = 0.45 * (1.0 - smooth((t - (L - 0.7)) / 0.5))
        kn = kneel_up(t, L, lean=lean)
        p = blend_pose(_filled(p), _filled(kn), w2)
        wr = w2 * (1.0 - smooth((t - (L - 0.55)) / 0.45))
        p = _hand_off(p, "r", Vector((-0.05, -0.02, 0.0)) * wr)
        er = p.elbows["r"]
        p.elbows = dict(p.elbows)
        p.elbows["r"] = lambda S: er(S) + Vector((-0.12 * wr, 0.0, 0.0))
    if w1 > 0.0:
        p1a = pin_a(thrown=0.55).ankles
        kna = kneel_up(t, L).ankles
        p.ankles = (feet_track(t, [(0.0, ank_sp[0]), (T + 0.22, ank_sp[0]), (T + 0.5, p1a[0]), (T + 0.75, p1a[0]),
                                   (T + 1.05, kna[0]), (L + 1, kna[0])])[0],
                    feet_track(t, [(0.0, ank_sp[1]), (T + 0.1, ank_sp[1]), (T + 0.38, p1a[1]), (T + 0.62, p1a[1]),
                                   (T + 0.92, kna[1]), (L + 1, kna[1])])[0])
    clutch = _win(t, T + 0.14, T + 0.5, L - 0.7, L - 0.3)                  # ліва — з B на свої ребра (теж продає)
    if clutch > 0.0:
        hl = p.hands["l"]
        ev = lambda v, S: v(S) if callable(v) else v
        p.hands = dict(p.hands)
        lift_c = 0.1 * math.sin(math.pi * clutch) if t < T + 0.6 else 0.0
        p.hands["l"] = (lambda S: ev(hl[0], S).lerp(_met_rib(S), clutch) + Vector((0.0, 0.0, lift_c)), hl[1], hl[2])
        el = p.elbows["l"]
        p.elbows = dict(p.elbows)
        p.elbows["l"] = lambda S: el(S).lerp(_met_rib_elbow(S), clutch)                   # лікоть позаду вздовж боку:
        p.fist = dict(p.fist)                                                                # пальці лягають уперед по ребрах
        p.fist["l"] = p.fist["l"] * (1.0 - clutch) + 0.1 * clutch
        p.pitch = p.pitch + 10.0 * clutch * w2
    return _met_sweep(p, t, T)


def clip_land_meteor_partial_b(t, L, partner=None):
    T = MET_HIT_P
    hit = hit_spring(t, T, w=14.0, z=0.3)
    tuck = smooth(t / 0.3) * (1.0 - smooth((t - (T + 0.7)) / 0.5))
    sell = _win(t, T + 0.6, T + 1.0, L - 0.8, L - 0.25)                   # обидві руки на груди, коліна вгору
    p = pin_b(pinned=0.0, tuck=tuck, legs=0.4 * max(0.0, hit) + 0.45 * sell)
    look = smooth(t / 0.2) * (1.0 - smooth((t - T + 0.08) / 0.1))
    p.pitch = p.pitch + 6.0 * abs(hit) + 8.0 * sell
    p.head_pitch = p.head_pitch + 22.0 * look - 12.0 * max(0.0, hit) + 15.0 * sell
    p.roll = p.roll - 12.0 * sell                                          # від A, на правий бік
    if sell > 0.0:
        ev = lambda v, S: v(S) if callable(v) else v
        hs_ = dict(p.hands)
        arc_ = 0.22 if t < T + 1.2 else 0.05                             # дугою над боком — лише на груди; з грудей — низом
        for s_, dx in (("l", 0.07), ("r", -0.07)):
            g0, f0, u0 = hs_[s_]
            hs_[s_] = (lambda S, g0=g0, dx=dx: ev(g0, S).lerp(S["chest"] + Vector((dx, 0.06, 0.17)), sell)
                       + Vector((0.0, 0.0, arc_ * math.sin(math.pi * sell))), f0, u0)
        p.hands = hs_
        el = dict(p.elbows)
        for s_, dx in (("l", 0.3), ("r", -0.3)):
            e0 = el[s_]
            el[s_] = (lambda S, e0=e0, dx=dx, s_=s_: e0(S).lerp(S[s_] + Vector((dx, -0.1, 0.05)), sell))
        p.elbows = el
    return p


MET_PALM_BRACE = (0.22, -0.78)   # × рука: долоня від таза A на маті (x — убік від MET_X, y — уздовж тіла): упор уперед
MET_PALM_CHEST = (0.16, -0.25)   #   і біля нижніх ребер — відтиснутись (плече лежачи ≈ −0,46)


def _met_palms(p, y_s, k, elbows="push"):
    """Долоні A пласко на маті (пальці до голови, великі — досередини) у нерухомих точках простору пари — кисті не
    ковзають: k = 0 — упор уперед, куди вдарились; 1 — біля ребер (переставляються дугою). Лікті — угору-назад
    уздовж тіла, не вбік: поруч лежить B. elbows="fours" — рачки: руки майже прямі, лікті назад."""
    p = _filled(p)
    a = BODY["arm"]
    lift = 0.07 * math.sin(math.pi * k)
    def pt(sg):
        b = Vector((MET_X + MET_PALM_BRACE[0] * a * sg, y_s + MET_PALM_BRACE[1] * a, 0.07))
        c = Vector((MET_X + MET_PALM_CHEST[0] * a * sg, y_s + MET_PALM_CHEST[1] * a, 0.07))
        return b.lerp(c, k) + Vector((0.0, 0.0, lift))
    p.hands = {s_: (pt(sg), FWD, RIGHT if s_ == "l" else LEFT) for s_, sg in (("l", 1.0), ("r", -1.0))}
    ev = Vector((0.04, 0.3, -0.15)) if elbows == "fours" else Vector((0.04, 0.18 + 0.1 * k, 0.22))
    p.elbows = {s_: (lambda S, s_=s_, v=Vector((ev.x * sg, ev.y, ev.z)): S[s_] + v) for s_, sg in (("l", 1.0), ("r", -1.0))}
    return p


def met_all_fours(y0):
    """Рачки над місцем, де лежав: стегна прямовисно, коліна на маті під тазом, долоні під плечима (як у продажу
    Bump_Front_Sell_Knees), зсунуто до MET_X."""
    lk = BODY["leg"]
    q = Quaternion(LEFT, math.radians(78.0))
    kp = body(off_for(Vector((0.0, y0, 0.5 * lk + 0.06))), q, knee_bend=0.0)
    dx = Vector((MET_X, 0.0, 0.0))
    kp.pelvis = kp.pelvis + dx
    kp.ankles = (Vector((MET_X + 0.13, y0 + 0.44 * lk, 0.1)), Vector((MET_X - 0.13, y0 + 0.44 * lk, 0.1)))
    tip = Quaternion(RIGHT, math.radians(-75.0))
    kp.foot_rot = (tip, tip)
    kp.knees = (Vector((MET_X + 0.14, y0 - 0.4, 0.0)), Vector((MET_X - 0.14, y0 - 0.4, 0.0)))
    kp.pitch, kp.head_pitch, kp.head_yaw = 8.0, 20.0, 0.0
    kp.hands = {s_: (lambda S, s_=s_, sg=sg: Vector((S[s_].x + 0.02 * sg, S[s_].y,
                                                    max(0.07, S[s_].z - 0.48 * BODY["arm"]))), q @ UP, DOWN)
                for s_, sg in (("l", 1.0), ("r", -1.0))}
    kp.elbows = {s_: (lambda S, s_=s_, sg=sg: S[s_] + Vector((0.1 * sg, 0.15, -0.1))) for s_, sg in (("l", 1.0), ("r", -1.0))}
    kp.fist = {"l": 0.1, "r": 0.1}
    kp.forced = set()
    return kp


MISS_KNEEL_DY = 0.42      # м: таз A з мату на коліна — до стоп (коліна лишаються, де були)


def clip_land_meteor_miss_a(t, L, partner=None, fall=None):
    """Грудьми об мат (руки — вузький упор уперед), корчиться на правий бік (від B), відтискається рачки й стає на
    коліна, тримаючись за груди; кінець — kneel_up (лежачи поруч з B лікті обох не вміщались у 0,5 м від середини)."""
    fall = fall or met_fall_pose
    T = MET_HIT_M
    side = smooth(t / 0.3)
    if t <= T:
        return fall(t, MET_VX_M, side=side)
    f0 = fall(T, MET_VX_M, side=1.0)
    y_s = MET_Y0 - MET_VX_M * T
    kp = smooth((t - (T + 0.15)) / 0.45)                                 # долоні — з упору вперед до ребер
    fl = _met_palms(met_floor(y_s), y_s, kp)
    p = blend_pose(_filled(f0), _filled(fl), smooth((t - T) / 0.12))
    hs = hit_spring(t, T, w=18.0, z=0.35)
    p.pelvis = p.pelvis + Vector((0.0, 0.0, 0.02 * max(0.0, hs) + 0.025 * (1.0 - smooth((t - T - 0.15) / 0.3))))
    sell = _win(t, T + 0.15, T + 0.4, T + 0.5, T + 0.7)                   # корчиться на правий бік — від B
    if sell > 0.0:
        sl = _met_palms(met_floor(y_s, push=0.4, roll=-8.0, frog=(0.0, 0.2)), y_s, kp)
        sl.head_pitch = 12.0
        p = blend_pose(_filled(p), _filled(sl), sell)
    y0 = y_s + MISS_KNEEL_DY
    w1 = smooth((t - (T + 0.55)) / 0.45)                                  # відтискається рачки
    if w1 > 0.0:
        out = blend_pose(_filled(p), _filled(_met_palms(met_all_fours(y0), y_s, 1.0, elbows="fours")), w1)
        hump = 0.12 * math.sin(math.pi * w1) + 0.02 * min(1.0, w1 * 15.0) * (1.0 - w1)
        out.pelvis = out.pelvis + Vector((0.0, 0.0, hump))
        if out.knees:
            out.knees = tuple(k_ + Vector((0.0, 0.0, hump)) for k_ in out.knees)
        out.ankles = tuple(a_ + Vector((0.0, 0.0, 0.6 * hump)) for a_ in out.ankles)
        p = out
    w2 = smooth((t - (T + 1.0)) / 0.45)                                   # сідає на коліна
    if w2 > 0.0:
        lean = 0.5 * (1.0 - smooth((t - (L - 0.7)) / 0.45))
        kn = _shift_v(kneel_up(t, L, lean=lean), Vector((MET_X, y0 - 0.02, 0.0)))
        p = blend_pose(_filled(p), _filled(kn), w2)
        clutch = _win(t, T + 1.1, T + 1.45, L - 0.65, L - 0.25)          # обидві руки — на груди
        if clutch > 0.0:
            ev = lambda v, S: v(S) if callable(v) else v
            hs_ = dict(p.hands)
            el = dict(p.elbows)
            for s_, sg in (("l", 1.0), ("r", -1.0)):
                g0, f0_, u0 = hs_[s_]
                hs_[s_] = (lambda S, g0=g0, sg=sg: ev(g0, S).lerp(_sw_belly(S) + Vector((0.07 * sg, 0.0, 0.05)),
                                                                  clutch), f0_, u0)
                e0 = el[s_]
                el[s_] = (lambda S, e0=e0, sg=sg, s_=s_: e0(S).lerp(S[s_] + Vector((0.28 * sg, 0.0, -0.3)), clutch))
            p.hands, p.elbows = hs_, el
            p.head_pitch = p.head_pitch + 14.0 * clutch
    return p


def met_roll_b(k, ka=1.0, rel=None):
    """Лежачи на спині — перекат через правий бік (від A) на живіт, ≤ 0,5 м; кінець — lying_front, повернутий на 180°
    (вузол не залежить від курсу). Руки посередині — до грудей (не під тілом)."""
    a = -180.0 * k
    q = Quaternion(BACK, math.radians(a)) @ Q_BACK
    sk = math.sin(math.pi * k)
    off = off_for(Vector((-MISS_ROLL * k, 0.0, LIE_Z + (FRONT_Z - LIE_Z) * k + 0.1 * sk
                          + 0.06 * max(0.0, sk) ** 0.25 * smooth((k - 0.5) / 0.2))))
    p = body(off, q, knee_bend=0.15 - 0.1 * k + 0.25 * sk * (1.0 - smooth((k - 0.45) / 0.3)))
    ak = BODY["arm"]
    wm = 1.0 - smooth(ka)
    we = smooth((k - 0.45) / 0.4)
    wt = max(0.0, 1.0 - wm - we)
    p.hands, p.elbows = {}, {}
    wo = smooth(ka * 0.14 / 0.25)                                          # права — над голову за 0,25 с
    wer = smooth((k - 0.75) / 0.25) if rel is None else rel               # і лишається там (rel — коли вниз, у вузол)
    for s_, sg in (("l", 1.0), ("r", -1.0)):
        hm = Vector((0.42 * sg, 0.05, -0.32))
        he = Vector((0.12 * sg, 0.0, 0.32))
        if s_ == "l":
            ht = Vector((-0.05, -0.32, -0.25))
            hv = (hm * wm + ht * wt + he * we) * ak
        else:
            ho = Vector((-0.2, -0.1, 0.92))
            hv = (hm.lerp(ho, wo)).lerp(he, wer) * ak
        def g(S, s_=s_, hv=hv):
            pt = S[s_] + q @ hv
            return Vector((pt.x, pt.y, max(pt.z, 0.06)))
        p.hands[s_] = (g, (q @ DOWN).lerp(q @ UP, we), (q @ FWD).lerp(DOWN, we))
        em = Vector((0.4 * sg, 0.1, -0.1))
        ee = Vector((0.4 * sg, 0.0, 0.0))
        if s_ == "l":
            et = Vector((0.12, -0.35, -0.2))
            ev_ = em * wm + et * wt + ee * we
        else:
            ev_ = em.lerp(Vector((-0.1, 0.0, 0.3)), wo).lerp(ee, wer)
        lift = 0.1 * we if s_ == "l" else 0.1 * we + 0.3 * wo * (1.0 - wer)
        p.elbows[s_] = (lambda S, s_=s_, ev_=ev_, lift=lift: S[s_] + q @ ev_ + Vector((0, 0, lift)))
    p.fist = {"l": 0.15 + 0.3 * wt, "r": 0.15 + 0.3 * wt}
    p.pitch = 10.0 * sk
    p.head_pitch = -30.0 * (1.0 - k) - 10.0 * k + 25.0 * sk
    p.head_yaw = 75.0 * smooth((k - 0.5) / 0.5)
    return p


def clip_land_meteor_miss_b(t, L, partner=None):
    k = smooth((t - 0.12) / 0.33)                                          # котиться одразу — до удару A вже на животі
    # права (з боку A) — над голову раніше, ніж плече ляже на мат, і лежить там витягнута, доки A поруч (лікоть біля
    # голови, не вбік до A); у вузол (лікоть убік) — коли A вже на колінах
    p = met_roll_b(k, ka=t / 0.14, rel=smooth((t - (MET_HIT_M + 1.25)) / 0.6))
    br = smooth((t - 0.6) / 0.4) * (1.0 - smooth((t - (L - 0.4)) / 0.35))
    p.pitch = p.pitch - 3.0 * br * (0.5 + 0.5 * math.sin(2 * math.pi * t / 0.9))   # важко дихає
    return p


# ================================================================ «Ластівка» (swallow_dive, P2-1) — кут → Air → Land
# Склейка (ТЗ §1.2, swallow_dive): кут — Top_Rope_Perch_Loop (набір RING_Cast); Air_Swallow — пірнання вперед над
# центром без сальто: з нахилу вперед тіло лягає горизонтально, спина прогнута, руки врозліт у сторони, ноги разом.
# Стик Air → Land — той самий, що в «Метеора» (висота 1,45 м, 1,34 м/с униз, точка над B), тож пари Land 10+ і ≤6 —
# ті самі функції «Метеора» з іншою позою падіння (руки врозліт, тіло горизонтально, оберт уперед).
# Ступені: 10+ Land_Swallow (тіло на тіло, жертву підкидає, за 0,5 с — накриття); 7–9 Land_Swallow_Knees (жертва
# підняла коліна: A б'ється животом об коліна, складається й відштовхується на свої коліна, тримаючись за живіт;
# B продає — коліна вбило в груди); ≤6 Land_Swallow_Miss (жертва відкотилась ≤ 0,5 м, A б'ється об мат і продає груди).
# Кут th — як у «Метеора» (оберт навколо LEFT на −th): 360 — стоїть, 270 — горизонтально обличчям донизу; пірнання
# вперед — th спадає (сальто «Метеора» — росте).
SW_LEN = 0.8             # §1.2.2: 0,6–0,9 с
SW_TH_START = 335.0      # щойно відірвався від кута: нахил уперед 25°
SW_TH0 = 280.0           # на стику: голова трохи вище таза, груди ведуть
SW_W = -30.0             # °/с на стику: доводить до горизонталі якраз до удару (≈ 0,3 с)
SW_WINGS = (Vector((0.95, 0.05, 0.12)), Vector((0.35, 0.35, 0.05)))   # руки врозліт у сторони (рамка тіла: x — ліво,
SW_BRACE = (Vector((0.3, -0.3, 0.85)), Vector((0.35, 0.3, 0.1)))      #   y — назад, z — до голови); 7–9: уперед-униз


def sw_arms(p, q, side=0.0, brace=0.0):
    """Руки «ластівки» (крила врозліт); side — у боки до мату (промах, як у «Метеора»); brace — уперед-униз (7–9)."""
    ak = BODY["arm"]
    p.hands, p.elbows = {}, {}
    for s_, sg in (("l", 1.0), ("r", -1.0)):
        h_ = SW_WINGS[0].lerp(MET_SIDE[0], side).lerp(SW_BRACE[0], brace)
        e_ = SW_WINGS[1].lerp(MET_SIDE[1], side).lerp(SW_BRACE[1], brace)
        hv = Vector((h_.x * sg, h_.y, h_.z))
        ev = Vector((e_.x * sg, e_.y, e_.z))
        f = (q @ hv).normalized()
        u = q @ Vector((0.0, -1.0, 0.0))
        u = u - f * u.dot(f)
        u = u.normalized() if u.length > 1e-4 else q @ UP
        p.hands[s_] = (lambda S, s_=s_, hv=hv: S[s_] + q @ (hv * ak), f, u)
        p.elbows[s_] = (lambda S, s_=s_, ev=ev: S[s_] + q @ ev)
    p.forced = set()
    p.fist = {"l": 0.1, "r": 0.1}
    return p


def sw_th(t, L):
    """Кут нахилу: ермітова крива від SW_TH_START (швидкість 0) до SW_TH0 (швидкість SW_W)."""
    u = t / L
    h00, h01, h11 = 2 * u ** 3 - 3 * u ** 2 + 1, -2 * u ** 3 + 3 * u ** 2, u ** 3 - u ** 2
    return h00 * SW_TH_START + h01 * SW_TH0 + h11 * SW_W * L


def clip_air_swallow(t, L):
    """Пірнання: вершина дуги на 0,35 L, тіло з нахилу лягає горизонтально, спина прогнута, руки врозліт; на стику —
    висота, швидкість і кут ті самі, що на старті Land."""
    u = t / L
    ta = 0.35 * L
    g_ = MET_V0 / (L - ta)
    za = MET_Z0 + 0.5 * g_ * (L - ta) ** 2
    z = za - 0.5 * g_ * (t - ta) ** 2
    y = MET_Y0 + 0.4 * (1.0 - u)
    q = Quaternion(LEFT, math.radians(-sw_th(t, L)))
    p = body(off_for(Vector((MET_X, y, z))), q, knee_bend=0.08)
    p.pitch = -14.0 * math.sin(math.pi * min(1.0, u * 1.15))         # прогин спини; на стику — 0, як у падінні
    p.head_pitch = -25.0
    return sw_arms(p, q)


def sw_fall_pose(t, vx, side=0.0, brace=0.0):
    """Падіння від стику Air_Swallow → Land: балістика (g), оберт SW_W, тіло горизонтально, руки врозліт."""
    z = MET_Z0 - MET_V0 * t - 4.905 * t * t
    q = Quaternion(LEFT, math.radians(-(SW_TH0 + SW_W * t)))
    p = body(off_for(Vector((MET_X, MET_Y0 - vx * t, z))), q, knee_bend=0.08)
    p.head_pitch = -25.0
    return sw_arms(p, q, side=side, brace=brace)


def clip_land_swallow_a(t, L, partner=None):
    return clip_land_meteor_a(t, L, partner, fall=sw_fall_pose)


def clip_land_swallow_b(t, L, partner=None):
    """Як у «Метеора», але жертву підкидає сильніше: ноги й плечі відриваються від мату на ударі."""
    T = MET_HIT
    hit = hit_spring(t, T, w=14.0, z=0.3)
    p = pin_b(pinned=smooth((t - T - 0.08) / 0.4), tuck=smooth(t / 0.3), legs=0.45 * max(0.0, hit))
    look = smooth(t / 0.2) * (1.0 - smooth((t - T + 0.08) / 0.1))
    p.pitch = p.pitch + 10.0 * abs(hit)
    p.head_pitch = p.head_pitch + 22.0 * look - 16.0 * max(0.0, hit)
    return _hand_arc(p, "r", 0.16 * max(0.0, hit))


def clip_land_swallow_miss_a(t, L, partner=None):
    return clip_land_meteor_miss_a(t, L, partner, fall=sw_fall_pose)


# --- 7–9: коліна. B лежить так само поперек (90°), але зсунутий уздовж свого тіла до голови — під животом A його
# підняті коліна (стопи по мату до сідниць). A влучає животом (не грудьми) і складається навколо колін.
SW_ZHIT_K, SW_VX_K = 0.7, 0.3                  # таз A над колінами B у мить удару (коліна ≈ 0,55 м + живіт 0,12)
SW_HIT_K = met_hit_time(SW_ZHIT_K)             # ≈ 0,28 с
SW_KNEE_DX = 0.23                              # коліно B від його таза вздовж тіла (стопа зсунута на SW_KNEE_SLIDE)
SW_KNEE_SLIDE = 0.44
SW_BELLY = 0.2                                 # живіт A — на 0,2 м від таза до голови
KNEES_OFF = Vector((MET_X - SW_KNEE_DX, MET_Y0 - SW_VX_K * SW_HIT_K - SW_BELLY, 0.0))
KNEES_LEN = 2.4
KNEES_KNEEL = Vector((MET_X, 0.12, 0.0))      # A стає на коліна позаду від місця удару (від B)


def _shift_v(p, v):
    """Уся поза зі зсувом v (таз, щиколотки, полюси колін); кисті й лікті — від плечей, ідуть самі."""
    p.pelvis = p.pelvis + v
    if p.ankles:
        p.ankles = tuple(a_ + v for a_ in p.ankles)
    if p.knees:
        p.knees = tuple(k_ + v for k_ in p.knees)
    return p


def sw_knees_b(t, L):
    """B бачить, що летить, і підтягує коліна (стопи по мату до сідниць); удар — коліна просідають; далі ноги
    сповзають на мат, B обома руками за груди (продає), кінець — lying_back."""
    T = SW_HIT_K
    kup = _win(t, 0.0, 0.22, T + 0.25, T + 0.85)
    hit = hit_spring(t, T, w=16.0, z=0.35)
    sell = _win(t, T + 0.35, T + 0.75, L - 0.75, L - 0.25)
    p = lying(knee_bend=0.15)
    off_b = off_for(Vector((0.0, 0.0, LIE_Z)))
    k_ = kup * (1.0 - 0.12 * max(0.0, hit))
    a0 = Vector((0.0, -0.25 * 0.15, 0.35 * 0.15))                    # як у вузлі lying_back
    p.ankles = (xf(off_b, Q_BACK, REST["ankle_l"] + a0.lerp(Vector((0.02, 0.0, SW_KNEE_SLIDE)), k_)),
                xf(off_b, Q_BACK, REST["ankle_r"] + a0.lerp(Vector((-0.02, 0.0, SW_KNEE_SLIDE)), k_)))
    p.knees = (xf(off_b, Q_BACK, REST["knee_l"] + Vector((0.03 + 0.05 * kup + 0.3 * sell, -0.6, 0.0))),
               xf(off_b, Q_BACK, REST["knee_r"] + Vector((-0.03 - 0.05 * kup - 0.3 * sell, -0.6, 0.0))))
    p.pelvis = p.pelvis + Vector((0.0, 0.0, -0.012 * max(0.0, hit)))
    look = smooth(t / 0.2) * (1.0 - smooth((t - T + 0.05) / 0.12))
    p.head_pitch = p.head_pitch + 25.0 * look - 10.0 * max(0.0, hit) + 15.0 * sell
    p.pitch = p.pitch + 5.0 * abs(hit) + 8.0 * sell
    if sell > 0.0:
        ev = lambda v, S: v(S) if callable(v) else v
        hs_ = dict(p.hands)
        for s_, dx in (("l", 0.07), ("r", -0.07)):
            g0, f0, u0 = hs_[s_]
            hs_[s_] = (lambda S, g0=g0, dx=dx: ev(g0, S).lerp(S["chest"] + Vector((dx, 0.06, 0.17)), sell)
                       + Vector((0.0, 0.0, 0.2 * math.sin(math.pi * sell))), f0, u0)
        p.hands = hs_
        el = dict(p.elbows)
        for s_, dx in (("l", 0.3), ("r", -0.3)):
            e0 = el[s_]
            el[s_] = (lambda S, e0=e0, dx=dx, s_=s_: e0(S).lerp(S[s_] + Vector((dx, -0.1, 0.05)), sell))
        p.elbows = el
    return p


def clip_land_swallow_knees_b(t, L, partner=None):
    return sw_knees_b(t, L)


def _sw_belly(S):
    """Кисть на своєму животі (під ребрами, спереду)."""
    fwd, lr = _body_ax(S)
    return S["pelvis"] + (S["chest"] - S["pelvis"]) * 0.7 + fwd * 0.25


def clip_land_swallow_knees_a(t, L, partner=None):
    """Животом на коліна B: руки вперед-униз (пружинить), тіло складається навколо колін (голова й ноги вниз), далі
    відштовхується назад на свої коліна позаду, обома руками за живіт; кінець — kneel_up."""
    T = SW_HIT_K
    br = smooth((t - (T - 0.3)) / 0.25)
    if t <= T:
        return sw_fall_pose(t, SW_VX_K, brace=br)
    f0 = sw_fall_pose(T, SW_VX_K, brace=1.0)
    hs = hit_spring(t, T, w=15.0, z=0.35)
    fold = _win(t, T, T + 0.12, T + 0.3, T + 0.6)                        # склався навколо колін
    ank0 = f0.ankles
    p = _filled(f0)
    p.pelvis = p.pelvis + Vector((0.0, 0.0, -0.02 * max(0.0, hs)))
    p.pitch = 28.0 * fold
    w2 = smooth((t - T - 0.25) / 0.75)                                    # на коліна
    lean = 0.55 * (1.0 - smooth((t - (L - 0.75)) / 0.5))
    kn = _shift_v(kneel_up(t, L, lean=lean), KNEES_KNEEL)
    if w2 > 0.0:
        p = blend_pose(_filled(p), _filled(kn), w2)
    # ноги: падають униз (коліна на мат), стопи в повітрі, доки не стали на місце вузла
    p.ankles = (feet_track(t, [(0.0, ank0[0]), (T + 0.1, ank0[0] + Vector((0.0, 0.0, -0.18))),
                               (T + 0.75, kn.ankles[0]), (L + 1, kn.ankles[0])])[0],
                feet_track(t, [(0.0, ank0[1]), (T + 0.16, ank0[1] + Vector((0.0, 0.0, -0.18))),
                               (T + 0.85, kn.ankles[1]), (L + 1, kn.ankles[1])])[0])
    clutch = _win(t, T + 0.3, T + 0.75, L - 0.7, L - 0.3)                # обидві руки — на живіт
    if clutch > 0.0:
        ev = lambda v, S: v(S) if callable(v) else v
        hs_ = dict(p.hands)
        el = dict(p.elbows)
        for s_, dx in (("l", 0.06), ("r", -0.06)):
            g0, f0_, u0 = hs_[s_]
            hs_[s_] = (lambda S, g0=g0, dx=dx: ev(g0, S).lerp(_sw_belly(S) + Vector((dx, 0.0, 0.0)), clutch), f0_, u0)
            e0 = el[s_]
            sg = 1.0 if s_ == "l" else -1.0
            el[s_] = (lambda S, e0=e0, sg=sg, s_=s_: e0(S).lerp(S[s_] + Vector((0.28 * sg, -0.05, -0.25)), clutch))
        p.hands, p.elbows = hs_, el
        p.fist = {"l": 0.3 * (1.0 - clutch) + 0.5 * clutch, "r": 0.3 * (1.0 - clutch) + 0.5 * clutch}
        p.head_pitch = p.head_pitch + 12.0 * clutch
    return p


# ================================================================ зустрічні з місця (whip 10+, P2-2) — A стоїть
# Склейка (ТЗ §5.1, whip): Whip_Ropes* до release → план «спина в канат» (Rope_* від rope_hit) → цей кадр: B уже біжить
# назад і за CT_LEAD від контакту, A стоїть на місці (≤ 0,5 м, пів-кроку вперед на удар) і зустрічає. Дія A — та сама
# накладка, що в Center_* (_ctr_act), реакція B — той самий бамп, що в Center_* (як у шолдер таклі: з бігу, стопи — де
# стояли в мить удару, доки не піднімуться). Кадр починається склейкою: вузлів на старті немає; A уже чекає (дія
# почалась), B — на швидкості бігу.
CT_LEAD = 0.42           # м — B пробігає до контакту (≤ 0,5 м від свого старту)


class Cnt:
    """Зустрічний з місця. a — дія A, як у Ctr; b — реакція B ("back", "back_fast", "side", "flip"); bx — лінія бігу B
    від A (м, простір A: + — ліворуч від A); gap — від таза A до таза B уздовж Y у мить контакту; lunge — пів-крок A
    уперед лівою на удар (м)."""
    pass_ = False

    def __init__(self, name, a, b, bx, gap, lunge=0.2, run=None, L=2.2, lead_b=0.12, reach=None):
        self.name, self.a, self.b, self.bx, self.gap, self.lunge, self.reach = name, a, b, bx, gap, lunge, reach
        self.run = run or CT_RUN
        self.v = self.run.stride / self.run.L
        self.t_hit = CT_LEAD / self.v
        self.L, self.lead_b = L, lead_b

    def act_w(self, t):
        after = 0.14 if self.b == "flip" else 0.35          # на сальто рука одразу йде вниз — ноги B летять над нею
        return track(t, [(0.0, 0.3), (self.t_hit - 0.03, 1.0), (self.t_hit + 0.08, 1.0), (self.t_hit + after, 0.0),
                         (99.0, 0.0)])

    def kick_w(self, t):
        return track(t, [(0.0, 0.35), (self.t_hit - 0.03, 1.0), (self.t_hit + 0.1, 1.0), (self.t_hit + 0.5, 0.0),
                         (99.0, 0.0)])

    def b_offset(self):
        return Vector((self.bx, -self.lunge * 0.6 - self.gap - CT_LEAD, 0.0))


COUNTERS = [
    Cnt("Counter_Clothesline", "line", "back", -0.7, 0.47),          # ноги B летять уперед — повз ноги A
    Cnt("Counter_Clothesline_Flip", "line", "flip", -0.7, 0.45),
    Cnt("Counter_ShoulderBlock", "block", "back", 0.62, 0.38, lunge=0.15, reach=0.05),   # ліве плече в ліве плече
    Cnt("Counter_BigBoot", "boot", "back", -0.1, 0.98, lunge=0.0, lead_b=0.03),
    Cnt("Counter_Lariat_Left", "line_l", "side", 0.85, 0.49, lunge=0.12, reach=0.05),
]


BLOCK_TUCK = (("l", Vector((-0.12, -0.2, -0.2))), ("r", Vector((0.1, -0.2, -0.18))))   # кулаки перед грудьми, від плечей


def _tuck(p, w):
    """Кулаки притиснуті перед грудьми (плечовий блок — б'ються плечима, не руками)."""
    if w <= 0.0:
        return p
    ev = lambda v, S: v(S) if callable(v) else v
    p = _filled(p)
    hs = dict(p.hands)
    for s_, v in BLOCK_TUCK:
        g, a_, b_ = hs[s_]
        hs[s_] = (lambda S, g=g, v=v, s_=s_: lerp(ev(g, S), S[s_] + v, w), a_, b_)
    p.hands = hs
    p.fist = {s_: lerp(p.fist[s_], 0.8, w) for s_ in "lr"}
    return p


def counter_a(c):
    """Стоїть у стійці; на удар — пів-кроку лівою вперед (вага в удар), права підтягується після; дія — _ctr_act."""
    def f(t, L, partner=None):
        p = stance(t, L, sway=0.0)
        lg = c.lunge
        lx = 0.08 if c.a == "block" else 0.0             # блок — крок лівою вперед-ліворуч, до плеча B
        p.ankles = steps(t, [(0.0, (0.2, -0.105)), (c.t_hit - 0.02, (0.2 + lx, -0.105 - lg))],
                            [(0.0, (-0.2, 0.075)), (c.t_hit + 0.35, (-0.2, 0.075)), (c.t_hit + 0.7, (-0.2 + lx, 0.075 - lg))])
        p.foot_rot = (Quaternion(UP, math.radians(10.0)), Quaternion(UP, math.radians(-14.0)))
        ky = track(t, [(0.0, 0.0), (c.t_hit - 0.02, 0.6), (c.t_hit + 0.35, 0.6), (c.t_hit + 0.7, 1.0), (99.0, 1.0)])
        # таз іде за правою стопою лише після удару (рука вже пішла з грудей B), не тисне руку в B
        p.pelvis = p.pelvis + Vector((lx * ky, -lg * ky, 0.0))
        if c.a in ("block", "line_l"):                   # тулуб до B (голова ліворуч): плече чи рука дістає, ноги — ні
            p.roll = p.roll + 8.0 * c.act_w(t)
        if c.a == "block":                               # з місця: кулаки притиснуті до грудей, б'є плече (не руки)
            w = c.act_w(t)
            ev = lambda v, S: v(S) if callable(v) else v
            hs = dict(p.hands)
            for s_, v in BLOCK_TUCK:
                g, a_, b_ = hs[s_]
                hs[s_] = (lambda S, g=g, v=v, s_=s_: lerp(ev(g, S), S[s_] + v, w), a_, b_)
            p.hands = hs
        return _ctr_act(p, t, c, c.act_w(t))
    return f


def _run_feet_hold(t, t_c, R, reach=None, y_hit=-CT_LEAD):
    """Стопи бігу після мить t_c: опорна стоїть, де стояла; махова довершує мах і стає туди, куди йшла (без ривка
    вниз і без ковзання по мату). reach — гальмівний крок: махова стає не далі reach попереду таза в мить удару
    (y_hit, простір B), за 0,2 с (ноги не летять уперед у ноги A)."""
    out = []
    for i, ph0 in enumerate(((t_c / R.L) % 1.0, (t_c / R.L + 0.5) % 1.0)):
        if ph0 < R.duty:
            out.append(run_pose(t_c, R).ankles[i].copy())
            continue
        T = (1.0 - ph0) * R.L
        land = run_pose(t_c + T, R).ankles[i]
        if reach is None or land.y >= y_hit - reach:
            out.append(run_pose(min(max(t, t_c), t_c + T), R).ankles[i].copy())
            continue
        p0 = run_pose(t_c, R).ankles[i]
        u = min(1.0, max(0.0, (t - t_c) / 0.2))
        tgt = Vector((p0.x, y_hit - reach, land.z))
        q = p0.lerp(tgt, smooth(u))
        q.z = lerp(p0.z, land.z, smooth(u)) + 0.05 * math.sin(math.pi * u)
        out.append(q)
    return out


def counter_b(c):
    """Біжить на A; від удару — бамп з місця, де вдарили. Стопи: доки бамп тримає свої стопи на маті, B стоїть на
    стопах бігу (махова доходить і стає); щойно стопи бампа рушають (відрив), різниця тане — без ковзання."""
    def hitf(s):
        if c.b in ("back", "back_fast"):
            return clip_bump_back(min(BUMP_LEN, s * (1.2 if c.b == "back_fast" else 1.0)), BUMP_LEN)
        if c.b == "side":
            return clip_bump_side(min(SBUMP_LEN, s), SBUMP_LEN)
        return clip_flip_front(min(FLIP_LEN, s), FLIP_LEN)

    cache = {}

    def lift_times(t_c):
        """Коли стопа бампа рушає з місця (с від початку бампа) і як далеко тоді стопа бігу від неї (м)."""
        if "l" not in cache:
            h0 = _shift(hitf(0.0), -CT_LEAD).ankles
            out = []
            for i in range(2):
                s_ = 0.0
                while s_ < 1.2 and (_shift(hitf(s_), -CT_LEAD).ankles[i] - h0[i]).length < 0.01:
                    s_ += 0.01
                rf = _run_feet_hold(t_c + s_, t_c, c.run, c.reach)[i]
                out.append((s_, (rf - h0[i]).length))
            cache["l"] = out
        return cache["l"]

    def f(t, L, partner=None):
        t_c = c.t_hit - c.lead_b
        tb = t - t_c
        if tb <= 0:
            tw = smooth((t - (c.t_hit - 0.3)) / 0.18) if c.a == "block" else 0.0
            return _tuck(run_pose(t, c.run), tw)
        hit = _shift(hitf(tb), -CT_LEAD)
        h0 = _shift(hitf(0.0), -CT_LEAD).ankles
        if hit.ankles and h0:
            lifts = lift_times(t_c)
            an = []
            for i, (a_, a0) in enumerate(zip(hit.ankles, h0)):
                tl, dl = lifts[i]
                # різниця тане від відриву стопи бампа — тим довше, чим далі стопа бігу від стопи бампа (без ривка)
                k = smooth((tb - tl) / min(0.45, max(0.18, dl / 1.4)))
                r_ = _run_feet_hold(t, t_c, c.run, c.reach)[i]
                an.append(a_ + (r_ - a0) * (1.0 - k))
            hit.ankles = tuple(an)
        tw = (smooth((t - (c.t_hit - 0.3)) / 0.18) * (1.0 - smooth((tb - 0.2) / 0.25))) if c.a == "block" else 0.0
        w = smooth(tb / 0.12)
        if w >= 1.0:
            return _tuck(hit, tw)
        q = blend_pose(_filled(run_pose(t_c, c.run)), _filled(hit), w)
        q.ankles = hit.ankles
        return _tuck(q, tw)
    return f


# ================================================================ повернення з канатів (whip 7–9, P2-2) — одиночні
# Склейка (ТЗ §5.1, whip 7–9): Whip_Ropes_Resist до release → план «спина в канат» → цей кадр: жертва вже біжить назад
# від канатів на швидкості, гальмує за ≤ 0,45 м і стає в стійку (далі — наступна дія в центрі). Втомлений — з бігу
# Run_Tired_Loop, важко дихає, руки висять; спотикання — з Run_Jog_Loop, носок чіпляє мат, тулуб пірнає вперед, руки
# ловлять рівновагу, два кроки — і стійка.
RET_TIRED_LEN, RET_STUMBLE_LEN = 1.8, 1.4


def _brake_tau(t, td):
    """«Час бігу» з рівним гальмуванням до нуля за td с: шлях — v·td/2."""
    u = min(t, td)
    return u - u * u / (2.0 * td)


def _brake_to_stance(p, t, L, t_stop, dur=0.5):
    """Після зупинки — стійка там, де став: ліва стопа, потім права, з підйомом (без ковзання)."""
    k = smooth((t - t_stop) / dur) if t > t_stop else 0.0
    if k <= 0.0:
        return p
    st = stance(t, L, sway=0.0)
    _shift(st, p.pelvis.y)
    frozen = p.ankles
    q = blend_pose(_filled(p), _filled(st), k)

    def step(a_, b_, w):
        w = min(1.0, max(0.0, w))
        m = lerp(a_, b_, smooth(w))
        return m + Vector((0.0, 0.0, 0.07 * math.sin(math.pi * w) * min(1.0, (a_ - b_).length / 0.015)))
    dy = Vector((0.0, p.pelvis.y, 0.0))
    sl, sr_ = foot(0.2, -0.105) + dy, foot(-0.2, 0.075) + dy
    q.ankles = (step(frozen[0], sl, k / 0.6), step(frozen[1], sr_, (k - 0.4) / 0.6))
    st_rot = (Quaternion(UP, math.radians(10.0)), Quaternion(UP, math.radians(-14.0)))
    q.foot_rot = tuple(fr.slerp(r_, k) for fr, r_ in zip(p.foot_rot, st_rot))
    q.knees = None if k > 0.5 else p.knees
    return q


def _arms_hang(p, w, out=0.1, fwd=-0.1, down=-0.55):
    if w <= 0.0:
        return p
    ev = lambda v, S: v(S) if callable(v) else v
    p = _filled(p)
    hs = dict(p.hands)
    for s_, sg in (("l", 1.0), ("r", -1.0)):
        g, a_, b_ = hs[s_]
        hs[s_] = (lambda S, g=g, sg=sg, s_=s_: lerp(ev(g, S), S[s_] + Vector((out * sg, fwd, down)) * BODY["arm"], w), a_, b_)
    p.hands = hs
    return p


def clip_rope_return_tired(t, L, partner=None):
    R = RUN_LOOPS["Run_Tired_Loop"]
    td = 0.36                                            # 2,14 м/с · 0,36 / 2 ≈ 0,39 м
    p = run_pose(_brake_tau(t, td), R)
    p = _brake_to_stance(p, t, L, td, dur=0.55)
    tw = _win(t, 0.15, 0.5, L - 0.75, L - 0.3)          # важко дихає, зігнувся, руки висять — до стійки наприкінці
    br = 0.5 + 0.5 * math.sin(2.0 * math.pi * t / 0.7)
    p.pitch = p.pitch + (14.0 + 4.0 * br) * tw
    p.head_pitch = p.head_pitch + 18.0 * tw
    p.pelvis = p.pelvis + Vector((0.0, 0.0, -0.03 * tw))
    return _arms_hang(p, tw)


def clip_rope_return_stumble(t, L, partner=None):
    R = CT_RUN
    td = 0.32                                            # 2,5 м/с · 0,32 / 2 = 0,40 м
    p = run_pose(_brake_tau(t, td), R)
    p = _brake_to_stance(p, t, L, td, dur=0.5)
    dip = track(t, [(0.0, 0.0), (0.06, 0.0), (0.2, 1.0), (0.42, 0.45), (0.8, 0.0), (99.0, 0.0)])   # пірнає вперед
    p.pitch = p.pitch + 24.0 * dip
    p.head_pitch = p.head_pitch - 12.0 * dip
    p.pelvis = p.pelvis + Vector((0.0, 0.0, -0.05 * dip))
    fl = _win(t, 0.08, 0.2, 0.5, 0.85)                   # руки вперед-убік — ловить рівновагу
    if fl > 0.0:
        ev = lambda v, S: v(S) if callable(v) else v
        p = _filled(p)
        hs = dict(p.hands)
        for s_, sg, ph in (("l", 1.0, 0.0), ("r", -1.0, 0.5)):
            g, a_, b_ = hs[s_]
            hs[s_] = (lambda S, g=g, sg=sg, s_=s_, ph=ph: lerp(ev(g, S), S[s_] + Vector((
                0.38 * sg, -0.3 - 0.06 * math.sin(2 * math.pi * (t / 0.45 + ph)), -0.08)) * BODY["arm"], fl), a_, b_)
        p.hands = hs
    return p


# ================================================================ реєстр
LL_CONTACTS = {(a_, b_) for a_ in ("hand_l", "hand_r", "lowerarm_l", "lowerarm_r", "upperarm_l", "spine_03", "spine_02")
               for b_ in ("calf_r", "foot_r", "ball_r")}
for _nm, _ln, _fa, _fb, _loop in [
    ("LegLock_Enter", LL_ENTER_LEN, clip_ll_enter_a, clip_ll_enter_b, False),
    ("LegLock_Hold_Loop", LL_LOOP, clip_ll_hold_a, clip_ll_hold_b, True),
    ("LegLock_Tap", LL_TAP_LEN, clip_ll_tap_a, clip_ll_tap_b, False),
    ("LegLock_Escape", LL_ESC_LEN, clip_ll_escape_a, clip_ll_escape_b, False),
    ("LegLock_Reach", LL_REACH_LEN, clip_ll_reach_a, clip_ll_reach_b, False),
    ("LegLock_Enter_Fail", LL_FAIL_LEN, clip_ll_fail_a, clip_ll_fail_b, False),
    ("LegLock_Hold_Partial_Loop", LL_LOOP, clip_ll_partial_a, clip_ll_partial_b, True),
] + [(C.name, C.L, crank_a(C), crank_b(C), False) for C in LL_CRANKS]:
    MOVE_CLIPS.append((_nm, _ln, _fa, _fb, _loop, LL_OFF.copy(), LL_YAW))
    CENTER_RULE.add(_nm)
    MOVE_CONTACTS[_nm] = LL_CONTACTS
CLIP_DEFINES["LegLock_Enter"] = {"A": "leglock_A", "B": "leglock_B"}
KEEP_OFFSET.add("LegLock_Enter")
MOVE_CONTACTS["LegLock_Enter_Fail"] = LL_CONTACTS | {(a_, b_) for a_ in ("upperarm_l", "clavicle_l", "spine_03")
                                                     for b_ in ("foot_r", "ball_r")}
_LLN = {"A": ("leglock_A", "leglock_A"), "B": ("leglock_B", "leglock_B")}
MOVE_CLIP_NODES.update({
    "LegLock_Enter": {"A": ("stance", "leglock_A"), "B": ("lying_back", "leglock_B")},
    "LegLock_Hold_Loop": _LLN, "LegLock_Reach": _LLN, "LegLock_Hold_Partial_Loop": _LLN,
    "LegLock_Tap": {"A": ("leglock_A", "kneel_up"), "B": ("leglock_B", "lying_back")},
    "LegLock_Escape": {"A": ("leglock_A", "kneel_up"), "B": ("leglock_B", "lying_back")},
    "LegLock_Enter_Fail": {"A": ("stance", "kneel_up"), "B": ("lying_back", "lying_back")},
})
for C in LL_CRANKS:
    MOVE_CLIP_NODES[C.name] = _LLN
    MOVE_EVENTS[C.name] = [("qte_open", QTE_OPEN, "-", "-"), ("qte_close", QTE_CLOSE, "-", "-"),
                           ("contact", C.t_pk, "lowerarm_l", "calf_r"), ("sell", C.t_pk + 0.05, "-", "calf_r")]
MOVE_EVENTS.update({
    "LegLock_Enter": [("contact", LL_LOCK, "lowerarm_l", "calf_r"), ("sell", LL_LOCK + 0.1, "-", "calf_r")],
    "LegLock_Tap": [("tap", tt, "-", "hand_r") for tt in LL_TAPS],
    "LegLock_Escape": [("release", 0.5, "-", "calf_r"), ("sell", 0.75, "-", "calf_r")],
    "LegLock_Reach": [("reach", 1.1, "-", "hand_l")],
    "LegLock_Enter_Fail": [("contact", 0.92, "upperarm_l", "foot_r"), ("sell", 0.95, "upperarm_l", "-")],
})
for _nm, _fn in [("Rope_Grab_Back", clip_rope_grab_back), ("Rope_Grab_Front", clip_rope_grab_front)]:
    MOVE_CLIPS.append((_nm, ROPE_GRAB_LEN, _fn, None, False, None, 0))
    MOVE_EVENTS[_nm] = [("rope_grab", ROPE_GRAB_T, "hand_l", "-")]
MOVE_CLIP_NODES.update({"Rope_Grab_Back": {"A": ("lying_back", None)}, "Rope_Grab_Front": {"A": ("lying_front", None)}})

AB_CONTACTS = {(a_, b_) for a_ in ("hand_l", "hand_r", "lowerarm_l", "lowerarm_r")
               for b_ in ("hand_r", "lowerarm_r", "upperarm_r")}
for _nm, _ln, _fa, _fb, _loop in [
    ("Armbar_Enter", AB_ENTER_LEN, clip_ab_enter_a, clip_ab_enter_b, False),
    ("Armbar_Hold_Loop", AB_LOOP, clip_ab_hold_a, clip_ab_hold_b, True),
    ("Armbar_Tap", AB_TAP_LEN, clip_ab_tap_a, clip_ab_tap_b, False),
    ("Armbar_Escape", AB_ESC_LEN, clip_ab_escape_a, clip_ab_escape_b, False),
    ("Armbar_Reach", AB_REACH_LEN, clip_ab_reach_a, clip_ab_reach_b, False),
    ("Armbar_Enter_Fail", AB_FAIL_LEN, clip_ab_fail_a, clip_ab_fail_b, False),
    ("Armbar_Hold_Partial_Loop", AB_LOOP, clip_ab_partial_a, clip_ab_partial_b, True),
] + [(C.name, C.L, ab_crank_a(C), ab_crank_b(C), False) for C in AB_CRANKS]:
    MOVE_CLIPS.append((_nm, _ln, _fa, _fb, _loop, AB_OFF.copy(), AB_YAW))
    CENTER_RULE.add(_nm)
    MOVE_CONTACTS[_nm] = AB_CONTACTS
CLIP_DEFINES["Armbar_Enter"] = {"A": "armbar_A", "B": "armbar_B"}
KEEP_OFFSET.add("Armbar_Enter")
_ABN = {"A": ("armbar_A", "armbar_A"), "B": ("armbar_B", "armbar_B")}
MOVE_CLIP_NODES.update({
    "Armbar_Enter": {"A": (None, "armbar_A"), "B": (None, "armbar_B")},
    "Armbar_Hold_Loop": _ABN, "Armbar_Reach": _ABN, "Armbar_Hold_Partial_Loop": _ABN,
    "Armbar_Tap": {"A": ("armbar_A", "kneel_up"), "B": ("armbar_B", "lying_front")},
    "Armbar_Escape": {"A": ("armbar_A", "kneel_up"), "B": ("armbar_B", "lying_front")},
    "Armbar_Enter_Fail": {"A": (None, "stance"), "B": (None, "stance")},
})
for C in AB_CRANKS:
    MOVE_CLIP_NODES[C.name] = _ABN
    MOVE_EVENTS[C.name] = [("qte_open", QTE_OPEN, "-", "-"), ("qte_close", QTE_CLOSE, "-", "-"),
                           ("contact", C.t_pk, "hand_l", "lowerarm_r"), ("sell", C.t_pk + 0.05, "-", "upperarm_r")]
MOVE_EVENTS.update({
    "Armbar_Enter": [("sell", AB_LAND, "-", "spine_03"), ("contact", AB_LOCK, "hand_l", "lowerarm_r")],
    "Armbar_Tap": [("tap", tt, "-", "hand_l") for tt in AB_TAPS],
    "Armbar_Escape": [("release", 0.45, "-", "lowerarm_r"), ("sell", 0.7, "-", "upperarm_r")],
    "Armbar_Reach": [("reach", 1.1, "-", "hand_l")],
    "Armbar_Enter_Fail": [("release", 0.5, "-", "hand_r")],
})

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
                      ("Ref_Count_Pin_3_Slow", SLOW3[2] + 1.1, clip_ref_count3_slow),
                      ("Ref_Count_Pin_2_Slow", SLOW3[1] + 1.7, clip_ref_count2_slow),
                      ("Ref_Count_Pin_1_Slow", SLOW3[0] + 1.7, clip_ref_count1_slow),
                      ("Ref_Count_Pin_2_Half", SLOW3[2] + 1.4, clip_ref_count2half)]:
    MOVE_CLIPS.append((_nm, _ln, _fn, None, False, None, 0))
    CENTER_RULE.add(_nm)
MOVE_CLIP_NODES.update({"Kneel_To_Stance": {"A": ("kneel_up", "stance")},
                        "Kneel_Disbelief": {"A": ("kneel_up", "kneel_up")}})
MOVE_CLIP_NODES.update({_nm: {"A": ("ref_kneel", "ref_kneel")} for _nm in
                        ("Ref_Count_Pin_3_Slow", "Ref_Count_Pin_2_Slow", "Ref_Count_Pin_1_Slow", "Ref_Count_Pin_2_Half")})


# P1-1b для старих швидких кліпів рахунку (крок 0,95 с, старий бій): той самий вхід і вихід через ref_kneel, на «два» —
# той самий жест. Довжини й мітки не змінюються (вхід — до першого замаху, вихід — після останнього удару).
def clip_ref_count3_node(t, L):
    return _ref_edges(clip_ref_count3(t, L), t, L)


def clip_ref_count2_node(t, L):
    return _ref_edges(_ref_sign(clip_ref_count2(t, L), t, COUNT2_TIMES[-1] + 0.18, 2), t, L)


_OLD_COUNT = {"Ref_Count_Pin_3": clip_ref_count3_node, "Ref_Count_Pin_2": clip_ref_count2_node}
MOVE_CLIPS[:] = [(r[0], r[1], _OLD_COUNT[r[0]]) + tuple(r[3:]) if r[0] in _OLD_COUNT else r for r in MOVE_CLIPS]
MOVE_CLIP_NODES.update({_nm: {"A": ("ref_kneel", "ref_kneel")} for _nm in _OLD_COUNT})
MOVE_EVENTS.update({
    "Ref_Count_Pin_3_Slow": [("count_%d" % (i + 1), tc, "hand_r", "-") for i, tc in enumerate(SLOW3)],
    "Ref_Count_Pin_2_Slow": [("count_%d" % (i + 1), tc, "hand_r", "-") for i, tc in enumerate(SLOW3[:2])]
                            + [("kickout", SLOW3[1] + 0.2, "-", "-")],
    "Ref_Count_Pin_1_Slow": [("count_1", SLOW3[0], "hand_r", "-"), ("kickout", SLOW3[0] + 0.2, "-", "-")],
    "Ref_Count_Pin_2_Half": [("count_%d" % (i + 1), tc, "hand_r", "-") for i, tc in enumerate(SLOW3[:2])]
                            + [("kickout", HALF_KICK, "-", "-")],
})


# --- «Метеор» (P1-3)
for _nm, _ln, _fn in [("Air_Meteor", AIR_LEN, clip_air_meteor), ("Air_Meteor_Hype", AIR_HYPE_LEN, clip_air_meteor_hype)]:
    MOVE_CLIPS.append((_nm, _ln, _fn, None, False, None, 0))
    CENTER_RULE.add(_nm)
    MOVE_CLIP_NODES[_nm] = {"A": (None, None)}
    MOVE_EVENTS[_nm] = []
MET_CONTACTS = CLIP_CONTACTS["Cover"] | {("thigh_l", "upperarm_l"), ("thigh_r", "upperarm_l")} | {
    (a_, b_) for a_ in ("hand_l", "lowerarm_l") for b_ in ("pelvis", "spine_01", "thigh_l", "thigh_r", "calf_l")}
for _nm, _ln, _fa, _fb in [("Land_Meteor", LAND_LEN, clip_land_meteor_a, clip_land_meteor_b),
                           ("Land_Meteor_Partial", PARTIAL_LEN, clip_land_meteor_partial_a, clip_land_meteor_partial_b),
                           ("Land_Meteor_Miss", MISS_LEN, clip_land_meteor_miss_a, clip_land_meteor_miss_b)]:
    MOVE_CLIPS.append((_nm, _ln, _fa, _fb, False, MISS_OFF.copy() if _nm.endswith("_Miss") else PIN_OFF.copy(),
                       MISS_YAW if _nm.endswith("_Miss") else 90))
    CENTER_RULE.add(_nm)
    MOVE_CONTACTS[_nm] = set(MET_CONTACTS)
MOVE_CONTACTS["Land_Meteor_Partial"] |= {(a_, b_) for a_ in ("spine_03", "spine_02", "upperarm_l", "clavicle_l")
                                         for b_ in ("upperarm_l", "lowerarm_l", "hand_l", "spine_03", "spine_02", "spine_01")}
MOVE_CLIP_NODES.update({
    "Land_Meteor": {"A": (None, "pinned_A"), "B": ("lying_back", "pinned_B")},
    "Land_Meteor_Partial": {"A": (None, "kneel_up"), "B": ("lying_back", "lying_back")},
    "Land_Meteor_Miss": {"A": (None, "kneel_up"), "B": ("lying_back", "lying_front")},
})
MOVE_EVENTS.update({
    "Land_Meteor": [("contact", MET_HIT, "spine_03", "spine_03"), ("sell", MET_HIT + 0.1, "-", "spine_03")],
    "Land_Meteor_Partial": [("contact", MET_HIT_P, "spine_02", "spine_03"), ("sell", MET_HIT_P + 0.1, "-", "spine_03")],
    "Land_Meteor_Miss": [("contact", MET_HIT_M, "spine_03", "-")],
})


# --- «Ластівка» (P2-1, swallow_dive)
MOVE_CLIPS.append(("Air_Swallow", SW_LEN, clip_air_swallow, None, False, None, 0))
CENTER_RULE.add("Air_Swallow")
MOVE_CLIP_NODES["Air_Swallow"] = {"A": (None, None)}
MOVE_EVENTS["Air_Swallow"] = []
for _nm, _ln, _fa, _fb, _off in [("Land_Swallow", LAND_LEN, clip_land_swallow_a, clip_land_swallow_b, PIN_OFF),
                                 ("Land_Swallow_Knees", KNEES_LEN, clip_land_swallow_knees_a, clip_land_swallow_knees_b,
                                  KNEES_OFF),
                                 ("Land_Swallow_Miss", MISS_LEN, clip_land_swallow_miss_a, clip_land_meteor_miss_b,
                                  MISS_OFF)]:
    MOVE_CLIPS.append((_nm, _ln, _fa, _fb, False, _off.copy(), MISS_YAW if _nm.endswith("_Miss") else 90))
    CENTER_RULE.add(_nm)
    MOVE_CONTACTS[_nm] = set(MET_CONTACTS)
MOVE_CONTACTS["Land_Swallow_Knees"] |= {(a_, b_) for a_ in ("spine_01", "spine_02", "spine_03", "pelvis")
                                        for b_ in ("thigh_l", "thigh_r", "calf_l", "calf_r")}
MOVE_CLIP_NODES.update({
    "Land_Swallow": {"A": (None, "pinned_A"), "B": ("lying_back", "pinned_B")},
    "Land_Swallow_Knees": {"A": (None, "kneel_up"), "B": ("lying_back", "lying_back")},
    "Land_Swallow_Miss": {"A": (None, "kneel_up"), "B": ("lying_back", "lying_front")},
})
MOVE_EVENTS.update({
    "Land_Swallow": [("contact", MET_HIT, "spine_03", "spine_03"), ("sell", MET_HIT + 0.1, "-", "spine_03")],
    "Land_Swallow_Knees": [("contact", SW_HIT_K, "spine_01", "calf_l"), ("sell", SW_HIT_K + 0.1, "-", "spine_03")],
    "Land_Swallow_Miss": [("contact", MET_HIT_M, "spine_03", "-")],
})

PAIR_MID_RULE.update({"Land_Meteor", "Land_Meteor_Partial", "Land_Meteor_Miss",
                      "Land_Swallow", "Land_Swallow_Knees", "Land_Swallow_Miss"})


# --- зустрічні з місця (P2-2, whip 10+)
_CNT_HIT = {"line": ("upperarm_r", "lowerarm_r", "hand_r"), "line_l": ("upperarm_l", "lowerarm_l", "hand_l"),
            "block": ("upperarm_l", "spine_03", "clavicle_l"), "boot": ("foot_r", "calf_r", "ball_r")}
_CNT_END = {"back": "lying_back", "back_fast": "lying_back", "flip": "lying_front", "side": None}
for _c in COUNTERS:
    MOVE_CLIPS.append((_c.name, _c.L, counter_a(_c),
                       (lambda fb_: (lambda t, L, partner=None: hands_out_of_torso(fb_(t, L, partner), r=0.19)))(counter_b(_c)),
                       False, _c.b_offset(), 180))
    CENTER_RULE.add(_c.name)
    MOVE_CLIP_NODES[_c.name] = {"A": (None, "stance"), "B": (None, _CNT_END[_c.b])}
    MOVE_EVENTS[_c.name] = [("contact", _c.t_hit, _CNT_HIT[_c.a][0], "spine_03"), ("sell", _c.t_hit + 0.1, "-", "spine_03")]
    MOVE_CONTACTS[_c.name] = {(a_, b_) for a_ in _CNT_HIT[_c.a]
                              for b_ in ("spine_03", "spine_02", "spine_01", "neck_01", "head", "upperarm_l", "upperarm_r",
                                         "clavicle_l", "clavicle_r")}


# --- повернення з канатів (P2-2, whip 7–9)
for _nm, _ln, _fn in (("Rope_Return_Tired", RET_TIRED_LEN, clip_rope_return_tired),
                      ("Rope_Return_Stumble", RET_STUMBLE_LEN, clip_rope_return_stumble)):
    MOVE_CLIPS.append((_nm, _ln, _fn, None, False, None, 0))
    CENTER_RULE.add(_nm)
    MOVE_CLIP_NODES[_nm] = {"A": (None, "stance")}
    MOVE_EVENTS[_nm] = []
