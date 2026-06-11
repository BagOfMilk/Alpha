using Game.Core.Balance;
using Game.Core.Characters;
using Game.Core.Health;
using Game.Core.Stats;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>Шрамы: присваиваются Серьёзным+ ранением, вечны, не ломают слоты трейтов (US-2.5 / 4.2 / 4.3).</summary>
    public class ScarTrackTests
    {
        private static Companion Make(int str, int agi, int wits, int will)
            => new Companion("c", new AttributeBlock(str, agi, wits, will), 4);

        [Test]
        public void SeriousInjury_AssignsScar_AndEffectApplies()
        {
            var cfg = new BalanceConfig();
            var c = Make(3, 5, 3, 3); // база Accuracy = 50 + 5*2 = 60
            var scar = new Scar("one_eye", "Одноглазый").WithCombat(DerivedStat.Accuracy, -10);

            bool scarred = c.ApplyInjury(InjuryTier.Serious, cfg, scar);

            Assert.IsTrue(scarred);
            Assert.IsTrue(c.Scars.Has("one_eye"));
            Assert.AreEqual(50, c.GetDerived(DerivedStat.Accuracy, cfg)); // 60 − 10
            Assert.AreEqual(0, c.Traits.UsedSlots); // шрам НЕ занимает слот трейта
        }

        [Test]
        public void LightInjury_GivesNoScar()
        {
            var cfg = new BalanceConfig();
            var c = Make(3, 3, 3, 3);
            bool scarred = c.ApplyInjury(InjuryTier.Light, cfg, new Scar("x", "X"));
            Assert.IsFalse(scarred);
            Assert.AreEqual(0, c.Scars.Count);
        }

        [Test]
        public void InjuryTier_MapsToRecoveryDays()
        {
            var cfg = new BalanceConfig();
            var c = Make(3, 3, 3, 3);
            c.ApplyInjury(InjuryTier.Critical, cfg, null);
            Assert.AreEqual(cfg.InjuryDaysCritical, c.RecoveryDaysRemaining);
            Assert.AreEqual(InjuryTier.Critical, c.CurrentInjury);
            Assert.AreEqual(CompanionStatus.Injured, c.Status);
        }

        [Test]
        public void Scars_ArePermanent_TrackOnlyAccumulates()
        {
            // Несбрасываемость гарантирована отсутствием метода удаления (компайл-тайм);
            // здесь фиксируем, что шрамы накапливаются и не теряются.
            var track = new ScarTrack();
            track.Add(new Scar("a", "A"));
            track.Add(new Scar("b", "B"));
            Assert.AreEqual(2, track.Count);
            Assert.IsTrue(track.Has("a") && track.Has("b"));
        }
    }
}
