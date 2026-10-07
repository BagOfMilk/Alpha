# Модульний гардероб Alpha: тіла культур, одяг епох, броня, зброя, бороди — на одному скелеті
# (риг MPFB «game_engine»). Трек V, Поправки №18–№19.
#
# Запуск у Blender (MPFB увімкнений, CC0-паки MakeHuman встановлені):
#   exec(open(r"<repo>/tools/blender/alpha_people.py", encoding="utf-8").read())
#   exec(open(r"<repo>/tools/blender/alpha_wardrobe.py", encoding="utf-8").read())
#   build_kit("m"); build_kit("f")
#
# Одяг і броня — «оболонки» тіла: грані тіла-проксі обраних кісток зсуваються назовні по нормалях,
# за потреби подовжуються вниз (поли туніки, кафтана, плаща); групи вершин тіла зберігаються, тож
# річ одразу скінена на той самий скелет. Шоломи, щити, зброя — власні меші, на 100 % прив'язані до
# кістки (head, lowerarm_l, hand_r). Тканини — нейтральні текстури Poly Haven (CC0): колір задає гра
# множенням базового кольору матеріалу, тож одна сітка = будь-яка барва культури.
# Детерміновано: жодного random.

import bpy, bmesh, math, os
from mathutils import Vector, Matrix

KIT = "Alpha_Kit"

# Частина -> (матеріал Poly Haven або процедурний, розмір плитки текстури в м)
FABRICS = {
    "linen":   ("rough_linen", 0.45),
    "wool":    ("poly_wool_herringbone", 0.45),
    "fleece":  ("caban", 0.45),
    "leather": ("brown_leather", 0.5),
    "steel":   ("metal_plate_02", 0.8),
    "mail":    ("M_Chainmail", 0.12),
    "quilt":   ("M_Quilt", 0.3),
    "wood":    ("weathered_planks", 1.0),
    "hair":    ("M_HairCard", 0.2),
}

TORSO = {"spine_01", "spine_02", "spine_03", "clavicle_l", "clavicle_r"}
NECK = {"neck_01"}
UPPER_ARMS = {"upperarm_l", "upperarm_r"}
LOWER_ARMS = {"lowerarm_l", "lowerarm_r"}
HANDS_PREFIX = ("hand_", "index_", "middle_", "pinky_", "ring_", "thumb_")
PELVIS = {"pelvis"}
THIGHS = {"thigh_l", "thigh_r"}
CALVES = {"calf_l", "calf_r"}
FEET = {"foot_l", "foot_r", "ball_l", "ball_r"}

# ---------------------------------------------------------------- службове

def kit_collection():
    col = bpy.data.collections.get(KIT)
    if col is None:
        col = bpy.data.collections.new(KIT)
        bpy.context.scene.collection.children.link(col)
    bpy.context.view_layer.active_layer_collection = bpy.context.view_layer.layer_collection.children[KIT]
    return col

def _fabric_material(kind):
    name, _ = FABRICS[kind]
    m = bpy.data.materials.get(name)
    if m is None:
        m = bpy.data.materials.new(name); m.use_nodes = True; m.use_fake_user = True
    return m

def _dominant(ob, bone_names):
    names = {g.index: g.name for g in ob.vertex_groups}
    body_idx = ob.vertex_groups["body"].index if "body" in ob.vertex_groups else None
    out = []
    for v in ob.data.vertices:
        if body_idx is not None and not any(g.group == body_idx and g.weight > 0 for g in v.groups):
            out.append(None); continue                    # допоміжна геометрія MPFB (очі, зуби, «спідниця»)
        cands = [g for g in v.groups if names.get(g.group) in bone_names]
        best = max(cands, key=lambda g: g.weight, default=None)
        out.append(names[best.group] if best else None)
    return out

def _new_skinned(name, bm, src, rig, kind):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new(name, me)
    kit_collection().objects.link(ob)
    for g in src.vertex_groups:
        ob.vertex_groups.new(name=g.name)
    ob.parent = rig
    mod = ob.modifiers.new("Armature", 'ARMATURE'); mod.object = rig
    ob["kit_part"] = name.split(".", 1)[1] if "." in name else name
    ob["kit_kind"] = kind
    _uv_triplanar(ob, FABRICS[kind][1])
    ob.data.materials.clear(); ob.data.materials.append(_fabric_material(kind))
    for p in ob.data.polygons:
        p.use_smooth = True
    return ob

def _uv_triplanar(ob, tile):
    bm = bmesh.new(); bm.from_mesh(ob.data)
    uv = bm.loops.layers.uv.verify()
    for f in bm.faces:
        n = f.normal
        ax = max(range(3), key=lambda k: abs(n[k]))
        for l in f.loops:
            co = l.vert.co
            u, v = {0: (co.y, co.z), 1: (co.x, co.z), 2: (co.x, co.y)}[ax]
            l[uv].uv = (u / tile, v / tile)
    bm.to_mesh(ob.data); bm.free()

def _rigid(name, bm, rig, bone, kind, body):
    """Жорстка річ (шолом, щит, зброя): усі вершини на 100 % у групі кістки."""
    ob = _new_skinned(name, bm, body, rig, kind)
    g = ob.vertex_groups[bone]
    g.add(list(range(len(ob.data.vertices))), 1.0, 'REPLACE')
    return ob

def _bone_frame(rig, bone):
    b = rig.data.bones[bone]
    return b.head_local.copy(), b.tail_local.copy(), b.matrix_local.copy()

# ---------------------------------------------------------------- тіло набору

def kit_body(gender, culture=None):
    """Нейтральне тіло набору (стать m/f) — під нього кроїться весь гардероб."""
    cid = f"kit_{gender}"
    spec = dict(g=1.0 if gender == "m" else 0.0, age=0.5, mus=0.5, w=0.5, h=0.5, race=(0.34, 0.33, 0.33),
                skin="young_caucasian_male" if gender == "m" else "young_caucasian_female",
                proxy="male1591" if gender == "m" else "female1605")
    return _make_body(cid, spec)

def _make_body(cid, spec):
    col = kit_collection()
    for o in [o for o in bpy.data.objects if o.get("alpha_char") == cid
              or (o.name == cid or o.name.startswith(cid + "."))]:
        bpy.data.objects.remove(o, do_unlink=True)
    for me in [m for m in bpy.data.meshes if m.users == 0]:
        bpy.data.meshes.remove(me)
    s = bpy.context.scene
    s.MPFB_NH_scale_factor = 'METER'; s.MPFB_NH_load_clothes = False
    bpy.ops.mpfb.create_human()
    base = bpy.context.active_object
    hp = _svc("entities.objectproperties", "HumanObjectProperties")
    ts = _svc("services.targetservice", "TargetService")
    a, asn, c = spec["race"]; tot = float(a + asn + c) or 1.0
    for k, v in (("gender", spec["g"]), ("age", spec["age"]), ("muscle", spec["mus"]), ("weight", spec["w"]),
                 ("height", spec["h"]), ("african", a / tot), ("asian", asn / tot), ("caucasian", c / tot)):
        hp.set_value(k, v, entity_reference=base)
    for k, v in spec.get("face", {}).items():   # тонкі ручки обличчя MPFB, якщо задані
        try:
            hp.set_value(k, v, entity_reference=base)
        except Exception:
            pass
    ts.reapply_macro_details(base)
    s.MPFB_ADR_standard_rig = 'game_engine'
    _activate(base); bpy.ops.mpfb.add_standard_rig()
    rig = base.parent
    _activate(base); bpy.ops.mpfb.load_library_skin(filepath=_asset("skins", spec["skin"], ".mhmat"))
    _activate(base); bpy.ops.mpfb.load_library_proxy(filepath=_asset("proxymeshes", spec["proxy"], ".proxy"), object_type="Proxymeshes")
    _activate(base); bpy.ops.mpfb.load_library_clothes(filepath=_asset("eyes", "low-poly", ".mhclo"), object_type="Eyes", material_type="MAKESKIN")
    # Брови й вії (CC0 MakeHuman): без них обличчя читалось манекеном (власник 07.10.2026: «чому все так погано?»).
    for kind, name, otype in (("eyebrows", spec.get("brows", "eyebrow001"), "Eyebrows"), ("eyelashes", "eyelashes01", "Eyelashes")):
        try:
            _activate(base); bpy.ops.mpfb.load_library_clothes(filepath=_asset(kind, name, ".mhclo"), object_type=otype, material_type="MAKESKIN")
        except Exception as e:
            print("brows fail", kind, name, e)
    rig.name = cid
    body = None
    for o in list(rig.children):
        o["alpha_char"] = cid
        for m in [m for m in o.modifiers if m.type == 'SUBSURF']:
            o.modifiers.remove(m)
        if o is base:
            o.name = cid + ".basemesh"; o.hide_set(True); o.hide_render = True
        elif o.name.startswith(spec["proxy"]) or spec["proxy"] in o.name:
            o.name = cid + ".body"; body = o
            o.data.materials.clear()
            for m in base.data.materials:
                o.data.materials.append(m)
        else:
            o.name = cid + "." + o.name.split(".", 1)[-1]
    rig["alpha_char"] = cid
    return rig, body

# ---------------------------------------------------------------- оболонки (одяг і броня)

def _source(rig):
    """Джерело крою — повна базова сітка MPFB (18 тис. граней): точний відбір і рівні поли."""
    return bpy.data.objects[rig.name + ".basemesh"]

