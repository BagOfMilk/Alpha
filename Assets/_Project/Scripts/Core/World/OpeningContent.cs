using System.Collections.Generic;
using Game.Core.Checks;
using Game.Core.Pressure;

namespace Game.Core.World
{
    /// <summary>
    /// Контент открытия «Перевал» (docs/FIRST_HOUR.md §2.2) — шаг 5 порядка
    /// сборки.
    ///
    /// Без него срез играется, но первые пять суток пустые: конвейер исправен,
    /// а событий нет. Здесь появляются узел первых суток, два авторских
    /// инцидента и именной накопитель «Тугар».
    ///
    /// Пороги помечены ПЛЕЙСХОЛДЕР: в §2.2 они записаны как «Persuade ≥ P» и
    /// «Tactics ≥ T», числа владелец не называл. Поставлены такие, чтобы оба
    /// пути реально выбирались стартовой общиной, и вынесены сюда одним местом.
    /// </summary>
    public static class OpeningContent
    {
        /// <summary>ПЛЕЙСХОЛДЕР: порог тихого пути узла 1 (§2.2, «Persuade ≥ P»).</summary>
        public const int PassQuietThreshold = 7;

        /// <summary>ПЛЕЙСХОЛДЕР: порог кровавого пути узла 1 (§2.2, «Tactics ≥ T»).</summary>
        public const int PassBloodyThreshold = 6;

        /// <summary>Именной накопитель: боярин где-то договаривается.</summary>
        public const string TuharSourceId = "tuhar";

        /// <summary>
        /// Узел 1, сутки 1 — авангард орды на перевале.
        ///
        /// Оба порога показываются ДО подтверждения: это первая в игре точка
        /// решения, и по ней игрок учится, что пути разные не по цвету, а по
        /// цене. Тихий путь сеет `spoiled_stores` на вторые сутки, кровавый —
        /// рану исполнителю и страх общины.
        /// </summary>
        public static IncidentDefinition PassVanguard() => new IncidentDefinition
        {
            Id = "pass_vanguard",
            TopicId = "incident.pass_vanguard",
            SourceId = "opening.pass",
            DomainTag = "перевал",
            MinBand = TensionBand.Calm,
            MaxBand = TensionBand.Fracture,
            MinTier = 1,
            Weight = 100,

            QuietPathSkill = SkillKeys.Persuade,
            QuietPathThreshold = PassQuietThreshold,
            QuietPathApproach = ApproachForm.Persuade,

            BloodyPathSkill = SkillKeys.Tactics,
            BloodyPathThreshold = PassBloodyThreshold,

            RelevantPositionId = "council_seat",
            TensionByBand = new[] { 35, 10, -15, -35 }
        };

        /// <summary>
        /// Сутки 2 — испорченные припасы: след тихого пути. Авангард ушёл, но
        /// забрал скот, и склад считает потери.
        /// </summary>
        public static IncidentDefinition SpoiledStores() => new IncidentDefinition
        {
            Id = "spoiled_stores",
            TopicId = "incident.spoiled_stores",
            SourceId = "opening.stores",
            DomainTag = "склад",
            MinBand = TensionBand.Calm,
            MaxBand = TensionBand.Fracture,
            MinTier = 1,
            Weight = 60,

            // Навык тот же, что у ДОМЕНА поста (склад докладывает по Выживанию):
            // иначе человек, стоящий на складе, не может разобраться с бедой
            // склада, и исход Худший при любом выборе — это не решение, а
            // ловушка. Поймано прогоном среза, а не рассуждением.
            QuietPathSkill = SkillKeys.Survival,
            QuietPathThreshold = 5,
            QuietPathApproach = ApproachForm.Neutral,

            RelevantPositionId = "storehouse_dock",
            TensionByBand = new[] { 25, 8, -8, -20 }
        };

