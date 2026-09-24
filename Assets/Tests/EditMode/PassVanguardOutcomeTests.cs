using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Checks;
using Game.Core.Economy;
using Game.Core.Story;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// PassVanguardOutcome (аудит G7, рядок 19 чеклиста §3 FIRST_HOUR.md):
    /// раніше <c>OpeningScenes.PassResolution</c> показувала репліку, а склад
    /// ростера й гаманець не мінялись жодним кодом. Тут перевіряється, що
    /// <see cref="PassVanguardOutcome.Apply"/> реально мутує підсумок вузла 1
    /// за всіма 4 полосами (§3.1 TEST_BUILD.md).
    /// </summary>
    public class PassVanguardOutcomeTests
    {
        private static (BaseState state, Companion maksym, Companion myroslava) NewWorld()
        {
            var roster = new Roster();
            var ledger = new ResourceLedger();
            var cfg = new BalanceConfig();
            var state = new BaseState(roster, ledger, cfg);

            var maksym = new CompanionArchetype("maksym", "Максим Беркут").CreateInstance("maksym");
            var myroslava = new CompanionArchetype("myroslava", "Мирослава").CreateInstance("myroslava");
            roster.Add(maksym);
            roster.Add(myroslava);

            ledger.Add(ResourceType.Materials, 20);
            ledger.Add(ResourceType.Food, 20);

            return (state, maksym, myroslava);
        }

        [Test]
        public void Apply_Best_NoWound_NoLeave_NoPlunder()
        {
            var (state, maksym, myroslava) = NewWorld();
            var flags = new StoryFlags();

            PassVanguardOutcome.Apply(state, OutcomeBand.Best, wasBloody: true, flags);

            Assert.AreEqual(0.0, maksym.InjuryPoints, "Найкраща — склад цілий, ніхто не ранений");
            Assert.AreEqual(CompanionStatus.Idle, myroslava.Status, "Мирослава лишається");
            Assert.AreEqual(20, state.Resources.Get(ResourceType.Materials), "склад цілий на Найкращій");
            Assert.IsFalse(flags.Get(PassVanguardOutcome.DefectorSeededFlag));
            Assert.IsTrue(flags.Get("pass_vanguard_resolved"));
        }

        [Test]
        public void Apply_Good_Bloody_WoundsMaksym_MyroslavaStays()
        {
            var (state, maksym, myroslava) = NewWorld();
            var flags = new StoryFlags();

            PassVanguardOutcome.Apply(state, OutcomeBand.Good, wasBloody: true, flags);

            Assert.Greater(maksym.InjuryPoints, 0.0, "Хороша на кровавому шляху — Максим ранений");
            Assert.AreEqual(CompanionStatus.Injured, maksym.Status);
            Assert.AreEqual(CompanionStatus.Idle, myroslava.Status, "Мирослава лишається на Хорошій");
            Assert.AreEqual(20, state.Resources.Get(ResourceType.Materials), "склад цілий, коли вона лишається");
        }

        [Test]
        public void Apply_Good_Quiet_NoWound()
        {
            var (state, maksym, _) = NewWorld();

            PassVanguardOutcome.Apply(state, OutcomeBand.Good, wasBloody: false, new StoryFlags());

            Assert.AreEqual(0.0, maksym.InjuryPoints,
                "Рана — ціна КРОВІ (Поправка №1): тихий шлях Хорошою полосою нікого не ранить");
        }

        [Test]
        public void Apply_Base_MyroslavaLeaves_StorehousePlundered_NoWoundOnQuiet()
        {
            var (state, maksym, myroslava) = NewWorld();
            var flags = new StoryFlags();

            PassVanguardOutcome.Apply(state, OutcomeBand.Base, wasBloody: false, flags);

            Assert.AreEqual(0.0, maksym.InjuryPoints, "Базова не ранить Максима");
            Assert.AreEqual(CompanionStatus.Idle, myroslava.Status, "«Йде» — статус Idle за §3.1");
            Assert.IsNull(myroslava.AssignedSlotId);
            Assert.Less(state.Resources.Get(ResourceType.Materials), 20, "склад розграбований на Базовій/Найгіршій");
            Assert.IsTrue(flags.Get(PassVanguardOutcome.DefectorSeededFlag), "зерно зради посіяно (§3.1) — ще НЕ дефекція");
        }

        [Test]
        public void Apply_Worst_Bloody_WoundsMaksym_AndMyroslavaLeaves()
        {
            var (state, maksym, myroslava) = NewWorld();
            var flags = new StoryFlags();

            PassVanguardOutcome.Apply(state, OutcomeBand.Worst, wasBloody: true, flags);

            Assert.Greater(maksym.InjuryPoints, 0.0, "Найгірша на кровавому шляху — Максим теж ранений");
            Assert.AreEqual(CompanionStatus.Idle, myroslava.Status);
            Assert.Less(state.Resources.Get(ResourceType.Food), 20);
            Assert.IsTrue(flags.Get(PassVanguardOutcome.DefectorSeededFlag));
        }

        [Test]
        public void Apply_UnassignsMyroslava_WhenSheHeldAPost()
        {
            var (state, _, myroslava) = NewWorld();
            state.AddSlot(new AssignmentSlotDefinition("scouting_post", "Дозор", BaseSectionType.ScoutingPost));
            Assert.AreEqual(AssignmentResult.Success, state.TryAssign("myroslava", "scouting_post"));

            PassVanguardOutcome.Apply(state, OutcomeBand.Worst, wasBloody: false, new StoryFlags());

            Assert.IsNull(myroslava.AssignedSlotId, "яка йде — пост не тримає");
            Assert.IsFalse(state.GetSlot("scouting_post").IsOccupied);
        }

        [Test]
        public void ResolveKey_MapsAllFourBands()
        {
            Assert.AreEqual("best", PassVanguardOutcome.ResolveKey(OutcomeBand.Best, true));
            Assert.AreEqual("good", PassVanguardOutcome.ResolveKey(OutcomeBand.Good, true));
            Assert.AreEqual("base", PassVanguardOutcome.ResolveKey(OutcomeBand.Base, false));
            Assert.AreEqual("worst", PassVanguardOutcome.ResolveKey(OutcomeBand.Worst, false));
        }

        [Test]
        public void MyroslavaLoyaltyDelta_MatchesSpecNumbers()
        {
            // §3.1: Найкраща +15, Хороша +5, Базова −20, Найгірша −35.
            // ДАНІ на майбутнє: Companion.LoyaltyBand (B4) тут ще не існує.
            Assert.AreEqual(15, PassVanguardOutcome.MyroslavaLoyaltyDelta(OutcomeBand.Best));
            Assert.AreEqual(5, PassVanguardOutcome.MyroslavaLoyaltyDelta(OutcomeBand.Good));
            Assert.AreEqual(-20, PassVanguardOutcome.MyroslavaLoyaltyDelta(OutcomeBand.Base));
            Assert.AreEqual(-35, PassVanguardOutcome.MyroslavaLoyaltyDelta(OutcomeBand.Worst));
        }

        [Test]
        public void OpeningScenes_PassResolution_ExistsForAllFourKeys()
        {
            // G7: перевірка, а не переписування — сцена вже узагальнена рядком.
            foreach (var band in new[] { OutcomeBand.Best, OutcomeBand.Good, OutcomeBand.Base, OutcomeBand.Worst })
            {
                var key = PassVanguardOutcome.ResolveKey(band, wasBloody: true);
                var scene = Game.Core.Scenes.OpeningScenes.PassResolution(key);
                Assert.IsNotNull(scene);
                Assert.IsTrue(scene.Steps.Exists(s => s.Key == "scene.pass." + key));
            }
        }
    }
}
