#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Перевірка гліфів шрифту для «Рингу» на рівні cmap (Статут вигляду TYPE-05).

Запуск: python -I tools/ring_design/check_font_glyphs.py <font.ttf> [ще.ttf ...]
Код виходу 1, якщо якогось із потрібних знаків немає. Шрифти — лише аргументами (недовірені файли не виконуються).
"""
import sys

from fontTools.ttLib import TTFont

REQUIRED = {
    "Ґ ґ Є є І і Ї ї": "ҐґЄєІіЇї",
    "кирилиця А–Я а–я": "".join(chr(c) for c in range(0x0410, 0x0450)),
    "апостроф ’ і '": "’'",
    "« » № — –": "«»№—–",
    "цифри 0–9 і % : · / ( ) ! ? + − ×": "0123456789%:·/()!?+−×",
    "латиниця A–Z a–z": "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz",
}


# Не рахуються як помилка: у display-ролі грошовий знак не потрібен (ціни — роль text, Fixel має ₴).
OPTIONAL = {"₴ (потрібен лише ролі text)": "₴"}


def main(paths):
    # -I ігнорує PYTHONIOENCODING, а консоль Windows (cp1251) не має ₴ і ✓ — виводимо в UTF-8 явно.
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except AttributeError:
        pass
    bad = 0
    for path in paths:
        font = TTFont(path)
        cmap = font.getBestCmap()
        name = font["name"]
        fam = name.getDebugName(1)
        sub = name.getDebugName(2)
        weight = font["OS/2"].usWeightClass
        variable = "fvar" in font
        print("%s — «%s %s», вага %s, змінний: %s" % (path, fam, sub, weight, "так" if variable else "ні"))
        for label, chars in REQUIRED.items():
            missing = [ch for ch in chars if ord(ch) not in cmap]
            if missing:
                bad += 1
                print("   ✗ %s: немає %s" % (label, " ".join(missing)))
            else:
                print("   ✓ %s" % label)
        for label, chars in OPTIONAL.items():
            missing = [ch for ch in chars if ord(ch) not in cmap]
            print("   %s %s%s" % ("•" if missing else "✓", label, ": немає (допустимо для display)" if missing else ""))
    return 1 if bad else 0


if __name__ == "__main__":
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    sys.exit(main(sys.argv[1:]))
