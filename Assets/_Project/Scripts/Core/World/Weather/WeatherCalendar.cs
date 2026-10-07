using System;
using Game.Core.Balance;

namespace Game.Core.World
{
    /// <summary>
    /// Погода як чиста функція від номера доби (Поправка №21.2).
    ///
    /// ЯКІР: стан неба на добу — один на обидві фази.
    /// СПОЖИВАЧІ: ферми (їжа), вилазки (здобич і доби тихого підходу), нічний
    /// накопичувач (злодії люблять туман), дальні постріли в бою.
    /// СИГНАЛ: слово в шапці з прогнозом на завтра і фонова репліка, коли погода
    /// змінилась.
    ///
    /// Чому функція, а не стан: її не треба зберігати (сейв відновлює добу —
    /// і з нею погоду), не треба кроку конвеєра і не можна розсинхронізувати.
    /// Випадковості немає (інваріант 1): зважений вибір за хешем доби, тож
    /// прогноз на завтра — правда, а не здогад.
    /// </summary>
    public static class WeatherCalendar
    {
        public static WeatherKind KindFor(int day, WeatherBalance balance)
        {
            if (balance == null || !balance.Enabled || day < 1) return WeatherKind.Clear;

            if (balance.Overrides != null)
            {
                foreach (var entry in balance.Overrides)
                    if (entry.Day == day) return entry.Kind;
            }

            // Перша година — авторський спокійний календар: гравець вчиться
            // місту, а не погоді, і темп відкриття від неба не залежить.
            if (day <= balance.CalmOpeningDays)
                return (Mix(day, balance.Salt) & 1) == 0 ? WeatherKind.Clear : WeatherKind.Overcast;

            var weights = day >= balance.AutumnFromDay ? balance.AutumnWeights : balance.SummerWeights;
            return Pick(weights, Mix(day, balance.Salt));
        }

        /// <summary>Сьогодні і завтра — обидва точно (детермінізм робить прогноз чесним).</summary>
        public static (WeatherKind today, WeatherKind tomorrow) Forecast(int day, WeatherBalance balance) =>
            (KindFor(day, balance), KindFor(day + 1, balance));

        private static WeatherKind Pick(int[] weights, uint hash)
        {
            if (weights == null || weights.Length == 0) return WeatherKind.Clear;

            int total = 0;
            foreach (int w in weights) total += Math.Max(0, w);
            if (total <= 0) return WeatherKind.Clear;

            int roll = (int)(hash % (uint)total);
            for (int i = 0; i < weights.Length; i++)
            {
                roll -= Math.Max(0, weights[i]);
                if (roll < 0) return (WeatherKind)i;
            }
            return WeatherKind.Clear;
        }

        /// <summary>splitmix32-подібне перемішування: сусідні доби не корелюють.</summary>
        private static uint Mix(int day, int salt)
        {
            unchecked
            {
                uint x = (uint)day * 0x9E3779B9u ^ (uint)salt;
                x ^= x >> 16;
                x *= 0x85EBCA6Bu;
                x ^= x >> 13;
                x *= 0xC2B2AE35u;
                x ^= x >> 16;
                return x;
            }
        }
    }
}
