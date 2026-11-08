# Одяг і волосся персонажа (виконується в Blender із cast_build.py): оболонки на тілі й лофти (поли) зі своїми вагами, бахрома, зачіска з набору.
# Ваги: оболонка успадковує ваги тіла, тому не розходиться з ним; лофт бере ваги найближчих точок тіла й згладжує їх уздовж кільця й донизу:
# поли поводяться як штанини зі згладженим центром — не рвуться й тягнуться за ногою, тому нога не протикає тканину.
import bpy, bmesh, math, os
import numpy as np
from mathutils import kdtree, Vector
from mathutils.bvhtree import BVHTree
import cast_outfits as OF

# Прямокутники UV в атласі одягу: оболонка — у ліву верхню частину, лофти — решта.
RECT_SHELL = (0.0, 0.0, 0.62, 0.62)
RECT_LOFT = (0.62, 0.0, 0.38, 1.0)
RECT_LOFT2 = (0.0, 0.62, 0.62, 0.30)
RECT_FRINGE = (0.0, 0.92, 0.62, 0.08)
# Атлас альфа-матеріалу
RECT_HAIR = (0.0, 0.0, 0.75, 0.75)
RECT_BEARD = (0.75, 0.0, 0.25, 0.25)
RECT_BROW = (0.75, 0.25, 0.25, 0.25)
RECT_LASH = (0.75, 0.50, 0.25, 0.25)
RECT_EYE = (0.75, 0.75, 0.25, 0.25)


def new_mesh_obj(name, P, faces, uv, names, W, ctx):
    me = bpy.data.meshes.new(name)
    me.from_pydata(P.tolist(), [], faces)
    me.update()
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    ul = me.uv_layers.new(name="UVMap")
    flat = []
    for p in me.polygons:
        for li in p.loop_indices:
            flat.append(uv[me.loops[li].vertex_index])
    ul.data.foreach_set("uv", np.array(flat).reshape(-1))
    ctx["set_weights"](o, names, W, limit=4)
    return o


def body_arrays(ctx):
    body = ctx["body"]
    return ctx["get_co"](body.data), ctx["get_normals"](body.data)


def edge_distance(faces, nv, P):
    """Відстань кожної вершини до межі сітки вздовж ребер (Дейкстра спрощена: релаксація)."""
    from collections import defaultdict
    edge = defaultdict(int)
    nbr = defaultdict(set)
    for f in faces:
        n = len(f)
        for i in range(n):
            a, b = f[i], f[(i + 1) % n]
            edge[(min(a, b), max(a, b))] += 1
            nbr[a].add(b); nbr[b].add(a)
    d = np.full(nv, 1e9)
    for (a, b), c in edge.items():
        if c == 1:
            d[a] = 0; d[b] = 0
    for _ in range(60):
        ch = False
        for v, ns in nbr.items():
            for u in ns:
                nd = d[u] + np.linalg.norm(P[v] - P[u])
                if nd < d[v] - 1e-6:
                    d[v] = nd; ch = True
        if not ch:
            break
    d[d > 1e8] = 1.0
    return d


def relax_boundary(Q, faces, iters=10, lam=0.55):
    """Згладжує край оболонки: без зубців уздовж викладки граней."""
    from collections import defaultdict
    cnt = defaultdict(int)
    for f in faces:
        n = len(f)
        for i in range(n):
            a, b = f[i], f[(i + 1) % n]
            cnt[(min(a, b), max(a, b))] += 1
    nb = defaultdict(list)
    for (a, b), c in cnt.items():
        if c == 1:
            nb[a].append(b); nb[b].append(a)
    Q = Q.copy()
    ids = [v for v, ns in nb.items() if len(ns) == 2]
    for _ in range(iters):
        R = Q.copy()
        for v in ids:
            ns = nb[v]
            R[v] = Q[v] * (1 - lam) + (Q[ns[0]] + Q[ns[1]]) * 0.5 * lam
        Q = R
    return Q


