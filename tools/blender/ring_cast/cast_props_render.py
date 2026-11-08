# -*- coding: utf-8 -*-
"""Контрольні рендери реквізиту «Рингу» (EEVEE): кілька ракурсів кожного предмета з осями контракту хвату,
лінійкою 10 см і каркасом очікуваного (за ТЗ) розміру.

  "C:/Program Files/Blender Foundation/Blender 4.5/blender.exe" -b --factory-startup --python tools/blender/ring_cast/cast_props_render.py -- <репо> <тека виводу> [предмети…] [--views a,b,c]

Вісі: X — червона, Y — зелена, Z — синя (довжина 12 см, початок = центр хвату). Золота лінійка — 10 см із позначками по сантиметру.
Блакитний каркас — очікувані за ТЗ габарити, центровані на фактичному обмежувальному об'ємі: розбіжність видно одразу.
"""
import bpy, sys, os, math, json
from mathutils import Vector

A = sys.argv[sys.argv.index("--") + 1:]
REPO, OUTD = A[0], A[1]
rest = A[2:]
VIEWS = ["front", "side", "top", "q1", "q2", "close"]
if "--views" in rest:
    i = rest.index("--views")
    VIEWS = rest[i + 1].split(",")
    rest = rest[:i] + rest[i + 2:]
NAMES = rest
BLEND = os.path.join(REPO, "ArtSource", "Ring", "Cast", "props.blend")
os.makedirs(OUTD, exist_ok=True)

# очікувані габарити (контрактні осі x, y, z) за ТЗ; None — довільно
EXPECT = {
    "belt": (0.20, None, 1.25),
    "chair": (0.05, 0.84, 0.44),
    "kendo": (0.09, 0.09, 1.12),
    "case": (0.11, 0.405, 0.46),
    "mic": (0.05, 0.05, 0.19),
    "spray_tank": (0.12, 0.30, 0.12),
    "spray_gun": (None, 0.20, None),
}
FOCUS = {"belt": "close", "spray_gun": "close"}

bpy.ops.wm.open_mainfile(filepath=BLEND)
sc = bpy.context.scene
for n in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE", "CYCLES"):
    try:
        sc.render.engine = n
        break
    except TypeError:
        continue
print("engine", sc.render.engine)
sc.render.resolution_x, sc.render.resolution_y = 900, 700
sc.render.film_transparent = False
try:
    sc.eevee.taa_render_samples = 48
except Exception:
    pass
sc.view_settings.view_transform = "AgX" if "AgX" in [i.identifier for i in bpy.types.ColorManagedViewSettings.bl_rna.properties["view_transform"].enum_items] else "Standard"

# світ: холодний градієнтний «павільйон» (для відблисків металу)
w = bpy.data.worlds.new("W")
sc.world = w
w.use_nodes = True
nt = w.node_tree
for n in list(nt.nodes):
    nt.nodes.remove(n)
out = nt.nodes.new("ShaderNodeOutputWorld")
bg = nt.nodes.new("ShaderNodeBackground")
tc = nt.nodes.new("ShaderNodeTexCoord")
gr = nt.nodes.new("ShaderNodeTexGradient")
ramp = nt.nodes.new("ShaderNodeValToRGB")
mp = nt.nodes.new("ShaderNodeMapping")
mp.inputs["Rotation"].default_value = (math.radians(0), 0, 0)
nt.links.new(tc.outputs["Generated"], mp.inputs["Vector"])
nt.links.new(mp.outputs["Vector"], gr.inputs["Vector"])
nt.links.new(gr.outputs["Fac"], ramp.inputs["Fac"])
nt.links.new(ramp.outputs["Color"], bg.inputs["Color"])
nt.links.new(bg.outputs["Background"], out.inputs["Surface"])
ramp.color_ramp.elements[0].color = (0.10, 0.11, 0.14, 1)
ramp.color_ramp.elements[1].color = (0.62, 0.66, 0.74, 1)
bg.inputs["Strength"].default_value = 0.9


