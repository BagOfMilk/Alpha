using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Characters;
using Game.Core.Characters.Perks;
using Game.Core.Characters.Scars;
using Game.Core.Characters.Traits;
using Game.Core.Stats;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Трейты в слотах (US-2.4). Главное здесь — что переполнение не теряет
    /// трейт молча и не бросает исключение: по дизайну это развилка для игрока.
    /// </summary>
    public class TraitSlotsTests
    {
        private static TraitDefinition Trait(string id, TraitPolarity p = TraitPolarity.Neutral)
            => new TraitDefinition(id, id, p);

        [Test]
        public void AddingUpToCapacity_Works()
        {
            var slots = new TraitSlots(2);
            Assert.AreEqual(TraitAddResult.Added, slots.TryAdd(Trait("brave")));
            Assert.AreEqual(TraitAddResult.Added, slots.TryAdd(Trait("greedy", TraitPolarity.Vice)));
            Assert.AreEqual(2, slots.Active.Count);
            Assert.IsTrue(slots.IsFull);
        }

        [Test]
        public void BeyondCapacity_ReportsSlotsFull_AndKeepsExisting()
        {
            var slots = new TraitSlots(1);
            slots.TryAdd(Trait("brave"));

            Assert.AreEqual(TraitAddResult.SlotsFull, slots.TryAdd(Trait("greedy")));
            Assert.AreEqual(1, slots.Active.Count, "старый трейт остаётся на месте");
            Assert.IsTrue(slots.Has("brave"));
            Assert.IsFalse(slots.Has("greedy"));
        }

        /// <summary>Число слотов приходит из баланса, а не зашито в код.</summary>
        [Test]
        public void Capacity_ComesFromBalance()
        {
            var cfg = new BalanceConfig { TraitSlots = 3 };
            Assert.AreEqual(3, TraitSlots.FromConfig(cfg).Capacity);
            Assert.AreEqual(0, TraitSlots.FromConfig(null).Capacity);
        }

        [Test]
        public void Duplicate_IsRejected()
        {
            var slots = new TraitSlots(4);
            slots.TryAdd(Trait("brave"));
            Assert.AreEqual(TraitAddResult.AlreadyPresent, slots.TryAdd(Trait("brave")));
        }

        [Test]
        public void Replace_SwapsOneForAnother()
        {
            var slots = new TraitSlots(1);
            slots.TryAdd(Trait("brave"));

            Assert.IsTrue(slots.Replace("brave", Trait("greedy")));
            Assert.IsFalse(slots.Has("brave"));
            Assert.IsTrue(slots.Has("greedy"));
            Assert.AreEqual(1, slots.Active.Count);
        }

        [Test]
        public void Replace_WithDuplicate_LeavesSlotsUntouched()
        {
            var slots = new TraitSlots(2);
            slots.TryAdd(Trait("brave"));
            slots.TryAdd(Trait("greedy"));

            Assert.IsFalse(slots.Replace("brave", Trait("greedy")), "такой трейт уже есть");
            Assert.IsTrue(slots.Has("brave"), "обмен атомарный: исходящий не пропал");
        }

        /// <summary>Трейт меняет числа через общий агрегатор, а не своей веткой в формуле.</summary>
        [Test]
        public void TraitModifiers_ReachTheSnapshot()
        {
            var slots = new TraitSlots(4);
            slots.TryAdd(Trait("sharpshooter").WithModifier(StatKey.Accuracy, 5));

            var snap = StatResolver.Resolve(new AttributeSet(4, 4, 4, 4), new SkillSet(),
                new IModifierProvider[] { slots }, new BalanceConfig());

            Assert.AreEqual(4 + 5, snap.Get(StatKey.Accuracy), 1e-9, "Ловкость 4 x 1.0 плюс трейт 5");
        }
    }

    /// <summary>Вечный трек шрамов (US-2.5, US-4.2).</summary>
    public class ScarTrackTests
    {
        private static ScarDefinition OneEyed()
            => new ScarDefinition("one_eyed", "Одноглазый").WithModifier(StatKey.Accuracy, -10);

        [Test]
        public void Scar_ModifiesStatsThroughAggregator()
        {
            var track = new ScarTrack();
            track.Add(OneEyed());

            var snap = StatResolver.Resolve(new AttributeSet(4, 4, 4, 4), new SkillSet(),
                new IModifierProvider[] { track }, new BalanceConfig());

            Assert.AreEqual(4 - 10, snap.Get(StatKey.Accuracy), 1e-9);
        }

        [Test]
        public void SameScarTwice_IsIgnored()
        {
            var track = new ScarTrack();
            Assert.IsTrue(track.Add(OneEyed()));
            Assert.IsFalse(track.Add(OneEyed()));
            Assert.AreEqual(1, track.Count);
        }

        /// <summary>Лёгкая рана следов не оставляет — прямое правило US-4.2.</summary>
        [Test]
        public void LightWound_EarnsNoScar()
        {
            var scar = OneEyed();
            Assert.IsFalse(ScarTrack.IsEarnedBy(scar, WoundTier.Light));
            Assert.IsFalse(ScarTrack.IsEarnedBy(scar, WoundTier.None));
        }

        [Test]
        public void SeriousAndAbove_EarnScar()
        {
            var scar = OneEyed();
            Assert.IsTrue(ScarTrack.IsEarnedBy(scar, WoundTier.Serious));
            Assert.IsTrue(ScarTrack.IsEarnedBy(scar, WoundTier.Critical));
        }

        /// <summary>
        /// Снять шрам нечем — у трека нет метода удаления. Тест фиксирует это
        /// как контракт: если кто-то добавит Remove, правило US-2.5 сломается молча.
        /// </summary>
        [Test]
        public void ScarTrack_HasNoRemovalApi()
        {
            var methods = typeof(ScarTrack).GetMethods();
            foreach (var m in methods)
                Assert.IsFalse(m.Name == "Remove" || m.Name == "Clear" || m.Name == "RemoveAt",
                    $"шрамы несбрасываемы, а у трека появился {m.Name}");
        }
    }

    /// <summary>Перки: гейт по уровню скила и пререквизиты (US-2.2, US-3.10).</summary>
    public class PerkGatingTests
    {
        private static SkillSet Skills(SkillType s, int level)
        {
            var set = new SkillSet();
            set[s] = level;
            return set;
        }

        private static PerkDefinition Steady()
            => new PerkDefinition("steady", "Твёрдая рука", SkillType.Ranged, 4)
                .WithModifier(StatKey.Accuracy, 5);

        [Test]
        public void BelowThreshold_IsRefused()
        {
            var perks = new CompanionPerks();
            Assert.AreEqual(PerkAvailability.SkillTooLow, perks.TryTake(Steady(), Skills(SkillType.Ranged, 3)));
            Assert.AreEqual(0, perks.Count);
        }

        [Test]
        public void AtThreshold_IsTaken()
        {
            var perks = new CompanionPerks();
            Assert.AreEqual(PerkAvailability.Available, perks.TryTake(Steady(), Skills(SkillType.Ranged, 4)));
            Assert.IsTrue(perks.Has("steady"));
        }

        [Test]
        public void Prerequisite_IsEnforced()
        {
            var perks = new CompanionPerks();
            var advanced = new PerkDefinition("deadeye", "Меткий глаз", SkillType.Ranged, 4).Requiring("steady");
            var skills = Skills(SkillType.Ranged, 6);

            Assert.AreEqual(PerkAvailability.MissingPrerequisite, perks.TryTake(advanced, skills));
            perks.TryTake(Steady(), skills);
            Assert.AreEqual(PerkAvailability.Available, perks.TryTake(advanced, skills));
        }

        [Test]
        public void TakingTwice_IsRefused()
        {
            var perks = new CompanionPerks();
            var skills = Skills(SkillType.Ranged, 4);
            perks.TryTake(Steady(), skills);
            Assert.AreEqual(PerkAvailability.AlreadyTaken, perks.TryTake(Steady(), skills));
        }

        /// <summary>Предпросмотр не должен ничего менять — на нём стоит планировщик билда.</summary>
        [Test]
        public void Evaluate_HasNoSideEffects()
        {
            var perks = new CompanionPerks();
            var skills = Skills(SkillType.Ranged, 6);

            perks.Evaluate(Steady(), skills);
            perks.Evaluate(Steady(), skills);
            Assert.AreEqual(0, perks.Count);
        }

        /// <summary>Перк поднимает живучесть поверх производной — это требование US-5.2.</summary>
        [Test]
        public void Perk_RaisesMaxHpOnTopOfDerived()
        {
            var perks = new CompanionPerks();
            var tough = new PerkDefinition("tough", "Двужильный", SkillType.Survival, 3)
                .WithModifier(StatKey.MaxHp, 3);
            perks.TryTake(tough, Skills(SkillType.Survival, 3));

            var snap = StatResolver.Resolve(new AttributeSet(4, 4, 4, 4), new SkillSet(),
                new IModifierProvider[] { perks }, new BalanceConfig());

            Assert.AreEqual(6 + 4 + 3, snap.Derived(DerivedStat.MaxHp), 1e-9);
        }

        [Test]
        public void CompanionPerks_HasNoForgetApi()
        {
            foreach (var m in typeof(CompanionPerks).GetMethods())
                Assert.IsFalse(m.Name == "Remove" || m.Name == "Forget" || m.Name == "Reset",
                    $"респека нет, а у перков появился {m.Name}");
        }
    }

    /// <summary>Три провайдера на одном персонаже складываются без двойного счёта.</summary>
    public class ProviderStackTests
    {
        [Test]
        public void TraitScarAndPerk_StackIntoOneNumber()
        {
            var traits = new TraitSlots(4);
            traits.TryAdd(new TraitDefinition("sharp", "Острый глаз").WithModifier(StatKey.Accuracy, 5));

            var scars = new ScarTrack();
            scars.Add(new ScarDefinition("one_eyed", "Одноглазый").WithModifier(StatKey.Accuracy, -10));

            var perks = new CompanionPerks();
            var set = new SkillSet();
            set[SkillType.Ranged] = 4;
            perks.TryTake(new PerkDefinition("steady", "Твёрдая рука", SkillType.Ranged, 4)
                .WithModifier(StatKey.Accuracy, 2), set);

            var snap = StatResolver.Resolve(new AttributeSet(4, 4, 4, 4), set,
                new IModifierProvider[] { traits, scars, perks }, new BalanceConfig());

            // Ловкость 4 + трейт 5 − шрам 10 + перк 2
            Assert.AreEqual(1, snap.Get(StatKey.Accuracy), 1e-9);
            Assert.AreEqual(3, snap.SourcesOf(StatKey.Accuracy).Count, "каждый источник виден отдельно");
        }
    }
}
