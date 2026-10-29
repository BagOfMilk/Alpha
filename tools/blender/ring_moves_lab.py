# Парні прийоми, зроблені в лабораторії (tools/blender/ring_lab.py): ключові пози за референсом (будова прийому —
# docs/research/RING_ANIM_REFERENCES.md, своїми словами), кисті закріплені за кістками суперника, перевірка зіткнень
# по сітці й огляд зблизька перед показом.
import math
from mathutils import Vector
import ring_lab

G = ring_lab.G

LOCKUP_START = 1.18        # м між тазами на старті (стійка навпроти)
LOCKUP_FWD = 0.33          # кожен проходить назустріч: плечі майже впритул (рука на шиї — зігнута)
NECK_OUT = {"l": (0.0, -1.0, 0.35)}   # кисть на шиї виходить з тіла за шию (назад і вгору), не на горло


def _base(fwd=0.0, crouch=0.0, pitch=None, lean_x=0.0):
    """Стійка, зсунута вперед на fwd (простір свого ригу: −Y — уперед), з присідом crouch."""
    p = G["stance"](0.0, 2.4, sway=0.0)
    p.pelvis = p.pelvis + Vector((lean_x, -fwd, -crouch))
    if pitch is not None:
        p.pitch = pitch
    p.ankles = (p.ankles[0] + Vector((0, -fwd, 0)), p.ankles[1] + Vector((0, -fwd, 0))) if p.ankles else \
        (G["foot"](0.2, -0.105 - fwd), G["foot"](-0.2, 0.075 - fwd))
    return p


SURF = {}                   # точка долоні на потилиці партнера (простір свого ригу), за роллю
APART = [0.0]               # додатковий розхід пари вбік (м) у кадрі, якщо голови перетинаються
def neck_surface():
    """Точки долоні на шкірі потилиці партнера для обох (ліва кисть): промінь ззаду-згори до основи шиї."""
    A, B = ring_lab.LAB["armA"], ring_lab.LAB["armB"]
    pa = ring_lab.surface_point(B, "neck_01", 0.2, (0.0, 1.0, 0.25))     # потилиця B (його «назад» — +Y ригу)
    pb = ring_lab.surface_point(A, "neck_01", 0.2, (0.0, 1.0, 0.25))
    SURF["A"] = ring_lab.to_rig(A, pa)
    SURF["B"] = ring_lab.to_rig(B, pb)


def tie(fwd, grip=1.0, push=0.0, hold_head=1.0, role=None):
    """Поза зчепу «комір — лікоть» (референс: глибокий присід, тулуби назустріч ~45°, голова над правим плечем
    суперника, ліве передпліччя на його плечі з долонею на потилиці, права охоплює лівий трицепс).
    fwd — скільки пройшов назустріч; grip 0..1 — руки від стійки до хвату; push — тисне (+) / подається (−)."""
    def f(Pt):
        P_ = G["P"]
        FWD, DOWN, UP, LEFT, RIGHT = G["FWD"], G["DOWN"], G["UP"], G["LEFT"], G["RIGHT"]
        p = _base(fwd + 0.04 * push, crouch=0.13 * hold_head)
        p.pelvis = p.pelvis + Vector((0.19 * hold_head, 0.1 * hold_head, 0.0))
        p.pitch = 16.0 + 32.0 * hold_head + 5.0 * push
        p.roll = -4.0 * hold_head
        p.yaw = -10.0 * hold_head + 4.0 * push
        p.head_yaw = -40.0 * hold_head
        p.head_pitch = -10.0 - 2.0 * hold_head
        p.ankles = (G["foot"](0.2 + 0.1 * hold_head, -0.105 - fwd - 0.055 * hold_head),
                    G["foot"](-0.2 + 0.02 * hold_head, 0.075 - fwd + 0.145 * hold_head))
        p.forced = {"l"} if grip > 0.5 else set()
        base = {"l": Vector((-0.07, -0.27, -0.2)), "r": Vector((0.07, -0.27, -0.2))}
        arc = math.sin(math.pi * grip)
        def hl(S):
            t = (SURF.get(role) if grip > 0.6 else None) or P_(S, "neck_01", 0.15, Vector((-0.02, -0.07, 0.0))) or (S["l"] + base["l"])
            return (S["l"] + base["l"]).lerp(t, grip) + Vector((0.06, 0.0, 0.12)) * arc      # дуга вгору й назовні
        def hr(S):
            t = P_(S, "lowerarm_l", 0.02, Vector((0.0, 0.0, -0.05))) or (S["r"] + base["r"])          # лікоть руки суперника на моїй шиї, знизу
            return (S["r"] + base["r"]).lerp(t, grip) + Vector((-0.05, 0.0, -0.04)) * arc
        _, fl, ul = p.hands["l"]; _, fr, ur = p.hands["r"]
        # долоня на основі шиї ззаду: пальці — донизу по спині суперника, великий палець — убік
        p.hands = {"l": (hl, fl.lerp(DOWN + FWD * 0.15, grip), ul.lerp(RIGHT, grip)),
                   "r": (hr, fr.lerp(FWD + LEFT * 0.4 + UP * 0.2, grip), ur.lerp(LEFT * 0.4 + UP, grip))}   # долоня вгору під лікоть
        el = p.elbows
        p.elbows = {"l": lambda S: el["l"](S).lerp(S["l"] + Vector((-0.12, -0.45, -0.45)), grip),   # лікоть спереду-внизу, перед грудьми суперника
                    "r": lambda S: el["r"](S).lerp(S["r"] + Vector((-0.4, 0.05, -0.3)), grip)}
        p.fist = {"l": 0.25 - 0.1 * grip, "r": 0.25 + 0.55 * grip}          # на шиї — пальці зімкнені, на лікті — охоплюють          # на шиї — пальці зімкнені, майже прямі
        return p
    return f


