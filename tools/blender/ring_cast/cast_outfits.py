# Спорядження, намальоване на тілі: зони (труси, трико, чоботи, наколінники, тейп, татуювання) як поля в просторі тіла.
# Працює і для вершин (потовщення в Blender), і для текселів (малювання). Жодних слів у малюнках.
import numpy as np
import cast_shapes as SH


def rgb(h):
    return ((h >> 16 & 255) / 255.0, (h >> 8 & 255) / 255.0, (h & 255) / 255.0)


class Fields:
    def __init__(self, P, F, B, sex, soft=0.012):
        self.P = P
        self.x, self.y, self.z = P[..., 0], P[..., 1], P[..., 2]
        self.ax = np.abs(self.x)
        self.arm, self.leg, self.head = F["arm"], F["leg"], F["head"]
        self.torso = np.clip(1.0 - self.arm - self.leg - self.head, 0.0, 1.0)
        self.B = B
        self.sex = sex
        self.soft = soft
        self.A = SH.anchors(B)
        A = self.A
        self.z_waist = A["spine_01"][2] + (0.035 if sex == "m" else 0.03)
        self.z_hip = A["pelvis"][2]
        self.z_knee = A["calf_l"][2]
        self.z_ankle = A["foot_l"][2]
        self.z_chest = A["spine_03"][2]
        self.z_neck = A["neck_01"][2]
        self.z_bust = A["pelvis"][2] + 0.62 * (A["neck_01"][2] - A["pelvis"][2])
        self.hz = A["head"][2]
        self.hy = A["head"][1]
        self.ys = A["spine_03"][1]
        self.front = self.y < A["pelvis"][1]

    def st(self, v, w=None):
        """Плавний поріг: 1 при v>0."""
        w = self.soft if w is None else w
        return np.clip(v / w + 0.5, 0.0, 1.0)

    def along(self, bone):
        """Для кістки: t (0..1 вздовж), радіус, кут навколо кістки (0 — уперед)."""
        a = np.asarray(self.B[bone]["head"], dtype=np.float64)
        b = np.asarray(self.B[bone]["tail"], dtype=np.float64)
        ax = b - a
        L = np.linalg.norm(ax)
        ax = ax / L
        q = self.P - a
        al = np.sum(q * ax, axis=-1)
        rad = q - al[..., None] * ax
        r = np.linalg.norm(rad, axis=-1)
        f = np.array([0, -1.0, 0]); f = f - np.dot(f, ax) * ax; f /= np.linalg.norm(f)
        s = np.cross(ax, f)
        phi = np.arctan2(np.sum(rad * s, axis=-1), np.sum(rad * f, axis=-1))
        return al / L, r, phi

    def side(self, bone_base):
        """Параметри кістки своєї сторони (за знаком X)."""
        tl, rl, pl = self.along(bone_base + "_l")
        tr, rr, pr = self.along(bone_base + "_r")
        left = self.x >= 0
        return np.where(left, tl, tr), np.where(left, rl, rr), np.where(left, pl, pr)

    def not_arm(self):
        return 1.0 - self.st(self.arm - 0.5, 0.2)

    def on_leg(self):
        return self.st(self.leg - 0.25, 0.2)

    def on_arm(self):
        return self.st(self.arm - 0.5, 0.2)

    # --- зони ---
    def below_waist(self, extra=0.0):
        return self.st(self.z_waist + extra - self.z) * self.not_arm()

    def thigh_cut(self, cut):
        t, _, _ = self.side("thigh")
        return self.st((cut - t) * 0.42)

    def trunks(self, cut=0.10, extra=0.0):
        return self.below_waist(extra) * self.thigh_cut(cut)

    def boots(self, up=0.62, curve=0.018):
        t, _, phi = self.side("calf")
        top = self.z_ankle + (self.z_knee - self.z_ankle) * up + curve * np.cos(phi)
        return self.st(top - self.z) * self.on_leg()

    def tights(self):
        return self.below_waist() * (1.0 - self.boots(0.45) * 0.0)

    def band(self, z, half):
        return self.st(half - np.abs(self.z - z))

    def knee_pad(self, half=0.075, side=None):
        t, r, phi = self.side("calf")
        zk = self.z_knee
        m = self.st(half - np.abs(self.z - zk - 0.005)) * self.on_leg()
        if side is not None:
            m = m * (self.x * side > 0)
        return m

    def wrist(self, lo=0.74, hi=1.02):
        t, r, phi = self.side("lowerarm")
        return self.st((t - lo) * 0.25) * self.st((hi - t) * 0.25) * self.on_arm()

    def forearm_wrap(self, lo=0.2):
        return self.wrist(lo, 1.02)

    def hand_wrap(self):
        t, r, phi = self.side("hand")
        return self.st((0.75 - t) * 0.09) * self.on_arm() * self.st(t * 0.09 + 0.01)

    def glove(self, lo=0.8):
        t, r, _ = self.side("lowerarm")
        return self.st((t - lo) * 0.25) * self.on_arm()

    def upper_arm_band(self, t0, half):
        t, r, phi = self.side("upperarm")
        return self.st((half - np.abs(t - t0)) * 0.3) * self.on_arm()


