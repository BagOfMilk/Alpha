# Ассеты для графического прототипа

Подбор под наш кейс: **постапок изометрический base-builder CRPG** (ходибельный
город-хаб + тактика на сетке, GDD §0/§7). Цель — когерентный визуальный грейбокс,
на котором уже можно тестировать бой, укрытия, город и UI.

> **Выбор стиля: low-poly 3D + изометрическая камера.** Живой поиск подтвердил:
> бесплатной 2D-изометрики по постапоку практически нет (itch почти весь —
> пиксель-арт топдаун, другой жанр ощущения), а low-poly 3D-экосистемы (Kenney/
> Quaternius) покрывают город, персонажей всех трёх семейств врагов и VFX —
> бесплатно и в одном стиле. Изо-подача достигается ортографической камерой.

**Статус проверки:** позиции с пометкой ✅ проверены по живым страницам
(11.06.2026); остальное — устойчивые каталоги, сверить при скачивании.

---

## 0. Инфраструктура ДО импорта арта (обязательно)

1. **URP.** Пакет `com.unity.render-pipelines.universal@17.4.0` добавлен в
   `Packages/manifest.json` (версия из кеша редактора 6000.4.10f1 — ставится
   офлайн). **Активация вручную:** Assets → Create → Rendering → *URP Asset
   (with Universal Renderer)* → назначить в Project Settings → Graphics (и в
   Quality). Причина: ✅ Unity Particle Pack для Unity 6 — **только URP**;
   большинство современных паков тоже даёт URP-материалы (в Built-in они розовые).
2. **Коммитить `.meta`.** До сих пор `.meta` не коммитились (для чистого кода это
   сходило с рук). Для арта это обязательно: без `.meta` на другом клоне рассыпятся
   GUID-ссылки материалов/префабов/сцен. Первым коммитом графической фазы — добавить
   все текущие `.meta` + `ProjectSettings/`.
3. **UPM-пакеты по мере надобности** (Window → Package Manager → Install by name):
   `com.unity.probuilder` (грейбокс уровней), `com.unity.cinemachine` (изо-камера),
   `com.unity.inputsystem` (GDD §18). TextMeshPro в Unity 6 уже внутри uGUI.
4. **Папки:** сторонний арт — в `Assets/ThirdParty/<Vendor>/<Pack>/` с `LICENSE.txt`
   рядом (изоляция лицензий и лёгкое обновление пака целиком).

---

## 1. P0 — скачать сразу (бесплатно)

### Kenney — город, реквизит, VFX-спрайты, UI (CC0, без аккаунта)
`https://kenney.nl/assets/<slug>` · форматы FBX/OBJ/GLTF + PNG-атлас. ✅ каталог жив
(Graveyard Kit, Car Kit, Input Prompts подтверждены на первой странице).

| Пак | Slug | Закрывает |
|---|---|---|
| City Kit (Commercial / Suburban / Roads) | `city-kit-commercial` и др. | здания и дороги города-хаба |
| Survival Kit | `survival-kit` | лагерь, ящики, баррикады = **укрытия** |
| Car Kit | `car-kit` | ржавые остовы = укрытия на сетке ✅ |
| Graveyard Kit | `graveyard-kit` | ограды, руины, мрачный реквизит ✅ |
| Nature Kit | `nature-kit` | пустоши вокруг города |
| Furniture Kit | `furniture-kit` | интерьеры зданий базы |
| Weapon Pack / Blaster Kit | `weapon-pack` / `blaster-kit` | стволы в руки (баллистика/энергия) |
| Prototype Textures | `prototype-textures` | чистый грейбокс до арта |
| Particle Pack (2D-спрайты) | `particle-pack` | текстуры для своих частиц |
| UI Pack + Game Icons + Input Prompts | `ui-pack`… | каркас UI, базовые иконки ✅ |

### Quaternius — ВСЕ персонажи (CC0, прямое скачивание) ✅
`https://quaternius.com` · GLTF/FBX/BLEND, риг + анимации. Все названия проверены
по живому каталогу. **Правило когерентности: персонажи только отсюда.**

| Пак | Закрывает |
|---|---|
| Ultimate Modular Men / Women Pack | отряд игрока + люди-рейдеры (семейство Human) |
| Ultimate Animated Character Pack | NPC города (мудборд улиц, US-7.8/10.5) |
| Ultimate Monsters | семейство Mutant (гули и пр.) |
| Animated Robot Pack + Animated Mech Pack | семейство Robot (дроны/боты) |
| Universal Animation Library (1 и 2) | idle/walk/attack/death под тактику |
| Ultimate Stylized Nature Pack | альтернатива Kenney Nature |

### Иконки и шрифты
- **game-icons.net** — ✅ **CC BY 3.0**, формулировка атрибуции: «Icons made by
  {author}. Available on https://game-icons.net» (строка в титры!). 4000+ SVG/PNG —
  иконки на 10 скилов, 7 состояний, типы урона, предметы. Брать PNG.
