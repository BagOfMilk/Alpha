using System.Collections.Generic;
using Game.Core.Characters;
using Game.Core.Combat;
using Game.Core.Companions;
using Game.Core.Saves;
using Game.Core.Threats;
using UnityEngine;

namespace Game.Gameplay.Content
{
    /// <summary>
    /// Библиотека контента (US-18.1): агрегирует контент-ассеты и собирает из них
    /// рантайм-каталог/пулы — замена код-константам Default*. Авто-сборка по папке
    /// делается сидером/редактором; рантайм получает готовую библиотеку.
    /// </summary>
    [CreateAssetMenu(menuName = "Alpha/Content/Content Library", fileName = "ContentLibrary")]
    public sealed class ContentLibraryAsset : ScriptableObject
    {
        public List<TraitAsset> traits = new List<TraitAsset>();
        public List<ScarAsset> scars = new List<ScarAsset>();
        public List<PerkAsset> perks = new List<PerkAsset>();
        public List<AbilityAsset> abilities = new List<AbilityAsset>();
        public List<ItemAsset> items = new List<ItemAsset>();
        public List<EnemyAsset> enemies = new List<EnemyAsset>();
        public List<IncidentAsset> incidents = new List<IncidentAsset>();
        public List<BackgroundAsset> backgrounds = new List<BackgroundAsset>();

        /// <summary>Каталог для сейвов/перков (арки пока код-контент — добавляются из DefaultArcs).</summary>
        public ContentCatalog BuildCatalog()
        {
            var c = new ContentCatalog();
            foreach (var t in traits) if (t != null) c.AddTrait(t.ToDefinition());
            foreach (var s in scars) if (s != null) c.AddScar(s.ToDefinition());
            foreach (var i in items) if (i != null) c.AddItem(i.ToDefinition());
            foreach (var p in perks) if (p != null) c.AddPerk(p.ToDefinition());
            c.AddArc(DefaultArcs.MedicOldDebt());
            c.AddArc(DefaultArcs.BrawlerFistsAndConscience());
            return c;
        }

        public List<AbilityDefinition> AbilityPool()
        {
            var list = new List<AbilityDefinition>();
            foreach (var a in abilities) if (a != null) list.Add(a.ToDefinition());
            return list;
        }

        public List<EnemyDefinition> EnemyPool()
        {
            var list = new List<EnemyDefinition>();
            foreach (var e in enemies) if (e != null) list.Add(e.ToDefinition());
            return list;
        }

        public List<IncidentDefinition> IncidentPool()
        {
            var list = new List<IncidentDefinition>();
            foreach (var i in incidents) if (i != null) list.Add(i.ToDefinition());
            return list;
        }

        public List<Background> BackgroundPool()
        {
            var list = new List<Background>();
            foreach (var b in backgrounds) if (b != null) list.Add(b.ToDefinition());
            return list;
        }

        /// <summary>Структурная проверка контента: null-ссылки, пустые и дублирующиеся id.</summary>
        public bool Validate(out List<string> errors)
        {
            var found = new List<string>();
            var seen = new HashSet<string>();
            void CheckId(string kind, string id)
            {
                if (string.IsNullOrEmpty(id)) { found.Add($"{kind}: пустой id"); return; }
                if (!seen.Add(kind + ":" + id)) found.Add($"{kind}: дубликат id «{id}»");
            }

            foreach (var a in traits) { if (a == null) found.Add("traits: null"); else CheckId("trait", a.trait.Id); }
            foreach (var a in scars) { if (a == null) found.Add("scars: null"); else CheckId("scar", a.scar.Id); }
            foreach (var a in perks) { if (a == null) found.Add("perks: null"); else CheckId("perk", a.perk.Id); }
            foreach (var a in abilities) { if (a == null) found.Add("abilities: null"); else CheckId("ability", a.ability.Id); }
            foreach (var a in items) { if (a == null) found.Add("items: null"); else CheckId("item", a.definition.Id); }
            var abilityPool = new HashSet<AbilityAsset>(abilities);
            foreach (var a in enemies)
            {
                if (a == null) { found.Add("enemies: null"); continue; }
                CheckId("enemy", a.definition.Id);
                for (int i = 0; i < a.abilities.Count; i++)
                {
                    if (a.abilities[i] == null)
                        found.Add($"enemy «{a.definition.Id}»: abilities[{i}] — null/битая ссылка");
                    else if (!abilityPool.Contains(a.abilities[i]))
                        found.Add($"enemy «{a.definition.Id}»: способность «{a.abilities[i].ability.Id}» вне общего пула (US-3.14)");
                }
                foreach (var r in a.resists)
                {
                    if (r.type == DamageType.True)
                        found.Add($"enemy «{a.definition.Id}»: resist для True-типа игнорируется");
                    else if (r.multiplier < 0.5 || r.multiplier > 1.5)
                        found.Add($"enemy «{a.definition.Id}»: resist {r.type} ×{r.multiplier} вне ×0.5–1.5 " +
                                  "(незаполненный 0 = полный иммунитет)");
                }
            }
            foreach (var a in incidents) { if (a == null) found.Add("incidents: null"); else CheckId("incident", a.incident.Id); }
            foreach (var a in backgrounds) { if (a == null) found.Add("backgrounds: null"); else CheckId("background", a.background.Id); }

            errors = found;
            return errors.Count == 0;
        }
    }
}
