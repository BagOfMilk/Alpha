# Жіноча сцена лабораторії (04.11.2026): з чоловічої Logs/ring_anims/lab_scene.blend — риги Rig_Red / Rig_Blue і їхні
# сітки Red_* / Blue_* замінюються набором kit_f (ті самі назви, та сама видимість). Арена й якорі лишаються.
#   blender -b Logs/ring_anims/lab_scene.blend --python tools/blender/ring_lab_scene_f.py
# Пише Logs/ring_anims/lab_scene_f.blend; лабораторія на ній розв'язує жіночі пари в tools/blender/lab_clips_f.
import bpy, os

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
KIT = os.path.join(REPO, "Assets", "Art", "Characters", "Kit", "kit_f.fbx")

for side in ("Red", "Blue"):
    rig = bpy.data.objects["Rig_" + side]
    mw = rig.matrix_world.copy()
    shown = {o.name[len(side) + 1:] for o in bpy.data.objects
             if o.name.startswith(side + "_") and not o.hide_get() and not o.hide_viewport}
    for o in [o for o in bpy.data.objects if o.name.startswith(side + "_")] + [rig]:
        bpy.data.objects.remove(o, do_unlink=True)
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=KIT, primary_bone_axis='Y', secondary_bone_axis='X',
                             automatic_bone_orientation=False, ignore_leaf_bones=False)
    new = [o for o in bpy.data.objects if o not in before]
    arm = next(o for o in new if o.type == 'ARMATURE')
    arm.name = "Rig_" + side
    arm.matrix_world = mw
    for o in new:
        if o is arm:
            continue
        base = o.name.split(".")[1] if o.name.startswith("kit_f.") else o.name.split(".")[0]   # «kit_f.body_torso.001»
        o.name = side + "_" + base
        vis = base in shown
        o.hide_set(not vis)
        o.hide_viewport = not vis
        o.hide_render = not vis
    print(f"[сцена f] {side}: {len(new)} об'єктів, видимі {sorted(shown)}", flush=True)

out = os.path.join(REPO, "Logs", "ring_anims", "lab_scene_f.blend")
bpy.ops.wm.save_as_mainfile(filepath=out)
print("[сцена f] збережено", out, flush=True)
