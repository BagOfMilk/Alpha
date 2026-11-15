# Живе очікування (власник 14.11.2026: «добавь более живие айдл анимации когда просто стояние происходит, что бы чуть
# живее выглядело»; ПМ того ж дня — так, «з stance у stance»). Виконується всередині ring_anims.py (exec) після
# ring_anims_move.py — бере stance, ref_stand, clip_ref_idle, steps, track, smooth, lerp, hit_spring, STANCE_FOOT.
#
# Будова: вставка = петля стійки (та сама фаза, що в Ring_Stance_Loop / Ref_Idle_Loop) + шар жесту. Довжина — рівно
# один період петлі (2,4 с у борця, 3,2 с у рефері), тож на обох краях поза й швидкість збігаються з петлею: гра
# вставляє її на межі петлі й повертається в петлю без змішування (RingIdleLife, режисер дає «вільне вікно»).
# Закони (docs/ring/…, пам'ять «animation-principles»): передчуття перед жестом, дуги кистей, розгін і гальмування,
# перехльост на виході, вторинна дія (голова й тулуб відстають від рук на 0,1–0,15 с), асиметрія, стопи стоять.
# Жести — загальні рухи борця між діями (розім'ятись, глянути на зал, витерти піт), без чужих фірмових поз.
#
# Огляд 15.11.2026 (чоловічий набір, аркуші по 12 кадрів і виміри зібраного FBX) — що виправлено:
#   * підстрибок: стопа йшла носком УГОРУ, обидві стопи зависали над матом на 7 см — тепер п'ята вгору, подушечка на маті;
#   * витирання поту: зап'ястя виходило на 5 см ближче до обличчя, ніж ціль долоні, і передпліччя проходило крізь голову
#     (8 см за капсулами) — рука йде в площині перед обличчям, лікоть попереду плеча;
#   * шия: «коло» з повороту й кивка читалось як роззирання — тепер нахил вухом до плеча (поле head_roll, див. нижче);
#   * «ну давай» і манжет рефері: рух без зламів швидкості, лікоть рефері зігнутий.

IDLE_L = 2.4            # = Ring_Stance_Loop
REF_IDLE_L = 3.2        # = Ref_Idle_Loop


def env(t, a, b, c, d):
    """0 до a, розгін до 1 на b, тримає до c, гальмує до 0 на d."""
    if t <= a or t >= d:
        return 0.0
    if t < b:
        return smooth((t - a) / (b - a))
    if t <= c:
        return 1.0
    return 1.0 - smooth((t - c) / (d - c))


def _wave(t, t0, period, n):
    """n плавних хвиль 0→1→0 від t0 з періодом period: швидкість нульова на початку й кінці кожної (без зламів)."""
    if t <= t0 or t >= t0 + period * n:
        return 0.0
    return 0.5 - 0.5 * math.cos(2 * math.pi * (t - t0) / period)


