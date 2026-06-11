using System.Collections.Generic;

namespace Game.Core.Base
{
    /// <summary>
    /// Сводка продвижения «мирного» времени на базе (GDD §1). Материалы тут НЕ
    /// производятся — они приходят с вылазок; за это время лечатся раненые,
    /// достраиваются стройки и растёт население.
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
    }
}
