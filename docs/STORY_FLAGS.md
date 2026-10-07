# Сюжетні прапори: хто пише, хто читає

> **Станом на 07.10.2026** (ROADMAP M1.1 → **M1.2 закрито**, `GDD_AMENDMENTS` Поправка №17 — «вибір → наслідок»). Не джерело істини, а ЗНІМОК аудиту.
> Актуальну таблицю друкує тест: `dotnet test tools/Game.Tests.Headless/Game.Tests.EditMode.csproj --filter "FullyQualifiedName~StoryFlagReaderGuardTests.PrintFlagTable" --logger "console;verbosity=detailed"` (він Explicit — запускається лише вручну). Номери рядків дрейфують; шукайте за символом.

## Що таке прапор і хто «читач»

Два НЕ перетинні простори: **story** (`StoryFlags`: пишуть `QuestConsequence.Flag(...)`, `flags.Set`, `flagsToSet:` в опціях данжу; читають `flags.Get`, `GateFlag`, `BlockFlag`) і **arc** (`_arcFlags` глав особистих арок: пише `SetsFlag`, читає `NeedsFlag`). Читач — продакшн-код, що міняє поведінку залежно від прапора; тест, що лише перевіряє «прапор виставлено», читачем не є.

Охоронець — `Assets/Tests/EditMode/StoryFlagReaderGuardTests.cs` (`EveryFlagHasAReader`): новий прапор без читача валить тест; прапор зі списку `KnownUnread`, що знайшов читача, теж (список не застоюється); id, схований за змінною, теж. **З M1.2 `KnownUnread` порожній — борг не повернеться мовчки.** Перевірено мутаціями: прибрати читача `zakhar_prepared_assault` / `hafiya_grass_found` / `NeedsFlag` арки, додати прапор-сироту, сховати id за змінною, дати читача прапору зі списку — кожне червонить свої тести.

## Підсумок

Усього прапорів, що виставляються: **29** (27 story + 2 arc), **усі з читачем**. Аудит M1.1 (02.10.2026) знайшов 34 прапори й **19 без читача** — 3 службових і 16 виборів сцен/квестів/данжу без наслідку. M1.2 (07.10.2026) закрила всі 19: **14 отримали споживача, 5 знято** (таблиця нижче). Тести — `Assets/Tests/EditMode/StoryFlagConsumersTests.cs` (33 тести; кожен читач перевірено мутацією — прибрати читача, тест червоніє).

## Закриття M1.2: прапор → споживач або знято

Два види споживачів. **Числові** міняють те, що гравець бачить у прев'ю заздалегідь (інваріант 8), без нових шкал (інваріант 6) і драйверів Напруги (інваріант 5), детерміновано (інваріант 1). **Відлуння** — рядки підсумку доби 5 «Що громада запам'ятала» (`StoryEchoes`, `SummaryView.Echoes`, тексти `summary.echo.*` у `UkrainianText.AddStoryEchoKeys`): чиста функція від прапорів, порядок Мирослава → Максим → світ.

| Прапор | Було | Тепер |
|---|---|---|
| `myroslava_trusted` | вибір без наслідку | **поріг:** нічна розмова доби 3 — «переконати» легше на 1 (`CompanionScenes.ConfrontationThresholds`, передає `OfferMyroslavaEveningScene`); + рядок підсумку |
| `myroslava_watched` | вибір без наслідку | **поріг:** нічна розмова — «звинуватити» (Залякування) легше на 1; + рядок підсумку |
| `myroslava_sent_away` | вибір без наслідку | **поріг:** нічна розмова — «переконати» важче на 1; + рядок підсумку |
| `myroslava_hint` | вибір без наслідку | **поріг:** тихий шлях вузла 1 легший на 1 (`GameSession.ApplyMyroslavaHintBonusIfNeeded`, той самий прийом, що торг; живе в зліпку); + рядок підсумку |
| `myroslava_asked` | вибір без наслідку | рядок підсумку «питав, вона відмовчалась» (коли підказки не було) |
| `myroslava_checkup_reassure` / `_space` | вибір без наслідку | рядок підсумку (тиха розмова доби 3) |
| `myroslava_ch2_remember` / `_silence` | вибір без наслідку | рядок підсумку, лише коли арку Мирослави завершено |
| `maksym_ch1_revenge_clean` | вибір без наслідку | рядок підсумку (найкраща смуга «помсти») |
| `maksym_ch2_forgive` / `_guard` | вибір без наслідку | рядок підсумку, лише коли арку Максима завершено |
| `abandoned_camp_grain_taken` | вибір без наслідку | рядок підсумку (подія «жадібно» в таборі) |
| `pass_vanguard_resolved` | службовий, без читача | **читає сам `PassVanguardOutcome.Apply`:** другий виклик не ранить і не грабує вдруге (ідемпотентність, яку обіцяв коментар) |
| `arc_myroslava_done` / `arc_maksym_done` | вибір без наслідку | **знято:** дублювали `ArcState.Completed`. Споживач — стан арки (`GameSession.IsArcCompleted`): хто завершив арку, **не зраджує** (`TickDefectionWatch`), а Мирослава в такому разі **не ворог у фіналі** (`BuildFinalePlan`) |
| `tugar_offer_refused` | вибір без наслідку | **знято:** наслідок відмови вже в фракціях (`community +10`, `tuhar_boyars −10`) |
| `tugar_offer_seen` | службовий | **знято:** ставився на переході сцени, нічого не означав |
| `first_building.*` | службовий | **знято:** обрана будівля вже в `CityWorks.Built`, видна в місті й підсумку |

