# Перевірка стану мешу тіла: шви між зонами, дірки, зріст. Лише читання, нічого не пише.
#   blender -b --factory-startup --python tools/blender/ring_cast/probe_body.py -- <репо> body_m_nordic
import bpy, bmesh, os, sys
from mathutils import Vector

a = sys.argv[sys.argv.index("--") + 1:]
REPO, BODY = a[0], a[1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=os.path.join(REPO, "Assets/Art/Characters/Kit", BODY + ".fbx"), use_anim=False,
                         automatic_bone_orientation=False, ignore_leaf_bones=False, primary_bone_axis='Y', secondary_bone_axis='X')
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
print("ARM loc", tuple(round(x, 3) for x in arm.location), "matrix pelvis", tuple(round(x, 3) for x in arm.data.bones['pelvis'].head_local))
parts = [o for o in bpy.data.objects if o.type == 'MESH']
for o in parts:
    print("PART", o.name, "parent", o.parent.name if o.parent else None, "loc", tuple(round(x, 3) for x in o.location),
          "worldpt", tuple(round(x, 3) for x in (o.matrix_world @ o.data.vertices[0].co)), "mods", [m.type for m in o.modifiers])
bm = bmesh.new()
for o in parts:
    if ".body_" in o.name:
        me = o.data.copy()
        me.transform(o.matrix_world)
        bm.from_mesh(me)
print("BEFORE weld: verts", len(bm.verts), "faces", len(bm.faces))
for d in (1e-5, 1e-4, 5e-4, 2e-3):
    b2 = bm.copy()
    bmesh.ops.remove_doubles(b2, verts=b2.verts, dist=d)
    bd = [e for e in b2.edges if e.is_boundary]
    nm = [e for e in b2.edges if len(e.link_faces) > 2]
    print("weld", d, "verts", len(b2.verts), "boundary edges", len(bd), "nonmanifold>2", len(nm))
    if d == 5e-4:
        seen = set(); loops = []
        for e in bd:
            if e in seen: continue
            stack = [e]; L = []
            while stack:
                x = stack.pop()
                if x in seen: continue
                seen.add(x); L.append(x)
                for v in x.verts:
                    for y in v.link_edges:
                        if y.is_boundary and y not in seen: stack.append(y)
            c = sum((v.co for x in L for v in x.verts), Vector()) / (2 * len(L))
            per = sum(x.calc_length() for x in L)
            loops.append((len(L), round(per, 3), tuple(round(k, 3) for k in c)))
        print("LOOPS", sorted(loops, key=lambda t: -t[1]))
        zs = [v.co.z for v in b2.verts]; print("z range", round(min(zs), 3), round(max(zs), 3))
        # окремі шматки
        vis = set(); comps = 0
        for v in b2.verts:
            if v in vis: continue
            comps += 1; st = [v]
            while st:
                q = st.pop()
                if q in vis: continue
                vis.add(q)
                for e in q.link_edges: st.append(e.other_vert(q))
        print("components", comps)
