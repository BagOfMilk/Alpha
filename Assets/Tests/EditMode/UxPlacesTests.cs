using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Characters.Creation;
using Game.Core.Combat;
using Game.Core.Session;
using Game.Core.Session.Views;
using Game.Gameplay.Text;
using Game.Gameplay.UI;
using Game.Gameplay.Walk;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Місто без табличок (власник, 30.09.2026: «прибери найбільшу частину
    /// табличок… досі неможна зайти до будівлі і поговорити з персонажем»):
    /// одне місце на будівлю, двері, кімната, люди на своїх місцях, панелі з
    /// карток замість одинадцяти вкладок. Твердження про чисті моделі — сцена
    /// лише застосовує.
    /// </summary>
    public class UxPlacesTests
    {
        private static readonly Regex RawKey = new Regex(@"\b(ui|ux|place|post|building|site|talk)\.[a-z_]+");

        // ---------------- сесія і підставна оболонка ----------------

        private sealed class FakeHost : IUxHost
        {
            public GameSession Session { get; set; }
            public Gender Gender { get; set; } = Gender.Male;
            public UxPanelState PanelState { get; } = new UxPanelState();
            public readonly List<string> Opened = new List<string>();
            public SceneStepView Scene;

            public void OpenPanel(UxPanelId panel, string context) => Opened.Add(panel + ":" + context);
            public void WalkTo(string placeId) => Opened.Add("walk:" + placeId);
            public void BeginScene(SceneStepView first) => Scene = first;
            public IReadOnlyList<SaveSlotView> SaveSlots() => new List<SaveSlotView> { new SaveSlotView { Slot = 0 } };
            public UxOutcome SaveToSlot(int slot) => UxOutcome.Success();
            public UxOutcome LoadSlot(int slot) => UxOutcome.Success();
        }

        /// <summary>Нова гра і пролог (усюди перший варіант) — ранок доби 1.</summary>
        private static FakeHost Morning()
        {
            var s = new GameSession();
            s.NewGame(new NewGameOptions { SkipCreation = true, HitRule = HitRuleKind.Threshold });
            var step = s.AdvanceScene();
            while (step != null && !step.IsFinished)
                step = step.IsChoice ? s.ChooseSceneOption(0) : s.AdvanceScene();
            Assume.That(s.State, Is.EqualTo(SessionState.Morning), "пролог мав привести до ранку");
            return new FakeHost { Session = s };
        }

        private static IEnumerable<string> AllText(UxPanelModel p)
        {
            yield return p.Title;
            if (p.EmptyText != null) yield return p.EmptyText;
            foreach (var c in p.Cards)
            {
                yield return c.Title;
                yield return c.Subtitle;
                yield return c.Section;
                foreach (var ch in c.Chips) yield return ch.Text;
                foreach (var l in c.Lines) yield return l;
                foreach (var a in c.Actions)
                {
                    yield return a.Label;
                    yield return a.DisabledReason;
                    foreach (var ch in a.Chips) yield return ch.Text;
                }
            }
        }

        // ---------------- місця ----------------

        private static PlotAnchor Anchor(string id, float x = 0f, float z = 0f) =>
            new PlotAnchor { BuildingId = id, CenterX = x, CenterZ = z, Half = 1.5f, DoorX = x, DoorZ = z - 2f };

        [Test]
        public void OneBuilding_OnePlace_PlotUntilBuilt_DoorWhenBuilt()
        {
            // Мутація: повернути окремо ділянку й пост (як до 30.09.2026) — у BuildVillage
            // з'являться два місця на будівлю, тест падає.
            var anchor = Anchor(DefaultBuildings.Infirmary, 5f, 5f);
            var plot = VillagePlaces.ForBuilding(anchor, 0);
            Assert.AreEqual(PlaceKind.Plot, plot.Kind);
            Assert.AreEqual(UxPanelId.PlotCard, plot.Panel);
            Assert.AreEqual("plot:" + DefaultBuildings.Infirmary, VillagePlaces.ForBuilding(anchor, 3).Id, "у роботі — та сама ділянка");

            var door = VillagePlaces.ForBuilding(anchor, 5);
            Assert.AreEqual(PlaceKind.Building, door.Kind);
            Assert.AreEqual(UxPanelId.None, door.Panel, "зведена вхідна будівля — заходять, а не відкривають картку");
            Assert.AreEqual(anchor.DoorZ, door.Z, "місце — біля дверей");
            Assert.AreEqual(anchor.CenterZ, door.LabelZ, "підпис — над будівлею");

            var fort = VillagePlaces.ForBuilding(Anchor(DefaultBuildings.Fortifications), 5);
            Assert.AreEqual(UxPanelId.BuildingCard, fort.Panel, "у Укріплення не заходять — чесна картка (UX-10)");

            var plots = UxBricks.BuildingIds.Select(id => Anchor(id)).ToList();
            var places = VillagePlaces.BuildVillage(plots, null, null, null, id => id == DefaultBuildings.Storehouse ? 5 : 0);
            Assert.AreEqual(plots.Count, places.Count, "одне місце на будівлю");
            Assert.AreEqual(places.Count, places.Select(p => p.Id).Distinct().Count());
        }

        [Test]
        public void EveryFirstBuildingChoice_IsEnterable_WithStationsAndExit()
        {
            foreach (var id in DefaultBuildings.FirstBuildingChoices)
            {
                var building = BuildingCatalog.Get(id);
                Assert.IsNotNull(building, id + ": першу будівлю видно в каталозі місць");
                Assert.IsTrue(building.Enterable, id + ": у першу будівлю заходять");
                Assert.That(building.Stations.Count, Is.InRange(1, BuildingCatalog.MaxStations), id);
                var inside = Interiors.PlacesFor(id);
                Assert.AreEqual(building.Stations.Count + 1, inside.Count, id + ": станції і двері виходу");
                Assert.AreEqual(1, inside.Count(p => p.Kind == PlaceKind.Exit));
            }
        }

        [Test]
        public void EveryPost_HasAStation_AndEveryStationHasUkrainianLabel()
        {
            foreach (var postId in UxBricks.PostIds)
                Assert.IsNotNull(BuildingCatalog.StationOfPost(postId), postId + ": пост = станція (UX_DESIGN §4.8)");

            var stations = BuildingCatalog.All.SelectMany(b => b.Stations).Concat(BuildingCatalog.OpenAirStations);
            foreach (var st in stations)
                foreach (var g in new[] { Gender.Male, Gender.Female })
                    Assert.IsTrue(UkrainianText.Has(st.LabelKey, g), st.Id + ": підпис станції");
        }

        [Test]
        public void Interior_StationsAndExit_AreReachableFromSpawn()
        {
            // Та сама сітка, що будує HeroWalker у кімнаті: межі підлоги і п'єдестали.
            foreach (var building in BuildingCatalog.All.Where(b => b.Enterable))
            {
                var grid = new WalkGrid(Interiors.MinX + 0.35f, Interiors.MinZ + 0.35f, Interiors.MaxX - 0.35f, Interiors.MaxZ - 0.35f, 0.25f);
                float half = Interiors.StationSize * 0.5f + 0.3f;
                for (int i = 0; i < building.Stations.Count; i++)
                {
                    var slot = Interiors.Slot(i);
                    grid.Block(slot.X - half, slot.Z - half, slot.X + half, slot.Z + half);
                }
                Assert.IsTrue(grid.IsFree(Interiors.Spawn), building.BuildingId + ": точка появи вільна");
                foreach (var place in Interiors.PlacesFor(building.BuildingId))
                {
                    var path = grid.FindPath(Interiors.Spawn, new WalkPoint(place.X, place.Z));
                    var end = path.Count > 0 ? path[path.Count - 1] : Interiors.Spawn;
                    Assert.LessOrEqual(WalkPoint.Distance(end, new WalkPoint(place.X, place.Z)), place.Radius,
                        building.BuildingId + ": до «" + place.Id + "» можна підійти");
                }
            }
        }

        [Test]
        public void Nearest_SmallPlaceBeatsBigPlot()
        {
            var places = new List<WalkPlace>
            {
                VillagePlaces.Station(BuildingCatalog.FindStation(BuildingCatalog.VecheStation), 0f, 0f, inside: false),
                VillagePlaces.ForBuilding(Anchor(DefaultBuildings.Tavern, 3f, 0f), 0),
            };
            Assert.IsNull(VillagePlaces.Nearest(places, new WalkPoint(0f, 8f)), "далеко від усього — нічого поруч");
            Assert.AreEqual("station:veche", VillagePlaces.Nearest(places, new WalkPoint(0.5f, 0f)).Id);
            Assert.AreEqual("plot:" + DefaultBuildings.Tavern, VillagePlaces.Nearest(places, new WalkPoint(3f, 1f)).Id);
        }

        [Test]
        public void Describe_OneLabel_WithState_NoRawKeys()
        {
            var roster = new RosterView { Companions = new List<CompanionSummary>
            {
                new CompanionSummary { Id = "healer", Status = CompanionStatus.Assigned, AssignedSlotId = "infirmary_bed" }
            } };
            var built = VillagePlaces.ForBuilding(Anchor(DefaultBuildings.Infirmary), 5);
            var building = VillagePlaces.ForBuilding(Anchor(DefaultBuildings.Tavern), 3);
            foreach (var g in new[] { Gender.Male, Gender.Female })
            {
                string a = VillagePlaces.Describe(built, g, 5, roster);
                StringAssert.Contains(UkrainianText.Get("building.infirmary", g), a);
                StringAssert.Contains(ScreenText.ResolveCompanionName("healer", g, roster), a, "хто працює — в тому самому підписі");
                string b = VillagePlaces.Describe(building, g, 3, roster);
                StringAssert.Contains("3 з 5", b);
                foreach (var text in new[] { a, b })
                {
                    Assert.IsFalse(RawKey.IsMatch(text), "ключ без перекладу: " + text);
                    StringAssert.DoesNotContain("{", text);
                }
            }
        }

        [Test]
        public void People_OnPostOrAtTheFire_NeverAwayDeadOrProtagonist()
        {
            // Мутація: пустити до вогнища тих, хто на вилазці, — тест падає.
            var roster = new RosterView { Companions = new List<CompanionSummary>
            {
                new CompanionSummary { Id = GameSession.ProtagonistId, Status = CompanionStatus.Idle },
                new CompanionSummary { Id = "zakhar", Status = CompanionStatus.Assigned, AssignedSlotId = "council_seat" },
                new CompanionSummary { Id = "maksym", Status = CompanionStatus.Idle },
                new CompanionSummary { Id = "myroslava", Status = CompanionStatus.OnMission },
                new CompanionSummary { Id = "keeper", Status = CompanionStatus.Dead },
                new CompanionSummary { Id = "healer", Status = CompanionStatus.Injured },
            } };
            var figures = new Dictionary<string, WalkPoint> { { "council_seat", new WalkPoint(1f, 1f) } };
            var idle = new List<WalkPoint> { new WalkPoint(5f, 5f), new WalkPoint(6f, 6f) };
            var spots = VillagePeople.Arrange(roster, figures, idle);

            CollectionAssert.AreEqual(new[] { "zakhar", "maksym", "healer" }, spots.Select(s => s.CompanionId).ToArray());
            Assert.IsTrue(spots[0].AtPost);
            Assert.AreEqual(1f, spots[0].X);
            Assert.AreEqual(0, spots[1].IdleIndex);
            Assert.AreEqual(1, spots[2].IdleIndex);
        }

        // ---------------- панелі ----------------

        [Test]
        public void EveryPanelAndStation_Builds_Ukrainian_NoRawKeys()
        {
            var h = Morning();
            var models = new List<UxPanelModel>();
            foreach (var info in UxPanelCatalog.All)
                if (info.Id != UxPanelId.Station && info.Id != UxPanelId.Talk)
                    models.Add(UxPanelFactory.Build(h, info.Id, info.Id == UxPanelId.PlotCard || info.Id == UxPanelId.BuildingCard ? DefaultBuildings.Tavern : null));
            foreach (var st in BuildingCatalog.All.SelectMany(b => b.Stations).Concat(BuildingCatalog.OpenAirStations))
                models.Add(UxPanelFactory.Build(h, st.Panel, st.Id));
            foreach (var c in h.Session.GetRosterView().Companions)
            {
                models.Add(UxPanelFactory.Build(h, UxPanelId.Talk, c.Id));
                models.Add(UxPanelFactory.Build(h, UxPanelId.People, c.Id));
            }

            foreach (var g in new[] { Gender.Male, Gender.Female })
            {
                h.Gender = g;
                foreach (var m in models)
                    foreach (var text in AllText(m).Where(t => t != null))
                    {
                        Assert.IsFalse(RawKey.IsMatch(text), m.Id + ": ключ без перекладу: " + text);
                        StringAssert.DoesNotContain("{", text, m.Id + ": не підставлено аргумент");
                    }
            }
        }

        [Test]
        public void Panels_HaveAtMostFourSections()
        {
            var h = Morning();
            foreach (var info in UxPanelCatalog.All)
            {
                var m = UxPanelFactory.Build(h, info.Id, null);
                Assert.LessOrEqual(m.Cards.Select(c => c.Section).Where(s => s != null).Distinct().Count(), 4,
                    info.Slug + ": не більше чотирьох підвкладок (UX_DESIGN §3.3)");
            }
        }

        /// <summary>
        /// Кожна команда ядра, яку кликав старий хаб (одинадцять вкладок), тепер
        /// має домівку — станцію, місце чи панель. Мутація: прибрати облаву з
        /// Віча — тест падає.
        /// </summary>
        [Test]
        public void EveryFormerHubCommand_IsCalledByAPanel()
        {
            string ui = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Assets", "_Project", "Scripts", "Gameplay");
            var sources = Directory.GetFiles(Path.Combine(ui, "UI"), "*.cs", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(ui, "GameShell*.cs"))
                .Select(File.ReadAllText).ToList();
            string[] formerHub =
            {
                "Assign", "Unassign", "OrderBuilding", "OrderRaid", "OrderSettlers", "OrderDecree", "OrderDiplomacy",
                "OrderInvestment", "OrderPrepareThreat", "OrderOutfitExpedition", "PreviewExpedition", "DepartExpedition",
                "Equip", "Unequip", "CraftUpgrade", "PreviewBuildPlan", "CommitBuildPlan", "OfferQuestStage",
                "ResolveQuestChoice", "NewTrainingBattle", "SaveState", "GetMechanicsJournal", "AdvanceDay"
            };
            var missing = formerHub.Where(cmd => !sources.Any(src => src.Contains("." + cmd + "("))).ToList();
            CollectionAssert.IsEmpty(missing, "команди старого хаба без домівки: " + string.Join(", ", missing));
            Assert.IsFalse(File.Exists(Path.Combine(ui, "UI", "HubScreen.cs")), "вкладок хаба більше немає");
        }

        [Test]
        public void Veche_HasRaidSettlersPrepareAndSeat_WithoutHall()
        {
            var h = Morning();
            var veche = UxPanelFactory.Build(h, UxPanelId.Veche, null);
            var ids = veche.Cards.SelectMany(c => c.Actions).Select(a => a.Id).ToList();
            CollectionAssert.Contains(ids, "raid");
            CollectionAssert.Contains(ids, "settlers");
            CollectionAssert.Contains(ids, "prepare_threat");
            CollectionAssert.Contains(ids, "open:blueprints", "креслення — з Віча, огляд без ходьби");
        }

        [Test]
        public void Talk_HasGreeting_AndLinksToSheet_NotForTheAbsent()
        {
            var h = Morning();
            var present = h.Session.GetRosterView().Companions.First(VillagePeople.IsInVillage);
            var talk = UxPanelFactory.Build(h, UxPanelId.Talk, present.Id);
            Assert.IsNotEmpty(talk.Cards[0].Lines, "людина вітається");
            var sheet = talk.Cards[0].Actions.Single(a => a.Id == "talk:sheet:" + present.Id);
            Assert.IsTrue(UxCommandRunner.Invoke(sheet, SessionState.Morning, false).Ok);
            CollectionAssert.Contains(h.Opened, UxPanelId.People + ":" + present.Id);

            var absent = UxTalkPanel.Build(h, "no_such_person");
            Assert.IsEmpty(absent.Cards);
            Assert.IsNotEmpty(absent.EmptyText);
        }

        [Test]
        public void Muster_PickSitePartyPreview_ThenDepartAppears()
        {
            var h = Morning();
            var muster = UxPanelFactory.Build(h, UxPanelId.Muster, null);
            var someone = h.Session.GetRosterView().Companions.First(c => ScreenText.AssignCandidateLegality(c).Enabled);
            var add = muster.Cards.SelectMany(c => c.Actions).Single(a => a.Id == "party:" + someone.Id);
            Assert.IsTrue(UxCommandRunner.Invoke(add, SessionState.Morning, false).Ok);
            CollectionAssert.Contains(h.PanelState.MusterParty, someone.Id);

            muster = UxPanelFactory.Build(h, UxPanelId.Muster, null);
            var preview = muster.Cards.SelectMany(c => c.Actions).Single(a => a.Id == "muster:preview");
            Assert.IsNull(preview.DisabledReason);
            Assert.IsTrue(UxCommandRunner.Invoke(preview, h.Session.State, false).Ok);
            muster = UxPanelFactory.Build(h, UxPanelId.Muster, null);
            Assert.IsTrue(muster.Cards.SelectMany(c => c.Actions).Any(a => a.Id == "muster:depart"), "після прогнозу — «Вирушати»");
        }

        [Test]
        public void Growth_CommitIsIrreversible_AskedToConfirm()
        {
            // Мутація: прибрати Confirm із «Затвердити» — тест падає (UX-12).
            var h = Morning();
            var person = UxPanelFactory.Build(h, UxPanelId.People, GameSession.ProtagonistId);
            var invest = person.Cards.SelectMany(c => c.Actions).FirstOrDefault(a => a.Id.StartsWith("plan:") && a.DisabledReason == null && a.Id != "plan:reset");
            Assume.That(invest, Is.Not.Null, "на старті є очки розвитку");
            UxCommandRunner.Invoke(invest, SessionState.Morning, false);
            person = UxPanelFactory.Build(h, UxPanelId.People, GameSession.ProtagonistId);
            var commit = person.Cards.SelectMany(c => c.Actions).FirstOrDefault(a => a.Id == "plan:commit");
            Assume.That(commit, Is.Not.Null);
            Assert.IsNotNull(commit.Confirm, "затвердження розвитку — лише з підтвердженням");
        }

        [Test]
        public void Preflight_WarnsAboutEmptyPostWithSomeoneFree_OtherwiseSilent()
        {
            var h = Morning();
            var roster = h.Session.GetRosterView();
            var seated = roster.Companions.FirstOrDefault(c => c.AssignedSlotId == "council_seat");
            Assume.That(seated, Is.Not.Null, "Захар на вічі з першого ранку");
            h.Session.Unassign("council_seat");
            var warnings = UxPreflight.Check(h.Session, Gender.Male);
            Assert.IsTrue(warnings.Any(w => w.Contains(UkrainianText.Get("post.council_seat", Gender.Male))),
                "порожнє місце в раді, а людина вільна — нагадати перед «Почати день»");
        }

        [Test]
        public void QuestOffers_NotInFreePlay_AndCachedPerDay()
        {
            var h = Morning();
            var cache = new QuestOfferCache();
            int before = h.Session.DayLog.Count;
            cache.Get(h.Session, Game.Core.Quests.DefaultQuests.StoneSoupId);
            int afterFirst = h.Session.DayLog.Count;
            cache.Get(h.Session, Game.Core.Quests.DefaultQuests.StoneSoupId);
            Assert.AreEqual(afterFirst, h.Session.DayLog.Count, "повторний показ того самого дня не пише в стрічку");
            Assert.IsFalse(QuestOfferCache.CanOffer(SessionState.FreePlay), "у вільній грі ядро пропозицій не дає — не питати");
            Assert.GreaterOrEqual(afterFirst, before);
        }
    }
}