## Повна таблиця (стан після M1.2)

| Простір | Прапор | Пишуть | Читають |
|---|---|---|---|
| arc | `arc_maksym_ch1` | Core/Companions/DefaultArcs.cs:37 | Core/Companions/DefaultArcs.cs:39 |
| arc | `arc_myroslava_ch1` | Core/Companions/DefaultArcs.cs:27 | Core/Companions/DefaultArcs.cs:29 |
| story | `abandoned_camp_grain_taken` | Core/Dungeons/DefaultDungeon.cs:94 | Core/Session/StoryEchoes.cs:88 |
| story | `defector_seeded` | Core/Story/PassVanguardOutcome.cs:140 | Core/Session/GameSession.cs:1734; Core/Session/GameSession.cs:4496; Core/Session/GameSession.cs:4684 |
| story | `hafiya_grass_found` | Core/Quests/DefaultQuests.cs:75; Core/Quests/DefaultQuests.cs:76 | Core/Session/GameSession.cs:5297 |
| story | `maksym_ch1_revenge_clean` | Core/Quests/DefaultQuests.cs:316 | Core/Session/StoryEchoes.cs:80 |
| story | `maksym_ch2_forgive` | Core/Scenes/CompanionScenes.cs:100 | Core/Session/StoryEchoes.cs:83 |
| story | `maksym_ch2_guard` | Core/Scenes/CompanionScenes.cs:103 | Core/Session/StoryEchoes.cs:84 |
| story | `myroslava_asked` | Core/Scenes/OpeningScenes.cs:135; Core/Scenes/OpeningScenes.cs:136; Core/Scenes/OpeningScenes.cs:137; Core/Scenes/OpeningScenes.cs:138 | Core/Session/StoryEchoes.cs:61 |
| story | `myroslava_ch2_remember` | Core/Scenes/CompanionScenes.cs:83 | Core/Session/StoryEchoes.cs:75 |
| story | `myroslava_ch2_silence` | Core/Scenes/CompanionScenes.cs:86 | Core/Session/StoryEchoes.cs:76 |
| story | `myroslava_checkup_reassure` | Core/Scenes/CompanionScenes.cs:185 | Core/Session/StoryEchoes.cs:69 |
| story | `myroslava_checkup_space` | Core/Scenes/CompanionScenes.cs:188 | Core/Session/StoryEchoes.cs:70 |
| story | `myroslava_confrontation_resolved` | Core/Session/GameSession.cs:4682 | Core/Session/GameSession.cs:4509; Core/Session/GameSession.cs:4676 |
| story | `myroslava_confronted_failed` | Core/Scenes/CompanionScenes.cs:124; Core/Scenes/CompanionScenes.cs:125 | Core/Session/GameSession.cs:5245 |
| story | `myroslava_confronted_provoked` | Core/Scenes/CompanionScenes.cs:131; Core/Scenes/CompanionScenes.cs:132; Core/Scenes/CompanionScenes.cs:133; Core/Scenes/CompanionScenes.cs:134 | Core/Session/GameSession.cs:5243 |
| story | `myroslava_confronted_release` | Core/Scenes/CompanionScenes.cs:151 | Core/Session/GameSession.cs:1743; Core/Session/GameSession.cs:5244 |
| story | `myroslava_confronted_trust` | Core/Scenes/CompanionScenes.cs:126; Core/Scenes/CompanionScenes.cs:127 | Core/Session/GameSession.cs:1732 |
| story | `myroslava_defection_executed` | Core/Session/GameSession.cs:5252 | Core/Session/GameSession.cs:5240 |
| story | `myroslava_hint` | Core/Scenes/OpeningScenes.cs:137; Core/Scenes/OpeningScenes.cs:138 | Core/Session/GameSession.cs:5213; Core/Session/StoryEchoes.cs:60 |
| story | `myroslava_sent_away` | Core/Scenes/CompanionScenes.cs:69 | Core/Session/GameSession.cs:4692; Core/Session/StoryEchoes.cs:66 |
| story | `myroslava_trusted` | Core/Scenes/CompanionScenes.cs:63 | Core/Session/GameSession.cs:4690; Core/Session/StoryEchoes.cs:64 |
| story | `myroslava_watched` | Core/Scenes/CompanionScenes.cs:66 | Core/Session/GameSession.cs:4691; Core/Session/StoryEchoes.cs:65 |
| story | `pass_vanguard_resolved` | Core/Story/PassVanguardOutcome.cs:139 | Core/Story/PassVanguardOutcome.cs:121 |
| story | `tugar_bargained_time` | Core/Scenes/OpeningScenes.cs:129; Core/Scenes/OpeningScenes.cs:130 | Core/Session/GameSession.cs:5183 |
| story | `zakhar_council_done` | Core/Session/GameSession.cs:4709 | Core/Session/GameSession.cs:4706 |
| story | `zakhar_dam_bonus_applied` | Core/Session/GameSession.cs:5275 | Core/Session/GameSession.cs:5272 |
| story | `zakhar_prepared_assault` | Core/Scenes/CompanionScenes.cs:205 | Core/Session/GameSession.cs:1750 |
| story | `zakhar_prepared_dam` | Core/Scenes/CompanionScenes.cs:202 | Core/Session/GameSession.cs:5272 |

