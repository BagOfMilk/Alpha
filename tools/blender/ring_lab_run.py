# Фоновий прогін лабораторії (довгі розрахунки рвали з'єднання MCP):
#   blender -b Logs/ring_anims/lab_scene.blend --python tools/blender/ring_lab_run.py -- <прийом>
# Розв'язує прийом за картою, проганяє перевірки (ворота 3), пише звіт і кліп у lab_clips.
import sys, os, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ring_lab, ring_moves_lab as M
name = sys.argv[sys.argv.index("--") + 1]
ring_lab.setup()
mv = ring_lab.load_map(name)
M.MOVE_MAP = mv
if name == "Lockup":
    print("[лабораторія] крок до суперника:", M.calibrate_reach(mv), flush=True)
t0 = time.time()
{"Lockup": M.lockup_frames, "Bodyslam": M.slam_frames}[name]()
res = ring_lab.check_clip(mv)
print(f"[лабораторія] {name}: {round(time.time() - t0)} с, кадрів з провалами {len(res)}", flush=True)
print(ring_lab.save_clip(name), flush=True)
