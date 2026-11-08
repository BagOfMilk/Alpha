# Другий прохід у Blender: матеріали з текстур, сокети реквізиту, збереження .blend, експорт FBX і GLB.
#   blender -b --factory-startup --python tools/blender/ring_cast/cast_finish.py -- <репо> <id> <тека роботи> <текстури> <вих. .blend> <вих. .fbx> <вих. .glb> [сокети.json]
import bpy, json, math, os, sys
from mathutils import Matrix, Quaternion, Vector
import numpy as np

a = sys.argv[sys.argv.index("--") + 1:]
REPO, CID, WORK, TEX, OUT_BLEND, OUT_FBX, OUT_GLB = a[:7]
bpy.ops.wm.open_mainfile(filepath=os.path.join(WORK, CID, "stage1.blend"))
res = bpy.data.objects[CID]
arm = bpy.data.objects[CID + "_rig"]


def image(name, srgb=True):
    p = os.path.join(TEX, name)
    im = bpy.data.images.load(p, check_existing=True)
    im.colorspace_settings.name = 'sRGB' if srgb else 'Non-Color'
    return im


def setup(mat, albedo, normal=None, orm=None, alpha=False, double=False):
    t = mat.node_tree
    t.nodes.clear()
    out = t.nodes.new('ShaderNodeOutputMaterial')
    b = t.nodes.new('ShaderNodeBsdfPrincipled')
    t.links.new(b.outputs[0], out.inputs[0])
    tx = t.nodes.new('ShaderNodeTexImage')
    tx.image = image(albedo, True)
    t.links.new(tx.outputs['Color'], b.inputs['Base Color'])
    if alpha:
        t.links.new(tx.outputs['Alpha'], b.inputs['Alpha'])
        mat.blend_method = 'CLIP'
        mat.alpha_threshold = 0.42
        mat.use_backface_culling = False
    b.inputs['Specular IOR Level'].default_value = 0.5
    if orm:
        o = t.nodes.new('ShaderNodeTexImage')
        o.image = image(orm, False)
        sp = t.nodes.new('ShaderNodeSeparateColor')
        t.links.new(o.outputs['Color'], sp.inputs[0])
        t.links.new(sp.outputs[1], b.inputs['Roughness'])
        t.links.new(sp.outputs[2], b.inputs['Metallic'])
    else:
        b.inputs['Roughness'].default_value = 0.82 if alpha else 0.65
    if normal:
        n = t.nodes.new('ShaderNodeTexImage')
        n.image = image(normal, False)
        nm = t.nodes.new('ShaderNodeNormalMap')
        nm.inputs['Strength'].default_value = 1.0
        t.links.new(n.outputs['Color'], nm.inputs['Color'])
        t.links.new(nm.outputs[0], b.inputs['Normal'])
    mat.use_backface_culling = False if double else True


for m in res.data.materials:
    kind = m.name.split("_")[-1]
    if kind == "Body":
        setup(m, CID + "_body_albedo.png", CID + "_body_normal.png", CID + "_body_orm.png")
    elif kind in ("Gear", "Outer"):
        setup(m, CID + "_gear_albedo.png", CID + "_gear_normal.png", CID + "_gear_orm.png", double=True)
    elif kind == "Alpha":
        setup(m, CID + "_alpha_albedo.png", alpha=True)

# Сокети реквізиту: порожні об'єкти на кістках кисті. Реквізит кладеться в них із нульовим локальним зсувом.
# Контракт предмета: початок — центр кулака, +Z уздовж ручки в бік великого пальця, +Y до основної маси, X = Y×Z.
sys.path.insert(0, os.path.join(REPO, "tools", "blender", "ring_cast"))
import cast_spec as CS
PFX = CS.PREFIX


def hand_frame(side):
    S = "Right" if side == "r" else "Left"
    B = arm.data.bones
    h = B[PFX + S + "Hand"].head_local.copy()
    mid = B[PFX + S + "HandMiddle1"].head_local
    idx = B[PFX + S + "HandIndex1"].head_local
    pky = B[PFX + S + "HandPinky1"].head_local
    f = (mid - h).normalized()
    a = idx - pky
    a = (a - f * a.dot(f)).normalized()
    n = a.cross(f) if side == "r" else f.cross(a)
    return h, f, a, n, (mid - h).length


def socket_matrix(side, y_axis, z_axis):
    """y_axis/z_axis — напрямки осей предмета в рамці кисті: 'F' пальці, 'A' великий палець, 'N' долоня (можна з мінусом)."""
    h, f, a, n, d = hand_frame(side)
    ax = dict(F=f, A=a, N=n)
    def v(k):
        return -ax[k[1]] if k[0] == "-" else ax[k]
    Y = v(y_axis).copy()
    Z = v(z_axis).copy()
    X = Y.cross(Z)
    c = h + f * (d * 0.74) + n * (d * 0.30)
    M = Matrix((X, Y, Z)).transposed().to_4x4()
    M.translation = c
    return M


