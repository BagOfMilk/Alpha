using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Game.Core.Characters.Creation;
using Game.Core.Combat;
using Game.Core.Loop;
using Game.Core.Session;
using Game.Core.Session.Bots;
using Game.Core.Session.Views;
using Game.Gameplay.Text;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Пакет E3b (docs/TEST_BUILD.md §6.2 + технічне завдання пакета): три
    /// незалежні перевірки покриття єдиної текстової таблиці
    /// (<see cref="UkrainianText"/>), яких до цього пакета НЕ існувало —
    /// таблицю писав пакет E3 проти §7 специфікації, а не проти коду, який
    /// її насправді споживає:
    ///
    /// 1. <see cref="DocsFile_EveryListedKey_HasText"/> — кожен рядок уже
    ///    зафіксованого <c>docs/TEST_BUILD_KEYS.txt</c> (пакет D2,
    ///    <c>TimingAndKeysHarnessTests.Coverage_CollectsEveryEmittedKey_...</c>)
    ///    має текст. Швидка перевірка на зафіксованому знімку — ловить регресію
    ///    без повторного прогону 5×15 діб.
    /// 2. <see cref="LiveHarness_EveryEmittedOrViewKey_HasText"/> — те саме
    ///    покриття, але зі СВІЖОГО прогону (а не зі старого файла): якщо код
    ///    почне випускати новий ключ, цей тест впаде раніше, ніж
    ///    TEST_BUILD_KEYS.txt встигне застаріти.
    /// 3. <see cref="StaticScan_EveryLiteralKeyPassedToGetOrFormat_ExistsInTable"/> —
    ///    грep по вихідникам <c>Gameplay/**/*.cs</c>: жоден рядковий літерал,
    ///    переданий у <c>UkrainianText.Get/Format</c>, не повинен бути відсутнім
    ///    у таблиці (це ж покриє ключі E1b/E2 після мержу — вони пишуть у свої
    ///    блоки того самого файлу).
    /// </summary>
    public class UkrainianTextCoverageTests
    {
        private const int RunDays = 15;

        // =====================================================================
        // 1) docs/TEST_BUILD_KEYS.txt — зафіксований знімок §6.2.
        // =====================================================================

        [Test]
        public void DocsFile_EveryListedKey_HasText()
        {
            string path = Path.Combine(RepoRoot(), "docs", "TEST_BUILD_KEYS.txt");
            Assert.IsTrue(File.Exists(path), "Не знайдено " + path + " — його пише TimingAndKeysHarnessTests.");

            var lines = File.ReadAllLines(path).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
            Assert.Greater(lines.Count, 10, "docs/TEST_BUILD_KEYS.txt виглядає порожнім/неповним.");

            var missing = lines.Where(k => !HasEitherGender(k)).ToList();
            CollectionAssert.IsEmpty(missing,
                "Ключі з docs/TEST_BUILD_KEYS.txt без тексту в UkrainianText (" + missing.Count + "): " +
                string.Join(", ", missing));
        }

        // =====================================================================
        // 2) Живий харнес: 5 бот-політик × 15 діб (5 сценарних + 10 вільних,
        //    §3.6) через GameSession/BotRunner, той самий прогін, що D2 уже
        //    ганяє для TEST_BUILD_KEYS.txt — тут його результат ЗВІРЯЄТЬСЯ з
        //    таблицею, а не просто записується у файл.
        //
        //    Джерела ключів (явно, як вимагає завдання пакета):
        //      a) GameEvent.Key — КОЖНА подія DayLog за весь прогін
        //         (команди/наслідки/сигнали — SignalComposer пише TopicId
        //         прямо як GameEvent.Key, див. GameSession.TranslateReport).
        //      b) SceneStepView.LineKey / .EffectKey / .TransitionKey —
        //         кроки AdvanceScene() (сцена відкриття, розв'язка вузла 1).
        //      c) PendingOfferView(.QuestOfferView).Options[].TextKey — лише
        //         варіанти КВЕСТУ (§4.2: "лише для варіантів квесту-вибору,
        //         ключ репліки варіанту, а не скіл") — інцидентні DecisionOptionView
        //         несуть SkillKey/BestActorId (ідентифікатори, не текстові
        //         ключі) і навмисно СЮДИ не йдуть.
        //      d) DungeonRoomView.EventOptionKeys — кімнати-події данжу
        //         (Type=="Event"); зібрано напряму з Core/Dungeons/DefaultDungeon.cs
        //         (детерміновний авторський контент, R1/R4 — не рандом), а не
        //         сподіванням, що бот-прогін відвідає саме цю кімнату: жадібна
        //         DelveGreedyPolicy штовхає лише "abandoned_camp", а
        //         "old_hermitage" (вільна гра) бот у цьому прогоні може й не
        //         відвідати.
        //
        //    НЕ перевіряються тут (свідомо, з причиною):
        //      - CompanionSummary.DisplayName / FactionSummary.DisplayName —
        //        це Core-контентний DisplayName (R7 explicit: "гравець
        //        НІКОЛИ не бачить"), не ключ; правильний виклик — char.<Id>/
        //        faction.<Id> через таблицю, а НЕ це поле. Показ цього поля
        //        напряму — порушення R7 самим викликачем, не діра в таблиці.
        //      - BattleUnitView.DisplayNameKey — за іменем мало б бути ключем,
        //        але GameSession.GetBattleView() сьогодні кладе туди
        //        u.Profile.DisplayName (сирий Core-текст, не ключ) — той самий
        //        R7-розрив, що вище, зафіксований у звіті пакета як відкрите
        //        питання (не файл цього пакета — Core/Session належить D1).
        //      - PendingOfferView.TopicId / QuestOfferView.QuestId /
        //        DecisionOptionView.SkillKey/BestActorId — ідентифікатори
        //        (наприклад "hafiya", "Persuade", companionId), а не повний
        //        ключ тексту сам по собі; префіксовані похідні ключі
        //        ("incident.<TopicId>.title" тощо) уже перевірені явним
        //        переліком §7 (UkrainianTextTests.EverySpecKey_IsPresent) і
        //        статичним сканом нижче (тест 3), коли їх реально підставляє
        //        Gameplay-код.
        // =====================================================================

        [Test]
        public void LiveHarness_EveryEmittedOrViewKey_HasText()
        {
            var collected = new HashSet<string>(StringComparer.Ordinal);

            foreach (var policy in FivePolicies())
            {
                var log = new List<GameEvent>();
                var offers = new List<PendingOfferView>();
                var viewKeys = new List<string>();
                var options = new NewGameOptions { SkipCreation = false, HitRule = HitRuleKind.Threshold, Seed = 1 };

                BotRunner.PlayDays(policy, RunDays, options, log, null, offers, viewKeys);

                foreach (var e in log)
                    if (!string.IsNullOrEmpty(e.Key)) collected.Add(e.Key);

                foreach (var k in viewKeys) // (b) SceneStepView.* — зібрано самим BotRunner.Drive
                    if (!string.IsNullOrEmpty(k)) collected.Add(k);

                foreach (var offer in offers) // (c) варіанти квесту
                {
                    if (offer?.Options == null) continue;
                    foreach (var opt in offer.Options)
                        if (!string.IsNullOrEmpty(opt.TextKey)) collected.Add(opt.TextKey);
                }
            }

            // (d) кімнати-події данжу — напряму з авторського контенту Core.
            foreach (var siteId in Game.Core.Dungeons.DefaultDungeon.KnownSiteIds)
            {
                var rooms = Game.Core.Dungeons.DefaultDungeon.Rooms(siteId);
                if (rooms == null) continue;
                foreach (var room in rooms)
                    foreach (var opt in room.EventOptions)
                        if (!string.IsNullOrEmpty(opt.LabelKey)) collected.Add(opt.LabelKey);
            }

            Assert.Greater(collected.Count, 10,
                "Живий прогін мав дати хоч якісь ключі — забагато порожньо для 5×15 діб.");

            var missing = collected.Where(k => !HasEitherGender(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
            CollectionAssert.IsEmpty(missing,
                "Ключі зі свіжого бот-прогону без тексту в UkrainianText (" + missing.Count + " з " +
                collected.Count + "): " + string.Join(", ", missing));
        }

        private static IEnumerable<IBotPolicy> FivePolicies()
        {
            yield return new StewardPolicy();
            yield return new PacifistPolicy();
            yield return new BloodyPolicy();
            yield return new PatrolAlwaysPolicy();
            yield return new DelveGreedyPolicy();
        }

        // =====================================================================
        // 3) Статичний скан Gameplay/**/*.cs: кожен РЯДКОВИЙ ЛІТЕРАЛ, переданий
        //    першим аргументом у UkrainianText.Get(...)/Format(...), існує в
        //    таблиці. Ловить літерали, набрані з друкарською помилкою, ще до
        //    того, як хтось ЗАПУСТИТЬ той код-шлях. Обчислені ключі (конкатенація/
        //    змінна першим аргументом) статично не перевіряються — це і є
        //    призначення живого харнесу (тест 2) та §7-переліку
        //    (UkrainianTextTests.EverySpecKey_IsPresent) поруч.
        // =====================================================================

        /// <summary>
        /// Ловить лише випадок, коли ПЕРШИЙ аргумент — рядковий літерал ЦІЛКОМ
        /// (за лапками одразу кома чи закрита дужка): <c>Get("key", g)</c>,
        /// не <c>Get("prefix." + x, g)</c>. Останнє — обчислений ключ, його
        /// статично не перевірити (і не треба: перший аргумент там не
        /// починається й не закінчується на межі виразу самою лапкою).
        /// </summary>
        private static readonly Regex GetOrFormatLiteral = new Regex(
            "UkrainianText\\s*\\.\\s*(?:Get|Format)\\s*\\(\\s*\"((?:[^\"\\\\]|\\\\.)*)\"\\s*[,)]",
            RegexOptions.Compiled);

        [Test]
        public void StaticScan_EveryLiteralKeyPassedToGetOrFormat_ExistsInTable()
        {
            string gameplayDir = Path.Combine(RepoRoot(), "Assets", "_Project", "Scripts", "Gameplay");
            Assert.IsTrue(Directory.Exists(gameplayDir), "Не знайдено " + gameplayDir);

            var problems = new List<string>();
            var files = Directory.GetFiles(gameplayDir, "*.cs", SearchOption.AllDirectories);
            Assert.Greater(files.Length, 0, "Не знайдено жодного .cs у " + gameplayDir);

            foreach (var file in files)
            {
                string text = File.ReadAllText(file);
                foreach (Match m in GetOrFormatLiteral.Matches(text))
                {
                    string key = Regex.Unescape(m.Groups[1].Value);
                    if (string.IsNullOrEmpty(key)) continue; // Get("") — навмисний тест заглушки, не реальний ключ.
                    if (!HasEitherGender(key))
                        problems.Add(RelativePath(file) + ": \"" + key + "\"");
                }
            }

            CollectionAssert.IsEmpty(problems,
                "Літеральні ключі UkrainianText.Get/Format без тексту в таблиці (" + problems.Count + "):\n" +
                string.Join("\n", problems));
        }

        // =====================================================================
        // Спільне.
        // =====================================================================

        /// <summary>
        /// Рід тут не відомий (тест перевіряє існування, не конкретний
        /// текст) — узгоджено із завданням пакета ("gendered .m/.f fallback
        /// allowed"): ключ вважається покритим, якщо ХОЧ ОДИН з родів дає
        /// текст (для нерозщеплених ключів обидва виклики повертають той
        /// самий базовий ключ — <see cref="UkrainianText.Has"/>).
        /// </summary>
        private static bool HasEitherGender(string key) =>
            UkrainianText.Has(key, Gender.Male) || UkrainianText.Has(key, Gender.Female);

        /// <summary>Той самий прийом, що й TimingAndKeysHarnessTests: корінь репозиторію через Application.dataPath (Unity — справжній, headless — UnityShim.cs).</summary>
        private static string RepoRoot() => Path.GetDirectoryName(Application.dataPath);

        private static string RelativePath(string fullPath)
        {
            string root = RepoRoot();
            return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                ? fullPath.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                : fullPath;
        }
    }
}
