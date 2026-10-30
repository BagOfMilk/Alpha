# Біг від канатів — 15 варіантів (власник 29.10.2026: «біг від канатів … Усіх варіант по 15»).
# Виконується всередині tools/blender/ring_anims.py (exec) після ring_anims_run.py.
#
# Техніка (основи тренування реслінгу, своїми словами): ~3 кроки до канатів; на останньому — розворот боком; верхній
# канат лягає під пахву, середній — на рівні стегна; ближня рука перехоплює верхній канат (або лікоть заводиться за
# нього); коліна амортизують прогин канатів; відштовхування стегном і рукою; ще чверть оберту — і біг назад.
# Канат будує гра і гне фізикою (Game.Core.Ring.RopeSim): кліп дає мітки rope_hit / rope_release.
#
# Стопи — ПЛАНУВАЛЬНИК КРОКІВ: кожна стопа ставиться туди, де в мить приземлення буде тіло (з його поворотом), стоїть
# нерухомо до відриву, між опорами йде дугою з підтягнутою п'ятою. Так розворот і зворотний біг не ковзають.

class Reb:
    # hit — лише "back": власник 30.10.2026 «Бег от канатов всегда спиной»; side — через яке плече розворот перед канатами
    def __init__(self, name, side=1, speed=1.0, sink=0.26, hit="back", grip="hand", ret=True, lean=7.0,
                 crouch=0.0, arm_up=0.0, duck=0.0, heavy=0.0, hesitate=0.0):
        self.__dict__.update({k: v for k, v in locals().items() if k != "self"})

def foot_plan_track(t, falls, lift=0.16, kick=0.25):
    """falls: [(t_land, t_off, Vector точка стопи, поворот носка °)] у порядку часу. Повертає (точка, поворот °)."""
    if t <= falls[0][1]:
        return falls[0][2], falls[0][3]
    for (l0, o0, p0, y0), (l1, o1, p1, y1) in zip(falls, falls[1:]):
        if t <= o0:
            return p0, y0
        if t < l1:                                   # мах між відривом і наступною опорою
            s = (t - o0) / (l1 - o0)
            h = smooth(min(1.0, max(0.0, (s - 0.05) / 0.8)))
            back = math.sin(math.pi * min(1.0, s / 0.7)) if s < 0.7 else 0.0
            pos = p0.lerp(p1, h)
            d = (p1 - p0)
            if d.length > 1e-4:                      # п'ята підтягується назад відносно напрямку кроку
                pos = pos - d.normalized() * 0.12 * back
            pos = Vector((pos.x, pos.y, 0.071 + lift * math.sin(math.pi * s) + kick * back * 0.4))
            return pos, y0 + (y1 - y0) * h
        if t <= o1:
            return p1, y1
    return falls[-1][2], falls[-1][3]

