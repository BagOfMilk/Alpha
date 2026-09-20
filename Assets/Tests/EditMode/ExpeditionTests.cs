using System.Collections.Generic;
using Game.Core;
using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Checks;
using Game.Core.Economy;
using Game.Core.Expeditions;
using Game.Core.Stats;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Заглушка вылазки — единственный кран материалов (Э6.2, Приложение А).
    /// Проверяется не «работает ли», а то, ради чего она написана: что материалы
    /// приходят только отсюда, что тихий путь не калечит и что исход не зависит
    /// ни от порядка в списке, ни от чего-либо случайного.
    /// </summary>
    public class ExpeditionTests
    {
        private static BalanceConfig Cfg() => new BalanceConfig { FoodUpkeepPerCompanion = 0 };

        private static Companion Scout(string id, int survival, BalanceConfig cfg)
        {
            var arch = new CompanionArchetype(id, id);
            arch.SetAttribute(AttributeType.Agility, 5);
            arch.SetSkill(SkillType.Survival, survival);
            return arch.CreateInstance(id, cfg);
        }

        private static List<ISettlementActor> Party(BalanceConfig cfg, params Companion[] cs)
        {
            var list = new List<ISettlementActor>();
            foreach (var c in cs) list.Add(new CompanionActorAdapter(c, false, cfg));
            return list;
        }

        private static ExpeditionSite Site(int threshold = 5)
        {
            return new ExpeditionSite("ruins", "Развалины")
            {
                QuietDays = 6, ForcefulDays = 3,
                QuietSkill = SkillKeys.Survival, ForcefulSkill = SkillKeys.Survival,
                Threshold = threshold, BaseMaterials = 4, BaseGold = 10
            };
        }

        [Test]
        public void PartyValue_LeaderPlusHalfOfEachCompanion()
        {
            var cfg = Cfg();
            var party = Party(cfg, Scout("a", 6, cfg), Scout("b", 4, cfg), Scout("c", 3, cfg));

            // 6 + 4/2 + 3/2 = 6 + 2 + 1 = 9
            Assert.AreEqual(9, ExpeditionResolver.PartyValue(party, SkillKeys.Survival));
        }

        [Test]
        public void PartyValue_DoesNotDependOnOrder()
        {
            var cfg = Cfg();
            var a = Scout("a", 6, cfg); var b = Scout("b", 4, cfg); var c = Scout("c", 3, cfg);

            Assert.AreEqual(
                ExpeditionResolver.PartyValue(Party(cfg, a, b, c), SkillKeys.Survival),
                ExpeditionResolver.PartyValue(Party(cfg, c, a, b), SkillKeys.Survival));
        }

        /// <summary>
        /// Поправка №1 в числах: тихий путь безопасен, пока подготовки хватает.
        /// Если это когда-нибудь перестанет быть правдой, столп сломается молча.
        /// </summary>
        [Test]
        public void QuietApproach_WithEnoughSkill_WoundsNobody()
        {
            var cfg = Cfg();
            var party = Party(cfg, Scout("a", 7, cfg));
            var result = ExpeditionResolver.Resolve(Site(), ExpeditionApproach.Quiet, party, new SiteLedger(), cfg);

            CollectionAssert.IsEmpty(result.Wounded);
            Assert.AreEqual(6, result.Days, "тихий путь длиннее");
        }

        [Test]
        public void ForcefulApproach_IsFasterButWounds()
        {
            var cfg = Cfg();
            var party = Party(cfg, Scout("a", 7, cfg), Scout("b", 3, cfg));
            var result = ExpeditionResolver.Resolve(Site(), ExpeditionApproach.Forceful, party, new SiteLedger(), cfg);

            Assert.AreEqual(3, result.Days, "силовой путь короче");
            Assert.AreEqual(1, result.Wounded.Count);
            Assert.AreEqual("b", result.Wounded[0].ActorId, "достаётся наименее подготовленному");
        }

        /// <summary>Силовой провал калечит всерьёз — по US-4.2 это тир со шрамом.</summary>
        [Test]
        public void ForcefulFailure_LeavesSeriousWounds()
        {
            var cfg = Cfg();
            var party = Party(cfg, Scout("a", 1, cfg), Scout("b", 1, cfg));
            var result = ExpeditionResolver.Resolve(Site(threshold: 9), ExpeditionApproach.Forceful, party, new SiteLedger(), cfg);

            Assert.AreEqual(OutcomeBand.Worst, result.Band);
            Assert.AreEqual(2, result.Wounded.Count);
            foreach (var w in result.Wounded)
                Assert.AreEqual(WoundTier.Serious, w.Tier);
        }

        /// <summary>Точка иссякает предсказуемо и не уходит в ноль.</summary>
        [Test]
        public void Site_DepletesDeterministically_AndHasAFloor()
        {
            var cfg = Cfg();
            var ledger = new SiteLedger();
            var party = Party(cfg, Scout("a", 5, cfg));
            var site = Site();

            var first = ExpeditionResolver.Resolve(site, ExpeditionApproach.Quiet, party, ledger, cfg);
            var second = ExpeditionResolver.Resolve(site, ExpeditionApproach.Quiet, party, ledger, cfg);

            Assert.Greater(first.Materials, second.Materials, "вторая ходка беднее первой");

            for (int i = 0; i < 20; i++) ExpeditionResolver.Resolve(site, ExpeditionApproach.Quiet, party, ledger, cfg);
            var late = ExpeditionResolver.Preview(site, ExpeditionApproach.Quiet, party, ledger, cfg);
            Assert.AreEqual(cfg.ExpeditionDepletionFloor, late.YieldMultiplier, 1e-9, "есть пол");
            Assert.Greater(late.Materials, 0, "истощённая точка всё ещё что-то даёт");
        }

        /// <summary>Предпросмотр обещает ровно то, что выдаст резолв (US-17.3).</summary>
        [Test]
        public void Preview_MatchesResolve()
        {
            var cfg = Cfg();
            var party = Party(cfg, Scout("a", 6, cfg));
            var site = Site();

            var preview = ExpeditionResolver.Preview(site, ExpeditionApproach.Quiet, party, new SiteLedger(), cfg);
            var result = ExpeditionResolver.Resolve(site, ExpeditionApproach.Quiet, party, new SiteLedger(), cfg);

            Assert.AreEqual(preview.Materials, result.Materials);
            Assert.AreEqual(preview.Gold, result.Gold);
            Assert.AreEqual(preview.Band, result.Band);
            Assert.AreEqual(preview.Days, result.Days);
            Assert.AreEqual(preview.ExpectedWounded, result.Wounded.Count);
        }

        [Test]
        public void SameInputs_GiveSameResult()
        {
            var cfg = Cfg();
            var site = Site();
            var a = ExpeditionResolver.Resolve(site, ExpeditionApproach.Forceful,
                Party(cfg, Scout("a", 4, cfg), Scout("b", 2, cfg)), new SiteLedger(), cfg);
            var b = ExpeditionResolver.Resolve(site, ExpeditionApproach.Forceful,
                Party(cfg, Scout("a", 4, cfg), Scout("b", 2, cfg)), new SiteLedger(), cfg);

            Assert.AreEqual(a.Band, b.Band);
            Assert.AreEqual(a.Materials, b.Materials);
            Assert.AreEqual(a.Wounded.Count, b.Wounded.Count);
            Assert.AreEqual(a.Wounded[0].ActorId, b.Wounded[0].ActorId);
        }
    }
}
