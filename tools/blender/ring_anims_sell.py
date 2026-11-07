# «Продаж» після бампа (власник 30.10.2026: «анімації бампів теперь», «Должно быть несколько вариаций селлинга у
# каждого бампа»). Виконується в ring_anims.py (exec). Будова — з фото й записів матчів, своїми словами:
#   KO     — лежить пластом, руки розкинуті, голова набік, лише важке дихання;
#   Back   — перекочується на бік, коліна підтягнуті, долоня на попереку;
#   Head   — на боці, обидві долоні на голові, коліна підтягнуті;
#   Arch   — вигинається: одне коліно вгору, голова закинута, руки упираються в мат;
#   Reach  — перекочується на бік до канатів і тягнеться рукою над головою.
# Кожен варіант: бамп (той самий, що Bump_Back) → продаж → знову «лежить на спині» (вузол lying_back), тож далі —
# GetUp_Back або прийом суперника.

SELL_LEN = 3.4
SELL_IN, SELL_HOLD, SELL_OUT = 1.25, 1.65, 2.75     # с: початок, повний продаж, повернення на спину
BUMP_END_Y = 0.4                                   # де лежить таз після бампа (як у clip_bump_back)

SELLS = {
    # roll — перекат навколо довгої осі тіла (°, + — на правий бік); knees — згин колін; arch — підйом грудей (°)
    "KO":    dict(roll=0.0,   knees=0.15, knees_r=0.15, arch=0.0,  head_yaw=45.0, head_pitch=-30.0, breath=1.6),
    "Back":  dict(roll=-55.0,  knees=0.75, knees_r=0.75, arch=0.0,  head_yaw=10.0, head_pitch=-10.0, breath=1.0),
    "Head":  dict(roll=45.0, knees=0.7,  knees_r=0.7,  arch=0.0,  head_yaw=-10.0, head_pitch=-5.0, breath=1.0),
    "Arch":  dict(roll=0.0,   knees=0.15, knees_r=0.8,  arch=10.0, head_yaw=0.0,  head_pitch=-25.0, breath=1.2, lift=0.07),
    "Reach": dict(roll=65.0, knees=0.35, knees_r=0.2,  arch=0.0,  head_yaw=-25.0, head_pitch=-15.0, breath=1.0),
}


def sell_pose(t, kind):
    v = SELLS[kind]
    s = track(t, [(SELL_IN, 0.0), (SELL_HOLD, 1.0), (SELL_OUT, 1.0), (SELL_LEN - 0.15, 0.0), (SELL_LEN + 1.0, 0.0)])
    br = math.sin(2 * math.pi * t / 0.9) * 0.5 + 0.5        # важке дихання — частіше за звичайне
    roll = v["roll"] * s
    q = Q_BACK @ Quaternion(UP, math.radians(-roll))       # перекат навколо довгої осі тіла
    # вигин — таз піднімається над матом (голова й плечі лишаються на ньому), на боці — таз вище на півширини
    off = off_for(Vector((0.0, BUMP_END_Y, LIE_Z + 0.09 * abs(math.sin(math.radians(roll))) + v.get("lift", 0.0) * s)))
    kb_l = 0.15 + (v["knees"] - 0.15) * s
    kb_r = 0.15 + (v["knees_r"] - 0.15) * s
    p = body(off, q, knee_bend=kb_l)
    if abs(kb_r - kb_l) > 1e-4:                            # праве коліно окремо (Arch, Reach)
        p.ankles = (p.ankles[0], xf(off, q, REST["ankle_r"] + Vector((0.0, -0.25 * kb_r, 0.35 * kb_r))))
    p.pitch = -v["arch"] * s + 4.0 * br * v["breath"] * (0.4 + 0.6 * s)
    p.head_yaw = v["head_yaw"] * s
    p.head_pitch = -30.0 * (1 - s) + v["head_pitch"] * s
    lift = 0.0 if kind != "Arch" else 0.0
    arms_on_mat(p, q, lift_l=lift, lift_r=lift)
    mat = dict(p.hands)
    ak = BODY["arm"]
    def on_back(S):                                          # долоня на попереку (ззаду таза — +Y тіла)
        return S["pelvis"] + q @ Vector((-0.1, 0.2, 0.14))
    def on_head(side):
        def g(S):
            return S["head"] + q @ Vector((0.09 * (1 if side == "l" else -1), -0.02, 0.06))
        return g
    def reach_up_r(S):                                       # верхня рука тягнеться над головою до канатів
        pt = S["r"] + q @ (Vector((-0.1, -0.15, 0.4)) * ak)
        return Vector((pt.x, pt.y, max(pt.z, 0.14)))
    # нижня рука (на тому боці, на який перекотився) — на маті перед грудьми/під головою, не під тілом
    down = None if abs(v["roll"]) < 1 else ("l" if v["roll"] > 0 else "r")
    def on_mat_front(side):
        def g(S):
            fwd_w = q @ FWD                                  # «уперед» тіла лежачи — угору; на боці — убік по мату
            pt = S["chest"] + fwd_w * 0.32 + q @ Vector((0.0, 0.0, 0.12))
            return Vector((pt.x, pt.y, max(pt.z, 0.16)))
        return g
    tgt = {"KO": {}, "Back": {"r": on_back}, "Head": {"l": on_head("l"), "r": on_head("r")},
           "Arch": {}, "Reach": {"r": reach_up_r}}[kind]
    if down and down not in tgt:
        tgt[down] = on_mat_front(down)
    hands = {}
    for s_ in "lr":
        if s_ in tgt:
            g0, g1 = mat[s_][0], tgt[s_]
            def via(S, g0=g0, g1=g1):                       # шлях — над тілом (дуга вгору), не крізь плече
                a, b = g0(S), g1(S)
                return a.lerp(b, s) + Vector((0, 0, 0.18)) * math.sin(math.pi * s)
            hands[s_] = (via, q @ (DOWN.lerp(FWD, s)), q @ FWD)
        else:
            hands[s_] = mat[s_]
    p.hands = hands
    # лікті — від мату вгору (світове +Z) і назовні: на боці «назовні» тіла може дивитись у мат
    # лікті — від мату вгору (світове +Z) і назовні; на старті продажу — ті самі, що в «лежить» (без стрибка руки)
    base_el = _filled(lying(y=BUMP_END_Y, sit=0.0, knee_bend=0.15)).elbows
    new_el = {"l": lambda S: S["l"] + q @ Vector((0.35, -0.1, -0.25)) + Vector((0, 0, 0.25)),
              "r": lambda S: S["r"] + q @ Vector((-0.35, -0.1, -0.25)) + Vector((0, 0, 0.25))}
    if kind == "Back":                                       # рука на попереку — лікоть назад і вгору
        new_el["r"] = lambda S: S["r"] + q @ Vector((-0.3, 0.3, -0.15)) + Vector((0, 0, 0.2))
    # полюс «лежить» дивиться в мат (лікоть під мат, щойно рука згинається) — до полюса продажу за першу третину
    we = min(1.0, s * 3.0)
    p.elbows = {s_: (lambda S, s_=s_: base_el[s_](S).lerp(new_el[s_](S), we)) for s_ in "lr"}
    p.fist = {"l": 0.2 + 0.3 * s * (kind in ("Head", "Back")), "r": 0.2 + 0.3 * s * (kind in ("Head", "Back"))}
    return p


