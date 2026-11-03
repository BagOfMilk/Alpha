# Кліпи «промо з мікрофоном» для «Рингу» (docs/ring/gdd.md §8): Promo_Mic_Talk_Loop і Promo_Mic_Point — один борець,
# той самий скелет набору, що в 187 кліпах RING_Moves*.fbx, ті самі IK-хелпери й звіт якості.
#
#   "C:\Program Files\Blender Foundation\Blender 4.5\blender.exe" -b --python tools/blender/ring_anims_promo.py -- <репо> [--female] [--look]
#
# Не змінює tools/blender/ring_anims.py: той файл підвантажується як текст без останнього рядка «main()» (так само, як
# ring_anims.py підвантажує alpha_anims.py), після чого тут перевизначаються шляхи виводу й запуск.
# Виходи (окремо від RING_Moves*.fbx, їх не чіпаємо):
#   Assets/Art/Animations/RING_Promo.fbx (+ _F для жіночого), ring_promo[_f]_manifest.txt, Logs/ring_promo[_f]/*.png, report.md
# Вузол «promo» (promo → promo): ліва рука опущена, права тримає мікрофон біля підборіддя, ноги ширше за бійцівську стійку.
# Мікрофон кріпиться до hand_r: зсув і поворот (простір кістки Blender, Y уздовж кістки) — рядком «# attach» у маніфесті.
import os, sys

_RA = open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "ring_anims.py"), encoding="utf-8").read()
exec(_RA.rsplit("\nmain()", 1)[0])             # усі хелпери, RPose, Rig, CLIPS, quality … без запуску main()

OUT_FBX = os.path.join(REPO, "Assets", "Art", "Animations", "RING_Promo%s.fbx" % SFX)
MANIFEST = os.path.join(REPO, "Assets", "Art", "Animations", "ring_promo%s_manifest.txt" % SFX.lower())
LOOK_DIR = os.path.join(REPO, "Logs", "ring_promo" + SFX.lower())

# ---------------------------------------------------------------- пальці: вказівний можна випрямити

_apply_w_base = apply_w

def apply_w(rig, p, partner=None):
    """apply_w() рингу + p.finger_open = {сторона: [пальці]}: названі пальці випрямляються (вказування). Пальці не входять
    у ланцюги IK, тож поворот після розв'язання нічого не ламає."""
    _apply_w_base(rig, p, partner)
    opened = getattr(p, "finger_open", None)
    if opened:
        pb = rig.arm.pose.bones
        for s, fingers in opened.items():
            for f in fingers:
                for k in ("01", "02", "03"):
                    n = "%s_%s_%s" % (f, k, s)
                    if n in pb:
                        q = pb[n].rotation_quaternion
                        pb[n].rotation_quaternion = Quaternion().slerp(q, 0.06)
        bpy.context.view_layer.update()

# ---------------------------------------------------------------- мікрофон: де він і як лежить у руці

MIC_LEN_BELOW, MIC_LEN_ABOVE = 0.065, 0.125       # від центру рукояті (Mic_Grip) до торця і до верху решітки (м)
MIC_A = Vector((0.06, 0.70, 0.71)).normalized()    # вісь мікрофона (низ → верх): угору й до обличчя, ~45° від вертикалі
MIC_LOG = []                                       # (G, a) на кожен кадр — для зсуву кріплення й перевірки

def mic_plan(S, head_yaw, dist):
    """G — центр рукояті, a — вісь. Верх решітки — перед підборіддям, нижче рота; обертається разом з головою."""
    R = Rz(head_yaw)
    mouth = S["head"] + R @ Vector((0.0, -0.09, 0.04))
    T = mouth + R @ Vector((0.01, -dist, -0.06 * (1.0 - 0.3 * (dist - 0.065) / 0.03)))
    a = R @ MIC_A
    return T - a * MIC_LEN_ABOVE, a

def mic_hand(head_yaw_fn, dist_fn):
    def grip(S):
        G, a = mic_plan(S, head_yaw_fn(), dist_fn())
        MIC_LOG.append((G.copy(), a.copy()))
        return G
    def up(S):
        return mic_plan(S, head_yaw_fn(), dist_fn())[1]
    return (grip, Vector((0.9, -0.25, 0.1)), up)

# ---------------------------------------------------------------- рамка кліпів

