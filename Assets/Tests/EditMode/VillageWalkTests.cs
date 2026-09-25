using System.Collections.Generic;
using Game.Core.Characters.Creation;
using Game.Gameplay.Text;
using Game.Gameplay.Walk;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Прогулянка селом (власник, 25.09.2026: «Я хотів шоб я міг бігати як у
    /// CRPG»). Сцена лише застосовує модель — тут твердження про саму модель:
    /// герой обходить хату, у стіну не проходить, клік у стіну веде до її
    /// краю, біля місця видно, куди зайти, і кожне місце веде у справжню
    /// вкладку хаба з українським підписом.
    /// </summary>
    public class VillageWalkTests
    {
        // Поле 10×10, клітинка 0.5; «хата» — стіна поперек посередині з проходом угорі.
        private static WalkGrid GridWithWall()
        {
            var grid = new WalkGrid(0f, 0f, 10f, 10f, 0.5f);
            grid.Block(4.6f, 0f, 5.4f, 7.9f);
            return grid;
        }

        [Test]
        public void FindPath_GoesAroundTheWall_NeverThroughIt()
        {
            var grid = GridWithWall();
            var from = new WalkPoint(1f, 1f);
            var to = new WalkPoint(9f, 1f);

            var path = grid.FindPath(from, to);

            Assert.IsNotEmpty(path, "обхід через прохід угорі існує");
            Assert.AreEqual(to.X, path[path.Count - 1].X, 1e-4);
            Assert.AreEqual(to.Z, path[path.Count - 1].Z, 1e-4);

            // Пройти відрізками з кроком 0.1 — жодна точка не в стіні.
            var at = from;
            foreach (var p in path)
            {
                float d = WalkPoint.Distance(at, p);
                int steps = (int)(d / 0.1f) + 1;
                for (int i = 1; i <= steps; i++)
                {
                    var q = new WalkPoint(at.X + (p.X - at.X) * i / steps, at.Z + (p.Z - at.Z) * i / steps);
                    Assert.IsTrue(grid.IsFree(q), "шлях пройшов крізь стіну в точці " + q.X + "," + q.Z);
                }
                at = p;
            }

            bool wentAround = false;
            foreach (var p in path) if (p.Z >= 7.9f) wentAround = true;
            Assert.IsTrue(wentAround, "через стіну не пройти — шлях мусить обігнути її вгорі");
        }

        [Test]
        public void FindPath_IsDeterministic()
        {
            var a = GridWithWall().FindPath(new WalkPoint(1f, 1f), new WalkPoint(9f, 1f));
            var b = GridWithWall().FindPath(new WalkPoint(1f, 1f), new WalkPoint(9f, 1f));
            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a[i].X, b[i].X);
                Assert.AreEqual(a[i].Z, b[i].Z);
            }
        }

        [Test]
        public void FindPath_ClickIntoWall_EndsAtItsFreeEdge()
        {
            var grid = GridWithWall();
            var path = grid.FindPath(new WalkPoint(1f, 1f), new WalkPoint(5f, 2f));
            Assert.IsNotEmpty(path);
            var end = path[path.Count - 1];
            Assert.IsTrue(grid.IsFree(end), "кінець шляху — вільна клітинка");
            Assert.Less(end.X, 4.6f, "клік у стіну веде до її ближнього краю, а не на той бік");
        }

        [Test]
        public void FindPath_WalledOffTarget_GoesNowhere()
        {
            var grid = new WalkGrid(0f, 0f, 10f, 10f, 0.5f);
            grid.Block(4.6f, 0f, 5.4f, 10f); // стіна на всю висоту
            var path = grid.FindPath(new WalkPoint(1f, 1f), new WalkPoint(9f, 1f));
            Assert.IsEmpty(path, "за суцільною стіною шляху немає — герой стоїть, а не телепортується");
        }

        [Test]
        public void Slide_StopsAtWall_AndSlidesAlongIt()
        {
            var grid = GridWithWall();
            // Упритул до стіни, крок по діагоналі в неї: по X — ні, по Z — так.
            var from = new WalkPoint(4.2f, 2f);
            var next = grid.Slide(from, 0.5f, 0.5f);
            Assert.AreEqual(4.2f, next.X, 1e-4, "у стіну не пройти");
            Assert.AreEqual(2.5f, next.Z, 1e-4, "уздовж стіни — ковзає");
        }

        [Test]
        public void NearestFreePoint_LeavesABuildingThatGrewUnderTheHero()
        {
            var grid = GridWithWall();
            var inside = new WalkPoint(5f, 3f);
            Assert.IsFalse(grid.IsFree(inside));
            var outside = grid.NearestFreePoint(inside);
            Assert.IsTrue(grid.IsFree(outside));
            Assert.Less(WalkPoint.Distance(inside, outside), 1.0f, "виходить до найближчого краю");
        }

        [Test]
        public void Nearest_OnlyWithinRadius_ClosestRelativeWins()
        {
            var places = new List<WalkPlace>
            {
                VillagePlaces.Post("council_seat", 0f, 0f),
                VillagePlaces.Plot("tavern", 3f, 0f, 1.5f),
            };
            Assert.IsNull(VillagePlaces.Nearest(places, new WalkPoint(0f, 8f)), "далеко від усього — нікуди зайти");
            Assert.AreEqual("post:council_seat", VillagePlaces.Nearest(places, new WalkPoint(0.5f, 0f)).Id);
            Assert.AreEqual("plot:tavern", VillagePlaces.Nearest(places, new WalkPoint(3f, 1f)).Id);
        }

        /// <summary>
        /// Кожне місце села веде у справжню вкладку хаба: пости — усі сім зі
        /// сцени, дошка — «Квести», майданчик — «Готовність» (там тренувальний
        /// бій), ділянка — «Будівлі». І в кожного — український підпис, а не ключ.
        /// </summary>
        [Test]
        public void EveryPlace_LeadsToARealHubTab_WithAUkrainianLabel()
        {
            string[] scenePosts =
            {
                "council_seat", "storehouse_dock", "settlement_market", "infirmary_bed",
                "settlement_farms", "scouting_post", "workshop_bench"
            };
            var places = new List<WalkPlace>();
            foreach (var id in scenePosts)
            {
                var place = VillagePlaces.Post(id, 0f, 0f);
                Assert.IsNotNull(place, "пост " + id + " є в сцені — він мусить бути місцем прогулянки");
                places.Add(place);
            }
            places.Add(VillagePlaces.NoticeBoard(0f, 0f));
            places.Add(VillagePlaces.TrainingGround(0f, 0f));
            places.Add(VillagePlaces.Plot(Game.Core.Base.DefaultBuildings.Tavern, 0f, 0f, 1.5f));

            Assert.AreEqual(VillagePlaces.TabExpedition, VillagePlaces.TabForPost("scouting_post"), "застава біля воріт — вилазка");
            Assert.AreEqual(VillagePlaces.TabQuests, VillagePlaces.NoticeBoard(0f, 0f).HubTab);
            Assert.AreEqual(VillagePlaces.TabReadiness, VillagePlaces.TrainingGround(0f, 0f).HubTab, "тренувальний бій — у «Готовності»");

            foreach (var g in new[] { Gender.Male, Gender.Female })
                foreach (var place in places)
                {
                    Assert.That(place.HubTab, Is.InRange(0, 10), place.Id);
                    Assert.IsTrue(UkrainianText.Has(Game.Gameplay.UI.ScreenText.HubTabKey(place.HubTab), g), place.Id);
                    string label = VillagePlaces.LabelFor(place, g);
                    StringAssert.DoesNotContain("place.", label, place.Id + ": підпис — ключ без перекладу");
                    StringAssert.DoesNotContain("building.", label, place.Id);
                    StringAssert.DoesNotContain("{", label, place.Id + ": не підставлено аргумент");
                }

            StringAssert.Contains("Таверна", VillagePlaces.LabelFor(places[places.Count - 1], Gender.Male),
                "ділянка підписана назвою будівлі");
        }
    }
}