def smart_uv(o, rect):
    """Власна розгортка оболонки на весь прямокутник. Повертає UV кутів кожної грані, у порядку граней."""
    vl = bpy.context.view_layer
    vl.update()
    bpy.ops.object.select_all(action='DESELECT')
    vl.objects.active = o
    o.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(58.0), island_margin=0.012, area_weight=0.0, correct_aspect=True, scale_to_bounds=True)
    bpy.ops.object.mode_set(mode='OBJECT')
    me = o.data
    ul = me.uv_layers[0]
    uv = np.zeros(len(me.loops) * 2)
    ul.data.foreach_get("uv", uv)
    uv = uv.reshape(-1, 2)
    uv = np.stack([rect[0] + np.clip(uv[:, 0], 0, 1) * rect[2], rect[1] + np.clip(uv[:, 1], 0, 1) * rect[3]], axis=1)
    ul.data.foreach_set("uv", uv.reshape(-1))
    return [[uv[li] for li in p.loop_indices] for p in me.polygons]


def boundary_verts(faces):
    from collections import defaultdict
    cnt = defaultdict(int)
    for f in faces:
        n = len(f)
        for i in range(n):
            a, b = f[i], f[(i + 1) % n]
            cnt[(min(a, b), max(a, b))] += 1
    ids = set()
    for (a, b), c in cnt.items():
        if c == 1:
            ids.add(a); ids.add(b)
    return sorted(ids)


def make_shell(ctx, name, zone, offset, rect, pid, snap=None):
    """Копія граней тіла, де zone>0.5, зсунута назовні на offset(P). Ваги — точно як у тіла.
    snap(Q, ids) — рівняє край оболонки: край з граней тіла сам по собі зубчастий."""
    body = ctx["body"]
    np_ = np
    P, N = body_arrays(ctx)
    me = body.data
    me.calc_loop_triangles()
    names, W = ctx["weights_dense"](body)
    zv = zone
    keep = []
    uv_loop = np.zeros(len(me.loops) * 2)
    me.uv_layers[0].data.foreach_get("uv", uv_loop)
    uv_loop = uv_loop.reshape(-1, 2)
    vuv = {}
    for p in me.polygons:
        if all(zv[v] > 0.5 for v in p.vertices):
            keep.append(p)
            for v, li in zip(p.vertices, p.loop_indices):
                vuv.setdefault(v, uv_loop[li])
    used = sorted({v for p in keep for v in p.vertices})
    remap = {v: i for i, v in enumerate(used)}
    Q = P[used] + N[used] * offset[used][:, None]
    faces = [[remap[v] for v in p.vertices] for p in keep]
    Q = relax_boundary(Q, faces)
    if snap is not None:
        Q = snap(Q, boundary_verts(faces))
        Q = relax_boundary(Q, faces, iters=3, lam=0.4)
    uv = np.array([vuv[v] for v in used])
    uv2 = np.stack([rect[0] + uv[:, 0] * rect[2], rect[1] + uv[:, 1] * rect[3]], axis=1)
    o = new_mesh_obj(name, Q, faces, uv2, names, W[used], ctx)
    o["pid"] = pid
    face_uv = smart_uv(o, rect)
    return o, dict(P=Q, faces=faces, uv=uv2, face_uv=face_uv, edge=edge_distance(faces, len(Q), Q))


def ring_envelope(P, select, center, z, n, dz=0.025):
    """Обгортка перерізу тіла на висоті z: радіус за кутом (кут 0 — уперед, −Y; зростає до +X)."""
    m = select & (np.abs(P[:, 2] - z) < dz)
    th = np.linspace(0, 2 * np.pi, n, endpoint=False)
    r = np.zeros(n)
    if m.any():
        q = P[m][:, :2] - center
        a = np.arctan2(q[:, 0], -q[:, 1])
        d = np.linalg.norm(q, axis=1)
        for i, t in enumerate(th):
            da = np.abs((a - t + np.pi) % (2 * np.pi) - np.pi)
            sel = da < (2 * np.pi / n) * 1.8
            r[i] = d[sel].max() if sel.any() else 0.0
        # залатати нулі й згладити
        for _ in range(4):
            r = np.maximum(r, 0.5 * (np.roll(r, 1) + np.roll(r, -1)) * 0.98)
        for _ in range(3):
            r = (np.roll(r, 1) + 2 * r + np.roll(r, -1)) * 0.25
    return r


