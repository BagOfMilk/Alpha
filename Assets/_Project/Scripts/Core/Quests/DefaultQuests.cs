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

        public static List<QuestDefinition> All(BalanceConfig cfg) => new List<QuestDefinition> { Hafiya(cfg) };

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
