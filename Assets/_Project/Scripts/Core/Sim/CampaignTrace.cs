using System.Collections.Generic;
using Game.Core.Loop;

namespace Game.Core.Sim
{
    /// <summary>
    /// Одна фаза кампании со всеми числами, включая скрытые.
    ///
    /// ВЕСЬ тип internal, как и всё в этом namespace. Сборка Game.Gameplay его
    /// не увидит, поэтому собрать дашборд из трассы физически нельзя — это
    /// инструмент дизайнера, а не окно игры (инвариант 3, Поправка №3.4).
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

        /// <summary>Накопители, ударившие в эту фазу. Через ';'.</summary>
        public string FiredSources = string.Empty;

        /// <summary>Полосы исхода случившихся инцидентов. Через ';'.</summary>
        public string OutcomeBands = string.Empty;

        /// <summary>Часть ростера в эту фазу вне города (вылазка).</summary>
        public bool PartyAway;

        /// <summary>Сколько очков шкалы прибавил каждый драйвер в эту фазу.</summary>
        public Dictionary<string, int> TensionByDriver = new Dictionary<string, int>();

        /// <summary>Заряд каждого накопителя: то, ради чего трасса и существует.</summary>
        public Dictionary<string, int> Charges = new Dictionary<string, int>();
        /// <summary>Ступень, которую игрок реально услышал.</summary>
        public Dictionary<string, int> Delivered = new Dictionary<string, int>();
    }

    /// <summary>Политика прогона: чем именно занят игрок всю кампанию.</summary>
    internal enum SimPolicy
    {
        /// <summary>Ничего не делает: спит каждую ночь, в квестах не участвует.</summary>
        Passive = 0,
        /// <summary>Патрулирует каждую ночь — единственная контригра, что есть в Э1.</summary>
        PatrolEveryNight = 1,
        /// <summary>Спит, но раз в десять суток делает крупный выбор в квесте.</summary>
        AggressiveChoices = 2,
        /// <summary>
        /// Партия периодически уходит в вылазку, оставляя посты пустыми.
        /// Кто именно и когда — решает вызывающий через колбэк: симулятор
        /// не знает про ростер и знать не должен.
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
    /// Сводка по трассе. Именно её читают тесты темпа: утверждение о кампании
    /// («первый инцидент между такими-то сутками») проверяется по распределению,
    /// а не по одной точке.
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

        /// <summary>Смены полосы, не породившие сигнала — прямое нарушение инварианта 4.</summary>
        public int BandChangesWithoutSignal;

        public int LongestStreakWithoutDelta;
        public int LongestStreakWithoutIncident;

        public double SignalsPerPhase;
        public int DistinctTopics;
        public int MaxTopicRepeats;
        public string MostRepeatedTopic = string.Empty;

        /// <summary>Сколько раз лестница предвестников прошла 1 → 2 → 3 целиком.</summary>
        public int FullLadders;
        /// <summary>Сколько раз ступень была перепрыгнута.</summary>
        public int SkippedLadderSteps;

        public int FinalBand;
        public int FinalPopulation;

        /// <summary>
        /// БЮДЖЕТ ДАВЛЕНИЯ: кто именно загнал город наверх. Без этой разбивки
        /// «Напряжение только растёт» — наблюдение, а не диагноз: непонятно,
        /// виноват фоновый тик (то есть само течение времени) или исходы
        /// событий (то есть игра игрока).
        /// </summary>
        public Dictionary<string, int> TensionByDriver = new Dictionary<string, int>();

        /// <summary>Сколько инцидентов разошлось по полосам исхода.</summary>
        public int[] OutcomeCounts = new int[4];

        /// <summary>
        /// СТЫК ДВУХ ЛУПОВ. Те же исходы, разложенные на «партия дома» и
        /// «партия в вылазке». Если вылазка ничего не стоит городу, эти
        /// столбцы совпадут — и тогда ось «время против риска» пуста.
        /// </summary>
        public int PhasesAway;
        public int PhasesHome;
        public int[] OutcomesAway = new int[4];
        public int[] OutcomesHome = new int[4];
        public int TensionGainAway;
        public int TensionGainHome;
    }
}
