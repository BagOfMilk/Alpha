# Світло-шоу й ефекти арен «Рингу» (ітерація 2, docs/TRPG_DESIGN.md §11.0.3). Підключається з ring_venues.py (exec).
#
# Власник 27.10.2026: «Немає освітлення, светомузики, єфектів». Тут:
#   * прилади — ті, що змодельовано в арені (PAR-банки на штативах і фермах, голови), кожен — справжнє джерело;
#   * сцени світла (cue) — що світить, яким кольором і куди: «бій» (білий ключ із ферми на ринг, зал притушено),
#     «вихід» (зал темний, голови кольору борця на завісу й рампу, погоня по такту), «фінішер» (білий спалах
#     і перехресні промені), «перемога» (кольорова погоня, конфеті), у палаці — піро, CO2, лазери;
#   * дим (об'ємний туман у межах залу) — промені видно; у спортзалі диму немає (правило власника №6: дешево, клубно);
#   * контент LED-екранів — заставки з ring_textures.py (led_logo, led_fire, led_waves, led_name);
#   * «светомузика» — сцена має такт (BPM) і крок погоні: кольори голів зсуваються щотакту (для відео; у знімку — кадр).
# Сцени пишуться в venue_<id>_cues.txt (гра відтворює їх своїми приладами й частинками), знімки — Logs/venues.

SPARK_COLOR = (1.0, 0.55, 0.15)

def cue(name, **kw):
    c = dict(name=name, house=0.25, ambient=0.06, haze=0.0, pars=1.0, par_color=None, heads=1.0, head_mode="ring",
             head_colors=((1.0, 0.95, 0.9),), led="led_logo", fx=(), follow=None, bpm=0, chase=0, shots=("overview",))
    c.update(kw)
    return c

RED, BLUE, GOLD, WHITE, PURPLE, GREEN = (1.0, 0.12, 0.08), (0.15, 0.3, 1.0), (1.0, 0.7, 0.2), (1.0, 0.97, 0.92), (0.6, 0.15, 1.0), (0.2, 1.0, 0.3)

CUES = {
    "gym": [
        cue("match", house=1.0, ambient=0.35, pars=1.0, shots=("overview", "closeup", "crowd")),
        cue("entrance", house=0.35, ambient=0.12, pars=1.3, par_color=RED, follow="anchor_curtain",
            shots=("overview", "entrance")),
    ],
    "hall": [
        cue("match", house=0.45, ambient=0.08, haze=0.005, pars=1.0, heads=0.8, head_mode="ring", shots=("overview", "closeup", "crowd")),
        cue("entrance", house=0.12, ambient=0.03, haze=0.02, pars=0.25, heads=1.4, head_mode="curtain",
            head_colors=(RED, BLUE, RED, WHITE), led="led_name", bpm=120, chase=1, fx=("sparklers",), follow="anchor_curtain",
            shots=("overview", "entrance")),
        cue("finisher", house=0.15, ambient=0.03, haze=0.02, pars=1.6, heads=1.6, head_mode="fan",
            head_colors=(WHITE, GOLD, WHITE, PURPLE), led="led_fire", shots=("ringside",)),
    ],
    "arena": [
        cue("entrance", house=0.06, ambient=0.02, haze=0.016, pars=0.2, heads=1.6, head_mode="ramp",
            head_colors=(RED, GOLD, RED, WHITE), led="led_fire", bpm=128, chase=2, fx=("pyro", "co2", "lasers"),
            follow="anchor_curtain", shots=("overview", "entrance")),
        cue("match", house=0.3, ambient=0.06, haze=0.008, pars=1.2, heads=0.9, head_mode="ring",
            head_colors=(WHITE,), led="led_logo", shots=("overview", "closeup", "crowd")),
        cue("victory", house=0.5, ambient=0.08, haze=0.012, pars=0.8, heads=1.4, head_mode="crowd",
            head_colors=(GOLD, BLUE, GOLD, PURPLE), led="led_waves", bpm=128, chase=3, fx=("confetti",),
            shots=("overview", "ringside")),
    ],
}

