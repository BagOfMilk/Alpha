# Лабораторія парних прийомів у живому Blender (власник 29.10.2026: керований риг — ключові пози ставляться й
# перевіряються очима у вікні Blender, як у аніматора, а не формулами наосліп).
#
#   у Blender MCP:  import sys; sys.path.insert(0, r"<репо>\tools\blender"); import ring_lab; ring_lab.setup()
#
# Бере машинерію генератора (tools/blender/ring_anims.py: Rig з IK-цілями й полюсами, RPose, apply_w, точки на
# кістках партнера P(...)), але:
#   * пози — КЛЮЧОВІ (поза-до-пози): key(кадр, поза A, поза B) розв'язує IK пари й пише ключі кісток у дію;
#     між ключами — криві Blender; кисті з контактом після інтерполяції доводяться IK до кістки партнера (contact);
#   * зіткнення — по справжній сітці фігур на кожному кадрі, кисті й передпліччя включно (collide());
#   * огляд — close(): кадри зблизька з трьох ракурсів, перед показом власнику.
import bpy, os, sys, math
from mathutils import Vector, Matrix, Quaternion
from mathutils.bvhtree import BVHTree

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
# жіноча лабораторія (04.11.2026): сцена lab_scene_f.blend (набір kit_f) — генератор з --female, кліпи в lab_clips_f.
# Жіночий набір брав чоловічі кліпи як є — на вужчих тілах пари проходили одне в одне на 3–8 см.
FEMALE = os.path.basename(bpy.data.filepath).startswith("lab_scene_f")
CLIPS = os.path.join(REPO, "tools", "blender", "lab_clips_f" if FEMALE else "lab_clips")
G = {}                      # простір імен генератора (RPose, stance, P, apply_w, …)
LAB = {}                    # стан лабораторії: риги, Rig-обгортки, дії


def _load_core():
    argv = sys.argv
    sys.argv = ["blender", "--", REPO] + (["--female"] if FEMALE else [])
    try:
        src = open(os.path.join(REPO, "tools", "blender", "ring_anims.py"), encoding="utf-8").read()
        G.clear()
        G["__name__"] = "ring_lab_core"
        exec(compile(src.rsplit("\nmain()", 1)[0], "ring_anims.py", "exec"), G)
    finally:
        sys.argv = argv


def setup(a="Rig_Red", b="Rig_Blue"):
    """Риги пари з IK-цілями генератора. A — у нулі обличчям у −Y, B — навпроти (зсув/курс задає place())."""
    _load_core()
    armA, armB = bpy.data.objects[a], bpy.data.objects[b]
    for arm in (armA, armB):
        arm.animation_data_clear()
        for pb in arm.pose.bones:
            pb.rotation_mode = 'QUATERNION'
            pb.rotation_quaternion = Quaternion()          # калібрування полюсів IK — з пози спокою
            pb.location = Vector()
            for c in list(pb.constraints):
                pb.constraints.remove(c)
        arm.matrix_world = Matrix.Identity(4)
    bpy.context.view_layer.update()
    for o in list(bpy.data.objects):
        if o.name.startswith("ik_"):
            bpy.data.objects.remove(o, do_unlink=True)
    rigA, rigB = G["Rig"](armA), G["Rig"](armB)
    for r, tag in ((rigA, "A"), (rigB, "B")):
        for k, e in list(r.empties.items()):
            e.name = f"ik_{tag}_{k}"
            e.empty_display_size = 0.04
    G["init_rest"](armA)
    LAB.update(armA=armA, armB=armB, rigA=rigA, rigB=rigB, keys={"A": {}, "B": {}}, contacts=[])
    return "ok"


ORIGIN = Vector((0.0, 0.59, 0.0))   # де стоїть A: середина пари (A + зсув B / 2) — у центрі рингу (0, 0)
# Власник 30.10.2026: «все прийоми по центру ринга, если не требуется иначе (приемы в углу, у канатов, за рингом)» —
# місце прийому з карти (moves/*.json, поле "place"); для «center» ORIGIN = −зсув B / 2.


def place(offset, yaw, z=0.0):
    """Поставити пару: A — у ORIGIN (висота z) обличчям у −Y, B — зі зсувом offset від A і поворотом yaw (°)."""
    G["place"](LAB["armA"], ORIGIN + Vector((0, 0, z)), 0)
    G["place"](LAB["armB"], ORIGIN + Vector(offset) + Vector((0, 0, z)), yaw)


