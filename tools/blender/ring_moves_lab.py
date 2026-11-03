# Парні прийоми, зроблені в лабораторії (tools/blender/ring_lab.py): ключові пози за референсом (будова прийому —
# docs/research/RING_ANIM_REFERENCES.md, своїми словами), кисті закріплені за кістками суперника, перевірка зіткнень
# по сітці й огляд зблизька перед показом.
import math
from mathutils import Vector, Quaternion
import ring_lab

G = ring_lab.G

LOCKUP_START = 1.18        # м між тазами на старті (стійка навпроти)
LOCKUP_FWD = 0.35          # кожен проходить назустріч: плечі майже впритул (рука на шиї — зігнута)
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
APART = [0.0]
REACH = {"A": 0.0, "B": 0.0}   # додатковий крок до суперника (м), якщо рука не дістає (тіла різних пропорцій)
MOVE_MAP = None              # карта прийому (tools/blender/moves/*.json), якщо задана — кисті в триманні за нею               # додатковий розхід пари вбік (м) у кадрі, якщо голови перетинаються
def neck_surface():
    """Цілі хвату на шкірі партнера для обох. Референс зчепу (фото тренування, пара в ринзі): ПРАВА рука — через ліве
    плече суперника на основу його шиї ззаду; ЛІВА — тримає його правий лікоть знизу; голови — щока до щоки, у кожного
    зі свого ЛІВОГО боку (власник 29.10.2026: «ліва рука не там де має бути, вона справа»)."""
    A, B = ring_lab.LAB["armA"], ring_lab.LAB["armB"]
    for me, ot, k in ((A, B, "A"), (B, A, "B")):
        pw, nw, tw = ring_lab.surface_frame(ot, "spine_03", 1.0, (0.0, 1.0, 0.15), palm=0.035)   # основа шиї, не голова   # «назад» партнера — +Y його ригу
        R = me.matrix_world.to_3x3().inverted()
        SURF[k] = ring_lab.to_rig(me, pw)
        SURF[k + "_n"] = (R @ nw).normalized() * NECK_THUMB[k]
        SURF[k + "_t"] = (R @ tw).normalized()            # уздовж спини суперника донизу
        # поперек шиї — до правого боку суперника: так пальці охоплюють шию (вісь кисті за референсом)
        SURF[k + "_x"] = (R @ (ot.matrix_world.to_3x3() @ Vector((-1.0, 0.0, 0.0)))).normalized()
        # під ліктем руки суперника, що лежить на моїй шиї (його права): промінь знизу — кисть охоплює лікоть знизу
        SURF[k + "_e"] = ring_lab.to_rig(me, ring_lab.surface_point(ot, "lowerarm_r", 0.06, (0.0, 0.0, -1.0), palm=0.03))


NECK_THUMB = {"A": 1.0, "B": -1.0}
NECK_UP = 1.0       # бік великого пальця на шиї (перевіряється оглядом)


