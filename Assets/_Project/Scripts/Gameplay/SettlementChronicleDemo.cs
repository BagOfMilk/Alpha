using System.Collections.Generic;
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
    /// Хроника поселения: полный цикл Э1 в консоли — день и ночь, предвестники,
    /// инциденты, доклады с постов и кризис, о котором предупреждали.
    ///
    /// Повесь на пустой объект, нажми Play. Обрати внимание: ни одного числа
    /// скрытых шкал здесь нет и быть не может — они internal в ядре.
    /// </summary>
    public sealed class SettlementChronicleDemo : MonoBehaviour
    {
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

        private void Start()
        {
            if (runOnStart) RunSimulation();
        }

        [ContextMenu("Run Chronicle")]
        public void RunSimulation()
        {
            var balance = balanceAsset != null ? balanceAsset.ToConfig() : new BalanceConfig();

            var roster = BuildRoster();

            // Позиции раздаём через публичный API базы: сеттер AssignedSlotId
            // намеренно internal, чтобы состояние не правили мимо правил.
            var baseState = new BaseState(roster, new ResourceLedger(), balance);
            baseState.AddSlot(new AssignmentSlotDefinition("watch", "Дозор", BaseSectionType.Fortifications));
            baseState.AddSlot(new AssignmentSlotDefinition("market", "Рынок", BaseSectionType.Settlement));
            baseState.TryAssign("guard", "watch");
            baseState.TryAssign("trader", "market");

            var adapter = new RosterAdapter(roster, "hero");
            var tension = new TensionState(balance.Tension);

            var pulse = new WorldPulse(balance.Pulse);
            foreach (var source in DefaultPressureSources.All()) pulse.AddSource(source);

            var processor = new DayProcessor(tension, balance, DayProcessor.DefaultSteps())
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

            var choices = new HashSet<int>(heavyChoiceDays ?? new int[0]);
            var log = new StringBuilder();
            log.AppendLine($"=== ХРОНИКА ПОСЕЛЕНИЯ: {daysToSimulate} дней ===");

            var lastBand = tension.Band;

            for (int i = 0; i < daysToSimulate; i++)
            {
                foreach (var phase in new[] { DayPhase.Day, DayPhase.Night })
                {
                    var report = processor.Advance(phase);
                    string mark = phase == DayPhase.Night ? "ночь" : "день";

                    foreach (var line in Describe(report))
                        log.AppendLine($"[{mark} {report.Day / 2 + 1}] {line}");

                    foreach (var incident in report.Incidents)
                        log.AppendLine($"[{mark} {report.Day / 2 + 1}] {DescribeIncident(incident, roster)}");
                }

                if (choices.Contains(i + 1))
                {
                    TensionDrivers.QuestChoice(tension, TensionDrivers.ChoiceWeight.Major, "q" + i, balance);
                    log.AppendLine($"[решение] тяжёлый выбор в квесте");
                }

                if (tension.Band != lastBand)
                {
                    log.AppendLine($"*** город переходит в состояние «{BandName(tension.Band)}» ***");
                    lastBand = tension.Band;
                }
            }

            log.AppendLine($"=== итог: {BandName(tension.Band)} ===");
            foreach (var c in roster.All)
                log.AppendLine($"  {c.DisplayName}: {StatusName(c)}");

            Debug.Log(log.ToString());
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
            arch.BaseStats.Set(StatType.Charisma, skill);
            arch.BaseStats.Set(StatType.Will, skill);
            arch.BaseStats.Set(StatType.Survival, skill);
            arch.BaseStats.Set(StatType.Medicine, skill);
            arch.BaseStats.Set(StatType.Engineering, skill);
            arch.BaseStats.Set(StatType.Tech, skill);
            arch.BaseStats.Set(StatType.Leadership, skill);
            arch.BaseStats.Set(StatType.Aim, skill);

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

        /// <summary>Заглушка таблицы реплик: на Э2 её заменит SO-набор для писателя.</summary>
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
