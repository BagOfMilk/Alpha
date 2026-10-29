# Чейн (обмін захватами після локапу) і дії по центру після відскоку — по 15 (власник 29.10.2026: «локап, чейн,
# біг від канатів з діями по центру ринга … Усіх варіант по 15»). Виконується всередині ring_anims.py (exec).
#
# Кожен кліп — ключові пози (поза-до-пози) для обох борців: таз, нахил, поворот, стопи, і для кожної кисті — ціль на
# тілі суперника (кістка + частка + зсув) або «вартова» позиція. Між ключами — сплайн (track), стопи — кроками (steps),
# кисті переходять між цілями плавно. Будова прийомів — з навчальних матеріалів і записів матчів (своїми словами):
#   захват голови збоку — рука обвиває шию, друга замикає зап'ястя; викрут руки — обидві кисті на зап'ясті й лікті
#   суперника, його рука випрямлена; хаммерлок — рука суперника зігнута за спиною, кисть тисне на зап'ясті;
#   кидок через стегно — стегно під таз суперника, рука за спину, ривок; підсічка/захоплення ноги — пірнає під ногу.

GUARD_L, GUARD_R = Vector((-0.07, -0.27, -0.2)), Vector((0.07, -0.27, -0.2))

class K:
    """Ключова поза одного борця. pel — зсув таза (x, вперед(+), z) від стійки; feet — (x, вперед) лівої й правої;
    hands — {"l"/"r": None (вартова) | (кістка суперника, частка, (зсув x,y,z у просторі свого ригу))}."""
    def __init__(self, pel=(0, 0, 0), pitch=16.0, yaw=0.0, roll=0.0, head=(0.0, -8.0), feet=None, hands=None,
                 fist=(0.3, 0.3), pyaw=0.0):
        self.pel, self.pitch, self.yaw, self.roll, self.head = pel, pitch, yaw, roll, head
        self.feet = feet or ((0.2, 0.105), (-0.2, -0.075))
        self.hands = hands or {}
        self.fist, self.pyaw = fist, pyaw

def _interp(t, keys, get):
    return track(t, [(tt, get(k)) for tt, k in keys])

def keyed_pose(t, L, keys):
    """keys: [(час, K)]. Повертає RPose з контактами на суперника."""
    p = stance(t, L, sway=0.0)
    px = _interp(t, keys, lambda k: k.pel[0]); py = _interp(t, keys, lambda k: k.pel[1])
    pz = _interp(t, keys, lambda k: k.pel[2])
    p.pelvis = p.pelvis + Vector((px, -py, pz))
    p.pitch = _interp(t, keys, lambda k: k.pitch)
    p.yaw = _interp(t, keys, lambda k: k.yaw)
    p.roll = _interp(t, keys, lambda k: k.roll)
    p.pelvis_yaw = _interp(t, keys, lambda k: k.pyaw)
    p.head_yaw = _interp(t, keys, lambda k: k.head[0]) + p.pelvis_yaw
    p.head_pitch = _interp(t, keys, lambda k: k.head[1])
    # стопи — кроки між ключовими положеннями (steps дає відрив і дугу); час кроку — навколо ключа
    def fkeys(i):
        out = []
        for j, (tt, k) in enumerate(keys):
            xy = (k.feet[i][0], -k.feet[i][1])
            if j == 0:
                out.append((tt, xy))
            else:
                prev_t = keys[j - 1][0]
                start = prev_t + (tt - prev_t) * (0.35 if i == 0 else 0.5)
                out.append((start, out[-1][1])); out.append((tt, xy))
        return out
    p.ankles = steps(t, fkeys(0), fkeys(1))
    p.forced = set()
    # кисті: між двома ключами — змішування цілей (кожна ціль — точка на суперникові або вартова)
    def target(k, s):
        h = k.hands.get(s)
        if h is None:
            g = GUARD_L if s == "l" else GUARD_R
            return lambda S: S[s] + g
        bone, along, off = h
        return lambda S: (P(S, bone, along, Vector(off)) or (S[s] + (GUARD_L if s == "l" else GUARD_R)))
    def hand(s):
        def g(S):
            if t <= keys[0][0]:
                return target(keys[0][1], s)(S)
            for (t0, k0), (t1, k1) in zip(keys, keys[1:]):
                if t <= t1:
                    w = smooth((t - t0) / (t1 - t0)) if t1 > t0 else 1.0
                    a, b = target(k0, s)(S), target(k1, s)(S)
                    arc = Vector((0.12 if s == "l" else -0.12, 0.0, 0.08)) * math.sin(math.pi * w) if (a - b).length > 0.2 else Vector()
                    return a.lerp(b, w) + arc
            return target(keys[-1][1], s)(S)
        return g
    held = lambda s: any(k.hands.get(s) for _, k in keys)
    p.hands = {s: (hand(s), FWD + DOWN * 0.3, (RIGHT if s == "l" else LEFT) + UP * 0.3) for s in "lr"}
    p.elbows = {"l": lambda S: S["l"] + Vector((0.3, 0.05, -0.35)), "r": lambda S: S["r"] + Vector((-0.3, 0.05, -0.35))}
    p.fist = {"l": _interp(t, keys, lambda k: k.fist[0]), "r": _interp(t, keys, lambda k: k.fist[1])}
    return p

ST_A = K()
ST_B = K()
TIE = dict(pel=(0.17, 0.3, -0.12), pitch=34.0, roll=-4.0, yaw=-10.0, head=(-38.0, 0.0), feet=((0.3, 0.42), (-0.18, 0.2)))   # як лабораторний зчеп: голова повз плече
def tie_k(**kw):
    d = dict(TIE); d.update(kw)
    return K(**d)
TIE_HANDS = {"l": ("neck_01", 0.5, (0.0, -0.05, 0.0)), "r": ("lowerarm_l", 0.05, (0.0, 0.0, -0.04))}

# Ланки чейну (захват голови, викрут руки, …) будуються в лабораторії за картами з референсів
# (tools/blender/ring_chain_lab.py, moves/Chain_*.json) і приходять кліпами з lab_clips/. Перші чернетки поза-до-пози,
# зроблені тут без референсу, власник відхилив 29.10.2026 («голова за спиною», «частини тіла проходять крізь») — зняті.
CHAIN = {}

def _chain_fns(name):
    L, ka, kb = CHAIN[name]
    return (lambda t, L_, partner=None: keyed_pose(t, L_, ka)), (lambda t, L_, partner=None: keyed_pose(t, L_, kb))

for _n, (_L, _ka, _kb) in CHAIN.items():
    _fa, _fb = _chain_fns(_n)
    MOVE_CLIPS.append((_n, _L, _fa, _fb, False, Vector((0, -1.18, 0)), 180))   # як у локапу: кожен у хваті на 0,3 м уперед
    MOVE_CLIP_NODES[_n] = {"A": (None, None), "B": (None, None)}
