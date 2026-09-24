using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Game.Core.Combat;
using Game.Core.Session;
using Game.Core.Session.Bots;
using Game.Gameplay.Combat;

namespace Alpha.Sim
{
    /// <summary>
    /// Харнес темпу (пакет D2, §5: "tools/Alpha.Sim to use FirstHourWorld/
    /// GameSession bots"). Раніше гонив кампанії напряму через DayProcessor/
    /// SettlementCycle (Game.Core.Sim.CampaignSimulator) — окремий, менш
    /// повний шлях, що обходив GameSession і читав internal-стан. Тепер той
    /// самий <see cref="BotRunner"/> (Core/Session/Bots), що й
    /// AllMechanicsCoverageTests/Alpha.Play — гонить усі 5 політик §4.10 через
    /// ПУБЛІЧНИЙ фасад GameSession і пише трасу з <see cref="GameSession.DayLog"/>
    /// (§4.3), а не з внутрішнього стану: харнес бачить рівно те саме, що й гра.
    ///
    /// Game.Core.Sim.CampaignSimulator (тір/політика-розгортка для калібровки
    /// балансу, CampaignPacingTests) лишається окремим — цей інструмент його
    /// НЕ замінює і не чіпає, лише більше сам його не викликає.
    ///
    /// Запуск:  dotnet run --project tools/Alpha.Sim -- --days 90 --out sim-out
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            int days = 90;
            string outDir = "sim-out";
            ulong seed = 1;

            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--days") int.TryParse(args[i + 1], out days);
                if (args[i] == "--out") outDir = args[i + 1];
                if (args[i] == "--seed") ulong.TryParse(args[i + 1], out seed);
            }
            if (days < 1) days = 1;

            Directory.CreateDirectory(outDir);

            var policies = FivePolicies();
            var summaries = new List<PolicySummary>();

            foreach (var policy in policies)
            {
                var roller = new SeededDiceRoller(seed);
                var options = new NewGameOptions { SkipCreation = false, HitRule = HitRuleKind.Threshold, Seed = seed, Roller = roller };
                var log = new List<GameEvent>();

                var session = BotRunner.PlayDays(policy, days, options, log);

                string tracePath = Path.Combine(outDir, "trace-" + policy.Name + ".csv");
                WriteTrace(tracePath, policy.Name, log);

                summaries.Add(Summarize(policy.Name, session, log, days));
            }

            WriteSummary(Path.Combine(outDir, "summary.csv"), summaries);
            PrintSummary(summaries, days, outDir);
            return 0;
        }

        private static IBotPolicy[] FivePolicies()
        {
            return new IBotPolicy[]
            {
                new StewardPolicy(), new PacifistPolicy(), new BloodyPolicy(),
                new PatrolAlwaysPolicy(), new DelveGreedyPolicy()
            };
        }

        // ---- трасa: рядок на подію DayLog (§4.3) ----

        private static void WriteTrace(string path, string policyName, List<GameEvent> log)
        {
            var sb = new StringBuilder();
            sb.AppendLine("policy,day,phase,key,args");
            foreach (var e in log)
            {
                sb.Append(policyName).Append(',')
                  .Append(e.Day).Append(',')
                  .Append(e.Phase).Append(',')
                  .Append(Quote(e.Key)).Append(',')
                  .Append(Quote(ArgsToString(e.Args)))
                  .AppendLine();
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        private static string ArgsToString(IReadOnlyDictionary<string, string> args)
        {
            if (args == null || args.Count == 0) return string.Empty;
            var sb = new StringBuilder();
            bool first = true;
            foreach (var kv in args)
            {
                if (!first) sb.Append(';');
                sb.Append(kv.Key).Append('=').Append(kv.Value);
                first = false;
            }
            return sb.ToString();
        }

        private static string Quote(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        // ---- підсумок за політику ----

        private sealed class PolicySummary
        {
            public string Policy;
            public int Days;
            public int TotalEvents;
            public int Decisions, BloodyDecisions, Battles, Crises;
            public string FinalTensionBand, FinalCrowdBand, FinaleOutcomeKey;
            public int FinalGold, FinalMaterials, FinalFood, FinalTier;
        }

        private static PolicySummary Summarize(string policyName, GameSession session, List<GameEvent> log, int days)
        {
            var s = new PolicySummary { Policy = policyName, Days = days, TotalEvents = log.Count };
            foreach (var e in log)
            {
                if (e.Key == "decision.resolved")
                {
                    s.Decisions++;
                    if (e.Args != null && e.Args.TryGetValue("path", out var p) && p == "Bloody") s.BloodyDecisions++;
                }
                if (e.Key == "combat.battle.started") s.Battles++;
                if (e.Key == "crisis.test.warn") s.Crises++;
            }

            var view = session.CurrentView;
            s.FinalTensionBand = view.TensionBand;
            s.FinalCrowdBand = view.CrowdBand;
            s.FinalTier = view.Tier;

            var econ = session.GetEconomyView();
            s.FinalGold = econ.Gold;
            s.FinalMaterials = econ.Materials;
            s.FinalFood = econ.Food;

            var summary = session.GetSummaryView();
            s.FinaleOutcomeKey = summary?.FinaleOutcomeKey;

            return s;
        }

        private static void WriteSummary(string path, List<PolicySummary> all)
        {
            var sb = new StringBuilder();
            sb.AppendLine("policy,days,totalEvents,decisions,bloodyDecisions,battles,crises,finalTensionBand,finalCrowdBand,finalTier,finalGold,finalMaterials,finalFood,finaleOutcomeKey");
            foreach (var m in all)
            {
                sb.Append(m.Policy).Append(',').Append(m.Days).Append(',').Append(m.TotalEvents).Append(',')
                  .Append(m.Decisions).Append(',').Append(m.BloodyDecisions).Append(',').Append(m.Battles).Append(',')
                  .Append(m.Crises).Append(',').Append(m.FinalTensionBand).Append(',').Append(m.FinalCrowdBand).Append(',')
                  .Append(m.FinalTier).Append(',').Append(m.FinalGold).Append(',').Append(m.FinalMaterials).Append(',')
                  .Append(m.FinalFood).Append(',').Append(m.FinaleOutcomeKey)
                  .AppendLine();
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        private static void PrintSummary(List<PolicySummary> all, int days, string outDir)
        {
            Console.WriteLine();
            Console.WriteLine("Прогонів: {0} по {1} діб (GameSession/BotRunner, §4.10). Траси: {2}", all.Count, days, outDir);
            Console.WriteLine();
            Console.WriteLine("{0,-14} {1,6} {2,10} {3,7} {4,8} {5,6} {6,9} {7,7}",
                "політика", "подій", "рішень", "кров'ю", "боїв", "криз", "напруга", "тир");
            Console.WriteLine(new string('-', 76));
            foreach (var m in all)
            {
                Console.WriteLine("{0,-14} {1,6} {2,10} {3,7} {4,8} {5,6} {6,9} {7,7}",
                    m.Policy, m.TotalEvents, m.Decisions, m.BloodyDecisions, m.Battles, m.Crises, m.FinalTensionBand, m.FinalTier);
            }
        }
    }
}
