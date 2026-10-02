using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// <c>EveryFlagHasAReader</c> (ROADMAP M1.1): кожен сюжетний прапор, який гра ВИСТАВЛЯЄ, має мати читача —
    /// код, що через нього міняє поведінку. Прапор без читача — це «вибір без наслідку»: гравець вирішив, гра
    /// записала, і більше нічого (так жили <c>myroslava_trusted</c>, <c>zakhar_prepared_assault</c> та ін.;
    /// мета M1 — «кожен вибір першої години дає видимий наслідок»).
    ///
    /// Охоронець СТАТИЧНИЙ: сканує текст продакшн-коду (<c>Assets/_Project/Scripts</c>, без тестів), бо читач
    /// може сидіти на гілці, яку жоден автопрогін не пройде (Мирослава ПІШЛА / ЗАЛИШИЛАСЬ). Два простори
    /// прапорів, які НЕ перетинаються навіть при збігу id: <c>story</c> (<c>StoryFlags</c>: <c>.Flag(...)</c>,
    /// <c>flags.Set</c>, <c>flagsToSet:</c> → <c>flags.Get</c>, <c>GateFlag</c>, <c>BlockFlag</c>) і <c>arc</c>
    /// (<c>_arcFlags</c> глав арок: <c>SetsFlag</c> → <c>NeedsFlag</c>).
    ///
    /// Відомі прогалини — у <see cref="KnownUnread"/> (ратчет): новий прапор без читача валить тест; прапор, що
    /// ЗНАЙШОВ читача, теж валить його, доки його не приберуть зі списку, — прогалини не застоюються мовчки.
    /// Закриття прогалин — ROADMAP M1.2 (споживач + тест «прапор змінює X», або зняти прапор).
    /// Сам сканер перевіряється на синтетичному коді, тож охоронець має зуби незалежно від реального вмісту.
    /// </summary>
    public class StoryFlagReaderGuardTests
    {
        // ---------- відомі прогалини (M1.2) ----------

        /// <summary>«простір:id» (для динамічних родин — «простір:префікс*»). ПЛЕЙСХОЛДЕР-борг, а не норма.</summary>
        private static readonly HashSet<string> KnownUnread = new HashSet<string>(StringComparer.Ordinal)
        {
            // --- вибори сцен без наслідку (M1.2: споживач — репліки напарників/епілог/підсумок — або зняти) ---
            "story:myroslava_trusted", "story:myroslava_watched", "story:myroslava_sent_away",   // вибір «лишитися» (CompanionScenes)
            "story:myroslava_asked", "story:myroslava_hint",                                      // розмова з Мирославою у відкритті (OpeningScenes)
            "story:myroslava_ch2_remember", "story:myroslava_ch2_silence",                        // глава 2 Мирослави
            "story:myroslava_checkup_reassure", "story:myroslava_checkup_space",                  // перевірка стану
            "story:maksym_ch1_revenge_clean",                                                     // Максим, глава 1 (DefaultQuests)
            "story:maksym_ch2_forgive", "story:maksym_ch2_guard",                                 // Максим, глава 2
            "story:tugar_offer_refused",                                                          // відмова Тугарові (фракції вже зсунуто напряму)
            "story:abandoned_camp_grain_taken",                                                   // данж «Покинутий табір»
            "arc:arc_myroslava_done", "arc:arc_maksym_done",                                      // завершення особистих арок
            // --- службові позначки (прогрес/пам'ять; читач не потрібен, але тоді їх варто зняти) ---
            "story:pass_vanguard_resolved", "story:tugar_offer_seen", "story:first_building.*",
        };

        /// <summary>
        /// Місця, де прапор передається ЗМІННОЮ (загальна «труба»), а не id: сканер не може їх розкрити, і це нормально —
        /// конкретні id приходять через <c>GateFlag("…")</c>/<c>BlockFlag("…")</c>/<c>.Flag("…")</c>. Нове нерозкрите місце
        /// (хтось сховав id за змінною) валить тест: id має бути літералом або константою.
        /// </summary>
        private static readonly HashSet<string> UnresolvedPlumbing = new HashSet<string>(StringComparer.Ordinal)
        {
            "Core/Quests/QuestStage.cs|RequiresFlag",     // QuestOption.IsAvailable: id приходить через GateFlag("…")
            "Core/Quests/QuestStage.cs|BlockedByFlag",    // … через BlockFlag("…")
            "Core/Session/GameSession.cs|flag",           // ApplyConsequence/DungeonConsequence: id приходять із .Flag("…")/flagsToSet
        };

        // ---------- охоронці над реальним кодом ----------

        [Test]
        public void EveryWrittenFlag_HasAReader_OrIsAKnownGap()
        {
            var scan = FlagScan.Run(ProductionSources());
            var unread = FlagScan.Unread(scan);

            var fresh = unread.Where(k => !KnownUnread.Contains(k)).ToList();
            Assert.IsEmpty(fresh,
                "Прапори виставляються, але їх ніхто не читає — вибір без наслідку (ROADMAP M1.1/M1.2). " +
                "Додайте читача (споживача) або заведіть у KnownUnread з номером задачі:\n  " + string.Join("\n  ", fresh));
        }

        [Test]
        public void KnownGaps_AreStillGaps_NoStaleEntries()
        {
            var scan = FlagScan.Run(ProductionSources());
            var unread = new HashSet<string>(FlagScan.Unread(scan), StringComparer.Ordinal);

            var stale = KnownUnread.Where(k => !unread.Contains(k)).ToList();
            Assert.IsEmpty(stale,
                "У KnownUnread лишились прапори, що вже мають читача або більше не виставляються — приберіть зі списку " +
                "(прогалину закрито):\n  " + string.Join("\n  ", stale));
        }

        [Test]
        public void FlagIds_AreNeverHiddenBehindVariables_ExceptKnownPlumbing()
        {
            var scan = FlagScan.Run(ProductionSources());
            var sites = scan.Unresolved.Select(u => u.File + "|" + u.Arg).Distinct().ToList();

            var fresh = sites.Where(s => !UnresolvedPlumbing.Contains(s)).ToList();
            Assert.IsEmpty(fresh,
                "Прапор передано змінною або виразом, який сканер не розкриває — охоронець його не бачить. " +
                "Передайте літерал чи const string (або внесіть «трубу» в UnresolvedPlumbing):\n  " + string.Join("\n  ", fresh));

            var stale = UnresolvedPlumbing.Where(s => !sites.Contains(s)).ToList();
            Assert.IsEmpty(stale, "UnresolvedPlumbing містить місця, яких уже немає:\n  " + string.Join("\n  ", stale));
        }

        [Test]
        public void EveryReadFlag_IsWrittenSomewhere_NoTypos()
        {
            var orphans = FlagScan.Orphans(FlagScan.Run(ProductionSources()));
            Assert.IsEmpty(orphans,
                "Прапор читається, але ніхто його не виставляє — одруківка в id або мертва умова:\n  " + string.Join("\n  ", orphans));
        }

        [Test]
        public void FlagConstants_AreNotDeclaredTwiceWithDifferentValues()
        {
            var ambiguous = FlagScan.Run(ProductionSources()).Ambiguous;
            Assert.IsEmpty(ambiguous,
                "Константа-id прапора оголошена з різними значеннями: сканер злив би їх в одну, а дрейф лишився б невидимим:\n  " + string.Join("\n  ", ambiguous));
        }

        [Test]
        public void StoryFlagsHolders_AreAllKnownToTheScanner()
        {
            // Сканер впізнає читання/запис лише через змінні _flags / flags / Flags. Нова змінна типу StoryFlags під іншим
            // іменем лишилась би невидимою — тож перелічуємо всі держателі і вимагаємо, щоб вони були в цьому списку.
            var known = new HashSet<string>(StringComparer.Ordinal) { "_flags", "flags", "Flags" };
            var holder = new Regex(@"\bStoryFlags\??\s+(?<name>[A-Za-z_]\w*)\s*(?:[;=,){]|$)", RegexOptions.Multiline);
            var unknown = new List<string>();
            foreach (var (file, text) in ProductionSources())
                foreach (Match m in holder.Matches(text))
                {
                    string name = m.Groups["name"].Value;
                    if (!known.Contains(name)) unknown.Add(file + ": " + name);
                }
            Assert.IsEmpty(unknown,
                "Знайдено держателя StoryFlags під ім'ям, якого не знає сканер (додайте його в Calls у FlagScan і сюди):\n  " + string.Join("\n  ", unknown));
        }

        [Test]
        public void RealCode_Scan_FindsTheChannelsItShouldFind_SanityCheck()
        {
            // Якщо сканер раптом нічого не бачить (змінилась форма виклику), решта охоронців мовчки зеленіють.
            var scan = FlagScan.Run(ProductionSources());
            Assert.GreaterOrEqual(scan.Writes.Count(w => w.Ns == "story"), 15, "записів story-прапорів мало — сканер зламався?");
            Assert.GreaterOrEqual(scan.Reads.Count(r => r.Ns == "story"), 10, "читань story-прапорів мало — сканер зламався?");
            Assert.GreaterOrEqual(scan.Writes.Count(w => w.Ns == "arc"), 4, "записів arc-прапорів мало");
            Assert.GreaterOrEqual(scan.Reads.Count(r => r.Ns == "arc"), 2, "читань arc-прапорів мало");
            Assert.IsTrue(scan.Writes.Any(w => w.Value == "zakhar_prepared_assault"));
            Assert.IsTrue(scan.Reads.Any(r => r.Value == "zakhar_prepared_assault"),
                "ZakharPreparedAssaultFlag читає BuildFinalePlan (Поправка №17.2, B2)");
        }

        // ---------- охоронець сканера: синтетичний код ----------

        [Test]
        public void Scanner_FlagsAWriteWithoutAReader_AndClearsItWhenAReaderAppears()
        {
            var writeOnly = FlagScan.Run(new[] { ("A.cs", "x = new QuestConsequence().Flag(\"lonely\");") });
            CollectionAssert.AreEqual(new[] { "story:lonely" }, FlagScan.Unread(writeOnly));

            var withReader = FlagScan.Run(new[]
            {
                ("A.cs", "x = new QuestConsequence().Flag(\"lonely\");"),
                ("B.cs", "if (_flags.Get(\"lonely\")) { }")
            });
            CollectionAssert.IsEmpty(FlagScan.Unread(withReader));
        }

        [Test]
        public void Scanner_ResolvesConstants_AcrossFiles()
        {
            var scan = FlagScan.Run(new[]
            {
                ("Consts.cs", "public const string BigFlag = \"big\";"),
                ("A.cs", "c.Flag(Consts.BigFlag);"),
                ("B.cs", "bool b = flags.Get(BigFlag);")
            });
            CollectionAssert.IsEmpty(FlagScan.Unread(scan), "константа резолвиться з будь-якого файлу, за останнім ідентифікатором");
            CollectionAssert.IsEmpty(scan.Unresolved);
        }

        [Test]
        public void Scanner_KeepsStoryAndArcNamespacesApart()
        {
            // Той самий id у двох просторах: читач в arc НЕ рятує запис у story.
            var scan = FlagScan.Run(new[]
            {
                ("A.cs", "q.Flag(\"shared\");"),
                ("B.cs", "ch.NeedsFlag(\"shared\");")
            });
            CollectionAssert.AreEqual(new[] { "story:shared" }, FlagScan.Unread(scan));

            var arc = FlagScan.Run(new[]
            {
                ("A.cs", "ch.SetsFlag(\"arc_x\");"),
                ("B.cs", "ch2.NeedsFlag(\"arc_x\");")
            });
            CollectionAssert.IsEmpty(FlagScan.Unread(arc));
        }

        [Test]
        public void Scanner_HandlesDynamicFamilies_ByPrefix()
        {
            var unread = FlagScan.Run(new[]
            {
                ("C.cs", "public const string P = \"fam.\";"),
                ("A.cs", "q.Flag(P + id);")
            });
            CollectionAssert.AreEqual(new[] { "story:fam.*" }, FlagScan.Unread(unread));

            var read = FlagScan.Run(new[]
            {
                ("C.cs", "public const string P = \"fam.\";"),
                ("A.cs", "q.Flag(P + id);"),
                ("B.cs", "if (flags.Get(P + id)) { }")
            });
            CollectionAssert.IsEmpty(FlagScan.Unread(read));

            var readByLiteral = FlagScan.Run(new[]
            {
                ("C.cs", "public const string P = \"fam.\";"),
                ("A.cs", "q.Flag(P + id);"),
                ("B.cs", "if (flags.Get(\"fam.watch\")) { }")
            });
            CollectionAssert.IsEmpty(FlagScan.Unread(readByLiteral), "читання конкретного члена родини закриває родину");
        }

        [Test]
        public void Scanner_SeesDungeonOptionFlags_AndGateAndBlockReaders()
        {
            var scan = FlagScan.Run(new[]
            {
                ("D.cs", "new DungeonOption(\"k\", flagsToSet: new[] { \"grain\", \"torn\" });"),
                ("Q.cs", "opt.GateFlag(\"grain\"); opt2.BlockFlag(\"torn\");")
            });
            CollectionAssert.IsEmpty(FlagScan.Unread(scan));
        }

        [Test]
        public void Scanner_IgnoresCommentsAndStringsThatLookLikeCalls()
        {
            var scan = FlagScan.Run(new[]
            {
                ("A.cs", "// q.Flag(\"ghost\");\n/* flags.Get(\"ghost\") */ q.Flag(\"real\"); var s = \"flags.Get(\\\"fake\\\")\";"),
            });
            Assert.IsFalse(scan.Reads.Any(), "коментарі й рядки не читачі");
            Assert.IsFalse(scan.Writes.Any(w => w.Value == "ghost"), "закоментований запис не рахується");
            CollectionAssert.AreEqual(new[] { "story:real" }, FlagScan.Unread(scan));
        }

        [Test]
        public void Scanner_FindsReadsOfNeverWrittenFlags_AsTypoOrphans()
        {
            var typo = FlagScan.Run(new[]
            {
                ("A.cs", "q.Flag(\"myroslava_trusted\");"),
                ("B.cs", "if (_flags.Get(\"myroslava_trust\")) { }")
            });
            CollectionAssert.AreEqual(new[] { "story:myroslava_trust" }, FlagScan.Orphans(typo));
            CollectionAssert.AreEqual(new[] { "story:myroslava_trusted" }, FlagScan.Unread(typo), "а справжній запис лишився без читача");

            var ok = FlagScan.Run(new[] { ("A.cs", "q.Flag(\"x\");"), ("B.cs", "flags.Get(\"x\");") });
            CollectionAssert.IsEmpty(FlagScan.Orphans(ok));
        }

        [Test]
        public void Scanner_FlagsAConstantDeclaredTwiceWithDifferentValues()
        {
            var scan = FlagScan.Run(new[]
            {
                ("A.cs", "public const string SeedFlag = \"seeded\";"),
                ("B.cs", "public const string SeedFlag = \"seeded_v2\";"),
                ("C.cs", "flags.Set(SeedFlag);")
            });
            CollectionAssert.AreEqual(new[] { "SeedFlag" }, scan.Ambiguous);

            var same = FlagScan.Run(new[]
            {
                ("A.cs", "public const string SeedFlag = \"seeded\";"),
                ("B.cs", "public const string SeedFlag = \"seeded\";"),
                ("C.cs", "flags.Set(SeedFlag);")
            });
            CollectionAssert.IsEmpty(same.Ambiguous, "однакові значення — не дрейф");
        }

        [Test]
        public void Scanner_ReportsVariableArguments_AsUnresolved()
        {
            var scan = FlagScan.Run(new[] { ("A.cs", "flags.Set(someVariable); flags.Get(other.Member());") });
            Assert.AreEqual(2, scan.Unresolved.Count);
        }

        // ---------- таблиця для аудиту (M1.1): запускається вручну ----------

        [Test, Explicit("Друкує таблицю «прапор → хто пише → хто читає» для docs/STORY_FLAGS.md")]
        public void PrintFlagTable()
        {
            var scan = FlagScan.Run(ProductionSources());
            var sb = new StringBuilder();
            sb.AppendLine("| Простір | Прапор | Пишуть | Читають |");
            sb.AppendLine("|---|---|---|---|");
            var keys = scan.Writes.Select(w => w.Ns + "|" + (w.IsPrefix ? w.Value + "*" : w.Value)).Distinct().OrderBy(k => k, StringComparer.Ordinal);
            var unread = new HashSet<string>(FlagScan.Unread(scan), StringComparer.Ordinal);
            foreach (var key in keys)
            {
                var parts = key.Split('|');
                string ns = parts[0], id = parts[1];
                var writers = scan.Writes.Where(w => w.Ns == ns && (w.IsPrefix ? w.Value + "*" : w.Value) == id).Select(w => w.Site).Distinct();
                var readers = scan.Reads.Where(r => r.Ns == ns && FlagScan.Matches(id, r)).Select(r => r.Site).Distinct();
                string status = unread.Contains(ns + ":" + id) ? "**НЕМАЄ**" : string.Join("; ", readers);
                sb.AppendLine("| " + ns + " | `" + id + "` | " + string.Join("; ", writers) + " | " + status + " |");
            }
            sb.AppendLine();
            sb.AppendLine("Нерозкриті місця (труба): " + string.Join(", ", scan.Unresolved.Select(u => u.File + "|" + u.Arg).Distinct()));
            Console.WriteLine("=====FLAGTABLE=====" + Environment.NewLine + sb + "=====END=====");
        }

        // ---------- вхідні файли ----------

        private static List<(string File, string Text)> ProductionSources()
        {
            string root = Path.Combine(Application.dataPath, "_Project", "Scripts");
            Assert.IsTrue(Directory.Exists(root), "не знайдено " + root);
            return Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
                .Select(p => (Rel(root, p), File.ReadAllText(p)))
                .ToList();
        }

        private static string Rel(string root, string path) => path.Substring(root.Length).TrimStart('\\', '/').Replace('\\', '/');
    }

    /// <summary>Статичний сканер прапорів по тексту C# (без Roslyn): достатньо для форм викликів, які вживає проєкт.</summary>
    internal static class FlagScan
    {
        internal sealed class Use
        {
            public string Ns;
            public string Value;
            public bool IsPrefix;
            public string Site;
        }

        internal sealed class UnresolvedUse
        {
            public string File;
            public string Arg;
        }

        internal sealed class Result
        {
            public readonly List<Use> Writes = new List<Use>();
            public readonly List<Use> Reads = new List<Use>();
            public readonly List<UnresolvedUse> Unresolved = new List<UnresolvedUse>();
            /// <summary>Імена констант, що використані як id прапора, але оголошені з РІЗНИМИ значеннями в різних місцях.</summary>
            public readonly List<string> Ambiguous = new List<string>();
        }

        // (регулярний вираз початку виклику, простір, чи запис)
        private static readonly (Regex Rx, string Ns, bool Write)[] Calls =
        {
            (new Regex(@"\.\s*Flag\s*\(", RegexOptions.CultureInvariant), "story", true),
            (new Regex(@"\b(?:_flags|flags|Flags)\s*\.\s*Set\s*\(", RegexOptions.CultureInvariant), "story", true),
            (new Regex(@"\b(?:_flags|flags|Flags)\s*\.\s*Get\s*\(", RegexOptions.CultureInvariant), "story", false),
            (new Regex(@"\.\s*GateFlag\s*\(", RegexOptions.CultureInvariant), "story", false),
            (new Regex(@"\.\s*BlockFlag\s*\(", RegexOptions.CultureInvariant), "story", false),
            (new Regex(@"\.\s*SetsFlag\s*\(", RegexOptions.CultureInvariant), "arc", true),
            (new Regex(@"\.\s*NeedsFlag\s*\(", RegexOptions.CultureInvariant), "arc", false),
        };

        private static readonly Regex FlagsToSetRx = new Regex(@"\bflagsToSet\s*:\s*new\s*(?:string)?\s*\[\s*\]\s*\{(?<list>[^}]*)\}", RegexOptions.CultureInvariant);
        private static readonly Regex ConstRx = new Regex(@"\bconst\s+string\s+(?<name>\w+)\s*=\s*""(?<v>(?:\\.|[^""\\])*)""", RegexOptions.CultureInvariant);
        private static readonly Regex LiteralRx = new Regex(@"^""(?<v>(?:\\.|[^""\\])*)""$", RegexOptions.CultureInvariant);
        private static readonly Regex IdentPathRx = new Regex(@"^[A-Za-z_]\w*(?:\s*\.\s*[A-Za-z_]\w*)*$", RegexOptions.CultureInvariant);

        internal static Result Run(IEnumerable<(string File, string Text)> files)
        {
            var sources = files.Select(f => (f.File, Text: Strip(f.Text))).ToList();

            var consts = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var (file, text) in sources)
                foreach (Match m in ConstRx.Matches(text))
                {
                    string name = m.Groups["name"].Value;
                    if (!consts.TryGetValue(name, out var set)) consts[name] = set = new HashSet<string>(StringComparer.Ordinal);
                    set.Add(Unescape(m.Groups["v"].Value));
                }

            var result = new Result();
            foreach (var (file, text) in sources)
            {
                // Виклики шукаємо в тексті з ЗАМАСКОВАНИМИ рядковими літералами (щоб «flags.Get(...)» усередині рядка не
                // рахувався викликом), а аргумент читаємо з оригіналу за тими самими зсувами (довжина збережена).
                string masked = MaskStrings(text);
                foreach (var (rx, ns, write) in Calls)
                    foreach (Match m in rx.Matches(masked))
                    {
                        int argStart = m.Index + m.Length;
                        string arg = ReadArg(text, argStart);
                        string site = file + ":" + LineOf(text, m.Index);
                        Record(result, consts, ns, write, arg, site, file);
                    }

                foreach (Match m in FlagsToSetRx.Matches(masked))
                {
                    string site = file + ":" + LineOf(text, m.Index);
                    string list = text.Substring(m.Groups["list"].Index, m.Groups["list"].Length);
                    foreach (string item in SplitTopLevel(list))
                        Record(result, consts, "story", true, item, site, file);
                }
            }
            return result;
        }

        private static void Record(Result result, Dictionary<string, HashSet<string>> consts, string ns, bool write, string arg, string site, string file)
        {
            if (string.IsNullOrWhiteSpace(arg)) return;
            arg = arg.Trim();

            var target = write ? result.Writes : result.Reads;
            var lit = LiteralRx.Match(arg);
            if (lit.Success)
            {
                target.Add(new Use { Ns = ns, Value = Unescape(lit.Groups["v"].Value), Site = site });
                return;
            }

            if (IdentPathRx.IsMatch(arg))
            {
                string last = arg.Split('.').Last().Trim();
                if (consts.TryGetValue(last, out var values))
                {
                    if (values.Count > 1 && !result.Ambiguous.Contains(last)) result.Ambiguous.Add(last);
                    foreach (var v in values) target.Add(new Use { Ns = ns, Value = v, Site = site });
                    return;
                }
            }
            else
            {
                // «Префікс + щось»: динамічна родина за відомим префіксом.
                int plus = TopLevelPlus(arg);
                if (plus > 0)
                {
                    string head = arg.Substring(0, plus).Trim();
                    string prefix = null;
                    var l2 = LiteralRx.Match(head);
                    if (l2.Success) prefix = Unescape(l2.Groups["v"].Value);
                    else if (IdentPathRx.IsMatch(head) && consts.TryGetValue(head.Split('.').Last().Trim(), out var pv) && pv.Count == 1)
                        prefix = pv.First();
                    if (!string.IsNullOrEmpty(prefix))
                    {
                        target.Add(new Use { Ns = ns, Value = prefix, IsPrefix = true, Site = site });
                        return;
                    }
                }
            }

            result.Unresolved.Add(new UnresolvedUse { File = file, Arg = arg });
        }

        /// <summary>Ключі «простір:id» (для родин — «простір:префікс*») записаних прапорів, яких ніхто не читає.</summary>
        internal static List<string> Unread(Result r)
        {
            var written = r.Writes
                .Select(w => (w.Ns, Id: w.IsPrefix ? w.Value + "*" : w.Value, w.IsPrefix, w.Value))
                .Distinct()
                .ToList();

            var unread = new List<string>();
            foreach (var w in written)
            {
                bool read = r.Reads.Any(x => x.Ns == w.Ns && Matches(w.Id, x));
                if (!read) unread.Add(w.Ns + ":" + w.Id);
            }
            unread.Sort(StringComparer.Ordinal);
            return unread;
        }

        /// <summary>Читання прапорів, які ніхто не виставляє (одруківка в id читача чи записаного прапора): «простір:id».</summary>
        internal static List<string> Orphans(Result r)
        {
            var orphans = new List<string>();
            foreach (var read in r.Reads.Select(x => (x.Ns, x.Value, x.IsPrefix)).Distinct())
            {
                bool written = r.Writes.Any(w => w.Ns == read.Ns && WriteMatchesRead(w, read.Value, read.IsPrefix));
                if (!written) orphans.Add(read.Ns + ":" + read.Value + (read.IsPrefix ? "*" : ""));
            }
            orphans.Sort(StringComparer.Ordinal);
            return orphans;
        }

        private static bool WriteMatchesRead(Use write, string readValue, bool readIsPrefix)
        {
            if (!write.IsPrefix) return readIsPrefix ? write.Value.StartsWith(readValue, StringComparison.Ordinal) : write.Value == readValue;
            return readIsPrefix
                ? write.Value.StartsWith(readValue, StringComparison.Ordinal) || readValue.StartsWith(write.Value, StringComparison.Ordinal)
                : readValue.StartsWith(write.Value, StringComparison.Ordinal);
        }

        /// <summary>Чи це читання закриває запис <paramref name="writtenId"/> (точний id або родина «префікс*»).</summary>
        internal static bool Matches(string writtenId, Use read)
        {
            bool writtenIsFamily = writtenId.EndsWith("*", StringComparison.Ordinal);
            string w = writtenIsFamily ? writtenId.Substring(0, writtenId.Length - 1) : writtenId;

            if (!read.IsPrefix)
                return writtenIsFamily ? read.Value.StartsWith(w, StringComparison.Ordinal) : read.Value == w;

            // читання родини: закриває точні id з цим префіксом і споріднені родини
            return writtenIsFamily
                ? read.Value.StartsWith(w, StringComparison.Ordinal) || w.StartsWith(read.Value, StringComparison.Ordinal)
                : w.StartsWith(read.Value, StringComparison.Ordinal);
        }

        // ---------- розбір тексту ----------

        private static readonly Regex StripRx = new Regex(
            @"'(?:\\.|[^'\\\n])'|""(?:\\.|[^""\\\n])*""|//[^\n]*|/\*.*?\*/", RegexOptions.Singleline | RegexOptions.CultureInvariant);

        /// <summary>Прибирає коментарі (зберігаючи рядки для номерів) і ЗАМІНЮЄ вміст рядкових літералів ТІЛЬКИ в коментарях — літерали лишаються.</summary>
        private static string Strip(string source)
        {
            return StripRx.Replace(source, m =>
            {
                string v = m.Value;
                if (v.StartsWith("//", StringComparison.Ordinal) || v.StartsWith("/*", StringComparison.Ordinal))
                    return new string('\n', v.Count(c => c == '\n'));
                return v;
            });
        }

        private static readonly Regex StringLiteralRx = new Regex("\"(?:\\\\.|[^\"\\\\\\n])*\"", RegexOptions.CultureInvariant);

        /// <summary>Замінює вміст рядкових літералів на «x» тієї самої довжини (лапки лишаються) — для пошуку викликів.</summary>
        private static string MaskStrings(string source)
        {
            return StringLiteralRx.Replace(source, m => "\"" + new string('x', m.Length - 2) + "\"");
        }

        /// <summary>Читає аргумент виклику від позиції після «(» до коми чи закриваючої дужки на нульовій глибині.</summary>
        private static string ReadArg(string text, int start)
        {
            int depth = 0;
            bool inStr = false;
            for (int i = start; i < text.Length; i++)
            {
                char c = text[i];
                if (inStr)
                {
                    if (c == '\\') i++;
                    else if (c == '"') inStr = false;
                    continue;
                }
                if (c == '"') { inStr = true; continue; }
                if (c == '(' || c == '{' || c == '[') depth++;
                else if (c == ')' || c == '}' || c == ']')
                {
                    if (depth == 0) return text.Substring(start, i - start).Trim();
                    depth--;
                }
                else if (c == ',' && depth == 0) return text.Substring(start, i - start).Trim();
            }
            return null;
        }

        private static IEnumerable<string> SplitTopLevel(string list)
        {
            int depth = 0, from = 0;
            bool inStr = false;
            for (int i = 0; i < list.Length; i++)
            {
                char c = list[i];
                if (inStr) { if (c == '\\') i++; else if (c == '"') inStr = false; continue; }
                if (c == '"') { inStr = true; continue; }
                if (c == '(' || c == '{' || c == '[') depth++;
                else if (c == ')' || c == '}' || c == ']') depth--;
                else if (c == ',' && depth == 0) { yield return list.Substring(from, i - from); from = i + 1; }
            }
            if (from < list.Length) yield return list.Substring(from);
        }

        private static int TopLevelPlus(string expr)
        {
            int depth = 0;
            bool inStr = false;
            for (int i = 0; i < expr.Length; i++)
            {
                char c = expr[i];
                if (inStr) { if (c == '\\') i++; else if (c == '"') inStr = false; continue; }
                if (c == '"') { inStr = true; continue; }
                if (c == '(' || c == '[' || c == '{') depth++;
                else if (c == ')' || c == ']' || c == '}') depth--;
                else if (c == '+' && depth == 0) return i;
            }
            return -1;
        }

        private static int LineOf(string text, int index)
        {
            int line = 1;
            for (int i = 0; i < index && i < text.Length; i++) if (text[i] == '\n') line++;
            return line;
        }

        private static string Unescape(string s) => s.Replace("\\\"", "\"").Replace("\\\\", "\\");
    }
}
