# Сюжетні прапори: хто пише, хто читає

> **Станом на 02.10.2026** (ROADMAP M1.1, `GDD_AMENDMENTS` Поправка №17 — «вибір → наслідок»). Не джерело істини, а ЗНІМОК аудиту.
> Актуальну таблицю друкує тест: `dotnet test tools/Game.Tests.Headless/Game.Tests.EditMode.csproj --filter "FullyQualifiedName~StoryFlagReaderGuardTests.PrintFlagTable" --logger "console;verbosity=detailed"` (він Explicit — запускається лише вручну). Номери рядків дрейфують; шукайте за символом.

## Що таке прапор і хто «читач»

Два НЕ перетинні простори: **story** (`StoryFlags`: пишуть `QuestConsequence.Flag(...)`, `flags.Set`, `flagsToSet:` в опціях данжу; читають `flags.Get`, `GateFlag`, `BlockFlag`) і **arc** (`_arcFlags` глав особистих арок: пише `SetsFlag`, читає `NeedsFlag`). Читач — продакшн-код, що міняє поведінку залежно від прапора; тест, що лише перевіряє «прапор виставлено», читачем не є.

Охоронець — `Assets/Tests/EditMode/StoryFlagReaderGuardTests.cs` (`EveryFlagHasAReader`): новий прапор без читача валить тест; прапор зі списку `KnownUnread`, що знайшов читача, теж (список не застоюється); id, схований за змінною, теж. Перевірено мутаціями: прибрати читача `zakhar_prepared_assault` / `hafiya_grass_found` / `NeedsFlag` арки, додати прапор-сироту, сховати id за змінною, дати читача прапору зі списку — кожне червонить свої тести.

## Підсумок

Усього прапорів, що виставляються: **34**. З читачем: **15**. **Без читача: 19** — із них 3 службових і 16 — вибори сцен/квестів/данжу, що не мають наслідку (задача M1.2). ROADMAP до аудиту називав 7–8 таких прапорів.

## Прапори без читача (борг M1.2)

