"""Рендер ролика в кадри (Pillow): складає шари з перетвореннями так, як це робитиме програвач UI Toolkit
(translate/scale/rotate/opacity + кольорова заливка з токена). Для перегляду очима й еталонних знімків.
"""
import math
from functools import lru_cache

from PIL import Image, ImageDraw, ImageFont

from . import timeline as T
from .common import BRAND, KITS, ROOT, SPRITES, W, H, resolve


# ----------------------------------------------------------------- текст

@lru_cache(maxsize=None)
def _font(key, px):
    return ImageFont.truetype(str(ROOT / KITS["fonts"][key]), px)


@lru_cache(maxsize=1024)
def _text_raw(text, fkey, px, fill, shadow, sdx, sdy, outline, ow, slant, tracking):
    f = _font(fkey, px)
    adv = [f.getlength(ch) for ch in text]
    track = tracking * px
    total = sum(adv) + track * max(0, len(text) - 1)
    asc, desc = f.getmetrics()
    pad = int(px * 0.5 + ow * 2 + abs(sdx) + abs(sdy)) + 8
    k = math.tan(math.radians(slant))
    wc = int(total + 2 * pad + abs(k) * (asc + desc))
    hc = asc + desc + 2 * pad
    im = Image.new("RGBA", (wc, hc), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)

    def run(dx, dy, col, stroke=None, sw=0):
        x = pad
        for ch, a in zip(text, adv):
            kw = {"stroke_width": sw, "stroke_fill": stroke + (255,)} if stroke is not None and sw > 0 else {}
            d.text((x + dx, pad + dy), ch, font=f, fill=col + (255,), **kw)
            x += a + track

    if shadow is not None:
        run(sdx, sdy, shadow, outline, ow)
    run(0, 0, fill, outline, ow)
    if slant:
        y0 = hc * 0.5
        im = im.transform(im.size, Image.AFFINE, (1, k, -k * y0, 0, 1, 0), resample=Image.BICUBIC)
    bb = im.getbbox()
    return im.crop(bb) if bb else im


def text_stamp(text, spec, size_px, fit_px, slots, tier):
    """Кольоровий спрайт тексту. size_px — кегль, fit_px — найбільша ширина (0 = без обмеження). Малюється ×2 й зменшується."""
    fkey = spec.get("font", "display900")
    if fkey.startswith("display") and tier and KITS["tiers"][tier]["handFont"] and spec.get("hand"):
        fkey = "hand"
        size_px = size_px * 1.35
    fill = resolve(spec.get("fill", "$paper"), slots)
    sh = spec.get("shadow")
    shadow, sdx, sdy = (resolve(sh[0], slots), sh[1] * H * 2, sh[2] * H * 2) if sh else (None, 0, 0)
    ol = spec.get("outline")
    outline, ow = (resolve(ol[0], slots), int(ol[1] * H * 2)) if ol else (None, 0)
    args = (text, fkey, int(size_px * 2), fill, shadow, round(sdx), round(sdy), outline, ow,
            spec.get("slant", 0), spec.get("tracking", 0.0))
    im = _text_raw(*args)
    sc = 0.5
    if fit_px and im.width * sc > fit_px:
        sc = fit_px / im.width
    return im.resize((max(1, round(im.width * sc)), max(1, round(im.height * sc))), Image.LANCZOS)


# ----------------------------------------------------------------- спрайти

@lru_cache(maxsize=None)
def _sprite(sid):
    if sid.startswith("brand_"):
        name = {"brand_seal": "emblem_seal_1024.png", "brand_rope": "rope_frame_1040.png"}[sid]
        return Image.open(BRAND / name).convert("RGBA")
    return Image.open(SPRITES / (sid + ".png")).convert("RGBA")


def _tint(img, rgb, a):
    solid = Image.new("RGBA", img.size, rgb + (255,))
    alpha = img.getchannel("A").point(lambda v: int(v * a))
    solid.putalpha(alpha)
    return solid


@lru_cache(maxsize=256)
def _tiled(sid, tw, th, aw, ah):
    """Великий полотно з плиток розміром tw×th, на запас у одну плитку з кожного боку (для зсуву)."""
    tile = _sprite(sid).resize((tw, th), Image.BICUBIC)
    cv = Image.new("RGBA", (aw + tw * 2, ah + th * 2), (0, 0, 0, 0))
    for yy in range(0, ah + th * 2, th):
        for xx in range(0, aw + tw * 2, tw):
            cv.paste(tile, (xx, yy))
    return cv


def _poly_mask(pw, ph, kind, slant):
    ss = 2
    m = Image.new("L", (pw * ss, ph * ss), 0)
    d = ImageDraw.Draw(m)
    if kind == "rect":
        d.rectangle([0, 0, pw * ss, ph * ss], fill=255)
    elif kind == "disc":
        d.ellipse([0, 0, pw * ss - 1, ph * ss - 1], fill=255)
    return m.resize((pw, ph), Image.LANCZOS)


def _plate_mask(w_px, h_px, slant):
    shear = math.tan(math.radians(slant)) * h_px
    bw = int(w_px + abs(shear))
    ss = 2
    m = Image.new("L", (bw * ss, h_px * ss), 0)
    s = shear * ss
    if shear >= 0:
        pts = [(s, 0), (s + w_px * ss, 0), (w_px * ss, h_px * ss), (0, h_px * ss)]
    else:
        pts = [(0, 0), (w_px * ss, 0), (w_px * ss - s, h_px * ss), (-s, h_px * ss)]
    ImageDraw.Draw(m).polygon(pts, fill=255)
    return m.resize((bw, h_px), Image.LANCZOS)


