using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Checks;
using Game.Core.Combat;
using Game.Core.Economy;
using Game.Core.Factions;
using Game.Core.Items;
using Game.Core.Quests;
using Game.Core.Stats;
using Game.Core.Threats;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Квесты и сюжетные миссии (Эпики 13–14): ветвление проверок, честность
    /// (телеграф леталок, утилита soft-fail → альтернатива), последствия выбора
    /// (SocialConsequence + лояльность), награды, источники, дублёры спайна.
    /// </summary>
    public class QuestTests
    {
        private sealed class World
        {
            public BaseState Base;
            public Roster Roster;
            public BalanceConfig Cfg;
            public FactionRegistry Factions;
            public ThreatSystem Threats;
            public HashSet<string> Flags;
        }

        private static World NewWorld()
        {
            var cfg = new BalanceConfig();
            var roster = new Roster();
            return new World
            {
                Cfg = cfg,
                Roster = roster,
                Base = new BaseState(roster, new ResourceLedger(), cfg),
                Factions = DefaultFactions.NewRegistry(),
                Threats = new ThreatSystem(cfg, new ScriptedRng(), new List<IncidentDefinition>()),
                Flags = new HashSet<string>()
            };
        }

        private static Companion Comp(Roster roster, string id, SkillType skill = SkillType.None, int level = 0,
                                      int strength = 3)
        {
            var c = new Companion(id, new AttributeBlock(strength, 3, 3, 3), 4);
            if (skill != SkillType.None && level > 0) c.Skills.Set(skill, level);
            roster.Add(c);
            return c;
        }

        private static QuestRun Run(World w, QuestDefinition def)
            => new QuestRun(def, w.Base, w.Cfg, w.Factions, w.Threats, w.Flags);

        // ---- Проверки и ветвление ----
        [Test]
        public void Check_Success_BranchesAndRewards()
        {
            var w = NewWorld();
            Comp(w.Roster, "a", SkillType.Survival, 5);
            var def = new QuestDefinition("q", "Q", QuestSource.NpcSettlement)
                .Stage(QuestStage.SkillCheck("c", "", SkillType.Survival, 3, onSuccess: 1, onFailure: 2))
                .Stage(QuestStage.OutcomeStage("ok", "", true, new QuestReward(xp: 50, gold: 10)))
                .Stage(QuestStage.OutcomeStage("bad", "", false));

            var run = Run(w, def);
            var rep = run.ResolveCheck(w.Roster.All);

            Assert.IsTrue(rep.CheckSuccess);
            Assert.AreEqual("a", rep.ResolvedById);
            Assert.AreEqual(QuestState.Succeeded, run.State);
            Assert.AreEqual(10, w.Base.Resources.Get(ResourceType.Gold));
            Assert.AreEqual(50, w.Roster.Get("a").Xp, "квест даёт XP без гринда боёв (US-5.1)");
        }

        [Test]
        public void Check_UtilitySoftFail_RoutesToAlternative_NotDeadEnd()
        {
            var w = NewWorld();
            Comp(w.Roster, "a", SkillType.Survival, 1); // ниже порога
            var def = new QuestDefinition("q", "Q", QuestSource.NpcLocation)
                .Stage(QuestStage.SkillCheck("c", "", SkillType.Survival, 3, onSuccess: 1, onFailure: 2)
                    .AsUtility().FailCost(new SocialConsequence().Tension(3)))
                .Stage(QuestStage.OutcomeStage("fast", "", true, new QuestReward(40)))
                .Stage(QuestStage.OutcomeStage("slow", "", true, new QuestReward(20))); // альтернатива, тоже успех

            var run = Run(w, def);
            var rep = run.ResolveCheck(w.Roster.All);

            Assert.IsFalse(rep.CheckSuccess);
            Assert.IsTrue(rep.WasUtility);
            Assert.AreEqual(QuestState.Succeeded, run.State, "утилитарный провал — не тупик, ведёт на альтернативу");
            Assert.AreEqual(3, w.Threats.Tension.Value, 0.001, "мягкий сетбэк применён");
        }

        [Test]
        public void Check_Lethal_IsTelegraphedBeforeResolve()
        {
            var w = NewWorld();
            Comp(w.Roster, "a", SkillType.Mechanics, 1);
            var def = new QuestDefinition("q", "Q", QuestSource.NpcLocation)
                .Stage(QuestStage.SkillCheck("defuse", "", SkillType.Mechanics, 4, 1, 2).AsLethal())
                .Stage(QuestStage.OutcomeStage("ok", "", true))
                .Stage(QuestStage.OutcomeStage("boom", "", false));

            var run = Run(w, def);
            Assert.IsTrue(run.Current.Lethal, "летальность видна ЗАРАНЕЕ (US-13.2)");
            Assert.AreEqual(4, run.Current.Threshold, "порог показан заранее");
        }

        [Test]
        public void SocialCheck_UsesContextualAttribute()
        {
            var w = NewWorld();
            var c = Comp(w.Roster, "a", SkillType.Intimidation, 1, strength: 5); // Запугать → +Сила
            var def = new QuestDefinition("q", "Q", QuestSource.RandomEvent)
                .Stage(QuestStage.SocialCheck("scare", "", CheckApproach.Intimidate, threshold: 5, 1, 2))
                .Stage(QuestStage.OutcomeStage("ok", "", true))
                .Stage(QuestStage.OutcomeStage("no", "", false));

            var rep = Run(w, def).ResolveCheck(w.Roster.All);
            Assert.IsTrue(rep.CheckSuccess, "Запугивание 1 + Сила 5 = 6 ≥ 5");
        }

        // ---- Выборы и последствия ----
        [Test]
        public void Choice_AppliesSocialConsequence_AndLoyaltyReactions()
        {
            var w = NewWorld();
            var n = Comp(w.Roster, "n");
            var def = new QuestDefinition("q", "Q", QuestSource.NpcSettlement)
                .Stage(QuestStage.ChoiceStage("pick", "")
                    .Option(new QuestOption("жёстко", next: 1)
                        .With(new SocialConsequence().Faction(DefaultFactions.Garrison, 10).Reputation(5).Influence(2).Tension(4).Flag("hardline"))
                        .React("n", -6)))
                .Stage(QuestStage.OutcomeStage("done", "", true));

            var run = Run(w, def);
            var rep = run.Choose(0, w.Roster.All);

            Assert.IsTrue(rep.Accepted);
            Assert.AreEqual(10, w.Factions.Get(DefaultFactions.Garrison).Value, 0.001);
            Assert.AreEqual(5, w.Factions.Reputation, 0.001);
            Assert.AreEqual(2, w.Factions.Influence);
            Assert.AreEqual(4, w.Threats.Tension.Value, 0.001, "гражданская рябь — скрыта (US-10.3), но применяется");
            Assert.IsTrue(w.Flags.Contains("hardline"));
            Assert.AreEqual(44, n.Loyalty, "видимая рябь лояльности: 50 − 6");
            Assert.AreEqual(1, rep.ReactionLines.Count);
        }

        [Test]
        public void Choice_GatedOption_Unavailable_DoesNotAdvance()
        {
            var w = NewWorld();
            Comp(w.Roster, "a", SkillType.Persuasion, 2); // ниже гейта
            var def = new QuestDefinition("q", "Q", QuestSource.NpcSettlement)
                .Stage(QuestStage.ChoiceStage("pick", "")
                    .Option(new QuestOption("убедить", next: 1).GateSkill(SkillType.Persuasion, 4))
                    .Option(new QuestOption("по фракции", next: 1).GateFaction(DefaultFactions.Garrison, FactionBand.Warm)))
                .Stage(QuestStage.OutcomeStage("done", "", true));

            var run = Run(w, def);
            Assert.IsFalse(run.OptionAvailable(def.StageAt(0).Options[0], w.Roster.All), "скил ниже гейта");
            Assert.IsFalse(run.OptionAvailable(def.StageAt(0).Options[1], w.Roster.All), "фракция ниже полосы");

            var rep = run.Choose(0, w.Roster.All);
            Assert.IsFalse(rep.Accepted);
            Assert.AreEqual(0, run.CurrentIndex, "недоступный вариант не двигает квест");
            Assert.AreEqual(QuestState.Active, run.State);
        }

        // ---- Награды ----
        [Test]
        public void Reward_NamedItem_BankedToBaseInventory()
        {
            var w = NewWorld();
            Comp(w.Roster, "a", SkillType.Survival, 5);
            var def = new QuestDefinition("q", "Q", QuestSource.NpcLocation)
                .Stage(QuestStage.SkillCheck("c", "", SkillType.Survival, 3, 1, 1))
                .Stage(QuestStage.OutcomeStage("ok", "", true,
                    new QuestReward(10).Item(DefaultItems.Widowmaker())));

            Run(w, def).ResolveCheck(w.Roster.All);

            Assert.AreEqual(1, w.Base.Inventory.Count);
            Assert.IsTrue(w.Base.Inventory.Items[0].Definition.IsNamed);
            Assert.AreEqual(StatusType.Bleeding, w.Base.Inventory.Items[0].Weapon.StatusOnHit);
        }

        // ---- Журнал и источники ----
        [Test]
        public void QuestLog_GatesByFlag()
        {
            var w = NewWorld();
            var def = new QuestDefinition("q", "Q", QuestSource.CouncilBoard).GateFlag("unlock_me");
            var log = new QuestLog();

            log.CollectFrom(new[] { def }, w.Factions, w.Flags, w.Roster.All);
            Assert.AreEqual(0, log.Available.Count, "флага нет — квест скрыт");

            w.Flags.Add("unlock_me");
            log.CollectFrom(new[] { def }, w.Factions, w.Flags, w.Roster.All);
            Assert.AreEqual(1, log.Available.Count);
            Assert.IsTrue(log.Start("q"));
            Assert.AreEqual(QuestStatus.Active, log.StatusOf("q"));
        }

        [Test]
        public void QuestLog_SpawnFromIncident_BecomesAvailable()
        {
            var log = new QuestLog();
            var def = DefaultQuests.StreetShakedown();
            log.SpawnFromIncident(def);
            Assert.AreEqual(QuestStatus.Available, log.StatusOf(def.Id));
            Assert.AreEqual(QuestSource.TensionIncident, def.Source);
        }

        // ---- Дублёры спайна (US-14.1) ----
        [Test]
        public void DelivererChain_PreferredThenUnderstudyThenNarrator()
        {
            var roster = new Roster();
            var leader = Comp(roster, "leader");
            var under = Comp(roster, "under");
            var beat = new StoryBeat("b", "leader").Understudy("under");

            Assert.AreEqual("leader", DelivererChain.Resolve(beat, roster).DelivererId);

            leader.Kill();
            Assert.AreEqual("under", DelivererChain.Resolve(beat, roster).DelivererId);

            under.Kill();
            Assert.IsTrue(DelivererChain.Resolve(beat, roster).IsNarratorFallback);
        }

        [Test]
        public void DelivererChain_SkipsAntagonist()
        {
            var roster = new Roster();
            var traitor = Comp(roster, "traitor");
            Comp(roster, "loyal");
            traitor.Status = CompanionStatus.Antagonist;
            var beat = new StoryBeat("b", "traitor").Understudy("loyal");

            Assert.AreEqual("loyal", DelivererChain.Resolve(beat, roster).DelivererId);
        }

        [Test]
        public void CriticalSafe_FailsWhenCarrierCanBetray()
        {
            var roster = new Roster();
            var prot = Comp(roster, "prot");
            prot.IsProtagonist = true;
            var devoted = Comp(roster, "devoted");
            devoted.AdjustLoyalty(30); // 50 → 80 = Devoted
            var wary = Comp(roster, "wary"); // 50 = Steady (способен предать)

            Assert.IsTrue(DelivererChain.IsCriticalSafe(new StoryBeat("b", "prot").Critical(), roster));
            Assert.IsTrue(DelivererChain.IsCriticalSafe(new StoryBeat("b", "devoted").Critical(), roster));
            Assert.IsFalse(DelivererChain.IsCriticalSafe(new StoryBeat("b", "wary").Critical(), roster),
                "критбит на ненадёжном носителе — нарушение (US-14.1)");
        }

        // ---- Структурная честность контента ----
        [Test]
        public void Validate_DetectsDeadEnd()
        {
            var bad = new QuestDefinition("q", "Q", QuestSource.RandomEvent)
                .Stage(QuestStage.SkillCheck("c", "", SkillType.Survival, 3, onSuccess: 1, onFailure: 99))
                .Stage(QuestStage.OutcomeStage("ok", "", true));
            Assert.IsFalse(bad.Validate(out _));
        }

        [Test]
        public void DefaultQuests_AllValidate_NoDeadEnds()
        {
            foreach (var q in DefaultQuests.All())
                Assert.IsTrue(q.Validate(out var err), $"{q.Id}: {err}");
        }
    }
}
