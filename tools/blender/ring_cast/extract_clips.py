# Витягує кліпи з FBX у проміжний формат (.npz): матриці кісток у просторі арматури покадрово.
#
#   blender -b --factory-startup --python tools/blender/ring_cast/extract_clips.py -- <репо> <fbx відносно репо> <вихід.npz>
#
# Далі кліпи обробляє tools/blender/ring_cast/clips_build.py (звичайний Python + numpy): перенесення на mixamorig,
# зняття зміщення, цикли, переходи. Blender тут лише читає FBX, щоб обробка не залежала від нього.
import bpy, os, sys, json
import numpy as np

a = sys.argv[sys.argv.index("--") + 1:]
REPO, SRC, OUT = a[0], a[1], a[2]

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=os.path.join(REPO, SRC), use_anim=True, automatic_bone_orientation=False,
                         ignore_leaf_bones=False, primary_bone_axis='Y', secondary_bone_axis='X')
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
scene = bpy.context.scene
fps = scene.render.fps
names = [b.name for b in arm.data.bones]
parents = [names.index(b.parent.name) if b.parent else -1 for b in arm.data.bones]
rest = np.array([np.array(b.matrix_local) for b in arm.data.bones], dtype=np.float32)
pbones = [arm.pose.bones[n] for n in names]
if arm.animation_data is None:
    arm.animation_data_create()

out = {"names": np.array(names), "parents": np.array(parents, dtype=np.int32), "rest": rest, "fps": np.array(fps)}
index = []
for act in sorted(bpy.data.actions, key=lambda x: x.name):
    clip = act.name.split("|")[-1]
    arm.animation_data.action = act
    # Blender 4.4+: дія без призначеного слота нічого не анімує (поза лишається незмінною).
    if getattr(act, "slots", None):
        arm.animation_data.action_slot = act.slots[0]
    f0, f1 = int(round(act.frame_range[0])), int(round(act.frame_range[1]))
    frames = np.zeros((f1 - f0 + 1, len(names), 4, 4), dtype=np.float32)
    for i, f in enumerate(range(f0, f1 + 1)):
        scene.frame_set(f)
        for j, pb in enumerate(pbones):
            frames[i, j] = np.array(pb.matrix)
    out["clip/" + clip] = frames
    index.append(clip)
    print("CLIP", clip, f1 - f0 + 1, flush=True)
np.savez_compressed(OUT, **out)
print("DONE", SRC, len(index), "fps", fps, flush=True)
