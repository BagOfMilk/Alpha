# Робота в куті — 15 варіантів (план ітерації 2, група 2; власник 04.11.2026: «Чому немає взаємодії з вуглами?»).
# Виконується всередині ring_anims.py (exec) після ring_anims_whip.py. Карта — tools/blender/moves/Corner_Work.json.
#
# Будова (записи матчів, своїми словами): B спирається спиною на кут (вузол corner_lean — після Whip_Corner), A
# перед ним. Серії: удари кулаком у голову (голова B відлітає вбік і назад, рефері рахує до п'яти — A відходить),
# відкритою долонею в груди, плечем у живіт (A нахилений, B складається навпіл), ногою в ребра; з розбігу — тілом,
# коліном, ногою, рукою-«шлагбаумом»; B після важкого удару сповзає або виходить з кута, хитаючись. Кожен удар —
# замах, контакт (B відкидає назад у подушку й повертає пружиною), відведення. A у нулі обличчям у −Y, B — у куті
# навпроти обличчям до A (зсув (0, −gap), 180°).


class Corn:
    """kind: "punch"/"chop"/"forearm"/"shoulder"/"kick" — серія з місця (n ударів); "run_*" — з розбігу (splash, knee,
    boot, line); "break" — A відходить на рахунок рефері; "escape" — B пірнає під руку A й виходить з кута.
    end_b: вузол B наприкінці (corner_lean / stance / None — сповзає)."""
    def __init__(self, name, kind, n=3, gap=0.62, slump=False, L=None):
        self.name, self.kind, self.n, self.gap, self.slump = name, kind, n, gap, slump
        self.run = kind.startswith("run_")
        self.beat = 0.55 if kind in ("punch", "chop", "forearm") else 0.7
        self.t0 = 0.35
        self.hits = [self.t0 + 0.25 + i * self.beat for i in range(n)] if not self.run else [1.15]
        self.L = L or (self.hits[-1] + (1.4 if slump else 0.9))


CORNERS = [
    Corn("Corner_Punches", "punch", n=4),
    Corn("Corner_Chops", "chop", n=3),
    Corn("Corner_Forearms", "forearm", n=3),
    Corn("Corner_Shoulders", "shoulder", n=3, gap=0.5),
    Corn("Corner_Kicks", "kick", n=3, gap=0.85),
    Corn("Corner_Punches_Ten", "punch", n=6),
    Corn("Corner_Running_Splash", "run_splash", gap=0.42),
    Corn("Corner_Running_Knee", "run_knee", gap=0.6),
    Corn("Corner_Running_Boot", "run_boot", gap=0.95),
    Corn("Corner_Running_Line", "run_line", gap=0.55),
    Corn("Corner_Running_Splash_Slump", "run_splash", gap=0.42, slump=True),
    Corn("Corner_Running_Knee_Slump", "run_knee", gap=0.6, slump=True),
    Corn("Corner_Chops_Slump", "chop", n=3, slump=True),
    Corn("Corner_Ref_Break", "break", n=2),
    Corn("Corner_Escape", "escape", n=1, gap=0.7),
]



def _corner_b_base(t, L, C):
    c = corner_lean(t, L, 1.0)
    set_feet(c, (foot(0.2, -0.105), 10.0), (foot(-0.2, 0.075), -14.0))
    return c