def clip_bump_sell(kind):
    def f(t, L):
        if t < SELL_IN:
            return clip_bump_back(t, BUMP_LEN)
        return sell_pose(t, kind)
    return f


for _k in SELLS:
    MOVE_CLIPS.append((f"Bump_Back_Sell_{_k}", SELL_LEN, clip_bump_sell(_k), None, False, None, 0))
    MOVE_CLIP_NODES[f"Bump_Back_Sell_{_k}"] = {"A": ("stance", "lying_back")}


# ================================================================ бамп обличчям донизу (front bump)
# Референс (записи матчів і навчальні відео, своїми словами): з удару в спину чи з підсічки — коліна підгинаються,
# тіло падає вперед прямим; передпліччя й долоні б'ють мат першими (під кутом, біля голови), груди й стегна
# лягають одночасно, голова повернута вбік. Продаж: нокаут лицем донизу; відкочується на спину, тримаючись за груди;
# піднімається на лікті й падає знову; повзе до канатів; стає рачки, тримаючись за живіт, і падає назад.
Q_FRONT = Quaternion(LEFT, math.radians(90.0))      # лежить на животі, голова — у −Y ригу
FRONT_Z = 0.13
FRONT_END_Y = -0.45
FBUMP_LEN = 1.3


def lying_front(y=FRONT_END_Y, knee_bend=0.05, head_side=1.0, push=0.0, roll=0.0, frog=(0.0, 0.0)):
    """Лежить на животі: передпліччя на маті біля голови, голова повернута вбік (обличчям не в мат).
    frog — коліно лівої/правої підтягнуте вбік по мату (лежачи на животі коліно не може йти «вперед» — там мат)."""
    q = Q_FRONT @ Quaternion(UP, math.radians(roll))
    off = off_for(Vector((0.0, y, FRONT_Z + 0.1 * push + 0.09 * abs(math.sin(math.radians(roll))) + 0.03 * max(frog))))
    p = body(off, q, knee_bend=knee_bend)
    for i, (s_, f_) in enumerate((("l", frog[0]), ("r", frog[1]))):
        if f_ > 1e-4:
            sg = 1 if s_ == "l" else -1
            an = xf(off, q, REST["ankle_" + s_] + Vector((0.12 * sg * f_, 0.0, 0.3 * f_)))
            kn = xf(off, q, REST["knee_" + s_] + Vector((0.5 * sg, 0.0, 0.2)))
            ank, kns = list(p.ankles), list(p.knees)
            ank[i], kns[i] = an, kn
            p.ankles, p.knees = tuple(ank), tuple(kns)
    p.pitch = -25.0 * push                                  # на ліктях — груди вгору
    p.head_yaw = 75.0 * head_side
    p.head_pitch = -10.0 - 20.0 * push
    ak = BODY["arm"]
    def hand(s_):
        sg = 1 if s_ == "l" else -1
        def g(S):
            pt = S[s_] + q @ (Vector((0.12 * sg, 0.0, 0.32)) * ak) + Vector((0, 0, 0.05 * push))
            return Vector((pt.x, pt.y, max(pt.z, 0.06)))
        return g
    p.hands = {s_: (hand(s_), q @ UP, DOWN) for s_ in "lr"}        # долоні на мат, пальці — до голови
    p.elbows = {"l": lambda S: S["l"] + q @ Vector((0.4, 0.0, 0.0)) + Vector((0, 0, 0.1)),
                "r": lambda S: S["r"] + q @ Vector((-0.4, 0.0, 0.0)) + Vector((0, 0, 0.1))}
    p.fist = {"l": 0.15, "r": 0.15}
    return p


