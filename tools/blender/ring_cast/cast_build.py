# Збирає персонажа «Рингу» в Blender: один цільний меш, спільний скелет mixamorig, зріст, дані для малювання текстур.
#
#   blender -b --factory-startup --python tools/blender/ring_cast/cast_build.py -- <репо> <id> <тека роботи> <clips_x.npz>
#
# Виходи в <тека роботи>/<id>/: paint.npz (дані для cast_paint.py), stage1.blend (меш і скелет без текстур), report.json.
import bpy, bmesh, json, math, os, sys
import numpy as np
from mathutils import Matrix, Vector, kdtree

a = sys.argv[sys.argv.index("--") + 1:]
REPO, CID, WORK, CLIPS = a[0], a[1], a[2], a[3]
HERE = os.path.join(REPO, "tools", "blender", "ring_cast")
sys.path.insert(0, HERE)
import cast_spec as CS
import cast_shapes as SH
import cast_outfits as OF
import cast_garments as GR

SPEC = CS.CAST[CID]
SEX = SPEC["sex"]
OUT = os.path.join(WORK, CID)
os.makedirs(OUT, exist_ok=True)
REPORT = {"id": CID}
EYE_RIMS = []     # ребра краю очниці: звідси лінія повіки на шкірі

KIT = os.path.join(REPO, "Assets", "Art", "Characters", "Kit")
ARM_BONES = ("clavicle", "upperarm", "lowerarm", "hand", "index", "middle", "ring", "pinky", "thumb")
LEG_BONES = ("thigh", "calf", "foot", "ball")


def import_fbx(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path, use_anim=False, automatic_bone_orientation=False, ignore_leaf_bones=False,
                             primary_bone_axis='Y', secondary_bone_axis='X')
    return [o for o in bpy.data.objects if o not in before]


def activate(o):
    bpy.ops.object.mode_set(mode='OBJECT') if bpy.context.object and bpy.context.object.mode != 'OBJECT' else None
    bpy.ops.object.select_all(action='DESELECT')
    o.select_set(True)
    bpy.context.view_layer.objects.active = o


def join(objs, name):
    activate(objs[0])
    for o in objs[1:]:
        o.select_set(True)
    bpy.ops.object.join()
    r = bpy.context.view_layer.objects.active
    r.name = name
    r.data.name = name
    return r


def weights_dense(o):
    names = [g.name for g in o.vertex_groups]
    W = np.zeros((len(o.data.vertices), len(names)), dtype=np.float32)
    for v in o.data.vertices:
        for g in v.groups:
            W[v.index, g.group] = g.weight
    return names, W


def get_co(me):
    c = np.zeros(len(me.vertices) * 3)
    me.vertices.foreach_get("co", c)
    return c.reshape(-1, 3)


def set_co(me, P):
    me.vertices.foreach_set("co", P.reshape(-1))
    me.update()


def get_normals(me):
    n = np.zeros(len(me.vertices) * 3)
    me.vertices.foreach_get("normal", n)
    return n.reshape(-1, 3)


def bone_table(arm):
    """Голова й хвіст кожної кістки (хвіст = голова першої змістовної дитини)."""
    T = {}
    for b in arm.data.bones:
        T[b.name] = dict(head=list(b.head_local), tail=list(b.tail_local))
    follow = {"thigh": "calf", "calf": "foot", "upperarm": "lowerarm", "lowerarm": "hand", "foot": "ball"}
    for s in ("l", "r"):
        for x, y in follow.items():
            T[x + "_" + s]["tail"] = T[y + "_" + s]["head"]
    T["neck_01"]["tail"] = T["head"]["head"]
    return T


def region_fields(names, W):
    idx = lambda pred: [i for i, n in enumerate(names) if pred(n)]
    arm = W[:, idx(lambda n: n.startswith(ARM_BONES) and not n.startswith("clavicle"))].sum(1)
    leg = W[:, idx(lambda n: n.startswith(LEG_BONES))].sum(1)
    head = W[:, idx(lambda n: n in ("head", "neck_01"))].sum(1)
    return dict(arm=np.clip(arm, 0, 1).astype(np.float64), leg=np.clip(leg, 0, 1).astype(np.float64), head=np.clip(head, 0, 1).astype(np.float64))


