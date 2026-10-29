# Ланки чейну в лабораторії (tools/blender/ring_lab.py): старт — останній кадр зчепу (lab_clips/Lockup.json), далі
# покадрово: параметри пози — криві за ключами (поза-до-пози), кожен кадр розв'язується заново, кисті сідають на
# шкіру, руки виштовхуються з тіла суперника; перевірка проникнення по сітці (depth2).
#
# Пара: A — у нулі обличчям у −Y (його ліво — +X), B — у (0, −1,18) з поворотом 180° (як у зчепу). Пози B задаються
# в «кадрі A» (світ без висоти підлоги) і переводяться в простір ригу B функціями a2b/d2b — так позу пари видно одним
# поглядом.
#
#   blender -b Logs/ring_anims/lab_scene.blend --python tools/blender/ring_lab_run.py -- Chain_Headlock
import json, math, os
import bpy
from mathutils import Vector, Quaternion
import ring_lab

G = ring_lab.G
LAB = ring_lab.LAB
B_OFF = Vector((0.0, -1.18, 0.0))


def a2b(v):
    """Точка з кадру A у простір ригу B."""
    return Vector((-(v.x - B_OFF.x), -(v.y - B_OFF.y), v.z))


def d2b(v):
    """Напрямок з кадру A у простір ригу B."""
    return Vector((-v.x, -v.y, v.z))


def heading(deg):
    """Напрямок «обличчям» у кадрі A: 0° — −Y (як A), +90° — до +X (ліво A), 180° — до A (як B у зчепі)."""
    r = math.radians(deg)
    return Vector((math.sin(r), -math.cos(r), 0.0))


def load_frames(name):
    p = os.path.join(ring_lab.REPO, "tools", "blender", "lab_clips", name + ".json")
    d = json.load(open(p, encoding="utf-8"))
    conv = lambda fr: {n: (Vector(l), Quaternion(r)) for n, (l, r) in fr.items()}
    return [conv(f) for f in d["A"]], [conv(f) for f in d["B"]]


# ================================================================ захват голови збоку
# Референс (кадр власника з гри-взірця «Pro wrestling chain» + записи матчів, своїми словами):
#   A — стоїть майже прямо, трохи відкинувшись, ноги в широкому розкроку; ПРАВА рука — петлею навколо шиї B:
#       плече лежить на його потилиці, лікоть — з дальнього боку голови, передпліччя — під підборіддям; ЛІВА кисть
#       замикає праве зап'ястя під обличчям B; голова A піднята (кричить у зал).
#   B — зігнутий у попереку, тіло йде ВБІК від A (праворуч), голова — під правою пахвою A, ПЕРЕД його грудьми (не за
#       спиною); ліва рука B обхоплює A за поперек ззаду, права тягне вниз передпліччя, що душить; коліна зігнуті.
# Параметри пози (кадр A): a_pel/a_pz — таз A; a_pitch/a_yaw/a_roll — тулуб; a_hy/a_hp — голова; a_feet — стопи;
# b_head — курс B; b_pel/b_drop — таз B; b_pelpitch + b_pitch — нахил; b_hy/b_hp — голова; b_feet — стопи;
# grab 0..1 — кисті від хвату зчепу (шия + лікоть) до петлі захвату.
HOLD = dict(a_pel=(0.04, -0.04), a_pz=-0.12, a_pitch=4.0, a_yaw=0.0, a_roll=6.0, a_hy=15.0, a_hp=-18.0,
            a_feet=((0.2, -0.2), (-0.24, 0.2)), a_fyaw=(15.0, -30.0),
            b_head=90.0, b_pel=(-0.6, -0.24), b_drop=-0.021, b_pelpitch=30.0, b_pitch=50.0, b_yaw=-8.0, b_roll=0.0,
            b_hy=20.0, b_hp=25.0, b_feet=None, b_fl=0.3, grab=1.0,
            a_eloop=(-0.12, -0.2, 0.05), a_chin=(0.0, 0.1, -1.0), a_lh=(0.06, -0.03, -0.03), a_lel=(0.3, -0.2, -0.35),
            a_arc=(-0.08, -0.05, 0.28))
# Зчеп (останній кадр Lockup.json) у тих самих параметрах: тази, стопи, нахили виміряні в лабораторії 29.10.2026.
TIE = dict(HOLD, a_pel=(0.2, -0.153), a_pz=-0.18, a_pitch=46.0, a_yaw=-10.0, a_roll=-4.0, a_hy=-60.0, a_hp=0.0,
           a_feet=((0.3, -0.51), (-0.18, -0.13)), a_fyaw=(10.0, -14.0),
           b_head=180.0, b_pel=(-0.2, -1.027), b_drop=0.09, b_pelpitch=0.0, b_pitch=46.0, b_yaw=-10.0, b_roll=-4.0,
           b_hy=-60.0, b_hp=0.0, b_feet=((-0.3, -0.67), (0.18, -1.05)), grab=0.0)
HL = dict(HOLD)
# Хват зчепу відносно кісток суперника (простір свого ригу) — підганяється calibrate_tie() під останній кадр Lockup.json,
# щоб ланка починалась рівно там, де кисті були в зчепі (без стрибка).
TIE_OFF = {"Ar": (-0.094, -0.055, 0.082), "Al": (-0.116, -0.127, -0.129),     # виміряно 29.10.2026
           "Br": (-0.169, -0.082, 0.054), "Bl": (-0.113, -0.11, -0.072)}


TIE_BONE = {"Ar": "neck_01", "Al": "lowerarm_r", "Br": "neck_01", "Bl": "lowerarm_r"}
TIE_ALONG = {"Ar": 0.1, "Al": 0.06, "Br": 0.1, "Bl": 0.06}
TIE_LOCAL = {}


def tie_local():
    """Точки хвату зчепу в системі кістки суперника (а не свого ригу): коли суперник повертається, точка їде з
    кісткою, а не всередину його тіла. Рахується з пози TIE (ті самі точки, що дає TIE_OFF)."""
    keep = dict(HL)
    HL.clear(); HL.update(TIE)
    ring_lab.solve(a_hold(), b_hold())
    for k, bone in TIE_BONE.items():
        me, ot = (LAB["armA"], LAB["armB"]) if k[0] == "A" else (LAB["armB"], LAB["armA"])
        pb = ot.pose.bones[bone]
        p_rig = (me.matrix_world.inverted() @ ot.matrix_world) @ (pb.head + (pb.tail - pb.head) * TIE_ALONG[k]) + Vector(TIE_OFF[k])
        Mw = ot.matrix_world @ pb.matrix
        TIE_LOCAL[k] = Mw.inverted() @ (me.matrix_world @ p_rig)
    HL.clear(); HL.update(keep)


def tie_points():
    """Точки хвату зчепу для поточної пози (простір свого ригу) — у SURF["t…"]."""
    for k, bone in TIE_BONE.items():
        if k not in TIE_LOCAL:
            continue
        me, ot = (LAB["armA"], LAB["armB"]) if k[0] == "A" else (LAB["armB"], LAB["armA"])
        Mw = ot.matrix_world @ ot.pose.bones[bone].matrix
        SURF["t" + k] = ring_lab.to_rig(me, Mw @ TIE_LOCAL[k])


def calibrate_tie(iters=4):
    """Зсуви хвату зчепу: кисті в позі TIE стають туди, де вони в останньому кадрі зчепу."""
    la, lb = load_frames("Lockup")
    want = {}
    for tag, fr in (("A", la[-1]), ("B", lb[-1])):
        G["set_basis"](LAB["rig" + tag], fr)
        arm = LAB["arm" + tag]
        want[tag] = {s: arm.pose.bones["hand_" + s].head.copy() for s in "lr"}     # простір свого ригу
    HL.clear(); HL.update(TIE)
    for _ in range(iters):
        ring_lab.solve(a_hold(), b_hold())
        for tag in "AB":
            arm = LAB["arm" + tag]
            for s in "lr":
                e = want[tag][s] - arm.pose.bones["hand_" + s].head
                k = tag + s
                TIE_OFF[k] = tuple(Vector(TIE_OFF[k]) + e)
    return {k: tuple(round(x, 3) for x in v) for k, v in TIE_OFF.items()}


def b_feet_hold(pel=None, h=None):
    """Стопи B у триманні: позаду таза, розкрок поперек курсу (кадр A)."""
    fwd = heading(HL["b_head"] if h is None else h)
    left = Vector((-fwd.y, fwd.x, 0.0))
    pel = Vector((*(HL["b_pel"] if pel is None else pel), 0.0))
    fl = pel - fwd * 0.05 + left * HL["b_fl"]
    fr = pel + fwd * 0.12 - left * 0.2
    return (tuple(fl.xy), tuple(fr.xy))


def _lerp_fn(a, b, w):
    return lambda S: a(S).lerp(b(S), w)