TALK_LEN, POINT_LEN = 4.0, 2.5
PF_L, PF_R = Vector((0.16, -0.035, 0.071)), Vector((-0.16, 0.035, 0.071))
PF_R2 = Vector((-0.25, 0.015, 0.071))
YL, YR, YR2 = 14.0, -16.0, -22.0

def bump(t, a, b, c, d):
    """0 до a, плавно до 1 на [a, b], тримає до c, плавно до 0 на [c, d]."""
    if t <= a or t >= d:
        return 0.0
    if t < b:
        return smooth((t - a) / (b - a))
    if t <= c:
        return 1.0
    return 1.0 - smooth((t - c) / (d - c))

def key_track(t, keys, idx):
    """Ключі (час, поле0, поле1 …) → значення поля idx (Vector або число) монотонним сплайном."""
    return track(t, [(k[0], k[idx]) for k in keys])

def sc(v):
    """Цілі рук — під руки набору (жіночі коротші)."""
    return v * BODY["arm"]

# Ліва рука: (час, точка відносно плеча, напрям передпліччя, бік великого пальця, кулак)
REST_L = (Vector((0.06, -0.03, -0.52)), DOWN + FWD * 0.15, FWD + RIGHT * 0.3)
def L_KEYS():
    r = REST_L
    return [
        (0.0, r[0], r[1], r[2], 0.3),
        (0.30, Vector((0.07, -0.10, -0.46)), DOWN + FWD * 0.3, FWD + RIGHT * 0.3, 0.3),
        (0.75, Vector((0.14, -0.32, -0.17)), FWD + DOWN * 0.1, LEFT * 0.45 + UP * 0.9, 0.1),   # відкрита долоня вгору (на півоберта)
        (1.15, Vector((0.14, -0.35, -0.14)), FWD + DOWN * 0.05, LEFT * 0.45 + UP * 0.9, 0.1),
        (1.55, Vector((0.09, -0.22, -0.40)), DOWN + FWD * 0.5, FWD + RIGHT * 0.3, 0.25),
        (1.90, Vector((0.01, -0.33, -0.04)), FWD, UP, 0.15),
        (2.15, Vector((0.01, -0.31, -0.25)), FWD + DOWN * 0.4, UP, 0.15),             # рубить долонею вниз
        (2.42, Vector((0.01, -0.33, -0.04)), FWD, UP, 0.15),
        (2.68, Vector((0.01, -0.31, -0.25)), FWD + DOWN * 0.4, UP, 0.15),
        (3.15, Vector((0.38, -0.25, -0.14)), FWD + LEFT * 0.8, RIGHT, 0.1),            # широкий жест убік, долоня вниз
        (3.55, Vector((0.18, -0.12, -0.36)), DOWN + FWD * 0.4, FWD + RIGHT * 0.3, 0.25),
        (TALK_LEN, r[0], r[1], r[2], 0.3),
    ]
L_TALK = L_KEYS()

def promo_feet(t, L):
    """Права стопа виносить на 9 см убік і повертається (переступ); ліва — опора. Вага — на лівій, поки права в русі."""
    kl = [(0.0, ("w", PF_L, YL), Vector(), 0.0), (L, ("w", PF_L, YL), Vector(), 0.0)]
    kr = [(0.0, ("w", PF_R, YR), Vector(), 0.0), (1.30, ("w", PF_R, YR), Vector(), 0.0),
          (1.72, ("w", PF_R2, YR2), Vector(), 0.0), (2.90, ("w", PF_R2, YR2), Vector(), 0.0),
          (3.32, ("w", PF_R, YR), Vector(), 0.0), (L, ("w", PF_R, YR), Vector(), 0.0)]
    return foot_plan(t, kl), foot_plan(t, kr)

def weight_left(t):
    return max(bump(t, 0.85, 1.25, 1.80, 2.2), bump(t, 2.6, 2.95, 3.40, 3.8))

def promo_base(t, L, tf=None):
    """Спільна поза «у себе». t — час руху, tf — час стоп (для переступу; у Point — 0, стопи стоять)."""
    tf = t if tf is None else tf
    w = weight_left(tf)
    p = RPose(forced={"l", "r"})
    fl, fr = promo_feet(tf, TALK_LEN)
    p.pelvis = Vector((0.04 * w - 0.005, 0.0, -0.035 - 0.012 * w))
    p.roll = -3.0 * w
    p.pitch = 3.0 + breathe(t, L, 0.9)
    p.head_pitch = -1.0
    set_feet(p, fl, fr)
    return p

