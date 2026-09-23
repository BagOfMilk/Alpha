# Бесплатные ассеты: каталог под визуальный срез первого часа

> **Статус: рабочий каталог, не канон.** Здесь то, что можно взять и
> попробовать; что именно ляжет в игру — решает владелец. Лицензия у
> каждой строки указана отдельно, потому что **«бесплатно» ≠ «общественное
> достояние»**: часть источников требует атрибуции, часть запрещает
> коммерческое использование вовсе.

## Куда класть файлы (23.09.2026)

Слот под картинку уже готов — скачанного пока ничего нет, и это осознанно:
какие киты лягут в игру, решает владелец.

**Портреты персонажей:** `Assets/Resources/Portraits/<id>.png`, где `id` —
идентификатор из `OpeningCast` (`tuhar`, `zakhar`, `maksym`, `myroslava`,
`keeper`, `healer`, `protagonist`). Файла нет — сцена всё равно играется, на
месте портрета именная заглушка. Ничего в коде править не нужно: портрет
подбирается по ключу, как и реплика.

Сцена собирается командой меню **Alpha → Пересоздать сцену «Открытие»**;
`Assets/Resources/Portraits/ЧИТАЙ.txt` повторяет эти правила рядом с файлами.

**Проверять лицензию на каждый файл отдельно.** «Бесплатно» не равно
«общественное достояние»: часть источников требует атрибуции, часть запрещает
коммерческое использование. Столбцы ниже — отбор, а не разрешение.

## Правила, по которым отбиралось

1. Годится только то, что разрешено в **коммерческой** игре.
2. Порядок предпочтения: CC0 / PD → MIT, OFL, Apache → CC BY (нужна атрибуция) →
   бесплатные ассеты Unity Asset Store (в игру можно, перепродавать нельзя).
3. У каждой записи — точная лицензия и нужна ли атрибуция.
4. Музейные изображения: статус **произведения** и условия на **фотографию**
   различаются, и это указано отдельно.
5. Столбец «Проверено» — вердикт отдельного агента-скептика, который открывал
   страницу лицензии сам. ⚠️ и ❔ означают: перед использованием проверить руками.

## Окружение: село, хаты, частокол, лес, горы

