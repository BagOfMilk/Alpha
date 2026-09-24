using System;
using System.Collections.Generic;
using System.Linq;
using Game.Core.Characters.Creation;

namespace Game.Gameplay.Text
{
    /// <summary>
    /// ЄДИНА текстова таблиця тестової збірки (R7, docs/TEST_BUILD.md §7): Core
    /// віддає лише ключі (building.&lt;id&gt;, post.&lt;id&gt;, skill.&lt;key&gt;, TopicId
    /// подій, ...), а слова живуть тут. Українською, без винятків: жодного
    /// Core-контентного <c>DisplayName</c> гравець не бачить — тільки текст із
    /// цієї таблиці за ключем.
    ///
    /// Рід протагоніста (і будь-якого іншого підмета репліки — компаньйона,
    /// ворога) — параметр лукапу, не частина ключа з боку викликача: якщо для
    /// ключа <c>"x"</c> існує варіант <c>"x.m"</c>/<c>"x.f"</c>, обирається він,
    /// інакше повертається сам ключ <c>"x"</c> (§7, підсумковий абзац).
    ///
    /// Замінює (коли D2 переключить викликачів — R7/§5.1, координовано, не
    /// зараз): <c>Gameplay/SceneLines.cs</c>, <c>tools/Shared/SceneText.cs</c>,
    /// <c>tools/Shared/SignalText.cs</c> і текстову частину <c>VillageView</c>.
    /// Ці три файли навмисно НЕ видалені й НЕ перемкнуті цим пакетом (E3) — вони
    /// самодостатні (власна таблиця всередині) і працюють, як і раніше, доки їх
    /// користувачів не перевели на цю таблицю.
    ///
    /// Чиста C# (без <c>UnityEngine</c>) — компілюється і в
    /// <c>Game.Gameplay.Lint</c>, і в <c>tools/Game.Tests.Headless</c> прямим
    /// <c>&lt;Compile Include&gt;</c> (той самий прийом, що вже є для
    /// <c>SceneText.cs</c>/<c>SignalText.cs</c>/<c>SeededDiceRoller.cs</c>).
    /// </summary>
    public static class UkrainianText
    {
        /// <summary>Видимий маркер відсутнього тексту — навмисно кидається в очі під час прогону.</summary>
        public static string MissingMarker(string key) => "[" + (key ?? "null") + "]";

        private static readonly Dictionary<string, string> Table = BuildTable();

        /// <summary>Усі ключі таблиці (варіанти <c>.m</c>/<c>.f</c> — окремими записами).</summary>
        public static IReadOnlyList<string> AllKeys { get; } =
            Table.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

        /// <summary>
        /// Явний перелік для тесту покриття §6.2 (<c>Coverage_EveryEmittedKeyExistsInTable</c>):
        /// D2 порівнює цей список із тим, що фактично вилетіло з бот-прогону.
        /// Сьогодні — уся таблиця (кожен ключ тут навмисний, жодного «про запас»).
        /// </summary>
        public static IReadOnlyList<string> RequiredKeys() => AllKeys;

        /// <summary>Ключ існує (як варіант за родом, або як базовий ключ) — і НЕ повертає заглушку.</summary>
        public static bool Has(string key, Gender gender)
        {
            if (string.IsNullOrEmpty(key)) return false;
            return Table.ContainsKey(ResolveVariant(key, gender));
        }

        public static bool Has(string key, bool isFemale) => Has(key, isFemale ? Gender.Female : Gender.Male);

        /// <summary>
        /// Текст за ключем і родом підмета репліки. <c>key.m</c>/<c>key.f</c>
        /// перекриває <c>key</c>, коли існує (§7); інакше — сам <c>key</c>.
        /// Відсутній ключ повертає видиму заглушку (детектується тестом).
        /// </summary>
        public static string Get(string key, Gender gender)
        {
            if (string.IsNullOrEmpty(key)) return MissingMarker(key);
            string resolved = ResolveVariant(key, gender);
            return Table.TryGetValue(resolved, out var text) ? text : MissingMarker(key);
        }

        public static string Get(string key, bool isFemale) => Get(key, isFemale ? Gender.Female : Gender.Male);

        /// <summary>
        /// Текст за ключем із підстановкою <c>{ім'я}</c>-плейсхолдерів. Аргументи —
        /// пари "ім'я","значення" підряд (непарний хвіст ігнорується). Заглушка
        /// відсутнього ключа повертається без підстановки.
        /// </summary>
        public static string Format(string key, Gender gender, params string[] args)
        {
            string template = Get(key, gender);
            if (args == null || args.Length < 2) return template;

            var sb = new System.Text.StringBuilder(template);
            int pairs = args.Length / 2;
            for (int i = 0; i < pairs; i++)
            {
                string name = args[i * 2];
                string value = args[i * 2 + 1] ?? "";
                sb.Replace("{" + name + "}", value);
            }
            return sb.ToString();
        }

        public static string Format(string key, bool isFemale, params string[] args) =>
            Format(key, isFemale ? Gender.Female : Gender.Male, args);

        private static string ResolveVariant(string key, Gender gender)
        {
            string variant = key + (gender == Gender.Female ? ".f" : ".m");
            return Table.ContainsKey(variant) ? variant : key;
        }

        // ------------------------------------------------------------------
        // Побудова таблиці. Кожен розділ називає підрозділ docs/TEST_BUILD.md,
        // з якого взято ключі й текст (§7 — буквально, решта — переклад/
        // дороблення контенту, що вже живе в Core/старих текстових заглушках).
        // ------------------------------------------------------------------

        private static Dictionary<string, string> BuildTable()
        {
            var t = new Dictionary<string, string>(StringComparer.Ordinal);

            AddScenePassNeighbour(t);      // §7.1
            AddNode1(t);                  // §7.2
            AddPostsAndAssignment(t);      // §7.3
            AddNight(t);                   // §7.4
            AddPostReports(t);              // §7.5
            AddIncidentSpoiledStores(t);    // §7.6
            AddQuestHafiya(t);               // §7.7 (+ phase-B: keys-phaseB.json)
            AddIncidentSickChild(t);         // §7.8
            AddFactions(t);                  // §7.9
            AddWorkshopBuilding(t);          // §7.10
            AddDungeonAbandonedCamp(t);      // §7.11 (+ phase-B drafts)
            AddCraft(t);                     // §7.12
            AddCouncilActions(t);            // §7.13 (+ real CityWorks/CityWorksStep event keys)
            AddCrisisTest(t);                // §7.14
            AddFinale(t);                    // §7.15
            AddSummary(t);                   // §7.16
            AddCreation(t);                  // §7.17 (+ phase-B backgrounds)
            AddBuildPlanner(t);              // §7.18
            AddSaveTitleTraining(t);         // §7.19
            AddBattleUiAndLog(t);            // §7.20
            AddSkillsAttrsResourcesBandsChars(t); // §7.21

            // За межами §7, буквально: ідентифікатори контенту з Core (R7:
            // "building.<id>, post.<id>, site.<id>, skill.<key>, attr.<key>,
            // item.<id>, enemy.<id>, faction.<id>, ..."), знайдені grep'ом по
            // Default*.cs (звіт пакета E3 перелічує джерело кожного блоку).
            AddBuildingIds(t);
            AddPostIds(t);
            AddSiteIds(t);
            AddItemIds(t);
            AddEnemyAndWeaponAndAbilityIds(t);
            AddScarIds(t);
            AddCompanionArcIds(t);
            AddOldHermitageDungeon(t);
            AddStatusesAndWoundTiers(t);
            AddCouncilResultFeedback(t);

            // Портовано з тимчасових текстових заглушок (SceneLines/SceneText/
            // SignalText/VillageView) — амбієнт напруги/ночі, заголовки
            // інцидентів і подій міста, слова настрою. Старі файли НЕ
            // видалені (E3 цього не робить — узгоджено з D2), але їхній вміст
            // тут перевикладений українською під конвенцію ключів R7, щоб D2
            // мав звідки перемикати виклики без другого проходу перекладу.
            AddIncidentHeadlinesAndOutcomes(t);
            AddCityAndCouncilEvents(t);
            AddAmbientSignals(t);

            // Ключі-«стрічка подій»: кожен GameEvent.Key із §2/§4.3/§6.1, що
            // потрапляє у DayLog як окрема подія і повинен мати рядок для
            // EventFeedScreen (D2/E1 підключать пізніше — тут лише текст).
            AddGameEventFeedLines(t);

            // ==== E2: бойова презентація (арена/портрети/HUD) — див. блок унизу файлу. ====
            AddBattlePresentationE2(t);

            return t;
        }

