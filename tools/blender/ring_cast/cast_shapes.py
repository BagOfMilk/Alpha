# Форма тіла за архетипом: м'язи й об'єм як зміщення вздовж нормалі. Працює на масивах numpy і для вершин (Blender), і для текселів (малювання).
# Координати — скелет набору до масштабування: X — ліво(+), Y — назад(+), Z — вгору; борець дивиться в −Y.
import numpy as np


def bone_pts(B, name):
    return np.asarray(B[name]["head"], dtype=np.float64), np.asarray(B[name]["tail"], dtype=np.float64)


def limb_blob(P, B, bone, t, r_along, r_side=None, direction=None, sharp=1.5):
    """Гаусіан уздовж кістки; direction — вектор: лише з того боку кістки. Повертає вагу 0..1."""
    a, b = bone_pts(B, bone)
    ax = b - a
    L = np.linalg.norm(ax)
    ax = ax / L
    q = P - a
    al = np.sum(q * ax, axis=-1)
    g = np.exp(-((al - t * L) / (r_along * L)) ** 2)
    if direction is not None:
        rad = q - al[..., None] * ax
        rn = np.linalg.norm(rad, axis=-1)
        d = np.asarray(direction, dtype=np.float64)
        d = d - np.dot(d, ax) * ax
        d = d / max(np.linalg.norm(d), 1e-6)
        c = np.sum(rad * d, axis=-1) / np.maximum(rn, 1e-6)
        g = g * np.maximum(c * 0.5 + 0.5, 0.0) ** sharp
    return g


def ell_blob(P, c, r):
    d = (P - np.asarray(c, dtype=np.float64)) / np.asarray(r, dtype=np.float64)
    return np.exp(-np.sum(d * d, axis=-1))


def anchors(B):
    A = {}
    for k in ("pelvis", "spine_01", "spine_02", "spine_03", "neck_01", "head", "thigh_l", "calf_l", "foot_l", "ball_l", "upperarm_l", "lowerarm_l", "hand_l", "clavicle_l"):
        A[k] = np.asarray(B[k]["head"], dtype=np.float64)
    A["head_top"] = np.asarray(B["head"]["tail"], dtype=np.float64)
    return A


# Амплітуди, м. Значення підібрані очима на кадрах.
SHAPES = {
    "power": dict(all=0.006, delt=0.024, bicep=0.024, tricep=0.016, forearm=0.014, pec=0.026, trap=0.026, lat=0.022, abs=0.004, gut=0.010, glute=0.010, quad=0.024, ham=0.016, calf=0.016, neck=0.016, waist=0.0),
    "lean": dict(all=-0.002, delt=0.012, bicep=0.012, tricep=0.008, forearm=0.006, pec=0.012, trap=0.008, lat=0.012, abs=0.008, gut=-0.012, glute=0.004, quad=0.012, ham=0.008, calf=0.010, neck=0.002, waist=-0.012),
    "showman": dict(all=0.002, delt=0.016, bicep=0.014, tricep=0.010, forearm=0.008, pec=0.018, trap=0.012, lat=0.012, abs=0.002, gut=0.002, glute=0.006, quad=0.014, ham=0.010, calf=0.010, neck=0.006, waist=-0.004),
    "monster": dict(all=0.012, delt=0.032, bicep=0.030, tricep=0.022, forearm=0.020, pec=0.030, trap=0.040, lat=0.030, abs=0.0, gut=0.026, glute=0.014, quad=0.030, ham=0.020, calf=0.022, neck=0.028, waist=0.0),
    "athletic_f": dict(all=0.0, delt=0.010, bicep=0.008, tricep=0.005, forearm=0.004, pec=0.0, trap=0.006, lat=0.006, abs=0.006, gut=-0.008, glute=0.008, quad=0.012, ham=0.008, calf=0.008, neck=0.0, waist=-0.008),
    "lean_f": dict(all=-0.001, delt=0.006, bicep=0.005, tricep=0.004, forearm=0.002, pec=0.0, trap=0.002, lat=0.002, abs=0.004, gut=-0.010, glute=0.004, quad=0.006, ham=0.004, calf=0.005, neck=-0.001, waist=-0.010),
    "scrappy_f": dict(all=0.001, delt=0.009, bicep=0.008, tricep=0.005, forearm=0.005, pec=0.0, trap=0.005, lat=0.006, abs=0.003, gut=-0.003, glute=0.005, quad=0.009, ham=0.006, calf=0.006, neck=0.001, waist=-0.004),
    "plain_f": dict(all=0.0, delt=0.0, bicep=0.0, tricep=0.0, forearm=0.0, pec=0.0, trap=0.0, lat=0.0, abs=0.0, gut=-0.004, glute=0.002, quad=0.0, ham=0.0, calf=0.0, neck=0.0, waist=-0.006),
}


