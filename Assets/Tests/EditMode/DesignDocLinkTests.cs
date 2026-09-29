using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Охоронець зв'язку «код ↔ статут дизайну» (docs/DESIGN_CHARTER.md, §0).
    /// Посилання на принцип, якого немає, — гірше за відсутність посилання:
    /// читач іде по сліду і не знаходить нічого. Так уже було з «REFS» —
    /// коментарі посилалися на документ, якого в репозиторії не існувало.
    /// </summary>
    public class DesignDocLinkTests
    {
        // Складено з частин, щоб рядок-зразок у цьому файлі сам себе не ловив.
        private static readonly Regex CharterRef =
            new Regex("Статут" + @"\s+((?:MECH|UI|CONT|PROC|ANTI)-\d{2})");

        private static readonly Regex CharterAnchor =
            new Regex(@"<a id=""((?:MECH|UI|CONT|PROC|ANTI)-\d{2})""></a>");

        private static readonly Regex DocsLink =
            new Regex(@"docs/[A-Za-z0-9_\-/.]+?\.md");

        private static string RepoRoot() => Path.GetDirectoryName(Application.dataPath);

        private static string CharterPath() => Path.Combine(RepoRoot(), "docs", "DESIGN_CHARTER.md");

        private static HashSet<string> CharterIds()
        {
            string path = CharterPath();
            Assert.IsTrue(File.Exists(path), "Немає статуту: " + path);
            return new HashSet<string>(
                CharterAnchor.Matches(File.ReadAllText(path)).Cast<Match>().Select(m => m.Groups[1].Value));
        }

        /// <summary>(а) Токен «REFS» у скриптах гри — посилання в нікуди.</summary>
        [Test]
        public void Scripts_DoNotReferenceMissingRefsDocument()
        {
            var token = new Regex(@"\b" + "RE" + "FS" + @"\b");
            string dir = Path.Combine(RepoRoot(), "Assets", "_Project", "Scripts");
            var offenders = Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories)
                .Where(f => token.IsMatch(File.ReadAllText(f)))
                .Select(Path.GetFileName)
                .ToList();

            Assert.IsEmpty(offenders,
                "Посилання на відсутній документ; пишіть ID статуту: " + string.Join(", ", offenders));
        }

        /// <summary>Статут має якорі, і вони не повторюються.</summary>
        [Test]
        public void Charter_AnchorsExist_AndAreUnique()
        {
            string text = File.ReadAllText(CharterPath());
            var all = CharterAnchor.Matches(text).Cast<Match>().Select(m => m.Groups[1].Value).ToList();

            CollectionAssert.Contains(all, "PROC-01", "Процедура рішення — серце статуту");
            var dupes = all.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Assert.IsEmpty(dupes, "ID статуту повторюються: " + string.Join(", ", dupes));
        }

        /// <summary>
        /// (б) Кожне «Статут XX-nn» у скриптах, тестах і CLAUDE.md має якір у статуті.
        /// </summary>
        [Test]
        public void EveryCharterReference_ResolvesToAnAnchor()
        {
            var ids = CharterIds();
            var sources = new List<string>();
            sources.AddRange(Directory.GetFiles(Path.Combine(RepoRoot(), "Assets", "_Project", "Scripts"),
                "*.cs", SearchOption.AllDirectories));
            sources.AddRange(Directory.GetFiles(Path.Combine(RepoRoot(), "Assets", "Tests"),
                "*.cs", SearchOption.AllDirectories));
            sources.Add(Path.Combine(RepoRoot(), "CLAUDE.md"));

            int seen = 0;
            var broken = new List<string>();
            foreach (var file in sources.Where(File.Exists))
                foreach (Match m in CharterRef.Matches(File.ReadAllText(file)))
                {
                    seen++;
                    if (!ids.Contains(m.Groups[1].Value))
                        broken.Add(Path.GetFileName(file) + " → " + m.Groups[1].Value);
                }

            // Коментарі AlphaSkin / UkrainianText / BattleArenaView посилаються на UI-01..03:
            // якщо регулярка раптом нічого не бачить, тест не має проходити «порожньо».
            Assert.GreaterOrEqual(seen, 3, "Охоронець не знайшов жодного посилання на статут — зламана регулярка?");
            Assert.IsEmpty(broken, "Посилання на неіснуючий принцип статуту: " + string.Join("; ", broken));
        }

        /// <summary>(в) Кожен шлях «docs/….md» у CLAUDE.md і статуті веде на існуючий файл.</summary>
        [TestCase("CLAUDE.md")]
        [TestCase("docs/DESIGN_CHARTER.md")]
        public void EveryDocsLink_PointsToAnExistingFile(string relative)
        {
            string path = Path.Combine(RepoRoot(), relative);
            Assert.IsTrue(File.Exists(path), "Немає файлу: " + path);

            var links = DocsLink.Matches(File.ReadAllText(path)).Cast<Match>()
                .Select(m => m.Value).Distinct().ToList();
            Assert.IsNotEmpty(links, relative + ": жодного посилання на docs — зламана регулярка?");

            var missing = links
                .Where(l => !File.Exists(Path.Combine(RepoRoot(), l.Replace('/', Path.DirectorySeparatorChar))))
                .ToList();
            Assert.IsEmpty(missing, relative + " посилається на відсутні файли: " + string.Join(", ", missing));
        }
    }
}