def b_hold(k=1.0):
    """Поза B (кадр A → простір B)."""
    def f(Pt):
        P_ = G["P"]
        h = HL["b_head"]
        fwd = heading(h)
        left = Vector((-fwd.y, fwd.x, 0.0))
        p = G["stance"](0.0, 2.4, sway=0.0)
        pel = Vector((*HL["b_pel"], 0.0))
        p.pelvis = a2b(pel) + Vector((0, 0, -0.09 - HL["b_drop"]))
        db = d2b(fwd)
        p.pelvis_yaw = math.degrees(math.atan2(db.x, -db.y))
        p.pelvis_pitch = HL["b_pelpitch"]
        p.pitch = HL["b_pitch"]
        p.yaw = HL["b_yaw"]
        p.roll = HL["b_roll"]
        p.head_yaw = p.pelvis_yaw + HL["b_hy"]
        p.head_pitch = HL["b_hp"]
        fl, fr = (Vector((*xy, 0.0)) for xy in (HL["b_feet"] or b_feet_hold()))
        zl, zr = HL.get("b_feet_z", (0.0, 0.0))
        p.ankles = (G["foot"](*a2b(fl).xy) + Vector((0, 0, zl)), G["foot"](*a2b(fr).xy) + Vector((0, 0, zr)))
        p.feet_yaw = (p.pelvis_yaw + 10.0, p.pelvis_yaw - 10.0)
        p.foot_rot = None
        p.knees = (a2b(fl + fwd * 0.6 + left * 0.1) + Vector((0, 0, 0.5)),
                   a2b(fr + fwd * 0.6 - left * 0.1) + Vector((0, 0, 0.5)))
        w = HL["grab"]
        wb = G["smooth"]((w - 0.55) / 0.45)          # B тримає хват зчепу, доки A не замкне петлю
        # зчеп: права — на шиї A ззаду, ліва — під його правим ліктем
        tie_r = lambda S: SURF.get("tBr") or P_(S, "neck_01", 0.1, Vector(TIE_OFF["Br"])) or S["r"] + Vector((0, -0.3, -0.3))
        tie_l = lambda S: SURF.get("tBl") or P_(S, "lowerarm_r", 0.06, Vector(TIE_OFF["Bl"])) or S["l"] + Vector((0, -0.3, -0.3))
        # захват: ліва — за поперек A ззаду (долоня на шкірі), права — упирається в його праве стегно
        hl_l = lambda S: SURF.get("Bl") or P_(S, "spine_01", 0.5, d2b(Vector((0.04, 0.15, 0.0)))) or S["l"] + Vector((0, -0.3, -0.3))
        hl_r = lambda S: SURF.get("Br") or P_(S, "lowerarm_r", 0.55, Vector((0, 0, -0.05))) or S["r"] + Vector((0, -0.3, -0.3))
        arc = math.sin(math.pi * wb) * 0.08
        def br(S):                                     # права — до стегна A спереду, в обхід його тулуба
            below = hl_r(S) + d2b(Vector((-0.05, -0.22, 0.2)))
            if wb < 0.6:
                return tie_r(S).lerp(below, wb / 0.6)
            return below.lerp(hl_r(S), (wb - 0.6) / 0.4)

        def bl(S):                                     # ліва — в обхід правого боку A (не крізь груди) за поперек
            side = P_(S, "spine_02", 0.5, d2b(Vector((-0.3, 0.02, -0.05))))
            if side is None:
                return tie_l(S).lerp(hl_l(S), wb)
            if wb < 0.5:
                return tie_l(S).lerp(side, wb / 0.5)
            return side.lerp(hl_l(S), (wb - 0.5) / 0.5)
        p.hands = {"l": (bl,
                         Vector((0, -1, 0.3)).lerp(d2b(Vector((1, 0.3, 0))), wb), Vector((0.4, 0, 1)).lerp(d2b(Vector((0, 1, 0.2))), wb)),
                   "r": (br, Vector((-1, 0, -0.2)).lerp(d2b(Vector((1, 0, 0))), wb), Vector((0, 0, 1)))}
        p.elbows = {"l": lambda S: (S["l"] + Vector((0.4, 0.05, -0.3))).lerp(S["l"] + d2b(Vector((0.1, 0.35, -0.25))), wb),
                    "r": lambda S: (S["r"] + Vector((-0.45, -0.15, 0.15))).lerp(S["r"] + d2b(Vector((-0.3, -0.15, -0.3))), wb)}
        p.fist = {"l": 0.35 + 0.15 * wb, "r": 0.1 + 0.1 * wb}
        p.forced = {"l", "r"}
        return p
    return f


def _lift(w):
    """Підйом руки над головою суперника: піком рано (рука вгору — тоді голова пірнає), до нуля на 0,31."""
    u = min(1.0, w / 0.5 * 1.6)
    return math.sin(math.pi * u ** 0.7)


def a_hold(k=1.0):
    """Поза A (свій простір = кадр A)."""
    def f(Pt):
        P_ = G["P"]
        p = G["stance"](0.0, 2.4, sway=0.0)
        p.pelvis = Vector((*HL["a_pel"], HL["a_pz"]))
        p.pitch = HL["a_pitch"]
        p.yaw = HL["a_yaw"]
        p.roll = HL["a_roll"]
        p.head_yaw = HL["a_hy"]
        p.head_pitch = HL["a_hp"]
        (lx, ly), (rx, ry) = HL["a_feet"]
        zl, zr = HL.get("a_feet_z", (0.0, 0.0))
        p.ankles = (G["foot"](lx, ly) + Vector((0, 0, zl)), G["foot"](rx, ry) + Vector((0, 0, zr)))
        p.feet_yaw = HL["a_fyaw"]
        w = HL["grab"]
        # зчеп: права — на основі шиї B ззаду (B у зчепі дивиться на A, його потилиця — у −Y), ліва — під його правим ліктем
        tie_r = lambda S: SURF.get("tAr") or P_(S, "neck_01", 0.1, Vector(TIE_OFF["Ar"])) or S["r"] + Vector((0.07, -0.27, -0.2))
        tie_l = lambda S: SURF.get("tAl") or P_(S, "lowerarm_r", 0.06, Vector(TIE_OFF["Al"])) or S["l"] + Vector((-0.07, -0.27, -0.2))
        chin = lambda S: SURF.get("Ar") or P_(S, "head", 0.3, Vector((0, 0, -0.12))) or S["r"] + Vector((0.1, -0.3, -0.2))
        ls = HL.get("a_ls", 0.0)                       # удар лівою: 0 замок, 1 замах, 2 кулак у голову B
        arc = math.sin(math.pi * w)
        # права — дугою НАД головою B (через потилицю), а не крізь неї
        def hr(S):                                     # потилиця → дальній бік голови B → під підборіддя
            far = SURF.get("Af")
            if far is None:
                return tie_r(S).lerp(chin(S), w) + Vector(HL["a_arc"]) * arc
            if w < 0.5:
                return tie_r(S).lerp(far, w / 0.5) + Vector(HL["a_arc"]) * _lift(w) * 0.6
            return far.lerp(chin(S), (w - 0.5) / 0.5)
        p.hands = {"r": (hr,
                         Vector((-1, 0, -0.2)).lerp(Vector((1, 0, 0)), w), Vector((0, 0, 1))),
                   "l": (lambda S: _phase(ls, tie_l(S).lerp(chin(S) + Vector(HL["a_lh"]), w),
                                          S["l"] + Vector((0.12, 0.08, 0.28)), SURF.get("Hs") or chin(S)),
                         Vector((0, -1, 0.3)).lerp(Vector((-0.2, 0, -1)), w), Vector((-0.4, 0, 1)).lerp(Vector((-1, 0, 0)), w))}
        # лікоть — за головою B ззовні (плече на його потилиці), передпліччя — під підборіддям
        def er(S):                                     # лікоть — угору над головою B одразу, поки вона не пірнула
            a = S["r"] + Vector((-0.45, -0.15, 0.15))
            b = SURF.get("Ae") or (S["r"] + Vector((-0.3, -0.4, 0.0)))
            return a.lerp(b, w) + Vector((0.0, -0.1, 0.5)) * _lift(w)
        p.elbows = {"r": er,
                    "l": lambda S: ((S["l"] + Vector((0.4, 0.05, -0.3))).lerp(S["l"] + Vector(HL["a_lel"]), w)
                                    .lerp(S["l"] + Vector((0.35, 0.15, 0.05)), min(1.0, ls)))}
        p.fist = {"r": 0.1 + 0.5 * w, "l": 0.35 + 0.4 * w + 0.25 * min(1.0, ls)}
        p.forced = {"l", "r"}
        return p
    return f


SURF = {}


def hl_surface():
    """Цілі кистей на шкірі для фази захвату: B — поперек A ззаду і передпліччя A знизу; A — петля навколо шиї B."""
    A, B = LAB["armA"], LAB["armB"]
    fwd = heading(HL["b_head"])
    SURF["Bl"] = ring_lab.to_rig(B, ring_lab.surface_world(A, "spine_01", 0.6, (0.3, 1.0, 0.0), palm=0.03))
    # права B — упирається A в праве стегно спереду (кадр власника); тягнутись до передпліччя під своїм підборіддям —
    # рука лізе у власну голову
    SURF["Br"] = ring_lab.to_rig(B, ring_lab.surface_world(A, "thigh_r", 0.2, (-0.3, -1.0, 0.2), palm=0.03))
    pb = B.pose.bones["head"]
    c = B.matrix_world @ (pb.head + (pb.tail - pb.head) * 0.4)
    left = Vector((-fwd.y, fwd.x, 0.0))
    up = Vector((0, 0, 1))
    ex, el, ez = HL["a_eloop"]
    SURF["Ae"] = ring_lab.to_rig(A, c + fwd * ex + left * el + up * ez)
    SURF["Af"] = ring_lab.to_rig(A, ring_lab.surface_world(B, "head", 0.4, -left + up * 0.1, palm=0.04))   # дальній бік голови
    hs = HL.get("hs", ("head", 0.6, (0.6, 0.0, 0.8)))      # куди б'є ліва A: (кістка B, частка, звідки) — за ланкою
    SURF["Hs"] = ring_lab.to_rig(A, ring_lab.surface_world(B, hs[0], hs[1], hs[2], palm=0.045))
    cx, cl, cz = HL["a_chin"]
    SURF["Ar"] = ring_lab.to_rig(A, ring_lab.surface_world(B, "head", 0.15, fwd * cx + left * cl + up * cz, palm=0.035))


def push_arm(tag, s, max_depth=0.003, iters=25):
    """Зворотна посадка: вершини тіла суперника, що ввійшли в кисть чи передпліччя руки s (тонка рука не має
    вершин усередині товстої голови — settle цього не бачить), — рука зсувається так, щоб вони лишились на шкірі:
    ціль зап'ястя і полюс ліктя йдуть на усереднене (вершина − найближча точка шкіри руки), кроком ≤ 1 см."""
    me = LAB["rig" + tag]
    other = LAB["armB" if tag == "A" else "armA"]
    worst = 0.0
    for _ in range(iters):
        dg = bpy.context.evaluated_depsgraph_get()
        av = ring_lab._arm_verts(me.arm, s, dg)
        if not av:
            break
        trees = ring_lab._part_trees(me.arm, dg)
        worst, push, n = 0.0, Vector(), 0
        for part in ("body_lowerarms", "body_hands"):
            tree = trees.get(part)
            if not tree:
                continue
            for o in ring_lab._meshes(other):
                if o.name.endswith(("body_hands", "body_lowerarms")) and part == "body_hands":
                    continue
                e = o.evaluated_get(dg); m = e.to_mesh(); mw = e.matrix_world
                for v in list(m.vertices)[::2]:
                    w = mw @ v.co
                    loc, _n, _i, d = tree[0].find_nearest(w, 0.12)
                    if loc is None or d <= max_depth:
                        continue
                    if min((w - a).length for a in av[::7]) > 0.08:      # лише біля цієї руки
                        continue
                    if ring_lab._inside(tree[0], w):
                        worst = max(worst, d)
                        push += (w - loc).normalized() * min(d + 0.003, 0.01)
                        n += 1
                e.to_mesh_clear()
        if worst <= max_depth:
            break
        push /= max(n, 1)
        for k in ("wrist_" + s, "elbow_" + s):
            em = me.empties[k]; em.location = em.location + push
        bpy.context.view_layer.update()
    return round(worst * 100, 1)


