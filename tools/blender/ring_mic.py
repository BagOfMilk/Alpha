# Ручний мікрофон для промо «Рингу» (docs/ring/gdd.md §8): власна генерація, без чужих асетів і без слів у текстурах.
#
#   "C:\Program Files\Blender Foundation\Blender 4.5\blender.exe" -b --python tools/blender/ring_mic.py -- <репо> [--iter N] [--look]
#
# Модель — у метрах, вісь мікрофона уздовж +Z, «вперед» (кнопка) — −Y; Mic_Grip — порожній об'єкт у центрі рукояті (там, де хват).
# Довжина 19 см: від −6,5 см (торець із кабелем) до +12,5 см (верх решітки), голова Ø 5 см, рукоять Ø 3,5 → 2,8 см.
# Кабель звисає дугою від торця до підлоги (форма розрахована на нахил мікрофона у промо-кліпах, THETA — кут осі від вертикалі).
# Три ітерації (правило власника, docs/TRPG_DESIGN.md §11.0): 1 — low poly, 2 — фаски й пропорції, решітка нормаллю,
# 3 — деталі студійного рівня в межах бюджету (LOD0 ≤ 600 трикутників, LOD1 ≤ 100). --iter N будує ітерацію N
# (за замовчуванням 3) і пише Logs/ring_mic/iterN.png; експорт — Assets/Art/Ring/Props/mic.fbx (осі як у props.fbx).
import bpy, bmesh, math, os, sys
from mathutils import Vector, Matrix

_args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
REPO = _args[0] if _args else os.getcwd()
ITER = int(_args[_args.index("--iter") + 1]) if "--iter" in _args else 3
OUT_DIR = os.path.join(REPO, "Assets", "Art", "Ring", "Props")
LOG_DIR = os.path.join(REPO, "Logs", "ring_mic")
THETA = math.radians(45.0)          # нахил осі мікрофона від вертикалі в промо-позі (верх — до обличчя)
Z_BOT, Z_TOP = -0.065, 0.125

# ---------------------------------------------------------------- матеріали й текстури

def _grille_images():
    """Решітка: діагональна сітка отворів. diff — темні отвори на сталі; nor — нормаль зі шпаруватого поля висот."""
    import numpy as np
    N = 256
    u = (np.arange(N) + 0.5) / N
    X, Y = np.meshgrid(u, u)
    cells = 16
    fx = (X * cells) % 1.0
    fy = (Y * cells) % 1.0
    hx = np.minimum(fx, 1 - fx)
    hy = np.minimum(fy, 1 - fy)
    d = np.sqrt((fx - 0.5) ** 2 + (fy - 0.5) ** 2)                 # відстань до центру отвору
    hole = np.clip((0.30 - d) / 0.06, 0.0, 1.0)                    # 1 — отвір
    height = 1.0 - hole * 0.9
    gx = np.gradient(height, axis=1) * N * 0.02
    gy = np.gradient(height, axis=0) * N * 0.02
    nz = np.ones_like(gx)
    ln = np.sqrt(gx ** 2 + gy ** 2 + nz ** 2)
    nor = np.stack([(-gx / ln) * 0.5 + 0.5, (-gy / ln) * 0.5 + 0.5, nz / ln * 0.5 + 0.5, np.ones_like(gx)], axis=-1)
    return nor, hole, N

def _save_png(path, arr, nonc):
    h, w = arr.shape[:2]
    img = bpy.data.images.new(os.path.basename(path), width=w, height=h, alpha=True)
    img.pixels = arr.astype("float32").ravel().tolist()
    img.filepath_raw = path
    img.file_format = 'PNG'
    if nonc:
        img.colorspace_settings.name = 'Non-Color'
    img.save()
    return img

def make_textures():
    import numpy as np
    nor, hole, N = _grille_images()
    diff = np.zeros((N, N, 4), dtype=np.float32)
    steel = 0.55 + 0.1 * (1.0 - hole)
    for c in range(3):
        diff[..., c] = steel * (1.0 - hole) + 0.02 * hole
    diff[..., 3] = 1.0
    os.makedirs(OUT_DIR, exist_ok=True)
    pd = os.path.join(OUT_DIR, "mic_grille_diff.png")
    pn = os.path.join(OUT_DIR, "mic_grille_nor.png")
    _save_png(pd, diff, False)
    _save_png(pn, nor, True)
    return pd, pn

