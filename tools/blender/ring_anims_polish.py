# Ітерація 2 анімацій «Рингу» (власник 03.11.2026: «Давай вторую итерацию анимаций всех… вторая это среднее между
# сейчас и АА и ближе к АА»). Шар «оживлення» поверх розв'язаних кадрів кожного кліпу (exec у ring_anims.py):
#   перекриття й запізнення (закон 5) — орієнтація кожної кістки ланцюжків таз → хребет → шия → голова і ключиця →
#   плече → передпліччя → кисть → пальці веде пружина з затуханням у просторі ригу; що ближче до кінця ланцюжка,
#   то м'якша пружина — кінцівка запізнюється за тулубом і проскакує після різкої зупинки (follow-through);
#   повільний вхід/вихід (закон 6) — пружина зрізає пікові прискорення (ривки у звіті);
#   вторинна дія (закон 8) — дихання грудьми й ключицями.
# Ноги й таз не чіпаються (стопи на маті, ковзання — 0). У парних кліпах руки, що тримають суперника, лишаються як
# розв'язав IK (інакше кисть зісковзує з тіла), а хребет пружинить жорсткіше. На краях кліпу шар плавно сходить до
# нуля — вузли стикування (stance, lying_back…) точні; петлі проходять двічі, щоб пружина зійшлась по колу.

POLISH = "--no-polish" not in _args
# кістка: (ω, рад/с — жорсткість; найбільше відхилення від розв'язаної пози, °)
SPRING = {"spine_01": (34.0, 3.0), "spine_02": (28.0, 4.0), "spine_03": (24.0, 5.0), "neck_01": (19.0, 6.0),
          "head": (15.0, 8.0),
          "clavicle_l": (24.0, 4.0), "clavicle_r": (24.0, 4.0), "upperarm_l": (20.0, 6.0), "upperarm_r": (20.0, 6.0),
          "lowerarm_l": (17.0, 7.0), "lowerarm_r": (17.0, 7.0), "hand_l": (14.0, 9.0), "hand_r": (14.0, 9.0)}
FINGER_SPRING = (12.0, 10.0)
ZETA = 0.62                       # затухання: <1 — легкий проскок (follow-through), без «желе»
ARM_BONES = {"clavicle_l", "clavicle_r", "upperarm_l", "upperarm_r", "lowerarm_l", "lowerarm_r", "hand_l", "hand_r"}
FINGERS = ("thumb_", "index_", "middle_", "ring_", "pinky_")
BREATH = {"spine_02": 0.7, "spine_03": 0.5, "clavicle_l": -0.6, "clavicle_r": -0.6}   # ° на вдиху (нахил назад)


def _rotvec(q):
    """Кватерніон → вектор повороту (вісь × кут)."""
    if q.w < 0:
        q = -q
    a = 2.0 * math.acos(max(-1.0, min(1.0, q.w)))
    s = math.sqrt(max(1e-12, 1.0 - q.w * q.w))
    return Vector((q.x / s, q.y / s, q.z / s)) * a if a > 1e-7 else Vector()


def _expq(v):
    a = v.length
    return Quaternion(v / a, a) if a > 1e-9 else Quaternion()


def _order(arm):
    out, seen = [], set()
    def add(b):
        if b.name in seen:
            return
        if b.parent:
            add(b.parent)
        seen.add(b.name); out.append(b)
    for b in arm.data.bones:
        add(b)
    return out


def _fk(arm, basis, order):
    """Матриці кісток у просторі ригу з локальних поворотів (та сама формула, що в cap_basis, навпаки)."""
    m = {}
    for b in order:
        loc, rot = basis[b.name]
        lm = Matrix.Translation(loc) @ rot.to_matrix().to_4x4()
        rel = (b.parent.matrix_local.inverted() @ b.matrix_local) if b.parent else b.matrix_local
        m[b.name] = (m[b.parent.name] @ rel @ lm) if b.parent else (rel @ lm)
    return m


