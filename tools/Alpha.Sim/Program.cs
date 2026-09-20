using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Checks;
using Game.Core.Loop;
using Game.Core.Settlement;
using Game.Core.Sim;
using Game.Core.Stats;
using Game.Core.World;

namespace Alpha.Sim
{
    /// <summary>
    /// Прогон кампаний и выгрузка трассы в CSV.
    ///
    /// Зачем: детерминизм ядра оплачен дорого, а главную его выгоду мы не
    /// использовали. Раз кампания воспроизводима до байта, темп можно измерить,
    /// а не обсуждать. Тесты темпа читают те же метрики через
    /// CampaignSimulator.Measure, поэтому CLI и CI смотрят на одни числа.
    ///
    /// Запуск:  dotnet run --project tools/Alpha.Sim -- --days 200 --out sim-out
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            int days = 200;
            string outDir = "sim-out";

            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--days") int.TryParse(args[i + 1], out days);
                if (args[i] == "--out") outDir = args[i + 1];
            }
            if (days < 1) days = 1;

            Directory.CreateDirectory(outDir);

            var policies = new[] { SimPolicy.Passive, SimPolicy.PatrolEveryNight, SimPolicy.AggressiveChoices, SimPolicy.Expedition };
            var tiers = new[] { 1, 2, 3, 4 };

            var summary = new List<CampaignMetrics>();

            foreach (var policy in policies)
                foreach (int tier in tiers)
                {
                    var processor = BuildProcessor(tier);
                    var trace = policy == SimPolicy.Expedition
                        ? RunExpedition(processor, days)
                        : CampaignSimulator.Run(processor, policy, days, Balance);
                    var metrics = CampaignSimulator.Measure(trace);
                    summary.Add(metrics);

                    string file = Path.Combine(outDir,
                        string.Format(CultureInfo.InvariantCulture, "trace-{0}-tier{1}.csv", policy, tier));
                    WriteCsv(file, trace);
                }

            string summaryPath = Path.Combine(outDir, "summary.csv");
            WriteSummary(summaryPath, summary);
            PrintSummary(summary, days, outDir);
            return 0;
        }

        private static BalanceConfig Balance
        {
            get { return new BalanceConfig(); }
        }

        // ---- сборка кампании ----

        private static readonly string[] Positions =
        {
            "storehouse_dock", "settlement_market", "settlement_farms",
            "infirmary_bed", "council_seat", "scouting_post", "workshop_bench"
        };

        private static DayProcessor BuildProcessor(int tier)
        {
            var cfg = Balance;
            var roster = new Roster();

            // Шесть напарников с разными профилями: специалист по каждому домену
            // плюс два середняка. Числа намеренно скромные — «хутор», а не элита.
            roster.Add(Make("guard", SkillType.Trade, 8, Positions[0]));
            roster.Add(Make("trader", SkillType.Trade, 7, Positions[1]));
            roster.Add(Make("farmer", SkillType.Survival, 6, Positions[2]));
            roster.Add(Make("medic", SkillType.Medicine, 7, Positions[3]));
            roster.Add(Make("elder", SkillType.Persuade, 6, Positions[4]));
            roster.Add(Make("scout", SkillType.Survival, 6, Positions[5]));

            var adapter = new RosterAdapter(roster);
            // Партия — те, кто держит склад, разведку и мастерскую. Когда они
            // уходят, эти три поста пустеют: вылазка обязана стоить городу.
            adapter.PartyForSim = new List<Companion> { roster.Get("guard"), roster.Get("scout"), roster.Get("elder") };
            var pulse = new WorldPulse(cfg.Pulse);
            foreach (var s in DefaultPressureSources.All()) pulse.AddSource(s);

            return new DayProcessor(new TensionStateFactory().Create(cfg), cfg, DayProcessor.DefaultSteps())
            {
                Tier = tier,
                Roster = adapter,
                Casualties = adapter,
                Population = new PopulationState(),
                Pulse = pulse,
                Incidents = DefaultIncidents.BuildTable(),
                Repeats = new RepeatTracker(),
                PostDomains = new[]
                {
                    new PostDomain(Positions[0], "склад", SkillKeys.Survival, 5),
                    new PostDomain(Positions[1], "рынок", SkillKeys.Trade, 5),
                    new PostDomain(Positions[3], "лазарет", SkillKeys.Medicine, 5)
                }
            };
        }

        // ---- вылазка: партия из трёх уходит, три поста пустеют ----

        /// <summary>Каждые сорок суток партия уходит на десять. Четверть кампании — без трёх рук.</summary>
        private static bool PartyIsAway(int day)
        {
            int inCycle = (day - 1) % 40;
            return inCycle >= 20 && inCycle < 30;
        }

        private static CampaignTrace RunExpedition(DayProcessor processor, int days)
        {
            var adapter = (RosterAdapter)processor.Roster;
            var party = adapter.PartyForSim;

            return CampaignSimulator.Run(processor, SimPolicy.Expedition, days, Balance, delegate (int day)
            {
                bool away = PartyIsAway(day);
                for (int i = 0; i < party.Count; i++)
                    if (!party[i].IsDead)
                        party[i].Status = away ? CompanionStatus.OnMission : CompanionStatus.Assigned;
                return away;
            });
        }

        private static Companion Make(string id, SkillType skill, int value, string position)
        {
            var arch = new CompanionArchetype(id, id);
            arch.SetSkill(skill, value);
            var c = arch.CreateInstance(id);
            c.AssignedSlotId = position;
            return c;
        }

        /// <summary>Мелкий помощник: TensionState требует секцию баланса, а не весь конфиг.</summary>
        private sealed class TensionStateFactory
        {
            public Game.Core.Pressure.TensionState Create(BalanceConfig cfg)
            {
                return new Game.Core.Pressure.TensionState(cfg.Tension);
            }
        }

        // ---- выгрузка ----

        private static void WriteCsv(string path, CampaignTrace trace)
        {
            var sb = new StringBuilder();

            sb.Append("policy,tier,day,phase,tension,band,daysInBand,population,")
              .Append("signals,deltas,bandSignal,incidents,crisis");
            foreach (var id in trace.TrackIds) sb.Append(",charge_").Append(id);
            foreach (var id in trace.TrackIds) sb.Append(",heard_").Append(id);
            sb.AppendLine(",topics,incidentIds,forewarnings,fired");

            foreach (var row in trace.Rows)
            {
                sb.Append(trace.Policy).Append(',')
                  .Append(trace.Tier).Append(',')
                  .Append(row.Day).Append(',')
                  .Append(row.Phase).Append(',')
                  .Append(row.TensionValue).Append(',')
                  .Append(row.Band).Append(',')
                  .Append(row.DaysInBand).Append(',')
                  .Append(row.Population).Append(',')
                  .Append(row.SignalCount).Append(',')
                  .Append(row.DeltaCount).Append(',')
                  .Append(row.HadBandSignal ? 1 : 0).Append(',')
                  .Append(row.IncidentCount).Append(',')
                  .Append(row.HadCrisis ? 1 : 0);

                foreach (var id in trace.TrackIds) sb.Append(',').Append(Get(row.Charges, id));
                foreach (var id in trace.TrackIds) sb.Append(',').Append(Get(row.Delivered, id));

                sb.Append(',').Append(Quote(row.Topics))
                  .Append(',').Append(Quote(row.Incidents))
                  .Append(',').Append(Quote(row.Forewarnings))
                  .Append(',').Append(Quote(row.FiredSources))
                  .AppendLine();
            }

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        private static int Get(Dictionary<string, int> map, string key)
        {
            int v;
            return map.TryGetValue(key, out v) ? v : 0;
        }

        private static string Quote(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        private static void WriteSummary(string path, List<CampaignMetrics> all)
        {
            var sb = new StringBuilder();
            sb.AppendLine("policy,tier,days,firstForewarnDay,firstIncidentDay,firstDeltaDay," +
                          "firstBandChangeDay,firstCrisisDay,incidents,crises,bandChanges," +
                          "bandChangesWithoutSignal,longestNoDeltaPhases,longestNoIncidentPhases," +
                          "signalsPerPhase,distinctTopics,maxTopicRepeats,mostRepeatedTopic," +
                          "fullLadders,skippedLadderSteps,finalBand,finalPopulation," +
                          "outWorst,outBase,outGood,outBest," +
                          "tenTierTick,tenThreat,tenEvent,tenQuest,tenBlood");

            foreach (var m in all)
            {
                sb.Append(m.Policy).Append(',').Append(m.Tier).Append(',').Append(m.Days).Append(',')
                  .Append(m.FirstForewarnDay).Append(',').Append(m.FirstIncidentDay).Append(',')
                  .Append(m.FirstDeltaDay).Append(',').Append(m.FirstBandChangeDay).Append(',')
                  .Append(m.FirstCrisisDay).Append(',').Append(m.IncidentTotal).Append(',')
                  .Append(m.CrisisTotal).Append(',').Append(m.BandChangesTotal).Append(',')
                  .Append(m.BandChangesWithoutSignal).Append(',')
                  .Append(m.LongestStreakWithoutDelta).Append(',')
                  .Append(m.LongestStreakWithoutIncident).Append(',')
                  .Append(m.SignalsPerPhase.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
                  .Append(m.DistinctTopics).Append(',').Append(m.MaxTopicRepeats).Append(',')
                  .Append(m.MostRepeatedTopic).Append(',')
                  .Append(m.FullLadders).Append(',').Append(m.SkippedLadderSteps).Append(',')
                  .Append(m.FinalBand).Append(',').Append(m.FinalPopulation)
                  .Append(',').Append(m.OutcomeCounts[0]).Append(',').Append(m.OutcomeCounts[1])
                  .Append(',').Append(m.OutcomeCounts[2]).Append(',').Append(m.OutcomeCounts[3]);

                foreach (var driver in new[] { "CityTierTick", "ThreatOutcome", "EventOutcome", "QuestChoice", "PlaystyleBlood" })
                {
                    int v;
                    m.TensionByDriver.TryGetValue(driver, out v);
                    sb.Append(',').Append(v);
                }
                sb.AppendLine();
            }

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        private static void PrintSummary(List<CampaignMetrics> all, int days, string outDir)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine();
            Console.WriteLine("Кампаний: {0} по {1} суток. Трассы: {2}", all.Count, days, outDir);
            Console.WriteLine();
            Console.WriteLine("{0,-18} {1,4} {2,7} {3,7} {4,7} {5,7} {6,6} {7,7} {8,8} {9,8}",
                "политика", "тир", "1-й пре", "1-й инц", "1-я дел", "1-я кри", "инц", "лестниц", "тишина", "повтор");
            Console.WriteLine(new string('-', 96));

            foreach (var m in all)
            {
                Console.WriteLine("{0,-18} {1,4} {2,7} {3,7} {4,7} {5,7} {6,6} {7,7} {8,8} {9,8}",
                    m.Policy, m.Tier,
                    Show(m.FirstForewarnDay), Show(m.FirstIncidentDay),
                    Show(m.FirstDeltaDay), Show(m.FirstCrisisDay),
                    m.IncidentTotal, m.FullLadders,
                    m.LongestStreakWithoutDelta, m.MaxTopicRepeats);
            }

            Console.WriteLine();
            Console.WriteLine("СТЫК ДВУХ ЛУПОВ (только политика Expedition): партия дома против партии в вылазке");
            Console.WriteLine("{0,4} | {1,6} {2,6} | {3,7} {4,7} | {5,8} {6,8} | {7,9} {8,9}",
                "тир", "фаз д", "фаз в", "Worst д", "Worst в", "давл д", "давл в", "W/фаза д", "W/фаза в");
            foreach (var m in all)
            {
                if (m.Policy != SimPolicy.Expedition) continue;
                double wh = m.PhasesHome > 0 ? (double)m.OutcomesHome[0] / m.PhasesHome : 0;
                double wa = m.PhasesAway > 0 ? (double)m.OutcomesAway[0] / m.PhasesAway : 0;
                Console.WriteLine("{0,4} | {1,6} {2,6} | {3,7} {4,7} | {5,8} {6,8} | {7,9:0.000} {8,9:0.000}",
                    m.Tier, m.PhasesHome, m.PhasesAway, m.OutcomesHome[0], m.OutcomesAway[0],
                    m.TensionGainHome, m.TensionGainAway, wh, wa);
            }
            Console.WriteLine();
            Console.WriteLine("Столбцы: сутки первого предвестника / инцидента / дельта-сигнала / кризиса;");
            Console.WriteLine("всего инцидентов; полных лестниц 1-2-3; самая длинная тишина без дельты (в фазах);");
            Console.WriteLine("сколько раз повторился самый частый ключ реплики.");
        }

        private static string Show(int day)
        {
            return day < 0 ? "-" : day.ToString(CultureInfo.InvariantCulture);
        }
    }
}