def solve(fa, fb):
    """Розв'язати пару для поз fa / fb (RPose або функції від партнера), з контактами за кістками партнера."""
    rigA, rigB = LAB["rigA"], LAB["rigB"]
    for rig in (rigA, rigB):                         # show() вимикає IK рук — повернути
        G["unmute"](rig)
        for s in "lr":
            rig.arm.pose.bones["lowerarm_" + s].constraints[0].influence = 1.0
    heads_in = G["heads_in"]
    pa = (lambda P_: fa(P_)) if callable(fa) else (lambda P_: fa)
    pb = (lambda P_: fb(P_)) if callable(fb) else (lambda P_: fb)
    G["apply_w"](rigB, pb(None))
    for _ in range(3):
        G["apply_w"](rigA, pa(heads_in(rigB, rigA)), heads_in(rigB, rigA))
        G["apply_w"](rigB, pb(heads_in(rigA, rigB)), heads_in(rigA, rigB))
    G["apply_w"](rigA, pa(heads_in(rigB, rigA)), heads_in(rigB, rigA))
    close_fingers(rigA.arm); close_fingers(rigB.arm)


def capture(tag):
    """Поточна поза ригу (після IK) — локальні повороти кісток і зсув таза."""
    arm = LAB["arm" + tag]
    cap = {pb.name: pb.matrix.copy() for pb in arm.pose.bones}
    return G["cap_basis"](arm, cap)


def key(frame, fa, fb):
    """Ключова поза пари в кадрі: розв'язати IK і запам'ятати локальні повороти обох."""
    solve(fa, fb)
    LAB["keys"]["A"][frame] = capture("A")
    LAB["keys"]["B"][frame] = capture("B")
    return frame


def bake(name, length, contacts=(), fps=30):
    """Ключові пози → покадрова дія для обох (slerp з м'яким входом/виходом між ключами). contacts:
    [(роль, кисть 'l'/'r', кістка партнера, частка вздовж, зсув, кадр_від, кадр_до)] — у цих кадрах кисть після
    інтерполяції доводиться IK до точки на кістці партнера (тримає, а не пливе крізь нього)."""
    smooth = G["smooth"]
    frames = int(round(length * fps))
    out = {"A": [], "B": []}
    for tag in ("A", "B"):
        ks = sorted(LAB["keys"][tag].items())
        for f in range(frames + 1):
            if f <= ks[0][0]:
                out[tag].append(ks[0][1]); continue
            if f >= ks[-1][0]:
                out[tag].append(ks[-1][1]); continue
            (f0, b0), (f1, b1) = next((a, b) for a, b in zip(ks, ks[1:]) if a[0] <= f <= b[0])
            k = smooth((f - f0) / (f1 - f0))
            pose = {}
            for n, (l0, r0) in b0.items():
                l1, r1 = b1[n]
                pose[n] = (l0.lerp(l1, k), r0.slerp(r1, k))
            out[tag].append(pose)
    LAB["frames"] = out
    LAB["contacts"] = list(contacts)
    LAB["name"] = name
    return frames


def show(f):
    """Поставити кадр f запеченої пари (з доведенням контактів IK)."""
    for tag in ("A", "B"):
        G["set_basis"](LAB["rig" + tag], LAB["frames"][tag][f])
    G["unmute"](LAB["rigA"]); G["unmute"](LAB["rigB"])
    # контакти: увімкнути IK лише на кистях, що тримають; решту — FK
    for rig in (LAB["rigA"], LAB["rigB"]):
        for s in "lr":
            rig.arm.pose.bones["lowerarm_" + s].constraints[0].influence = 0.0
    bpy.context.view_layer.update()
    for tag, s, bone, along, off, fa, fb in LAB["contacts"]:
        if not (fa <= f <= fb):
            continue
        me, other = (LAB["rigA"], LAB["armB"]) if tag == "A" else (LAB["rigB"], LAB["armA"])
        pb = other.pose.bones[bone]
        W = other.matrix_world
        tgt = W @ (pb.head + (pb.tail - pb.head) * along) + Vector(off)
        me.empties["wrist_" + s].location = tgt
        me.arm.pose.bones["lowerarm_" + s].constraints[0].influence = 1.0
    bpy.context.view_layer.update()


def _meshes(arm):
    return [o for o in bpy.data.objects if o.type == 'MESH' and o.find_armature() == arm and not o.hide_viewport
            and not o.name.split("_", 1)[-1].startswith(("eyebrow", "eyelash", "low-poly"))
            and ".hair_" not in o.name]                      # волосся — м'яка оболонка: кисть може лягти крізь неї


def _bvh(objs, dg):
    verts, polys = [], []
    for o in objs:
        e = o.evaluated_get(dg)
        me = e.to_mesh()
        base = len(verts)
        mw = e.matrix_world
        verts += [mw @ v.co for v in me.vertices]
        polys += [[base + i for i in p.vertices] for p in me.polygons]
        e.to_mesh_clear()
    return BVHTree.FromPolygons(verts, polys) if polys else None


