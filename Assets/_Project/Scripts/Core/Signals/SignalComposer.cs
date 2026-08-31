using System;
using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Checks;
using Game.Core.Pressure;
using Game.Core.World;

namespace Game.Core.Signals
{
    /// <summary>
    /// Единственная точка, где внутреннее состояние превращается в наблюдаемое.
    /// Всё, что игрок когда-либо узнаёт о скрытых шкалах, проходит здесь.
    ///
    /// Три правила слоя (Поправка №3.4):
    /// 1) нет немого перехода полосы — смена полосы обязана дать сигнал;
    /// 2) бюджет внимания — не больше MaxSignalsPerDay, иначе шум;
    /// 3) минимум один слот отдан под «что изменилось со вчера».
    ///
    /// Полностью детерминирован: порядок отбора задан правилами и лексикографией
    /// ключей, никакого Random (Поправка №3.3).
    /// </summary>
    public static class SignalComposer
    {
        public static SignalDigest Compose(
            TensionBand band,
            IReadOnlyList<TensionChange> dayLedger,
            int tier,
            SignalBalance cfg)
        {
            return Compose(band, dayLedger, tier, cfg, null, null, null, false);
        }

        /// <summary>
        /// Полная композиция дня: полосы, предвестники, инциденты, доклады с постов.
        /// </summary>
        public static SignalDigest Compose(
            TensionBand band,
            IReadOnlyList<TensionChange> dayLedger,
            int tier,
            SignalBalance cfg,
            IReadOnlyList<Forewarning> forewarnings,
            IReadOnlyList<IncidentOutcome> incidents,
            IReadOnlyList<PostReport> postReports,
            bool isNight)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));

            var candidates = new List<SignalRequest>();

            // Предвестники: чем выше ступень, тем громче. Уровень 2 обязан
            // назвать домен, уровень 3 — близость. Точный день — никогда.
            if (forewarnings != null)
            {
                for (int i = 0; i < forewarnings.Count; i++)
                {
                    var f = forewarnings[i];
                    candidates.Add(new SignalRequest(
                        SignalChannel.Forewarning,
                        "forewarn.level" + f.Level,
                        UrgencyForForewarn(f.Level),
                        subjectId: f.SourceId,
                        isDelta: true,
                        tags: new[] { "domain:" + f.DomainTag, "level:" + f.Level }));
                }
            }

            // Инциденты: то, что уже произошло, игрок обязан узнать.
            if (incidents != null)
            {
                for (int i = 0; i < incidents.Count; i++)
                {
                    var inc = incidents[i];
                    var urgency = inc.WasCrisis
                        ? SignalUrgency.Imminent
                        : (inc.Band == OutcomeBand.Worst ? SignalUrgency.Alarming : SignalUrgency.Notable);

                    var tags = new List<string> { "domain:" + inc.DomainTag, "band:" + inc.Band };
                    if (inc.WasUnmanned) tags.Add("unmanned");
                    if (inc.WasCrisis) tags.Add("crisis");
                    if (inc.Bite.HasValue) tags.Add("bite:" + inc.Bite.Value);

                    candidates.Add(new SignalRequest(
                        SignalChannel.CompanionLine,
                        inc.TopicId + "." + inc.Band,
                        urgency,
                        subjectId: inc.AffectedActorId,
                        isDelta: true,
                        tags: tags.ToArray()));
                }
            }

            // Доклады с постов: читаемое состояние без панели.
            if (postReports != null)
            {
                for (int i = 0; i < postReports.Count; i++)
                {
                    var report = postReports[i];
                    if (report.IsSilent) continue;

                    candidates.Add(new SignalRequest(
                        SignalChannel.PostReport,
                        "post." + report.DomainTag + "." + report.Accuracy,
                        SignalUrgency.Notable,
                        subjectId: report.ActorId,
                        isDelta: false,
                        tags: new[] { "domain:" + report.DomainTag, "accuracy:" + report.Accuracy }));
                }
            }

            // Ночь звучит иначе: горожан не слышно, слышно город.
            if (isNight)
            {
                candidates.Add(new SignalRequest(
                    SignalChannel.Ambient,
                    "night.ambient." + band,
                    SignalUrgency.Ambient,
                    isDelta: false,
                    tags: new[] { "night", "band:" + band }));
            }

            // 1. Переходы полос — самое важное, что случилось за день.
            if (dayLedger != null)
            {
                for (int i = 0; i < dayLedger.Count; i++)
                {
                    var change = dayLedger[i];
                    if (change.Rejected || !change.ChangedBand) continue;
                    if (!cfg.ForceSignalOnBandChange) continue;

                    bool rising = change.To > change.From;
                    candidates.Add(new SignalRequest(
                        SignalChannel.CompanionLine,
                        "tension.band." + change.To,
                        UrgencyForBand(change.To),
                        subjectId: null,
                        isDelta: true,
                        tags: new[] { rising ? "rising" : "falling", "band:" + change.To }));
                }
            }

            // 2. Фоновая реплика по текущей полосе — «так сейчас», не «изменилось».
            //    Ночью недоступна: диалоги с горожанами закрыты (US-1.5).
            if (!isNight)
            {
                candidates.Add(new SignalRequest(
                    SignalChannel.CitizenLine,
                    "tension.ambient." + band,
                    SignalUrgency.Ambient,
                    subjectId: null,
                    isDelta: false,
                    tags: new[] { "band:" + band }));
            }

            var selected = Select(candidates, cfg);
            return new SignalDigest(selected, BuildMoodboard(band, tier));
        }

        /// <summary>
        /// Полоса задаёт не только текст, но и громкость: «Излом» нельзя
        /// сообщить шёпотом, иначе кризис покажется несправедливым (риск R8).
        /// </summary>
        public static SignalUrgency UrgencyForBand(TensionBand band)
        {
            switch (band)
            {
                case TensionBand.Calm:
                case TensionBand.Murmur:
                    return SignalUrgency.Notable;
                case TensionBand.Ferment:
                    return SignalUrgency.Notable;
                case TensionBand.Heat:
                    return SignalUrgency.Alarming;
                default:
                    return SignalUrgency.Imminent;
            }
        }

        /// <summary>
        /// Громкость предвестника растёт со ступенью: «вот-вот» нельзя сообщить
        /// шёпотом, иначе кризис покажется несправедливым.
        /// </summary>
        public static SignalUrgency UrgencyForForewarn(int level)
        {
            if (level >= 3) return SignalUrgency.Alarming;
            if (level == 2) return SignalUrgency.Notable;
            return SignalUrgency.Ambient;
        }

        private static List<SignalRequest> Select(List<SignalRequest> candidates, SignalBalance cfg)
        {
            candidates.Sort(Compare);

            int budget = cfg.MaxSignalsPerDay > 0 ? cfg.MaxSignalsPerDay : 1;
            var pickedIndices = new List<int>();
            for (int i = 0; i < candidates.Count && pickedIndices.Count < budget; i++)
                pickedIndices.Add(i);

            // Гарантия слота под дельту: если изменения были, но не попали
            // в бюджет — вытесняем самый тихий недельтовый сигнал.
            int deltaNeeded = cfg.MinDeltaSlots;
            if (deltaNeeded > 0)
            {
                int haveDelta = CountDelta(candidates, pickedIndices);

                for (int i = 0; i < candidates.Count && haveDelta < deltaNeeded; i++)
                {
                    if (!candidates[i].IsDelta || pickedIndices.Contains(i)) continue;

                    int victimSlot = LastNonDeltaSlot(candidates, pickedIndices);
                    if (victimSlot < 0) break;

                    pickedIndices[victimSlot] = i;
                    haveDelta++;
                }
            }

            var picked = new List<SignalRequest>(pickedIndices.Count);
            for (int i = 0; i < pickedIndices.Count; i++)
                picked.Add(candidates[pickedIndices[i]]);
            return picked;
        }

        /// <summary>Срочность убыв. → дельта вперёд → ключ по алфавиту (для воспроизводимости).</summary>
        private static int Compare(SignalRequest a, SignalRequest b)
        {
            int byUrgency = b.Urgency.CompareTo(a.Urgency);
            if (byUrgency != 0) return byUrgency;

            int byDelta = b.IsDelta.CompareTo(a.IsDelta);
            if (byDelta != 0) return byDelta;

            return string.CompareOrdinal(a.TopicId, b.TopicId);
        }

        private static int CountDelta(List<SignalRequest> candidates, List<int> picked)
        {
            int n = 0;
            for (int i = 0; i < picked.Count; i++)
                if (candidates[picked[i]].IsDelta) n++;
            return n;
        }

        private static int LastNonDeltaSlot(List<SignalRequest> candidates, List<int> picked)
        {
            for (int slot = picked.Count - 1; slot >= 0; slot--)
                if (!candidates[picked[slot]].IsDelta) return slot;
            return -1;
        }

        /// <summary>
        /// Процветание растёт от тира, упадок — от полосы Напряжения.
        /// Две независимые оси: см. комментарий к MoodboardState.
        /// </summary>
        private static MoodboardState BuildMoodboard(TensionBand band, int tier)
        {
            int prosperity = tier - 1;
            if (prosperity < 0) prosperity = 0;
            if (prosperity > 4) prosperity = 4;

            int decay = (int)band;

            var overlays = new List<string>();
            if (band >= TensionBand.Ferment) overlays.Add("граффити");
            if (band >= TensionBand.Heat) overlays.Add("баррикады");
            if (band >= TensionBand.Fracture) overlays.Add("оружие_открыто");

            return new MoodboardState(prosperity, decay, overlays.ToArray());
        }
    }
}