def _contact_pts(arm, cap):
    W = arm.matrix_world
    bones = arm.data.bones
    return {n: (W @ cap[n].translation, W @ (cap[n] @ Vector((0.0, bones[n].length, 0.0)))) for n in CONTACT_BONES}


JERK_MAX = 18.0                   # °/кадр²: обмежувач ривка (ітерація 2; у звіті було до 75)
# ноги й таз — не тут: стопи стоять на маті через IK, згладжування стегна зсувало опорну стопу (ковзання)
JERK_BONES = ("spine_01", "spine_02", "spine_03", "neck_01", "head", "clavicle_l", "clavicle_r",
              "upperarm_l", "upperarm_r", "lowerarm_l", "lowerarm_r", "hand_l", "hand_r")


def limit_jerk(basis_seq, keep=(), passes=6, freeze=None):
    """Де кутове прискорення локального повороту кістки понад JERK_MAX, кадр підтягується до середини сусідніх
    (slerp): пік прискорення зрізається, а швидкість руху лишається — удар не «в'язне». Перший і останній кадри
    (вузли стикування) не чіпаються; кістки з keep — теж."""
    n = len(basis_seq)
    if n < 5:
        return basis_seq
    seq = [dict(b) for b in basis_seq]
    lim = math.radians(JERK_MAX)
    for _ in range(passes):
        changed = False
        for name in JERK_BONES:
            if name in keep or name not in seq[0]:
                continue
            for i in range(1, n - 1):
                if freeze and name in freeze[i]:                     # кисть торкається суперника — руку тримає посадка
                    continue
                q0, q1, q2 = seq[i - 1][name][1], seq[i][name][1], seq[i + 1][name][1]
                mid = q0.slerp(q2, 0.5)
                if q1.rotation_difference(mid).angle * 2.0 > lim:     # друга різниця ≈ 2·відхилення від середини
                    loc, _r = seq[i][name]
                    seq[i][name] = (loc, q1.slerp(mid, 0.6))
                    changed = True
        if not changed:
            break
    return seq


LEG = {"l": ("thigh_l", "calf_l", "foot_l"), "r": ("thigh_r", "calf_r", "foot_r")}


def limit_jerk_swing(arm, basis_seq, order, passes=6):
    """Обмежувач ривка для ніг — лише в кадрах, де стопа в повітрі (щиколотка вище 9,5 см і вона там і до, і після):
    махова нога (удар ногою, розворот, підтягування в повзанні) без піків, опорна — як розв'язав IK (ковзання 0)."""
    n = len(basis_seq)
    if n < 5:
        return basis_seq
    caps = [_fk(arm, b, order) for b in basis_seq]
    W = arm.matrix_world
    seq = [dict(b) for b in basis_seq]
    lim = math.radians(JERK_MAX * 1.4)              # ноги — трохи вільніше (крок і біг мають природні прискорення)
    for s_, bones_ in LEG.items():
        if bones_[0] not in seq[0]:
            continue
        air = [(W @ c[bones_[2]].translation).z > 0.095 for c in caps]
        for _ in range(passes):
            changed = False
            for name in bones_:
                for i in range(1, n - 1):
                    if not air[i]:                           # сам кадр у повітрі (і перед приземленням)
                        continue
                    q0, q1, q2 = seq[i - 1][name][1], seq[i][name][1], seq[i + 1][name][1]
                    mid = q0.slerp(q2, 0.5)
                    if q1.rotation_difference(mid).angle * 2.0 > lim:
                        seq[i][name] = (seq[i][name][0], q1.slerp(mid, 0.6))
                        changed = True
            if not changed:
                break
    return seq


ROLL_UP = 24.0      # ° носок униз на відриві (стопа відштовхується)
ROLL_DOWN = 14.0    # ° носок угору перед приземленням (ставиться п'ятою)


