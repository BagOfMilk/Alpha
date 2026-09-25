using System.Collections.Generic;

namespace Game.Core.Stats
{
    /// <summary>Як модифікатор входить у згортку.</summary>
    public enum ModMode
    {
        Flat = 0,       // додається до бази
        PercentAdd = 1, // відсотки складаються між собою, потім множать суму
        Multiplier = 2, // перемножуються
        Override = 3    // перебиває все
    }

    /// <summary>
    /// Звідки прийшов модифікатор. Потрібен не для арифметики, а для розбору: тултіп
    /// «звідки взялося +5» і аудит на подвійний рахунок.
    /// </summary>
    public enum ModifierSource
    {
        Base = 0,
        Gear = 1,
        Trait = 2,
        Scar = 3,
        Status = 4,
        Buff = 5,
        Perk = 6,
        Building = 7
    }

    /// <summary>
    /// Один внесок в один стат. Усе, що змінює числа персонажа — гір, трейт,
    /// шрам, стан, бафф, перк — виражається цим типом і нічим іншим:
    /// у цьому й полягає правило «один ефект — одна система» (US-18.2).
    /// </summary>
    public readonly struct StatModifier
    {
        public readonly StatKey Key;
        public readonly double Value;
        public readonly ModMode Mode;
        public readonly ModifierSource Source;
        public readonly string SourceId;

        public StatModifier(StatKey key, double value, ModMode mode, ModifierSource source, string sourceId = null)
        {
            Key = key;
            Value = value;
            Mode = mode;
            Source = source;
            SourceId = sourceId;
        }

        public static StatModifier Flat(StatKey key, double value, ModifierSource source, string sourceId = null)
            => new StatModifier(key, value, ModMode.Flat, source, sourceId);

        public override string ToString() => $"{Key} {Mode} {Value:+0.##;-0.##;0} ({Source}:{SourceId})";
    }

    /// <summary>
    /// Усе, що вміє додавати модифікатори персонажу. Гір, набір трейтів, трек
    /// шрамів, активний стан, бафф ради — реалізують цей інтерфейс, і
    /// агрегатору більше нічого знати не потрібно.
    /// </summary>
    public interface IModifierProvider
    {
        void CollectModifiers(List<StatModifier> into);
    }
}