def weld_and_close(o):
    """Шви між зонами зварено; рот закрито, внутрішній шматок рота прибрано; очні западини закрито чашею за оком."""
    me = o.data
    bm = bmesh.new()
    bm.from_mesh(me)
    v0 = len(bm.verts)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-4)
    bm.verts.ensure_lookup_table()
    # компоненти: лишаємо найбільший
    seen = set()
    comps = []
    for v in bm.verts:
        if v in seen:
            continue
        st = [v]; cur = []
        while st:
            q = st.pop()
            if q in seen:
                continue
            seen.add(q); cur.append(q)
            for e in q.link_edges:
                st.append(e.other_vert(q))
        comps.append(cur)
    comps.sort(key=len, reverse=True)
    junk = 0
    for c in comps[1:]:
        junk += len(c)
        bmesh.ops.delete(bm, geom=c, context='VERTS')
    uv = bm.loops.layers.uv.active
    def loops_of_boundary():
        seen = set(); out = []
        for e in bm.edges:
            if not e.is_boundary or e in seen:
                continue
            st = [e]; L = []
            while st:
                x = st.pop()
                if x in seen:
                    continue
                seen.add(x); L.append(x)
                for vv in x.verts:
                    for y in vv.link_edges:
                        if y.is_boundary and y not in seen:
                            st.append(y)
            out.append(L)
        return out
    def vert_uv(v):
        for l in v.link_loops:
            return l[uv].uv.copy()
        return None
    filled = 0
    for L in loops_of_boundary():
        vs = list({x for e in L for x in e.verts})
        c = sum((x.co for x in vs), Vector()) / len(vs)
        is_eye = abs(c.x) > 0.015
        if is_eye:
            EYE_RIMS.append([(tuple(e.verts[0].co), tuple(e.verts[1].co)) for e in L])
            # чаша: кільце всередину голови й кришка
            ret = bmesh.ops.extrude_edge_only(bm, edges=L)
            nv = [g for g in ret["geom"] if isinstance(g, bmesh.types.BMVert)]
            for x in nv:
                x.co = c + (x.co - c) * 0.55 + Vector((0, 0.016, 0))
            ne = [g for g in ret["geom"] if isinstance(g, bmesh.types.BMEdge) and all(q in nv for q in g.verts)]
            res = bmesh.ops.holes_fill(bm, edges=ne, sides=64)
            for f in ret["geom"]:
                if isinstance(f, bmesh.types.BMFace):
                    for l in f.loops:
                        l[uv].uv = vert_uv(l.vert) if l.vert.link_loops else l[uv].uv
            for f in res.get("faces", []):
                for l in f.loops:
                    u = None
                    for k in l.vert.link_loops:
                        if k.face is not f:
                            u = k[uv].uv.copy(); break
                    if u is not None:
                        l[uv].uv = u
        else:
            res = bmesh.ops.holes_fill(bm, edges=L, sides=64)
            for f in res.get("faces", []):
                for l in f.loops:
                    u = None
                    for k in l.vert.link_loops:
                        if k.face is not f:
                            u = k[uv].uv.copy(); break
                    if u is not None:
                        l[uv].uv = u
        filled += 1
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bd = sum(1 for e in bm.edges if e.is_boundary)
    nm = sum(1 for e in bm.edges if len(e.link_faces) > 2)
    bm.to_mesh(me)
    bm.free()
    me.update()
    REPORT["weld"] = dict(verts_before=v0, verts_after=len(me.vertices), junk_verts=junk, holes_closed=filled, boundary_edges=bd, nonmanifold_edges=nm)
    print("WELD", REPORT["weld"], flush=True)