def mat(name, color, metal, rough, tex=None):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    b = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    b.inputs["Base Color"].default_value = (*color, 1.0)
    b.inputs["Metallic"].default_value = metal
    b.inputs["Roughness"].default_value = rough
    m.diffuse_color = (*color, 1.0)
    if tex:
        pd, pn = tex
        td = nt.nodes.new("ShaderNodeTexImage")
        td.image = bpy.data.images.load(pd, check_existing=True)
        tn = nt.nodes.new("ShaderNodeTexImage")
        tn.image = bpy.data.images.load(pn, check_existing=True)
        tn.image.colorspace_settings.name = 'Non-Color'
        nm = nt.nodes.new("ShaderNodeNormalMap")
        nm.inputs["Strength"].default_value = 1.0
        nt.links.new(td.outputs["Color"], b.inputs["Base Color"])
        nt.links.new(tn.outputs["Color"], nm.inputs["Color"])
        nt.links.new(nm.outputs["Normal"], b.inputs["Normal"])
    return m

# ---------------------------------------------------------------- геометрія

def lathe(bm, profile, segs, mat_i, uv_grille=False, closed_top=True):
    """Тіло обертання: профіль [(r, z)] знизу вгору; r=0 на краях — полюс (трикутники)."""
    rings = []
    for (r, z) in profile:
        if r < 1e-6:
            rings.append(bm.verts.new((0, 0, z)))
        else:
            rings.append([bm.verts.new((r * math.cos(2 * math.pi * k / segs), r * math.sin(2 * math.pi * k / segs), z))
                          for k in range(segs)])
    faces = []
    for i in range(len(profile) - 1):
        a, b = rings[i], rings[i + 1]
        for k in range(segs):
            k2 = (k + 1) % segs
            if isinstance(a, list) and isinstance(b, list):
                f = bm.faces.new((a[k], a[k2], b[k2], b[k]))
            elif isinstance(a, list):
                f = bm.faces.new((a[k], a[k2], b))
            else:
                f = bm.faces.new((a, b[k2], b[k]))
            f.material_index = mat_i
            faces.append(f)
    return faces

def _grille_uv(bm, faces):
    uvl = bm.loops.layers.uv.verify()
    for f in faces:
        for l in f.loops:
            v = l.vert.co
            ang = math.atan2(v.y, v.x) / (2 * math.pi) + 0.5
            l[uvl].uv = (ang * 3.0, (v.z - 0.07) * 14.0)

def tube_path(bm, pts, r, segs, mat_i):
    """Трубка (кабель) вздовж ламаної: кільця на кожній точці, грані між ними (без торців усередині)."""
    rings = []
    for i, p in enumerate(pts):
        t = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        ref = Vector((1, 0, 0)) if abs(t.x) < 0.9 else Vector((0, 1, 0))
        u = t.cross(ref).normalized()
        v = t.cross(u).normalized()
        rings.append([bm.verts.new(p + (u * math.cos(2 * math.pi * k / segs) + v * math.sin(2 * math.pi * k / segs)) * r)
                      for k in range(segs)])
    for i in range(len(rings) - 1):
        for k in range(segs):
            k2 = (k + 1) % segs
            f = bm.faces.new((rings[i][k], rings[i][k2], rings[i + 1][k2], rings[i + 1][k]))
            f.material_index = mat_i

def cable_points(it):
    """Кабель від торця: вниз уздовж −Z, дуга до «плумба» D, пряма до підлоги, завиток по підлозі."""
    s, c = math.sin(THETA), math.cos(THETA)
    D = Vector((0, s, -c))
    H = Vector((0, -c, -s))                  # горизонталь «вперед» (−Y світу) у системі мікрофона
    pts = [Vector((0, 0, Z_BOT - 0.01))]
    # вихід по осі й дуга (радіус 6 см)
    R = 0.07
    n_arc = 3 if it == 1 else 4
    start = Vector((0, 0, Z_BOT - 0.03))
    pts.append(start)
    # дуга: кут від −Z до D (в площині YZ мікрофона)
    for i in range(1, n_arc + 1):
        a = THETA * i / n_arc
        pts.append(start + Vector((0, R * (1 - math.cos(a)), -R * math.sin(a))))
    p = pts[-1]
    drop = 1.18                                # довжина падіння до підлоги по світовій вертикалі
    Ls = drop / c
    n_str = 2 if it == 1 else 3
    for i in range(1, n_str + 1):
        pts.append(p + D * (Ls * i / n_str))
    p = pts[-1]
    # завиток по підлозі: плавний поворот на горизонталь і хвиля
    n_c = 3 if it == 1 else 5
    for i in range(1, n_c + 1):
        u = i / n_c
        side = Vector((1, 0, 0))
        pts.append(p + D * (0.04 * math.sin(u * math.pi * 0.5)) + H * (0.28 * u) + side * (0.09 * math.sin(u * math.pi * 1.6)))
    return pts

