using System.IO;
using System.Reflection;
using Game.Core.Ring;
using Game.Core.Ring.Match;
using Game.Gameplay.Ring.Models;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Довжина матчу 8–15 дій (P2-1, 11.11.2026): підйом не рахується в лічильнику, фінішер — з FinisherMinRound-ї дії,
    /// пізня стадія росте. Розподіл довжин на 1000 матчів міряє <see cref="MatchFeelTests"/>.
    /// </summary>
    public sealed class MatchLengthTests
    {
        [Test]
        public void GetUp_StaysAnAction_ButDoesNotCountTowardsMatchLength()
        {
            var e = MatchKit.Engine(true, new[] { 11 });
            e.State.Player.Posture = Posture.Down;
            int round = e.State.Round, logged = e.State.Log.Count;
            MatchKit.Play(e, MatchActionId.GetUp);
            Assert.AreEqual(round, e.State.Round, "підйом не збільшує лічильник дій матчу");
            Assert.Greater(e.State.Log.Count, logged, "але лишається подією журналу");
            int gets = 0;
            foreach (var ev in e.State.Log) if (ev.Kind == MatchEventKind.Action && ev.Action == MatchActionId.GetUp) gets++;
            Assert.AreEqual(1, gets);
        }

        [Test]
        public void OtherActions_StillCount()
        {
            var e = MatchKit.Engine(true, new[] { 11 });
            int round = e.State.Round;
            MatchKit.Play(e, MatchActionId.Strike);
            Assert.AreEqual(round + 1, e.State.Round);
        }

        [Test]
        public void Finisher_IsTooEarlyBeforeTheMinimumAction_AndOpensAtIt()
        {
            var e = MatchKit.Engine(true);
            e.State.Player.Momentum = 5; e.State.Hype = 4; e.State.Rival.Damage = 6;
            e.State.Round = MatchRules.FinisherMinRound - 1;
            Assert.AreEqual(UnavailableReason.TooEarly, e.Describe(MatchActionId.Finisher).Reason);
            Assert.IsFalse(e.Describe(MatchActionId.Finisher).Available);
            e.State.Round = MatchRules.FinisherMinRound;
            Assert.IsTrue(e.Describe(MatchActionId.Finisher).Available);
        }

        /// <summary>
        /// Мінімум дії вимикає лише явне поле показу (демо), а не довгий ліміт дій. Раніше вимикав неявний прапорець
        /// «ліміт понад матч»; рев'ю D 11.11.2026.
        /// </summary>
        [Test]
        public void FinisherMinimum_OffOnlyByTheExplicitDemoFlag_NotByALongRoundLimit()
        {
            var longLimit = MatchKit.Engine(true, null, s => s.RoundLimit = 400);
            longLimit.State.Player.Momentum = 5; longLimit.State.Hype = 4; longLimit.State.Rival.Damage = 6;
            Assert.AreEqual(UnavailableReason.TooEarly, longLimit.Describe(MatchActionId.Finisher).Reason, "довгий ліміт — правило те саме");

            var demo = MatchKit.Engine(true, null, s => s.NoFinisherMinimum = true);
            demo.State.Player.Momentum = 5; demo.State.Hype = 4; demo.State.Rival.Damage = 6;
            Assert.IsTrue(demo.Describe(MatchActionId.Finisher).Available, "демо — фінішер з першої дії");
            Assert.IsTrue(Game.Gameplay.Ring.Stage.RingDemoScript.Setup(false).NoFinisherMinimum, "демо ставить прапорець саме");
        }

        [Test]
        public void TooEarly_HasAReadableText_WithTheNumber()
        {
            string dir = RingCampaignDriver.Dir();
            var t = RingText.FromJson(File.ReadAllText(Path.Combine(dir, "strings_uk.json")), File.ReadAllText(Path.Combine(dir, "ui_uk.json")));
            string s = RingLabels.Unavailable(t, UnavailableReason.TooEarly);
            Assert.IsFalse(s.Contains("⟦"), s);
            StringAssert.Contains(MatchRules.FinisherMinRound.ToString(), s);
        }

        [Test]
        public void LateStage_StartsAtItsRound_AndGrowsByOnePerAction()
        {
            var e = MatchKit.Engine(true);
            var late = typeof(MatchEngine).GetMethod("Late", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(late, "приватний Late() зник — оновити тест");
            e.State.Round = MatchRules.LateStageRound - 1;
            Assert.AreEqual(0, (int)late.Invoke(e, null));
            e.State.Round = MatchRules.LateStageRound;
            Assert.AreEqual(MatchRules.LateStagePenalty, (int)late.Invoke(e, null));
            e.State.Round = MatchRules.LateStageRound + 3;
            Assert.AreEqual(MatchRules.LateStagePenalty + 3 / MatchRules.LateStageStep, (int)late.Invoke(e, null));
        }
    }
}
