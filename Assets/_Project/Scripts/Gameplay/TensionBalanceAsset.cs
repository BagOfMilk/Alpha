using Game.Core.Balance;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Числа скрытой шкалы «Напряжение» в инспекторе. Правятся в Play-режиме
    /// без перекомпиляции (US-18.3).
    ///
    /// Создание: ПКМ в Project → Create → Alpha → Balance → Напряжение.
    /// </summary>
    [CreateAssetMenu(fileName = "TensionBalance", menuName = "Alpha/Balance/Напряжение", order = 10)]
    public sealed class TensionBalanceAsset : ScriptableObject
    {
        [Header("Шкала")]
        [Tooltip("Потолок шкалы. Игроку не показывается никогда.")]
        public int max = 1000;
        [Tooltip("Границы полос: Спокойно / Ропот / Брожение / Накал / Излом.")]
        public int[] bandThresholds = { 200, 400, 600, 800 };

        [Header("Фоновый тик")]
        [Tooltip("Прирост в день по тиру города: хутор → село → слобода → городок.")]
        public double[] tierTickPerDay = { 1.0, 2.0, 4.0, 7.0 };
        [Tooltip("Множитель тика по Укладу: Вольница / Присмотр / Порядок / Затвор.")]
        public double[] orderTickMultiplier = { 1.25, 1.0, 0.85, 0.7 };

        [Header("Дренаж и события")]
        public int raidDelta = -80;
        public int raidCooldownDays = 10;
        public double templeDrainPerDay = -0.5;
        public double fortificationDrainPerDay = -0.3;
        public int bloodDeltaPerNode = 10;
        public int bloodCapPerExpedition = 50;
        [Tooltip("Окно на реакцию после входа в «Излом» до кризиса.")]
        public int crisisGraceDays = 3;

        public TensionBalance ToConfig()
        {
            return new TensionBalance
            {
                Max = max,
                BandThresholds = bandThresholds,
                TierTickPerDay = tierTickPerDay,
                OrderTickMultiplier = orderTickMultiplier,
                RaidDelta = raidDelta,
                RaidCooldownDays = raidCooldownDays,
                TempleDrainPerDay = templeDrainPerDay,
                FortificationDrainPerDay = fortificationDrainPerDay,
                BloodDeltaPerNode = bloodDeltaPerNode,
                BloodCapPerExpedition = bloodCapPerExpedition,
                CrisisGraceDays = crisisGraceDays

                // Белый список драйверов НАМЕРЕННО не выведен в инспектор:
                // это дизайн-решение уровня поправки к GDD, а не крутилка баланса.
            };
        }
    }
}
