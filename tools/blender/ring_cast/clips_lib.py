# Математика кліпів: кватерніони, пряма кінематика, IK руки й ноги. Лише numpy, без Blender.
# Простір — простір арматури (Z угору, борець дивиться в −Y). Кватерніон — [w, x, y, z].
import numpy as np


def q_norm(q):
    return q / np.maximum(np.linalg.norm(q, axis=-1, keepdims=True), 1e-12)


def q_mul(a, b):
    w1, x1, y1, z1 = np.moveaxis(a, -1, 0)
    w2, x2, y2, z2 = np.moveaxis(b, -1, 0)
    return np.stack([w1 * w2 - x1 * x2 - y1 * y2 - z1 * z2,
                     w1 * x2 + x1 * w2 + y1 * z2 - z1 * y2,
                     w1 * y2 - x1 * z2 + y1 * w2 + z1 * x2,
                     w1 * z2 + x1 * y2 - y1 * x2 + z1 * w2], axis=-1)


def q_inv(q):
    r = q.copy()
    r[..., 1:] *= -1
    return r


def q_rot(q, v):
    """Повертає вектор(и) v кватерніоном q."""
    u = q[..., 1:]
    w = q[..., :1]
    t = 2.0 * np.cross(u, v)
    return v + w * t + np.cross(u, t)


def q_slerp(a, b, t):
    """Покомпонентний slerp; t — число або масив."""
    a = np.asarray(a); b = np.asarray(b)
    d = np.sum(a * b, axis=-1, keepdims=True)
    b = np.where(d < 0, -b, b)
    d = np.abs(d)
    t = np.asarray(t, dtype=np.float64)
    if t.ndim < a.ndim:
        t = t[..., None] if t.ndim else t
    th = np.arccos(np.clip(d, -1.0, 1.0))
    s = np.sin(th)
    small = s < 1e-5
    w1 = np.where(small, 1.0 - t, np.sin((1.0 - t) * th) / np.where(small, 1.0, s))
    w2 = np.where(small, t, np.sin(t * th) / np.where(small, 1.0, s))
    return q_norm(w1 * a + w2 * b)


def q_from_mat(m):
    """Кватерніон із верхньої частини матриці 3×3 / 4×4 (масиви ...×3×3)."""
    m = m[..., :3, :3]
    m00, m11, m22 = m[..., 0, 0], m[..., 1, 1], m[..., 2, 2]
    t = m00 + m11 + m22
    q = np.zeros(m.shape[:-2] + (4,), dtype=np.float64)
    c0 = t > 0
    c1 = (~c0) & (m00 >= m11) & (m00 >= m22)
    c2 = (~c0) & (~c1) & (m11 >= m22)
    c3 = (~c0) & (~c1) & (~c2)
    s = np.sqrt(np.maximum(t[c0] + 1.0, 1e-12)) * 2
    q[c0] = np.stack([0.25 * s, (m[c0][:, 2, 1] - m[c0][:, 1, 2]) / s, (m[c0][:, 0, 2] - m[c0][:, 2, 0]) / s, (m[c0][:, 1, 0] - m[c0][:, 0, 1]) / s], -1)
    s = np.sqrt(np.maximum(1.0 + m00[c1] - m11[c1] - m22[c1], 1e-12)) * 2
    q[c1] = np.stack([(m[c1][:, 2, 1] - m[c1][:, 1, 2]) / s, 0.25 * s, (m[c1][:, 0, 1] + m[c1][:, 1, 0]) / s, (m[c1][:, 0, 2] + m[c1][:, 2, 0]) / s], -1)
    s = np.sqrt(np.maximum(1.0 + m11[c2] - m00[c2] - m22[c2], 1e-12)) * 2
    q[c2] = np.stack([(m[c2][:, 0, 2] - m[c2][:, 2, 0]) / s, (m[c2][:, 0, 1] + m[c2][:, 1, 0]) / s, 0.25 * s, (m[c2][:, 1, 2] + m[c2][:, 2, 1]) / s], -1)
    s = np.sqrt(np.maximum(1.0 + m22[c3] - m00[c3] - m11[c3], 1e-12)) * 2
    q[c3] = np.stack([(m[c3][:, 1, 0] - m[c3][:, 0, 1]) / s, (m[c3][:, 0, 2] + m[c3][:, 2, 0]) / s, (m[c3][:, 1, 2] + m[c3][:, 2, 1]) / s, 0.25 * s], -1)
    return q_norm(q)