def clip_bump_front(t, L):
    """Стоїть → коліна підгинаються → падає вперед прямим → передпліччя й груди на мат."""
    th = track(t, [(0.0, 0.0), (0.2, 6.0), (0.45, 50.0), (0.58, 90.0), (FBUMP_LEN, 90.0)])
    y = track(t, [(0.0, 0.0), (0.2, -0.04), (0.45, -0.3), (0.58, FRONT_END_Y), (FBUMP_LEN, FRONT_END_Y)])
    z = track(t, [(0.0, REST["pelvis"].z - 0.09), (0.2, REST["pelvis"].z - 0.16), (0.45, (0.45 + 0.25 * (1.0 - BODY["leg"])) * BODY["leg"]),
                  (0.58, FRONT_Z), (FBUMP_LEN, FRONT_Z)])
    bounce = math.sin(math.pi * smooth((t - 0.58) / 0.2)) * 0.025 if 0.58 < t < 0.78 else 0.0
    q = Quaternion(LEFT, math.radians(th))
    p = body(off_for(Vector((0.0, y, z + bounce))), q, knee_bend=0.25 * (1 - smooth(th / 90.0)))
    planted = 1.0 - smooth((th - 25.0) / 35.0)
    sl, sr = foot(0.2, -0.105), foot(-0.2, 0.075)
    planted_xy = 1.0 - smooth((th - 30.0) / 25.0)          # стопи спершу відриваються, потім летять (без ковзання по мату)
    def fl_(a_, s_):
        xy = lerp(a_, s_, planted_xy)
        return Vector((xy.x, xy.y, a_.z + (s_.z - a_.z) * planted))
    p.ankles = (fl_(p.ankles[0], sl), fl_(p.ankles[1], sr))
    yl, yr = Quaternion(UP, math.radians(10.0)), Quaternion(UP, math.radians(-14.0))
    p.foot_rot = (yl.slerp(q, 1 - planted), yr.slerp(q, 1 - planted))
    p.pitch = 14.0 * (1 - smooth(th / 70.0))
    p.head_yaw = 75.0 * smooth((th - 40.0) / 40.0)          # обличчя — вбік до удару об мат
    p.head_pitch = -15.0 * smooth(th / 90.0)
    st = stance(0.0, L, sway=0.0)
    lf = lying_front()
    w = smooth((th - 20.0) / 50.0)                          # руки — з гарду вперед на мат раніше за груди
    p = _arms_to(p, st, 1.0)
    p = _arms_to(p, lf, w)
    end_w = smooth((t - 0.95) / 0.3)
    if end_w > 0:
        return blend_pose(_filled(p), _filled(lying_front()), end_w)
    return p


FLIP_LEN = 1.5
FLIP_BACK = 0.6          # м: сальто відносить назад від удару


