using System.Collections.Generic;
using System.Linq;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Combat;
using Game.Core.Quests;
using Game.Core.Scenes;
using Game.Core.Session;
using Game.Core.Session.Bots;
using Game.Core.Session.Views;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Поправка №12.7, переглянута Поправкою №12.9 (рішення власника
    /// 29.09.2026: «рада — віче просто неба від старту, Зала ради — пізніше
    /// як розширення»; «давай подумаємо які 3 по логіці мають бути першими в
    /// місті» → варіанти першої будівлі — ремесла присутніх фахівців
    /// (Сторожа/Склад/Лазарет), рада більше не серед них). Охоронці: віче
    /// працює без Зали (Облава/переселенці/підготовка), Указ/Дипломатія/
    /// Інвестиція/Спорядження досі за нею, вибір першої будівлі існує в обох
    /// гілках розмови з Тугаром, кожен варіант має видимий наслідок (Статут
    /// MECH-05), вибір живе в зліпку, боти обирають детерміновано.
    /// </summary>
    public class FirstBuildingChoiceTests
    {
        private static NewGameOptions Quick() => new NewGameOptions { SkipCreation = true, HitRule = HitRuleKind.Threshold };

        /// <summary>Проходить пролог: на виборі Тугара — <paramref name="tugarOption"/>, на виборі будівлі — <paramref name="buildingId"/>. Повертає крок вибору будівлі.</summary>
        private static SceneStepView PlayOpening(GameSession s, string buildingId, int tugarOption = 0)
        {
            SceneStepView buildingStep = null;
            var step = s.AdvanceScene();
            while (!step.IsFinished)
            {
                if (!step.IsChoice) { step = s.AdvanceScene(); continue; }
                if (step.ChoiceId == OpeningScenes.FirstBuildingChoiceId)
                {
                    buildingStep = step;
                    step = s.ChooseSceneOption(System.Array.IndexOf(DefaultBuildings.FirstBuildingChoices, buildingId));
                }
                else step = s.ChooseSceneOption(tugarOption);
            }
            Assert.AreEqual(SessionState.Morning, s.State, "після прологу — ранок доби 1");
            return buildingStep;
        }

        private static bool Built(GameSession s, string id) => s.GetCityView().Built.Any(b => b.Id == id);

        [Test]
        public void NewGame_BeforeTheChoice_HasOnlyTheCouncilSquare()
        {
            var s = new GameSession();
            s.NewGame(Quick());
            Assert.AreEqual(SessionState.Scene, s.State);
            CollectionAssert.IsEmpty(s.GetCityView().Built, "до вибору не стоїть жодна будівля");

            // Поправка №12.9: рада-віче просто неба — Захар на посту з першого
            // ранку, незалежно від вибору першої будівлі (він і не будівля).
            CollectionAssert.Contains(s.GetCityView().OpenPosts, "council_seat", "віче зібралося ще до вибору");
            Assert.AreEqual("council_seat", s.GetRosterView().Companions.First(c => c.Id == "zakhar").AssignedSlotId);

            CollectionAssert.DoesNotContain(s.GetCityView().OpenPosts, "storehouse_dock");
            CollectionAssert.DoesNotContain(s.GetCityView().OpenPosts, "infirmary_bed");
        }

        [Test]
        public void EveryTugarBranch_LeadsToTheFirstBuildingChoice_ThenToNode1()
        {
            for (int tugar = 0; tugar < 3; tugar++)
            {
                var s = new GameSession();
                s.NewGame(Quick());
                var step = PlayOpening(s, DefaultBuildings.Watch, tugar);
                Assert.IsNotNull(step, "гілка " + tugar + ": вибір першої будівлі мав з'явитися");
                Assert.AreEqual(DefaultBuildings.FirstBuildingChoices.Length, step.Options.Count);
                for (int i = 0; i < step.Options.Count; i++)
                    Assert.AreEqual("scene.neighbour.option." + DefaultBuildings.FirstBuildingChoices[i], step.Options[i].TextKey);
                Assert.IsTrue(s.DayLog.Any(e => e.Key == "scene.finished" && e.Args["transition"] == "to.node1.pass"),
                    "усі гілки сходяться в тому самому вузлі 1");
            }
        }

        [Test]
        public void EachChoice_BuildsOnlyThatBuilding_ForFree()
        {
            foreach (var buildingId in DefaultBuildings.FirstBuildingChoices)
            {
                var s = new GameSession();
                s.NewGame(Quick());
                int goldBefore = s.GetEconomyView().Gold;
                PlayOpening(s, buildingId);

                Assert.IsTrue(Built(s, buildingId), buildingId + ": обрана будівля стоїть одразу");
                Assert.AreEqual(1, s.GetCityView().Built.Count, buildingId + ": решта — за звичайними правилами (№6)");
                Assert.AreEqual(goldBefore, s.GetEconomyView().Gold, buildingId + ": перше спільне зусилля — без ціни");

                foreach (var other in DefaultBuildings.FirstBuildingChoices.Where(o => o != buildingId))
                    Assert.IsFalse(Built(s, other));

                Assert.IsTrue(s.DayLog.Any(e => e.Key == "city.granted" && e.Args["buildingId"] == buildingId), "зміна міста звучить (MECH-13)");
                Assert.IsTrue(s.DayLog.Any(e => e.Key == "scene.choice.made" && e.Args["optionId"] == buildingId));
            }
        }

        /// <summary>Склад і Лазарет відкривають і заселяють свій пост; Сторожа поста не має — Захар і так на віче з ранку 1.</summary>
        [Test]
        public void StorehouseAndInfirmary_OpenAndStaffTheirPost()
        {
            foreach (var buildingId in new[] { DefaultBuildings.Storehouse, DefaultBuildings.Infirmary })
            {
                var s = new GameSession();
                s.NewGame(Quick());
                PlayOpening(s, buildingId);

                string slot = DefaultBuildings.Get(buildingId).OpensSlotId;
                string keeper = OpeningScenes.FirstBuildingKeeperOf(buildingId);
                CollectionAssert.Contains(s.GetCityView().OpenPosts, slot);
                Assert.AreEqual(slot, s.GetRosterView().Companions.First(c => c.Id == keeper).AssignedSlotId,
                    buildingId + ": на пост стає свій іменний — наслідок видно одразу (MECH-05)");
                Assert.IsTrue(s.DayLog.Any(e => e.Key == "city.granted.staffed" && e.Args["companionId"] == keeper));
            }
        }

        [Test]
        public void Watch_HasNoSlot_AndDoesNotMoveZakhar()
        {
            var s = new GameSession();
            s.NewGame(Quick());
            PlayOpening(s, DefaultBuildings.Watch);

            Assert.IsNull(DefaultBuildings.Get(DefaultBuildings.Watch).OpensSlotId, "Сторожа поста не відкриває");
            Assert.AreEqual("council_seat", s.GetRosterView().Companions.First(c => c.Id == "zakhar").AssignedSlotId,
                "Захар лишається на віче — вибір Сторожі його нікуди не переставляє");
            Assert.IsFalse(s.DayLog.Any(e => e.Key == "city.granted.staffed"), "Сторожа нікого не заселяє");
        }

        /// <summary>Поправка №12.9: рада-віче діє з першого ранку незалежно від вибору першої будівлі — Облава/переселенці/підготовка до загрози не питають Залу.</summary>
        [Test]
        public void CouncilSquare_WorksFromDayOne_RegardlessOfFirstBuildingChoice_ButDecreeAndFriendsNeedTheHall()
        {
            var s = new GameSession();
            s.NewGame(Quick());
            PlayOpening(s, DefaultBuildings.Storehouse);

            Assert.AreNotEqual(CouncilOrderResult.NoCouncilHall, s.OrderRaid(), "Облава — рішення віче, Зали не питає");
            Assert.AreNotEqual(CouncilOrderResult.NoCouncilHall, s.OrderSettlers(), "переселенці — так само");
            Assert.AreNotEqual(CouncilOrderResult.NoCouncilHall, s.OrderPrepareThreat(), "підготовка до загрози — так само");

            Assert.AreEqual(CouncilOrderResult.NoCouncilHall, s.OrderDecree(Game.Core.Factions.DefaultFactions.TuharBoyars),
                "Указ і далі чекає на Залу ради");
            Assert.AreEqual(CouncilOrderResult.NoCouncilHall, s.OrderDiplomacy(Game.Core.Factions.DefaultFactions.TuharBoyars));
            Assert.AreEqual(CouncilOrderResult.NoCouncilHall, s.OrderOutfitExpedition("outskirts"));

            Assert.AreEqual(BuildOrderResult.Started, s.OrderBuilding(DefaultBuildings.CouncilHall),
                "Зала ради — звичайна будівля за ціною (40 золота, 5 діб)");
        }

        [Test]
        public void FirstBuilding_SurvivesSave_IntoAFreshSession()
        {
            var s = new GameSession();
            s.NewGame(Quick());
            PlayOpening(s, DefaultBuildings.Infirmary);
            string blob = s.SaveState(0);

            var fresh = new GameSession();
            fresh.NewGame(Quick());
            fresh.RestoreFromBlob(blob);

            Assert.IsTrue(Built(fresh, DefaultBuildings.Infirmary));
            Assert.IsFalse(Built(fresh, DefaultBuildings.CouncilHall));
            CollectionAssert.Contains(fresh.GetCityView().OpenPosts, "infirmary_bed");
            Assert.AreEqual("infirmary_bed", fresh.GetRosterView().Companions.First(c => c.Id == "healer").AssignedSlotId);
            // Регресія на старий сейв, де першою була обрана Зала ради (до
            // Поправки №12.9): council_seat переживає завантаження й лишається
            // за Захаром незалежно від того, яка будівля прийшла зі зліпку.
            CollectionAssert.Contains(fresh.GetCityView().OpenPosts, "council_seat");
            Assert.AreEqual("council_seat", fresh.GetRosterView().Companions.First(c => c.Id == "zakhar").AssignedSlotId);
        }

        [Test]
        public void Bots_ChooseTheFirstBuilding_Deterministically()
        {
            var steward = BotRunner.PlayDays(new StewardPolicy(), 1, Quick());
            var bloody = BotRunner.PlayDays(new BloodyPolicy(), 1, Quick());
            var again = BotRunner.PlayDays(new StewardPolicy(), 1, Quick());

            Assert.IsTrue(steward.GetCityView().Built.Any(b => b.Id == DefaultBuildings.FirstBuildingChoices[0]),
                "«обережна» політика бере перший варіант");
            Assert.IsTrue(bloody.GetCityView().Built.Any(b => b.Id == DefaultBuildings.FirstBuildingChoices[DefaultBuildings.FirstBuildingChoices.Length - 1]),
                "«кривава» — останній");
            CollectionAssert.AreEqual(steward.GetCityView().Built.Select(b => b.Id).ToList(), again.GetCityView().Built.Select(b => b.Id).ToList());
        }

        [Test]
        public void GrantBuilt_IsIdempotent_AndRemovesAPendingProject()
        {
            var state = new BaseState(new Roster(), new Game.Core.Economy.ResourceLedger(), new Game.Core.Balance.BalanceConfig());
            foreach (var slot in Game.Core.DefaultContent.AllSlots()) state.AddSlot(slot);
            state.Resources.Add(Game.Core.Economy.ResourceType.Gold, 100);
            var works = new CityWorks();
            works.ApplyToSlots(state);

            Assert.AreEqual(BuildOrderResult.Started, works.Order(DefaultBuildings.Workshop, state));
            Assert.IsTrue(works.GrantBuilt(DefaultBuildings.Workshop, state));
            Assert.IsFalse(works.IsBuilding(DefaultBuildings.Workshop), "незавершений проєкт знято — двічі одна будівля не стоїть");
            Assert.IsTrue(state.GetSlot("workshop_bench").Unlocked);
            Assert.IsFalse(works.GrantBuilt(DefaultBuildings.Workshop, state), "вдруге — нічого");
            Assert.IsFalse(works.GrantBuilt("no_such_building", state));
        }

        [Test]
        public void QuestConsequence_Building_IsAConsequence_AndMerges()
        {
            var c = new QuestConsequence().Building(DefaultBuildings.Storehouse);
            Assert.IsFalse(c.IsEmpty, "будівля — видимий наслідок, не порожній вибір");
            var merged = QuestConsequence.Merge(c, new QuestConsequence().Building(DefaultBuildings.Infirmary));
            CollectionAssert.AreEqual(new[] { DefaultBuildings.Storehouse, DefaultBuildings.Infirmary }, merged.BuildingIds);
        }

        // ---- Поправка №12.9: варіанти вибору — ремесла присутніх (хук на чистій функції) ----

        [Test]
        public void AvailableFirstBuildingChoices_WithNoFilter_ReturnsTheWholeCatalogInOrder()
        {
            CollectionAssert.AreEqual(DefaultBuildings.FirstBuildingChoices,
                OpeningScenes.AvailableFirstBuildingChoices());
        }

        [Test]
        public void AvailableFirstBuildingChoices_ExcludesTheChoiceWhoseKeeperIsAbsent()
        {
            var result = OpeningScenes.AvailableFirstBuildingChoices(companionId => companionId != "keeper");
            CollectionAssert.DoesNotContain(result, DefaultBuildings.Storehouse, "Дід Овсій відсутній — Склад не серед варіантів");
            CollectionAssert.Contains(result, DefaultBuildings.Watch);
            CollectionAssert.Contains(result, DefaultBuildings.Infirmary);
        }

        // ---- стрибок сцени (SceneStep.Goto) ----

        [Test]
        public void Goto_JumpsToTheLabel_AndTheValidatorCatchesAMissingOne()
        {
            var scene = new Scene("t", "t.title")
                .Step(SceneStep.Shot("zakhar", ShotFraming.Close))
                .Step(SceneStep.Goto("end"))
                .Step(SceneStep.Line("zakhar", "never.said"))
                .Step(SceneStep.Line("zakhar", "said").WithLabel("end"))
                .Step(SceneStep.Transition("done"));

            var play = new ScenePlayback(scene);
            var lines = new List<string>();
            while (play.Next())
                if (play.Current.LineKey != null) lines.Add(play.Current.LineKey);
            CollectionAssert.AreEqual(new[] { "said" }, lines, "після стрибка пропущена репліка не звучить");
            Assert.AreEqual("done", play.TransitionKey);

            var cast = OpeningCast.All();
            var problems = SceneValidator.Validate(scene, cast);
            Assert.IsTrue(problems.Any(p => p.Contains("після переходу") || p.Contains("после перехода")),
                "крок одразу після стрибка без мітки — мертвий: " + string.Join("; ", problems));

            var broken = new Scene("b", "b.title")
                .Step(SceneStep.Shot("zakhar", ShotFraming.Close))
                .Step(SceneStep.Goto("nowhere"))
                .Step(SceneStep.Transition("done").WithLabel("x"));
            Assert.IsTrue(SceneValidator.Validate(broken, cast).Any(p => p.Contains("nowhere")), "стрибок у нікуди ловиться до редактора");
        }
    }
}
