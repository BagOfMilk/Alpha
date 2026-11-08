using System.Linq;
using Game.Core.Ring;
using Game.Core.Ring.Match;
using Game.Gameplay.Combat;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>«Промінь надії»: слабший, що сильно відстає, раз за матч робить великий прийом, який не може закінчити матч.</summary>
    public class HopeSpotTests
    {
        private static MatchSetup Setup(bool hope)
        {
            var s = new MatchSetup
            {
                Player = new WrestlerStats("p", "p"), Rival = new WrestlerStats("r", "r"), StartHype = 4, PlayerRole = RingPath.Face,
                RivalRole = RingPath.Heel, HopeSpotEnabled = hope
            };
            return s;
        }

        /// <summary>Рушій, де першим ходить гравець (підбір зерна — кубик детермінований).</summary>
        private static MatchEngine PlayerFirst(bool hope, ulong from = 1)
        {
            for (ulong seed = from; seed < from + 200; seed++)
            {
                var e = new MatchEngine(Setup(hope), new SeededDiceRoller(seed));
                if (e.CurrentActor == MatchSide.Player) return e;
            }
            throw new System.InvalidOperationException("не знайшов зерно");
        }

        [Test]
        public void NotOffered_WhenMatchDidNotEnableIt()
        {
            var e = PlayerFirst(false);
            e.State.Player.Damage = 9; e.State.Rival.Damage = 0;
            Assert.IsFalse(e.HopeSpotAvailable);
            Assert.AreEqual(UnavailableReason.NotFarBehind, e.Describe(MatchActionId.HopeSpot).Reason);
        }

        [Test]
        public void NotInTheDefaultActionList_SoOldScreensDoNotChange()
        {
            var e = PlayerFirst(true);
            // Усі дії рушія, крім самого «Променя надії» (число дій змінювалось: 08.11.2026 прибрано зони, додано «Перевести подих»).
            Assert.AreEqual(System.Enum.GetValues(typeof(MatchActionId)).Length - 1, e.GetAvailableActions().Count);
            Assert.IsFalse(e.GetAvailableActions().Any(o => o.Id == MatchActionId.HopeSpot));
        }

        [Test]
        public void Threshold_NeedsDamageAndGap()
        {
            var e = PlayerFirst(true);
            e.State.Player.Damage = 5; e.State.Rival.Damage = 0;   // розрив 5, але ушкодження < 6
            Assert.IsFalse(e.HopeSpotAvailable);
            e.State.Player.Damage = 8; e.State.Rival.Damage = 5;   // ушкодження 8, розрив лише 3
            Assert.IsFalse(e.HopeSpotAvailable);
            Assert.AreEqual(UnavailableReason.NotFarBehind, e.Describe(MatchActionId.HopeSpot).Reason);
            e.State.Player.Damage = 8; e.State.Rival.Damage = 4;   // розрив 4 — рівно поріг
            Assert.IsTrue(e.HopeSpotAvailable);
        }

        [Test]
        public void Perform_KnocksTheLeaderDown_NeverEndsTheMatch_AndIsOncePerMatch()
        {
            int done = 0;
            for (ulong seed = 1; seed < 150; seed++)
            {
                var e = new MatchEngine(Setup(true), new SeededDiceRoller(seed));
                if (e.CurrentActor != MatchSide.Player) continue;
                e.State.Player.Damage = 9; e.State.Rival.Damage = 1;
                int hype = e.State.Hype;
                int rivalDmg = e.State.Rival.Damage;
                Assert.IsTrue(e.HopeSpotAvailable, "seed " + seed);
                e.Perform(MatchActionId.HopeSpot);
                Assert.AreEqual(MatchPhase.AwaitBoost, e.Phase);
                e.Commit(0);
                Assert.IsFalse(e.IsOver, "промінь надії не закінчує матч, seed " + seed);
                Assert.AreEqual(Posture.Down, e.State.Rival.Posture);
                Assert.Greater(e.State.Rival.Damage, rivalDmg);
                Assert.AreEqual(Posture.Standing, e.State.Player.Posture);
                Assert.GreaterOrEqual(e.State.Hype, hype + 2, "ажіотаж +2 чи +3");
                Assert.IsTrue(e.State.Player.HopeUsed);
                if (e.CurrentActor == MatchSide.Player && e.Phase == MatchPhase.AwaitAction)
                {
                    Assert.IsFalse(e.HopeSpotAvailable);
                    Assert.AreEqual(UnavailableReason.AlreadyUsed, e.Describe(MatchActionId.HopeSpot).Reason);
                }
                done++;
            }
            Assert.GreaterOrEqual(done, 20);
        }

        [Test]
        public void EvenAMissedRoll_IsAtLeastAPartialSuccess_AndKeepsTheTurn()
        {
            bool sawMiss = false;
            for (ulong seed = 1; seed < 400 && !sawMiss; seed++)
            {
                var e = new MatchEngine(Setup(true), new SeededDiceRoller(seed));
                if (e.CurrentActor != MatchSide.Player) continue;
                e.State.Player.Damage = 9; e.State.Rival.Damage = 0;
                e.Perform(MatchActionId.HopeSpot);
                if (e.PendingRoll.Tier != RollTier.Miss) continue;
                e.Commit(0);
                sawMiss = true;
                Assert.AreEqual(Posture.Down, e.State.Rival.Posture, "надія не підводить цілком");
                Assert.AreEqual(e.CurrentActor, MatchSide.Player, "невдалий кидок не віддає хід");
            }
            Assert.IsTrue(sawMiss);
        }

        [Test]
        public void CostsNoStamina_AvailableEvenWinded()
        {
            var e = PlayerFirst(true);
            e.State.Player.Damage = 9; e.State.Rival.Damage = 0;
            Assert.AreEqual(0, e.Describe(MatchActionId.HopeSpot).Cost);
            e.State.Player.Stamina = 0;
            Assert.IsTrue(e.Describe(MatchActionId.HopeSpot).Available);
            Assert.IsFalse(e.Describe(MatchActionId.HopeSpot).Winded, "промінь надії без штрафу «Сил»");
        }

        [Test]
        public void Ai_UsesItWhenFarBehind_OnlyWhenEnabled()
        {
            var e = new MatchEngine(Setup(true), new SeededDiceRoller(5));
            e.State.CurrentActor = MatchSide.Rival;
            e.State.Rival.Damage = 9; e.State.Player.Damage = 0;
            Assert.AreEqual(MatchActionId.HopeSpot, MatchAi.ChooseAction(e));
            var off = new MatchEngine(Setup(false), new SeededDiceRoller(5));
            off.State.CurrentActor = MatchSide.Rival;
            off.State.Rival.Damage = 9; off.State.Player.Damage = 0;
            Assert.AreNotEqual(MatchActionId.HopeSpot, MatchAi.ChooseAction(off));
        }

        [Test]
        public void FullAiMatches_WithHopeSpotEnabled_AllFinish_AndHopeIsUsedSometimes()
        {
            int used = 0;
            for (ulong seed = 1; seed <= 120; seed++)
            {
                var e = new MatchEngine(Setup(true), new SeededDiceRoller(seed));
                var r = MatchAutoplay.Run(e, true);
                Assert.IsNotNull(r);
                if (e.State.Player.HopeUsed || e.State.Rival.HopeUsed) used++;
            }
            Assert.Greater(used, 0, "у повних матчах промінь надії хоч раз спрацьовує");
        }

        [Test]
        public void HopeSpot_IsAppendedToTheEnum_SoSavesAndScreensKeepWorking()
        {
            Assert.AreEqual(17, (int)MatchActionId.HopeSpot);
            Assert.AreEqual(16, (int)MatchActionId.CatchBreath);
        }
    }
}