def _shaped_coords(src):
    """Вершини базової сітки З формами тіла (shape keys MPFB: стать, раса, м'язи, вага) — як їх обчислює
    сам Blender (модифікатори на мить вимкнено, щоб топологія збіглась). Сира сітка — нейтральне тіло;
    оболонки з неї відходили від справжнього тіла в середньому на 6 см (до 12) — «одяг рваний і ніби
    прозорий» (власник 07.10.2026). Ручна сума ключів дала хибну мітку рота (вуса на переніссі)."""
    saved = [(m, m.show_viewport) for m in src.modifiers]
    for m, _ in saved:
        m.show_viewport = False
    try:
        dg = bpy.context.evaluated_depsgraph_get()
        dg.update()
        ev = src.evaluated_get(dg)
        me = ev.to_mesh()
        co = [v.co.copy() for v in me.vertices]
        ev.to_mesh_clear()
    finally:
        for m, vis in saved:
            m.show_viewport = vis
    if len(co) != len(src.data.vertices):
        raise RuntimeError(f"{src.name}: форма {len(co)} вершин, сітка {len(src.data.vertices)}")
    return co

def _body_verts(src):
    """Індекси вершин самого тіла: допоміжні сітки MPFB (колготи, спідниця, шапка волосся — ховає маска
    «Hide helpers») у крій потрапляти не мають — інакше подвоєні поверхні просвічують."""
    keep = set(range(len(src.data.vertices)))
    for m in src.modifiers:
        # лише маска допоміжних сіток: «Hide base mesh» ховає саме тіло (у кадрі — проксі)
        if m.type != 'MASK' or "helper" not in m.name.lower() or not m.vertex_group:
            continue
        g = src.vertex_groups.get(m.vertex_group)
        if g is None:
            continue
        member = set()
        for v in src.data.vertices:
            for e in v.groups:
                if e.group == g.index and e.weight > 0.0:
                    member.add(v.index); break
        keep &= (set(range(len(src.data.vertices))) - member) if m.invert_vertex_group else member
    return keep

def shell(rig, body, part, kind, bones, offset, hem=None, cut=None, flare=1.12, rings=3, budget=2000):
    """Річ-оболонка: грані тіла, усі вершини яких належать кісткам `bones` (і проходять `cut`),
    зсунуті на `offset` назовні; hem=(довжина, z_max) — подовжити нижній край (поли)."""
    src = _source(rig)
    dom = _dominant(src, set(rig.data.bones.keys()))
    shaped = _shaped_coords(src)
    body_only = _body_verts(src)
    bm = bmesh.new(); bm.from_mesh(src.data)
    bm.verts.ensure_lookup_table()
    for v in bm.verts:
        v.co = shaped[v.index]
    def sel(v):
        d = dom[v.index]
        if d is None or v.index not in body_only:
            return False
        ok = d in bones or (any(d.startswith(p) for p in HANDS_PREFIX) and "hands" in bones)
        return ok and (cut is None or cut(v.co))
    keep = {v.index for v in bm.verts if sel(v)}
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if not all(v.index in keep for v in f.verts)], context='FACES')
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
    _smooth_boundary(bm)
    bm.normal_update()
    for v in bm.verts:
        v.co += v.normal * offset
    if hem:
        length, zmax = hem
        edges = [e for e in bm.edges if e.is_boundary and max(v.co.z for v in e.verts) < zmax]
        if edges:
            vs = {v for e in edges for v in e.verts}
            cx = sum(v.co.x for v in vs) / len(vs); cy = sum(v.co.y for v in vs) / len(vs)
            cur = edges
            for r in range(rings):
                res = bmesh.ops.extrude_edge_only(bm, edges=cur)
                nv = [g for g in res["geom"] if isinstance(g, bmesh.types.BMVert)]
                k = flare ** (1.0 / rings)
                for v in nv:
                    v.co.z -= length / rings
                    v.co.x = cx + (v.co.x - cx) * k
                    v.co.y = cy + (v.co.y - cy) * k
                cur = [g for g in res["geom"] if isinstance(g, bmesh.types.BMEdge) and g.is_boundary
                       and all(v in nv for v in g.verts)]
    bm.normal_update()
    ob = _new_skinned(f"{rig.name}.{part}", bm, src, rig, kind)
    _budget(ob, budget)
    return ob

def _smooth_boundary(bm, iters=6):
    """Край оболонки — зигзаг по ребрах тіла: вирівнюємо вершини межі вздовж самої межі (Лапласіан по
    петлі), решта сітки не рухається."""
    for _ in range(iters):
        new = {}
        for v in bm.verts:
            if not v.is_boundary:
                continue
            nb = [e.other_vert(v) for e in v.link_edges if e.is_boundary]
            if len(nb) == 2:
                new[v] = v.co * 0.5 + (nb[0].co + nb[1].co) * 0.25
        for v, c in new.items():
            v.co = c

def _budget(ob, limit):
    tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
    if tris > limit:
        mod = ob.modifiers.new("lod", 'DECIMATE'); mod.ratio = limit / tris
        with bpy.context.temp_override(object=ob, active_object=ob, selected_objects=[ob]):
            bpy.ops.object.modifier_move_to_index(modifier=mod.name, index=0)
            bpy.ops.object.modifier_apply(modifier=mod.name)
        _uv_triplanar(ob, FABRICS[ob["kit_kind"]][1])

def _z_of(rig, bone, at="head"):
    b = rig.data.bones[bone]
    return (b.head_local if at == "head" else b.tail_local).z

def wardrobe(rig, body):
    """Одяг епох і броня під тіло набору. Повертає {назва: обʼєкт}."""
    zk = _z_of(rig, "calf_l")                 # коліно
    za = _z_of(rig, "foot_l")                 # кісточка
    zw = _z_of(rig, "spine_01")               # талія
    zh = _z_of(rig, "pelvis")                 # стегна
    shin_cut = lambda co: co.z < zk - 0.12
    out = {}
    # Верхній одяг кроїться з тулуба разом з верхом таза (вище кульшових суглобів) — нижній край
    # тоді суцільне кільце навколо стегон, і поли подовжуються без проріх на попереку.
    top = TORSO | PELVIS | THIGHS          # стегна — бо верх сідниць належить кісткам стегна
    hips = lambda co: co.z > zh - 0.03
    hz = zh + 0.04
    out["shirt"] = shell(rig, body, "shirt", "linen", top | UPPER_ARMS | LOWER_ARMS, 0.010, cut=hips, hem=(0.16, hz), flare=1.06)
    out["tunic"] = shell(rig, body, "tunic", "wool", top | UPPER_ARMS, 0.016, cut=hips, hem=(0.36, hz), flare=1.18)
    out["kaftan"] = shell(rig, body, "kaftan", "wool", top | UPPER_ARMS | LOWER_ARMS, 0.022, cut=hips, hem=(zh - za - 0.12, hz), flare=1.35, rings=5)
    out["robe"] = shell(rig, body, "robe", "linen", top | NECK | UPPER_ARMS | LOWER_ARMS, 0.024, cut=hips, hem=(zh - za - 0.04, hz), flare=1.45, rings=5)
    out["vest"] = shell(rig, body, "vest", "fleece", top, 0.028, cut=hips, hem=(0.06, hz), flare=1.04)
    # --- низ
    out["trousers"] = shell(rig, body, "trousers", "wool", PELVIS | THIGHS | CALVES, 0.010, cut=lambda co: co.z > za + 0.10)
    out["sharovary"] = shell(rig, body, "sharovary", "linen", PELVIS | THIGHS | CALVES, 0.035, cut=lambda co: co.z > za + 0.12)
    out["skirt_long"] = shell(rig, body, "skirt_long", "linen", PELVIS, 0.02, hem=(zh - za - 0.05, zh + 0.02), flare=1.55, rings=5,
                              cut=lambda co: co.z > zh - 0.06)
    # --- взуття
    out["boots"] = shell(rig, body, "boots", "leather", CALVES | FEET, 0.012, cut=shin_cut)
    out["shoes"] = shell(rig, body, "shoes", "leather", FEET, 0.008)
    # --- плащ (спина від плечей, довгий)
    out["cloak"] = shell(rig, body, "cloak", "wool", TORSO | UPPER_ARMS, 0.05,
                         cut=lambda co: co.y > _back_y(rig) and co.z > zw + 0.08,
                         hem=(zw - zk + 0.35, zw + 0.2), flare=1.0, rings=4)   # без розширення: поли здувались грудками на стегнах
    # --- броня
    out["gambeson"] = shell(rig, body, "gambeson", "quilt", top | UPPER_ARMS, 0.026, cut=hips, hem=(0.24, hz), flare=1.12)
    out["mail"] = shell(rig, body, "mail", "mail", top | UPPER_ARMS | LOWER_ARMS, 0.032, cut=hips, hem=(0.3, hz), flare=1.15)
    out["cuirass"] = shell(rig, body, "cuirass", "steel", TORSO, 0.05, cut=lambda co: co.z > zw - 0.02)
    out["bracers"] = shell(rig, body, "bracers", "leather", LOWER_ARMS, 0.016, cut=lambda co: True)
    out["greaves"] = shell(rig, body, "greaves", "steel", CALVES, 0.022, cut=lambda co: co.y < _front_y(rig) + 0.06 and co.z > za + 0.08)
    return out

