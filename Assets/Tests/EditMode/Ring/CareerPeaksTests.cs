using Game.Core.Ring;
using Game.Core.Ring.Career;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Піки сили (власник 06.11.2026: «Піки 70–80%»; рев'ю ПМ 12.11.2026): реванш і «переріс щабель картки» — суперник слабший за
    /// знімок на щаблі рівня федерації, щаблі можуть бути від'ємними (до <see cref="CareerRules.RivalStepsFloor"/>) — тоді суперник
    /// слабший за базу ростеру. Числа — ПЛЕЙСХОЛДЕР у CareerRules, тести беруть їх звідти; самі відсотки піків тримає CareerTempoTests.
    /// </summary>
    public class CareerPeaksTests
    {
        private static int Sum(WrestlerStats s) { int n = 0; for (int i = 0; i < 5; i++) n += s.Get((RingAttr)i); return n; }

        private static WrestlerStats Stats(int str, int agi, int tech, int sta, int cha, RingAttr finisher)
        {
            var s = new WrestlerStats("t", "t") { FinisherAttr = finisher };
            s.Set(RingAttr.Strength, str); s.Set(RingAttr.Agility, agi); s.Set(RingAttr.Technique, tech);
            s.Set(RingAttr.Stamina, sta); s.Set(RingAttr.Charisma, cha);
            return s;
        }

        [Test]
        public void Weaken_LowersFromTheEndOfTheQueue_KeepsTheFinisher_NotBelowZero()
        {
            // черга посилення — фінішер, Витривалість, Техніка, Сила; послаблення — з кінця: Сила → Техніка → Витривалість
            var s = Stats(2, 3, 1, 3, 3, RingAttr.Technique);
            Assert.AreEqual(4, ShowBooker.Weaken(s, 4));
            Assert.AreEqual(0, s.Get(RingAttr.Strength));
            Assert.AreEqual(1, s.Get(RingAttr.Technique), "атрибут фінішера не чіпаємо");
            Assert.AreEqual(1, s.Get(RingAttr.Stamina));
            Assert.AreEqual(3, s.Get(RingAttr.Agility), "Спритність і Харизма — поза чергою");
            Assert.AreEqual(3, s.Get(RingAttr.Charisma));
            Assert.AreEqual(1, ShowBooker.Weaken(s, 5), "далі — лише до нуля");
            Assert.AreEqual(0, s.Get(RingAttr.Stamina));
            Assert.AreEqual(0, ShowBooker.Weaken(s, 3));

            // від'ємні щаблі в Strengthen — те саме, витримка (mods) не змінюється
            var t = Stats(1, 1, 2, 2, 1, RingAttr.Strength);
            var mods = new MatchModifiers();
            Assert.AreEqual(-2, ShowBooker.Strengthen(t, -2, mods));
            Assert.AreEqual(1, t.Get(RingAttr.Strength));
            Assert.AreEqual(0, t.Get(RingAttr.Technique));
            Assert.AreEqual(2, t.Get(RingAttr.Stamina));
            Assert.AreEqual(0, mods.KickOutBonus + mods.DamageThresholdShift + mods.ExtraReversalTokens);
        }

        /// <summary>Герой, що виріс щонайменше на PeakLag щаблів (усі атрибути в межі), на тижні федерації <paramref name="tier"/>.</summary>
        private static CareerSession Grown(ulong seed, int tier)
        {
            var s = CareerKit.NewHero(seed);
            if (tier != s.FederationTier) s.EnterFederation(tier, s.Popularity, s.Trust);
            for (int i = 0; i < 5; i++) s.Player.Set((RingAttr)i, WrestlerStats.MaxAttr);
            Assert.GreaterOrEqual(s.PlayerGrowth(), CareerRules.PeakLag, "передумова: гравець виріс");
            Assert.AreEqual(CareerStage.Week, s.Stage);
            Assert.IsFalse(s.State.IsBig);
            return s;
        }

        [Test]
        public void Peaks_RematchAndOutgrown_WeakenTheRival_ByTier_DownToTheFloor()
        {
            for (int tier = 1; tier <= CareerContent.MaxTier; tier++)
            {
                string at = "рівень " + tier + ": ";
                var s = Grown((ulong)(40 + tier), tier);
                var pos = s.State.Position;
                int growth = s.PlayerGrowth();

                // знімок щабля — поточний ріст: не пік
                s.State.TierPos = (int)pos; s.State.TierGrowth = growth;
                Assert.IsFalse(s.IsOutgrown, at + "знімок свіжий");
                Assert.AreEqual(CareerRules.RivalSteps(pos, growth, tier), s.RivalStepsNow(), at + "щотижневі — за місцем, ростом і рівнем");

                // виріс на PeakLag після знімка, підвищення не було — «переріс щабель»
                int snap = growth - CareerRules.PeakLag;
                s.State.TierGrowth = snap;
                Assert.IsTrue(s.IsOutgrown, at + "переріс щабель");
                int outgrown = CareerRules.RivalSteps(pos, snap, tier) - CareerRules.PeakSteps(tier);
                Assert.AreEqual(System.Math.Max(CareerRules.RivalStepsFloor, outgrown), s.RivalStepsNow(), at + "переріс — суперник слабший за знімок");
                var card = s.GetShowCard();
                Assert.IsTrue(card.IsOutgrown);
                Assert.IsFalse(card.IsRematch);
                Assert.AreEqual(s.RivalStepsNow(), card.RivalSteps, at + "картка показує щаблі наперед");
                s.State.TierGrowth = snap + 1;
                Assert.IsFalse(s.IsOutgrown, at + "виріс на щабель — ще не пік");

                // реванш — свій пік, «переріс» поверх нього не додається
                s.State.TierGrowth = snap;
                s.State.Grudges.Add(s.State.Opponent);
                Assert.IsTrue(s.IsRematch);
                int rematch = CareerRules.RivalSteps(pos, snap, tier) - CareerRules.RematchSteps(tier);
                Assert.AreEqual(System.Math.Max(CareerRules.RivalStepsFloor, rematch), s.RivalStepsNow(), at + "реванш");
                card = s.GetShowCard();
                Assert.IsTrue(card.IsRematch);
                Assert.IsFalse(card.IsOutgrown, at + "один пік за вечір");

                // велике шоу — кульмінація: «переріс» не діє
                s.State.Grudges.Clear();
                s.State.IsBig = true;
                Assert.IsFalse(s.IsOutgrown, at + "велике шоу");
                s.State.IsBig = false;
            }
        }

        [Test]
        public void Rematch_OnTheLocalTier_RivalBelowTheRosterBase_FloorHolds()
        {
            // місцева, перший тиждень: суперник без щаблів — реванш опускає його нижче бази ростеру (було — обрізало до нуля)
            var s = CareerKit.NewHero(51);
            Assert.AreEqual(1, s.FederationTier);
            s.State.Grudges.Add(s.State.Opponent);
            int expected = System.Math.Max(CareerRules.RivalStepsFloor,
                CareerRules.RivalSteps(s.State.Position, s.State.TierGrowth, 1) - CareerRules.RematchSteps(1));
            Assert.Less(expected, 0, "передумова: реванш нижче нуля");
            Assert.AreEqual(expected, s.RivalStepsNow());
            Assert.GreaterOrEqual(s.RivalStepsNow(), CareerRules.RivalStepsFloor);

            var npc = s.Content.FindNpc(s.State.Opponent);
            s.EndWeek();
            var e = s.StartShow();
            Assert.Less(Sum(e.Setup.Rival), Sum(npc.Stats), "суперник слабший за базу ростеру");
            Assert.AreEqual(npc.Stats.Get(npc.Stats.FinisherAttr), e.Setup.Rival.Get(npc.Stats.FinisherAttr), "атрибут фінішера — як у ростері");
        }
    }
}
