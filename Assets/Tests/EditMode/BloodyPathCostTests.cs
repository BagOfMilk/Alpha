using System.Collections.Generic;
using System.Linq;
using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Checks;
using Game.Core.Loop;
using Game.Core.Pressure;
using Game.Core.Settlement;
using Game.Core.Signals;
using Game.Core.Stats;
using Game.Core.World;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Ціна кривавого шляху.
    ///
    /// Точка рішення оживила кривавий шлях, але оживила його БЕЗКОШТОВНИМ: він давав
    /// той самий підсумок тими самими навичками і не коштував нічого. Тихий шлях за
    /// Поправкою №1 зобов'язаний бути повільним і дорогим, а кривавий — швидким і
    /// ДУЖЕ важким; на ділі виходило навпаки, і перший же гравець зробив би висновок
    /// «гра про різанину». Тут ця інверсія закрита трьома цінами разом: Напруга за
    /// власним драйвером, рана виконавцю, страх громади на кілька діб.
    /// </summary>
    public class BloodyPathCostTests
    {
        private const string Post = "storehouse_dock";

        private static IncidentDefinition Theft()
        {
            // Інцидент з обома шляхами: тихий — Переконання (соціальний підхід,
            // по якому й б'є страх), кривавий — Залякування.
            return DefaultIncidents.All().First(i => i.Id == "petty_theft");
        }

        // Навичка 6 підібрана так, щоб ОБИДВА шляхи давали Базову полосу: тихий
        // вимагає 5, кривавий 4. Інакше порівняння ціни було б нечесним —
        // кривавий шлях вигравав би полосою, а не ціною.
        private static Roster BuildRoster(int skill = 6)
        {
            var roster = new Roster();
            // Тихий шлях крадіжки йде Переконанням, кривавий — Залякуванням:
            // обидва скіли рівні, щоб ціна порівнювалась при одній полосі підсумку.
            var arch = new CompanionArchetype("guard", "guard")
                .SetSkill(SkillType.Persuade, skill)
                .SetSkill(SkillType.Intimidate, skill)
                .SetSkill(SkillType.Survival, skill);
            var c = arch.CreateInstance("guard");
            c.AssignedSlotId = Post;
            roster.Add(c);
            return roster;
        }

        private static TensionState FreshTension(BalanceConfig cfg)
        {
            var tension = new TensionState(cfg.Tension, 300);
            tension.BeginDay();
            return tension;
        }

        private static IncidentOutcome Resolve(IncidentPath path, BalanceConfig cfg,
            Roster roster, TensionState tension, FearState fear = null, int day = 1)
        {
            var adapter = new RosterAdapter(roster);
            return IncidentResolver.Resolve(Theft(), adapter, null, adapter, null,
                tension, day, cfg, path, fear);
        }

        // ================= Напруга =================

        [Test]
        public void Blood_RaisesTension_ByItsOwnDriver()
        {
            var cfg = new BalanceConfig();

            var quietTension = FreshTension(cfg);
            Resolve(IncidentPath.Quiet, cfg, BuildRoster(), quietTension);

            var bloodyTension = FreshTension(cfg);
            Resolve(IncidentPath.Bloody, cfg, BuildRoster(), bloodyTension);

            Assert.IsFalse(quietTension.DayLedger.Any(c => c.Driver == TensionDriver.PlaystyleBlood),
                "Тихий путь не имеет права писать стиль прохождения");

            var blood = bloodyTension.DayLedger.Where(c => c.Driver == TensionDriver.PlaystyleBlood).ToList();
            Assert.AreEqual(1, blood.Count,
                "Кровавый разбор обязан отметиться ровно одной записью драйвера PlaystyleBlood");
            Assert.AreEqual(cfg.Tension.BloodDeltaPerNode, blood[0].Requested,
                "Вес крови берётся из баланса, а не из кода");
            Assert.IsFalse(blood[0].Rejected,
                "PlaystyleBlood стоит в белом списке повышающих — отклонять его нечем");
        }

        [Test]
        public void Blood_IsNotStrictlyBetterThanQuiet()
        {
            // Головний тест цього файлу: за ОДНАКОВОГО підсумку кров зобов'язана
            // обійтись дорожче. Інакше вибір шляху — не вибір, а пастка для тих,
            // хто грає мирно.
            var cfg = new BalanceConfig();

            var quietTension = FreshTension(cfg);
            var quietRoster = BuildRoster();
            var quiet = Resolve(IncidentPath.Quiet, cfg, quietRoster, quietTension);

            var bloodyTension = FreshTension(cfg);
            var bloodyRoster = BuildRoster();
            var bloody = Resolve(IncidentPath.Bloody, cfg, bloodyRoster, bloodyTension);

            Assert.AreEqual(quiet.Band, bloody.Band,
                "Предпосылка теста: ростер подобран так, чтобы оба пути давали одну полосу");

            int quietSum = quietTension.DayLedger.Sum(c => c.Applied);
            int bloodySum = bloodyTension.DayLedger.Sum(c => c.Applied);

            Assert.Greater(bloodySum, quietSum,
                "При том же исходе кровь обязана стоить дороже по Напряжению");
            Assert.Greater(bloodyRoster.Get("guard").InjuryPoints, 0.0,
                "И оставить рану: «быстро и очень тяжело» — вторая половина Поправки №1");
            Assert.AreEqual(0.0, quietRoster.Get("guard").InjuryPoints,
                "Тихий путь ран не оставляет");
        }

        // ================= Рана виконавцю =================

        [Test]
        public void Blood_WoundsTheOneWhoWentIn()
        {
            var cfg = new BalanceConfig();
            var roster = BuildRoster();

            var outcome = Resolve(IncidentPath.Bloody, cfg, roster, FreshTension(cfg));

            Assert.IsFalse(outcome.WasUnmanned, "Предпосылка: на посту кто-то стоял");
            Assert.AreEqual(cfg.Checks.BloodyPathInjury, roster.Get("guard").InjuryPoints,
                "Рану получает тот, кто ходил в дело, и ровно столько, сколько назначил баланс");
        }

        [Test]
        public void Blood_OnEmptyPost_WoundsNobody()
        {
            var cfg = new BalanceConfig();

            var roster = new Roster();
            var arch = new CompanionArchetype("idle", "idle").SetSkill(SkillType.Intimidate, 12);
            roster.Add(arch.CreateInstance("idle")); // сильний, але не на посту

            var outcome = Resolve(IncidentPath.Bloody, cfg, roster, FreshTension(cfg));

            Assert.IsTrue(outcome.WasUnmanned, "Никого на позиции — разбирать некому");
            Assert.AreEqual(0.0, roster.Get("idle").InjuryPoints,
                "Ранить некого: за пустой пост уже назначена Худшая полоса, второй раз не наказываем");
        }

        // ================= Страх громади =================

        [Test]
        public void Fear_AfterBlood_MakesTalkingMoreExpensive()
        {
            var cfg = new BalanceConfig();
            var fear = new FearState();

            int before = IncidentResolver.BuildRequest(Theft(), IncidentPath.Quiet, fear, 1, cfg).Threshold;

            Resolve(IncidentPath.Bloody, cfg, BuildRoster(), FreshTension(cfg), fear, day: 1);

            int during = IncidentResolver.BuildRequest(Theft(), IncidentPath.Quiet, fear, 2, cfg).Threshold;
            int after = IncidentResolver.BuildRequest(Theft(), IncidentPath.Quiet, fear,
                1 + cfg.Checks.FearDurationDays + 1, cfg).Threshold;

            Assert.AreEqual(before + cfg.Checks.FearPenaltyStep, during,
                "Пока община помнит кровь, договариваться дороже");
            Assert.AreEqual(before, after,
                "Страх обязан проходить: вечный штраф — это не цена, а наказание");
        }

        [Test]
        public void Fear_DoesNotDiscountIntimidation()
        {
            // Якби страх здешевлював залякування, кривавий шлях окупав би сам
            // себе, і інверсія повернулася б з іншого боку.
            var cfg = new BalanceConfig();
            var racket = DefaultIncidents.All().First(i => i.Id == "protection_racket");
            Assert.AreEqual(ApproachForm.Intimidate, racket.QuietPathApproach, "Предпосылка теста");

            var fear = new FearState();
            int calm = IncidentResolver.BuildRequest(racket, IncidentPath.Quiet, fear, 1, cfg).Threshold;

            fear.Remember(1, cfg.Checks);
            int scared = IncidentResolver.BuildRequest(racket, IncidentPath.Quiet, fear, 2, cfg).Threshold;

            Assert.AreEqual(calm, scared, "Страх общины не делает запугивание дешевле");
        }

        [Test]
        public void Fear_FailedIntimidation_CountsToo()
        {
            // Друге джерело страху — провалене залякування. Воно існувало
            // в CheckOutcome з Е1 і не читалося ніким.
            var cfg = new BalanceConfig();
            var racket = DefaultIncidents.All().First(i => i.Id == "protection_racket");

            var roster = new Roster();
            // Поріг залякування 8 недосяжний при скілі 1 → Найгірша полоса.
            var arch = new CompanionArchetype("weak", "weak").SetSkill(SkillType.Intimidate, 1);
            var c = arch.CreateInstance("weak");
            c.AssignedSlotId = racket.RelevantPositionId;
            roster.Add(c);

            var adapter = new RosterAdapter(roster);
            var fear = new FearState();
            var outcome = IncidentResolver.Resolve(racket, adapter, null, adapter, null,
                FreshTension(cfg), 1, cfg, IncidentPath.Quiet, fear);

            Assert.AreEqual(OutcomeBand.Worst, outcome.Band, "Предпосылка: запугивание провалено");
            Assert.IsTrue(outcome.CausedFear, "Провал запугивания обязан пугать общину");
            Assert.IsTrue(fear.IsAfraid(2), "И этот страх обязан дожить до завтра");
        }

        [Test]
        public void Fear_IsNeverSilent()
        {
            // Інваріант 6: у нової шкали зобов'язаний бути сигнал. Прихована ціна,
            // про яку гравець не може дізнатися, — це не механіка, а підлість.
            var cfg = new BalanceConfig();
            var scared = new IncidentOutcome("petty_theft", "incident.petty_theft", "склад",
                OutcomeBand.Base, false, false, null, null, 0, causedFear: true);

            var digest = SignalComposer.Compose(TensionBand.Murmur, new List<TensionChange>(), 1,
                cfg.Signals, null, new[] { scared }, null, false);

            Assert.IsTrue(digest.Requests.Any(r => r.Tags.Contains("fear")),
                "Страх общины обязан прозвучать в тот же день, а не молча поднять порог");
        }

        [Test]
        public void Fear_SurvivesSaveAndLoad()
        {
            var cfg = new BalanceConfig();
            var p = BuildProcessor(cfg);
            p.Fear.RestoreForSave(42);

            string blob = p.SaveState();

            var restored = BuildProcessor(cfg);
            restored.RestoreState(blob);

            Assert.AreEqual(42, restored.Fear.UntilDay,
                "Иначе загрузка — бесплатный способ снять цену кровавого пути");
            Assert.IsTrue(restored.Fear.IsAfraid(42));
            Assert.IsFalse(restored.Fear.IsAfraid(43));
        }

        [Test]
        public void Fear_ShownThreshold_EqualsApplied()
        {
            // Інваріант 8 під страхом: поріг, який гравець БАЧИТЬ у пропозиції,
            // зобов'язаний бути тим самим, що застосується при розборі.
            var cfg = new BalanceConfig();
            var p = BuildProcessor(cfg, askPlayer: true);
            p.Fear.RestoreForSave(9999); // громада боїться весь час прогону

            var report = RunUntilAsked(p);
            Assert.IsNotNull(report, "За отведённые сутки конвейер не остановился");

            var quiet = report.Pending.Options.First(o => o.Path == IncidentPath.Quiet);
            var incident = DefaultIncidents.All().First(i => i.Id == report.Pending.IncidentId);

            int expected = IncidentResolver
                .BuildRequest(incident, IncidentPath.Quiet, p.Fear, report.Day, cfg).Threshold;

            Assert.AreEqual(expected, quiet.Threshold,
                "Показанный порог обязан включать надбавку за вчерашнюю кровь");
        }

        // ================= фікстура =================

        private static readonly string[] AllPositions =
        {
            "storehouse_dock", "settlement_market", "settlement_farms",
            "infirmary_bed", "council_seat", "scouting_post", "workshop_bench"
        };

        private static DayProcessor BuildProcessor(BalanceConfig cfg, bool askPlayer = false)
        {
            var roster = new Roster();
            for (int i = 0; i < AllPositions.Length; i++)
            {
                var arch = new CompanionArchetype("actor" + i, "actor" + i);
                foreach (var skill in Skills.All) arch.SetSkill(skill, 7);
                var c = arch.CreateInstance("actor" + i);
                c.AssignedSlotId = AllPositions[i];
                roster.Add(c);
            }

            var adapter = new RosterAdapter(roster);
            var pulse = new WorldPulse(cfg.Pulse);
            foreach (var s in DefaultPressureSources.All()) pulse.AddSource(s);

            return new DayProcessor(new TensionState(cfg.Tension, 300), cfg, DayProcessor.DefaultSteps())
            {
                Tier = 2,
                Roster = adapter,
                Casualties = adapter,
                Population = new PopulationState(),
                Pulse = pulse,
                Incidents = DefaultIncidents.BuildTable(),
                Repeats = new RepeatTracker(),
                RequirePlayerDecision = askPlayer
            };
        }

        private static DayReport RunUntilAsked(DayProcessor p, int maxDays = 60)
        {
            for (int day = 1; day <= maxDays; day++)
                foreach (var phase in new[] { DayPhase.Day, DayPhase.Night })
                {
                    var report = p.Advance(phase);
                    if (report.AwaitsDecision) return report;
                }
            return null;
        }
    }
}
