# Дії по центру після відскоку від канатів — 15 (власник 29.10.2026: «біг від канатів з діями по центру ринга …
# Усіх варіант по 15»; план ітерації 1: «завжди з реакцією жертви»). Виконується всередині ring_anims.py (exec) після
# ring_anims_sell.py — бере цикл бігу (ring_anims_run.py) і бампи (Bump_Back / Bump_Front / Bump_Side).
#
# Будова (записи матчів і навчальні матеріали, своїми словами): нападник вилітає з канатів бігом; удар — у мить, коли
# тіло ще несе швидкість (лікоть, передпліччя, плече, рука-«шлагбаум» на рівні шиї), або нападник гальмує й б'є
# ногою чи коліном з місця; жертва приймає удар і падає (на спину, на бік, обличчям донизу), відходить на ногах
# («продаж» удару) або ухиляється — і нападник пробігає повз. Пара — як у інших парних кліпів: A у нулі обличчям у −Y,
# B стоїть у центрі обличчям до A; кліп починається з бігу (вузла немає: гра змішує з кінця відскоку), A закінчує
# в стійці там, де зупинився, B — у вузлі свого бампа або в стійці.

CT_RUN = RUN_LOOPS["Run_Jog_Loop"]                # 2,5 м/с: з бігу 3,9 м/с A вилітав після удару на край рингу
CT_HEAVY = RUN_LOOPS["Run_Heavy_Loop"]


class Ctr:
    """Дія по центру. a — дія A ("block", "line", "line_l", "elbow", "forearm", "boot", "knee", "head", "nosell");
    b — реакція B ("back", "back_fast", "side", "front", "stagger", "sidestep", "duck"); bx — зсув B убік від лінії
    бігу A (м, кадр A: − праворуч від A); pass_ — A пробігає повз (інакше гальмує до удару); gap — від таза A до таза B
    у мить контакту."""
    def __init__(self, name, a, b, bx, pass_=True, gap=0.5, run=None, L=2.6, t_hit=0.62):
        self.name, self.a, self.b, self.bx, self.pass_, self.gap = name, a, b, bx, pass_, gap
        self.run, self.L, self.t_hit = run or CT_RUN, L, t_hit
        if pass_:
            self.t0, self.td = t_hit + 0.3, 0.85                # тримає швидкість крізь контакт, гальмує далеко за B
                                                                 # (поруч із головою й руками B на маті)
        else:
            self.t0, self.td = t_hit - 0.3, 0.45                 # гальмує до удару, б'є майже з місця
        v = self.run.stride / self.run.L
        self.v = v
        self.y_hit = -v * self.tau(t_hit)
        self.t_stop = self.t0 + self.td
        self.y_stop = -v * self.tau(self.t_stop)

    def tau(self, t):
        if t <= self.t0:
            return t
        u = min(t - self.t0, self.td)
        return self.t0 + u - u * u / (2 * self.td)

    def b_run(self):
        """Скільки B пробігає сам до контакту (шолдер такл — біжать обидва)."""
        return self.v * self.t_hit if self.b == "run_back" else 0.0

    def b_offset(self):
        return Vector((self.bx, self.y_hit - self.gap - self.b_run(), 0.0))


CENTER = [
    Ctr("Center_ShoulderBlock", "block", "back", 0.64, gap=0.42),
    Ctr("Center_Clothesline", "line", "back", -0.66),
    Ctr("Center_Clothesline_Flip", "line", "front", -0.58),         # розвертає — падає обличчям, позаду A
    Ctr("Center_Lariat_Left", "line_l", "side", 0.62),
    Ctr("Center_RunningElbow", "elbow", "side", 0.62),            # лівим ліктем; B падає на бік — геть від лінії бігу
    Ctr("Center_ForearmShiver", "forearm", "stagger", -0.3, pass_=False, gap=0.72),
    Ctr("Center_BigBoot", "boot", "back", -0.22, pass_=False, gap=1.02),
    Ctr("Center_RunningKnee", "knee", "back", -0.26, pass_=False, gap=0.85),
    Ctr("Center_ShoulderBlock_NoSell", "nosell", "stagger", 0.12, pass_=False, gap=0.75),
    Ctr("Center_Sidestep", "line", "sidestep", -0.42),
    Ctr("Center_Duck", "line", "duck", -0.58),
    Ctr("Center_Headbutt", "head", "stagger", 0.0, pass_=False, gap=0.72),
    Ctr("Center_ShoulderBlock_Heavy", "block", "back_fast", 0.64, gap=0.42, run=CT_HEAVY),
    Ctr("Center_BigBoot_Stagger", "boot", "stagger", -0.22, pass_=False, gap=1.02),
    Ctr("Center_Lariat_Heavy", "line", "back", -0.58, run=CT_HEAVY),
    # шолдер такл: обидва біжать назустріч, плече в плече — B збито з ніг на ходу
    Ctr("Center_ShoulderTackle", "block", "run_back", 0.64, gap=0.42),
]


