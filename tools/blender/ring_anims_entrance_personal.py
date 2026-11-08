# Персональні виходи «Рингу» (docs/ring/ENTRANCE_PROFILE.md; власник 06.11.2026: «Выходы что бы были персонализированные,
# что бы выход зависил от твоих статов + от того какой у тебя гиммик, хил\фейс и тд.»).
# Виконується всередині tools/blender/ring_anims_entrance.py (exec) перед ENTRANCE_CLIPS: додає кліпи бітів 1–4,
# оточення для огляду (кут, бар'єр, підлога біля фартуха) і перевірку канатів, що прогинаються під руками й ногами
# (у грі канати — верле з капсулами тіл, RingRopes.cs: рука чи стопа гне канат сама).
#
# Системи координат — як у наявних кліпах виходу: борець дивиться в −Y, Z угору.
#   * Завіса, хода, бар'єр, пози на маті — на місці, підлога/мат z = 0.
#   * Вхід у ринг (Spread, Vault, Roll_In) — площина канатів y = 0, назовні y > 0, фартух/мат z = 0; канати 0,48/0,86/1,24.
#     Край фартуха — y = +0,5; підлога зали — z = −1,0.
#   * Кутові пози — стійка (перетин канатів) на (0, −0,9), канати від неї назад під 45°.

# ---------------------------------------------------------------- спільне

M = lambda v: Vector((-v.x, v.y, v.z))          # дзеркало лівої руки на праву
H0 = Vector((0.07, -0.03, -0.5))                # кисть уздовж тіла (відносно плеча, × довжина руки)
E0 = Vector((0.24, 0.28, -0.3))                 # полюс ліктя спокою (відносно плеча, м)
STAG = 0.12                                     # стопи завіси/пози — розставлені «для кроку» (як старт циклу ходи)

def arm_keys(p, side, t, keys, anchor=None):
    """keys: [(час, кисть відносно плеча ×рука, полюс ліктя відносно плеча, стиск)] для ЛІВОЇ руки; права — дзеркало.
    anchor(S) — інша точка відліку кисті (напр. лікоть другої руки); тоді вектор кисті — у метрах, без дзеркала."""
    sd = 1 if side == "l" else -1
    mir = (lambda v: v) if sd == 1 else M
    off = track(t, [(k[0], mir(k[1])) for k in keys])
    el = track(t, [(k[0], mir(k[2])) for k in keys])
    fi = track(t, [(k[0], float(k[3])) for k in keys])
    ka = BODY["arm"]
    hands, elbows, fist = dict(p.hands), dict(p.elbows), dict(p.fist)
    if anchor is None:
        hands[side] = (lambda S, s=side, o=off: S[s] + o * ka, DOWN + FWD * 0.15, FWD + (RIGHT if sd == 1 else LEFT) * 0.3)
    else:
        hands[side] = (lambda S, o=off: anchor(S) + o, DOWN + FWD * 0.15, FWD + (RIGHT if sd == 1 else LEFT) * 0.3)
    elbows[side] = lambda S, s=side, e=el: S[s] + e
    fist[side] = fi
    p.hands, p.elbows, p.fist = hands, elbows, fist
    return p

def body_keys(p, t, **tracks):
    """Кути тулуба/голови й зсув таза за ключами: body_keys(p, t, pitch=[(t, v)…], pz=[…], …)."""
    for f, keys in tracks.items():
        v = track(t, keys)
        if f == "pz":
            p.pelvis = p.pelvis + Vector((0, 0, v))
        elif f == "px":
            p.pelvis = p.pelvis + Vector((v, 0, 0))
        elif f == "py":
            p.pelvis = p.pelvis + Vector((0, v, 0))
        else:
            setattr(p, f, v)
    return p

def stand_base(t, L, wx=0.13, stag=STAG, feet_z=(0.0, 0.0)):
    """Стоїть рівно, стопи «для кроку» (ліва попереду на stag/2), руки вздовж тіла, дихає."""
    p = ref_stand(t, L, sway=0.0)
    p.pitch = 1.5 + breathe(t, L, 0.6)
    p.head_pitch = -2.0
    arms_at_side(p)
    legs(p, (Vector((wx, -stag / 2, 0.071 + feet_z[0])), 8.0, 0.0), (Vector((-wx, stag / 2, 0.071 + feet_z[1])), -8.0, 0.0))
    return p

def ends(t, L, a=0.35, b=0.45):
    """Вага жесту: 0 на краях кліпу (стики з ходою), 1 у середині."""
    return bump(t, 0.0, a, L - b, L)

def lift_ankle(a, h):
    return Vector((a.x, a.y, a.z + h))

# ---------------------------------------------------------------- біт 1: завіса

def curtain_monster(t, L):
    """Голова опущена, плечі зібрані — повільно піднімає голову, груди вперед, руки розходяться в «силову» стійку з
    кулаками, плечима — два перекати. Важко, без поспіху."""
    p = stand_base(t, L, wx=0.16)
    body_keys(p, t, pitch=[(0, 1.5), (0.4, 16), (1.1, 16), (2.1, -6), (2.9, -5), (L, 1.5)],
              head_pitch=[(0, -2), (0.4, 30), (1.1, 28), (2.1, -10), (2.9, -8), (L, -2)],
              pz=[(0, 0), (0.4, -0.02), (1.1, -0.03), (2.1, -0.04), (2.9, -0.04), (L, 0)])
    roll_w = bump(t, 2.0, 2.2, 2.75, 2.95)
    p.roll = 5.0 * math.sin(2 * math.pi * (t - 2.0) / 0.45) * roll_w
    p.yaw = 7.0 * math.sin(2 * math.pi * (t - 2.0) / 0.45 + 0.6) * roll_w
    flex = Vector((0.14, -0.24, -0.40))
    keys = [(0, H0, E0, 0.3), (0.4, Vector((0.05, -0.08, -0.5)), E0, 0.7), (1.1, Vector((0.06, -0.08, -0.5)), E0, 0.9),
            (2.1, flex, Vector((0.52, 0.04, -0.10)), 1.0), (2.9, flex, Vector((0.52, 0.04, -0.10)), 1.0), (L, H0, E0, 0.3)]
    arm_keys(p, "l", t, keys)
    arm_keys(p, "r", t, keys)
    return p

FLY_PZ = [(0, 0), (0.42, -0.17), (0.56, 0.0), (0.78, 0.30), (0.98, 0.0), (1.16, -0.15), (1.45, -0.02), (2.6, 0.0)]

def curtain_flyer(t, L):
    """Присід — стрибок з підібганими ногами й руками вгору — приземлення — палець у небо, погляд угору."""
    p = stand_base(t, L, wx=0.13)
    air = 0.56 < t < 0.98
    lift = track(t, [(0, 0), (0.56, 0), (0.78, 0.42), (0.98, 0), (L, 0)]) if air else 0.0
    PLANT_LOG.append((not air, not air))
    al, ar = lift_ankle(p.ankles[0], lift), lift_ankle(p.ankles[1], lift)
    tuck = clamp01(lift / 0.42)
    pz = track(t, FLY_PZ)
    ab = REST["pelvis"] + p.pelvis + Vector((0, 0, pz))
    if not air:
        pz = limit_pelvis(ab.x, ab.y, ab.z, (al, ar)) - REST["pelvis"].z - p.pelvis.z
    p.pelvis = p.pelvis + Vector((0, 0, pz))
    hip = REST["pelvis"] + p.pelvis
    kn = lambda a, sx: (a + Rz(0) @ Vector((0.08 * sx, -0.9, 0.45))).lerp(hip + Vector((0.12 * sx, -0.42, -0.05)), tuck)
    legs(p, (al, 8.0, 0.0), (ar, -8.0, 0.0), knee_l=kn(al, 1), knee_r=kn(ar, -1))
    body_keys(p, t, pitch=[(0, 1.5), (0.42, 22), (0.6, 4), (0.8, 10), (0.98, 6), (1.16, 18), (1.5, -4), (2.2, -4), (L, 1.5)],
              head_pitch=[(0, -2), (0.42, 8), (0.8, -12), (1.16, 4), (1.5, -24), (2.2, -22), (L, -2)])
    up = Vector((0.10, -0.06, 0.56))
    kl = [(0, H0, E0, 0.3), (0.42, Vector((0.10, 0.30, -0.40)), Vector((0.3, 0.4, -0.2)), 0.5), (0.7, up, Vector((0.35, 0.1, 0.2)), 0.6),
          (0.98, up, Vector((0.35, 0.1, 0.2)), 0.6), (1.16, Vector((0.20, -0.30, -0.30)), E0, 0.4), (1.5, Vector((0.40, -0.10, 0.22)), Vector((0.4, 0.2, -0.1)), 0.2),
          (2.2, Vector((0.40, -0.10, 0.22)), Vector((0.4, 0.2, -0.1)), 0.2), (L, H0, E0, 0.3)]
    kr = kl[:5] + [(1.5, Vector((0.02, -0.10, 0.58)), Vector((0.30, 0.15, 0.25)), 0.75), (2.2, Vector((0.02, -0.12, 0.58)), Vector((0.30, 0.15, 0.25)), 0.75), (L, H0, E0, 0.3)]
    arm_keys(p, "l", t, kl)
    arm_keys(p, "r", t, kr)
    return p

