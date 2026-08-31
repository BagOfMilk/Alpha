namespace Game.Core.World
{
    /// <summary>Природа источника давления — определяет, чем обернётся срабатывание.</summary>
    public enum WorldEventKind
    {
        /// <summary>Внутренняя угроза города: кражи, поборы, организованная преступность.</summary>
        InternalThreat = 0,
        /// <summary>Ночная преступность: активна только в тёмную фазу.</summary>
        NightCrime = 1,
        /// <summary>Кризис: бьёт больно и требует трёх ступеней предвестников.</summary>
        Crisis = 2
    }

    /// <summary>
    /// Предвестник. Честность системы: «что и где — честно, когда — нет».
    /// Уровень 2 обязан назвать домен, уровень 3 — близость, но точный день
    /// не сообщается никогда, даже косвенно.
    /// </summary>
    public readonly struct Forewarning
    {
        public readonly string SourceId;
        /// <summary>1 — амбиент без адреса; 2 — назван домен; 3 — «вот-вот».</summary>
        public readonly int Level;
        public readonly string DomainTag;

        public Forewarning(string sourceId, int level, string domainTag)
        {
            SourceId = sourceId;
            Level = level;
            DomainTag = domainTag;
        }
    }
}