class Show:
    def __init__(self, root, info, scene):
        self.scene, self.info = scene, info
        self.obs = {o.name: o for o in root.children_recursive}
        self.made = []
        self.spots = []
        for k, (pos, d, color, cone, kind) in enumerate(LIGHTS):
            L = bpy.data.lights.new("spot%d" % k, 'SPOT')
            L.spot_size = math.radians(cone)
            L.spot_blend = 0.35
            L.shadow_soft_size = 0.05
            L.use_shadow = kind == "head"
            o = bpy.data.objects.new("spot%d" % k, L)
            scene.collection.objects.link(o)
            o.location = pos
            self.aim(o, Vector(pos) + Vector(d))
            self.spots.append((o, Vector(pos), Vector(d), color, kind))
            self.made.append(o)
        self.areas = []
        for k, (pos, size, color, watts) in enumerate(AREAS):
            L = bpy.data.lights.new("area%d" % k, 'AREA')
            L.color = color
            L.size = size
            L.use_shadow = False
            o = bpy.data.objects.new("area%d" % k, L)
            scene.collection.objects.link(o)
            o.location = pos
            self.areas.append((o, watts))
            self.made.append(o)
        fl = bpy.data.lights.new("follow", 'SPOT')
        fl.spot_size = math.radians(7)
        fl.spot_blend = 0.2
        fl.color = (1.0, 0.98, 0.95)
        self.follow = bpy.data.objects.new("follow", fl)
        scene.collection.objects.link(self.follow)
        self.made.append(self.follow)
        w = bpy.data.worlds.new("show_world")
        w.use_nodes = True
        self.bg = next(n for n in w.node_tree.nodes if n.type == "BACKGROUND")
        self.bg.inputs[0].default_value = (0.55, 0.6, 0.7, 1)
        scene.world = w
        self.haze = self.make_haze()
        self.fx = {}

    @staticmethod
    def aim(o, target):
        d = Vector(target) - o.location
        if d.length > 1e-4:
            o.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()

    def make_haze(self):
        """Дим: куб на весь зал з об'ємним матеріалом (у грі — туман і сітки-промені)."""
        bm = bmesh.new()
        bmesh.ops.create_cube(bm, size=1.0)
        ext = {"gym": (36, 26, 8), "hall": (34, 29, 10), "arena": (100, 90, 24)}[self.info["id"]]
        bmesh.ops.scale(bm, vec=ext, verts=list(bm.verts))
        bmesh.ops.translate(bm, vec=(0, 1, ext[2] / 2 + 0.05), verts=list(bm.verts))
        me = bpy.data.meshes.new("haze")
        bm.to_mesh(me)
        bm.free()
        o = bpy.data.objects.new("haze", me)
        self.scene.collection.objects.link(o)
        m = bpy.data.materials.new("Haze")
        m.use_nodes = True
        nt = m.node_tree
        for n in list(nt.nodes):
            if n.type == "BSDF_PRINCIPLED":
                nt.nodes.remove(n)
        out = next(n for n in nt.nodes if n.type == "OUTPUT_MATERIAL")
        pv = nt.nodes.new("ShaderNodeVolumePrincipled")
        pv.inputs["Color"].default_value = (0.8, 0.82, 0.85, 1)
        pv.inputs["Anisotropy"].default_value = 0.5
        nt.links.new(pv.outputs[0], out.inputs["Volume"])
        me.materials.append(m)
        self.haze_node = pv
        self.made.append(o)
        return o

    def target_for(self, k, n, mode, t=0.0):
        mz = self.info["ring"]["mat_z"]
        curtain = self.obs.get("anchor_curtain")
        cpos = curtain.matrix_world.translation if curtain else Vector((0, 10, 1))
        u = (k + 0.5) / max(1, n)
        if mode == "ring":
            a = 2 * math.pi * u
            return Vector((0.9 * math.cos(a), 0.9 * math.sin(a), mz))
        if mode == "curtain":
            return cpos + Vector(((u - 0.5) * 6.0, -2.0 - 2.0 * h01(k, 91), 0.5))
        if mode == "ramp":
            path = sorted([o for nm, o in self.obs.items() if nm.startswith("anchor_path_")], key=lambda o: o.name)
            if path:
                p = path[k % len(path)].matrix_world.translation
                return p + Vector(((h01(k, 92) - 0.5) * 3.0, 0, 0))
            return cpos
        if mode == "crowd":
            a = 2 * math.pi * u + t
            r = {"gym": 9.0, "hall": 10.0, "arena": 22.0}[self.info["id"]]
            return Vector((r * math.cos(a), 1.0 + r * math.sin(a), 2.0 + 4.0 * h01(k, 93)))
        if mode == "fan":                                             # віяло вгору й назовні
            o, pos, d, c, kind = self.spots[k]
            out = Vector((pos.x, pos.y, 0)).normalized() if pos.xy.length > 0.1 else Vector((1, 0, 0))
            return pos + out * 6.0 + Vector((0, 0, 3.0 + 4.0 * h01(k, 94)))
        return Vector((0, 0, mz))

    def apply(self, c, beat=0):
        heads = [s for s in self.spots if s[4] == "head"]
        for k, (o, pos, d, color, kind) in enumerate(self.spots):
            L = o.data
            if kind == "par":
                L.energy = 1500.0 * c["pars"]
                L.color = c["par_color"] or color
                self.aim(o, pos + d)
            else:
                hi = heads.index((o, pos, d, color, kind))
                cols = c["head_colors"]
                L.color = cols[(hi + beat * max(1, c["chase"])) % len(cols)]
                L.energy = 2600.0 * c["heads"]
                self.aim(o, self.target_for(hi, len(heads), c["head_mode"], t=beat * 0.4))
        for o, watts in self.areas:
            o.data.energy = watts * c["house"]
        self.bg.inputs[1].default_value = c["ambient"]
        self.haze.hide_render = c["haze"] <= 0
        self.haze_node.inputs["Density"].default_value = c["haze"]
        fa = c["follow"] and self.obs.get(c["follow"])
        self.follow.hide_render = not fa
        if fa:
            tp = fa.matrix_world.translation + Vector((0, 0, 1.5))
            far = {"gym": Vector((14, -10, 7)), "hall": Vector((0, -12, 5.5)), "arena": Vector((0, -40, 18))}[self.info["id"]]
            self.follow.location = far
            self.follow.data.energy = 9000.0 if self.info["id"] == "arena" else 4000.0
            self.aim(self.follow, tp)
        led = bpy.data.materials.get("LED_Content")
        if led:
            t = next((n for n in led.node_tree.nodes if n.type == "TEX_IMAGE"), None)
            p = os.path.join(TEX_DIR, c["led"] + ".png")
            if t and os.path.exists(p):
                t.image = bpy.data.images.load(p, check_existing=True)
        for name in ("pyro", "co2", "lasers", "confetti", "sparklers"):
            want = name in c["fx"]
            if want and name not in self.fx:
                self.fx[name] = getattr(self, "fx_" + name)()
            for o in self.fx.get(name, ()):
                o.hide_render = not want

    # ---------------------------------------------------------------- ефекти
    def _emit_mat(self, name, color, strength):
        m = bpy.data.materials.get(name)
        if m:
            return m
        m = bpy.data.materials.new(name)
        m.use_nodes = True
        b = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
        b.inputs["Base Color"].default_value = (*color, 1)
        for key in ("Emission Color", "Emission"):
            if key in b.inputs:
                b.inputs[key].default_value = (*color, 1)
                break
        b.inputs["Emission Strength"].default_value = strength
        return m

    def _obj(self, name, bm, mat):
        me = bpy.data.meshes.new(name)
        bm.to_mesh(me)
        bm.free()
        o = bpy.data.objects.new(name, me)
        self.scene.collection.objects.link(o)
        me.materials.append(mat)
        self.made.append(o)
        return o

    def _sparks(self, bm, base, n, v0, spread, life, salt):
        g = Vector((0, 0, -9.8))
        for i in range(n):
            a = 2 * math.pi * h01(i, salt)
            el = math.radians(90 - spread * h01(i, salt + 1))
            sp = v0 * (0.7 + 0.3 * h01(i, salt + 2))
            v = Vector((math.cos(a) * math.cos(el), math.sin(a) * math.cos(el), math.sin(el))) * sp
            t = life * h01(i, salt + 3)
            p = Vector(base) + v * t + g * (0.5 * t * t)
            if p.z < 0.05:
                continue
            vel = (v + g * t).normalized()
            side = vel.cross(Vector((0, 0, 1)))
            side = (side.normalized() if side.length > 1e-3 else Vector((1, 0, 0))) * 0.012
            ln = 0.05 + 0.12 * h01(i, salt + 4)
            q = [p - side, p + side, p + side - vel * ln, p - side - vel * ln]
            bm.faces.new([bm.verts.new(x) for x in q])

    def fx_pyro(self):
        """Піро-фонтани на якорях anchor_pyro_* (палац): іскри по параболах + помаранчеве світло біля основи."""
        out = []
        bm = bmesh.new()
        anchors = [o for nm, o in self.obs.items() if nm.startswith("anchor_pyro_")]
        for j, a in enumerate(anchors):
            base = a.matrix_world.translation + Vector((0, 0, 0.3))
            self._sparks(bm, base, 420, 10.5, 12.0, 1.6, 100 + j * 7)
            L = bpy.data.lights.new("pyro%d" % j, 'POINT')
            L.color = SPARK_COLOR
            L.energy = 4000.0
            L.shadow_soft_size = 0.5
            lo = bpy.data.objects.new("pyro%d" % j, L)
            self.scene.collection.objects.link(lo)
            lo.location = base + Vector((0, 0, 2.0))
            self.made.append(lo)
            out.append(lo)
        out.append(self._obj("fx_pyro", bm, self._emit_mat("FX_Spark", SPARK_COLOR, 40.0)))
        return out

    def fx_sparklers(self):
        """Холодні іскри (зала): два невеликі фонтани обабіч завіси."""
        out = []
        bm = bmesh.new()
        c = self.obs.get("anchor_curtain")
        cp = c.matrix_world.translation if c else Vector((0, 14, 1))
        for j, sx in enumerate((-2.2, 2.2)):
            base = cp + Vector((sx, -1.4, 0.1))
            self._sparks(bm, base, 260, 5.5, 14.0, 1.1, 200 + j * 7)
            L = bpy.data.lights.new("spk%d" % j, 'POINT')
            L.color = SPARK_COLOR
            L.energy = 900.0
            lo = bpy.data.objects.new("spk%d" % j, L)
            self.scene.collection.objects.link(lo)
            lo.location = base + Vector((0, 0, 1.0))
            self.made.append(lo)
            out.append(lo)
        out.append(self._obj("fx_sparklers", bm, self._emit_mat("FX_Spark", SPARK_COLOR, 40.0)))
        return out

    def fx_co2(self):
        """CO2-струмені: білі об'ємні конуси з краю сцени вгору."""
        out = []
        m = bpy.data.materials.get("FX_CO2") or bpy.data.materials.new("FX_CO2")
        m.use_nodes = True
        nt = m.node_tree
        for n in list(nt.nodes):
            if n.type == "BSDF_PRINCIPLED":
                nt.nodes.remove(n)
        outn = next(n for n in nt.nodes if n.type == "OUTPUT_MATERIAL")
        pv = nt.nodes.new("ShaderNodeVolumePrincipled")
        pv.inputs["Density"].default_value = 1.2
        pv.inputs["Color"].default_value = (0.95, 0.97, 1.0, 1)
        nt.links.new(pv.outputs[0], outn.inputs["Volume"])
        anchors = [o for nm, o in self.obs.items() if nm.startswith("anchor_pyro_")]
        for j, a in enumerate(anchors[1::2] or anchors):
            bm = bmesh.new()
            bmesh.ops.create_cone(bm, cap_ends=True, segments=12, radius1=0.15, radius2=1.1, depth=5.0)
            bmesh.ops.translate(bm, vec=a.matrix_world.translation + Vector((0, -0.6, 2.6)), verts=list(bm.verts))
            out.append(self._obj("fx_co2_%d" % j, bm, m))
        return out

    def fx_lasers(self):
        """Лазери (палац): зелені промені з ферми сцени віялом на трибуни."""
        bm = bmesh.new()
        c = self.obs.get("anchor_curtain")
        cp = c.matrix_world.translation if c else Vector((0, 20, 1))
        src = cp + Vector((0, -1.0, 9.0))
        for i in range(14):
            a = math.radians(-160 + 140 * i / 13)
            tgt = Vector((32 * math.cos(a), 1.0 + 30 * math.sin(a), 8.0 + 5.0 * h01(i, 95)))
            d = tgt - src
            r = bmesh.ops.create_cone(bm, cap_ends=False, segments=6, radius1=0.012, radius2=0.03, depth=d.length)
            rot = d.to_track_quat('Z', 'Y').to_matrix().to_4x4()
            bmesh.ops.transform(bm, matrix=rot, verts=r["verts"])
            bmesh.ops.translate(bm, vec=src + d / 2, verts=r["verts"])
        return [self._obj("fx_lasers", bm, self._emit_mat("FX_Laser", GREEN, 60.0))]

    def fx_confetti(self):
        """Конфеті: тисячі дрібних листків у повітрі над рингом і першими рядами (5 кольорів)."""
        cols = (GOLD, (0.9, 0.9, 0.9), RED, BLUE, (0.85, 0.3, 0.7))
        out = []
        R = {"gym": 8.0, "hall": 9.0, "arena": 16.0}[self.info["id"]]
        top = {"gym": 7.0, "hall": 8.0, "arena": 14.0}[self.info["id"]]
        for ci, colr in enumerate(cols):
            bm = bmesh.new()
            for i in range(700):
                k = ci * 1000 + i
                rr = R * math.sqrt(h01(k, 96))
                a = 2 * math.pi * h01(k, 97)
                p = Vector((rr * math.cos(a), rr * math.sin(a), 0.3 + top * h01(k, 98)))
                ax = Vector((h01(k, 99) - 0.5, h01(k, 100) - 0.5, h01(k, 101) - 0.5)).normalized()
                q = Quaternion(ax, 6.28 * h01(k, 102))
                s = 0.025
                pts = [p + q @ Vector(v) for v in ((-s, -s * 0.6, 0), (s, -s * 0.6, 0), (s, s * 0.6, 0), (-s, s * 0.6, 0))]
                bm.faces.new([bm.verts.new(x) for x in pts])
            m = self._emit_mat("FX_Confetti_%d" % ci, colr, 0.6)
            out.append(self._obj("fx_confetti_%d" % ci, bm, m))
        return out

    def remove(self):
        for o in self.made:
            if o.name in bpy.data.objects:
                bpy.data.objects.remove(o, do_unlink=True)

