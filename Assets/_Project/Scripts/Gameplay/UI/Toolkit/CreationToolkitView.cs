using System;
using System.Collections.Generic;
using Game.Core.Characters;
using Game.Core.Characters.Creation;
using Game.Core.Session;
using Game.Core.Session.Views;
using Game.Core.Stats;
using Game.Gameplay.Characters;
using Game.Gameplay.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Gameplay.UI.Toolkit
{
    /// <summary>
    /// Екран створення героя на UI Toolkit (Поправка №19.3; №17.4 — нових IMGUI-екранів не робимо). З плейтесту 13.10.2026
    /// («ЧОму ти не зробив як в інших Сrpg…») — кроки, як у CRPG: походження → клас → атрибути й навички → зовнішність →
    /// ім'я (<see cref="CreationSteps"/>). Ліворуч — вибір кроку, посередині живе 3D-прев'ю (тягнути мишею — поворот; на
    /// кроках «Зовнішність» і «Ім'я» камера наїжджає на обличчя), праворуч — аркуш героя з числами, внизу «Назад» /
    /// «Далі». Логіка вибору образу — <see cref="CreationLookModel"/>, числа й правила — ядро
    /// (<c>GameSession.AdjustProtagonistAttribute</c>, <c>SetProtagonistClass</c>). Щокадру екран перечитує сесію (її
    /// можуть змінити автотур чи «Продовжити»), а поле імені, поки в ньому фокус, не перезаписує.
    /// </summary>
    public sealed class CreationToolkitView : IShellOverlay
    {
        private readonly GameShell _shell;
        private GameObject _host;
        private UIDocument _document;
        private PanelSettings _panel;
        private VisualElement _screen;
        private CharacterPreviewRig _preview;
        private CreationLookModel _look;
        private string _pushedLook;
        private bool _wasVisible;
        private int _step;

        private Label _title, _stepCaption, _stepHint, _hint, _message;
        private Button _back, _next;
        private readonly List<Button> _tabs = new List<Button>();
        private readonly VisualElement[] _pages = new VisualElement[CreationSteps.Count];

        // походження
        private Label _genderCaption, _backgroundCaption;
        private Button _male, _female;
        private VisualElement _backgrounds;
        private readonly List<Button> _backgroundButtons = new List<Button>();
        private readonly List<string> _backgroundIds = new List<string>();
        private Gender _builtFor = (Gender)(-1);

        // клас
        private readonly List<Button> _classButtons = new List<Button>();
        private readonly List<CompanionClass> _classIds = new List<CompanionClass>();

        // атрибути
        private Label _points, _skillsCaption;
        private readonly Dictionary<AttributeType, StatRow> _attrRows = new Dictionary<AttributeType, StatRow>();
        private VisualElement _skillsList;

        // зовнішність
        private readonly Dictionary<CreationLookField, LookRow> _rows = new Dictionary<CreationLookField, LookRow>();

        // ім'я
        private Label _nameCaption;
        private TextField _name;

        // аркуш
        private Label _sheetTitle, _sheetName, _sheetOrigin, _sheetClass, _sheetClassDesc, _sheetAttrs, _sheetSkills;

        private Image _image;
        private bool _dragging;
        private float _lastX;

        private sealed class LookRow
        {
            public VisualElement Root;
            public Label Caption;
            public Label Value;
            public VisualElement Swatch;
        }

        private sealed class StatRow
        {
            public Label Name, Desc, Value;
            public Button Minus, Plus;
        }

        private CreationToolkitView(GameShell shell)
        {
            _shell = shell;
        }

        /// <summary>Міст для <see cref="GameShell"/> (рефлексія): null — лишається IMGUI-екран.</summary>
        public static IShellOverlay TryCreate(GameShell shell)
        {
            var view = new CreationToolkitView(shell);
            try
            {
                view.Build();
                return view;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Створення] UI Toolkit недоступний, лишаємо IMGUI: " + ex);
                view.Dispose();
                return null;
            }
        }

        public bool Handles(SessionState state) => state == SessionState.Creation && _screen != null;

        // ============================ побудова ============================

        private void Build()
        {
            var asset = Resources.Load<PanelSettings>(HudToolkitTheme.PanelResourcePath);
            _panel = asset != null ? UnityEngine.Object.Instantiate(asset) : ScriptableObject.CreateInstance<PanelSettings>();
            _panel.name = "AlphaCreationPanel";
            _panel.scaleMode = PanelScaleMode.ConstantPixelSize;
            _panel.scale = UiScale.PanelScale();
            _panel.sortingOrder = 1f; // поверх шапки HUD (яка в Creation однаково схована)
            if (_panel.themeStyleSheet == null)
            {
                var theme = Resources.Load<ThemeStyleSheet>(HudToolkitTheme.ThemeResourcePath);
                if (theme != null) _panel.themeStyleSheet = theme;
            }

            _host = new GameObject("AlphaCreation (UI Toolkit)");
            _host.SetActive(false);
            _document = _host.AddComponent<UIDocument>();
            _document.panelSettings = _panel;
            _host.SetActive(true);

            var root = _document.rootVisualElement;
            if (root == null) throw new InvalidOperationException("UIDocument.rootVisualElement == null");

            _preview = UnityEngine.Object.FindFirstObjectByType<CharacterPreviewRig>();
            if (_preview == null || _preview.Library == null || !_preview.Library.IsComplete)
                throw new InvalidOperationException("немає CharacterPreviewRig з набором (перезберіть сцену меню Alpha)");

            _screen = new VisualElement { name = "creation" };
            _screen.style.position = Position.Absolute;
            _screen.style.left = 0f; _screen.style.top = 0f; _screen.style.right = 0f; _screen.style.bottom = 0f;
            _screen.style.backgroundColor = new Color(0.06f, 0.055f, 0.05f, 0.97f);
            _screen.style.flexDirection = FlexDirection.Column;
            HudToolkitTheme.Padding(_screen, 18f, 24f);
            var font = HudToolkitTheme.LoadFont();
            if (font != null) _screen.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(font));
            _screen.style.fontSize = HudToolkitTheme.BodySize;
            _screen.style.color = HudToolkitTheme.TextColor;
            _screen.style.display = DisplayStyle.None;
            root.Add(_screen);

            var head = new VisualElement();
            head.style.flexDirection = FlexDirection.Row;
            head.style.alignItems = Align.Center;
            head.style.marginBottom = 12f;
            _title = HudToolkitTheme.Text(string.Empty, HudToolkitTheme.TitleSize + 6f, HudToolkitTheme.AccentColor, bold: true);
            _title.style.flexGrow = 1f;
            head.Add(_title);
            for (int i = 0; i < CreationSteps.Count; i++)
            {
                int step = i;
                var tab = MakeButton(string.Empty, () => GoTo(step));
                tab.style.marginLeft = 6f;
                head.Add(tab);
                _tabs.Add(tab);
            }
            _screen.Add(head);

            var columns = new VisualElement();
            columns.style.flexDirection = FlexDirection.Row;
            columns.style.flexGrow = 1f;
            _screen.Add(columns);

            columns.Add(BuildLeft());
            columns.Add(BuildCenter());
            columns.Add(BuildSheet());

            var bottom = new VisualElement();
            bottom.style.flexDirection = FlexDirection.Row;
            bottom.style.alignItems = Align.Center;
            bottom.style.marginTop = 12f;
            _message = HudToolkitTheme.Text(string.Empty, HudToolkitTheme.BodySize, HudToolkitTheme.WarningColor);
            _message.style.flexGrow = 1f;
            bottom.Add(_message);
            _back = MakeButton(string.Empty, () => GoTo(_step - 1));
            _back.style.minWidth = 160f;
            _back.style.marginRight = 8f;
            bottom.Add(_back);
            _next = MakeButton(string.Empty, OnNext, primary: true);
            _next.style.minWidth = 240f;
            bottom.Add(_next);
            _screen.Add(bottom);
        }

        private VisualElement Column(float width, bool grow)
        {
            var c = new VisualElement();
            c.style.flexDirection = FlexDirection.Column;
            if (grow) c.style.flexGrow = 1f; else c.style.width = width;
            c.style.backgroundColor = HudToolkitTheme.PanelColor;
            HudToolkitTheme.Border(c, 1f, HudToolkitTheme.BorderColor);
            HudToolkitTheme.Padding(c, 14f, 16f);
            c.style.marginRight = 12f;
            return c;
        }

        private VisualElement BuildLeft()
        {
            var col = Column(420f, false);
            _stepCaption = HudToolkitTheme.Text(string.Empty, HudToolkitTheme.ZoneTitleSize + 2f, HudToolkitTheme.AccentColor, bold: true);
            col.Add(_stepCaption);
            _stepHint = HudToolkitTheme.Text(string.Empty, HudToolkitTheme.BodySize - 2f, HudToolkitTheme.TextDimColor);
            _stepHint.style.whiteSpace = WhiteSpace.Normal;
            _stepHint.style.marginBottom = 6f;
            col.Add(_stepHint);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1f;
            col.Add(scroll);
            var body = scroll.contentContainer;

            _pages[CreationSteps.Origin] = BuildOrigin();
            _pages[CreationSteps.Class] = BuildClass();
            _pages[CreationSteps.Attributes] = BuildAttributes();
            _pages[CreationSteps.Look] = BuildLook();
            _pages[CreationSteps.Name] = BuildName();
            foreach (var p in _pages) body.Add(p);
            return col;
        }

        private VisualElement BuildOrigin()
        {
            var page = new VisualElement();
            _genderCaption = Caption(page);
            var genders = new VisualElement();
            genders.style.flexDirection = FlexDirection.Row;
            _male = MakeButton(string.Empty, () => SetGender(Gender.Male));
            _female = MakeButton(string.Empty, () => SetGender(Gender.Female));
            _male.style.flexGrow = 1f; _female.style.flexGrow = 1f;
            genders.Add(_male); genders.Add(_female);
            page.Add(genders);
            _backgroundCaption = Caption(page);
            _backgrounds = new VisualElement();
            page.Add(_backgrounds);
            return page;
        }

        private VisualElement BuildClass()
        {
            var page = new VisualElement();
            foreach (CompanionClass c in Enum.GetValues(typeof(CompanionClass)))
            {
                var captured = c;
                var card = Card(() => _shell.TryRun(() => _shell.Session.SetProtagonistClass(captured)));
                page.Add(card);
                _classButtons.Add(card);
                _classIds.Add(c);
            }
            return page;
        }

        private VisualElement BuildAttributes()
        {
            var page = new VisualElement();
            _points = HudToolkitTheme.Text(string.Empty, HudToolkitTheme.BodySize, HudToolkitTheme.AccentColor, bold: true);
            _points.style.marginTop = 6f;
            _points.style.marginBottom = 6f;
            page.Add(_points);
            foreach (var a in Attributes.All)
            {
                var attr = a;
                var row = new StatRow();
                var box = new VisualElement();
                box.style.marginBottom = 8f;
                var line = new VisualElement();
                line.style.flexDirection = FlexDirection.Row;
                line.style.alignItems = Align.Center;
                row.Name = HudToolkitTheme.Text(string.Empty, HudToolkitTheme.BodySize + 1f, HudToolkitTheme.TextColor, bold: true);
                row.Name.style.flexGrow = 1f;
                line.Add(row.Name);
                row.Minus = MakeButton("-", () => Adjust(attr, -1));
                line.Add(row.Minus);
                row.Value = HudToolkitTheme.Text(string.Empty, HudToolkitTheme.TitleSize, HudToolkitTheme.AccentColor, bold: true);
                row.Value.style.width = 54f;
                row.Value.style.unityTextAlign = TextAnchor.MiddleCenter;
                line.Add(row.Value);
                row.Plus = MakeButton("+", () => Adjust(attr, 1));
                line.Add(row.Plus);
                box.Add(line);
                row.Desc = HudToolkitTheme.Text(string.Empty, HudToolkitTheme.BodySize - 3f, HudToolkitTheme.TextDimColor);
                row.Desc.style.whiteSpace = WhiteSpace.Normal;
                box.Add(row.Desc);
                page.Add(box);
                _attrRows[attr] = row;
            }
            _skillsCaption = Caption(page);
            _skillsList = new VisualElement();
            page.Add(_skillsList);
            return page;
        }

        private VisualElement BuildLook()
        {
            var page = new VisualElement();
            foreach (CreationLookField f in Enum.GetValues(typeof(CreationLookField)))
                page.Add(BuildRow(f));
            return page;
        }

        private VisualElement BuildName()
        {
            var page = new VisualElement();
            _nameCaption = Caption(page);
            _name = new TextField { name = "creation-name" };
            _name.style.fontSize = HudToolkitTheme.BodySize + 2f;
            _name.RegisterValueChangedCallback(e => _shell.TryRun(() => _shell.Session.SetProtagonistName(e.newValue)));
            // Поле в тоні панелі: стандартне біле з блідим текстом читалось як вимкнене (тур 13.10.2026).
            var input = _name.Q(className: TextField.inputUssClassName);
            if (input != null)
            {
                input.style.backgroundColor = HudToolkitTheme.RaisedColor;
                input.style.color = HudToolkitTheme.TextColor;
                HudToolkitTheme.Border(input, 1f, HudToolkitTheme.AccentColor);
                HudToolkitTheme.Padding(input, 6f, 10f);
            }
            page.Add(_name);
            return page;
        }

        private VisualElement BuildCenter()
        {
            var col = Column(0f, true);
            col.style.alignItems = Align.Center;
            _image = new Image { name = "creation-preview", scaleMode = ScaleMode.ScaleToFit };
            _image.image = _preview.Texture;
            _image.style.flexGrow = 1f;
            _image.style.alignSelf = Align.Stretch;
            _image.RegisterCallback<PointerDownEvent>(e => { _dragging = true; _lastX = e.position.x; _image.CapturePointer(e.pointerId); });
            _image.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!_dragging) return;
                _preview.Yaw += (e.position.x - _lastX) * 0.6f;
                _lastX = e.position.x;
            });
            _image.RegisterCallback<PointerUpEvent>(e => { _dragging = false; _image.ReleasePointer(e.pointerId); });
            col.Add(_image);
            _hint = HudToolkitTheme.Text(string.Empty, HudToolkitTheme.BodySize - 2f, HudToolkitTheme.TextDimColor);
            col.Add(_hint);
            return col;
        }

        /// <summary>Аркуш героя — як у CRPG: усе обране одним поглядом, з числами.</summary>
        private VisualElement BuildSheet()
        {
            var col = Column(340f, false);
            col.style.marginRight = 0f;
            _sheetTitle = Caption(col);
            _sheetName = HudToolkitTheme.Text(string.Empty, HudToolkitTheme.TitleSize, HudToolkitTheme.TextColor, bold: true);
            col.Add(_sheetName);
            _sheetOrigin = SheetLine(col, HudToolkitTheme.TextDimColor);
            _sheetClass = SheetLine(col, HudToolkitTheme.AccentColor);
            _sheetClass.style.marginTop = 10f;
            _sheetClass.style.unityFontStyleAndWeight = FontStyle.Bold;
            _sheetClassDesc = SheetLine(col, HudToolkitTheme.TextDimColor);
            _sheetAttrs = SheetLine(col, HudToolkitTheme.TextColor);
            _sheetAttrs.style.marginTop = 12f;
            _sheetSkills = SheetLine(col, HudToolkitTheme.TextColor);
            _sheetSkills.style.marginTop = 12f;
            return col;
        }

        private static Label SheetLine(VisualElement parent, Color color)
        {
            var l = HudToolkitTheme.Text(string.Empty, HudToolkitTheme.BodySize - 1f, color);
            l.style.whiteSpace = WhiteSpace.Normal;
            parent.Add(l);
            return l;
        }

        private VisualElement BuildRow(CreationLookField field)
        {
            var row = new LookRow { Root = new VisualElement() };
            row.Root.style.flexDirection = FlexDirection.Column;
            row.Root.style.marginTop = 8f;
            row.Caption = HudToolkitTheme.Text(string.Empty, HudToolkitTheme.BodySize - 2f, HudToolkitTheme.TextDimColor);
            row.Root.Add(row.Caption);

            var line = new VisualElement();
            line.style.flexDirection = FlexDirection.Row;
            line.style.alignItems = Align.Center;
            line.Add(MakeButton("<", () => Step(field, -1)));
            row.Swatch = new VisualElement();
            row.Swatch.style.width = 22f; row.Swatch.style.height = 22f;
            row.Swatch.style.marginLeft = 8f;
            HudToolkitTheme.Border(row.Swatch, 1f, HudToolkitTheme.BorderColor);
            line.Add(row.Swatch);
            row.Value = HudToolkitTheme.Text(string.Empty, HudToolkitTheme.BodySize, HudToolkitTheme.TextColor);
            row.Value.style.flexGrow = 1f;
            row.Value.style.unityTextAlign = TextAnchor.MiddleCenter;
            line.Add(row.Value);
            line.Add(MakeButton(">", () => Step(field, 1)));
            row.Root.Add(line);
            _rows[field] = row;
            return row.Root;
        }

        private static Label Caption(VisualElement parent)
        {
            var l = HudToolkitTheme.Text(string.Empty, HudToolkitTheme.ZoneTitleSize, HudToolkitTheme.AccentColor, bold: true);
            l.style.marginTop = 10f;
            l.style.marginBottom = 4f;
            parent.Add(l);
            return l;
        }

        private static Button Card(Action onClick)
        {
            var card = MakeButton(string.Empty, onClick);
            card.style.flexDirection = FlexDirection.Column;
            card.style.alignItems = Align.FlexStart;
            card.style.marginBottom = 6f;
            card.style.whiteSpace = WhiteSpace.Normal;
            card.style.unityTextAlign = TextAnchor.UpperLeft;
            return card;
        }

        private static Button MakeButton(string text, Action onClick, bool primary = false)
        {
            var b = new Button(() => { SoundSettings.Request(SoundCue.UiClick); onClick(); }) { text = text };
            b.focusable = false;
            b.style.fontSize = HudToolkitTheme.BodySize;
            b.style.color = primary ? HudToolkitTheme.AccentColor : HudToolkitTheme.TextColor;
            var idle = HudToolkitTheme.RaisedColor;
            b.style.backgroundColor = idle;
            HudToolkitTheme.Padding(b, 6f, 12f);
            HudToolkitTheme.Border(b, 1f, primary ? HudToolkitTheme.AccentColor : HudToolkitTheme.BorderColor);
            b.RegisterCallback<PointerEnterEvent>(e => b.style.backgroundColor = HudToolkitTheme.HoverColor);
            b.RegisterCallback<PointerLeaveEvent>(e => b.style.backgroundColor = idle);
            return b;
        }

        // ============================ кожен кадр ============================

        public void Tick()
        {
            if (_screen == null) return;
            var session = _shell.Session;
            bool visible = session != null && session.State == SessionState.Creation;
            _screen.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            // Прев'ю спільне з «лялькою» — перемикаємо лише на переході, щоб не гасити чужий показ.
            if (visible != _wasVisible)
            {
                _preview.SetActive(visible);
                _wasVisible = visible;
                if (visible) _step = CreationSteps.Origin;
                else _preview.Zoom = 0f;
            }
            if (!visible) { _look = null; return; }
            if (CreationSteps.TourStep.HasValue) _step = CreationSteps.Clamp(CreationSteps.TourStep.Value);

            var view = session.GetProtagonistCreationView();
            var g = view.Gender;
            if (_shell.ProtagonistGender != g) _shell.ProtagonistGender = g;

            // Образ змінили ззовні (автотур, «Продовжити», зміна статі в ядрі) — підхопити.
            string sessionLook = view.Appearance.Encode();
            if (_look == null || sessionLook != _pushedLook)
            {
                if (_look == null) _look = new CreationLookModel(view.Appearance);
                else _look.Load(view.Appearance);
                _pushedLook = sessionLook;
            }

            if (_name.focusController == null || _name.focusController.focusedElement != _name)
            {
                string shown = view.Name ?? UkrainianText.Get("ui.creation.name.default", g);
                if (_name.value != shown) _name.SetValueWithoutNotify(shown);
            }

            if (_builtFor != g) RebuildBackgrounds(view, g);
            for (int i = 0; i < _pages.Length; i++)
                _pages[i].style.display = i == _step ? DisplayStyle.Flex : DisplayStyle.None;
            RefreshTexts(view, g);
            _preview.Zoom = CreationSteps.PreviewZoom(_step);
            _preview.Show(CharacterKitPlan.From(_look.Build(), null).Stowed());
            if (_image.image != _preview.Texture) _image.image = _preview.Texture;
        }

        private void RebuildBackgrounds(ProtagonistCreationView view, Gender g)
        {
            _builtFor = g;
            _backgrounds.Clear();
            _backgroundButtons.Clear();
            _backgroundIds.Clear();
            foreach (var id in view.AvailableBackgrounds)
            {
                string captured = id;
                var card = Card(() => _shell.TryRun(() => _shell.Session.SetProtagonistBackground(captured)));
                card.text = UkrainianText.Get("background." + id + ".label", g) + "\n" + UkrainianText.Get("background." + id, g);
                _backgrounds.Add(card);
                _backgroundButtons.Add(card);
                _backgroundIds.Add(id);
            }
        }

        private void RefreshTexts(ProtagonistCreationView view, Gender g)
        {
            _title.text = UkrainianText.Get("ui.creation.title", g);
            for (int i = 0; i < _tabs.Count; i++)
            {
                _tabs[i].text = (i + 1) + ". " + UkrainianText.Get(CreationSteps.TitleKeys[i], g);
                Mark(_tabs[i], i == _step);
            }
            _stepCaption.text = UkrainianText.Get(CreationSteps.TitleKeys[_step], g);
            _stepHint.text = UkrainianText.Get(CreationSteps.TitleKeys[_step] + ".hint", g);
            _hint.text = UkrainianText.Get("ui.creation.rotate_hint", g);
            _back.text = UkrainianText.Get("ui.creation.back", g);
            _back.SetEnabled(_step > 0);
            _next.text = UkrainianText.Get(_step == CreationSteps.Count - 1 ? "ui.creation.confirm" : "ui.creation.next", g);
            _message.text = _shell.LastMessage ?? string.Empty;

            // походження
            _genderCaption.text = UkrainianText.Get("ui.creation.gender", g);
            _backgroundCaption.text = UkrainianText.Get("ui.creation.background.title", g);
            _male.text = UkrainianText.Get("ui.creation.gender.male", g);
            _female.text = UkrainianText.Get("ui.creation.gender.female", g);
            Mark(_male, g == Gender.Male);
            Mark(_female, g == Gender.Female);
            for (int i = 0; i < _backgroundButtons.Count; i++)
                Mark(_backgroundButtons[i], _backgroundIds[i] == view.BackgroundId);

            // клас
            for (int i = 0; i < _classButtons.Count; i++)
            {
                string key = ClassKey(_classIds[i]);
                _classButtons[i].text = UkrainianText.Get(key, g) + "\n" + UkrainianText.Get(key + ".desc", g);
                Mark(_classButtons[i], _classIds[i] == view.Class);
            }

            // атрибути й навички
            _points.text = UkrainianText.Format("ui.creation.attributes.points", g,
                "moved", view.PointsMoved.ToString(), "budget", view.PointsBudget.ToString(), "free", view.FreePoints.ToString());
            foreach (var line in view.Attributes)
            {
                AttributeType type;
                StatRow row;
                if (!Enum.TryParse(line.Id, out type) || !_attrRows.TryGetValue(type, out row)) continue;
                string key = "attr." + line.Id.ToLowerInvariant();
                row.Name.text = UkrainianText.Get(key, g);
                row.Desc.text = UkrainianText.Get(key + ".desc", g);
                row.Value.text = line.Value == line.Base ? line.Value.ToString()
                    : line.Value + " (" + (line.Value > line.Base ? "+" : "") + (line.Value - line.Base) + ")";
                row.Minus.SetEnabled(line.CanLower);
                row.Plus.SetEnabled(line.CanRaise);
            }
            _skillsCaption.text = UkrainianText.Get("ui.creation.skills.title", g);
            if (_skillsList.childCount != view.Skills.Count)
            {
                _skillsList.Clear();
                foreach (var s in view.Skills) _skillsList.Add(HudToolkitTheme.Text(string.Empty, HudToolkitTheme.BodySize, HudToolkitTheme.TextColor));
            }
            for (int i = 0; i < view.Skills.Count; i++)
                ((Label)_skillsList[i]).text = UkrainianText.Get("skill." + view.Skills[i].Id.ToLowerInvariant(), g) + " — " + view.Skills[i].Value;

            // зовнішність
            foreach (var kv in _rows)
            {
                var f = kv.Key;
                var row = kv.Value;
                row.Root.style.display = _look.Visible(f) ? DisplayStyle.Flex : DisplayStyle.None;
                row.Caption.text = UkrainianText.Get(CaptionKey(f), g);
                string key = _look.ValueKey(f);
                string value;
                if (key != null) value = UkrainianText.Get(key, g);
                else if (f == CreationLookField.OutfitColor && _look.Index(f) == 0) value = UkrainianText.Get("ui.creation.outfit_color.default", g);
                else value = UkrainianText.Format("ui.creation.option", g, "n", (_look.Index(f) + 1).ToString(), "total", _look.Count(f).ToString());
                row.Value.text = value;
                string swatch = _look.Swatch(f);
                row.Swatch.style.display = swatch != null ? DisplayStyle.Flex : DisplayStyle.None;
                if (swatch != null)
                {
                    float r, gg, b;
                    CharacterKitPlan.ParseColor(swatch, out r, out gg, out b);
                    row.Swatch.style.backgroundColor = new Color(r, gg, b, 1f);
                }
            }

            // ім'я
            _nameCaption.text = UkrainianText.Get("ui.creation.name", g);

            // аркуш
            _sheetTitle.text = UkrainianText.Get("ui.creation.sheet.title", g);
            _sheetName.text = string.IsNullOrEmpty(view.Name) ? UkrainianText.Get("ui.creation.name.default", g) : view.Name;
            _sheetOrigin.text = UkrainianText.Get("background." + view.BackgroundId + ".label", g);
            string cls = ClassKey(view.Class);
            _sheetClass.text = UkrainianText.Get(cls, g);
            _sheetClassDesc.text = UkrainianText.Get(cls + ".desc", g);
            var attrs = new System.Text.StringBuilder();
            foreach (var line in view.Attributes)
                attrs.Append(UkrainianText.Get("attr." + line.Id.ToLowerInvariant(), g)).Append(" — ").Append(line.Value).Append('\n');
            _sheetAttrs.text = attrs.ToString().TrimEnd('\n');
            var skills = new System.Text.StringBuilder();
            foreach (var line in view.Skills)
                skills.Append(UkrainianText.Get("skill." + line.Id.ToLowerInvariant(), g)).Append(" — ").Append(line.Value).Append('\n');
            _sheetSkills.text = skills.ToString().TrimEnd('\n');
        }

        private static string ClassKey(CompanionClass c) => "class." + c.ToString().ToLowerInvariant();

        private static string CaptionKey(CreationLookField f)
        {
            switch (f)
            {
                case CreationLookField.Culture: return "ui.creation.culture";
                case CreationLookField.Hair: return "ui.creation.hair";
                case CreationLookField.HairColor: return "ui.creation.hair_color";
                case CreationLookField.FacialHair: return "ui.creation.facial";
                case CreationLookField.Outfit: return "ui.creation.outfit";
                default: return "ui.creation.outfit_color";
            }
        }

        private static void Mark(Button b, bool selected)
        {
            b.style.borderTopColor = b.style.borderBottomColor = b.style.borderLeftColor = b.style.borderRightColor =
                selected ? HudToolkitTheme.AccentColor : HudToolkitTheme.BorderColor;
            b.style.unityFontStyleAndWeight = selected ? FontStyle.Bold : FontStyle.Normal;
        }

        // ============================ дії ============================

        private void GoTo(int step)
        {
            _step = CreationSteps.Clamp(step);
        }

        private void OnNext()
        {
            if (_step < CreationSteps.Count - 1) GoTo(_step + 1);
            else OnConfirm();
        }

        private void Adjust(AttributeType attribute, int delta)
        {
            _shell.TryRun(() => _shell.Session.AdjustProtagonistAttribute(attribute, delta));
        }

        private void Step(CreationLookField field, int delta)
        {
            if (_look == null) return;
            _look.Step(field, delta);
            Push();
        }

        private void SetGender(Gender g)
        {
            if (_look == null) return;
            _shell.TryRun(() => _shell.Session.SetProtagonistGender(g));
            _shell.ProtagonistGender = g;
            _look.SetGender(g);
            Push();
        }

        /// <summary>Образ — у ядро тією ж командою, що й завжди; ядро ще раз його перевіряє.</summary>
        private void Push()
        {
            var a = _look.Build();
            bool ok = false;
            _shell.TryRun(() => ok = _shell.Session.SetProtagonistAppearance(a));
            if (ok) _pushedLook = a.Encode();
        }

        private void OnConfirm()
        {
            _preview.Zoom = 0f;
            _shell.TryRun(() => _shell.Session.ConfirmCreation());
        }

        public void Dispose()
        {
            if (_host != null) UnityEngine.Object.Destroy(_host);
            if (_panel != null) UnityEngine.Object.Destroy(_panel);
            _host = null;
            _panel = null;
            _screen = null;
        }
    }
}