def reb_plan(R):
    """Час, шлях таза й кроки варіанту."""
    k = 1.0 / R.speed
    h = R.hesitate
    D = ROPE_D
    sd = R.side
    # спиною: розворот через ліве плече (side=1) — до −180°, через праве (side=−1) — до +180°. Раніше обидва крутились
    # в один бік, а кроки ставились під бік варіанту — стопа не діставала (Rope_Back_R, 30.10.2026)
    face = {"side": -90.0 * sd, "back": -180.0 * sd, "chest": 0.0}[R.hit]
    ret_face = (180.0 if face > 0 else -180.0) if R.ret else face   # розворот коротшим боком
    rope_y = -(D + 0.12)
    gap = {"side": 0.3, "back": 0.36, "chest": 0.4}[R.hit] + 0.05 * R.crouch          # таз від каната в мить дотику: плече й рука — не за канатом
    T = dict(hit=0.66 * k, sink=0.8 * k, push=(0.94 + h) * k, end=(1.62 + h) * k if R.ret else (1.3 + h) * k)
    path_y = [(0.0, 0.0), (0.14 * k, -0.12), (0.5 * k, -(D - 0.55)), (T["hit"], rope_y + gap),
              (T["sink"], rope_y + gap - R.sink), (T["push"], rope_y + gap)]
    path_y += [(T["push"] + 0.18 * k, rope_y + gap + 0.55), (T["end"], -0.25)] if R.ret else [(T["end"], rope_y + gap + 0.3)]
    # повороти — по 0,3 с: швидше кисті «летять» разом із тілом (і на екрані це ривок)
    # розворот спиною — 0,3 с: стопи розставлені під нього (0,42 с давало ковзання стоп 3 см і ривок стегна)
    turn0 = 0.64 * k - max(0.3 * k, 0.3)        # розворот не коротший за 0,3 с і на швидкому варіанті (кисть летіла 0,7 м за кадр)
    path_yaw = [(0.0, 0.0), (turn0, 0.0), (0.64 * k, face), (T["push"], face)]
    path_yaw += [(T["push"] + 0.3 * k, ret_face), (T["end"], ret_face)] if R.ret else [(T["end"], face * 0.5)]
    legk = 0.92 if globals().get("FEMALE") else 1.0      # коротші ноги жіночого набору — коротші кроки (стопа не діставала)
    def at(tt, lat, ahead, yaw_extra=0.0):
        y = track(tt, path_y)
        yw = track(tt, path_yaw) + yaw_extra
        off = Rz(yw) @ Vector((lat * legk, -ahead * legk, 0.0))
        return Vector((off.x, y + off.y, 0.071)), yw
    # кроки: (нога, приземлення, відрив, бокове зміщення, винос уперед)
    run_steps = [("r", 0.12, 0.22), ("l", 0.27, 0.38), ("r", 0.43, 0.52)]
    falls = {"l": [(-1.0, 0.16 * k, foot(0.2, -0.105), 10.0)], "r": [(-1.0, 0.04 * k, foot(-0.2, 0.075), -14.0)]}
    for leg, tl, to in run_steps:
        p, yw = at(tl * k, 0.11 if leg == "l" else -0.11, 0.22)
        falls[leg].append((tl * k, to * k, p, yw + (6.0 if leg == "l" else -6.0)))
    # розворот: дві стопи ставляться в бічну стійку біля канатів і стоять увесь прогин
    near, far = ("l", "r") if sd > 0 else ("r", "l")
    if R.hit == "side":
        pn, yn = at(T["hit"], 0.0, -0.1 * 0.0, 0.0); pn = pn + Rz(face) @ Vector((0.17 * sd, 0.0, 0.0))
        pf, yf = at(T["hit"], 0.0, 0.0, 0.0); pf = pf + Rz(face) @ Vector((-0.17 * sd, 0.0, 0.0))
    else:
        pn, yn = at(T["hit"], 0.15, 0.0); pf, yf = at(T["hit"], -0.15, 0.0)
    if R.hit == "back" and sd < 0:
        # спиною кінцева поза та сама при будь-якому боці розвороту: ліва стопа — на своєму місці, права — на своєму;
        # від боку залежить лише, яка стає першою (раніше праву ставили на місце лівої — ноги схрещувались, 30.10.2026)
        pn, pf = pf, pn
    falls[near].append((0.56 * k, T["push"] + 0.02 * k, pn, face))
    falls[far].append((0.62 * k, T["push"] + 0.1 * k, pf, face))
    if R.ret:
        for leg, tl, to in (("l" if near == "r" else "r", 0.0, 0.0),):
            pass
        back_steps = [(near, T["push"] + 0.2 * k, T["push"] + 0.3 * k), (far, T["push"] + 0.36 * k, T["push"] + 0.46 * k),
                      (near, T["push"] + 0.52 * k, T["push"] + 0.62 * k), (far, T["end"] - 0.02 * k, T["end"] + 1.0)]
        for leg, tl, to in back_steps:
            lat = 0.11 if leg == "l" else -0.11
            p, yw = at(tl, lat, 0.22)
            falls[leg].append((tl, to, p, yw + (6.0 if leg == "l" else -6.0)))
        falls[near].append((T["end"] + 0.0, T["end"] + 1.0, at(T["end"], 0.11 if near == "l" else -0.11, 0.0)[0], ret_face))
    for leg in "lr":
        falls[leg].sort(key=lambda f: f[0])
    return T, path_y, path_yaw, falls, face, rope_y

