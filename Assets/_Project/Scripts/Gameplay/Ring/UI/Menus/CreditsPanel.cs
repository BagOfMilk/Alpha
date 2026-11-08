using System;
using Game.Gameplay.Ring.Models;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Gameplay.Ring.UI.Menus
{
    /// <summary>
    /// Титри (B01 §6.4): робоча назва, хто що зробив, ліцензійні атрибуції (шрифти OFL, звук CC0, власна генерація).
    /// Рядки — ключі <c>m.credits.*</c>; Esc/B/Enter — назад.
    /// </summary>
    public sealed class CreditsPanel : IMenuNavigable
    {
        private static readonly string[] Lines =
        {
            "m.credits.game", "m.credits.design", "m.credits.fonts", "m.credits.sound", "m.credits.music", "m.credits.art", "m.credits.engine",
        };

        public readonly VisualElement Root;
        private readonly MenuAnimator _anim;
        private readonly Action _close;
        private bool _closing;

        public CreditsPanel(RingText t, MenuAnimator anim, Action close)
        {
            _anim = anim; _close = close;
            Root = MenuShapes.Layer();
            Root.pickingMode = PickingMode.Position;
            Root.Add(new MenuBackdrop { Scrim = 0.86f, WedgeWidth = 0.5f, WedgeAlpha = 0.7f, CrimsonCut = false });
            var head = MenuKit.Collage(t.Get("m.credits.title"), 56f, 3);
            head.style.position = Position.Absolute;
            head.style.left = 80f; head.style.top = 44f;
            Root.Add(head);
            var col = new VisualElement();
            col.style.position = Position.Absolute;
            col.style.left = 80f; col.style.top = 170f; col.style.right = 80f;
            for (int i = 0; i < Lines.Length; i++)
            {
                var title = MenuKit.Display(t.Get(Lines[i] + ".h"), 24f, RingTheme.Gold, RingFontRole.Display800, 0.08f);
                title.style.marginTop = i == 0 ? 0f : 16f;
                col.Add(title);
                var body = MenuKit.Body(t.Get(Lines[i]), BrandTokens.TypeBody, RingTheme.Text);
                body.style.maxWidth = 1100f;
                col.Add(body);
                _anim.FromTo(title, MenuProp.X, -60f, 0f, BrandTokens.MotionMenuPlateInMs, Ease.OutCubic, MenuMotion.StaggerMs(i));
            }
            Root.Add(col);
            Label hints;
            Root.Add(MenuKit.Footer(out hints, null));
            hints.text = t.Get("m.footer.back");
            RegisterClose();
            _anim.FromTo(Root, MenuProp.Opacity, 0f, 1f, BrandTokens.MotionMenuWipeMs, Ease.OutCubic);
        }

        private void RegisterClose()
        {
            Root.RegisterCallback<PointerDownEvent>(e => Close());   // будь-який клік — назад (підказка в підвалі)
        }

        private void Close()
        {
            if (_closing) return;
            _closing = true;
            RingMenuServices.Play(MenuSfx.Back);
            _anim.To(Root, MenuProp.Opacity, 0f, BrandTokens.MotionMenuPlateOutMs, Ease.InCubic, 0f, _close);
        }

        public void OnMenu(RingAction action, MenuDevice device)
        {
            if (action == RingAction.Back || action == RingAction.Confirm) Close();
        }
    }
}
