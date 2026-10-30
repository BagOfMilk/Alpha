# Рефері: ще дев'ять реакцій (план ітерації 1: «рефері — різноманітніші реакції»). Виконується всередині ring_anims.py
# (exec) після ring_anims_center.py. Сигнали рефері (навчальні матеріали й записи матчів, своїми словами):
#   розвести борців — долоні між ними, руки розходяться в боки; канати — рахунок до п'яти пальцями піднятої руки й
#   вказівка на канати; «не зараховано» — руки схрещуються перед поясом і розлітаються в боки; відсахнутися, коли
#   борці летять поруч; відступити з піднятими руками; «ти як?» — нахилився, руки на колінах; падіння рефері після
#   випадкового удару (бамп на спину). Усі — від стійки рефері до стійки (крім падіння — у «лежить на спині»).


def _ref_arms(p, k, lt, rt, lf=None, rf=None, le=None, re=None):
    """Руки рефері: змішати від стійки до цілей lt/rt (функції S → точка) з вагою k (число або функція часу)."""
    st, el = p.hands, p.elbows
    lf = lf or (FWD + UP * 0.3)
    rf = rf or (FWD + UP * 0.3)
    p.hands = {"l": (lambda S: lerp(st["l"][0](S), lt(S), k), DOWN.lerp(lf, k), FWD.lerp(RIGHT, k)),
               "r": (lambda S: lerp(st["r"][0](S), rt(S), k), DOWN.lerp(rf, k), FWD.lerp(LEFT, k))}
    p.elbows = {"l": (lambda S: el["l"](S).lerp(S["l"] + (le or Vector((0.35, 0.05, -0.2))), k)),
                "r": (lambda S: el["r"](S).lerp(S["r"] + (re or Vector((-0.35, 0.05, -0.2))), k))}
    return p


def clip_ref_separate(t, L):
    # долоні між борцями (перед грудьми), руки розходяться в боки — «розійдіться»
    p = ref_stand(t, L, sway=0.0)
    k = track(t, [(0.0, 0.0), (0.3, 1.0), (1.3, 1.0), (1.7, 0.0)])
    o = smooth((t - 0.55) / 0.4) * (1 - smooth((t - 1.2) / 0.3)) if 0.55 < t < 1.5 else 0.0
    p.pitch = 4.0 + 8.0 * k
    p.head_pitch = -8.0
    _ref_arms(p, k, lambda S: S["l"] + Vector((-0.1 + 0.42 * o, -0.42 + 0.12 * o, -0.05)),
              lambda S: S["r"] + Vector((0.1 - 0.42 * o, -0.42 + 0.12 * o, -0.05)), FWD + UP * 0.8, FWD + UP * 0.8)
    p.fist = {"l": 0.05, "r": 0.05}
    return p


def clip_ref_rope_count(t, L):
    # рахунок до п'яти на канатах: права рука вгору, пальці розгинаються по одному (кисть «відкривається» щосекунди),
    # ліва вказує на канати; голова — до борців
    p = ref_stand(t, L, sway=0.0)
    k = track(t, [(0.0, 0.0), (0.3, 1.0), (L - 0.4, 1.0), (L, 0.0)])
    n = min(5.0, max(0.0, (t - 0.4) / 0.55))
    beat = 0.04 * math.sin(math.pi * (n % 1.0)) * k             # кисть смикається на кожному рахунку
    p.head_yaw = 25.0 * k
    _ref_arms(p, k, lambda S: S["l"] + Vector((0.45, -0.25, -0.15)),
              lambda S: S["r"] + Vector((-0.08, -0.12, 0.42 + beat)), FWD + LEFT * 0.8, UP + FWD * 0.2,
              le=Vector((0.4, 0.1, -0.15)), re=Vector((-0.35, 0.1, 0.0)))
    p.fist = {"l": 0.2, "r": 0.9 - 0.18 * n * k}                   # пальці розгинаються з рахунком
    return p


def clip_ref_point_ropes(t, L):
    # права рука витягнута до канатів, крок у той бік; голова — на борців: «канати!»
    p = ref_stand(t, L, sway=0.0)
    k = track(t, [(0.0, 0.0), (0.25, 1.0), (1.1, 1.0), (1.5, 0.0)])
    p.yaw = -18.0 * k
    p.head_yaw = 20.0 * k
    _ref_arms(p, k, lambda S: S["l"] + Vector((0.06, -0.03, -0.52)),
              lambda S: S["r"] + Vector((-0.55, -0.25, 0.05)), DOWN + FWD * 0.15, RIGHT + FWD * 0.3,
              re=Vector((-0.3, 0.2, -0.25)))
    p.fist = {"l": 0.3, "r": 0.6}
    return p


