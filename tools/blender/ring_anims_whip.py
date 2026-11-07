# Кидок у канати й у кут — 15 варіантів (план ітерації 2, група 1; власник 04.11.2026: «Чому немає whip в канати чи в
# вугол?», «Мені подобається 1-5 … Роби вже по плану»). Виконується всередині ring_anims.py (exec) після
# ring_anims_center.py. Карта — tools/blender/moves/Whip_Ropes.json і Whip_Corner.json.
#
# Будова (записи матчів і навчальні матеріали, своїми словами): A бере B за зап'ястя ближньої руки; крок назад і
# розворот на опорній нозі — B, ведений за руку, проходить повз праве плече A; A відпускає, коли рука B випрямилась,
# і B біжить далі сам (у канати — кліп закінчується бігом, далі гра ставить Rope_Back_*; у кут — B розвертається
# спиною й б'ється об кут, далі стоїть, спершись). Сила кидка — у розвороті A, не в руці.
# Пара — як у Center_*: A у нулі обличчям у −Y, B навпроти обличчям до A (зсув (0, −GAP), 180°). У просторі B
# він біжить у свій −Y (повз A з правого боку A), кут — попереду за CORNER_RUN м.

WHIP_GAP = 0.85
WHIP_GRAB, WHIP_PULL = 0.3, 0.42          # с: хват зап'ястя; ведення до відпускання
CORNER_RUN = 2.2                           # м від точки відпускання до удару спиною об кут


class Whip:
    """Варіант кидка. kind: "ropes" — B біжить у канати (кінець — біг); "corner" — у кут (кінець — corner_lean);
    "short" — A не відпускає й смикає назад (B падає на спину). speed — множник швидкості бігу B; two — хват обома;
    resist — B упирається перед відпусканням (с); stumble — спотикається після відпускання; hit — "back"/"chest"/
    "slump" для кута; side — +1 B проходить повз праве плече A, −1 — повз ліве."""
    def __init__(self, name, kind="ropes", speed=1.0, two=False, resist=0.0, stumble=0.0, hit="back", side=1, L=None):
        self.name, self.kind, self.speed, self.two, self.resist, self.stumble, self.hit, self.side = \
            name, kind, speed, two, resist, stumble, hit, side
        self.t_rel = WHIP_GRAB + WHIP_PULL + resist
        self.L = L or (self.t_rel + (1.3 if kind == "ropes" else 2.3 if kind == "corner" else 1.6) / max(0.7, speed))


WHIPS = [
    Whip("Whip_Ropes"),
    Whip("Whip_Ropes_Fast", speed=1.3),
    Whip("Whip_Ropes_Two_Hand", two=True),
    Whip("Whip_Ropes_Resist", resist=0.45),
    Whip("Whip_Ropes_Stumble", stumble=1.0, speed=0.85),
    Whip("Whip_Ropes_Tired", speed=0.72),
    Whip("Whip_Ropes_Left", side=-1),
    Whip("Whip_Short_Arm", kind="short"),
    Whip("Whip_Corner", kind="corner"),
    Whip("Whip_Corner_Fast", kind="corner", speed=1.3),
    Whip("Whip_Corner_Chest", kind="corner", hit="chest"),
    Whip("Whip_Corner_Slump", kind="corner", hit="slump", speed=1.15),
    Whip("Whip_Corner_Two_Hand", kind="corner", two=True),
    Whip("Whip_Corner_Resist", kind="corner", resist=0.45),
    Whip("Whip_Corner_Left", kind="corner", side=-1),
]

WHIP_RUN = RUN_LOOPS["Run_Jog_Loop"]


def _corner_t_turn(W):
    """Мить розвороту: коли біг (з розгоном) справді пройшов CORNER_RUN − 0,4 м (далі — розворот на місці)."""
    vv = WHIP_RUN.stride / WHIP_RUN.L
    t = W.t_rel
    while vv * _whip_run_tau(t, W) < CORNER_RUN - 0.4 and t < W.t_rel + 5.0:
        t += 0.005
    return t


def _whip_run_tau(t, W):
    """Пройдений «час бігу» після відпускання: розгін з місця (як Run_Start), далі рівна швидкість."""
    T = 0.45
    u = max(0.0, t - W.t_rel) * W.speed
    return u * u / (2 * T) if u < T else u - T / 2