def curtain_technician(t, L):
    """Технік: права кисть підтягує налокітник на лівому лікті (два ривки), потім ліва кисть перемотує бинт на правому
    зап'ясті (передпліччя перед грудьми); кивок і хрусь шиєю — готовий до роботи."""
    p = stand_base(t, L, wx=0.12)
    PLANT_LOG.append((True, True))
    body_keys(p, t, pitch=[(0, 1.5), (0.4, 5), (2.3, 5), (2.7, 2), (L, 1.5)],
              head_pitch=[(0, -2), (0.4, 16), (2.2, 14), (2.5, 22), (2.75, -6), (3.0, 2), (L, -2)],
              head_yaw=[(0, 0), (0.4, 14), (1.3, 12), (1.6, -12), (2.3, -10), (2.6, 0), (2.9, 18), (3.1, -10), (L, 0)])
    ka = BODY["arm"]
    w1 = bump(t, 0.2, 0.5, 1.3, 1.55)          # фаза налокітника
    w2 = bump(t, 1.45, 1.75, 2.25, 2.55)       # фаза бинта
    tug = 0.03 * math.sin(2 * math.pi * (t - 0.6) / 0.35) * bump(t, 0.55, 0.65, 1.15, 1.3)
    twist = 0.025 * math.sin(2 * math.pi * (t - 1.75) / 0.25) * bump(t, 1.7, 1.8, 2.15, 2.25)
    l0, r0 = p.hands["l"], p.hands["r"]
    l_bent = Vector((0.08, -0.26, -0.34))      # ліва: лікоть притиснутий збоку, передпліччя вперед
    r_bent = Vector((0.14, -0.22, -0.18))      # права (у дзеркалі): передпліччя перед грудьми, лікоть зігнутий
    def l_hand(S):
        rest_ = l0[0](S)
        bent = S["l"] + l_bent * ka
        wrist_r = S["r"] + M(r_bent) * ka
        return rest_.lerp(bent, w1).lerp(wrist_r + Vector((0.05, -0.02, 0.05 + twist)), w2)
    def r_hand(S):
        rest_ = r0[0](S)
        elbow_l = S["l"] + Vector((0.10, 0.02, -0.27))
        pad = elbow_l + Vector((-0.03, -0.17, 0.03 + tug))
        return rest_.lerp(pad, w1).lerp(S["r"] + M(r_bent) * ka, w2)
    p.hands = {"l": (l_hand, l0[1], l0[2]), "r": (r_hand, r0[1], r0[2])}
    el_side = Vector((0.20, 0.10, -0.32))
    el_out = Vector((0.36, -0.18, -0.18))
    p.elbows = {"l": lambda S: S["l"] + E0.lerp(el_side, w1).lerp(el_out, w2),
                "r": lambda S: S["r"] + M(E0).lerp(M(el_out), w1).lerp(M(el_side), w2)}
    p.fist = {"l": 0.3 + 0.3 * w1 - 0.1 * w2, "r": 0.3 - 0.1 * w1 + 0.4 * w2}
    return p

def curtain_showman(t, L):
    """Руки повільно розходяться вбік до горизонту, долоні до залу, підборіддя вгору — тримає, ведучи погляд по залу
    зліва направо; груди вперед, потім руки опускаються."""
    p = stand_base(t, L, wx=0.15)
    PLANT_LOG.append((True, True))
    w = ends(t, L, 0.9, 0.9)
    body_keys(p, t, pitch=[(0, 1.5), (0.9, -7), (L - 0.9, -7), (L, 1.5)],
              head_pitch=[(0, -2), (0.9, -14), (L - 0.9, -12), (L, -2)],
              pz=[(0, 0), (0.9, 0.01), (L - 0.9, 0.01), (L, 0)])
    p.head_yaw = 32.0 * math.sin(math.pi * (clamp01((t - 0.9) / (L - 1.8)) - 0.5)) * w
    p.yaw = 0.3 * p.head_yaw
    p.roll = -0.1 * p.head_yaw
    wide = Vector((0.34, -0.12, 0.18))
    breathe_a = Vector((0, 0, 0.02 * math.sin(2 * math.pi * t / 1.2)))
    keys = [(0, H0, E0, 0.3), (0.9, wide, Vector((0.40, 0.10, -0.25)), 0.05), (L - 0.9, wide + breathe_a, Vector((0.40, 0.10, -0.25)), 0.05), (L, H0, E0, 0.3)]
    arm_keys(p, "l", t, keys)
    arm_keys(p, "r", t, keys)
    return p

def curtain_face_point(t, L):
    """Фейс: вказує на зал праворуч, потім ліворуч, голова й плечі за рукою, коротке «так!» кулаком."""
    p = stand_base(t, L)
    PLANT_LOG.append((True, True))
    body_keys(p, t, yaw=[(0, 0), (0.55, -14), (0.95, -12), (1.3, 14), (1.75, 12), (2.05, 0), (L, 0)],
              head_yaw=[(0, 0), (0.5, -32), (0.95, -30), (1.3, 32), (1.75, 30), (2.05, 0), (L, 0)],
              head_pitch=[(0, -2), (0.5, -6), (1.75, -6), (2.05, -2), (L, -2)])
    point = Vector((0.34, -0.36, 0.16))
    kr = [(0, H0, E0, 0.3), (0.45, point, Vector((0.4, 0.15, -0.1)), 0.75), (0.95, point, Vector((0.4, 0.15, -0.1)), 0.75), (1.3, H0, E0, 0.3), (L, H0, E0, 0.3)]
    kl = [(0, H0, E0, 0.3), (0.95, H0, E0, 0.3), (1.25, point, Vector((0.4, 0.15, -0.1)), 0.75), (1.75, point, Vector((0.4, 0.15, -0.1)), 0.75),
          (1.95, Vector((0.10, -0.22, -0.10)), Vector((0.35, 0.1, -0.3)), 1.0), (2.1, Vector((0.10, -0.20, -0.18)), Vector((0.35, 0.1, -0.3)), 1.0), (L, H0, E0, 0.3)]
    arm_keys(p, "r", t, kr)
    arm_keys(p, "l", t, kl)
    return p

def curtain_heel_dismiss(t, L):
    """Хіл: руки схрещені на грудях (передпліччя одне над одним), підборіддя задерте, голова відвернута від залу;
    права розплітається й тильним боком долоні відмахується вниз-убік — «не варті»."""
    p = stand_base(t, L, wx=0.15)
    PLANT_LOG.append((True, True))
    body_keys(p, t, head_pitch=[(0, -2), (0.4, -12), (1.4, -12), (1.9, -6), (L, -2)],
              head_yaw=[(0, 0), (0.4, 22), (1.3, 20), (1.6, -18), (2.0, -14), (L, 0)],
              roll=[(0, 0), (0.4, -3), (1.4, -3), (L, 0)], pitch=[(0, 1.5), (0.4, -5), (1.4, -5), (L, 1.5)])
    cross = bump(t, 0.05, 0.4, 1.35, 1.6)
    cross_l = bump(t, 0.05, 0.4, 1.9, 2.3)
    flick = track(t, [(0, 0), (1.4, 0), (1.6, 1.0), (1.85, 1.0), (2.3, 0), (L, 0)])
    sweep = smooth(clamp01((t - 1.6) / 0.18))
    ka = BODY["arm"]
    l0, r0 = p.hands["l"], p.hands["r"]
    def l_hand(S):                                   # ліва — зверху: кисть біля правого біцепса; знімається дугою назовні
        arc = Vector((0.12, -0.10, 0.0)) * (4.0 * cross_l * (1.0 - cross_l)) * (1.0 if t > 1.0 else 0.0)
        return l0[0](S).lerp(S["r"] + Vector((0.03, -0.17, -0.22)), cross_l) + arc
    def r_hand(S):                                   # права — під лівою: кисть біля лівого біцепса; потім відмах
        crossed = S["l"] + Vector((-0.03, -0.22, -0.28))
        w0, w1 = S["r"] + Vector((-0.10, -0.30, -0.26)), S["r"] + Vector((-0.40, -0.12, -0.34))
        out = r0[0](S).lerp(crossed, cross)
        return out.lerp(w0.lerp(w1, sweep), flick)
    p.hands = {"l": (l_hand, l0[1], l0[2]), "r": (r_hand, r0[1], r0[2])}
    el_x = Vector((0.24, -0.30, -0.26))
    p.elbows = {"l": lambda S: S["l"] + E0.lerp(el_x, cross_l),
                "r": lambda S: S["r"] + M(E0).lerp(M(el_x) + Vector((0, -0.02, -0.04)), cross).lerp(Vector((-0.38, 0.05, -0.25)), flick)}
    p.fist = {"l": 0.3 + 0.2 * cross_l, "r": 0.3 + 0.2 * cross - 0.25 * flick}
    return p