REL = 0.62     # с: кисті з каната вперед. За 0,42 с плече оберталось на 85–150° за кадр (ривок, 30.10.2026)

def rebound_pose(t, L, R):
    T, path_y, path_yaw, falls, face, rope_y = reb_plan(R)
    yaw = track(t, path_yaw)
    on = track(t, [(0.0, 0.0), (T["hit"] - (0.4 if R.hit == "back" else 0.28), 0.0), (T["hit"], 1.0), (T["push"], 1.0), (min(T["push"] + REL, L - 0.04), 0.0), (L + 0.01, 0.0)])   # з каната — REL с (не далі кінця кліпу: ключі назад у часі давали стрибок кисті на 38 см)
    run = 1.0 - on
    ret_t = max(0.0, t - T["push"])
    duck = R.duck * smooth(min(1.0, ret_t / 0.2)) if R.ret else 0.0
    p = RPose(forced=set())
    bob = 0.025 * math.cos(2 * math.pi * t / 0.33) * run
    p.pelvis = Vector((0.0, track(t, path_y), -0.07 - bob - 0.14 * on - 0.06 * R.crouch * on - 0.03 * R.heavy - 0.08 * duck))
    p.pelvis_yaw = yaw
    hit_tilt = {"side": -2.0, "back": -8.0, "chest": 10.0}[R.hit]
    p.pitch = (R.lean + 4.0 * R.heavy) * run + hit_tilt * on + 10.0 * duck
    # плече в канати — лише трохи: більший крен виносить голову й шию за канат (власник 29.10.2026, кадр «крізь канат»)
    p.roll = (6.0 * R.side if R.hit == "side" else 0.0) * on
    p.yaw = -6.0 * math.cos(2 * math.pi * t / 0.66) * run
    p.head_pitch = -6.0 + 10.0 * on                                 # біля канатів — підборіддя трохи вниз, не закидати
    # погляд: на розбігу — куди біжить; біля канатів — через плече в центр ринга (суперник, зворотний біг)
    look_c = face - 65.0 * R.side if R.hit == "side" else (face if R.hit == "back" else 0.0)
    p.head_yaw = yaw * (1 - on) + look_c * on
    fl, yl = foot_plan_track(t, falls["l"])
    fr, yr = foot_plan_track(t, falls["r"])
    p.ankles = (fl, fr)
    p.foot_rot = (Quaternion(UP, math.radians(yl)), Quaternion(UP, math.radians(yr)))
    p.knees = (fl + Rz(yl) @ Vector((0.08, -0.9, 0.45)), fr + Rz(yr) @ Vector((-0.08, -0.9, 0.45)))
    pump = math.sin(2 * math.pi * t / 0.66) * run
    near = "l" if R.side > 0 else "r"
    def hand(s_):
        sg = 1 if s_ == "l" else -1
        def g(S):
            # не далі 0,88 довжини руки від плеча: тіло відштовхується від канатів, а точка хвату лишається позаду —
            # рука випрямлялась до кінця й ривком згиналась (30.10.2026)
            pt = g0(S)
            reach = 0.44 if globals().get("FEMALE") else 0.46
            d = pt - S[s_]
            return S[s_] + d.normalized() * reach if d.length > reach else pt
        def g0(S):
            free = S[s_] + Rz(yaw) @ Vector((0.14 * sg, -0.06 - 0.24 * pump * sg, -0.28 + 0.08 * max(0.0, pump * sg)))
            if not R.ret and t > T["push"]:            # зупинка біля канатів: руки опускаються вздовж тіла, не біг
                free = S[s_] + Rz(yaw) @ Vector((0.2 * sg, 0.03, -0.42))
            grab = (s_ == near) if R.hit == "side" else True
            if not grab:
                # вільна рука: розслаблено вниз і трохи назад (не на груди), або вгору — до публіки
                tgt = S[s_] + Rz(yaw) @ (Vector((0.1 * sg, -0.12, 0.42)) if R.arm_up else Vector((0.18 * sg, -0.06, -0.34)))
                return lerp(free, tgt, on)
            hz = ROPE_H[2] - (0.02 if R.grip == "hand" else 0.14)
            side_off = Rz(yaw) @ Vector((0.18 * sg, 0.0, 0.0)) if R.hit != "side" else Vector()
            # хват — на канаті попереду тіла (уздовж каната, куди дивиться борець), не біля самого плеча
            fwd_w = Rz(yaw) @ Vector((0.0, -1.0, 0.0))
            along = fwd_w.x * ((0.42 if R.grip == "elbow" else 0.28) if R.hit == "side" else 0.0) * (0.75 if globals().get("FEMALE") else 1.0)
            # точка хвату — у кінцевій позі (спиною до канатів, курс face), а не від плеча в поточному розвороті:
            # на розвороті плече перетинає середню лінію, і ціль правої кисті стрибала до грудей (кадр 16, 30.10.2026)
            span = (0.38 if not globals().get("FEMALE") else 0.33) * sg
            rope = Vector(((Rz(face) @ Vector((span, 0.0, 0.0))).x + along, rope_y + 0.04, hz))
            # шлях: перед тілом → збоку від свого плеча → канат позаду. Пряма «перед → позаду» на розвороті різала
            # груди на 12–17 см (перевірка рук у грудях, 30.10.2026: «руки из плечей растут»)
            arm_k = 0.8 if globals().get("FEMALE") else 1.0
            side_pt = S[s_] + Rz(yaw) @ Vector((0.4 * sg * arm_k, 0.05, -0.14))     # збоку від плеча (ближче — лікоть у ребрах)
            # до каната — лише коли спина вже біля нього (on > 0,75): раніше точка на канаті була за 0,5 м позаду, рука
            # випрямлялась до кінця й знову згиналась — лікоть ривком 50–60° за кадр (30.10.2026)
            if t > T["push"]:                          # відпускання: канат → бік (пів часу) → біг, без поспіху
                u = 1.0 - on
                if u < 0.5:
                    return lerp(rope, side_pt, smooth(u / 0.5))
                return lerp(side_pt, free, smooth((u - 0.5) / 0.5))
            if on < 0.45:
                u = smooth(on / 0.45)
                return lerp(free, side_pt, u) + Rz(yaw) @ Vector((0.06 * sg, 0.0, 0.04)) * math.sin(math.pi * u)
            # до каната — коли спина вже біля нього І розворот майже закінчено (інакше точка на канаті ще збоку тіла
            # й кисть перескакувала через півтіла за кадр, 30.10.2026)
            # хват за канат — за 0,15 с ПІСЛЯ розвороту (так і в записах матчів: руки ловлять канат уже в прогині)
            t_turn = 0.64 / R.speed
            u = smooth(max(0.0, min(1.0, (t - t_turn) / 0.15)))
            return lerp(side_pt, rope, u)
        return g
    p.hands = {s_: (hand(s_), FWD.lerp(DOWN, on), LEFT if s_ == "l" else RIGHT) for s_ in "lr"}
    def elbow(s_):
        hf = hand(s_)
        return lambda S: perp_pole(S[s_], hf(S), yaw, 1 if s_ == "l" else -1, (R.name, s_), t)
    p.elbows = {s_: elbow(s_) for s_ in "lr"}
    p.fist = {"l": 0.55, "r": 0.55}
    return p