def make_loft(ctx, name, z_top, z_bot, rings, seg, margin, flare, pleats, rect, pid, opening=None, arc=None, center_y=None, back_push=None, hip_bias=0.0, weight_bones=None):
    """Спідниця/поли: кільця від талії до подолу. opening — (кут, напівширина) розрізу; arc — (кут0, кут1) лише дуга (плащ).
    Повертає об'єкт і дані для малювання."""
    body = ctx["body"]
    P, N = body_arrays(ctx)
    F = ctx["F"]
    B = ctx["bones"]
    names, W = ctx["weights_dense"](body)
    sel = (F["arm"] < 0.35) & (F["head"] < 0.5)
    cy = B["pelvis"]["head"][1] if center_y is None else center_y
    center = np.array([0.0, cy])
    closed = arc is None and opening is None
    verts = []
    uvs = []
    params = []
    rows = rings + 1
    if arc is not None:
        a0, a1 = arc
        angs = np.linspace(a0, a1, seg + 1)
    elif opening is not None:
        # кути від краю розрізу до краю розрізу
        ac, hw = opening
        angs = np.linspace(ac + hw, ac + 2 * np.pi - hw, seg + 1)
    else:
        angs = np.linspace(0, 2 * np.pi, seg, endpoint=False)
    M = np.zeros((rows, len(angs), 3))
    for i in range(rows):
        v = i / rings
        z = z_top + (z_bot - z_top) * v
        env = ring_envelope(P, sel, center, z, 64, dz=0.03)
        for j, a in enumerate(angs):
            f = (a % (2 * np.pi)) / (2 * np.pi) * 64
            k0 = int(f) % 64; k1 = (k0 + 1) % 64
            r = env[k0] + (env[k1] - env[k0]) * (f - int(f))
            rr = r + margin + flare * v ** 1.4
            rr += pleats * (v ** 1.1) * math.sin(a * 11.0 + 1.2 * math.sin(a * 3.0))
            if back_push is not None:
                rr += back_push * (v ** 1.2)
            M[i, j] = (center[0] + math.sin(a) * rr, center[1] - math.cos(a) * rr, z)
    # сітка
    idx = lambda i, j: i * len(angs) + j
    faces = []
    for i in range(rings):
        for j in range(len(angs) - (0 if closed else 1)):
            j2 = (j + 1) % len(angs)
            faces.append([idx(i, j), idx(i + 1, j), idx(i + 1, j2), idx(i, j2)])
    Q = M.reshape(-1, 3)
    # UV
    uv = np.zeros((len(Q), 2))
    for i in range(rows):
        for j in range(len(angs)):
            uv[idx(i, j)] = (rect[0] + rect[2] * (j / max(1, len(angs) - (0 if closed else 1))), rect[1] + rect[3] * (1.0 - i / rings))
    # ваги: найближчі точки тіла
    tree = kdtree.KDTree(int(sel.sum()))
    ids = np.nonzero(sel)[0]
    for k, i in enumerate(ids):
        tree.insert(P[i], k)
    tree.balance()
    Wq = np.zeros((len(Q), W.shape[1]), dtype=np.float32)
    for q in range(len(Q)):
        res = tree.find_n(Q[q], 8)
        ws = 0.0
        for co, k, dist in res:
            wt = 1.0 / (dist * dist + 1e-4)
            Wq[q] += W[ids[k]] * wt
            ws += wt
        Wq[q] /= ws
    # згладжування мале: панель іде за своєю ногою (нога не протикає), центр між ногами лишається плавним
    G = Wq.reshape(rows, len(angs), -1)
    for it in range(5):
        G2 = G.copy()
        for i in range(rows):
            v = i / rings
            a = 0.22 + 0.18 * v
            if closed:
                nb = (np.roll(G[i], 1, axis=0) + np.roll(G[i], -1, axis=0)) * 0.5
                G2[i] = G[i] * (1 - a) + nb * a
            else:
                nb = G[i].copy()
                nb[1:-1] = (G[i][:-2] + G[i][2:]) * 0.5
                G2[i] = G[i] * (1 - a) + nb * a
        G3 = G2.copy()
        for i in range(1, rows - 1):
            G3[i] = G2[i] * 0.6 + (G2[i - 1] + G2[i + 1]) * 0.2
        G = G3
    # частка таза: поли менше тягнуться за ногами
    if hip_bias > 0:
        hips = np.zeros(W.shape[1], dtype=np.float32)
        hips[names.index("pelvis")] = 1.0
        for i in range(rows):
            G[i] = G[i] * (1 - hip_bias * (i / rings)) + hips * hip_bias * (i / rings)
    if weight_bones:
        # примусова частка кісток угорі (плащ кріпиться до плечей)
        t = np.zeros(W.shape[1], dtype=np.float32)
        for n, w in weight_bones.items():
            t[names.index(n)] = w
        for i in range(rows):
            k = max(0.0, 1.0 - (i / rings) / 0.35)
            G[i] = G[i] * (1 - k) + t * k
    Wq = G.reshape(len(Q), -1)
    o = new_mesh_obj(name, Q, faces, uv, names, Wq, ctx)
    o["pid"] = pid
    return o, dict(P=Q, faces=faces, uv=uv, edge=edge_distance(faces, len(Q), Q))


