# Персонажі Alpha для Blender через MPFB (трек V, Поправка №18).
#
# Запуск у Blender з увімкненим MPFB і встановленими CC0-паками MakeHuman
# (makehuman_system_assets, shirts01, pants01, shoes01, hair01, skirts01, dress01, hats01, equipment01):
#   exec(open(r"<repo>/tools/blender/alpha_people.py", encoding="utf-8").read())
#   build_people()
#   export_people(r"<repo>/Assets/Art")
#
# Тіла, одяг, волосся й шкіри — CC0 (MakeHuman, вихід MPFB); риг — «game_engine» (Unity Humanoid
# мапить його сам), анімації — окремо (Quaternius UAL, CC0) і ретаргетяться в Unity через Humanoid.
# Детерміновано: жодного random — фенотип кожного персонажа заданий числами нижче.

import bpy, re, os, importlib

MPFB_DATA = os.path.join(bpy.utils.user_resource('EXTENSIONS'), ".user", "user_default", "mpfb", "data")
COL = "Alpha_Characters"

def _svc(mod, cls):
    return getattr(importlib.import_module("bl_ext.user_default.mpfb." + mod), cls)

def asset_license(kind, name):
    """Ліцензія асета бібліотеки MakeHuman за заголовками його .mhclo/.mhmat/.proxy: 'CC0' або рядок як є.
    Бібліотека змішана: поруч із CC0 лежать AGPL3 і CC BY, а репозиторій публічний (Поправка №18 — лише CC0)."""
    folder = os.path.join(MPFB_DATA, kind, name)
    heads = []
    for f in sorted(os.listdir(folder)):
        if f.endswith((".mhclo", ".mhmat", ".proxy")):
            with open(os.path.join(folder, f), encoding="utf-8", errors="ignore") as fh:
                heads += [l.strip() for _, l in zip(range(40), fh) if re.search(r"licen|CC0|CC-0|AGPL|CC.?BY", l, re.I)]
    text = " | ".join(heads)
    if re.search(r"AGPL|CC[ _-]?BY", text, re.I):
        return text
    return "CC0" if re.search(r"CC-?0", text) else (text or "невідома")

def _asset(kind, name, ext):
    folder = os.path.join(MPFB_DATA, kind, name)
    lic = asset_license(kind, name)
    if lic != "CC0":
        raise PermissionError(f"{kind}/{name}: не CC0 ({lic})")
    for f in os.listdir(folder):
        if f.endswith(ext):
            return os.path.join(folder, f)
    raise FileNotFoundError(f"{kind}/{name}/*{ext}")

def _collection():
    col = bpy.data.collections.get(COL)
    if col is None:
        col = bpy.data.collections.new(COL)
        bpy.context.scene.collection.children.link(col)
    bpy.context.view_layer.active_layer_collection = bpy.context.view_layer.layer_collection.children[COL]
    return col

def _remove(cid):
    for o in [o for o in bpy.data.objects if o.get("alpha_char") == cid]:
        bpy.data.objects.remove(o, do_unlink=True)

def _activate(ob):
    for o in bpy.context.selected_objects:
        o.select_set(False)
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob

# Персонаж: стать 0..1 (1 — чоловік), вік 0..1 (0.5 ≈ 25 р., 0.875 ≈ 60+), м'язи, вага, ріст,
# раси (частки), шкіра, сітка тіла, одяг, волосся, брови, зброя в руці (кістка hand_r).
PEOPLE = {
    "hero_m":     dict(g=1.0, age=0.5,  mus=0.6, w=0.5,  h=0.55, race=(0, 0, 1), skin="young_caucasian_male",
                       proxy="male1591", clothes=["elvs_crude_t-shirt_male", "toigo_wool_pants", "culturalibre_male_boots"],
                       hair="short02", brows="eyebrow001"),
    "hero_f":     dict(g=0.0, age=0.5,  mus=0.5, w=0.45, h=0.5,  race=(0, 0, 1), skin="young_caucasian_female",
                       proxy="female1605", clothes=["joepal_crude_t-shirt_female", "toigo_long_full_skirt", "toigo_ankle_boots_female"],
                       hair="elvs_french_braid_variation", brows="eyebrow002"),
    "maksym":     dict(g=1.0, age=0.47, mus=0.75, w=0.5, h=0.6,  race=(0, 0, 1), skin="young_caucasian_male2",
                       proxy="male1591", clothes=["elvs_crude_t-shirt_male", "toigo_wool_pants", "culturalibre_hero_boots_1"],
                       hair="short03", brows="eyebrow003", weapon="joepal_crude_sword"),
    "myroslava":  dict(g=0.0, age=0.45, mus=0.55, w=0.4, h=0.55, race=(0, 0, 1), skin="young_caucasian_female2",
                       proxy="female1605", clothes=["toigo_bodice-style_top", "toigo_long_full_skirt", "culturalibre_heroine_boots_1"],
                       hair="elvs_double_mh_braid", brows="eyebrow004", weapon="culturalibre_wooden_bow"),
    "zakhar":     dict(g=1.0, age=0.95, mus=0.45, w=0.5, h=0.55, race=(0, 0, 1), skin="old_caucasian_male",
                       proxy="male1591", clothes=["mindfront_kimono", "toigo_wool_pants", "culturalibre_male_boots"],
                       hair="long01", brows="eyebrow005"),
    "keeper":     dict(g=1.0, age=1.0,  mus=0.35, w=0.6, h=0.4,  race=(0, 0, 1), skin="old_caucasian_male",
                       proxy="male1591", clothes=["toigo_fisherman_sweater", "toigo_wool_pants", "toigo_mj_cloth_shoes"],
                       hair="short01", brows="eyebrow006"),
    "healer":     dict(g=0.0, age=0.85, mus=0.4, w=0.55, h=0.45, race=(0, 0, 1), skin="old_caucasian_female",
                       proxy="female1605", clothes=["toigo_bodice_dress_with_lace_ruffle_skirt", "toigo_mj_cloth_shoes"],
                       hair="rehmanpolanski_hair_bun_brown", brows="eyebrow007"),
    "goban":      dict(g=1.0, age=0.75, mus=0.7, w=0.55, h=0.5,  race=(0, 0, 1), skin="middleage_caucasian_male",
                       proxy="male1591", clothes=["toigo_fisherman_sweater", "toigo_wool_pants", "culturalibre_hero_boots_2"],
                       hair="short04", brows="eyebrow008", weapon="culturalibre_war_hammer"),
    "sindbad":    dict(g=1.0, age=0.7,  mus=0.5, w=0.5,  h=0.5,  race=(0.15, 0.25, 0.6), skin="middleage_asian_male",
                       proxy="male1591", clothes=["mindfront_kimono", "toigo_harem_pants", "toigo_mj_cloth_shoes"],
                       hair="short02", brows="eyebrow009", weapon="o4saken_dagger"),
    "tuhar":      dict(g=1.0, age=0.7,  mus=0.6, w=0.55, h=0.6,  race=(0, 0, 1), skin="middleage_caucasian_male",
                       proxy="male1591", clothes=["mindfront_kimono", "toigo_wool_pants", "culturalibre_hero_boots_3"],
                       hair="culturalibre_hair_01", brows="eyebrow010", weapon="joepal_crude_sword"),
    "horde_commander": dict(g=1.0, age=0.65, mus=0.95, w=0.6, h=0.7, race=(1, 0, 0), skin="middleage_african_male",
                       proxy="male1591", clothes=["wdg_mycenaean_tunic", "toigo_harem_pants", "culturalibre_hero_boots_4"],
                       hair="short01", brows="eyebrow011", weapon="culturalibre_war_hammer"),
    "raider_a":   dict(g=1.0, age=0.5,  mus=0.8, w=0.5,  h=0.55, race=(0.2, 0.5, 0.3), skin="young_asian_male",
                       proxy="male1591", clothes=["elvs_crude_t-shirt_male", "toigo_harem_pants", "culturalibre_hero_boots_5"],
                       hair="culturalibre_hair_02", brows="eyebrow012", weapon="joepal_crude_sword"),
    "raider_b":   dict(g=1.0, age=0.55, mus=0.7, w=0.65, h=0.5,  race=(0.6, 0.1, 0.3), skin="young_african_male",
                       proxy="male1591", clothes=["wdg_mycenaean_tunic", "toigo_wool_pants", "culturalibre_male_boots"],
                       hair="short04", brows="eyebrow001", weapon="o4saken_dagger"),
    "villager_m": dict(g=1.0, age=0.6,  mus=0.5, w=0.55, h=0.5,  race=(0, 0, 1), skin="middleage_caucasian_male",
                       proxy="male1591", clothes=["elvs_crude_t-shirt_male", "toigo_wool_pants", "toigo_mj_cloth_shoes"],
                       hair="short03", brows="eyebrow002"),
    "villager_f": dict(g=0.0, age=0.6,  mus=0.45, w=0.55, h=0.45, race=(0, 0, 1), skin="middleage_caucasian_female",
                       proxy="female1605", clothes=["joepal_crude_t-shirt_female", "toigo_tiered_skirt", "toigo_mj_cloth_shoes"],
                       hair="rehmanpolanski_hair_bun_brown", brows="eyebrow003"),
    "villager_youth": dict(g=1.0, age=0.4, mus=0.45, w=0.4, h=0.45, race=(0, 0, 1), skin="young_caucasian_male",
                       proxy="male1591", clothes=["elvs_crude_t-shirt_male", "toigo_wool_pants", "toigo_mj_cloth_shoes"],
                       hair="cortu_short_messy_hair", brows="eyebrow004"),
}