def collide(step=1, allow_hands=True):
    """Зіткнення по СПРАВЖНІЙ сітці на кожному кадрі: сітка A проти сітки B (кисті включно). Дозволені лише кисті,
    що в цей кадр тримають партнера (contacts). Повертає [(кадр, пар трикутників, які частини)]."""
    bad = []
    nA = len(LAB["frames"]["A"])
    for f in range(0, nA, step):
        show(f)
        dg = bpy.context.evaluated_depsgraph_get()
        holding = {(t, s) for t, s, *_r, fa, fb in [(c[0], c[1], c[5], c[6]) for c in LAB["contacts"]] if fa <= f <= fb}
        partsA = _meshes(LAB["armA"]); partsB = _meshes(LAB["armB"])
        def strip(parts, tag):
            if not allow_hands:
                return parts
            held = {s for t, s in holding if t == tag}
            return [o for o in parts if not (held and o.name.endswith("body_hands"))]
        ta, tb = _bvh(strip(partsA, "A"), dg), _bvh(strip(partsB, "B"), dg)
        if ta and tb:
            n = len(ta.overlap(tb))
            if n:
                bad.append((f, n))
    return bad


def close(frames, path, size=420):
    """Лист огляду: кадри × три ракурси зблизька (збоку, 3/4, згори-спереду), центр — середина пари."""
    import numpy as np
    sc = bpy.context.scene
    cam = bpy.data.objects.get("lab_cam") or bpy.data.objects.new("lab_cam", bpy.data.cameras.new("lab_cam"))
    if cam.name not in sc.collection.objects:
        sc.collection.objects.link(cam)
    sc.camera = cam
    cam.data.lens = 40
    try:
        sc.render.engine = 'BLENDER_WORKBENCH'
    except TypeError:
        pass
    sc.display.shading.light = 'STUDIO'
    sc.display.shading.color_type = 'TEXTURE'
    sc.render.resolution_x = sc.render.resolution_y = size
    rows = []
    for f in frames:
        show(f)
        pa = LAB["armA"].matrix_world @ LAB["armA"].pose.bones["spine_03"].head
        pb = LAB["armB"].matrix_world @ LAB["armB"].pose.bones["spine_03"].head
        c = (pa + pb) / 2
        row = []
        for d in (Vector((1, 0, 0.15)), Vector((0.7, -0.7, 0.35)), Vector((0.2, -1, 0.6))):
            cam.location = c + d.normalized() * 2.6
            cam.rotation_euler = (c - cam.location).to_track_quat('-Z', 'Y').to_euler()
            p = os.path.join(os.path.dirname(path), "_lab_tile.png")
            sc.render.filepath = p
            bpy.ops.render.render(write_still=True)
            img = bpy.data.images.load(p, check_existing=False)
            row.append(np.array(img.pixels[:], dtype=np.float32).reshape(size, size, 4))
            bpy.data.images.remove(img)
        rows.append(np.concatenate(row, axis=1))
    sheet = np.concatenate(rows[::-1], axis=0)
    out = bpy.data.images.new("lab_sheet", width=sheet.shape[1], height=sheet.shape[0], alpha=True)
    out.pixels = sheet.ravel().tolist()
    out.filepath_raw = path; out.file_format = 'PNG'; out.save()
    bpy.data.images.remove(out)
    return path


def _part_trees(arm, dg):
    """Сітки фігури по частинах: {частина: (BVH, вершини у світі)}."""
    out = {}
    for o in _meshes(arm):
        e = o.evaluated_get(dg)
        me = e.to_mesh()
        mw = e.matrix_world
        vs = [mw @ v.co for v in me.vertices]
        ps = [list(p.vertices) for p in me.polygons]
        part = o.name.split("_", 1)[-1] if o.name.startswith(("Red_", "Blue_", "Ref_")) else o.name.split(".")[1]
        if ps:
            out[part] = (BVHTree.FromPolygons(vs, ps), vs)
        e.to_mesh_clear()
    return out


def depth(f, tol_touch=0.015):
    """Глибина проникнення в кадрі f: вершини частин A всередині частин B і навпаки (за нормаллю найближчої точки).
    Повертає [(глибина см, 'A.частина → B.частина')] понад допуск: дотик ≤ tol_touch вважається нормою."""
    show(f)
    dg = bpy.context.evaluated_depsgraph_get()
    ta, tb = _part_trees(LAB["armA"], dg), _part_trees(LAB["armB"], dg)
    worst = {}
    for src, dst, ts, td in (("A", "B", ta, tb), ("B", "A", tb, ta)):
        for ps, (_, vs) in ts.items():
            for pd, (tree, _) in td.items():
                for v in vs[::2]:
                    loc, nrm, _i, dist = tree.find_nearest(v, 0.12)
                    if loc is None:
                        continue
                    d = (loc - v).dot(nrm)              # > 0 — вершина за поверхнею (всередині)
                    if d > tol_touch:
                        k = f"{src}.{ps} → {dst}.{pd}"
                        worst[k] = max(worst.get(k, 0.0), d)
    return sorted(((round(v * 100, 1), k) for k, v in worst.items()), reverse=True)


