using System.Collections.Generic;
using Game.Core.Characters;
using Game.Core.Companions;
using Game.Core.Health;
using Game.Core.Items;
using Game.Core.Traits;

namespace Game.Core.Saves
{
    /// <summary>
    /// Каталог контента по id (US-16.1): нужен при загрузке, чтобы восстановить
    /// трейты/шрамы/предметы/арки по их идентификаторам (сами данные в сейве не
    /// дублируются — только id + рантайм-состояние).
    /// </summary>
    public sealed class ContentCatalog
    {
        private readonly Dictionary<string, Trait> _traits = new Dictionary<string, Trait>();
        private readonly Dictionary<string, Scar> _scars = new Dictionary<string, Scar>();
        private readonly Dictionary<string, ItemDefinition> _items = new Dictionary<string, ItemDefinition>();
        private readonly Dictionary<string, CompanionArc> _arcs = new Dictionary<string, CompanionArc>();
        private readonly Dictionary<string, PerkDefinition> _perks = new Dictionary<string, PerkDefinition>();

        /// <summary>Каталог перков: перки — производная скилов, при загрузке пересчитываются, не хранятся.</summary>
        public IEnumerable<PerkDefinition> Perks => _perks.Values;

        public Trait GetTrait(string id) => id != null && _traits.TryGetValue(id, out var t) ? t : null;
        public Scar GetScar(string id) => id != null && _scars.TryGetValue(id, out var s) ? s : null;
        public ItemDefinition GetItem(string id) => id != null && _items.TryGetValue(id, out var i) ? i : null;
        public CompanionArc GetArc(string id) => id != null && _arcs.TryGetValue(id, out var a) ? a : null;

        public ContentCatalog AddTrait(Trait t) { if (t != null && t.Id != null) _traits[t.Id] = t; return this; }
        public ContentCatalog AddScar(Scar s) { if (s != null && s.Id != null) _scars[s.Id] = s; return this; }
        public ContentCatalog AddItem(ItemDefinition i) { if (i != null && i.Id != null) _items[i.Id] = i; return this; }
        public ContentCatalog AddArc(CompanionArc a) { if (a != null && a.Id != null) _arcs[a.Id] = a; return this; }
        public ContentCatalog AddPerk(PerkDefinition p) { if (p != null && p.Id != null) _perks[p.Id] = p; return this; }

        /// <summary>Каталог из дефолтного контента (затравка прототипа).</summary>
        public static ContentCatalog Default()
        {
            var c = new ContentCatalog();
            foreach (var t in DefaultContent.AllTraits()) c.AddTrait(t);
            foreach (var s in DefaultContent.AllScars()) c.AddScar(s);
            foreach (var i in DefaultItems.AllDefinitions()) c.AddItem(i);
            c.AddArc(DefaultArcs.MedicOldDebt());
            c.AddArc(DefaultArcs.BrawlerFistsAndConscience());
            foreach (var p in DefaultContent.PerkCatalog()) c.AddPerk(p);
            return c;
        }
    }
}
