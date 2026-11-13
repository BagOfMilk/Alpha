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
        /// <summary>
        /// Стеля зсуву порогів: поріг «на межі» не вище <see cref="MaxDamage"/>, інакше він недосяжний. «Залізна щелепа» (+2)
        /// разом із підсиленням букера (+1) чи ґіміком «Хардкорник» давали 13 — борець ставав невразливим до штрафу −2.
        /// </summary>
        public const int MaxThresholdShift = MaxDamage - EdgeDamage;

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
        /// <summary>
        /// «Свіжий борець» (баланс 08.11.2026, ПЛЕЙСХОЛДЕР): перше накриття проти борця в матчі — +2 до «Вирватися», перший больовий —
        /// +3 до «Терпіти» (больовий ще й несе штраф від чистого прийому нападника). Без цього ~11 % матчів закінчувались за 3–4 дії
        /// одним ланцюгом (кидок → трюк → утримання чи больовий), жодного разу не віддавши ініціативи. Однакові +3 давали матч довший
        /// за 15 дій і перемоги у ворожнечі нижче вилки, однакові +2 лишали ~6 % «розгромів».
        /// </summary>
        public const int FirstPinBonus = 2;
        public const int FirstHoldBonus = 4;

        public const int WarningsToDq = 3;

        /// <summary>
        /// Відлік за рингом (§4.9, ПЛЕЙСХОЛДЕР): кожна дія за рингом — +3; на 10 — програє той, хто лежить; лежать обидва або стоять
        /// обидва — подвійний відлік (нічия). У матчі «без DQ» відліку немає.
        /// </summary>
        public const int OutsideCountStep = 3;
        public const int OutsideCountLimit = 10;

        /// <summary>
        /// Ліміт матчу в діях (§4.11, ПЛЕЙСХОЛДЕР): далі нічия. Підйоми (GetUp) у лічильник не входять (P2-1). Зріз 03.11.2026 мав 20 раундів по ~4 дії; ланцюг рахує кожну дію.
        /// </summary>
        public const int ActionLimit = 40;
        /// <summary>
        /// Пізня стадія (P2-1, довжина матчу 8–15 дій, ПЛЕЙСХОЛДЕР): з цієї дії −<see cref="LateStagePenalty"/> до «Вирватися» й «Терпіти»,
        /// далі штраф росте на 1 за кожні <see cref="LateStageStep"/> дій (втома, матч іде до фіналу). Було 24 плоским −1 — після p90 довжини.
        /// </summary>
        public const int LateStageRound = 11;
        public const int LateStagePenalty = 1;
        public const int LateStageStep = 1;
        /// <summary>
        /// Фінішер недоступний, доки лічильник дій матчу (<c>State.Round</c>) менший за це (P2-1, ПЛЕЙСХОЛДЕР): без цього ~12 % матчів
        /// закінчувались фінішером за 3–7 дій.
        /// </summary>
        public const int FinisherMinRound = 8;

        /// <summary>Реверс фінішера і штрафи «Вирватися» після фінішера (10+ / 7–9).</summary>
        public const int ReverseVsFinisherPenalty = -2;
        public const int FinisherStrongPinPenalty = 3;
        public const int FinisherPartialPinPenalty = 2;
        public const int SubmissionStrongPenalty = 2;
        public const int SubmissionPartialPenalty = 1;

        /// <summary>Пороги ШІ (gdd §2.8).</summary>
        public const int AiPinDamage = 8;
        public const int AiSubmissionDamage = 7;
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
                case MatchActionId.LeaveRing:
                    return 1;
                case MatchActionId.Throw:
                case MatchActionId.Submission:
                case MatchActionId.Weapon:
                    return 2;
                case MatchActionId.TopRope:
                    return 3;
                case MatchActionId.Finisher:
                    return 4;
                default:
                    return 0;   // встати, відновитися, напарник, перевести подих, промінь надії, повернутися в ринг
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
