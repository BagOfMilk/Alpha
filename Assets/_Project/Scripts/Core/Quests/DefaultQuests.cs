using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Checks;

namespace Game.Core.Quests
{
    /// <summary>
    /// Квест Гафії «Гірка розрада» (TEST_BUILD.md §3.2–3.5, пакет B6): 3 сюжетні
    /// біти — пропозиція (доба 2, ніч), похід по гірку траву (перевірка,
    /// впливає на інцидент <c>sick_child</c> доби 3) і подяка (доба 5).
    ///
    /// Механічно це 5 вузлів рушія (пропозиція-вибір, перевірка, два термінали
    /// «подяки» за полосою і термінал відмови) — «3 сюжетні біти» не рахують
    /// відмову і не рахують два дзеркальні термінали подяки за окремі етапи:
    /// вони те саме, що дві полоси однієї перевірки, як скрізь у грі
    /// (Поправка №3.7).
    /// </summary>
    public static class DefaultQuests
    {
        public const string HafiyaId = "hafiya";

        /// <summary>
        /// Прапор: трава знайдена (полоса Good/Best етапу «grass»). D1 читає
        /// його, щоб застосувати <see cref="QuestBalance.HafiyaGrassBonusToSickChild"/>
        /// до порогу інциденту <c>sick_child</c> (OpeningContent.cs, чужий файл —
        /// B6 лише експонує прапор і число, застосування день у день не наше).
        /// </summary>
        public const string HafiyaGrassFoundFlag = "hafiya_grass_found";

        // ---- текстові ключі (§7.7 TEST_BUILD.md) ----
        public const string OfferKey = "quest.hafiya.offer";
        public const string OfferAcceptKey = "quest.hafiya.offer.option.accept";
        public const string OfferDeclineKey = "quest.hafiya.offer.option.decline";
        public const string DeclinedKey = "quest.hafiya.declined";
        public const string Stage2FoundKey = "quest.hafiya.stage2.found";
        public const string Stage2MissingKey = "quest.hafiya.stage2.missing";
        public const string Stage3BestKey = "quest.hafiya.stage3.best";
        public const string Stage3WorstKey = "quest.hafiya.stage3.worst";

        /// <summary>
        /// Індекс термінала «трава знайдена» — day-3 реплику (<see cref="Stage2FoundKey"/>)
        /// D1 показує ОДРАЗУ по <c>ResolveCheck</c> (за <c>report.Band</c>), а
        /// <see cref="Stage3BestKey"/>/<see cref="Stage3WorstKey"/> на терміналі —
        /// день-5 подяку за вже готовим результатом (той самий вузол, дві репліки
        /// різних діб — квест по суті один, розтягнутий у часі постановкою).
        /// </summary>
        public static QuestDefinition Hafiya(BalanceConfig cfg)
        {
            var q = cfg.Quest;

            var def = new QuestDefinition(HafiyaId, OfferKey);

            // 0 — пропозиція: узятися чи ні.
            def.Stage(QuestStage.ChoiceStage("offer", OfferKey)
                .Option(new QuestOption(OfferAcceptKey, next: 1))
                .Option(new QuestOption(OfferDeclineKey, next: 4)));

            // 1 — похід по траву на дальні схили. Порог і навик — Виживання:
            // тема утилітарна, не соціальна (Neutral), як і в OpeningContent.
            def.Stage(QuestStage.Check(
                id: "grass",
                textKey: null, // немає окремого «прев'ю» тексту — етап іде відразу за прийняттям пропозиції
                skill: SkillKeys.Survival,
                threshold: q.HafiyaGrassThreshold,
                approach: ApproachForm.Neutral,
                nextByBand: new[] { 3, 3, 2, 2 }, // Worst,Base -> missing(3); Good,Best -> found(2)
                consequenceByBand: new[]
                {
                    // Захищений доступ (QuestBalance.Pick), а не пряма індексація:
                    // TensionByBand — правлений власником Balance SO масив (R14), і
                    // короткий контентний масив деградує до fallback=0, а не валить
                    // побудову квесту винятком (фікс-рев'ю B6).
                    new QuestConsequence().Tension(QuestBalance.Pick(q.TensionByBand, 0, 0)),
                    new QuestConsequence().Tension(QuestBalance.Pick(q.TensionByBand, 1, 0)),
                    new QuestConsequence().Tension(QuestBalance.Pick(q.TensionByBand, 2, 0)).Flag(HafiyaGrassFoundFlag),
                    new QuestConsequence().Tension(QuestBalance.Pick(q.TensionByBand, 3, 0)).Flag(HafiyaGrassFoundFlag)
                }));

            // 2 — трава знайдена: подяка при всіх (найкраща розв'язка лінії).
            def.Stage(QuestStage.OutcomeStage("thanks_best", Stage3BestKey, success: true,
                new QuestConsequence().WithXp(30)));

            // 3 — трави нема: дитина одужує повільніше, Гафія цього не забуде.
            def.Stage(QuestStage.OutcomeStage("thanks_worst", Stage3WorstKey, success: false,
                new QuestConsequence().WithXp(10)));

            // 4 — відмова від пропозиції: квест закритий без походу.
            def.Stage(QuestStage.OutcomeStage("declined", DeclinedKey, success: false));

            return def;
        }