def clip_flip_front(t, L):
    """Сальто назад від «шлагбаума» (огляд 04.11.2026: «Clothesline_Flip — без перевороту»): ноги вилітають угору,
    тіло крутиться назад на три чверті оберту в згрупованому стані й лягає грудьми на мат; руки — на мат першими.
    Референс — записи матчів, своїми словами: удар підрізає під шию, ноги йдуть уперед-угору, оберт навколо шиї."""
    th = track(t, [(0.0, 0.0), (0.1, 25.0), (0.3, 150.0), (0.46, 250.0), (0.56, 270.0), (L, 270.0)])
    y = track(t, [(0.0, 0.0), (0.1, 0.05), (0.3, 0.35), (0.56, FLIP_BACK), (L, FLIP_BACK)])
    z0 = REST["pelvis"].z - 0.09
    z = track(t, [(0.0, z0), (0.1, z0 + 0.04), (0.28, z0 + 0.32), (0.38, z0 + 0.22), (0.46, 0.6 * BODY["leg"]), (0.56, FRONT_Z), (L, FRONT_Z)])
    bounce = math.sin(math.pi * smooth((t - 0.56) / 0.2)) * 0.03 if 0.56 < t < 0.76 else 0.0
    q = Quaternion(LEFT, math.radians(-th))
    tuck = math.sin(math.pi * min(1.0, max(0.0, (th - 20.0) / 230.0)))      # згрупований посередині оберту
    p = body(off_for(Vector((0.0, y, z + bounce))), q, knee_bend=0.05 + 0.65 * tuck)
    planted = 1.0 - smooth((th - 8.0) / 25.0)
    sl, sr = foot(0.2, -0.105), foot(-0.2, 0.075)
    planted_xy = 1.0 - smooth((th - 30.0) / 25.0)          # стопи спершу відриваються, потім летять (без ковзання по мату)
    def fl_(a_, s_):
        xy = lerp(a_, s_, planted_xy)
        return Vector((xy.x, xy.y, a_.z + (s_.z - a_.z) * planted))
    p.ankles = (fl_(p.ankles[0], sl), fl_(p.ankles[1], sr))
    yl, yr = Quaternion(UP, math.radians(10.0)), Quaternion(UP, math.radians(-14.0))
    p.foot_rot = (yl.slerp(q, 1 - planted), yr.slerp(q, 1 - planted))
    p.pitch = 20.0 * tuck                                   # підборіддя й коліна до грудей
    p.head_pitch = 30.0 * tuck - 15.0 * smooth((th - 240.0) / 30.0)
    p.head_yaw = 75.0 * smooth((th - 230.0) / 40.0)
    st = stance(0.0, L, sway=0.0)
    p = _arms_to(p, st, 1.0)
    p = _arms_to(p, lying_front(y=FLIP_BACK), smooth((th - 200.0) / 50.0))     # руки на мат перед грудьми
    end_w = smooth((t - 0.9) / 0.4)
    if end_w > 0:
        return blend_pose(_filled(p), _filled(lying_front(y=FLIP_BACK)), end_w)
    return p


FSELLS = {
    # end: вузол наприкінці; push — на ліктях; roll — перекат (°, 180 — на спину)
    "KO":     dict(end="lying_front"),
    "RollBack": dict(end="lying_back"),
    "PushUp": dict(end="lying_front"),
    "Crawl":  dict(end="lying_front"),
    "Knees":  dict(end="lying_front"),
}
FSELL_LEN = 3.4


