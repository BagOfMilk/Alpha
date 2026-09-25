using System;
using System.Collections.Generic;
using Game.Core.Stats;

namespace Game.Core.Characters.Scars
{
    /// <summary>
    /// Шрам — вічна позначка (GDD US-2.5). Дає свої ефекти, але слот трейта
    /// не займає: білд лишається вибором гравця, а історія лишається видимою.
    /// </summary>
    [Serializable]
    public sealed class ScarDefinition
    {
        public string Id;
        public string DisplayName;

        /// <summary>З якого тіру поранення шрам узагалі може дістатися (US-4.2).</summary>
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
    /// Вічний трек шрамів.
    ///
    /// Методу видалення тут НЕМАЄ, і це не недогляд: «усі шрами незнімні»
    /// (US-2.5) тримається типом, а не дисципліною того, хто викликає. Зняти шрам
    /// не можна просто тому, що для цього немає API.
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

        /// <summary>
        /// Відновлення зі збереження: трек стає рівно таким, яким був у зліпку.
        /// <c>internal</c> навмисно — у грі шрам не знімається (див. коментар
        /// класу), і Game.Gameplay цього методу не бачить; заміна існує лише
        /// для того, щоб завантаження повертало збережену історію.
        /// </summary>
        internal void RestoreFromSave(IEnumerable<ScarDefinition> scars)
        {
            _scars.Clear();
            if (scars == null) return;
            foreach (var scar in scars) Add(scar);
        }

        /// <summary>Повторне присвоєння того самого шраму нічого не змінює.</summary>
        public bool Add(ScarDefinition scar)
        {
            if (scar == null || string.IsNullOrEmpty(scar.Id)) return false;
            if (Has(scar.Id)) return false;
            _scars.Add(scar);
            return true;
        }

        /// <summary>
        /// Чи дістається шрам за рану такої тяжкості. Легкі рани слідів не
        /// лишають — це пряме правило US-4.2, а не тюнінг.
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