REBS = [                                       # усі — спиною до канатів (власник 30.10.2026)
    Reb("Rope_Back_L"),                                     # розворот через ліве плече
    Reb("Rope_Back_R", side=-1),                            # через праве
    Reb("Rope_Fast", speed=1.2, sink=0.3),
    Reb("Rope_Slow_Tired", speed=0.82, sink=0.2, lean=13.0),
    Reb("Rope_Deep", sink=0.34, crouch=1.0),
    Reb("Rope_Light", sink=0.15),
    Reb("Rope_Back_Low", crouch=0.6),
    Reb("Rope_Elbow_Hook", grip="elbow"),
    Reb("Rope_Stop_Lean", ret=False),
    Reb("Rope_Duck_Return", duck=1.0),
    Reb("Rope_Roar", arm_up=1.0),
    Reb("Rope_Heavy", heavy=1.0, speed=0.9, sink=0.32),
    Reb("Rope_Back_Stop", ret=False, sink=0.2, crouch=0.4),
    Reb("Rope_Hesitate", hesitate=0.25),
    Reb("Rope_Back_R_Fast", side=-1, speed=1.2, sink=0.3),
]

def reb_len(R):
    return reb_plan(R)[0]["end"]

MOVE_CLIPS += [(r.name, reb_len(r), (lambda t, L, r=r: rebound_pose(t, L, r)), None, False, None, 0) for r in REBS]
MOVE_EVENTS.update({r.name: [("rope_hit", reb_plan(r)[0]["hit"], "spine_02", "rope2:%.2f" % r.sink),
                             ("rope_hit", reb_plan(r)[0]["hit"] + 0.02, "spine_03", "rope3:%.2f" % (r.sink * 0.8)),
                             ("rope_release", reb_plan(r)[0]["push"], "-", "-")] for r in REBS})