def _h(hexv):
    return rgb(hexv)


def lay(name, m, color, rough=0.5, metal=0.0, thick=0.0, fab="cloth", seam=True, mix=1.0, clearcoat=False, **kw):
    d = dict(name=name, m=m, color=color, rough=rough, metal=metal, thick=thick, fab=fab, seam=seam, mix=mix)
    d.update(kw)
    return d


def ink(name, m, color=0x1E2A3A, mix=0.82):
    return dict(name=name, m=m, color=rgb(color), rough=None, metal=0.0, thick=0.0, fab="ink", seam=False, mix=mix)


# --- малюнки татуювань ---
def tribal_band(G, bone, t0, half, side, n=7):
    t, r, phi = G.along(bone)
    s = (G.x * side > 0) * G.on_arm()
    w = half * (0.55 + 0.45 * np.sin(phi * n))
    m = G.st((w - np.abs(t - t0)) * 0.3, 0.003) * s
    spikes = G.st((half * 1.5 * (0.5 + 0.5 * np.cos(phi * n * 2)) ** 3 - np.abs(t - t0 - half)) * 0.3, 0.003) * s
    return np.maximum(m, spikes)


def star_field(u, v, n=5):
    a = np.arctan2(v, u)
    r = np.hypot(u, v)
    k = 0.5 + 0.5 * np.cos(a * n)
    return r - (0.45 + 0.55 * k ** 2)


def chest_crest(G, cz, size):
    u = G.x / size
    v = (G.z - cz) / size
    fr = G.st(G.ys + 0.02 - G.y) * G.torso
    star = G.st(-star_field(u, v, 8) * size, 0.0015)
    ring = G.st((0.12 - np.abs(np.hypot(u, v) - 1.15)) * size, 0.0015)
    return np.maximum(star, ring) * fr


def hammer_mark(G, cz, size):
    u = G.x / size
    v = (G.z - cz) / size
    fr = G.st(G.ys + 0.02 - G.y) * G.torso
    # молот нахилено: так він не читається як літера
    c, s = np.cos(0.5), np.sin(0.5)
    uu = u * c - v * s
    vv = u * s + v * c
    head = G.st(0.78 - np.abs(uu), 0.02) * G.st(0.30 - np.abs(vv - 0.62), 0.02)
    notch = 1.0 - G.st(0.10 - np.abs(np.abs(uu) - 0.42), 0.02) * G.st(0.34 - np.abs(vv - 0.62), 0.02) * 0.65
    shaft = G.st(0.12 - np.abs(uu), 0.02) * G.st(1.0 - np.abs(vv + 0.15), 0.02)
    return np.maximum(head * notch, shaft) * fr


def web_mark(G, cx, cz, size, front=None):
    u = (G.x - cx) / size
    v = (G.z - cz) / size
    r = np.hypot(u, v)
    a = np.arctan2(v, u)
    spokes = G.st(0.012 / np.maximum(size, 1e-3) * size - np.abs(np.sin(a * 4.0)) * r * size, 0.0012)
    rings = G.st(0.0022 - np.abs(np.sin(r * 11.0) * 0.012 * (1.0 + 0.2 * np.cos(a * 8.0))), 0.0012)
    m = np.maximum(spokes, rings) * G.st(1.0 - r, 0.02)
    return m