        private static void AddKey(Dictionary<string, string> t, string key, string text)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("Порожній ключ тексту.");
            if (string.IsNullOrEmpty(text)) throw new ArgumentException("Порожній текст для ключа: " + key);
            if (t.ContainsKey(key)) throw new InvalidOperationException("Дублікат текстового ключа: " + key);
            t[key] = text;
        }

        // ---- §7.1 Сцена «Сусід з претензією» (доба 1, ранок) ----
        private static void AddScenePassNeighbour(Dictionary<string, string> t)
        {
            AddKey(t, "scene.pass.tuhar.offer",
                "Тугар Вовк: «Пропусти авангард — і громада дістане свою долю зі здобичі. " +
                "Відмовишся — прийдуть усі одразу, і ти вже не питатимеш, кого пропускати.»");
            AddKey(t, "scene.pass.tuhar.threat",
                "Тугар Вовк: «Не думай, що встигнеш зібрати всіх на раду. Орда не чекає на балачки старійшин.»");
            AddKey(t, "scene.pass.zakhar.refuses",
                "Захар Беркут: «Ми не торгуємо перевалом. Ані з тобою, ані з тими, хто йде за тобою.»");
            AddKey(t, "scene.pass.myroslava.aside",
                "Мирослава (тихо): «Батько говорить не за себе. Слухай, що він НЕ каже.»");
        }

        // ---- §7.2 Вузол 1 — точка рішення і тактичний бій (доба 1, день) ----
        private static void AddNode1(Dictionary<string, string> t)
        {
            AddKey(t, "node1.decision.title", "Авангард орди вийшов на перевал. Часу на раду немає.");
            AddKey(t, "node1.option.quiet", "Тихо: умовити Тугара (Переконання ≥ P) — відступ за добу, нуль ран.");
            AddKey(t, "node1.option.bloody", "Криваво: засідка на стежці (Тактика ≥ T) — бій, тримати скелю над стежкою.");
            AddKey(t, "battle.pass_vanguard.intro", "Розвідники орди вже на стежці. Максим і Мирослава — поруч.");

            AddKey(t, "node1.outcome.best.m",
                "Максим і Мирослава — обидва з тобою. Склад цілий. Громада бачила, як ти стояв на перевалі.");
            AddKey(t, "node1.outcome.best.f",
                "Максим і Мирослава — обидва з тобою. Склад цілий. Громада бачила, як ти стояла на перевалі.");
            AddKey(t, "node1.outcome.good.m",
                "Максим ранений, лежить у лазареті кілька днів. Мирослава з тобою. Склад цілий.");
            AddKey(t, "node1.outcome.good.f",
                "Максим ранений, лежить у лазареті кілька днів. Мирослава з тобою. Склад цілий.");
            AddKey(t, "node1.outcome.base",
                "Ти з Максимом стоїш. Мирослава йде за батьком — назад до орди. Склад розграбований.");
            AddKey(t, "node1.outcome.worst",
                "Максим ранений. Мирослава йде. Склад розграбований, і громада тепер боїться голосно говорити.");
            AddKey(t, "signal.node1.myroslava_left",
                "Мирослава пішла за батьком ще до заходу сонця. Ніхто не наздогнав.");
        }

        // ---- §7.3 Розстановка та пости (доба 1, вечір) ----
        private static void AddPostsAndAssignment(Dictionary<string, string> t)
        {
            AddKey(t, "post.council_seat", "Місце в раді");
            AddKey(t, "post.storehouse_dock", "Причал складу");
            AddKey(t, "post.infirmary_bed", "Койка лазарету");
            AddKey(t, "post.settlement_market", "Ринок поселення");
            AddKey(t, "post.settlement_farms", "Ферми поселення");
            AddKey(t, "post.workshop_bench", "Верстак майстерні");
            AddKey(t, "post.scouting_post", "Розвідпост");
            AddKey(t, "post.empty", "Порожньо — і це видно всім.");
            AddKey(t, "signal.settlement.shelter_given.m",
                "Громада дає тобі дах не за золото — за те, що ти стояв на перевалі.");
            AddKey(t, "signal.settlement.shelter_given.f",
                "Громада дає тобі дах не за золото — за те, що ти стояла на перевалі.");
        }

        // ---- §7.4 Ніч і патруль ----
        private static void AddNight(Dictionary<string, string> t)
        {
            AddKey(t, "ui.night.title", "Патрулювати чи спати?");
            AddKey(t, "ui.night.patrol", "Патрулювати (втрачаєш відпочинок, чуєш більше)");
            AddKey(t, "ui.night.sleep", "Спати (лікуєшся швидше, чуєш менше)");
            AddKey(t, "forewarn.level1", "Собака третю ніч гавкає на щось за частоколом.");
            AddKey(t, "forewarn.level2", "Третій день топчеться одна й та сама пара слідів біля межі.");
            AddKey(t, "forewarn.level3", "Щось готують. Скоро.");
        }

