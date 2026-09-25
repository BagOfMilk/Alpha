using System;
using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Checks;
using Game.Core.Pressure;
using Game.Core.World;

namespace Game.Core.Signals
{
    /// <summary>
    /// Єдина точка, де внутрішній стан перетворюється на спостережуване.
    /// Усе, що гравець коли-небудь дізнається про приховані шкали, проходить тут.
    ///
    /// Три правила шару (Поправка №3.4):
    /// 1) нема німого переходу полоси — зміна полоси зобов'язана дати сигнал;
    /// 2) бюджет уваги — не більше MaxSignalsPerDay, інакше шум;
    /// 3) щонайменше один слот віддано під «що змінилося з учора».
    ///
    /// З правила 2 є рівно один виняток, і він навмисний (G21,
    /// закрито 24.09.2026): кандидати з <see cref="SignalRequest.Mandatory"/>
    /// (зміна полоси, ступінь передвісника) ідуть у дайджест ПОНАД бюджетом і
    /// не ділять спільний резерв правила 3 (<see cref="Balance.SignalBalance.MinDeltaSlots"/>)
    /// з іншими дельтами. До цього виправлення обидва правила ДОДАВАЛИ
    /// кандидата, але не резервували йому слот — у густий день бюджет або
    /// конкуруюча дельта могли витіснити саме зміну полоси або саме
    /// ступінь драбини, і правило 1 порушувалось мовчки. Див. <see cref="Select"/>.
    ///
    /// Цілком детермінований: порядок відбору заданий правилами і лексикографією
    /// ключів, жодного Random (Поправка №3.3).
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
        /// Повна композиція дня: полоси, передвісники, інциденти, доповіді з постів.
        /// </summary>
        public static SignalDigest Compose(
            TensionBand band,
            IReadOnlyList<TensionChange> dayLedger,
            int tier,
            SignalBalance cfg,
            IReadOnlyList<Forewarning> forewarnings,
            IReadOnlyList<IncidentOutcome> incidents,
            IReadOnlyList<PostReport> postReports,
            bool isNight,
            SignalMemory memory = null,
            int day = 0,
            IReadOnlyList<CityEvent> cityEvents = null)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));

            var candidates = new List<SignalRequest>();

            // Що зробило місто: будівництво, люди, тір (Поправка №6). Городяни
            // говорять про це самі — це їхні вулиці і їхні сусіди.
            if (cityEvents != null)
                for (int i = 0; i < cityEvents.Count; i++)
                {
                    var e = cityEvents[i];
                    candidates.Add(new SignalRequest(
                        SignalChannel.CitizenLine, e.TopicId, e.Urgency,
                        isDelta: true, tags: e.Tags));
                }

            // Передвісники: чим вища ступінь, тим гучніше. Рівень 2 зобов'язаний
            // назвати домен, рівень 3 — близькість. Точний день — ніколи.
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
                        tags: new[] { "domain:" + f.DomainTag, "level:" + f.Level },
                        // Мандатно (G21): накопичувач уже зарахував цю ступінь
                        // почутою (WorldPulse.MarkDelivered — PulseStep, ДО
                        // цього кроку). Якщо бюджет її тут відкине, драбина
                        // мовчки просунеться далі, а гравець цю ступінь
                        // не почує ніколи — це і є «пропущена ступінь».
                        mandatory: true));
                }
            }

            // Інциденти: те, що вже сталося, гравець зобов'язаний дізнатися.
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

                    // Страх громади — прихована ціна, і за інваріантом 6 у неї
                    // зобов'язаний бути голос: інакше гравець побачить вирослий
                    // поріг соціальної перевірки і не зрозуміє, чим він це
                    // заслужив. Голосом слугує тег на репліці про саму подію,
                    // а не окремий сигнал: бюджет уваги один, і витрачати два
                    // слоти на одну подію означає глушити щось інше (правило 2
                    // шару сигналів).
                    if (inc.CausedFear) tags.Add("fear");
                    if (inc.PeopleArrived > 0) tags.Add("arrived:" + inc.PeopleArrived);

                    candidates.Add(new SignalRequest(
                        SignalChannel.CompanionLine,
                        inc.TopicId + "." + inc.Band,
                        urgency,
                        subjectId: inc.AffectedActorId,
                        isDelta: true,
                        tags: tags.ToArray()));
                }
            }

            // Доповіді з постів: читабельний стан без панелі.
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

            // Ніч звучить інакше: городян не чутно, чутно місто.
            if (isNight)
            {
                candidates.Add(new SignalRequest(
                    SignalChannel.Ambient,
                    "night.ambient." + band,
                    SignalUrgency.Ambient,
                    isDelta: false,
                    tags: new[] { "night", "band:" + band }));
            }

            // 1. Переходи полос — найважливіше, що сталося за день.
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
                        tags: new[] { rising ? "rising" : "falling", "band:" + change.To },
                        // Мандатно (G21, інваріант 4): зміна полоси — найважливіше,
                        // що сталося за добу. Раніше цей кандидат ТІЛЬКИ
                        // додавався і міг бути витіснений бюджетом уваги (або
                        // іншою дельтою за спільний MinDeltaSlots) — тоді
                        // перехід лишався німим. Тепер він гарантований понад
                        // бюджетом, а не конкурує за спільний резерв дельт.
                        mandatory: true));
                }
            }

            // 2. Фонова репліка за поточною полосою — «так зараз», не «змінилося».
            //    Уночі недоступна: діалоги з городянами закриті (US-1.5).
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

            var selected = Select(candidates, cfg, memory, day);

            // Показане запам'ятовується: завтра за рівної терміновості вперед
            // піде те, чого гравець довше не чув.
            if (memory != null) memory.Remember(selected, day);

            return new SignalDigest(selected, BuildMoodboard(band, tier));
        }

        /// <summary>
        /// Полоса задає не лише текст, а й гучність: «Злам» не можна
        /// повідомити пошепки, інакше криза здасться несправедливою (ризик R8).
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
        /// Гучність передвісника росте зі ступенем: «ось-ось» не можна
        /// повідомити пошепки, інакше криза здасться несправедливою.
        /// </summary>
        public static SignalUrgency UrgencyForForewarn(int level)
        {
            if (level >= 3) return SignalUrgency.Alarming;
            if (level == 2) return SignalUrgency.Notable;
            return SignalUrgency.Ambient;
        }

        private static List<SignalRequest> Select(List<SignalRequest> candidates, SignalBalance cfg,
            SignalMemory memory, int day)
        {
            candidates.Sort((a, b) => Compare(a, b, memory, day));

            // Придушення повторів. Знімається лише те, що гравець нещодавно чув
            // і що не є дельтою; останній кандидат не знімається, інакше
            // день став би німим.
            if (memory != null && cfg.TopicCooldownDays > 0)
            {
                for (int i = candidates.Count - 1; i >= 0 && candidates.Count > 1; i--)
                {
                    if (candidates[i].IsDelta || candidates[i].Mandatory) continue;
                    if (memory.Staleness(candidates[i].TopicId, day) >= cfg.TopicCooldownDays) continue;
                    candidates.RemoveAt(i);
                }
            }

            int budget = cfg.MaxSignalsPerDay > 0 ? cfg.MaxSignalsPerDay : 1;
            var pickedIndices = new List<int>();
            for (int i = 0; i < candidates.Count && pickedIndices.Count < budget; i++)
                pickedIndices.Add(i);

            // Гарантія слоту під дельту: якщо зміни були, але не потрапили
            // в бюджет — витісняємо найтихіший недельтовий сигнал.
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

            // Гарантія мандатних сигналів (G21, інваріант 4): зміна полоси і
            // ступінь передвісника потрапляють у дайджест ЗАВЖДИ, навіть понад
            // бюджетом — вони не ділять один спільний резерв з іншими дельтами
            // (MinDeltaSlots) і не можуть бути витіснені густим днем. Це
            // єдиний навмисний виняток із бюджету уваги: не почута зміна
            // полоси читається як «гра зламалася», а не почута ступінь драбини —
            // як пропущена назавжди (драбина просунулась мовчки — WorldPulse
            // вже вважає її почутою).
            for (int i = 0; i < candidates.Count; i++)
            {
                if (!candidates[i].Mandatory || pickedIndices.Contains(i)) continue;
                pickedIndices.Add(i);
            }

            var picked = new List<SignalRequest>(pickedIndices.Count);
            for (int i = 0; i < pickedIndices.Count; i++)
                picked.Add(candidates[pickedIndices[i]]);
            return picked;
        }

        /// <summary>
        /// Терміновість спад. → дельта вперед → давно не звучало вперед → ключ за
        /// алфавітом (для відтворюваності).
        ///
        /// Третій критерій і є анти-повтор. Він нічого не забороняє: термінове
        /// як і раніше обганяє все, а от два однаково тихих сигнали більше не
        /// вирішуються алфавітом раз і назавжди на користь одного й того самого.
        /// </summary>
        private static int Compare(SignalRequest a, SignalRequest b, SignalMemory memory, int day)
        {
            int byUrgency = b.Urgency.CompareTo(a.Urgency);
            if (byUrgency != 0) return byUrgency;

            int byDelta = b.IsDelta.CompareTo(a.IsDelta);
            if (byDelta != 0) return byDelta;

            if (memory != null)
            {
                int byStaleness = memory.Staleness(b.TopicId, day).CompareTo(memory.Staleness(a.TopicId, day));
                if (byStaleness != 0) return byStaleness;
            }

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
        /// Процвітання росте від тіра, занепад — від полоси Напруги.
        /// Дві незалежні осі: див. коментар до MoodboardState.
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
