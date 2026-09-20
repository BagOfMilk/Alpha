using System.Collections.Generic;
using System.Linq;
using Game.Core;
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
    /// Присутствие как манёвр и целостность каталога позиций.
    ///
    /// До этого одна строка в резолвере — «протагонист кандидат всегда» — делала
    /// недостижимыми сразу три механики Поправки №3.8: Худшую полосу за пустой
    /// пост, молчание доклада и «слепоту» города в домене. Лидер выигрывал каждую
    /// проверку на каждой позиции, и расстановка переставала быть решением.
    /// Здесь эти три механики защищены.
    /// </summary>
    public class PresenceAndCatalogTests
    {
        private static BalanceConfig Cfg() => new BalanceConfig();

        private static Companion Make(string id, int skill, string position = null)
        {
            var arch = new CompanionArchetype(id, id);
            arch.SetSkill(SkillType.Survival, skill);
            arch.SetSkill(SkillType.Trade, skill);
            arch.SetSkill(SkillType.Persuade, skill);
            var c = arch.CreateInstance(id);
            c.AssignedSlotId = position;
            return c;
        }

        // ================= Присутствие =================

        [Test]
        public void Presence_Protagonist_IsCandidateOnlyWhereHeStands()
        {
            var cfg = Cfg();
            var roster = new Roster();
            roster.Add(Make("leader", 40, "council_seat"));
            var adapter = new RosterAdapter(roster, "leader");

            var here = new CheckRequest(SkillKeys.Persuade, 5, ApproachForm.Neutral, "t", "council_seat");
            var elsewhere = new CheckRequest(SkillKeys.Persuade, 5, ApproachForm.Neutral, "t", "storehouse_dock");

            Assert.IsTrue(CheckResolver.Preview(here, adapter, null, 1, cfg).HasCandidate,
                "Там, где лидер стоит, он обязан быть кандидатом");
            Assert.IsFalse(CheckResolver.Preview(elsewhere, adapter, null, 1, cfg).HasCandidate,
                "Вездесущий лидер обесценивает расстановку: он выигрывал бы каждую проверку на каждой позиции");
        }

        [Test]
        public void Presence_UnmannedPosition_YieldsWorstBand()
        {
            var cfg = Cfg();
            var incident = DefaultIncidents.All().First(i => !i.IsCrisis);

            var roster = new Roster();
            roster.Add(Make("idle", 40)); // силён, но не стоит нигде
            var adapter = new RosterAdapter(roster, "idle");

            var tension = new TensionState(cfg.Tension, 300);
            tension.BeginDay();
            var outcome = IncidentResolver.Resolve(incident, adapter, null, null, null, tension, 1, cfg);

            Assert.IsTrue(outcome.WasUnmanned,
                "Пустая позиция — это и есть цена, которую Поправка №3.8 назначает за отсутствие людей");
            Assert.AreEqual(OutcomeBand.Worst, outcome.Band,
                "Никого на посту — разбирать некому, и исход обязан быть худшим");
        }

        [Test]
        public void Presence_UnmannedPost_ReportsNothingAtAll()
        {
            var cfg = Cfg();
            var roster = new Roster();
            roster.Add(Make("idle", 40)); // не на посту
            var adapter = new RosterAdapter(roster, "idle");

            var p = new DayProcessor(new TensionState(cfg.Tension), cfg, DayProcessor.DefaultSteps())
            {
                Tier = 1,
                Roster = adapter,
                Population = new PopulationState(),
                PostDomains = new[] { new PostDomain("storehouse_dock", "склад", SkillKeys.Survival, 5) }
            };

            var report = p.Advance(DayPhase.Day);

            Assert.IsFalse(report.Signals.Requests.Any(r => r.Channel == SignalChannel.PostReport),
                "Никого на позиции — город в этом домене слеп, и молчание и есть сигнал");
        }

        // ================= Каталог позиций =================

        [Test]
        public void Catalog_EveryIncidentPosition_ExistsInSlotCatalog()
        {
            var known = new HashSet<string>(DefaultContent.AllSlots().Select(s => s.Id));

            var orphans = DefaultIncidents.All()
                .Select(i => i.RelevantPositionId)
                .Where(id => !string.IsNullOrEmpty(id) && !known.Contains(id))
                .Distinct()
                .ToList();

            Assert.IsEmpty(orphans,
                "Инцидент адресует позицию, которой нет в каталоге слотов: " + string.Join(", ", orphans) +
                ". Такую позицию невозможно занять, поэтому разбор навсегда остаётся без кандидата");
        }

        [Test]
        public void Catalog_NoCitySlot_ProducesMaterials()
        {
            // Правило Эпика 15 и Поправки №5: оба компонента приходят ТОЛЬКО извне.
            // Слот, производящий материалы внутри города, отменяет экономическую
            // подставу вылазки — и именно так это и прожило в контенте первой
            // итерации, пока никто не проверял.
            var offenders = DefaultContent.AllSlots()
                .Where(s => s.OutputKind == SlotOutputKind.Resource
                         && s.OutputResource == Game.Core.Economy.ResourceType.Materials)
                .Select(s => s.Id)
                .ToList();

            Assert.IsEmpty(offenders,
                "Город не производит материалы — они только снаружи: " + string.Join(", ", offenders));
        }

        [Test]
        public void Catalog_EveryIncident_NamesItsPosition()
        {
            var nameless = DefaultIncidents.All()
                .Where(i => string.IsNullOrEmpty(i.RelevantPositionId))
                .Select(i => i.Id)
                .ToList();

            Assert.IsEmpty(nameless,
                "Инцидент без позиции берёт лучшего из всех присутствующих — то есть расстановка на него не влияет: "
                + string.Join(", ", nameless));
        }
    }
}