SOCKETS = {
    "belt": ("RightHand", "r", "F", "A"),
    "chair": ("RightHand", "r", "F", "A"),
    "case": ("RightHand", "r", "F", "A"),
    "kendo": ("RightHand", "r", "F", "A"),
    "mic": ("RightHand", "r", "F", "A"),
    "spray_tank": ("LeftHand", "l", "F", "A"),
    "spray_gun": ("RightHand", "r", "F", "A"),
}
wanted = []
for pr in CS.CAST[CID]["props"]:
    wanted += ["spray_tank", "spray_gun"] if pr == "spray" else [pr]
if "mic" not in wanted:
    wanted.append("mic")     # промо є в усіх
def mic_matrix():
    """Хват мікрофона — точно той, з яким записано кліпи промо (рядок # attach у маніфесті)."""
    sex = CS.CAST[CID]["sex"]
    path = os.path.join(REPO, "Assets", "Art", "Animations", "ring_promo%s_manifest.txt" % ("_f" if sex == "f" else ""))
    line = next(l for l in open(path, encoding="utf-8") if l.startswith("# attach"))
    pos = [float(x) for x in line.split("pos=")[1].split()[0].split(",")]
    qq = [float(x) for x in line.split("quat_wxyz=")[1].split()[0].split(",")]
    bone = arm.data.bones[PFX + "RightHand"]
    mid = arm.data.bones[PFX + "RightHandMiddle1"]
    # кисть цього персонажа більша чи менша за кисть скелета кліпів: зсув масштабуємо з нею
    d = np.load(os.path.join(WORK, "..", "clips", "clips_%s.npz" % sex))
    names = [str(x) for x in d["names"]]
    rest = d["rest"]
    kit_len = float(np.linalg.norm(rest[names.index(PFX + "RightHandMiddle1"), :3, 3] - rest[names.index(PFX + "RightHand"), :3, 3]))
    ratio = (mid.head_local - bone.head_local).length / kit_len
    return bone.matrix_local @ Matrix.Translation(Vector(pos) * ratio) @ Quaternion(qq).to_matrix().to_4x4()


for name in wanted:
    bone_short, side, yy, zz = SOCKETS[name]
    bn = PFX + bone_short
    M = mic_matrix() if name == "mic" else socket_matrix(side, yy, zz)
    e = bpy.data.objects.new("Socket_" + name, None)
    e.empty_display_size = 0.05
    bpy.context.scene.collection.objects.link(e)
    e.parent = arm
    e.parent_type = 'BONE'
    e.parent_bone = bn
    bone = arm.data.bones[bn]
    tail_m = bone.matrix_local @ Matrix.Translation((0, bone.length, 0))
    e.matrix_parent_inverse = Matrix.Identity(4)
    e.matrix_basis = tail_m.inverted() @ M



