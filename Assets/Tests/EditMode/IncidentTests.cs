using System.Collections.Generic;
using System.Linq;
using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Checks;
using Game.Core.Pressure;
using Game.Core.Settlement;
using Game.Core.Stats;
using Game.Core.World;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Инциденты и кризис. Здесь защищается тезис этапа: кризис бьёт больно,
    /// но честно — и никогда не загоняет игру в тупик.
    /// </summary>
    public class IncidentTests
    {
        private static BalanceConfig Cfg() => new BalanceConfig();

        private static Companion MakeCompanion(string id, int charisma = 5)
        {
            var arch = new CompanionArchetype(id, id);
            arch.BaseStats.Set(StatType.Charisma, charisma);
            arch.BaseStats.Set(StatType.Will, charisma);
            return arch.CreateInstance(id);
        }

        private static (Roster roster, RosterAdapter adapter) MakeRoster(string protagonistId, params string[] ids)
        {
            var roster = new Roster();
            foreach (var id in ids) roster.Add(MakeCompanion(id));
            return (roster, new RosterAdapter(roster, protagonistId));
        }

        // ---- Поправка №1: тихий путь есть везде ----

        [Test]
        public void Content_EveryIncident_HasNonViolentPath()
        {
            foreach (var incident in DefaultIncidents.All())
                Assert.IsTrue(incident.HasQuietPath,
                    $"Инцидент «{incident.Id}» без тихого пути нарушает столп ненасилия");
        }

        [Test]
        public void Content_EveryIncident_HasTensionForEveryBand()
        {
            foreach (var incident in DefaultIncidents.All())
                Assert.AreEqual(4, incident.TensionByBand.Length,
                    $"У «{incident.Id}» должна быть дельта на каждую полосу исхода");
        }

        // ---- US-11.3: таблица взвешена полосой и тиром ----

        [Test]
        public void Incidents_SeriousOnesRequireHigherBand()
        {
            var table = DefaultIncidents.BuildTable();

            var calm = table.Eligible(TensionBand.Murmur, tier: 3, isNight: false, crisisOnly: false);
            var hot = table.Eligible(TensionBand.Fracture, tier: 3, isNight: false, crisisOnly: false);

            Assert.IsFalse(calm.Any(i => i.Id == "protection_racket"),
                "Организованная преступность не лезет в тихом городе");
            Assert.IsTrue(hot.Any(i => i.Id == "protection_racket"),
                "…но появляется на верхних полосах (US-11.1)");
        }

        [Test]
        public void Incidents_TierGatesContent()
        {
            var table = DefaultIncidents.BuildTable();

            var hamlet = table.Eligible(TensionBand.Fracture, tier: 1, isNight: false, crisisOnly: false);
            Assert.IsFalse(hamlet.Any(i => i.MinTier > 1), "На хуторе не бывает городских проблем");
        }

        [Test]
        public void Incidents_NightOnly_NotPickedInDaytime()
        {
            var table = DefaultIncidents.BuildTable();

            var day = table.Eligible(TensionBand.Fracture, 3, isNight: false, crisisOnly: false);
            Assert.IsFalse(day.Any(i => i.NightOnly));

            var night = table.Eligible(TensionBand.Fracture, 3, isNight: true, crisisOnly: false);
            Assert.IsTrue(night.Any(i => i.NightOnly), "Ночью появляется своё");
        }

        [Test]
        public void Incidents_PickIsDeterministic()
        {
            var table = DefaultIncidents.BuildTable();
            for (int selector = 0; selector < 50; selector++)
            {
                var a = table.Pick(TensionBand.Ferment, 2, false, false, selector);
                var b = table.Pick(TensionBand.Ferment, 2, false, false, selector);
                Assert.AreEqual(a?.Id, b?.Id, "Один и тот же селектор — один и тот же инцидент");
            }
        }

        // ---- Резолв и Напряжение ----

        [Test]
        public void Incident_BadOutcome_RaisesTension_GoodOutcome_Lowers()
        {
            var cfg = Cfg();
            var incident = DefaultIncidents.All().First(i => i.Id == "petty_theft");

            var weak = MakeRoster("hero", "hero", "weakling").adapter;
            var tensionWeak = new TensionState(cfg.Tension, 300);
            tensionWeak.BeginDay();
            IncidentResolver.Resolve(incident, weak, null, null, null, tensionWeak, 1, cfg);

            Assert.Greater(tensionWeak.Value, 300, "Провал разбора накаляет город");

            var strongRoster = new Roster();
            strongRoster.Add(MakeCompanion("ace", charisma: 30));
            var strong = new RosterAdapter(strongRoster, "ace");
            var tensionStrong = new TensionState(cfg.Tension, 300);
            tensionStrong.BeginDay();
            IncidentResolver.Resolve(incident, strong, null, null, null, tensionStrong, 1, cfg);

            Assert.Less(tensionStrong.Value, 300, "Хороший разбор разряжает");
        }

        // ---- Кризис: зубы и ограждения ----

        [Test]
        public void Crisis_KillsCompanion_AndDeathIsIrreversible()
        {
            var cfg = Cfg();
            var crisis = DefaultIncidents.All().First(i => i.IsCrisis);
            var (roster, adapter) = MakeRoster("hero", "hero", "alpha", "beta");

            var outcome = IncidentResolver.Resolve(crisis, adapter, null, adapter,
                new PopulationState(), new TensionState(cfg.Tension, 900), 1, cfg);

            Assert.IsTrue(outcome.WasCrisis);
            Assert.AreEqual(CrisisBite.KillCompanion, outcome.Bite, "Слабый разбор кризиса стоит жизни");

            var victim = roster.Get(outcome.AffectedActorId);
            Assert.IsTrue(victim.IsDead);
            Assert.IsNull(victim.AssignedSlotId, "Мёртвый пост не держит");
        }

        [Test]
        public void Crisis_NeverKillsProtagonist()
        {
            var cfg = Cfg();
            var crisis = DefaultIncidents.All().First(i => i.IsCrisis);
            var (roster, adapter) = MakeRoster("hero", "hero", "alpha", "beta");

            for (int day = 1; day <= 5; day++)
                IncidentResolver.Resolve(crisis, adapter, null, adapter,
                    new PopulationState(), new TensionState(cfg.Tension, 900), day, cfg);

            Assert.IsFalse(roster.Get("hero").IsDead, "Протагонист неприкосновенен (US-4.4)");
        }

        [Test]
        public void Crisis_NeverKillsLastCompanion()
        {
            var cfg = Cfg();
            var crisis = DefaultIncidents.All().First(i => i.IsCrisis);
            var (roster, adapter) = MakeRoster("hero", "hero", "only");

            var outcome = IncidentResolver.Resolve(crisis, adapter, null, adapter,
                new PopulationState(), new TensionState(cfg.Tension, 900), 1, cfg);

            Assert.IsFalse(roster.Get("only").IsDead,
                "Последнего напарника не убиваем — иначе это дорога в софт-лок");
            Assert.AreNotEqual(CrisisBite.KillCompanion, outcome.Bite);
        }

        [Test]
        public void Crisis_WithNobodyToKill_HitsPopulationInstead()
        {
            var cfg = Cfg();
            var crisis = DefaultIncidents.All().First(i => i.IsCrisis);
            var (_, adapter) = MakeRoster("hero", "hero");
            var population = new PopulationState(200);

            var outcome = IncidentResolver.Resolve(crisis, adapter, null, adapter,
                population, new TensionState(cfg.Tension, 900), 1, cfg);

            Assert.AreEqual(CrisisBite.PopulationOutflow, outcome.Bite);
            Assert.Greater(outcome.PopulationLost, 0, "Кризис обязан чем-то ударить");
        }

        [Test]
        public void Crisis_GoodHandling_SoftensTheBlow()
        {
            var cfg = Cfg();
            var crisis = DefaultIncidents.All().First(i => i.IsCrisis);

            var roster = new Roster();
            roster.Add(MakeCompanion("hero", 40));
            roster.Add(MakeCompanion("alpha"));
            roster.Add(MakeCompanion("beta"));
            var adapter = new RosterAdapter(roster, "hero");

            var outcome = IncidentResolver.Resolve(crisis, adapter, null, adapter,
                new PopulationState(), new TensionState(cfg.Tension, 900), 1, cfg);

            Assert.AreEqual(CrisisBite.WoundCompanion, outcome.Bite,
                "Подготовленный город переживает кризис ранением, а не гробом");
            Assert.IsFalse(roster.Get(outcome.AffectedActorId).IsDead);
        }

        [Test]
        public void Crisis_VictimChoiceIsDeterministic()
        {
            var cfg = Cfg();
            var crisis = DefaultIncidents.All().First(i => i.IsCrisis);

            string RunOnce()
            {
                var (_, adapter) = MakeRoster("hero", "hero", "zeta", "alpha", "mid");
                return IncidentResolver.Resolve(crisis, adapter, null, adapter,
                    new PopulationState(), new TensionState(cfg.Tension, 900), 1, cfg).AffectedActorId;
            }

            Assert.AreEqual(RunOnce(), RunOnce(), "Жертва выбирается воспроизводимо, а не как повезёт");
            Assert.AreEqual("alpha", RunOnce(), "При прочих равных — первый по Id");
        }

        [Test]
        public void Death_IsIrreversible()
        {
            var c = MakeCompanion("x");
            var roster = new Roster();
            roster.Add(c);
            var adapter = new RosterAdapter(roster);

            adapter.Kill("x");
            Assert.IsTrue(c.IsDead);

            // Попытка «полечить» мёртвого не должна его воскрешать.
            adapter.Wound("x", 10);
            Assert.IsTrue(c.IsDead, "Из смерти нет пути назад");
            Assert.IsFalse(adapter.KillableActorIds.Contains("x"));
            Assert.IsFalse(adapter.PresentActors.Any(a => a.Id == "x"), "Мёртвый не участвует в проверках");
        }
    }
}
