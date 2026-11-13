using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Core.Ring;
using Game.Core.Ring.Match;
using Game.Gameplay.Combat;
using Game.Gameplay.Ring.Models;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    /// <summary>Моделі екранів «Рингу» (без UnityEngine): відсотки, тексти без пропусків, причини блокування.</summary>
    public class RingModelsTests
    {
        private static RingText LoadText()
        {
            string dir = RingCampaignDriver.Dir();
            return RingText.FromJson(File.ReadAllText(Path.Combine(dir, "strings_uk.json")), File.ReadAllText(Path.Combine(dir, "ui_uk.json")));
        }

        private static void AssertClean(string s, string where)
        {
            Assert.IsFalse(string.IsNullOrEmpty(s), "порожній текст: " + where);
            Assert.IsFalse(s.Contains("⟦"), "ключ без тексту: " + s + " (" + where + ")");
        }

        // ---------- відсотки ----------

        [Test]
        public void Percent_SumsToExactly100_ForEveryModifier()
        {
            for (int mod = -6; mod <= 8; mod++)
            {
                var c36 = TwoD6.ChancesOf36(mod);
                Assert.AreEqual(36, c36.Sum());
                var p = RingChances.Percent(c36);
                Assert.AreEqual(100, p.Sum(), "модифікатор " + mod);
                for (int i = 0; i < 3; i++)
                    Assert.LessOrEqual(Math.Abs(p[i] - c36[i] * 100.0 / 36.0), 1.0, "відсоток близький до точного (мод " + mod + ")");
            }
        }

        [Test]
        public void Signed_UsesRealMinus()
        {
            Assert.AreEqual("+2", RingChances.Signed(2));
            Assert.AreEqual("−1", RingChances.Signed(-1));
            Assert.AreEqual("0", RingChances.Signed(0));
        }

        // ---------- рядки ----------

        [Test]
        public void Text_MissingKeyIsMarkedAndRemembered()
        {
            var t = RingText.FromJson("{\"a\":\"Так\"}");
            Assert.AreEqual("Так", t.Get("a"));
            Assert.IsEmpty(t.Missing);
            Assert.AreEqual("⟦b⟧", t.Get("b"));
            CollectionAssert.AreEquivalent(new[] { "b" }, t.Missing.ToArray());
        }

        [Test]
        public void UiStrings_DoNotCollideWithFeudStrings_AndHaveNoEmptyValues()
        {
            string dir = RingCampaignDriver.Dir();
            var feud = MiniJson.ParseObject(File.ReadAllText(Path.Combine(dir, "strings_uk.json")));
            var ui = MiniJson.ParseObject(File.ReadAllText(Path.Combine(dir, "ui_uk.json")));
            foreach (var k in ui.Keys) Assert.IsFalse(feud.ContainsKey(k), "ключ у двох файлах: " + k);
            foreach (var kv in ui) Assert.IsFalse(string.IsNullOrWhiteSpace((string)kv.Value), "порожнє значення: " + kv.Key);
        }

        /// <summary>
        /// Назва ґіміка за статтю борця — кожен із восьми, обидві форми з текстом. Підстановка форм героїні (HeroForms.Apply)
        /// назву суперника-чоловіка не чіпає: рід дає лише прапорець <c>female</c>.
        /// </summary>
        [Test]
        public void GimmickName_ByTheWrestlersSex_EveryGimmickHasBothForms_HeroFormsDoNotTouchIt()
        {
            var t = LoadText();
            var gimmicks = CareerKit.Content().Gimmicks;
            Assert.AreEqual(8, gimmicks.Count);
            var male = new Dictionary<string, string>();
            foreach (var g in gimmicks)
            {
                string m = RingLabels.GimmickName(t, g.Key, false), f = RingLabels.GimmickName(t, g.Key, true);
                AssertClean(m, g.Key);
                AssertClean(f, g.Key + ".fem");
                Assert.AreEqual(t.Get("gimmick." + g.Key), m, "чоловіча форма — та сама, що досі на екранах");
                male[g.Key] = m;
            }
            Assert.AreEqual("Силачка", RingLabels.GimmickName(t, "strongman", true));
            Assert.AreEqual("Монстр", RingLabels.GimmickName(t, "monster", true), "Монстр — спільного роду");
            HeroForms.Apply(t, true, true);
            Assert.IsTrue(HeroForms.ShowsHeroine(t));
            foreach (var g in gimmicks)
                Assert.AreEqual(male[g.Key], RingLabels.GimmickName(t, g.Key, false), "суперник героїні лишається в чоловічому роді: " + g.Key);
        }

        [Test]
        public void Labels_EveryEnumValueHasAText()
        {
            var t = LoadText();
            foreach (RingAttr a in Enum.GetValues(typeof(RingAttr))) AssertClean(RingLabels.AttrName(t, a), a.ToString());
            foreach (RingPath p in Enum.GetValues(typeof(RingPath)))
            {
                AssertClean(RingLabels.PathName(t, p), p.ToString());
                AssertClean(RingLabels.PathMark(t, p), p.ToString());
            }
            foreach (var v in FeudVars.All) AssertClean(RingLabels.Var(t, v), v);
            foreach (BookerPlan p in Enum.GetValues(typeof(BookerPlan))) AssertClean(RingLabels.PlanName(t, p), p.ToString());
            foreach (FeudPlanOutcome o in Enum.GetValues(typeof(FeudPlanOutcome))) AssertClean(RingLabels.PlanOutcome(t, o), o.ToString());
            foreach (RollTier x in Enum.GetValues(typeof(RollTier))) AssertClean(RingLabels.TierName(t, x), x.ToString());
            foreach (Posture z in Enum.GetValues(typeof(Posture))) AssertClean(RingLabels.PostureName(t, z), z.ToString());
            foreach (MatchActionId a in Enum.GetValues(typeof(MatchActionId))) AssertClean(RingLabels.ActionName(t, a), a.ToString());
            foreach (UnavailableReason r in Enum.GetValues(typeof(UnavailableReason))) AssertClean(RingLabels.Unavailable(t, r), r.ToString());
            foreach (MatchEndKind k in Enum.GetValues(typeof(MatchEndKind))) AssertClean(RingLabels.EndKind(t, k), k.ToString());
            foreach (MatchWinner w in Enum.GetValues(typeof(MatchWinner))) AssertClean(RingLabels.WinnerName(t, w), w.ToString());
            foreach (var e in FeudEndings.All)
            {
                AssertClean(RingLabels.EndingName(t, e), e.ToString());
                AssertClean(RingLabels.EndingDesc(t, e), e.ToString());
            }
            foreach (var p in PerkCatalog.All)
            {
                AssertClean(RingLabels.PerkName(t, p.Id), p.Id);
                AssertClean(RingLabels.PerkDesc(t, p.Id), p.Id);
            }
            for (int h = 0; h <= 10; h++) AssertClean(RingLabels.HypeStageText(t, h), "hype " + h);
            foreach (MatchEffectKind k in Enum.GetValues(typeof(MatchEffectKind))) AssertClean(t.Get("fx." + (int)k), k.ToString());
            foreach (var key in new[] { "dev.reason.unknown", "dev.reason.owned", "dev.reason.rank", "dev.reason.points", "dev.reason.max" })
                AssertClean(t.Get(key), key);
            Assert.IsEmpty(t.Missing, string.Join(", ", t.Missing));
        }

        [Test]
        public void ReasonText_EveryConditionInTheScript_HasAText()
        {
            var t = LoadText();
            var script = RingCampaignDriver.RealScript();
            int seen = 0;
            foreach (var promo in script.Promos)
                foreach (var node in promo.Nodes)
                    foreach (var o in node.Options)
                        foreach (var c in o.Requires)
                        {
                            AssertClean(PromoScreenModel.ReasonText(t, c), promo.Id + "/" + node.Id + "/" + o.Id + " " + c);
                            seen++;
                        }
            Assert.Greater(seen, 3);
            // Усі види умов, навіть яких у даних поки немає: загальна причина, а не порожнеча.
            foreach (ConditionKind k in Enum.GetValues(typeof(ConditionKind)))
                {
                string key = k == ConditionKind.Attr ? "charisma" : k == ConditionKind.Var ? "audience" : k == ConditionKind.Path ? "face" : "some_unknown_branch";
                AssertClean(PromoScreenModel.ReasonText(t, new FeudCondition { Kind = k, Key = key, Min = 1 }), k.ToString());
            }
            Assert.IsEmpty(t.Missing, string.Join(", ", t.Missing));
        }

        // ---------- екрани ----------

        [Test]
        public void Promo_LockedOptionHasReason_AndCannotBeChosen()
        {
            var t = LoadText();
            var c = RingCampaignDriver.NewCampaign(1);
            var runner = c.StartPromo();
            var m = new PromoScreenModel(runner, t);
            while (m.Kind != PromoViewKind.Choice) { Assert.IsTrue(m.CanAdvance); m.Advance(); }
            var rows = m.Choices();
            Assert.GreaterOrEqual(rows.Count, 2);
            var locked = rows.Where(r => r.Locked).ToList();
            Assert.IsNotEmpty(locked, "у першому виборі є сірий варіант (харизма 0)");
            foreach (var r in locked)
            {
                AssertClean(r.ReasonText, r.Id);
                Assert.IsFalse(m.Choose(r.Id));
                Assert.IsFalse(m.ChooseNumber(r.Number));
            }
            foreach (var r in rows.Where(x => x.HasPath)) AssertClean(r.PathMark, r.Id);
            var free = rows.First(r => !r.Locked);
            Assert.IsTrue(m.ChooseNumber(free.Number));
            Assert.AreNotEqual(PromoViewKind.Choice, m.Kind, "вибір пройдено");
        }

        [Test]
        public void Promo_CheckPanel_ShowsThreePercentsThatSumTo100_AndRewardsNeedExactCount()
        {
            var t = LoadText();
            var c = RingCampaignDriver.NewCampaign(1);
            var m = new PromoScreenModel(c.StartPromo(), t);
            int guard = 0;
            while (m.Kind != PromoViewKind.Check && guard++ < 100)
            {
                if (m.Kind == PromoViewKind.Line) m.Advance();
                else if (m.Kind == PromoViewKind.Choice) m.ChooseNumber(m.Choices().First(r => !r.Locked).Number);
                else Assert.Fail("несподіваний вид " + m.Kind);
            }
            Assert.AreEqual(PromoViewKind.Check, m.Kind);
            var p = m.ChancePercents();
            Assert.AreEqual(3, p.Length);
            Assert.AreEqual(100, p.Sum());
            AssertClean(m.CheckAttrText, "attr");

            // Кидаємо поки не вийде вибір нагород (різні зерна — різні ступені), але лише на цьому ж вузлі.
            var res = m.Roll(c.Dice);
            Assert.AreEqual(res.Die1 + res.Die2 + res.Modifier, res.Total);
            Assert.That(res.Die1, Is.InRange(1, 6));
            Assert.That(res.Die2, Is.InRange(1, 6));
            AssertClean(res.TierText, "tier");
            AssertClean(res.TotalText, "total");
            if (m.Kind == PromoViewKind.Rewards)
            {
                Assert.Greater(m.RewardsToPick, 0);
                Assert.IsFalse(m.CanConfirmRewards);
                Assert.Throws<InvalidOperationException>(() => m.ConfirmRewards());
                var rows = m.Rewards();
                foreach (var r in rows) AssertClean(r.Text, r.Id);
                for (int i = 0; i < rows.Count; i++) m.ToggleReward(rows[i].Id);
                Assert.AreEqual(m.RewardsToPick, m.RewardsPicked, "понад ліміт не ставиться");
                Assert.IsTrue(m.CanConfirmRewards);
                m.ConfirmRewards();
            }
            else
            {
                Assert.AreEqual(PromoViewKind.Result, m.Kind, "провал: показ результату до переходу далі");
                m.AcknowledgeResult();
            }
            Assert.IsNull(m.Result);
        }

        [Test]
        public void WholeCampaign_ThroughModels_LeavesNoMissingTexts()
        {
            for (ulong seed = 1; seed <= 3; seed++)
            {
                var t = LoadText();
                var c = RingCampaignDriver.NewCampaign(seed);
                var feud = new FeudScreenModel(c, t);
                int guard = 0;
                int promos = 0, matches = 0, interDev = 0;

                // Усі три акти через моделі: промо → матч → розвиток між матчами → … → розв'язка (стан Ending).
                while (c.Stage != RingStage.Ending)
                {
                    Assert.Less(++guard, 4000);
                    if (c.Stage == RingStage.Overview)
                    {
                        TouchFeud(feud);
                        c.StartPromo();
                        promos++;
                        continue;
                    }
                    if (c.Stage == RingStage.MatchPrep)
                    {
                        TouchFeud(feud);
                        Assert.AreEqual(matches + 1, feud.MatchNumber);
                        PlayMatchThroughModel(c, t);
                        matches++;
                        continue;
                    }
                    if (c.Stage == RingStage.Development)
                    {
                        var between = new DevelopmentScreenModel(c, t);
                        Assert.IsFalse(between.IsFinal, "кінцівка лише після розв'язки");
                        TouchDev(between, c);
                        AssertClean(between.ContinueText, "continue");
                        Assert.IsTrue(between.CanContinue);
                        Assert.AreEqual(matches, between.MatchLines().Count);
                        between.Continue();
                        interDev++;
                        continue;
                    }
                    var pm = new PromoScreenModel(c.CurrentPromoRunner, t);
                    while (!pm.IsFinished)
                    {
                        Assert.Less(++guard, 4000);
                        AssertClean(pm.TitleText, "title");
                        AssertClean(pm.PlaceText, "place");
                        switch (pm.Kind)
                        {
                            case PromoViewKind.Line:
                                AssertClean(pm.SpeakerText, "speaker");
                                AssertClean(pm.LineText, "line");
                                pm.Advance();
                                break;
                            case PromoViewKind.Choice:
                                var rows = pm.Choices();
                                foreach (var r in rows)
                                {
                                    AssertClean(r.Text, r.Id);
                                    if (r.Locked) AssertClean(r.ReasonText, r.Id);
                                }
                                Assert.IsTrue(pm.ChooseNumber(rows.First(r => !r.Locked).Number));
                                break;
                            case PromoViewKind.Check:
                                Assert.AreEqual(100, pm.ChancePercents().Sum());
                                pm.Roll(c.Dice);
                                break;
                            case PromoViewKind.Rewards:
                                foreach (var r in pm.Rewards().Take(pm.RewardsToPick)) pm.ToggleReward(r.Id);
                                pm.ConfirmRewards();
                                break;
                            case PromoViewKind.Result:
                                AssertClean(pm.Result.TierText, "tier");
                                pm.AcknowledgeResult();
                                break;
                        }
                    }
                    c.FinishPromo();
                }

                Assert.AreEqual(4, promos);
                Assert.AreEqual(3, matches);
                Assert.AreEqual(2, interDev, "розвиток між матчами — після першого й другого");
                Assert.AreEqual(RingStage.Ending, c.Stage);

                c.OpenDevelopment();
                var dev = new DevelopmentScreenModel(c, t);
                TouchDev(dev, c);
                // Купуємо, що можна, і граємо реванш.
                c.State.AddPath(RingPath.Face, 4);
                c.State.DevPoints += 4;
                foreach (var row in dev.Perks().Where(r => r.State == PerkState.Available).ToList()) dev.BuyPerk(row.Id);
                dev.BuyAttr(RingAttr.Technique);
                TouchDev(dev, c);
                Assert.IsTrue(dev.CanRematch);
                c.StartRematch();
                PlayMatchThroughModel(c, t);
                Assert.AreEqual(RingStage.Development, c.Stage);
                Assert.IsTrue(dev.HasRematchResult);
                foreach (var l in dev.RematchLines()) AssertClean(l, "rematch");
                TouchDev(dev, c);

                Assert.IsEmpty(t.Missing, "зерно " + seed + ": " + string.Join(", ", t.Missing));
            }
        }

        private static void TouchFeud(FeudScreenModel m)
        {
            AssertClean(m.TitleText, "feud title");
            AssertClean(m.VersusText, "versus");
            AssertClean(m.ProgressText, "progress");
            AssertClean(m.ContinueText, "continue");
            AssertClean(m.PlanText, "plan");
            AssertClean(m.AllyText, "ally");
            foreach (var s in m.StipulationTexts()) AssertClean(s, "stip");
            foreach (var r in m.Meters()) { AssertClean(r.Name, r.Id); AssertClean(r.ValueText, r.Id); }
            foreach (var r in m.Paths()) { AssertClean(r.Name, "path"); AssertClean(r.RankText, "path"); AssertClean(r.Mark, "mark"); }
            if (m.HasNextPromo) { AssertClean(m.NextTitleText, "next"); AssertClean(m.NextPlaceText, "next place"); }
        }

        private static void TouchDev(DevelopmentScreenModel m, RingCampaign c)
        {
            AssertClean(m.EndingTitle, "ending");
            AssertClean(m.EndingDesc, "ending desc");
            AssertClean(m.PlanText, "dev plan");
            AssertClean(m.PointsText, "points");
            foreach (var l in m.ResultLines()) AssertClean(l, "result");
            foreach (var r in m.Paths()) { AssertClean(r.Name, "path"); AssertClean(r.RankText, "rank"); }
            foreach (var r in m.Perks())
            {
                AssertClean(r.Name, r.Id); AssertClean(r.Desc, r.Id); AssertClean(r.CostText, r.Id); AssertClean(r.PathText, r.Id);
                if (r.State == PerkState.Locked) AssertClean(r.ReasonText, r.Id);
            }
            foreach (var r in m.Attrs())
            {
                AssertClean(r.Name, "attr"); AssertClean(r.BuyText, "attr");
                if (!r.CanBuy) AssertClean(r.ReasonText, r.Name);
            }
        }

        /// <summary>Матч через модель: ШІ веде суперника, гравець ходить за порадою ШІ, але лише через команди моделі.</summary>
        private static void PlayMatchThroughModel(RingCampaign c, RingText t)
        {
            var engine = c.Stage == RingStage.MatchPrep ? c.StartMatch() : c.CurrentMatch;
            var m = new MatchScreenModel(engine, t);
            int steps = 0;
            while (!m.IsOver)
            {
                Assert.Less(++steps, MatchAutoplay.MaxSteps);
                TouchMatch(m);
                switch (m.Input)
                {
                    case MatchInput.Ai: m.StepAi(); break;
                    case MatchInput.PlayerAction:
                        var id = MatchAi.ChooseAction(engine);
                        Assert.IsTrue(m.Perform(id), "дію " + id + " порадив ШІ, модель мала б її дозволити");
                        break;
                    case MatchInput.PlayerReaction:
                        Assert.IsTrue(m.React(MatchAi.ChooseReaction(engine)));
                        break;
                    case MatchInput.PlayerBoost:
                        Assert.IsTrue(m.Commit(MatchAi.ChooseBoost(engine)));
                        break;
                }
            }
            TouchMatch(m);
            foreach (var l in m.ResultLines()) AssertClean(l, "match result");
            c.FinishMatch();
        }

        private static void TouchMatch(MatchScreenModel m)
        {
            foreach (var side in new[] { MatchSide.Player, MatchSide.Rival })
            {
                var card = m.Card(side);
                AssertClean(card.Name, "name");
                AssertClean(card.DamageText, "damage"); AssertClean(card.MomentumText, "momentum");
                AssertClean(card.PostureText, "posture"); AssertClean(card.StaminaText, "stamina"); AssertClean(card.TokensText, "tokens");
            }
            AssertClean(m.HypeText, "hype"); AssertClean(m.RoundText, "round");
            if (!m.IsOver) AssertClean(m.InitiativeText, "initiative");
            if (m.IsOutside) AssertClean(m.PlaceText, "place");
            if (m.CountText.Length > 0) AssertClean(m.CountText, "count");
            AssertClean(m.RefereeText, "referee"); AssertClean(m.TurnText, "turn");
            foreach (var l in m.LogTexts()) AssertClean(l, "log");
            foreach (var a in m.Actions())
            {
                AssertClean(a.Name, a.Id.ToString()); AssertClean(a.CostText, a.Id.ToString());
                if (!a.Available) AssertClean(a.ReasonText, a.Id.ToString());
                if (a.HasRoll) Assert.AreEqual(100, a.Percents.Sum(), a.Id.ToString());
            }
            var r = m.Reaction();
            if (r != null)
            {
                AssertClean(r.PromptText, "react"); AssertClean(r.SellText, "sell"); AssertClean(r.ReverseText, "reverse");
                Assert.AreEqual(100, r.ReversePercents.Sum());
            }
            var boost = m.BoostOptions();
            if (boost.Count > 0)
            {
                AssertClean(m.BoostPrompt, "boost");
                foreach (var b in boost) AssertClean(b.Text, "boost option");
            }
        }

        [Test]
        public void EveryTextKeyWrittenInTheRingScreensAndModels_ExistsInTheStringFiles()
        {
            var t = LoadText();
            string root = Path.Combine(Application.dataPath, "_Project", "Scripts", "Gameplay", "Ring");
            var rx = new System.Text.RegularExpressions.Regex(@"\.(?:Get|Format)\(""([A-Za-z0-9_.]+)""");
            int found = 0;
            foreach (var file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
                foreach (System.Text.RegularExpressions.Match m in rx.Matches(File.ReadAllText(file)))
                {
                    if (m.Groups[1].Value.EndsWith(".")) continue; // префікс динамічного ключа: перевіряє тест переліків
                    found++;
                    Assert.IsTrue(t.Has(m.Groups[1].Value), "ключ без тексту: " + m.Groups[1].Value + " у " + Path.GetFileName(file));
                }
            Assert.Greater(found, 40, "регулярний вираз не знайшов ключів — перевірка порожня");
        }

        [Test]
        public void Match_ActionNumbersAreForAvailableOnly_AndRowsCarryReasons()
        {
            var t = LoadText();
            var c = RingCampaignDriver.NewCampaign(2);
            while (c.Stage != RingStage.MatchPrep) RingCampaignDriver.Step(c);
            var engine = c.StartMatch();
            var m = new MatchScreenModel(engine, t);
            while (m.Input == MatchInput.Ai) m.StepAi();
            Assert.AreEqual(MatchInput.PlayerAction, m.Input);
            var rows = m.Actions();
            Assert.AreEqual(Enum.GetValues(typeof(MatchActionId)).Length - 1, rows.Count, "усі дії рушія, крім «Променя надії»");
            Assert.IsTrue(rows.Where(r => r.Number > 0).All(r => r.Available));
            Assert.AreEqual(rows.Where(r => r.Available).Take(9).Count(), rows.Count(r => r.Number > 0));
            Assert.IsTrue(rows.Where(r => !r.Available).All(r => r.Number == 0 && r.ReasonText.Length > 0));
            Assert.IsFalse(m.Perform(rows.First(r => !r.Available).Id), "сіру дію не виконати");
        }
    }
}
