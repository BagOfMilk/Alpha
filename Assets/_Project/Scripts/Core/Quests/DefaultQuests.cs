using System.Collections.Generic;
using Game.Core.Checks;
using Game.Core.Factions;
using Game.Core.Stats;

namespace Game.Core.Quests
{
    /// <summary>
    /// Сид-квесты прототипа (Эпики 13–14, ПЛЕЙСХОЛДЕР-флавор). Демонстрируют все
    /// этапы: утилитарная проверка с мягким провалом, выбор с рябью по фракциям/
    /// Напряжению/лояльности, бой, телеграфированная ОБХОДИМАЯ леталка (US-13.2),
    /// награды (XP/золото/именной предмет). Именной предмет берётся из DefaultItems.
    /// </summary>
    public static class DefaultQuests
    {
        /// <summary>
        /// «Пропавший караван» (NPC поселения): выследить → разрулить (договор/сила/
        /// уйти). Ветки бьют по фракциям и лояльности; силовой путь даёт бой и
        /// именную награду.
        /// </summary>
        public static QuestDefinition LostCaravan()
        {
            var q = new QuestDefinition("lost_caravan", "Пропавший караван", QuestSource.NpcSettlement)
                .Flavor("Торговец у ворот: «Караван не вернулся с восточного тракта. Найдите его — заплачу.»");

            // 0 — утилитарная проверка: выследить. Провал не тупик — теряем время (Напряжение+).
            q.Stage(QuestStage.SkillCheck("track", "Выследить караван по следам на тракте.",
                        SkillType.Survival, threshold: 3, onSuccess: 1, onFailure: 1)
                    .AsUtility()
                    .FailCost(new SocialConsequence().Tension(2)));

            // 1 — выбор: караван держат отступники из Вольных.
            q.Stage(QuestStage.ChoiceStage("standoff", "Караван держат отступники-Вольные. Что делаешь?")
                .Option(new QuestOption("Убедить отпустить", next: 2)
                    .GateSkill(SkillType.Persuasion, 3)
                    .With(new SocialConsequence().Faction(DefaultFactions.FreeFolk, 8).Reputation(5).Tension(-3).Flag("caravan_saved"))
                    .React("negotiator", +6, "Переговорщик одобряет: «Без крови — это по-нашему.»")
                    .React("leader", +3))
                .Option(new QuestOption("Отбить силой", next: 3)
                    .With(new SocialConsequence().Faction(DefaultFactions.Garrison, 6).Faction(DefaultFactions.FreeFolk, -8).Tension(4))
                    .React("negotiator", -5, "Переговорщик мрачнеет: «Опять кровь…»")
                    .React("brawler", +4, "Боец ухмыляется: «Давно пора.»"))
                .Option(new QuestOption("Бросить караван", next: 6)
                    .With(new SocialConsequence().Reputation(-4).Tension(2))
                    .React("leader", -4)));

            // 2 — мирный исход.
            q.Stage(QuestStage.OutcomeStage("peaceful", "Караван отпущен миром. Торговец благодарен.", success: true,
                new QuestReward(xp: 80, gold: 120).Materials(4, 2)
                    .WithSocial(new SocialConsequence().Reputation(6).Influence(1).Flag("caravan_saved"))));

            // 3 — бой с отступниками (ведёт вызывающий через CombatState).
            q.Stage(QuestStage.CombatStage("skirmish", "Короткая стычка с отступниками.", "raiders", onWin: 4, onLoss: 5));

            // 4 — силовой успех + именная награда.
            q.Stage(QuestStage.OutcomeStage("recovered", "Караван отбит. В обозе — старая броня.", success: true,
                new QuestReward(xp: 90, gold: 90).Materials(6, 3)
                    .Item(Game.Core.Items.DefaultItems.AegisPlate())
                    .WithSocial(new SocialConsequence().Faction(DefaultFactions.Garrison, 4).Flag("caravan_saved"))));

            // 5 — бой проигран (мягкий фейл-стейт: главный путь не закрыт, US-16.2).
            q.Stage(QuestStage.OutcomeStage("routed", "Отряд отступил. Караван потерян.", success: false,
                new QuestReward().WithSocial(new SocialConsequence().Reputation(-3).Tension(3))));

            // 6 — караван брошен.
            q.Stage(QuestStage.OutcomeStage("abandoned", "Караван брошен на произвол.", success: false,
                new QuestReward().WithSocial(new SocialConsequence().Reputation(-2))));

            return q;
        }

