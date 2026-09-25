namespace Game.Core.Signals
{
    /// <summary>
    /// Запит на сигнал. Ядро видає КЛЮЧ і теги, а не готовий текст: конкретні
    /// репліки живуть у ScriptableObject-таблицях, тому письменнику не потрібен
    /// програміст, а контент масштабується без перекомпіляції (US-18.1).
    /// </summary>
    public readonly struct SignalRequest
    {
        public readonly SignalChannel Channel;
        /// <summary>Ключ у таблиці реплік, наприклад «tension.band.Heat».</summary>
        public readonly string TopicId;
        public readonly SignalUrgency Urgency;
        /// <summary>Фракція / напарник / будівля, про яку йдеться (може бути null).</summary>
        public readonly string SubjectId;
        /// <summary>true — сигнал відповідає на «що змінилося з учора».</summary>
        public readonly bool IsDelta;

        /// <summary>
        /// true — сигнал зобов'язаний потрапити в денний дайджест незалежно від
        /// бюджету уваги (інваріант 4 CLAUDE.md: нема німого переходу полоси).
        /// Ставиться зміні полоси будь-якої прихованої шкали і ступеню драбини
        /// передвісників — єдині два випадки, де пропуск читається як «гра
        /// зламалася» або як справжній незворотний пропуск ступені (G21).
        /// Звичайний бюджет (<see cref="Balance.SignalBalance.MaxSignalsPerDay"/>)
        /// обмежує все інше; мандатні кандидати — гарантія ПОНАД ним, не
        /// конкурент за спільний резерв дельт
        /// (<see cref="Balance.SignalBalance.MinDeltaSlots"/>).
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