def talk_pose(t, L):
    p = promo_base(t, L)
    ph = 2 * math.pi * t / L
    p.yaw = 5.0 * math.sin(ph)
    p.head_yaw = 16.0 * math.sin(ph) + 4.0 * math.sin(2 * ph)
    p.head_pitch = -1.0 + 2.5 * math.sin(2 * ph + 1.0) - 2.5 * math.sin(1.0)
    p.pitch += 1.5 * math.sin(2 * ph)
    hy = p.head_yaw
    dist = 0.082 + 0.014 * math.sin(2 * ph + 0.6) - 0.014 * math.sin(0.6)           # мікрофон наближається й віддаляється
    p.hands["r"] = mic_hand(lambda: hy, lambda: dist)
    # ліва рука — жести
    pos, fo, up = key_track(t, L_TALK, 1), key_track(t, L_TALK, 2), key_track(t, L_TALK, 3)
    fist = key_track(t, L_TALK, 4)
    p.hands["l"] = ((lambda S, pos=pos: S["l"] + Vector((pos.x * BODY["arm"], pos.y * BODY["arm"], pos.z * BODY["arm"]))), fo, up)
    spread = 0.5 * (1 - (pos.z + 0.52) / 0.5)
    p.elbows = {"l": lambda S: S["l"] + Vector((0.34, 0.08 - 0.22 * max(0.0, min(1.0, spread)), -0.32)),
                "r": lambda S: S["r"] + Vector((-0.22, 0.12, -0.34))}
    p.fist = {"l": fist, "r": 0.85}
    return p

def point_pose(t, L):
    ph = 2 * math.pi * t / TALK_LEN
    p = talk_pose(0.0, TALK_LEN)                                          # старт і фініш — рівно вузол «promo»
    e = bump(t, 0.15, 0.72, 1.62, 2.30)
    stab = bump(t, 0.95, 1.05, 1.08, 1.2) + bump(t, 1.30, 1.40, 1.43, 1.55)        # два короткі «тичка» вперед
    p.pelvis = p.pelvis + Vector((0.0, -0.045 * e, -0.01 * e))
    p.pitch = p.pitch + 7.0 * e + breathe(t, L, 0.5)
    p.yaw = 8.0 * e
    p.head_yaw = 8.0 * e
    p.head_pitch = p.head_pitch - 3.0 * e
    hy = p.head_yaw
    dist = 0.082 + 0.01 * e
    p.hands["r"] = mic_hand(lambda: hy, lambda: dist)
    r = REST_L
    reach = 0.44 + 0.035 * stab
    tgt = Vector((-0.01, -reach, 0.07))
    def lpos(S, e=e, reach=reach):
        v = r[0].lerp(Vector((-0.01, -reach, 0.06)), smooth(e))
        return S["l"] + v * BODY["arm"]
    fo = (DOWN + FWD * 0.15).lerp(FWD + UP * 0.05, smooth(e))
    up = (FWD + RIGHT * 0.3).lerp(UP, smooth(e))
    p.hands["l"] = (lpos, fo, up)
    p.elbows = {"l": lambda S: S["l"] + Vector((0.30 - 0.08 * e, 0.08 - 0.02 * e, -0.32 + 0.02 * e)),
                "r": lambda S: S["r"] + Vector((-0.22, 0.12, -0.34))}
    p.fist = {"l": 0.3 + 0.62 * e, "r": 0.85}
    p.finger_open = {"l": ["index"]} if e > 0.4 else {}
    return p

PROMO_CLIPS = [
    ("Promo_Mic_Talk_Loop", TALK_LEN, talk_pose, None, True, None, 0),
    ("Promo_Mic_Point", POINT_LEN, point_pose, None, False, None, 0),
]
NODE_POSES["promo"] = lambda: talk_pose(0.0, TALK_LEN)
CLIP_NODES["Promo_Mic_Talk_Loop"] = {"A": ("promo", "promo")}
CLIP_NODES["Promo_Mic_Point"] = {"A": ("promo", "promo")}

# ---------------------------------------------------------------- перевірки мікрофона