def fsell_pose(t, kind):
    s = track(t, [(SELL_IN, 0.0), (SELL_HOLD, 1.0), (SELL_OUT, 1.0), (FSELL_LEN - 0.15, 0.0), (FSELL_LEN + 1.0, 0.0)])
    br = math.sin(2 * math.pi * t / 0.9) * 0.5 + 0.5
    if kind == "KO":
        p = lying_front()
        p.pitch = -3.0 * br
        return p
    if kind == "PushUp":
        p = lying_front(push=s * (1.5 + 0.2 * br))           # огляд 04.11.2026: груди майже не відривались
        return p
    if kind == "Crawl":                                     # тягнеться правою вперед, підтягує ліве коліно
        k = track(t, [(SELL_IN, 0.0), (SELL_HOLD, 1.0), (SELL_OUT, 1.0), (FSELL_LEN - 0.15, 0.0), (FSELL_LEN + 1, 0.0)])
        # огляд 04.11.2026: від нокауту не відрізнялось — два ривки: права далеко вперед, ліве коліно вбік, таз за рукою
        pull = 0.5 - 0.5 * math.cos(2 * math.pi * min(1.0, max(0.0, (t - SELL_HOLD) / (SELL_OUT - SELL_HOLD))) * 2) if t > SELL_HOLD else 0.0
        p = lying_front(y=FRONT_END_Y - 0.12 * k * min(1.0, max(0.0, (t - SELL_HOLD) / (SELL_OUT - SELL_HOLD))),
                        roll=-22.0 * k, frog=(min(1.0, 1.1 * k * (0.5 + 0.5 * pull)), 0.0), push=0.3 * k)
        q = Q_FRONT @ Quaternion(UP, math.radians(-22.0 * k))
        g0 = p.hands["r"][0]
        reach = 0.55 + 0.25 * (1 - pull)
        p.hands["r"] = (lambda S: g0(S).lerp(Vector((S["r"].x, (S["r"] + q @ Vector((0, 0, reach * BODY["arm"]))).y, 0.07)), k),
                        q @ UP, DOWN)
        p.pelvis = p.pelvis + Vector((0.0, 0.0, 0.025))     # підтягнуте коліно — над матом (стегно йшло під мат на 0,6 см)
        return p
    if kind == "Knees":                                     # рачки, рука на животі, далі знову на мат
        # огляд 04.11.2026 (двічі): «дошка» на прямих ногах, а не рачки — тепер окрема поза: стегна прямовисно,
        # коліна на маті під тазом, гомілки й п'яти позаду, права долоня на маті під плечем, ліва — на животі
        lf = lying_front()
        if s <= 1e-4:
            return lf
        y0 = FRONT_END_Y + 0.42
        lk = BODY["leg"]
        q = Quaternion(LEFT, math.radians(78.0))
        kp = body(off_for(Vector((0.0, y0, 0.5 * lk + 0.06))), q, knee_bend=0.0)
        kp.ankles = (Vector((0.13, y0 + 0.44 * lk, 0.1)), Vector((-0.13, y0 + 0.44 * lk, 0.1)))
        tip = Quaternion(RIGHT, math.radians(-75.0))
        kp.foot_rot = (tip, tip)
        kp.knees = (Vector((0.14, y0 - 0.4, 0.0)), Vector((-0.14, y0 - 0.4, 0.0)))
        kp.pitch = 8.0
        kp.head_pitch = 20.0
        kp.head_yaw = 0.0
        kp.hands = {"r": (lambda S: Vector((S["r"].x - 0.02, S["r"].y, max(0.07, S["r"].z - 0.48 * BODY["arm"]))), q @ UP, DOWN),
                    "l": (lambda S: S["pelvis"] + q @ Vector((0.04, -0.14, 0.2)), q @ UP, DOWN)}
        kp.elbows = {"r": lambda S: S["r"] + Vector((-0.2, 0.15, -0.1)), "l": lambda S: S["l"] + Vector((0.25, 0.2, -0.15))}
        kp.fist = {"l": 0.4, "r": 0.1}
        kp.forced = set()
        out = blend_pose(_filled(lf), _filled(kp), smooth(s))
        hump = 0.12 * math.sin(math.pi * smooth(s)) + 0.02 * min(1.0, s * 15.0)   # на переході — таз і коліна над матом (лінійне змішування вело гомілку в мат)
        out.pelvis = out.pelvis + Vector((0.0, 0.0, hump))
        if out.knees:
            out.knees = tuple(k_ + Vector((0.0, 0.0, hump)) for k_ in out.knees)
        out.ankles = tuple(a_ + Vector((0.0, 0.0, 0.6 * hump)) for a_ in out.ankles)
        return hands_out_of_torso(out, r=0.19)
    if kind == "RollBack":                                  # перекат на спину одним поворотом, долоня на грудях
        r = smooth(track(t, [(SELL_IN, 0.0), (SELL_HOLD + 0.2, 1.0), (FSELL_LEN + 1, 1.0)]))
        hold = track(t, [(SELL_IN, 0.0), (SELL_HOLD + 0.2, 1.0), (SELL_OUT, 1.0), (FSELL_LEN - 0.15, 0.0), (FSELL_LEN + 1, 0.0)])
        roll = -180.0 * r                                  # на лівий бік: права рука — зверху, вільна до грудей
        q = Q_FRONT @ Quaternion(UP, math.radians(roll))
        # на боці таз і плечі — на півширини над матом (інакше нижнє плече й лікоть ішли під мат)
        off = off_for(Vector((0.0, FRONT_END_Y, FRONT_Z + 0.19 * abs(math.sin(math.radians(roll))))))
        p = body(off, q, knee_bend=0.05 + 0.1 * r + 0.4 * hold * r)        # наприкінці — коліна як у «лежить на спині»
        p.head_yaw = 75.0 * (1 - r)
        p.head_pitch = -10.0 - 20.0 * r
        ak = BODY["arm"]
        lb = smooth(track(t, [(SELL_HOLD + 0.15, 0.0), (SELL_HOLD + 0.65, 1.0), (FSELL_LEN + 1, 1.0)]))
        def hand(s_):
            sg = 1 if s_ == "l" else -1
            def g(S):
                # перекат «колодою»: обидві долоні притиснуті до грудей (рука не потрапляє під тіло), після — ліва на мат
                front = S[s_] + q @ (Vector((0.12 * sg, 0.0, 0.32)) * ak)
                chest = S["chest"] + q @ Vector((0.07 * sg, -0.2, 0.06))
                back = S[s_] + q @ (Vector((0.42 * sg, 0.05, -0.32)) * ak)
                over = S[s_] + q @ (Vector((0.05, 0.0, 0.45)) * ak)   # нижня (ліва) — над головою вздовж осі перекату
                if s_ == "l":
                    if r < 0.25:
                        pt = front.lerp(over, smooth(r / 0.25))
                    else:                                       # на мат — уже після перекату, за 0,5 с (не стрибком)
                        pt = over.lerp(back, lb)
                elif r < 0.35:                                 # права — дугою вбік від тіла, не крізь живіт
                    u = smooth(r / 0.35)
                    pt = front.lerp(chest, u) + q @ Vector((-0.18, 0.0, 0.0)) * math.sin(math.pi * u)
                else:
                    pt = chest
                if s_ == "r":                                  # права лишається на грудях, поки триває продаж
                    pt = pt.lerp(back, smooth(1.0 - hold) * r) if r > 0.99 else pt
                return Vector((pt.x, pt.y, max(pt.z, 0.07)))
            return g
        p.hands = {s_: (hand(s_), q @ (UP.lerp(DOWN, r)), DOWN.lerp(q @ FWD, r)) for s_ in "lr"}
        tuck = smooth(min(1.0, r / 0.25)) * (1.0 - smooth(max(0.0, (r - 0.8) / 0.2)) * 0.0)
        def elb(s_):
            sg = 1 if s_ == "l" else -1
            def e(S):
                side = S[s_] + q @ Vector((0.4 * sg, 0.0, 0.0)) + Vector((0, 0, 0.15))
                if s_ == "l":                                   # витягнута над головою — лікоть угору від мату
                    ov = S[s_] + q @ Vector((0.0, 0.0, 0.25)) + Vector((0, 0, 0.3))
                    return side.lerp(ov, tuck * (1.0 - lb))
                front = S[s_] + q @ Vector((-0.12, -0.3, -0.2))  # права притиснута — лікоть перед грудьми
                return side.lerp(front, tuck)
            return e
        p.elbows = {s_: elb(s_) for s_ in "lr"}
        p.fist = {"l": 0.15, "r": 0.15 + 0.3 * hold}
        return p
    raise KeyError(kind)