# ---------------------------------------------------------------- збірка: розбіг і біг назад — з перевіреного циклу бігу
# (власник 29.10.2026: на відштовхуванні рука викручувалась, ноги перехрещувались на зворотному бігу). Розбіг і біг
# назад — run_pose (ring_anims_run.py), відскок — rebound_pose; між ними — короткі змішування.

def run_at(tau, origin_y, yaw, R):
    p = run_pose(tau, R)
    Rm = Rz(yaw)
    off = Vector((0.0, origin_y, 0.0))
    ankles = tuple(off + Rm @ a for a in p.ankles)
    knees = tuple(off + Rm @ k for k in p.knees)
    rots = tuple(Quaternion(UP, math.radians(yaw)) @ q for q in p.foot_rot)
    orient(p, (0.0, origin_y), yaw)
    p.ankles, p.knees, p.foot_rot = ankles, knees, rots
    return p

def _warp(x):
    x = max(0.0, x)
    return x * x * (2.0 - x) if x < 1.0 else x       # розгін з нуля, на x=1 — рівна швидкість

def body_blend_arms(a, c, k, yaw_m, key=None, t=None):
    """Цілі кистей і полюси ліктів двох поз (a — біг, c — відскок), змішані в системі тіла (відносно плеча, без курсу
    кожної пози) і повернуті в поточний курс yaw_m. Лікоть: у бігу — свій полюс бігу (там лікоть не в грудях), у
    відскоку — perp_pole (без перевертань), між ними — змішування точок полюса в системі тіла."""
    ev = lambda v, S: v(S) if callable(v) else v
    ya, yc = a.pelvis_yaw or 0.0, c.pelvis_yaw or 0.0
    ah, ch, ae = dict(a.hands), dict(c.hands), dict(a.elbows)     # знімок: результат може замінити a.hands
    def loc(v, S, s_, y):
        return Rz(-y) @ (v - S[s_])
    def hand(s_):
        def g(S):
            la = loc(ev(ah[s_][0], S), S, s_, ya); lc = loc(ev(ch[s_][0], S), S, s_, yc)
            return S[s_] + Rz(yaw_m) @ lerp(la, lc, k)
        def d(i):
            return lambda S: Rz(yaw_m) @ lerp(Rz(-ya) @ ev(ah[s_][i], S), Rz(-yc) @ ev(ch[s_][i], S), k)
        return (g, d(1), d(2))
    hs = {s_: hand(s_) for s_ in "lr"}
    def elbow(s_):
        def e(S):
            pc = perp_pole(S[s_], hs[s_][0](S), yaw_m, 1 if s_ == "l" else -1, key and (key, s_), t)
            if k >= 1.0:
                return pc
            la = loc(ev(ae[s_], S), S, s_, ya)
            return S[s_] + Rz(yaw_m) @ lerp(la, Rz(-yaw_m) @ (pc - S[s_]), k)
        return e
    return hs, {s_: elbow(s_) for s_ in "lr"}


