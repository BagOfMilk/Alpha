using Game.Core;
using Game.Core.Balance;
using Game.Core.Characters;
using Game.Core.Combat;
using Game.Core.Stats;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Способности (US-3.9/3.11): гейт скила, AP/КД, эффекты (атаки, состояния,
    /// рывок/перестановка, хил, гаджет, ловушка) и достроенные состояния
    /// Метка / Сбит-с-ног.
    /// </summary>
    public class AbilityTests
    {
        private static CombatUnit U(string id, Side side, int init, int hp = 50, int ap = 8,
                                    int acc = 99, int def = 0, int armor = 0,
                                    WeaponDefinition w = null, AbilityDefinition ability = null)
        {
            var p = new UnitProfile
            {
                DisplayName = id, MaxHp = hp, MaxAp = ap, Accuracy = acc, Defense = def,
                Initiative = init, CritChance = 0, Armor = armor, Resolve = 0
            };
            var unit = new CombatUnit(id, side, p, w);
            if (ability != null) unit.Abilities.Add(ability);
            return unit;
        }

        private static WeaponDefinition W(int damage, bool melee = false)
            => new WeaponDefinition("w", "W", melee ? SkillType.Melee : SkillType.Ranged)
            { DamageMin = damage, DamageMax = damage, CritDamageBonus = 1, ApCost = 3, OptimalRange = melee ? 1 : 12 };

        private static CombatState NewCombat(GridMap map, params int[] rng)
            => new CombatState(map, new BalanceConfig(), new ScriptedRng(rng));

        // ---- Гейт скила при сборке юнита ----
        [Test]
        public void Companion_KnowsAbilities_BySkillThreshold()
        {
            var cfg = new BalanceConfig();
            var c = new Companion("c", new AttributeBlock(3, 3, 3, 3), 4);
            c.Skills.Set(SkillType.Ranged, 3); // Очередь (1) и Подавляющий огонь (3), но не Метка (5)

            var unit = CombatUnit.FromCompanion(c, DefaultContent.Rifle(), cfg, DefaultContent.AbilityCatalog());

            Assert.IsNotNull(unit.FindAbility("burst"));
            Assert.IsNotNull(unit.FindAbility("suppressing_fire"));
            Assert.IsNull(unit.FindAbility("mark_target"));
        }

        // ---- AP и кулдаун ----
        [Test]
        public void UseAbility_PaysAp_SetsCooldown_ReadyAfterOwnTurn()
        {
            var map = new GridMap(12, 1);
            var cs = NewCombat(map);
            var hero = U("hero", Side.Player, 10, ability: DefaultContent.MarkTarget());
            var enemy = U("enemy", Side.Enemy, 0);
            cs.AddUnit(hero, new GridPos(0, 0));
            cs.AddUnit(enemy, new GridPos(5, 0));
            cs.Begin();

            Assert.AreEqual(CombatActionResult.Success, cs.UseAbility("mark_target", "enemy"));
            Assert.AreEqual(6, hero.Ap); // 8 − 2
            Assert.AreEqual(1, hero.CooldownRemaining("mark_target"));
            Assert.AreEqual(CombatActionResult.OnCooldown, cs.UseAbility("mark_target", "enemy"));

            cs.EndTurn(); // враг
            cs.EndTurn(); // снова герой: тик КД в начале своего хода
            Assert.AreEqual(0, hero.CooldownRemaining("mark_target"));
            Assert.AreEqual(CombatActionResult.Success, cs.UseAbility("mark_target", "enemy"));
        }

        [Test]
        public void UseAbility_UnknownOrTooExpensive_Rejected()
        {
            var map = new GridMap(8, 1);
            var cs = NewCombat(map);
            var hero = U("hero", Side.Player, 10, ap: 1, ability: DefaultContent.MarkTarget());
            cs.AddUnit(hero, new GridPos(0, 0));
            cs.AddUnit(U("enemy", Side.Enemy, 0), new GridPos(5, 0));
            cs.Begin();

            Assert.AreEqual(CombatActionResult.InvalidAction, cs.UseAbility("ghost", "enemy"));
            Assert.AreEqual(CombatActionResult.NotEnoughAp, cs.UseAbility("mark_target", "enemy")); // 1 AP < 2
        }

        // ---- Стрелковое ----
        [Test]
        public void Burst_RollsTwoAttacks()
        {
            var map = new GridMap(12, 1);
            var cs = NewCombat(map, 1, 100, 1, 1, 100, 1); // два полных попадания по 1
            var hero = U("hero", Side.Player, 10, w: W(1), ability: DefaultContent.Burst());
            var enemy = U("enemy", Side.Enemy, 0);
            cs.AddUnit(hero, new GridPos(0, 0));
            cs.AddUnit(enemy, new GridPos(5, 0));
            cs.Begin();

            Assert.AreEqual(CombatActionResult.Success, cs.UseAbility("burst", "enemy"));
            Assert.AreEqual(48, enemy.Hp); // 50 − 1 − 1
            Assert.AreEqual(2, hero.Ap);   // 8 − 6
            Assert.AreEqual(2, hero.StrikeMeter, "оба попадания копят Strike");
        }

        [Test]
        public void Burst_PreviewChance_MatchesActualRoll()
        {
            var map = new GridMap(12, 1);
            var cs = NewCombat(map, 1, 100, 1, 1, 100, 1);
            var burst = DefaultContent.Burst();
            var hero = U("hero", Side.Player, 10, acc: 70, w: W(1), ability: burst);
            var enemy = U("enemy", Side.Enemy, 0);
            cs.AddUnit(hero, new GridPos(0, 0));
            cs.AddUnit(enemy, new GridPos(5, 0));
            cs.Begin();

            // Бонус ОДНОГО ролла, а не сумма по залпу: «Черга» — два выстрела по −10,
            // и показанные игроку 50% расходились бы с реальными 60% на каждом.
            Assert.AreEqual(2, burst.WeaponAttackCount(), "«Черга» делает два отдельных ролла");
            Assert.AreEqual(-10, burst.PreviewAccuracyBonus());

            int preview = cs.HitChancePreview(hero, enemy, burst.PreviewAccuracyBonus());
            Assert.AreEqual(CombatActionResult.Success, cs.UseAbility("burst", "enemy"));

            Assert.AreEqual(2, cs.Attacks.Count);
            Assert.AreEqual(preview, cs.Attacks[0].Chance, "показанный процент = тот, против которого катится d100 (US-3.3, R11)");
            Assert.AreEqual(preview, cs.Attacks[1].Chance);
        }

        [Test]
        public void SuppressingFire_StatusGuaranteed_EvenOnMiss()
        {
            var map = new GridMap(12, 1);
            var cs = NewCombat(map, 100); // ролл 100 при низком шансе → чистый промах
            var hero = U("hero", Side.Player, 10, acc: 30, w: W(2), ability: DefaultContent.SuppressingFire());
            var enemy = U("enemy", Side.Enemy, 0);
            cs.AddUnit(hero, new GridPos(0, 0));
            cs.AddUnit(enemy, new GridPos(5, 0));
            cs.Begin();

            Assert.AreEqual(CombatActionResult.Success, cs.UseAbility("suppressing_fire", "enemy"));
            Assert.AreEqual(50, enemy.Hp, "промах — урона нет");
            Assert.IsTrue(enemy.HasStatus(StatusType.Suppressed), "наложение гарантированное (US-3.7)");
        }

        [Test]
        public void Marked_RaisesHitChance_LowLevelFormula()
        {
            var cfg = new BalanceConfig();
            int plain = HitChanceCalculator.Compute(70, false, 0, CoverType.None, false, 3, 6, cfg);
            int marked = HitChanceCalculator.Compute(70, false, 0, CoverType.None, false, 3, 6, cfg, targetMarked: true);
            Assert.AreEqual(plain + cfg.MarkedHitBonus, marked);
        }

        // ---- Ближнее ----
        [Test]
        public void Lunge_DashesAdjacent_AndStrikes()
        {
            var map = new GridMap(12, 1);
            var cs = NewCombat(map, 1, 100, 3);
            var hero = U("hero", Side.Player, 10, w: W(3, melee: true), ability: DefaultContent.Lunge());
            var enemy = U("enemy", Side.Enemy, 0);
            cs.AddUnit(hero, new GridPos(0, 0));
            cs.AddUnit(enemy, new GridPos(3, 0)); // дистанция 3 ≤ 4
            cs.Begin();

            Assert.AreEqual(CombatActionResult.Success, cs.UseAbility("lunge", "enemy"));
            Assert.AreEqual(1, GridPos.Chebyshev(hero.Pos, enemy.Pos), "встал вплотную");
            Assert.AreEqual("hero", map.OccupantAt(hero.Pos));
            Assert.AreEqual(47, enemy.Hp); // удар после рывка
        }

        [Test]
        public void TripStrike_Knockdown_EasierToHit_StandUpCostsAp()
        {
            var cfg = new BalanceConfig();
            var map = new GridMap(12, 1);
            var cs = new CombatState(map, cfg, new ScriptedRng(1, 100, 3));
            var hero = U("hero", Side.Player, 10, w: W(3, melee: true), ability: DefaultContent.TripStrike());
            var victim = U("victim", Side.Enemy, 5, def: 5);
            cs.AddUnit(hero, new GridPos(0, 0));
            cs.AddUnit(victim, new GridPos(1, 0));
            cs.Begin();

            int chanceBefore = cs.HitChancePreview(hero, victim);
            Assert.AreEqual(CombatActionResult.Success, cs.UseAbility("trip_strike", "victim"));
            Assert.IsTrue(victim.HasStatus(StatusType.KnockedDown));
            Assert.Greater(cs.HitChancePreview(hero, victim), chanceBefore, "сбитый — лёгкая цель (−защита)");

            cs.EndTurn(); // ход victim: встаёт за AP
            Assert.AreSame(victim, cs.Current);
            Assert.IsFalse(victim.HasStatus(StatusType.KnockedDown));
            Assert.AreEqual(8 - cfg.StandUpApCost, victim.Ap);
        }

        // ---- Тактика ----
        [Test]
        public void Rally_GrantsApToAlly()
        {
            var map = new GridMap(12, 1);
            var cs = NewCombat(map);
            var leader = U("leader", Side.Player, 10, ability: DefaultContent.Rally());
            var ally = U("ally", Side.Player, 5);
            cs.AddUnit(leader, new GridPos(0, 0));
            cs.AddUnit(ally, new GridPos(1, 0));
            cs.AddUnit(U("enemy", Side.Enemy, 0), new GridPos(11, 0));
            cs.Begin();

            // Самоцель проверяем ДО применения: после него сработал бы кулдаун (OnCooldown).
            Assert.AreEqual(CombatActionResult.InvalidTarget, cs.UseAbility("rally", "leader"), "Ally ≠ сам кастер");

            int before = ally.Ap;
            Assert.AreEqual(CombatActionResult.Success, cs.UseAbility("rally", "ally"));
            Assert.AreEqual(before + 4, ally.Ap);
        }

        [Test]
        public void MoveOrder_RepositionsAlly_WithinLimit()
        {
            var map = new GridMap(12, 1);
            var cs = NewCombat(map);
            var leader = U("leader", Side.Player, 10, ability: DefaultContent.MoveOrder());
            var ally = U("ally", Side.Player, 5);
            cs.AddUnit(leader, new GridPos(0, 0));
            cs.AddUnit(ally, new GridPos(1, 0));
            cs.AddUnit(U("enemy", Side.Enemy, 0), new GridPos(11, 0));
            cs.Begin();

            Assert.AreEqual(CombatActionResult.NotReachable,
                cs.UseAbility("move_order", "ally", new GridPos(5, 0)), "дальше лимита перестановки (3)");

            Assert.AreEqual(CombatActionResult.Success,
                cs.UseAbility("move_order", "ally", new GridPos(4, 0)));
            Assert.AreEqual(new GridPos(4, 0), ally.Pos);
            Assert.AreEqual("ally", map.OccupantAt(new GridPos(4, 0)));
            Assert.IsNull(map.OccupantAt(new GridPos(1, 0)));
        }

        [Test]
        public void SnapOut_RemovesControlStatuses()
        {
            var map = new GridMap(12, 1);
            var cs = NewCombat(map);
            var leader = U("leader", Side.Player, 10, ability: DefaultContent.SnapOut());
            var ally = U("ally", Side.Player, 5);
            cs.AddUnit(leader, new GridPos(0, 0));
            cs.AddUnit(ally, new GridPos(1, 0));
            cs.AddUnit(U("enemy", Side.Enemy, 0), new GridPos(11, 0));
            cs.Begin();

            cs.ApplyStatus(ally, StatusType.Suppressed);
            cs.ApplyStatus(ally, StatusType.Marked);
            Assert.AreEqual(CombatActionResult.Success, cs.UseAbility("snap_out", "ally"));
            Assert.IsFalse(ally.HasStatus(StatusType.Suppressed));
            Assert.IsFalse(ally.HasStatus(StatusType.Marked));
        }

        // ---- Утилита ----
        [Test]
        public void FieldDressing_HealsSelfOrAlly_CappedAtMax()
        {
            var map = new GridMap(12, 1);
            var cs = NewCombat(map);
            var medic = U("medic", Side.Player, 10, hp: 10, ability: DefaultContent.FieldDressing());
            cs.AddUnit(medic, new GridPos(0, 0));
            cs.AddUnit(U("enemy", Side.Enemy, 0), new GridPos(11, 0));
            medic.Hp = 8;
            cs.Begin();

            Assert.AreEqual(CombatActionResult.Success, cs.UseAbility("field_dressing")); // на себя
            Assert.AreEqual(10, medic.Hp, "хил капается максимумом HP");
        }

        [Test]
        public void ShockCharge_FlatDamage_RespectsArmor_AndShreds()
        {
            var map = new GridMap(12, 1);
            var cs = NewCombat(map);
            var tech = U("tech", Side.Player, 10, ability: DefaultContent.ShockCharge());
            var bot = U("bot", Side.Enemy, 0, armor: 1);
            cs.AddUnit(tech, new GridPos(0, 0));
            cs.AddUnit(bot, new GridPos(3, 0));
            cs.Begin();

            Assert.AreEqual(CombatActionResult.Success, cs.UseAbility("shock_charge", "bot"));
            Assert.AreEqual(48, bot.Hp); // 3 (без резистов) − броня 1 = 2
            Assert.AreEqual(1, bot.ArmorShred);
            Assert.AreEqual(0, bot.EffectiveArmor);
        }

        [Test]
        public void FlatDamage_AppliesTypeMultiplier()
        {
            var vulnerable = U("v", Side.Enemy, 0);
            vulnerable.Profile.Resists.With(DamageType.Energy, 1.5);
            Assert.AreEqual(6, DamageResolver.FlatDamage(4, DamageType.Energy, vulnerable)); // 4×1.5, без брони
        }

        [Test]
        public void Trap_Placed_TriggersOnEnemyEntering()
        {
            var map = new GridMap(12, 1);
            var cs = NewCombat(map);
            var scout = U("scout", Side.Player, 10, ability: DefaultContent.SetTrap());
            var enemy = U("enemy", Side.Enemy, 5);
            cs.AddUnit(scout, new GridPos(0, 0));
            cs.AddUnit(enemy, new GridPos(3, 0)); // в пределах дальности ловушки (3)
            cs.Begin();

            Assert.AreEqual(CombatActionResult.InvalidTarget,
                cs.UseAbility("set_trap", targetTile: new GridPos(3, 0)), "занятый тайл — нельзя");
            Assert.AreEqual(CombatActionResult.Success,
                cs.UseAbility("set_trap", targetTile: new GridPos(2, 0)));
            Assert.AreEqual(1, cs.Traps.Count);

            cs.EndTurn(); // ход врага
            Assert.AreSame(enemy, cs.Current);
            Assert.AreEqual(CombatActionResult.Success, cs.Move(new GridPos(2, 0)));

            Assert.AreEqual(47, enemy.Hp, "ловушка: 3 True-урона");
            Assert.IsTrue(enemy.HasStatus(StatusType.Bleeding));
            Assert.AreEqual(0, cs.Traps.Count, "одноразовая");
        }

        // ---- Взлом робота (US-3.11/3.14): переманить на свою сторону ----
        [Test]
        public void HackRobot_ConvertsRobot_RejectsFlesh()
        {
            var map = new GridMap(12, 1);
            var cs = NewCombat(map);
            var hacker = U("hacker", Side.Player, 10, ability: DefaultContent.HackDrone());
            var drone = CombatUnit.FromEnemy(DefaultContent.RustDrone(), "drone");
            var raider = CombatUnit.FromEnemy(DefaultContent.RaiderBruiser(), "raider");
            cs.AddUnit(hacker, new GridPos(0, 0));
            cs.AddUnit(drone, new GridPos(3, 0));
            cs.AddUnit(raider, new GridPos(4, 0));
            cs.Begin();

            Assert.AreEqual(CombatActionResult.InvalidTarget, cs.UseAbility("hack_drone", "raider"),
                "взлом берёт только роботов (валидация ДО оплаты)");
            Assert.AreEqual(8, hacker.Ap, "AP не потрачены на невалидную цель");

            Assert.AreEqual(CombatActionResult.Success, cs.UseAbility("hack_drone", "drone"));
            Assert.AreEqual(Side.Player, drone.Side, "дрон переманен — дерётся за отряд");
            Assert.AreEqual(CombatOutcome.Ongoing, cs.Outcome, "рейдер ещё стоит");
        }

        [Test]
        public void HackRobot_LastEnemy_EndsCombat()
        {
            var map = new GridMap(12, 1);
            var cs = NewCombat(map);
            var hacker = U("hacker", Side.Player, 10, ability: DefaultContent.HackDrone());
            var drone = CombatUnit.FromEnemy(DefaultContent.RustDrone(), "drone");
            cs.AddUnit(hacker, new GridPos(0, 0));
            cs.AddUnit(drone, new GridPos(3, 0));
            cs.Begin();

            Assert.AreEqual(CombatActionResult.Success, cs.UseAbility("hack_drone", "drone"));
            Assert.AreEqual(CombatOutcome.Victory, cs.Outcome, "последний враг переманен — бой окончен");
        }
    }
}
