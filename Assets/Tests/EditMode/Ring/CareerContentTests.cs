using System;
using System.Collections.Generic;
using System.Linq;
using Game.Core.Ring;
using Game.Core.Ring.Career;
using Game.Core.Ring.Match;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>Дані кар'єри (career_*.json): склад ростеру, каталог прийомів, посилання між файлами й повнота тексту.</summary>
    public class CareerContentTests
    {
        private static HashSet<string> Keys()
        {
            var keys = new HashSet<string>();
            foreach (var f in new[] { "ui_uk.json", "strings_uk.json" })
                foreach (var kv in MiniJson.ParseObject(CareerKit.Read(f))) keys.Add(kv.Key);
            return keys;
        }

        private static void AssertHas(HashSet<string> keys, string key) { Assert.IsTrue(keys.Contains(key), "немає ключа тексту: " + key); }

        [Test]
        public void Roster_HasSixNpc_WithUniqueRanksOneToSix_BothRoles_AndAllGimmicks()
        {
            var c = CareerKit.Content();
            Assert.AreEqual(6, c.Roster.Count);
            CollectionAssert.AreEquivalent(new[] { 1, 2, 3, 4, 5, 6 }, c.Roster.Select(n => n.Rank).ToArray());
            Assert.AreEqual(6, c.Roster.Select(n => n.Id).Distinct().Count());
            Assert.IsTrue(c.Roster.Any(n => n.Role == CareerRole.Hero));
            Assert.IsTrue(c.Roster.Any(n => n.Role == CareerRole.Villain));
            foreach (Gimmick g in Enum.GetValues(typeof(Gimmick))) Assert.IsTrue(c.Roster.Any(n => n.Gimmick == g), "у ростері немає ґіміка " + g);
            foreach (var n in c.Roster)
                for (int i = 0; i < 5; i++) Assert.That(n.Stats.Get((RingAttr)i), Is.InRange(WrestlerStats.MinAttr, WrestlerStats.MaxAttr), n.Id);
            Assert.AreEqual(6, c.FindNpc(c.BeltHolderId).Rank, "пояс на старті — у найсильнішого");
        }

        [Test]
        public void Hammer_InTheRoster_IsTheSameWrestlerAsTheFeudRival()
        {
            var c = CareerKit.Content();
            var h = c.FindNpc(CareerContent.HammerId);
            Assert.AreEqual(c.Feud.Rival.Id, h.Stats.Id);
            Assert.AreEqual(c.Feud.Rival.NameKey, h.NameKey);
            Assert.AreEqual(c.Feud.RivalStartMomentum, h.StartMomentum);
            for (int i = 0; i < 5; i++) Assert.AreEqual(c.Feud.Rival.Get((RingAttr)i), h.Stats.Get((RingAttr)i));
        }

        [Test]
        public void Moves_AreTwentyFour_IdsUnique_WithTheRightKinds()
        {
            var c = CareerKit.Content();
            Assert.AreEqual(24, c.Moves.All.Count);
            Assert.AreEqual(24, c.Moves.All.Select(m => m.Id).Distinct().Count());
            Assert.AreEqual(6, c.Moves.All.Count(m => m.Kind == MoveKind.Basic));
            Assert.AreEqual(8, c.Moves.All.Count(m => m.Kind == MoveKind.Gimmick));
            Assert.AreEqual(4, c.Moves.All.Count(m => m.Kind == MoveKind.Signature));
            Assert.AreEqual(4, c.Moves.All.Count(m => m.Kind == MoveKind.Finisher));
            Assert.AreEqual(2, c.Moves.All.Count(m => m.Kind == MoveKind.Learn));
            foreach (var m in c.Moves.All)
            {
                Assert.That(m.Heat, Is.InRange(0, 2), m.Id);
                if (m.Kind == MoveKind.Finisher) Assert.AreEqual(MatchActionId.Finisher, m.Action, m.Id);
                if (m.Action == MatchActionId.Finisher) Assert.AreEqual(MoveKind.Finisher, m.Kind, m.Id);
                if (m.IsLearnable) Assert.Greater(m.Cost, 0, m.Id);
            }
            foreach (MoveCategory cat in Enum.GetValues(typeof(MoveCategory))) Assert.IsTrue(c.Moves.All.Any(m => m.Category == cat), "категорія без прийомів: " + cat);
            foreach (BodyZone z in Enum.GetValues(typeof(BodyZone))) Assert.IsTrue(c.Moves.All.Any(m => m.Zone == z), "зона без прийомів: " + z);
        }

        [Test]
        public void Gimmicks_AreFour_EachWithTwoMovesSignatureAndFinisherOfItsOwn()
        {
            var c = CareerKit.Content();
            Assert.AreEqual(4, c.Gimmicks.Count);
            foreach (var g in c.Gimmicks)
            {
                Assert.AreEqual(2, g.GimmickMoves.Length);
                foreach (var id in g.GimmickMoves) { Assert.AreEqual(MoveKind.Gimmick, c.Moves.Get(id).Kind); Assert.AreEqual(g.Key, c.Moves.Get(id).GimmickId); }
                Assert.AreEqual(MoveKind.Signature, c.Moves.Get(g.SignatureMove).Kind);
                Assert.AreEqual(MoveKind.Finisher, c.Moves.Get(g.FinisherMove).Kind);
                Assert.AreEqual(g.Key, c.Moves.Get(g.FinisherMove).GimmickId);
                int sum = g.Attr.Sum();
                Assert.That(sum, Is.InRange(2, 4), "основа ґіміка " + g.Key + " збалансована за сумою");
            }
        }

        [Test]
        public void EveryVisibleKey_IsInTheTextFiles()
        {
            var k = Keys();
            var c = CareerKit.Content();
            foreach (var m in c.Moves.All) AssertHas(k, m.NameKey);
            foreach (var g in c.Gimmicks) { AssertHas(k, g.NameKey); AssertHas(k, g.NameKey + ".desc"); }
            foreach (var n in c.Roster) { AssertHas(k, n.NameKey); AssertHas(k, n.BioKey); }
            foreach (MoveCategory x in Enum.GetValues(typeof(MoveCategory))) AssertHas(k, "move.cat." + (int)x);
            foreach (MoveKind x in Enum.GetValues(typeof(MoveKind))) AssertHas(k, "move.kind." + (int)x);
            foreach (BodyZone x in Enum.GetValues(typeof(BodyZone))) AssertHas(k, "bodyzone." + (int)x);
            foreach (CardPosition x in Enum.GetValues(typeof(CardPosition))) AssertHas(k, "card.pos." + (int)x);
            foreach (CareerStage x in Enum.GetValues(typeof(CareerStage))) AssertHas(k, "career.stage." + (int)x);
            foreach (WeekActionKind x in Enum.GetValues(typeof(WeekActionKind))) { AssertHas(k, "week.action." + (int)x); AssertHas(k, "week.action." + (int)x + ".desc"); }
            foreach (WeekPromoReward x in Enum.GetValues(typeof(WeekPromoReward))) AssertHas(k, "week.promo.reward." + (int)x);
            foreach (var r in new[] { "stage", "no_actions", "money", "choice_pending", "nothing", "zone" }) AssertHas(k, "week.reason." + r);
            foreach (var r in new[] { "stage", "draft", "name", "name_long", "costume", "attr_max" }) AssertHas(k, "create.reason." + r);
            foreach (var r in new[] { "unknown", "not_learnable", "owned", "points", "stage" }) AssertHas(k, "move.reason." + r);
            foreach (var r in new[] { "hammer_big", "defense", "title_shot", "booker_cold", "big_show", "next_opponent" }) AssertHas(k, "rumor." + r);
            foreach (var key in new[] { "role.hero", "role.villain", "role.hero.desc", "role.villain.desc", "card.stip.single", "card.stip.no_dq", "card.big",
                "card.title", "card.defense", "card.fee", "card.withdraw", "season.fired", "season.title_shot", "season.defense", "season.next",
                "show.withdrew", "show.won", "show.lost", "show.draw", "show.belt_won", "week.strained", "week.end", "move.injured", "move.repeat",
                "action.17", "unavail.23" }) AssertHas(k, key);
        }

        [Test]
        public void RumorKeys_ReturnedByTheSession_AreAllInTheText()
        {
            var k = Keys();
            for (ulong seed = 1; seed < 150; seed++)
            {
                var s = CareerKit.NewHero(seed, Gimmick.Showman);
                var r = s.Backstage();
                if (r.NeedsScene) r = CareerKit.PlayScene(s, (int)seed);
                if (r.RumorKey != null) AssertHas(k, r.RumorKey);
            }
        }

        [Test]
        public void NoRealWrestlersOrBrands_InNamesAndBios()
        {
            var banned = new[] { "wwe", "aew", "hulk", "undertaker", "cena", "rock", "triple h", "nxt", "wcw" };
            var keys = MiniJson.ParseObject(CareerKit.Read("ui_uk.json"));
            foreach (var kv in keys)
                if (kv.Key.StartsWith("w.") || kv.Key.StartsWith("npc."))
                    foreach (var b in banned) Assert.IsFalse(((string)kv.Value).ToLowerInvariant().Contains(b), kv.Key);
        }

        [Test]
        public void BrokenData_IsRefusedWithAClearError()
        {
            var feud = CareerKit.Read("feud_hammer.json"); var moves = CareerKit.Read("career_moves.json");
            var gim = CareerKit.Read("career_gimmicks.json"); var roster = CareerKit.Read("career_roster.json");
            Assert.Throws<FormatException>(() => CareerContent.Load(feud, moves.Replace("\"strike\"", "\"nonsense\""), gim, roster));
            Assert.Throws<FormatException>(() => CareerContent.Load(feud, moves, gim.Replace("bulldozer", "no_such_move"), roster));
            Assert.Throws<FormatException>(() => CareerContent.Load(feud, moves, gim, roster.Replace("\"beltHolder\": \"komet\"", "\"beltHolder\": \"ghost\"")));
            Assert.Throws<FormatException>(() => CareerContent.Load(feud, moves, gim, roster.Replace("\"id\": \"hammer\"", "\"id\": \"hamster\"")));
        }
    }
}
