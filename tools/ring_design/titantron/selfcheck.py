"""Самоперевірка генератора екранів виходу (Python-бік; C#-бік — TitantronAssetsGuardTests).

    cd tools && python -m ring_design.titantron.selfcheck      → код виходу 0 = усе гаразд
"""
import json
import sys

from . import bake, compose, render, timeline as T
from .common import ALPHA, ART, BRAND, COL, KITS, POOLS, ROOT, SPRITES, TIERS, allowed_alphas, slot_map, token_of, utf8

ERR = []


def err(msg):
    ERR.append(msg)


def near(v, allowed):
    return any(abs(v - a) < 1e-4 for a in allowed)


def tints_of(layer):
    out = []
    if layer.get("tint"):
        out.append(layer["tint"])
    sp = layer.get("text")
    if sp:
        out.append(sp.get("fill", "$paper"))
        if sp.get("shadow"):
            out.append(sp["shadow"][0])
        if sp.get("outline"):
            out.append(sp["outline"][0])
    return out


def check_pool(name, pool, sprites_idx):
    g, role, tier = pool["gimmick"], pool["role"], pool["tier"]
    tcfg = KITS["tiers"][tier]
    alphas = allowed_alphas()
    if len(pool["variants"]) != KITS["variants"]:
        err("%s: варіантів %d, треба %d" % (name, len(pool["variants"]), KITS["variants"]))
    for tl in pool["variants"]:
        tag = "%s v%d" % (name, tl["variant"])
        if len(tl["layers"]) > tcfg["maxLayers"]:
            err("%s: шарів %d > бюджет щабля %d" % (tag, len(tl["layers"]), tcfg["maxLayers"]))
        if tl["hitBeat"] != tcfg["hitBeat"] or tl["fps"] != tcfg["fps"] or tl["bpm"] != KITS["gimmicks"][g]["bpm"]:
            err("%s: hitBeat/fps/bpm не збігаються з kits.json" % tag)
        if not any(e["t"] == "hit" and e["b"] == tl["hitBeat"] for e in tl["events"]):
            err("%s: немає події hit на hitBeat" % tag)
        if (tl["hitBeat"] * 2) % 1 != 0:
            err("%s: hitBeat не на сітці півбітів" % tag)
        if tl["introBeats"] * 60.0 / tl["bpm"] > 5.0:
            err("%s: вступ довший за 5 с" % tag)
        # безпека (WCAG 2.3.1): рівно один шар-спалах, не яскравіший за ghost; блимання не частіше 3 за секунду
        flashes = [l for l in tl["layers"] if l["id"].startswith("flash")]
        if len(flashes) != 1:
            err("%s: шарів-спалахів %d, має бути 1" % (tag, len(flashes)))
        for l in flashes:
            for k in l.get("tr", {}).get("a", []):
                if k[1] > ALPHA["ghost"] + 1e-6:
                    err("%s: спалах яскравіший за ghost (%s)" % (tag, k[1]))
        for l in tl["layers"]:
            m = l.get("mod", {})
            if "blink" in m and tl["bpm"] / 60.0 > 3.0:
                err("%s: %s блимає частіше 3 разів за секунду" % (tag, l["id"]))
            if "flick" in m and tl["bpm"] / 60.0 / m["flick"][0] > 3.0:
                err("%s: %s спалахує частіше 3 разів за секунду" % (tag, l["id"]))
        ids = set()
        for lay in tl["layers"]:
            if lay["id"] in ids:
                err("%s: дубль id шару %s" % (tag, lay["id"]))
            ids.add(lay["id"])
            if lay["k"] == "sprite":
                sid = lay["sprite"]
                if not (sid in sprites_idx or sid.startswith("brand_")):
                    err("%s: невідомий спрайт %s" % (tag, sid))
            if lay["k"] == "text":
                sp = lay["text"]
                if sp["slot"] not in ("nick", "full", "tag"):
                    err("%s: невідомий текстовий слот %s" % (tag, sp["slot"]))
                if sp["font"] not in KITS["fonts"]:
                    err("%s: невідомий шрифт %s" % (tag, sp["font"]))
                if sp["size"] * 512 < 30:
                    err("%s: кегль %s надто малий для екрана" % (tag, sp["size"]))
            # кольори: слоти, що розкриваються у ВСІХ ролях і щаблях; золото лише в гравця
            for tint in tints_of(lay):
                for rl in KITS["roles"]:
                    for tr_ in TIERS:
                        sm = slot_map(rl, tr_, False)
                        tok = token_of(tint, sm)
                        if tok not in COL:
                            err("%s: тінт %s → невідомий токен %s" % (tag, tint, tok))
                        elif tok == "gold":
                            err("%s: золото в NPC (%s, %s, %s)" % (tag, lay["id"], rl, tr_))
            if "a" in lay and not near(lay["a"], alphas):
                err("%s: %s: статична прозорість %s не з кроків" % (tag, lay["id"], lay["a"]))
            for prop, keys in lay.get("tr", {}).items():
                last = -1
                for k in keys:
                    if k[0] < last:
                        err("%s: %s.%s: ключі не за зростанням біта" % (tag, lay["id"], prop))
                    last = k[0]
                    if len(k) > 2 and k[2] not in KITS["easing"]:
                        err("%s: %s.%s: невідома плавність %s" % (tag, lay["id"], prop, k[2]))
                    if prop == "a" and not near(k[1], alphas):
                        err("%s: %s: ключ прозорості %s не з кроків" % (tag, lay["id"], k[1]))
        # гравець: золото лише через $accent і $flash
        for rl in KITS["roles"]:
            sm = slot_map(rl, tl["tier"], True)
            for slot, tok in sm.items():
                if tok == "gold" and slot not in ("$accent", "$flash"):
                    err("гравець: золото в слоті %s" % slot)


