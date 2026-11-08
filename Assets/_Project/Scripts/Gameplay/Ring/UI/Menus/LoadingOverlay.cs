using System;
using System.Collections.Generic;
using Game.Gameplay.Ring.Models;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Gameplay.Ring.UI.Menus
{
    /// <summary>
    /// Екран завантаження (ТЗ B08 §6, мова «Енергія»): кобальтовий клин і напівтон, печатка-талісман з променями (обертається
    /// сам шар — без перемальовування), колажний заголовок, сегментна шкала з відсотком і назвою етапу, поради, що змінюються.
    /// Прогрес — реальні кроки хоста (<see cref="CompleteStep"/>); показ не блимає (мінімальний час) і не гальмує роботу:
    /// кожен кадр змінює лише transform/opacity і текст відсотка.
    /// </summary>
    public sealed class LoadingOverlay
    {
        public readonly VisualElement Root;
        private readonly RingText _t;
        private readonly MenuAnimator _anim = new MenuAnimator();
        private readonly LoadingProgress _progress;
        private readonly string[] _stepKeys;
        private readonly List<string> _tips = new List<string>();
        private readonly VisualElement _seal, _rays, _tipCard;
        private readonly SegmentBar _bar;
        private readonly Label _pct, _stage, _stageText, _tip;
        private int _tipShown = -1, _tipFirst;
        private float _spin;
        private bool _doneSignalled;
        private const float GroupW = 940f;

        public LoadingOverlay(RingText t, IList<string> stepKeys, IList<float> weights, int tipSeed)
        {
            _t = t;
            _stepKeys = new string[stepKeys.Count];
            stepKeys.CopyTo(_stepKeys, 0);
            _progress = new LoadingProgress(weights, BrandTokens.MotionLoadingMinMs);
            for (int i = 1; i <= 40; i++)
            {
                string k = "m.tip." + i;
                if (!t.Has(k)) break;          // Has — щоб пошук кінця списку не записав ключ у «відсутні»
                _tips.Add(t.Get(k));
            }
            _tipFirst = _tips.Count > 0 ? Math.Abs(tipSeed) % _tips.Count : 0;

            Root = MenuShapes.Layer();
            Root.pickingMode = PickingMode.Position;   // під екраном нічого не натискається
            Root.style.backgroundColor = RingTheme.Bg;
            Root.Add(new MenuBackdrop { Scrim = 0f, WedgeWidth = 0.58f, WedgeAlpha = 0.9f, CrimsonCut = true });
            Root.Add(new SpeedLines(7));

            // талісман: печатка «Рингу» з променями (власний знак гри, не персонаж)
            var mascot = new VisualElement();
            mascot.style.position = Position.Absolute;
            mascot.style.left = Length.Percent(50f); mascot.style.marginLeft = -230f; mascot.style.top = 140f;
            mascot.style.width = 460f; mascot.style.height = 460f;
            mascot.style.justifyContent = Justify.Center;
            mascot.style.alignItems = Align.Center;
            mascot.pickingMode = PickingMode.Ignore;
            _rays = new Rays();
            _rays.style.position = Position.Absolute;
            _rays.style.left = 0f; _rays.style.top = 0f; _rays.style.right = 0f; _rays.style.bottom = 0f;
            mascot.Add(_rays);
            _seal = new VisualElement();
            _seal.style.width = 210f; _seal.style.height = 210f;
            if (RingMenuArt.Seal != null) _seal.style.backgroundImage = new StyleBackground(RingMenuArt.Seal);
            _seal.pickingMode = PickingMode.Ignore;
            mascot.Add(_seal);
            Root.Add(mascot);

            // група низу: 940 завширшки, по центру екрана за будь-якої ширини (4:3 … 21:9)
            var box = new VisualElement();
            box.style.position = Position.Absolute;
            box.style.left = Length.Percent(50f); box.style.marginLeft = -GroupW * 0.5f; box.style.width = GroupW;
            box.style.top = 0f; box.style.bottom = 0f;
            box.pickingMode = PickingMode.Ignore;
            Root.Add(box);
            var head = MenuKit.Collage(t.Get("m.load.title"), 54f, 4);
            head.style.position = Position.Absolute;
            head.style.left = 0f; head.style.top = 618f;
            box.Add(head);

            _bar = new SegmentBar();
            _bar.style.position = Position.Absolute;
            _bar.style.left = 0f; _bar.style.top = 712f; _bar.style.width = 800f; _bar.style.height = 30f;
            box.Add(_bar);
            _pct = MenuKit.Display("0 %", 50f, RingTheme.Gold, RingFontRole.Display900, 0.02f);
            _pct.style.unityFontStyleAndWeight = FontStyle.Italic;
            _pct.style.position = Position.Absolute;
            _pct.style.left = 820f; _pct.style.top = 698f;
            box.Add(_pct);
            // рядок етапу — одним рядком на темній підкладці (ACC-03): «Борці й костюми» переносився й ховався під порадою,
            // а сірий «ЕТАП» на кармінному клині мав слабкий контраст (кадри 08.11.2026)
            var stageRow = new VisualElement();
            stageRow.style.position = Position.Absolute;
            stageRow.style.left = 0f; stageRow.style.top = 750f;
            stageRow.style.flexDirection = FlexDirection.Row;
            stageRow.style.alignItems = Align.Center;
            stageRow.pickingMode = PickingMode.Ignore;
            MenuKit.Underlay(stageRow);
            _stage = MenuKit.Display("", 18f, RingTheme.TextDim, RingFontRole.Display800, 0.12f);
            _stage.style.marginRight = 18f;
            stageRow.Add(_stage);
            _stageText = MenuKit.Body("", BrandTokens.TypeBody, RingTheme.Text, true);
            _stageText.style.whiteSpace = WhiteSpace.NoWrap;
            stageRow.Add(_stageText);
            box.Add(stageRow);

            // порада — паперова плашка (єдина паперова поверхня екрана)
            _tipCard = new VisualElement();
            _tipCard.style.position = Position.Absolute;
            _tipCard.style.left = 0f; _tipCard.style.top = 790f; _tipCard.style.width = GroupW;
            _tipCard.style.flexDirection = FlexDirection.Row;
            _tipCard.style.alignItems = Align.Center;
            _tipCard.style.paddingLeft = 26f; _tipCard.style.paddingRight = 24f; _tipCard.style.paddingTop = 10f; _tipCard.style.paddingBottom = 10f;
            _tipCard.style.backgroundColor = RingTheme.Paper;
            _tipCard.style.borderLeftWidth = 10f;
            _tipCard.style.borderLeftColor = RingTheme.Crimson;
            _tipCard.pickingMode = PickingMode.Ignore;
            var tipHead = MenuKit.Display(t.Get("m.load.tip"), 20f, RingTheme.Villain, RingFontRole.Display900, 0.12f);
            tipHead.style.marginRight = 18f;
            _tipCard.Add(tipHead);
            _tip = MenuKit.Body("", BrandTokens.TypeBody, RingTheme.Bg, true);
            _tip.style.flexShrink = 1f;
            _tipCard.Add(_tip);
            box.Add(_tipCard);
            if (_tips.Count == 0) _tipCard.style.display = DisplayStyle.None;

            Update(0f);
            _anim.FromTo(head, MenuProp.X, -140f, 0f, BrandTokens.MotionMenuPlateInMs, Ease.OutBack, 80f);
            _anim.FromTo(_seal, MenuProp.Scale, 0.6f, 1f, BrandTokens.MotionMenuPlateInMs * 2f, Ease.OutBack);
        }

        public LoadingProgress Progress { get { return _progress; } }

        /// <summary>Хост завершив крок роботи.</summary>
        public void CompleteStep() { _progress.CompleteStep(); }

        /// <summary>Можна йти: робота готова, шкала дійшла до 100 %, минув мінімальний час (екран не блимає).</summary>
        public bool CanExit { get { return _progress.CanExit; } }

        public void Tick(float dt)
        {
            float ms = dt * 1000f;
            _progress.Tick(ms);
            _anim.Tick(ms);
            Update(ms);
        }

        private void Update(float ms)
        {
            float f = _progress.Shown;
            _bar.Set(14, f * 14f, RingTheme.Gold, RingTheme.Border);
            _pct.text = (int)Math.Round(f * 100f) + " %";
            int step = Math.Min(_progress.StepAtShown(), _stepKeys.Length - 1);
            _stage.text = RingTheme.Caps(string.Format(_t.Get("m.load.stage"), step + 1, _stepKeys.Length));
            _stageText.text = _t.Get(_stepKeys[Math.Max(0, step)]);
            int tip = LoadingProgress.TipIndex(_progress.ElapsedMs, BrandTokens.MotionMenuTipEveryMs, _tipFirst, _tips.Count);
            if (tip >= 0 && tip != _tipShown)
            {
                _tipShown = tip;
                _tip.text = _tips[tip];
                _anim.FromTo(_tipCard, MenuProp.Opacity, 0f, 1f, BrandTokens.MotionMenuWipeMs, Ease.OutCubic);
                _anim.FromTo(_tipCard, MenuProp.X, -24f, 0f, BrandTokens.MotionMenuWipeMs, Ease.OutCubic);
            }
            if (!MenuAnimator.Reduced)
            {
                _spin += ms * 0.012f;                 // ≈ 12°/с — спокійно, не відволікає
                _rays.style.rotate = new Rotate(Angle.Degrees(_spin % 360f));
                float bob = (float)Math.Sin(_progress.ElapsedMs / 1000f * Math.PI * 0.8) * 6f;
                _seal.style.translate = new Translate(new Length(0f), new Length(bob));
            }
            if (_progress.WorkDone && f >= 0.999f && !_doneSignalled)
            {
                _doneSignalled = true;
                RingMenuServices.Play(MenuSfx.LoadDone);
                _anim.FromTo(_pct, MenuProp.Scale, 1.25f, 1f, BrandTokens.MotionStingerInMs, Ease.OutBack);
            }
        }
    }
}
