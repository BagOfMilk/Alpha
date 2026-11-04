namespace Game.Core.Ring.Match
{
    /// <summary>
    /// Підказка рушію від кар'єри: який саме прийом стоїть за дією (TRPG_DESIGN §5.4). Рушій прийомів не знає — лише ключ (для «повтору
    /// двічі поспіль»), додаток до кидка (травма зони, бонус прийому) і ажіотаж за чистий успіх. Без підказки (null/Default) — як було.
    /// </summary>
    public sealed class MoveHint
    {
        public static readonly MoveHint Default = new MoveHint();

        /// <summary>Ключ прийому; та сама дія з різними ключами повтором не вважається (−1 ажіотажу лише за той самий прийом).</summary>
        public string Key;
        /// <summary>Додаток до кидка нападника (травма зони −1, бонус прийому).</summary>
        public int RollMod;
        /// <summary>Додатковий ажіотаж за чистий успіх (10+).</summary>
        public int HypeOnStrong;
    }
}