# Нахил голови до плеча. У Pose генератора є лише head_yaw і head_pitch; поле head_roll (°, + — ліворуч, як roll тулуба)
# читає обгортка: після розв'язання кадру докручує neck_01 і head навколо осі «вперед». Пози без head_roll не зачіпаються,
# файл генератора (зона Кліпів-1) не змінюється. Той самий прийом — у ring_anims_promo.py (finger_open).
if "_idle_apply_w" not in globals():
    _idle_apply_w = apply_w

    def apply_w(rig, p, partner=None):
        _idle_apply_w(rig, p, partner)
        hr = getattr(p, "head_roll", 0.0)
        toes = getattr(p, "toe_bend", None) or {}
        aim = getattr(p, "hand_aim", None) or {}
        flap = getattr(p, "hand_flap", None) or {}
        if abs(hr) < 1e-6 and not toes and not aim and not flap:
            return
        arm, pb = rig.arm, rig.arm.pose.bones
        # hand_aim = {"r": (вісь кисті, напрям великого пальця, вага 0..1)}: кисть повертається В ЗАП'ЯСТІ до заданої рамки
        # (у просторі ригу), а не продовжує лінію передпліччя. Вага 0 — як було, тож вхід і вихід без стрибка. Скручування
        # при запіканні переносить у передпліччя ядро (move_twist_to_forearm), залишок міряє його ж мірка «кисть».
        # hand_flap = {"l": кут}: кисть «бовтається» в зап'ясті — згин навколо лінії кісточок (струшування рук).
        for s, deg in flap.items():
            hb = pb["hand_" + s]
            b = arm.data.bones
            line = (b["pinky_01_" + s].head_local - b["index_01_" + s].head_local).normalized()
            ax = (b["hand_" + s].matrix_local.to_3x3().inverted() @ line).normalized()
            hb.rotation_quaternion = hb.rotation_quaternion @ Quaternion(ax, math.radians(deg))
        if flap:
            bpy.context.view_layer.update()
        for s, (fore, up, w) in aim.items():
            if w <= 1e-4:
                continue
            hb = pb["hand_" + s]
            f1, u1 = ortho(fore, up)
            q_t = hand_rotation(rig, s, f1, u1)
            m = hb.matrix.copy()
            q_c = m.to_quaternion()
            if q_c.dot(q_t) < 0.0:
                q_t.negate()
            new = q_c.slerp(q_t, min(1.0, w)).to_matrix().to_4x4()
            new.translation = m.translation
            hb.matrix = new
        if aim:
            bpy.context.view_layer.update()
        if abs(hr) >= 1e-6:
            for bone, w in (("neck_01", 0.45), ("head", 0.55)):
                if bone in pb:
                    ax = (rest(arm, bone).to_3x3().inverted() @ FWD).normalized()
                    pb[bone].rotation_quaternion = pb[bone].rotation_quaternion @ Quaternion(ax, math.radians(-hr * w))
        # toe_bend = {"l": кут, "r": кут}: пальці стопи розгинаються на кут підйому п'яти й лишаються плазом на маті
        # (без цього кінчики пальців ідуть під мат на ~2 см, коли стопа стає на носок).
        for s, deg in toes.items():
            bone = "ball_" + s
            if bone in pb and abs(deg) > 1e-6:
                ax = (rest(arm, bone).to_3x3().inverted() @ LEFT).normalized()
                pb[bone].rotation_quaternion = Quaternion(ax, math.radians(-deg))
        bpy.context.view_layer.update()


def _guard(side):
    return (lambda S: S["l"] + Vector((0.04, -0.34, -0.14))) if side == "l" else (lambda S: S["r"] + Vector((-0.04, -0.34, -0.14)))


def _elbow(side):
    return (lambda S: S["l"] + Vector((0.2, 0.05, -0.45))) if side == "l" else (lambda S: S["r"] + Vector((-0.2, 0.05, -0.45)))


# (напрям передпліччя → кисть, напрям великого пальця) у блоці стійки. Кисть без хвату йде лінією передпліччя (forced
# порожній), тож ці осі лише ставлять зап'ястя: воно на 5,2 см позаду центру долоні вздовж передпліччя.
_HAND_DIR = {"l": (FWD + UP * 0.6, RIGHT * 0.5 + FWD * 0.2), "r": (FWD + UP * 0.6, LEFT * 0.5 + FWD * 0.2)}


def _hand(side, fn):
    return (fn,) + _HAND_DIR[side]


def _path(t, keys):
    """Кисть за ключами [(час, функція S → точка)]: точки рахуються від поточних кісток, між ними — сплайн без зупинок."""
    return lambda S: track(t, [(tt, f(S)) for tt, f in keys])


_BALL_FWD, _BALL_DROP = 0.126, 0.063     # щиколотка → подушечка стопи: уперед уздовж стопи й униз (м); заміряно в зібраному FBX


def _tiptoe(side, a_deg):
    """Стати на носок: п'ята піднята на a_deg, подушечка стопи лишається на маті на своєму місці — щиколотка йде вгору й
    уперед, до носка. Повертає (щиколотка, поворот стопи). Знак — як у tip навколішках (Quaternion(RIGHT, −кут): носок
    униз). Зсув щиколотки вперед узято 0,8 від геометричного: мірка «ковзання» генератора рахує горизонтальний хід самої
    щиколотки, поки вона нижче 9,5 см, і повний зсув дав би хибні 1,7 см при порозі 1,5; подушечка через це їде назад
    не більше ніж на 0,7 см на вершині підйому."""
    a = math.radians(a_deg)
    f = _BALL_FWD * math.cos(a) - _BALL_DROP * math.sin(a)             # подушечка попереду щиколотки
    d = _BALL_FWD * math.sin(a) + _BALL_DROP * math.cos(a)             # … і нижче за неї
    yaw = Quaternion(UP, math.radians(STANCE_FOOT_YAW[side]))
    at = STANCE_FOOT[side] + yaw @ Vector((0.0, -0.8 * (_BALL_FWD - f), 0.0)) + Vector((0.0, 0.0, d - _BALL_DROP))
    return at, yaw @ Quaternion(LEFT, math.radians(a_deg))


