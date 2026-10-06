using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Game.Core.Base;
using Game.Core.Characters.Creation;
using Game.Core.Combat;
using Game.Core.Session;
using Game.Core.Session.Views;
using Game.Gameplay.UI;
using Game.Gameplay.Walk;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Поправка №18 (власник, 05.10.2026: «…таблиці були опціональні і усе
    /// можна було обрати через світ гри, а не в меню, меню має залишитись
    /// тільки опціональним»). Охоронці трьох шарів: кожна команда ядра, яку
    /// кличе інтерфейс, має дім (<see cref="UxWorldHomes"/>); панелі огляду не
    /// мають власних дій (UX-04); станції без дієслова лише зі справжнім станом
    /// (UX-10); «Показати в селі» веде до місця, що існує; дія — не глибше трьох
    /// кроків від села (UX-06); у кожної клавіші реєстру є обробник. Списки
    /// «ще не у світі» — храповики: лише скорочуються кроками U8–U13.
    /// </summary>
    public class WorldFirstTests
    {
        // ---------------- храповики (лише скорочуються) ----------------

        /// <summary>Команди, що ще живуть поза світом; крок плану, що їх закриє, — у <see cref="UxHome.Where"/>.</summary>
        private static readonly string[] ExpectedGaps =
        {
            // U8 (06.10.2026): CommitBuildPlan і PreviewBuildPlan — у Наметі героя.
            "AbandonDungeon", "AdvanceNight", "ConfirmEvening", "ExtractDungeon",
            "PushDeeper", "ReactToCrisis", "ResolveDungeonEvent", "ResolveDungeonParley", "ResolveDungeonRoom",
            "ResolveFinale", "ResolveIncident", "SetPatrol",
        };

        /// <summary>Станції без дієслова й без справжнього стану (UX-10). U8: порожньо.</summary>
        private static readonly string[] ExpectedDeadStations = { };

        /// <summary>Мертві «Показати в селі». U8: порожньо (Журнал веде на Заставу, Лабораторії без ділянки немає в кресленнях).</summary>
        private static readonly string[] ExpectedDeadLinks = { };

        /// <summary>Клавіші в реєстрі без обробника у файлі-власнику. U8: порожньо (L прибрано, доки Хроніка порожня).</summary>
        private static readonly string[] ExpectedUnhandledKeys = { };

        // ---------------- сесія і оболонка без рушія ----------------

        internal sealed class Host : IUxHost
        {
            public GameSession Session { get; set; }
            public Gender Gender { get; set; } = Gender.Male;
            public UxPanelState PanelState { get; } = new UxPanelState();
            public readonly UxInputCore Core;
            public readonly List<string> Walks = new List<string>();
            public SceneStepView Scene;
            /// <summary>Слот 0 зайнятий — для перевірки підтвердження незворотного (завантаження поверх гри).</summary>
            public bool SlotOccupied;
            public int Loaded = -1;

            public Host(GameSession s)
            {
                Session = s;
                Core = new UxInputCore(this);
            }

            public void OpenPanel(UxPanelId panel, string context) => Core.Open(panel, context);
            public void WalkTo(string placeId) { Core.ClosePanel(); Walks.Add(placeId); }
            public void BeginScene(SceneStepView first) { Core.ClosePanel(); Scene = first; }
            public IReadOnlyList<SaveSlotView> SaveSlots() => new List<SaveSlotView> { new SaveSlotView { Slot = 0, Occupied = SlotOccupied, Day = 1 } };
            public UxOutcome SaveToSlot(int slot) => UxOutcome.Success();
            public UxOutcome LoadSlot(int slot) { Loaded = slot; return UxOutcome.Success(); }
        }

        /// <summary>Нова гра і пролог (усюди перший варіант) — ранок доби 1.</summary>
        internal static Host Morning()
        {
            var s = new GameSession();
            s.NewGame(new NewGameOptions { SkipCreation = true, HitRule = HitRuleKind.Threshold });
            var step = s.AdvanceScene();
            while (step != null && !step.IsFinished)
                step = step.IsChoice ? s.ChooseSceneOption(0) : s.AdvanceScene();
            Assume.That(s.State, Is.EqualTo(SessionState.Morning), "пролог мав привести до ранку");
            return new Host(s);
        }

        private static string Gameplay => Path.Combine(Application.dataPath, "_Project", "Scripts", "Gameplay");

        private static IEnumerable<UxAction> Actions(UxPanelModel m) => m.Cards.SelectMany(c => c.Actions);

        /// <summary>Відкриті команди <see cref="GameSession"/> (без читань Get/Is/Can/Has/Debug/Try).</summary>
        private static HashSet<string> SessionCommands()
        {
            var skip = new Regex("^(Get|Is|Can|Has|Debug|Try)");
            return new HashSet<string>(typeof(GameSession)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName && !skip.IsMatch(m.Name))
                .Select(m => m.Name));
        }

        // ---------------- кожна команда має дім ----------------

        /// <summary>
        /// Кожна команда ядра, яку кличе будь-який файл Gameplay, записана в
        /// таблиці домівок. Мутація: прибрати з таблиці облаву — тест падає.
        /// </summary>
        [Test]
        public void EveryCommandTheUiCalls_HasADeclaredHome()
        {
            var sources = Directory.GetFiles(Gameplay, "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText).ToList();
            var homeless = SessionCommands()
                .Where(cmd => sources.Any(src => src.Contains("." + cmd + "(")))
                .Where(cmd => UxWorldHomes.Of(cmd).Count == 0)
                .OrderBy(x => x).ToList();
            CollectionAssert.IsEmpty(homeless, "команди без дому (UxWorldHomes): " + string.Join(", ", homeless));
        }

        [Test]
        public void EveryHome_NamesARealCommand_AndARealPlace()
        {
            var commands = SessionCommands();
            var landmarks = new[] { UxWorldHomes.AnyPlot, UxWorldHomes.AnyPerson, VillagePlaces.NoticeBoardId, VillagePlaces.TrainingGroundId, VillagePlaces.HeroTentId };
            foreach (var h in UxWorldHomes.All)
            {
                Assert.IsTrue(commands.Contains(h.Command), h.Command + ": такої команди в GameSession немає");
                switch (h.Kind)
                {
                    case UxHomeKind.Station:
                        Assert.IsNotNull(BuildingCatalog.FindStation(h.Where), h.Command + ": станції «" + h.Where + "» немає в каталозі");
                        break;
                    case UxHomeKind.Place:
                        CollectionAssert.Contains(landmarks, h.Where, h.Command + ": невідоме місце «" + h.Where + "»");
                        break;
                    case UxHomeKind.Gap:
                        StringAssert.IsMatch(@"^U\d+$", h.Where, h.Command + ": дірка мусить називати крок плану");
                        break;
                    case UxHomeKind.Hud:
                        Assert.AreEqual(UxWorldHomes.PhaseButton, h.Where, h.Command);
                        break;
                }
            }
        }

        /// <summary>
        /// Храповик: «ще не у світі» лише скорочується. Крок закрив дірку —
        /// приберіть її з <see cref="ExpectedGaps"/>; нова дірка — заборонено.
        /// </summary>
        [Test]
        public void GapList_OnlyShrinks()
        {
            CollectionAssert.AreEqual(ExpectedGaps.OrderBy(x => x, StringComparer.Ordinal).ToList(),
                UxWorldHomes.GapCommands().OrderBy(x => x, StringComparer.Ordinal).ToList());
        }

        // ---------------- дім справжній ----------------

        /// <summary>
        /// Дім, видимий уже ранку доби 1: панель його місця справді має дію з
        /// цією командою. Мутація: змінити позначку облави на прибульців — тест падає.
        /// </summary>
        [Test]
        public void DayOneHomes_HaveTheTaggedActionInTheirPlace()
        {
            var h = Morning();
            var city = h.Session.GetCityView();
            foreach (var home in UxWorldHomes.All.Where(x => x.InWorld && x.Check == UxHomeCheck.DayOne))
            {
                UxPanelModel model;
                if (home.Kind == UxHomeKind.Station)
                {
                    var st = BuildingCatalog.FindStation(home.Where);
                    h.Core.Open(st.Panel, st.Id);
                    if (home.Via != UxPanelId.None)
                    {
                        string go = "open:" + UxPanelCatalog.Get(home.Via).Slug;
                        Assert.IsTrue(h.Core.Invoke(go).Ok, home.Command + ": зі станції «" + st.Id + "» немає переходу «" + go + "»");
                        Assert.AreEqual(home.Via, h.Core.OpenPanel);
                    }
                    model = h.Core.CurrentModel();
                }
                else if (home.Where == UxWorldHomes.AnyPlot)
                {
                    string plot = UxBricks.BuildingIds.First(b => BuildingCatalog.HasPlot(b) && UxBricks.Stage(city, b, out _) == 0);
                    model = UxPanelFactory.Build(h, UxPanelId.PlotCard, plot);
                }
                else if (home.Where == VillagePlaces.NoticeBoardId) model = UxPanelFactory.Build(h, UxPanelId.NoticeBoard, null);
                else if (home.Where == VillagePlaces.TrainingGroundId) model = UxPanelFactory.Build(h, UxPanelId.TrainingGround, null);
                else { Assert.Fail(home.Command + ": для «" + home.Where + "» перевірки доби 1 немає — позначте Source"); return; }

                Assert.IsTrue(Actions(model).Any(a => a.Command == home.Command),
                    home.Command + ": у «" + home.Where + "» немає дії з цією командою");
            }
        }

        private static readonly string[] VecheExtras =
        {
            "RecruitPrisoner", "RansomPrisoner", "ReleasePrisoner", "RansomCaptive", "NegotiateCaptive", "RaidCaptors"
        };

        /// <summary>
        /// Дім, що з'являється лише в певному стані: дія позначена в коді
        /// моделей; полонені — у блоці під Вічем (IMGUI, трек бою №14).
        /// </summary>
        [Test]
        public void SourceHomes_AreTaggedInTheModelCode()
        {
            string models = string.Join("\n", Directory.GetFiles(Path.Combine(Gameplay, "UI", "Models"), "*.cs").Select(File.ReadAllText));
            string extras = File.ReadAllText(Path.Combine(Gameplay, "UI", "PrisonersPanel.cs")) +
                            File.ReadAllText(Path.Combine(Gameplay, "UI", "CaptivesPanel.cs"));
            string shell = File.ReadAllText(Path.Combine(Gameplay, "GameShell.Ux.cs"));
            StringAssert.Contains("PrisonersPanel.Draw", shell, "блок полонених малюється під Вічем");
            StringAssert.Contains("CaptivesPanel.Draw", shell);

            foreach (var home in UxWorldHomes.All.Where(x => x.InWorld && x.Check == UxHomeCheck.Source))
            {
                string cmd = home.Command;
                bool tagged = models.Contains("Calls(nameof(GameSession." + cmd + "))") || models.Contains("Command = nameof(GameSession." + cmd + ")");
                if (VecheExtras.Contains(cmd)) tagged = extras.Contains("." + cmd + "(");
                // Пропозиція квесту і прогноз розвитку — не дії, а розрахунок, щойно відкрито картку місця.
                if (cmd == nameof(GameSession.OfferQuestStage) || cmd == nameof(GameSession.PreviewBuildPlan)) tagged = models.Contains("." + cmd + "(");
                Assert.IsTrue(tagged, cmd + " («" + home.Where + "»): дія не позначена в коді моделей");
            }
        }

        /// <summary>
        /// Позначка дії не бреше: поруч із <c>Calls(nameof(GameSession.X))</c> кличеться
        /// саме <c>Session.X(</c>, і кожен виклик будь-якої команди ядра в моделях —
        /// будь-яким шляхом, не лише через «Session.» — позначений (огляд 06.10.2026:
        /// непозначена дія в панелі огляду інакше проходила повз охоронців).
        /// Мутація: позначити облаву як прибульців — тест падає.
        /// </summary>
        [Test]
        public void CallsTags_MatchTheCommandsTheyRun()
        {
            var tag = new Regex(@"Calls\(nameof\(GameSession\.(\w+)\)\)");
            var init = new Regex(@"Command = nameof\(GameSession\.(\w+)\)");
            var call = new Regex(@"\.(\w+)\(");
            var commands = new HashSet<string>(UxWorldHomes.All.Select(x => x.Command));
            // Розрахунок, щойно відкрито картку (не дія): пропозиція квесту, прогноз розвитку.
            var computed = new HashSet<string> { nameof(GameSession.OfferQuestStage), nameof(GameSession.PreviewBuildPlan) };
            var delegated = new Dictionary<string, string> { { "SaveState", "save(index)" }, { "ContinueGame", "load(index)" } };
            var problems = new List<string>();
            foreach (var file in Directory.GetFiles(Path.Combine(Gameplay, "UI", "Models"), "*.cs"))
            {
                var lines = File.ReadAllLines(file);
                string name = Path.GetFileName(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    foreach (Match m in tag.Matches(lines[i]))
                        if (!Window(lines, i - 12, i).Contains("Session." + m.Groups[1].Value + "("))
                            problems.Add(name + ":" + (i + 1) + " позначено " + m.Groups[1].Value + ", а кличеться інше");
                    foreach (Match m in init.Matches(lines[i]))
                    {
                        string cmd = m.Groups[1].Value;
                        string expect = delegated.TryGetValue(cmd, out var d) ? d : "Session." + cmd + "(";
                        if (!Window(lines, i, i + 12).Contains(expect))
                            problems.Add(name + ":" + (i + 1) + " Command = " + cmd + ", а кличеться інше");
                    }
                    foreach (Match m in call.Matches(lines[i]))
                    {
                        string cmd = m.Groups[1].Value;
                        if (!commands.Contains(cmd) || computed.Contains(cmd) || lines[i].TrimStart().StartsWith("//") || lines[i].TrimStart().StartsWith("///")) continue;
                        if (lines[i].Contains("nameof(GameSession." + cmd + ")")) continue;
                        // Головна кнопка фази (HP-2) — сама собі дім, не дія картки: лише в UxPhaseButton.
                        if (name == "UxPhaseButton.cs" && UxWorldHomes.Of(cmd).Any(x => x.Kind == UxHomeKind.Hud)) continue;
                        bool ok = Window(lines, i, i + 12).Contains("Calls(nameof(GameSession." + cmd + "))") ||
                                  Window(lines, i - 12, i).Contains("Command = nameof(GameSession." + cmd + ")");
                        if (!ok) problems.Add(name + ":" + (i + 1) + " виклик " + cmd + " без позначки дії");
                    }
                }
            }
            CollectionAssert.IsEmpty(problems, string.Join("\n", problems));
        }

        private static string Window(string[] lines, int from, int to)
        {
            from = Math.Max(0, from);
            to = Math.Min(lines.Length - 1, to);
            return from > to ? string.Empty : string.Join("\n", lines, from, to - from + 1);
        }

        // ---------------- панелі огляду без власних дій (UX-04) ----------------

        /// <summary>
        /// Панелі огляду (усі з клавішею в каталозі — C, J, N, F10 — і картки людей):
        /// кожна їхня дія з командою має дім у світі (UX-04). Пільги для дірок немає
        /// (огляд 06.10.2026). Мутація: дати Журналу власну дію — тест падає.
        /// </summary>
        [Test]
        public void OverviewPanels_HaveNoVerbsOfTheirOwn()
        {
            var h = Morning();
            var overview = UxPanelCatalog.All.Where(p => p.Hotkey != null)
                .Select(p => UxPanelFactory.Build(h, p.Id, null)).ToList();
            Assert.GreaterOrEqual(overview.Count, 4, "C, J, N, F10");
            foreach (var c in h.Session.GetRosterView().Companions)
                overview.Add(UxPanelFactory.Build(h, UxPanelId.People, c.Id));

            var own = overview.SelectMany(Actions).Where(a => a.Command != null && !UxWorldHomes.InWorld(a.Command))
                .Select(a => a.Command).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
            CollectionAssert.IsEmpty(own, "дії панелей огляду, яких немає у світі: " + string.Join(", ", own));
        }

        // ---------------- станції (UX-10) ----------------

        [Test]
        public void EveryStation_HasAVerbOrARealState()
        {
            var stations = BuildingCatalog.All.SelectMany(b => b.Stations).Concat(BuildingCatalog.OpenAirStations).ToList();
            var dead = stations.Where(s => !UxWorldHomes.StationHasVerb(s.Id) && !UxWorldHomes.StateOnlyStations.ContainsKey(s.Id))
                .Select(s => s.Id).OrderBy(x => x, StringComparer.Ordinal).ToList();
            CollectionAssert.AreEqual(ExpectedDeadStations, dead, "станції без дієслова й без справжнього стану (UX-10)");

            var h = Morning();
            foreach (var id in UxWorldHomes.StateOnlyStations.Keys)
            {
                Assert.IsNotNull(BuildingCatalog.FindStation(id), id);
                var m = UxPanelFactory.Build(h, UxPanelId.Station, id);
                Assert.IsTrue(m.Cards.Count > 0 || !string.IsNullOrEmpty(m.EmptyText), id + ": стан показано словами або порожній стан навчає");
            }
        }

        // ---------------- місця і посилання ----------------

        /// <summary>Ділянки в коді сцени — ті самі, що в каталозі (`HasPlot`). Мутація: дати Лабораторії ділянку в каталозі — тест падає.</summary>
        [Test]
        public void ScenePlots_MatchTheCatalog()
        {
            string builder = File.ReadAllText(Path.Combine(Application.dataPath, "_Project", "Scripts", "Editor", "GameSceneBuilder.cs"));
            var fields = typeof(DefaultBuildings).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral && f.FieldType == typeof(string)).ToDictionary(f => f.Name, f => (string)f.GetRawConstantValue());
            var inScene = Regex.Matches(builder, @"Plot\(group, Game\.Core\.Base\.DefaultBuildings\.(\w+)")
                .Cast<Match>().Select(m => fields[m.Groups[1].Value]).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var inCatalog = BuildingCatalog.All.Select(b => b.BuildingId).Where(BuildingCatalog.HasPlot).OrderBy(x => x, StringComparer.Ordinal).ToList();
            CollectionAssert.AreEqual(inCatalog, inScene);
        }

        /// <summary>«Показати в селі» веде до місця, що існує в селі. Храповик: два мертві посилання — U8.</summary>
        [Test]
        public void Links_LeadToPlacesThatExist()
        {
            var h = Morning();
            var models = new List<UxPanelModel>();
            foreach (var info in UxPanelCatalog.All)
                if (info.Id != UxPanelId.Station && info.Id != UxPanelId.Talk && info.Id != UxPanelId.PlotCard && info.Id != UxPanelId.BuildingCard)
                    models.Add(UxPanelFactory.Build(h, info.Id, null));
            foreach (var b in UxBricks.BuildingIds)
            {
                models.Add(UxPanelFactory.Build(h, UxPanelId.PlotCard, b));
                models.Add(UxPanelFactory.Build(h, UxPanelId.BuildingCard, b));
            }
            foreach (var st in BuildingCatalog.All.SelectMany(b => b.Stations).Concat(BuildingCatalog.OpenAirStations))
                models.Add(UxPanelFactory.Build(h, st.Panel, st.Id));
            foreach (var c in h.Session.GetRosterView().Companions)
            {
                models.Add(UxPanelFactory.Build(h, UxPanelId.Talk, c.Id));
                models.Add(UxPanelFactory.Build(h, UxPanelId.People, c.Id));
            }

            var places = UxWorldReach.VillagePlaceIds(h.Session);
            var dead = models.SelectMany(m => m.Cards).Select(c => c.LinkPlaceId)
                .Where(id => id != null && !places.Contains(id)).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
            CollectionAssert.AreEqual(ExpectedDeadLinks.OrderBy(x => x, StringComparer.Ordinal).ToList(), dead, "мертві «Показати в селі»");
        }

        // ---------------- кроки (UX-06) і клавіші ----------------

        /// <summary>Дія у світі — не глибше трьох кроків: будівля → станція → дія (UX-06).</summary>
        [Test]
        public void EveryWorldVerb_AtMostThreeStepsFromTheVillage()
        {
            foreach (var home in UxWorldHomes.All.Where(x => x.InWorld))
            {
                int steps = (home.Kind == UxHomeKind.Station && BuildingCatalog.BuildingOfStation(home.Where) != null ? 2 : 1)
                            + (home.Via != UxPanelId.None ? 1 : 0) + 1;
                Assert.LessOrEqual(steps, 3, home.Command + " у «" + home.Where + "»: " + steps + " кроків");
            }
        }

        /// <summary>Кожна клавіша реєстру читається у файлі-власнику. Храповик: L (Хроніка) — U8 ховає.</summary>
        [Test]
        public void EveryRegisteredKey_IsHandledByItsOwner()
        {
            var files = Directory.GetFiles(Gameplay, "*.cs", SearchOption.AllDirectories)
                .GroupBy(Path.GetFileName).ToDictionary(g => g.Key, g => g.First());
            var unhandled = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var b in UxKeyMap.All)
            {
                Assert.IsTrue(files.ContainsKey(b.OwnerFile), b.Key + ": файлу-власника «" + b.OwnerFile + "» немає");
                // Без коментарів і цілим словом: «KeyCode.F1» не рахується за «KeyCode.F10», «E» — за «Escape» (огляд 06.10.2026).
                string src = Regex.Replace(File.ReadAllText(files[b.OwnerFile]), @"//[^\n]*", string.Empty);
                string key = Regex.IsMatch(b.Key, @"^Alpha\d$") ? "Alpha1" : b.Key;
                if (!Regex.IsMatch(src, @"KeyCode\." + key + @"\b")) unhandled.Add(b.Key + "@" + b.OwnerFile);
            }
            CollectionAssert.AreEqual(ExpectedUnhandledKeys, unhandled.ToList(), "клавіші без обробника");
        }

        // ---------------- Намет героя (U8) ----------------

        /// <summary>
        /// Розвиток героя — у світі: Намет біля Віча показує ту саму картку, що
        /// «Люди → Ти» (UX-04: одна дія — одна картка), і досяжний з села одним кроком.
        /// Мутація: прибрати картку розвитку з Намету — тест падає.
        /// </summary>
        [Test]
        public void HeroTent_ShowsTheSameGrowthCardAsTheSheet()
        {
            var h = Morning();
            var tent = UxPanelFactory.Build(h, UxPanelId.HeroTent, VillagePlaces.HeroTentId);
            var sheet = UxPanelFactory.Build(h, UxPanelId.People, GameSession.ProtagonistId);
            string growthTitle = Game.Gameplay.Text.UkrainianText.Get("ui.buildplanner.title", Gender.Male);
            Assert.IsTrue(sheet.Cards.Any(c => c.Title == growthTitle), "у картці героя є розвиток");
            Assert.IsTrue(tent.Cards.Any(c => c.Title == growthTitle), "Намет показує ту саму картку розвитку");
            var growthIds = Actions(sheet).Where(a => a.Id.StartsWith("plan:")).Select(a => a.Id).ToList();
            CollectionAssert.IsSubsetOf(growthIds, Actions(tent).Select(a => a.Id).ToList(), "ті самі дії розвитку");
            var entry = UxWorldReach.Now(h.Session).SingleOrDefault(e => e.PlaceId == VillagePlaces.HeroTentId);
            Assert.IsNotNull(entry, "Намет — місце в селі");
            Assert.AreEqual(1, entry.Depth);
            Assert.IsTrue(UxWorldHomes.InWorld(nameof(GameSession.CommitBuildPlan)), "затвердження розвитку має дім у світі");
        }

        /// <summary>
        /// Без очок і без плану картка розвитку — одне речення, а не десять сірих «+1» з
        /// однаковою причиною (UX-13; знімок туру 06.10.2026). На старті очок немає.
        /// </summary>
        [Test]
        public void Growth_WithoutPoints_TeachesInsteadOfTenGreyButtons()
        {
            var h = Morning();
            var tent = UxPanelFactory.Build(h, UxPanelId.HeroTent, VillagePlaces.HeroTentId);
            Assume.That(Actions(tent).Any(a => a.Id.StartsWith("plan:") && a.DisabledReason == null), Is.False, "на старті очок немає");
            Assert.IsFalse(Actions(tent).Any(a => a.Id.StartsWith("plan:")), "сірих «+1» без очок немає");
            Assert.IsTrue(tent.Cards.SelectMany(c => c.Lines).Any(l => l == Game.Gameplay.Text.UkrainianText.Get("ux.growth.no_points", Gender.Male)));
        }

        // ---------------- правдивість світу (огляд 06.10.2026) ----------------

        /// <summary>
        /// Застава пропонує кожну точку ядра: звичайні — тихо чи силою, данжі — лише вглиб.
        /// Раніше «Старого скиту» (другий данж, зустріч Гафії №15.1) у грі не було, а
        /// «Покинутий табір» пропонував підхід, якого ядро для данжу не знає.
        /// Мутація: прибрати данжі з переліку Застави — тест падає.
        /// </summary>
        [Test]
        public void Muster_OffersEveryCoreSite_DungeonsOnlyByDelve()
        {
            var h = Morning();
            var core = Game.Core.Expeditions.DefaultSites.All().Select(x => x.Id)
                .Concat(Game.Core.Dungeons.DefaultDungeon.KnownSiteIds).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
            var muster = UxPanelFactory.Build(h, UxPanelId.Muster, null);
            var offered = Actions(muster).Where(a => a.Id.StartsWith("site:")).Select(a => a.Id.Substring(5)).OrderBy(x => x, StringComparer.Ordinal).ToList();
            CollectionAssert.AreEqual(core, offered, "Застава пропонує всі точки ядра");

            foreach (var site in core)
            {
                h.Core.Open(UxPanelId.Muster, BuildingCatalog.MusterStation);
                Assert.IsTrue(h.Core.Invoke("site:" + site).Ok);
                var approaches = Actions(h.Core.CurrentModel()).Where(a => a.Id.StartsWith("approach:")).Select(a => a.Id).ToList();
                if (Game.Core.Dungeons.DefaultDungeon.KnownSiteIds.Contains(site))
                    CollectionAssert.AreEqual(new[] { "approach:" + Game.Core.Expeditions.ExpeditionApproach.Delve }, approaches, site + ": данж — лише вглиб");
                else
                    CollectionAssert.DoesNotContain(approaches, "approach:" + Game.Core.Expeditions.ExpeditionApproach.Delve, site);
            }
        }

        /// <summary>
        /// Етап-перевірку квесту можна пройти у світі (Дошка, людина, ринок) — кнопка
        /// була лише в NightScreen. Мутація: прибрати дію перевірки — тест падає.
        /// </summary>
        [Test]
        public void QuestCheckStage_CanBeFinishedInTheWorld()
        {
            var h = Morning();
            h.Core.Open(UxPanelId.NoticeBoard, VillagePlaces.NoticeBoardId);
            var first = Actions(h.Core.CurrentModel()).FirstOrDefault(a => a.Id.StartsWith("quest:") && a.ReasonIn(SessionState.Morning, false) == null);
            Assume.That(first, Is.Not.Null, "вранці на Дошці є квест із вибором");
            string questId = first.Id.Split(':')[1];
            Assert.IsTrue(h.Core.Invoke(first.Id).Ok);

            h.Core.Open(UxPanelId.NoticeBoard, VillagePlaces.NoticeBoardId);
            var check = Actions(h.Core.CurrentModel()).FirstOrDefault(a => a.Id == "quest:" + questId + ":check");
            Assume.That(h.Session.OfferQuestStage(questId)?.Options?.Count ?? -1, Is.EqualTo(0), "наступний етап — перевірка");
            h.PanelState.Quests.Invalidate(questId);
            h.Core.Open(UxPanelId.NoticeBoard, VillagePlaces.NoticeBoardId);
            check = Actions(h.Core.CurrentModel()).FirstOrDefault(a => a.Id == "quest:" + questId + ":check");
            Assert.IsNotNull(check, "етап-перевірка має дію у світі");
            Assert.AreEqual(nameof(GameSession.ResolveQuestChoice), check.Command);
            Assert.IsNull(check.ReasonIn(SessionState.Morning, false), "вранці перевірку можна спробувати");
            Assert.IsTrue(h.Core.Invoke(check.Id).Ok, "перевірка пройшла через той самий вхід, що й людина");
        }

        /// <summary>
        /// «Що досяжне зі світу» ставить людей так само, як сцена: фігура на пості й
        /// <see cref="VillagePeople.IdleSpotCount"/> місць біля вогнища — не всіх мешканців поспіль.
        /// </summary>
        [Test]
        public void WorldReach_PeopleAreThoseTheSceneActuallyPlaces()
        {
            var h = Morning();
            var reach = UxWorldReach.Now(h.Session).Where(e => e.Panel == UxPanelId.Talk).Select(e => e.Context).ToList();
            var roster = h.Session.GetRosterView();
            int idleInVillage = roster.Companions.Count(c => VillagePeople.IsInVillage(c) && string.IsNullOrEmpty(c.AssignedSlotId));
            int onPosts = roster.Companions.Count(c => VillagePeople.IsInVillage(c) && !string.IsNullOrEmpty(c.AssignedSlotId));
            Assert.AreEqual(onPosts + Math.Min(idleInVillage, VillagePeople.IdleSpotCount), reach.Count);
            Assert.GreaterOrEqual(VillagePeople.IdleSpotCount, 6, "біля вогнища вміщається щонайменше шестеро без поста");
        }

        // ---------------- рішення дня прив'язане до місця (U9) ----------------

        /// <summary>
        /// Кожен інцидент ядра має місце в селі (Поправка №18.3): двері, ділянка чи
        /// станція просто неба, що справді існує зараз. Авангард з перевалу — на Заставі.
        /// Мутація: зламати мапу «пост → місце» — тест падає.
        /// </summary>
        [Test]
        public void EveryIncident_HasAPlaceInTheVillage()
        {
            var h = Morning();
            var city = h.Session.GetCityView();
            var places = UxWorldReach.VillagePlaceIds(h.Session);
            var incidents = Game.Core.World.DefaultIncidents.All().Concat(Game.Core.World.OpeningContent.All()).ToList();
            Assert.IsNotEmpty(incidents);
            foreach (var inc in incidents)
            {
                string place = UxDecisionPlace.Of(new PendingOfferView { IncidentId = inc.Id, RelevantPositionId = inc.RelevantPositionId }, city);
                CollectionAssert.Contains(places, place, inc.Id + " (" + inc.RelevantPositionId + ") → " + place);
            }
            Assert.AreEqual(VillagePlaces.StationPrefix + BuildingCatalog.MusterStation,
                UxDecisionPlace.Of(new PendingOfferView { IncidentId = "pass_vanguard", RelevantPositionId = "council_seat" }, city),
                "авангард з перевалу — біля воріт");
        }

        /// <summary>Вид рішення, що справді чекає гравця, несе місце події (ядро віддає id і пост).</summary>
        [Test]
        public void PendingDecision_CarriesItsPlace()
        {
            var h = Morning();
            UxPhaseButton.StartDay(h.Session);
            Assume.That(h.Session.State, Is.EqualTo(SessionState.Decision), "перший день дає рішення");
            var offer = h.Session.GetPendingOffer();
            Assert.IsNotNull(offer);
            Assert.IsNotEmpty(offer.IncidentId, "вид рішення називає інцидент");
            Assert.IsNotEmpty(offer.RelevantPositionId, "вид рішення називає пост");
            CollectionAssert.Contains(UxWorldReach.VillagePlaceIds(h.Session), UxDecisionPlace.Of(offer, h.Session.GetCityView()));
        }

        // ---------------- камера як у Wasteland 3 (W1) ----------------

        /// <summary>
        /// Q/E обертають камеру селом кроками по 90° (той самий жест, що в бою;
        /// Поправка №18.5): кут завжди 0/90/180/270, чотири кроки — повне коло,
        /// плавний поворот іде найкоротшою дугою, відстань до героя не змінюється.
        /// </summary>
        [Test]
        public void CameraOrbit_TurnsByQuarters_ShortestArc_KeepsDistance()
        {
            Assert.AreEqual(90f, CameraOrbit.Turn(0f, +1));
            Assert.AreEqual(270f, CameraOrbit.Turn(0f, -1));
            float yaw = 0f;
            for (int i = 0; i < 4; i++) yaw = CameraOrbit.Turn(yaw, +1);
            Assert.AreEqual(0f, yaw, "чотири кроки — повне коло");
            Assert.AreEqual(180f, CameraOrbit.Turn(91.7f, +1), "крок від найближчої чверті, похибка не накопичується");

            Assert.AreEqual(355f, CameraOrbit.Approach(350f, 0f, 0.5f), 0.001f, "найкоротша дуга: 350 → 0 через 355, а не через 175");
            Assert.AreEqual(90f, CameraOrbit.Approach(89.995f, 90f, 0.1f), "біля цілі — рівно ціль");

            CameraOrbit.Rotate(1f, 0f, 90f, out float x, out float z);
            Assert.AreEqual(0f, x, 1e-5f);
            Assert.AreEqual(-1f, z, 1e-5f, "той самий знак, що Quaternion.Euler(0, 90, 0) у Unity");
            foreach (var a in new[] { 0f, 37f, 90f, 180f, 271f })
            {
                CameraOrbit.Rotate(3f, -5f, a, out x, out z);
                Assert.AreEqual(Math.Sqrt(34.0), Math.Sqrt(x * x + z * z), 1e-4, "відстань від героя не змінюється");
            }
        }

        // ---------------- спільний вхід (UI-14) ----------------

        /// <summary>
        /// Незворотна дія через <see cref="IUxInput"/> спершу чекає підтвердження
        /// (UX-12), скасування нічого не робить. Мутація: виконувати дію з
        /// <c>Confirm</c> одразу — тест падає.
        /// </summary>
        [Test]
        public void UxInput_IrreversibleActionWaitsForConfirm()
        {
            var h = Morning();
            h.SlotOccupied = true;
            IUxInput input = h.Core;
            input.TryOpen(UxPanelId.Save, null);
            var pending = input.Invoke("load:0");
            Assert.IsTrue(pending.AwaitingConfirm, "завантаження поверх гри — лише з підтвердженням");
            Assert.IsTrue(input.ConfirmPending);
            Assert.AreEqual(UxLayer.Confirm, input.Top);
            Assert.AreEqual(-1, h.Loaded, "до підтвердження нічого не завантажено");
            input.CancelConfirm();
            Assert.IsFalse(input.ConfirmPending);
            Assert.AreEqual(UxLayer.Panel, input.Top);
            Assert.AreEqual(-1, h.Loaded, "скасування нічого не робить");
            Assert.IsTrue(input.Invoke("load:0").AwaitingConfirm);
            Assert.IsTrue(input.Confirm().Ok);
            Assert.AreEqual(0, h.Loaded, "підтвердили — завантажено");
        }

        [Test]
        public void UxInput_DisabledActionIsRefusedWithAReason_NotConfirmed()
        {
            var h = Morning();
            IUxInput input = h.Core;
            input.TryOpen(UxPanelId.Veche, BuildingCatalog.VecheStation);
            h.Session.ConfirmMorning();
            var outcome = input.Invoke("raid");
            Assert.IsFalse(outcome.Ok);
            Assert.IsFalse(outcome.AwaitingConfirm);
            Assert.IsNotEmpty(outcome.Refusal, "відмова людською мовою (UI-04)");
        }
    }
}
