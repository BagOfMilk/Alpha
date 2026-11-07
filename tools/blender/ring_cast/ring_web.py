# Справжній ринг гри для сторінки анімацій: об'єкти ring_* з Assets/Art/Ring/venue_<арена>.fbx (нічого не малюємо заново),
# матеріали — ті самі текстури, що в ring_venues.py, стиснені для вебу. Канатів у FBX немає й у грі: їх будує сторінка за
# якорями арени (як RingRopes.cs).
#   blender -b --factory-startup --python tools/blender/ring_cast/ring_web.py -- <репо> <дані сторінки> [арени]
# Вихід: <дані>/ring_<арена>.glb, <дані>/rings.json (розміри, якорі, кольори канатів, написи на фартуху).
import json, os, re, sys

import bpy
import numpy as np

args = sys.argv[sys.argv.index("--") + 1:]
REPO, WEB = args[0], args[1]
VENUES = args[2:] or ["arena", "hall", "gym"]

ART = os.path.join(REPO, "Assets", "Art", "Ring")
HERO = os.path.join(ART, "Hero", "Textures")
TEX = os.path.join(ART, "Textures")
CC0 = os.path.join(REPO, "Assets", "ThirdParty", "CC0", "PolyHaven", "Textures")

# Те, що ring_venues.py знає про матеріали рингу (колір відтінку, метал). Числа — звідти ж.
TINT = {"Pad_Red": (0.55, 0.06, 0.06), "Pad_Blue": (0.06, 0.12, 0.45), "Pad_Black": (0.03, 0.03, 0.03), "Pad_Yellow": (0.75, 0.62, 0.08)}
KIT_METAL = {"painted_metal_shutter": 0.2, "metal_plate": 0.9, "metal_plate_02": 0.9}
ROPE_COLOR = {"White": "#D8D5CC", "Black": "#17171A", "Gold": "#B8912F", "Red": "#9E1B1B", "Blue": "#1C3A8C"}
TITLE = {"arena": "Арена · 20 футів", "hall": "Клубна зала · 18 футів", "gym": "Спортзал · 16 футів (зараз у грі)"}


def load(path, colorspace, size):
    if not os.path.exists(path) or os.path.getsize(path) < 1024:
        raise RuntimeError("немає текстури або це вказівник LFS: " + path)
    img = bpy.data.images.load(path, check_existing=False)
    img.colorspace_settings.name = colorspace
    if size and max(img.size) > size:
        img.scale(size, size)
    return img


def as_gray(img):
    """Набори набору: лише яскравість фото, колір дає відтінок вершин (build_textured у ring_venues.py)."""
    w, h = img.size
    px = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(px)
    px = px.reshape(-1, 4)
    g = px[:, 0] * 0.2126 + px[:, 1] * 0.7152 + px[:, 2] * 0.0722
    px[:, 0] = px[:, 1] = px[:, 2] = g
    out = bpy.data.images.new(img.name + "_gray", w, h, alpha=False)
    out.colorspace_settings.name = "sRGB"
    out.pixels.foreach_set(px.ravel())
    out.pack()
    bpy.data.images.remove(img)
    return out


def orm_from_mask(path, size):
    """Маска героїчних предметів: R — метал, A — гладкість (hero_material у ring_venues.py). glTF: G — шорсткість, B — метал."""
    m = load(path, "Non-Color", size)
    w, h = m.size
    px = np.empty(w * h * 4, dtype=np.float32)
    m.pixels.foreach_get(px)
    px = px.reshape(-1, 4)
    out = np.ones_like(px)
    out[:, 1] = np.clip(1.0 - px[:, 3], 0.04, 1.0)
    out[:, 2] = px[:, 0]
    o = bpy.data.images.new(m.name + "_orm", w, h, alpha=False)
    o.colorspace_settings.name = "Non-Color"
    o.pixels.foreach_set(out.ravel())
    o.pack()
    bpy.data.images.remove(m)
    return o


def new_mat(name):
    m = bpy.data.materials.new(name + "__web")
    m.use_nodes = True
    return m, next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")


def link_tex(m, img):
    t = m.node_tree.nodes.new("ShaderNodeTexImage")
    t.image = img
    return t


