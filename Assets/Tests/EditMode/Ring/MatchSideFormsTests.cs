using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Game.Core.Ring.Match;
using Game.Gameplay.Ring.Models;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Рід борця на табло матчу (P2-6, ПМ 12.11.2026): «Хід суперниці», «Її «Вирватися»», «ВИРВАЛАСЯ!», «Приголомшена» — у роді того,
    /// про кого слово. Стать — зі стану <see cref="HeroForms"/> (хост кладе його перед екраном матчу): героїня проти суперниці — обидві
    /// жінки; героїня проти Молота — вона жінка, він чоловік; герой — базові тексти.
    /// </summary>
    public class MatchSideFormsTests
    {
        // Ключі з формою «.w» і хто їх показує: кожен має виклик через RingLabels.ForSide — мертвих форм немає.
        private static readonly string[] WomanKeys =
        {
            "posture.2", "unavail.4", "unavail.5", "unavail.6", "unavail.13",
            "match.initiative.rival", "match.turn.rival", "match.turn.rival.react", "match.action.chances.opp",
            "match.call.kickout", "match.call.rival", "match.chance.opp.strong", "fx.4",
        };

        private static RingText LoadText()
        {
            string dir = RingCampaignDriver.Dir();
            var t = RingText.FromJson(File.ReadAllText(Path.Combine(dir, "strings_uk.json")), File.ReadAllText(Path.Combine(dir, "ui_uk.json")));
            t.Add("p", "Герой");
            t.Add("r", "Суперник");
            return t;
        }

        private static Dictionary<string, string> Ui()
        {
            var t = RingText.FromJson(File.ReadAllText(Path.Combine(RingCampaignDriver.Dir(), "ui_uk.json")));
            return new Dictionary<string, string>(t.AsDictionary());
        }

        [Test]
        public void EveryWomanForm_HasItsBase_SamePlaceholders_NoMasculine_AndIsInTheList()
        {
            var d = Ui();
            var masc = new Regex(@"(?<![\p{L}])(суперник|Суперник|його|Його|вирвався|Вирвався|ВИРВАВСЯ|підвівся|приголомшений|Приголомшений|побитий)(?![\p{L}])");
            var ph = new Regex(@"\{\d+\}");
            var forms = d.Keys.Where(k => k.EndsWith(RingLabels.WomanSuffix)).ToList();
            CollectionAssert.AreEquivalent(WomanKeys.Select(k => k + RingLabels.WomanSuffix).ToList(), forms, "перелік форм «.w» і ui_uk.json розійшлися");
            foreach (var w in forms)
            {
                string bk = w.Substring(0, w.Length - RingLabels.WomanSuffix.Length);
                Assert.IsTrue(d.ContainsKey(bk), "форма без базового ключа: " + w);
                Assert.AreNotEqual(d[bk], d[w], "форма збігається з базою: " + w);
                CollectionAssert.AreEqual(ph.Matches(d[bk]).Cast<Match>().Select(m => m.Value).OrderBy(x => x).ToList(),
                    ph.Matches(d[w]).Cast<Match>().Select(m => m.Value).OrderBy(x => x).ToList(), "плейсхолдери: " + w);
                Assert.IsFalse(masc.IsMatch(d[w]), "чоловічий рід у жіночій формі: " + w + " — " + d[w]);
                // «.w» — не «.f»: HeroForms підмінив би її для обох борців
                Assert.IsFalse(d.ContainsKey(bk + HeroForms.FemaleSuffix), "і «.f», і «.w» в одного ключа: " + bk);
                Assert.IsFalse(d.ContainsKey(bk + HeroForms.RivalSuffix), "і «.rf», і «.w» в одного ключа: " + bk);
            }
        }

        [Test]
        public void ForSide_FollowsTheWrestlerSex()
        {
            var t = LoadText();
            // герой — базові
            HeroForms.Apply(t, false, false);
            Assert.AreEqual("ВИРВАВСЯ!", RingLabels.ForSide(t, "match.call.kickout", MatchSide.Player));
            Assert.AreEqual("ВИРВАВСЯ!", RingLabels.ForSide(t, "match.call.kickout", MatchSide.Rival));
            Assert.AreEqual("Хід суперника", RingLabels.ForSide(t, "match.turn.rival", MatchSide.Rival));
            // героїня проти Молота: вона — жінка, він — чоловік
            HeroForms.Apply(t, true, false);
            Assert.AreEqual("ВИРВАЛАСЯ!", RingLabels.ForSide(t, "match.call.kickout", MatchSide.Player));
            Assert.AreEqual("ВИРВАВСЯ!", RingLabels.ForSide(t, "match.call.kickout", MatchSide.Rival));
            Assert.AreEqual("Суперник", RingLabels.ForSide(t, "match.call.rival", MatchSide.Rival));
            Assert.AreEqual("підвелася", RingLabels.ForSide(t, "fx.4", MatchSide.Player));
            Assert.AreEqual("підвівся", RingLabels.ForSide(t, "fx.4", MatchSide.Rival));
            // героїня проти суперниці: обидві
            HeroForms.Apply(t, true, true);
            Assert.AreEqual("ВИРВАЛАСЯ!", RingLabels.ForSide(t, "match.call.kickout", MatchSide.Rival));
            Assert.AreEqual("Суперниця", RingLabels.ForSide(t, "match.call.rival", MatchSide.Rival));
            Assert.AreEqual("Приголомшена", RingLabels.PostureName(t, Posture.Stunned, MatchSide.Rival));
            // без форми — базовий текст, а не сирий ключ
            Assert.AreEqual(t.Get("posture.0"), RingLabels.PostureName(t, Posture.Standing, MatchSide.Rival));
            // назад до героя — знову чоловічі
            HeroForms.Apply(t, false, false);
            Assert.AreEqual("Суперник", RingLabels.ForSide(t, "match.call.rival", MatchSide.Rival));
            Assert.AreEqual("Приголомшений", RingLabels.PostureName(t, Posture.Stunned, MatchSide.Player));
        }

        [Test]
        public void Board_HeroineVsWoman_SpeaksOfHer_AndHeroineVsHammer_OfHim()
        {
            var t = LoadText();
            var e = MatchKit.Engine(false);   // першим діє суперник
            var m = new MatchScreenModel(e, t);
            e.State.Get(MatchSide.Rival).Posture = Posture.Stunned;
            e.State.Get(MatchSide.Player).Posture = Posture.Stunned;

            HeroForms.Apply(t, true, true);
            Assert.AreEqual("Хід суперниці", m.TurnText);
            StringAssert.StartsWith("Ініціатива: суперниця", m.InitiativeText);
            Assert.AreEqual("Приголомшена", m.Card(MatchSide.Rival).PostureText);
            Assert.AreEqual("Приголомшена", m.Card(MatchSide.Player).PostureText);

            HeroForms.Apply(t, true, false);
            Assert.AreEqual("Хід суперника", m.TurnText);
            StringAssert.StartsWith("Ініціатива: суперник ", m.InitiativeText);
            Assert.AreEqual("Приголомшений", m.Card(MatchSide.Rival).PostureText);
            Assert.AreEqual("Приголомшена", m.Card(MatchSide.Player).PostureText, "героїня — у своєму роді й проти Молота");

            HeroForms.Apply(t, false, false);
            Assert.AreEqual("Приголомшений", m.Card(MatchSide.Player).PostureText);
        }

        [Test]
        public void Chances_AndReasons_AreInTheRivalsSex()
        {
            var t = LoadText();
            var pin = new MatchActionRow { Side = MatchSide.Player, HasRoll = true, RollIsOpponents = true, Percents = new[] { 30, 40, 30 } };
            HeroForms.Apply(t, true, true);
            StringAssert.StartsWith("Її «Вирватися»", MatchScreenModel.ChancesShort(t, pin));
            Assert.AreEqual("Суперниця не лежить", RingLabels.Unavailable(t, UnavailableReason.TargetNotDown, MatchSide.Player));
            HeroForms.Apply(t, true, false);
            StringAssert.StartsWith("Його «Вирватися»", MatchScreenModel.ChancesShort(t, pin));
            Assert.AreEqual("Суперник не лежить", RingLabels.Unavailable(t, UnavailableReason.TargetNotDown, MatchSide.Player));
            // рід самої героїні в причині — через «.f» HeroForms
            Assert.AreEqual("Щойно накривала — спершу прийом", RingLabels.Unavailable(t, UnavailableReason.JustPinned, MatchSide.Player));
            HeroForms.Apply(t, false, false);
            Assert.AreEqual("Щойно накривав — спершу прийом", RingLabels.Unavailable(t, UnavailableReason.JustPinned, MatchSide.Player));
        }
    }
}
