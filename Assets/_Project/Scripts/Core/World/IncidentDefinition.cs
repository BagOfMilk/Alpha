using Game.Core.Checks;
using Game.Core.Pressure;

namespace Game.Core.World
{
    /// <summary>Чим криза б'є. Список закритий: кожен наслідок вимагає своєї системи.</summary>
    public enum CrisisBite
    {
        /// <summary>Поранення напарника, що виводить його з ладу надовго.</summary>
        WoundCompanion = 0,
        /// <summary>Смерть напарника. Незворотна (US-11.1).</summary>
        KillCompanion = 1,
        /// <summary>Відтік населення.</summary>
        PopulationOutflow = 2
    }

    /// <summary>
    /// Інцидент як ДАНІ (US-11.3). В Unity стане ScriptableObject; тут —
    /// чистий C#, щоб ядро тестувалося без рушія.
    ///
    /// Ключове поле — <see cref="QuietPathSkill"/>: у кожного інциденту ОБОВ'ЯЗКОВО
    /// має бути ненасильницький шлях (Поправка №1). Це перевіряється тестом за
    /// всім контентом, а не лишається на совісті автора.
    /// </summary>
    public sealed class IncidentDefinition
    {
        public string Id;
        public string TopicId;
        public string DomainTag;

        /// <summary>
        /// Який накопичувач породжує цей інцидент. Порожньо — підходить будь-якому
        /// (зручно для вузьких тестів). Без цього зв'язку передвісник називає один
        /// домен, а приходить подія із зовсім іншого.
        /// </summary>
        public string SourceId;

        /// <summary>З якої полоси Напруги інцидент взагалі можливий.</summary>
        public TensionBand MinBand = TensionBand.Murmur;
        /// <summary>Вище цієї полоси дрібницю витісняють серйозні речі.</summary>
        public TensionBand MaxBand = TensionBand.Fracture;

        public int MinTier = 1;
        /// <summary>Вага в таблиці відбору; більше — частіше (US-11.3).</summary>
        public int Weight = 10;

        /// <summary>Лише нічний інцидент (US-1.5: ніч — вікно загроз).</summary>
        public bool NightOnly;

        /// <summary>Тихий шлях: навичка і поріг. ОБОВ'ЯЗКОВИЙ.</summary>
        public SkillKey QuietPathSkill;
        public int QuietPathThreshold = 5;
        public ApproachForm QuietPathApproach = ApproachForm.Neutral;

        /// <summary>Кривавий шлях: швидше, але дорожче за наслідками.</summary>
        public SkillKey BloodyPathSkill;
        public int BloodyPathThreshold = 4;

        /// <summary>Посада, чий держатель розбирається з цим (US-8.2).</summary>
        public string RelevantPositionId;

        /// <summary>Дельта Напруги за полосами наслідку: Найгірша → Найкраща.</summary>
        public int[] TensionByBand = { 40, 15, -10, -30 };

        public bool IsCrisis;
        public CrisisBite Bite = CrisisBite.PopulationOutflow;
        public int PopulationLoss = 20;

        /// <summary>
        /// Скільки людей приходить у місто, якщо розбір вдався на Хорошу або
        /// Найкращу полосу (Поправка №6.3: «прийти по івенту»). Нуль — подія
        /// людей не приводить.
        /// </summary>
        public int ArrivalsOnGood;

        /// <summary>Чи є в інциденту прописаний тихий шлях.</summary>
        public bool HasQuietPath => !QuietPathSkill.IsNone;

        /// <summary>Чи є кривавий шлях. Обов'язковим він не є (Поправка №1).</summary>
        public bool HasBloodyPath => !BloodyPathSkill.IsNone;
    }

    /// <summary>Що сталося за підсумком — публічно, бо це вже відбулося.</summary>
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

        /// <summary>
        /// Скільки людей прийшло в місто за підсумками розбору (Поправка №6.3:
        /// «прийти по івенту»). Нуль у більшості подій.
        /// </summary>
        public readonly int PeopleArrived;

        /// <summary>
        /// Розбір налякав громаду: кривавий шлях або провалене залякування.
        /// Шар сигналів зобов'язаний це озвучити — страх не має права прийти мовчки.
        /// </summary>
        public readonly bool CausedFear;

        public IncidentOutcome(string incidentId, string topicId, string domainTag, OutcomeBand band,
            bool wasUnmanned, bool wasCrisis, string affectedActorId, CrisisBite? bite, int populationLost,
            bool causedFear = false, int peopleArrived = 0)
        {
            CausedFear = causedFear;
            PeopleArrived = peopleArrived;
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
