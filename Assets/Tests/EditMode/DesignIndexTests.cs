using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Охоронець покажчика дизайн-документації (docs/DESIGN_INDEX.md).
    ///
    /// Покажчик — не джерело істини, але редактори GDD v7 і асистент читають
    /// статус поправки саме з нього. Якщо з'явилася нова поправка, а реєстр
    /// про неї мовчить, або чернетку затвердили, а в реєстрі досі «ЧЕРНЕТКА»,
    /// хтось обов'язково вважатиме чинним те, що не чинне (або навпаки).
    /// Тому звіряємо реєстр із шапками поправок у docs/GDD_AMENDMENTS.md:
    ///  - кожен заголовок «## Поправка №N» має рядок «| №N |» у реєстрі;
    ///  - у реєстрі немає рядків без поправки і немає дублів;
    ///  - статус рядка починається з «ЧЕРНЕТКА» тоді й лише тоді, коли
    ///    шапка поправки (заголовок + цитата-статус одразу під ним) каже
    ///    «ЧЕРНЕТКА»; інакше — з «ЗАТВЕРДЖЕНО»;
    ///  - кожна поправка має хоч один рядок у мапі для GDD v7.
    /// </summary>
    public class DesignIndexTests
    {
        private const string RegistryStart = "<!-- registry:start -->";
        private const string RegistryEnd = "<!-- registry:end -->";
        private const string MapStart = "<!-- v7map:start -->";
        private const string MapEnd = "<!-- v7map:end -->";

        private static readonly Regex AmendmentHeading =
            new Regex(@"^## Поправка №(\d+(?:\.\d+)?)(?!\d)", RegexOptions.CultureInvariant);

        private static readonly Regex RowNumber =
            new Regex(@"^\|\s*№(\d+(?:\.\d+)?)(?![\d.])", RegexOptions.CultureInvariant);

        [Test]
        public void EveryAmendment_HasRegistryRow_WithMatchingStatus()
        {
            var amendments = ReadAmendmentHeaders();
            Assert.That(amendments.Count, Is.GreaterThanOrEqualTo(11),
                "у GDD_AMENDMENTS.md знайдено підозріло мало поправок — зламався розбір заголовків?");

            var rows = ReadRegistryRows();
            var problems = new List<string>();

            foreach (var duplicate in rows.GroupBy(r => r.Number).Where(g => g.Count() > 1))
                problems.Add($"№{duplicate.Key}: у реєстрі {duplicate.Count()} рядки");

            var byNumber = rows.GroupBy(r => r.Number).ToDictionary(g => g.Key, g => g.First());

            foreach (var amendment in amendments)
            {
                if (!byNumber.TryGetValue(amendment.Key, out var row))
                {
                    problems.Add($"№{amendment.Key}: немає рядка в реєстрі DESIGN_INDEX.md §1");
                    continue;
                }

                bool rowIsDraft = row.Status.StartsWith("ЧЕРНЕТКА", StringComparison.Ordinal);
                bool rowIsApproved = row.Status.StartsWith("ЗАТВЕРДЖЕНО", StringComparison.Ordinal);
                if (!rowIsDraft && !rowIsApproved)
                    problems.Add($"№{amendment.Key}: статус «{row.Status}» не починається ні з ЧЕРНЕТКА, ні з ЗАТВЕРДЖЕНО");
                else if (rowIsDraft != amendment.Value)
                    problems.Add($"№{amendment.Key}: у шапці поправки {(amendment.Value ? "ЧЕРНЕТКА" : "не чернетка")}, " +
                                 $"а в реєстрі «{row.Status}»");
            }

            foreach (var row in rows.Where(r => !amendments.ContainsKey(r.Number)))
                problems.Add($"№{row.Number}: рядок реєстру без поправки в GDD_AMENDMENTS.md");

            Assert.IsEmpty(problems, "Реєстр поправок розійшовся з GDD_AMENDMENTS.md:\n" + string.Join("\n", problems));
        }

        [Test]
        public void EveryAmendment_AppearsInGddV7Map()
        {
            var amendments = ReadAmendmentHeaders();
            // Рядок мапи починається з пункту: «| №3.7 …», «| №4 правило …».
            // «№3» зараховує і «№3.7» (підпункт), але «№1» не зараховує «№10».
            var mapText = string.Join("\n", SectionLines(ReadDoc("DESIGN_INDEX.md"), MapStart, MapEnd));
            var missing = amendments.Keys
                .Where(n => !Regex.IsMatch(mapText, @"^\|\s*№" + Regex.Escape(n) + @"(?!\d)", RegexOptions.Multiline))
                .ToList();

            Assert.IsEmpty(missing, "Поправки без жодного рядка в мапі GDD v7 (DESIGN_INDEX.md §4): №" +
                                    string.Join(", №", missing));
        }

        // ---------- розбір ----------

        private sealed class RegistryRow
        {
            public string Number;
            public string Status;
        }

        /// <summary>Номер поправки → чи її шапка каже «ЧЕРНЕТКА».</summary>
        private static Dictionary<string, bool> ReadAmendmentHeaders()
        {
            var lines = ReadDoc("GDD_AMENDMENTS.md");
            var result = new Dictionary<string, bool>();
            for (int i = 0; i < lines.Length; i++)
            {
                var match = AmendmentHeading.Match(lines[i]);
                if (!match.Success) continue;

                // Шапка = сам заголовок + цитата-статус одразу під ним (порожні
                // рядки пропускаються). Тіло поправки не рахується: слово
                // «чернетки» в тексті затвердженої №5 статусом не є.
                var header = new List<string> { lines[i] };
                for (int j = i + 1; j < lines.Length; j++)
                {
                    string t = lines[j].Trim();
                    if (t.Length == 0) continue;
                    if (!t.StartsWith(">", StringComparison.Ordinal)) break;
                    header.Add(t);
                }

                bool isDraft = header.Any(h => h.Contains("ЧЕРНЕТКА"));
                result[match.Groups[1].Value] = isDraft;
            }
            return result;
        }

        private static List<RegistryRow> ReadRegistryRows()
        {
            var rows = new List<RegistryRow>();
            foreach (var line in SectionLines(ReadDoc("DESIGN_INDEX.md"), RegistryStart, RegistryEnd))
            {
                var match = RowNumber.Match(line);
                if (!match.Success) continue;
                var cells = line.Split('|');
                // | № | Назва | Дата | Статус | … → cells[0] порожній, статус — cells[4].
                Assert.That(cells.Length, Is.GreaterThan(5), "рядок реєстру без колонки «Статус»: " + line);
                rows.Add(new RegistryRow { Number = match.Groups[1].Value, Status = cells[4].Trim() });
            }
            return rows;
        }

        private static IEnumerable<string> SectionLines(string[] lines, string start, string end)
        {
            int from = Array.FindIndex(lines, l => l.Trim() == start);
            int to = Array.FindIndex(lines, l => l.Trim() == end);
            Assert.That(from, Is.GreaterThanOrEqualTo(0), "у DESIGN_INDEX.md немає маркера " + start);
            Assert.That(to, Is.GreaterThan(from), "у DESIGN_INDEX.md немає маркера " + end + " після " + start);
            return lines.Skip(from + 1).Take(to - from - 1);
        }

        private static string[] ReadDoc(string name)
        {
            string path = Path.Combine(Path.GetDirectoryName(Application.dataPath), "docs", name);
            Assert.That(File.Exists(path), Is.True, "не знайдено " + path);
            return File.ReadAllLines(path);
        }
    }
}
