using Game.Core.Balance;

namespace Game.Core.Settlement
{
    /// <summary>
    /// Страх громади — ціна кривавого шляху, розтягнута в часі.
    ///
    /// НАВІЩО. Перевірка вміла повертати <c>CausedFear</c> ще з самого Е1, але
    /// читати його не було кому: кривавий шлях не коштував нічого, і «швидко і
    /// дуже складно» (Поправка №1) насправді означало просто «швидко». Тут у
    /// прапорця нарешті з'являється споживач.
    ///
    /// ЯКІР: доба, до якої громада пам'ятає кров. Поки пам'ятає — соціальні
    /// підходи (Переконання і Торгівля) дорожчі на <c>FearPenaltyStep</c>: з
    /// тим, хто вчора вирішував справу ножем, домовляються неохоче.
    ///
    /// Залякування страхом НЕ дешевшає. Інакше кривавий шлях окупав би сам
    /// себе, і інверсія, яку цей клас лагодить, повернулася б з іншого боку.
    ///
    /// СИГНАЛ: поява страху чутна того самого дня (шар сигналів видає
    /// «settlement.fear»). Інваріант 6 вимагає всі три речі разом — якір,
    /// споживача і сигнал, — і тут вони є.
    /// </summary>
    public sealed class FearState
    {
        /// <summary>
        /// Остання доба дії страху. Число назовні не віддається: гравець
        /// дізнається про страх з реплік і з вирослого порога, а не з лічильника.
        /// </summary>
        internal int UntilDay { get; private set; } = -1;

        /// <summary>Чи боїться громада прямо зараз.</summary>
        public bool IsAfraid(int day)
        {
            return day <= UntilDay;
        }

        /// <summary>Запам'ятати кров. Повторна кров продовжує страх, а не складає його.</summary>
        internal void Remember(int day, CheckBalance cfg)
        {
            int until = day + (cfg != null ? cfg.FearDurationDays : 0);
            if (until > UntilDay) UntilDay = until;
        }

        /// <summary>Надбавка до порога соціальної перевірки на цю добу.</summary>
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
