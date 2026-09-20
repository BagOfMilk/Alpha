using Game.Core.Balance;

namespace Game.Core.Settlement
{
    /// <summary>
    /// Страх общины — цена кровавого пути, растянутая во времени.
    ///
    /// ЗАЧЕМ. Проверка умела возвращать <c>CausedFear</c> с самого Э1, но читать
    /// его было некому: кровавый путь не стоил ничего, и «быстро и очень тяжело»
    /// (Поправка №1) на деле означало просто «быстро». Здесь у флага наконец
    /// появляется потребитель.
    ///
    /// ЯКОРЬ: сутки, до которых община помнит кровь. Пока помнит — социальные
    /// подходы (Убеждение и Торговля) дороже на <c>FearPenaltyStep</c>: с тем,
    /// кто вчера решал дело ножом, договариваются неохотно.
    ///
    /// Запугивание страхом НЕ дешевеет. Иначе кровавый путь окупал бы сам себя,
    /// и инверсия, которую этот класс чинит, вернулась бы с другой стороны.
    ///
    /// СИГНАЛ: появление страха слышно в тот же день (слой сигналов выдаёт
    /// «settlement.fear»). Инвариант 6 требует все три вещи разом — якорь,
    /// потребителя и сигнал, — и здесь они есть.
    /// </summary>
    public sealed class FearState
    {
        /// <summary>
        /// Последние сутки действия страха. Число наружу не отдаётся: игрок
        /// узнаёт о страхе по репликам и по выросшему порогу, а не по счётчику.
        /// </summary>
        internal int UntilDay { get; private set; } = -1;

        /// <summary>Боится ли община прямо сейчас.</summary>
        public bool IsAfraid(int day)
        {
            return day <= UntilDay;
        }

        /// <summary>Запомнить кровь. Повторная кровь продлевает страх, а не складывает его.</summary>
        internal void Remember(int day, CheckBalance cfg)
        {
            int until = day + (cfg != null ? cfg.FearDurationDays : 0);
            if (until > UntilDay) UntilDay = until;
        }

        /// <summary>Надбавка к порогу социальной проверки на эти сутки.</summary>
        internal int PenaltyOn(int day, CheckBalance cfg)
        {
            if (cfg == null || !IsAfraid(day)) return 0;
            return cfg.FearPenaltyStep;
        }

        internal void RestoreForSave(int untilDay)
        {
            UntilDay = untilDay;
        }
    }
}