        // ---- §7.5 Доповіді з постів (2 варіанти на домен: зайнято/порожньо — AUDIT G13/G14) ----
        private static void AddPostReports(Dictionary<string, string> t)
        {
            AddKey(t, "post.report.storehouse.good", "Дід Овсій: «Рахунок сходиться. Поки що.»");
            AddKey(t, "post.report.storehouse.silence", "З причалу складу — тиша. Нема кому доповісти.");
            AddKey(t, "post.report.infirmary.good", "Знахарка Гафія: «Рани гояться, як має. Тримайте їх у теплі.»");
            AddKey(t, "post.report.infirmary.silence", "Лазарет порожній зверху донизу. Ніхто не скаже, як там хворі.");
            AddKey(t, "post.report.council.good", "Захар Беркут: «Рада йде, як має йти.»");
            AddKey(t, "post.report.council.silence", "Місце в раді порожнє — і громада це бачить.");
            AddKey(t, "post.report.market.good", "Ринок працює, хоч і без розмаху.");
            AddKey(t, "post.report.market.silence", "Ринок стоїть порожній — торгувати нема кому.");
            AddKey(t, "post.report.farms.good", "Ферми дають своє, без надлишку й без нарікань.");
            AddKey(t, "post.report.farms.silence", "На фермах нікого — поле чекає рук.");
            AddKey(t, "post.report.workshop.good", "Верстак не стоїть без роботи.");
            AddKey(t, "post.report.workshop.silence", "Верстак мовчить — нікому братися за роботу.");
            AddKey(t, "post.report.scouting.good", "Розвідпост доповідає вчасно й по суті.");
            AddKey(t, "post.report.scouting.silence", "Розвідпост порожній — нікому дивитися за околицею.");
        }

        // ---- §7.6 Інцидент spoiled_stores (доба 2) ----
        private static void AddIncidentSpoiledStores(Dictionary<string, string> t)
        {
            AddKey(t, "incident.spoiled_stores.title", "Частина запасів зіпсована або забрана.");
            AddKey(t, "incident.spoiled_stores.option.quiet", "Тихо: домовитися чи вивідати правду (Торгівля/Виживання ≥ 5).");
            AddKey(t, "incident.spoiled_stores.option.bloody", "Криваво: вибити зізнання силою (Залякування ≥ 6).");
            AddKey(t, "incident.spoiled_stores.outcome.best", "Дід Овсій знаходить майже все.");
            AddKey(t, "incident.spoiled_stores.outcome.good", "Половину повернено.");
            AddKey(t, "incident.spoiled_stores.outcome.base", "Повернено мало.");
            AddKey(t, "incident.spoiled_stores.outcome.worst", "Нічого не повернено, і по селу шепочуть.");
        }

        // ---- §7.7 Квест Гафії «Гірка розрада» (+ phase-B: keys-phaseB.json) ----
        private static void AddQuestHafiya(Dictionary<string, string> t)
        {
            AddKey(t, "quest.hafiya.offer", "Знахарка Гафія: «На дальніх схилах росте гірка трава. Принесіть, якщо буде час.»");
            AddKey(t, "quest.hafiya.offer.option.accept", "Принести траву");
            AddKey(t, "quest.hafiya.offer.option.decline", "Не зараз");
            AddKey(t, "quest.hafiya.declined", "Гафія лише кивнула. «Не всі встигають усе.»");
            AddKey(t, "quest.hafiya.stage2.found", "Трава знайдена. Гафія кивнула — а таке буває нечасто.");
            AddKey(t, "quest.hafiya.stage2.missing", "Трави нема. «Обійдемося тим, що є.»");
            AddKey(t, "quest.hafiya.stage3.best", "Дитина одужує. Гафія лишає обряд собі — а подяку віддає тобі при всіх.");
            AddKey(t, "quest.hafiya.stage3.worst", "Дитина одужує повільніше, ніж могла б. Гафія цього не забуде.");
        }

        // ---- §7.8 Інцидент sick_child (доба 3) ----
        private static void AddIncidentSickChild(Dictionary<string, string> t)
        {
            AddKey(t, "incident.sick_child.title", "Дівчинку, ранену в набігу, принесли до лазарету.");
            AddKey(t, "incident.sick_child.option.quiet", "Лікувати (Медицина).");
            AddKey(t, "incident.sick_child.outcome.best", "Гафія впоралася без зайвого дня.");
            AddKey(t, "incident.sick_child.outcome.good", "Дитина одужає за кілька днів.");
            AddKey(t, "incident.sick_child.outcome.base", "Одужання йде важко.");
            AddKey(t, "incident.sick_child.outcome.worst", "Лазарет порожній — нікому було лікувати.");
        }

        // ---- §7.9 Фракції ----
        private static void AddFactions(Dictionary<string, string> t)
        {
            AddKey(t, "faction.community", "Громада Тухольщини");
            AddKey(t, "faction.tuhar_boyars", "Бояри Тугара");
            AddKey(t, "faction.horde", "Орда (Бурунда)");
            AddKey(t, "faction.band.hostile", "Ворожість");
            AddKey(t, "faction.band.wary", "Стриманість");
            AddKey(t, "faction.band.neutral", "Байдужість");
            AddKey(t, "faction.band.awaiting", "Вичікування");
            AddKey(t, "faction.band.allied", "Союз");
            AddKey(t, "signal.faction.tuhar_boyars.wary", "Тугар усе ще вичікує — але недовго.");
            AddKey(t, "signal.faction.tuhar_boyars.hostile", "Тугар більше не вдає, що на нашому боці.");
            AddKey(t, "forewarn.tugar.level1", "Хтось бачив боярина за частоколом.");
            AddKey(t, "forewarn.tugar.level2", "Тугар усе частіше зникає з двору саме тоді, коли треба радитися.");
            AddKey(t, "forewarn.tugar.level3", "Боярин уже не приховує, з ким вечеряє.");
        }

        // ---- §7.10 Стройка (Майстерня) ----
        private static void AddWorkshopBuilding(Dictionary<string, string> t)
        {
            AddKey(t, "building.workshop.ordered", "Рада замовляє Майстерню. Ліс уже звозять.");
            AddKey(t, "building.workshop.stage", "Риштування росте — стадія {stage} з 5.");
            AddKey(t, "building.workshop.ready", "Майстерня готова. Верстак чекає на руки і на матеріал.");
        }

        // ---- §7.11 Данж «Покинутий табір авангарду» (доба 4) (+ phase-B drafts) ----
        private static void AddDungeonAbandonedCamp(Dictionary<string, string> t)
        {
            AddKey(t, "site.abandoned_camp", "Покинутий табір авангарду");
            AddKey(t, "dungeon.depart", "Троє йдуть повернути забране. Пости лишаються порожні на дві доби.");
            AddKey(t, "dungeon.room1.title", "Кілька розвідників орди не встигли втекти з табору.");
            AddKey(t, "dungeon.room1.quiet", "Тихо: обійти (Виживання ≥ 5) або переконати здатися (Переконання ≥ 5).");
            AddKey(t, "dungeon.room1.bloody", "Криваво: короткий бій (Ближній бій/Тактика ≥ 6).");
            AddKey(t, "dungeon.room2.title", "У схованці під возом — те, що орда не встигла забрати.");
            AddKey(t, "item.scout_horn.found", "Ріг розвідника. Той самий, яким орда подавала сигнали — тепер він подаватиме їх нам.");
            AddKey(t, "item.scout_horn.effect", "Ефект: наступні два передвісники чуються чіткіше й раніше.");
            AddKey(t, "dungeon.room3.title", "Прихований попіл — і під ним ще щось ціле.");
            AddKey(t, "dungeon.room3.greedy", "Забрати все зерно (більше здобичі, вищий ризик).");
            AddKey(t, "dungeon.room3.cautious", "Забрати менше, спалити слід (менше здобичі, спокійніше).");
            AddKey(t, "dungeon.extract", "Здобич збережено: {materials} матеріалів, {gold} золота.");
            AddKey(t, "dungeon.wiped", "Бій пішов не так. Усе незбережене втрачено — троє повертаються з порожніми руками, але живі.");
            AddKey(t, "dungeon.room.bypassed", "Кімнату пройдено без бою.");
        }