## Незалежна перевірка і межі охоронця

Охоронець написано як статичний сканер, а цифри звірено з **незалежним ручним аудитом трьох агентів** (по зонах «сцени/арки», «квести/данжі» і адверсарним, що шукав пастки). Результат збігся без розбіжностей: **34 прапори (30 story + 4 arc), 15 з читачем, 19 без**. Адверсарний аудит знайшов те, що підрахунок не показує; частину закрито в коді, решта — борг:

**Закрито в охоронці (02.10.2026):**
- читання прапора, якого ніхто не виставляє (одруківка в id) — тест `EveryReadFlag_IsWrittenSomewhere_NoTypos`;
- константа-id, оголошена двічі з різними значеннями, — `FlagConstants_AreNotDeclaredTwiceWithDifferentValues`; сам дубль `DefectorSeededFlag` (було в `Defection` і `PassVanguardOutcome`) прибрано: друга тепер посилається на першу;
- новий держатель `StoryFlags` під іменем, якого сканер не знає, — `StoryFlagsHolders_AreAllKnownToTheScanner`.

**Лишається боргом (не міняє підрахунку, але вводить в оману):**
1. **`SceneValidator` вважає голий прапор наслідком** (`QuestConsequence.IsEmpty` рахує `Flags`, `SceneValidator.cs` ≈186–191), тож перевірка «вибір без ефекту» (Поправка №7.4) пропускає варіанти, чий єдиний наслідок — прапор без читача: `watch` (`CompanionScenes.cs` ≈58), `space` (≈155), базова смуга `ask_myroslava` (`OpeningScenes.cs` ≈125), найкраща смуга `revenge_check` (`DefaultQuests.cs` ≈310). M1.2 закрила всі такі прапори читачами, тож зараз перевірка не пропускає жодного «порожнього» варіанта; але валідатор і далі не знає про читачів — його варто навчити рахувати прапор наслідком, лише якщо в нього є читач.
2. **«Читачі-самоохоронці»** рахуються читачами, хоча це не наслідки вибору, а маркери ідемпотентності: `zakhar_council_done`, `zakhar_dam_bonus_applied`, `myroslava_defection_executed` (останнього читання ще й зайве: `Status == Antagonist` уже перевіряється поруч) і, з M1.2, `pass_vanguard_resolved` (його читає сам `PassVanguardOutcome.Apply`).
3. **`arc_*_ch1`** зараховуються як прочитані на місці `NeedsFlag` у даних; сама перевірка в `CompanionArc.Refresh` тавтологічна (наступна глава стає поточною лише після `CompleteChapter`, який ставить цей прапор). Прапори `arc_*_done` у M1.2 знято саме через це: стан арки (`ArcState.Completed`) каже те саме без окремого прапора.
4. **`defector_seeded`**: читач глобальний (`Defection.ShouldDefect` застосовує його до будь-якого напарника зі смугою ≤ Resentful), прапор ніколи не знімається, хоча коментар у `GameSession` ≈4294 каже «прапор знято».
5. **Читачі фінального плану** (`BuildFinalePlan`) діють лише на кривавому шляху фіналу; на тихому вони нічого не міняють. При найвищій готовності друге пом'якшення стає порожнім ходом (гейт `Count > 1`).
6. **Документація бреше про прапори тестової збірки:** `hafiya_quest_active`, `hafiya_quest_stage`, `crisis_test_mitigated` описані в `TEST_BUILD.md` ≈561–563, але в продакшн-коді не виставляються (прапор Гафії називається `hafiya_grass_found`).
7. **Смуга Best для Persuade недосяжна** (`CheckResolver.cs` ≈99–100), тож четвертий варіант наслідку в `ask_myroslava` і persuade-конфронтації мертвий.
8. Дрібниця: `GameSession.CaptureArcState` перебирає `HashSet` без сортування (`StoryFlags.CaptureState` сортує) — для детермінізму зліпка бажано відсортувати.

## Як тримати прапори чистими далі

Новий прапор без читача валить `EveryWrittenFlag_HasAReader_OrIsAKnownGap`; у `KnownUnread` нічого не вносимо — або **споживач** (код, що читає прапор і міняє щось видиме гравцю: репліку, рядок підсумку, ціну чи поріг перевірки, доступність варіанта) разом із тестом «прапор змінює X» і мутацією, або не ставити прапор (як `tugar_offer_refused`: фракції зсунуто напряму). Для «що громада запам'ятала» достатньо додати рядок у `StoryEchoes` (+ ключ у `UkrainianText.AddStoryEchoKeys`).
