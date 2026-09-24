using Game.Core.Balance;
using Game.Core.Combat;
using Game.Core.Stats;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// CombatState: действия (движение/атака/стабилизация), даун с окном на
    /// спасение, Strike-метр, проки оружия, исход боя. Перенесено из архивной
    /// боевой линии (коммит 20b8dcf), адаптировано на IHitRule/IDiceRoller (R1):
    /// вместо d100-роллов через IRng сценарии собираются через ThresholdRule —
    /// «показанное число» (Accuracy минус Defense — остальные слагаемые формулы
    /// в этих тестах нулевые) кладётся прямо в нужную полосу исхода по марже
    /// от CombatBalance.ThresholdBaseline/GrazeBand/CritBand.
    /// </summary>
    public class CombatStateTests
    {
        private static readonly BalanceConfig Cfg = new BalanceConfig();

        // Полосы по умолчанию: Baseline=50, GrazeBand=15, CritBand=35.
        private const int MissAcc = 40;  // margin −10 → Miss
        private const int HitAcc = 70;   // margin 20  → Hit (не Crit)
        private const int CritAcc = 90;  // margin 40  → Crit

        private static CombatUnit U(string id, Side side, int init, int hp = 10, int ap = 8,
                                    int acc = HitAcc, int armor = 0, bool downable = false,
                                    int medicine = 0, WeaponDefinition w = null)
        {
            var p = new UnitProfile
            {
                DisplayName = id, MaxHp = hp, MaxAp = ap, Accuracy = acc, Defense = 0,
                Initiative = init, CritChance = 0, Armor = armor, Resolve = 0,
                MedicineSkill = medicine, CanBeDowned = downable, MoveApPerTile = 1
            };
            return new CombatUnit(id, side, p, w);
        }

        /// <summary>Оружие с фикс уроном (min = max) для детерминизма — тот же приём, что в архиве.</summary>
        private static WeaponDefinition W(int damage, int apCost = 3, bool melee = false, int range = 6)
            => new WeaponDefinition("w", "W", melee ? SkillType.Melee : SkillType.Ranged)
            {
                DamageMin = damage, DamageMax = damage, CritDamageBonus = 1,
                ApCost = apCost, OptimalRange = melee ? 1 : range
            };

        private static CombatState NewCombat(GridMap map)
            => new CombatState(map, Cfg, new ThresholdRule(Cfg), null);

        // ---- Движение ----
        [Test]
        public void Move_SpendsAp_AndUpdatesOccupancy()
        {
            var map = new GridMap(12, 1);
            var cs = NewCombat(map);
            var a = U("a", Side.Player, 10);
            cs.AddUnit(a, new GridPos(0, 0));
            cs.AddUnit(U("e", Side.Enemy, 0), new GridPos(11, 0));
            cs.Begin();

            Assert.AreEqual(CombatActionResult.Success, cs.Move(new GridPos(3, 0)));
            Assert.AreEqual(5, a.Ap); // 8 − 3
            Assert.AreEqual("a", map.OccupantAt(new GridPos(3, 0)));
            Assert.IsNull(map.OccupantAt(new GridPos(0, 0)));

            Assert.AreEqual(CombatActionResult.NotReachable, cs.Move(new GridPos(9, 0))); // 6 > 5 AP
        }

        // ---- Валидации атаки ----
        [Test]
        public void Attack_Validations()
        {
            var map = new GridMap(12, 1);
            map.SetWall(new GridPos(6, 0));
            var cs = NewCombat(map);
            var lowAp = U("lowap", Side.Player, 20, ap: 2, w: W(3, apCost: 3));
            var brawler = U("brawler", Side.Player, 15, w: W(3, melee: true));
            var walled = U("walled", Side.Player, 10, w: W(3));
            var enemy = U("enemy", Side.Enemy, 5, hp: 50, w: W(3));
            cs.AddUnit(lowAp, new GridPos(0, 0));
            cs.AddUnit(brawler, new GridPos(1, 0));
            cs.AddUnit(walled, new GridPos(4, 0));
            cs.AddUnit(enemy, new GridPos(9, 0)); // за стеной (6,0)
            cs.Begin();

            Assert.AreEqual(CombatActionResult.NotEnoughAp, cs.Attack("enemy"));      // 2 AP < 3
            Assert.AreEqual(CombatActionResult.InvalidTarget, cs.Attack("brawler"));  // союзник
            cs.EndTurn();

            Assert.AreSame(brawler, cs.Current);
            Assert.AreEqual(CombatActionResult.OutOfRange, cs.Attack("enemy"));       // мили издалека
            cs.EndTurn();

            Assert.AreSame(walled, cs.Current);
            Assert.AreEqual(CombatActionResult.NoLineOfSight, cs.Attack("enemy"));    // стена рвёт LOS
        }

        // ---- Даун, окно, стабилизация ----
        private static (CombatState cs, CombatUnit victim, CombatUnit medic) DownScenario()
        {
            var map = new GridMap(12, 1);
            var cs = NewCombat(map);
            var enemy = U("enemy", Side.Enemy, 10, hp: 50, acc: HitAcc, w: W(5, apCost: 3, range: 12));
            var victim = U("victim", Side.Player, 5, hp: 3, downable: true);
            var medic = U("medic", Side.Player, 1, medicine: 3);
            cs.AddUnit(enemy, new GridPos(0, 0));
            cs.AddUnit(victim, new GridPos(5, 0));
            cs.AddUnit(medic, new GridPos(6, 0)); // вплотную к victim
            cs.Begin(); // ход врага

            Assert.AreEqual(CombatActionResult.Success, cs.Attack("victim"));
            return (cs, victim, medic);
        }

        [Test]
        public void Companion_DownsAt0Hp_WithRescueWindow()
        {
            var (_, victim, _) = DownScenario();
            Assert.AreEqual(UnitLifeState.Downed, victim.LifeState);
            Assert.AreEqual(0, victim.Hp);
            Assert.AreEqual(Cfg.Combat.DownWindowTurns, victim.DownWindowRemaining);
        }

        [Test]
        public void Downed_Stabilized_ByAdjacentMedic()
        {
            var (cs, victim, medic) = DownScenario();
            cs.EndTurn(); // враг → victim пропускает (окно 2→1) → ход медика
            Assert.AreSame(medic, cs.Current);

            Assert.AreEqual(CombatActionResult.Success, cs.Stabilize("victim"));
            Assert.AreEqual(UnitLifeState.Stabilized, victim.LifeState);
            Assert.AreEqual(8 - cs.Balance.Combat.StabilizeApCost, medic.Ap);
            Assert.AreEqual(CombatOutcome.Ongoing, cs.Outcome);
        }

        [Test]
        public void Downed_Dies_WhenWindowExpires()
        {
            var (cs, victim, _) = DownScenario();
            cs.EndTurn(); // victim: окно 2→1, ход медика
            cs.EndTurn(); // медик ничего не делает; раунд 2: ход врага
            cs.EndTurn(); // враг пас; victim: окно 1→0 → смерть
            Assert.AreEqual(UnitLifeState.Dead, victim.LifeState);
            Assert.AreEqual(CombatOutcome.Ongoing, cs.Outcome); // медик ещё стоит
        }

        [Test]
        public void Stabilize_RequiresMedicineSkill()
        {
            var map = new GridMap(5, 1);
            var cs = NewCombat(map);
            var noMedic = U("nomedic", Side.Player, 10, medicine: 0);
            var downed = U("downed", Side.Player, 5, downable: true);
            cs.AddUnit(noMedic, new GridPos(0, 0));
            cs.AddUnit(downed, new GridPos(1, 0));
            cs.AddUnit(U("e", Side.Enemy, 0), new GridPos(4, 0));
            downed.Hp = 0;
            downed.LifeState = UnitLifeState.Downed;
            downed.DownWindowRemaining = 2;
            cs.Begin();

            Assert.AreEqual(CombatActionResult.InvalidAction, cs.Stabilize("downed"));
        }

        // ---- Смерть врага и исход ----
        [Test]
        public void Enemy_DiesImmediately_VictoryEndsCombat()
        {
            var map = new GridMap(8, 1);
            var cs = NewCombat(map);
            var hero = U("hero", Side.Player, 10, acc: HitAcc, w: W(5, range: 8));
            var lastEnemy = U("enemy", Side.Enemy, 5, hp: 3);
            cs.AddUnit(hero, new GridPos(0, 0));
            cs.AddUnit(lastEnemy, new GridPos(5, 0));
            cs.Begin();

            Assert.AreEqual(CombatActionResult.Success, cs.Attack("enemy"));
            Assert.AreEqual(UnitLifeState.Dead, lastEnemy.LifeState);
            Assert.AreEqual(CombatOutcome.Victory, cs.Outcome);
            Assert.AreEqual(CombatActionResult.InvalidAction, cs.Move(new GridPos(1, 0)));
        }

        // ---- Strike-метр ----
        [Test]
        public void Strike_AccumulatesOnHits_SpendGuaranteesAndResets()
        {
            var map = new GridMap(8, 1);
            var cs = NewCombat(map);
            var hero = U("hero", Side.Player, 10, acc: HitAcc, w: W(1, apCost: 2, range: 8));
            var bag = U("bag", Side.Enemy, 5, hp: 50);
            cs.AddUnit(hero, new GridPos(0, 0));
            cs.AddUnit(bag, new GridPos(5, 0));
            cs.Begin();

            cs.Attack("bag");
            cs.Attack("bag");
            cs.Attack("bag");
            Assert.AreEqual(3, hero.StrikeMeter);
            Assert.AreEqual(47, bag.Hp);

            cs.EndTurn(); // ход мешка
            cs.EndTurn(); // обратно к герою (AP восстановлены)

            Assert.AreEqual(CombatActionResult.Success, cs.Attack("bag", useStrike: true));
            Assert.AreEqual(0, hero.StrikeMeter);
            Assert.AreEqual(46, bag.Hp);
        }

        [Test]
        public void Strike_RequiresFullMeter()
        {
            var map = new GridMap(8, 1);
            var cs = NewCombat(map);
            var hero = U("hero", Side.Player, 10, w: W(1, apCost: 2, range: 8));
            cs.AddUnit(hero, new GridPos(0, 0));
            cs.AddUnit(U("bag", Side.Enemy, 5, hp: 50), new GridPos(5, 0));
            cs.Begin();

            Assert.AreEqual(CombatActionResult.InvalidAction, cs.Attack("bag", useStrike: true));
            Assert.AreEqual(8, hero.Ap, "AP не списывается за невалидную strike-атаку");
        }

        // ---- Проки оружия ----
        [Test]
        public void WeaponProcs_ShredAndStatus_OnFullHit()
        {
            var map = new GridMap(8, 1);
            var cs = NewCombat(map);
            var w = W(1, apCost: 3, range: 8);
            w.ShredOnHit = 1;
            w.StatusOnHit = StatusType.Bleeding;
            var hero = U("hero", Side.Player, 10, acc: HitAcc, w: w);
            var armored = U("armored", Side.Enemy, 5, hp: 30, armor: 2);
            cs.AddUnit(hero, new GridPos(0, 0));
            cs.AddUnit(armored, new GridPos(5, 0));
            cs.Begin();

            cs.Attack("armored");
            Assert.AreEqual(1, armored.ArmorShred);
            Assert.AreEqual(1, armored.EffectiveArmor); // 2 − 1
            Assert.IsTrue(armored.HasStatus(StatusType.Bleeding));
        }

        [Test]
        public void Graze_DealsNoProcs_HalfDamage()
        {
            var map = new GridMap(8, 1);
            var cs = NewCombat(map);
            var w = W(2, apCost: 3, range: 8);
            w.StatusOnHit = StatusType.Bleeding;
            var hero = U("hero", Side.Player, 10, acc: 50, w: w); // margin 0 → Graze (в полосе [0,15))
            var target = U("target", Side.Enemy, 5, hp: 30);
            cs.AddUnit(hero, new GridPos(0, 0));
            cs.AddUnit(target, new GridPos(5, 0));
            cs.Begin();

            cs.Attack("target");
            Assert.AreEqual(29, target.Hp, "граза — половина урона (2×50%=1)");
            Assert.IsFalse(target.HasStatus(StatusType.Bleeding), "граза не триггерит проки оружия");
        }

        // ---- Телеметрия атак ----
        [Test]
        public void AttackHistory_RecordsChanceOutcomeAndDamage()
        {
            var map = new GridMap(8, 1);
            var cs = NewCombat(map);
            var hero = U("hero", Side.Player, 10, acc: HitAcc, w: W(1, apCost: 2, range: 8));
            cs.AddUnit(hero, new GridPos(0, 0));
            cs.AddUnit(U("bag", Side.Enemy, 5, hp: 50), new GridPos(5, 0));
            cs.Begin();

            cs.Attack("bag");
            Assert.AreEqual(1, cs.Attacks.Count, "атака записана в историю");
            var rec = cs.Attacks[0];
            Assert.AreEqual("hero", rec.AttackerId);
            Assert.AreEqual("bag", rec.TargetId);
            Assert.AreEqual(Side.Player, rec.AttackerSide);
            Assert.AreEqual(AttackOutcome.Hit, rec.Outcome);
            Assert.AreEqual(1, rec.Damage);
            Assert.IsFalse(rec.Forced);
            Assert.Greater(rec.Chance, 0, "показанное число зафиксировано");

            hero.StrikeMeter = cs.Balance.Combat.StrikeGuaranteeAt;
            cs.Attack("bag", useStrike: true);
            Assert.AreEqual(2, cs.Attacks.Count);
            Assert.IsTrue(cs.Attacks[1].Forced);
        }

        [Test]
        public void CritBand_ProducesCritOutcome()
        {
            var map = new GridMap(8, 1);
            var cs = NewCombat(map);
            var hero = U("hero", Side.Player, 10, acc: CritAcc, w: W(4, apCost: 2, range: 8, melee: false));
            cs.AddUnit(hero, new GridPos(0, 0));
            cs.AddUnit(U("bag", Side.Enemy, 5, hp: 50), new GridPos(5, 0));
            cs.Begin();

            cs.Attack("bag");
            Assert.AreEqual(AttackOutcome.Crit, cs.Attacks[0].Outcome);
        }
    }
}