def _shift(p, dy):
    """Зсунути позу вздовж Y (таз, стопи, коліна)."""
    d = Vector((0.0, dy, 0.0))
    p.pelvis = p.pelvis + d
    if p.ankles:
        p.ankles = (p.ankles[0] + d, p.ankles[1] + d)
    if p.knees:
        p.knees = (p.knees[0] + d, p.knees[1] + d)
    return p


def _act_w(t, c, before=0.22, after=0.3):
    """Вага дії: наростає до контакту, тримається мить, спадає."""
    return track(t, [(0.0, 0.0), (c.t_hit - before, 0.0), (c.t_hit - 0.04, 1.0), (c.t_hit + 0.08, 1.0),
                     (c.t_hit + after, 0.0), (99.0, 0.0)])


def center_a(c):
    def f(t, L, partner=None):
        p = run_pose(c.tau(t), c.run)
        # після зупинки — стійка там, де став: стопи переходять у стійку коротким кроком (з підйомом, без ковзання)
        k = smooth((t - c.t_stop) / 0.4) if t > c.t_stop else 0.0
        if k > 0:
            st = stance(t, L, sway=0.0)
            _shift(st, p.pelvis.y)
            frozen = p.ankles
            q = blend_pose(p, st, k)
            def step(a_, b_, w):
                m = lerp(a_, b_, smooth(w))                       # уперед — посередині підйому, не біля мату
                return m + Vector((0.0, 0.0, 0.07 * math.sin(math.pi * w) * min(1.0, (a_ - b_).length / 0.015)))
            dy = Vector((0.0, p.pelvis.y, 0.0))
            sl, sr_ = foot(0.2, -0.105) + dy, foot(-0.2, 0.075) + dy           # стопи стійки там, де став
            q.ankles = (step(frozen[0], sl, k), step(frozen[1], sr_, k))
            st_rot = (Quaternion(UP, math.radians(10.0)), Quaternion(UP, math.radians(-14.0)))
            q.foot_rot = tuple(fr.slerp(r_, k) for fr, r_ in zip(p.foot_rot, st_rot))
            q.knees = None if k > 0.5 else p.knees
            p = q
        w = _act_w(t, c)
        if c.a == "nosell" and t > c.t_hit:
            # B не зрушив — A відскакує від нього й падає на спину сам (той самий бамп, з того місця, де вдарився)
            bb = _shift(clip_bump_back(t - c.t_hit, BUMP_LEN), -c.v * c.tau(c.t_hit))
            r0 = run_pose(c.tau(c.t_hit), c.run)           # стопи — де стояли при ударі, доки не піднімуться
            u = smooth((t - c.t_hit - 0.2) / 0.22)
            if u < 1.0 and bb.ankles:
                bb.ankles = tuple(Vector((m.x, m.y, b_.z)) for m, b_ in ((lerp(f0, b_, u), b_) for f0, b_ in zip(r0.ankles, bb.ankles)))
            hb = blend_pose(_filled(p), _filled(bb), smooth((t - c.t_hit) / 0.12))
            hb.ankles = bb.ankles
            return hb
        if c.a in ("line", "line_l"):
            s_ = "r" if c.a == "line" else "l"
            sd = -1 if s_ == "r" else 1
            hold = p.hands[s_]
            arm = lambda S, s_=s_, sd=sd: S[s_] + Vector((0.55 * sd, -0.12, 0.02))        # рука вбік, на рівні шиї
            p.hands = dict(p.hands)
            p.hands[s_] = (lambda S, h=hold, a_=arm: lerp(h[0](S), a_(S), w), lambda S, h=hold: lerp(h[1] if not callable(h[1]) else h[1](S), Vector((sd, -0.2, 0.0)), w),
                           lambda S, h=hold: lerp(h[2] if not callable(h[2]) else h[2](S), UP, w))
            el = p.elbows[s_]
            p.elbows = dict(p.elbows)
            p.elbows[s_] = lambda S, e=el, s_=s_, sd=sd: e(S).lerp(S[s_] + Vector((0.3 * sd, 0.15, -0.15)), w)
            p.yaw = p.yaw + 14.0 * w * (1 if s_ == "r" else -1)
            p.fist = dict(p.fist); p.fist[s_] = 0.2 + 0.7 * w
        elif c.a in ("block", "nosell"):
            p.pitch = p.pitch + 14.0 * w
            p.yaw = p.yaw - 22.0 * w                              # ліве плече вперед
            p.roll = p.roll + 6.0 * w
        elif c.a == "elbow":                                  # лівий лікоть уперед на рівні голови B, кулак біля грудей
            hold = p.hands["l"]
            tgt = lambda S: S["l"] + Vector((0.0, -0.18, 0.04))
            p.hands = dict(p.hands)
            p.hands["l"] = (lambda S, h=hold: lerp(h[0](S), tgt(S), w), hold[1], hold[2])
            el = p.elbows["l"]
            p.elbows = dict(p.elbows)
            p.elbows["l"] = lambda S, e=el: e(S).lerp(S["l"] + Vector((0.32, -0.3, 0.08)), w)     # лікоть убік — до B
            p.yaw = p.yaw - 18.0 * w
        elif c.a == "forearm":
            hold = p.hands["r"]
            hit = lambda S: P(S, "spine_03", 0.7, Vector((0.14, 0.06, 0.02))) or (S["r"] + Vector((0.1, -0.55, -0.05)))
            p.hands = dict(p.hands)
            p.hands["r"] = (lambda S, h=hold: lerp(h[0](S), hit(S), w), lambda S, h=hold: lerp(h[1] if not callable(h[1]) else h[1](S), FWD + LEFT * 0.6, w),
                            lambda S, h=hold: lerp(h[2] if not callable(h[2]) else h[2](S), UP, w))
            el = p.elbows["r"]
            p.elbows = dict(p.elbows)
            p.elbows["r"] = lambda S, e=el: e(S).lerp(S["r"] + Vector((0.02, -0.18, -0.3)), w)
            p.yaw = p.yaw - 20.0 * w
            p.fist = dict(p.fist); p.fist["r"] = 0.85
        elif c.a == "head":                                   # лобом у груди, не в голову
            p.pitch = p.pitch + 30.0 * w
            p.head_pitch = p.head_pitch + 10.0 * w
        elif c.a in ("boot", "knee"):
            # опорна — ліва (стоїть там, де стала), права — удар уперед: підошвою в груди або коліном у живіт
            kw = _act_w(t, c, before=0.28, after=0.4)
            hip = REST["hip_r"] + p.pelvis
            if c.a == "boot":
                kick = hip + Vector((0.0, -0.8 * BODY["leg"], 0.3 * BODY["leg"]))           # підошва — у груди, не в голову
            else:
                kick = hip + Vector((0.0, -0.3 * BODY["leg"], 0.0))
            p.ankles = (p.ankles[0], lerp(p.ankles[1], kick, kw))
            if c.a == "boot":
                p.foot_rot = (p.foot_rot[0], p.foot_rot[1].slerp(Quaternion(RIGHT, math.radians(70.0)), kw))
                p.pitch = p.pitch - 14.0 * kw                     # відхиляється назад, як при ударі ногою
            else:
                p.knees = (p.knees[0] if p.knees else None, hip + Vector((0.0, -0.5 * BODY["leg"], 0.12)))
                p.pitch = p.pitch + 6.0 * kw
            if p.knees and p.knees[0] is None:
                p.knees = None
        return p
    return f