def build_body(it):
    bm = bmesh.new()
    segs = {1: 8, 2: 12, 3: 12}[it]
    # профіль «знизу вгору»: (r, z); матеріали: 0 корпус, 1 решітка, 2 акцент, 3 кабель, 4 кнопка
    if it == 1:
        body = [(0.0, Z_BOT), (0.014, Z_BOT), (0.018, -0.03), (0.0175, 0.074), (0.0, 0.074)]
    elif it == 2:
        body = [(0.0, Z_BOT), (0.012, Z_BOT), (0.016, Z_BOT + 0.008), (0.0145, -0.04), (0.0165, 0.02),
                (0.0185, 0.065), (0.0165, 0.072), (0.0, 0.072)]
    else:
        body = [(0.0, Z_BOT), (0.011, Z_BOT), (0.0145, Z_BOT + 0.004), (0.0165, Z_BOT + 0.012), (0.0150, -0.040),
                (0.0158, -0.01), (0.0170, 0.025), (0.0178, 0.055), (0.0190, 0.064), (0.0215, 0.068),
                (0.0235, 0.074), (0.0, 0.074)]
    lathe(bm, body, segs, 0)
    # акцентна смуга (емблема-кільце, без слів)
    if it >= 2:
        z0, z1 = (0.0, 0.016) if it == 2 else (0.012, 0.027)
        rr = 0.0175 if it == 2 else 0.0178
        lathe(bm, [(rr, z0), (rr + 0.0006, z0 + 0.001), (rr + 0.0006, z1 - 0.001), (rr, z1)], segs, 2)
        if it == 2:
            pass
    else:
        lathe(bm, [(0.0185, 0.0), (0.0185, 0.015)], segs, 2)
    # решітка: куля (голова), центр на 0,10 (Ø 5 см)
    rings = 3 if it == 1 else 5
    cz, R = 0.1, 0.0255 if it >= 2 else 0.025
    prof = [(R * 0.55, 0.074)] if it >= 2 else [(0.02, 0.074)]
    for i in range(0, rings + 1):
        a = math.radians(-35 + 125 * i / rings) if it >= 2 else math.radians(-30 + 120 * i / rings)
        prof.append((max(R * math.cos(a), 0.0), cz + R * math.sin(a) * 1.0))
    prof[-1] = (0.0, prof[-1][1])
    faces = lathe(bm, prof, segs, 1)
    if it >= 2:
        _grille_uv(bm, faces)
    # кнопка спереду (−Y): 1 — призма, 2–3 — заокруглена пластинка
    if it >= 2:
        bw = bmesh.ops.create_cube(bm, size=1.0)
        sc = (0.011, 0.005, 0.022) if it == 2 else (0.010, 0.0045, 0.020)
        bmesh.ops.scale(bm, vec=sc, verts=bw["verts"])
        bmesh.ops.translate(bm, vec=(0, -0.0188, 0.040 if it == 3 else 0.035), verts=bw["verts"])
        for f in {f for v in bw["verts"] for f in v.link_faces}:
            f.material_index = 4
        if it == 3:
            bmesh.ops.bevel(bm, geom=list({e for v in bw["verts"] for e in v.link_edges}), offset=0.0012, segments=1,
                            affect='EDGES')
    # кабель
    pts = cable_points(it)
    tube_path(bm, pts, 0.0045 if it < 3 else 0.0042, 4, 3)
    bm.normal_update()
    return bm

def build_lod1():
    bm = bmesh.new()
    lathe(bm, [(0.0, Z_BOT), (0.014, Z_BOT), (0.0165, 0.0), (0.0185, 0.07), (0.0, 0.07)], 6, 0)
    lathe(bm, [(0.0, 0.07), (0.022, 0.082), (0.0255, 0.105), (0.0, 0.126)], 6, 1)
    return bm

def tris(me):
    return sum(len(p.vertices) - 2 for p in me.polygons)

def build_all(it):
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for m in list(bpy.data.meshes):
        bpy.data.meshes.remove(m)
    for m in list(bpy.data.materials):
        bpy.data.materials.remove(m)
    tex = make_textures() if it >= 2 else None
    mats = [mat("Mic_Body", (0.10, 0.10, 0.11), 0.85, 0.38), mat("Mic_Grille", (0.55, 0.56, 0.58), 0.9, 0.32, tex),
            mat("Mic_Accent", (0.75, 0.08, 0.06), 0.1, 0.4), mat("Mic_Cable", (0.02, 0.02, 0.02), 0.0, 0.55),
            mat("Mic_Button", (0.45, 0.46, 0.48), 0.2, 0.5)]
    root = bpy.data.objects.new("prop_mic", None)
    bpy.context.scene.collection.objects.link(root)
    meshes = {}
    for name, bm in (("prop_mic_LOD0", build_body(it)), ("prop_mic_LOD1", build_lod1())):
        me = bpy.data.meshes.new(name)
        bm.to_mesh(me)
        bm.free()
        for m in mats:
            me.materials.append(m)
        for p in me.polygons:
            p.use_smooth = True if it >= 2 else False
        o = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(o)
        o.parent = root
        meshes[name] = (o, me)
    g = bpy.data.objects.new("Mic_Grip", None)
    g.empty_display_type = 'PLAIN_AXES'
    g.empty_display_size = 0.02
    bpy.context.scene.collection.objects.link(g)
    g.parent = root
    return root, meshes

