using System;
using System.Collections.Generic;
using Game.Core.Characters;
using Game.Core.Characters.Creation;
using Game.Core.Session;
using Game.Core.Session.Views;
using Game.Gameplay.Characters;
using Game.Gameplay.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Gameplay.UI.Toolkit
{
    /// <summary>
    /// Екран створення героя на UI Toolkit (Поправка №19.3; №17.4 — нових IMGUI-екранів не робимо).
    /// Три колонки: ліворуч ім'я, рід і передісторія; посередині живе 3D-прев'ю (тягнути мишею —
    /// поворот); праворуч зовнішність — культура, зачіска, колір волосся, борода, вбрання, колір
    /// вбрання. Логіка вибору — <see cref="CreationLookModel"/> (чистий C#, під тестами), збирання
    /// моделі — <see cref="CharacterPreviewRig"/>. Усі зміни йдуть у ядро тими самими командами, що й
    /// у старому екрані, тож автотур і сейв не змінюються. Щокадру екран перечитує сесію (її можуть
    /// змінити автотур чи «Продовжити»), а поле імені, поки в ньому фокус, не перезаписує.
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

        private Label _title, _nameCaption, _genderCaption, _backgroundCaption, _lookCaption, _hint, _message;
        private TextField _name;
        private Button _male, _female, _confirm;
        private VisualElement _backgrounds;
        private readonly List<Button> _backgroundButtons = new List<Button>();
        private readonly List<string> _backgroundIds = new List<string>();
        private Image _image;
        private readonly Dictionary<CreationLookField, LookRow> _rows = new Dictionary<CreationLookField, LookRow>();
        private bool _dragging;
        private float _lastX;
        private Gender _builtFor = (Gender)(-1);

        private sealed class LookRow
        {
            public VisualElement Root;
            public Label Caption;
            public Label Value;
            public VisualElement Swatch;
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

            _title = HudToolkitTheme.Text(string.Empty, HudToolkitTheme.TitleSize + 6f, HudToolkitTheme.AccentColor, bold: true);
            _title.style.marginBottom = 12f;
            _screen.Add(_title);

            var columns = new VisualElement();
            columns.style.flexDirection = FlexDirection.Row;
            columns.style.flexGrow = 1f;
            _screen.Add(columns);

            columns.Add(BuildLeft());
            columns.Add(BuildCenter());
            columns.Add(BuildRight());

            var bottom = new VisualElement();
            bottom.style.flexDirection = FlexDirection.Row;
            bottom.style.alignItems = Align.Center;
            bottom.style.marginTop = 12f;
            _message = HudToolkitTheme.Text(string.Empty, HudToolkitTheme.BodySize, HudToolkitTheme.WarningColor);
            _message.style.flexGrow = 1f;
            bottom.Add(_message);
            _confirm = MakeButton(string.Empty, OnConfirm, primary: true);
            _confirm.style.minWidth = 240f;
            bottom.Add(_confirm);
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
            var col = Column(380f, false);
            _nameCaption = Caption(col);
            _name = new TextField { name = "creation-name" };
            _name.style.fontSize = HudToolkitTheme.BodySize;
            _name.RegisterValueChangedCallback(e => _shell.TryRun(() => _shell.Session.SetProtagonistName(e.newValue)));
            col.Add(_name);

            _genderCaption = Caption(col);
            var genders = new VisualElement();
            genders.style.flexDirection = FlexDirection.Row;
            _male = MakeButton(string.Empty, () => SetGender(Gender.Male));
            _female = MakeButton(string.Empty, () => SetGender(Gender.Female));
            _male.style.flexGrow = 1f; _female.style.flexGrow = 1f;
            genders.Add(_male); genders.Add(_female);
            col.Add(genders);

            _backgroundCaption = Caption(col);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1f;
            _backgrounds = scroll.contentContainer;
            col.Add(scroll);
            return col;
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

        private VisualElement BuildRight()
        {
            var col = Column(420f, false);
            col.style.marginRight = 0f;
            _lookCaption = Caption(col);
            foreach (CreationLookField f in Enum.GetValues(typeof(CreationLookField)))
                col.Add(BuildRow(f));
            return col;
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
            if (visible != _wasVisible) { _preview.SetActive(visible); _wasVisible = visible; }
            if (!visible) { _look = null; return; }

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
            RefreshTexts(view, g);
            _preview.Show(CharacterKitPlan.From(_look.Build(), null));
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
                var card = MakeButton(string.Empty, () => _shell.TryRun(() => _shell.Session.SetProtagonistBackground(captured)));
                card.style.flexDirection = FlexDirection.Column;
                card.style.alignItems = Align.FlexStart;
                card.style.marginBottom = 6f;
                card.style.whiteSpace = WhiteSpace.Normal;
                card.text = UkrainianText.Get("background." + id + ".label", g) + "\n" + UkrainianText.Get("background." + id, g);
                _backgrounds.Add(card);
                _backgroundButtons.Add(card);
                _backgroundIds.Add(id);
            }
        }

        private void RefreshTexts(ProtagonistCreationView view, Gender g)
        {
            _title.text = UkrainianText.Get("ui.creation.title", g);
            _nameCaption.text = UkrainianText.Get("ui.creation.name", g);
            _genderCaption.text = UkrainianText.Get("ui.creation.gender", g);
            _backgroundCaption.text = UkrainianText.Get("ui.creation.background.title", g);
            _lookCaption.text = UkrainianText.Get("ui.creation.look", g);
            _hint.text = UkrainianText.Get("ui.creation.rotate_hint", g);
            _confirm.text = UkrainianText.Get("ui.creation.confirm", g);
            _male.text = UkrainianText.Get("ui.creation.gender.male", g);
            _female.text = UkrainianText.Get("ui.creation.gender.female", g);
            Mark(_male, g == Gender.Male);
            Mark(_female, g == Gender.Female);
            for (int i = 0; i < _backgroundButtons.Count; i++)
                Mark(_backgroundButtons[i], _backgroundIds[i] == view.BackgroundId);
            _message.text = _shell.LastMessage ?? string.Empty;

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
        }

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
