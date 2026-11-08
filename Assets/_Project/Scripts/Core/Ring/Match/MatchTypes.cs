using System.Collections.Generic;

namespace Game.Core.Ring.Match
{
    public enum MatchSide { Player = 0, Rival = 1 }

    public enum Posture { Standing = 0, Down = 1, Stunned = 2 }

    public enum MatchPhase { AwaitAction = 0, AwaitReaction = 1, AwaitBoost = 2, Over = 3 }

    public enum ReactionChoice { Sell = 0, Reverse = 1 }

    /// <summary>
    /// Постановка прийому (рішення власника 08.11.2026, TRPG_DESIGN §4.4/§4.12): усе — у центрі рингу; прийом із канатів чи кута
    /// показується склейкою кадру (план нападника на канаті/куті → стрибок → приземлення в центр). Рушію байдуже — лише для показу.
    /// </summary>
    public enum MoveStaging { Center = 0, Ropes = 1, Corner = 2 }

    /// <summary>
    /// Дії матчу (TRPG_DESIGN §4.6). Значення стабільні — на них посилаються UI (ключі «action.N») і журнал. 08.11.2026 прибрано
    /// 2 «У канати», 3 «У кут», 12 «Вийти з рингу», 13 «Повернутися» — усе в центрі рингу (§4.4); номери не перевикористовувати.
    /// </summary>
    public enum MatchActionId
    {
        Strike = 0, Throw = 1,
        /// <summary>«Ризикований трюк» (§4.6): стрибок із канатів чи кута, показ склейкою; кута для нього більше не треба.</summary>
        TopRope = 4,
        Taunt = 5, Dirty = 6, Submission = 7, Pin = 8, Finisher = 9, Desperate = 10, GetUp = 11,
        Recover = 14, Ally = 15,
        /// <summary>«Перевести подих»: +«Сили», ініціатива переходить до суперника.</summary>
        CatchBreath = 16,
        /// <summary>«Промінь надії» (кар'єра): великий прийом слабшого, що не може закінчити матч. Не входить до GetAvailableActions —
        /// пропонується окремо (Describe/HopeSpotAvailable), доки матч не ввімкнув MatchSetup.HopeSpotEnabled.</summary>
        HopeSpot = 17
    }

    /// <summary>Чому дія недоступна. Значення стабільні (ключі «unavail.N»); 7–10, 20, 21 — зони рингу, прибрані 08.11.2026.</summary>
    public enum UnavailableReason
    {
        None = 0, WrongPhase = 1,
        /// <summary>Замало «Сил» на ризикований трюк чи фінішер (решту можна робити й на нулі — з −1 до кидка).</summary>
        NotEnoughStamina = 2,
        ActorDown = 3, TargetNotStanding = 4, TargetNotDown = 5,
        /// <summary>«Дражнити» — коли суперник лежить або приголомшений.</summary>
        TargetNotDownOrStunned = 6,
        NeedImpulse = 11, NeedHype = 12, TargetNotHurt = 13, ImpulseTooHigh = 14,
        NotExhausted = 15, AlreadyUsed = 16, NoAlly = 17, AllyForbidden = 18, NotLying = 19,
        NothingToRecover = 22, NotFarBehind = 23,
        /// <summary>Щойно накривав: утримати знову можна лише після іншого прийому.</summary>
        JustPinned = 24
    }

    /// <summary>
    /// Подія журналу. TurnEnd — «ініціатива переходить» (Actor — той, хто її втратив); Exchange — Обмін на старті й після реверсу 7–9;
    /// Flourish — вдале QTE (лише видовище: +1 ажіотажу, кидків не чіпає). Значення стабільні.
    /// </summary>
    public enum MatchEventKind { Exchange = 0, Action, Sell, Reverse, Kickout, Endure, Rescue, TurnEnd, MatchEnd, Flourish }

    /// <summary>Наслідки подій. 6, 9, 11 (зона, відлік за рингом, AP) прибрано 08.11.2026 разом із зонами й очками дій.</summary>
    public enum MatchEffectKind
    {
        Damage = 0, Momentum = 1, Hype = 2, Down = 3, Stand = 4, Stunned = 5, Warning = 7, RefereeDistracted = 8, Token = 10,
        /// <summary>«Сили» (ускладнення 7–9 удару, «Перевести подих»).</summary>
        Stamina = 12
    }