def foot_roll(arm, basis_seq, order, axis=Vector((1.0, 0.0, 0.0)), sign=1.0):
    """Перекат стопи в кроці: швидкість щиколотки вгору → носок униз, донизу → носок угору; поруч із матом — гасне
    (носок не під мат). Поворот стопи навколо щиколотки: щиколотка на місці, ковзання 0."""
    n = len(basis_seq)
    if n < 5:
        return basis_seq
    caps = [_fk(arm, b, order) for b in basis_seq]
    W = arm.matrix_world
    seq = [dict(b) for b in basis_seq]
    for s_ in "lr":
        fo = "foot_" + s_
        if fo not in seq[0]:
            continue
        z = [(W @ c[fo].translation).z for c in caps]
        # знак: пробний поворот +10° навколо осі — куди йде носок (вісь кістки в наборі наперед не відома)
        m = caps[0][fo]
        toe0 = (m.to_3x3() @ Vector((0.0, 1.0, 0.0))).z
        toe1 = ((m @ Quaternion(axis, math.radians(10.0)).to_matrix().to_4x4()).to_3x3() @ Vector((0.0, 1.0, 0.0))).z
        sgn = -1.0 if toe1 > toe0 else 1.0                       # додатний кут нижче — «носок униз»
        for i in range(1, n - 1):
            vz = (z[i + 1] - z[i - 1]) * FPS / 2.0              # м/с
            h = smooth((z[i] - 0.085) / 0.06)                    # біля мату — без перекату
            if h <= 0.0:
                continue
            ang = (ROLL_UP * smooth(vz / 0.9) if vz > 0 else -ROLL_DOWN * smooth(-vz / 0.9)) * h
            if abs(ang) < 0.2:
                continue
            loc, rot = seq[i][fo]
            seq[i][fo] = (loc, rot @ Quaternion(axis, math.radians(ang * sgn)))
    return seq


def polish_seq(arm, basis_seq, loop, pair, holds, lite=0, freeze=None):
    """Повертає (локальні повороти, матриці, точки контакту) після шару оживлення.
    lite: 0 — повний шар; 1 — руки як розв'язав IK; 2 — ще й ноги (без перекату й обмежувача)."""
    if lite:
        holds = True
    n = len(basis_seq)
    if n < 4:
        caps = [_fk(arm, b, _order(arm)) for b in basis_seq]
        return basis_seq, caps, [(c, _contact_pts(arm, c)) for c in caps]
    order = _order(arm)
    dt = 1.0 / FPS
    caps0 = [_fk(arm, b, order) for b in basis_seq]
    springs = {}
    for b in order:
        if b.name in SPRING and not ((pair or lite) and holds and b.name in ARM_BONES):
            w, dev = SPRING[b.name]
            if pair:
                w, dev = w * 2.0, dev * 0.15                # пара: хребет жорсткіший — кисті суперника на шиї/плечах
            springs[b.name] = (w, math.radians(dev))
        elif b.name.startswith(FINGERS) and not ((pair or lite) and holds):
            springs[b.name] = (FINGER_SPRING[0], math.radians(FINGER_SPRING[1]))
    # дихання: період кратний довжині петлі (шов без стрибка)
    L = (n - 1) * dt
    cyc = max(1, round(L / 3.4)) if loop else None
    per = (L / cyc) if loop else 3.4
    nb = max(1, int(round(BLEND_S * FPS)))
    out_basis = []
    state = {k: [None, Vector()] for k in springs}
    passes = 2 if loop else 1
    for ps in range(passes):
        out_basis = []
        for f in range(n):
            tgt = caps0[f]
            edge = min(1.0, smooth(f / nb), smooth((n - 1 - f) / nb))   # краї — точно вузол (і в петлях: шов = вузол)
            # лежачи пружина хребта тисне плечима в мат — гасне, коли таз опускається до мату
            edge *= smooth(((arm.matrix_world @ tgt["pelvis"].translation).z - 0.25) / 0.2)
            br = math.sin(2 * math.pi * f * dt / per)
            nbasis = {}
            cap = {}
            for b in order:
                loc, rot = basis_seq[f][b.name]
                if b.name in BREATH:
                    rot = rot @ Quaternion(Vector((1, 0, 0)), math.radians(BREATH[b.name] * br * edge))
                rel = (b.parent.matrix_local.inverted() @ b.matrix_local) if b.parent else b.matrix_local
                pm = cap[b.parent.name] if b.parent else Matrix.Identity(4)
                if b.name in springs:
                    w, dev = springs[b.name]
                    q_t = (tgt[b.name].to_quaternion())                 # розв'язана орієнтація в просторі ригу
                    st = state[b.name]
                    if st[0] is None:
                        st[0] = q_t.copy()
                    err = _rotvec(q_t @ st[0].inverted())
                    st[1] = st[1] + (err * (w * w) - st[1] * (2.0 * ZETA * w)) * dt
                    st[0] = (_expq(st[1] * dt) @ st[0]).normalized()
                    d = st[0].rotation_difference(q_t).angle
                    if d > dev:                                           # не далі межі від розв'язаної пози
                        st[0] = st[0].slerp(q_t, 1.0 - dev / d)
                    q_w = q_t.slerp(st[0], edge)
                    parent_q = (pm @ rel).to_quaternion()
                    rot = parent_q.inverted() @ q_w
                nbasis[b.name] = (loc, rot)
                cap[b.name] = pm @ rel @ (Matrix.Translation(loc) @ rot.to_matrix().to_4x4())
            out_basis.append(nbasis)
        if loop and ps == 0:
            continue
    out_basis = limit_jerk(out_basis, keep=ARM_BONES if lite else (), freeze=freeze)   # lite: руки на маті/суперникові
    if lite < 2:
        out_basis = foot_roll(arm, out_basis, order)
        out_basis = limit_jerk_swing(arm, out_basis, order)
    caps = [_fk(arm, b, order) for b in out_basis]
    return out_basis, caps, [(c, _contact_pts(arm, c)) for c in caps]


