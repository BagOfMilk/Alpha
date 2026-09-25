using System.Collections.Generic;
using Game.Core.Checks;
using Game.Core.Pressure;

namespace Game.Core.World
{
    /// <summary>
    /// Стартовий пул інцидентів — ЗАГЛУШКИ. Текст заміниться, коли наративна
    /// майстерня видасть канон (світ, фракції, імена); механіка від цього не
    /// залежить, тому що інциденти за US-11.3 — дані.
    ///
    /// У КОЖНОГО прописаний тихий шлях (Поправка №1) — це перевіряється тестом
    /// по всьому пулу, а не лишається на совісті автора.
    /// </summary>
    public static class DefaultIncidents
    {
        public static IncidentTable BuildTable()
        {
            var table = new IncidentTable();
            table.AddRange(All());
            return table;
        }

        public static IEnumerable<IncidentDefinition> All()
        {
            // ---- Ропіт: дрібниця, яку видно на вулиці ----
            yield return new IncidentDefinition
            {
                Id = "petty_theft", TopicId = "incident.petty_theft", SourceId = "street", DomainTag = "склад",
                MinBand = TensionBand.Murmur, MaxBand = TensionBand.Heat, MinTier = 1, Weight = 12,
                QuietPathSkill = SkillKeys.Persuade, QuietPathThreshold = 5,
                QuietPathApproach = ApproachForm.Persuade,
                BloodyPathSkill = SkillKeys.Intimidate, BloodyPathThreshold = 4,
                RelevantPositionId = "storehouse_dock",
                TensionByBand = new[] { 25, 10, -5, -15 }
            };

            yield return new IncidentDefinition
            {
                Id = "market_brawl", TopicId = "incident.market_brawl", SourceId = "street", DomainTag = "рынок",
                MinBand = TensionBand.Murmur, MaxBand = TensionBand.Fracture, MinTier = 1, Weight = 10,
                QuietPathSkill = SkillKeys.Trade, QuietPathThreshold = 6,
                QuietPathApproach = ApproachForm.Trade,
                BloodyPathSkill = SkillKeys.Melee, BloodyPathThreshold = 5,
                RelevantPositionId = "settlement_market",
                TensionByBand = new[] { 30, 12, -5, -20 }
            };

            yield return new IncidentDefinition
            {
                Id = "spoiled_stores", TopicId = "incident.spoiled_stores", SourceId = "street", DomainTag = "припасы",
                MinBand = TensionBand.Calm, MaxBand = TensionBand.Heat, MinTier = 1, Weight = 8,
                QuietPathSkill = SkillKeys.Survival, QuietPathThreshold = 5,
                RelevantPositionId = "settlement_farms",
                TensionByBand = new[] { 20, 8, -5, -12 }
            };

            yield return new IncidentDefinition
            {
                Id = "sick_child", TopicId = "incident.sick_child", SourceId = "street", DomainTag = "лазарет",
                MinBand = TensionBand.Calm, MaxBand = TensionBand.Fracture, MinTier = 1, Weight = 9,
                QuietPathSkill = SkillKeys.Medicine, QuietPathThreshold = 6,
                RelevantPositionId = "infirmary_bed",
                TensionByBand = new[] { 25, 10, -8, -20 }
            };

            // ---- Бродіння: організована злочинність ----
            yield return new IncidentDefinition
            {
                Id = "protection_racket", TopicId = "incident.protection_racket", SourceId = "street", DomainTag = "рынок",
                MinBand = TensionBand.Ferment, MaxBand = TensionBand.Fracture, MinTier = 2, Weight = 12,
                QuietPathSkill = SkillKeys.Intimidate, QuietPathThreshold = 8,
                QuietPathApproach = ApproachForm.Intimidate,
                BloodyPathSkill = SkillKeys.Ranged, BloodyPathThreshold = 6,
                RelevantPositionId = "council_seat",
                TensionByBand = new[] { 45, 18, -12, -30 }
            };

            yield return new IncidentDefinition
            {
                Id = "missing_person", TopicId = "incident.missing_person", SourceId = "street", DomainTag = "окраина",
                MinBand = TensionBand.Ferment, MaxBand = TensionBand.Fracture, MinTier = 1, Weight = 10,
                QuietPathSkill = SkillKeys.Survival, QuietPathThreshold = 7,
                BloodyPathSkill = SkillKeys.Melee, BloodyPathThreshold = 6,
                RelevantPositionId = "scouting_post",
                // Знайшли — і не одного: разом із зниклим приходять ті, хто прибився в дорозі.
                ArrivalsOnGood = 3,
                TensionByBand = new[] { 40, 15, -10, -25 }
            };

            // ---- Нічні ----
            yield return new IncidentDefinition
            {
                Id = "night_burglary", TopicId = "incident.night_burglary", SourceId = "night", DomainTag = "ночь",
                MinBand = TensionBand.Murmur, MaxBand = TensionBand.Fracture, MinTier = 1, Weight = 14,
                NightOnly = true,
                QuietPathSkill = SkillKeys.Lockpick, QuietPathThreshold = 6,
                BloodyPathSkill = SkillKeys.Melee, BloodyPathThreshold = 5,
                RelevantPositionId = "storehouse_dock",
                TensionByBand = new[] { 30, 12, -8, -18 }
            };

            yield return new IncidentDefinition
            {
                Id = "night_arson", TopicId = "incident.night_arson", SourceId = "night", DomainTag = "ночь",
                MinBand = TensionBand.Heat, MaxBand = TensionBand.Fracture, MinTier = 2, Weight = 10,
                NightOnly = true,
                QuietPathSkill = SkillKeys.Mechanics, QuietPathThreshold = 8,
                BloodyPathSkill = SkillKeys.Ranged, BloodyPathThreshold = 7,
                RelevantPositionId = "workshop_bench",
                TensionByBand = new[] { 50, 20, -12, -28 }
            };

            // ---- Криза ----
            yield return new IncidentDefinition
            {
                Id = "crisis_riot", TopicId = "incident.crisis_riot", SourceId = "crisis", DomainTag = "площадь",
                // Джерело кризи накопичує з «Розпалу» (IsActive: TensionBandIndex >= 3),
                // отже й розв'язуватися криза зобов'язана з «Розпалу». Інакше визріла на
                // Розпалі криза не знаходить собі інциденту: Eligible ріже за полосою,
                // Pick повертає null, а заряд УЖЕ згорів у Fire — і джерело йде
                // на 30 днів кулдауну даремно.
                MinBand = TensionBand.Heat, MaxBand = TensionBand.Fracture, MinTier = 1, Weight = 100,
                QuietPathSkill = SkillKeys.Persuade, QuietPathThreshold = 10,
                QuietPathApproach = ApproachForm.Persuade,
                BloodyPathSkill = SkillKeys.Tactics, BloodyPathThreshold = 9,
                RelevantPositionId = "council_seat",
                TensionByBand = new[] { 60, 20, -40, -80 },
                IsCrisis = true,
                Bite = CrisisBite.KillCompanion,
                PopulationLoss = 30
            };
        }
    }