# ---------------------------------------------------------------- біт 2: хода

def make_walk(step=0.5, wx=0.11, sway=0.020, yaw=11.0, roll=2.5, pitch=-2.0, head=-3.0, arm=0.30, arm_out=0.0,
              lift=0.10, el_out=0.0, fist=0.3, bob=0.025, bounce=0.0, limp=0.0, head_scan=0.0, crouch=0.0):
    """Цикл ходи (2 кроки), за будовою Entrance_Walk_Loop, з параметрами манери."""
    def clip(t, L):
        half = step / 2
        s_ = step * 2
        path = lambda u: ((0.0, -s_ * u), 0.0)
        fl = Vector((wx, -half, 0.071)); fr = Vector((-wx, half, 0.071))
        kr = [(0.0, 0.0, fr, -6.0), (0.04 * L, 0.0, fr, -6.0), (0.46 * L, 0.5, Vector((-wx, -half, 0.071)), -6.0),
              (L, 0.5, Vector((-wx, -half, 0.071)), -6.0)]
        kl = [(0.0, 0.0, fl, 6.0), (0.54 * L, 0.0, fl, 6.0), (0.96 * L, 1.0, fl, 6.0)]
        sw = math.sin(2 * math.pi * t / L)
        sr = max(0.0, -sw)                                       # права на опорі (друга половина циклу)
        def base(t_, L_):
            p = ref_stand(t_, L_, sway=0.0)
            up = bounce * abs(math.sin(2 * math.pi * t_ / L_))
            vert = -bob * math.cos(4 * math.pi * t_ / L_)               # найвище — на середині опори, найнижче — на двох опорах
            p.pelvis = p.pelvis + Vector((sway * sw, 0, -0.035 - crouch + vert + up - limp * 0.045 * sr))
            p.pitch = pitch + 1.0 * math.sin(4 * math.pi * t_ / L_ + 0.5) + limp * 4.0 * sr
            p.pelvis_yaw = 5.0 * sw * (yaw / 11.0)
            p.yaw = -yaw * sw
            p.roll = -roll * sw - limp * 7.0 * sr
            p.head_pitch = head
            p.head_yaw = -3.0 * sw + head_scan * math.sin(math.pi * t_ / L_)
            arms_at_side(p, arm * sw, -arm * sw, lift=lift)
            ka = BODY["arm"]
            if arm_out:
                hl, hr = p.hands["l"], p.hands["r"]
                p.hands = {"l": (lambda S, g=hl[0]: g(S) + Vector((arm_out, 0, 0)) * ka, hl[1], hl[2]),
                           "r": (lambda S, g=hr[0]: g(S) + Vector((-arm_out, 0, 0)) * ka, hr[1], hr[2])}
            if limp:                                               # права кисть притискає праве стегно
                hr = p.hands["r"]
                thigh = lambda S: S["r"] + Vector((-0.10, -0.10, -0.52)) * ka
                p.hands = {"l": p.hands["l"], "r": (lambda S, g=hr[0]: g(S).lerp(thigh(S), limp), hr[1], hr[2])}
            p.elbows = {"l": lambda S: S["l"] + Vector((0.22 + el_out, 0.30 - 0.15 * max(0, sw), -0.3)),
                        "r": lambda S: S["r"] + Vector((-0.22 - el_out, 0.30 - 0.15 * max(0, -sw), -0.3))}
            p.fist = {"l": fist, "r": fist}
            return p
        PATH[0] = path
        p = locomote(t, L, base, path, kl, kr, lambda t_: t_ / L, bob=0.0)
        p.knees = (p.ankles[0] + Vector((0.05, -0.9, 0.45)), p.ankles[1] + Vector((-0.05, -0.9, 0.45)))
        return p
    return clip

WALKS = [
    # ім'я, довжина циклу, параметри манери
    ("Entrance_Walk_Heavy_Loop", 1.30, dict(step=0.46, wx=0.15, sway=0.045, yaw=14.0, roll=5.0, pitch=4.0, head=4.0, arm=0.18,
                                             arm_out=0.14, lift=0.04, el_out=0.14, fist=0.85, bob=0.025, crouch=0.02)),
    ("Entrance_Walk_Bouncy_Loop", 0.92, dict(step=0.52, wx=0.10, sway=0.012, yaw=8.0, roll=2.0, pitch=-1.0, head=-4.0, arm=0.22,
                                              lift=0.12, fist=0.25, bob=0.0, bounce=0.024, crouch=0.03)),
    ("Entrance_Walk_Focused_Loop", 1.08, dict(step=0.50, wx=0.10, sway=0.015, yaw=6.0, roll=1.5, pitch=3.0, head=9.0, arm=0.12,
                                               lift=0.03, fist=0.55, bob=0.015)),
    ("Entrance_Walk_Strut_Loop", 1.20, dict(step=0.50, wx=0.12, sway=0.035, yaw=17.0, roll=4.5, pitch=-6.0, head=-9.0, arm=0.34,
                                             arm_out=0.06, lift=0.14, el_out=0.06, fist=0.15, bob=0.02, head_scan=18.0)),
    ("Entrance_Walk_Limp_Loop", 1.45, dict(step=0.40, wx=0.12, sway=0.03, yaw=6.0, roll=2.0, pitch=6.0, head=6.0, arm=0.12,
                                            lift=0.02, fist=0.5, bob=0.01, limp=1.0)),
]

# ---------------------------------------------------------------- біт 2: зупинка біля бар'єра (бар'єр ліворуч, +X 0,6 м)

BARRIER_X, BARRIER_Z = 0.62, 1.05

def barrier_high_five(t, L):
    """Фейс: розвертається до бар'єра, ліва рука тягнеться до фанів і б'є три «п'ятірки» вздовж огорожі, усмішка
    головою; права — кулаком угору наприкінці."""
    p = stand_base(t, L, wx=0.15)
    PLANT_LOG.append((True, True))
    w = ends(t, L, 0.45, 0.5)
    body_keys(p, t, yaw=[(0, 0), (0.45, 32), (L - 0.5, 30), (L, 0)], pelvis_yaw=[(0, 0), (0.45, 12), (L - 0.5, 12), (L, 0)],
              roll=[(0, 0), (0.45, 6), (L - 0.5, 6), (L, 0)], head_yaw=[(0, 0), (0.45, 45), (L - 0.5, 40), (L, 0)],
              pitch=[(0, 1.5), (0.45, 8), (L - 0.5, 8), (L, 1.5)])
    taps = [0.75, 1.15, 1.55]
    ys = [-0.10, -0.32, -0.54]
    ty = track(t, [(0, ys[0])] + [(tp, y) for tp, y in zip(taps, ys)] + [(L, ys[-1])])
    tz = sum(-0.10 * bump(t, tp - 0.12, tp - 0.03, tp + 0.02, tp + 0.12) for tp in taps)
    l0 = p.hands["l"]
    def l_hand(S):
        tgt = Vector((BARRIER_X - 0.02, ty, BARRIER_Z + 0.10 + tz))
        return l0[0](S).lerp(tgt, w)
    p.hands = {"l": (l_hand, l0[1], l0[2]), "r": p.hands["r"]}
    p.elbows = {"l": lambda S: S["l"] + E0.lerp(Vector((0.25, 0.25, -0.35)), w), "r": p.elbows["r"]}
    p.fist = {"l": 0.3 - 0.25 * w, "r": 0.3}
    pump = Vector((0.06, -0.10, 0.55))
    arm_keys(p, "r", t, [(0, H0, E0, 0.3), (1.7, H0, E0, 0.3), (1.95, pump, Vector((0.35, 0.1, 0.1)), 1.0), (2.25, pump, Vector((0.35, 0.1, 0.1)), 1.0), (L, H0, E0, 0.3)])
    return p