def _arm_verts(arm, side, dg):
    """Вершини кисті й передпліччя боку side у світі (за вагою кісток hand_/lowerarm_ боку)."""
    out = []
    for o in _meshes(arm):
        part = o.name.split("_", 1)[-1]
        if part not in ("body_hands", "body_lowerarms"):
            continue
        e = o.evaluated_get(dg)
        me = e.to_mesh()
        mw = e.matrix_world
        gi = {g.index: g.name for g in o.vertex_groups}
        for v in me.vertices:
            w = max(((gi.get(g.group, ""), g.weight) for g in v.groups), key=lambda x: x[1], default=("", 0))[0]
            if w.endswith("_" + side) and (w.startswith(("hand", "lowerarm", "thumb", "index", "middle", "ring", "pinky"))):
                out.append(mw @ v.co)
        e.to_mesh_clear()
    return out


def settle(tag, sides=("l", "r"), max_depth=0.003, iters=20, prefer=None, max_shift=0.04):
    """Посадка на поверхню: вершини кисті й передпліччя, що опинились усередині тіла партнера (тест парності
    променів), виштовхуються по нормалі найближчої поверхні — ціль IK зап'ястя зсувається, доки глибина ≤ max_depth.
    prefer: {бік: вектор у просторі свого ригу} — кисть виходить з тіла саме туди (за шию, а не на горло).
    Повертає залишкову глибину (см) по боках."""
    me = LAB["rig" + tag]
    other = LAB["armB" if tag == "A" else "armA"]
    res = {}
    for s in sides:
        e = me.empties["wrist_" + s]
        e0 = e.location.copy()
        worst = 0.0
        for _ in range(iters):
            dg = bpy.context.evaluated_depsgraph_get()
            whole = _bvh(_meshes(other), dg)
            worst, push = 0.0, Vector()
            for v in _arm_verts(me.arm, s, dg)[::2]:
                loc, nrm, _i, dist = whole.find_nearest(v, 0.3)
                if loc is None or dist <= max_depth or dist <= worst:
                    continue
                if _inside(whole, v):
                    worst = dist
                    if prefer and s in prefer:
                        dirw = (me.arm.matrix_world.to_3x3() @ Vector(prefer[s])).normalized()
                        push = dirw * (dist + 0.01)
                    else:
                        push = (loc - v).normalized() * (dist + 0.004)
            if worst <= max_depth:
                break
            if max_shift is not None and (e.location + push - e0).length > max_shift:
                break                                      # не відводити кисть від цілі: краще звіт про проникнення
            e.location = e.location + push
            bpy.context.view_layer.update()
        res[s] = round(worst * 100, 1)
    return res


def key_settled(frame, fa, fb, settle_sides=None):
    """key(), але кисті з хватом спершу сідають на поверхню партнера (settle_sides: {"A": ("l","r"), "B": (...)})."""
    solve(fa, fb)
    for tag, sides in (settle_sides or {}).items():
        settle(tag, sides)
    LAB["keys"]["A"][frame] = capture("A")
    LAB["keys"]["B"][frame] = capture("B")
    return frame


_DIRS = (Vector((0, 0, 1)), Vector((0.03, 0.02, -1)).normalized(), Vector((1, 0.04, 0.02)).normalized())


def _inside(tree, v):
    """Точка всередині сітки — за парністю перетинів трьох променів (більшість): працює і для тіла з окремих
    відкритих зон, бо збирається в одну сітку."""
    votes = 0
    for d in _DIRS:
        n, o = 0, v
        for _ in range(16):
            hit = tree.ray_cast(o, d)
            if hit[0] is None:
                break
            n += 1
            o = hit[0] + d * 1e-4
        votes += n % 2
    return votes >= 2


def depth2(f, tol=0.004):   # 4 мм: власник бачить і 1,5 см «руки крізь спину»
    """Проникнення в кадрі f за тестом «всередині»: вершини кожної частини A всередині тіла B (одна сітка) і навпаки;
    глибина — до найближчої поверхні. Повертає [(см, 'A.частина → B')] понад tol."""
    show(f)
    dg = bpy.context.evaluated_depsgraph_get()
    res = []
    for src, me_arm, ot_arm in (("A", LAB["armA"], LAB["armB"]), ("B", LAB["armB"], LAB["armA"])):
        whole = _bvh(_meshes(ot_arm), dg)
        for part, (_t, vs) in _part_trees(me_arm, dg).items():
            worst = 0.0
            for v in vs[::2]:
                loc, _n, _i, dist = whole.find_nearest(v, 0.3)
                if loc is None or dist <= tol:
                    continue
                if _inside(whole, v):
                    worst = max(worst, dist)
            if worst > tol:
                res.append((round(worst * 100, 1), f"{src}.{part} → {'B' if src == 'A' else 'A'}"))
    return sorted(res, reverse=True)


