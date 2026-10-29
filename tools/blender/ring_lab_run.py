# Фоновий прогін лабораторії (довгі розрахунки рвали з'єднання MCP):
#   blender -b Logs/ring_anims/lab_scene.blend --python tools/blender/ring_lab_run.py -- <прийом>
# Розв'язує прийом за картою, проганяє перевірки (ворота 3), пише звіт і кліп у lab_clips.
import sys, os, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ring_lab, ring_moves_lab as M
name = sys.argv[sys.argv.index("--") + 1]
ring_lab.setup()
if name.startswith("Chain_"):                        # ланки чейну: свої пози й перевірка (ring_chain_lab)
    import ring_chain_lab as C
    t0 = time.time()
    log = {"Chain_Headlock": C.headlock_frames, "Chain_ArmWringer": C.armwringer_frames,
           "Chain_Hammerlock": C.hammerlock_frames, "Chain_Waistlock": C.waistlock_frames,
           "Chain_FrontFacelock": C.facelock_frames, "Chain_Wristlock": C.wristlock_frames,
           "Chain_ShoveOff": C.shove_frames}[name]()
    bad = [(f, p) for f, p in log if p]
    print(f"[лабораторія] {name}: {round(time.time() - t0)} с, кадрів з проникненням {len(bad)}", flush=True)
    for f, p in bad:
        print(f"  кадр {f}: {p}", flush=True)
    print(ring_lab.save_clip(name), flush=True)
    sys.exit(0)
mv = ring_lab.load_map("Lockup" if name.startswith("Lockup") else name)
M.MOVE_MAP = mv
if name.startswith("Lockup"):
    print("[лабораторія] крок до суперника:", M.calibrate_reach(mv), flush=True)
t0 = time.time()
if name.startswith("Lockup"):
    M.lockup_var_frames(name)
else:
    {"Bodyslam": M.slam_frames}[name]()
res = ring_lab.check_clip(mv)
print(f"[лабораторія] {name}: {round(time.time() - t0)} с, кадрів з провалами {len(res)}", flush=True)
print(ring_lab.save_clip(name), flush=True)