# ---------------------------------------------------------------- борець

def clip_life_shake(t, L):
    """Струсити руки: опускає їх перед собою (лікті зігнуті, кисті на рівні пояса, попереду стегон) і трясе розслабленими
    кистями — кисті бовтаються в зап'ястях, ліва й права не в такт, лікті підстрибують; плечі по колу назад, голова за
    руками; повертає блок із перехльостом. Прийом ПМ 15.11.2026: у першій редакції руки сім кадрів із дванадцяти просто
    висіли вздовж тіла — тепер рух у самих кистях, а утримання коротше."""
    p = stance(t, IDLE_L)
    w = env(t, 0.15, 0.45, 1.45, 2.0)
    shake = env(t, 0.42, 0.55, 1.35, 1.55)
    f = 2 * math.pi * 4.6 * t                                            # 4,6 Гц: кисть ±40° у зап'ясті, передпліччя ±2 см
    roll = env(t, 0.25, 0.55, 0.95, 1.25)
    ov = hit_spring(t, 1.9, w=13.0, z=0.45) * 0.02                        # перехльост рук на поверненні в блок
    flap = {}
    for sd, sx in (("l", 1.0), ("r", -1.0)):
        g, e = _guard(sd), _elbow(sd)
        ph = 0.0 if sd == "l" else 1.9
        low = (lambda S, sx=sx, sd=sd: S[sd] + Vector((0.09 * sx, -0.24, -0.34)))     # перед стегном, на рівні пояса
        def hand(S, g=g, low=low, sx=sx, ph=ph):
            j = Vector((0.012 * sx * math.sin(f + ph), 0.0, 0.022 * math.sin(f + ph + 1.2))) * shake
            return lerp(g(S), low(S), w) + j + Vector((0, 0, ov))
        p.hands[sd] = (hand, lerp(FWD + UP * 0.6, FWD * 0.6 + DOWN, w), _HAND_DIR[sd][1])
        p.elbows[sd] = (lambda S, e=e, sx=sx, ph=ph: e(S) + Vector((0.06 * sx, 0.14 * roll, 0.1 * roll + 0.03 * shake * math.sin(f + ph))))
        flap[sd] = 40.0 * shake * math.sin(f + ph)
    p.hand_flap = flap
    p.pitch = p.pitch - 5.0 * w - 4.0 * roll
    p.pelvis = p.pelvis + Vector((0.0, 0.0, 0.02 * w + 0.006 * shake * math.sin(2 * f)))
    p.head_pitch = p.head_pitch + 12.0 * env(t, 0.5, 0.75, 1.3, 1.6) - 5.0 * roll
    p.head_yaw = -8.0 * env(t, 0.55, 0.85, 1.25, 1.6)
    p.fist = {"l": 0.25 - 0.12 * w, "r": 0.25 - 0.12 * w}
    return p


def _life_look(side):
    """Глянути на зал (side +1 — ліворуч): спершу очі й голова, за ними плечі; погляд ковзає трибуною й повертається."""
    def f(t, L):
        p = stance(t, IDLE_L)
        look = track(t, [(0.0, 0.0), (0.3, 0.0), (0.68, 1.0), (1.5, 1.0), (1.92, 0.0), (2.4, 0.0)])
        body = track(t, [(0.0, 0.0), (0.42, 0.0), (0.85, 1.0), (1.55, 1.0), (2.05, 0.0), (2.4, 0.0)])
        scan = math.sin(2 * math.pi * (t - 0.68) / 0.82) * env(t, 0.68, 0.8, 1.4, 1.5)
        p.head_yaw = side * (52.0 * look + 9.0 * scan)
        p.yaw = p.yaw + side * 14.0 * body
        # Погляд рівний, на трибуни, а не на лампи: стійка вже тримає підборіддя піднятим (тулуб нахилено вперед), тож тут
        # голову ледь опускаємо (прийом ПМ 15.11.2026: «дивиться на лампи»).
        p.head_pitch = p.head_pitch + 5.0 * look
        p.pelvis = p.pelvis + Vector((0.02 * side * body, 0.0, 0.0))
        for sd in "lr":
            g = _guard(sd)
            p.hands[sd] = _hand(sd, lambda S, g=g: g(S) + Vector((0.0, 0.04 * body, -0.06 * body)))
        p.fist = {"l": 0.25 - 0.1 * body, "r": 0.25 - 0.1 * body}
        return p
    return f


