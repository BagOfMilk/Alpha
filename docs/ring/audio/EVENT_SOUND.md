# «Ринг» — таблиця «подія → звук»

Критерій М1 (ПМ, 08.11.2026): **«таблиця «подія → звук» без порожніх подій (удар, гонг, рахунок, натовп, QTE, вихід)»**.
Таблиця в коді — `Gameplay/Ring/Models/RingSoundMap.cs`; грає `Stage/Unity/RingSound.cs`. **Звучить лише схвалене власником**
у бібліотеці звуку (`docs/ring/audio/SOUND_APPROVALS.md`): доки слот не схвалено, подія мовчить, хоч рядок у таблиці є.
Охоронець — `RingSoundMapTests`. Він перевіряє:
- кожне значення `MatchEventKind` і `MatchActionId` має звук;
- кожен наслідок `MatchEffectKind` розглянуто;
- кожен звук має слоти, а слоти є у файлах сцени або в бібліотеці;
- цей документ не відстає від коду.

Таблиця ключована **назвами** подій, дій і наслідків. Тому вона працює й з основною гілкою, й з переробкою бою
(`claude/ring-combat-overhaul`). Там з'являються:
- подія `Flourish`;
- дія `Weapon`;
- наслідок `Place`;
- поле `MatchEvent.Outside`.

`IntoRopes` і `IntoCorner` там зникають. Поле `Outside` таблиця читає за назвою (`RingSoundMap.OutsideOf`): до злиття
переробки його немає, і пара вважається на рингу.

Звук іде за тим, що **видно на екрані** (`Stage/RingClipTable`). Удар із провалом — суперник ухиляється, тож удару по тілу
немає. Предмет — за `docs/ring/CLIP_NEEDS.md` і TRPG_DESIGN §4.4 (відповідь ПМ 08.11.2026; примітки `RingClipTable` виправлено в
77688e6 на `claude/ring-combat-overhaul`).

## Звуки й слоти бібліотеки

| Звук (`RingCue`) | Слоти бібліотеки | Звідки файл | Коли звучить |
|---|---|---|---|
| Hit — удар по тілу | `hit_01`, `hit_02`, `hit_03`, `hit_04` (по колу) | набір сцени (CC0) | удари, брудні й відчайдушні прийоми |
| Slam — падіння на мат | `slam_01`, `slam_02`, `slam_03`, `slam_04`, `slam_05` (по колу) | набір сцени (CC0) | кидки, фінішер, промінь надії, реверс |
| Land — приземлення | `land` | набір сцени (CC0) | ризикований трюк (стрибок з канатів чи кута) |
| MatSlap — рахунок рефері | `mat_slap` | набір сцени (CC0) | долоня об мат на «раз» і «два» (мітки кліпа рефері), «три» на утриманні, два удари на кікауті |
| Lockup — обмін | `lockup` | новий слот | обмін на старті й після реверсу 7–9 |
| PinCover — утримання | `pin_cover` | новий слот | дія «утримання» (накриття) |
| HoldStrain — больовий | `hold_strain` | новий слот | больовий і витримка больового |
| RopeRebound — канати | `rope_rebound` | новий слот | кидок на канати (у переробці бою — склейка канатів) |
| CornerHit — кут | `corner_hit` | новий слот | кидок у кут |
| FloorThud — за рингом | `floor_thud` | новий слот | вихід за ринг і повернення |
| CanvasShuffle — підйом | `canvas_shuffle` | новий слот | підвестися, відновитися, перевести подих |
| Pop — вибух залу | `applause_hall` | набір сцени (CC0) | розв'язка відліку (режисер), виклик залу, допомога союзника, порятунок |
| CrowdBed — гул залу | `crowd_shouting` (петля) | набір сцени (CC0) | постійно під час шоу; гучність — за ажіотажем (0…3), за лаштунками — тиша |
| BellStart — дзвін початку | `bell_01` | набір сцени (CC0) | перша подія матчу |
| BellEnd — дзвін кінця | `bell_02` (тричі через 0,32 с) | набір сцени (CC0) | кінець матчу |
| Entrance — удар виходу | `gong_01` | набір сцени (CC0) | старт музики виходу борця |
| QtePrompt — QTE з'явилось | `qte_prompt` | новий слот | режисер матчу: `RingSound.Qte(RingQte.Prompt)` |
| QteHit — вдале QTE | `qte_hit` | новий слот | подія `Flourish` (переробка бою: «лише видовище», +1 ажіотажу) |
| QteMiss — QTE пропущено | `qte_miss` | новий слот | режисер матчу: `RingSound.Qte(RingQte.Miss)` |
| Whiff — промах | `whiff` | новий слот | удар чи брудний прийом із провалом: суперник ухилився, контакту немає; предмет ≤6 — замах повз |
| BarrierHit — удар у бар'єр | `barrier_hit` | новий слот | бій за рингом: удар відкидає суперника на сталевий бар'єр |
| BarrierSlam — кидок у бар'єр | `barrier_slam` | новий слот | бій за рингом: кидок у бар'єр; предмет ≤6 — другий дотик: нападник після замаху сам у бар'єр |
| WeaponChair — стілець | `weapon_chair` | новий слот | предмет 10+, якщо режисер обрав стілець; предмет 7–9 — стілець по дотичній |
| WeaponTable — стіл | `weapon_table` | новий слот | предмет 10+, якщо режисер обрав стіл: кидок на стіл, стіл ламається |
| CountOut — відлік за рингом | `count_out` | новий слот | наслідок `Count`: по звуку на цифру (до трьох за дію) |
| CrowdSurge — зал здіймається | `crowd_surge` | новий слот | наслідок `Place`: склейка — пара вилетіла за ринг чи повернулась |

