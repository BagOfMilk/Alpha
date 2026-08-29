using System;

namespace Game.Core.Balance
{
    /// <summary>
    /// Настройки слоя сигналов. Слой — часть системы, а не UI (Поправка №3.4):
    /// это единственный канал, через который игрок узнаёт о скрытых шкалах.
    /// </summary>
    [Serializable]
    public sealed class SignalBalance
    {
        /// <summary>Бюджет внимания игрока: больше — превращается в шум (риск N1).</summary>
        public int MaxSignalsPerDay = 4;

        /// <summary>Сколько слотов гарантированно уходит под «что изменилось со вчера».
        /// Прямое лекарство от «нет читаемого состояния».</summary>
        public int MinDeltaSlots = 1;

        /// <summary>Нет немого перехода полосы: смена полосы обязана дать сигнал.</summary>
        public bool ForceSignalOnBandChange = true;
    }
}