def corner_b(C):
    def f(t, L, partner=None):
        p = _corner_b_base(t, L, C)
        if C.kind == "escape":                           # пірнає під праву руку A й виходить уздовж канату
            th = C.hits[0]
            d = track(t, [(0.0, 0.0), (th - 0.2, 0.0), (th, 1.0), (th + 0.25, 1.0), (th + 0.6, 0.0), (99.0, 0.0)])
            go = smooth((t - th) / 0.6)
            p.pelvis = p.pelvis + Vector((0.55 * go, -0.25 * go, -0.28 * d))
            p.pitch = p.pitch + 35.0 * d
            p.ankles = steps(t, [(0.0, (0.2, -0.105)), (th - 0.05, (0.2, -0.105)), (th + 0.25, (0.62, -0.32))],
                                [(0.0, (-0.2, 0.075)), (th + 0.2, (-0.2, 0.075)), (th + 0.55, (0.38, -0.2))])
            return p
        if C.kind == "break":
            return p
        jolt = 0.0
        for th in C.hits:
            jolt = max(jolt, hit_spring(t, th, w=16.0, z=0.35))
        head = C.kind in ("punch", "forearm", "run_line", "run_boot")
        p.pelvis = p.pelvis + Vector((0.0, 0.05 * jolt, 0.0))        # у подушку кута
        if head:
            p.head_pitch = p.head_pitch - 28.0 * jolt
            p.head_yaw = 30.0 * jolt * (1 if int((t - C.t0) / C.beat) % 2 == 0 else -1)
        else:                                                       # у груди / живіт — складається вперед
            p.pitch = p.pitch + (30.0 if C.kind in ("shoulder", "kick", "run_knee") else 12.0) * jolt
            p.head_pitch = p.head_pitch + 12.0 * jolt
        if C.slump:                                                 # сповзає по куту на сідниці
            th = C.hits[-1]
            sl = smooth((t - th - 0.25) / 0.7)
            p.pelvis = p.pelvis + Vector((0.0, 0.08 * sl, -0.5 * sl * BODY["leg"]))
            p.ankles = (foot(0.2, -0.105 - 0.35 * sl), foot(-0.2, 0.075 - 0.35 * sl))
            p.knees = (p.ankles[0] + Vector((0.12, -0.6, 0.6)), p.ankles[1] + Vector((-0.12, -0.6, 0.6)))
            p.head_pitch = p.head_pitch + 30.0 * sl
            p.pitch = p.pitch + 15.0 * sl
        return p
    return f


def _strike_hand(kind, s_, w, S):
    """Ціль кисті в мить удару (кістка партнера)."""
    if kind == "punch":
        return P(S, "head", 0.4, Vector((0.0, 0.0, 0.0)))
    if kind == "chop":
        return P(S, "spine_03", 0.6, Vector((0.0, 0.0, 0.0)))
    return P(S, "neck_01", 0.5, Vector((0.0, 0.0, 0.0)))        # передпліччям у шию


def corner_a(C):
    def f(t, L, partner=None):
        if C.run:
            v = CT_RUN.stride / CT_RUN.L
            th = C.hits[0]
            # біжить так, щоб у мить удару бути на відстані gap від B (B у нулі зсуву пари — шлях A уздовж −Y)
            tau = max(0.0, t)
            p = run_pose(min(tau, th), CT_RUN)
            w = _act_w(t, type("c", (), {"t_hit": th})(), before=0.25, after=0.4)
            if C.kind == "run_splash":                  # грудьми в груди, руки розкинуті на канати
                p.pitch = p.pitch - 6.0 * w
                p.hands = {s_: (lambda S, s_=s_: S[s_] + Vector((0.45 * (1 if s_ == "l" else -1), -0.3, 0.1)), FWD, UP) for s_ in "lr"}
            elif C.kind == "run_line":
                p.hands = dict(p.hands)
                hold = p.hands["r"]
                p.hands["r"] = (lambda S: lerp(hold[0](S), S["r"] + Vector((-0.55, -0.15, -0.05)), w), hold[1], hold[2])
            elif C.kind in ("run_knee", "run_boot"):
                hip = REST["hip_r"] + p.pelvis
                kick = hip + (Vector((0.0, -0.75 * BODY["leg"], 0.3 * BODY["leg"])) if C.kind == "run_boot"
                              else Vector((0.0, -0.3 * BODY["leg"], 0.0)))
                p.ankles = (p.ankles[0], lerp(p.ankles[1], kick, w))
                if C.kind == "run_knee":
                    p.knees = (p.knees[0], hip + Vector((0.0, -0.5 * BODY["leg"], 0.12)))
            if t > th:                                  # після удару — відходить на крок і в стійку
                k = smooth((t - th - 0.3) / 0.5)
                st = stance(t, L, sway=0.0)
                y0 = -v * th
                st = _shift(st, y0 + 0.35 * k)
                p = blend_pose(_filled(p), _filled(st), k)
            return p
        p = stance(t, L, sway=0.0)
        if C.kind == "escape":                           # замах правою — B пірнає під неї; A б'є в подушку й розвертається
            th = C.hits[0]
            w = _act_w(t, type("c", (), {"t_hit": th})(), before=0.3, after=0.4)
            hold = p.hands["r"]
            p.hands = dict(p.hands)
            p.hands["r"] = (lambda S: lerp(hold[0](S), S["r"] + Vector((0.1, -0.5, 0.15)), w), hold[1], hold[2])
            p.yaw = p.yaw - 15.0 * w
            p.head_yaw = 60.0 * smooth((t - th) / 0.5)
            return p
        if C.kind == "break":                            # рефері рахує — A відходить з піднятими руками
            k = smooth((t - 0.5) / 0.6)
            p.pelvis = p.pelvis + Vector((0.0, 0.4 * k, 0.04 * k))
            p.ankles = steps(t, [(0.0, (0.2, -0.105)), (0.5, (0.2, -0.105)), (0.8, (0.2, 0.15))],
                                [(0.0, (-0.2, 0.075)), (0.7, (-0.2, 0.075)), (1.1, (-0.2, 0.33))])
            up = smooth((t - 0.4) / 0.4)
            p.hands = {s_: (lambda S, s_=s_: S[s_] + Vector((0.12 * (1 if s_ == "l" else -1), -0.15, 0.32 * up)), FWD + UP, UP) for s_ in "lr"}
            p.pitch = p.pitch - 8.0 * up
            return p
        side = "r"
        acc = 0.0
        for i, th in enumerate(C.hits):
            s_ = "r" if (i % 2 == 0 or C.kind in ("chop", "shoulder", "kick")) else "l"
            w = track(t, [(0.0, 0.0), (th - 0.22, 0.0), (th - 0.03, 1.0), (th + 0.06, 1.0), (th + 0.3, 0.0), (99.0, 0.0)])
            if w > acc:
                acc, side = w, s_
        w = acc
        if C.kind in ("punch", "chop", "forearm"):
            hold = p.hands[side]
            wind = lambda S, s_=side: S[s_] + Vector((0.15 * (1 if s_ == "l" else -1), 0.12, 0.2 if C.kind == "chop" else 0.0))
            def g(S, s_=side, hold=hold):
                tgt = _strike_hand(C.kind, s_, w, S) or (S[s_] + Vector((0.0, -0.55, 0.05)))
                pre = lerp(hold[0](S), wind(S), smooth(min(1.0, w * 2.0)))
                return lerp(pre, tgt, smooth(max(0.0, w * 2.0 - 1.0)))
            p.hands = dict(p.hands)
            p.hands[side] = (g, hold[1], hold[2])
            p.yaw = p.yaw + 18.0 * w * (1 if side == "r" else -1)
            p.fist = dict(p.fist); p.fist[side] = 0.95 if C.kind == "punch" else 0.15
        elif C.kind == "shoulder":                       # нахил і плечем у живіт
            p.pitch = p.pitch + 40.0 * w
            p.pelvis = p.pelvis + Vector((0.0, -0.12 * w, -0.08 * w))
            p.yaw = p.yaw - 25.0 * w
        elif C.kind == "kick":                           # правою ногою в ребра
            hip = REST["hip_r"] + p.pelvis
            p.ankles = (foot(0.2, -0.105), lerp(foot(-0.2, 0.075), hip + Vector((-0.05, -0.55 * BODY["leg"], -0.25 * BODY["leg"])), w))
            p.pitch = p.pitch - 10.0 * w
        return p
    return f