def clip_life_bounce(t, L):
    """Підстрибнути на носках: присід-передчуття, три пружні підйоми на носки (подушечки стоп не відриваються від мату),
    четвертий — згасаючий; коліна пружинять на кожному приземленні, розслаблені руки струшуються в такт."""
    p = stance(t, IDLE_L)
    b = env(t, 0.2, 0.45, 1.85, 2.2)
    ph = 2 * math.pi * (t - 0.45) / 0.47
    up = b * (0.5 - 0.5 * math.cos(ph)) if t > 0.45 else 0.0              # 0 — п'яти на маті, 1 — на носках
    al, ql = _tiptoe("l", 24.0 * up)
    ar, qr = _tiptoe("r", 24.0 * up)
    p.ankles = (al, ar)
    p.foot_rot = (ql, qr)
    p.toe_bend = {"l": 24.0 * up, "r": 24.0 * up}
    rise = al.z - STANCE_FOOT["l"].z
    p.pelvis = p.pelvis + Vector((0.0, -0.012 * up, 0.92 * rise - 0.014 * b * (1.0 - up)))
    p.pitch = p.pitch - 3.0 * b + 1.5 * up
    p.head_pitch = p.head_pitch - 2.0 * up
    for sd, sx, phase in (("l", 1.0, 0.0), ("r", -1.0, math.pi)):
        g = _guard(sd)
        p.hands[sd] = _hand(sd, lambda S, g=g, sx=sx, phase=phase: g(S) + Vector(
            (0.02 * sx * math.sin(ph + phase) * b, 0.02 * b, -0.07 * b - 0.03 * up + 0.02 * math.sin(ph * 2 + phase) * b)))
    p.fist = {"l": 0.25 - 0.15 * b, "r": 0.25 - 0.15 * b}
    return p


def clip_life_neck(t, L):
    """Розім'яти шию: вухо до лівого плеча — через підборіддя вниз — до правого плеча, там короткий «хрускіт» із відскоком.
    Плече з боку нахилу ледь піднімається назустріч, протилежне тягнеться вниз; тулуб ледь іде за головою."""
    p = stance(t, IDLE_L)
    tilt = track(t, [(0.0, 0.0), (0.2, 0.0), (0.58, 1.0), (0.84, 1.0), (1.3, -1.0), (1.52, -1.0), (2.0, 0.0), (2.4, 0.0)])
    down = env(t, 0.84, 1.04, 1.1, 1.3)                                    # між боками — підборіддя до грудей
    crack = hit_spring(t, 1.4, w=18.0, z=0.45)
    p.head_roll = 24.0 * tilt - 6.0 * crack
    p.head_yaw = 8.0 * tilt
    p.head_pitch = p.head_pitch + 5.0 * tilt * tilt + 16.0 * down
    p.roll = p.roll + 2.0 * tilt
    for sd, sx in (("l", 1.0), ("r", -1.0)):
        g, e = _guard(sd), _elbow(sd)
        lift = max(0.0, tilt * sx)                                         # плече з боку нахилу
        drop = max(0.0, -tilt * sx)
        p.elbows[sd] = (lambda S, e=e, lift=lift, drop=drop: e(S) + Vector((0.0, 0.0, 0.05 * lift - 0.03 * drop)))
        p.hands[sd] = _hand(sd, lambda S, g=g, lift=lift, drop=drop: g(S) + Vector((0.0, 0.0, 0.02 * lift - 0.03 * drop)))
    return p