def _stagger_b(t, L, c):
    """«Продаж» удару на ногах — та сама реакція, що в удару передпліччям (відкидає, крок назад, назад у стійку)."""
    return clip_forearm_b(t - (c.t_hit - STRIKE_HIT), STRIKE_LEN)


def center_b(c):
    def f(t, L, partner=None):
        tb = t - (c.t_hit - (0.12 if c.pass_ else 0.04))  # бамп — на мить раніше удару (борці «приймають» його);
                                                          # від удару з місця — майже разом, інакше нога не дістає
        if c.b == "run_back":                            # біжить назустріч A, від удару — на спину з місця, де збили
            d = c.b_run()
            if tb <= 0:
                return run_pose(t, CT_RUN)
            hit = _shift(clip_bump_back(min(BUMP_LEN, tb * 1.2), BUMP_LEN), -d)
            r0 = run_pose(c.t_hit - 0.12, CT_RUN)              # стопи — де стояли в мить удару, доки не піднімуться
            u = smooth((tb - (0.12 if globals().get("FEMALE") else 0.2)) / 0.22)   # коротші ноги — відрив раніше (коліно під мат)
            if u < 1.0 and hit.ankles:
                hit.ankles = tuple(Vector((m.x, m.y, a_.z)) for m, a_ in
                                   ((lerp(f0, a_, u), a_) for f0, a_ in zip(r0.ankles, hit.ankles)))
            w = smooth(tb / 0.12)
            if w >= 1.0:
                return hit
            q = blend_pose(_filled(run_pose(min(t, c.t_hit - 0.12), CT_RUN)), _filled(hit), w)
            q.ankles = hit.ankles
            return q
        if c.b in ("back", "back_fast"):
            if tb <= 0:
                return stance(t, L, sway=0.0)
            sp = 1.2 if c.b == "back_fast" else 1.0          # «перевертає» — падає швидше й різкіше
            return clip_bump_back(min(BUMP_LEN, tb * sp), BUMP_LEN)
        if c.b == "side":
            return stance(t, L, sway=0.0) if tb <= 0 else clip_bump_side(min(SBUMP_LEN, tb), SBUMP_LEN)
        if c.b == "front":
            return stance(t, L, sway=0.0) if tb <= 0 else clip_bump_front(min(FBUMP_LEN, tb), FBUMP_LEN)
        if c.b == "stagger":
            return _stagger_b(t, L, c) if t > c.t_hit - STRIKE_HIT else stance(t, L, sway=0.0)
        p = stance(t, L, sway=0.0)
        if c.b == "sidestep":                                # крок убік від лінії бігу A — і лишається там
            o = 1.0 if c.bx < 0 else -1.0                     # простір B: + — його ліво (= праворуч від A)
            x = 0.36 * o
            p.ankles = steps(t, [(0.0, (0.2, -0.105)), (c.t_hit - 0.35, (0.2, -0.105)), (c.t_hit - 0.15, (0.2 + x, -0.105))],
                                [(0.0, (-0.2, 0.075)), (c.t_hit - 0.3, (-0.2, 0.075)), (c.t_hit - 0.02, (-0.2 + x, 0.075))])
            sh = track(t, [(0.0, 0.0), (c.t_hit - 0.35, 0.0), (c.t_hit - 0.05, 1.0), (99.0, 1.0)])
            lk = track(t, [(0.0, 0.0), (c.t_hit - 0.3, 0.0), (c.t_hit, 1.0), (c.t_hit + 0.6, 1.0), (c.t_hit + 1.1, 0.0), (99.0, 0.0)])
            p.pelvis = p.pelvis + Vector((x * sh, 0.0, -0.03 * lk))
            p.head_yaw = -30.0 * o * lk                           # проводить A поглядом
        elif c.b == "duck":                                  # пірнає під руку: присід, корпус уперед, голова вниз
            d = track(t, [(0.0, 0.0), (c.t_hit - 0.3, 0.0), (c.t_hit - 0.08, 1.0), (c.t_hit + 0.2, 1.0), (c.t_hit + 0.6, 0.0), (99.0, 0.0)])
            p.pelvis = p.pelvis + Vector((0.0, 0.08 * d, -0.3 * d * BODY["leg"]))
            p.pitch = p.pitch + 34.0 * d
            p.head_pitch = p.head_pitch + 20.0 * d
            p.knees = (foot(0.2, -0.105) + Vector((0.05, -0.6, 0.45)), foot(-0.2, 0.075) + Vector((-0.05, -0.6, 0.45)))
        return p
    return f