def make_person(cid, spec):
    _collection(); _remove(cid)
    s = bpy.context.scene
    s.MPFB_NH_scale_factor = 'METER'
    s.MPFB_NH_load_clothes = False
    bpy.ops.mpfb.create_human()
    base = bpy.context.active_object
    hp = _svc("entities.objectproperties", "HumanObjectProperties")
    ts = _svc("services.targetservice", "TargetService")
    a, asn, c = spec["race"]; tot = float(a + asn + c) or 1.0
    for k, v in (("gender", spec["g"]), ("age", spec["age"]), ("muscle", spec["mus"]), ("weight", spec["w"]),
                 ("height", spec["h"]), ("african", a / tot), ("asian", asn / tot), ("caucasian", c / tot)):
        hp.set_value(k, v, entity_reference=base)
    ts.reapply_macro_details(base)
    s.MPFB_ADR_standard_rig = 'game_engine'
    _activate(base); bpy.ops.mpfb.add_standard_rig()
    rig = base.parent
    _activate(base)
    bpy.ops.mpfb.load_library_skin(filepath=_asset("skins", spec["skin"], ".mhmat"))
    _activate(base)
    bpy.ops.mpfb.load_library_proxy(filepath=_asset("proxymeshes", spec["proxy"], ".proxy"), object_type="Proxymeshes")
    for kind, name, otype in [("eyes", "low-poly", "Eyes"), ("eyebrows", spec["brows"], "Eyebrows"),
                              ("hair", spec["hair"], "Hair")] + [("clothes", n, "Clothes") for n in spec["clothes"]]:
        _activate(base)
        bpy.ops.mpfb.load_library_clothes(filepath=_asset(kind, name, ".mhclo"), object_type=otype, material_type="MAKESKIN")
    rig.name = cid
    for o in [rig] + list(rig.children_recursive):
        o["alpha_char"] = cid
        # Згладжування (subsurf) у грі не потрібне: бюджет персонажа ≤ 15 тис. трикутників (ART_BIBLE §3).
        for m in [m for m in o.modifiers if m.type == 'SUBSURF']:
            o.modifiers.remove(m)
        if o.type == 'MESH' and o is not base:
            o.name = cid + "." + o.name.split(".", 1)[-1]
    base.name = cid + ".basemesh"
    base.hide_set(True); base.hide_render = True   # у гру йде сітка-проксі, а не базова (18 тис. граней з помічниками)
    if spec.get("weapon"):
        _activate(base)
        before = set(bpy.data.objects)
        bpy.ops.mpfb.load_library_clothes(filepath=_asset("clothes", spec["weapon"], ".mhclo"), object_type="Clothes", material_type="MAKESKIN")
        for o in set(bpy.data.objects) - before:
            o["alpha_char"] = cid; o["alpha_weapon"] = True
    return rig


