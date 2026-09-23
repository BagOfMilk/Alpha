using System.Linq;
using Game.Core.Balance;
using Game.Core.Combat;
using Game.Core.Stats;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Overwatch (US-3.6): «вход в overwatch резервирует AP; срабатывает один раз
    /// при входе врага в конус до следующего хода юнита; без двойного профита».
    ///
    /// Геометрия сценариев: поле 10×5, дозорный в (0,2) смотрит вдоль оси X на
    /// (9,2); конус 90° (tg полуширины = 1/1). Враг идёт по нижнему ряду y=4 из
    /// (0,4): клетка (1,4) вне конуса, (2,4) — ровно на его краю, дальше — внутри.
    /// </summary>
    public class CombatOverwatchTests
    {
        private static CombatUnit U(string id, Side side, int init, int hp = 50, int ap = 8,
                                    int acc = 99, WeaponDefinition w = null)
        {
            var p = new UnitProfile
            {
                DisplayName = id, MaxHp = hp, MaxAp = ap, Accuracy = acc, Defense = 0,
                Initiative = init, CritChance = 0, Armor = 0, Resolve = 0,
                MedicineSkill = 0, CanBeDowned = false
            };
            return new CombatUnit(id, side, p, w);
        }

        /// <summary>Оружие с фиксированным уроном: пустой ScriptedRng отдаёт середину диапазона.</summary>
        private static WeaponDefinition W(int damage = 3, int apCost = 3, bool melee = false)
            => new WeaponDefinition("w", "W", melee ? SkillType.Melee : SkillType.Ranged)
            {
                DamageMin = damage, DamageMax = damage, CritDamageBonus = 1,
                ApCost = apCost, OptimalRange = melee ? 1 : 6
            };

        private static CombatState NewCombat(GridMap map, params int[] rng)
            => new CombatState(map, new BalanceConfig(), new ScriptedRng(rng));

        /// <summary>Дозорный в (0,2) и враг в enemyAt; ход у дозорного.</summary>
        private static (CombatState cs, CombatUnit watcher, CombatUnit enemy) Scene(
            GridPos enemyAt, int enemyHp = 50, GridMap map = null, WeaponDefinition watcherWeapon = null,
            GridPos? watcherAt = null)
        {
            map = map ?? new GridMap(10, 5);
            var cs = NewCombat(map);
            var watcher = U("watcher", Side.Player, 20, w: watcherWeapon ?? W());
            var enemy = U("enemy", Side.Enemy, 10, hp: enemyHp, w: W());
            cs.AddUnit(watcher, watcherAt ?? new GridPos(0, 2));
            cs.AddUnit(enemy, enemyAt);
            cs.Begin();
            return (cs, watcher, enemy);
        }

        private static int ShotsBy(CombatState cs, string attackerId)
            => cs.Attacks.Count(a => a.AttackerId == attackerId);

        // ---- Вход в дозор ----

        [Test]
        public void Overwatch_ReservesWeaponAp_StoresStance_AndEndsTurn()
        {
            var (cs, watcher, enemy) = Scene(new GridPos(8, 4));

            Assert.AreEqual(CombatActionResult.Success, cs.Overwatch(new GridPos(9, 2)));

            Assert.AreEqual(5, watcher.Ap, "резерв = цена выстрела оружием (8 − 3)");
            Assert.IsTrue(watcher.IsOverwatching);
            Assert.AreEqual(new GridPos(0, 2), watcher.Overwatch.Origin);
            Assert.AreEqual(new GridPos(9, 2), watcher.Overwatch.Aim);
            Assert.AreEqual(3, watcher.Overwatch.ReservedAp);
            Assert.AreSame(enemy, cs.Current, "вход в дозор завершает ход");
        }

        [Test]
        public void Overwatch_Validations_DoNotSpendApOrEndTurn()
        {
            var map = new GridMap(10, 5);
            var cs = NewCombat(map);
            var unarmed = U("unarmed", Side.Player, 30);
            var poor = U("poor", Side.Player, 20, ap: 2, w: W(apCost: 3));
            cs.AddUnit(unarmed, new GridPos(0, 0));
            cs.AddUnit(poor, new GridPos(0, 2));
            cs.AddUnit(U("enemy", Side.Enemy, 10, w: W()), new GridPos(9, 4));
            cs.Begin();

            Assert.AreEqual(CombatActionResult.InvalidAction, cs.Overwatch(new GridPos(5, 0)), "без оружия");
            Assert.AreSame(unarmed, cs.Current);
            cs.EndTurn();

            Assert.AreEqual(CombatActionResult.InvalidTarget, cs.Overwatch(new GridPos(0, 2)), "прицел в себя");
            Assert.AreEqual(CombatActionResult.InvalidTarget, cs.Overwatch(new GridPos(20, 2)), "прицел за картой");
            Assert.AreEqual(CombatActionResult.NotEnoughAp, cs.Overwatch(new GridPos(9, 2)), "2 AP < 3");
            Assert.AreEqual(2, poor.Ap);
            Assert.IsFalse(poor.IsOverwatching);
            Assert.AreSame(poor, cs.Current);
        }

        // ---- Срабатывание ----

        [Test]
        public void Overwatch_FiresOnce_OnFirstStepIntoSector_AndMoverKeepsWalking()
        {
            var (cs, watcher, enemy) = Scene(new GridPos(0, 4));
            cs.Overwatch(new GridPos(9, 2));

            // Путь (1,4)→(2,4)→(3,4)→(4,4): выстрел на (2,4), дальше — ни одного.
            Assert.AreEqual(CombatActionResult.Success, cs.Move(new GridPos(4, 4)));

            Assert.AreEqual(1, ShotsBy(cs, "watcher"), "одно срабатывание, хотя в секторе три шага");
            Assert.AreEqual(47, enemy.Hp, "попадание на 3");
            Assert.AreEqual(new GridPos(4, 4), enemy.Pos, "выживший идёт дальше");
            Assert.IsFalse(watcher.IsOverwatching, "выстрел снимает дозор");
        }

        [Test]
        public void Overwatch_KillStopsTheMover_WhereHeFell()
        {
            var (cs, _, enemy) = Scene(new GridPos(0, 4), enemyHp: 2);
            cs.Overwatch(new GridPos(9, 2));

            cs.Move(new GridPos(4, 4));

            Assert.AreEqual(UnitLifeState.Dead, enemy.LifeState);
            Assert.AreEqual(new GridPos(2, 4), enemy.Pos, "упал на первом шаге в секторе, а не в точке прибытия");
            Assert.AreEqual(CombatOutcome.Victory, cs.Outcome);
        }

        [Test]
        public void Overwatch_NeverFiresOnAllies_WalkingThroughSector()
        {
            var map = new GridMap(10, 5);
            var cs = NewCombat(map);
            var watcher = U("watcher", Side.Player, 30, w: W());
            var ally = U("ally", Side.Player, 20, w: W());
            cs.AddUnit(watcher, new GridPos(0, 2));
            cs.AddUnit(ally, new GridPos(0, 4));
            cs.AddUnit(U("enemy", Side.Enemy, 10, w: W()), new GridPos(9, 0));
            cs.Begin();
            cs.Overwatch(new GridPos(9, 2));

            Assert.AreSame(ally, cs.Current);
            cs.Move(new GridPos(4, 4)); // тот же путь по сектору, что у врага в сценариях выше

            Assert.AreEqual(0, ShotsBy(cs, "watcher"), "по своим дозор не стреляет");
            Assert.AreEqual(50, ally.Hp);
            Assert.IsTrue(watcher.IsOverwatching, "и не тратится на них");
        }

        [Test]
        public void Overwatch_IgnoresMovesOutsideSector_AndKeepsStance()
        {
            // Дозорный в (4,2) смотрит вправо; враг ходит у него за спиной по x=0.
            var (cs, watcher, _) = Scene(new GridPos(0, 0), watcherAt: new GridPos(4, 2));
            cs.Overwatch(new GridPos(9, 2));

            cs.Move(new GridPos(0, 4));

            Assert.AreEqual(0, ShotsBy(cs, "watcher"));
            Assert.IsTrue(watcher.IsOverwatching, "сектор никто не пересёк — дозор стоит");
        }

        [Test]
        public void Overwatch_NeedsLineOfSight_ForRangedWeapon()
        {
            // Стена x=2 (y 1..3) между дозорным и колонной x=4, которая вся в конусе.
            var map = new GridMap(10, 5);
            map.SetWall(new GridPos(2, 1));
            map.SetWall(new GridPos(2, 2));
            map.SetWall(new GridPos(2, 3));
            var (cs, watcher, _) = Scene(new GridPos(4, 0), map: map);
            cs.Overwatch(new GridPos(4, 2));

            cs.Move(new GridPos(4, 4));

            Assert.AreEqual(0, ShotsBy(cs, "watcher"), "в секторе, но за стеной — не видно, не стреляет");
            Assert.IsTrue(watcher.IsOverwatching);
        }

        [Test]
        public void Overwatch_Melee_GuardsOnlyTheDoorway()
        {
            // Ближний дозорный в (2,2) смотрит на (3,2). Враг идёт (5,3)→(4,3)→(3,3):
            // (4,3) в конусе, но не в контакте; (3,3) — в конусе и вплотную.
            // Удар смертельный — поэтому видно, НА КАКОЙ клетке он случился.
            var (cs, watcher, enemy) = Scene(new GridPos(5, 3), enemyHp: 2,
                watcherWeapon: W(melee: true), watcherAt: new GridPos(2, 2));
            cs.Overwatch(new GridPos(3, 2));

            cs.Move(new GridPos(3, 3));

            Assert.AreEqual(1, ShotsBy(cs, "watcher"));
            Assert.AreEqual(new GridPos(3, 3), enemy.Pos, "ударил у двери, а не за шаг до неё");
            Assert.AreEqual(UnitLifeState.Dead, enemy.LifeState);
            Assert.IsFalse(watcher.IsOverwatching);
        }

        [Test]
        public void Overwatch_SeesRepositionByOrder_IntoSector()
        {
            // Враг-дозорный в (9,2) смотрит влево. Командир переставляет союзника
            // «Командным рывком» из-за спины дозорного (9,4) прямо в сектор (7,4).
            var map = new GridMap(10, 5);
            var cs = NewCombat(map);
            var watcher = U("watcher", Side.Enemy, 30, w: W());
            var leader = U("leader", Side.Player, 20, w: W());
            var ally = U("ally", Side.Player, 10, w: W());
            leader.Abilities.Add(Game.Core.DefaultContent.MoveOrder());
            cs.AddUnit(watcher, new GridPos(9, 2));
            cs.AddUnit(leader, new GridPos(5, 0));
            cs.AddUnit(ally, new GridPos(9, 4));
            cs.Begin();
            cs.Overwatch(new GridPos(0, 2));

            Assert.AreEqual(CombatActionResult.Success,
                cs.UseAbility("move_order", "ally", new GridPos(7, 4)));

            Assert.AreEqual(1, ShotsBy(cs, "watcher"), "перестановка по приказу — тоже движение в секторе");
            Assert.AreEqual(47, ally.Hp);
            Assert.IsFalse(watcher.IsOverwatching);
        }

        [Test]
        public void Overwatch_BeforeTrap_OnMove_FallenUnitDoesNotSpringIt()
        {
            // Дозорный ставит ловушку на (2,4) — первую клетку сектора на нижнем
            // ряду — и встаёт в дозор. Враг с 2 HP идёт (0,4)→(1,4)→(2,4).
            var (cs, watcher, enemy) = Scene(new GridPos(0, 4), enemyHp: 2);
            watcher.Abilities.Add(Game.Core.DefaultContent.SetTrap());
            Assert.AreEqual(CombatActionResult.Success, cs.UseAbility("set_trap", null, new GridPos(2, 4)));
            cs.Overwatch(new GridPos(9, 2));

            cs.Move(new GridPos(2, 4));

            Assert.AreEqual(1, ShotsBy(cs, "watcher"), "сначала выстрел из дозора");
            Assert.AreEqual(UnitLifeState.Dead, enemy.LifeState);
            Assert.IsNotNull(cs.TrapAt(new GridPos(2, 4)), "упавший ловушку не спускает — она ждёт следующего");
        }

        [Test]
        public void Overwatch_BeforeTrap_OnLunge_SameOrderAsMove()
        {
            // Та же развилка через рывок: враг-клинчер в (0,4) бросается на приманку
            // в (3,4) и приземляется в (2,3) — в сектор дозора и на ловушку.
            var map = new GridMap(10, 5);
            var cs = NewCombat(map);
            var watcher = U("watcher", Side.Player, 30, w: W());
            var lunger = U("lunger", Side.Enemy, 10, hp: 2, w: W(melee: true));
            var bait = U("bait", Side.Player, 5, w: W());
            watcher.Abilities.Add(Game.Core.DefaultContent.SetTrap());
            lunger.Abilities.Add(Game.Core.DefaultContent.Lunge());
            cs.AddUnit(watcher, new GridPos(0, 2));
            cs.AddUnit(lunger, new GridPos(0, 4));
            cs.AddUnit(bait, new GridPos(3, 4));
            cs.Begin();
            Assert.AreEqual(CombatActionResult.Success, cs.UseAbility("set_trap", null, new GridPos(2, 3)));
            cs.Overwatch(new GridPos(9, 2));

            Assert.AreSame(lunger, cs.Current);
            Assert.AreEqual(CombatActionResult.Success, cs.UseAbility("lunge", "bait"));

            Assert.AreEqual(1, ShotsBy(cs, "watcher"), "дозор видит приземление рывка — раньше ловушки");
            Assert.AreEqual(new GridPos(2, 3), lunger.Pos);
            Assert.AreEqual(UnitLifeState.Dead, lunger.LifeState);
            Assert.IsNotNull(cs.TrapAt(new GridPos(2, 3)), "исход тот же, что при обычном ходе");
            Assert.AreEqual(50, bait.Hp, "упавший в рывке уже не бьёт");
        }

        // ---- Без двойного профита ----

        [Test]
        public void Overwatch_Shot_HasPenalty_MatchesPreview_AndFeedsNoStrike()
        {
            var cfg = new BalanceConfig();
            var (cs, watcher, enemy) = Scene(new GridPos(0, 4));
            cs.Overwatch(new GridPos(9, 2));
            cs.Move(new GridPos(4, 4));

            var shot = cs.Attacks.Single(a => a.AttackerId == "watcher");
            Assert.AreEqual(99 - cfg.OverwatchAccuracyPenalty, shot.Chance, "навскидку — со штрафом");
            Assert.AreEqual(cs.OverwatchHitChancePreview(watcher, enemy), shot.Chance,
                "показанный шанс обязан совпадать с фактическим (телеграфия US-3.3)");
            Assert.AreEqual(HitOutcome.Hit, shot.Outcome);
            Assert.IsFalse(shot.Forced, "из дозора Strike не тратится");
            Assert.AreEqual(0, watcher.StrikeMeter, "попадание из дозора Strike-метр не копит");
        }

        [Test]
        public void Overwatch_UnusedReserve_BurnsAtOwnNextTurn_NoApCarryOver()
        {
            var (cs, watcher, _) = Scene(new GridPos(0, 0), watcherAt: new GridPos(4, 2));
            cs.Overwatch(new GridPos(9, 2));
            cs.Move(new GridPos(0, 4)); // мимо сектора
            cs.EndTurn();

            Assert.AreSame(watcher, cs.Current);
            Assert.IsFalse(watcher.IsOverwatching, "дозор живёт до своего следующего хода");
            Assert.AreEqual(8, watcher.Ap, "полный пул, а не пул + несгоревший резерв");
        }

        [Test]
        public void Overwatch_BrokenByStunAndKnockdown_NotBySuppression()
        {
            var (cs, watcher, _) = Scene(new GridPos(8, 4));
            cs.Overwatch(new GridPos(9, 2));

            cs.ApplyStatus(watcher, StatusType.Suppressed);
            Assert.IsTrue(watcher.IsOverwatching, "подавленный держит сектор (со штрафом к точности)");

            cs.ApplyStatus(watcher, StatusType.Stunned);
            Assert.IsFalse(watcher.IsOverwatching, "оглушённый — нет");

            var (cs2, watcher2, _) = Scene(new GridPos(8, 4));
            cs2.Overwatch(new GridPos(9, 2));
            cs2.ApplyStatus(watcher2, StatusType.KnockedDown);
            Assert.IsFalse(watcher2.IsOverwatching, "сбитый с ног — нет");
        }

        // ---- Геометрия конуса: только целые числа ----

        [Test]
        public void Cone_IsIntegerExact_EdgeIncluded_BackAndSideExcluded()
        {
            var o = new GridPos(0, 0);
            var aim = new GridPos(5, 0);

            Assert.IsTrue(OverwatchStance.InCone(o, aim, new GridPos(3, 0), 1, 1), "на оси");
            Assert.IsTrue(OverwatchStance.InCone(o, aim, new GridPos(3, 3), 1, 1), "ровно 45° — край включён");
            Assert.IsFalse(OverwatchStance.InCone(o, aim, new GridPos(3, 4), 1, 1), "за краем");
            Assert.IsFalse(OverwatchStance.InCone(o, aim, new GridPos(0, 3), 1, 1), "сбоку под 90°");
            Assert.IsFalse(OverwatchStance.InCone(o, aim, new GridPos(-2, 0), 1, 1), "за спиной");
            Assert.IsFalse(OverwatchStance.InCone(o, aim, o, 1, 1), "своя клетка");
            Assert.IsFalse(OverwatchStance.InCone(o, o, new GridPos(3, 0), 1, 1), "без оси конуса нет");

            // Диагональная ось: край конуса ложится на оси координат.
            Assert.IsTrue(OverwatchStance.InCone(o, new GridPos(5, 5), new GridPos(5, 0), 1, 1));
            Assert.IsFalse(OverwatchStance.InCone(o, new GridPos(5, 5), new GridPos(5, -1), 1, 1));

            // Нулевая ширина — только сама ось; ширину правит баланс, а не код.
            Assert.IsTrue(OverwatchStance.InCone(o, aim, new GridPos(4, 0), 0, 1));
            Assert.IsFalse(OverwatchStance.InCone(o, aim, new GridPos(4, 1), 0, 1));
            Assert.IsFalse(OverwatchStance.InCone(o, aim, new GridPos(-4, 0), 0, 1), "ось назад — не ось вперёд");
        }

        // ---- ИИ: симметрия ----

        [Test]
        public void Ai_WithNoShot_TakesOverwatch_TowardItsTarget()
        {
            // Стрелок ИИ замурован в (0,1): ходить некуда, цель за стеной.
            var map = new GridMap(8, 3);
            map.SetWall(new GridPos(0, 0));
            map.SetWall(new GridPos(0, 2));
            map.SetWall(new GridPos(1, 0));
            map.SetWall(new GridPos(1, 1));
            map.SetWall(new GridPos(1, 2));
            var cs = NewCombat(map);
            var ai = U("ai", Side.Enemy, 20, w: W());
            var target = U("target", Side.Player, 10, w: W());
            cs.AddUnit(ai, new GridPos(0, 1));
            cs.AddUnit(target, new GridPos(5, 1));
            cs.Begin();

            CombatAi.TakeTurn(cs);

            Assert.IsTrue(ai.IsOverwatching, "не по кому стрелять, AP есть — дозор");
            Assert.AreEqual(target.Pos, ai.Overwatch.Aim);
            Assert.AreSame(target, cs.Current);
        }
    }
}
