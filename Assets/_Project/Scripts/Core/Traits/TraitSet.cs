using System.Collections.Generic;
using Game.Core.Stats;

namespace Game.Core.Traits
{
    /// <summary>Исход попытки добавить трейт в набор со слотами.</summary>
    public enum TraitAddResult
    {
        Added = 0,
        NeedsEviction = 1, // слотов не хватает — нужен выбор: вытеснить существующий
        AlreadyPresent = 2,
        Rejected = 3
    }

    /// <summary>
    /// Активные трейты персонажа с фикс числом слотов (GDD §2.4 US-2.4). Новый
    /// трейт сверх лимита НЕ добавляется молча — требует явного выбора вытеснения
    /// (<see cref="AddEvicting"/>), чтобы персонаж не оброс нечитаемым числом ярлыков.
    /// Шрамы тут НЕ живут — у них отдельный вечный трек.
    /// </summary>
    [System.Serializable]
    public sealed class TraitSet
    {
        private readonly List<Trait> _traits = new List<Trait>();

        public int MaxSlots { get; set; }

        public TraitSet(int maxSlots)
        {
            MaxSlots = maxSlots < 0 ? 0 : maxSlots;
        }

        public IReadOnlyList<Trait> Traits => _traits;

        public int UsedSlots
        {
            get
            {
                int s = 0;
                for (int i = 0; i < _traits.Count; i++) s += _traits[i].SlotCost;
                return s;
            }
        }

        public int FreeSlots => MaxSlots - UsedSlots;

        public bool Contains(string id)
        {
            for (int i = 0; i < _traits.Count; i++)
                if (_traits[i].Id == id) return true;
            return false;
        }

        /// <summary>
        /// Пытается добавить трейт. Если слотов хватает — добавляет; иначе НЕ
        /// добавляет и возвращает <see cref="TraitAddResult.NeedsEviction"/>.
        /// </summary>
        public TraitAddResult TryAdd(Trait trait)
        {
            if (trait == null || string.IsNullOrEmpty(trait.Id)) return TraitAddResult.Rejected;
            if (Contains(trait.Id)) return TraitAddResult.AlreadyPresent;
            if (trait.SlotCost <= FreeSlots)
            {
                _traits.Add(trait);
                return TraitAddResult.Added;
            }
            return TraitAddResult.NeedsEviction;
        }

        /// <summary>Добавляет трейт, вытеснив указанный (выбор игрока при переполнении).</summary>
        public bool AddEvicting(Trait trait, string evictId)
        {
            if (trait == null) return false;
            Remove(evictId);
            return TryAdd(trait) == TraitAddResult.Added;
        }

        public bool Remove(string id)
        {
            for (int i = 0; i < _traits.Count; i++)
                if (_traits[i].Id == id) { _traits.RemoveAt(i); return true; }
            return false;
        }

        public int CheckModifierFor(SkillType skill)
        {
            int sum = 0;
            for (int i = 0; i < _traits.Count; i++) sum += _traits[i].CheckModifierFor(skill);
            return sum;
        }

        public IEnumerable<StatModifier> CombatModifiers()
        {
            for (int i = 0; i < _traits.Count; i++)
            {
                var mods = _traits[i].CombatModifiers;
                for (int j = 0; j < mods.Count; j++) yield return mods[j];
            }
        }
    }
}
