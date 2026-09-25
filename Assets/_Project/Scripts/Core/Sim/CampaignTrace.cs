using System.Collections.Generic;
using Game.Core.Loop;

namespace Game.Core.Sim
{
    /// <summary>
    /// Одна фаза кампанії з усіма числами, включно з прихованими.
    ///
    /// ВЕСЬ тип internal, як і все в цьому namespace. Збірка Game.Gameplay його
    /// не побачить, тому зібрати дашборд із траси фізично неможливо — це
    /// інструмент дизайнера, а не вікно гри (інваріант 3, Поправка №3.4).
    /// </summary>
    internal sealed class DayRow
    {
        public int Day;
        public DayPhase Phase;

        public int TensionValue;
        public int Band;
        public int DaysInBand;
        public int Population;

        public int SignalCount;
        public int DeltaCount;
        public bool HadBandSignal;

        public int IncidentCount;
        public bool HadCrisis;

        public string Topics = string.Empty;
        public string Channels = string.Empty;
        public string Incidents = string.Empty;
        public string Forewarnings = string.Empty;

        /// <summary>Накопичувачі, що вдарили в цю фазу. Через ';'.</summary>
        public string FiredSources = string.Empty;

        /// <summary>Полоси наслідку інцидентів, що сталися. Через ';'.</summary>
        public string OutcomeBands = string.Empty;

        /// <summary>Частина ростера в цю фазу поза містом (вилазка).</summary>
        public bool PartyAway;

        /// <summary>Скільки очок шкали додав кожен драйвер у цю фазу.</summary>
        public Dictionary<string, int> TensionByDriver = new Dictionary<string, int>();

        /// <summary>Заряд кожного накопичувача: те, заради чого траса й існує.</summary>
        public Dictionary<string, int> Charges = new Dictionary<string, int>();
        /// <summary>Ступінь, яку гравець справді почув.</summary>
        public Dictionary<string, int> Delivered = new Dictionary<string, int>();
    }

    /// <summary>Політика прогону: чим саме зайнятий гравець усю кампанію.</summary>
    internal enum SimPolicy
    {
        /// <summary>Нічого не робить: спить щоночі, у квестах не бере участі.</summary>
        Passive = 0,
        /// <summary>Патрулює щоночі — єдина контргра, що є в Е1.</summary>
        PatrolEveryNight = 1,
        /// <summary>Спить, але раз на десять діб робить велике рішення в квесті.</summary>
        AggressiveChoices = 2,
        /// <summary>
        /// Партія періодично йде у вилазку, лишаючи пости порожніми.
        /// Хто саме і коли — вирішує викликач через колбек: симулятор
        /// не знає про ростер і знати не повинен.
        /// </summary>
        Expedition = 3
    }

    internal sealed class CampaignTrace
    {
        public SimPolicy Policy;
        public int Tier;
        public List<DayRow> Rows = new List<DayRow>();
        public List<string> TrackIds = new List<string>();
    }

    /// <summary>
    /// Зведення за трасою. Саме її читають тести темпу: твердження про кампанію
    /// («перший інцидент між такими-то добами») перевіряється за розподілом,
    /// а не за однією точкою.
    /// </summary>
    internal sealed class CampaignMetrics
    {
        public SimPolicy Policy;
        public int Tier;
        public int Days;
        public int Phases;

        public int FirstIncidentDay = -1;
        public int FirstCrisisDay = -1;
        public int FirstBandChangeDay = -1;
        public int FirstDeltaDay = -1;
        public int FirstForewarnDay = -1;

        public int IncidentTotal;
        public int CrisisTotal;
        public int BandChangesTotal;

        /// <summary>Зміни полоси, що не породили сигналу — пряме порушення інваріанту 4.</summary>
        public int BandChangesWithoutSignal;

        public int LongestStreakWithoutDelta;
        public int LongestStreakWithoutIncident;

        public double SignalsPerPhase;
        public int DistinctTopics;
        public int MaxTopicRepeats;
        public string MostRepeatedTopic = string.Empty;

        /// <summary>Скільки разів драбина передвісників пройшла 1 → 2 → 3 цілком.</summary>
        public int FullLadders;
        /// <summary>Скільки разів ступінь була перестрибнута.</summary>
        public int SkippedLadderSteps;

        public int FinalBand;
        public int FinalPopulation;

        /// <summary>
        /// БЮДЖЕТ ТИСКУ: хто саме загнав місто нагору. Без цього розбиття
        /// «Напруга тільки росте» — спостереження, а не діагноз: незрозуміло,
        /// винен фоновий тік (тобто саме плинення часу) чи наслідки
        /// подій (тобто гра гравця).
        /// </summary>
        public Dictionary<string, int> TensionByDriver = new Dictionary<string, int>();

        /// <summary>Скільки інцидентів розійшлося за полосами наслідку.</summary>
        public int[] OutcomeCounts = new int[4];

        /// <summary>
        /// СТИК ДВОХ ЛУПІВ. Ті самі наслідки, розкладені на «партія вдома» і
        /// «партія у вилазці». Якщо вилазка нічого не коштує місту, ці
        /// стовпці збіжаться — і тоді вісь «час проти ризику» порожня.
        /// </summary>
        public int PhasesAway;
        public int PhasesHome;
        public int[] OutcomesAway = new int[4];
        public int[] OutcomesHome = new int[4];
        public int TensionGainAway;
        public int TensionGainHome;
    }
}
