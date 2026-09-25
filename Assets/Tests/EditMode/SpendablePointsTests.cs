using Game.Core.Balance;
using Game.Core.Characters;
using Game.Core.Characters.Build;
using Game.Core.Characters.Progression;
using Game.Core.Stats;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Банк очок протагоніста (R11/G19): <c>Companion.GainXp</c> витрачає очки
    /// скілів одразу — <c>BuildPlanner</c> лишався недосяжним, бо очок ніде
    /// не накопичувалось. Протагоніст банкує через
    /// <see cref="Companion.GainXpNoAutoSpend"/> + <see cref="SpendablePoints"/>,
    /// напарники, як і раніше, тратять авто (регресія на обидва шляхи).
    /// </summary>
    public class SpendablePointsTests
    {
        private static BalanceConfig Cfg() => new BalanceConfig();

        private static Companion Fresh(string id, BalanceConfig cfg) =>
            new CompanionArchetype(id, id).CreateInstance(id, cfg);

        [Test]
        public void Protagonist_BanksPoints_SkillsUnchangedUntilSpent()
        {
            var cfg = Cfg();
            var protagonist = Fresh("protagonist", cfg);
            var bank = new SpendablePoints();

            int before = protagonist.Skill(SkillType.Persuade);
            var levelUp = protagonist.GainXpNoAutoSpend(1000, cfg); // свідомо вистачить на рівень
            Assert.IsTrue(levelUp.LeveledUp, "предпосылка теста: должен вырасти хоть один уровень");

            bank.Grant(protagonist.Id, levelUp.LevelsGained * cfg.SkillPointsPerLevel);

            Assert.AreEqual(before, protagonist.Skill(SkillType.Persuade),
                "скилы протагониста НЕ трогаются автоматически — это и есть смысл R11");
            Assert.Greater(bank.Get(protagonist.Id), 0, "очки легли в банк, а не потерялись");
        }

        [Test]
        public void Companion_KeepsAutoSpend_UnlikeProtagonist()
        {
            var cfg = Cfg();
            // Ростовому профілю потрібна хоч одна вага — інакше AllocatePoints
            // чесно роздає нуль усім, і тест нічого б не перевірив.
            var arch = new CompanionArchetype("maksym", "maksym").SetGrowth(SkillType.Melee, 1.0);
            var companion = arch.CreateInstance("maksym_1", cfg);
            int totalBefore = companion.Skills.TotalPoints;

            var levelUp = companion.GainXp(1000, cfg);

            Assert.IsTrue(levelUp.LeveledUp);
            Assert.Greater(companion.Skills.TotalPoints, totalBefore,
                "напарник, в отличие от протагониста, тратит очки скилов сразу же (US-2.1 не меняется)");
        }

        [Test]
        public void BankedPoints_FeedBuildPlannerCommit_ThenAreSpent()
        {
            var cfg = Cfg();
            var protagonist = Fresh("protagonist", cfg);
            var bank = new SpendablePoints();

            var levelUp = protagonist.GainXpNoAutoSpend(1000, cfg);
            bank.Grant(protagonist.Id, levelUp.LevelsGained * cfg.SkillPointsPerLevel);
            int available = bank.Get(protagonist.Id);
            Assert.Greater(available, 0);

            var plan = new BuildPlan().Invest(SkillType.Persuade, 1);
            int before = protagonist.Skill(SkillType.Persuade);

            var status = BuildPlanner.Commit(protagonist, plan, available, null, cfg, confirmedIrreversible: true);

            Assert.AreEqual(BuildPlanStatus.Ok, status);
            Assert.AreEqual(before + 1, protagonist.Skill(SkillType.Persuade));

            bool spent = bank.Spend(protagonist.Id, 1);
            Assert.IsTrue(spent);
            Assert.AreEqual(available - 1, bank.Get(protagonist.Id));
        }

        [Test]
        public void Spend_MoreThanAvailable_IsRefused_AndBankUnchanged()
        {
            var bank = new SpendablePoints();
            bank.Grant("hero", 2);

            Assert.IsFalse(bank.Spend("hero", 5));
            Assert.AreEqual(2, bank.Get("hero"), "отказ атомарный — банк не тронут");
        }

        [Test]
        public void SaveAndRestore_RoundTrips()
        {
            var bank = new SpendablePoints();
            bank.Grant("hero", 5);
            bank.Grant("second", 2);

            string blob = bank.CaptureState();

            var restored = new SpendablePoints();
            restored.RestoreState(blob);

            Assert.AreEqual(5, restored.Get("hero"));
            Assert.AreEqual(2, restored.Get("second"));
        }

        [Test]
        public void UnknownCompanion_HasZeroPoints()
        {
            var bank = new SpendablePoints();
            Assert.AreEqual(0, bank.Get("nobody"));
        }
    }
}