def tie(fwd, grip=1.0, push=0.0, hold_head=1.0, role=None):
    """Поза зчепу «комір — лікоть» (див. neck_surface): глибокий присід, тулуби назустріч, голова — над лівим плечем
    суперника, права рука на його шиї (передпліччя на плечі), ліва — на його правому лікті.
    fwd — скільки пройшов назустріч; grip 0..1 — руки від стійки до хвату; push — тисне (+) / подається (−)."""
    def f(Pt):
        P_ = G["P"]
        FWD, DOWN, UP, LEFT, RIGHT = G["FWD"], G["DOWN"], G["UP"], G["LEFT"], G["RIGHT"]
        p = _base(fwd + 0.04 * push + REACH.get(role, 0.0) * grip, crouch=0.09 * hold_head)
        p.pelvis = p.pelvis + Vector((0.2 * hold_head, 0.2 * hold_head, 0.0))     # голова — ліворуч; таз відведений назад (референс)
        p.pitch = 16.0 + 30.0 * hold_head + 5.0 * push       # тулуби назустріч, нижче — дистанція
        p.roll = -4.0 * hold_head
        p.yaw = -10.0 * hold_head + 4.0 * push
        p.head_yaw = -60.0 * hold_head               # дивиться повз плече суперника, не в обличчя
        p.head_pitch = -6.0 + 6.0 * hold_head
        p.ankles = (G["foot"](0.2 + 0.1 * hold_head, -0.105 - fwd - 0.055 * hold_head),
                    G["foot"](-0.2 + 0.02 * hold_head, 0.075 - fwd + 0.145 * hold_head))
        p.forced = {"r"} if grip > 0.5 else set()
        base = {"l": Vector((-0.07, -0.27, -0.2)), "r": Vector((0.07, -0.27, -0.2))}
        arc = math.sin(math.pi * grip)
        def hr(S):          # на шию — дугою вгору через ліве плече суперника
            t = (SURF.get(role) if grip > 0.0 else None) or P_(S, "neck_01", 0.15, Vector((0.02, -0.07, 0.0))) or (S["r"] + base["r"])
            return (S["r"] + base["r"]).lerp(t, grip) + Vector((-0.16, 0.12, 0.42)) * arc
        def hl(S):          # на правий лікоть суперника, знизу
            t = (SURF.get(f"{role}_e") if grip > 0.0 else None) or P_(S, "lowerarm_r", 0.02, Vector((0.0, 0.0, -0.05))) or (S["l"] + base["l"])
            return (S["l"] + base["l"]).lerp(t, grip) + Vector((0.05, 0.0, -0.04)) * arc
        _, fl, ul = p.hands["l"]; _, fr, ur = p.hands["r"]
        # пальці — уздовж спини суперника (дотична), долоня — на шкіру
        tn = SURF.get(f"{role}_t") if grip > 0.0 else None
        nn = SURF.get(f"{role}_n") if grip > 0.0 else None
        xn = SURF.get(f"{role}_x") if grip > 0.0 else None
        fore_r = xn if xn is not None else DOWN + FWD * 0.15
        up_r = UP * NECK_UP                                # великий палець — угору, до голови
        p.hands = {"r": (hr, fr.lerp(fore_r, grip), ur.lerp(up_r, grip)),
                   "l": (hl, fl.lerp(FWD + RIGHT * 0.4 + UP * 0.2, grip), ul.lerp(RIGHT * 0.4 + UP, grip))}   # долоня вгору під лікоть
        el = p.elbows
        p.elbows = {"r": lambda S: el["r"](S).lerp(S["r"] + Vector((-0.45, -0.15, 0.15)), grip),   # лікоть високо й назовні (референс)
                    "l": lambda S: el["l"](S).lerp(S["l"] + Vector((0.4, 0.05, -0.3)), grip)}
        p.fist = {"r": 0.25 - 0.15 * grip, "l": 0.25 + 0.1 * grip}    # на шиї — пальці лише ледь зігнуті (шия товста), на лікті — підтримує
        return p
    return f


def calibrate_reach(mv, d=None):
    """Скільки кожному підійти ближче, щоб рука на шиї дістала (тіла різних культур мають різні пропорції)."""
    d = LOCKUP_FWD if d is None else d
    REACH["A"] = REACH["B"] = 0.0
    for _ in range(4):
        SURF.clear()
        ring_lab.solve(tie(d, role="A"), tie(d, role="B")); neck_surface(); ring_lab.solve(tie(d, role="A"), tie(d, role="B"))
        ring_lab.place_hands(mv)
        done = True
        for t in ("A", "B"):
            rig = ring_lab.LAB["rig" + t]; W = rig.arm.matrix_world; pb = rig.arm.pose.bones
            miss = (rig.empties["wrist_r"].location - W @ pb["hand_r"].head).length
            if miss > 0.008:
                REACH[t] += miss + 0.005
                done = False
        if done:
            break
    return dict(REACH)


