using Game.Core.Characters;
using Game.Core.Checks;
using Game.Core.Stats;
using Game.Core.Traits;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>Детерминированные проверки (US-2.6): скил ≥ порога, лучший среди присутствующих, соц-подходы.</summary>
    public class CheckResolverTests
    {
        private static Companion WithHacking(string id, int hacking, int wits = 3)
        {
            var c = new Companion(id, new AttributeBlock(3, 3, wits, 3), 4);
            c.Skills.Set(SkillType.Hacking, hacking);
            return c;
        }

        [Test]
        public void Resolve_TakesBestAmongPresent()
        {
            var r = CheckResolver.Resolve(new[] { WithHacking("a", 1), WithHacking("b", 4) }, SkillType.Hacking, 3);
            Assert.IsTrue(r.Success);
            Assert.AreEqual(4, r.Value);
            Assert.AreEqual("b", r.ResolvedById);
        }

        [Test]
        public void Resolve_BelowThreshold_Fails()
        {
            var r = CheckResolver.Resolve(new[] { WithHacking("a", 2) }, SkillType.Hacking, 3);
            Assert.IsFalse(r.Success);
        }

        [Test]
        public void Resolve_TraitModifier_CountsTowardCheck()
        {
            var c = WithHacking("a", 2);
            c.Traits.TryAdd(new Trait("h", "Рукастый").WithCheck(SkillType.Hacking, 1));
            var r = CheckResolver.Resolve(new[] { c }, SkillType.Hacking, 3);
            Assert.IsTrue(r.Success);   // 2 + 1
            Assert.AreEqual(3, r.Value);
        }

        [Test]
        public void Social_Persuade_AddsWits()
        {
            var c = new Companion("c", new AttributeBlock(3, 3, 6, 3), 4);
            c.Skills.Set(SkillType.Persuasion, 3);
            var r = CheckResolver.ResolveSocial(new[] { c }, CheckApproach.Persuade, 8);
            Assert.IsTrue(r.Success);
            Assert.AreEqual(9, r.Value); // 3 + Wits6
        }

        [Test]
        public void Social_Intimidate_UsesBestOfStrengthOrWill()
        {
            var c = new Companion("c", new AttributeBlock(6, 3, 3, 2), 4);
            c.Skills.Set(SkillType.Intimidation, 1);
            var r = CheckResolver.ResolveSocial(new[] { c }, CheckApproach.Intimidate, 7);
            Assert.IsTrue(r.Success);
            Assert.AreEqual(7, r.Value); // 1 + max(Str6, Will2)
        }

        [Test]
        public void Resolve_NoParticipants_FailsGracefully()
        {
            var r = CheckResolver.Resolve(new Companion[0], SkillType.Hacking, 1);
            Assert.IsFalse(r.Success);
            Assert.IsNull(r.ResolvedById);
        }
    }
}