def clip_fbump_sell(kind):
    def f(t, L):
        if t < SELL_IN:
            return clip_bump_front(t, FBUMP_LEN)
        return fsell_pose(t, kind)
    return f


MOVE_NODE_POSES["lying_front"] = lambda: lying_front()
MOVE_CLIPS.append(("Bump_Front", FBUMP_LEN, clip_bump_front, None, False, None, 0))
MOVE_CLIP_NODES["Bump_Front"] = {"A": ("stance", "lying_front")}
for _k, _v in FSELLS.items():
    MOVE_CLIPS.append((f"Bump_Front_Sell_{_k}", FSELL_LEN, clip_fbump_sell(_k), None, False, None, 0))
    MOVE_CLIP_NODES[f"Bump_Front_Sell_{_k}"] = {"A": ("stance", _v["end"])}


# ================================================================ бамп на бік (side bump)
# Референс (записи кидків через стегно й підсічок, своїми словами): ноги вилітають, тіло падає на правий бік — стегно,
# лікоть-передпліччя й плече б'ють мат майже разом, нижня рука ляскає по мату долонею, верхня — перед грудьми, голова
# піднята від мату. Продаж: лежить на боці скрутившись; тримається за ребра; за плече; перекочується на живіт і
# піднімається на лікті; перекочується на спину й вигинається, тримаючи поперек.
SIDE_ROLL = 90.0                                   # лежить на правому боці (перекат +90 від «на спині», перевірено заміром)
SIDE_X = -0.38                                     # падає вправо (простір ригу: −X — право постаті)
SBUMP_LEN = 1.3


def side_q(roll):
    return Q_BACK @ Quaternion(UP, math.radians(-roll))


