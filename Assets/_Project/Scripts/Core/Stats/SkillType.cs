namespace Game.Core.Stats
{
    /// <summary>Группа скила. Разнесена по десяткам значений enum, а не отдельным полем.</summary>
    public enum SkillGroup
    {
        Combat = 0,
        Utility = 1,
        Social = 2
    }

    /// <summary>
    /// Десять скилов (GDD Э2.2). Растут за очки, классов нет, респека нет.
    ///
    /// Значения разнесены по десяткам: 1–9 бой, 10–19 утилита, 20–29 соц.
    /// Это не украшение — по диапазону определяется группа, и тулинг карты
    /// читает её оттуда же. Шкала 0–10.
    /// </summary>
    public enum SkillType
    {
        None = 0,

        // Бой
        Ranged = 1,       // стрелковое (тяжёлое оружие — его подтип)
        Melee = 2,        // ближнее
        Tactics = 3,      // тактика

        // Утилита
        Lockpick = 10,    // взлом
        Mechanics = 11,   // механика
        Survival = 12,    // выживание
        Medicine = 13,    // медицина

        // Соц
        Persuade = 20,    // убеждение
        Intimidate = 21,  // запугивание
        Trade = 22        // торговля
    }

    public static class Skills
    {
        /// <summary>Все скилы без None — в порядке объявления.</summary>
        public static readonly SkillType[] All =
        {
            SkillType.Ranged, SkillType.Melee, SkillType.Tactics,
            SkillType.Lockpick, SkillType.Mechanics, SkillType.Survival, SkillType.Medicine,
            SkillType.Persuade, SkillType.Intimidate, SkillType.Trade
        };

        /// <summary>Группа читается из диапазона значения — второго источника правды нет.</summary>
        public static SkillGroup GroupOf(SkillType s)
        {
            int v = (int)s;
            if (v >= 20) return SkillGroup.Social;
            if (v >= 10) return SkillGroup.Utility;
            return SkillGroup.Combat;
        }

        public static bool IsCombat(SkillType s) => GroupOf(s) == SkillGroup.Combat;

        public static string DisplayName(SkillType s)
        {
            switch (s)
            {
                case SkillType.Ranged: return "Стрелковое";
                case SkillType.Melee: return "Ближнее";
                case SkillType.Tactics: return "Тактика";
                case SkillType.Lockpick: return "Взлом";
                case SkillType.Mechanics: return "Механика";
                case SkillType.Survival: return "Выживание";
                case SkillType.Medicine: return "Медицина";
                case SkillType.Persuade: return "Убеждение";
                case SkillType.Intimidate: return "Запугивание";
                case SkillType.Trade: return "Торговля";
                default: return "—";
            }
        }

        /// <summary>
        /// Строковый ключ, которым скил зовётся в городском слое (SkillKeys).
        /// Маппинг держится здесь, чтобы не расползтись по адаптерам.
        /// </summary>
        public static string KeyId(SkillType s)
        {
            switch (s)
            {
                case SkillType.Ranged: return "ranged";
                case SkillType.Melee: return "melee";
                case SkillType.Tactics: return "tactics";
                case SkillType.Lockpick: return "lockpick";
                case SkillType.Mechanics: return "mechanics";
                case SkillType.Survival: return "survival";
                case SkillType.Medicine: return "medicine";
                case SkillType.Persuade: return "persuade";
                case SkillType.Intimidate: return "intimidate";
                case SkillType.Trade: return "trade";
                default: return null;
            }
        }

        /// <summary>Обратный разбор строкового ключа. None — если ключ неизвестен.</summary>
        public static SkillType FromKeyId(string id)
        {
            if (string.IsNullOrEmpty(id)) return SkillType.None;
            for (int i = 0; i < All.Length; i++)
                if (KeyId(All[i]) == id) return All[i];
            return SkillType.None;
        }
    }
}