def sleeve_blocks(G, bone, side, t0, t1, n=9):
    t, r, phi = G.along(bone)
    s = (G.x * side > 0) * G.on_arm() * G.st((t - t0) * 0.3, 0.003) * G.st((t1 - t) * 0.3, 0.003)
    a = np.sin((t * n * 3.1) + np.sin(phi * 3.0) * 1.2) + 0.65 * np.sin(phi * 5.0 + t * 7.0)
    return G.st(a - 0.3, 0.05) * s


# --- персонажі ---
def layers(cid, G):
    return globals()["look_" + cid](G)


def moustache(G):
    """Козацькі вуса: від-під носа дві дуги звужуються й опускаються нижче кутиків рота."""
    A = G.A
    hz = A["head"][2]
    fr = (G.y < A["head"][1] - 0.02) * G.st(G.head - 0.5, 0.2)
    u = np.clip(G.ax / 0.037, 0, 1)
    cz = hz - 0.030 - 0.033 * u ** 2.0
    half = 0.0058 * (1.0 - u ** 3) + 0.0007
    m = G.st(half - np.abs(G.z - cz), 0.0012) * G.st(0.037 - G.ax, 0.0012)
    # під носом вуса розділені
    m = m * G.st(G.ax - 0.0020, 0.0016)
    return m * fr


def brows(G):
    """Брови на шкірі: м'яка дуга над оком, кінець опускається до скроні."""
    hz = G.hz
    fr = (G.y < G.hy - 0.02) * G.st(G.head - 0.5, 0.2)
    u = (G.ax - 0.0345) / 0.0245
    cz = hz + 0.0535 + 0.0045 * (1.0 - u * u) - 0.0018 * u
    half = 0.0040 * (1.0 - 0.65 * np.clip((u + 1.0) * 0.5, 0, 1) ** 2)
    m = G.st(half - np.abs(G.z - cz), 0.0016) * G.st(1.0 - np.abs(u), 0.10)
    return m * fr


def goatee(G):
    """Вуса й борідка навколо рота, акуратні."""
    hz = G.hz
    fr = (G.y < G.hy - 0.015) * G.st(G.head - 0.5, 0.2)
    mouth = np.exp(-((G.ax / 0.021) ** 2 + ((G.z - (hz - 0.041)) / 0.0085) ** 2))
    ring = np.exp(-((G.ax / 0.040) ** 2 + ((G.z - (hz - 0.052)) / 0.038) ** 2))
    return G.st(ring - 0.42, 0.04) * (1.0 - G.st(mouth - 0.45, 0.1)) * fr


def full_beard(G):
    """Повна борода: щоки нижче вилиць, підборіддя, верх шиї; рот вільний."""
    hz, hy = G.hz, G.hy
    h = G.head
    mouth = np.exp(-((G.ax / 0.024) ** 2 + ((G.z - (hz - 0.043)) / 0.010) ** 2))
    # верх: від вуса вниз по щоці й угору бакенбардом до вуха
    upper = hz - 0.034 + 0.008 * np.exp(-(G.ax / 0.020) ** 2) + 0.040 * np.clip((G.ax - 0.055) / 0.035, 0, 1) ** 2
    # низ іде лінією щелепи; край нерівний за кутом навколо голови: не шарф на шиї й без зубців збоку
    a = np.arctan2(G.x, -(G.y - hy))
    strands = 0.0030 * np.sin(a * 46.0) + 0.0018 * np.sin(a * 97.0 + 1.3)
    lower = hz - 0.104 + 0.62 * G.ax + strands
    m = G.st(upper - G.z, 0.005) * G.st(h - 0.55, 0.15) * G.st(hy + 0.012 + 0.002 * np.sin(G.z * 300.0) - G.y, 0.006) * G.st(G.z - lower, 0.005)
    return m * (1.0 - G.st(mouth - 0.45, 0.1))


