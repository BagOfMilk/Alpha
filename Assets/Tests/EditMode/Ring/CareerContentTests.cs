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

        /// <summary>
        /// Чотири федерації (П5, власник 11.11.2026): у кожній 3 чоловіки + 3 жінки (на місцевій ще Молот), ранги 1–6, обидві ролі;
        /// два пояси — у найсильнішого свого дивізіону; стеля, підлога й економіка ростуть з рівнем; 19 нових NPC.
        /// </summary>
        [Test]
        public void FourFederations_SixNpcEach_ThreePlusThree_TwoBelts_TablesGrow()
        {
            var c = CareerKit.Content();
            Assert.AreEqual(CareerContent.MaxTier, c.Federations.Count);
            Assert.AreEqual(25, c.Roster.Count, "6 місцевих + Молот + 18 вищих");
            Assert.AreEqual(c.Roster.Count, c.Roster.Select(n => n.Id).Distinct().Count());
            int[] ceil = { 4, 6, 8, 10 }, floor = { 0, 2, 4, 6 };
            for (int t = 1; t <= CareerContent.MaxTier; t++)
            {
                var f = c.Federation(t);
                Assert.AreEqual(t, f.Tier);
                var npcs = f.Roster.Where(n => n.Id != CareerContent.HammerId).ToList();
                Assert.AreEqual(6, npcs.Count, "федерація " + t);
                Assert.AreEqual(3, npcs.Count(n => n.Female), "федерація " + t + ": три жінки");
                CollectionAssert.AreEquivalent(new[] { 1, 2, 3, 4, 5, 6 }, npcs.Select(n => n.Rank).ToArray(), "федерація " + t);
                Assert.IsTrue(npcs.Any(n => n.Role == CareerRole.Hero) && npcs.Any(n => n.Role == CareerRole.Villain));
                Assert.IsTrue(f.Roster.All(n => n.Tier == t));
                for (int d = 0; d < 2; d++)
                {
                    bool female = d == 1;
                    var holder = c.FindNpc(f.BeltOf(female));
                    Assert.AreEqual(female, holder.Female);
                    Assert.AreEqual(npcs.Where(n => n.Female == female).Max(n => n.Rank), holder.Rank, "пояс — у найсильнішого дивізіону " + t);
                }
                Assert.AreEqual(ceil[t - 1], f.Ceiling); Assert.AreEqual(floor[t - 1], f.Floor);
                if (t > 1)
                {
                    var prev = c.Federation(t - 1);
                    Assert.Greater(f.FeePercent, prev.FeePercent); Assert.Greater(f.LivingCost, prev.LivingCost);
                    Assert.Greater(f.CostumeCost, prev.CostumeCost); Assert.GreaterOrEqual(f.PopularityHype, prev.PopularityHype);
                    Assert.Greater(npcs.Sum(n => Enumerable.Range(0, 5).Sum(i => n.Stats.Get((RingAttr)i))),
                        prev.Roster.Where(n => n.Id != CareerContent.HammerId).Sum(n => Enumerable.Range(0, 5).Sum(i => n.Stats.Get((RingAttr)i))),
                        "вища федерація — сильніший ростер");
                }
            }
            foreach (Gimmick g in Enum.GetValues(typeof(Gimmick))) Assert.IsTrue(c.Roster.Any(n => n.Gimmick == g), "у ростері немає ґіміка " + g);
            foreach (var n in c.Roster)
                for (int i = 0; i < 5; i++) Assert.That(n.Stats.Get((RingAttr)i), Is.InRange(WrestlerStats.MinAttr, WrestlerStats.MaxAttr), n.Id);
            Assert.AreEqual("baron", c.BeltHolderId);
            Assert.AreEqual("komet", c.Federation(1).BeltFemale);
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
        public void Moves_AreForty_IdsUnique_WithTheRightKinds()
        {
            // 24 прийоми чотирьох перших ґіміків + 16 ґіміків 5–8 (П5, §5.2): по два ґімік-прийоми, коронний і фінішер на ґімік.
            var c = CareerKit.Content();
            Assert.AreEqual(40, c.Moves.All.Count);
            Assert.AreEqual(40, c.Moves.All.Select(m => m.Id).Distinct().Count());
            Assert.AreEqual(6, c.Moves.All.Count(m => m.Kind == MoveKind.Basic));
            Assert.AreEqual(16, c.Moves.All.Count(m => m.Kind == MoveKind.Gimmick));
            Assert.AreEqual(8, c.Moves.All.Count(m => m.Kind == MoveKind.Signature));
            Assert.AreEqual(8, c.Moves.All.Count(m => m.Kind == MoveKind.Finisher));
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
        public void Gimmicks_AreEight_FourOnTheCreationScreen_EachWithTwoMovesSignatureAndFinisherOfItsOwn()
        {
            var c = CareerKit.Content();
            Assert.AreEqual(8, c.Gimmicks.Count);
            CollectionAssert.AreEqual(new[] { Gimmick.Monster, Gimmick.Flyer, Gimmick.Technician, Gimmick.Showman },
                c.Gimmicks.Where(g => g.OnCreationScreen).Select(g => g.Id).ToArray(), "ґіміки 5–8 — на екрані лише після У0 власника");
            Assert.AreEqual(Enum.GetValues(typeof(Gimmick)).Length, c.Gimmicks.Select(g => g.Id).Distinct().Count(), "кожен ґімік enum — у даних");
            foreach (var g in c.Gimmicks)
            {
                Assert.AreEqual(2, g.GimmickMoves.Length);
                foreach (var id in g.GimmickMoves) { Assert.AreEqual(MoveKind.Gimmick, c.Moves.Get(id).Kind); Assert.AreEqual(g.Key, c.Moves.Get(id).GimmickId); }
                Assert.AreEqual(MoveKind.Signature, c.Moves.Get(g.SignatureMove).Kind);
                Assert.AreEqual(MoveKind.Finisher, c.Moves.Get(g.FinisherMove).Kind);
                Assert.AreEqual(g.Key, c.Moves.Get(g.FinisherMove).GimmickId);
                // Бюджет створення = основа + вільні бали: 3–4. Новачок — 2 (§5.2 «Рівно, +2 вільні»): слабший старт, зате росте швидше.
                int budget = g.Attr.Sum() + g.FreePoints;
                if (g.Id == Gimmick.Rookie) Assert.AreEqual(2, budget, "Новачок — рівно, +2 вільні");
                else Assert.That(budget, Is.InRange(3, 4), "основа ґіміка " + g.Key + " збалансована за сумою");
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
            Assert.Throws<FormatException>(() => CareerContent.Load(feud, moves, gim, roster.Replace("\"beltHolder\": \"baron\"", "\"beltHolder\": \"ghost\"")));
            Assert.Throws<FormatException>(() => CareerContent.Load(feud, moves, gim, roster.Replace("\"beltFemale\": \"komet\"", "\"beltFemale\": \"baron\"")), "пояс жінок — у чоловіка");
            Assert.Throws<FormatException>(() => CareerContent.Load(feud, moves, gim, roster.Replace("\"ceiling\": 6", "\"ceiling\": 3")), "стеля нижча за попередню");
            Assert.Throws<FormatException>(() => CareerContent.Load(feud, moves, gim, roster.Replace("\"booker\": \"booker3\"", "\"booker\": \"nobody\"")));
            Assert.Throws<FormatException>(() => CareerContent.Load(feud, moves, gim, roster.Replace("\"id\": \"hammer\"", "\"id\": \"hamster\"")));
        }
    }
}
