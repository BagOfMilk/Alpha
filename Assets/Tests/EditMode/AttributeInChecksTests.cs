using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Checks;
using Game.Core.Stats;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// G16 (GDD:98): «число добирается контекстным атрибутом под подход» —
    /// адаптер годами игнорировал вторую половину формулы и считал только
    /// голый скил. Запугать → Воля (тот же атрибут, что и сопротивление
    /// состояниям — AttributeType.Will), Убедить/Торговля → Смекалка.
    /// </summary>
    public class AttributeInChecksTests
    {
        private static Companion Build(int skillValue, SkillType skill,
            int wits = 0, int will = 0, int strength = 0, int agility = 0, string id = "c")
        {
            var arch = new CompanionArchetype(id, id);
            arch.SetSkill(skill, skillValue);
            arch.SetAttribute(AttributeType.Wits, wits);
            arch.SetAttribute(AttributeType.Will, will);
            arch.SetAttribute(AttributeType.Strength, strength);
            arch.SetAttribute(AttributeType.Agility, agility);
            return arch.CreateInstance(id);
        }

        [Test]
        public void Neutral_DoesNotAddAnyAttribute()
        {
            var c = Build(5, SkillType.Survival, wits: 9, will: 9);
            var adapter = new CompanionActorAdapter(c, false, new BalanceConfig());

            Assert.AreEqual(5, adapter.GetCheckValue(SkillKeys.Survival, ApproachForm.Neutral),
                "утилитарные проверки не добирают атрибут — иначе вылазка и доклады с постов сдвинулись бы");
        }

        [Test]
        public void Neutral_IsAlsoTheDefault_WhenApproachOmitted()
        {
            var c = Build(5, SkillType.Survival, wits: 9, will: 9);
            var adapter = new CompanionActorAdapter(c, false, new BalanceConfig());

            Assert.AreEqual(5, adapter.GetCheckValue(SkillKeys.Survival),
                "старые вызывающие (вылазка), не знающие про Approach, не должны увидеть новое число");
        }

        [Test]
        public void Persuade_AddsWits()
        {
            var c = Build(5, SkillType.Persuade, wits: 3);
            var adapter = new CompanionActorAdapter(c, false, new BalanceConfig());

            Assert.AreEqual(8, adapter.GetCheckValue(SkillKeys.Persuade, ApproachForm.Persuade));
        }

        [Test]
        public void Trade_AddsWits()
        {
            var c = Build(4, SkillType.Trade, wits: 6);
            var adapter = new CompanionActorAdapter(c, false, new BalanceConfig());

            Assert.AreEqual(10, adapter.GetCheckValue(SkillKeys.Trade, ApproachForm.Trade));
        }

        [Test]
        public void Intimidate_AddsWill_NotStrength()
        {
            var c = Build(4, SkillType.Intimidate, will: 5, strength: 9);
            var adapter = new CompanionActorAdapter(c, false, new BalanceConfig());

            Assert.AreEqual(9, adapter.GetCheckValue(SkillKeys.Intimidate, ApproachForm.Intimidate),
                "AttributeType.Will уже подписан в коде как «запугивание» — Сила сюда не идёт");
        }

        [Test]
        public void CheckResolver_Preview_PicksActorByAttributeAdjustedValue()
        {
            var cfg = new BalanceConfig();
            var weakerSkillHigherWits = new CompanionActorAdapter(
                Build(4, SkillType.Persuade, wits: 6, id: "weaker"), false, cfg); // 4+6=10
            var strongerSkillNoWits = new CompanionActorAdapter(
                Build(8, SkillType.Persuade, wits: 0, id: "stronger"), false, cfg); // 8+0=8

            var roster = new FakeRoster();
            roster.Actors.Add(weakerSkillHigherWits);
            roster.Actors.Add(strongerSkillNoWits);

            var preview = CheckResolver.Preview(
                new CheckRequest(SkillKeys.Persuade, 5, ApproachForm.Persuade), roster, null, 1, cfg);

            Assert.AreEqual(weakerSkillHigherWits.Id, preview.BestActorId,
                "атрибут учитывается при выборе лучшего кандидата, не только в итоговом числе");
            Assert.AreEqual(10, preview.BestValue);
        }

        private sealed class FakeRoster : IRosterView
        {
            public System.Collections.Generic.List<ISettlementActor> Actors { get; } =
                new System.Collections.Generic.List<ISettlementActor>();
            public System.Collections.Generic.IReadOnlyList<ISettlementActor> PresentActors => Actors;
            public ISettlementActor Protagonist => null;
        }
    }
}
