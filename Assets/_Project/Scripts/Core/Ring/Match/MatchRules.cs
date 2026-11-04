namespace Game.Core.Ring.Match
{
    /// <summary>Усі числа матчу в одному місці (gdd §2). ПЛЕЙСХОЛДЕР: балансуються тут, а не по коду.</summary>
    public static class MatchRules
    {
        public const int ApPerTurn = 3;
        public const int MaxCarryAp = 1;
        public const int StunApPenalty = 1;

        public const int MaxDamage = 12;
        public const int MaxMomentum = 10;
        public const int MaxHype = 10;
        /// <summary>Пороги штрафу ушкодження: «виснажений» (−1) і «на межі» (−2); зсуваються DamageThresholdShift.</summary>
        public const int ExhaustedDamage = 6;
        public const int EdgeDamage = 10;

        public const int BaseReversalTokens = 2;
        public const int FinisherMomentum = 5;
        public const int FinisherHype = 4;
        public const int FinisherTargetDamage = 6;
        public const int DesperateMaxMomentum = 2;
        public const int DesperateMinDamage = 6;
        /// <summary>
        /// Мінімальне ушкодження суперника для «Утримання» і «Больового»: утримання — лише після накопиченої переваги
        /// (Superstar Pro Wrestling: поріг очок; Wrasslin': спершу ушкодження). Без порогу перше ж утримання вигравало
        /// ~42 % спроб, матч тривав ~5 раундів (gdd §2.6, баланс 03.11.2026).
        /// </summary>
        public const int PinMinDamage = 4;
        /// <summary>Основа захисних кидків «Вирватися» й «Терпіти»: свіжий борець майже не програє з першого покриття.</summary>
        public const int DefenseBase = 2;

        public const int OutsideCountStep = 3;
        public const int OutsideCountLimit = 10;
        public const int WarningsToDq = 3;

        public const int LateStageRound = 12;
        public const int LateStagePenalty = 1;

        /// <summary>Реверс фінішера і штрафи «Вирватися» після фінішера (10+ / 7–9).</summary>
        public const int ReverseVsFinisherPenalty = -2;
        public const int FinisherStrongPinPenalty = 3;
        public const int FinisherPartialPinPenalty = 2;
        public const int SubmissionStrongPenalty = 2;
        public const int SubmissionPartialPenalty = 1;

        /// <summary>Пороги ШІ (gdd §2.8).</summary>
        public const int AiPinDamage = 8;
        public const int AiSubmissionDamage = 6;
        public const int AiReverseDamage = 6;

        /// <summary>
        /// «Промінь надії» (ПЛЕЙСХОЛДЕР): відстаючий бік — його ушкодження не менше HopeMinDamage і на HopeGap більше за суперника —
        /// раз за матч може зробити великий прийом, що ніколи не завершує матч (без утримання й больового, суддя не дивиться).
        /// </summary>
        public const int HopeGap = 4;
        public const int HopeMinDamage = 6;

        public static int Cost(MatchActionId id)
        {
            switch (id)
            {
                case MatchActionId.Throw:
                case MatchActionId.TopRope:
                case MatchActionId.Submission:
                case MatchActionId.Desperate:
                case MatchActionId.HopeSpot:
                    return 2;
                case MatchActionId.Finisher:
                    return 3;
                case MatchActionId.CatchBreath:
                    return 0;
                default:
                    return 1;
            }
        }

        /// <summary>Великі прийоми: захисник може відповісти до кидка нападника (gdd §2.5).</summary>
        public static bool IsBig(MatchActionId id)
        {
            return id == MatchActionId.Throw || id == MatchActionId.TopRope || id == MatchActionId.Finisher;
        }
    }
}
