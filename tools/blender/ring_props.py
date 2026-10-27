# Реквізит арен «Рингу» (ітерація 2, docs/TRPG_DESIGN.md §11.0.3): моделі, яких у залі сотні й тисячі, —
# розкладний стілець і крісло трибуни. Будуються ОДИН раз і ставляться екземплярами: у грі — GPU-інстансинг
# (модель + список місць venue_<id>_props.txt), у знімках огляду — пов'язані копії Blender.
# Підключається з ring_venues.py (exec), сам не запускається. Кожна модель — два рівні: LOD0 (повна, ~300
# трикутників) і LOD1 (дві площини: сидіння і спинка). Осі: лицем у −Y, сидіння — на висоті 0,45 м, Z угору.
# Матеріали: Prop_Frame (фарбований метал), Prop_Seat (пластик; колір — з кольору об'єкта: у грі — з
# MaterialPropertyBlock екземпляра, тож одна модель — будь-який колір сидінь).

def _tube(bm, pts, r, segs=6):
    """Трубка вздовж ламаної: циліндр на кожен відрізок, стики перекриваються на радіус."""
    pts = [Vector(p) for p in pts]
    for a, b in zip(pts, pts[1:]):
        d = b - a
        L = d.length
        if L < 1e-5:
            continue
        res = bmesh.ops.create_cone(bm, cap_ends=False, segments=segs, radius1=r, radius2=r, depth=L + r)
        rot = d.to_track_quat('Z', 'Y').to_matrix().to_4x4()
        bmesh.ops.transform(bm, matrix=rot, verts=res["verts"])
        bmesh.ops.translate(bm, vec=(a + b) / 2, verts=res["verts"])

def _rbox(bm, c, s, bevel, segs=2):
    tb = bmesh.new()
    bmesh.ops.create_cube(tb, size=1.0)
    bmesh.ops.scale(tb, vec=s, verts=list(tb.verts))
    bmesh.ops.bevel(tb, geom=list(tb.edges), offset=bevel, segments=segs, affect='EDGES', profile=0.5)
    bmesh.ops.translate(tb, vec=c, verts=list(tb.verts))
    me = bpy.data.meshes.new("_tmp")
    tb.to_mesh(me)
    tb.free()
    bm.from_mesh(me)
    bpy.data.meshes.remove(me)

def _shell(bm, profile, width, nx, cup=0.0, thick=0.012):
    """Литий корпус: профіль [(y, z)] уздовж −Y→+Y, витягнутий поперек на width; cup — прогин до середини (м)."""
    rows = []
    for i in range(nx + 1):
        u = i / nx
        x = (u - 0.5) * width
        dy = cup * (1 - (2 * u - 1) ** 2)
        rows.append([Vector((x, y + dy, z)) for (y, z) in profile])
    def grid(off):
        vs = [[bm.verts.new(p + off) for p in row] for row in rows]
        return vs
    top = grid(Vector((0, 0, 0)))
    bot = grid(Vector((0, thick * 0.6, -thick)))
    n = len(profile)
    for i in range(nx):
        for j in range(n - 1):
            bm.faces.new((top[i][j], top[i + 1][j], top[i + 1][j + 1], top[i][j + 1]))
            bm.faces.new((bot[i][j + 1], bot[i + 1][j + 1], bot[i + 1][j], bot[i][j]))
    for i in range(nx):                              # торці переднього й заднього країв
        bm.faces.new((top[i][0], bot[i][0], bot[i + 1][0], top[i + 1][0]))
        bm.faces.new((top[i + 1][n - 1], bot[i + 1][n - 1], bot[i][n - 1], top[i][n - 1]))
    for i, rng in ((0, range(n - 1)), (nx, range(n - 1))):
        for j in rng:
            q = (top[i][j], top[i][j + 1], bot[i][j + 1], bot[i][j])
            bm.faces.new(q if i == 0 else tuple(reversed(q)))
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))

def _mesh(name, parts):
    """parts: [(матеріал, bmesh)] → одна сітка з кількома слотами матеріалів, розгортка в метрах."""
    me = bpy.data.meshes.new(name)
    out = bmesh.new()
    for k, (mat, bm) in enumerate(parts):
        world_uv(bm, mat)
        tmp = bpy.data.meshes.new("_p")
        bm.to_mesh(tmp)
        bm.free()
        n0 = len(out.faces)
        out.from_mesh(tmp)
        bpy.data.meshes.remove(tmp)
        out.faces.ensure_lookup_table()
        for f in out.faces[n0:]:
            f.material_index = k
    out.to_mesh(me)
    out.free()
    for mat, _ in parts:
        me.materials.append(prop_material(mat))
    return me