def emit(name, col, strength=2.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    t = m.node_tree
    for n in list(t.nodes):
        t.nodes.remove(n)
    o = t.nodes.new("ShaderNodeOutputMaterial")
    e = t.nodes.new("ShaderNodeEmission")
    e.inputs["Color"].default_value = (*col, 1)
    e.inputs["Strength"].default_value = strength
    t.links.new(e.outputs["Emission"], o.inputs["Surface"])
    return m


MAT = {k: emit("ax_" + k, c) for k, c in (("x", (1, 0.1, 0.1)), ("y", (0.1, 1, 0.2)), ("z", (0.2, 0.4, 1)), ("gold", (0.9, 0.7, 0.2)), ("exp", (0.2, 0.75, 1)))}


def bar(p0, p1, r, mat, name="bar"):
    p0, p1 = Vector(p0), Vector(p1)
    d = p1 - p0
    bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=r, depth=d.length, location=(p0 + p1) / 2)
    o = bpy.context.object
    o.rotation_euler = d.to_track_quat("Z", "Y").to_euler()
    o.data.materials.append(mat)
    o.name = name
    o.visible_shadow = False
    return o


def bounds(obj):
    vs = [obj.matrix_world @ v.co for v in obj.data.vertices]
    lo = Vector((min(v.x for v in vs), min(v.y for v in vs), min(v.z for v in vs)))
    hi = Vector((max(v.x for v in vs), max(v.y for v in vs), max(v.z for v in vs)))
    return lo, hi


