# Акценти модульного набору Alpha: символи й квірки іменних персонажів (Поправка №19;
# каталог — Core/Characters/AppearanceCatalog.cs, ключі — KitParts.Accents).
#
# Запуск у Blender після alpha_people.py і alpha_wardrobe.py:
#   exec(open(r"<repo>/tools/blender/alpha_accents.py", encoding="utf-8").read())
#   accent_textures(r"<repo>/Assets/Art"); accents(bpy.data.objects["kit_m"], bpy.data.objects["kit_m.body"])
#
# Колір акценту — частина символу (червоний пояс, золотий перстень, вовче хутро, пір'їна беркута),
# тому матеріали акцентів мають власний запечений колір, а не нейтральну тканину під фарбування.
# Текстури процедурні (numpy, без random — хвилі з фіксованими фазами): власні, CC0 проєкту.

FABRICS.update({
    "acc_red":       ("M_AccRed", 0.3),       # червоне сукно (крайка, стрічка, хустинка)
    "acc_gold":      ("M_AccGold", 0.2),
    "acc_iron":      ("M_AccIron", 0.3),
    "acc_fur":       ("M_AccFur", 0.25),      # сіро-буре вовче хутро
    "acc_sheepskin": ("M_AccSheep", 0.25),    # кучерява овчина
    "acc_feather":   ("M_AccFeather", 1.0),
    "acc_emb_rb":    ("M_EmbRedBlack", 0.32), # вишивка: червоне й чорне по білому (мотив ~4 см — 0,12 читалось крапками)
    "acc_emb_gold":  ("M_EmbGold", 0.12),     # золоте шиття по червоному
    "acc_scar":      ("M_AccScar", 0.1),
    "acc_dleather":  ("M_AccDarkLeather", 0.4),
    "acc_cord":      ("M_AccCord", 0.1),
    "acc_black":     ("M_AccBlack", 0.2),
    "acc_herb":      ("M_AccHerb", 0.2),
    "acc_scarf":     ("M_AccScarf", 0.3),     # темна хустка з дрібним візерунком
})


