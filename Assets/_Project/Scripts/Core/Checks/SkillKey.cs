using System;

namespace Game.Core.Checks
{
    /// <summary>
    /// Ключ навички/атрибута для перевірки.
    ///
    /// Рядок, а не enum, навмисно: модель персонажа (4 атрибути + 10 скілів)
    /// ще буде переписана, і міський шар не повинен від неї залежати.
    /// Мапінг ключа на конкретний стат живе ТІЛЬКИ в адаптері — в одному місці.
    /// Конвенція рядкового ключа в проєкті вже прийнята (PassiveBonusId).
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
    /// Відомі ключі. Єдине місце, де назви навичок зашиті в код;
    /// увесь інший контент бере їх звідси або з даних.
    /// Склад відповідає десяти скілам GDD (Епік 2.2).
    /// </summary>
    public static class SkillKeys
    {
        // Бій
        public static readonly SkillKey Ranged = new SkillKey("ranged");
        public static readonly SkillKey Melee = new SkillKey("melee");
        public static readonly SkillKey Tactics = new SkillKey("tactics");

        // Утиліта
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
