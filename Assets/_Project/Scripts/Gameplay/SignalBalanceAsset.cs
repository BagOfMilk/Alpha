using Game.Core.Balance;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Настройки слоя сигналов в инспекторе.
    /// Создание: ПКМ в Project → Create → Alpha → Balance → Сигналы.
    /// </summary>
    [CreateAssetMenu(fileName = "SignalBalance", menuName = "Alpha/Balance/Сигналы", order = 11)]
    public sealed class SignalBalanceAsset : ScriptableObject
    {
        [Header("Бюджет внимания")]
        [Tooltip("Больше — превращается в шум, который игрок перестаёт читать.")]
        [Min(1)] public int maxSignalsPerDay = 4;
        [Tooltip("Сколько слотов гарантированно уходит под «что изменилось со вчера».")]
        [Min(0)] public int minDeltaSlots = 1;

        [Header("Правила")]
        [Tooltip("Нет немого перехода: смена полосы обязана дать сигнал.")]
        public bool forceSignalOnBandChange = true;

        public SignalBalance ToConfig()
        {
            return new SignalBalance
            {
                MaxSignalsPerDay = maxSignalsPerDay,
                MinDeltaSlots = minDeltaSlots,
                ForceSignalOnBandChange = forceSignalOnBandChange
            };
        }
    }
}