# ---------------------------------------------------------------- мить удару («хіт-стоп»)
# На контакті удару рух обох майже завмирає на ~0,1 с і надолужує після (так подають удар ігри рівня АА: вага й сила).
# Час кліпу переналаштовується монотонно — довжина, початок і кінець ті самі (вузли стикування точні).
HITSTOP = 0.1          # с — тривалість «завмирання»
HITSTOP_SLOW = 0.2     # частка швидкості в мить удару
HITSTOP_OFF = {c.name for c in CENTER if c.b in ("stagger", "sidestep", "duck") or c.a == "nosell"}


def hit_times(name):
    # лише там, де жертва від удару падає: якщо вона «продає» удар кроком, стиснутий після удару час прискорював крок
    # і опорна стопа ковзала (1,9 см)
    if name.startswith("Forearm") or name in HITSTOP_OFF:
        return []
    return [ev[1] for ev in CLIP_EVENTS.get(name, []) if ev[0] == "contact"]


def make_warp(name, length, n=600):
    """Таблиця «час кадру → час кліпу» з уповільненням біля кожного контакту (інтеграл швидкості, нормований)."""
    hits = hit_times(name)
    if not hits:
        return None
    ts = [length * i / n for i in range(n + 1)]
    def speed(t):
        v = 1.0
        for h in hits:
            u = (t - h) / HITSTOP
            if -1.0 < u < 1.0:
                v = min(v, 1.0 - (1.0 - HITSTOP_SLOW) * (0.5 + 0.5 * math.cos(math.pi * u)))
        return v
    acc = [0.0]
    for i in range(n):
        acc.append(acc[-1] + 0.5 * (speed(ts[i]) + speed(ts[i + 1])))
    k = length / acc[-1]
    vals = [a * k for a in acc]
    def warp(t):
        if t <= 0:
            return 0.0
        if t >= length:
            return length
        x = t / length * n
        i = int(x)
        return vals[i] + (vals[min(n, i + 1)] - vals[i]) * (x - i)
    return warp