def mic_matrix(G, a):
    """Рамка мікрофона в просторі ригу: Z — вісь, Y — до спини (BACK), X = Y × Z."""
    Z = a.normalized()
    Y = (BACK - Z * BACK.dot(Z)).normalized()
    X = Y.cross(Z).normalized()
    m = Matrix((X, Y, Z)).transposed().to_4x4()
    m.translation = G
    return m

def pt_seg(p, a, b):
    d = b - a
    t = 0.0 if d.length < 1e-9 else max(0.0, min(1.0, (p - a).dot(d) / d.dot(d)))
    return (p - (a + d * t)).length

def mic_checks(seq, loc_attach):
    """Мікрофон у руці за кістками: відстань верху решітки до рота, найближче підхід руки й мікрофона до центру голови."""
    out = {"tip": [9, 0], "head_hand": 9.0, "head_mic": 9.0, "drift": 0.0}
    for f, cap in enumerate(seq):
        M = cap["hand_r"] @ loc_attach
        tip = M @ Vector((0, 0, MIC_LEN_ABOVE))
        hb, hh = cap["head"].translation, cap["head"] @ Vector((0, 0.07, 0.0))        # центр черепа ≈ над головою кістки
        mouth = hb + Vector((0.0, -0.09, 0.04))
        d_tip = (tip - mouth).length
        out["tip"] = [min(out["tip"][0], d_tip), max(out["tip"][1], d_tip)]
        c_head = hb + Vector((0.0, 0.0, 0.07))
        out["head_mic"] = min(out["head_mic"], pt_seg(c_head, M @ Vector((0, 0, -MIC_LEN_BELOW)), tip))
        out["head_hand"] = min(out["head_hand"], pt_seg(c_head, cap["lowerarm_r"].translation, cap["hand_r"].translation))
        if f < len(MIC_LOG):
            G, a = MIC_LOG[f]
            out["drift"] = max(out["drift"], (M.translation - G).length)
    return out

# ---------------------------------------------------------------- огляд з мікрофоном

def load_mic_mesh():
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=os.path.join(REPO, "Assets", "Art", "Ring", "Props", "mic.fbx"))
    new = [o for o in bpy.data.objects if o not in before]
    mic = next(o for o in new if o.type == 'MESH' and o.name.endswith("LOD0"))
    mic.parent = None
    for o in new:
        if o is not mic:
            bpy.data.objects.remove(o, do_unlink=True)
    mic.data.transform(mic.matrix_world)
    mic.matrix_world = Matrix.Identity(4)
    zs = [v.co.z for v in mic.data.vertices if True]
    print("[мікрофон] імпорт: z від %.3f до %.3f" % (min(zs), max(zs)), flush=True)
    mic.hide_viewport = False
    return mic

