using System.Linq;
using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Economy;
using Game.Core.Expeditions;
using Game.Core.Loop;
using Game.Core.Stats;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Вихід і повернення партії (Поправка №5.6 п. 4, чеклист §3 стр. 15 і 18).
    ///
    /// Перевіряється ціна: поки вихід не знімав пости, вилазка була безкоштовною —
    /// люди йшли і водночас працювали вдома.
    /// </summary>
    public class ExpeditionPartyTests
    {
        private static BaseState Build()
        {
            var cfg = new BalanceConfig();
            var roster = new Roster();
            var state = new BaseState(roster, new ResourceLedger(), cfg);
            foreach (var slot in Game.Core.DefaultContent.AllSlots()) state.AddSlot(slot);

            foreach (var (id, skill, post) in new[]
            {
                ("guard", SkillType.Survival, "storehouse_dock"),
                ("trader", SkillType.Trade, "settlement_market"),
                ("medic", SkillType.Medicine, "infirmary_bed")
            })
            {
                var arch = new CompanionArchetype(id, id);
                arch.SetSkill(skill, 6);
                roster.Add(arch.CreateInstance(id));
                state.TryAssign(id, post);
            }
            return state;
        }

        [Test]
        public void Depart_EmptiesThePostsOfThoseWhoLeft()
        {
            var state = Build();
            var party = new ExpeditionParty();

            Assert.IsTrue(party.Depart(state, new[] { "guard", "medic" }, days: 2));

            Assert.IsNull(state.GetSlot("storehouse_dock").AssignedCompanionId, "склад опустел");
            Assert.IsNull(state.GetSlot("infirmary_bed").AssignedCompanionId, "лазарет опустел");
            Assert.AreEqual("trader", state.GetSlot("settlement_market").AssignedCompanionId,
                "оставшийся стоит где стоял");
            Assert.AreEqual(CompanionStatus.OnMission, state.Roster.Get("guard").Status);
        }

        /// <summary>Повернення НЕ розставляє нікого: розстановка — рішення гравця.</summary>
        [Test]
        public void Return_DoesNotReassignAnyone()
        {
            var state = Build();
            var party = new ExpeditionParty();
            party.Depart(state, new[] { "guard" }, days: 1);

            Assert.IsTrue(party.TickDay(), "сутки прошли — партия дома");
            var returned = party.Return(state);

            CollectionAssert.Contains(returned.ToList(), "guard");
            Assert.AreEqual(CompanionStatus.Idle, state.Roster.Get("guard").Status);
            Assert.IsNull(state.GetSlot("storehouse_dock").AssignedCompanionId,
                "пост остался пустым: игрок расставляет заново, уже зная, что было без него");
            Assert.IsFalse(party.IsAway);
        }

        [Test]
        public void Calendar_CountsDownAndReportsArrival()
        {
            var state = Build();
            var party = new ExpeditionParty();
            party.Depart(state, new[] { "guard" }, days: 3);

            Assert.AreEqual(3, party.DaysRemaining);
            Assert.IsFalse(party.TickDay());
            Assert.IsFalse(party.TickDay());
            Assert.IsTrue(party.TickDay(), "на третьи сутки партия дошла");
        }

        [Test]
        public void SecondDeparture_WhileAway_IsRefused()
        {
            var state = Build();
            var party = new ExpeditionParty();
            party.Depart(state, new[] { "guard" }, days: 2);

            Assert.IsFalse(party.Depart(state, new[] { "trader" }, days: 2),
                "партия одна: вторую снарядить нечем");
            Assert.AreEqual("trader", state.GetSlot("settlement_market").AssignedCompanionId);
        }

        /// <summary>
        /// Продовження невідрізниме від безперервного (чеклист §3 стр. 18): після
        /// завантаження люди стоять там же, ті, хто пішов, все ще в полі, календар той самий.
        /// </summary>
        [Test]
        public void SaveAndLoad_KeepsRosterAndPartyInTheField()
        {
            var state = Build();
            var party = new ExpeditionParty();
            party.Depart(state, new[] { "guard", "medic" }, days: 2);
            party.TickDay();

            var adapter = new RosterAdapter(state.Roster);
            string rosterBlob = ((IStateBlob)adapter).CaptureState();
            string partyBlob = party.CaptureState();

            // Інша сесія: той самий контент, стан лише зі зліпка.
            var reloaded = Build();
            var reloadedAdapter = new RosterAdapter(reloaded.Roster);
            ((IStateBlob)reloadedAdapter).RestoreState(rosterBlob);
            var reloadedParty = new ExpeditionParty();
            reloadedParty.RestoreState(partyBlob);

            Assert.AreEqual(CompanionStatus.OnMission, reloaded.Roster.Get("guard").Status,
                "ушедший после загрузки всё ещё в поле, а не дома");
            Assert.IsNull(reloaded.Roster.Get("guard").AssignedSlotId, "и не стоит на посту");
            Assert.AreEqual("settlement_market", reloaded.Roster.Get("trader").AssignedSlotId,
                "оставшийся стоит там же");
            Assert.AreEqual(1, reloadedParty.DaysRemaining, "календарь вылазки тот же");
            CollectionAssert.AreEquivalent(party.Away.ToList(), reloadedParty.Away.ToList());
        }

        [Test]
        public void NobodyToSend_IsRefused()
        {
            var state = Build();
            var party = new ExpeditionParty();

            Assert.IsFalse(party.Depart(state, new[] { "ghost" }, days: 2));
            Assert.IsFalse(party.IsAway);
        }
    }
}