_B_END = {"run_back": "lying_back", "back": "lying_back", "back_fast": "lying_back", "front": "lying_front", "side": None,
          "stagger": "stance", "sidestep": "stance", "duck": "stance"}
for _c in CENTER:
    MOVE_CLIPS.append((_c.name, _c.L, center_a(_c), center_b(_c), False, _c.b_offset(), 180))
    MOVE_CLIP_NODES[_c.name] = {"A": (None, "lying_back" if _c.a == "nosell" else "stance"),
                                "B": (None if _c.b == "run_back" else "stance", _B_END[_c.b])}
    CLIP_EVENTS[_c.name] = [("contact", _c.t_hit, "spine_03", "spine_03")] if _c.b not in ("sidestep", "duck") else []
    _hit = {"line": ("upperarm_r", "lowerarm_r", "hand_r"), "line_l": ("upperarm_l", "lowerarm_l", "hand_l"),
            "elbow": ("upperarm_l", "lowerarm_l"), "forearm": ("lowerarm_r", "hand_r"), "block": ("upperarm_l", "spine_03"),
            "nosell": ("upperarm_l", "spine_03"), "head": ("head", "neck_01"), "boot": ("foot_r", "calf_r"),
            "knee": ("thigh_r", "calf_r")}[_c.a]
    CLIP_CONTACTS[_c.name] = {(a_, b_) for a_ in _hit                               # лише кінцівка, що б'є, — у тулуб і голову
                              for b_ in ("spine_03", "spine_02", "spine_01", "neck_01", "head", "upperarm_l", "upperarm_r")}