def surface_point(arm, bone, along, from_local, palm=0.025):
    """Точка на шкірі фігури arm: від точки кістки (частка along) назовні в напрямку from_local (простір ригу цієї
    фігури, напр. (0, 1, 0.2) — назад-угору від її потилиці) пускається промінь назад до кістки; перше влучання в
    сітку — шкіра; плюс товщина долоні по нормалі. Світові координати."""
    dg = bpy.context.evaluated_depsgraph_get()
    whole = _bvh(_meshes(arm), dg)
    pb = arm.pose.bones[bone]
    W = arm.matrix_world
    c = W @ (pb.head + (pb.tail - pb.head) * along)
    d = (W.to_3x3() @ Vector(from_local)).normalized()
    start = c + d * 0.35
    hit = whole.ray_cast(start, -d, 0.6)
    if hit[0] is None:
        return c + d * 0.08
    return hit[0] + hit[1] * palm


def to_rig(arm, pw):
    """Світова точка — у простір ригу arm (простір, у якому задаються цілі поз)."""
    return arm.matrix_world.inverted() @ pw


def close_fingers(arm, k=0.75):
    """Звести пальці: у пасці спокою моделі пальці розчепірені — вказівний, безіменний і мізинець повертаються
    до напрямку середнього (частка k). Інакше кисть на шиї чи лікті виглядає «лапою з жахів»."""
    pb = arm.pose.bones
    bpy.context.view_layer.update()
    for s in "lr":
        mid = pb["middle_01_" + s]
        md = (mid.tail - mid.head).normalized()
        for f in ("index", "ring", "pinky"):
            b = pb[f"{f}_01_{s}"]
            d = (b.tail - b.head).normalized()
            ax = d.cross(md)
            if ax.length < 1e-6:
                continue
            ang = d.angle(md) * k
            # обертання в просторі ригу → у локальний простір кістки (до наявного згину)
            R = b.matrix.to_3x3()
            local_ax = (R.inverted() @ ax.normalized()).normalized()
            b.rotation_quaternion = b.rotation_quaternion @ Quaternion(local_ax, ang)
    bpy.context.view_layer.update()


def part_depth(part_a, part_b, tol=0.0):
    """Найбільша глибина вершин частини part_a фігури A всередині фігури B і part_b фігури B всередині A (м)."""
    dg = bpy.context.evaluated_depsgraph_get()
    worst = 0.0
    for src, ot, part in ((LAB["armA"], LAB["armB"], part_a), (LAB["armB"], LAB["armA"], part_b)):
        whole = _bvh(_meshes(ot), dg)
        pt = _part_trees(src, dg).get(part)
        if not pt:
            continue
        for v in pt[1][::2]:
            loc, _n, _i, dist = whole.find_nearest(v, 0.2)
            if loc is not None and dist > max(tol, worst) and _inside(whole, v):
                worst = dist
    return worst


def save_clip(name=None):
    """Покадрові пози пари (локальні повороти кісток + зсув таза) — у tools/blender/lab_clips/<ім'я>.json; генератор
    (ring_anims.py) бере кліп звідти замість формули."""
    import json
    name = name or LAB["name"]
    out = {"fps": 30, "frames": len(LAB["frames"]["A"])}
    for tag in ("A", "B"):
        out[tag] = [{n: [[round(x, 5) for x in l], [round(x, 5) for x in r]] for n, (l, r) in fr.items()}
                    for fr in LAB["frames"][tag]]
    d = CLIPS
    os.makedirs(d, exist_ok=True)
    p = os.path.join(d, name + ".json")
    with open(p, "w", encoding="utf-8") as fh:
        json.dump(out, fh, separators=(",", ":"))
    return p


def surface_frame(arm, bone, along, from_local, palm=0.025, down_bone=("spine_03", "spine_02")):
    """Як surface_point, але ще й рамка долоні: (точка, нормаль назовні, дотична «вниз уздовж тіла» — від кістки
    down_bone[0] до down_bone[1]). Пальці кладуться по дотичній, долоня — на нормаль (не в тіло)."""
    dg = bpy.context.evaluated_depsgraph_get()
    whole = _bvh(_meshes(arm), dg)
    pb = arm.pose.bones[bone]
    W = arm.matrix_world
    c = W @ (pb.head + (pb.tail - pb.head) * along)
    d = (W.to_3x3() @ Vector(from_local)).normalized()
    hit = whole.ray_cast(c + d * 0.35, -d, 0.6)
    if hit[0] is None:
        p, n = c + d * 0.08, d
    else:
        p, n = hit[0] + hit[1] * palm, hit[1].normalized()
    a, b = arm.pose.bones[down_bone[0]], arm.pose.bones[down_bone[1]]
    down = (W @ b.head) - (W @ a.tail)
    tan = (down - n * down.dot(n)).normalized()
    return p, n, tan


def surface_world(arm, bone, along, dir_world, palm=0.03):
    """Точка на шкірі фігури arm з боку світового напрямку dir_world від точки кістки (напр. (0,0,-1) — знизу)."""
    dg = bpy.context.evaluated_depsgraph_get()
    whole = _bvh(_meshes(arm), dg)
    pb = arm.pose.bones[bone]
    W = arm.matrix_world
    c = W @ (pb.head + (pb.tail - pb.head) * along)
    d = Vector(dir_world).normalized()
    hit = whole.ray_cast(c + d * 0.35, -d, 0.6)
    return (hit[0] + hit[1] * palm) if hit[0] is not None else c + d * 0.1


