# «Ринг» — зони сесій і правила роботи до М1

Веде ПМ — сесія «Ітерації 1, 2, 3 та поточний стан» (`local_27331424-bf18-4e28-a4b8-8cf4f822b8b1`).

Власник 08.11.2026:
- «ты теперь ПМ всех сессий»;
- «Пуш усе щоб усі робили з однакової версії Але потім не чіпали зони один одного.»;
- «Перший майлстоун це ітерація 1 повністю готова перевірена тобою.»;
- «Я замовник, Ти показуєш мені шо зроблено, я поправлю Чи на переробку віддаю».

## Правила

1. **Чужої зони не правимо.** Потрібна зміна в чужому файлі — запит власнику зони через ПМ. Дозволено лише
   додавати рядки до спільних файлів (нижче).
2. **Новий файл** належить тому, хто його створив, і той дописує його в таблицю. Файл, якого немає в таблиці, —
   питання до ПМ перед правкою.
3. **Моделі й вигляд екранів кар'єри розділені:** моделі (`Models/*ScreenModel`, ядро) — Кар'єра, вигляд
   (`UI/**`) — Меню.
4. **Спільні файли:**
   - `Assets/Resources/Ring/ui_uk.json`, `strings_uk.json` — лише свої ключі у своєму блоці; з появою англійської —
     і `en`;
   - заглушки лінту `tools/Game.Gameplay.Lint/**` — лише додавати;
   - конфлікти злиття розв'язує інтегратор разом із власником зони.
5. **Черга Unity і Blender** (RAM 16 ГБ): одночасно одна збірка чи редактор Unity і один Blender. Перед запуском
   написати ПМ і дочекатися черги. Збірка — без Development, спершу прохід компіляції. **Звук:** у гру йде лише
   схвалене власником (ворота ef64920, `RingSoundGate`); збірку з гілки без ef64920 (перевірка —
   `git merge-base --is-ancestor ef64920 <гілка>`) запускати лише із заглушеним процесом, доки гілку не перебазовано.
   Власник 08.11.2026 після турів «Меню» без воріт: «Почему снова звуки есть?»
6. **Злиття в основну гілку** `claude/wrestling-rpg-design-d0b2f6` робить лише інтегратор («Відео огляд»), після
   «так» власника на пакет. Пуш на GitHub — лише за командою власника.
7. **Видиме:** спершу У0 арт-директору, потім кадри (Статут). **Звіт:** у трьох розділах — «перевірено очима /
   тестом / не перевірено (чому)».

## Зони

Шляхи — від `Assets/_Project/Scripts/`, якщо не сказано інакше.

