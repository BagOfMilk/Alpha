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
        public const int LivingCost = 48;
        /// <summary>Плата тренеру за тренування (дає +1 очко розвитку понад звичайне).</summary>
        public const int TrainerCost = 10;
        public const int DoctorCost = 15;
        /// <summary>Гонорар за місцем у картці: відкриття, середина, півголовна, головна подія.</summary>
        public static readonly int[] FeeByPosition = { 10, 20, 35, 60 };
        /// <summary>Велике шоу: гонорар у відсотках від звичайного (було ×2; 06.11.2026 — менше, щоб кінець сезону не заливав грошима).</summary>
        public const int BigShowFeePercent = 150;
        public const int WinBonus = 5;
        public const int HypeBonus = 5;
        public const int HypeBonusAt = 7;
        public const int SideJobBase = 25;
        /// <summary>Ризик підробітку: сума 2d6 не вище цього — зона тіла дістає ушкодження.</summary>
        public const int SideJobInjuryAtMost = 4;
        public const int SideJobZoneDamage = 2;
        public const int TitleWinBonus = 60;

        // ---------- сітки витрат (06.11.2026, пункт 3 плану лупу: «гроші мають програвати часу чи травмі»). ПЛЕЙСХОЛДЕР ----------
        /// <summary>Костюм: разово; +1 до ажіотажу на старті кожного матчу (вихід виглядає дорожче).</summary>
        public const int CostumeCost = 60;
        public const int CostumeHype = 1;
        /// <summary>Тренер-ростер: плата за тиждень, поки найнятий; кожне тренування — прогрес його атрибута, за CoachProgressStep — +1.</summary>
        public const int CoachWeeklyFee = 20;
        public const int CoachProgressStep = 3;
        /// <summary>Атрибути трьох тренерів ростера (індекс — номер тренера).</summary>
        public static readonly RingAttr[] Coaches = { RingAttr.Strength, RingAttr.Technique, RingAttr.Stamina };
        /// <summary>Клініка — лікар другого рівня: травма зони знімається повністю, ушкодження зони — до нуля.</summary>
        public const int ClinicCost = 45;
        /// <summary>Виїзне шоу: дорога коштує грошей, гонорар ×2 (на відкриванні — у мінус, вище в картці — у плюс), наступного тижня на одну дію менше (тиск часу).</summary>
        public const int AwayTravelCost = 20;
        /// <summary>Гонорар виїзного шоу у відсотках від звичайного.</summary>
        public const int AwayFeePercent = 200;
        /// <summary>Страховка на сезон: кожна нова травма коротша на тиждень (не менше одного).</summary>
        public const int InsuranceCost = 40;

        // ---------- розвиток ----------
        public const int TrainingPoints = 1;
        public const int TrainerExtraPoints = 1;
        public const int LearnMoveCost = 2;
        /// <summary>Скільки очок розвитку дає підробіток (мало досвіду, §6.1) — жодного; лишено для налаштування.</summary>
        public const int SideJobPoints = 0;

        // ---------- довгі шкали ----------
        public const int MaxPopularity = 10;
        /// <summary>
        /// Популярність новачка на старті кар'єри (П5): місцева федерація має шкалу 0–4, і стартові 3 зі сценарію ворожнечі (шкала
        /// 0–10) — це вже півголовна подія (перший суперник — Барон, 18 дій). 1 — відкривання, але ще не звільнення (0 на кінці сезону).
        /// </summary>
        public const int StartPopularity = 1;
        /// <summary>
        /// P1-6 (рішення власника за планом П5): поразка чи нічия дає популярність лише з ажіотажем фіналу ≥ 9 і щонайбільше раз
        /// за сезон (раніше — будь-який матч з ажіотажем ≥ 7). Перемога — як і була.
        /// </summary>
        public const int LossPopularityHype = 9;
        public const int MaxTrust = 5;
        public const int MaxFeud = 4;
        public const int TitleShotTrust = 4;
        public const int FiredPopularity = 1;
        public const int FiredTrust = 1;

        // ---------- перехід між федераціями: правило А2 «два ключі» (власник 11.11.2026: «А2, як радиш»). ПЛЕЙСХОЛДЕР ----------
        /// <summary>Ключ глядачів: популярність ≥ стеля поточної федерації − це число (місцева 3, регіональна 5, національна 7).</summary>
        public const int OfferPopularityGap = 1;
        /// <summary>Ключ букера: довіра ≥ цього числа.</summary>
        public const int OfferTrust = 4;
        /// <summary>Чинний чемпіон своєї федерації на кінець сезону: довіра в перевірці рахується на стільки більше. Популярність пояс не замінює.</summary>
        public const int OfferChampionTrustBonus = 1;
        /// <summary>Статус входу в М1 — один: попередня стеля (= нова − 2), середина картки. Статуси входу — ідея ітерації 2.</summary>
        public const int EntryCeilingGap = 2;
        /// <summary>Довіра в новій федерації: чемпіон віддав пояс за планом («пішов красиво») — 2; інакше — 1.</summary>
        public const int FarewellTrust = 2;
        public const int TransferTrust = 1;
        /// <summary>Відмова від пропозиції стільки разів поспіль — букер ображений: довіра −1.</summary>
        public const int DeclinesToOffend = 2;

        /// <summary>
        /// Обидва ключі пропозиції вищої федерації (А2): глядачі (популярність) і букер (довіра, пояс додає +1). Одна функція — одна
        /// правка умови. <paramref name="maxTier"/> — скільки федерацій є; з найвищої пропозиції немає.
        /// </summary>
        public static OfferKeys OfferReady(int popularity, int trust, bool champion, FederationDef fed, int maxTier)
        {
            var k = new OfferKeys
            {
                FromTier = fed.Tier, ToTier = fed.Tier + 1, HasHigher = fed.Tier < maxTier,
                Popularity = popularity, PopularityNeed = fed.Ceiling - OfferPopularityGap,
                Trust = trust, BeltCounted = champion, TrustNeed = OfferTrust
            };
            k.TrustCounted = trust + (champion ? OfferChampionTrustBonus : 0);
            k.AudienceOk = popularity >= k.PopularityNeed;
            k.BookerOk = k.TrustCounted >= k.TrustNeed;
            k.Ready = k.HasHigher && k.AudienceOk && k.BookerOk;
            return k;
        }

        /// <summary>Популярність, з якою гравець входить у федерацію (статус входу; у М1 — стеля нової − 2).</summary>
        public static int EntryPopularity(FederationDef to) { return Math.Max(to.Floor, to.Ceiling - EntryCeilingGap); }

        /// <summary>
        /// Контракт не продовжують: популярність нижче підлоги федерації на кінці сезону (власник 11.11.2026 — рівень нижче); на
        /// місцевій (підлога 0) — популярність 0, як і було (§6.2: повернення на мінімум, не кінець гри).
        /// </summary>
        public static bool IsFired(int popularity, FederationDef fed) { return fed.Floor <= 0 ? popularity <= 0 : popularity < fed.Floor; }

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
        /// Відкривання 1 → 0 (П5, 11.11.2026, рушій P2-1 — матч 8–15 дій): перший тиждень став 51 %, місцева загалом 52 % проти
        /// вікна 55–65 %; з 0 — перший тиждень 61 %, місцева 56 %.
        /// </summary>
        public static readonly int[] RivalStepsByPosition = { 0, 1, 1, 2 };
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
        /// <summary>На початку сезону знімок росту щабля наздоганяє поточний ріст на Num/Den.</summary>
        public const int SeasonCatchUpNum = 1, SeasonCatchUpDen = 1;
        /// <summary>Реванш із тим, хто тебе переміг: суперник слабший на стільки щаблів (ти знаєш його слабкість).</summary>
        public const int RematchSteps = 2;
        /// <summary>Букер охоче ставить реванш: стільки балів до вибору суперника, який тебе переміг.</summary>
        public const int RematchBookingScore = 3;

        /// <summary>
        /// Молот: щаблів понад ріст гравця в 1-му, 2-му і 3-му матчі ворожнечі (матчі — у різних сезонах), як і до федерацій:
        /// база 1 + 2 за матч.
        /// </summary>
        public static readonly int[] HammerStepsByMatch = { 1, 3, 5 };
        /// <summary>
        /// Молот іде слідом (варіант А, П5): на кожному рівні федерації вище місцевої — + стільки щаблів. Без цього ворожнеча на
        /// регіональній — 61–63 % (замір 200 кар'єр на рушії P2-1; Молот упирається в межу атрибутів, а гравець, якого підвищили,
        /// сильніший); з 3 — 54 %.
        /// </summary>
        public const int FeudTierSteps = 3;
        public static int HammerSteps(int matchesPlayed)
        {
            return HammerStepsByMatch[Math.Max(0, Math.Min(HammerStepsByMatch.Length - 1, matchesPlayed))];
        }
        /// <summary>
        /// Титульний матч (і захист пояса): чемпіон — найсильніший у федерації; щаблів понад ріст — за рівнем федерації (1…4).
        /// П5 (11.11.2026, вікно власника «титул 35–45 %», рушій P2-1): однакові 3 щаблі давали 49 / 53 / 71 / 75 % — на вищих рівнях
        /// атрибути чемпіона впираються в межу, і кожен щабель понад неї (витримка) важить менше; з {4, 6, 7, 9} — 35 / 43 / 43 / 45 %.
        /// </summary>
        public static readonly int[] TitleStepsByTier = { 4, 6, 7, 9 };
        public static int TitleSteps(int tier) { return TitleStepsByTier[Math.Max(0, Math.Min(TitleStepsByTier.Length - 1, tier - 1))]; }

        public static int RivalSteps(CardPosition p, int growth) { return RivalStepsByPosition[(int)p] + Math.Max(0, growth); }

        /// <summary>
        /// Місце в картці від відстані до стелі федерації (П5): на стелі — головна подія, −1 — півголовна, −2 — середина, нижче —
        /// відкривання. Новачок вищої федерації приходить із попередньою стелею (= нова − 2) — у середину картки.
        /// </summary>
        public static CardPosition PositionFor(int popularity, int ceiling)
        {
            if (popularity >= ceiling) return CardPosition.Main;
            if (popularity >= ceiling - 1) return CardPosition.SemiMain;
            if (popularity >= ceiling - 2) return CardPosition.Midcard;
            return CardPosition.Opener;
        }
    }

    public enum CardPosition { Opener = 0, Midcard = 1, SemiMain = 2, Main = 3 }

    /// <summary>
    /// Два ключі пропозиції (А2) для екрана кінця сезону: «Глядачі N/потрібно», «Букер M/потрібно» (+ пояс, якщо зарахований) —
    /// рядок одразу показує, чого бракує.
    /// </summary>
    public sealed class OfferKeys
    {
        public int FromTier;
        public int ToTier;
        /// <summary>Є куди рости (з найвищої федерації пропозиції немає).</summary>
        public bool HasHigher;
        public int Popularity;
        public int PopularityNeed;
        public bool AudienceOk;
        /// <summary>Довіра без пояса й з поясом (у перевірці).</summary>
        public int Trust;
        public int TrustCounted;
        public int TrustNeed;
        public bool BookerOk;
        public bool BeltCounted;
        public bool Ready;
    }

    public enum CareerStage { Title = 0, Create = 1, Week = 2, Show = 3, SeasonEnd = 4 }

    /// <summary>Фаза шоу: картка вечора → матч (або етапи ворожнечі на великому шоу) → підсумок.</summary>
    public enum ShowPhase { None = 0, Card = 1, Match = 2, Feud = 3, Result = 4 }

    public enum WeekActionKind { Training = 0, Recovery = 1, Promo = 2, Backstage = 3, SideJob = 4 }

    /// <summary>Нагорода промо на «7–9»: одне з двох (§6.1).</summary>
    public enum WeekPromoReward { Feud = 0, Hype = 1 }
}
