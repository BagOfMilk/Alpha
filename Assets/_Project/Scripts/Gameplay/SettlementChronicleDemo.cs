using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Economy;
using Game.Core.Checks;
using Game.Core.Loop;
using Game.Core.Pressure;
using Game.Core.Settlement;
using Game.Core.Signals;
using Game.Core.Stats;
using Game.Core.World;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Хроніка поселення: повний цикл Е1 у консолі — день і ніч, передвісники,
    /// інциденти, доповіді з постів і криза, про яку попереджали.
    ///
    /// Повісь на порожній об'єкт, натисни Play. Зверни увагу: жодного числа
    /// прихованих шкал тут нема і бути не може — вони internal у ядрі.
    /// </summary>
    public sealed class SettlementChronicleDemo : MonoBehaviour
    {
        [Tooltip("Имя пресета. Идёт префиксом в каждую запись консоли — иначе,\n" +
                 "если включить два объекта сразу, две хроники смешаются.")]
        public string presetName = "Поселение";

        [Tooltip("Необязательно. Пусто — берутся числа по умолчанию.")]
        public BalanceConfigAsset balanceAsset;

        [Header("Симуляция")]
        [Min(1)] public int daysToSimulate = 90;
        [Range(1, 4)] public int tier = 2;
        [Tooltip("Ночью патрулировать вместо сна: узнаёшь больше, но не лечишься.")]
        public bool patrolAtNight = true;
        public bool runOnStart = true;

        [Header("Давление")]
        [Tooltip("Дни, когда игрок принимает тяжёлое решение в квесте.")]
        public int[] heavyChoiceDays = { 8, 18, 28, 38, 48, 58 };

        [Header("Вывод")]
        [Tooltip("Сколько дней в одной записи консоли. Одна запись на всю хронику\n" +
                 "нечитаема: сотни строк без возможности фильтровать.")]
        [Min(1)] public int daysPerLogEntry = 10;

        [Tooltip("Писать полный текст в Chronicle-<пресет>.txt рядом с проектом.")]
        public bool writeToFile = true;

        private void Start()
        {
            if (runOnStart) RunSimulation();
        }

        [ContextMenu("Run Chronicle")]
        public void RunSimulation()
        {
            var balance = balanceAsset != null ? balanceAsset.ToConfig() : new BalanceConfig();

            var roster = BuildRoster();

            // Позиції роздаємо через публічний API бази: сеттер AssignedSlotId
            // навмисно internal, щоб стан не правили в обхід правил.
            var baseState = new BaseState(roster, new ResourceLedger(), balance);
            baseState.AddSlot(new AssignmentSlotDefinition("watch", "Дозор", BaseSectionType.Fortifications));
            baseState.AddSlot(new AssignmentSlotDefinition("market", "Рынок", BaseSectionType.Settlement));
            baseState.TryAssign("guard", "watch");
            baseState.TryAssign("trader", "market");

            // Комора на весь прогін. Хроніка показує тиск міста, а не
            // голод: ферм у цієї громади нема, і без запасу міст чесно довів би
            // голод до Напруги з перших діб. Голодний пресет — це порожня
            // комора, а не окремий режим.
            baseState.Resources.Add(ResourceType.Food,
                balance.FoodUpkeepPerCompanion * roster.Count * (daysToSimulate + 1));

            var adapter = new RosterAdapter(roster, "hero");
            var tension = new TensionState(balance.Tension);

            var pulse = new WorldPulse(balance.Pulse);
            foreach (var source in DefaultPressureSources.All()) pulse.AddSource(source);

            // Цикл поселення йде штатним кроком дня: у доби зобов'язаний бути
            // матеріальний підсумок, інакше хроніка показує тиск без господарства.
            var production = new ProductionStep(baseState);
            var processor = new DayProcessor(tension, balance, SettlementCycle.BuildSteps(production))
            {
                Tier = tier,
                Roster = adapter,
                Casualties = adapter,
                Population = new PopulationState(),
                Pulse = pulse,
                Incidents = DefaultIncidents.BuildTable(),
                Repeats = new RepeatTracker(),
                IsPatrolling = patrolAtNight,
                PostDomains = new[]
                {
                    new PostDomain("watch", "улицы", SkillKeys.Survival, 6),
                    new PostDomain("market", "рынок", SkillKeys.Trade, 6)
                }
            };

            // Час іде тільки через міст: він же переносить у конвеєр прапорець
            // голоду. Поки хроніка крутила processor.Advance напряму, кроки
            // виробництва в ній стояли, а голод не тиснув ніколи.
            var cycle = new SettlementCycle(baseState, processor, production);

            var choices = new HashSet<int>(heavyChoiceDays ?? new int[0]);

            // full — увесь текст для файлу; chunk — поточна декада для консолі.
            var full = new StringBuilder();
            var chunk = new StringBuilder();

            string head = $"=== {presetName}: {daysToSimulate} дней, тир {tier}, " +
                          (patrolAtNight ? "ночью патруль" : "ночью сон") + " ===";
            full.AppendLine(head);
            Debug.Log($"[{presetName}] {head}");

            int chunkStart = 1;
            int incidentCount = 0, crisisCount = 0;
            var lastBand = tension.Band;

            for (int day = 1; day <= daysToSimulate; day++)
            {
                foreach (var phase in new[] { DayPhase.Day, DayPhase.Night })
                {
                    var report = cycle.AdvanceDay(phase);
                    string mark = phase == DayPhase.Night ? "ночь" : "день";

                    foreach (var line in Describe(report))
                        Append(full, chunk, $"[{mark} {day}] {line}");

                    foreach (var incident in report.Incidents)
                    {
                        incidentCount++;
                        string text = DescribeIncident(incident, roster);
                        Append(full, chunk, $"[{mark} {day}] {text}");

                        // Криза і смерть не мають права потонути в загальному полотні:
                        // окремий запис — його видно за кольором і можна відфільтрувати.
                        if (incident.WasCrisis)
                        {
                            crisisCount++;
                            Debug.LogWarning($"[{presetName}] {mark} {day}: {text}");
                        }
                    }
                }

                var produced = production.LastReport;
                if (produced != null && produced.FoodShortage)
                    Append(full, chunk, $"[день {day}] ⚠ еды не хватило — завтра все работают хуже");

                if (choices.Contains(day))
                {
                    // G22: доба вже закрита (обидві фази дня віддані вище) —
                    // прямий TensionDrivers.QuestChoice(tension, …) тут губив
                    // мандатний сигнал зміни полоси (BeginDay() наступної фази
                    // стирав журнал раніше SignalStep). QueueQuestChoice кладе
                    // заявку містком R6 — тік наступної фази її почує.
                    processor.QueueQuestChoice(TensionDrivers.ChoiceWeight.Major);
                    Append(full, chunk, $"[день {day}] тяжёлое решение в квесте");
                }

                if (tension.Band != lastBand)
                {
                    string move = $"день {day}: город переходит в состояние «{BandName(tension.Band)}»";
                    full.AppendLine("*** " + move + " ***");
                    chunk.AppendLine("*** " + move + " ***");
                    Debug.Log($"[{presetName}] {move}");
                    lastBand = tension.Band;
                }

                bool lastDay = day == daysToSimulate;
                if (day % Math.Max(1, daysPerLogEntry) == 0 || lastDay)
                {
                    if (chunk.Length > 0)
                        Debug.Log($"[{presetName}] дни {chunkStart}–{day} — {BandName(tension.Band)}\n{chunk}");
                    chunk.Length = 0;
                    chunkStart = day + 1;
                }
            }

            var tail = new StringBuilder();
            tail.AppendLine($"=== {presetName}: итог за {daysToSimulate} дней ===");
            tail.AppendLine($"  состояние города: {BandName(tension.Band)}");
            tail.AppendLine($"  происшествий: {incidentCount}, из них кризисов: {crisisCount}");
            foreach (var c in roster.All)
                tail.AppendLine($"  {c.DisplayName}: {StatusName(c)}");

            full.Append(tail);
            Debug.Log(tail.ToString());

            if (writeToFile) SaveToFile(full.ToString());
        }

        private static void Append(StringBuilder full, StringBuilder chunk, string line)
        {
            full.AppendLine(line);
            chunk.AppendLine(line);
        }

        /// <summary>
        /// Повний текст — у файл поруч із проектом: консоль хороша, щоб помітити,
        /// а порівнювати два прогони зручніше в текстовому редакторі.
        /// </summary>
        private void SaveToFile(string text)
        {
            try
            {
                string file = Path.Combine(Application.dataPath, "..",
                    "Chronicle-" + Sanitize(presetName) + ".txt");
                File.WriteAllText(Path.GetFullPath(file), text);
                Debug.Log($"[{presetName}] полная хроника: {Path.GetFullPath(file)}");
            }
            catch (Exception e)
            {
                // Не змогли записати — це не привід зривати прогін.
                Debug.LogWarning($"[{presetName}] не удалось записать файл хроники: {e.Message}");
            }
        }

        private static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name)) return "preset";
            var sb = new StringBuilder(name.Length);
            foreach (var ch in name)
                sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), ch) >= 0 ? '_' : ch);
            return sb.ToString();
        }

        private static Roster BuildRoster()
        {
            var roster = new Roster();
            roster.Add(Make("hero", "Лидер", 9));
            roster.Add(Make("guard", "Дозорный", 7));
            roster.Add(Make("trader", "Меняла", 6));
            roster.Add(Make("scout", "Ходок", 5));
            return roster;
        }

        private static Companion Make(string id, string name, int skill)
        {
            var arch = new CompanionArchetype(id, name);
            // Демка рівняє всі скіли під один рівень: її завдання — показати
            // хроніку міста, а не різницю між людьми.
            for (int i = 0; i < Skills.All.Length; i++) arch.SetSkill(Skills.All[i], skill);

            return arch.CreateInstance(id);
        }

        private static IEnumerable<string> Describe(DayReport report)
        {
            if (report.Signals == null) yield break;

            foreach (var r in report.Signals.Requests)
            {
                string domain = FindTag(r.Tags, "domain:");
                string suffix = domain != null ? $" ({domain})" : "";
                yield return $"{Speaker(r.Channel)}: {Line(r)}{suffix}";
            }
        }

        private static string DescribeIncident(IncidentOutcome incident, Roster roster)
        {
            string verdict;
            switch (incident.Band)
            {
                case OutcomeBand.Best: verdict = "разобрались чисто"; break;
                case OutcomeBand.Good: verdict = "уладили"; break;
                case OutcomeBand.Base: verdict = "кое-как замяли"; break;
                default: verdict = incident.WasUnmanned ? "никто не занимался — вышло скверно" : "вышло скверно"; break;
            }

            if (!incident.WasCrisis) return $"ПРОИСШЕСТВИЕ ({incident.DomainTag}): {verdict}";

            string bite = "";
            if (incident.Bite == CrisisBite.KillCompanion)
                bite = $" ПОГИБ: {Name(roster, incident.AffectedActorId)}";
            else if (incident.Bite == CrisisBite.WoundCompanion)
                bite = $" тяжело ранен: {Name(roster, incident.AffectedActorId)}";
            else if (incident.Bite == CrisisBite.PopulationOutflow)
                bite = $" из города ушли {incident.PopulationLost} человек";

            return $"!!! КРИЗИС ({incident.DomainTag}): {verdict}.{bite}";
        }

        private static string Name(Roster roster, string id)
        {
            if (string.IsNullOrEmpty(id)) return "—";
            var c = roster.Get(id);
            return c != null ? c.DisplayName : id;
        }

        private static string FindTag(string[] tags, string prefix)
        {
            if (tags == null) return null;
            foreach (var t in tags)
                if (t != null && t.StartsWith(prefix)) return t.Substring(prefix.Length);
            return null;
        }

        private static string Speaker(SignalChannel channel)
        {
            switch (channel)
            {
                case SignalChannel.CitizenLine: return "горожанин";
                case SignalChannel.CompanionLine: return "напарник";
                case SignalChannel.Forewarning: return "слух";
                case SignalChannel.PostReport: return "доклад";
                case SignalChannel.Ambient: return "город";
                default: return "вид";
            }
        }

        /// <summary>Заглушка таблиці реплік: на Е2 її замінить SO-набір для письменника.</summary>
        private static string Line(SignalRequest r)
        {
            switch (r.TopicId)
            {
                case "tension.ambient.Calm": return "«Хорошо, что вы здесь». Дети во дворах.";
                case "tension.ambient.Murmur": return "У колодца спорят о ценах.";
                case "tension.ambient.Ferment": return "Разговор смолкает, когда подходишь.";
                case "tension.ambient.Heat": return "Ставни закрыты днём. Патруль ходит парами.";
                case "tension.ambient.Fracture": return "Площадь пуста. Оружие носят открыто.";

                case "forewarn.level1": return "«Собаки третью ночь брешут».";
                case "forewarn.level2": return "«Третий день топчется один и тот же».";
                case "forewarn.level3": return "«Что-то готовят. Скоро».";

                case "night.ambient.Calm": return "Тихо. Только ветер.";
                case "night.ambient.Murmur": return "Где-то хлопнула ставня.";
                case "night.ambient.Ferment": return "Шаги за углом стихли, когда обернулся.";
                case "night.ambient.Heat": return "Костры в бочках. Голоса не местные.";
                case "night.ambient.Fracture": return "Ни одного огня в окнах.";

                default:
                    if (r.TopicId.StartsWith("post.")) return "сводка по домену";
                    if (r.TopicId.StartsWith("tension.band.")) return "«Меняется. И не в лучшую сторону».";
                    return r.TopicId;
            }
        }

        private static string BandName(TensionBand band)
        {
            switch (band)
            {
                case TensionBand.Calm: return "Спокойно";
                case TensionBand.Murmur: return "Ропот";
                case TensionBand.Ferment: return "Брожение";
                case TensionBand.Heat: return "Накал";
                default: return "Излом";
            }
        }

        private static string StatusName(Companion c)
        {
            if (c.IsDead) return "погиб";
            if (c.IsInjured) return "ранен";
            return "жив";
        }
    }
}
