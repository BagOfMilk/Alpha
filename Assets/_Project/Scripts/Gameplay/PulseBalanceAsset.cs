using Game.Core.Balance;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Числа «Пульса мира» в инспекторе.
    /// Создание: ПКМ в Project → Create → Alpha → Balance → Пульс.
    /// </summary>
    [CreateAssetMenu(fileName = "PulseBalance", menuName = "Alpha/Balance/Пульс", order = 12)]
    public sealed class PulseBalanceAsset : ScriptableObject
    {
        [Header("Накопитель")]
        [Tooltip("Якорь: дней до вмешательства ≈ порог / настойчивость источника.")]
        public int defaultThreshold = 100;
        [Tooltip("Потолок заряда, чтобы простой не копил бесконечную очередь.")]
        public int chargeCapMultiplier = 2;

        [Header("Предвестники (доля заполнения)")]
        [Tooltip("Амбиент без адреса.")]
        [Range(0f, 1f)] public double forewarn1At = 0.55;
        [Tooltip("Обязан назвать домен и место.")]
        [Range(0f, 1f)] public double forewarn2At = 0.80;
        [Tooltip("Названа близость — но никогда не точный день.")]
        [Range(0f, 1f)] public double forewarn3At = 0.95;

        [Header("Срабатывания за фазу")]
        [Min(0)] public int maxFiresPerDay = 1;
        [Tooltip("Ночью инциденты кучнее (US-11.1).")]
        [Min(0)] public int maxFiresPerNight = 2;

        [Header("Инвариант детерминизма")]
        [Tooltip("Меньше трёх накопителей — и система читается насквозь. Проверяется тестом.")]
        [Min(1)] public int minActiveTracks = 3;

        public PulseBalance ToConfig()
        {
            return new PulseBalance
            {
                DefaultThreshold = defaultThreshold,
                ChargeCapMultiplier = chargeCapMultiplier,
                Forewarn1At = forewarn1At,
                Forewarn2At = forewarn2At,
                Forewarn3At = forewarn3At,
                MaxFiresPerDay = maxFiresPerDay,
                MaxFiresPerNight = maxFiresPerNight,
                MinActiveTracks = minActiveTracks
            };
        }
    }
}
