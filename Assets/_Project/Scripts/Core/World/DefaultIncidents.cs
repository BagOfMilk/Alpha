using System.Collections.Generic;
using Game.Core.Checks;
using Game.Core.Pressure;

namespace Game.Core.World
{
    /// <summary>
    /// Стартовый пул инцидентов — ЗАГЛУШКИ. Текст заменится, когда нарративная
    /// мастерская выдаст канон (мир, фракции, имена); механика от этого не
    /// зависит, потому что инциденты по US-11.3 — данные.
    ///
    /// У КАЖДОГО прописан тихий путь (Поправка №1) — это проверяется тестом
    /// по всему пулу, а не остаётся на совести автора.
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
            // ---- Ропот: мелочь, которую видно на улице ----
            yield return new IncidentDefinition
            {
                Id = "petty_theft", TopicId = "incident.petty_theft", DomainTag = "склад",
                MinBand = TensionBand.Murmur, MaxBand = TensionBand.Heat, MinTier = 1, Weight = 12,
                QuietPathSkill = SkillKeys.Persuade, QuietPathThreshold = 5,
                QuietPathApproach = ApproachForm.Persuade,
                BloodyPathSkill = SkillKeys.Intimidate, BloodyPathThreshold = 4,
                RelevantPositionId = "storehouse_dock",
                TensionByBand = new[] { 25, 10, -5, -15 }
            };

            yield return new IncidentDefinition
            {
                Id = "market_brawl", TopicId = "incident.market_brawl", DomainTag = "рынок",
                MinBand = TensionBand.Murmur, MaxBand = TensionBand.Fracture, MinTier = 1, Weight = 10,
                QuietPathSkill = SkillKeys.Trade, QuietPathThreshold = 6,
                QuietPathApproach = ApproachForm.Trade,
                BloodyPathSkill = SkillKeys.Melee, BloodyPathThreshold = 5,
                RelevantPositionId = "settlement_market",
                TensionByBand = new[] { 30, 12, -5, -20 }
            };

            yield return new IncidentDefinition
            {
                Id = "spoiled_stores", TopicId = "incident.spoiled_stores", DomainTag = "припасы",
                MinBand = TensionBand.Calm, MaxBand = TensionBand.Heat, MinTier = 1, Weight = 8,
                QuietPathSkill = SkillKeys.Survival, QuietPathThreshold = 5,
                RelevantPositionId = "settlement_farms",
                TensionByBand = new[] { 20, 8, -5, -12 }
            };

            yield return new IncidentDefinition
            {
                Id = "sick_child", TopicId = "incident.sick_child", DomainTag = "лазарет",
                MinBand = TensionBand.Calm, MaxBand = TensionBand.Fracture, MinTier = 1, Weight = 9,
                QuietPathSkill = SkillKeys.Medicine, QuietPathThreshold = 6,
                RelevantPositionId = "infirmary_bed",
                TensionByBand = new[] { 25, 10, -8, -20 }
            };

            // ---- Брожение: организованная преступность ----
            yield return new IncidentDefinition
            {
                Id = "protection_racket", TopicId = "incident.protection_racket", DomainTag = "рынок",
                MinBand = TensionBand.Ferment, MaxBand = TensionBand.Fracture, MinTier = 2, Weight = 12,
                QuietPathSkill = SkillKeys.Intimidate, QuietPathThreshold = 8,
                QuietPathApproach = ApproachForm.Intimidate,
                BloodyPathSkill = SkillKeys.Ranged, BloodyPathThreshold = 6,
                RelevantPositionId = "council_seat",
                TensionByBand = new[] { 45, 18, -12, -30 }
            };

            yield return new IncidentDefinition
            {
                Id = "missing_person", TopicId = "incident.missing_person", DomainTag = "окраина",
                MinBand = TensionBand.Ferment, MaxBand = TensionBand.Fracture, MinTier = 1, Weight = 10,
                QuietPathSkill = SkillKeys.Survival, QuietPathThreshold = 7,
                BloodyPathSkill = SkillKeys.Melee, BloodyPathThreshold = 6,
                RelevantPositionId = "scouting_post",
                TensionByBand = new[] { 40, 15, -10, -25 }
            };