| Сесія | Файли |
|---|---|
| **ПМ + Бій** — `local_27331424…`, гілка `claude/ring-combat-overhaul` | `Core/Ring/Match/**`, `Core/Ring/RingContracts.cs` (кар'єрне з нього переїде в `CareerContracts.cs` — Кар'єрі), `Gameplay/Ring/Stage/RingClipTable.cs`, `Stage/RingBeats.cs`, `Stage/Unity/RingMatchDirector.cs`, `Models/MatchScreenModel.cs`, `Models/RingLabels.cs`, `UI/MatchScreenView.cs`, `UI/RingCallout.cs`, `RingGameBootstrap.Match.cs`; `Assets/Resources/Ring/career_moves.json`; тести `Match*`, `HopeSpot*`, `FinisherReach*`, `RingBeat*`, матчеві частини `RingModelsTests`; документи `docs/TRPG_DESIGN.md`, `docs/TRPG_DECISIONS.md`, `docs/ring/iter1_gaps.md`, `docs/ring/ZONES.md`, `docs/ring/CLIP_NEEDS.md` |
| **Изучение игры Ring** — `local_4d3baef2…` | Сцена, камера, вихід: `Gameplay/Ring/Stage/Unity/**` (крім `RingMatchDirector`, `RingSound`, `RingMenuAudio`, `RingMenuAssets`), `Stage/RingStageContract.cs`, `Stage/EntranceProfile.cs`, `Stage/EntranceScreen.cs`, `Core/Ring/RopeSim.cs`, `RingGameBootstrap.cs` (каркас), Editor: `RingSceneBuilder` (сцена), `RingStageSceneSetup`, `RingInEditor`, `RingEntranceScreenImport`; Blender: арени, натовп, реквізит, cast, закулісся (`tools/blender/ring_venues*`, `ring_gym`, `ring_show`, `ring_crowd`, `ring_props`, `ring_hero`, `ring_cast/**`); `docs/ring/ENTRANCE_PROFILE.md`, `Assets/Art/Ring/Cast/CAST_ANIM.md`; тести `Entrance*` |
| **Кар'єра** — нова сесія | `Core/Ring/Career/**`, `Core/Ring/Feud/**`, `Core/Ring/Promo/**`, `Core/Ring/Progression/**`, `Core/Ring/Json/**`, `Core/Ring/RingCampaign.cs`, `Models/DevelopmentScreenModel.cs`, `FeudScreenModel.cs`, `PromoScreenModel.cs`, нові моделі екранів кар'єри, `CareerLooks.cs`, `RingFeudBootstrap.cs`, `RingGameBootstrap.Career.cs`, `RingSaveFile.cs` (після злиття «Скилів»); ресурси `career_backstage.json`, `career_gimmicks.json`, `career_roster.json`, `feud_hammer.json`, `backstage_uk.json`; тести `Career*`, `Feud*`, `Promo*`, `Gimmick*`, `Economy*`, `Progression*`, `Backstage*`, `WeekActions*`, `RingCampaign*`, `MiniJson*` |
| **Меню** — `local_d8c816ac…` (+ форк звуку `local_bc183877…`) | `Gameplay/Ring/UI/Menus/**`, вигляд екранів кар'єри (`UI/Career/**` B03 v2, `UI/CareerScreens.cs` — на зняття, `FeudScreenView`, `PromoScreenView`, `EndingScreenView`), `UI/RingUi.cs`, `Models/RingText.cs`, `MenuMotion.cs`, `RingSettings.cs`, `RingPause.cs`, `RingAudioMix.cs`, `RingSoundApproval.cs`, `RingGameBootstrap.Menus.cs`, `RingMenuShots.cs`, `RingSoundGate.cs`, `Stage/Unity/RingSound.cs`, `RingMenuAudio.cs`, `RingMenuAssets.cs`, Editor `RingMenuAudioImport`; `sound_approvals.json`, `tools/ring_audio/**`, переклади `ui_<мова>.json` і `ring_strings_<мова>.txt` (крім `uk`); тести `RingMenuModel*`, `RingSoundApproval*`, `MonoBehaviourFileName*`; журнал `docs/menus-progress.md` |
| **Кліпи** — `local_815630bf…` (+ wwe `local_383c333e…`) | `tools/blender/ring_anims*`, `tools/blender/ring_review.py` (огляд кліпів; створено лінією анімацій, 8861986), `docs/ring/ANIM_REVIEW_*.md`, `Assets/Art/Animations/RING_Moves*.fbx`, `ring_anims_*.txt`, `docs/ring/ANIM_BRIEF_COMBAT_CENTER.md`, `docs/ring/ANIM_PLAN_ITER2.md`, `docs/research/RING_ANIM_REFERENCES.md` |
| **Відео огляд** — `local_c3c3cec5…` (інтегратор) | Злиття в основну гілку, `tools/run-offscreen.ps1`, `tools/ring-editor.ps1`, Editor `RingSceneBuilder.Player.cs` (BuildPlayer*), `RingLaunchArgs.cs`, `Assets/Settings/URP-*`, `ProjectSettings/QualitySettings.asset`, оточення збірки (Burst) |
| **Скилы** — `local_f69d1b50…` (QA) | `.claude/skills/**`, `docs/ring/playtest_*`, `docs/ring/skills_audit_*`. Код інших зон не правити, вади — промптом ПМ |
| **Арт-директор** — нова сесія замість зниклої | `docs/ring/DESIGN_SYSTEM.md`, `docs/ring/design/**`, `tools/ring_design/**`, `Models/BrandTokens.cs`, `UI/RingTheme.cs`, `UI/RingFonts.cs`, `RingFontSet.cs`, `RingFontSpike.cs`, Editor `RingFontAssets`, шрифти, тести `RingDesignGuard*`, `TitantronAssetsGuard*` |