def settle_pair(rounds=10, ok=0.003):
    """Посадка кистей обох по черзі (посадка одного зсуває руки другого), доки залишок кожного ≤ ok або вичерпано
    кола; наприкінці — перевірка по всьому тілу (depth2) і ще коло, якщо щось лишилось."""
    for _ in range(rounds):
        a = ring_lab.settle("A"); b = ring_lab.settle("B")
        if max(list(a.values()) + list(b.values())) <= ok * 100:
            a2 = ring_lab.settle("A")
            if max(a2.values()) <= ok * 100:
                return True
    return False


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
    grip = [(0, 0.0), (6, 0.0), (12, 0.4), (17, 0.75), (24, 1.0), (72, 1.0)]   # хват — після зіткнення, не крізь тіло
    head = [(0, 0.0), (6, 0.0), (12, 0.3), (17, 0.8), (26, 1.0), (72, 1.0)]
    push = [(0, 0.0), (17, 0.0), (20, -0.3), (26, 0.0), (40, 0.7), (54, -0.6), (66, 0.25), (72, 0.0)]
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
        for _try in range(1):                        # розведення голів вимкнено: голова повз плече (карта), а зсув таза ламав досяжність кисті
            SURF.clear()
            ring_lab.solve(mk(1.0), mk(-1.0))
            if ga > 0.05:                            # шкіра потилиці партнера — з уже поставленої пари, і ще раз
                neck_surface()
                ring_lab.solve(mk(1.0), mk(-1.0))
            if h < 0.5 or max(ring_lab.part_depth("body_head", "body_head"), ring_lab.part_depth("body_torso", "body_torso")) <= 0.006:
                break
            APART[0] += 0.01
        if MOVE_MAP and ga >= 0.99:                  # тримання: кисті за картою прийому (ворота 2)
            ring_lab.place_hands(MOVE_MAP); ring_lab.place_hands(MOVE_MAP)
        elif settle and ga > 0.05:                   # підхід: посадка кистей на поверхню
            settle_pair()
        out["A"].append(ring_lab.capture("A")); out["B"].append(ring_lab.capture("B"))
    ring_lab.LAB["frames"] = out
    ring_lab.LAB["contacts"] = []
    ring_lab.LAB["name"] = "Lockup"
    return n


# ================================================================ бодислем
# Референс (фото й ілюстрації техніки, своїми словами): A підсідає під B — права рука між ніг під стегно, ліва через
# праве плече B на спину; підйом через груди, B перевертається головою вниз біля лівого боку A (ноги вгору), лягає
# горизонтально поперек грудей A животом до них; A опускає B перед собою — B розвертається й падає плоскою спиною.
SLAM_GAP = 0.62
SLAM_T = {"scoop": 12, "invert": 30, "across": 40, "hold": 46, "land": 54, "end": 84}


def _a2b(v):
    """Точка з простору A у простір B (B стоїть на SLAM_GAP перед A, розвернутий на 180°)."""
    return Vector((-v.x, -(v.y + SLAM_GAP), v.z))


def slam_b(f):
    """Поза B у кадрі f: таз (простір A) і поворот тіла — ключі; тіло цілком у рамці (body() генератора)."""
    T = G["track"]
    UPv, LEFTv, FWDv = G["UP"], G["LEFT"], G["FWD"]
    t = SLAM_T
    # таз B у просторі A (над матом, м); B — перед A
    px = T(f, [(0, 0.0), (t["scoop"], 0.02), (t["invert"], -0.16), (t["across"], -0.08), (t["hold"], -0.08),
               (t["land"], -0.1), (t["end"], -0.1)])
    py = T(f, [(0, -SLAM_GAP), (t["scoop"], -0.52), (t["invert"], -0.44), (t["across"], -0.4), (t["hold"], -0.4),
               (t["land"], -0.9), (t["end"], -0.9)])
    pz = T(f, [(0, G["REST"]["pelvis"].z), (t["scoop"], G["REST"]["pelvis"].z + 0.05), (t["invert"], 1.5),
               (t["across"], 1.27), (t["hold"], 1.3), (t["land"], G["LIE_Z"]), (t["end"], G["LIE_Z"])])
    # повороти: крен головою до лівого боку A (= правий бік B), потім — на спину
    roll = T(f, [(0, 0.0), (t["scoop"], 10.0), (t["invert"], 150.0), (t["across"], 90.0), (t["hold"], 90.0),
                 (t["land"], 90.0), (t["end"], 90.0)])   # голова B — ліворуч від A, через його ліве плече (там голова A)
    back = T(f, [(0, 0.0), (t["hold"], 0.0), (t["land"] - 2, 85.0), (t["land"], 90.0), (t["end"], 90.0)])
    q = Quaternion(FWDv, math.radians(roll))
    # на спину: поворот навколо довгої осі тіла (після крену вона горизонтальна)
    axis = (q @ UPv).normalized()
    q = Quaternion(axis, math.radians(back)) @ q
    pel = _a2b(Vector((px, py, pz)))
    p = G["body"](G["off_for"](pel), q, knee_bend=0.35)
    p.head_pitch = 25.0                      # підборіддя до грудей — голова не б'ється об мат
    return p


