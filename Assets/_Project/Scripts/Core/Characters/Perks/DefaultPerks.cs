using System.Collections.Generic;
using Game.Core.Stats;

namespace Game.Core.Characters.Perks
{
    /// <summary>
    /// Контент перків (полірування, ціль 1 «Картка персонажа»): до цього
    /// пакета <see cref="PerkDefinition"/> існував без жодного екземпляра —
    /// картка персонажа не мала що показати в секції "Перки (доступні)".
    /// Гейти — по скілах, які вже реально є в стартовому касті (§3.0
    /// TEST_BUILD.md), щоб "доступний" не було порожнім твердженням для
    /// жодного напарника. ПЛЕЙСХОЛДЕР-модифікатори, як і решта чисел зрізу.
    ///
    /// Взяття перка (<see cref="CompanionPerks.TryTake"/>) командою
    /// GameSession НЕ підключено цим пакетом — картка лише ПОКАЗУЄ, що
    /// доступно, той самий обсяг, що просив власник ("perks
    /// unlocked/available"); гейт-вітрина білд-планувальника — окрема робота.
    /// </summary>
    public static class DefaultPerks
    {
        public static PerkDefinition HardenedFighter() =>
            new PerkDefinition("hardened_fighter", "Загартований", SkillType.Melee, 5)
                .WithModifier(StatKeys.Of(DerivedStat.MaxHp), 2.0);

        public static PerkDefinition FieldMedic() =>
            new PerkDefinition("field_medic", "Польовий лікар", SkillType.Medicine, 6)
                .WithModifier(StatKeys.Of(DerivedStat.StatusDurationReduction), 0.1);

        public static PerkDefinition MasterTrader() =>
            new PerkDefinition("master_trader", "Бувалий торговець", SkillType.Trade, 6)
                .WithModifier(StatKeys.Of(DerivedStat.CarryCapacity), 2.0);

        public static PerkDefinition Sharpshooter() =>
            new PerkDefinition("sharpshooter", "Влучний стрілець", SkillType.Ranged, 5)
                .WithModifier(StatKeys.Of(DerivedStat.CritChance), 0.05);

        /// <summary>Порядок оголошення — порядок показу на картці/в тестах покриття.</summary>
        public static IReadOnlyList<PerkDefinition> All() => new List<PerkDefinition>
        {
            HardenedFighter(), FieldMedic(), MasterTrader(), Sharpshooter()
        };
    }
}