def face_metrics(by, Pz):
    """Брови набору як поліси на шкірі: середня лінія й напівтовщина за відстанню від середини обличчя; край очниць — відрізки лінії повіки."""
    out = {}
    br = next((by[k] for k in by if k.startswith("eyebrow")), None)
    if br is not None:
        Q = get_co(br.data)
        ax, z = np.abs(Q[:, 0]), Q[:, 2]
        edges = np.linspace(ax.min(), ax.max(), 11)
        cx, zc, hw = [], [], []
        for a0, a1 in zip(edges[:-1], edges[1:]):
            m = (ax >= a0) & (ax <= a1)
            if m.sum() >= 2:
                cx.append(float((a0 + a1) / 2)); zc.append(float((z[m].max() + z[m].min()) / 2)); hw.append(float((z[m].max() - z[m].min()) / 2))
        out["brow"] = dict(ax=cx, z=zc, half=hw, ax0=float(ax.min()), ax1=float(ax.max()))
    tree = kdtree.KDTree(len(Pz))
    for i, p in enumerate(Pz):
        tree.insert(p.tolist(), i)
    tree.balance()
    lids = []
    for rim in EYE_RIMS:
        segs = []
        for a0, a1 in rim:
            i0 = tree.find(a0)[1]; i1 = tree.find(a1)[1]
            segs.append([float(Pz[i0, 0]), float(Pz[i0, 2]), float(Pz[i1, 0]), float(Pz[i1, 2])])
        lids.append(segs)
    out["lids"] = lids
    return out


def subdivide(o, levels=1):
    m = o.modifiers.new("sub", 'SUBSURF')
    m.levels = levels
    m.render_levels = levels
    m.uv_smooth = 'PRESERVE_CORNERS'
    m.boundary_smooth = 'ALL'
    # перед арматурою
    while o.modifiers[0] != m:
        activate(o); bpy.ops.object.modifier_move_up(modifier=m.name)
    activate(o)
    bpy.ops.object.modifier_apply(modifier=m.name)


def smooth_all(o):
    o.data.polygons.foreach_set("use_smooth", [True] * len(o.data.polygons))
    o.data.update()


def set_weights(o, names, W, limit=4):
    """Записує щільну матрицю ваг: не більше limit впливів на вершину, сума 1."""
    W = W.copy()
    if limit:
        order = np.argsort(-W, axis=1)
        keep = order[:, :limit]
        M = np.zeros_like(W, dtype=bool)
        np.put_along_axis(M, keep, True, axis=1)
        W[~M] = 0.0
    s = W.sum(1, keepdims=True)
    W = W / np.maximum(s, 1e-6)
    o.vertex_groups.clear()
    groups = [o.vertex_groups.new(name=n) for n in names]
    for i in range(W.shape[0]):
        nz = np.nonzero(W[i] > 1e-4)[0]
        for j in nz:
            groups[j].add([i], float(W[i, j]), 'REPLACE')