# Ключ набору -> річ бібліотеки MPFB (CC0, справжній крій і розгортка) для статі m/f. Ключі ті самі, що в
# ядрі (KitParts.Clothing) — гра й сейви не змінюються. Чого в бібліотеці немає (жилет у чоловіків, плащ,
# броня), лишається оболонкою. Власник 07.10.2026: оболонки «рвані й просвічують» — «роби краще».
LIBRARY_CLOTHES = {
    "shirt":      {"m": "toigo_fisherman_sweater", "f": "toigo_fisherman_sweater"},
    "tunic":      {"m": "toigo_shift_dress", "f": "toigo_shift_dress"},
    "robe":       {"m": "mindfront_kimono", "f": "mindfront_kimono"},
    "kaftan":     {"m": "mindfront_kimono", "f": "mindfront_kimono"},
    "trousers":   {"m": "toigo_wool_pants", "f": "toigo_wool_pants"},
    "sharovary":  {"m": "toigo_harem_pants", "f": "toigo_harem_pants"},
    "skirt_long": {"f": "toigo_long_full_skirt"},
    "vest":       {"f": "toigo_bodice-style_top"},
    "boots":      {"m": "culturalibre_hero_boots_1", "f": "culturalibre_heroine_boots_1"},   # male_boots — 30 тис. трикутників, після спрощення шипи
    "shoes":      {"m": "toigo_mj_cloth_shoes", "f": "toigo_mj_cloth_shoes"},
}
LIBRARY_FABRIC = {"shirt": "linen", "tunic": "wool", "robe": "linen", "kaftan": "wool", "trousers": "wool",
                  "sharovary": "linen", "skirt_long": "linen", "vest": "fleece", "boots": "leather", "shoes": "leather"}

def library_wardrobe(rig, gender, items):
    """Замінює оболонки на речі з бібліотеки MPFB, де є відповідник. Тканина — наш нейтральний матеріал
    (колір задає гра множником), розгортка — рідна бібліотечна. Повертає {ключ: обʼєкт}."""
    base = bpy.data.objects[rig.name + ".basemesh"]
    out = {}
    for key, by_gender in LIBRARY_CLOTHES.items():
        asset = by_gender.get(gender)
        if not asset:
            continue
        before = set(bpy.data.objects)
        _activate(base)
        try:
            bpy.ops.mpfb.load_library_clothes(filepath=_asset("clothes", asset, ".mhclo"), object_type="Clothes", material_type="MAKESKIN")
        except Exception as e:
            print("library fail", key, asset, e); continue
        new = [o for o in set(bpy.data.objects) - before if o.type == 'MESH']
        if not new:
            print("library empty", key, asset); continue
        old = items.get(key) or bpy.data.objects.get(f"{rig.name}.{key}")
        if old is not None:
            bpy.data.objects.remove(old, do_unlink=True)
        o = new[0]
        for m in [m for m in o.modifiers if m.type == 'SUBSURF']:
            o.modifiers.remove(m)
        o.name = f"{rig.name}.{key}"; o["kit_part"] = key; o["kit_kind"] = LIBRARY_FABRIC[key]; o["alpha_char"] = rig.name
        o.data.materials.clear(); o.data.materials.append(_fabric_material(LIBRARY_FABRIC[key]))
        _budget(o, 4000)
        # Рідна розгортка бібліотеки — під її власну текстуру: наша тканина лягала на річ однією плиткою, і
        # плями льону розтягувались на пів рукава з різкими краями на швах (лукбук 07.10.2026).
        _uv_triplanar(o, FABRICS[LIBRARY_FABRIC[key]][1])
        out[key] = o
    items.update(out)
    return out

def _back_y(rig):
    return rig.data.bones["spine_03"].head_local.y + 0.02

def _front_y(rig):
    return rig.data.bones["calf_l"].head_local.y

# ---------------------------------------------------------------- жорсткі речі

def _helmet_mesh(kind, r):
    m = bmesh.new()
    if kind in ("spangen", "conical", "kettle", "kabuto"):
        segs, rings = 16, 6
        peak = {"spangen": 1.15, "conical": 1.6, "kettle": 1.0, "kabuto": 0.95}[kind]
        top = m.verts.new((0, 0, r * peak))
        prev = None; ringsv = []
        for i in range(1, rings + 1):
            t = i / rings
            ang = t * math.pi / 2
            rr = r * math.sin(ang); z = r * peak * math.cos(ang)
            ring = [m.verts.new((rr * math.cos(2 * math.pi * k / segs), rr * math.sin(2 * math.pi * k / segs), z)) for k in range(segs)]
            if prev is None:
                for k in range(segs):
                    m.faces.new((top, ring[k], ring[(k + 1) % segs]))
            else:
                for k in range(segs):
                    m.faces.new((prev[k], prev[(k + 1) % segs], ring[(k + 1) % segs], ring[k]))
            prev = ring; ringsv.append(ring)
        if kind == "kettle":                                        # широкі криси
            outer = [m.verts.new((r * 1.7 * math.cos(2 * math.pi * k / segs), r * 1.7 * math.sin(2 * math.pi * k / segs), -r * 0.12)) for k in range(segs)]
            for k in range(segs):
                m.faces.new((prev[k], prev[(k + 1) % segs], outer[(k + 1) % segs], outer[k]))
        if kind == "kabuto":                                        # ступінчастий назатильник
            cur = prev
            for step in range(3):
                nxt = [m.verts.new((v.co.x * 1.18, v.co.y * 1.18, v.co.z - r * 0.28)) for v in cur]
                for k in range(segs):
                    if cur[k].co.y < -r * 0.2:   # спереду відкрито
                        continue
                    m.faces.new((cur[k], cur[(k + 1) % segs], nxt[(k + 1) % segs], nxt[k]))
                cur = nxt
        if kind in ("spangen", "conical"):                         # наносник
            r0 = bmesh.ops.create_cube(m, size=1.0)
            bmesh.ops.scale(m, vec=(0.03, 0.02, r * 0.75), verts=r0["verts"])
            bmesh.ops.translate(m, vec=(0, -r * 1.0, -r * 0.32), verts=r0["verts"])
        if kind == "spangen":                                       # бармиця
            cur = prev
            for step in range(2):
                nxt = [m.verts.new((v.co.x * 1.06, v.co.y * 1.06, v.co.z - r * 0.32)) for v in cur]
                for k in range(segs):
                    if cur[k].co.y < -r * 0.45:
                        continue
                    m.faces.new((cur[k], cur[(k + 1) % segs], nxt[(k + 1) % segs], nxt[k]))
                cur = nxt
    elif kind == "turban":
        for i in range(5):
            res = bmesh.ops.create_cone(m, cap_ends=False, segments=16, radius1=r * (1.12 - 0.05 * i), radius2=r * (1.05 - 0.07 * i), depth=r * 0.32)
            bmesh.ops.rotate(m, cent=(0, 0, 0), matrix=Matrix.Rotation(0.12 * (1 if i % 2 else -1), 3, 'X'), verts=res["verts"])
            bmesh.ops.translate(m, vec=(0, 0, r * (0.05 + 0.26 * i)), verts=res["verts"])
    return m

def _skull(rig):
    """Верх черепа і центр голови за базовою сіткою (точніше, ніж кінець кістки head)."""
    src = _source(rig)
    dom = _dominant(src, set(rig.data.bones.keys()))
    shaped, body_only = _shaped_coords(src), _body_verts(src)
    pts = [shaped[v.index] for v in src.data.vertices if dom[v.index] == "head" and v.index in body_only]
    top = max(p.z for p in pts)
    crown = [p for p in pts if p.z > top - 0.06]
    cx = sum(p.x for p in crown) / len(crown); cy = sum(p.y for p in crown) / len(crown)
    return Vector((cx, cy, top))

def helmets(rig, body):
    sk = _skull(rig)
    r = 0.112
    out = {}
    for kind, part in (("spangen", "helm_spangen"), ("conical", "helm_conical"), ("kettle", "helm_kettle"),
                       ("kabuto", "helm_kabuto"), ("turban", "turban")):
        m = _helmet_mesh(kind, r)
        # купол сідає на голову: верх шолома на 1–2 см над тім'ям, тюрбан — нижче й ширший
        # нижній край купола — на рівні лоба (≈ 0.55 r під тім'ям), тюрбан сидить вище брів
        lift = sk.z - r * (1.15 if kind == "spangen" else 1.6 if kind == "conical" else 1.0) + 0.045
        if kind == "kabuto":
            lift += 0.02
        if kind == "turban":
            lift = sk.z - r * 0.62
        bmesh.ops.translate(m, vec=(sk.x, sk.y + 0.008, lift), verts=m.verts[:])
        out[part] = _rigid(f"{rig.name}.{part}", m, rig, "head", "linen" if kind == "turban" else "steel", body)
    return out

def shields(rig, body):
    h, t, mat = _bone_frame(rig, "lowerarm_l")
    mid = (h + t) / 2
    out = {}
    for part, shape in (("shield_round", "round"), ("shield_kite", "kite"), ("buckler", "buckler")):
        m = bmesh.new()
        if shape in ("round", "buckler"):
            rad = 0.42 if shape == "round" else 0.2
            res = bmesh.ops.create_circle(m, cap_ends=True, segments=20, radius=rad)
            ext = bmesh.ops.extrude_face_region(m, geom=m.faces[:])
            bmesh.ops.translate(m, vec=(0, 0, 0.025), verts=[g for g in ext["geom"] if isinstance(g, bmesh.types.BMVert)])
            boss = bmesh.ops.create_uvsphere(m, u_segments=10, v_segments=6, radius=0.07 if shape == "round" else 0.08)
            bmesh.ops.translate(m, vec=(0, 0, 0.03), verts=boss["verts"])
        else:
            pts = [(0, 0.5), (0.27, 0.38), (0.25, 0.0), (0.0, -0.55), (-0.25, 0.0), (-0.27, 0.38)]
            vs = [m.verts.new((x, y, 0)) for x, y in pts]
            f = m.faces.new(vs)
            ext = bmesh.ops.extrude_face_region(m, geom=[f])
            bmesh.ops.translate(m, vec=(0, 0, 0.03), verts=[g for g in ext["geom"] if isinstance(g, bmesh.types.BMVert)])
        # щит на зовнішньому боці передпліччя: площина щита ⟂ осі X тіла
        bmesh.ops.rotate(m, cent=(0, 0, 0), matrix=Matrix.Rotation(math.pi / 2, 3, 'Y'), verts=m.verts[:])
        bmesh.ops.translate(m, vec=(mid.x + 0.07, mid.y, mid.z), verts=m.verts[:])
        out[part] = _rigid(f"{rig.name}.{part}", m, rig, "lowerarm_l", "wood" if shape != "buckler" else "steel", body)
    return out