def solve_frame():
    """Розв'язати пару для поточних HL: цілі на шкірі, посадка кистей, виштовхування рук."""
    SURF.clear()
    ring_lab.solve(a_hold(), b_hold())
    if HL["grab"] > 0.05:
        for _ in range(2):
            hl_surface()
            ring_lab.solve(a_hold(), b_hold())
    for _ in range(2):
        ring_lab.settle("A", max_depth=0.002); push_arm("A", "r", iters=40); push_arm("A", "l", iters=40)
        ring_lab.settle("B", max_depth=0.002); push_arm("B", "r", iters=20); push_arm("B", "l", iters=20)


def fit_head(tgt, rounds=4):
    """Таз B так, щоб голова (основа кістки head) стала в tgt (кадр A зі світовою висотою)."""
    tgt = Vector(tgt)
    pel = Vector(HL["b_pel"])
    for _ in range(rounds):
        HL["b_pel"] = tuple(pel)
        ring_lab.solve(a_hold(), b_hold())
        B = LAB["armB"]
        e = tgt - B.matrix_world @ B.pose.bones["head"].head
        pel += Vector((e.x, e.y))
        HL["b_drop"] -= e.z * 0.7
    HL["b_pel"] = tuple(pel)


def self_push(tag, s, max_depth=0.004, iters=20):
    """Рука s виходить зі свого тулуба (кисть і передпліччя): ціль зап'ястя — по нормалі назовні, кроком ≤ 1 см."""
    rig = LAB["rig" + tag]
    worst = 0.0
    for _ in range(iters):
        dg = bpy.context.evaluated_depsgraph_get()
        trees = ring_lab._part_trees(rig.arm, dg)
        worst, push, n = 0.0, Vector(), 0
        for v in ring_lab._arm_verts(rig.arm, s, dg)[::2]:
            for part in ("body_torso", "body_head"):
                t = trees[part][0]
                loc, _n, _i, d = t.find_nearest(v, 0.2)
                if loc is None or d <= max_depth or not ring_lab._inside(t, v):
                    continue
                worst = max(worst, d)
                push += (loc - v).normalized() * min(d + 0.003, 0.01)
                n += 1
        if worst <= max_depth:
            break
        push /= max(n, 1)
        for k in ("wrist_" + s, "elbow_" + s):
            e = rig.empties[k]; e.location = e.location + push
        bpy.context.view_layer.update()
    return round(worst * 100, 1)


def self_pen(tol=0.006):
    """Руки у своєму тулубі й голові (кисть за спиною, рука біля грудей): [(см, 'B.l')] понад tol."""
    dg = bpy.context.evaluated_depsgraph_get()
    res = []
    for tag in "AB":
        arm = LAB["arm" + tag]
        trees = ring_lab._part_trees(arm, dg)
        body = [trees[p][0] for p in ("body_torso", "body_head") if p in trees]
        for s in "lr":
            worst = 0.0
            for v in ring_lab._arm_verts(arm, s, dg)[::2]:
                for t in body:
                    loc, _n, _i, d = t.find_nearest(v, 0.2)
                    if loc is not None and d > max(tol, worst) and ring_lab._inside(t, v):
                        worst = d
            if worst > tol:
                res.append((round(worst * 100, 1), f"{tag}.{s}"))
    return res


def _pen(tol=0.004):
    """Найглибші вершини кожної частини A в B і B в A (см), понад tol."""
    dg = bpy.context.evaluated_depsgraph_get()
    res = []
    # по частинах суперника окремо: парність променя через одну сітку з усіх частин (зони тіла перекриваються в
    # суглобах) давала хибні 20–25 см там, де на кадрі голова чиста (перевірено оглядом 29.10.2026)
    for src, me_arm, ot_arm in (("A", LAB["armA"], LAB["armB"]), ("B", LAB["armB"], LAB["armA"])):
        parts_ot = [t for t, _vs in ring_lab._part_trees(ot_arm, dg).values()]
        for part, (_t, vs) in ring_lab._part_trees(me_arm, dg).items():
            deep = []
            for v in vs[::2]:
                for tree in parts_ot:
                    loc, _n, _i, dist = tree.find_nearest(v, 0.3)
                    if loc is not None and dist > tol and ring_lab._inside(tree, v):
                        deep.append(dist)
                        break
            # одна вершина «глибоко» — промінь пройшов крізь відкриту горловину зони тіла, не проникнення
            deep.sort(reverse=True)
            worst = deep[2] if len(deep) >= 3 else 0.0
            if worst > tol:
                res.append((round(worst * 100, 1), f"{src}.{part}"))
    return sorted(res, reverse=True)


# ---------------------------------------------------------------- кліп
# Ключі (кадр 30 к/с → зміни параметрів від попереднього ключа). Фази: 0 зчеп → 4 передчуття (A присідає, тягне
# голову B донизу) → 11 A переступає праворуч, рука петлею через потилицю, B розвертають і гнуть → 16 захват
# з віддачею (B провисає) → 22 тримання → 34 A тисне й кричить у зал, у B підгинаються коліна → 46 B упирається,
# пробує вирвати голову → 58 A тисне знову → 72 тримання.
def _hold(**kw):
    d = dict(HOLD); d.update(kw); return d


HEADLOCK_KEYS = [
    (0, dict(TIE)),
    (4, dict(TIE, a_pz=-0.2, a_pitch=38.0, a_hy=-40.0, a_hp=-5.0, b_pitch=48.0, b_drop=0.1, b_hp=5.0)),   # A випрямляється, рука йде вгору; голова B пірнає лише після того
    (11, dict(TIE, a_pel=(0.12, -0.1), a_pz=-0.2, a_pitch=30.0, a_yaw=10.0, a_roll=0.0, a_hy=-10.0, a_hp=10.0,
              a_feet=((0.26, -0.4), (-0.24, 0.05)), b_head=140.0, b_pel=(-0.538, -0.903), b_drop=0.23,
              b_pelpitch=22.0, b_pitch=56.0, b_yaw=0.0, b_roll=0.0, b_hy=-20.0, b_hp=25.0,
              b_feet=((-0.8, -0.749), (-0.308, -0.94)), grab=0.65)),   # голова B — перед правим плечем A, донизу
    (16, _hold(a_pz=-0.15, a_pitch=12.0, a_hp=0.0, b_drop=0.02, b_hp=32.0, grab=1.0)),
    (22, _hold()),
    (34, _hold(a_pz=-0.1, a_pitch=-6.0, a_roll=10.0, a_hy=25.0, a_hp=-28.0, b_drop=0.03, b_hy=28.0, b_hp=30.0)),
    (46, _hold(a_pitch=8.0, a_hp=-10.0, b_drop=-0.04, b_pitch=44.0, b_hy=8.0, b_hp=18.0)),
    (58, _hold(a_pz=-0.1, a_pitch=-6.0, a_roll=10.0, a_hy=20.0, a_hp=-28.0, b_drop=0.03, b_hy=28.0, b_hp=30.0)),
    (72, _hold()),
]
HEADLOCK_LEN = 72


def _key_feet(keys, name, side):
    """Ключі стопи для steps(): [(кадр, (x, y))]."""
    out = []
    for f, k in keys:
        ft = k[name] if k.get(name) else b_feet_hold(k["b_pel"], k["b_head"])
        out.append((float(f), tuple(ft[side])))
    return out


def headlock_frames(frames=None):
    keys = [(f, dict(k, b_feet=k["b_feet"] or b_feet_hold(k["b_pel"], k["b_head"]))) for f, k in HEADLOCK_KEYS]
    return chain_frames("Chain_Headlock", keys, HEADLOCK_LEN, a_hold, b_hold, hl_surface, frames=frames)


# ================================================================ викрут руки
# Референс (карта tools/blender/moves/Chain_ArmWringer.json, своїми словами): A навпроти на відстані руки, обидві
# кисті на правій руці B (ліва — на зап'ясті згори, права — на кисті); рука B випрямлена до A на рівні пояса й
# закручена (лікоть угору); B зігнутий уперед і розвернутий за викрутом, обличчя донизу-вбік, ліва рука зігнута,
# кисть біля свого правого плеча. Параметри — як у захвату; b_wr — куди A тягне праву кисть B (кадр A, світова
# висота); grab — кисті від зчепу до викруту.
Z0 = 1.0          # підлога лабораторної сцени
AW = dict(a_pel=(0.12, 0.02), a_pz=-0.12, a_pitch=10.0, a_yaw=-12.0, a_roll=-4.0, a_hy=-10.0, a_hp=20.0,
          a_feet=((0.28, -0.22), (-0.12, 0.16)), a_fyaw=(10.0, -20.0),
          b_head=180.0, b_pel=(-0.25, -1.15), b_drop=0.1, b_pelpitch=10.0, b_pitch=32.0, b_yaw=-25.0, b_roll=10.0,
          b_hy=-30.0, b_hp=25.0, b_feet=((-0.38, -0.82), (0.12, -1.18)), b_wr=(0.06, -0.55, 1.95), grab=1.0)
# пара на відстані руки (лабораторія 29.10.2026: проникнень немає)
AW_TIE = dict(AW, a_pel=TIE["a_pel"], a_pz=TIE["a_pz"], a_pitch=TIE["a_pitch"], a_yaw=TIE["a_yaw"],
              a_roll=TIE["a_roll"], a_hy=TIE["a_hy"], a_hp=TIE["a_hp"], a_feet=TIE["a_feet"], a_fyaw=TIE["a_fyaw"],
              b_head=180.0, b_pel=TIE["b_pel"], b_drop=TIE["b_drop"], b_pelpitch=0.0, b_pitch=TIE["b_pitch"],
              b_yaw=TIE["b_yaw"], b_roll=TIE["b_roll"], b_hy=TIE["b_hy"], b_hp=0.0, b_feet=TIE["b_feet"], grab=0.0)