# ---------------------------------------------------------------- кольори епохи
# Сучасний одяг з паків перефарбовується в «тканини епохи» прямо в текстурі (знебарвити й помножити
# на колір): у FBX іде лише базова текстура, вузли кольору Blender у Unity не доходять.
LINEN, WOOL_BROWN, LEATHER, CLOTH_SHOE = (0.86, 0.78, 0.62), (0.36, 0.28, 0.21), (0.42, 0.27, 0.16), (0.46, 0.39, 0.3)
DEFAULT_TINT = {
    "elvs_crude_t-shirt_male": LINEN, "joepal_crude_t-shirt_female": LINEN,
    "toigo_wool_pants": WOOL_BROWN, "toigo_harem_pants": (0.32, 0.29, 0.23),
    "culturalibre_male_boots": LEATHER, "culturalibre_hero_boots_1": LEATHER, "culturalibre_hero_boots_2": LEATHER,
    "culturalibre_hero_boots_3": (0.2, 0.14, 0.1), "culturalibre_hero_boots_4": LEATHER, "culturalibre_hero_boots_5": LEATHER,
    "culturalibre_heroine_boots_1": LEATHER, "toigo_ankle_boots_female": LEATHER,
    "toigo_mj_cloth_shoes": CLOTH_SHOE, "toigo_flats": CLOTH_SHOE,
    "mindfront_kimono": (0.55, 0.5, 0.42), "wdg_mycenaean_tunic": (0.7, 0.56, 0.32),
    "toigo_fisherman_sweater": (0.62, 0.56, 0.46), "toigo_bodice-style_top": (0.5, 0.12, 0.1),
    "toigo_long_full_skirt": (0.26, 0.23, 0.33), "toigo_bodice_dress_with_lace_ruffle_skirt": (0.32, 0.33, 0.26),
    "toigo_tiered_skirt": (0.42, 0.3, 0.24),
}
TINT_OVERRIDES = {
    ("sindbad", "mindfront_kimono"): (0.78, 0.6, 0.28), ("sindbad", "toigo_harem_pants"): (0.2, 0.25, 0.5),
    ("tuhar", "mindfront_kimono"): (0.45, 0.08, 0.08), ("goban", "toigo_fisherman_sweater"): (0.32, 0.4, 0.3),
    ("horde_commander", "toigo_harem_pants"): (0.5, 0.35, 0.15), ("raider_b", "wdg_mycenaean_tunic"): (0.4, 0.35, 0.25),
    ("hero_f", "toigo_long_full_skirt"): (0.36, 0.3, 0.25),
}

def _tinted_image(src, rgb, out_path, size=1024, desat=0.95):
    import numpy as np
    img = src.copy()
    if max(img.size) > size:
        img.scale(size, size)
    w, h = img.size
    px = np.empty(w * h * 4, dtype=np.float32); img.pixels.foreach_get(px)
    px = px.reshape(-1, 4)
    lum = px[:, :3] @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)
    # нормування за середньою яскравістю: і білий, і чорний оригінал дають у середньому колір rgb;
    # контраст приглушено (0.45) — сучасні принти (квіти, камуфляж) стають фактурою тканини
    mean = float(np.clip(lum[px[:, 3] > 0.05].mean() if (px[:, 3] > 0.05).any() else lum.mean(), 0.05, 1.0))
    soft = mean + (lum - mean) * 0.45
    base = px[:, :3] * (1 - desat) * (soft / np.maximum(lum, 1e-3))[:, None] + soft[:, None] * desat
    px[:, :3] = np.clip(base / mean * np.array(rgb, dtype=np.float32), 0, 1)
    img.pixels.foreach_set(px.ravel())
    img.filepath_raw = out_path; img.file_format = 'PNG'; img.save()
    return img

def _find_in_mpfb(name):
    for root, _, files in os.walk(MPFB_DATA):
        if name in files:
            return os.path.join(root, name)
    return None

