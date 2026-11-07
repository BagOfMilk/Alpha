# Власні кліпи й пози поверх кліпів: чого немає готовим. Усе — ключові пози з IK (кисті, стопи), передчуття й затримка кінцівок,
# без чужих даних руху. На сторінці вони позначені статусом authored.
import numpy as np
from clips_lib import *

UP = np.array([0, 0, 1.0])


def hermite(ts, vs):
    """Монотонний кубічний сплайн (Фріч — Карлсон) для ключів; повертає функцію t -> значення (підтримка векторів)."""
    ts = np.asarray(ts, dtype=np.float64)
    vs = np.asarray(vs, dtype=np.float64)
    if vs.ndim == 1:
        vs = vs[:, None]
    n = len(ts)
    d = (vs[1:] - vs[:-1]) / np.maximum(ts[1:] - ts[:-1], 1e-9)[:, None] if n > 1 else np.zeros((0, vs.shape[1]))
    m = np.zeros_like(vs)
    for i in range(1, n - 1):
        same = d[i - 1] * d[i] > 0
        h0, h1 = ts[i] - ts[i - 1], ts[i + 1] - ts[i]
        w1, w2 = 2 * h1 + h0, h1 + 2 * h0
        m[i] = np.where(same, (w1 + w2) / np.where(same, w1 / np.where(d[i - 1] == 0, 1, d[i - 1]) + w2 / np.where(d[i] == 0, 1, d[i]), 1), 0.0)

    def f(t):
        t = np.asarray(t, dtype=np.float64)
        out = np.zeros(t.shape + (vs.shape[1],))
        idx = np.clip(np.searchsorted(ts, t, side="right") - 1, 0, n - 2) if n > 1 else np.zeros(t.shape, dtype=int)
        if n == 1:
            out[...] = vs[0]
            return out
        h = ts[idx + 1] - ts[idx]
        u = np.clip((t - ts[idx]) / np.maximum(h, 1e-9), 0, 1)[..., None]
        h00 = 2 * u**3 - 3 * u**2 + 1
        h10 = u**3 - 2 * u**2 + u
        h01 = -2 * u**3 + 3 * u**2
        h11 = u**3 - u**2
        return h00 * vs[idx] + h10 * h[..., None] * m[idx] + h01 * vs[idx + 1] + h11 * h[..., None] * m[idx + 1]
    return f


