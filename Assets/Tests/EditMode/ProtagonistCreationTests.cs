using Game.Core.Characters;
using Game.Core.Characters.Creation;
using Game.Core.Stats;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Створення протагоніста — швидкий екран (R12/US-2.7, спрощено Поправкою
    /// №5.9): три готові preset'и замість поінт-бай. Тут — що preset реально
    /// застосовується до вже існуючого протагоніста <c>FirstHourWorld</c>.
    /// </summary>
    public class ProtagonistCreationTests
    {
        private static Companion PlaceholderProtagonist()
        {
            // Той самий плейсхолдер, що ставить FirstHourWorld.BuildRoster до
            // застосування R12 (§3.0 FIRST_HOUR): всі чотири атрибути й чотири
            // скіли рівні 4.
            var arch = new CompanionArchetype("protagonist", "Провідник");
            arch.SetAttribute(AttributeType.Strength, 4).SetAttribute(AttributeType.Agility, 4)
                .SetAttribute(AttributeType.Wits, 4).SetAttribute(AttributeType.Will, 4)
                .SetSkill(SkillType.Persuade, 4).SetSkill(SkillType.Tactics, 4)
                .SetSkill(SkillType.Melee, 4).SetSkill(SkillType.Survival, 4);
            return arch.CreateInstance("protagonist");
        }

        [Test]
        public void ThreePresets_Exist_WithDistinctIds()
        {
            var all = Backgrounds.All();
            Assert.AreEqual(3, all.Count);

            var ids = new System.Collections.Generic.HashSet<string>();
            foreach (var p in all)
            {
                Assert.IsFalse(string.IsNullOrEmpty(p.Id));
                Assert.IsFalse(string.IsNullOrEmpty(p.DisplayNameKey), "Core отдаёт ключ, не текст (R7)");
                Assert.IsTrue(ids.Add(p.Id), "id пресета не должен повторяться");
            }
        }

        [Test]
        public void Warrior_Apply_OverwritesAttributesAndSkills()
        {
            var protagonist = PlaceholderProtagonist();
            var preset = Backgrounds.Warrior();

            ProtagonistCreation.Apply(protagonist, preset);

            Assert.AreEqual(preset.Attributes[AttributeType.Strength], protagonist.Attribute(AttributeType.Strength));
            Assert.AreEqual(preset.Attributes[AttributeType.Wits], protagonist.Attribute(AttributeType.Wits));
            Assert.AreEqual(preset.Skills[SkillType.Melee], protagonist.Skill(SkillType.Melee));

            Assert.Greater(protagonist.Attribute(AttributeType.Strength), protagonist.Attribute(AttributeType.Wits),
                "воин по тексту §7.17: больше Силы и Ближнего боя, меньше Смекалки");
        }

        [Test]
        public void Trader_Apply_FavoursWitsAndTradeOverStrength()
        {
            var protagonist = PlaceholderProtagonist();
            ProtagonistCreation.Apply(protagonist, Backgrounds.Trader());

            Assert.Greater(protagonist.Attribute(AttributeType.Wits), protagonist.Attribute(AttributeType.Strength));
            Assert.Greater(protagonist.Skill(SkillType.Trade), 0);
        }

        [Test]
        public void Healer_Apply_FavoursWillAndMedicineOverAgility()
        {
            var protagonist = PlaceholderProtagonist();
            ProtagonistCreation.Apply(protagonist, Backgrounds.Healer());

            Assert.Greater(protagonist.Attribute(AttributeType.Will), protagonist.Attribute(AttributeType.Agility));
            Assert.Greater(protagonist.Skill(SkillType.Medicine), 0);
        }

        [Test]
        public void Apply_WithCustomName_OverridesDisplayName()
        {
            var protagonist = PlaceholderProtagonist();
            ProtagonistCreation.Apply(protagonist, Backgrounds.Warrior(), "Данило");

            Assert.AreEqual("Данило", protagonist.DisplayName);
        }

        [Test]
        public void Apply_WithoutName_LeavesDisplayNameUntouched()
        {
            var protagonist = PlaceholderProtagonist();
            string before = protagonist.DisplayName;

            ProtagonistCreation.Apply(protagonist, Backgrounds.Warrior());

            Assert.AreEqual(before, protagonist.DisplayName,
                "порожнє ім'я — не помилка: дефолтний текст обирає E3 за ключем роду (§7.17)");
        }

        [Test]
        public void Apply_NullPresetOrProtagonist_DoesNothing()
        {
            var protagonist = PlaceholderProtagonist();
            int before = protagonist.Attribute(AttributeType.Strength);

            ProtagonistCreation.Apply(protagonist, null);
            ProtagonistCreation.Apply(null, Backgrounds.Warrior());

            Assert.AreEqual(before, protagonist.Attribute(AttributeType.Strength));
        }

        [Test]
        public void ById_FindsPreset_AndReturnsNullForUnknown()
        {
            Assert.AreEqual("trader", Backgrounds.ById("trader").Id);
            Assert.IsNull(Backgrounds.ById("nope"));
        }
    }
}
