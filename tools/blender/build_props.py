# Перезбирання окремих моделей реквізиту з alpha_kit.py одним запуском Blender (без живої сесії).
#
#   "C:\Program Files\Blender Foundation\Blender 4.5\blender.exe" -b --python tools/blender/build_props.py -- <корінь репо> buildings
#   (або prop_tent — лише намет)
#
# Матеріали в свіжій сцені створюються з іменами Poly Haven / набору (finish → _material): Unity бере текстуру
# за назвою матеріалу (ArtImportSettings), тож .blend із завантаженими текстурами не потрібен.
import bpy, os, sys

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
R = args[0] if args else os.getcwd()
names = args[1:] or ["prop_tent"]
exec(open(os.path.join(R, "tools", "blender", "alpha_kit.py"), encoding="utf-8").read())

bpy.ops.wm.read_homefile(use_empty=True)
# «buildings» — усі будівлі й реквізит (build_all; дерева — ні: їм потрібні текстури хвої в сцені).
builders = {"prop_tent": lambda: tent(), "buildings": lambda: build_all()}
for n in names:
    builders[n]()
paths = export_all(os.path.join(R, "Assets", "Art"))
print("ЕКСПОРТ:", paths, flush=True)
