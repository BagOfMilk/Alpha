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
    }
}