def write_cues(vid):
    """venue_<id>_cues.txt: сцени світла для гри — рівні, кольори, режим голів, такт, ефекти."""
    with open(os.path.join(OUT, "venue_%s_cues.txt" % vid), "w", encoding="utf-8", newline="\n") as f:
        f.write("# Сцени світла (tools/blender/ring_show.py): ім'я зал амбієнт дим PAR колір_PAR голови режим кольори_голів "
                "LED такт_BPM погоня ефекти прожектор\n")
        f.write("# прилади: %d PAR, %d голів (позиції — у знімках; у грі — з якорів і моделі арени)\n"
                % (sum(1 for l in LIGHTS if l[4] == "par"), sum(1 for l in LIGHTS if l[4] == "head")))
        for c in CUES.get(vid, ()):
            pc = "-" if not c["par_color"] else "%.2f,%.2f,%.2f" % c["par_color"]
            hc = ";".join("%.2f,%.2f,%.2f" % x for x in c["head_colors"])
            f.write("%s %.2f %.2f %.3f %.2f %s %.2f %s %s %s %d %d %s %s\n" % (
                c["name"], c["house"], c["ambient"], c["haze"], c["pars"], pc, c["heads"], c["head_mode"], hc, c["led"],
                c["bpm"], c["chase"], ",".join(c["fx"]) or "-", c["follow"] or "-"))