def _run_from(tau, W, x0, y0):
    """Біг B у свій −Y з точки (x0, y0); стопи й коліна — з циклу, зсунуті разом з тазом."""
    p = run_pose(tau, WHIP_RUN)
    d = Vector((x0, y0, 0.0))
    p.pelvis = p.pelvis + d
    p.ankles = tuple(a + d for a in p.ankles)
    p.knees = tuple(k + d for k in p.knees)
    return p


def _place(p, x, y, yaw):
    """Поза «у себе» — у точку (x, y) з поворотом yaw: таз і руки (orient), стопи, коліна й носки — разом."""
    R = Rz(yaw)
    d = Vector((x, y, 0.0))
    orient(p, (x, y), yaw)
    if p.ankles:
        p.ankles = tuple(d + R @ a for a in p.ankles)
    if p.knees:
        p.knees = tuple(d + R @ k for k in p.knees)
    if p.foot_rot:
        p.foot_rot = tuple(Quaternion(UP, math.radians(yaw)) @ q for q in p.foot_rot)
    return p


def hands_out_of_torso(p, r=0.2):
    """Цілі кистей — поза тулубом (вісь від лінії плечей униз уздовж хребта): на переходах між позами кисть ішла крізь живіт."""
    hs = {}
    for s_, (g_, a_, b_) in p.hands.items():
        def h(S, g_=g_):
            pt = g_(S) if callable(g_) else g_
            mid = (S["l"] + S["r"]) * 0.5
            down = (S["chest"] - mid).normalized()
            top = mid + down * 0.08
            u = (pt - top).dot(down)
            if 0.0 <= u <= 0.55:
                c_ = top + down * u
                d_ = pt - c_
                if d_.length < r:
                    pt = c_ + (d_.normalized() if d_.length > 1e-4 else FWD) * r
            return pt
        hs[s_] = (h, a_, b_)
    p.hands = hs
    return p


