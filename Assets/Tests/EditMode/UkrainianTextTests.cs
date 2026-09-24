using System.Collections.Generic;
using System.Linq;
using Game.Core.Characters.Creation;
using Game.Gameplay.Text;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Заборони для єдиної текстової таблиці (R7, docs/TEST_BUILD.md §7/§6.2, пакет E3):
    /// без дублікатів і порожніх значень, плейсхолдери збалансовані, кожен
    /// ключ §7 присутній, пари <c>.m</c>/<c>.f</c> узгоджені, відсутній ключ
    /// повертає видиму заглушку.
    /// </summary>
    public class UkrainianTextTests
    {
        [Test]
        public void AllKeys_HasNoDuplicates()
        {
            var keys = UkrainianText.AllKeys;
            var distinct = new HashSet<string>(keys);
            Assert.AreEqual(keys.Count, distinct.Count, "Знайдено дублікат ключа тексту.");
        }

        [Test]
        public void AllKeys_ResolveToNonEmptyText()
        {
            foreach (var key in UkrainianText.AllKeys)
            {
                string text = UkrainianText.Get(key, Gender.Male);
                Assert.IsFalse(string.IsNullOrEmpty(text), "Порожній текст для ключа: " + key);
                Assert.AreNotEqual(UkrainianText.MissingMarker(key), text,
                    "Ключ із таблиці повертає власну заглушку (не мало би бути можливим): " + key);
            }
        }

        [Test]
        public void AllKeys_PlaceholdersAreBalanced()
        {
            var problems = new List<string>();
            foreach (var key in UkrainianText.AllKeys)
            {
                string text = UkrainianText.Get(key, Gender.Male);
                int open = text.Count(c => c == '{');
                int close = text.Count(c => c == '}');
                if (open != close) problems.Add(key + " (" + open + " '{' / " + close + " '}')");
            }
            CollectionAssert.IsEmpty(problems, "Незбалансовані плейсхолдери: " + string.Join("; ", problems));
        }

        /// <summary>
        /// Пари <c>.m</c>/<c>.f</c> мають існувати РАЗОМ — інакше лукап за родом,
        /// якого нема, тихо провалюється до "протилежного" тексту як до "базового"
        /// ключа (Resolve падає на буквальний ключ, коли варіанта нема).
        /// </summary>
        [Test]
        public void GenderedKeys_MAndFPairsAreConsistent()
        {
            var all = new HashSet<string>(UkrainianText.AllKeys);
            var problems = new List<string>();
            foreach (var key in all)
            {
                if (key.EndsWith(".m", System.StringComparison.Ordinal))
                {
                    string sibling = key.Substring(0, key.Length - 2) + ".f";
                    if (!all.Contains(sibling)) problems.Add(key + " без " + sibling);
                }
                else if (key.EndsWith(".f", System.StringComparison.Ordinal))
                {
                    string sibling = key.Substring(0, key.Length - 2) + ".m";
                    if (!all.Contains(sibling)) problems.Add(key + " без " + sibling);
                }
            }
            CollectionAssert.IsEmpty(problems, "Непарні варіанти роду: " + string.Join("; ", problems));
        }

        /// <summary>
        /// Фікс-ревью (major, знайдено QA): "production.recovered" тримав
        /// сиру дужкову нотацію роду ("одужав(-ла)") замість розщеплення на
        /// .m/.f (той самий клас бага, що вже стався й був закритий для
        /// companion.died/ui.reason.*) — гравець бачив дужки буквально.
        /// Охоронець тут, щоб таке не повернулося непоміченим: жоден текст
        /// таблиці не має нести сирої "(-...)"-нотації.
        /// </summary>
        [Test]
        public void AllKeys_NoRawParentheticalGenderNotation()
        {
            var problems = new List<string>();
            foreach (var key in UkrainianText.AllKeys)
            {
                string text = UkrainianText.Get(key, Gender.Male);
                if (text.Contains("(-")) problems.Add(key + ": " + text);
            }
            CollectionAssert.IsEmpty(problems, "Сира дужкова нотація роду (мало бути розщеплено на .m/.f): " + string.Join("; ", problems));
        }

        [Test]
        public void Get_UnknownKey_ReturnsVisibleMarker()
        {
            const string unknown = "no.such.key.in.this.table";
            Assert.AreEqual("[" + unknown + "]", UkrainianText.Get(unknown, Gender.Male));
            Assert.AreEqual("[" + unknown + "]", UkrainianText.Get(unknown, Gender.Female));
            Assert.IsFalse(UkrainianText.Has(unknown, Gender.Male));
        }

        [Test]
        public void Get_EmptyOrNullKey_ReturnsMarkerAndNeverThrows()
        {
            Assert.AreEqual("[]", UkrainianText.Get("", Gender.Male));
            Assert.AreEqual("[null]", UkrainianText.Get(null, Gender.Male));
        }

        [Test]
        public void Get_GenderedVariant_OverridesBaseKey()
        {
            Assert.AreEqual("Провідник", UkrainianText.Get("ui.creation.name.default", Gender.Male));
            Assert.AreEqual("Провідниця", UkrainianText.Get("ui.creation.name.default", Gender.Female));
        }

        [Test]
        public void Get_UnsplitKey_IgnoresGender()
        {
            // node1.outcome.base не розщеплений (§7.2) — рід не впливає.
            string m = UkrainianText.Get("node1.outcome.base", Gender.Male);
            string f = UkrainianText.Get("node1.outcome.base", Gender.Female);
            Assert.AreEqual(m, f);
            Assert.AreNotEqual(UkrainianText.MissingMarker("node1.outcome.base"), m);
        }

        [Test]
        public void Format_SubstitutesNamedPlaceholders()
        {
            string result = UkrainianText.Format("dungeon.extract", Gender.Male, "materials", "5", "gold", "10");
            Assert.AreEqual("Здобич збережено: 5 матеріалів, 10 золота.", result);
        }

        [Test]
        public void RequiredKeys_MatchesAllKeys()
        {
            CollectionAssert.AreEquivalent(UkrainianText.AllKeys, UkrainianText.RequiredKeys());
        }

        /// <summary>Кожен ключ §7 TEST_BUILD.md — присутній буквально (акцептанс E3, §6.2).</summary>
        [TestCaseSource(nameof(AllSpecSection7Keys))]
        public void EverySpecKey_IsPresent(string key)
        {
            Assert.IsTrue(UkrainianText.Has(key, Gender.Male) || UkrainianText.Has(key, Gender.Female),
                "Ключ §7 відсутній у таблиці: " + key);
        }

        private static IEnumerable<string> AllSpecSection7Keys()
        {
            return new[]
            {
                // §7.1
                "scene.pass.tuhar.offer", "scene.pass.tuhar.threat", "scene.pass.zakhar.refuses",
                "scene.pass.myroslava.aside",
                // §7.2
                "node1.decision.title", "node1.option.quiet", "node1.option.bloody",
                "battle.pass_vanguard.intro", "node1.outcome.best", "node1.outcome.good",
                "node1.outcome.base", "node1.outcome.worst", "signal.node1.myroslava_left",
                // §7.3
                "post.council_seat", "post.storehouse_dock", "post.infirmary_bed",
                "post.settlement_market", "post.settlement_farms", "post.workshop_bench",
                "post.scouting_post", "post.empty", "signal.settlement.shelter_given",
                // §7.4
                "ui.night.title", "ui.night.patrol", "ui.night.sleep",
                "forewarn.level1", "forewarn.level2", "forewarn.level3",
                // §7.5
                "post.report.storehouse.good", "post.report.storehouse.silence",
                "post.report.infirmary.good", "post.report.infirmary.silence",
                "post.report.council.good", "post.report.council.silence",
                "post.report.market.good", "post.report.market.silence",
                "post.report.farms.good", "post.report.farms.silence",
                "post.report.workshop.good", "post.report.workshop.silence",
                "post.report.scouting.good", "post.report.scouting.silence",
                // §7.6
                "incident.spoiled_stores.title", "incident.spoiled_stores.option.quiet",
                "incident.spoiled_stores.option.bloody", "incident.spoiled_stores.outcome.best",
                "incident.spoiled_stores.outcome.good", "incident.spoiled_stores.outcome.base",
                "incident.spoiled_stores.outcome.worst",
                // §7.7
                "quest.hafiya.offer", "quest.hafiya.stage2.found", "quest.hafiya.stage2.missing",
                "quest.hafiya.stage3.best", "quest.hafiya.stage3.worst",
                // keys-phaseB.json (Hafiya offer choice)
                "quest.hafiya.offer.option.accept", "quest.hafiya.offer.option.decline",
                "quest.hafiya.declined",
                // §7.8
                "incident.sick_child.title", "incident.sick_child.option.quiet",
                "incident.sick_child.outcome.best", "incident.sick_child.outcome.good",
                "incident.sick_child.outcome.base", "incident.sick_child.outcome.worst",
                // §7.9
                "faction.community", "faction.tuhar_boyars", "faction.horde",
                "faction.band.hostile", "faction.band.wary", "faction.band.neutral",
                "faction.band.awaiting", "faction.band.allied",
                "signal.faction.tuhar_boyars.wary", "signal.faction.tuhar_boyars.hostile",
                "forewarn.tugar.level1", "forewarn.tugar.level2", "forewarn.tugar.level3",
                // §7.10
                "building.workshop.ordered", "building.workshop.stage", "building.workshop.ready",
                // §7.11 (+ keys-phaseB.json)
                "site.abandoned_camp", "dungeon.depart", "dungeon.room1.title", "dungeon.room1.quiet",
                "dungeon.room1.bloody", "dungeon.room2.title", "item.scout_horn.found",
                "item.scout_horn.effect", "dungeon.room3.title", "dungeon.room3.greedy",
                "dungeon.room3.cautious", "dungeon.extract", "dungeon.wiped", "dungeon.room.bypassed",
                // §7.12
                "craft.confirm", "craft.done",
                // §7.13
                "council.raid.ordered", "council.settlers.ordered", "council.decree.ordered",
                "council.diplomacy.ordered", "council.investment.ordered",
                "council.prepare_threat.ordered", "council.outfit_expedition.ordered",
                // keys-phaseB.json (реальні CityWorks/CityWorksStep літерали)
                "council.invest.payout", "council.raid",
                // §7.14
                "crisis.test.warn", "crisis.test.window", "crisis.test.mitigated",
                "crisis.test.unmitigated",
                // §7.15
                "finale.tuhar.present", "finale.burunda.present", "finale.myroslava.ally",
                "finale.myroslava.enemy", "finale.option.quiet", "finale.option.bloody",
                "battle.finale.intro", "finale.outcome.best", "finale.outcome.good",
                "finale.outcome.base", "finale.outcome.worst",
                // §7.16
                "summary.title", "summary.roster", "summary.village", "summary.tuhar",
                // §7.17 (+ keys-phaseB.json backgrounds)
                "ui.creation.title", "ui.creation.name", "ui.creation.name.default",
                "ui.creation.gender", "ui.creation.background.title",
                "background.warrior", "background.trader", "background.healer",
                // §7.18
                "ui.buildplanner.title", "ui.buildplanner.points", "ui.buildplanner.preview",
                "ui.buildplanner.commit.confirm", "ui.buildplanner.commit.done",
                // §7.19
                "ui.title.newgame", "ui.title.continue", "ui.title.training", "ui.title.quit",
                "ui.title.hitrule.percent", "ui.title.hitrule.threshold",
                "ui.save.slot", "ui.save.slot.empty", "ui.save.autosave", "ui.training.title",
                // §7.20
                "ui.battle.ap", "ui.battle.ap_reserved", "ui.battle.overwatch.button",
                "ui.battle.overwatch.indicator", "ui.battle.autoresolve", "ui.battle.victory",
                "ui.battle.defeat", "combat.overwatch.triggered.line", "combat.attack.hit",
                "combat.attack.graze", "combat.attack.crit", "combat.attack.miss",
                // §7.21
                "skill.ranged", "skill.melee", "skill.tactics", "skill.lockpick", "skill.mechanics",
                "skill.survival", "skill.medicine", "skill.persuade", "skill.intimidate", "skill.trade",
                "attr.strength", "attr.agility", "attr.wits", "attr.will",
                "resource.gold", "resource.materials", "resource.food",
                "band.best", "band.good", "band.base", "band.worst",
                "loyalty.band.broken", "loyalty.band.resentful", "loyalty.band.wary",
                "loyalty.band.steady", "loyalty.band.devoted",
                "readiness.band.unprepared", "readiness.band.bracing", "readiness.band.ready",
                "readiness.band.fortified",
                "char.maksym", "char.myroslava", "char.zakhar", "char.keeper", "char.healer",
                "char.tuhar", "char.horde_commander", "char.protagonist",
                "enemy.horde_skirmisher", "enemy.horde_raider",
            };
        }
    }
}