def q_axis_angle(axis, angle):
    axis = np.asarray(axis, dtype=np.float64)
    axis = axis / np.maximum(np.linalg.norm(axis, axis=-1, keepdims=True), 1e-12)
    a = np.asarray(angle, dtype=np.float64)[..., None] * 0.5
    return np.concatenate([np.cos(a), axis * np.sin(a)], axis=-1)


def q_between(a, b):
    """Найкоротший поворот вектора a у вектор b."""
    a = a / np.maximum(np.linalg.norm(a, axis=-1, keepdims=True), 1e-12)
    b = b / np.maximum(np.linalg.norm(b, axis=-1, keepdims=True), 1e-12)
    w = 1.0 + np.sum(a * b, axis=-1, keepdims=True)
    xyz = np.cross(a, b)
    q = np.concatenate([w, xyz], axis=-1)
    bad = w[..., 0] < 1e-6
    if np.any(bad):
        ax = np.cross(a[bad], np.array([1.0, 0, 0]))
        ax2 = np.cross(a[bad], np.array([0, 1.0, 0]))
        use = np.linalg.norm(ax, axis=-1, keepdims=True) < 1e-4
        ax = np.where(use, ax2, ax)
        q[bad] = np.concatenate([np.zeros(ax.shape[:-1] + (1,)), ax], axis=-1)
    return q_norm(q)


def q_continuous(q):
    """Прибирає стрибки знаку вздовж осі часу (перша вісь)."""
    q = q.copy()
    for f in range(1, q.shape[0]):
        s = np.sum(q[f] * q[f - 1], axis=-1) < 0
        q[f][s] *= -1
    return q


def smoothstep(x):
    x = np.clip(x, 0.0, 1.0)
    return x * x * (3.0 - 2.0 * x)


def minjerk(x):
    x = np.clip(x, 0.0, 1.0)
    return x * x * x * (10.0 + x * (-15.0 + 6.0 * x))


class Skeleton:
    """Скелет: імена, батьки, поза спокою в просторі арматури. parents[i] < i."""

    def __init__(self, names, parents, rest):
        self.names = list(names)
        self.index = {n: i for i, n in enumerate(self.names)}
        self.parents = np.asarray(parents, dtype=np.int32)
        self.rest = np.asarray(rest, dtype=np.float64)
        self.rest_q = q_from_mat(self.rest)
        self.rest_p = self.rest[:, :3, 3].copy()
        n = len(self.names)
        self.offset = np.zeros((n, 3))      # зміщення від батька в системі батька (спокій)
        self.rest_local_q = np.zeros((n, 4))
        for i in range(n):
            p = self.parents[i]
            if p < 0:
                self.offset[i] = self.rest_p[i]
                self.rest_local_q[i] = self.rest_q[i]
            else:
                self.offset[i] = q_rot(q_inv(self.rest_q[p]), self.rest_p[i] - self.rest_p[p])
                self.rest_local_q[i] = q_mul(q_inv(self.rest_q[p]), self.rest_q[i])

    def i(self, name):
        return self.index[name]

    def fk(self, lq, hips):
        """Локальні кватерніони (F,J,4) і позиція кореня (F,3) -> світові (F,J,4), (F,J,3)."""
        F, J = lq.shape[:2]
        wq = np.zeros_like(lq)
        wp = np.zeros((F, J, 3))
        for j in range(J):
            p = self.parents[j]
            if p < 0:
                wq[:, j] = lq[:, j]
                wp[:, j] = hips
            else:
                wq[:, j] = q_mul(wq[:, p], lq[:, j])
                wp[:, j] = wp[:, p] + q_rot(wq[:, p], self.offset[j])
        return wq, wp

    def to_local(self, wq):
        lq = np.zeros_like(wq)
        for j in range(wq.shape[1]):
            p = self.parents[j]
            lq[:, j] = wq[:, j] if p < 0 else q_mul(q_inv(wq[:, p]), wq[:, j])
        return lq

    def rest_pose(self, F=1):
        lq = np.tile(self.rest_local_q, (F, 1, 1))
        hips = np.tile(self.rest_p[0], (F, 1))
        return lq, hips