| Простір | Прапор | Пишуть | Тип |
|---|---|---|---|
| arc | `arc_maksym_done` | Core/Companions/DefaultArcs.cs:34 | вибір без наслідку |
| arc | `arc_myroslava_done` | Core/Companions/DefaultArcs.cs:24 | вибір без наслідку |
| story | `abandoned_camp_grain_taken` | Core/Dungeons/DefaultDungeon.cs:88 | вибір без наслідку |
| story | `first_building.*` | Core/Scenes/OpeningScenes.cs:214 | службовий (прогрес/пам'ять) |
| story | `maksym_ch1_revenge_clean` | Core/Quests/DefaultQuests.cs:310 | вибір без наслідку |
| story | `maksym_ch2_forgive` | Core/Scenes/CompanionScenes.cs:92 | вибір без наслідку |
| story | `maksym_ch2_guard` | Core/Scenes/CompanionScenes.cs:95 | вибір без наслідку |
| story | `myroslava_asked` | Core/Scenes/OpeningScenes.cs:124; Core/Scenes/OpeningScenes.cs:125; Core/Scenes/OpeningScenes.cs:126; Core/Scenes/OpeningScenes.cs:127 | вибір без наслідку |
| story | `myroslava_ch2_remember` | Core/Scenes/CompanionScenes.cs:75 | вибір без наслідку |
| story | `myroslava_ch2_silence` | Core/Scenes/CompanionScenes.cs:78 | вибір без наслідку |
| story | `myroslava_checkup_reassure` | Core/Scenes/CompanionScenes.cs:152 | вибір без наслідку |
| story | `myroslava_checkup_space` | Core/Scenes/CompanionScenes.cs:155 | вибір без наслідку |
| story | `myroslava_hint` | Core/Scenes/OpeningScenes.cs:126; Core/Scenes/OpeningScenes.cs:127 | вибір без наслідку |
| story | `myroslava_sent_away` | Core/Scenes/CompanionScenes.cs:61 | вибір без наслідку |
| story | `myroslava_trusted` | Core/Scenes/CompanionScenes.cs:55 | вибір без наслідку |
| story | `myroslava_watched` | Core/Scenes/CompanionScenes.cs:58 | вибір без наслідку |
| story | `pass_vanguard_resolved` | Core/Story/PassVanguardOutcome.cs:128 | службовий (прогрес/пам'ять) |
| story | `tugar_offer_refused` | Core/Scenes/OpeningScenes.cs:112 | вибір без наслідку |
| story | `tugar_offer_seen` | Core/Session/GameSession.cs:675 | службовий (прогрес/пам'ять) |

## Повна таблиця

| Простір | Прапор | Пишуть | Читають |
|---|---|---|---|
| arc | `arc_maksym_ch1` | Core/Companions/DefaultArcs.cs:32 | Core/Companions/DefaultArcs.cs:34 |
| arc | `arc_maksym_done` | Core/Companions/DefaultArcs.cs:34 | **НЕМАЄ** |
| arc | `arc_myroslava_ch1` | Core/Companions/DefaultArcs.cs:22 | Core/Companions/DefaultArcs.cs:24 |
| arc | `arc_myroslava_done` | Core/Companions/DefaultArcs.cs:24 | **НЕМАЄ** |
| story | `abandoned_camp_grain_taken` | Core/Dungeons/DefaultDungeon.cs:88 | **НЕМАЄ** |
| story | `defector_seeded` | Core/Story/PassVanguardOutcome.cs:129 | Core/Session/GameSession.cs:1528; Core/Session/GameSession.cs:4283; Core/Session/GameSession.cs:4461 |
| story | `first_building.*` | Core/Scenes/OpeningScenes.cs:214 | **НЕМАЄ** |
| story | `hafiya_grass_found` | Core/Quests/DefaultQuests.cs:75; Core/Quests/DefaultQuests.cs:76 | Core/Session/GameSession.cs:5037 |
| story | `maksym_ch1_revenge_clean` | Core/Quests/DefaultQuests.cs:310 | **НЕМАЄ** |
| story | `maksym_ch2_forgive` | Core/Scenes/CompanionScenes.cs:92 | **НЕМАЄ** |
| story | `maksym_ch2_guard` | Core/Scenes/CompanionScenes.cs:95 | **НЕМАЄ** |
| story | `myroslava_asked` | Core/Scenes/OpeningScenes.cs:124; Core/Scenes/OpeningScenes.cs:125; Core/Scenes/OpeningScenes.cs:126; Core/Scenes/OpeningScenes.cs:127 | **НЕМАЄ** |
| story | `myroslava_ch2_remember` | Core/Scenes/CompanionScenes.cs:75 | **НЕМАЄ** |
| story | `myroslava_ch2_silence` | Core/Scenes/CompanionScenes.cs:78 | **НЕМАЄ** |
| story | `myroslava_checkup_reassure` | Core/Scenes/CompanionScenes.cs:152 | **НЕМАЄ** |
| story | `myroslava_checkup_space` | Core/Scenes/CompanionScenes.cs:155 | **НЕМАЄ** |
| story | `myroslava_confrontation_resolved` | Core/Session/GameSession.cs:4459 | Core/Session/GameSession.cs:4296; Core/Session/GameSession.cs:4453 |
| story | `myroslava_confronted_failed` | Core/Scenes/CompanionScenes.cs:111; Core/Scenes/CompanionScenes.cs:112 | Core/Session/GameSession.cs:4985 |
| story | `myroslava_confronted_provoked` | Core/Scenes/CompanionScenes.cs:118; Core/Scenes/CompanionScenes.cs:119; Core/Scenes/CompanionScenes.cs:120; Core/Scenes/CompanionScenes.cs:121 | Core/Session/GameSession.cs:4983 |
| story | `myroslava_confronted_release` | Core/Scenes/CompanionScenes.cs:138 | Core/Session/GameSession.cs:1537; Core/Session/GameSession.cs:4984 |
| story | `myroslava_confronted_trust` | Core/Scenes/CompanionScenes.cs:113; Core/Scenes/CompanionScenes.cs:114 | Core/Session/GameSession.cs:1526 |
| story | `myroslava_defection_executed` | Core/Session/GameSession.cs:4992 | Core/Session/GameSession.cs:4980 |
| story | `myroslava_hint` | Core/Scenes/OpeningScenes.cs:126; Core/Scenes/OpeningScenes.cs:127 | **НЕМАЄ** |
| story | `myroslava_sent_away` | Core/Scenes/CompanionScenes.cs:61 | **НЕМАЄ** |
| story | `myroslava_trusted` | Core/Scenes/CompanionScenes.cs:55 | **НЕМАЄ** |
| story | `myroslava_watched` | Core/Scenes/CompanionScenes.cs:58 | **НЕМАЄ** |
| story | `pass_vanguard_resolved` | Core/Story/PassVanguardOutcome.cs:128 | **НЕМАЄ** |
| story | `tugar_bargained_time` | Core/Scenes/OpeningScenes.cs:118; Core/Scenes/OpeningScenes.cs:119 | Core/Session/GameSession.cs:4953 |
| story | `tugar_offer_refused` | Core/Scenes/OpeningScenes.cs:112 | **НЕМАЄ** |
| story | `tugar_offer_seen` | Core/Session/GameSession.cs:675 | **НЕМАЄ** |
| story | `zakhar_council_done` | Core/Session/GameSession.cs:4480 | Core/Session/GameSession.cs:4477 |
| story | `zakhar_dam_bonus_applied` | Core/Session/GameSession.cs:5015 | Core/Session/GameSession.cs:5012 |
| story | `zakhar_prepared_assault` | Core/Scenes/CompanionScenes.cs:172 | Core/Session/GameSession.cs:1544 |
| story | `zakhar_prepared_dam` | Core/Scenes/CompanionScenes.cs:169 | Core/Session/GameSession.cs:5012 |

## Незалежна перевірка і межі охоронця

Охоронець написано як статичний сканер, а цифри звірено з **незалежним ручним аудитом трьох агентів** (по зонах «сцени/арки», «квести/данжі» і адверсарним, що шукав пастки). Результат збігся без розбіжностей: **34 прапори (30 story + 4 arc), 15 з читачем, 19 без**. Адверсарний аудит знайшов те, що підрахунок не показує; частину закрито в коді, решта — борг:

**Закрито в охоронці (02.10.2026):**
- читання прапора, якого ніхто не виставляє (одруківка в id) — тест `EveryReadFlag_IsWrittenSomewhere_NoTypos`;
- константа-id, оголошена двічі з різними значеннями, — `FlagConstants_AreNotDeclaredTwiceWithDifferentValues`; сам дубль `DefectorSeededFlag` (було в `Defection` і `PassVanguardOutcome`) прибрано: друга тепер посилається на першу;
- новий держатель `StoryFlags` під іменем, якого сканер не знає, — `StoryFlagsHolders_AreAllKnownToTheScanner`.

**Лишається боргом (не міняє підрахунку, але вводить в оману):**
1. **`SceneValidator` вважає голий прапор наслідком** (`QuestConsequence.IsEmpty` рахує `Flags`, `SceneValidator.cs` ≈186–191), тож перевірка «вибір без ефекту» (Поправка №7.4) пропускає варіанти, чий єдиний наслідок — прапор без читача: `watch` (`CompanionScenes.cs` ≈58), `space` (≈155), базова смуга `ask_myroslava` (`OpeningScenes.cs` ≈125), найкраща смуга `revenge_check` (`DefaultQuests.cs` ≈310). Після M1.2 це закриється само, але валідатор варто навчити рахувати прапор наслідком, лише якщо в нього є читач.
2. **«Читачі-самоохоронці»** рахуються читачами, хоча це не наслідки вибору, а маркери ідемпотентності: `zakhar_council_done`, `zakhar_dam_bonus_applied`, `myroslava_defection_executed` (останнього читання ще й зайве: `Status == Antagonist` уже перевіряється поруч).
3. **`arc_*_ch1`** зараховуються як прочитані на місці `NeedsFlag` у даних; сама перевірка в `CompanionArc.Refresh` тавтологічна (наступна глава стає поточною лише після `CompleteChapter`, який ставить цей прапор).
4. **`defector_seeded`**: читач глобальний (`Defection.ShouldDefect` застосовує його до будь-якого напарника зі смугою ≤ Resentful), прапор ніколи не знімається, хоча коментар у `GameSession` ≈4294 каже «прапор знято».
5. **Читачі фінального плану** (`BuildFinalePlan`) діють лише на кривавому шляху фіналу; на тихому вони нічого не міняють. При найвищій готовності друге пом'якшення стає порожнім ходом (гейт `Count > 1`).
6. **Документація бреше про прапори тестової збірки:** `hafiya_quest_active`, `hafiya_quest_stage`, `crisis_test_mitigated` описані в `TEST_BUILD.md` ≈561–563, але в продакшн-коді не виставляються (прапор Гафії називається `hafiya_grass_found`).
7. **Смуга Best для Persuade недосяжна** (`CheckResolver.cs` ≈99–100), тож четвертий варіант наслідку в `ask_myroslava` і persuade-конфронтації мертвий.
8. Дрібниця: `GameSession.CaptureArcState` перебирає `HashSet` без сортування (`StoryFlags.CaptureState` сортує) — для детермінізму зліпка бажано відсортувати.

## Як закривати прогалину (M1.2)

Для кожного прапора з розділу «без читача» — або **споживач** (код, що читає прапор і міняє щось видиме гравцю: репліку напарника, рядок підсумку/епілогу, ціну чи поріг перевірки, доступність варіанта) разом із тестом «прапор змінює X» і мутацією, або **зняти прапор** (прибрати `.Flag(...)`), якщо вибір уже має наслідок іншим шляхом (як `tugar_offer_refused`: фракції зсунуто напряму). Після закриття прибрати запис із `KnownUnread` — інакше `KnownGaps_AreStillGaps_NoStaleEntries` запротестує.
