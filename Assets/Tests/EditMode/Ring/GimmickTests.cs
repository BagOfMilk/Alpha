using System;
using System.Linq;
using Game.Core.Randomness;
using Game.Core.Ring;
using Game.Core.Ring.Career;
using Game.Core.Ring.Match;
using Game.Gameplay.Combat;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>Створення борця (TRPG_DESIGN §5.1–§5.4): ґімік, роль, вільний бал, набір прийомів і прийоми як обгортки дій рушія.</summary>
    public class GimmickTests
    {
        [TestCase(Gimmick.Monster, 2, -1, 0, 2, 0)]
        [TestCase(Gimmick.Flyer, 0, 3, 0, -1, 0)]
        [TestCase(Gimmick.Technician, 0, 0, 3, 0, 0)]
        [TestCase(Gimmick.Showman, -1, 0, 0, 0, 3)]
        public void GimmickBase_IsInTheData(Gimmick g, int str, int agi, int tech, int sta, int cha)
        {
            var d = CareerKit.Content().GimmickOf(g);
            CollectionAssert.AreEqual(new[] { str, agi, tech, sta, cha }, d.Attr);
        }

        [Test]
        public void CreateHero_GimmickBasePlusOneFreePoint_AndFinisherAttrFromGimmick()
        {
            var s = CareerKit.NewAtCreate(1);
            s.CreateHero(CareerKit.Draft(Gimmick.Technician, CareerRole.Hero, RingAttr.Stamina, "Орест"));
            Assert.AreEqual(3, s.Player.Get(RingAttr.Technique));
            Assert.AreEqual(1, s.Player.Get(RingAttr.Stamina), "вільний бал");
            Assert.AreEqual(0, s.Player.Get(RingAttr.Strength));
            Assert.AreEqual(RingAttr.Technique, s.Player.FinisherAttr);
            Assert.AreEqual("Орест", s.State.RingName);
            Assert.AreEqual(2, s.State.Costume);
            Assert.AreEqual(CareerStage.Week, s.Stage);
            Assert.AreEqual(1, s.State.Week);
            Assert.AreEqual(CareerRules.StartMoney, s.Money);
        }

        [Test]
        public void CreateHero_AttrsStayInBounds_FreePointOnMaxIsRefused()
        {
            var s = CareerKit.NewAtCreate(1);
            string reason;
            Assert.IsFalse(s.CanCreate(CareerKit.Draft(Gimmick.Flyer, CareerRole.Hero, RingAttr.Agility), out reason));
            Assert.AreEqual("attr_max", reason);
            Assert.Throws<InvalidOperationException>(() => s.CreateHero(CareerKit.Draft(Gimmick.Flyer, CareerRole.Hero, RingAttr.Agility)));
            foreach (Gimmick g in Enum.GetValues(typeof(Gimmick)))
                for (int a = 0; a < 5; a++)
                {
                    var c = CareerKit.NewAtCreate(1);
                    if (!c.CanCreate(CareerKit.Draft(g, CareerRole.Hero, (RingAttr)a), out reason)) continue;
                    c.CreateHero(CareerKit.Draft(g, CareerRole.Hero, (RingAttr)a));
                    for (int i = 0; i < 5; i++) Assert.That(c.Player.Get((RingAttr)i), Is.InRange(WrestlerStats.MinAttr, WrestlerStats.MaxAttr));
                }
        }

        [Test]
        public void CreateHero_ValidatesNameAndCostume()
        {
            var s = CareerKit.NewAtCreate(1);
            string reason;
            Assert.IsFalse(s.CanCreate(CareerKit.Draft(name: "   "), out reason)); Assert.AreEqual("name", reason);
            Assert.IsFalse(s.CanCreate(CareerKit.Draft(name: new string('а', CareerSession.MaxNameLength + 1)), out reason)); Assert.AreEqual("name_long", reason);
            var d = CareerKit.Draft(); d.CostumeColor = CareerSession.CostumeCount;
            Assert.IsFalse(s.CanCreate(d, out reason)); Assert.AreEqual("costume", reason);
            Assert.IsTrue(s.CanCreate(CareerKit.Draft(), out reason));
        }

        [Test]
        public void Creation_OnlyAtCreateStage()
        {
            var s = CareerKit.NewAtTitle(1);
            string reason;
            Assert.IsFalse(s.CanCreate(CareerKit.Draft(), out reason));
            Assert.AreEqual("stage", reason);
            s.StartNewGame();
            s.CreateHero(CareerKit.Draft());
            Assert.Throws<InvalidOperationException>(() => s.StartNewGame());
        }

        [Test]
        public void Role_SetsStartingPath_HeroFaceVillainHeel()
        {
            var h = CareerKit.NewHero(1, role: CareerRole.Hero);
            var v = CareerKit.NewHero(1, role: CareerRole.Villain);
            Assert.AreEqual(RingPath.Face, h.CurrentRole);
            Assert.AreEqual(RingPath.Heel, v.CurrentRole);
        }

        [Test]
        public void CreationView_ListsFourGimmicks_WithBaseAttrsAndTenStartMoves()
        {
            var v = CareerKit.NewAtCreate(1).GetCreationView();
            Assert.AreEqual(4, v.Gimmicks.Count);
            Assert.AreEqual(1, v.FreePoints);
            foreach (var g in v.Gimmicks) Assert.AreEqual(10, g.StartMoves.Count);
        }

        [Test]
        public void StartingSet_IsSixBasic_TwoGimmick_Signature_Finisher()
        {
            foreach (Gimmick g in Enum.GetValues(typeof(Gimmick)))
            {
                // Вільний бал — у атрибут, де основа ґіміка не на стелі (Силач: сила вже 3+1; Монстр — без техніки за основою).
                var free = g == Gimmick.Monster ? RingAttr.Technique : g == Gimmick.Strongman ? RingAttr.Charisma : RingAttr.Strength;
                var s = CareerKit.NewHero(1, g, CareerRole.Hero, free);
                var v = s.GetCareerView();
                int gm = g == Gimmick.Rookie ? 0 : 2; // Новачок — «мало прийомів»: ґімік-прийоми вчить з нуля
                Assert.AreEqual(8 + gm, v.Moves.Count, g.ToString());
                Assert.AreEqual(6, v.Moves.Count(m => m.Kind == MoveKind.Basic));
                Assert.AreEqual(gm, v.Moves.Count(m => m.Kind == MoveKind.Gimmick), g.ToString());
                Assert.AreEqual(1, v.Moves.Count(m => m.Kind == MoveKind.Signature));
                Assert.AreEqual(1, v.Moves.Count(m => m.Kind == MoveKind.Finisher));
                foreach (var m in v.Moves.Where(m => m.Kind != MoveKind.Basic)) Assert.AreEqual(g.ToString().ToLowerInvariant(), m.GimmickId);
            }
        }

        [Test]
        public void MoveCatalog_WrapsEngineActions_AndAttrComesFromTheAction()
        {
            var c = CareerKit.Content();
            var fighter = new Fighter { Stats = new WrestlerStats("x", "x") { FinisherAttr = RingAttr.Agility } };
            foreach (var m in c.Moves.All)
            {
                Assert.That((int)m.Action, Is.InRange(0, 17));
                Assert.AreNotEqual(MatchActionId.Pin, m.Action, "утримання — системна дія, не прийом");
                RingAttr a;
                Assert.IsTrue(MatchEngine.AttrOf(m.Action, fighter, out a), m.Id + " без атрибута");
                if (m.Action == MatchActionId.Finisher) Assert.AreEqual(RingAttr.Agility, a);
            }
            Assert.AreEqual(RingAttr.Strength, Attr(MatchActionId.Strike));
            Assert.AreEqual(RingAttr.Charisma, Attr(MatchActionId.Taunt));
            Assert.AreEqual(RingAttr.Technique, Attr(MatchActionId.Submission));
        }

        private static RingAttr Attr(MatchActionId id)
        {
            RingAttr a;
            MatchEngine.AttrOf(id, new Fighter { Stats = new WrestlerStats("x", "x") }, out a);
            return a;
        }

        [Test]
        public void Learning_CostsPoints_NotSignatureNorFinisher()
        {
            var s = CareerKit.NewHero(1, Gimmick.Monster);
            string reason;
            Assert.IsFalse(s.CanLearnMove("dropkick", out reason)); Assert.AreEqual("points", reason);
            Assert.IsFalse(s.CanLearnMove("swallow_dive", out reason)); Assert.AreEqual("not_learnable", reason);
            Assert.IsFalse(s.CanLearnMove("meteor", out reason)); Assert.AreEqual("not_learnable", reason);
            Assert.IsFalse(s.CanLearnMove("forearm", out reason)); Assert.AreEqual("not_learnable", reason);
            Assert.IsFalse(s.CanLearnMove("nope", out reason)); Assert.AreEqual("unknown", reason);
            s.Feud.State.DevPoints = 5;
            Assert.IsTrue(s.LearnMove("dropkick"));
            Assert.AreEqual(3, s.DevPoints);
            Assert.IsTrue(s.KnowsMove("dropkick"));
            Assert.IsFalse(s.CanLearnMove("dropkick", out reason)); Assert.AreEqual("owned", reason);
            Assert.IsTrue(s.LearnMove("eye_poke"));
            Assert.AreEqual(1, s.DevPoints);
        }

        [Test]
        public void Development_AttrAndPerk_ThroughTheFacade_OnlyAtWeekOrSeasonEnd()
        {
            var s = CareerKit.NewHero(1);
            s.Feud.State.DevPoints = 10;
            Assert.IsTrue(s.BuyAttr(RingAttr.Technique));
            Assert.AreEqual(8, s.DevPoints);
            Assert.IsTrue(s.BuyPerk(PerkCatalog.Veteran));
            s.EndWeek();
            string reason;
            Assert.IsFalse(s.CanBuyAttr(RingAttr.Agility, out reason));
            Assert.AreEqual("stage", reason);
            Assert.IsFalse(s.LearnMove("dropkick"));
        }

        [Test]
        public void MoveOptions_ListKnownMoves_AndUniversalActions()
        {
            var s = CareerKit.NewHero(2);
            s.EndWeek();
            s.StartShow();
            var moves = s.GetMoveOptions();
            Assert.AreEqual(10, moves.Count);
            var uni = s.GetUniversalOptions();
            CollectionAssert.AreEquivalent(CareerSession.UniversalActions, uni.Select(u => u.Id).ToArray());
            // ходить лише один бік: у ворога моя таблиця не дійсна
            if (s.Engine.CurrentActor == MatchSide.Rival) Assert.IsTrue(moves.All(m => !m.Option.Available));
        }

        private static MatchEngine EngineWithHype(ulong seed, int startHype)
        {
            var setup = new MatchSetup
            {
                Player = new WrestlerStats("p", "p"), Rival = new WrestlerStats("r", "r"), StartHype = startHype, PlayerRole = RingPath.Face,
                RivalRole = RingPath.Heel
            };
            setup.Player.Set(RingAttr.Strength, 3);
            setup.Rival.Set(RingAttr.Stamina, 3);
            return new MatchEngine(setup, new SeededDiceRoller(seed));
        }

        private static int FirstTwoHypes(ulong seed, MoveHint first, MoveHint second, out bool ok)
        {
            var e = EngineWithHype(seed, 5);
            ok = false;
            if (e.CurrentActor != MatchSide.Player) return 0;
            e.Perform(MatchActionId.Strike, first); e.Commit(0);
            if (e.IsOver || e.CurrentActor != MatchSide.Player || e.Phase != MatchPhase.AwaitAction) return 0;
            int before = e.State.Hype;
            e.Perform(MatchActionId.Strike, second);
            if (e.Phase != MatchPhase.AwaitBoost) return 0;
            e.Commit(0);
            ok = true;
            return e.State.Hype - before;
        }

        [Test]
        public void RepeatingTheSameMoveTwice_CostsOneHype_DifferentMoveWithSameActionDoesNot()
        {
            int checkedSeeds = 0;
            for (ulong seed = 1; seed < 400 && checkedSeeds < 5; seed++)
            {
                bool okA, okB;
                int same = FirstTwoHypes(seed, new MoveHint { Key = "forearm" }, new MoveHint { Key = "forearm" }, out okA);
                int other = FirstTwoHypes(seed, new MoveHint { Key = "forearm" }, new MoveHint { Key = "low_kick" }, out okB);
                if (!okA || !okB) continue;
                Assert.AreEqual(other - 1, same, "seed " + seed);
                checkedSeeds++;
            }
            Assert.GreaterOrEqual(checkedSeeds, 3);
        }

        [Test]
        public void MoveHint_HypeOnStrong_AddsHypeOnlyOnCleanSuccess()
        {
            int strongSeen = 0;
            for (ulong seed = 1; seed < 300 && strongSeen < 4; seed++)
            {
                var a = EngineWithHype(seed, 2); var b = EngineWithHype(seed, 2);
                if (a.CurrentActor != MatchSide.Player) continue;
                a.Perform(MatchActionId.Strike, null); a.Commit(0);
                b.Perform(MatchActionId.Strike, new MoveHint { Key = "x", HypeOnStrong = 2 }); b.Commit(0);
                var last = b.State.Log.Last(ev => ev.Kind == MatchEventKind.Action);
                if (last.Tier == RollTier.Strong) { Assert.AreEqual(a.State.Hype + 2, b.State.Hype); strongSeen++; }
                else Assert.AreEqual(a.State.Hype, b.State.Hype);
            }
            Assert.GreaterOrEqual(strongSeen, 2);
        }

        [Test]
        public void MoveHint_RollMod_ShowsInChancesAndInTheRoll()
        {
            var e = EngineWithHype(3, 3);
            var plain = e.Describe(MatchActionId.Strike);
            var hurt = e.Describe(MatchActionId.Strike, -1);
            Assert.AreEqual(plain.Modifier - 1, hurt.Modifier);
            Assert.Greater(plain.Chances[2], hurt.Chances[2]);
        }
    }
}
