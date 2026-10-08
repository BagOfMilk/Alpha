using System;
using System.Collections.Generic;
using Game.Core.Session;
using Game.Core.Session.Views;
using Game.Gameplay.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Gameplay.UI.Toolkit
{
    /// <summary>
    /// Вікно діалогу поверх світу (власник, 08.10.2026: «зробити діалоги між персонажами як в baldursgayte 3»;
    /// «Камера в світі, як у BG3»; Поправка №22 — діалог у вікні поверх світу на UI Toolkit; №17.4 — нових
    /// IMGUI-екранів не робимо). Унизу екрана: ім'я того, хто говорить, репліка, варіанти з номерами (поріг
    /// перевірки видно поруч — інваріант 8), «Далі». Камеру й постаті веде <see cref="DialogueStage"/>.
    ///
    /// Курсор сцени лишається в <see cref="SceneScreen"/>: кнопки вікна кличуть ті самі Driver*-методи, що й
    /// автотур, клавіші ловить <see cref="SceneScreen.HandleKeys"/>. Поза лінтом (як вікно створення героя):
    /// створюється через міст <c>TryCreate</c>; null — лишається IMGUI-сцена з портретами.
    /// </summary>
    public sealed class DialogueToolkitView : IShellOverlay
    {
        private static readonly Color Gold = new Color(0.86f, 0.73f, 0.47f, 1f);
        private static readonly Color PanelBg = new Color(0.05f, 0.045f, 0.04f, 0.86f);
        private static readonly Color OptionHover = new Color(0.86f, 0.73f, 0.47f, 0.16f);

        private readonly GameShell _shell;
        private GameObject _host;
        private UIDocument _document;
        private PanelSettings _panel;
        private DialogueStage _stage;

        private VisualElement _screen;
        private VisualElement _box;
        private Label _speaker;
        private Label _line;
        private VisualElement _body;

        private object _shownStep;
        private bool _shownConsequence;
        private float _appliedFactor = -1f;

        private DialogueToolkitView(GameShell shell)
        {
            _shell = shell;
        }

        /// <summary>Міст для <see cref="GameShell"/> (рефлексія): null — лишається IMGUI-сцена.</summary>
        public static IShellOverlay TryCreate(GameShell shell)
        {
            var view = new DialogueToolkitView(shell);
            try
            {
                view.Build();
                return view;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Діалог] UI Toolkit недоступний, лишаємо IMGUI: " + ex);
                view.Dispose();
                return null;
            }
        }

        public bool Handles(SessionState state) =>
            (state == SessionState.Scene || state == SessionState.Opening) && _screen != null;

        private void Build()
        {
            var asset = Resources.Load<PanelSettings>(HudToolkitTheme.PanelResourcePath);
            _panel = asset != null ? UnityEngine.Object.Instantiate(asset) : ScriptableObject.CreateInstance<PanelSettings>();
            _panel.name = "AlphaDialoguePanel";
            _panel.scaleMode = PanelScaleMode.ConstantPixelSize;
            _panel.scale = UiScale.PanelScale();
            _panel.sortingOrder = 2f;
            if (_panel.themeStyleSheet == null)
            {
                var theme = Resources.Load<ThemeStyleSheet>(HudToolkitTheme.ThemeResourcePath);
                if (theme != null) _panel.themeStyleSheet = theme;
            }

            _host = new GameObject("AlphaDialogue (UI Toolkit)");
            _host.SetActive(false);
            _document = _host.AddComponent<UIDocument>();
            _document.panelSettings = _panel;
            _host.SetActive(true);
            _stage = _host.AddComponent<DialogueStage>();
            _stage.Init(_shell);

            var root = _document.rootVisualElement;
            if (root == null) throw new InvalidOperationException("UIDocument.rootVisualElement == null");
            root.pickingMode = PickingMode.Ignore;

            _screen = new VisualElement { name = "dialogue" };
            _screen.pickingMode = PickingMode.Ignore;
            _screen.style.position = Position.Absolute;
            _screen.style.left = 0f; _screen.style.right = 0f; _screen.style.top = 0f; _screen.style.bottom = 0f;
            _screen.style.flexDirection = FlexDirection.Column;
            _screen.style.justifyContent = Justify.FlexEnd;
            _screen.style.alignItems = Align.Center;
            _screen.style.paddingBottom = 28f;
            var font = HudToolkitTheme.LoadFont();
            if (font != null) _screen.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(font));
            _screen.style.color = HudToolkitTheme.TextColor;
            _screen.style.display = DisplayStyle.None;
            root.Add(_screen);

            _box = new VisualElement { name = "dialogue-box" };
            _box.style.width = new Length(62f, LengthUnit.Percent);
            _box.style.maxWidth = 980f;
            _box.style.minWidth = 520f;
            _box.style.backgroundColor = PanelBg;
            _box.style.borderTopWidth = 2f;
            _box.style.borderTopColor = Gold;
            _box.style.borderBottomWidth = 1f;
            _box.style.borderBottomColor = new Color(Gold.r, Gold.g, Gold.b, 0.35f);
            _box.style.borderTopLeftRadius = 4f; _box.style.borderTopRightRadius = 4f;
            _box.style.borderBottomLeftRadius = 4f; _box.style.borderBottomRightRadius = 4f;
            HudToolkitTheme.Padding(_box, 16f, 26f);
            _screen.Add(_box);

            _speaker = HudToolkitTheme.Text("", 21f, Gold, bold: true);
            _speaker.style.marginBottom = 6f;
            _box.Add(_speaker);
            _line = HudToolkitTheme.Text("", 19f, HudToolkitTheme.TextColor);
            _line.style.marginBottom = 12f;
            _box.Add(_line);
            _body = new VisualElement { name = "dialogue-body" };
            _body.style.flexDirection = FlexDirection.Column;
            _box.Add(_body);
        }

        // ============================ кадр ============================

        public void Tick()
        {
            if (_screen == null) return;
            var session = _shell.Session;
            bool on = session != null && Handles(session.State) && _shell.Scene.Current != null;
            _screen.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (!on)
            {
                _stage.End();
                _shownStep = null;
                return;
            }

            float factor = UiScale.PanelScale();
            if (Mathf.Abs(factor - _appliedFactor) > 0.001f)
            {
                _panel.scale = factor;
                _appliedFactor = factor;
            }

            var scene = _shell.Scene;
            var current = scene.Current;
            _stage.Tick(current, scene.DisplaySpeakerId);

            if (ReferenceEquals(current, _shownStep) && scene.IsShowingConsequence == _shownConsequence) return;
            _shownStep = current;
            _shownConsequence = scene.IsShowingConsequence;
            Fill(current, scene);
        }

        private void Fill(SceneStepView current, SceneScreen scene)
        {
            var g = _shell.ProtagonistGender;
            var roster = _shell.Session.GetRosterView();
            string speakerId = scene.DisplaySpeakerId;
            string lineKey = scene.DisplayLineKey;
            _speaker.text = !string.IsNullOrEmpty(speakerId) ? ScreenText.ResolveCompanionName(speakerId, g, roster) : string.Empty;
            _speaker.style.display = string.IsNullOrEmpty(_speaker.text) ? DisplayStyle.None : DisplayStyle.Flex;
            _line.text = !string.IsNullOrEmpty(lineKey) ? UkrainianText.Get(lineKey, g) : string.Empty;
            _line.style.display = string.IsNullOrEmpty(_line.text) ? DisplayStyle.None : DisplayStyle.Flex;

            _body.Clear();
            if (scene.IsShowingConsequence)
            {
                var title = HudToolkitTheme.Text(UkrainianText.Get("ui.scene.consequence.title", g), 18f, Gold, bold: true);
                title.style.marginBottom = 4f;
                _body.Add(title);
                var events = scene.ConsequenceEvents;
                bool any = false;
                if (events != null)
                    foreach (var evt in events)
                    {
                        string text = ScreenText.EventLine(evt, g, roster);
                        if (string.IsNullOrEmpty(text)) continue;
                        any = true;
                        _body.Add(HudToolkitTheme.Text("• " + text, 17f, HudToolkitTheme.TextColor));
                    }
                if (!any) _body.Add(HudToolkitTheme.Text(UkrainianText.Get("ui.common.empty", g), 17f, HudToolkitTheme.TextDimColor));
                _body.Add(NextRow(g, () => _shell.Scene.DriverContinueConsequence()));
                return;
            }

            if (current.IsChoice && current.Options != null)
            {
                for (int i = 0; i < current.Options.Count; i++)
                {
                    int index = i;
                    string text = (i + 1) + ". " + ScreenText.SceneOptionLine(current.Options[i], g, roster);
                    _body.Add(OptionButton(text, () => _shell.Scene.DriverChoose(_shell, index)));
                }
                var choiceHint = HudToolkitTheme.Text(UkrainianText.Get("ui.dialogue.choice_hint", g), 15f, HudToolkitTheme.TextDimColor);
                choiceHint.style.marginTop = 6f;
                _body.Add(choiceHint);
                return;
            }

            _body.Add(NextRow(g, () => _shell.Scene.DriverAdvance(_shell)));
        }

        /// <summary>Варіант відповіді: рядок тексту на всю ширину, підсвітка при наведенні (як у BG3), без рамки кнопки.</summary>
        private static Button OptionButton(string text, Action onClick)
        {
            var b = new Button(() => { SoundSettings.Request(SoundCue.UiClick); onClick(); }) { text = text };
            b.focusable = false; // Пробіл/Enter не «натискають» кнопку у фокусі (UI-11)
            b.style.unityTextAlign = TextAnchor.MiddleLeft;
            b.style.whiteSpace = WhiteSpace.Normal;
            b.style.fontSize = 18f;
            b.style.color = HudToolkitTheme.TextColor;
            b.style.backgroundColor = new Color(0f, 0f, 0f, 0f);
            HudToolkitTheme.Border(b, 0f, new Color(0f, 0f, 0f, 0f));
            HudToolkitTheme.Padding(b, 5f, 8f);
            b.style.marginLeft = 0f; b.style.marginRight = 0f; b.style.marginTop = 1f; b.style.marginBottom = 1f;
            b.RegisterCallback<MouseEnterEvent>(_ => { b.style.backgroundColor = OptionHover; b.style.color = Gold; });
            b.RegisterCallback<MouseLeaveEvent>(_ => { b.style.backgroundColor = new Color(0f, 0f, 0f, 0f); b.style.color = HudToolkitTheme.TextColor; });
            return b;
        }

        private static VisualElement NextRow(Game.Core.Characters.Creation.Gender g, Action onClick)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.alignItems = Align.Center;
            row.style.marginTop = 8f;
            var hint = HudToolkitTheme.Text(UkrainianText.Get("ui.dialogue.hint", g), 15f, HudToolkitTheme.TextDimColor);
            row.Add(hint);
            var b = new Button(() => { SoundSettings.Request(SoundCue.UiClick); onClick(); }) { text = UkrainianText.Get("ui.scene.next", g) };
            b.focusable = false;
            b.style.fontSize = 17f;
            b.style.color = Gold;
            b.style.backgroundColor = new Color(Gold.r, Gold.g, Gold.b, 0.12f);
            HudToolkitTheme.Border(b, 1f, new Color(Gold.r, Gold.g, Gold.b, 0.55f));
            HudToolkitTheme.Padding(b, 5f, 18f);
            row.Add(b);
            return row;
        }

        public void Dispose()
        {
            if (_stage != null) _stage.End();
            if (_host != null) UnityEngine.Object.Destroy(_host);
            if (_panel != null) UnityEngine.Object.Destroy(_panel);
            _host = null;
            _panel = null;
            _screen = null;
        }
    }
}