def clip_life_wipe(t, L):
    """Витерти піт із чола тильним боком кисті. Права рука піднімається перед правим плечем (не перед ротом), кисть лягає
    на чоло тильним боком — пальці ліворуч, уздовж чола, великий палець донизу — і ковзає від середини чола до правої
    скроні; голова схиляється назустріч. Потім кисть струшує піт убік-униз і повертається в блок.
    Карта правої руки у фазі витирання: лікоть — попереду й правіше плеча, на рівні підборіддя (полюс уперед-убік);
    передпліччя піднімається до правої скроні; кисть зігнута в зап'ясті й лежить уздовж чола, а не продовжує передпліччя
    (hand_aim). Прийом ПМ 15.11.2026: у попередній редакції кисть ішла перед ротом («кашляє в кулак») і лягала «кігтем» на
    маківку з високим ліктем («чухає голову»)."""
    p = stance(t, IDLE_L)
    g = _guard("r")
    rise = lambda S: S["r"] + Vector((-0.06, -0.33, 0.04))                # підйом перед правим плечем, збоку від обличчя
    brow_c = lambda S: S["head"] + Vector((0.035, -0.205, 0.125))         # центр долоні над лівою бровою: кисть накриває чоло
    brow_r = lambda S: S["head"] + Vector((-0.085, -0.175, 0.12))         # … зап'ястя вже біля правої скроні
    out_a = lambda S: S["r"] + Vector((-0.24, -0.18, -0.30))
    out_b = lambda S: S["r"] + Vector((-0.22, -0.20, -0.36))
    keys = [(0.0, g), (0.2, g), (0.46, rise), (0.68, brow_c), (1.06, brow_r), (1.4, out_a), (1.56, out_b), (2.02, g), (2.4, g)]
    wipe = env(t, 0.44, 0.68, 1.06, 1.28)
    flick = env(t, 1.1, 1.4, 1.58, 1.98)
    rest_ = max(0.0, 1.0 - wipe - flick)
    along = LEFT + UP * 0.1 + BACK * 0.12                                 # вісь кисті на чолі: ліворуч, уздовж чола
    fore = (FWD + UP * 0.6) * rest_ + along * wipe + (DOWN + RIGHT * 0.35 + FWD * 0.3) * flick
    thumb = (LEFT * 0.5 + FWD * 0.2) * rest_ + (DOWN + FWD * 0.15) * wipe + (FWD + UP * 0.3) * flick
    p.hands["r"] = (_path(t, keys), fore, thumb)
    p.hand_aim = {"r": (along, DOWN + FWD * 0.15, wipe)}
    e = _elbow("r")
    p.elbows["r"] = (lambda S: lerp(e(S), S["r"] + Vector((-0.50, -0.45, -0.22)), wipe))
    bow = env(t, 0.4, 0.68, 1.04, 1.26)
    p.head_pitch = p.head_pitch + 16.0 * bow - 4.0 * env(t, 1.26, 1.4, 1.56, 1.8)
    p.head_yaw = 6.0 * _wave(t, 1.26, 0.3, 1) - 3.0 * _wave(t, 1.56, 0.3, 1)
    p.roll = p.roll - 4.0 * env(t, 1.04, 1.3, 1.46, 1.8)
    p.pitch = p.pitch - 2.0 * wipe
    p.fist = {"l": 0.25, "r": 0.25 + 0.1 * wipe - 0.15 * flick}
    return p


