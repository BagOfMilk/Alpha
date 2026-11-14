using System;
using System.Collections.Generic;
using Game.Gameplay.Ring.Models;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Gameplay.Ring.UI.Menus
{
    /// <summary>
    /// Діалог підтвердження (вихід із гри, незбережені налаштування): затемнення `Scrim` α 0,72, колажний заголовок,
    /// речення й пластини варіантів. Навігація — та сама (↑/↓, Enter, Esc = останній варіант «назад»).
    /// </summary>
    public sealed class MenuDialog : IMenuNavigable
    {
        /// <summary>
        /// Відступи підкладки: праворуч більше — сходинка варіантів (14 на рядок), зсув фокуса (25) і ширша форма фокуса
        /// (<see cref="SlantPlate.FocusGrow"/>) мають лишатися на підкладці.
        /// </summary>
        public const float DialogPadLeft = 40f, DialogPadRight = 72f, DialogPadTop = 30f, DialogPadBottom = 20f;

        public readonly VisualElement Root;
        private readonly List<MenuButton> _buttons = new List<MenuButton>();
        private readonly List<Action> _actions = new List<Action>();
        private readonly MenuFocus _focus = new MenuFocus();
        private readonly MenuAnimator _anim;
        private readonly int _backIndex;
        private bool _closed;

        /// <summary>Варіанти: (текст, дія); <paramref name="backIndex"/> — що робить Esc/B.</summary>
        public MenuDialog(MenuAnimator anim, string title, string body, IList<KeyValuePair<string, Action>> options, int backIndex, int focusIndex = 0)
        {
            _anim = anim;
            _backIndex = backIndex;
            Root = MenuShapes.Layer();
            Root.pickingMode = PickingMode.Position;
            Root.style.backgroundColor = RingTheme.WithAlpha(RingTheme.Scrim, BrandTokens.AlphaScrim);
            Root.style.justifyContent = Justify.Center;
            Root.style.alignItems = Align.Center;
            // Підкладка діалогу — темна трапеція під заголовком, реченням і варіантами (огляд слоту 31, 12.11.2026): на одному
            // затемненні речення лежало просто на сцені (канат крізь текст на «Вийти?» і «На титул?»), а на 4:3 заголовок
            // «НЕ ЗБЕРЕЖЕНО» торкався підказки вкладки під затемненням.
            var card = new VisualElement();
            card.style.paddingLeft = DialogPadLeft; card.style.paddingRight = DialogPadRight;
            card.style.paddingTop = DialogPadTop; card.style.paddingBottom = DialogPadBottom;
            card.style.overflow = Overflow.Visible;
            card.generateVisualContent = ctx =>
            {
                float w = card.layout.width, h = card.layout.height;
                if (float.IsNaN(w) || w < 2f || h < 2f) return;
                MenuShapes.Trapezoid(ctx.painter2D, 0f, 0f, w, h, MenuShapes.SlantK, RingTheme.WithAlpha(RingTheme.Bg, BrandTokens.AlphaPlate));
            };
            card.RegisterCallback<GeometryChangedEvent>(e => card.MarkDirtyRepaint());
            var col = new VisualElement();
            col.style.alignItems = Align.FlexStart;
            col.style.maxWidth = 760f;
            col.Add(MenuKit.Collage(title, 52f, 1));
            var gap = new VisualElement(); gap.style.height = 18f; col.Add(gap);
            if (!string.IsNullOrEmpty(body))
            {
                var b = MenuKit.Body(body, BrandTokens.TypeLead, RingTheme.Text);
                b.style.maxWidth = 640f;
                b.style.marginBottom = 22f;
                col.Add(b);
            }
            var enabled = new List<bool>();
            for (int i = 0; i < options.Count; i++)
            {
                int idx = i;
                var btn = MenuKit.Button(options[i].Key, 420f, 56f);
                btn.style.marginBottom = 10f;
                btn.style.marginLeft = i * 14f;                   // сходинка — мова «Енергія»
                btn.OnHover = () => { if (RingMenuInput.MouseArmed && _focus.Set(idx)) { Sync(); RingMenuServices.Play(MenuSfx.Move); } };
                btn.OnClick = () => { _focus.Set(idx); Sync(); Choose(); };
                _buttons.Add(btn);
                _actions.Add(options[i].Value);
                enabled.Add(true);
                col.Add(btn);
            }
            card.Add(col);
            Root.Add(card);
            _focus.Reset(enabled, focusIndex);
            Sync();
            _anim.FromTo(Root, MenuProp.Opacity, 0f, 1f, BrandTokens.MotionMenuPlateInMs, Ease.OutCubic);
            for (int i = 0; i < _buttons.Count; i++)
                _anim.FromTo(_buttons[i], MenuProp.X, -80f, 0f, BrandTokens.MotionMenuPlateInMs, Ease.OutBack, MenuMotion.StaggerMs(i));
        }

        public bool Closed { get { return _closed; } }

        private void Sync()
        {
            for (int i = 0; i < _buttons.Count; i++)
            {
                bool f = i == _focus.Index;
                _buttons[i].State = f ? PlateState.Focus : PlateState.Normal;
                _anim.To(_buttons[i].Plate, MenuProp.X, f ? 25f : 0f, BrandTokens.MotionMenuFocusMs, Ease.OutCubic);
                _anim.To(_buttons[i].Text, MenuProp.X, f ? 25f : 0f, BrandTokens.MotionMenuFocusMs, Ease.OutCubic);
            }
        }

        private void Choose()
        {
            if (_closed || _focus.Index < 0) return;
            _closed = true;
            var act = _actions[_focus.Index];
            var b = _buttons[_focus.Index];
            b.State = PlateState.Pressed;
            _anim.FromTo(b, MenuProp.Scale, 0.98f, 1f, BrandTokens.MotionMenuPressMs, Ease.OutCubic);
            RingMenuServices.Play(_focus.Index == _backIndex ? MenuSfx.Back : MenuSfx.Confirm);
            Root.RemoveFromHierarchy();
            if (act != null) act();
        }

        public void OnMenu(RingAction action, MenuDevice device)
        {
            if (_closed) return;
            switch (action)
            {
                case RingAction.Up: if (_focus.Move(-1)) { Sync(); RingMenuServices.Play(MenuSfx.Move); } break;
                case RingAction.Down: if (_focus.Move(1)) { Sync(); RingMenuServices.Play(MenuSfx.Move); } break;
                case RingAction.Confirm: Choose(); break;
                case RingAction.Back:
                    if (_backIndex >= 0 && _backIndex < _buttons.Count) { _focus.Set(_backIndex); Choose(); }
                    break;
            }
        }
    }
}