        /// <summary>
        /// «Минный проход» (NPC локации): телеграфированная ЛЕТАЛЬНАЯ проверка, но
        /// ОБХОДИМАЯ (US-13.2) — можно обойти длинным путём без риска.
        /// </summary>
        public static QuestDefinition MinedPass()
        {
            var q = new QuestDefinition("mined_pass", "Минный проход", QuestSource.NpcLocation)
                .Flavor("Разведчик шепчет: «Проход заминирован. Дальше — растяжки.»");

            // 0 — выбор: риск или обход (леталка всегда обходима).
            q.Stage(QuestStage.ChoiceStage("approach", "Проход заминирован. Как пройти?")
                .Option(new QuestOption("Обезвредить растяжку (смертельно при провале)", next: 1))
                .Option(new QuestOption("Обойти длинным путём (дольше)", next: 3)
                    .With(new SocialConsequence().Tension(2))));

            // 1 — ЛЕТАЛЬНАЯ телеграфированная проверка.
            q.Stage(QuestStage.SkillCheck("defuse", "Обезвредить растяжку. Провал — взрыв.",
                        SkillType.Mechanics, threshold: 4, onSuccess: 2, onFailure: 4)
                    .AsLethal());

            // 2 — обезврежено.
            q.Stage(QuestStage.OutcomeStage("defused", "Растяжка обезврежена. Проход чист.", success: true,
                new QuestReward(xp: 70, gold: 60)));

            // 3 — обход.
            q.Stage(QuestStage.OutcomeStage("detour", "Обошли длинным путём — дольше, но целы.", success: true,
                new QuestReward(xp: 50, gold: 30)));

            // 4 — взрыв (телеграф был — наказание заслуженно).
            q.Stage(QuestStage.OutcomeStage("blast", "Взрыв. Кто-то не вернётся из этого прохода.", success: false,
                new QuestReward().WithSocial(new SocialConsequence().Tension(5))));

            return q;
        }

        /// <summary>Квест от инцидента «Напряжения» (US-14.3): подслушал на улице → дело.</summary>
        public static QuestDefinition StreetShakedown()
        {
            return new QuestDefinition("street_shakedown", "Уличный рэкет", QuestSource.TensionIncident)
                .Flavor("На рынке вымогают долю. Подслушал у лотков — можно вмешаться.")
                .Stage(QuestStage.SocialCheck("confront", "Осадить вымогателей словом.",
                            CheckApproach.Intimidate, threshold: 3, onSuccess: 1, onFailure: 2))
                .Stage(QuestStage.OutcomeStage("cowed", "Вымогатели отступили. На рынке выдохнули.", success: true,
                    new QuestReward(xp: 40).WithSocial(new SocialConsequence().Faction(DefaultFactions.Traders, 6).Reputation(3).Tension(-4))))
                .Stage(QuestStage.OutcomeStage("escalated", "Слово не подействовало — стало только злее.", success: false,
                    new QuestReward().WithSocial(new SocialConsequence().Tension(4))));
        }

        public static List<QuestDefinition> All() => new List<QuestDefinition>
        {
            LostCaravan(), MinedPass(), StreetShakedown()
        };

        /// <summary>Сюжетный бит акта 1 с цепочкой дублёров (US-14.1): протагонист → надёжные.</summary>
        public static StoryBeat Act1Briefing() =>
            new StoryBeat("act1_briefing", "leader")
                .Understudy("negotiator")
                .Understudy("marksman")
                .Critical();
    }
}