def bone_axis_local(sk, j, child):
    """Напрямок кістки j на дитину в локальній системі кістки."""
    return q_rot(q_inv(sk.rest_q[j]), sk.rest_p[child] - sk.rest_p[j])


def solve_two_bone(root, target, l1, l2, pole, stretch=1.0):
    """Позиція середнього суглоба (F,3). pole — точка, у бік якої згинається."""
    d = target - root
    dist = np.linalg.norm(d, axis=-1, keepdims=True)
    maxd = (l1 + l2) * 0.9995 * stretch
    u = d / np.maximum(dist, 1e-9)
    dc = np.minimum(dist, maxd)
    a = (l1 * l1 - l2 * l2 + dc * dc) / (2.0 * dc)
    h = np.sqrt(np.maximum(l1 * l1 - a * a, 0.0))
    side = pole - root
    side = side - u * np.sum(side * u, axis=-1, keepdims=True)
    side = side / np.maximum(np.linalg.norm(side, axis=-1, keepdims=True), 1e-9)
    if side.shape[0] >= 5:
        # площина ліктя не перекидається за кадр: згладжуємо її в часі
        k = np.array([0.1, 0.2, 0.4, 0.2, 0.1])
        pad = np.pad(side, ((2, 2), (0, 0)), mode="edge")
        side = np.stack([np.convolve(pad[:, c], k, mode="valid") for c in range(3)], axis=1)
        side = side - u * np.sum(side * u, axis=-1, keepdims=True)
        side = side / np.maximum(np.linalg.norm(side, axis=-1, keepdims=True), 1e-9)
    return root + u * a + side * h


def aim(wq_prev, axis_local, new_dir):
    """Мінімально довертає світовий кватерніон так, щоб локальна вісь axis_local дивилась у new_dir."""
    cur = q_rot(wq_prev, axis_local)
    return q_norm(q_mul(q_between(cur, new_dir), wq_prev))