def side_lying(t, roll, knees=0.3, knees_r=None, hands=None, head_pitch=-20.0, head_yaw=0.0, curl=0.0, x=SIDE_X, zoff=0.0):
    """Лежить, повернутий на roll° від «на спині» (+90 — на лівому боці, 180 — на животі)."""
    q = side_q(roll)
    off = off_for(Vector((x, BUMP_END_Y, LIE_Z + 0.19 * abs(math.sin(math.radians(roll))) + zoff)))
    back_w = 1.0 - abs(math.sin(math.radians(roll)))          # на спині — коліна й руки як у вузлі «лежить»
    if roll < 45.0:
        knees = knees + (0.15 - knees) * back_w
        if knees_r is not None:
            knees_r = knees_r + (0.15 - knees_r) * back_w
    p = body(off, q, knee_bend=knees)
    if knees_r is not None:
        p.ankles = (p.ankles[0], xf(off, q, REST["ankle_r"] + Vector((0.0, -0.25 * knees_r, 0.35 * knees_r))))
    br = math.sin(2 * math.pi * t / 0.9) * 0.5 + 0.5
    p.pitch = curl + 3.0 * br
    p.head_pitch = head_pitch
    p.head_yaw = head_yaw
    arms_on_mat(p, q)
    ak = BODY["arm"]
    sn = math.sin(math.radians(roll))
    down = "r" if sn > 0 else "l"                             # +roll — на правому боці (заміри 30.10.2026)
    wd = smooth((abs(sn) - 0.2) / 0.6)                        # нижня рука переходить на мат перед грудьми плавно
    def on_mat_front(S):                                   # нижня рука — на маті перед грудьми
        pt = S["chest"] + (q @ FWD) * 0.32 + q @ Vector((0.0, 0.0, 0.12))
        return Vector((pt.x, pt.y, max(pt.z, 0.16)))
    hs = dict(p.hands)
    g_mat = hs[down][0]
    hs[down] = (lambda S: g_mat(S).lerp(on_mat_front(S), wd), (q @ DOWN).lerp(q @ FWD, wd), (q @ FWD).lerp(DOWN, wd))
    # верхня рука на боці — на своє стегно (огляд 04.11.2026: ціль «долоня на маті» з пози на спині на боці опинялась
    # над тілом, і рука стирчала вгору 30+ кадрів)
    up = "l" if down == "r" else "r"
    g_up = hs[up][0]
    def on_hip(S):
        return S["pelvis"].lerp(S[up], 0.3) + (q @ FWD) * 0.08 + Vector((0.0, 0.0, 0.1))
    hs[up] = (lambda S: g_up(S).lerp(on_hip(S), wd), (q @ DOWN).lerp(DOWN, wd), q @ FWD)
    for s_, g in (hands or {}).items():
        hs[s_] = (g, q @ FWD, q @ UP)
    hs = {s_: (lambda S, h=hs[s_][0]: (lambda v: Vector((v.x, v.y, max(v.z, 0.1))))(h(S)), hs[s_][1], hs[s_][2]) for s_ in "lr"}
    p.hands = hs
    p.elbows = {"l": lambda S: S["l"] + q @ Vector((0.35, -0.1, -0.25)) + Vector((0, 0, 0.25)),
                "r": lambda S: S["r"] + q @ Vector((-0.35, -0.1, -0.25)) + Vector((0, 0, 0.25))}
    p.fist = {"l": 0.25, "r": 0.25}
    return p


def clip_bump_side(t, L):
    """Стоїть → ноги вилітають → тіло падає на лівий бік: стегно, передпліччя й плече — на мат разом."""
    k = track(t, [(0.0, 0.0), (0.18, 0.05), (0.42, 0.55), (0.56, 1.0), (SBUMP_LEN, 1.0)])
    kk = smooth(k)
    q_end = side_q(SIDE_ROLL)
    q = Quaternion().slerp(q_end, kk)
    z_end = LIE_Z + 0.19
    z = track(t, [(0.0, REST["pelvis"].z - 0.09), (0.18, REST["pelvis"].z - 0.14), (0.42, 0.42 * BODY["leg"]),
                  (0.56, z_end), (SBUMP_LEN, z_end)])
    x = track(t, [(0.0, 0.0), (0.2, 0.0), (0.42, 0.18), (0.56, SIDE_X), (SBUMP_LEN, SIDE_X)])   # убік — коли ноги вже в повітрі
    y = track(t, [(0.0, 0.0), (0.56, BUMP_END_Y), (SBUMP_LEN, BUMP_END_Y)])
    bounce = math.sin(math.pi * smooth((t - 0.56) / 0.2)) * 0.02 if 0.56 < t < 0.76 else 0.0
    p = body(off_for(Vector((x, y, z + bounce))), q, knee_bend=0.3 * kk)
    planted = 1.0 - smooth((k - 0.02) / 0.2)            # ноги вилітають першими (підсічка)
    sl, sr = foot(0.2, -0.105), foot(-0.2, 0.075)
    up = Vector((0, 0, 0.22 * (1 - planted) * (1.0 - smooth((k - 0.45) / 0.45))))   # угору, а не волоком по мату
    def lifted(a):                                        # тіло нахиляється назад — щиколотки йшли б під мат; угору
        return Vector((a.x, a.y, max(a.z, 0.071))) + up
    p.ankles = (lifted(lerp(p.ankles[0], sl, planted)), lifted(lerp(p.ankles[1], sr, planted)))
    yl, yr = Quaternion(UP, math.radians(10.0)), Quaternion(UP, math.radians(-14.0))
    p.foot_rot = (yl.slerp(q, 1 - planted), yr.slerp(q, 1 - planted))
    p.pitch = 10.0 * (1 - kk)
    p.head_pitch = -20.0 * kk                               # голова піднята від мату
    st = stance(0.0, L, sway=0.0)
    p = _arms_to(p, st, 1.0)
    p = _arms_to(p, side_lying(t, SIDE_ROLL), smooth((k - 0.2) / 0.5))
    return p


def _sw(t):
    return track(t, [(SELL_IN, 0.0), (SELL_HOLD, 1.0), (SELL_OUT, 1.0), (SELL_LEN - 0.15, 0.0), (SELL_LEN + 1.0, 0.0)])


