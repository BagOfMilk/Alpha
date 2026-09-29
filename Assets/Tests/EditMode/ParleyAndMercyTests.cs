using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Game.Core.Balance;
using Game.Core.Characters;
using Game.Core.Combat;
using Game.Core.Dungeons;
using Game.Core.Expeditions;
using Game.Core.Session;
using Game.Core.Stats;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Друга партія здібностей (docs/ABILITIES.md §4.6 і §5; власник, 29.09.2026: «ок»):
    /// «Слово миру», «Скласти зброю!», «Відкуп» — розмова перед боєм, одна спроба;
    /// «Милосердя на полі» — звалений ворог, що може здатися, стає полоненим.
    /// Пороги й ціна видно до кліку (інваріант 8); діє лише на тих, хто може здатися.
    /// </summary>
    public class ParleyAndMercyTests
    {
        private static readonly BalanceConfig Cfg = new BalanceConfig();

        // ---------------- розмова перед боєм (сесія) ----------------

        /// <summary>Сесія в першій кімнаті данжу; <paramref name="tune"/> — поріг, щоб точно вийшло чи ні.</summary>
        private static GameSession InDungeon(string siteId, System.Action<CombatBalance> tune)
        {
            var s = new GameSession();
            s.NewGame(new NewGameOptions { SkipCreation = true, HitRule = HitRuleKind.Threshold });
            var step = s.AdvanceScene();
            while (!step.IsFinished) step = step.IsChoice ? s.ChooseSceneOption(0) : s.AdvanceScene();
            // Єдина рефлексія у файлі: надбавки порогів — щоб результат не залежав від складу прибульців.
            var cfg = (BalanceConfig)typeof(GameSession).GetField("_cfg", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(s);
            tune?.Invoke(cfg.Combat);

            var party = new List<string> { GameSession.ProtagonistId };
            party.AddRange(s.GetRosterView().Companions
                .Where(c => c.Id != GameSession.ProtagonistId && (c.Status == CompanionStatus.Idle || c.Status == CompanionStatus.Assigned))
                .Select(c => c.Id).Take(2));
            s.DepartExpedition(siteId, ExpeditionApproach.Delve, party, 2);
            Assert.AreEqual(SessionState.Dungeon, s.State);
            return s;
        }

        private static Game.Core.Session.Views.ParleyView Parley(GameSession s, string form) =>
            s.GetDungeonView().CurrentRoom.Parley.Single(p => p.Form == form);

        private static int Enemies(GameSession s) => s.GetBattleView().Units.Count(u => u.Side == "Enemy");

        [Test]
        public void Peace_ShowsTheThresholdAndWhoLeaves_AndOnlyTheUnyieldingStay()
        {
            var s = InDungeon(DefaultDungeon.AbandonedCamp, c => c.PeaceOverResolve = -99);
            var peace = Parley(s, "peace");
            Assert.IsNull(peace.BlockKey);
            Assert.AreEqual(1, peace.LeavingCount, "розвідник може здатися — відійде");
            Assert.AreEqual(1, peace.RemainingCount, "сокирник не здається — лишиться");
            Assert.IsTrue(peace.Passes);

            Assert.IsNull(s.ResolveDungeonParley("peace"), "з рештою — бій");
            Assert.AreEqual(1, Enemies(s), "менший бій замість жодного");
            Assert.AreEqual("Encounter", s.GetBattleView().Opening);
            Assert.IsTrue(s.DayLog.Any(e => e.Key == "dungeon.parley.peace.success"));
        }

        [Test]
        public void Peace_Failed_FightsEveryone_WithoutAnAmbush()
        {
            var s = InDungeon(DefaultDungeon.AbandonedCamp, c => c.PeaceOverResolve = 99);
            Assert.IsFalse(Parley(s, "peace").Passes, "видно до кліку, що не вийде");
            s.ResolveDungeonParley("peace");
            Assert.AreEqual(2, Enemies(s));
            Assert.AreEqual("Encounter", s.GetBattleView().Opening, "ворог насторожі — без засідки");
        }

        [Test]
        public void Ultimatum_Accepted_ThoseWhoCanYieldBecomePrisoners()
        {
            var s = InDungeon(DefaultDungeon.AbandonedCamp, c => c.UltimatumOverResolve = -99);
            int before = s.GetPrisonersView().Count;
            s.ResolveDungeonParley("surrender");
            Assert.AreEqual(before + 1, s.GetPrisonersView().Count, "розвідник — полонений, долю вирішить віче");
            Assert.AreEqual(1, Enemies(s), "з сокирником — бій");
        }

        [Test]
        public void Ultimatum_Refused_EnemyIsProvoked_AndStrikesTruerInRoundOne()
        {
            var s = InDungeon(DefaultDungeon.AbandonedCamp, c => c.UltimatumOverResolve = 99);
            s.ResolveDungeonParley("surrender");
            Assert.AreEqual("Provoked", s.GetBattleView().Opening);
            Assert.AreEqual(2, Enemies(s));
        }

        [Test]
        public void Bribe_TheHordeIsNotForSale_NothingHappens()
        {
            var s = InDungeon(DefaultDungeon.AbandonedCamp, null);
            Assert.AreEqual("not_for_sale", Parley(s, "bribe").BlockKey);
            Assert.IsNotNull(s.ResolveDungeonParley("bribe"));
            Assert.AreEqual(SessionState.Dungeon, s.State, "заблокована форма нічого не запускає");
        }

        [Test]
        public void Bribe_TheBanditTakesTheGold_AndTheRoomIsPassedWithoutAFight()
        {
            var s = InDungeon(DefaultDungeon.OldHermitage, c => c.BribeOverGreed = -99);
            var bribe = Parley(s, "bribe");
            Assert.AreEqual(Cfg.Combat.BribeGoldPerRank[1], bribe.GoldCost, "міні-бос — ціна видна до кліку");
            Assert.AreEqual(0, bribe.RemainingCount);
            int gold = s.GetEconomyView().Gold;
            int cleared = s.GetDungeonView().RoomsCleared;

            Assert.IsNotNull(s.ResolveDungeonParley("bribe"));
            Assert.AreEqual(SessionState.Dungeon, s.State, "бою немає");
            Assert.AreEqual(gold - bribe.GoldCost, s.GetEconomyView().Gold);
            Assert.AreEqual(cleared + 1, s.GetDungeonView().RoomsCleared);
        }

        [Test]
        public void Bribe_Refused_NextPriceGrows_AndTheGrudgeIsSaved()
        {
            var s = InDungeon(DefaultDungeon.OldHermitage, c => c.BribeOverGreed = 99);
            s.ResolveDungeonParley("bribe");
            Assert.AreEqual(SessionState.Battle, s.State, "відмова — бій");

            var refusals = (Dictionary<string, int>)typeof(GameSession)
                .GetField("_bribeRefusals", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(s);
            var plan = GameSession.PlanParley("bribe", new[] { "forest_bandit" }, 3, _ => 0, 0, 1000, refusals, Cfg.Combat);
            Assert.Greater(plan.GoldCost, Cfg.Combat.BribeGoldPerRank[1], "наступного разу — дорожче");
        }

        [Test]
        public void Plan_FearMakesWordsDearer_ButNotThreats()
        {
            var ids = new[] { "horde_vanguard", "horde_scout" };
            var calm = GameSession.PlanParley("peace", ids, 3, _ => 5, 0, 0, null, Cfg.Combat);
            var afraid = GameSession.PlanParley("peace", ids, 3, _ => 5, 2, 0, null, Cfg.Combat);
            Assert.AreEqual(calm.Threshold + 2, afraid.Threshold, "страх громади — Переконання дорожче");

            var threat = GameSession.PlanParley("surrender", ids, 3, _ => 5, 2, 0, null, Cfg.Combat);
            var threatCalm = GameSession.PlanParley("surrender", ids, 3, _ => 5, 0, 0, null, Cfg.Combat);
            Assert.AreEqual(threatCalm.Threshold, threat.Threshold, "Залякування страхом не дешевшає і не дорожчає (FearState)");

            var fewer = GameSession.PlanParley("surrender", ids, 2, _ => 5, 0, 0, null, Cfg.Combat);
            Assert.AreEqual(threatCalm.Threshold + 1, fewer.Threshold, "перевага сил — ультиматум на одиницю легший");
        }

        // ---------------- «Милосердя на полі» (бій) ----------------

        private static WeaponDefinition Club(int dmg) => new WeaponDefinition("club", "Club", SkillType.Melee)
        { DamageMin = dmg, DamageMax = dmg, ApCost = 2, OptimalRange = 1 };

        private static CombatUnit Foe(EnemyRank rank, bool canSurrender, string id = "x")
        {
            var def = new EnemyDefinition("enemy." + id, id, EnemyRole.Breacher, EnemyFamily.Human)
            {
                MaxHp = 10, Rank = rank, CanSurrender = canSurrender, SurrenderAtHpPercent = 30, Initiative = 1,
                Weapon = new WeaponDefinition("bow", "Bow", SkillType.Ranged) { DamageMin = 1, DamageMax = 1, ApCost = 3, OptimalRange = 8 }
            };
            return CombatUnit.FromEnemy(def, id + "#0");
        }

        /// <summary>Герой впритул до ворога; другий ворог (якщо є) — далі, щоб бій не скінчився з першим упалим.</summary>
        private static CombatState Duel(CombatUnit foe, out CombatUnit hero, CombatUnit second = null)
        {
            var cs = new CombatState(new GridMap(6, 1), Cfg, new ThresholdRule(Cfg), null);
            hero = new CombatUnit("hero", Side.Player,
                new UnitProfile { DisplayName = "hero", MaxHp = 30, MaxAp = 8, Accuracy = 90, Initiative = 9, MoveApPerTile = 1 }, Club(15));
            hero.Abilities.Add(DefaultCombatContent.Mercy());
            cs.AddUnit(hero, new GridPos(0, 0));
            cs.AddUnit(foe, new GridPos(1, 0));
            if (second != null) cs.AddUnit(second, new GridPos(5, 0));
            cs.Begin();
            return cs;
        }

        [Test]
        public void OverkilledYielder_FallsInsteadOfDying_TheBossStillDies()
        {
            var yielder = Foe(EnemyRank.Grunt, true);
            Duel(yielder, out _).Attack(yielder.Id); // 10 → −5 одним ударом, повз поріг здачі
            Assert.AreEqual(UnitLifeState.Downed, yielder.LifeState, "той, хто може здатися, падає — його можна пощадити");

            var boss = Foe(EnemyRank.Boss, true);
            Duel(boss, out _).Attack(boss.Id);
            Assert.AreEqual(UnitLifeState.Dead, boss.LifeState, "бос не здається — і не падає");
        }

        [Test]
        public void Mercy_SparesTheFallen_MidBattle_StraightIntoCaptivity()
        {
            var foe = Foe(EnemyRank.Grunt, true);
            var other = Foe(EnemyRank.Grunt, false, "y");
            var cs = Duel(foe, out var hero, other);
            cs.Attack(foe.Id);
            Assert.AreEqual(UnitLifeState.Downed, foe.LifeState);
            Assert.AreEqual(CombatOutcome.Ongoing, cs.Outcome, "інший ворог ще стоїть");

            Assert.AreEqual(CombatActionResult.Success, cs.UseAbility("ability.mercy", foe.Id));
            Assert.AreEqual(UnitLifeState.Surrendered, foe.LifeState);
            Assert.IsTrue(foe.Spared);
            Assert.IsTrue(cs.Map.IsFree(new GridPos(1, 0)), "пощадженого винесли з поля");
        }

        [Test]
        public void AfterVictory_TheFallenWhoCanYield_AreAtYourMercy()
        {
            var foe = Foe(EnemyRank.Grunt, true);
            var cs = Duel(foe, out _);
            cs.Attack(foe.Id);
            Assert.AreEqual(CombatOutcome.Victory, cs.Outcome, "упав останній — бій скінчено");
            var fallen = BattleResult.From(cs).SurrenderedEnemies.Single();
            Assert.IsFalse(fallen.Spared, "не пощаджений у бою — його долю питають на панелі результату (відпустити / у полон / добити)");
        }

        [Test]
        public void Mercy_OnlyOnTheFallen()
        {
            var foe = Foe(EnemyRank.Grunt, true);
            var cs = Duel(foe, out var hero);
            int ap = hero.Ap;
            Assert.AreEqual(CombatActionResult.InvalidTarget, cs.UseAbility("ability.mercy", foe.Id), "на ногах — не пощадиш");
            Assert.AreEqual(ap, hero.Ap);
        }

        [Test]
        public void ProvokedOpening_EnemyStrikesTruerOnlyInRoundOne()
        {
            var cs = new CombatState(new GridMap(6, 1), Cfg, new ThresholdRule(Cfg), null);
            var hero = new CombatUnit("hero", Side.Player,
                new UnitProfile { DisplayName = "hero", MaxHp = 30, MaxAp = 8, Accuracy = 60, Initiative = 1, MoveApPerTile = 1 }, Club(1));
            var foe = Foe(EnemyRank.Grunt, true);
            foe.Profile.Accuracy = 50;
            cs.AddUnit(hero, new GridPos(0, 0));
            cs.AddUnit(foe, new GridPos(5, 0));
            cs.Begin(BattleOpening.Provoked);

            int calm = HitChanceCalculator.Compute(foe, hero, cs.Map, Cfg);
            Assert.Greater(calm, 0);
            Assert.AreEqual(calm + Cfg.Combat.ProvokedAccuracyBonus, cs.HitChancePreview(foe, hero), "раунд 1 — розлючений");
            Assert.AreEqual(HitChanceCalculator.Compute(hero, foe, cs.Map, Cfg), cs.HitChancePreview(hero, foe), "на загін бонус не діє");
            while (cs.Round == 1) cs.EndTurn();
            Assert.AreEqual(calm, cs.HitChancePreview(foe, hero), "з раунду 2 — як завжди");
        }
    }
}