def fit_belt():
    """Пояс згинається через праве плече цього персонажа в позі belt_hold і кріпиться до кисті жорстко: хват збігається з кліпом."""
    props_path = os.path.join(REPO, "ArtSource", "Ring", "Cast", "props.blend")
    sock = bpy.data.objects.get("Socket_belt")
    if sock is None or not os.path.exists(props_path):
        return
    with bpy.data.libraries.load(props_path) as (src, dst):
        dst.objects = [n for n in src.objects if n == "Prop_belt"]
    belt = dst.objects[0] if dst.objects else None
    if belt is None:
        print("НЕМАЄ Prop_belt")
        return
    # поза belt_hold, кадр 0
    sex = CS.CAST[CID]["sex"]
    d = np.load(os.path.join(WORK, "..", "clips", "clips_%s.npz" % sex))
    names = [str(x) for x in d["names"]]
    parents = d["parents"].tolist()
    rest_bones = {n: arm.data.bones[n].matrix_local.copy() for n in names}
    RL = {}
    for i, n in enumerate(names):
        p = parents[i]
        RL[n] = rest_bones[n] if p < 0 else rest_bones[names[p]].inverted() @ rest_bones[n]
    q = d["q/belt_hold"][0]
    h = d["h/belt_hold"][0]
    hip_scale = rest_bones[names[0]].translation.z / float(d["rest"][0][2, 3])
    for pb in arm.pose.bones:
        pb.rotation_mode = 'QUATERNION'
    for j, n in enumerate(names):
        arm.pose.bones[n].rotation_quaternion = RL[n].to_quaternion().inverted() @ Quaternion(q[j].tolist())
    hp = Vector((h * hip_scale).tolist())
    arm.pose.bones[names[0]].location = rest_bones[names[0]].to_3x3().inverted() @ (hp - rest_bones[names[0]].translation)
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    ev = res.evaluated_get(dg)
    em = ev.to_mesh()
    body_slot = [i for i, m in enumerate(res.data.materials) if m.name.endswith("_Body")][0]
    verts = [v.co.copy() for v in em.vertices]
    polys = [tuple(p.vertices) for p in em.polygons if p.material_index == body_slot]
    from mathutils.bvhtree import BVHTree
    bvh = BVHTree.FromPolygons(verts, polys, all_triangles=False)
    ev.to_mesh_clear()
    pb = arm.pose.bones
    J = lambda n: (arm.matrix_world @ pb[PFX + n].head).copy()
    grip = sock.matrix_world.translation.copy()
    up = Vector((0, 0, 1)); back = Vector((0, 1, 0))
    sk = J("RightArm")
    neck = J("Neck")
    shoulder = sk * 0.62 + neck * 0.38 + up * 0.05
    chest_front = J("Spine2") * 0.5 + grip * 0.5
    plate = grip + up * 0.170 - back * 0.01
    pts = [grip - up * 0.62 - back * 0.02, grip - up * 0.32, grip - up * 0.10, grip, plate, plate * 0.45 + shoulder * 0.55 - back * 0.05, shoulder, shoulder + back * 0.10 - up * 0.08, shoulder + back * 0.13 - up * 0.30, shoulder + back * 0.12 - up * 0.60]
    # сплайн Катмулла — Рома
    def cr(p0, p1, p2, p3, t):
        return 0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t * t * t)
    poly = []
    P = [pts[0]] + pts + [pts[-1]]
    for i in range(1, len(P) - 2):
        for k in range(20):
            poly.append(cr(P[i - 1], P[i], P[i + 1], P[i + 2], k / 20.0))
    poly.append(pts[-1].copy())
    # відсунути від тіла: стрічка лежить на грудях і плечі, не всередині
    N = [Vector((0, -1, 0))] * len(poly)
    for it in range(6):
        for i, c in enumerate(poly):
            loc, nor, idx, dist = bvh.find_nearest(c, 0.5)
            if loc is None:
                continue
            if nor.dot(c - loc) < 0 or dist < 0.016:
                poly[i] = loc + nor * 0.016
            N[i] = nor
        for i in range(1, len(poly) - 1):
            poly[i] = poly[i] * 0.5 + (poly[i - 1] + poly[i + 1]) * 0.25
    # довжина вздовж шляху від центру пластини
    L = [0.0]
    for i in range(1, len(poly)):
        L.append(L[-1] + (poly[i] - poly[i - 1]).length)
    ic = min(range(len(poly)), key=lambda i: (poly[i] - plate).length)
    s0 = L[ic]
    def at(s):
        x = s0 + s
        x = max(L[0], min(L[-1] - 1e-5, x))
        lo = 0
        while L[lo + 1] < x:
            lo += 1
        u = (x - L[lo]) / max(L[lo + 1] - L[lo], 1e-6)
        pos = poly[lo].lerp(poly[lo + 1], u)
        tan = (poly[min(lo + 4, len(poly) - 1)] - poly[max(lo - 3, 0)]).normalized()
        # ширина стрічки тримається поперек тіла: стрічка лежить плоско й не скручується; на плечі вона нахилена за схилом
        slope = math.radians(22.0) * min(abs(tan.y) * 1.2, 1.0)
        wide = Vector((math.cos(slope), 0.0, math.sin(slope)))
        nrm = wide.cross(tan).normalized()
        return pos, tan, nrm
    me = belt.data.copy()
    inv = sock.matrix_world.inverted()
    for v in me.vertices:
        x, y, z = v.co
        pos, tan, nrm = at(z)
        side = tan.cross(nrm)
        v.co = inv @ (pos + side * x + nrm * y)
    me.update()
    bpy.data.objects.remove(belt, do_unlink=True)
    fit = bpy.data.objects.new("Prop_belt", me)
    bpy.context.scene.collection.objects.link(fit)
    fit.parent = sock
    fit.matrix_parent_inverse = Matrix.Identity(4)
    fit.matrix_basis = Matrix.Identity(4)
    # поза спокою назад
    for pbn in arm.pose.bones:
        pbn.rotation_quaternion = Quaternion()
        pbn.location = Vector()
    bpy.context.view_layer.update()
    print("BELT OK path", round(L[-1], 3), "plate", tuple(round(x, 3) for x in plate), flush=True)


if "belt" in wanted:
    fit_belt()

for p in (OUT_BLEND, OUT_FBX, OUT_GLB):
    os.makedirs(os.path.dirname(p), exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=OUT_BLEND, relative_remap=True)
bpy.ops.object.select_all(action='DESELECT')
arm.select_set(True)
res.select_set(True)
for o in bpy.data.objects:
    if o.name.startswith(("Socket_", "Prop_")):
        o.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.export_scene.fbx(filepath=OUT_FBX, use_selection=True, object_types={'ARMATURE', 'MESH', 'EMPTY'}, apply_unit_scale=True,
                         apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y', add_leaf_bones=False, primary_bone_axis='Y',
                         secondary_bone_axis='X', bake_anim=False, mesh_smooth_type='FACE', use_tspace=True, path_mode='RELATIVE', use_mesh_modifiers=False)
print("FBX OK", os.path.getsize(OUT_FBX) // 1024, "KB", flush=True)
bpy.ops.export_scene.gltf(filepath=OUT_GLB, export_format='GLB', use_selection=True, export_animations=False, export_skins=True, export_yup=True,
                          export_apply=False, export_morph=False, export_materials='EXPORT', export_image_format='WEBP', export_image_quality=88,
                          export_tangents=False, export_normals=True, export_texcoords=True, export_def_bones=False)
print("GLB OK", os.path.getsize(OUT_GLB) // 1024, "KB", flush=True)