def clip_life_reset(t, L):
    """Переставити стопи: вага на задню ногу (передчуття), передня ступає трохи вперед і всередину й повертається;
    руки роблять короткий фінт уперед."""
    p = stance(t, IDLE_L)
    L0, R0 = (0.2, -0.105), (-0.2, 0.075)
    L1 = (0.185, -0.2)                                                    # уперед і ледь усередину (0,16 вивертало щиколотку в Ж)
    p.ankles = steps(t, [(0.0, L0), (0.5, L0), (0.82, L1), (1.45, L1), (1.82, L0), (2.4, L0)],
                     [(0.0, R0), (2.4, R0)])
    # У повітрі носок розслаблено опущений (а не стопа плазом): два перенесення — туди й назад.
    air = max(_wave(t, 0.5, 0.32, 1), _wave(t, 1.45, 0.37, 1))
    p.foot_rot = (Quaternion(UP, math.radians(STANCE_FOOT_YAW["l"])) @ Quaternion(LEFT, math.radians(14.0 * air)),
                  Quaternion(UP, math.radians(STANCE_FOOT_YAW["r"])))
    load = env(t, 0.2, 0.5, 0.6, 0.85)                                    # вага на праву (задню), поки ліва в повітрі
    fwd = env(t, 0.75, 0.95, 1.4, 1.75)                                   # таз за передньою ногою
    p.pelvis = p.pelvis + Vector((-0.035 * load + 0.01 * fwd, -0.04 * fwd + 0.015 * load, -0.015 * fwd))
    p.pitch = p.pitch + 4.0 * fwd
    feint = hit_spring(t, 0.98, w=12.0, z=0.45)
    for sd in "lr":
        g = _guard(sd)
        p.hands[sd] = _hand(sd, lambda S, g=g: g(S) + Vector((0.0, -0.07 * max(0.0, feint), 0.0)))
    p.head_pitch = p.head_pitch + 3.0 * fwd
    return p


def clip_life_come_on(t, L):
    """«Ну давай!»: груди вперед, підборіддя вгору, руки розкриваються, кисті двічі підкликають суперника; з кожним
    підкликанням — короткий кивок і поштовх грудьми. Ліва рука трохи вища за праву (асиметрія)."""
    p = stance(t, IDLE_L)
    a = env(t, 0.25, 0.58, 1.6, 2.1)
    pump = _wave(t, 0.72, 0.44, 2)
    p.pitch = p.pitch - 11.0 * a + 2.0 * pump
    p.head_pitch = p.head_pitch - 10.0 * a + 4.0 * pump
    p.pelvis = p.pelvis + Vector((0.0, 0.015 * a - 0.01 * pump, 0.03 * a))
    for sd, sx in (("l", 1.0), ("r", -1.0)):
        g, e = _guard(sd), _elbow(sd)
        hi = 0.03 if sd == "l" else -0.02
        open_ = (lambda S, sd=sd, sx=sx, hi=hi: S[sd] + Vector((0.36 * sx, -0.24 + 0.1 * pump, -0.12 + hi)))
        p.hands[sd] = _hand(sd, lambda S, g=g, o=open_: lerp(g(S), o(S), a))
        p.elbows[sd] = (lambda S, e=e, sx=sx: e(S) + Vector((0.12 * sx * a, 0.0, 0.08 * a)))
    p.fist = {"l": 0.25 - 0.2 * a + 0.5 * pump, "r": 0.25 - 0.2 * a + 0.5 * pump}
    return p


def clip_life_fists(t, L):
    """Стиснути кулаки: кулаки сходяться перед грудьми, лікті назовні, тремтіння напруги; видих — і назад у блок."""
    p = stance(t, IDLE_L)
    a = env(t, 0.25, 0.6, 1.4, 1.75)
    tremble = 0.004 * math.sin(2 * math.pi * 9.0 * t) * env(t, 0.6, 0.7, 1.35, 1.45)
    out = hit_spring(t, 1.55, w=13.0, z=0.4)
    for sd, sx in (("l", 1.0), ("r", -1.0)):
        g, e = _guard(sd), _elbow(sd)
        mid = (lambda S, sx=sx: S["chest"] + Vector((0.06 * sx, -0.3, 0.02)))
        p.hands[sd] = _hand(sd, lambda S, g=g, m=mid: lerp(g(S), m(S), a) + Vector((tremble, -0.03 * out, 0.0)))
        p.elbows[sd] = (lambda S, e=e, sx=sx: e(S) + Vector((0.18 * sx * a, 0.0, 0.18 * a)))
    p.pitch = p.pitch + 6.0 * a
    p.head_pitch = p.head_pitch + 7.0 * a
    p.pelvis = p.pelvis + Vector((0.0, 0.0, -0.02 * a))
    p.fist = {"l": 0.25 + 0.75 * a, "r": 0.25 + 0.75 * a}
    return p