class Tools:
    def __init__(self, sk, lib, meta, sex, fps):
        self.sk, self.lib, self.meta, self.sex, self.fps = sk, lib, meta, sex, fps
        self.k = float(sk.rest_p[0, 2]) / 0.9233          # масштаб постаті відносно чоловічого скелета
        self.arm = {s: Chain(sk, "upperarm_" + s, "lowerarm_" + s, "hand_" + s) for s in "lr"}
        self.leg = {s: Chain(sk, "thigh_" + s, "calf_" + s, "foot_" + s) for s in "lr"}
        self.i = sk.i

    def frames(self, dur):
        F = int(round(dur * self.fps)) + 1
        return F, np.arange(F) / self.fps

    def base(self, name, F=None, t0=0.0):
        """Кадри наявного кліпу (із повтором, якщо він цикл і коротший)."""
        lq, h, _ = self.lib[name]
        if F is None:
            return lq.copy(), h.copy()
        n = lq.shape[0]
        idx = (np.arange(F) + int(t0 * self.fps))
        if self.meta[name]["loop"]:
            idx = idx % (n - 1)
        else:
            idx = np.minimum(idx, n - 1)
        return lq[idx].copy(), h[idx].copy()

    def still(self, name, frame=0, F=1):
        lq, h, _ = self.lib[name]
        frame = frame if frame >= 0 else lq.shape[0] + frame
        return np.tile(lq[frame], (F, 1, 1)), np.tile(h[frame], (F, 1))

    def fk(self, lq, h):
        return self.sk.fk(lq, h)

    def add(self, name, lq, h, root=None, loop=False, cat="authored", status="authored", note=None, node_from=None, node_to=None, source="Власний (ключові пози й IK)", **extra):
        F = lq.shape[0]
        lq = q_continuous(q_norm(lq))
        sk = self.sk
        wq0, wp0 = sk.fk(lq, h)
        wq0 = fix_elbows(sk, wq0, h, [self.arm["l"], self.arm["r"], self.leg["l"], self.leg["r"]])
        wp0 = world_recompute(sk, wq0, h)
        pairs = []
        for sd in "lr":
            pairs += [(sk.i("upperarm_" + sd), sk.i("lowerarm_" + sd)), (sk.i("lowerarm_" + sd), sk.i("hand_" + sd)), (sk.i("thigh_" + sd), sk.i("calf_" + sd)), (sk.i("calf_" + sd), sk.i("foot_" + sd))]
        wq1 = untwist(sk, wq0, wp0, pairs, loop=loop)
        wq1 = fix_steps(wq1, [sk.i(n) for n in ("hand_l", "hand_r", "foot_l", "foot_r", "ball_l", "ball_r")])
        lq = q_continuous(sk.to_local(wq1))
        if loop:
            lq[-1] = lq[0]
            h = h.copy()
            h[-1] = h[0]
        if root is None:
            root = np.zeros((F, 3))
        self.lib[name] = (lq, h, root)
        dur = (F - 1) / self.fps
        net = root[-1]
        m = dict(name=name, frames=int(F), duration=round(dur, 4), loop=loop, source=source, category=cat, status=status, role=None, base=None,
                 offset=None, rot=None, node_from=node_from, node_to=node_to, net=[round(float(net[0]), 4), round(float(net[1]), 4), round(float(net[2]), 4)],
                 speed=round(float(np.linalg.norm(net[:2])) / max(dur, 1e-6), 3))
        if note:
            m["note"] = note
        m.update(extra)
        self.meta[name] = m

    # ---- обертання тулуба в просторі арматури ----
    def turn(self, lq, h, joints_rot):
        """joints_rot: {ім'я кістки: кватерніон (F,4) у світових осях}. Зберігає дітей."""
        sk = self.sk
        for n, Rw in joints_rot.items():
            j = sk.i(n)
            wq, wp = sk.fk(lq, h)
            p = sk.parents[j]
            if p < 0:
                lq[:, j] = q_mul(Rw, lq[:, j])
            else:
                lq[:, j] = q_mul(q_inv(wq[:, p]), q_mul(Rw, wq[:, j]))
        return lq

    def spine_bend(self, lq, h, fwd=0.0, side=0.0, twist=0.0):
        """Нахил тулуба, градуси, масив (F,) або число: уперед(+), вліво(+), скручування вліво(+). Розподіл по трьох кістках — перекриття."""
        F = lq.shape[0]
        fwd, side, twist = [np.broadcast_to(np.asarray(x, dtype=np.float64), (F,)) for x in (fwd, side, twist)]
        for n, w in (("spine_01", 0.25), ("spine_02", 0.35), ("spine_03", 0.40)):
            # у просторі арматури: уперед = −Y, тож нахил уперед — навколо +X... лівий борт +X
            q = q_mul(q_axis_angle(np.array([0, 0, 1.0]), np.radians(twist * w)),
                      q_mul(q_axis_angle(np.array([1.0, 0, 0]), np.radians(-fwd * w) * -1), q_axis_angle(np.array([0, 1.0, 0]), np.radians(side * w) * -1)))
            lq = self.turn(lq, h, {n: q})
        return lq

    def look(self, lq, h, pitch=0.0, yaw=0.0, roll=0.0):
        F = lq.shape[0]
        pitch, yaw, roll = [np.broadcast_to(np.asarray(x, dtype=np.float64), (F,)) for x in (pitch, yaw, roll)]
        q = q_mul(q_axis_angle(UP, np.radians(yaw)), q_mul(q_axis_angle(np.array([1.0, 0, 0]), np.radians(pitch)), q_axis_angle(np.array([0, 1.0, 0]), np.radians(roll))))
        half = q_slerp(np.tile([1.0, 0, 0, 0], (F, 1)), q, 0.5)
        return self.turn(lq, h, {"neck_01": half, "head": half})

    def hand_frame_rest(self, s):
        sk = self.sk
        h = sk.rest_p[sk.i("hand_" + s)]
        mid = sk.rest_p[sk.i("middle_01_" + s)]
        idx = sk.rest_p[sk.i("index_01_" + s)]
        pky = sk.rest_p[sk.i("pinky_01_" + s)]
        f = mid - h
        f /= np.linalg.norm(f)
        a = idx - pky
        a = a - f * np.dot(a, f)
        a /= np.linalg.norm(a)
        n = np.cross(a, f) if s == "r" else np.cross(f, a)   # нормаль долоні (від долоні назовні)
        return f, a, n

    def hand_q(self, s, fingers_dir, palm_dir):
        """Світовий кватерніон кисті: пальці в fingers_dir, долоня дивиться в palm_dir. Масиви (F,3)."""
        f0, a0, n0 = self.hand_frame_rest(s)
        fingers_dir = fingers_dir / np.linalg.norm(fingers_dir, axis=-1, keepdims=True)
        n = palm_dir - fingers_dir * np.sum(palm_dir * fingers_dir, axis=-1, keepdims=True)
        n = n / np.maximum(np.linalg.norm(n, axis=-1, keepdims=True), 1e-9)
        a = np.cross(fingers_dir, n) if s == "r" else np.cross(n, fingers_dir)
        R = np.stack([fingers_dir, n, a], axis=-1)                     # (F,3,3) стовпці
        R0 = np.stack([f0, n0, a0], axis=-1)
        Q = q_from_mat(R @ R0.T)
        return q_mul(Q, np.tile(self.sk.rest_q[self.sk.i("hand_" + s)], (Q.shape[0], 1)))

    def grip(self, lq, s, amount=1.0, thumb=1.0):
        """Стискає кисть s у кулак. Форма кулака — з вихідного кліпу спокою: обчислені кути лишали пальці напіврозігнутими, а хват із кліпу мікрофона тримає великий палець уздовж ручки.
        amount — масив (F,) або число: 0 — кисть спокою, 1 — повний кулак."""
        sk = self.sk
        F = lq.shape[0]
        amount = np.broadcast_to(np.asarray(amount, dtype=np.float64), (F,))
        fist = self.lib["U_Idle_Loop"][0][0]
        for fg in ("index", "middle", "ring", "pinky", "thumb"):
            w = amount * (thumb if fg == "thumb" else 1.0)
            for i in range(3):
                j = sk.i("%s_0%d_%s" % (fg, i + 1, s))
                lq[:, j] = q_slerp(np.tile(sk.rest_local_q[j], (F, 1)), np.tile(fist[j], (F, 1)), w[:, None])
        return lq

    def chest_point(self, wq, wp, off):
        """Точка в рамці грудей (X ліво, Y назад, Z вгору в спокої; обертається з грудьми)."""
        j = self.sk.i("spine_03")
        rel = q_mul(wq[:, j], q_inv(self.sk.rest_q[j]))
        return wp[:, j] + q_rot(rel, off)

    def pelvis_point(self, wq, wp, off):
        j = self.sk.i("pelvis")
        rel = q_mul(wq[:, j], q_inv(self.sk.rest_q[j]))
        return wp[:, j] + q_rot(rel, off)

    def chest_dir(self, wq, d):
        j = self.sk.i("spine_03")
        rel = q_mul(wq[:, j], q_inv(self.sk.rest_q[j]))
        return q_rot(rel, d)

    def ik_arm(self, lq, h, s, target_fn, pole_off, weight=None, hand=None, twist_limit=np.radians(80)):
        """target_fn(wq, wp) -> (F,3); pole_off — точка біля грудей, куди дивиться лікоть (у рамці грудей). hand(wq,wp) -> кватерніон кисті (F,4)."""
        sk = self.sk
        wq, wp = sk.fk(lq, h)
        tg = target_fn(wq, wp)
        pole = self.chest_point(wq, wp, np.asarray(pole_off, dtype=np.float64))
        endq = hand(wq, wp) if hand else None
        ch = self.arm[s]
        wq2 = ch.solve(wq, wp, tg, pole, blend=weight, end_q=endq, twist_limit=twist_limit)
        wq2 = propagate_children(sk, wq, wq2, [ch.a, ch.b, ch.c])
        return sk.to_local(wq2)

    def ik_leg(self, lq, h, s, target, pole_dir=None, foot_q=None):
        sk = self.sk
        wq, wp = sk.fk(lq, h)
        ch = self.leg[s]
        if pole_dir is None:
            # площина коліна з поточної пози: без стрибка скручування стегна, якщо ціль не змінилась
            knee_hint = wp[:, ch.b] + (wp[:, ch.b] - 0.5 * (wp[:, ch.a] + wp[:, ch.c])) * 2.0 + np.array([0, -0.02, 0])
        else:
            knee_hint = 0.5 * (wp[:, ch.a] + target) + np.asarray(pole_dir, dtype=np.float64) * 0.5
        wq2 = ch.solve(wq, wp, target, knee_hint, end_q=foot_q, transfer_twist=False)
        wq2 = propagate_children(sk, wq, wq2, [ch.a, ch.b, ch.c])
        return sk.to_local(wq2)

    def feet_of(self, lq, h):
        wq, wp = self.sk.fk(lq, h)
        j_l, j_r = self.sk.i("foot_l"), self.sk.i("foot_r")
        return wp[:, j_l].copy(), wp[:, j_r].copy(), wq[:, j_l].copy(), wq[:, j_r].copy()

    def pin_feet_to(self, lq, h, base_lq, base_h):
        """Стопи лишаються там, де вони в базовому кліпі: таз може рухатись, підошва не їде й не йде під мат."""
        al, ar, aql, aqr = self.feet_of(base_lq, base_h)
        lq = self.ik_leg(lq, h, "l", al, foot_q=aql)
        lq = self.ik_leg(lq, h, "r", ar, foot_q=aqr)
        return lq

    def transition(self, a_lq, a_h, b_lq, b_h, dur, delay_upper=0.08, lift=0.05, pin_feet=True):
        """Перехід поза -> поза (кадри (J,4), (3,)): таз і суглоби з повільним стартом і фінішем, верх запізнюється, стопи не йдуть під мат."""
        sk = self.sk
        F, ts = self.frames(dur)
        u = ts / dur
        e = minjerk(u)
        J = len(sk.names)
        lq = np.zeros((F, J, 4))
        upper = [sk.i(n) for n in sk.names if n.startswith(("spine", "neck", "head", "clavicle", "upperarm", "lowerarm", "hand", "thumb", "index", "middle", "ring", "pinky"))]
        lag = np.zeros(J)
        for n in sk.names:
            if n.startswith(("neck", "head")):
                lag[sk.i(n)] = 1.4
            elif n.startswith(("lowerarm", "hand", "thumb", "index", "middle", "ring", "pinky")):
                lag[sk.i(n)] = 1.2
            elif n.startswith(("spine", "clavicle", "upperarm")):
                lag[sk.i(n)] = 0.7
        for j in range(J):
            d = lag[j] * delay_upper / dur
            ee = minjerk(np.clip((u - d) / max(1.0 - d, 1e-3), 0, 1))
            lq[:, j] = q_slerp(a_lq[j][None], b_lq[j][None], ee[:, None])
        h = a_h[None] + (b_h - a_h)[None] * e[:, None]
        if pin_feet:
            al, ar, aql, aqr = self.feet_of(a_lq[None], a_h[None])
            bl, br, bql, bqr = self.feet_of(b_lq[None], b_h[None])
            for s, A, B, QA, QB, dl in (("l", al, bl, aql, bql, 0.0), ("r", ar, br, aqr, bqr, 0.12)):
                tt = minjerk(np.clip((u - dl) / max(1 - dl, 1e-3) if dl else u, 0, 1))
                tg = A + (B - A) * tt[:, None]
                mv = np.linalg.norm(B - A)
                tg = tg + UP * (np.sin(np.pi * tt) * min(lift, mv * 0.35))[:, None]
                fq = q_slerp(np.tile(QA, (F, 1)), np.tile(QB, (F, 1)), tt[:, None])
                lq = self.ik_leg(lq, h, s, tg, foot_q=fq)
            # перший і останній кадри — точно вихідні пози (щоб кросфейд на стику був нульовий)
            lq[0] = a_lq
            lq[-1] = b_lq
            h[0] = a_h
            h[-1] = b_h
        return lq, h

    def pose_keys(self, dur, keys, base_lq, base_h):
        """Спільний аніматор: keys = dict(hips=[(t,(x,y,z))], turn=[(t,(fwd,side,twist))], look=[...], hand_l=..., hand_r=..., foot_l=..., foot_r=...)."""
        raise NotImplementedError