def render(path, root, it, meshes):
    scene = bpy.context.scene
    for e in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE", "BLENDER_WORKBENCH"):
        try:
            scene.render.engine = e
            break
        except TypeError:
            continue
    # куб 10 см для масштабу
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=0.10)
    cm = bpy.data.meshes.new("scale_cube")
    bm.to_mesh(cm)
    bm.free()
    cube = bpy.data.objects.new("scale_cube", cm)
    bpy.context.scene.collection.objects.link(cube)
    cube.location = (0.14, 0, 0.0)
    cube.data.materials.append(bpy.data.materials.new("cube"))
    cube.data.materials[0].diffuse_color = (0.5, 0.5, 0.5, 1)
    # світло
    for i, (loc, en) in enumerate((((0.5, -0.8, 0.6), 60), ((-0.6, -0.3, 0.3), 20))):
        li = bpy.data.lights.new("L%d" % i, 'AREA')
        li.energy = en
        li.size = 0.4
        lo = bpy.data.objects.new("L%d" % i, li)
        lo.location = loc
        bpy.context.scene.collection.objects.link(lo)
        lo.rotation_euler = (-Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
    w = bpy.data.worlds.new("w")
    w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (0.18, 0.19, 0.21, 1)
    scene.world = w
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    bpy.context.scene.collection.objects.link(cam)
    scene.camera = cam
    scene.render.resolution_x, scene.render.resolution_y = 700, 520
    cam.data.type = 'ORTHO'
    # три ракурси: збоку, три чверті, кабель повністю
    views = [("side", (0.0, -1.0, 0.03), 0.30, Vector((0, 0, 0.03))), ("three", (0.7, -0.8, 0.35), 0.30, Vector((0, 0, 0.03))),
             ("cable", (0.0, -3.0, 0.0), 2.6, Vector((0, 0, -0.62)))]
    outs = []
    for tag, loc, scale, tgt in views:
        cam.data.ortho_scale = scale
        cam.location = tgt + Vector(loc) * 2.0
        cam.rotation_euler = (tgt - cam.location).to_track_quat('-Z', 'Y').to_euler()
        scene.render.filepath = path.replace(".png", "_" + tag + ".png")
        bpy.ops.render.render(write_still=True)
        outs.append(scene.render.filepath)
    cube.hide_render = True
    return outs

def export(root, meshes):
    sel = [root] + [o for o, _ in meshes.values()] + [o for o in bpy.data.objects if o.name == "Mic_Grip"]
    bpy.ops.object.select_all(action='DESELECT')
    for o in sel:
        o.select_set(True)
    os.makedirs(OUT_DIR, exist_ok=True)
    path = os.path.join(OUT_DIR, "mic.fbx")
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
                             axis_forward='-Z', axis_up='Y', object_types={'EMPTY', 'MESH'}, mesh_smooth_type='FACE',
                             path_mode='RELATIVE', embed_textures=False, bake_space_transform=False)
    return path

def main():
    bpy.ops.wm.read_homefile(use_empty=True)
    root, meshes = build_all(ITER)
    t0, t1 = tris(meshes["prop_mic_LOD0"][1]), tris(meshes["prop_mic_LOD1"][1])
    cab = sum(1 for p in meshes["prop_mic_LOD0"][1].polygons if p.material_index == 3) * 2
    print(f"[мікрофон] ітерація {ITER}: LOD0 {t0} трикутників (кабель ≈{cab}), LOD1 {t1}", flush=True)
    assert t0 <= 600 and t1 <= 100, "бюджет перевищено"
    export_path = export(root, meshes)
    print("[експорт] " + export_path, flush=True)
    os.makedirs(LOG_DIR, exist_ok=True)
    meshes["prop_mic_LOD1"][0].hide_render = True
    outs = render(os.path.join(LOG_DIR, "iter%d.png" % ITER), root, ITER, meshes)
    print("[знімки] " + ", ".join(outs), flush=True)

main()