        // ---- §7.12 Крафт ----
        private static void AddCraft(Dictionary<string, string> t)
        {
            AddKey(t, "craft.confirm", "Підняти якість Рогу розвідника коштуватиме матеріалів майстерні. Назад не буде.");
            AddKey(t, "craft.done", "Ріг розвідника тепер чутніший, ніж будь-коли.");
        }

        // ---- §7.13 Рада (нові дії) — + реальні ключі CityWorks/CityWorksStep ----
        private static void AddCouncilActions(Dictionary<string, string> t)
        {
            AddKey(t, "council.raid.ordered", "Рада скликає облаву — вулиці стануть спокійнішими за кілька днів.");
            AddKey(t, "council.settlers.ordered", "Рада приймає переселенців — ротів стане більше, як і рук.");
            AddKey(t, "council.decree.ordered", "Рада видає указ — одна фракція втрачає, інша дістає.");
            AddKey(t, "council.diplomacy.ordered", "Посольство вирушає — слово коштує дешевше за зброю, поки воно діє.");
            AddKey(t, "council.investment.ordered", "Рада вкладає золото наперед — і чекає віддачі не одразу.");
            AddKey(t, "council.prepare_threat.ordered", "Громада готується — це видно не сьогодні, а тоді, коли знадобиться.");
            AddKey(t, "council.outfit_expedition.ordered", "Загін споряджають краще, ніж завжди — за це заплачено наперед.");

            // Реальні літерали з Core/Base/Buildings/CityWorks*.cs (не однакові зі
            // словами вище — інтегратор ще не звів назви; лишаю обидва набори,
            // щоб покриття не впало, коли D1 підключить стрічку подій напряму
            // до сирих ключів CityEvent, а не до перелічених вище "*.ordered").
            AddKey(t, "council.raid", "Варта виходить в облаву — вулиці спокійніші за кілька днів.");
            AddKey(t, "council.invest.payout", "Вкладення ради дало віддачу: золото повернулося з надлишком.");
            AddKey(t, "council.trade_discount", "Торговий підхід на посту зіграв — ціна нижча.");
        }

        // ---- §7.14 Форсована тест-криза «Вогонь на в'їзді» (доба 5) ----
        private static void AddCrisisTest(Dictionary<string, string> t)
        {
            AddKey(t, "crisis.test.warn", "Хтось бачив дим біля в'їзду ще до світанку. Це не звичайний ранок.");
            AddKey(t, "crisis.test.window", "Є час діяти — до ночі.");
            AddKey(t, "crisis.test.mitigated", "Вогонь погашено вчасно. Дехто не спав усю ніч заради цього.");
            AddKey(t, "crisis.test.unmitigated", "Вогонь дійшов до крайньої хати. Хтось постраждав — і громада це бачила.");
        }

        // ---- §7.15 Фінал (доба 5, ніч) — реальний, обидва шляхи ----
        private static void AddFinale(Dictionary<string, string> t)
        {
            AddKey(t, "finale.tuhar.present", "Тугар Вовк стоїть поруч із Бурундою — саме там, де його бачили в перший день.");
            AddKey(t, "finale.burunda.present", "Бурунда-бегадир: сила без хитрощів, і перевал перед ним — просто перешкода.");
            AddKey(t, "finale.myroslava.ally", "Мирослава — у строю громади, попри батька.");
            AddKey(t, "finale.myroslava.enemy", "Мирослава — серед чужих. Вона не дивиться в твій бік.");
            AddKey(t, "finale.option.quiet", "Тихо: загатити річку (Механіка/Тактика — пороги залежать від Готовності громади).");
            AddKey(t, "finale.option.bloody", "Криваво: тримати перевал — тактичний бій, сила орди залежить від Готовності громади.");
            AddKey(t, "battle.finale.intro", "Перевал за тобою. Орда — попереду. Хто з громади готовий — поруч.");
            AddKey(t, "finale.outcome.best", "Річка бере на себе те, що мала б узяти громада. Перевал цілий. Ціна — шлях назад для декого з тих, хто пішов з водою.");
            AddKey(t, "finale.outcome.good", "Дамба тримає. Дехто заплатив тим, що лишився на тому боці.");
            AddKey(t, "finale.outcome.base", "Перевал тримають — дорого, але тримають.");
            AddKey(t, "finale.outcome.worst", "Перевал тримають — і рахунок за це прийде довгий.");
        }

        // ---- §7.16 Підсумок доби 5 і титри ----
        private static void AddSummary(Dictionary<string, string> t)
        {
            AddKey(t, "summary.title", "П'ять діб на перевалі.");
            AddKey(t, "summary.roster", "Хто з тобою, хто в лазареті, хто пішов — і чому.");
            AddKey(t, "summary.village", "Що збудовано, скільки в гаманці, чи чути страх у голосах.");
            AddKey(t, "summary.tuhar", "Де тепер Тугар, і хто про нього говорить.");
        }

        // ---- §7.17 Створення протагоніста (R12) (+ phase-B backgrounds) ----
        private static void AddCreation(Dictionary<string, string> t)
        {
            AddKey(t, "ui.creation.title", "Хто ти на цьому перевалі?");
            AddKey(t, "ui.creation.name", "Ім'я");
            AddKey(t, "ui.creation.name.default.m", "Провідник");
            AddKey(t, "ui.creation.name.default.f", "Провідниця");
            AddKey(t, "ui.creation.gender", "Рід");
            AddKey(t, "ui.creation.background.title.m", "Звідки ти прийшов");
            AddKey(t, "ui.creation.background.title.f", "Звідки ти прийшла");
            AddKey(t, "background.warrior.m", "Вигнанець зі зброєю: більше Сили й Ближнього бою, менше Кмітливості.");
            AddKey(t, "background.warrior.f", "Вигнанка зі зброєю: більше Сили й Ближнього бою, менше Кмітливості.");
            AddKey(t, "background.trader.m", "Мандрівний торговець: більше Кмітливості й Торгівлі, менше Сили.");
            AddKey(t, "background.trader.f", "Мандрівна торговка: більше Кмітливості й Торгівлі, менше Сили.");
            AddKey(t, "background.healer.m", "Учень знахарки: більше Волі й Медицини, менше Ловкості.");
            AddKey(t, "background.healer.f", "Учениця знахарки: більше Волі й Медицини, менше Ловкості.");
        }

        // ---- §7.18 Білд-планувальник (R11) ----
        private static void AddBuildPlanner(Dictionary<string, string> t)
        {
            AddKey(t, "ui.buildplanner.title", "Куди підеш далі");
            AddKey(t, "ui.buildplanner.points", "Вільних очок: {points}");
            AddKey(t, "ui.buildplanner.preview", "Переглянути");
            AddKey(t, "ui.buildplanner.commit.confirm", "Це рішення незворотне. Підтвердити?");
            AddKey(t, "ui.buildplanner.commit.done", "Вибір зроблено.");
        }

