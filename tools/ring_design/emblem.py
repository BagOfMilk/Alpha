#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Знак «Рингу»: єдина геометрія печатки й канатної рамки (Статут вигляду, ARENA-03). Без слів.

Печатка: золоте кільце, чорне поле, три золоті канати за зіркою, кармінна восьмипроменева зірка з чорним обідком, золота
цятка. БЕЗ планки, що розрізала старий знак і читалась як «в'їзд заборонено». Канатна рамка: дві стійки з подушками й три
канати; «РИНГ» у ній — ЖИВИЙ текст (шрифт display, ВЕЛИКІ), у текстурі літер немає (правило власника 27.10.2026).

Один модуль — три виходи: PIL-картинка з суперсемплінгом (для текстур арен і значків), SVG (майстер у docs) і PNG-набір.
Кольори — лише з токенів (tools/blender/ring_brand.py).

  python tools/ring_design/emblem.py <корінь репо>      # експорт SVG-майстрів і PNG-значків
"""
import math
import os
import sys

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "..", "blender"))
import ring_brand as B  # noqa: E402

from emblem_geometry import (RING_R, RING_W, ROPES, ROPE_W, STAR_OUT, STAR_IN, STAR_POINTS, STAR_OUTLINE, DOT_R,  # noqa: E402
                             FRAME_W, FRAME_H, FRAME_ROPES, FRAME_POSTS, FRAME_PADS, star_points as _star_points)


def seal_rgba(size, ss=4):
    """Печатка на прозорому тлі, size × size px (суперсемплінг ss для гладких країв)."""
    w = size * ss
    k = w / 200.0
    im = Image.new("RGBA", (w, w), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    gold, ink, crim = B.rgba("gold"), B.rgba("ink0"), B.rgba("crimson")
    outer, inner = RING_R + RING_W / 2, RING_R - RING_W / 2
    d.ellipse(((100 - outer) * k, (100 - outer) * k, (100 + outer) * k, (100 + outer) * k), fill=gold)
    d.ellipse(((100 - inner) * k, (100 - inner) * k, (100 + inner) * k, (100 + inner) * k), fill=ink)
    for y, x0, x1 in ROPES:
        d.line((x0 * k, y * k, x1 * k, y * k), fill=gold, width=max(1, int(ROPE_W * k)))
    pts = [(x * k, y * k) for x, y in _star_points(100, 100, STAR_OUT, STAR_IN, STAR_POINTS)]
    d.polygon(pts, fill=crim)
    # Обідок як у SVG (штрих по краю, половина назовні): PIL-овий outline у polygon дає «зубці» у западинах зірки.
    d.line(pts + pts[:2], fill=ink, width=max(1, int(STAR_OUTLINE * k)), joint="curve")
    d.ellipse(((100 - DOT_R) * k, (100 - DOT_R) * k, (100 + DOT_R) * k, (100 + DOT_R) * k), fill=gold)
    return im.resize((size, size), Image.LANCZOS)


def paste_seal(img, cx, cy, r):
    """Накласти печатку на RGBA-картинку: центр (cx, cy), радіус кільця r (px)."""
    size = int(round(r * 2 / 0.955))          # зовнішній край кільця = 95,5 з 100
    seal = seal_rgba(size)
    img.alpha_composite(seal, (int(round(cx - size / 2)), int(round(cy - size / 2)))) if img.mode == "RGBA" else \
        img.paste(seal, (int(round(cx - size / 2)), int(round(cy - size / 2))), seal)


def rope_frame_rgba(width=1040, ss=4):
    """Канатна рамка без літер: стійки з подушками й три канати. Висота за пропорцією 520:160."""
    scale = width / FRAME_W
    w, h = int(round(FRAME_W * scale)), int(round(FRAME_H * scale))
    k = scale * ss
    im = Image.new("RGBA", (w * ss, h * ss), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    for y, hh, col in FRAME_ROPES:
        d.rectangle((22 * k, y * k, 498 * k, (y + hh) * k), fill=B.rgba(col))
    for x, y, pw, ph in FRAME_POSTS:
        d.rounded_rectangle((x * k, y * k, (x + pw) * k, (y + ph) * k), radius=4 * k, fill=B.rgba("gold"))
    for x, y, pw, ph in FRAME_PADS:
        d.rounded_rectangle((x * k, y * k, (x + pw) * k, (y + ph) * k), radius=4 * k, fill=B.rgba("crimson"))
    return im.resize((w, h), Image.LANCZOS)


def app_icon(size):
    """Значок застосунку: печатка на чорному заокругленому квадраті."""
    ss = 4
    w = size * ss
    im = Image.new("RGBA", (w, w), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    d.rounded_rectangle((0, 0, w - 1, w - 1), radius=int(w * 0.22), fill=B.rgba("ink0"))
    d.rounded_rectangle((int(w * 0.03), int(w * 0.03), w - 1 - int(w * 0.03), w - 1 - int(w * 0.03)), radius=int(w * 0.19),
                        outline=B.rgba("gold"), width=max(2, int(w * 0.012)))
    im = im.resize((size, size), Image.LANCZOS)
    seal = seal_rgba(int(size * 0.74))
    im.alpha_composite(seal, ((size - seal.size[0]) // 2, (size - seal.size[1]) // 2))
    return im


def seal_svg():
    g, i, c = B.hexstr("gold"), B.hexstr("ink0"), B.hexstr("crimson")
    pts = " ".join("%.1f,%.1f" % p for p in _star_points(100, 100, STAR_OUT, STAR_IN, STAR_POINTS))
    ropes = "".join('<line x1="%g" y1="%g" x2="%g" y2="%g"/>' % (x0, y, x1, y) for y, x0, x1 in ROPES)
    return ('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 200" role="img"><title>Печатка «Рингу»</title>'
            '<circle cx="100" cy="100" r="%g" fill="%s" stroke="%s" stroke-width="%g"/>'
            '<g stroke="%s" stroke-width="%g">%s</g>'
            '<polygon points="%s" fill="%s" stroke="%s" stroke-width="%g" stroke-linejoin="round"/>'
            '<circle cx="100" cy="100" r="%g" fill="%s"/></svg>\n') % (
        RING_R, i, g, RING_W, g, ROPE_W, ropes, pts, c, i, STAR_OUTLINE, DOT_R, g)


def rope_frame_svg():
    parts = ['<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 %d %d" role="img"><title>Канатна рамка «Рингу» (без літер)</title>' % (FRAME_W, FRAME_H)]
    for y, hh, col in FRAME_ROPES:
        parts.append('<rect x="22" y="%d" width="476" height="%d" fill="%s"/>' % (y, hh, B.hexstr(col)))
    for x, y, pw, ph in FRAME_POSTS:
        parts.append('<rect x="%d" y="%d" width="%d" height="%d" rx="4" fill="%s"/>' % (x, y, pw, ph, B.hexstr("gold")))
    for x, y, pw, ph in FRAME_PADS:
        parts.append('<rect x="%d" y="%d" width="%d" height="%d" rx="4" fill="%s"/>' % (x, y, pw, ph, B.hexstr("crimson")))
    parts.append("</svg>\n")
    return "".join(parts)


def export(repo):
    docs = os.path.join(repo, "docs", "ring", "design", "brand")
    art = os.path.join(repo, "Assets", "Art", "Ring", "Brand")
    os.makedirs(docs, exist_ok=True)
    os.makedirs(art, exist_ok=True)
    with open(os.path.join(docs, "emblem_seal.svg"), "w", encoding="utf-8", newline="\n") as f:
        f.write(seal_svg())
    with open(os.path.join(docs, "rope_frame.svg"), "w", encoding="utf-8", newline="\n") as f:
        f.write(rope_frame_svg())
    outs = {"emblem_seal_1024.png": seal_rgba(1024), "emblem_seal_256.png": seal_rgba(256),
            "rope_frame_1040.png": rope_frame_rgba(1040), "app_icon_1024.png": app_icon(1024), "app_icon_256.png": app_icon(256)}
    for name, img in outs.items():
        img.save(os.path.join(art, name))
        print("BRAND", name, img.size)


if __name__ == "__main__":
    export(sys.argv[1] if len(sys.argv) > 1 else os.getcwd())
