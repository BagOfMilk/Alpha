using Game.Core.Balance;
using Game.Core.Checks;
using Game.Core.Loop;
using Game.Core.Pressure;
using Game.Core.Settlement;
using Game.Core.Signals;
using Game.Core.Story;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Готовність громади (R8) — друга шкала того ж роду, що Напруга
    /// (інваріант 2), і фінал, чия складність зсунута нею (§3.5, §4.14
    /// TEST_BUILD.md). <c>ReadinessTickStep</c> стоїть на
    /// <c>DayStepOrder.Readiness</c> (550) і читає лише те, що вже видно з
    /// конвеєра дня — будівництво (<c>DayContext.CityEvents</c>) і відсутність
    /// страху (<c>FearState</c>); вилазку й квест (поза конвеєром, R6) додає
    /// напряму D1 — це задокументований сеам, не пропуск.
    /// </summary>
    public class ReadinessAndFinaleTests
    {
        private static ReadinessBalance Cfg() => new ReadinessBalance();

        // ---- ReadinessTrack: якір/потребитель/сигнал (інваріант 6) ----

        [Test]
        public void Add_ChangesBand_AndFiresSignal()
        {
            var track = new ReadinessTrack(Cfg());
            ReadinessBand? from = null, to = null;
            track.BandChanged += (a, b) => { from = a; to = b; };

            track.Add(30); // поріг Unprepared->Bracing = 25 (плейсхолдер)

            Assert.AreEqual(ReadinessBand.Bracing, track.Band);
            Assert.AreEqual(ReadinessBand.Unprepared, from);
            Assert.AreEqual(ReadinessBand.Bracing, to);
        }

        [Test]
        public void Add_NeverNegative_ClampsAtZero()
        {
            var track = new ReadinessTrack(Cfg());
            track.Add(-999);
            Assert.AreEqual(ReadinessBand.Unprepared, track.Band);
            Assert.AreEqual(0, track.Value);
        }

        [Test]
        public void SaveRoundTrip_PreservesValueAndBand_WithoutFiringSignal()
        {
            var track = new ReadinessTrack(Cfg());
            track.Add(60);
            var blob = track.CaptureState();

            var restored = new ReadinessTrack(Cfg());
            bool fired = false;
            restored.BandChanged += (a, b) => fired = true;
            restored.RestoreState(blob);

            Assert.AreEqual(track.Band, restored.Band);
            Assert.AreEqual(track.Value, restored.Value);
            Assert.IsFalse(fired, "завантаження — не подія гри, а її продовження (як TensionState.RestoreForSave)");
        }

        // ---- ReadinessTickStep: віхи, видні з конвеєра дня ----

        private static DayContext NewDayContext(int day = 1, bool night = false)
        {
            var balance = new BalanceConfig();
            var tension = new TensionState(balance.Tension);
            return new DayContext(day, night ? DayPhase.Night : DayPhase.Day, 1, 1, balance, tension)
            {
                Fear = new FearState()
            };
        }

        [Test]
        public void TickStep_BuildingCompleted_AddsAmount()
        {
            var track = new ReadinessTrack(Cfg());
            var step = new ReadinessTickStep(track, Cfg());
            var ctx = NewDayContext();
            // Ізолюємо саме будівельну віху: громада боїться, тож бонус «немає
            // страху» сьогодні не спрацює і не домішається до суми.
            ctx.Fear.Remember(ctx.Day, new BalanceConfig().Checks);
            ctx.CityEvents.Add(new CityEvent("city.built.workshop", SignalUrgency.Notable));

            step.Execute(ctx);

            Assert.AreEqual(Cfg().BuildingCompletedAmount, track.Value);
        }

        [Test]
        public void TickStep_PrepareThreatEvent_AddsAmount()
        {
            var track = new ReadinessTrack(Cfg());
            var step = new ReadinessTickStep(track, Cfg());
            var ctx = NewDayContext();
            ctx.Fear.Remember(ctx.Day, new BalanceConfig().Checks); // ізолюємо від бонуса «немає страху»
            ctx.CityEvents.Add(new CityEvent("council.prepare_threat.ordered", SignalUrgency.Notable));

            step.Execute(ctx);

            Assert.AreEqual(Cfg().PrepareThreatAmount, track.Value);
        }

        [Test]
        public void TickStep_NoFearToday_AddsAmount()
        {
            var track = new ReadinessTrack(Cfg());
            var step = new ReadinessTickStep(track, Cfg());
            var ctx = NewDayContext(day: 5);
            // FearState свіжий -> IsAfraid(5) == false.

            step.Execute(ctx);

            Assert.AreEqual(Cfg().NoFearDayAmount, track.Value);
        }

        [Test]
        public void TickStep_FearActive_NoGainFromFear()
        {
            var track = new ReadinessTrack(Cfg());
            var step = new ReadinessTickStep(track, Cfg());
            var ctx = NewDayContext(day: 5);
            ctx.Fear.Remember(5, new BalanceConfig().Checks);
            Assert.IsTrue(ctx.Fear.IsAfraid(5));

            step.Execute(ctx);

            Assert.AreEqual(0, track.Value, "поки громада боїться — жодної надбавки за «немає страху»");
        }

        [Test]
        public void TickStep_NightPhase_DoesNothing()
        {
            var track = new ReadinessTrack(Cfg());
            var step = new ReadinessTickStep(track, Cfg());
            var ctx = NewDayContext(night: true);
            ctx.CityEvents.Add(new CityEvent("city.built.workshop", SignalUrgency.Notable));

            step.Execute(ctx);

            Assert.AreEqual(0, track.Value, "той самий гейт, що в Construction/Tension: доба — одна фаза для приросту");
        }

        // ---- Finale: складність монотонно зсунута полосою Готовності ----

        [Test]
        public void BuildAssault_WorseReadiness_MeansMoreEnemies_AndAlwaysBurunda()
        {
            var worst = Finale.BuildAssault(ReadinessBand.Unprepared);
            var best = Finale.BuildAssault(ReadinessBand.Fortified);

            Assert.Greater(worst.EnemyDefinitionIds.Count, best.EnemyDefinitionIds.Count,
                "гірша Готовність — важчий фінал (монотонність, акцептанс B6)");
            CollectionAssert.Contains(worst.EnemyDefinitionIds, Finale.BurundaBossId);
            CollectionAssert.Contains(best.EnemyDefinitionIds, Finale.BurundaBossId, "бос — завжди, окремо від рахунку рядових");
        }

        [Test]
        public void BuildAssault_PassesDefectorIdThrough()
        {
            var plan = Finale.BuildAssault(ReadinessBand.Ready, "myroslava");
            Assert.AreEqual("myroslava", plan.DefectorCompanionId);

            var noDefector = Finale.BuildAssault(ReadinessBand.Ready);
            Assert.IsNull(noDefector.DefectorCompanionId);
        }

        [Test]
        public void BuildDam_WorseReadiness_MeansHigherThreshold()
        {
            var worst = Finale.BuildDam(ReadinessBand.Unprepared);
            var best = Finale.BuildDam(ReadinessBand.Fortified);

            Assert.Greater(worst.Threshold, best.Threshold,
                "поріг показаний заздалегідь (інваріант 8) і монотонно важчий при гіршій Готовності");
            Assert.AreEqual(SkillKeys.Mechanics, worst.Skill);
        }

        [Test]
        public void Finale_MonotonicDifficulty_AcrossTwoReadinessBands()
        {
            // Акцептанс пакета B6 (§5 TEST_BUILD.md): дві Готовності — обидва шляхи важчі на гіршій.
            Assert.Greater(
                Finale.BuildAssault(ReadinessBand.Bracing).EnemyDefinitionIds.Count,
                Finale.BuildAssault(ReadinessBand.Ready).EnemyDefinitionIds.Count);
            Assert.Greater(
                Finale.BuildDam(ReadinessBand.Bracing).Threshold,
                Finale.BuildDam(ReadinessBand.Ready).Threshold);
        }

        [Test]
        public void Resolve_MapsBandToKeyAndCost_NoBandIsFree()
        {
            var best = Finale.Resolve(OutcomeBand.Best);
            var good = Finale.Resolve(OutcomeBand.Good);
            var baseR = Finale.Resolve(OutcomeBand.Base);
            var worst = Finale.Resolve(OutcomeBand.Worst);

            Assert.AreEqual("best", best.Key);
            Assert.AreEqual("worst", worst.Key);

            Assert.AreEqual(FinaleCostKind.Compromise, best.Cost);
            Assert.AreEqual(FinaleCostKind.Compromise, good.Cost);
            Assert.AreEqual(FinaleCostKind.Hostage, baseR.Cost);
            Assert.AreEqual(FinaleCostKind.Hostage, worst.Cost, "жодна полоса не «чиста» перемога (§7.15)");
        }
    }
}