        // ---- §7.19 Збереження/завантаження, титул, тренувальний бій ----
        private static void AddSaveTitleTraining(Dictionary<string, string> t)
        {
            AddKey(t, "ui.title.newgame", "Нова гра");
            AddKey(t, "ui.title.continue", "Продовжити");
            AddKey(t, "ui.title.training", "Тренувальний бій");
            AddKey(t, "ui.title.quit", "Вийти");
            AddKey(t, "ui.title.hitrule.percent", "Правило попадання: показаний відсоток");
            AddKey(t, "ui.title.hitrule.threshold", "Правило попадання: показаний поріг");
            AddKey(t, "ui.save.slot", "Слот {slot}: {headline}, доба {day}");
            AddKey(t, "ui.save.slot.empty", "Слот {slot}: порожньо");
            AddKey(t, "ui.save.autosave", "Автозбереження — щоранку");
            AddKey(t, "ui.training.title", "Тренувальний бій — оцінка механіки бою поза кампанією.");
        }

        // ---- §7.20 Бойовий інтерфейс, автобій, overwatch ----
        private static void AddBattleUiAndLog(Dictionary<string, string> t)
        {
            AddKey(t, "ui.battle.ap", "Очки дій: {current}/{max}");
            AddKey(t, "ui.battle.ap_reserved", "У дозорі: {reserved}");
            AddKey(t, "ui.battle.overwatch.button", "Дозор");
            AddKey(t, "ui.battle.overwatch.indicator", "У дозорі");
            AddKey(t, "ui.battle.autoresolve", "Автобій");
            AddKey(t, "ui.battle.victory", "Перемога");
            AddKey(t, "ui.battle.defeat", "Поразка");
            AddKey(t, "combat.overwatch.triggered.line", "{attacker} стріляє з дозору по {target}.");
            AddKey(t, "combat.attack.hit.m", "Влучив.");
            AddKey(t, "combat.attack.hit.f", "Влучила.");
            AddKey(t, "combat.attack.graze.m", "Зачепив частково.");
            AddKey(t, "combat.attack.graze.f", "Зачепила частково.");
            AddKey(t, "combat.attack.crit.m", "Влучив критично.");
            AddKey(t, "combat.attack.crit.f", "Влучила критично.");
            AddKey(t, "combat.attack.miss.m", "Не влучив.");
            AddKey(t, "combat.attack.miss.f", "Не влучила.");
        }

        // ---- §7.21 Довідкові підписи (скіли/атрибути/ресурси/полоси/лояльність) ----
        private static void AddSkillsAttrsResourcesBandsChars(Dictionary<string, string> t)
        {
            AddKey(t, "skill.ranged", "Стрілецтво");
            AddKey(t, "skill.melee", "Ближній бій");
            AddKey(t, "skill.tactics", "Тактика");
            AddKey(t, "skill.lockpick", "Злом");
            AddKey(t, "skill.mechanics", "Механіка");
            AddKey(t, "skill.survival", "Виживання");
            AddKey(t, "skill.medicine", "Медицина");
            AddKey(t, "skill.persuade", "Переконання");
            AddKey(t, "skill.intimidate", "Залякування");
            AddKey(t, "skill.trade", "Торгівля");

            AddKey(t, "attr.strength", "Сила");
            AddKey(t, "attr.agility", "Спритність");
            AddKey(t, "attr.wits", "Кмітливість");
            AddKey(t, "attr.will", "Воля");

            AddKey(t, "resource.gold", "Золото");
            AddKey(t, "resource.materials", "Матеріали");
            AddKey(t, "resource.food", "Їжа");

            AddKey(t, "band.best", "Найкраща");
            AddKey(t, "band.good", "Хороша");
            AddKey(t, "band.base", "Базова");
            AddKey(t, "band.worst", "Найгірша");

            AddKey(t, "loyalty.band.broken", "Зламана");
            AddKey(t, "loyalty.band.resentful", "Ображена");
            AddKey(t, "loyalty.band.wary", "Обережна");
            AddKey(t, "loyalty.band.steady", "Стійка");
            AddKey(t, "loyalty.band.devoted", "Віддана");

            AddKey(t, "readiness.band.unprepared", "Непідготовлена");
            AddKey(t, "readiness.band.bracing", "Насторожена");
            AddKey(t, "readiness.band.ready", "Готова");
            AddKey(t, "readiness.band.fortified", "Укріплена");

            AddKey(t, "char.maksym", "Максим Беркут");
            AddKey(t, "char.myroslava", "Мирослава");
            AddKey(t, "char.zakhar", "Захар Беркут");
            AddKey(t, "char.keeper", "Дід Овсій");
            AddKey(t, "char.healer", "Знахарка Гафія");
            AddKey(t, "char.tuhar", "Тугар Вовк");
            AddKey(t, "char.horde_commander", "Бурунда-бегадир");
            AddKey(t, "char.protagonist.m", "Провідник");
            AddKey(t, "char.protagonist.f", "Провідниця");

            AddKey(t, "enemy.horde_skirmisher", "Застрільник орди");
            AddKey(t, "enemy.horde_raider", "Наскочник орди");
        }

        // ==================================================================
        // За межами §7: ідентифікатори з Default*.cs (Core), знайдені grep'ом.
        // ==================================================================

        // Core/Base/Buildings/DefaultBuildings.cs — 10 будівель US-7.1.
        private static void AddBuildingIds(Dictionary<string, string> t)
        {
            AddKey(t, "building.infirmary", "Лазарет");
            AddKey(t, "building.workshop", "Майстерня");
            AddKey(t, "building.storehouse", "Склад");
            AddKey(t, "building.council_hall", "Зала ради");
            AddKey(t, "building.market", "Ринок");
            AddKey(t, "building.tavern", "Таверна");
            AddKey(t, "building.temple", "Храм");
            AddKey(t, "building.fortifications", "Укріплення");
            AddKey(t, "building.armory", "Збройня");
            AddKey(t, "building.laboratory", "Лабораторія");
        }

        // Core/DefaultContent.cs (AllSlots) — усі слоти бази, включно з lab_station,
        // якого §3.0/§7.3 не називають (восьмий слот, приходить із Laboratory, US-6.4).
        private static void AddPostIds(Dictionary<string, string> t)
        {
            AddKey(t, "post.lab_station", "Дослідницький стіл");
        }

        // Core/Expeditions/DefaultSites.cs + Core/Dungeons/DefaultDungeon.cs (site id'и).
        private static void AddSiteIds(Dictionary<string, string> t)
        {
            AddKey(t, "site.outskirts", "Ближні розвалини");
            AddKey(t, "site.old_workshop", "Занедбана майстерня");
            AddKey(t, "site.far_highway", "Далекий шлях");
            AddKey(t, "site.old_hermitage", "Старий скит");
        }