def accent_textures(art_dir):
    import numpy as np
    out = os.path.join(art_dir, "Textures", "Kit"); os.makedirs(out, exist_ok=True)
    s = 256
    y, x = np.mgrid[0:s, 0:s].astype(np.float32)

    def noise(freq, seed):
        n = np.zeros((s, s), np.float32)
        for k in range(1, 5):
            a = seed * 1.7 + k * 2.3
            n += np.sin((x * np.cos(a) + y * np.sin(a)) * freq * k / s * 6.283 + seed * k) / k
        return (n - n.min()) / (n.max() - n.min() + 1e-6)

    def rgba(rgb, mod=None):
        base = np.ones((s, s, 4), np.float32)
        for c in range(3):
            base[..., c] = rgb[c] * (1.0 if mod is None else (0.75 + 0.5 * mod))
        return np.clip(base, 0, 1)

    def mat(name, arr, tex, rough, metal, hgt=None, strength=2.0):
        n_img = _save_rgba(tex + "_nor", _normal_from_height(hgt, strength), out, False) if hgt is not None else None
        return _pbr_material(name, _save_rgba(tex, arr, out), n_img, rough, metal)

    n1, n2, n3 = noise(3, 1.0), noise(9, 2.0), noise(30, 3.0)
    mat("M_AccGold", rgba((0.83, 0.62, 0.24), n2 * 0.5 + n3 * 0.2), "acc_gold", 0.35, 1.0, n3, 1.0)
    mat("M_AccIron", rgba((0.24, 0.24, 0.25), n2 * 0.6 + n3 * 0.3), "acc_iron", 0.6, 0.9, n3, 1.5)
    strands = 0.5 + 0.5 * np.sin(x * 1.3 + np.sin(y * 0.11) * 4 + n2 * 6)
    mat("M_AccFur", rgba((0.42, 0.38, 0.33), strands * 0.7 + n1 * 0.3), "acc_fur", 0.95, 0.0, strands, 3.0)
    curls = 0.5 + 0.5 * np.sin(np.sqrt(((x % 16) - 8) ** 2 + ((y % 16) - 8) ** 2) * 1.2)
    mat("M_AccSheep", rgba((0.78, 0.72, 0.6), curls * 0.6 + n2 * 0.4), "acc_sheepskin", 0.95, 0.0, curls, 3.0)
    # пір'їна беркута: темно-бура зі світлішими поперечними смугами і світлим стрижнем
    bars = 0.5 + 0.5 * np.sin(y / s * 6.283 * 7)
    shaft = np.exp(-((x - s / 2) ** 2) / (2 * 4.0 ** 2))
    fea = np.dstack([0.22 + 0.2 * bars + 0.5 * shaft, 0.14 + 0.13 * bars + 0.42 * shaft,
                     0.07 + 0.06 * bars + 0.3 * shaft, np.ones_like(bars)])
    mat("M_AccFeather", np.clip(fea, 0, 1), "acc_feather", 0.7, 0.0, bars * 0.5 + shaft, 2.0)
    # вишивка: геометричний ромб і «хрестик» (червоне + чорне по білому полотну)
    cell = 32.0
    cx = np.abs((x % cell) - cell / 2); cy = np.abs((y % cell) - cell / 2)
    rhomb = (cx + cy < cell * 0.38) & (cx + cy > cell * 0.22)
    cross = ((cx < 2.5) | (cy < 2.5)) & (cx + cy < cell * 0.2)
    stitch = (((x + y) % 4) < 2).astype(np.float32) * 0.15
    emb = np.dstack([np.full((s, s), 0.92), np.full((s, s), 0.89), np.full((s, s), 0.82), np.ones((s, s))]).astype(np.float32)
    emb[rhomb] = (0.62, 0.07, 0.06, 1.0)
    emb[cross] = (0.05, 0.04, 0.04, 1.0)
    emb[..., :3] -= stitch[..., None] * (rhomb | cross)[..., None]
    mat("M_EmbRedBlack", np.clip(emb, 0, 1), "acc_emb_rb", 0.85, 0.0, (rhomb | cross).astype(np.float32), 2.0)
    embg = np.dstack([np.full((s, s), 0.5), np.full((s, s), 0.08), np.full((s, s), 0.07), np.ones((s, s))]).astype(np.float32)
    embg[rhomb | cross] = (0.85, 0.66, 0.26, 1.0)
    mat("M_EmbGold", embg, "acc_emb_gold", 0.6, 0.3, (rhomb | cross).astype(np.float32), 2.0)
    mat("M_AccRed", rgba((0.6, 0.09, 0.07), n3 * 0.5 + n2 * 0.3), "acc_red", 0.85, 0.0, n3, 1.0)
    mat("M_AccScar", rgba((0.48, 0.25, 0.22), n2), "acc_scar", 0.5, 0.0, n2, 1.0)
    mat("M_AccDarkLeather", rgba((0.18, 0.12, 0.08), n2 * 0.6 + n3 * 0.4), "acc_dleather", 0.7, 0.0, n3, 1.5)
    twist = 0.5 + 0.5 * np.sin((x + y) * 0.8)
    mat("M_AccCord", rgba((0.45, 0.34, 0.22), twist), "acc_cord", 0.9, 0.0, twist, 2.0)
    mat("M_AccBlack", rgba((0.03, 0.03, 0.03), n2 * 0.3), "acc_black", 0.6, 0.0)
    mat("M_AccHerb", rgba((0.32, 0.38, 0.18), n3 * 0.6 + n1 * 0.4), "acc_herb", 0.9, 0.0, n3, 2.0)
    dots = ((((x % 12) - 6) ** 2 + ((y % 12) - 6) ** 2) < 4).astype(np.float32)
    mat("M_AccScarf", rgba((0.13, 0.11, 0.12), dots * 0.8 + n2 * 0.2), "acc_scarf", 0.85, 0.0, dots, 1.0)