def muscle_fields(P, F, B):
    """Поле кожної групи м'язів 0..1 у точках P. F — поля-маски (arm, leg, head)."""
    A = anchors(B)
    arm, leg, head = F["arm"], F["leg"], F["head"]
    torso = np.clip(1.0 - arm - leg - head, 0.0, 1.0)
    front = (0, -1, 0)
    back = (0, 1, 0)
    out = {}
    chest_z = A["pelvis"][2] + 0.60 * (A["neck_01"][2] - A["pelvis"][2])     # лінія грудей, а не кістка spine_03
    wide = abs(A["upperarm_l"][0])
    ys = A["spine_03"][1]
    def both(fn):
        return np.maximum(fn(1.0), fn(-1.0))
    def side_bone(n, s):
        return n + ("_l" if s > 0 else "_r")
    out["delt"] = both(lambda s: limb_blob(P, B, side_bone("upperarm", s), 0.10, 0.34) * np.maximum(arm, 0.35 * torso))
    out["bicep"] = both(lambda s: limb_blob(P, B, side_bone("upperarm", s), 0.52, 0.28, direction=front) * arm)
    out["tricep"] = both(lambda s: limb_blob(P, B, side_bone("upperarm", s), 0.45, 0.32, direction=back) * arm)
    out["forearm"] = both(lambda s: limb_blob(P, B, side_bone("lowerarm", s), 0.28, 0.30) * arm)
    out["pec"] = both(lambda s: ell_blob(P, (s * wide * 0.46, ys - 0.11, chest_z), (wide * 0.46, 0.09, 0.075))) * torso
    out["trap"] = both(lambda s: ell_blob(P, (s * wide * 0.42, ys + 0.03, A["neck_01"][2] - 0.015), (wide * 0.50, 0.08, 0.06))) * np.maximum(torso, head * 0.4)
    out["lat"] = both(lambda s: ell_blob(P, (s * wide * 0.80, ys + 0.03, chest_z - 0.11), (wide * 0.30, 0.10, 0.13))) * torso
    z_abs = A["spine_01"][2] + 0.09
    out["abs"] = ell_blob(P, (0, ys - 0.10, z_abs), (wide * 0.42, 0.08, 0.14)) * torso
    out["gut"] = ell_blob(P, (0, ys - 0.12, A["spine_01"][2] + 0.04), (wide * 0.72, 0.12, 0.16)) * torso
    out["waist"] = both(lambda s: ell_blob(P, (s * wide * 0.72, ys + 0.0, A["spine_01"][2] + 0.04), (wide * 0.30, 0.13, 0.10))) * torso
    out["glute"] = both(lambda s: ell_blob(P, (s * 0.085, A["pelvis"][1] + 0.11, A["pelvis"][2] - 0.07), (0.09, 0.09, 0.10)))
    out["quad"] = both(lambda s: limb_blob(P, B, side_bone("thigh", s), 0.42, 0.34, direction=front, sharp=1.0) * leg)
    out["ham"] = both(lambda s: limb_blob(P, B, side_bone("thigh", s), 0.48, 0.32, direction=back, sharp=1.0) * leg)
    out["calf"] = both(lambda s: limb_blob(P, B, side_bone("calf", s), 0.28, 0.26, direction=back, sharp=1.0) * leg)
    a, b = bone_pts(B, "neck_01")
    out["neck"] = limb_blob(P, B, "neck_01", 0.35, 0.65) * np.clip(head + torso * 0.5, 0, 1)
    out["all"] = np.clip(arm + leg + torso, 0.0, 1.0) * (1.0 - head)
    return out


