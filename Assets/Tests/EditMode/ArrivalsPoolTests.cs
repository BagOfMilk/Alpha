using System.Linq;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Combat;
using Game.Core.Scenes;
using Game.Core.Session;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Поправка №12.10 (пул прибульців, рішення власника 29.09.2026: «поки
    /// всі троє присутні завжди. - погано» → «Нові попаданці в пул»).
    ///
    /// Три шари перевірки: (1) чиста функція <see cref="ArrivalsPool.Determine"/> —
    /// усі 9 комбінацій передісторія×відповідь дають рівно ДВОХ різних
    /// прибульців; (2) варіанти першої будівлі для кожної комбінації —
    /// Сторожа + ремесла прибульців; (3) відсутній фахівець ніде не
    /// з'являється (ростер, пости, перевірки).
    /// </summary>
    public class ArrivalsPoolTests
    {
        private static readonly string[] Backgrounds = { "warrior", "trader", "healer" };
        private static readonly string[] TugarChoices = { "refuse", "bargain", "ask_myroslava" };

        // ================= (1) чиста функція: усі 9 комбінацій =================

        [Test]
        public void Determine_AllNineCombinations_YieldExactlyTwoDistinctArrivals()
        {
            foreach (var bg in Backgrounds)
            foreach (var tugar in TugarChoices)
            {
                var a = ArrivalsPool.Determine(bg, tugar);

                CollectionAssert.Contains(ArrivalsPool.AllSpecialistIds, a.FromBackground,
                    bg + "/" + tugar + ": FromBackground мусить бути з пулу");
                CollectionAssert.Contains(ArrivalsPool.AllSpecialistIds, a.FromTugar,
                    bg + "/" + tugar + ": FromTugar мусить бути з пулу");
                Assert.AreNotEqual(a.FromBackground, a.FromTugar,
                    bg + "/" + tugar + ": прибульці мусять бути РІЗНІ (рівно двоє з чотирьох)");
            }
        }

        [Test]
        public void Determine_Background_MapsToTheRightSpecialist()
        {
            Assert.AreEqual(ArrivalsPool.HealerId, ArrivalsPool.Determine("healer", "refuse").FromBackground);
            Assert.AreEqual(ArrivalsPool.SindbadId, ArrivalsPool.Determine("trader", "refuse").FromBackground);
            Assert.AreEqual(ArrivalsPool.GobanId, ArrivalsPool.Determine("warrior", "refuse").FromBackground);
        }

        [Test]
        public void Determine_TugarChoice_MapsToTheRightSpecialist()
        {
            // trader-фон приводить Синдбада, тож тут беремо warrior-фон
            // (Гобан-Сайр), щоб жодна з трьох відповідей Тугарові з ним не колізувала.
            Assert.AreEqual(ArrivalsPool.KeeperId, ArrivalsPool.Determine("warrior", "refuse").FromTugar);
            Assert.AreEqual(ArrivalsPool.SindbadId, ArrivalsPool.Determine("warrior", "bargain").FromTugar);
            Assert.AreEqual(ArrivalsPool.HealerId, ArrivalsPool.Determine("warrior", "ask_myroslava").FromTugar);
        }

        [Test]
        public void Determine_Collision_FallsBackToKeeper()
        {
            // trader+bargain обидва тягнуть Синдбада — відкат на Діда Овсія.
            var a1 = ArrivalsPool.Determine("trader", "bargain");
            Assert.AreEqual(ArrivalsPool.SindbadId, a1.FromBackground);
            Assert.AreEqual(ArrivalsPool.KeeperId, a1.FromTugar);

            // healer+ask_myroslava обидва тягнуть Гафію — відкат на Діда Овсія.
            var a2 = ArrivalsPool.Determine("healer", "ask_myroslava");
            Assert.AreEqual(ArrivalsPool.HealerId, a2.FromBackground);
            Assert.AreEqual(ArrivalsPool.KeeperId, a2.FromTugar);
        }

        [Test]
        public void Determine_UnknownOrNullInputs_FallBackToDefaults()
        {
            // Дефолт передісторії — Backgrounds.All()[0].Id == "warrior" (Гобан-Сайр);
            // дефолт відповіді Тугарові — перший варіант сцени, "refuse" (Дід Овсій).
            var a = ArrivalsPool.Determine(null, null);
            Assert.AreEqual(ArrivalsPool.GobanId, a.FromBackground);
            Assert.AreEqual(ArrivalsPool.KeeperId, a.FromTugar);
        }

        [Test]
        public void Arrivals_Contains_IsTrueOnlyForTheTwoWhoArrived()
        {
            var a = ArrivalsPool.Determine("healer", "refuse"); // {healer, keeper}
            Assert.IsTrue(a.Contains(ArrivalsPool.HealerId));
            Assert.IsTrue(a.Contains(ArrivalsPool.KeeperId));
            Assert.IsFalse(a.Contains(ArrivalsPool.GobanId));
            Assert.IsFalse(a.Contains(ArrivalsPool.SindbadId));
            Assert.IsFalse(a.Contains(null));
            Assert.IsFalse(a.Contains(""));
        }

        // ================= (2) варіанти першої будівлі за прибульцями =================

        /// <summary>Мапа фахівець → його ремесло (будівля), для звірки з AvailableFirstBuildingChoices.</summary>
        private static string BuildingOf(string specialistId)
        {
            switch (specialistId)
            {
                case "keeper": return DefaultBuildings.Storehouse;
                case "healer": return DefaultBuildings.Infirmary;
                case "goban": return DefaultBuildings.Workshop;
                case "sindbad": return DefaultBuildings.Market;
                default: return null;
            }
        }

        [Test]
        public void AllNineCombinations_FirstBuildingChoices_AreWatchPlusTheTwoArrivedCrafts()
        {
            foreach (var bg in Backgrounds)
            foreach (var tugar in TugarChoices)
            {
                var arrivals = ArrivalsPool.Determine(bg, tugar);
                var expected = new[] { DefaultBuildings.Watch, BuildingOf(arrivals.FromBackground), BuildingOf(arrivals.FromTugar) }
                    .OrderBy(id => System.Array.IndexOf(DefaultBuildings.FirstBuildingChoices, id)).ToArray();

                var actual = OpeningScenes.AvailableFirstBuildingChoices(
                    id => id == "zakhar" || arrivals.Contains(id));

                CollectionAssert.AreEqual(expected, actual,
                    bg + "/" + tugar + ": Сторожа + ремесла рівно двох прибульців, порядок каталогу");
            }
        }

        // ================= (3) відсутній фахівець ніде не з'являється =================

        [Test]
        public void AbsentSpecialist_IsExcludedFromRoster_Posts_AndChecks()
        {
            var s = new GameSession();
            s.NewGame(new NewGameOptions { SkipCreation = true, HitRule = HitRuleKind.Threshold });
            // Дефолт (SkipCreation, без передісторії) = warrior → Гобан-Сайр;
            // tugarOption 0 = refuse → Дід Овсій. Відсутні: Гафія й Синдбад.
            var step = s.AdvanceScene();
            while (!step.IsFinished)
                step = step.IsChoice ? s.ChooseSceneOption(0) : s.AdvanceScene();
            Assert.AreEqual(SessionState.Morning, s.State);

            var rosterIds = s.GetRosterView().Companions.Select(c => c.Id).ToList();
            CollectionAssert.Contains(rosterIds, "goban", "прибулець за передісторією мусить бути в ростері");
            CollectionAssert.Contains(rosterIds, "keeper", "прибулець за Тугаром мусить бути в ростері");
            CollectionAssert.DoesNotContain(rosterIds, "healer", "відсутня Гафія не мусить з'являтися в ростері (вкладка «Люди»)");
            CollectionAssert.DoesNotContain(rosterIds, "sindbad", "відсутній Синдбад не мусить з'являтися в ростері (вкладка «Люди»)");

            // Пост: спробувати призначити відсутнього на відкритий пост — SlotLocked/CompanionUnavailable, не Success.
            Assert.AreNotEqual(AssignmentResult.Success, s.Assign("healer", "settlement_farms"),
                "відсутня Гафія не мусить ставати на жоден пост");
            Assert.AreNotEqual(AssignmentResult.Success, s.Assign("sindbad", "settlement_farms"),
                "відсутній Синдбад не мусить ставати на жоден пост");

            // Квест Гафії не пропонується, коли вона відсутня.
            Assert.IsNull(s.OfferQuestStage(Game.Core.Quests.DefaultQuests.HafiyaId),
                "квест Гафії не пропонується, коли вона не прибила до гурту");
        }

        [Test]
        public void PresentSpecialist_BecomesAvailableForItsOwnBuildingAndQuest()
        {
            var s = new GameSession();
            s.NewGame(new NewGameOptions { SkipCreation = true, HitRule = HitRuleKind.Threshold });
            // tugarOption 2 = ask_myroslava → Гафія прибиває (разом з дефолтним Гобаном-Сайром).
            var step = s.AdvanceScene();
            bool first = true;
            while (!step.IsFinished)
            {
                if (!step.IsChoice) { step = s.AdvanceScene(); continue; }
                int idx;
                if (step.ChoiceId == OpeningScenes.TugarOfferChoiceId) idx = 2;
                else
                {
                    // варіант першої будівлі — Лазарет (Гафія), останній серед доступних.
                    idx = step.Options.Count - 1;
                }
                step = s.ChooseSceneOption(idx);
                first = false;
            }
            Assert.IsFalse(first);
            Assert.AreEqual(SessionState.Morning, s.State);

            Assert.AreEqual("infirmary_bed", s.GetRosterView().Companions.First(c => c.Id == "healer").AssignedSlotId,
                "прибула Гафія стає на свій пост одразу, коли Лазарет — перша будівля");

            var offer = s.OfferQuestStage(Game.Core.Quests.DefaultQuests.HafiyaId);
            Assert.IsNotNull(offer, "квест Гафії пропонується, коли вона присутня");
        }

        // ================= сейв: склад гурту переживає збереження =================

        [Test]
        public void ArrivedStatus_SurvivesSave_IntoAFreshSession()
        {
            var s = new GameSession();
            s.NewGame(new NewGameOptions { SkipCreation = true, HitRule = HitRuleKind.Threshold });
            var step = s.AdvanceScene();
            while (!step.IsFinished)
                step = step.IsChoice ? s.ChooseSceneOption(0) : s.AdvanceScene(); // refuse + перший варіант будівлі
            Assert.AreEqual(SessionState.Morning, s.State);

            string blob = s.SaveState(0);

            var fresh = new GameSession();
            fresh.NewGame(new NewGameOptions { SkipCreation = true, HitRule = HitRuleKind.Threshold });
            fresh.RestoreFromBlob(blob);

            var rosterIds = fresh.GetRosterView().Companions.Select(c => c.Id).ToList();
            CollectionAssert.Contains(rosterIds, "goban");
            CollectionAssert.Contains(rosterIds, "keeper");
            CollectionAssert.DoesNotContain(rosterIds, "healer", "склад гурту — у зліпку: відсутність Гафії теж переживає завантаження");
            CollectionAssert.DoesNotContain(rosterIds, "sindbad");
        }
    }
}