def sside_pose(t, kind):
    s = _sw(t)
    rb = smooth(track(t, [(SELL_OUT - 0.2, 0.0), (SELL_LEN - 0.15, 1.0), (SELL_LEN + 1, 1.0)]))   # повернення на спину
    if kind == "Curl":                                      # скрутився на боці, коліна до грудей
        return side_lying(t, SIDE_ROLL * (1 - rb), knees=0.3 + 0.5 * s, curl=12.0 * s, head_pitch=-20 + 15 * s)
    if kind == "Ribs":                                      # права долоня на ребрах (лівий бік зверху — ні: ребра правого)
        def ribs(S):
            q = side_q(SIDE_ROLL * (1 - rb))
            return S["chest"] + q @ Vector((0.2, -0.15, -0.1))
        p = side_lying(t, SIDE_ROLL * (1 - rb), knees=0.3 + 0.4 * s, curl=8.0 * s)
        g0 = p.hands["l"][0]                                # верхня рука — ліва
        p.hands["l"] = (lambda S: g0(S).lerp(ribs(S), s) + Vector((0, 0, 0.2)) * math.sin(math.pi * s), p.hands["l"][1], p.hands["l"][2])
        return p
    if kind == "Writhe":                                    # корчиться на боці: коліна по черзі, долоня на стегні
        w = math.sin(2 * math.pi * (t - SELL_IN) / 0.8)
        def hip(S):
            q = side_q(SIDE_ROLL * (1 - rb))
            return S["pelvis"] + q @ Vector((0.24, -0.06, 0.04))
        # огляд 04.11.2026: «корчиться» майже не рухалось — коліна по черзі до грудей, тулуб гойдається, голова назад
        p = side_lying(t, SIDE_ROLL * (1 - rb) + 12.0 * s * w, knees=0.25 + 0.55 * s * (0.5 + 0.5 * w),
                       knees_r=0.25 + 0.55 * s * (0.5 - 0.5 * w), curl=6.0 * s + 14.0 * s * (0.5 + 0.5 * w),
                       head_pitch=-20 + 10 * s - 15.0 * s * (0.5 - 0.5 * w))
        g0 = p.hands["l"][0]
        p.hands["l"] = (lambda S: g0(S).lerp(hip(S), s) + Vector((0, 0, 0.15)) * math.sin(math.pi * s), p.hands["l"][1], p.hands["l"][2])
        return p
    if kind == "RollFront":                                 # на живіт і на лікті → лежить на животі
        r = smooth(track(t, [(SELL_IN, 0.0), (SELL_HOLD + 0.5, 1.0), (SELL_LEN + 1, 1.0)]))
        # на животі коліна не згинаються (гомілка пішла б у мат); таз — на висоті «лежить на животі»
        p = side_lying(t, SIDE_ROLL + 90.0 * r, knees=0.3 * (1 - smooth(r / 0.6)) + 0.05 * smooth((r - 0.9) / 0.1), head_yaw=-70.0 * r, zoff=(FRONT_Z - LIE_Z + 0.02) * r)
        p.pitch = p.pitch - 15.0 * r * s                    # на животі — груди вгору на ліктях
        p.pelvis = p.pelvis + Vector((0.0, 0.0, 0.03 * math.sin(math.pi * r)))       # перекат — над матом, не крізь нього
        return hands_out_of_torso(p, r=0.17)                # кисть не в тазу на перекаті
    if kind == "BackArch":                                  # на спину, вигин, долоня на попереку
        r = smooth(track(t, [(SELL_IN, 0.0), (SELL_HOLD + 0.45, 1.0), (SELL_LEN + 1, 1.0)]))
        return side_lying(t, SIDE_ROLL * (1 - r), knees=0.3 + 0.4 * s * r, curl=-6.0 * s * r, head_pitch=-20 - 15 * s)
    raise KeyError(kind)


SSELLS = {"Curl": "lying_back", "Ribs": "lying_back", "Writhe": "lying_back", "RollFront": "lying_front", "BackArch": "lying_back"}


def clip_sbump_sell(kind):
    def f(t, L):
        if t < SELL_IN:
            return clip_bump_side(t, SBUMP_LEN)
        return sside_pose(t, kind)
    return f


MOVE_CLIPS.append(("Bump_Side", SBUMP_LEN, clip_bump_side, None, False, None, 0))
MOVE_CLIP_NODES["Bump_Side"] = {"A": ("stance", None)}
for _k, _end in SSELLS.items():
    MOVE_CLIPS.append((f"Bump_Side_Sell_{_k}", SELL_LEN, clip_sbump_sell(_k), None, False, None, 0))
    MOVE_CLIP_NODES[f"Bump_Side_Sell_{_k}"] = {"A": ("stance", _end)}
