using Game.Core.Balance;
using Game.Core.Combat;
using Game.Core.Stats;
using Game.Gameplay.Combat;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Боевой ИИ: читаемые ролевые биасы, скоринг цели, стабилизация медиком,
    /// завершаемость AI-vs-AI. Перенесено из архивной боевой линии, адаптировано
    /// на IHitRule (R1): большинство сценариев играются через ThresholdRule,
    /// чтобы поведение ИИ проверялось само по себе, а не пряталось за удачей
    /// броска; настоящая случайность (PercentRule + SeededDiceRoller) нужна
    /// только тесту на воспроизводимость по сиду.
    /// </summary>
    public class CombatAiTests
    {
        private static CombatUnit U(string id, Side side, int init, int hp = 12, int acc = 70,
                                    WeaponDefinition w = null, EnemyRole role = EnemyRole.Skirmisher,
                                    int medicine = 0)
        {
            var p = new UnitProfile
            {
                DisplayName = id, MaxHp = hp, MaxAp = 8, Accuracy = acc, Defense = 0,
                Initiative = init, Resolve = 0, Role = role, MedicineSkill = medicine,
                CanBeDowned = side == Side.Player, MoveApPerTile = 1
            };
            return new CombatUnit(id, side, p, w);
        }

        private static WeaponDefinition Rifle() => new WeaponDefinition("r", "Винтовка", SkillType.Ranged)
        { DamageMin = 3, DamageMax = 5, CritDamageBonus = 1, ApCost = 4, OptimalRange = 5 };

        private static WeaponDefinition Club() => new WeaponDefinition("c", "Дубина", SkillType.Melee)
        { DamageMin = 3, DamageMax = 6, CritDamageBonus = 1, ApCost = 3, OptimalRange = 1 };

        private static readonly BalanceConfig Cfg = new BalanceConfig();
        private static CombatState Deterministic(GridMap map) => new CombatState(map, Cfg, new ThresholdRule(Cfg), null);

        // ---- Ролевые биасы позиции ----
        [Test]
        public void MeleeRole_ClosesDistance()
        {
            var map = new GridMap(12, 1);
            var cs = Deterministic(map);
            var brute = U("brute", Side.Enemy, 10, w: Club(), role: EnemyRole.Tank);
            var marks = U("marks", Side.Player, 1, w: Rifle());
            cs.AddUnit(brute, new GridPos(0, 0));
            cs.AddUnit(marks, new GridPos(11, 0));
            cs.Begin(); // ходит brute

            CombatAi.TakeTurn(cs);
            Assert.Less(GridPos.Chebyshev(brute.Pos, marks.Pos), 11, "клинч-роль сближается");
        }

        [Test]
        public void RangedRole_PrefersCoverTile()
        {
            var map = new GridMap(12, 3);
            map.SetCover(new GridPos(6, 1), Direction.East, CoverType.Full);
            var cs = Deterministic(map);
            var gunner = U("gunner", Side.Enemy, 10, acc: 1, w: Rifle()); // мизерный шанс — сперва позиция
            var target = U("target", Side.Player, 1, w: Rifle());
            cs.AddUnit(gunner, new GridPos(5, 0));
            cs.AddUnit(target, new GridPos(11, 1));
            cs.Begin();

            CombatAi.TakeTurn(cs);
            Assert.AreEqual(new GridPos(6, 1), gunner.Pos,
                "стрелок с плохим шансом идёт в укрытие на оптимале, а не стоит столбом");
        }

        // ---- Скоринг цели ----
        [Test]
        public void Target_FinishesAlmostDeadOverCloser()
        {
            var map = new GridMap(12, 3);
            var cs = Deterministic(map);
            var shooter = U("shooter", Side.Enemy, 10, w: Rifle());
            var healthy = U("healthy", Side.Player, 2, hp: 12, w: Rifle());
            var dying = U("dying", Side.Player, 1, hp: 12, w: Rifle());
            cs.AddUnit(shooter, new GridPos(0, 1));
            cs.AddUnit(healthy, new GridPos(3, 1));
            cs.AddUnit(dying, new GridPos(5, 1));
            dying.Hp = 2; // добивание ценнее близости
            cs.Begin();

            CombatAi.TakeTurn(cs);
            Assert.IsFalse(dying.IsActive, "умирающая цель снята в первую очередь (ThresholdRule — гарантированный Hit)");
        }

        // ---- Медик ----
        [Test]
        public void Medic_StabilizesDownedAlly_First()
        {
            var map = new GridMap(6, 1);
            var cs = Deterministic(map);
            var medic = U("medic", Side.Player, 10, medicine: 2, w: Rifle());
            var fallen = U("fallen", Side.Player, 5, w: Rifle());
            var enemy = U("enemy", Side.Enemy, 1, hp: 50, w: Rifle());
            cs.AddUnit(medic, new GridPos(0, 0));
            cs.AddUnit(fallen, new GridPos(1, 0));
            cs.AddUnit(enemy, new GridPos(5, 0));
            fallen.LifeState = UnitLifeState.Downed;
            fallen.DownWindowRemaining = 2;
            cs.Begin(); // ходит медик

            CombatAi.TakeTurn(cs);
            Assert.AreEqual(UnitLifeState.Stabilized, fallen.LifeState, "медик спасает, а не стреляет");
        }

        // ---- Статусы (контролёр давит статусом, не ждёт удачного ролла) ----
        [Test]
        public void Controller_AppliesFreshStatus_NotDuplicate()
        {
            var map = new GridMap(10, 1);
            var cs = Deterministic(map);

            var suppress = new AbilityDefinition("t_suppress", "suppress", SkillType.None, 1)
                .Costs(ap: 2, cooldown: 3)
                .Targets(AbilityTarget.Enemy, range: 6, needsLos: true)
                .WithEffect(new AbilityEffect { Kind = AbilityEffectKind.ApplyStatus, Status = StatusType.Suppressed });
            var droneDef = new EnemyDefinition("test.drone", "drone", EnemyRole.Controller, EnemyFamily.Robot)
            {
                MaxHp = 10, MaxAp = 8, Accuracy = 70, Weapon = Rifle(),
                Abilities = { suppress }
            };
            var drone = CombatUnit.FromEnemy(droneDef, "drone1");
            var hero = U("hero", Side.Player, 1, w: Rifle());
            cs.AddUnit(drone, new GridPos(0, 0));
            cs.AddUnit(hero, new GridPos(4, 0));
            cs.Begin(); // дрон (иниц. 7 по умолчанию EnemyDefinition) ходит первым

            CombatAi.TakeTurn(cs);
            Assert.IsTrue(hero.HasStatus(StatusType.Suppressed), "контролёр давит статусом, а не ждёт ролла");
        }

        // ---- Завершаемость и симметрия ----
        [Test]
        public void AiVsAi_SkirmishConcludes()
        {
            var map = new GridMap(12, 8);
            var cs = Deterministic(map);

            var a = U("a", Side.Player, 6, hp: 14, w: Rifle());
            var b = U("b", Side.Player, 5, hp: 14, w: Rifle());
            var m = U("m", Side.Player, 4, hp: 12, w: Rifle(), medicine: 2);
            cs.AddUnit(a, new GridPos(1, 2));
            cs.AddUnit(b, new GridPos(1, 4));
            cs.AddUnit(m, new GridPos(0, 3));
            cs.AddUnit(CombatUnit.FromEnemy(DefaultCombatContent.TuharBoyar(), "e1"), new GridPos(10, 2));
            cs.AddUnit(CombatUnit.FromEnemy(DefaultCombatContent.HordeSkirmisher(), "e2"), new GridPos(11, 3));
            cs.AddUnit(CombatUnit.FromEnemy(DefaultCombatContent.HordeScout(), "e3"), new GridPos(10, 5));
            cs.AddUnit(CombatUnit.FromEnemy(DefaultCombatContent.HordeScout(), "e4"), new GridPos(11, 6));
            cs.Begin();

            CombatAi.AutoResolve(cs, turnBudget: 300);
            Assert.AreNotEqual(CombatOutcome.Ongoing, cs.Outcome,
                "ИИ-против-ИИ завершается в разумный бюджет (гарантия — и бюджет ходов, и RoundCap внутри CombatState)");
        }

        [Test]
        public void MeleeWeapon_VolleyAbility_RequiresContact()
        {
            var map = new GridMap(12, 1);
            var cs = Deterministic(map);
            var brute = U("brute", Side.Player, 10, w: Club());
            brute.Abilities.Add(DefaultCombatContent.Volley());
            var foe = U("foe", Side.Enemy, 1, hp: 30, w: Rifle());
            cs.AddUnit(brute, new GridPos(0, 0));
            cs.AddUnit(foe, new GridPos(3, 0));
            cs.Begin();

            Assert.AreEqual(CombatActionResult.OutOfRange, cs.UseAbility("ability.volley", "foe"),
                "мили-удар способностью не бьёт с дистанции (симметрия с Attack), AP не тратится");
            Assert.AreEqual(8, brute.Ap);

            cs.Move(new GridPos(2, 0)); // в контакт
            Assert.AreEqual(CombatActionResult.Success, cs.UseAbility("ability.volley", "foe"));
        }

        [Test]
        public void AiVsAi_IsDeterministic_BySeed()
        {
            string RunOnce()
            {
                var map = new GridMap(10, 3);
                var cs = new CombatState(map, Cfg, new PercentRule(Cfg), new SeededDiceRoller(7));
                cs.AddUnit(U("p", Side.Player, 5, w: Rifle()), new GridPos(0, 1));
                cs.AddUnit(CombatUnit.FromEnemy(DefaultCombatContent.HordeScout(), "e"), new GridPos(9, 1));
                cs.Begin();
                CombatAi.AutoResolve(cs, 100);
                return cs.Outcome + ":" + cs.Log.Count + ":" + cs.Attacks.Count;
            }
            Assert.AreEqual(RunOnce(), RunOnce(), "тот же сид — тот же бой (реплей)");
        }
    }
}
