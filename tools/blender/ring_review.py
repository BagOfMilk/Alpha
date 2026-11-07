# Огляд УСІХ кліпів набору очима (власник 04.11.2026: «ДИВИСЯ ВСЕ»): кожен кліп з маніфесту — пара на маті за
# зсувом із маніфесту, два ракурси (збоку й спереду, камера йде за серединою пари), кожен step-й кадр; лист кадрів на
# кліп: кадри в Logs/review<_f>/<кліп>/, лист складає tools/blender/ring_review_sheet.py (у Blender немає PIL). Без арени й рефері поруч — нічого не закриває дію.
#   blender -b --python tools/blender/ring_review.py -- <репо> [--female] [--step 4] [--only Ім'я,…]
import bpy, os, sys, math
from mathutils import Vector, Matrix

args = sys.argv[sys.argv.index("--") + 1:]
REPO = args[0]
FEMALE = "--female" in args
STEP = int(args[args.index("--step") + 1]) if "--step" in args else 4
ONLY = set(args[args.index("--only") + 1].split(",")) if "--only" in args else None
SFX = "_f" if FEMALE else ""
ZOOM = float(args[args.index("--zoom") + 1]) if "--zoom" in args else 1.0   # <1 — ближче (контакти)
DIRS = int(args[args.index("--dirs") + 1]) if "--dirs" in args else 2      # 4 — ще ззаду й зліва
ANIM = args[args.index("--anim") + 1] if "--anim" in args else os.path.join(REPO, "Assets", "Art", "Animations")
OUT = args[args.index("--out") + 1] if "--out" in args else os.path.join(REPO, "Logs", "review" + SFX)
os.makedirs(OUT, exist_ok=True)

bpy.ops.wm.read_homefile(use_empty=True)
sc = bpy.context.scene


def load_rig(tag):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=os.path.join(REPO, "Assets", "Art", "Characters", "Kit", "kit_f.fbx" if FEMALE else "kit_m.fbx"),
                             primary_bone_axis='Y', secondary_bone_axis='X', automatic_bone_orientation=False,
                             ignore_leaf_bones=False)
    new = [o for o in bpy.data.objects if o not in before]
    arm = next(o for o in new if o.type == 'ARMATURE')
    arm.name = "Rev_" + tag
    mat = bpy.data.materials.new("rev_" + tag)
    mat.diffuse_color = (0.85, 0.45, 0.4, 1) if tag == "A" else (0.45, 0.6, 0.9, 1)
    for o in new:
        if o.type != 'MESH':
            continue
        show = ".body_" in o.name or "body_" in o.name.split(".")[-1]
        o.hide_render = o.hide_viewport = not show
        if show:
            o.data.materials.clear(); o.data.materials.append(mat)
    for pb in arm.pose.bones:
        pb.rotation_mode = 'QUATERNION'
    # дії FBX несуть ключі положення самого ригу — зсув пари ставиться батьківському порожньому, не ригу
    holder = bpy.data.objects.new("Hold_" + tag, None)
    sc.collection.objects.link(holder)
    arm.parent = holder
    arm["holder"] = holder.name
    return arm


A, B = load_rig("A"), load_rig("B")
HA, HB = bpy.data.objects["Hold_A"], bpy.data.objects["Hold_B"]
acts_before = set(bpy.data.actions)
before = set(bpy.data.objects)
bpy.ops.import_scene.fbx(filepath=os.path.join(ANIM, "RING_Moves%s.fbx" % ("_F" if FEMALE else "")))
for o in [o for o in bpy.data.objects if o not in before]:
    bpy.data.objects.remove(o, do_unlink=True)
ACTS = {}
for a in bpy.data.actions:
    if a in acts_before:
        continue
    ACTS[a.name.split("|")[-1].split(".")[0]] = a

bpy.ops.mesh.primitive_plane_add(size=8)
cam = bpy.data.objects.new("RevCam", bpy.data.cameras.new("RevCam"))
sc.collection.objects.link(cam)
sc.camera = cam
cam.data.lens = 50
r = sc.render
r.resolution_x = r.resolution_y = int(args[args.index("--res") + 1]) if "--res" in args else 240
try:
    r.engine = 'BLENDER_WORKBENCH'