# ---------------------------------------------------------------- ворота 3: перевірки за картою прийому
# Карта — tools/blender/moves/<прийом>.json (ворота 1, з референсів). Перевіряє ТЕ, що власник бачив очима:
# правильна кінцівка на правильному місці, вісь кисті, бік ліктя й голови, проникнення по сітці.

def load_map(name):
    import json
    return json.load(open(os.path.join(REPO, "tools", "blender", "moves", name + ".json"), encoding="utf-8"))


def _palm(arm, s):
    """Центр долоні, нормаль долоні (у бік, куди згинаються пальці) і напрям пальців — у світі."""
    pb = arm.pose.bones
    W = arm.matrix_world
    h = W @ pb["hand_" + s].head
    m0 = W @ pb["middle_01_" + s].head
    tip = W @ pb["middle_03_" + s].tail
    i0, p0 = W @ pb["index_01_" + s].head, W @ pb["pinky_01_" + s].head
    n = (i0 - p0).cross(m0 - h).normalized()
    if (tip - m0).dot(n) < 0:               # нормаль — у бік долоні (туди, куди зігнуті пальці)
        n = -n
    return (h + m0) / 2 + n * 0.012, n, (m0 - h).normalized()


def check_frame(mv, f):
    """Провали кадру f за картою mv (фаза тримання). Кадр уже поставлений show(f)."""
    lim = mv["limits"]
    out = []
    hold = any(ph.get("hold") and ph["from"] <= f <= ph["to"] for ph in mv["phases"])
    for me_tag, ot_tag in (("A", "B"), ("B", "A")):
        me, ot = LAB["arm" + me_tag], LAB["arm" + ot_tag]
        if not hold:
            continue
        for limb, spec in mv["hold"].items():
            if not limb.startswith("hand_"):
                continue
            s = limb[-1]
            if "from" in spec:
                tgt, nrm, _t = surface_frame(ot, spec["bone"], spec["along"], spec["from"], palm=0.0)
            else:
                tgt = surface_world(ot, spec["bone"], spec["along"], spec["from_world"], palm=0.0)
                nrm = Vector(spec["from_world"]).normalized()
            c, pn, fd = _palm(me, s)
            dist = (c - tgt).length * 100 - spec.get("palm_off", 0.03) * 100
            if abs(dist) > lim["contact_cm"]:
                out.append(f"{me_tag}.{limb}: долоня за {dist:.1f} см від «{spec['on']}»")
            ang = math.degrees(pn.angle(-nrm))
            if ang > lim["axis_deg"]:
                out.append(f"{me_tag}.{limb}: долоня не до шкіри — {ang:.0f}°")
            if "fingers_dir_partner" in spec:
                want = (ot.matrix_world.to_3x3() @ Vector(spec["fingers_dir_partner"])).normalized()
                want = (want - nrm * want.dot(nrm)).normalized()
                fa = math.degrees(fd.angle(want))
                if fa > lim["axis_deg"] + 15:
                    out.append(f"{me_tag}.{limb}: пальці не туди — {fa:.0f}°")
        pb, W = me.pose.bones, me.matrix_world
        if "elbow_r" in mv["hold"]:
            drop = ((W @ pb["upperarm_r"].head).z - (W @ pb["lowerarm_r"].head).z) * 100
            if drop > lim["elbow_drop_cm"]:
                out.append(f"{me_tag}.лікоть_r: нижче плеча на {drop:.0f} см")
        if mv["hold"].get("head", "").startswith("зі свого лівого"):
            hm, ho = W @ pb["head"].head, ot.matrix_world @ ot.pose.bones["head"].head
            side = (W.inverted() @ ho - W.inverted() @ hm).x       # голова партнера має бути праворуч (−X), моя — зліва
            if side > -0.02:
                out.append(f"{me_tag}.голова: не зліва від голови партнера")
    pen = depth2(f, tol=lim["pen_cm"] / 100)
    for d, where in pen[:3]:
        out.append(f"проникнення {d:.1f} см ({where})")
    return out


def check_clip(mv, step=1, report=None):
    """Усі кадри; повертає {кадр: [провали]} і пише звіт Logs/ring_anims/<прийом>_check.md."""
    res = {}
    for f in range(0, len(LAB["frames"]["A"]), step):
        show(f)
        bad = check_frame(mv, f)
        if bad:
            res[f] = bad
    lines = [f"# Перевірка «{mv['title']}» за картою tools/blender/moves/{mv['name']}.json", "",
             f"Кадрів з провалами: {len(res)} з {len(LAB['frames']['A'])}", ""]
    for f, b in res.items():
        lines.append(f"- кадр {f}: " + "; ".join(b))
    path = report or os.path.join(REPO, "Logs", "ring_anims", mv["name"] + "_check.md")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    open(path, "w", encoding="utf-8", newline="\n").write("\n".join(lines) + "\n")
    return res


