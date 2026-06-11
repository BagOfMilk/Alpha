using Game.Core.Stats;
using Game.Core.Traits;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>Трейты-слоты с фикс лимитом и вытеснением (US-2.4).</summary>
    public class TraitSetTests
    {
        [Test]
        public void TryAdd_WithinSlots_Adds()
        {
            var set = new TraitSet(2);
            Assert.AreEqual(TraitAddResult.Added, set.TryAdd(new Trait("a", "A")));
            Assert.AreEqual(TraitAddResult.Added, set.TryAdd(new Trait("b", "B")));
            Assert.AreEqual(2, set.UsedSlots);
            Assert.AreEqual(0, set.FreeSlots);
        }

        [Test]
        public void TryAdd_OverLimit_NeedsEviction_AndDoesNotAdd()
        {
            var set = new TraitSet(1);
            set.TryAdd(new Trait("a", "A"));
            Assert.AreEqual(TraitAddResult.NeedsEviction, set.TryAdd(new Trait("b", "B")));
            Assert.AreEqual(1, set.Traits.Count);
            Assert.IsFalse(set.Contains("b"));
        }

        [Test]
        public void AddEvicting_ReplacesChosenTrait()
        {
            var set = new TraitSet(1);
            set.TryAdd(new Trait("a", "A"));
            Assert.IsTrue(set.AddEvicting(new Trait("b", "B"), "a"));
            Assert.IsFalse(set.Contains("a"));
            Assert.IsTrue(set.Contains("b"));
        }

        [Test]
        public void TryAdd_Duplicate_ReportsAlreadyPresent()
        {
            var set = new TraitSet(3);
            set.TryAdd(new Trait("a", "A"));
            Assert.AreEqual(TraitAddResult.AlreadyPresent, set.TryAdd(new Trait("a", "A2")));
        }

        [Test]
        public void CheckModifier_SumsAcrossTraits()
        {
            var set = new TraitSet(3);
            set.TryAdd(new Trait("a", "A").WithCheck(SkillType.Persuasion, 1));
            set.TryAdd(new Trait("b", "B").WithCheck(SkillType.Persuasion, 2));
            Assert.AreEqual(3, set.CheckModifierFor(SkillType.Persuasion));
        }
    }
}