        public static List<QuestDefinition> All(BalanceConfig cfg) => new List<QuestDefinition> { Hafiya(cfg), StoneSoup(cfg), MarketToll(cfg) };

        // ---------------------------------------------------------------
        // «Кам'яна юшка» (docs/narrative/QUEST_CANDIDATES.md:270, ATU 1548,
        // PD з 1720) — Trade-квест, Робочий пакет 3 фіксу глибини механік
        // (25.09.2026). Мандрівник без ресурсу береться варити юшку з
        // порожнього казана і по одному дрібному внеску домовляється з
        // містом, поки спільний результат не перевищить суму частин —
        // тихий шлях за задумом джерела. Кривавий аналог тут наш, не
        // з оригіналу (Поправка №1 вимагає, щоб він був завжди): силова
        // реквізиція запасів замість перемовин.
        // ---------------------------------------------------------------

        public const string StoneSoupId = "stone_soup";

        public const string StoneSoupOfferKey = "quest.stone_soup.offer";
        public const string StoneSoupJoinKey = "quest.stone_soup.offer.option.join";
        public const string StoneSoupSeizeKey = "quest.stone_soup.offer.option.seize";
        public const string StoneSoupIgnoreKey = "quest.stone_soup.offer.option.ignore";
        public const string StoneSoupGenerousKey = "quest.stone_soup.trade.generous";
        public const string StoneSoupModestKey = "quest.stone_soup.trade.modest";
        public const string StoneSoupFailedKey = "quest.stone_soup.trade.failed";
        public const string StoneSoupSeizeDoneKey = "quest.stone_soup.seize.done";
        public const string StoneSoupSeizeResistedKey = "quest.stone_soup.seize.resisted";
        public const string StoneSoupDeclinedKey = "quest.stone_soup.declined";

