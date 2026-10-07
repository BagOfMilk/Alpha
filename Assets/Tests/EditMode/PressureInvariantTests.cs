using System;
using System.Collections.Generic;
using System.Linq;
using Game.Core.Balance;
using Game.Core.World;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Охоронець інваріанта 2 (M1.7, AUDIT G11): «не менш ніж три активні
    /// накопичувачі з різними ставками, і ставки змінюються від дій гравця».
    ///
    /// Колись усі три накопичувачі були функціями однієї полоси Напруги, і
    /// тест лише ФІКСУВАВ розрив («три — у 2 станах з 10»). Тепер вимагається:
    /// (1) у КОЖНОМУ стані перебору активних не менше <see cref="PulseBalance.MinActiveTracks"/>;
    /// (2) кожне джерело має ВЛАСНУ вхідну змінну — таку, що рухає його ставку і
    /// не рухає жодної чужої, — інакше, прочитавши одне число, гравець читає
    /// весь Пульс; (3) тліюче джерело (активне, але не озброєне) мовчить:
    /// не сповіщає і не б'є, інакше «активне завжди» вилилося б у хибні
    /// тривоги.
    ///
    /// Мутаційна перевірка: звести дві ставки до однієї змінної (наприклад,
    /// <c>StreetPressureSource.InsistencePerDay</c> → <c>ctx.TensionBandIndex</c>)
    /// — <see cref="EachTrack_HasItsOwnInputVariable"/> червоніє. Чутливість самого
    /// охоронця закріплена окремо
    /// (<see cref="Guard_DetectsTwoTracksSharingOneVariable"/>).
    /// </summary>
    public class PressureInvariantTests
    {
        // ---- Проби: один вхід за раз, від базового стану ----

        private static PulseContext Make(bool night, int band = 1, bool patrol = false, int tier = 1,
            bool hungry = false, int crowd = 2, int order = 1)
            => new PulseContext(1, night, tier, band, patrol, hungry, crowd, order);

        private static readonly KeyValuePair<string, Func<bool, PulseContext>>[] Probes =
        {
            P("band",   night => Make(night, band: 4)),
            P("tier",   night => Make(night, tier: 3)),
            P("patrol", night => Make(night, patrol: true)),
            P("hunger", night => Make(night, hungry: true)),
            P("crowd",  night => Make(night, crowd: 4)),
            P("order",  night => Make(night, order: 3)),
        };

        private static KeyValuePair<string, Func<bool, PulseContext>> P(string name, Func<bool, PulseContext> f)
            => new KeyValuePair<string, Func<bool, PulseContext>>(name, f);

        /// <summary>Які проби рухають ставку кожного джерела (у будь-якій фазі доби).</summary>
        private static Dictionary<string, HashSet<string>> Dependencies(IEnumerable<IPressureSource> sources)
        {
            var result = new Dictionary<string, HashSet<string>>();
            foreach (var s in sources)
            {
                var set = new HashSet<string>();
                foreach (var probe in Probes)
                    foreach (bool night in new[] { false, true })
                        if (s.InsistencePerDay(Make(night)) != s.InsistencePerDay(probe.Value(night)))
                            set.Add(probe.Key);
                result[s.Id] = set;
            }
            return result;
        }

        /// <summary>Власні змінні: що рухає ставку цього джерела й жодного іншого.</summary>
        private static Dictionary<string, List<string>> OwnVariables(Dictionary<string, HashSet<string>> deps)
        {
            var result = new Dictionary<string, List<string>>();
            foreach (var pair in deps)
            {
                result[pair.Key] = pair.Value
                    .Where(v => deps.Where(o => o.Key != pair.Key).All(o => !o.Value.Contains(v)))
                    .OrderBy(v => v, StringComparer.Ordinal)
                    .ToList();
            }
            return result;
        }

        [Test]
        public void ActiveTracks_AtLeastMinimum_InEveryState()
        {
            var cfg = new PulseBalance();
            var pulse = new WorldPulse(cfg);
            foreach (var s in DefaultPressureSources.All()) pulse.AddSource(s);

            int states = 0;
            for (int band = 0; band <= 4; band++)
                foreach (bool night in new[] { false, true })
                    foreach (bool patrol in new[] { false, true })
                        foreach (bool hungry in new[] { false, true })
                            for (int crowd = 0; crowd <= 4; crowd++)
                                for (int order = 0; order <= 3; order++)
                                {
                                    int active = pulse.CountActive(Make(night, band, patrol, 1, hungry, crowd, order));
                                    Assert.GreaterOrEqual(active, cfg.MinActiveTracks,
                                        $"Інваріант 2: у стані band={band} night={night} patrol={patrol} hungry={hungry} " +
                                        $"crowd={crowd} order={order} активних лише {active}");
                                    states++;
                                }

            Assert.AreEqual(5 * 2 * 2 * 2 * 5 * 4, states, "Перебір повний, а не вибірковий");
        }

        [Test]
        public void EachTrack_HasItsOwnInputVariable()
        {
            var deps = Dependencies(DefaultPressureSources.All());
            var own = OwnVariables(deps);

            Assert.GreaterOrEqual(deps.Count, new PulseBalance().MinActiveTracks);
            foreach (var pair in own)
                Assert.IsNotEmpty(pair.Value,
                    $"Накопичувач «{pair.Key}» не має власної змінної: усе, що рухає його ставку " +
                    $"({string.Join(", ", deps[pair.Key])}), рухає й чужу — інваріант 2 / AUDIT G11");

            // Задокументована розкладка: хто що читає.
            CollectionAssert.AreEquivalent(new[] { "crowd", "hunger" }, own["street"],
                "Вулиця — людність і голод (не полоса Напруги)");
            CollectionAssert.AreEquivalent(new[] { "order", "patrol" }, own["night"],
                "Ніч — Уклад і патруль");
            CollectionAssert.AreEquivalent(new[] { "band" }, own["crisis"],
                "Криза — полоса Напруги, і тільки вона");
        }

        [Test]
        public void EveryTrackRate_MovesWithPlayerActions()
        {
            // «Ставки змінюються від дій гравця»: кожна читає хоча б одне
            // з того, чим гравець керує (їжа, люди, указ, патруль, а полоса —
            // сума всіх його дій).
            var deps = Dependencies(DefaultPressureSources.All());
            foreach (var pair in deps)
                Assert.IsNotEmpty(pair.Value, $"Ставка «{pair.Key}» не залежить ні від чого");
        }

        /// <summary>
        /// Чутливість охоронця: два накопичувачі, що читають одну полосу, — це
        /// рівно та дірка G11, і охоронець зобов'язаний її бачити.
        /// </summary>
        [Test]
        public void Guard_DetectsTwoTracksSharingOneVariable()
        {
            var sources = new IPressureSource[]
            {
                new BandSource("a", 6, 4),
                new BandSource("b", 10, 5),
                new CrisisPressureSource()
            };

            var own = OwnVariables(Dependencies(sources));

            Assert.IsEmpty(own["a"], "Два джерела з однією змінною — власної змінної в жодного");
            Assert.IsEmpty(own["b"]);
        }

        private sealed class BandSource : IPressureSource
        {
            private readonly int _base, _perBand;

            public BandSource(string id, int baseRate, int perBand)
            {
                Id = id;
                _base = baseRate;
                _perBand = perBand;
            }

            public string Id { get; }
            public WorldEventKind Kind => WorldEventKind.InternalThreat;
            public string DomainTag => "домен";
            public int Threshold => 100;
            public int CooldownDays => 1;
            public bool Announces => true;
            public bool IsActive(PulseContext ctx) => true;
            public int InsistencePerDay(PulseContext ctx) => _base + ctx.TensionBandIndex * _perBand;
        }

        // ---- Тління: активне, але мовчить ----

        [Test]
        public void SmolderingSources_TickButNeverForewarnNorFire_InAQuietCity()
        {
            var cfg = new PulseBalance();
            var pulse = new WorldPulse(cfg);
            foreach (var s in DefaultPressureSources.All()) pulse.AddSource(s);

            // Спокійне місто, лише денні фази: ніч дрімає, криза тліє.
            for (int day = 1; day <= 400; day++)
            {
                var tick = pulse.Advance(Make(false, band: 0));
                pulse.MarkDelivered(tick.Forewarnings, day);

                CollectionAssert.DoesNotContain(tick.FiredSourceIds, "night", $"Доба {day}: ніч не б'є вдень");
                CollectionAssert.DoesNotContain(tick.FiredSourceIds, "crisis", $"Доба {day}: криза не б'є в спокої");
                Assert.IsFalse(tick.Forewarnings.Any(f => f.SourceId == "night" || f.SourceId == "crisis"),
                    $"Доба {day}: тліюче джерело не сповіщає");
            }

            Assert.Greater(pulse.Tracks["night"].Charge, 0, "Ніч вдень розвідує: заряд тікає");
            Assert.Greater(pulse.Tracks["crisis"].Charge, 0, "Криза в спокої тліє: заряд тікає");
            Assert.Less(pulse.Tracks["night"].Fill, cfg.Forewarn1At, "Тління не доходить до першого ступеня");
            Assert.Less(pulse.Tracks["crisis"].Fill, cfg.Forewarn1At, "Тління не доходить до першого ступеня");
        }

        [Test]
        public void SmolderCeilings_LieBelowTheFirstForewarnLevel()
        {
            var cfg = new PulseBalance();
            foreach (var s in DefaultPressureSources.All().OfType<ISmolderingSource>())
                Assert.Less(s.SmolderCeiling, cfg.Forewarn1At,
                    $"Стеля тління «{s.Id}» сама стала б передвісником");
        }

        [Test]
        public void Crisis_ChargeBuiltAtHeat_IsKeptButSilent_WhenTheCityCoolsDown()
        {
            var cfg = new PulseBalance();
            var pulse = new WorldPulse(cfg);
            pulse.AddSource(new CrisisPressureSource());

            // Розпал: заряд росте і доходить до передвісників.
            int day = 0;
            while (pulse.Tracks["crisis"].Fill < cfg.Forewarn1At && day < 200)
                pulse.Advance(Make(false, band: 3 + day++ % 2));
            int charge = pulse.Tracks["crisis"].Charge;
            Assert.GreaterOrEqual(pulse.Tracks["crisis"].Fill, cfg.Forewarn1At);

            // Місто охололо: заряд не зникає, але голосу немає — передвісник
            // «не бреше», кризи в спокої бути не може.
            for (int i = 0; i < 30; i++)
            {
                var tick = pulse.Advance(Make(false, band: 1));
                Assert.IsEmpty(tick.Forewarnings, "Охололе місто не отримує передвісників кризи");
                Assert.IsEmpty(tick.FiredSourceIds);
            }
            Assert.AreEqual(charge, pulse.Tracks["crisis"].Charge, "Тління нічого не додає понад накопичене");
        }
    }
}