def lights():
    for (loc, e, sz, col) in (((1.6, -2.2, 2.0), 380, 1.6, (1, 0.96, 0.9)), ((-2.2, -1.0, 1.0), 110, 1.8, (0.8, 0.88, 1.0)), ((0.3, 2.4, 1.6), 200, 1.6, (1.0, 0.9, 0.8))):
        bpy.ops.object.light_add(type="AREA", location=loc)
        L = bpy.context.object
        L.data.energy = e
        L.data.size = sz
        L.data.color = col
        L.rotation_euler = (Vector((0, 0, 0)) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
        L.name = "L_tmp"


props = [o for o in bpy.data.objects if o.name.startswith("Prop_")]
for o in props:
    key = o.name[5:]
    if NAMES and key not in NAMES:
        continue
    for p in props:
        p.hide_render = p is not o
    for x in [x for x in bpy.data.objects if x.name.startswith(("ax", "bar", "L_tmp", "cam"))]:
        bpy.data.objects.remove(x)
    lo, hi = bounds(o)
    size = hi - lo
    cen = (lo + hi) / 2
    diag = size.length
    # фон: темна підлога-циклорама
    # осі контракту
    ax = min(0.12, max(0.06, diag * 0.15))
    bar((0, 0, 0), (ax, 0, 0), 0.0016, MAT["x"], "ax_x")
    bar((0, 0, 0), (0, ax, 0), 0.0016, MAT["y"], "ax_y")
    bar((0, 0, 0), (0, 0, ax), 0.0016, MAT["z"], "ax_z")
    bpy.ops.mesh.primitive_uv_sphere_add(radius=0.004, location=(0, 0, 0))
    s = bpy.context.object
    s.name = "ax_o"
    s.data.materials.append(MAT["gold"])
    # лінійка 10 см біля предмета (уздовж найдовшої осі), збоку
    axis = max(range(3), key=lambda k: size[k])
    off = [0, 0, 0]
    other = [k for k in range(3) if k != axis]
    base = [lo[0] - 0.04, lo[1] - 0.04, lo[2] - 0.04]
    base[axis] = lo[axis]
    p0 = Vector(base)
    d = [0, 0, 0]
    d[axis] = 1
    p1 = p0 + Vector(d) * 0.10
    bar(p0, p1, 0.0012, MAT["gold"], "bar_ruler")
    for k in range(11):
        pk = p0 + Vector(d) * 0.01 * k
        t0 = pk.copy()
        t1 = pk.copy()
        t1[other[0]] += 0.008 if k % 5 == 0 else 0.004
        bar(t0, t1, 0.0008, MAT["gold"], "bar_tick")
    # очікуваний каркас
    ex = EXPECT.get(key)
    if ex:
        e = [ex[i] if ex[i] is not None else size[i] for i in range(3)]
        c = cen
        h = [e[i] / 2 for i in range(3)]
        corners = [Vector((c.x + sx * h[0], c.y + sy * h[1], c.z + sz * h[2])) for sx in (-1, 1) for sy in (-1, 1) for sz in (-1, 1)]
        for i in range(8):
            for j in range(i + 1, 8):
                diffs = sum(1 for k in range(3) if abs(corners[i][k] - corners[j][k]) > 1e-9)
                if diffs == 1:
                    bar(corners[i], corners[j], 0.0007, MAT["exp"], "bar_exp")
    lights()
    # ракурси
    R = max(diag * 1.35, 0.45)
    cams = {
        "front": ((0, -1, 0.0), 0.0),
        "side": ((1, 0, 0.0), 0.0),
        "top": ((0, 0, 1), 0.0),
        "q1": ((0.9, -0.9, 0.55), 0),
        "q2": ((-0.9, -0.8, 0.5), 0),
        "back": ((0, 1, 0.1), 0),
        "close": ((0.8, -1.0, 0.5), 0),
        "tsuba": ((0.8, -1.0, 0.4), 0),
        "tip": ((0.8, -1.0, 0.4), 0),
    }
    for v in VIEWS:
        dv, _ = cams[v]
        dirv = Vector(dv).normalized()
        dist = R * (0.55 if v == "close" else 1.0)
        target = cen
        if v == "close":
            # крупний план: найцікавіша частина — біля хвату/голови; беремо центр хвату для малих і верх для довгих
            target = Vector((0, 0, 0)) if key in ("belt", "kendo") else cen
            dist = (0.55 if key in ("belt",) else (0.40 if key == "kendo" else R * 0.55))
        if v in ("tsuba", "tip"):
            target = Vector((0, 0, 0.19 if v == "tsuba" else 1.0))
            dist = 0.30
        cam_loc = target + dirv * dist
        cd = bpy.data.cameras.new("cam")
        cd.lens = 50 if v != "top" else 50
        cd.clip_start = 0.01
        co = bpy.data.objects.new("cam_" + v, cd)
        sc.collection.objects.link(co)
        co.location = cam_loc
        up_hint = "Y"
        co.rotation_euler = (target - cam_loc).to_track_quat("-Z", "Y" if v != "top" else "Y").to_euler()
        if v == "top":
            co.rotation_euler = (math.radians(0), 0, 0)
            co.location = cen + Vector((0, 0, dist))
            co.rotation_euler = (0, 0, 0)
        sc.camera = co
        # для предметів, де Z — довга вісь, «вгору» камери — Z
        if v in ("front", "side", "q1", "q2", "back", "close"):
            tq = (target - cam_loc).to_track_quat("-Y", "Z")      # камера дивиться вздовж −Z своєї осі; використаємо Z-up
            co.rotation_euler = (target - cam_loc).to_track_quat("-Z", "Y").to_euler()
            # вирівнювання: потрібна Z-up світу
            dirw = (target - cam_loc).normalized()
            right = dirw.cross(Vector((0, 0, 1))).normalized()
            upv = right.cross(dirw).normalized()
            from mathutils import Matrix
            m = Matrix((right, upv, -dirw)).transposed()
            co.rotation_euler = m.to_euler()
        elif v == "top":
            dirw = Vector((0, 0, -1))
            right = Vector((1, 0, 0))
            upv = Vector((0, 1, 0))
            from mathutils import Matrix
            m = Matrix((right, upv, -dirw)).transposed()
            co.rotation_euler = m.to_euler()
        sc.render.filepath = os.path.join(OUTD, "%s_%s.png" % (key, v))
        # масштаб кадру: підганяємо фокус так, щоб предмет заповнив кадр
        cd.lens = 50
        bpy.ops.render.render(write_still=True)
        print("rendered", sc.render.filepath)
        bpy.data.objects.remove(co)
