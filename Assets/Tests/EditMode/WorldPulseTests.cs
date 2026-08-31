using System.Collections.Generic;
using System.Linq;
using Game.Core.Balance;
using Game.Core.World;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Пульс — замена кубику. Здесь защищается главное обещание дизайна:
    /// прежде чем ударить, мир предупреждает.
    /// </summary>
    public class WorldPulseTests
    {
        private sealed class FixedSource : IPressureSource
        {
            public string Id { get; set; } = "src";
            public WorldEventKind Kind { get; set; } = WorldEventKind.InternalThreat;
            public string DomainTag { get; set; } = "домен";
            public int Rate { get; set; } = 10;
            public int Threshold { get; set; } = 100;
            public int CooldownDays { get; set; }
            public bool Active { get; set; } = true;

            public int InsistencePerDay(PulseContext ctx) => Rate;
            public bool IsActive(PulseContext ctx) => Active;
        }

        private static PulseBalance Cfg() => new PulseBalance();
        private static PulseContext Ctx(int day, bool night = false, int band = 0, bool patrol = false)
            => new PulseContext(day, night, 1, band, patrol);

        [Test]
        public void Pulse_FiresExactlyWhenChargeReachesThreshold()
        {
            var pulse = new WorldPulse(Cfg());
            pulse.AddSource(new FixedSource { Rate = 10, Threshold = 100 });

            for (int day = 1; day <= 9; day++)
                Assert.IsEmpty(pulse.Advance(Ctx(day)).FiredSourceIds, $"День {day}: рано");

            Assert.IsNotEmpty(pulse.Advance(Ctx(10)).FiredSourceIds, "Ставка 10, порог 100 → день 10");
        }

        [Test]
        public void Pulse_ForewarnPrecedesEveryFire()
        {
            var pulse = new WorldPulse(Cfg());
            pulse.AddSource(new FixedSource { Rate = 10, Threshold = 100 });

            var seenLevels = new List<int>();
            for (int day = 1; day <= 10; day++)
            {
                var tick = pulse.Advance(Ctx(day));
                foreach (var f in tick.Forewarnings) seenLevels.Add(f.Level);

                if (tick.FiredSourceIds.Count > 0)
                {
                    Assert.Contains(1, seenLevels, "Перед ударом обязан быть предвестник 1-й ступени");
                    Assert.Contains(2, seenLevels, "И 2-й — с названным доменом");
                    return;
                }
            }
            Assert.Fail("Источник так и не сработал");
        }

        [Test]
        public void Pulse_ForewarnLevel2_NamesDomain()
        {
            var pulse = new WorldPulse(Cfg());
            pulse.AddSource(new FixedSource { Rate = 10, Threshold = 100, DomainTag = "склад" });

            for (int day = 1; day <= 10; day++)
                foreach (var f in pulse.Advance(Ctx(day)).Forewarnings)
                    if (f.Level >= 2)
                    {
                        Assert.AreEqual("склад", f.DomainTag,
                            "Со 2-й ступени игрок обязан узнать, ГДЕ зреет");
                        return;
                    }
            Assert.Fail("Предвестник 2-й ступени не появился");
        }

        [Test]
        public void Pulse_DefaultSources_SatisfyMinActiveTracks()
        {
            var cfg = Cfg();
            var pulse = new WorldPulse(cfg);
            foreach (var s in DefaultPressureSources.All()) pulse.AddSource(s);

            // Ночь на верхней полосе — момент, когда работают все три.
            int active = pulse.CountActive(Ctx(1, night: true, band: 4));

            Assert.GreaterOrEqual(active, cfg.MinActiveTracks,
                "Меньше трёх накопителей — и полный детерминизм читается насквозь");
        }

        [Test]
        public void Pulse_Cooldown_PreventsImmediateRepeat()
        {
            var pulse = new WorldPulse(Cfg());
            pulse.AddSource(new FixedSource { Rate = 200, Threshold = 100, CooldownDays = 5 });

            Assert.IsNotEmpty(pulse.Advance(Ctx(1)).FiredSourceIds);
            Assert.IsEmpty(pulse.Advance(Ctx(2)).FiredSourceIds, "КД не даёт бить каждый день");
            Assert.IsNotEmpty(pulse.Advance(Ctx(6)).FiredSourceIds, "После КД — снова можно");
        }

        [Test]
        public void Pulse_InactiveSource_DoesNotAccumulate()
        {
            var pulse = new WorldPulse(Cfg());
            pulse.AddSource(new FixedSource { Rate = 100, Threshold = 100, Active = false });

            for (int day = 1; day <= 20; day++)
                Assert.IsEmpty(pulse.Advance(Ctx(day)).FiredSourceIds);
        }

        [Test]
        public void Pulse_NightBudget_AllowsMoreFires()
        {
            var cfg = Cfg();
            var day = new WorldPulse(cfg);
            var night = new WorldPulse(cfg);

            for (int i = 0; i < 3; i++)
            {
                day.AddSource(new FixedSource { Id = "s" + i, Rate = 200, Threshold = 100 });
                night.AddSource(new FixedSource { Id = "s" + i, Rate = 200, Threshold = 100 });
            }

            Assert.AreEqual(cfg.MaxFiresPerDay, day.Advance(Ctx(1)).FiredSourceIds.Count);
            Assert.AreEqual(cfg.MaxFiresPerNight, night.Advance(Ctx(1, night: true)).FiredSourceIds.Count,
                "Ночью инциденты кучнее (US-11.1)");
        }

        [Test]
        public void Pulse_Patrol_SlowsNightCrime()
        {
            var asleep = new NightPressureSource().InsistencePerDay(Ctx(1, night: true, band: 2));
            var patrolling = new NightPressureSource().InsistencePerDay(Ctx(1, night: true, band: 2, patrol: true));

            Assert.Less(patrolling, asleep, "Патруль — небоевая контригра ночной преступности");
        }

        [Test]
        public void Pulse_RateGrowsWithTension()
        {
            var street = new StreetPressureSource();
            Assert.Less(street.InsistencePerDay(Ctx(1, band: 0)), street.InsistencePerDay(Ctx(1, band: 4)),
                "Напряжённый город порождает события чаще");
        }

        [Test]
        public void Pulse_SameInputs_IdenticalSchedule()
        {
            string Run()
            {
                var pulse = new WorldPulse(Cfg());
                foreach (var s in DefaultPressureSources.All()) pulse.AddSource(s);

                var log = new System.Text.StringBuilder();
                for (int day = 1; day <= 200; day++)
                {
                    bool night = day % 2 == 0;
                    var tick = pulse.Advance(Ctx(day, night, band: day % 5));
                    foreach (var id in tick.FiredSourceIds) log.Append(day).Append(':').Append(id).Append(';');
                }
                return log.ToString();
            }

            Assert.AreEqual(Run(), Run(), "200 дней должны воспроизводиться день в день");
        }

        [Test]
        public void Pulse_SourceOrder_DoesNotAffectSchedule()
        {
            string Run(bool reversed)
            {
                var pulse = new WorldPulse(Cfg());
                var sources = DefaultPressureSources.All().ToList();
                if (reversed) sources.Reverse();
                foreach (var s in sources) pulse.AddSource(s);

                var log = new System.Text.StringBuilder();
                for (int day = 1; day <= 100; day++)
                {
                    var tick = pulse.Advance(Ctx(day, day % 2 == 0, band: 4));
                    foreach (var id in tick.FiredSourceIds.OrderBy(x => x))
                        log.Append(day).Append(':').Append(id).Append(';');
                }
                return log.ToString();
            }

            Assert.AreEqual(Run(false), Run(true),
                "Расписание не должно зависеть от порядка регистрации источников");
        }
    }
}