def shape_offset(P, F, B, shape):
    S = SHAPES[shape]
    fields = muscle_fields(P, F, B)
    d = np.zeros(P.shape[:-1])
    for k, g in fields.items():
        d += S.get(k, 0.0) * g
    return d


def definition_height(P, F, B, shape):
    """Висота дрібного малюнка м'язів для карти нормалей: борозни між групами, кубики преса. У метрах, ±."""
    S = SHAPES[shape]
    k = sum(max(S.get(n, 0.0), 0.0) for n in ("pec", "abs", "delt", "bicep", "quad")) / 0.09
    k = min(k, 1.0)
    A = anchors(B)
    arm, leg, head = F["arm"], F["leg"], F["head"]
    torso = np.clip(1.0 - arm - leg - head, 0.0, 1.0)
    x, y, z = P[..., 0], P[..., 1], P[..., 2]
    h = np.zeros(P.shape[:-1])
    ys = A["spine_03"][1]
    frontm = np.clip((ys + 0.02 - y) / 0.05, 0, 1) * torso
    # лінія посередині грудей і живота
    chest_z = A["pelvis"][2] + 0.60 * (A["neck_01"][2] - A["pelvis"][2])
    h -= 0.0028 * np.exp(-(x / 0.008) ** 2) * frontm * np.clip((chest_z + 0.09 - z) / 0.03, 0, 1) * np.clip((z - A["pelvis"][2] - 0.02) / 0.05, 0, 1)
    # нижній край грудей
    pec_low = chest_z - 0.075 - 0.25 * np.abs(x) * 0.5
    h -= 0.0024 * np.exp(-((z - pec_low + 0.42 * x * x) / 0.012) ** 2) * frontm * np.clip((0.15 - np.abs(x)) / 0.03, 0, 1)
    # кубики преса (лише якщо рельєф є)
    zs = A["spine_01"][2]
    ab = S.get("abs", 0.0)
    if ab > 0.003:
        for dz in (-0.03, 0.03, 0.09, 0.145):
            h -= 0.0016 * min(ab / 0.006, 1.0) * np.exp(-((z - (zs + dz)) / 0.007) ** 2) * frontm * np.clip((0.075 - np.abs(x)) / 0.02, 0, 1)
    # борозна дельти / біцепса
    if arm is not None:
        def along(bone):
            a = np.asarray(B[bone]["head"]); b = np.asarray(B[bone]["tail"])
            ax = (b - a) / np.linalg.norm(b - a)
            return np.sum((P - a) * ax, axis=-1) / np.linalg.norm(b - a)
        for s, sd in ((1, "l"), (-1, "r")):
            m = arm * (np.sign(x) == s)
            h -= 0.0020 * np.exp(-((along("upperarm_" + sd) - 0.36) / 0.05) ** 2) * m
            h -= 0.0012 * np.exp(-((along("lowerarm_" + sd) - 0.06) / 0.05) ** 2) * m
    # над коліном
    for s, sd in ((1, "l"), (-1, "r")):
        a = np.asarray(B["thigh_" + sd]["head"]); b = np.asarray(B["thigh_" + sd]["tail"])
        ax = (b - a) / np.linalg.norm(b - a)
        t = np.sum((P - a) * ax, axis=-1) / np.linalg.norm(b - a)
        h -= 0.0015 * np.exp(-((t - 0.88) / 0.05) ** 2) * leg * (np.sign(x) == s)
    return h * (0.35 + 0.65 * k)