    /// <summary>
    /// Стартові джерела тиску. Їх ТРИ з різними ставками — це інваріант
    /// детермінізму, а не прикраса: один накопичувач читався б наскрізь.
    /// Ставки залежать від стану світу, тому розрахунковий строк «пливе» після
    /// кожної дії гравця.
    /// </summary>
    public static class DefaultPressureSources
    {
        /// <summary>
        /// <paramref name="crisis"/> — гачок Поправки №7 (тестова збірка,
        /// <c>TestBuildTensionPace</c>): підмінити тільки джерело кризи
        /// стисненим (менший поріг), не чіпаючи інших двох. null — кампанійний
        /// дефолт, як і раніше.
        /// </summary>
        public static IEnumerable<IPressureSource> All(CrisisPressureSource crisis = null)
        {
            yield return new StreetPressureSource();
            yield return new NightPressureSource();
            yield return crisis ?? new CrisisPressureSource();
        }
    }

    /// <summary>Вулиця: накопичує швидше, коли місто напружене.</summary>
    public sealed class StreetPressureSource : IPressureSource
    {
        public string Id => "street";
        public WorldEventKind Kind => WorldEventKind.InternalThreat;
        public string DomainTag => "улицы";
        public int Threshold => 100;
        public int CooldownDays => 4;
        public bool IsActive(PulseContext ctx) => true;

