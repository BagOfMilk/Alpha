# Перевірка довгого одягу: чи не протикає тіло тканину в кліпах. Міряє вершини ніг і рук, що опинились назовні тканини (не біля її краю).
#   blender -b --factory-startup --python tools/blender/ring_cast/cast_cloth_check.py -- <файл .blend> <clips_x.npz> <clips_x.json> <вихід.json> [кліпи через кому] [--step N]
# Поріг 8 мм: тонші виходи не видно. Ребра близько до краю (розріз, поділ) не рахуються: там нога виходить законно.
import bpy, bmesh, json, os, sys
import numpy as np
from mathutils import Matrix, Quaternion, Vector
from mathutils.bvhtree import BVHTree

a = sys.argv[sys.argv.index("--") + 1:]
BLEND, NPZ, META, OUT = a[:4]
CLIPS = a[4].split(",") if len(a) > 4 and not a[4].startswith("--") else ["U_Walk_Loop", "Run_Loop", "Run_Sprint_Loop", "Kneel_Idle_Loop", "Sit_Mat_Idle_Loop", "Lying_Back_Loop", "Bump_Back", "GetUp_Back", "Ring_Stance_Loop", "Move_Walk_Fwd_Loop", "Rope_Back_L", "Entrance_Climb_Steps"]
STEP = int(a[a.index("--step") + 1]) if "--step" in a else 3

bpy.ops.wm.open_mainfile(filepath=BLEND)
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
mesh = next(o for o in bpy.data.objects if o.type == 'MESH' and o.parent == arm)
mats = [m.name.split("_")[-1] for m in mesh.data.materials]
d = np.load(NPZ)
names = [str(x) for x in d["names"]]
parents = d["parents"].tolist()
meta = json.load(open(META, encoding="utf-8"))
R = {n: arm.data.bones[n].matrix_local.copy() for n in names}
RL = {}
for i, n in enumerate(names):
    p = parents[i]
    RL[n] = R[n] if p < 0 else R[names[p]].inverted() @ R[n]
RLq = {n: RL[n].to_quaternion() for n in names}
hips = names[0]
hip_rest_kit = float(d["rest"][0][2, 3])
hip_scale = R[hips].translation.z / hip_rest_kit
for pb in arm.pose.bones:
    pb.rotation_mode = 'QUATERNION'
arm.animation_data_clear()

cloth_idx = [i for i, m in enumerate(mats) if m in ("Outer", "Gear")]
body_idx = mats.index("Body")
me = mesh.data
poly_mat = np.zeros(len(me.polygons), dtype=np.int32)
me.polygons.foreach_get("material_index", poly_mat)
cloth_polys = [p.index for p in me.polygons if poly_mat[p.index] in cloth_idx]
if not cloth_polys:
    json.dump({"note": "одягу немає"}, open(OUT, "w", encoding="utf-8"), ensure_ascii=False)
    print("NO CLOTH")
    sys.exit(0)
body_verts = sorted({v for p in me.polygons if poly_mat[p.index] == body_idx for v in p.vertices})
cloth_verts = sorted({v for pi in cloth_polys for v in me.polygons[pi].vertices})
# вершини краю тканини та їхня околиця
bm = bmesh.new(); bm.from_mesh(me); bm.edges.ensure_lookup_table(); bm.verts.ensure_lookup_table(); bm.faces.ensure_lookup_table()
cset = set(cloth_polys)
edge_verts = set()
for e in bm.edges:
    fs = [f.index for f in e.link_faces if f.index in cset]
    if len(fs) == 1:
        edge_verts.update(v.index for v in e.verts)
bm.free()
rest = np.zeros(len(me.vertices) * 3); me.vertices.foreach_get("co", rest); rest = rest.reshape(-1, 3)
limb_groups = {i: g.name for i, g in enumerate(mesh.vertex_groups)}
limb = []
for v in body_verts:
    w = 0.0
    for g in me.vertices[v].groups:
        n = limb_groups[g.group]
        if any(k in n for k in ("LeftLeg", "RightLeg", "LeftFoot", "RightFoot", "LeftUpLeg", "RightUpLeg")):
            w += g.weight
    if w > 0.4:
        limb.append(v)
limb = np.array(limb)
print("cloth polys", len(cloth_polys), "limb verts", len(limb), "edge verts", len(edge_verts), flush=True)

res = {}
for clip in CLIPS:
    if "q/" + clip not in d.files:
        continue
    q = d["q/" + clip]; h = d["h/" + clip]
    F = q.shape[0]
    worst = (0, -1); total = 0; samples = 0
    per = []
    for f in range(0, F, STEP):
        for j, n in enumerate(names):
            pb = arm.pose.bones[n]
            pb.rotation_quaternion = RLq[n].inverted() @ Quaternion(q[f, j].tolist())
        hp = Vector((h[f] * hip_scale).tolist())
        arm.pose.bones[hips].location = R[hips].to_3x3().inverted() @ (hp - R[hips].translation)
        bpy.context.view_layer.update()
        dg = bpy.context.evaluated_depsgraph_get()
        ev = mesh.evaluated_get(dg)
        em = ev.to_mesh()
        co = np.zeros(len(em.vertices) * 3); em.vertices.foreach_get("co", co); co = co.reshape(-1, 3)
        tris = [tuple(em.polygons[pi].vertices) for pi in cloth_polys]
        bvh = BVHTree.FromPolygons([Vector(x) for x in co.tolist()], tris, all_triangles=False)
        ev.to_mesh_clear()
        cnt = 0
        for v in limb:
            p = Vector(co[v].tolist())
            r = bvh.find_nearest(p, 0.12)
            if r[0] is None:
                continue
            loc, nrm, ti, dist = r
            if (p - loc).dot(nrm) > 0.008 and dist > 0.008:
                # біля краю тканини — законний вихід
                vs = tris[ti]
                if any(x in edge_verts for x in vs):
                    continue
                cnt += 1
        per.append(cnt)
        total += cnt; samples += 1
        if cnt > worst[0]:
            worst = (cnt, f)
    res[clip] = dict(frames=F, worst_verts=int(worst[0]), worst_frame=int(worst[1]), mean=round(total / max(samples, 1), 2))
    print(clip, res[clip], flush=True)
json.dump(res, open(OUT, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
