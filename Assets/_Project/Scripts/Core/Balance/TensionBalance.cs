using System;
using Game.Core.Pressure;

namespace Game.Core.Balance
{
    /// <summary>
    /// Числа скрытой шкалы «Напряжение» и её белый список драйверов.
    ///
    /// ЯКОРЬ шкалы: хутор без единого действия игрока НЕ доходит до кризиса за всю
    /// кампанию. При тике 1/день и кампании 150–200 дней пассивный дрейф — около
    /// одной полосы. Значит кризис — это всегда выборы игрока плюс тир города,
    /// а не течение времени (гарантия US-1.3).
    /// </summary>
    [Serializable]
    public sealed class TensionBalance
    {
        public int Max = 1000;

        /// <summary>Границы полос: ниже первой — Спокойно, выше последней — Излом.</summary>
        public int[] BandThresholds = { 200, 400, 600, 800 };

        /// <summary>Фоновый тик по тиру города (хутор → городок).</summary>
        public double[] TierTickPerDay = { 1.0, 2.0, 4.0, 7.0 };

        /// <summary>Множитель тика по Укладу (Вольница → Затвор). Уклад не новый
        /// драйвер, а модулятор существующего — см. Поправку №3.5.</summary>
        public double[] OrderTickMultiplier = { 1.25, 1.0, 0.85, 0.7 };

        /// <summary>Кому позволено повышать. Всё остальное отклоняется.</summary>
        public TensionDriver[] AllowedRaising =
        {
            TensionDriver.CityTierTick,
            TensionDriver.QuestChoice,
            TensionDriver.ThreatOutcome,
            TensionDriver.PlaystyleBlood
        };

        /// <summary>Кому позволено понижать.</summary>
        public TensionDriver[] AllowedLowering =
        {
            TensionDriver.CouncilRaid,
            TensionDriver.CouncilEdict,
            TensionDriver.TempleAura,
            TensionDriver.Fortifications,
            TensionDriver.EventOutcome
        };

        /// <summary>Вес выборов в квестах: мелкий / крупный / чудовищный.</summary>
        public int ChoiceMinor = 15;
        public int ChoiceMajor = 40;
        public int ChoiceMonstrous = 60;

        public int RaidDelta = -80;
        public int RaidCooldownDays = 10;
        public double TempleDrainPerDay = -0.5;
        public double FortificationDrainPerDay = -0.3;
        public int BloodDeltaPerNode = 10;
        public int BloodCapPerExpedition = 50;

        /// <summary>Окно на реакцию после входа в «Излом» до кризиса.</summary>
        public int CrisisGraceDays = 3;

        public TensionBand BandFor(int value)
        {
            var t = BandThresholds;
            if (t == null || t.Length == 0) return TensionBand.Calm;
            for (int i = 0; i < t.Length; i++)
                if (value < t[i]) return (TensionBand)i;
            return (TensionBand)t.Length;
        }

        /// <summary>
        /// Белый список в действии: положительная дельта разрешена только
        /// повышающим драйверам, отрицательная — только понижающим.
        /// </summary>
        public bool IsAllowed(TensionDriver driver, double delta)
        {
            if (driver == TensionDriver.None) return false;
            if (delta > 0) return Contains(AllowedRaising, driver);
            if (delta < 0) return Contains(AllowedLowering, driver);
            return true;
        }

        /// <summary>Фоновый тик за день с учётом тира и Уклада.</summary>
        public double TierTick(int tier, int orderLevel)
        {
            double baseTick = Pick(TierTickPerDay, tier - 1, 1.0);
            double mult = Pick(OrderTickMultiplier, orderLevel, 1.0);
            return baseTick * mult;
        }

        private static bool Contains(TensionDriver[] list, TensionDriver d)
        {
            if (list == null) return false;
            for (int i = 0; i < list.Length; i++)
                if (list[i] == d) return true;
            return false;
        }

        private static double Pick(double[] arr, int index, double fallback)
        {
            if (arr == null || arr.Length == 0) return fallback;
            if (index < 0) index = 0;
            if (index >= arr.Length) index = arr.Length - 1;
            return arr[index];
        }
    }
}
