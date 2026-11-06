"""Спільне для генератора екранів виходу: шляхи, токени, kits.json, слоти кольорів.

Генератор не вигадує кольорів: усе — імена токенів `docs/ring/design/brand_tokens.json` (власник — арт-директор),
слоти `$role/$roleText/…` розкриваються за `kits.json`.
"""
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
TOKENS = json.loads((ROOT / "docs/ring/design/brand_tokens.json").read_text(encoding="utf-8"))
KITS = json.loads((ROOT / "docs/ring/design/titantron/kits.json").read_text(encoding="utf-8"))
ART = ROOT / "Assets/Art/Ring/Titantron"
SPRITES = ART / "sprites"
POOLS = ART / "pools"
BRAND = ROOT / "Assets/Art/Ring/Brand"
PREVIEW = ROOT / "docs/ring/design/titantron/preview"
W, H = KITS["frame"]["w"], KITS["frame"]["h"]
TIERS = KITS["tiersOrder"]


def utf8():
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except Exception:  # noqa: BLE001
        pass


def hex_rgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


COL = {k: hex_rgb(v) for k, v in TOKENS["colors"].items()}
ALPHA = {k: TOKENS["alpha"][v] for k, v in KITS["alpha"].items()}  # ghost / dim / mid


def slot_map(role, tier="arena", player=False):
    """Слот → ім'я токена. Золото — лише акцент і спалах гравця; щабель «спортзал» — лише тло, колір ролі й папір."""
    m = dict(KITS["slots"][role])
    if player:
        m.update(KITS["playerSlots"])
    if KITS["tiers"][tier]["mono"]:
        m.update(KITS["monoSlots"])
    return m


def token_of(tint, slots):
    return slots.get(tint, tint) if tint.startswith("$") else tint


def resolve(tint, slots):
    return COL[token_of(tint, slots)]


def allowed_alphas():
    return sorted({0.0, 1.0} | set(ALPHA.values()))
