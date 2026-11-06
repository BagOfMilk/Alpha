using System.Collections.Generic;

namespace Game.Core.Ring.Match
{
    /// <summary>Борець у матчі (gdd §2.1). Поля відкриті для читання UI і тестів; міняє їх лише рушій.</summary>
    public sealed class Fighter
    {
        public MatchSide Side;
        public WrestlerStats Stats;
        public int Damage;
        public int Momentum;
        public Posture Posture;
        public Zone Zone;
        public int ReversalTokens;
        /// <summary>Вирвані утримання: кожне — −1 до наступного «Вирватися».</summary>
        public int KickoutsDone;
        public int Warnings;
        public bool DesperateUsed;
        /// <summary>«Промінь надії» уже використано (раз за матч на бік).</summary>
        public bool HopeUsed;
        /// <summary>Перенесений на наступний хід AP (до 1).</summary>
        public int CarryAp;
        /// <summary>«У канати» вдався з бонусом: наступний удар/кидок +1 (діє до кінця ходу).</summary>
        public bool NextHitBonus;
        /// <summary>Зона «канати/кут» лишилась з минулого ходу — на кінці цього ходу скинеться до центру.</summary>
        public bool ZoneCarried;
        /// <summary>Перки з дією «раз за матч»: уже спрацювали.</summary>
        public bool ComebackGiven, BribeUsed, DirtyCoverUsed, ImproviseUsed;

        public bool IsUpright { get { return Posture != Posture.Down; } }
    }

    /// <summary>Повний стан матчу (gdd §2.1). Єдине джерело правди для показу.</summary>
    public sealed class MatchState
    {
        public Fighter Player;
        public Fighter Rival;
        public int Hype;
        public bool RefereeDistracted;
        public int OutsideCount;
        public int Round = 1;
        /// <summary>0 — ходить перший у раунді, 1 — другий.</summary>
        public int TurnInRound;
        public MatchSide FirstActor;
        public MatchSide CurrentActor;
        public int Ap;
        public bool AllyUsed;
        public bool HasLastAction;
        public MatchActionId LastAction;
        /// <summary>Ключ прийому останньої дії (кар'єра: два різні прийоми з тією самою дією повтором не вважаються); null — без ключа.</summary>
        public string LastMoveKey;
        public int PlayerDirtyMoves;
        public int PlayerSells;
        public int PlayerKickoutsAtTwo;
        public bool PlayerUsedAlly;
        public readonly List<MatchEvent> Log = new List<MatchEvent>();

        public Fighter Get(MatchSide s) { return s == MatchSide.Player ? Player : Rival; }
        public Fighter Actor { get { return Get(CurrentActor); } }
        public Fighter Other { get { return Get(MatchEngine.Opp(CurrentActor)); } }
    }
}