# ---------------------------------------------------------------- проникнення пари по СІТКАХ тіл
# Власник 03.11.2026 (ролик ітерації 2): «дуже багато проходить скрізь тіло… Ти віддав це не перевіривши». Капсули кісток
# у quality() тонші за справжнє тіло: нога A на руці B на маті, тіло в тілі на кидку проходили як «чисто» (у зібраному
# ролику — до 20 см). Тут — те саме, що в лабораторії чейну: вершина одного тіла всередині іншого (з внутрішнього боку
# найближчої грані й за парністю трьох променів), глибина — третя найглибша вершина.
from mathutils.bvhtree import BVHTree
_DIRS3 = (Vector((0.31, 0.17, 0.94)).normalized(), Vector((-0.83, 0.45, 0.33)).normalized(), Vector((0.12, -0.97, -0.2)).normalized())


def _body_meshes(arm):
    tag = arm["pair"] if "pair" in arm else None
    return [o for o in bpy.data.objects if o.type == "MESH" and ".body_" in o.name and o.get("pair") == tag]


def _inside3(tree, v):
    votes = 0
    for d in _DIRS3:
        n, o = 0, v
        for _ in range(12):
            hit = tree.ray_cast(o, d)
            if hit[0] is None:
                break
            n += 1
            o = hit[0] + d * 1e-4
        votes += n % 2
    return votes >= 2


def _set_caps(arm, cap):
    bones = arm.data.bones
    for pb in arm.pose.bones:
        for c in pb.constraints:
            c.mute = True
    for pb in arm.pose.bones:
        b = bones[pb.name]
        m = cap[pb.name]
        base = (cap[b.parent.name] @ b.parent.matrix_local.inverted() @ b.matrix_local) if b.parent else b.matrix_local
        loc, rot, _ = (base.inverted() @ m).decompose()
        pb.rotation_quaternion = rot
        pb.location = loc if pb.name == "pelvis" else Vector()


def mesh_pen(armA, armB, sa, sb, step=2, tol=0.006):
    """Найглибше проникнення тіл пари по сітках: (см, 'A у B, кадр N')."""
    ma, mb = _body_meshes(armA), _body_meshes(armB)
    for o in ma + mb:
        o.hide_viewport = False
    worst = (0.0, "")
    for f in range(0, len(sa), step):
        _set_caps(armA, sa[f]); _set_caps(armB, sb[f])
        bpy.context.view_layer.update()
        dg = bpy.context.evaluated_depsgraph_get()
        data = {}
        for tag, ms in (("A", ma), ("B", mb)):
            vs, ps, vo, fo = [], [], [], []
            for o in ms:
                e = o.evaluated_get(dg); me = e.to_mesh(); base = len(vs)
                part = o.name.split(".body_", 1)[1].split(".")[0]
                vs += [e.matrix_world @ v.co for v in me.vertices]
                vo += [part] * len(me.vertices)
                ps += [[base + i for i in p.vertices] for p in me.polygons]
                fo += [part] * len(me.polygons)
                e.to_mesh_clear()
            data[tag] = (BVHTree.FromPolygons(vs, ps), vs, vo, fo)
        for a, b in (("A", "B"), ("B", "A")):
            tree = data[b][0]
            deep = []
            for k in range(0, len(data[a][1]), 3):
                v = data[a][1][k]
                loc, nrm, fi, d = tree.find_nearest(v, 0.3)
                if loc is not None and d > tol and nrm.dot(v - loc) < 0 and _inside3(tree, v):
                    deep.append((d, data[a][2][k], data[b][3][fi]))
            deep.sort(reverse=True)
            if len(deep) >= 3 and deep[2][0] > worst[0]:
                worst = (deep[2][0], f"{a}.{deep[2][1]} у {b}.{deep[2][2]}, кадр {f}")
    for o in ma + mb:
        o.hide_viewport = True
    for arm in (armA, armB):                         # обмеження IK — назад (інакше наступний кліп розв'язується без них)
        for pb in arm.pose.bones:
            for c in pb.constraints:
                c.mute = False
    bpy.context.view_layer.update()
    return worst[0] * 100.0, worst[1]


