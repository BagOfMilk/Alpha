using System.Collections.Generic;
using Game.Core.Economy;

namespace Game.Core.Base
{
    /// <summary>
    /// Зведення за підсумками одного циклу (дня). Повертається з AdvanceCycle і
    /// зручна для показу гравцю: «що виробила база за день».
    /// </summary>
    public sealed class CycleReport
    {
        public int Cycle;

        /// <summary>Вироблені ресурси за цикл (resource -> скільки).</summary>
        public readonly Dictionary<ResourceType, int> Produced = new Dictionary<ResourceType, int>();

        /// <summary>Накопичені пасивні бонуси за цикл (bonusId -> величина).</summary>
        public readonly Dictionary<string, int> PassiveBonuses = new Dictionary<string, int>();

        /// <summary>Напарники, що підвищили рівень у цьому циклі.</summary>
        public readonly List<string> LeveledUp = new List<string>();

        /// <summary>Напарники, що повністю вилікувались у цьому циклі.</summary>
        public readonly List<string> Recovered = new List<string>();

        /// <summary>Не вистачило їжі на утримання поселення.</summary>
        public bool FoodShortage;

        /// <summary>
        /// R11: скільки рівнів постова XP дала протагоністу в цьому циклі
        /// (0, якщо протагоніст не призначений на пост або не підняв рівень).
        /// BaseState.AdvanceCycle не витрачає ці рівні за протагоніста сама —
        /// GameSession читає це поле і банкує очки через SpendablePoints,
        /// тим самим шляхом, що й бойова/квестова/інцидентна XP.
        /// </summary>
        public int ProtagonistLevelsGained;

        internal void AddProduced(ResourceType resource, int amount)
        {
            if (resource == ResourceType.None || amount == 0) return;
            Produced.TryGetValue(resource, out var cur);
            Produced[resource] = cur + amount;
        }

        internal void AddPassive(string bonusId, int amount)
        {
            if (string.IsNullOrEmpty(bonusId) || amount == 0) return;
            PassiveBonuses.TryGetValue(bonusId, out var cur);
            PassiveBonuses[bonusId] = cur + amount;
        }
    }
}
