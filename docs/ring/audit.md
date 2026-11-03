# Ринг — аудит проєкту перед зрізом «ворожнеча» (03.11.2026)

> Етап 0 доручення «пошагова RPG про реслінг-фьюд». Що вже є в репозиторії, що придатне повторно, що зламане.
> Мова — українська (власник 03.11.2026). Чинний дизайн зрізу — `docs/ring/gdd.md`; кар'єра в цілому — `docs/TRPG_DESIGN.md`.
> Аудит зроблено на гілці `claude/wrestling-rpg-design-d0b2f6` після злиття гілки майстерні (`152fa3c`), де лежить
> уся робота по «Рингу». Скорочення: `R` = `Assets/_Project/Scripts`, `T` = `Assets/Tests/EditMode`.

## 1. Стан коротко

- Проєкт — Unity 6000.4.10f1, URP; Blender 4.5 (`C:/Program Files/Blender Foundation/Blender 4.5`).
- Ядро `Game.Core` (`R/Core`, без UnityEngine) тестується без Unity: `tools/Alpha.Headless.sln`. Новий код у `R/Core/**`
  потрапляє в headless-збірку сам (glob); **мова ядра — C# 7.3, netstandard2.0** (без `record`, `init`, switch-виразів).
  Тести в `T/**` теж підхоплюються glob'ом; файли `R/Gameplay`, чисті від Unity, додаються в тестовий проєкт вручну.
- `Game.Gameplay` перевіряється лінтом збірки із заглушками UnityEngine/UIElements (`tools/Game.Gameplay.Lint`).
- `R/Core` містить стару гру (base-builder, ~264 файли тестів). Для «Рингу» в ядрі є лише `Core/Ring/RopeSim.cs`.
- Механік промо, ворожнечі й матчу рестлінгу в коді **немає** — лише дизайн (`docs/TRPG_DESIGN.md` §4–§7).

## 2. Придатне повторно

| Що | Шлях | Як використати в «Рингу» |
|---|---|---|
| Сценовий рушій діалогу: кроки Shot/Line/Beat/Effect/Choice/Goto, курсор, валідатор міток | `R/Core/Scenes/Scene.cs`, `ScenePlayback.cs`, `SceneValidator.cs`; тести `T/SceneTests.cs`, `ScenePlaybackTests.cs` | **Зразок будови** для графа промо (курсор `Next/Choose`, валідатор). Сам тип не беремо: він зшитий із міськими наслідками (`QuestConsequence`: Напруга, будівлі, лояльність) і `GameSession` |
| Умови доступу до варіанта | `R/Core/Quests/QuestStage.cs` (`GateFlag/BlockFlag/IsAvailable`) | Та сама ідея «відкрити / закрити гілку прапором» — у вузлах промо |
| Прапори сюжету + сейв-блок | `R/Core/Story/StoryFlags.cs`, `IStateBlob` (`R/Core/Loop/SettlementSave.cs`) | Патерн «блок стану → рядок сейву» для стану ворожнечі |
| Кубик із зерном | `R/Core/Randomness/IDiceRoller.cs`; реалізація `R/Gameplay/Combat/SeededDiceRoller.cs` (уже в headless-тестах) | **Беремо як є** для 2d6. Охоронець `ArchitectureGuardTests` забороняє `System.Random` і реалізацію `IDiceRoller` у ядрі — правило дотримуємо |
| Черга ходів | `R/Core/Combat/TurnSystem.cs` | Ідея раундів і першого ходу; у матчі рестлінгу двоє — черга простіша, пишемо свою |
| Колесо черги ходів (чиста модель) | `R/Gameplay/UI/Models/TurnWheelModel.cs` (7 тестів) | Індикатор «чий хід» на екрані матчу |
| Контракт «UI і бот ходять лише через інтерфейс» | `R/Gameplay/UI/BattleContract.cs` | Той самий патерн для матчу: екран і автотест кличуть одну модель |
| Діалогова камера (Wide/Close/OverShoulder/Two) | `R/Gameplay/UI/Models/DialogueShot.cs`, `DialogueStage.cs` | Промо на рингу (рішення §4.12 TRPG_DESIGN) — коли буде 3D-сцена |
| Бойова екшн-камера, сповільнення | `Models/BattleActionCamera.cs`, `BattleCameraEvents.cs`, `KillSlowMo.cs` | Великі прийоми й фінішер (рішення §4.13) — після 3D-сцени, потрібна адаптація від `BattleView` |
| Вікно діалогу UI Toolkit | `R/Gameplay/UI/Toolkit/DialogueToolkitView.cs` | Зразок стилю; екран промо пишемо свій (тип вузлів інший) |
| Шрифти Fixel / Noto Serif (OFL) | `Assets/ThirdParty/Fonts/` | Усі нові екрани |
| Значки game-icons (CC BY 3.0) | `Assets/ThirdParty/GameIcons/` | `choice`, `padlock` (закрита гілка), `ringing-bell` (гонг) |
| Звук CC0 (UI, удари, джингли, музика) | `Assets/ThirdParty/CC0/Audio/` (72 файли, `LICENSES.md`) | Клік вибору, удар (Kenney Impact), гонг/джингл перемоги |
| Арени рингу: спортзал, клубна зала, палац + якорі, тексти, сцени світла | `Assets/Art/Ring/venue_{gym,hall,arena}.fbx` і `*_anchors/texts/cues.txt`, `ring_strings_uk.txt` | Місце матчу й промо (3D-показ — після 3 ітерацій вигляду, рішення власника) |
| Борці (16 тіл, 8 культур × 2 статі) і рефері | `Assets/Art/Characters/Kit/body_*.fbx`, `Assets/Art/Ring/Wrestlers/` | Постаті матчу й промо |
| 187 + 187 кліпів рингу (Ч/Ж), мітки подій, кореневий рух | `Assets/Art/Animations/RING_Moves.fbx`, `RING_Moves_F.fbx`, `ring_anims_*_manifest.txt`, `*_events.txt`, `*_motion.txt` | Кожна дія матчу має кліп — див. таблицю в `docs/ring/gdd.md` §8 |
| Натовп і його анімації | `Assets/Art/Crowd/crowd_fans.fbx`, `CROWD_Anims.fbx` | Реакція публіки на ажіотаж |
| Ходьба й стійка (CC0) | `Assets/Art/Animations/UAL1_Standard.fbx`, `UAL2_Standard.fbx`; `R/Gameplay/FigureAnimation.cs` (Playables) | Закулісся (рішення §4.13) |
| Фізика канатів | `R/Core/Ring/RopeSim.cs`, `T/RopeSimTests.cs` | Відскок від канатів у 3D; у сцену ще не підключено |
| Генератори Blender | `tools/blender/ring_*.py`, `tools/blender/moves/*.json` | Нові ассети — тим самим конвеєром |

