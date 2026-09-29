using System.Linq;
using System.Reflection;
using Game.Core.Balance;
using Game.Core.Combat;
using Game.Core.Session;
using Game.Core.Stats;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Трек C5 — перша партія здібностей (docs/ABILITIES.md §7; власник, 29.09.2026:
    /// «ок», «Тенета норм»; звірі лишаються ворогами — «так»). Кожна дія — реальний
    /// ефект, поріг видно до кліку (інваріант 8), відмова «Імунітет» не з'їдає ОД,
    /// провал змагання — з'їдає (так у картці).
    /// </summary>
    public class AbilityBatchOneTests
    {
        private static readonly BalanceConfig Cfg = new BalanceConfig();

        private static WeaponDefinition Club(int dmg) => new WeaponDefinition("club", "Club", SkillType.Melee)
        { DamageMin = dmg, DamageMax = dmg, ApCost = 2, OptimalRange = 1 };

        private static CombatUnit Player(string id, int initiative, int intimidate = 0, int hp = 30, int accuracy = 200)
        {
            var p = new UnitProfile
            {
                DisplayName = id, MaxHp = hp, MaxAp = 8, Accuracy = accuracy, Initiative = initiative,
                MoveApPerTile = 1, IntimidateSkill = intimidate
            };
            return new CombatUnit(id, Side.Player, p, Club(5));
        }

        private static CombatUnit Foe(string id, int resolve = 1, EnemyRank rank = EnemyRank.Grunt,
                                      EnemyFamily family = EnemyFamily.Human, bool canSurrender = false,
                                      int hp = 20, int armor = 0, int accuracy = 200)
        {
            var def = new EnemyDefinition("enemy." + id, id, EnemyRole.Breacher, family)
            {
                MaxHp = hp, MaxAp = 8, Accuracy = accuracy, Initiative = 1, Resolve = resolve, Armor = armor,
                Rank = rank, CanSurrender = canSurrender, SurrenderAtHpPercent = 30,
                Weapon = Club(4)
            };
            return CombatUnit.FromEnemy(def, def.Id + "#0");
        }

        private static CombatState NewCombat(int w = 8, int h = 3) =>
            new CombatState(new GridMap(w, h), Cfg, new ThresholdRule(Cfg), null);

        // ---------------- «Підбадьорити» ----------------

        [Test]
        public void Rally_ClearsSuppressionAndKnockdown_OncePerBattlePerAlly()
        {
            var cs = NewCombat();
            var hero = Player("hero", 9);
            var ally = Player("ally", 8);
            hero.Abilities.Add(DefaultCombatContent.Rally());
            cs.AddUnit(hero, new GridPos(0, 0));
            cs.AddUnit(ally, new GridPos(2, 0));
            cs.AddUnit(Foe("foe"), new GridPos(7, 2));
            cs.Begin();
            cs.ApplyStatus(ally, StatusType.Suppressed);
            cs.ApplyStatus(ally, StatusType.KnockedDown);

            Assert.AreEqual(CombatActionResult.Success, cs.UseAbility("ability.rally", ally.Id));
            Assert.IsFalse(ally.HasStatus(StatusType.Suppressed));
            Assert.IsFalse(ally.HasStatus(StatusType.KnockedDown), "знімає те, що на ньому є");
            Assert.AreEqual(0, ally.BonusApNextTurn, "стан зняли — ОД не додаємо");

            int ap = hero.Ap;
            Assert.AreEqual(CombatActionResult.InvalidTarget, cs.UseAbility("ability.rally", ally.Id), "раз за бій на союзника");
            Assert.AreEqual(ap, hero.Ap, "відмова ДО дії не з'їдає ОД");
        }

        [Test]
        public void Rally_OnCleanAlly_GivesOneApOnHisNextTurnOnly()
        {
            var cs = NewCombat();
            var hero = Player("hero", 9);
            var ally = Player("ally", 8);
            hero.Abilities.Add(DefaultCombatContent.Rally());
            cs.AddUnit(hero, new GridPos(0, 0));
            cs.AddUnit(ally, new GridPos(2, 0));
            cs.AddUnit(Foe("foe"), new GridPos(7, 2));
            cs.Begin();

            Assert.AreEqual(CombatActionResult.Success, cs.UseAbility("ability.rally", ally.Id));
            cs.EndTurn();
            Assert.AreSame(ally, cs.Current);
            Assert.AreEqual(ally.Profile.MaxAp + 1, ally.Ap, "+1 ОД саме на цей хід");
            Assert.AreEqual(0, ally.BonusApNextTurn, "бонус не переходить далі");
        }

        // ---------------- «Розлютити» ----------------

        [Test]
        public void Enrage_Success_EnemyGoesForTheProvoker_NotTheWoundedAlly()
        {
            var cs = NewCombat();
            var hero = Player("hero", 9, intimidate: 2, hp: 40);
            var weak = Player("weak", 8, hp: 3);
            var foe = Foe("foe", resolve: 2);
            hero.Abilities.Add(DefaultCombatContent.Enrage());
            cs.AddUnit(hero, new GridPos(0, 0));
            cs.AddUnit(weak, new GridPos(5, 1));
            cs.AddUnit(foe, new GridPos(6, 1)); // впритул до слабкого — без люті добив би його
            cs.Begin();

            var check = cs.DescribeCheck(hero, foe, DefaultCombatContent.Enrage());
            Assert.IsTrue(check.Passes, "Залякування 2 ≥ Воля 2");
            Assert.AreEqual(CombatActionResult.Success, cs.UseAbility("ability.enrage", foe.Id));
            Assert.IsTrue(foe.HasStatus(StatusType.Enraged));
            cs.EndTurn(); // hero
            cs.EndTurn(); // weak
            Assert.AreSame(foe, cs.Current);

            CombatAi.TakeTurn(cs);

            Assert.AreEqual(3, weak.Hp, "розлючений не чіпає пораненого поруч");
            Assert.Less(hero.Hp, 40, "а йде й б'є провокатора");
        }

        [Test]
        public void Enrage_EnragedTarget_IsEasierToHit_WithAVisibleTerm()
        {
            var cs = NewCombat();
            var hero = Player("hero", 9, intimidate: 3, accuracy: 60);
            var foe = Foe("foe", resolve: 1);
            hero.Abilities.Add(DefaultCombatContent.Enrage());
            cs.AddUnit(hero, new GridPos(0, 0));
            cs.AddUnit(foe, new GridPos(3, 0));
            cs.Begin();

            int before = cs.HitChancePreview(hero, foe);
            cs.UseAbility("ability.enrage", foe.Id);
            Assert.AreEqual(before + Cfg.Combat.EnragedDefensePenalty, cs.HitChancePreview(hero, foe),
                "лють без оглядки — захист падає, і це окремий рядок розкладу шансу");
        }

        [Test]
        public void Enrage_Boss_IsImmune_AndCostsNothing_WeakWill_FailsAndCostsAp()
        {
            var cs = NewCombat();
            var hero = Player("hero", 9, intimidate: 1);
            var boss = Foe("boss", resolve: 0, rank: EnemyRank.Boss);
            var stubborn = Foe("stubborn", resolve: 4);
            hero.Abilities.Add(DefaultCombatContent.Enrage());
            cs.AddUnit(hero, new GridPos(0, 0));
            cs.AddUnit(boss, new GridPos(3, 0));
            cs.AddUnit(stubborn, new GridPos(3, 2));
            cs.Begin();

            var immune = cs.DescribeCheck(hero, boss, DefaultCombatContent.Enrage());
            Assert.IsTrue(immune.Immune);
            Assert.AreEqual("immune", immune.BlockKey);
            int ap = hero.Ap;
            Assert.AreEqual(CombatActionResult.InvalidTarget, cs.UseAbility("ability.enrage", boss.Id));
            Assert.AreEqual(ap, hero.Ap, "«Імунітет» видно заздалегідь — ОД не згорають");

            Assert.AreEqual(CombatActionResult.Success, cs.UseAbility("ability.enrage", stubborn.Id));
            Assert.IsFalse(stubborn.HasStatus(StatusType.Enraged), "Залякування 1 < Воля 4");
            Assert.AreEqual(ap - 2, hero.Ap, "провал змагання — ОД витрачено (картка)");
            Assert.IsTrue(cs.Journal.Any(e => e.Key == CombatLogKeys.EnrageFailed));
        }

        // ---------------- «Залякати» ----------------

        [Test]
        public void Intimidate_NeedsWillPlusOne_Suppresses_AndBringsSurrenderCloser()
        {
            var cs = NewCombat();
            var hero = Player("hero", 9, intimidate: 3);
            var foe = Foe("foe", resolve: 2, canSurrender: true);
            hero.Abilities.Add(DefaultCombatContent.Intimidate());
            cs.AddUnit(hero, new GridPos(0, 0));
            cs.AddUnit(foe, new GridPos(3, 0));
            cs.Begin();

            var check = cs.DescribeCheck(hero, foe, DefaultCombatContent.Intimidate());
            Assert.AreEqual(3, check.Threshold, "Воля 2 + 1");
            Assert.IsTrue(check.Passes);
            int thresholdBefore = cs.SurrenderThresholdPercent(foe);

            Assert.AreEqual(CombatActionResult.Success, cs.UseAbility("ability.intimidate", foe.Id));
            Assert.IsTrue(foe.HasStatus(StatusType.Suppressed), "наявний стан, нового немає (один ефект — одна система)");
            Assert.Greater(cs.SurrenderThresholdPercent(foe), thresholdBefore, "залякана ціль здається раніше (№14.2)");
        }

        [Test]
        public void Intimidate_Beast_Flees_AndTheFightEndsWithoutBloodOrCaptivity()
        {
            var cs = NewCombat();
            var hero = Player("hero", 9, intimidate: 3);
            var wolf = Foe("wolf", resolve: 1, family: EnemyFamily.Beast);
            hero.Abilities.Add(DefaultCombatContent.Intimidate());
            cs.AddUnit(hero, new GridPos(0, 0));
            cs.AddUnit(wolf, new GridPos(3, 0));
            cs.Begin();

            Assert.AreEqual(CombatActionResult.Success, cs.UseAbility("ability.intimidate", wolf.Id));
            Assert.AreEqual(UnitLifeState.Fled, wolf.LifeState, "звір не здається — тікає");
            Assert.IsFalse(wolf.IsActive);
            Assert.IsTrue(cs.Map.IsFree(new GridPos(3, 0)), "клітинка звільнилась");
            Assert.AreEqual(CombatOutcome.Victory, cs.Outcome, "останній ворог утік — бій скінчено");
            Assert.AreEqual(0, BattleResult.From(cs).SurrenderedEnemies.Count, "утікач — не полонений");
        }

        // ---------------- Тенета ----------------

        [Test]
        public void Net_LaysAHarmlessSuppressingTrap_AndBreaksEnemyOverwatchWithinTwo()
        {
            var cs = NewCombat(8, 5);
            var hero = Player("hero", 5);
            var watcherNear = Foe("near");
            var watcherFar = Foe("far");
            watcherNear.Profile.Initiative = 9;
            watcherFar.Profile.Initiative = 8;
            hero.Abilities.Add(DefaultCombatContent.Net());
            cs.AddUnit(hero, new GridPos(0, 2));
            cs.AddUnit(watcherNear, new GridPos(4, 2));
            cs.AddUnit(watcherFar, new GridPos(7, 4));
            cs.Begin();

            Assert.AreSame(watcherNear, cs.Current);
            Assert.AreEqual(CombatActionResult.Success, cs.Overwatch(new GridPos(0, 2)));
            Assert.AreSame(watcherFar, cs.Current);
            Assert.AreEqual(CombatActionResult.Success, cs.Overwatch(new GridPos(0, 2)));
            Assert.AreSame(hero, cs.Current);

            var tile = new GridPos(3, 2);
            Assert.AreEqual(CombatActionResult.Success, cs.UseAbility("ability.net", null, tile));

            var trap = cs.TrapAt(tile);
            Assert.IsNotNull(trap);
            Assert.AreEqual(0, trap.Damage, "сітка без шкоди");
            Assert.AreEqual(StatusType.Suppressed, trap.StatusOnTrigger);
            Assert.IsFalse(watcherNear.IsOverwatching, "на відстані 1 від сітки — дозор збито");
            Assert.IsTrue(watcherFar.IsOverwatching, "на відстані 4 — дозор лишився");
            Assert.IsTrue(cs.Journal.Any(e => e.Key == CombatLogKeys.OverwatchLostNet));
        }

        // ---------------- «Пробити» ----------------

        [Test]
        public void Pierce_WaitsForWornArmor_ThenHitsSurely_ThroughIt()
        {
            var cs = NewCombat();
            var hero = Player("hero", 9, accuracy: 0); // сам по собі не влучив би ніколи
            var foe = Foe("foe", hp: 30, armor: 4);
            hero.Abilities.Add(DefaultCombatContent.Pierce());
            cs.AddUnit(hero, new GridPos(0, 0));
            cs.AddUnit(foe, new GridPos(1, 0));
            cs.Begin();

            int ap = hero.Ap;
            Assert.AreEqual(CombatActionResult.InvalidTarget, cs.UseAbility("ability.pierce", foe.Id), "броня ще ціла");
            Assert.AreEqual(ap, hero.Ap);
            Assert.AreEqual("armor_intact", cs.DescribeCheck(hero, foe, DefaultCombatContent.Pierce()).BlockKey);

            foe.ArmorShred = 3;
            Assert.AreEqual(CombatActionResult.Success, cs.UseAbility("ability.pierce", foe.Id));
            Assert.AreEqual(30 - 5, foe.Hp, "гарантовано і без броні: уся шкода зброї");
        }

        // ---------------- прев'ю і каталог ----------------

        [Test]
        public void Preview_ShowsTheSameCheck_AsTheAction()
        {
            var cs = NewCombat();
            var hero = Player("hero", 9, intimidate: 1);
            var boss = Foe("boss", resolve: 0, rank: EnemyRank.Boss);
            var foe = Foe("foe", resolve: 1);
            hero.Abilities.Add(DefaultCombatContent.Enrage());
            hero.Abilities.Add(DefaultCombatContent.Intimidate());
            cs.AddUnit(hero, new GridPos(0, 0));
            cs.AddUnit(boss, new GridPos(3, 0));
            cs.AddUnit(foe, new GridPos(3, 2));
            cs.Begin();

            var session = new GameSession();
            typeof(GameSession).GetField("_battle", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(session, cs);

            var immune = session.PreviewAttack(hero.Id, boss.Id, "ability.enrage");
            Assert.AreEqual("InvalidTarget", immune.Result);
            Assert.IsTrue(immune.CheckImmune);
            Assert.AreEqual("immune", immune.CheckBlockKey);

            var weak = session.PreviewAttack(hero.Id, foe.Id, "ability.intimidate");
            Assert.AreEqual("Success", weak.Result, "дію можна почати — просто не вийде");
            Assert.AreEqual("Contest", weak.CheckKind);
            Assert.AreEqual(1, weak.CheckValue);
            Assert.AreEqual(2, weak.CheckThreshold);
            Assert.IsFalse(weak.CheckPasses, "видно до кліку, що ОД згорять");
        }

        [Test]
        public void Catalog_HasTheBatch_WithTheCardGates()
        {
            var catalog = DefaultCombatContent.AbilityCatalog().ToDictionary(a => a.Id);
            Assert.AreEqual(SkillType.Persuade, catalog["ability.rally"].Skill);
            Assert.AreEqual(3, catalog["ability.rally"].RequiredSkillLevel);
            Assert.IsFalse(catalog["ability.rally"].RequiresLineOfSight, "голос чути з-за укриття");
            Assert.AreEqual(SkillType.Intimidate, catalog["ability.enrage"].Skill);
            Assert.AreEqual(SkillType.Intimidate, catalog["ability.intimidate"].Skill);
            Assert.AreEqual(SkillType.Survival, catalog["ability.net"].Skill);
            Assert.AreEqual(SkillType.Melee, catalog["ability.pierce"].Skill);
            Assert.AreEqual(4, catalog["ability.pierce"].RequiredSkillLevel);
        }
    }
}