def _blade(m, length, width, curve=0.0, grip=0.2, guard=0.18, pommel=True):
    """Клинок уздовж +Z від нуля (руків'я внизу): руків'я, гарда, лезо (з вигином)."""
    def box(c, s):
        r = bmesh.ops.create_cube(m, size=1.0); bmesh.ops.scale(m, vec=s, verts=r["verts"]); bmesh.ops.translate(m, vec=c, verts=r["verts"])
    box((0, 0, grip / 2), (0.03, 0.03, grip))
    box((0, 0, grip + 0.015), (guard, 0.035, 0.03))
    if pommel:
        p = bmesh.ops.create_uvsphere(m, u_segments=8, v_segments=5, radius=0.025); bmesh.ops.translate(m, vec=(0, 0, -0.01), verts=p["verts"])
    n = 8
    prev = None
    for i in range(n + 1):
        t = i / n
        z = grip + 0.03 + length * t
        wv = width * (1 - 0.85 * t ** 3)
        x = curve * length * t * t
        row = [m.verts.new((x - wv / 2, 0, z)), m.verts.new((x, -0.006, z)), m.verts.new((x + wv / 2, 0, z)), m.verts.new((x, 0.006, z))]
        if prev:
            for k in range(4):
                m.faces.new((prev[k], prev[(k + 1) % 4], row[(k + 1) % 4], row[k]))
        prev = row

def weapons(rig, body):
    """Зброя в правій руці (lowerarm/hand_r): клинок уздовж осі кисті."""
    out = {}
    grip, blade = _grip(rig, "r")
    specs = {
        "sword":  lambda m: _blade(m, 0.78, 0.05),
        "sabre":  lambda m: _blade(m, 0.8, 0.04, curve=0.12),
        "katana": lambda m: _blade(m, 0.72, 0.032, curve=0.05, grip=0.27, guard=0.07, pommel=False),
        "dagger": lambda m: _blade(m, 0.25, 0.035, guard=0.09),
        "axe":    lambda m: _axe(m),
        "mace":   lambda m: _mace(m),
        "spear":  lambda m: _spear(m),
        "club":   lambda m: _club(m),
    }
    for part, fn in specs.items():
        m = bmesh.new(); fn(m)
        # зброю змодельовано вздовж +Z (руків'я внизу); розвертаємо Z → напрям клинка, тримаємо за руків'я
        rot = blade.to_track_quat('Z', 'Y').to_matrix().to_4x4()
        bmesh.ops.transform(m, matrix=rot, verts=m.verts[:])
        bmesh.ops.translate(m, vec=grip - blade * 0.1, verts=m.verts[:])
        kind = "wood" if part in ("spear", "club") else "steel"
        out["wpn_" + part] = _rigid(f"{rig.name}.wpn_{part}", m, rig, "hand_r", kind, body)
    out["wpn_bow"] = _bow(rig, body)
    out["wpn_musket"] = _musket(rig, body)
    return out

def _grip(rig, side):
    """Точка хвату (центр долоні) і напрям, у який зброя виходить з кулака: від великого пальця,
    перпендикулярно передпліччю."""
    b = rig.data.bones
    fore = (b["hand_" + side].head_local - b["lowerarm_" + side].head_local).normalized()
    th = b["thumb_01_" + side].head_local - b["hand_" + side].head_local
    up = (th - fore * th.dot(fore)).normalized()
    grip = b["hand_" + side].head_local + fore * 0.07
    return grip, up

def _axe(m):
    r = bmesh.ops.create_cone(m, cap_ends=True, segments=8, radius1=0.02, radius2=0.018, depth=0.7)
    bmesh.ops.translate(m, vec=(0, 0, 0.35), verts=r["verts"])
    vs = [m.verts.new(p) for p in [(0.02, 0, 0.6), (0.2, 0, 0.52), (0.22, 0, 0.72), (0.02, 0, 0.68)]]
    f = m.faces.new(vs); e = bmesh.ops.extrude_face_region(m, geom=[f])
    bmesh.ops.translate(m, vec=(0, 0.012, 0), verts=[g for g in e["geom"] if isinstance(g, bmesh.types.BMVert)])

def _mace(m):
    r = bmesh.ops.create_cone(m, cap_ends=True, segments=8, radius1=0.018, radius2=0.018, depth=0.55)
    bmesh.ops.translate(m, vec=(0, 0, 0.27), verts=r["verts"])
    h = bmesh.ops.create_icosphere(m, subdivisions=1, radius=0.07); bmesh.ops.translate(m, vec=(0, 0, 0.6), verts=h["verts"])

def _spear(m):
    r = bmesh.ops.create_cone(m, cap_ends=True, segments=8, radius1=0.018, radius2=0.016, depth=2.0)
    bmesh.ops.translate(m, vec=(0, 0, 0.55), verts=r["verts"])
    t = bmesh.ops.create_cone(m, cap_ends=True, segments=4, radius1=0.035, radius2=0.0, depth=0.28)
    bmesh.ops.translate(m, vec=(0, 0, 1.69), verts=t["verts"])

def _club(m):
    r = bmesh.ops.create_cone(m, cap_ends=True, segments=8, radius1=0.02, radius2=0.05, depth=0.7)
    bmesh.ops.translate(m, vec=(0, 0, 0.35), verts=r["verts"])

def _bow(rig, body):
    grip, up = _grip(rig, "l")
    m = bmesh.new(); prev = None; n = 14
    for i in range(n + 1):
        a = -0.75 + 1.5 * i / n
        z = 0.75 * math.sin(a); x = -0.18 * (1 - math.cos(a) * 1.0)
        rr = 0.016 * (1 - 0.4 * abs(a))
        ring = [m.verts.new((x + rr * math.cos(2 * math.pi * k / 6), rr * math.sin(2 * math.pi * k / 6), z)) for k in range(6)]
        if prev:
            for k in range(6):
                m.faces.new((prev[k], prev[(k + 1) % 6], ring[(k + 1) % 6], ring[k]))
        prev = ring
    s = bmesh.ops.create_cone(m, cap_ends=False, segments=3, radius1=0.002, radius2=0.002, depth=1.36)
    bmesh.ops.translate(m, vec=(0.0, 0, 0), verts=s["verts"])
    bmesh.ops.transform(m, matrix=up.to_track_quat('Z', 'Y').to_matrix().to_4x4(), verts=m.verts[:])
    bmesh.ops.translate(m, vec=grip, verts=m.verts[:])
    return _rigid(f"{rig.name}.wpn_bow", m, rig, "hand_l", "wood", body)

def _musket(rig, body):
    grip, up = _grip(rig, "r")
    fore = (rig.data.bones["hand_r"].head_local - rig.data.bones["lowerarm_r"].head_local).normalized()
    m = bmesh.new()
    st = bmesh.ops.create_cube(m, size=1.0); bmesh.ops.scale(m, vec=(0.05, 0.4, 0.09), verts=st["verts"])
    bmesh.ops.translate(m, vec=(0, 0.15, -0.02), verts=st["verts"])
    br = bmesh.ops.create_cone(m, cap_ends=True, segments=8, radius1=0.014, radius2=0.012, depth=0.95)
    bmesh.ops.rotate(m, cent=(0, 0, 0), matrix=Matrix.Rotation(math.pi / 2, 3, 'X'), verts=br["verts"])
    bmesh.ops.translate(m, vec=(0, -0.5, 0.02), verts=br["verts"])
    # ствол — уздовж передпліччя вперед (-Y моделі → напрям передпліччя)
    bmesh.ops.transform(m, matrix=(-fore).to_track_quat('Y', 'Z').to_matrix().to_4x4(), verts=m.verts[:])
    bmesh.ops.translate(m, vec=grip, verts=m.verts[:])
    return _rigid(f"{rig.name}.wpn_musket", m, rig, "hand_r", "wood", body)

# ---------------------------------------------------------------- волосся обличчя

def _mouth(rig):
    """Лінія губ і точка під носом за профілем обличчя (форма з shape keys). Мітка MPFB «joint-mouth» —
    суглоб у глибині голови, вище губ: вуса за нею лягали на перенісся (лукбук 07.10.2026)."""
    src = _source(rig)
    dom = _dominant(src, set(rig.data.bones.keys()))
    shaped, body_only = _shaped_coords(src), _body_verts(src)
    prof = [shaped[i] for i in body_only if dom[i] == "head" and abs(shaped[i].x) < 0.008]
    eye = _landmark(rig, "joint-l-eye") if "joint-l-eye" in src.vertex_groups else None
    band = [p for p in prof if eye is None or eye.z - 0.09 < p.z < eye.z]
    nose = min(band, key=lambda p: p.y)                     # кінчик носа — найдальше вперед (−Y)
    bins = {}
    for p in prof:
        if nose.z - 0.075 < p.z < nose.z:
            k = round((nose.z - p.z) / 0.002)
            bins[k] = min(bins.get(k, 1e9), p.y)            # профіль: найпередніша точка на висоті
    ks = sorted(bins)
    def deepest(lo, hi):                                     # найглибша (найбільша y) точка профілю в смузі
        cand = [k for k in ks if lo <= k * 0.002 <= hi]
        return max(cand, key=lambda k: bins[k]) if cand else None
    sub = deepest(0.006, 0.03)                               # під носом
    sto = deepest((sub or 10) * 0.002 + 0.008, (sub or 10) * 0.002 + 0.03)   # між губами
    z_sub = nose.z - (sub if sub is not None else 12) * 0.002
    z_sto = nose.z - (sto if sto is not None else 25) * 0.002
    y_lip = min((p.y for p in prof if z_sto < p.z < z_sub), default=nose.y + 0.02)
    return Vector((0.0, y_lip, z_sto)), z_sub

