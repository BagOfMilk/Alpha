using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Game.Core.Loop;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Инварианты архитектуры, записанные в CLAUDE.md, проверяются автоматически.
    /// Дизайн-правило, которое держится только на дисциплине, рано или поздно
    /// нарушат; правило, которое роняет тесты, — нет.
    /// </summary>
    public class ArchitectureGuardTests
    {
        private static string CoreRoot =>
            Path.Combine(Application.dataPath, "_Project", "Scripts", "Core");

        /// <summary>
        /// Инвариант №1: в ядре нет System.Random. Планировщик событий —
        /// детерминированный накопитель (Поправка №3.3).
        /// </summary>
        [Test]
        public void Core_ContainsNoRandom()
        {
            var offenders = new List<string>();
            var pattern = new Regex(@"\bSystem\.Random\b|\bnew\s+Random\s*\(|UnityEngine\.Random");

            foreach (var file in Directory.GetFiles(CoreRoot, "*.cs", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(file);
                if (pattern.IsMatch(StripComments(text)))
                    offenders.Add(Path.GetFileName(file));
            }

            Assert.IsEmpty(offenders,
                "В ядре не должно быть случайности: " + string.Join(", ", offenders));
        }

        /// <summary>
        /// Инвариант: городской слой не завязан на типы, которые будут переписаны
        /// при перестройке ядра (StatType, ResourceType и их namespace).
        /// </summary>
        [Test]
        public void SettlementLayer_DoesNotReferenceLegacyTypes()
        {
            string[] layers = { "Pressure", "Signals", "Loop" };
            var forbidden = new Regex(@"Game\.Core\.Stats|Game\.Core\.Economy|\bStatType\b|\bResourceType\b");
            var offenders = new List<string>();

            foreach (var layer in layers)
            {
                var dir = Path.Combine(CoreRoot, layer);
                if (!Directory.Exists(dir)) continue;

                foreach (var file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
                {
                    if (forbidden.IsMatch(StripComments(File.ReadAllText(file))))
                        offenders.Add($"{layer}/{Path.GetFileName(file)}");
                }
            }

            Assert.IsEmpty(offenders,
                "Городской слой обязан развязываться от умирающих типов через порты: "
                + string.Join(", ", offenders));
        }

        /// <summary>
        /// Инвариант №9: названия настольных систем-источников не упоминаются
        /// нигде в проекте (юридическая гигиена, Поправка №3.11).
        /// </summary>
        [Test]
        public void Project_DoesNotNameSourceSystems()
        {
            // Слово собирается из частей намеренно: иначе тест поймал бы сам себя.
            string needle = "GU" + "RPS" + "|" + "Steve" + @"\s+" + "Jackson";
            var forbidden = new Regex(needle, RegexOptions.IgnoreCase);
            var offenders = new List<string>();

            foreach (var file in Directory.GetFiles(Application.dataPath, "*.cs", SearchOption.AllDirectories))
                if (forbidden.IsMatch(File.ReadAllText(file)))
                    offenders.Add(Path.GetFileName(file));

            Assert.IsEmpty(offenders, "Источник заимствования не называется: " + string.Join(", ", offenders));
        }

        /// <summary>
        /// Порядок шагов дня объявлен один раз и растёт монотонно: скрытые
        /// зависимости между системами не заводятся.
        /// </summary>
        [Test]
        public void DayStepOrder_IsStrictlyIncreasing()
        {
            int[] order =
            {
                DayStepOrder.Clock, DayStepOrder.Construction, DayStepOrder.Production,
                DayStepOrder.Population, DayStepOrder.Derived, DayStepOrder.Tension,
                DayStepOrder.Obligations, DayStepOrder.Pulse, DayStepOrder.Incidents,
                DayStepOrder.Healing, DayStepOrder.Signals, DayStepOrder.Report
            };

            for (int i = 1; i < order.Length; i++)
                Assert.Less(order[i - 1], order[i], "Шаги дня обязаны идти строго по возрастанию");
        }

        /// <summary>
        /// Лечение стоит ПОСЛЕ Напряжения и его не трогает — техническая
        /// сторона гарантии US-1.3.
        /// </summary>
        [Test]
        public void Healing_RunsAfterTension()
        {
            Assert.Greater(DayStepOrder.Healing, DayStepOrder.Tension);
        }

        /// <summary>Сигналы собираются последними — по финальному состоянию дня.</summary>
        [Test]
        public void Signals_RunAfterEverythingElse()
        {
            Assert.Greater(DayStepOrder.Signals, DayStepOrder.Incidents);
            Assert.Greater(DayStepOrder.Signals, DayStepOrder.Healing);
            Assert.Less(DayStepOrder.Signals, DayStepOrder.Report);
        }

        private static string StripComments(string source)
        {
            source = Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
            return Regex.Replace(source, @"//.*?$", string.Empty, RegexOptions.Multiline);
        }
    }
}