Музика не входить у таблицю подій, вона має свої слоти бібліотеки: титул і вступ — `music_rock_theme_loop`; виходи — `music_rock_city_ransom`
(герой), `music_hammer_of_the_berserker` (Молот), `music_downtown_destruction` (федерація); меню — `music_login`, `music_loading`,
`music_settings` (`RingMenuAudio`).

Нові слоти лежать у грі в `Assets/Art/Ring/Sound/Resources/RingSound/<слот>.wav`; `RingSound` бере їх із Resources. Поки
власник слот не схвалив, файлу там немає.

## Події матчу (`MatchEventKind`)

| Подія | Звук | Примітка |
|---|---|---|
| `Exchange` | Lockup | |
| `Action` | за дією (таблиця нижче) | |
| `Sell` | CrowdBed | продаж іде до кидка прийому; удар звучить на самій дії. До 08.11.2026 удар звучав двічі, і перший раз ще до контакту |
| `Reverse` | Slam | |
| `Kickout` | MatSlap ×2, потім Pop | «раз-два» на мітках рефері, вибух залу на кікауті |
| `Endure` | HoldStrain | витримка больового |
| `Rescue` | Pop | союзник рятує |
| `TurnEnd` | CrowdBed | ініціатива переходить; окремого удару немає, звучить гул залу |
| `MatchEnd` | BellEnd (+ MatSlap ×3 на утриманні) | кінець відліком (`CountOut`) чи подвійним відліком (`DoubleCountOut`): останні цифри CountOut звучать на дії, далі дзвін |
| `Flourish` | QteHit | лише в переробці бою |

## Дії (`MatchActionId`, подія `Action`)

Ступінь кидка і місце пари уточнюють звук. Нижче — «на рингу, влучив», далі — винятки.

| Дія | Звук |
|---|---|
| `Strike` | Hit; провал — Whiff; за рингом — BarrierHit |
| `Dirty` | Hit; провал — Whiff |
| `Desperate` | Hit; провал — Slam (сам падає) |
| `Throw` | Slam; за рингом — BarrierSlam |
| `Finisher` | Slam (і спалах, сповільнення 0,4× у режисера) |
| `HopeSpot` | Slam |
| `TopRope` | Land |
| `IntoRopes` | RopeRebound (лише основна гілка) |
| `IntoCorner` | CornerHit (лише основна гілка) |
| `Taunt` | Pop |
| `Submission` | HoldStrain |
| `Pin` | PinCover, далі MatSlap на рахунок |
| `GetUp` | CanvasShuffle |
| `Recover` | CanvasShuffle |
| `CatchBreath` | CanvasShuffle |
| `LeaveRing` | FloorThud; провал — Lockup (суперник утримався) |
| `ReturnToRing` | CanvasShuffle (закотив суперника й заліз сам) |
| `Ally` | Pop |
| `Weapon` | 10+ — WeaponChair чи WeaponTable (реквізит режисера); 7–9 — WeaponChair, потім Hit; ≤6 — Whiff, потім BarrierSlam (лише переробка бою) |

