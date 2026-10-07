namespace Game.Gameplay.UI
{
    /// <summary>
    /// Рівень графіки (Поправка №21.1). Значення — індекси рівнів якості в
    /// <c>ProjectSettings/QualitySettings.asset</c> і асетах URP <c>Editor/GraphicsTiersSetup</c>.
    /// </summary>
    public enum GraphicsLevel
    {
        Low = 0,
        Medium = 1,
        High = 2
    }

    /// <summary>
    /// Автопідбір рівня графіки на першому запуску — чиста функція від того, що каже
    /// <c>SystemInfo</c>, без рушія, тож перевіряється тестами (Статут PERF-01).
    ///
    /// Цільова слабка машина (Поправка №21.1): вбудована відеокарта рівня UHD 620, 8 ГБ ОЗП,
    /// 1080p — стабільні 30 кадрів на Низькій. Сумнів трактуємо на користь швидкості:
    /// гравець підніме якість сам, а гру, що гальмує з першої хвилини, просто закриє.
    /// </summary>
    public static class GraphicsTierPicker
    {
        public static GraphicsLevel Pick(string deviceName, int graphicsMemoryMb, int systemMemoryMb, int processorCount)
        {
            if (LooksIntegrated(deviceName) || graphicsMemoryMb < 1500 || systemMemoryMb < 7000)
                return GraphicsLevel.Low;
            if (graphicsMemoryMb < 4000 || systemMemoryMb < 12000 || processorCount < 6)
                return GraphicsLevel.Medium;
            return GraphicsLevel.High;
        }

        /// <summary>
        /// Вбудована графіка за назвою пристрою: Intel HD/UHD/Iris і вбудовані Radeon
        /// («Radeon(TM) Graphics», Vega у процесорі). Дискретні Intel Arc — не вбудовані.
        /// </summary>
        public static bool LooksIntegrated(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName)) return false;
            string n = deviceName.ToLowerInvariant();
            if (n.Contains("intel"))
                return !n.Contains("arc");
            if (n.Contains("radeon") && (n.Contains("vega") || n.Contains("radeon(tm) graphics") || n.Contains("radeon graphics")))
                return true;
            return false;
        }
    }
}