def look_stalevyi(G):
    steel, steel_d, trim, black = 0x5A6A78, 0x39434F, 0xC9D2DA, 0x15171B
    t, r, phi = G.side("thigh")
    side_panel = G.st(0.022 - np.abs(np.abs(phi) - 1.57) * r) * G.below_waist()
    return [
        lay("вуса", moustache(G), _h(0x4A3321), rough=0.75, fab="hair", thick=0.0025, seam=False),
        ink("плечова смуга", tribal_band(G, "upperarm_r", 0.42, 0.12, -1), 0x13202C),
        ink("передпліччя", sleeve_blocks(G, "lowerarm_l", 1, 0.15, 0.62), 0x13202C, 0.7),
        lay("трико", G.below_waist() , _h(steel), rough=0.42, fab="lycra", thick=0.004),
        lay("бік трико", side_panel, _h(steel_d), rough=0.5, fab="lycra"),
        lay("наколінники", G.knee_pad(0.08), _h(black), rough=0.62, fab="neoprene", thick=0.011),
        lay("кант наколінника", G.st(0.008 - np.abs(np.abs(G.z - G.z_knee - 0.005) - 0.072)) * G.on_leg(), _h(trim), rough=0.5, fab="lycra", seam=False),
        lay("чоботи", G.boots(0.66), _h(black), rough=0.32, fab="leather", thick=0.007),
        lay("шнурівка", G.boots(0.66) * G.st(0.02 - np.abs(G.side("calf")[2]) * 0.05) * G.st(G.z - G.z_ankle - 0.01), _h(trim), rough=0.8, fab="lace", seam=False),
        lay("силовий пояс", G.band(G.z_waist - 0.005, 0.036) * G.not_arm(), _h(0x3A2415), rough=0.45, fab="leather", thick=0.009),
        lay("пряжка", G.st(0.038 - G.ax) * G.band(G.z_waist - 0.005, 0.026) * G.front * G.not_arm(), _h(0xB9BEC4), rough=0.28, metal=1.0, fab="metal", thick=0.002, seam=False),
        lay("тейп на зап'ястях", G.wrist(0.78), _h(0xD8D3C6), rough=0.82, fab="tape", thick=0.005),
    ]


def look_dzhura(G):
    teal, gold, white = 0x1FA3A0, 0xD4A429, 0xE8E8E8
    t, r, phi = G.side("thigh")
    tc, rc, phic = G.side("calf")
    x, z = G.x, G.z
    head_m = G.st(G.head - 0.45, 0.2) * G.st(z - G.A["neck_01"][2] - 0.012)
    cx = 0.032
    zc = G.A["head"][2] + 0.035
    ey = G.y < G.A["head"][1]
    eye = np.exp(-(((G.ax - cx) / 0.024) ** 2 + ((z - zc) / 0.014) ** 2)) * ey
    mouth = np.exp(-((G.ax / 0.032) ** 2 + ((z - (G.A["head"][2] - 0.045)) / 0.020) ** 2)) * ey
    mask = head_m * (1.0 - G.st(eye - 0.5, 0.15)) * (1.0 - G.st(mouth - 0.5, 0.15))
    eye_trim = G.st(np.abs(eye - 0.32) * -1 + 0.14, 0.03) * head_m
    mouth_trim = G.st(np.abs(mouth - 0.32) * -1 + 0.14, 0.03) * head_m
    crest = G.st(0.012 - G.ax) * head_m * G.st(z - zc)
    return [
        ink("пір'я на передпліччі", sleeve_blocks(G, "lowerarm_r", -1, 0.12, 0.65, 7), 0x0E3B3A, 0.72),
        lay("шорти", G.trunks(0.42), _h(teal), rough=0.32, fab="lycra", thick=0.006),
        lay("золотий бік", G.trunks(0.42) * G.st(0.03 - np.abs(np.abs(phi) - 1.57) * r), _h(gold), rough=0.28, metal=0.6, fab="lycra", seam=False),
        lay("пояс шортів", G.band(G.z_waist - 0.004, 0.02) * G.not_arm(), _h(gold), rough=0.3, metal=0.5, fab="lycra", thick=0.002),
        lay("наколінники", G.knee_pad(0.07), _h(white), rough=0.55, fab="neoprene", thick=0.009),
        lay("чоботи", G.boots(0.80), _h(white), rough=0.28, fab="leather", thick=0.006),
        lay("крила чобота", G.boots(0.80) * G.st(0.5 * np.sin(tc * 16.0 + np.abs(phic) * 2.0) - 0.15, 0.1) * G.st(np.abs(phic) - 0.9, 0.2), _h(teal), rough=0.3, fab="leather", seam=False),
        lay("кант чобота", G.st(0.012 - np.abs(G.z - (G.z_ankle + (G.z_knee - G.z_ankle) * 0.80 + 0.018 * np.cos(phic)))) * G.on_leg(), _h(gold), rough=0.3, metal=0.6, fab="lycra", seam=False),
        lay("браслети", G.wrist(0.80), _h(gold), rough=0.3, metal=0.7, fab="lycra", thick=0.005),
        lay("маска", mask, _h(teal), rough=0.34, fab="lycra", thick=0.0015),
        lay("око маски", eye_trim, _h(gold), rough=0.3, metal=0.6, fab="lycra", seam=False),
        lay("рот маски", mouth_trim, _h(gold), rough=0.3, metal=0.6, fab="lycra", seam=False),
        lay("гребінь маски", crest, _h(gold), rough=0.3, metal=0.6, fab="lycra", seam=False),
    ]


