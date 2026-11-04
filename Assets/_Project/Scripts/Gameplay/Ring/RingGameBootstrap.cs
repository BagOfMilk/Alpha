using System;
using System.Collections.Generic;
using Game.Core.Ring;
using Game.Core.Ring.Career;
using Game.Core.Ring.Match;
using Game.Gameplay.Combat;
using Game.Gameplay.Ring.Models;
using Game.Gameplay.Ring.Stage;
using Game.Gameplay.Ring.UI;
using Game.Gameplay.UI.Toolkit;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Gameplay.Ring
{
    /// <summary>
    /// Уся гра «Ринг» (ітерація 1 — кар'єра альфи, TRPG_DESIGN §11.3): вступ → титул → створення борця → тиждень (закулісся) →
    /// вечір (картка, виходи з титантроном, матч або етапи ворожнечі з Молотом на великому шоу, підсумок) → кінець сезону → новий
    /// сезон. Правил тут немає — лише показ, клавіші й автозбереження після кожного кроку (<see cref="CareerSession.CaptureState"/>).
    /// Огляд вигляду: <c>-ring-shots &lt;тека&gt;</c> — бот проходить перший сезон тими самими кнопками і знімає кадри.
    /// </summary>
    public sealed class RingGameBootstrap : MonoBehaviour, ICareerHost
    {
        private const string FeudPath = "Ring/feud_hammer";
        private const string StringsPath = "Ring/strings_uk";
        private const string UiStringsPath = "Ring/ui_uk";
        private const string MovesPath = "Ring/career_moves";
        private const string GimmicksPath = "Ring/career_gimmicks";
        private const string RosterPath = "Ring/career_roster";
        private const string SeedArg = "-ring-seed";
        private const string ShotsArg = "-ring-shots";

        private const float ShotStep = 0.45f;
        private const int ShotLimit = 1500;
        private const float AnimShotEvery = 1.3f;
        private float _animClock, _shotClock;
        private string _shotsDir;
        private int _shotIndex, _shotDigit, _showShots;
        private float _quitAt = -1f;
        private bool _shotAct = true, _shotPressDigit;

        private RingText _text;
        private CareerContent _content;
        private CareerSession _career;
        private ulong _seed;
        private RingUiContext _ctx;

        private GameObject _host;
        private PanelSettings _panel;
        private VisualElement _root;
        private float _appliedScale = -1f;
        private RingScreenView _view;
        private object _viewKey;

        // Вступ перед меню і черга виходів перед матчем (поки шоу грає, UI сховано, будь-яка клавіша пропускає).
        private bool _inIntro;
        private bool _onTitle;
        private readonly Queue<bool> _entrances = new Queue<bool>();
        private MatchEngine _matchPending;
        private object _stageKey;

        // ---------------- ICareerHost ----------------

        public int HeroBody { get; set; }
        public int HeroHair { get; set; }
        public WeekActionResult LastWeekResult { get; set; }

        private string SavePath
        {
            get { return System.IO.Path.Combine(Application.persistentDataPath, _shotsDir != null ? "ring_career_bot.txt" : "ring_career.txt"); }
        }

        public bool HasSave { get { return _shotsDir == null && System.IO.File.Exists(SavePath); } }

        public void NewGame()
        {
            _career = CareerSession.New(_content, new SeededDiceRoller(_seed), _seed);
            _seed += 1;
            HeroBody = 0; HeroHair = 0;
            LastWeekResult = null;
            _career.StartNewGame();
            _onTitle = false;
            Rebind();
        }

        public void ContinueGame()
        {
            try
            {
                var lines = System.IO.File.ReadAllLines(SavePath);
                var c = CareerSession.New(_content, new SeededDiceRoller(_seed), _seed);
                c.RestoreState(lines[0]);
                if (lines.Length > 1)
                {
                    var p = lines[1].Split('|');
                    int b, h;
                    if (p.Length > 1 && int.TryParse(p[0], out b) && int.TryParse(p[1], out h)) { HeroBody = Math.Max(0, Math.Min(b, CareerLooks.HeroBodies.Length - 1)); HeroHair = Math.Max(0, Math.Min(h, CareerLooks.HeroHair.Length - 1)); }
                }
                _career = c;
                _onTitle = false;
                LastWeekResult = null;
                Rebind();
            }
            catch (Exception ex)
            {
                Debug.LogError("[Ринг] Сейв не відкрився: " + ex.Message);
            }
        }

        public void Save()
        {
            if (_career == null || _career.Stage == CareerStage.Title || _career.Stage == CareerStage.Create) return;
            try
            {
                System.IO.File.WriteAllLines(SavePath, new[] { _career.CaptureState(), HeroBody + "|" + HeroHair });
            }
            catch (Exception ex)
            {
                Debug.LogError("[Ринг] Сейв не записався: " + ex.Message);
            }
        }

        public void Quit() { HardExit.Now(0); }

        public void ToggleFullscreen() { Screen.fullScreen = !Screen.fullScreen; }

        // ---------------- запуск ----------------

        private void Start()
        {
            _seed = ReadSeed();
            _shotsDir = ReadArg(ShotsArg);
            if (_shotsDir != null)
            {
                Application.runInBackground = true;
                System.IO.Directory.CreateDirectory(_shotsDir);
                // Огляд проходить сезон: кліпи й паузи ШІ втричі швидше (кадри ті самі, лише менше чекання).
                Time.timeScale = 3f;
            }
            if (!BuildUi()) return;
            if (!LoadData()) return;
            _career = CareerSession.New(_content, new SeededDiceRoller(_seed), _seed);
            _seed += 1;
            _onTitle = true;
            Rebind();
            var stage = RingStageHub.Current;
            if (stage != null && !RingStageHub.DemoRequested)
            {
                _inIntro = true;
                stage.BeginIntro(_text.Get("c.game.title"), _text.Get("c.game.sub"));
            }
        }

        private void OnDestroy()
        {
            if (_host != null) Destroy(_host);
            if (_panel != null) Destroy(_panel);
        }

        private static ulong ReadSeed()
        {
            string s = ReadArg(SeedArg);
            ulong v;
            return s != null && ulong.TryParse(s, out v) ? v : (ulong)DateTime.UtcNow.Ticks;
        }

        private static string ReadArg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }

        private bool BuildUi()
        {
            var asset = Resources.Load<PanelSettings>(HudToolkitTheme.PanelResourcePath);
            _panel = asset != null ? Instantiate(asset) : ScriptableObject.CreateInstance<PanelSettings>();
            _panel.name = "RingGamePanel";
            _panel.scaleMode = PanelScaleMode.ConstantPixelSize;
            _panel.sortingOrder = 1f;
            if (_panel.themeStyleSheet == null)
            {
                var theme = Resources.Load<ThemeStyleSheet>(HudToolkitTheme.ThemeResourcePath);
                if (theme != null) _panel.themeStyleSheet = theme;
            }
            ApplyScale();
            _host = new GameObject("Ring (UI Toolkit)");
            _host.SetActive(false);
            var doc = _host.AddComponent<UIDocument>();
            doc.panelSettings = _panel;
            _host.SetActive(true);
            _root = doc.rootVisualElement;
            if (_root == null) { Debug.LogError("[Ринг] UIDocument.rootVisualElement == null"); return false; }
            _root.style.position = Position.Absolute;
            _root.style.left = 0f; _root.style.right = 0f; _root.style.top = 0f; _root.style.bottom = 0f;
            _root.style.backgroundColor = RingStageHub.Has3D ? Color.clear : RingUi.Bg;
            if (RingStageHub.DemoRequested) _root.style.display = DisplayStyle.None;
            return true;
        }

        private void ApplyScale()
        {
            float s = Mathf.Clamp(Screen.height / 720f, 0.75f, 3f);
            if (Mathf.Abs(s - _appliedScale) < 0.001f) return;
            _appliedScale = s;
            _panel.scale = s;
        }

        private bool LoadData()
        {
            var feud = Resources.Load<TextAsset>(FeudPath);
            var strings = Resources.Load<TextAsset>(StringsPath);
            var ui = Resources.Load<TextAsset>(UiStringsPath);
            var moves = Resources.Load<TextAsset>(MovesPath);
            var gimmicks = Resources.Load<TextAsset>(GimmicksPath);
            var roster = Resources.Load<TextAsset>(RosterPath);
            if (feud == null || strings == null || ui == null || moves == null || gimmicks == null || roster == null)
            {
                ShowError(new List<string> { "Resources/Ring: немає даних кар'єри" });
                return false;
            }
            try
            {
                _text = RingText.FromJson(strings.text, ui.text);
                _content = CareerContent.Load(feud.text, moves.text, gimmicks.text, roster.text);
            }
            catch (Exception ex)
            {
                ShowError(new List<string> { ex.Message });
                return false;
            }
            var errors = PromoValidator.Validate(_content.Feud, _text.AsDictionary());
            if (errors.Count > 0)
            {
                var lines = new List<string>();
                foreach (var e in errors) lines.Add(e.ToString());
                ShowError(lines);
                return false;
            }
            return true;
        }

        private void ShowError(List<string> lines)
        {
            foreach (var l in lines) Debug.LogError("[Ринг] " + l);
            _root.Clear();
            var box = RingUi.Card(20f);
            box.style.marginLeft = 40f; box.style.marginRight = 40f; box.style.marginTop = 40f; box.style.marginBottom = 40f;
            box.Add(RingUi.Lbl(_text != null ? _text.Get("ui.error.title") : "Ring data error", 26f, RingUi.Bad, true));
            foreach (var l in lines) box.Add(RingUi.Lbl(l, 16f));
            _root.Add(box);
        }

        /// <summary>Нова чи відновлена кар'єра: контекст екранів, ім'я героя в тексті матчу й промо.</summary>
        private void Rebind()
        {
            _ctx = new RingUiContext
            {
                Campaign = _career.Feud, Text = _text, Career = _career, Host = this,
                Restart = NewGame,
                FinishMatch = () => { _career.FinishMatch(); Save(); },
                StartFeudMatch = () => { _career.StartFeudMatch(); },
                AfterDevelopment = () => { _career.FinishBigShow(); Save(); }
            };
            if (!string.IsNullOrEmpty(_career.State.RingName)) _text.Add("w.player.name", _career.State.RingName);
            _view = null;
            _viewKey = null;
            _stageKey = null;
        }

        // ---------------- кадр ----------------

        private void Update()
        {
            if (_quitAt > 0f && Time.unscaledTime >= _quitAt) HardExit.Now(0);
            if (_career == null || _root == null) return;
            ApplyScale();
            if (ShowStep()) return;
            Sync();
            if (_view == null) return;
            HandleKeys();
            if (_shotsDir != null) ShotsStep();
            if (_view != null) _view.Tick(Time.unscaledDeltaTime);
            Sync();
        }

        /// <summary>Шоу 3D (вступ, виходи): true — ще грає, UI сховано.</summary>
        private bool ShowStep()
        {
            var stage = RingStageHub.Current;
            if (stage == null || RingStageHub.DemoRequested) return false;
            if (!stage.ShowBusy && _entrances.Count > 0)
            {
                bool player = _entrances.Dequeue();
                var engine = _matchPending ?? _career.Engine;
                var f = engine != null ? engine.State.Get(player ? MatchSide.Player : MatchSide.Rival) : null;
                string name = f != null ? _text.Get(f.Stats.NameKey) : "";
                int rgb = player ? CareerLooks.CostumeRgb(_career.State.Costume) : CareerLooks.ForNpc(_career.State.Opponent).Rgb;
                string sub = player ? _text.Get("gimmick." + _content.GimmickOf(_career.State.Gimmick).Key) : NpcGimmick();
                stage.BeginEntrance(player, name, sub, rgb);
            }
            // Лише шоу (вступ, вихід) ховає UI; кліпи матчу йдуть під панеллю матчу.
            bool showing = stage.ShowBusy || _entrances.Count > 0;
            if (!showing)
            {
                if (_inIntro) { _inIntro = false; _viewKey = null; }
                if (_matchPending != null)
                {
                    if (_matchPending == _career.Engine) stage.BeginMatch(_matchPending);
                    _matchPending = null;
                }
            }
            _root.style.display = showing ? DisplayStyle.None : DisplayStyle.Flex;
            if (!showing) return false;
            if (Input.anyKeyDown) stage.SkipShow();
            else if (_shotsDir != null && ShowShot()) stage.SkipShow();
            return true;
        }

        private string NpcGimmick()
        {
            var n = _content.FindNpc(_career.State.Opponent);
            return n != null ? _text.Get("gimmick." + _content.GimmickOf(n.Gimmick).Key) : "";
        }

        private bool ShowShot()
        {
            _animClock += Time.unscaledDeltaTime;
            if (_animClock < AnimShotEvery || _shotIndex >= ShotLimit) return false;
            _animClock = 0f;
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(_shotsDir, string.Format("{0:000}_show.png", _shotIndex++)));
            // Бот дивиться вступ (~16 кадрів) і перші виходи повністю, далі пропускає після трьох кадрів.
            int limit = _shotIndex < 40 ? 16 : 3;
            return ++_showShots % limit == 0;
        }

        /// <summary>Екран за етапом кар'єри (і за етапом ворожнечі на великому шоу).</summary>
        private void Sync()
        {
            object key = ViewKey();
            if (_view != null && Equals(key, _viewKey)) return;
            _viewKey = key;
            _view = MakeView();
            _root.Clear();
            _root.Add(_view.Root);
            _view.Refresh();
            SyncStage();
        }

        private object ViewKey()
        {
            if (_onTitle) return "title";
            var st = _career.State;
            switch (_career.Stage)
            {
                case CareerStage.Create: return "create";
                case CareerStage.Week: return "week:" + st.Season + ":" + st.Week;
                case CareerStage.SeasonEnd: return "season:" + st.Season;
                case CareerStage.Show:
                    switch (st.Phase)
                    {
                        case ShowPhase.Card: return "card:" + st.Season + ":" + st.Week + ":" + st.IsTitle;
                        case ShowPhase.Match: return _career.Engine;
                        case ShowPhase.Result: return "result:" + st.Season + ":" + st.Week + ":" + st.IsTitle;
                        case ShowPhase.Feud:
                            var f = _career.Feud;
                            switch (f.Stage)
                            {
                                case RingStage.Promo: return f.CurrentPromoRunner;
                                case RingStage.Match: return f.CurrentMatch;
                                case RingStage.Overview:
                                case RingStage.MatchPrep: return "feud:" + f.Stage + ":" + f.StepIndex;
                                default: return "feudend:" + f.StepIndex;
                            }
                    }
                    break;
            }
            return "title";
        }

        private RingScreenView MakeView()
        {
            if (_onTitle) return new TitleScreenView(_ctx);
            switch (_career.Stage)
            {
                case CareerStage.Create: return new CreateScreenView(_ctx);
                case CareerStage.Week: return new WeekScreenView(_ctx);
                case CareerStage.SeasonEnd: return new SeasonEndScreenView(_ctx);
                case CareerStage.Show:
                    switch (_career.State.Phase)
                    {
                        case ShowPhase.Card: return new ShowCardScreenView(_ctx, StartShow);
                        case ShowPhase.Match: return new MatchScreenView(_ctx, _career.Engine);
                        case ShowPhase.Result: return new ShowResultScreenView(_ctx);
                        case ShowPhase.Feud:
                            var f = _career.Feud;
                            switch (f.Stage)
                            {
                                case RingStage.Promo: return new PromoScreenView(_ctx, f.CurrentPromoRunner);
                                case RingStage.Match: return new MatchScreenView(_ctx, f.CurrentMatch);
                                case RingStage.Overview:
                                case RingStage.MatchPrep: return new FeudScreenView(_ctx);
                                default: return new EndingScreenView(_ctx);
                            }
                    }
                    break;
            }
            return new TitleScreenView(_ctx);
        }

        private void StartShow()
        {
            Save();
            _career.StartShow();
        }

        /// <summary>3D під екраном: хто на рингу (герой і суперник вечора), закулісся тижня, виходи перед матчем, промо.</summary>
        private void SyncStage()
        {
            var stage = RingStageHub.Current;
            if (stage == null || RingStageHub.DemoRequested || _inIntro) return;
            if (_career.Stage != CareerStage.Title && _career.Stage != CareerStage.Create)
            {
                var opp = CareerLooks.ForNpc(_career.State.Opponent);
                stage.SetWrestlers(CareerLooks.HeroBodies[HeroBody], CareerLooks.CostumeRgb(_career.State.Costume), CareerLooks.HeroHair[HeroHair],
                    opp.Body, opp.Rgb, opp.Hair, opp.Scale);
            }
            if (_onTitle || _career.Stage == CareerStage.Create || _career.Stage == CareerStage.SeasonEnd) { stage.ShowIdle(); return; }
            if (_career.Stage == CareerStage.Week) { stage.ShowBackstage(); return; }
            var phase = _career.State.Phase;
            MatchEngine match = null;
            if (phase == ShowPhase.Match) match = _career.Engine;
            else if (phase == ShowPhase.Feud && _career.Feud.Stage == RingStage.Match) match = _career.Feud.CurrentMatch;
            if (match != null)
            {
                if (Equals(_stageKey, match)) return;
                _stageKey = match;
                _entrances.Clear();
                _entrances.Enqueue(true);
                _entrances.Enqueue(false);
                _matchPending = match;
                return;
            }
            _stageKey = null;
            if (phase == ShowPhase.Feud && _career.Feud.Stage == RingStage.Promo) stage.BeginPromo();
            else stage.ShowIdle();
        }

        // ---------------- клавіші й бот ----------------

        private void HandleKeys()
        {
            for (int i = 1; i <= 9; i++)
            {
                if (!Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + i - 1))) continue;
                _view.OnDigit(i);
                return;
            }
            if (Input.GetKeyDown(KeyCode.L)) _view.OnToggleLog();
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space))
                _view.OnConfirm();
            if (Input.GetKeyDown(KeyCode.Escape) && !_onTitle && _career.Stage == CareerStage.Week)
            {
                Save();
                _onTitle = true;
            }
        }

        private void ShotsStep()
        {
            if (RingStageHub.Busy)
            {
                _animClock += Time.unscaledDeltaTime;
                if (_animClock >= AnimShotEvery && _shotIndex < ShotLimit)
                {
                    _animClock = 0f;
                    ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(_shotsDir, string.Format("{0:000}_{1}_anim.png", _shotIndex++, Label())));
                }
                return;
            }
            _animClock = 0f;
            _shotClock += Time.unscaledDeltaTime;
            if (_shotClock < ShotStep) return;
            _shotClock = 0f;
            _shotAct = !_shotAct;
            if (_shotAct)
            {
                // На тижні бот робить усі три дії різними цифрами, тоді «Далі»; деінде — цифра й «Далі» по черзі.
                bool week = !_onTitle && _career.Stage == CareerStage.Week;
                if (week && (_career.State.ActionsLeft > 0 || _career.State.PromoChoicePending)) _view.OnDigit(1 + _shotDigit++ % 6);
                else if (_shotPressDigit) _view.OnDigit(1 + _shotDigit++ % 5);
                else _view.OnConfirm();
                _shotPressDigit = !_shotPressDigit;
                return;
            }
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(_shotsDir, string.Format("{0:000}_{1}.png", _shotIndex++, Label())));
            bool done = !_onTitle && _career.Stage == CareerStage.SeasonEnd && _career.State.Season >= 1 && _seasonShots++ >= 3;
            if (done || _shotIndex >= ShotLimit)
            {
                Debug.Log("[Ринг] Огляд: знято " + _shotIndex + " кадрів, сезон " + _career.State.Season + ", тиждень " + _career.State.Week);
                _shotsDir = null;
                _quitAt = Time.unscaledTime + 1f;
            }
        }

        private int _seasonShots;

        private string Label()
        {
            if (_onTitle) return "Title";
            var st = _career.State;
            string s = "s" + st.Season + "w" + st.Week + "_" + _career.Stage;
            if (_career.Stage == CareerStage.Show) s += "_" + st.Phase + (st.Phase == ShowPhase.Feud ? "_" + _career.Feud.Stage : "");
            return s;
        }
    }
}
