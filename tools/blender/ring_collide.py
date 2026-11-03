# Перевірка колізій арен «Рингу» (власник 27.10.2026: «перевір шоб ніде колізії не було»). Підключається з
# ring_venues.py (exec), запуск — прапорець --check:
#
#   "C:\Program Files\Blender Foundation\Blender 4.5\blender.exe" -b --python tools/blender/ring_venues.py -- <репо> gym hall arena --check
#
# Перевіряються справжні сітки (BVH), не габарити:
#   * стілець / крісло (props.fbx, LOD0) — проти інших стільців і нерухомої геометрії арени;
#   * фанат у своїй запеченій позі (обидві фази кліпу) — проти інших фанатів, чужих стільців і арени
#     (свій стілець не рахується: на ньому сидять);
#   * чисті зони: ринг з килимками (жодного стільця чи фаната) і прохід борця від завіси до рингу (коридор 1,4 м).
# Дотик не є колізією: усе, що стоїть на підлозі, перед перевіркою піднімається на LIFT, а сітки підтискаються до
# центру на SHRINK — так рахується лише справжнє проникнення глибше за кілька міліметрів.
# Звіт — Assets/Art/Ring/venue_<id>_collisions.txt; ненульовий підсумок — помилка генератора.

from mathutils.bvhtree import BVHTree

LIFT = 0.012          # м: опора на підлогу / сходинку — не колізія
SHRINK = 0.006        # м: дотик поверхонь — не колізія

def _tree(me, M, shrink=SHRINK):
    vs = [M @ v.co for v in me.vertices]
    if shrink:
        c = sum(vs, Vector()) / max(1, len(vs))
        vs = [v + (c - v).normalized() * shrink if (c - v).length > shrink else v for v in vs]
    polys = [tuple(p.vertices) for p in me.polygons]
    return BVHTree.FromPolygons(vs, polys, epsilon=0.0), vs

def _bounds(vs):
    xs, ys, zs = [v.x for v in vs], [v.y for v in vs], [v.z for v in vs]
    return (min(xs), min(ys), min(zs)), (max(xs), max(ys), max(zs))

def _boxes_touch(a, b, pad=0.0):
    return all(a[0][i] - pad <= b[1][i] and b[0][i] - pad <= a[1][i] for i in range(3))

