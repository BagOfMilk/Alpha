using System.Collections.Generic;
using System.Text;
using Game.Core.Balance;
using Game.Core.Loop;
using Game.Core.Pressure;

namespace Game.Core.Sim
{
    /// <summary>
    /// Гоняет кампанию под заданной политикой и пишет трассу.
    ///
    /// Зачем это вообще: детерминизм ядра оплачен дорого (Поправка №3.3), а
    /// главную его выгоду мы не использовали. Раз кампания воспроизводима до
    /// байта, темп можно ИЗМЕРИТЬ, а не обсуждать: прогнать сто кампаний за
    /// секунду и увидеть, на какие сутки приходит первый инцидент, сколько
    /// суток подряд игроку не показывают ничего нового и сколько раз повторится
    /// один и тот же ключ реплики.
    ///
    /// Симулятор не строит ростер и не знает про StatType: он получает готовый
    /// конвейер. Так этот файл остаётся в тех же рамках, что и весь городской
    /// слой (архитектурный тест это проверяет).
    /// </summary>
    internal static class CampaignSimulator
    {
        /// <summary>Раз в столько суток агрессивная политика делает крупный выбор.</summary>
        private const int ChoiceEveryDays = 10;

        /// <summary>
        /// Колбэк перед каждыми сутками: вызывающий двигает ростер (кто ушёл,
        /// кто вернулся) и отвечает, вне города ли сейчас партия. Симулятор
        /// остаётся развязанным от модели персонажа.
        /// </summary>
        internal delegate bool BeforeDay(int day);

        internal static CampaignTrace Run(DayProcessor processor, SimPolicy policy, int days, BalanceConfig balance)
        {
            return Run(processor, policy, days, balance, null);
        }

        internal static CampaignTrace Run(DayProcessor processor, SimPolicy policy, int days,
            BalanceConfig balance, BeforeDay beforeDay)
        {
            var trace = new CampaignTrace { Policy = policy, Tier = processor.Tier };
            var lastFired = new Dictionary<string, int>();

            processor.IsPatrolling = policy == SimPolicy.PatrolEveryNight;

            if (processor.Pulse != null)
                foreach (var pair in processor.Pulse.Tracks)
                    trace.TrackIds.Add(pair.Key);
            trace.TrackIds.Sort();

            for (int day = 1; day <= days; day++)
            {
                bool away = beforeDay != null && beforeDay(day);

                if (policy == SimPolicy.AggressiveChoices && day % ChoiceEveryDays == 0)
                {
                    TensionDrivers.QuestChoice(processor.Tension,
                        TensionDrivers.ChoiceWeight.Major, "quest:" + day, balance);
                }

                // Снимок берётся ПОСЛЕ КАЖДОЙ фазы отдельно. AdvanceFullDay
                // вернул бы оба отчёта разом, и тогда дневная строка показывала
                // бы ночные заряды — трасса врала бы ровно в том столбце, ради
                // которого её и заводили.
                var dayReport = processor.Advance(DayPhase.Day);
                var dayRow = Capture(processor, dayReport, trace.TrackIds, lastFired);
                dayRow.PartyAway = away;
                trace.Rows.Add(dayRow);

                var nightReport = processor.Advance(DayPhase.Night);
                var nightRow = Capture(processor, nightReport, trace.TrackIds, lastFired);
                nightRow.PartyAway = away;
                trace.Rows.Add(nightRow);
            }

            return trace;
        }