def barrier_heel_taunt(t, L):
    """Хіл: різко повертається до бар'єра, нахиляється до фанів, тиче пальцем і кричить (голова вперед), тоді
    відмахується й демонстративно відвертається."""
    p = stand_base(t, L, wx=0.16)
    PLANT_LOG.append((True, True))
    body_keys(p, t, yaw=[(0, 0), (0.3, 30), (1.7, 30), (2.1, -8), (L, 0)], pelvis_yaw=[(0, 0), (0.3, 14), (1.7, 14), (L, 0)],
              roll=[(0, 0), (0.3, 8), (1.7, 8), (2.1, -2), (L, 0)], pitch=[(0, 1.5), (0.3, 14), (1.7, 12), (2.1, 0), (L, 1.5)],
              head_yaw=[(0, 0), (0.3, 50), (1.7, 46), (2.05, -30), (L, 0)],
              head_pitch=[(0, -2), (0.3, -8), (0.6, -2), (0.8, -10), (1.0, -2), (1.2, -10), (1.7, -6), (L, -2)])
    jab = sum(0.07 * bump(t, c - 0.08, c, c + 0.02, c + 0.15) for c in (0.62, 0.92, 1.22))
    point = Vector((0.46, -0.40, 0.08))
    arm_keys(p, "l", t, [(0, H0, E0, 0.3), (0.35, point, Vector((0.4, 0.1, -0.15)), 0.8), (1.5, point + Vector((0, -jab, 0)), Vector((0.4, 0.1, -0.15)), 0.8),
                         (1.75, H0, E0, 0.3), (L, H0, E0, 0.3)])
    arm_keys(p, "r", t, [(0, H0, E0, 0.3), (1.5, H0, E0, 0.3), (1.7, Vector((-0.10, -0.34, -0.15)), Vector((0.3, 0.1, -0.25)), 0.1),
                         (1.88, Vector((0.36, -0.28, -0.08)), Vector((0.35, 0.1, -0.2)), 0.1), (2.3, H0, E0, 0.3), (L, H0, E0, 0.3)])
    return p

# ---------------------------------------------------------------- біт 4: пози на маті

def pose_monster_roar(t, L):
    """Центр рингу: широка стійка, нахил уперед, руки в «краба» перед стегнами (усі м'язи), потім подвійний біцепс угору
    й рев — голова закинута назад."""
    p = stand_base(t, L, wx=0.20, stag=0.0)
    PLANT_LOG.append((True, True))
    body_keys(p, t, pitch=[(0, 1.5), (0.5, 22), (1.4, 22), (1.9, -8), (2.8, -8), (L, 1.5)],
              pz=[(0, 0), (0.5, -0.10), (1.4, -0.10), (1.9, -0.05), (2.8, -0.05), (L, 0)],
              head_pitch=[(0, -2), (0.5, -18), (1.4, -16), (1.9, -30), (2.8, -28), (L, -2)])
    shake = 0.012 * math.sin(2 * math.pi * t / 0.12) * (bump(t, 0.6, 0.7, 1.3, 1.4) + bump(t, 2.0, 2.1, 2.7, 2.8))
    crab = Vector((-0.06, -0.26, -0.46 + shake))
    bi = Vector((0.40, 0.02, 0.10 + shake))
    keys = [(0, H0, E0, 0.3), (0.5, crab, Vector((0.50, 0.05, -0.15)), 1.0), (1.4, crab, Vector((0.50, 0.05, -0.15)), 1.0),
            (1.9, bi, Vector((0.48, 0.10, -0.02)), 1.0), (2.8, bi, Vector((0.48, 0.10, -0.02)), 1.0), (L, H0, E0, 0.3)]
    arm_keys(p, "l", t, keys)
    arm_keys(p, "r", t, keys)
    return p

def pose_heel_shush(t, L):
    """Хіл: груди вперед, вказівний палець до губ — «тихо!» — ведучи погляд по залу; друга рука на поясі."""
    p = stand_base(t, L, wx=0.15)
    PLANT_LOG.append((True, True))
    w = ends(t, L, 0.5, 0.5)
    body_keys(p, t, pitch=[(0, 1.5), (0.5, -6), (L - 0.5, -6), (L, 1.5)], head_pitch=[(0, -2), (0.5, -10), (L - 0.5, -10), (L, -2)])
    p.head_yaw = 35.0 * math.sin(math.pi * (clamp01((t - 0.5) / (L - 1.0)) - 0.5)) * w
    p.yaw = 0.35 * p.head_yaw
    r0 = p.hands["r"]
    def r_hand(S):
        mouth = S["head"] + Vector((0.0, -0.10, -0.06))
        return r0[0](S).lerp(mouth + Vector((-0.01, -0.06, -0.13)), w)
    p.hands = {"l": p.hands["l"], "r": (r_hand, r0[1], r0[2])}
    p.elbows = {"l": p.elbows["l"], "r": lambda S: S["r"] + M(E0).lerp(Vector((-0.20, -0.05, -0.30)), w)}
    p.fist = {"l": p.fist["l"], "r": 0.3 + 0.4 * w}
    hip = Vector((0.16, 0.02, -0.40))
    arm_keys(p, "l", t, [(0, H0, E0, 0.3), (0.5, hip, Vector((0.42, 0.10, -0.15)), 0.5), (L - 0.5, hip, Vector((0.42, 0.10, -0.15)), 0.5), (L, H0, E0, 0.3)])
    return p

def pose_heel_mock(t, L):
    """Хіл: долоня за вухом — «не чую вас!» — нахил до залу; потім показне знизування: передпліччя врозліт долонями
    вгору, лікті при тілі, голова набік."""
    p = stand_base(t, L, wx=0.15)
    PLANT_LOG.append((True, True))
    w1 = bump(t, 0.1, 0.45, 1.4, 1.65)
    w2 = bump(t, 1.6, 1.85, L - 0.6, L - 0.1)
    body_keys(p, t, roll=[(0, 0), (0.45, 9), (1.4, 9), (1.8, 0), (2.0, -5), (L - 0.6, -5), (L, 0)],
              head_yaw=[(0, 0), (0.45, -25), (1.4, -25), (1.8, 0), (L, 0)],
              head_pitch=[(0, -2), (0.45, 4), (1.4, 4), (1.9, -4), (L - 0.6, -4), (L, -2)],
              pz=[(0, 0), (1.85, 0), (2.0, 0.015), (L - 0.6, 0.015), (L, 0)])
    shrug = Vector((0.24, -0.20, -0.30))
    el_in = Vector((0.12, 0.10, -0.40))
    keys = [(0, H0, E0, 0.3), (1.6, H0, E0, 0.3), (1.85, shrug, el_in, 0.1), (L - 0.6, shrug, el_in, 0.1), (L - 0.1, H0, E0, 0.3), (L, H0, E0, 0.3)]
    arm_keys(p, "l", t, keys)
    arm_keys(p, "r", t, keys)
    l0, le = p.hands["l"], p.elbows["l"]
    def l_hand(S):                                    # долоня за вухом: зап'ясток нижче вуха, пальці вгору
        ear = S["head"] + Vector((0.08, -0.03, 0.07))
        arc = Vector((0.12, -0.08, 0.0)) * math.sin(math.pi * w1) * (1.0 if t < 1.0 else 0.0)   # вгору дугою повз груди
        return l0[0](S).lerp(ear + Vector((0.06, 0.0, -0.15)), w1) + arc
    p.hands = {"l": (l_hand, l0[1], l0[2]), "r": p.hands["r"]}
    p.elbows = {"l": lambda S: le(S).lerp(S["l"] + Vector((0.36, -0.12, -0.05)), w1), "r": p.elbows["r"]}
    p.fist = {"l": p.fist["l"] - 0.2 * w1, "r": p.fist["r"]}
    return p

def pose_belt_raise(t, L):
    """Чемпіон: пояс на правому плечі — обидві руки підіймають його над головою (кисті на ширині поясу), два
    «качки» вгору, повертає на плече. Реквізит поясу кріпиться до hand_r у грі."""
    p = stand_base(t, L, wx=0.16)
    PLANT_LOG.append((True, True))
    body_keys(p, t, pitch=[(0, 1.5), (0.8, -6), (L - 0.6, -6), (L, 1.5)], head_pitch=[(0, -2), (0.8, -18), (L - 0.6, -16), (L, -2)])
    pump = 0.05 * (bump(t, 1.2, 1.35, 1.4, 1.6) + bump(t, 1.8, 1.95, 2.0, 2.2))
    shoulder_r = Vector((0.06, 0.02, 0.08))
    over = Vector((-0.12, -0.06, 0.56 + pump))
    kr = [(0, shoulder_r, Vector((0.30, -0.10, -0.25)), 0.85), (0.25, shoulder_r, Vector((0.30, -0.10, -0.25)), 0.85),
          (0.8, over, Vector((0.35, 0.05, 0.15)), 0.85), (L - 0.6, over, Vector((0.35, 0.05, 0.15)), 0.85), (L, shoulder_r, Vector((0.30, -0.10, -0.25)), 0.85)]
    kl = [(0, H0, E0, 0.3), (0.3, H0, E0, 0.3), (0.8, over, Vector((0.35, 0.05, 0.15)), 0.85), (L - 0.6, over, Vector((0.35, 0.05, 0.15)), 0.85), (L, H0, E0, 0.3)]
    arm_keys(p, "r", t, kr)
    arm_keys(p, "l", t, kl)
    return p

