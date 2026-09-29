# Alpha — «Перевал» (Unity 6.4)

Сюжетна RPG про живе поселення. Головний луп — міський: громада на
карпатському перевалі живе день за днем, ти ставиш людей на пости, радишся
з радою, будуєш, переживаєш інциденти й кризи, ростеш від хутора до
містечка. Вилазки — джерело матеріалів, переважно тихі. **Бої рідкісні** й
тактичні (модель Wasteland 3: пул очок дій, укриття, дозор, стани) — і в
кожного завдання є тихий шлях («Ненасильство понад усе», Поправка №1).
Перша година — за повістю Івана Франка «Захар Беркут».

> Код ігрової логіки — чистий C# без рушія (`Assets/_Project/Scripts/Core`):
> його можна збирати й тестувати звичайним .NET за секунди.

## Документи — з чого почати

| Файл | Що там |
|---|---|
| `docs/DESIGN_INDEX.md` | **покажчик:** статуси поправок, «тема → чинне джерело», відкриті питання |
| `docs/GDD.md` | дизайн-документ **GDD v7** (поправки влито, чернетки позначені) |
| `docs/GDD_AMENDMENTS.md` | поправки власника — історія рішень із дослівними цитатами |
| `docs/DESIGN_CHARTER.md` | статут дизайну: принципи, планки якості, процедура рішення |
| `docs/ROADMAP.md` | що не доробленo, віхи M1–M4 |
| `docs/TEST_BUILD.md` | тестова збірка «усі механіки» |
| `docs/COMBAT_V2.md`, `docs/HUD_DESIGN.md` | подача бою; план HUD решти екранів |
| `CLAUDE.md` | робоча пам'ять проєкту: інваріанти, архітектура, граблі |

`docs/DESIGN.md` і `docs/BALANCE.md` — історичні, джерелом істини не є.

## Як зібрати і перевірити

```bash
bash tools/run-tests.sh                 # ядро + лінт Unity-обгорток, headless .NET
```

```powershell
powershell -File tools/build-unity.ps1  # сцена Game.unity + Build/Windows/Alpha.exe
```

Автотури (ведуть справжній інтерфейс ботом, знімають кожен екран у
`Build/Windows/Screenshots*`; код виходу 0 — пройдено):

```
Alpha.exe -autoplay            # перша година до вільної гри
Alpha.exe -autoplay-journal    # усі 44 записи журналу механік
Alpha.exe -autoplay-battle     # лише бій
Alpha.exe -autoplay-long       # вільна гра до великого бунту
```

Відкрити в редакторі: Unity Hub → Unity **6000.4.10f1** → Add project from
disk. Сцена гри збирається кодом (`Editor/GameSceneBuilder.cs`).

## Структура

```
Assets/_Project/Scripts/
  Core/        чистий C#: Session (фасад партії GameSession), Pressure/World/
               Signals/Loop (Напруга, накопичувачі, сигнали, конвеєр дня),
               Base (пости, будівлі, рада, населення), Combat, Dungeons,
               Companions, Factions, Quests, Story, Items, Characters, Stats,
               Economy, Balance
  Gameplay/    тонкі обгортки Unity: екрани (IMGUI), 3D-арена бою, прогулянка
               селом, текст гри (Text/UkrainianText.cs), автотури
  Editor/      збирачі сцен, імпорт асетів
Assets/Tests/EditMode/   тести ядра (компілюються й у headless-проєкт)
tools/         headless-рішення, консольна збірка Alpha.Play, харнес Alpha.Sim
docs/          дизайн-документи
```