        private static DayRow Capture(DayProcessor p, DayReport report, List<string> trackIds,
            Dictionary<string, int> lastFired)
        {
            var row = new DayRow
            {
                Day = report.Day,
                Phase = report.Phase,
                TensionValue = p.Tension.Value,
                Band = (int)p.Tension.Band,
                DaysInBand = p.Tension.DaysInCurrentBand,
                Population = p.Population != null ? p.Population.Count : 0
            };

            var topics = new StringBuilder();
            var channels = new StringBuilder();
            if (report.Signals != null && report.Signals.Requests != null)
            {
                var requests = report.Signals.Requests;
                row.SignalCount = requests.Count;
                for (int i = 0; i < requests.Count; i++)
                {
                    var r = requests[i];
                    if (r.IsDelta) row.DeltaCount++;
                    if (r.TopicId != null && r.TopicId.StartsWith("tension.band.")) row.HadBandSignal = true;

                    if (topics.Length > 0) topics.Append(';');
                    topics.Append(r.TopicId);
                    if (channels.Length > 0) channels.Append(';');
                    channels.Append(r.Channel);
                }
            }
            row.Topics = topics.ToString();
            row.Channels = channels.ToString();

            var incidents = new StringBuilder();
            if (report.Incidents != null)
            {
                row.IncidentCount = report.Incidents.Count;
                for (int i = 0; i < report.Incidents.Count; i++)
                {
                    var o = report.Incidents[i];
                    if (o.WasCrisis) row.HadCrisis = true;
                    if (incidents.Length > 0) incidents.Append(';');
                    incidents.Append(o.IncidentId);
                }
            }
            row.Incidents = incidents.ToString();

            var bands = new StringBuilder();
            if (report.Incidents != null)
            {
                for (int i = 0; i < report.Incidents.Count; i++)
                {
                    if (bands.Length > 0) bands.Append(';');
                    bands.Append((int)report.Incidents[i].Band);
                }
            }
            row.OutcomeBands = bands.ToString();

            if (report.TensionChanges != null)
            {
                for (int i = 0; i < report.TensionChanges.Count; i++)
                {
                    var change = report.TensionChanges[i];
                    if (change.Rejected || change.Applied == 0) continue;

                    string key = change.Driver.ToString();
                    int had;
                    row.TensionByDriver.TryGetValue(key, out had);
                    row.TensionByDriver[key] = had + change.Applied;
                }
            }

            var fore = new StringBuilder();
            if (report.Forewarnings != null)
            {
                for (int i = 0; i < report.Forewarnings.Count; i++)
                {
                    var f = report.Forewarnings[i];
                    if (fore.Length > 0) fore.Append(';');
                    fore.Append(f.SourceId).Append(':').Append(f.Level);
                }
            }
            row.Forewarnings = fore.ToString();

            if (p.Pulse != null)
            {
                var fired = new StringBuilder();
                for (int i = 0; i < trackIds.Count; i++)
                    PressureTrackSnapshot(p, trackIds[i], row, lastFired, fired);
                row.FiredSources = fired.ToString();
            }

            return row;
        }

        /// <summary>
        /// Удар фиксируется точно, а не угадывается по падению заряда: у трека
        /// есть дата последнего срабатывания, и достаточно заметить её смену.
        /// </summary>
        private static void PressureTrackSnapshot(DayProcessor p, string id, DayRow row,
            Dictionary<string, int> lastFired, StringBuilder fired)
        {
            World.PressureTrack track;
            if (!p.Pulse.Tracks.TryGetValue(id, out track)) return;

            row.Charges[id] = track.Charge;
            row.Delivered[id] = track.DeliveredLevel;

            int seen;
            bool known = lastFired.TryGetValue(id, out seen);
            if (track.LastFiredDay != 0 && (!known || track.LastFiredDay != seen))
            {
                if (fired.Length > 0) fired.Append(';');
                fired.Append(id);
            }
            lastFired[id] = track.LastFiredDay;
        }