class Chain:
    """Кінцівка з трьох кісток (плече-лікоть-кисть чи стегно-коліно-стопа)."""

    def __init__(self, sk, a, b, c):
        self.sk = sk
        self.a, self.b, self.c = sk.i(a), sk.i(b), sk.i(c)
        self.l1 = float(np.linalg.norm(sk.offset[self.b]))
        self.l2 = float(np.linalg.norm(sk.offset[self.c]))
        self.ax_a = bone_axis_local(sk, self.a, self.b)
        self.ax_b = bone_axis_local(sk, self.b, self.c)

    def solve(self, wq, wp, target, pole, blend=None, end_q=None, twist_limit=None, transfer_twist=True):
        """Правка кадрів на місці: кисть/стопа йде в target (F,3). blend (F,) — вага IK. end_q — світовий кватерніон кінцевої кістки."""
        sk = self.sk
        a, b, c = self.a, self.b, self.c
        root = wp[:, a]
        mid = solve_two_bone(root, target, self.l1, self.l2, pole)
        new_a = aim(wq[:, a], self.ax_a, mid - root)
        pos_b = root + q_rot(new_a, sk.offset[b] * 1.0)
        new_b_w = q_mul(new_a, q_mul(q_inv(wq[:, a]), wq[:, b]))
        new_b = aim(new_b_w, self.ax_b, target - pos_b)
        if blend is not None:
            bl = np.asarray(blend)[:, None]
            new_a = q_slerp(wq[:, a], new_a, bl)
            new_b_w2 = q_mul(new_a, q_mul(q_inv(wq[:, a]), wq[:, b]))
            new_b = q_slerp(new_b_w2, new_b, bl)
        old_end_rel = q_mul(q_inv(wq[:, b]), wq[:, c])
        if end_q is None:
            end = q_mul(new_b, old_end_rel)
        else:
            end = end_q if blend is None else q_slerp(q_mul(new_b, old_end_rel), end_q, np.asarray(blend)[:, None])
            # Скручування кисті перекладаємо в передпліччя (анатомія: кисть сама не скручується). Для ноги не треба.
            rel = q_mul(q_inv(new_b), end) if transfer_twist else None
        if end_q is not None and transfer_twist:
            ax = self.ax_b / np.linalg.norm(self.ax_b)
            # Кут скручування зі знаком, безперервний у часі: без цього межа ±180° дає перекидання передпліччя за кадр.
            theta = 2.0 * np.arctan2(np.sum(rel[:, 1:] * ax, axis=-1), rel[:, 0])
            theta = (theta + np.pi) % (2.0 * np.pi) - np.pi
            theta = np.unwrap(theta)
            theta = theta - 2.0 * np.pi * np.round(np.mean(theta) / (2.0 * np.pi))
            if twist_limit is not None:
                theta = twist_limit * np.tanh(theta / twist_limit)
            tw = q_axis_angle(np.tile(ax, (theta.shape[0], 1)), theta)
            new_b = q_norm(q_mul(new_b, tw))
        wq2 = wq.copy()
        wq2[:, a] = new_a
        wq2[:, b] = new_b
        wq2[:, c] = end
        return wq2


def world_recompute(sk, wq, hips, from_joint=0):
    """Позиції суглобів із світових кватерніонів."""
    F, J = wq.shape[:2]
    wp = np.zeros((F, J, 3))
    for j in range(J):
        p = sk.parents[j]
        wp[:, j] = hips if p < 0 else wp[:, p] + q_rot(wq[:, p], sk.offset[j])
    return wp


def propagate_children(sk, wq_old, wq_new, joints):
    """Після зміни joints діти зберігають локальні обертання: перераховує світові обертання нащадків."""
    changed = np.zeros(len(sk.names), dtype=bool)
    changed[list(joints)] = True
    out = wq_new.copy()
    for j in range(len(sk.names)):
        p = sk.parents[j]
        if p >= 0 and changed[p] and not changed[j]:
            local = q_mul(q_inv(wq_old[:, p]), wq_old[:, j])
            out[:, j] = q_mul(out[:, p], local)
            changed[j] = True
    return out


def despike(lq, joints, thr_deg=28.0, radius=4, iters=2, loop=False):
    """Згладжує кістку там, де вона за кадр провертається більше ніж на thr_deg: прибирає сплески скручування, не чіпаючи решту кліпу."""
    F = lq.shape[0]
    if F < 5:
        return lq, 0
    lq = lq.copy()
    fixed = 0
    w = np.exp(-(np.arange(-radius, radius + 1) / (radius * 0.6)) ** 2)
    for j in joints:
        for _ in range(iters):
            q = lq[:, j]
            d = np.abs(np.sum(q[1:] * q[:-1], axis=-1))
            a = np.degrees(2 * np.arccos(np.minimum(d, 1.0)))
            sp = np.nonzero(a > thr_deg)[0]
            if len(sp) == 0:
                break
            mask = np.zeros(F, dtype=bool)
            for s in sp:
                mask[max(0, s - radius): min(F, s + radius + 2)] = True
            q2 = q.copy()
            for f in np.nonzero(mask)[0]:
                acc = np.zeros(4)
                for k, wt in zip(range(-radius, radius + 1), w):
                    g = f + k
                    if loop:
                        g = g % (F - 1)
                    else:
                        g = min(max(g, 0), F - 1)
                    x = q[g]
                    if np.dot(x, q[f]) < 0:
                        x = -x
                    acc += x * wt
                q2[f] = acc / np.linalg.norm(acc)
            lq[:, j] = q2
            fixed += int(mask.sum())
    return lq, fixed


