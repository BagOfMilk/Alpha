using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Core.Ring;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Справжні дані ворожнечі (Assets/Resources/Ring): граф промо валідний, тупиків немає, кожна кінцівка досяжна
    /// (docs/ring/gdd.md §4.2, §9). Ловить правку контенту, що зламала гілку.
    /// </summary>
    public class FeudContentTests
    {
        private static string Dir() => Path.Combine(Application.dataPath, "Resources", "Ring");

        private static FeudScript Script() =>
            FeudScriptLoader.Load(File.ReadAllText(Path.Combine(Dir(), "feud_hammer.json")));

        private static Dictionary<string, string> Strings()
        {
            var obj = MiniJson.ParseObject(File.ReadAllText(Path.Combine(Dir(), "strings_uk.json")));
            return obj.ToDictionary(kv => kv.Key, kv => (string)kv.Value);
        }

        [Test]
        public void Hammer_HasThreeToFivePromos_AndPassesValidator()
        {
            var script = Script();
            Assert.That(script.Promos.Count, Is.InRange(3, 5));
            var errors = PromoValidator.Validate(script, Strings());
            Assert.IsEmpty(errors, string.Join("\n", errors.Select(e => e.ToString())));
        }

        [Test]
        public void Hammer_EveryEndingIsReachable_AndThereAreNoDeadEnds()
        {
            var report = FeudReachability.Analyze(Script());
            Assert.IsFalse(report.Truncated, "перебір обрізано: " + report.StatesVisited + " станів");
            Assert.IsEmpty(report.DeadEnds, string.Join("\n", report.DeadEnds));
            Assert.IsEmpty(report.Unreachable(), "недосяжні: " + string.Join(", ", report.Unreachable()));
        }

        [Test]
        public void Hammer_StringsHaveNoUnusedKeys_ExceptSharedOnes()
        {
            // Кожен рядок або згаданий у сценарії, або спільний (мовці, нагороди) — мертвого тексту немає.
            string json = File.ReadAllText(Path.Combine(Dir(), "feud_hammer.json"));
            // жіноча форма «ключ.f» (HeroForms) у сценарії не згадується — досить, щоб був базовий ключ
            var unused = Strings().Keys.Select(k => k.EndsWith(".f") ? k.Substring(0, k.Length - 2) : k)
                .Where(k => !json.Contains("\"" + k + "\"") && !k.StartsWith("speaker.")).ToList();
            Assert.IsEmpty(unused, string.Join(", ", unused));
        }
    }
}
