# Записує бібліотеку кліпів у Blender на арматуру mixamorig, зберігає .blend і експортує FBX (Unity) та GLB (веб).
#   blender -b --factory-startup --python tools/blender/ring_cast/clips_write.py -- <clips_x.npz> <clips_x.json> <вихід.blend> <вихід.fbx> <вихід.glb> [--only A,B]
import bpy, json, os, sys
import numpy as np
from mathutils import Matrix, Quaternion, Vector

a = sys.argv[sys.argv.index("--") + 1:]
NPZ, META, OUT_BLEND, OUT_FBX, OUT_GLB = a[:5]
ONLY = set(a[a.index("--only") + 1].split(",")) if "--only" in a else None
FPS = 30

d = np.load(NPZ)
names = [str(x) for x in d["names"]]
parents = d["parents"].tolist()
rest = d["rest"].astype(np.float64)
meta = json.load(open(META, encoding="utf-8"))

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.render.fps = FPS
arm_data = bpy.data.armatures.new("Skeleton")
arm = bpy.data.objects.new("Skeleton", arm_data)
scene.collection.objects.link(arm)
bpy.context.view_layer.objects.active = arm
arm.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')
kids = {}
for i, p in enumerate(parents):
    kids.setdefault(p, []).append(i)
for i, n in enumerate(names):
    eb = arm_data.edit_bones.new(n)
    c = kids.get(i, [])
    L = np.linalg.norm(rest[c[0], :3, 3] - rest[i, :3, 3]) if c else 0.05
    eb.head = (0, 0, 0)
    eb.tail = (0, max(float(L), 0.02), 0)
    eb.matrix = Matrix(rest[i].tolist())
for i, p in enumerate(parents):
    if p >= 0:
        arm_data.edit_bones[names[i]].parent = arm_data.edit_bones[names[p]]
bpy.ops.object.mode_set(mode='OBJECT')

# Поза спокою бонів у просторі арматури, після всіх округлень Blender
R = {n: arm_data.bones[n].matrix_local.copy() for n in names}
RL = {}
for i, n in enumerate(names):
    p = parents[i]
    RL[n] = R[n] if p < 0 else R[names[p]].inverted() @ R[n]
RLq = np.array([list(RL[n].to_quaternion()) for n in names])

def qmul(a, b):
    w1, x1, y1, z1 = np.moveaxis(a, -1, 0)
    w2, x2, y2, z2 = np.moveaxis(b, -1, 0)
    return np.stack([w1*w2-x1*x2-y1*y2-z1*z2, w1*x2+x1*w2+y1*z2-z1*y2, w1*y2-x1*z2+y1*w2+z1*x2, w1*z2+x1*y2-y1*x2+z1*w2], -1)

RLinv = RLq * np.array([1, -1, -1, -1])
hip_rest_inv = np.array(R[names[0]].inverted().to_3x3())
hip_rest_p = np.array(R[names[0]].translation)

arm.animation_data_create()
arm.rotation_mode = 'QUATERNION'
count = 0
keep = [k for k in meta if (ONLY is None or k in ONLY)]
for clip in keep:
    lq = d["q/" + clip].astype(np.float64)
    h = d["h/" + clip].astype(np.float64)
    F = lq.shape[0]
    basis = qmul(RLinv[None], lq)                        # F,J,4 pose_basis
    # безперервність знаку
    for f in range(1, F):
        s = np.sum(basis[f] * basis[f-1], axis=-1) < 0
        basis[f][s] *= -1
    loc = (hip_rest_inv @ (h - hip_rest_p).T).T
    act = bpy.data.actions.new(clip)
    act.use_fake_user = True
    frames = np.arange(0, F, dtype=np.float32)
    def put(path, idx, vals, group):
        fc = act.fcurves.new(path, index=idx, action_group=group)
        fc.keyframe_points.add(F)
        co = np.empty(F * 2, dtype=np.float32)
        co[0::2] = frames
        co[1::2] = vals
        fc.keyframe_points.foreach_set("co", co)
        fc.keyframe_points.foreach_set("interpolation", np.full(F, 1, dtype=np.int32))   # LINEAR
        fc.update()
    for j, n in enumerate(names):
        path = 'pose.bones["%s"].rotation_quaternion' % n
        for c in range(4):
            put(path, c, basis[:, j, c], n)
    path = 'pose.bones["%s"].location' % names[0]
    for c in range(3):
        put(path, c, loc[:, c], names[0])
    count += 1
    if count % 50 == 0:
        print("actions", count, flush=True)
print("ACTIONS", count, flush=True)
for pb in arm.pose.bones:
    pb.rotation_mode = 'QUATERNION'

# Призначаємо кожну дію, щоб вона мала слот для цієї арматури (Blender 4.4+)
for act in bpy.data.actions:
    arm.animation_data.action = act
    if getattr(act, "slots", None) and len(act.slots):
        arm.animation_data.action_slot = act.slots[0]
arm.animation_data.action = None
# Експортер glTF бере дії лише з доріжок NLA: кожна дія на своїй доріжці.
for act in bpy.data.actions:
    tr = arm.animation_data.nla_tracks.new()
    tr.name = act.name
    st = tr.strips.new(act.name, 0, act)
    if getattr(act, "slots", None) and len(act.slots) and hasattr(st, "action_slot"):
        st.action_slot = act.slots[0]
    tr.mute = True
scene.frame_start = 0
scene.frame_end = 1

for p in (OUT_BLEND, OUT_FBX, OUT_GLB):
    if p != "-":
        os.makedirs(os.path.dirname(p), exist_ok=True)
if OUT_BLEND != "-":
    bpy.ops.wm.save_as_mainfile(filepath=OUT_BLEND)
bpy.ops.object.select_all(action='DESELECT')
arm.select_set(True)
bpy.context.view_layer.objects.active = arm
if OUT_FBX != "-":
    bpy.ops.export_scene.fbx(filepath=OUT_FBX, use_selection=True, object_types={'ARMATURE'}, apply_unit_scale=True,
                             apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y', add_leaf_bones=False,
                             primary_bone_axis='Y', secondary_bone_axis='X', bake_anim=True, bake_anim_use_all_actions=True,
                             bake_anim_use_nla_strips=False, bake_anim_use_all_bones=True, bake_anim_force_startend_keying=True,
                             bake_anim_step=1.0, bake_anim_simplify_factor=0.0)
    print("FBX OK", os.path.getsize(OUT_FBX) // 1048576, "MB", flush=True)
if OUT_GLB != "-":
    bpy.ops.export_scene.gltf(filepath=OUT_GLB, export_format='GLB', use_selection=True, export_animations=True,
                              export_animation_mode='ACTIONS', export_force_sampling=True, export_frame_step=1,
                              export_anim_single_armature=False, export_optimize_animation_size=True, export_skins=True,
                              export_yup=True, export_apply=False, export_morph=False, export_materials='NONE', export_def_bones=False)
    print("GLB OK", os.path.getsize(OUT_GLB) // 1048576, "MB", flush=True)