def aw_b(k=1.0):
    """Поза B у викруті (кадр A → простір B)."""
    def f(Pt):
        P_ = G["P"]
        fwd = heading(HL["b_head"])
        p = G["stance"](0.0, 2.4, sway=0.0)
        pel = Vector((*HL["b_pel"], 0.0))
        p.pelvis = a2b(pel) + Vector((0, 0, -0.09 - HL["b_drop"]))
        db = d2b(fwd)
        p.pelvis_yaw = math.degrees(math.atan2(db.x, -db.y))
        p.pelvis_pitch = HL["b_pelpitch"]
        p.pitch, p.yaw, p.roll = HL["b_pitch"], HL["b_yaw"], HL["b_roll"]
        p.head_yaw = p.pelvis_yaw + HL["b_hy"]
        p.head_pitch = HL["b_hp"]
        fl, fr = (Vector((*xy, 0.0)) for xy in HL["b_feet"])
        zl, zr = HL.get("b_feet_z", (0.0, 0.0))
        p.ankles = (G["foot"](*a2b(fl).xy) + Vector((0, 0, zl)), G["foot"](*a2b(fr).xy) + Vector((0, 0, zr)))
        p.feet_yaw = (p.pelvis_yaw + 10.0, p.pelvis_yaw - 14.0)
        w = HL["grab"]
        tie_r = lambda S: SURF.get("tBr") or P_(S, "neck_01", 0.1, Vector(TIE_OFF["Br"])) or S["r"] + Vector((0, -0.3, -0.3))
        tie_l = lambda S: SURF.get("tBl") or P_(S, "lowerarm_r", 0.06, Vector(TIE_OFF["Bl"])) or S["l"] + Vector((0, -0.3, -0.3))
        wr = a2b(Vector(HL["b_wr"]) - Vector((0, 0, Z0)))               # права кисть — туди, куди тягне A
        own = lambda S: S["chest"] + Vector((-0.12, -0.12, 0.1))         # ліва — біля свого правого плеча
        arc = math.sin(math.pi * w) * 0.06
        p.hands = {"r": (lambda S: tie_r(S).lerp(wr, w) + Vector((0, 0, arc)),
                         Vector((-1, 0, -0.2)).lerp(Vector((0, -1, 0)), w), Vector((0, 0, 1)).lerp(Vector((1, 0, 0.3)), w)),
                   "l": (lambda S: tie_l(S).lerp(own(S), w),
                         Vector((0, -1, 0.3)).lerp(Vector((-1, 0, 0.3)), w), Vector((0.4, 0, 1)).lerp(Vector((0, -1, 0)), w))}
        # права рука закручена: лікоть угору-назовні; ліва — лікоть угору перед грудьми (реакція)
        relb = Vector(HL.get("b_relb", (-0.25, 0.1, 0.45)))
        p.elbows = {"r": lambda S: (S["r"] + Vector((-0.45, -0.15, 0.15))).lerp(S["r"] + relb, w),
                    "l": lambda S: (S["l"] + Vector((0.4, 0.05, -0.3))).lerp(S["l"] + Vector((0.2, -0.35, 0.05)), w)}
        p.fist = {"r": 0.3 + 0.4 * w, "l": 0.35 + 0.35 * w}
        p.forced = {"l", "r"}
        return p
    return f


def aw_a(k=1.0):
    """Поза A у викруті (свій простір)."""
    def f(Pt):
        P_ = G["P"]
        p = G["stance"](0.0, 2.4, sway=0.0)
        p.pelvis = Vector((*HL["a_pel"], HL["a_pz"]))
        p.pitch, p.yaw, p.roll = HL["a_pitch"], HL["a_yaw"], HL["a_roll"]
        p.head_yaw, p.head_pitch = HL["a_hy"], HL["a_hp"]
        (lx, ly), (rx, ry) = HL["a_feet"]
        zl, zr = HL.get("a_feet_z", (0.0, 0.0))
        p.ankles = (G["foot"](lx, ly) + Vector((0, 0, zl)), G["foot"](rx, ry) + Vector((0, 0, zr)))
        p.feet_yaw = HL["a_fyaw"]
        w = HL["grab"]
        tie_r = lambda S: SURF.get("tAr") or P_(S, "neck_01", 0.1, Vector(TIE_OFF["Ar"])) or S["r"] + Vector((0.07, -0.27, -0.2))
        tie_l = lambda S: SURF.get("tAl") or P_(S, "lowerarm_r", 0.06, Vector(TIE_OFF["Al"])) or S["l"] + Vector((-0.07, -0.27, -0.2))
        wrist = lambda S: SURF.get("Wl") or P_(S, "lowerarm_r", 0.92, Vector((0, 0, 0.05))) or S["l"] + Vector((0, -0.3, -0.3))
        hand = lambda S: SURF.get("Wr") or P_(S, "hand_r", 0.5, Vector((0, 0, 0.04))) or S["r"] + Vector((0, -0.3, -0.3))
        arc = math.sin(math.pi * w) * 0.08
        p.hands = {"l": (lambda S: tie_l(S).lerp(wrist(S), w), Vector((0, -1, 0.3)).lerp(Vector((-1, -0.3, -0.6)), w),
                         Vector((-0.4, 0, 1)).lerp(Vector((0, -1, 0)), w)),
                   "r": (lambda S: tie_r(S).lerp(hand(S), w) + Vector((0, 0, arc)),
                         Vector((-1, 0, -0.2)).lerp(Vector((1, -0.3, -0.6)), w), Vector((0, 0, 1)).lerp(Vector((0, -1, 0.2)), w))}
        p.elbows = {"l": lambda S: (S["l"] + Vector((0.4, 0.05, -0.3))).lerp(S["l"] + Vector((0.35, 0.05, -0.3)), w),
                    "r": lambda S: (S["r"] + Vector((-0.45, -0.15, 0.15))).lerp(S["r"] + Vector((-0.35, 0.05, -0.3)), w)}
        p.fist = {"l": 0.35 + 0.45 * w, "r": 0.1 + 0.6 * w}
        p.forced = {"l", "r"}
        return p
    return f


def aw_surface():
    """Кисті A на шкірі правої руки B: ліва — на зап'ясті згори, права — на кисті збоку."""
    A, B = LAB["armA"], LAB["armB"]
    SURF["Wl"] = ring_lab.to_rig(A, ring_lab.surface_world(B, "lowerarm_r", 0.9, (0.0, 0.0, 1.0), palm=0.03))
    SURF["Wr"] = ring_lab.to_rig(A, ring_lab.surface_world(B, "hand_r", 0.45, (1.0, 0.0, 0.2), palm=0.03))


def _aw(**kw):
    d = dict(AW); d.update(kw); return d


ARMWRINGER_KEYS = [
    (0, dict(AW_TIE)),
    (4, dict(AW_TIE, a_pz=-0.2, a_pitch=40.0, b_pitch=40.0)),
    (12, _aw(a_pitch=20.0, a_yaw=0.0, b_pitch=24.0, b_yaw=-5.0, b_roll=0.0, b_hy=-20.0, b_hp=5.0, b_drop=0.06,
             b_wr=(0.02, -0.6, 2.02), grab=0.85)),
    (18, _aw(a_yaw=-20.0, a_pitch=6.0, b_pitch=40.0, b_yaw=-35.0, b_roll=14.0, b_drop=0.14, b_hy=-40.0, b_hp=30.0,
             b_wr=(0.08, -0.53, 1.88))),
    (26, _aw()),
    (38, _aw(a_yaw=-18.0, a_hp=10.0, b_pitch=38.0, b_yaw=-32.0, b_roll=14.0, b_drop=0.13, b_hy=-38.0, b_hp=32.0,
             b_wr=(0.08, -0.54, 1.9))),
    (50, _aw(a_yaw=-8.0, b_pitch=28.0, b_yaw=-18.0, b_hy=-20.0, b_hp=15.0, b_drop=0.08, b_wr=(0.05, -0.57, 1.97))),
    (66, _aw()),
]
ARMWRINGER_LEN = 66


def chain_frames(name, keys, length, fa, fb, surf, blend_in=0, frames=None, smooth=True):
    """Ланка чейну покадрово (як headlock_frames): криві параметрів за ключами, кадр розв'язується заново, кисті
    сідають, руки виштовхуються; перші blend_in кадрів — вхід з останнього кадру зчепу. Стопи — ключі в кожному
    ключі (a_feet, b_feet), між різними — крок з підйомом."""
    T = G["track"]
    ring_lab.place((0, -1.18, 0), 180, z=1.0)
    base = keys[0][1]
    scal = [n for n, v in base.items() if isinstance(v, float)]
    tup = [n for n, v in base.items() if isinstance(v, tuple) and n not in ("a_feet", "b_feet")
           and isinstance(v[0], (int, float))]                 # ("hs" — кістка-ціль, не інтерполюється)
    feet = {(nm, s): [(float(f), tuple(k[nm][s])) for f, k in keys] for nm in ("a_feet", "b_feet") for s in (0, 1)}
    la, lb = load_frames("Lockup")
    tie_local()
    out = {"A": [], "B": []}
    log = []
    corr = {}
    for f in (frames or range(length + 1)):
        ff = float(f)
        HL.clear(); HL.update(base)
        for n in scal + tup:
            HL[n] = T(ff, [(k, v[n]) for k, v in keys])
        HL["grab"] = max(0.0, min(1.0, HL["grab"]))
        for nm in ("a_feet", "b_feet"):
            l, r = G["steps"](ff, feet[(nm, 0)], feet[(nm, 1)])
            HL[nm] = (tuple(l.xy), tuple(r.xy))
            HL[nm + "_z"] = (l.z - 0.071, r.z - 0.071)
        SURF.clear()
        ring_lab.solve(fa(), fb())
        for _ in range(2):
            tie_points()
            if HL["grab"] > 0.05:
                surf()
            ring_lab.solve(fa(), fb())
        # посадка з теплого старту: поправка цілей з попереднього кадру (згасає), щоб руки не стрибали через кадр
        e0 = {}
        for tag in "AB":
            rig = LAB["rig" + tag]
            for k in ("wrist_l", "wrist_r", "elbow_l", "elbow_r"):
                e = rig.empties[k]
                e0[(tag, k)] = e.location.copy()
                e.location = e.location + corr.get((tag, k), Vector()) * 0.85
        bpy.context.view_layer.update()
        for _ in range(2):
            ring_lab.settle("A", max_depth=0.002); push_arm("A", "r", iters=40); push_arm("A", "l", iters=40)
            ring_lab.settle("B", max_depth=0.002); push_arm("B", "r", iters=20); push_arm("B", "l", iters=20)
            for tag in "AB":
                for sd in "lr":
                    self_push(tag, sd)
        for (tag, k), b0 in e0.items():
            corr[(tag, k)] = LAB["rig" + tag].empties[k].location - b0
        ca, cb = ring_lab.capture("A"), ring_lab.capture("B")
        if f < blend_in:
            w = G["smooth"](f / blend_in)
            ca = {n: (la[-1][n][0].lerp(l, w), la[-1][n][1].slerp(r, w)) for n, (l, r) in ca.items()}
            cb = {n: (lb[-1][n][0].lerp(l, w), lb[-1][n][1].slerp(r, w)) for n, (l, r) in cb.items()}
        out["A"].append(ca); out["B"].append(cb)
        print(f"[{name}] кадр {f} розв'язано", flush=True)
    if smooth:                                        # удари не згладжуються: згладжування зрізає пік контакту
        out = {t: smooth_frames(fr, keep=blend_in) for t, fr in out.items()}
    LAB["frames"] = out
    for f in range(len(out["A"])):                       # перевірка — того, що піде в кліп (FK після згладжування)
        show_fk(f)
        pen = _pen() + self_pen()
        pen.sort(reverse=True)
        log.append((f, pen[:3]))
        print(f"[{name}] кадр {f}: {pen[:3]}", flush=True)
    LAB["contacts"] = []
    LAB["name"] = name
    return log