def make_fringe(ctx, name, bone_side, t_at, count, length, width, pid, phi_range=(-math.pi, math.pi), direction=(0, 0, -1), along_bone=False):
    """Бахрома: стрічки навколо кістки, кожна жорстко тримається за сусідню вершину тіла."""
    body = ctx["body"]
    P, N = body_arrays(ctx)
    names, W = ctx["weights_dense"](body)
    B = ctx["bones"]
    tree = kdtree.KDTree(len(P))
    for i, p in enumerate(P):
        tree.insert(p, i)
    tree.balance()
    verts, faces, uv, wts = [], [], [], []
    rect = RECT_FRINGE
    n_all = 0
    for s in ("l", "r"):
        a = np.array(B[bone_side + "_" + s]["head"]); b = np.array(B[bone_side + "_" + s]["tail"])
        c = a + (b - a) * t_at
        ax = (b - a) / np.linalg.norm(b - a)
        f = np.array([0, -1.0, 0]); f = f - f.dot(ax) * ax; f /= np.linalg.norm(f)
        g = np.cross(ax, f)
        drn = ax if along_bone else np.array(direction, dtype=float)
        for k in range(count):
            th = phi_range[0] + (phi_range[1] - phi_range[0]) * (k + 0.5) / count
            d = f * math.cos(th) + g * math.sin(th)
            # точка на поверхні: найближча вершина до променя
            co, vi, _ = tree.find(c + d * 0.12)
            anchor = np.array(co) + N[vi] * 0.004
            tang = np.cross(N[vi], drn)
            tang = tang / max(np.linalg.norm(tang), 1e-6) * (width * 0.5)
            L = length * (0.75 + 0.25 * math.sin(k * 12.9898) ** 2)
            base = len(verts)
            for seg in range(4):
                v = seg / 3.0
                sway = N[vi] * (0.003 + 0.010 * v * v)
                p = anchor + drn * L * v + sway
                verts.append(p - tang * (1 - 0.35 * v)); verts.append(p + tang * (1 - 0.35 * v))
                u0 = rect[0] + rect[2] * ((n_all % 16) / 16.0)
                uv.append((u0, rect[1] + rect[3] * (1 - v))); uv.append((u0 + rect[2] / 16.0 * 0.8, rect[1] + rect[3] * (1 - v)))
                wts.append(W[vi]); wts.append(W[vi])
            for seg in range(3):
                faces.append([base + seg * 2, base + seg * 2 + 1, base + seg * 2 + 3, base + seg * 2 + 2])
            n_all += 1
    Q = np.array(verts)
    o = new_mesh_obj(name, Q, faces, np.array(uv), names, np.array(wts, dtype=np.float32), ctx)
    o["pid"] = pid
    return o, dict(P=Q, faces=faces, uv=np.array(uv), edge=np.full(len(Q), 1.0))


