using System.Collections.Generic;

namespace Game.Core.Stats
{
    /// <summary>Как модификатор входит в свёртку.</summary>
    public enum ModMode
    {
        Flat = 0,       // складывается с базой
        PercentAdd = 1, // проценты складываются между собой, потом умножают сумму
        Multiplier = 2, // перемножаются
        Override = 3    // перебивает всё
    }

    /// <summary>
    /// Откуда пришёл модификатор. Нужен не для арифметики, а для разбора: тултип
    /// «откуда взялось +5» и аудит на двойной счёт.
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
    /// Один вклад в один стат. Всё, что меняет числа персонажа — гир, трейт,
    /// шрам, состояние, бафф, перк — выражается этим типом и ничем другим:
    /// в этом и состоит правило «один эффект — одна система» (US-18.2).
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
    /// Всё, что умеет добавлять модификаторы персонажу. Гир, набор трейтов, трек
    /// шрамов, активное состояние, бафф совета — реализуют этот интерфейс, и
    /// агрегатору больше ничего знать не нужно.
    /// </summary>
    public interface IModifierProvider
    {
        void CollectModifiers(List<StatModifier> into);
    }
}
