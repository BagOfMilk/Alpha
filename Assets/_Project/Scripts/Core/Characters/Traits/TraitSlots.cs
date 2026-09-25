using System.Collections.Generic;
using Game.Core.Stats;

namespace Game.Core.Characters.Traits
{
    /// <summary>Наслідок спроби взяти трейт.</summary>
    public enum TraitAddResult
    {
        Added = 0,
        AlreadyPresent = 1,
        SlotsFull = 2,   // US-2.4: гравець обирає — витіснити наявний або відмовитися
        Invalid = 3
    }

    /// <summary>
    /// Активні трейти персонажа у фіксованій кількості слотів (GDD US-2.4).
    ///
    /// Переповнення не кидає виняток і не втрачає трейт мовчки, а повертає
    /// SlotsFull — бо за дизайном це розвилка для гравця, а не помилка.
    /// Інтерфейс перетворює її на діалог «витіснити чи відмовитися».
    /// </summary>
    public sealed class TraitSlots : IModifierProvider
    {
        private readonly List<TraitDefinition> _active = new List<TraitDefinition>();

        public TraitSlots(int capacity)
        {
            Capacity = capacity < 0 ? 0 : capacity;
        }

        /// <summary>Місткість береться з балансу — кількість слотів тюниться без перекомпіляції.</summary>
        public static TraitSlots FromConfig(Game.Core.Balance.BalanceConfig cfg)
            => new TraitSlots(cfg == null ? 0 : cfg.TraitSlots);

        public int Capacity { get; }
        public IReadOnlyList<TraitDefinition> Active => _active;
        public bool IsFull => _active.Count >= Capacity;

        public bool Has(string traitId) => IndexOf(traitId) >= 0;

        public TraitAddResult TryAdd(TraitDefinition trait)
        {
            if (trait == null || string.IsNullOrEmpty(trait.Id)) return TraitAddResult.Invalid;
            if (Has(trait.Id)) return TraitAddResult.AlreadyPresent;
            if (IsFull) return TraitAddResult.SlotsFull;

            _active.Add(trait);
            return TraitAddResult.Added;
        }

        /// <summary>
        /// Витіснити один трейт іншим — друга половина розвилки US-2.4.
        /// Обмін атомарний: якщо вхідний не годиться, вихідний лишається на місці.
        /// </summary>
        public bool Replace(string outgoingId, TraitDefinition incoming)
        {
            if (incoming == null || string.IsNullOrEmpty(incoming.Id)) return false;
            if (Has(incoming.Id)) return false;

            int i = IndexOf(outgoingId);
            if (i < 0) return false;

            _active[i] = incoming;
            return true;
        }

        public bool Remove(string traitId)
        {
            int i = IndexOf(traitId);
            if (i < 0) return false;
            _active.RemoveAt(i);
            return true;
        }

        public void CollectModifiers(List<StatModifier> into)
        {
            if (into == null) return;
            for (int i = 0; i < _active.Count; i++)
            {
                var mods = _active[i].Modifiers;
                if (mods == null) continue;
                for (int j = 0; j < mods.Count; j++) into.Add(mods[j]);
            }
        }

        /// <summary>Цінності всіх активних трейтів — вхід для зв'язків ростера (US-9.6).</summary>
        public IReadOnlyList<string> Values
        {
            get
            {
                var all = new List<string>();
                for (int i = 0; i < _active.Count; i++)
                {
                    var v = _active[i].Values;
                    if (v == null) continue;
                    for (int j = 0; j < v.Count; j++)
                        if (!all.Contains(v[j])) all.Add(v[j]);
                }
                return all;
            }
        }

        private int IndexOf(string traitId)
        {
            if (string.IsNullOrEmpty(traitId)) return -1;
            for (int i = 0; i < _active.Count; i++)
                if (_active[i].Id == traitId) return i;
            return -1;
        }
    }
}
