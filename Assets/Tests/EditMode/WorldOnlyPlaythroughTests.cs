using System;
using System.Collections.Generic;
using System.Linq;
using Game.Core.Combat;
using Game.Core.Loop;
using Game.Core.Session;
using Game.Gameplay.UI;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Капстоун Поправки №18 («усе можна було обрати через світ гри, а не в
    /// меню»): бот проходить тестову партію до підсумку, і кожна дія ранку —
    /// дія картки місця у світі (<see cref="UxWorldReach"/>) через той самий
    /// вхід, що й людина (<see cref="IUxInput"/>). Прямо в ядро бот може
    /// кликати лише те, що ще не у світі (<see cref="UxHomeKind.Gap"/>) або є
    /// подією (сцена, бій). Коли крок плану переносить команду у світ, прямий
    /// виклик стає порушенням — бот мусить піти через місце. Заодно доводить
    /// «з кожного стану є дія вперед»: застрягання — червоний тест.
    /// </summary>
    public class WorldOnlyPlaythroughTests
    {
        private const int StepLimit = 600;

        private sealed class Run
        {
            public readonly WorldFirstTests.Host Host;
            public readonly SortedSet<string> Directs = new SortedSet<string>(StringComparer.Ordinal);
            public readonly List<string> WorldActions = new List<string>();
            public readonly SortedSet<string> WorldCommands = new SortedSet<string>(StringComparer.Ordinal);

            /// <summary>Зібрати загін на Заставі (перемикачі «до загону» — дії без команди ядра) і вийти тим самим входом.</summary>
            public void Muster()
            {
                Host.Core.Open(UxPanelId.Muster, Game.Gameplay.Walk.BuildingCatalog.MusterStation);
                var pick = Host.Core.CurrentModel().Cards.SelectMany(c => c.Actions)
                    .FirstOrDefault(a => a.Id.StartsWith("party:") && a.ReasonIn(S.State, false) == null && !a.Selected);
                if (pick != null) Host.Core.Invoke(pick.Id);
                Host.Core.ClosePanel();
                if (TryWorld(nameof(GameSession.PreviewExpedition))) TryWorld(nameof(GameSession.DepartExpedition));
            }

            public Run(WorldFirstTests.Host host) { Host = host; }

            public GameSession S => Host.Session;

            /// <summary>Прямий виклик ядра — лише для того, що не має дому у світі чи на кнопці фази.</summary>
            public void Direct(string command, Action call)
            {
                var homes = UxWorldHomes.Of(command);
                Assert.IsNotEmpty(homes, command + ": команди немає в таблиці домівок");
                Assert.IsFalse(homes.Any(h => h.InWorld || h.Kind == UxHomeKind.Hud),
                    command + ": у команди є дім у світі — бот мусить іти через місце, а не в ядро");
                Directs.Add(command);
                call();
            }

            /// <summary>Знайти в досяжних зі світу місцях дію з командою й виконати через <see cref="IUxInput"/>.</summary>
            public bool TryWorld(string command, Func<UxAction, bool> pick = null)
            {
                foreach (var entry in UxWorldReach.Now(S))
                {
                    Host.Core.Open(entry.Panel, entry.Context);
                    var model = Host.Core.CurrentModel();
                    if (model == null) continue;
                    var action = model.Cards.SelectMany(c => c.Actions)
                        .FirstOrDefault(a => a.Command == command && a.ReasonIn(S.State, false) == null && (pick == null || pick(a)));
                    if (action == null) continue;
                    var outcome = Host.Core.Invoke(action.Id);
                    if (outcome.AwaitingConfirm) outcome = Host.Core.Confirm();
                    Host.Core.ClosePanel();
                    if (!outcome.Ok) continue;
                    WorldActions.Add(entry.PlaceId + " → " + action.Id);
                    WorldCommands.Add(command);
                    return true;
                }
                Host.Core.ClosePanel();
                return false;
            }
        }

        [Test]
        public void Bot_ReachesTheSummary_MorningsOnlyThroughTheWorld()
        {
            var run = new Run(WorldFirstTests.Morning());
            var s = run.S;
            bool finaleDone = false;
            int lastMorning = 0;

            for (int step = 0; step < StepLimit && s.State != SessionState.Summary; step++)
            {
                switch (s.State)
                {
                    case SessionState.Opening:
                    case SessionState.Scene:
                    {
                        var frame = default(Game.Core.Session.Views.SceneStepView);
                        run.Direct(nameof(GameSession.AdvanceScene), () => frame = s.AdvanceScene());
                        if (frame != null && frame.IsChoice)
                            run.Direct(nameof(GameSession.ChooseSceneOption), () => s.ChooseSceneOption(0));
                        break;
                    }

                    case SessionState.Morning:
                    case SessionState.FreePlay:
                    {
                        int day = s.CurrentView.Day;
                        if (day != lastMorning)
                        {
                            lastMorning = day;
                            // Розставити вільних на відкриті порожні пости — на станціях у світі.
                            for (int i = 0; i < 8 && run.TryWorld(nameof(GameSession.Assign)); i++) { }
                            // Першого ранку — замовити будівлю з ділянки; другого — облава на Вічі;
                            // третього — збори й вилазка із Застави. Усе тим самим входом, що й людина.
                            if (day == 1) run.TryWorld(nameof(GameSession.OrderBuilding));
                            if (day == 2) run.TryWorld(nameof(GameSession.OrderRaid));
                            if (day == 3) run.Muster();
                        }
                        Assert.IsTrue(UxWorldHomes.Of(nameof(GameSession.ConfirmMorning)).Any(h => h.Kind == UxHomeKind.Hud));
                        UxPhaseButton.StartDay(s);
                        break;
                    }

                    case SessionState.Day:
                        UxPhaseButton.StartDay(s);
                        break;

                    case SessionState.Decision:
                        run.Direct(nameof(GameSession.ResolveIncident), () => s.ResolveIncident(IncidentPath.Quiet));
                        break;

                    case SessionState.Evening:
                        run.Direct(nameof(GameSession.ConfirmEvening), () => s.ConfirmEvening());
                        break;

                    case SessionState.Night:
                        if (s.CurrentView.Day == 5 && !finaleDone)
                        {
                            finaleDone = true;
                            run.Direct(nameof(GameSession.ResolveFinale), () => s.ResolveFinale(IncidentPath.Quiet));
                        }
                        else run.Direct(nameof(GameSession.AdvanceNight), () => s.AdvanceNight());
                        break;

                    case SessionState.Battle:
                        run.Direct(nameof(GameSession.CombatAutoResolve), () => s.CombatAutoResolve());
                        break;

                    default:
                        Assert.Fail("бот не знає, що робити у стані " + s.State + " (доба " + s.CurrentView.Day + ")");
                        break;
                }
            }

            Assert.AreEqual(SessionState.Summary, s.State, "бот не дійшов до підсумку за " + StepLimit + " кроків");
            Assert.IsNotEmpty(run.WorldActions, "ранки мали пройти діями місць у світі");
            Assert.GreaterOrEqual(run.WorldCommands.Count, 3,
                "через світ виконано щонайменше три різні команди: " + string.Join(", ", run.WorldCommands));
            TestContext.WriteLine("Дії у світі: " + string.Join("; ", run.WorldActions));
            TestContext.WriteLine("Прямо в ядро (події й дірки): " + string.Join(", ", run.Directs));

            // Що бот кликав прямо, — лише події й дірки; дірки — з храповика.
            foreach (var cmd in run.Directs)
                Assert.IsTrue(UxWorldHomes.Of(cmd).All(h => h.Kind == UxHomeKind.Gap || h.Kind == UxHomeKind.Event || h.Kind == UxHomeKind.Auto), cmd);
        }
    }
}
