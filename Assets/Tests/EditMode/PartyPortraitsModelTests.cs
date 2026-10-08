using System.Collections.Generic;
using Game.Core.Session.Views;
using Game.Gameplay.UI;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Портрети загону в бою, як у BG3 (власник, 08.10.2026: «іконки персонажів такі самі на рушії гри»; «У бою»):
    /// лише свої, протагоніст першим, чий хід — видно, стовпець влазить між колесом і нижньою панеллю.
    /// </summary>
    public class PartyPortraitsModelTests
    {
        private static BattleUnitView U(string id, string side, int hp = 10, int ap = 8, bool downed = false, bool out_ = false)
            => new BattleUnitView { Id = id, Side = side, Hp = hp, HpMax = 10, Ap = ap, ApMax = 8, IsDowned = downed, IsOutOfBattle = out_ };

        private static BattleView View(string current, params BattleUnitView[] units)
            => new BattleView { Units = new List<BattleUnitView>(units), CurrentUnitId = current };

        [Test]
        public void Build_OnlyOwnSide_ProtagonistFirst_CurrentMarked()
        {
            var view = View("u_zakhar",
                U("u_zakhar", "Player"), U("e_1", "Enemy"), U("u_protagonist", "Player"), U("defector_x", "FromDefector"));
            var slots = PartyPortraitsModel.Build(view, "protagonist");
            Assert.AreEqual(2, slots.Count, "лише свої");
            Assert.AreEqual("protagonist", slots[0].CharacterId);
            Assert.AreEqual("zakhar", slots[1].CharacterId);
            Assert.IsTrue(slots[1].IsCurrent);
            Assert.IsFalse(slots[0].IsCurrent);
        }

        [Test]
        public void Build_DownedStays_OutOfBattleLeaves()
        {
            var view = View(null, U("u_a", "Player", hp: 0, downed: true), U("u_b", "Player", out_: true));
            var slots = PartyPortraitsModel.Build(view, "protagonist");
            Assert.AreEqual(1, slots.Count);
            Assert.IsTrue(slots[0].IsDowned);
            Assert.AreEqual(0f, slots[0].HpFraction);
        }

        [Test]
        public void Build_Fractions_AreClampedShares()
        {
            var slot = PartyPortraitsModel.Build(View(null, U("u_a", "Player", hp: 5, ap: 2)), "protagonist")[0];
            Assert.AreEqual(0.5f, slot.HpFraction, 1e-4f);
            Assert.AreEqual(0.25f, slot.ApFraction, 1e-4f);
        }

        [Test]
        public void CharacterIdOf_StripsBattlePrefixes()
        {
            Assert.AreEqual("zakhar", PartyPortraitsModel.CharacterIdOf("u_zakhar"));
            Assert.AreEqual("myroslava", PartyPortraitsModel.CharacterIdOf("defector_myroslava"));
            Assert.AreEqual("raider_1", PartyPortraitsModel.CharacterIdOf("raider_1"));
        }

        [Test]
        public void Layout_FitsBetweenWheelAndBottomPanel_ShrinkingCards()
        {
            var roomy = PartyPortraitsModel.Layout(4, 10f, 300f, 900f, 80f, 6f);
            Assert.AreEqual(4, roomy.Count);
            Assert.AreEqual(80f, roomy[0].Width);

            var tight = PartyPortraitsModel.Layout(4, 10f, 300f, 600f, 80f, 6f);
            Assert.AreEqual(4, tight.Count, "у тісноті картки менші, але всі");
            Assert.That(tight[0].Width, Is.LessThan(80f));
            Assert.That(tight[3].Bottom, Is.LessThanOrEqualTo(600.01f));
            for (int i = 1; i < tight.Count; i++)
                Assert.That(tight[i].Y, Is.GreaterThanOrEqualTo(tight[i - 1].Bottom), "картки не налазять одна на одну");
        }

        [Test]
        public void Layout_NoRoom_DrawsNothingRatherThanOverlapping()
        {
            Assert.AreEqual(0, PartyPortraitsModel.Layout(3, 10f, 500f, 520f, 80f, 6f).Count);
            Assert.AreEqual(0, PartyPortraitsModel.Layout(0, 10f, 0f, 900f, 80f, 6f).Count);
        }
    }
}
