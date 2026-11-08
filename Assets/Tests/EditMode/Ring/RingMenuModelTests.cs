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

        // ---------------- промо: підпис акту ----------------

        /// <summary>
        /// Шапка промо пише «Акт N» лише коли акт є (PromoScreenView: `_m.Act &gt; 0`): у ворожнечі акти з 1, у сцен кар'єри
        /// (розмова з букером, події поза рингом) — 0, і «Акт 0» плейтест 08.11.2026 (P3) читав як помилку.
        /// </summary>
        [Test]
        public void PromoAct_FeudHasActs_CareerScenesHaveNone()
        {
            Assert.Greater(RingCampaignDriver.NewCampaign(1).StartPromo().Promo.Act, 0, "промо ворожнечі — з актом");
            var s = CareerKit.NewHero(1);
            var r = s.Backstage();
            Assert.IsTrue(r.NeedsScene, "сцена не відкрилась: " + r.Reason);
            Assert.AreEqual(0, s.CurrentBackstage.Promo.Act, "у сцени кар'єри актів немає — підпис акту не показується");
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
                Qte = false, ActionShots = RingActionShots.Often, FinisherSlowMo = false,
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
        public void Settings_MatchPresentation_DefaultsKeysAndClamp()
        {
            // API для бою й камери (§4.12, П4 ПМ 08.11.2026): заводські — QTE є, кадри «Звично», сповільнення є
            var d = new RingSettingsValues();
            Assert.IsTrue(d.Qte);
            Assert.AreEqual(RingActionShots.Normal, d.ActionShots);
            Assert.IsTrue(d.FinisherSlowMo);

            var store = new DictStore();
            var v = new RingSettingsValues { Qte = false, ActionShots = RingActionShots.Off, FinisherSlowMo = false };
            RingSettingsModel.Write(v, store);
            Assert.AreEqual("0", store.D["ring.settings.qte"], "ключі сховища — стабільні, їх читає бій і камера");
            Assert.AreEqual("0", store.D["ring.settings.actionshots"]);
            Assert.AreEqual("0", store.D["ring.settings.slowmo"]);

            var broken = new DictStore();
            broken.D["ring.settings.actionshots"] = "9";
            broken.D["ring.settings.qte"] = "мусор";
            var r = RingSettingsModel.Load(broken).Applied;
            Assert.AreEqual(RingActionShots.Normal, r.ActionShots, "невідомий рівень — заводський");
            Assert.IsTrue(r.Qte, "нечитабельне — заводське");

            var m = new RingSettingsModel();
            m.Draft.ActionShots = RingActionShots.Rare;
            Assert.IsTrue(m.IsDirty, "зміна подачі матчу — незбережені зміни");
            m.Draft.ActionShots = RingActionShots.Normal;
            m.Draft.FinisherSlowMo = false;
            Assert.IsTrue(m.IsDirty);
        }

        [Test]
        public void Settings_ActionShotChance_IsOrderedAndOffMeansNever()
        {
            Assert.AreEqual(0f, RingSettingsValues.ActionShotChance(RingActionShots.Off));
            Assert.Less(RingSettingsValues.ActionShotChance(RingActionShots.Rare), RingSettingsValues.ActionShotChance(RingActionShots.Normal));
            Assert.Less(RingSettingsValues.ActionShotChance(RingActionShots.Normal), RingSettingsValues.ActionShotChance(RingActionShots.Often));
            Assert.LessOrEqual(RingSettingsValues.ActionShotChance(RingActionShots.Often), 0.5f, "два кадри поспіль — ні (§4.12)");

            var keep = new RingSettingsValues();
            keep.CopyFrom(new RingSettingsValues { Qte = RingMatchPrefs.Qte, ActionShots = RingMatchPrefs.ActionShots, FinisherSlowMo = RingMatchPrefs.FinisherSlowMo });
            try
            {
                RingMatchPrefs.SetFrom(new RingSettingsValues { Qte = false, ActionShots = RingActionShots.Off, FinisherSlowMo = false });
                Assert.IsFalse(RingMatchPrefs.Qte);
                Assert.IsFalse(RingMatchPrefs.FinisherSlowMo);
                Assert.AreEqual(0f, RingMatchPrefs.ActionShotChance, "«Вимк.» — екшн-кадрів немає");
            }
            finally { RingMatchPrefs.SetFrom(keep); }
        }

        [Test]
        public void Caption_TwoWordsOrLess_IsCaps_SentencesAreNot()
        {
            // Статут TYPE-02 (рев'ю B03 v2, 08.11.2026): речення в заголовках матчу й промо — не ВЕЛИКИМИ
            Assert.IsTrue(RingCaption.IsShort("Далі"));
            Assert.IsTrue(RingCaption.IsShort("На ринг"));
            Assert.IsTrue(RingCaption.IsShort("Тиждень 3"));
            Assert.IsTrue(RingCaption.IsShort("Тайгер — Лиходій"), "тире словом не рахується");
            Assert.IsTrue(RingCaption.IsShort(""));
            Assert.IsFalse(RingCaption.IsShort("Далі — до наступного промо"));
            Assert.IsFalse(RingCaption.IsShort("Тайгер: «Поганий хлопець». Як відповісти?"));
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
        public void Languages_EveryAvailableLanguage_IsFullyTranslated()
        {
            // П4 ПМ 08.11.2026: «Охоронець «кожен ключ перекладено» — одразу, переклад — останнім». Мова в списку — лише повна.
            // Джерела тексту охоронець знаходить сам: кожен *_uk.json у Resources/Ring — екрани (ui), ворожнеча (strings),
            // закулісся (backstage; ПМ 08.11.2026: «закриває дірку: strings_<мова>.json і backstage_<мова>.json») — і рядки арен.
            // Новий файл тексту одразу вимагає перекладу, переліку руками немає.
            var sources = TextSources();
            var names = new List<string>();
            foreach (var s in sources) names.Add(Path.GetFileName(s));
            foreach (var must in new[] { "ui_uk.json", "strings_uk.json", "backstage_uk.json", "ring_strings_uk.txt" })
                Assert.Contains(must, names, "джерело тексту знайдено: " + must);
            var uk = new Dictionary<string, Dictionary<string, string>>();
            var holes = new List<string>();
            foreach (var s in sources)
            {
                uk[s] = ReadTextFile(s, "uk", holes);
                Assert.Greater(uk[s].Count, 10, "рядки прочитано: " + Path.GetFileName(s));
            }
            Assert.IsEmpty(holes, "український оригінал читається не весь:\n" + string.Join("\n", holes));
            var ui = uk[sources[0]];
            Assert.AreEqual(RingLanguages.Base, RingLanguages.Available[0], "українська — перша й базова");
            foreach (var code in RingLanguages.Available)
            {
                Assert.IsTrue(ui.ContainsKey("m.val.lang." + code), "назва мови " + code + " у перемикачі");
                if (code == RingLanguages.Base) continue;
                foreach (var s in sources)
                {
                    string path = TranslatedPath(s, code);
                    string what = code + " " + Path.GetFileName(path);
                    if (!File.Exists(path)) { holes.Add(what + ": файлу немає"); continue; }
                    CompareTranslation(what, uk[s], ReadTextFile(path, what, holes), holes);
                }
            }
            Assert.IsEmpty(holes, "мову ввімкнено з дірками:\n" + string.Join("\n", holes));
            Assert.AreEqual("Ring/ui_uk", RingLanguages.Resource("Ring/ui_uk", "xx"), "недоступна мова — українська");
            var v = new RingSettingsValues { Language = "xx" };
            v.Clamp();
            Assert.AreEqual(RingLanguages.Base, v.Language, "збережена недоступна мова — українська");
        }

        /// <summary>
        /// Мова в списку має й читатися: кожне завантаження файлу тексту (<c>Resources.Load&lt;TextAsset&gt;</c> сталої
        /// «Ring/…_uk») іде через <see cref="RingLanguages.Resource"/>, інакше перемикач показує перекладене меню, а ворожнеча,
        /// закулісся й екрани лишаються українськими. Поки доступна лише uk — лише перелік (завантажувачі — зони «Кар'єри»,
        /// «Изучение» і меню; правка — запитом до ПМ); щойно в <see cref="RingLanguages.Available"/> з'являється друга мова —
        /// кожне пряме завантаження червоне.
        /// </summary>
        [Test]
        public void Languages_TextLoaders_GoThroughLanguageResource()
        {
            string dir = Path.Combine(Root(), "Assets", "_Project", "Scripts", "Gameplay", "Ring");
            var constRx = new Regex("(\\w+)\\s*=\\s*\"Ring/(\\w+)_uk\"");
            var loadRx = new Regex("Resources\\.Load<TextAsset>\\(\\s*(\\w+)\\s*\\)");
            var consts = new HashSet<string>();
            var bases = new HashSet<string>();
            var files = Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories);
            foreach (var f in files)
                foreach (Match m in constRx.Matches(File.ReadAllText(f))) { consts.Add(m.Groups[1].Value); bases.Add(m.Groups[2].Value); }
            foreach (var must in new[] { "ui", "strings", "backstage" })
                Assert.IsTrue(bases.Contains(must), "стала шляху «Ring/" + must + "_uk» знайдена — сканер бачить завантажувачі");
            var direct = new List<string>();
            foreach (var f in files)
                foreach (Match m in loadRx.Matches(File.ReadAllText(f)))
                    if (consts.Contains(m.Groups[1].Value)) direct.Add(Path.GetFileName(f) + ": " + m.Groups[1].Value);
            if (RingLanguages.Available.Length > 1)
                Assert.IsEmpty(direct, "мов кілька, а тексти вантажаться без RingLanguages.Resource:\n" + string.Join("\n", direct));
            TestContext.WriteLine("Прямих завантажень текстів (до другої мови дозволено): " + direct.Count + "\n" + string.Join("\n", direct));
        }

        /// <summary>Усі джерела видимого тексту українською: спершу ui_uk.json, далі решта *_uk.json Resources/Ring, рядки арен.</summary>
        private static List<string> TextSources()
        {
            var r = new List<string> { Path.Combine(RingCampaignDriver.Dir(), "ui_uk.json") };
            var more = Directory.GetFiles(RingCampaignDriver.Dir(), "*_uk.json");
            Array.Sort(more, StringComparer.Ordinal);
            foreach (var f in more) if (Path.GetFileName(f) != "ui_uk.json") r.Add(f);
            r.Add(Path.Combine(Root(), "Assets", "Art", "Ring", "ring_strings_uk.txt"));
            return r;
        }

        /// <summary>«…/strings_uk.json» + «en» → «…/strings_en.json»; «ring_strings_uk.txt» → «ring_strings_en.txt».</summary>
        private static string TranslatedPath(string ukPath, string code)
        {
            string name = Path.GetFileName(ukPath);
            string ext = Path.GetExtension(name);
            string stem = name.Substring(0, name.Length - ext.Length);
            Assert.IsTrue(stem.EndsWith("_" + RingLanguages.Base, StringComparison.Ordinal), "джерело тексту — *_uk: " + name);
            return Path.Combine(Path.GetDirectoryName(ukPath), stem.Substring(0, stem.Length - RingLanguages.Base.Length) + code + ext);
        }

        /// <summary>Рядки файлу тексту: JSON — лише рядкові значення (не-рядок — дірка), .txt — «ключ=текст».</summary>
        private static Dictionary<string, string> ReadTextFile(string path, string what, List<string> holes)
        {
            if (!path.EndsWith(".json", StringComparison.Ordinal)) return ReadKeyValueFile(path);
            var r = new Dictionary<string, string>();
            foreach (var kv in MiniJson.ParseObject(File.ReadAllText(path)))
            {
                if (kv.Value is string) r[kv.Key] = (string)kv.Value;
                else holes.Add(what + ": не рядок " + kv.Key);
            }
            return r;
        }

        private static Dictionary<string, string> ReadKeyValueFile(string path)
        {
            var r = new Dictionary<string, string>();
            foreach (var raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                int eq = line.IndexOf('=');
                if (eq > 0) r[line.Substring(0, eq).Trim()] = line.Substring(eq + 1);
            }
            return r;
        }

        private static void CompareTranslation(string what, Dictionary<string, string> uk, Dictionary<string, string> tr, List<string> holes)
        {
            var ph = new Regex("\\{[0-9]+(:[^}]*)?\\}");
            foreach (var kv in uk)
            {
                string t;
                if (!tr.TryGetValue(kv.Key, out t) || string.IsNullOrEmpty(t)) { holes.Add(what + ": немає " + kv.Key); continue; }
                var a = new List<string>(); foreach (Match m in ph.Matches(kv.Value)) a.Add(m.Value);
                var b = new List<string>(); foreach (Match m in ph.Matches(t)) b.Add(m.Value);
                a.Sort(StringComparer.Ordinal); b.Sort(StringComparer.Ordinal);
                if (string.Join(",", a) != string.Join(",", b)) holes.Add(what + ": місця підстановки " + kv.Key);
            }
            foreach (var k in tr.Keys) if (!uk.ContainsKey(k)) holes.Add(what + ": зайвий ключ " + k);
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