def slam_a(f, Pt=None):
    """Поза A: стійка → підсідає (крок лівою під B) → встає з вагою, відхиляючись → тримає → опускає B, присідає."""
    T = G["track"]
    t = SLAM_T
    P_ = G["P"]
    FWD, DOWN, UP = G["FWD"], G["DOWN"], G["UP"]
    sc = T(f, [(0, 0.0), (t["scoop"], 1.0), (t["invert"], 0.5), (t["across"], 0.0), (t["hold"], 0.0),
               (t["land"], 0.9), (t["end"], 0.7)])            # присід
    lean = T(f, [(0, 0.0), (t["scoop"], 1.0), (t["invert"], -0.3), (t["hold"], -0.35), (t["land"], 1.0), (t["end"], 0.8)])
    grab = T(f, [(0, 0.0), (8, 0.3), (t["scoop"], 1.0), (t["land"] - 3, 1.0), (t["land"] + 4, 0.0), (t["end"], 0.0)])
    p = G["stance"](0.0, 2.4, sway=0.0)
    p.pelvis = p.pelvis + Vector((0.17 * sc, -0.04 * sc, -0.2 * sc))   # підсідає збоку: голова — повз тулуб B
    p.pitch = 16.0 + 26.0 * lean
    p.yaw = -8.0 * sc
    p.roll = 24.0 * sc                                   # голова — під ліву пахву B, не в його груди
    p.head_yaw = 25.0 * sc
    p.head_pitch = -6.0
    p.ankles = G["steps"](f / 30.0, [(0.0, (0.2, -0.105)), (0.15, (0.2, -0.105)), (0.38, (0.24, -0.24))],
                          [(0.0, (-0.2, 0.075)), (1.6, (-0.2, 0.075)), (1.85, (-0.18, -0.15))])
    p.forced = set()
    base = {"l": Vector((-0.07, -0.27, -0.2)), "r": Vector((0.07, -0.27, -0.2))}
    def hr(S):
        tgt = SLAM_SURF.get("r") or P_(S, "thigh_r", 0.15, Vector((0.0, 0.0, -0.06))) or (S["r"] + base["r"])
        return (S["r"] + base["r"]).lerp(tgt, grab)                                    # під стегно B, на шкірі
    def hl(S):
        tgt = SLAM_SURF.get("l") or P_(S, "spine_03", 0.4, Vector((0.0, 0.0, 0.0))) or (S["l"] + base["l"])
        return (S["l"] + base["l"]).lerp(tgt, grab)                                    # під лопатки B, на шкірі
    p.hands = {"r": (hr, FWD, UP), "l": (hl, FWD + UP * 0.3, UP)}
    p.elbows = {"l": lambda S: S["l"] + Vector((0.4, 0.05, -0.3)), "r": lambda S: S["r"] + Vector((-0.4, 0.0, -0.4))}
    p.fist = {"l": 0.45 * grab, "r": 0.5 * grab}
    return p


SLAM_SURF = {}


def slam_surface(f):
    """Цілі кистей A на шкірі B: до перевороту — права під стегно між ніг (знизу), ліва — на спину через плече
    (ззаду); у триманні — обидві знизу під B (стегно й лопатки)."""
    A, B = ring_lab.LAB["armA"], ring_lab.LAB["armB"]
    SLAM_SURF.clear()
    down = (0, 0, -1)
    back_b = B.matrix_world.to_3x3() @ Vector((0, 1, 0))
    if f < SLAM_T["scoop"]:
        r = ring_lab.surface_world(B, "thigh_r", 0.12, down)
        l = ring_lab.surface_world(B, "spine_03", 0.6, back_b)
    else:
        r = ring_lab.surface_world(B, "thigh_r", 0.35, down)
        l = ring_lab.surface_world(B, "spine_03", 0.3, down)
    SLAM_SURF["r"] = ring_lab.to_rig(A, r)
    SLAM_SURF["l"] = ring_lab.to_rig(A, l)