def check_names():
    """Найдовші імена мають вміщатись без сильного стиснення (fit)."""
    f = KITS["fonts"]["display900"]
    worst = {}
    for text in ("СТАЛЕВИЙ", "ҐРИНДЖОЛИ", "КОЛІБРІ", "ЄВПРАКСІЯ", "ВОЛОДИМИР", "ВОЛОДИМИР ВЕЛИКИЙ", "ПЕРЕМОЖЕЦЬ ЄНОТ"):
        spec = {"font": "display900", "slant": 12, "tracking": 0.01}
        im = render._text_raw(text, "display900", int(0.33 * 512 * 2), (255, 255, 255), None, 0, 0, None, 0, 12, 0.01)
        w = im.width * 0.5 / 512.0  # у одиницях висоти кадру
        scale = min(1.0, 1.5 / w)
        worst[text] = (round(w, 2), round(scale, 2))
        need = 0.95 if len(text) <= 9 else 0.75 if len(text) <= 13 else 0.55
        if scale < need:
            err("ім'я «%s»: після підгонки лишається %.0f %% кегля (< %.0f %%)" % (text, scale * 100, need * 100))
    return worst


def main():
    utf8()
    sprites_idx = json.loads((ART / "sprites.json").read_text(encoding="utf-8"))
    for sid in sprites_idx:
        if not (SPRITES / (sid + ".png")).exists():
            err("немає файла спрайта " + sid)
    for p in ("emblem_seal_1024.png", "rope_frame_1040.png"):
        if not (BRAND / p).exists():
            err("немає бренд-спрайта " + p)
    for key, path in KITS["fonts"].items():
        if not (ROOT / path).exists():
            err("немає шрифта %s: %s" % (key, path))
    fresh = bake.pools()
    for name, pool in fresh.items():
        check_pool(name, pool, sprites_idx)
        f = POOLS / name
        if not f.exists():
            err("пул не запечено: " + name)
        elif f.read_text(encoding="utf-8") != bake.dump(pool):
            err("пул застарів (запустити bake): " + name)
        # детермінізм
        again = bake.dump({"x": compose.compose(pool["gimmick"], pool["role"], pool["tier"], 0)})
        once = bake.dump({"x": pool["variants"][0]})
        if again != once:
            err("недетермінований ролик " + name)
    # паритет плавності: еталонні значення з golden.json
    gold = json.loads((ART / "golden.json").read_text(encoding="utf-8"))
    for nm, pts in gold["ease"].items():
        for t, v in pts:
            if abs(T.EASE[nm](t) - v) > 1e-4:
                err("golden: плавність %s(%s) розходиться" % (nm, t))
    worst = check_names()
    print("пулів: %d; шарів у найбільшому ролику: %d" % (len(fresh), max(len(tl["layers"]) for p in fresh.values() for tl in p["variants"])))
    print("підгонка імен (ширина, масштаб):", worst)
    if ERR:
        print("ПОМИЛОК: %d" % len(ERR))
        for e in ERR[:40]:
            print(" -", e)
        sys.exit(1)
    print("selfcheck: усе гаразд")


if __name__ == "__main__":
    main()
