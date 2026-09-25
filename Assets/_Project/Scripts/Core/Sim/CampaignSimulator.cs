using System.Collections.Generic;
using System.Text;
using Game.Core.Balance;
using Game.Core.Loop;
using Game.Core.Pressure;

namespace Game.Core.Sim
{
    /// <summary>
    /// Ганяє кампанію за заданою політикою і пише трасу.
    ///
    /// Навіщо це взагалі: детермінізм ядра оплачений дорого (Поправка №3.3), а
    /// головну його вигоду ми не використовували. Раз кампанія відтворювана до
    /// байта, темп можна ВИМІРЯТИ, а не обговорювати: прогнати сто кампаній за
    /// секунду і побачити, на яку добу приходить перший інцидент, скільки
    /// діб поспіль гравцю не показують нічого нового і скільки разів повториться
    /// той самий ключ репліки.
    ///
    /// Симулятор не будує ростер і не знає про StatType: він отримує готовий
    /// конвеєр. Тому цей файл лишається в тих самих рамках, що і весь міський
    /// шар (архітектурний тест це перевіряє).
    /// </summary>
    internal static class CampaignSimulator
    {
        /// <summary>Раз на стільки діб агресивна політика робить велике рішення.</summary>
        private const int ChoiceEveryDays = 10;

        /// <summary>
        /// Колбек перед кожною добою: викликач рухає ростер (хто пішов,
        /// хто повернувся) і відповідає, чи поза містом зараз партія. Симулятор
        /// лишається розв'язаним від моделі персонажа.
        /// </summary>
        internal delegate bool BeforeDay(int day);

        internal static CampaignTrace Run(DayProcessor processor, SimPolicy policy, int days, BalanceConfig balance)
        {
            return Run(processor, policy, days, balance, null);
        }

        internal static CampaignTrace Run(DayProcessor processor, SimPolicy policy, int days,
            BalanceConfig balance, BeforeDay beforeDay)
        {
            return RunCore(processor, policy, days, balance, beforeDay, processor.Advance);
        }

        /// <summary>
        /// Той самий прогін, але час іде через <see cref="Base.SettlementCycle"/>
        /// (Поправка №7.1), а не голим DayProcessor.Advance: тільки міст
        /// переносить прапорець голоду з BaseState у конвеєр, тому темп на світі
        /// з виробництвом міряється чесно, а не з мовчки вимкненим
        /// голодом. tools/Alpha.Sim і CampaignPacingTests кличуть цей шлях.
        /// </summary>
        internal static CampaignTrace Run(Base.SettlementCycle cycle, SimPolicy policy, int days, BalanceConfig balance)
        {
            return Run(cycle, policy, days, balance, null);
        }

        internal static CampaignTrace Run(Base.SettlementCycle cycle, SimPolicy policy, int days,
            BalanceConfig balance, BeforeDay beforeDay)
        {
            if (cycle == null) throw new System.ArgumentNullException(nameof(cycle));
            return RunCore(cycle.Processor, policy, days, balance, beforeDay, cycle.AdvanceDay);
        }

        private delegate DayReport AdvancePhase(DayPhase phase);

        private static CampaignTrace RunCore(DayProcessor processor, SimPolicy policy, int days,
            BalanceConfig balance, BeforeDay beforeDay, AdvancePhase advance)
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
                    // G22: доба в цій точці циклу вже ЗАКРИТА (минула фаза
                    // віддала свій звіт, наступна ще не почата) — прямий
                    // TensionDrivers.QuestChoice(processor.Tension, …) тут
                    // міняв полосу, але наступний Advance() стирав журнал
                    // (TensionState.BeginDay()) раніше, ніж SignalStep встигав
                    // його прочитати: зміна полоси відбувалася БЕЗ мандатного
                    // сигналу G21, і причина була не в бюджеті, а в тому,
                    // що кандидат узагалі не будувався. QueueQuestChoice —
                    // місток R6: заявка ляже на тік наступної фази, ДО
                    // SignalStep тієї самої фази, і полосу почують у її звіті.
                    processor.QueueQuestChoice(TensionDrivers.ChoiceWeight.Major);
                }

                // Знімок береться ПІСЛЯ КОЖНОЇ фази окремо. AdvanceFullDay
                // повернув би обидва звіти разом, і тоді денний рядок показував
                // би нічні заряди — траса брехала б рівно в тому стовпці, заради
                // якого її й заводили.
                var dayReport = advance(DayPhase.Day);
                var dayRow = Capture(processor, dayReport, trace.TrackIds, lastFired);
                dayRow.PartyAway = away;
                trace.Rows.Add(dayRow);

                var nightReport = advance(DayPhase.Night);
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
        /// Удар фіксується точно, а не вгадується за падінням заряду: у трека
        /// є дата останнього спрацювання, і досить помітити її зміну.
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

        /// <summary>Звести трасу до чисел, про які можна писати тести.</summary>
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
                    // Інваріант 4: зміна полоси зобов'язана породити спостережуваний сигнал.
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
        /// Драбина міряється за ФАКТИЧНИМ станом накопичувача, а не
        /// відновлюється за ланцюжком подій: видана ступінь зобов'язана бути
        /// рівно на одиницю вища за те, що гравець уже почув до минулої фази.
        ///
        /// Реконструкція «пам'ятаю, де була драбина, і скидаю після удару»
        /// давала хибні пропуски: вона не бачила різниці між тим, що пам'ятає
        /// метрика, і тим, що пам'ятає сам трек. Міряти треба предмет, а не свою
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
