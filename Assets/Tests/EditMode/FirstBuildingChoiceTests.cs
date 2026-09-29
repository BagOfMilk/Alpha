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
    /// Поправка №12.7 (рішення власника 29.09.2026: «Так придовити, перша
    /// будівля зьявляється як вибор після прологу»): гра стартує без будівель,
    /// а одразу після прологу гравець обирає першу. Охоронці: вибір існує в
    /// обох гілках розмови з Тугаром, кожен варіант має видимий наслідок
    /// (Статут MECH-05), відсутні будівлі читаються (UI-04 — у ядрі це
    /// причина відмови), вибір живе в зліпку, боти обирають детерміновано.
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
        public void NewGame_BeforeTheChoice_HasNoBuildings()
        {
            var s = new GameSession();
            s.NewGame(Quick());
            Assert.AreEqual(SessionState.Scene, s.State);
            CollectionAssert.IsEmpty(s.GetCityView().Built, "до вибору не стоїть жодна будівля");
            CollectionAssert.DoesNotContain(s.GetCityView().OpenPosts, "council_seat");
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
                var step = PlayOpening(s, DefaultBuildings.CouncilHall, tugar);
                Assert.IsNotNull(step, "гілка " + tugar + ": вибір першої будівлі мав з'явитися");
                Assert.AreEqual(DefaultBuildings.FirstBuildingChoices.Length, step.Options.Count);
                for (int i = 0; i < step.Options.Count; i++)
                    Assert.AreEqual("scene.neighbour.option." + DefaultBuildings.FirstBuildingChoices[i], step.Options[i].TextKey);
                Assert.IsTrue(s.DayLog.Any(e => e.Key == "scene.finished" && e.Args["transition"] == "to.node1.pass"),
                    "усі гілки сходяться в тому самому вузлі 1");
            }
        }

        [Test]
        public void EachChoice_BuildsOnlyThatBuilding_ForFree_OpensAndStaffsItsPost()
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

                string slot = DefaultBuildings.Get(buildingId).OpensSlotId;
                string keeper = OpeningScenes.FirstBuildingKeeperOf(buildingId);
                CollectionAssert.Contains(s.GetCityView().OpenPosts, slot);
                Assert.AreEqual(slot, s.GetRosterView().Companions.First(c => c.Id == keeper).AssignedSlotId,
                    buildingId + ": на пост стає свій іменний — наслідок видно одразу (MECH-05)");

                foreach (var other in DefaultBuildings.FirstBuildingChoices.Where(o => o != buildingId))
                {
                    Assert.IsFalse(Built(s, other));
                    CollectionAssert.DoesNotContain(s.GetCityView().OpenPosts, DefaultBuildings.Get(other).OpensSlotId,
                        other + ": пост без будівлі закритий");
                }

                Assert.IsTrue(s.DayLog.Any(e => e.Key == "city.granted" && e.Args["buildingId"] == buildingId), "зміна міста звучить (MECH-13)");
                Assert.IsTrue(s.DayLog.Any(e => e.Key == "city.granted.staffed" && e.Args["companionId"] == keeper));
                Assert.IsTrue(s.DayLog.Any(e => e.Key == "scene.choice.made" && e.Args["optionId"] == buildingId));
            }
        }

        [Test]
        public void WithoutCouncilHall_CouncilSaysWhy_AndTheHallCanBeBuiltNormally()
        {
            var s = new GameSession();
            s.NewGame(Quick());
            PlayOpening(s, DefaultBuildings.Storehouse);

            Assert.AreEqual(CouncilOrderResult.NoCouncilHall, s.OrderRaid(), "без зали — зрозуміла відмова, а не тиша");
            Assert.AreEqual(CouncilOrderResult.NoCouncilHall, s.OrderSettlers());
            Assert.AreEqual(AssignmentResult.SlotLocked, s.Assign("zakhar", "council_seat"), "пост ради закритий, доки нема зали");

            Assert.AreEqual(BuildOrderResult.Started, s.OrderBuilding(DefaultBuildings.CouncilHall),
                "решта будівель — звичайним наказом за ціну");
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
