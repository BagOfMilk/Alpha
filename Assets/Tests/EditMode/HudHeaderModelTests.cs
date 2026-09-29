using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Game.Core.Characters;
using Game.Core.Characters.Creation;
using Game.Core.Loop;
using Game.Core.Session;
using Game.Core.Session.Views;
using Game.Gameplay.UI;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Спайк H4 (docs/HUD_DESIGN.md §4.2, §8, §11): модель шапки без рушія.
    /// Рішення власника 29.09.2026 «1. B» — слово полоси Напруги в шапці
    /// постійно, при наведенні — драбина сусідніх полос; без чисел, стрілок і
    /// відстані до межі (інв. 3, Статут UI-06).
    /// </summary>
    public class HudHeaderModelTests
    {
        private const Gender G = Gender.Male;

        private static SessionView View(string band = "Murmur", int day = 7, string crowd = "Village",
            int daysInBand = 3, bool patrol = false, bool freePlay = false)
        {
            return new SessionView
            {
                State = SessionState.Morning,
                Day = day,
                Phase = DayPhase.Night,
                Tier = 2,
                CrowdBand = crowd,
                TensionBand = band,
                DaysInBand = daysInBand,
                IsPatrolling = patrol,
                IsFreePlay = freePlay
            };
        }

        private static RosterView Roster(params CompanionStatus[] statuses)
        {
            var list = new List<CompanionSummary>();
            for (int i = 0; i < statuses.Length; i++)
                list.Add(new CompanionSummary { Id = "c" + i, DisplayName = "C" + i, Status = statuses[i] });
            return new RosterView { Companions = list };
        }

        private static HudHeader Build(SessionView view, RosterView roster = null)
        {
            var economy = new EconomyView { Gold = 120, BuildComponent = 14, CraftComponent = 3, Food = 38 };
            return HudHeaderModel.Build(view, SessionState.Morning, HudHeaderModel.ResourcesFrom(economy, G), roster ?? Roster(), G);
        }

        /// <summary>Усі рядки шапки, які бачить гравець, — одним списком.</summary>
        private static List<string> AllStrings(HudHeader h)
        {
            var s = new List<string> { h.DayLine, h.BandKey, h.BandWord, h.BandLine, h.BandAboveWord, h.BandBelowWord, h.AwayLine };
            s.AddRange(h.LadderLines);
            s.AddRange(h.Ladder.Select(r => r.Word + "|" + r.IsCurrent));
            s.AddRange(h.Resources.Select(r => r.Key + "=" + r.Label + ":" + r.Value));
            s.AddRange(h.Badges.Select(b => b.Key + "=" + b.Label));
            return s;
        }

        // ------------------------------ інваріант 3 ------------------------------

        [Test]
        public void Inv3_HeaderAndFeedTypes_ExposeOnlyAllowListedNumbers()
        {
            // Allow-list — за ІМЕНЕМ, як у GameSession_Views_NeverExposeRawHiddenNumbers:
            // доба, значення відкритого ресурсу (R17), люди поза містом, ×N і
            // скільки новин не влізло. Будь-яке нове числове поле моделі шапки
            // чи стрічки мусить пройти сюди свідомо.
            var allowed = new HashSet<string> { "Value", "AwayCount", "Count", "Day", "HiddenNews", "X", "Y", "Width", "Height", "Scale" };
            var numeric = new HashSet<System.Type> { typeof(int), typeof(float), typeof(double), typeof(long) };
            var types = new[]
            {
                typeof(HudHeader), typeof(HudResource), typeof(HudLadderRung), typeof(HudBadge),
                typeof(FeedItem), typeof(FeedPanel), typeof(HudFrame), typeof(HudBox)
            };
            var offenders = new List<string>();
            foreach (var t in types)
            {
                foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
                    if (numeric.Contains(f.FieldType) && !allowed.Contains(f.Name)) offenders.Add(t.Name + "." + f.Name);
                foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    if (numeric.Contains(p.PropertyType) && !allowed.Contains(p.Name)) offenders.Add(t.Name + "." + p.Name);
            }
            Assert.IsEmpty(offenders, "Модель шапки/стрічки віддає невнесене в allow-list число: " + string.Join(", ", offenders));
        }

        [Test]
        public void Inv3_DaysInBand_NeverReachesTheHeader()
        {
            // SessionView.DaysInBand публічний, але це числовий слід прихованої
            // шкали (HUD_DESIGN §2.5) — шапка його не показує ні числом, ні
            // побічно (інша драбина, інше слово).
            var a = AllStrings(Build(View(daysInBand: 1)));
            var b = AllStrings(Build(View(daysInBand: 17)));
            CollectionAssert.AreEqual(a, b, "Скільки діб у полосі — не в шапці");
        }

        [Test]
        public void Inv3_BandWordAndLadder_CarryNoDigits()
        {
            foreach (var band in HudHeaderModel.BandOrder)
            {
                var h = Build(View(band));
                var words = new List<string> { h.BandWord, h.BandLine, h.BandAboveWord, h.BandBelowWord };
                words.AddRange(h.LadderLines);
                words.AddRange(h.Ladder.Select(r => r.Word));
                foreach (var w in words.Where(x => x != null))
                {
                    Assert.IsFalse(w.Any(char.IsDigit), band + ": у слові полоси чи драбині є цифра — «" + w + "»");
                    // Рішення власника 29.09.2026 «1. B»: без стрілок (напрям руху шкали — теж слід числа).
                    Assert.IsFalse(w.IndexOfAny("↑↓→←⇧⇩▲▼△▽↗↘%".ToCharArray()) >= 0, band + ": у драбині стрілка чи відсоток — «" + w + "»");
                }
                Assert.IsFalse(string.IsNullOrEmpty(h.BandWord), band + ": слово полоси порожнє");
            }
        }

        // ------------------------------ драбина ------------------------------

        [Test]
        public void Ladder_BottomEdge_Calm_HasOnlyAbove()
        {
            var h = Build(View("Calm"));
            Assert.AreEqual("Спокій", h.BandWord);
            Assert.IsNull(h.BandBelowWord, "нижче Спокою нічого немає");
            Assert.AreEqual("Ропіт", h.BandAboveWord);
            Assert.AreEqual(2, h.LadderLines.Count, "на краю — два рядки: вище і зараз");
            StringAssert.Contains("Ропіт", h.LadderLines[0]);
            StringAssert.Contains("Спокій", h.LadderLines[1]);
        }

        [Test]
        public void Ladder_TopEdge_Fracture_HasOnlyBelow()
        {
            var h = Build(View("Fracture"));
            Assert.AreEqual("Злам", h.BandWord);
            Assert.IsNull(h.BandAboveWord, "вище Зламу нічого немає");
            Assert.AreEqual("Розпал", h.BandBelowWord);
            Assert.AreEqual(2, h.LadderLines.Count);
            StringAssert.Contains("Злам", h.LadderLines[0]);
            StringAssert.Contains("Розпал", h.LadderLines[1]);
        }

        [Test]
        public void Ladder_Middle_Ferment_NeighboursAreMurmurAndHeat()
        {
            var h = Build(View("Ferment"));
            Assert.AreEqual("Ропіт", h.BandBelowWord);
            Assert.AreEqual("Розпал", h.BandAboveWord);
            CollectionAssert.AreEqual(new[] { "Вище — Розпал", "Зараз — Бродіння", "Нижче — Ропіт" }, h.LadderLines.ToArray());
        }

        [Test]
        public void Ladder_IsOrderedBottomUp_WithExactlyOneCurrent()
        {
            var h = Build(View("Heat"));
            CollectionAssert.AreEqual(new[] { "Спокій", "Ропіт", "Бродіння", "Розпал", "Злам" }, h.Ladder.Select(r => r.Word).ToArray());
            Assert.AreEqual(1, h.Ladder.Count(r => r.IsCurrent));
            Assert.AreEqual("Розпал", h.Ladder.Single(r => r.IsCurrent).Word);
        }

        // ------------------------------ шапка ------------------------------

        [Test]
        public void DayLine_IsDayPhaseTierName_WithoutTierNumberDuplicate()
        {
            var h = Build(View(day: 7, crowd: "Village"));
            Assert.AreEqual("Доба 7 · Ранок · Село", h.DayLine);
            foreach (var s in AllStrings(h).Where(x => x != null))
                StringAssert.DoesNotContain("Тір", s, "дубль «Тір N» прибрано (HUD_DESIGN §2.2)");
        }

        [Test]
        public void Resources_AreAGenericListFromTheWallet_InTableOrder()
        {
            var h = Build(View());
            CollectionAssert.AreEqual(new[] { "gold", "build_component", "craft_component", "food" }, h.Resources.Select(r => r.Key).ToArray());
            CollectionAssert.AreEqual(new[] { 120, 14, 3, 38 }, h.Resources.Select(r => r.Value).ToArray());
            CollectionAssert.AreEqual(new[] { "Золото", "Будматеріал", "Сировина", "Їжа" }, h.Resources.Select(r => r.Label).ToArray());
        }

        [Test]
        public void ResourceTable_CoversEveryEconomyField()
        {
            // Паралельна гілка ділить «матеріали» на два компоненти: після злиття
            // в EconomyView з'явиться нове поле — і цей тест скаже додати РЯДОК
            // у таблицю HudHeaderModel.ResourceRows (більше нічого).
            var fields = typeof(EconomyView).GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => f.FieldType == typeof(int)).Select(f => f.Name).ToList();
            var missing = fields.Where(f => !HudHeaderModel.CoveredEconomyFields.Contains(f)).ToList();
            Assert.IsEmpty(missing, "Поле гаманця без рядка в шапці: " + string.Join(", ", missing));
        }

        [Test]
        public void Away_CountsOnlyCompanionsOnMission()
        {
            var h = Build(View(), Roster(CompanionStatus.OnMission, CompanionStatus.Assigned, CompanionStatus.OnMission, CompanionStatus.Dead));
            Assert.AreEqual(2, h.AwayCount);
            Assert.AreEqual("Поза містом: 2", h.AwayLine);

            var home = Build(View(), Roster(CompanionStatus.Assigned, CompanionStatus.Idle));
            Assert.AreEqual(0, home.AwayCount);
            Assert.IsNull(home.AwayLine, "усі вдома — позначки немає");
        }

        [Test]
        public void Badges_PatrolAndFreePlay()
        {
            var h = Build(View(patrol: true, freePlay: true));
            CollectionAssert.AreEqual(new[] { "patrolling", "freeplay" }, h.Badges.Select(b => b.Key).ToArray());
            Assert.IsEmpty(Build(View()).Badges);
        }

        // ------------------------------ розкладка ------------------------------

        [Test]
        public void Layout_Scale_FollowsTheSmallerSide_Clamped()
        {
            Assert.AreEqual(1f, HudLayout.ScaleFor(1280, 720), 1e-4);
            Assert.AreEqual(1.25f, HudLayout.ScaleFor(1600, 900), 1e-4);
            Assert.AreEqual(1.5f, HudLayout.ScaleFor(1920, 1080), 1e-4);
            Assert.AreEqual(1f, HudLayout.ScaleFor(1024, 600), 1e-4, "нижче мінімуму не зменшуємо");
            Assert.AreEqual(2f, HudLayout.ScaleFor(7680, 4320), 1e-4, "стеля 2");
            Assert.AreEqual(1.2f, HudLayout.ScaleFor(2560, 864), 1e-4, "ультраширокий — від висоти");
        }

        [Test]
        public void Layout_BodyNeverOverlapsHeaderOrFeed_AtAllReferenceSizes()
        {
            foreach (var size in new[] { (1280f, 720f), (1600f, 900f), (1920f, 1080f), (2560f, 1080f) })
            {
                var f = HudLayout.For(size.Item1, size.Item2);
                Assert.IsFalse(f.Body.Overlaps(f.Header), size + ": тіло під шапкою");
                Assert.IsFalse(f.Body.Overlaps(f.Feed), size + ": тіло під стрічкою");
                Assert.IsFalse(f.Feed.Overlaps(f.Header), size + ": стрічка під шапкою");
                Assert.AreEqual(size.Item1, f.Body.Width + f.Feed.Width, 0.5f, size + ": тіло і стрічка ділять ширину без щілини");
            }
            Assert.AreEqual(360f, HudLayout.For(1600, 900).Feed.Width, 5f, "§5.1: стрічка ≈360 px на 1600");
            Assert.AreEqual(290f, HudLayout.For(1280, 720).Feed.Width, 1f, "§5.1: ≈300 px на 1280");
        }
    }
}