def look_promo(name, arm, seq, mic, attach, length):
    """Контактний лист: 6 моментів × 2 ракурси (збоку, три чверті), мікрофон приставлений до hand_r."""
    import numpy as np
    scene = bpy.context.scene
    try:
        scene.render.engine = 'BLENDER_WORKBENCH'
    except TypeError:
        pass
    scene.display.shading.light = 'STUDIO'
    scene.display.shading.color_type = 'OBJECT'
    W, H = 300, 340
    scene.render.resolution_x, scene.render.resolution_y = W, H
    cam = bpy.data.objects.get("look_cam")
    if cam is None:
        cam = bpy.data.objects.new("look_cam", bpy.data.cameras.new("look_cam"))
        scene.collection.objects.link(cam)
    scene.camera = cam
    cam.data.type = 'ORTHO'
    cam.data.ortho_scale = 2.0
    for o in bpy.data.objects:
        if o.type != 'MESH' or o is mic:
            continue
        show = ("body" in o.name or "low-poly" in o.name or "trousers" in o.name)
        o.hide_viewport = not show
        o.hide_render = not show
        o.color = (0.8, 0.45, 0.25, 1)
    mic.color = (0.1, 0.1, 0.9, 1)
    if arm.animation_data:
        arm.animation_data.action = None          # дії вже записані: інакше вони перекривають пози огляду
    for pb in arm.pose.bones:
        for c in pb.constraints:
            c.mute = True
    bones = arm.data.bones
    def set_pose(cap):
        for pb in arm.pose.bones:
            b = bones[pb.name]
            m = cap[pb.name]
            base = (cap[b.parent.name] @ b.parent.matrix_local.inverted() @ b.matrix_local) if b.parent else b.matrix_local
            loc, rot, _ = (base.inverted() @ m).decompose()
            pb.rotation_quaternion = rot
            pb.location = loc if pb.name == "pelvis" else Vector()
    n = len(seq)
    idx = [round((n - 1) * i / 5) for i in range(6)]
    ctr = Vector((0.0, -0.15, 0.95))
    views = [Vector((-3.2, -3.4, 0.5)), Vector((3.2, -3.4, 0.5))]       # з боку мікрофона й з боку жестів
    tiles = []
    for d in views:
        row = []
        for i in idx:
            set_pose(seq[i])
            bpy.context.view_layer.update()
            mic.matrix_world = arm.matrix_world @ seq[i]["hand_r"] @ attach
            cam.location = ctr + d
            cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
            cam.data.ortho_scale = 2.1
            path = os.path.join(LOOK_DIR, "_tile.png")
            scene.render.filepath = path
            bpy.ops.render.render(write_still=True)
            img = bpy.data.images.load(path, check_existing=False)
            px = np.array(img.pixels[:], dtype=np.float32).reshape(H, W, 4)
            bpy.data.images.remove(img)
            row.append(px)
        tiles.append(np.concatenate(row, axis=1))
    sheet = np.concatenate(tiles[::-1], axis=0)
    a_ = sheet[..., 3:4]
    sheet = np.concatenate([sheet[..., :3] * a_ + 0.85 * (1 - a_), np.ones_like(a_)], axis=-1)      # світлий фон
    out = bpy.data.images.new("sheet", width=sheet.shape[1], height=sheet.shape[0], alpha=True)
    out.pixels = sheet.ravel().tolist()
    fp = os.path.join(LOOK_DIR, name + ".png")
    out.filepath_raw = fp
    out.file_format = 'PNG'
    out.save()
    bpy.data.images.remove(out)
    for pb in arm.pose.bones:
        for c in pb.constraints:
            c.mute = False
    print("[огляд] " + fp, flush=True)

# ---------------------------------------------------------------- запуск

