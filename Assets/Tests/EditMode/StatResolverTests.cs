using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Stats;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Єдиний агрегатор модифікаторів (US-18.2). Головне, що тут перевіряється —
    /// що подвійний рахунок неможливий і що порядок вставки на результат не впливає:
    /// без цього «один ефект — одна система» лишається гаслом.
    /// </summary>
    public class StatResolverTests
    {
        private sealed class Provider : IModifierProvider
        {
            private readonly StatModifier[] _mods;
            public Provider(params StatModifier[] mods) => _mods = mods;
            public void CollectModifiers(List<StatModifier> into) => into.AddRange(_mods);
        }

        private static BalanceConfig Cfg() => new BalanceConfig();

        private static AttributeSet Attrs(int str = 4, int agi = 4, int wit = 4, int wil = 4)
            => new AttributeSet(str, agi, wit, wil);

        [Test]
        public void BaseAxes_LandInSnapshot()
        {
            var skills = new SkillSet();
            skills[SkillType.Medicine] = 7;

            var snap = StatResolver.Resolve(Attrs(str: 6), skills, null, Cfg());

            Assert.AreEqual(6, snap.Attribute(AttributeType.Strength));
            Assert.AreEqual(7, snap.Skill(SkillType.Medicine));
            Assert.AreEqual(0, snap.Skill(SkillType.Ranged), "невкачанный скил — ноль, а не отсутствие");
        }

        [Test]
        public void Derived_ComesFromAttributes()
        {
            var cfg = Cfg();
            var snap = StatResolver.Resolve(Attrs(str: 4, agi: 9), new SkillSet(), null, cfg);

            Assert.AreEqual(10, snap.Derived(DerivedStat.MaxHp), "HpBase 6 + Сила 4 x 1.0");
            Assert.AreEqual(11, snap.Derived(DerivedStat.MaxAp), "ApBase 8 + Спритність 9 / 3");
        }

        /// <summary>Перк «+3 HP» зобов'язаний лягати поверх похідної — на цьому тримається US-5.2.</summary>
        [Test]
        public void Derived_AcceptsModifiers()
        {
            var perk = new Provider(StatModifier.Flat(StatKey.MaxHp, 3, ModifierSource.Perk, "tough"));
            var snap = StatResolver.Resolve(Attrs(str: 4), new SkillSet(), new[] { perk }, Cfg());

            Assert.AreEqual(13, snap.Derived(DerivedStat.MaxHp), "6 + 4 + перк 3");
        }

        [Test]
        public void Fold_IsFlatThenPercentThenMultiplier()
        {
            var p = new Provider(
                new StatModifier(StatKey.Armor, 2, ModMode.Flat, ModifierSource.Gear),
                new StatModifier(StatKey.Armor, 0.5, ModMode.PercentAdd, ModifierSource.Trait),
                new StatModifier(StatKey.Armor, 1.2, ModMode.Multiplier, ModifierSource.Buff));

            var snap = StatResolver.Resolve(Attrs(), new SkillSet(), new[] { p }, Cfg());

            // база 0 + 2 = 2; ×(1 + 0.5) = 3; ×1.2 = 3.6
            Assert.AreEqual(3.6, snap.Get(StatKey.Armor), 1e-9);
        }

        /// <summary>Той самий набір в іншому порядку зобов'язаний дати той самий результат.</summary>
        [Test]
        public void Fold_IsOrderIndependent()
        {
            var a = new StatModifier(StatKey.Accuracy, 5, ModMode.Flat, ModifierSource.Gear);
            var b = new StatModifier(StatKey.Accuracy, 0.25, ModMode.PercentAdd, ModifierSource.Trait);
            var c = new StatModifier(StatKey.Accuracy, 1.1, ModMode.Multiplier, ModifierSource.Status);

            var one = StatResolver.Resolve(Attrs(), new SkillSet(), new[] { new Provider(a, b, c) }, Cfg());
            var two = StatResolver.Resolve(Attrs(), new SkillSet(), new[] { new Provider(c, a, b) }, Cfg());
            var three = StatResolver.Resolve(Attrs(), new SkillSet(), new[] { new Provider(b, c, a) }, Cfg());

            Assert.AreEqual(one.Get(StatKey.Accuracy), two.Get(StatKey.Accuracy), 1e-9);
            Assert.AreEqual(one.Get(StatKey.Accuracy), three.Get(StatKey.Accuracy), 1e-9);
        }

        /// <summary>
        /// Один і той самий ключ від чотирьох різних джерел складається один раз.
        /// Це і є «немає подвійного рахунку» у перевірюваному вигляді.
        /// </summary>
        [Test]
        public void SameKeyFromFourSources_SumsOnce()
        {
            var p = new Provider(
                StatModifier.Flat(StatKey.Armor, 1, ModifierSource.Gear),
                StatModifier.Flat(StatKey.Armor, 1, ModifierSource.Trait),
                StatModifier.Flat(StatKey.Armor, -1, ModifierSource.Scar),
                StatModifier.Flat(StatKey.Armor, 1, ModifierSource.Status));

            var snap = StatResolver.Resolve(Attrs(), new SkillSet(), new[] { p }, Cfg());
            Assert.AreEqual(2, snap.Get(StatKey.Armor), 1e-9);
            Assert.AreEqual(4, snap.SourcesOf(StatKey.Armor).Count, "разбор источников сохранён для тултипа");
        }

        [Test]
        public void Override_BeatsEverything()
        {
            var p = new Provider(
                StatModifier.Flat(StatKey.Defense, 100, ModifierSource.Gear),
                new StatModifier(StatKey.Defense, 7, ModMode.Override, ModifierSource.Status, "stunned"));

            var snap = StatResolver.Resolve(Attrs(), new SkillSet(), new[] { p }, Cfg());
            Assert.AreEqual(7, snap.Get(StatKey.Defense), 1e-9);
        }

        [Test]
        public void UnknownKey_ReturnsZeroNotThrow()
        {
            var snap = StatResolver.Resolve(Attrs(), new SkillSet(), null, Cfg());
            Assert.AreEqual(0, snap.Get(StatKey.DamageBonus));
        }

        [Test]
        public void NullsAreTolerated()
        {
            Assert.DoesNotThrow(() => StatResolver.Resolve(null, null, null, Cfg()));
        }
    }

    /// <summary>
    /// Забір від розсинхрону: плаский ключ агрегатора зобов'язаний взаємно однозначно
    /// відповідати осям моделі. Додав скіл і забув ключ — тест червоніє.
    /// </summary>
    public class StatKeyMappingTests
    {
        [Test]
        public void EveryAttribute_HasUniqueRoundTrippingKey()
        {
            var seen = new HashSet<StatKey>();
            foreach (var a in Attributes.All)
            {
                var key = StatKeys.Of(a);
                Assert.AreNotEqual(StatKey.None, key, $"нет ключа для {a}");
                Assert.IsTrue(seen.Add(key), $"ключ {key} занят дважды");
                Assert.IsTrue(StatKeys.TryToAttribute(key, out var back) && back == a, $"round-trip сломан для {a}");
            }
        }

        [Test]
        public void EverySkill_HasUniqueRoundTrippingKey()
        {
            var seen = new HashSet<StatKey>();
            foreach (var s in Skills.All)
            {
                var key = StatKeys.Of(s);
                Assert.AreNotEqual(StatKey.None, key, $"нет ключа для {s}");
                Assert.IsTrue(seen.Add(key), $"ключ {key} занят дважды");
                Assert.IsTrue(StatKeys.TryToSkill(key, out var back) && back == s, $"round-trip сломан для {s}");
            }
        }

        [Test]
        public void EveryDerived_HasUniqueRoundTrippingKey()
        {
            foreach (var d in DerivedStatCatalog.All)
            {
                var key = StatKeys.Of(d);
                Assert.IsTrue(StatKeys.IsDerived(key), $"{d} должен попадать в диапазон производных");
                Assert.IsTrue(StatKeys.TryToDerived(key, out var back) && back == d);
            }
        }

        /// <summary>Діапазони не повинні перетинатися — на них зав'язаний і тулінг карти.</summary>
        [Test]
        public void KeyRanges_DoNotOverlap()
        {
            foreach (var a in Attributes.All) Assert.IsTrue(StatKeys.IsAttribute(StatKeys.Of(a)));
            foreach (var s in Skills.All) Assert.IsTrue(StatKeys.IsSkill(StatKeys.Of(s)));
            foreach (var d in DerivedStatCatalog.All) Assert.IsTrue(StatKeys.IsDerived(StatKeys.Of(d)));
        }

        /// <summary>Скіли моделі зобов'язані збігатися з ключами, якими їх кличе міський шар.</summary>
        [Test]
        public void EverySkill_MapsToSettlementKey()
        {
            foreach (var s in Skills.All)
            {
                var id = Skills.KeyId(s);
                Assert.IsNotNull(id, $"нет строкового ключа для {s}");
                Assert.AreEqual(s, Skills.FromKeyId(id), "разбор строкового ключа не сходится");
            }
        }

        [Test]
        public void SkillGroups_FollowValueRanges()
        {
            Assert.AreEqual(SkillGroup.Combat, Skills.GroupOf(SkillType.Ranged));
            Assert.AreEqual(SkillGroup.Utility, Skills.GroupOf(SkillType.Medicine));
            Assert.AreEqual(SkillGroup.Social, Skills.GroupOf(SkillType.Trade));
        }
    }
}