def look_baron(G):
    purple, gold, white = 0x8E2E8E, 0xD4A429, 0xE8E8E8
    return [
        ink("герб на грудях", chest_crest(G, G.z_bust - 0.06, 0.048), 0x1B1B2C, 0.78),
        lay("борідка", goatee(G), _h(0x1C1512), rough=0.75, fab="hair", thick=0.0035, seam=False),
        ink("плече", tribal_band(G, "upperarm_l", 0.35, 0.10, 1, 5), 0x1B1B2C),
        lay("труси", G.trunks(0.14), _h(purple), rough=0.26, fab="lycra", thick=0.004),
        lay("золотий пояс", G.band(G.z_waist - 0.006, 0.022) * G.not_arm(), _h(gold), rough=0.26, metal=0.85, fab="metal", thick=0.003),
        lay("блискавка на трусах", G.trunks(0.14) * G.st(np.abs(np.sin(G.x * 60 + G.z * 40)) - 0.92, 0.04), _h(gold), rough=0.3, metal=0.7, fab="lycra", seam=False),
        lay("наколінники", G.knee_pad(0.07), _h(white), rough=0.42, fab="neoprene", thick=0.009),
        lay("чоботи", G.boots(0.74), _h(white), rough=0.18, fab="leather", thick=0.006),
        lay("пряжка чобота", G.st(0.016 - np.abs(G.z - G.z_ankle - 0.09)) * G.on_leg(), _h(gold), rough=0.3, metal=0.9, fab="metal", seam=False),
        lay("манжети", G.wrist(0.82), _h(gold), rough=0.26, metal=0.9, fab="metal", thick=0.005),
    ]