# ---------------------------------------------------------------- ворота 2: кисті за картою
def _hand_pen(arm, s, other):
    """Скільки вершин самої кисті боку s усередині тіла other (без кистей і передпліч суперника)."""
    dg = bpy.context.evaluated_depsgraph_get()
    whole = _bvh([o for o in _meshes(other) if not o.name.endswith(("body_hands", "body_lowerarms"))], dg)
    n = 0
    for o in _meshes(arm):
        if not o.name.endswith("body_hands"):
            continue
        e = o.evaluated_get(dg); me = e.to_mesh(); g = {x.index: x.name for x in o.vertex_groups}
        for v in list(me.vertices)[::2]:
            w = max(((g.get(x.group, ""), x.weight) for x in v.groups), key=lambda z: z[1], default=("", 0))[0]
            if w.endswith("_" + s) and _inside(whole, e.matrix_world @ v.co):
                n += 1
        e.to_mesh_clear()
    return n


def _arm_pen(arm, s, other, dg=None):
    """Скільки вершин руки (кисть, передпліччя) боку s усередині тіла other."""
    dg = dg or bpy.context.evaluated_depsgraph_get()
    # кисті суперника не рахуються: за картою вони тримають цю руку (лікоть) — це задуманий контакт, не зіткнення
    whole = _bvh([o for o in _meshes(other) if not o.name.endswith(("body_hands", "body_lowerarms"))], dg)   # кисть і передпліччя суперника тримають мій лікоть
    return sum(1 for v in _arm_verts(arm, s, dg)[::2] if _inside(whole, v))


def reach_with_clavicle(arm, s, target_w, max_deg=28.0):
    """Ціль поза досяжністю — плече виноситься вперед ключицею (як у живої людини), до max_deg."""
    pb = arm.pose.bones
    W = arm.matrix_world
    up, lo = pb["upperarm_" + s], pb["lowerarm_" + s]
    L = (up.length + lo.length) * 0.985
    cl = pb["clavicle_" + s]
    for _ in range(6):
        bpy.context.view_layer.update()
        sh = W @ up.head
        gap = (target_w - sh).length - L
        if gap <= 0.003:
            return gap
        c0 = W @ cl.head
        a, b = (sh - c0), (target_w - c0)
        ax = a.cross(b)
        if ax.length < 1e-6:
            return gap
        step = min(math.radians(max_deg) / 3, gap / max(a.length, 1e-3) * 1.2)
        if math.degrees(cl.rotation_quaternion.angle) > max_deg:
            return gap
        Wi = W.to_3x3().inverted()
        ax_l = (Wi @ ax).normalized()                  # вісь у просторі арматури
        h = cl.head.copy()
        cl.matrix = Matrix.Translation(h) @ Quaternion(ax_l, step).to_matrix().to_4x4() @ Matrix.Translation(-h) @ cl.matrix
    bpy.context.view_layer.update()
    return (target_w - W @ up.head).length - L


