using System.Collections.Generic;

namespace Game.Core.Companions
{
    /// <summary>Тип зв'язку двох напарників за цінностями (US-9.6, порт B4).</summary>
    public enum BondType
    {
        Friction = -1, // конфліктують за протилежними цінностями
        Neutral = 0,
        Kinship = 1    // сходяться за спільними цінностями
    }

    /// <summary>
    /// Легкий емерджентний шар зв'язків за цінностями (US-9.6): НЕ відстежувана
    /// NxN-матриця, а обчислення за вже готовими даними — тегами цінностей з
    /// <see cref="Game.Core.Characters.Traits.TraitDefinition.Values"/> (та сама
    /// таблиця, яку агрегатор статів читає через <c>TraitSlots.Values</c>).
    /// Спільний тег зближує (+), протилежна пара сварить (−); підсумок —
    /// Kinship/Neutral/Friction.
    /// </summary>
    public sealed class ValueSystem
    {
        private readonly HashSet<string> _opposed = new HashSet<string>();

        /// <summary>Оголошує пару цінностей протилежними (контент).</summary>
        public ValueSystem Oppose(string a, string b)
        {
            if (!string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b)) _opposed.Add(Key(a, b));
            return this;
        }

        public bool AreOpposed(string a, string b) => _opposed.Contains(Key(a, b));

        /// <summary>Рахунок зв'язку: +за кожну спільну цінність, −за кожну протилежну пару.</summary>
        public int Score(IEnumerable<string> aValues, IEnumerable<string> bValues)
        {
            var a = ToSet(aValues);
            var b = ToSet(bValues);
            if (a.Count == 0 || b.Count == 0) return 0;

            int score = 0;
            foreach (var v in a)
                if (b.Contains(v)) score++; // спільна цінність
            foreach (var x in a)
                foreach (var y in b)
                    if (AreOpposed(x, y)) score--; // протилежні цінності
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

    /// <summary>Канонічні цінності зрізу (ПЛЕЙСХОЛДЕР-флейвор) + їхні протилежності.</summary>
    public static class DefaultValues
    {
        public const string Order = "order";       // порядок
        public const string Freedom = "freedom";   // свобода
        public const string Mercy = "mercy";       // милосердя
        public const string Ruthless = "ruthless"; // жорсткість
        public const string Duty = "duty";         // обов'язок
        public const string Profit = "profit";     // вигода

        public static ValueSystem System() => new ValueSystem()
            .Oppose(Order, Freedom)
            .Oppose(Mercy, Ruthless);
    }
}