def look_hammer(G):
    red, black, dirty = 0xB0201A, 0x17171A, 0xB6AE9B
    z, ax, y = G.z, G.ax, G.y
    # безрукавка-борцівка: лямки на плечах, глибокі пройми
    strap = G.st(0.042 - np.abs(ax - 0.10)) * G.st(z - G.z_bust + 0.04)
    body = G.st(G.z_bust + 0.0 - z) * G.st(z - G.z_waist + 0.2) * (1.0 - G.st(G.arm - 0.25, 0.1)) * G.st(0.215 - ax + 0.0, 0.02)
    neckline = 1.0 - G.st(0.085 - np.hypot(ax, (z - G.z_bust - 0.03) * 0.8), 0.006) * G.st(G.ys + 0.04 - y)
    singlet = np.maximum(body, strap * G.st(G.torso - 0.12, 0.05)) * neckline * (1.0 - G.st(G.head - 0.5, 0.2))
    legs = G.trunks(0.36)
    return [
        ink("молот на грудях", hammer_mark(G, G.z_bust - 0.055, 0.058), 0x10151C, 0.8),
        lay("борода", full_beard(G), _h(0x6B4C2A), rough=0.9, fab="hair", thick=0.0035, seam=False),
        ink("рукав лівий", sleeve_blocks(G, "upperarm_l", 1, 0.05, 0.98, 11), 0x10151C, 0.8),
        ink("рукав правий", sleeve_blocks(G, "lowerarm_r", -1, 0.05, 0.72, 9), 0x10151C, 0.8),
        lay("борцівка", np.maximum(singlet, legs), _h(red), rough=0.46, fab="knit", thick=0.006),
        lay("чорний бік", np.maximum(singlet, legs) * G.st(0.024 - np.abs(G.ax - 0.20)), _h(black), rough=0.5, fab="knit", seam=False),
        lay("широкий пояс", G.band(G.z_waist - 0.012, 0.044) * G.not_arm(), _h(black), rough=0.38, fab="leather", thick=0.012),
        lay("наколінники", G.knee_pad(0.085), _h(black), rough=0.66, fab="neoprene", thick=0.014),
        lay("робочі чоботи", G.boots(0.55), _h(0x201A15), rough=0.46, fab="leather", thick=0.012),
        lay("брудний тейп на кистях", G.hand_wrap(), _h(dirty), rough=0.86, fab="tape", thick=0.004),
        lay("тейп на передпліччях", G.wrist(0.56), _h(dirty), rough=0.86, fab="tape", thick=0.005),
    ]


def look_komet(G):
    gold, deep, white = 0xD4A429, 0x7A5A10, 0xF2E9D2
    z, ax = G.z, G.ax
    top = G.st(G.z_bust + 0.065 - z) * G.st(z - (G.z_bust - 0.095)) * G.not_arm() * (1.0 - G.st(G.head - 0.5, 0.2))
    strap = G.st(0.016 - np.abs(ax - 0.062 - (z - G.z_bust) * 0.15)) * G.st(z - G.z_bust) * G.st(G.torso - 0.12, 0.05)
    t, r, phi = G.side("thigh")
    st_u = G.x / 0.028
    st_v = (z - G.z_hip) / 0.028
    star = G.st(-star_field(np.mod(st_u + 1, 2) - 1, np.mod(st_v + 1, 2) - 1, 5) * 0.028 + 0.012, 0.0015)
    return [
        ink("комета на плечі", tribal_band(G, "upperarm_l", 0.30, 0.08, 1, 4), 0x2A1806, 0.8),
        lay("топ", np.maximum(top, strap), _h(gold), rough=0.24, metal=0.65, fab="lycra", thick=0.004),
        lay("труси", G.trunks(0.04), _h(gold), rough=0.24, metal=0.65, fab="lycra", thick=0.003),
        lay("зірки", G.trunks(0.04) * star, _h(deep), rough=0.3, metal=0.5, fab="lycra", seam=False),
        lay("пояс", G.band(G.z_waist - 0.005, 0.014) * G.not_arm(), _h(deep), rough=0.3, metal=0.6, fab="lycra", thick=0.001),
        lay("наколінники", G.knee_pad(0.062), _h(deep), rough=0.5, fab="neoprene", thick=0.007),
        lay("високі чоботи", G.boots(0.92), _h(gold), rough=0.22, metal=0.55, fab="leather", thick=0.005),
        lay("зірка чобота", G.boots(0.92) * G.st(-star_field((G.ax - 0.11) / 0.03, (z - G.z_ankle - 0.17) / 0.03, 5) * 0.03, 0.002) * G.front, _h(white), rough=0.3, fab="leather", seam=False),
        lay("рукавички", G.glove(0.86), _h(deep), rough=0.4, fab="leather", thick=0.002),
    ]