def facial_hair(rig, body):
    """Бороди й вуса — оболонка нижньої частини обличчя (кістка head, нижче носа, спереду)."""
    mouth, z_sub = _mouth(rig); jaw = _landmark(rig, "joint-jaw")
    fy = mouth.y
    out = {}
    # Лінія губ і точка під носом — з профілю обличчя; суглоб щелепи MPFB — задня межа бороди. Борода —
    # від щелепи вниз і щоки нижче рота (центр рота лишається відкритим), вуса — над верхньою губою.
    mz = mouth.z
    for part, zlo, zmax, depth, off in (("beard_full", mz - 0.11, mz + 0.03, 0.06, 0.012),
                                        ("beard_short", mz - 0.09, mz + 0.02, 0.015, 0.006),
                                        ("moustache", z_sub - 0.013, z_sub + 0.002, 0.0, 0.004)):   # шкіра над губою, не сама губа
        if part == "moustache":
            cut = lambda co, zlo=zlo, zmax=zmax: zlo < co.z < zmax and co.y < fy + 0.01 and abs(co.x) < 0.032
        else:
            cut = lambda co, zlo=zlo, zmax=zmax: (zlo < co.z < zmax and co.y < jaw.y + 0.02
                                                  and not (abs(co.x) < 0.028 and co.z > mz - 0.018))
        ob = shell(rig, body, part, "hair", {"head", "neck_01"}, off, cut=cut,
                   hem=(depth, zlo + 0.03) if depth else None, flare=0.85, rings=2, budget=500)
        out[part] = ob
    return out

def _landmark(rig, group):
    src = _source(rig)
    gi = src.vertex_groups[group].index
    shaped = _shaped_coords(src)                 # мітки суглобів — допоміжні вершини, але форма тіла — справжня
    pts = [shaped[v.index] for v in src.data.vertices if any(g.group == gi and g.weight > 0.5 for g in v.groups)]
    return sum(pts, Vector()) / max(1, len(pts))


# ---------------------------------------------------------------- зони тіла (ховаються під одягом)
BODY_ZONES = {
    "head": {"head", "neck_01"}, "torso": TORSO | PELVIS, "upperarms": UPPER_ARMS, "lowerarms": LOWER_ARMS,
    "hands": "hands", "thighs": THIGHS, "calves": CALVES, "feet": FEET,
}
# Річ -> зони тіла, які вона повністю закриває (гра їх вимикає, щоб тіло не випирало крізь тканину).
COVERS = {
    "shirt": ["torso", "upperarms", "lowerarms"], "tunic": ["torso", "upperarms"],
    "kaftan": ["torso", "upperarms", "lowerarms", "thighs"], "robe": ["torso", "upperarms", "lowerarms", "thighs", "calves"],
    "vest": [], "trousers": ["thighs", "calves"], "sharovary": ["thighs", "calves"], "skirt_long": ["thighs", "calves"],
    "boots": ["feet"], "shoes": ["feet"], "cloak": [], "gambeson": ["torso", "upperarms"],
    "mail": ["torso", "upperarms", "lowerarms"], "cuirass": [], "bracers": [], "greaves": [],
}

# Шари одягу від тіла назовні. Річ шару k виштовхується назовні від усіх речей нижчих шарів там, де вони
# перетинаються (у межах reach — 15 см: кімоно відходить від тіла далеко): ліф проступав крізь светр, вишивка — з-під сорочки, спідниця плямилась
# (власник 07.10.2026: «одяг рваний і ніби прозорий»). Поєднання не важливе: річ стоїть над будь-якою
# нижчою, тож без нижньої вона лише на кілька мм далі від тіла.
LAYERS = [
    {"boots", "shoes"},                                   # халяви притиснуті до ноги (tighten_boots) — штани поверх
    {"trousers", "sharovary"},
    {"shirt"},                                            # сорочка — навипуск поверх штанів
    {"embroidery_red_black", "embroidery_red_black_cuffs", "embroidery_gold", "tunic", "skirt_long",
     "gambeson", "bracers"},
    {"kaftan", "robe", "mail", "greaves"},
    {"vest", "cuirass", "iron_armrings"},
    {"sash", "boyar_belt", "carpenter_apron", "kerchief", "wolf_fur_collar", "cloak"},
]