**Разовий розподіл на Етапі 0** (робить «Изучение» як власник файлів) — **зроблено 08.11.2026, 7f19686**:
- `RingGameBootstrap.cs` («Изучение», каркас): запуск, UI, кадр, шоу 3D, виходи, `SyncStage`, клавіші, огляд, лукбук;
- `RingGameBootstrap.Career.cs` (Кар'єра; до її появи — «Изучение»): дані, сейв `ICareerHost`, `MigrateSave`, `Rebind`,
  `ViewKey` / `MakeView`, `BackstageCtx`, `StartShow`, бот тижня (`BotWeekStep`, `ReviewPerks`, `BotGrow`);
- `RingGameBootstrap.Match.cs` (Бій): `BotMatchStep`, `ReviewRopes` (у бою — `ReviewMoves`), `BotTurn`, `BeatShots`;
- `RingGameBootstrap.Menus.cs` («Меню») — без змін;
- `Editor/RingSceneBuilder.cs` — сцена («Изучение»); `Editor/RingSceneBuilder.Player.cs` — збірка («Відео огляд»).

Межі між ними (від «Изучение»):
- `SyncStage` у каркасі читає `CareerStage`, `ShowPhase`, `RingStage` і `SeasonRecord`: зміна етапів кар'єри —
  запит до «Изучение».
- `SavePath` (`.Career`) знає файл огляду меню `ring_menu_bot.txt` і поле `_menuShotsDir` із `.Menus`.
- Кадри огляду: ключ із `MatchEvent` (`BeatShots`) — Бій; моменти FX сцени (`FxShots`) — каркас.
- `ApplyScale` у каркасі: перелік екранів поза «Розміром інтерфейсу» (рядок `bool menu = …`) задає «Меню» — 1488447
  додав екрани кар'єри B03 v2 (`UI.CareerScreenView`) за рішенням власника «Так» (08.11.2026); змінювати цей рядок —
  запитом до «Изучение», решта методу — каркас.

**Рішення ПМ (08.11.2026, вечір):**
- `RingScreenView` — абстрактний клас у `UI/RingUi.cs`, отже зона «Меню». Віртуальний `OnBack()` додає «Меню»;
  Esc спершу закриває верхнє (шухляду, діалог), і лише коли закривати нічого, відкриває паузу. Перевизначення в
  `MatchScreenView` — за «ПМ + Бій» (Ф3).
- **Переклади** всіх файлів `*_<мова>` (крім `uk`), зокрема `strings_<мова>.json` і `backstage_<мова>.json`, робить
  «Меню» — одним голосом. Український оригінал і ключі вмісту кар'єри — Кар'єра; охоронець «кожен ключ перекладено» —
  «Меню».
- Завантаження тексту за мовою (`RingLanguages.Resource`) у `RingGameBootstrap.cs` («Изучение») і `RingFeudBootstrap.cs`
  (Кар'єра) — запитами після перебазування на основу Етапу 0.
- **Поділ звуку** (погоджено «Меню» і форком, 08.11.2026):
  - **форк звуку** (`local_bc183877…`, гілка `claude/ring-sound-approval-a5975d`) — П4.4: таблиця «подія → звук»,
    моно, бібліотека й 3 нові варіанти на відмову; `Stage/Unity/RingSound.cs`, `RingSoundMap`, `RingSoundGate.cs`,
    `Models/RingSoundApproval.cs`, Editor `RingSoundImport`, `tools/ring_audio/library`, `ring_bells*`, `ring_impacts*`,
    `ring_crowd*`, `ring_music*`, `docs/ring/audio/**`, `.meta` у `Assets/ThirdParty/CC0/Audio/Ring`;
  - **«Меню»** — `Models/RingAudioMix.cs` (шини, гучність), `RingMenuAudio.cs`, `RingMenuAssets.cs`, Editor
    `RingMenuAudioImport`, `UI/Menus/**`, `RingGameBootstrap.Menus.cs`; ворота звуку «Меню» лише викликає.
- **Пакет бою** (ядро ПМ + «Изучение» `RingStage3D` / `RingStageDemo` / `RingCine` + форк `RingSound.cs`) іде в
  основну одним злиттям: окремо одне без одного не збирається. Порядок: Етап 0 → ПМ зливає основну в
  `claude/ring-combat-overhaul` (merge, без переписування SHA) → «Изучение» і форк роблять гілки від її верхівки →
  ПМ вливає їх → «так» власника → інтегратор.

**Черга Unity / Blender** (веде ПМ; оновлюється тут):
1. ~~«Меню» — збірка без Development + офскрін-тури меню й кар'єри (1080p, 720p, 4:3, масштаб 120 %)~~ — **зроблено
   08.11.2026**, Unity закрито; гілка без воріт звуку — ПМ глушив процес Ring на час турів;
2. ~~форк звуку — прохід компіляції `RingSound.cs` і `RingSoundImport.cs`~~ — **зроблено** 16:29 (126b631, 0 помилок);
3. ~~«Меню» — збірка 26 і тури кар'єри 1080 / 4:3~~ — **зроблено** 16:44 (звіт: вад немає; 32211c3, 273d4bf, 775e753);
4. «Скилы» — базовий прохід ботом трьох сезонів на збірці з основи 7f19686 — **«можна» дано 17:08**;
5. Етап 0 — прохід компіляції в редакторі й коміт згенерованих `.meta` (зокрема `RingSoundApprovalTests.cs.meta`) —
   «Відео огляд», після поновлення ліміту сесії; перед стартом питає «можна».

**Черга У0** (арт-директора зараз немає — сесію «Дизайн-система игры» не знайдено; ПМ тримає запити тут до його появи;
власник 08.11.2026: «Створю директора» — нову сесію арт-директора створює власник, бриф дав ПМ):
1. «Меню», 08.11.2026 — вкладка «Гра» в налаштуваннях (B08, ключ `m.set.tab.game`): три рядки тим самим
   компонентом-вибором, що й на інших вкладках (назва ліворуч, значення з ◀ ▶ праворуч, підказка рядка внизу панелі):
   QTE — Увімк. / Вимк.; Екшн-кадри — Вимк. / Рідко / Звично / Часто; Сповільнення фінішера — Увімк. / Вимк. Нових
   кольорів, шрифтів і складників немає. Питання: місце вкладки (перша чи після «Звук») і чи потрібен під кожним рядком
   підпис-пояснення. API є (c735e79 після перебазування, було bba5be8; `RingMatchPrefs`); вкладку не роблять до відповіді.
2. ~~«Меню», 08.11.2026 — «Розмір інтерфейсу» 120 % ламав екрани кар'єри~~ — **вирішено власником 08.11.2026
   («Так»):** у М1 масштаб на кар'єру не діє (32211c3, після перебазування — 1488447), компактний макет — ітерація 2
   (`TRPG_DECISIONS`).

**Передача:** правки переробки бою в `CareerContent.cs`, `CareerSession.cs`, `GimmickTests.cs`, `CareerKit.cs`
(гілка `claude/ring-combat-overhaul`) після злиття переходять у зону Кар'єри. У `b478385` до них додалось
`CareerSession.UniversalActions`: дії «Вийти з рингу», «Повернутися в ринг», «Стілець або стіл» (бій за рингом, §4.4).
Нові тести бою (`MatchOutsideTests`) — зона «ПМ + Бій» за шаблоном `Match*`.

## Етап 0 — «Одна версія»

**Стан 08.11.2026, 17:10:** основа `claude/wrestling-rpg-design-d0b2f6` = **7f19686, запушено на GitHub** (власник у
вікні «Відео огляд»: «Так, пушай»; тести 2286/0, компіляція проти DLL Unity без редактора — 0 помилок, секретів 0,
великих файлів поза LFS немає). «Скилы» (5947993) уже в ній. Лишилось: прохід у редакторі й `.meta` (крок 4) і
перебазування гілок сесій на 7f19686 з SHA бази ПМ (крок 7):
- «ПМ + Бій» — **злито**: 1c03451 (тести 2292/0); від неї гілки пакета бою «Изучение» і форку звуку;
- «Меню» — **перебазовано**: `claude/menu-design-animations-audio-17ad7f` від 7f19686, верхівка 7cb06e7 (7 комітів:
  47e0048 B03 v2 … 7cb06e7; не запушено; ПМ звірив `merge-base`: 7f19686 і ef64920 — у гілці). 21 файл — зона «Меню»,
  крім рядка `ApplyScale` у каркасі (див. «Межі»); `ui_uk.json` — обидва набори ключів (8c3f16b і «Меню»), заглушки
  лінту лише додані. Тести в них 2283/2290, 1 збій `AnimClipPresenceTests` — кліпи в LFS не витягнуті в їхній робочій
  копії (у ПМ на 1c03451 той самий тест зелений). Правки 8c3f16b зі старих `CareerScreens` (P1-5, P2-3, P2-4, кінець
  сезону, P3) «Меню» переносить у вигляди B03 v2 окремим комітом;
- «Кліпи» — **перебазовано**: `claude/ring-anims-center-544e67` від 7f19686, верхівка 15bb78c (не запушено; ПМ звірив
  `merge-base --is-ancestor`: 7f19686 і ворота звуку ef64920 — у гілці). Три коміти, лише генератори зони «Кліпи»
  (`ring_anims.py`, `ring_anims_combat.py`, `ring_review.py`): P1-1 утримання — у порогах (8c9f806), P1-2 — WIP;
  FBX нових кліпів у `Assets` ще немає.

1. Кожна сесія комітить поточну роботу на своїй гілці (незавершене — «WIP»). Першою — пачка «Изучение»: **закомічено
   08.11.2026** (власник у вікні «Изучение»: «Так, комітити») — bec45bf (матч, P1-4; правки в `MatchScreenView` /
   `MatchScreenModel` зроблено до зон, у Ф2/Ф3 бій їх зберігає), 8c3f16b (кар'єра: P1-5, P2-3, P2-4, підсумок сезону;
   у `CareerScreens` — «Меню» переносить їх у вигляди B03 v2), 56e4852 (3D: P0-2, `RingVenueKit`, велике шоу P1-7, P3).
   Далі «Изучение» зливає основну a2aef60 у свою гілку (конфлікти — її), інтегратор перемотує основну на результат.
2. Інтегратор зливає в основну гілку, з тестами після кожного кроку:
   1. бриф `claude/ring-anim-brief-center` до `9a6b5bc` — **злито** (основна = a2aef60, тести 2286/0, 08.11.2026);
   2. пачку «Изучение» — гілка «Изучение» злила основну в себе (aa69e23, конфлікт лише в RingFx), розподілила файли
      (7f19686; лінт, тести 2286/0); інтегратор перемотує основну на 7f19686;
   3. «Скилы» (`claude/ring-game-skills-0d696d`, 89ac15d + 931498e). Конфлікти в RingFx, RingStage3D,
      RingStageDemo і RingGameBootstrap — з підтвердженням «Изучение»; доступність — з меню (ca3a7c5), дублі
      прибрати.
3. Разовий розподіл `RingGameBootstrap` і `RingSceneBuilder` (вище) — **зроблено**, 7f19686.
4. Перевірки:
   - `git lfs ls-files` до й після злиття, цілість вказівників LFS;
   - прохід компіляції Unity і коміт згенерованих `.meta`;
   - дублі ключів JSON.
5. Перед пушем (репозиторій публічний):
   - секрети й токени;
   - великі файли поза LFS;
   - рядки ліцензій нових асетів (`docs/ASSETS.md`);
   - нотатки «чиста кімната» в `docs/research/`;
   - `git lfs push --dry-run` — обсяг проти квоти GitHub.

   Знахідка або ризик оплати — стоп і питання власнику.
6. Пуш основної гілки «Рингу», гілок сесій «Рингу» і LFS. `main` Alpha не чіпаємо.
7. Незлите — переробку бою, модуль кліпів і B03 v2 — власники перебазовують на нову основну й пушать гілкою.
   Кожна сесія пише ПМ SHA своєї бази.

## Пункти проходу ботом 08.11 (`docs/ring/playtest_2026-11-08_prompt.md`) → власник

| Пункт | Власник | Стан |
|---|---|---|
| P0-1 level0 | — | злито (06ece58, 734fa50) |
| P0-2 стільці, P1-5 (ядро), P1-7 (частина), P2-3, P2-4, частина P3 | Изучение | у пачці, незакомічено |
| P0-3 Burst AOT | Відео огляд | відкрито |
| P1-1 утримання, P1-2 больовий, P1-3 фінішер → утримання | Бій (таймінг) + Кліпи | Кліпи: P1-1 у генераторі в порогах (8c9f806, рахунок 1,6 с), FBX ще не в `Assets`; P1-2 WIP (15bb78c); P1-3 «Метеор» — далі. Бій: таймінг — Ф2 |
| P1-4 3D і журнал | Изучение (пачка); Бій тримає в новому режисері | у пачці |
| P1-5 вигляд розвитку, P3 екранів кар'єри | Меню (B03 v2) | відкрито |
| P1-6 популярність, P2-2 поради розвитку, P2-6 стать суперника | Кар'єра + рішення власника | відкрито |
| P1-7 велике шоу, зал за рівнем | Изучение | відкрито |
| P2-1 довжина матчу | Бій | відкрито |
| P2-5 закулісся | Изучение (3D) + Кар'єра (вміст) | відкрито |
| P3 HUD матчу (підкладка журналу, підказка без миші) | Бій | відкрито |
| P3 сцена («!», кабель, світло, роздягальня, модель у промо) | Изучение | частково в пачці |
| P3 геймпад | Меню | відкрито |