for _c in CORNERS:
    # з розбігу: A пробігає v·t_удару по −Y — B стоїть так, щоб у мить удару між тазами було gap
    off = Vector((0.0, -(_c.gap + (CT_RUN.stride / CT_RUN.L * _c.hits[0] if _c.run else 0.0)), 0.0))
    MOVE_CLIPS.append((_c.name, _c.L, corner_a(_c), corner_b(_c), False, off, 180))
    MOVE_CLIP_NODES[_c.name] = {"A": (None if _c.run else "stance", "stance"),
                                "B": ("corner_lean", None if _c.slump else "stance" if _c.kind == "escape" else "corner_lean")}
    MOVE_EVENTS[_c.name] = [("contact", th, "-", "head" if _c.kind == "punch" else "spine_03") for th in _c.hits] + \
                           ([("corner_hit", _c.hits[0], "spine_03", "corner")] if _c.run else [])
    _hit = {"punch": ("hand_r", "hand_l"), "chop": ("hand_r",), "forearm": ("lowerarm_r", "lowerarm_l", "hand_r", "hand_l"),
            "shoulder": ("upperarm_l", "spine_03", "head"), "kick": ("foot_r", "calf_r"), "run_splash": ("spine_03", "spine_02", "upperarm_l", "upperarm_r"),
            "run_knee": ("thigh_r", "calf_r"), "run_boot": ("foot_r", "calf_r"), "run_line": ("upperarm_r", "lowerarm_r"),
            "break": (), "escape": ("hand_r",)}[_c.kind]
    MOVE_CONTACTS[_c.name] = {(a_, b_) for a_ in _hit for b_ in ("head", "neck_01", "spine_03", "spine_02", "spine_01", "pelvis",
                                                               "upperarm_l", "upperarm_r")}