# технік: обличчям до канатів (площина канатів — на 0,9 м попереду), руки на верхньому канаті, розтягує литки й спину
STRETCH_ROPE_Y = -0.62

def pose_technician_stretch(t, L):
    """Технік біля канатів: руки на верхній канат, крок назад правою — розтяжка литки, корпус униз — спина, повтор
    лівою; шия по колу. Акуратно, без показухи."""
    p = stand_base(t, L, wx=0.13, stag=0.0)
    hold = bump(t, 0.1, 0.55, L - 0.7, L - 0.2)
    back_r = bump(t, 0.6, 0.95, 1.9, 2.2)
    back_l = bump(t, 2.1, 2.4, 3.4, 3.7)
    al, ar = p.ankles
    al = Vector((al.x, al.y + 0.42 * back_l, al.z)); ar = Vector((ar.x, ar.y + 0.42 * back_r, ar.z))
    moving = lambda w: 0.02 < w < 0.98 and (0.6 < t < 0.95 or 1.9 < t < 2.4 or 3.4 < t < 3.7)
    lift_l = 0.05 * sinp((t - 2.06) / 0.38) if 2.06 < t < 2.44 else (0.05 * sinp((t - 3.36) / 0.38) if 3.36 < t < 3.74 else 0.0)
    lift_r = 0.05 * sinp((t - 0.56) / 0.43) if 0.56 < t < 0.99 else (0.05 * sinp((t - 1.86) / 0.38) if 1.86 < t < 2.24 else 0.0)
    PLANT_LOG.append((lift_l == 0.0, lift_r == 0.0))
    legs(p, (lift_ankle(al, lift_l), 8.0, 0.0), (lift_ankle(ar, lift_r), -8.0, 0.0))
    lean = max(back_r, back_l)
    body_keys(p, t, pitch=[(0, 1.5), (0.55, 18), (L - 0.7, 18), (L, 1.5)], head_pitch=[(0, -2), (0.55, 4), (L - 0.7, 4), (L, -2)])
    p.pitch += 16.0 * lean
    p.pelvis = p.pelvis + Vector((0, 0.10 * lean, -0.05 * lean))
    neck = bump(t, 3.7, 3.9, L - 0.7, L - 0.4)
    p.head_yaw = 25.0 * math.sin(2 * math.pi * (t - 3.7) / 0.9) * neck
    l0, r0 = p.hands["l"], p.hands["r"]
    def on_rope(g, x):
        return lambda S: g(S).lerp(Vector((x, STRETCH_ROPE_Y + 0.03, ROPE_Z[2] + 0.04)), hold)
    p.hands = {"l": (on_rope(l0[0], 0.26), l0[1], l0[2]), "r": (on_rope(r0[0], -0.26), r0[1], r0[2])}
    p.elbows = {"l": lambda S: S["l"] + E0.lerp(Vector((0.35, 0.05, -0.35)), hold), "r": lambda S: S["r"] + M(E0).lerp(Vector((-0.35, 0.05, -0.35)), hold)}
    p.fist = {"l": 0.3 + 0.4 * hold, "r": 0.3 + 0.4 * hold}
    pz = limit_pelvis((REST["pelvis"] + p.pelvis).x, (REST["pelvis"] + p.pelvis).y, (REST["pelvis"] + p.pelvis).z, p.ankles)
    p.pelvis.z = pz - REST["pelvis"].z
    return p

# ---------------------------------------------------------------- біт 4: на другому канаті в куті

POST = Vector((0.0, -0.9, 0.0))                 # перетин канатів кута (кутова стійка)
DIAG = (Vector((0.7071, 0.7071, 0.0)), Vector((-0.7071, 0.7071, 0.0)))     # канати від стійки: ліворуч-назад, праворуч-назад

def rope_pt(side, d, rz):
    return POST + DIAG[0 if side == "l" else 1] * d + Vector((0, 0, rz))

TB_FOOT1 = {s: rope_pt(s, 0.40, ROPE_Z[0] + ROPE_R + 0.055) for s in "lr"}       # щиколотка на нижньому канаті
TB_FOOT2 = {s: rope_pt(s, 0.32, ROPE_Z[1] + ROPE_R + 0.055) for s in "lr"}       # на середньому
TB_GRIP = {s: rope_pt(s, 0.42, ROPE_Z[2] + 0.03) for s in "lr"}                  # кисть на верхньому
TB_START = {"l": Vector((0.13, -0.22, 0.071)), "r": Vector((-0.13, -0.22, 0.071))}
TB_LAND = {"l": Vector((0.14, -0.28, 0.071)), "r": Vector((-0.14, -0.28, 0.071))}

def turnbuckle(kind):
    """Обличчям до кута: руки на верхні канати, ліва-права на нижній, ліва-права на середній — стоїть над кутом, гомілки
    впираються у верхній канат; руки відпускає й піднімає (V — шоумен/фейс, палець у небо — флаєр), тримає; руки
    назад на канат, зістрибує назад на мат з присіду."""
    T = dict(grip=0.55, f1l=(0.75, 1.15), f1r=(1.15, 1.55), f2l=(1.6, 2.0), f2r=(2.0, 2.4), up=(2.55, 2.95), down=(4.25, 4.55), hop=(4.7, 5.0))
    def clip(t, L):
        def foot(s):
            o = (T["f1l"], T["f2l"]) if s == "l" else (T["f1r"], T["f2r"])
            ev = [(o[0][0], o[0][1], TB_FOOT1[s], 8.0 if s == "l" else -8.0, 0.08),
                  (o[1][0], o[1][1], TB_FOOT2[s], 20.0 if s == "l" else -20.0, 0.10),
                  (T["hop"][0], T["hop"][1], TB_LAND[s], 8.0 if s == "l" else -8.0, 0.06)]
            return Seq(TB_START[s], 8.0 if s == "l" else -8.0, ev).at(t)
        fl, yl, pl, _ = foot("l")
        fr, yr, pr_, _ = foot("r")
        PLANT_LOG.append((pl, pr_))
        mid = lambda: (fl + fr) * 0.5
        # таз: над стопами, трохи позаду кута; висота — з ніг (коліна злегка зігнуті), на піку пози — випрямлений
        on2 = smooth(clamp01((t - T["f2l"][0]) / (T["f2r"][1] - T["f2l"][0])))
        on1 = smooth(clamp01((t - T["f1l"][0]) / (T["f1r"][1] - T["f1l"][0])))
        land = smooth(clamp01((t - T["hop"][0]) / (T["hop"][1] - T["hop"][0])))
        y = track(t, [(0, -0.22), (T["grip"], -0.26), (T["f1r"][1], -0.40), (T["f2r"][1], -0.50), (T["down"][1], -0.50), (T["hop"][1], -0.30), (L, -0.30)])
        zr = track(t, [(0, 0.913), (T["grip"], 0.90), (T["f1l"][1], 1.05), (T["f1r"][1], 1.24), (T["f2l"][1], 1.40), (T["f2r"][1], 1.72),
                       (T["up"][1], 1.80), (T["down"][0], 1.80), (T["down"][1], 1.66), (T["hop"][0] + 0.05, 1.60), (T["hop"][1], 0.66),
                       (T["hop"][1] + 0.35, 0.78), (L, 0.913)])
        pz = limit_pelvis(0.0, y, zr, (fl, fr), margin=0.97)
        p = RPose(forced=set())
        p.pelvis = Vector((0.0, y, pz)) - REST["pelvis"]
        p.pitch = track(t, [(0, 2.0), (T["grip"], 10.0), (T["f2r"][1], 16.0), (T["up"][1], -4.0), (T["down"][0], -4.0), (T["down"][1], 14.0),
                            (T["hop"][1], 24.0), (T["hop"][1] + 0.35, 10.0), (L, 2.0)])
        p.head_pitch = track(t, [(0, -2), (T["f2r"][1], 4), (T["up"][1], -16), (T["down"][0], -14), (T["down"][1], 6), (L, -2)])
        hold = bump(t, T["up"][0], T["up"][1], T["down"][0], T["down"][1])
        p.head_yaw = 28.0 * math.sin(2 * math.pi * (t - T["up"][1]) / 1.3) * hold if kind == "arms" else 0.0
        hipw = Vector((0.0, y, pz))
        kl = fl + Vector((0.06, -0.35, 0.30)) if on1 > 0.0 or land < 1.0 else None
        kr = fr + Vector((-0.06, -0.35, 0.30)) if on1 > 0.0 or land < 1.0 else None
        legs(p, (fl, yl, 0.0), (fr, yr, 0.0), knee_l=kl, knee_r=kr)
        # руки: на верхній канат біля стійки → угору в позу → назад на канат → зістрибує
        grip = bump(t, 0.15, T["grip"], T["up"][0], T["up"][1]) + bump(t, T["down"][0], T["down"][1], T["hop"][0], T["hop"][0] + 0.15)
        ka = BODY["arm"]
        if kind == "arms":
            up_l, up_r = Vector((0.30, -0.10, 0.48)), M(Vector((0.30, -0.10, 0.48)))
            fl_, fr_ = 0.05, 0.05
        else:
            up_l, up_r = Vector((0.24, -0.30, 0.02)), Vector((-0.02, -0.10, 0.58))
            fl_, fr_ = 0.3, 0.8
        pump = 0.03 * math.sin(2 * math.pi * (t - T["up"][1]) / 0.65) * hold
        def hand(s, upv):
            def g(S):
                rest_ = S[s] + (H0 if s == "l" else M(H0)) * ka
                tgt = rest_.lerp(TB_GRIP[s], clamp01(grip))
                return tgt.lerp(S[s] + (upv + Vector((0, 0, pump))) * ka, hold)
            return g
        p.hands = {"l": (hand("l", up_l), DOWN, FWD), "r": (hand("r", up_r), DOWN, FWD)}
        el_g, el_u = Vector((0.36, 0.10, -0.25)), Vector((0.40, 0.10, 0.10))
        p.elbows = {"l": lambda S: S["l"] + E0.lerp(el_g, clamp01(grip)).lerp(el_u, hold),
                    "r": lambda S: S["r"] + M(E0).lerp(M(el_g), clamp01(grip)).lerp(M(el_u), hold)}
        p.fist = {"l": 0.3 + 0.5 * clamp01(grip) * (1 - hold) + (fl_ - 0.3) * hold, "r": 0.3 + 0.5 * clamp01(grip) * (1 - hold) + (fr_ - 0.3) * hold}
        return p
    return clip

