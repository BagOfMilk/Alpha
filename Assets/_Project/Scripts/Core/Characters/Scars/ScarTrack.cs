using System;
using System.Collections.Generic;
using Game.Core.Stats;

namespace Game.Core.Characters.Scars
{
    /// <summary>
    /// Шрам — вечная отметина (GDD US-2.5). Даёт свои эффекты, но слот трейта
    /// не занимает: билд остаётся выбором игрока, а история остаётся видимой.
    /// </summary>
    [Serializable]
    public sealed class ScarDefinition
    {
        public string Id;
        public string DisplayName;

        /// <summary>С какого тира ранения шрам вообще может достаться (US-4.2).</summary>
        public WoundTier MinTier = WoundTier.Serious;

        public List<StatModifier> Modifiers = new List<StatModifier>();

        public ScarDefinition() { }

        public ScarDefinition(string id, string displayName, WoundTier minTier = WoundTier.Serious)
        {
            Id = id;
            DisplayName = displayName;
            MinTier = minTier;
        }

        public ScarDefinition WithModifier(StatKey key, double value, ModMode mode = ModMode.Flat)
        {
            Modifiers.Add(new StatModifier(key, value, mode, ModifierSource.Scar, Id));
            return this;
        }
    }

    /// <summary>
    /// Вечный трек шрамов.
    ///
    /// Метода удаления здесь НЕТ, и это не упущение: «все шрамы несбрасываемы»
    /// (US-2.5) держится типом, а не дисциплиной вызывающего. Снять шрам нельзя
    /// просто потому, что для этого нет API.
    /// </summary>
    public sealed class ScarTrack : IModifierProvider
    {
        private readonly List<ScarDefinition> _scars = new List<ScarDefinition>();

        public IReadOnlyList<ScarDefinition> Scars => _scars;
        public int Count => _scars.Count;

        public bool Has(string scarId)
        {
            if (string.IsNullOrEmpty(scarId)) return false;
            for (int i = 0; i < _scars.Count; i++)
                if (_scars[i].Id == scarId) return true;
            return false;
        }

        /// <summary>Повторное присвоение того же шрама ничего не меняет.</summary>
        public bool Add(ScarDefinition scar)
        {
            if (scar == null || string.IsNullOrEmpty(scar.Id)) return false;
            if (Has(scar.Id)) return false;
            _scars.Add(scar);
            return true;
        }

        /// <summary>
        /// Достаётся ли шрам за рану такой тяжести. Лёгкие раны следов не
        /// оставляют — это прямое правило US-4.2, а не тюнинг.
        /// </summary>
        public static bool IsEarnedBy(ScarDefinition scar, WoundTier tier)
            => scar != null && tier != WoundTier.None && tier >= scar.MinTier;

        public void CollectModifiers(List<StatModifier> into)
        {
            if (into == null) return;
            for (int i = 0; i < _scars.Count; i++)
            {
                var mods = _scars[i].Modifiers;
                if (mods == null) continue;
                for (int j = 0; j < mods.Count; j++) into.Add(mods[j]);
            }
        }
    }
}
