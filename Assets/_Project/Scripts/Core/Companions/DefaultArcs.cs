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
                .Stage(QuestStage.SkillCheck("treat", "Вытащить товарища с того света.", SkillType.Medicine, 3, 1, 2))
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
    }
}
