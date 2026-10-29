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
    def __init__(self, name, side=1, speed=1.0, sink=0.26, hit="side", grip="hand", ret=True, lean=7.0,
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
    face = {"side": -90.0 * sd, "back": -180.0, "chest": 0.0}[R.hit]
    ret_face = -180.0 if R.ret else face
    rope_y = -(D + 0.12)
    gap = {"side": 0.17, "back": 0.17, "chest": 0.22}[R.hit]       # таз від лінії каната в мить контакту
    T = dict(hit=0.66 * k, sink=0.8 * k, push=(0.94 + h) * k, end=(1.62 + h) * k if R.ret else (1.3 + h) * k)
    path_y = [(0.0, 0.0), (0.14 * k, -0.12), (0.5 * k, -(D - 0.55)), (T["hit"], rope_y + gap),
              (T["sink"], rope_y + gap - R.sink), (T["push"], rope_y + gap)]
    path_y += [(T["push"] + 0.18 * k, rope_y + gap + 0.55), (T["end"], -0.25)] if R.ret else [(T["end"], rope_y + gap + 0.3)]
    # повороти — по 0,3 с: швидше кисті «летять» разом із тілом (і на екрані це ривок)
    path_yaw = [(0.0, 0.0), (0.34 * k, 0.0), (0.64 * k, face), (T["push"], face)]
    path_yaw += [(T["push"] + 0.3 * k, ret_face), (T["end"], ret_face)] if R.ret else [(T["end"], face * 0.5)]
    def at(tt, lat, ahead, yaw_extra=0.0):
        y = track(tt, path_y)
        yw = track(tt, path_yaw) + yaw_extra
        off = Rz(yw) @ Vector((lat, -ahead, 0.0))
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

def rebound_pose(t, L, R):
    T, path_y, path_yaw, falls, face, rope_y = reb_plan(R)
    yaw = track(t, path_yaw)
    on = track(t, [(0.0, 0.0), (T["hit"] - 0.2, 0.0), (T["hit"], 1.0), (T["push"], 1.0), (T["push"] + 0.24, 0.0), (L, 0.0)])   # рука до каната — 0,2 с, не стрибком
    run = 1.0 - on
    ret_t = max(0.0, t - T["push"])
    duck = R.duck * smooth(min(1.0, ret_t / 0.2)) if R.ret else 0.0
    p = RPose(forced=set())
    bob = 0.025 * math.cos(2 * math.pi * t / 0.33) * run
    p.pelvis = Vector((0.0, track(t, path_y), -0.07 - bob - 0.06 * on - 0.06 * R.crouch * on - 0.03 * R.heavy - 0.08 * duck))
    p.pelvis_yaw = yaw
    hit_tilt = {"side": -2.0, "back": -14.0, "chest": 22.0}[R.hit]
    p.pitch = (R.lean + 4.0 * R.heavy) * run + hit_tilt * on + 30.0 * duck
    p.roll = (14.0 * R.side if R.hit == "side" else 0.0) * on       # плече в канати
    p.yaw = -6.0 * math.cos(2 * math.pi * t / 0.66) * run
    p.head_pitch = -6.0
    p.head_yaw = 0.0
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
            free = S[s_] + Rz(yaw) @ Vector((0.1 * sg, -0.06 - 0.16 * pump * sg, -0.32))
            grab = (s_ == near) if R.hit == "side" else True
            if not grab:
                tgt = S[s_] + Rz(yaw) @ (Vector((0.1 * sg, -0.12, 0.42)) if R.arm_up else Vector((0.14 * sg, -0.2, -0.18)))
                return lerp(free, tgt, on)
            hz = ROPE_H[2] - (0.02 if R.grip == "hand" else 0.14)
            side_off = Rz(yaw) @ Vector((0.18 * sg, 0.0, 0.0)) if R.hit != "side" else Vector()
            rope = Vector((S[s_].x + side_off.x, rope_y + 0.04, hz))
            # дуга: кисть іде до каната збоку-зверху, не крізь тіло
            arc = Rz(yaw) @ Vector((0.12 * sg, 0.0, 0.1)) * math.sin(math.pi * on)
            return lerp(free, rope, on) + arc
        return g
    p.hands = {s_: (hand(s_), FWD.lerp(DOWN, on), LEFT if s_ == "l" else RIGHT) for s_ in "lr"}
    p.elbows = {"l": lambda S: S["l"] + Rz(yaw) @ Vector((0.28, 0.25, -0.2 + 0.35 * on)),
                "r": lambda S: S["r"] + Rz(yaw) @ Vector((-0.28, 0.25, -0.2 + 0.35 * on))}
    p.fist = {"l": 0.55, "r": 0.55}
    return p

REBS = [
    Reb("Rope_Side_L"),
    Reb("Rope_Side_R", side=-1),
    Reb("Rope_Fast", speed=1.2, sink=0.3),
    Reb("Rope_Slow_Tired", speed=0.82, sink=0.2, lean=13.0),
    Reb("Rope_Deep", sink=0.34, crouch=1.0),
    Reb("Rope_Light", sink=0.15),
    Reb("Rope_Back", hit="back"),
    Reb("Rope_Elbow_Hook", grip="elbow"),
    Reb("Rope_Stop_Lean", ret=False),
    Reb("Rope_Duck_Return", duck=1.0),
    Reb("Rope_Roar", arm_up=1.0),
    Reb("Rope_Heavy", heavy=1.0, speed=0.9, sink=0.32),
    Reb("Rope_Chest_Stop", hit="chest", ret=False, sink=0.2),
    Reb("Rope_Hesitate", hesitate=0.25),
    Reb("Rope_Side_R_Fast", side=-1, speed=1.2, sink=0.3),
]

def reb_len(R):
    return reb_plan(R)[0]["end"]

MOVE_CLIPS += [(r.name, reb_len(r), (lambda t, L, r=r: rebound_pose(t, L, r)), None, False, None, 0) for r in REBS]
MOVE_EVENTS.update({r.name: [("rope_hit", reb_plan(r)[0]["hit"], "spine_02", "rope2:%.2f" % r.sink),
                             ("rope_hit", reb_plan(r)[0]["hit"] + 0.02, "spine_03", "rope3:%.2f" % (r.sink * 0.8)),
                             ("rope_release", reb_plan(r)[0]["push"], "-", "-")] for r in REBS})
