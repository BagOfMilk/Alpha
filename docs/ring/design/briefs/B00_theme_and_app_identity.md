# B00 · Тема в коді, шрифти, ідентичність застосунку

`R/` = `Assets/_Project/Scripts/Gameplay/Ring/`. Ключові кадри-приклади — `docs/ring/design/ref/key_palette.png`, `key_identity.png`.

## Мета
Одним невеликим кроком привести ВСІ екрани до єдиної палітри й шрифтів, не чіпаючи їх розкладки, і прибрати «Alpha» з застосунку.

## 1 · Палітра: `RingUi` стає фасадом `RingTheme` (без змін екранів)
`R/UI/RingTheme.cs` і `R/Models/BrandTokens.cs` згенеровані (`python tools/ring_design/gen_brand.py`). У `R/UI/RingUi.cs:69–84` заміни значення, **імена лишаються**:

| Було (`RingUi.*`) | Стане | Примітка |
|---|---|---|
| `Bg`, `Panel`, `PanelRaised`, `Border` | `RingTheme.Bg`, `Panel`, `PanelRaised`, `Border` | майже той самий відтінок |
| `PanelHover` | `RingTheme.Hover` | |
| `Text` / `Dim` / `Disabled` | `RingTheme.Text` / `TextDim` / `Disabled` | |
| `Gold` | `RingTheme.Gold` (`#E8BA3C`) | єдине золото |
| `Good`, `Warn` | `RingTheme.Gold` | зеленого й бурштинового більше немає; «успіх» = золото |
| `Bad` | `RingTheme.Signal` | шкода/провал |
| `Face` / `Heel` / `Rebel` | `HeroText` / `VillainText` / `Rebel` (рожевий) | для поверхонь — `Hero`, `Villain` |
| `Overlay` | `RingTheme.WithAlpha(RingTheme.Scrim, BrandTokens.AlphaScrim)` | |

- `RingUi.PathColor`: Face → `HeroText`, Heel → `VillainText`, Rebel → `Rebel`. `RingUi.TierColor`: Strong → `Strong` (золото), Partial → `Partial` (папір), Miss → `Miss` (сигнал).
- Прозорості (`0,45; 0,86; 0,80; 0,84`) → `BrandTokens.AlphaShade/AlphaPanel/AlphaGlass`; підкладка слова виноски (`0,62`) → `BrandTokens.AlphaUnderlay`, «ПОРАЗКА» (`0,62; 0,72; 0,95`) → `RingTheme.Defeat`.
- Решту літералів кольору (`MatchScreenView`, `PromoScreenView`, `CareerScreens`, `RingCallout`, `RingShow` `#FFD966`, `CareerLooks` костюми не чіпати — це вибір гравця) — замінити на `RingTheme.*`. Мета: числа в `guard_baseline.json` падають.
- `CareerScreenView` дублює `RingUi.SideColumn/GlassCard/Section/HintLine/SmallButton` (`CareerScreens.cs:39–108`) — злити в `RingUi`.

## 2 · Шрифти (спайк + підключення)
Файли в репо: `Assets/ThirdParty/Fonts/SofiaSansExtraCondensed/` (Bold 700, ExtraBold 800, Black 900 — статичні TTF), `AmaticSC/`, `RubikSprayPaint/`; текст — `Fixel/` (є).
- Єдина точка: нині `HudToolkitTheme.LoadFont()` → `AlphaHudArt` (Alpha-асет, його оновлює Alpha-скрипт — крихко). Зроби **`RingFonts`** (Ring-асет або `Resources/Ring`): ролі `Display(weight)`, `Text(weight)`, `Hand`, `Spray`; екрани беруть шрифт лише звідти. Ключ тесту `Ratchet_FontLoads_DoNotGrow` має падати.
- **Спайк (≤ пів дня):** імпорт TTF у TextCore/UITK; чи рендеряться Ґ Є І Ї; **Sofia лише ВЕЛИКИМИ** (`RingTheme.Caps(текст)`: її малі кириличні — болгарські форми, перевірено очима); **знака ₴ у Sofia немає** — гроші зі знаком лише `Text`; чи працює `-unity-font-style: italic` (синтетичний нахил) для ВЕЛИКИХ; кеглі з `BrandTokens.Type*`/`Stinger*`.
- Дебаг-мітка зі стрічкою «Їжак Єнот Ґанок · Л — Λ · 0123456789 · ₴» на трьох кеглях у кожній ролі — кадр у звіті.
- Правила імпорту шрифтів — `Editor/ThirdPartyUiImportSettings.cs`; додай нові шляхи.

## 3 · Ідентичність застосунку (`ProjectSettings/ProjectSettings.asset`)
- Нині `productName: Alpha`, `companyName: DefaultCompany`, іконка/курсор не задані, сплеш Unity увімкнено (`:15–42`). Зроби: `productName` «Ринг»; значок — `Assets/Art/Ring/Brand/app_icon_1024.png` (+256); курсор — пізніше.
- **Ризик збережень:** `ring_career.txt` лежить у `Application.persistentDataPath` (`RingGameBootstrap.cs:72`), а шлях залежить від `companyName/productName` — зміна **осиротить наявні збереження**. Додай міграцію (при першому запуску скопіювати зі старого `…/DefaultCompany/Alpha/` у новий) або лиши назви й зміни лише заголовок вікна. `companyName` — рішення власника (спитай).
- Сплеш Unity — вимкнути, якщо ліцензія дозволяє (перевір). «Development Build» у кадрі — лише у dev-збірці; для показу власнику додай збірку без `BuildOptions.Development` (`Editor/RingSceneBuilder.cs:103`).

## Приймання (У2)
Кадри титулу, створення, тижня, картки шоу, матчу до/після зі зміненою палітрою; кадр шрифтової стрічки; вікно застосунку з назвою й значком; `RingDesignGuardTests` зелений, база знижена. Не чіпати розкладку екранів — це B01–B03.