def _bone_point(rig, bone, t=0.0):
    b = rig.data.bones[bone]
    return b.head_local.lerp(b.tail_local, t)


def _mesh_rigid(rig, body, part, kind, bone, build):
    m = bmesh.new()
    build(m)
    return _rigid(f"{rig.name}.{part}", m, rig, bone, kind, body)


def _box(m, c, s):
    r = bmesh.ops.create_cube(m, size=1.0)
    bmesh.ops.scale(m, vec=s, verts=r["verts"])
    bmesh.ops.translate(m, vec=c, verts=r["verts"])


def _torus(m, c, axis, R, r, segs=12, rsegs=5):
    """Тор (кільце) навколо осі axis з центром c."""
    q = Vector(axis).normalized().to_track_quat('Z', 'Y').to_matrix()
    rings = []
    for i in range(segs):
        a = 2 * math.pi * i / segs
        ring = []
        for j in range(rsegs):
            b = 2 * math.pi * j / rsegs
            p = Vector(((R + r * math.cos(b)) * math.cos(a), (R + r * math.cos(b)) * math.sin(a), r * math.sin(b)))
            ring.append(m.verts.new(Vector(c) + q @ p))
        rings.append(ring)
    for i in range(segs):
        for j in range(rsegs):
            m.faces.new((rings[i][j], rings[(i + 1) % segs][j], rings[(i + 1) % segs][(j + 1) % rsegs], rings[i][(j + 1) % rsegs]))


def _sphere(m, c, rad, scale=(1, 1, 1), u=8, v=5):
    r = bmesh.ops.create_uvsphere(m, u_segments=u, v_segments=v, radius=rad)
    bmesh.ops.scale(m, vec=scale, verts=r["verts"])
    bmesh.ops.translate(m, vec=c, verts=r["verts"])


def _rod(m, a, b, rad, segs=6):
    a, b = Vector(a), Vector(b)
    d = b - a
    r = bmesh.ops.create_cone(m, cap_ends=True, segments=segs, radius1=rad, radius2=rad, depth=d.length)
    bmesh.ops.transform(m, matrix=d.to_track_quat('Z', 'Y').to_matrix().to_4x4(), verts=r["verts"])
    bmesh.ops.translate(m, vec=a + d / 2, verts=r["verts"])


def _head_landmarks(rig):
    src = _source(rig)
    dom = _dominant(src, set(rig.data.bones.keys()))
    hv = [v.co for v in src.data.vertices if dom[v.index] == "head"]
    mouth = _landmark(rig, "joint-mouth")

    def eye(side):
        g = "joint-%s-eye" % side
        if g in src.vertex_groups:
            return _landmark(rig, g)
        return Vector((0.032 * (1 if side == "l" else -1), mouth.y, mouth.z + 0.07))

    ez = eye("l").z
    ring = [p for p in hv if abs(p.z - (ez - 0.02)) < 0.012]
    half_w = max(abs(p.x) for p in ring) if ring else 0.075
    back = max(p.y for p in hv)
    front = min(p.y for p in hv)
    return dict(mouth=mouth, eye_l=eye("l"), eye_r=eye("r"), half_w=half_w, back=back, front=front,
                top=max(p.z for p in hv), cy=(back + front) / 2)


def _rigid_cards(rig, body, part, kind, bone, build):
    """Як _mesh_rigid, але UV задає сама побудова (картка пір'їни): триплан не накладається."""
    m = bmesh.new()
    build(m)
    me = bpy.data.meshes.new(f"{rig.name}.{part}")
    m.to_mesh(me); m.free()
    ob = bpy.data.objects.new(me.name, me)
    kit_collection().objects.link(ob)
    for g in body.vertex_groups:
        ob.vertex_groups.new(name=g.name)
    ob.vertex_groups[bone].add(list(range(len(me.vertices))), 1.0, 'REPLACE')
    ob.parent = rig
    mod = ob.modifiers.new("Armature", 'ARMATURE'); mod.object = rig
    ob["kit_part"] = part; ob["kit_kind"] = kind
    ob.data.materials.append(_fabric_material(kind))
    return ob