def kit_parts(ctx, keys):
    """Зачіска й борода з набору, підігнані під голову цього тіла. Повертає {ключ: об'єкт}."""
    SEX = ctx["SEX"]
    new = ctx["import_fbx"](os.path.join(ctx["KIT"], "kit_%s.fbx" % SEX))
    arm2 = next(o for o in new if o.type == 'ARMATURE')
    arm2.location = (0, 0, 0)
    bpy.context.view_layer.update()
    tag = lambda o: o.name.split(".", 1)[1].rsplit(".", 1)[0] if o.name.count(".") > 1 else o.name.split(".", 1)[1]
    table = {tag(o): o for o in new if o.type == 'MESH'}
    kit_head = table["body_head"]
    KP = ctx["get_co"](kit_head.data)
    kW = ctx["weights_dense"](kit_head)
    knames, kw = kW
    kh = kw[:, knames.index("head")] > 0.5
    kmin, kmax = KP[kh].min(0), KP[kh].max(0)
    P, _ = body_arrays(ctx)
    names, W = ctx["weights_dense"](ctx["body"])
    h = W[:, names.index("head")] > 0.5
    # голова тіла без потовщення спорядження беремо зі знімка
    Pz = ctx["Pz"]
    bmin, bmax = Pz[h].min(0), Pz[h].max(0)
    sc = (bmax - bmin) / (kmax - kmin)
    out = {}
    # поверхня тіла: зачіска підганяється за габаритами голови, тому на вужчих головах частина шапки йде під шкіру
    me_b = ctx["body"].data
    bvh = BVHTree.FromPolygons([tuple(p) for p in P.tolist()], [tuple(p.vertices) for p in me_b.polygons], all_triangles=False)
    head_top = float(P[h, 2].max())
    head_low = float(P[h, 2].min())
    for k in keys:
        o = table[k]
        Q = ctx["get_co"](o.data)
        Q2 = bmin + (Q - kmin) * sc
        if k.startswith("hair"):
            lift = 0.0028
            moved = 0
            for i in range(len(Q2)):
                c = Vector(Q2[i].tolist())
                if c.z < head_low or c.z > head_top + 0.05:
                    continue
                loc, nor, idx, dist = bvh.find_nearest(c, 0.06)
                if loc is None:
                    continue
                sd = (c - loc).dot(nor)
                if sd < lift:
                    Q2[i] = np.array((loc + nor * lift).to_tuple())
                    moved += 1
            ctx["REPORT"]["hair_lifted"] = moved
        ctx["set_co"](o.data, Q2)
        o.parent = None
        out[k] = o
    for o in new:
        if o not in out.values():
            bpy.data.objects.remove(o, do_unlink=True)
    return out


