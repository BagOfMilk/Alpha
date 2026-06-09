using System;
using Game.Core.Stats;

namespace Game.Core.Characters
{
    /// <summary>
    /// Шаблон напарника (класс/архетип). Чистые данные: стартовые статы и
    /// профиль роста. В Unity оборачивается ScriptableObject, но логика создания
    /// напарника живёт здесь, чтобы её можно было тестировать без движка.
    /// </summary>
    [Serializable]
    public sealed class CompanionArchetype
    {
        public string Id;
        public string DisplayName;

        /// <summary>Стартовые характеристики на 1-м уровне.</summary>
        public StatBlock BaseStats = new StatBlock();

        /// <summary>Веса распределения очков при повышении уровня.</summary>
        public GrowthProfile Growth = new GrowthProfile();

        public CompanionArchetype() { }

        public CompanionArchetype(string id, string displayName)
        {
            Id = id;
            DisplayName = displayName;
        }

        /// <summary>Создаёт нового напарника 1-го уровня по этому архетипу.</summary>
        public Companion CreateInstance(string instanceId)
        {
            return new Companion(instanceId, this);
        }
    }
}