def _outward_sign(o, body_tree):
    """+1 — нормалі речі дивляться від тіла, −1 — вивернуті (у частини бібліотечних речей, напр. чобіт).
    Більшість граней: нормаль проти напрямку «від найближчої точки тіла»."""
    mw = o.matrix_world; n3 = mw.to_3x3().inverted().transposed()
    votes = 0
    for f in list(o.data.polygons)[::max(1, len(o.data.polygons) // 400)]:
        c = mw @ f.center
        loc, _n, _i, _d = body_tree.find_nearest(c)
        if loc is None:
            continue
        votes += 1 if (n3 @ f.normal).dot(c - loc) >= 0.0 else -1
    return 1.0 if votes >= 0 else -1.0

def tighten_boots(rig, snug=0.008):
    """Халява вище щиколотки — не далі snug від тіла. Бібліотечні чоботи мають товсту халяву з відворотом, і
    вона проступала крізь штани плямами на колінах; виштовхувати штани над нею — складки й шипи (лукбук
    07.10.2026). Облягаюча халява ховається під будь-якими штанами, а без них читається як чобіт для верхової їзди."""
    from mathutils.bvhtree import BVHTree
    body = next((o for o in rig.children if o.name.endswith(".body") and o.type == 'MESH'), None)
    boots = next((o for o in rig.children if o.get("kit_part") == "boots" and o.type == 'MESH'), None)
    if body is None or boots is None:
        return 0
    bmw = body.matrix_world
    tree = BVHTree.FromPolygons([bmw @ v.co for v in body.data.vertices], [list(f.vertices) for f in body.data.polygons])
    ankle = _z_of(rig, "foot_l") + 0.04
    mw = boots.matrix_world; inv = mw.inverted()
    moved = 0
    for v in boots.data.vertices:
        p = mw @ v.co
        if p.z < ankle:
            continue
        loc, _n, _i, dist = tree.find_nearest(p)
        if loc is None or dist <= snug:
            continue
        v.co = inv @ (loc + (p - loc).normalized() * snug)
        moved += 1
    boots.data.update()
    return moved

def layer_clothes(rig, gap=0.004, reach=0.15):
    """Повертає {частина: скільки вершин зсунуто}. Вершина речі, що опинилась усередині нижчої речі (за
    нормаллю самої вершини), виходить назовні від найближчої ЗОВНІШНЬОЇ стінки нижчих речей. Лише зовнішні
    стінки (грань дивиться від тіла): у речей з товщиною (халяви) найближчою була внутрішня стінка, і халяви
    проступали крізь штани (лукбук 07.10.2026). Нормалі кожної речі орієнтовано відносно тіла — у частини
    бібліотечних речей вони вивернуті. (Промінь назовні пробували — одяг ставав грудкуватим.)"""
    from mathutils.bvhtree import BVHTree
    parts = {o.get("kit_part"): o for o in rig.children if o.get("kit_part") and o.type == 'MESH'}
    body = next((o for o in rig.children if o.name.endswith(".body") and o.type == 'MESH'), None)
    body_tree = None
    sign = {}
    if body is not None:
        bmw = body.matrix_world
        body_tree = BVHTree.FromPolygons([bmw @ v.co for v in body.data.vertices], [list(f.vertices) for f in body.data.polygons])
        for o in parts.values():
            sign[o.name] = _outward_sign(o, body_tree)
    lower, moved = [], {}
    for layer in LAYERS:
        objs = [parts[k] for k in sorted(layer) if k in parts]
        if lower:
            verts, polys = [], []
            for o in lower:
                mw = o.matrix_world; n3 = mw.to_3x3().inverted().transposed()
                sg = sign.get(o.name, 1.0)
                base = len(verts)
                verts += [mw @ v.co for v in o.data.vertices]
                for f in o.data.polygons:
                    if body_tree is not None:
                        c = mw @ f.center
                        loc, _n, _i, _d = body_tree.find_nearest(c)
                        if loc is not None and (n3 @ f.normal).dot(c - loc) * sg < 0.0:
                            continue                         # внутрішня стінка (дивиться до тіла)
                    idx = [base + i for i in f.vertices]
                    polys.append(idx if sg > 0 else idx[::-1])
            tree = BVHTree.FromPolygons(verts, polys)
            for o in objs:
                me = o.data
                mw = o.matrix_world; inv = mw.inverted(); n3 = mw.to_3x3().inverted().transposed()
                sg = sign.get(o.name, 1.0)
                # Безрукавка й плащ лягають поверх халата чи каптана — кімоно відходить від тіла до 25 см.
                part = o.get("kit_part")
                # Пояси й фартух — лише по талії: на розкльошеній туніці вони здувались грудками на 15 см.
                r = 0.25 if part in ("vest", "cloak") else 0.05 if part in ("sash", "boyar_belt", "carpenter_apron") else reach
                need = [0.0] * len(me.vertices)
                for v in me.vertices:
                    p = mw @ v.co
                    loc, nrm, _i, _d = tree.find_nearest(p, r)
                    if loc is None:
                        continue
                    nw = (n3 @ v.normal).normalized() * sg
                    side = (p - loc).dot(nw)
                    if side < gap:
                        need[v.index] = gap - side
                adj = [[] for _ in me.vertices]
                for e in me.edges:
                    a, b = e.vertices
                    adj[a].append(b); adj[b].append(a)
                for _ in range(3):                      # без сходинок між зсунутими й сусідніми вершинами
                    need = [max(need[i], 0.6 * max((need[j] for j in adj[i]), default=0.0)) for i in range(len(need))]
                for v in me.vertices:
                    if need[v.index] > 0.0:
                        nw = (n3 @ v.normal).normalized() * sg
                        v.co = inv @ (mw @ v.co + nw * need[v.index])
                me.update()
                moved[o.get("kit_part")] = sum(1 for x in need if x > 0.0)
        lower += objs
    return moved

def split_body(rig, body):
    """Тіло-проксі -> окремі сітки зон (kit_m.body_torso тощо); оригінал ховається."""
    dom = _dominant(body, set(rig.data.bones.keys()))
    out = {}
    for zone, bones in BODY_ZONES.items():
        bm = bmesh.new(); bm.from_mesh(body.data)
        bm.verts.ensure_lookup_table()
        def ok(v):
            d = dom[v.index]
            if d is None:
                return False
            if bones == "hands":
                return any(d.startswith(p) for p in HANDS_PREFIX)
            return d in bones
        # грань належить зоні за більшістю вершин — зони не перетинаються і не мають щілин
        bmesh.ops.delete(bm, geom=[f for f in bm.faces if sum(ok(v) for v in f.verts) * 2 <= len(f.verts)], context='FACES')
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
        me = bpy.data.meshes.new(f"{rig.name}.body_{zone}"); bm.to_mesh(me); bm.free()
        ob = bpy.data.objects.new(me.name, me); kit_collection().objects.link(ob)
        for g in body.vertex_groups:
            ob.vertex_groups.new(name=g.name)
        ob.parent = rig; m = ob.modifiers.new("Armature", 'ARMATURE'); m.object = rig
        for mat in body.data.materials:
            ob.data.materials.append(mat)
        ob["kit_part"] = "body_" + zone; ob["kit_kind"] = "skin"
        out["body_" + zone] = ob
    body.hide_set(True); body.hide_render = True
    return out

def preview(rig, parts):
    """Показати набір речей і сховати зони тіла, які вони закривають."""
    covered = {z for p in parts for z in COVERS.get(p, [])}
    for o in rig.children:
        k = o.get("kit_part", "")
        if k.startswith("body_"):
            o.hide_set(k[5:] in covered)
        elif k in ("body", "basemesh"):
            o.hide_set(True)
        else:
            o.hide_set(k not in parts and not o.name.endswith("low-poly"))

def build_kit(gender):
    rig, body = kit_body(gender)
    items = {}
    items.update(split_body(rig, body))
    items.update(wardrobe(rig, body)); items.update(helmets(rig, body)); items.update(shields(rig, body))
    items.update(weapons(rig, body))
    if gender == "m":
        items.update(facial_hair(rig, body))
    return rig, body, items

# ---------------------------------------------------------------- текстури набору

def _save_rgba(name, arr, out_dir, color=True):
    import numpy as np
    h, w, _ = arr.shape
    img = bpy.data.images.get(name) or bpy.data.images.new(name, w, h, alpha=True)
    if tuple(img.size) != (w, h):
        img.scale(w, h)
    img.pixels.foreach_set(np.ascontiguousarray(arr, dtype=np.float32).ravel())
    img.filepath_raw = os.path.join(out_dir, name + ".png"); img.file_format = 'PNG'
    if not color:
        img.colorspace_settings.name = 'Non-Color'
    img.save(); img.use_fake_user = True
    return img

def _image_array(img, size=1024):
    import numpy as np
    w, h = img.size
    a = np.empty(len(img.pixels), dtype=np.float32); img.pixels.foreach_get(a)
    a = a.reshape(h, w, 4)
    if (w, h) != (size, size):                      # найближчий сусід до квадрата size×size
        ys = (np.arange(size) * h // size).clip(0, h - 1); xs = (np.arange(size) * w // size).clip(0, w - 1)
        a = a[ys][:, xs]
    return np.ascontiguousarray(a)

def _normal_from_height(hgt, strength=2.0):
    import numpy as np
    gx = np.roll(hgt, -1, 1) - np.roll(hgt, 1, 1); gy = np.roll(hgt, -1, 0) - np.roll(hgt, 1, 0)
    n = np.dstack([-gx * strength, -gy * strength, np.ones_like(hgt)])
    n /= np.linalg.norm(n, axis=2, keepdims=True)
    return np.dstack([(n + 1) / 2, np.ones_like(hgt)])

def _pbr_material(name, base_img, nor_img=None, rough=0.8, metal=0.0):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True; m.use_fake_user = True
    nt = m.node_tree; nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial"); b = nt.nodes.new("ShaderNodeBsdfPrincipled")
    nt.links.new(b.outputs[0], out.inputs[0])
    t = nt.nodes.new("ShaderNodeTexImage"); t.image = base_img; nt.links.new(t.outputs["Color"], b.inputs["Base Color"])
    if base_img.name.endswith("_hair"):
        nt.links.new(t.outputs["Alpha"], b.inputs["Alpha"])
    if nor_img is not None:
        tn = nt.nodes.new("ShaderNodeTexImage"); tn.image = nor_img; tn.image.colorspace_settings.name = 'Non-Color'
        nm = nt.nodes.new("ShaderNodeNormalMap"); nt.links.new(tn.outputs["Color"], nm.inputs["Color"])
        nt.links.new(nm.outputs["Normal"], b.inputs["Normal"])
    b.inputs["Roughness"].default_value = rough; b.inputs["Metallic"].default_value = metal
    return m

def highpass_fabric(a, radius=40, keep=0.25):
    """Прибрати з тканини великі плями (низькі частоти яскравості), лишити переплетення: на одязі вони
    читались брудними плямами. a — RGBA float [h, w, 4]; keep — скільки низьких частот лишити."""
    import numpy as np
    lum = a[..., :3] @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)
    k = 2 * radius + 1
    pad = np.pad(lum, radius, mode="wrap")                  # тканина тайлиться — розмиття теж по колу
    c = np.cumsum(np.cumsum(pad, 0), 1)
    c = np.pad(c, ((1, 0), (1, 0)))
    blur = (c[k:, k:] - c[:-k, k:] - c[k:, :-k] + c[:-k, :-k]) / (k * k)
    flat = lum - (1.0 - keep) * (blur - lum.mean())
    ratio = np.where(lum > 1e-4, flat / np.maximum(lum, 1e-4), 1.0)
    out = a.copy()
    out[..., :3] = np.clip(a[..., :3] * ratio[..., None], 0, 1)
    return out

def prepare_textures(art_dir):
    """Нейтральні тканини (колір задає гра множенням), кольчуга, стьобка, волосся-карти — у Textures/Kit."""
    import numpy as np
    out = os.path.join(art_dir, "Textures", "Kit"); os.makedirs(out, exist_ok=True)
    def from_disk(mat_name, kit_name, rough, metal=0.0):
        # Чистий запуск (build_kit_all.py): вихідних матеріалів Poly Haven у сцені немає, але
        # нейтралізовані текстури вже лежать у Textures/Kit — беремо їх.
        p = os.path.join(out, f"kit_{kit_name}.png")
        if not os.path.exists(p):
            raise KeyError(f"немає ні матеріалу, ні {p}")
        base = bpy.data.images.load(p, check_existing=True); base.name = f"kit_{kit_name}"
        pn = os.path.join(out, f"kit_{kit_name}_nor.png")
        nor = bpy.data.images.load(pn, check_existing=True) if os.path.exists(pn) else None
        if nor is not None:
            nor.name = f"kit_{kit_name}_nor"
            nor.colorspace_settings.name = 'Non-Color'
        return _pbr_material(mat_name, base, nor, rough, metal)
    def neutral(src_mat, name, target=0.78, rough=0.85, metal=0.0):
        if src_mat not in bpy.data.materials:
            return from_disk(FABRICS[name][0], name, rough, metal)
        mat = bpy.data.materials[src_mat]
        imgs = {n.image.name: n.image for n in mat.node_tree.nodes if n.type == 'TEX_IMAGE' and n.image}
        if any(k.startswith("kit_") for k in imgs):      # уже нейтралізовано (повторний запуск)
            return mat
        diff = next(i for k, i in imgs.items() if "Diffuse" in k or "diff" in k.lower() or "col" in k.lower())
        nor = next((i for k, i in imgs.items() if "nor" in k.lower()), None)
        a = _image_array(diff)
        lum = a[..., :3] @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)
        lum = lum / max(lum.mean(), 1e-3) * target
        a[..., :3] = np.clip(np.dstack([lum] * 3) * np.array([1.0, 0.97, 0.92], dtype=np.float32), 0, 1)
        if name in ("linen", "wool", "fleece"):
            a = highpass_fabric(a)
        base = _save_rgba(f"kit_{name}", a, out)
        n_img = None
        if nor is not None:
            n_img = _save_rgba(f"kit_{name}_nor", _image_array(nor), out, color=False)
        return _pbr_material(FABRICS[name][0], base, n_img, rough, metal)
    neutral("rough_linen", "linen")
    neutral("poly_wool_herringbone", "wool", target=0.7)
    neutral("caban", "fleece", target=0.72)
    leather = bpy.data.materials.get("brown_leather")
    if leather is None:
        from_disk("brown_leather", "leather", 0.7)
    elif any(n.type == 'TEX_IMAGE' and n.image and n.image.name.startswith("kit_") for n in leather.node_tree.nodes):
        leather = None
    neutral("metal_plate_02", "steel", target=0.6, rough=0.45, metal=0.9)
    # шкіра лишається своєю (коричнева), лише копіюється в набір
    if leather is not None:
        imgs = {n.image.name: n.image for n in leather.node_tree.nodes if n.type == 'TEX_IMAGE' and n.image}
        d = next(i for k, i in imgs.items() if "Diffuse" in k); nr = next(i for k, i in imgs.items() if "nor" in k.lower())
        _pbr_material("brown_leather", _save_rgba("kit_leather", _image_array(d), out), _save_rgba("kit_leather_nor", _image_array(nr), out, False), 0.7)
    wood = bpy.data.materials.get("weathered_planks")
    if wood is None:
        from_disk("weathered_planks", "wood", 0.75)
    elif not any(n.type == 'TEX_IMAGE' and n.image and n.image.name.startswith("kit_") for n in wood.node_tree.nodes):
        imgs = {n.image.name: n.image for n in wood.node_tree.nodes if n.type == 'TEX_IMAGE' and n.image}
        d = next(i for k, i in imgs.items() if "Diffuse" in k); nr = next(i for k, i in imgs.items() if "nor" in k.lower())
        _pbr_material("weathered_planks", _save_rgba("kit_wood", _image_array(d), out), _save_rgba("kit_wood_nor", _image_array(nr), out, False), 0.75)
    # кольчуга: сітка кілець (власна процедурна, CC0 проєкту)
    s = 512; y, x = np.mgrid[0:s, 0:s].astype(np.float32)
    cell = 16.0
    hgt = np.zeros((s, s), np.float32)
    for oy in (0.0, cell / 2):
        cx = (x + (oy * 0)) % cell - cell / 2; cy = (y + oy) % cell - cell / 2
        r = np.sqrt(cx ** 2 + cy ** 2)
        hgt = np.maximum(hgt, np.clip(1 - np.abs(r - cell * 0.32) / (cell * 0.11), 0, 1))
    alb = np.dstack([0.25 + 0.45 * hgt] * 3 + [np.ones_like(hgt)])
    _pbr_material("M_Chainmail", _save_rgba("kit_mail", alb, out), _save_rgba("kit_mail_nor", _normal_from_height(hgt, 3.0), out, False), 0.4, 0.95)
    # стьобка: ромбоподібні шви на нейтральному льоні
    lin = _image_array(bpy.data.images["kit_linen"], 512)
    q = 64.0
    seam = np.minimum(np.abs(((x + y) % q) - q / 2), np.abs(((x - y) % q) - q / 2))
    puff = np.clip(seam / (q * 0.5), 0, 1) ** 0.5
    lin[..., :3] *= (0.75 + 0.25 * puff)[..., None]
    _pbr_material("M_Quilt", _save_rgba("kit_quilt", lin, out), _save_rgba("kit_quilt_nor", _normal_from_height(puff, 4.0), out, False), 0.85)
    # волосся (бороди): темні пасма з альфою по краях
    strands = 0.5 + 0.5 * np.sin(x * 0.9 + np.sin(y * 0.05) * 3.0)
    hair = np.dstack([0.12 + 0.06 * strands, 0.08 + 0.04 * strands, 0.05 + 0.03 * strands, np.ones_like(strands)])
    _pbr_material("M_HairCard", _save_rgba("kit_hair", hair, out), _save_rgba("kit_hair_nor", _normal_from_height(strands, 1.5), out, False), 0.6)
    return out

# ---------------------------------------------------------------- культури, зачіски, експорт

# Культура -> (частки рас MPFB african/asian/caucasian, шкіра MPFB, тон шкіри-множник, ріст, пропорції)
# Частка українських у ростері — ≈20 % (№17.3): решта культур — з усього світу (№12.9, №19).
CULTURES = {
    "ukrainian":     ((0.0, 0.0, 1.0),   "young_caucasian", (1.00, 0.97, 0.94), 0.55),
    "west_african":  ((1.0, 0.0, 0.0),   "young_african",   (1.00, 1.00, 1.00), 0.55),
    "east_asian":    ((0.0, 1.0, 0.0),   "young_asian",     (1.00, 1.00, 1.00), 0.45),
    "south_asian":   ((0.3, 0.45, 0.25), "young_asian",     (0.78, 0.66, 0.55), 0.45),
    "middle_eastern":((0.12, 0.1, 0.78), "young_caucasian", (0.86, 0.74, 0.62), 0.5),
    "latin":         ((0.2, 0.4, 0.4),   "young_caucasian", (0.82, 0.68, 0.55), 0.45),
    "nordic":        ((0.0, 0.0, 1.0),   "young_caucasian", (1.04, 1.0, 0.98), 0.7),
    "mediterranean": ((0.05, 0.05, 0.9), "young_caucasian", (0.92, 0.84, 0.74), 0.5),
}

def _tint_skin_materials(obj_list, tone, tag, tex_dir):
    import numpy as np
    done = {}
    for o in obj_list:
        for sl in o.material_slots:
            m = sl.material
            if m is None or not m.use_nodes:
                continue
            key = m.name
            if key not in done:
                nm = m.copy(); nm.name = f"skin_{tag}"
                for n in nm.node_tree.nodes:
                    if n.type == 'TEX_IMAGE' and n.image and n.image.colorspace_settings.name.lower().startswith("srgb"):
                        a = _image_array(n.image, 1024)
                        a[..., :3] = np.clip(a[..., :3] * np.array(tone, dtype=np.float32), 0, 1)
                        n.image = _save_rgba(f"skin_{tag}", a, tex_dir)
                done[key] = nm
            sl.material = done[key]

def culture_body(gender, culture, tex_dir):
    """Тіло культури (зони тіла + очі) на тому самому скелеті, що й набір; повертає {зона: обʼєкт}."""
    race, skin, tone, height = CULTURES[culture]
    g = "male" if gender == "m" else "female"
    cid = f"body_{gender}_{culture}"
    spec = dict(g=1.0 if gender == "m" else 0.0, age=0.5, mus=0.5, w=0.5, h=0.5, race=race,
                skin=f"{skin}_{g}", proxy="male1591" if gender == "m" else "female1605")
    rig, body = _make_body(cid, spec)
    zones = split_body(rig, body)
    _tint_skin_materials(list(zones.values()), tone, f"{gender}_{culture}", tex_dir)
    return rig, zones

def kit_hair(rig, gender):
    """CC0-зачіски MakeHuman (інші ліцензії відкидає _asset), припасовані до тіла набору; текстури
    нейтралізовані (колір — у грі). До 07.10.2026 бралися всі підряд — серед них були AGPL3 і CC BY."""
    import numpy as np
    base = bpy.data.objects[rig.name + ".basemesh"]
    out = {}
    for name in sorted(os.listdir(os.path.join(MPFB_DATA, "hair"))):
        if name.startswith("cortu_shaggy_green") or name.startswith("cortu_strawberry") or name == "learning_anime_hair":
            continue                                     # стилізовані/фантазійні — не під сеттинг
        before = set(bpy.data.objects)
        _activate(base)
        try:
            path = _asset("hair", name, ".mhclo")
        except PermissionError as e:
            print("hair skip", e); continue
        try:
            bpy.ops.mpfb.load_library_clothes(filepath=path, object_type="Hair", material_type="MAKESKIN")
        except Exception as e:
            print("hair fail", name, e); continue
        for o in set(bpy.data.objects) - before:
            for m in [m for m in o.modifiers if m.type == 'SUBSURF']:
                o.modifiers.remove(m)
            o.name = f"{rig.name}.hair_{name}"; o["kit_part"] = "hair_" + name; o["kit_kind"] = "hair"
            _budget_hair(o, 3000)
            out["hair_" + name] = o
    return out

def _budget_hair(o, limit):
    tris = sum(len(p.vertices) - 2 for p in o.data.polygons)
    if tris > limit:
        mod = o.modifiers.new("lod", 'DECIMATE'); mod.ratio = limit / tris
        with bpy.context.temp_override(object=o, active_object=o, selected_objects=[o]):
            bpy.ops.object.modifier_move_to_index(modifier=mod.name, index=0)
            bpy.ops.object.modifier_apply(modifier=mod.name)

def neutral_hair_textures(rig, tex_dir):
    """Зачіски -> нейтральні світло-сірі текстури з альфою (колір волосся задає гра)."""
    import numpy as np
    for o in rig.children:
        if o.get("kit_kind") != "hair":
            continue
        for sl in o.material_slots:
            m = sl.material
            if m is None or m.name.startswith("hairN_"):
                continue
            nm = m.copy(); nm.name = "hairN_" + o["kit_part"]
            for n in nm.node_tree.nodes:
                if n.type == 'TEX_IMAGE' and n.image and n.image.colorspace_settings.name.lower().startswith("srgb"):
                    a = _image_array(n.image, 512)
                    lum = a[..., :3] @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)
                    lum = np.clip(lum / max(lum[a[..., 3] > 0.1].mean() if (a[..., 3] > 0.1).any() else 0.5, 1e-3) * 0.7, 0, 1)
                    a[..., :3] = lum[..., None]
                    n.image = _save_rgba("hairN_" + o["kit_part"][5:], a, tex_dir)
            sl.material = nm

