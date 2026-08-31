using Game.Core.Checks;
using Game.Core.Pressure;

namespace Game.Core.World
{
    /// <summary>Чем кризис бьёт. Список закрыт: каждый исход требует своей системы.</summary>
    public enum CrisisBite
    {
        /// <summary>Ранение напарника, выводящее его из строя надолго.</summary>
        WoundCompanion = 0,
        /// <summary>Смерть напарника. Необратима (US-11.1).</summary>
        KillCompanion = 1,
        /// <summary>Отток населения.</summary>
        PopulationOutflow = 2
    }

    /// <summary>
    /// Инцидент как ДАННЫЕ (US-11.3). В Unity станет ScriptableObject; здесь —
    /// чистый C#, чтобы ядро тестировалось без движка.
    ///
    /// Ключевое поле — <see cref="QuietPathSkill"/>: у каждого инцидента ОБЯЗАН
    /// быть ненасильственный путь (Поправка №1). Это проверяется тестом по всему
    /// контенту, а не остаётся на совести автора.
    /// </summary>
    public sealed class IncidentDefinition
    {
        public string Id;
        public string TopicId;
        public string DomainTag;

        /// <summary>С какой полосы Напряжения инцидент вообще возможен.</summary>
        public TensionBand MinBand = TensionBand.Murmur;
        /// <summary>Выше этой полосы мелочь вытесняется серьёзными вещами.</summary>
        public TensionBand MaxBand = TensionBand.Fracture;

        public int MinTier = 1;
        /// <summary>Вес в таблице отбора; больше — чаще (US-11.3).</summary>
        public int Weight = 10;

        /// <summary>Только ночной инцидент (US-1.5: ночь — окно угроз).</summary>
        public bool NightOnly;

        /// <summary>Тихий путь: навык и порог. ОБЯЗАТЕЛЕН.</summary>
        public SkillKey QuietPathSkill;
        public int QuietPathThreshold = 5;
        public ApproachForm QuietPathApproach = ApproachForm.Neutral;

        /// <summary>Кровавый путь: быстрее, но дороже по последствиям.</summary>
        public SkillKey BloodyPathSkill;
        public int BloodyPathThreshold = 4;

        /// <summary>Позиция, чей держатель разбирается с этим (US-8.2).</summary>
        public string RelevantPositionId;

        /// <summary>Дельта Напряжения по полосам исхода: Худшая → Лучшая.</summary>
        public int[] TensionByBand = { 40, 15, -10, -30 };

        public bool IsCrisis;
        public CrisisBite Bite = CrisisBite.PopulationOutflow;
        public int PopulationLoss = 20;

        /// <summary>Есть ли у инцидента прописанный тихий путь.</summary>
        public bool HasQuietPath => !QuietPathSkill.IsNone;
    }

    /// <summary>Что случилось по итогу — публично, потому что это уже произошло.</summary>
    public readonly struct IncidentOutcome
    {
        public readonly string IncidentId;
        public readonly string TopicId;
        public readonly string DomainTag;
        public readonly OutcomeBand Band;
        public readonly bool WasUnmanned;
        public readonly bool WasCrisis;
        public readonly string AffectedActorId;
        public readonly CrisisBite? Bite;
        public readonly int PopulationLost;

        public IncidentOutcome(string incidentId, string topicId, string domainTag, OutcomeBand band,
            bool wasUnmanned, bool wasCrisis, string affectedActorId, CrisisBite? bite, int populationLost)
        {
            IncidentId = incidentId;
            TopicId = topicId;
            DomainTag = domainTag;
            Band = band;
            WasUnmanned = wasUnmanned;
            WasCrisis = wasCrisis;
            AffectedActorId = affectedActorId;
            Bite = bite;
            PopulationLost = populationLost;
        }
    }
}