`.blend` у репозиторії немає й не має бути (правило треку V): геометрія й кліпи відтворюються скриптами.
Animator Controller'ів (`*.controller`) немає — анімація йде графом Playables (`FigureAnimation.cs`).

## 3. Механіки проєкту, схожі на механіки настільних ігор про реслінг

| Що є | Де | На що схоже |
|---|---|---|
| 2d6 + атрибут, три ступені 10+ / 7–9 / ≤6, шанс видно до кидка | `docs/TRPG_DESIGN.md` §4.1 (**рішення власника**, коду немає) | Ходи World Wide Wrestling RPG |
| Імпульс, ажіотаж, ворожнеча 0–4, популярність, довіра букера | `TRPG_DESIGN.md` §4.3, §6.2, §4.10 (пропозиції) | Momentum / Heat / Audience / Creative у World Wide Wrestling RPG |
| Зони рингу (центр, канати, кут, підлога) | `TRPG_DESIGN.md` §4.4 | Спецтаблиці канатів, кута й «поза рингом» у Legends of Wrestling; дії канатів і кута в RUMBLESLAM |
| Жетони реверсу, ескалація вирваного утримання | `TRPG_DESIGN.md` §4.6–4.7 | Карта Reverse у Wrasslin'; зростання рейтингу утримання в Legends of Wrestling |
| Полоси наслідку (Worst/Base/Good/Best), прев'ю порога | `R/Core/Checks` (`CheckResolver`, `OutcomeBand`) | Багатоступеневий результат — але детермінований; для 2d6 не підходить |
| Здача в бою | `R/Core/Combat/CombatState.cs` (`CanSurrender`) | Больовий і «здався» |
| Пул AP, ініціатива, стани (оглушений, збитий) | `R/Core/Combat` | Очки дій RUMBLESLAM; стани «оглушений / лежить» з Legends of Wrestling |
| Перки, трейти, очки розвитку | `R/Core/Characters/Perks`, `Progression/SpendablePoints` | Розвиток за шляхом — зразок, тип не беремо (зшитий з моделлю Е2) |

## 4. Зламане або непридатне

- **Стара гра повністю не потрібна «Рингу»:** `Core/Base`, `Settlement`, `Economy`, `Expeditions`, `Dungeons`, `World`,
  `Loop`, `Signals`, `Pressure`, міські екрани й `GameSession` (6640 рядків, «god class»). **Не видаляємо** — видалення
  чекає погодженого власником переліку (журнал рішень 18.10.2026). Новий код до старого не звертається.
- `R/Core/Combat/CombatState.cs` — сітковий тактичний бій (укриття, зброя, дозор); для пари борців не підходить як є.
- `ThresholdRule` / `PercentRule` — детермінований накопичувач; для 2d6 не потрібні.
- IMGUI-екрани (`SceneScreen`, `BattleHudScreen` тощо) — нових IMGUI не робимо (Поправка №17.4).
- `SceneValidator` не перевіряє досяжність гілок і тупики — для графа промо потрібен новий валідатор.
- `RopeSim` ніхто не використовує в `Gameplay` — не зламаний, просто не підключений.
- Кліпів **немає** для: больового, стрибка зі стійки, виходу з рингу й повернення, брудного прийому за спиною рефері,
  промо з мікрофоном. Реквізиту **немає**: ручного мікрофона.
- Звуків рингу (гонг, удар об мат, натовп, рефері) окремо немає — лише загальні Kenney Impact і джингли.

## 5. Висновок для зрізу

1. Промо — новий граф у `R/Core/Ring/Promo` за зразком `Scenes` (курсор, валідатор) і `QuestStage` (умови прапорами),
   дані — JSON + окремий файл рядків (написи лише ключами, правило власника 27.10.2026).
2. Стан ворожнечі — новий тип у `R/Core/Ring/Feud` з сейв-блоком за патерном `IStateBlob`.
3. Матч — новий рушій у `R/Core/Ring/Match`; кубик — наявний `IDiceRoller` + `SeededDiceRoller`.
4. Екрани — UI Toolkit у `R/Gameplay/Ring/UI`, окрема сцена без 3D-рингу (3D — після трьох ітерацій вигляду).
5. Нові ассети Blender — лише мікрофон і кліпи промо з мікрофоном (решти дій вистачає наявних кліпів, див. `docs/ring/gdd.md` §8).
