using System;
using System.Collections;
using System.Collections.Generic;
using Game.Core.Ring.Career;
using Game.Gameplay.Combat;
using Game.Gameplay.Ring.Models;
using Game.Gameplay.Ring.Stage;
using Game.Gameplay.Ring.UI;
using Game.Gameplay.Ring.UI.Menus;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Gameplay.Ring
{
    /// <summary>
    /// Меню «Рингу» в хості гри (ТЗ B08): налаштування з диска, ввід меню, шари екрана й поверх нього (завантаження, розріз),
    /// вхід у гру через завантаження, огляд меню <c>-ring-menu-shots &lt;тека&gt;</c>. Окремий файл, щоб правки меню не
    /// перемішувались з кар'єрою в основному файлі хоста.
    /// </summary>
    public sealed partial class RingGameBootstrap : IMenuHost
    {
        private const string MenuShotsArg = "-ring-menu-shots";

        private RingSettingsRuntime _settings;
        private RingMenuInput _menuInput;
        private VisualElement _screenLayer, _overlayLayer;
        private LoadingOverlay _loading;
        private WipeTransition _wipe;
        private string _menuShotsDir;
        private string _saveSummary;
        private bool _bootDone;

        // ---------------- IMenuHost ----------------

        public RingSettingsRuntime Settings { get { return _settings; } }
        public RingMenuInput MenuInput { get { return _menuInput; } }

        public string Version { get { return string.Format(_text != null ? _text.Get("m.version") : "{0}", Application.version); } }

        public string SaveSummary
        {
            get
            {
                if (_saveSummary == null && HasSave) { string e; CheckSave(out e); }
                return _saveSummary;
            }
        }

        /// <summary>Сейв читається в окрему кар'єру й відкидається: гра не змінюється, поки гравець не увійде.</summary>
        public bool CheckSave(out string errorKey)
        {
            errorKey = null;
            try
            {
                CareerSession c = null;
                bool fromBackup;
                RingSaveFile.Read(SavePath, l =>
                {
                    var t = CareerSession.New(_content, new SeededDiceRoller(_seed), _seed);
                    t.RestoreState(l[0]);
                    c = t;
                }, out fromBackup);
                _saveSummary = string.Format(_text.Get("m.title.summary"), c.State.Season, c.State.Week);
                return true;
            }
            catch (System.IO.FileNotFoundException) { errorKey = "m.error.save.missing"; }
            catch (System.IO.InvalidDataException) { errorKey = "m.error.save.empty"; }
            catch (Exception ex)
            {
                Debug.LogWarning("[Ринг] сейв не пройшов перевірку: " + ex.Message);
                errorKey = "m.error.save.broken";
            }
            _saveSummary = null;
            return false;
        }

        public void StartGame(bool continueSave)
        {
            if (_wipe.Busy || _loading != null) return;
            _wipe.Play(() =>
            {
                ShowLoading(continueSave
                    ? new[] { "m.load.s.save", "m.load.s.wrestlers", "m.load.s.arena", "m.load.s.crowd" }
                    : new[] { "m.load.s.career", "m.load.s.wrestlers", "m.load.s.arena", "m.load.s.crowd" },
                    new[] { 3f, 3f, 2f, 1f });
                StartCoroutine(GameLoad(continueSave));
            }, null);
        }

        /// <summary>Сейв переписано: підсумок для «Продовжити» перечитати при наступному показі титулу.</summary>
        private void InvalidateSaveSummary() { _saveSummary = null; }

        public void SettingsApplied()
        {
            _menuInput.SetBindings(_settings.Model.Applied.Keys);
            _appliedScale = -1f;
            ApplyScale();
        }

        // ---------------- запуск ----------------

        /// <summary>Налаштування й ввід меню — до першого кадру. Огляди пишуть налаштування лише в пам'ять.</summary>
        private void InitMenus()
        {
            _menuShotsDir = ReadArg(MenuShotsArg);
            bool review = _shotsDir != null || _menuShotsDir != null || ReadArg("-ring-lookbook") != null || ReadArg("-ring-font-spike") != null;
            _settings = new RingSettingsRuntime(review ? (IRingSettingsStore)new MemorySettingsStore() : new PlayerPrefsSettingsStore(), !review);
            _settings.ApplyAll();
            _menuInput = new RingMenuInput();
            _menuInput.SetBindings(_settings.Model.Applied.Keys);
            RingMenuServices.MenuMusicActive = true;
            if (_menuShotsDir != null)
            {
                Application.runInBackground = true;
                System.IO.Directory.CreateDirectory(_menuShotsDir);
            }
        }

        /// <summary>Шари кореня: екран кар'єри (міняється) і поверх нього — завантаження й розріз (живуть між екранами).</summary>
        private void BuildLayers()
        {
            _screenLayer = MenuShapes.Layer();     // сам шар мишу пропускає, екрани в ньому — ловлять
            _overlayLayer = MenuShapes.Layer();
            _root.Add(_screenLayer);
            _root.Add(_overlayLayer);
            _wipe = new WipeTransition();
            _overlayLayer.Add(_wipe.Root);
        }

        /// <summary>
        /// Старт гри за екраном завантаження: дані, сейв, арена, світло — справжні кроки (арена збудована ще в Awake сцени,
        /// тож її крок — кадр показу). Потім вступ (3D) і вхід. Лукбук і спайк шрифтів — без завантаження, як раніше.
        /// </summary>
        private IEnumerator Boot()
        {
            string look = ReadArg("-ring-lookbook");
            string spike = ReadArg("-ring-font-spike");
            bool quick = look != null || spike != null || RingStageHub.DemoRequested;
            LoadTextEarly();   // екран завантаження показує назви кроків і поради з першого кадру
            if (!quick) ShowLoading(new[] { "m.load.s.data", "m.load.s.save", "m.load.s.arena", "m.load.s.crowd" }, new[] { 4f, 1f, 3f, 2f });
            if (!quick) RingMenuServices.Music(MenuMusic.Loading);
            yield return null;
            if (!LoadData()) { HideLoading(); yield break; }
            StepLoading();
            yield return null;
            MigrateSave();
            if (_menuShotsDir != null) WriteBotSave(true);
            _career = CareerSession.New(_content, new SeededDiceRoller(_seed), _seed);
            _seed += 1;
            StepLoading();
            yield return null;
            var stage = RingStageHub.Current;
            if (stage != null && !RingStageHub.DemoRequested) stage.ShowIdle();
            StepLoading();
            yield return null;
            StepLoading();
            if (look != null && stage != null) { _onTitle = true; Rebind(); _bootDone = true; StartCoroutine(Lookbook(stage, look)); yield break; }
            if (spike != null) { _onTitle = true; Rebind(); _bootDone = true; StartCoroutine(FontSpike(spike)); yield break; }
            if (_menuShotsDir != null) yield return MenuShots.LoadingShots(this);
            while (_loading != null && !_loading.CanExit) yield return null;
            if (_loading != null)
            {
                bool covered = false;
                _wipe.Play(() => { HideLoading(); covered = true; }, null);
                while (!covered) yield return null;
            }
            _onTitle = true;
            Rebind();
            _bootDone = true;
            if (stage != null && !RingStageHub.DemoRequested && _menuShotsDir == null)
            {
                _inIntro = true;
                stage.BeginIntro(_text.Get("c.game.title"), _text.Get("c.game.sub"));
            }
            if (!quick) RingMenuServices.Music(MenuMusic.Login);
            if (_menuShotsDir != null) StartCoroutine(MenuShots.Run(this));
        }

        /// <summary>Вхід у гру після «Продовжити» / «Нова кар'єра»: справжні кроки, далі розріз у гру.</summary>
        private IEnumerator GameLoad(bool continueSave)
        {
            RingMenuServices.Music(MenuMusic.Loading);
            yield return null;
            if (continueSave) ContinueGame(); else NewGame();
            if (_onTitle) { _viewKey = null; Debug.LogWarning("[Ринг] вхід не вдався — титул заново"); }
            StepLoading();
            yield return null;
            Sync();                       // екран і 3D: борці, костюми, зала
            StepLoading();
            yield return null;
            StepLoading();
            yield return null;
            StepLoading();
            if (_menuShotsDir != null) yield return MenuShots.LoadingShots(this);
            while (_loading != null && !_loading.CanExit) yield return null;
            EnterGameAudio();
            _wipe.Play(HideLoading, null);
        }

        /// <summary>Звук гри після меню: музика меню згасає, музика зали (RingSound) повертається — кросфейд.</summary>
        private void EnterGameAudio()
        {
            RingMenuServices.MenuMusicActive = false;
            RingMenuServices.Music(MenuMusic.None);
            if (RingMenuServices.GameMusic != null) RingMenuServices.GameMusic(true);
        }

        private void ShowLoading(IList<string> steps, IList<float> weights)
        {
            HideLoading();
            _loading = new LoadingOverlay(_text ?? EmptyText(), steps, weights, (int)(_seed % 1000UL));
            _overlayLayer.Insert(0, _loading.Root);
        }

        private static RingText EmptyText() { return RingText.FromJson("{}", "{}", "{}"); }

        /// <summary>Лише тексти (до LoadData): рядки потрібні екрану завантаження ще до даних кар'єри. LoadData потім читає їх знову.</summary>
        private void LoadTextEarly()
        {
            try
            {
                var strings = Resources.Load<TextAsset>(StringsPath);
                var ui = Resources.Load<TextAsset>(UiStringsPath);
                var back = Resources.Load<TextAsset>(BackstageTextPath);
                if (strings != null && ui != null && back != null) _text = RingText.FromJson(strings.text, ui.text, back.text);
            }
            catch (Exception e) { Debug.LogWarning("[Ринг] тексти до завантаження не прочитались: " + e.Message); }
        }

        private void StepLoading() { if (_loading != null) _loading.CompleteStep(); }

        private void HideLoading()
        {
            if (_loading == null) return;
            _loading.Root.RemoveFromHierarchy();
            _loading = null;
        }

        /// <summary>Поки йде завантаження чи розріз, екран під ними не приймає клавіш (і бот огляду кар'єри чекає).</summary>
        private bool MenuOverlayBusy { get { return _loading != null || (_wipe != null && _wipe.Busy) || !_bootDone; } }

        private void TickMenuOverlays()
        {
            float dt = Time.unscaledDeltaTime;
            if (_loading != null) _loading.Tick(dt);
            if (_wipe != null) _wipe.Tick(dt);
        }

        /// <summary>Перепризначена клавіша дії натиснута (для екранів кар'єри, що слухають старі клавіші).</summary>
        private bool BoundDown(RingAction a)
        {
            if (_menuInput == null) return false;
            var k = _menuInput.KeyOf(a);
            return (int)k != 0 && UnityEngine.Input.GetKeyDown(k);
        }

        /// <summary>Ввід меню (клавіатура, геймпад, колесо) — для екранів-меню; true — екран його прийняв.</summary>
        private bool PollMenuInput()
        {
            var nav = _view as IMenuNavigable;
            if (nav == null || _menuInput == null) return false;
            _menuInput.Poll(nav, Time.unscaledDeltaTime);
            return true;
        }

        /// <summary>Сейв огляду меню: справжній стан нової кар'єри (true) або зіпсований рядок (false) — для стану «помилка».</summary>
        internal void WriteBotSave(bool valid)
        {
            try
            {
                string state;
                if (valid)
                {
                    var c = CareerSession.New(_content, new SeededDiceRoller(_seed + 77UL), _seed + 77UL);
                    c.StartNewGame();
                    state = c.CaptureState();
                }
                else state = "зіпсовано|42|не сейв";
                // Огляд показує биту кар'єру: запасна копія не повинна її «врятувати».
                RingSaveFile.Delete(SavePath);
                if (valid) RingSaveFile.Write(SavePath, new[] { state, "0|0" });
                else System.IO.File.WriteAllLines(SavePath, new[] { state, "0|0" });
                _saveSummary = null;
            }
            catch (Exception ex) { Debug.LogWarning("[Ринг] огляд меню: сейв не записано: " + ex.Message); }
        }

        // ---------------- для огляду меню ----------------

        internal string MenuShotsDir { get { return _menuShotsDir; } }
        internal RingScreenView CurrentView { get { return _view; } }
        internal LoadingOverlay CurrentLoading { get { return _loading; } }
        internal WipeTransition Wipe { get { return _wipe; } }
        internal bool OnTitle { get { return _onTitle; } }
        internal void Shot(string name)
        {
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(_menuShotsDir, string.Format("{0:000}_{1}.png", _menuShot++, name)));
        }

        private int _menuShot;

        internal void ExitAfterShots() { _quitAt = Time.unscaledTime + 1f; }
    }
}