def show_fk(f):
    """Кадр f кліпу чистим FK (усі обмеження вимкнені) — саме те, що піде в FBX. ring_lab.show() вмикає назад IK
    ніг зі старими цілями, тому для перевірки не годиться."""
    for tag in ("A", "B"):
        G["set_basis"](LAB["rig" + tag], LAB["frames"][tag][f])
    bpy.context.view_layer.update()


def smooth_frames(frames, keep=0, w=(1.0, 2.0, 3.0, 2.0, 1.0)):
    """Часове згладжування кліпу (повороти кісток і зсув таза): прибирає тремтіння від покадрової посадки рук.
    Перші keep кадрів (вхід зі зчепу) і останній — без змін."""
    n, h = len(frames), len(w) // 2
    out = []
    for i, fr in enumerate(frames):
        if i < keep or i == n - 1:
            out.append(fr); continue
        pose = {}
        for b, (l0, q0) in fr.items():
            loc, q, tw = Vector(), Quaternion((0, 0, 0, 0)), 0.0
            for j, wj in enumerate(w):
                k = min(n - 1, max(0, i + j - h))
                l, r = frames[k][b]
                if r.dot(q0) < 0:
                    r = -r
                loc += l * wj
                q = Quaternion((q.w + r.w * wj, q.x + r.x * wj, q.y + r.y * wj, q.z + r.z * wj))
                tw += wj
            q.normalize()
            pose[b] = (loc / tw, q)
        out.append(pose)
    return out


def armwringer_frames():
    return chain_frames("Chain_ArmWringer", ARMWRINGER_KEYS, ARMWRINGER_LEN, aw_a, aw_b, aw_surface)


# ================================================================ хаммерлок
# Референс (tools/blender/moves/Chain_Hammerlock.json, своїми словами): з викруту A розвертає B спиною до себе; права
# рука B зігнута за спиною (передпліччя поперек попереку, кисть під лівою лопаткою), A лівою тисне його зап'ястя
# вгору, правою давить на праве плече; B зігнутий уперед, на носках від болю, ліва рука вільна — тягнеться вперед
# або назад до A. grab: 0 — хват зчепу, 0,5 — рука B відведена вбік (як у викруті), 1 — за спиною.
HM = dict(AW, a_pel=(0.02, -0.28), a_pz=-0.1, a_pitch=18.0, a_yaw=-8.0, a_roll=0.0, a_hy=-8.0, a_hp=20.0,
          a_feet=((0.18, -0.42), (-0.22, -0.08)), a_fyaw=(10.0, -20.0),
          b_head=0.0, b_pel=(-0.12, -0.78), b_drop=0.04, b_pelpitch=18.0, b_pitch=44.0, b_yaw=10.0, b_roll=8.0,
          b_hy=-20.0, b_hp=-5.0, b_feet=((-0.32, -1.06), (0.1, -0.86)), b_lh=(0.0, 0.0, 0.0), grab=1.0,
          b_behind=(1.0, 0.24, 0.05))     # кисть між лопатками, без входу у власну спину (лабораторія 29.10.2026)
HM_TIE = dict(HM, **{k: AW_TIE[k] for k in AW_TIE if k not in ("b_wr",)}, b_lh=(0.0, 0.0, 0.0))


def _body_axes(S):
    """Осі тулуба зі своїх кісток (простір ригу): вгору вздовж хребта, вліво (між плечима), назад (зі спини)."""
    up = (S["chest"] - S["pelvis"]).normalized()
    lr = (S["l"] - S["r"]).normalized()
    back = up.cross(lr).normalized()
    return up, lr, back


def hm_b(k=1.0):
    """Поза B у хаммерлоку (кадр A → простір B)."""
    def f(Pt):
        P_ = G["P"]
        fwd = heading(HL["b_head"])
        p = G["stance"](0.0, 2.4, sway=0.0)
        pel = Vector((*HL["b_pel"], 0.0))
        p.pelvis = a2b(pel) + Vector((0, 0, -0.09 - HL["b_drop"]))
        db = d2b(fwd)
        p.pelvis_yaw = math.degrees(math.atan2(db.x, -db.y))
        p.pelvis_pitch = HL["b_pelpitch"]
        p.pitch, p.yaw, p.roll = HL["b_pitch"], HL["b_yaw"], HL["b_roll"]
        p.head_yaw = p.pelvis_yaw + HL["b_hy"]
        p.head_pitch = HL["b_hp"]
        fl, fr = (Vector((*xy, 0.0)) for xy in HL["b_feet"])
        zl, zr = HL.get("b_feet_z", (0.0, 0.0))
        p.ankles = (G["foot"](*a2b(fl).xy) + Vector((0, 0, zl)), G["foot"](*a2b(fr).xy) + Vector((0, 0, zr)))
        p.feet_yaw = (p.pelvis_yaw + 10.0, p.pelvis_yaw - 14.0)
        left = Vector((-fwd.y, fwd.x, 0.0))
        p.knees = (a2b(fl + fwd * 0.6 + left * 0.1) + Vector((0, 0, 0.5)),
                   a2b(fr + fwd * 0.6 - left * 0.1) + Vector((0, 0, 0.5)))
        w = HL["grab"]
        tie_r = lambda S: SURF.get("tBr") or P_(S, "neck_01", 0.1, Vector(TIE_OFF["Br"])) or S["r"] + Vector((0, -0.3, -0.3))
        tie_l = lambda S: SURF.get("tBl") or P_(S, "lowerarm_r", 0.06, Vector(TIE_OFF["Bl"])) or S["l"] + Vector((0, -0.3, -0.3))

        def side(S):                                   # рука відведена вбік-уперед на рівні пояса (як у викруті)
            up, lr, back = _body_axes(S)
            return S["r"] - lr * 0.22 - back * 0.3 - up * 0.42

        def behind(S):                                 # кисть за спиною, під лівою лопаткою
            up, lr, back = _body_axes(S)
            return S["pelvis"].lerp(S["chest"], HL["b_behind"][0]) + back * HL["b_behind"][1] + lr * HL["b_behind"][2]

        def hr(S):
            if w < 0.5:
                return tie_r(S).lerp(side(S), w / 0.5)
            return side(S).lerp(behind(S), (w - 0.5) / 0.5)

        def er(S):                                     # лікоть: від зчепу — убік, за спиною — назовні й назад
            up, lr, back = _body_axes(S)
            out = S["r"] - lr * 0.4 + back * 0.15 - up * 0.3
            return (S["r"] + Vector((-0.45, -0.15, 0.15))).lerp(out, min(1.0, w * 1.5))

        def fr_(S):
            up, lr, back = _body_axes(S)
            return lr if w > 0.5 else Vector((-1, 0, -0.2))

        def ur_(S):
            up, lr, back = _body_axes(S)
            return back if w > 0.5 else Vector((0, 0, 1))
        free = lambda S: S["l"] + Vector(HL["b_lh"]) + Vector((0.1, -0.3, -0.4))     # ліва — вільна, вперед-донизу
        p.hands = {"r": (hr, fr_, ur_),
                   "l": (lambda S: tie_l(S).lerp(free(S), w), Vector((0, -1, 0.3)), Vector((0.4, 0, 1)))}
        p.elbows = {"r": er, "l": lambda S: S["l"] + Vector((0.35, 0.05, -0.35))}
        p.fist = {"r": 0.2 + 0.5 * w, "l": 0.35 - 0.2 * w}
        p.forced = {"l", "r"}
        return p
    return f


def hm_a(k=1.0):
    """Поза A у хаммерлоку (свій простір)."""
    def f(Pt):
        P_ = G["P"]
        p = G["stance"](0.0, 2.4, sway=0.0)
        p.pelvis = Vector((*HL["a_pel"], HL["a_pz"]))
        p.pitch, p.yaw, p.roll = HL["a_pitch"], HL["a_yaw"], HL["a_roll"]
        p.head_yaw, p.head_pitch = HL["a_hy"], HL["a_hp"]
        (lx, ly), (rx, ry) = HL["a_feet"]
        zl, zr = HL.get("a_feet_z", (0.0, 0.0))
        p.ankles = (G["foot"](lx, ly) + Vector((0, 0, zl)), G["foot"](rx, ry) + Vector((0, 0, zr)))
        p.feet_yaw = HL["a_fyaw"]
        w = HL["grab"]
        tie_r = lambda S: SURF.get("tAr") or P_(S, "neck_01", 0.1, Vector(TIE_OFF["Ar"])) or S["r"] + Vector((0.07, -0.27, -0.2))
        tie_l = lambda S: SURF.get("tAl") or P_(S, "lowerarm_r", 0.06, Vector(TIE_OFF["Al"])) or S["l"] + Vector((-0.07, -0.27, -0.2))
        wrist = lambda S: SURF.get("Hl") or P_(S, "lowerarm_r", 0.9, Vector((0, 0, 0.05))) or S["l"] + Vector((0, -0.3, -0.3))
        shoulder = lambda S: SURF.get("Hr") or P_(S, "upperarm_r", 0.15, Vector((0, 0, 0.06))) or S["r"] + Vector((0, -0.3, -0.2))
        p.hands = {"l": (lambda S: tie_l(S).lerp(wrist(S), w), Vector((0, -1, 0.3)).lerp(Vector((-1, 0, -0.3)), w),
                         Vector((-0.4, 0, 1)).lerp(Vector((0, -1, 0.2)), w)),
                   "r": (lambda S: tie_r(S).lerp(shoulder(S), w) + Vector((0, 0, math.sin(math.pi * w) * 0.08)),
                         Vector((-1, 0, -0.2)).lerp(Vector((0, -1, -0.3)), w), Vector((0, 0, 1)))}
        p.elbows = {"l": lambda S: S["l"] + Vector((0.35, 0.1, -0.3)),
                    "r": lambda S: S["r"] + Vector((-0.4, 0.05, -0.2))}
        p.fist = {"l": 0.35 + 0.45 * w, "r": 0.1 + 0.3 * w}
        p.forced = {"l", "r"}
        return p
    return f