def accents(rig, body):
    """Усі 24 акценти KitParts.Accents для набору. Складені акценти мають допоміжні частини з
    суфіксом (sash + sash_tails) — гра вмикає їх разом за префіксом ключа."""
    for o in [o for o in rig.children if o.get("kit_kind", "").startswith("acc_")
              or o.get("kit_part") in ("staff",) or str(o.get("kit_part", "")).startswith(("sash", "boyar_belt", "embroidery", "quiver", "herb_pouch"))]:
        bpy.data.objects.remove(o, do_unlink=True)
    out = {}
    zw = _z_of(rig, "spine_01")
    zh = _z_of(rig, "pelvis")
    hl = _head_landmarks(rig)
    neck = _bone_point(rig, "neck_01", 0.0)
    cuff_l = _bone_point(rig, "hand_l")
    cuff_r = _bone_point(rig, "hand_r")
    to_cuff = lambda co: min((co - cuff_l).length, (co - cuff_r).length)

    # --- смуги-оболонки по тілу
    out["sash"] = shell(rig, body, "sash", "acc_red", TORSO | PELVIS | THIGHS, 0.032,
                        cut=lambda co: zw - 0.05 < co.z < zw + 0.04, budget=600)

    def sash_tails(m):
        side = Vector((0.13, -0.07, zw - 0.02))
        _box(m, side + Vector((0.0, 0, -0.15)), (0.05, 0.012, 0.28))
        _box(m, side + Vector((0.04, 0.01, -0.12)), (0.045, 0.012, 0.22))
    out["sash_tails"] = _mesh_rigid(rig, body, "sash_tails", "acc_red", "pelvis", sash_tails)

    out["boyar_belt"] = shell(rig, body, "boyar_belt", "acc_dleather", TORSO | PELVIS | THIGHS, 0.036,
                              cut=lambda co: zw - 0.035 < co.z < zw + 0.015, budget=600)

    def belt_plaques(m):
        for k in range(9):
            a = -1.2 + k * 0.3
            _box(m, (0.155 * math.sin(a), -0.115 * math.cos(a), zw - 0.01), (0.03, 0.008, 0.035))
    out["boyar_belt_plaques"] = _mesh_rigid(rig, body, "boyar_belt_plaques", "acc_gold", "pelvis", belt_plaques)

    emb_cut = lambda co: ((co.z > neck.z - 0.07) or (abs(co.x) < 0.035 and co.y < -0.05 and co.z > zw + 0.15)) and co.z < neck.z + 0.03
    out["embroidery_red_black"] = shell(rig, body, "embroidery_red_black", "acc_emb_rb", TORSO | NECK, 0.0135, cut=emb_cut, budget=700)
    out["embroidery_red_black_cuffs"] = shell(rig, body, "embroidery_red_black_cuffs", "acc_emb_rb", LOWER_ARMS, 0.0135,
                                              cut=lambda co: to_cuff(co) < 0.1, budget=300)
    out["embroidery_gold"] = shell(rig, body, "embroidery_gold", "acc_emb_gold", TORSO | NECK, 0.0175, cut=emb_cut, budget=700)
    out["chain_scars"] = shell(rig, body, "chain_scars", "acc_scar", LOWER_ARMS, 0.0018,
                               cut=lambda co: 0.015 < to_cuff(co) < 0.065, budget=300)
    zu = _bone_point(rig, "upperarm_l", 0.6).z
    out["iron_armrings"] = shell(rig, body, "iron_armrings", "acc_iron", LOWER_ARMS | UPPER_ARMS, 0.012,
                                 cut=lambda co: 0.06 < to_cuff(co) < 0.1 or abs(co.z - zu) < 0.02, budget=600)
    out["wolf_fur_collar"] = shell(rig, body, "wolf_fur_collar", "acc_fur", TORSO | NECK, 0.05,
                                   cut=lambda co: neck.z - 0.09 < co.z < neck.z + 0.05, budget=900)
    out["kerchief"] = shell(rig, body, "kerchief", "acc_red", NECK | TORSO, 0.02,
                            cut=lambda co: neck.z - 0.04 < co.z < neck.z + 0.04, budget=400)
    out["headscarf"] = shell(rig, body, "headscarf", "acc_scarf", {"head"}, 0.016,
                             cut=lambda co: co.z > hl["eye_l"].z + 0.012 or (co.y > hl["cy"] + 0.02 and co.z > hl["mouth"].z - 0.04),
                             hem=(0.12, hl["mouth"].z + 0.02), flare=1.15, rings=2, budget=900)
    py = _bone_point(rig, "pelvis").y
    out["carpenter_apron"] = shell(rig, body, "carpenter_apron", "acc_dleather", PELVIS | THIGHS, 0.05,
                                   cut=lambda co: co.y < py - 0.03 and zh - 0.12 < co.z < zw + 0.02,
                                   hem=(0.3, zh - 0.05), flare=1.05, rings=2, budget=500)
    mz = hl["mouth"].z
    out["scar_cheek"] = shell(rig, body, "scar_cheek", "acc_scar", {"head"}, 0.0015,
                              cut=lambda co: co.x > 0.03 and co.y < hl["mouth"].y + 0.02
                              and abs((co.z - mz) - (co.x - 0.03) * 1.6 - 0.03) < 0.006, budget=120)

    # --- жорсткі речі на кістках
    def feather(m):
        base = Vector((hl["half_w"] * 0.95, hl["cy"] + 0.02, hl["eye_l"].z + 0.06))
        uv = m.loops.layers.uv.verify()
        n = 7
        prev = None
        for i in range(n + 1):
            t = i / n
            p = base + Vector((0.02 * t, 0.05 * t, 0.2 * t))
            w = 0.03 * math.sin(math.pi * min(1.0, t * 1.1 + 0.05))
            # лицьовий ряд і окремий зворотний (свої вершини — інакше Blender не дасть двох граней)
            row = [m.verts.new(p + Vector((-w, 0, 0))), m.verts.new(p + Vector((w, 0, 0))),
                   m.verts.new(p + Vector((-w, 0.0005, 0))), m.verts.new(p + Vector((w, 0.0005, 0)))]
            if prev:
                t0 = (i - 1) / n
                for quad, uvs in (((prev[0], prev[1], row[1], row[0]), ((0, t0), (1, t0), (1, t), (0, t))),
                                  ((row[2], row[3], prev[3], prev[2]), ((0, t), (1, t), (1, t0), (0, t0)))):
                    f = m.faces.new(quad)
                    for lp, uvv in zip(f.loops, uvs):
                        lp[uv].uv = uvv
            prev = row
    out["berkut_feather"] = _rigid_cards(rig, body, "berkut_feather", "acc_feather", "head", feather)

    def ribbon(m):
        c = Vector((0, hl["back"] + 0.02, hl["mouth"].z - 0.02))
        _torus(m, c, (0, 1, 0), 0.025, 0.008, 10, 4)
        _box(m, c + Vector((-0.02, 0.01, -0.1)), (0.018, 0.004, 0.18))
        _box(m, c + Vector((0.02, 0.01, -0.12)), (0.018, 0.004, 0.2))
    out["braid_ribbon_red"] = _mesh_rigid(rig, body, "braid_ribbon_red", "acc_red", "head", ribbon)

    def quiver(m):
        s3 = _bone_point(rig, "spine_03", 0.5)
        a = Vector((s3.x - 0.08, s3.y + 0.15, s3.z - 0.25))
        b = Vector((s3.x + 0.12, s3.y + 0.16, s3.z + 0.22))
        _rod(m, a, b, 0.05, 10)
        d = (b - a).normalized()
        for k in range(5):
            off = Vector(((k - 2) * 0.014, 0.0, (k % 2) * 0.01))
            _rod(m, b + off - d * 0.05, b + off + d * 0.18, 0.004, 4)
            _box(m, b + off + d * 0.15, (0.004, 0.025, 0.05))
    out["quiver"] = _mesh_rigid(rig, body, "quiver", "acc_dleather", "spine_03", quiver)

    def staff(m):
        grip, up = _grip(rig, "l")
        _rod(m, grip - up * 0.95, grip + up * 0.75, 0.017, 7)
        _sphere(m, grip + up * 0.78, 0.035, (1, 1, 1.3))
    out["staff"] = _mesh_rigid(rig, body, "staff", "wood", "hand_l", staff)

    def signet(m):
        f = rig.data.bones.get("ring_01_r") or rig.data.bones["hand_r"]
        mid = f.head_local.lerp(f.tail_local, 0.5)
        _torus(m, mid, f.tail_local - f.head_local, 0.011, 0.003, 10, 4)
        _box(m, mid + Vector((0, -0.012, 0.004)), (0.012, 0.005, 0.012))
    out["gold_signet"] = _mesh_rigid(rig, body, "gold_signet", "acc_gold", "hand_r", signet)

    def tally(m):
        start = Vector((-0.15, -0.06, zw - 0.03))
        for k in range(9):
            _sphere(m, start + Vector((0.004 * math.sin(k), 0.0, -0.028 * k)), 0.008 if k % 3 else 0.012, u=6, v=4)
        _rod(m, start, start + Vector((0, 0, -0.25)), 0.0025, 4)
    out["tally_cord"] = _mesh_rigid(rig, body, "tally_cord", "acc_cord", "pelvis", tally)

    hp = Vector((0.16, -0.03, zw - 0.09))

    def herb_pouch(m):
        _sphere(m, hp, 0.05, (1, 0.6, 1.15))
        for k in range(4):
            _rod(m, hp + Vector((0.0, 0, 0.04)), hp + Vector((-0.02 + k * 0.014, -0.01, 0.12 + 0.02 * k)), 0.005, 4)
    out["herb_pouch"] = _mesh_rigid(rig, body, "herb_pouch", "acc_dleather", "pelvis", herb_pouch)

    def herbs(m):
        for k in range(4):
            _sphere(m, hp + Vector((-0.02 + k * 0.014, -0.01, 0.13 + 0.02 * k)), 0.018, (1, 1, 1.6), u=6, v=4)
    out["herb_pouch_herbs"] = _mesh_rigid(rig, body, "herb_pouch_herbs", "acc_herb", "pelvis", herbs)

    def purse(m):
        c = Vector((-0.12, -0.1, zw - 0.08))
        _sphere(m, c, 0.045, (1, 0.7, 1.0))
        _torus(m, c + Vector((0, 0, 0.035)), (0, 0, 1), 0.025, 0.004, 10, 4)
    out["merchant_purse"] = _mesh_rigid(rig, body, "merchant_purse", "acc_dleather", "pelvis", purse)

    def brooch(m):
        c = _bone_point(rig, "clavicle_r", 0.75) + Vector((0, -0.07, 0.03))
        _torus(m, c, (0, 1, 0), 0.03, 0.006, 14, 4)
        _rod(m, c + Vector((-0.04, -0.004, 0.0)), c + Vector((0.045, -0.004, 0.0)), 0.003, 4)
    out["cloak_brooch_knot"] = _mesh_rigid(rig, body, "cloak_brooch_knot", "acc_gold", "spine_03", brooch)

    def earring(m):
        _torus(m, Vector((hl["half_w"] + 0.003, hl["cy"] + 0.005, hl["mouth"].z + 0.02)), (1, 0, 0), 0.012, 0.0028, 12, 4)
    out["gold_earring"] = _mesh_rigid(rig, body, "gold_earring", "acc_gold", "head", earring)

    def amulets(m):
        c = Vector((0, neck.y - 0.02, neck.z - 0.02))
        _torus(m, c, (0, 0.35, 1), 0.075, 0.003, 16, 4)
        for k in range(3):
            _box(m, Vector(((k - 1) * 0.045, neck.y - 0.1 + abs(k - 1) * 0.01, neck.z - 0.12 + abs(k - 1) * 0.03)), (0.03, 0.012, 0.035))
    out["gris_gris_amulets"] = _mesh_rigid(rig, body, "gris_gris_amulets", "acc_dleather", "spine_03", amulets)

    def hat(m):
        r = hl["half_w"] + 0.02
        base_z = hl["eye_l"].z + 0.025
        res = bmesh.ops.create_cone(m, cap_ends=True, segments=14, radius1=r * 1.08, radius2=r * 0.92, depth=0.13)
        bmesh.ops.translate(m, vec=(0, hl["cy"], base_z + 0.065), verts=res["verts"])
        _torus(m, (0, hl["cy"], base_z + 0.01), (0, 0, 1), r * 1.08, 0.022, 16, 5)
    out["sheepskin_hat"] = _mesh_rigid(rig, body, "sheepskin_hat", "acc_sheepskin", "head", hat)

    def eyepatch(m):
        e = hl["eye_r"]
        _sphere(m, Vector((e.x, e.y - 0.012, e.z)), 0.02, (1, 0.35, 0.8), u=10, v=5)
        _torus(m, Vector((0, hl["cy"], e.z + 0.005)), (0, 0.15, 1), hl["half_w"] + 0.004, 0.0025, 20, 4)
    out["eyepatch"] = _mesh_rigid(rig, body, "eyepatch", "acc_black", "head", eyepatch)
    return out


