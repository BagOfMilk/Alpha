using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Game.Core.Balance;
using Game.Core.Combat;
using Game.Core.Prisoners;
using Game.Core.Session;
using Game.Core.Session.Bots;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Трек C4 — здача і полон (Поправка №14.2; власник, 29.09.2026: «далеко не всі
    /// вороги, або міні боси можуть здатися (полон добре бо можна собі потім його
    /// переманити)»). Здаються лише позначені рядові й міні-боси за показаним
    /// порогом; бос — ніколи; полонених годують, вмовляють і стережуть; віче
    /// переманює, бере викуп чи відпускає; переманений переживає сейв.
    /// </summary>
    public class SurrenderAndPrisonerTests
    {
        private static readonly BalanceConfig Cfg = new BalanceConfig();

        private static WeaponDefinition Club(int dmg) => new WeaponDefinition("club", "Club", Game.Core.Stats.SkillType.Melee)
        { DamageMin = dmg, DamageMax = dmg, ApCost = 2, OptimalRange = 1 };

        private static CombatUnit Enemy(string id, EnemyRank rank, bool canSurrender, int hp = 20, int at = 30)
        {
            var def = new EnemyDefinition("enemy." + id, id, EnemyRole.Breacher, EnemyFamily.Human)
            {
                MaxHp = hp, Rank = rank, CanSurrender = canSurrender, SurrenderAtHpPercent = at, Initiative = 1
            };
            return CombatUnit.FromEnemy(def, def.Id + "#0");
        }

        private static CombatUnit Hero(int dmg)
        {
            var p = new UnitProfile { DisplayName = "hero", MaxHp = 30, MaxAp = 8, Accuracy = 200, Initiative = 9, MoveApPerTile = 1 };
            return new CombatUnit("hero", Side.Player, p, Club(dmg));
        }

        private static CombatState Duel(CombatUnit enemy, int heroDamage)
        {
            var cs = new CombatState(new GridMap(4, 1), Cfg, new ThresholdRule(Cfg), null);
            cs.AddUnit(Hero(heroDamage), new GridPos(0, 0));
            cs.AddUnit(enemy, new GridPos(1, 0));
            cs.Begin();
            return cs;
        }

        // ---------------- бій ----------------

        [Test]
        public void MarkedGrunt_SurrendersAtTheShownThreshold_AndTheFightEnds()
        {
            var foe = Enemy("grunt", EnemyRank.Grunt, true, hp: 20, at: 30);
            var cs = Duel(foe, heroDamage: 15); // 20 → 5 (25% ≤ 30%)

            Assert.AreEqual(30, cs.SurrenderThresholdPercent(foe), "поріг видно заздалегідь");
            cs.Attack(foe.Id);

            Assert.AreEqual(UnitLifeState.Surrendered, foe.LifeState);
            Assert.Greater(foe.Hp, 0, "здався живим");
            Assert.AreEqual(CombatOutcome.Victory, cs.Outcome, "останній ворог здався — бій скінчено");
            Assert.IsTrue(cs.Journal.Any(e => e.Key == CombatLogKeys.Surrendered));

            var result = BattleResult.From(cs);
            Assert.AreEqual(1, result.SurrenderedEnemies.Count);
            Assert.AreEqual("enemy.grunt", result.SurrenderedEnemies[0].EnemyDefinitionId);
        }

        [Test]
        public void Boss_NeverSurrenders_EvenIfMarked()
        {
            var boss = Enemy("boss", EnemyRank.Boss, canSurrender: true, hp: 20, at: 90);
            var cs = Duel(boss, heroDamage: 15);
            cs.Attack(boss.Id);
            Assert.AreEqual(UnitLifeState.Active, boss.LifeState, "бос не здається ніколи");
            Assert.AreEqual(0, cs.SurrenderThresholdPercent(boss));
        }

        [Test]
        public void Unmarked_DoesNotSurrender_FarFromAll()
        {
            var foe = Enemy("stubborn", EnemyRank.Grunt, canSurrender: false);
            var cs = Duel(foe, heroDamage: 15);
            cs.Attack(foe.Id);
            Assert.AreEqual(UnitLifeState.Active, foe.LifeState, "«далеко не всі вороги»");
        }

        [Test]
        public void Suppressed_SurrendersSooner()
        {
            var foe = Enemy("scared", EnemyRank.Grunt, true, hp: 20, at: 30);
            var cs = Duel(foe, heroDamage: 11); // 20 → 9 (45%)
            cs.ApplyStatus(foe, StatusType.Suppressed);
            Assert.AreEqual(30 + Cfg.Combat.SuppressedSurrenderBonusPercent, cs.SurrenderThresholdPercent(foe));
            cs.Attack(foe.Id);
            Assert.AreEqual(UnitLifeState.Surrendered, foe.LifeState, "залякана ціль здається раніше (№14.2 × «Залякати»)");
        }

        // ---------------- реєстр полонених ----------------

        [Test]
        public void Ledger_PersuadedAndFed_BecomesReady_OnlyThroughBands()
        {
            var ledger = new PrisonerLedger();
            var p = ledger.Take("enemy.horde_scout", "horde_scout", 0, false, 1);
            var bands = new List<PrisonerDisposition>();
            for (int d = 2; d <= 5; d++)
            {
                ledger.Tick(new PrisonerDayInputs(d, bestPersuade: 5, guarded: true, fed: true));
                bands.Add(p.DispositionBand);
            }
            CollectionAssert.AreEqual(new[] { PrisonerDisposition.Hostile, PrisonerDisposition.Wavering, PrisonerDisposition.Wavering, PrisonerDisposition.Ready }, bands);
        }

        [Test]
        public void Ledger_Unguarded_PrisonerEscapes_WithASignal()
        {
            var ledger = new PrisonerLedger();
            ledger.Take("enemy.horde_scout", "horde_scout", 0, false, 1);
            var events = new List<PrisonerEvent>();
            for (int d = 2; d <= 7 && ledger.All.Count > 0; d++)
                events.AddRange(ledger.Tick(new PrisonerDayInputs(d, 0, guarded: false, fed: true)));
            Assert.AreEqual(0, ledger.All.Count, "без варти тікає");
            Assert.IsTrue(events.Any(e => e.Kind == "restless"), "перед утечею — неспокій (інваріант 4)");
            Assert.IsTrue(events.Any(e => e.Kind == "escaped"));
        }

        [Test]
        public void Ledger_Hunger_PushesTowardHostile()
        {
            var ledger = new PrisonerLedger();
            var p = ledger.Take("enemy.horde_scout", "horde_scout", 0, false, 1);
            ledger.Tick(new PrisonerDayInputs(2, 5, true, true));
            ledger.Tick(new PrisonerDayInputs(3, 5, true, true)); // 50 — вагається
            var events = ledger.Tick(new PrisonerDayInputs(4, 5, true, fed: false));
            Assert.IsTrue(events.Any(e => e.Kind == "hungry"));
            Assert.AreEqual(PrisonerDisposition.Wavering, p.DispositionBand);
        }

        [Test]
        public void Ledger_SurvivesCaptureRestore()
        {
            var a = new PrisonerLedger();
            a.Take("enemy.forest_bandit", "forest_bandit", 1, true, 3);
            a.Tick(new PrisonerDayInputs(4, 5, true, true));
            var b = new PrisonerLedger();
            b.RestoreState(a.CaptureState());
            Assert.AreEqual(1, b.All.Count);
            Assert.AreEqual("forest_bandit", b.All[0].DisplayName);
            Assert.IsTrue(b.All[0].NeverRecruitable);
            Assert.AreEqual(a.All[0].DispositionBand, b.All[0].DispositionBand);
        }

        // ---------------- сесія: рішення, віче, сейв ----------------

        private static GameSession Morning()
        {
            var s = new GameSession();
            s.NewGame(new NewGameOptions { SkipCreation = true, HitRule = HitRuleKind.Threshold });
            var step = s.AdvanceScene();
            while (!step.IsFinished) step = step.IsChoice ? s.ChooseSceneOption(0) : s.AdvanceScene();
            return s;
        }

        /// <summary>Єдина рефлексія у файлі: здача з'являється лише в справжньому бою, тут кладемо її напряму.</summary>
        private static void InjectSurrender(GameSession s, string unitId, string defId, string name, bool neverRecruitable = false)
        {
            var field = typeof(GameSession).GetField("_pendingSurrenders", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field);
            ((List<SurrenderedEnemy>)field.GetValue(s)).Add(new SurrenderedEnemy
            {
                UnitId = unitId, EnemyDefinitionId = defId, DisplayName = name, Rank = EnemyRank.Grunt, NeverRecruitable = neverRecruitable
            });
        }

        [Test]
        public void Session_Capture_Persuade_Recruit_AndTheRecruitSurvivesAFreshRestore()
        {
            var s = Morning();
            InjectSurrender(s, "enemy.horde_scout#1", "enemy.horde_scout", "horde_scout");
            Assert.AreEqual(1, s.GetPendingSurrenders().Count);

            Assert.IsTrue(s.DecideSurrender("enemy.horde_scout#1", SurrenderFate.Capture));
            Assert.AreEqual(0, s.GetPendingSurrenders().Count);
            var prisoner = s.GetPrisonersView().Single();
            Assert.AreEqual("Hostile", prisoner.Disposition);
            Assert.IsFalse(prisoner.CanRecruitNow);

            BotRunner.Drive(s, new HomebodyPolicy(), 5);
            prisoner = s.GetPrisonersView().SingleOrDefault();
            Assert.IsNotNull(prisoner, "за п'ять діб з варти чи без — ще не втік (або тест треба переглянути)");
            Assert.AreEqual("Ready", prisoner.Disposition, "найкраще Переконання в громаді вмовило");

            Assert.IsTrue(s.RecruitPrisoner(prisoner.Id));
            Assert.AreEqual(0, s.GetPrisonersView().Count);
            string recruitId = s.GetRosterView().Companions.Select(c => c.Id).Single(id => id.StartsWith("recruit_"));

            string blob = s.SaveState(0);
            var fresh = new GameSession();
            fresh.NewGame(new NewGameOptions { SkipCreation = true, HitRule = HitRuleKind.Threshold });
            fresh.RestoreFromBlob(blob);
            Assert.IsTrue(fresh.GetRosterView().Companions.Any(c => c.Id == recruitId), "переманений переживає відновлення у свіжу сесію");
        }

        [Test]
        public void Session_NeverRecruitable_CanBeRansomed_ButNotRecruited()
        {
            var s = Morning();
            InjectSurrender(s, "u#1", "enemy.horde_scout", "horde_scout", neverRecruitable: true);
            s.DecideSurrender("u#1", SurrenderFate.Capture);
            var p = s.GetPrisonersView().Single();
            Assert.IsTrue(p.NeverRecruitable);
            Assert.IsFalse(s.RecruitPrisoner(p.Id), "правило кастингу №12.9");

            int gold = s.GetEconomyView().Gold;
            Assert.IsTrue(s.RansomPrisoner(p.Id));
            Assert.AreEqual(gold + p.RansomGold, s.GetEconomyView().Gold, "викуп — у казну, сума була видна до кліку");
        }

        [Test]
        public void Session_Undecided_AreReleasedNextDay_AndExecutionCostsBlood()
        {
            var s = Morning();
            InjectSurrender(s, "a#1", "enemy.horde_scout", "horde_scout");
            InjectSurrender(s, "b#1", "enemy.horde_scout", "horde_scout");
            Assert.IsTrue(s.DecideSurrender("b#1", SurrenderFate.Execute));
            Assert.IsTrue(s.DayLog.Any(e => e.Key == "enemy.executed"));

            BotRunner.Drive(s, new HomebodyPolicy(), 1);
            Assert.AreEqual(0, s.GetPendingSurrenders().Count, "невирішених відпускають на наступну добу");
        }
    }
}