except TypeError:
    pass
sc.display.shading.color_type = 'MATERIAL'
r.fps = 30


def assign(arm, act):
    ad = arm.animation_data or arm.animation_data_create()
    ad.action = act
    if act is not None and hasattr(ad, "action_slot") and getattr(act, "slots", None):
        ad.action_slot = act.slots[0]


rows = []
for line in open(os.path.join(ANIM, "ring_anims%s_manifest.txt" % SFX), encoding="utf-8"):
    if line.startswith("#") or not line.strip():
        continue
    p = line.split()
    if p[1] == "B":
        continue
    rows.append(p)

done = 0
for p in rows:
    name, role, length = p[0], p[1], float(p[2])
    base = name[:-2] if role == "A" else name
    if ONLY and base not in ONLY and name not in ONLY:
        continue
    act_a = ACTS.get(name)
    if act_a is None:
        print("[огляд] немає дії", name, flush=True)
        continue
    pair = role == "A"
    assign(A, act_a)
    HA.matrix_world = Matrix.Identity(4)
    if pair:
        off = Vector((float(p[4]), float(p[5]), float(p[6])))
        assign(B, ACTS.get(base + "_B"))
        HB.matrix_world = Matrix.Translation(off) @ Matrix.Rotation(math.radians(float(p[7])), 4, 'Z')
    else:
        assign(B, None)
        HB.matrix_world = Matrix.Translation(Vector((0, 60, 0)))
    f0, f1 = int(act_a.frame_range[0]), int(act_a.frame_range[1])
    if "--dbg" in args:
        sc.frame_set(f0)
        pa = A.matrix_world @ A.pose.bones["pelvis"].head; pb_ = B.matrix_world @ B.pose.bones["pelvis"].head
        ha = A.matrix_world @ A.pose.bones["head"].head; fa = (A.matrix_world.to_3x3() @ A.pose.bones["pelvis"].matrix.to_3x3()).col
        print(f"[dbg] {base} A таз {tuple(round(x,2) for x in pa)} B таз {tuple(round(x,2) for x in pb_)}", flush=True)
    frames = list(range(f0, f1 + 1, STEP))
    if frames[-1] != f1:
        frames.append(f1)
    tiles = []
    # камера нерухома на весь кліп (04.11.2026: камера за парою ховала переміщення — відтиск до канатів на 1 м
    # виглядав як стояння на місці); центр і відстань — з розкиду таза по всьому кліпу
    pts = []
    for f in frames:
        sc.frame_set(f)
        pts.append(A.matrix_world @ A.pose.bones["spine_02"].head)
        if pair:
            pts.append(B.matrix_world @ B.pose.bones["spine_02"].head)
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), 0)); hi = Vector((max(p.x for p in pts), max(p.y for p in pts), 0))
    c0 = (lo + hi) / 2; c0.z = 0.8
    span = max((hi - lo).length, 0.8)
    for ai, d in enumerate((Vector((1, 0, 0)), Vector((0, -1, 0)), Vector((-1, 0, 0)), Vector((0, 1, 0)))[:DIRS]):
        for f in frames:
            sc.frame_set(f)
            c = c0.copy()
            if not pair:                               # сольний кліп з ходом (біг, канати) — камера за тазом, інакше виходить з кадру
                pp = A.matrix_world @ A.pose.bones["spine_02"].head
                c = Vector((pp.x, pp.y, 0.8))
            cam.location = c + d * (((3.3 + span) if pair else 3.4) * ZOOM) + Vector((0, 0, 0.25))
            cam.rotation_euler = (c - cam.location).to_track_quat('-Z', 'Y').to_euler()
            os.makedirs(os.path.join(OUT, base), exist_ok=True)
            fp = os.path.join(OUT, base, f"{ai}_{f - f0:04d}.png")
            r.filepath = fp
            bpy.ops.render.render(write_still=True)
            tiles.append((ai, f, fp))
    done += 1
    print(f"[огляд] {base}: {len(frames)} кадрів", flush=True)
print(f"[огляд] готово листів: {done}", flush=True)
