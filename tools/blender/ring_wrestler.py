# Борці «Рингу» — лоу-полі, але детальні (план ітерації 1, крок 6; власник 29.10.2026: «всі лоу полі але детальні»).
# Ігрова сітка — тіло набору (Assets/Art/Characters/Kit/body_*.fbx, ~1,8 тис. вершин, риг і ваги не змінюються);
# деталь запікається з детальної версії того самого тіла: підрозділення, рельєф м'язів і шкіри, екіпіровка борця —
# труси з поясом, взуття зі шнурівкою, наколінники, тейп на зап'ястях. Метод — як у ring_hero.py (предмети рингу).
#
#   "C:\Program Files\Blender Foundation\Blender 4.5\blender.exe" -b --python tools/blender/ring_wrestler.py -- <корінь репо> [тіло…] [--preview]
#
# Виходи (на кожне тіло, UV — та сама, що в наборі, тож текстури лягають на зони тіла без змін):
#   Assets/Art/Ring/Wrestlers/Textures/wrestler_<тіло>_normal.png (дотична, OpenGL), _albedo.png (sRGB), _mask.png
#   (R — металевість, G — AO, B — зона екіпіровки під колір команди, A — гладкість);
#   Assets/Art/Ring/Wrestlers/wrestler_manifest.txt; Logs/wrestler/<тіло>_preview.png (--preview).
# Детерміновано: шум — з зерна за іменем тіла. Асети — лише свої (тіла набору — власна генерація, CC0-сумісні).

import bpy, bmesh, math, os, sys, time
import numpy as np
from mathutils import Vector, kdtree

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
REPO = args[0] if args else os.getcwd()
WANT = [a for a in args[1:] if not a.startswith("--")]
PREVIEW = "--preview" in args
KIT = os.path.join(REPO, "Assets", "Art", "Characters", "Kit")
OUT = os.path.join(REPO, "Assets", "Art", "Ring", "Wrestlers")
TEX = os.path.join(OUT, "Textures")
LOGS = os.path.join(REPO, "Logs", "wrestler")
S = 1024                                                   # текстура на тіло (Android: 1024 вистачає для 1,8 м)

# зона: (колір лінійний, металевість, шорсткість, підфарбовується під команду)
ZONES = {
    "skin":   ((0.8, 0.8, 0.8), 0.0, 0.55, False),          # колір — з текстури шкіри тіла набору (skin_<тіло>.png)
    "trunks": ((0.10, 0.02, 0.02), 0.0, 0.35, True),        # лайкра, блиск
    "band":   ((0.02, 0.02, 0.02), 0.0, 0.5, False),        # пояс трусів
    "boots":  ((0.015, 0.015, 0.016), 0.0, 0.3, True),      # шкіра взуття
    "laces":  ((0.6, 0.6, 0.58), 0.0, 0.7, False),
    "pads":   ((0.02, 0.02, 0.022), 0.0, 0.6, True),        # неопрен наколінників
    "tape":   ((0.75, 0.74, 0.7), 0.0, 0.8, False),         # спортивний тейп
    "top":    ((0.10, 0.02, 0.02), 0.0, 0.35, True),        # спортивний топ (жіночі тіла), у колір трусів
    # форма рефері: смугаста сорочка з короткими рукавами, чорні штани, чорне взуття
    "shirt_w": ((0.8, 0.8, 0.78), 0.0, 0.75, False),
    "shirt_b": ((0.012, 0.012, 0.012), 0.0, 0.75, False),
    "pants":  ((0.01, 0.01, 0.012), 0.0, 0.65, False),
    "shoes":  ((0.008, 0.008, 0.008), 0.0, 0.25, False),
}
ZN = list(ZONES)


def zone_rgb(i):
    return ((i * 53 % 256) / 255.0, (i * 97 % 256) / 255.0, (i * 151 % 256) / 255.0)


def zone_mat(z):
    m = bpy.data.materials.get("Z_" + z) or bpy.data.materials.new("Z_" + z)
    m.use_nodes = True
    b = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    c = zone_rgb(ZN.index(z))
    b.inputs["Base Color"].default_value = (*c, 1)
    b.inputs["Emission Color"].default_value = (*c, 1)
    b.inputs["Emission Strength"].default_value = 0.0
    return m


def load_body(name):
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o)
    bpy.ops.import_scene.fbx(filepath=os.path.join(KIT, name + ".fbx"))
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    zones = [o for o in bpy.data.objects if o.type == "MESH" and ".body_" in o.name]
    return arm, zones


