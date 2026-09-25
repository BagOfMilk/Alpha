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
    /// Автотур (Gameplay/AutoplayGameDriver.cs) мусить грати тими самими діями,
    /// що й кнопки гри. 25.09.2026 аудит журналу механік знайшов: кнопка
    /// «Почати день» лише підтверджувала ранок, а сам день
    /// (GameSession.AdvanceDay) кликав тільки водій автотуру — людина
    /// застрягала на першому ранку, а всі тури проходили. Охоронець: кожна
    /// команда сесії, яку кличе водій, має хоч один виклик з екранів гри
    /// (Gameplay/UI і Gameplay/*.cs, крім самого автотуру). Читання-запити без
    /// побічних дій перелічені явно.
    /// </summary>
    public class AutoplayDriverGuardTests
    {
        private static readonly HashSet<string> QueriesAllowed = new HashSet<string>
        {
            // Лише показ, стану не змінює: прев'ю шансу влучання під курсором.
            "PreviewHitChance"
        };

        [Test]
        public void AutoplayDriver_UsesOnlyCommandsTheScreensAlsoUse()
        {
            string gameplay = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Assets", "_Project", "Scripts", "Gameplay");
            string driver = File.ReadAllText(Path.Combine(gameplay, "AutoplayGameDriver.cs"));

            // «Екрани» — лише те, чим грає людина: Gameplay/UI/*, оболонка і бойова
            // арена. Демо-сцени (BaseGameDemo, хроніка, VillageLife) не рахуються:
            // там є однойменні виклики інших класів (SettlementCycle.AdvanceDay).
            var screens = new List<string>();
            foreach (var f in Directory.GetFiles(Path.Combine(gameplay, "UI"), "*.cs"))
                screens.Add(File.ReadAllText(f));
            foreach (var name in new[] { "GameShell.cs", "BattleArenaController.cs", "BattleArenaView.cs", "PortraitRig.cs", "VillageStageBridge.cs" })
                screens.Add(File.ReadAllText(Path.Combine(gameplay, name)));

            // Команди — публічні методи GameSession; виклик шукаємо з будь-яким
            // отримувачем (Session.X(, s.X(, shell.Session.X().
            var sessionMethods = new HashSet<string>(typeof(Game.Core.Session.GameSession)
                .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName).Select(m => m.Name));
            var call = new Regex(@"\.([A-Z]\w+)\(");
            var driverCommands = new HashSet<string>(Commands(call, driver).Where(sessionMethods.Contains));
            var screenCommands = new HashSet<string>(screens.SelectMany(s => Commands(call, s)).Where(sessionMethods.Contains));

            var driverOnly = driverCommands.Where(c => !screenCommands.Contains(c) && !QueriesAllowed.Contains(c)).OrderBy(c => c).ToList();
            CollectionAssert.IsEmpty(driverOnly,
                "Автотур кличе команди, яких не кличе жоден екран гри — людина до них не дотягнеться: " + string.Join(", ", driverOnly));
            CollectionAssert.Contains(screenCommands, "AdvanceDay", "«Почати день» мусить проводити день (HubScreen.StartDay)");
        }

        private static HashSet<string> Commands(Regex call, string source)
        {
            // коментарі не рахуються: вони можуть згадувати виклики
            string code = Regex.Replace(source, @"//.*", "");
            return new HashSet<string>(call.Matches(code).Cast<Match>().Select(m => m.Groups[1].Value));
        }

        // =====================================================================
        // Бій v2 (docs/COMBAT_V2.md §7.4, доручення власника 25.09.2026):
        // "автотур ходить у бій ЛИШЕ через IBattleInput — ті самі методи, що
        // клік миші". PlayJournalBattleNaively — ЄДИНИЙ задокументований
        // виняток (сценарій навмисно грає ОБИДВІ сторони наївно, єдиний
        // спосіб довести defection/roster_drama/betrayal_confrontation за
        // один прогін, MechanicsJournalCompletionTests) і сам вимикає ШІ
        // презентера на свій час. Охоронці нижче звіряють, що виняток
        // лишається РІВНО там: (а) прямі виклики Session.Combat*/
        // CombatAiStepOneAction, (б) EnemyAiEnabled=false.
        // =====================================================================

        private const string NaiveJournalMethodName = "PlayJournalBattleNaively";
        private const string NaiveJournalSignature = "IEnumerable<int> " + NaiveJournalMethodName + "(";

        [Test]
        public void AutoplayDriver_CallsCombatCoreOnlyInsideNaiveJournalBattle()
        {
            string outside = SourceOutsideNaiveJournalBattle();

            var call = new Regex(@"Session\.(Combat\w*)\(");
            var offenders = call.Matches(outside).Cast<Match>().Select(m => m.Value).Distinct().OrderBy(s => s).ToList();

            CollectionAssert.IsEmpty(offenders,
                "Автотур кличе Session.Combat*/CombatAiStepOneAction напряму поза " + NaiveJournalMethodName +
                " — це рівно той завис ходу ворога, що вже трапився (докладніше — docs/COMBAT_V2.md §0/§7.4): " +
                string.Join(", ", offenders));
        }

        [Test]
        public void AutoplayDriver_SetsEnemyAiEnabledFalseOnlyInsideNaiveJournalBattle()
        {
            string stripped = StripLineComments(ReadDriverSource());
            string outside = RemoveMethodBody(stripped, NaiveJournalSignature);
            string methodBody = ExtractMethodBody(stripped, NaiveJournalSignature);

            var falseAssign = new Regex(@"EnemyAiEnabled\s*=\s*false");
            Assert.IsFalse(falseAssign.IsMatch(outside),
                "EnemyAiEnabled=false поза " + NaiveJournalMethodName +
                " — вимикати ШІ ворога дозволено лише на час наївного журнального бою.");
            Assert.IsTrue(falseAssign.IsMatch(methodBody),
                NaiveJournalMethodName + " мусить вимикати EnemyAiEnabled на час свого циклу (інакше презентер веде той самий хід одночасно з водієм).");
        }

        /// <summary>
        /// Скарга власника 25.09.2026 («коли наступив ход опонентів гра тупа
        /// зупинилась») на рівні вихідників: обидва екрани бою — 3D-презентер
        /// і IMGUI-фолбек — зобов'язані самі вести хід ворога через
        /// CombatAiStepOneAction (docs/COMBAT_V2.md §5). Раніше цей виклик був
        /// лише в автотурі, тому тур проходив, а людина застрягала.
        /// </summary>
        [Test]
        public void BattleScreens_DriveEnemyTurnThroughCombatAiStepOneAction()
        {
            string gameplay = GameplayDir();
            string arena = File.ReadAllText(Path.Combine(gameplay, "BattleArenaController.cs"));
            string fallback = File.ReadAllText(Path.Combine(gameplay, "UI", "BattleScreen.cs"));

            StringAssert.Contains("CombatAiStepOneAction", arena,
                "BattleArenaController мусить вести хід ворога через GameSession.CombatAiStepOneAction (docs/COMBAT_V2.md §5).");
            StringAssert.Contains("CombatAiStepOneAction", fallback,
                "Фолбек-екран бою (BattleScreen) мусить вести хід ворога так само (docs/COMBAT_V2.md §5).");
        }

        /// <summary>
        /// Гарячі клавіші бою (Пробіл, 1..9, O) має обробляти ОДИН шар — HUD.
        /// Коли їх ловили і контролер (Input.GetKeyDown в Update), і HUD (подія
        /// KeyDown), одне натискання спрацьовувало двічі: «1»/«O» озброювали дію
        /// й тут же знімали, Пробіл завершував хід і вмикав «Прискорити»
        /// (рев'ю зведення Бою v2, 25.09.2026). Автотур клавіш не тисне, тож
        /// ловить це лише цей охоронець.
        /// </summary>
        [Test]
        public void BattleHotkeys_HandledOnlyByHud_NotByArenaController()
        {
            string arena = StripLineComments(File.ReadAllText(Path.Combine(GameplayDir(), "BattleArenaController.cs")));
            foreach (var key in new[] { "KeyCode.Space", "KeyCode.Alpha1", "KeyCode.O)" })
                StringAssert.DoesNotContain(key, arena,
                    "BattleArenaController не обробляє " + key + " — це робить BattleHudScreen.HandleHotkeys (інакше подвійне спрацювання).");

            string hud = File.ReadAllText(Path.Combine(GameplayDir(), "UI", "BattleHudScreen.cs"));
            StringAssert.Contains("KeyCode.Space", hud, "HUD лишається власником Пробілу (кінець ходу / прискорити).");
            StringAssert.Contains("KeyCode.Alpha1", hud, "HUD лишається власником 1..9 (здібності).");
        }

        private static string GameplayDir() =>
            Path.Combine(Path.GetDirectoryName(Application.dataPath), "Assets", "_Project", "Scripts", "Gameplay");

        private static string ReadDriverSource() =>
            File.ReadAllText(Path.Combine(GameplayDir(), "AutoplayGameDriver.cs"));

        private static string StripLineComments(string source) => Regex.Replace(source, @"//.*", "");

        private static string SourceOutsideNaiveJournalBattle()
        {
            string stripped = StripLineComments(ReadDriverSource());
            return RemoveMethodBody(stripped, NaiveJournalSignature);
        }

        /// <summary>Тіло методу, знайденого за унікальним підписом (не просто ім'ям — ім'я трапляється й на місці виклику), з балансом фігурних дужок.</summary>
        private static string ExtractMethodBody(string strippedSource, string methodSignature)
        {
            int sigIdx = strippedSource.IndexOf(methodSignature, StringComparison.Ordinal);
            Assert.Greater(sigIdx, -1, "Не знайдено підпис \"" + methodSignature + "\" у AutoplayGameDriver.cs.");

            int braceStart = strippedSource.IndexOf('{', sigIdx);
            Assert.Greater(braceStart, -1, "Не знайдено тіло методу за підписом \"" + methodSignature + "\".");

            int depth = 0;
            for (int i = braceStart; i < strippedSource.Length; i++)
            {
                if (strippedSource[i] == '{') depth++;
                else if (strippedSource[i] == '}')
                {
                    depth--;
                    if (depth == 0) return strippedSource.Substring(braceStart, i - braceStart + 1);
                }
            }

            Assert.Fail("Незбалансовані фігурні дужки в тілі методу за підписом \"" + methodSignature + "\".");
            return null;
        }

        private static string RemoveMethodBody(string strippedSource, string methodSignature)
        {
            string body = ExtractMethodBody(strippedSource, methodSignature);
            return strippedSource.Replace(body, string.Empty);
        }
    }
}