def hm_surface():
    """Кисті A на шкірі B: ліва — на правому зап'ясті B ззаду (тисне вгору), права — на його правому плечі згори."""
    A, B = LAB["armA"], LAB["armB"]
    SURF["Hl"] = ring_lab.to_rig(A, ring_lab.surface_world(B, "lowerarm_r", 0.85, (0.0, 1.0, -0.3), palm=0.03))
    SURF["Hr"] = ring_lab.to_rig(A, ring_lab.surface_world(B, "upperarm_r", 0.1, (0.0, 0.3, 1.0), palm=0.03))


def _hm(**kw):
    d = dict(HM); d.update(kw); return d


HAMMERLOCK_KEYS = [
    (0, dict(HM_TIE)),
    (4, dict(HM_TIE, a_pz=-0.2, a_pitch=40.0, b_pitch=40.0)),
    (11, dict(HM, a_pel=(0.12, -0.12), a_pitch=14.0, a_yaw=-15.0, a_hy=-10.0, a_feet=((0.28, -0.4), (-0.14, -0.05)),
              b_head=150.0, b_pel=(-0.24, -1.0), b_drop=0.08, b_pelpitch=8.0, b_pitch=26.0, b_yaw=-20.0,
              b_hy=-30.0, b_hp=15.0, b_feet=((-0.38, -0.72), (0.12, -1.08)), grab=0.5)),
    (19, _hm(b_head=30.0, b_pel=(-0.14, -0.8), b_pitch=38.0, b_hp=10.0, grab=0.85,
             b_feet=((-0.36, -1.02), (0.1, -0.9)))),
    (25, _hm(b_drop=-0.02, b_pitch=34.0, b_hp=-20.0, b_hy=-10.0)),          # ривок угору від болю, на носки
    (36, _hm(a_pitch=24.0, a_pz=-0.14, b_drop=0.06, b_pitch=54.0, b_hp=5.0, b_hy=-30.0)),
    (50, _hm(b_pitch=40.0, b_hy=10.0, b_hp=-10.0, b_lh=(0.1, 0.35, 0.25))),  # тягнеться лівою назад до A
    (62, _hm(a_pitch=24.0, a_pz=-0.14, b_drop=0.07, b_pitch=56.0, b_hp=8.0, b_hy=-30.0)),
    (72, _hm()),
]
HAMMERLOCK_LEN = 72


def hammerlock_frames():
    return chain_frames("Chain_Hammerlock", HAMMERLOCK_KEYS, HAMMERLOCK_LEN, hm_a, hm_b, hm_surface)


# ================================================================ захват корпусу ззаду (go-behind)
# Референс (tools/blender/moves/Chain_Waistlock.json, своїми словами): A заходить за спину B, груди до його спини,
# голова збоку від його плеча; руки обхоплюють пояс B, кисті замкнені на його животі (одна на іншій); B трохи
# зігнутий, лікті назовні, обидві кисті на передпліччях A — розчіплює. Тут розвертаються обидва: a_head — курс A
# (кадр A, як b_head), A обходить B зліва, B доводять спиною до A.
WS = dict(HM, a_head=90.0, a_pel=(-0.58, -0.9), a_pz=-0.16, a_pitch=22.0, a_yaw=0.0, a_roll=-10.0, a_hy=-60.0,
          a_hp=5.0, a_feet=((-0.5, -0.8), (-0.68, -1.15)), a_fyaw=(10.0, -20.0),
          b_head=90.0, b_pel=(-0.2, -0.98), b_drop=0.06, b_pelpitch=8.0, b_pitch=20.0, b_yaw=0.0, b_roll=0.0,
          b_hy=10.0, b_hp=10.0, b_feet=((-0.1, -0.83), (-0.16, -1.13)), grab=1.0)
# вихід за спину: A обходить B з його лівого боку (пірнає під ліву руку), B розвертається лише на чверть
WS_TIE = dict(WS, **{k: HM_TIE[k] for k in HM_TIE if k not in ("a_head",)}, a_head=0.0)


def ws_b(k=1.0):
    """Поза B у захваті корпусу (кадр A → простір B)."""
    def f(Pt):
        P_ = G["P"]
        fwd = heading(HL["b_head"])
        p = G["stance"](0.0, 2.4, sway=0.0)
        pel = Vector((*HL["b_pel"], 0.0))
        p.pelvis = a2b(pel) + Vector((0, 0, -0.09 - HL["b_drop"]))
        db = d2b(fwd)
        p.pelvis_yaw = math.degrees(math.atan2(db.x, -db.y))
        p.pelvis_pitch = HL["b_pelpitch"]
        p.pitch, p.yaw, p.roll = HL["b_pitch"], HL["b_yaw"], HL["b_roll"]
        p.head_yaw = p.pelvis_yaw + HL["b_hy"]
        p.head_pitch = HL["b_hp"]
        fl, fr = (Vector((*xy, 0.0)) for xy in HL["b_feet"])
        zl, zr = HL.get("b_feet_z", (0.0, 0.0))
        p.ankles = (G["foot"](*a2b(fl).xy) + Vector((0, 0, zl)), G["foot"](*a2b(fr).xy) + Vector((0, 0, zr)))
        p.feet_yaw = (p.pelvis_yaw + 10.0, p.pelvis_yaw - 14.0)
        left = Vector((-fwd.y, fwd.x, 0.0))
        p.knees = (a2b(fl + fwd * 0.6 + left * 0.1) + Vector((0, 0, 0.5)),
                   a2b(fr + fwd * 0.6 - left * 0.1) + Vector((0, 0, 0.5)))
        w = HL["grab"]
        wb = G["smooth"]((w - 0.5) / 0.5)              # B хапає руки A, коли ті вже замкнені
        tie_r = lambda S: SURF.get("tBr") or P_(S, "neck_01", 0.1, Vector(TIE_OFF["Br"])) or S["r"] + Vector((0, -0.3, -0.3))
        tie_l = lambda S: SURF.get("tBl") or P_(S, "lowerarm_r", 0.06, Vector(TIE_OFF["Bl"])) or S["l"] + Vector((0, -0.3, -0.3))
        on_r = lambda S: SURF.get("Pr") or P_(S, "lowerarm_r", 0.75, Vector((0, 0, 0.05))) or S["r"] + Vector((0, -0.3, -0.4))
        on_l = lambda S: SURF.get("Pl") or P_(S, "lowerarm_l", 0.75, Vector((0, 0, 0.05))) or S["l"] + Vector((0, -0.3, -0.4))
        p.hands = {"r": (lambda S: tie_r(S).lerp(on_r(S), wb), Vector((-1, 0, -0.2)).lerp(Vector((0.3, -1, -0.5)), wb),
                         Vector((0, 0, 1))),
                   "l": (lambda S: _phase(es, tie_l(S).lerp(on_l(S), wb), wind(S), shoulder(S)),
                         Vector((0, -1, 0.3)).lerp(Vector((-0.3, -1, -0.5)), wb), Vector((0.4, 0, 1)).lerp(Vector((0, 0, 1)), wb))}
        es = HL.get("b_es", 0.0)                       # удар лівим ліктем назад: 0 на руках A, 1 замах уперед, 2 лікоть назад

        def wind(S):
            up, lr, back = _body_axes(S)
            return S["chest"] - back * 0.32 + lr * 0.05 + up * 0.05

        def shoulder(S):
            up, lr, back = _body_axes(S)
            return S["chest"] - back * 0.2 - up * 0.12 + lr * 0.12    # кисть перед своїми грудьми — лікоть назад у ребра

        def el(side):                                  # лікті назовні — в осях свого тулуба (B розвернутий)
            def g(S):
                up, lr, back = _body_axes(S)
                return S[side] + lr * (0.4 if side == "l" else -0.4) - up * 0.3 - back * 0.1
            return g
        def el_l(S):                                   # у ліктьовому ударі — лікоть назад, до голови A
            up, lr, back = _body_axes(S)
            ha = P_(S, "spine_02", 0.5) or (S["l"] + back * 0.5 - up * 0.3 + lr * 0.1)     # лікоть — у ребра A
            return el("l")(S).lerp(ha, min(1.0, max(0.0, es - 0.6)))
        p.elbows = {"r": el("r"), "l": el_l}
        p.fist = {"r": 0.35 + 0.35 * wb, "l": 0.35 + 0.35 * wb}
        p.forced = {"l", "r"}
        return p
    return f


def ws_a(k=1.0):
    """Поза A у захваті корпусу (свій простір; a_head — розворот усього тіла)."""
    def f(Pt):
        P_ = G["P"]
        fwd = heading(HL["a_head"])
        left = Vector((-fwd.y, fwd.x, 0.0))
        p = G["stance"](0.0, 2.4, sway=0.0)
        p.pelvis = Vector((*HL["a_pel"], HL["a_pz"]))
        p.pelvis_yaw = HL["a_head"]
        p.pitch, p.yaw, p.roll = HL["a_pitch"], HL["a_yaw"], HL["a_roll"]
        p.head_yaw, p.head_pitch = HL["a_head"] + HL["a_hy"], HL["a_hp"]
        (lx, ly), (rx, ry) = HL["a_feet"]
        zl, zr = HL.get("a_feet_z", (0.0, 0.0))
        fl, fr = Vector((lx, ly, 0.0)), Vector((rx, ry, 0.0))
        p.ankles = (G["foot"](lx, ly) + Vector((0, 0, zl)), G["foot"](rx, ry) + Vector((0, 0, zr)))
        p.feet_yaw = (HL["a_head"] + HL["a_fyaw"][0], HL["a_head"] + HL["a_fyaw"][1])
        p.knees = (fl + fwd * 0.6 + left * 0.1 + Vector((0, 0, 0.5)), fr + fwd * 0.6 - left * 0.1 + Vector((0, 0, 0.5)))
        w = HL["grab"]
        tie_r = lambda S: SURF.get("tAr") or P_(S, "neck_01", 0.1, Vector(TIE_OFF["Ar"])) or S["r"] + Vector((0.07, -0.27, -0.2))
        tie_l = lambda S: SURF.get("tAl") or P_(S, "lowerarm_r", 0.06, Vector(TIE_OFF["Al"])) or S["l"] + Vector((-0.07, -0.27, -0.2))
        belly = lambda S: SURF.get("Wb") or P_(S, "spine_01", 0.5, Vector((0, -0.15, 0))) or S["r"] + Vector((0, -0.3, -0.4))
        # кисті: права — на животі B, ліва — поверх неї (замок); у дорозі — убік від тіла B, щоб обійти його
        side_r = lambda S: (SURF.get("Sr") or belly(S))
        side_l = lambda S: (SURF.get("Sl") or belly(S))

        def via(a, m, b, u):
            return a.lerp(m, u / 0.5) if u < 0.5 else m.lerp(b, (u - 0.5) / 0.5)
        p.hands = {"r": (lambda S: via(tie_r(S), side_r(S), belly(S), w), Vector((-1, 0, -0.2)).lerp(left, w),
                         Vector((0, 0, 1)).lerp(fwd, w)),
                   "l": (lambda S: via(tie_l(S), side_l(S), belly(S) + left * 0.0 + Vector((0, 0, 0.03)), w),
                         Vector((0, -1, 0.3)).lerp(-left, w), Vector((-0.4, 0, 1)).lerp(fwd, w))}
        p.elbows = {"r": lambda S: (S["r"] + Vector((-0.45, -0.15, 0.15))).lerp(S["r"] - left * 0.35 + Vector((0, 0, -0.3)), w),
                    "l": lambda S: (S["l"] + Vector((0.4, 0.05, -0.3))).lerp(S["l"] + left * 0.35 + Vector((0, 0, -0.3)), w)}
        p.fist = {"r": 0.1 + 0.2 * w, "l": 0.35 - 0.1 * w}     # долоні пласко на животі — зігнуті пальці входять у нього
        p.forced = {"l", "r"}
        return p
    return f


