using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Alpha.Shared;
using Game.Core.Combat;
using Game.Core.Loop;
using Game.Core.Session;
using Game.Core.Session.Bots;
using Game.Core.Session.Views;
using Game.Gameplay.Combat;

namespace Alpha.Play
{
    /// <summary>
    /// Текстова збірка першого часу (docs/TEST_BUILD.md, пакет D2) — тепер
    /// цілком на <see cref="GameSession"/>, як і вимагає §5 (D2, "Alpha.Play/
    /// Program.cs"): жодного прямого звернення до DayProcessor/SettlementCycle,
    /// той самий фасад, що й Unity/тести. <see cref="BotRunner"/> (Core/Session/
    /// Bots, §1.1 "боти в ядрі") веде сесію однаково для --auto (готова
    /// політика) і для ручної гри (<see cref="ConsolePolicy"/> — теж
    /// IBotPolicy, лише питає стрічку замість власного "характеру").
    ///
    /// Бої консоль грає ЛИШЕ «Автобоєм» (§1.1: "Консоль грає бої лише
    /// Автобоєм — гравець обирає шлях, а не ходи") — тактичні ходи лишаються
    /// Unity й тренувальному бою.
    ///
    /// Запуск:
    ///   dotnet run --project tools/Alpha.Play -- --auto
    ///   dotnet run --project tools/Alpha.Play
    ///   dotnet run --project tools/Alpha.Play -- --auto --path bloody --days 15
    /// </summary>
    internal static class Program
    {
        private const int SliceDays = 5;

        private static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            bool auto = Has(args, "--auto");
            string pathArg = Value(args, "--path", "quiet");
            int days = int.TryParse(Value(args, "--days", SliceDays.ToString()), out var parsedDays) ? parsedDays : SliceDays;
            string savePath = Value(args, "--save", null);
            string loadPath = Value(args, "--load", null);
            ulong seed = ulong.TryParse(Value(args, "--seed", "1"), out var parsedSeed) ? parsedSeed : 1UL;

            var roller = new SeededDiceRoller(seed);
            var session = new GameSession(roller);

            IBotPolicy policy = !auto
                ? new ConsolePolicy()
                : (pathArg == "bloody" ? (IBotPolicy)new BloodyPolicy() : new PacifistPolicy());

            if (loadPath != null && File.Exists(loadPath))
            {
                session.NewGame(new NewGameOptions { SkipCreation = true, HitRule = HitRuleKind.Threshold, Seed = seed, Roller = roller });
                session.RestoreFromBlob(File.ReadAllText(loadPath));
                Console.WriteLine("— завантажено: " + loadPath + ", доба " + session.CurrentView.Day);
            }
            else
            {
                session.NewGame(new NewGameOptions { SkipCreation = false, HitRule = HitRuleKind.Threshold, Seed = seed, Roller = roller });
            }

            var cast = Cast();
            BotRunner.Drive(session, policy, days, null, e => PrintEvent(e, session), null, null, null, null,
                step => PrintSceneStep(step, cast));

            Console.WriteLine();
            Console.WriteLine("=== Стан: " + session.State + ", доба " + session.CurrentView.Day + " ===");
            if (session.State == SessionState.FreePlay)
                PrintSummary(session);

            if (savePath != null)
            {
                if (session.State == SessionState.Morning || session.State == SessionState.FreePlay)
                {
                    File.WriteAllText(savePath, session.SaveState(0));
                    Console.WriteLine("— збережено: " + savePath);
                }
                else
                {
                    Console.WriteLine("— збереження недоступне поза Morning/FreePlay (R13), поточний стан: " + session.State);
                }
            }

            return 0;
        }

        /// <summary>Карточки всіх, хто може з'явитись у сценах зрізу (для друку імені за ActorId).</summary>
        private static List<Game.Core.Characters.CharacterCard> Cast()
        {
            var cast = Game.Core.Characters.OpeningCast.All();
            cast.Add(Game.Core.Scenes.OpeningScenes.Protagonist());
            return cast;
        }

        private static string Who(IEnumerable<Game.Core.Characters.CharacterCard> cast, string id)
        {
            if (string.IsNullOrEmpty(id)) return "порожньо";
            foreach (var card in cast)
                if (card != null && card.Id == id) return card.DisplayName;
            return id == GameSession.ProtagonistId ? "ти" : id;
        }