def clip_life_breath(t, L):
    """Глибокий вдих і видих: груди піднімаються, плечі вгору, видих — тулуб осідає, руки падають і знову в блок."""
    p = stance(t, IDLE_L)
    inh = env(t, 0.15, 0.85, 0.95, 1.25)
    exh = env(t, 1.05, 1.4, 1.6, 2.15)
    p.pitch = p.pitch - 7.0 * inh + 7.0 * exh
    p.pelvis = p.pelvis + Vector((0.0, 0.0, 0.015 * inh - 0.02 * exh))
    p.head_pitch = p.head_pitch - 7.0 * inh + 6.0 * exh
    for sd in "lr":
        g, e = _guard(sd), _elbow(sd)
        p.hands[sd] = _hand(sd, lambda S, g=g: g(S) + Vector((0.0, 0.0, 0.03 * inh - 0.06 * exh)))
        p.elbows[sd] = (lambda S, e=e: e(S) + Vector((0.0, 0.0, 0.05 * inh)))
    return p


# ---------------------------------------------------------------- рефері (погляд з пари — не довше ~1 с)

def _ref_base(t):
    return clip_ref_idle(t, REF_IDLE_L)


def clip_ref_life_crouch(t, L):
    """Рефері нахиляється до пари: таз назад і вниз, тулуб уперед на ~55°, руки майже прямі, долоні на стегнах над колінами
    (зверху, не між ногами — огляд 15.11.2026), лікті ледь назовні; голова піднята, далі стежить за обома; випрямляється."""
    p = _ref_base(t)
    c = env(t, 0.35, 0.9, 2.1, 2.75)
    p.pelvis = p.pelvis + Vector((0.0, 0.10 * c, -0.22 * c))
    p.pitch = p.pitch + 50.0 * c
    p.head_pitch = p.head_pitch - 4.0 * c                                 # тулуб і так тягне голову вгору (0,7 нахилу)
    for sd, sx in (("l", 1.0), ("r", -1.0)):
        base = p.hands[sd]
        knee = (lambda S, sx=sx: S["pelvis"] + Vector((0.15 * sx, -0.28, -0.19)) * BODY["leg"])
        p.hands[sd] = (lambda S, b=base[0], k=knee: lerp(b(S), k(S), c), base[1], base[2])
        el = p.elbows[sd]
        p.elbows[sd] = (lambda S, el=el, sx=sx: el(S) + Vector((0.25 * sx * c, -0.15 * c, 0.1 * c)))
    p.fist = {"l": 0.3 - 0.2 * c, "r": 0.3 - 0.2 * c}
    return p


def clip_ref_life_cuffs(t, L):
    """Рефері поправляє манжет: праве передпліччя піднімається перед грудьми (лікоть зігнутий під прямим кутом), ліва кисть
    бере манжет на зап'ясті й двічі підтягує; погляд униз лише на ~0,6 с. Висота — від плечей, на рівні нижніх ребер
    (огляд 15.11.2026: від кістки грудей кисті опинялись біля пояса й паху)."""
    p = _ref_base(t)
    a = env(t, 0.3, 0.9, 1.85, 2.45)                                      # руки піднімаються 0,6 с — без ривка
    look = env(t, 0.85, 1.05, 1.45, 1.7)
    p.head_pitch = p.head_pitch + 22.0 * look
    p.head_yaw = p.head_yaw * (1.0 - look) - 10.0 * look
    tug = _wave(t, 1.0, 0.35, 2)
    br, bl = p.hands["r"], p.hands["l"]
    fr = FWD * 0.7 + LEFT * 0.6 + UP * 0.2                                # праве передпліччя: вперед-ліворуч
    fl = FWD * 0.6 + RIGHT * 0.7 + UP * 0.1                               # ліве — назустріч
    wr = (lambda S: (S["l"] + S["r"]) * 0.5 + Vector((-0.05, -0.27, -0.24)))   # права долоня перед нижніми ребрами
    cuff = (lambda S: wr(S) - fr.normalized() * 0.07 + Vector((0.02, -0.02 - 0.02 * tug, 0.03)))   # манжет: на зап'ясті, згори
    p.hands["r"] = (lambda S: lerp(br[0](S), wr(S), a), lerp(br[1], fr, a), lerp(br[2], UP + LEFT * 0.2, a))
    p.hands["l"] = (lambda S: lerp(bl[0](S), cuff(S), a), lerp(bl[1], fl, a), lerp(bl[2], UP + RIGHT * 0.2, a))
    el, er = p.elbows["l"], p.elbows["r"]
    p.elbows["l"] = (lambda S: el(S) + Vector((0.1 * a, -0.1 * a, 0.06 * a)))
    p.elbows["r"] = (lambda S: er(S) + Vector((-0.1 * a, -0.05 * a, 0.02 * a)))
    p.fist = {"l": 0.3 + 0.4 * a, "r": 0.3}
    return p


