using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Characters.Traits;
using Game.Core.Companions;
using Game.Core.Economy;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Лояльність на Companion (R2), таблиця наслідків (LoyaltyRules, B4) і
    /// сейв-безперервність. Якір/споживач/сигнал (інваріант 6) — див.
    /// коментар над <see cref="Companion.LoyaltyBand"/>: якір "наскільки
    /// напарник вірить у твій шлях і громаду", споживач — Defection/CompanionArc,
    /// сигнал — <see cref="LoyaltyChange.BandChanged"/> на кожну зміну полоси.
    /// </summary>
    public class LoyaltyRulesTests
    {
        private static Companion Comp(Roster roster, string id, params string[] values)
        {
            var arch = new CompanionArchetype(id, id);
            if (values.Length > 0)
            {
                var trait = new TraitDefinition("vt_" + id, id);
                foreach (var v in values) trait.WithValue(v);
                arch.AddStartingTrait(trait);
            }
            var c = arch.CreateInstance(id);
            roster.Add(c);
            return c;
        }

        // ---- Полоси 0..100 -> 5, якорні точки сценарію доба 1 ----
        [Test]
        public void Default_Loyalty_Is50_Steady()
        {
            var c = new CompanionArchetype("c", "c").CreateInstance("c");
            Assert.AreEqual(LoyaltyBand.Steady, c.LoyaltyBand);
        }

        [Test]
        public void BandFor_MatchesScenarioAnchors()
        {
            var social = new CompanionSocialBalance();
            Assert.AreEqual(LoyaltyBand.Wary, social.BandFor(45), "старт Мирослави (§3.0)");
            Assert.AreEqual(LoyaltyBand.Steady, social.BandFor(60), "старт Максима (§3.0)");
            Assert.AreEqual(LoyaltyBand.Resentful, social.BandFor(25), "розв'язка вузла 1, Базова (§3.1)");
            Assert.AreEqual(LoyaltyBand.Resentful, social.BandFor(10), "розв'язка вузла 1, Найгірша (§3.1)");
            Assert.AreEqual(LoyaltyBand.Broken, social.BandFor(9));
            Assert.AreEqual(LoyaltyBand.Devoted, social.BandFor(70));
        }

        [Test]
        public void ApplyLoyaltyDelta_ReportsBandChange_OnlyWhenBandCrossed()
        {
            var c = new CompanionArchetype("c", "c").CreateInstance("c"); // 50 = Steady (нижня межа полоси)
            c.ApplyLoyaltyDelta(10); // 60 — впевнено в середині Steady, подалі від межі

            var small = c.ApplyLoyaltyDelta(-1); // 59, все ще Steady
            Assert.IsFalse(small.BandChanged);

            var big = c.ApplyLoyaltyDelta(-25); // 34 -> Wary
            Assert.IsTrue(big.BandChanged);
            Assert.AreEqual(LoyaltyBand.Steady, big.From);
            Assert.AreEqual(LoyaltyBand.Wary, big.To);
        }

        [Test]
        public void ApplyLoyaltyDelta_ClampsToRange()
        {
            var c = new CompanionArchetype("c", "c").CreateInstance("c");
            c.ApplyLoyaltyDelta(-1000);
            Assert.AreEqual(LoyaltyBand.Broken, c.LoyaltyBand);
            c.ApplyLoyaltyDelta(1000);
            Assert.AreEqual(LoyaltyBand.Devoted, c.LoyaltyBand);
        }

        // ---- Таблиця наслідків ----
        [Test]
        public void OnBloodyChoice_HurtsPeaceValued_ReliefsRuthless_SparesNeutral()
        {
            var roster = new Roster();
            var peaceful = Comp(roster, "peaceful", DefaultValues.Mercy);
            var ruthless = Comp(roster, "ruthless", DefaultValues.Ruthless);
            var neutral = Comp(roster, "neutral", DefaultValues.Profit);
            var cfg = new BalanceConfig();

            LoyaltyRules.OnBloodyChoice(roster, cfg);

            Assert.AreEqual(50 - cfg.CompanionSocial.BloodyChoiceValuedHit, peaceful.Loyalty);
            Assert.AreEqual(50 + cfg.CompanionSocial.BloodyChoiceRuthlessRelief, ruthless.Loyalty);
            Assert.AreEqual(50, neutral.Loyalty, "нейтральные ценности не реагируют на кровавый путь");
        }

        [Test]
        public void OnRequestIgnored_HitsTheRequester()
        {
            var roster = new Roster();
            var c = Comp(roster, "c");
            var cfg = new BalanceConfig();

            var change = LoyaltyRules.OnRequestIgnored(c, cfg);

            Assert.AreEqual(50 - cfg.CompanionSocial.RequestIgnoredHit, c.Loyalty);
            Assert.AreEqual(LoyaltyBand.Steady, change.From);
        }

        [Test]
        public void OnMorale_AuditG25_CouncilSeatPassiveBonus_FeedsLoyalty()
        {
            var roster = new Roster();
            var a = Comp(roster, "a");
            var dead = Comp(roster, "dead");
            dead.MarkDead();
            var cfg = new BalanceConfig();

            // Справжній шлях: слот council_seat з PassiveBonusId="Morale" через
            // BaseState.AdvanceCycle кладе в CycleReport.PassiveBonuses. Тут
            // збираємо мінімальний CycleReport тим самим шляхом, яким його будує
            // BaseState — через слот із Passive-виходом.
            var state = new BaseState(roster, new ResourceLedger(), cfg);
            var slot = new AssignmentSlotDefinition("council_seat", "Рада", BaseSectionType.Council)
            {
                OutputKind = SlotOutputKind.Passive,
                PassiveBonusId = "Morale",
                BaseOutput = 5
            };
            state.AddSlot(slot);
            state.TryAssign("a", "council_seat");

            var cycleReport = InvokeAdvanceCycle(state);

            var changes = LoyaltyRules.OnMorale(cycleReport, roster, cfg);

            Assert.IsTrue(cycleReport.PassiveBonuses.ContainsKey("Morale"), "council_seat действительно даёт Morale");
            Assert.IsTrue(changes.Exists(c => c.CompanionId == "a"));
            Assert.IsFalse(changes.Exists(c => c.CompanionId == "dead"), "мёртвый не получает моральный бонус");
        }

        // AdvanceCycle — internal у BaseState (єдиний легальний викликач —
        // ProductionStep конвеєра); тести мають доступ через InternalsVisibleTo.
        private static CycleReport InvokeAdvanceCycle(BaseState state)
        {
            var method = typeof(BaseState).GetMethod("AdvanceCycle",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return (CycleReport)method.Invoke(state, null);
        }

        // ---- Сейв-безперервність (§4.8 R13: Loyalty входить в roster=) ----
        [Test]
        public void RosterAdapter_SaveRoundTrip_KeepsLoyalty()
        {
            var roster = new Roster();
            var c = Comp(roster, "c");
            c.ApplyLoyaltyDelta(-23); // 27 = Resentful

            var adapter = new RosterAdapter(roster);
            string blob = adapter.CaptureState();

            var reloadedRoster = new Roster();
            Comp(reloadedRoster, "c"); // дефолт 50, як «порожній» інстанс перед завантаженням
            var reloadedAdapter = new RosterAdapter(reloadedRoster);
            reloadedAdapter.RestoreState(blob);

            Assert.AreEqual(27, reloadedRoster.Get("c").Loyalty);
            Assert.AreEqual(LoyaltyBand.Resentful, reloadedRoster.Get("c").LoyaltyBand);
        }

        [Test]
        public void RosterAdapter_RestoreState_ToleratesOldBlobWithoutLoyaltyField()
        {
            // Старий формат (4 поля, без Лояльності) — не повинен падати і не
            // повинен чіпати дефолт.
            var roster = new Roster();
            var c = Comp(roster, "c");
            var adapter = new RosterAdapter(roster);

            string oldBlob = "c>0>>0"; // id>status>slot>injury, без п'ятого поля
            adapter.RestoreState(oldBlob);

            Assert.AreEqual(50, c.Loyalty, "старый слепок без пятого поля не трогает лояльность");
        }
    }
}
