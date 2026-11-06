# -*- coding: utf-8 -*-
"""Геометрія знака «Рингу» — чисті константи й математика без Pillow/numpy/Blender, щоб один і той самий знак малювали
tools/ring_design/emblem.py (PIL), tools/blender/ring_textures.py (numpy у Blender) і SVG-майстри. Статут вигляду, ARENA-03."""
import math

# Печатка в одиницях viewBox 200 × 200 (центр 100, 100).
RING_R, RING_W = 92.0, 7.0
ROPES = ((68.0, 22.0, 178.0), (100.0, 14.0, 186.0), (132.0, 22.0, 178.0))   # (y, x0, x1)
ROPE_W = 4.0
STAR_OUT, STAR_IN, STAR_POINTS, STAR_OUTLINE = 66.0, 30.0, 8, 5.0
DOT_R = 13.0

# Канатна рамка в одиницях viewBox 520 × 160 (літер немає — «РИНГ» у ній ЖИВИЙ текст).
FRAME_W, FRAME_H = 520, 160
FRAME_ROPES = ((40, 8, "crimson"), (76, 8, "paper"), (112, 8, "crimson"))   # (y, висота, токен кольору)
FRAME_POSTS = ((8, 22, 22, 116), (490, 22, 22, 116))                        # (x, y, w, h)
FRAME_PADS = ((4, 36, 30, 16), (4, 108, 30, 16), (486, 36, 30, 16), (486, 108, 30, 16))


def star_points(cx, cy, r_out, r_in, n):
    """Вершини n-променевої зірки (промінь угору першим), у координатах «y вниз»."""
    pts = []
    for k in range(n * 2):
        a = math.radians(-90 + k * 180.0 / n)
        rr = r_in if k % 2 else r_out
        pts.append((cx + rr * math.cos(a), cy + rr * math.sin(a)))
    return pts


def star_radius_numpy(a, r_out, r_in, n):
    """Радіус межі зірки як функція кута (numpy-масив a, радіани): пряма між променем і западиною — той самий многокутник."""
    import numpy as np
    period = 2 * np.pi / n
    phi = np.abs(((a + np.pi / 2 + period / 2) % period) - period / 2)        # відстань до найближчого променя, 0..період/2
    th = np.pi / n
    t = r_out * np.tan(phi) / (r_in * np.sin(th) - np.tan(phi) * (r_in * np.cos(th) - r_out))
    px = r_out + t * (r_in * np.cos(th) - r_out)
    py = t * r_in * np.sin(th)
    return np.sqrt(px * px + py * py)