def clip_ref_waive_off(t, L):
    # «не зараховано»: руки схрещуються перед поясом і розлітаються в боки, двічі; голова хитається «ні»
    p = ref_stand(t, L, sway=0.0)
    k = track(t, [(0.0, 0.0), (0.25, 1.0), (1.55, 1.0), (1.95, 0.0)])
    x = 0.5 - 0.5 * math.cos(2 * math.pi * (t - 0.25) / 0.65) if 0.25 < t < 1.55 else 0.0
    p.head_yaw = 18.0 * math.sin(2 * math.pi * t / 0.5) * k
    _ref_arms(p, k, lambda S: S["l"] + Vector((-0.22 + 0.62 * x, -0.32 + 0.06 * x, -0.32 + 0.12 * x)),
              lambda S: S["r"] + Vector((0.22 - 0.62 * x, -0.32 + 0.06 * x, -0.32 + 0.12 * x)), FWD + DOWN * 0.3, FWD + DOWN * 0.3)
    p.fist = {"l": 0.05, "r": 0.05}
    return p


def clip_ref_flinch(t, L):
    # відсахнувся: крок назад правою, руки до грудей долонями вперед, корпус відхилився, голова відвернулась
    p = ref_stand(t, L, sway=0.0)
    k = track(t, [(0.0, 0.0), (0.12, 1.0), (0.7, 1.0), (1.2, 0.0)])
    p.ankles = (REF_FOOT["l"].copy(), feet_track(t, [(0.0, REF_FOOT["r"].copy()), (0.08, REF_FOOT["r"].copy()),
                                                        (0.3, REF_FOOT["r"] + Vector((0.0, 0.22, 0.0))),
                                                        (0.8, REF_FOOT["r"] + Vector((0.0, 0.22, 0.0))),
                                                        (1.1, REF_FOOT["r"].copy())])[0])
    p.pelvis = p.pelvis + Vector((0.0, 0.1 * k, -0.04 * k))
    p.pitch = 4.0 - 10.0 * k
    p.head_yaw = 30.0 * k
    p.head_pitch = -2.0 + 10.0 * k
    _ref_arms(p, k, lambda S: S["l"] + Vector((-0.08, -0.25, -0.12)), lambda S: S["r"] + Vector((0.08, -0.25, -0.12)),
              UP + FWD * 0.3, UP + FWD * 0.3)
    p.fist = {"l": 0.1, "r": 0.1}
    return p


def clip_ref_back_up(t, L):
    # два кроки назад з піднятими руками (долоні вперед) — «я нічого не бачив / розійдіться»
    p = ref_stand(t, L, sway=0.0)
    k = track(t, [(0.0, 0.0), (0.25, 1.0), (1.4, 1.0), (1.8, 0.0)])
    back = 0.28
    p.ankles = (feet_track(t, [(0.0, REF_FOOT["l"].copy()), (0.3, REF_FOOT["l"].copy()), (0.6, REF_FOOT["l"] + Vector((0, back, 0))),
                                (2.0, REF_FOOT["l"] + Vector((0, back, 0)))])[0],
                feet_track(t, [(0.0, REF_FOOT["r"].copy()), (0.65, REF_FOOT["r"].copy()), (0.95, REF_FOOT["r"] + Vector((0, back, 0))),
                               (2.0, REF_FOOT["r"] + Vector((0, back, 0)))])[0])
    mv = track(t, [(0.0, 0.0), (0.3, 0.0), (0.95, 1.0), (2.0, 1.0)])
    p.pelvis = p.pelvis + Vector((0.0, back * mv, 0.0))
    p.head_yaw = 12.0 * math.sin(2 * math.pi * t / 1.2) * k
    _ref_arms(p, k, lambda S: S["l"] + Vector((0.12, -0.2, 0.25)), lambda S: S["r"] + Vector((-0.12, -0.2, 0.25)),
              UP + FWD * 0.2, UP + FWD * 0.2, le=Vector((0.35, 0.0, -0.1)), re=Vector((-0.35, 0.0, -0.1)))
    p.fist = {"l": 0.05, "r": 0.05}
    return p