def garments_for(ctx):
    """Повертає список (слот, об'єкт, дані малювання) для цього персонажа."""
    cid = ctx["CID"]
    P, N = body_arrays(ctx)
    Pz = ctx["Pz"]
    F = ctx["F"]
    B = ctx["bones"]
    G = OF.Fields(Pz, F, B, ctx["SEX"], soft=0.002)
    out = []
    head_ok = F["head"] < 0.35
    if cid == "baron":
        # халат: верх — оболонка (тулуб і рукави), низ — поли; спереду розріз
        tu, _, _ = G.side("upperarm")
        tl, _, _ = G.side("lowerarm")
        v_w = 0.018 + 0.085 * np.clip((Pz[:, 2] - G.z_waist) / (G.z_neck - G.z_waist), 0, 1)
        opening = (np.abs(Pz[:, 0]) < v_w) & (Pz[:, 1] < G.ys + 0.01)
        sleeve = (G.arm > 0.45) & (tl < 0.62) & (Pz[:, 0] * Pz[:, 0] > 0)
        torso = (G.arm <= 0.45) & (G.leg < 0.5) & (Pz[:, 2] > G.z_waist - 0.05) & head_ok
        zone = ((torso | sleeve) & (~opening)).astype(float)
        off = 0.014 + 0.018 * np.clip(tl, 0, 1) * (G.arm > 0.45) + 0.006 * (G.arm <= 0.45)
        o, d = make_shell(ctx, "robe_top", zone, off, RECT_SHELL, 1)
        out.append(("Outer", o, d))
        o, d = make_loft(ctx, "robe_low", G.z_waist + 0.0, G.z_knee - 0.22, 12, 60, 0.036, 0.05, 0.010, RECT_LOFT, 2, opening=(0.0, 0.16), hip_bias=0.0)
        out.append(("Outer", o, d))
        o, d = make_loft(ctx, "robe_sash", G.z_waist + 0.028, G.z_waist - 0.032, 2, 54, 0.032, 0.002, 0.0, RECT_LOFT2, 3)
        out.append(("Outer", o, d))
    elif cid == "vdova":
        zone = ((Pz[:, 2] > G.z_neck - 0.13) & (G.arm <= 0.10) & (np.abs(Pz[:, 0]) < 0.155) & (Pz[:, 2] < G.z_neck + 0.02) & (Pz[:, 1] > G.ys + 0.012) & head_ok).astype(float)
        o, d = make_shell(ctx, "cape_collar", zone, np.full(len(Pz), 0.016), RECT_SHELL, 1)
        out.append(("Outer", o, d))
        o, d = make_loft(ctx, "cape", G.z_neck - 0.09, G.z_hip - 0.24, 10, 40, 0.030, 0.030, 0.008, RECT_LOFT, 2, arc=(math.radians(104), math.radians(256)),
                         back_push=0.05, hip_bias=0.30, weight_bones={"spine_03": 1.0})
        out.append(("Outer", o, d))
    elif cid == "novachka":
        t, _, _ = G.side("thigh")
        zone = ((Pz[:, 2] < G.z_waist + 0.012) & (t < 0.56) & (G.arm <= 0.45)).astype(float)
        off = 0.007 + 0.018 * np.clip(t / 0.56, 0, 1) ** 2
        z_top = G.z_waist + 0.012
        z_mid = G.z_hip
        bones = ctx["bones"]
        def snap_shorts(Q, ids):
            # пояс на одній висоті, холоші впоперек стегна
            Q = Q.copy()
            for v in ids:
                if Q[v, 2] > z_mid:
                    Q[v, 2] = z_top
                else:
                    sd = "thigh_l" if Q[v, 0] >= 0 else "thigh_r"
                    a = np.asarray(bones[sd]["head"], dtype=np.float64); b = np.asarray(bones[sd]["tail"], dtype=np.float64)
                    ax = b - a; L = np.linalg.norm(ax); ax = ax / L
                    tv = np.dot(Q[v] - a, ax) / L
                    Q[v] = Q[v] + ax * (0.56 - tv) * L
            return Q
        o, d = make_shell(ctx, "shorts", zone, off, RECT_SHELL, 1, snap=snap_shorts)
        out.append(("Gear", o, d))
    elif cid == "lina":
        tl, _, _ = G.side("lowerarm")
        v_w = 0.02 + 0.06 * np.clip((Pz[:, 2] - G.z_chest + 0.05) / (G.z_neck - G.z_chest + 0.05), 0, 1)
        opening = (np.abs(Pz[:, 0]) < v_w) & (Pz[:, 1] < G.ys + 0.01) & (Pz[:, 2] > G.z_chest - 0.06)
        sleeve = (G.arm > 0.45) & (tl < 0.93)
        torso = (G.arm <= 0.45) & (G.leg < 0.5) & (Pz[:, 2] > G.z_hip + 0.0) & head_ok
        zone = ((torso | sleeve) & (~opening)).astype(float)
        o, d = make_shell(ctx, "blazer", zone, np.full(len(Pz), 0.010), RECT_SHELL, 1)
        out.append(("Gear", o, d))
        o, d = make_loft(ctx, "skirt", G.z_waist + 0.012, G.z_knee - 0.03, 10, 60, 0.013, 0.014, 0.002, RECT_LOFT, 2)
        out.append(("Gear", o, d))
    elif cid == "dzhura":
        o, d = make_fringe(ctx, "fringe", "calf", 0.26, 20, 0.105, 0.016, 3, phi_range=(-2.6, 2.6), along_bone=True)
        out.append(("Gear", o, d))
    elif cid == "komet":
        o, d = make_fringe(ctx, "fringe2", "calf", 0.20, 16, 0.095, 0.016, 3, phi_range=(-2.4, 2.4), along_bone=True)
        out.append(("Gear", o, d))
    return out


def build(ctx):
    SPEC = ctx["SPEC"]
    pieces = {"Gear": [], "Outer": [], "Alpha": []}
    ctx["_gdata"] = []
    for slot, o, d in garments_for(ctx):
        pieces[slot].append(o)
        d["pid"] = o["pid"]
        ctx["_gdata"].append(d)
    keys = [k for k in (SPEC.get("hair"), SPEC.get("beard")) if k]
    parts = kit_parts(ctx, keys) if keys else {}
    ctx["_kit"] = parts
    pieces["Alpha"].extend(parts.values())
    return pieces


