using System.Collections.Generic;
using Game.Core.Threats;

namespace Game.Core.Base
{
    /// <summary>
    /// Сводка продвижения «мирного» времени на базе (GDD §1). Материалы тут НЕ
    /// производятся — они приходят с вылазок; за это время лечатся раненые,
    /// достраиваются стройки, растёт население и тикают скрытые угрозы.
    /// </summary>
    public sealed class CycleReport
    {
        public int FromDay;
        public int ToDay;
        public int DaysAdvanced;

        /// <summary>Напарники, полностью вылечившиеся за это время.</summary>
        public readonly List<string> Recovered = new List<string>();

        /// <summary>Завершённые за это время стройки/апгрейды.</summary>
        public readonly List<string> ConstructionCompleted = new List<string>();

        /// <summary>Население после продвижения времени.</summary>
        public int Population;

        /// <summary>Инциденты «Напряжения» за период (если подключена система угроз).</summary>
        public readonly List<IncidentReport> Incidents = new List<IncidentReport>();

        /// <summary>Качественная полоса Напряжения после периода (число скрыто, US-17.2).</summary>
        public TensionBand TensionBand = TensionBand.Calm;
    }
}