def export_kit(rig, art_dir, extra_rigs=()):
    """Один FBX на набір (стать): скелет + усі модульні частини. Тіла культур — окремими FBX
    (той самий скелет за іменами кісток). Текстури — відносні шляхи в Textures/Kit."""
    out_dir = os.path.join(art_dir, "Characters", "Kit"); os.makedirs(out_dir, exist_ok=True)
    paths = []
    for r in (rig,) + tuple(extra_rigs):
        for o in bpy.context.selected_objects:
            o.select_set(False)
        objs = [r] + [o for o in r.children if o.type == 'MESH' and not o.name.endswith((".basemesh", ".body"))]
        for o in objs:
            o.hide_set(False); o.hide_render = False; o.select_set(True)
        bpy.context.view_layer.objects.active = r
        path = os.path.join(out_dir, r.name + ".fbx")
        bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'ARMATURE', 'MESH'},
                                 apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y',
                                 use_mesh_modifiers=True, mesh_smooth_type='FACE', add_leaf_bones=False,
                                 primary_bone_axis='Y', secondary_bone_axis='X', bake_anim=False,
                                 path_mode='RELATIVE', embed_textures=False)
        paths.append(path)
    return paths

# ---------------------------------------------------------------- огляд (лише для знімків у Blender)

LOOKS_F = {
    "ukrainian":      dict(hair="hair_braid01", parts=["shirt", "skirt_long", "boots", "vest"],
                           colors={"shirt": (0.95, 0.93, 0.88), "skirt_long": (0.12, 0.12, 0.14), "vest": (0.5, 0.1, 0.08)}, hc=(0.3, 0.2, 0.1)),
    "west_african":   dict(hair="hair_afro01", parts=["robe", "shoes"], colors={"robe": (0.85, 0.55, 0.12)}, hc=(0.05, 0.04, 0.03)),
    "east_asian":     dict(hair="hair_ponytail01", parts=["kaftan", "skirt_long", "shoes", "wpn_katana"],
                           colors={"kaftan": (0.55, 0.12, 0.2), "skirt_long": (0.15, 0.15, 0.25)}, hc=(0.04, 0.035, 0.03)),
    "south_asian":    dict(hair="hair_braid01", parts=["robe", "shoes", "bracers"], colors={"robe": (0.75, 0.15, 0.35)}, hc=(0.04, 0.03, 0.03)),
    "middle_eastern": dict(hair="hair_long01", parts=["robe", "shoes", "turban", "wpn_dagger"],
                           colors={"robe": (0.2, 0.35, 0.4), "turban": (0.85, 0.8, 0.7)}, hc=(0.05, 0.04, 0.03)),
    "latin":          dict(hair="hair_ponytail01", parts=["tunic", "trousers", "boots", "cloak", "wpn_bow"],
                           colors={"tunic": (0.75, 0.45, 0.2), "trousers": (0.3, 0.25, 0.2), "cloak": (0.2, 0.4, 0.3)}, hc=(0.08, 0.05, 0.03)),
    "nordic":         dict(hair="hair_braid01", parts=["tunic", "trousers", "boots", "mail", "shield_round", "wpn_spear"],
                           colors={"tunic": (0.25, 0.3, 0.45), "trousers": (0.35, 0.3, 0.25)}, hc=(0.7, 0.55, 0.3)),
    "mediterranean":  dict(hair="hair_long01", parts=["tunic", "skirt_long", "shoes", "gambeson", "wpn_sword"],
                           colors={"tunic": (0.9, 0.85, 0.75), "skirt_long": (0.45, 0.15, 0.1)}, hc=(0.12, 0.07, 0.04)),
}