def _ring_mask(pw, ph, thick_px):
    ss = 2
    m = Image.new("L", (pw * ss, ph * ss), 0)
    d = ImageDraw.Draw(m)
    d.ellipse([0, 0, pw * ss - 1, ph * ss - 1], fill=255)
    t = int(thick_px * ss)
    if pw * ss - 2 * t > 2 and ph * ss - 2 * t > 2:
        d.ellipse([t, t, pw * ss - 1 - t, ph * ss - 1 - t], fill=0)
    return m.resize((pw, ph), Image.LANCZOS)


# ----------------------------------------------------------------- шар → штамп

def _base_stamp(layer, st, slots, texts, tier, hpx):
    """RGBA штамп шару ДО обертання (з клипом), розмір у пікселях кадру."""
    k = layer["k"]
    a = st["a"]
    sc = st["s"]
    if k in ("rect", "plate", "disc", "ring"):
        pw = round(st["w"] * hpx * st["sx"] * sc)
        ph = round(st["h"] * hpx * st["sy"] * sc)
        if pw < 2 or ph < 2:
            return None
        rgb = resolve(layer["tint"], slots)
        if k == "rect":
            mask = Image.new("L", (pw, ph), 255)
        elif k == "plate":
            mask = _plate_mask(pw, ph, layer.get("slant", 12))
        elif k == "disc":
            mask = _poly_mask(pw, ph, "disc", 0)
        else:
            mask = _ring_mask(pw, ph, layer.get("t", 0.03) * hpx * sc)
        im = Image.new("RGBA", mask.size, rgb + (255,))
        im.putalpha(mask.point(lambda v: int(v * a)))
    elif k == "sprite":
        sid = layer["sprite"]
        pw = max(1, round(st["w"] * hpx * st["sx"] * sc))
        ph = max(1, round(st["h"] * hpx * st["sy"] * sc))
        if layer.get("tile"):
            th = max(2, round(layer["tile"] * hpx * sc))
            src = _sprite(sid)
            tw = max(2, round(th * src.width / src.height))
            cv = _tiled(sid, tw, th, pw, ph)
            ox = int(st["ox"] * hpx) % tw
            oy = int(st["oy"] * hpx) % th
            crop = cv.crop((tw + ox, th + oy, tw + ox + pw, th + oy + ph))
            im = _tint(crop, resolve(layer["tint"], slots), a) if layer.get("tint") else crop
        else:
            src = _sprite(sid).resize((pw, ph), Image.BICUBIC)
            im = _tint(src, resolve(layer["tint"], slots), a) if layer.get("tint") else src
            if not layer.get("tint") and a < 1:
                im.putalpha(im.getchannel("A").point(lambda v: int(v * a)))
    elif k == "text":
        spec = layer["text"]
        txt = texts.get(spec["slot"], "")
        if not txt:
            return None
        txt = txt.upper()
        im = text_stamp(txt, spec, spec["size"] * hpx * sc, spec.get("fit", 0) * hpx, slots, tier)
        if a < 1:
            im.putalpha(im.getchannel("A").point(lambda v: int(v * a)))
    else:
        raise ValueError("невідомий вид шару: " + k)
    clip = st["clip"]
    if clip < 0.999:
        cw = int(im.width * max(0.0, clip))
        al = im.getchannel("A")
        keep = Image.new("L", im.size, 0)
        if cw > 0:
            keep.paste(255, (0, 0, cw, im.height))
        from PIL import ImageChops
        im.putalpha(ImageChops.multiply(al, keep))
    return im


def _paste(base, im, cx, cy):
    left, top = round(cx - im.width / 2), round(cy - im.height / 2)
    x0, y0 = max(0, left), max(0, top)
    x1, y1 = min(base.width, left + im.width), min(base.height, top + im.height)
    if x1 <= x0 or y1 <= y0:
        return
    sub = im.crop((x0 - left, y0 - top, x1 - left, y1 - top))
    base.alpha_composite(sub, (x0, y0))


def render(tl, beat, texts, slots, tier, size=(W, H)):
    """Один кадр: RGB-зображення розміру size."""
    wpx, hpx = size
    base = Image.new("RGBA", size, (0, 0, 0, 255))
    shx, shy = T.shake_offset(tl.get("events", []), beat)
    for layer in tl["layers"]:
        st = T.state(layer, beat)
        if st["a"] <= 0.002:
            continue
        im = _base_stamp(layer, st, slots, texts, tier, hpx)
        if im is None:
            continue
        if layer.get("flipx"):
            im = im.transpose(Image.FLIP_LEFT_RIGHT)
        rot = st["rot"]
        if abs(rot) > 0.01:
            im = im.rotate(-rot, expand=True, resample=Image.BICUBIC)
        sx, sy = (0, 0) if layer.get("fixed") else (shx, shy)
        x, y = st["x"] + sx, st["y"] + sy
        if layer["k"] != "sprite" or not layer.get("tile"):
            x += st["ox"]
            y += st["oy"]
        _paste(base, im, wpx / 2 + x * hpx, hpx / 2 + y * hpx)
    return base.convert("RGB")


def quantize(beat, bpm, fps):
    """Ступінчастий показ: час округлюється вниз до кадру щабля (проєктор 6 к/с, ТВ 12 к/с, LED 30 к/с)."""
    sec = beat * 60.0 / bpm
    return math.floor(sec * fps) / fps * bpm / 60.0
