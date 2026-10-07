using System;
using System.Collections.Generic;
using System.Text;
using Game.Core.Session;
using Game.Gameplay.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Gameplay.UI.Toolkit
{
    /// <summary>
    /// Спайк H4 (docs/HUD_DESIGN.md §8; рішення власника 29.09.2026 «3.
    /// спробуємо»): шапка і стрічка міста на UI Toolkit. Бій лишається на
    /// IMGUI. ЩО показати рахують <see cref="HudHeaderModel"/> і
    /// <see cref="FeedModel"/> (чистий C#, headless-тести), ДЕ — <see cref="HudLayout"/>;
    /// цей клас лише будує дерево елементів і застосовує готові рядки.
    ///
    /// ЧОМУ ВСЕ СТВОРЮЄТЬСЯ В РАНТАЙМІ, А НЕ В GameSceneBuilder. Білдер сцени
    /// не лінтується (глибокий редактор), а спайк не можна перевірити запуском
    /// Unity з цієї сесії. Тут же кожне звернення до UIElements проходить лінт
    /// (критерій 3 §8), прапорець <c>-imgui-hud</c> вмикає старий варіант без
    /// перебудови сцени, і в сцені немає серіалізованих посилань на асети
    /// PanelSettings/UXML, які могли б мовчки загубитися. Єдиний асет —
    /// тема <c>Resources/AlphaHudTheme.tss</c>; без неї шапка все одно
    /// малюється (шрифт заданий явно).
    ///
    /// Кліки (критерій 5 §8): корінь і всі текстові елементи — <see cref="PickingMode.Ignore"/>;
    /// IMGUI-вміст отримує лише <see cref="HudFrame.Body"/>, який не
    /// перетинається з шапкою і стрічкою; на прогулянці прямокутники шапки і
    /// стрічки йдуть у <see cref="GameShell.ExploreUiRects"/>, тож клік по
    /// панелі не стає командою «йти». Кнопка «Прогулянка» не фокусована
    /// (<c>focusable = false</c>): інакше Пробіл/Enter, які в UI Toolkit
    /// «натискають» кнопку у фокусі, перехоплювали б клавіші сцени (UI-11).
    /// </summary>
    public sealed class HudToolkitView
    {
        private readonly GameShell _shell;
        private GameObject _host;
        private UIDocument _document;
        private PanelSettings _panel;

        private VisualElement _hud;
        private VisualElement _header;
        private Label _dayLabel;
        private VisualElement _bandChip;
        private Label _bandLabel;
        private VisualElement _ladderTip;
        private VisualElement _badgeRow;
        private Button _exploreButton;
        private Label _awayLabel;
        private VisualElement _resourceRow;

        private VisualElement _feed;
        private VisualElement _pendingZone;
        private VisualElement _pendingList;
        private VisualElement _importantZone;
        private VisualElement _importantList;
        private ScrollView _newsScroll;
        private Label _newsEmpty;
        private Label _newsMore;

        private bool _hovered;
        private string _signature;
        private float _appliedWidth = -1f, _appliedHeight = -1f;
        private bool _appliedExploring;
        private bool _visible = true;

        /// <summary>Дерево побудовано і панель живе — GameShell не малює IMGUI-шапку і стрічку.</summary>
        public bool IsReady { get; private set; }

        /// <summary>Автотур: примусово показати драбину (знімок стану «при наведенні»), без симуляції миші.</summary>
        public bool ForceLadderOpen { get; set; }

        /// <summary>Драбина зараз видима (наведення або <see cref="ForceLadderOpen"/>).</summary>
        public bool LadderShown => IsReady && _visible && (_hovered || ForceLadderOpen);

        // ---- підказка при наведенні (драбина полос, назва ресурсу) ----
        // Малює її GameShell в IMGUI, у кінці OnGUI: IMGUI малюється ПОВЕРХ
        // UI Toolkit, і підказка, що звисає з шапки на хаб, ховалась під його
        // панеллю (знімок Unity 29.09.2026). Поки хаб на IMGUI — підказка теж там;
        // коли хаб переїде на UI Toolkit, _ladderTip повертається в дерево.
        private VisualElement _tipAnchor;
        private readonly List<string> _tipLines = new List<string>();
        private int _tipCurrent = -1;

        /// <summary>Рядки підказки при наведенні; порожньо — підказки немає.</summary>
        public IReadOnlyList<string> HoverTipLines
        {
            get
            {
                if (LadderShown && LastHeader != null)
                {
                    var lines = new List<string>();
                    lines.Add(UkrainianText.Get("ui.hud.ladder.title", _shell.ProtagonistGender));
                    if (LastHeader.LadderLines != null) lines.AddRange(LastHeader.LadderLines);
                    return lines;
                }
                return _tipAnchor != null ? _tipLines : (IReadOnlyList<string>)new string[0];
            }
        }

        /// <summary>Індекс виділеного рядка підказки (поточна полоса драбини), -1 — без виділення.</summary>
        public int HoverTipCurrentIndex
        {
            get
            {
                if (LadderShown && LastHeader != null && LastHeader.LadderLines != null)
                {
                    string current = UkrainianText.Format("ui.hud.ladder.current", _shell.ProtagonistGender, "band", LastHeader.BandWord ?? string.Empty);
                    for (int i = 0; i < LastHeader.LadderLines.Count; i++)
                        if (LastHeader.LadderLines[i] == current) return i + 1; // +1 — рядок заголовка
                    return -1;
                }
                return _tipCurrent;
            }
        }

        /// <summary>Прямокутник елемента, до якого прив'язана підказка, у пікселях екрана (GUI, y згори).</summary>
        public Rect HoverTipGuiRect
        {
            get
            {
                var anchor = LadderShown ? _bandChip : _tipAnchor;
                if (anchor == null) return new Rect(0f, 0f, 0f, 0f);
                var wb = anchor.worldBound;
                float s = HudLayout.ScaleFor(Screen.width, Screen.height);
                return new Rect(wb.x * s, wb.y * s, wb.width * s, wb.height * s);
            }
        }

        /// <summary>Остання побудована шапка — автотур кладе її в лог разом зі знімком.</summary>
        public HudHeader LastHeader { get; private set; }

        /// <summary>Остання побудована стрічка.</summary>
        public FeedPanel LastFeed { get; private set; }

        private HudToolkitView(GameShell shell)
        {
            _shell = shell;
        }

        /// <summary>
        /// Створити вид; null — UI Toolkit недоступний (виняток при побудові).
        /// Тоді GameShell тихо лишається на IMGUI-шапці — гра не ламається через спайк.
        /// </summary>
        public static HudToolkitView TryCreate(GameShell shell)
        {
            var view = new HudToolkitView(shell);
            try
            {
                view.Build();
                return view.IsReady ? view : null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[HUD] UI Toolkit недоступний, лишаємо IMGUI: " + ex);
                view.Dispose();
                return null;
            }
        }

        private void Build()
        {
            float scale = HudLayout.ScaleFor(Screen.width, Screen.height);

            // Асет з редактора (Editor/HudPanelAssets) — першим: він тримає
            // посилання на рантайм-шейдери UI Toolkit і тему, тож білд їх не
            // вирізає. Копія — щоб зміна масштабу в Play Mode не бруднила асет.
            var asset = Resources.Load<PanelSettings>(HudToolkitTheme.PanelResourcePath);
            _panel = asset != null ? UnityEngine.Object.Instantiate(asset) : ScriptableObject.CreateInstance<PanelSettings>();
            if (asset == null) Debug.LogWarning("[HUD] Асет " + HudToolkitTheme.PanelResourcePath + " не знайдено — панель створено кодом.");
            _panel.name = "AlphaHudPanel";
            _panel.scaleMode = PanelScaleMode.ConstantPixelSize;
            _panel.scale = scale;
            _panel.sortingOrder = 0f;
            if (_panel.themeStyleSheet == null)
            {
                var theme = Resources.Load<ThemeStyleSheet>(HudToolkitTheme.ThemeResourcePath);
                if (theme != null) _panel.themeStyleSheet = theme;
                else Debug.LogWarning("[HUD] Тема " + HudToolkitTheme.ThemeResourcePath + ".tss не знайдена в Resources — текст малюється, смуги прокрутки ні.");
            }

            // Хост вимкнений, поки UIDocument не отримав PanelSettings: інакше
            // OnEnable документа відпрацює без панелі і дерево не під'єднається.
            _host = new GameObject("AlphaHud (UI Toolkit)");
            _host.SetActive(false);
            _document = _host.AddComponent<UIDocument>();
            _document.panelSettings = _panel;
            _host.SetActive(true);

            var root = _document.rootVisualElement;
            if (root == null) throw new InvalidOperationException("UIDocument.rootVisualElement == null після ввімкнення.");
            root.pickingMode = PickingMode.Ignore;

            _hud = new VisualElement { name = "alpha-hud" };
            _hud.pickingMode = PickingMode.Ignore;
            _hud.style.position = Position.Absolute;
            _hud.style.left = 0f;
            _hud.style.top = 0f;
            _hud.style.right = 0f;
            _hud.style.bottom = 0f;
            // Шрифт успадковується всім деревом — одна точка (HudToolkitTheme.LoadFont).
            var font = HudToolkitTheme.LoadFont();
            if (font != null) _hud.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(font));
            _hud.style.fontSize = HudToolkitTheme.BodySize;
            _hud.style.color = HudToolkitTheme.TextColor;
            root.Add(_hud);

            BuildHeader();
            BuildFeed();
            IsReady = true;
        }

        // ============================ шапка ============================

        private void BuildHeader()
        {
            var g = _shell.ProtagonistGender;
            _header = new VisualElement { name = "hud-header" };
            _header.style.position = Position.Absolute;
            _header.style.left = 0f;
            _header.style.top = 0f;
            _header.style.flexDirection = FlexDirection.Row;
            _header.style.alignItems = Align.Center;
            _header.style.backgroundColor = HudToolkitTheme.PanelColor;
            _header.style.borderBottomWidth = 1f;
            _header.style.borderBottomColor = HudToolkitTheme.BorderColor;
            HudToolkitTheme.Padding(_header, 0f, 14f);
            _hud.Add(_header);

            _dayLabel = HudToolkitTheme.Text(string.Empty, HudToolkitTheme.TitleSize, HudToolkitTheme.AccentColor, bold: true);
            _dayLabel.style.whiteSpace = WhiteSpace.NoWrap;
            _header.Add(_dayLabel);

            // Слово полоси — постійно; драбина — при наведенні (рішення власника «1. B»).
            _bandChip = new VisualElement { name = "hud-band" };
            _bandChip.style.marginLeft = 18f;
            _bandChip.style.backgroundColor = HudToolkitTheme.RaisedColor;
            HudToolkitTheme.Padding(_bandChip, 4f, 10f);
            HudToolkitTheme.Border(_bandChip, 1f, HudToolkitTheme.BorderColor);
            _bandChip.RegisterCallback<PointerEnterEvent>(OnBandEnter);
            _bandChip.RegisterCallback<PointerLeaveEvent>(OnBandLeave);
            _bandLabel = HudToolkitTheme.Text(string.Empty, HudToolkitTheme.BodySize, HudToolkitTheme.TextColor);
            _bandLabel.style.whiteSpace = WhiteSpace.NoWrap;
            _bandChip.Add(_bandLabel);
            _header.Add(_bandChip);

            _ladderTip = new VisualElement { name = "hud-ladder" };
            _ladderTip.pickingMode = PickingMode.Ignore;
            _ladderTip.style.position = Position.Absolute;
            _ladderTip.style.left = 0f;
            _ladderTip.style.top = Length.Percent(100f);
            _ladderTip.style.marginTop = 6f;
            _ladderTip.style.minWidth = 220f;
            _ladderTip.style.backgroundColor = HudToolkitTheme.PanelColor;
            HudToolkitTheme.Padding(_ladderTip, 8f, 12f);
            HudToolkitTheme.Border(_ladderTip, 1f, HudToolkitTheme.AccentColor);
            _ladderTip.style.display = DisplayStyle.None;
            _bandChip.Add(_ladderTip);

            _badgeRow = new VisualElement { name = "hud-badges" };
            _badgeRow.pickingMode = PickingMode.Ignore;
            _badgeRow.style.flexDirection = FlexDirection.Row;
            _badgeRow.style.marginLeft = 12f;
            _header.Add(_badgeRow);

            _exploreButton = new Button(OnExploreClicked) { name = "hud-explore" };
            _exploreButton.text = UkrainianText.Get("ui.explore.enter", g);
            _exploreButton.focusable = false;
            _exploreButton.style.marginLeft = 12f;
            _exploreButton.style.fontSize = HudToolkitTheme.BodySize;
            _exploreButton.style.color = HudToolkitTheme.TextColor;
            _exploreButton.style.backgroundColor = HudToolkitTheme.RaisedColor;
            HudToolkitTheme.Padding(_exploreButton, 4f, 12f);
            HudToolkitTheme.Border(_exploreButton, 1f, HudToolkitTheme.BorderColor);
            _exploreButton.RegisterCallback<PointerEnterEvent>(e => _exploreButton.style.backgroundColor = HudToolkitTheme.HoverColor);
            _exploreButton.RegisterCallback<PointerLeaveEvent>(e => _exploreButton.style.backgroundColor = HudToolkitTheme.RaisedColor);
            _header.Add(_exploreButton);

            var spacer = new VisualElement();
            spacer.pickingMode = PickingMode.Ignore;
            spacer.style.flexGrow = 1f;
            _header.Add(spacer);

            _awayLabel = HudToolkitTheme.Text(string.Empty, HudToolkitTheme.BodySize, HudToolkitTheme.AccentColor);
            _awayLabel.style.whiteSpace = WhiteSpace.NoWrap;
            _awayLabel.style.marginRight = 18f;
            _header.Add(_awayLabel);

            _resourceRow = new VisualElement { name = "hud-resources" };
            _resourceRow.pickingMode = PickingMode.Ignore;
            _resourceRow.style.flexDirection = FlexDirection.Row;
            _header.Add(_resourceRow);
        }

        private void OnBandEnter(PointerEnterEvent evt) { _hovered = true; ApplyLadderVisibility(); }

        private void OnBandLeave(PointerLeaveEvent evt) { _hovered = false; ApplyLadderVisibility(); }

        private void OnExploreClicked()
        {
            if (_shell.CanExplore && !_shell.Exploring && !_shell.EscapeOpen) _shell.SetExploring(true);
        }

        // ============================ стрічка ============================

        private void BuildFeed()
        {
            var g = _shell.ProtagonistGender;
            _feed = new VisualElement { name = "hud-feed" };
            _feed.style.position = Position.Absolute;
            _feed.style.flexDirection = FlexDirection.Column;
            _feed.style.backgroundColor = HudToolkitTheme.PanelColor;
            _feed.style.borderLeftWidth = 1f;
            _feed.style.borderLeftColor = HudToolkitTheme.BorderColor;
            HudToolkitTheme.Padding(_feed, 10f, 12f);
            _hud.Add(_feed);

            _pendingZone = Zone("ui.hud.feed.pending", g, out _pendingList);
            _importantZone = Zone("ui.hud.feed.important", g, out _importantList);
            _feed.Add(_pendingZone);
            _feed.Add(_importantZone);

            var newsTitle = ZoneTitle("ui.hud.feed.news", g);
            _feed.Add(newsTitle);
            _newsScroll = new ScrollView(ScrollViewMode.Vertical) { name = "hud-news" };
            _newsScroll.style.flexGrow = 1f;
            _newsScroll.style.flexShrink = 1f;
            _feed.Add(_newsScroll);
            _newsEmpty = HudToolkitTheme.Text(UkrainianText.Get("ui.feed.empty", g), HudToolkitTheme.BodySize, HudToolkitTheme.TextDimColor);
            _newsMore = HudToolkitTheme.Text(string.Empty, HudToolkitTheme.BodySize, HudToolkitTheme.TextDimColor);
            _newsMore.style.marginTop = 6f;
            _feed.Add(_newsMore);
        }

        private static Label ZoneTitle(string key, Game.Core.Characters.Creation.Gender g)
        {
            var title = HudToolkitTheme.Text(UkrainianText.Get(key, g).ToUpperInvariant(), HudToolkitTheme.ZoneTitleSize, HudToolkitTheme.AccentColor, bold: true);
            title.style.marginBottom = 4f;
            return title;
        }

        private static VisualElement Zone(string titleKey, Game.Core.Characters.Creation.Gender g, out VisualElement list)
        {
            var zone = new VisualElement();
            zone.pickingMode = PickingMode.Ignore;
            zone.style.marginBottom = 10f;
            zone.style.paddingBottom = 8f;
            zone.style.borderBottomWidth = 1f;
            zone.style.borderBottomColor = HudToolkitTheme.BorderColor;
            zone.Add(ZoneTitle(titleKey, g));
            list = new VisualElement();
            list.pickingMode = PickingMode.Ignore;
            zone.Add(list);
            return zone;
        }

        private static VisualElement FeedRow(FeedItem item, Game.Core.Characters.Creation.Gender g)
        {
            var row = new VisualElement();
            row.pickingMode = PickingMode.Ignore;
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = 4f;
            var color = HudToolkitTheme.SeverityColor(item.Severity);
            if (item.Severity != FeedSeverity.Neutral)
            {
                row.style.borderLeftWidth = 4f;
                row.style.borderLeftColor = color;
                row.style.paddingLeft = 6f;
                row.style.backgroundColor = HudToolkitTheme.RaisedColor;
            }
            var mark = HudToolkitTheme.Text(HudToolkitTheme.SeverityMark(item.Severity), HudToolkitTheme.BodySize, color, bold: true);
            mark.style.width = 16f;
            mark.style.flexShrink = 0f;
            row.Add(mark);
            var text = HudToolkitTheme.Text(FeedModel.DisplayText(item, g), HudToolkitTheme.BodySize, HudToolkitTheme.TextColor);
            text.style.flexGrow = 1f;
            text.style.flexShrink = 1f;
            row.Add(text);
            return row;
        }

        // ============================ кадр ============================

        /// <summary>Щокадру з GameShell.LateUpdate: видимість, розкладка, вміст — лише якщо щось змінилось.</summary>
        public void Tick()
        {
            if (!IsReady) return;
            bool show = _shell.CityHudVisible;
            if (show != _visible)
            {
                _visible = show;
                _hud.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                if (!show) _hovered = false;
            }
            if (!show) return;

            ApplyLayoutIfChanged();
            if (ShouldRefresh()) RefreshIfChanged();
            ApplyLadderVisibility();
        }

        // Статут PERF-01: повний підпис шапки збирає кілька видів сесії й рядків — не щокадру.
        // Дешеві ознаки (стан, довжина журналу, прогулянка, пауза) перевіряються щокадру і
        // оновлюють шапку одразу; решта змін (гаманець, рід) — не пізніше ніж за чверть секунди.
        private const float RefreshInterval = 0.25f;
        private float _sinceRefresh = RefreshInterval;
        private int _gateLogCount = -1;
        private SessionState _gateState;
        private bool _gateExploring, _gateEscape, _gateCanExplore;

        private bool ShouldRefresh()
        {
            var session = _shell.Session;
            int logCount = session?.DayLog?.Count ?? -1;
            var state = session != null ? session.State : default(SessionState);
            bool exploring = _shell.Exploring, escape = _shell.EscapeOpen, canExplore = _shell.CanExplore;

            _sinceRefresh += Time.unscaledDeltaTime;
            bool cheapChanged = logCount != _gateLogCount || state != _gateState || exploring != _gateExploring
                                || escape != _gateEscape || canExplore != _gateCanExplore;
            if (!cheapChanged && _sinceRefresh < RefreshInterval) return false;

            _sinceRefresh = 0f;
            _gateLogCount = logCount;
            _gateState = state;
            _gateExploring = exploring;
            _gateEscape = escape;
            _gateCanExplore = canExplore;
            return true;
        }

        private void ApplyLayoutIfChanged()
        {
            float w = Screen.width, h = Screen.height;
            bool exploring = _shell.Exploring;
            if (w == _appliedWidth && h == _appliedHeight && exploring == _appliedExploring) return;
            _appliedWidth = w;
            _appliedHeight = h;
            _appliedExploring = exploring;

            var frame = HudLayout.For(w, h, exploring);
            _panel.scale = frame.Scale;
            Place(_header, frame.Header, frame.Scale);
            Place(_feed, frame.Feed, frame.Scale);
        }

        /// <summary>Пікселі розкладки → одиниці панелі (панель множить їх на масштаб).</summary>
        private static void Place(VisualElement e, HudBox box, float scale)
        {
            e.style.left = HudLayout.ToUnits(box.X, scale);
            e.style.top = HudLayout.ToUnits(box.Y, scale);
            e.style.width = HudLayout.ToUnits(box.Width, scale);
            e.style.height = HudLayout.ToUnits(box.Height, scale);
        }

        private void RefreshIfChanged()
        {
            var session = _shell.Session;
            var view = session?.CurrentView;
            if (view == null) return;
            var g = _shell.ProtagonistGender;
            var economy = session.GetEconomyView();
            var roster = session.GetRosterView();
            var pending = session.State == SessionState.Decision ? session.GetPendingOffer() : null;
            var log = session.DayLog;
            bool exploreVisible = _shell.CanExplore && !_shell.Exploring;

            var sig = new StringBuilder(128);
            sig.Append(session.State).Append('|').Append(view.Day).Append('|').Append(view.Phase).Append('|')
               .Append(view.TensionBand).Append('|').Append(view.CrowdBand).Append('|')
               .Append(view.IsPatrolling).Append(view.IsFreePlay).Append('|')
               .Append(HudHeaderModel.CountAway(roster)).Append('|')
               .Append(pending?.TopicId).Append('|').Append(exploreVisible).Append(_shell.EscapeOpen).Append('|')
               .Append(g).Append('|').Append(_shell.Exploring).Append('|');
            // Ресурси — тим самим узагальненим списком, що йде в шапку: новий
            // рядок таблиці HudHeaderModel (два види матеріалів після злиття)
            // одразу потрапляє і в підпис, інакше шапка показувала б застаре значення.
            var resources = HudHeaderModel.ResourcesFrom(economy, g);
            foreach (var r in resources) sig.Append(r.Key).Append('=').Append(r.Value).Append(',');
            var weather = session.GetWeatherView();
            sig.Append('|').Append(weather?.Today).Append('>').Append(weather?.Tomorrow);
            sig.Append('|').Append(log?.Count ?? 0);
            if (log != null && log.Count > 0) sig.Append(log[log.Count - 1].Key).Append(log[log.Count - 1].Day);
            string signature = sig.ToString();
            if (signature == _signature) return;
            _signature = signature;

            var header = HudHeaderModel.Build(view, session.State, resources, roster, g, weather);
            LastHeader = header;
            ApplyHeader(header, exploreVisible);

            var feed = FeedModel.Build(log, pending, view.Day, g, roster);
            LastFeed = feed;
            ApplyFeed(feed, g);
        }

        private void ApplyHeader(HudHeader header, bool exploreVisible)
        {
            _dayLabel.text = header.DayLine ?? string.Empty;
            _bandLabel.text = header.BandLine ?? string.Empty;
            _bandChip.style.display = string.IsNullOrEmpty(header.BandLine) ? DisplayStyle.None : DisplayStyle.Flex;

            _ladderTip.Clear();
            _ladderTip.Add(HudToolkitTheme.Text(UkrainianText.Get("ui.hud.ladder.title", _shell.ProtagonistGender),
                HudToolkitTheme.BodySize, HudToolkitTheme.AccentColor, bold: true));
            string currentLine = UkrainianText.Format("ui.hud.ladder.current", _shell.ProtagonistGender, "band", header.BandWord ?? string.Empty);
            foreach (var line in header.LadderLines)
            {
                bool current = line == currentLine;
                var label = HudToolkitTheme.Text(line, HudToolkitTheme.BodySize,
                    current ? HudToolkitTheme.TextColor : HudToolkitTheme.TextDimColor, bold: current);
                label.style.whiteSpace = WhiteSpace.NoWrap;
                _ladderTip.Add(label);
            }

            _badgeRow.Clear();
            foreach (var badge in header.Badges)
            {
                var chip = HudToolkitTheme.Text(badge.Label, HudToolkitTheme.BodySize, HudToolkitTheme.TextColor);
                chip.style.whiteSpace = WhiteSpace.NoWrap;
                chip.style.marginRight = 8f;
                chip.style.backgroundColor = badge.Key == "patrolling" ? HudToolkitTheme.AccentColor : HudToolkitTheme.RaisedColor;
                HudToolkitTheme.Padding(chip, 2f, 8f);
                _badgeRow.Add(chip);
            }

            _exploreButton.style.display = exploreVisible ? DisplayStyle.Flex : DisplayStyle.None;
            _exploreButton.SetEnabled(!_shell.EscapeOpen);

            _awayLabel.text = header.AwayLine ?? string.Empty;
            _awayLabel.style.display = header.AwayLine == null ? DisplayStyle.None : DisplayStyle.Flex;

            _resourceRow.Clear();
            foreach (var r in header.Resources)
            {
                // Значок + число; назва ресурсу — у підказці при наведенні
                // (HUD_DESIGN §6.3: кожен значок має текстову підказку). Без
                // значка — назва словом, як було.
                var item = new VisualElement();
                item.pickingMode = PickingMode.Position;
                item.style.flexDirection = FlexDirection.Row;
                item.style.alignItems = Align.Center;
                item.style.marginLeft = 16f;

                var icon = HudToolkitTheme.Icon(r.Key);
                if (icon != null)
                {
                    var image = new VisualElement();
                    image.pickingMode = PickingMode.Ignore;
                    image.style.width = 22f;
                    image.style.height = 22f;
                    image.style.marginRight = 6f;
                    image.style.backgroundImage = new StyleBackground(icon);
                    image.style.unityBackgroundImageTintColor = HudToolkitTheme.AccentColor;
                    item.Add(image);
                }

                var label = HudToolkitTheme.Text(icon != null ? r.Value.ToString() : r.Label + " " + r.Value,
                    HudToolkitTheme.BodySize, HudToolkitTheme.TextColor);
                label.style.whiteSpace = WhiteSpace.NoWrap;
                item.Add(label);

                string tip = r.Label;
                item.RegisterCallback<PointerEnterEvent>(evt => ShowTip(item, tip));
                item.RegisterCallback<PointerLeaveEvent>(evt => HideTip(item));
                _resourceRow.Add(item);
            }
        }

        private void ApplyFeed(FeedPanel feed, Game.Core.Characters.Creation.Gender g)
        {
            FillZone(_pendingZone, _pendingList, feed.Pending, g);
            FillZone(_importantZone, _importantList, feed.Important, g);

            _newsScroll.Clear();
            if (feed.News.Count == 0) _newsScroll.Add(_newsEmpty);
            foreach (var item in feed.News) _newsScroll.Add(FeedRow(item, g));
            _newsScroll.scrollOffset = new Vector2(0f, 0f); // найновіше зверху — показуємо верх

            _newsMore.text = feed.HiddenNews > 0
                ? UkrainianText.Format("ui.hud.feed.more", g, "count", feed.HiddenNews.ToString())
                : string.Empty;
            _newsMore.style.display = feed.HiddenNews > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static void FillZone(VisualElement zone, VisualElement list, IReadOnlyList<FeedItem> items, Game.Core.Characters.Creation.Gender g)
        {
            list.Clear();
            foreach (var item in items) list.Add(FeedRow(item, g));
            zone.style.display = items.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void ApplyLadderVisibility()
        {
            // Драбину малює GameShell в IMGUI (див. HoverTipLines): дерево UI Toolkit
            // тримає її прихованою, інакше вона двоїлася б під панеллю хаба.
            _ladderTip.style.display = DisplayStyle.None;
        }

        private void ShowTip(VisualElement anchor, string line)
        {
            _tipAnchor = anchor;
            _tipLines.Clear();
            _tipLines.Add(line);
            _tipCurrent = -1;
        }

        private void HideTip(VisualElement anchor)
        {
            if (_tipAnchor != anchor) return;
            _tipAnchor = null;
            _tipLines.Clear();
        }

        /// <summary>Прибрати панель і документ (GameShell.OnDestroy).</summary>
        public void Dispose()
        {
            IsReady = false;
            if (_host != null) UnityEngine.Object.Destroy(_host);
            if (_panel != null) UnityEngine.Object.Destroy(_panel);
            _host = null;
            _panel = null;
        }
    }
}
