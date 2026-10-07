# Повне перезбирання модульного набору персонажів одним запуском (без живої сесії Blender MCP).
# Раніше набір збирався десятками викликів у відкритому Blender, і відтворити його було неможливо.
#
#   "C:\Program Files\Blender Foundation\Blender 4.5\blender.exe" -b --python tools/blender/build_kit_all.py -- <корінь репо>
#
# Кроки — ті самі, що вручну 07.10.2026: тіла набору (m, f) → гардероб і броня → речі з бібліотеки MPFB
# замість оболонок → зачіски → акценти іменних → тіла 8 культур × 2 статі → експорт FBX → маніфест.
import bpy, os, sys, io, contextlib, shutil, traceback, time

R = sys.argv[sys.argv.index("--") + 1] if "--" in sys.argv else os.getcwd()
for name in ("alpha_people.py", "alpha_wardrobe.py", "alpha_accents.py"):
    exec(open(os.path.join(R, "tools", "blender", name), encoding="utf-8").read())

ART = os.path.join(R, "Assets", "Art")
TEX = os.path.join(ART, "Textures", "Kit")
t0 = time.time()
log = io.StringIO()

def step(msg):
    print(f"[{round(time.time() - t0)} с] {msg}", flush=True)

try:
    bpy.ops.wm.read_homefile(use_empty=True)
    with contextlib.redirect_stdout(log):
        prepare_textures(ART)
        accent_textures(ART)
    step("текстури готові")

    rigs = {}
    for g in ("m", "f"):
        with contextlib.redirect_stdout(log):
            rig, body, items = build_kit(g)
            rig.location = (0, 0 if g == "m" else 3, 0)
            library_wardrobe(rig, g, items)
            kit_hair(rig, g)
            neutral_hair_textures(rig, TEX)
            _tint_skin_materials([o for o in rig.children if o.get("kit_kind") == "skin" or o.name.endswith("low-poly")],
                                 (1, 1, 1), f"{g}_default", TEX)
            accents(rig, body)
            moved = layer_clothes(rig)
        rigs[g] = rig
        step(f"шари {g}: " + ", ".join(f"{k} {n}" for k, n in sorted(moved.items()) if n))
        step(f"набір {g}: {len([o for o in rig.children if o.get('kit_part')])} частин")

    bodies = []
    for g in ("m", "f"):
        for i, c in enumerate(CULTURES):
            with contextlib.redirect_stdout(log):
                r, zones = culture_body(g, c, TEX)
                r.location = (1.2 * (i + 1), 6 if g == "m" else 9, 0)
            bodies.append(r)
    step(f"тіла культур: {len(bodies)}")

    # Текстури поза репозиторієм (бібліотека MPFB) — у Textures/Kit, щоб FBX посилався відносно. Кожна —
    # з рядком походження й ліцензії в SOURCES.txt; не CC0 — збій збирання (репозиторій публічний).
    sources = []
    for img in bpy.data.images:
        p = bpy.path.abspath(img.filepath) if img.filepath else ""
        if p and os.path.exists(p) and os.path.normcase(ART) not in os.path.normcase(p):
            folder = os.path.dirname(p)
            kind, name = os.path.basename(os.path.dirname(folder)), os.path.basename(folder)
            if os.path.normcase(MPFB_DATA) in os.path.normcase(p):
                if os.path.basename(folder) == "textures":   # skins/<name>/textures/*.png
                    kind, name = os.path.basename(os.path.dirname(os.path.dirname(folder))), os.path.basename(os.path.dirname(folder))
                lic = asset_license(kind, name)
            else:
                lic = "невідома (поза бібліотекою MPFB)"
            if lic != "CC0":
                raise PermissionError(f"текстура {p}: {lic}")
            sources.append(f"{os.path.basename(p)}\tMakeHuman/MPFB {kind}/{name}\t{lic}")
            dst = os.path.join(TEX, os.path.basename(p))
            if not os.path.exists(dst):
                shutil.copyfile(p, dst)
            img.filepath = dst
    open(os.path.join(TEX, "SOURCES.txt"), "w", encoding="utf-8", newline="\n").write(
        "# Текстури з бібліотеки MakeHuman/MPFB у наборі: файл, асет, ліцензія (генерує tools/blender/build_kit_all.py).\n" +
        "\n".join(sorted(set(sources))) + "\n")

    with contextlib.redirect_stdout(log):
        paths = export_kit(rigs["m"], ART, extra_rigs=[rigs["f"]] + bodies)
    step(f"експорт: {len(paths)} FBX")

    out = os.path.join(ART, "Characters", "Kit", "kit_manifest.txt")
    lines = ["# Частини модульного набору у FBX (генерує tools/blender/build_kit_all.py). <стать>:<ключ частини>"]
    for g in ("m", "f"):
        parts = sorted({o.get("kit_part") for o in rigs[g].children if o.get("kit_part") and not o.get("preview")})
        lines += [f"{g}:{p}" for p in parts]
    for g in ("m", "f"):
        for c in CULTURES:
            lines.append(f"{g}:culture:{c}")
    open(out, "w", encoding="utf-8", newline="\n").write("\n".join(lines) + "\n")
    step(f"маніфест: {len(lines) - 1} рядків")
except Exception:
    print(traceback.format_exc(), flush=True)
finally:
    fails = [l for l in log.getvalue().splitlines() if "fail" in l or "empty" in l or "нема" in l]
    print("ЗБІЙ/ПРОПУСК:", fails[:30], flush=True)