# ---------------------------------------------------------------- посадка кистей і стоп на тіло суперника (пари)
# Перевірка по сітках показала (03.11.2026): у парах тіло входить у тіло майже завжди кистю чи стопою — хват тисне
# пальцями в тулуб, удар проходить крізь голову, рука жертви на падінні — у стегно суперника, підошва бутса — у груди.
# Після розв'язку кадру кисть/стопа, що увійшла в тіло суперника, виштовхується назовні по нормалі шкіри (ціль IK
# зап'ястя/щиколотки) — до дотику, не далі SETTLE_MAX; IK розв'язується заново (рука лишається з'єднаною з тілом).
SETTLE_MAX = 0.025      # лише страховка для дрібних: більші виштовхування давали ривки рук до 94 °/кадр²
SETTLE_DBG = "--settle-dbg" in _args
_SIDE_CACHE = {}


def _ends(arm):
    """Вершини кистей і стоп фігури з боком (l/r) за найважчою групою — кеш на сітку."""
    out = []
    for o in bpy.data.objects:
        if o.type != "MESH" or o.get("pair") != arm.get("pair") or not (".body_hands" in o.name or ".body_feet" in o.name):
            continue
        key = o.name
        if key not in _SIDE_CACHE:
            g = {x.index: x.name for x in o.vertex_groups}
            side = []
            for v in o.data.vertices:
                nm = max(((g.get(x.group, ""), x.weight) for x in v.groups), key=lambda z: z[1], default=("", 0))[0]
                side.append(nm[-1] if nm[-2:] in ("_l", "_r") else "")
            _SIDE_CACHE[key] = side
        out.append((o, _SIDE_CACHE[key], "hands" in o.name))
    return out


def _out_dir(arm, p):
    """Назовні від тіла суперника: від осі найближчої кістки (нормалі деяких зон набору дивляться всередину —
    посадка за ними заганяла кисть глибше: 2 → 7 → 9 см)."""
    from mathutils.geometry import intersect_point_line
    W = arm.matrix_world
    best = None
    for pb in arm.pose.bones:
        if not pb.bone.use_deform:
            continue
        h, t = W @ pb.head, W @ pb.tail
        q, k = intersect_point_line(p, h, t)
        q = h if k < 0 else (t if k > 1 else q)
        d = (p - q).length
        if best is None or d < best[0]:
            best = (d, q)
    v = p - best[1]
    return v.normalized() if v.length > 1e-6 else Vector((0, 0, 1))


def settle_contacts(rigA, rigB, iters=3):
    for me, ot in ((rigA, rigB), (rigB, rigA)):
        parts = _ends(me.arm)
        body = [o for o in bpy.data.objects if o.type == "MESH" and ".body_" in o.name and o.get("pair") == ot.arm.get("pair")]
        if not parts or not body:
            continue
        for _ in range(iters):
            for o in parts + [(b, None, None) for b in body]:
                o[0].hide_viewport = False
            bpy.context.view_layer.update()
            dg = bpy.context.evaluated_depsgraph_get()
            vs, ps = [], []
            for b in body:
                e = b.evaluated_get(dg); m = e.to_mesh(); base = len(vs)
                vs += [e.matrix_world @ v.co for v in m.vertices]
                ps += [[base + i for i in p.vertices] for p in m.polygons]
                e.to_mesh_clear()
            tree = BVHTree.FromPolygons(vs, ps)
            push = {}
            for o, side, hand in parts:
                e = o.evaluated_get(dg); m = e.to_mesh()
                for i, v in enumerate(m.vertices):
                    s_ = side[i]
                    if not s_:
                        continue
                    p = e.matrix_world @ v.co
                    loc, nrm, _f, d = tree.find_nearest(p, 0.15)
                    if loc is None or d < 0.004 or nrm.dot(p - loc) >= 0 or not _inside3(tree, p):
                        continue
                    k = (("wrist_" if hand else "ankle_") + s_)
                    if d > push.get(k, (0.0, None))[0]:
                        push[k] = (d, _out_dir(ot.arm, p))
                e.to_mesh_clear()
            for o in parts + [(b, None, None) for b in body]:
                o[0].hide_viewport = True
            if not push:
                break
            if SETTLE_DBG:
                print("SETTLE", me.arm.name, {k: round(v[0] * 100, 1) for k, v in push.items()}, flush=True)
            for k, (d, n) in push.items():
                me.empties[k].location = me.empties[k].location + n * min(SETTLE_MAX, d + 0.004)
                s_ = k[-1]
                for bn in ((("lowerarm_", "hand_") if k.startswith("wrist_") else ("calf_", "foot_"))):
                    for c in me.arm.pose.bones[bn + s_].constraints:
                        c.mute = False                       # доведення до вузла могло вимкнути IK цієї кінцівки
            bpy.context.view_layer.update()