POLE_DIR = (0.15, 0.6, -1.0)   # бажаний бік ліктя в системі тіла на старті кліпу: донизу, трохи назад і назовні
_POLE_MEM = {}                 # (кліп, бік) -> (час, напрям ліктя в системі тіла) — неперервність між кадрами


def perp_pole(sh, wr, yaw, sg, key=None, t=None):
    """Полюс ліктя — перпендикулярно до лінії плече—кисть, у бік, найближчий до напряму ліктя попереднього кадру
    (key, t). Будь-який нерухомий напрям рано чи пізно лягає на лінію руки (кисть на розвороті проходить спереду,
    збоку й ззаду) — IK перевертав плече на 90–176° за кадр (30.10.2026)."""
    Rm = Rz(yaw)
    want = Rm @ Vector((POLE_DIR[0] * sg, POLE_DIR[1], POLE_DIR[2]))
    if key is not None and key in _POLE_MEM:
        t0, d0 = _POLE_MEM[key]
        if t is not None and 0.0 <= t - t0 <= 0.06:
            # тягнеться до попереднього, але повертається до бажаного боку: без цього полюс дрейфував у груди
            # (лікоть у тулубі на 8–12 см наприкінці кліпу, 30.10.2026)
            want = Rm @ (d0 * 0.75 + Vector((POLE_DIR[0] * sg, POLE_DIR[1], POLE_DIR[2])).normalized() * 0.25)
    ax = wr - sh
    if ax.length < 1e-4:
        return sh + want.normalized() * 0.4
    axn = ax.normalized()
    perp = want - axn * want.dot(axn)
    if perp.length < 1e-3:
        perp = Rm @ Vector((sg, 0.0, 0.0)) - axn * (Rm @ Vector((sg, 0.0, 0.0))).dot(axn)
    perp.normalize()
    if key is not None and t is not None:
        _POLE_MEM[key] = (t, Rz(-yaw) @ perp)
    return sh + ax * 0.5 + perp * 0.4

def rebound_full(t, L, R):
    T, path_y, path_yaw, falls, face, rope_y = reb_plan(R)
    RL = RUN_LOOPS["Run_Heavy_Loop" if R.heavy else ("Run_Tired_Loop" if R.lean > 10 else "Run_Loop")]
    v = RL.stride / RL.L
    contact_y = track(T["hit"], path_y)
    tau_hit = abs(contact_y) / v
    hit, push = T["hit"], T["push"]
    # тіло й стопи — до бігу за 0,3 с (довше — стопи ковзали); руки — за REL (швидше — плече крутилось ривком)
    wc = track(t, [(0.0, 0.0), (hit - 0.28, 0.0), (hit, 1.0), (push, 1.0), (push + 0.3 / R.speed, 0.0), (L + 1.0, 0.0)])
    wa = track(t, [(0.0, 0.0), (hit - 0.28, 0.0), (hit, 1.0), (push, 1.0), (min(push + REL, L - 0.04), 0.0), (L + 1.0, 0.0)])
    c = rebound_pose(t, L, R)
    if t < push:
        run = run_at(tau_hit * _warp(t / hit) if t < hit else tau_hit + v * 0 + (t - hit) * 0.0, 0.0, 0.0, RL)
    elif not R.ret:
        return c
    else:
        tr = (t - push) / 0.25
        run = run_at(0.25 * _warp(tr) if tr < 1 else 0.25 * (tr - 0.5) + 0.125, contact_y, (180.0 if R.side < 0 else -180.0), RL)   # −180, не 180: змішування кутів з фазою відскоку (−90…−180) без перекруту
    if wc >= 1.0 and wa >= 1.0:
        return c
    if wc <= 0.0 and wa <= 0.0:
        return run
    m = blend_pose(run, c, wc) if wc > 0.0 else run
    # руки змішуються в системі тіла, а не у світі: цілі бігу задані для курсу 0°, тіло вже повернуте на
    # −60…−130°, і середнє двох світових цілей лягало в груди (30.10.2026)
    m.hands, m.elbows = body_blend_arms(run, c, wa, m.pelvis_yaw, R.name, t)
    return m

