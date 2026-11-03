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


def limit_jerk(basis_seq, keep=(), passes=6):
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


def polish_seq(arm, basis_seq, loop, pair, holds, lite=0):
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
    out_basis = limit_jerk(out_basis, keep=ARM_BONES if lite else ())    # lite: руки торкаються мату/суперника
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

