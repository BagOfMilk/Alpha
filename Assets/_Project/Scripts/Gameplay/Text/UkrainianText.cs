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
    /// Замінила (пакет E3b, R7/§5.1): <c>Gameplay/SceneLines.cs</c> (видалено —
    /// <see cref="Game.Gameplay.ScenePlayer"/> тепер читає цю таблицю напряму),
    /// <c>tools/Shared/SceneText.cs</c>/<c>SignalText.cs</c> (видалено —
    /// <c>tools/Alpha.Play/Program.cs</c> так само) і текстову частину
    /// <c>Gameplay/VillageView.cs</c> (переведена на ключі цієї таблиці, її
    /// власна математика світла/кольору — ні).
    ///
    /// Чиста C# (без <c>UnityEngine</c>) — компілюється і в
    /// <c>Game.Gameplay.Lint</c>, і в <c>tools/Game.Tests.Headless</c>, і в
    /// <c>tools/Alpha.Play</c>/<c>Alpha.Sim</c> прямим <c>&lt;Compile Include&gt;</c>
    /// (той самий прийом, що вже є для <c>SeededDiceRoller.cs</c>).
    /// </summary>
    public static class UkrainianText
    {
        /// <summary>Видимий маркер відсутнього тексту — навмисно кидається в очі під час прогону.</summary>
        public static string MissingMarker(string key) => "[" + (key ?? "null") + "]";

        private static readonly Dictionary<string, string> Table = BuildTable();

        /// <summary>
        /// Фаза F (UI-tour autoplay, docs/TEST_BUILD.md): лічильник ключів, для
        /// яких <see cref="Get(string,Gender)"/> повернув видиму заглушку —
        /// гачок для <c>AutoplayBootstrap</c>, щоб дим-тест міг повернути код
        /// виходу 3 і перелічити прогалини в підсумковому логу, а не лише
        /// покластися на те, що хтось помітить "[...]" на скріншоті.
        /// </summary>
        private static readonly Dictionary<string, int> MissingKeyCountsInternal = new Dictionary<string, int>();

        /// <summary>Скидає лічильник відсутніх ключів — викликається на старті прогону автопрогону, щоб не змішати лічильник з попереднім прогоном у тому самому процесі.</summary>
        public static void ResetMissingKeyTracking() => MissingKeyCountsInternal.Clear();

        /// <summary>Ключі, повернуті заглушкою відколи викликали <see cref="ResetMissingKeyTracking"/> (або від старту процесу), з кількістю показів кожного.</summary>
        public static IReadOnlyDictionary<string, int> MissingKeyCounts => MissingKeyCountsInternal;

        private static void TrackMissing(string key)
        {
            string k = key ?? "null";
            MissingKeyCountsInternal.TryGetValue(k, out int count);
            MissingKeyCountsInternal[k] = count + 1;
        }

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
            if (string.IsNullOrEmpty(key)) { TrackMissing(key); return MissingMarker(key); }
            string resolved = ResolveVariant(key, gender);
            if (Table.TryGetValue(resolved, out var text)) return text;
            TrackMissing(key);
            return MissingMarker(key);
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

            // Портовано з колишніх тимчасових текстових заглушок (SceneLines/
            // SceneText/SignalText/VillageView) — амбієнт напруги/ночі,
            // заголовки інцидентів і подій міста, слова настрою. Пакет E3b
            // видалив усі три старі файли й перевів VillageView на цю
            // таблицю (§5, "Own: ... Gameplay/VillageView.cs text parts").
            AddIncidentHeadlinesAndOutcomes(t);
            AddCityAndCouncilEvents(t);
            AddAmbientSignals(t);

            // Ключі-«стрічка подій»: кожен GameEvent.Key із §2/§4.3/§6.1, що
            // потрапляє у DayLog як окрема подія і повинен мати рядок для
            // EventFeedScreen (D2/E1 підключать пізніше — тут лише текст).
            AddGameEventFeedLines(t);

            // ==================================================================
            // Пакет E3b: ключі, якими код НАСПРАВДІ говорить, а не якими його
            // задумав §7 TEST_BUILD.md. Знайдено читанням GameSession.LogEvent/
            // SignalComposer/OpeningScenes (жоден пакет цього не звіряв — §6.2
            // тест-покриття не існував до E3b): TEST_BUILD_KEYS.txt (пакет D2,
            // машинний прогін бот-політик) розійшовся з таблицею §7 у форматі
            // кількох конвенцій. Нижче — фактичні ключі, а не переклад §7 ще раз.
            // ==================================================================
            AddRealOpeningSceneKeys(t);     // Core/Scenes/OpeningScenes.cs — інша сцена й інші ключі за §7.1
            AddRealCommandFeedbackKeys(t);  // GameSession.LogEvent "голі" ключі команд, яких немає в §7
            AddIncidentBandKeys(t);         // SignalComposer: TopicId + "." + Band (Best/Good/Base/Worst), не "outcome.<band>"
            AddPostReportBandKeys(t);       // SignalComposer: "post." + кириличний DomainTag + "." + Accuracy
            AddAmbientAndThreatBandKeys(t); // SignalComposer/DungeonRun: Band.ToString() з великої літери
            AddVillageViewKeys(t);          // Gameplay/VillageView.cs — ключі власного ткача стрічки/мудборду
            AddConsoleLabels(t);            // tools/Alpha.Play/Program.cs — дрібні підписи консолі
            AddScenePlayerKeys(t);          // Gameplay/ScenePlayer.cs — підписи IMGUI портретної сцени

            // ==== E1b: оболонка екранів (Gameplay/UI/*Screen.cs) ====
            AddE1bShellCommon(t);
            AddE1bHubTabs(t);
            AddE1bDecisionAndBattleUi(t);
            AddE1bDungeonAndNightUi(t);
            AddE1bPeopleGearUi(t);
            AddE1bFeedback(t);
            AddE1bMissingEventKeys(t);
            AddE1bMoodChips(t);
            AddE1bReviewFixKeys(t);
            // ==== кінець блоку E1b ====

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
            AddKey(t, "ui.title.hitrule.section", "Правило попадання");
            AddKey(t, "ui.save.slot", "Слот {slot}: {headline}, доба {day}");
            AddKey(t, "ui.save.slot.empty", "Слот {slot}: порожньо");
            AddKey(t, "ui.save.slot.auto_label", "Автозбереження");
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

            // SessionView.TensionBand — сирий TensionBand.ToString() (Calm/
            // Murmur/Ferment/Heat/Fracture); короткий підпис-слово для
            // бейджа/шапки UI (не плутати з "tension.band.<Band>" вище —
            // те повне речення-оголошення зміни полоси на подію signal.domain).
            AddKey(t, "tension.band.label.calm", "Спокій");
            AddKey(t, "tension.band.label.murmur", "Ропіт");
            AddKey(t, "tension.band.label.ferment", "Бродіння");
            AddKey(t, "tension.band.label.heat", "Накал");
            AddKey(t, "tension.band.label.fracture", "Злам");

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
        // Перевикладено з колишніх тимчасових заглушок (SceneLines.cs/
        // SceneText.cs/SignalText.cs — усі три видалено пакетом E3b; VillageView
        // тепер читає цю таблицю, а не власний switch) — заголовки інцидентів,
        // події міста, амбієнт напруги. Стара нейборська сцена ("Сосед с
        // претензией") — це сцена, яку РЕАЛЬНО грає Core/Scenes/OpeningScenes.cs
        // СЬОГОДНІ (§7.1 "Сусід з претензією" з Тугаром/Захаром/Мирославою —
        // чернетка наступної ітерації сцени, поки ніхто не перепідключив
        // OpeningScenes на неї): її буквальні ключі (scene.opening.neighbour.*/
        // scene.neighbour.*/scene.pass.title|best|good|base|worst/sfx.door.slam/
        // to.node1.pass/to.settlement.evening) — в AddRealOpeningSceneKeys
        // (кінець файлу, пакет E3b).
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
        //
        // Пакет E3b (фікс-ревью): плейсхолдери нижче ПЕРЕЙМЕНОВАНО, щоб буквально
        // збігатися з іменами GameEvent.Args, якими GameSession.LogEvent їх
        // насправді наповнює (grep по GameSession.cs) — раніше тут стояли
        // аспіраційні назви ("{companion}", "{item}", "{post}", "{scar}",
        // "{quest}", "{faction}", "{chapter}"), яких немає в жодному
        // Args(...)-виклику: Format() підставляє ЛИШЕ пари, які прийшли,
        // тож старий текст показав би сирі фігурні дужки в консолі/UI замість
        // значення — саме той дефект, який §6.2/деливрабл (4) пакета мали ловити.
        private static void AddGameEventFeedLines(Dictionary<string, string> t)
        {
            AddKey(t, "day.advanced", "Настав день {day}.");
            AddKey(t, "assign.made", "{companionId} стає на {slotId}.");
            AddKey(t, "assign.cleared", "{slotId} звільнено.");
            AddKey(t, "night.forewarn", "Уночі щось почулося — {domain}.");
            AddKey(t, "production.leveled_up", "{companionId}: виробництво на посту зросло.");
            AddKey(t, "dungeon.push", "Загін іде глибше у підземелля.");
            AddKey(t, "loot.dropped", "Здобич: {itemId}.");
            AddKey(t, "craft.upgraded", "{itemId} покращено.");
            AddKey(t, "scar.granted", "{companionId} носитиме це до кінця: {scarId}.");
            AddKey(t, "loyalty.band_changed", "{companionId}: тепер {band}.");
            AddKey(t, "roster.rippled", "Звістка розходиться по загону.");
            AddKey(t, "companion.defected", "{companionId} більше не з нами.");
            AddKey(t, "companion.died.m", "{companionId} загинув на цьому шляху.");
            AddKey(t, "companion.died.f", "{companionId} загинула на цьому шляху.");
            AddKey(t, "arc.chapter_opened", "{companionId}: нова глава — {chapterId}.");
            AddKey(t, "quest.choice.resolved", "{questId}: рішення ухвалено — {band}.");
            AddKey(t, "faction.standing_changed", "{factionId}: тепер {band}.");
            AddKey(t, "finale.resolved", "Фінал: {band}.");
            AddKey(t, "combat.autoresolved", "Бій вирішено автобоєм.");
            AddKey(t, "creation.confirmed", "Шлях обрано.");
            AddKey(t, "progression.level_up.m", "{companionId} став сильнішим (рівень {level}).");
            AddKey(t, "progression.level_up.f", "{companionId} стала сильнішою (рівень {level}).");
            AddKey(t, "game.saved", "Збережено: слот {slot}.");
            AddKey(t, "game.loaded", "Завантажено: слот {slot}.");
            AddKey(t, "char.seen", "{char} тут.");
        }

        // ==================================================================
        // Пакет E3b: сцена відкриття, ЯК ВОНА РЕАЛЬНО ЗАВЕДЕНА в
        // Core/Scenes/OpeningScenes.cs — не §7.1 (задум §7.1 лишається
        // текстом у AddScenePassNeighbour вище як чернетка для наступної
        // ітерації сцени, коли хтось перепідключить OpeningScenes на неї;
        // сьогодні ScenePlayer/Alpha.Play програють САМЕ цю сцену).
        // ==================================================================
        private static void AddRealOpeningSceneKeys(Dictionary<string, string> t)
        {
            // Без імені підмета всередині рядка (на відміну від §7.1-style
            // "Ім'я: «…»" у AddScenePassNeighbour вище): і ScenePlayer (GUI), і
            // Alpha.Play (PrintSceneStep) уже друкують ім'я мовця окремо через
            // SceneStepView.SpeakerId/ActorId → "char.<id>" — вбудований префікс
            // дублював би його ("Тугар Вовк: Тугар Вовк: «…»").
            AddKey(t, "scene.opening.neighbour.title", "Сусід з претензією");
            AddKey(t, "scene.neighbour.offer",
                "«Пропусти їх через перевал. Візьмуть своє й підуть. Тобі — доля».");
            AddKey(t, "scene.neighbour.threat",
                "«Не пустиш по-доброму — пройдуть по-іншому. І спитають уже з громади».");
            AddKey(t, "scene.neighbour.elder_refuses",
                "«Перевал не мій і не твій. Він громадський. Схід вирішить».");
            AddKey(t, "sfx.door.slam", "грюкнули двері");

            AddKey(t, "scene.pass.title", "Після перевалу");
            AddKey(t, "scene.pass.best", "«Склад цілий. І всі повернулися».");
            AddKey(t, "scene.pass.good", "«Максим не встане пару днів. Але склад цілий».");
            AddKey(t, "scene.pass.base", "«Вона пішла за батьком. А склад вичистили до дощок».");
            AddKey(t, "scene.pass.worst", "«Максим поранений, її нема, складу нема. Громада дивиться й мовчить».");

            // Transition-ключі ScenePlayback.TransitionKey (SceneStepView) —
            // текст не показується напряму (переходи мовчазні), але §6.2
            // все одно зобов'язує мати рядок на КОЖЕН ключ, що вилітає з
            // View-шару: тримаємо коротку діагностичну підпис.
            AddKey(t, "to.node1.pass", "Перехід: до вузла 1 (перевал).");
            AddKey(t, "to.settlement.evening", "Перехід: до вечора в поселенні.");
        }

        // ==================================================================
        // Пакет E3b: "голі" ключі, якими GameSession.LogEvent НАСПРАВДІ пише
        // DayLog для команд/наслідків, яких §7 не називає (§7 або дає їм інший
        // формат — "*.ordered" через CityWorks-сигнал, — або не знає про них
        // узагалі). "*.ordered"-варіанти з §7.13 — це ОКРЕМІ, теж реальні
        // ключі (оголошення через CityWorks/SignalComposer, з затримкою на
        // добу); ключі нижче — миттєвий відгук самої команди GameSession.
        // ==================================================================
        private static void AddRealCommandFeedbackKeys(Dictionary<string, string> t)
        {
            AddKey(t, "combat.overwatch.triggered", "{attackerId} стріляє з дозору по {targetId}.");

            // Finale.DamTopicId/.DamTacticsTopicId (Core/Story/Finale.cs) — теми
            // тихого фіналу («загатити річку»), показуються як пороги перевірки
            // ще ДО резолву, тим самим шляхом, що dungeon.abandoned_camp.room1.
            AddKey(t, "finale.dam", "Загатити річку (Механіка).");
            AddKey(t, "finale.dam.tactics", "Тримати позицію на дамбі (Тактика).");
        }

        // ==================================================================
        // Пакет E3b: SignalComposer.Compose будує ключ інциденту як буквальний
        // конкатенат <c>inc.TopicId + "." + inc.Band</c> (Core/Signals/
        // SignalComposer.cs, ~рядок 107) — <c>OutcomeBand</c> ("Best"/"Good"/
        // "Base"/"Worst", З ВЕЛИКОЇ). §7.6/§7.8 і AddIncidentHeadlinesAndOutcomes
        // вище дають ЦЕЙ САМИЙ зміст під ключами "incident.<id>.outcome.<band>"
        // (нижній регістр, окреме слово "outcome") — тими користується UI, що
        // читає структурований DayReportView.Incidents; ключі нижче — тим, хто
        // читає СИРУ стрічку сигналів/DayLog (GameEvent.Key/SignalLineView.TopicId).
        // Текст навмисно ідентичний відповідним "outcome.<band>" вище.
        // ==================================================================
        private static void AddIncidentBandKeys(Dictionary<string, string> t)
        {
            AddIncidentBand(t, "petty_theft",
                "Крадія знайшли, і майже все повернулося на місце.",
                "Половину зниклого повернуто.",
                "Розібралися абияк — крадій пішов непокараний.",
                "Нічого не знайшли, і крадуть далі.");
            AddIncidentBand(t, "market_brawl",
                "Сторони розійшлися самі, без синців і без боргів.",
                "Бійку розвели, торг триває.",
                "Розвели силою — ринок ще довго гуде.",
                "Ринок стоїть — торгувати нікому, всі налякані.");
            AddIncidentBand(t, "spoiled_stores",
                "Дід Овсій знаходить майже все.",
                "Половину повернено.",
                "Повернено мало.",
                "Нічого не повернено, і по селу шепочуть.");
            AddIncidentBand(t, "sick_child",
                "Гафія впоралася без зайвого дня.",
                "Дитина одужає за кілька днів.",
                "Одужання йде важко.",
                "Лазарет порожній — нікому було лікувати.");
            AddIncidentBand(t, "protection_racket",
                "Побори скасовано — торговці дихнули вільно.",
                "Побори зменшено, поки що.",
                "Побори лишились — торговці платять і мовчать.",
                "Побори зросли. Торговці шукають іншого ринку.");
            AddIncidentBand(t, "missing_person",
                "Знайшли живим — і не одного: із ним прибилися ще люди.",
                "Знайшли живим.",
                "Знайшли, але пізно — рана вже своя.",
                "Не знайшли. Громада знає, що це означає.");
            AddIncidentBand(t, "night_burglary",
                "Злодія взяли на гарячому — украдене повернулося.",
                "Злодія прогнали, частину майна повернули.",
                "Злодій утік із половиною здобичі.",
                "Склад обчищено — і ніхто нічого не бачив.");
            AddIncidentBand(t, "night_arson",
                "Вогонь погашено, поки не побачив ніхто, крім вартового.",
                "Вогонь погашено, згоріло небагато.",
                "Погасили пізно — сарай не врятувати.",
                "Погасити не встигли. Громада бачила заграву.");
            AddIncidentBand(t, "crisis_riot",
                "Натовп розійшовся мирно — слово подіяло.",
                "Бунт стих, обійшлося без крові.",
                "Бунт придушили силою — рахунок за це прийде.",
                "Площа в крові. Це надовго запам'ятають.");

            // pass_vanguard: та сама розв'язка вузла 1, що й node1.outcome.* /
            // scene.pass.*, але у форматі, яким сигнальний шар РЕАЛЬНО емітить
            // "incident.pass_vanguard.<Band>" (SignalComposer, окремо від
            // "decision.resolved"/incidentId="pass_vanguard" і від сцени
            // розв'язки). Best/Good — короткий нейтральний виклад без роду
            // (сигнальна репліка — не пряма мова протагоніста).
            AddKey(t, "incident.pass_vanguard.Best", "Максим і Мирослава — обидва з тобою. Склад цілий.");
            AddKey(t, "incident.pass_vanguard.Good", "Максим ранений, лежить у лазареті. Мирослава з тобою. Склад цілий.");
            AddKey(t, "incident.pass_vanguard.Base",
                "Ти з Максимом стоїш. Мирослава йде за батьком — назад до орди. Склад розграбований.");
            AddKey(t, "incident.pass_vanguard.Worst",
                "Максим ранений. Мирослава йде. Склад розграбований, і громада тепер боїться голосно говорити.");
        }

        private static void AddIncidentBand(Dictionary<string, string> t, string incidentId,
            string best, string good, string @base, string worst)
        {
            AddKey(t, "incident." + incidentId + ".Best", best);
            AddKey(t, "incident." + incidentId + ".Good", good);
            AddKey(t, "incident." + incidentId + ".Base", @base);
            AddKey(t, "incident." + incidentId + ".Worst", worst);
        }

        // ==================================================================
        // Пакет E3b: доповіді з постів (SignalComposer, ~рядок 125) —
        // "post." + PostDomain.DomainTag + "." + OutcomeBand. DomainTag —
        // КИРИЛИЦЕЮ вже в Core (Session/FirstHourWorld.cs: "рада"/"склад"/
        // "лазарет" — усвідомлений відступ від латиниці §7 "конвенції ключів",
        // не помилка E3b: домен постів — єдине місце, де Core сам вписує
        // українське слово в ключ, бо це буквально назва посади). Лише ЦІ
        // три пости мають PostDomain на сьогодні (FirstHourWorld) — інші 4
        // (ринок/ферми/верстак/розвідпост) не доповідають узагалі (те саме
        // G13/G14 AUDIT-GAPS, зафіксовано в §7.5 TEST_BUILD.md як відомий
        // розрив вільної гри); "post.report.<domain>.good/.silence" §7.5 —
        // окрема, наразі недосяжна конвенція для тих чотирьох постів, лишена
        // в таблиці про запас (AddPostReports вище).
        // ==================================================================
        private static void AddPostReportBandKeys(Dictionary<string, string> t)
        {
            AddPostReportBand(t, "рада",
                "Захар Беркут: «Рада йде, як має йти — і трохи краще».",
                "Захар Беркут: «Рада йде, як має йти».",
                "Захар Беркут: «Рада йде — так собі, але йде».",
                "Захар Беркут: «Раді сьогодні не до ладу».");
            AddPostReportBand(t, "склад",
                "Дід Овсій: «Рахунок сходиться день у день, тютелька в тютельку».",
                "Дід Овсій: «Рахунок сходиться. Поки що».",
                "Дід Овсій: «Рахунок приблизний, але не бреше».",
                "Дід Овсій: «Щось у рахунку не сходиться, а що — не скажу».");
            AddPostReportBand(t, "лазарет",
                "Знахарка Гафія: «Усі на ногах, кому годиться».",
                "Знахарка Гафія: «Рани гояться, як має. Тримайте їх у теплі».",
                "Знахарка Гафія: «Рани гояться, тільки повільно».",
                "Знахарка Гафія: «Сьогодні не встигаю за всіма».");
        }

        private static void AddPostReportBand(Dictionary<string, string> t, string domainTagCyrillic,
            string best, string good, string @base, string worst)
        {
            AddKey(t, "post." + domainTagCyrillic + ".Best", best);
            AddKey(t, "post." + domainTagCyrillic + ".Good", good);
            AddKey(t, "post." + domainTagCyrillic + ".Base", @base);
            AddKey(t, "post." + domainTagCyrillic + ".Worst", worst);
        }

        // ==================================================================
        // Пакет E3b: амбієнт/переходи полос, ЯК ЇХ РЕАЛЬНО буквує
        // SignalComposer.Compose — "night.ambient."/"tension.ambient."/
        // "tension.band." + TensionBand.ToString() (Calm/Murmur/Ferment/Heat/
        // Fracture, З ВЕЛИКОЇ), не lowercase §7.4/§7.9-стиль з
        // AddNight/AddAmbientSignals вище. Та ж причина, що в incident-блоці:
        // нижній регістр лишається для того, хто читає вже структурований
        // домен (band-слово саме по собі), ключі нижче — для сирого
        // GameEvent.Key/SignalLineView.TopicId. Плюс DungeonThreatBand —
        // довідкові підписи, які код поки нікуди в GameEvent не пише (лише
        // DungeonView.ThreatBand — сирий рядок view-поля), але знадобляться
        // будь-якому UI, що покаже загрозу підземелля словом.
        // ==================================================================
        private static void AddAmbientAndThreatBandKeys(Dictionary<string, string> t)
        {
            AddKey(t, "tension.ambient.Calm", "«Добре, що ви тут.» Діти у дворах.");
            AddKey(t, "tension.ambient.Murmur", "У колодязя сперечаються про ціни.");
            AddKey(t, "tension.ambient.Ferment", "Розмова стихає, коли підходиш.");
            AddKey(t, "tension.ambient.Heat", "Ставні зачинені вдень. Патруль ходить парами.");
            AddKey(t, "tension.ambient.Fracture", "Площа порожня. Зброю носять відкрито.");

            AddKey(t, "night.ambient.Calm", "Тихо. Лише вітер.");
            AddKey(t, "night.ambient.Murmur", "Десь хлопнула ставня.");
            AddKey(t, "night.ambient.Ferment", "Кроки за рогом стихли, коли обернувся.");
            AddKey(t, "night.ambient.Heat", "Вогні в бочках. Голоси не місцеві.");
            AddKey(t, "night.ambient.Fracture", "Ані одного вогника у вікнах.");

            AddKey(t, "tension.band.Calm", "«Стихає. Нарешті».");
            AddKey(t, "tension.band.Murmur", "«З'явився ропіт — чути по дворах».");
            AddKey(t, "tension.band.Ferment", "«Бродить. Люди сходяться гуртками».");
            AddKey(t, "tension.band.Heat", "«Накалюється. Це вже не приховати».");
            AddKey(t, "tension.band.Fracture", "«Змінюється. І не в кращий бік».");

            AddKey(t, "dungeon.threat.calm", "Спокійно");
            AddKey(t, "dungeon.threat.tense", "Напружено");
            AddKey(t, "dungeon.threat.dangerous", "Небезпечно");
            AddKey(t, "dungeon.threat.deadly", "Смертельно");
        }

        // ==================================================================
        // Пакет E3b: tools/Alpha.Play/Program.cs — консольні підписи, яких не
        // несе жоден GameEvent/View (звертання до протагоніста, порожній
        // актор сцени). Дрібні, але теж "текст, що бачить гравець" (R7).
        // ==================================================================
        private static void AddConsoleLabels(Dictionary<string, string> t)
        {
            AddKey(t, "ui.console.you", "ти");
            AddKey(t, "ui.console.empty_actor", "порожньо");
        }

        // ==================================================================
        // Пакет E3b: Gameplay/VillageView.cs (сцена "Село живе сутками",
        // CLAUDE.md) — раніше своя вбудована таблиця словами замінена цими
        // ключами. Мудборд/лінт-ткач сцени — не §7 TEST_BUILD.md (той описує
        // GameSession/GameEvent), тому власні, окремі ключі; де вже є готовий
        // ключ ("city.built.&lt;id&gt;", "city.tier.&lt;n&gt;", "council.raid",
        // "tension.ambient.&lt;Band&gt;", "night.ambient.&lt;Band&gt;",
        // "tension.band.&lt;Band&gt;" — усі вище в AddAmbientAndThreatBandKeys/
        // AddCityAndCouncilEvents/AddCouncilActions) — VillageView читає його
        // напряму, без дублю.
        // ==================================================================
        private static void AddVillageViewKeys(Dictionary<string, string> t)
        {
            AddKey(t, "village.headline", "Доба {day} · {phase} · {mood}");
            AddKey(t, "village.headline.phase.day", "день");
            AddKey(t, "village.headline.phase.night", "ніч");

            AddKey(t, "village.mood.tier.0", "хутір");
            AddKey(t, "village.mood.tier.1", "село");
            AddKey(t, "village.mood.tier.2", "слобода");
            AddKey(t, "village.mood.tier.3", "містечко");
            // decay.0 навмисно відсутній у таблиці — AddKey забороняє порожній
            // текст (сторожа проти "забули дописати рядок"), а тут порожній
            // рядок і Є правильним значенням (без суфікса). VillageView.MoodWords
            // обробляє відсутність ключа для decayStep==0 як порожній суфікс.
            AddKey(t, "village.mood.decay.1", ", подекуди занедбаний");
            AddKey(t, "village.mood.decay.2", ", занепалий");
            AddKey(t, "village.mood.decay.3", ", забитий і засмічений");

            // Короткі дієслівні фрази для стрічки подій (не плутати з
            // одиничними словами "band.best/good/base/worst" §7.21 — ті йдуть
            // як підпис на бейдж, ці — у складене речення про інцидент).
            AddKey(t, "village.band.best", "розібралися якнайкраще");
            AddKey(t, "village.band.good", "впоралися чисто");
            AddKey(t, "village.band.base", "якось владнали");
            AddKey(t, "village.band.worst", "вийшло погано");

            AddKey(t, "village.incident.line", "{what} — {how}");
            AddKey(t, "village.incident.crisis_line", "КРИЗА: {what} — {how}");
            AddKey(t, "village.incident.suffix.unmanned", "; на посту нікого не було");
            AddKey(t, "village.incident.suffix.fear", "; громада налякана");
            AddKey(t, "village.incident.suffix.population_lost", "; люди йдуть");
            AddKey(t, "village.incident.suffix.people_arrived", "; з ними прийшли ще {count}");

            AddKey(t, "village.forewarn.level1", "Щось назріває");
            AddKey(t, "village.forewarn.level2", "Тривожно: розмови про {domain}");
            AddKey(t, "village.forewarn.level3", "Біда близько, і вона про {domain}");

            AddKey(t, "village.city.people.left.hunger", "Люди йдуть — голодно.");
            AddKey(t, "village.city.people.left.fear", "Люди йдуть — бояться.");
            AddKey(t, "village.city.people.left.other", "Люди йдуть — ніщо тут не тримає.");
            AddKey(t, "village.city.people.arrived.council", "Прийшли люди — покликала рада.");
            AddKey(t, "village.city.people.arrived.expedition", "Прийшли люди — привів загін.");
            AddKey(t, "village.city.people.arrived.other", "Прийшли люди самі.");
            AddKey(t, "village.city.count_suffix", " ({count})");

            AddKey(t, "village.city.crowd.up", "Людей на вулицях стало більше.");
            AddKey(t, "village.city.crowd.down", "Вулиці рідшають.");
        }

        // ==================================================================
        // Пакет E3b (фікс-ревью, мінорна знахідка №3): Gameplay/ScenePlayer.cs
        // — чотири жорстко зашиті російські рядки IMGUI-показу портретної
        // сцени, які раніше минали UkrainianText (R7).
        // ==================================================================
        private static void AddScenePlayerKeys(Dictionary<string, string> t)
        {
            AddKey(t, "scene.player.empty_framing", "— порожньо —");
            AddKey(t, "scene.player.finished", "Сцену завершено.");
            AddKey(t, "scene.player.no_portrait", "(портрета нема)");
            AddKey(t, "scene.player.hint", "пробіл або клік — далі   ·   портрети: Assets/Resources/Portraits/<id>.png");
        }

        // E1b — оболонка екранів. Свій блок наприкінці таблиці (як просить
        // §5 пакета E1b): нові ключі для GameShell/*Screen.cs, щоб мердж із
        // E2/E3 лишався тривіальним (жоден рядок вище не зачіпається). Імена
        // — lowercase dotted latin, як і решта таблиці.
        // ==================================================================

        // ---- Загальне: заголовок, вихід, панель подій, скасування ----
        private static void AddE1bShellCommon(Dictionary<string, string> t)
        {
            AddKey(t, "ui.common.back", "Назад");
            AddKey(t, "ui.common.cancel", "Скасувати");
            AddKey(t, "ui.common.confirm", "Підтвердити");
            AddKey(t, "ui.common.close", "Закрити");
            AddKey(t, "ui.common.none", "—");
            AddKey(t, "ui.common.empty", "Порожньо.");

            AddKey(t, "ui.feed.title", "Стрічка подій");
            AddKey(t, "ui.feed.empty", "Поки що тихо.");

            AddKey(t, "ui.topbar.day", "Доба {day}");
            AddKey(t, "ui.topbar.tier", "Тір {tier}");
            AddKey(t, "ui.topbar.crowd", "Люди: {band}");
            AddKey(t, "ui.topbar.mood", "Настрій: {band}");
            AddKey(t, "ui.topbar.freeplay", "Вільна гра");
            AddKey(t, "ui.topbar.patrolling", "На варті");

            AddKey(t, "ui.escape.title", "Пауза");
            AddKey(t, "ui.escape.resume", "Повернутися до гри");
            AddKey(t, "ui.escape.save", "Зберегти й вийти в меню");
            AddKey(t, "ui.escape.quit", "Вийти без збереження");
            AddKey(t, "ui.escape.hint", "Esc — відкрити/закрити це меню.");

            AddKey(t, "ui.creation.confirm", "Вирушати");
            AddKey(t, "background.warrior.label", "Вигнанець зі зброєю");
            AddKey(t, "background.trader.label", "Мандрівний торговець");
            AddKey(t, "background.healer.label", "Учень знахарки");
            AddKey(t, "ui.creation.gender.male", "Він");
            AddKey(t, "ui.creation.gender.female", "Вона");

            AddKey(t, "ui.scene.next", "Далі");
            AddKey(t, "ui.scene.hint", "Пробіл або клік — далі.");
            AddKey(t, "ui.scene.portrait.placeholder", "?");

            AddKey(t, "ui.start_day", "Почати день");
            AddKey(t, "ui.confirm_evening", "До ночі");
            AddKey(t, "ui.summary.continue", "Грати далі");
        }

        // ---- Хаб: назви вкладок і базові підписи вкладок ----
        private static void AddE1bHubTabs(Dictionary<string, string> t)
        {
            AddKey(t, "ui.tab.posts", "Пости");
            AddKey(t, "ui.tab.buildings", "Будівлі");
            AddKey(t, "ui.tab.council", "Рада");
            AddKey(t, "ui.tab.expedition", "Вилазка");
            AddKey(t, "ui.tab.gear", "Спорядження");
            AddKey(t, "ui.tab.people", "Люди");
            AddKey(t, "ui.tab.quests", "Квести");
            AddKey(t, "ui.tab.factions", "Фракції");
            AddKey(t, "ui.tab.readiness", "Готовність");
            AddKey(t, "ui.tab.save", "Збереження");

            AddKey(t, "ui.posts.assign", "Призначити");
            AddKey(t, "ui.posts.unassign", "Звільнити");
            AddKey(t, "ui.posts.empty_slot", "Пост порожній.");

            AddKey(t, "ui.buildings.order", "Замовити");
            AddKey(t, "ui.buildings.built", "Збудовано");
            AddKey(t, "ui.buildings.in_progress", "Стадія {stage} з 5");

            AddKey(t, "ui.council.raid", "Облава");
            AddKey(t, "ui.council.settlers", "Прийняти переселенців");
            AddKey(t, "ui.council.decree", "Указ");
            AddKey(t, "ui.council.diplomacy", "Посольство");
            AddKey(t, "ui.council.investment", "Вкласти в будівлю");
            AddKey(t, "ui.council.prepare_threat", "Готуватися до загрози");
            AddKey(t, "ui.council.outfit_expedition", "Спорядити відряд");

            AddKey(t, "ui.expedition.approach.quiet", "Тихо");
            AddKey(t, "ui.expedition.approach.forceful", "Силою");
            AddKey(t, "ui.expedition.approach.delve", "Спуститися");
            AddKey(t, "ui.expedition.preview", "Прев'ю");
            AddKey(t, "ui.expedition.depart", "Вирушати");
            AddKey(t, "ui.expedition.days", "Днів у полі: {days}");
            AddKey(t, "ui.expedition.threshold", "Поріг: {threshold}");
            AddKey(t, "ui.expedition.party_value", "Сила відряду: {value}");
            AddKey(t, "ui.expedition.expected", "Очікувана полоса: {band}");

            AddKey(t, "ui.quests.title", "Квести");
            AddKey(t, "ui.quests.none_active", "Пропозицій наразі немає.");
            AddKey(t, "ui.quests.stage", "Етап {stage}");

            AddKey(t, "ui.factions.title", "Фракції");

            AddKey(t, "ui.readiness.title", "Готовність громади");
            AddKey(t, "ui.readiness.milestones", "Віхи: {reached} з {total}");
        }

        // ---- Рішення / бій ----
        private static void AddE1bDecisionAndBattleUi(Dictionary<string, string> t)
        {
            AddKey(t, "ui.decision.title", "Рішення чекає");
            AddKey(t, "ui.decision.option_line",
                "{path}: {skill} ≥ {threshold} — {candidate}, очікувана полоса: {band}.");
            AddKey(t, "ui.decision.candidate", "візьметься {name}");
            AddKey(t, "ui.decision.no_candidate", "нікому взятися");
            AddKey(t, "ui.decision.path.quiet", "Тихо");
            AddKey(t, "ui.decision.path.bloody", "Криваво");
            AddKey(t, "decision.resolved", "Рішення ухвалено: {path} — {band}.");

            AddKey(t, "ui.battle.fallback.title", "Тактичний бій (спрощений вигляд)");
            AddKey(t, "ui.battle.move", "Рух");
            AddKey(t, "ui.battle.attack", "Атака");
            AddKey(t, "ui.battle.end_turn", "Завершити хід");
            AddKey(t, "ui.battle.round", "Раунд {round}");
            AddKey(t, "ui.battle.no_target", "Ціль не обрана.");
            AddKey(t, "combat.battle.started", "Бій почався.");
            AddKey(t, "combat.battle.resolved", "Бій завершено: {band}.");
        }

        // ---- Підземелля / вечір-ніч ----
        private static void AddE1bDungeonAndNightUi(Dictionary<string, string> t)
        {
            AddKey(t, "ui.dungeon.title", "Підземелля");
            AddKey(t, "ui.dungeon.depth", "Глибина");
            AddKey(t, "ui.dungeon.threat", "Загроза");
            AddKey(t, "ui.dungeon.push", "Глибше");
            AddKey(t, "ui.dungeon.extract", "Витягти здобич і вийти");
            AddKey(t, "ui.dungeon.abandon", "Відступити (усе незбережене втрачено)");
            AddKey(t, "ui.dungeon.quiet", "Тихо");
            AddKey(t, "ui.dungeon.bloody", "Криваво");
            AddKey(t, "ui.dungeon.unbanked", "Незбережено: {materials} матеріалів, {gold} золота");
            // Фаза F: короткий рядок кімнати данжу (тільки шлях/навик/поріг,
            // без candidate/band, яких у данжі немає — див. коментар у
            // DungeonScreen.DrawRoom) і окремий підпис кровавого шляху бойової
            // кімнати, де перевірки взагалі немає (завжди бій).
            AddKey(t, "ui.dungeon.option_line", "{path}: {skill} ≥ {threshold}.");
            AddKey(t, "ui.dungeon.bloody_fight", "Криваво: бій неминучий, перевірки немає.");
            AddKey(t, "dungeon.threat_band_changed", "Загроза підземелля тепер: {band}.");

            AddKey(t, "ui.night.crisis.title", "Вікно реакції на кризу");
            AddKey(t, "ui.night.crisis.spend_gold", "Витратити золото");
            AddKey(t, "ui.night.crisis.send_defender", "Відрядити людину з поста");
            AddKey(t, "ui.night.crisis.ignore", "Не реагувати");

            AddKey(t, "ui.night.finale.title", "Фінал доби 5");
            AddKey(t, "ui.advance_night", "До ранку");
            AddKey(t, "ui.night.evening.title", "Вечір");
            AddKey(t, "ui.night.night.title", "Ніч");
            AddKey(t, "ui.night.patrol.section", "Патруль чи сон");

            AddKey(t, "ui.quest.offer.title", "Пропозиція");
            AddKey(t, "quest.offered", "Нова пропозиція: {quest}.");

            AddKey(t, "expedition.departed", "Відряд вирушив: {site}.");
            AddKey(t, "expedition.returned", "Відряд повернувся: {site} — {band}.");
            AddKey(t, "scene.finished", "Сцена завершена.");
            AddKey(t, "production.resource", "Виробництво дало плоди.");
            AddKey(t, "production.recovered", "{companion} одужав(-ла) і повернувся(-лась) до справ.");
            AddKey(t, "production.food_shortage", "Їжі не вистачає — це вже видно.");
            AddKey(t, "item.scout_horn.forewarn_boosted", "Ріг розвідника чує далі: {charges} наступні передвісники чутніші.");
        }

        // ---- Люди / спорядження ----
        private static void AddE1bPeopleGearUi(Dictionary<string, string> t)
        {
            AddKey(t, "ui.people.title", "Люди");
            AddKey(t, "ui.people.level", "Рівень {level}");
            AddKey(t, "ui.people.scars", "Шрамів: {count}");
            // Фікс-ревью (Фаза F, знайдено тур-автоплеєм): HubScreen.DrawPeople
            // читає обидва ключі через Get(), не Format() (значення приходить
            // ОКРЕМИМ другим стовпцем LabeledRow — ScreenText.CompanionStatusLabel/
            // LoyaltyLabel), тож "{status}"/"{band}" у самому шаблоні ніколи не
            // підставлялись — гравець бачив рядок буквально "Стан: {status}".
            AddKey(t, "ui.people.loyalty", "Довіра:");
            AddKey(t, "ui.people.status", "Стан:");
            AddKey(t, "ui.people.equipped", "Спорядження: {items}");
            AddKey(t, "ui.people.equipped.none", "нічого");
            AddKey(t, "ui.people.sheet.limited",
                "Повний листок персонажа (атрибути/скіли/трейти) GameSession поки не віддає в жодному View — тут лише те, що видно назовні.");

            AddKey(t, "ui.buildplanner.invest", "+1");
            AddKey(t, "ui.buildplanner.commit", "Підтвердити незворотно");
            AddKey(t, "ui.buildplanner.reset", "Скинути план");
            AddKey(t, "ui.buildplanner.no_points", "Вільних очок немає.");

            AddKey(t, "ui.gear.slot.weapon", "Зброя");
            AddKey(t, "ui.gear.slot.armor", "Броня");
            AddKey(t, "ui.gear.slot.accessory", "Аксесуар");
            AddKey(t, "ui.gear.equip", "Одягти");
            AddKey(t, "ui.gear.unequip", "Зняти");
            AddKey(t, "ui.gear.stash.empty", "Схованка порожня.");
            AddKey(t, "ui.gear.stash.title", "Схованка");
            AddKey(t, "ui.gear.craft", "Покращити");
        }

        // ---- Текстовий фідбек результатів команд (AssignmentResult/BuildOrderResult/...) ----
        private static void AddE1bFeedback(Dictionary<string, string> t)
        {
            AddKey(t, "ui.reason.dead", "Загинув(-ла) — не годиться.");
            AddKey(t, "ui.reason.on_mission", "У полі — недоступний(-на).");
            AddKey(t, "ui.reason.antagonist", "Проти нас — недоступний(-на).");
            AddKey(t, "ui.reason.unknown_companion", "Такого напарника немає.");
            AddKey(t, "ui.reason.empty_party", "Оберіть хоч когось у відряд.");
            AddKey(t, "ui.reason.duplicate_companion", "Один і той самий двічі в списку.");

            AddKey(t, "ui.feedback.assign.success", "Призначено.");
            AddKey(t, "ui.feedback.assign.slot_not_found", "Такого поста немає.");
            AddKey(t, "ui.feedback.assign.slot_locked", "Пост ще закритий.");
            AddKey(t, "ui.feedback.assign.slot_occupied", "Пост уже зайнятий.");
            AddKey(t, "ui.feedback.assign.companion_not_found", "Такого напарника немає.");
            AddKey(t, "ui.feedback.assign.companion_unavailable", "Зараз не може стати на пост.");

            AddKey(t, "ui.feedback.build.started", "Замовлено.");
            AddKey(t, "ui.feedback.build.unknown_building", "Такої будівлі не існує.");
            AddKey(t, "ui.feedback.build.already_built", "Уже збудовано.");
            AddKey(t, "ui.feedback.build.already_in_progress", "Уже будується.");
            AddKey(t, "ui.feedback.build.quest_only", "Ця будівля відкривається лише сюжетом.");
            AddKey(t, "ui.feedback.build.not_enough_gold", "Золота не досить.");
            AddKey(t, "ui.feedback.build.not_enough_materials", "Матеріалів не досить.");

            AddKey(t, "ui.feedback.dispatch.success", "Відряд вирушив.");
            AddKey(t, "ui.feedback.dispatch.no_such_site", "Такої точки немає.");
            AddKey(t, "ui.feedback.dispatch.empty_party", "Відряд порожній.");
            AddKey(t, "ui.feedback.dispatch.party_too_large", "Забагато людей для одного відряду.");
            AddKey(t, "ui.feedback.dispatch.unknown_companion", "Такого напарника немає.");
            AddKey(t, "ui.feedback.dispatch.companion_unavailable", "Хтось у списку зараз недоступний.");
            AddKey(t, "ui.feedback.dispatch.duplicate_companion", "Один і той самий двічі в списку.");
            AddKey(t, "ui.feedback.dispatch.party_already_away", "Відряд ще не повернувся з попередньої вилазки.");

            AddKey(t, "ui.feedback.craft.success", "Покращено.");
            AddKey(t, "ui.feedback.craft.invalid_item", "Такого предмета немає в схованці.");
            AddKey(t, "ui.feedback.craft.named_not_upgradable", "Іменний предмет уже досконалий — далі нікуди.");
            AddKey(t, "ui.feedback.craft.already_max_rarity", "Вища якість уже неможлива.");
            AddKey(t, "ui.feedback.craft.workshop_closed", "Майстерня ще не збудована.");
            AddKey(t, "ui.feedback.craft.cannot_afford", "Не вистачає матеріалів або золота.");

            AddKey(t, "ui.feedback.buildplan.ok", "Готово до підтвердження.");
            AddKey(t, "ui.feedback.buildplan.not_enough_points", "Вільних очок не досить.");
            AddKey(t, "ui.feedback.buildplan.above_skill_ceiling", "Вище стелі скіла.");
            AddKey(t, "ui.feedback.buildplan.perk_unavailable", "Перк ще недоступний.");
            AddKey(t, "ui.feedback.buildplan.not_confirmed", "Не підтверджено.");
        }

        // ---- Буквальні ключі подій GameSession, яких немає в §7.13/§7.16 (звірено з реальним LogEvent) ----
        private static void AddE1bMissingEventKeys(Dictionary<string, string> t)
        {
            AddKey(t, "council.decree", "Рада видає указ.");
            AddKey(t, "council.diplomacy", "Посольство вирушає.");
            AddKey(t, "council.invest", "Рада вкладає в будівлю.");
            AddKey(t, "council.prepare_threat", "Громада готується до загрози.");
            AddKey(t, "council.outfit_expedition", "Відряд споряджають краще, ніж завжди.");
        }

        // ---- Короткі слова-чіпи для верхньої панелі: настрій (TensionBand — уже
        // й так лише слово, R17/інваріант 3, тут коротший синонім за прозові
        // tension.ambient.*) і натовп (CrowdBand словом, а не реченням §7.5). ----
        private static void AddE1bMoodChips(Dictionary<string, string> t)
        {
            AddKey(t, "ui.mood.calm", "Спокій");
            AddKey(t, "ui.mood.murmur", "Ропіт");
            AddKey(t, "ui.mood.ferment", "Бродіння");
            AddKey(t, "ui.mood.heat", "Накал");
            AddKey(t, "ui.mood.fracture", "Розкол");

            // SessionView.CrowdBand — рядок GameSession.CrowdBandName(int), буквально
            // "Hamlet"/"Village"/"Settlement"/"Town"/"City" (той самий тір поселення,
            // що й city.tier.*, лише англійським словом-ідентифікатором) — ключ тут
            // за цим словом у нижньому регістрі, не за числом.
            AddKey(t, "ui.crowd.hamlet", "Хутір");
            AddKey(t, "ui.crowd.village", "Село");
            AddKey(t, "ui.crowd.settlement", "Слобода");
            AddKey(t, "ui.crowd.town", "Містечко");
            AddKey(t, "ui.crowd.city", "Город");

            AddKey(t, "ui.threat.calm", "Спокійно");
            AddKey(t, "ui.threat.tense", "Напружено");
            AddKey(t, "ui.threat.dangerous", "Небезпечно");
            AddKey(t, "ui.threat.deadly", "Смертельно");
        }

        /// <summary>
        /// Фікс-ревью пакета E1b: сім ключів подій GameSession.LogEvent, які
        /// вилітали в стрічку без тексту (перевірено grep'ом по реальних
        /// LogEvent-викликах, не лише по трасі бот-прогону), плюс правильна
        /// назва оверватч-події (стара "combat.overwatch.triggered.line" у
        /// §7.20 лишається — ScreenText.EventLine шукає точну назву
        /// GameSession.LogEvent, не її), плюс полоси результату бою для
        /// BattleScreen.cs (view.Outcome раніше йшов сирим "Ongoing"/...).
        /// </summary>
        private static void AddE1bReviewFixKeys(Dictionary<string, string> t)
        {
            AddKey(t, "city.building.ordered", "Замовлено будівництво: {building}.");
            AddKey(t, "equip.changed", "{companion}: спорядження змінено ({item}).");
            AddKey(t, "progression.build_committed", "{companion}: розподіл очок розвитку підтверджено незворотно.");
            AddKey(t, "combat.training.started", "Тренувальний бій розпочато.");
            AddKey(t, "signal.domain", "Звістка з дороги: відряд вирушив ({domain}).");
            AddKey(t, "companion.left_settlement", "{companion} залишає поселення.");

            // Фікс-ревью (major): ExpeditionSite.DomainTag (DefaultSites.cs) —
            // сирі латинські "road"/"craft"/"trade", які EventLine раніше
            // підставляв у signal.domain напряму (Arg(a,"domain") без
            // ContentLabel) — англійське слово посеред українського речення
            // на кожній вилазці. Тепер ContentLabel("domain", ...) шукає ці
            // ключі, як і item/building/site/faction/scar поруч.
            AddKey(t, "domain.road", "дорога");
            AddKey(t, "domain.craft", "ремесло");
            AddKey(t, "domain.trade", "торгівля");

            // Точна назва події з GameSession.cs:1257 (LogNewAttacks) — без
            // плейсхолдерів attacker/target: ScreenText.EventLine їх не знає
            // (передає лише фіксований набір іменованих аргументів), той самий
            // прийом, що вже в сусідніх combat.attack.*.
            AddKey(t, "combat.overwatch.triggered.m", "Вистрілив із дозору.");
            AddKey(t, "combat.overwatch.triggered.f", "Вистрілила з дозору.");

            AddKey(t, "ui.battle.outcome.ongoing", "Триває");
            AddKey(t, "ui.battle.outcome.victory", "Перемога");
            AddKey(t, "ui.battle.outcome.defeat", "Поразка");

            AddKey(t, "ui.battle.overwatch.aim_hint", "Оберіть клітинку — напрямок дозору.");
            AddKey(t, "ui.battle.abilities.label", "Уміння:");

            AddKey(t, "ui.title.header", "Alpha — перевал");
            AddKey(t, "ui.title.skip_creation", "Пропустити створення персонажа");

            AddKey(t, "ui.state.day", "день");
            AddKey(t, "ui.state.decision", "рішення");
            AddKey(t, "ui.state.dungeon", "підземелля");
            AddKey(t, "ui.state.evening", "вечір");
            AddKey(t, "ui.state.night", "ніч");
            AddKey(t, "ui.state.battle", "бій");
            AddKey(t, "ui.state.scene", "сцена");
            AddKey(t, "ui.state.opening", "вступ");
            AddKey(t, "ui.state.title", "титул");
            AddKey(t, "ui.state.creation", "створення персонажа");
            AddKey(t, "ui.state.summary", "підсумок");
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

            // Fix-ревью (major): HP ніде не показувався — ні для поточного
            // юніта, ні для наведеної цілі під прев'ю шансу.
            AddKey(t, "ui.battle.hp", "Здоров'я: {current}/{max}");
            AddKey(t, "ui.battle.hp.target", "Ціль — здоров'я: {current}/{max}");

            // Fix-ревью (minor): три з чотирьох здібностей не лишають сліду в
            // бойовому логу (Core не пише Attacks для Lunge/SetTrap/Reposition) —
            // загальне підтвердження на будь-яке успішне застосування.
            AddKey(t, "ui.battle.ability.used", "{ability} застосовано.");

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
