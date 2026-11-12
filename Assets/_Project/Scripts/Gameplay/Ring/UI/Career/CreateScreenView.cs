using System;
using System.Collections.Generic;
using Game.Core.Ring;
using Game.Core.Ring.Career;
using Game.Gameplay.Ring.Models;
using Game.Gameplay.Ring.Stage;
using Game.Gameplay.Ring.UI.Menus;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Gameplay.Ring.UI
{
    /// <summary>
    /// Створення борця (ТЗ B03 v2 §7, мова «Арена»; TRPG_DESIGN §5.1): ліворуч стрічка-поле імені (обводка й смужка — колір ролі) і
    /// плита «Вигляд» (колір костюма, тіло, зачіска); праворуч паперова афіша «Твій борець» (гімік, роль, опис ролі, вільний бал,
    /// п'ять характеристик) і вкладка «Прийоми»; унизу канати, ряд вибору (плитки 1–4 — гімік, 5–6 — роль) і золота плита
    /// «Почати кар'єру». Центр лишається вільним для 3D (герой і суддя на рингу). Логіка — як і раніше (CareerSession).
    /// </summary>
    public sealed class CreateScreenView : CareerScreenView
    {
        private readonly CharacterDraft _d = new CharacterDraft { RingName = "", FreePoint = RingAttr.Strength };
        private readonly CreationView _v;
        private bool _moves = ExpandForShots;
        private readonly List<ActionTile> _tiles = new List<ActionTile>();
        private Label _hint;
        private VisualElement _row, _head, _gold, _reasonBox, _ropes, _roleHead, _roleTag, _movesBox, _rightCol;
        private ArenaPlate _movesPlate;
        private string _hintDefault = "";
        private TextField _nameField;
        private int _focusNameAfter = -1;

        /// <summary>Курсор у поле імені — кадром пізніше за натискання, що його попросило (ціль фокусу «name»).</summary>
        public override void Tick(float unscaledDeltaTime)
        {
            base.Tick(unscaledDeltaTime);
            if (_focusNameAfter < 0 || Time.frameCount <= _focusNameAfter) return;
            _focusNameAfter = -1;
            if (_nameField != null && _nameField.panel != null) _nameField.Focus();
        }

        public CreateScreenView(RingUiContext ctx) : base(ctx)
        {
            _v = C.GetCreationView();
            _d.RingName = T.Get("c.create.default_name");
            _d.Gimmick = Gimmick.Monster;
            _d.FreePoint = RingAttr.Stamina;
            Root.RegisterCallback<GeometryChangedEvent>(e => Fit());
        }

        private GimmickView Current { get { foreach (var g in _v.Gimmicks) if (g.Def.Id == _d.Gimmick) return g; return _v.Gimmicks[0]; } }

        private int MoveCount() { int n = 0; foreach (var m in Current.StartMoves) n++; return n; }

        private static RingPath PathOf(CareerRole r) { return r == CareerRole.Hero ? RingPath.Face : RingPath.Heel; }

        /// <summary>Esc закриває розгорнуту картку «Прийоми»; інакше — пауза.</summary>
        public override bool OnBack()
        {
            if (!_moves) return false;
            _moves = false;
            Refresh();
            return true;
        }

        public override void Refresh()
        {
            Root.Clear();
            Root.style.paddingLeft = 0f; Root.style.paddingRight = 0f; Root.style.paddingTop = 0f; Root.style.paddingBottom = 0f;
            _tiles.Clear();
            RingPath path = PathOf(_d.Role);
            Color roleColor = ArenaKit.RoleColor(path);

            Root.Add(NameStrip(path, roleColor));
            Root.Add(LookPlate(roleColor));
            Root.Add(RightColumn(path, roleColor));

            _ropes = new RopeLine();
            ArenaKit.Place(_ropes, ArenaKit.Edge, null, ArenaKit.Edge, ArenaKit.RopesBottom, null, 22f);
            Root.Add(_ropes);
            Root.Add(RowHeader());

            _row = ArenaKit.Abs(ArenaKit.Edge, null, ArenaKit.Edge + ArenaKit.GoldW + 14f, ArenaKit.RowBottom, null, ArenaKit.TileH);
            _row.style.flexDirection = FlexDirection.Row;
            _row.pickingMode = PickingMode.Ignore;
            foreach (var g in _v.Gimmicks)
            {
                var gid = g.Def.Id;
                var tile = AddTile((int)gid + 1, T.Get(g.Def.NameKey), "",
                    () => { _d.Gimmick = gid; Refresh(); }, T.Get(g.Def.NameKey + ".desc"));
                tile.SetEffectParts(RingLabels.AttrBonusParts(T, g.BaseAttr));   // переноси лише між характеристиками
                tile.Selected = gid == _d.Gimmick;
            }
            // підпис «РОЛЬ» у самому ряду — видно лише на вузькій панелі, де ролі йдуть другим рядом (шапка «РОЛЬ» тоді прихована)
            _roleTag = new VisualElement();
            _roleTag.style.width = 86f; _roleTag.style.height = ArenaKit.TileH;
            _roleTag.style.justifyContent = Justify.Center;
            _roleTag.style.display = DisplayStyle.None;
            _roleTag.pickingMode = PickingMode.Ignore;
            var roleWord = MenuKit.Display(T.Get("c.create.role"), 27f, RingTheme.Text, RingFontRole.Display900, 0.02f);
            roleWord.style.unityFontStyleAndWeight = FontStyle.Italic;
            _roleTag.Add(roleWord);
            _row.Add(_roleTag);
            foreach (var r in new[] { CareerRole.Hero, CareerRole.Villain })
            {
                var rr = r;
                string key = r == CareerRole.Hero ? "role.hero" : "role.villain";
                var tile = AddTile(r == CareerRole.Hero ? 5 : 6, T.Get(key), "", () => { _d.Role = rr; Refresh(); }, T.Get(key + ".desc"));
                Color rc = ArenaKit.RoleColor(PathOf(r));
                tile.Accent = rc;
                tile.SelectColor = rc;
                tile.SelectDarkText = false;
                tile.Selected = r == _d.Role;
                tile.Sync();
            }
            Root.Add(_row);

            string reason;
            bool can = C.CanCreate(_d, out reason);
            _gold = new GoldPlate(T.Get("c.create.start"), ArenaKit.ConfirmHint(T), OnConfirm, can);
            ArenaKit.Place(_gold, null, null, ArenaKit.Edge, ArenaKit.RowBottom, ArenaKit.GoldW, ArenaKit.TileH);
            Root.Add(_gold);

            _reasonBox = ArenaKit.Abs(null, null, ArenaKit.Edge, ArenaKit.RowBottom + ArenaKit.TileH + 4f, ArenaKit.GoldW, 45f);
            _reasonBox.style.justifyContent = Justify.FlexEnd;
            if (!can)
            {
                var rl = MenuKit.Body(T.Get("create.reason." + reason), BrandTokens.TypeMinInfo, RingTheme.Signal, true);
                rl.style.unityTextAlign = TextAnchor.LowerRight;
                MenuKit.Underlay(rl);
                _reasonBox.Add(rl);
            }
            Root.Add(_reasonBox);
            Fit();
            ApplyLook();
        }

        /// <summary>Вузька панель (4:3, великий масштаб інтерфейсу): золота плита вужча, ряд і шапка ряду займають решту.</summary>
        private void Fit()
        {
            if (_row == null || _gold == null) return;
            float width = Root.layout.width;
            if (float.IsNaN(width) || width <= 0f) return;
            bool narrow = width < ArenaKit.NarrowWidth;
            float gw = CareerTileLayout.CreateGoldWidth(width);   // повна плита лише на 21:9 — інакше плитки ґіміків завузькі
            _gold.style.width = gw;
            float right = ArenaKit.Edge + gw + CareerTileLayout.GoldGap;
            _row.style.right = right;
            if (_head != null) _head.style.right = right;
            if (_reasonBox != null) _reasonBox.style.width = gw;
            // Вузька панель (кадри 4:3, 08.11.2026: три рядки характеристик гіміка не влазили в ~120 одиниць): два ряди —
            // гіміки зверху по чотири, ролі знизу з підписом «РОЛЬ»; канати й шапка піднімаються на ряд.
            float lift = narrow ? ArenaKit.TileH + 12f : 0f;
            _row.style.height = ArenaKit.TileH + lift;
            _row.style.flexWrap = narrow ? Wrap.Wrap : Wrap.NoWrap;
            // вузька плитка (16:9 — ~194, 4:3 — 195) — компактна, як на тижні: назва меншим кеглем, ключ на краї; на вузькій
            // панелі ширина задана точно (ґіміки по чотири), а не часткою — та сама, що міряє охоронець CareerTextFitTests
            float tileW = _tiles.Count > 0 ? CareerTileLayout.CreateTileWidth(width, _tiles.Count) : 0f;
            bool compact = _tiles.Count > 0 && CareerTileLayout.Compact(tileW);
            foreach (var t in _tiles) t.SetCompact(compact);
            foreach (var t in _tiles)
            {
                t.style.flexBasis = narrow ? new Length(tileW) : new Length(0f);
                t.style.flexGrow = narrow ? 0f : 1f;
                t.style.marginBottom = narrow ? 12f : 0f;
            }
            if (_roleTag != null) _roleTag.style.display = narrow ? DisplayStyle.Flex : DisplayStyle.None;
            if (_roleHead != null) _roleHead.style.display = narrow ? DisplayStyle.None : DisplayStyle.Flex;
            if (_ropes != null) _ropes.style.bottom = ArenaKit.RopesBottom + lift;
            if (_head != null) _head.style.bottom = ArenaKit.HeaderBottom + lift;
            
            // «Прийоми» на вузькій панелі: під афішею місця до піднятих канатів немає — блок стоїть над канатами праворуч і
            // росте вгору поверх низу афіші (картка над вкладкою), як шухляда тижня (кадр 4:3, 08.11.2026)
            if (_movesBox != null && _rightCol != null && _movesBox.parent != (narrow ? Root : _rightCol))
            {
                _movesBox.RemoveFromHierarchy();
                if (narrow)
                {
                    _movesBox.style.position = Position.Absolute;
                    _movesBox.style.right = ArenaKit.Edge; _movesBox.style.width = ArenaKit.StripW;
                    _movesBox.style.bottom = ArenaKit.RopesBottom + 22f + lift + 6f;
                    _movesBox.style.flexDirection = FlexDirection.ColumnReverse;
                    Root.Add(_movesBox);
                }
                else
                {
                    _movesBox.style.position = Position.Relative;
                    _movesBox.style.right = StyleKeyword.Auto; _movesBox.style.bottom = StyleKeyword.Auto;
                    _movesBox.style.flexDirection = FlexDirection.Column;
                    _rightCol.Add(_movesBox);
                }
            }
            // на вузькій панелі картка лежить поверх рядків характеристик: напівпрозора — крізь неї видно «+3», «Харизма»
            // (кадр 4:3, 08.11.2026); непрозора там, де під нею афіша, напівпрозора — над 3D
            if (_movesPlate != null)
            {
                _movesPlate.Fill = narrow ? RingTheme.Panel : RingTheme.WithAlpha(RingTheme.Panel, BrandTokens.AlphaPlate);
                _movesPlate.Redraw();
            }
        }

        private ActionTile AddTile(int key, string title, string effect, Action act, string desc)
        {
            var t = new ActionTile(key, title, effect, null, true);
            t.OnClick = act;
            t.OnHover = () =>
            {
                foreach (var o in _tiles) o.Focused = o == t;
                if (_hint != null) _hint.text = desc ?? "";
            };
            t.OnUnhover = () => { t.Focused = false; if (_hint != null) _hint.text = _hintDefault; };
            _tiles.Add(t);
            _row.Add(t);
            return t;
        }

        // ---------------- ліворуч: стрічка-поле імені, «Вигляд» ----------------

        private VisualElement NameStrip(RingPath path, Color roleColor)
        {
            var box = ArenaKit.Abs(ArenaKit.Edge, ArenaKit.Top, null, null, ArenaKit.StripW, ArenaKit.StripH);
            box.style.overflow = Overflow.Visible;
            box.Add(new ArenaPlate { Fill = RingTheme.WithAlpha(RingTheme.Panel, BrandTokens.AlphaPanel), Outline = roleColor, Stripe = roleColor });
            var label = ArenaKit.Tag(T.Get("c.create.name"), RingTheme.TextDim);
            ArenaKit.Place(label, 24f, 10f, null, null);
            box.Add(label);
            var tag = ArenaKit.Row();
            ArenaKit.Place(tag, null, 10f, 34f, null);
            // пробіл після «·» — на початку наступного напису: кінцевий пробіл напис не малює (кадри слоту 27, 09.11.2026)
            tag.Add(ArenaKit.Tag(T.Get("gimmick." + C.Content.GimmickOf(_d.Gimmick).Key) + " ·", RingTheme.TextDim));
            tag.Add(ArenaKit.Tag(" " + RingLabels.PathName(T, path), ArenaKit.RoleText(path)));
            box.Add(tag);

            var well = ArenaKit.Abs(24f, 36f, 38f, null, null, 58f);
            well.pickingMode = PickingMode.Position;
            well.style.backgroundColor = RingTheme.PanelRaised;
            var field = new TextField { value = _d.RingName, maxLength = _v.MaxNameLength };
            RingFonts.Apply(field, RingFontRole.Display900);
            field.style.fontSize = 36f;
            field.style.color = RingTheme.Text;
            field.style.backgroundColor = Color.clear;
            field.style.position = Position.Absolute;
            field.style.left = 8f; field.style.right = 8f; field.style.top = 2f; field.style.height = 50f;
            field.style.marginLeft = 0f; field.style.marginRight = 0f; field.style.marginTop = 0f; field.style.marginBottom = 0f;
            field.RegisterValueChangedCallback(e => _d.RingName = e.newValue);
            // внутрішнє поле вводу має своє біле тло з теми Unity — інакше світлий текст на білому не читався (кадр 08.11.2026)
            var input = field.Q(null, "unity-base-text-field__input");
            if (input != null)
            {
                input.style.backgroundColor = Color.clear;
                input.style.borderTopWidth = 0f; input.style.borderBottomWidth = 0f; input.style.borderLeftWidth = 0f; input.style.borderRightWidth = 0f;
                input.style.color = RingTheme.Text;
                // Fixel, не display: гравець набирає малими, а малі кириличні display — болгарських форм («в», «к»; Статут
                // TYPE-03, кадр 1080 08.11.2026). У стрічках ім'я й далі ВЕЛИКИМИ display.
                RingFonts.Apply(input, RingFontRole.TextBold);
                input.style.fontSize = 30f;
                input.style.paddingLeft = 6f;
            }
            well.Add(field);
            // ціль фокусу без миші: A / Enter ставить курсор у поле (друкувати — з клавіатури); ранг 5 — першою стає плитка ґіміка.
            // Курсор — наступного кадру: інакше той самий Enter доходив би вже до поля (аудит P3, 11.11.2026)
            PadMark.Mark(well, () => _focusNameAfter = Time.frameCount, "name", 5);
            _nameField = field;
            var under = ArenaKit.Abs(0f, null, 0f, 0f, null, 3f);
            under.style.backgroundColor = RingTheme.Gold;
            well.Add(under);
            box.Add(well);
            return box;
        }

        private VisualElement LookPlate(Color roleColor)
        {
            var box = ArenaKit.Abs(ArenaKit.Edge, ArenaKit.SecondY, null, null, ArenaKit.StripW, 252f);
            box.style.overflow = Overflow.Visible;
            box.Add(new ArenaPlate { Fill = RingTheme.WithAlpha(RingTheme.Panel, BrandTokens.AlphaPanel), Stripe = roleColor });
            var title = ArenaKit.Big(T.Get("c.create.look"), 30f, RingTheme.Text);
            ArenaKit.Place(title, 24f, 6f, null, null);
            box.Add(title);
            var cl = ArenaKit.Tag(T.Get("c.create.costume"), RingTheme.TextDim);
            ArenaKit.Place(cl, 24f, 48f, null, null);
            box.Add(cl);

            var sw = ArenaKit.Row();
            ArenaKit.Place(sw, 24f, 72f, null, null);
            for (int i = 0; i < _v.CostumeCount; i++)
            {
                int ci = i;
                var s = new VisualElement();
                s.style.width = 40f; s.style.height = 40f;
                s.style.marginRight = 5f;
                s.pickingMode = PickingMode.Position;
                s.style.backgroundColor = RingTheme.Rgb((uint)CareerLooks.CostumeRgb(i));   // кольори костюмів — дані гри (COL-07)
                bool on = i == _d.CostumeColor;
                float bw = on ? 3f : 1f;
                s.style.borderTopWidth = bw; s.style.borderBottomWidth = bw; s.style.borderLeftWidth = bw; s.style.borderRightWidth = bw;
                Color bc = on ? RingTheme.Text : RingTheme.Border;
                s.style.borderTopColor = bc; s.style.borderBottomColor = bc; s.style.borderLeftColor = bc; s.style.borderRightColor = bc;
                s.RegisterCallback<PointerDownEvent>(e => { if (e.button == 0) { _d.CostumeColor = ci; Refresh(); } });
                sw.Add(s);
            }
            int colors = Math.Max(1, _v.CostumeCount);
            Action<int> stepColor = d => { _d.CostumeColor = (_d.CostumeColor + d + colors) % colors; Refresh(); };
            PadMark.Mark(sw, () => stepColor(1), "costume", 4, null, stepColor);
            box.Add(sw);

            var body = Carousel(T.Get("c.create.body"), BodyName(Host.HeroBody),
                d => Host.HeroBody = (Host.HeroBody + d + CareerLooks.HeroBodies.Length) % CareerLooks.HeroBodies.Length);
            ArenaKit.Place(body, 24f, 124f, 40f, null, null, 54f);
            box.Add(body);
            var hair = Carousel(T.Get("c.create.hair"), HairName(CareerLooks.HeroHair[Host.HeroHair]),
                d => Host.HeroHair = (Host.HeroHair + d + CareerLooks.HeroHair.Length) % CareerLooks.HeroHair.Length);
            ArenaKit.Place(hair, 24f, 186f, 40f, null, null, 54f);
            box.Add(hair);
            return box;
        }

        /// <summary>Карусель «Тіло»/«Зачіска»: стрілки-кнопки з боків, підпис і значення посередині.</summary>
        private VisualElement Carousel(string label, string value, Action<int> step)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Stretch;
            row.style.backgroundColor = RingTheme.PanelRaised;
            row.pickingMode = PickingMode.Position;
            row.Add(new ArrowButton(true, () => { step(-1); Refresh(); }));
            var mid = new VisualElement();
            mid.style.flexGrow = 1f; mid.style.flexShrink = 1f;
            mid.style.justifyContent = Justify.Center;
            mid.style.paddingLeft = 16f;
            mid.pickingMode = PickingMode.Ignore;
            mid.Add(ArenaKit.Tag(label, RingTheme.TextDim));
            var val = MenuKit.Body(value, BrandTokens.TypeMinInfo, RingTheme.Text, true);
            val.style.whiteSpace = WhiteSpace.NoWrap;
            mid.Add(val);
            row.Add(mid);
            row.Add(new ArrowButton(false, () => { step(1); Refresh(); }));
            // ціль фокусу без миші — уся карусель: ← → гортають, A — наступне (як стрілки з боків)
            PadMark.Mark(row, () => { step(1); Refresh(); }, "carousel:" + label, 4, null, d => { step(d); Refresh(); });
            return row;
        }

        /// <summary>Кнопка-стрілка каруселі: трикутник малюється кодом (у Fixel немає ◀ ▶).</summary>
        private sealed class ArrowButton : VisualElement
        {
            private readonly bool _left;

            public ArrowButton(bool left, Action onClick)
            {
                _left = left;
                style.width = 46f;
                style.flexShrink = 0f;
                style.backgroundColor = RingTheme.Panel;
                pickingMode = PickingMode.Position;
                generateVisualContent = Draw;
                RegisterCallback<GeometryChangedEvent>(e => MarkDirtyRepaint());
                RegisterCallback<PointerDownEvent>(e => { if (e.button == 0 && onClick != null) onClick(); });
            }

            private void Draw(MeshGenerationContext ctx)
            {
                var r = contentRect;
                float cx = r.width * 0.5f, cy = r.height * 0.5f;
                if (r.width < 4f) return;
                Vector2[] tri = _left
                    ? new[] { new Vector2(cx + 7f, cy - 11f), new Vector2(cx + 7f, cy + 11f), new Vector2(cx - 9f, cy) }
                    : new[] { new Vector2(cx - 7f, cy - 11f), new Vector2(cx - 7f, cy + 11f), new Vector2(cx + 9f, cy) };
                MenuShapes.Polygon(ctx.painter2D, tri, RingTheme.Text);
            }
        }

        // ---------------- праворуч: афіша «Твій борець» і вкладка «Прийоми» ----------------

        private VisualElement RightColumn(RingPath path, Color roleColor)
        {
            var col = ArenaKit.Abs(null, ArenaKit.Top, ArenaKit.Edge, null, ArenaKit.StripW, null);
            col.style.overflow = Overflow.Visible;
            col.Add(Poster(path, roleColor));
            _rightCol = col;
            // вкладка «Прийоми» з карткою — окремий блок: на вузькій панелі він переїжджає й росте вгору над канатами (Fit)
            _movesBox = new VisualElement();
            _movesBox.style.overflow = Overflow.Visible;
            _movesBox.pickingMode = PickingMode.Ignore;
            col.Add(_movesBox);
            _movesPlate = null;
            var tab = new TabPlate(T.Get("c.hub.create.moves"), MoveCount(), _moves, () => { _moves = !_moves; Refresh(); }, false);
            tab.style.alignSelf = Align.FlexStart;
            tab.style.marginTop = 24f;
            _movesBox.Add(tab);
            if (_moves)
            {
                var card = new VisualElement();
                card.style.marginTop = 8f;
                card.style.overflow = Overflow.Visible;
                _movesPlate = new ArenaPlate { Fill = RingTheme.WithAlpha(RingTheme.Panel, BrandTokens.AlphaPlate), Stripe = RingTheme.Gold, StripeW = 5f };
                card.Add(_movesPlate);
                var wrap = new VisualElement();
                wrap.style.paddingLeft = 20f; wrap.style.paddingRight = 26f; wrap.style.paddingTop = 8f; wrap.style.paddingBottom = 10f;
                wrap.pickingMode = PickingMode.Ignore;
                // одним рядком з переносом, як було: 10 прийомів стовпчиком заходили на канати й ряд плиток (рев'ю 08.11.2026)
                var names = new List<string>();
                foreach (var m in Current.StartMoves) names.Add(T.Get(m.NameKey));
                var l = MenuKit.Body(string.Join(" · ", names), BrandTokens.TypeMinInfo, RingTheme.Text);
                l.style.whiteSpace = WhiteSpace.Normal;
                wrap.Add(l);
                card.Add(wrap);
                _movesBox.Add(card);
            }
            return col;
        }

        private VisualElement Poster(RingPath path, Color roleColor)
        {
            var box = new VisualElement();
            box.style.overflow = Overflow.Visible;
            box.pickingMode = PickingMode.Ignore;
            box.Add(new PaperSheet { Torn = true });
            var col = new VisualElement();
            col.pickingMode = PickingMode.Ignore;
            col.style.paddingLeft = 26f; col.style.paddingRight = 22f; col.style.paddingTop = 16f; col.style.paddingBottom = 24f;
            col.Add(MenuKit.Display(T.Get("c.hub.create.poster"), 24f, RingTheme.Bg, RingFontRole.Display800, 0.04f));

            var head = ArenaKit.Row();
            head.style.marginTop = 8f;
            var gname = ArenaKit.Big(T.Get("gimmick." + C.Content.GimmickOf(_d.Gimmick).Key), 52f, RingTheme.Bg);
            gname.style.flexShrink = 1f;
            head.Add(gname);
            head.Add(ArenaKit.Spacer());
            head.Add(RolePlate(path, roleColor));
            col.Add(head);

            string roleKey = _d.Role == CareerRole.Hero ? "role.hero.desc" : "role.villain.desc";
            var desc = MenuKit.Body(T.Get(roleKey), BrandTokens.TypeBody, RingTheme.Bg);
            desc.style.marginTop = 10f;
            col.Add(desc);

            var line = new VisualElement();
            line.style.height = 2f; line.style.backgroundColor = RingTheme.Bg; line.style.marginTop = 10f; line.style.marginBottom = 8f;
            col.Add(line);

            var hr = ArenaKit.Row();
            hr.Add(MenuKit.Display(T.Get("c.create.freepoint"), BrandTokens.TypeLabel, RingTheme.Ash, RingFontRole.Display700, 0.1f));
            hr.Add(ArenaKit.Spacer());
            hr.Add(MenuKit.Display(T.Get("c.hub.create.pick"), BrandTokens.TypeLabel, RingTheme.Ash, RingFontRole.Display700, 0.1f));
            hr.style.marginBottom = 4f;
            col.Add(hr);

            var baseAttr = Current.BaseAttr;
            for (int i = 0; i < 5; i++) col.Add(AttrRow(i, baseAttr[i]));
            box.Add(col);
            return box;
        }

        private VisualElement RolePlate(RingPath path, Color roleColor)
        {
            var plate = new VisualElement();
            plate.style.height = 46f;
            plate.style.flexShrink = 0f;
            plate.style.overflow = Overflow.Visible;
            plate.style.justifyContent = Justify.Center;
            plate.pickingMode = PickingMode.Ignore;
            plate.Add(new ArenaPlate { Fill = roleColor, Shadow = false, Mirror = true });
            var l = MenuKit.Display(RingLabels.PathName(T, path), 26f, RingTheme.Text, RingFontRole.Display900, 0.04f);
            l.style.marginLeft = 24f; l.style.marginRight = 22f;
            plate.Add(l);
            return plate;
        }

        /// <summary>Рядок характеристики: ▶ вибраного, назва, біполярна шкала (мінус ліворуч — сигнал, вільний бал — золотий блок), значення.</summary>
        private VisualElement AttrRow(int i, int baseValue)
        {
            var a = (RingAttr)i;
            bool sel = a == _d.FreePoint;
            bool ok = baseValue + 1 <= _v.MaxAttr;
            int shown = baseValue + (sel ? 1 : 0);

            var row = ArenaKit.Row();
            row.style.height = 38f;
            row.style.marginLeft = -6f; row.style.paddingLeft = 6f;
            row.style.marginRight = -6f; row.style.paddingRight = 6f;
            row.pickingMode = PickingMode.Position;
            if (sel) row.style.backgroundColor = RingTheme.WithAlpha(RingTheme.Bg, 0.12f);
            row.RegisterCallback<PointerDownEvent>(e => { if (e.button == 0 && ok) { _d.FreePoint = a; Refresh(); } });
            VisualElement frame = null;
            PadMark.Mark(row, ok ? (Action)(() => { _d.FreePoint = a; Refresh(); }) : null, "attr:" + i, 4, on =>
            {
                if (on && frame == null) { frame = PadFocus.Frame(RingTheme.Bg); row.Add(frame); }
                else if (!on && frame != null) { frame.RemoveFromHierarchy(); frame = null; }
            });

            var mark = new Marker(sel);
            row.Add(mark);
            Color nameColor = !ok ? RingTheme.Ash : sel ? RingTheme.Bg : RingTheme.Ash;
            var name = MenuKit.Display(RingLabels.AttrName(T, a), 18f, nameColor, sel ? RingFontRole.Display900 : RingFontRole.Display700, 0.08f);
            name.style.width = 168f;
            name.style.marginLeft = 8f;
            row.Add(name);
            row.Add(ArenaKit.Spacer());
            row.Add(BipolarBar(baseValue, sel && ok));
            var val = ArenaKit.Big(Signed(shown), 26f, !ok ? RingTheme.Ash : RingTheme.Bg);
            val.style.width = 50f;
            val.style.unityTextAlign = TextAnchor.MiddleRight;
            val.style.marginLeft = 8f;
            row.Add(val);
            return row;
        }

        private VisualElement BipolarBar(int baseValue, bool withPoint)
        {
            int max = Math.Max(1, _v.MaxAttr);
            var bar = ArenaKit.Row();
            // мінусовий блок
            bool negFree = withPoint && baseValue < 0;
            bar.Add(Cell(baseValue < 0 && !negFree ? RingTheme.Signal : RingTheme.Border, negFree));
            var div = new VisualElement();
            div.style.width = 3f; div.style.height = 24f; div.style.backgroundColor = RingTheme.Bg; div.style.marginLeft = 3f; div.style.marginRight = 3f;
            div.pickingMode = PickingMode.Ignore;
            bar.Add(div);
            for (int k = 1; k <= max; k++)
            {
                bool freeCell = withPoint && baseValue >= 0 && k == baseValue + 1;
                bool filled = k <= baseValue;
                bar.Add(Cell(filled ? RingTheme.Bg : RingTheme.Border, freeCell));
            }
            return bar;
        }

        /// <summary>Блок шкали; вільний бал — золотий з обводкою кольору тла.</summary>
        private static VisualElement Cell(Color c, bool free)
        {
            var e = new VisualElement();
            e.style.width = 26f; e.style.height = 15f; e.style.marginRight = 3f;
            e.pickingMode = PickingMode.Ignore;
            if (free)
            {
                e.style.backgroundColor = RingTheme.Gold;
                e.style.borderTopWidth = 2f; e.style.borderBottomWidth = 2f; e.style.borderLeftWidth = 2f; e.style.borderRightWidth = 2f;
                e.style.borderTopColor = RingTheme.Bg; e.style.borderBottomColor = RingTheme.Bg; e.style.borderLeftColor = RingTheme.Bg; e.style.borderRightColor = RingTheme.Bg;
                e.style.height = 19f;
            }
            else e.style.backgroundColor = c;
            return e;
        }

        /// <summary>▶ біля вибраного рядка (малюється кодом).</summary>
        private sealed class Marker : VisualElement
        {
            private readonly bool _on;

            public Marker(bool on)
            {
                _on = on;
                style.width = 14f; style.height = 20f; style.flexShrink = 0f;
                pickingMode = PickingMode.Ignore;
                generateVisualContent = Draw;
            }

            private void Draw(MeshGenerationContext ctx)
            {
                if (!_on) return;
                MenuShapes.Polygon(ctx.painter2D, new[] { new Vector2(1f, 2f), new Vector2(1f, 18f), new Vector2(13f, 10f) }, RingTheme.Bg);
            }
        }

        // ---------------- шапка ряду ----------------

        private VisualElement RowHeader()
        {
            var row = ArenaKit.Abs(ArenaKit.Edge, null, ArenaKit.Edge + ArenaKit.GoldW + 14f, ArenaKit.HeaderBottom, null, 42f);
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.pickingMode = PickingMode.Ignore;
            _head = row;

            var g1 = new VisualElement();
            g1.style.flexGrow = 4f; g1.style.flexShrink = 1f; g1.style.flexBasis = 0f;
            g1.style.flexDirection = FlexDirection.Row;
            g1.style.alignItems = Align.Center;
            g1.pickingMode = PickingMode.Ignore;
            var t1 = MenuKit.Display(T.Get("c.create.gimmick"), 27f, RingTheme.Text, RingFontRole.Display900, 0.02f);
            t1.style.unityFontStyleAndWeight = FontStyle.Italic;
            g1.Add(t1);
            _hintDefault = T.Get(Current.Def.NameKey + ".desc");
            _hint = MenuKit.Body(_hintDefault, BrandTokens.TypeMinInfo, RingTheme.Text);
            _hint.style.marginLeft = 22f;
            _hint.style.flexShrink = 1f;
            MenuKit.Underlay(_hint);
            g1.Add(_hint);
            row.Add(g1);

            var g2 = new VisualElement();
            g2.style.flexGrow = 2f; g2.style.flexShrink = 1f; g2.style.flexBasis = 0f;
            g2.style.flexDirection = FlexDirection.Row;
            g2.style.alignItems = Align.Center;
            g2.style.paddingLeft = 4f;
            g2.pickingMode = PickingMode.Ignore;
            var t2 = MenuKit.Display(T.Get("c.create.role"), 27f, RingTheme.Text, RingFontRole.Display900, 0.02f);
            t2.style.unityFontStyleAndWeight = FontStyle.Italic;
            g2.Add(t2);
            row.Add(g2);
            _roleHead = g2;
            return row;
        }

        // ---------------- допоміжне ----------------

        /// <summary>
        /// Тіло — номером і будовою, без національності: «3 з 16 · чоловіче» (плейтест 08.11.2026, P3, 8c3f16b: «чоловік,
        /// українець(-ка)» — і національність, і «(-ка)» для чоловічого тіла).
        /// </summary>
        private string BodyName(int index)
        {
            string body = CareerLooks.HeroBodies[index];
            string sex = body.StartsWith("body_f_") ? "f" : "m";
            return T.Format("c.look.body", index + 1, CareerLooks.HeroBodies.Length, T.Get("c.look.build." + sex));
        }

        private string HairName(string hair)
        {
            return string.IsNullOrEmpty(hair) ? T.Get("c.look.hair.none") : T.Get("c.look.hair." + hair);
        }

        /// <summary>Показати героя на рингу таким, яким його збирають (тіло, зачіска, колір костюма).</summary>
        private void ApplyLook()
        {
            var stage = RingStageHub.Current;
            if (stage == null) return;
            var hammer = CareerLooks.ForNpc(CareerContent.HammerId);
            stage.SetWrestlers(CareerLooks.HeroBodies[Host.HeroBody], CareerLooks.CostumeRgb(_d.CostumeColor), CareerLooks.HeroHair[Host.HeroHair],
                hammer.Body, hammer.Rgb, hammer.Hair, hammer.Scale);
        }

        public override void OnDigit(int n)
        {
            if (n >= 1 && n <= 4) { _d.Gimmick = (Gimmick)(n - 1); Refresh(); }
            else if (n == 5) { _d.Role = CareerRole.Hero; Refresh(); }
            else if (n == 6) { _d.Role = CareerRole.Villain; Refresh(); }
        }

        public override void OnConfirm()
        {
            string reason;
            if (!C.CanCreate(_d, out reason)) return;
            C.CreateHero(_d);
            Host.Save();
        }
    }
}