def promo_main():
    load_pair()
    armA = bpy.data.objects["Arm_A"]
    for o in list(bpy.data.objects):
        if o.get("pair") == "B":
            bpy.data.objects.remove(o, do_unlink=True)
    armA.matrix_world = Matrix.Identity(4)
    bpy.context.view_layer.update()
    rig = Rig(armA)
    init_rest(armA)
    capture_node(rig, "promo")
    if "--dbg" in _args:
        for tt in (0.0, 0.8, 1.2):
            pp = talk_pose(tt, TALK_LEN)
            print('DBG', tt, key_track(tt, L_TALK, 1), flush=True)
    unmute(rig)
    report = ["# Звіт якості кліпів промо (tools/blender/ring_anims_promo.py)", "",
              "Пороги: ковзання стопи ≤ %.1f см, самопроникнення ≤ %.1f см, під матом %.0f, IK не дістав ≤ %.1f см, "
              "вузол ≤ %.0f° / %.0f см, кисть ≤ %.0f°, лікоть у тулубі ≤ %.0f см." % (
                  LIMITS["slide"], LIMITS["self"], LIMITS["under"], LIMITS["reach"], LIMITS["node_deg"], LIMITS["node_cm"],
                  LIMITS["wrist"], LIMITS["elbow_in"]), "",
              "| Кліп | Ковзання | Само | Під матом | IK | Вузол | Ривок | Верх решітки—рот, см | Рука/мікрофон—голова, см | Дрейф мікрофона, см | Стан |",
              "|---|---|---|---|---|---|---|---|---|---|---|"]
    manifest = ["# Кліпи промо (tools/blender/ring_anims_promo.py). <кліп> <роль A|B|-> <довжина с> <петля 0|1> "
                "<зсув B: x y z (м, простір A)> <поворот B °> <з вузла> <у вузол>"]
    samples, failed, attach, seq_for_look = [], [], None, {}
    for name, length, fa, fb, loop, offset, yaw in PROMO_CLIPS:
        place(armA, Vector(), 0)
        MIC_LOG.clear()
        sa, sb, wa, wb, q, gaps = bake_clip(rig, rig, name, length, fa, None)
        if attach is None:
            M0 = mic_matrix(*MIC_LOG[0])
            attach = sa[0]["hand_r"].inverted() @ M0
        mc = mic_checks(sa, attach)
        if '--dbg' in _args:
            for f_ in (0, 24, 36, 48, 72):
                print('DBG', name, f_, tuple(round(x,2) for x in sa[f_]['hand_l'].translation), tuple(round(x,2) for x in sa[f_]['upperarm_l'].translation), flush=True)
        bad = fails(q)
        state = "OK" if not bad else "ПОНАД ПОРІГ: " + ", ".join(bad)
        if q["jerk"][0] > JERK_WARN:
            state += " · ривок %.1f (%s)" % q["jerk"]
        if not (0.05 < mc["tip"][0] and mc["tip"][1] < 0.13):
            state += " · мікрофон далеко від рота"
        if mc["head_hand"] < 0.10 or mc["head_mic"] < 0.0615:
            state += " · мікрофон/рука в голові"
        if mc["drift"] > 0.02:
            state += " · мікрофон «пливе»"
        node_s = "%.1f° / %.1f см" % q["node"] if "node" in q else "—"
        line = ("| %s | %.1f %s | %.1f %s | %.1f %s | %.1f %s | %s | %.1f %s | %.1f–%.1f | %.1f / %.1f | %.1f | %s |" % (
            name, q["slide"][0], q["slide"][1], q["self"][0], q["self"][1], q["under"][0], q["under"][1],
            q["reach"][0], q["reach"][1], node_s, q["jerk"][0], q["jerk"][1], mc["tip"][0] * 100, mc["tip"][1] * 100,
            mc["head_hand"] * 100, mc["head_mic"] * 100, mc["drift"] * 100, state))
        for k in ("wrist", "elbow_in"):
            if q.get(k, (0, ""))[0] > LIMITS[k]:
                line += " %s %.0f (%s)" % (k, q[k][0], q[k][1])
        print("[кліп] " + line, flush=True)
        report.append(line)
        if bad:
            failed.append(name)
        samples.append((name, sa, armA))
        seq_for_look[name] = (sa, length)
        manifest.append("%s - %.3f %d 0 0 0 0 promo promo" % (name, length, int(loop)))
    strip_constraints(rig)
    actions = {n: write_action(armA, n, s_) for n, s_, arm in samples}
    loop_of = {c[0]: c[4] for c in PROMO_CLIPS}
    for n, act in actions.items():
        overlap(act, n, loop_of.get(n, False))
    if LOOK:
        os.makedirs(LOOK_DIR, exist_ok=True)
        mic = load_mic_mesh()
        for n, (sa, ln) in seq_for_look.items():
            look_promo(n, armA, sa, mic, attach, ln)
    for o in list(bpy.data.objects):
        if o.type == 'MESH':
            bpy.data.objects.remove(o, do_unlink=True)
    place(armA, Vector(), 0)
    export_ring(armA)
    loc, rot, _ = attach.decompose()
    eul = rot.to_euler('XYZ')
    manifest.insert(1, "# attach mic bone=hand_r pos=%.4f,%.4f,%.4f quat_wxyz=%.5f,%.5f,%.5f,%.5f (простір кістки Blender: Y уздовж "
                       "кістки; початок мікрофона — центр рукояті, вісь Z мікрофона — до верху решітки, −Y — кнопка; "
                       "Mic_Grip у mic.fbx = початок)" % (loc.x, loc.y, loc.z, rot.w, rot.x, rot.y, rot.z))
    with open(MANIFEST, "w", encoding="utf-8", newline="\n") as fh:
        fh.write("\n".join(manifest) + "\n")
    os.makedirs(LOOK_DIR, exist_ok=True)
    with open(os.path.join(LOOK_DIR, "report.md"), "w", encoding="utf-8", newline="\n") as fh:
        fh.write("\n".join(report) + "\n")
    print("[експорт] " + OUT_FBX, flush=True)
    print("[кріплення] pos=%.4f,%.4f,%.4f quat=%.5f,%.5f,%.5f,%.5f" % (loc.x, loc.y, loc.z, rot.w, rot.x, rot.y, rot.z), flush=True)
    print("[якість] " + ("понад поріг: " + ", ".join(failed) if failed else "усі кліпи в межах порогів"), flush=True)

promo_main()