- **Шрифты с кириллицей (OFL, Google Fonts):** Russo One (заголовки,
  индустриальный характер), PT Sans (основной текст — спроектирован под русский),
  PT Mono или JetBrains Mono (боевой лог/терминалы). TMP-атлас: диапазон
  `0400-045F` + `0451` (ё).

### Unity Asset Store (бесплатное)
- **Particle Pack** (Unity Technologies) — ✅ FREE, 157 МБ, взрывы/огонь/дым;
  **Unity 6 = только URP**: https://assetstore.unity.com/packages/vfx/particles/particle-pack-127325
  → VFX статусов: Поджог, искры Шреда, выстрелы.

---

## 2. P1 — точечные добивки с itch.io (проверены живьём ✅)

| Пак | Лицензия/стиль | URL |
|---|---|---|
| 3D Apocalyptic Building / City | **CC0**, low-poly руины небоскрёбов | https://majadroid.itch.io/3d-apocalyptic-building-city-cc0 |
| Post-Apocalyptic World (Atomic Realm) | 3D low-poly постапок-реквизит | https://atomicrealm.itch.io/post-apocalytic-world *(слаг сверить со страницей поиска)* |
| Voxel Dystopian Characters / Voxel Zombies | вокс-стиль — ⚠ НЕ смешивать с low-poly сетом | https://maxparata.itch.io/voxel-dystopian-characters |
| Hazmat suits workers (PSX) | ретро-PSX — ⚠ другой стиль, только как мудборд | https://imaginais.itch.io/hazmat-suits-workers-retro-psx-character-pack |

2D-пиксельные паки itch (TheLazyStone, Wasteland Essentials и т.п.) — НЕ берём:
другой жанр подачи.

---

## 3. P2 — платное (после того как срез докажет себя)

| Ассет | Цена (проверять на странице) | Зачем |
|---|---|---|
| **Synty POLYGON Apocalypse Wasteland** | ✅ $379.99 (флагман; у Synty есть подписка ~$30/мес на всю библиотеку) | идеальное стилевое попадание в Wasteland-вайб: город+персонажи+транспорт одним сетом — https://assetstore.unity.com/packages/3d/environments/landscapes/polygon-apocalypse-wasteland-pack-art-by-synty-284001 |
| POLYGON Apocalypse Pack (старший, 2019) | дешевле — проверить | бюджетная альтернатива: https://assetstore.unity.com/packages/3d/environments/urban/polygon-apocalypse-pack-art-by-synty-154193 |
| Инструменты GDD §18: Odin Inspector, Easy Save 3, DOTween Pro, A* Pathfinding Pro, Dialogue System + Quest Machine (или **Yarn Spinner — бесплатен**) | цены на страницах стора | авторинг данных, сейвы, твины, пути, нарратив — нужны ближе к контент-фазе, не для грейбокса |

---

## 4. Правила сборки визуала

- **Персонажи из одного источника** (Quaternius): разница пропорций между
  экосистемами читается как баг; внутренняя разница «люди/монстры/роботы» — как фича.
- **Окружение Kenney + Quaternius смешивается нормально** (flat-color low-poly);
  унификация: один toon/flat-шейдер, перекраска крошечных палитр-атласов, единый
  масштаб (1 юнит = 1 м, прогнать «линейку» по всем китам), общий лёгкий пост-процесс.
- **Импорт:** предпочитать FBX (GLTF требует пакет `com.unity.cloud.gltfast`);
  следить за Blender-скейлом (0.01) и осью (поворот −89.98° по X); риги работают
  как Generic из коробки; пивоты Kenney по углам — удобно для снапа по сетке боя.

## 5. Маппинг на наши системы

| Система в коде | Чем визуализируем |
|---|---|
| `GridMap` укрытия (Half/Full) | Car Kit остовы, Survival ящики/мешки, Graveyard ограды |
| Семейства врагов (US-3.14) | Human → Modular Men/Women; Mutant → Ultimate Monsters; Robot → Animated Robot/Mech |
| Статусы (US-3.7) | Поджог — Particle Pack огонь; Кровотечение — декаль + иконка; Подавление/Метка/Сбит — иконки game-icons над юнитом |
| Секции базы (`BaseSectionType`) | City Kit здания: Лазарет/Мастерская/Зал совета/Склад/Рынок |
| 5 стадий стройки (US-7.3) | каркасы из Prototype/City Kit частей |
| Лог боя / телеграфия времени | PT Mono поверх Kenney UI Pack |

## 6. Чек-лист первого графического спринта

1. Активировать URP (см. §0.1) → пустая сцена с ортокамерой ~30–45°.
2. Закоммитить `.meta` + `ProjectSettings` (см. §0.2).
3. Скачать P0-минимум: City Kit Commercial + Survival Kit + Car Kit (Kenney),
   Ultimate Modular Men + Ultimate Monsters + Animated Robot (Quaternius),
   Particle Pack, 30–40 иконок game-icons, шрифты.
4. Грейбокс арены 12×8 из `CombatDemo.BuildArena()`: тайлы, стены, укрытия из китов.
5. Капсулы → персонажи Quaternius на юниты `CombatState` (позиция/HP/статусы).
6. TMP-шрифты с кириллицей + панель лога боя из `CombatState.Log`.
