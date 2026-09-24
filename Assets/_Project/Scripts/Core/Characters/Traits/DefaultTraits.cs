using System.Collections.Generic;
using Game.Core.Stats;

namespace Game.Core.Characters.Traits
{
    /// <summary>
    /// Контент стартових трейтів іменного касту (полірування, ціль 1
    /// «Картка персонажа»): до цього пакета <see cref="TraitDefinition"/>
    /// існував без жодного екземпляра — система слотів була, вкладати в неї
    /// було нічого, і картка персонажа показувала б порожню секцію "Трейти"
    /// для всієї гри. ПЛЕЙСХОЛДЕР-контент, як і решта чисел зрізу (§9
    /// TEST_BUILD.md): модифікатори — маленькі й орієнтовні, не збалансовані
    /// проти реального бою.
    ///
    /// Дані, а не код (за прикладом <see cref="Scars.DefaultScars"/>): Id +
    /// модифікатори тут, український текст (назва/ефект) — виключно в
    /// <c>UkrainianText</c> (R7, ключі "trait.&lt;id&gt;"/"trait.&lt;id&gt;.effect").
    /// </summary>
    public static class DefaultTraits
    {
        /// <summary>Захар/Максим: не відступає й не піддається станам страху.</summary>
        public static TraitDefinition Steadfast() =>
            new TraitDefinition("steadfast", "Незламний", TraitPolarity.Virtue)
                .WithModifier(StatKeys.Of(DerivedStat.StatusDurationReduction), 0.1)
                .WithValue("resolve");

        /// <summary>Максим: б'ється за громаду, а не заради помсти — але норов гарячий.</summary>
        public static TraitDefinition HotBlooded() =>
            new TraitDefinition("hot_blooded", "Гарячий норов", TraitPolarity.Vice)
                .WithModifier(StatKeys.Of(SkillType.Persuade), -1.0)
                .WithValue("temper");

        /// <summary>Мирослава: слухає більше, ніж каже.</summary>
        public static TraitDefinition Wary() =>
            new TraitDefinition("wary", "Обережна", TraitPolarity.Neutral)
                .WithModifier(StatKeys.Of(DerivedStat.Initiative), 1.0)
                .WithValue("caution");

        /// <summary>Мирослава: бачить те, чого не бачать інші.</summary>
        public static TraitDefinition SharpEyed() =>
            new TraitDefinition("sharp_eyed", "Гострозора", TraitPolarity.Virtue)
                .WithModifier(StatKeys.Of(DerivedStat.Accuracy), 1.0)
                .WithValue("vigilance");

        /// <summary>Дід Овсій: рахує кожен мішок уголос і не вірить чужим числам.</summary>
        public static TraitDefinition Meticulous() =>
            new TraitDefinition("meticulous", "Прискіпливий", TraitPolarity.Neutral)
                .WithModifier(StatKeys.Of(SkillType.Trade), 1.0)
                .WithModifier(StatKeys.Of(SkillType.Persuade), -1.0)
                .WithValue("order");

        /// <summary>Знахарка Гафія: лікує всіх, але кожному каже правду в очі.</summary>
        public static TraitDefinition Blunt() =>
            new TraitDefinition("blunt", "Прямий", TraitPolarity.Neutral)
                .WithModifier(StatKeys.Of(SkillType.Medicine), 1.0)
                .WithModifier(StatKeys.Of(SkillType.Trade), -1.0)
                .WithValue("honesty");

        /// <summary>Порядок оголошення — порядок показу в довіднику/тестах покриття.</summary>
        public static IReadOnlyList<TraitDefinition> All() => new List<TraitDefinition>
        {
            Steadfast(), HotBlooded(), Wary(), SharpEyed(), Meticulous(), Blunt()
        };
    }
}