def untwist(sk, wq, wp, pairs, thr_deg=25.0, loop=False):
    """Прибирає стрибки скручування кістки навколо її власної осі. Напрямки кісток, тобто всі положення суглобів і кисті, не змінюються.
    pairs — [(кістка, дитина)]. Працює у світових обертаннях: решта кісток не змінює свого світового обертання."""
    F = wq.shape[0]
    if F < 4:
        return wq
    wq = wq.copy()
    for j, c in pairs:
        ax = bone_axis_local(sk, j, c)
        ax = ax / np.linalg.norm(ax)
        d = wp[:, c] - wp[:, j]
        d = d / np.maximum(np.linalg.norm(d, axis=-1, keepdims=True), 1e-9)
        # паралельне перенесення орієнтації вздовж шляху напрямку
        rp = np.zeros_like(wq[:, j])
        rp[0] = wq[0, j]
        for f in range(1, F):
            rp[f] = q_norm(q_mul(q_between(d[f - 1], d[f]), rp[f - 1]))
        rel = q_mul(q_inv(rp), wq[:, j])
        th = 2.0 * np.arctan2(np.sum(rel[:, 1:] * ax, axis=-1), rel[:, 0])
        th = (th + np.pi) % (2 * np.pi) - np.pi
        th = np.unwrap(th)
        dth = np.abs(np.diff(th))
        if dth.max() < np.radians(thr_deg):
            continue
        # медіана знімає одно-двокадрові сплески, далі згладжування лише навколо стрибків
        n = len(th)
        med = np.array([np.median(th[max(0, i - 3): min(n, i + 4)]) for i in range(n)])
        k = np.exp(-(np.arange(-7, 8) / 3.5) ** 2)
        k /= k.sum()
        pad = np.pad(med, 7, mode="edge")
        sm = np.convolve(pad, k, mode="same")[7:-7]
        mask = np.zeros(n)
        for i in np.nonzero(dth > np.radians(thr_deg * 0.6))[0]:
            mask[max(0, i - 10): min(n, i + 11)] = 1.0
        mk = np.convolve(np.pad(mask, 7, mode="edge"), k, mode="same")[7:-7]
        new = th * (1 - mk) + sm * mk
        # кінці точно збігаються з вихідними (стик із вузлом), похибка розтікається на сусідні 12 кадрів, а не лишається стрибком на останньому
        m = min(12, n // 2)
        ramp = 1.0 - smoothstep(np.arange(m) / float(m))
        new[:m] += (th[0] - new[0]) * ramp
        new[-m:] += (th[-1] - new[-1]) * ramp[::-1]
        wq[:, j] = q_norm(q_mul(rp, q_axis_angle(np.tile(ax, (n, 1)), new)))
    return wq


def _spans(mask, grow):
    """Відрізки кадрів навколо True: (початок, кінець) включно, розширені на grow."""
    n = len(mask)
    idx = np.nonzero(mask)[0]
    out = []
    for i in idx:
        a, b = max(0, i - grow), min(n - 1, i + 1 + grow)
        if out and a <= out[-1][1] + 1:
            out[-1][1] = max(out[-1][1], b)
        else:
            out.append([a, b])
    return out


def fix_steps(wq, joints, thr_deg=45.0, grow=4):
    """Різке перекидання обертання кістки за кадр: замінює відрізок навколо нього плавним поворотом між його краями."""
    wq = wq.copy()
    F = wq.shape[0]
    if F < 4:
        return wq
    for j in joints:
        for _ in range(3):
            q = wq[:, j]
            a = np.degrees(2 * np.arccos(np.minimum(np.abs(np.sum(q[1:] * q[:-1], axis=-1)), 1.0)))
            sp = a > thr_deg
            if not sp.any():
                break
            for s, e in _spans(sp, grow):
                if e - s < 2:
                    continue
                u = smoothstep((np.arange(s, e + 1) - s) / float(e - s))
                wq[s:e + 1, j] = q_slerp(np.tile(q[s], (e - s + 1, 1)), np.tile(q[e], (e - s + 1, 1)), u[:, None])
    return wq


def fix_elbows(sk, wq, hips, chains, thr=0.11, grow=6):
    """Перекидання ліктя чи коліна за кадр. Кисть і стопа лишаються точно на місці; площина суглоба береться зі згладженого шляху ліктя.
    chains — список Chain. thr — стрибок суглоба за кадр відносно його кореня, м."""
    F = wq.shape[0]
    if F < 6:
        return wq
    wq = wq.copy()
    for ch in chains:
        wp = world_recompute(sk, wq, hips)
        rel = wp[:, ch.b] - wp[:, ch.a]
        jump = np.linalg.norm(rel[1:] - rel[:-1], axis=-1)
        sp = jump > thr
        if not sp.any():
            continue
        # медіана знімає перекидання, далі легке згладжування
        n = F
        med = np.stack([[np.median(rel[max(0, i - 5): min(n, i + 6), c]) for i in range(n)] for c in range(3)], axis=1)
        k = np.array([0.06, 0.12, 0.2, 0.24, 0.2, 0.12, 0.06])
        pad = np.pad(med, ((3, 3), (0, 0)), mode="edge")
        smo = np.stack([np.convolve(pad[:, c], k, mode="valid") for c in range(3)], axis=1)
        w = np.zeros(n)
        for s, e in _spans(sp, grow):
            w[s:e + 1] = 1.0
        kw = np.array([0.1, 0.2, 0.4, 0.2, 0.1])
        w = np.convolve(np.pad(w, 2, mode="edge"), kw, mode="valid")
        pole = wp[:, ch.a] + smo * 3.0
        tgt = wp[:, ch.c]
        sol = ch.solve(wq, wp, tgt, pole, blend=w, end_q=wq[:, ch.c], transfer_twist=False)
        sol = propagate_children(sk, wq, sol, [ch.a, ch.b, ch.c])
        wq = sol
    return wq


def _cyclic_window(mask):
    """Найдовший суцільний циклічний відрізок True: (початок, довжина)."""
    n = len(mask)
    if not mask.any():
        return None
    if mask.all():
        return 0, n
    start = int(np.nonzero(~mask)[0][0])
    best = (0, 0)
    run = 0
    st = 0
    for k in range(1, n + 1):
        i = (start + k) % n
        if mask[i]:
            if run == 0:
                st = i
            run += 1
            if run > best[1]:
                best = (st, run)
        else:
            run = 0
    return best


def clean_feet(sk, lq, hips, root, tol=0.035, min_slip=0.05, max_shift=0.16):
    """Прибирає ковзання стоп у циклі руху: на час опори стопа нерухома у світі (кліп із кореневим рухом) або йде рівно зі стрічкою (кліп на місці).
    Зсув, потрібний на краях опори, розтікається по фазі перенесення, тому цикл лишається без шва. Повертає lq, швидкість стрічки (для кліпа на місці) і звіт."""
    F = lq.shape[0]
    n = F - 1
    t = np.arange(F) / 30.0
    wq, wp = sk.fk(lq, hips)
    in_place = float(np.linalg.norm(root[-1, :2] - root[0, :2])) < 0.05
    rz = q_axis_angle(np.array([0, 0, 1.0]), root[:, 2])
    feet = {sd: (sk.i("foot_" + sd), sk.i("ball_" + sd)) for sd in "lr"}

    def world_ball(j, belt):
        W = q_rot(rz, wp[:, j]) + np.concatenate([root[:, :2], np.zeros((F, 1))], axis=1)
        W = W.copy()
        W[:, 1] -= belt * t      # стрічка: персонаж дивиться в -Y
        return W

    # опора кожної стопи
    win = {}
    for sd, (fj, bj) in feet.items():
        z = wp[:n, bj, 2]
        w = _cyclic_window(z < z.min() + tol)
        if w is None or w[1] < 3:
            return lq, None, dict(skip="немає опори")
        win[sd] = w

    # швидкість стрічки: медіана руху стопи в середині опори
    belt = 0.0
    if in_place:
        vs = []
        for sd, (fj, bj) in feet.items():
            a, L = win[sd]
            cut = max(1, L // 4)
            idx = [(a + cut + k) % n for k in range(max(1, L - 2 * cut))]
            nxt = [(i + 1) % n for i in idx]
            vs.append(np.median((wp[nxt, bj, 1] - wp[idx, bj, 1]) * 30.0))
        belt = float(np.mean(vs))
        if belt <= 0.0:
            return lq, None, dict(skip="стопа не йде назад")

    rep = {}
    out = wq.copy()
    changed = False
    for sd, (fj, bj) in feet.items():
        W = world_ball(bj, belt)
        a, L = win[sd]
        # неперервна в часі світова позиція: кадри, що перейшли через шов циклу, отримують зсув за цикл
        cyc = W[n, :2] - W[0, :2]
        Wc = np.array([W[(a + k) % n, :2] + cyc * ((a + k) // n) for k in range(L)])
        lock = Wc[L // 2]
        # великий зсув означає, що у вікно потрапило перенесення: звужуємо опору, а не розтягуємо ногу
        while L > 3 and np.max(np.linalg.norm(Wc - lock, axis=1)) > max_shift:
            a = (a + 1) % n
            L -= 2
            Wc = np.array([W[(a + k) % n, :2] + cyc * ((a + k) // n) for k in range(L)])
            lock = Wc[L // 2]
        if np.max(np.linalg.norm(Wc - lock, axis=1)) > max_shift:
            rep[sd] = "опора не знайдена"
            continue
        idx = [(a + k) % n for k in range(L)]
        slip = float(np.max(np.linalg.norm(Wc - lock, axis=1)) / max((L - 1) / 30.0, 1e-3))
        rep[sd] = round(slip, 3)
        if slip < min_slip:
            continue
        changed = True
        delta = np.zeros((n, 2))
        for k, i in enumerate(idx):
            delta[i] = lock - Wc[k]
        # перенесення: плавно від зсуву кінця опори до зсуву її початку
        Ls = n - L
        d_end = lock - Wc[-1]
        d_start = lock - Wc[0]
        for k in range(Ls):
            i = (a + L + k) % n
            u = smoothstep(np.array([(k + 1) / float(Ls + 1)]))[0]
            delta[i] = d_end * (1 - u) + d_start * u
        delta = np.concatenate([delta, delta[:1]], axis=0)
        d3 = np.concatenate([delta, np.zeros((F, 1))], axis=1)
        # у простір скелета
        d_sk = q_rot(q_inv(rz), d3)
        ch = Chain(sk, "thigh_" + sd, "calf_" + sd, "foot_" + sd)
        wp_now = world_recompute(sk, out, hips)
        tgt = wp_now[:, ch.c] + d_sk
        pole = wp_now[:, ch.a] + (wp_now[:, ch.b] - wp_now[:, ch.a]) * 3.0
        out = ch.solve(out, wp_now, tgt, pole, end_q=out[:, ch.c], transfer_twist=False)
    rep["belt"] = round(belt, 3)
    rep["in_place"] = in_place
    if not changed:
        return lq, belt if in_place else None, rep
    lq2 = q_continuous(sk.to_local(out))
    lq2[-1] = lq2[0]
    return lq2, belt if in_place else None, rep