def slam_frames():
    ring_lab.place((0, -SLAM_GAP, 0), 180, z=1.0)
    out = {"A": [], "B": []}
    for f in range(SLAM_T["end"] + 1):
        ff = float(f)
        SLAM_SURF.clear()
        ring_lab.solve(lambda Pt, ff=ff: slam_a(ff, Pt), lambda Pt, ff=ff: slam_b(ff))
        if 4 < f < SLAM_T["land"]:
            slam_surface(f)
            ring_lab.solve(lambda Pt, ff=ff: slam_a(ff, Pt), lambda Pt, ff=ff: slam_b(ff))
            settle_pair()
        out["A"].append(ring_lab.capture("A")); out["B"].append(ring_lab.capture("B"))
    ring_lab.LAB["frames"] = out
    ring_lab.LAB["contacts"] = []
    ring_lab.LAB["name"] = "Bodyslam"
    return SLAM_T["end"]


# ================================================================ локап — 15 варіантів (власник: «локап … по 15»)
# Варіант — параметри одного зчепу: тиск (push), дрейф пари вперед/назад (drift, + — A тисне B назад), боковий
# дрейф (lat), присід, темп підходу, розрив наприкінці. Стопи не ковзають: кожна переступає сходинками (крок кожні
# 0,3 с, ліва й права по черзі, з відривом), таз іде плавно.
def _stairs(f, F, period=9, phase=0, dur=5):
    """Положення стопи (вперед, м) для плавної кривої F(кадр): переступає раз на period кадрів, крок — dur кадрів."""
    k = (f - phase) // period
    t0 = phase + k * period
    prev = F(max(0, t0 - period + dur)) if t0 > 0 else F(0)
    cur = F(t0 + dur)
    if f < t0:
        return F(0), 0.0
    s = min(1.0, (f - t0) / dur)
    lift = 0.06 * math.sin(math.pi * s) if abs(cur - prev) > 0.01 else 0.0
    return prev + (cur - prev) * G["smooth"](s), lift


LOCKUP_VARS = {
    "Lockup": {},
    "Lockup_Power_A": {"drift": [(26, 0.0), (60, 0.55), (72, 0.6)], "push": [(26, 0.0), (36, 0.9), (72, 0.8)]},
    "Lockup_Power_B": {"drift": [(26, 0.0), (60, -0.55), (72, -0.6)], "push": [(26, 0.0), (36, -0.9), (72, -0.8)]},
    "Lockup_Stalemate": {"push": [(26, 0.0), (32, 0.9), (38, -0.9), (44, 0.9), (50, -0.9), (56, 0.9), (62, -0.6), (72, 0.0)]},
    "Lockup_To_Ropes": {"drift": [(26, 0.0), (66, 1.1), (72, 1.15)], "push": [(26, 0.0), (34, 1.0), (72, 1.0)]},
    "Lockup_Side_Walk": {"lat": [(26, 0.0), (72, 0.7)], "push": [(26, 0.0), (40, 0.5), (56, -0.4), (72, 0.0)]},
    "Lockup_Side_Walk_R": {"lat": [(26, 0.0), (72, -0.7)], "push": [(26, 0.0), (40, -0.5), (56, 0.4), (72, 0.0)]},
    "Lockup_Fast": {"tempo": 1.4},
    "Lockup_Cautious": {"tempo": 0.7, "crouch_x": 0.04},
    "Lockup_Heavy": {"crouch_x": 0.06, "push": [(26, 0.0), (44, 0.5), (60, -0.5), (72, 0.0)]},
    "Lockup_Shove_Off": {"break": True, "push": [(26, 0.0), (40, 0.6), (54, 1.0), (72, 0.0)]},
    "Lockup_Ref_Break": {"break": True, "push": [(26, 0.0), (40, 0.3), (72, 0.0)]},
    "Lockup_Exchange": {"drift": [(26, 0.0), (40, 0.3), (54, -0.3), (66, 0.2), (72, 0.15)],
                        "push": [(26, 0.0), (36, 0.8), (48, -0.8), (60, 0.6), (72, 0.0)]},
    "Lockup_Low": {"crouch_x": 0.1, "push": [(26, 0.0), (40, 0.4), (56, -0.4), (72, 0.0)]},
    "Lockup_Drive_Back_Ropes": {"drift": [(26, 0.0), (66, -1.1), (72, -1.15)], "push": [(26, 0.0), (34, -1.0), (72, -1.0)]},
}


