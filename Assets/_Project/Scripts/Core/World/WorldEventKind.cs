namespace Game.Core.World
{
    /// <summary>Природа джерела тиску — визначає, чим обернеться спрацювання.</summary>
    public enum WorldEventKind
    {
        /// <summary>Внутрішня загроза міста: крадіжки, побори, організована злочинність.</summary>
        InternalThreat = 0,
        /// <summary>Нічна злочинність: активна лише в темну фазу.</summary>
        NightCrime = 1,
        /// <summary>Криза: б'є боляче і вимагає трьох ступенів передвісників.</summary>
        Crisis = 2
    }

    /// <summary>
    /// Передвісник. Чесність системи: «що і де — чесно, коли — ні».
    /// Рівень 2 зобов'язаний назвати домен, рівень 3 — близькість, але точний день
    /// не повідомляється ніколи, навіть непрямо.
    /// </summary>
    public readonly struct Forewarning
    {
        public readonly string SourceId;
        /// <summary>1 — амбієнт без адреси; 2 — названо домен; 3 — «от-от».</summary>
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