def check_collisions(root, info):
    vid = info["id"]
    lines, bad = [], {}
    bad_fans = set()
    def report(kind, where, fans_=()):
        bad_fans.update(fans_)
        bad[kind] = bad.get(kind, 0) + 1
        if bad[kind] <= 60:
            lines.append("%s: %s" % (kind, where))
    # нерухома арена (після злиття), без підлог під ногами — на них стоять (перевірка висоти — підйом LIFT)
    statics = []
    FLAT = ("Cable", "carpet", "Carpet", "Decal", "_Line", "Paint", "rubber_tiles", "Gym_Line")
    for o in root.children_recursive:
        if o.type != 'MESH' or o.hide_render or any(k in o.name for k in FLAT):
            continue                                   # кабелі, килими, розмітка, наклейки, килимки — лежать на підлозі
        t, vs = _tree(o.data, o.matrix_world, shrink=0.0)
        statics.append((o.name, t, _bounds(vs)))
    lib = props_library()
    # стільці
    chairs = []
    for k, (pm, pp, py, pc) in enumerate(PLACE):
        M = Matrix.Translation((pp[0], pp[1], pp[2] + LIFT)) @ Matrix.Rotation(py, 4, 'Z')
        t, vs = _tree(lib[pm][0], M)
        chairs.append((k, pm, pp, t, _bounds(vs)))
    cell = 1.2
    grid = {}
    for c in chairs:
        grid.setdefault((int(c[2][0] // cell), int(c[2][1] // cell)), []).append(c)
    def near(x, y):
        gx, gy = int(x // cell), int(y // cell)
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                for c in grid.get((gx + dx, gy + dy), ()):
                    yield c
    for (k, pm, pp, t, bb) in chairs:
        for (k2, pm2, pp2, t2, bb2) in near(pp[0], pp[1]):
            if k2 <= k or not _boxes_touch(bb, bb2):
                continue
            if t.overlap(t2):
                report("стілець↔стілець", "%s (%.2f, %.2f) і (%.2f, %.2f)" % (pm, pp[0], pp[1], pp2[0], pp2[1]))
        for name, ts, bbs in statics:
            if _boxes_touch(bb, bbs) and t.overlap(ts):
                report("стілець↔арена", "%s (%.2f, %.2f, %.2f) у «%s»" % (pm, pp[0], pp[1], pp[2], name))
    # фанати: запечені пози обох фаз
    fans_meshes = {}
    if CROWD and os.path.exists(CROWD_LIB):
        want = {"pose_%s_%s_%d_L0" % (c[4], c[5], ph) for c in CROWD for ph in (0, 1)}
        with bpy.data.libraries.load(CROWD_LIB, link=False) as (src, dst):
            dst.meshes = [n for n in src.meshes if n in want]
        fans_meshes = {m.name: m for m in bpy.data.meshes if m.name in want}
    fans = []
    for i, (x, y, z, yaw, fan, clip, ph, col) in enumerate(CROWD):
        trees = []
        for p_ in (0, 1):
            me = fans_meshes.get("pose_%s_%s_%d_L0" % (fan, clip, p_))
            if me is None:
                continue
            M = Matrix.Translation((x, y, z + LIFT)) @ Matrix.Rotation(math.radians(yaw), 4, 'Z')
            t, vs = _tree(me, M)
            trees.append((t, _bounds(vs)))
        if not trees:
            continue
        own = set()                                    # свій стілець (і його рюкзак, куртка) — у межах 0,35 м
        for c in near(x, y):
            if math.hypot(c[2][0] - x, c[2][1] - y) < 0.35 and abs(c[2][2] - (CROWD[i][2] if CROWD[i][5].startswith("Cam_") else c[2][2])) < 0.5:
                own.add(c[0])
        fans.append((i, x, y, clip, trees, own))
    fgrid = {}
    for f in fans:
        fgrid.setdefault((int(f[1] // cell), int(f[2] // cell)), []).append(f)
    for (i, x, y, clip, trees, own) in fans:
        gx, gy = int(x // cell), int(y // cell)
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                for (j, x2, y2, clip2, trees2, own2) in fgrid.get((gx + dx, gy + dy), ()):
                    if j <= i:
                        continue
                    if any(_boxes_touch(a[1], b[1]) and a[0].overlap(b[0]) for a in trees for b in trees2):
                        report("фанат↔фанат", "%s (%.2f, %.2f) і %s (%.2f, %.2f)" % (clip, x, y, clip2, x2, y2), (j,))
        for c in near(x, y):
            if c[0] in own:
                continue
            if any(_boxes_touch(a[1], c[4]) and a[0].overlap(c[3]) for a in trees):
                yw = math.radians(CROWD[i][3])
                dx_, dy_ = c[2][0] - x, c[2][1] - y
                lx, ly = dx_ * math.cos(-yw) - dy_ * math.sin(-yw), dx_ * math.sin(-yw) + dy_ * math.cos(-yw)
                report("фанат↔стілець", "%s (%.2f, %.2f) у %s (%.2f, %.2f) — стілець відносно фаната: вбік %.2f, вперед %.2f, вгору %.2f"
                       % (clip, x, y, c[1], c[2][0], c[2][1], lx, -ly, c[2][2] - CROWD[i][2]), (i,))
        for name, ts, bbs in statics:
            if any(_boxes_touch(a[1], bbs) and a[0].overlap(ts) for a in trees):
                report("фанат↔арена", "%s (%.2f, %.2f) у «%s»" % (clip, x, y, name), (i,))
    # реквізит ↔ реквізит: кожен предмет (with item / itemize у ring_venues.py) проти інших
    items = []
    for name, bm in ITEMS.items():
        if not bm.verts:
            continue
        bm.verts.index_update()                      # без цього індекси вершин випадкові — BVH падає
        bm.verts.ensure_lookup_table()
        vs = [v.co.copy() for v in bm.verts]
        c = sum(vs, Vector()) / len(vs)
        vs = [v + (c - v).normalized() * SHRINK if (c - v).length > SHRINK else v for v in vs]
        t = BVHTree.FromPolygons(vs, [[v.index for v in f.verts] for f in bm.faces], epsilon=0.0)
        items.append((name, t, _bounds(vs)))
    for i_ in range(len(items)):
        for j_ in range(i_ + 1, len(items)):
            (na, ta, ba), (nb, tb, bb) = items[i_], items[j_]
            if _boxes_touch(ba, bb) and ta.overlap(tb):
                report("реквізит↔реквізит", "«%s» і «%s»" % (na, nb))
    # чисті зони: ринг з килимками і прохід борця
    mh = info["ring"]["mats_half"] + 0.15
    path = sorted([o for o in root.children_recursive if o.name.startswith("anchor_path_")], key=lambda o: o.name)
    pts = [o.matrix_world.translation.xy.copy() for o in path]
    def in_path(p):
        for a, b in zip(pts, pts[1:]):
            ab = b - a
            t_ = max(0.0, min(1.0, (p - a).dot(ab) / max(1e-6, ab.length_squared)))
            if (a + ab * t_ - p).length < 0.7:
                return True
        return False
    for (k, pm, pp, t, bb) in chairs:
        p = Vector((pp[0], pp[1]))
        if k in STAFF:
            continue                                   # службові стільці (хронометрист, коментатори) — біля рингу за задумом
        if abs(p.x) < mh and abs(p.y) < mh:
            report("стілець у зоні рингу", "(%.2f, %.2f)" % (p.x, p.y))
        if in_path(p):
            report("стілець у проході борця", "(%.2f, %.2f)" % (p.x, p.y))
    for (i, x, y, clip, trees, own) in fans:
        p = Vector((x, y))
        if clip.startswith("Cam_"):
            continue                                   # оператори працюють біля рингу й у проході за задумом
        if abs(x) < mh and abs(y) < mh:
            report("фанат у зоні рингу", "(%.2f, %.2f)" % (x, y), (i,))
        if in_path(p):
            report("фанат у проході борця", "(%.2f, %.2f) %s" % (x, y, clip), (i,))
    total = sum(bad.values())
    with open(os.path.join(OUT, "venue_%s_collisions.txt" % vid), "w", encoding="utf-8", newline="\n") as f:
        f.write("# Колізії арени %s (tools/blender/ring_collide.py): стільців %d, фанатів %d, нерухомих сіток %d\n"
                % (vid, len(chairs), len(fans), len(statics)))
        f.write("# Підсумок: %d%s\n" % (total, "" if total else " — колізій немає"))
        for kind, n in sorted(bad.items()):
            f.write("%s %d\n" % (kind, n))
        f.write("\n".join(lines) + ("\n" if lines else ""))
    print("CHECK %s: %s" % (vid, ", ".join("%s %d" % kv for kv in sorted(bad.items())) or "колізій немає"), flush=True)
    return total, bad_fans
