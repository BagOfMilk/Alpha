using System;
using System.Collections.Generic;
using Game.Core.Stats;

namespace Game.Core.Health
{
    /// <summary>
    /// Шрам — вечная метка от тяжёлой раны (GDD §2.3 / Эпик 4). Живёт на отдельном
    /// треке (<see cref="ScarTrack"/>), НЕ занимает слоты трейтов-билда и даёт свои
    /// эффекты (напр. «Одноглазый» −точность). В Unity оборачивается ScriptableObject.
    /// </summary>
    [Serializable]
    public sealed class Scar
    {
        public string Id;
        public string DisplayName;

        /// <summary>Боевые модификаторы производных статов. Source = Scar.</summary>
        public List<StatModifier> Modifiers = new List<StatModifier>();

        /// <summary>Модификаторы к проверкам (рана мешает не только в бою).</summary>
        public List<SkillCheckModifier> CheckModifiers = new List<SkillCheckModifier>();

        public Scar() { }

        public Scar(string id, string displayName)
        {
            Id = id;
            DisplayName = displayName;
        }

        public int CheckModifierFor(SkillType skill)
        {
            int sum = 0;
            for (int i = 0; i < CheckModifiers.Count; i++)
                if (CheckModifiers[i].Skill == skill) sum += CheckModifiers[i].Value;
            return sum;
        }

        // ---- Флюент-хелперы для авторинга контента ----
        public Scar WithCombat(DerivedStat stat, double value, ModMode mode = ModMode.Flat)
        {
            Modifiers.Add(new StatModifier(stat, value, mode, ModifierSource.Scar));
            return this;
        }

        public Scar WithCheck(SkillType skill, int value)
        {
            CheckModifiers.Add(new SkillCheckModifier(skill, value));
            return this;
        }
    }
}
