using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Game.Core.Characters;
using Game.Core.Loop;
using Game.Core.Stats;

namespace Game.Core.Items
{
    /// <summary>
    /// Сташ поселення (Епік 6/15): лут з вилазок копиться тут — це faucet гіра
    /// для екіпірування й крафту. Реалізує <see cref="IStateBlob"/> так само,
    /// як StoryFlags/SiteLedger/CityWorks — конвеєр дня заворачивает
    /// непрозорим фрагментом власного слепка (D1/A1), звертатись прямо сюди
    /// самому конвеєру не потрібно.
    /// </summary>
    public sealed class Inventory : IStateBlob
    {
        private readonly List<ItemInstance> _items = new List<ItemInstance>();

        public IReadOnlyList<ItemInstance> Items => _items;
        public int Count => _items.Count;

        public void Add(ItemInstance item)
        {
            if (item != null) _items.Add(item);
        }

        public bool Remove(ItemInstance item) => _items.Remove(item);

        /// <summary>
        /// Пошук за стабільним <see cref="ItemInstance.InstanceId"/> — саме так
        /// контракт GameSession (docs/TEST_BUILD.md §4.1) адресує ОДИН
        /// конкретний предмет у команди Equip/CraftUpgrade, коли в сташі лежить
        /// кілька дропів однієї бази (напр. два Common «Потерті каптани»).
        /// </summary>
        public ItemInstance Find(string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId)) return null;
            for (int i = 0; i < _items.Count; i++)
                if (string.Equals(_items[i].InstanceId, instanceId, StringComparison.Ordinal))
                    return _items[i];
            return null;
        }

        /// <summary>
        /// Знімає з напарника ВСЕ надіте спорядження і повертає його в сташ
        /// (порт архівного Inventory.RecoverGearFrom). Викликати ПЕРЕД/ПРИ
        /// Companion.MarkDead() — сам Items цей виклик не робить (MarkDead()
        /// зветься з Core/Base/SettlementAdapters.cs, чужий пакет), інакше
        /// надітий гір, зокрема єдиний іменний предмет кампанії, зникає
        /// назавжди разом із загиблим.
        /// </summary>
        public void RecoverGearFrom(Companion companion)
        {
            if (companion == null) return;
            foreach (EquipSlot slot in Enum.GetValues(typeof(EquipSlot)))
            {
                var recovered = companion.Equipment.Unequip(slot);
                if (recovered != null) Add(recovered);
            }
        }

        // ---- Слепок (формат непрозорий назовні, як і решта IStateBlob): ----
        // <defId>:<rarity>:<instanceId>:<statKey>=<value>,...;<defId2>:...
        // Значення персистяться, а не пере-обчислюються з бази (Definition):
        // крафт-апгрейд масштабує ВІД поточного значення (ItemInstance.
        // UpgradeTo), тож лише збережене число відновлює предмет побайтово
        // точно — прямий Resolve(рідкість) після ≥2 апгрейдів дав би інше
        // округлення (див. коментар ItemInstance.FromSaved). InstanceId теж
        // персистується (не перегенеровується): інакше команда Equip/
        // CraftUpgrade, видана до збереження, після завантаження адресувала б
        // уже неіснуючий id (докладніше — ItemInstance.FromSaved).

        public string CaptureState()
        {
            if (_items.Count == 0) return string.Empty;

            var parts = new List<string>(_items.Count);
            for (int i = 0; i < _items.Count; i++)
            {
                var item = _items[i];
                var mods = new List<string>();
                var statMods = item.StatMods;
                for (int j = 0; j < statMods.Count; j++)
                {
                    var m = statMods[j];
                    mods.Add(((int)m.Key).ToString(CultureInfo.InvariantCulture) + "="
                        + m.Value.ToString("R", CultureInfo.InvariantCulture));
                }

                parts.Add(item.Definition.Id + ":" + (int)item.Rarity + ":" + item.InstanceId + ":"
                    + string.Join(",", mods.ToArray()));
            }

            return string.Join(";", parts.ToArray());
        }

        public void RestoreState(string blob)
        {
            _items.Clear();
            if (string.IsNullOrEmpty(blob)) return;

            var defsById = new Dictionary<string, ItemDefinition>();
            var defs = DefaultItems.AllDefinitions();
            for (int i = 0; i < defs.Count; i++)
                if (!string.IsNullOrEmpty(defs[i].Id)) defsById[defs[i].Id] = defs[i];

            foreach (var part in blob.Split(';'))
            {
                if (string.IsNullOrEmpty(part)) continue;
                var f = part.Split(':');
                if (f.Length < 2) continue;

                ItemDefinition def;
                if (!defsById.TryGetValue(f[0], out def)) continue; // невідомий предмет — пропускаємо, не кидаємо

                var rarity = (Rarity)ParseInt(f[1]);
                string instanceId = f.Length > 2 ? f[2] : null; // порожнє — FromSaved згенерує новий лічильником

                var mods = new List<StatModifier>();
                if (f.Length > 3 && f[3].Length > 0)
                {
                    foreach (var pair in f[3].Split(','))
                    {
                        var kv = pair.Split('=');
                        if (kv.Length != 2) continue;
                        var key = (StatKey)ParseInt(kv[0]);
                        double value = ParseDouble(kv[1]);
                        mods.Add(StatModifier.Flat(key, value, ModifierSource.Gear, def.Id));
                    }
                }

                _items.Add(ItemInstance.FromSaved(def, rarity, mods, instanceId));
            }
        }

        private static int ParseInt(string s)
        {
            int v;
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : 0;
        }

        private static double ParseDouble(string s)
        {
            double v;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0.0;
        }
    }
}
