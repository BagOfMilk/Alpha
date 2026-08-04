using Game.Core;
using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Combat;
using Game.Core.Council;
using Game.Core.Economy;
using Game.Core.Expeditions;
using Game.Core.Items;
using Game.Core.Quests;
using Game.Core.Saves;
using Game.Core.Stats;
using Game.Core.Story;
using Game.Core.Threats;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Кампания как целое (итерация 11): финал по сюжетной вехе с Готовностью
    /// (US-11.4/14.2), спайн-квесты, единый ход времени (совет — time-sink) и
    /// жизненный цикл вылазки (InExpedition ведёт оркестрация, US-16.1).
    /// </summary>
    public class CampaignTests
    {
        // ---- Финал (US-11.4/14.2) ----
        [Test]
        public void Finale_LockedUntilStoryMilestone()
        {
            var campaign = Campaign.NewGame(new BalanceConfig());
            Assert.IsFalse(FinalBattle.IsUnlocked(campaign.Flags));
            Assert.Throws<System.InvalidOperationException>(() => FinalBattle.BuildEncounter(campaign));
            Assert.Throws<System.InvalidOperationException>(() => FinalBattle.Resolve(campaign, true));
        }

        [Test]
        public void Spine_Validates_AndLeadsToFinaleFlag()
        {
            foreach (var q in DefaultQuests.StorySpine())
                Assert.IsTrue(q.Validate(out var err), $"{q.Id}: {err}");

            var campaign = Campaign.NewGame(new BalanceConfig());
            var log = new QuestLog();
            log.CollectFrom(DefaultQuests.StorySpine(), campaign.Factions, campaign.Flags, campaign.Roster.All);
            Assert.AreEqual(QuestStatus.Available, log.StatusOf("spine_act1"));
            Assert.IsNull(log.StatusOf("spine_act2"), "акт 2 заперт флагом акта 1 (US-14.2)");

            // Акт 1: ЛЮБОЙ исход двигает спайн (мягкие фейл-стейты, US-16.2).
            var act1 = new QuestRun(DefaultQuests.SpineAct1Shadow(), campaign.Base, campaign.Cfg,
                campaign.Factions, campaign.Base.ThreatsSystem, campaign.Flags);
            act1.ResolveCheck(campaign.Roster.All);
            Assert.IsTrue(campaign.Flags.Contains(DefaultQuests.SpineAct1Flag));

            // Акт 2 открылся; оборонительный путь ставит веху финала.
            log.CollectFrom(DefaultQuests.StorySpine(), campaign.Factions, campaign.Flags, campaign.Roster.All);
            Assert.AreEqual(QuestStatus.Available, log.StatusOf("spine_act2"));

            var act2 = new QuestRun(DefaultQuests.SpineAct2Storm(), campaign.Base, campaign.Cfg,
                campaign.Factions, campaign.Base.ThreatsSystem, campaign.Flags);
            var step = act2.Choose(1, campaign.Roster.All); // «укрепляться» → dig_in
            Assert.IsTrue(step.Terminal && step.QuestSucceeded);
            Assert.IsTrue(FinalBattle.IsUnlocked(campaign.Flags), "веха финала поставлена сюжетом, не порогом");
        }

        [Test]
        public void EffectiveReadiness_CountsCouncilAndRosterStrength()
        {
            var cfg = new BalanceConfig();
            var campaign = Campaign.NewGame(cfg);

            // Сила ростера (US-11.4): 6 живых 1-го уровня.
            double roster = 6 * (cfg.ReadinessPerAliveCompanion + cfg.ReadinessPerCompanionLevel);
            Assert.AreEqual(roster,
                FinalBattle.EffectiveReadiness(campaign.Base.ThreatsSystem, campaign.Roster, cfg), 0.001);
            Assert.AreEqual(ReadinessBand.Unprepared,
                FinalBattle.BandFor(roster, cfg),
                "нетронутый ростер сам по себе НЕ даёт полосу: её берут вложениями в город");

            campaign.Base.ThreatsSystem.Readiness.AddPreparation(); // совет: +10
            double withPrep = roster + cfg.ReadinessPerPreparation;
            Assert.AreEqual(withPrep,
                FinalBattle.EffectiveReadiness(campaign.Base.ThreatsSystem, campaign.Roster, cfg), 0.001);

            campaign.Roster.Get("brawler").Kill(); // потери снижают готовность
            Assert.Less(FinalBattle.EffectiveReadiness(campaign.Base.ThreatsSystem, campaign.Roster, cfg),
                withPrep, "смерть бойца ослабляет оборону");
        }

        [Test]
        public void Readiness_MaxedRoster_StillNeedsCityInvestment()
        {
            var cfg = new BalanceConfig();
            var campaign = Campaign.NewGame(cfg);
            foreach (var c in campaign.Roster.All) c.GainXp(5000, cfg); // потолок реального прогона

            double rosterOnly = FinalBattle.EffectiveReadiness(campaign.Base.ThreatsSystem, campaign.Roster, cfg);
            Assert.AreEqual(ReadinessBand.Unprepared, FinalBattle.BandFor(rosterOnly, cfg),
                "прокачанный ростер без вложений в город не должен брать полосу бесплатно");
        }

        [Test]
        public void Readiness_FortifiedBand_IsReachable()
        {
            var cfg = new BalanceConfig();
            var campaign = Campaign.NewGame(cfg);
            foreach (var c in campaign.Roster.All) c.GainXp(1400, cfg); // ~6 уровень

            var readiness = campaign.Base.ThreatsSystem.Readiness;
            readiness.AddFortification(); // Укрепления
            readiness.AddPreparation();   // единственная «Подготовка» за кампанию

            Assert.AreEqual(ReadinessBand.Fortified,
                FinalBattle.BandFor(
                    FinalBattle.EffectiveReadiness(campaign.Base.ThreatsSystem, campaign.Roster, cfg), cfg),
                "верхняя полоса обязана быть достижимой (иначе порог — декорация)");
        }

        [Test]
        public void FinaleEncounter_ScalesWithReadiness_NotHpSponges()
        {
            var unprepared = FinalBattle.BuildEncounter(ReadinessBand.Unprepared);
            var braced = FinalBattle.BuildEncounter(ReadinessBand.Braced);
            var fortified = FinalBattle.BuildEncounter(ReadinessBand.Fortified);

            Assert.Greater(unprepared.Enemies.Count, braced.Enemies.Count, "готовность режет волну составом");
            Assert.Greater(braced.Enemies.Count, fortified.Enemies.Count);
            Assert.AreEqual(0, unprepared.DefenderAccuracyBonus);
            Assert.Greater(fortified.DefenderAccuracyBonus, braced.DefenderAccuracyBonus,
                "укрепления прикрывают защитников");
            Assert.IsTrue(unprepared.Enemies.Exists(e => e.Id == "plague_bearer"),
                "неготовых встречает и Чумоносец");
        }

        [Test]
        public void Finale_CannotBeReplayed_AfterCampaignEnds()
        {
            var campaign = Campaign.NewGame(new BalanceConfig());
            campaign.Flags.Add(FinalBattle.ReadyFlag);
            FinalBattle.Resolve(campaign, won: true);

            Assert.Throws<System.InvalidOperationException>(() => FinalBattle.Resolve(campaign, false),
                "выигранную кампанию нельзя перезаписать поражением (сейв/лоад не переигрывает финал)");
            Assert.Throws<System.InvalidOperationException>(() => FinalBattle.BuildEncounter(campaign));
            Assert.AreEqual(CampaignOutcome.Won, campaign.Outcome);
        }

        [Test]
        public void Spine_CompletedActs_NotReofferedByFreshLog()
        {
            // Как после загрузки: флаги в сейве есть, журнал собирается заново.
            var campaign = Campaign.NewGame(new BalanceConfig());
            campaign.Flags.Add(DefaultQuests.SpineAct1Flag);
            campaign.Flags.Add(DefaultQuests.FinaleReadyFlag);

            var log = new QuestLog();
            log.CollectFrom(DefaultQuests.StorySpine(), campaign.Factions, campaign.Flags, campaign.Roster.All);
            Assert.IsNull(log.StatusOf("spine_act1"), "пройденный акт не предлагается заново (анти-ферма наград)");
            Assert.IsNull(log.StatusOf("spine_act2"));
        }

        [Test]
        public void Ironman_SingleSourceOfTruth_InCfg()
        {
            var cfg = new BalanceConfig { Ironman = false };
            var campaign = Campaign.NewGame(cfg);
            campaign.Ironman = true; // тумблер (US-16.1)
            Assert.IsTrue(campaign.Cfg.Ironman,
                "переключение видит и боевой слой: протагонист смертен, game over работает");
        }

        [Test]
        public void Finale_Victory_WinsCampaign_Defeat_IsGameOver()
        {
            var wonRun = Campaign.NewGame(new BalanceConfig());
            wonRun.Flags.Add(FinalBattle.ReadyFlag);
            var report = FinalBattle.Resolve(wonRun, won: true);
            Assert.AreEqual(CampaignOutcome.Won, wonRun.Outcome);
            Assert.IsTrue(wonRun.Flags.Contains(FinalBattle.WonFlag));
            Assert.IsTrue(report.Won);

            var lostRun = Campaign.NewGame(new BalanceConfig());
            lostRun.Flags.Add(FinalBattle.ReadyFlag);
            FinalBattle.Resolve(lostRun, won: false);
            Assert.AreEqual(CampaignOutcome.Lost, lostRun.Outcome, "game over — только финал (US-16.2)");
        }

        // ---- Контент типов урона (US-3.12): огонь/токсин реально накладывают DoT ----
        [Test]
        public void FireAndToxin_Content_ProcsDots()
        {
            var ember = DefaultItems.Ember();
            Assert.IsTrue(ember.IsNamed);
            Assert.AreEqual(DamageType.Fire, ember.Weapon.Damage);
            Assert.AreEqual(StatusType.Burning, ember.Weapon.StatusOnHit, "огонь накладывает Поджог");

            var bearer = DefaultContent.PlagueBearer();
            Assert.AreEqual(DamageType.Toxin, bearer.Weapon.Damage);
            Assert.AreEqual(StatusType.Poisoned, bearer.Weapon.StatusOnHit, "токсин накладывает Яд");
            Assert.AreEqual(EnemyRole.Controller, bearer.Role);
        }

        // ---- Оркестратор: единый ход времени ----
        [Test]
        public void AdvanceDays_TicksCouncil_NoCalendarDesync()
        {
            var cfg = new BalanceConfig();
            var campaign = Campaign.NewGame(cfg);
            campaign.Base.Resources.Add(ResourceType.Gold, 100);
            campaign.Factions.AddInfluence(5);

            var council = DefaultCouncil.NewCouncil(campaign.Factions, campaign.Base.Resources,
                campaign.Base.ThreatsSystem, campaign.Base);
            campaign.AttachCouncil(council);

            Assert.AreEqual(CouncilActionResult.Success, council.Execute(DefaultCouncil.Prepare).Result);
            Assert.Greater(council.CooldownRemaining(DefaultCouncil.Prepare), 0);

            campaign.AdvanceDays(6); // единый ход времени тикает и совет (time-sink)
            Assert.AreEqual(0, council.CooldownRemaining(DefaultCouncil.Prepare),
                "КД совета живёт в календаре кампании — рассинхрона нет");
        }

        // ---- Жизненный цикл вылазки (US-16.1) ----
        [Test]
        public void Expedition_Lifecycle_DrivesInExpeditionGate()
        {
            var cfg = new BalanceConfig { Ironman = true };
            var campaign = Campaign.NewGame(cfg);
            var plan = new ExpeditionPlan("p", "Точка") { TravelDaysOut = 1, TravelDaysBack = 1, RewardGold = 10 };

            var exp = campaign.LaunchExpedition(plan);
            Assert.AreEqual(ExpeditionSendResult.Success, exp.TrySend(new[] { "marksman", "brawler" }));
            Assert.IsTrue(campaign.CanQuickSave, "до выхода — сейв свободен");

            campaign.DepartExpedition();
            Assert.IsTrue(campaign.InExpedition);
            Assert.IsFalse(campaign.CanQuickSave, "в вылазке под айронменом сейв заблокирован (US-16.1)");

            // Мини-бой: один выстрел сносит единственного врага.
            var cs = new CombatState(new GridMap(12, 1), cfg, new ScriptedRng(1, 100, 5));
            var units = exp.BuildCombatUnits(_ => Wpn(5));
            for (int i = 0; i < units.Count; i++) cs.AddUnit(units[i], new GridPos(i, 0));
            cs.AddUnit(EnemyUnit("e", 3), new GridPos(5, 0));
            cs.Begin();
            cs.Attack("e");
            Assert.AreEqual(CombatOutcome.Victory, cs.Outcome);

            campaign.ConcludeExpedition(cs);
            Assert.IsFalse(campaign.InExpedition, "вернулись — гейт снят оркестрацией");
            Assert.IsNull(campaign.ActiveExpedition);
            Assert.IsTrue(campaign.CanQuickSave);
            Assert.AreEqual(10, campaign.Base.Resources.Get(ResourceType.Gold));
        }

        // ---- Создание протагониста (US-2.7): point-buy поверх бэкграунда ----
        [Test]
        public void ProtagonistBuilder_PointBuy_BudgetsAndCaps()
        {
            var cfg = new BalanceConfig
            { ProtagonistAttributePoints = 2, ProtagonistSkillPoints = 3, CreationSkillMax = 4 };
            var b = new ProtagonistBuilder(DefaultContent.Leader(), cfg); // старт 4/4/5/5, Тактика 2

            // «Пустой» атрибут не съедает очко (симметрично RaiseSkill(None)).
            int budget = b.AttributePointsRemaining;
            Assert.AreEqual(CreationStep.AtCap, b.RaiseAttribute(AttributeType.None));
            Assert.AreEqual(budget, b.AttributePointsRemaining, "очко не сгорело в None");

            Assert.AreEqual(CreationStep.Ok, b.RaiseAttribute(AttributeType.Strength)); // 4→5
            Assert.AreEqual(CreationStep.Ok, b.RaiseAttribute(AttributeType.Wits));     // 5→6
            Assert.AreEqual(CreationStep.NoBudget, b.RaiseAttribute(AttributeType.Will),
                "бюджет очков атрибутов кончился");

            Assert.AreEqual(CreationStep.Ok, b.RaiseSkill(SkillType.Tactics)); // 2→3
            Assert.AreEqual(CreationStep.Ok, b.RaiseSkill(SkillType.Tactics)); // 3→4
            Assert.AreEqual(CreationStep.AtCap, b.RaiseSkill(SkillType.Tactics), "потолок скила при создании");

            b.DisplayName = "Кастомный лидер";
            var hero = b.Build("leader");
            Assert.IsTrue(hero.IsProtagonist);
            Assert.AreEqual(5, hero.GetAttribute(AttributeType.Strength));
            Assert.AreEqual(4, hero.GetSkill(SkillType.Tactics));
            Assert.AreEqual("Кастомный лидер", hero.DisplayName);
        }

        [Test]
        public void ProtagonistBuilder_ExtraTraits_LimitedByBudget()
        {
            var cfg = new BalanceConfig { ProtagonistExtraTraits = 1 };
            var b = new ProtagonistBuilder(DefaultContent.Marksman(), cfg); // sharp_eye уже в бэкграунде
            Assert.AreEqual(CreationStep.DuplicateTrait, b.AddTrait(DefaultContent.SharpEye()));
            Assert.AreEqual(CreationStep.Ok, b.AddTrait(DefaultContent.SilverTongue()));
            Assert.AreEqual(CreationStep.NoTraitRoom, b.AddTrait(DefaultContent.Handy()),
                "бюджет стартовых трейтов сверх бэкграунда");
        }

        [Test]
        public void NewGame_WithCustomProtagonist_ReplacesLeader()
        {
            var cfg = new BalanceConfig();
            var b = new ProtagonistBuilder(DefaultContent.Medic(), cfg);
            var campaign = Campaign.NewGame(cfg, b.Build("leader"));

            Assert.AreEqual(6, campaign.Roster.Count, "кастом занял место дефолтного лидера");
            var hero = campaign.Roster.Get("leader");
            Assert.IsTrue(hero.IsProtagonist);
            Assert.AreEqual(3, hero.GetSkill(SkillType.Medicine), "старт бэкграунда Медика при кастоме");
        }

        [Test]
        public void NewGame_CustomProtagonist_WrongId_ThrowsInsteadOfSilentLoss()
        {
            var cfg = new BalanceConfig();
            var b = new ProtagonistBuilder(DefaultContent.Medic(), cfg);
            Assert.Throws<System.ArgumentException>(() => Campaign.NewGame(cfg, b.Build("hero")),
                "id != leader ломает спайн — явная ошибка вместо тихой потери героя");
            Assert.Throws<System.ArgumentException>(() => Campaign.NewGame(cfg, b.Build("medic")),
                "коллизия с бэкграундным id — тоже явная ошибка");
        }

        [Test]
        public void Perks_LiveInCampaignLifecycle_NewGameAndSave()
        {
            var cfg = new BalanceConfig();
            var campaign = Campaign.NewGame(cfg);
            Assert.IsTrue(campaign.Roster.Get("marksman").HasPerk("steady_hand"),
                "стартовый ростер сразу с перками (Ranged 3 ≥ порога 2)");
            Assert.IsTrue(campaign.Roster.Get("leader").HasPerk("light_step"));

            var json = UnityEngine.JsonUtility.ToJson(SaveSystem.Capture(campaign));
            var loaded = SaveSystem.Restore(UnityEngine.JsonUtility.FromJson<SaveData>(json),
                cfg, ContentCatalog.Default());
            Assert.IsTrue(loaded.Roster.Get("marksman").HasPerk("steady_hand"),
                "перки пересчитываются при загрузке — чек-бонусы не теряются");
        }

        // ---- Карта мира (US-1.1): узлы с доступностью ----
        [Test]
        public void WorldMap_GatesNodes_ByTierAndFlags()
        {
            var campaign = Campaign.NewGame(new BalanceConfig());
            var map = DefaultWorld.NewMap();

            var open = map.Available(campaign.Base.CityTier, campaign.Flags);
            Assert.AreEqual(1, open.Count, "на старте открыт только Восточный тракт");
            Assert.AreEqual("east_road", open[0].Id);
            Assert.AreEqual(2, map.Locked(campaign.Base.CityTier, campaign.Flags).Count,
                "недоступные узлы видимы отдельным списком (US-1.1)");

            campaign.Base.AdvanceCityTier(); // тир 2 открывает дальнюю точку (US-7.6)
            Assert.IsTrue(map.IsAvailable(map.Get("rusted_works"), campaign.Base.CityTier, campaign.Flags));

            campaign.Flags.Add(FinalBattle.ReadyFlag); // веха открывает финальную окраину
            Assert.AreEqual(3, map.Available(campaign.Base.CityTier, campaign.Flags).Count);
        }

        // ---- Ачивки (US-16.1): только в айронмене, персистятся ----
        [Test]
        public void Achievements_OnlyInIronman_AndPersist()
        {
            var casual = Campaign.NewGame(new BalanceConfig { Ironman = false });
            casual.Flags.Add(FinalBattle.ReadyFlag);
            FinalBattle.Resolve(casual, won: true);
            Assert.AreEqual(0, casual.Achievements.Count, "вне айронмена ачивки не берутся");

            var iron = Campaign.NewGame(new BalanceConfig { Ironman = true });
            iron.Flags.Add(FinalBattle.ReadyFlag);
            FinalBattle.Resolve(iron, won: true);
            Assert.IsTrue(iron.Achievements.Contains(FinalBattle.IronVictoryAchievement));

            var json = UnityEngine.JsonUtility.ToJson(SaveSystem.Capture(iron));
            var loaded = SaveSystem.Restore(UnityEngine.JsonUtility.FromJson<SaveData>(json),
                new BalanceConfig(), ContentCatalog.Default());
            Assert.IsTrue(loaded.Achievements.Contains(FinalBattle.IronVictoryAchievement), "ачивки в сейве");
        }

        // ---- Онбординг (US-17.4): ведомые шаги + пролог с микс-развилкой (US-17.1) ----
        [Test]
        public void Onboarding_GuidedSteps_FollowCampaignState()
        {
            var campaign = Campaign.NewGame(new BalanceConfig());
            var flow = new OnboardingFlow();
            Assert.AreEqual(OnboardingStep.Prologue, flow.Step);
            Assert.IsFalse(string.IsNullOrEmpty(flow.Hint), "у каждого шага есть подсказка");

            // Пролог: бой → развилка «прикрыть переговорщика».
            var prologue = new QuestRun(DefaultQuests.Prologue(), campaign.Base, campaign.Cfg,
                campaign.Factions, campaign.Base.ThreatsSystem, campaign.Flags);
            prologue.ResolveCombat(won: true);
            var step = prologue.Choose(0, campaign.Roster.All);
            Assert.IsTrue(step.Terminal && step.QuestSucceeded);
            Assert.IsTrue(flow.TryAdvance(campaign), "флаг пролога закрывает шаг");
            Assert.AreEqual(OnboardingStep.SettlementIntro, flow.Step);

            flow.AcknowledgeIntro(campaign);
            Assert.AreEqual(OnboardingStep.FirstAssignment, flow.Step);
            Assert.IsFalse(flow.TryAdvance(campaign), "никто не назначен — шаг открыт");

            campaign.Base.TryAssign("medic", "infirmary_bed");
            Assert.IsTrue(flow.TryAdvance(campaign));
            Assert.AreEqual(OnboardingStep.FirstWait, flow.Step);

            campaign.AdvanceDays(1);
            Assert.IsTrue(flow.TryAdvance(campaign));
            Assert.AreEqual(OnboardingStep.FirstExpedition, flow.Step);

            flow.NotifyExpeditionConcluded(campaign);
            Assert.IsTrue(flow.IsDone, "петля собрана — системы введены по одной");
        }

        [Test]
        public void Onboarding_FastForwards_FromSavedFlags()
        {
            // Как после загрузки: флаги в сейве есть, flow создаётся заново.
            var campaign = Campaign.NewGame(new BalanceConfig());
            campaign.Flags.Add(DefaultQuests.PrologueDoneFlag);
            campaign.Flags.Add(OnboardingFlow.IntroAckFlag);
            campaign.Base.TryAssign("medic", "infirmary_bed");
            campaign.Flags.Add(OnboardingFlow.WaitedFlag);
            campaign.Flags.Add(OnboardingFlow.DoneFlag);

            var flow = new OnboardingFlow();
            while (flow.TryAdvance(campaign)) { }
            Assert.IsTrue(flow.IsDone, "онбординг восстановился из персистентных флагов");
        }

        [Test]
        public void Onboarding_EarlyExpedition_IsSticky()
        {
            var campaign = Campaign.NewGame(new BalanceConfig());
            var flow = new OnboardingFlow();
            flow.NotifyExpeditionConcluded(campaign); // вылазка случилась ДО шага — не теряется

            campaign.Flags.Add(DefaultQuests.PrologueDoneFlag);
            flow.TryAdvance(campaign);          // → SettlementIntro
            flow.AcknowledgeIntro(campaign);    // → FirstAssignment
            campaign.Base.TryAssign("medic", "infirmary_bed");
            flow.TryAdvance(campaign);          // → FirstWait
            campaign.AdvanceDays(1);
            flow.TryAdvance(campaign);          // → FirstExpedition
            Assert.IsTrue(flow.TryAdvance(campaign), "липкое событие закрывает шаг");
            Assert.IsTrue(flow.IsDone);
        }

        [Test]
        public void Prologue_SpurnedPath_SeedsAct1Boss()
        {
            var campaign = Campaign.NewGame(new BalanceConfig());
            var negotiator = campaign.Roster.Get("negotiator");
            negotiator.Equipment.Equip(ItemInstance.NamedFrom(DefaultItems.Whisper()));

            var prologue = new QuestRun(DefaultQuests.Prologue(), campaign.Base, campaign.Cfg,
                campaign.Factions, campaign.Base.ThreatsSystem, campaign.Flags);
            prologue.ResolveCombat(won: true);
            prologue.Choose(2, campaign.Roster.All); // «уходить, не оглядываясь»

            Assert.IsTrue(campaign.Flags.Contains(DefaultQuests.PrologueSpurnedFlag));
            var record = Prologue.TrySeedDefector(campaign);
            Assert.IsNotNull(record, "переговорщик ушёл к злодею — босс акта 1 засеян (US-17.1)");
            Assert.AreEqual(CompanionStatus.Antagonist, negotiator.Status);
            Assert.AreEqual(1, record.CapturedGear.Count, "ушёл со своим гиром (US-9.4)");
            Assert.IsTrue(campaign.Flags.Contains(Prologue.BossSeededFlag));
            Assert.IsNull(Prologue.TrySeedDefector(campaign), "сид одноразовый");
        }

        [Test]
        public void Prologue_LoyalPath_NoBossSeeded()
        {
            var campaign = Campaign.NewGame(new BalanceConfig());
            Assert.IsTrue(DefaultQuests.Prologue().Validate(out var err), err);

            var prologue = new QuestRun(DefaultQuests.Prologue(), campaign.Base, campaign.Cfg,
                campaign.Factions, campaign.Base.ThreatsSystem, campaign.Flags);
            prologue.ResolveCombat(won: true);
            prologue.Choose(0, campaign.Roster.All); // прикрыли переговорщика

            Assert.AreEqual(58, campaign.Roster.Get("negotiator").Loyalty, "поддержка видна сразу (US-9.2)");
            Assert.IsNull(Prologue.TrySeedDefector(campaign), "лояльный путь боссов не сеет");
        }

        private static WeaponDefinition Wpn(int damage) =>
            new WeaponDefinition("w", "Ствол", SkillType.Ranged)
            { DamageMin = damage, DamageMax = damage, CritDamageBonus = 1, ApCost = 3, OptimalRange = 12 };

        private static CombatUnit EnemyUnit(string id, int hp)
        {
            var p = new UnitProfile
            {
                DisplayName = id, MaxHp = hp, MaxAp = 8, Accuracy = 99,
                Initiative = 0, CanBeDowned = false
            };
            return new CombatUnit(id, Side.Enemy, p, Wpn(1));
        }
    }
}