def whip_b(W):
    sd = W.side

    def f(t, L, partner=None, _qonly=False):
        p = stance(t, L, sway=0.0)
        grab = smooth(min(1.0, max(0.0, (t - 0.1) / (WHIP_GRAB - 0.1))))
        pull = smooth(min(1.0, max(0.0, (t - WHIP_GRAB - W.resist) / WHIP_PULL)))
        # ведений за руку: два кроки вперед-убік (повз плече A), тулуб нахилений за рукою
        x1, y1 = 0.75 * sd, -0.3                         # обходить A на відстані руки (0,5 — рука в руку)
        p.ankles = steps(t, [(0.0, (0.2, -0.105)), (WHIP_GRAB + W.resist, (0.2, -0.105)),
                             (WHIP_GRAB + W.resist + 0.2, (0.2 + x1 * 0.6, -0.105 + y1 * 0.6))],
                            [(0.0, (-0.2, 0.075)), (WHIP_GRAB + W.resist + 0.16, (-0.2, 0.075)),
                             (W.t_rel, (-0.2 + x1, 0.075 + y1))])
        p.pelvis = p.pelvis + Vector((x1 * pull, y1 * pull, -0.03 * pull))
        p.pitch = p.pitch + 10.0 * pull
        if W.resist:                                     # упирається: відхилився назад, п'яти в мат
            r = track(t, [(0.0, 0.0), (WHIP_GRAB, 0.0), (WHIP_GRAB + 0.15, 1.0), (WHIP_GRAB + W.resist, 1.0),
                          (WHIP_GRAB + W.resist + 0.12, 0.0), (99.0, 0.0)])
            p.pitch = p.pitch - 22.0 * r
            p.pelvis = p.pelvis + Vector((0.0, 0.06 * r, -0.04 * r))
        near = "l" if sd > 0 else "r"
        g = 1 if near == "l" else -1
        reach = lambda S: S[near] + Vector((-0.16 * g, -0.42, -0.20))        # рука витягнута до кисті A (не в його тулуб)
        hold = p.hands[near]
        p.hands = dict(p.hands)
        p.hands[near] = (lambda S: lerp(hold[0](S), reach(S), grab), hold[1], hold[2])
        p.elbows = dict(p.elbows)
        el = p.elbows[near]
        p.elbows[near] = lambda S: el(S).lerp(S[near] + Vector((0.3 * g, 0.0, -0.3)), grab)
        if t <= W.t_rel:
            return p
        if W.kind == "short":                            # A не відпустив — ривок назад, B падає на спину
            tb = t - W.t_rel
            hit = clip_bump_back(min(BUMP_LEN, tb * 1.1), BUMP_LEN)
            hit = _shift(hit, y1)
            d = Vector((x1, 0.0, 0.0))
            hit.pelvis = hit.pelvis + d
            hit.ankles = tuple(a + d for a in hit.ankles) if hit.ankles else hit.ankles
            hit.knees = tuple(k + d for k in hit.knees) if hit.knees else hit.knees
            out = blend_pose(_filled(p), _filled(hit), smooth(tb / 0.12))
            if hit.ankles and p.ankles:                  # стопи — з того місця, де стоять, і рухаються лише як у падінні
                h0 = _shift(clip_bump_back(0.0, BUMP_LEN), y1)
                h0a = tuple(a + d for a in h0.ankles)
                m = smooth(min(1.0, max(0.0, (tb - 0.45) / 0.35)))     # лежачи — ноги як у вузлі «на спині»
                out.ankles = tuple(lerp(pa + (ha - h0a_), ha, m) for pa, ha, h0a_ in zip(p.ankles, hit.ankles, h0a))
                if hit.knees:
                    out.knees = tuple(lerp(k + (pa - h0a_), k, m) for k, pa, h0a_ in zip(hit.knees, p.ankles, h0a))
            return out
        tau = _whip_run_tau(t, W)
        r = _run_from(tau, W, x1, y1)
        if W.stumble:                                    # спотикається: перший крок важкий, тулуб уперед
            s_ = track(t, [(W.t_rel, 0.0), (W.t_rel + 0.25, 1.0), (W.t_rel + 0.6, 0.0), (99.0, 0.0)]) * W.stumble
            r.pitch = r.pitch + 18.0 * s_
            r.pelvis = r.pelvis + Vector((0.0, 0.0, -0.06 * s_))
        w = smooth((t - W.t_rel) / 0.2)
        fa = p.ankles
        q = blend_pose(_filled(p), _filled(r), w)
        if w < 1.0 and fa:                               # стопи — кроком з відривом, а не ковзанням до циклу бігу
            hw = smooth(min(1.0, max(0.0, (w - 0.1) / 0.8)))      # горизонталь — лише після відриву
            q.ankles = tuple(lerp(a_, b_, hw) + Vector((0.0, 0.0, 0.08 * math.sin(math.pi * w) * min(1.0, (a_ - b_).length / 0.03)))
                             for a_, b_ in zip(fa, r.ankles))
        if W.kind != "corner" or _qonly:
            return q if _qonly else hands_out_of_torso(q)
        # у кут: біг → розворот спиною за 0,25 с → удар спиною (або грудьми) → спирається на кут
        v = WHIP_RUN.stride / WHIP_RUN.L * W.speed
        t_turn = _corner_t_turn(W)
        if t < t_turn:
            return hands_out_of_torso(q)
        q = f(t_turn, L, partner, True)                  # біг зупиняється на розвороті: далі стопи ведуть лише кроки розвороту
        y_end = y1 - WHIP_RUN.stride / WHIP_RUN.L * _whip_run_tau(t_turn, W)   # де біг зупинився: розворот — на місці
        turn = 0.0 if W.hit == "chest" else smooth((t - t_turn) / 0.25)
        t_hit = t_turn + 0.3
        jolt = hit_spring(t, t_hit, w=14.0, z=0.3)
        c = corner_lean(t, L, 1.0)
        set_feet(c, (foot(0.2, -0.105), 10.0), (foot(-0.2, 0.075), -14.0))   # коліна разом зі стопами — інакше після розвороту назад
        if W.hit == "chest":                             # грудьми: відлітає на крок назад і хитається
            c = stance(t, L, sway=0.0)
            set_feet(c, (foot(0.2, -0.105), 10.0), (foot(-0.2, 0.075), -14.0))
            c.pitch = c.pitch - 12.0 * jolt
            c.head_pitch = c.head_pitch - 20.0 * jolt
        if W.hit == "slump":                             # сповзає по куту на сідниці
            sl = smooth((t - t_hit - 0.3) / 0.6)
            c.pelvis = c.pelvis + Vector((0.0, 0.05 * sl, -0.42 * sl * BODY["leg"]))
            c.knees = (c.ankles[0] + Vector((0.08, -0.9, 0.45)), c.ankles[1] + Vector((-0.08, -0.9, 0.45))) if c.ankles else c.knees
            c.head_pitch = c.head_pitch + 25.0 * sl
        c.pelvis = c.pelvis + Vector((0.0, -0.05 * jolt, 0.0))
        ank_loc, rot_loc = c.ankles, c.foot_rot
        _place(c, x1, y_end, 180.0 * turn)
        d_end = Vector((x1, y_end, 0.0))                 # стопи ставляться одразу туди, де стоятимуть після розвороту
        ank_fin = tuple(d_end + Rz(0.0 if W.hit == "chest" else 180.0) @ a for a in ank_loc) if ank_loc else None
        k = smooth((t - t_turn) / 0.3)
        out = blend_pose(_filled(q), _filled(c), k)
        if c.knees and c.ankles and out.ankles:          # полюс коліна — разом з розворотом (без нього права коліном назад, 176°)
            qd = (Vector((0.22, -0.9, 0.45)), Vector((-0.22, -0.9, 0.45)))
            if q.knees and q.ankles:
                qd = tuple(kn - an for kn, an in zip(q.knees, q.ankles))
            out.knees = tuple(an + lerp(qd[i], c.knees[i] - c.ankles[i], k) for i, an in enumerate(out.ankles))
        if 0.0 < k < 1.0:                                # на розвороті кисті йдуть дугою назовні (від свого плеча), не крізь тулуб
            arc = math.sin(math.pi * k)
            hs = {}
            for s_, (g_, a_, b_) in out.hands.items():
                def h(S, g_=g_, s_=s_):
                    side = S[s_] - (S["l"] + S["r"]) * 0.5
                    side = Vector((side.x, side.y, 0.0)).normalized()
                    return g_(S) + side * 0.28 * arc + Vector((0.0, 0.0, 0.08 * arc))
                hs[s_] = (h, a_, b_)
            out.hands = hs
            def el_out(e_, s_):
                def g(S):
                    d_ = S[s_] - (S["l"] + S["r"]) * 0.5
                    return e_(S) + Vector((d_.x, d_.y, 0.0)).normalized() * 0.3 * arc
                return g
            out.elbows = {s_: el_out(out.elbows[s_], s_) for s_ in "lr"}
        hands_out_of_torso(out, r=0.2 + 0.1 * math.sin(math.pi * k))
        if k < 1.0 and q.ankles and c.ankles:           # розворот — переступанням з відривом, не ковзанням
            ph = ((0.0, 0.6), (0.4, 1.0))
            def stp(a_, b_, u):                          # спершу переноситься в повітрі, сідає вже на місці (без ковзання)
                h = smooth(min(1.0, max(0.0, (u - 0.05) / 0.65)))
                v = smooth(min(1.0, max(0.0, (u - 0.7) / 0.3)))
                xy = lerp(a_, b_, h)
                z = a_.z + (b_.z - a_.z) * v + 0.10 * math.sin(math.pi * min(1.0, u / 0.85)) * (1.0 - v)
                return Vector((xy.x, xy.y, max(z, b_.z)))
            out.ankles = tuple(stp(a_, b_, min(1.0, max(0.0, (k - p0) / (p1 - p0))))
                               for (a_, b_), (p0, p1) in zip(zip(q.ankles, ank_fin or c.ankles), ph))
            if c.knees and c.ankles:
                out.knees = tuple(an + (kn - ca) for an, kn, ca in zip(out.ankles, c.knees, c.ankles))
        return out
    return f


