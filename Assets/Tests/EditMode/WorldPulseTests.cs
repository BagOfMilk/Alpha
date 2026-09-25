using System.Collections.Generic;
using System.Linq;
using Game.Core.Balance;
using Game.Core.World;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Пульс — заміна кубику. Тут захищається головна обіцянка дизайну:
    /// перш ніж вдарити, світ попереджає.
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
            public bool Announces => true;
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

                // Ступінь зараховується лише доставлена — інакше накопичувач
                // чесно пропонує одну й ту саму знову і знову. У грі це
                // робить крок Pulse; тут ми граємо його роль.
                pulse.MarkDelivered(tick.Forewarnings, day);
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

        /// <summary>
        /// Попередження зобов'язане передувати КОЖНОМУ удару, а не лише
        /// першому. Ставка 30 при порозі 100: на 4-ту добу накопичувач і
        /// доходить до третьої ступені, і б'є — в одному тіку. Раніше ця
        /// ступінь видавалася разом з ударом, а зараховувалась (MarkDelivered
        /// після Advance) вже на обнулений трек: друге коло починалося з
        /// «третя почута» і мовчало до наступного удару.
        /// </summary>
        [Test]
        public void Pulse_EveryCycle_HasItsOwnLadder_BeforeTheFire()
        {
            var pulse = new WorldPulse(Cfg());
            pulse.AddSource(new FixedSource { Rate = 30, Threshold = 100 });

            var heardThisCycle = new List<int>();
            int fires = 0;
            for (int day = 1; day <= 20; day++)
            {
                var tick = pulse.Advance(Ctx(day));
                pulse.MarkDelivered(tick.Forewarnings, day);
                bool fired = tick.FiredSourceIds.Contains("src");

                if (fired)
                    Assert.IsEmpty(tick.Forewarnings,
                        $"День {day}: удар сам и есть событие — предвестник в том же тике ничего не предупреждает");
                heardThisCycle.AddRange(tick.Forewarnings.Select(f => f.Level));

                if (!fired) continue;
                fires++;
                CollectionAssert.AreEqual(new[] { 1, 2 }, heardThisCycle,
                    $"Удар №{fires} (день {day}): его круг обязан пройти свою лестницу, а не молчать");
                Assert.AreEqual(0, pulse.DeliveredLevelOf("src"),
                    $"День {day}: после удара лестница начинается с нуля, а не с засчитанной задним числом ступени");
                heardThisCycle.Clear();
            }

            Assert.GreaterOrEqual(fires, 3, "Нужно несколько кругов: вырождение видно со второго");
        }

        /// <summary>
        /// «Передвісник не бреше» (SETTLEMENT_LAYER §5.1, правило 4). Криза
        /// копиться і під час відкату, і раніше її драбина знову доходила до
        /// третьої ступені через кілька діб після кризи — а наступний
        /// відкат пускав лише через 30: «скоро» висіло 25 діб. Тепер кожна
        /// почута третя ступінь веде до кризи не довше ніж за вікно на
        /// реакцію плюс довжину драбини, а дні криз ті самі — рівно через
        /// відкат (драбина встигає до його кінця).
        /// </summary>
        [Test]
        public void Pulse_CrisisLadder_NeverPromisesACrisisTheCooldownForbids()
        {
            var cfg = Cfg();
            var pulse = new WorldPulse(cfg);
            pulse.AddSource(new FixedSource
            {
                Id = "crisis", Kind = WorldEventKind.Crisis, Rate = 12, Threshold = 120, CooldownDays = 30
            });

            const int days = 130;
            var level3Days = new List<int>();
            var fireDays = new List<int>();
            for (int day = 1; day <= days; day++)
            {
                var tick = pulse.Advance(Ctx(day));
                pulse.MarkDelivered(tick.Forewarnings, day);
                if (tick.FiredSourceIds.Contains("crisis")) fireDays.Add(day);
                level3Days.AddRange(tick.Forewarnings.Where(f => f.Level == 3).Select(f => day));
            }

            Assert.GreaterOrEqual(fireDays.Count, 3, "нужно несколько кругов: ложь видна со второго");
            for (int i = 1; i < fireDays.Count; i++)
                Assert.AreEqual(30, fireDays[i] - fireDays[i - 1],
                    "насыщенный кризис бьёт ровно через откат — лестница не должна его задерживать");

            int promise = cfg.CrisisGraceDays + cfg.CrisisLadderLeadDays;
            foreach (int d3 in level3Days)
            {
                if (d3 + promise > days) continue;
                Assert.IsTrue(fireDays.Exists(f => f > d3 && f <= d3 + promise),
                    $"третья ступень на сутки {d3} не привела к кризису до суток {d3 + promise} (кризисы: {string.Join(",", fireDays)})");
            }
        }

        /// <summary>
        /// Перша криза не утримується: відкату ще не було, і драбина
        /// звучить за заповненням, як завжди. Знайдено рев'ю 25.09.2026: без
        /// цієї перевірки зняття винятку «ще не спрацьовував» зсувало першу
        /// кризу з 13-ї доби на 30-ту, а кампанійні тести мовчали — у них
        /// накопичувач починає копитися (з «Розпалу») вже після 30-ї доби.
        /// </summary>
        [Test]
        public void Pulse_FirstCrisis_IsNotHeldBack_ByAnUnstartedCooldown()
        {
            var cfg = Cfg();
            var pulse = new WorldPulse(cfg);
            pulse.AddSource(new FixedSource
            {
                Id = "crisis", Kind = WorldEventKind.Crisis, Rate = 12, Threshold = 120, CooldownDays = 30
            });

            int firstLevel1 = -1, firstFire = -1;
            for (int day = 1; day <= 40 && firstFire < 0; day++)
            {
                var tick = pulse.Advance(Ctx(day));
                pulse.MarkDelivered(tick.Forewarnings, day);
                if (firstLevel1 < 0 && tick.Forewarnings.Any(f => f.Level == 1)) firstLevel1 = day;
                if (tick.FiredSourceIds.Contains("crisis")) firstFire = day;
            }

            Assert.AreEqual(6, firstLevel1, "ставка 12, порог 120: первая ступень — на 6-е сутки (55%), без удержания");
            Assert.AreEqual(10 + cfg.CrisisGraceDays, firstFire,
                "третья ступень на 10-е сутки + окно на реакцию — первый кризис не ждёт отката, которого не было");
        }

        /// <summary>Утримання стосується лише кризи: інші загрози з коротким відкатом попереджають, як раніше.</summary>
        [Test]
        public void Pulse_NonCrisisSource_ForewarnsDuringCooldown_AsBefore()
        {
            var pulse = new WorldPulse(Cfg());
            pulse.AddSource(new FixedSource { Rate = 60, Threshold = 100, CooldownDays = 10 });

            int fireDay = -1;
            for (int day = 1; day <= 12 && fireDay < 0; day++)
            {
                var tick = pulse.Advance(Ctx(day));
                pulse.MarkDelivered(tick.Forewarnings, day);
                if (tick.FiredSourceIds.Count > 0) fireDay = day;
            }
            Assert.Greater(fireDay, 0);

            var next = pulse.Advance(Ctx(fireDay + 1));
            Assert.IsTrue(next.Forewarnings.Any(f => f.Level == 1),
                "не-кризисная угроза начинает новую лестницу сразу после удара, как и раньше");
        }

        [Test]
        public void Pulse_ForewarnLevel2_NamesDomain()
        {
            var pulse = new WorldPulse(Cfg());
            pulse.AddSource(new FixedSource { Rate = 10, Threshold = 100, DomainTag = "склад" });

            for (int day = 1; day <= 10; day++)
            {
                var tick = pulse.Advance(Ctx(day));
                pulse.MarkDelivered(tick.Forewarnings, day);

                foreach (var f in tick.Forewarnings)
                    if (f.Level >= 2)
                    {
                        Assert.AreEqual("склад", f.DomainTag,
                            "Со 2-й ступени игрок обязан узнать, ГДЕ зреет");
                        return;
                    }
            }
            Assert.Fail("Предвестник 2-й ступени не появился");
        }

        [Test]
        public void Pulse_ActiveTrackCount_AcrossWholeStateSpace()
        {
            var cfg = Cfg();
            var pulse = new WorldPulse(cfg);
            foreach (var s in DefaultPressureSources.All()) pulse.AddSource(s);

            // Попередня версія цього тесту міряла ОДНУ точку — ніч на верхній
            // полосі, — тобто рівно той єдиний кут, де інваріант
            // виконується. Перебираємо весь набір станів цілком.
            int min = int.MaxValue, max = 0, meetingInvariant = 0, total = 0;
            for (int band = 0; band <= 4; band++)
                foreach (bool night in new[] { false, true })
                {
                    int active = pulse.CountActive(Ctx(1, night: night, band: band));
                    if (active < min) min = active;
                    if (active > max) max = active;
                    if (active >= cfg.MinActiveTracks) meetingInvariant++;
                    total++;
                }

            // ВІДОМИЙ РОЗРИВ, зафіксований навмисно: MinActiveTracks = 3
            // виконується у 2 станах з 10, тому що всі три ставки —
            // функції однієї полоси. Тест тримає реальну картину на видноті; коли
            // накопичувачі перестануть бути трьома обгортками однієї змінної, він
            // впаде — і це буде приводом оновити очікування, а не підігнати його.
            Assert.AreEqual(1, min, "Худший случай: работает один накопитель");
            Assert.AreEqual(3, max, "Лучший случай: работают все три");
            Assert.AreEqual(2, meetingInvariant,
                $"Инвариант «не меньше трёх» держится в {meetingInvariant} состояниях из {total}");
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

        /// <summary>
        /// Готове джерело, якому нічим спрацювати, не розряджається: заряд і
        /// почута драбина лишаються, єдиний слот доби йде тому, у
        /// кого наслідок є. Сирота навмисно стоїть раніше за Id: без питання
        /// про наслідок нічию брав би він — і згорав би мовчки, забираючи слот.
        /// </summary>
        [Test]
        public void Pulse_ReadySourceWithoutConsequence_HoldsItsChargeAndLadder_AndYieldsTheSlot()
        {
            var cfg = Cfg();
            var pulse = new WorldPulse(cfg);
            pulse.AddSource(new FixedSource { Id = "a_orphan", Rate = 25, Threshold = 100 });
            pulse.AddSource(new FixedSource { Id = "b_real", Rate = 25, Threshold = 100 });
            System.Func<string, bool> hasConsequence = id => id == "b_real";

            var orphanLevels = new List<int>();
            int realFiredOn = -1;
            double lastFill = 0.0;
            for (int day = 1; day <= 10; day++)
            {
                var tick = pulse.Advance(Ctx(day), hasConsequence);
                pulse.MarkDelivered(tick.Forewarnings, day);

                CollectionAssert.DoesNotContain(tick.FiredSourceIds, "a_orphan", $"День {day}: сироте нечем сработать");
                if (realFiredOn < 0 && tick.FiredSourceIds.Contains("b_real")) realFiredOn = day;
                orphanLevels.AddRange(tick.Forewarnings.Where(f => f.SourceId == "a_orphan").Select(f => f.Level));

                double fill = pulse.Tracks["a_orphan"].Fill;
                Assert.GreaterOrEqual(fill, lastFill, $"День {day}: заряд сироты сброшен без события");
                lastFill = fill;
            }

            Assert.AreEqual(4, realFiredOn, "Слот дня, в который оба дошли до порога, достаётся источнику с последствием");
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, orphanLevels,
                "Лестница сироты проходится один раз и стоит на третьей ступени, а не начинается заново");
            Assert.AreEqual(3, pulse.DeliveredLevelOf("a_orphan"));
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
