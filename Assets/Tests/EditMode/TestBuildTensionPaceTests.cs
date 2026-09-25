using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Session;
using Game.Core.Session.Bots;
using Game.Core.World;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Замір темпу Напруги в тестовій збірці (Поправка №7, рішення власника
    /// 24.09.2026: «За 5 днів пожежа, потім інші механіки. Так для тесту
    /// ввімкни що за 15 днів перша смуга за 20 друга і за 25 криза?»).
    ///
    /// Так само, як <see cref="CampaignPacingTests"/> — це тести ТЕМПА, не
    /// арифметики: замір "коли гравець уперше побачить зміну смуги/кризу", а
    /// не перевірка конкретної формули. Три боти зі своїм "норовом":
    /// "безрукий еталон" (ніколи не замовляє Облаву, не будує Храм/Укріплення,
    /// тихий шлях), "дефузер" (той самий рутинний бот, що й
    /// AllMechanicsCoverageTests/tools/Alpha.Sim — Облава + Храм/Укріплення
    /// увімкнені), "шкідник" (кроваво, порожні пости, без патруля, без Облави
    /// й Храму) — детермінізм (інваріант 1) означає, що те саме дерево рішень
    /// завжди дає той самий прогін.
    /// </summary>
    public class TestBuildTensionPaceTests
    {
        private const int Days = 30;

        private static (List<GameEvent> log, GameSession session) Run(IBotPolicy policy, bool suppressCouncilRoutine, int days = Days)
        {
            var options = new NewGameOptions();
            var session = new GameSession(options.Roller);
            session.NewGame(options);
            var log = new List<GameEvent>();
            BotRunner.Drive(session, policy, days, fullLog: log, suppressCouncilRoutine: suppressCouncilRoutine);
            return (log, session);
        }

        private static int? FirstDay(List<GameEvent> log, string key)
        {
            foreach (var e in log)
                if (e.Key == key) return e.Day;
            return null;
        }

        private static int? FirstDayCrisisResolved(List<GameEvent> log)
        {
            foreach (var e in log)
                if (e.Key == "decision.resolved" && e.Args.TryGetValue("incidentId", out var id) && id == "crisis_riot")
                    return e.Day;
            return null;
        }

        private static int? FirstDayForewarn(List<GameEvent> log, int level, string subject)
        {
            foreach (var e in log)
                if (e.Key == "forewarn.level" + level && e.Args.TryGetValue("subject", out var s) && s == subject)
                    return e.Day;
            return null;
        }

        // ================= (a) "Безрукий еталон" =================

        [Test]
        public void Reference_HitsMurmurFermentHeat_InTargetWindows()
        {
            var (log, _) = Run(new PacifistPolicy(), suppressCouncilRoutine: true);

            int? murmur = FirstDay(log, "tension.band.Murmur");
            int? ferment = FirstDay(log, "tension.band.Ferment");
            int? heat = FirstDay(log, "tension.band.Heat");

            Assert.NotNull(murmur, "еталон повинен побачити Ропіт за 30 діб");
            Assert.That(murmur.Value, Is.InRange(14, 16), "Ропіт цілиться в добу 15 (±1)");

            Assert.NotNull(ferment, "еталон повинен побачити Брожіння за 30 діб");
            Assert.That(ferment.Value, Is.InRange(19, 22), "Брожіння цілиться в добу 20 (±1-2)");

            Assert.NotNull(heat, "еталон повинен побачити Накал за 30 діб");
            Assert.That(heat.Value, Is.InRange(21, 24), "Накал цілиться в добу 22-23");
        }

        [Test]
        public void Reference_CrisisRiotOffered_AfterFullForewarnLadder_NearDay25()
        {
            var (log, _) = Run(new PacifistPolicy(), suppressCouncilRoutine: true);

            int? l1 = FirstDayForewarn(log, 1, "crisis");
            int? l2 = FirstDayForewarn(log, 2, "crisis");
            int? l3 = FirstDayForewarn(log, 3, "crisis");
            int? riot = FirstDayCrisisResolved(log);

            Assert.NotNull(l1, "перший предвестник кризи має прозвучати");
            Assert.NotNull(l2, "другий предвестник кризи має прозвучати");
            Assert.NotNull(l3, "третій предвестник кризи має прозвучати");
            Assert.NotNull(riot, "великий кризис має вдарити за 30 діб для еталонного прогону");

            // Інваріант 4 / лестниця передвісників: жодна ступінь не пропущена,
            // і криза не має права вдарити, доки третя не прозвучала.
            Assert.LessOrEqual(l1.Value, l2.Value);
            Assert.LessOrEqual(l2.Value, l3.Value);
            Assert.LessOrEqual(l3.Value, riot.Value, "криза не може вдарити німо — третя ступінь має прозвучати ДО неї");

            Assert.That(riot.Value, Is.InRange(24, 26), "бунт цілиться в добу 25 (±1)");
        }

        // ================= (b) "Дефузер" =================

        [Test]
        public void Defuser_RiotIsLaterThanReference_OrNeverWithin30Days()
        {
            var (referenceLog, _) = Run(new PacifistPolicy(), suppressCouncilRoutine: true);
            var (defuserLog, _) = Run(new StewardPolicy(), suppressCouncilRoutine: false);

            int? referenceRiot = FirstDayCrisisResolved(referenceLog);
            int? defuserRiot = FirstDayCrisisResolved(defuserLog);

            Assert.NotNull(referenceRiot, "еталон — контрольна точка порівняння, має вдарити");

            // Облава (-80 Напруги) і згодом Храм/Укріплення (дренаж) — той самий
            // рутинний бот, що й AllMechanicsCoverageTests/tools/Alpha.Sim.
            // Або кризи взагалі немає за 30 діб, або вона прийшла ПІЗНІШЕ еталону.
            if (defuserRiot.HasValue)
                Assert.Greater(defuserRiot.Value, referenceRiot.Value,
                    "дефузер (Облава + Храм/Укріплення) має відкласти бунт пізніше еталону");
        }

        // ================= (c) "Шкідник" =================

        [Test]
        public void Neglect_RiotIsClearlyEarlierThanReference()
        {
            var (referenceLog, _) = Run(new PacifistPolicy(), suppressCouncilRoutine: true);
            var (neglectLog, _) = Run(new NeglectPolicy(), suppressCouncilRoutine: true);

            int? referenceRiot = FirstDayCrisisResolved(referenceLog);
            int? neglectRiot = FirstDayCrisisResolved(neglectLog);

            Assert.NotNull(referenceRiot);
            Assert.NotNull(neglectRiot, "шкідник (кроваво, порожні пости, без патруля) має добігти до бунту за 30 діб");
            Assert.Less(neglectRiot.Value, referenceRiot.Value,
                "шкідник (кроваво, порожні пости, без патруля, без Облави/Храму) мусить дійти до бунту раніше еталону");
        }

        // ================= бите/лог кризи =================

        [Test]
        public void Riot_AppliesBiteAndLogsCompanionDeath_DuringFreePlay()
        {
            // Регресія, знайдена саме цим заміром (24.09.2026): природна криза
            // раніше НІКОЛИ не спрацьовувала за жодного прогону (Напруга не
            // доходила до Накалу за тестований діапазон днів), тож побічний
            // ефект "GameSession.ResolveIncident ховає щойно розв'язаний
            // інцидент від TranslateReport (MarkIncidentsAlreadyTranslated), а
            // разом з ним — виклик HandleCompanionDeath для
            // CrisisBite.KillCompanion" лишався непоміченим.
            var (log, session) = Run(new NeglectPolicy(), suppressCouncilRoutine: true);

            int? riotDay = FirstDayCrisisResolved(log);
            Assert.NotNull(riotDay, "шкідник має дійти до бунту за 30 діб");
            Assert.Greater(riotDay.Value, 5, "бунт має статись у вільній грі — ПІСЛЯ форсованого фіналу доби 5");

            bool diedLogged = false;
            foreach (var e in log)
                if (e.Key == "companion.died" && e.Day == riotDay.Value) diedLogged = true;

            Assert.IsTrue(diedLogged,
                "CrisisBite.KillCompanion зобов'язаний лишити \"companion.died\" у DayLog того самого дня, що й бунт");
            Assert.AreEqual(SessionState.FreePlay, session.State, "прогін на 30 діб має завершитись у вільній грі");
        }

        // ================= флаг вимкнено: кампанія не бачить різниці =================

        [Test]
        public void Off_CampaignProfileUnchanged()
        {
            var campaignDefaultTension = new TensionBalance();
            var campaignDefaultPulse = new PulseBalance();

            var cfg = new BalanceConfig();
            var world = FirstHourWorld.Build(1, requirePlayerDecision: false, balance: cfg, testBuildTensionPace: false);

            Assert.AreEqual(campaignDefaultTension.BandThresholds, cfg.Tension.BandThresholds);
            Assert.AreEqual(campaignDefaultTension.TierTickPerDay, cfg.Tension.TierTickPerDay);
            Assert.AreEqual(campaignDefaultTension.TempleDrainPerDay, cfg.Tension.TempleDrainPerDay);
            Assert.AreEqual(campaignDefaultTension.FortificationDrainPerDay, cfg.Tension.FortificationDrainPerDay);
            Assert.AreEqual(campaignDefaultTension.HungerDeltaPerDay, cfg.Tension.HungerDeltaPerDay);
            Assert.AreEqual(campaignDefaultTension.RaidDelta, cfg.Tension.RaidDelta);

            Assert.AreEqual(campaignDefaultPulse.CrisisGraceDays, cfg.Pulse.CrisisGraceDays);
            Assert.AreEqual(campaignDefaultPulse.Forewarn1At, cfg.Pulse.Forewarn1At);

            // Джерело кризи лишається кампанійним (Threshold=120) — не
            // стисненим тестовим (internal PressureTrack, IVT).
            Assert.AreEqual(120, world.Processor.Pulse.Tracks["crisis"].Threshold);
        }

        [Test]
        public void On_IsTheDefaultForNewGameOptions()
        {
            // Усі три шляхи в тестову збірку (Unity/Alpha.Play/боти) йдуть крізь
            // GameSession.NewGame без явного значення цього поля — дефолт
            // зобов'язаний лишатись true, інакше тестова збірка мовчки втратить
            // стиснутий темп (той самий приём, що й TestBuildOneDayConstruction).
            Assert.IsTrue(new NewGameOptions().TestBuildTensionPace);
        }

        // ================= збереження/завантаження =================

        [Test]
        public void SaveLoad_MidFreePlay_PreservesTrajectory()
        {
            var baseline = TrajectorySnapshot(new StewardPolicy(), saveLoadAtDay: null);
            var withSaveLoad = TrajectorySnapshot(new StewardPolicy(), saveLoadAtDay: 18);

            Assert.AreEqual(baseline, withSaveLoad,
                "той самий детермінований прогін до і після SaveState/LoadState посеред вільної гри мусить дати ту саму траєкторію смуг");
        }

        private static List<string> TrajectorySnapshot(IBotPolicy policy, int? saveLoadAtDay)
        {
            var options = new NewGameOptions();
            var session = new GameSession(options.Roller);
            session.NewGame(options);

            var trajectory = new List<string>();

            if (saveLoadAtDay.HasValue)
            {
                var log1 = new List<GameEvent>();
                BotRunner.Drive(session, policy, saveLoadAtDay.Value, fullLog: log1, suppressCouncilRoutine: false);
                CollectBandAndForewarn(log1, trajectory);

                string blob = session.SaveState(0);
                session.LoadState(0);

                var log2 = new List<GameEvent>();
                BotRunner.Drive(session, policy, Days - saveLoadAtDay.Value, fullLog: log2, suppressCouncilRoutine: false);
                CollectBandAndForewarn(log2, trajectory);
            }
            else
            {
                var log = new List<GameEvent>();
                BotRunner.Drive(session, policy, Days, fullLog: log, suppressCouncilRoutine: false);
                CollectBandAndForewarn(log, trajectory);
            }

            return trajectory;
        }

        private static void CollectBandAndForewarn(List<GameEvent> log, List<string> outTrajectory)
        {
            foreach (var e in log)
                if (e.Key.StartsWith("tension.band.") || e.Key.StartsWith("forewarn.level"))
                    outTrajectory.Add(e.Day + ":" + e.Key);
        }
    }
}