        /// <summary>
        /// Сутки 3 — раненая в набеге девочка.
        ///
        /// Урок расстановки: если лазарет пуст, разбираться некому, и полоса
        /// исхода Худшая (чеклист §3 стр. 7). Это не подлость дизайна — цена
        /// пустого поста должна быть видимой, иначе расстановка не решение.
        /// </summary>
        public static IncidentDefinition SickChild() => new IncidentDefinition
        {
            Id = "sick_child",
            TopicId = "incident.sick_child",
            SourceId = "opening.child",
            DomainTag = "лазарет",
            MinBand = TensionBand.Calm,
            MaxBand = TensionBand.Fracture,
            MinTier = 1,
            Weight = 60,

            QuietPathSkill = SkillKeys.Medicine,
            QuietPathThreshold = 5,
            QuietPathApproach = ApproachForm.Neutral,

            RelevantPositionId = "infirmary_bed",
            TensionByBand = new[] { 30, 10, -10, -25 }
        };

        /// <summary>
        /// Источник, который срабатывает в НАЗНАЧЕННЫЕ сутки и только в них.
        ///
        /// Открытие поставлено автором: узел на первые сутки, испорченные
        /// припасы на вторые, девочка на третьи (§2.2). Отдать это случайности
        /// накопителей значило бы, что первая игровая час каждый раз разная —
        /// а её как раз и проверяют по чеклисту.
        ///
        /// Сделано источником, а не веткой в консоли, чтобы срез и харнес
        /// видели одно и то же: последовательность — часть контента.
        /// </summary>
        public sealed class ScriptedSource : IPressureSource
        {
            private readonly int _day;

            public ScriptedSource(string id, string domainTag, int day)
            {
                Id = id;
                DomainTag = domainTag;
                _day = day;
            }

            public string Id { get; }
            public WorldEventKind Kind => WorldEventKind.InternalThreat;
            public string DomainTag { get; }

            public int Threshold => 1;
            public int CooldownDays => 999;   // один раз за срез

            public bool IsActive(PulseContext ctx) => ctx.Day == _day && !ctx.IsNight;

            /// <summary>
            /// Авторская сцена о себе не предупреждает: её «предвестник» —
            /// сама постановка. Иначе поставленный узел сыплет лестницей слухов
            /// о себе самом, и настоящие предвестники в ней тонут.
            /// </summary>
            public bool Announces => false;
            public int InsistencePerDay(PulseContext ctx) => ctx.Day == _day && !ctx.IsNight ? 1 : 0;
        }

        /// <summary>Авторская последовательность открытия: какой узел в какие сутки.</summary>
        public static List<IPressureSource> ScriptedSources() => new List<IPressureSource>
        {
            new ScriptedSource("opening.pass", "перевал", 1),
            new ScriptedSource("opening.stores", "склад", 2),
            new ScriptedSource("opening.child", "лазарет", 3)
        };

        /// <summary>Весь контент открытия — для таблицы инцидентов среза.</summary>
        public static List<IncidentDefinition> All() => new List<IncidentDefinition>
        {
            PassVanguard(), SpoiledStores(), SickChild()
        };

        /// <summary>
        /// Именной накопитель «Тугар»: копит медленно и с первых суток, чтобы
        /// первая ступень предвестника дошла до игрока на 3–4 сутки (§2.2).
        /// Слух о боярине — телеграфия того же человека, которого игрок видел в
        /// сцене открытия.
        ///
        /// Своего инцидента у «Тугара» нет: его развязка — сценарный Финал, а
        /// не срабатывание пульса. Дойдя до порога, накопитель не разряжается
        /// (WorldPulse.Advance спрашивает таблицу, есть ли чем сработать) и
        /// держит услышанную третью ступень до Финала, а не начинает лестницу
        /// заново.
        /// </summary>
        public sealed class TuharPressureSource : IPressureSource
        {
            public string Id => TuharSourceId;
            public WorldEventKind Kind => WorldEventKind.InternalThreat;
            public string DomainTag => "перевал";
            public int Threshold => 60;
            public int CooldownDays => 6;

            /// <summary>Боярин договаривается независимо от того, спокойно ли в городе.</summary>
            public bool IsActive(PulseContext ctx) => true;

            /// <summary>Слух о боярине — именно предвестник: он и обязан дойти (§2.2).</summary>
            public bool Announces => true;

            public int InsistencePerDay(PulseContext ctx) => 12;
        }
    }
}
