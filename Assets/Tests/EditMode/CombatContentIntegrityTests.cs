using System.Collections.Generic;
using System.Reflection;
using Game.Core.Combat;
using Game.Core.Dungeons;
using Game.Core.Session;
using Game.Core.Story;
using Game.Gameplay.Text;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Цілісність бойового контенту. Поправка №11 знайшла: кімната «Дозор
    /// скиту» посилалась на <c>forest_bandit</c>, якого не було в жодному
    /// каталозі, — <c>CombatBattleBuilder</c> мовчки пропускав спавн, і
    /// кривавий шлях ішов проти НУЛЯ ворогів. Тепер кожен id ворога, на який
    /// посилається контент, зобов'язаний резолвитись, а кожен ворог і його
    /// зброя — мати назву в <see cref="UkrainianText"/> (інакше автотур падає
    /// кодом 3 уже в грі).
    /// </summary>
    public class CombatContentIntegrityTests
    {
        private static IEnumerable<(string where, string id)> ReferencedEnemyIds()
        {
            foreach (var site in DefaultDungeon.KnownSiteIds)
                foreach (var room in DefaultDungeon.Rooms(site))
                    foreach (var id in room.EnemyIds)
                        yield return (site + "/" + room.Id, id);

            var node1 = (string[])typeof(GameSession)
                .GetField("Node1BloodyEnemyIds", BindingFlags.NonPublic | BindingFlags.Static)
                .GetValue(null);
            foreach (var id in node1) yield return ("вузол 1", id);

            yield return ("фінал: бос", Finale.BurundaBossId);
            yield return ("фінал: рядові", Finale.RankAndFileEnemyId);
        }

        private static EnemyDefinition Resolve(Dictionary<string, EnemyDefinition> catalog, string id)
        {
            // Той самий порядок, що GameSession.ResolveEnemyById.
            if (catalog.TryGetValue(id, out var def)) return def;
            return catalog.TryGetValue("enemy." + id, out def) ? def : null;
        }

        [Test]
        public void EveryReferencedEnemyId_ResolvesToADefinition()
        {
            var catalog = DefaultCombatContent.EnemyCatalog();
            var missing = new List<string>();
            foreach (var (where, id) in ReferencedEnemyIds())
                if (Resolve(catalog, id) == null) missing.Add(where + " → " + id);
            CollectionAssert.IsEmpty(missing, "контент посилається на ворогів, яких немає в EnemyCatalog (бій мовчки йшов би без них)");
        }

        [Test]
        public void EveryCatalogEnemy_AndItsWeapon_HasUkrainianName()
        {
            var missing = new List<string>();
            foreach (var def in DefaultCombatContent.EnemyCatalog().Values)
            {
                if (!UkrainianText.Has(def.Id, false)) missing.Add(def.Id);
                if (def.Weapon != null && !UkrainianText.Has(def.Weapon.Id, false)) missing.Add(def.Weapon.Id);
            }
            CollectionAssert.IsEmpty(missing, "ворог або його зброя без назви в UkrainianText");
        }
    }
}