        // Core/Items/DefaultItems.cs — базовий дроп-пул + іменні предмети.
        private static void AddItemIds(Dictionary<string, string> t)
        {
            AddKey(t, "item.scavenged_knife", "Знайдений ніж");
            AddKey(t, "item.worn_vest", "Потертий каптан");
            AddKey(t, "item.hunters_bow", "Мисливський лук");
            AddKey(t, "item.scouting_gear", "Розвідницьке спорядження");
            AddKey(t, "item.aegis_plate", "Егіда");
            AddKey(t, "item.scout_horn", "Ріг розвідника");
        }

        // Core/Combat/DefaultCombatContent.cs — вороги/зброя/здібності доби 1 і фіналу.
        private static void AddEnemyAndWeaponAndAbilityIds(Dictionary<string, string> t)
        {
            AddKey(t, "enemy.horde_scout", "Розвідник орди");
            AddKey(t, "enemy.tuhar_boyar", "Боярин Тугара");
            AddKey(t, "enemy.burunda", "Бурунда-бегадир");
            // enemy.horde_skirmisher / .horde_raider — уже в §7.21 (буквально зі спеки).

            AddKey(t, "weapon.horde_bow", "Лук орди");
            AddKey(t, "weapon.horde_spear", "Спис орди");
            AddKey(t, "weapon.boyar_saber", "Шабля боярина");
            AddKey(t, "weapon.burunda_mace", "Булава Бурунди");

            AddKey(t, "ability.lunge", "Ривок");
            AddKey(t, "ability.set_trap", "Пастка");
            AddKey(t, "ability.move_order", "Наказ пересунутися");
            AddKey(t, "ability.volley", "Залп");
        }

        // Core/Characters/Scars/DefaultScars.cs.
        private static void AddScarIds(Dictionary<string, string> t)
        {
            AddKey(t, "scar.one_eyed", "Одноокий");
            AddKey(t, "scar.limp", "Кульгавий");
            AddKey(t, "scar.broken_hand", "Перебита рука");
            AddKey(t, "scar.haunted", "Обпалений страхом");
        }

        // Core/Companions/DefaultArcs.cs — заголовки арок Мирослави/Максима.
        private static void AddCompanionArcIds(Dictionary<string, string> t)
        {
            AddKey(t, "arc.myroslava.title", "Арка Мирослави");
            AddKey(t, "arc.myroslava.ch1.title", "Довіра, що росте");
            AddKey(t, "arc.myroslava.ch2.title", "Вибір лишитися");
            AddKey(t, "arc.maksym.title", "Арка Максима");
            AddKey(t, "arc.maksym.ch1.title", "Вірність понад образу");
            AddKey(t, "arc.maksym.ch2.title", "Побратим до кінця");
        }

        // Core/Dungeons/DefaultDungeon.cs (OldHermitageRooms) — другий данж, вільна гра.
        private static void AddOldHermitageDungeon(Dictionary<string, string> t)
        {
            AddKey(t, "dungeon.hermitage.room1.title", "Розбійник вартує двір старого скиту. Живим тут не раді.");
            AddKey(t, "dungeon.hermitage.room2.title", "Погріб скиту вцілів. Ченці забрали не все.");
            AddKey(t, "enemy.forest_bandit", "Лісовий розбійник");
        }

        // CompanionStatus / WoundTier — довідкові підписи для RosterScreen/InfirmaryScreen.
        private static void AddStatusesAndWoundTiers(Dictionary<string, string> t)
        {
            AddKey(t, "status.idle", "Вільний");
            AddKey(t, "status.assigned", "На посту");
            AddKey(t, "status.on_mission", "У загоні");
            AddKey(t, "status.injured.m", "Ранений");
            AddKey(t, "status.injured.f", "Ранена");
            AddKey(t, "status.resting", "На лікуванні");
            AddKey(t, "status.dead.m", "Загинув");
            AddKey(t, "status.dead.f", "Загинула");
            AddKey(t, "status.antagonist", "Проти нас");

            AddKey(t, "wound.light", "Легка рана");
            AddKey(t, "wound.serious", "Серйозна рана");
            AddKey(t, "wound.critical", "Тяжка рана");

            AddKey(t, "combat.status.bleeding", "Кровотеча");
            AddKey(t, "combat.status.stunned", "Оглушення");
            AddKey(t, "combat.status.suppressed", "Придушення");
            AddKey(t, "combat.status.knocked_down", "Збитий з ніг");
            AddKey(t, "combat.status.marked", "Позначений");
            AddKey(t, "combat.status.burning", "Горіння");
            AddKey(t, "combat.status.poisoned", "Отруєний");
        }

        // CouncilOrderResult — коротка причина відмови ради (UI-фідбек команд Order*).
        private static void AddCouncilResultFeedback(Dictionary<string, string> t)
        {
            AddKey(t, "ui.council.result.queued", "Замовлено. Виконається за свій термін.");
            AddKey(t, "ui.council.result.applied", "Зроблено одразу.");
            AddKey(t, "ui.council.result.no_council_hall", "Без Зали ради рішення нікому ухвалювати.");
            AddKey(t, "ui.council.result.already_queued", "Уже в черзі — вдруге не піде.");
            AddKey(t, "ui.council.result.on_cooldown", "Зарано. Рада ще не готова до цього знову.");
            AddKey(t, "ui.council.result.not_enough_gold", "Золота не досить.");
            AddKey(t, "ui.council.result.not_enough_food", "Їжі не досить.");
            AddKey(t, "ui.council.result.unknown_faction", "Ця фракція раді не відома.");
            AddKey(t, "ui.council.result.building_not_built", "Такої будівлі ще нема — вкладати нема куди.");
        }

        // ==================================================================
        // Перевикладено з тимчасових заглушок (SceneLines/SceneText/SignalText/
        // VillageView) — заголовки інцидентів, події міста, амбієнт напруги.
        // Стара нейборська сцена (SceneLines/SceneText: "Сосед с претензией")
        // НЕ перенесена буквально — її переграла й замінила краща й іменна
        // §7.1 ("Сусід з претензією" → Тугар/Захар/Мирослава). Старі ключі
        // scene.opening.neighbour.*/scene.neighbour.*/scene.pass.title|best|
        // good|base|worst/sfx.door.slam лишаються тільки в SceneLines.cs/
        // SceneText.cs (самодостатні, не читають цю таблицю) — звіт пакета
        // E3 називає це рішення явно.
        // ==================================================================

