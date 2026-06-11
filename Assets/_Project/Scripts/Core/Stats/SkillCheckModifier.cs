namespace Game.Core.Stats
{
    /// <summary>
    /// Флэт-модификатор к проверке по конкретному скилу (GDD §2.4 US-2.6).
    /// Несут трейты и шрамы; складываются в CheckResolver вместе со значением скила.
    /// </summary>
    [System.Serializable]
    public struct SkillCheckModifier
    {
        public SkillType Skill;
        public int Value;

        public SkillCheckModifier(SkillType skill, int value)
        {
            Skill = skill;
            Value = value;
        }
    }
}