def wire_orm(m, b, orm):
    t = link_tex(m, orm)
    sep = m.node_tree.nodes.new("ShaderNodeSeparateColor")
    m.node_tree.links.new(t.outputs["Color"], sep.inputs["Color"])
    m.node_tree.links.new(sep.outputs[1], b.inputs["Roughness"])
    m.node_tree.links.new(sep.outputs[2], b.inputs["Metallic"])


def wire_normal(m, b, img):
    t = link_tex(m, img)
    n = m.node_tree.nodes.new("ShaderNodeNormalMap")
    m.node_tree.links.new(t.outputs["Color"], n.inputs["Color"])
    m.node_tree.links.new(n.outputs["Normal"], b.inputs["Normal"])


def hero_mat(name):
    # Hero_<предмет>__<варіант>[__<відтінок>]
    parts = name[5:].split("__")
    asset, var = parts[0], parts[1]
    big = 2048 if asset.startswith("ring_canvas") else 1024
    m, b = new_mat(name)
    alb = load(os.path.join(HERO, "hero_%s_%s_albedo.png" % (asset, var)), "sRGB", big)
    t = link_tex(m, alb)
    if len(parts) > 2:
        c = TINT[parts[2]]
        mix = m.node_tree.nodes.new("ShaderNodeMix")
        mix.data_type, mix.blend_type = "RGBA", "MULTIPLY"
        mix.inputs["Factor"].default_value = 1.0
        mix.inputs[7].default_value = (min(1, c[0] / 0.8), min(1, c[1] / 0.8), min(1, c[2] / 0.8), 1)
        m.node_tree.links.new(t.outputs["Color"], mix.inputs[6])
        m.node_tree.links.new(mix.outputs[2], b.inputs["Base Color"])
    else:
        m.node_tree.links.new(t.outputs["Color"], b.inputs["Base Color"])
    wire_orm(m, b, orm_from_mask(os.path.join(HERO, "hero_%s_%s_mask.png" % (asset, var)), 512))
    wire_normal(m, b, load(os.path.join(HERO, "hero_%s_normal.png" % asset), "Non-Color", 1024))
    return m


def kit_mat(name, rough_mean):
    # Kit_<набір>: фото (сіре) × відтінок вершин × 4; сторінка множить на 4 (FACTOR_SCALE у ring_venues.py)
    kset = name[4:]
    d = os.path.join(CC0, kset)
    m, b = new_mat(name)
    diff = os.path.join(d, kset + "_diff_2k.jpg")
    if os.path.exists(diff):
        t = link_tex(m, as_gray(load(diff, "sRGB", 1024)))
        m.node_tree.links.new(t.outputs["Color"], b.inputs["Base Color"])
    b.inputs["Metallic"].default_value = KIT_METAL.get(kset, 0.0)
    b.inputs["Roughness"].default_value = float(rough_mean)
    wire_normal(m, b, load(os.path.join(d, kset + "_nor_gl_2k.jpg"), "Non-Color", 1024))
    return m


def decal_mat(name):
    fn = ("decal_" + name[6:].lower()) if name.startswith("Decal_") else ("art_" + name[4:].lower())
    m, b = new_mat(name)
    t = link_tex(m, load(os.path.join(TEX, fn + ".png"), "sRGB", 1024))
    m.node_tree.links.new(t.outputs["Color"], b.inputs["Base Color"])
    m.node_tree.links.new(t.outputs["Alpha"], b.inputs["Alpha"])
    b.inputs["Roughness"].default_value = 0.8
    m.surface_render_method = "BLENDED"
    return m


def read_info(v):
    info = {}
    for line in open(os.path.join(ART, "venue_%s_info.txt" % v), encoding="utf-8"):
        k, _, val = line.strip().partition(" ")
        info[k] = val
    return info


def read_anchors(v):
    a = {}
    for line in open(os.path.join(ART, "venue_%s_anchors.txt" % v), encoding="utf-8"):
        p = line.split()
        if len(p) >= 4 and p[0].startswith("anchor_"):
            a[p[0]] = [float(p[1]), float(p[2]), float(p[3])]
    return a


def read_labels(v, strings):
    """Написи рингу — текстом за ключем, у текстурах слів немає (правило власника): сторінка малює їх сама."""
    out = []
    for line in open(os.path.join(ART, "venue_%s_texts.txt" % v), encoding="utf-8"):
        if line.startswith("#"):
            continue
        p = line.split()
        if len(p) < 15 or p[0] != "ring.apron.brand":
            continue
        f = [float(x) for x in p[1:14]]
        out.append(dict(text=strings.get(p[0], "РИНГ"), pos=f[0:3], right=f[3:6], up=f[6:9], height=f[9], color=f[10:13]))
    return out