def ws_surface():
    """A: кисті на животі B (спереду), обхідні точки — боки B; B: кисті на передпліччях A згори."""
    A, B = LAB["armA"], LAB["armB"]
    fwd = heading(HL["b_head"])
    left = Vector((-fwd.y, fwd.x, 0.0))
    SURF["Wb"] = ring_lab.to_rig(A, ring_lab.surface_world(B, "spine_01", 0.4, fwd, palm=0.045))
    SURF["Sr"] = ring_lab.to_rig(A, ring_lab.surface_world(B, "spine_01", 0.4, -left + fwd * 0.3, palm=0.08))
    SURF["Sl"] = ring_lab.to_rig(A, ring_lab.surface_world(B, "spine_01", 0.4, left + fwd * 0.3, palm=0.08))
    SURF["Pr"] = ring_lab.to_rig(B, ring_lab.surface_world(A, "lowerarm_r", 0.75, fwd * 0.6 + Vector((0, 0, 0.8)), palm=0.04))
    SURF["Pl"] = ring_lab.to_rig(B, ring_lab.surface_world(A, "lowerarm_l", 0.75, fwd * 0.6 + Vector((0, 0, 0.8)), palm=0.04))


def _ws(**kw):
    d = dict(WS); d.update(kw); return d


WAISTLOCK_KEYS = [
    (0, dict(WS_TIE)),
    (4, dict(WS_TIE, a_pz=-0.28, a_pitch=40.0, a_hy=-50.0, b_pitch=36.0)),                  # A пірнає під руку B
    (10, _ws(a_head=40.0, a_pel=(-0.6, -0.4), a_pz=-0.22, a_pitch=22.0, a_roll=0.0, a_hy=-20.0,
             a_feet=((-0.36, -0.4), (-0.66, -0.2)), b_head=150.0, b_pel=(-0.18, -1.0), b_pitch=26.0, b_hy=-30.0,
             b_feet=((-0.3, -0.7), (0.14, -1.06)), grab=0.4)),                                    # обходить зліва, широко
    (17, _ws(a_pz=-0.2, a_pitch=20.0, b_pitch=16.0, b_hp=-10.0, grab=0.9)),
    (23, _ws(a_pz=-0.14, a_pitch=18.0, b_drop=0.02, b_pitch=12.0, b_hp=-15.0)),   # стиснув — B випрямляється
    (36, _ws(a_pitch=20.0, a_hy=-70.0, b_pitch=22.0, b_yaw=6.0, b_hy=-12.0, b_hp=5.0, b_drop=0.06)),   # B крутиться (голова — від A)
    (50, _ws(a_pitch=20.0, b_pitch=16.0, b_yaw=-5.0, b_hy=-20.0, b_hp=0.0)),
    (62, _ws(a_pz=-0.13, a_pitch=18.0, b_drop=0.03, b_pitch=12.0, b_hp=-15.0)),
    (72, _ws()),
]
WAISTLOCK_LEN = 72


def waistlock_frames():
    return chain_frames("Chain_Waistlock", WAISTLOCK_KEYS, WAISTLOCK_LEN, ws_a, ws_b, ws_surface)


# ================================================================ передній захват голови (front facelock)
# Референс (tools/blender/moves/Chain_FrontFacelock.json, своїми словами): B зігнутий обличчям до A, голова під правою
# пахвою A біля його ребер, обличчям йому за спину; права рука A — петлею через потилицю, передпліччя під підборіддям,
# ліва замикає зап'ястя; A нависає над спиною B. B не розвертається — та сама поза, що й захват збоку, з курсом 175°.
FF = dict(HOLD, b_head=175.0, b_pel=(-0.292, -0.726), b_drop=0.001, b_pelpitch=30.0, b_pitch=55.0,
          a_pitch=35.0, a_pz=-0.16, a_hp=10.0, a_hy=-10.0, b_hy=20.0, b_hp=25.0,
          a_eloop=(-0.1, 0.25, 0.12), a_chin=(0.0, 0.0, -1.0))   # лікоть — з дальнього від тіла A боку голови B


def _ff(**kw):
    d = dict(FF); d.update(kw); return d


FACELOCK_KEYS = [
    (0, dict(TIE)),
    (4, dict(TIE, a_pz=-0.2, a_pitch=38.0, a_hy=-40.0, a_hp=-5.0, b_pitch=48.0, b_drop=0.1, b_hp=5.0)),
    (10, dict(TIE, a_pel=(0.12, -0.08), a_pz=-0.18, a_pitch=36.0, a_yaw=0.0, a_roll=0.0, a_hy=-20.0, a_hp=5.0,
              b_head=178.0, b_pel=(-0.26, -0.86), b_drop=0.06, b_pelpitch=18.0, b_pitch=56.0, b_yaw=0.0, b_roll=0.0,
              b_hy=0.0, b_hp=25.0, grab=0.6)),
    (15, _ff(a_pitch=42.0, b_drop=0.04, b_hp=32.0, grab=1.0)),
    (22, _ff()),
    (34, _ff(a_pitch=28.0, a_pz=-0.13, a_hp=-15.0, a_hy=15.0, b_drop=-0.02, b_pitch=52.0, b_hp=18.0)),   # тягне голову вгору
    (46, _ff(a_pel=(0.04, 0.02), a_pitch=40.0, b_pel=(-0.28, -0.68), b_drop=0.05, b_pitch=58.0, b_hy=5.0)),   # B тисне вперед
    (58, _ff(a_pitch=28.0, a_pz=-0.13, a_hp=-15.0, a_hy=15.0, b_drop=-0.02, b_pitch=52.0, b_hp=18.0)),
    (72, _ff()),
]
FACELOCK_LEN = 72


def facelock_frames():
    keys = [(f, dict(k, b_feet=k["b_feet"] or b_feet_hold(k["b_pel"], k["b_head"]))) for f, k in FACELOCK_KEYS]
    return chain_frames("Chain_FrontFacelock", keys, FACELOCK_LEN, a_hold, b_hold, hl_surface)


# ================================================================ викрут кисті (wristlock)
# Референс (tools/blender/moves/Chain_Wristlock.json, своїми словами): A навпроти, ближче, ніж у викруті руки, обидві
# кисті на кисті B на рівні грудей, заломлює її; рука B зігнута в лікті, лікоть донизу-назовні; B відхиляється вбік
# від болю, плече вгору, голова набік. Пози — викруту руки (aw_a/aw_b) з іншою точкою кисті й ліктем донизу.
WL = dict(AW, a_pel=(0.1, -0.16), a_pitch=6.0, a_yaw=-6.0, b_pel=(-0.22, -1.08), b_pitch=14.0, b_yaw=-15.0,
          b_roll=16.0, b_hy=-35.0, b_hp=-5.0, b_drop=0.05, b_wr=(0.0, -0.66, 2.05), b_relb=(-0.3, -0.05, -0.35),
          b_feet=((-0.36, -0.78), (0.1, -1.12)), a_feet=((0.26, -0.28), (-0.12, 0.12)))
WL_TIE = dict(AW_TIE, b_relb=WL["b_relb"])


def _wl(**kw):
    d = dict(WL); d.update(kw); return d


WRISTLOCK_KEYS = [
    (0, dict(WL_TIE)),
    (4, dict(WL_TIE, a_pz=-0.2, a_pitch=40.0, b_pitch=40.0)),
    (11, _wl(a_pitch=16.0, b_pitch=22.0, b_yaw=-5.0, b_roll=4.0, b_hy=-20.0, b_hp=5.0, b_wr=(0.0, -0.7, 1.96), grab=0.85)),
    (17, _wl(b_roll=22.0, b_hy=-45.0, b_hp=-12.0, b_drop=0.02, b_wr=(0.02, -0.64, 2.12))),   # заломив — B на носки
    (24, _wl()),
    (36, _wl(a_yaw=-14.0, b_roll=24.0, b_yaw=-22.0, b_hy=-45.0, b_wr=(0.03, -0.63, 2.13))),
    (48, _wl(b_roll=8.0, b_yaw=-6.0, b_hy=-15.0, b_hp=10.0, b_drop=0.08, b_wr=(-0.01, -0.68, 1.98))),   # B тягне руку до себе
    (60, _wl(a_yaw=-14.0, b_roll=24.0, b_yaw=-22.0, b_hy=-45.0, b_wr=(0.03, -0.63, 2.13))),
    (66, _wl()),
]
WRISTLOCK_LEN = 66


def wristlock_frames():
    return chain_frames("Chain_Wristlock", WRISTLOCK_KEYS, WRISTLOCK_LEN, aw_a, aw_b, aw_surface)


# ================================================================ відштовхування (розрив зчепу)
# Референс (tools/blender/moves/Chain_ShoveOff.json, своїми словами): A скидає руки B, двома долонями б'є в груди;
# B відлітає назад двома кроками, корпус відкинутий, руки змахують для рівноваги; ловить рівновагу й повертається в
# стійку обличчям до A; A після поштовху випрямляється, розводить руки (виклик). ph — фаза рук: 0 хват зчепу,
# 1 долоні на грудях B (у B — руки злетіли вбік), 2 вартова стійка.
SH = dict(AW_TIE, ph=0.0, a_head=0.0)


