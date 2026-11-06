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

### Додано 06.11.2026 для «Рингу» (з дозволу власника, відповідь на питання плану; Статут вигляду TYPE-01)

| Папка | Шрифт | Файли | Джерело | Навіщо |
|---|---|---|---|---|
| `SofiaSansExtraCondensed/` | Sofia Sans Extra Condensed (Lettersoup, B. Nikoltchev, A. Petrova) | `-Bold` (700), `-ExtraBold` (800), `-Black` (900) — статичні TTF, зібрані зі змінного `tools/ring_design/font_sources/SofiaSansExtraCondensed/SofiaSansExtraCondensed-Variable.ttf` (345 304 Б) скриптом `tools/ring_design/make_font_instances.py` (fontTools instancer) | https://github.com/google/fonts (`ofl/sofiasansextracondensed/`), OFL 1.1, Reserved Font Name не заявлено | display: виноски, мітки, імена — **лише ВЕЛИКІ літери** |
| `AmaticSC/` | Amatic SC (V. Adams, B. Nathan, T. Jockin) | `AmaticSC-Bold.ttf` (статичний, 156 180 Б) | `ofl/amaticsc/` | таблички шанувальників у 3D-світі (не UI) |
| `RubikSprayPaint/` | Rubik Spray Paint (NaN, L. Prowse) | `RubikSprayPaint-Regular.ttf` (статичний, 941 576 Б) | `ofl/rubikspraypaint/` | спрей-написи підвалу у 3D-світі (не UI) |

Перевірка гліфів — `python -I tools/ring_design/check_font_glyphs.py <файли>`: Ґ ґ Є є І і Ї ї, кирилиця А–Я а–я, апостроф ’,
« » № — –, цифри, латиниця — у всіх трьох родинах є. **Особливості (перевірено очима, рендер з файлів репозиторію):**
(1) у Sofia Sans Extra Condensed **немає знака ₴** — гроші зі знаком лише в ролі text (Fixel має ₴);
(2) її **малі кириличні — болгарські форми** (г як r, д як g, и як u, к як k), великі чисті (велика Л гостроверха «Λ») —
тому display лише ВЕЛИКИМИ через `RingTheme.Caps()`; (3) змінний файл у `Assets/` не кладемо (TextCore будує SDF зі
статичних накреслень) — він лежить поза `Assets/`, у `tools/ring_design/font_sources/`.

**Чому статичні файли.** Змінний `FixelVariable.ttf` і архів Noto на 70 МБ не
беремо: TextCore будує SDF-атлас зі статичних накреслень, а хінтинг у SDF ні на
що не впливає (тому Noto — unhinted, на третину менший).

Каталог і обґрунтування вибору — `docs/ASSETS.md`, рішення — `docs/GDD_AMENDMENTS.md`
Поправка №12.2, де що використовується — `docs/HUD_DESIGN.md` §6.2.