def whip_a(W):
    sd = W.side

    def f(t, L, partner=None):
        p = stance(t, L, sway=0.0)
        grab = smooth(min(1.0, max(0.0, (t - 0.1) / (WHIP_GRAB - 0.1))))
        piv = smooth(min(1.0, max(0.0, (t - WHIP_GRAB - W.resist) / (WHIP_PULL + 0.1))))
        yaw = -75.0 * sd * piv                          # розворот на лівій (опорній) — за B
        t_off = W.t_rel + (0.2 if W.kind == "short" else 0.0)      # короткий: тримає, доки смикає, і відпускає на падінні
        rel = smooth(min(1.0, max(0.0, (t - t_off + 0.14) / 0.2)))  # пальці розтискаються, поки B ще поруч (не тягнеться за ним)
        st = p
        orient(p, (0.0, 0.0), yaw)
        # стопи — кроками: права обходить по дузі, ліва довертається на місці (без ковзання)
        Ry = lambda a: Rz(a)
        fl0, fr0 = Vector((0.2, -0.105)), Vector((-0.2, 0.075))
        fr1 = (Ry(-75.0 * sd) @ Vector((-0.2, 0.075, 0.0))).xy
        fl1 = (Ry(-75.0 * sd) @ Vector((0.2, -0.105, 0.0))).xy
        an = steps(t, [(0.0, tuple(fl0)), (WHIP_GRAB + W.resist + 0.25, tuple(fl0)), (W.t_rel + 0.15, tuple(fl1))],
                      [(0.0, tuple(fr0)), (WHIP_GRAB + W.resist, tuple(fr0)), (WHIP_GRAB + W.resist + 0.3, tuple(fr1))])
        yl_ = 10.0 - 75.0 * sd * smooth(min(1.0, max(0.0, (t - WHIP_GRAB - W.resist - 0.31) / (W.t_rel + 0.12 - WHIP_GRAB - W.resist - 0.31))))
        yr_ = -14.0 - 75.0 * sd * smooth(min(1.0, max(0.0, (t - WHIP_GRAB - W.resist) / 0.3)))
        set_feet(p, (an[0], yl_), (an[1], yr_))           # стопи, носки й коліна разом (коліна None ламали стегно на 78°)
        p.pitch = p.pitch - 6.0 * piv * (1 - rel)        # відкидається назад — сила з корпусу
        near = "r" if sd > 0 else "l"                    # тримає ближню до B руку його зап'ястя
        other = "l" if near == "r" else "r"
        wrist = lambda S: P(S, "hand_l" if sd > 0 else "hand_r", 0.0, Vector((0.0, 0.0, 0.065)))
        hold = p.hands[near]
        def gn(S):
            w_ = wrist(S)
            g0 = hold[0](S)
            if w_ is None:
                return g0
            tgt = lerp(g0, lerp(w_, g0, rel), grab)
            sh = S[near]
            v = tgt - sh
            m = 0.47 * BODY["arm"]                          # не далі, ніж дістає рука: інакше IK тягне її в струну
            return sh + v.normalized() * m if v.length > m else tgt
        p.hands = dict(p.hands)
        p.hands[near] = (gn, hold[1], hold[2])
        if W.two:                                        # друга кисть — на передпліччі B, ближче до ліктя
            ho = p.hands[other]
            fa = lambda S: P(S, "lowerarm_l" if sd > 0 else "lowerarm_r", 0.4, Vector((0.0, 0.0, 0.075)))
            def go(S):
                tgt = lerp(ho[0](S), lerp(fa(S) or ho[0](S), ho[0](S), rel), grab)
                v = tgt - S[other]
                m = 0.47 * BODY["arm"]
                return S[other] + v.normalized() * m if v.length > m else tgt
            p.hands[other] = (go, ho[1], ho[2])
        p.forced = set()                                 # кисть за передпліччям: силоміць повернута давала скрут 40°
        if W.kind == "short" and t > W.t_rel:            # смикає назад обома руками
            s_ = track(t, [(W.t_rel, 0.0), (W.t_rel + 0.15, 1.0), (W.t_rel + 0.5, 0.0), (99.0, 0.0)])
            p.pitch = p.pitch - 10.0 * s_
        return p
    return f


for _w in WHIPS:
    MOVE_CLIPS.append((_w.name, _w.L, whip_a(_w), whip_b(_w), False, Vector((0.0, -WHIP_GAP, 0.0)), 180))
    MOVE_CLIP_NODES[_w.name] = {"A": ("stance", "stance"),
                                "B": ("stance", "corner_lean" if _w.kind == "corner" and _w.hit == "back" else
                                      "lying_back" if _w.kind == "short" else
                                      "stance" if _w.kind == "corner" and _w.hit == "chest" else None)}
    ev = [("grab", WHIP_GRAB, "hand_" + ("r" if _w.side > 0 else "l"), "hand_" + ("l" if _w.side > 0 else "r")),
          ("release", _w.t_rel, "-", "-")]
    if _w.kind == "corner":
        ev.append(("corner_hit", _corner_t_turn(_w) + 0.3, "spine_03", "corner"))
    MOVE_EVENTS[_w.name] = ev
    MOVE_CONTACTS[_w.name] = {(a_, b_) for a_ in ("hand_r", "hand_l", "lowerarm_r", "lowerarm_l")
                              for b_ in ("hand_l", "hand_r", "lowerarm_l", "lowerarm_r")}