def reorient_rest(arm, rest_by_kit):
    """Орієнтації кісток = орієнтації скелета кліпів; голови — свої. Меш не рухається, кліпи лягають без скручувань."""
    activate(arm)
    bpy.ops.object.mode_set(mode='EDIT')
    eb = arm.data.edit_bones
    for e in eb:
        e.use_connect = False
    head = {e.name: e.head.copy() for e in eb}
    length = {e.name: e.length for e in eb}
    for n, M in rest_by_kit.items():
        if n in eb:
            m = Matrix(M.tolist())
            m.translation = head[n]
            eb[n].matrix = m
            eb[n].length = max(length[n], 0.02)
    if "Root" in eb:
        for e in eb:
            if e.parent and e.parent.name == "Root":
                e.parent = None
        eb.remove(eb["Root"])
    for e in eb:
        if e.name in CS.BONE_MAP:
            e.name = CS.PREFIX + CS.BONE_MAP[e.name]
    bpy.ops.object.mode_set(mode='OBJECT')


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    objs = import_fbx(os.path.join(KIT, "body_%s_%s.fbx" % (SEX, SPEC["culture"])))
    arm = next(o for o in objs if o.type == 'ARMATURE')
    arm.location = (0, 0, 0)
    bpy.context.view_layer.update()
    parts = [o for o in objs if o.type == 'MESH']
    tag = lambda o: o.name.split(".", 1)[1].rsplit(".", 1)[0] if o.name.count(".") > 1 else o.name.split(".", 1)[1]
    by = {tag(o): o for o in parts}
    body = join([by[k] for k in by if k.startswith("body_")], "Body")
    weld_and_close(body)
    subdivide(body, 1)
    smooth_all(body)
    names, W = weights_dense(body)
    if "Root" in names:
        W[:, names.index("Root")] = 0
    bones = bone_table(arm)
    F = region_fields(names, W)
    P = get_co(body.data)
    Nn = get_normals(body.data)
    # форма архетипу
    d = SH.shape_offset(P, F, bones, SPEC["shape"])
    P1 = P + Nn * d[:, None]
    set_co(body.data, P1)
    Pz = P1.copy()     # зони й малювання рахуються від цього положення (до потовщення спорядження)
    # потовщення спорядження
    G = OF.Fields(P1, F, bones, SEX)
    layers = OF.layers(CID, G)
    thick = OF.thickness(layers)
    Nn1 = get_normals(body.data)
    set_co(body.data, P1 + Nn1 * thick[:, None])

    # зріст
    head_top = float(get_co(body.data)[:, 2].max())
    foot_low = float(get_co(body.data)[:, 2].min())
    h0 = head_top - foot_low
    scale = SPEC["height"] / h0
    REPORT.update(height_raw=round(h0, 4), scale=round(scale, 4), tris_body=sum(len(p.vertices) - 2 for p in body.data.polygons))
    print("HEIGHT", h0, "scale", scale, flush=True)

    # брови й вії набору — рвані смужки з альфою: малюємо їх на шкірі, у сітці лишається лише око
    face = face_metrics(by, Pz)
    for k in list(by):
        if k.startswith("eyebrow") or k.startswith("eyelash"):
            bpy.data.objects.remove(by[k], do_unlink=True)
            del by[k]
    bpy.context.view_layer.update()
    # дрібні частини обличчя лишаються окремими шматками в одному об'єкті
    extras = {k: by[k] for k in by if not k.startswith("body_")}
    # одяг і волосся
    ctx = dict(REPO=REPO, SEX=SEX, SPEC=SPEC, CID=CID, KIT=KIT, body=body, arm=arm, bones=bones, names=names, W=W, F=F, Pz=Pz,
               import_fbx=import_fbx, activate=activate, join=join, weights_dense=weights_dense, get_co=get_co, set_co=set_co, get_normals=get_normals,
               set_weights=set_weights, REPORT=REPORT, np=np, bpy=bpy, bmesh=bmesh, Vector=Vector, kdtree=kdtree)
    pieces = GR.build(ctx)     # {слот матеріалу: [об'єкти]}
    # передній край зачіски: вище нього скальп фарбується в колір волосся, нижче лишається шкіра
    hy_head = SH.anchors(bones)["head"][1]
    for k, ho in ctx.get("_kit", {}).items():
        if k.startswith("hair"):
            Qh = get_co(ho.data)
            mh = (Qh[:, 1] < hy_head - 0.045) & (np.abs(Qh[:, 0]) < 0.045)
            if mh.any():
                eh = np.linspace(0.0, 0.045, 6)
                low = [float(Qh[mh & (np.abs(Qh[:, 0]) >= e0) & (np.abs(Qh[:, 0]) < e1), 2].min()) for e0, e1 in zip(eh[:-1], eh[1:]) if (mh & (np.abs(Qh[:, 0]) >= e0) & (np.abs(Qh[:, 0]) < e1)).any()]
                face["hairline_z"] = float(np.median(low))

    # Дані для малювання (тіло в його UV)
    me = body.data
    me.calc_loop_triangles()
    tris = np.zeros(len(me.loop_triangles) * 3, dtype=np.int32)
    me.loop_triangles.foreach_get("vertices", tris)
    ltri = np.zeros(len(me.loop_triangles) * 3, dtype=np.int32)
    me.loop_triangles.foreach_get("loops", ltri)
    uvd = np.zeros(len(me.loops) * 2)
    me.uv_layers[0].data.foreach_get("uv", uvd)
    uvd = uvd.reshape(-1, 2)
    paint = dict(tris=tris.reshape(-1, 3), tri_uv=uvd[ltri].reshape(-1, 3, 2), P=Pz, N=Nn1, arm=F["arm"], leg=F["leg"], head=F["head"])
    gear = GR.paint_data(ctx, pieces)
    for k, v in gear.items():
        paint["gear_" + k] = v
    np.savez_compressed(os.path.join(OUT, "paint.npz"), **paint)
    json.dump(dict(bones=bones, sex=SEX, id=CID, scale=scale, culture=SPEC["culture"], face=face), open(os.path.join(OUT, "paint.json"), "w", encoding="utf-8"))

    # Скелет: орієнтації кліпів, імена mixamorig. Ваги перейменовуються разом із кістками.
    d = np.load(CLIPS)
    kit_names = [str(x) for x in d["kit_names"]]
    rest_by_kit = {n: d["rest"][i] for i, n in enumerate(kit_names)}
    for ob in [body] + list(extras.values()) + [p for v in pieces.values() for p in v]:
        for g in list(ob.vertex_groups):
            if g.name == "Root":
                ob.vertex_groups.remove(g)
            elif g.name in CS.BONE_MAP:
                g.name = CS.PREFIX + CS.BONE_MAP[g.name]
    reorient_rest(arm, rest_by_kit)

    # Масштаб: усе разом; стопи на нуль
    allm = [body] + list(extras.values()) + [p for v in pieces.values() for p in v]
    S = Matrix.Diagonal((scale, scale, scale, 1.0))
    T = Matrix.Translation((0, 0, -foot_low * scale))
    for ob in allm:
        ob.data.transform(T @ S)
        ob.data.update()
    activate(arm)
    bpy.ops.object.mode_set(mode='EDIT')
    for e in arm.data.edit_bones:
        m = e.matrix.copy()
        ln = e.length
        loc = T @ S @ m.translation
        m.translation = loc
        e.matrix = m
        e.length = ln * scale
    bpy.ops.object.mode_set(mode='OBJECT')

    # Один об'єкт: тіло, обличчя, одяг, волосся (слоти матеріалів зберігаються)
    GR.assign_materials(ctx, pieces, extras)
    mesh_objs = allm
    res = join(mesh_objs, CID)
    for m in list(res.modifiers):
        res.modifiers.remove(m)
    res.parent = arm
    res.matrix_parent_inverse = Matrix.Identity(4)
    mod = res.modifiers.new("Armature", 'ARMATURE')
    mod.object = arm
    arm.name = CID + "_rig"
    arm.data.name = CID + "_rig"
    activate(res)
    bpy.ops.object.vertex_group_limit_total(limit=4)
    bpy.ops.object.vertex_group_normalize_all(lock_active=False)
    res.data.polygons.foreach_set("use_smooth", [True] * len(res.data.polygons))
    res.data.update()
    hips_rest = arm.data.bones[CS.PREFIX + "Hips"].head_local
    REPORT.update(tris_total=sum(len(p.vertices) - 2 for p in res.data.polygons), verts=len(res.data.vertices), hips_height=round(hips_rest.z, 4),
                  materials=[m.name for m in res.data.materials], height_final=round(max(v.co.z for v in body.data.vertices) if False else SPEC["height"], 3))
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "stage1.blend"))
    json.dump(REPORT, open(os.path.join(OUT, "report.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    print("REPORT", json.dumps(REPORT, ensure_ascii=False), flush=True)


main()
