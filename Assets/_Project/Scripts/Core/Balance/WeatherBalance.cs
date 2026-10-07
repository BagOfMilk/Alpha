using System;
using Game.Core.World;

namespace Game.Core.Balance
{
    /// <summary>
    /// Числа погоди (Поправка №21.2). Усі — ПЛЕЙСХОЛДЕРИ.
    ///
    /// Таблиці індексуються <see cref="WeatherKind"/>: Clear, Overcast, Rain, Fog, Storm.
    /// Погода лише МОДУЛЮЄ наявні системи — свого драйвера Напруги в неї немає
    /// (інваріант 5); тиск доходить до Напруги тільки через голод і нічний накопичувач.
    /// </summary>
    [Serializable]
    public sealed class WeatherBalance
    {
        /// <summary>Вимкнено — завжди ясно, усі множники нейтральні.</summary>
        public bool Enabled = true;

        /// <summary>Сіль хешу доби: інша сіль — інший календар тієї самої довжини.</summary>
        public int Salt = 0x51A7;

        /// <summary>Доби відкриття лише ясні або хмарні (перша година — про місто, не про небо).</summary>
        public int CalmOpeningDays = 7;

        /// <summary>З цієї доби діє осінній розподіл.</summary>
        public int AutumnFromDay = 46;

        /// <summary>Ваги станів улітку: ясно, хмарно, дощ, туман, буря.</summary>
        public int[] SummerWeights = { 40, 25, 20, 8, 7 };

        /// <summary>Ваги станів восени.</summary>
        public int[] AutumnWeights = { 20, 30, 25, 15, 10 };

        /// <summary>Сценарні доби: тут погода задана руками і перекриває календар.</summary>
        public WeatherOverride[] Overrides = Array.Empty<WeatherOverride>();

        /// <summary>Множник їжі з ферм: дощ поливає, буря б'є врожай.</summary>
        public double[] FoodMultiplier = { 1.0, 1.0, 1.2, 1.0, 0.6 };

        /// <summary>Множник здобичі вилазки в день відходу.</summary>
        public double[] ExpeditionYieldMultiplier = { 1.0, 1.0, 0.9, 1.0, 0.75 };

        /// <summary>Множник щоденного приросту нічного накопичувача: злодії люблять туман і бурю.</summary>
        public double[] NightRateMultiplier = { 1.0, 1.0, 1.0, 1.3, 1.3 };

        /// <summary>Поправка влучності дальніх атак у відсоткових пунктах.</summary>
        public int[] RangedAccuracyDelta = { 0, 0, -5, -15, -10 };

        public double Food(WeatherKind kind) => Enabled ? At(FoodMultiplier, kind, 1.0) : 1.0;
        public double ExpeditionYield(WeatherKind kind) => Enabled ? At(ExpeditionYieldMultiplier, kind, 1.0) : 1.0;
        public double NightRate(WeatherKind kind) => Enabled ? At(NightRateMultiplier, kind, 1.0) : 1.0;
        public int RangedAccuracy(WeatherKind kind) => Enabled ? At(RangedAccuracyDelta, kind, 0) : 0;

        private static T At<T>(T[] table, WeatherKind kind, T fallback)
        {
            int i = (int)kind;
            return table != null && i >= 0 && i < table.Length ? table[i] : fallback;
        }
    }

    /// <summary>Погода, задана руками на конкретну добу.</summary>
    [Serializable]
    public struct WeatherOverride
    {
        public int Day;
        public WeatherKind Kind;

        public WeatherOverride(int day, WeatherKind kind)
        {
            Day = day;
            Kind = kind;
        }
    }
}
