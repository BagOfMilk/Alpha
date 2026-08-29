namespace Game.Core.Signals
{
    /// <summary>
    /// Запрос на сигнал. Ядро выдаёт КЛЮЧ и теги, а не готовый текст: конкретные
    /// реплики живут в ScriptableObject-таблицах, поэтому писателю не нужен
    /// программист, а контент масштабируется без перекомпиляции (US-18.1).
    /// </summary>
    public readonly struct SignalRequest
    {
        public readonly SignalChannel Channel;
        /// <summary>Ключ в таблице реплик, например «tension.band.Heat».</summary>
        public readonly string TopicId;
        public readonly SignalUrgency Urgency;
        /// <summary>Фракция / напарник / здание, о котором речь (может быть null).</summary>
        public readonly string SubjectId;
        /// <summary>true — сигнал отвечает на «что изменилось со вчера».</summary>
        public readonly bool IsDelta;
        public readonly string[] Tags;

        public SignalRequest(SignalChannel channel, string topicId, SignalUrgency urgency,
            string subjectId = null, bool isDelta = false, string[] tags = null)
        {
            Channel = channel;
            TopicId = topicId;
            Urgency = urgency;
            SubjectId = subjectId;
            IsDelta = isDelta;
            Tags = tags ?? System.Array.Empty<string>();
        }
    }
}
