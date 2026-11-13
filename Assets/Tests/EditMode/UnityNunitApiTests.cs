using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Тести компілює й Unity, а там NUnit 3.5 (пакет com.unity.ext.nunit 2.0.3). Headless-прогін іде на NUnit 3.14
    /// (tools/Game.Tests.Headless), тож новіше API там компілюється, а в Unity — CS0117, і batchmode падає ще до збірки.
    /// Так сталося 13.11.2026: `Is.AnyOf` у GimmickRulesTests зупинив слот 32 «Меню». Охоронець ловить це без Unity.
    /// Список — лише API, якого точно немає в 3.5; натрапили на нове — додати сюди.
    /// </summary>
    public class UnityNunitApiTests
    {
        private static readonly KeyValuePair<string, string>[] NotInNunit35 =
        {
            Pair(@"\bIs\.AnyOf\s*\(", "Is.AnyOf (NUnit 3.12) → Has.Member: Assert.That(new[] { a, b }, Has.Member(x))"),
            Pair(@"\bAssert\.Multiple\s*\(", "Assert.Multiple (NUnit 3.6) → окремі Assert"),
            Pair(@"\bAssert\.EnterMultipleScope\s*\(", "Assert.EnterMultipleScope (NUnit 4.2) → окремі Assert"),
            Pair(@"\bAssert\.ThatAsync\s*\(", "Assert.ThatAsync (NUnit 4) → синхронний Assert.That"),
            Pair(@"\bAssert\.Warn\s*\(|\bWarn\.(If|Unless)\s*\(", "Assert.Warn / Warn.If (NUnit 3.6) → Assert або лог"),
            Pair(@"\bHas\.ItemAt\s*\(", "Has.ItemAt (NUnit 3.13) → індекс і Assert.AreEqual"),
        };

        private static KeyValuePair<string, string> Pair(string pattern, string hint) { return new KeyValuePair<string, string>(pattern, hint); }

        [Test]
        public void Tests_UseOnlyTheNunitApiThatUnityShips()
        {
            string tests = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Assets", "Tests");
            var files = Directory.GetFiles(tests, "*.cs", SearchOption.AllDirectories)
                .Where(f => Path.GetFileName(f) != nameof(UnityNunitApiTests) + ".cs").ToArray();
            Assert.Greater(files.Length, 100, "тести знайдено: " + tests);
            var bad = new List<string>();
            foreach (var f in files)
            {
                var lines = File.ReadAllLines(f);
                for (int i = 0; i < lines.Length; i++)
                {
                    string code = lines[i];
                    int comment = code.IndexOf("//", System.StringComparison.Ordinal);
                    if (comment >= 0) code = code.Substring(0, comment);
                    foreach (var p in NotInNunit35)
                        if (Regex.IsMatch(code, p.Key))
                            bad.Add(Path.GetFileName(f) + ":" + (i + 1) + " — " + p.Value);
                }
            }
            Assert.IsEmpty(bad, "API, якого немає в NUnit 3.5 Unity:\n" + string.Join("\n", bad));
        }
    }
}