def finish_person(cid, tex_dir):
    """Шкіра на тіло-проксі; одяг — у кольори епохи; усі текстури персонажа — у tex_dir (≤ 1K)."""
    import shutil
    os.makedirs(tex_dir, exist_ok=True)
    rig = bpy.data.objects[cid]
    base = bpy.data.objects[cid + ".basemesh"]
    spec = PEOPLE[cid]
    for o in rig.children:
        if o.type != 'MESH' or o is base:
            continue
        item = o.name.split(".", 1)[1]
        if item == spec["proxy"]:
            o.data.materials.clear()
            for m in base.data.materials:
                o.data.materials.append(m)
        tint = TINT_OVERRIDES.get((cid, item)) or DEFAULT_TINT.get(item)
        for sl in o.material_slots:
            m = sl.material
            if m is None or not m.use_nodes:
                continue
            if tint:
                m = m.copy(); m.name = f"{cid}.{item}"; sl.material = m
            for n in m.node_tree.nodes:
                if n.type != 'TEX_IMAGE' or n.image is None:
                    continue
                is_color = n.image.colorspace_settings.name.lower().startswith("srgb")
                stem = os.path.splitext(os.path.basename(n.image.filepath or n.image.name))[0]
                if tint and is_color:
                    out = os.path.join(tex_dir, f"{cid}_{item}_{stem}.png")
                    n.image = _tinted_image(n.image, tint, out)
                else:
                    name = os.path.basename(n.image.filepath or (stem + ".png"))
                    out = os.path.join(tex_dir, name)
                    if not os.path.exists(out):
                        src = bpy.path.abspath(n.image.filepath)
                        if not os.path.exists(src):
                            src = _find_in_mpfb(name)
                        if src:
                            shutil.copyfile(src, out)
                    n.image.filepath = out
                    n.image.reload()


# Бюджети на річ (трикутники): одяг, волосся, зброя — щоб персонаж ліз у ≤ 15 тис. (ART_BIBLE §3).
TRI_LIMITS = {"clothes": 2500, "hair": 3000, "weapon": 1200}

def _decimate_to(ob, limit):
    tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
    if tris <= limit:
        return tris
    mod = ob.modifiers.new("lod", 'DECIMATE'); mod.ratio = limit / tris
    with bpy.context.temp_override(object=ob, active_object=ob, selected_objects=[ob]):
        bpy.ops.object.modifier_move_to_index(modifier=mod.name, index=0)
        bpy.ops.object.modifier_apply(modifier=mod.name)
    return sum(len(p.vertices) - 2 for p in ob.data.polygons)

def optimize_person(cid):
    spec = PEOPLE[cid]
    for o in bpy.data.objects[cid].children:
        if o.type != 'MESH' or o.hide_render:
            continue
        item = o.name.split(".", 1)[1]
        if o.get("alpha_weapon"):
            _decimate_to(o, TRI_LIMITS["weapon"])
        elif item == spec["hair"]:
            _decimate_to(o, TRI_LIMITS["hair"])
        elif item in spec["clothes"]:
            _decimate_to(o, TRI_LIMITS["clothes"])

def build_people(ids=None):
    out = []
    for i, cid in enumerate(ids or PEOPLE):
        rig = make_person(cid, PEOPLE[cid])
        rig.location = ((i % 8) * 1.5, -6 - (i // 8) * 2.5, 0)
        out.append(rig.name)
    return out

def stats(cid):
    rig = bpy.data.objects[cid]
    tris = 0
    for o in rig.children_recursive:
        if o.type == 'MESH' and not o.hide_render:
            tris += sum(len(p.vertices) - 2 for p in o.data.polygons)
    return tris, round(max((o.dimensions.z for o in rig.children_recursive if o.type == 'MESH'), default=0), 2)

def export_people(art_dir, ids=None):
    """FBX кожного персонажа: риг + видимі сітки (без базової), у <art_dir>/Characters/<id>.fbx;
    текстури — у <art_dir>/Textures/Characters (finish_person), посилання відносні."""
    out_dir = os.path.join(art_dir, "Characters"); os.makedirs(out_dir, exist_ok=True)
    res = []
    for cid in ids or PEOPLE:
        rig = bpy.data.objects[cid]
        loc = rig.location.copy(); rig.location = (0, 0, 0)
        for o in bpy.context.selected_objects:
            o.select_set(False)
        objs = [rig] + [o for o in rig.children_recursive if o.type == 'MESH' and not o.hide_render]
        for o in objs:
            o.hide_set(False); o.select_set(True)
        bpy.context.view_layer.objects.active = rig
        path = os.path.join(out_dir, cid + ".fbx")
        bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'ARMATURE', 'MESH'},
                                 apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y',
                                 use_mesh_modifiers=True, mesh_smooth_type='FACE', add_leaf_bones=False,
                                 primary_bone_axis='Y', secondary_bone_axis='X', bake_anim=False,
                                 path_mode='RELATIVE', embed_textures=False)
        rig.location = loc
        res.append(path)
    return res
