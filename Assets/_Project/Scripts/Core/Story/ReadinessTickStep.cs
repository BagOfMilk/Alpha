using System;
using Game.Core.Balance;
using Game.Core.Loop;

namespace Game.Core.Story
{
    /// <summary>
    /// Крок дня, що перетворює денні віхи на Готовність (R8), на
    /// <c>DayStepOrder.Readiness</c> (550, між Tension=500 і Obligations=600).
    ///
    /// Покриває ЛИШЕ ті віхи, які видно прямо з конвеєра дня:
    /// • будівля добудована сьогодні (<c>DayContext.CityEvents</c>, ключ
    ///   <c>city.built.*</c> — той самий, що ставить <c>CityWorksStep</c>);
    /// • майбутній указ ради «Готуватись» (<c>council.prepare_threat*</c>,
    ///   B5 — рядком, без залежності від типу CityWorks, форвард-сумісно);
    /// • доба, коли громада не боїться (<c>FearState.IsAfraid</c> == false).
    ///
    /// Вилазка (B7) і завершення квесту (R6, поза конвеєром — §1.1) НЕ можуть
    /// пройти через цей крок узагалі: обидві події трапляються поза викликом
    /// <c>DayProcessor.Advance</c>. Для них <see cref="ReadinessTrack.Add"/>
    /// (internal, той самий прийом, що в TensionDrivers) викликає безпосередньо
    /// D1 у момент, коли подія стається — задокументований сеам, не недогляд.
    /// </summary>
    public sealed class ReadinessTickStep : IDayStep
    {
        private readonly ReadinessTrack _track;
        private readonly ReadinessBalance _cfg;

        public ReadinessTickStep(ReadinessTrack track, ReadinessBalance cfg = null)
        {
            _track = track;
            _cfg = cfg ?? new ReadinessBalance();
        }

        public int Order => DayStepOrder.Readiness;

        public void Execute(DayContext ctx)
        {
            if (_track == null) return;

            // Той самий гейт, що в Construction/Tension: подія одної календарної
            // доби не рахується двічі — за день і за ніч у тій же добі.
            if (ctx.IsNight) return;

            if (ctx.CityEvents != null)
                for (int i = 0; i < ctx.CityEvents.Count; i++)
                {
                    var topic = ctx.CityEvents[i].TopicId;
                    if (string.IsNullOrEmpty(topic)) continue;

                    if (topic.StartsWith("city.built.", StringComparison.Ordinal))
                        _track.Add(_cfg.BuildingCompletedAmount);
                    else if (topic.StartsWith("council.prepare_threat", StringComparison.Ordinal))
                        _track.Add(_cfg.PrepareThreatAmount);
                }

            if (ctx.Fear != null && !ctx.Fear.IsAfraid(ctx.Day))
                _track.Add(_cfg.NoFearDayAmount);
        }
    }
}
