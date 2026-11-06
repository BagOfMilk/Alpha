"""Запікання пулів роликів для гри: гімік × роль × щабель × варіант → Assets/Art/Ring/Titantron/pools/*.json,
manifest.json (що де лежить, формули) і golden.json (еталонні значення плавності й станів шарів для паритету C#-програвача).

    cd tools && python -m ring_design.titantron.bake          (спершу motifs: спрайти)
"""
import json
import math

from . import compose, timeline as T
from .common import ART, KITS, POOLS, TIERS, utf8


def dump(obj):
    return json.dumps(obj, ensure_ascii=False, separators=(",", ":"))


def fnv1a32(text):
    h = 0x811C9DC5
    for b in text.encode("utf-8"):
        h = ((h ^ b) * 0x01000193) & 0xFFFFFFFF
    return h


def variant_for(name, seed, n=None):
    """Вибір варіанта: FNV-1a 32 біти від UTF-8 рядка «ім'я|seed», за модулем n."""
    return fnv1a32("%s|%s" % (name, seed)) % (n or KITS["variants"])


def pools():
    out = {}
    for g in KITS["gimmicks"]:
        for role in KITS["roles"]:
            for tier in TIERS:
                out["%s_%s_%s.json" % (g, role, tier)] = {
                    "v": 1, "gimmick": g, "role": role, "tier": tier,
                    "variants": [compose.compose(g, role, tier, v) for v in range(KITS["variants"])]}
    return out


def used_sprites(pool_map):
    ids = set()
    for pool in pool_map.values():
        for tl in pool["variants"]:
            for lay in tl["layers"]:
                if lay["k"] == "sprite":
                    ids.add(lay["sprite"])
    return sorted(ids)


def golden(pool_map):
    g = {"ease": {}, "states": [], "variantFor": []}
    for name in KITS["easing"]:
        g["ease"][name] = [[t, round(T.EASE[name](t), 5)] for t in (0.0, 0.1, 0.25, 0.5, 0.75, 0.9, 1.0)]
    picks = [("monster_villain_arena.json", 0), ("flyer_hero_hall.json", 1), ("technician_rebel_arena.json", 2),
             ("showman_hero_gym.json", 3)]
    for name, v in picks:
        tl = pool_map[name]["variants"][v]
        H = tl["hitBeat"]
        for b in (0.0, H - 0.1, H + 0.05, H + 0.2, H + 1.2, H + 9.37):
            st = {}
            for lay in tl["layers"]:
                s = T.state(lay, b)
                st[lay["id"]] = {k: round(val, 4) for k, val in s.items()}
            g["states"].append({"pool": name, "variant": v, "beat": round(b, 3), "layers": st})
    for nm, seed in (("Сталевий", 0), ("Колібрі", 0), ("Вдова", 1), ("Новачок", 7), ("", 0)):
        g["variantFor"].append({"name": nm, "seed": seed, "variant": variant_for(nm, seed), "hash": fnv1a32("%s|%s" % (nm, seed))})
    return g


def bake():
    POOLS.mkdir(parents=True, exist_ok=True)
    for f in POOLS.glob("*.json"):
        f.unlink()
    pm = pools()
    total = 0
    for name, pool in pm.items():
        txt = dump(pool)
        (POOLS / name).write_text(txt, encoding="utf-8")
        total += len(txt.encode("utf-8"))
    manifest = {
        "version": 1,
        "frame": KITS["frame"],
        "pools": sorted(pm),
        "sprites": used_sprites(pm),
        "textSlots": ["nick", "full", "tag"],
        "easing": KITS["easing"],
        "mods": ["pulse", "spin", "sway", "rock", "drift", "blink", "flick", "sweep"],
        "tiers": {t: {"fps": KITS["tiers"][t]["fps"], "hitBeat": KITS["tiers"][t]["hitBeat"]} for t in TIERS},
        "gimmicks": KITS["gimmicks"],
        "variants": KITS["variants"],
        "variantPick": "FNV-1a 32 біти від UTF-8 рядка «ім'я|seed», variant = hash mod variants",
    }
    (ART / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=1), encoding="utf-8")
    (ART / "golden.json").write_text(dump(golden(pm)), encoding="utf-8")
    return len(pm), total


if __name__ == "__main__":
    utf8()
    n, total = bake()
    print("пулів: %d, разом %.1f КБ (JSON без стиснення)" % (n, total / 1024))