        // ---- друк портретної сцени (SceneStepView, окремий гачок BotRunner.Drive) ----

        private static void PrintSceneStep(SceneStepView step, List<Game.Core.Characters.CharacterCard> cast)
        {
            if (step == null) return;

            if (!string.IsNullOrEmpty(step.LineKey))
                Console.WriteLine("    " + Who(cast, step.SpeakerId ?? step.ActorId) + ": " + SceneText.Line(step.LineKey));
            if (!string.IsNullOrEmpty(step.EffectKey))
                Console.WriteLine("    ( " + SceneText.Line(step.EffectKey) + " )");
            if (step.IsFinished)
                Console.WriteLine("  ── кінець сцени ──");
        }

        // ---- друк стрічки подій (§4.3 GameSession.DayLog) через наявні текстові таблиці ----

        private static void PrintEvent(GameEvent e, GameSession session)
        {
            if (e == null || string.IsNullOrEmpty(e.Key)) return;

            switch (e.Key)
            {
                case "day.advanced":
                    Console.WriteLine();
                    Console.WriteLine("=== Доба " + e.Args.GetValueOrDefault("day", "?") + ", " +
                        (e.Phase == DayPhase.Night ? "ніч" : "день") + ". Стан: " + session.CurrentView.TensionBand + " ===");
                    return;

                case "scene.finished":
                    return; // саму сцену друкує PrintSceneStep (onSceneStep) покроково

                default:
                    break;
            }

            if (e.Key.StartsWith("forewarn.level", StringComparison.Ordinal) ||
                e.Key.StartsWith("night.ambient.", StringComparison.Ordinal) ||
                e.Key.StartsWith("tension.ambient.", StringComparison.Ordinal) ||
                e.Key.StartsWith("post.", StringComparison.Ordinal))
            {
                Console.WriteLine("    " + SignalText.TextForTopic(e.Key));
                return;
            }

            if (e.Key == "decision.resolved" || e.Key == "finale.resolved")
            {
                string mark = e.Args.GetValueOrDefault("noCandidate", "0") == "1" ? "!!" : "  ";
                Console.WriteLine("  " + mark + " " + (e.Args.GetValueOrDefault("incidentId", e.Key)) +
                    ": " + e.Args.GetValueOrDefault("band", "?"));
                return;
            }

            // Решта ключів (assign.*, council.*, combat.*, dungeon.*, quest.*, ...) —
            // сама конвенція <домен>.<дія> вже читається; E3 замінить на справжній
            // текст (§6.2), тут — чесний ключ + аргументи, як і документує §4.3.
            var argsText = new StringBuilder();
            foreach (var kv in e.Args)
                argsText.Append(' ').Append(kv.Key).Append('=').Append(kv.Value);
            Console.WriteLine("    [" + e.Key + "]" + argsText);
        }

        private static void PrintSummary(GameSession session)
        {
            var summary = session.GetSummaryView();
            Console.WriteLine();
            Console.WriteLine("=== Підсумок ===");
            Console.WriteLine("  фінал: " + (summary.FinaleOutcomeKey ?? "—"));
            Console.WriteLine("  гаманець: золото " + summary.Wallet.Gold + ", матеріали " + summary.Wallet.Materials + ", їжа " + summary.Wallet.Food);
            Console.WriteLine("  збудовано: " + string.Join(", ", summary.BuiltBuildings));
            foreach (var c in summary.FinalRoster)
                Console.WriteLine("  " + c.DisplayName + " (" + c.Id + "): " + c.Status +
                    (c.Loyalty.HasValue ? ", лояльність " + c.Loyalty.Value : "") + ", рівень " + c.Level);
            foreach (var f in summary.Factions)
                Console.WriteLine("  фракція " + f.DisplayName + ": " + f.Band);
        }

        private static bool Has(string[] args, string flag)
        {
            for (int i = 0; i < args.Length; i++)
                if (args[i] == flag) return true;
            return false;
        }

        private static string Value(string[] args, string key, string fallback)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == key) return args[i + 1];
            return fallback;
        }
    }

    internal static class DictionaryExtensions
    {
        public static string GetValueOrDefault(this IReadOnlyDictionary<string, string> dict, string key, string fallback)
            => dict != null && dict.TryGetValue(key, out var v) ? v : fallback;
    }
}
