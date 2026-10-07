using System.Collections.Generic;
using System.Linq;
using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Combat;
using Game.Core.Dungeons;
using Game.Core.Expeditions;
using Game.Core.Loop;
using Game.Core.Scenes;
using Game.Core.Session;
using Game.Core.Session.Bots;
using Game.Core.Session.Views;
using Game.Core.World;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// M1.6 — Поправка №8.3 («Гравець призначає заступника вручну при зборах»;
    /// «Криза може обрати жертву і серед відсутніх»; вилазка і данж тривають
    /// добами): (а) криза дістає й відсутніх; (б) данж займає ≥ 1 доби, здобич
    /// лягає на поверненні; (в) заступник на пост того, хто йде, — рішення
    /// гравця при зборах, без автопризначення; усе переживає сейв у СВІЖУ сесію.
    /// </summary>
    public class ExpeditionAbsenceTests
    {
        // ---------------- сесія і доба ----------------

        private static NewGameOptions Options() =>
            new NewGameOptions { SkipCreation = true, HitRule = HitRuleKind.Threshold, TestBuildOneDayConstruction = true };

        /// <summary>Нова гра і пролог (усюди перший варіант) — ранок доби 1. Старт: Захар на council_seat; keeper, goban, maksym, myroslava, protagonist — вільні.</summary>
        private static GameSession Morning()
        {
            var s = new GameSession();
            s.NewGame(Options());
            RunScene(s);
            Assert.AreEqual(SessionState.Morning, s.State, "пролог мав привести до ранку");
            return s;
        }

        private static void RunScene(GameSession s)
        {
            var step = s.AdvanceScene();
            while (!step.IsFinished) step = step.IsChoice ? s.ChooseSceneOption(0) : s.AdvanceScene();
        }

        private static void Collect(GameSession s, List<GameEvent> into) => into?.AddRange(s.DayLog);

        /// <summary>Одна повна тиха доба (до дня 5 включно без фіналу).</summary>
        private static void PlayDay(GameSession s, List<GameEvent> into = null)
        {
            s.ConfirmMorning();
            var report = s.AdvanceDay();
            Collect(s, into);
            while (report != null && report.AwaitsDecision) { report = s.ResolveIncident(IncidentPath.Quiet); Collect(s, into); }
            if (s.State == SessionState.Scene) RunScene(s);
            if (s.State == SessionState.Evening) s.ConfirmEvening();
            if (s.State == SessionState.Night)
            {
                var night = s.AdvanceNight();
                Collect(s, into);
                while (night != null && night.AwaitsDecision) { night = s.ResolveIncident(IncidentPath.Quiet); Collect(s, into); }
            }
            if (s.State == SessionState.Summary) s.AcknowledgeSummary();
        }

        /// <summary>Грає доби, доки не прозвучить подія <paramref name="key"/>; повертає, скільки діб знадобилось (-1, якщо не дочекались).</summary>
        private static int PlayUntil(GameSession s, string key, int maxDays, List<GameEvent> log)
        {
            for (int day = 1; day <= maxDays; day++)
            {
                PlayDay(s, log);
                if (log.Any(e => e.Key == key)) return day;
            }
            return -1;
        }

        private static CompanionSummary Of(GameSession s, string id) =>
            s.GetRosterView().Companions.Single(c => c.Id == id);

        private static IReadOnlyDictionary<string, string> Deputies(params string[] slotAndWho)
        {
            var d = new Dictionary<string, string>();
            for (int i = 0; i + 1 < slotAndWho.Length; i += 2) d[slotAndWho[i]] = slotAndWho[i + 1];
            return d;
        }

        // =====================================================================
        // (а) Криза дістає і відсутніх
        // =====================================================================

        private static Companion Person(string id)
        {
            var arch = new CompanionArchetype(id, id);
            return arch.CreateInstance(id);
        }

        [Test]
        public void KillableActors_IncludeThoseAwayOnMission()
        {
            var roster = new Roster();
            roster.Add(Person("hero")); roster.Add(Person("alpha")); roster.Add(Person("beta"));
            roster.Get("alpha").Status = CompanionStatus.OnMission;
            var adapter = new RosterAdapter(roster, "hero");

            CollectionAssert.AreEqual(new[] { "alpha", "beta" }, adapter.KillableActorIds,
                "відсутність — не сховище від кризи (Поправка №8.3): партія в полі втрачає імунітет");
        }

        [Test]
        public void NaturalCrisis_TakesTheAbsentVictim_FirstById()
        {
            var cfg = new BalanceConfig();
            var crisis = DefaultIncidents.All().First(i => i.IsCrisis);
            var roster = new Roster();
            roster.Add(Person("hero")); roster.Add(Person("alpha")); roster.Add(Person("mid")); roster.Add(Person("zeta"));
            roster.Get("alpha").Status = CompanionStatus.OnMission; // перший за id — у вилазці
            var adapter = new RosterAdapter(roster, "hero");

            var outcome = IncidentResolver.Resolve(crisis, adapter, null, adapter,
                new Game.Core.Settlement.PopulationState(), new Game.Core.Pressure.TensionState(cfg.Tension, 900), 1, cfg);

            Assert.AreEqual("alpha", outcome.AffectedActorId, "жертву обрано з відсутніх: id перший, ним і був");
        }

        [Test]
        public void ForcedCrisis_WoundsTheAbsent_AndHeStaysWoundedOnReturn()
        {
            var s = Morning();
            var others = s.GetRosterView().Companions.Where(c => c.Id != GameSession.ProtagonistId).Select(c => c.Id).OrderBy(i => i, System.StringComparer.Ordinal).ToList();
            string target = others[0]; // укус тест-кризи б'є першого за id з тих, кого можна зачепити
            Assume.That(Of(s, target).Status, Is.EqualTo(CompanionStatus.Idle).Or.EqualTo(CompanionStatus.Assigned));

            var party = new List<string> { target };
            Assert.AreEqual(DispatchResult.Success,
                s.DepartExpedition("far_highway", ExpeditionApproach.Quiet, party, 8, BotSupport.ChooseDeputies(s.GetMusterView(party))));

            for (int day = 1; day <= 4; day++) PlayDay(s);
            s.ConfirmMorning();
            var report = s.AdvanceDay();
            while (report != null && report.AwaitsDecision) report = s.ResolveIncident(IncidentPath.Quiet);
            if (s.State == SessionState.Scene) RunScene(s);
            s.ConfirmEvening();
            Assert.AreEqual(CompanionStatus.OnMission, Of(s, target).Status, "загін у полі в ніч кризи");
            s.ResolveFinale(IncidentPath.Quiet);
            Assert.AreEqual(0.0, s.DebugInjuryPoints(target), 0.0001, "до укусу він здоровий");

            s.AdvanceNight();

            Assert.IsTrue(s.DayLog.Any(e => e.Key == "crisis.test.unmitigated"), "ніхто не реагував — укус на повну");
            Assert.Greater(s.DebugInjuryPoints(target), 0.0, "криза дісталась відсутнього, який у полі");
            Assert.AreEqual(CompanionStatus.OnMission, Of(s, target).Status, "рана не повертає його з вилазки додому");

            if (s.State == SessionState.Summary) s.AcknowledgeSummary();
            var log = new List<GameEvent>();
            Assert.GreaterOrEqual(PlayUntil(s, "expedition.returned", 12, log), 1, "загін мав повернутись");
            Assert.AreEqual(CompanionStatus.Injured, Of(s, target).Status, "поранений у полі — повертається пораненим, а не видужує дорогою");
        }

        // =====================================================================
        // (б) Вилазка і данж тривають добами
        // =====================================================================

        [Test]
        public void EveryExpeditionSite_TakesAtLeastADay_ForBothApproaches()
        {
            foreach (var site in DefaultSites.All())
            {
                Assert.GreaterOrEqual(site.DaysFor(ExpeditionApproach.Quiet), 1, site.Id);
                Assert.GreaterOrEqual(site.DaysFor(ExpeditionApproach.Forceful), 1, site.Id);
            }
            Assert.GreaterOrEqual(DefaultDungeon.Days, 1, "данж займає ≥ 1 доби (Поправка №8.3)");
        }

        /// <summary>Табір, тихо, глибше до схованки: незабановано сировини 3 (охоронець маршруту — перевірка нижче).</summary>
        private static void DelveCampForCraft3(GameSession s, List<string> party)
        {
            Assert.AreEqual(DispatchResult.Success,
                s.DepartExpedition(DefaultDungeon.AbandonedCamp, ExpeditionApproach.Delve, party, 1,
                    BotSupport.ChooseDeputies(s.GetMusterView(party))));
            Assert.AreEqual(SessionState.Dungeon, s.State);
            s.ResolveDungeonRoom(IncidentPath.Quiet);
            s.PushDeeper();
            s.ResolveDungeonRoom(IncidentPath.Quiet);
            Assert.AreEqual(3, s.GetDungeonView().UnbankedCraftComponent, "тест сам зламався: маршрут схованки змінився");
        }

        private static readonly string[] Trio = { GameSession.ProtagonistId, "maksym", "myroslava" };

        [Test]
        public void Dungeon_KeepsThePartyAwayForDays_AndBanksTheLootOnReturn()
        {
            var s = Morning();
            var party = new List<string>(Trio);
            int craftBefore = s.GetEconomyView().CraftComponent;

            DelveCampForCraft3(s, party);
            s.ExtractDungeon();

            Assert.AreEqual(SessionState.Morning, s.State);
            foreach (var id in party)
                Assert.AreEqual(CompanionStatus.OnMission, Of(s, id).Status, id + ": данж не закінчується в ту саму мить — загін іде додому добами");
            Assert.AreEqual(craftBefore, s.GetEconomyView().CraftComponent, "здобич ще в дорозі, у гаманець не лягла");
            Assert.AreEqual(DispatchResult.PartyAlreadyAway,
                s.DepartExpedition("outskirts", ExpeditionApproach.Quiet, new List<string> { "keeper" }, 4),
                "поки загін не повернувся, другого відряду немає");

            var log = new List<GameEvent>();
            int days = PlayUntil(s, "dungeon.returned", 6, log);
            Assert.GreaterOrEqual(days, 1, "данж займає щонайменше добу");
            Assert.AreEqual(DefaultDungeon.Days, days, "вдома рівно через DefaultDungeon.Days діб");
            foreach (var id in party)
                Assert.AreNotEqual(CompanionStatus.OnMission, Of(s, id).Status, id + ": загін вдома");
            Assert.AreEqual(craftBefore + 3, s.GetEconomyView().CraftComponent, "здобич лягла на поверненні");
        }

        [Test]
        public void Dungeon_Abandon_AlsoTakesDays_AndBanksNothing()
        {
            var s = Morning();
            var party = new List<string>(Trio);
            var before = s.GetEconomyView();
            DelveCampForCraft3(s, party);
            s.AbandonDungeon();

            Assert.AreEqual(CompanionStatus.OnMission, Of(s, GameSession.ProtagonistId).Status, "обережний вихід теж іде додому добами");
            var log = new List<GameEvent>();
            Assert.AreEqual(DefaultDungeon.Days, PlayUntil(s, "dungeon.returned", 6, log));
            Assert.AreEqual(before.CraftComponent, s.GetEconomyView().CraftComponent, "незабановане при виході пропадає");
        }

        [Test]
        public void DelvePreview_NamesTheSameDays_AsTheDungeonTakes()
        {
            var s = Morning();
            var preview = s.PreviewExpedition(DefaultDungeon.AbandonedCamp, ExpeditionApproach.Delve, new List<string>(Trio));
            Assert.AreEqual(DefaultDungeon.Days, preview.Days, "прогноз у зборах каже правду про тривалість");
        }

        // =====================================================================
        // (в) Заступник призначається вручну при зборах
        // =====================================================================

        [Test]
        public void Muster_WithoutADecision_RefusesWithAReason_AndChangesNothing()
        {
            var s = Morning();
            var party = new List<string> { "zakhar" };
            var muster = s.GetMusterView(party);
            Assert.IsTrue(muster.NeedsChoice, "пост звільняється, є кому заступити — рішення потрібне");
            var vacancy = muster.Vacancies.Single();
            Assert.AreEqual("council_seat", vacancy.SlotId);
            Assert.AreEqual("zakhar", vacancy.HolderId);
            CollectionAssert.Contains(muster.FreeIds, GameSession.ProtagonistId);
            CollectionAssert.DoesNotContain(muster.FreeIds, "zakhar");

            var r = s.DepartExpedition("outskirts", ExpeditionApproach.Quiet, party, 4);

            Assert.AreEqual(DispatchResult.SubstituteNotChosen, r, "відмова з причиною, а не мовчки і не автопризначення");
            Assert.AreEqual(CompanionStatus.Assigned, Of(s, "zakhar").Status, "нічого не змінилося: Захар на посту");
            Assert.AreEqual("council_seat", Of(s, "zakhar").AssignedSlotId);
            Assert.IsFalse(s.DayLog.Any(e => e.Key == "expedition.departed"));
            Assert.IsFalse(Trio.Concat(new[] { "keeper", "goban" }).Any(id => Of(s, id).AssignedSlotId == "council_seat"),
                "ніхто сам не зайняв порожній пост");
        }

        [Test]
        public void Muster_ChosenDeputy_StandsOnTheVacatedPost()
        {
            var s = Morning();
            var party = new List<string> { "zakhar" };

            var r = s.DepartExpedition("outskirts", ExpeditionApproach.Quiet, party, 4, Deputies("council_seat", "keeper"));

            Assert.AreEqual(DispatchResult.Success, r);
            Assert.AreEqual(CompanionStatus.OnMission, Of(s, "zakhar").Status);
            Assert.AreEqual("council_seat", Of(s, "keeper").AssignedSlotId, "обраний заступник стоїть на посту того, хто пішов");
            var evt = s.DayLog.Single(e => e.Key == "expedition.deputy_assigned");
            Assert.AreEqual("keeper", evt.Args["deputyId"]);
            Assert.AreEqual("zakhar", evt.Args["holderId"]);
        }

        [Test]
        public void Muster_ExplicitlyLeavingThePostEmpty_IsAChoice_AndIsSignalled()
        {
            var s = Morning();
            var party = new List<string> { "zakhar" };

            var r = s.DepartExpedition("outskirts", ExpeditionApproach.Quiet, party, 4, Deputies("council_seat", ""));

            Assert.AreEqual(DispatchResult.Success, r);
            Assert.IsFalse(s.GetRosterView().Companions.Any(c => c.AssignedSlotId == "council_seat"), "пост лишився порожнім — за рішенням гравця");
            Assert.IsTrue(s.DayLog.Any(e => e.Key == "expedition.post_left_empty" && e.Args["slotId"] == "council_seat"),
                "порожній пост — спостережуваний сигнал, не німа зміна");
        }

        [Test]
        public void Muster_InvalidDeputies_AreRejected_BeforeAnyChange()
        {
            var s = Morning();
            Assert.AreEqual(AssignmentResult.Success, s.Assign("keeper", "scouting_post"));
            var party = new List<string> { "zakhar" };
            var attempts = new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                ["у загоні"] = Deputies("council_seat", "zakhar"),
                ["вже на посту"] = Deputies("council_seat", "keeper"),
                ["невідома людина"] = Deputies("council_seat", "nobody_here"),
                ["пост не з цього загону"] = Deputies("council_seat", "goban", "settlement_farms", "maksym"),
            };
            foreach (var kv in attempts)
            {
                Assert.AreEqual(DispatchResult.SubstituteInvalid,
                    s.DepartExpedition("outskirts", ExpeditionApproach.Quiet, party, 4, kv.Value), kv.Key);
                Assert.AreEqual(CompanionStatus.Assigned, Of(s, "zakhar").Status, kv.Key + ": відмова нічого не змінила");
            }

            // Один заступник на два пости — теж відмова.
            Assert.AreEqual(AssignmentResult.Success, s.Assign("goban", "settlement_farms"));
            var two = new List<string> { "zakhar", "goban" };
            Assert.AreEqual(DispatchResult.SubstituteInvalid,
                s.DepartExpedition("outskirts", ExpeditionApproach.Quiet, two, 4,
                    Deputies("council_seat", "maksym", "settlement_farms", "maksym")));
            Assert.AreEqual(CompanionStatus.Assigned, Of(s, "goban").Status);
        }

        [Test]
        public void Muster_NobodyFreeToReplace_NoDecisionRequired()
        {
            var s = Morning();
            Assert.AreEqual(AssignmentResult.Success, s.Assign("goban", "settlement_farms"));
            Assert.AreEqual(AssignmentResult.Success, s.Assign("keeper", "scouting_post"));
            Assert.AreEqual(AssignmentResult.Success, s.Assign("maksym", "lab_station"));
            var party = new List<string> { "zakhar", "myroslava", GameSession.ProtagonistId };

            var muster = s.GetMusterView(party);
            Assert.IsFalse(muster.NeedsChoice, "вільних нема — вибирати не з кого");
            Assert.AreEqual(DispatchResult.Success, s.DepartExpedition("outskirts", ExpeditionApproach.Quiet, party, 4),
                "без заступника відряд іде лише тоді, коли заступити справді нікому");
        }

        [Test]
        public void Muster_TwoVacanciesOneFree_OnlyOnePostNeedsAChoice()
        {
            var s = Morning();
            Assert.AreEqual(AssignmentResult.Success, s.Assign("goban", "settlement_farms"));
            Assert.AreEqual(AssignmentResult.Success, s.Assign("keeper", "scouting_post"));
            Assert.AreEqual(AssignmentResult.Success, s.Assign("maksym", "lab_station"));
            var party = new List<string> { "zakhar", "goban", "myroslava" };
            Assert.AreEqual(2, s.GetMusterView(party).Vacancies.Count);
            CollectionAssert.AreEqual(new[] { GameSession.ProtagonistId }, s.GetMusterView(party).FreeIds);

            Assert.AreEqual(DispatchResult.SubstituteNotChosen, s.DepartExpedition("outskirts", ExpeditionApproach.Quiet, party, 4));
            Assert.AreEqual(DispatchResult.Success,
                s.DepartExpedition("outskirts", ExpeditionApproach.Quiet, party, 4, Deputies("council_seat", GameSession.ProtagonistId)),
                "один вільний — рішення потрібне лише на один пост, другий лишається порожнім без вибору");
        }

        [Test]
        public void Muster_NotLegalParty_FailsWithItsOwnReason_BeforeSubstitutes()
        {
            var s = Morning();
            Assert.AreEqual(DispatchResult.EmptyParty, s.DepartExpedition("outskirts", ExpeditionApproach.Quiet, new List<string>(), 4));
            Assert.AreEqual(DispatchResult.UnknownCompanion,
                s.DepartExpedition("outskirts", ExpeditionApproach.Quiet, new List<string> { "zakhar", "no_one" }, 4));
        }

        [Test]
        public void BotSupport_ChooseDeputies_PicksDistinctFreePeople_OrLeavesThePostEmptyExplicitly()
        {
            var two = new MusterView
            {
                Vacancies = new List<MusterVacancyView>
                {
                    new MusterVacancyView { SlotId = "a", HolderId = "x", CandidateIds = new List<string> { "p", "q" } },
                    new MusterVacancyView { SlotId = "b", HolderId = "y", CandidateIds = new List<string> { "p", "q" } },
                    new MusterVacancyView { SlotId = "c", HolderId = "z", CandidateIds = new List<string> { "p", "q" } }
                },
                FreeIds = new List<string> { "p", "q" }
            };
            var chosen = BotSupport.ChooseDeputies(two);
            Assert.AreEqual("p", chosen["a"]);
            Assert.AreEqual("q", chosen["b"]);
            Assert.AreEqual(string.Empty, chosen["c"], "кандидати скінчились — пост лишено порожнім явним рішенням");
        }

        // =====================================================================
        // Сейв посеред багатоденної вилазки відновлюється у СВІЖУ сесію
        // =====================================================================

        private static GameSession Fresh(string blob)
        {
            var fresh = new GameSession();
            fresh.NewGame(Options());
            fresh.RestoreFromBlob(blob);
            return fresh;
        }

        [Test]
        public void SaveMidExpedition_RestoresIntoAFreshSession_WithDeputyAndReturn()
        {
            var live = Morning();
            var party = new List<string> { "zakhar", GameSession.ProtagonistId };
            Assert.AreEqual(DispatchResult.Success,
                live.DepartExpedition("outskirts", ExpeditionApproach.Quiet, party, 4, Deputies("council_seat", "keeper")));
            PlayDay(live);
            string blob = live.SaveState(0);
            var fresh = Fresh(blob);

            Assert.AreEqual(CompanionStatus.OnMission, Of(fresh, "zakhar").Status, "відсутні лишились відсутніми");
            Assert.AreEqual(CompanionStatus.OnMission, Of(fresh, GameSession.ProtagonistId).Status);
            Assert.AreEqual("council_seat", Of(fresh, "keeper").AssignedSlotId, "заступник на посту пережив збереження");
            Assert.AreEqual(DispatchResult.PartyAlreadyAway,
                fresh.DepartExpedition("old_workshop", ExpeditionApproach.Quiet, new List<string> { "goban" }, 4),
                "відряд ще в дорозі і в свіжій сесії");

            var logLive = new List<GameEvent>();
            var logFresh = new List<GameEvent>();
            int daysLive = PlayUntil(live, "expedition.returned", 8, logLive);
            int daysFresh = PlayUntil(fresh, "expedition.returned", 8, logFresh);
            Assert.GreaterOrEqual(daysLive, 1);
            Assert.AreEqual(daysLive, daysFresh, "повернення на ту саму добу, що й без збереження");
            Assert.AreEqual(live.GetEconomyView().Gold, fresh.GetEconomyView().Gold, "здобич та сама");
            Assert.AreEqual(CompanionStatus.Idle, Of(fresh, GameSession.ProtagonistId).Status);
        }

        [Test]
        public void SaveWhileDungeonLootIsOnTheRoad_RestoresIntoAFreshSession_AndBanksOnReturn()
        {
            var live = Morning();
            var party = new List<string>(Trio);
            int craftBefore = live.GetEconomyView().CraftComponent;
            DelveCampForCraft3(live, party);
            live.ExtractDungeon();
            string blob = live.SaveState(0);
            var fresh = Fresh(blob);

            Assert.AreEqual(CompanionStatus.OnMission, Of(fresh, "maksym").Status);
            Assert.AreEqual(craftBefore, fresh.GetEconomyView().CraftComponent, "у свіжій сесії здобич теж ще в дорозі");

            var log = new List<GameEvent>();
            Assert.AreEqual(DefaultDungeon.Days, PlayUntil(fresh, "dungeon.returned", 6, log));
            Assert.AreEqual(craftBefore + 3, fresh.GetEconomyView().CraftComponent,
                "заморожена здобич данжу пережила збереження і лягла на поверненні");
        }
    }
}