        /// <summary>Загроза, про яку не попередили, — нечесна.</summary>
        public bool Announces => true;

        public int InsistencePerDay(PulseContext ctx)
        {
            // База 6 плюс по 4 за кожну полосу Напруги: тихе місто майже мовчить.
            return 6 + ctx.TensionBandIndex * 4;
        }
    }

    /// <summary>Ніч: активна лише в темну фазу; патруль збиває темп.</summary>
    public sealed class NightPressureSource : IPressureSource
    {
        public string Id => "night";
        public WorldEventKind Kind => WorldEventKind.NightCrime;
        public string DomainTag => "ночь";
        public int Threshold => 80;
        public int CooldownDays => 3;
        public bool IsActive(PulseContext ctx) => ctx.IsNight;

        /// <summary>Загроза, про яку не попередили, — нечесна.</summary>
        public bool Announces => true;

        public int InsistencePerDay(PulseContext ctx)
        {
            int rate = 10 + ctx.TensionBandIndex * 5;
            // Патруль — небойова контргра: тисне нічну злочинність (US-1.5).
            if (ctx.IsPatrolling) rate /= 3;
            return rate;
        }
    }

    /// <summary>
    /// Криза: накопичує лише на верхніх полосах і повільно — щоб у гравця було
    /// час побачити три ступені передвісників і встигнути втрутитися.
    /// </summary>
    public sealed class CrisisPressureSource : IPressureSource
    {
        private readonly int _threshold;
        private readonly int _cooldownDays;
        private readonly int _insistenceAtHeat;
        private readonly int _insistenceAtFracture;

        /// <summary>
        /// Параметри конструктора — кампанійні значення лишаються дефолтом
        /// (виклик <c>new CrisisPressureSource()</c> без аргументів дає той
        /// самий об'єкт, що й раніше). Тестова збірка (Поправка №7,
        /// <c>TestBuildTensionPace</c>) передає лише стиснутий
        /// <paramref name="threshold"/> — темп самого накопичення (ставки
        /// нижче) і вікно милосердя (<see cref="Balance.PulseBalance.CrisisGraceDays"/>)
        /// лишаються окремими важелями.
        /// </summary>
        public CrisisPressureSource(int threshold = 120, int cooldownDays = 30,
            int insistenceAtHeat = 5, int insistenceAtFracture = 12)
        {
            _threshold = threshold > 0 ? threshold : 120;
            _cooldownDays = cooldownDays > 0 ? cooldownDays : 30;
            _insistenceAtHeat = insistenceAtHeat;
            _insistenceAtFracture = insistenceAtFracture;
        }

        public string Id => "crisis";
        public WorldEventKind Kind => WorldEventKind.Crisis;
        public string DomainTag => "площадь";
        public int Threshold => _threshold;
        public int CooldownDays => _cooldownDays;
        public bool IsActive(PulseContext ctx) => ctx.TensionBandIndex >= 3;

        /// <summary>Загроза, про яку не попередили, — нечесна.</summary>
        public bool Announces => true;

        public int InsistencePerDay(PulseContext ctx)
        {
            return ctx.TensionBandIndex >= 4 ? _insistenceAtFracture : _insistenceAtHeat;
        }
    }
}
