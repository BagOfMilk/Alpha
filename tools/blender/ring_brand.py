# -*- coding: utf-8 -*-
"""Палітра «Рингу» для скриптів Blender/PIL. Згенеровано tools/ring_design/gen_brand.py
з docs/ring/design/brand_tokens.json. Вручну не правити: нові кольори — через арт-директора."""

HEX = {
    'ink0': '#0E0D12',
    'ink1': '#17151D',
    'ink2': '#221F2B',
    'ink3': '#2E2A39',
    'paper': '#EDE5D6',
    'dim': '#9A948A',
    'ash': '#6B6672',
    'gold': '#E8BA3C',
    'crimson': '#AA141C',
    'signal': '#E5483F',
    'cobalt': '#2F6FD6',
    'cobaltText': '#6DB6FF',
    'rose': '#FF7AB8',
    'frost': '#B7C6E0',
    'slate': '#232A39',
    'black': '#000000',
}

COLORS = {
    'ink0': (14, 13, 18),
    'ink1': (23, 21, 29),
    'ink2': (34, 31, 43),
    'ink3': (46, 42, 57),
    'paper': (237, 229, 214),
    'dim': (154, 148, 138),
    'ash': (107, 102, 114),
    'gold': (232, 186, 60),
    'crimson': (170, 20, 28),
    'signal': (229, 72, 63),
    'cobalt': (47, 111, 214),
    'cobaltText': (109, 182, 255),
    'rose': (255, 122, 184),
    'frost': (183, 198, 224),
    'slate': (35, 42, 57),
    'black': (0, 0, 0),
}

ROLES = {
    'bg': 'ink0',
    'panel': 'ink1',
    'panelRaised': 'ink2',
    'border': 'ink3',
    'hover': 'ink3',
    'text': 'paper',
    'textDim': 'dim',
    'disabled': 'ash',
    'accent': 'gold',
    'reward': 'gold',
    'primaryAction': 'gold',
    'strong': 'gold',
    'partial': 'paper',
    'miss': 'signal',
    'damage': 'signal',
    'danger': 'signal',
    'hero': 'cobalt',
    'heroText': 'cobaltText',
    'villain': 'crimson',
    'villainText': 'signal',
    'rebel': 'rose',
    'impulse': 'gold',
    'hype': 'paper',
    'defeat': 'frost',
    'defeatPlate': 'slate',
    'scrim': 'black',
}

ALPHA = {
    'panel': 0.86,
    'glass': 0.84,
    'shade': 0.45,
    'scrim': 0.72,
    'plate': 0.94,
    'underlay': 0.62,
    'tronGhost': 0.18,
    'tronDim': 0.34,
    'tronMid': 0.58,
}


def rgb(name):
    """Колір (R, G, B) 0..255; name — сирий токен або роль."""
    return COLORS[ROLES.get(name, name)]


def rgba(name, a=255):
    r, g, b = rgb(name)
    return (r, g, b, int(a))


def hexstr(name):
    return HEX[ROLES.get(name, name)]


def _lin(c):
    c = c / 255.0
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def linear_rgba(name, a=1.0):
    """Лінійний RGBA 0..1 для вузлів матеріалів Blender (sRGB → лінійний)."""
    r, g, b = rgb(name)
    return (_lin(r), _lin(g), _lin(b), a)

