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
   написати ПМ і дочекатися черги. Збірка — без Development, спершу прохід компіляції.
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
| **Меню** — `local_d8c816ac…` (+ форк звуку `local_bc183877…`) | `Gameplay/Ring/UI/Menus/**`, вигляд екранів кар'єри (`UI/Career/**` B03 v2, `UI/CareerScreens.cs` — на зняття, `FeudScreenView`, `PromoScreenView`, `EndingScreenView`), `UI/RingUi.cs`, `Models/RingText.cs`, `MenuMotion.cs`, `RingSettings.cs`, `RingPause.cs`, `RingAudioMix.cs`, `RingSoundApproval.cs`, `RingGameBootstrap.Menus.cs`, `RingMenuShots.cs`, `RingSoundGate.cs`, `Stage/Unity/RingSound.cs`, `RingMenuAudio.cs`, `RingMenuAssets.cs`, Editor `RingMenuAudioImport`; `sound_approvals.json`, `tools/ring_audio/**`, переклади `ui_<мова>.json` і `ring_strings_<мова>.txt` (крім `uk`); тести `RingMenuModel*`, `RingSoundApproval*`, `MonoBehaviourFileName*` |
| **Кліпи** — `local_815630bf…` (+ wwe `local_383c333e…`) | `tools/blender/ring_anims*`, `Assets/Art/Animations/RING_Moves*.fbx`, `ring_anims_*.txt`, `docs/ring/ANIM_BRIEF_COMBAT_CENTER.md`, `docs/ring/ANIM_PLAN_ITER2.md`, `docs/research/RING_ANIM_REFERENCES.md` |
| **Відео огляд** — `local_c3c3cec5…` (інтегратор) | Злиття в основну гілку, `tools/run-offscreen.ps1`, `tools/ring-editor.ps1`, Editor `RingSceneBuilder` (частина BuildPlayer*), `RingLaunchArgs.cs`, `Assets/Settings/URP-*`, `ProjectSettings/QualitySettings.asset`, оточення збірки (Burst) |
| **Скилы** — `local_f69d1b50…` (QA) | `.claude/skills/**`, `docs/ring/playtest_*`, `docs/ring/skills_audit_*`. Код інших зон не правити, вади — промптом ПМ |
| **Арт-директор** — нова сесія замість зниклої | `docs/ring/DESIGN_SYSTEM.md`, `docs/ring/design/**`, `tools/ring_design/**`, `Models/BrandTokens.cs`, `UI/RingTheme.cs`, `UI/RingFonts.cs`, `RingFontSet.cs`, `RingFontSpike.cs`, Editor `RingFontAssets`, шрифти, тести `RingDesignGuard*`, `TitantronAssetsGuard*` |

**Разовий розподіл на Етапі 0** (робить «Изучение» як власник файлів): `RingGameBootstrap.cs` → каркас + partial
`.Match.cs` (Бій) і `.Career.cs` (Кар'єра); `Editor/RingSceneBuilder.cs` → partial «сцена» («Изучение») і
`BuildPlayer` («Відео огляд»).

**Рішення ПМ (08.11.2026, вечір):**
- `RingScreenView` — абстрактний клас у `UI/RingUi.cs`, отже зона «Меню». Віртуальний `OnBack()` додає «Меню»;
  Esc спершу закриває верхнє (шухляду, діалог), і лише коли закривати нічого, відкриває паузу. Перевизначення в
  `MatchScreenView` — за «ПМ + Бій» (Ф3).
- **Переклади** всіх файлів `*_<мова>` (крім `uk`), зокрема `strings_<мова>.json` і `backstage_<мова>.json`, робить
  «Меню» — одним голосом. Український оригінал і ключі вмісту кар'єри — Кар'єра; охоронець «кожен ключ перекладено» —
  «Меню».
- Завантаження тексту за мовою (`RingLanguages.Resource`) у `RingGameBootstrap.cs` («Изучение») і `RingFeudBootstrap.cs`
  (Кар'єра) — запитами після перебазування на основу Етапу 0.
- Поділ звуку між «Меню» і форком — рядком від них; ПМ дописує сюди.

**Черга Unity / Blender** (веде ПМ; оновлюється тут):
1. «Меню» — збірка без Development + офскрін-тури меню й кар'єри (1080p, 720p, 4:3, масштаб 120 %), ~30–40 хв — **іде
   з 08.11 вечора**;
2. Етап 0 — прохід компіляції основної гілки й коміт згенерованих `.meta` (інтегратор «Відео огляд» з «Изучение»);
3. «Скилы» — базовий прохід ботом трьох сезонів на основі Етапу 0.

**Передача:** правки переробки бою в `CareerContent.cs`, `CareerSession.cs`, `GimmickTests.cs`, `CareerKit.cs`
(гілка `claude/ring-combat-overhaul`) після злиття переходять у зону Кар'єри. У `b478385` до них додалось
`CareerSession.UniversalActions`: дії «Вийти з рингу», «Повернутися в ринг», «Стілець або стіл» (бій за рингом, §4.4).
Нові тести бою (`MatchOutsideTests`) — зона «ПМ + Бій» за шаблоном `Match*`.

## Етап 0 — «Одна версія»

1. Кожна сесія комітить поточну роботу на своїй гілці (незавершене — «WIP»). Першою — пачка «Изучение».
2. Інтегратор зливає в основну гілку, з тестами після кожного кроку:
   1. бриф `claude/ring-anim-brief-center` (01428d8);
   2. пачку «Изучение»;
   3. «Скилы» (`claude/ring-game-skills-0d696d`, 89ac15d + 931498e). Конфлікти в RingFx, RingStage3D,
      RingStageDemo і RingGameBootstrap — з підтвердженням «Изучение»; доступність — з меню (ca3a7c5), дублі
      прибрати.
3. Разовий розподіл `RingGameBootstrap` і `RingSceneBuilder` (вище).
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
| P1-1 утримання, P1-2 больовий, P1-3 фінішер → утримання | Бій (таймінг) + Кліпи | відкрито |
| P1-4 3D і журнал | Изучение (пачка); Бій тримає в новому режисері | у пачці |
| P1-5 вигляд розвитку, P3 екранів кар'єри | Меню (B03 v2) | відкрито |
| P1-6 популярність, P2-2 поради розвитку, P2-6 стать суперника | Кар'єра + рішення власника | відкрито |
| P1-7 велике шоу, зал за рівнем | Изучение | відкрито |
| P2-1 довжина матчу | Бій | відкрито |
| P2-5 закулісся | Изучение (3D) + Кар'єра (вміст) | відкрито |
| P3 HUD матчу (підкладка журналу, підказка без миші) | Бій | відкрито |
| P3 сцена («!», кабель, світло, роздягальня, модель у промо) | Изучение | частково в пачці |
| P3 геймпад | Меню | відкрито |