        public static QuestDefinition StoneSoup(BalanceConfig cfg)
        {
            var def = new QuestDefinition(StoneSoupId, StoneSoupOfferKey);

            // 0 — пропозиція: домовлятися (тихо), забрати силою (криваво) чи пройти повз.
            // Індекси next: рахуй за порядком def.Stage() нижче — 0:offer,
            // 1:trade, 2:seize, 3:trade_generous, 4:trade_modest,
            // 5:trade_failed, 6:seize_done, 7:seize_resisted, 8:declined.
            def.Stage(QuestStage.ChoiceStage("offer", StoneSoupOfferKey)
                .Option(new QuestOption(StoneSoupJoinKey, next: 1))
                .Option(new QuestOption(StoneSoupSeizeKey, next: 2))
                .Option(new QuestOption(StoneSoupIgnoreKey, next: 8)));

            // 1 — тихий шлях: перемовини від дому до дому (Trade). Чотири
            // полоси ведуть у ТРИ різних термінали (не дві, як у Гафії) —
            // урок з аудиту тонкості квестів: не зливати всі полоси в одну пару.
            def.Stage(QuestStage.Check(
                id: "trade", textKey: null,
                skill: SkillKeys.Trade, threshold: 5, approach: ApproachForm.Trade,
                nextByBand: new[] { 5, 4, 4, 3 }, // Worst->failed(5); Base,Good->modest(4); Best->generous(3)
                consequenceByBand: new[]
                {
                    new QuestConsequence().Tension(5).Faction("community", -3),
                    new QuestConsequence().Faction("community", 3),
                    new QuestConsequence().Faction("community", 6),
                    new QuestConsequence().Faction("community", 10)
                }));

            // 2 — кривавий шлях: силова реквізиція запасів (Intimidate).
            // Швидко, але страх і ставлення громади падають незалежно від
            // результату — Поправка №1: насильство купує швидкість ціною ризику.
            def.Stage(QuestStage.Check(
                id: "seize", textKey: null,
                skill: SkillKeys.Intimidate, threshold: 5, approach: ApproachForm.Intimidate,
                nextByBand: new[] { 7, 7, 6, 6 }, // Worst,Base->resisted(7); Good,Best->done(6)
                consequenceByBand: new[]
                {
                    new QuestConsequence().Tension(12).Faction("community", -12),
                    new QuestConsequence().Tension(8).Faction("community", -8),
                    new QuestConsequence().Tension(15).Faction("community", -12),
                    new QuestConsequence().Tension(18).Faction("community", -15)
                }));

            def.Stage(QuestStage.OutcomeStage("trade_generous", StoneSoupGenerousKey, success: true,
                new QuestConsequence().WithXp(25)));
            def.Stage(QuestStage.OutcomeStage("trade_modest", StoneSoupModestKey, success: true,
                new QuestConsequence().WithXp(12)));
            def.Stage(QuestStage.OutcomeStage("trade_failed", StoneSoupFailedKey, success: false,
                new QuestConsequence().WithXp(5)));
            def.Stage(QuestStage.OutcomeStage("seize_done", StoneSoupSeizeDoneKey, success: true,
                new QuestConsequence().WithXp(15)));
            def.Stage(QuestStage.OutcomeStage("seize_resisted", StoneSoupSeizeResistedKey, success: false,
                new QuestConsequence().WithXp(0)));
            def.Stage(QuestStage.OutcomeStage("declined", StoneSoupDeclinedKey, success: false));

            return def;
        }

        // ---------------------------------------------------------------
        // «Ринковий побір» (docs/narrative/QUEST_CANDIDATES.md:265,
        // «Рінконете і Кортадільйо», Сервантес 1613, PD) — рекет як
        // переговори зі ступінчастою довірою, адаптований під наявну
        // фракцію Бояр Тугара (нової фракції-гільдії злодіїв НЕ заводимо —
        // поза обсягом Робочого пакета 3). Дрібна крадіжка на ринку веде не
        // до покарання, а до «побору» від імені бояр — можна виторгувати
        // менший побір (Persuade) чи прогнати збирача силою (Intimidate,
        // кривавий шлях). Навмисно перевіряє щойно полагоджений консьюмер
        // щабля довіри (Робочий пакет 1): кривавий шлях здатний опустити
        // Бояр до Hostile, після чого проста Дипломатія з ними в раді
        // перестає діяти, поки довіру не піднято іншим шляхом.
        // ---------------------------------------------------------------

        public const string MarketTollId = "market_toll";

        public const string MarketTollOfferKey = "quest.market_toll.offer";
        public const string MarketTollNegotiateKey = "quest.market_toll.offer.option.negotiate";
        public const string MarketTollConfrontKey = "quest.market_toll.offer.option.confront";
        public const string MarketTollIgnoreKey = "quest.market_toll.offer.option.ignore";
        public const string MarketTollNegotiateBestKey = "quest.market_toll.negotiate.best";
        public const string MarketTollNegotiateGoodKey = "quest.market_toll.negotiate.good";
        public const string MarketTollNegotiateBaseKey = "quest.market_toll.negotiate.base";
        public const string MarketTollNegotiateWorstKey = "quest.market_toll.negotiate.worst";
        public const string MarketTollConfrontWonKey = "quest.market_toll.confront.won";
        public const string MarketTollConfrontFailedKey = "quest.market_toll.confront.failed";
        public const string MarketTollDeclinedKey = "quest.market_toll.declined";