# ---------------------------------------------------------------- огляд іменних (дзеркало AppearanceCatalog.Named)
# Дані повторюють Core/Characters/AppearanceCatalog.cs лише для знімка в Blender; джерело істини — C#.
NAMED_LOOKS = {
    "maksym":  ("m", "ukrainian", "hair_short03", "#5a3d22", "moustache", "wpn_axe",
                [("shirt", "#ece6d6"), ("trousers", "#e2d6bd"), ("vest", "#6b4a2e"), ("boots", "#4a3020")],
                ["sash", "embroidery_red_black", "berkut_feather", "chain_scars"]),
    "myroslava": ("f", "ukrainian", "hair_braid01", "#2e1f14", "", "wpn_bow",
                [("shirt", "#ece6d6"), ("tunic", "#2f4a2a"), ("skirt_long", "#33302c"), ("boots", "#7a2e1d")],
                ["embroidery_gold", "braid_ribbon_red", "quiver"]),
    "zakhar":  ("m", "ukrainian", "hair_long01", "#d8d4cc", "beard_full", "",
                [("robe", "#ece6d6"), ("vest", "#6b4a2e"), ("shoes", "#4a3020")],
                ["sash", "staff", "berkut_feather", "herb_pouch"]),
    "tuhar":   ("m", "ukrainian", "hair_short02", "#2e1f14", "moustache", "wpn_sabre",
                [("kaftan", "#6e1414"), ("trousers", "#22201e"), ("boots", "#22201e")],
                ["wolf_fur_collar", "boyar_belt", "gold_signet"]),
    "keeper":  ("m", "ukrainian", "", "#9d968c", "beard_short", "wpn_bow",
                [("shirt", "#e2d6bd"), ("vest", "#6b4a2e"), ("trousers", "#5a4030"), ("boots", "#4a3020")],
                ["tally_cord", "sheepskin_hat", "sash"]),
    "healer":  ("f", "ukrainian", "hair_ponytail01", "#9d968c", "", "",
                [("shirt", "#ece6d6"), ("skirt_long", "#33302c"), ("vest", "#5a4030"), ("shoes", "#4a3020")],
                ["headscarf", "herb_pouch", "embroidery_red_black"]),
    "goban":   ("m", "nordic", "hair_short04", "#8a3b1c", "beard_full", "wpn_axe",
                [("tunic", "#5c6b3a"), ("trousers", "#5a4030"), ("cloak", "#2f4a2a"), ("boots", "#4a3020")],
                ["cloak_brooch_knot", "carpenter_apron"]),
    "sindbad": ("m", "middle_eastern", "hair_short01", "#16110d", "beard_short", "wpn_sabre",
                [("robe", "#c9a24a"), ("sharovary", "#2b3a66"), ("turban", "#ece6d6"), ("shoes", "#7a2e1d")],
                ["sash", "gold_earring", "merchant_purse"]),
    "horde_commander": ("m", "west_african", "hair_short04", "#16110d", "beard_short", "wpn_mace",
                [("robe", "#2b3a66"), ("sharovary", "#22201e"), ("boots", "#22201e")],
                ["iron_armrings", "gris_gris_amulets"]),
}