            // ---- Ночные ----
            yield return new IncidentDefinition
            {
                Id = "night_burglary", TopicId = "incident.night_burglary", DomainTag = "ночь",
                MinBand = TensionBand.Murmur, MaxBand = TensionBand.Fracture, MinTier = 1, Weight = 14,
                NightOnly = true,
                QuietPathSkill = SkillKeys.Lockpick, QuietPathThreshold = 6,
                BloodyPathSkill = SkillKeys.Melee, BloodyPathThreshold = 5,
                RelevantPositionId = "storehouse_dock",
                TensionByBand = new[] { 30, 12, -8, -18 }
            };

            yield return new IncidentDefinition
            {
                Id = "night_arson", TopicId = "incident.night_arson", DomainTag = "ночь",
                MinBand = TensionBand.Heat, MaxBand = TensionBand.Fracture, MinTier = 2, Weight = 10,
                NightOnly = true,
                QuietPathSkill = SkillKeys.Mechanics, QuietPathThreshold = 8,
                BloodyPathSkill = SkillKeys.Ranged, BloodyPathThreshold = 7,
                RelevantPositionId = "workshop_bench",
                TensionByBand = new[] { 50, 20, -12, -28 }
            };

            // ---- Кризис ----
            yield return new IncidentDefinition
            {
                Id = "crisis_riot", TopicId = "incident.crisis_riot", DomainTag = "площадь",
                MinBand = TensionBand.Fracture, MaxBand = TensionBand.Fracture, MinTier = 1, Weight = 100,
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
    /// Стартовые источники давления. Их ТРИ с разными ставками — это инвариант
    /// детерминизма, а не украшение: один накопитель читался бы насквозь.
    /// Ставки зависят от состояния мира, поэтому расчётный срок «плывёт» после
    /// каждого действия игрока.
    /// </summary>
    public static class DefaultPressureSources
    {
        public static IEnumerable<IPressureSource> All()
        {
            yield return new StreetPressureSource();
            yield return new NightPressureSource();
            yield return new CrisisPressureSource();
        }
    }

    /// <summary>Улица: копит быстрее, когда город напряжён.</summary>
    public sealed class StreetPressureSource : IPressureSource
    {
        public string Id => "street";
        public WorldEventKind Kind => WorldEventKind.InternalThreat;
        public string DomainTag => "улицы";
        public int Threshold => 100;
        public int CooldownDays => 4;
        public bool IsActive(PulseContext ctx) => true;

        public int InsistencePerDay(PulseContext ctx)
        {
            // База 6 плюс по 4 за каждую полосу Напряжения: тихий город почти молчит.
            return 6 + ctx.TensionBandIndex * 4;
        }
    }

    /// <summary>Ночь: активна только в тёмную фазу; патруль сбивает темп.</summary>
    public sealed class NightPressureSource : IPressureSource
    {
        public string Id => "night";
        public WorldEventKind Kind => WorldEventKind.NightCrime;
        public string DomainTag => "ночь";
        public int Threshold => 80;
        public int CooldownDays => 3;
        public bool IsActive(PulseContext ctx) => ctx.IsNight;

        public int InsistencePerDay(PulseContext ctx)
        {
            int rate = 10 + ctx.TensionBandIndex * 5;
            // Патруль — небоевая контригра: давит ночную преступность (US-1.5).
            if (ctx.IsPatrolling) rate /= 3;
            return rate;
        }
    }

    /// <summary>
    /// Кризис: копит только на верхних полосах и медленно — чтобы у игрока было
    /// время увидеть три ступени предвестников и успеть вмешаться.
    /// </summary>
    public sealed class CrisisPressureSource : IPressureSource
    {
        public string Id => "crisis";
        public WorldEventKind Kind => WorldEventKind.Crisis;
        public string DomainTag => "площадь";
        public int Threshold => 120;
        public int CooldownDays => 30;
        public bool IsActive(PulseContext ctx) => ctx.TensionBandIndex >= 3;

        public int InsistencePerDay(PulseContext ctx)
        {
            return ctx.TensionBandIndex >= 4 ? 12 : 5;
        }
    }
}