def joined_copy(zones, name):
    bpy.ops.object.select_all(action='DESELECT')
    cps = []
    for z in zones:
        c = z.copy(); c.data = z.data.copy()
        bpy.context.scene.collection.objects.link(c)
        for m in list(c.modifiers):
            c.modifiers.remove(m)                          # поза спокою (без арматури)
        mw = z.matrix_world.copy()
        c.parent = None
        c.matrix_world = mw
        for uv in c.data.uv_layers:                       # одне ім'я UV — інакше об'єднання розкладає зони по різних шарах
            uv.name = "UVMap"
        cps.append(c)
    for c in cps:
        c.select_set(True)
    bpy.context.view_layer.objects.active = cps[0]
    bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active
    o.name = name
    for p in o.data.polygons:                              # гладко, як у грі: з пласких граней карта нормалей
        p.use_smooth = True                                # малювала трикутники на шкірі
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.remove_doubles(threshold=0.0005); bpy.ops.object.mode_set(mode='OBJECT')
    return o


def bone_pts(arm):
    W = arm.matrix_world
    return {b.name: (W @ b.head_local, W @ b.tail_local) for b in arm.data.bones}


def seg_d(p, a, b):
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-9)))
    return (p - (a + ab * t)).length, t


def classify_ref(p, B):
    """Форма рефері (записи матчів, своїми словами): сорочка у вертикальну смугу ~5 см з короткими рукавами до
    середини плеча, чорні штани до стопи, чорне взуття."""
    hip = B["pelvis"][0]
    foot_z = min(B["foot_l"][0].z, B["foot_r"][0].z)
    on_hand = min(seg_d(p, *B["hand_" + s])[0] for s in "lr") < 0.06
    on_fore = min(seg_d(p, *B["lowerarm_" + s])[0] for s in "lr") < 0.07
    up = [seg_d(p, *B["upperarm_" + s]) for s in "lr"]
    on_up = min(d for d, _ in up) < 0.08
    sleeve = on_up and min((t for d, t in up if d < 0.08), default=1.0) < 0.55
    neck_z = B["neck_01"][0].z - 0.01
    if p.z < foot_z + 0.09 and not on_hand:
        return "shoes", 0.006
    if p.z < hip.z + 0.05 and not (on_hand or on_fore):
        return "pants", 0.004
    if p.z < neck_z and not (on_hand or on_fore) and (not on_up or sleeve):
        return ("shirt_w" if int((p.x + 2.0) / 0.05) % 2 == 0 else "shirt_b"), 0.003
    return "skin", 0.0


HAND_BONES = ("hand_", "thumb_", "index_", "middle_", "ring_", "pinky_")


def near_hand(p, B):
    """Точка найближча до кісток кисті чи пальців — шкіра (опущені пальці нижче поясу йшли в штани/труси)."""
    best, nm = 9.0, ""
    for n, (h, t) in B.items():
        d = seg_d(p, h, t)[0]
        if d < best:
            best, nm = d, n
    return nm.startswith(HAND_BONES)


def classify(p, B, female, ref=False):
    if near_hand(p, B):
        return "skin", 0.0
    if ref:
        return classify_ref(p, B)
    """Зона екіпіровки й висота рельєфу (м) для точки тіла p — одне правило і для детальної сітки, і для пікселів
    текстури (межі зон рівні: раніше зони бралися з граней сітки й ішли сходинками)."""
    hip = B["pelvis"][0]
    z, d = "skin", 0.0
    knee_d = min(seg_d(p, *B["calf_" + s])[0] if seg_d(p, *B["calf_" + s])[1] < 0.18 else 9 for s in "lr")
    wrist_d = min(seg_d(p, *B["lowerarm_" + s]) for s in "lr")
    foot_z = min(B["foot_l"][0].z, B["foot_r"][0].z)
    on_arm = min(seg_d(p, *B[n + "_" + s])[0] for n in ("upperarm", "lowerarm", "hand") for s in "lr") < 0.1
    chest_lo, chest_hi = B["spine_02"][0].z + 0.02, B["spine_03"][1].z - 0.03
    on_upper = min(seg_d(p, *B["upperarm_" + s])[0] for s in "lr") < 0.07
    if female and not on_upper and not on_arm and chest_lo < p.z < chest_hi:
        z = "top"; d = 0.004
    elif not on_arm and hip.z - 0.2 < p.z < hip.z + 0.06:
        z = "band" if p.z > hip.z + 0.025 else "trunks"
        d = 0.004 if z == "trunks" else 0.007
    elif p.z < foot_z + 0.2 and not on_arm:
        z = "boots"; d = 0.006
        fs = "foot_l" if p.x > 0 else "foot_r"                # шнурівка — вузька смужка по центру підйому стопи
        front = p.y < B[fs][0].y - 0.02 and abs(p.x - B[fs][0].x) < 0.022
        if front and foot_z + 0.03 < p.z < foot_z + 0.17 and int((p.z - foot_z) / 0.018) % 2 == 0:
            z = "laces"; d = 0.008
    elif knee_d < 0.075 and not on_arm and p.y < min(B["calf_l"][0].y, B["calf_r"][0].y) + 0.02:
        z = "pads"; d = 0.012 * (1 - knee_d / 0.075) ** 0.5 + 0.004
    elif wrist_d[1] > 0.72 and wrist_d[0] < 0.07:
        z = "tape"; d = 0.0025 + 0.0008 * math.sin((p - B["hand_l" if p.x > 0 else "hand_r"][0]).length * 2 * math.pi / 0.012)
    return z, d


