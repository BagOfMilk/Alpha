using System.Collections.Generic;
using Game.Core.Checks;
using Game.Core.Pressure;

namespace Game.Core.World
{
    /// <summary>
    /// Контент відкриття «Перевал» (docs/FIRST_HOUR.md §2.2) — крок 5 порядку
    /// збірки.
    ///
    /// Без нього зріз грається, але перші п'ять діб порожні: конвеєр справний,
    /// а подій немає. Тут з'являються вузол перших діб, два авторських
    /// інциденти і іменний накопичувач «Тугар».
    ///
    /// Пороги позначені ПЛЕЙСХОЛДЕР: у §2.2 вони записані як «Persuade ≥ P» і
    /// «Tactics ≥ T», числа власник не називав. Поставлені такі, щоб обидва
    /// шляхи реально обиралися стартовою громадою, і винесені сюди одним місцем.
    /// </summary>
    public static class OpeningContent
    {
        /// <summary>ПЛЕЙСХОЛДЕР: поріг тихого шляху вузла 1 (§2.2, «Persuade ≥ P»).</summary>
        public const int PassQuietThreshold = 7;

        /// <summary>ПЛЕЙСХОЛДЕР: поріг кривавого шляху вузла 1 (§2.2, «Tactics ≥ T»).</summary>
        public const int PassBloodyThreshold = 6;

        /// <summary>Іменний накопичувач: боярин десь домовляється.</summary>
        public const string TuharSourceId = "tuhar";

        /// <summary>
        /// Вузол 1, доба 1 — авангард орди на перевалі.
        ///
        /// Обидва пороги показуються ДО підтвердження: це перша в грі точка
        /// рішення, і на ній гравець вчиться, що шляхи різні не за кольором, а за
        /// ціною. Тихий шлях сіє `spoiled_stores` на другу добу, кривавий —
        /// рану виконавцю і страх громади.
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
        /// Доба 2 — зіпсовані запаси: слід тихого шляху. Авангард пішов, але
        /// забрав худобу, і склад рахує втрати.
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

            // Навичка та сама, що у ДОМЕНА поста (склад доповідає за Виживанням):
            // інакше людина, що стоїть на складі, не може розібратися з бідою
            // складу, і наслідок Найгірший за будь-якого вибору — це не рішення, а
            // пастка. Спіймано прогоном зрізу, а не міркуванням.
            QuietPathSkill = SkillKeys.Survival,
            QuietPathThreshold = 5,
            QuietPathApproach = ApproachForm.Neutral,

            RelevantPositionId = "storehouse_dock",
            TensionByBand = new[] { 25, 8, -8, -20 }
        };

        /// <summary>
        /// Доба 3 — поранена в набігу дівчинка.
        ///
        /// Урок розстановки: якщо лазарет пустий, розбиратися нікому, і полоса
        /// наслідку Найгірша (чеклист §3 стор. 7). Це не підступність дизайну —
        /// ціна пустого поста повинна бути видимою, інакше розстановка не рішення.
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
        /// Джерело, яке спрацьовує у ПРИЗНАЧЕНІ доби і тільки в них.
        ///
        /// Відкриття поставлене автором: вузол на першу добу, зіпсовані
        /// запаси на другу, дівчинка на третю (§2.2). Віддати це випадковості
        /// накопичувачів означало б, що перша ігрова година щоразу інша —
        /// а її якраз і перевіряють за чеклистом.
        ///
        /// Зроблено джерелом, а не гілкою в консолі, щоб зріз і харнес
        /// бачили те саме: послідовність — частина контенту.
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
            public int CooldownDays => 999;   // один раз за зріз

            public bool IsActive(PulseContext ctx) => ctx.Day == _day && !ctx.IsNight;

            /// <summary>
            /// Авторська сцена про себе не попереджає: її «передвісник» —
            /// сама постановка. Інакше поставлений вузол сипле драбиною чуток
            /// про себе самого, і справжні передвісники в ній тонуть.
            /// </summary>
            public bool Announces => false;
            public int InsistencePerDay(PulseContext ctx) => ctx.Day == _day && !ctx.IsNight ? 1 : 0;
        }

        /// <summary>Авторська послідовність відкриття: який вузол у які доби.</summary>
        public static List<IPressureSource> ScriptedSources() => new List<IPressureSource>
        {
            new ScriptedSource("opening.pass", "перевал", 1),
            new ScriptedSource("opening.stores", "склад", 2),
            new ScriptedSource("opening.child", "лазарет", 3)
        };

        /// <summary>Увесь контент відкриття — для таблиці інцидентів зрізу.</summary>
        public static List<IncidentDefinition> All() => new List<IncidentDefinition>
        {
            PassVanguard(), SpoiledStores(), SickChild()
        };

        /// <summary>
        /// Іменний накопичувач «Тугар»: копить повільно і з перших діб, щоб
        /// перша ступінь передвісника дійшла до гравця на 3–4 добу (§2.2).
        /// Чутка про боярина — телеграфія тієї самої людини, яку гравець бачив у
        /// сцені відкриття.
        ///
        /// Свого інциденту у «Тугара» немає: його розв'язка — сценарний Фінал, а
        /// не спрацювання пульсу. Дійшовши до порога, накопичувач не розряджається
        /// (WorldPulse.Advance питає таблицю, чи є чим спрацювати) і
        /// тримає почуту третю ступінь до Фіналу, а не починає драбину
        /// знову.
        /// </summary>
        public sealed class TuharPressureSource : IPressureSource
        {
            public string Id => TuharSourceId;
            public WorldEventKind Kind => WorldEventKind.InternalThreat;
            public string DomainTag => "перевал";
            public int Threshold => 60;
            public int CooldownDays => 6;

            /// <summary>Боярин домовляється незалежно від того, чи спокійно в місті.</summary>
            public bool IsActive(PulseContext ctx) => true;

            /// <summary>Чутка про боярина — саме передвісник: він і зобов'язаний дійти (§2.2).</summary>
            public bool Announces => true;

            public int InsistencePerDay(PulseContext ctx) => 12;
        }
    }
}