def lockup_var_frames(name, settle=True):
    """Варіант локапу name (LOCKUP_VARS). Підхід і хват — як у базовому; далі — тиск, дрейф, присід, розрив."""
    T = G["track"]
    v = LOCKUP_VARS[name]
    d = LOCKUP_FWD
    tempo = v.get("tempo", 1.0)
    sc = lambda keys: [(k[0] / tempo if k[0] <= 26 else k[0] - 26 + 26 / tempo, k[1]) for k in keys]
    fwd = sc([(0, 0.0), (6, -0.03), (12, d * 0.45), (17, d * 0.9), (20, d * 0.84), (26, d)])
    grip = sc([(0, 0.0), (6, 0.0), (12, 0.4), (17, 0.75), (24, 1.0), (26, 1.0)])
    head = sc([(0, 0.0), (6, 0.0), (12, 0.3), (17, 0.8), (26, 1.0)])
    t_hold = 26 / tempo
    n = int(round(t_hold + 46))
    shift = lambda keys: [(k[0] - 26 + t_hold, k[1]) for k in keys]
    push = [(0, 0.0)] + shift(v.get("push", [(26, 0.0), (40, 0.7), (54, -0.6), (66, 0.25), (72, 0.0)]))
    drift = [(0, 0.0)] + shift(v.get("drift", [(26, 0.0), (72, 0.0)]))
    lat = [(0, 0.0)] + shift(v.get("lat", [(26, 0.0), (72, 0.0)]))
    if v.get("break"):                                   # розрив: руки відпускають, обидва крок назад
        fwd += [(n - 12, d), (n, d - 0.3)]
        grip += [(n - 14, 1.0), (n - 4, 0.0)]
        head += [(n - 14, 1.0), (n - 4, 0.0)]
    fwd += [(n + 1, fwd[-1][1])]
    grip += [(n + 1, grip[-1][1])]
    head += [(n + 1, head[-1][1])]
    crouch = [(0, 0.0), (6 / tempo, 0.06), (12 / tempo, 0.02), (t_hold, v.get("crouch_x", 0.0)), (n, v.get("crouch_x", 0.0))]
    ring_lab.place((0, -LOCKUP_START, 0), 180, z=1.0)
    out = {"A": [], "B": []}
    for f in range(n + 1):
        ff = float(f)
        ga, h, pu, c = T(ff, grip), T(ff, head), T(ff, push), T(ff, crouch)
        fw, dr, la = T(ff, fwd), T(ff, drift), T(ff, lat)
        def mk(sign):
            role = "A" if sign > 0 else "B"
            Ftot = lambda x: T(float(x), fwd) + sign * T(float(x), drift)
            Ltot = lambda x: sign * T(float(x), lat)
            def pose(Pt):
                p = tie(fw, ga, push=sign * pu, hold_head=h, role=role)(Pt)
                body_f = fw + sign * dr
                p.pelvis = p.pelvis + Vector((sign * la, -(body_f - fw), -c))
                p.head_yaw -= APART[0] * 150.0
                al, ar = list(p.ankles)
                for i, (ph) in enumerate((0, 4)):
                    sf, lf = _stairs(f, Ftot, phase=ph)
                    sl, ll = _stairs(f, Ltot, phase=ph)
                    a_ = (al, ar)[i]
                    a_ = a_ + Vector((sl, -(sf - fw), max(lf, ll)))
                    if i == 0: al = a_
                    else: ar = a_
                p.ankles = (al, ar)
                if p.knees:
                    p.knees = (p.knees[0] + Vector((0, -(body_f - fw), 0)), p.knees[1] + Vector((0, -(body_f - fw), 0)))
                return p
            return pose
        APART[0] = 0.0
        SURF.clear()
        ring_lab.solve(mk(1.0), mk(-1.0))
        if ga > 0.05:
            neck_surface()
            ring_lab.solve(mk(1.0), mk(-1.0))
        if MOVE_MAP and ga >= 0.99:
            ring_lab.place_hands(MOVE_MAP); ring_lab.place_hands(MOVE_MAP)
        elif settle and ga > 0.05:
            settle_pair()
        out["A"].append(ring_lab.capture("A")); out["B"].append(ring_lab.capture("B"))
    ring_lab.LAB["frames"] = out
    ring_lab.LAB["contacts"] = []
    ring_lab.LAB["name"] = name
    return n
