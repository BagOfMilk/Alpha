#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Статичні накреслення зі змінного шрифту.

Навіщо: TextCore (Unity) будує SDF-атлас зі СТАТИЧНИХ накреслень, змінні TTF у проєкт не беремо
(Assets/ThirdParty/Fonts/README.md). SIL OFL 1.1 дозволяє похідні файли; Reserved Font Name у Sofia Sans не заявлено.

Запуск (шлях до змінного файлу й тека виводу — аргументами):
  python -I tools/ring_design/make_font_instances.py <variable.ttf> <out_dir> <ім'я-основа> 700:Bold 800:ExtraBold 900:Black
Приклад (як зроблено 06.11.2026):
  python -I tools/ring_design/make_font_instances.py tools/ring_design/font_sources/SofiaSansExtraCondensed/SofiaSansExtraCondensed-Variable.ttf \
      Assets/ThirdParty/Fonts/SofiaSansExtraCondensed SofiaSansExtraCondensed 700:Bold 800:ExtraBold 900:Black
Перевірка гліфів: tools/ring_design/check_font_glyphs.py.
"""
import os
import sys

from fontTools.ttLib import TTFont
from fontTools.varLib import instancer


def main(argv):
    if len(argv) < 5:
        sys.exit(__doc__)
    src, out_dir, base = argv[1], argv[2], argv[3]
    os.makedirs(out_dir, exist_ok=True)
    for spec in argv[4:]:
        wght, label = spec.split(":")
        font = TTFont(src)
        inst = instancer.instantiateVariableFont(font, {"wght": float(wght)}, inplace=False, updateFontNames=True)
        out = os.path.join(out_dir, "%s-%s.ttf" % (base, label))
        inst.save(out)
        print("%s  %d Б  (wght=%s)" % (out, os.path.getsize(out), wght))


if __name__ == "__main__":
    main(sys.argv)
