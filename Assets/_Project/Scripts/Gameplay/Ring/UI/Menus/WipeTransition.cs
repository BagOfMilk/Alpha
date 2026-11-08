using System;
using Game.Gameplay.Ring.Models;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Gameplay.Ring.UI.Menus
{
    /// <summary>
    /// Перехід між екранами «діагональний розріз» (ТЗ B08 §3): три смуги — кобальт, кармін, тло — проїжджають екран під
    /// кутом і закривають його (<see cref="BrandTokens.MotionMenuWipeMs"/>), хост міняє екран під ними, смуги їдуть далі
    /// й відкривають новий. Рух — лише translate шару; смуги намальовані раз.
    /// </summary>
    public sealed class WipeTransition
    {
        public readonly VisualElement Root;
        private readonly VisualElement _bands;
        private float _t = -1f, _width = 1600f, _height = 900f;
        private const float SlantH = 0.45f;   // зсув верху смуги відносно низу, частка висоти
        private Action _covered, _done;
        private bool _coverFired;

        public WipeTransition()
        {
            Root = MenuShapes.Layer();
            Root.style.overflow = Overflow.Hidden;
            Root.pickingMode = PickingMode.Ignore;
            Root.style.display = DisplayStyle.None;
            _bands = new VisualElement();
            _bands.style.position = Position.Absolute;
            _bands.style.top = 0f; _bands.style.bottom = 0f;
            _bands.style.left = Length.Percent(-10f);
            _bands.style.width = Length.Percent(180f);
            _bands.pickingMode = PickingMode.Ignore;
            _bands.generateVisualContent = Draw;
            Root.Add(_bands);
            Root.RegisterCallback<GeometryChangedEvent>(e =>
            {
                _width = Root.layout.width > 1f ? Root.layout.width : 1600f;
                _height = Root.layout.height > 1f ? Root.layout.height : 900f;
                _bands.MarkDirtyRepaint();
            });
        }

        public bool Busy { get { return _t >= 0f; } }

        private void Draw(MeshGenerationContext ctx)
        {
            var r = _bands.contentRect;
            float W = r.width, H = r.height;
            if (W < 4f || H < 4f) return;
            var p = ctx.painter2D;
            float k = SlantH * H;    // крута діагональ «удару»
            // смуги ліворуч направо (частки ширини шару): сталь 0–4 %, кармін 4–6 %, тло 6–86 % (закриває екран), золото 86–88 %
            Band(p, 0f, W * 0.04f, H, k, MenuPalette.Steel);
            Band(p, W * 0.04f, W * 0.02f, H, k, RingTheme.Villain);
            Band(p, W * 0.06f, W * 0.80f, H, k, RingTheme.Bg);
            Band(p, W * 0.86f, W * 0.02f, H, k, RingTheme.Gold);
        }

        private static void Band(Painter2D p, float x, float w, float h, float k, Color c)
        {
            MenuShapes.Polygon(p, new[] { new Vector2(x + k, 0f), new Vector2(x + k + w, 0f), new Vector2(x + w, h), new Vector2(x, h) }, c);
        }

        /// <summary>Почати: <paramref name="covered"/> — екран закрито (час міняти), <paramref name="done"/> — відкрито.</summary>
        public void Play(Action covered, Action done)
        {
            _covered = covered; _done = done;
            _t = 0f;
            _coverFired = false;
            Root.style.display = DisplayStyle.Flex;
            Root.BringToFront();
            RingMenuServices.Play(MenuSfx.Transition);
            Place(0f);
        }

        /// <summary>Хід: перша половина — смуги в'їжджають до закриття, друга — їдуть далі й відкривають.</summary>
        public void Tick(float dt)
        {
            if (_t < 0f) return;
            float cover = BrandTokens.MotionMenuWipeMs;
            _t += dt * 1000f;
            if (_t < cover) { Place(MenuMotion.Eval(Ease.InCubic, _t / cover)); return; }
            if (!_coverFired)
            {
                _coverFired = true;
                Place(1f);
                if (_covered != null) _covered();
                return;                // кадр під закритим екраном — хост ставить новий екран
            }
            float t2 = (_t - cover) / cover;
            Place(1f + MenuMotion.Eval(Ease.OutCubic, t2));
            if (t2 < 1f) return;
            _t = -1f;
            Root.style.display = DisplayStyle.None;
            if (_done != null) _done();
        }

        /// <summary>Вимкнути без анімації (огляд, помилка).</summary>
        public void Stop()
        {
            _t = -1f;
            Root.style.display = DisplayStyle.None;
        }

        // s: 0 — смуги ліворуч поза екраном, 1 — широка смуга тла закриває екран, 2 — смуги праворуч поза екраном
        private void Place(float s)
        {
            // шар: left = −10 % ширини екрана, ширина 180 %; x — зсув шару в пікселях панелі
            float W = _width, k = SlantH * _height;
            float start = -1.5f * W - k - 10f;                     // золото (правий край) ще ліворуч за екраном
            float hi = -(0.008f * W + k), lo = -0.448f * W;          // тло накриває екран повністю між lo і hi
            float coverX = hi > lo ? (lo + hi) * 0.5f : lo;
            float endX = 1.12f * W;                                 // кобальт (лівий край) уже праворуч за екраном
            float x = s <= 1f ? start + (coverX - start) * s : coverX + (endX - coverX) * (s - 1f);
            _bands.style.translate = new Translate(new Length(x), new Length(0f));
        }
    }
}