def paint_data(ctx, pieces):
    """Плоскі масиви граней одягу для малювання атласу."""
    tri_uv, tri_P, tri_edge, tri_pid = [], [], [], []
    for d in ctx.get("_gdata", []):
        P = d["P"]; uv = d["uv"]; e = d["edge"]
        fu = d.get("face_uv")
        for fi, f in enumerate(d["faces"]):
            for k in range(1, len(f) - 1):
                ids = [f[0], f[k], f[k + 1]]
                tri_uv.append(np.array([fu[fi][0], fu[fi][k], fu[fi][k + 1]]) if fu is not None else uv[ids])
                tri_P.append(P[ids]); tri_edge.append(e[ids]); tri_pid.append(d["pid"])
    if not tri_uv:
        return dict(tri_uv=np.zeros((0, 3, 2)), tri_P=np.zeros((0, 3, 3)), tri_edge=np.zeros((0, 3)), tri_pid=np.zeros((0,), dtype=np.int32))
    return dict(tri_uv=np.array(tri_uv), tri_P=np.array(tri_P), tri_edge=np.array(tri_edge), tri_pid=np.array(tri_pid, dtype=np.int32))


def remap_uv(me, rect):
    uv = np.zeros(len(me.loops) * 2)
    ul = me.uv_layers[0]
    ul.data.foreach_get("uv", uv)
    uv = uv.reshape(-1, 2)
    uv = np.stack([rect[0] + np.clip(uv[:, 0], 0, 1) * rect[2], rect[1] + np.clip(uv[:, 1], 0, 1) * rect[3]], axis=1)
    ul.data.foreach_set("uv", uv.reshape(-1))


def planar_uv(o, rect):
    """Борода: нова UV з фронтальної проєкції (стара — плитка за межами 0..1)."""
    me = o.data
    P = np.zeros(len(me.vertices) * 3)
    me.vertices.foreach_get("co", P)
    P = P.reshape(-1, 3)
    mn, mx = P.min(0), P.max(0)
    u = (P[:, 0] - mn[0]) / max(mx[0] - mn[0], 1e-6)
    v = (P[:, 2] - mn[2]) / max(mx[2] - mn[2], 1e-6)
    ul = me.uv_layers[0]
    flat = np.zeros(len(me.loops) * 2)
    for li, l in enumerate(me.loops):
        flat[li * 2] = rect[0] + rect[2] * u[l.vertex_index]
        flat[li * 2 + 1] = rect[1] + rect[3] * v[l.vertex_index]
    ul.data.foreach_set("uv", flat)


def single_material(o, mat):
    o.data.materials.clear()
    o.data.materials.append(mat)
    o.data.polygons.foreach_set("material_index", [0] * len(o.data.polygons))
    o.data.update()


def assign_materials(ctx, pieces, extras):
    cid = ctx["CID"]
    mats = {}
    for s in ("Body", "Gear", "Outer", "Alpha"):
        m = bpy.data.materials.new("%s_%s" % (cid, s))
        m.use_nodes = True
        mats[s] = m
    single_material(ctx["body"], mats["Body"])
    for s in ("Gear", "Outer"):
        for o in pieces[s]:
            single_material(o, mats[s])
    for k, o in extras.items():
        if k.startswith("eyebrow"):
            remap_uv(o.data, RECT_BROW)
        elif k.startswith("eyelash"):
            remap_uv(o.data, RECT_LASH)
        else:
            remap_uv(o.data, RECT_EYE)
        single_material(o, mats["Alpha"])
    for k, o in ctx.get("_kit", {}).items():
        if k.startswith("hair"):
            remap_uv(o.data, RECT_HAIR)
            ctx["hair_key"] = k
        else:
            planar_uv(o, RECT_BEARD)
            ctx["beard_key"] = k
        single_material(o, mats["Alpha"])
    ctx["REPORT"]["hair"] = ctx.get("hair_key")
    ctx["REPORT"]["beard"] = ctx.get("beard_key")
