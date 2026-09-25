namespace Game.Core.Signals
{
    /// <summary>
    /// Що місто зробило за добу: добудувало, виросло, прийняло людей або
    /// втратило їх.
    ///
    /// Приходить у шар сигналів нарівні з інцидентами і передвісниками і
    /// виходить назовні ТІЛЬКИ через нього — ключем і тегами. Так правило
    /// «усе, що гравець дізнається про місто, проходить через одну точку»
    /// (Поправка №3.4) не отримує других дверей.
    /// </summary>
    public readonly struct CityEvent
    {
        public readonly string TopicId;
        public readonly SignalUrgency Urgency;
        public readonly string[] Tags;

        public CityEvent(string topicId, SignalUrgency urgency, params string[] tags)
        {
            TopicId = topicId;
            Urgency = urgency;
            Tags = tags ?? System.Array.Empty<string>();
        }
    }
}