def clip_ref_ask_ok(t, L):
    # «ти як?»: нахилився вперед, руки на колінах, голова набік; киває
    p = ref_stand(t, L, sway=0.0)
    k = track(t, [(0.0, 0.0), (0.4, 1.0), (1.6, 1.0), (2.0, 0.0)])
    p.pelvis = p.pelvis + Vector((0.0, 0.08 * k, -0.12 * k))
    p.pitch = 4.0 + 38.0 * k
    p.head_pitch = -2.0 - 22.0 * k + 6.0 * math.sin(2 * math.pi * t / 0.6) * k
    p.head_yaw = 10.0 * k
    _ref_arms(p, k, lambda S: S["pelvis"] + Vector((0.14, -0.26, -0.35)), lambda S: S["pelvis"] + Vector((-0.14, -0.26, -0.35)),
              DOWN + FWD * 0.3, DOWN + FWD * 0.3, le=Vector((0.3, 0.1, -0.3)), re=Vector((-0.3, 0.1, -0.3)))
    p.fist = {"l": 0.3, "r": 0.3}
    return p


def clip_ref_admonish(t, L):
    # обидві долоні вперед, крок до борців, долоні тиснуть двічі — «назад!»
    p = ref_stand(t, L, sway=0.0)
    k = track(t, [(0.0, 0.0), (0.25, 1.0), (1.4, 1.0), (1.8, 0.0)])
    push = 0.06 * max(0.0, math.sin(2 * math.pi * (t - 0.3) / 0.5)) if 0.3 < t < 1.3 else 0.0
    p.ankles = (feet_track(t, [(0.0, REF_FOOT["l"].copy()), (0.2, REF_FOOT["l"].copy()), (0.45, REF_FOOT["l"] + Vector((0, -0.2, 0))),
                                (1.3, REF_FOOT["l"] + Vector((0, -0.2, 0))), (1.6, REF_FOOT["l"].copy())])[0], REF_FOOT["r"].copy())
    fw = track(t, [(0.0, 0.0), (0.2, 0.0), (0.45, 1.0), (1.3, 1.0), (1.6, 0.0)])
    p.pelvis = p.pelvis + Vector((0.0, -0.08 * fw, -0.02 * fw))
    p.pitch = 4.0 + 8.0 * k
    _ref_arms(p, k, lambda S: S["l"] + Vector((-0.02, -0.45 - push, 0.08)), lambda S: S["r"] + Vector((0.02, -0.45 - push, 0.08)),
              UP + FWD * 0.2, UP + FWD * 0.2)
    p.fist = {"l": 0.05, "r": 0.05}
    return p


def clip_ref_knocked_down(t, L):
    # випадковий удар: рефері падає на спину (той самий бамп), лежить «без тями» до кінця кліпу
    if t < 0.08:
        return ref_stand(t, L, sway=0.0)
    w = smooth((t - 0.08) / 0.15)
    tb = t - 0.08
    b = clip_bump_back(min(BUMP_LEN, tb), BUMP_LEN)
    u = smooth((tb - 0.28) / 0.25)                            # стопи рефері (вужчі) стоять, доки не піднімуться над матом
    if u < 1.0 and b.ankles:
        def keep(ref, a_):
            m = lerp(ref, a_, u)
            return Vector((m.x, m.y, a_.z))                    # висота — з бампа: убік стопа йде вже в повітрі
        b.ankles = (keep(REF_FOOT["l"], b.ankles[0]), keep(REF_FOOT["r"], b.ankles[1]))
    if w < 1.0:
        return blend_pose(_filled(ref_stand(t, L, sway=0.0)), _filled(b), w)
    if t > BUMP_LEN + 0.08:                                    # лежить «без тями»: руки розкинуті, голова набік
        k = smooth((t - BUMP_LEN - 0.08) / 0.5)
        b.head_yaw = 40.0 * k
    return b


REF_MORE = [("Ref_Separate", 1.8, clip_ref_separate, "ref_stand"), ("Ref_Rope_Count", 3.4, clip_ref_rope_count, "ref_stand"),
            ("Ref_Point_Ropes", 1.6, clip_ref_point_ropes, "ref_stand"), ("Ref_Waive_Off", 2.0, clip_ref_waive_off, "ref_stand"),
            ("Ref_Flinch", 1.3, clip_ref_flinch, "ref_stand"), ("Ref_Back_Up", 1.9, clip_ref_back_up, "ref_stand"),
            ("Ref_Ask_OK", 2.1, clip_ref_ask_ok, "ref_stand"), ("Ref_Admonish", 1.9, clip_ref_admonish, "ref_stand"),
            ("Ref_Knocked_Down", 2.6, clip_ref_knocked_down, "lying_back")]
for _n, _L, _f, _end in REF_MORE:
    MOVE_CLIPS.append((_n, _L, _f, None, False, None, 0))
    MOVE_CLIP_NODES[_n] = {"A": ("ref_stand", _end)}
