using System.Collections.Generic;
using System.Linq;
using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Checks;
using Game.Core.Combat;
using Game.Core.Economy;
using Game.Core.Expeditions;
using Game.Core.Loop;
using Game.Core.Pressure;
using Game.Core.Session;
using Game.Core.Stats;
using Game.Core.World;
using Game.Gameplay;
using Game.Gameplay.UI;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Погода (Поправка №21.2): чиста функція від доби, лише множники до наявних систем,
    /// прогноз чесний, сигнал — лише коли небо змінилось. Рівень графіки (№21.1) — автопідбір.
    /// </summary>
    public class WeatherTests
    {
        // ---------------- календар ----------------

        [Test]
        public void Calendar_IsDeterministic_SameDaySameWeather()
        {
            var a = new WeatherBalance();
            var b = new WeatherBalance();
            for (int day = 1; day <= 120; day++)
                Assert.AreEqual(WeatherCalendar.KindFor(day, a), WeatherCalendar.KindFor(day, b), "доба " + day);
        }

        [Test]
        public void Calendar_OpeningDays_AreCalm()
        {
            var cfg = new WeatherBalance();
            for (int day = 1; day <= cfg.CalmOpeningDays; day++)
            {
                var kind = WeatherCalendar.KindFor(day, cfg);
                Assert.That(kind == WeatherKind.Clear || kind == WeatherKind.Overcast,
                    "перша година — лише ясно або хмарно; доба " + day + ": " + kind);
            }
        }

        [Test]
        public void Calendar_EveryKindAppears_WithinTheCampaign()
        {
            var cfg = new WeatherBalance();
            var seen = new HashSet<WeatherKind>();
            for (int day = 1; day <= 90; day++) seen.Add(WeatherCalendar.KindFor(day, cfg));
            foreach (WeatherKind kind in System.Enum.GetValues(typeof(WeatherKind)))
                Assert.That(seen.Contains(kind), "за 90 діб жодного разу не було: " + kind);
        }

        [Test]
        public void Forecast_TomorrowIsExactlyTomorrowsWeather()
        {
            var cfg = new WeatherBalance();
            for (int day = 1; day <= 60; day++)
            {
                var f = WeatherCalendar.Forecast(day, cfg);
                Assert.AreEqual(WeatherCalendar.KindFor(day, cfg), f.today);
                Assert.AreEqual(WeatherCalendar.KindFor(day + 1, cfg), f.tomorrow, "прогноз мусить справджуватись");
            }
        }

        [Test]
        public void Disabled_IsAlwaysClear_AndAllMultipliersNeutral()
        {
            var cfg = new WeatherBalance { Enabled = false };
            for (int day = 1; day <= 90; day++) Assert.AreEqual(WeatherKind.Clear, WeatherCalendar.KindFor(day, cfg));
            Assert.AreEqual(1.0, cfg.Food(WeatherKind.Storm));
            Assert.AreEqual(1.0, cfg.ExpeditionYield(WeatherKind.Storm));
            Assert.AreEqual(1.0, cfg.NightRate(WeatherKind.Fog));
            Assert.AreEqual(0, cfg.RangedAccuracy(WeatherKind.Fog));
        }

        [Test]
        public void Override_WinsOverCalendar_EvenOnOpeningDays()
        {
            var cfg = new WeatherBalance { Overrides = new[] { new WeatherOverride(2, WeatherKind.Storm) } };
            Assert.AreEqual(WeatherKind.Storm, WeatherCalendar.KindFor(2, cfg));
        }

        // ---------------- ферми ----------------

        private static (BaseState state, BalanceConfig cfg) Farm()
        {
            var cfg = new BalanceConfig { FoodUpkeepPerCompanion = 0 };
            var roster = new Roster();
            var state = new BaseState(roster, new ResourceLedger(), cfg);
            var arch = new CompanionArchetype("farmer", "Хлібороб");
            arch.SetSkill(SkillType.Survival, 5);
            var comp = arch.CreateInstance("farmer_1");
            roster.Add(comp);
            state.AddSlot(new AssignmentSlotDefinition("farms", "Ниви", BaseSectionType.Workshop)
            {
                OutputKind = SlotOutputKind.Resource,
                OutputResource = ResourceType.Food,
                PrimarySkill = SkillType.Survival,
                BaseOutput = 10,
                OutputPerPrimaryPoint = 0,
                OutputPerSecondaryPoint = 0
            });
            state.TryAssign(comp.Id, "farms");
            return (state, cfg);
        }

        private static int FoodUnder(WeatherKind weather)
        {
            var (state, cfg) = Farm();
            state.FoodWeatherMultiplier = cfg.Weather.Food(weather);
            int before = state.Resources.Get(ResourceType.Food);
            state.AdvanceCycle();
            return state.Resources.Get(ResourceType.Food) - before;
        }

        [Test]
        public void Rain_WatersTheFields_StormBeatsThem()
        {
            int clear = FoodUnder(WeatherKind.Clear);
            Assert.Greater(FoodUnder(WeatherKind.Rain), clear, "дощ поливає ниви");
            Assert.Less(FoodUnder(WeatherKind.Storm), clear, "буря б'є врожай");
        }

        [Test]
        public void ProductionStep_TakesTheMultiplierFromTheDaysWeather()
        {
            var (state, cfg) = Farm();
            cfg.Weather.Overrides = new[] { new WeatherOverride(1, WeatherKind.Storm) };
            var step = new ProductionStep(state);
            step.Execute(new DayContext(1, DayPhase.Day, 1, 1, cfg, new TensionState(cfg.Tension)));
            Assert.AreEqual(cfg.Weather.Food(WeatherKind.Storm), state.FoodWeatherMultiplier);
        }

        // ---------------- вилазки ----------------

        private static ExpeditionSite Site() => new ExpeditionSite("ruins", "Руїни")
        {
            QuietDays = 6, ForcefulDays = 3,
            QuietSkill = SkillKeys.Survival, ForcefulSkill = SkillKeys.Survival,
            Threshold = 3, BaseBuildComponent = 8, BaseGold = 20
        };

        private static List<ISettlementActor> Party(BalanceConfig cfg)
        {
            var arch = new CompanionArchetype("scout", "Розвідник");
            arch.SetSkill(SkillType.Survival, 6);
            return new List<ISettlementActor> { new CompanionActorAdapter(arch.CreateInstance("scout_1", cfg), false, cfg) };
        }

        [Test]
        public void Storm_CutsExpeditionYield_AndPreviewStillMatchesResolve()
        {
            var cfg = new BalanceConfig { FoodUpkeepPerCompanion = 0 };
            var clear = ExpeditionResolver.Preview(Site(), ExpeditionApproach.Quiet, Party(cfg), new SiteLedger(), cfg);
            var storm = ExpeditionResolver.Preview(Site(), ExpeditionApproach.Quiet, Party(cfg), new SiteLedger(), cfg, WeatherKind.Storm);
            var result = ExpeditionResolver.Resolve(Site(), ExpeditionApproach.Quiet, Party(cfg), new SiteLedger(), cfg, WeatherKind.Storm);

            Assert.Less(storm.Gold, clear.Gold, "у бурю несуть менше");
            Assert.AreEqual(storm.Gold, result.Gold, "прев'ю = резолв (інваріант 8)");
            Assert.AreEqual(storm.BuildComponent, result.BuildComponent);
            Assert.AreEqual(WeatherKind.Storm, storm.Weather);
        }

        // ---------------- ніч ----------------

        [Test]
        public void FogAtNight_FeedsTheNightAccumulatorFaster_WithoutANewDriver()
        {
            var source = new NightPressureSource();
            var clear = new PulseContext(10, true, 1, 0, false);
            var fog = new PulseContext(10, true, 1, 0, false, nightWeatherBonusPercent: 30);
            Assert.Greater(source.InsistencePerDay(fog), source.InsistencePerDay(clear));

            var day = new PulseContext(10, false, 1, 0, false, nightWeatherBonusPercent: 30);
            Assert.AreEqual(NightPressureSource.ScoutingRate, source.InsistencePerDay(day), "вдень погода нічну розвідку не чіпає");
        }

        // ---------------- бій ----------------

        [Test]
        public void HitChance_WeatherIsItsOwnVisibleTerm()
        {
            var cfg = new BalanceConfig();
            int delta = cfg.Weather.RangedAccuracy(WeatherKind.Fog);
            Assert.Less(delta, 0, "туман заважає дальнім пострілам");

            var terms = HitChanceCalculator.Decompose(60, false, 0, CoverType.None, false, 0, 6, cfg, weatherDelta: delta);
            Assert.AreEqual(delta, terms.Single(t => t.Key == HitChanceCalculator.TermKeys.Weather).ChanceDelta,
                "причину видно окремим рядком розкладу");
            Assert.AreEqual(60 + delta,
                HitChanceCalculator.Compute(60, false, 0, CoverType.None, false, 0, 6, cfg, weatherDelta: delta));
        }

        // ---------------- сигнал ----------------

        [Test]
        public void WeatherLine_OnlyWhenTheSkyChanges()
        {
            var cfg = new BalanceConfig();
            cfg.Weather.Overrides = new[]
            {
                new WeatherOverride(1, WeatherKind.Rain),
                new WeatherOverride(2, WeatherKind.Rain),
                new WeatherOverride(3, WeatherKind.Fog)
            };
            var p = new DayProcessor(new TensionState(cfg.Tension), cfg, new IDayStep[] { new SignalStep() });

            bool Has(DayReport r, string topic) => r.Signals.Requests.Any(q => q.TopicId == topic);

            var d1 = p.Advance(DayPhase.Day);
            Assert.IsTrue(Has(d1, "weather.ambient.Rain"), "учора було ясно — про дощ кажуть");
            p.Advance(DayPhase.Night);
            var d2 = p.Advance(DayPhase.Day);
            Assert.IsFalse(d2.Signals.Requests.Any(q => q.TopicId.StartsWith("weather.")), "про той самий дощ удруге не кажуть");
            p.Advance(DayPhase.Night);
            var d3 = p.Advance(DayPhase.Day);
            Assert.IsTrue(Has(d3, "weather.ambient.Fog"));
        }

        // ---------------- сесія і шапка ----------------

        [Test]
        public void Session_FirstMorning_ShowsDayOnesWeather_AndTomorrow()
        {
            var s = new GameSession();
            s.NewGame(new NewGameOptions { SkipCreation = true, HitRule = HitRuleKind.Threshold });
            var step = s.AdvanceScene();
            while (!step.IsFinished) step = step.IsChoice ? s.ChooseSceneOption(0) : s.AdvanceScene();
            Assert.AreEqual(SessionState.Morning, s.State);

            var cfg = new WeatherBalance();
            var view = s.GetWeatherView();
            Assert.AreEqual(WeatherCalendar.KindFor(1, cfg).ToString(), view.Today, "уранці доби 1 — небо доби 1");
            Assert.AreEqual(WeatherCalendar.KindFor(2, cfg).ToString(), view.Tomorrow);
        }

        [Test]
        public void HudHeader_ShowsWeatherWithForecast()
        {
            var line = HudHeaderModel.WeatherLine(new Game.Core.Session.Views.WeatherView { Today = "Rain", Tomorrow = "Fog" },
                Game.Core.Characters.Creation.Gender.Male);
            StringAssert.Contains("Дощ", line);
            StringAssert.Contains("Туман", line);
            Assert.IsNull(HudHeaderModel.WeatherLine(null, Game.Core.Characters.Creation.Gender.Male));
        }

        [Test]
        public void VillageLight_RainIsDarkerThanClear_FogTurnsOnFog()
        {
            var mood = new Game.Core.Signals.MoodboardState(0, 0, null);
            Assert.Less(VillageView.SunFor(DayPhase.Day, WeatherKind.Rain).Intensity,
                VillageView.SunFor(DayPhase.Day, WeatherKind.Clear).Intensity);
            Assert.IsFalse(VillageView.FogFor(DayPhase.Day, mood, WeatherKind.Clear).On);
            Assert.IsTrue(VillageView.FogFor(DayPhase.Day, mood, WeatherKind.Fog).On);
            Assert.AreEqual(WeatherKind.Storm, VillageView.ParseWeather("Storm"));
            Assert.AreEqual(WeatherKind.Clear, VillageView.ParseWeather("щось інше"));
        }

        // ---------------- рівень графіки (№21.1) ----------------

        [Test]
        public void GraphicsPicker_WeakMachineGetsLow_StrongGetsHigh()
        {
            Assert.AreEqual(GraphicsLevel.Low, GraphicsTierPicker.Pick("Intel(R) UHD Graphics 620", 1024, 8192, 4),
                "цільова слабка машина Поправки №21.1");
            Assert.AreEqual(GraphicsLevel.Low, GraphicsTierPicker.Pick("AMD Radeon(TM) Graphics", 2048, 16384, 8));
            Assert.AreEqual(GraphicsLevel.Medium, GraphicsTierPicker.Pick("NVIDIA GeForce GTX 1050", 2048, 8192, 4));
            Assert.AreEqual(GraphicsLevel.High, GraphicsTierPicker.Pick("NVIDIA GeForce RTX 3060", 12288, 16384, 12));
            Assert.AreEqual(GraphicsLevel.High, GraphicsTierPicker.Pick("Intel(R) Arc(TM) A770", 16384, 32768, 16),
                "дискретна Intel Arc — не вбудована");
        }
    }
}