# ---------------------------------------------------------------- біт 3: флаєр — перемах над верхнім канатом

VAULT = dict(grip=(0.15, 0.45), dip=(0.45, 0.7), off=0.7, over=1.02, land=1.38, rise=(1.55, 2.3))

def clip_vault(t, L):
    """Руки на верхній канат, присід, поштовх — коліна до грудей, ноги проходять між руками над канатом, таз — над
    канатом на ~1,45 м, приземлення в ринг у м'який присід, випрямлення з руками вгору."""
    V = VAULT
    y = track(t, [(0, 0.32), (V["dip"][1], 0.28), (V["off"] + 0.12, 0.16), (V["over"], -0.02), (V["land"], -0.52), (L, -0.58)])
    z = track(t, [(0, 0.913), (V["dip"][0], 0.90), (V["dip"][1], 0.78), (V["off"] + 0.12, 1.12), (V["off"] + 0.22, 1.42), (V["over"], 1.62), (V["land"] - 0.12, 1.02),
                  (V["land"], 0.80), (V["land"] + 0.17, 0.62), (V["rise"][1], 0.913), (L, 0.913)])
    air = V["off"] + 0.03 < t < V["land"]
    tuck = bump(t, V["off"] + 0.22, V["off"] + 0.32, V["over"] + 0.08, V["land"] - 0.05)
    st_l, st_r = Vector((0.13, 0.32, 0.071)), Vector((-0.13, 0.32, 0.071))
    la_l, la_r = Vector((0.15, -0.60, 0.071)), Vector((-0.15, -0.60, 0.071))
    hip = Vector((0.0, y, z))
    tucked = lambda sx: hip + Vector((0.10 * sx, 0.0, -0.16))           # п'яти під сідницями, коліна вперед
    s_land = smooth(clamp01((t - V["over"]) / (V["land"] - V["over"])))
    def ankle(st, la, sx):
        if t <= V["off"] + 0.03:
            return st
        if t >= V["land"]:
            return la
        hang = hip + Vector((0.12 * sx, 0.12, -0.80))                     # спершу ноги звисають під тазом (коліна за канатом)
        a = st.lerp(hang, smooth(clamp01((t - V["off"]) / 0.15)))
        a = a.lerp(tucked(sx), smooth(clamp01((t - V["off"] - 0.22) / 0.10)))
        return a.lerp(la, s_land)
    al, ar = ankle(st_l, la_l, 1), ankle(st_r, la_r, -1)
    PLANT_LOG.append((not air, not air))
    if not air:
        z = limit_pelvis(0.0, y, z, (al, ar), margin=0.985)
    p = RPose(forced=set())
    p.pelvis = Vector((0.0, y, z)) - REST["pelvis"]
    p.pitch = track(t, [(0, 3.0), (V["grip"][1], 12.0), (V["dip"][1], 24.0), (V["off"] + 0.12, 34.0), (V["over"], 30.0), (V["land"], 14.0),
                        (V["land"] + 0.17, 22.0), (V["rise"][1], -6.0), (L, 2.0)])
    p.head_pitch = track(t, [(0, -4), (V["dip"][1], 6), (V["over"], 10), (V["land"], 0), (V["rise"][1], -18), (L, -6)])
    fwd = smooth(clamp01((0.10 - y) / 0.25))                              # над канатом — коліна виходять уперед
    kn = lambda a, sx: (a + Vector((0.08 * sx, -0.9, 0.45))).lerp(hip + Vector((0.11 * sx, -0.06 - 0.32 * fwd, 0.22 - 0.16 * fwd)), tuck)
    legs(p, (al, 8.0, 0.0), (ar, -8.0, 0.0), knee_l=kn(al, 1), knee_r=kn(ar, -1))
    ka = BODY["arm"]
    # руки відштовхуються від каната на злеті (таз іде вище, ніж дістає рука) і в польоті йдуть уперед-угору для рівноваги
    grip = bump(t, V["grip"][0], V["grip"][1], V["off"] + 0.08, V["off"] + 0.2)
    fly = bump(t, V["off"] + 0.08, V["off"] + 0.22, V["land"], V["land"] + 0.2)
    def hand(s, x):
        sd = 1.0 if s == "l" else -1.0
        def g(S):
            rest_ = S[s] + (H0 if s == "l" else M(H0)) * ka
            air_ = S[s] + Vector((0.26 * sd, -0.30, 0.10)) * ka
            return rest_.lerp(Vector((x, 0.0, ROPE_Z[2] + ROPE_R + 0.03)), grip).lerp(air_, fly)
        return g
    up = bump(t, V["rise"][0], V["rise"][1], L - 0.25, L + 1.0)
    p.hands = {"l": (lambda S, g=hand("l", 0.34): g(S).lerp(S["l"] + Vector((0.22, -0.06, 0.44)) * ka, up), DOWN, FWD),
               "r": (lambda S, g=hand("r", -0.34): g(S).lerp(S["r"] + Vector((-0.22, -0.06, 0.44)) * ka, up), DOWN, FWD)}
    p.elbows = {"l": lambda S: S["l"] + E0.lerp(Vector((0.42, 0.20, 0.05)), grip).lerp(Vector((0.40, 0.10, 0.12)), up),
                "r": lambda S: S["r"] + M(E0).lerp(Vector((-0.42, 0.20, 0.05)), grip).lerp(Vector((-0.40, 0.10, 0.12)), up)}
    p.fist = {"l": 0.3 + 0.55 * grip - 0.2 * up, "r": 0.3 + 0.55 * grip - 0.2 * up}
    return p

# ---------------------------------------------------------------- біт 3: закочується з підлоги під нижній канат

# Система координат Roll_In — як у підйому сходами: підлога зали z = 0, фартух і мат z = 1,0 (генератор тримає стопи
# не нижче нуля); площина канатів y = 0, край фартуха y = +0,5.
TOP = 1.0
APRON_EDGE = 0.5
ROLL = dict(reach=(0.15, 0.55), dip=(0.55, 0.8), hop=(0.8, 1.3), slide=(1.3, 2.4), knees=(2.4, 3.0), up=(3.0, 3.8))

def up3(p, d):
    """Зсув пози (таз, стопи, коліна) на вектор d."""
    p.pelvis = p.pelvis + d
    if p.ankles:
        p.ankles = (p.ankles[0] + d, p.ankles[1] + d)
    if p.knees:
        p.knees = (p.knees[0] + d, p.knees[1] + d)
    return p