MOVE_CLIPS[:] = [c if not any(c[0] == r.name for r in REBS) else
                 (c[0], c[1], (lambda t, L, r=next(r for r in REBS if r.name == c[0]): rebound_full(t, L, r)), None, False, None, 0)
                 for c in MOVE_CLIPS]

# перевірка «крізь канат» у генераторі: лінія каната й прогин у часі для кожного варіанту
def _sink_fn(R):
    T = reb_plan(R)[0]
    if not R.ret:                                         # тіло лишається на канатах — прогин тримається
        return lambda tt: track(tt, [(0.0, 0.0), (T["hit"], 0.0), (T["sink"], R.sink), (99.0, R.sink * 0.8)])
    return lambda tt: track(tt, [(0.0, 0.0), (T["hit"], 0.0), (T["sink"], R.sink), (T["push"], R.sink * 0.6),
                                 (T["push"] + 0.15, 0.0), (99.0, 0.0)])
ROPE_CHECK = {r.name: (-(ROPE_D + 0.12), _sink_fn(r)) for r in REBS}


# ---------------------------------------------------------------- замок стоп (foot lock), загальний
# На стиках змішувань стопа на маті повзе (змішуються дві різні точки опори). Замок: стопа, що стоїть на маті,
# тримає точку, де стала; коли відривається — ціль повертається до кліпу за 3 кадри. Стан — по кліпу, кадри йдуть
# підряд (bake_clip).
def foot_locked(fn, ground=0.096, release=3.0 / 30.0):
    st = {"t": None}
    def g(t, L, *a):
        p = fn(t, L, *a)
        if st["t"] is None or t <= 1e-6 or t < st["t"]:
            st.update(lock=[None, None], free=[0.0, 0.0], step=[None, None], lock_prev=[None, None])
        st["t"] = t
        out = list(p.ankles)
        for i in (0, 1):
            a_ = p.ankles[i]
            side = "l" if i == 0 else "r"
            hip = REST["hip_" + side] + p.pelvis
            leg = (REST["hip_" + side] - REST["ankle_" + side]).length * 0.98
            if st["lock"][i] is not None and (Vector((st["lock"][i].x, st["lock"][i].y, 0.071)) - hip).length > leg * 0.92:
                st["lock"][i] = None                              # нога не дістає — стопа переступає (крок)
                st["step"] = st.get("step", [None, None])
                lp = st["lock_prev"][i] if st.get("lock_prev") and st["lock_prev"][i] else a_
                start = hip + (Vector((lp.x, lp.y, 0.071)) - hip).normalized() * leg   # з досяжної точки
                st["step"][i] = (t, start)
            stp = st.get("step", [None, None])[i]
            if stp is not None:                                   # короткий крок: дуга до точки кліпу за 0,2 с
                k = min(1.0, (t - stp[0]) / 0.2)
                pos = stp[1].lerp(a_, smooth(k))
                pos.z = max(a_.z, (0.1 + 0.06 * math.sin(math.pi * k)) if k < 0.97 else a_.z)   # відрив одразу
                out[i] = pos
                if k >= 1.0:
                    st["step"][i] = None
                continue
            if a_.z < ground:
                if st["lock"][i] is None:
                    st["lock"][i] = a_.copy()
                lk = st["lock"][i]
                out[i] = Vector((lk.x, lk.y, a_.z))
                st["free"][i] = t
                st.setdefault("lock_prev", [None, None])[i] = lk
            else:
                lk = st["lock"][i]
                if lk is not None:
                    k = min(1.0, (t - st["free"][i]) / release)
                    out[i] = Vector((lk.x, lk.y, a_.z)).lerp(a_, smooth(k))
                    if k >= 1.0:
                        st["lock"][i] = None
        if p.knees:
            p.knees = (p.knees[0] + (out[0] - p.ankles[0]), p.knees[1] + (out[1] - p.ankles[1]))
        p.ankles = tuple(out)
        return p
    return g

MOVE_CLIPS[:] = [c if not any(c[0] == r.name for r in REBS) else
                 (c[0], c[1], foot_locked(lambda t, L, r=next(r for r in REBS if r.name == c[0]): rebound_full(t, L, r)),
                  None, False, None, 0) for c in MOVE_CLIPS]
