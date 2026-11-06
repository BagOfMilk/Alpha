using System.Collections.Generic;

namespace Game.Core.Ring.Match
{
    public enum MatchSide { Player = 0, Rival = 1 }

    public enum Posture { Standing = 0, Down = 1, Stunned = 2 }

    public enum Zone { Center = 0, Ropes = 1, Corner = 2, Outside = 3 }

    public enum MatchPhase { AwaitAction = 0, AwaitReaction = 1, AwaitBoost = 2, Over = 3 }

    public enum ReactionChoice { Sell = 0, Reverse = 1 }

    /// <summary>Дії матчу (gdd §2.3). Значення стабільні — на них посилаються UI і журнал.</summary>
    public enum MatchActionId
    {
        Strike = 0, Throw = 1, IntoRopes = 2, IntoCorner = 3, TopRope = 4, Taunt = 5, Dirty = 6,
        Submission = 7, Pin = 8, Finisher = 9, Desperate = 10, GetUp = 11, LeaveRing = 12, ReturnToRing = 13,
        Recover = 14, Ally = 15, CatchBreath = 16,
        /// <summary>«Промінь надії» (кар'єра): великий прийом слабшого, що не може закінчити матч. Не входить до GetAvailableActions —
        /// пропонується окремо (Describe/HopeSpotAvailable), доки матч не ввімкнув MatchSetup.HopeSpotEnabled.</summary>
        HopeSpot = 17
    }

    public enum UnavailableReason
    {
        None = 0, WrongPhase, NotEnoughAp, ActorDown, TargetNotStanding, TargetNotDown, TargetNotDownOrOutside,
        NotSameZone, NotInRing, NotInCorner, InRopes, NeedImpulse, NeedHype, TargetNotHurt, ImpulseTooHigh,
        NotExhausted, AlreadyUsed, NoAlly, AllyForbidden, NotLying, NotOutside, AlreadyOutside, NothingToRecover,
        NotFarBehind
    }

    public enum MatchEventKind { Exchange = 0, Action, Sell, Reverse, Kickout, Endure, Rescue, TurnEnd, MatchEnd }

    public enum MatchEffectKind
    {
        Damage = 0, Momentum, Hype, Down, Stand, Stunned, ZoneChange, Warning, RefereeDistracted, Count, Token, ApChange
    }

    /// <summary>Один наслідок події: хто, що, на скільки (Amount для зони — її число).</summary>
    public struct MatchEffect
    {
        public MatchEffectKind Kind;
        public MatchSide Who;
        public int Amount;
        public MatchEffect(MatchEffectKind kind, MatchSide who, int amount) { Kind = kind; Who = who; Amount = amount; }
    }

    /// <summary>Запис журналу матчу — для UI (стрічка, кидки) і тестів. Текст будує показ, не ядро.</summary>
    public sealed class MatchEvent
    {
        public int Seq;
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
        /// <summary>Перк з дією, що змінив цю подію (показ: «Імпровізація», «Підкуплений суддя»); null — жоден.</summary>
        public string Perk;
        public List<MatchEffect> Effects = new List<MatchEffect>();

        public int EffectSum(MatchEffectKind kind, MatchSide who)
        {
            int s = 0;
            for (int i = 0; i < Effects.Count; i++)
                if (Effects[i].Kind == kind && Effects[i].Who == who) s += Effects[i].Amount;
            return s;
        }
    }

    /// <summary>Рядок списку дій для UI: ціна, атрибут, доступність з причиною і шанси до кидка.</summary>
    public sealed class ActionOption
    {
        public MatchActionId Id;
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

    /// <summary>Захисник обирає до кидка нападника (gdd §2.5).</summary>
    public sealed class PendingReaction
    {
        public MatchSide Attacker;
        public MatchSide Defender;
        public MatchActionId Action;
        public int ReverseModifier;
        public int[] ReverseChances;
    }
}
