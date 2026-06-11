using System.Collections.Generic;
using Game.Core.Stats;

namespace Game.Core.Health
{
    /// <summary>
    /// Вечный трек шрамов (GDD §2.3 US-2.5). Шрамы НЕ занимают слоты трейтов и
    /// НЕСБРАСЫВАЕМЫ — поэтому здесь есть только добавление; метода удаления нет
    /// намеренно (раны метят навсегда). Их эффекты вливаются в общий агрегатор
    /// модификаторов наравне с гиром/трейтами.
    /// </summary>
    [System.Serializable]
    public sealed class ScarTrack
    {
        private readonly List<Scar> _scars = new List<Scar>();

        public IReadOnlyList<Scar> Scars => _scars;
        public int Count => _scars.Count;

        public void Add(Scar scar)
        {
            if (scar != null) _scars.Add(scar);
        }

        public bool Has(string id)
        {
            for (int i = 0; i < _scars.Count; i++)
                if (_scars[i].Id == id) return true;
            return false;
        }

        public IEnumerable<StatModifier> Modifiers()
        {
            for (int i = 0; i < _scars.Count; i++)
            {
                var mods = _scars[i].Modifiers;
                for (int j = 0; j < mods.Count; j++) yield return mods[j];
            }
        }

        public int CheckModifierFor(SkillType skill)
        {
            int sum = 0;
            for (int i = 0; i < _scars.Count; i++) sum += _scars[i].CheckModifierFor(skill);
            return sum;
        }
    }
}