        public static QuestDefinition MarketToll(BalanceConfig cfg)
        {
            var def = new QuestDefinition(MarketTollId, MarketTollOfferKey);

            // Індекси next: 0:offer, 1:negotiate, 2:confront, 3:negotiate_best,
            // 4:negotiate_good, 5:negotiate_base, 6:negotiate_worst,
            // 7:confront_won, 8:confront_failed, 9:declined.
            def.Stage(QuestStage.ChoiceStage("offer", MarketTollOfferKey)
                .Option(new QuestOption(MarketTollNegotiateKey, next: 1))
                .Option(new QuestOption(MarketTollConfrontKey, next: 2))
                .Option(new QuestOption(MarketTollIgnoreKey, next: 9)));

            // 1 — тихий шлях: виторгувати менший побір (Persuade). Усі
            // чотири полоси ведуть у РІЗНІ термінали — навмисно, щоб не
            // повторити тонкість MaksymCh1 (де перевірка на 4 полоси
            // сходиться в один і той самий вихід). CheckResolver.BandFor
            // для Persuade сам знижує Best до Good (підхід «підіймає підлогу,
            // опускає стелю») — індекс 3 (Best) лишається недосяжним, той
            // самий контракт, що вже в MaksymCh1.justice_check.
            def.Stage(QuestStage.Check(
                id: "negotiate", textKey: null,
                skill: SkillKeys.Persuade, threshold: 5, approach: ApproachForm.Persuade,
                nextByBand: new[] { 6, 5, 4, 3 }, // Worst->6; Base->5; Good->4; Best->3 (недосяжний для Persuade)
                consequenceByBand: new[]
                {
                    new QuestConsequence().Tension(5).Faction("tuhar_boyars", -10),
                    new QuestConsequence().Faction("tuhar_boyars", -2),
                    new QuestConsequence().Faction("tuhar_boyars", 5),
                    new QuestConsequence().Faction("tuhar_boyars", 10)
                }));

            // 2 — кривавий шлях: прогнати збирача силою (Intimidate).
            def.Stage(QuestStage.Check(
                id: "confront", textKey: null,
                skill: SkillKeys.Intimidate, threshold: 5, approach: ApproachForm.Intimidate,
                nextByBand: new[] { 8, 8, 7, 7 }, // Worst,Base->failed(8); Good,Best->won(7)
                consequenceByBand: new[]
                {
                    new QuestConsequence().Tension(15).Faction("tuhar_boyars", -20),
                    new QuestConsequence().Tension(10).Faction("tuhar_boyars", -15),
                    new QuestConsequence().Tension(12).Faction("tuhar_boyars", -15),
                    new QuestConsequence().Tension(10).Faction("tuhar_boyars", -10)
                }));

            def.Stage(QuestStage.OutcomeStage("negotiate_best", MarketTollNegotiateBestKey, success: true,
                new QuestConsequence().WithXp(25)));
            def.Stage(QuestStage.OutcomeStage("negotiate_good", MarketTollNegotiateGoodKey, success: true,
                new QuestConsequence().WithXp(18)));
            def.Stage(QuestStage.OutcomeStage("negotiate_base", MarketTollNegotiateBaseKey, success: true,
                new QuestConsequence().WithXp(10)));
            def.Stage(QuestStage.OutcomeStage("negotiate_worst", MarketTollNegotiateWorstKey, success: false,
                new QuestConsequence().WithXp(5)));
            def.Stage(QuestStage.OutcomeStage("confront_won", MarketTollConfrontWonKey, success: true,
                new QuestConsequence().WithXp(20)));
            def.Stage(QuestStage.OutcomeStage("confront_failed", MarketTollConfrontFailedKey, success: false,
                new QuestConsequence().WithXp(0)));
            def.Stage(QuestStage.OutcomeStage("declined", MarketTollDeclinedKey, success: false));

            return def;
        }