def prop_material(mat):
    """Prop_Seat — пластик з кольором об'єкта (екземпляра); решта — звичайні матеріали арени."""
    if mat != "Prop_Seat":
        return material(mat)
    m = bpy.data.materials.get(mat)
    if m is not None:
        return m
    m = bpy.data.materials.new(mat)
    m.use_nodes = True
    nt = m.node_tree
    b = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    info = SETS.get("plastic")
    oi = nt.nodes.new("ShaderNodeObjectInfo")
    mul = nt.nodes.new("ShaderNodeMix")
    mul.data_type = 'RGBA'
    mul.blend_type = 'MULTIPLY'
    mul.inputs["Factor"].default_value = 1.0
    nt.links.new(oi.outputs["Color"], mul.inputs[7])
    if info:
        t = tex_node(m, info["diff"], False)
        nt.links.new(t.outputs["Color"], mul.inputs[6])
        nn = nt.nodes.new("ShaderNodeNormalMap")
        nt.links.new(tex_node(m, info["nor"], True).outputs["Color"], nn.inputs["Color"])
        nt.links.new(nn.outputs["Normal"], b.inputs["Normal"])
    nt.links.new(mul.outputs[2], b.inputs["Base Color"])
    b.inputs["Roughness"].default_value = 0.5
    m["kit"] = "plastic"
    return m

def build_chair_folding():
    """Сталевий розкладний стілець: задні ноги переходять у стійки спинки, передні — навскіс під сидіння,
    перекладини; сидіння з закругленими краями, вигнута спинка."""
    fr, seat = bmesh.new(), bmesh.new()
    r = 0.011
    for sx in (-1, 1):
        x = 0.19 * sx
        _tube(fr, [(x, 0.21, 0.03), (x, 0.18, 0.44), (x, 0.195, 0.78), (x, 0.215, 0.86)], r)      # задня нога + стійка
        _tube(fr, [(x, -0.21, 0.03), (x, -0.16, 0.30), (x, -0.13, 0.425)], r)                      # передня нога
        _tube(fr, [(x * 0.97, -0.19, 0.425), (x * 0.97, 0.17, 0.425)], r * 0.9)                    # рама сидіння
        _rbox(fr, (x, 0.21, 0.012), (0.03, 0.035, 0.024), 0.008, 1)                                 # ковпачки ніг
        _rbox(fr, (x, -0.21, 0.012), (0.03, 0.035, 0.024), 0.008, 1)
    _tube(fr, [(-0.19, -0.19, 0.09), (0.19, -0.19, 0.09)], r * 0.85)
    _tube(fr, [(-0.19, 0.205, 0.12), (0.19, 0.205, 0.12)], r * 0.85)
    _tube(fr, [(-0.19, 0.19, 0.62), (0.19, 0.19, 0.62)], r * 0.8)
    _rbox(seat, (0, -0.01, 0.445), (0.40, 0.39, 0.035), 0.012, 2)
    prof = [(0.17 + 0.012 * math.sin(math.pi * k / 5), 0.64 + 0.22 * k / 5) for k in range(6)]
    _shell(seat, prof, 0.40, 6, cup=0.025, thick=0.012)
    hm = hero_mesh("chair_folding")                     # героїчна модель (ring_hero.py): ті самі габарити, запечена деталь
    if hm is not None:
        fr.free()
        seat.free()
        lod0 = hm.copy()
        lod0.name = "prop_chair_folding_LOD0"
        lod0.materials.clear()
        lod0.materials.append(material("Hero_chair_folding__frame__obj"))   # колір сидіння — колір екземпляра (маска B)
    else:
        lod0 = _mesh("prop_chair_folding_LOD0", [("Chair_Frame", fr), ("Prop_Seat", seat)])
    return lod0, _lod1_mesh("prop_chair_folding_LOD1", 0.45, 0.19, 0.74, 0.24)

def build_seat_stadium():
    """Крісло трибуни: литий корпус (сидіння й спинка одним профілем, прогин посередині), кронштейн до сходинки."""
    fr, seat = bmesh.new(), bmesh.new()
    prof = [(-0.21, 0.43), (-0.19, 0.445), (-0.08, 0.44), (0.05, 0.43), (0.12, 0.44), (0.16, 0.52),
            (0.18, 0.62), (0.2, 0.72), (0.22, 0.8)]
    _shell(seat, prof, 0.44, 7, cup=0.03, thick=0.015)
    _rbox(fr, (0, 0.08, 0.2), (0.09, 0.06, 0.4), 0.01, 1)                                       # стояк
    _rbox(fr, (0, 0.08, 0.02), (0.2, 0.16, 0.03), 0.008, 1)                                      # п'ята на сходинці
    _rbox(fr, (0, 0.0, 0.41), (0.32, 0.05, 0.04), 0.01, 1)                                       # поперечка під сидінням
    lod0 = _mesh("prop_seat_stadium_LOD0", [("Chair_Frame", fr), ("Prop_Seat", seat)])
    return lod0, _lod1_mesh("prop_seat_stadium_LOD1", 0.44, 0.19, 0.66, 0.36)

