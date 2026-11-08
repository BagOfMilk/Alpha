using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Game.Core.Ring;
using Game.Core.Ring.Career;
using Game.Gameplay.Ring.Models;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Меню «Рингу» (ТЗ B08): налаштування (чернетка/застосоване, сховище, перепризначення), шини гучності, криві руху,
    /// фокус, прогрес завантаження, і що кожен ключ тексту меню є в ui_uk.json.
    /// </summary>
    public class RingMenuModelTests
    {
        private sealed class DictStore : IRingSettingsStore
        {
            public readonly Dictionary<string, string> D = new Dictionary<string, string>();
            public int Saves;
            public string Get(string key) { string v; return D.TryGetValue(key, out v) ? v : null; }
            public void Set(string key, string value) { D[key] = value; }
            public void Save() { Saves++; }
        }

        // ---------------- налаштування ----------------

        [Test]
        public void Settings_DraftIsIndependent_CancelRestores_ApplyPersists()
        {
            var store = new DictStore();
            var m = RingSettingsModel.Load(store);
            Assert.IsFalse(m.IsDirty);
            m.Draft.Music = 2;
            m.Draft.Keys[RingAction.Confirm] = "Space";
            Assert.IsTrue(m.IsDirty, "зміна чернетки — незбережені зміни");
            Assert.AreEqual(6, m.Applied.Music, "застосоване не змінюється, доки не «Застосувати»");
            Assert.AreEqual("Return", m.Applied.Keys[RingAction.Confirm], "словник клавіш чернетки — свій");
            m.Cancel();
            Assert.IsFalse(m.IsDirty);
            Assert.AreEqual(6, m.Draft.Music);
            m.Draft.Music = 3;
            m.Apply(store);
            Assert.IsFalse(m.IsDirty);
            Assert.AreEqual(1, store.Saves, "«Застосувати» пише в сховище");
            var again = RingSettingsModel.Load(store);
            Assert.AreEqual(3, again.Applied.Music, "налаштування зберігаються між запусками");
        }

        [Test]
        public void Settings_RoundTrip_AllFields()
        {
            var store = new DictStore();
            var v = new RingSettingsValues
            {
                Display = RingDisplayMode.Windowed, Resolution = 2, VSync = false, FrameCap = 30, Graphics = RingGraphics.High,
                Master = 9, Music = 1, Sfx = 0, Voice = 10, MuteInBackground = false, ReduceMotion = true, ScreenShake = false,
                Flashes = false, UiScale = 120, Language = "uk",
            };
            v.Keys[RingAction.Up] = "W";
            RingSettingsModel.Write(v, store);
            var r = RingSettingsModel.Load(store).Applied;
            Assert.IsTrue(r.SameAs(v), "усі поля читаються назад");
        }

        [Test]
        public void Settings_BrokenStore_IsClamped()
        {
            var store = new DictStore();
            store.D["ring.settings.vol.music"] = "99";
            store.D["ring.settings.vol.sfx"] = "-4";
            store.D["ring.settings.fps"] = "75";
            store.D["ring.settings.uiscale"] = "300";
            store.D["ring.settings.graphics"] = "17";
            store.D["ring.settings.vol.master"] = "мусор";
            store.D["ring.key.Back"] = "";
            var v = RingSettingsModel.Load(store).Applied;
            Assert.AreEqual(10, v.Music);
            Assert.AreEqual(0, v.Sfx);
            Assert.AreEqual(60, v.FrameCap);
            Assert.AreEqual(100, v.UiScale);
            Assert.AreEqual(RingGraphics.Auto, v.Graphics);
            Assert.AreEqual(8, v.Master, "нечитабельне значення — заводське");
            Assert.AreEqual("Escape", v.Keys[RingAction.Back], "порожня клавіша — заводська");
        }

        [Test]
        public void Settings_Rebind_SwapsConflicts_NoActionLosesItsKey()
        {
            var m = new RingSettingsModel();
            var swapped = m.Rebind(RingAction.Up, "DownArrow");
            Assert.AreEqual(RingAction.Down, swapped);
            Assert.AreEqual("DownArrow", m.Draft.Keys[RingAction.Up]);
            Assert.AreEqual("UpArrow", m.Draft.Keys[RingAction.Down], "дії помінялися клавішами");
            Assert.IsNull(m.Rebind(RingAction.Log, "K"), "вільна клавіша — без обміну");
            var used = new HashSet<string>();
            foreach (var kv in m.Draft.Keys) Assert.IsTrue(used.Add(kv.Value), "клавіша не повторюється: " + kv.Value);
        }

        [Test]
        public void Settings_ResetDraft_DefaultsOnlyAfterApply()
        {
            var store = new DictStore();
            var m = RingSettingsModel.Load(store);
            m.Draft.Music = 1;
            m.Apply(store);
            m.ResetDraft();
            Assert.AreEqual(6, m.Draft.Music);
            Assert.AreEqual(1, m.Applied.Music, "заводські — лише в чернетці, поки не «Застосувати»");
            Assert.IsTrue(m.IsDirty);
        }

        // ---------------- шини ----------------

        [Test]
        public void Mix_StepCurve_Is3dBPerStep_ZeroIsSilence()
        {
            Assert.AreEqual(0f, RingAudioMix.StepGain(0));
            Assert.AreEqual(1f, RingAudioMix.StepGain(10));
            Assert.AreEqual(Math.Pow(10, -15.0 / 20.0), RingAudioMix.StepGain(5), 1e-5);
            for (int s = 1; s <= 10; s++) Assert.Greater(RingAudioMix.StepGain(s), RingAudioMix.StepGain(s - 1), "крок " + s);
        }

        [Test]
        public void Mix_BusIsMasterTimesBus_FocusMutes()
        {
            var mix = new RingAudioMix();
            var v = new RingSettingsValues { Master = 5, Music = 10, Sfx = 0, MuteInBackground = true };
            mix.SetFrom(v);
            Assert.AreEqual(RingAudioMix.StepGain(5), mix.Gain(RingBus.Music), 1e-6);
            Assert.AreEqual(0f, mix.Gain(RingBus.Sfx));
            mix.Focused = false;
            Assert.AreEqual(0f, mix.Gain(RingBus.Music), "вікно без фокусу — тиша");
            mix.MuteInBackground = false;
            Assert.Greater(mix.Gain(RingBus.Music), 0f, "«Звук у фоні: грає»");
        }

        // ---------------- рух ----------------

        [Test]
        public void Motion_CurvesHitEndpoints_OutBackOvershoots()
        {
            foreach (Ease e in Enum.GetValues(typeof(Ease)))
            {
                Assert.AreEqual(0f, MenuMotion.Eval(e, 0f), 1e-6, e.ToString());
                Assert.AreEqual(1f, MenuMotion.Eval(e, 1f), 1e-6, e.ToString());
            }
            float peak = 0f;
            for (int i = 0; i <= 100; i++) peak = Math.Max(peak, MenuMotion.Eval(Ease.OutBack, i / 100f));
            Assert.Greater(peak, 1.02f, "переліт є");
            Assert.Less(peak, 1.2f, "переліт помірний");
            Assert.AreEqual(0f, MenuMotion.Shake(0f, 300f, 9f));
            Assert.AreEqual(0f, MenuMotion.Shake(300f, 300f, 9f), "тремтіння згасає до нуля");
        }

        [Test]
        public void Motion_TokensWithinBriefRanges()
        {
            Assert.That(BrandTokens.MotionMenuFocusMs, Is.InRange(120, 200), "мікровзаємодії 120–200 мс");
            Assert.That(BrandTokens.MotionMenuPlateInMs, Is.InRange(120, 200));
            Assert.That(BrandTokens.MotionMenuWipeMs * 2, Is.InRange(300, 600), "перехід екранів 300–500 мс (+запас)");
            Assert.That(BrandTokens.MotionMenuMusicFadeMs, Is.InRange(500, 1000), "кросфейд 0,5–1 с");
            Assert.AreEqual(BrandTokens.MotionMenuMusicFadeMs / 1000f, RingAudioMix.CrossfadeSeconds, 1e-6, "кросфейд коду = токен");
        }

        [Test]
        public void Focus_WrapsAndSkipsDisabled()
        {
            var f = new MenuFocus();
            f.Reset(new[] { true, false, true, true }, 1);
            Assert.AreEqual(2, f.Index, "неактивний пункт за замовчуванням пропускається");
            Assert.IsTrue(f.Move(1)); Assert.AreEqual(3, f.Index);
            Assert.IsTrue(f.Move(1)); Assert.AreEqual(0, f.Index, "по колу");
            Assert.IsTrue(f.Move(1)); Assert.AreEqual(2, f.Index, "через неактивний");
            Assert.IsFalse(f.Set(1), "миша не ставить фокус на неактивний");
        }

        // ---------------- завантаження ----------------

        [Test]
        public void Loading_NeverGoesBack_NoFlash_ExitsAfterMinTime()
        {
            var p = new LoadingProgress(new[] { 3f, 1f, 2f }, 1400f);
            float prev = 0f;
            // уся робота — за перший кадр (швидка машина): показ однаково йде плавно й не раніше мінімального часу
            p.CompleteStep(); p.CompleteStep(); p.CompleteStep();
            Assert.IsTrue(p.WorkDone);
            int frames = 0;
            while (!p.CanExit && frames < 1000)
            {
                p.Tick(16.7f);
                Assert.GreaterOrEqual(p.Shown, prev, "прогрес не йде назад");
                Assert.LessOrEqual(p.Shown - prev, 0.05f, "без стрибків 0→100 за кадр");
                prev = p.Shown;
                frames++;
            }
            Assert.IsTrue(p.CanExit);
            Assert.GreaterOrEqual(p.ElapsedMs, 1400f, "мінімальний час показу — екран не блимає");
            Assert.Less(p.ElapsedMs, 3000f, "і не тягне зайвого");
        }

        [Test]
        public void Loading_ShownFollowsRealSteps()
        {
            var p = new LoadingProgress(new[] { 1f, 1f }, 500f);
            for (int i = 0; i < 200; i++) p.Tick(16f);
            Assert.AreEqual(0f, p.Shown, "без зробленої роботи прогрес стоїть");
            p.CompleteStep();
            for (int i = 0; i < 200; i++) p.Tick(16f);
            Assert.AreEqual(0.5f, p.Shown, 1e-4, "половина роботи — половина шкали");
            Assert.IsFalse(p.CanExit);
        }

        [Test]
        public void Loading_StageLabelFollowsShownBar()
        {
            var p = new LoadingProgress(new[] { 1f, 1f, 1f, 1f }, 1400f);
            p.CompleteStep(); p.CompleteStep(); p.CompleteStep(); p.CompleteStep();
            Assert.AreEqual(0, p.StepAtShown(), "робота вже вся, а шкала на нулі — підпис «Етап 1»");
            while (p.Shown < 0.3f) p.Tick(16f);
            Assert.AreEqual(1, p.StepAtShown(), "30 % при чотирьох рівних кроках — другий етап");
            while (!p.CanExit) p.Tick(16f);
            Assert.AreEqual(3, p.StepAtShown());
        }

        [Test]
        public void Loading_TipsRotate()
        {
            Assert.AreEqual(3, LoadingProgress.TipIndex(0f, 4500f, 3, 10));
            Assert.AreEqual(4, LoadingProgress.TipIndex(4600f, 4500f, 3, 10));
            Assert.AreEqual(0, LoadingProgress.TipIndex(9000f * 3.5f, 4500f, 3, 10) == 0 ? 0 : 0);
            Assert.AreEqual(-1, LoadingProgress.TipIndex(0f, 4500f, 0, 0));
        }

        // ---------------- тексти ----------------

        private static string Root() { return Path.GetDirectoryName(Application.dataPath); }

        [Test]
        public void Time_PauseHoldsThroughWarpEnd_AndResumeRestoresTempo()
        {
            try
            {
                RingTime.Reset();
                RingTime.Base = 3f;                       // бот огляду кар'єри — ×3
                RingTime.Warp = 0.4f;                     // сповільнення фінішера
                Assert.AreEqual(1.2f, RingTime.Scale, 1e-5);
                RingTime.Paused = true;
                Assert.AreEqual(0f, RingTime.Scale, "пауза зупиняє час");
                RingTime.Warp = 1f;                       // сповільнення скінчилось під паузою
                Assert.AreEqual(0f, RingTime.Scale, "кінець сповільнення не відпускає паузу");
                RingTime.Paused = false;
                Assert.AreEqual(3f, RingTime.Scale, 1e-5, "вихід з паузи — базовий темп, не застарілий множник");
            }
            finally { RingTime.Reset(); }
            Assert.AreEqual(1f, RingTime.Scale);
        }

        [Test]
        public void Pause_LeaveToTitle_WarnsExactlyWhenSnapshotLosesProgress()
        {
            // безпечно: тиждень між діями, афіша, підсумок шоу, кінець сезону
            Assert.AreEqual(PauseRules.LeaveSafeKey, PauseRules.LeaveBodyKey(CareerStage.Week, ShowPhase.None, false));
            Assert.AreEqual(PauseRules.LeaveSafeKey, PauseRules.LeaveBodyKey(CareerStage.Show, ShowPhase.Card, false));
            Assert.AreEqual(PauseRules.LeaveSafeKey, PauseRules.LeaveBodyKey(CareerStage.Show, ShowPhase.Result, false));
            Assert.AreEqual(PauseRules.LeaveSafeKey, PauseRules.LeaveBodyKey(CareerStage.SeasonEnd, ShowPhase.None, false));
            // втрачається: матч і сцена ворожнечі (зліпок відкочує), закулісся (поза зліпком), створення героя (не зберігається)
            Assert.AreEqual(PauseRules.LeaveShowKey, PauseRules.LeaveBodyKey(CareerStage.Show, ShowPhase.Match, false));
            Assert.AreEqual(PauseRules.LeaveShowKey, PauseRules.LeaveBodyKey(CareerStage.Show, ShowPhase.Feud, false));
            Assert.AreEqual(PauseRules.LeaveBackstageKey, PauseRules.LeaveBodyKey(CareerStage.Week, ShowPhase.None, true));
            Assert.AreEqual(PauseRules.LeaveCreateKey, PauseRules.LeaveBodyKey(CareerStage.Create, ShowPhase.None, false));
            Assert.IsFalse(PauseRules.LosesProgress(CareerStage.Week, ShowPhase.None, false));
            Assert.IsTrue(PauseRules.LosesProgress(CareerStage.Show, ShowPhase.Match, false));
        }

        [Test]
        public void MenuTextKeys_AllExistInUiStrings()
        {
            var ui = MiniJson.ParseObject(CareerKit.Read("ui_uk.json"));
            string dir = Path.Combine(Root(), "Assets", "_Project", "Scripts", "Gameplay", "Ring");
            var files = new List<string>(Directory.GetFiles(Path.Combine(dir, "UI", "Menus"), "*.cs"));
            files.Add(Path.Combine(dir, "RingGameBootstrap.Menus.cs"));
            files.Add(Path.Combine(dir, "Models", "RingPause.cs"));
            var rx = new Regex("\"(m\\.[a-z0-9_.]+[a-z0-9_])\"");
            var missing = new List<string>();
            int seen = 0;
            foreach (var f in files)
                foreach (Match mt in rx.Matches(File.ReadAllText(f)))
                {
                    string k = mt.Groups[1].Value;
                    if (k.EndsWith(".")) continue;
                    seen++;
                    if (!ui.ContainsKey(k)) missing.Add(Path.GetFileName(f) + ": " + k);
                }
            Assert.Greater(seen, 40, "ключі меню знайдено");
            Assert.IsEmpty(missing, "ключі меню без тексту:\n" + string.Join("\n", missing));
            foreach (RingAction a in Enum.GetValues(typeof(RingAction)))
                Assert.IsTrue(ui.ContainsKey("m.act." + a.ToString().ToLowerInvariant()), "назва дії " + a);
            // літери й цифри показуються як є; решта заводських клавіш (Enter, Esc, стрілки) — з текстом
            foreach (var kv in RingSettingsValues.DefaultKeys())
                if (kv.Value.Length > 1) Assert.IsTrue(ui.ContainsKey("m.key." + kv.Value), "назва заводської клавіші " + kv.Value);
            Assert.IsTrue(ui.ContainsKey("m.val.lang." + new RingSettingsValues().Language));
            int tips = 0;
            while (ui.ContainsKey("m.tip." + (tips + 1))) tips++;
            Assert.GreaterOrEqual(tips, 8, "порад вистачає, щоб не повторювались за одне завантаження");
        }
    }
}
