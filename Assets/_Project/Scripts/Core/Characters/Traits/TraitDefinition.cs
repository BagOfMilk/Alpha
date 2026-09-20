using System;
using System.Collections.Generic;
using Game.Core.Stats;

namespace Game.Core.Characters.Traits
{
    /// <summary>Знак трейта. Пороки существуют наравне с добродетелями (GDD Э2.3).</summary>
    public enum TraitPolarity
    {
        Vice = -1,    // порок
        Neutral = 0,  // нейтральный
        Virtue = 1    // добродетель
    }

    /// <summary>
    /// Трейт — тег со знаком, занимающий слот (GDD Э2.3).
    ///
    /// Один и тот же список модификаторов работает и на базе, и в бою: трейт не
    /// знает, где его читают, потому что всё уходит в общий агрегатор. Это и есть
    /// «один эффект — одна система» — трейт не дублируется двумя механиками.
    /// </summary>
    [Serializable]
    public sealed class TraitDefinition
    {
        public string Id;
        public string DisplayName;
        public TraitPolarity Polarity = TraitPolarity.Neutral;

        /// <summary>Что трейт меняет в числах. Уходит в StatResolver как есть.</summary>
        public List<StatModifier> Modifiers = new List<StatModifier>();

        /// <summary>
        /// Ценности персонажа: по ним сходятся и конфликтуют напарники (US-9.6).
        /// Здесь только теги — сама механика связей живёт в слое ростера.
        /// </summary>
        public List<string> Values = new List<string>();

        /// <summary>Особые опции проверок, которые трейт открывает (US-2.6).</summary>
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
