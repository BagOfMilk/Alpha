using Game.Core.Characters;

namespace Game.Core.Companions
{
    /// <summary>
    /// Одна строка баентера: КЛЮЧ (R7 — Core отдаёт ключи, не текст) плюс id
    /// двух говорящих, чтобы текстовый слой (E3, UkrainianText) подставил
    /// «char.&lt;id&gt;» в шаблон реплики. Разговор идёт по ценностной связи
    /// (близкие сходятся, несогласные пикируются) — тот же принцип, что
    /// использует <see cref="RosterBonds"/> для ряби.
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
    /// Баентер — контекстные реплики между напарниками (US-9.6, порт B4,
    /// минимум для среза): питается теми же ценностными связями, что и рябь.
    /// Core отдаёт ключ «banter.kinship»/«banter.friction»/«banter.neutral»
    /// (§7 текстовой таблицы дополняет E3); письмо-ёмкий слой (много вариантов
    /// на связь) стадируется отдельно.
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
