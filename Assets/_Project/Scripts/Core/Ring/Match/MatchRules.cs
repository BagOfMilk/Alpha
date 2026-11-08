namespace Game.Core.Ring.Match
{
    /// <summary>Усі числа матчу в одному місці (TRPG_DESIGN §4). ПЛЕЙСХОЛДЕР: балансуються тут, а не по коду.</summary>
    public static class MatchRules
    {
        /// <summary>«Герой залу»: з якого ажіотажу «продати» дає ще +1 імпульсу. ПЛЕЙСХОЛДЕР (06.11.2026).</summary>
        public const int HeroOfCrowdHype = 7;
        /// <summary>«Камбек»: після скількох вирваних утримань — жетон реверсу й імпульс до порогу фінішера. ПЛЕЙСХОЛДЕР (06.11.2026).</summary>
        public const int ComebackKickouts = 2;

        /// <summary>«Сили» (TRPG_DESIGN §4.3, рішення 08.11.2026 разом із ланцюгом ініціативи §4.5). ПЛЕЙСХОЛДЕР.</summary>
        public const int MaxStamina = 10;
        public const int StartStamina = 10;
        /// <summary>Захисник після кожного прийому суперника відновлює стільки «Сил» (§4.5).</summary>
        public const int DefenderStaminaRegen = 1;
        /// <summary>«Перевести подих»: стільки «Сил» — і ініціатива переходить.</summary>
        public const int CatchBreathStamina = 3;
        /// <summary>«Сил» 0 — стільки мінус до кидків дій (крім променя надії).</summary>
        public const int WindedPenalty = 1;

        public const int MaxDamage = 12;
        public const int MaxMomentum = 10;
        public const int MaxHype = 10;
        /// <summary>Пороги штрафу ушкодження: «виснажений» (−1) і «на межі» (−2); зсуваються DamageThresholdShift.</summary>
        public const int ExhaustedDamage = 6;
        public const int EdgeDamage = 10;

        public const int BaseReversalTokens = 2;
        /// <summary>
        /// Імпульс для фінішера. Було 5 — за короткий матч (≈5 раундів) імпульсу набиралось у середньому ~2,5, фінішер ставав
        /// доступним лише в 20 % матчів кар'єри (власник 04.11.2026: «бот жодного разу не зробив успішний фінішер … це погано»).
        /// З 3 — доступний у ~66 %, проведений у ~52 % (охоронець FinisherReachTests). ПЛЕЙСХОЛДЕР.
        /// </summary>
        public const int FinisherMomentum = 3;
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

        public const int WarningsToDq = 3;

        /// <summary>
        /// Ліміт матчу в діях (§4.11, ПЛЕЙСХОЛДЕР): далі нічия. Зріз 03.11.2026 мав 20 раундів по ~4 дії; ланцюг рахує кожну дію.
        /// </summary>
        public const int ActionLimit = 40;
        /// <summary>Пізня стадія з цієї дії: −1 до «Вирватися» й «Терпіти» (втома, матч іде до фіналу).</summary>
        public const int LateStageRound = 24;
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

        /// <summary>Ціна дії в «Силах» (ПЛЕЙСХОЛДЕР, TRPG_DESIGN §4.3): удар 1, кидок і больовий 2, ризикований трюк 3, фінішер 4.</summary>
        public static int Cost(MatchActionId id)
        {
            switch (id)
            {
                case MatchActionId.Strike:
                case MatchActionId.Taunt:
                case MatchActionId.Dirty:
                case MatchActionId.Pin:
                case MatchActionId.Desperate:
                    return 1;
                case MatchActionId.Throw:
                case MatchActionId.Submission:
                    return 2;
                case MatchActionId.TopRope:
                    return 3;
                case MatchActionId.Finisher:
                    return 4;
                default:
                    return 0;   // встати, відновитися, напарник, перевести подих, промінь надії
            }
        }

        /// <summary>Ризикований трюк і фінішер вимагають повну ціну; решту можна робити й на нулі «Сил» (з −1 до кидка).</summary>
        public static bool NeedsFullStamina(MatchActionId id)
        {
            return id == MatchActionId.TopRope || id == MatchActionId.Finisher;
        }

        /// <summary>Великі прийоми: захисник може відповісти до кидка нападника (gdd §2.5).</summary>
        public static bool IsBig(MatchActionId id)
        {
            return id == MatchActionId.Throw || id == MatchActionId.TopRope || id == MatchActionId.Finisher;
        }
    }
}
