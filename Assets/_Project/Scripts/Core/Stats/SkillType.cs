namespace Game.Core.Stats
{
    /// <summary>
    /// Десять скилов (GDD §2.2). Растут за очки скилов с уровней — классов нет,
    /// любой напарник может вкачивать любую ветку; перки гейтятся порогом скила +
    /// пререквизитами; <b>респека нет</b>. Три группы: боевые, утилита, соц.
    /// </summary>
    public enum SkillType
    {
        None = 0,

        // ---- Боевые (3) ---- (Тяжёлое оружие — подтип Стрелкового)
        Ranged = 1,   // Стрелковое
        Melee = 2,    // Ближнее
        Tactics = 3,  // Тактика

        // ---- Утилита (4) ----
        Hacking = 10,    // Взлом
        Mechanics = 11,  // Механика
        Survival = 12,   // Выживание
        Medicine = 13,   // Медицина

        // ---- Соц (3) ----
        Persuasion = 20,    // Убеждение
        Intimidation = 21,  // Запугивание
        Trade = 22          // Торговля
    }

    /// <summary>Группа скила — для UI/фильтров и для логики (соц-скилы дают пассив, а не активки).</summary>
    public enum SkillGroup
    {
        Combat = 0,
        Utility = 1,
        Social = 2
    }

    public static class SkillTypeExtensions
    {
        /// <summary>К какой из трёх групп относится скил (см. GDD §2.2).</summary>
        public static SkillGroup Group(this SkillType skill)
        {
            switch (skill)
            {
                case SkillType.Ranged:
                case SkillType.Melee:
                case SkillType.Tactics:
                    return SkillGroup.Combat;
                case SkillType.Persuasion:
                case SkillType.Intimidation:
                case SkillType.Trade:
                    return SkillGroup.Social;
                default:
                    return SkillGroup.Utility;
            }
        }
    }
}
