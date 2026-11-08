using System;
using System.Collections.Generic;
using Game.Gameplay.Ring.UI;
using Game.Gameplay.Ring.UI.Menus;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Gameplay.Ring.Stage
{
    /// <summary>
    /// Програвач екрана виходу (ТЗ B07, Статут вигляду FX-09): з запеченого пулу збирає дерево шарів UI Toolkit у текстуру
    /// 1024×512 над завісою. Щокадру — лише translate / scale / rotate / opacity (+ ширина відкриття <c>clip</c>), без blur і без
    /// перерахунку розкладки; показ ступінчастий (gym 6, hall 12, arena 30 кадр/с). Кольори — лише токени через слоти
    /// (<see cref="EntranceScreenSlots"/>); написи — текстом (слоти nick · full · tag), шрифти — ролями <see cref="RingFonts"/>.
    /// «Менше руху» (<see cref="RingAccessibility"/>): без тремтіння, спалаху, spin · rock · blink · flick · sweep; pulse ≤ 3 %.
    /// Формули — <see cref="TronLayer.Evaluate"/> (паритет з Python — EntranceScreenTests). Не лінтується (тека Stage/Unity).
    /// </summary>
    public sealed class EntranceScreenPlayer
    {
        private const float Hpx = 512f, Wpx = 1024f;

        private readonly VisualElement _root;
        private readonly Dictionary<string, TextAsset> _poolAssets = new Dictionary<string, TextAsset>();
        private readonly Dictionary<string, TronPool> _pools = new Dictionary<string, TronPool>();
        private readonly Dictionary<string, Texture2D> _sprites = new Dictionary<string, Texture2D>();
        private readonly List<View> _views = new List<View>();

        private TronTimeline _tl;
        private EntranceRecipe _rec;
        private TronTier _tier;
        private bool _reduce, _noFlash, _noShake;
        private double _t, _lastShown = -1;
        private double _fps;

        public bool Playing { get { return _tl != null; } }
        public TronTimeline Timeline { get { return _tl; } }
        /// <summary>Секунди від початку ролика до удару (з урахуванням скороченого повтору).</summary>
        public double HitIn { get; private set; }
        public double Bpm { get { return _tl != null ? _tl.Bpm : 120; } }
        /// <summary>Скільки шарів зараз у дереві (Д6: ≤ 30).</summary>
        public int LayerCount { get { return _views.Count; } }
        public string Describe { get; private set; }

        private sealed class View
        {
            public TronLayer L;
            public VisualElement Outer, Content;
            public VisualElement Text;
            public float FullW, FullH, BaseW, BaseH, TileW, TileH, FitScale = 1f;
            public double LastClip = 1;
            /// <summary>Яскравість кольору шару (sRGB, 0..1) — для компенсації змішування (див. <see cref="Opacity"/>).</summary>
            public float Lum = 1f;
            public bool Clip, Tile, Hidden;
        }

        public EntranceScreenPlayer(VisualElement root, TextAsset[] pools, Texture2D[] sprites, Texture2D seal)
        {
            _root = root;
            if (pools != null) foreach (var p in pools) if (p != null) _poolAssets[p.name] = p;
            if (sprites != null) foreach (var s in sprites) if (s != null) _sprites[s.name] = s;
            if (seal != null) _sprites["brand_seal"] = seal;
        }

        public bool HasPool(string name) { return _poolAssets.ContainsKey(name); }

        private TronPool Pool(string name)
        {
            TronPool p;
            if (_pools.TryGetValue(name, out p)) return p;
            TextAsset a;
            if (!_poolAssets.TryGetValue(name, out a)) return null;
            try { p = TronPool.Parse(a.text); }
            catch (Exception ex) { Debug.LogError("[Ринг] екран виходу: пул " + name + " не читається: " + ex.Message); p = null; }
            _pools[name] = p;
            return p;
        }

        /// <summary>
        /// Почати ролик за рецептом. <paramref name="repeat"/> — повторний вихід цього борця за сесію: з <c>hitBeat − 1</c>
        /// (без тизера) і без тремтіння (B07 п. 4). false — пулу немає (екран лишається темним).
        /// </summary>
        public bool Play(EntranceRecipe r, bool repeat, bool lowTier)
        {
            Stop();
            var pool = Pool(r.PoolName);
            if (pool == null || pool.Variants.Count == 0) { Debug.LogWarning("[Ринг] екран виходу: немає пулу " + r.PoolName); return false; }
            _rec = r;
            _tl = pool.Variants[r.VariantOf(pool.Variants.Count)];
            _tier = EntranceScreenSlots.Tier(r.Tier);
            _reduce = RingAccessibility.ReduceMotion;
            _noFlash = _reduce || !RingAccessibility.Flashes;
            _noShake = _reduce || !RingAccessibility.ScreenShake || repeat;
            // GraphicsTier.Low — не більше 12 кадр/с (B07 п. 5, дешевий режим PERF-01).
            _fps = lowTier ? Math.Min(_tl.Fps, 12) : _tl.Fps;
            _t = repeat ? Math.Max(0, (_tl.HitBeat - 1) * 60.0 / _tl.Bpm) : 0;
            HitIn = _tl.HitTimeSec - _t;
            Describe = r.PoolName + " v" + _tl.Variant + (repeat ? " (повтор)" : "") + (_reduce ? " (менше руху)" : "");
            Build();
            Apply(true);
            return true;
        }

        public void Stop()
        {
            _tl = null;
            _views.Clear();
            _root.Clear();
            _lastShown = -1;
        }

        public void Tick(float dt)
        {
            if (_tl == null) return;
            _t += dt;
            Apply(false);
        }

        /// <summary>Огляд: стати точно на мить <paramref name="sec"/> від початку ролика (без накопичення похибки float).</summary>
        public void Seek(double sec)
        {
            if (_tl == null) return;
            _t = sec;
            Apply(false);
        }

        // ============================ збирання ============================

        private Color Tint(string tint)
        {
            string tok = EntranceScreenSlots.Token(tint, _tl.Role, _rec.Player, _tier.Mono);
            uint rgb;
            return EntranceScreenSlots.TryRgb(tok, out rgb) ? RingTheme.Rgb(rgb) : RingTheme.Paper;
        }

        private void Build()
        {
            _root.style.backgroundColor = RingTheme.Black;
            _root.style.overflow = Overflow.Hidden;
            foreach (var l in _tl.Layers)
            {
                if (l.IsFlash && _noFlash) continue;
                var v = new View { L = l };
                // Базовий розмір ящика — статичний w/h (або найбільший ключ доріжки); зміна w/h у часі — масштабом, без розкладки.
                v.BaseW = (float)Math.Max(0.001, MaxAbs(l.Tracks[TronLayer.PW], l.W));
                v.BaseH = (float)Math.Max(0.001, MaxAbs(l.Tracks[TronLayer.PH], l.H));
                float shear = l.Kind == "plate" ? Mathf.Abs(Mathf.Tan((float)l.Slant * Mathf.Deg2Rad)) * v.BaseH : 0f;
                v.FullW = (v.BaseW + shear) * Hpx;
                v.FullH = v.BaseH * Hpx;
                // Суміжні шари (половини завіси, плитки) стикаються рівно по краю — згладжування краю давало темний шов у
                // 1 піксель (Python округлює до цілих). Ящик заливки й спрайта — на піксель більший.
                if (l.Kind == "rect" || l.Kind == "sprite") { v.FullW += 1f; v.FullH += 1f; }
                if (l.Kind == "text") { v.FullW = Wpx; v.FullH = Hpx; }
                v.Clip = l.Tracks[TronLayer.PClip] != null || l.Clip < 0.999;

                var outer = v.Outer = new VisualElement { pickingMode = PickingMode.Ignore, name = l.Id };
                outer.style.position = Position.Absolute;
                outer.style.left = Wpx / 2f - v.FullW / 2f;
                outer.style.top = Hpx / 2f - v.FullH / 2f;
                outer.style.width = v.FullW;
                outer.style.height = v.FullH;
                outer.style.transformOrigin = new TransformOrigin(new Length(v.FullW / 2f), new Length(v.FullH / 2f), 0f);
                if (v.Clip) outer.style.overflow = Overflow.Hidden;
                var content = v.Content = new VisualElement { pickingMode = PickingMode.Ignore };
                content.style.position = Position.Absolute;
                content.style.left = 0; content.style.top = 0;
                content.style.width = v.FullW; content.style.height = v.FullH;
                outer.Add(content);

                switch (l.Kind)
                {
                    case "rect":
                        content.style.backgroundColor = Tint(l.Tint);
                        v.Lum = Lum(Tint(l.Tint));
                        break;
                    case "plate":
                    case "disc":
                    case "ring":
                        {
                            Color c = Tint(l.Tint);
                            v.Lum = Lum(c);
                            string kind = l.Kind;
                            float w = v.BaseW * Hpx, h = v.BaseH * Hpx, sh = shear * Hpx * Mathf.Sign((float)l.Slant), t = (float)l.T * Hpx;
                            content.generateVisualContent += ctx => Paint(ctx.painter2D, kind, c, w, h, sh, t);
                            break;
                        }
                    case "sprite":
                        BuildSprite(v);
                        break;
                    case "text":
                        if (!BuildText(v)) continue;
                        break;
                    default:
                        continue;
                }
                _views.Add(v);
                _root.Add(outer);
            }
        }

        private static float Lum(Color c) { return 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b; }

        /// <summary>
        /// Прозорість як у генераторі директора (змішування в sRGB). Проєкт лінійний, і UI Toolkit змішує в лінійному просторі:
        /// світлий шар на темному тлі виходить світлішим (промені й конуси кроків 0,18 · 0,34 · 0,58 світились), темний на
        /// світлому — блідішим (сканлінії на рожевій пластині майже зникали; кадри паритету 08.11.2026). Світлому шару α^2,2,
        /// темному 1 − (1 − α)^2,2 — точно над темним і над світлим тлом відповідно. «Темний» — майже чорний шар (ink: сканлінії,
        /// тіні; яскравість до ≈ 0,1), бо тло ролика — ink0: будь-який кольоровий шар лежить на темнішому за себе (синій теж —
        /// з плавною сумішшю за яскравістю промені героя на арені знову світились, паритет 08.11.2026).
        /// </summary>
        private static float Opacity(double a, float lum)
        {
            a = Math.Max(0, Math.Min(1, a));
            double light = Math.Pow(a, 2.2), dark = 1 - Math.Pow(1 - a, 2.2);
            double t = Math.Max(0, Math.Min(1, (lum - 0.07) / 0.07));
            double k = t * t * (3 - 2 * t);
            return (float)(dark + (light - dark) * k);
        }

        private static double MaxAbs(TronKey[] keys, double stat)
        {
            if (keys == null) return Math.Abs(stat);
            double m = 0;
            foreach (var k in keys) m = Math.Max(m, Math.Abs(k.V));
            return m;
        }

        private void BuildSprite(View v)
        {
            var l = v.L;
            Texture2D tex;
            if (!_sprites.TryGetValue(l.Sprite ?? "", out tex) || tex == null)
            {
                Debug.LogWarning("[Ринг] екран виходу: немає спрайта " + l.Sprite);
                v.Hidden = true;
                return;
            }
            var s = v.Content.style;
            s.backgroundImage = Background.FromTexture2D(tex);
            if (l.Tint != null) { s.unityBackgroundImageTintColor = Tint(l.Tint); v.Lum = Lum(Tint(l.Tint)); }
            if (l.Tile > 0)
            {
                v.Tile = true;
                v.TileH = Mathf.Max(2f, (float)l.Tile * Hpx);
                v.TileW = Mathf.Max(2f, v.TileH * tex.width / Mathf.Max(1f, tex.height));
                s.backgroundRepeat = new BackgroundRepeat(Repeat.Repeat, Repeat.Repeat);
                s.backgroundSize = new BackgroundSize(new Length(v.TileW), new Length(v.TileH));
                s.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Left, new Length(0f));
                s.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Top, new Length(0f));
            }
            else
            {
                s.backgroundRepeat = new BackgroundRepeat(Repeat.NoRepeat, Repeat.NoRepeat);
                s.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
            }
        }

        private const string NoBreakSpace = "\u00A0";

        /// <summary>Напис (або одна літера) зі стилем шару: шрифт ролі, кегль, колір, нахил, жорстка тінь, обведення.</summary>
        private Label Glyph(string text, TronText spec, string font, float size)
        {
            var lb = new Label(text) { pickingMode = PickingMode.Ignore };
            var s = lb.style;
            s.whiteSpace = WhiteSpace.NoWrap;
            s.unityTextAlign = TextAnchor.MiddleCenter;
            s.marginLeft = 0; s.marginRight = 0; s.marginTop = 0; s.marginBottom = 0;
            s.paddingLeft = 0; s.paddingRight = 0; s.paddingTop = 0; s.paddingBottom = 0;
            RingFonts.Apply(lb, FontRole(font));
            s.fontSize = size;
            s.color = Tint(spec.Fill ?? "$paper");
            if (spec.Slant > 0.5) s.unityFontStyleAndWeight = FontStyle.Italic;
            if (spec.ShadowTint != null)
                s.textShadow = new TextShadow { color = Tint(spec.ShadowTint), offset = new Vector2((float)spec.ShadowDx * Hpx, (float)spec.ShadowDy * Hpx), blurRadius = 0f };
            if (spec.OutlineTint != null)
            {
                s.unityTextOutlineColor = Tint(spec.OutlineTint);
                s.unityTextOutlineWidth = (float)spec.OutlineT * Hpx;
            }
            return lb;
        }

        private static RingFontRole FontRole(string key)
        {
            switch (key)
            {
                case "display700": return RingFontRole.Display700;
                case "display800": return RingFontRole.Display800;
                case "hand": return RingFontRole.Hand;
                case "text": return RingFontRole.Text;
                default: return RingFontRole.Display900;
            }
        }

        private bool BuildText(View v)
        {
            var spec = v.L.Text;
            if (spec == null) return false;
            string txt = _rec.SlotText(spec.Slot);
            if (string.IsNullOrEmpty(txt)) return false;
            string font = spec.Font ?? "display900";
            float size = (float)spec.Size * Hpx;
            // Рукописний у спортзалі (B07: «hand — у gym Amatic ×1,35»): лише для display-шрифтів.
            if (_tier.HandFont && spec.Hand && font.StartsWith("display", StringComparison.Ordinal)) { font = "hand"; size *= 1.35f; }
            v.Lum = Lum(Tint(spec.Fill ?? "$paper"));
            // Розрядка. Шрифти «Рингу» — звичайні TTF, і letter-spacing UI Toolkit на них ліг нерівно (частина літер її
            // ігнорувала: «ҐАН НА ЄВТУШЕН КО», кадри паритету 08.11.2026). Генератор директора рахує напис як суму ширин
            // окремих гліфів (без кернінгу) плюс розрядка — так само тут: помітна розрядка (≥ 0,05 кегля) — рядок з окремих
            // літер з відступом; мала — один напис без розрядки.
            VisualElement label;
            if (spec.Tracking >= 0.05)
            {
                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                float gap = (float)spec.Tracking * size;
                for (int i = 0; i < txt.Length; i++)
                {
                    // Пробіл — нерозривний: окремий пробіл UI Toolkit стискав би до нуля.
                    var g = Glyph(txt[i] == ' ' ? NoBreakSpace : txt[i].ToString(), spec, font, size);
                    if (i + 1 < txt.Length) g.style.marginRight = gap;
                    row.Add(g);
                }
                label = row;
            }
            else label = Glyph(txt, spec, font, size);
            v.Text = label;
            var s = label.style;
            s.position = Position.Absolute;
            s.left = v.FullW / 2f; s.top = v.FullH / 2f;
            s.translate = new Translate(Length.Percent(-50), Length.Percent(-50));
            v.Content.Add(label);
            // Після першої розкладки (один раз): fit — довге ім'я стискається по ширині, не переноситься; ящик шару — за
            // видимим текстом (як штамп у Python), щоб відкриття clip ішло по самому написові, а центр обертання — його центр.
            float fit = (float)spec.Fit * Hpx;
            var vv = v;
            label.RegisterCallback<GeometryChangedEvent>(e =>
            {
                float w = e.newRect.width, h = e.newRect.height;
                if (w < 1f || h < 1f) return;
                float k = fit > 1f && w > fit ? fit / w : 1f;
                float fw = w * k;
                if (Mathf.Abs(k - vv.FitScale) < 0.002f && Mathf.Abs(fw - vv.FullW) < 0.5f && Mathf.Abs(h * k - vv.FullH) < 0.5f) return;
                vv.FitScale = k;
                // Пропорційно (B07: «fit — пропорційне стиснення»; Python стискає штамп за обома осями).
                label.style.scale = new Scale(new Vector3(k, k, 1f));
                h *= k;
                vv.FullW = fw; vv.FullH = h;
                var os = vv.Outer.style;
                os.left = Wpx / 2f - fw / 2f; os.top = Hpx / 2f - h / 2f;
                os.width = vv.Clip ? fw * Mathf.Clamp01((float)vv.LastClip) : fw;
                os.height = h;
                os.transformOrigin = new TransformOrigin(new Length(fw / 2f), new Length(h / 2f), 0f);
                vv.Content.style.width = fw; vv.Content.style.height = h;
                label.style.left = fw / 2f; label.style.top = h / 2f;
            });
            return true;
        }

        private static void Paint(Painter2D p, string kind, Color c, float w, float h, float shear, float t)
        {
            p.fillColor = c;
            p.BeginPath();
            if (kind == "plate")
            {
                // Паралелограм у ящику w + |shear|: верхня грань зсунута праворуч (Python _plate_mask).
                float s = Mathf.Abs(shear);
                if (shear >= 0f) { p.MoveTo(new Vector2(s, 0f)); p.LineTo(new Vector2(s + w, 0f)); p.LineTo(new Vector2(w, h)); p.LineTo(new Vector2(0f, h)); }
                else { p.MoveTo(new Vector2(0f, 0f)); p.LineTo(new Vector2(w, 0f)); p.LineTo(new Vector2(w + s, h)); p.LineTo(new Vector2(s, h)); }
                p.ClosePath();
                p.Fill();
                return;
            }
            Ellipse(p, w * 0.5f, h * 0.5f, w * 0.5f, h * 0.5f);
            if (kind == "ring" && w - 2f * t > 2f && h - 2f * t > 2f)
            {
                Ellipse(p, w * 0.5f, h * 0.5f, w * 0.5f - t, h * 0.5f - t);
                p.Fill(FillRule.OddEven);
                return;
            }
            p.Fill();
        }

        private static void Ellipse(Painter2D p, float cx, float cy, float rx, float ry)
        {
            const int n = 48;
            p.MoveTo(new Vector2(cx + rx, cy));
            for (int i = 1; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                p.LineTo(new Vector2(cx + rx * Mathf.Cos(a), cy + ry * Mathf.Sin(a)));
            }
            p.ClosePath();
        }

        // ============================ кадр ============================

        private void Apply(bool force)
        {
            double shown = EntranceScreenMath.ShownBeat(_t, _tl.Bpm, _fps);
            if (!force && Math.Abs(shown - _lastShown) < 1e-9) return;
            _lastShown = shown;
            double shx, shy;
            _tl.Shake(shown, _noShake, out shx, out shy);
            var st = new TronState();
            foreach (var v in _views)
            {
                if (v.Hidden) { v.Outer.style.display = DisplayStyle.None; continue; }
                var l = v.L;
                l.Evaluate(shown, _reduce, ref st);
                var s = v.Outer.style;
                if (st.A <= 0.002) { s.visibility = Visibility.Hidden; continue; }
                s.visibility = Visibility.Visible;
                s.opacity = Opacity(st.A, v.Lum);
                double x = st.X, y = st.Y;
                if (!l.Fixed) { x += shx; y += shy; }
                if (!v.Tile) { x += st.Ox; y += st.Oy; }
                s.translate = new Translate(new Length((float)(x * Hpx)), new Length((float)(y * Hpx)));
                s.rotate = new Rotate(new Angle((float)st.Rot, AngleUnit.Degree));
                float kx, ky;
                if (l.Kind == "text") { kx = ky = (float)st.S; }
                else
                {
                    kx = (float)(st.W / v.BaseW * st.Sx * st.S);
                    ky = (float)(st.H / v.BaseH * st.Sy * st.S);
                }
                if (l.FlipX) kx = -kx;
                s.scale = new Scale(new Vector3(kx, ky, 1f));
                if (v.Clip) { v.LastClip = st.Clip; s.width = v.FullW * Mathf.Clamp01((float)st.Clip); }
                if (v.Tile)
                {
                    float ox = (float)(st.Ox * Hpx) % v.TileW, oy = (float)(st.Oy * Hpx) % v.TileH;
                    v.Content.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Left, new Length(-ox));
                    v.Content.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Top, new Length(-oy));
                }
            }
        }
    }
}