def look_vdova(G):
    plum, black, web = 0x30202E, 0x121014, 0x8F6B9A
    z, ax = G.z, G.ax
    suit_top = G.st(G.z_neck - 0.012 - z) * G.not_arm() * (1.0 - G.st(G.head - 0.5, 0.2))
    tu, ru, pu = G.side("upperarm")
    tl, rl, pl = G.side("lowerarm")
    sleeve = G.on_arm() * G.st((1.01 - tl) * 0.25) * (1.0 - G.st(G.side("hand")[0] - 0.25, 0.1))
    body = np.maximum(suit_top, sleeve)
    body = np.maximum(body, G.below_waist())
    wm = web_mark(G, 0.0, G.z_hip - 0.02, 0.26) + web_mark(G, 0.10, G.z_bust - 0.02, 0.14)
    return [
        ink("павутина на шиї", web_mark(G, 0.040, G.z_neck + 0.012, 0.050) * G.st(G.A["head"][2] - 0.06 - G.z, 0.004), 0x201528, 0.72),
        lay("комбінезон", body, _h(plum), rough=0.3, fab="lycra", thick=0.0035),
        lay("павутина", body * np.clip(wm, 0, 1), _h(web), rough=0.28, fab="lycra", seam=False, mix=0.55),
        lay("чорні вставки", body * G.st(0.04 - np.abs(np.abs(G.side("thigh")[2]) - 1.57) * G.side("thigh")[1]) * G.on_leg(), _h(black), rough=0.34, fab="lycra", seam=False),
        lay("пояс", G.band(G.z_waist - 0.002, 0.016) * G.not_arm(), _h(black), rough=0.34, fab="leather", thick=0.003),
        lay("наколінники", G.knee_pad(0.062), _h(black), rough=0.6, fab="neoprene", thick=0.007),
        lay("чоботи", G.boots(0.86), _h(black), rough=0.24, fab="leather", thick=0.005),
        lay("рукавички без пальців", G.hand_wrap(), _h(black), rough=0.42, fab="leather", thick=0.002),
    ]


def look_novachka(G):
    blue, white, tape = 0x2F6FD0, 0xD9D6CF, 0xC9C2B0
    z, ax = G.z, G.ax
    top = G.st(G.z_bust + 0.070 - z) * G.st(z - (G.z_bust - 0.105)) * G.not_arm() * (1.0 - G.st(G.head - 0.5, 0.2))
    strap = G.st(0.020 - np.abs(ax - 0.07)) * G.st(z - G.z_bust) * G.st(G.torso - 0.12, 0.05)
    return [
        ink("стрілка на передпліччі", tribal_band(G, "lowerarm_l", 0.42, 0.07, 1, 3), 0x16202E, 0.78),
        lay("топ", np.maximum(top, strap), _h(blue), rough=0.48, fab="knit", thick=0.003),
        lay("смуга топа", G.st(0.008 - np.abs(z - (G.z_bust - 0.092))) * G.st(0.2 - ax) * G.torso, _h(white), rough=0.5, fab="knit", seam=False),
        lay("кросівки", G.boots(0.13, 0.008), _h(white), rough=0.55, fab="canvas", thick=0.006),
        lay("підошва", G.st(G.z_ankle - 0.052 - z) * G.on_leg(), _h(0x2A2A2C), rough=0.72, fab="canvas", seam=False, thick=0.004),
        lay("тейп на щиколотках", G.st(0.04 - np.abs(z - G.z_ankle - 0.09)) * G.on_leg(), _h(tape), rough=0.86, fab="tape", thick=0.004),
        lay("наколінник", G.knee_pad(0.065, side=1), _h(0x20242C), rough=0.62, fab="neoprene", thick=0.008),
        lay("тейп на кистях", G.hand_wrap(), _h(tape), rough=0.86, fab="tape", thick=0.003),
    ]


def look_lina(G):
    white, black = 0xE6E2DA, 0x151518
    z, ax = G.z, G.ax
    blouse = G.st(G.z_neck - 0.02 - z) * G.st(z - G.z_waist + 0.06) * G.not_arm() * (1.0 - G.st(G.head - 0.4, 0.2))
    return [
        lay("блуза", blouse, _h(white), rough=0.6, fab="knit", thick=0.002),
        lay("туфлі", G.st(G.z_ankle + 0.012 - z) * G.on_leg(), _h(black), rough=0.2, fab="leather", thick=0.003),
    ]


def thickness(layers_list):
    """Потовщення вершин: найбільше серед спорядження."""
    t = None
    for L in layers_list:
        if L.get("thick"):
            v = np.clip(L["m"], 0, 1) * L["thick"]
            t = v if t is None else np.maximum(t, v)
    return t if t is not None else 0.0
