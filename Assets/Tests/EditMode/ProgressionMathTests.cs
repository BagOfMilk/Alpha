using Game.Core.Balance;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    public class ProgressionMathTests
    {
        private BalanceConfig Cfg() => new BalanceConfig
        {
            XpBase = 100, XpExponent = 1.5, MaxLevel = 20, StatPointsPerLevel = 3
        };

        [Test]
        public void XpToNext_GrowsWithLevel()
        {
            var cfg = Cfg();
            Assert.Less(ProgressionMath.XpToNext(1, cfg), ProgressionMath.XpToNext(2, cfg));
            Assert.Less(ProgressionMath.XpToNext(2, cfg), ProgressionMath.XpToNext(5, cfg));
        }

        [Test]
        public void GrantXp_SingleLevelUp()
        {
            var cfg = Cfg();
            int need = ProgressionMath.XpToNext(1, cfg); // 100
            var r = ProgressionMath.GrantXp(1, 0, need, cfg);
            Assert.AreEqual(2, r.Level);
            Assert.AreEqual(1, r.LevelsGained);
            Assert.AreEqual(0, r.RemainderXp);
        }

        [Test]
        public void GrantXp_MultiLevel_CarriesRemainder()
        {
            var cfg = Cfg();
            int need1 = ProgressionMath.XpToNext(1, cfg);
            int need2 = ProgressionMath.XpToNext(2, cfg);
            var r = ProgressionMath.GrantXp(1, 0, need1 + need2 + 5, cfg);
            Assert.AreEqual(3, r.Level);
            Assert.AreEqual(2, r.LevelsGained);
            Assert.AreEqual(5, r.RemainderXp);
        }

        [Test]
        public void GrantXp_DoesNotExceedMaxLevel()
        {
            var cfg = Cfg();
            cfg.MaxLevel = 3;
            var r = ProgressionMath.GrantXp(1, 0, 1_000_000, cfg);
            Assert.AreEqual(3, r.Level);
            Assert.AreEqual(0, r.RemainderXp);
        }
    }
}