def _phase(ph, a, b, c):
    """Точка руки за фазою ph: 0 → a, 1 → b, 2 → c (лінійно між)."""
    return a.lerp(b, ph) if ph <= 1.0 else b.lerp(c, ph - 1.0)


def sh_b(k=1.0):
    def f(Pt):
        P_ = G["P"]
        p = aw_b()(Pt)
        ph = HL["ph"]
        tie_r = lambda S: SURF.get("tBr") or P_(S, "neck_01", 0.1, Vector(TIE_OFF["Br"])) or S["r"] + Vector((0, -0.3, -0.3))
        tie_l = lambda S: SURF.get("tBl") or P_(S, "lowerarm_r", 0.06, Vector(TIE_OFF["Bl"])) or S["l"] + Vector((0, -0.3, -0.3))
        FWD, DOWN, UP, LEFT, RIGHT = (G[n] for n in ("FWD", "DOWN", "UP", "LEFT", "RIGHT"))
        guard = {"l": lambda S: S["l"] + Vector((-0.07, -0.27, -0.2)), "r": lambda S: S["r"] + Vector((0.07, -0.27, -0.2))}
        flail = {"l": lambda S: S["l"] + Vector((0.42, -0.05, -0.05)), "r": lambda S: S["r"] + Vector((-0.42, -0.05, -0.05))}
        p.hands = {"r": (lambda S: _phase(ph, tie_r(S), flail["r"](S), guard["r"](S)), FWD + DOWN * 0.3, LEFT + UP * 0.3),
                   "l": (lambda S: _phase(ph, tie_l(S), flail["l"](S), guard["l"](S)), FWD + DOWN * 0.3, RIGHT + UP * 0.3)}
        p.elbows = {"l": lambda S: S["l"] + Vector((0.3, 0.05, -0.35)), "r": lambda S: S["r"] + Vector((-0.3, 0.05, -0.35))}
        p.fist = {"l": 0.25, "r": 0.25}
        return p
    return f


def sh_a(k=1.0):
    def f(Pt):
        P_ = G["P"]
        p = aw_a()(Pt)
        ph = HL["ph"]
        tie_r = lambda S: SURF.get("tAr") or P_(S, "neck_01", 0.1, Vector(TIE_OFF["Ar"])) or S["r"] + Vector((0.07, -0.27, -0.2))
        tie_l = lambda S: SURF.get("tAl") or P_(S, "lowerarm_r", 0.06, Vector(TIE_OFF["Al"])) or S["l"] + Vector((-0.07, -0.27, -0.2))
        chest = {s: (lambda S, s=s: SURF.get("C" + s) or S[s] + Vector((0.0, -0.45, -0.1))) for s in "lr"}
        open_ = {"l": lambda S: S["l"] + Vector((0.45, -0.15, 0.05)), "r": lambda S: S["r"] + Vector((-0.45, -0.15, 0.05))}
        p.hands = {"r": (lambda S: _phase(ph, tie_r(S), chest["r"](S), open_["r"](S)), Vector((0, 0, 1)), Vector((0, 1, 0))),
                   "l": (lambda S: _phase(ph, tie_l(S), chest["l"](S), open_["l"](S)), Vector((0, 0, 1)), Vector((0, 1, 0)))}
        p.elbows = {"l": lambda S: S["l"] + Vector((0.3, 0.05, -0.35)), "r": lambda S: S["r"] + Vector((-0.3, 0.05, -0.35))}
        p.fist = {"l": 0.1, "r": 0.1}
        return p
    return f


def sh_surface():
    """Долоні A — на грудях B (ліва — на його правому грудному м'язі, права — на лівому)."""
    A, B = LAB["armA"], LAB["armB"]
    SURF["Cl"] = ring_lab.to_rig(A, ring_lab.surface_world(B, "spine_03", 0.5, (0.12, 1.0, 0.1), palm=0.03))
    SURF["Cr"] = ring_lab.to_rig(A, ring_lab.surface_world(B, "spine_03", 0.5, (-0.12, 1.0, 0.1), palm=0.03))


def _sh(**kw):
    d = dict(SH); d.update(kw); return d


SHOVE_KEYS = [
    (0, _sh()),
    (5, _sh(a_pz=-0.22, a_pitch=50.0, b_pitch=44.0, ph=0.35)),                                  # скидає руки, замах
    (9, _sh(a_pel=(0.18, -0.24), a_pz=-0.14, a_pitch=24.0, a_hy=-10.0, a_hp=5.0, a_feet=((0.3, -0.62), (-0.18, -0.13)),
            ph=1.0, b_pitch=20.0)),                                                              # поштовх
    (13, _sh(a_pel=(0.16, -0.26), a_pitch=14.0, a_hp=-5.0, a_feet=((0.3, -0.62), (-0.18, -0.13)), ph=1.25,
             b_pel=(-0.2, -1.3), b_pitch=-14.0, b_hp=-18.0, b_drop=0.04, b_feet=((-0.3, -0.67), (0.18, -1.42)))),   # B відлітає
    (19, _sh(a_pel=(0.12, -0.2), a_pitch=6.0, a_hy=0.0, a_hp=-12.0, a_feet=((0.3, -0.62), (-0.14, -0.2)), ph=1.6,
             b_pel=(-0.18, -1.62), b_pitch=-8.0, b_hp=-10.0, b_drop=0.1, b_feet=((-0.32, -1.66), (0.18, -1.42)))),
    (27, _sh(a_pel=(0.1, -0.18), a_pitch=4.0, a_hp=-15.0, a_feet=((0.3, -0.62), (-0.14, -0.2)), ph=2.0,
             b_pel=(-0.16, -1.7), b_pitch=18.0, b_hp=-6.0, b_drop=0.06, b_feet=((-0.32, -1.66), (0.12, -1.86)))),   # ловить рівновагу
    (42, _sh(a_pel=(0.1, -0.18), a_pitch=10.0, a_hp=-8.0, a_feet=((0.3, -0.62), (-0.14, -0.2)), ph=2.0,
             b_pel=(-0.16, -1.72), b_pitch=16.0, b_hp=-8.0, b_drop=0.06, b_feet=((-0.32, -1.66), (0.12, -1.86)))),
]
SHOVE_LEN = 42


def shove_frames():
    return chain_frames("Chain_ShoveOff", SHOVE_KEYS, SHOVE_LEN, sh_a, sh_b, sh_surface)


# ================================================================ захват збоку + удари кулаком по голові
# Референс (moves/Chain_Headlock_Punches.json, своїми словами): у захваті збоку A розмикає замок і б'є вільною лівою
# зверху по голові B (три удари з замахом від плеча); на кожен удар голова й коліна B просідають, B смикається.
def _hp_keys():
    k = [(f, dict(v)) for f, v in HEADLOCK_KEYS if f <= 22]
    hold = dict(HOLD, a_ls=0.0)
    for f, v in k:
        v.setdefault("a_ls", 0.0)
    for f0 in (26, 38, 50):                                    # замах → удар → віддача
        k.append((f0, dict(hold, a_ls=1.0, a_pitch=0.0, a_roll=10.0, a_hp=-10.0)))
        k.append((f0 + 4, dict(hold, a_ls=2.0, a_pitch=10.0, a_roll=0.0, b_drop=0.05, b_hp=34.0, b_hy=26.0)))
        k.append((f0 + 8, dict(hold, a_ls=1.2, b_drop=0.0)))
    k.append((66, dict(hold, a_ls=0.0)))
    k.append((72, dict(hold, a_ls=0.0)))
    return k


def headlock_punches_frames():
    keys = [(f, dict(k, b_feet=k["b_feet"] or b_feet_hold(k["b_pel"], k["b_head"]))) for f, k in _hp_keys()]
    return chain_frames("Chain_Headlock_Punches", keys, 72, a_hold, b_hold, hl_surface, smooth=False)


# ================================================================ передній захват + удари передпліччям по спині
# Референс (moves/Chain_FrontFacelock_Clubs.json, своїми словами): з переднього захвату A вільною лівою б'є
# передпліччям-«молотом» зверху по спині B між лопатками, тричі; B на кожен удар просідає, коліна підгинаються.
def _fc_keys():
    k = [(f, dict(v, a_ls=0.0)) for f, v in FACELOCK_KEYS if f <= 22]
    hold = dict(FF, a_ls=0.0)
    for f0 in (26, 38, 50):
        k.append((f0, dict(hold, a_ls=1.0, a_pitch=20.0, a_roll=8.0, a_hp=-5.0)))
        k.append((f0 + 4, dict(hold, a_ls=2.0, a_pitch=42.0, a_roll=0.0, b_drop=0.06, b_pitch=60.0, b_hp=30.0)))
        k.append((f0 + 8, dict(hold, a_ls=1.2, b_drop=0.01)))
    k.append((66, dict(hold)))
    k.append((72, dict(hold)))
    return [(f, dict(v, hs=("spine_03", 0.3, (0.2, 0.0, 1.0)))) for f, v in k]


def facelock_clubs_frames():
    keys = [(f, dict(k, b_feet=k["b_feet"] or b_feet_hold(k["b_pel"], k["b_head"]))) for f, k in _fc_keys()]
    return chain_frames("Chain_FrontFacelock_Clubs", keys, 72, a_hold, b_hold, hl_surface, smooth=False)


# ================================================================ захват корпусу + удари ліктем назад (вихід)
# Референс (moves/Chain_Waistlock_Elbows.json, своїми словами): у захваті корпусу ззаду B звільняє ліву руку й б'є
# ліктем назад у ребра A — тричі, з замахом уперед; A на кожен удар згинається й послаблює хват. (Удар у голову
# не дістає: голова A — за плечем B далі, ніж плече й лікоть, перевірено 29.10.2026.)
def _we_keys():
    k = [(f, dict(v, b_es=0.0)) for f, v in WAISTLOCK_KEYS if f <= 23]
    hold = dict(WS, b_es=0.0)
    for f0 in (27, 39, 51):
        k.append((f0, dict(hold, b_es=1.0, b_yaw=-6.0, b_pitch=18.0, b_hy=10.0)))
        k.append((f0 + 4, dict(hold, b_es=2.0, b_yaw=14.0, b_pitch=10.0, b_hy=-10.0,
                               a_hp=12.0, a_pitch=30.0, a_pz=-0.18)))      # A згинається від удару в ребра
        k.append((f0 + 8, dict(hold, b_es=1.3, a_hp=0.0)))
    k.append((66, dict(hold)))
    k.append((72, dict(hold)))
    return k


def waistlock_elbows_frames():
    return chain_frames("Chain_Waistlock_Elbows", _we_keys(), 72, ws_a, ws_b, ws_surface, smooth=False)