def clip_roll_in(t, L):
    """З підлоги біля фартуха: долоні на фартух, присід, підскок — грудьми на фартух, ковзає під нижнім канатом у ринг,
    відтискається на коліна й встає. Без сходів — швидко й низько (хіл, спритні)."""
    R = ROLL
    st_l, st_r = Vector((0.13, 0.86, 0.071)), Vector((-0.13, 0.86, 0.071))
    stand_p = REST["pelvis"] + Vector((0, 0.86, 0.0))
    ka = BODY["arm"]
    on_apron = {"l": Vector((0.24, 0.36, TOP + 0.035)), "r": Vector((-0.24, 0.36, TOP + 0.035))}
    if t <= R["hop"][0]:
        dip = bump(t, R["dip"][0], R["dip"][1], R["dip"][1], R["hop"][0] + 0.02)
        p = RPose(forced=set())
        pel = stand_p + Vector((0, -0.03 * smooth(clamp01(t / R["reach"][1])), -0.12 * dip))
        p.pelvis = pel - REST["pelvis"]
        p.pitch = track(t, [(0, 2.0), (R["reach"][1], 24.0), (R["dip"][1], 30.0), (R["hop"][0], 28.0)])
        p.head_pitch = track(t, [(0, -2.0), (R["reach"][1], 4.0), (R["hop"][0], -6.0)])
        legs(p, (st_l, 8.0, 0.0), (st_r, -8.0, 0.0))
        reach = smooth(clamp01((t - R["reach"][0]) / (R["reach"][1] - R["reach"][0])))
        p.hands = {s: (lambda S, s=s: (S[s] + (H0 if s == "l" else M(H0)) * ka).lerp(on_apron[s], reach), DOWN, FWD) for s in "lr"}
        p.elbows = {"l": lambda S: S["l"] + Vector((0.32, 0.20, -0.25)), "r": lambda S: S["r"] + Vector((-0.32, 0.20, -0.25))}
        p.fist = {"l": 0.15, "r": 0.15}
        PLANT_LOG.append((True, True))
        return p
    if t <= R["knees"][0]:
        # тіло цілком: кут th (0 — стоїть, 90 — на животі); таз — над краєм фартуха, далі ковзає під канатом
        th = track(t, [(R["hop"][0], 0.0), (R["hop"][0] + 0.22, 55.0), (R["hop"][1], 90.0), (R["knees"][0], 90.0)])
        y = track(t, [(R["hop"][0], 0.83), (R["hop"][0] + 0.22, 0.86), (R["hop"][1], 0.80), (R["slide"][1], -1.05), (R["knees"][0], -1.05)])
        z = track(t, [(R["hop"][0], stand_p.z - 0.12), (R["hop"][0] + 0.22, TOP + 0.10), (R["hop"][1], TOP + FRONT_Z + 0.02), (R["knees"][0], TOP + FRONT_Z + 0.02)])
        q = Quaternion(LEFT, math.radians(th))
        p = body(off_for(Vector((0.0, y, z))), q, knee_bend=0.10 * (1.0 - th / 90.0) + 0.05)
        p.pelvis_q = q
        p.pitch = track(t, [(R["hop"][0], 28.0), (R["hop"][1], 0.0), (R["knees"][0], 0.0)])
        # стопи відриваються плавно; за краєм фартуха ноги звисають (коліна гнуться вниз), а не висять у повітрі
        k0 = smooth(clamp01((t - R["hop"][0]) / 0.16))
        ank, kns = list(p.ankles), list(p.knees)
        settle = clamp01((t - R["hop"][1] + 0.12) / 0.15)                   # тіло вже лягло на фартух
        for i, st in enumerate((st_l, st_r)):
            a = st.lerp(ank[i], k0) + Vector((0, 0, 0.08 * math.sin(math.pi * k0)))
            if settle > 0.0:
                sx = 1.0 if i == 0 else -1.0
                hip_y = y + 0.08
                knee_y = hip_y + BODY["thigh"]
                if knee_y > APRON_EDGE - 0.02:                              # коліно на краю — гомілка звисає
                    kn = Vector((0.11 * sx, min(knee_y, APRON_EDGE + 0.04), TOP + 0.07))
                    hang = Vector((0.11 * sx, kn.y + 0.10, kn.z - BODY["calf"] * 0.96))
                    a = a.lerp(hang, settle)
                    kns[i] = kn + Vector((0, 0.30, 0.05))                   # коліно — назовні за край, не вниз у фартух
            ank[i] = a
        p.ankles, p.knees = tuple(ank), tuple(kns)
        sl = clamp01((t - R["slide"][0]) / (R["slide"][1] - R["slide"][0]))
        def hand(s, sd, ph):
            def g(S):
                if t <= R["hop"][1]:
                    return on_apron[s]
                stroke = 0.5 - 0.5 * math.cos(2 * math.pi * (sl * 2.0 + ph))
                pt = S[s] + q @ (Vector((0.12 * sd, 0.0, 0.30)) * ka) + Vector((0, -0.10 * stroke, 0.0))
                return Vector((pt.x, pt.y, TOP + 0.035 + 0.05 * math.sin(math.pi * stroke)))
            return g
        p.hands = {"l": (hand("l", 1, 0.0), q @ UP, DOWN), "r": (hand("r", -1, 0.5), q @ UP, DOWN)}
        p.elbows = {"l": lambda S: S["l"] + q @ Vector((0.4, 0.0, 0.0)) + Vector((0, 0, 0.1)),
                    "r": lambda S: S["r"] + q @ Vector((-0.4, 0.0, 0.0)) + Vector((0, 0, 0.1))}
        p.fist = {"l": 0.15, "r": 0.15}
        p.head_yaw = 0.0
        p.head_pitch = -4.0
        PLANT_LOG.append((False, False))
        return p
    # на коліна й встає (з «на животі, на руках» через коліна — як рефері підводиться з колін)
    lift = Vector((0, 0, TOP))
    if t <= R["knees"][1]:
        k = smooth((t - R["knees"][0]) / (R["knees"][1] - R["knees"][0]))
        a_ = up3(_filled(lying_front(y=-1.05, push=1.0, head_side=0.0)), lift)
        b_ = up3(_filled(_shift(ref_kneel(t, L), -0.62)), lift)
        PLANT_LOG.append((False, False))
        p = blend_pose(a_, b_, k)
        return up3(p, Vector((0, 0, 0.10 * math.sin(math.pi * k))))     # коліна підтягуються над матом, а не крізь нього
    k = 1.0 - smooth(clamp01((t - R["up"][0]) / (R["up"][1] - R["up"][0])))
    PLANT_LOG.append((k < 0.02, k < 0.02))
    return up3(_shift(kneel_blend(t, L, k), -0.62), lift)

# ---------------------------------------------------------------- канати, що гнуться: перевірка

def straight_ropes(dz=0.0):
    return [[Vector((x, 0.0, rz + dz)) for x in [(-1.4 + 2.8 * i / 40) for i in range(41)]] for rz in ROPE_Z]

def corner_ropes():
    out = []
    for rz in ROPE_Z:
        for d in DIAG:
            out.append([POST + d * (1.8 * i / 30) + Vector((0, 0, rz)) for i in range(31)])
    return out

PRESS_SPAN = 1.5        # м: прогин каната спадає від точки тиску до нуля на цій відстані вздовж каната

def deform(rope, pressers):
    """Канат-полілінія під капсулами-«тиснями» (кисті, стопи, гомілки): кожна точка зсувається, щоб не заходити в
    капсулу; зсув розходиться вздовж каната трикутником (як верле-канат гри в спокої)."""
    n = len(rope)
    seglen = (rope[1] - rope[0]).length
    disp = [Vector() for _ in range(n)]
    for a, b, r in pressers:
        best_i, best_v = None, None
        for i, pt in enumerate(rope):
            ab = b - a
            u = clamp01((pt - a).dot(ab) / max(ab.dot(ab), 1e-9))
            c = a + ab * u
            d = pt - c
            pen = r + ROPE_R - d.length
            if pen > 0 and (best_v is None or pen > best_v.length):
                dirn = d.normalized() if d.length > 1e-6 else UP
                best_i, best_v = i, dirn * pen
        if best_i is None:
            continue
        for i in range(n):
            w = max(0.0, 1.0 - abs(i - best_i) * seglen / PRESS_SPAN)
            v = best_v * w
            if v.length > disp[i].length:
                disp[i] = v
    return [pt + d for pt, d in zip(rope, disp)]

def rope_pen_dyn(wa, ropes, press_bones, limb_allow=0.0):
    """Найглибший захід тулуба/голови й кінцівок у канати, що прогнулися під press_bones (ті з перевірки виключені)."""
    core, limb, cw, lw, sag = 0.0, 0.0, "", "", 0.0
    for fi, (_, w) in enumerate(wa):
        pressers = [(w[n][0], w[n][1], RADIUS.get(n, 0.05)) for n in press_bones]
        for ri, rope in enumerate(ropes):
            rr = deform(rope, pressers)
            sag = max(sag, max((a - b).length for a, b in zip(rr, rope)))
            for j in range(len(rr) - 1):
                a, b = rr[j], rr[j + 1]
                for n, r in CORE.items():
                    v = r + ROPE_R - seg_dist(w[n][0], w[n][1], a, b)
                    if v > core:
                        core, cw = v, "%s × канат %d, кадр %d" % (n, ri + 1, fi)
                for n, r in LIMB.items():
                    if n in press_bones:
                        continue
                    v = r + ROPE_R - seg_dist(w[n][0], w[n][1], a, b) - limb_allow
                    if v > limb:
                        limb, lw = v, "%s × канат %d, кадр %d" % (n, ri + 1, fi)
    return core * 100.0, cw, limb * 100.0, lw, sag * 100.0

