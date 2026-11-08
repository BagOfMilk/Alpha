using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Game.Core.Ring.Match;
using Game.Gameplay.Ring.Models;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Таблиця «подія → звук» (критерій М1, ПМ 08.11.2026: «таблиця «подія → звук» без порожніх подій (удар, гонг, рахунок, натовп,
    /// QTE, вихід)»): кожне значення переліків подій і дій ядра має звук, кожен звук — слоти бібліотеки, слоти — справжні файли
    /// або нові слоти бібліотеки звуку, документ таблиці не відстає. Імпорт звуку рингу — моно (пакет П4).
    /// </summary>
    public class RingSoundMapTests
    {
        private static string Root() { return Path.GetDirectoryName(Application.dataPath); }

        [Test]
        public void EveryEventKind_HasASound()
        {
            var empty = new List<string>();
            foreach (MatchEventKind k in Enum.GetValues(typeof(MatchEventKind)))
                foreach (MatchActionId a in Enum.GetValues(typeof(MatchActionId)))
                    if (RingSoundMap.ForEvent(k, a) == RingCue.None) empty.Add(k + "/" + a);
            Assert.IsEmpty(empty, "події без звуку:\n" + string.Join("\n", empty));
        }

        [Test]
        public void EveryAction_HasASound()
        {
            var empty = Enum.GetValues(typeof(MatchActionId)).Cast<MatchActionId>().Where(a => RingSoundMap.ForAction(a) == RingCue.None).ToList();
            Assert.IsEmpty(empty, "дії без звуку: " + string.Join(", ", empty));
        }

        [Test]
        public void EveryCue_PlaysFromLibrarySlots()
        {
            foreach (var c in RingSoundMap.AllCues())
                Assert.Greater(RingSoundMap.SlotsOf(c).Count, 0, "звук без слотів бібліотеки: " + c);
            Assert.AreEqual(0, RingSoundMap.SlotsOf(RingCue.None).Count);
        }

        [Test]
        public void M1_RequiredEvents_HaveSounds()
        {
            // удар, гонг (дзвін рингу), рахунок рефері, натовп, QTE, вихід борця
            Assert.AreEqual(RingCue.Hit, RingSoundMap.ForAction(MatchActionId.Strike), "удар");
            Assert.AreEqual(RingCue.BellEnd, RingSoundMap.ForEvent(MatchEventKind.MatchEnd, MatchActionId.Pin), "дзвін кінця");
            Assert.AreEqual(RingCue.MatSlap, RingSoundMap.ForEvent(MatchEventKind.Kickout, MatchActionId.Pin), "рахунок — долоня рефері");
            Assert.AreEqual(RingCue.CrowdBed, RingSoundMap.ForEvent(MatchEventKind.TurnEnd, MatchActionId.CatchBreath), "натовп — гул залу");
            foreach (var c in new[] { RingCue.BellStart, RingCue.BellEnd, RingCue.MatSlap, RingCue.CrowdBed, RingCue.Pop, RingCue.QtePrompt,
                         RingCue.QteHit, RingCue.QteMiss, RingCue.Entrance, RingCue.Hit })
                Assert.Greater(RingSoundMap.SlotsOf(c).Count, 0, c.ToString());
        }

        [Test]
        public void Flourish_IsAQteHit_ByName()
        {
            // Переробка бою (claude/ring-combat-overhaul) додає подію Flourish — вдале QTE; таблиця ключована назвами, тож готова до злиття.
            Assert.IsTrue(RingSoundMap.HasKind("Flourish"));
            foreach (MatchEventKind k in Enum.GetValues(typeof(MatchEventKind)))
                Assert.IsTrue(RingSoundMap.HasKind(k.ToString()), "вид події без рядка таблиці: " + k);
        }

        [Test]
        public void Sell_DoesNotRepeatTheHit()
        {
            // Продаж іде ДО кидка прийому: удар звучить на самій дії, а не двічі (і не до контакту).
            Assert.AreEqual(RingCue.CrowdBed, RingSoundMap.ForEvent(MatchEventKind.Sell, MatchActionId.Strike));
            Assert.AreEqual(RingCue.Hit, RingSoundMap.ForEvent(MatchEventKind.Action, MatchActionId.Strike));
        }

        private static HashSet<string> SceneClips()
        {
            var set = new HashSet<string>();
            string dir = Path.Combine(Root(), "Assets", "ThirdParty", "CC0", "Audio", "Ring");
            foreach (var f in Directory.GetFiles(dir))
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext == ".wav" || ext == ".ogg" || ext == ".mp3") set.Add(Path.GetFileNameWithoutExtension(f));
            }
            return set;
        }

        [Test]
        public void Slots_AreSceneClipsOrNewLibrarySlots()
        {
            var scene = SceneClips();
            string builder = File.ReadAllText(Path.Combine(Root(), "tools", "ring_audio", "library", "build_library.py"));
            var bad = new List<string>();
            foreach (var c in RingSoundMap.AllCues())
                foreach (var s in RingSoundMap.SlotsOf(c))
                {
                    bool isNew = RingSoundMap.NewSlots.Contains(s);
                    if (scene.Contains(s) == isNew) bad.Add(s + (isNew ? " — новий слот, але файл є в наборі сцени" : " — немає файлу в наборі сцени"));
                    if (!builder.Contains("\"" + s + "\"") && !builder.Contains("'" + s + "'")) bad.Add(s + " — слота немає в бібліотеці звуку (build_library.py)");
                }
            foreach (var s in RingSoundMap.NewSlots)
                if (!RingSoundMap.AllCues().Any(c => RingSoundMap.SlotsOf(c).Contains(s))) bad.Add(s + " — новий слот не грає жодна подія");
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        [Test]
        public void EventTableDocument_ListsEveryRow()
        {
            string doc = File.ReadAllText(Path.Combine(Root(), "docs", "ring", "audio", "EVENT_SOUND.md"));
            var missing = new List<string>();
            foreach (MatchEventKind k in Enum.GetValues(typeof(MatchEventKind)))
                if (!doc.Contains("`" + k + "`")) missing.Add("подія " + k);
            foreach (MatchActionId a in Enum.GetValues(typeof(MatchActionId)))
                if (!doc.Contains("`" + a + "`")) missing.Add("дія " + a);
            foreach (var c in RingSoundMap.AllCues())
                foreach (var s in RingSoundMap.SlotsOf(c))
                    if (!doc.Contains(s)) missing.Add("слот " + s);
            Assert.IsEmpty(missing, "docs/ring/audio/EVENT_SOUND.md відстає від RingSoundMap: " + string.Join(", ", missing));
        }

        private static readonly Regex Field = new Regex(@"^\s*(\w+): ([^\r\n]*)", RegexOptions.Multiline);

        private static Dictionary<string, string> Meta(string path)
        {
            var d = new Dictionary<string, string>();
            foreach (Match m in Field.Matches(File.ReadAllText(path)))
                if (!d.ContainsKey(m.Groups[1].Value)) d[m.Groups[1].Value] = m.Groups[2].Value.Trim();
            return d;
        }

        private static string Get(Dictionary<string, string> m, string key) { string v; return m.TryGetValue(key, out v) ? v : null; }

        [Test]
        public void RingSound_IsImportedMono()
        {
            var bad = new List<string>();
            int n = 0;
            foreach (var rel in new[] { "Assets/ThirdParty/CC0/Audio/Ring", "Assets/Art/Ring/Sound" })
            {
                string dir = Path.Combine(Root(), rel);
                if (!Directory.Exists(dir)) continue;
                foreach (var f in Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories))
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext != ".wav" && ext != ".ogg" && ext != ".mp3") continue;
                    n++;
                    string name = Path.GetFileNameWithoutExtension(f);
                    if (!File.Exists(f + ".meta")) { bad.Add(name + ": немає .meta"); continue; }
                    var m = Meta(f + ".meta");
                    bool music = name.StartsWith("music_"), bed = name == "crowd_shouting" || name == "applause_hall";
                    string load = music ? "2" : bed ? "1" : "0";
                    if (Get(m, "forceToMono") != "1") bad.Add(name + ": не моно");
                    if (Get(m, "loadType") != load) bad.Add(name + ": loadType " + Get(m, "loadType") + " замість " + load);
                    if (Get(m, "compressionFormat") != "1") bad.Add(name + ": не Vorbis");
                    if (!music && Get(m, "preloadAudioData") != "1") bad.Add(name + ": короткий звук без попереднього завантаження (затримка на першому контакті)");
                }
            }
            Assert.GreaterOrEqual(n, 20, "звук рингу знайдено");
            Assert.IsEmpty(bad, "імпорт звуку рингу розійшовся з Editor/RingSoundImport:\n" + string.Join("\n", bad));
            string src = File.ReadAllText(Path.Combine(Root(), "Assets", "_Project", "Scripts", "Editor", "RingSoundImport.cs"));
            StringAssert.Contains("forceToMono = true", src, "правило імпорту рингу — моно");
        }
    }
}
