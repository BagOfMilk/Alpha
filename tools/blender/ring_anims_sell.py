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