        /// <summary>Свести трассу к числам, о которых можно писать тесты.</summary>
        internal static CampaignMetrics Measure(CampaignTrace trace)
        {
            var m = new CampaignMetrics
            {
                Policy = trace.Policy,
                Tier = trace.Tier,
                Phases = trace.Rows.Count
            };
            if (trace.Rows.Count == 0) return m;

            m.Days = trace.Rows[trace.Rows.Count - 1].Day;
            m.FinalBand = trace.Rows[trace.Rows.Count - 1].Band;
            m.FinalPopulation = trace.Rows[trace.Rows.Count - 1].Population;

            var topicCounts = new Dictionary<string, int>();
            var prevDelivered = new Dictionary<string, int>();

            int prevBand = trace.Rows[0].Band;
            int signals = 0;
            int streakNoDelta = 0, streakNoIncident = 0;

            for (int i = 0; i < trace.Rows.Count; i++)
            {
                var row = trace.Rows[i];
                signals += row.SignalCount;

                if (row.IncidentCount > 0)
                {
                    m.IncidentTotal += row.IncidentCount;
                    if (m.FirstIncidentDay < 0) m.FirstIncidentDay = row.Day;
                    streakNoIncident = 0;
                }
                else
                {
                    streakNoIncident++;
                    if (streakNoIncident > m.LongestStreakWithoutIncident)
                        m.LongestStreakWithoutIncident = streakNoIncident;
                }

                if (row.HadCrisis)
                {
                    m.CrisisTotal++;
                    if (m.FirstCrisisDay < 0) m.FirstCrisisDay = row.Day;
                }

                if (row.DeltaCount > 0)
                {
                    if (m.FirstDeltaDay < 0) m.FirstDeltaDay = row.Day;
                    streakNoDelta = 0;
                }
                else
                {
                    streakNoDelta++;
                    if (streakNoDelta > m.LongestStreakWithoutDelta)
                        m.LongestStreakWithoutDelta = streakNoDelta;
                }

                if (row.Band != prevBand)
                {
                    m.BandChangesTotal++;
                    if (m.FirstBandChangeDay < 0) m.FirstBandChangeDay = row.Day;
                    // Инвариант 4: смена полосы обязана породить наблюдаемый сигнал.
                    if (!row.HadBandSignal) m.BandChangesWithoutSignal++;
                    prevBand = row.Band;
                }

                CountTopics(row.Topics, topicCounts);
                ScanLadder(row, prevDelivered, m);

                foreach (var pair in row.TensionByDriver)
                {
                    int had;
                    m.TensionByDriver.TryGetValue(pair.Key, out had);
                    m.TensionByDriver[pair.Key] = had + pair.Value;
                }

                if (row.PartyAway) m.PhasesAway++; else m.PhasesHome++;

                int gained = 0;
                foreach (var pair in row.TensionByDriver)
                    if (pair.Value > 0) gained += pair.Value;
                if (row.PartyAway) m.TensionGainAway += gained; else m.TensionGainHome += gained;

                if (!string.IsNullOrEmpty(row.OutcomeBands))
                {
                    var parts = row.OutcomeBands.Split(';');
                    for (int b = 0; b < parts.Length; b++)
                    {
                        int band;
                        if (!int.TryParse(parts[b], out band) || band < 0 || band >= 4) continue;
                        m.OutcomeCounts[band]++;
                        if (row.PartyAway) m.OutcomesAway[band]++; else m.OutcomesHome[band]++;
                    }
                }
            }

            m.SignalsPerPhase = (double)signals / trace.Rows.Count;
            m.DistinctTopics = topicCounts.Count;
            foreach (var pair in topicCounts)
            {
                if (pair.Value > m.MaxTopicRepeats ||
                    (pair.Value == m.MaxTopicRepeats && string.CompareOrdinal(pair.Key, m.MostRepeatedTopic) < 0))
                {
                    m.MaxTopicRepeats = pair.Value;
                    m.MostRepeatedTopic = pair.Key;
                }
            }

            return m;
        }

        private static void CountTopics(string topics, Dictionary<string, int> counts)
        {
            if (string.IsNullOrEmpty(topics)) return;
            var parts = topics.Split(';');
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length == 0) continue;
                int n;
                counts.TryGetValue(parts[i], out n);
                counts[parts[i]] = n + 1;
            }
        }

        /// <summary>
        /// Лестница меряется по ФАКТИЧЕСКОМУ состоянию накопителя, а не
        /// восстанавливается по цепочке событий: выданная ступень обязана быть
        /// ровно на единицу выше того, что игрок уже услышал к прошлой фазе.
        ///
        /// Реконструкция «помню, где была лестница, и сбрасываю после удара»
        /// давала ложные пропуски: она не видела разницы между тем, что помнит
        /// метрика, и тем, что помнит сам трек. Мерить надо предмет, а не свою
        /// модель предмета.
        /// </summary>
        private static void ScanLadder(DayRow row, Dictionary<string, int> prevDelivered, CampaignMetrics m)
        {
            if (!string.IsNullOrEmpty(row.Forewarnings))
            {
                var parts = row.Forewarnings.Split(';');
                for (int i = 0; i < parts.Length; i++)
                {
                    int colon = parts[i].LastIndexOf(':');
                    if (colon <= 0) continue;

                    string src = parts[i].Substring(0, colon);
                    int level;
                    if (!int.TryParse(parts[i].Substring(colon + 1), out level)) continue;

                    if (m.FirstForewarnDay < 0) m.FirstForewarnDay = row.Day;

                    int heard;
                    if (!prevDelivered.TryGetValue(src, out heard)) heard = 0;

                    if (level != heard + 1) m.SkippedLadderSteps++;
                    if (level >= 3) m.FullLadders++;
                }
            }

            foreach (var pair in row.Delivered)
                prevDelivered[pair.Key] = pair.Value;
        }
    }
}
