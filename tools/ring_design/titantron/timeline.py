"""Модель ролика: шари з доріжками ключових кадрів у бітах (формат — B07). Чиста математика, без Pillow.

Координати: одиниця = висота кадру; x вправо, y вниз, початок — центр кадру (кадр 2 × 1 одиниці).
Ключ: [біт, значення, плавність]; плавність — для відрізка, що ПРИХОДИТЬ до цього ключа.
Формули плавності — еталон для C#-програвача (паритет перевіряє `selfcheck.py`).
"""
import math


def _o2(t): return 1 - (1 - t) ** 2
def _o3(t): return 1 - (1 - t) ** 3
def _i2(t): return t * t
def _i3(t): return t ** 3
def _io2(t): return 2 * t * t if t < 0.5 else 1 - (-2 * t + 2) ** 2 / 2


def _ob(t, c1=1.70158):
    c3 = c1 + 1
    return 1 + c3 * (t - 1) ** 3 + c1 * (t - 1) ** 2


EASE = {"l": lambda t: t, "o2": _o2, "o3": _o3, "i2": _i2, "i3": _i3, "io2": _io2, "ob": _ob, "s": lambda t: 0.0}


def track(keys, b):
    if b <= keys[0][0]:
        return keys[0][1]
    for i in range(1, len(keys)):
        b0, v0 = keys[i - 1][0], keys[i - 1][1]
        b1, v1 = keys[i][0], keys[i][1]
        if b < b1:
            e = keys[i][2] if len(keys[i]) > 2 else "l"
            t = (b - b0) / (b1 - b0) if b1 > b0 else 1.0
            return v0 + (v1 - v0) * EASE[e](t)
    return keys[-1][1]


def state(layer, b):
    """Стан шару в біті b (після доріжок і модифікаторів)."""
    st = {"x": layer.get("x", 0.0), "y": layer.get("y", 0.0), "s": layer.get("s", 1.0),
          "sx": layer.get("sx", 1.0), "sy": layer.get("sy", 1.0), "rot": layer.get("rot", 0.0),
          "a": layer.get("a", 1.0), "clip": layer.get("clip", 1.0), "w": layer.get("w", 1.0), "h": layer.get("h", 1.0),
          "ox": 0.0, "oy": 0.0}
    for prop, keys in layer.get("tr", {}).items():
        st[prop] = track(keys, b)
    m = layer.get("mod")
    if not m:
        return st
    if "pulse" in m:  # [кожні, амплітуда, спад, старт]
        every, amp, decay, start = m["pulse"]
        if b >= start:
            st["s"] *= 1 + amp * math.exp(-((b - start) % every) / decay)
    if "spin" in m:  # [град/біт, старт]
        rate, start = m["spin"]
        if b > start:
            st["rot"] += rate * (b - start)
    if "sway" in m:  # [ax, ay, період, фаза]
        ax, ay, per, ph = m["sway"]
        k = math.sin(2 * math.pi * (b / per + ph))
        st["x"] += ax * k
        st["y"] += ay * k
    if "rock" in m:  # [градуси, період, фаза]
        deg, per, ph = m["rock"]
        st["rot"] += deg * math.sin(2 * math.pi * (b / per + ph))
    if "drift" in m:  # [vx, vy, старт] — для плиток це зсув текстури (ox, oy), інакше — x, y
        vx, vy, start = m["drift"]
        if b > start:
            st["ox"] += vx * (b - start)
            st["oy"] += vy * (b - start)
    if "blink" in m:  # [парність, старт]
        par, start = m["blink"]
        if b >= start and int(math.floor(b)) % 2 != par:
            st["a"] = 0.0
    if "flick" in m:  # [кожні, тривалість, старт]: видно лише на початку кожного періоду
        every, dur, start = m["flick"]
        if b < start or ((b - start) % every) >= dur:
            st["a"] = 0.0
    if "sweep" in m:  # [період, старт, x0, x1]: пролітає один раз за період
        per, start, x0, x1 = m["sweep"]
        if b < start:
            st["a"] = 0.0
        else:
            st["x"] = x0 + (x1 - x0) * (((b - start) % per) / per)
    return st


def shake_offset(events, b):
    """Глобальне тремтіння: детермінований «шум», лінійний спад."""
    dx = dy = 0.0
    for e in events:
        if e["t"] == "shake" and e["b"] <= b < e["b"] + e["dur"]:
            k = e["amp"] * (1 - (b - e["b"]) / e["dur"])
            dx += k * math.sin(b * 83.1) * math.cos(b * 41.7)
            dy += k * math.cos(b * 67.3) * math.sin(b * 29.9)
    return dx, dy


def intro_end(tl):
    """Біт, після якого ключові кадри скінчились (далі лише модифікатори)."""
    last = 0.0
    for lay in tl["layers"]:
        for keys in lay.get("tr", {}).values():
            last = max(last, keys[-1][0])
    return round(last, 3)