def clip_ref_life_clap(t, L):
    """Рефері двічі плескає в долоні перед грудьми — «працюйте!»; погляд на парі весь час. Долоні сходяться ПЛОЩИНОЮ: кисті
    повернуті в зап'ястях пальцями вперед-угору, долонями одна до одної (hand_aim), лікті біля боків. Кожен оплеск —
    розмах на 14 см у кожен бік, швидке зведення, короткий відскік. Прийом ПМ 15.11.2026: у попередній редакції кисті йшли
    лінією передпліч і сходились кінчиками пальців «будиночком», без розмаху."""
    p = _ref_base(t)
    a = env(t, 0.25, 0.85, 1.85, 2.45)                                    # руки піднімаються 0,6 с — без ривка
    # Два оплески — на 1,00 і 1,42 с: розмах тримається, зведення швидке (0,09 с), після удару — короткий відскік.
    # wide: 1 — долоні розведені на розмах, 0 — разом.
    wide = track(t, [(0.0, 1.0), (0.78, 1.0), (0.91, 1.0), (1.0, 0.0), (1.06, 0.16), (1.2, 1.0), (1.33, 1.0), (1.42, 0.0),
                     (1.48, 0.16), (1.7, 0.6), (2.45, 1.0), (3.2, 1.0)])
    aim = {}
    for sd, sx in (("l", 1.0), ("r", -1.0)):
        b = p.hands[sd]
        fore = FWD * 0.85 + UP * 0.5                                      # пальці вперед-угору
        thumb = UP * 0.85 + BACK * 0.5                                    # великі пальці вгору — долоні одна до одної
        mid = (lambda S, sx=sx: (S["l"] + S["r"]) * 0.5 + Vector((sx * (0.018 + 0.14 * wide), -0.35, -0.17)))
        p.hands[sd] = (lambda S, b=b, m=mid: lerp(b[0](S), m(S), a), lerp(b[1], fore, a), lerp(b[2], thumb, a))
        aim[sd] = (fore, thumb, a)
        el = p.elbows[sd]
        p.elbows[sd] = (lambda S, el=el, sx=sx: el(S) + Vector((-0.06 * sx * a, -0.05 * a, -0.05 * a)))   # лікті до боків
    p.hand_aim = aim
    p.pitch = p.pitch + 3.0 * a
    p.head_pitch = p.head_pitch - 4.0 * a
    p.fist = {"l": 0.3 - 0.22 * a, "r": 0.3 - 0.22 * a}
    return p


# ---------------------------------------------------------------- реєстр

IDLE_LIFE = [
    ("Stance_Life_Shake", clip_life_shake),
    ("Stance_Life_Look_L", _life_look(1.0)),
    ("Stance_Life_Look_R", _life_look(-1.0)),
    ("Stance_Life_Bounce", clip_life_bounce),
    ("Stance_Life_Neck", clip_life_neck),
    ("Stance_Life_Wipe", clip_life_wipe),
    ("Stance_Life_Reset", clip_life_reset),
    ("Stance_Life_ComeOn", clip_life_come_on),
    ("Stance_Life_Fists", clip_life_fists),
    ("Stance_Life_Breath", clip_life_breath),
]
REF_LIFE = [
    ("Ref_Life_Crouch", clip_ref_life_crouch),
    ("Ref_Life_Cuffs", clip_ref_life_cuffs),
    ("Ref_Life_Clap", clip_ref_life_clap),
]
for _n, _f in IDLE_LIFE:
    MOVE_CLIPS.append((_n, IDLE_L, _f, None, False, None, 0))
    MOVE_CLIP_NODES[_n] = {"A": ("stance", "stance")}
for _n, _f in REF_LIFE:
    MOVE_CLIPS.append((_n, REF_IDLE_L, _f, None, False, None, 0))
    MOVE_CLIP_NODES[_n] = {"A": ("ref_stand", "ref_stand")}
