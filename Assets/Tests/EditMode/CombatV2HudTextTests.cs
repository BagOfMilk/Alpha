using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Game.Core.Characters.Creation;
using Game.Core.Combat;
using Game.Core.Session.Views;
using Game.Gameplay; // BattleLogKind
using Game.Gameplay.Text;
using Game.Gameplay.UI;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Бій v2 (docs/COMBAT_V2.md, доручення власника 25.09.2026 — «Боевка
    /// полный пиздец»), частина «HUD»: тексти й хелпери, доведені цим пакетом
    /// поверх уже наявних <see cref="BattleLogTextTests"/>. Тут — те, чого не
    /// було до контракту §7: порядковий номер дублікатів імен (§7.1
    /// <c>BattleUnitView.Ordinal</c>), підпис доданка шансу (§7.2), спливаючі
    /// написи для всіх типів рядка журналу (§6), і що «AP»/«орди» не
    /// лишилось у текстах, які власник уже критикував напряму.
    /// </summary>
    public class CombatV2HudTextTests
    {
        // =====================================================================
        // Порядковий номер дублікатів (аудит HUD п.2: «два вороги — однакове
        // ім'я, неможливо зрозуміти, хто саме»)
        // =====================================================================

        [TestCase(1, "I")]
        [TestCase(2, "II")]
        [TestCase(3, "III")]
        [TestCase(4, "IV")]
        [TestCase(9, "IX")]
        [TestCase(14, "XIV")]
        public void RomanNumeral_MatchesStandardForm(int n, string expected)
        {
            Assert.AreEqual(expected, BattleLogText.RomanNumeral(n));
        }

        [Test]
        public void UnitName_AppendsRomanOrdinal_WhenDuplicateNameInBattle()
        {
            var view = new BattleView
            {
                Units = new List<BattleUnitView>
                {
                    new BattleUnitView { Id = "horde_scout_1", DisplayNameKey = "horde_scout", Side = "Enemy", Ordinal = 1 },
                    new BattleUnitView { Id = "horde_scout_2", DisplayNameKey = "horde_scout", Side = "Enemy", Ordinal = 2 },
                    new BattleUnitView { Id = "u_maksym", DisplayNameKey = "Максим Беркут", Side = "Player", Ordinal = 0 },
                }
            };

            Assert.AreEqual("Розвідник орди I", BattleLogText.UnitName(view, "horde_scout_1", Gender.Male));
            Assert.AreEqual("Розвідник орди II", BattleLogText.UnitName(view, "horde_scout_2", Gender.Male));
            Assert.AreEqual("Максим Беркут", BattleLogText.UnitName(view, "u_maksym", Gender.Male),
                "Ordinal=0 — ім'я унікальне, жодного номера не додається");
        }

        // =====================================================================
        // §7.2 розклад шансу: кожен ключ доданка має підпис
        // =====================================================================

        private static readonly string[] ChanceTermKeys =
        {
            "accuracy", "ability", "defense", "knocked_down", "marked",
            "cover_half", "cover_full", "distance", "suppressed", "clamp"
        };

        [Test]
        public void EveryChanceTermKey_HasText()
        {
            foreach (var key in ChanceTermKeys)
                Assert.IsTrue(UkrainianText.Has("ui.battle.term." + key, Gender.Male),
                    "доданок §7.2 без підпису: " + key);
        }

        // =====================================================================
        // Описи здібностей (аудит HUD п.10: «жодного опису ефекту не було
        // ні в даних, ні на екрані»)
        // =====================================================================

        [Test]
        public void EveryAbilityInCatalog_HasDescription()
        {
            foreach (var ability in DefaultCombatContent.AbilityCatalog())
                Assert.IsTrue(UkrainianText.Has(ability.Id + ".desc", Gender.Male),
                    "здібність без опису: " + ability.Id);
        }

        // =====================================================================
        // Зброя — нейтральна назва (аудит HUD п.13: власні бійці носять
        // «...орди» без пояснення)
        // =====================================================================

        [Test]
        public void SharedWeapons_NameDoesNotMentionHorde()
        {
            Assert.AreEqual("Лук", UkrainianText.Get("weapon.horde_bow", Gender.Male));
            Assert.AreEqual("Спис", UkrainianText.Get("weapon.horde_spear", Gender.Male));
        }

        // =====================================================================
        // Жодного «AP» замість «ОД» (аудит HUD, minor п. «AP» замість «ОД»)
        // =====================================================================

        private static readonly Regex StandaloneAp = new Regex(@"\bAP\b", RegexOptions.Compiled);

        [Test]
        public void NoBattleKey_UsesEnglishApAbbreviation()
        {
            var offenders = new List<string>();
            foreach (var key in UkrainianText.AllKeys)
            {
                if (!key.StartsWith("ui.battle.") && !key.StartsWith("ability.") && !key.StartsWith("combat.log.")) continue;
                string text = UkrainianText.Get(key, Gender.Male);
                if (StandaloneAp.IsMatch(text)) offenders.Add(key + ": " + text);
            }
            CollectionAssert.IsEmpty(offenders, string.Join("\n", offenders));
        }

        // =====================================================================
        // Відкат/вікно порятунку — відмінюється, не «х.» (аудит HUD, minor
        // «абревіатура непослідовна»)
        // =====================================================================

        [TestCase(1, "ще 1 хід")]
        [TestCase(2, "ще 2 ходи")]
        [TestCase(3, "ще 3 ходи")]
        [TestCase(4, "ще 4 ходи")]
        [TestCase(5, "ще 5 ходів")]
        [TestCase(11, "ще 11 ходів")]
        [TestCase(12, "ще 12 ходів")]
        [TestCase(21, "ще 21 хід")]
        [TestCase(22, "ще 22 ходи")]
        [TestCase(25, "ще 25 ходів")]
        public void DeclineTurns_MatchesUkrainianNumeralAgreement(int n, string expected)
        {
            Assert.AreEqual(expected, UkrainianText.DeclineTurns(n));
        }

        [Test]
        public void AbilityCooldownText_UsesDeclinedTurns_NotAbbreviation()
        {
            string text = UkrainianText.Format("ui.battle.ability.cooldown", Gender.Male, "turns", UkrainianText.DeclineTurns(2));
            Assert.AreEqual("відкат: ще 2 ходи", text);
            Assert.IsFalse(text.Contains(" х."), "старе скорочення «х.» більше не мало лишитись у панелі здібностей");
        }

        [Test]
        public void AbilityNotEnoughAp_IsUkrainian()
        {
            Assert.AreEqual("бракує ОД", UkrainianText.Get("ui.battle.ability.not_enough_ap", Gender.Male));
        }

        // =====================================================================
        // §6/§7.4 спливаючі написи: покриття Hit/Heal/Status/Ability/Overwatch
        // (BattleLogText.Floating — сигнатура заморожена §7.4, тексти доводить
        // ця частина)
        // =====================================================================

        private static BattleLogLineView Entry(string key, params (string, string)[] args)
        {
            var dict = new Dictionary<string, string>();
            foreach (var (k, v) in args) dict[k] = v;
            return new BattleLogLineView { Round = 1, Key = key, Args = dict };
        }

        [Test]
        public void Floating_Hit_ShowsOverTarget()
        {
            var spec = BattleLogText.Floating(Entry(CombatLogKeys.AttackHit, ("unitId", "a"), ("targetId", "b"), ("chance", "60"), ("damage", "4")),
                null, Gender.Male);
            Assert.IsNotNull(spec);
            Assert.AreEqual("b", spec.UnitId);
            Assert.AreEqual(BattleLogKind.Hit, spec.Kind);
        }

        [Test]
        public void Floating_Heal_ShowsPlusAmountOverUnit()
        {
            var spec = BattleLogText.Floating(Entry(CombatLogKeys.Heal, ("unitId", "a"), ("amount", "5"), ("hp", "9"), ("hpMax", "14")),
                null, Gender.Male);
            Assert.IsNotNull(spec);
            Assert.AreEqual("a", spec.UnitId);
            Assert.AreEqual("+5", spec.Text);
        }

        [Test]
        public void Floating_StatusApplied_ShowsStatusName_ButNotStatusExpired()
        {
            var applied = BattleLogText.Floating(Entry(CombatLogKeys.StatusApplied, ("unitId", "a"), ("status", "bleeding"), ("turns", "2")),
                null, Gender.Male);
            Assert.IsNotNull(applied);
            Assert.AreEqual("Кровотеча", applied.Text);

            var expired = BattleLogText.Floating(Entry(CombatLogKeys.StatusExpired, ("unitId", "a"), ("status", "bleeding")), null, Gender.Male);
            Assert.IsNull(expired, "спад стану не мусить дублювати спливаючий напис — про це вже є рядок журналу");
        }

        [Test]
        public void Floating_Ability_ShowsAbilityName()
        {
            var spec = BattleLogText.Floating(Entry(CombatLogKeys.Ability, ("unitId", "a"), ("abilityId", "ability.lunge")), null, Gender.Male);
            Assert.IsNotNull(spec);
            Assert.AreEqual("Ривок", spec.Text);
        }

        [Test]
        public void Floating_Overwatch_ShowsOnSetAndFired_ButNotOnExpiredOrLost()
        {
            var set = BattleLogText.Floating(Entry(CombatLogKeys.OverwatchSet, ("unitId", "a"), ("ap", "3")), null, Gender.Male);
            Assert.IsNotNull(set);
            Assert.AreEqual(BattleLogKind.Overwatch, set.Kind);

            var fired = BattleLogText.Floating(Entry(CombatLogKeys.OverwatchFired, ("unitId", "a"), ("targetId", "b")), null, Gender.Male);
            Assert.IsNotNull(fired);

            var expired = BattleLogText.Floating(Entry(CombatLogKeys.OverwatchExpired, ("unitId", "a")), null, Gender.Male);
            Assert.IsNull(expired);
        }

        // =====================================================================
        // §7.3: суфікси ap/cover — порожні, доки Core не пише ці args;
        // з'являються, щойно args присутні (наперед-сумісність із «ядро»)
        // =====================================================================

        [Test]
        public void AttackLog_ApAndCoverSuffix_EmptyWhenArgsAbsent()
        {
            string line = BattleLogText.Line(Entry(CombatLogKeys.AttackMiss, ("unitId", "a"), ("targetId", "b"), ("chance", "56"), ("damage", "0")),
                true, id => id, id => false);
            Assert.AreEqual("a → b: промах (шанс 56%).", line);
        }

        [Test]
        public void AttackLog_ApAndCoverSuffix_AppearWhenArgsPresent()
        {
            string line = BattleLogText.Line(Entry(CombatLogKeys.AttackMiss,
                ("unitId", "a"), ("targetId", "b"), ("chance", "56"), ("damage", "0"), ("ap", "4"), ("cover", "Half")),
                true, id => id, id => false);
            Assert.AreEqual("a → b: промах (шанс 56%), укриття цілі: половинне · ціна 4 ОД.", line);
        }
    }
}
