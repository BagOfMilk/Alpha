using System;
using Game.Core.Ring.Match;

namespace Game.Core.Ring.Career
{
    /// <summary>
    /// Усі числа кар'єри в одному місці (TRPG_DESIGN §5–§8, альфа §11.3). ПЛЕЙСХОЛДЕР: балансуються тут, а не по коду.
    /// </summary>
    public static class CareerRules
    {
        // ---------- час ----------
        /// <summary>
        /// Звичайний сезон — 4 тижні, четвертий — велике шоу (власник 04.11.2026: «давай не больше 4х только некоторые в
        /// будущем сюжетные будут больше»). Довші сюжетні сезони задаватиме вміст — через <see cref="WeeksInSeason"/>.
        /// </summary>
        public const int WeeksPerSeason = 4;
        public const int BigShowEvery = 4;

        /// <summary>Тижнів у сезоні. Поки всі сезони звичайні; сюжетні (довші) прийдуть із вмістом кампанії.</summary>
        public static int WeeksInSeason(int season) { return WeeksPerSeason; }
        public const int ActionsPerWeek = 3;
        /// <summary>Тиждень після невиплаченого проживання (бідність): дві дії.</summary>
        public const int StrainedActions = 2;

        // ---------- гроші (§8) ----------
        public const int StartMoney = 60;
        /// <summary>Проживання за тиждень (якір шкали грошей).</summary>
        public const int LivingCost = 20;
        /// <summary>Плата тренеру за тренування (дає +1 очко розвитку понад звичайне).</summary>
        public const int TrainerCost = 10;
        public const int DoctorCost = 15;
        /// <summary>Гонорар за місцем у картці: відкриття, середина, півголовна, головна подія.</summary>
        public static readonly int[] FeeByPosition = { 10, 20, 35, 60 };
        public const int BigShowFeeMultiplier = 2;
        public const int WinBonus = 5;
        public const int HypeBonus = 5;
        public const int HypeBonusAt = 7;
        public const int SideJobBase = 25;
        /// <summary>Ризик підробітку: сума 2d6 не вище цього — зона тіла дістає ушкодження.</summary>
        public const int SideJobInjuryAtMost = 4;
        public const int SideJobZoneDamage = 2;
        public const int TitleWinBonus = 60;

        // ---------- розвиток ----------
        public const int TrainingPoints = 1;
        public const int TrainerExtraPoints = 1;
        public const int LearnMoveCost = 2;
        /// <summary>Скільки очок розвитку дає підробіток (мало досвіду, §6.1) — жодного; лишено для налаштування.</summary>
        public const int SideJobPoints = 0;

        // ---------- довгі шкали ----------
        public const int MaxPopularity = 10;
        public const int MaxTrust = 5;
        public const int MaxFeud = 4;
        public const int TitleShotTrust = 4;
        public const int FiredPopularity = 1;
        public const int FiredTrust = 1;

        // ---------- травми (§6.3) ----------
        public const int InjuryAtZoneDamage = 3;
        /// <summary>Ушкодження зони з матчу: очки з ударів суперника по зоні ділиться на цей дільник.</summary>
        public const int ZonePointsDivisor = 3;
        public const int MinInjuryWeeks = 1;
        public const int MaxInjuryWeeks = 4;
        /// <summary>Природне загоєння зон щотижня.</summary>
        public const int WeeklyZoneHeal = 1;
        public const int RecoverZoneHeal = 2;
        /// <summary>Виступ травмованим: ажіотаж старту +1; якщо в матчі отримано стільки ушкодження — травма затягується на тиждень.</summary>
        public const int InjuredShowHype = 1;
        public const int InjuryWorsenDamage = MatchRules.ExhaustedDamage;
        public const int InjuryRollPenalty = 1;

        // ---------- промо й закулісся (§6.1) ----------
        public const int PromoStrongHype = 2;

        // ---------- NPC-ворожнеча ----------
        /// <summary>Ажіотаж фіналу, з якого матч піднімає ворожнечу з NPC на рівень.</summary>
        public const int FeudRiseHype = 6;
        public const int NoDqFeudLevel = 3;
        /// <summary>Після розв'язки ворожнеча з NPC повертається до цього рівня.</summary>
        public const int FeudLevelAfterBlowoff = 2;

        // ---------- сила суперника (власник 06.11.2026: «1: за місцем у картці»; замір — docs/ring/fun_audit_2026-11-06.md §1.1) ----------
        // Гравець росте (розвиток, перки, прийоми), ростер — ні: без цього з другого сезону перемоги 90 %+. Щабель — +1 атрибут
        // суперникові (ShowBooker.Strengthen). Ціль: щотижневі 55–65 %, ворожнеча ~50 %, титул 35–45 %, у кожному сезоні.

        /// <summary>
        /// Щаблів сили на щотижневому шоу за місцем у картці: відкривання → мейн-івент. Мейн-івент +1 — рішення власника 06.11.2026
        /// («Повернути»); провал другого сезону від цієї надбавки знято порогом росту (<see cref="RivalGrowthOffset"/> 16 → 21).
        /// </summary>
        public static readonly int[] RivalStepsByPosition = { 1, 1, 1, 2 };
        /// <summary>
        /// Букер ставить суперників рівня гравця: + щабель за кожен куплений атрибут понад базу ґіміка і за кожні
        /// <see cref="RivalPerksPerStep"/> перки. Без цього місце в картці насичується на мейн-івенті, а гравець росте далі
        /// (замір 06.11.2026: лише за місцем — третій сезон 67–81 %, Молот 78–88 %).
        /// </summary>
        public const int RivalPerksPerStep = 2;
        /// <summary>
        /// Щаблі за ріст гравця: (Num·одиниці − Offset) / Den, не менше 0 (замір: щабель за одиницю — перебір, усе 25–45 %).
        /// Offset 21: перші одиниці росту (кінець першого — початок другого сезону) ще не підсилюють суперника.
        /// </summary>
        public const int RivalGrowthNum = 5, RivalGrowthOffset = 21, RivalGrowthDen = 8;
        /// <summary>Молот: щаблів у першому матчі ворожнечі й за кожен наступний (матчі — у різних сезонах).</summary>
        public const int HammerBaseSteps = 1;
        public const int HammerStepsPerMatch = 2;
        /// <summary>Титульний матч (і захист пояса): чемпіон — найсильніший у федерації.</summary>
        public const int TitleSteps = 5;

        public static int RivalSteps(CardPosition p, int growth) { return RivalStepsByPosition[(int)p] + Math.Max(0, growth); }

        /// <summary>Місце в картці за популярністю 0–10.</summary>
        public static CardPosition PositionFor(int popularity)
        {
            if (popularity >= 9) return CardPosition.Main;
            if (popularity >= 6) return CardPosition.SemiMain;
            if (popularity >= 3) return CardPosition.Midcard;
            return CardPosition.Opener;
        }
    }

    public enum CardPosition { Opener = 0, Midcard = 1, SemiMain = 2, Main = 3 }

    public enum CareerStage { Title = 0, Create = 1, Week = 2, Show = 3, SeasonEnd = 4 }

    /// <summary>Фаза шоу: картка вечора → матч (або етапи ворожнечі на великому шоу) → підсумок.</summary>
    public enum ShowPhase { None = 0, Card = 1, Match = 2, Feud = 3, Result = 4 }

    public enum WeekActionKind { Training = 0, Recovery = 1, Promo = 2, Backstage = 3, SideJob = 4 }

    /// <summary>Нагорода промо на «7–9»: одне з двох (§6.1).</summary>
    public enum WeekPromoReward { Feud = 0, Hype = 1 }
}
