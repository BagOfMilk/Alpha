using System;
using Game.Core.Pressure;

namespace Game.Core.Balance
{
    /// <summary>
    /// Числа прихованої шкали «Напруга» і її білий список драйверів.
    ///
    /// ЯКІР шкали: хутір без жодної дії гравця НЕ доходить до кризи за всю
    /// кампанію. При тику 1/добу і кампанії 150–200 днів пасивний дрейф — близько
    /// однієї полоси. Отже криза — це завжди вибори гравця плюс тір міста,
    /// а не плин часу (гарантія US-1.3).
    /// </summary>
    [Serializable]
    public sealed class TensionBalance
    {
        public int Max = 1000;

        /// <summary>Межі полос: нижче першої — Спокій, вище останньої — Злам.</summary>
        public int[] BandThresholds = { 200, 400, 600, 800 };

        /// <summary>Фоновий тик за тіром міста (хутір → містечко).</summary>
        public double[] TierTickPerDay = { 1.0, 2.0, 4.0, 7.0 };

        /// <summary>Множник тика за Укладом (Вольниця → Затвор). Уклад не новий
        /// драйвер, а модулятор наявного — див. Поправку №3.5.</summary>
        public double[] OrderTickMultiplier = { 1.25, 1.0, 0.85, 0.7 };

        /// <summary>Кому дозволено підвищувати. Усе інше відхиляється.</summary>
        public TensionDriver[] AllowedRaising =
        {
            TensionDriver.CityTierTick,
            TensionDriver.QuestChoice,
            TensionDriver.ThreatOutcome,
            TensionDriver.PlaystyleBlood,
            TensionDriver.Hunger
        };

        /// <summary>Кому дозволено знижувати.</summary>
        public TensionDriver[] AllowedLowering =
        {
            TensionDriver.CouncilRaid,
            TensionDriver.CouncilEdict,
            TensionDriver.TempleAura,
            TensionDriver.Fortifications,
            TensionDriver.EventOutcome
        };

        /// <summary>Вага виборів у квестах: дрібний / великий / жахливий.</summary>
        public int ChoiceMinor = 15;
        public int ChoiceMajor = 40;
        public int ChoiceMonstrous = 60;

        public int RaidDelta = -80;
        public int RaidCooldownDays = 10;
        public double TempleDrainPerDay = -0.5;
        public double FortificationDrainPerDay = -0.3;
        public int BloodDeltaPerNode = 10;
        public int BloodCapPerExpedition = 50;

        /// <summary>Скільки Напруги додає один голодний день (Поправка №4).</summary>
        public int HungerDeltaPerDay = 8;

        public TensionBand BandFor(int value)
        {
            var t = BandThresholds;
            if (t == null || t.Length == 0) return TensionBand.Calm;
            for (int i = 0; i < t.Length; i++)
                if (value < t[i]) return (TensionBand)i;
            return (TensionBand)t.Length;
        }

        /// <summary>
        /// Білий список у дії: додатна дельта дозволена лише
        /// підвищувальним драйверам, від'ємна — лише знижувальним.
        /// </summary>
        public bool IsAllowed(TensionDriver driver, double delta)
        {
            if (driver == TensionDriver.None) return false;
            if (delta > 0) return Contains(AllowedRaising, driver);
            if (delta < 0) return Contains(AllowedLowering, driver);
            return true;
        }

        /// <summary>Фоновий тик за день з урахуванням тіра і Уклада.</summary>
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
