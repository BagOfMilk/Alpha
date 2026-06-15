using Game.Core.Characters;

namespace Game.Core.Companions
{
    /// <summary>
    /// Баентер — контекстные реплики между напарниками (US-9.6, минимум для среза):
    /// питается ценностными связями (близкие сходятся, несогласные пикируются).
    /// ПЛЕЙСХОЛДЕР-строки; письмо-ёмкий слой стадируется отдельно.
    /// </summary>
    public static class BanterPicker
    {
        public static string Pick(Companion a, Companion b, BondType bond)
        {
            if (a == null || b == null) return null;
            switch (bond)
            {
                case BondType.Kinship:
                    return $"{a.DisplayName} и {b.DisplayName} понимающе переглядываются — одной породы.";
                case BondType.Friction:
                    return $"{a.DisplayName} цедит колкость, {b.DisplayName} не остаётся в долгу.";
                default:
                    return $"{a.DisplayName} и {b.DisplayName} перекидываются парой ничего не значащих слов.";
            }
        }
    }
}
