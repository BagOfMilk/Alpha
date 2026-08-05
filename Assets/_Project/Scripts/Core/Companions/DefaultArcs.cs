using Game.Core.Characters;
using Game.Core.Factions;
using Game.Core.Quests;
using Game.Core.Stats;

namespace Game.Core.Companions
{
    /// <summary>
    /// Сид-арки напарников (US-9.5, ПЛЕЙСХОЛДЕР-флавор). Главы — обычные
    /// QuestDefinition (играются через QuestRun), гейтятся полосой лояльности и
    /// прогрессом: вторая глава открывается флагом первой. Смерть/предательство
    /// арку обрывают (CompanionArcRun.Refresh).
    /// </summary>
    public static class DefaultArcs
    {
        /// <summary>Арка медика «Старый долг»: спасти товарища (Steady) → расплата (Devoted).</summary>
        public static CompanionArc MedicOldDebt()
        {
            return new CompanionArc("arc_medic", "medic", "Старый долг")
                .Chapter(new ArcChapter("ch1", Chapter1())
                    .Loyalty(LoyaltyBand.Steady).SetsFlag("arc_medic_ch1"))
                .Chapter(new ArcChapter("ch2", Chapter2())
                    .Loyalty(LoyaltyBand.Devoted).NeedsFlag("arc_medic_ch1").SetsFlag("arc_medic_done"));
        }

        private static QuestDefinition Chapter1() =>
            new QuestDefinition("arc_medic_q1", "Письмо из прошлого", QuestSource.NpcSettlement)
                .Flavor("Медику весть: старый товарищ при смерти. Успеть бы.")
                // Порог 6 — на очко ВЫШЕ стартового медика (Медицина 3 + Спокойные
                // руки 2 = 5): глава арки есть проверка того, вкладывались ли в него.
                // При пороге ≤5 ветка «Опоздали» была недостижима — арка гейтится
                // живым медиком, значит его значение в ростере всегда присутствует.
                .Stage(QuestStage.SkillCheck("treat", "Вытащить товарища с того света.", SkillType.Medicine, 6, 1, 2))
                .Stage(QuestStage.OutcomeStage("saved", "Товарищ выкарабкался.", true, new QuestReward(40, 30)))
                .Stage(QuestStage.OutcomeStage("lost", "Опоздали. Это останется с медиком.", false));

        private static QuestDefinition Chapter2() =>
            new QuestDefinition("arc_medic_q2", "Расплата", QuestSource.NpcLocation)
                .Flavor("Тот, кто подставил товарища, найден. Слово за тобой.")
                .Stage(QuestStage.ChoiceStage("verdict", "Как поступить с виновником?")
                    .Option(new QuestOption("Простить", 1)
                        .With(new SocialConsequence().Faction(DefaultFactions.Commune, 5).Reputation(3)))
                    .Option(new QuestOption("Наказать", 1)
                        .With(new SocialConsequence().Faction(DefaultFactions.Garrison, 5).Tension(3))))
                .Stage(QuestStage.OutcomeStage("closure", "Старый долг закрыт.", true, new QuestReward(60, 50)));

        /// <summary>Арка бойца «Кулаки и совесть»: старый ринг зовёт (Steady) → цена славы (Devoted).</summary>
        public static CompanionArc BrawlerFistsAndConscience()
        {
            return new CompanionArc("arc_brawler", "brawler", "Кулаки и совесть")
                .Chapter(new ArcChapter("ch1", BrawlerChapter1())
                    .Loyalty(LoyaltyBand.Steady).SetsFlag("arc_brawler_ch1"))
                .Chapter(new ArcChapter("ch2", BrawlerChapter2())
                    .Loyalty(LoyaltyBand.Devoted).NeedsFlag("arc_brawler_ch1").SetsFlag("arc_brawler_done"));
        }

        private static QuestDefinition BrawlerChapter1() =>
            new QuestDefinition("arc_brawler_q1", "Старый ринг", QuestSource.NpcSettlement)
                .Flavor("Бойца зовут «тряхнуть стариной» на подпольном ринге. Долги не забываются.")
                // Порог 10 — на очко ВЫШЕ стартового бойца (Запугивание 1 + Громила 2
                // + Сила 6 = 9), см. арку медика: ветка «Пришлось выйти на ринг»
                // должна быть достижима, а арка гейтится живым Бойцом.
                .Stage(QuestStage.SocialCheck("refuse", "Отговорить устроителей — без крови.",
                    Game.Core.Checks.CheckApproach.Intimidate, threshold: 10, onSuccess: 1, onFailure: 2))
                .Stage(QuestStage.OutcomeStage("walked_away", "Ринг остался в прошлом. Боец молчит, но благодарен.", true,
                    new QuestReward(xp: 40, gold: 20)))
                .Stage(QuestStage.OutcomeStage("dragged_in", "Пришлось выйти на ринг. Победа — но осадок тяжёлый.", false,
                    new QuestReward(xp: 30).WithSocial(new SocialConsequence().Tension(2))));

        private static QuestDefinition BrawlerChapter2() =>
            new QuestDefinition("arc_brawler_q2", "Цена славы", QuestSource.NpcLocation)
                .Flavor("Старый соперник нашёл Бойца. Он не драться пришёл — просить.")
                .Stage(QuestStage.ChoiceStage("plea", "Соперник просит вступиться за его семью. Что скажет Боец?")
                    .Option(new QuestOption("Вступиться", 1)
                        .With(new SocialConsequence().Faction(DefaultFactions.Commune, 5).Reputation(3)))
                    .Option(new QuestOption("Отказать: у своих забот хватает", 1)
                        .With(new SocialConsequence().Reputation(-2).Tension(2))))
                .Stage(QuestStage.OutcomeStage("respect", "Как бы ни решилось — старый счёт закрыт.", true,
                    new QuestReward(xp: 60, gold: 40)));
    }
}