def ease_keys(ts, keys):
    t = [k[0] for k in keys]
    v = [k[1] for k in keys]
    return hermite(t, v)(ts)


# --- опора в просторі тіла: k — масштаб постаті ---
def add_all(sk, lib, meta, sex, fps):
    T = Tools(sk, lib, meta, sex, fps)
    k = T.k
    # ----- псевдоніми брифу
    T.add("promo_idle", *T.base("Promo_Mic_Talk_Loop"), loop=True, cat="promo", status="ready", source=meta["Promo_Mic_Talk_Loop"]["source"], note="Те саме, що Promo_Mic_Talk_Loop; мікрофон у правій руці.", prop="mic")

    # ----- стани, яких бракує: лежання, сидіння, коліна, присід
    for nm, src, node in (("Lying_Back_Loop", "Bump_Back", "lying_back"), ("Lying_Front_Loop", "Bump_Front", "lying_front")):
        F, ts = T.frames(4.0)
        lq, h = T.still(src, -1, F)
        b = np.sin(ts * 2 * np.pi / 2.0)
        lq = T.spine_bend(lq, h, fwd=0.9 * b * (1 if node == "lying_back" else -1))
        T.add(nm, lq, h, loop=True, cat="idle", status="ready", source="Власний (дихання поверх останнього кадру падіння)", node_from=node, node_to=node, note="Лежить на маті й дихає; для рагдолу після фінішера — точка повернення.")

    stance = T.still("Ring_Stance_Loop", 0, 1)
    idle = T.still("U_Idle_Loop", 0, 1)
    crouch = T.still("U_Crouch_Idle_Loop", 0, 1)

    # Коліна: обидва коліна на маті, тулуб рівно
    Fk = 1
    def kneel_pose(arms_from=None):
        lq, h = T.still("U_Idle_Loop", 0, 1)
        lq = lq.copy(); h = h.copy()
        l1 = T.leg["l"].l1; l2 = T.leg["l"].l2
        foot_z = sk.rest_p[sk.i("foot_l"), 2]
        h[:, 2] = l1 * 0.98 + 0.06 * k
        h[:, 1] = h[:, 1] * 0 + 0.0
        # коліно прямо під тазом, гомілка лежить позаду
        for s, sg in (("l", 1), ("r", -1)):
            t = np.array([[sg * 0.12 * k, l2 * 0.92, foot_z * 0.95]])
            fq = T.hand_q  # заглушка, стопу задаємо нижче
            lq = T.ik_leg(lq, h, s, t, pole_dir=(0, -1.0, 0))
            # носок униз-назад: стопа витягнута вздовж гомілки
            wq, wp = sk.fk(lq, h)
            j = sk.i("foot_" + s)
            rest = sk.rest_q[j]
            q = q_mul(q_axis_angle(np.array([1.0, 0, 0]), np.radians(-70.0)), rest[None])
            wq2 = wq.copy(); wq2[:, j] = q
            wq2 = propagate_children(sk, wq, wq2, [j])
            lq = sk.to_local(wq2)
        return lq, h
    kq, kh = kneel_pose()
    # Сидіння на маті: ноги вперед, тулуб вертикально
    def sit_pose():
        lq, h = T.still("U_Idle_Loop", 0, 1)
        lq = lq.copy(); h = h.copy()
        foot_z = sk.rest_p[sk.i("foot_l"), 2]
        h[:, 2] = 0.13 * k
        h[:, 1] = 0.05 * k
        l = T.leg["l"]
        for s, sg, y in (("l", 1, -(l.l1 + l.l2) * 0.86), ("r", -1, -(l.l1 + l.l2) * 0.78)):
            lq = T.ik_leg(lq, h, s, np.array([[sg * 0.17 * k, y + 0.05 * k, foot_z * 0.9]]), pole_dir=(0, -0.2, 1.0))
        lq = T.spine_bend(lq, h, fwd=8.0)
        return lq, h
    sq, sh = sit_pose()

    def breathe_loop(name, lq0, h0, dur=3.2, amp=1.2, hipamp=0.0, cat="idle", node=None, note=None, status="ready"):
        F, ts = T.frames(dur)
        lq = np.tile(lq0[0], (F, 1, 1))
        h = np.tile(h0[0], (F, 1))
        b = np.sin(ts * 2 * np.pi / dur)
        h = h + np.array([0, 0, hipamp])[None] * b[:, None]
        lq = T.spine_bend(lq, h, fwd=amp * b, side=0.3 * amp * np.sin(ts * 2 * np.pi / dur + 1.3))
        lq = T.look(lq, h, pitch=0.8 * np.sin(ts * 2 * np.pi / dur + 0.7), yaw=2.0 * np.sin(ts * 2 * np.pi / dur + 2.1))
        T.add(name, lq, h, loop=True, cat=cat, status=status, node_from=node, node_to=node, note=note)
        return lq, h

    kq_l, kh_l = breathe_loop("Kneel_Idle_Loop", kq, kh, node="kneel", cat="guard", note="Обидва коліна на маті. Власна поза.", status="authored")
    sq_l, sh_l = breathe_loop("Sit_Mat_Idle_Loop", sq, sh, node="sit_mat", cat="sit", note="Сидить на маті, ноги вперед. Власна поза.", status="authored")

    # Переходи
    def trans(name, a, b, dur, nf, nt, note=None):
        lq, h = T.transition(a[0][0], a[1][0], b[0][0], b[1][0], dur)
        T.add(name, lq, h, cat="transition", status="authored", node_from=nf, node_to=nt, note=note or "Клип-перехід: таз і стопи плавно, верх із запізненням.")
    S0 = (stance[0], stance[1])
    I0 = (idle[0], idle[1])
    C0 = (crouch[0], crouch[1])
    K0 = (kq, kh)
    Sit0 = (sq, sh)
    trans("Tr_Idle_To_Stance", I0, S0, 0.55, "idle", "stance")
    trans("Tr_Stance_To_Idle", S0, I0, 0.65, "stance", "idle")
    trans("Tr_Stance_To_Crouch", S0, C0, 0.55, "stance", "crouch")
    trans("Tr_Crouch_To_Stance", C0, S0, 0.60, "crouch", "stance")
    trans("Tr_Crouch_To_Kneel", C0, K0, 0.80, "crouch", "kneel")
    trans("Tr_Kneel_To_Crouch", K0, C0, 0.85, "kneel", "crouch")
    trans("Tr_Stance_To_Kneel", S0, K0, 1.05, "stance", "kneel")
    trans("Tr_Kneel_To_Stance", K0, S0, 1.20, "kneel", "stance")
    trans("Tr_Kneel_To_Sit", K0, Sit0, 1.0, "kneel", "sit_mat")
    trans("Tr_Sit_To_Kneel", Sit0, K0, 1.1, "sit_mat", "kneel")
    promo0 = T.still("promo_idle", 0, 1)
    P0 = (promo0[0], promo0[1])
    trans("Tr_Idle_To_Promo", I0, P0, 0.75, "idle", "promo", "Підіймає мікрофон до рота.")
    trans("Tr_Promo_To_Idle", P0, I0, 0.75, "promo", "idle", "Опускає мікрофон.")
    meta["Tr_Idle_To_Promo"]["prop"] = "mic"
    meta["Tr_Promo_To_Idle"]["prop"] = "mic"
    # Перекат із живота на спину: хвіст готового кліпу продажу падіння (Bump_Front_Sell_RollBack)
    src = "Bump_Front_Sell_RollBack"
    if src in lib:
        lq_s, h_s, r_s = lib[src]
        lf = lib["Lying_Front_Loop"][0][0]
        hf = lib["Lying_Front_Loop"][1][0]
        d = np.array([np.mean(1 - np.abs(np.sum(lq_s[f] * lf, axis=-1))) + np.abs(h_s[f, 2] - hf[2]) for f in range(lq_s.shape[0])])
        f0 = int(np.argmin(d[: lq_s.shape[0] // 2 + 6]))
        seg_q = lq_s[f0:].copy(); seg_h = h_s[f0:].copy(); seg_r = r_s[f0:] - r_s[f0]
        n = seg_q.shape[0]
        k0 = min(8, n // 3)
        for i in range(k0):
            w = float(smoothstep((i + 1) / (k0 + 1.0)))
            seg_q[i] = q_slerp(lf, seg_q[i], np.full(lf.shape[:1] + (1,), w))
            seg_h[i] = hf + (seg_h[i] - hf) * w
        lb = lib["Lying_Back_Loop"][0][0]; hb = lib["Lying_Back_Loop"][1][0]
        for i in range(k0):
            w = float(smoothstep((i + 1) / (k0 + 1.0)))
            f = n - 1 - i
            seg_q[f] = q_slerp(lb, seg_q[f], np.full(lb.shape[:1] + (1,), w))
            seg_h[f] = hb + (seg_h[f] - hb) * w
        T.add("Roll_Front_To_Back", seg_q, seg_h, root=seg_r, cat="getup", status="ready", source=meta[src]["source"] + ", хвіст кліпу " + src, node_from="lying_front", node_to="lying_back", note="Перекат із живота на спину: далі встає через GetUp_Back.")

    # ----- поза рук у захваті: шар верхньої частини тіла
    behind = lambda s, sg: (lambda wq, wp: T.pelvis_point(wq, wp, np.array([sg * 0.06 * k, 0.20 * k, 0.10 * k])))
    def hold_arms(lq, h, ts, dur):
        F = lq.shape[0]
        strug = np.sin(ts * 2 * np.pi / dur)
        strug2 = np.sin(ts * 2 * np.pi * 2 / dur + 0.8)
        lq = T.spine_bend(lq, h, fwd=-6 + 2.5 * strug, side=3.0 * strug2, twist=4.0 * strug)
        lq = T.look(lq, h, pitch=-10 + 6 * strug2, yaw=8 * strug, roll=5 * strug2)
        for s, sg, ph in (("l", 1, 0.0), ("r", -1, 1.6)):
            # кисті перед куприком, одне зап'ястя на іншому; пальці продовжують передпліччя до середини спини, долоня назовні
            tg = lambda wq, wp, sg=sg, ph=ph: T.pelvis_point(wq, wp, np.array([sg * 0.055 * k, (0.195 if s == "l" else 0.225) * k, 0.20 * k])) + np.array([0, 0, 0.010])[None] * np.sin(ts[:, None] * 2 * np.pi * 2 / dur + ph)
            lq = T.ik_arm(lq, h, s, tg, (sg * 0.50 * k, 0.30 * k, -0.06 * k), hand=lambda wq, wp, sg=sg, s=s: T.hand_q(s, np.tile(np.array([-sg * 0.92, 0.10, 0.12]), (F, 1)), np.tile(np.array([0.0, 1.0, 0.0]), (F, 1))))
        lq = T.grip(lq, "l", 0.9)
        lq = T.grip(lq, "r", 0.9)
        return lq
    for name, src, node, nt in (("Held_Stand_Loop", "Ring_Stance_Loop", "held_stand", "Стоячи: руки заведені за спину, борець смикається."),
                                 ("Held_Sit_Loop", "Sit_Mat_Idle_Loop", "held_sit", "Сидячи на маті: руки зафіксовані за спиною."),
                                 ("Held_Kneel_Loop", "Kneel_Idle_Loop", "held_kneel", "На колінах: руки зафіксовані за спиною.")):
        F, ts = T.frames(3.2)
        lq, h = T.base(src, F)
        lq = hold_arms(lq, h, ts, 3.2)
        lq[-1] = lq[0]
        T.add(name, lq, h, loop=True, cat="hold", node_from=node, node_to=node, note=nt, upper_layer=True, layer_mask="upper_body")

    for a_name, b_name, nf, nt, dur in (("Ring_Stance_Loop", "Held_Stand_Loop", "stance", "held_stand", 0.5), ("Kneel_Idle_Loop", "Held_Kneel_Loop", "kneel", "held_kneel", 0.5), ("Sit_Mat_Idle_Loop", "Held_Sit_Loop", "sit_mat", "held_sit", 0.5)):
        a = T.still(a_name, 0, 1); b = T.still(b_name, 0, 1)
        lq, h = T.transition(a[0][0], a[1][0], b[0][0], b[1][0], dur, pin_feet=False)
        T.add("Tr_To_" + nt.capitalize(), lq, h, cat="transition", node_from=nf, node_to=nt, note="Руки заводить за спину: клип-перехід у захват.")
        lq, h = T.transition(b[0][0], b[1][0], a[0][0], a[1][0], dur + 0.1, pin_feet=False)
        T.add("Tr_From_" + nt.capitalize(), lq, h, cat="transition", node_from=nt, node_to=nf, note="Вирвався із захвату: клип-перехід.")

    # Шар: лише руки в захваті (для накладання поверх будь-якого стану)
    F, ts = T.frames(3.2)
    lq, h = T.base("U_Idle_Loop", F)
    lq = hold_arms(lq, h, ts, 3.2)
    lq[-1] = lq[0]
    T.add("Layer_Held_Arms_Loop", lq, h, loop=True, cat="layer", note="Шар верхньої частини тіла: у захваті поверх idle/стійки/ходьби.", layer_mask="upper_body", upper_layer=True)

    # ----- реквізит: рука з предметом. Сокет кріпиться до кисті, а хват визначає кліп.
    def carry_hand(lq, h, s="r", swing=0.0, ts=None, kind="low", fingers=(0.0, -0.15, -1.0)):
        F = lq.shape[0]
        sg = 1 if s == "l" else -1
        if kind == "low":
            # рука вниз збоку, предмет висить
            osc = 0 if ts is None else 0.020 * np.sin(ts * 2 * np.pi * 1.0)
            tg = lambda wq, wp: T.pelvis_point(wq, wp, np.array([sg * 0.285 * k, -0.02 * k, -0.11 * k])) + np.array([0, 1.0, 0])[None] * (np.zeros(F) + osc)[:, None]
            pole = (sg * 0.55 * k, 0.15 * k, -0.20 * k)
            hand = lambda wq, wp: T.hand_q(s, np.tile(np.array(fingers), (F, 1)), np.tile(np.array([-sg * 1.0, 0.0, 0.0]), (F, 1)))
        lq = T.ik_arm(lq, h, s, tg, pole, hand=hand)
        lq = T.grip(lq, s, 1.0)
        return lq

    # пояс на правому плечі, права кисть тримає ремінь біля грудей
    def belt_arm(lq, h, ts):
        F = lq.shape[0]
        tg = lambda wq, wp: T.chest_point(wq, wp, np.array([-0.12 * k, -0.25 * k, -0.04 * k])) + np.array([0, 0, 0.006])[None] * np.sin(ts[:, None] * 2 * np.pi / 3.0)
        hand = lambda wq, wp: T.hand_q("r", T.chest_dir(wq, np.tile(np.array([0.85, -0.15, 0.5]), (F, 1))), T.chest_dir(wq, np.tile(np.array([0.0, 1.0, 0.0]), (F, 1))))
        lq = T.ik_arm(lq, h, "r", tg, (-0.52 * k, -0.10 * k, -0.34 * k), hand=hand)
        lq = T.grip(lq, "r", 0.92)
        return lq
    F, ts = T.frames(3.0)
    lq, h = T.base("U_Idle_Loop", F)
    lq = belt_arm(lq, h, ts)
    lq[-1] = lq[0]
    T.add("belt_hold", lq, h, loop=True, cat="prop", prop="belt", note="Пояс закинуто на праве плече; кисть тримає ремінь біля грудей.", layer_mask="right_arm")
    F = lib["U_Walk_Loop"][0].shape[0]
    ts = np.arange(F) / fps
    lq, h = T.base("U_Walk_Loop", F)
    lq = belt_arm(lq, h, ts)
    lq[-1] = lq[0]
    T.add("belt_walk_Loop", lq, h, loop=True, cat="prop", prop="belt", note="Ходьба з поясом: поза руки поверх кліпу ходьби.", speed_from_clip="U_Walk_Loop", layer_mask="right_arm")
    meta["belt_walk_Loop"]["speed"] = meta["U_Walk_Loop"]["speed"]
    meta["belt_walk_Loop"]["phase"] = meta["U_Walk_Loop"].get("phase")

    # предмет у правій руці внизу (стілець, кейс, палка)
    for pref, prop, fing in (("chair", "chair", (0.0, -0.15, -1.0)), ("case", "case", (0.0, -0.15, -1.0)), ("kendo", "kendo", (0.0, 0.5, -0.87))):
        F, ts = T.frames(3.0)
        lq, h = T.base("U_Idle_Loop", F)
        lq = carry_hand(lq, h, "r", ts=ts, fingers=fing)
        lq[-1] = lq[0]
        T.add(pref + "_idle_Loop", lq, h, loop=True, cat="prop", prop=prop, note="Стоїть із предметом у правій руці.", layer_mask="right_arm")
        F = lib["U_Walk_Loop"][0].shape[0]
        ts = np.arange(F) / fps
        lq, h = T.base("U_Walk_Loop", F)
        lq = carry_hand(lq, h, "r", ts=ts, fingers=fing)
        lq[-1] = lq[0]
        T.add(pref + "_walk_Loop", lq, h, loop=True, cat="prop", prop=prop, note="Ходьба з предметом: поза руки поверх кліпу ходьби.", layer_mask="right_arm")
        meta[pref + "_walk_Loop"]["speed"] = meta["U_Walk_Loop"]["speed"]
        meta[pref + "_walk_Loop"]["phase"] = meta["U_Walk_Loop"].get("phase")

    # удар важким предметом однією рукою: замах назад і вгору, удар згори вниз уперед, супровід, повернення
    def heavy_swing(name, prop, dur=1.9, note=None):
        F, ts = T.frames(dur)
        lq, h = T.base("Ring_Stance_Loop", F)
        base_lq, base_h = lq.copy(), h.copy()
        t = ts
        fwd = ease_keys(t, [(0, (0,)), (0.45, (-14,)), (0.66, (-16,)), (0.92, (26,)), (1.10, (30,)), (dur, (0,))])[:, 0]
        twist = ease_keys(t, [(0, (0,)), (0.45, (-28,)), (0.66, (-32,)), (0.92, (18,)), (1.18, (24,)), (dur, (0,))])[:, 0]
        h = h.copy()
        hy = ease_keys(t, [(0, (0,)), (0.5, (0.06 * k,)), (0.92, (-0.09 * k,)), (1.18, (-0.10 * k,)), (dur, (0,))])[:, 0]
        hz = ease_keys(t, [(0, (0,)), (0.5, (0.015,)), (0.92, (-0.045,)), (1.18, (-0.05,)), (dur, (0,))])[:, 0]
        h[:, 1] += hy; h[:, 2] += hz
        lq = T.pin_feet_to(lq, h, base_lq, base_h)
        lq = T.spine_bend(lq, h, fwd=fwd, twist=twist)
        lq = T.look(lq, h, pitch=-fwd * 0.35, yaw=-twist * 0.5)
        tkeys = [(0, (-0.30, -0.16, -0.02)), (0.25, (-0.36, 0.02, 0.20)), (0.55, (-0.26, 0.14, 0.50)), (0.68, (-0.22, 0.10, 0.56)), (0.80, (-0.20, -0.20, 0.56)), (0.92, (-0.12, -0.46, 0.26)), (1.02, (-0.08, -0.56, -0.08)), (1.24, (-0.16, -0.40, -0.20)), (dur, (-0.30, -0.16, -0.02))]
        off = ease_keys(t, tkeys) * k
        tg = lambda wq, wp: T.chest_point(wq, wp, off)
        fingers = ease_keys(t, [(0, (0.0, -0.8, -0.4)), (0.55, (0.1, 0.1, 1.0)), (0.68, (0.1, -0.1, 1.0)), (0.92, (0.1, -1.0, 0.3)), (1.04, (0.1, -1.0, -0.4)), (dur, (0.0, -0.8, -0.4))])
        palm = np.tile(np.array([1.0, 0.0, 0.0]), (F, 1))
        lq = T.ik_arm(lq, h, "r", tg, (-0.62 * k, 0.15 * k, -0.05 * k), hand=lambda wq, wp: T.hand_q("r", fingers, palm))
        lq = T.grip(lq, "r", 1.0)
        # ліва рука: баланс, виноситься вперед і назад
        offl = ease_keys(t, [(0, (0.26, -0.16, -0.06)), (0.60, (0.32, -0.34, 0.04)), (1.0, (0.36, 0.06, -0.14)), (dur, (0.26, -0.16, -0.06))]) * k
        lq = T.ik_arm(lq, h, "l", lambda wq, wp: T.chest_point(wq, wp, offl), (0.60 * k, 0.15 * k, -0.25 * k))
        T.add(name, lq, h, cat="prop", prop=prop, note=note, events=[dict(event="contact", t=0.96)], node_from="stance", node_to="stance", layer_mask="upper_body")
    heavy_swing("chair_swing", "chair", note="Стальний стілець в одній руці: замах, удар, повернення до стійки.")
    heavy_swing("case_swing", "case", note="Кейс в одній руці: той самий удар, що й стільцем.")

    # кендо: дві руки, удар згори. Ліва кисть вище на ручці.
    def kendo_swing(dur=1.6):
        F, ts = T.frames(dur)
        lq, h = T.base("Ring_Stance_Loop", F)
        base_lq, base_h = lq.copy(), h.copy()
        t = ts
        fwd = ease_keys(t, [(0, (0,)), (0.40, (-12,)), (0.56, (-13,)), (0.84, (24,)), (1.00, (26,)), (dur, (0,))])[:, 0]
        h = h.copy()
        h[:, 1] += ease_keys(t, [(0, (0,)), (0.42, (0.05 * k,)), (0.84, (-0.08 * k,)), (1.02, (-0.09 * k,)), (dur, (0,))])[:, 0]
        lq = T.pin_feet_to(lq, h, base_lq, base_h)
        lq = T.spine_bend(lq, h, fwd=fwd)
        lq = T.look(lq, h, pitch=-fwd * 0.4)
        off = ease_keys(t, [(0, (-0.08, -0.30, -0.10)), (0.40, (-0.10, -0.30, 0.52)), (0.56, (-0.10, -0.26, 0.66)), (0.72, (-0.08, -0.46, 0.46)), (0.84, (-0.04, -0.52, 0.10)), (0.98, (-0.04, -0.48, -0.08)), (dur, (-0.08, -0.30, -0.10))]) * k
        fingers = ease_keys(t, [(0, (0.05, -0.85, -0.5)), (0.40, (0.05, -1.0, 0.45)), (0.56, (0.05, -1.0, 0.55)), (0.72, (0.05, -0.8, -0.35)), (0.84, (0.05, -0.15, -1.0)), (0.98, (0.05, 0.15, -1.0)), (dur, (0.05, -0.85, -0.5))])
        palm = np.tile(np.array([1.0, 0.0, 0.0]), (F, 1))
        lq = T.ik_arm(lq, h, "r", lambda wq, wp: T.chest_point(wq, wp, off), (-0.58 * k, 0.05 * k, -0.25 * k), hand=lambda wq, wp: T.hand_q("r", fingers, palm))
        lq = T.grip(lq, "r", 1.0)
        # ліва кисть — на ручці палки вище за праву (вздовж напрямку пальців)
        def left_target(wq, wp):
            j = sk.i("hand_r")
            fd = q_rot(q_mul(wq[:, j], q_inv(sk.rest_q[j])), T.hand_frame_rest("r")[0])
            nd = q_rot(q_mul(wq[:, j], q_inv(sk.rest_q[j])), T.hand_frame_rest("r")[1])
            ad = q_rot(q_mul(wq[:, j], q_inv(sk.rest_q[j])), T.hand_frame_rest("r")[2])
            return wp[:, j] + ad * 0.17 * k + nd * 0.0 + fd * 0.03
        lq = T.ik_arm(lq, h, "l", left_target, (0.58 * k, 0.05 * k, -0.25 * k))
        lq = T.grip(lq, "l", 0.95)
        T.add("kendo_swing", lq, h, cat="prop", prop="kendo", note="Кендо-палка: удар згори двома руками.", events=[dict(event="contact", t=0.87)], node_from="stance", node_to="stance", layer_mask="upper_body")
    kendo_swing()

    # спрей: бак у лівій руці біля пояса, розпилювач у правій уперед
    def spray_arms(lq, h, ts, squeeze, aim_up=0.0):
        F = lq.shape[0]
        tl = lambda wq, wp: T.pelvis_point(wq, wp, np.array([0.27 * k, -0.03 * k, -0.04 * k]))
        lq = T.ik_arm(lq, h, "l", tl, (0.55 * k, 0.18 * k, -0.25 * k),
                      hand=lambda wq, wp: T.hand_q("l", np.tile(np.array([0.0, -0.12, -1.0]), (F, 1)), np.tile(np.array([-1.0, 0.0, 0.0]), (F, 1))))
        lq = T.grip(lq, "l", 1.0)
        off = np.array([-0.17, -0.42, 0.06]) * k
        offs = np.tile(off, (F, 1)) + np.stack([0.02 * np.sin(ts * 2.3), np.zeros(F), aim_up + 0.012 * np.sin(ts * 2 * np.pi * 2)], 1) * k
        lq = T.ik_arm(lq, h, "r", lambda wq, wp: T.chest_point(wq, wp, offs), (-0.55 * k, 0.15 * k, -0.28 * k),
                      hand=lambda wq, wp: T.hand_q("r", np.tile(np.array([0.05, -1.0, 0.05]), (F, 1)), np.tile(np.array([1.0, 0.0, -0.2]), (F, 1))))
        lq = T.grip(lq, "r", 0.85)
        # вказівний палець тисне на гашетку
        j1 = sk.i("index_01_r")
        f0, a0, n0 = T.hand_frame_rest("r")
        axl = q_rot(q_inv(sk.rest_q[j1]), a0)
        lq[:, j1] = q_mul(lq[:, j1], q_axis_angle(np.tile(axl, (F, 1)), np.radians(18.0) * squeeze))
        return lq
    F, ts = T.frames(3.0)
    lq, h = T.base("U_Idle_Loop", F)
    lq = spray_arms(lq, h, ts, np.zeros(F))
    lq[-1] = lq[0]
    T.add("spray_idle_Loop", lq, h, loop=True, cat="prop", prop="spray", note="Бак у лівій руці, розпилювач у правій, без бризок.", layer_mask="upper_body")
    F, ts = T.frames(2.0)
    lq, h = T.base("U_Idle_Loop", F)
    sq2 = np.clip(np.sin(ts * 2 * np.pi * 2.0) * 2.0, 0, 1)
    lq = spray_arms(lq, h, ts, sq2)
    lq[-1] = lq[0]
    T.add("spray_water", lq, h, loop=True, cat="prop", prop="spray", note="Бризкає водою: тисне на гашетку двічі за цикл.", events=[dict(event="spray", t=0.13), dict(event="spray", t=1.13)], layer_mask="upper_body")

    # ----- кут: залізти на верхній канат і стрибки. Чорновий рівень.
    corner_top(T, k)


def corner_top(T, k):
    sk = T.sk
    foot_z = sk.rest_p[sk.i("foot_l"), 2]
    # Заліз у кут (рига -Y — у ринг; кут позаду +Y): стартує спиною до рингу? Простий вхід: обличчям у кут.
    dur = 2.6
    F, ts = T.frames(dur)
    lq, h = T.base("Ring_Stance_Loop", F)
    H0 = h[0].copy()
    z = ease_keys(ts, [(0, (0,)), (0.5, (0.02,)), (1.0, (0.42 * k,)), (1.25, (0.46 * k,)), (1.8, (0.90 * k,)), (dur, (1.18 * k,))])[:, 0]
    yy = ease_keys(ts, [(0, (0,)), (1.0, (0.18 * k,)), (dur, (0.30 * k,))])[:, 0]
    h = h.copy(); h[:, 2] = H0[2] + z; h[:, 1] = H0[1] + yy
    lq = T.spine_bend(lq, h, fwd=ease_keys(ts, [(0, (0,)), (0.9, (24,)), (1.8, (18,)), (dur, (4,))])[:, 0], side=ease_keys(ts, [(0, (0,)), (0.9, (6,)), (1.8, (-6,)), (dur, (0,))])[:, 0])
    # стопи: кроки на нижній, середній і верхній канат
    fl = ease_keys(ts, [(0, (0.14 * k, H0[1], foot_z)), (0.4, (0.14 * k, H0[1], foot_z)), (0.85, (0.24 * k, 0.40 * k, 0.55)), (1.45, (0.24 * k, 0.40 * k, 0.55)), (2.0, (0.26 * k, 0.48 * k, 1.05)), (dur, (0.26 * k, 0.50 * k, 1.10))])
    fr = ease_keys(ts, [(0, (-0.14 * k, H0[1], foot_z)), (0.9, (-0.14 * k, H0[1], foot_z)), (1.35, (-0.24 * k, 0.40 * k, 0.55)), (1.9, (-0.24 * k, 0.42 * k, 0.60)), (2.3, (-0.26 * k, 0.50 * k, 1.10)), (dur, (-0.26 * k, 0.50 * k, 1.10))])
    lq = T.ik_leg(lq, h, "l", fl)
    lq = T.ik_leg(lq, h, "r", fr)
    hl = ease_keys(ts, [(0, (0.30, -0.10, 0.0)), (0.5, (0.35, 0.40, 0.34)), (1.6, (0.38, 0.42, 0.20)), (2.2, (0.44, 0.30, -0.15)), (dur, (0.48, -0.10, 0.02))]) * k
    hr = ease_keys(ts, [(0, (-0.30, -0.10, 0.0)), (0.5, (-0.35, 0.40, 0.34)), (1.6, (-0.38, 0.42, 0.20)), (2.2, (-0.44, 0.30, -0.15)), (dur, (-0.48, -0.10, 0.02))]) * k
    lq = T.ik_arm(lq, h, "l", lambda wq, wp: T.chest_point(wq, wp, hl), (0.65 * k, 0.1 * k, -0.2 * k))
    lq = T.ik_arm(lq, h, "r", lambda wq, wp: T.chest_point(wq, wp, hr), (-0.65 * k, 0.1 * k, -0.2 * k))
    lq = T.grip(lq, "l", 0.8); lq = T.grip(lq, "r", 0.8)
    T.add("Corner_Climb_Top", lq, h, cat="corner", status="authored", node_from="stance", node_to="top_rope", note="Чорновий: заліз на верхній канат у куті, стоїть на двох стопах. Висота каната 1,10 м.")
    last_lq, last_h = lq[-1], h[-1]
    # Стоїть нагорі: руки вгору
    dur = 3.0
    F, ts = T.frames(dur)
    lq = np.tile(last_lq, (F, 1, 1)); h = np.tile(last_h, (F, 1))
    b = np.sin(ts * 2 * np.pi / dur)
    h = h + np.array([0, 0, 0.012])[None] * b[:, None]
    lq = T.spine_bend(lq, h, fwd=-3 + 2 * b)
    T.add("Top_Rope_Perch_Loop", lq, h, loop=True, cat="corner", node_from="top_rope", node_to="top_rope", note="Чорновий: балансує нагорі перед стрибком.")
    # Стрибки: балістичний таз, тіло розгортається вперед, падіння на живіт
    def dive(name, face_down=True, elbow=False, dur=1.55, note=None):
        F, ts = T.frames(dur)
        lq = np.tile(last_lq, (F, 1, 1)); h = np.tile(last_h, (F, 1)).copy()
        H = last_h.copy()
        land = 0.16 * k
        T1 = 0.92
        # таз: присід, виліт уперед (−Y), парабола, приземлення
        ydist = 1.55 * k
        u = np.clip((ts - 0.28) / (T1 - 0.28), 0, 1)
        prep = ease_keys(ts, [(0, (0,)), (0.22, (-0.14 * k,)), (0.28, (-0.08 * k,)), (dur, (0,))])[:, 0]
        arc = 4 * u * (1 - u) * (0.48 * k)
        zz = H[2] * (1 - u) + land * u + arc + prep * (ts < 0.4)
        yy = H[1] - ydist * u ** 0.9
        h[:, 2] = zz; h[:, 1] = yy
        # оберт таза вперед: лицем униз
        pitch = ease_keys(ts, [(0, (0,)), (0.28, (6,)), (0.55, (52,)), (T1, (90,)), (dur, (90,))])[:, 0]
        wrot = q_axis_angle(np.array([1.0, 0, 0]), np.radians(pitch))
        lq[:, 0] = q_mul(wrot, lq[:, 0])
        # руки: уперед і в сторони; ноги: витягнуті назад
        wq, wp = sk.fk(lq, h)
        for s, sg in (("l", 1), ("r", -1)):
            off = ease_keys(ts, [(0, (sg * 0.48, -0.10, 0.02)), (0.28, (sg * 0.46, 0.18, -0.12)), (0.55, (sg * 0.55, -0.30, 0.25)), (T1, (sg * 0.60, -0.05, 0.05)), (dur, (sg * 0.56, -0.02, 0.02))]) * k
            if elbow and s == "r":
                off = ease_keys(ts, [(0, (sg * 0.48, -0.10, 0.02)), (0.28, (sg * 0.40, 0.10, 0.18)), (0.60, (sg * 0.22, -0.10, 0.34)), (T1, (sg * 0.18, -0.26, 0.05)), (dur, (sg * 0.30, -0.10, 0.02))]) * k
            lq = T.ik_arm(lq, h, s, lambda wq, wp, off=off: T.chest_point(wq, wp, off), (sg * 0.6 * k, 0.15 * k, 0.1 * k))
            lq = T.grip(lq, s, 0.4 if not (elbow and s == "r") else 1.0)
            # нога: від стопи на канаті до витягнутої позаду
            foot_end = ease_keys(ts, [(0, tuple(sk.fk(lq[:1], h[:1])[1][0, sk.i("foot_" + s)] - h[0])), (0.28, (sg * 0.20 * k, 0.10 * k, -0.40 * k)), (0.55, (sg * 0.18 * k, 0.55 * k, -0.30 * k)), (T1, (sg * 0.17 * k, 0.86 * k, -0.04 * k)), (dur, (sg * 0.17 * k, 0.86 * k, -0.08 * k))])
            lq = T.ik_leg(lq, h, s, h + foot_end, pole_dir=(0, 0.6, -0.8))
        # кінець — поза лежання на животі (плавно за 0,4 с після контакту), щоб стик із циклом лежання був нульовий
        lf = T.lib["Lying_Front_Loop"][0][0]; hf = T.lib["Lying_Front_Loop"][1][0]
        w = smoothstep((ts - T1) / max(dur - T1, 1e-3))
        lq = q_slerp(lq, np.tile(lf, (F, 1, 1)), w[:, None, None])
        h[:, 2] = h[:, 2] * (1 - w) + hf[2] * w
        # рух переносимо в корінь
        from clips_build_rootfree import to_root
        qq, hh, root = to_root(sk, lq, h, F)
        hh[:, :2] = hh[:, :2] * (1 - w[:, None]) + hf[:2] * w[:, None]
        T.add(name, qq, hh, root=root, cat="corner", status="authored", node_from="top_rope", node_to="lying_front", note=note, events=[dict(event="contact", t=T1)])
    dive("Top_Dive_Crossbody", note="Чорновий: стрибок із верхнього каната тілом уперед, приземлення на живіт (на суперника чи на мат).")
    dive("Top_Dive_Elbow", elbow=True, note="Чорновий: стрибок із верхнього каната з ліктем.")
    # Удар у куті: упирається рукою в канат і б'є плечем (одна постать, суперник — Move_Corner_Lean_Loop)
    dur = 1.5
    F, ts = T.frames(dur)
    lq, h = T.base("Ring_Stance_Loop", F)
    base_lq, base_h = lq.copy(), h.copy()
    h = h.copy()
    h[:, 1] += ease_keys(ts, [(0, (0,)), (0.35, (0.10 * k,)), (0.62, (-0.22 * k,)), (0.8, (-0.24 * k,)), (dur, (0,))])[:, 0]
    lq = T.pin_feet_to(lq, h, base_lq, base_h)
    lq = T.spine_bend(lq, h, fwd=ease_keys(ts, [(0, (0,)), (0.35, (-12,)), (0.62, (22,)), (0.85, (24,)), (dur, (0,))])[:, 0], twist=ease_keys(ts, [(0, (0,)), (0.35, (-26,)), (0.62, (28,)), (dur, (0,))])[:, 0])
    off = ease_keys(ts, [(0, (-0.24, -0.20, 0.0)), (0.35, (-0.34, 0.10, 0.10)), (0.62, (-0.10, -0.52, 0.10)), (0.78, (-0.12, -0.48, 0.05)), (dur, (-0.24, -0.20, 0.0))]) * k
    lq = T.ik_arm(lq, h, "r", lambda wq, wp: T.chest_point(wq, wp, off), (-0.6 * k, 0.15 * k, -0.25 * k))
    lq = T.grip(lq, "r", 1.0)
    T.add("Corner_Strike", lq, h, cat="corner", node_from="stance", node_to="stance", note="Чорновий: удар рукою по суперникові в куті.", events=[dict(event="contact", t=0.64)], layer_mask="upper_body")