def build_high(lo, arm, seed, female=False, ref=False):
    """Детальна версія: підрозділення, зони екіпіровки (за кістками рігу) і зсув уздовж нормалі — рельєф."""
    hi = lo.copy(); hi.data = lo.data.copy()
    bpy.context.scene.collection.objects.link(hi)
    hi.name = lo.name + "_high"
    m = hi.modifiers.new("sub", 'SUBSURF'); m.levels = m.render_levels = 3      # межі зон — без сходинок
    bpy.context.view_layer.objects.active = hi
    bpy.ops.object.modifier_apply(modifier="sub")
    B = bone_pts(arm)
    bm = bmesh.new(); bm.from_mesh(hi.data)
    bm.verts.ensure_lookup_table(); bm.normal_update()
    zone_v = {}
    disp = {}
    for v in bm.verts:
        z, d = classify(v.co, B, female, ref)
        zone_v[v.index] = z
        disp[v.index] = d
    for v in bm.verts:
        v.co = v.co + v.normal * disp[v.index]
    mats = {z: i for i, z in enumerate(ZN)}
    hi.data.materials.clear()
    for z in ZN:
        hi.data.materials.append(zone_mat(z))
    for f in bm.faces:
        zs = [zone_v[v.index] for v in f.verts]
        f.material_index = mats[max(set(zs), key=zs.count)]
        f.smooth = True
    bm.to_mesh(hi.data); bm.free()
    return hi


def setup_cycles():
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'
    try:
        prefs = bpy.context.preferences.addons["cycles"].preferences
        for dt in ("OPTIX", "CUDA"):
            try:
                prefs.compute_device_type = dt
            except TypeError:
                continue
            prefs.get_devices()
            if any(d.type == dt for d in prefs.devices):
                for d in prefs.devices:
                    d.use = d.type == dt
                sc.cycles.device = 'GPU'
                break
    except Exception as e:
        print("[wrestler] GPU недоступний:", e, flush=True)
    sc.cycles.samples = 64
    if sc.world is None:
        sc.world = bpy.data.worlds.new("W")
    sc.world.light_settings.distance = 0.12


def bake(lo, his, kind, ray, name, selected=True):
    img = bpy.data.images.new(name, S, S, alpha=False, float_buffer=True)
    img.colorspace_settings.name = 'Non-Color'
    for ms in lo.material_slots:
        nt = ms.material.node_tree
        n = nt.nodes.get("bake_img") or nt.nodes.new("ShaderNodeTexImage")
        n.name = "bake_img"; n.image = img
        nt.nodes.active = n
    bpy.ops.object.select_all(action='DESELECT')
    for h in his:
        h.select_set(True)
    lo.select_set(True)
    bpy.context.view_layer.objects.active = lo
    kw = dict(type=kind, use_selected_to_active=selected, margin=8, use_clear=True)
    if selected:
        kw.update(cage_extrusion=ray * 0.5, max_ray_distance=ray)
    if kind == 'NORMAL':
        kw["normal_space"] = 'TANGENT'
    if kind == 'DIFFUSE':
        kw["pass_filter"] = {'COLOR'}
    bpy.ops.object.bake(**kw)
    a = np.empty(S * S * 4, dtype=np.float32)
    img.pixels.foreach_get(a)
    bpy.data.images.remove(img)
    return a.reshape(S, S, 4)


