using System;
using System.Collections.Generic;

namespace Game.Core.Stats
{
    /// <summary>
    /// Уровни десяти скилов. Sparse: невыкачанный скил = 0. Растут за очки скилов
    /// с уровней (классов нет, GDD §2.2). <b>Респека нет</b> — понижать нельзя,
    /// поэтому есть только <see cref="Raise"/>, а <see cref="Set"/> — для авторской
    /// раздачи стартовых значений (бэкграунд) и загрузки сейва.
    /// </summary>
    [Serializable]
    public sealed class SkillSet
    {
        private readonly Dictionary<SkillType, int> _levels = new Dictionary<SkillType, int>();

        public int Get(SkillType skill) => _levels.TryGetValue(skill, out var v) ? v : 0;

        /// <summary>Жёстко задаёт уровень скила (старт/сейв). 0 и меньше — удаляет запись.</summary>
        public void Set(SkillType skill, int level)
        {
            if (skill == SkillType.None) return;
            if (level <= 0) { _levels.Remove(skill); return; }
            _levels[skill] = level;
        }

        /// <summary>Повышает скил на delta (&gt;0). Респека нет: уменьшать нельзя.</summary>
        public void Raise(SkillType skill, int delta = 1)
        {
            if (skill == SkillType.None || delta <= 0) return;
            Set(skill, Get(skill) + delta);
        }

        public IReadOnlyDictionary<SkillType, int> Levels => _levels;

        public SkillSet Clone()
        {
            var copy = new SkillSet();
            foreach (var kv in _levels) copy._levels[kv.Key] = kv.Value;
            return copy;
        }
    }
}
