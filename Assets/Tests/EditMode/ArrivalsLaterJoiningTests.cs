using System.Collections.Generic;
using System.Linq;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Combat;
using Game.Core.Dungeons;
using Game.Core.Expeditions;
using Game.Core.Loop;
using Game.Core.Scenes;
using Game.Core.Session;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Поправка №15.1 (пізніше приєднання, рішення власника 29.09.2026:
    /// «Він же може доєднатись пізніше з квесту, або якось же треба щоб
    /// напарники приходили» → «Таверна приводить», «Зустріч на вилазці»,
    /// «Рада: прийом переселенців»).
    ///
    /// Хто НЕ прибився на старті (<see cref="ArrivalsPool"/>, Поправка
    /// №12.10) лишається <see cref="CompanionStatus.NotArrived"/> і рано чи
    /// пізно приєднується одним із трьох детермінованих шляхів, кожен —
    /// окремим блоком тестів нижче; спільне — "один фахівець приходить лише
    /// раз" і збереження/відновлення розкладу Таверни.
    /// </summary>
    public class ArrivalsLaterJoiningTests
    {
        // ================= допоміжні: доганяємо відкриття до Morning =================

        private static NewGameOptions CreationOptions()
            => new NewGameOptions { SkipCreation = false, HitRule = HitRuleKind.Threshold };

        /// <summary>
        /// Сценарій A: передісторія "warrior" (дефолт → Гобан-Сайр) + Тугар
        /// "refuse" (→ Дід Овсій). Присутні: goban, keeper. Відсутні: healer,
        /// sindbad — саме в цьому порядку йдуть у пулі (ArrivalsPool.
        /// AllSpecialistIds), зручно для тестів Таверни/переселенців
        /// (порядок черги) і данжу "old_hermitage" (Гафія).
        /// </summary>
        private static GameSession NewScenarioA_AbsentHealerAndSindbad()
        {
            var s = new GameSession();
            s.NewGame(CreationOptions());
            s.SetProtagonistBackground("warrior");
            s.ConfirmCreation();
            RunSceneToFinish(s, tugarChoiceIndex: 0);
            Assert.AreEqual(SessionState.Morning, s.State);
            return s;
        }

        /// <summary>
        /// Сценарій B: передісторія "healer" (→ Гафія) + Тугар "bargain"
        /// (→ Синдбад). Присутні: healer, sindbad. Відсутні: keeper, goban —
        /// кожен на своїй точці вилазки ("outskirts"/"old_workshop").
        /// </summary>
        private static GameSession NewScenarioB_AbsentKeeperAndGoban()
        {
            var s = new GameSession();
            s.NewGame(CreationOptions());
            s.SetProtagonistBackground("healer");
            s.ConfirmCreation();
            RunSceneToFinish(s, tugarChoiceIndex: 1);
            Assert.AreEqual(SessionState.Morning, s.State);
            return s;
        }

        private static void RunSceneToFinish(GameSession s, int tugarChoiceIndex)
        {
            var step = s.AdvanceScene();
            while (!step.IsFinished)
            {
                if (!step.IsChoice) { step = s.AdvanceScene(); continue; }
                int idx = step.ChoiceId == OpeningScenes.TugarOfferChoiceId ? tugarChoiceIndex : 0;
                step = s.ChooseSceneOption(idx);
            }
        }

        /// <summary>Один повний тихий ігровий день — Morning/FreePlay → Day → (Evening/Night, якщо є) → назад до Morning/FreePlay. Акумулює DayLog у <paramref name="into"/>, якщо переданий.</summary>
        private static void PlayFullDayQuiet(GameSession s, List<GameEvent> into = null)
        {
            s.ConfirmMorning();
            var report = s.AdvanceDay();
            Collect(s, into);
            while (report != null && report.AwaitsDecision)
            {
                report = s.ResolveIncident(IncidentPath.Quiet);
                Collect(s, into);
            }
            if (s.State == SessionState.Scene) RunSceneToFinish(s, tugarChoiceIndex: 0);
            if (s.State == SessionState.Evening) s.ConfirmEvening();
            if (s.State == SessionState.Night)
            {
                var night = s.AdvanceNight();
                Collect(s, into);
                while (night != null && night.AwaitsDecision)
                {
                    night = s.ResolveIncident(IncidentPath.Quiet);
                    Collect(s, into);
                }
            }
            if (s.State == SessionState.Summary) s.AcknowledgeSummary();
        }

        private static void Collect(GameSession s, List<GameEvent> into)
        {
            if (into != null) into.AddRange(s.DayLog);
        }

        private static bool Saw(List<GameEvent> log, string key) => log.Any(e => e.Key == key);

        private static GameEvent Find(List<GameEvent> log, string key) => log.First(e => e.Key == key);

        private static bool IsArrived(GameSession s, string specialistId) =>
            s.GetRosterView().Companions.Any(c => c.Id == specialistId);

        // ================= (1) чиста функція: точка вилазки за фахівцем =================

        [Test]
        public void ExpeditionSiteOf_MapsAllFourSpecialists_ToDistinctPoints()
        {
            Assert.AreEqual("outskirts", ArrivalsPool.ExpeditionSiteOf(ArrivalsPool.KeeperId));
            Assert.AreEqual("old_workshop", ArrivalsPool.ExpeditionSiteOf(ArrivalsPool.GobanId));
            Assert.AreEqual("far_highway", ArrivalsPool.ExpeditionSiteOf(ArrivalsPool.SindbadId));
            Assert.AreEqual(DefaultDungeon.OldHermitage, ArrivalsPool.ExpeditionSiteOf(ArrivalsPool.HealerId));

            var sites = ArrivalsPool.AllSpecialistIds.Select(ArrivalsPool.ExpeditionSiteOf).ToList();
            Assert.AreEqual(4, sites.Distinct().Count(), "чотири фахівці — чотири РІЗНІ точки, жодного накладення");
        }

        [Test]
        public void ExpeditionSiteOf_UnknownId_ReturnsNull()
        {
            Assert.IsNull(ArrivalsPool.ExpeditionSiteOf("nobody"));
            Assert.IsNull(ArrivalsPool.ExpeditionSiteOf(null));
        }

        // ================= (2) шлях «Таверна»: день відомий заздалегідь =================

        [Test]
        public void Tavern_Built_AnnouncesNextAbsentSpecialist_AndBringsThemOnTheDueDay()
        {
            var s = NewScenarioA_AbsentHealerAndSindbad();
            Assert.AreEqual(BuildOrderResult.Started, s.OrderBuilding(DefaultBuildings.Tavern));

            var log = new List<GameEvent>();
            PlayFullDayQuiet(s, log); // доба 1: Таверна добудована (1 доба, тестова збірка) → оголошення
            Assert.IsTrue(Saw(log, "arrivals.tavern.announced"), "оголошення мусить прозвучати того самого дня, коли Таверна стала");
            var announce = Find(log, "arrivals.tavern.announced");
            Assert.AreEqual(ArrivalsPool.HealerId, announce.Args["companionId"], "наступний за порядком пула — Гафія (healer передує sindbad)");
            Assert.AreEqual("3", announce.Args["days"], "ПЛЕЙСХОЛДЕР N=3 (CityBalance.TavernSpecialistArrivalDays)");
            Assert.IsFalse(IsArrived(s, ArrivalsPool.HealerId), "не одразу — лише в оголошений день");

            PlayFullDayQuiet(s, log); // доба 2
            PlayFullDayQuiet(s, log); // доба 3
            Assert.IsFalse(IsArrived(s, ArrivalsPool.HealerId), "ще не той день");

            log.Clear();
            PlayFullDayQuiet(s, log); // доба 4 = 1 + 3 (ПЛЕЙСХОЛДЕР) — день приходу
            Assert.IsTrue(Saw(log, "arrivals.tavern"), "прихід звучить рівно в заявлений день");
            Assert.AreEqual(ArrivalsPool.HealerId, Find(log, "arrivals.tavern").Args["companionId"]);
            Assert.IsTrue(IsArrived(s, ArrivalsPool.HealerId), "Гафія тепер у ростері (вкладка «Люди»)");

            // Того самого дня — новий розклад на другого відсутнього (sindbad).
            Assert.IsTrue(Saw(log, "arrivals.tavern.announced"), "хтось іще відсутній (sindbad) — Таверна планує наступного одразу");
            Assert.AreEqual(ArrivalsPool.SindbadId, log.Last(e => e.Key == "arrivals.tavern.announced").Args["companionId"]);
        }

        // ================= (3) шлях «зустріч на вилазці» =================

        [Test]
        public void Expedition_Return_FromWaitingSpecialistsSite_BringsThemIn()
        {
            var s = NewScenarioB_AbsentKeeperAndGoban();
            var roster = s.GetRosterView();
            var partyIds = new List<string> { GameSession.ProtagonistId };

            var preview = s.PreviewExpedition(ArrivalsPool.ExpeditionSiteOf(ArrivalsPool.KeeperId), ExpeditionApproach.Quiet, partyIds);
            Assert.IsNotNull(preview);
            Assert.AreEqual(DispatchResult.Success,
                s.DepartExpedition(preview.SiteId, ExpeditionApproach.Quiet, partyIds, preview.Days));

            var log = new List<GameEvent>();
            bool returned = false;
            for (int day = 0; day < preview.Days + 2 && !returned; day++)
            {
                PlayFullDayQuiet(s, log);
                returned = Saw(log, "expedition.returned");
            }

            Assert.IsTrue(returned, "загін мусив повернутися за відведені дні");
            Assert.IsTrue(Saw(log, "arrivals.expedition"), "Дід Овсій чекав саме на «outskirts»");
            Assert.AreEqual(ArrivalsPool.KeeperId, Find(log, "arrivals.expedition").Args["companionId"]);
            Assert.IsTrue(IsArrived(s, ArrivalsPool.KeeperId));
            Assert.IsFalse(IsArrived(s, ArrivalsPool.GobanId), "Гобан-Сайр чекає на ІНШІЙ точці — цей похід його не привів");
        }

        [Test]
        public void Dungeon_Extract_FromHealersHermitage_BringsHealerIn()
        {
            var s = NewScenarioA_AbsentHealerAndSindbad();
            var partyIds = new List<string> { GameSession.ProtagonistId };
            string siteId = ArrivalsPool.ExpeditionSiteOf(ArrivalsPool.HealerId);
            Assert.AreEqual(DefaultDungeon.OldHermitage, siteId);

            Assert.AreEqual(DispatchResult.Success,
                s.DepartExpedition(siteId, ExpeditionApproach.Delve, partyIds, 2));
            Assert.AreEqual(SessionState.Dungeon, s.State);

            s.ExtractDungeon();
            Assert.AreEqual(SessionState.Morning, s.State);

            Assert.IsTrue(s.DayLog.Any(e => e.Key == "arrivals.expedition" && e.Args["companionId"] == ArrivalsPool.HealerId),
                "видобуток данжу «Старий скит» повертає загін звідти, звідки він вийшов — Гафія приєднується");
            Assert.IsTrue(IsArrived(s, ArrivalsPool.HealerId));
        }

        [Test]
        public void Dungeon_Abandon_StillCountsAsAReturn_BringsWaitingSpecialistIn()
        {
            var s = NewScenarioA_AbsentHealerAndSindbad();
            var partyIds = new List<string> { GameSession.ProtagonistId };
            Assert.AreEqual(DispatchResult.Success,
                s.DepartExpedition(DefaultDungeon.OldHermitage, ExpeditionApproach.Delve, partyIds, 2));

            s.AbandonDungeon();

            Assert.IsTrue(s.DayLog.Any(e => e.Key == "arrivals.expedition" && e.Args["companionId"] == ArrivalsPool.HealerId),
                "«вилазка відбулась» навіть коли загін пішов, не довівши данж до кінця");
            Assert.IsTrue(IsArrived(s, ArrivalsPool.HealerId));
        }

        // ================= (4) прев'ю вилазки називає, хто там чекає =================

        [Test]
        public void PreviewExpedition_NamesWaitingSpecialist_AndClearsAfterArrival()
        {
            var s = NewScenarioB_AbsentKeeperAndGoban();
            var partyIds = new List<string> { GameSession.ProtagonistId };

            var before = s.PreviewExpedition("outskirts", ExpeditionApproach.Quiet, partyIds);
            Assert.AreEqual(ArrivalsPool.KeeperId, before.WaitingSpecialistId);

            var elsewhere = s.PreviewExpedition("far_highway", ExpeditionApproach.Quiet, partyIds);
            Assert.IsNull(elsewhere.WaitingSpecialistId, "на far_highway чекає Синдбад, який уже присутній — нема кого називати");

            Assert.AreEqual(DispatchResult.Success,
                s.DepartExpedition("outskirts", ExpeditionApproach.Quiet, partyIds, before.Days));
            var log = new List<GameEvent>();
            for (int day = 0; day < before.Days + 2; day++)
            {
                PlayFullDayQuiet(s, log);
                if (Saw(log, "expedition.returned")) break;
            }
            Assert.IsTrue(IsArrived(s, ArrivalsPool.KeeperId));

            var after = s.PreviewExpedition("outskirts", ExpeditionApproach.Quiet, partyIds);
            Assert.IsNull(after.WaitingSpecialistId, "Дід Овсій уже прибув — прев'ю більше нікого там не називає");
        }

        // ================= (5) шлях «рада: прийом переселенців» =================

        [Test]
        public void Settlers_ArrivingWithSettlers_BringsNextAbsentSpecialist()
        {
            var s = NewScenarioA_AbsentHealerAndSindbad();

            // Стартового гаманця (їжа 20, ПЛЕЙСХОЛДЕР) не вистачає на ціну
            // прийому (30) — фермуємо кілька тихих діб, доки OrderSettlers
            // не перестане відмовляти нестачею їжі (сам показник — CityWorks.
            // SettlersReady/OrderSettlers — числа виробітку не наш предмет).
            s.Assign(ArrivalsPool.GobanId, "settlement_farms");
            CouncilOrderResult order = CouncilOrderResult.NotEnoughFood;
            var log = new List<GameEvent>();
            for (int day = 0; day < 12 && order != CouncilOrderResult.Queued; day++)
            {
                order = s.OrderSettlers();
                if (order == CouncilOrderResult.Queued) break;
                PlayFullDayQuiet(s, log);
                log.Clear();
            }
            Assert.AreEqual(CouncilOrderResult.Queued, order, "тест сам зламався: переселенців так і не вдалось замовити за 12 діб ферми");

            PlayFullDayQuiet(s, log); // доба, коли CityWorksStep справді приводить переселенців
            Assert.IsTrue(Saw(log, "city.people.arrived"), "тест сам зламався: переселенці не прийшли того самого дня");
            Assert.IsTrue(Saw(log, "arrivals.settlers"), "разом з переселенцями приходить наступний відсутній фахівець");
            Assert.AreEqual(ArrivalsPool.HealerId, Find(log, "arrivals.settlers").Args["companionId"],
                "наступний за порядком пула — Гафія (healer передує sindbad)");
            Assert.IsTrue(IsArrived(s, ArrivalsPool.HealerId));
        }

        // ================= (6) один фахівець — один раз =================

        [Test]
        public void Specialist_ScheduledByTavern_ButArrivesEarlierByExpedition_DoesNotArriveTwice()
        {
            var s = NewScenarioA_AbsentHealerAndSindbad();
            Assert.AreEqual(BuildOrderResult.Started, s.OrderBuilding(DefaultBuildings.Tavern));

            var log = new List<GameEvent>();
            PlayFullDayQuiet(s, log); // доба 1: Таверна стала, Гафію заплановано на добу 4
            var announce = Find(log, "arrivals.tavern.announced");
            Assert.AreEqual(ArrivalsPool.HealerId, announce.Args["companionId"]);

            // Гафію ловить вилазка РАНІШЕ запланованого дня — данж "Старий скит".
            var partyIds = new List<string> { GameSession.ProtagonistId };
            Assert.AreEqual(DispatchResult.Success,
                s.DepartExpedition(DefaultDungeon.OldHermitage, ExpeditionApproach.Delve, partyIds, 2));
            log.Clear();
            s.ExtractDungeon();
            Assert.IsTrue(s.DayLog.Any(e => e.Key == "arrivals.expedition" && e.Args["companionId"] == ArrivalsPool.HealerId));
            Assert.IsTrue(IsArrived(s, ArrivalsPool.HealerId));

            // Доба 4 (запланована Таверною) настає — жодного дубля приходу.
            log.Clear();
            PlayFullDayQuiet(s, log); // доба 2
            PlayFullDayQuiet(s, log); // доба 3
            log.Clear();
            PlayFullDayQuiet(s, log); // доба 4
            Assert.IsFalse(log.Any(e => e.Key == "arrivals.tavern" && e.Args["companionId"] == ArrivalsPool.HealerId),
                "Гафія вже прибула вилазкою раніше — Таверна не приводить її вдруге");

            // Ще хтось відсутній (sindbad) — Таверна самозцілюється й планує його того самого дня.
            Assert.IsTrue(Saw(log, "arrivals.tavern.announced"));
            Assert.AreEqual(ArrivalsPool.SindbadId, Find(log, "arrivals.tavern.announced").Args["companionId"]);
        }

        // ================= (7) сейв: розклад Таверни переживає збереження =================

        [Test]
        public void TavernSchedule_SurvivesSave_IntoAFreshSession()
        {
            var s = NewScenarioA_AbsentHealerAndSindbad();
            Assert.AreEqual(BuildOrderResult.Started, s.OrderBuilding(DefaultBuildings.Tavern));
            PlayFullDayQuiet(s); // доба 1: заплановано на добу 4

            string blob = s.SaveState(0);

            var fresh = new GameSession();
            fresh.NewGame(new NewGameOptions { SkipCreation = true, HitRule = HitRuleKind.Threshold });
            fresh.RestoreFromBlob(blob);

            var log = new List<GameEvent>();
            void Play() { fresh.ConfirmMorning(); var r = fresh.AdvanceDay(); Collect(fresh, log);
                while (r != null && r.AwaitsDecision) { r = fresh.ResolveIncident(IncidentPath.Quiet); Collect(fresh, log); }
                if (fresh.State == SessionState.Evening) fresh.ConfirmEvening();
                if (fresh.State == SessionState.Night) { var n = fresh.AdvanceNight(); Collect(fresh, log);
                    while (n != null && n.AwaitsDecision) { n = fresh.ResolveIncident(IncidentPath.Quiet); Collect(fresh, log); } }
                if (fresh.State == SessionState.Summary) fresh.AcknowledgeSummary();
            }

            Play(); // доба 2
            Play(); // доба 3
            Assert.IsFalse(IsArrived(fresh, ArrivalsPool.HealerId));
            log.Clear();
            Play(); // доба 4 — розклад, привезений із зліпка, мусить спрацювати
            Assert.IsTrue(Saw(log, "arrivals.tavern"), "розклад Таверни (id+доба) пережив збереження й відновлення у СВІЖУ сесію");
            Assert.AreEqual(ArrivalsPool.HealerId, Find(log, "arrivals.tavern").Args["companionId"]);
            Assert.IsTrue(IsArrived(fresh, ArrivalsPool.HealerId));
        }

        // ================= (8) сейв: старий зліпок без "tavernNext=" читається =================

        [Test]
        public void OldFormatBlob_WithoutTavernNextField_RestoresAsNoScheduleYet_AndSelfHeals()
        {
            var s = NewScenarioA_AbsentHealerAndSindbad();
            Assert.AreEqual(BuildOrderResult.Started, s.OrderBuilding(DefaultBuildings.Tavern));
            PlayFullDayQuiet(s); // доба 1: заплановано на добу 4

            string newBlob = s.SaveState(0);
            string oldBlob = RemoveField(newBlob, ";tavernNext=");
            StringAssert.DoesNotContain(";tavernNext=", oldBlob, "тест сам зламався: поле мало зникнути");

            var fresh = new GameSession();
            fresh.NewGame(new NewGameOptions { SkipCreation = true, HitRule = HitRuleKind.Threshold });
            fresh.RestoreFromBlob(oldBlob); // не мусить кинути виняток

            var log = new List<GameEvent>();
            fresh.ConfirmMorning();
            var report = fresh.AdvanceDay();
            log.AddRange(fresh.DayLog);
            while (report != null && report.AwaitsDecision) { report = fresh.ResolveIncident(IncidentPath.Quiet); log.AddRange(fresh.DayLog); }

            // Без успадкованого розкладу код сам планує наступного відсутнього
            // наново — Таверна вже стоїть (частина "b:" зліпка), тож
            // "arrivals.tavern.announced" з'являється вже в перший день
            // ФРЕШ-сесії, з добою відліку від ПОТОЧНОЇ (не старої) доби.
            Assert.IsTrue(Saw(log, "arrivals.tavern.announced"),
                "без успадкованого розкладу код перепланує сам, щойно побачить Таверну і відсутнього");
            Assert.AreEqual(ArrivalsPool.HealerId, Find(log, "arrivals.tavern.announced").Args["companionId"]);
        }

        /// <summary>Прибирає поле "<paramref name="key"/>...;" із заголовка зліпка (до ";core=") — той самий прийом, що ArrivalsPoolTests.ToOldFormatBlob для "arrivals=1".</summary>
        private static string RemoveField(string blob, string key)
        {
            int idx = blob.IndexOf(key, System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(idx, 0, "тест сам зламався: поля нема в зліпку взагалі");
            int end = blob.IndexOf(';', idx + 1);
            Assert.Greater(end, idx, "тест сам зламався: не знайшов кінця поля");
            return blob.Substring(0, idx) + blob.Substring(end);
        }
    }
}