    /// <summary>Один наслідок події: хто, що, на скільки.</summary>
    public struct MatchEffect
    {
        public MatchEffectKind Kind;
        public MatchSide Who;
        public int Amount;
        public MatchEffect(MatchEffectKind kind, MatchSide who, int amount) { Kind = kind; Who = who; Amount = amount; }
    }

    /// <summary>Запис журналу матчу — для UI (стрічка, кидки), показу і тестів. Текст будує показ, не ядро.</summary>
    public sealed class MatchEvent
    {
        public int Seq;
        /// <summary>Номер дії матчу (з 08.11.2026 ланцюг ініціативи: кожна дія — +1; раундів немає).</summary>
        public int Round;
        public MatchEventKind Kind;
        public MatchSide Actor;
        public MatchActionId Action;
        public bool HasRoll;
        /// <summary>Сума 2d6 до модифікатора.</summary>
        public int Roll;
        public int Modifier;
        /// <summary>Roll + Modifier + докрутка.</summary>
        public int Total;
        public RollTier Tier;
        public int BoostSpent;
        /// <summary>Обмін: підсумок суперника (Total — гравця).</summary>
        public int OtherTotal;
        /// <summary>Перк з дією, що змінив цю подію (показ: «Імпровізація», «Підкуплений суддя»); null — жоден.</summary>
        public string Perk;
        /// <summary>Ключ прийому кар'єри (каталог), що стоїть за дією; null — без прийому (дія рушія, ШІ без підказки).</summary>
        public string MoveKey;
        /// <summary>Постановка прийому для показу: центр або склейка з канатів/кута.</summary>
        public MoveStaging Staging;
        /// <summary>Місце дії в ланцюгу ініціативи (1 — перша дія того, хто щойно взяв ініціативу).</summary>
        public int ChainIndex;
        public List<MatchEffect> Effects = new List<MatchEffect>();

        public int EffectSum(MatchEffectKind kind, MatchSide who)
        {
            int s = 0;
            for (int i = 0; i < Effects.Count; i++)
                if (Effects[i].Kind == kind && Effects[i].Who == who) s += Effects[i].Amount;
            return s;
        }
    }

    /// <summary>Рядок списку дій для UI: ціна в «Силах», атрибут, доступність з причиною і шанси до кидка (як у XCOM — завжди видно).</summary>
    public sealed class ActionOption
    {
        public MatchActionId Id;
        /// <summary>Ціна в «Силах» (TRPG_DESIGN §4.3).</summary>
        public int Cost;
        public bool HasAttr;
        public RingAttr Attr;
        public bool Available;
        public UnavailableReason Reason;
        /// <summary>Великий прийом — захисник відповідає (продати/реверс).</summary>
        public bool IsBig;
        public bool HasRoll;
        /// <summary>Чий кидок показують шанси: свій чи суперника (для «Утримання» — його «Вирватися»).</summary>
        public bool RollIsOpponents;
        public int Modifier;
        /// <summary>«Сил» 0 — у модифікаторі вже −1.</summary>
        public bool Winded;
        /// <summary>Шанси у тридцять шостих: [провал ≤6, 7–9, 10+] (для кидка суперника — його ступені).</summary>
        public int[] Chances;
    }

    /// <summary>Кидок нападника відомий, наслідки ще ні — тут гравець може докрутити імпульсом.</summary>
    public sealed class PendingRoll
    {
        public MatchSide Actor;
        public MatchActionId Action;
        public int Roll;
        public int Modifier;
        public int Total;
        public RollTier Tier;
        public int Impulse;
        /// <summary>Скільки імпульсу треба до 7–9 / до 10+ (0 — вже досягнуто або недосяжно).</summary>
        public int PointsToPartial;
        public int PointsToStrong;
        public bool CanBoost;
    }

    /// <summary>Захисник обирає до кидка нападника (TRPG_DESIGN §4.7).</summary>
    public sealed class PendingReaction
    {
        public MatchSide Attacker;
        public MatchSide Defender;
        public MatchActionId Action;
        public int ReverseModifier;
        public int[] ReverseChances;
    }
}
