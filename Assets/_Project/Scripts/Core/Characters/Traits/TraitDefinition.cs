using System;
using System.Collections.Generic;
using Game.Core.Stats;

namespace Game.Core.Characters.Traits
{
    /// <summary>Знак трейта. Вади існують нарівні з чеснотами (GDD Е2.3).</summary>
    public enum TraitPolarity
    {
        Vice = -1,    // вада
        Neutral = 0,  // нейтральний
        Virtue = 1    // чеснота
    }

    /// <summary>
    /// Трейт — тег зі знаком, що займає слот (GDD Е2.3).
    ///
    /// Один і той самий список модифікаторів працює і на базі, і в бою: трейт не
    /// знає, де його читають, бо все йде в загальний агрегатор. Це і є
    /// «один ефект — одна система» — трейт не дублюється двома механіками.
    /// </summary>
    [Serializable]
    public sealed class TraitDefinition
    {
        public string Id;
        public string DisplayName;
        public TraitPolarity Polarity = TraitPolarity.Neutral;

        /// <summary>Що трейт змінює в числах. Йде в StatResolver як є.</summary>
        public List<StatModifier> Modifiers = new List<StatModifier>();

        /// <summary>
        /// Цінності персонажа: за ними сходяться і конфліктують напарники (US-9.6).
        /// Тут лише теги — сама механіка зв'язків живе в шарі ростера.
        /// </summary>
        public List<string> Values = new List<string>();

        /// <summary>Особливі опції перевірок, які трейт відкриває (US-2.6).</summary>
        public List<string> UnlocksOptionIds = new List<string>();

        public TraitDefinition() { }

        public TraitDefinition(string id, string displayName, TraitPolarity polarity = TraitPolarity.Neutral)
        {
            Id = id;
            DisplayName = displayName;
            Polarity = polarity;
        }

        public TraitDefinition WithModifier(StatKey key, double value, ModMode mode = ModMode.Flat)
        {
            Modifiers.Add(new StatModifier(key, value, mode, ModifierSource.Trait, Id));
            return this;
        }

        public TraitDefinition WithValue(string valueTag)
        {
            if (!string.IsNullOrEmpty(valueTag)) Values.Add(valueTag);
            return this;
        }
    }
}
