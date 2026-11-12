using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Game.Core.Ring;
using Game.Core.Ring.Match;
using Game.Gameplay.Combat;
using Game.Gameplay.Ring.Models;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Екран матчу Ф3 (лише модель): рядок дії з трьома шансами, табло, хід суперника «як свій», вікно QTE, журнал із боком діяча,
    /// фокус без миші. Вигляду тут немає — він після воріт У0.
    /// </summary>
    public class MatchScreenF3Tests
    {
        [SetUp]
        public void SetUp() { RingMatchPrefs.ResetToDefaults(); }

        [TearDown]
        public void TearDown() { RingMatchPrefs.ResetToDefaults(); }

        private static RingText LoadText()
        {
            string dir = RingCampaignDriver.Dir();
            var t = RingText.FromJson(File.ReadAllText(Path.Combine(dir, "strings_uk.json")), File.ReadAllText(Path.Combine(dir, "ui_uk.json")));
            t.Add("p", "Герой");
            t.Add("r", "Суперник");
            return t;
        }

        private static MatchScreenModel Model(MatchEngine e) { return new MatchScreenModel(e, LoadText()); }

        private static void Qte(bool on) { RingMatchPrefs.SetFrom(new RingSettingsValues { Qte = on }); }

        // ---------- (a) рядок дії ----------

        [Test]
        public void ActionRow_ChanceTexts_AreInOrder10Plus_7to9_6Minus_AndMatchThePercents()
        {
            var m = Model(MatchKit.Engine(true));
            int withRoll = 0;
            foreach (var r in m.Actions())
            {
                if (!r.HasRoll) { Assert.AreEqual(0, r.ChanceTexts.Length, r.Id.ToString()); continue; }
                withRoll++;
                Assert.AreEqual(3, r.ChanceTexts.Length, r.Id.ToString());
                Assert.AreEqual(100, r.Percents.Sum(), r.Id.ToString());
                foreach (var s in r.ChanceTexts) { Assert.IsFalse(string.IsNullOrEmpty(s)); Assert.IsFalse(s.Contains("⟦"), s); }
                if (r.RollIsOpponents)
                {
                    Assert.IsTrue(r.ChancesInverted);
                    StringAssert.StartsWith("Вирвався", r.ChanceTexts[0]);
                    StringAssert.StartsWith("На два", r.ChanceTexts[1]);
                    StringAssert.StartsWith("Три", r.ChanceTexts[2]);
                    // «Три» — це ≤6 кидка суперника: число індексу 0
                    StringAssert.Contains(r.Percents[0] + "%", r.ChanceTexts[2]);
                    StringAssert.Contains(r.Percents[2] + "%", r.ChanceTexts[0]);
                }
                else
                {
                    Assert.IsFalse(r.ChancesInverted);
                    Assert.IsTrue(r.MissIsFailure);
                    StringAssert.StartsWith("10+", r.ChanceTexts[0]);
                    StringAssert.StartsWith("7–9", r.ChanceTexts[1]);
                    StringAssert.StartsWith("≤6", r.ChanceTexts[2]);
                    StringAssert.Contains(r.Percents[2] + "%", r.ChanceTexts[0]);
                    StringAssert.Contains(r.Percents[1] + "%", r.ChanceTexts[1]);
                    StringAssert.Contains(r.Percents[0] + "%", r.ChanceTexts[2]);
                }
            }
            Assert.Greater(withRoll, 5);
            Assert.IsTrue(m.Actions().Any(r => r.RollIsOpponents), "утримання серед дій");
        }

        [Test]
        public void ActionRow_CostAndStaminaAfter_ComeFromThePreview_AndWarningsAreText()
        {
            var e = MatchKit.Engine(true);
            var m = Model(e);
            foreach (var r in m.Actions())
            {
                var pv = e.Preview(MatchSide.Player, r.Id);
                Assert.AreEqual(pv.StaminaBefore, r.StaminaBefore, r.Id.ToString());
                Assert.AreEqual(pv.StaminaAfter, r.StaminaAfter, r.Id.ToString());
                Assert.AreEqual(r.Cost > 0 ? r.Cost + " сил" : "—", r.CostText, r.Id.ToString());
                if (r.StaminaAfter != r.StaminaBefore) StringAssert.Contains(r.StaminaBefore + " → " + r.StaminaAfter, r.StaminaAfterText);
                else Assert.AreEqual("", r.StaminaAfterText);
                Assert.IsFalse(r.Winded, "на старті сили є");
                Assert.AreEqual("", r.WindedText);
            }
            var strike = m.Actions().First(r => r.Id == MatchActionId.Strike);
            Assert.AreEqual(strike.StaminaBefore - 1, strike.StaminaAfter);
            // нуль сил: попередження текстом
            e.State.Player.Stamina = 0;
            var tired = Model(e).Actions().First(r => r.Id == MatchActionId.Strike);
            Assert.IsTrue(tired.Winded);
            StringAssert.Contains("−1", tired.WindedText);
            var breath = Model(e).Actions().First(r => r.Id == MatchActionId.CatchBreath);
            Assert.AreEqual(MatchRules.CatchBreathStamina, breath.StaminaAfter, "перевести подих: +3");
        }

        [Test]
        public void HopeSpotRow_MissIsNotAFailure()
        {
            var e = MatchKit.Engine(false, null, s => s.HopeSpotEnabled = true);
            e.State.Rival.Damage = 9; e.State.Player.Damage = 0;
            var intent = Model(e).RivalIntent();
            Assert.AreEqual(MatchActionId.HopeSpot, intent.Action);
            Assert.IsFalse(intent.Row.MissIsFailure);
            StringAssert.Contains("без провалу", intent.Row.ChanceTexts[2]);
            Assert.AreNotEqual("", intent.Row.ChanceNote);
        }

        // ---------- (b) табло ----------

        [Test]
        public void Scoreboard_ShowsInitiativeStaminaOfBothAndThePlace()
        {
            var e = MatchKit.Engine(true, new[] { 12 });
            var m = Model(e);
            var sb = m.Scoreboard();
            Assert.AreEqual(MatchSide.Player, sb.InitiativeSide);
            Assert.AreEqual(1, sb.ChainNumber);
            Assert.AreEqual(m.InitiativeText, sb.InitiativeText);
            Assert.AreEqual(10, sb.Player.Stamina);
            Assert.AreEqual(MatchRules.MaxStamina, sb.Player.StaminaMax);
            Assert.AreEqual(1f, sb.Player.Stamina01, 1e-4f);
            Assert.IsFalse(sb.Player.Winded);
            Assert.AreEqual("На рингу", sb.PlaceLabel);
            Assert.AreEqual("", sb.CountText);
            Assert.IsFalse(sb.Outside);

            MatchKit.Play(e, MatchActionId.LeaveRing);
            sb = m.Scoreboard();
            Assert.IsTrue(sb.Outside);
            Assert.AreEqual("За рингом", sb.PlaceLabel);
            StringAssert.Contains("Відлік", sb.CountText);
            Assert.AreEqual(2, sb.ChainNumber, "×N росте з ланцюгом");
            Assert.Less(sb.Player.Stamina, 10);
            Assert.AreEqual(sb.Player.Stamina / (float)MatchRules.MaxStamina, sb.Player.Stamina01, 1e-4f);

            e.State.Rival.Stamina = 0;
            sb = m.Scoreboard();
            Assert.IsTrue(sb.Rival.Winded);
            StringAssert.Contains("−1", sb.Rival.WindedText);
            Assert.AreEqual(0f, sb.Rival.Stamina01);
        }

        [Test]
        public void Scoreboard_NoDq_HasNoCount_AndTheInitiativeSwitchesSide()
        {
            var e = MatchKit.Engine(true, new[] { 12 }, s => s.Stipulations = MatchStipulations.NoDq);
            MatchKit.Play(e, MatchActionId.LeaveRing);
            var m = Model(e);
            Assert.IsTrue(m.Scoreboard().Outside);
            Assert.AreEqual("", m.Scoreboard().CountText, "у «без DQ» відліку нема");
            var e2 = MatchKit.Engine(false);
            var sb = Model(e2).Scoreboard();
            Assert.AreEqual(MatchSide.Rival, sb.InitiativeSide);
            Assert.IsTrue(sb.Rival.HasInitiative);
            Assert.IsFalse(sb.Player.HasInitiative);
            StringAssert.Contains("суперник", sb.InitiativeText);
        }

        // ---------- (c) хід суперника як свій ----------

        private static string Snapshot(MatchEngine e)
        {
            var sb = new StringBuilder();
            foreach (var ev in e.State.Log)
            {
                sb.Append(ev.Seq).Append(':').Append(ev.Kind).Append(':').Append(ev.Actor).Append(':').Append(ev.Action).Append(':')
                  .Append(ev.Roll).Append(':').Append(ev.Total).Append(':').Append(ev.Tier);
                foreach (var f in ev.Effects) sb.Append('[').Append(f.Kind).Append(f.Who).Append(f.Amount).Append(']');
                sb.Append(';');
            }
            foreach (var f in new[] { e.State.Player, e.State.Rival })
                sb.Append('|').Append(f.Damage).Append('/').Append(f.Momentum).Append('/').Append(f.Stamina).Append('/').Append(f.Posture)
                  .Append('/').Append(f.ReversalTokens);
            sb.Append('|').Append(e.State.Hype).Append('/').Append(e.Phase).Append('/').Append(e.CurrentActor).Append('/').Append(e.Chain);
            return sb.ToString();
        }

        /// <summary>Хід гравця порадою ШІ — лише через команди моделі.</summary>
        private static void PlayerStep(MatchScreenModel m, MatchEngine e)
        {
            switch (m.Input)
            {
                case MatchInput.PlayerAction: Assert.IsTrue(m.Perform(MatchAi.ChooseAction(e))); break;
                case MatchInput.PlayerReaction: Assert.IsTrue(m.React(MatchAi.ChooseReaction(e))); break;
                case MatchInput.PlayerBoost: Assert.IsTrue(m.Commit(MatchAi.ChooseBoost(e))); break;
            }
        }

        [Test]
        public void RivalIntent_IsWhatStepAiDoesFirst_AndNeverMutatesState()
        {
            var seen = new Dictionary<MatchIntentKind, int>();
            int catchBreaths = 0, boosts = 0;
            for (int seed = 1; seed <= 250; seed++)
            {
                var e = new MatchEngine(MatchKit.Setup(s => s.HopeSpotEnabled = seed % 2 == 0), new SeededDiceRoller((ulong)seed));
                var m = Model(e);
                int steps = 0;
                while (!m.IsOver && ++steps < 2000)
                {
                    if (m.Input != MatchInput.Ai) { PlayerStep(m, e); continue; }
                    string before = Snapshot(e);
                    var intent = m.RivalIntent();
                    Assert.IsNotNull(intent);
                    Assert.AreEqual(before, Snapshot(e), "прев'ю наміру не мутує стан, seed " + seed);
                    Assert.AreEqual(before, Snapshot(e));
                    Assert.AreEqual(MatchSide.Rival, intent.Side);
                    StringAssert.DoesNotContain("⟦", intent.Text);
                    int cnt = e.State.Log.Count;
                    m.StepAi();
                    int n; seen.TryGetValue(intent.Kind, out n); seen[intent.Kind] = n + 1;
                    // Дія з кидком спершу стає «чекаю докрутку» (подій ще нема): тоді перша подія прийде кроком докрутки.
                    bool fresh = e.State.Log.Count > cnt;
                    var first = fresh ? e.State.Log[cnt] : null;
                    switch (intent.Kind)
                    {
                        case MatchIntentKind.Action:
                            if (intent.Action == MatchActionId.CatchBreath)
                            {
                                Assert.IsTrue(fresh);
                                Assert.IsTrue(first.Kind == MatchEventKind.TurnEnd || (first.Kind == MatchEventKind.Action && first.Action == MatchActionId.CatchBreath), "seed " + seed);
                                Assert.IsTrue(e.State.Log.Skip(cnt).Any(x => x.Kind == MatchEventKind.TurnEnd), "переведення подиху віддає ініціативу, seed " + seed);
                                catchBreaths++;
                            }
                            else if (fresh)
                            {
                                // великий прийом без жетонів у гравця: перша подія — його автоматичний «продати» цього прийому
                                Assert.IsTrue(first.Kind == MatchEventKind.Action || first.Kind == MatchEventKind.Sell, "seed " + seed + " " + first.Kind);
                                Assert.AreEqual(intent.Action, first.Action, "показана дія — та, що відбулась, seed " + seed);
                                if (first.Kind == MatchEventKind.Action) Assert.AreEqual(MatchSide.Rival, first.Actor);
                            }
                            else
                            {
                                Assert.IsTrue(m.Input == MatchInput.PlayerReaction || m.Input == MatchInput.Ai, "дія чекає реакції чи докрутки, seed " + seed);
                            }
                            break;
                        case MatchIntentKind.Sell: Assert.IsTrue(fresh); Assert.AreEqual(MatchEventKind.Sell, first.Kind, "seed " + seed); break;
                        case MatchIntentKind.Reverse: Assert.IsTrue(fresh); Assert.AreEqual(MatchEventKind.Reverse, first.Kind, "seed " + seed); break;
                        case MatchIntentKind.Boost:
                            Assert.IsTrue(fresh);
                            Assert.AreEqual(MatchEventKind.Action, first.Kind, "seed " + seed);
                            Assert.AreEqual(intent.BoostTier, first.Tier, "ступінь після докрутки, seed " + seed);
                            Assert.AreEqual(intent.Action, first.Action, "докрутка — тієї ж дії, seed " + seed);
                            boosts++;
                            break;
                    }
                }
                Assert.IsTrue(m.IsOver, "seed " + seed);
            }
            Assert.Greater(seen[MatchIntentKind.Action], 1000);
            Assert.Greater(seen[MatchIntentKind.Sell], 5);
            Assert.Greater(catchBreaths, 0);
            Assert.Greater(boosts, 20, "докрутки суперника трапились");
            TestContext.WriteLine("наміри: " + string.Join(", ", seen.Select(kv => kv.Key + "=" + kv.Value)));
        }

        [Test]
        public void RivalIntent_DoesNotTouchTheDice_FullMatchesAreIdenticalWithAndWithoutIt()
        {
            for (int seed = 1; seed <= 60; seed++)
            {
                var a = new MatchEngine(MatchKit.Setup(), new SeededDiceRoller((ulong)seed));
                var b = new MatchEngine(MatchKit.Setup(), new SeededDiceRoller((ulong)seed));
                var ma = Model(a); var mb = Model(b);
                for (int i = 0; i < 2000 && !ma.IsOver; i++)
                {
                    if (ma.Input == MatchInput.Ai) { ma.StepAi(); mb.RivalIntent(); mb.RivalIntent(); mb.StepAi(); }
                    else { PlayerStep(ma, a); PlayerStep(mb, b); }
                }
                Assert.AreEqual(Snapshot(a), Snapshot(b), "seed " + seed);
            }
        }

        [Test]
        public void RivalIntent_ActionRow_UsesTheSameTextsAsThePlayersRow()
        {
            // Суперник ходить першим: його рядок рахується його статами, тексти — ті самі ключі, що в гравця.
            var e = MatchKit.Engine(false);
            var m = Model(e);
            Assert.AreEqual(MatchInput.Ai, m.Input);
            Assert.AreEqual(0, m.Actions().Count, "Actions() для ШІ — як було");
            var i = m.RivalIntent();
            Assert.AreEqual(MatchIntentKind.Action, i.Kind);
            var pv = e.Preview(MatchSide.Rival, i.Action);
            Assert.AreEqual(pv.Cost, i.Row.Cost);
            Assert.AreEqual(pv.StaminaAfter, i.Row.StaminaAfter);
            if (pv.HasRoll) Assert.AreEqual(RingChances.Percent(pv.Chances36).Sum(), i.Row.Percents.Sum());
            Assert.IsNull(Model(MatchKit.Engine(true)).RivalIntent(), "не хід ШІ — наміру нема");
        }

        // ---------- (d) QTE ----------

        [Test]
        public void Qte_HitInsideTheWindow_GivesTheSeqExactlyOnce_AndNoMissAfter()
        {
            var w = new MatchQteWindow();
            Assert.IsTrue(w.Open(7, 1.0f, 1.6f));
            Assert.IsTrue(w.IsActive); Assert.IsFalse(w.IsOpen);
            Assert.AreEqual(QteTick.None, w.Tick(0.9f));
            int seq;
            Assert.IsFalse(w.Press(0.95f, out seq), "до відкриття — ігнор");
            Assert.IsTrue(w.IsActive, "вікно живе");
            Assert.AreEqual(QteTick.Opened, w.Tick(1.0f));
            Assert.IsTrue(w.IsOpen);
            Assert.AreEqual(1f, w.Remaining01(1.0f), 1e-4f);
            Assert.AreEqual(0.5f, w.Remaining01(1.3f), 1e-4f);
            Assert.IsTrue(w.Press(1.2f, out seq));
            Assert.AreEqual(7, seq);
            Assert.IsFalse(w.Press(1.3f, out seq), "подвійний натиск — вікна вже нема");
            Assert.AreEqual(QteTick.None, w.Tick(2f), "після влучання пропуску не буде");
            Assert.IsFalse(w.IsActive);
        }

        [Test]
        public void Qte_NoPress_MissExactlyOnce()
        {
            var w = new MatchQteWindow();
            Assert.IsTrue(w.Open(3, 1f, 1.5f));
            Assert.AreEqual(QteTick.Opened, w.Tick(1.1f));
            Assert.AreEqual(QteTick.None, w.Tick(1.4f));
            Assert.AreEqual(QteTick.Missed, w.Tick(1.6f));
            Assert.AreEqual(QteTick.None, w.Tick(1.7f));
            int seq;
            Assert.IsFalse(w.Press(1.8f, out seq), "пізній натиск не влучає");
            Assert.AreEqual(QteTick.None, w.Tick(2f));
        }

        [Test]
        public void Qte_LargeStep_ReportsOpenedAndMissedTogether()
        {
            var w = new MatchQteWindow();
            w.Open(1, 1f, 1.5f);
            Assert.AreEqual(QteTick.Opened | QteTick.Missed, w.Tick(5f));
            Assert.AreEqual(QteTick.None, w.Tick(6f));
        }

        [Test]
        public void Qte_LatePress_IsAMiss_NotAHit()
        {
            var w = new MatchQteWindow();
            w.Open(1, 1f, 1.5f);
            int seq;
            Assert.IsFalse(w.Press(2f, out seq));
            Assert.AreEqual(QteTick.Opened | QteTick.Missed, w.Tick(2.01f), "пропуск доставлено, рівно раз");
            Assert.AreEqual(QteTick.None, w.Tick(3f));
        }

        [Test]
        public void Qte_Disabled_NeverOpens_AndTheSettingIsReadAtOpenTime()
        {
            var w = new MatchQteWindow();
            Qte(false);
            Assert.IsFalse(w.Open(1, 1f, 1.5f));
            Assert.IsFalse(w.IsActive);
            Assert.AreEqual(QteTick.None, w.Tick(1.2f));
            int seq;
            Assert.IsFalse(w.Press(1.2f, out seq));
            Qte(true);
            Assert.IsTrue(w.Open(1, 1f, 1.5f), "налаштування читається в мить відкриття");
            Qte(false);
            Assert.AreEqual(QteTick.Opened, w.Tick(1.2f), "вікно, що вже відкрито, доживає");
        }

        [Test]
        public void Qte_Cancel_GivesNoMiss_AndInvalidWindowsAreRefused()
        {
            var w = new MatchQteWindow();
            w.Open(1, 1f, 1.5f);
            Assert.AreEqual(QteTick.Opened, w.Tick(1.1f));
            w.Cancel();
            Assert.IsFalse(w.IsActive);
            Assert.AreEqual(QteTick.None, w.Tick(5f), "скасування — без пропуску");
            int seq;
            Assert.IsFalse(w.Press(1.2f, out seq));
            Assert.IsFalse(w.Open(-1, 1f, 2f), "нема події");
            Assert.IsFalse(w.Open(1, 2f, 2f), "порожнє вікно");
            Assert.IsFalse(w.Open(1, 3f, 2f), "вікно навпаки");
            // нове вікно замінює старе без пропуску для старого
            w.Open(1, 1f, 1.5f); w.Tick(1.1f);
            Assert.IsTrue(w.Open(2, 5f, 6f));
            Assert.AreEqual(QteTick.None, w.Tick(5.5f - 0.6f));
            Assert.AreEqual(2, w.Seq);
        }

        [Test]
        public void QteSource_TopRope_OnlyThePlayersLandedMove()
        {
            var e = MatchKit.Engine(true, new[] { 12 });
            MatchKit.Play(e, MatchActionId.TopRope);
            var ev = e.State.Log.Last(x => x.Kind == MatchEventKind.Action);
            Assert.AreEqual(RollTier.Strong, ev.Tier);
            Assert.AreEqual(ev.Seq, MatchQteSource.SeqFor(e.State.Log, ev, "Springboard_Perch"));
            Assert.AreEqual(1, e.RegisterFlourish(MatchQteSource.SeqFor(e.State.Log, ev, "Springboard_Perch")).Count, "ядро приймає цей Seq");

            var miss = MatchKit.Engine(true, new[] { 3 });
            MatchKit.Play(miss, MatchActionId.TopRope);
            var mev = miss.State.Log.Last(x => x.Kind == MatchEventKind.Action);
            Assert.AreEqual(RollTier.Miss, mev.Tier);
            Assert.AreEqual(-1, MatchQteSource.SeqFor(miss.State.Log, mev, "Springboard_Perch"), "провал — вікна нема");

            var rival = MatchKit.Engine(false, new[] { 12 });
            MatchKit.Play(rival, MatchActionId.TopRope);
            var rev = rival.State.Log.Last(x => x.Kind == MatchEventKind.Action);
            Assert.AreEqual(MatchSide.Rival, rev.Actor);
            Assert.AreEqual(-1, MatchQteSource.SeqFor(rival.State.Log, rev, "Springboard_Perch"), "прийом суперника");

            var strike = MatchKit.Engine(true, new[] { 12 });
            MatchKit.Play(strike, MatchActionId.Strike);
            var sev = strike.State.Log.Last(x => x.Kind == MatchEventKind.Action);
            Assert.AreEqual(-1, MatchQteSource.SeqFor(strike.State.Log, sev, "Springboard_Perch"), "інші прийоми без вікна");
            Assert.AreEqual(-1, MatchQteSource.SeqFor(null, null, "x"));
        }

        [Test]
        public void QteSource_Endure_CrankClipWhenThePlayerHolds_MapsToTheSubmissionSeq()
        {
            var e = MatchKit.Engine(true, new[] { 8, 8 });
            e.State.Rival.Posture = Posture.Down; e.State.Rival.Damage = 6; MatchKit.NotFresh(e.State.Rival);
            MatchKit.Play(e, MatchActionId.Submission);
            var sub = e.State.Log.Last(x => x.Kind == MatchEventKind.Action && x.Action == MatchActionId.Submission);
            var endure = e.State.Log.Last(x => x.Kind == MatchEventKind.Endure);
            Assert.AreEqual(MatchSide.Rival, endure.Actor);
            Assert.AreEqual(sub.Seq, MatchQteSource.SeqFor(e.State.Log, endure, "Armbar_Crank"));
            Assert.AreEqual(sub.Seq, MatchQteSource.SeqFor(e.State.Log, endure, "LegLock_Crank_Hype"));
            Assert.AreEqual(-1, MatchQteSource.SeqFor(e.State.Log, endure, "Armbar_Reach"), "не кліп із дотиском");
            Assert.AreEqual(-1, MatchQteSource.SeqFor(e.State.Log, endure, null));
            Assert.AreEqual(1, e.RegisterFlourish(sub.Seq).Count, "ядро приймає Seq больового");

            // захват суперника (гравець терпить): вікно не відкривати
            var r = MatchKit.Engine(false, new[] { 8, 8 });
            r.State.Player.Posture = Posture.Down; r.State.Player.Damage = 6; MatchKit.NotFresh(r.State.Player);
            MatchKit.Play(r, MatchActionId.Submission);
            var rend = r.State.Log.Last(x => x.Kind == MatchEventKind.Endure);
            Assert.AreEqual(MatchSide.Player, rend.Actor);
            Assert.AreEqual(-1, MatchQteSource.SeqFor(r.State.Log, rend, "Armbar_Crank"));
        }

        /// <summary>
        /// Джерело вікна на *_Crank — больовий саме гравця: чужий больовий між ним і «Терпіти» пропускається (рев'ю D 11.11.2026:
        /// без умови «Actor == Player» тести не червоніли). Журнал зібрано руками — порядок подій тут важливіший за правила.
        /// </summary>
        [Test]
        public void QteSource_Endure_SkipsTheRivalsSubmission_TakesThePlayers()
        {
            var log = new List<MatchEvent>
            {
                new MatchEvent { Seq = 0, Kind = MatchEventKind.Action, Action = MatchActionId.Submission, Actor = MatchSide.Player, HasRoll = true, Tier = RollTier.Strong },
                new MatchEvent { Seq = 1, Kind = MatchEventKind.Action, Action = MatchActionId.Submission, Actor = MatchSide.Rival, HasRoll = true, Tier = RollTier.Miss },
                new MatchEvent { Seq = 2, Kind = MatchEventKind.Endure, Actor = MatchSide.Rival },
            };
            Assert.AreEqual(0, MatchQteSource.SeqFor(log, log[2], "Armbar_Crank"), "больовий гравця, не суперника");
        }

        private static void DriveTopRopeAndHolds(MatchEngine e)
        {
            if (e.IsOver) return;
            if (e.Phase == MatchPhase.AwaitReaction) { e.React(ReactionChoice.Sell); return; }
            if (e.Phase == MatchPhase.AwaitBoost) { e.Commit(0); return; }
            foreach (var id in new[] { MatchActionId.GetUp, MatchActionId.Submission, MatchActionId.Pin, MatchActionId.TopRope, MatchActionId.Throw, MatchActionId.Strike })
                if (e.Describe(id).Available) { e.Perform(id); return; }
            e.EndTurn();
        }

        private static string RollsOnly(MatchEngine e)
        {
            var sb = new StringBuilder();
            foreach (var ev in e.State.Log)
            {
                if (ev.Kind == MatchEventKind.Flourish) continue;
                sb.Append(ev.Kind).Append(':').Append(ev.Actor).Append(':').Append(ev.Action).Append(':').Append(ev.Roll).Append(':')
                  .Append(ev.Total).Append(':').Append(ev.Tier);
                foreach (var f in ev.Effects) if (f.Kind != MatchEffectKind.Hype) sb.Append('[').Append(f.Kind).Append(f.Who).Append(f.Amount).Append(']');
                sb.Append(';');
            }
            sb.Append(e.State.Player.Damage).Append('/').Append(e.State.Rival.Damage).Append('/').Append(e.CurrentActor);
            return sb.ToString();
        }

        /// <summary>Вікно QTE (з джерелом і автоматом) на кожній події: кидки, ступені, ушкодження й ініціатива — ті самі, що без нього.</summary>
        [Test]
        public void Qte_WithAndWithout_SameDiceTiersDamageAndInitiative()
        {
            int hits = 0, endureHits = 0, topRopeHits = 0;
            for (int seed = 1; seed <= 200; seed++)
            {
                var a = new MatchEngine(MatchKit.Setup(), new SeededDiceRoller((ulong)seed));
                var b = new MatchEngine(MatchKit.Setup(), new SeededDiceRoller((ulong)seed));
                var w = new MatchQteWindow();
                for (int step = 0; step < 400 && !a.IsOver; step++)
                {
                    int before = b.State.Log.Count;
                    DriveTopRopeAndHolds(a); DriveTopRopeAndHolds(b);
                    for (int i = before; i < b.State.Log.Count; i++)
                    {
                        var ev = b.State.Log[i];
                        string clip = ev.Kind == MatchEventKind.Endure ? "Armbar_Crank" : "Springboard_Perch";
                        int src = MatchQteSource.SeqFor(b.State.Log, ev, clip);
                        if (src < 0 || !w.Open(src, 0f, 1f)) continue;
                        w.Tick(0.1f);
                        int seq;
                        if (!w.Press(0.5f, out seq)) continue;
                        if (b.RegisterFlourish(seq).Count > 0)
                        {
                            hits++;
                            if (ev.Kind == MatchEventKind.Endure) endureHits++; else topRopeHits++;
                        }
                    }
                }
                Assert.AreEqual(a.IsOver, b.IsOver, "seed " + seed);
                Assert.AreEqual(RollsOnly(a), RollsOnly(b), "seed " + seed);
                Assert.GreaterOrEqual(b.State.Hype, a.State.Hype, "seed " + seed);
            }
            Assert.Greater(hits, 20, "QTE справді спрацьовував");
            Assert.Greater(endureHits, 0, "і на больовому");
            TestContext.WriteLine("влучань QTE: " + hits + " (стрибок " + topRopeHits + ", больовий " + endureHits + ")");

            // вимкнено — жодного вікна, жодного ажіотажу понад базовий
            Qte(false);
            var c = new MatchEngine(MatchKit.Setup(), new SeededDiceRoller(5));
            var d = new MatchEngine(MatchKit.Setup(), new SeededDiceRoller(5));
            var w2 = new MatchQteWindow();
            for (int step = 0; step < 400 && !c.IsOver; step++)
            {
                DriveTopRopeAndHolds(c); DriveTopRopeAndHolds(d);
                foreach (var ev in d.State.Log)
                {
                    int src = MatchQteSource.SeqFor(d.State.Log, ev, ev.Kind == MatchEventKind.Endure ? "Armbar_Crank" : "Springboard_Perch");
                    Assert.IsFalse(src >= 0 && w2.Open(src, 0f, 1f));
                }
            }
            Assert.AreEqual(Snapshot(c), Snapshot(d));
        }

        // ---------- (e) журнал ----------

        [Test]
        public void LogEntries_MatchLogTexts_CarryTheActor_AndMarkTheLatest()
        {
            var e = new MatchEngine(MatchKit.Setup(), new SeededDiceRoller(11));
            var m = Model(e);
            for (int i = 0; i < 12 && !m.IsOver; i++)
                if (m.Input == MatchInput.Ai) m.StepAi(); else PlayerStep(m, e);
            var entries = m.LogEntries();
            var texts = m.LogTexts();
            Assert.AreEqual(texts.Count, entries.Count);
            Assert.LessOrEqual(entries.Count, MatchScreenModel.LogLines);
            for (int i = 0; i < entries.Count; i++)
            {
                Assert.AreEqual(texts[i], entries[i].Text);
                Assert.AreEqual(e.State.Log[entries[i].Seq].Actor, entries[i].Actor);
                Assert.AreEqual(i == entries.Count - 1, entries[i].IsLatest);
            }
            Assert.IsTrue(entries.Any(x => x.Actor == MatchSide.Player) && entries.Any(x => x.Actor == MatchSide.Rival));

            // те саме відсікання за показаним: рядок не випереджає тіло
            int cut = entries[entries.Count / 2].Seq;
            var cutEntries = m.LogEntries(cut);
            Assert.AreEqual(cut, cutEntries[cutEntries.Count - 1].Seq);
            Assert.IsTrue(cutEntries[cutEntries.Count - 1].IsLatest);
            Assert.AreEqual(m.LogTexts(cut).Count, cutEntries.Count);
            Assert.AreEqual(0, m.LogEntries(-1).Count);
        }

        // ---------- (f) фокус ----------

        private static List<MatchFocusItem> Items(params string[] spec)
        {
            // "a" — доступний, "!b" — недоступний
            return spec.Select(s => new MatchFocusItem { Id = s.TrimStart('!'), Available = !s.StartsWith("!"), Hint = "hint-" + s.TrimStart('!') }).ToList();
        }

        [Test]
        public void Focus_MovesWithWrap_SkipsUnavailable_AndTabsLikeArrows()
        {
            var f = new MatchFocusModel { DefaultHint = "дефолт" };
            Assert.AreEqual("дефолт", f.FocusHintText);
            f.Set(Items("a", "!b", "c", "d"));
            Assert.AreEqual("a", f.FocusedId, "перший доступний");
            Assert.IsTrue(f.Move(RingAction.Down)); Assert.AreEqual("c", f.FocusedId, "b пропущено");
            Assert.IsTrue(f.Move(RingAction.Right)); Assert.AreEqual("d", f.FocusedId);
            Assert.IsTrue(f.Move(RingAction.TabNext)); Assert.AreEqual("a", f.FocusedId, "по колу вперед");
            Assert.IsTrue(f.Move(RingAction.Up)); Assert.AreEqual("d", f.FocusedId, "по колу назад");
            Assert.IsTrue(f.Move(RingAction.Left)); Assert.AreEqual("c", f.FocusedId);
            Assert.IsTrue(f.Move(RingAction.TabPrev)); Assert.AreEqual("a", f.FocusedId, "b пропущено назад");
            Assert.IsFalse(f.Move(RingAction.Confirm));
            Assert.IsFalse(f.Move(RingAction.Back));
            Assert.IsFalse(f.Move(RingAction.Log));
            Assert.AreEqual("a", f.FocusedId, "не-рух фокус не міняє");
        }

        [Test]
        public void Focus_KeepsTheIdAfterRowsRefresh_AndFallsBackWhenItIsGone()
        {
            var f = new MatchFocusModel();
            f.Set(Items("a", "b", "c"));
            f.Move(RingAction.Down); f.Move(RingAction.Down);
            Assert.AreEqual("c", f.FocusedId);
            f.Set(Items("x", "c", "a"));   // порядок змінився — фокус на тому ж Id
            Assert.AreEqual("c", f.FocusedId);
            Assert.AreEqual(1, f.Index);
            f.Set(Items("x", "!c", "a"));  // став недоступним
            Assert.AreEqual("x", f.FocusedId);
            f.Set(Items("!x", "!a"));
            Assert.AreEqual(-1, f.Index);
            Assert.IsNull(f.FocusedId);
            Assert.IsTrue(f.Move(RingAction.Down), "є що рухати — нічого, але це рух");
            Assert.AreEqual(-1, f.Index);
            f.Set(Items());
            Assert.IsFalse(f.FocusId("x"));
            Assert.IsTrue(new MatchFocusModel().Move(RingAction.Up));
        }

        [Test]
        public void Focus_FromTheScreenModel_HintIsTheMouseHoverText_ForActionsReactionAndBoost()
        {
            var t = LoadText();
            var e = MatchKit.Engine(true);
            var m = new MatchScreenModel(e, t);
            var f = new MatchFocusModel();
            m.RefreshFocus(f);
            Assert.AreEqual(t.Get("match.hint.default"), f.DefaultHint);
            var rows = m.Actions();
            Assert.AreEqual(rows.Count, f.Items.Count);
            Assert.IsNotNull(f.Focused);
            for (int i = 0; i < 40; i++)
            {
                var row = rows.First(r => MatchScreenModel.FocusId(r) == f.FocusedId);
                Assert.IsTrue(row.Available);
                Assert.AreEqual(m.HintFor(row), f.FocusHintText);
                string expect = row.Id == MatchActionId.CatchBreath
                    ? t.Get("match.endturn")
                    : row.Name + "  ·  " + row.CostText + (row.AttrText.Length > 0 ? "  ·  " + row.AttrText + " " + row.ModifierText : "")
                      + (row.HasRoll ? "   |   " + t.Format(row.RollIsOpponents ? "match.action.chances.opp" : "match.chances.short", row.Percents[0], row.Percents[1], row.Percents[2]) : "");
                Assert.AreEqual(expect, f.FocusHintText, "підказка — слово в слово як при наведенні мишею");
                f.Move(RingAction.Down);
            }
            // фокус на тому ж Id після оновлення рядків (екран перебудовується)
            string id = f.FocusedId;
            m.RefreshFocus(f);
            Assert.AreEqual(id, f.FocusedId);

            // реакція: продати / реверс
            var e2 = new MatchEngine(MatchKit.Setup(), new SeededDiceRoller(3));
            var m2 = new MatchScreenModel(e2, t);
            for (int i = 0; i < 800 && m2.Input != MatchInput.PlayerReaction && !m2.IsOver; i++)
                if (m2.Input == MatchInput.Ai) m2.StepAi(); else PlayerStep(m2, e2);
            if (m2.Input == MatchInput.PlayerReaction)
            {
                var f2 = new MatchFocusModel();
                m2.RefreshFocus(f2);
                Assert.AreEqual(2, f2.Items.Count);
                Assert.AreEqual(m2.Reaction().SellText, f2.FocusHintText);
                Assert.AreEqual(m2.Reaction().CanReverse, f2.Items[1].Available);
            }
        }

        [Test]
        public void Focus_BoostOptions_BecomeCandidates()
        {
            var t = LoadText();
            int found = 0;
            for (int seed = 1; seed <= 80 && found == 0; seed++)
            {
                var e = new MatchEngine(MatchKit.Setup(), new SeededDiceRoller((ulong)seed));
                var m = new MatchScreenModel(e, t);
                for (int i = 0; i < 400 && !m.IsOver && m.Input != MatchInput.PlayerBoost; i++)
                    if (m.Input == MatchInput.Ai) m.StepAi(); else PlayerStep(m, e);
                if (m.Input != MatchInput.PlayerBoost || m.BoostOptions().Count < 2) continue;
                found++;
                var f = new MatchFocusModel();
                m.RefreshFocus(f);
                var opts = m.BoostOptions();
                Assert.AreEqual(opts.Count, f.Items.Count);
                Assert.AreEqual(opts[0].Text, f.FocusHintText);
                f.Move(RingAction.Down);
                Assert.AreEqual(opts[1].Text, f.FocusHintText);
            }
            Assert.AreEqual(1, found, "докрутка з вибором трапилась");
        }

        [Test]
        public void Focus_Empty_WhenThereIsNoPlayerInput()
        {
            var m = Model(MatchKit.Engine(false));
            Assert.AreEqual(0, m.FocusItems().Count);
            var f = new MatchFocusModel();
            m.RefreshFocus(f);
            Assert.IsNull(f.Focused);
            Assert.AreEqual(m.DefaultHint, f.FocusHintText);
        }
    }
}
