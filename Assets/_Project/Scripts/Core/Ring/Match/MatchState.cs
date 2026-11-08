using System.Collections.Generic;

namespace Game.Core.Ring.Match
{
    /// <summary>Борець у матчі (TRPG_DESIGN §4.3). Поля відкриті для читання UI і тестів; міняє їх лише рушій.</summary>
    public sealed class Fighter
    {
        public MatchSide Side;
        public WrestlerStats Stats;
        public int Damage;
        public int Momentum;
        public Posture Posture;
        /// <summary>«Сили» 0–10: гальмо ланцюга ініціативи (кожен прийом коштує, захисник відновлює 1 після прийому суперника).</summary>
        public int Stamina;
        public int ReversalTokens;
        /// <summary>Вирвані утримання: кожне — −1 до наступного «Вирватися».</summary>
        public int KickoutsDone;
        /// <summary>Скільки разів його накривали і скільки разів він «терпів» больовий: перше — з бонусом свіжого (MatchRules.FirstPinBonus / FirstHoldBonus).</summary>
        public int PinsFaced, HoldsFaced;
        public int Warnings;
        public bool DesperateUsed;
        /// <summary>«Промінь надії» уже використано (раз за матч на бік).</summary>
        public bool HopeUsed;
        /// <summary>Перки з дією «раз за матч»: уже спрацювали.</summary>
        public bool ComebackGiven, BribeUsed, DirtyCoverUsed, ImproviseUsed;
        /// <summary>Уже виводив бій за ринг у цьому матчі (ШІ виводить щонайбільше раз).</summary>
        public bool LeftRing;

        public bool IsUpright { get { return Posture != Posture.Down; } }
    }

    /// <summary>Повний стан матчу. Єдине джерело правди для показу.</summary>
    public sealed class MatchState
    {
        public Fighter Player;
        public Fighter Rival;
        public int Hype;
        public bool RefereeDistracted;
        /// <summary>Номер наступної дії матчу (від 1; ліміт — MatchSetup.RoundLimit). Назва лишилась від раундів зрізу 03.11.2026.</summary>
        public int Round = 1;
        /// <summary>Хто виграв стартовий Обмін.</summary>
        public MatchSide FirstActor;
        /// <summary>У кого ініціатива — той і діє.</summary>
        public MatchSide CurrentActor;
        /// <summary>Скільки дій поспіль зробив нинішній власник ініціативи.</summary>
        public int Chain;
        /// <summary>Скільки разів ініціатива переходила (охоронець «ланцюг не нескінченний»).</summary>
        public int InitiativeChanges;
        public bool AllyUsed;
        /// <summary>
        /// Пара за рингом (рішення власника 08.11.2026: бій за рингом лишається в ітерації 1 склейкою — пара або на рингу, або за
        /// рингом, разом; вихід і повернення — окремим планом без бігу).
        /// </summary>
        public bool Outside;
        /// <summary>Відлік рефері за рингом (0–10); у матчі «без DQ» не йде. Скидається, щойно пара повернулась.</summary>
        public int OutsideCount;
        /// <summary>Остання дія в поточному ланцюгу (скидається, коли ініціатива переходить).</summary>
        public bool HasLastAction;
        public MatchActionId LastAction;
        /// <summary>Ключ прийому останньої дії (кар'єра: два різні прийоми з тією самою дією повтором не вважаються); null — без ключа.</summary>
        public string LastMoveKey;
        public int PlayerDirtyMoves;
        public int PlayerSells;
        public int PlayerKickoutsAtTwo;
        public bool PlayerUsedAlly;
        public readonly List<MatchEvent> Log = new List<MatchEvent>();

        public MatchSide Initiative { get { return CurrentActor; } }
        public Fighter Get(MatchSide s) { return s == MatchSide.Player ? Player : Rival; }
        public Fighter Actor { get { return Get(CurrentActor); } }
        public Fighter Other { get { return Get(MatchEngine.Opp(CurrentActor)); } }
    }
}
