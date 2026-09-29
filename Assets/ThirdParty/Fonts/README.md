# Сторонні асети: шрифти

Обидва шрифти — під **SIL Open Font License 1.1**. Атрибуція в титрах не
обов'язкова, але текст ліцензії лежить поруч із файлами (`OFL.txt`). Змінювати
назву шрифту (Reserved Font Name) можна лише за умовами OFL; імпорт у
TextCore / FontAsset файл не модифікує. Завантажено 29.09.2026 з дозволу
власника (Поправка №12.2). Кирилиця з ґ, є, і, ї, апостроф U+2019, ₴, « »
перевірені в кожному файлі.

| Папка | Шрифт | Файли | Джерело | Версія | Навіщо |
|---|---|---|---|---|---|
| `Fixel/` | Fixel Text + Fixel Display (MacPaw) | FixelText-Regular, -Medium, -SemiBold, -Bold, FixelDisplay-SemiBold (статичні TTF) | https://github.com/MacPaw/Fixel (`fonts/ttf/`, коміт `514fd02`) | 1.000 | інтерфейс і репліки; Display — заголовки |
| `NotoSerif/` | Noto Serif (Noto Project) | NotoSerif-Regular, -Italic, -Bold (статичні TTF, unhinted) | https://github.com/notofonts/notofonts.github.io (`fonts/NotoSerif/unhinted/ttf/`) | 2.015 | «паперові» екрани: хроніка, звіти, доповіді |

**Чому статичні файли.** Змінний `FixelVariable.ttf` і архів Noto на 70 МБ не
беремо: TextCore будує SDF-атлас зі статичних накреслень, а хінтинг у SDF ні на
що не впливає (тому Noto — unhinted, на третину менший).

Каталог і обґрунтування вибору — `docs/ASSETS.md`, рішення — `docs/GDD_AMENDMENTS.md`
Поправка №12.2, де що використовується — `docs/HUD_DESIGN.md` §6.2.
