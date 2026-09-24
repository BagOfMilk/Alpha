using System.Collections.Generic;

namespace Game.Core.Companions
{
    /// <summary>Тип связи двух напарников по ценностям (US-9.6, порт B4).</summary>
    public enum BondType
    {
        Friction = -1, // конфликтуют по противоположным ценностям
        Neutral = 0,
        Kinship = 1    // сходятся по общим ценностям
    }

    /// <summary>
    /// Лёгкий эмерджентный слой связей по ценностям (US-9.6): НЕ трекаемая
    /// NxN-матрица, а вычисление по уже готовым данным — тегам ценностей из
    /// <see cref="Game.Core.Characters.Traits.TraitDefinition.Values"/> (та же
    /// таблица, что и агрегатор статов читает через <c>TraitSlots.Values</c>).
    /// Общий тег сближает (+), противоположная пара ссорит (−); итог —
    /// Kinship/Neutral/Friction.
    /// </summary>
    public sealed class ValueSystem
    {
        private readonly HashSet<string> _opposed = new HashSet<string>();

        /// <summary>Объявляет пару ценностей противоположными (контент).</summary>
        public ValueSystem Oppose(string a, string b)
        {
            if (!string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b)) _opposed.Add(Key(a, b));
            return this;
        }

        public bool AreOpposed(string a, string b) => _opposed.Contains(Key(a, b));

        /// <summary>Счёт связи: +за каждую общую ценность, −за каждую противоположную пару.</summary>
        public int Score(IEnumerable<string> aValues, IEnumerable<string> bValues)
        {
            var a = ToSet(aValues);
            var b = ToSet(bValues);
            if (a.Count == 0 || b.Count == 0) return 0;

            int score = 0;
            foreach (var v in a)
                if (b.Contains(v)) score++; // общая ценность
            foreach (var x in a)
                foreach (var y in b)
                    if (AreOpposed(x, y)) score--; // противоположные ценности
            return score;
        }

        public BondType Bond(IEnumerable<string> aValues, IEnumerable<string> bValues)
        {
            int s = Score(aValues, bValues);
            return s > 0 ? BondType.Kinship : (s < 0 ? BondType.Friction : BondType.Neutral);
        }

        private static HashSet<string> ToSet(IEnumerable<string> values)
        {
            var set = new HashSet<string>();
            if (values != null) foreach (var v in values) if (!string.IsNullOrEmpty(v)) set.Add(v);
            return set;
        }

        private static string Key(string a, string b)
            => string.CompareOrdinal(a, b) <= 0 ? a + "|" + b : b + "|" + a;
    }

    /// <summary>Канонические ценности среза (ПЛЕЙСХОЛДЕР-флавор) + их противоположности.</summary>
    public static class DefaultValues
    {
        public const string Order = "order";       // порядок
        public const string Freedom = "freedom";   // свобода
        public const string Mercy = "mercy";       // милосердие
        public const string Ruthless = "ruthless"; // жёсткость
        public const string Duty = "duty";         // долг
        public const string Profit = "profit";     // выгода

        public static ValueSystem System() => new ValueSystem()
            .Oppose(Order, Freedom)
            .Oppose(Mercy, Ruthless);
    }
}
