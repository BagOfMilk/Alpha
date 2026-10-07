using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Characters.Creation;
using Game.Core.Checks;
using Game.Core.Combat;
using Game.Core.Loop;
using Game.Core.Companions;
using Game.Core.Dungeons;
using Game.Core.Economy;
using Game.Core.Quests;
using Game.Core.Scenes;
using Game.Core.Session;
using Game.Core.Session.Views;
using Game.Core.Story;
using Game.Gameplay.Text;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// ROADMAP M1.2 (07.10.2026): споживачі сюжетних прапорів, які раніше лише ставилися
    /// (<c>docs/STORY_FLAGS.md</c>, охоронець <c>StoryFlagReaderGuardTests</c>). Кожен тест — «прапор змінює X»;
    /// кожен перевірено мутацією (прибрати читача — тест червоніє), результат — у повідомленні коміту.
    ///
    /// Закриття (прапор → споживач):
    /// • <c>myroslava_trusted / watched / sent_away</c> → пороги нічної розмови доби 3 (CompanionScenes.ConfrontationThresholds);
    /// • <c>myroslava_hint</c> → поріг тихого шляху вузла 1 (GameSession.ApplyMyroslavaHintBonusIfNeeded);
    /// • <c>pass_vanguard_resolved</c> → ідемпотентність PassVanguardOutcome.Apply;
    /// • <c>myroslava_asked</c>, <c>*_checkup_*</c>, <c>*_ch2_*</c>, <c>maksym_ch1_revenge_clean</c>,
    ///   <c>abandoned_camp_grain_taken</c> → рядки підсумку (StoryEchoes → SummaryView.Echoes);
    /// • <c>arc_*_done</c> знято: те саме каже ArcState.Completed → захист від зради (GameSession.IsArcCompleted);
    /// • <c>tugar_offer_refused</c>, <c>tugar_offer_seen</c>, <c>first_building.*</c> знято (наслідок уже в іншому місці).
    /// </summary>
    public class StoryFlagConsumersTests
    {
        // ---------- хелпери ----------

        private static NewGameOptions Options() =>
            new NewGameOptions { SkipCreation = true, HitRule = HitRuleKind.Threshold, TestBuildOneDayConstruction = true };

        private static void RunSceneToFinish(GameSession s)
        {
            var step = s.AdvanceScene();
            while (!step.IsFinished)
                step = step.IsChoice ? s.ChooseSceneOption(0) : s.AdvanceScene();
        }

        private static void PlayFullDayQuiet(GameSession s)
        {
            s.ConfirmMorning();
            var report = s.AdvanceDay();
            while (report != null && report.AwaitsDecision)
                report = s.ResolveIncident(IncidentPath.Quiet);
            if (report != null && report.Pending == null && s.State == SessionState.Scene)
                RunSceneToFinish(s);

            if (s.State == SessionState.Evening) s.ConfirmEvening();
            if (s.State == SessionState.Night)
            {
                var night = s.AdvanceNight();
                while (night != null && night.AwaitsDecision)
                    night = s.ResolveIncident(IncidentPath.Quiet);
            }
        }

        private static GameSession NewMorningDay1()
        {
            var s = new GameSession();
            s.NewGame(Options());
            RunSceneToFinish(s);
            Assert.AreEqual(SessionState.Morning, s.State);
            return s;
        }

        /// <summary>Вечір доби 3: <paramref name="beforeDay3"/> — налаштування прапорів перед нічною розмовою.</summary>
        private static GameSession ToDay3Evening(Action<GameSession> beforeDay3)
        {
            var s = NewMorningDay1();
            PlayFullDayQuiet(s);
            PlayFullDayQuiet(s);
            beforeDay3?.Invoke(s);

            s.ConfirmMorning();
            var report = s.AdvanceDay();
            while (report != null && report.AwaitsDecision)
                report = s.ResolveIncident(IncidentPath.Quiet);
            if (s.State == SessionState.Scene) RunSceneToFinish(s);
            Assert.AreEqual(SessionState.Evening, s.State);
            Assert.AreEqual(3, s.CurrentView.Day);
            return s;
        }

        private static SceneStepView ConfrontationChoice(GameSession s)
        {
            var step = s.OfferMyroslavaEveningScene();
            Assert.IsNotNull(step, "доба 3 мала відкрити нічну розмову");
            while (step != null && !step.IsChoice && !step.IsFinished) step = s.AdvanceScene();
            Assert.IsTrue(step.IsChoice);
            return step;
        }

        private static (int Persuade, int Accuse) ThresholdsOf(SceneStepView choice)
        {
            int persuade = -1, accuse = -1;
            foreach (var o in choice.Options)
            {
                if (o.SkillKey == SkillKeys.Persuade.Id) persuade = o.Threshold;
                if (o.SkillKey == SkillKeys.Intimidate.Id) accuse = o.Threshold;
            }
            return (persuade, accuse);
        }

        private static (int Persuade, int Accuse) SessionConfrontationThresholds(params string[] flags)
        {
            var s = ToDay3Evening(g =>
            {
                g.DebugSetFlag(Defection.DefectorSeededFlag); // зрада насуває — нічна розмова, а не тиха перевірка
                foreach (var f in flags) g.DebugSetFlag(f);
            });
            return ThresholdsOf(ConfrontationChoice(s));
        }

        private static int PassThreshold(GameSession s) => s.DebugQuietThreshold("pass_vanguard", "opening.pass");

        // ============ Мирослава, глава 1: trusted / watched / sent_away -> пороги нічної розмови ============

        [Test]
        public void ConfrontationThresholds_PureFunction_ShiftsByCh1Choice()
        {
            Assert.AreEqual((4, 5), CompanionScenes.ConfrontationThresholds(false, false, false));
            Assert.AreEqual((3, 5), CompanionScenes.ConfrontationThresholds(true, false, false), "довіра — «переконати» легше");
            Assert.AreEqual((4, 4), CompanionScenes.ConfrontationThresholds(false, true, false), "пильнування — «звинуватити» легше");
            Assert.AreEqual((5, 5), CompanionScenes.ConfrontationThresholds(false, false, true), "відіслана — «переконати» важче");
        }

        [Test]
        public void Confrontation_Trusted_MakesPersuadeEasier()
        {
            var baseline = SessionConfrontationThresholds();
            var trusted = SessionConfrontationThresholds(CompanionScenes.MyroslavaTrustedFlag);

            Assert.AreEqual(CompanionScenes.ConfrontationPersuadeBaseThreshold, baseline.Persuade);
            Assert.AreEqual(baseline.Persuade - 1, trusted.Persuade, "myroslava_trusted мав полегшити «переконати» на 1");
            Assert.AreEqual(baseline.Accuse, trusted.Accuse, "«звинуватити» довіра не чіпає");
        }

        [Test]
        public void Confrontation_Watched_MakesAccuseEasier()
        {
            var baseline = SessionConfrontationThresholds();
            var watched = SessionConfrontationThresholds(CompanionScenes.MyroslavaWatchedFlag);

            Assert.AreEqual(baseline.Accuse - 1, watched.Accuse, "myroslava_watched мав полегшити «звинуватити» на 1");
            Assert.AreEqual(baseline.Persuade, watched.Persuade);
        }

        [Test]
        public void Confrontation_SentAway_MakesPersuadeHarder()
        {
            var baseline = SessionConfrontationThresholds();
            var sentAway = SessionConfrontationThresholds(CompanionScenes.MyroslavaSentAwayFlag);

            Assert.AreEqual(baseline.Persuade + 1, sentAway.Persuade, "myroslava_sent_away мав ускладнити «переконати» на 1");
            Assert.AreEqual(baseline.Accuse, sentAway.Accuse);
        }

        // ============ Мирослава, розмова про батька: hint -> поріг тихого шляху вузла 1 ============

        [Test]
        public void Hint_LowersQuietThreshold_OfNode1()
        {
            var plain = NewMorningDay1();
            var hinted = NewMorningDay1();
            hinted.DebugSetFlag(OpeningScenes.MyroslavaHintFlag);

            Assert.AreEqual(PassThreshold(plain) - 1, PassThreshold(hinted),
                "myroslava_hint мав полегшити тихий шлях вузла 1 рівно на 1");
        }

        [Test]
        public void Hint_BonusIsAppliedOnlyOnce()
        {
            var s = NewMorningDay1();
            int before = PassThreshold(s);
            s.DebugSetFlag(OpeningScenes.MyroslavaHintFlag);
            s.DebugSetFlag(OpeningScenes.MyroslavaHintFlag);
            Assert.AreEqual(before - 1, PassThreshold(s), "повторне виставлення прапора не знімає поріг вдруге");
        }

        [Test]
        public void Hint_SurvivesSaveAndContinue()
        {
            var s = NewMorningDay1();
            s.DebugSetFlag(OpeningScenes.MyroslavaHintFlag);
            int expected = PassThreshold(s);

            var restored = new GameSession();
            restored.PreloadSlot(1, s.SaveState(1));
            Assert.IsTrue(restored.ContinueGame(1));
            Assert.AreEqual(expected, PassThreshold(restored), "поріг вузла 1 не входить у зліпок — прапор мусить його відновити");
        }

        [Test]
        public void AskMyroslava_OnAGoodBand_GivesTheHint_AndLowersTheThreshold()
        {
            // Гравець-торговець (Переконання 4) питає Мирославу: прев'ю каже заздалегідь, чи вийде, — і тест звіряється з ним.
            int Run(out string band, out bool hint)
            {
                var s = new GameSession();
                s.NewGame(new NewGameOptions { SkipCreation = false, HitRule = HitRuleKind.Threshold });
                s.SetProtagonistName("Торговець");
                s.SetProtagonistBackground("trader");
                s.ConfirmCreation();

                var step = s.AdvanceScene();
                while (!step.IsChoice) step = s.AdvanceScene();
                int ask = -1;
                for (int i = 0; i < step.Options.Count; i++)
                    if (step.Options[i].TextKey == "scene.neighbour.option.ask_myroslava") ask = i;
                Assert.GreaterOrEqual(ask, 0);
                band = step.Options[ask].ExpectedBand;
                step = s.ChooseSceneOption(ask);
                while (!step.IsFinished) step = step.IsChoice ? s.ChooseSceneOption(0) : s.AdvanceScene();
                hint = s.DebugHasFlag(OpeningScenes.MyroslavaHintFlag);
                return PassThreshold(s);
            }

            int refusedThreshold;
            {
                var r = NewMorningDay1();
                refusedThreshold = PassThreshold(r);
            }

            int threshold = Run(out string band, out bool hint);
            bool good = band == "Good" || band == "Best";
            Assert.AreEqual(good, hint, "підказка Мирослави — лише на Good/Best смузі розмови (було: " + band + ")");
            if (good) Assert.Less(threshold, refusedThreshold, "підказка мала полегшити вузол 1");
        }

        // ============ pass_vanguard_resolved -> Apply рівно один раз ============

        private static (BaseState state, Companion maksym, Companion myroslava, ResourceLedger ledger) NewOutcomeWorld()
        {
            var roster = new Roster();
            var ledger = new ResourceLedger();
            var state = new BaseState(roster, ledger, new BalanceConfig());
            var maksym = new CompanionArchetype("maksym", "Максим Беркут").CreateInstance("maksym");
            var myroslava = new CompanionArchetype("myroslava", "Мирослава").CreateInstance("myroslava");
            roster.Add(maksym);
            roster.Add(myroslava);
            ledger.Add(ResourceType.BuildComponent, 40);
            ledger.Add(ResourceType.Food, 40);
            return (state, maksym, myroslava, ledger);
        }

        [Test]
        public void PassVanguardOutcome_Apply_IsIdempotent_ViaResolvedFlag()
        {
            var (state, maksym, _, ledger) = NewOutcomeWorld();
            var flags = new StoryFlags();

            PassVanguardOutcome.Apply(state, OutcomeBand.Worst, wasBloody: true, flags);
            double injuryAfterFirst = maksym.InjuryPoints;
            int buildAfterFirst = ledger.Get(ResourceType.BuildComponent);
            int foodAfterFirst = ledger.Get(ResourceType.Food);
            Assert.Greater(injuryAfterFirst, 0.0, "перший виклик ранить");
            Assert.Less(buildAfterFirst, 40, "перший виклик грабує склад");
            Assert.IsTrue(flags.Get(PassVanguardOutcome.ResolvedFlag));

            PassVanguardOutcome.Apply(state, OutcomeBand.Worst, wasBloody: true, flags);
            Assert.AreEqual(injuryAfterFirst, maksym.InjuryPoints, "вузол розв'язано — вдруге не ранить");
            Assert.AreEqual(buildAfterFirst, ledger.Get(ResourceType.BuildComponent), "вдруге не грабує");
            Assert.AreEqual(foodAfterFirst, ledger.Get(ResourceType.Food));
        }

        [Test]
        public void PassVanguardOutcome_Apply_WithoutFlags_StillAppliesEveryTime()
        {
            var (state, _, _, ledger) = NewOutcomeWorld();
            PassVanguardOutcome.Apply(state, OutcomeBand.Base, wasBloody: false, flags: null);
            int afterFirst = ledger.Get(ResourceType.BuildComponent);
            PassVanguardOutcome.Apply(state, OutcomeBand.Base, wasBloody: false, flags: null);
            Assert.LessOrEqual(ledger.Get(ResourceType.BuildComponent), afterFirst,
                "без прапорів (немає пам'яті) контракт не змінено: викликач сам відповідає за «один раз»");
        }

        // ============ арка завершена -> не зраджує (замість arc_*_done) ============

        private static void SetLoyalty(GameSession s, string companionId, int loyalty)
        {
            var field = typeof(GameSession).GetField("_worldRoster", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field);
            var roster = (Roster)field.GetValue(s);
            roster.Get(companionId).RestoreLoyaltyForSave(loyalty);
        }

        private static void TickDefectionWatch(GameSession s, int days)
        {
            var method = typeof(GameSession).GetMethod("TickDefectionWatch", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method);
            for (int i = 0; i < days; i++) method.Invoke(s, null);
        }

        private static CompanionStatus StatusOf(GameSession s, string id) =>
            s.GetRosterView().Companions.First(c => c.Id == id).Status;

        [Test]
        public void LowLoyalty_Defects_ButNotAfterThePersonalArcIsCompleted()
        {
            // Контроль: без завершеної арки низька лояльність за п'ять діб веде до зради.
            var control = NewMorningDay1();
            SetLoyalty(control, "maksym", 0);
            TickDefectionWatch(control, 6);
            Assert.AreEqual(CompanionStatus.Antagonist, StatusOf(control, "maksym"), "контроль: Максим із нульовою лояльністю мав зрадити");

            // Те саме, але арку Максима завершено (обидві глави): конфлікт розв'язано, він лишається.
            var completed = NewMorningDay1();
            completed.DebugCompleteArc("maksym");
            Assert.IsTrue(completed.DebugIsArcCompleted("maksym"));
            SetLoyalty(completed, "maksym", 0);
            TickDefectionWatch(completed, 6);
            Assert.AreNotEqual(CompanionStatus.Antagonist, StatusOf(completed, "maksym"),
                "хто завершив особисту арку, не зраджує — навіть із низькою лояльністю");
        }

        private static AssaultPlan FinalePlan(GameSession s)
        {
            var method = typeof(GameSession).GetMethod("BuildFinalePlan", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method);
            return (AssaultPlan)method.Invoke(s, null);
        }

        [Test]
        public void FinalePlan_SeededMyroslava_IsAnEnemy_UnlessHerArcIsCompleted()
        {
            var seeded = NewMorningDay1();
            seeded.DebugSetFlag(Defection.DefectorSeededFlag);
            Assert.AreEqual("myroslava", FinalePlan(seeded).DefectorCompanionId, "контроль: посіяна зрада — Мирослава серед ворогів");

            var completed = NewMorningDay1();
            completed.DebugSetFlag(Defection.DefectorSeededFlag);
            completed.DebugCompleteArc("myroslava");
            Assert.IsNull(FinalePlan(completed).DefectorCompanionId,
                "арку Мирослави завершено — вона вже не зрадить, у фіналі вона не ворог");
        }

        // ============ Відлуння рішень: рядки підсумку ============

        private static StoryFlags FlagsWith(params string[] ids)
        {
            var f = new StoryFlags();
            foreach (var id in ids) f.Set(id);
            return f;
        }

        private static string Key(string id) => StoryEchoes.TextKeyPrefix + id;

        [Test]
        public void Echoes_NoFlags_NoLines()
        {
            CollectionAssert.IsEmpty(StoryEchoes.Collect(new StoryFlags(), _ => true));
            CollectionAssert.IsEmpty(StoryEchoes.Collect(null, _ => true));
        }

        [TestCase(CompanionScenes.MyroslavaTrustedFlag, StoryEchoes.MyroslavaTrusted)]
        [TestCase(CompanionScenes.MyroslavaWatchedFlag, StoryEchoes.MyroslavaWatched)]
        [TestCase(CompanionScenes.MyroslavaSentAwayFlag, StoryEchoes.MyroslavaSentAway)]
        [TestCase(CompanionScenes.MyroslavaCheckupReassureFlag, StoryEchoes.MyroslavaCheckupReassure)]
        [TestCase(CompanionScenes.MyroslavaCheckupSpaceFlag, StoryEchoes.MyroslavaCheckupSpace)]
        [TestCase(OpeningScenes.MyroslavaHintFlag, StoryEchoes.MyroslavaHint)]
        [TestCase(OpeningScenes.MyroslavaAskedFlag, StoryEchoes.MyroslavaAsked)]
        [TestCase(DefaultQuests.MaksymCh1RevengeCleanFlag, StoryEchoes.MaksymRevengeClean)]
        [TestCase(DefaultDungeon.AbandonedCampGrainTakenFlag, StoryEchoes.AbandonedCampGrainTaken)]
        public void Echoes_FlagProducesItsLine(string flag, string echoId)
        {
            CollectionAssert.AreEqual(new[] { Key(echoId) }, StoryEchoes.Collect(FlagsWith(flag)),
                "прапор " + flag + " мав дати рівно один рядок підсумку");
        }

        [TestCase(CompanionScenes.MyroslavaCh2RememberFlag, "myroslava", StoryEchoes.MyroslavaCh2Remember)]
        [TestCase(CompanionScenes.MyroslavaCh2SilenceFlag, "myroslava", StoryEchoes.MyroslavaCh2Silence)]
        [TestCase(CompanionScenes.MaksymCh2ForgiveFlag, "maksym", StoryEchoes.MaksymCh2Forgive)]
        [TestCase(CompanionScenes.MaksymCh2GuardFlag, "maksym", StoryEchoes.MaksymCh2Guard)]
        public void Echoes_Chapter2Epilogue_OnlyWhenTheArcIsCompleted(string flag, string companionId, string echoId)
        {
            var flags = FlagsWith(flag);
            CollectionAssert.IsEmpty(StoryEchoes.Collect(flags), "арку не завершено — епілог не відлунює");
            CollectionAssert.IsEmpty(StoryEchoes.Collect(flags, id => false));
            CollectionAssert.AreEqual(new[] { Key(echoId) }, StoryEchoes.Collect(flags, id => id == companionId));
            CollectionAssert.IsEmpty(StoryEchoes.Collect(flags, id => id != companionId), "завершена арка ІНШОГО напарника не рахується");
        }

        [Test]
        public void Echoes_HintWinsOverAsked_AndOrderIsStable()
        {
            var flags = FlagsWith(OpeningScenes.MyroslavaAskedFlag, OpeningScenes.MyroslavaHintFlag,
                CompanionScenes.MyroslavaTrustedFlag, DefaultDungeon.AbandonedCampGrainTakenFlag,
                DefaultQuests.MaksymCh1RevengeCleanFlag);
            var echoes = StoryEchoes.Collect(flags);

            CollectionAssert.AreEqual(
                new[]
                {
                    Key(StoryEchoes.MyroslavaHint), Key(StoryEchoes.MyroslavaTrusted),
                    Key(StoryEchoes.MaksymRevengeClean), Key(StoryEchoes.AbandonedCampGrainTaken)
                },
                echoes, "підказка заступає «питав»; порядок: Мирослава → Максим → світ");
            CollectionAssert.AreEqual(echoes, StoryEchoes.Collect(flags), "детерміновано: той самий вхід — той самий вихід");
        }

        [Test]
        public void Echoes_EveryLine_HasUkrainianText_InBothGenders()
        {
            foreach (var id in StoryEchoes.AllIds)
            {
                Assert.IsTrue(UkrainianText.Has(Key(id), Gender.Male), "немає тексту для " + Key(id));
                Assert.IsTrue(UkrainianText.Has(Key(id), Gender.Female), "немає тексту для " + Key(id) + " (жін.)");
            }
            Assert.IsTrue(UkrainianText.Has("summary.echoes", Gender.Male));
        }

        [Test]
        public void Echoes_AllIds_AreReachable()
        {
            var flags = FlagsWith(
                OpeningScenes.MyroslavaHintFlag, CompanionScenes.MyroslavaTrustedFlag, CompanionScenes.MyroslavaWatchedFlag,
                CompanionScenes.MyroslavaSentAwayFlag, CompanionScenes.MyroslavaCheckupReassureFlag, CompanionScenes.MyroslavaCheckupSpaceFlag,
                CompanionScenes.MyroslavaCh2RememberFlag, CompanionScenes.MyroslavaCh2SilenceFlag, DefaultQuests.MaksymCh1RevengeCleanFlag,
                CompanionScenes.MaksymCh2ForgiveFlag, CompanionScenes.MaksymCh2GuardFlag, DefaultDungeon.AbandonedCampGrainTakenFlag);
            var keys = StoryEchoes.Collect(flags, _ => true);
            foreach (var id in StoryEchoes.AllIds.Where(i => i != StoryEchoes.MyroslavaAsked)) // «питав» заступає підказка
                CollectionAssert.Contains(keys, Key(id), "рядок " + id + " недосяжний");
        }

        [Test]
        public void SummaryView_CarriesTheEchoes_FromSessionFlags()
        {
            var s = NewMorningDay1();
            CollectionAssert.IsEmpty(s.GetSummaryView().Echoes, "на свіжій грі громада ще нічого не запам'ятала");

            s.DebugSetFlag(DefaultDungeon.AbandonedCampGrainTakenFlag);
            s.DebugSetFlag(CompanionScenes.MaksymCh2ForgiveFlag);
            CollectionAssert.AreEqual(new[] { Key(StoryEchoes.AbandonedCampGrainTaken) }, s.GetSummaryView().Echoes,
                "епілог глави 2 Максима без завершеної арки не відлунює");

            s.DebugCompleteArc("maksym");
            CollectionAssert.AreEqual(new[] { Key(StoryEchoes.MaksymCh2Forgive), Key(StoryEchoes.AbandonedCampGrainTaken) },
                s.GetSummaryView().Echoes);
        }

        // ============ знято: службові й дубльовані прапори не повертаються ============

        [Test]
        public void RemovedFlags_AreNotSetByTheOpening()
        {
            var s = NewMorningDay1();
            foreach (var removed in new[] { "tugar_offer_seen", "tugar_offer_refused", "arc_myroslava_done", "arc_maksym_done" })
                Assert.IsFalse(s.DebugHasFlag(removed), removed + " знято в M1.2 і не має виставлятися");
            foreach (var b in DefaultBuildings.FirstBuildingChoices)
                Assert.IsFalse(s.DebugHasFlag("first_building." + b), "прапор першої будівлі знято в M1.2");
        }

        [Test]
        public void RefuseTugar_StillShiftsFactionsDirectly_WithoutAFlag()
        {
            var scene = OpeningScenes.NeighbourWithADemand();
            var choice = scene.Steps.First(st => st.Kind == SceneStepKind.Choice && st.ActorId == OpeningScenes.TugarOfferChoiceId);
            var refuse = choice.Options.First(o => o.Id == "refuse");

            Assert.AreEqual(10, refuse.Consequence.FactionDeltas[OpeningScenes.CommunityFactionId]);
            Assert.AreEqual(-10, refuse.Consequence.FactionDeltas[OpeningScenes.TuharBoyarsFactionId]);
            CollectionAssert.IsEmpty(refuse.Consequence.Flags, "наслідок відмови — у фракціях, не в прапорі без читача");
        }

        [Test]
        public void DefaultArcs_NoLongerSetDoneFlags_ButArcStateSaysCompleted()
        {
            foreach (var arc in DefaultArcs.All())
                foreach (var chapter in arc.Chapters)
                    StringAssert.DoesNotEndWith("_done", chapter.CompletionFlag ?? "", "арка " + arc.Id + ": прапор «done» знято");

            var run = new CompanionArcRun(DefaultArcs.Maksym(), new HashSet<string>());
            run.CompleteChapter();
            run.CompleteChapter();
            Assert.AreEqual(ArcState.Completed, run.State);
        }
    }
}
