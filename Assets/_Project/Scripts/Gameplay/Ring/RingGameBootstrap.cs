using System;
using System.Collections.Generic;
using Game.Core.Ring;
using Game.Core.Ring.Career;
using Game.Core.Ring.Match;
using Game.Gameplay.Combat;
using Game.Gameplay.Ring.Models;
using Game.Gameplay.Ring.Stage;
using Game.Gameplay.Ring.UI;
using Game.Gameplay.Ring.UI.Menus;
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
    public sealed partial class RingGameBootstrap : MonoBehaviour, ICareerHost
    {
        private const string FeudPath = "Ring/feud_hammer";
        private const string StringsPath = "Ring/strings_uk";
        private const string UiStringsPath = "Ring/ui_uk";
        private const string MovesPath = "Ring/career_moves";
        private const string GimmicksPath = "Ring/career_gimmicks";
        private const string RosterPath = "Ring/career_roster";
        private const string BackstagePath = "Ring/career_backstage";
        private const string BackstageTextPath = "Ring/backstage_uk";
        private const string SeedArg = "-ring-seed";
        private const string ShotsArg = "-ring-shots";

        private const float ShotStep = 0.45f;
        private int ShotLimit = 1500;
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
            get
            {
                // Огляди пишуть свій файл: сейв гравця не чіпають (огляд кар'єри — ring_career_bot, огляд меню — ring_menu_bot).
                string file = _shotsDir != null ? "ring_career_bot.txt" : _menuShotsDir != null ? "ring_menu_bot.txt" : "ring_career.txt";
                return System.IO.Path.Combine(Application.persistentDataPath, file);
            }
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
                InvalidateSaveSummary();
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
            RingTime.Reset();         // темп часу однією формулою (пауза, стоп-кадр, огляд ×3) — Models/RingPause.cs
            if (_shotsDir != null)
            {
                Application.runInBackground = true;
                UI.CareerScreenView.ExpandForShots = true;
                System.IO.Directory.CreateDirectory(_shotsDir);
                // Огляд проходить сезон: кліпи й паузи ШІ втричі швидше (кадри ті самі, лише менше чекання).
                RingTime.Base = 3f;
                Time.timeScale = RingTime.Scale;
                int lim;
                string ls = ReadArg("-ring-shot-limit");
                if (ls != null && int.TryParse(ls, out lim) && lim > 0) ShotLimit = lim;
                // Скільки сезонів проходить огляд (за замовчуванням 1; уся ворожнеча з Молотом — 3).
                string ss = ReadArg("-ring-shot-seasons");
                if (ss != null && int.TryParse(ss, out lim) && lim > 0) _shotSeasons = lim;
            }
            // Меню (ТЗ B08): налаштування з диска, ввід, шари; далі екран завантаження веде справжні кроки старту (Boot).
            InitMenus();
            if (!BuildUi()) return;
            BuildLayers();
            StartCoroutine(Boot());
        }

        private void OnDestroy()
        {
            if (_host != null) Destroy(_host);
            if (_panel != null) Destroy(_panel);
        }

        /// <summary>
        /// Лукбук вигляду (-ring-lookbook &lt;тека&gt;): усі тіла створення героя (стать × культура) з різними зачісками й кольорами
        /// костюма, по двоє на ринг, кадр зблизька; і всі NPC федерації. Для перевірки очима, без гри.
        /// </summary>
        private System.Collections.IEnumerator Lookbook(IRingStage stage, string dir)
        {
            Application.runInBackground = true;
            System.IO.Directory.CreateDirectory(dir);
            _root.style.display = DisplayStyle.None;
            int n = CareerLooks.HeroBodies.Length;
            for (int i = 0; i < n; i += 2)
            {
                stage.SetWrestlers(CareerLooks.HeroBodies[i], CareerLooks.CostumeRgb(i % 8), CareerLooks.HeroHair[i % CareerLooks.HeroHair.Length],
                    CareerLooks.HeroBodies[i + 1], CareerLooks.CostumeRgb((i + 3) % 8), CareerLooks.HeroHair[(i + 1) % CareerLooks.HeroHair.Length], 1f);
                stage.LookbookShot();
                yield return new WaitForSeconds(1.2f);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, string.Format("look_{0:00}_{1}_{2}.png", i, CareerLooks.HeroBodies[i], CareerLooks.HeroBodies[i + 1])));
                yield return new WaitForSeconds(0.4f);
                // Ноги збоку на рівні мату: наколінники, чоботи, підошви проти полотна (заміри — у лог).
                stage.LookbookFeet(true);
                yield return new WaitForSeconds(0.6f);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, string.Format("legs_{0:00}_{1}_{2}.png", i, CareerLooks.HeroBodies[i], CareerLooks.HeroBodies[i + 1])));
                yield return new WaitForSeconds(0.3f);
                stage.LookbookFeet(false);
            }
            foreach (var npc in _content.Roster)
            {
                var l = CareerLooks.ForNpc(npc.Id);
                stage.SetWrestlers(CareerLooks.HeroBodies[0], CareerLooks.CostumeRgb(0), CareerLooks.HeroHair[0], l.Body, l.Rgb, l.Hair, l.Scale);
                stage.LookbookShot();
                yield return new WaitForSeconds(1.2f);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "npc_" + npc.Id + ".png"));
                yield return new WaitForSeconds(0.4f);
                stage.LookbookFeet(true);
                yield return new WaitForSeconds(0.6f);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "legs_npc_" + npc.Id + ".png"));
                yield return new WaitForSeconds(0.3f);
                stage.LookbookFeet(false);
            }
            // Ноги збоку на рівні мату: підошви проти полотна (огляд 04.11.2026 — «боти проходять крізь ринг»).
            stage.SetWrestlers(CareerLooks.HeroBodies[0], CareerLooks.CostumeRgb(0), CareerLooks.HeroHair[0],
                CareerLooks.HeroBodies[8], CareerLooks.CostumeRgb(3), CareerLooks.HeroHair[1], 1f);
            stage.LookbookShot();
            yield return new WaitForSeconds(1.2f);
            stage.LookbookFeet(true);
            yield return new WaitForSeconds(0.6f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "feet_side.png"));
            yield return new WaitForSeconds(0.4f);
            // Лежання після кидка (кінець Bodyslam_B) збоку: з Foot IK і без (огляд 04.11.2026 — ступні стирчать угору).
            foreach (bool ik in new[] { true, false })
            {
                stage.LookbookClip("Bodyslam_B", ik);
                yield return new WaitForSeconds(3.4f);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "lying_side_ik" + (ik ? "1" : "0") + ".png"));
                yield return new WaitForSeconds(0.4f);
            }
            stage.LookbookFeet(false);
            // Закулісся: Ліна й гості подій — загальний план і зблизька (камера на співрозмовника й на героя).
            foreach (var scene in new[] { null, "ev_parking", "ev_friend", "ev_interview", "ev_contract" })
            {
                stage.ShowBackstage();
                stage.SetBackstageScene(scene);
                yield return new WaitForSeconds(1.0f);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "bs_" + (scene ?? "booker") + "_wide.png"));
                yield return new WaitForSeconds(0.3f);
                stage.SetPromoSpeaker("booker");
                yield return new WaitForSeconds(1.0f);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "bs_" + (scene ?? "booker") + "_close.png"));
                yield return new WaitForSeconds(0.3f);
                stage.SetPromoSpeaker("player");
                yield return new WaitForSeconds(1.0f);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "bs_" + (scene ?? "booker") + "_hero.png"));
                yield return new WaitForSeconds(0.3f);
            }
            yield return new WaitForSeconds(0.5f);
            HardExit.Now(0);
        }

        /// <summary>Спайк шрифтів (ТЗ B00 §2): стрічка кожної ролі на трьох кеглях — кадр у файл і вихід.</summary>
        private bool _fontSpike;

        private System.Collections.IEnumerator FontSpike(string file)
        {
            _fontSpike = true;
            Application.runInBackground = true;
            _root.Clear();
            _root.style.display = DisplayStyle.Flex;
            _root.Add(Game.Gameplay.Ring.UI.RingFontSpike.Build());
            yield return new WaitForSeconds(1.5f);
            ScreenCapture.CaptureScreenshot(file);
            yield return new WaitForSeconds(0.8f);
            HardExit.Now(0);
        }

        /// <summary>
        /// Перенесення сейву (ТЗ B00 §3): з назвою застосунку «Ринг» тека persistentDataPath змінилась
        /// (…/DefaultCompany/Ринг замість …/DefaultCompany/Alpha) — при першому запуску копіюємо старий сейв, якщо нового ще немає.
        /// Старий файл не видаляємо; один запис у лог.
        /// </summary>
        private static void MigrateSave()
        {
            try
            {
                string now = Application.persistentDataPath;
                var company = System.IO.Directory.GetParent(now);
                if (company == null || company.Parent == null) return;
                string oldDir = System.IO.Path.Combine(company.Parent.FullName, "DefaultCompany", "Alpha");
                var seps = new[] { System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar };
                if (string.Equals(System.IO.Path.GetFullPath(oldDir).TrimEnd(seps), System.IO.Path.GetFullPath(now).TrimEnd(seps),
                        System.StringComparison.OrdinalIgnoreCase)) return;
                string oldFile = System.IO.Path.Combine(oldDir, "ring_career.txt");
                string newFile = System.IO.Path.Combine(now, "ring_career.txt");
                if (System.IO.File.Exists(newFile) || !System.IO.File.Exists(oldFile)) return;
                System.IO.Directory.CreateDirectory(now);
                System.IO.File.Copy(oldFile, newFile);
                Debug.Log("[Ринг] сейв перенесено зі старої теки: " + oldFile + " → " + newFile);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Ринг] сейв не перенесено: " + e.Message);
            }
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
            // Еталон 900 рядків (а не 720, як у зрізі): панелі менші, ринг видно (власник 04.11.2026: «зроби значно меньше»).
            // «Розмір інтерфейсу» діє на екрани гри (гнучка розкладка); меню й завантаження — завжди в еталоні 900 (розкладка за
            // безпечними зонами B01/B08: на 120 % кнопки й рядок стану пішли б за край).
            bool menu = _view is IMenuNavigable || _loading != null || !_bootDone || _pause != null;
            float s = Mathf.Clamp(Screen.height / 900f, 0.65f, 2.4f) * (_settings != null && !menu ? _settings.UiScale : 1f);
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
            var backstage = Resources.Load<TextAsset>(BackstagePath);
            var backstageText = Resources.Load<TextAsset>(BackstageTextPath);
            if (feud == null || strings == null || ui == null || moves == null || gimmicks == null || roster == null || backstage == null || backstageText == null)
            {
                ShowError(new List<string> { "Resources/Ring: немає даних кар'єри" });
                return false;
            }
            try
            {
                _text = RingText.FromJson(strings.text, ui.text, backstageText.text);
                _content = CareerContent.Load(feud.text, moves.text, gimmicks.text, roster.text, backstage.text);
            }
            catch (Exception ex)
            {
                ShowError(new List<string> { ex.Message });
                return false;
            }
            var errors = PromoValidator.Validate(_content.Feud, _text.AsDictionary());
            errors.AddRange(PromoValidator.ValidateScenes(_content.Backstage.Promos(), _text.AsDictionary()));
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
            var layer = _screenLayer ?? _root;
            layer.Clear();
            var box = RingUi.Card(20f);
            box.style.marginLeft = 40f; box.style.marginRight = 40f; box.style.marginTop = 40f; box.style.marginBottom = 40f;
            box.Add(RingUi.Lbl(_text != null ? _text.Get("ui.error.title") : "Ring data error", 26f, RingUi.Bad, true));
            foreach (var l in lines) box.Add(RingUi.Lbl(l, 16f));
            layer.Add(box);
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
            if (_fontSpike) return;   // спайк шрифтів: екран не оновлюється
            TickMenuOverlays();       // завантаження й розріз живуть і до появи кар'єри
            if (_career == null || _root == null || !_bootDone) return;
            ApplyScale();
            if (Paused)
            {
                // пауза: екран гри стоїть (не тікає, клавіш не бере), меню паузи живе в реальному часі
                PollPause(Input.GetKeyDown(KeyCode.Escape));
                if (_pause != null) _pause.Tick(Time.unscaledDeltaTime);
                return;
            }
            if (ShowStep()) return;
            Sync();
            if (_view == null) return;
            HandleKeys();
            if (_shotsDir != null && !MenuOverlayBusy) ShotsStep();
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
            // ховаємо лише екран: розріз і завантаження поверх лишаються видимими
            _screenLayer.style.display = showing ? DisplayStyle.None : DisplayStyle.Flex;
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
            _screenLayer.Clear();
            _screenLayer.Add(_view.Root);
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
                case CareerStage.Week:
                    // Сцена закулісся (розмова з букером чи подія поза рингом) — окремий екран діалогу поверх тижня.
                    return _career.BackstageOpen ? (object)_career.CurrentBackstage : "week:" + st.Season + ":" + st.Week;
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
            if (_onTitle) return new TitleMenuView(_ctx, this);
            switch (_career.Stage)
            {
                case CareerStage.Create: return new CreateScreenView(_ctx);
                case CareerStage.Week:
                    if (_career.BackstageOpen) return new PromoScreenView(BackstageCtx(), _career.CurrentBackstage);
                    return new WeekScreenView(_ctx);
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
            return new TitleMenuView(_ctx, this);
        }

        /// <summary>Контекст екрана сцени закулісся: той самий діалог, що й промо, але кубик і кінець — кар'єри.</summary>
        private RingUiContext BackstageCtx()
        {
            return new RingUiContext
            {
                Campaign = _ctx.Campaign, Text = _ctx.Text, Career = _ctx.Career, Host = this, Restart = _ctx.Restart,
                PromoDice = _career.Dice,
                FinishPromo = () => { LastWeekResult = _career.FinishBackstage(); Save(); }
            };
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
            if (_career.Stage == CareerStage.Week)
            {
                // Сцена закулісся — там само, камера за мовцем; подія — гість на місці букера.
                stage.ShowBackstage();
                var sc = _career.BackstageOpen ? _career.CurrentBackstageScene : null;
                stage.SetBackstageScene(sc != null && sc.Kind == BackstageKind.Event ? sc.Id : null);
                return;
            }
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
            else if (phase == ShowPhase.Result && _career.GetShowResult() != null && _career.GetShowResult().Played
                && _career.GetShowResult().Winner != MatchWinner.Draw)
                stage.ShowAftermath(_career.GetShowResult().Winner == MatchWinner.Player);
            else stage.ShowIdle();
        }

        // ---------------- клавіші й бот ----------------

        private void HandleKeys()
        {
            if (MenuOverlayBusy) return;          // під завантаженням і розрізом екран клавіш не бере
            if (PollMenuInput()) return;          // меню (вхід, налаштування): свій ввід — клавіатура, геймпад, колесо
            // пауза (B01 §6.2): Esc (і перепризначене «Назад») або Start геймпада — на будь-якому екрані кар'єри; поки друкується
            // ім'я (поле вводу у фокусі), паузу відкриває лише Esc — перепризначена літера має друкуватись
            bool typing = _root.focusController != null && _root.focusController.focusedElement is TextField;
            if ((Input.GetKeyDown(KeyCode.Escape) || (!typing && (BoundDown(RingAction.Back) || Input.GetKeyDown(KeyCode.JoystickButton7)))) && CanPause)
            {
                OpenPause();
                return;
            }
            for (int i = 1; i <= 9; i++)
            {
                if (!Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + i - 1))) continue;
                _view.OnDigit(i);
                return;
            }
            // перепризначені в налаштуваннях «Журнал», «Підтвердити», «Назад» діють і на екранах кар'єри; заводські — запасні
            if (Input.GetKeyDown(KeyCode.L) || BoundDown(RingAction.Log)) _view.OnToggleLog();
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space) || BoundDown(RingAction.Confirm))
                _view.OnConfirm();
        }

        private int _fxSeen;
        private float _fxShotAt = -1f;
        private int _fxShotsLeft;

        /// <summary>Кадри великого моменту (фінішер, перемога): одразу й ще двічі з інтервалом 0,12 с — стробоскоп короткий.</summary>
        private void FxShots()
        {
            var stage = RingStageHub.Current;
            if (stage == null) return;
            if (stage.FxMoments != _fxSeen) { _fxSeen = stage.FxMoments; _fxShotsLeft = 3; _fxShotAt = 0f; }
            if (_fxShotsLeft <= 0 || Time.unscaledTime < _fxShotAt) return;
            _fxShotsLeft--;
            _fxShotAt = Time.unscaledTime + 0.12f;
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(_shotsDir, string.Format("{0:000}_fx_{1}.png", _shotIndex++, stage.FxLastMoment)));
        }

        private int _beatShot = -1;
        private float _beatShotAt = -1f;

        /// <summary>Кадр кожного такту режисера (кидок, утримання, фінішер) через 0,15 с — виноска вже на екрані.</summary>
        private void BeatShots()
        {
            var stage = RingStageHub.Current;
            if (stage == null || stage.BeatEvent == null) return;
            if (stage.BeatSeq != _beatShot) { _beatShot = stage.BeatSeq; _beatShotAt = Time.unscaledTime + 0.15f; return; }
            if (_beatShotAt < 0f || Time.unscaledTime < _beatShotAt || _shotIndex >= ShotLimit) return;
            _beatShotAt = -1f;
            var ev = stage.BeatEvent;
            if (ev.Kind == MatchEventKind.TurnEnd || ev.Kind == MatchEventKind.Sell) return;
            string kind = ev.Kind == MatchEventKind.Kickout ? "pin" + (int)ev.Tier : ev.Kind + "_" + ev.Action + (ev.HasRoll ? "_" + ev.Tier : "");
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(_shotsDir, string.Format("{0:000}_beat_{1}.png", _shotIndex++, kind)));
        }

        private void ShotsStep()
        {
            // Огляд перків з дією (06.11.2026): герой огляду має «Імпровізацію» і «Підкупленого суддю», щоб виноска з назвою перка
            // потрапила в кадр. Лише сейв бота (ring_career_bot.txt), гравця не торкається.
            if (_career.Stage == CareerStage.Week && _career.Feud != null && !_career.Feud.State.Perks.Contains(PerkCatalog.Improvise))
            { _career.Feud.State.Perks.Add(PerkCatalog.Improvise); _career.Feud.State.Perks.Add(PerkCatalog.BribedRef); }
            FxShots();
            BeatShots();
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
                // Матч: бот грає за героя тим самим ШІ, що й за суперника (раніше випадкові цифри — фінішера огляд так і не
                // побачив; власник 04.11.2026: «це погано»).
                var eng = !_onTitle ? _career.Engine : null;
                if (eng != null && !eng.IsOver && BotTurn(eng))
                {
                    MatchAi.Step(eng);
                    _view.Refresh();
                    return;
                }
                bool week = !_onTitle && _career.Stage == CareerStage.Week && !_career.BackstageOpen;
                if (week && (_career.State.ActionsLeft > 0 || _career.State.PromoChoicePending)) _view.OnDigit(1 + _shotDigit++ % 6);
                else if (_shotPressDigit) _view.OnDigit(1 + _shotDigit++ % 5);
                else _view.OnConfirm();
                _shotPressDigit = !_shotPressDigit;
                return;
            }
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(_shotsDir, string.Format("{0:000}_{1}.png", _shotIndex++, Label())));
            bool done = !_onTitle && _career.Stage == CareerStage.SeasonEnd && _career.State.Season >= _shotSeasons && _seasonShots++ >= 3;
            if (done || _shotIndex >= ShotLimit)
            {
                Debug.Log("[Ринг] Огляд: знято " + _shotIndex + " кадрів, сезон " + _career.State.Season + ", тиждень " + _career.State.Week);
                _shotsDir = null;
                _quitAt = Time.unscaledTime + 1f;
            }
        }

        private int _seasonShots;
        private int _shotSeasons = 1;

        private static bool BotTurn(MatchEngine e)
        {
            switch (e.Phase)
            {
                case MatchPhase.AwaitReaction: return e.PendingReaction.Defender == MatchSide.Player;
                case MatchPhase.AwaitBoost: return e.PendingRoll.Actor == MatchSide.Player;
                default: return e.CurrentActor == MatchSide.Player;
            }
        }

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
