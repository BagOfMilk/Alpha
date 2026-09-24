using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Core.Stats;

namespace Game.Core.Items
{
    /// <summary>
    /// Рантайм-екземпляр предмета: визначення + конкретна рідкість + СВЕРНУТІ
    /// модифікатори (Source = Gear, ідуть у єдиний агрегатор, US-18.2). Жодної
    /// випадковості (інваріант 1, R1 специфікації тестової збірки): значення —
    /// детермінована функція (визначення, рідкість); повторний Resolve з тими
    /// самими вхідними даними завжди дає той самий результат.
    /// </summary>
    public sealed class ItemInstance
    {
        // Лічильник для InstanceId — НЕ джерело випадковості (інваріант 1):
        // просте монотонне зростання, детерміноване порядком створення
        // об'єктів у процесі, не System.Random/хеш/сід. Guid.NewGuid() свідомо
        // не використаний — RFC 4122 v4 бере ентропію з ОС, це і є прихований
        // источник випадковості в ядрі.
        private static long _nextInstanceSeq = 1;

        public ItemDefinition Definition { get; }
        public Rarity Rarity { get; private set; }

        /// <summary>
        /// Стабільний ідентифікатор ЦЬОГО конкретного екземпляра (на відміну
        /// від Definition.Id, спільного для всіх дропів однієї бази) — потрібен
        /// контракту GameSession (docs/TEST_BUILD.md §4.1): Equip(companionId,
        /// itemInstanceId, slot) і CraftUpgrade(itemInstanceId) адресують ОДИН
        /// предмет, а два Common-дропи «Потертого каптана» інакше нічим не
        /// розрізнити. Переживає save/load — див. Inventory.CaptureState/
        /// RestoreState і FromSaved нижче.
        /// </summary>
        public string InstanceId { get; private set; }

        private readonly List<StatModifier> _mods = new List<StatModifier>();
        public IReadOnlyList<StatModifier> StatMods => _mods;

        public EquipSlot Slot => Definition.Slot;
        public string DisplayName => Definition.DisplayName;

        public ItemInstance(ItemDefinition definition, Rarity rarity)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            InstanceId = NextInstanceId();
            Resolve(rarity);
        }

        /// <summary>Іменний предмет: фіксована рідкість і роли (детерміновано, без рандому).</summary>
        public static ItemInstance NamedFrom(ItemDefinition def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            return new ItemInstance(def, def.NamedRarity);
        }

        /// <summary>
        /// Відновлення із слепка (Inventory.RestoreState): рідкість і ВЖЕ
        /// свернуті модифікатори беруться як є, без пере-обчислення з бази.
        /// Так крафт-апгрейд (масштабування ВІД поточного значення, а не від
        /// авторського) лишається побайтово точним після save/load — прямий
        /// Resolve(рідкість) з бази дав би інше число після ≥2 апгрейдів
        /// (округлення накопичується по-різному). <paramref name="instanceId"/> —
        /// той самий id, що був до збереження (не перегенерований): інакше
        /// команда Equip/CraftUpgrade, видана до збереження, після завантаження
        /// адресувала б уже неіснуючий id.
        /// </summary>
        public static ItemInstance FromSaved(ItemDefinition def, Rarity rarity, IEnumerable<StatModifier> mods, string instanceId)
        {
            var inst = new ItemInstance(def, rarity);
            inst._mods.Clear();
            if (mods != null) inst._mods.AddRange(mods);
            if (!string.IsNullOrEmpty(instanceId))
            {
                inst.InstanceId = instanceId;
                AdvanceCounterPast(instanceId);
            }
            return inst;
        }

        private static string NextInstanceId()
            => "item_" + (_nextInstanceSeq++).ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// Підіймає лічильник, щоб НОВІ предмети, створені після завантаження
        /// сейву, не отримали id, який уже зайнятий відновленим предметом.
        /// </summary>
        private static void AdvanceCounterPast(string instanceId)
        {
            const string prefix = "item_";
            if (!instanceId.StartsWith(prefix, StringComparison.Ordinal)) return;
            long n;
            if (long.TryParse(instanceId.Substring(prefix.Length), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out n) && n >= _nextInstanceSeq)
                _nextInstanceSeq = n + 1;
        }

        /// <summary>Усі модифікатори: базові роли + унікальний ефект іменного (якщо є).</summary>
        public IEnumerable<StatModifier> Modifiers()
        {
            for (int i = 0; i < _mods.Count; i++) yield return _mods[i];
            if (Definition.Effect != null)
                foreach (var m in Definition.Effect.ExtraModifiers()) yield return m;
        }

        /// <summary>
        /// Крафт-апгрейд (US-6.3): піднімає рідкість і МАСШТАБУЄ вже наявні
        /// значення новим множником — без пере-роздачі з бази (Resolve не
        /// викликається). Так апгрейд монотонний: пряме Resolve(нова рідкість)
        /// могло б дати значення НИЖЧЕ прежнього після кількох апгрейдів через
        /// накопичене округлення — масштабування ВІД поточного цього не
        /// допускає структурно.
        /// </summary>
        internal void UpgradeTo(Rarity rarity)
        {
            if (Definition.IsNamed) return; // іменні фіксовані (US-6.1)
            double from = RarityTuning.MagnitudeMultiplier(Rarity);
            double to = RarityTuning.MagnitudeMultiplier(rarity);
            Rarity = rarity;
            if (from <= 0) return;

            for (int i = 0; i < _mods.Count; i++)
            {
                var m = _mods[i];
                double scaled = Math.Round(m.Value * to / from, MidpointRounding.AwayFromZero);
                if (scaled == m.Value) scaled = m.Value + 1; // апгрейд зобов'язаний бути помітним
                _mods[i] = new StatModifier(m.Key, scaled, m.Mode, m.Source, m.SourceId);
            }
        }

        private void Resolve(Rarity rarity)
        {
            Rarity = rarity;
            _mods.Clear();

            // Іменні фіксовані: базові значення не множаться магнітудою рідкості (US-6.1).
            double mult = Definition.IsNamed ? 1.0 : RarityTuning.MagnitudeMultiplier(rarity);
            var rolls = Definition.StatRolls;
            for (int i = 0; i < rolls.Count; i++)
            {
                var r = rolls[i];
                // AwayFromZero, а не банківське округлення за замовчуванням: множники
                // рідкості (1.0/1.5/2.0/2.5) на цілих базових значеннях регулярно
                // дають рівно .5, і «до парного» звело б 4.5→4, а 7.5→8 — магнітуда
                // залежала б від парності. Те саме правило, що в UpgradeTo.
                double value = Math.Round(r.BaseValue * mult, MidpointRounding.AwayFromZero);
                if (value != 0) _mods.Add(StatModifier.Flat(r.Key, value, ModifierSource.Gear, Definition.Id));
            }
        }
    }
}
