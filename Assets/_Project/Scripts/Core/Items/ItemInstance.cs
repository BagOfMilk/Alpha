using System;
using System.Collections.Generic;
using Game.Core.Combat;
using Game.Core.Stats;

namespace Game.Core.Items
{
    /// <summary>
    /// Рантайм-экземпляр предмета: определение + конкретная редкость + СВёрнутые
    /// роллы статов (Source = Gear, уходят в единый агрегатор). Для дропа роллы
    /// катаются в [Min..Max] × множитель редкости (Вариант Б); для именного —
    /// фиксированы. Уникальный эффект именного добавляет модификаторы поверх.
    /// </summary>
    public sealed class ItemInstance
    {
        public ItemDefinition Definition { get; }
        public Rarity Rarity { get; private set; }

        private readonly List<StatModifier> _mods = new List<StatModifier>();
        public IReadOnlyList<StatModifier> StatMods => _mods;

        public EquipSlot Slot => Definition.Slot;
        public WeaponDefinition Weapon => Definition.Weapon;
        public string DisplayName => Definition.DisplayName;

        public ItemInstance(ItemDefinition definition, Rarity rarity, IRng rng)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            Resolve(rarity, rng);
        }

        /// <summary>Именной предмет: фиксированные роллы и редкость (детерминирован).</summary>
        public static ItemInstance NamedFrom(ItemDefinition def)
            => new ItemInstance(def, def.NamedRarity, new ScriptedRng());

        /// <summary>
        /// Восстановление из сейва (US-16.1): редкость и УЖЕ свёрнутые БАЗОВЫЕ роллы
        /// (<see cref="StatMods"/>) берутся как есть — без повторного броска, чтобы гир
        /// после загрузки был идентичен. Эффект именного добавится из Definition сам.
        /// </summary>
        public static ItemInstance FromSaved(ItemDefinition def, Rarity rarity, IEnumerable<StatModifier> mods)
        {
            var inst = new ItemInstance(def, rarity, null);
            inst._mods.Clear();
            if (mods != null) inst._mods.AddRange(mods);
            return inst;
        }

        /// <summary>Все модификаторы: базовые роллы + уникальный эффект (Source = Gear).</summary>
        public IEnumerable<StatModifier> Modifiers()
        {
            for (int i = 0; i < _mods.Count; i++) yield return _mods[i];
            if (Definition.Effect != null)
                foreach (var m in Definition.Effect.ExtraModifiers()) yield return m;
        }

        /// <summary>
        /// Крафт-апгрейд (US-6.3): поднимает редкость и МАСШТАБИРУЕТ уже выпавшие
        /// значения новым множителем — без нового броска. Так апгрейд монотонен:
        /// раньше пере-ролл мог выдать значение НИЖЕ прежнего (диапазоны редкостей
        /// перекрываются), и игрок платил дефицитным крафт-компонентом за просадку.
        /// Побочно снимается и сейв-скам (детерминированно, RNG не участвует).
        /// </summary>
        internal void UpgradeTo(Rarity rarity)
        {
            if (Definition.IsNamed) return; // именные фиксированы (US-6.1)
            double from = RarityTuning.MagnitudeMultiplier(Rarity);
            double to = RarityTuning.MagnitudeMultiplier(rarity);
            Rarity = rarity;
            if (from <= 0) return;

            for (int i = 0; i < _mods.Count; i++)
            {
                var m = _mods[i];
                int scaled = (int)Math.Round(m.Value * to / from, MidpointRounding.AwayFromZero);
                if (scaled == (int)m.Value) scaled = (int)m.Value + 1; // апгрейд обязан быть заметен
                _mods[i] = new StatModifier(m.Stat, scaled, m.Mode, m.Source);
            }
        }

        /// <summary>Пере-катать роллы на заданной редкости (генерация дропа/тесты).</summary>
        internal void RerollAt(Rarity rarity, IRng rng) => Resolve(rarity, rng);

        private void Resolve(Rarity rarity, IRng rng)
        {
            Rarity = rarity;
            _mods.Clear();
            // Именные фиксированы: роллов не катают и магнитудой редкости не множатся (US-6.1).
            double mult = Definition.IsNamed ? 1.0 : RarityTuning.MagnitudeMultiplier(rarity);
            var rolls = Definition.StatRolls;
            for (int i = 0; i < rolls.Count; i++)
            {
                var r = rolls[i];
                int baseRoll = r.Min >= r.Max ? r.Min : (rng != null ? rng.Range(r.Min, r.Max) : r.Min);
                // AwayFromZero, а не банковское округление по умолчанию: множители
                // редкости (1.0/1.5/2.0/2.5) на целых роллах регулярно дают ровно .5,
                // и «к чётному» сворачивало бы 4.5→4, но 7.5→8 — магнитуда зависела
                // бы от чётности. Правило то же, что в UpgradeTo.
                int value = (int)Math.Round(baseRoll * mult, MidpointRounding.AwayFromZero);
                if (value != 0) _mods.Add(new StatModifier(r.Stat, value)); // Mode=Flat, Source=Gear
            }
        }
    }
}
