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

        /// <summary>
        /// true — сигнал обязан попасть в дневной дайджест независимо от бюджета
        /// внимания (инвариант 4 CLAUDE.md: нет немого перехода полосы). Ставится
        /// смене полосы любой скрытой шкалы и ступени лестницы предвестников —
        /// единственные два случая, где пропуск читается как «игра сломалась» или
        /// как настоящий необратимый пропуск ступени (G21). Обычный бюджет
        /// (<see cref="Balance.SignalBalance.MaxSignalsPerDay"/>) ограничивает всё
        /// остальное; мандатные кандидаты — гарантия СВЕРХ него, не конкурент за
        /// общий резерв дельт (<see cref="Balance.SignalBalance.MinDeltaSlots"/>).
        /// </summary>
        public readonly bool Mandatory;

        public readonly string[] Tags;

        public SignalRequest(SignalChannel channel, string topicId, SignalUrgency urgency,
            string subjectId = null, bool isDelta = false, string[] tags = null, bool mandatory = false)
        {
            Channel = channel;
            TopicId = topicId;
            Urgency = urgency;
            SubjectId = subjectId;
            IsDelta = isDelta;
            Tags = tags ?? System.Array.Empty<string>();
            Mandatory = mandatory;
        }
    }
}
