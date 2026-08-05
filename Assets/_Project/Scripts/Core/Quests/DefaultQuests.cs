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
                    .React("leader", -4))
                // Особая опция от трейта (US-2.6): дурная слава Громилы решает без боя и слов.
                .Option(new QuestOption("Молча выйти вперёд — репутация Громилы скажет всё", next: 2)
                    .GateTrait("bruiser")
                    .With(new SocialConsequence().Faction(DefaultFactions.FreeFolk, -4).Reputation(2).Tension(-1))
                    .React("brawler", +3, "Боец хрустит кулаками: «Вот так и договорились.»")));

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

            // 2 — обезврежено: в схроне за растяжкой — именная винтовка (US-6.1).
            q.Stage(QuestStage.OutcomeStage("defused", "Растяжка обезврежена. В схроне за ней — чужая винтовка.",
                success: true,
                new QuestReward(xp: 70, gold: 60).Item(Items.DefaultItems.Widowmaker())));

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
                .Stage(QuestStage.OutcomeStage("cowed", "Вымогатели отступили. Торговцы суют «подарок».", success: true,
                    new QuestReward(xp: 40).Item(Items.DefaultItems.Whisper())
                        .WithSocial(new SocialConsequence().Faction(DefaultFactions.Traders, 6).Reputation(3).Tension(-4))))
                .Stage(QuestStage.OutcomeStage("escalated", "Слово не подействовало — стало только злее.", success: false,
                    new QuestReward().WithSocial(new SocialConsequence().Tension(4))));
        }

        public static List<QuestDefinition> All() => new List<QuestDefinition>
        {
            LostCaravan(), MinedPass(), StreetShakedown()
        };

        // ===== Пролог (US-17.1: первый час, микс-развилка) =====
        public const string PrologueDoneFlag = "prologue_done";
        public const string PrologueSpurnedFlag = "prologue_spurned";

        /// <summary>
        /// Пролог на окраине (US-17.1): учит бой, а сюжетный выбор при отходе задаёт
        /// стартовые отношения. «Бросить своих» — жёсткий путь: переговорщик может
        /// уйти к злодею и вернуться боссом акта 1 (сид — Story.Prologue). Ставки
        /// телеграфированы; провал боя усугубляет, но пролог не запирает кампанию.
        /// </summary>
        public static QuestDefinition Prologue()
        {
            var q = new QuestDefinition("prologue", "Окраина", QuestSource.NpcLocation)
                .BlockFlag(PrologueDoneFlag)
                .Flavor("Засада на окраине. Этот бой — всерьёз: здесь теряют людей.");

            // 0 — пролог-бой (ведёт вызывающий код; поражение не рвёт пролог — отход).
            q.Stage(QuestStage.CombatStage("ambush", "Отбиться от засады на окраине.", "outskirts_ambush",
                onWin: 1, onLoss: 1));

            // 1 — микс-развилка: отход под огнём, прикрыть можно ОДНОГО. Опции про
            // конкретного бойца закрыты его смертью в бою этапа; «уходить» доступна
            // ВСЕГДА (иначе гибель обоих — софтлок этапа).
            q.Stage(QuestStage.ChoiceStage("retreat", "Отход под огнём. Кого прикрываешь?")
                .Option(new QuestOption("Прикрыть переговорщика", next: 2)
                    .GateAlive("negotiator")
                    .React("negotiator", +8, "Переговорщик выдыхает: «Я этого не забуду.»")
                    .React("marksman", -4, "Стрелок мрачно перезаряжается."))
                .Option(new QuestOption("Прикрыть стрелка", next: 2)
                    .GateAlive("marksman")
                    .React("marksman", +8, "Стрелок коротко кивает: «Сочтёмся.»")
                    .React("negotiator", -4, "Переговорщик отстал и молчит всю дорогу."))
                .Option(new QuestOption("Уходить, не оглядываясь", next: 3)
                    .With(new SocialConsequence().Tension(3).Reputation(-2))
                    .React("negotiator", -30, "Переговорщик смотрит вслед: «Вот, значит, как…»")
                    .React("marksman", -10)));

            // 2 — ушли вместе.
            q.Stage(QuestStage.OutcomeStage("escaped", "Оторвались. Отряд цел — и это уже победа.",
                success: true,
                new QuestReward(xp: 50).WithSocial(new SocialConsequence().Flag(PrologueDoneFlag))));

            // 3 — бросили своих: пролог пройден, но с меткой (возможен сид босса акта 1).
            q.Stage(QuestStage.OutcomeStage("spurned", "Ушли. За спиной — те, кого не прикрыли.",
                success: false,
                new QuestReward(xp: 30)
                    .WithSocial(new SocialConsequence().Flag(PrologueDoneFlag).Flag(PrologueSpurnedFlag))));

            return q;
        }

        // ===== Сюжетный спайн (US-14.2): линейная цепочка флагов → веха финала =====
        public const string SpineAct1Flag = "spine_act1_done";
        /// <summary>Веха финала — совпадает с FinalBattle.ReadyFlag (Core.Story).</summary>
        public const string FinaleReadyFlag = "finale_ready";

        /// <summary>
        /// Акт 1 «Тень на горизонте»: разведка подтверждает — снаружи собирается
        /// нашествие. Спайн линеен и НЕ запирается провалом (US-16.2): любой исход
        /// двигает флаг, провал лишь дороже (скрытая Напруга).
        /// </summary>
        public static QuestDefinition SpineAct1Shadow()
        {
            var q = new QuestDefinition("spine_act1", "Тень на горизонте", QuestSource.NpcSettlement)
                .BlockFlag(SpineAct1Flag) // пройденный акт не предлагается заново (после загрузки)
                .Flavor("Дозорный с вышки: «На востоке столбы дыма. Это не гроза, командир.»");

            q.Stage(QuestStage.SkillCheck("scout", "Разведать источники дыма на востоке.",
                        SkillType.Survival, threshold: 2, onSuccess: 1, onFailure: 2)
                    .AsUtility()
                    .FailCost(new SocialConsequence().Tension(3)));

            q.Stage(QuestStage.OutcomeStage("confirmed",
                "Разведка вернулась с картами лагерей — и трофейным клинком. Времени мало, но оно есть.",
                success: true,
                new QuestReward(xp: 60, gold: 40).Item(Items.DefaultItems.Sting())
                    .WithSocial(new SocialConsequence().Reputation(4).Flag(SpineAct1Flag))));

            // Провал — тоже вперёд (спайн не встаёт колом): узнали меньше, шума больше.
            q.Stage(QuestStage.OutcomeStage("rumors", "Разведчики вернулись ни с чем — только слухи и тревога.",
                success: false,
                new QuestReward(xp: 30)
                    .WithSocial(new SocialConsequence().Tension(3).Flag(SpineAct1Flag))));

            return q;
        }

        /// <summary>
        /// Акт 2 «Сбор бури»: выбор стратегии + пробный бой; ЛЮБОЙ исход ставит веху
        /// финала (нашествие придёт независимо — авторский исход, US-13.1/14.2).
        /// </summary>
        public static QuestDefinition SpineAct2Storm()
        {
            var q = new QuestDefinition("spine_act2", "Сбор бури", QuestSource.CouncilBoard)
                .GateFlag(SpineAct1Flag)
                .BlockFlag(FinaleReadyFlag) // веха стоит — акт пройден
                .Flavor("Совет собран: лагеря снаружи сливаются в орду. Как встретим?");

            q.Stage(QuestStage.ChoiceStage("strategy", "Орда близко. Что делаем до штурма?")
                .Option(new QuestOption("Ударить по передовому лагерю первыми", next: 1)
                    .With(new SocialConsequence().Faction(DefaultFactions.Garrison, 6).Tension(3))
                    .React("brawler", +4, "Боец кивает: «Лучше мы к ним, чем они к нам.»")
                    .React("negotiator", -3, "Переговорщик хмурится: «Кровь до штурма…»"))
                .Option(new QuestOption("Укрепляться и предупредить окраины", next: 2)
                    .With(new SocialConsequence().Faction(DefaultFactions.Commune, 6).Faction(DefaultFactions.FreeFolk, 4).Reputation(3))
                    .React("negotiator", +4, "Переговорщик выдыхает: «Спасаем своих. Верно.»")));

            // 1 — вылазка на передовой лагерь (бой ведёт вызывающий код).
            q.Stage(QuestStage.CombatStage("raid_camp", "Ночной удар по передовому лагерю орды.", "horde_vanguard",
                onWin: 3, onLoss: 4));

            // 2 — оборонительный путь: веха сразу (штурм придёт сам).
            q.Stage(QuestStage.OutcomeStage("dig_in", "Стены подняты, окраины предупреждены. Теперь — ждать бурю.",
                success: true,
                new QuestReward(xp: 70)
                    .WithSocial(new SocialConsequence().Reputation(3).Flag(FinaleReadyFlag))));

            // 3 — удар удался: орда придёт потрёпанной; из пожарища — именной трофей.
            q.Stage(QuestStage.OutcomeStage("vanguard_broken", "Передовой лагерь разбит. Орда придёт злее — но реже.",
                success: true,
                new QuestReward(xp: 90, gold: 60).Materials(4, 2)
                    .Item(Game.Core.Items.DefaultItems.Ember())
                    .WithSocial(new SocialConsequence().Faction(DefaultFactions.Garrison, 4).Flag(FinaleReadyFlag))));

            // 4 — удар захлебнулся: буря всё равно придёт (мягкий фейл-стейт, US-16.2).
            q.Stage(QuestStage.OutcomeStage("raid_failed", "Отряд отброшен. Орда идёт как шла — к стенам.",
                success: false,
                new QuestReward(xp: 40)
                    .WithSocial(new SocialConsequence().Tension(5).Flag(FinaleReadyFlag))));

            return q;
        }

        /// <summary>Спайн целиком (порядок = порядок актов; финал открывает флаг вехи).</summary>
        public static List<QuestDefinition> StorySpine() => new List<QuestDefinition>
        {
            SpineAct1Shadow(), SpineAct2Storm()
        };

        /// <summary>
        /// ВЕСЬ авторский пул (пролог + спайн + сайды) — источник для доски города
        /// и восстановления журнала из сейва (сейв хранит только id квестов).
        /// </summary>
        public static List<QuestDefinition> FullPool()
        {
            var pool = new List<QuestDefinition> { Prologue() };
            pool.AddRange(StorySpine());
            pool.AddRange(All());
            return pool;
        }

        /// <summary>Сюжетный бит акта 1 с цепочкой дублёров (US-14.1): протагонист → надёжные.</summary>
        public static StoryBeat Act1Briefing() =>
            new StoryBeat("act1_briefing", "leader")
                .Understudy("negotiator")
                .Understudy("marksman")
                .Critical();
    }
}
