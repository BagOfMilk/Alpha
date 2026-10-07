using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Game.Core.Characters.Creation;
using Game.Gameplay.Text;
using Game.Gameplay.UI;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Звук (Поправка №18.4, віха M1.19): кожен такт має файл, кожен файл — рядок ліцензії; події, на які
    /// чіпляється звук, справді є в журналі ядра; режисер звуку не читає Напруги (інваріант 3).
    /// </summary>
    public class SoundCueTableTests
    {
        private static string AudioRoot => Path.Combine(Application.dataPath, "ThirdParty", "CC0", "Audio");

        private static HashSet<string> Manifest() =>
            new HashSet<string>(File.ReadAllLines(Path.Combine(AudioRoot, "audio_manifest.txt")).Where(l => l.Length > 0 && l[0] != '#'));

        [Test]
        public void EveryCue_MusicAndAmbience_PointToFilesInTheManifest()
        {
            var m = Manifest();
            foreach (var cue in SoundCueTable.AllCues())
            {
                Assert.IsNotEmpty(SoundCueTable.FilesFor(cue), cue.ToString());
                foreach (var f in SoundCueTable.FilesFor(cue)) Assert.IsTrue(m.Contains(f), cue + ": " + f);
            }
            foreach (var t in new[] { MusicTrack.VillageDay, MusicTrack.VillageEvening, MusicTrack.Battle })
                Assert.IsTrue(m.Contains(SoundCueTable.FileOf(t)), t.ToString());
            foreach (var b in new[] { AmbienceBed.Day, AmbienceBed.Night })
                Assert.IsTrue(m.Contains(SoundCueTable.FileOf(b)), b.ToString());
            Assert.IsTrue(m.Contains(SoundCueTable.FireLoop));
        }

        [Test]
        public void EveryFileFolder_HasALicenseLine()
        {
            string licenses = File.ReadAllText(Path.Combine(AudioRoot, "LICENSES.md"));
            StringAssert.Contains("CC0", licenses);
            foreach (var f in Manifest())
            {
                string probe = f.StartsWith("Kenney/") ? "`Kenney/*`" : Path.GetFileName(f);
                StringAssert.Contains(probe, licenses, "немає рядка ліцензії: " + f);
            }
        }

        [Test]
        public void EventKeysWithSound_ExistInTheCoreJournal()
        {
            string core = Path.Combine(Application.dataPath, "_Project", "Scripts", "Core");
            var keys = new HashSet<string>();
            foreach (var file in Directory.GetFiles(core, "*.cs", SearchOption.AllDirectories))
                foreach (Match m in Regex.Matches(File.ReadAllText(file), @"LogEvent\(""([a-z_.]+)"""))
                    keys.Add(m.Groups[1].Value);
            string table = File.ReadAllText(Path.Combine(Application.dataPath, "_Project", "Scripts", "Gameplay", "UI", "Models", "SoundCueTable.cs"));
            foreach (Match m in Regex.Matches(table, "case \"([a-z_.]+)\":"))
                Assert.IsTrue(keys.Contains(m.Groups[1].Value), "подія без джерела в ядрі: " + m.Groups[1].Value);
            Assert.AreEqual(SoundCue.Anvil, SoundCueTable.ForEvent("forge.made"));
            Assert.AreEqual(SoundCue.None, SoundCueTable.ForEvent("nonexistent.key"));
        }

        [Test]
        public void Music_FollowsTheGameState()
        {
            Assert.AreEqual(MusicTrack.Battle, SoundCueTable.MusicFor(true, false, false));
            Assert.AreEqual(MusicTrack.VillageEvening, SoundCueTable.MusicFor(false, true, false));
            Assert.AreEqual(MusicTrack.VillageDay, SoundCueTable.MusicFor(false, false, false));
            Assert.AreEqual(AmbienceBed.Night, SoundCueTable.AmbienceFor(false, true, false));
        }

        [Test]
        public void Volumes_ClampAndStep_TextsExist()
        {
            float before = SoundSettings.Get(SoundBus.Music);
            SoundSettings.Set(SoundBus.Music, 5f);
            Assert.AreEqual(1f, SoundSettings.Get(SoundBus.Music));
            SoundSettings.Nudge(SoundBus.Music, -1);
            Assert.AreEqual(90, SoundSettings.Percent(SoundBus.Music));
            SoundSettings.Set(SoundBus.Music, before);
            foreach (SoundBus b in System.Enum.GetValues(typeof(SoundBus)))
                Assert.IsTrue(UkrainianText.Has(SoundSettings.TextKey(b), Gender.Male), b.ToString());
        }

        [Test]
        public void SoundCode_NeverReadsTension()
        {
            string g = Path.Combine(Application.dataPath, "_Project", "Scripts", "Gameplay");
            foreach (var f in new[] { Path.Combine(g, "Audio", "AudioDirector.cs"), Path.Combine(g, "UI", "Models", "SoundCueTable.cs") })
            {
                string code = File.ReadAllText(f);
                StringAssert.DoesNotContain("Tension", code, f);
                StringAssert.DoesNotContain("Pressure", code, f);
            }
        }

        [Test]
        public void PickedVariant_IsDeterministic_AndCyclesThroughFiles()
        {
            var seen = new HashSet<string>();
            for (int i = 0; i < 6; i++) seen.Add(SoundCueTable.Pick(SoundCue.FootstepGrass, i));
            Assert.AreEqual(5, seen.Count);
            Assert.AreEqual(SoundCueTable.Pick(SoundCue.UiClick, 7), SoundCueTable.Pick(SoundCue.UiClick, 7));
        }
    }
}
