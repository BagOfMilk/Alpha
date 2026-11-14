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
    ///
    /// Файли хоста за зонами сесій (docs/ring/ZONES.md, розподіл Етапу 0, 08.11.2026): тут — каркас (запуск, UI, кадр, шоу 3D,
    /// виходи, клавіші, огляд); <c>RingGameBootstrap.Career.cs</c> — кар'єра (сейв, дані, контекст і вибір екранів);
    /// <c>RingGameBootstrap.Match.cs</c> — бій (бот матчу в огляді, кадри тактів режисера); <c>RingGameBootstrap.Menus.cs</c> — меню.
    /// </summary>
    public sealed partial class RingGameBootstrap : MonoBehaviour, ICareerHost
    {
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
        /// <summary>Відкриття великого шоу ще не зіграно (перед першими виходами вечора) і ключ шоу, для якого вже зіграно.</summary>
        private bool _openPending;
        private string _openedShow;
        private object _stageKey;

        // ---------------- ICareerHost: застосунок (кар'єра — RingGameBootstrap.Career.cs) ----------------

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
                RingTime.Base = 3f;   // час лише через RingTime (правило меню): пауза й «Зменшити рух» множаться на базу
                // -ring-shot-speed 1 — справжня швидкість (огляд відчуття матчу й анімацій, а не лише екранів).
                float spd;
                string sp = ReadArg("-ring-shot-speed");
                if (sp != null && float.TryParse(sp, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out spd) && spd > 0f)
                    RingTime.Base = spd;
                Time.timeScale = RingTime.Scale;
                int lim;
                string ls = ReadArg("-ring-shot-limit");
                if (ls != null && int.TryParse(ls, out lim) && lim > 0) ShotLimit = lim;
                // Скільки сезонів проходить огляд (за замовчуванням 1; уся ворожнеча з Молотом — 3).
                string ss = ReadArg("-ring-shot-seasons");
                if (ss != null && int.TryParse(ss, out lim) && lim > 0) _shotSeasons = lim;
                // -ring-shot-grow: перед діями тижня бот витрачає очки розвитку, як CareerBot тестів балансу (прийом → атрибут → перк).
                _shotGrow = RingLaunchArgs.Has("-ring-shot-grow");
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

        private static ulong ReadSeed()
        {
            string s = ReadArg(SeedArg);
            ulong v;
            return s != null && ulong.TryParse(s, out v) ? v : (ulong)DateTime.UtcNow.Ticks;
        }

        /// <summary>Аргумент запуску; у Play Mode редактора — ще й з файла RingInEditor (див. <see cref="RingLaunchArgs"/>).</summary>
        private static string ReadArg(string name) { return RingLaunchArgs.Value(name); }

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
            // Екрани кар'єри B03 v2 (створення, тиждень, картка шоу, підсумок, кінець сезону) — та сама абсолютна розкладка за
            // безпечними зонами 900: на 120 % панель має ~750 одиниць заввишки, і афіша, шухляда, канати й ряди плиток налазять
            // одне на одне (кадри 1080 і 4:3 на 120 %, 08.11.2026). «Розмір інтерфейсу» діє на гнучкі екрани — матч і розмови.
            bool menu = _view is IMenuNavigable || _view is UI.CareerScreenView || _loading != null || !_bootDone || _pause != null;
            float s = Mathf.Clamp(Screen.height / 900f, 0.65f, 2.4f) * (_settings != null && !menu ? _settings.UiScale : 1f);
            if (Mathf.Abs(s - _appliedScale) >= 0.001f)
            {
                _appliedScale = s;
                _panel.scale = s;
                _appliedSafe = default(Rect);
            }
            ApplySafeArea();
        }

        private Rect _appliedSafe;

        /// <summary>
        /// Безпечна зона екрана (виріз камери, заокруглені кути, жести Android; аудит 08.11.2026, В4): корінь UI відступає
        /// від країв на стільки, скільки система забрала. На ПК безпечна зона — увесь екран, відступи нульові.
        /// Screen.safeArea — у пікселях від лівого нижнього кута; відступи панелі — у її одиницях (піксель / масштаб).
        /// </summary>
        private void ApplySafeArea()
        {
            if (_root == null || _panel == null) return;
            Rect safe = Screen.safeArea;
            if (safe == _appliedSafe) return;
            _appliedSafe = safe;
            float k = 1f / Mathf.Max(0.01f, _panel.scale);
            float w = Screen.width, h = Screen.height;
            bool sane = safe.width > 0f && safe.height > 0f;
            _root.style.left = sane ? Mathf.Max(0f, safe.xMin) * k : 0f;
            _root.style.right = sane ? Mathf.Max(0f, w - safe.xMax) * k : 0f;
            _root.style.top = sane ? Mathf.Max(0f, h - safe.yMax) * k : 0f;
            _root.style.bottom = sane ? Mathf.Max(0f, safe.yMin) * k : 0f;
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

        // ---------------- кадр ----------------

        private void Update()
        {
            if (_quitAt > 0f && Time.unscaledTime >= _quitAt) HardExit.Now(0);
            if (_fontSpike) return;   // спайк шрифтів: екран не оновлюється
            TickMenuOverlays();       // завантаження й розріз живуть і до появи кар'єри
            ApplySafeArea();          // поворот екрана чи зміна вирізу — і на титулі, і в грі
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
            if (!stage.ShowBusy && _openPending)
            {
                _openPending = false;
                stage.BeginShowOpen(ShowOpenTitle(), ShowOpenSub());
            }
            else if (!stage.ShowBusy && _entrances.Count > 0)
            {
                bool player = _entrances.Dequeue();
                var engine = _matchPending ?? _career.Engine;
                var f = engine != null ? engine.State.Get(player ? MatchSide.Player : MatchSide.Rival) : null;
                string name = f != null ? _text.Get(f.Stats.NameKey) : "";
                int rgb = player ? CareerLooks.CostumeRgb(_career.State.Costume) : CareerLooks.ForNpc(_career.State.Opponent).Rgb;
                // Персональний вихід (ENTRANCE_PROFILE.md) і екран виходу (ТЗ B07) — з тих самих даних кар'єри для обох.
                var npc = player ? null : _content.FindNpc(_career.State.Opponent);
                string body = player ? CareerLooks.HeroBodies[HeroBody] : CareerLooks.ForNpc(_career.State.Opponent).Body;
                bool female = body != null && body.StartsWith("body_f_", StringComparison.Ordinal);
                var input = player ? EntranceInput.ForPlayer(_career, female)
                    : npc != null ? EntranceInput.ForNpc(_career, npc, female) : new EntranceInput { Female = female };
                if (!player && npc == null && f != null) input.SetStats(f.Stats);
                // Рід тегу «ґімік · шлях»: герой — за формами героїні, суперник — за NpcDef.Female (тіло — запасне, без NPC).
                bool tagFemale = player ? HeroForms.ShowsHeroine(_text) : npc != null ? npc.Female : female;
                stage.BeginEntrance(player, EntranceRecipeFor(input, name, tagFemale), EntranceProfile.Compose(input), rgb);
            }
            // Лише шоу (вступ, вихід) ховає UI; кліпи матчу йдуть під панеллю матчу.
            bool showing = stage.ShowBusy || _entrances.Count > 0 || _openPending;
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

        /// <summary>Велике шоу вечора: ворожнеча, титул чи велике шоу тижня (плейтест 08.11.2026, P1-7).</summary>
        private bool BigShowNow
        {
            get
            {
                if (_career == null || _career.Stage != CareerStage.Show) return false;
                var st = _career.State;
                return st.IsBig || st.IsTitle || st.IsFeudShow;
            }
        }

        private string ShowOpenTitle()
        {
            var st = _career.State;
            return _text.Get(st.IsTitle ? "c.card.title_match" : st.IsFeudShow ? "c.open.feud" : "c.card.big");
        }

        private string ShowOpenSub()
        {
            var opp = _content.FindNpc(_career.State.Opponent);
            return opp == null ? "" : _text.Format("c.open.vs", _career.State.RingName, _text.Get(opp.NameKey));
        }

        /// <summary>
        /// Рецепт екрана виходу (ТЗ B07): ґімік і роль борця, тег «ґімік · шлях» ключами перекладу в роді борця
        /// (<see cref="EntranceScreenSlots.Tag"/>). Пул ролика — за сімейством ґіміка (Силач — монстр), назва в тезі — своя. Щабель —
        /// спортзал; велике шоу — зала, титул — арена (багатший ролик екрана, плейтест 08.11.2026, P1-7).
        /// </summary>
        private EntranceRecipe EntranceRecipeFor(EntranceInput input, string name, bool female)
        {
            string g = EntranceScreenSlots.GimmickKey(input.Gimmick);
            string tier = !BigShowNow ? "gym" : _career.State.IsTitle ? "arena" : "hall";
            return new EntranceRecipe
            {
                Gimmick = g, Role = EntranceScreenSlots.RoleOf(input.Path), Tier = tier, Name = name, Seed = 0, Player = input.Player,
                TagText = EntranceScreenSlots.Tag(_text, _content.GimmickOf(input.Gimmick).Key, input.Path, female),
            };
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
                // Люди кар'єри поза рингом (рішення власника 12.11.2026, № 10 і № 8): букер федерації в закуліссі, менеджерка в кутку.
                var fed = _career.Federation;
                stage.SetCareerCast(fed != null ? fed.Booker : null, _career.HasManager);
            }
            stage.SetBigShow(!_onTitle && BigShowNow);
            if (!_onTitle && _career.Stage == CareerStage.SeasonEnd)
            {
                // Кінець сезону — своя сцена: герой у центрі, добрий сезон — свято (плейтест 08.11.2026, P1-7).
                var rec = _career.State.SeasonRecord;
                var rep = _career.GetSeasonReport();
                stage.ShowSeasonEnd(rec[0] > rec[1] || (rep != null && (rep.TitleWon || rep.BeltIsPlayers)));
                return;
            }
            if (!_onTitle && _career.Stage == CareerStage.Create) { stage.ShowCreate(); return; }
            if (_onTitle) { stage.ShowIdle(); return; }
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
                // Велике шоу відкривається пролітками й назвою вечора — раз за шоу (перед першими виходами).
                string showKey = _career.State.Season + ":" + _career.State.Week + ":" + _career.State.IsTitle;
                _openPending = BigShowNow && showKey != _openedShow;
                if (_openPending) _openedShow = showKey;
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
            bool confirm = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space)
                || BoundDown(RingAction.Confirm);
            if (confirm && MatchQteInput.TryPress()) return;   // відкрите вікно QTE у матчі з'їдає «Підтвердити» (Ф3, зона бою)
            for (int i = 1; i <= 9; i++)
            {
                if (!Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + i - 1))) continue;
                _view.OnDigit(i);
                return;
            }
            // перепризначені в налаштуваннях «Журнал», «Підтвердити», «Назад» діють і на екранах кар'єри; заводські — запасні
            if (Input.GetKeyDown(KeyCode.L) || BoundDown(RingAction.Log)) _view.OnToggleLog();
            if (confirm) _view.OnConfirm();
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

        private void ShotsStep()
        {
            ReviewPerks();   // перки з дією для виносок — RingGameBootstrap.Career.cs
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
                // Матч: хід бота за героя — RingGameBootstrap.Match.cs (BotMatchStep); тиждень: розвиток і дії тижня —
                // RingGameBootstrap.Career.cs (BotWeekStep); деінде — цифра й «Далі» по черзі.
                if (BotMatchStep()) return;
                bool grew;
                if (BotWeekStep(out grew))
                {
                    if (!grew) _shotPressDigit = !_shotPressDigit;
                    return;
                }
                if (_shotPressDigit) _view.OnDigit(1 + _shotDigit++ % 5);
                else _view.OnConfirm();
                _shotPressDigit = !_shotPressDigit;
                return;
            }
            DumpScreenText(_shotIndex);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(_shotsDir, string.Format("{0:000}_{1}.png", _shotIndex++, Label())));
            bool done = !_onTitle && _career.Stage == CareerStage.SeasonEnd && _career.State.Season >= _shotSeasons && _seasonShots++ >= 3;
            if (done || _shotIndex >= ShotLimit)
            {
                Debug.Log("[Ринг] Огляд: знято " + _shotIndex + " кадрів, сезон " + _career.State.Season + ", тиждень " + _career.State.Week);
                if (RingStageHub.Current != null) Debug.Log(RingStageHub.Current.PlayedReport());
                _shotsDir = null;
                _quitAt = Time.unscaledTime + 1f;
            }
        }

        private string _lastDump;

        /// <summary>
        /// Текст екрана поруч із кадром (<c>screens.txt</c> у теці огляду): що саме гравцеві запропоновано й показано — щоб
        /// розбір проходу не вгадував вибори й числа з картинок. Лише нові екрани (той самий текст не повторюється).
        /// </summary>
        private void DumpScreenText(int index)
        {
            var sb = new System.Text.StringBuilder();
            CollectText(_root, sb);
            string s = sb.ToString();
            if (s == _lastDump) return;
            _lastDump = s;
            System.IO.File.AppendAllText(System.IO.Path.Combine(_shotsDir, "screens.txt"),
                string.Format("=== {0:000} {1} · t={3:0.0}с ===\n{2}\n", index, Label(), s, Time.realtimeSinceStartup));
        }

        private static void CollectText(VisualElement e, System.Text.StringBuilder sb)
        {
            if (e == null || e.style.display.value == DisplayStyle.None) return;
            var t = e as TextElement;
            if (t != null && !string.IsNullOrEmpty(t.text)) sb.Append(t.text.Replace("\n", " / ")).Append('\n');
            for (int i = 0; i < e.childCount; i++) CollectText(e[i], sb);
        }

        private void ShotNote(string line)
        {
            System.IO.File.AppendAllText(System.IO.Path.Combine(_shotsDir, "screens.txt"), "--- бот: " + line + "\n");
        }

        private int _seasonShots;
        private int _shotSeasons = 1;

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
