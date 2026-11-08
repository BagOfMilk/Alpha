using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Game.Gameplay.Ring.Models;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Звук лише зі схвалення власника (08.11.2026: «Выключи все звуки пока я не скажу обратного из всех будущих сборок…
    /// тогда они идут в игру»): модель списку, сам список у Resources і охоронець «кожен файл, що грає звук, ходить через
    /// ворота RingSoundGate» — новий програвач в обхід воріт зробить тест червоним.
    /// </summary>
    public class RingSoundApprovalTests
    {
        private static string Root() { return Path.GetDirectoryName(Application.dataPath); }

        [Test]
        public void EmptyList_SilencesEverything()
        {
            var a = RingSoundApproval.Parse("{\"approved\": []}");
            Assert.AreEqual(0, a.Count);
            Assert.AreEqual(0f, a.ListenerVolume, "нічого не схвалено — слухач вимкнений");
            Assert.IsFalse(a.Allows("music_login"));
            Assert.IsFalse(a.Allows(""));
            Assert.IsFalse(a.Allows(null));
        }

        [Test]
        public void ApprovedNames_PlayExactly()
        {
            var a = RingSoundApproval.Parse("{\"about\": \"x\", \"approved\": [\"bell_01\", \" ui_move \", \"\", 7]}");
            Assert.AreEqual(2, a.Count);
            Assert.AreEqual(1f, a.ListenerVolume);
            Assert.IsTrue(a.Allows("bell_01"));
            Assert.IsTrue(a.Allows("ui_move"), "пробіли навколо назви не заважають");
            Assert.IsFalse(a.Allows("bell_02"));
            Assert.IsFalse(a.Allows("Bell_01"), "назва кліпу — точна, з урахуванням регістру");
        }

        [Test]
        public void BrokenFile_MeansSilence()
        {
            foreach (var bad in new[] { null, "", "{", "[]", "{\"approved\": \"bell_01\"}", "{\"other\": [\"bell_01\"]}" })
            {
                var a = RingSoundApproval.Parse(bad);
                Assert.AreEqual(0, a.Count, "зіпсований список — тиша: " + (bad ?? "null"));
                Assert.AreEqual(0f, a.ListenerVolume);
            }
        }

        [Test]
        public void RepoList_ParsesAndNamesOnlyRealClips()
        {
            string path = Path.Combine(Root(), "Assets", "Resources", "Ring", "sound_approvals.json");
            Assert.IsTrue(File.Exists(path), "список схвалень є в Resources: " + path);
            string json = File.ReadAllText(path);
            var o = Game.Core.Ring.MiniJson.ParseObject(json);
            Assert.IsTrue(o.ContainsKey("approved"), "поле approved на місці (інакше гра мовчить завжди)");
            var a = RingSoundApproval.Parse(json);
            var clips = new HashSet<string>();
            foreach (var dir in new[] { Path.Combine(Root(), "Assets", "Art"), Path.Combine(Root(), "Assets", "ThirdParty", "CC0", "Audio") })
                if (Directory.Exists(dir))
                    foreach (var f in Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories))
                    {
                        string ext = Path.GetExtension(f).ToLowerInvariant();
                        if (ext == ".wav" || ext == ".ogg" || ext == ".mp3") clips.Add(Path.GetFileNameWithoutExtension(f));
                    }
            Assert.Greater(clips.Count, 30, "звукові файли гри знайдено");
            var unknown = a.Approved.Where(n => !clips.Contains(n)).ToList();
            Assert.IsEmpty(unknown, "у списку схвалень — назви без файлу (одрук?): " + string.Join(", ", unknown));
        }

        private static readonly Regex Playback = new Regex(@"\.(Play|PlayOneShot|PlayDelayed|PlayScheduled)\s*\(|PlayClipAtPoint\s*\(");

        [Test]
        public void EveryFileThatPlaysSound_GoesThroughTheGate()
        {
            string scripts = Path.Combine(Root(), "Assets", "_Project", "Scripts");
            var players = new List<string>();
            var bypass = new List<string>();
            foreach (var f in Directory.GetFiles(scripts, "*.cs", SearchOption.AllDirectories))
            {
                string src = File.ReadAllText(f);
                // звук грає лише AudioSource / AudioSource.PlayClipAtPoint; анімації (AnimationClip, PlayOneShot кліпу постаті) — ні
                if (!src.Contains("AudioSource") || !Playback.IsMatch(src)) continue;
                string rel = f.Substring(scripts.Length + 1).Replace('\\', '/');
                if (rel.Contains("/Editor/") || rel.StartsWith("Editor/")) continue;   // редактор не звучить у збірці
                players.Add(rel);
                if (!src.Contains("RingSoundGate.Allows")) bypass.Add(rel);
            }
            Assert.GreaterOrEqual(players.Count, 3, "програвачі звуку знайдено: " + string.Join(", ", players));
            Assert.IsEmpty(bypass, "файли грають звук повз ворота RingSoundGate.Allows (звук без схвалення власника):\n"
                + string.Join("\n", bypass));
        }
    }
}