def contact_freeze(wa, wb, near=0.08):
    """Для кожного кадру — кістки руки, кисть якої біля тіла суперника (≤ near від осі його кісток + радіус):
    там обмежувач ривка не чіпає руку (її поставила посадка на тіло)."""
    from mathutils.geometry import intersect_point_line
    def frz(own, other):
        out = []
        for f in range(len(own)):
            keep = set()
            pts = own[f][1]; oth = other[f][1]
            for s_ in "lr":
                h = pts["hand_" + s_][0]
                dmin = 9.0
                for n_, (a_, b_) in oth.items():
                    q, k = intersect_point_line(h, a_, b_)
                    q = a_ if k < 0 else (b_ if k > 1 else q)
                    dmin = min(dmin, (h - q).length - RADIUS.get(n_, 0.06))
                if dmin < near:
                    keep |= {"clavicle_" + s_, "upperarm_" + s_, "lowerarm_" + s_, "hand_" + s_}
            out.append(keep)
        return out
    return frz(wa, wb), frz(wb, wa)



def lab_mesh_report(armA, armB):
    """Перевірка по сітках лабораторних кліпів (lab_clips/*.json) — тих самих кадрів, що йдуть у FBX: пара в
    розстановці лабораторії (B у (0, −1,18) з поворотом 180°)."""
    import json
    lines = []
    order_a, order_b = _order(armA), _order(armB)
    keepA, keepB = armA.matrix_world.copy(), armB.matrix_world.copy()
    armA.matrix_world = Matrix.Identity(4)
    armB.matrix_world = Matrix.Translation(Vector((0.0, -1.18, 0.0))) @ Matrix.Rotation(math.pi, 4, 'Z')
    bpy.context.view_layer.update()
    for fn in sorted(os.listdir(LAB_DIR)):
        if not fn.endswith(".json"):
            continue
        d = json.load(open(os.path.join(LAB_DIR, fn), encoding="utf-8"))
        def caps(role, arm, order):
            out = []
            for fr in d[role]:
                basis = {}
                for b in order:
                    if b.name in fr:
                        l, r = fr[b.name]
                        basis[b.name] = (Vector(l), Quaternion(r))
                    else:
                        basis[b.name] = (Vector(), Quaternion())
                out.append(_fk(arm, basis, order))
            return out
        sa, sb = caps("A", armA, order_a), caps("B", armB, order_b)
        mp = mesh_pen(armA, armB, sa, sb)
        st = "OK" if mp[0] <= LIMITS["mpen"] else f"ПОНАД ПОРІГ: mpen · тіла по сітках {mp[0]:.1f} см ({mp[1]})"
        line = f"| {fn[:-5]} (лабораторія) | — | — | — | — | — | — | — | — | {st} |"
        print("[лаб] " + line, flush=True)
        lines.append(line)
    armA.matrix_world, armB.matrix_world = keepA, keepB
    bpy.context.view_layer.update()
    return lines
