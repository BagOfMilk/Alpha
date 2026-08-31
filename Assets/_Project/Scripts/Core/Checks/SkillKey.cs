using System;

namespace Game.Core.Checks
{
    /// <summary>
    /// Ключ навыка/атрибута для проверки.
    ///
    /// Строка, а не enum, намеренно: модель персонажа (4 атрибута + 10 скилов)
    /// ещё будет переписана, и городской слой не должен от неё зависеть.
    /// Маппинг ключа на конкретный стат живёт ТОЛЬКО в адаптере — в одном месте.
    /// Конвенция строкового ключа в проекте уже принята (PassiveBonusId).
    /// </summary>
    public readonly struct SkillKey : IEquatable<SkillKey>
    {
        public readonly string Id;

        public SkillKey(string id)
        {
            Id = id;
        }

        public bool IsNone => string.IsNullOrEmpty(Id);

        public bool Equals(SkillKey other) => string.Equals(Id, other.Id, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is SkillKey other && Equals(other);
        public override int GetHashCode() => Id == null ? 0 : Id.GetHashCode();
        public override string ToString() => Id ?? "<none>";

        public static bool operator ==(SkillKey a, SkillKey b) => a.Equals(b);
        public static bool operator !=(SkillKey a, SkillKey b) => !a.Equals(b);
    }

    /// <summary>
    /// Известные ключи. Единственное место, где имена навыков зашиты в код;
    /// весь остальной контент берёт их отсюда или из данных.
    /// Состав соответствует десяти скилам GDD (Эпик 2.2).
    /// </summary>
    public static class SkillKeys
    {
        // Бой
        public static readonly SkillKey Ranged = new SkillKey("ranged");
        public static readonly SkillKey Melee = new SkillKey("melee");
        public static readonly SkillKey Tactics = new SkillKey("tactics");

        // Утилита
        public static readonly SkillKey Lockpick = new SkillKey("lockpick");
        public static readonly SkillKey Mechanics = new SkillKey("mechanics");
        public static readonly SkillKey Survival = new SkillKey("survival");
        public static readonly SkillKey Medicine = new SkillKey("medicine");

        // Соц
        public static readonly SkillKey Persuade = new SkillKey("persuade");
        public static readonly SkillKey Intimidate = new SkillKey("intimidate");
        public static readonly SkillKey Trade = new SkillKey("trade");
    }
}