        private static void AddIncidentHeadlinesAndOutcomes(Dictionary<string, string> t)
        {
            // Заголовки — переклад VillageView.Headline(...) switch (§incident.*).
            AddKey(t, "incident.petty_theft.title", "Зі складу пропадають припаси.");
            AddKey(t, "incident.market_brawl.title", "Бійка на ринку.");
            AddKey(t, "incident.protection_racket.title", "До торговців приходять за часткою.");
            AddKey(t, "incident.missing_person.title", "Пропала людина.");
            AddKey(t, "incident.night_burglary.title", "Нічна крадіжка.");
            AddKey(t, "incident.night_arson.title", "Підпал.");
            AddKey(t, "incident.crisis_riot.title", "Бунт на площі.");
            // pass_vanguard дублює node1.decision.title — той самий вузол, інший TopicId.
            AddKey(t, "incident.pass_vanguard.title", "Авангард орди вийшов на перевал. Часу на раду немає.");

            // Пороги — з Core/World/DefaultIncidents.cs (SkillKeys/Threshold буквально).
            AddKey(t, "incident.petty_theft.option.quiet", "Тихо: домовитися (Переконання ≥ 5).");
            AddKey(t, "incident.petty_theft.option.bloody", "Криваво: залякати винного (Залякування ≥ 4).");
            AddKey(t, "incident.market_brawl.option.quiet", "Тихо: розвести сторони торгом (Торгівля ≥ 6).");
            AddKey(t, "incident.market_brawl.option.bloody", "Криваво: розігнати бійку силою (Ближній бій ≥ 5).");
            AddKey(t, "incident.protection_racket.option.quiet", "Тихо: пригрозити без зброї (Залякування ≥ 8).");
            AddKey(t, "incident.protection_racket.option.bloody", "Криваво: показати зброю (Стрілецтво ≥ 6).");
            AddKey(t, "incident.missing_person.option.quiet", "Тихо: прочесати околиці (Виживання ≥ 7).");
            AddKey(t, "incident.missing_person.option.bloody", "Криваво: силою розпитати підозрюваних (Ближній бій ≥ 6).");
            AddKey(t, "incident.night_burglary.option.quiet", "Тихо: вистежити злодія (Злом ≥ 6).");
            AddKey(t, "incident.night_burglary.option.bloody", "Криваво: чекати з клинком у темряві (Ближній бій ≥ 5).");
            AddKey(t, "incident.night_arson.option.quiet", "Тихо: погасити й змовчати (Механіка ≥ 8).");
            AddKey(t, "incident.night_arson.option.bloody", "Криваво: знайти й покарати підпалювача (Стрілецтво ≥ 7).");
            AddKey(t, "incident.crisis_riot.option.quiet", "Тихо: заспокоїти натовп словом (Переконання ≥ 10).");
            AddKey(t, "incident.crisis_riot.option.bloody", "Криваво: придушити бунт тактично (Тактика ≥ 9).");

            // Загальні полоси наслідку — коли в інцидента нема своєї фрази (переклад
            // VillageView.Verb(OutcomeBand): "разобрались лучше некуда" / "...чисто" /
            // "кое-как уладили" / "вышло скверно"). Специфічні інциденти (spoiled_stores,
            // sick_child) мають власні — див. §7.6/§7.8, ці не перекривають їх.
            AddKey(t, "incident.petty_theft.outcome.best", "Крадія знайшли, і майже все повернулося на місце.");
            AddKey(t, "incident.petty_theft.outcome.good", "Половину зниклого повернуто.");
            AddKey(t, "incident.petty_theft.outcome.base", "Розібралися абияк — крадій пішов непокараний.");
            AddKey(t, "incident.petty_theft.outcome.worst", "Нічого не знайшли, і крадуть далі.");

            AddKey(t, "incident.market_brawl.outcome.best", "Сторони розійшлися самі, без синців і без боргів.");
            AddKey(t, "incident.market_brawl.outcome.good", "Бійку розвели, торг триває.");
            AddKey(t, "incident.market_brawl.outcome.base", "Розвели силою — ринок ще довго гуде.");
            AddKey(t, "incident.market_brawl.outcome.worst", "Ринок стоїть — торгувати нікому, всі налякані.");

            AddKey(t, "incident.protection_racket.outcome.best", "Побори скасовано — торговці дихнули вільно.");
            AddKey(t, "incident.protection_racket.outcome.good", "Побори зменшено, поки що.");
            AddKey(t, "incident.protection_racket.outcome.base", "Побори лишились — торговці платять і мовчать.");
            AddKey(t, "incident.protection_racket.outcome.worst", "Побори зросли. Торговці шукають іншого ринку.");

            AddKey(t, "incident.missing_person.outcome.best", "Знайшли живим — і не одного: із ним прибилися ще люди.");
            AddKey(t, "incident.missing_person.outcome.good", "Знайшли живим.");
            AddKey(t, "incident.missing_person.outcome.base", "Знайшли, але пізно — рана вже своя.");
            AddKey(t, "incident.missing_person.outcome.worst", "Не знайшли. Громада знає, що це означає.");

            AddKey(t, "incident.night_burglary.outcome.best", "Злодія взяли на гарячому — украдене повернулося.");
            AddKey(t, "incident.night_burglary.outcome.good", "Злодія прогнали, частину майна повернули.");
            AddKey(t, "incident.night_burglary.outcome.base", "Злодій утік із половиною здобичі.");
            AddKey(t, "incident.night_burglary.outcome.worst", "Склад обчищено — і ніхто нічого не бачив.");

            AddKey(t, "incident.night_arson.outcome.best", "Вогонь погашено, поки не побачив ніхто, крім вартового.");
            AddKey(t, "incident.night_arson.outcome.good", "Вогонь погашено, згоріло небагато.");
            AddKey(t, "incident.night_arson.outcome.base", "Погасили пізно — сарай не врятувати.");
            AddKey(t, "incident.night_arson.outcome.worst", "Погасити не встигли. Громада бачила заграву.");

            AddKey(t, "incident.crisis_riot.outcome.best", "Натовп розійшовся мирно — слово подіяло.");
            AddKey(t, "incident.crisis_riot.outcome.good", "Бунт стих, обійшлося без крові.");
            AddKey(t, "incident.crisis_riot.outcome.base", "Бунт придушили силою — рахунок за це прийде.");
            AddKey(t, "incident.crisis_riot.outcome.worst", "Площа в крові. Це надовго запам'ятають.");
        }

        // ---- Події міста/ради — переклад VillageView.CityHeadline(...) + буквальні
        // ключі з Core/Base/Buildings/CityWorksStep.cs і PopulationStep.cs. ----
        private static void AddCityAndCouncilEvents(Dictionary<string, string> t)
        {
            AddKey(t, "city.built.infirmary", "Збудовано: Лазарет.");
            AddKey(t, "city.built.workshop", "Збудовано: Майстерня.");
            AddKey(t, "city.built.storehouse", "Збудовано: Склад.");
            AddKey(t, "city.built.council_hall", "Збудовано: Зала ради.");
            AddKey(t, "city.built.market", "Збудовано: Ринок.");
            AddKey(t, "city.built.tavern", "Збудовано: Таверна.");
            AddKey(t, "city.built.temple", "Збудовано: Храм.");
            AddKey(t, "city.built.fortifications", "Збудовано: Укріплення.");
            AddKey(t, "city.built.armory", "Збудовано: Збройня.");
            AddKey(t, "city.built.laboratory", "Збудовано: Лабораторія.");

            AddKey(t, "city.tier.2", "Хутір став селом.");
            AddKey(t, "city.tier.3", "Село розрослося в слободу.");
            AddKey(t, "city.tier.4", "Слобода стала містечком.");

            AddKey(t, "city.people.arrived", "Прийшли нові люди.");
            AddKey(t, "city.people.left", "Люди пішли з поселення.");

            AddKey(t, "city.crowd.0", "На вулицях майже нікого.");
            AddKey(t, "city.crowd.1", "Вулиці рідшають.");
            AddKey(t, "city.crowd.2", "На вулицях звично людно.");
            AddKey(t, "city.crowd.3", "Людей на вулицях стало більше.");
            AddKey(t, "city.crowd.4", "Вулиці повні — і це видно звідусіль.");
        }

