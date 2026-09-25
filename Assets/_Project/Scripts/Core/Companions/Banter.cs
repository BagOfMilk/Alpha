using Game.Core.Characters;

namespace Game.Core.Companions
{
    /// <summary>
    /// Один рядок баентера: КЛЮЧ (R7 — Core віддає ключі, не текст) плюс id
    /// двох тих, хто говорить, щоб текстовий шар (E3, UkrainianText) підставив
    /// «char.&lt;id&gt;» у шаблон репліки. Розмова йде за ціннісним зв'язком
    /// (близькі сходяться, незгодні пікіруються) — той самий принцип, що
    /// використовує <see cref="RosterBonds"/> для ряби.
    /// </summary>
    public readonly struct BanterLine
    {
        public readonly string Key;
        public readonly string SpeakerId;
        public readonly string OtherId;

        public BanterLine(string key, string speakerId, string otherId)
        {
            Key = key;
            SpeakerId = speakerId;
            OtherId = otherId;
        }
    }

    /// <summary>
    /// Баентер — контекстні репліки між напарниками (US-9.6, порт B4,
    /// мінімум для зрізу): живиться тими самими ціннісними зв'язками, що й рябь.
    /// Core віддає ключ «banter.kinship»/«banter.friction»/«banter.neutral»
    /// (§7 текстової таблиці доповнює E3); текстово-містку шар (багато варіантів
    /// на зв'язок) стадіюється окремо.
    /// </summary>
    public static class BanterPicker
    {
        public const string KinshipKey = "banter.kinship";
        public const string FrictionKey = "banter.friction";
        public const string NeutralKey = "banter.neutral";

        public static BanterLine? Pick(Companion a, Companion b, BondType bond)
        {
            if (a == null || b == null) return null;
            string key;
            switch (bond)
            {
                case BondType.Kinship: key = KinshipKey; break;
                case BondType.Friction: key = FrictionKey; break;
                default: key = NeutralKey; break;
            }
            return new BanterLine(key, a.Id, b.Id);
        }
    }
}