def stance_at(fwd=0.0, crouch=0.0, pitch=None):
    return lambda Pt: _base(fwd, crouch, pitch)


def lockup():
    """Ключові пози зчепу (30 к/с). Кадри — поза-до-пози; між ними — криві з м'яким входом/виходом."""
    ring_lab.LAB["keys"] = {"A": {}, "B": {}}
    ring_lab.place((0, -LOCKUP_START, 0), 180, z=1.0)
    K = ring_lab.key
    d = LOCKUP_FWD
    K(0, stance_at(), stance_at())                                          # 1 стійка навпроти
    K(6, stance_at(-0.03, 0.06, 12.0), stance_at(-0.03, 0.06, 12.0))        # 2 передчуття: присід, плечі назад
    K(12, tie(d * 0.45, 0.3, hold_head=0.3), tie(d * 0.45, 0.3, hold_head=0.3))   # крок лівою, руки йдуть угору
    K(17, tie(d * 0.9, 0.85, hold_head=0.8), tie(d * 0.9, 0.85, hold_head=0.8))   # зіткнення: руки на хвіт
    K(20, tie(d * 0.84, 1.0, push=-0.6), tie(d * 0.84, 1.0, push=-0.6))     # віддача від удару грудьми
    K(26, tie(d, 1.0), tie(d, 1.0))                                         # тримання
    K(40, tie(d, 1.0, push=1.0), tie(d, 1.0, push=-1.0))                    # A тисне, B подається
    K(54, tie(d, 1.0, push=-0.8), tie(d, 1.0, push=0.8))                    # B відтискає
    K(66, tie(d, 1.0, push=0.3), tie(d, 1.0, push=-0.3))
    K(72, tie(d, 1.0), tie(d, 1.0))
    contacts = [("A", "l", "neck_01", 0.35, (0, 0, 0), 17, 72), ("A", "r", "upperarm_l", 0.55, (0, 0, 0), 17, 72),
                ("B", "l", "neck_01", 0.35, (0, 0, 0), 17, 72), ("B", "r", "upperarm_l", 0.55, (0, 0, 0), 17, 72)]
    return ring_lab.bake("Lockup", 72 / 30.0, contacts=[])


def lockup_frames(fps=30, settle=True):
    """Зчеп покадрово: параметри пози (прохід, хват, тиск, голова) — криві за ключовими кадрами (поза-до-пози),
    на кожному кадрі риг розв'язується заново, кисті сідають на поверхню суперника (без проникнення)."""
    T = G["track"]
    d = LOCKUP_FWD
    ring_lab.place((0, -LOCKUP_START, 0), 180, z=1.0)
    # ключі параметрів (кадр, значення); B — у протифазі в боротьбі
    fwd = [(0, 0.0), (6, -0.03), (12, d * 0.45), (17, d * 0.9), (20, d * 0.84), (26, d), (72, d)]
    grip = [(0, 0.0), (8, 0.0), (17, 0.9), (22, 1.0), (72, 1.0)]
    head = [(0, 0.0), (6, 0.0), (12, 0.3), (17, 0.8), (26, 1.0), (72, 1.0)]
    push = [(0, 0.0), (17, 0.0), (20, -0.6), (26, 0.0), (40, 1.0), (54, -0.8), (66, 0.3), (72, 0.0)]
    crouch = [(0, 0.0), (6, 0.06), (12, 0.02), (72, 0.0)]          # 2 передчуття: присід перед кроком
    n = 72
    out = {"A": [], "B": []}
    worst = []
    for f in range(n + 1):
        ff = float(f)
        ga, h, pu, c = T(ff, grip), T(ff, head), T(ff, push), T(ff, crouch)
        fw = T(ff, fwd)
        def mk(sign):
            def pose(Pt):
                p = tie(fw, ga, push=sign * pu, hold_head=h, role="A" if sign > 0 else "B")(Pt)
                p.pelvis = p.pelvis + Vector((APART[0], 0, -c))
                p.head_yaw -= APART[0] * 150.0
                return p
            return pose
        APART[0] = 0.0
        for _try in range(5):                        # голови — щока до щоки, але не одна в одній
            SURF.clear()
            ring_lab.solve(mk(1.0), mk(-1.0))
            if ga > 0.6:                             # шкіра потилиці партнера — з уже поставленої пари, і ще раз
                neck_surface()
                ring_lab.solve(mk(1.0), mk(-1.0))
            if h < 0.5 or ring_lab.part_depth("body_head", "body_head") <= 0.012:
                break
            APART[0] += 0.012
        if settle and ga > 0.05:
            for _ in range(3):                       # по черзі: посадка одного зсуває руки другого
                ring_lab.settle("A"); ring_lab.settle("B")
        out["A"].append(ring_lab.capture("A")); out["B"].append(ring_lab.capture("B"))
    ring_lab.LAB["frames"] = out
    ring_lab.LAB["contacts"] = []
    ring_lab.LAB["name"] = "Lockup"
    return n