## Бій за рингом і наслідки (`MatchEffectKind`)

Бій за рингом — склейкою, рішення власника 08.11.2026 («Лишити в ітерації 1 склейкою»). Запит ПМ 08.11.2026.

| Що | Звук | Як |
|---|---|---|
| `Outside` = true, `Strike` | BarrierHit | замість Hit |
| `Outside` = true, `Throw` | BarrierSlam | замість Slam |
| `Weapon` 10+ | WeaponChair або WeaponTable | чистий удар; реквізит обирає режисер (чергує) і передає явно: `OnEvent(ev, delay, pinEnd, RingProp.Table)`; без нього — стілець |
| `Weapon` 7–9 | WeaponChair, потім Hit | стілець по дотичній; самоудар — другий дотик: режисер кличе `RingSound.FollowUp(ev)` на мітці кліпа |
| `Weapon` ≤6 | Whiff, потім BarrierSlam | замах повз; нападник сам у бар'єр — другий дотик, `RingSound.FollowUp(ev)` на мітці |
| наслідок `Place` | CrowdSurge | через 0,35 с після контакту дії, коли кадр уже на новому місці; і вихід, і повернення |
| наслідок `Count` | CountOut × Amount (не більше 3) | через 0,7 с після контакту, по 0,6 с між цифрами |
| кінець `DoubleCountOut` / `CountOut` | CountOut на дії, далі BellEnd | окремого звуку кінця немає |

Інші наслідки звучать дією, що їх спричинила. Власного звуку в них немає свідомо: `Damage`, `Momentum`, `Hype`, `Down`, `Stand`,
`Stunned`, `Warning`, `RefereeDistracted`, `Token`, `Stamina`; основна гілка — `ZoneChange`, `ApChange`.

Другий дотик предмета звучить лише на мітці кліпа: без мітки краще тиша, ніж звук не в такт показу. Хто і де кличе
`FollowUp` — зона режисера (Ф2).

**У переробці бою не грають** слоти `rope_rebound` і `corner_hit`: дій `IntoRopes`/`IntoCorner` там немає. Прийом «з канатів»
показується склейкою (`MoveStaging.Ropes`/`Corner`). Кличе режисер (прийнято ПМ 08.11.2026, Ф2): на кроці `Cut` зі `Staging=Ropes` —
`RingSound.Play(RingCue.RopeRebound)` у мить, коли нападник стає на канат; зі `Staging=Corner` — `CornerHit` на контакті жертви з кутом.

## Поза матчем

| Момент | Звук | Хто кличе |
|---|---|---|
| Вихід борця | музика виходу + Entrance (`gong_01`) | `RingStage3D` → `RingSound.EntranceMusic` |
| Титул і вступ | музика меню (`RingMenuAudio`) або `music_rock_theme_loop` | меню / `RingSound.TitleMusic` |
| Закулісся | тиша залу (`Crowd(-1)`) | `RingStage3D` |
| Звуки меню | `ui_*` | `RingMenuAudio` (зона «Меню») |

## Імпорт

Звук рингу — **моно** (пакет П4: «звук рингу імпортувати моно»), Vorbis. `Editor/RingSoundImport.cs` ставить це для
`Assets/ThirdParty/CC0/Audio/Ring/` і `Assets/Art/Ring/Sound/` (іде після загального правила Alpha). Пам'ять:
- музика виходів — потокова;
- гул залу й оплески — стиснені в пам'яті;
- короткі удари — розпаковані й завантажені заздалегідь. Досі вони вантажились на першому відтворенні, тож перший удар запізнювався.

Значення `.meta` перевіряє `RingSoundMapTests.RingSound_IsImportedMono`.