| Ассет | Что внутри | Лицензия | Атрибуция | Проверено |
|---|---|---|---|---|
| **[Fantasy Town Kit](https://kenney.nl/assets/fantasy-town-kit)** | 160 low-poly моделей: деревянные и каменные дома, стены, башни, ворота, элементы городка в едином стилизованном масштабе Kenney (тот же грид, что и другие киты Kenney). | CC0 1.0 Universal (Creative Commons Zero) | не нужна | ✅ проверено |
| **[Modular Village Pack](https://fertile-soil-productions.itch.io/modular-village-pack)** | 155 low-poly моделей: модульные дома, заборы и ВОРОТА (ближайший аналог частокола), телеги, бочки, ящики, лодки, причалы, фонарные столбы. | CC0 1.0 Universal (Creative Commons Zero) | не нужна | ✅ проверено |
| **[Medieval Village MegaKit](https://quaternius.com/packs/medievalvillagemegakit.html)** | 300+ модульных элементов: стены (внешние/внутренние), полы, лестницы, крыши, двери, окна, плющ — конструктор для сборки зданий любой конфигурации. | CC0 1.0 Universal (Creative Commons Zero) | не нужна | ✅ проверено |
| **[Nature Kit](https://kenney.nl/assets/nature-kit)** | 330+ природных объектов: деревья, камни/валуны, пни, водопады, растения, элементы рельефа и лагерное снаряжение. | CC0 1.0 Universal (Creative Commons Zero) | не нужна | ✅ проверено |
| [Stylized Nature MegaKit](https://quaternius.com/packs/stylizednaturemegakit.html) | 116 моделей: 40 деревьев, 35 растений/цветов, 27 камней, трава и кусты, все текстурированные. | CC0 1.0 Universal (Creative Commons Zero) | не нужна | ✅ проверено |
| [KayKit — Medieval Hexagon Pack (бесплатный уровень)](https://kaylousberg.itch.io/kaykit-medieval-hexagon) | 200+ моделей в бесплатном тире: гексагональные тайлы местности и именные постройки — кузница, лесопилка, церковь, таверна, рынок, мельница (ветряная и водяная), шахта, колодец, обычные дома, казармы,  | CC0 1.0 Universal (Creative Commons Zero) | не нужна | ✅ проверено |
| [KayKit — Forest Nature Pack (бесплатный уровень)](https://kaylousberg.itch.io/kaykit-forest) | 100+ моделей в бесплатном тире (в полной версии 200+/1500+ с цветовыми вариациями): деревья разных форм и размеров, кусты, два стиля травы, камни. | CC0 1.0 Universal (Creative Commons Zero) | не нужна | ✅ проверено |
| [Low Poly Cliff Pack](https://assetstore.unity.com/packages/3d/environments/landscapes/low-poly-cliff-pack-67289) | Low-poly скалы/утёсы для формирования горного рельефа (детальный список моделей на странице не приведён). | Standard Unity Asset Store EULA (Extension Asset), бесплатный пакет | неясно | ✅ проверено |
| [Free Medieval Houses 3D Low Poly Models](https://free-game-assets.itch.io/free-medieval-houses-3d-low-poly-models) | 20 моделей средневековых домов в FBX с PNG-текстурами. | Craftpix Freebie License (не CC0) | не нужна | ✅ проверено |

**Зачем это нам:**

- **Fantasy Town Kit** — Костяк села на перевале: хаты и хозпостройки вдоль улиц, стены и ворота как основа частокольной линии. Прямая стыковка с Kenney Nature Kit (тот же автор, единый визуальный язык). _Пайплайн: Built-in RP: подходит как есть. URP/HDRP не требуется.._ _Формат: FBX/OBJ/glTF, готовые (нетекстурированные или с простыми материалами) low-poly модели, единый масштаб._
  - Оговорка: Стиль скорее «фэнтези-городок», чем доиндустриальное карпатское село — потребуется перекраска текстур/материалов под сеттинг.
  - Со страницы лицензии: «Kenney site states license 'Creative Commons CC0' (link to CC0 1.0 deed); Kenney's standard policy: free for any use, no attribution required.»
- **Modular Village Pack** — Единственный из найденных пакетов с явными заборами/воротами — ближе всего к частоколу; лодки и причалы пригодятся для реки/моста на перевале. Требует текстурирования, чтобы получить «игру с текстурами», которую хочет владелец. _Пайплайн: Built-in RP: совместим, но нужны свои текстуры — пак изначально flat-color.._ _Формат: OBJ + MTL, модели БЕЗ UV-текстур — только сплошные цвета материалов; совместим с Modular Terrain Pack того же автора._
  - Оговорка: Модели без текстур (плоский цвет) — если владелец хочет буквально текстуры «из коробки», этот пак нужно дорабатывать в Blender/Unity. Стиль (геометрический, угловатый) может не совпадать со стилем Kenney/Quaternius без доп. работы.
  - Проверка: Note: unlike Kenney/Quaternius/KayKit, this creator's CC0 claim is corroborated mainly via a comment reply rather than a formal license statement box — worth a second glance before shipping, though itch.io's own 'License' metadata field also reads CC0.
- **Medieval Village MegaKit** — Быстрая сборка разнообразных хат и общественных построек села на перевале за счёт модульности стен/крыш/дверей — хорошо дополняет Fantasy Town Kit и Modular Village Pack. _Пайплайн: Built-in RP: raw FBX подходят напрямую; Source-шейдеры могут целиться в SRP.._ _Формат: FBX/OBJ/glTF (+ Blend в Source-версии), модульная сетка._
  - Оговорка: Явного упоминания частокола/забора на странице нет — фокус на зданиях, не на ограждениях.
  - Со страницы лицензии: «"Free to use in personal, educational and commercial projects. (CC0 License)"»
- **Nature Kit** — Лес и предгорья вокруг села на перевале; валуны и пни для горного склона; тот же масштаб, что у Fantasy Town Kit — стыкуется без подгонки. _Пайплайн: Built-in RP: подходит как есть.._ _Формат: FBX/OBJ/glTF, единый low-poly стиль и масштаб со всеми другими китами Kenney._
  - Оговорка: Полноценных «горных» мешей (крупные скальные массивы/утёсы) в наборе нет — только отдельные камни/валуны, для больших гор нужен отдельный пак (см. Low Poly Cliff Pack).
  - Со страницы лицензии: «Page links license as 'Creative Commons CC0' (creativecommons.org/publicdomain/zero/1.0/).»

## Персонажи: модели и анимации

| Ассет | Что внутри | Лицензия | Атрибуция | Проверено |
|---|---|---|---|---|
| **[Quaternius — Universal Base Characters + Modular Character Outfits (Fantasy) + Universal Animation Library](https://quaternius.com/packs/universalbasecharacters.html)** | 6 базовых гуманоидных тел (муж/жен × Superhero/Regular/Teen, ~13k трис, 20 причёсок) + компаньон 'Modular Character Outfits – Fantasy' (12 нарядов / 62 модульных детали, по 3 варианта текстур) + 'Univ | CC0 1.0 Universal | не нужна | ✅ проверено |
| **[KayKit — Character Pack: Adventurers (free-тир) + Character Animations](https://kaylousberg.itch.io/kaykit-adventurers)** | 5 риггованных low-poly гуманоидов (free-тир) + 25 аксессуаров/оружия; компаньон-пак 'Character Animations' — 100+ общих гуманоидных анимаций (idle/walk/run/jump/crawl/sneak/dodge/crouch, ближний бой о | CC0 1.0 Universal | не нужна | ✅ проверено |
| [Mixamo](https://www.mixamo.com/) | Авто-риггер + сотни бесплатных mocap-анимаций (ходьба/бег/idle/жесты/бой/работа), экспорт FBX со стандартным biped-скелетом под Unity Humanoid. | Условия использования Adobe/Mixamo (не CC, не OSS) | не нужна | ✅ проверено |
| [RPG Character Mecanim Animation Pack FREE](https://assetstore.unity.com/packages/3d/animations/rpg-character-mecanim-animation-pack-free-65284) | 88 Mecanim/Humanoid-анимаций (усечённая версия платного пака на 1437 анимаций); страница явно указывает совместимость с версиями Unity 2019.4–6000.0 и с Built-in/URP/HDRP. | Standard Unity Asset Store EULA | не нужна | ✅ проверено |
| [FREE - Modular Character - Fantasy RPG Human Male](https://assetstore.unity.com/packages/3d/characters/humanoids/humans/free-modular-character-fantasy-rpg-human-male-228952) | Модульный мужской гуманоид в фэнтезийном снаряжении, риг под Humanoid (детальный состав пака на странице не расписан). | Standard Unity Asset Store EULA | не нужна | ✅ проверено |
| [Kenney — Blocky Characters / Mini Characters](https://kenney.nl/assets/blocky-characters) | Blocky Characters — 18 персонажей / 27 анимаций; компаньон Mini Characters (https://kenney.nl/assets/mini-characters) — 12 персонажей / 32 анимации, включая позы для инвалидной коляски. | CC0 1.0 Universal | не нужна | ✅ проверено |
| [POLYGON — Starter Pack — Art by Synty](https://assetstore.unity.com/packages/3d/environments/polygon-starter-pack-art-by-synty-156819) | Бесплатный ознакомительный срез фирменного low-poly стиля POLYGON: немного персонажей плюс окружение/пропсы. | Standard Unity Asset Store EULA | не нужна | ✅ проверено |

**Зачем это нам:**

- **Quaternius — Universal Base Characters + Modular Character Outfits (Fantasy) + Universal Animation Library** — База тела протагониста и NPC-жителей села; модульная одежда перекрашивается под крестьянский/постапок вид; анимации ходьбы/бега/базовых действий закрывают ходибельную изометрию первого часа. _Пайплайн: Built-in RP: не требует конвертации (обычные FBX + PNG-текстуры).._ _Формат: FBX, GLB/GLTF (Blend — в pro/source версии)._
  - Оговорка: Три отдельных пака одного автора — Humanoid Avatar Definition в Unity нужно собрать и проверить руками на конкретном скелете; ретаргетинг между ними заявлен производителем, но не проверен на этом репозитории.
  - Проверка: Checked all three bundled sub-packs individually, not just the linked Universal Base Characters page. No discrepancy found.
- **KayKit — Character Pack: Adventurers (free-тир) + Character Animations** — Стилизованные крестьяне-жители подходят под 'не техногенный' сеттинг; боевые анимации годятся для редких стычек у поста; единый градиентный атлас 1024² легко перекрасить под свою палитру. _Формат: FBX, GLTF._
  - Оговорка: Собственный риг заявлен как ретаргетируемый, но не строго проверен под Unity Humanoid Avatar на этом проекте; 'Adventurers' — образ искателя приключений с оружием, для мирных жителей нужно убрать снаряжение/добавить рабочие позы вручную.
  - Со страницы лицензии: «"Free for personal and commercial use, no attribution required. (CC0 Licensed)." Free vs paid EXTRA/SOURCE tiers differ only in bonus content, not license terms; separate Character Animations pack (133 anims) is CC0 too.»

## Портреты именных персонажей (музейный open access)

| Ассет | Что внутри | Лицензия | Атрибуция | Проверено |
|---|---|---|---|---|
| **[The Metropolitan Museum of Art — Open Access](https://www.metmuseum.org/hubs/open-access)** | Огромная коллекция европейской и американской живописи XVI–XX вв., жанровые сцены и портреты; есть славянские сюжеты, но основной массив — западноевропейский/американский. | CC0 1.0 (для произведений со статусом Open Access) | не нужна | ✅ проверено |
| **[Smithsonian Open Access (включая National Portrait Gallery)](https://www.si.edu/openaccess)** | National Portrait Gallery — именно портретная коллекция США (президенты, деятели XVIII–XIX вв.) плюс остальные 20 музеев Смитсоновского института. | CC0 1.0 для объектов Open Access; остальные — "usage conditions apply" | не нужна | — не проверялось |
| **[Wikimedia Commons (агрегатор PD-репродукций)](https://commons.wikimedia.org/wiki/Commons:Licensing)** | Крупнейший агрегатор PD-живописи, в том числе репродукции работ Рєпіна, Васнецова, Богданова-Бєльського, Похитонова и других передвижников с бытовыми и портретными сценами восточноевропейской/карпатск | Public Domain / CC0 / CC BY-SA — указана индивидуально на странице файла | неясно | ✅ проверено |
| [National Gallery of Art (Вашингтон) — Open Access](https://www.nga.gov/artworks/free-images-and-open-access) | Европейская и американская живопись, значительная коллекция голландских/фламандских и французских портретов XVII–XIX вв. | CC0 (Creative Commons Zero) | не нужна | ✅ проверено |
| [Getty Open Content Program](https://www.getty.edu/projects/open-content-program/) | Живопись, скульптура, декоративное искусство, гравюры и фотоархив исследовательского института. | CC0 | не нужна | ✅ проверено |
| [Rijksmuseum — Rijksstudio / Data Services](https://data.rijksmuseum.nl/policy/) | Голландская золотая эпоха: портреты бюргеров, ремесленников, крестьян, натюрморты и жанровые сцены XVII–XIX вв. | CC0 1.0 для метаданных; изображения — Public Domain Mark и/или CC0, зависит от объекта | неясно | ✅ проверено |
| [Art Institute of Chicago — Open Access](https://www.artic.edu/open-access) | Западноевропейская и американская живопись, включая импрессионистов и жанровую живопись. | CC0 Public Domain Designation (только для помеченных объектов, не вся коллекция) | не нужна | 🔗 ссылка не открылась |
| [Europeana](https://www.europeana.eu/en/rights/terms-of-use) | Живопись, фотографии, этнографические материалы из восточноевропейских и центральноевропейских институций. | Смешанная: CC0/PDM/CC BY/CC BY-SA/CC BY-NC/No Copyright-NC/In Copyright — по каждой записи отдельно | неясно | 🔗 ссылка не открылась |
| [OpenGameArt.org — коллекция "CC0 Portraits"](https://opengameart.org/content/cc0-portraits) | Готовые нарисованные/пиксельные NPC-портреты под диалоговые системы игр. | CC0 1.0 Universal → **User-curated 'Collection' of ~300 separate OGA submissions, each with its OWN license (CC0/CC-BY/CC-BY-SA/GPL); no unified license field.** | неясно | ⚠️ лицензия иная |

**Зачем это нам:**

- **The Metropolitan Museum of Art — Open Access** — Основной источник для портретов старост, торговцев, священников и «фоновых» типажей — можно найти крестьянские/сельские портреты XVIII–XIX вв. под кадрирование под портретную сцену диалога. API удобно фильтровать по тегам. _Формат: ~406 000 изображений (JPEG), REST API (metmuseum.github.io), датасет метаданных на GitHub._
  - Оговорка: Не вся коллекция открыта: у произведений без статуса Open Access (часть современного искусства, депозиты) скачивания нет — нужно проверять флаг isPublicDomain в API перед использованием.
  - Проверка: Record's license field already flags this correctly. Reminder: ~492,000 OA images exist, but many Met items are NOT Open Access (still copyrighted/restricted) and won't carry CC0 — check the OA badge per object, don't assume the whole site is CC0.
- **Smithsonian Open Access (включая National Portrait Gallery)** — Прямое попадание в жанр «портретная сцена»: готовые погрудные портреты с нейтральным фоном под переозвучку персонажа. Также полезны предметные фото доиндустриальной утвари для референса частокола, хат и снаряжения ополченцев. _Формат: 2,8 млн изображений и данных, фильтр Open Access на npg.si.edu/portraits/collection-search, есть API._
  - Оговорка: Портреты в основном англоязычно-американские по типажу одежды XVIII–XIX вв. — годятся как референс позы/освещения, но одежду и антураж придётся перерисовывать под карпатский сеттинг. Не каждый объект CC0 — фильтр обязателен.
- **Wikimedia Commons (агрегатор PD-репродукций)** — Ключевой источник именно под сеттинг «карпатское горное село»: жанровые сцены крестьянского быта, портреты старост/крестьян/священников кисти передвижников — прямой референс и материал для портретных сцен диалогов протагониста и напарников. _Формат: Десятки миллионов файлов, поиск по License:PD, SPARQL/Wikidata Query Service, GLAM-загрузки музеев._
  - Оговорка: PD-статус применяется к самой 2D-репродукции по правилу Commons, но это позиция фонда, а не всех правообладателей — нужно проверять карточку файла (часть загрузок от музеев несёт доп. условия вроде некоммерческого использования фото, например часть загрузок из Третьяковки).
  - Проверка: Claim is accurate but attribution is not uniformly 'unclear' at the aggregator level — it's license-specific: CC0/PD files need none, CC BY-SA files require it. Always check the individual file page, not just this policy page.

## Текстуры, материалы, небо

| Ассет | Что внутри | Лицензия | Атрибуция | Проверено |
|---|---|---|---|---|
| **[Poly Haven — Textures + HDRIs](https://polyhaven.com/textures и https://polyhaven.com/hdris)** | Wood, Stone, Ground&Terrain (земля/снег), Concrete (штукатурка), Textiles, Organic>Thatch&Reed ('Thatch Roof Angled', 8K); HDRI-небо, вкл. подборку 'Pure Skies' | CC0 1.0 (Public Domain) | не нужна | ✅ проверено |
| **[ambientCG](https://ambientcg.com/)** | 2000+ материалов, в т.ч. ThatchedRoof002A/B, ThatchedRoofSubstance001 (соломенная кровля), Snow004/006, плюс Wood, Stone, Ground, Plaster/Concrete, Fabric | CC0 1.0 Universal | не нужна | ✅ проверено |
| [CGBookcase](https://www.cgbookcase.com/textures) | 566 материалов: металлы, камень, плитка, дерево, ткани | CC0 1.0 | не нужна | ✅ проверено |
| [3DTextures.me](https://3dtextures.me/) | Хендмейд-набор, много стилизованных и sci-fi текстур наравне с реалистичными | CC0 | не нужна | ✅ проверено |
| [ShareTextures.com](https://www.sharetextures.com/) | 1600+ текстур, 250+ 3D-моделей, 50+ атласов | Собственная CC0-based лицензия сайта (не буквальный CC0-текст) | не нужна | ✅ проверено |
| [TextureCan.com](https://www.texturecan.com/) | PBR-текстуры и CC0 3D-модели общего назначения | CC0 1.0 Universal | не нужна | ✅ проверено |
| [AllSky Free — 10 Sky / Skybox Set](https://assetstore.unity.com/packages/2d/textures-materials/sky/allsky-free-10-sky-skybox-set-146014) | 10 готовых Skybox-материалов «под ключ» | Standard Unity Asset Store EULA | неясно | ✅ проверено |

**Зачем это нам:**

- **Poly Haven — Textures + HDRIs** — Главный источник: деревянные хаты, частокол/камень, соломенная крыша, земля/снег под ногами, штукатурка, полотно для НПС-карточек. HDRI-небо для скайбокса горного перевала в изометрии. _Пайплайн: Built-in RP совместимо._ _Формат: PNG/JPG PBR-карты 1K–8K (сканы до 16K); HDRI — EXR/HDR 1K–16K + 8K JPG-подложка; есть API для массовой закачки._
  - Оговорка: Официального плагина под Unity нет — импорт руками/через сторонние конвертеры; roughness→smoothness вручную под Built-in Standard-шейдер; HDRI→Skybox настраивается вручную.
  - Со страницы лицензии: «polyhaven.com/license: 'Our assets are all licensed as CC0... You can use our assets for any purpose, including commercial work... You do not need to give credit or attribution.'»
- **ambientCG** — Второй источник тех же нужд, что Poly Haven, но с готовыми Unity-импортёрами — быстрее собрать материалы хат/снега/соломы; дублирует и расширяет Poly Haven там, где не хватает конкретного мотива. _Пайплайн: Built-in RP совместимо._ _Формат: PNG/JPG 1K-8K; часть — .sbsar; офиц. batch-downloader + community Unity-импортёры._
  - Оговорка: Сторонние Unity-импортёры — community-инструменты (GitHub), не от самого ambientCG; проверить под Unity 6.4 перед массовым использованием.
  - Со страницы лицензии: «ambientcg.com: 'All assets are released under the Creative Commons CC0 license, making them free to use without attribution - even in commercial circumstances.'»

## Эффекты: взрывы, огонь, дым

| Ассет | Что внутри | Лицензия | Атрибуция | Проверено |
|---|---|---|---|---|
| **[War FX](https://assetstore.unity.com/packages/vfx/particles/war-fx-5669)** | 40+ префабов в 5 категориях: взрывы (в т.ч. с ударной волной), дульные вспышки, трассеры, попадания пуль по разным поверхностям, дым и огонь. Оптимизировано под десктоп и мобильные. | Standard Unity Asset Store EULA (Extension Asset) | не нужна | ✅ проверено |
| **[Particle Pack (80+ sprites)](https://kenney.nl/assets/particle-pack)** | 80 PNG-спрайтов частиц (огонь, дым, искры, магия, электричество, «сердечки»), плюс тайлшит, векторный файл и готовый Unity-пакет с сэмплами на легаси Particle System. | CC0 1.0 (Public Domain) | не нужна | ✅ проверено |
| **[Smoke Particles](https://kenney.nl/assets/smoke-particles)** | Набор PNG-спрайтов дыма в нескольких стилях: чёрный дым, дым взрыва, «пшик», вспышка, белый клубок (по зеркалу на OpenGameArt — 77 файлов в 5 стилях). | CC0 1.0 (Public Domain) | не нужна | ✅ проверено |
| [Cartoon FX Remaster Free](https://assetstore.unity.com/packages/vfx/particles/cartoon-fx-remaster-free-109565) | 50 стилизованных эффектов из 4 платных паков Cartoon FX: взрывы, огонь, дым, молнии/электричество, попадания, ауры/щиты, погодные эффекты. | Standard Unity Asset Store EULA (Extension Asset) | не нужна | ✅ проверено |
| [More Explosions](https://opengameart.org/content/more-explosions) | Около полудюжины анимированных спрайт-листов взрыва/фаербола, кадры 100×100 px, чистые PNG без движковой обвязки. | CC0 | не нужна | ✅ проверено |
| [Particle Pack (Unity Technologies)](https://assetstore.unity.com/packages/vfx/particles/particle-pack-127325) | Флагманский стартовый набор частиц от самой Unity: взрывы, огонь, дым, искры, пыль, вода и другие базовые VFX. | Standard Unity Asset Store EULA (Extension Asset) | неясно | ✅ проверено |
| [Free Stylized Smoke Effects Pack](https://assetstore.unity.com/packages/vfx/particles/fire-explosions/free-stylized-smoke-effects-pack-226406) | Стилизованные эффекты дыма, по описанию издателя — на легаси Particle System (не VFX Graph) с legacy-шейдером. | Standard Unity Asset Store EULA (Extension Asset) | неясно | ✅ проверено |

**Зачем это нам:**

- **War FX** — Core-набор для редких боевых сцен (Поправка №3): взрыв на перевале/у частокола, попадания, дульные вспышки хабара. Дым из набора годится и для мирных сцен (костёр поста). _Формат: Unity-пакет, легаси Particle System (Shuriken), шейдеры Built-in — не VFX Graph; совместим с Unity начиная с 5.6.7, ~8 МБ.._
  - Оговорка: Страница Asset Store явно не подписывает 'Shuriken' — вывод сделан по возрасту пакета (с 2014 г.) и отсутствию упоминаний VFX Graph/URP. EULA запрещает выкладывать ассет 'как есть' в открытый код и перепродавать его отдельно от игры.
  - Проверка: Same EULA caveat as above: usable only via Unity project integration per EULA terms, not as a standalone asset redistribution — this is fine for the stated purpose (using in a Unity game) but worth knowing it's not CC0.
- **Particle Pack (80+ sprites)** — Сырьё для собственных Shuriken-систем без привязки к рендер-пайплайну: искры у наковальни/кузницы, печная и костровая дымка, вспышки сигналов инцидентов. Растровый стиль легко перекрашивается под палитру карпатского села. _Формат: PNG-спрайты + готовый Unity Package (Shuriken), ZIP ~9.8 МБ.._
  - Оговорка: Зеркало на OpenGameArt (opengameart.org/content/particle-pack-80-sprites) подтверждает тот же CC0 и авторство Kenney — но точный список 80 спрайтов виден только после распаковки архива.
  - Со страницы лицензии: «Kenney site license type: "Creative Commons CC0" — public domain dedication permitting unrestricted commercial use with no attribution requirement.»
- **Smoke Particles** — Мирный, не-боевой слой: дым из печной трубы хаты, дымка над постом, костёр — должно быть видно постоянно, а не только во время инцидента; закрывает требование «настоящего визуального среза», а не только боя. _Формат: Отдельные PNG-спрайты без готового Unity-пакета — под Shuriken/спрайтовую анимацию собирать вручную; ZIP ~13 МБ.._
  - Оговорка: Число файлов расходится между источниками (kenney.nl не даёт точного счётчика в выдаче, зеркало на OpenGameArt называет 77) — вероятно разные версии пака; качать лучше с kenney.nl как с первоисточника.
  - Со страницы лицензии: «Kenney site license type: "Creative Commons CC0" — public domain, commercial use allowed, attribution not required (crediting Kenney is a courtesy only).»

## Интерфейс: панели, иконки

| Ассет | Что внутри | Лицензия | Атрибуция | Проверено |
|---|---|---|---|---|
| **[Game-icons.net (4180+ SVG-иконок)](https://game-icons.net/)** | 4180+ монохромных SVG/PNG-иконок (перекрашиваемые, заливка одним цветом): навыки, атрибуты, оружие, броня, зелья, свитки, погода, время (луна/солнце/песочные часы), тревога/колокол/череп, существа, ре | CC BY 3.0 | **нужна** | — не проверялось |
| **[UI Pack](https://kenney.nl/assets/ui-pack)** | 430+ отдельных PNG-спрайтов, спрайт-листы и векторные файлы: кнопки, панели, слайдеры, чекбоксы, вкладки, полосы прогресса; плюс 2 TTF-шрифта и 6 звуков UI. | CC0 1.0 (Creative Commons Zero / Public Domain) | не нужна | — не проверялось |
| **[UI Pack (RPG Expansion)](https://kenney.nl/assets/ui-pack-rpg-expansion)** | 85 элементов: кнопки, панели и слайдеры в RPG-стилистике (расширение базового UI Pack, та же система координат/сетки). | CC0 1.0 (Creative Commons Zero / Public Domain) | не нужна | ✅ проверено |
| **[Cursor Pack](https://kenney.nl/assets/cursor-pack)** | 180 курсоров (базовый релиз + доп. 70 в версии 1.1) в PNG и SVG: стрелки, указатели, интерактивные состояния (клик/hover/недоступно), прицелы. | CC0 1.0 (Creative Commons Zero / Public Domain) | не нужна | ✅ проверено |
| [Fantasy RPG Game UI Kit — 53 Elements](https://orabon.itch.io/fantasy-rpg-game-ui-kit) | 53 hand-painted элемента в 3 размерах (512/256/128 px), прозрачный PNG: базы кнопок, слоты инвентаря, панели (пергамент, резное дерево, каменная табличка), полосы здоровья/маны, баннеры, круглые кнопк | Royalty-free для коммерческих и личных проектов (собственная лицензия автора, без переиздания/перепродажи) | не нужна | ✅ проверено |
| [Game Icons + Game Icons (Expansion)](https://kenney.nl/assets/game-icons) | 105 + 60 иконок общего игрового интерфейса (геймпад/джойстик, подсказки ввода, системные значки меню); не специализированы под навыки/атрибуты. | CC0 1.0 (Creative Commons Zero / Public Domain) | не нужна | ✅ проверено |
| [Pixel UI Pack](https://kenney.nl/assets/pixel-ui-pack) | 750+ пиксель-артовых UI-элементов: панели, кнопки (точный список подкатегории на странице не детализирован). | CC0 1.0 (Creative Commons Zero / Public Domain) | не нужна | ✅ проверено |
| [Free RPG Icons Pack](https://assetstore.unity.com/packages/2d/gui/icons/free-rpg-icons-pack-267155) | Набор иконок по тегам 'skills', 'magic', 'effects', 'spell' — заявлено как 'Created with AI'; точный список категории на странице не детализирован. | Standard Unity Asset Store EULA (Extension Asset) | неясно | ✅ проверено |

**Зачем это нам:**

- **Game-icons.net (4180+ SVG-иконок)** — Главный источник иконок для проверок и порогов (Сила/Ловкость/Смекалка/Воля, 10 скилов), сигналов Напряжения (колокол, песочные часы, череп) и ресурсов постов — под тег/подпись, единым стилем (задать один цвет обводки под общую палитру UI). _Пайплайн: Не зависит от render pipeline — 2D-иконки (PNG/SVG), работают и в Built-in RP._ _Формат: SVG + PNG (белый/чёрный/прозрачный фон), одноцветные._
  - Оговорка: Атрибуция ОБЯЗАТЕЛЬНА (CC BY 3.0, не CC0) — нужен экран/меню «об игре» со списком авторов и ссылкой на game-icons.net, иначе нарушение лицензии. Стиль каждой иконки индивидуален (разные авторы), для визуальной цельности рекомендуется брать одну общую заливку/обводку через код, не смешивать закрашенные и контурные варианты без обработки.
- **UI Pack** — Базовый скелет интерфейса поста/панели поселения: рамки панелей, кнопки назначений, слайдеры/полосы для ресурсов и Напряжения (только визуально — числа Напряжения internal и на экран не идут), чекбоксы для списка задач дня. _Пайплайн: Не зависит от render pipeline — 2D UI, Built-in RP ок._ _Формат: PNG (отдельные спрайты + spritesheet) + SVG/vector, TTF, WAV._
  - Оговорка: Стиль плоский/минималистичный, не 'карпатское дерево' — для формы «Перевал» лучше сочетать с деревянным/пергаментным набором (см. Orabon ниже) для акцентных элементов, а Kenney UI Pack держать под служебные экраны (настройки, списки).
- **UI Pack (RPG Expansion)** — RPG-акценты поверх базового UI Pack — рамки инвентаря/поста, кнопки действий вылазки; стыкуется 1:1 с UI Pack по сетке, можно комбинировать без швов. _Пайплайн: Не зависит от render pipeline._ _Формат: PNG (спрайты + spritesheet) + SVG/vector._
  - Оговорка: Всё ещё плоский векторный стиль Kenney, не имитирует резное дерево/кору — для портретных сцен (диалоги) визуально слабее, чем hand-painted наборы.
  - Проверка: None — claim matches. Same CC0-vs-public-domain terminology note as above.
- **Cursor Pack** — Прямое попадание в требование «курсоры»: курсор ходьбы по изометрии, курсор наведения на интерактивный объект (пост, NPC), курсор недоступного действия для сигналов запрета. _Пайплайн: Не зависит от render pipeline._ _Формат: PNG + SVG._
  - Оговорка: Стиль нейтральный/минималистичный — под сеттинг красить/перерисовывать не обязательно, курсоры почти никогда не воспринимаются как часть художественного стиля игры.
  - Со страницы лицензии: «Kenney asset page: 'License: Creative Commons CC0'»

## Шрифты с украинской кириллицей

| Ассет | Что внутри | Лицензия | Атрибуция | Проверено |
|---|---|---|---|---|
| **[Noto Sans + Noto Serif](https://fonts.google.com/noto/specimen/Noto+Sans)** | Универсальная пара гротеск+антиква, часть проекта Noto (цель — покрыть весь Unicode); явно заявлена поддержка украинской кириллицы (ґ, є, і, ї) наряду с сотнями языков | SIL Open Font License 1.1 | **нужна** | ✅ проверено |
| **[Fixel](https://fixel.macpaw.com/)** | Гротеск с человечными пропорциями, сделан украинской компанией с прицелом на украинскую кириллицу (README прямо упоминает символ тризуба Ніла Хасевича среди глифов) — то есть диакритика делалась не «м | SIL Open Font License 1.1 | не нужна | ✅ проверено |
| [IBM Plex Sans + IBM Plex Serif](https://fonts.google.com/specimen/IBM+Plex+Sans) | Профессиональная гротеск+антиква пара; в обзоре type.today «Cyrillic on Google Fonts: Humanist Sans» названа лучшей среди рассмотренных для украинского — но с оговоркой про сдвоенные ї, которые слегка | SIL Open Font License 1.1 (Reserved Font Name "Plex") | не нужна | ✅ проверено |
| [e-Ukraine + e-Ukraine Head](https://thedigital.gov.ua/fonts) | Гротеск, созданный специально для украинских госсервисов (используется в Дії) с прицелом на доступность и читаемость; e-Ukraine Head — более крупный/акцидентный компаньон для заголовков | Creative Commons Attribution 4.0 International (CC BY 4.0) | **нужна** | ✅ проверено |
| [Nyght Serif](https://gitlab.com/mkobuzan/nyght-serif) | Контрастная антиква с острыми засечками («spicy character»), расширенная латиница + украинская кириллица — сделан с прицелом именно на украинский, не «кириллицу вообще» | SIL Open Font License 1.1 | не нужна | ✅ проверено |
| [Yeseva One](https://fonts.google.com/specimen/Yeseva+One) | Акцидентная антиква, заявлена поддержка 51 языка, включая украинский (кириллица+латиница) | SIL Open Font License 1.1 (Reserved Font Name 'Yeseva') | не нужна | ✅ проверено |
| [Unbounded](https://fonts.google.com/specimen/Unbounded) | Геометрический дисплейный шрифт, заявлена поддержка кириллицы (в т.ч. украинской) на 1300+ глифов | SIL Open Font License 1.1 | не нужна | ✅ проверено |

**Зачем это нам:**

- **Noto Sans + Noto Serif** — Базовый безопасный вариант: Noto Sans — интерфейс и реплики в портретных сценах, Noto Serif — «бумажные» экраны (доклады с постов, журнал/дневник поселения). Визуально нейтральный, не несёт фолк-характера — как раз то, что нужно для читаемости длинных диалогов. _Формат: Google Fonts / TTF+variable, 9 начертаний × italic в обеих гарнитурах._
  - Оговорка: Специализированного разбора именно украинских диакритик (как у type.today) не нашёл — сужу по официальному заявлению проекта Noto о полном охвате языка. Нейтральный дизайн — не даёт «карпатского» характера, годится только как рабочая лошадка.
  - Проверка: Claimed attribution 'none' is imprecise: OFL 1.1 needs no on-screen credit, but redistributed font FILES bundled in the build must carry the copyright notice + license text (e.g. a credits/legal file), not zero obligation.
- **Fixel** — Кандидат на основной интерфейсный/диалоговый шрифт вместо Noto Sans — украинское происхождение снижает риск кривых акцентов над є/ї, характерных для многих западных гротесков (см. рейтинг type.today ниже) _Формат: Variable font (TTF), все начертания Thin–Black (9 степеней), варианты Text и Display._
  - Оговорка: Это variable-шрифт: для Unity/TextMeshPro нужно экспортировать статические инстансы нужных начертаний (fonttools instancer), TMP с осями напрямую не работает. Отдельного italic-начертания не нашёл — только прямое. Апостроф глазами не проверял, стандартный набор пунктуации должен включать.
  - Проверка: Page itself doesn't spell out commercial/attribution terms explicitly, only links to OFL; those terms are inferred from standard OFL 1.1 (no-attribution, commercial-OK), not quoted verbatim on the page. Nothing contradicts the claim.

## Звук: атмосфера, музыка, эффекты

| Ассет | Что внутри | Лицензия | Атрибуция | Проверено |
|---|---|---|---|---|
| **[#GameAudioGDC Bundle (Sonniss)](https://gdc.sonniss.com/)** | Ежегодные бесплатные сборники (по ~7 ГБ каждый выпуск, суммарно с архивом прошлых лет — сотни ГБ): шаги по разным поверхностям, дерево, металл, огонь, толпа, оружие, взрывы, окружение, монстры, интерф | Собственная лицензия Sonniss GDC Bundle License (проприетарная, не CC) | не нужна | ✅ проверено |
| **[Freesound.org (фильтр License: CC0)](https://freesound.org/)** | Свыше 380 000 отдельных звуков с лицензией CC0 (из ~734 000 всего) — очень точечный поиск: конкретные шаги, скрип двери, крики, дождь, костёр, металл о металл и т.п. | CC0 1.0 Universal (при выборе фильтра CC0 в поиске) | не нужна | — не проверялось |
| **[Kenney — RPG Audio / Interface Sounds / Impact Sounds / UI Audio](https://kenney.nl/assets/category:Audio)** | Готовые тематические паки: RPG Audio (шаги, оружие, фоли), Impact Sounds (130 ударов/попаданий), Interface Sounds (100 файлов), UI Audio (50 файлов кнопок/переключателей) — все нормализованы по громко | CC0 1.0 (Creative Commons Zero / общественное достояние) | не нужна | ✅ проверено |
| [OpenGameArt.org — коллекции «CC0 Sound Effects» / «CC0 Background Ambience»](https://opengameart.org/content/cc0-sound-effects) | Курированные подборки CC0-звуков специально под игры: заклинания, существа, бой (удары, оружие), окружение (шаги, двери, вода, листья), интерфейс, отдельно — фоновые эмбиенсы (лес, ветер, помещения). | CC0 (у каждой отдельной работы указано на её собственной странице) | не нужна | — не проверялось |
| [Incompetech (Kevin MacLeod)](https://incompetech.com/music/royalty-free/music.html) | Свыше 2000 треков разных настроений и жанров — спокойные фольклорные/акустические темы, тревожные напряжённые треки, короткие боевые стингеры. | Creative Commons Attribution 3.0/4.0 (CC BY) на бесплатный трек; есть платная безатрибуционная лицензия | **нужна** | ✅ проверено |
| [Alexander Nakarada / CreatorChords (экс-SerpentSound Studios)](https://creatorchords.com/) | Оркестрово-фэнтезийные и кельтские темы, эпик, драматичные/напряжённые (tense) треки — жанры Fantasy, Celtic, Epic, Cinematic. | Creative Commons Attribution 4.0 (CC BY) на бесплатной раздаче | **нужна** | ✅ проверено |
| [Pixabay Audio (Music + Sound Effects)](https://pixabay.com/music/) | Большой каталог фоновой музыки (эмбиент, напряжённая, боевая) и отдельных SFX без строгой рубрикации по жанрам/сеттингам. | Pixabay Content License (собственная, не CC) | не нужна | ✅ проверено |
| [99Sounds — бесплатные паки (99 Sound Effects, Cinematic Sounds и др.)](https://99sounds.org/free-sound-effects/) | Тематические паки в 24-бит WAV: кинематографичные импакты, свуши, взрывы/удары для трейлеров, отдельные sci-fi паки (не подходят под сеттинг). | Собственная royalty-free лицензия 99Sounds (не CC) | не нужна | ✅ проверено |
| [Zapsplat (бесплатный аккаунт)](https://www.zapsplat.com/) | Очень большая поисковая библиотека SFX и музыки на все случаи — шаги, дерево, металл, толпа, погода, интерфейс. | Zapsplat Standard License (собственная) | **нужна** | ✅ проверено |

**Зачем это нам:**

- **#GameAudioGDC Bundle (Sonniss)** — Основной источник для «мяса» звукового дизайна: шаги по земле/снегу/дереву частокола, скрип дерева хаты, металл (ковка, оружие), огонь (костёр, пожар), звук взрыва для боевой сцены, гул толпы для села. Без атрибуции — можно сразу класть в билд. _Пайплайн: не применимо (аудио)._ _Формат: WAV, обычно 44.1–96kHz, россыпь по категориям._
  - Оговорка: Нельзя перепродавать или раздавать сами звуки как звуки (только внутри готовой игры); строго запрещено использование для тренировки ИИ. Файлы качаются частями по годам — надо аккуратно вести список источников на всякий случай, хотя атрибуция не требуется.
  - Проверка: Claim confirmed. One extra restriction not in the original entry: the page states AI/ML training use is 'strictly prohibited under our licence terms' — worth knowing if any tooling would train on the audio.
- **Freesound.org (фильтр License: CC0)** — Точечная добавка того, чего нет в готовых бандлах: конкретный скрип ворот частокола, карканье ворон над перевалом, отдельные крики для инцидента, капли дождя на крыше хаты. Важно: фильтровать именно CC0 — на сайте много CC BY и CC BY-NC вперемешку. _Пайплайн: не применимо (аудио)._ _Формат: WAV/FLAC/MP3, разное качество (сообщество)._
  - Оговорка: Лицензия указана ПОШТУЧНО у каждого файла — нельзя полагаться на общий бренд сайта, каждый раз проверять карточку конкретного звука перед использованием (на странице есть блок License). Часть звуков — только CC BY-NC, их брать нельзя.
- **Kenney — RPG Audio / Interface Sounds / Impact Sounds / UI Audio** — Прямое попадание в «интерфейс поста»: клики, подтверждение назначения, переключение вкладок — Kenney исторически лучший бесплатный источник именно для UI-звуков. Impact/RPG-паки — заготовка под удары в боевой сцене. _Пайплайн: не применимо (аудио)._ _Формат: OGG/WAV, короткие сэмплы._
  - Оговорка: Звуки чистые и «геймдевные», не заточены под этнографию карпатского села — использовать для UI и общих импактов, не для атмосферы места.
  - Проверка: None found on the checked pack; the category page itself doesn't show licenses inline, but every individual asset page does and all say CC0, matching the claim.

## Стилизация: шейдеры и инструменты

| Ассет | Что внутри | Лицензия | Атрибуция | Проверено |
|---|---|---|---|---|
| **[Outline-Effect (cakeslice)](https://github.com/cakeslice/Outline-Effect)** | Пост-эффект обводки выбранных Renderer'ов (Mesh/Sprite/Line), настраиваемые цвет и толщина линии, HDR-поддержка. | MIT License | **нужна** | ✅ проверено |
| **[URP Toon Shader (Delt06)](https://github.com/Delt06/urp-toon-shader)** | Cel-шейдинг с режимами per-vertex/per-pixel освещения, встроенный inverted-hull outline, rim-light, поддержка теней, SRP Batcher/GPU instancing. | MIT License | **нужна** | ✅ проверено |
| **[Post Processing Stack v2 (Built-in) / URP Volume post-processing](https://github.com/Unity-Technologies/PostProcessing)** | Bloom, color grading, vignette, grain, depth of field, ambient occlusion и т.д. | Unity Companion License for Unity-dependent projects → **Unity Companion License** | **нужна** | ⚠️ лицензия иная |
| **[Fungus](https://github.com/snozbot/fungus)** | Flowchart-редактор диалогов, готовый SayDialog с портретом и именем говорящего, несколько портретов на персонажа по тегам, управление камерой/спрайтами, ветвления, переменные. | MIT License | **нужна** | ✅ проверено |
| [water-urp](https://github.com/daniel-ilett/water-urp) | Стилизованная вода с пеной, преломлением и цветовыми зонами по глубине. | MIT License | **нужна** | ✅ проверено |
| [Unity-GrassAndFur](https://github.com/Propagant/Unity-GrassAndFur) | Shell-texture рендеринг травы/меха с ветром и инструментом рисования по поверхности. | MIT License | **нужна** | ✅ проверено |
| [2D Tilemap Extras (Isometric Rule Tile)](https://github.com/Unity-Technologies/2d-extras) | Isometric Rule Tile, Hex Rule Tile, Rule Override Tile, Group/Line/Random/GameObject Brush. | Unity Companion License for Unity-dependent projects | не нужна | ✅ проверено |
| [Yarn Spinner for Unity](https://github.com/YarnSpinnerTool/YarnSpinner-Unity) | Движок ветвящихся диалогов, компилятор Yarn-скриптов, локализация, надстройки Speech Bubbles / Dialogue Wheel; использовался в коммерческих играх (Night in the Woods, A Short Hike). | MIT License | **нужна** | ✅ проверено |
| [Toon Shader Free (id 21288)](https://assetstore.unity.com/packages/vfx/shaders/toon-shader-free-21288) | Базовый toon-шейдер (детали не раскрыты на странице листинга). | Standard Unity Asset Store EULA | не нужна | ✅ проверено |

**Зачем это нам:**

- **Outline-Effect (cakeslice)** — Готов работать прямо сейчас на текущем Built-in RP без смены пайплайна: подсветка протагониста на изометрической карте, выделение говорящего портрета в диалоговой сцене. _Пайплайн: Built-in RP (image effect); URP не задокументирован, нужна адаптация._ _Формат: C# camera image effect (OnRenderImage) + шейдер, репозиторий на GitHub._
  - Оговорка: Репозиторий давно не обновлялся; совместимость с URP не заявлена явно — при переходе на URP придётся портировать вручную.
  - Со страницы лицензии: «MIT License, Copyright (c) 2015 José Guerreiro — permits commercial use, copy, modify, sell, provided the copyright notice and license text are included in distributions.»
- **URP Toon Shader (Delt06)** — Основной стилизованный шейдер для персонажей и построек карпатского села (частокол, хаты) — если проект перейдёт на URP; даёт cel-shading и контур в одном материале. _Пайплайн: ТРЕБУЕТ URP (жёстко завязан)._ _Формат: UPM-пакет (Packages/com.deltation.toon-shader), шейдеры + инструменты._
  - Оговорка: Автор пометил репозиторий как «no longer actively maintained», преемник — отдельный проект Toon RP, в этой сессии не проверялся. Требует URP, которого сейчас в manifest.json нет.
  - Проверка: Real author is Vladislav Kantaev, not 'Ivan' as listed in the provider field — minor factual slip, doesn't change the license verdict.
- **Post Processing Stack v2 (Built-in) / URP Volume post-processing** — Атмосфера туманного перевала и вечерний свет в портретных сценах; даёт «настоящую» картинку без художника по свету. _Пайплайн: PPv2 только Built-in RP (несовместим с URP); Volume-постобработка требует URP._ _Формат: Пакет Unity через Package Manager: com.unity.postprocessing для Built-in, либо Volume-стек внутри URP._
  - Оговорка: Два разных, НЕ взаимозаменяемых пакета под два pipeline — выбор Built-in vs URP нужно зафиксировать до настройки постобработки. У Unity Companion License есть условия использования (обычно не мешают инди-разработке, но стоит свериться с текстом).
  - Проверка: Record claimed attribution:'none' — wrong. UCL requires the license text/copyright notice in substantial portions of the Work, and the grant is conditional on holding a valid Unity engine license (met here, but not unconditional). Also worth checking whether the project (URP, Unity 6.4) even uses this legacy v2 GitHub package vs. the built-in URP Volume framework shipped with the Editor.
- **Fungus** — Готовый инструмент под портретные диалоговые сцены первого часа (портрет + реплика + ветвление) без написания UI с нуля — прямое попадание в задачу «минимальный, но настоящий срез». _Пайплайн: Не привязан к pipeline (UI на uGUI)._ _Формат: Unity-пакет/.unitypackage, визуальный flowchart-редактор._
  - Оговорка: Сопровождается сообществом, не Unity Technologies; совместимость с Unity 6.4 не проверена в этой сессии; кастомизация под свой арт-стиль потребует переработки префабов SayDialog.
  - Проверка: License claim is correct (MIT, commercial OK, must keep copyright/license notice). Not a license issue but worth noting: repo banner says it's no longer actively maintained; community fork is at github.com/Fungus-Community-Edition/Fungus.

## Что отвергнуто и почему

Отказ так же полезен, как находка: чтобы не искать это повторно.

- **Стилизация: шейдеры и инструменты:** Kino (keijiro) — отклонён: пост-эффекты работают ТОЛЬКО на HDRP (нужен HDRP 7.1+), а HDRP не рассматривается. Toony Colors Pro 2 — отклонён: платный ассет Asset Store (~$75), лицензии CC0/MIT нет. Сайты-репаки (unityassetsbox.com, unityassets4free.com, gfx-station.com, gameassetsfree.com, unityunreal.com, unityassetpack.com, unityassetcollection.com) — отклонены целиком как нелегальные перезаливы платных ассетов (RealToon, Toon Shader URP и др.) — запрещено правилом проекта, не открывались и не используются как источник ссылок. Animated Outline & Toon Shader URP (Asset Store) — не проверен: похож на платный листинг, бесплатность не подтверждена, поэтому не включён.
- **Эффекты: взрывы, огонь, дым:** 1) govfx.itch.io/realistic-smoke-vfx-free-pack — 404, лицензию проверить не удалось. 2) «VFX Graph - Stylized Smoke - Vol. 1» и «unity urp vfx fire toon particle» (itch.io) — на VFX Graph, минимум требуют URP; отложено до миграции пайплайна. 3) POLYGON Particle FX Pack (Synty) — платный. 4) Explosion Particles Sprite Atlas (TheJosh, CC0) — валиден, но это пересборка кадров Kenney Smoke Particles в атлас 512×512, дублирует уже отобранный пак Kenney. 5) Explosion Sheet (StumpyStrust, CC0) — один файл boom3.png, перекрывается более полным «More Explosions» того же автора. NonCommercial-лицензий, репаков и чужих торговых марок в выдаче не встретилось.
- **Окружение: село, хаты, частокол, лес, горы:** EmaceArt Stylized Rock Pack — стилистически подходит, но ПЛАТНЫЙ ($1.24–4.99), не «бескоштовний». Synty Studios POLYGON — известный источник стилизованных сред, но их village/nature паки платные; бесплатного полноценного набора не проверял отдельно (не хотел выдумывать ссылку). Результаты CGTrader/TurboSquid/Meshy/Free3D по "palisade"/"wooden fence" — маркетплейсы с разными лицензиями на каждую модель, ни одна страница индивидуально не открыта и не проверена, слишком общий источник. "Low poly castle" (Cosmo) и "PSX style going medieval" (valsekamerplant) с itch.io — найдены в CC0-подборке, но лицензию и стиль отдельно не проверял; PSX-стиль вероятно не сочетается визуально. Дубликат Quaternius Low Poly Nature Pack на OpenGameArt — тот же контент, что уже указан отдельно, не включаю повторно.
- **Текстуры, материалы, небо:** FreePBR.com — отклонено: бесплатно только некоммерчески, коммерческая лицензия — разовая оплата $16. Нарушает требование №1. Quixel Megascans (Fab/Epic) — отклонено: безлимитный доступ для всех движков был только до конца 2024, с 2025 большая часть платная по Fab Standard License, EULA сложный, заточен под Unreal/Fab, не CC0. Textures.com (бывший CGTextures) — отклонено как основной источник: бесплатный аккаунт даёт только небольшую страницу примеров, полная библиотека — за платные кредиты. cc0-textures.com — отдельная запись не добавлена: прежний домен/зеркало ambientCG, контент и лицензия совпадают. JulioVII 'Bricks & Wood' (itch.io) — отклонено: запрет перепродажи, лицензия ещё не CC0 («plans to release as CC0»). Безымянные itch.io-паки — не включены: найдены только в сниппетах, страницы с точным текстом лицензии не открывал.
- **Шрифты с украинской кириллицей:** PT Sans/PT Serif (Paratype) — лицензия (ParaType Free Font License) разрешает коммерцию и модификацию, но шрифты сделаны для проекта «Public Types of the Russian Federation» под российские госструктуры — провенанс-конфликт с сеттингом (враг — «орда из-за хребта»); отклонены по этой причине, а не по лицензии. Golos Text (Paratype) — тоже заказан для российских госсервисов, и type.today фиксирует брак в украинской диакритике (перекладина в є, наложение акцентов в ї). Отклонён по обеим причинам. Roboto, Inter, Manrope, Arimo, Oswald, Nunito, Overpass, Alumni Sans — все разобраны в type.today «Neo-Grotesques» с явными дефектами под украинский (смещённые/налезающие акценты; у Alumni Sans нет Ґ/ґ вообще). Не проходят «полную украинскую кириллицу». Montserrat, Comfortaa, Caveat, Philosopher, Marck Script — не включил: нет проверяемого источника про украинские диакритики именно у них.
- **Персонажи: модели и анимации:** 1) Kenney "Character Assets" (kenney.itch.io) — CC0, отличный набор, но платный (от $15, только урезанный free-сэмпл) — отклонён по цене. 2) EmaceArt "Slavic Medieval Villagers" (emaceart.itch.io) — лучшее тематическое попадание (карпатское село, крестьяне, props, 24 анимации), но минимум $7 на itch.io и кастомный 63-костный риг не нативен Unity Humanoid. Отклонён по цене — пересмотреть при бюджете. 3) unityassetcollection.com, gameassetsfree.com — репаки платных Unity-ассетов (найден репак Mecanim-пака) — несанкционированные перезаливы, исключены категорически. 4) Платные POLYGON Fantasy/Apocalypse Characters (Synty) — не бесплатны, не рассматривались; вошёл только free Starter Pack. 5) CC BY-NC гуманоидных паков в поиске не встретилось.
- **Интерфейс: панели, иконки:** Fantasy Minimal Pixel Art GUI (veyroa, itch.io) — заявлен CC0, но в комментариях указано, что пак собран из чужих ассетов с конфликтующими лицензиями (ToffeeCraft — запрет редистрибуции; ETA — только некоммерческое использование с credit); автор ответил про «правки», не раскрыв какие. Чистота не подтверждена — не берём. ZSS Game Lab Free Fantasy RPG UI Kit (itch.io) — на странице нет текста лицензии (pay what you want), плюс «AI-assisted workflow». Двойная неопределённость — отклонено. Pixel Art Icon Pack - RPG (cainos, itch.io) — лицензия чистая, но контент — предметы/руда/еда, не UI-панели/кнопки; стиль pixel-art не подходит под изометрию, ближе к категории «ресурсы». Rune Foundry Circular Frames Mini Pack — найден поиском, страницу лицензии открыть не успел — не включаю без проверки.
- **Портреты именных персонажей (музейный open access):** Третьяковская галерея — лучший по стилю (Рєпін, Крамской, Перов), но требует письменное разрешение музея на коммерческое использование, некоммерческое — только с указанием "Из собрания Третьяковской галереи". Не проходит по требованию №1. Часть тех же картин уже на Wikimedia Commons отдельными загрузками — брать оттуда с проверкой файла. Google Arts & Culture — условия площадки запрещают коммерческое использование платформы; права остаются у музеев-партнёров. Те же музеи отдают те же изображения напрямую под CC0 через свои порталы. NAMU (namu.ua) — не нашёл заявленной программы открытого доступа/CC0; есть виртуальные туры, но нет страницы с явной лицензией на переиспользование. piiixl "100+ Dialogue Portraits" — лицензия рабочая, но пиксель-арт 32×32/64×64, не живопись — не подходит под "нарисованные портреты", годится лишь как UI-плейсхолдер.
- **Звук: атмосфера, музыка, эффекты:** 1. Tabletop Audio — идеально по духу, но лицензия эмбиенсов CC BY-NC-ND: NonCommercial запрещает продаваемую игру. Автор неформально допускает «немного подзаработать», это не коммерческая лицензия. 2. BBC Sound Effects Archive — 16000 WAV, но лицензия RemArc — только личное/образовательное/исследовательское, запрещает зарабатывать. Коммерция — отдельно платно. 3. Ambient-Mixer.com / rpg.ambient-mixer.com — часть под CC Sampling Plus 1.0 (запрет коммерческой рекламы); агрегатор пользовательских миксов без построчной проверки — не включено. 4. Unity Asset Store — поиск под карпатское село не дал бесплатных совпадений (найденные «Medieval Village...» паки платные); непроверенный URL подставлять не стал. 5. Free Music Archive — смешивает лицензии от CC0 до CC BY-NC-SA построчно; профильные источники дали более предсказуемый результат.

## Замечания исследователей

- **Стилизация: шейдеры и инструменты:** Пайплайн проекта пока не выбран (сейчас Built-in RP, URP/HDRP в manifest.json нет), поэтому список сознательно смешивает: пайплайн-независимые ядровые инструменты (Outline-Effect, Fungus, Tilemap Extras) и вещи, которые фиксируют выбор в пользу URP (urp-toon-shader, water-urp, GrassAndFur, URP Volume-постобработка). Каждая запись помечена, чего требует. Лицензии проверялись открытием LICENSE-файлов/README на GitHub и страницы EULA на Asset Store, а не по памяти. «2D Tilemap Extras» полезен только при 2D/2.5D-тайловом подходе к сцене — это отдельное архитектурное решение, не зафиксированное в репозитории.
- **Эффекты: взрывы, огонь, дым:** Все core/good-записи здесь — легаси Particle System (Shuriken), не VFX Graph, и явно совместимы с Built-in RP, что совпадает с текущим состоянием репозитория (manifest.json без URP/HDRP — пайплайн ещё не выбран). VFX Graph требует минимум URP, поэтому такие паки отправлены в rejected — не из-за лицензии, а потому что физически не заведутся без миграции пайплайна. По лицензиям: Kenney-паки (включая зеркала на OpenGameArt) — настоящий CC0, самый безопасный вариант, атрибуция не нужна вообще. Остальные — Standard Unity Asset Store EULA: коммерческое использование разрешено, атрибуция не требуется, но запрещена перепродажа ассета отдельно от игры и публикация «как есть» в открытом исходном коде — это ограничение лицензии, а не запрет NonCommercial.
- **Окружение: село, хаты, частокол, лес, горы:** База: Kenney Fantasy Town Kit + Kenney Nature Kit + Quaternius Medieval Village MegaKit + Fertile Soil Modular Village Pack — все CC0, прямоугольный грид (кроме KayKit — гекс-сетка). Никто из проверенных не требует URP/HDRP: все читаются как обычные FBX/OBJ/glTF, подходят под нынешний Built-in RP репозитория (в manifest.json нет URP/HDRP); риск только у Low Poly Cliff Pack (2018, не проверен визуально) и Source-шейдеров Quaternius (целятся в SRP — брать raw FBX). Частокол явно нигде не назван — ближе всего заборы/ворота Fertile Soil (но без UV-текстур, флэт-цвет). Полноценных гор/утёсов в CC0-наборах нет — единственный найденный вариант не CC0 (Unity Asset Store standard EULA).
- **Текстуры, материалы, небо:** Все core/good-записи — чистый CC0 или CC0-эквивалент без атрибуции. AllSky Free (Unity Asset Store EULA, не CC0) — временный плейсхолдер неба, совместим с Built-in RP без решения по URP/HDRP; заменить на HDRI из Poly Haven при финализации. Ни один источник не требует URP/HDRP. Для соломенной кровли и снега — прямые совпадения: Poly Haven 'Thatch Roof Angled', ambientCG 'ThatchedRoof002A/B', 'Snow004/006' — брать в первую очередь.
- **Шрифты с украинской кириллицей:** Апостроф в украинской орфографии — обычный типографский апостроф (U+2019 ’), не отдельная кириллическая буква; есть в пунктуационном наборе почти любого латинского шрифта, отдельно по каждому кандидату не искал — но перед импортом в Unity/TMP стоит проверить на строке вида «м'ята». Рекомендуемая пара для первого часа: Fixel (UI/реплики) + Nyght Serif (заголовки/титры) — самый «украинский по духу» дуэт; Noto Sans+Serif — подстраховка на случай проблем с хинтингом Fixel/Nyght в TMP; IBM Plex — запасной профессиональный вариант; e-Ukraine — тематически точнее всего, но нужно вручную сверить лицензию на сайте (инструмент чтения страницы не открыл её дважды) и учесть обязательную атрибуцию.
- **Персонажи: модели и анимации:** Built-in RP (в manifest.json нет URP/HDRP): все модели — FBX+PNG, рендерятся из коробки; переход на URP потребует Convert Materials позже. Humanoid-риг: при смешении Quaternius+KayKit+Mixamo+Mecanim-пака нужен Avatar Definition на каждого персонажа; ретаргетинг заявлен, но не проверен на этом репо — тестировать на одном персонаже перед тиражированием. Портреты для диалогов — отдельная категория (музейные open-access коллекции), не входит в это задание. helpx.adobe.com/creative-cloud/faq/mixamo-faq.html не открылась (HTTP 403) — лицензия Mixamo дана по стороннему источнику, см. caveat записи. Даже без обязательной атрибуции стоит вести CREDITS.md с датой скачивания и версией лицензии.
- **Интерфейс: панели, иконки:** Курсоры — только у Kenney (Cursor Pack, CC0). Иконки под скилы/пороги проверок — только game-icons.net закрывает это качественно и полно (CC BY 3.0, атрибуция обязательна списком авторов). Панели/кнопки под сеттинг «карпатское село» лучше всего у orabon (резное дерево/пергамент, hand-painted), но это не CC-лицензия, а собственные условия автора без явного пункта об атрибуции — перед стартом продаж стоит написать автору за письменным подтверждением. Все форматы — плоский 2D (PNG/SVG), выбор Built-in vs URP/HDRP в manifest.json на них не влияет.
- **Портреты именных персонажей (музейный open access):** Порядок действий: (1) 3–5 портретов под именных напарников/боссов первой сцены — Met/Smithsonian Open Access, фильтр CC0, теги "peasant"/"elder"/"portrait"; (2) этнографический фон карпатского села — Wikimedia Commons (передвижники) и точечно Europeana (польские/словацкие музеи), с проверкой лицензии каждого файла; (3) для прототипа UI портретной сцены до готовности финального арта — CC0-заглушки с OpenGameArt. Ни один источник не завязан на URP/HDRP — это растровые JPEG/PNG, пайплайн Unity влияет только на последующую обработку/тонирование под общий стиль сцены.
- **Звук: атмосфера, музыка, эффекты:** Фильтрация по CC0: на Freesound — блок «licenses» в панели результатов (CC0 ~381000 из ~734000 звуков на сентябрь 2026), но лицензия у каждого файла своя — проверять карточку. На OpenGameArt лицензия — в панели каждой карточки; «CC0»-коллекции — просто список ссылок, имя не гарантия. Pixabay и Kenney — единая лицензия на весь каталог, фильтровать не нужно. Sonniss — отдельная проприетарная лицензия на весь архив. Общий принцип «бесплатно ≠ общественное достояние»: расплывчатое «royalty-free»/«free for commercial use» (99Sounds, Zapsplat) — не public domain, почти всегда нужна атрибуция или аккаунт.

## Минимальная витрина: чем собрать первый час

Это не «взять всё», а самый короткий набор, на котором первый час
(«Перевал», `docs/FIRST_HOUR.md`) можно увидеть глазами. Всё, кроме
иконок и музыки, — CC0: ни атрибуции, ни условий.

| Что нужно в срезе | Чем берём | Лицензия |
|---|---|---|
| Село на перевале: хаты, ворота, частокол | Kenney **Fantasy Town Kit** | CC0 |
| Лес и склон вокруг | Kenney **Nature Kit** (тот же масштаб) | CC0 |
| Протагонист и жители, ходьба по селу | Quaternius **Universal Base Characters** + **Modular Outfits** + **Universal Animation Library** | CC0 |
| Портреты Максима, Мирославы, Тугара, Захара | **Met Open Access** и **Wikimedia Commons** (живопись в PD) | CC0 / PD, статус проверяется у каждого файла |
| Взрыв и дым в сцене боя | Kenney **Particle Pack** + **Smoke Particles** | CC0 |
| Панели, кнопки, курсор | Kenney **UI Pack** + **RPG Expansion** + **Cursor Pack** | CC0 |
| Иконки навыков и порогов | **game-icons.net** | CC BY 3.0 — **нужна атрибуция** |
| Шрифт реплик и интерфейса | **Fixel** (украинский, OFL) | OFL 1.1 |
| Шрифт «бумажных» экранов (доклады, журнал) | **Noto Serif** | OFL 1.1 |
| Шаги, дерево, металл, интерфейс | Kenney **audio-паки** | CC0 |
| Точечные звуки (скрип, костёр, толпа) | **Freesound** с фильтром CC0 | CC0 |
| Музыка сцены | **Incompetech** или **CreatorChords** | CC BY — **нужна атрибуция** |

Почему именно так: Kenney и Quaternius держат единый масштаб и стиль
внутри своих наборов, поэтому село собирается без подгонки пропорций.
Портреты из музейной живописи — не экономия, а следствие Поправки №2:
все именные персонажи и так из общественного достояния, и портрет кисти
XIX века садится на них точнее, чем стоковая иллюстрация.

**Две ловушки, которые видно уже сейчас.**

- **Fixel — variable-шрифт.** TextMeshPro с осями не работает: нужно
  выгрузить статические начертания. Это десять минут, но узнать о них
  лучше до, а не после вёрстки интерфейса.
- **Стиль китов — «фэнтези-городок», а не Карпаты.** Геометрия подходит,
  палитра — нет. Перекраска материалов под сеттинг обязательна, иначе
  срез будет выглядеть как чужая демка.

## Что это требует от проекта

- **Решение о пайплайне — первое и самое дорогое.** В `Packages/manifest.json`
  сейчас нет ни URP, ни HDRP: проект на Built-in. **Рекомендация — URP.**
  Модели и текстуры из списка выше от пайплайна не зависят (FBX + PNG),
  а вот постобработка, стилизованные шейдеры и VFX Graph в URP доступны
  сразу. Цена: часть бесплатных пакетов эффектов сделана во времена
  Built-in (War FX, Cartoon FX) — сами системы частиц работают, но их
  материалы придётся перевести на URP-шейдеры. Обратный порядок хуже:
  переезд собранной сцены с Built-in на URP ломает все материалы разом.
- **Файл атрибуции.** Ассеты под CC BY и подобным требуют указания автора.
  Заводится `ATTRIBUTION.md` в корне и пополняется в том же коммите, что и ассет.
- **Правило на будущее:** ассет кладётся в репозиторий вместе со строкой о
  лицензии. Ассет без записанной лицензии — это мина, а не экономия времени.

