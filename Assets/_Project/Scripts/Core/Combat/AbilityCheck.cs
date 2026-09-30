namespace Game.Core.Combat
{
    /// <summary>Вид перевірки здібності: змагання навички з Волею цілі чи умова на полі.</summary>
    public enum AbilityCheckKind { Contest = 0, Condition = 1 }

    /// <summary>
    /// Перевірка здібності, видна ДО кліку (інваріант 8, docs/ABILITIES.md): що з чим
    /// порівнюється, чи ціль має «Імунітет», чи вдасться. Бій і прев'ю беруть її з
    /// одного місця (<see cref="CombatState.DescribeCheck"/>) — розійтися не можуть.
    /// </summary>
    public sealed class AbilityCheck
    {
        public AbilityCheckKind Kind;

        /// <summary>"intimidate" | "shred" | null.</summary>
        public string SkillKey;

        public int Value;
        public int Threshold;
        public bool Immune;
        public bool Passes;

        /// <summary>Чому дію не можна почати: "immune" | "rallied" | "armor_intact"; null — можна.</summary>
        public string BlockKey;
    }
}