def apron_pen(wa):
    """Підлога зали й фартух (Roll_In): точки ніг і тулуба не нижче опори під собою (фартух z 1,0 при y ≤ 0,5, далі
    підлога 0; бокова стінка фартуха — y = 0,5 між 0 і 1,0)."""
    worst, where = 0.0, ""
    for fi, (_, w) in enumerate(wa):
        for n, r in list(CORE.items()) + [("thigh_l", 0.06), ("thigh_r", 0.06), ("calf_l", 0.045), ("calf_r", 0.045)]:
            for pt in (w[n][0], w[n][1]):
                floor = TOP if pt.y <= APRON_EDGE else 0.0
                v = floor - (pt.z - r)
                if pt.y > APRON_EDGE and pt.y < APRON_EDGE + r and pt.z < TOP:     # у стінці фартуха
                    v = max(v, APRON_EDGE + r - pt.y)
                if v > worst:
                    worst, where = v, "%s, кадр %d" % (n, fi)
    return worst * 100.0, where

def barrier_pen(wa):
    """Бар'єр (x = BARRIER_X, верх 1,05): тулуб і руки не заходять за площину огорожі нижче її верху."""
    worst, where = 0.0, ""
    for fi, (_, w) in enumerate(wa):
        for n in ("spine_03", "head", "lowerarm_l", "hand_l", "upperarm_l"):
            for pt in (w[n][0], w[n][1]):
                if pt.z < BARRIER_Z:
                    v = pt.x + RADIUS.get(n, 0.05) - BARRIER_X
                    if v > worst:
                        worst, where = v, "%s, кадр %d" % (n, fi)
    return worst * 100.0, where

ENV_CHECKS = {
    "vault": lambda wa: rope_pen_dyn(wa, straight_ropes(), ("hand_l", "hand_r", "lowerarm_l", "lowerarm_r", "thigh_l", "thigh_r")),
    "rollin": lambda wa: rope_pen_dyn(wa, straight_ropes(TOP), ()),
    "corner": lambda wa: rope_pen_dyn(wa, corner_ropes(), ("foot_l", "foot_r", "calf_l", "calf_r", "thigh_l", "thigh_r", "hand_l", "hand_r")),
    "stretch": lambda wa: rope_pen_dyn([(c, {n: (v[0] - Vector((0, STRETCH_ROPE_Y, 0)), v[1] - Vector((0, STRETCH_ROPE_Y, 0))) for n, v in w.items()}) for c, w in wa],
                                       straight_ropes(), ("hand_l", "hand_r", "lowerarm_l", "lowerarm_r")),
}

# ---------------------------------------------------------------- оточення для огляду

_make_env_base = make_env

def make_env(kind):
    objs = []
    def box(c, size, color):
        bpy.ops.mesh.primitive_cube_add(size=1.0, location=c)
        o = bpy.context.active_object
        o.scale = size
        o.color = color
        objs.append(o)
    def cyl(a, b, r, color):
        d = b - a
        bpy.ops.mesh.primitive_cylinder_add(radius=r, depth=d.length, location=(a + b) * 0.5)
        o = bpy.context.active_object
        o.rotation_mode = 'QUATERNION'
        o.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(d.normalized())
        o.color = color
        objs.append(o)
    red, mat, skirt = (0.9, 0.2, 0.2, 1), (0.2, 0.3, 0.7, 1), (0.12, 0.12, 0.14, 1)
    if kind in ("vault", "rollin"):
        dz = TOP if kind == "rollin" else 0.0
        box((0, -1.2, dz - 0.08), (2.8, 2.4, 0.16), mat)
        box((0, 0.25, dz - 0.08), (2.8, 0.5, 0.16), mat)
        for rz in ROPE_Z:
            cyl(Vector((-1.4, 0, rz + dz)), Vector((1.4, 0, rz + dz)), ROPE_R, red)
        if kind == "rollin":
            box((0, -0.5, 0.42), (2.8, 2.0, 0.84), skirt)
            box((0, 1.2, -0.02), (2.8, 1.4, 0.04), (0.35, 0.35, 0.38, 1))
        return objs
    if kind == "stretch":
        box((0, -0.4, -0.08), (2.8, 2.4, 0.16), mat)
        for rz in ROPE_Z:
            cyl(Vector((-1.4, STRETCH_ROPE_Y, rz)), Vector((1.4, STRETCH_ROPE_Y, rz)), ROPE_R, red)
        return objs
    if kind == "corner":
        box((0, 0.2, -0.08), (2.6, 2.6, 0.16), mat)
        cyl(POST + Vector((0, 0, -0.1)), POST + Vector((0, 0, 1.45)), 0.05, (0.75, 0.75, 0.78, 1))
        for rope in corner_ropes():
            cyl(rope[0], rope[-1], ROPE_R, red)
        return objs
    if kind == "barrier":
        box((0, 0, -0.02), (2.0, 2.4, 0.04), (0.35, 0.35, 0.38, 1))
        box((BARRIER_X + 0.03, -0.3, BARRIER_Z / 2), (0.06, 2.4, BARRIER_Z), (0.75, 0.75, 0.2, 1))
        return objs
    return _make_env_base(kind)

# ---------------------------------------------------------------- досяжність кистей

def reach_clamp(fn, lim=0.47):
    """Ціль кисті не далі lim × довжини руки від плеча: інакше IK тягне руку в струну й «недотягує»."""
    def g(t, L):
        p = fn(t, L)
        ka = BODY["arm"]
        hs = {}
        for s_, (f_, a_, b_) in p.hands.items():
            def h(S, f_=f_, s_=s_):
                v = (f_(S) if callable(f_) else f_) - S[s_]
                m = lim * ka
                pt = S[s_] + (v.normalized() * m if v.length > m else v)
                # кисть не в тулубі: вісь — від лінії плечей (на 8 см нижче) униз уздовж хребта на 0,55 м, радіус 0,21 м
                mid = (S["l"] + S["r"]) * 0.5
                down = (S["chest"] - mid).normalized()
                top = mid + down * 0.08
                u = (pt - top).dot(down)
                if 0.0 <= u <= 0.55:
                    c = top + down * u
                    d = pt - c
                    if d.length < 0.21:
                        dirn = d.normalized() if d.length > 1e-4 else FWD
                        pt = c + dirn * 0.21
                return pt
            hs[s_] = (h, a_, b_)
        p.hands = hs
        return p
    return g

# ---------------------------------------------------------------- список

PERSONAL_CLIPS = [
    ("Entrance_Curtain_Monster", 3.4, curtain_monster, False, None, 2.4),
    ("Entrance_Curtain_Flyer", 2.6, curtain_flyer, False, None, 2.8),
    ("Entrance_Curtain_Technician", 3.4, curtain_technician, False, None, 2.4),
    ("Entrance_Curtain_Showman", 3.6, curtain_showman, False, None, 2.6),
    ("Entrance_Curtain_Face_Point", 2.4, curtain_face_point, False, None, 2.4),
    ("Entrance_Curtain_Heel_Dismiss", 2.8, curtain_heel_dismiss, False, None, 2.4),
] + [(n, L_, make_walk(**kw), True, None, 2.4) for n, L_, kw in WALKS] + [
    ("Entrance_Barrier_High_Five", 2.6, barrier_high_five, False, "barrier", 2.6),
    ("Entrance_Barrier_Heel_Taunt", 2.6, barrier_heel_taunt, False, "barrier", 2.6),
    ("Entrance_Vault_Over_Top", 2.6, clip_vault, False, "vault", 3.2),
    ("Entrance_Roll_In", 3.75, clip_roll_in, False, "rollin", 3.4),
    ("Entrance_Pose_Turnbuckle_Arms", 5.6, turnbuckle("arms"), False, "corner", 3.4),
    ("Entrance_Pose_Turnbuckle_Point", 5.6, turnbuckle("point"), False, "corner", 3.4),
    ("Entrance_Pose_Monster_Roar", 3.2, pose_monster_roar, False, None, 2.4),
    ("Entrance_Pose_Technician_Stretch", 4.6, pose_technician_stretch, False, "stretch", 3.0),
    ("Entrance_Pose_Heel_Shush", 3.0, pose_heel_shush, False, None, 2.4),
    ("Entrance_Pose_Heel_Mock", 3.2, pose_heel_mock, False, None, 2.4),
    ("Entrance_Pose_Belt_Raise", 3.0, pose_belt_raise, False, None, 2.6),
]

PERSONAL_CLIPS = [(n, L_, reach_clamp(f_), lp, k_, o_) for n, L_, f_, lp, k_, o_ in PERSONAL_CLIPS]
