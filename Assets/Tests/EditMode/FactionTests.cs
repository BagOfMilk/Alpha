using System;
using System.Collections.Generic;
using System.Linq;
using Game.Core.Balance;
using Game.Core.Factions;
using Game.Core.Loop;
using Game.Core.Pressure;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Пакет B5: фракції (R5). Порт архівних FactionTests на нову модель —
    /// там був окремий FactionBand з Reputation/Influence і ThreatSystem
    /// (Епік 10, знятий разом з архівним шаром загроз), тут — internal Value +
    /// public Band тим самим шаблоном, що TensionState (§4.15), і три конкретні
    /// фракції зрізу замість сід-плейсхолдерів прототипу.
    /// </summary>
    public class FactionTests
    {
        // ================= каталог =================

        [Test]
        public void DefaultFactions_All_ReturnsThreeFactionsWithExpectedIds()
        {
            var all = DefaultFactions.All();

            Assert.AreEqual(3, all.Count);
            CollectionAssert.AreEquivalent(
                new[] { DefaultFactions.Community, DefaultFactions.TuharBoyars, DefaultFactions.Horde },
                all.Select(f => f.Id).ToArray());

            Assert.AreEqual("Громада Тухольщини", all.First(f => f.Id == DefaultFactions.Community).DisplayName);
            Assert.AreEqual("Бояри Тугара", all.First(f => f.Id == DefaultFactions.TuharBoyars).DisplayName);
            Assert.AreEqual("Орда (Бурунда)", all.First(f => f.Id == DefaultFactions.Horde).DisplayName);
        }

        [Test]
        public void NewRegistry_RegistersAllThreeFactions_AtNeutralBand()
        {
            var cfg = new BalanceConfig();
            var reg = DefaultFactions.NewRegistry(cfg.Faction);

            foreach (var f in DefaultFactions.All())
            {
                var standing = reg.Get(f.Id);
                Assert.IsNotNull(standing, f.Id + " обязана быть зарегистрирована");
                Assert.AreEqual(FactionStandingBand.Neutral, standing.Band,
                    "Стартовое отношение — середина шкалы, значит Нейтральність");
            }
        }

        // ================= FactionStanding =================

        [Test]
        public void FactionStanding_Apply_ClampsToRange()
        {
            var cfg = new BalanceConfig();
            var standing = new FactionStanding(cfg.Faction, 50);

            standing.Apply(1000, "test");
            Assert.AreEqual(100, standing.Value, "Отношение не выходит за верхнюю границу шкалы");

            standing.Apply(-5000, "test");
            Assert.AreEqual(0, standing.Value, "И за нижнюю тоже");
        }

        [Test]
        public void FactionStanding_BandChange_FiresSignal()
        {
            // Інваріант 4: німого переходу полоси не буває.
            var cfg = new BalanceConfig();
            var standing = new FactionStanding(cfg.Faction, 50); // Neutral

            var seen = new List<(FactionStandingBand From, FactionStandingBand To)>();
            standing.BandChanged += (from, to) => seen.Add((from, to));

            standing.Apply(5, "test"); // 55, все ще Neutral
            Assert.IsEmpty(seen, "Изменение внутри полосы не должно звать сигнал");

            standing.Apply(10, "test"); // 65 -> Awaiting
            Assert.AreEqual(1, seen.Count, "Переход полосы обязан позвать сигнал ровно один раз");
            Assert.AreEqual(FactionStandingBand.Neutral, seen[0].From);
            Assert.AreEqual(FactionStandingBand.Awaiting, seen[0].To);
        }

        [Test]
        public void FactionRegistry_ApplySocialConsequence_UnknownFaction_DoesNothing()
        {
            var cfg = new BalanceConfig();
            var reg = DefaultFactions.NewRegistry(cfg.Faction);

            // Не повинно кидати — невідома фракція просто не рухається ніким.
            reg.ApplySocialConsequence("no_such_faction", 20);
            Assert.IsNull(reg.Get("no_such_faction"));
        }

        [Test]
        public void FactionRegistry_SaveRoundTrip_KeepsStandings()
        {
            var cfg = new BalanceConfig();
            var reg = DefaultFactions.NewRegistry(cfg.Faction);
            reg.ApplySocialConsequence(DefaultFactions.TuharBoyars, -35);
            reg.ApplySocialConsequence(DefaultFactions.Community, 25);

            string blob = reg.CaptureState();

            var fresh = DefaultFactions.NewRegistry(cfg.Faction);
            fresh.RestoreState(blob);

            Assert.AreEqual(reg.Get(DefaultFactions.TuharBoyars).Band, fresh.Get(DefaultFactions.TuharBoyars).Band);
            Assert.AreEqual(reg.Get(DefaultFactions.Community).Band, fresh.Get(DefaultFactions.Community).Band);
            Assert.AreEqual(FactionStandingBand.Hostile, fresh.Get(DefaultFactions.TuharBoyars).Band,
                "-35 от 50 -> 15, что ниже порога Hostile/Wary (20)");
        }

        // ================= SocialConsequence =================

        [Test]
        public void SocialConsequence_AppliesFactionDeltas()
        {
            var cfg = new BalanceConfig();
            var reg = DefaultFactions.NewRegistry(cfg.Faction);

            new SocialConsequence()
                .Faction(DefaultFactions.Community, 10)
                .Faction(DefaultFactions.TuharBoyars, -10)
                .Apply(reg, null);

            Assert.AreEqual(60, reg.Get(DefaultFactions.Community).Value);
            Assert.AreEqual(40, reg.Get(DefaultFactions.TuharBoyars).Value);
        }

        [Test]
        public void SocialConsequence_QueuesTension_OnlyThroughExistingDriver()
        {
            var cfg = new BalanceConfig();
            var tension = new TensionState(cfg.Tension, 300);
            var processor = new DayProcessor(tension, cfg, DayProcessor.DefaultSteps());

            new SocialConsequence().Tension(TensionDriver.QuestChoice, 15).Apply(null, processor);

            var report = processor.Advance(); // тік наступної фази зливає чергу
            var applied = report.TensionChanges.Where(x => x.Driver == TensionDriver.QuestChoice).ToList();

            Assert.IsNotEmpty(applied, "Заявка обязана примениться существующим драйвером на тике");
        }

        [Test]
        public void SocialConsequence_NeverIntroducesNewDriver()
        {
            // Список драйверів, дозволених соціальним наслідкам, — закрита
            // трійка (QuestChoice/ThreatOutcome/CouncilEdict), а не весь enum.
            Assert.Throws<ArgumentException>(() =>
                new SocialConsequence().Tension(TensionDriver.PlaystyleBlood, 10));
            Assert.Throws<ArgumentException>(() =>
                new SocialConsequence().Tension(TensionDriver.Hunger, 10));
            Assert.Throws<ArgumentException>(() =>
                new SocialConsequence().Tension(TensionDriver.CityTierTick, 10));

            Assert.DoesNotThrow(() => new SocialConsequence().Tension(TensionDriver.QuestChoice, 10));
            Assert.DoesNotThrow(() => new SocialConsequence().Tension(TensionDriver.ThreatOutcome, 10));
            Assert.DoesNotThrow(() => new SocialConsequence().Tension(TensionDriver.CouncilEdict, -10));
        }
    }
}
