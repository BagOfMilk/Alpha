# «Ринг» — таблиця «подія → звук»

Критерій М1 (ПМ, 08.11.2026): **«таблиця «подія → звук» без порожніх подій (удар, гонг, рахунок, натовп, QTE, вихід)»**.
Таблиця в коді — `Gameplay/Ring/Models/RingSoundMap.cs`; грає `Stage/Unity/RingSound.cs`. **Звучить лише схвалене власником**
у бібліотеці звуку (`docs/ring/audio/SOUND_APPROVALS.md`): доки слот не схвалено, подія мовчить, хоч рядок у таблиці є.
Охоронець — `RingSoundMapTests`: кожне значення `MatchEventKind` і `MatchActionId` має звук, кожен звук має слоти, слоти є у
файлах сцени або в бібліотеці, а цей документ не відстає від коду.

Таблиця ключована **назвами** подій і дій. Тому вона працює й з основною гілкою, й з переробкою бою (`claude/ring-combat-overhaul`):
там з'являється `Flourish`, а `IntoRopes`, `IntoCorner`, `LeaveRing`, `ReturnToRing` зникають.

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
| `MatchEnd` | BellEnd (+ MatSlap ×3 на утриманні) | |
| `Flourish` | QteHit | лише в переробці бою |

## Дії (`MatchActionId`, подія `Action`)

| Дія | Звук |
|---|---|
| `Strike` | Hit |
| `Dirty` | Hit |
| `Desperate` | Hit |
| `Throw` | Slam |
| `Finisher` | Slam (і спалах, сповільнення 0,4× у режисера) |
| `HopeSpot` | Slam |
| `TopRope` | Land |
| `IntoRopes` | RopeRebound |
| `IntoCorner` | CornerHit |
| `Taunt` | Pop |
| `Submission` | HoldStrain |
| `Pin` | PinCover, далі MatSlap на рахунок |
| `GetUp` | CanvasShuffle |
| `Recover` | CanvasShuffle |
| `CatchBreath` | CanvasShuffle |
| `LeaveRing` | FloorThud |
| `ReturnToRing` | FloorThud |
| `Ally` | Pop |

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
