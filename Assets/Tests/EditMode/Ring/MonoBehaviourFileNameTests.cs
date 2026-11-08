using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Охоронець: кожен MonoBehaviour / ScriptableObject / StateMachineBehaviour проєкту лежить у файлі зі своєю назвою.
    /// Інакше Unity не знаходить для класу скрипт (MonoScript береться з назви файлу), кладе в сцену вбудоване посилання без
    /// guid — і зібраний Ring.exe падає на старті: «level0 is corrupted… [Position out of bounds!]» (08.11.2026: клас
    /// RingMenuAssets жив у RingMenuAudio.cs; збірки то працювали, то падали — залежно від того, який клас Unity прив'язав).
    /// Нащадків власних базових класів теж перевіряємо (транзитивно). Текстовий розбір, тож вкладені класи не підтримує —
    /// компонент сцени вкладеним бути й не може.
    /// </summary>
    public class MonoBehaviourFileNameTests
    {
        private static readonly string[] UnityBases = { "MonoBehaviour", "ScriptableObject", "StateMachineBehaviour" };
        private static readonly Regex ClassDecl = new Regex(@"\bclass\s+(\w+)\s*(?:<[^>{]*>)?\s*:\s*([\w.]+)");

        [Test]
        public void EveryUnityObjectClass_LivesInAFileWithItsOwnName()
        {
            string scripts = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Assets", "_Project", "Scripts");
            var classes = new List<KeyValuePair<string, string>>();      // клас → файл
            var bases = new Dictionary<string, string>();                 // клас → базовий
            foreach (var file in Directory.GetFiles(scripts, "*.cs", SearchOption.AllDirectories))
                foreach (Match m in ClassDecl.Matches(File.ReadAllText(file)))
                {
                    string cls = m.Groups[1].Value, b = m.Groups[2].Value;
                    int dot = b.LastIndexOf('.');
                    if (dot >= 0) b = b.Substring(dot + 1);
                    classes.Add(new KeyValuePair<string, string>(cls, file));
                    if (!bases.ContainsKey(cls)) bases[cls] = b;
                }
            Assert.Greater(classes.Count, 100, "скрипти проєкту знайдено");

            // partial-клас (RingGameBootstrap.cs + RingGameBootstrap.Menus.cs) — норма: Unity досить, щоб ОДИН із файлів мав назву класу
            var files = new Dictionary<string, List<string>>();
            foreach (var kv in classes)
            {
                List<string> l;
                if (!files.TryGetValue(kv.Key, out l)) files[kv.Key] = l = new List<string>();
                l.Add(kv.Value);
            }
            var problems = new List<string>();
            int unityObjects = 0;
            foreach (var kv in files)
            {
                if (!IsUnityObject(kv.Key, bases)) continue;
                unityObjects++;
                bool own = false;
                foreach (var f in kv.Value) if (Path.GetFileNameWithoutExtension(f) == kv.Key) own = true;
                if (!own) problems.Add(kv.Key + " — у " + kv.Value[0].Substring(scripts.Length + 1).Replace('\\', '/'));
            }
            Assert.Greater(unityObjects, 10, "компоненти знайдено");
            Assert.IsEmpty(problems, "MonoBehaviour/ScriptableObject не у файлі зі своєю назвою (Ring.exe впаде на level0):\n" + string.Join("\n", problems));
        }

        private static bool IsUnityObject(string cls, Dictionary<string, string> bases)
        {
            var seen = new HashSet<string>();
            string c = cls;
            while (c != null && seen.Add(c))
            {
                string b;
                if (!bases.TryGetValue(c, out b)) return false;
                if (System.Array.IndexOf(UnityBases, b) >= 0) return true;
                c = b;
            }
            return false;
        }
    }
}