def place_hand(tag, s, spec, thumb_world=Vector((0, 0, 1))):
    """Поставити кисть ригу tag за специфікацією карти: долоня на шкірі цілі, пальці — за картою, долоня — до шкіри;
    полюс ліктя — серед кандидатів той, з яким рука не входить у тіло суперника (а за рівних — вищий лікоть)."""
    rig = LAB["rig" + tag]
    arm, ot = rig.arm, LAB["armB" if tag == "A" else "armA"]
    W = arm.matrix_world
    Ri = W.to_3x3().inverted()
    if "from" in spec:
        tgt, nrm, _t = surface_frame(ot, spec["bone"], spec["along"], spec["from"], palm=0.0)
    else:
        tgt = surface_world(ot, spec["bone"], spec["along"], spec["from_world"], palm=0.0)
        nrm = Vector(spec["from_world"]).normalized()
    if "fingers_dir_world" in spec:              # пальці — у світових осях (ланки чейну: суперник розвернутий)
        want = Vector(spec["fingers_dir_world"])
    elif "fingers_dir_partner" in spec:
        want = ot.matrix_world.to_3x3() @ Vector(spec["fingers_dir_partner"])
    else:                                    # уздовж кістки цілі (охопити лікоть / передпліччя)
        pb_ = ot.pose.bones[spec["bone"]]
        want = ot.matrix_world.to_3x3() @ (pb_.tail - pb_.head)
    want = (want - nrm * want.dot(nrm)).normalized()
    grip_w = tgt + nrm * spec.get("palm_off", 0.03)               # центр долоні — над шкірою на товщину долоні
    ik = arm.pose.bones["lowerarm_" + s].constraints[0]
    rig.iks["hand_" + s].influence = 1.0
    e = rig.empties["wrist_" + s]
    best = None
    # кандидати осі: пальці за картою, повернуті навколо нормалі шкіри (±25°) і з піднятими кінчиками (0..30°) —
    # обираємо без проникнення руки, далі — найменша похибка долоні (вісь не «вгадується», а перевіряється)
    for up_sign in (1.0, -1.0):
        for yaw in (0.0, -25.0, 25.0, -50.0, 50.0):
            for lift in (0.0, 15.0, 30.0, 45.0):
              for roll in (0.0, -20.0, 20.0):
                  w2 = Quaternion(nrm, math.radians(yaw)) @ want
                  w2 = Quaternion(w2.cross(nrm).normalized(), math.radians(-lift)) @ w2
                  up = (thumb_world - nrm * thumb_world.dot(nrm))
                  if up.length < 1e-3:
                    up = nrm.cross(w2)
                  up = Quaternion(w2.normalized(), math.radians(roll)) @ (up.normalized() * up_sign)   # крен навколо пальців
                  f_l, u_l = Ri @ w2, Ri @ up
                  e.location = W @ G["wrist_for_grip"](W.inverted() @ grip_w, f_l)
                  e.rotation_quaternion = W.to_quaternion() @ G["hand_rotation"](rig, s, f_l, u_l)
                  bpy.context.view_layer.update()
                  c, pn, fd = _palm(arm, s)
                  err = math.degrees(pn.angle(-nrm))
                  if err > 45:
                      continue
                  pen = _arm_pen(arm, s, ot)
                  key = (pen, err)
                  if best is None or key < best[0]:
                      best = (key, e.location.copy(), e.rotation_quaternion.copy())
    if best is None:
        best = ((0, 99), e.location.copy(), e.rotation_quaternion.copy())
    e.location, e.rotation_quaternion = best[1], best[2]
    ik.influence = 1.0
    bpy.context.view_layer.update()
    reach_with_clavicle(arm, s, e.location)
    # лікоть: кандидати навколо осі плече—кисть; найменше проникнення руки, далі — висота ліктя
    sh = W @ arm.pose.bones["upperarm_" + s].head
    wr = e.location.copy()
    axis = (wr - sh).normalized()
    ref = Vector((0, 0, 1)) - axis * axis.z
    if ref.length < 1e-3:
        ref = Vector((1, 0, 0))
    ref.normalize()
    side = ref.cross(axis)
    el = rig.empties["elbow_" + s]
    cands = []
    for k in range(12):
        a = 2 * math.pi * k / 12
        d = ref * math.cos(a) + side * math.sin(a)
        el.location = (sh + wr) / 2 + d * 0.5
        bpy.context.view_layer.update()
        pen = _arm_pen(arm, s, ot)
        ez = (W @ arm.pose.bones["lowerarm_" + s].head).z
        cands.append((pen, -ez, el.location.copy()))
    cands.sort(key=lambda c: (c[0], c[1]))
    el.location = cands[0][2]
    bpy.context.view_layer.update()
    # якщо рука все ще торкається тіла глибше за шкіру — відсунути долоню по нормалі шкіри (до 3 см), не в тіло
    pen = _hand_pen(arm, s, ot)                 # підйом — лише якщо в тілі сама кисть (передпліччя — справа ліктя)
    lifted = 0.0
    while pen > 3 and lifted < 0.03:              # кілька вершин кінчика — це дотик, не проникнення
        e.location = e.location + nrm * 0.005
        lifted += 0.005
        bpy.context.view_layer.update()
        pen = _hand_pen(arm, s, ot)
    cands[0] = (_arm_pen(arm, s, ot),) + cands[0][1:]
    return {"кут_долоні": round(best[0][1]), "проникнення_руки": cands[0][0]}


def tuck_thumb(arm, s, k=0.85):
    """Великий палець — уздовж вказівного (кисть «лопаткою»): на шиї він інакше впирається в потилицю суперника."""
    pb = arm.pose.bones
    bpy.context.view_layer.update()
    idx = pb["index_01_" + s]
    want = (idx.tail - idx.head).normalized()
    for k_ in ("01", "02"):
        b = pb[f"thumb_{k_}_{s}"]
        d = (b.tail - b.head).normalized()
        ax = d.cross(want)
        if ax.length < 1e-6:
            continue
        R = b.matrix.to_3x3()
        b.rotation_quaternion = b.rotation_quaternion @ Quaternion((R.inverted() @ ax.normalized()).normalized(), d.angle(want) * k)
        bpy.context.view_layer.update()


def place_hands(mv):
    """Усі кисті обох за фазою тримання карти."""
    out = {}
    for tag in ("A", "B"):
        for limb, spec in mv["hold"].items():
            if limb.startswith("hand_"):
                if spec.get("thumb") == "притиснутий":
                    tuck_thumb(LAB["arm" + tag], limb[-1])
                out[f"{tag}.{limb}"] = place_hand(tag, limb[-1], spec)
    return out
