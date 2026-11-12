using System.Collections.Generic;
using System.Linq;
using Game.Core.Ring;
using Game.Core.Ring.Match;
using Game.Gameplay.Combat;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Бій за рингом (власник 08.11.2026: «Лишити в ітерації 1 склейкою»; TRPG_DESIGN §4.4, §4.9, §4.11). Пара або на рингу, або за
    /// рингом — разом. Вихід і повернення показуються склейкою. Відлік: +3 за кожну дію за рингом. На 10 програє той, хто лежить;
    /// лежать обидва або стоять обидва — подвійний відлік. У матчі «без DQ» відліку немає, натомість є предмети.
    /// </summary>
    public class MatchOutsideTests
    {
        private static UnavailableReason Why(MatchEngine e, MatchActionId id) { return e.Describe(id).Reason; }

        private static void NoDq(MatchSetup s) { s.Stipulations = MatchStipulations.NoDq; }

        [Test]
        public void LeaveRing_Strong_PairOutside_RivalOnTheFloor_RingOnlyMovesClosed()
        {
            var e = MatchKit.Engine(true, new[] { 12 });
            Assert.AreEqual(UnavailableReason.NotOutside, Why(e, MatchActionId.ReturnToRing));
            Assert.AreEqual(UnavailableReason.NotOutside, Why(e, MatchActionId.Weapon));
            MatchKit.Play(e, MatchActionId.LeaveRing);
            Assert.IsTrue(e.State.Outside);
            Assert.AreEqual(Posture.Down, e.State.Rival.Posture, "10+ — суперник на підлозі");
            Assert.AreEqual(1, e.State.Rival.Damage);
            Assert.AreEqual(MatchSide.Player, e.CurrentActor, "ініціатива лишається");
            var act = e.State.Log.Last(x => x.Kind == MatchEventKind.Action);
            Assert.IsFalse(act.Outside, "дія почалась на рингу");
            Assert.AreEqual(1, act.EffectSum(MatchEffectKind.Place, MatchSide.Player), "склейка: пара за рингом");
            Assert.AreEqual(0, e.State.OutsideCount, "сам вихід відліку не додає");

            foreach (var id in new[] { MatchActionId.Pin, MatchActionId.Submission, MatchActionId.Finisher, MatchActionId.TopRope })
                Assert.AreEqual(UnavailableReason.NotInRing, Why(e, id), id.ToString());
            Assert.AreEqual(UnavailableReason.AlreadyOutside, Why(e, MatchActionId.LeaveRing));
            Assert.AreEqual(UnavailableReason.NeedNoDq, Why(e, MatchActionId.Weapon), "предмети — лише «без DQ»");
            Assert.IsTrue(e.Describe(MatchActionId.ReturnToRing).Available);
            Assert.IsTrue(e.Describe(MatchActionId.Taunt).Available, "дражнити лежачого — можна й за рингом");
        }

        [Test]
        public void LeaveRing_Miss_StaysInRing_InitiativePasses()
        {
            var e = MatchKit.Engine(true, new[] { 2 });
            MatchKit.Play(e, MatchActionId.LeaveRing);
            Assert.IsFalse(e.State.Outside);
            Assert.AreEqual(1, e.State.Player.Damage, "контратака");
            Assert.AreEqual(MatchSide.Rival, e.CurrentActor);
            Assert.IsTrue(e.State.Player.LeftRing, "спроба теж рахується — ШІ не виводить удруге");
        }

        [Test]
        public void Count_ReachesTen_TheOneLyingIsCountedOut()
        {
            // вихід 10+ (суперник лежить), далі «дражнити» 10+ чотири рази: відлік 3, 6, 9, 12 — суперник не встав
            var e = MatchKit.Engine(true, new[] { 12, 12, 12, 12, 12 });
            MatchKit.Play(e, MatchActionId.LeaveRing);
            for (int i = 1; i <= 3; i++)
            {
                MatchKit.Play(e, MatchActionId.Taunt);
                Assert.IsFalse(e.IsOver);
                Assert.AreEqual(3 * i, e.State.OutsideCount);
                Assert.IsTrue(e.State.Log.Last(x => x.Kind == MatchEventKind.Action).Outside, "дія за рингом");
            }
            MatchKit.Play(e, MatchActionId.Taunt);
            Assert.IsTrue(e.IsOver);
            Assert.AreEqual(MatchEndKind.CountOut, e.Result.Kind);
            Assert.AreEqual(MatchWinner.Player, e.Result.Winner, "лежав суперник — його й відрахували");
            Assert.AreEqual(12, e.State.Log.Sum(x => x.EffectSum(MatchEffectKind.Count, MatchSide.Player)));
        }

        [Test]
        public void Count_ReachesTen_BothStanding_DoubleCountOut()
        {
            // вихід 7–9 (суперник стоїть), далі чотири удари 7–9: обидва на ногах, коли рефері дорахував до 10
            var e = MatchKit.Engine(true, new[] { 7, 7, 7, 7, 7 });
            MatchKit.Play(e, MatchActionId.LeaveRing);
            Assert.IsTrue(e.State.Outside);
            Assert.AreEqual(Posture.Standing, e.State.Rival.Posture);
            for (int i = 0; i < 4; i++) MatchKit.Play(e, MatchActionId.Strike);
            Assert.IsTrue(e.IsOver);
            Assert.AreEqual(MatchEndKind.DoubleCountOut, e.Result.Kind);
            Assert.AreEqual(MatchWinner.Draw, e.Result.Winner);
        }

        [Test]
        public void ReturnToRing_ResetsTheCount_KeepsInitiative()
        {
            var e = MatchKit.Engine(true, new[] { 12, 12 });
            MatchKit.Play(e, MatchActionId.LeaveRing);
            MatchKit.Play(e, MatchActionId.Taunt);
            Assert.AreEqual(3, e.State.OutsideCount);
            MatchKit.Play(e, MatchActionId.ReturnToRing);
            Assert.IsFalse(e.State.Outside);
            Assert.AreEqual(0, e.State.OutsideCount);
            Assert.AreEqual(MatchSide.Player, e.CurrentActor, "закотив і повернувся — ініціатива лишається");
            var act = e.State.Log.Last(x => x.Kind == MatchEventKind.Action);
            Assert.AreEqual(MatchActionId.ReturnToRing, act.Action);
            Assert.IsTrue(act.Outside, "дія почалась за рингом");
            Assert.IsTrue(act.Effects.Any(f => f.Kind == MatchEffectKind.Place && f.Amount == 0), "склейка: знову на рингу");
            Assert.AreEqual(Posture.Down, e.State.Rival.Posture, "суперник лежить — уже на рингу");
            Assert.AreNotEqual(UnavailableReason.NotInRing, Why(e, MatchActionId.Pin));
        }

        [Test]
        public void NoDq_NoCount_WeaponOutside()
        {
            var e = MatchKit.Engine(true, new[] { 12, 12, 12, 12, 12, 12, 12 }, NoDq);
            MatchKit.Play(e, MatchActionId.LeaveRing);
            Assert.IsTrue(e.Describe(MatchActionId.Weapon).Available);
            int hype = e.State.Hype;
            MatchKit.Play(e, MatchActionId.Weapon);
            Assert.AreEqual(1 + 3, e.State.Rival.Damage, "стіл 10+: 3 ушкодження");
            Assert.AreEqual(hype + 2, e.State.Hype);
            for (int i = 0; i < 5; i++) MatchKit.Play(e, MatchActionId.Taunt);
            Assert.IsFalse(e.IsOver, "без DQ відліку немає");
            Assert.AreEqual(0, e.State.OutsideCount);
        }

        [Test]
        public void Weapon_NeverOffered_InTheRing()
        {
            var e = MatchKit.Engine(true, null, NoDq);
            Assert.AreEqual(UnavailableReason.NotOutside, Why(e, MatchActionId.Weapon));
        }

        /// <summary>
        /// Досяжність (критерій М1 «кожна кінцівка досяжна»): у 1000 матчах ШІ проти ШІ буває перемога відліком; у матчах «без DQ» ШІ
        /// бере предмети й відліку немає.
        /// </summary>
        [Test]
        public void ThousandAiMatches_CountOutHappens_NoDqUsesWeapons()
        {
            var kinds = new Dictionary<MatchEndKind, int>();
            int trips = 0, weapons = 0, noDqCount = 0;
            for (int seed = 1; seed <= 1000; seed++)
            {
                bool noDq = seed % 5 == 0;
                var setup = MatchKit.Setup(s =>
                {
                    foreach (RingAttr a in System.Enum.GetValues(typeof(RingAttr)))
                    {
                        s.Player.Set(a, 1 + (seed + (int)a) % 3);
                        s.Rival.Set(a, 1 + (seed / 3 + (int)a) % 3);
                    }
                    s.PlayerRole = (RingPath)(seed % 3);
                    s.RivalRole = RingPath.Heel;
                    if (noDq) s.Stipulations = MatchStipulations.NoDq;
                });
                var e = new MatchEngine(setup, new SeededDiceRoller((ulong)seed));
                var r = MatchAutoplay.Run(e, true);
                int n; kinds.TryGetValue(r.Kind, out n); kinds[r.Kind] = n + 1;
                foreach (var ev in e.State.Log)
                {
                    if (ev.Kind != MatchEventKind.Action) continue;
                    if (ev.Action == MatchActionId.LeaveRing) trips++;
                    if (ev.Action == MatchActionId.Weapon) { weapons++; Assert.IsTrue(noDq, "предмет лише «без DQ»: seed " + seed); }
                }
                if (noDq && (r.Kind == MatchEndKind.CountOut || r.Kind == MatchEndKind.DoubleCountOut)) noDqCount++;
            }
            TestContext.WriteLine("1000 матчів: виходів за ринг " + trips + ", предметів " + weapons + "; кінці: "
                                  + string.Join(", ", kinds.Select(k => k.Key + "=" + k.Value).ToArray()));
            Assert.Greater(trips, 50, "бій за рингом трапляється");
            Assert.Greater(weapons, 0, "«без DQ» — предмети");
            Assert.AreEqual(0, noDqCount, "«без DQ» відліком не закінчується");
            int co; kinds.TryGetValue(MatchEndKind.CountOut, out co);
            Assert.Greater(co, 0, "перемога відліком досяжна");
        }
    }
}
