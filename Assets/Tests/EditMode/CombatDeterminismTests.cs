using System;
using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Characters;
using Game.Core.Combat;
using Game.Core.Randomness;
using Game.Core.Stats;
using Game.Gameplay.Combat;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Б1, приймання пакета: parity ThresholdRule/PercentRule на одному сценарії,
    /// детермінізм (той самий сід — той самий лог), гарантія завершуваності автобою
    /// (запобіжник раундів → Draw), відступ, тренувальний бій,
    /// BattleSetup→CombatState→BattleResult через CombatBattleBuilder.
    /// </summary>
    public class CombatDeterminismTests
    {
        private static readonly BalanceConfig Cfg = new BalanceConfig();

        private static CombatUnit Unit(string id, Side side, int critChance = 0) => new CombatUnit(id, side,
            new UnitProfile { DisplayName = id, MaxHp = 10, MaxAp = 8, Accuracy = 70, CritChance = critChance }, null);

        // ---- Parity: обидва правила згодні на краях полоси ----
        [Test]
        public void Parity_ThresholdAndPercent_AgreeOnLowMargin_BothMiss()
        {
            var threshold = new ThresholdRule(Cfg);
            var percent = new PercentRule(Cfg);
            var a = Unit("a", Side.Player);
            var t = Unit("t", Side.Enemy);

            const int shown = 10; // margin −40 під Threshold; свідомо поганий кидок під Percent
            Assert.AreEqual(AttackOutcome.Miss, threshold.Resolve(a, t, shown, null));
            Assert.AreEqual(AttackOutcome.Miss, percent.Resolve(a, t, shown, new ScriptedDiceRoller(0.99)));
        }

        [Test]
        public void Parity_ThresholdAndPercent_AgreeOnHighMargin_BothCrit()
        {
            var threshold = new ThresholdRule(Cfg);
            var percent = new PercentRule(Cfg);
            var a = Unit("a", Side.Player, critChance: 100); // крит гарантований будь-яким другим кидком
            var t = Unit("t", Side.Enemy);

            const int shown = 95; // margin 45 ≥ CritBand(35) під Threshold; свідомо вдалий кидок під Percent
            Assert.AreEqual(AttackOutcome.Crit, threshold.Resolve(a, t, shown, null));
            Assert.AreEqual(AttackOutcome.Crit, percent.Resolve(a, t, shown, new ScriptedDiceRoller(0.0, 0.0)));
        }

        // ---- Детермінізм: той самий сід — той самий бій ПОЕЛЕМЕНТНО ----
        [Test]
        public void Determinism_SameSeed_ProducesIdenticalBattleLog()
        {
            (IReadOnlyList<string> log, IReadOnlyList<AttackRecord> attacks, CombatOutcome outcome) RunOnce()
            {
                var map = new GridMap(10, 6);
                map.SetCover(new GridPos(4, 2), Direction.West, CoverType.Half);
                var cs = new CombatState(map, Cfg, new PercentRule(Cfg), new SeededDiceRoller(1234));
                cs.AddUnit(Unit("p1", Side.Player), new GridPos(1, 1));
                cs.AddUnit(Unit("p2", Side.Player), new GridPos(1, 3));
                cs.AddUnit(CombatUnit.FromEnemy(DefaultCombatContent.HordeScout(), "e1"), new GridPos(8, 1));
                cs.AddUnit(CombatUnit.FromEnemy(DefaultCombatContent.HordeSkirmisher(), "e2"), new GridPos(8, 4));
                cs.Begin();
                CombatAi.AutoResolve(cs, 250);
                return (cs.Log, cs.Attacks, cs.Outcome);
            }

            var run1 = RunOnce();
            var run2 = RunOnce();

            CollectionAssert.AreEqual(run1.log, run2.log, "тот же сид и та же последовательность решений — побайтовый повтор лога");
            Assert.AreEqual(run1.outcome, run2.outcome);
            Assert.AreEqual(run1.attacks.Count, run2.attacks.Count, "тот же сид даёт тот же AttackRecord-лог");
            for (int i = 0; i < run1.attacks.Count; i++)
            {
                Assert.AreEqual(run1.attacks[i].AttackerId, run2.attacks[i].AttackerId, $"atk#{i}");
                Assert.AreEqual(run1.attacks[i].TargetId, run2.attacks[i].TargetId, $"atk#{i}");
                Assert.AreEqual(run1.attacks[i].Chance, run2.attacks[i].Chance, $"atk#{i}");
                Assert.AreEqual(run1.attacks[i].Outcome, run2.attacks[i].Outcome, $"atk#{i}");
                Assert.AreEqual(run1.attacks[i].Damage, run2.attacks[i].Damage, $"atk#{i}");
            }
        }

        // ---- Гарантія завершуваності: запобіжник раундів ----
        [Test]
        public void Autobattle_Terminates_AsDraw_WhenNeitherSideCanDamageTheOther()
        {
            // Обидва беззбройні: ШІ ніколи не атакує (HitChancePreview=0 без зброї),
            // тільки маневрує — реальний бій без запобіжника раундів
            // ніколи б не закінчився. Balance.Combat.RoundCap зобов'язаний перервати його.
            var map = new GridMap(10, 3);
            var cs = new CombatState(map, Cfg, new ThresholdRule(Cfg), null);
            cs.AddUnit(Unit("unarmed_p", Side.Player), new GridPos(0, 1));
            cs.AddUnit(Unit("unarmed_e", Side.Enemy), new GridPos(9, 1));
            cs.Begin();

            CombatAi.AutoResolve(cs, turnBudget: 1000);

            Assert.AreEqual(CombatOutcome.Draw, cs.Outcome, "без урона с обеих сторон бой обязан завершиться Draw по RoundCap");
            Assert.Greater(cs.Round, Cfg.Combat.RoundCap, "Draw наступил именно из-за предохранителя раундов");
        }

        // ---- Гарантія завершуваності не деградує на великому ростері ----
        [Test]
        public void AutoResolve_DefaultBudget_TerminatesEvenWithLargeRoster()
        {
            // Раунд у TurnSystem — один повний прохід черги ініціативи, тобто
            // ~ЧислоЮнітів викликів TakeTurn на раунд. Зі старою фіксованою
            // страховкою (400) загін із 14 беззбройних юнітів вичерпав би її
            // ЗАДОВГО до внутрішнього RoundCap (потрібно ~14×40=560 «сирих» ходів) —
            // AutoResolve повертався б з Outcome ще Ongoing, хоча контракт
            // гарантує термінацію (BattleResult.From кидає саме на Ongoing).
            var map = new GridMap(20, 20);
            var cs = new CombatState(map, Cfg, new ThresholdRule(Cfg), null);
            for (int i = 0; i < 7; i++)
            {
                cs.AddUnit(Unit($"p{i}", Side.Player), new GridPos(0, i));
                cs.AddUnit(Unit($"e{i}", Side.Enemy), new GridPos(19, i));
            }
            cs.Begin();

            CombatAi.AutoResolve(cs); // бюджет за замовчуванням — не передаємо

            Assert.AreNotEqual(CombatOutcome.Ongoing, cs.Outcome,
                "AutoResolve обязан вернуть терминальный исход даже для большого ростера на бюджете по умолчанию");
            Assert.AreEqual(CombatOutcome.Draw, cs.Outcome, "без урона с обеих сторон исход — Draw (по RoundCap или по внешнему ForceDraw)");
        }

        // ---- Відступ ----
        [Test]
        public void Retreat_EndsCombat_MapsToBattleResultRetreat()
        {
            var map = new GridMap(6, 1);
            var cs = new CombatState(map, Cfg, new ThresholdRule(Cfg), null);
            var p = Unit("p", Side.Player);
            cs.AddUnit(p, new GridPos(0, 0));
            cs.AddUnit(Unit("e", Side.Enemy), new GridPos(5, 0));
            cs.Begin();

            Assert.AreEqual(CombatActionResult.Success, cs.Retreat());
            Assert.AreEqual(CombatOutcome.Retreat, cs.Outcome);
            Assert.AreEqual(CombatActionResult.InvalidAction, cs.Retreat(), "повторное отступление — не действие");

            var result = BattleResult.From(cs);
            Assert.AreEqual(BattleOutcome.Retreat, result.Outcome);
        }

        // ---- BattleResult не чіпає Roster, тільки читає CombatState ----
        [Test]
        public void BattleResult_From_ThrowsWhileOngoing()
        {
            var map = new GridMap(6, 1);
            var cs = new CombatState(map, Cfg, new ThresholdRule(Cfg), null);
            cs.AddUnit(Unit("p", Side.Player), new GridPos(0, 0));
            cs.AddUnit(Unit("e", Side.Enemy), new GridPos(5, 0));
            cs.Begin();

            Assert.Throws<InvalidOperationException>(() => BattleResult.From(cs));
        }

        [Test]
        public void BattleResult_From_ReportsCasualtiesOnlyForPlayerSourcedUnits()
        {
            var map = new GridMap(6, 1);
            var cs = new CombatState(map, Cfg, new ThresholdRule(Cfg), null);
            var companionUnit = CombatUnit.FromCompanion(
                Archetype("hero").CreateInstance("hero", new BalanceConfig()),
                new WeaponDefinition("w", "W", SkillType.Ranged) { DamageMin = 3, DamageMax = 3, ApCost = 2, OptimalRange = 6 },
                new BalanceConfig());
            var plainEnemy = CombatUnit.FromEnemy(DefaultCombatContent.HordeScout(), "scout_1");
            cs.AddUnit(companionUnit, new GridPos(0, 0));
            cs.AddUnit(plainEnemy, new GridPos(5, 0));
            cs.Begin();

            // Внутрішні сетери (internal) доступні тесту (InternalsVisibleTo) —
            // імітуємо шкоду обом сторонам без розіграшу повного бою, щоб
            // перевірити саме фільтр Casualties, а не баланс влучань.
            companionUnit.Hp = companionUnit.Profile.MaxHp - 4;
            plainEnemy.Hp = plainEnemy.Profile.MaxHp - 4;
            cs.Retreat();

            var result = BattleResult.From(cs);
            Assert.AreEqual(1, result.Casualties.Count, "враг без SourceCompanionId не попадает в Casualties — только напарник");
            Assert.AreEqual("hero", result.Casualties[0].CompanionId);
            Assert.AreEqual(4, result.Casualties[0].HpLost);
        }

        private static CompanionArchetype Archetype(string id) => new CompanionArchetype(id, "Тест")
            .SetAttribute(AttributeType.Strength, 5).SetAttribute(AttributeType.Agility, 5)
            .SetAttribute(AttributeType.Wits, 5).SetAttribute(AttributeType.Will, 5)
            .SetSkill(SkillType.Ranged, 3);

        // ---- Тренувальний бій ----
        [Test]
        public void Training_Threshold_BuildsPlayableBattle_AndTerminates()
        {
            var cs = DefaultCombatContent.Training(hitRule: HitRuleKind.Threshold);
            Assert.AreEqual(4, cs.Units.Count);
            CombatAi.AutoResolve(cs, 400);
            Assert.AreNotEqual(CombatOutcome.Ongoing, cs.Outcome);
        }

        [Test]
        public void Training_Percent_RequiresExternalRoller()
        {
            Assert.Throws<ArgumentException>(() => DefaultCombatContent.Training(hitRule: HitRuleKind.Percent));
            Assert.DoesNotThrow(() => DefaultCombatContent.Training(hitRule: HitRuleKind.Percent, roller: new SeededDiceRoller(1)));
        }

        // ---- BattleSetup → CombatBattleBuilder → CombatState → BattleResult ----
        [Test]
        public void CombatBattleBuilder_BuildsFromSetup_AndBattleResultRoundTrips()
        {
            var cfg = new BalanceConfig();
            var hero = Archetype("hero2").CreateInstance("hero2", cfg);
            var weapon = new WeaponDefinition("w", "W", SkillType.Ranged) { DamageMin = 9, DamageMax = 9, ApCost = 2, OptimalRange = 8 };

            var setup = new BattleSetup
            {
                Width = 8,
                Height = 8,
                HitRule = HitRuleKind.Threshold,
                PlayerUnits = { new PlayerSpawn("hero2", new GridPos(0, 0)) },
                EnemyUnits = { new EnemySpawn("enemy.horde_scout", new GridPos(5, 0)) }
            };

            PlayerUnitSource ResolvePlayer(string id) => id == "hero2" ? new PlayerUnitSource(hero, weapon) : null;
            EnemyDefinition ResolveEnemy(string id) => DefaultCombatContent.EnemyCatalog().TryGetValue(id, out var d) ? d : null;

            var cs = CombatBattleBuilder.Build(setup, cfg, ResolvePlayer, ResolveEnemy, null);

            Assert.AreEqual(2, cs.Units.Count, "билдер собрал ровно тех юнитов, что перечислены в BattleSetup");
            Assert.AreEqual("u_hero2", cs.GetUnit("u_hero2")?.Id, "id напарника резолвится через переданный делегат, не через Roster");

            // Доводимо до термінального результату примусовим відступом —
            // тест перевіряє ПРОВОДКУ (BattleSetup→CombatState→BattleResult),
            // а не баланс чисел бою.
            cs.Retreat();
            var result = BattleResult.From(cs);

            Assert.AreEqual(BattleOutcome.Retreat, result.Outcome);
            Assert.AreEqual(cs.Round, result.Rounds);
            Assert.Contains("hero2", new List<string>(result.SurvivingCompanionIds));
        }
    }
}