        // ---- Амбієнт напруги/ночі — переклад SignalText.Text(topicId) ----
        private static void AddAmbientSignals(Dictionary<string, string> t)
        {
            AddKey(t, "tension.ambient.calm", "«Добре, що ви тут.» Діти у дворах.");
            AddKey(t, "tension.ambient.murmur", "У колодязя сперечаються про ціни.");
            AddKey(t, "tension.ambient.ferment", "Розмова стихає, коли підходиш.");
            AddKey(t, "tension.ambient.heat", "Ставні зачинені вдень. Патруль ходить парами.");
            AddKey(t, "tension.ambient.fracture", "Площа порожня. Зброю носять відкрито.");

            AddKey(t, "tension.band.risen", "«Змінюється. І не в кращий бік.»");

            AddKey(t, "night.ambient.calm", "Тихо. Лише вітер.");
            AddKey(t, "night.ambient.murmur", "Десь хлопнула ставня.");
            AddKey(t, "night.ambient.ferment", "Кроки за рогом стихли, коли обернувся.");
            AddKey(t, "night.ambient.heat", "Вогні в бочках. Голоси не місцеві.");
            AddKey(t, "night.ambient.fracture", "Ані одного вогника у вікнах.");
        }

        // ---- Стрічка подій: рядок на кожен GameEvent.Key із §2/§4.3/§6.1. ----
        private static void AddGameEventFeedLines(Dictionary<string, string> t)
        {
            AddKey(t, "day.advanced", "Настав день {day}.");
            AddKey(t, "assign.made", "{companion} стає на {post}.");
            AddKey(t, "assign.cleared", "{post} звільнено.");
            AddKey(t, "night.forewarn", "Уночі щось почулося — {domain}.");
            AddKey(t, "production.leveled_up", "Виробництво на {post} зросло.");
            AddKey(t, "dungeon.push", "Загін іде глибше у підземелля.");
            AddKey(t, "loot.dropped", "Здобич: {item}.");
            AddKey(t, "craft.upgraded", "{item} покращено.");
            AddKey(t, "scar.granted", "{companion} носитиме це до кінця: {scar}.");
            AddKey(t, "loyalty.band_changed", "{companion}: тепер {band}.");
            AddKey(t, "roster.rippled", "Звістка розходиться по загону.");
            AddKey(t, "companion.defected", "{companion} більше не з нами.");
            AddKey(t, "companion.died.m", "{companion} загинув на цьому шляху.");
            AddKey(t, "companion.died.f", "{companion} загинула на цьому шляху.");
            AddKey(t, "arc.chapter_opened", "Нова глава: {chapter}.");
            AddKey(t, "quest.choice.resolved", "{quest}: рішення ухвалено.");
            AddKey(t, "faction.standing_changed", "{faction}: тепер {band}.");
            AddKey(t, "finale.resolved", "Фінал: {band}.");
            AddKey(t, "combat.autoresolved", "Бій вирішено автобоєм.");
            AddKey(t, "creation.confirmed", "Шлях обрано.");
            AddKey(t, "progression.level_up.m", "{companion} став сильнішим.");
            AddKey(t, "progression.level_up.f", "{companion} стала сильнішою.");
            AddKey(t, "game.saved", "Збережено: слот {slot}.");
            AddKey(t, "game.loaded", "Завантажено: слот {slot}.");
            AddKey(t, "char.seen", "{char} тут.");
        }

        // ==== E2: бойова презентація — HUD арени (BattleHudScreen), власний блок ====
        // ==== в кінці таблиці, щоб мердж інших пакетів лишався тривіальним.    ====
        //
        // Ключі бойового логу (combat.attack.*/combat.overwatch.triggered.line),
        // імена юнітів (char.*/enemy.*) і кнопки дозору/автобою (ui.battle.ap*,
        // ui.battle.overwatch.*, ui.battle.autoresolve, ui.battle.victory/defeat)
        // уже є в таблиці (§7.20/§7.21) — тут лише те, чого бракувало для
        // повного HUD: заголовок, чергу ходу, панель здібностей/логу, кінець
        // ходу, озброєну дію, прев'ю шансу під обидва правила попадання
        // (R1 — «поріг/точність» замість «%» для ThresholdRule), і панель
        // результату бою з «Далі» (сама подія завершення й втрати вже мають
        // ключі — combat.autoresolved/companion.died.*/scar.granted, §7 і
        // «стрічка подій» вище).
        private static void AddBattlePresentationE2(Dictionary<string, string> t)
        {
            AddKey(t, "ui.battle.title", "Бій");
            AddKey(t, "ui.battle.current_unit", "Хід: {name}");
            AddKey(t, "ui.battle.enemyturn", "Хід ворога…");
            AddKey(t, "ui.battle.initiative", "Черга ходу");
            AddKey(t, "ui.battle.abilities", "Здібності");
            AddKey(t, "ui.battle.log", "Хід бою");
            AddKey(t, "ui.battle.endturn", "Кінець ходу");
            AddKey(t, "ui.battle.unit.downed", "Виведений з бою — потребує допомоги");

            // Озброєна дія (гравець обрав намір, чекає кліку по тайлу/юніту арени;
            // рух/атака — завжди клік без озброєння, «розумний клік» ArmedAction.None).
            AddKey(t, "ui.battle.armed.overwatch_aim", "Приціл дозору — клацни напрямок");
            AddKey(t, "ui.battle.armed.ability", "Ціль здібності «{ability}» — клацни по тайлу чи юніту");
            AddKey(t, "ui.battle.cancel", "Скасувати (ПКМ)");
            AddKey(t, "ui.battle.action.rejected", "Дію неможливо виконати зараз.");

            // Прев'ю шансу під курсором (R1: ThresholdRule показує поріг, PercentRule — відсоток).
            AddKey(t, "ui.battle.hitchance.percent", "Шанс влучення: {value}%");
            AddKey(t, "ui.battle.hitchance.threshold", "Поріг влучення: {value}");

            // Наслідки бою, яких немає серед band'ів (band.*): Нічия/Відступ.
            AddKey(t, "ui.battle.outcome.draw", "Нічия");
            AddKey(t, "ui.battle.outcome.retreat", "Відступ");

            // Панель результату після Outcome != Ongoing.
            AddKey(t, "ui.battle.result.rounds", "Раундів: {rounds}");
            AddKey(t, "ui.battle.result.casualties.title", "Втрати:");
            AddKey(t, "ui.battle.result.casualties.none", "Без видимих втрат.");
            AddKey(t, "ui.battle.result.next", "Далі");

            // Крайовий випадок шва E1b/E2 (фікс-ревью): Enter(session) міг
            // спрацювати, поки GetBattleView() ще повертає null — порожній
            // екран без виходу неприпустимий (§BattleHudScreen.DrawUnavailablePanel).
            AddKey(t, "ui.battle.unavailable", "Бій зараз недоступний.");
            AddKey(t, "ui.battle.unavailable.exit", "Назад");
        }
    }
}