def build(v, strings):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=os.path.join(ART, "venue_%s.fbx" % v))
    keep = [o for o in bpy.data.objects if o.type == "MESH" and o.name.startswith("ring_")]
    bbox = {}
    tris = 0
    for o in keep:
        me = o.data
        rough = 1.0
        ca = me.color_attributes
        if len(ca):
            ca.active_color_index = 0
            arr = np.empty(len(ca[0].data) * 4, dtype=np.float32)
            ca[0].data.foreach_get("color", arr)
            rough = min(1.0, float(arr[3::4].mean()) * 1.7 * 0.8)
        names = [s.material.name for s in o.material_slots if s.material]
        for i, nm in enumerate(names):
            base = nm.split(".")[0]
            if base.startswith("Hero_"):
                o.material_slots[i].material = hero_mat(base)
            elif base.startswith("Kit_"):
                o.material_slots[i].material = kit_mat(base, rough)
            elif base.startswith(("Decal_", "Art_")):
                o.material_slots[i].material = decal_mat(base)
            else:
                raise RuntimeError("невідомий матеріал рингу: " + nm)
        mw = o.matrix_world.copy()
        o.parent = None
        o.matrix_world = mw
        tris += sum(len(p.vertices) - 2 for p in me.polygons)
    # лишаємо тільки ринг: решта арени в GLB не йде
    for o in list(bpy.data.objects):
        if o not in keep:
            bpy.data.objects.remove(o, do_unlink=True)
    bpy.context.view_layer.update()
    bpy.ops.object.select_all(action="SELECT")
    bpy.context.view_layer.objects.active = keep[0]
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    zmax = {}
    for o in keep:
        co = np.empty(len(o.data.vertices) * 3, dtype=np.float32)
        o.data.vertices.foreach_get("co", co)
        co = co.reshape(-1, 3)
        bbox[o.name] = [co.min(0).round(3).tolist(), co.max(0).round(3).tolist()]
        zmax[o.name] = float(co[:, 2].max())
    path = os.path.join(WEB, "ring_%s.glb" % v)
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True, export_apply=False, export_yup=True,
                              export_image_format="WEBP", export_image_quality=84, export_vertex_color="ACTIVE",
                              export_normals=True, export_tangents=False, export_animations=False, export_materials="EXPORT")
    print("RING", v, "tris", tris, "MB", round(os.path.getsize(path) / 1e6, 1), "bbox", json.dumps(bbox), flush=True)

    info = read_info(v)
    an = read_anchors(v)
    mat = float(info["мат_висота_м"])
    half = float(info["платформа_половина_м"])
    post = float(info["стійки_м"])
    ring_keys = re.compile(r"^anchor_(rope_|corner|center|rebound|apron|floor_|steps_)")
    canvas = next(n for n in bbox if "ring_canvas" in n)
    return dict(id=v, title=TITLE[v], feet=int(info["ринг_фути"]), mat=mat, half=half, post=post, inner=float(info["всередині_канатів_м"]),
                rope=round(an["anchor_rope_NE_1"][0], 3), rope_heights=[round(an["anchor_rope_NE_%d" % (i + 1)][2] - mat, 3) for i in range(3)],
                rope_colors=[ROPE_COLOR.get(c, "#D8D5CC") for c in info["канати"].split()], canvas_top=round(bbox[canvas][1][2] - mat, 3),
                anchors={k: x for k, x in an.items() if ring_keys.match(k)}, labels=read_labels(v, strings), tris=tris, glb="data/ring_%s.glb" % v)


strings = {}
for line in open(os.path.join(ART, "ring_strings_uk.txt"), encoding="utf-8"):
    if "=" in line and not line.startswith("#"):
        k, _, val = line.strip().partition("=")
        strings[k] = val

rings = {}
for v in VENUES:
    rings[v] = build(v, strings)

p = os.path.join(WEB, "rings.json")
old = json.load(open(p, encoding="utf-8")) if os.path.exists(p) else {}
old.update(rings)
order = [v for v in ("arena", "hall", "gym") if v in old]
with open(p, "w", encoding="utf-8", newline="\n") as f:
    json.dump({k: old[k] for k in order}, f, ensure_ascii=False, indent=1)
print("OK", order, flush=True)
