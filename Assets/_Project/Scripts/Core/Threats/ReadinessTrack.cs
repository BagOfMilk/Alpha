using System;
using Game.Core.Balance;

namespace Game.Core.Threats
{
    /// <summary>Качественная полоса подготовки к финалу.</summary>
    public enum ReadinessBand
    {
        Unprepared = 0,
        Braced = 1,
        Fortified = 2
    }

    /// <summary>
    /// Скрытая шкала «Готовность» (Эпик 11.2, US-11.4): растёт от действия совета
    /// «Подготовка к угрозе», Укреплений и силы ростера; формирует сложность/исход
    /// финальной битвы. Финал запускается сюжетной вехой — доом-клока нет, поэтому
    /// шкала только копится. Число скрыто, наружу — полоса.
    /// </summary>
    public sealed class ReadinessTrack
    {
        private readonly BalanceConfig _cfg;

        public double Value { get; private set; }

        public ReadinessTrack(BalanceConfig cfg)
        {
            _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
        }

        public ReadinessBand Band
        {
            get
            {
                if (Value >= _cfg.ReadinessFortifiedAt) return ReadinessBand.Fortified;
                if (Value >= _cfg.ReadinessBracedAt) return ReadinessBand.Braced;
                return ReadinessBand.Unprepared;
            }
        }

        /// <summary>Действие совета «Подготовка к угрозе» (вход на итерацию совета).</summary>
        public void AddPreparation() => Add(_cfg.ReadinessPerPreparation);

        /// <summary>Построенные/улучшенные Укрепления.</summary>
        public void AddFortification() => Add(_cfg.ReadinessPerFortification);

        public void Add(double delta) => Value = Math.Max(0, Value + delta);
    }
}
