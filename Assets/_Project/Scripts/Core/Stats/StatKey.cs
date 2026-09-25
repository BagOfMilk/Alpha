using System;

namespace Game.Core.Stats
{
    /// <summary>
    /// Єдиний ключ агрегатора модифікаторів (US-18.2).
    ///
    /// Модель персонажа читається трьома осями (атрибути, скіли, похідні), але
    /// СКЛАДАЄТЬСЯ все за одним плоским ключем. Це не суперечність: різні
    /// мови для авторства і для арифметики. Плоский enum обрано тому, що в
    /// інспекторі Unity модифікатор має бути одним дропдауном, а не парою
    /// полів з кастомним драйвером (US-18.1).
    ///
    /// Діапазони: 100+ атрибути, 200+ скіли, 300+ похідні. Розсинхрон між
    /// віссю і ключем ловиться тестом StatKeyMappingTests, а не дисципліною.
    /// </summary>
    public enum StatKey
    {
        None = 0,

        // Атрибути: 100 + значення AttributeType
        Strength = 101,
        Agility = 102,
        Wits = 103,
        Will = 104,

        // Скіли: 200 + значення SkillType
        Ranged = 201,
        Melee = 202,
        Tactics = 203,
        Lockpick = 210,
        Mechanics = 211,
        Survival = 212,
        Medicine = 213,
        Persuade = 220,
        Intimidate = 221,
        Trade = 222,

        // Похідні: 300 + значення DerivedStat
        MaxHp = 301,
        MaxAp = 302,
        Accuracy = 303,
        Defense = 304,
        Initiative = 305,
        CritChance = 306,
        Armor = 307,
        CarryCapacity = 308,
        StatusDurationReduction = 309,
        DamageBonus = 310,
        MoveApPerTile = 311
    }

    public static class StatKeys
    {
        public static StatKey Of(AttributeType a) =>
            a == AttributeType.None ? StatKey.None : (StatKey)(100 + (int)a);

        public static StatKey Of(SkillType s) =>
            s == SkillType.None ? StatKey.None : (StatKey)(200 + (int)s);

        public static StatKey Of(DerivedStat d) =>
            d == DerivedStat.None ? StatKey.None : (StatKey)(300 + (int)d);

        public static bool IsAttribute(StatKey k) => (int)k >= 100 && (int)k < 200;
        public static bool IsSkill(StatKey k) => (int)k >= 200 && (int)k < 300;
        public static bool IsDerived(StatKey k) => (int)k >= 300;

        public static bool TryToAttribute(StatKey k, out AttributeType a)
        {
            if (IsAttribute(k)) { a = (AttributeType)((int)k - 100); return true; }
            a = AttributeType.None; return false;
        }

        public static bool TryToSkill(StatKey k, out SkillType s)
        {
            if (IsSkill(k)) { s = (SkillType)((int)k - 200); return true; }
            s = SkillType.None; return false;
        }

        public static bool TryToDerived(StatKey k, out DerivedStat d)
        {
            if (IsDerived(k)) { d = (DerivedStat)((int)k - 300); return true; }
            d = DerivedStat.None; return false;
        }

        public static string DisplayName(StatKey k)
        {
            if (TryToAttribute(k, out var a)) return Attributes.DisplayName(a);
            if (TryToSkill(k, out var s)) return Skills.DisplayName(s);
            return Enum.GetName(typeof(StatKey), k) ?? "—";
        }
    }
}