LOOKS_M = {
    "ukrainian":      dict(hair="hair_short02", parts=["shirt", "sharovary", "boots", "vest", "moustache", "wpn_sabre"],
                           colors={"shirt": (0.92, 0.9, 0.84), "sharovary": (0.55, 0.12, 0.1), "vest": (0.35, 0.25, 0.18)}, hc=(0.2, 0.13, 0.08)),
    "west_african":   dict(hair="hair_short04", parts=["robe", "shoes", "wpn_spear", "shield_round"],
                           colors={"robe": (0.2, 0.25, 0.55)}, hc=(0.05, 0.04, 0.03)),
    "east_asian":     dict(hair="hair_ponytail01", parts=["kaftan", "trousers", "shoes", "helm_kabuto", "wpn_katana"],
                           colors={"kaftan": (0.15, 0.17, 0.25), "trousers": (0.2, 0.2, 0.22)}, hc=(0.04, 0.035, 0.03)),
    "south_asian":    dict(hair="hair_short01", parts=["robe", "sharovary", "shoes", "turban", "wpn_dagger", "beard_short"],
                           colors={"robe": (0.95, 0.92, 0.85), "turban": (0.85, 0.5, 0.12), "sharovary": (0.9, 0.88, 0.8)}, hc=(0.05, 0.04, 0.03)),
    "middle_eastern": dict(hair="hair_short03", parts=["robe", "shoes", "turban", "wpn_sabre", "beard_full", "vest"],
                           colors={"robe": (0.8, 0.72, 0.55), "turban": (0.95, 0.95, 0.92), "vest": (0.3, 0.12, 0.1)}, hc=(0.06, 0.04, 0.03)),
    "latin":          dict(hair="hair_short02", parts=["tunic", "trousers", "boots", "cloak", "wpn_musket", "moustache"],
                           colors={"tunic": (0.7, 0.55, 0.35), "trousers": (0.3, 0.25, 0.2), "cloak": (0.55, 0.15, 0.12)}, hc=(0.07, 0.05, 0.03)),
    "nordic":         dict(hair="hair_long01", parts=["tunic", "trousers", "boots", "mail", "helm_spangen", "shield_round", "wpn_axe", "beard_full"],
                           colors={"tunic": (0.35, 0.4, 0.3), "trousers": (0.4, 0.33, 0.25)}, hc=(0.55, 0.42, 0.25)),
    "mediterranean":  dict(hair="hair_short04", parts=["tunic", "trousers", "boots", "cuirass", "greaves", "shield_kite", "wpn_sword", "bracers"],
                           colors={"tunic": (0.6, 0.12, 0.1), "trousers": (0.3, 0.25, 0.2), "shield_kite": (0.7, 0.6, 0.45)}, hc=(0.08, 0.06, 0.04)),
}

def _preview_material(src, rgb, tag):
    m = src.copy(); m.name = f"prev_{tag}"
    nt = m.node_tree
    b = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    link = next((l for l in nt.links if l.to_node == b and l.to_socket.name == "Base Color"), None)
    mix = nt.nodes.new("ShaderNodeMix"); mix.data_type = 'RGBA'; mix.blend_type = 'MULTIPLY'
    mix.inputs["Factor"].default_value = 1.0
    mix.inputs[7].default_value = (*rgb, 1.0)
    if link:
        nt.links.new(link.from_socket, mix.inputs[6]); nt.links.remove(link)
    nt.links.new(mix.outputs[2], b.inputs["Base Color"])
    return m

def fit_rig(kit, target, name):
    """Копія скелета набору, що через Copy Transforms повторює спокій скелета іншого тіла — так само,
    як у Unity річ із bindpose набору сідає на кістки тіла культури."""
    old = bpy.data.objects.get(name)
    if old:
        bpy.data.objects.remove(old, do_unlink=True)
    dup = kit.copy(); dup.name = name; dup["preview"] = name.split("_")[1]
    kit_collection().objects.link(dup)
    dup.location = target.location.copy()
    for pb in dup.pose.bones:
        for c in list(pb.constraints):
            pb.constraints.remove(c)
        if pb.name in target.pose.bones:
            c = pb.constraints.new('COPY_TRANSFORMS'); c.target = target; c.subtarget = pb.name
    dup.hide_set(True)
    return dup

def preview_lineup(gender="m", looks=None):
    """Збирає шеренгу персонажів з набору на тілах культур (копії речей, лише для огляду)."""
    looks = looks or LOOKS_M
    kit = bpy.data.objects[f"kit_{gender}"]
    col = kit_collection()
    for o in [o for o in col.objects if o.get("preview") == gender]:
        bpy.data.objects.remove(o, do_unlink=True)
    for i, (culture, lk) in enumerate(looks.items()):
        r = bpy.data.objects[f"body_{gender}_{culture}"]
        r.location = (1.1 * i, -2.5 if gender == "m" else -5.0, 0)
        covered = {z for p in lk["parts"] for z in COVERS.get(p, [])}
        for o in r.children:
            k = o.get("kit_part", "")
            if k.startswith("body_"):
                o.hide_set(k[5:] in covered)
        fitted = fit_rig(kit, r, f"fit_{gender}_{culture}")
        for part in lk["parts"] + [lk["hair"]]:
            src = bpy.data.objects.get(f"kit_{gender}.{part}")
            if src is None:
                print("нема", part); continue
            c = src.copy(); c.data = src.data; c["preview"] = gender
            col.objects.link(c); c.parent = fitted; c.matrix_parent_inverse.identity(); c.hide_set(False)
            for m in c.modifiers:
                if m.type == 'ARMATURE':
                    m.object = fitted
            rgb = lk["hc"] if part.startswith("hair_") else lk["colors"].get(part)
            if rgb:
                c.material_slots[0].link = 'OBJECT'
                c.material_slots[0].material = _preview_material(src.material_slots[0].material, rgb, f"{culture}_{part}")
