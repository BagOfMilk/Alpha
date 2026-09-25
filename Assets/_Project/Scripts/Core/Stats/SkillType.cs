namespace Game.Core.Stats
{
    /// <summary>Група скіла. Розведена по десятках значень enum, а не окремим полем.</summary>
    public enum SkillGroup
    {
        Combat = 0,
        Utility = 1,
        Social = 2
    }

    /// <summary>
    /// Десять скілів (GDD Е2.2). Ростуть за очки, класів немає, респеку немає.
    ///
    /// Значення розведені по десятках: 1–9 бій, 10–19 утиліта, 20–29 соц.
    /// Це не прикраса — за діапазоном визначається група, і тулінг карти
    /// читає її звідти ж. Шкала 0–10.
    /// </summary>
    public enum SkillType
    {
        None = 0,

        // Бій
        Ranged = 1,       // стрілецьке (важка зброя — його підтип)
        Melee = 2,        // ближній бій
        Tactics = 3,      // тактика

        // Утиліта
        Lockpick = 10,    // злом
        Mechanics = 11,   // механіка
        Survival = 12,    // виживання
        Medicine = 13,    // медицина

        // Соц
        Persuade = 20,    // переконання
        Intimidate = 21,  // залякування
        Trade = 22        // торгівля
    }

    public static class Skills
    {
        /// <summary>Усі скіли без None — у порядку оголошення.</summary>
        public static readonly SkillType[] All =
        {
            SkillType.Ranged, SkillType.Melee, SkillType.Tactics,
            SkillType.Lockpick, SkillType.Mechanics, SkillType.Survival, SkillType.Medicine,
            SkillType.Persuade, SkillType.Intimidate, SkillType.Trade
        };

        /// <summary>Група читається з діапазону значення — другого джерела істини нема.</summary>
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
        /// Строковий ключ, яким скіл зветься в міському шарі (SkillKeys).
        /// Мапінг тримається тут, щоб не розповзтися по адаптерах.
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

        /// <summary>Зворотний розбір строкового ключа. None — якщо ключ невідомий.</summary>
        public static SkillType FromKeyId(string id)
        {
            if (string.IsNullOrEmpty(id)) return SkillType.None;
            for (int i = 0; i < All.Length; i++)
                if (KeyId(All[i]) == id) return All[i];
            return SkillType.None;
        }
    }
}