def _lod1_mesh(name, zs, yb, zb, hb):
    bm = bmesh.new()
    w = 0.22
    for pts in ([(-w, -0.2, zs), (w, -0.2, zs), (w, 0.15, zs), (-w, 0.15, zs)],
                [(-w, yb, zb - hb / 2), (-w, yb, zb + hb / 2), (w, yb, zb + hb / 2), (w, yb, zb - hb / 2)]):
        bm.faces.new([bm.verts.new(p) for p in pts])
    return _mesh(name, [("Prop_Seat", bm)])

def build_bag():
    """Рюкзак під сидінням стільця (між ніжками): корпус із заокругленнями, кишеня, лямки."""
    b, s_ = bmesh.new(), bmesh.new()
    _rbox(b, (0, 0.0, 0.16), (0.28, 0.16, 0.3), 0.04, 2)
    _rbox(b, (0, -0.09, 0.11), (0.2, 0.04, 0.14), 0.015, 2)
    for sx in (-0.07, 0.07):
        _tube(s_, [(sx, 0.08, 0.29), (sx, 0.11, 0.2), (sx, 0.095, 0.06)], 0.012, 5)
    lod0 = _mesh("prop_bag_LOD0", [("Prop_Seat", b), ("Cable_Black", s_)])
    bm1 = bmesh.new()
    _rbox(bm1, (0, 0, 0.16), (0.28, 0.16, 0.3), 0.0, 1)
    return lod0, _mesh("prop_bag_LOD1", [("Prop_Seat", bm1)])

def build_jacket():
    """Куртка, перекинута через спинку стільця: складка зверху й пола позаду (не заходить на місце людини)."""
    b = bmesh.new()
    prof = [(0.18, 0.885), (0.205, 0.9), (0.235, 0.885), (0.25, 0.8), (0.255, 0.68), (0.25, 0.55), (0.26, 0.47)]
    _shell(b, prof, 0.44, 8, cup=-0.015, thick=0.01)
    lod0 = _mesh("prop_jacket_LOD0", [("Prop_Seat", b)])
    b1 = bmesh.new()
    b1.faces.new([b1.verts.new(p) for p in ((-0.22, 0.25, 0.47), (0.22, 0.25, 0.47), (0.22, 0.24, 0.89), (-0.22, 0.24, 0.89))])
    return lod0, _mesh("prop_jacket_LOD1", [("Prop_Seat", b1)])

PROPS = {}

def props_library():
    """Збирає моделі один раз за запуск; повертає {ім'я: (LOD0, LOD1)} (сітки Blender)."""
    if not PROPS:
        PROPS["chair_folding"] = build_chair_folding()
        PROPS["seat_stadium"] = build_seat_stadium()
        PROPS["bag"] = build_bag()
        PROPS["jacket"] = build_jacket()
    return PROPS

def export_props(out_dir):
    """Assets/Art/Ring/Props/props.fbx: для кожної моделі порожній «prop_<ім'я>» з дітьми _LOD0 і _LOD1."""
    lib = props_library()
    col = bpy.data.collections.new("PropsExport")
    bpy.context.scene.collection.children.link(col)
    obs = []
    for k, (name, (m0, m1)) in enumerate(lib.items()):
        root = bpy.data.objects.new("prop_" + name, None)
        col.objects.link(root)
        root.location = (k * 1.0, 0, 0)
        obs.append(root)
        for m in (m0, m1):
            o = bpy.data.objects.new(m.name, m)
            col.objects.link(o)
            o.parent = root
            obs.append(o)
    bpy.ops.object.select_all(action='DESELECT')
    for o in obs:
        o.select_set(True)
    os.makedirs(out_dir, exist_ok=True)
    path = os.path.join(out_dir, "props.fbx")
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
                             axis_forward='-Z', axis_up='Y', object_types={'EMPTY', 'MESH'}, mesh_smooth_type='FACE',
                             path_mode='RELATIVE', embed_textures=False, bake_space_transform=False)
    for o in obs:
        bpy.data.objects.remove(o, do_unlink=True)
    bpy.data.collections.remove(col)
    tris = {n: (sum(len(p.vertices) - 2 for p in m0.polygons), sum(len(p.vertices) - 2 for p in m1.polygons))
            for n, (m0, m1) in lib.items()}
    return path, tris
