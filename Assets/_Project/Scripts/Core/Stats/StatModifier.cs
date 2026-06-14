using System;

namespace Game.Core.Stats
{
    /// <summary>Как модификатор применяется к производному стату.</summary>
    public enum ModMode
    {
        Flat = 0,       // +N к значению
        PercentAdd = 1  // +N% от базы (проценты складываются аддитивно, затем множат)
    }

    /// <summary>
    /// Откуда пришёл модификатор. Нужен для прозрачности и для правила
    /// «один эффект — одна система»: гир/трейт/шрам/состояние/бафф — пять
    /// независимых источников, сходящихся в одном агрегаторе.
    /// </summary>
    public enum ModifierSource
    {
        Gear = 0,
        Trait = 1,
        Scar = 2,
        State = 3,
        Buff = 4,
        Perk = 5
    }

    /// <summary>
    /// Один модификатор производного стата (<see cref="DerivedStat"/>). Все
    /// источники складываются в <see cref="ModifierAggregator"/> (GDD §18 US-18.2),
    /// чтобы не было двойного счёта и новый стат добавлялся в одном месте.
    /// </summary>
    [Serializable]
    public struct StatModifier
    {
        public DerivedStat Stat;
        public double Value;
        public ModMode Mode;
        public ModifierSource Source;

        public StatModifier(DerivedStat stat, double value,
                            ModMode mode = ModMode.Flat, ModifierSource source = ModifierSource.Gear)
        {
            Stat = stat;
            Value = value;
            Mode = mode;
            Source = source;
        }
    }
}