def to_srgb(c):
    c = np.clip(c, 0, 1)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * np.power(c, 1 / 2.4) - 0.055)


def save_png(arr, path, srgb):
    img = bpy.data.images.new(os.path.basename(path), S, S, alpha=True, float_buffer=False)
    img.colorspace_settings.name = 'sRGB' if srgb else 'Non-Color'
    rgba = np.ones((S, S, 4), np.float32)
    rgba[..., :arr.shape[2]] = arr
    img.pixels.foreach_set(np.clip(rgba, 0, 1).ravel())
    img.filepath_raw = path; img.file_format = 'PNG'; img.save()
    bpy.data.images.remove(img)


def zone_map(lo, arm, female, n=1024, ref=False):
    """Зона для кожного пікселя (n×n, далі збільшується до S): позиція точки тіла запікається в текстуру і
    класифікується тим самим правилом, що й детальна сітка — межі поясу й наколінників рівні."""
    m = bpy.data.materials.new("POS"); m.use_nodes = True
    nt = m.node_tree; nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial"); em = nt.nodes.new("ShaderNodeEmission")
    geo = nt.nodes.new("ShaderNodeNewGeometry")
    nt.links.new(geo.outputs["Position"], em.inputs["Color"]); nt.links.new(em.outputs[0], out.inputs[0])
    old = [sl.material for sl in lo.material_slots]
    lo.data.materials.clear(); lo.data.materials.append(m)
    global S
    S0, S = S, n
    pos = bake(lo, [], 'EMIT', 0.0, "pos", selected=False)
    S = S0
    lo.data.materials.clear()
    for o_ in old:
        lo.data.materials.append(o_)
    B = bone_pts(arm)
    zi = np.zeros((n, n), np.int32)
    used = np.abs(pos[..., :3]).sum(-1) > 1e-6
    for y, x in zip(*np.nonzero(used)):
        zi[y, x] = ZN.index(classify(Vector(pos[y, x, :3]), B, female, ref)[0])
    return np.repeat(np.repeat(zi, S // n, 0), S // n, 1)


def skin_tex(name):
    """Кольорова шкіра тіла набору (MakeHuman, суспільне надбання) — та сама UV."""
    p = os.path.join(REPO, "Assets", "Art", "Textures", "Kit", name.replace("body_", "skin_") + ".png")
    img = bpy.data.images.load(p)
    w, h = img.size
    a = np.empty(w * h * 4, np.float32); img.pixels.foreach_get(a)
    a = a.reshape(h, w, 4)
    if w != S:
        idx = (np.arange(S) * w // S)
        a = a[idx][:, idx]
    bpy.data.images.remove(img)
    return a[..., :3]                                       # лінійні (Blender перевів із sRGB)


def compose(name, nrm, ao, zi, skin):
    save_png(nrm[..., :3], os.path.join(TEX, "wrestler_%s_normal.png" % name), False)
    alb = np.zeros((S, S, 3), np.float32)
    rough = np.zeros((S, S), np.float32)
    tint = np.zeros((S, S), np.float32)
    for i, z in enumerate(ZN):
        sel = zi == i
        col, met, rgh, tm = ZONES[z]
        alb[sel] = skin[sel] if z == "skin" else np.array(col, np.float32)[None]
        rough[sel] = rgh
        tint[sel] = 1.0 if tm else 0.0
    k_ao = np.where(zi == ZN.index("skin"), 0.12, 0.2)[..., None]   # на шкірі AO ледь-ледь — без плям
    alb = alb * (1 - k_ao + k_ao * ao[..., None])                  # частина AO — у колір (мобільні)
    save_png(to_srgb(alb), os.path.join(TEX, "wrestler_%s_albedo.png" % name), True)
    save_png(np.stack([np.zeros_like(ao), ao, tint, 1 - rough], -1), os.path.join(TEX, "wrestler_%s_mask.png" % name), False)
    return {z: int((zi == i).sum()) for i, z in enumerate(ZN)}


def preview(name, lo):
    m = bpy.data.materials.new("PV_" + name); m.use_nodes = True
    nt = m.node_tree; b = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    ia = nt.nodes.new("ShaderNodeTexImage"); ia.image = bpy.data.images.load(os.path.join(TEX, "wrestler_%s_albedo.png" % name))
    inn = nt.nodes.new("ShaderNodeTexImage"); inn.image = bpy.data.images.load(os.path.join(TEX, "wrestler_%s_normal.png" % name))
    inn.image.colorspace_settings.name = 'Non-Color'
    nm = nt.nodes.new("ShaderNodeNormalMap")
    nt.links.new(ia.outputs["Color"], b.inputs["Base Color"]); nt.links.new(inn.outputs["Color"], nm.inputs["Color"])
    nt.links.new(nm.outputs["Normal"], b.inputs["Normal"])
    lo.data.materials.clear(); lo.data.materials.append(m)
    for o in bpy.data.objects:                          # зони тіла набору лежать у тій самій поверхні — інакше мерехтять
        if o.type == "MESH" and o is not lo:
            o.hide_render = True
    sc = bpy.context.scene
    try:
        sc.render.engine = 'BLENDER_EEVEE_NEXT'
    except TypeError:
        sc.render.engine = 'BLENDER_EEVEE'
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam)
    li = bpy.data.objects.new("sun", bpy.data.lights.new("sun", 'SUN')); sc.collection.objects.link(li)
    li.rotation_euler = (math.radians(50), 0, math.radians(30)); li.data.energy = 3.0
    sc.camera = cam; cam.data.lens = 50
    sc.render.resolution_x, sc.render.resolution_y = 700, 900
    os.makedirs(LOGS, exist_ok=True)
    bb = [lo.matrix_world @ Vector(c) for c in lo.bound_box]
    ctr = sum(bb, Vector()) / 8
    for i, d in enumerate((Vector((0, -1, 0.05)), Vector((0.7, -0.7, 0.15)), Vector((0, 1, 0.05)))):
        cam.location = ctr + d.normalized() * 4.0
        cam.rotation_euler = (ctr - cam.location).to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = os.path.join(LOGS, "%s_preview_%d.png" % (name, i))
        bpy.ops.render.render(write_still=True)


def main():
    os.makedirs(TEX, exist_ok=True)
    names = WANT or sorted(f[:-4] for f in os.listdir(KIT) if f.startswith("body_") and f.endswith(".fbx"))
    manifest = []
    for spec in names:
        t0 = time.time()
        ref = spec.startswith("ref:")                     # «ref:<тіло>» — форма рефері на тілі набору
        body = spec[4:] if ref else spec
        name = ("ref_" + body[5:]) if ref else body
        arm, zones = load_body(body)
        setup_cycles()
        lo = joined_copy(zones, "lo_" + name)
        seed = sum(ord(c) * (k + 1) for k, c in enumerate(name))
        hi = build_high(lo, arm, seed, female="_f_" in body, ref=ref)
        tris_hi = sum(len(p.vertices) - 2 for p in hi.data.polygons)
        # детальна сітка не притискається до легкої (стає гранчастою — трикутники на шкірі), тож промінь довший:
        # підрозділення стягує її всередину до кількох сантиметрів
        nrm = bake(lo, [hi], 'NORMAL', 0.07, "n")
        ao = bake(lo, [], 'AO', 0.0, "ao", selected=False)[..., 0]     # AO — з самої легкої (з детальної — сітка ліній)
        for ms in hi.material_slots:
            next(n for n in ms.material.node_tree.nodes if n.type == "BSDF_PRINCIPLED").inputs["Emission Strength"].default_value = 1.0
        zi = zone_map(lo, arm, "_f_" in body, ref=ref)
        cover = compose(name, nrm, ao, zi, skin_tex(body))
        tris_lo = sum(len(p.vertices) - 2 for p in lo.data.polygons)
        manifest.append("wrestler %s %d %d %d %s" % (name, tris_lo, tris_hi, S, " ".join("%s=%d" % kv for kv in cover.items() if kv[1])))
        print("[wrestler] %s: легка %d трик., детальна %d трик., %.0f с" % (name, tris_lo, tris_hi, time.time() - t0), flush=True)
        if PREVIEW:
            bpy.data.objects.remove(hi)
            preview(name, lo)
    mf = os.path.join(OUT, "wrestler_manifest.txt")
    old = {}
    if os.path.exists(mf):
        for line in open(mf, encoding="utf-8"):
            if line.startswith("wrestler "):
                old[line.split()[1]] = line.rstrip("\n")
    for line in manifest:
        old[line.split()[1]] = line
    with open(mf, "w", encoding="utf-8", newline="\n") as f:
        f.write("# Борці (tools/blender/ring_wrestler.py): wrestler <тіло> <трик. легкої> <трик. детальної> <текстура> <зони=пікселі…>\n")
        for k in sorted(old):
            f.write(old[k] + "\n")
    print("[wrestler] готово:", ", ".join(names), flush=True)


main()