def _hex(c):
    return tuple(int(c[i:i + 2], 16) / 255.0 for i in (1, 3, 5))

def preview_named(row_y=-8.0):
    col = kit_collection()
    for o in [o for o in col.objects if o.get("preview") == "named"]:
        bpy.data.objects.remove(o, do_unlink=True)
    for i, (cid, (g, culture, hair, hc, facial, weapon, outfit, accs)) in enumerate(NAMED_LOOKS.items()):
        body_rig = bpy.data.objects[f"body_{g}_{culture}"]
        dup = body_rig.copy(); dup.data = body_rig.data; dup.name = f"named_{cid}"; dup["preview"] = "named"
        col.objects.link(dup); dup.location = (1.1 * i, row_y, 0)
        for z in body_rig.children:
            # лише зони тіла й очі: повне тіло-проксі й базова сітка в огляд не йдуть (вони під зонами)
            if z.type != 'MESH' or not (str(z.get("kit_part", "")).startswith("body_") or z.name.endswith("low-poly")):
                continue
            zc = z.copy(); zc.data = z.data; zc["preview"] = "named"; col.objects.link(zc); zc.parent = dup
            zc.matrix_parent_inverse.identity()
            for mo in zc.modifiers:
                if mo.type == 'ARMATURE':
                    mo.object = dup
        kit = bpy.data.objects[f"kit_{g}"]
        fitted = fit_rig(kit, dup, f"fitnamed_{cid}"); fitted["preview"] = "named"
        parts = [(p, c) for p, c in outfit] + ([(hair, hc)] if hair else []) + ([(facial, hc)] if facial else [])
        if weapon:
            parts.append((weapon, None))
        wanted = set(p for p, _ in parts)
        acc_prefix = tuple(accs)
        covered = {z for p in wanted for z in COVERS.get(p, [])}
        for zc in dup.children:
            k = zc.get("kit_part", "")
            if k.startswith("body_"):
                zc.hide_set(k[5:] in covered)
        for src in kit.children:
            k = src.get("kit_part", "")
            if not k or src.get("preview"):
                continue
            hit = k in wanted or any(k == a or k.startswith(a + "_") for a in acc_prefix)
            if not hit:
                continue
            c = src.copy(); c.data = src.data; c["preview"] = "named"
            col.objects.link(c); c.parent = fitted; c.matrix_parent_inverse.identity(); c.hide_set(False)
            for mo in c.modifiers:
                if mo.type == 'ARMATURE':
                    mo.object = fitted
            color = dict(parts).get(k)
            if color:
                c.material_slots[0].link = 'OBJECT'
                c.material_slots[0].material = _preview_material(src.material_slots[0].material, _hex(color), f"{cid}_{k}")
