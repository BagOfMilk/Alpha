using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Combat;
using Game.Core.Economy;
using Game.Core.Expeditions;
using Game.Core.Health;
using Game.Core.Stats;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Связка база ↔ бой: отправка (снятие с позиций), путешествие в днях,
    /// возврат последствий в ростер (смерть/ранения/шрамы/XP/лут), протагонист-
    /// защита и айронмен-GameOver.
    /// </summary>
    public class ExpeditionTests
    {
        private static (BaseState baseState, Roster roster, BalanceConfig cfg) MakeBase(BalanceConfig cfg = null)
        {
            cfg = cfg ?? new BalanceConfig();
            var roster = new Roster();
            var baseState = new BaseState(roster, new ResourceLedger(), cfg);
            return (baseState, roster, cfg);
        }

        private static Companion AddComp(Roster roster, string id, bool protagonist = false)
        {
            var c = new Companion(id, new AttributeBlock(3, 3, 3, 3), 4) { IsProtagonist = protagonist };
            roster.Add(c);
            return c;
        }

        private static ExpeditionPlan Plan(int gold = 100, int build = 8, int craft = 5)
            => new ExpeditionPlan("p", "P") { TravelDaysOut = 2, TravelDaysBack = 2,
                RewardGold = gold, RewardBuildingMaterial = build, RewardCraftingMaterial = craft };

        /// <summary>Оружие с фикс уроном для детерминизма.</summary>
        private static WeaponDefinition W(int damage, int apCost = 3)
            => new WeaponDefinition("w", "W", SkillType.Ranged)
            { DamageMin = damage, DamageMax = damage, CritDamageBonus = 1, ApCost = apCost, OptimalRange = 12 };

        private static CombatUnit Enemy(string id, int init, int hp, int dmg)
        {
            var p = new UnitProfile
            {
                DisplayName = id, MaxHp = hp, MaxAp = 8, Accuracy = 99,
                Initiative = init, CanBeDowned = false
            };
            return new CombatUnit(id, Side.Enemy, p, W(dmg));
        }

        // ---- Отправка ----
        [Test]
        public void TrySend_Validations()
        {
            var (baseState, roster, cfg) = MakeBase();
            AddComp(roster, "a");
            var wounded = AddComp(roster, "b");
            wounded.ApplyInjury(InjuryTier.Light, cfg, null);

            Assert.AreEqual(ExpeditionSendResult.EmptySquad,
                new Expedition(baseState, Plan(), cfg).TrySend(new string[0]));
            Assert.AreEqual(ExpeditionSendResult.SquadTooLarge,
                new Expedition(baseState, Plan(), cfg).TrySend(new[] { "a", "x1", "x2", "x3", "x4" }));
            Assert.AreEqual(ExpeditionSendResult.DuplicateCompanion,
                new Expedition(baseState, Plan(), cfg).TrySend(new[] { "a", "a" }));
            Assert.AreEqual(ExpeditionSendResult.CompanionNotFound,
                new Expedition(baseState, Plan(), cfg).TrySend(new[] { "ghost" }));
            Assert.AreEqual(ExpeditionSendResult.CompanionUnavailable,
                new Expedition(baseState, Plan(), cfg).TrySend(new[] { "b" })); // ранен

            var exp = new Expedition(baseState, Plan(), cfg);
            Assert.AreEqual(ExpeditionSendResult.Success, exp.TrySend(new[] { "a" }));
            Assert.AreEqual(CompanionStatus.InSquad, roster.Get("a").Status);
            Assert.AreEqual(ExpeditionSendResult.WrongPhase, exp.TrySend(new[] { "a" })); // повторно нельзя
        }

        [Test]
        public void TrySend_FreesPost_AndPostRejectsSquadMember()
        {
            var (baseState, roster, cfg) = MakeBase();
            AddComp(roster, "medic");
            baseState.AddSlot(new AssignmentSlotDefinition("bed", "Койка", BaseSectionType.Infirmary));
            baseState.TryAssign("medic", "bed");

            var exp = new Expedition(baseState, Plan(), cfg);
            Assert.AreEqual(ExpeditionSendResult.Success, exp.TrySend(new[] { "medic" }));

            Assert.IsFalse(baseState.GetSlot("bed").IsOccupied, "позиция пустует, пока напарник в отряде (US-8.3)");
            Assert.AreEqual(AssignmentResult.CompanionUnavailable, baseState.TryAssign("medic", "bed"));
        }

        [Test]
        public void Depart_AdvancesCalendar_AndConstruction()
        {
            var (baseState, roster, cfg) = MakeBase();
            AddComp(roster, "a");
            baseState.StartConstruction(new Construction("c", "Стройка", BaseSectionType.Workshop, 3));

            var exp = new Expedition(baseState, Plan(), cfg);
            exp.TrySend(new[] { "a" });
            exp.Depart();

            Assert.AreEqual(2, baseState.CurrentDay);
            Assert.AreEqual(1.0, baseState.ConstructionQueue[0].RemainingDays, 0.001); // 3 − 2
            Assert.AreEqual(ExpeditionPhase.Away, exp.Phase);
        }

        // ---- Возврат: победа ----
        private static (Expedition exp, CombatState cs, BaseState baseState, Roster roster, BalanceConfig cfg)
            VictoryScenario(BalanceConfig cfg = null)
        {
            var (baseState, roster, c) = MakeBase(cfg);
            AddComp(roster, "a");
            AddComp(roster, "b");
            AddComp(roster, "c");

            var exp = new Expedition(baseState, Plan(), c);
            Assert.AreEqual(ExpeditionSendResult.Success, exp.TrySend(new[] { "a", "b", "c" }));
            exp.Depart();

            // Бой: первый юнит отряда убивает единственного врага → победа.
            var cs = new CombatState(new GridMap(12, 1), c, new ScriptedRng(1, 100, 5));
            var units = exp.BuildCombatUnits(_ => W(5));
            for (int i = 0; i < units.Count; i++) cs.AddUnit(units[i], new GridPos(i, 0));
            cs.AddUnit(Enemy("e", 0, hp: 3, dmg: 5), new GridPos(5, 0));
            cs.Begin();
            Assert.AreEqual(CombatActionResult.Success, cs.Attack("e"));
            Assert.AreEqual(CombatOutcome.Victory, cs.Outcome);
            return (exp, cs, baseState, roster, c);
        }

        [Test]
        public void Conclude_Victory_BanksLoot_GrantsXp_AppliesInjuries()
        {
            var cfg = new BalanceConfig { XpPerExpeditionVictory = 100 }; // ровно на уровень 2
            var (exp, cs, baseState, roster, _) = VictoryScenario(cfg);

            // Итог боя: u_b потрёпан (3/10 HP → Лёгкое), u_c вытащен стабилизацией (Серьёзное + шрам).
            cs.GetUnit("u_b").Hp = 3;
            cs.GetUnit("u_c").LifeState = UnitLifeState.Stabilized;

            var report = exp.Conclude(cs);

            Assert.AreEqual(CombatOutcome.Victory, report.Outcome);
            Assert.AreEqual(100, baseState.Resources.Get(ResourceType.Gold));
            Assert.AreEqual(8, baseState.Resources.Get(ResourceType.BuildingMaterial));
            Assert.AreEqual(5, baseState.Resources.Get(ResourceType.CraftingMaterial));

            // a: цел → в лагере, без ранений; уровень поднял.
            Assert.AreEqual(CompanionStatus.InCamp, roster.Get("a").Status);
            Assert.IsFalse(roster.Get("a").IsInjured);

            // b: Лёгкое (2 дня) — выздоровел уже по дороге домой (2 дня пути).
            var ocB = report.Companions.Find(o => o.CompanionId == "b");
            Assert.AreEqual(InjuryTier.Light, ocB.Injury);
            Assert.IsNull(ocB.ScarId, "лёгкие раны шрамов не дают");
            Assert.Contains("b", report.RecoveredOnReturn);
            Assert.IsFalse(roster.Get("b").IsInjured);

            // c: Серьёзное (5 дней) + вечный шрам; 2 дня дороги → осталось 3.
            var ocC = report.Companions.Find(o => o.CompanionId == "c");
            Assert.AreEqual(InjuryTier.Serious, ocC.Injury);
            Assert.IsNotNull(ocC.ScarId);
            Assert.AreEqual(1, roster.Get("c").Scars.Count);
            Assert.AreEqual(CompanionStatus.Injured, roster.Get("c").Status);
            Assert.AreEqual(3.0, roster.Get("c").RecoveryDaysRemaining, 0.001);

            Assert.AreEqual(3, report.LeveledUp.Count, "XP за победу — всем выжившим");
            Assert.AreEqual(4, baseState.CurrentDay); // 2 туда + 2 обратно
            Assert.AreEqual(ExpeditionPhase.Concluded, exp.Phase);
            Assert.Throws<System.InvalidOperationException>(() => exp.Conclude(cs));
        }

        // ---- Возврат: поражение ----
        [Test]
        public void Conclude_Defeat_NoLoot_CriticalInjury()
        {
            var (baseState, roster, cfg) = MakeBase();
            AddComp(roster, "victim");

            var exp = new Expedition(baseState, Plan(gold: 100), cfg);
            exp.TrySend(new[] { "victim" });
            exp.Depart();

            // Враг одним выстрелом роняет единственного бойца → поражение.
            var cs = new CombatState(new GridMap(12, 1), cfg, new ScriptedRng(1, 100, 10));
            var units = exp.BuildCombatUnits(_ => W(5));
            cs.AddUnit(units[0], new GridPos(0, 0));
            cs.AddUnit(Enemy("e", 10, hp: 50, dmg: 10), new GridPos(5, 0));
            cs.Begin();
            Assert.AreEqual(CombatActionResult.Success, cs.Attack("u_victim"));
            Assert.AreEqual(CombatOutcome.Defeat, cs.Outcome);

            var report = exp.Conclude(cs);

            Assert.AreEqual(0, baseState.Resources.Get(ResourceType.Gold), "лут не банкуется при поражении");
            var oc = report.Companions.Find(o => o.CompanionId == "victim");
            Assert.IsFalse(oc.Died);
            Assert.AreEqual(InjuryTier.Critical, oc.Injury);
            Assert.IsNotNull(oc.ScarId);
            Assert.AreEqual(8.0, roster.Get("victim").RecoveryDaysRemaining, 0.001); // 10 − 2 дороги
            Assert.AreEqual(0, report.LeveledUp.Count, "XP только за победу");
        }

        // ---- Протагонист: защита и айронмен ----
        private static (Expedition exp, CombatState cs, Roster roster, BalanceConfig cfg)
            ProtagonistDownScenario(bool ironman)
        {
            var cfg = new BalanceConfig { Ironman = ironman };
            var (baseState, roster, _) = MakeBase(cfg);
            AddComp(roster, "prot", protagonist: true);
            AddComp(roster, "ally");

            var exp = new Expedition(baseState, Plan(), cfg);
            exp.TrySend(new[] { "prot", "ally" });
            exp.Depart();

            // RNG: выстрел врага по протагонисту [1,100,10], потом добивание врага союзником [1,100,5].
            var cs = new CombatState(new GridMap(12, 1), cfg, new ScriptedRng(1, 100, 10, 1, 100, 5));
            var units = exp.BuildCombatUnits(_ => W(5));
            cs.AddUnit(units[0], new GridPos(0, 0)); // prot (инициатива 6)
            cs.AddUnit(units[1], new GridPos(1, 0)); // ally (инициатива 6, после prot)
            cs.AddUnit(Enemy("e", 10, hp: 3, dmg: 10), new GridPos(5, 0));
            cs.Begin(); // ход врага

            Assert.AreEqual(CombatActionResult.Success, cs.Attack("u_prot")); // протагонист падает (окно 2)
            cs.EndTurn(); // prot: окно 2→1, пропуск → ход ally
            cs.EndTurn(); // ally пас → раунд 2: ход врага
            cs.EndTurn(); // враг пас → prot: окно 1→0 — развилка защиты
            return (exp, cs, roster, cfg);
        }

        [Test]
        public void Protagonist_NormalMode_SurvivesExpiredWindow()
        {
            var (exp, cs, roster, _) = ProtagonistDownScenario(ironman: false);
            Assert.AreEqual(UnitLifeState.Stabilized, cs.GetUnit("u_prot").LifeState, "сюжетная защита: жив, но выбыл");

            cs.Attack("e"); // союзник добивает врага → победа
            Assert.AreEqual(CombatOutcome.Victory, cs.Outcome);

            var report = exp.Conclude(cs);
            Assert.IsFalse(report.GameOver);
            Assert.IsTrue(roster.Get("prot").IsAlive);
            Assert.AreEqual(InjuryTier.Serious, report.Companions.Find(o => o.CompanionId == "prot").Injury);
        }

        [Test]
        public void Protagonist_Ironman_DiesAndGameOver()
        {
            var (exp, cs, roster, _) = ProtagonistDownScenario(ironman: true);
            Assert.AreEqual(UnitLifeState.Dead, cs.GetUnit("u_prot").LifeState, "в айронмене защиты нет");

            cs.Attack("e");
            Assert.AreEqual(CombatOutcome.Victory, cs.Outcome);

            var report = exp.Conclude(cs);
            Assert.IsTrue(report.GameOver, "айронмен: гибель протагониста = game over");
            Assert.IsFalse(roster.Get("prot").IsAlive);
            Assert.IsTrue(report.Companions.Find(o => o.CompanionId == "prot").Died);
        }
    }
}
