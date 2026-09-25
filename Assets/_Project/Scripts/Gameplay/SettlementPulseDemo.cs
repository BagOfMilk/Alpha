using System.Collections.Generic;
using System.Text;
using Game.Core.Balance;
using Game.Core.Loop;
using Game.Core.Pressure;
using Game.Core.Signals;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Грайбельна демонстрація етапу Е0 «Пульс міста»: повісь на порожній
    /// GameObject, натисни Play — у консоль піде хроніка поселення по днях.
    ///
    /// Це перевірка головної тези дизайну: «температура» міста має
    /// читатися словами, жодного разу не показавши числа. Зверни увагу — цей
    /// клас фізично НЕ МОЖЕ надрукувати значення Напруги: воно internal
    /// у збірці Game.Core (Поправка №3.4).
    /// </summary>
    public sealed class SettlementPulseDemo : MonoBehaviour
    {
        [Tooltip("Необязательно. Если пусто — берутся числа по умолчанию из кода.")]
        public BalanceConfigAsset balanceAsset;

        [Header("Симуляция")]
        [Min(1)] public int daysToSimulate = 120;
        [Tooltip("Тир поселения: 1 хутор, 2 село, 3 слобода, 4 городок.")]
        [Range(1, 4)] public int tier = 2;
        [Tooltip("Уклад: 0 Вольница, 1 Присмотр, 2 Порядок, 3 Затвор.")]
        [Range(0, 3)] public int orderLevel = 1;
        public bool runOnStart = true;

        [Header("Проверка выборов")]
        [Tooltip("На этих днях игрок принимает «тяжёлое» решение в квесте.")]
        public int[] heavyChoiceDays = { 10, 25, 40, 55, 70, 85, 100 };
        [Tooltip("На этих днях событие разрешается хорошо и разряжает обстановку.")]
        public int[] reliefDays = { 110 };

        private void Start()
        {
            if (runOnStart) RunSimulation();
        }

        [ContextMenu("Run Simulation")]
        public void RunSimulation()
        {
            BalanceConfig balance = balanceAsset != null ? balanceAsset.ToConfig() : new BalanceConfig();

            var tension = new TensionState(balance.Tension);
            var processor = new DayProcessor(tension, balance, DayProcessor.DefaultSteps())
            {
                Tier = tier,
                OrderLevel = orderLevel
            };

            var choiceDays = new HashSet<int>(heavyChoiceDays ?? new int[0]);
            var calmDays = new HashSet<int>(reliefDays ?? new int[0]);
            var log = new StringBuilder();
            log.AppendLine($"=== ХРОНИКА ПОСЕЛЕНИЯ: {daysToSimulate} дней, тир {tier} ===");

            var lastBand = tension.Band;
            log.AppendLine($"— начало: {BandName(lastBand)}");

            for (int i = 0; i < daysToSimulate; i++)
            {
                var report = processor.Advance();

                if (choiceDays.Contains(report.Day))
                {
                    // G22: доба вже закрита (Advance() вище віддав звіт) —
                    // прямий TensionDrivers.QuestChoice(tension, …) тут губив
                    // мандатний сигнал зміни полоси (BeginDay() наступної фази
                    // стирав журнал раніше SignalStep). QueueQuestChoice кладе
                    // заявку містком R6 — тік наступної фази її почує.
                    processor.QueueQuestChoice(TensionDrivers.ChoiceWeight.Major);
                    log.AppendLine($"[день {report.Day}] тяжёлое решение в квесте");
                }
                else if (calmDays.Contains(report.Day))
                {
                    processor.QueueEventOutcome(TensionDrivers.ChoiceWeight.Major);
                    log.AppendLine($"[день {report.Day}] город выдохнул: событие разрешилось хорошо");
                }

                foreach (var line in Describe(report))
                    log.AppendLine($"[день {report.Day}] {line}");

                if (tension.Band != lastBand)
                {
                    log.AppendLine($"[день {report.Day}] *** город переходит в состояние «{BandName(tension.Band)}» ***");
                    lastBand = tension.Band;
                }
            }

            log.AppendLine($"=== итог: {BandName(tension.Band)}, в этом состоянии {tension.DaysInCurrentBand} дн. ===");
            Debug.Log(log.ToString());
        }

        /// <summary>
        /// Заглушка таблиці реплік: на Е2 її замінить ScriptableObject-набір,
        /// який наповнює письменник без участі програміста (US-18.1).
        /// </summary>
        private static IEnumerable<string> Describe(DayReport report)
        {
            var digest = report.Signals;
            if (digest == null) yield break;

            foreach (var r in digest.Requests)
                yield return $"{Speaker(r.Channel)}: {Line(r.TopicId)}";
        }

        private static string Speaker(SignalChannel channel)
        {
            switch (channel)
            {
                case SignalChannel.CitizenLine: return "горожанин";
                case SignalChannel.CompanionLine: return "напарник";
                default: return "город";
            }
        }

        private static string Line(string topicId)
        {
            switch (topicId)
            {
                case "tension.ambient.Calm": return "«Хорошо, что вы здесь». Дети во дворах, бельё на верёвках.";
                case "tension.ambient.Murmur": return "У колодца спорят о ценах. Мусор третий день не убран.";
                case "tension.ambient.Ferment": return "Разговор смолкает, когда подходишь. На стене свежая метка.";
                case "tension.ambient.Heat": return "Ставни закрыты днём. Патруль ходит парами.";
                case "tension.ambient.Fracture": return "Площадь пуста. Оружие носят открыто.";

                case "tension.band.Murmur": return "«Народ ворчит. Пока только ворчит».";
                case "tension.band.Ferment": return "«Тебе стоило бы показаться на площади. Люди хотят увидеть, что ты есть».";
                case "tension.band.Heat": return "«Двое сегодня не вышли на работу. Их не искали».";
                case "tension.band.Fracture": return "«Если ничего не сделаешь — я их не удержу».";
                case "tension.band.Calm": return "«Отпустило. Впервые за долгое время спокойно».";

                default: return topicId;
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
    }
}
