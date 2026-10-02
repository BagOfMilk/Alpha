using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Game.Core.Combat;
using Game.Core.Loop;
using Game.Core.Randomness;
using Game.Core.Session;
using Game.Core.Session.Bots;
using Game.Core.Story;
using Game.Gameplay.Combat;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Темп і прохідність ВЕЛИКОГО бою — фіналу «Тримати перевал» (ROADMAP M1.3 і M1.5;
    /// Поправка №11.1: «довше 5 раундів — лише масштабні події і великі битви» і №11.5: фінал
    /// «не довгий, а миттєвий»; Поправка №1: кривавий шлях «дуже складний, але швидкий»;
    /// GDD §7.15: «жодна полоса не чиста перемога»).
    ///
    /// До 01.10.2026 фінал був НЕПРОХІДНИМ за будь-якої готовності й складу: 0 перемог з 20
    /// навіть у Fortified з чотирма бійцями, бій тривав 1–3 раунди (Бурунда знімав бійця за хід).
    /// Тому склад, який гравець обирає (Поправка №17.2), нічого не вирішував. Усі числа нижче —
    /// ПЛЕЙСХОЛДЕРИ, підібрані вимірюванням: кількість рядових за полосою
    /// (<see cref="Game.Core.Balance.ReadinessBalance.AssaultEnemyCountByBand"/>) і HP/броня Бурунди.
    /// Правиш числа контенту — правиш і ці межі, свідомо.
    ///
    /// «Грамотна гра» — <see cref="CombatAi"/> за обидві сторони (<c>CombatAutoResolve</c>);
    /// «наївна» — «атакуй найближчого, інакше зближуйся» (як у ботів).
    /// </summary>
    public class FinalePacingTests
    {
        private const int SeedCount = 20;

        // Найсильніший склад, який може обрати гравець: у цьому порядку, до ліміту.
        private static readonly string[] SquadPreference = { "maksym", "myroslava", "zakhar", "keeper", "goban" };

        private static readonly int[] ReadinessValueByBand = { 0, 25, 55, 90 }; // межі полос {25, 55, 90}
        private static readonly ReadinessBand[] Bands =
            { ReadinessBand.Unprepared, ReadinessBand.Bracing, ReadinessBand.Ready, ReadinessBand.Fortified };

        // ---- Межі (ПЛЕЙСХОЛДЕР, підібрано вимірюванням 01.10.2026: wins 2/6/14/20 з 20 на повному складі) ----

        /// <summary>Фінал не «миттєвий»: на повному складі грамотна гра триває в медіані не менше стільки раундів (було 1–2).</summary>
        private const int FullSquadMedianRoundsMin = 3;

        /// <summary>Фінал не марафон: жоден прогін не довший за це (запобіжник — 40; малі бої — 5).</summary>
        private const int RoundsHardMax = 12;

        private const double FortifiedWinRateMin = 0.85;
        private const double ReadyWinRateMin = 0.45;
        private const double BracingWinRateMax = 0.60;
        private const double UnpreparedWinRateMax = 0.30;

        /// <summary>Склад вирішує: протагоніст сам виграє не більше цієї частки прогонів у будь-якій полосі.</summary>
        private const double SoloWinRateMax = 0.10;

        /// <summary>«Жодна полоса не чиста перемога»: у Fortified хтось із загону падає щонайменше в такій частці прогонів.</summary>
        private const double FortifiedSomeoneFellRateMin = 0.50;

        /// <summary>Наївна гра в слабких полосах (Unprepared, Bracing) виграє не більше цієї частки.</summary>
        private const double NaiveWeakBandsWinRateMax = 0.20;

        // ---------- вимірювання ----------

        private struct Run
        {
            public int Rounds;
            public bool Won;
            public bool SomeoneFell;
            public int Party;
        }

        private static void RunScene(GameSession s)
        {
            var step = s.AdvanceScene();
            while (!step.IsFinished) step = step.IsChoice ? s.ChooseSceneOption(0) : s.AdvanceScene();
        }

        private static void QuietDay(GameSession s)
        {
            s.ConfirmMorning();
            var report = s.AdvanceDay();
            while (report != null && report.AwaitsDecision) report = s.ResolveIncident(IncidentPath.Quiet);
            if (report != null && report.Pending == null && s.State == SessionState.Scene) RunScene(s);
            if (s.State == SessionState.Evening) s.ConfirmEvening();
            if (s.State == SessionState.Night)
            {
                var night = s.AdvanceNight();
                while (night != null && night.AwaitsDecision) night = s.ResolveIncident(IncidentPath.Quiet);
            }
        }

        /// <summary>Ніч доби 5 з потрібною готовністю (internal-значення ставимо рефлексією — публічної команди для цього нема).</summary>
        private static GameSession FinaleNight(HitRuleKind rule, int seed, int readinessValue)
        {
            var s = new GameSession(new SeededDiceRoller((ulong)Math.Max(1, seed)));
            s.NewGame(new NewGameOptions { SkipCreation = true, HitRule = rule, Seed = (ulong)seed, TestBuildOneDayConstruction = true });
            RunScene(s);
            for (int day = 1; day <= 4; day++) QuietDay(s);

            // Вранці доби 5 гравець звільняє людей з постів: інакше їх не можна обрати (Поправка №17.2).
            foreach (var c in s.GetRosterView().Companions.Where(c => !string.IsNullOrEmpty(c.AssignedSlotId)).ToList())
                s.Unassign(c.AssignedSlotId);

            s.ConfirmMorning();
            var report = s.AdvanceDay();
            while (report != null && report.AwaitsDecision) report = s.ResolveIncident(IncidentPath.Quiet);
            if (s.State == SessionState.Scene) RunScene(s);
            s.ReactToCrisis(CrisisReaction.SpendGold);

            var field = typeof(GameSession).GetField("_readiness", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "GameSession._readiness мало існувати");
            var track = (ReadinessTrack)field.GetValue(s);
            var value = typeof(ReadinessTrack).GetProperty("Value", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            var add = typeof(ReadinessTrack).GetMethod("Add", BindingFlags.NonPublic | BindingFlags.Instance);
            int delta = readinessValue - (int)value.GetValue(track);
            if (delta != 0) add.Invoke(track, new object[] { delta });

            s.ConfirmEvening();
            Assert.AreEqual(SessionState.Night, s.State);
            return s;
        }

        private static void NaiveFight(GameSession s)
        {
            int guard = 0;
            while (s.State == SessionState.Battle && guard++ < 4000)
            {
                var view = s.GetBattleView();
                var current = BotSupport.FindCurrent(view);
                if (current == null) { s.CombatEndTurn(); continue; }
                var target = BotSupport.FindNearestOpposite(view, current);
                if (target == null) { s.CombatEndTurn(); continue; }
                if (s.CombatAttack(target.Id) == CombatActionResult.Success) continue;
                var step = BotSupport.StepToward(view, current, target.Pos);
                if (step.HasValue && s.CombatMove(new GridPos(step.Value.X, step.Value.Y)) == CombatActionResult.Success) continue;
                s.CombatEndTurn();
            }
            Assert.AreNotEqual(SessionState.Battle, s.State, "наївний бій мав завершитись до запобіжника, а не зависнути");
        }

        /// <summary>Один прогін фіналу: <paramref name="allies"/> — скільки напарників бере гравець (найсильніші за <see cref="SquadPreference"/>).</summary>
        private static Run Fight(HitRuleKind rule, int seed, ReadinessBand band, int allies, bool naive)
        {
            var s = FinaleNight(rule, seed, ReadinessValueByBand[Array.IndexOf(Bands, band)]);
            Assert.AreEqual(band, ReadinessBandOf(s), "готовність мала стати саме такою полосою");

            var view = s.GetFinaleView();
            var squad = SquadPreference.Where(id => view.Candidates.Any(c => c.CompanionId == id && c.Selectable))
                .Take(Math.Min(allies, view.PartyMax - 1)).ToList();
            s.ResolveFinale(IncidentPath.Bloody, squad);
            Assert.AreEqual(SessionState.Battle, s.State);

            if (naive) NaiveFight(s); else s.CombatAutoResolve();

            string outcome = null;
            int rounds = 0;
            foreach (var e in s.DayLog)
            {
                if (e.Key != "combat.autoresolved" && e.Key != "combat.battle.resolved") continue;
                outcome = e.Args["outcome"];
                rounds = int.Parse(e.Args["rounds"], System.Globalization.CultureInfo.InvariantCulture);
            }
            Assert.IsNotNull(outcome, "бій мав залишити combat.autoresolved/combat.battle.resolved у DayLog");

            var last = s.LastResolvedBattleView;
            Assert.IsNotNull(last);
            return new Run
            {
                Rounds = rounds,
                Won = outcome == "Victory",
                SomeoneFell = last.Units.Any(u => u.Side == "Player" && u.Hp <= 0),
                Party = squad.Count + 1
            };
        }

        private static ReadinessBand ReadinessBandOf(GameSession s)
        {
            var field = typeof(GameSession).GetField("_readiness", BindingFlags.NonPublic | BindingFlags.Instance);
            return ((ReadinessTrack)field.GetValue(s)).Band;
        }

        private static List<Run> Percent(ReadinessBand band, int allies, bool naive)
        {
            var runs = new List<Run>();
            for (int seed = 1; seed <= SeedCount; seed++) runs.Add(Fight(HitRuleKind.Percent, seed, band, allies, naive));
            return runs;
        }

        private static double Rate(IEnumerable<Run> runs, Func<Run, bool> pick)
        {
            var list = runs.ToList();
            return list.Count(pick) / (double)list.Count;
        }

        private static double MedianRounds(IEnumerable<Run> runs)
        {
            var sorted = runs.Select(r => (double)r.Rounds).OrderBy(x => x).ToList();
            int n = sorted.Count;
            return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
        }

        // ---------- охоронці ----------

        private const int FullSquadAllies = 3; // протагоніст + троє = FinalePartyMax 4

        [Test]
        public void FullSquad_Competent_WinRateGrowsWithReadiness_AndMatchesTheTargets()
        {
            var byBand = Bands.ToDictionary(b => b, b => Percent(b, FullSquadAllies, naive: false));
            foreach (var kv in byBand)
                Assert.AreEqual(4, kv.Value[0].Party, "повний склад — протагоніст і троє (FinalePartyMax = 4)");

            double unprep = Rate(byBand[ReadinessBand.Unprepared], r => r.Won);
            double bracing = Rate(byBand[ReadinessBand.Bracing], r => r.Won);
            double ready = Rate(byBand[ReadinessBand.Ready], r => r.Won);
            double fortified = Rate(byBand[ReadinessBand.Fortified], r => r.Won);

            string line = $"перемог: Unprepared {unprep:P0}, Bracing {bracing:P0}, Ready {ready:P0}, Fortified {fortified:P0}";
            Assert.LessOrEqual(unprep, UnpreparedWinRateMax, line + " — непідготовлена громада мала лишатись у меншості");
            Assert.LessOrEqual(bracing, BracingWinRateMax, line);
            Assert.GreaterOrEqual(ready, ReadyWinRateMin, line + " — готова громада мала вигравати часто");
            Assert.GreaterOrEqual(fortified, FortifiedWinRateMin, line + " — укріплена громада мала вигравати майже завжди");
            Assert.LessOrEqual(unprep, bracing, line + " — підготовка не може шкодити");
            Assert.LessOrEqual(bracing, ready, line);
            Assert.LessOrEqual(ready, fortified, line);
        }

        [Test]
        public void FullSquad_Competent_IsABigBattle_NotInstantAndNotAMarathon()
        {
            foreach (var band in Bands)
            {
                var runs = Percent(band, FullSquadAllies, naive: false);
                double median = MedianRounds(runs);
                int max = runs.Max(r => r.Rounds);
                Assert.GreaterOrEqual(median, FullSquadMedianRoundsMin,
                    $"{band}: фінал мав тривати в медіані не менше {FullSquadMedianRoundsMin} раундів (великий бій), вийшло {median}");
                Assert.LessOrEqual(max, RoundsHardMax,
                    $"{band}: жоден прогін фіналу не мав перевищити {RoundsHardMax} раундів, вийшло {max}");
            }
        }

        [Test]
        public void Fortified_FullSquad_IsAVictoryThatCostsSomething()
        {
            var runs = Percent(ReadinessBand.Fortified, FullSquadAllies, naive: false);
            double fell = Rate(runs, r => r.SomeoneFell);
            Assert.GreaterOrEqual(fell, FortifiedSomeoneFellRateMin,
                $"«жодна полоса не чиста перемога» (GDD §7.15): у Fortified хтось мав падати щонайменше в {FortifiedSomeoneFellRateMin:P0} прогонів, вийшло {fell:P0}");
        }

        [Test]
        public void Threshold_FullSquad_Competent_MatchesThePercentPicture()
        {
            // Детермінований режим (дефолт гри): один прогін на полосу.
            Assert.IsTrue(Fight(HitRuleKind.Threshold, 1, ReadinessBand.Fortified, FullSquadAllies, naive: false).Won,
                "Fortified + повний склад під порогом — перемога");
            Assert.IsTrue(Fight(HitRuleKind.Threshold, 1, ReadinessBand.Ready, FullSquadAllies, naive: false).Won,
                "Ready + повний склад під порогом — перемога");
            Assert.IsFalse(Fight(HitRuleKind.Threshold, 1, ReadinessBand.Unprepared, FullSquadAllies, naive: false).Won,
                "Unprepared під порогом — поразка (інакше підготовка нічого не вартує)");
        }

        [Test]
        public void SquadMatters_ProtagonistAlone_AlmostNeverWins_InAnyBand()
        {
            foreach (var band in Bands)
            {
                var runs = Percent(band, allies: 0, naive: false);
                double won = Rate(runs, r => r.Won);
                Assert.LessOrEqual(won, SoloWinRateMax,
                    $"{band}: протагоніст сам мав вигравати не більше {SoloWinRateMax:P0}, вийшло {won:P0} — інакше вибір складу нічого не значить");
                Assert.AreEqual(1, runs[0].Party);
            }
        }

        [Test]
        public void SquadMatters_EachExtraFighterHelps_InReadyBand()
        {
            double w1 = Rate(Percent(ReadinessBand.Ready, 1, naive: false), r => r.Won);
            double w2 = Rate(Percent(ReadinessBand.Ready, 2, naive: false), r => r.Won);
            double w3 = Rate(Percent(ReadinessBand.Ready, 3, naive: false), r => r.Won);
            Assert.Less(w1, w3, $"Ready: двоє ({w1:P0}) мали вигравати рідше за чотирьох ({w3:P0})");
            Assert.LessOrEqual(w1, w2, $"Ready: двоє ({w1:P0}) не краще за трьох ({w2:P0})");
            Assert.LessOrEqual(w2, w3, $"Ready: троє ({w2:P0}) не краще за чотирьох ({w3:P0})");
        }

        [Test]
        public void Naive_Play_IsWorseThanCompetent_AndShortOfAMarathon()
        {
            var weak = new List<Run>();
            weak.AddRange(Percent(ReadinessBand.Unprepared, FullSquadAllies, naive: true));
            weak.AddRange(Percent(ReadinessBand.Bracing, FullSquadAllies, naive: true));
            Assert.LessOrEqual(Rate(weak, r => r.Won), NaiveWeakBandsWinRateMax,
                "наївна гра в слабких полосах мала програвати майже завжди");
            Assert.LessOrEqual(weak.Max(r => r.Rounds), RoundsHardMax);

            var naiveStrong = new List<Run>();
            naiveStrong.AddRange(Percent(ReadinessBand.Ready, FullSquadAllies, naive: true));
            naiveStrong.AddRange(Percent(ReadinessBand.Fortified, FullSquadAllies, naive: true));
            var smartStrong = new List<Run>();
            smartStrong.AddRange(Percent(ReadinessBand.Ready, FullSquadAllies, naive: false));
            smartStrong.AddRange(Percent(ReadinessBand.Fortified, FullSquadAllies, naive: false));
            Assert.Less(Rate(naiveStrong, r => r.Won), Rate(smartStrong, r => r.Won),
                "наївна гра в Ready/Fortified мала вигравати рідше за грамотну (тактика важить)");
            Assert.LessOrEqual(naiveStrong.Max(r => r.Rounds), RoundsHardMax);
        }
    }
}
