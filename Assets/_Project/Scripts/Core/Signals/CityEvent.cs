namespace Game.Core.Signals
{
    /// <summary>
    /// Что город сделал за сутки: достроил, вырос, принял людей или потерял их.
    ///
    /// Приходит в слой сигналов наравне с инцидентами и предвестниками и
    /// выходит наружу ТОЛЬКО через него — ключом и тегами. Так правило «всё,
    /// что игрок узнаёт о городе, проходит через одну точку» (Поправка №3.4)
    /// не получает второй двери.
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