        // ---------------------------------------------------------------
        // Квест Максима «Не за кров» (Поправка №7.8, глава 1 арки Максима):
        // ЗАВМИСНО поза DefaultQuests.All() — це зміст квестової глави арки
        // (CompanionArcContent.IsQuestChapter), і реєструється в пулі лише
        // GameSession.BeginArcChapterQuest, коли главу справді відкрито
        // (гейт лояльності Steady). Загальний OfferQuestStage не міг би
        // запустити цей квест напряму, обійшовши гейт арки, — саме тому він
        // не в загальному пулі з самого початку прогону.
        // ---------------------------------------------------------------

        public const string MaksymId = "maksym";
        public const string MaksymCh1Id = "quest.maksym.ch1";

        public const string MaksymCh1OfferKey = "quest.maksym.ch1.offer";
        public const string MaksymCh1RevengeKey = "quest.maksym.ch1.offer.option.revenge";
        public const string MaksymCh1JusticeKey = "quest.maksym.ch1.offer.option.justice";
        public const string MaksymCh1RevengeDoneKey = "quest.maksym.ch1.revenge_done";
        public const string MaksymCh1JusticeDoneKey = "quest.maksym.ch1.justice_done";

        /// <summary>
        /// «Не за кров»: гонитель авангарду вбив когось із Максимових — вибір
        /// між помстою (Intimidate — залякати/покарати самотужки) і громадським
        /// судом (Persuade — довести до ради). Обидва шляхи — перевірка з 4
        /// полосами (Поправка №3.7), наслідок — лояльність Максима, ставлення
        /// громади (Faction) і Напруга через єдиний мостик
        /// <c>TensionDriver.QuestChoice</c> (R6, інваріант 5).
        /// </summary>
        public static QuestDefinition MaksymCh1(BalanceConfig cfg)
        {
            var def = new QuestDefinition(MaksymCh1Id, MaksymCh1OfferKey);

            def.Stage(QuestStage.ChoiceStage("path", MaksymCh1OfferKey)
                .Option(new QuestOption(MaksymCh1RevengeKey, next: 1))
                .Option(new QuestOption(MaksymCh1JusticeKey, next: 2)));

            def.Stage(QuestStage.Check(
                id: "revenge_check", textKey: null,
                skill: SkillKeys.Intimidate, threshold: 5, approach: ApproachForm.Intimidate,
                nextByBand: new[] { 3, 3, 3, 3 },
                consequenceByBand: new[]
                {
                    new QuestConsequence().Loyalty(MaksymId, -15).Tension(20).Faction("community", -10),
                    new QuestConsequence().Loyalty(MaksymId, -10).Tension(10),
                    new QuestConsequence().Loyalty(MaksymId, -5).Tension(5),
                    new QuestConsequence().Loyalty(MaksymId, 0).Tension(0).Flag("maksym_ch1_revenge_clean")
                }));

            def.Stage(QuestStage.Check(
                id: "justice_check", textKey: null,
                skill: SkillKeys.Persuade, threshold: 5, approach: ApproachForm.Persuade,
                nextByBand: new[] { 4, 4, 4, 4 },
                consequenceByBand: new[]
                {
                    new QuestConsequence().Loyalty(MaksymId, 5).Faction("community", -5),
                    new QuestConsequence().Loyalty(MaksymId, 10),
                    new QuestConsequence().Loyalty(MaksymId, 15).Faction("community", 5),
                    new QuestConsequence().Loyalty(MaksymId, 20).Faction("community", 10)
                }));

            // Прапор гейтингу глави 2 ("arc_maksym_ch1") ставить сам
            // CompanionArcRun.CompleteChapter() у СВІЙ контейнер (_arcFlags,
            // GameSession) — НЕ StoryFlags; тож термінал квесту його не
            // дублює (два різні контейнери, дублювання лише заплутало б).
            def.Stage(QuestStage.OutcomeStage("revenge_done", MaksymCh1RevengeDoneKey, success: true,
                new QuestConsequence().WithXp(20)));
            def.Stage(QuestStage.OutcomeStage("justice_done", MaksymCh1JusticeDoneKey, success: true,
                new QuestConsequence().WithXp(25)));

            return def;
        }
    }
}
