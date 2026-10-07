using System.Collections.Generic;
using System.Globalization;
using Game.Core.Stats;

namespace Game.Core.Items
{
    /// <summary>
    /// Один формат запису предметів для всіх місць, де вони лежать (сташ — <see cref="Inventory"/>,
    /// надіте — <see cref="Equipment"/>): <c>defId:rarity:instanceId:statKey=value,...</c>, предмети
    /// через ';'. Значення персистяться, а не пере-обчислюються з бази — так крафт-апгрейд лишається
    /// побайтово точним після save/load (див. <see cref="ItemInstance.FromSaved"/>).
    /// </summary>
    internal static class ItemCodec
    {
        public static string Encode(IEnumerable<ItemInstance> items)
        {
            var parts = new List<string>();
            foreach (var item in items)
            {
                if (item == null) continue;
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

        /// <summary>Невідомі визначення (контент прибрали) тихо пропускаються — сейв не падає.</summary>
        public static List<ItemInstance> Decode(string blob)
        {
            var result = new List<ItemInstance>();
            if (string.IsNullOrEmpty(blob)) return result;

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
                if (!defsById.TryGetValue(f[0], out def)) continue;

                var rarity = (Rarity)ParseInt(f[1]);
                string instanceId = f.Length > 2 ? f[2] : null;

                var mods = new List<StatModifier>();
                if (f.Length > 3 && f[3].Length > 0)
                {
                    foreach (var pair in f[3].Split(','))
                    {
                        var kv = pair.Split('=');
                        if (kv.Length != 2) continue;
                        mods.Add(StatModifier.Flat((StatKey)ParseInt(kv[0]), ParseDouble(kv[1]), ModifierSource.Gear, def.Id));
                    }
                }
                result.Add(ItemInstance.FromSaved(def, rarity, mods, instanceId));
            }
            return result;
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
