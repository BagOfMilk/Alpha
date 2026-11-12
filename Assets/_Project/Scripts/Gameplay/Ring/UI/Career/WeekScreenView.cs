using System;
using System.Collections.Generic;
using Game.Core.Ring;
using Game.Core.Ring.Career;
using Game.Core.Ring.Match;
using Game.Gameplay.Ring.Models;
using Game.Gameplay.Ring.UI.Menus;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Gameplay.Ring.UI
{
    /// <summary>
    /// Хаб тижня (ТЗ B03 v2 §1–§3, мова «Арена»): каркас матчу — ліворуч стрічка гравця (ім'я, гімік · роль, зони тіла й лялька) і
    /// ресурси, у центрі табличка тижня, праворуч стрічка суперника вечора й паперова афіша (гонорар, місце в картці, план букера,
    /// переваги, наліпка загрози); унизу канати, шапка ряду (дії тижня, підказка дії у фокусі, вкладки «Розвиток»/«Витрати»),
    /// шість плиток дій (клавіші 1–6) і золота плита «Перейти до вечора». Логіка дій — як і раніше (CareerSession).
    /// «ПРОТИ» між борцями поки не ставимо: у 3D тижня стоїть букер, а не суперник (сцену змінює сесія «Ринг», B03 §4).
    /// </summary>
    public sealed class WeekScreenView : CareerScreenView
    {
        // Шухляда: є очки — відкрито «Розвиток» (плейтест 08.11.2026, P1-5 «Изучение», 8c3f16b: бот проходу накопичив 39
        // невитрачених очок — «Розвиток» був згорнутий); «Витрати» — в огляді, коли очок немає. Лише на першій побудові тижня:
        // далі шухлядою керує гравець (вкладки, Esc).
        private bool _dev, _accordionSet;
        private bool _shop = ExpandForShots;
        private readonly List<ActionTile> _tiles = new List<ActionTile>();
        private Label _hint;
        private VisualElement _row, _gold, _weekPlate, _last, _ropes, _header, _drawer, _poster;
        private string _hintDefault = "";
        private bool _hintWarn;
        private ScrollView _drawerScroll;
        private Vector2 _drawerOffset;

        public WeekScreenView(RingUiContext ctx) : base(ctx)
        {
            Root.RegisterCallback<GeometryChangedEvent>(e => Fit());
        }

        public override bool OnBack()
        {
            if (!_dev && !_shop) return false;
            _dev = false; _shop = false;
            _drawerScroll = null; _drawerOffset = Vector2.zero;
            Refresh();
            return true;
        }

        public override void Refresh()
        {
            // покупка в шухляді перебудовує екран: місце в довгому списку «Усе (N)» не губиться (рев'ю 08.11.2026)
            if (_drawerScroll != null) _drawerOffset = _drawerScroll.scrollOffset;
            _drawerScroll = null;
            Root.Clear();
            Root.style.paddingLeft = 0f; Root.style.paddingRight = 0f; Root.style.paddingTop = 0f; Root.style.paddingBottom = 0f;
            _tiles.Clear();
            var v = C.GetCareerView();
            var w = C.GetWeekView();
            if (!_accordionSet)
            {
                _accordionSet = true;
                _dev = w.DevPoints > 0;
                _shop = ExpandForShots && !_dev;
            }
            Root.Add(ShowKit.PlayerStrip(C, T, v));
            Root.Add(ShowKit.Resources(T, v));
            _weekPlate = WeekPlate(v);
            Root.Add(_weekPlate);
            Root.Add(ShowKit.RivalStrip(C, T, w.Card.Opponent, w.Card.OpponentFeudLevel, true));
            _poster = Poster(w.Card, w.IsBigShowWeek);
            _poster.RegisterCallback<GeometryChangedEvent>(e => Fit());   // висота афіші відома лише після розкладки (межа шухляди)
            Root.Add(_poster);
            var last = Host.LastWeekResult;
            _last = last != null ? LastResult(last) : null;
            if (_last != null) Root.Add(_last);

            _ropes = new RopeLine();
            ArenaKit.Place(_ropes, ArenaKit.Edge, null, ArenaKit.Edge, ArenaKit.RopesBottom, null, 22f);
            Root.Add(_ropes);
            _header = RowHeader(w);
            Root.Add(_header);
            _drawer = _dev || _shop ? Drawer() : null;
            if (_drawer != null) Root.Add(_drawer);

            _row = ArenaKit.Abs(ArenaKit.Edge, null, ArenaKit.Edge + ArenaKit.GoldW + 14f, ArenaKit.RowBottom, null, ArenaKit.TileH);
            _row.style.flexDirection = FlexDirection.Row;
            _row.pickingMode = PickingMode.Ignore;
            if (w.PromoChoicePending)
            {
                AddTile(1, T.Get("week.promo.reward.0"), "", null, true, null, () => Do(C.ChoosePromoReward(WeekPromoReward.Feud)), T.Get("week.promo.reward.0"));
                AddTile(2, T.Get("week.promo.reward.1"), "", null, true, null, () => Do(C.ChoosePromoReward(WeekPromoReward.Hype)), T.Get("week.promo.reward.1"));
            }
            else
            {
                bool any = w.ActionsLeft > 0;
                string spent = T.Get("c.hub.no_actions");
                int cha = C.Player.Get(RingAttr.Charisma) - C.State.InjuryPenaltyFor(BodyZone.Head);
                int job = CareerRules.SideJobBase + 5 * Math.Max(0, C.Player.Get(RingAttr.Stamina));
                AddTile(1, T.Get("week.action.0"), T.Format("c.week.pay.train", CareerRules.TrainingPoints), null, any, spent,
                    () => Do(C.Train(false)), T.Get("week.action.0.desc"));
                bool coach = w.Money >= CareerRules.TrainerCost;
                AddTile(2, T.Get("c.week.train_coach"), T.Format("c.week.pay.coach", CareerRules.TrainingPoints + CareerRules.TrainerExtraPoints, CareerRules.TrainerCost),
                    null, any && coach, any ? T.Format("c.hub.need_money", CareerRules.TrainerCost - w.Money) : spent, () => Do(C.Train(true)), T.Get("c.week.train_coach.desc"));
                // травма на плитці — коротко: зона, мінус до кидків, тижні (≤ 2 рядки, ПМ 11.11.2026); про лікаря каже опис плитки
                AddTile(3, T.Get("week.action.1"), w.CanRecover ? T.Format("c.week.pay.recover", CareerRules.RecoverZoneHeal) : T.Get("c.week.pay.healthy"),
                    InjuryWarn(w), any && w.CanRecover, any ? null : spent, () => Do(C.Recover(WorstZone(w), false)), T.Get("week.action.1.desc"));   // «ти цілий» уже в ефекті
                // ефект — без чисел: шанси йдуть смугою з легендою під нею (опис ≤ 2 рядки, смуга в потоці — ПМ 11.11.2026)
                var promo = AddTile(4, T.Get("week.action.2"), T.Get("c.week.pay.promo"), null, any, spent, () => Do(C.Promo()), T.Get("week.action.2.desc"));
                var back = AddTile(5, T.Get("week.action.3"), T.Get("c.week.pay.backstage"), null, any, spent, () => Do(C.Backstage()), T.Get("week.action.3.desc"));
                var odds = TwoD6.ChancesOf36(cha);
                if (any)   // вимкнена плитка — без шансів
                {
                    string m = T.Format("c.odds.miss", Pct(odds[0])), pa = T.Format("c.odds.partial", Pct(odds[1])), st = T.Format("c.odds.strong", Pct(odds[2]));
                    promo.SetChances(odds[0], odds[1], odds[2], m, pa, st);
                    back.SetChances(odds[0], odds[1], odds[2], m, pa, st);
                }
                AddTile(6, T.Get("week.action.4"), T.Format("c.hub.pay.job", job), T.Format("c.hub.risk", Pct(Sum36AtMost(CareerRules.SideJobInjuryAtMost))),
                    any, spent, () => Do(C.SideJob()), T.Get("week.action.4.desc"));
            }
            Root.Add(_row);

            _gold = new GoldPlate(T.Get("week.end"), ArenaKit.ConfirmHint(T), OnConfirm, !w.PromoChoicePending);
            ArenaKit.Place(_gold, null, null, ArenaKit.Edge, ArenaKit.RowBottom, ArenaKit.GoldW, ArenaKit.TileH);
            Root.Add(_gold);
            Fit();
        }

        /// <summary>
        /// Вузька панель (4:3, великий масштаб інтерфейсу; кадри 08.11.2026): шість плиток по ~120 одиниць не вміщали назв —
        /// плитки стають двома рядами по три, канати й шапка ряду піднімаються на ряд, табличка тижня переходить під ресурси
        /// (між стрічками для неї немає місця), відгук дії — нижче.
        /// </summary>
        private void Fit()
        {
            if (_row == null || _gold == null) return;
            float width = Root.layout.width;
            if (float.IsNaN(width) || width <= 0f) return;
            bool narrow = width < ArenaKit.NarrowWidth;
            float gw = narrow ? ArenaKit.GoldWNarrow : ArenaKit.GoldW;
            _gold.style.width = gw;
            _row.style.right = ArenaKit.Edge + gw + 14f;
            bool twoRows = CareerTileLayout.WeekTwoRows(width, _tiles.Count);
            // Один ряд вузьких плиток (16:9: ~183 одиниці) — компактні плитки (назва 17, ключ на краї); тексту більше, ніж уміщає
            // TileH, — ряд росте вгору, а з ним шапка, канати й шухляда (кадри слоту 27, 09.11.2026: «ВІДНОВЛЕНН/Я», шанси під
            // смугою, рядок травми під плиткою). Щойно змінений режим — висота з наступного кадру, коли текст перерозкладено.
            float tileW = _tiles.Count > 0 ? CareerTileLayout.WeekTileWidth(width, _tiles.Count) : 0f;
            bool compact = !twoRows && CareerTileLayout.Compact(tileW);
            bool relaid = false;
            float need = ArenaKit.TileH;
            foreach (var t in _tiles) relaid |= t.SetCompact(compact);
            if (!relaid) foreach (var t in _tiles) need = Mathf.Max(need, t.NeededHeight());
            float tileH = relaid && !float.IsNaN(_gold.layout.height) && _gold.layout.height > 0f ? _gold.layout.height
                : Mathf.Min(ArenaKit.TileH + ArenaKit.TileGrowMax, (float)Math.Ceiling(need));
            float lift = tileH - ArenaKit.TileH + (twoRows ? tileH + 12f : 0f);
            _row.style.height = ArenaKit.TileH + lift;
            _row.style.flexWrap = twoRows ? Wrap.Wrap : Wrap.NoWrap;
            _gold.style.height = tileH;
            foreach (var t in _tiles)
            {
                t.style.height = tileH;
                t.style.flexBasis = twoRows ? Length.Percent(CareerTileLayout.TwoRowBasis * 100f) : new Length(0f);   // + ріст 1 = (ряд − 27) / 3, як у моделі
                t.style.marginBottom = twoRows ? 12f : 0f;
            }
            if (_ropes != null) _ropes.style.bottom = ArenaKit.RopesBottom + lift;
            if (_header != null) _header.style.bottom = ArenaKit.HeaderBottom + lift;
            if (_drawer != null)
            {
                _drawer.style.bottom = ArenaKit.HeaderBottom + 50f + lift;
                // шухляда не закриває афішу (гонорар, план, суперник), доки під нею лишається >= DrawerMinRoom — далі список
                // прокручується; менше — лише до стрічки суперника (рев'ю 08.11.2026; поріг 260 при 4:3 клав шухляду на план букера
                // і суперника — кадри слоту 27, 09.11.2026)
                float height = Root.layout.height;
                float bottomY = height - (ArenaKit.HeaderBottom + 50f + lift);
                bool posterReady = _poster != null && !float.IsNaN(_poster.layout.height) && _poster.layout.height > 0f;
                if (posterReady && !float.IsNaN(height))
                {
                    float room = bottomY - (_poster.layout.y + _poster.layout.height) - 12f;
                    if (room < ArenaKit.DrawerMinRoom) room = bottomY - (ArenaKit.Top + ArenaKit.StripH + 12f);
                    if (room > 120f) _drawer.style.maxHeight = room;
                }
            }
            if (_weekPlate != null)
            {
                if (narrow) { _weekPlate.style.left = ArenaKit.Edge; _weekPlate.style.marginLeft = 0f; _weekPlate.style.top = ArenaKit.SecondY + 104f + 14f; }
                else { _weekPlate.style.left = Length.Percent(50f); _weekPlate.style.marginLeft = -175f; _weekPlate.style.top = ArenaKit.Top; }
            }
            if (_last != null) _last.style.top = narrow ? ArenaKit.SecondY + 104f + 14f + 70f + 14f : 288f;
        }

        private ActionTile AddTile(int key, string title, string effect, string risk, bool enabled, string reason, Action act, string desc)
        {
            var t = new ActionTile(key, title, effect, risk, enabled, reason);
            t.OnClick = act;
            t.OnContentChanged = Fit;   // перенос рядків відомий лише після розкладки — висота ряду за ним
            t.OnHover = () =>
            {
                foreach (var o in _tiles) o.Focused = o == t;
                SetHint(desc);
            };
            t.OnUnhover = () => { t.Focused = false; SetHint(_hintDefault); };
            _tiles.Add(t);
            _row.Add(t);
            return t;
        }

        /// <summary>Рядок-підказка шапки ряду: підкладка лише під текстом (порожня підкладка читалась як зайва смуга — кадр 08.11.2026).</summary>
        private void SetHint(string text)
        {
            if (_hint == null) return;
            _hint.text = text ?? "";
            _hint.style.color = _hintWarn && text == _hintDefault ? RingTheme.Signal : RingTheme.Text;
            _hint.style.opacity = string.IsNullOrEmpty(text) ? 0f : 1f;
        }

        // ---------------- верх: стрічки, ресурси, табличка, афіша ----------------

        private VisualElement WeekPlate(CareerView v)
        {
            int total = CareerRules.WeeksInSeason(v.Season);
            var box = new VisualElement();
            box.style.position = Position.Absolute;
            box.style.left = Length.Percent(50f); box.style.marginLeft = -175f;
            box.style.top = ArenaKit.Top; box.style.width = 350f; box.style.height = 70f;
            box.style.overflow = Overflow.Visible;
            box.pickingMode = PickingMode.Ignore;
            box.Add(new PaperSheet { Torn = false, Chamfer = false });
            var row = ArenaKit.Row();
            ArenaKit.Place(row, 26f, 4f, 20f, 4f);
            row.Add(MenuKit.Display(T.Get("c.hub.week"), 22f, RingTheme.Bg, RingFontRole.Display800, 0.04f));
            var num = ArenaKit.Big(v.Week.ToString(), 58f, RingTheme.Bg, true);
            num.style.marginLeft = 14f;
            row.Add(num);
            var of = MenuKit.Display("/" + total, 21f, RingTheme.Ash, RingFontRole.Display800, 0f);
            of.style.marginLeft = 4f; of.style.marginTop = 18f;
            row.Add(of);
            row.Add(ArenaKit.Spacer());
            var right = new VisualElement();
            right.pickingMode = PickingMode.Ignore;
            right.Add(MenuKit.Display(T.Format("c.hub.season", v.Season), BrandTokens.TypeLabel, RingTheme.Bg, RingFontRole.Display800, 0.1f));
            right.Add(ArenaKit.Segments(total, v.Week, RingTheme.Bg, total * 20f, 14f, RingTheme.TextDim));   // на папері темне «порожнє» не видно
            row.Add(right);
            box.Add(row);
            return box;
        }

        private VisualElement Poster(ShowCardView c, bool big)
        {
            var box = ArenaKit.Abs(null, ArenaKit.SecondY, ArenaKit.Edge, null, ArenaKit.StripW, null);
            box.style.overflow = Overflow.Visible;
            box.Add(new PaperSheet { Torn = true });
            var col = new VisualElement();
            col.pickingMode = PickingMode.Ignore;
            col.style.paddingLeft = 24f; col.style.paddingRight = 22f; col.style.paddingTop = 14f; col.style.paddingBottom = 20f;
            var head = ArenaKit.Row();
            head.style.alignItems = Align.FlexStart;
            var left = new VisualElement();
            left.pickingMode = PickingMode.Ignore;
            left.style.flexShrink = 1f;
            left.Add(MenuKit.Display(T.Get(c.IsTitle ? "c.card.title_match" : big ? "c.card.big" : "c.card.weekly"), 25f, RingTheme.Bg, RingFontRole.Display800, 0.02f));
            var pos = MenuKit.Display(T.Get("card.pos." + (int)c.Position), BrandTokens.TypeLabel, RingTheme.Ash, RingFontRole.Display700, 0.1f);
            pos.style.marginTop = 4f;
            left.Add(pos);
            head.Add(left);
            head.Add(ArenaKit.Spacer());
            var fee = new VisualElement();
            fee.pickingMode = PickingMode.Ignore;
            fee.style.alignItems = Align.FlexEnd;
            fee.Add(MenuKit.Display(T.Get("c.hub.fee"), BrandTokens.TypeLabel, RingTheme.Ash, RingFontRole.Display700, 0.1f));
            fee.Add(ArenaKit.Money(c.Fee, 40f, RingTheme.Bg));
            head.Add(fee);
            col.Add(head);
            var line = new VisualElement();
            line.style.height = 2f; line.style.backgroundColor = RingTheme.Bg; line.style.marginTop = 6f; line.style.marginBottom = 8f;
            col.Add(line);
            string plan = (c.IsFeud ? "" : T.Get("c.card.plan") + ": " + PlanText(c.Plan) + " · ") + StipText(c.Stipulations);
            col.Add(MenuKit.Body(plan, BrandTokens.TypeMinInfo, RingTheme.Bg, true));
            if (c.PromoHypeBonus != 0) col.Add(Bullet(T.Format("c.card.promo_bonus", Signed(c.PromoHypeBonus))));
            if (c.IsRematch) col.Add(Bullet(T.Get("c.hub.rematch")));
            // P2-4 (8c3f16b): до росту гравця — нейтральний рядок «за місцем у картці», без червоної наліпки
            if (c.Opponent != null && c.RivalSteps > 0 && !ShowKit.RivalInForm(C)) col.Add(Bullet(ShowKit.RivalStepsText(T, c)));
            box.Add(col);
            if (c.Opponent != null && c.RivalSteps > 0 && ShowKit.RivalInForm(C))
            {
                var st = new Sticker(T.Format("c.hub.form", c.RivalSteps));
                st.style.position = Position.Absolute;
                st.style.left = 26f; st.style.bottom = -26f;
                box.Add(st);
            }
            return box;
        }

        private static VisualElement Bullet(string text)
        {
            var r = ArenaKit.Row();
            r.style.marginTop = 4f;
            r.style.alignItems = Align.FlexStart;
            var sq = new VisualElement();
            sq.style.width = 11f; sq.style.height = 11f; sq.style.marginTop = 4f; sq.style.marginRight = 8f; sq.style.flexShrink = 0f;
            sq.style.backgroundColor = RingTheme.Gold;
            sq.style.borderTopWidth = 1f; sq.style.borderBottomWidth = 1f; sq.style.borderLeftWidth = 1f; sq.style.borderRightWidth = 1f;
            sq.style.borderTopColor = RingTheme.Bg; sq.style.borderBottomColor = RingTheme.Bg; sq.style.borderLeftColor = RingTheme.Bg; sq.style.borderRightColor = RingTheme.Bg;
            r.Add(sq);
            var l = MenuKit.Body(text, BrandTokens.TypeMinInfo, RingTheme.Bg, true);
            l.style.flexShrink = 1f;
            r.Add(l);
            return r;
        }

        private VisualElement LastResult(WeekActionResult r)
        {
            var box = ArenaKit.Abs(ArenaKit.Edge, 288f, null, null, ArenaKit.StripW, null);
            box.style.overflow = Overflow.Visible;
            box.Add(new ArenaPlate { Fill = RingTheme.WithAlpha(RingTheme.Bg, BrandTokens.AlphaUnderlay), Stripe = r.Ok ? RingTheme.Gold : RingTheme.Signal, StripeW = 5f, Shadow = false });
            var l = MenuKit.Body(ResultText(r), BrandTokens.TypeMinInfo, r.Ok ? RingTheme.Text : RingTheme.Signal);
            l.style.paddingLeft = 20f; l.style.paddingRight = 26f; l.style.paddingTop = 7f; l.style.paddingBottom = 8f;
            box.Add(l);
            return box;
        }

        // ---------------- низ: шапка ряду, шухляда, плитки ----------------

        private VisualElement RowHeader(WeekView w)
        {
            var row = ArenaKit.Abs(ArenaKit.Edge, null, ArenaKit.Edge, ArenaKit.HeaderBottom, null, 42f);
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.pickingMode = PickingMode.Ignore;
            var title = MenuKit.Display(w.PromoChoicePending ? T.Get("c.hub.promo_choice") : T.Get("c.hub.actions"), 27f, RingTheme.Text, RingFontRole.Display900, 0.02f);
            title.style.unityFontStyleAndWeight = FontStyle.Italic;
            row.Add(title);
            if (!w.PromoChoicePending)
            {
                var dots = ArenaKit.Row();
                dots.style.marginLeft = 18f;
                for (int i = 0; i < w.ActionsTotal; i++)
                {
                    var d = new VisualElement();
                    d.style.width = 15f; d.style.height = 15f; d.style.marginRight = 6f;
                    d.style.borderTopLeftRadius = 8f; d.style.borderTopRightRadius = 8f; d.style.borderBottomLeftRadius = 8f; d.style.borderBottomRightRadius = 8f;
                    d.style.backgroundColor = i < w.ActionsLeft ? RingTheme.Gold : RingTheme.Border;
                    dots.Add(d);
                }
                row.Add(dots);
                var left = MenuKit.Display(T.Format("c.hub.left", w.ActionsLeft, w.ActionsTotal), BrandTokens.TypeLabel, RingTheme.TextDim, RingFontRole.Display700, 0.1f);
                left.style.marginLeft = 8f;
                row.Add(left);
            }
            // вибір нагороди за промо й перевтома — обидва тексти (раніше перевтома ховала інструкцію вибору); сигнальний колір —
            // лише для попередження, опис плитки при наведенні — звичайним (рев'ю 08.11.2026)
            string promo = w.PromoChoicePending ? T.Get("c.week.promo_choice") : null;
            string strained = w.Strained ? T.Get("week.strained") : null;
            _hintDefault = promo != null && strained != null ? promo + " " + strained : promo ?? strained ?? "";
            _hintWarn = strained != null;
            _hint = MenuKit.Body(_hintDefault, BrandTokens.TypeMinInfo, RingTheme.Text);
            _hint.style.marginLeft = 22f;
            _hint.style.flexShrink = 1f; _hint.style.flexGrow = 0f; _hint.style.minWidth = 0f;
            // не більше двох ПОВНИХ рядків (40 зрізало другий рядок навпіл — аудит 11.11.2026): на вузькій панелі довгий опис
            // не вилазить на канати й плитки; кожен опис плитки влазить у два рядки — стежить CareerTextFitTests
            _hint.style.maxHeight = CareerTileLayout.HintMaxHeight;
            _hint.style.overflow = Overflow.Hidden;
            MenuKit.Underlay(_hint);
            row.Add(_hint);
            SetHint(_hintDefault);
            row.Add(ArenaKit.Spacer());
            row.Add(new TabPlate(T.Get("dev.title"), w.DevPoints, _dev, () => { _dev = !_dev; if (_dev) _shop = false; _drawerScroll = null; _drawerOffset = Vector2.zero; Refresh(); }, w.DevPoints > 0));
            row.Add(new TabPlate(T.Get("c.hub.shop"), ShopAvailable(), _shop, () => { _shop = !_shop; if (_shop) _dev = false; _drawerScroll = null; _drawerOffset = Vector2.zero; Refresh(); }, false));
            return row;
        }

        /// <summary>Шухляда «Розвиток»/«Витрати» (B03 §3): виростає вгору над шапкою ряду, ряд дій не рухається; закривають вкладка й Esc (<see cref="OnBack"/>).</summary>
        private VisualElement Drawer()
        {
            var box = ArenaKit.Abs(null, null, ArenaKit.Edge, ArenaKit.HeaderBottom + 50f, 560f, null);
            box.style.maxHeight = 430f;   // уточнює Fit: не вище афіші, якщо під нею є місце
            box.style.overflow = Overflow.Visible;
            box.style.flexDirection = FlexDirection.Column;
            box.pickingMode = PickingMode.Position;
            box.Add(new ArenaPlate { Fill = RingTheme.WithAlpha(RingTheme.Panel, 0.96f), Stripe = RingTheme.Gold, StripeW = 6f });
            // заголовок і рядок-підказка — над прокруткою: опис пункту й причина «чому недоступно» видні завжди, а не внизу
            // довгого списку (рев'ю 08.11.2026)
            // Рядок-підказка — праворуч від заголовка, а не під ним: порожній під заголовком він читався як зайва смуга й з'їдав
            // висоту списку (кадри слоту 27, 09.11.2026)
            var top = ArenaKit.Row();
            top.pickingMode = PickingMode.Ignore;
            top.style.alignItems = Align.Center;
            top.style.paddingLeft = 18f; top.style.paddingRight = 24f; top.style.paddingTop = 8f;
            top.style.flexShrink = 0f;
            var head = MenuKit.Display(_dev ? T.Get("dev.title") : T.Get("c.hub.shop"), 25f, RingTheme.Text, RingFontRole.Display900, 0.02f);
            head.style.flexShrink = 0f;
            top.Add(head);
            var hint = MakeHint();   // кнопки розвитку й витрат пишуть сюди при наведенні
            hint.style.marginTop = 0f; hint.style.marginLeft = 16f;
            hint.style.flexGrow = 1f; hint.style.flexShrink = 1f;
            hint.style.unityTextAlign = TextAnchor.MiddleLeft;
            top.Add(hint);
            box.Add(top);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            ArenaKit.ThinScroller(scroll);
            scroll.style.flexShrink = 1f; scroll.style.minHeight = 60f;
            scroll.style.paddingLeft = 18f; scroll.style.paddingRight = 24f; scroll.style.paddingBottom = 10f;
            var content = _dev ? DevelopmentPanel(Refresh, 2) : ShopPanel();
            content.style.backgroundColor = Color.clear;
            content.style.borderTopWidth = 0f; content.style.borderBottomWidth = 0f; content.style.borderLeftWidth = 0f; content.style.borderRightWidth = 0f;
            scroll.Add(content);
            box.Add(scroll);
            _drawerScroll = scroll;
            var restore = _drawerOffset;
            bool restored = restore.x == 0f && restore.y == 0f;
            scroll.RegisterCallback<GeometryChangedEvent>(e => { if (restored) return; restored = true; scroll.scrollOffset = restore; });
            return box;
        }

        // ---------------- витрати (логіка як була) ----------------

        /// <summary>Скільки витрат доступно зараз (число на вкладці).</summary>
        private int ShopAvailable()
        {
            int n = (C.CanBuyCostume ? 1 : 0) + (C.CanBuyInsurance ? 1 : 0) + (C.CanGoAway ? 1 : 0);
            if (C.State.Coach < 0 && C.Money >= CareerRules.CoachWeeklyFee) n++;
            for (int z = 0; z < 3; z++) if (C.CanClinic((BodyZone)z)) { n++; break; }
            return n;
        }

        private VisualElement ShopPanel()
        {
            var card = Glass(8f);
            var row = RingUi.Row();
            row.style.flexWrap = Wrap.Wrap;
            var st = C.State;
            row.Add(ShopBtn("costume", st.CostumeBought ? T.Get("c.shop.costume.owned") : T.Format("c.shop.costume", CareerRules.CostumeCost),
                () => C.BuyCostume(), C.CanBuyCostume, T.Get("c.shop.costume.desc")));
            for (int i = 0; i < CareerRules.Coaches.Length; i++)
            {
                int k = i;
                bool hired = st.Coach == i;
                string name = RingLabels.AttrName(T, CareerRules.Coaches[i]);
                row.Add(ShopBtn("coach:" + i, hired ? T.Format("c.shop.coach.hired", name, st.CoachProgress, CareerRules.CoachProgressStep) : T.Format("c.shop.coach", name, CareerRules.CoachWeeklyFee),
                    () => C.HireCoach(hired ? -1 : k), hired || C.Money >= CareerRules.CoachWeeklyFee, T.Format("c.shop.coach.desc", name, CareerRules.CoachProgressStep)));
            }
            BodyZone worst = BodyZone.Body; int score = -1;
            for (int z = 0; z < 3; z++) { int sc = st.Injury[z] * 10 + st.ZoneDamage[z]; if (sc > score) { score = sc; worst = (BodyZone)z; } }
            row.Add(ShopBtn("clinic", T.Format("c.shop.clinic", CareerRules.ClinicCost), () => C.Clinic(worst), C.CanClinic(worst), T.Get("c.shop.clinic.desc")));
            row.Add(ShopBtn("insurance", st.InsuredSeason == st.Season ? T.Get("c.shop.insurance.owned") : T.Format("c.shop.insurance", CareerRules.InsuranceCost),
                () => C.BuyInsurance(), C.CanBuyInsurance, T.Get("c.shop.insurance.desc")));
            row.Add(ShopBtn("away", st.AwayShow ? T.Get("c.shop.away.on") : T.Format("c.shop.away", CareerRules.AwayTravelCost), () => C.GoAway(), C.CanGoAway,
                T.Get("c.shop.away.desc")));
            card.Add(row);
            return card;
        }

        private Button ShopBtn(string key, string text, Func<bool> act, bool enabled, string hint)
        {
            // сталий ключ: після покупки напис міняється («Костюм · 60» → «Костюм куплено»), а фокус лишається на кнопці
            var b = RingUi.PadKey(Small(text, () => { if (act()) Host.Save(); Refresh(); }, enabled, RingUi.Gold, hint), "shop:" + key);
            b.style.marginRight = 4f;
            // кнопка — одним рядком: стиснута в ряду, «Клініка · 45» ламалась на два (P3 проходу 08.11, кадр 1080)
            b.style.flexShrink = 0f;
            b.style.whiteSpace = WhiteSpace.NoWrap;
            return b;
        }

        // ---------------- шанси, дії, відгук ----------------

        /// <summary>«10+ 28 % · 7–9 44 %» для модифікатора кидка 2d6.</summary>
        private static int Pct(int of36) { return (int)Math.Round(of36 * 100.0 / 36.0); }

        private static int Sum36AtMost(int total)
        {
            int n = 0;
            for (int a = 1; a <= 6; a++) for (int b = 1; b <= 6; b++) if (a + b <= total) n++;
            return n;
        }

        /// <summary>
        /// Травма на плитці «Відновлення» рядком тривоги: «Корпус травмовано: −1 до кидків, ще 2 тиж. (клініка лікує)» (плейтест
        /// 08.11.2026, P2-3, 8c3f16b: бот двічі травмувався й жодного разу не лікувався). У плитці — найгірша зона (місця на два
        /// рядки); null — травм немає.
        /// </summary>
        /// <summary>Тривога травми найгіршої зони на плитці «Відновлення» (зона, мінус до кидків, тижнів лишилось); null — травм немає.</summary>
        private string InjuryWarn(WeekView w)
        {
            if (w.Injuries == null) return null;
            bool any = false;
            foreach (var i in w.Injuries) if (i.WeeksLeft > 0) any = true;
            if (!any) return null;
            var z = WorstZone(w);
            foreach (var i in w.Injuries)
                if (i.Zone == z && i.WeeksLeft > 0)
                    return T.Format("c.injury.tile", T.Get("bodyzone." + (int)i.Zone), C.State.InjuryPenaltyFor(i.Zone), i.WeeksLeft);
            return null;
        }

        private static BodyZone WorstZone(WeekView w)
        {
            BodyZone best = BodyZone.Body; int score = -1;
            foreach (var i in w.Injuries)
            {
                int s = i.WeeksLeft * 10 + i.ZoneDamage;
                if (s > score) { score = s; best = i.Zone; }
            }
            return best;
        }

        private void Do(WeekActionResult r)
        {
            // Закулісся відкриває сцену (екран діалогу): результат покаже хост, коли сцену доведуть до кінця.
            if (r.NeedsScene) { Host.LastWeekResult = null; return; }
            Host.LastWeekResult = r;
            Host.Save();
            Refresh();
        }

        private string ResultText(WeekActionResult r)
        {
            if (!r.Ok) return T.Get("week.reason." + r.Reason);
            var parts = new List<string> { T.Get("week.action." + (int)r.Kind) };
            // «кидок 6 + 0 = 6», а не «кидок 6 0 = 6» (плейтест 08.11.2026, P3, 8c3f16b)
            if (r.HasRoll) parts.Add(T.Format("c.roll", r.Roll, RingChances.Term(r.Modifier), r.Total, RingLabels.TierName(T, r.Tier)));
            if (r.MoneyDelta != 0) parts.Add(T.Format("c.delta.money", Signed(r.MoneyDelta)));
            if (r.DevPointsDelta != 0) parts.Add(T.Format("c.delta.dev", Signed(r.DevPointsDelta)));
            if (r.TrustDelta != 0) parts.Add(T.Format("c.delta.trust", Signed(r.TrustDelta)));
            if (r.FeudDelta != 0) parts.Add(T.Format("c.delta.feud", Signed(r.FeudDelta)));
            if (r.HypeBonusDelta != 0) parts.Add(T.Format("c.delta.hype", Signed(r.HypeBonusDelta)));
            if (r.RivalBoosted) parts.Add(T.Get("c.delta.rival"));
            if (r.InjuredZone >= 0) parts.Add(T.Format("c.delta.injury", T.Get("bodyzone." + r.InjuredZone)));
            if (r.PopularityDelta != 0) parts.Add(T.Format("c.delta.pop", Signed(r.PopularityDelta)));
            if (r.LockerDelta != 0) parts.Add(T.Format("c.delta.locker", Signed(r.LockerDelta)));
            if (r.ActionsDelta != 0) parts.Add(T.Format("c.delta.actions", Signed(r.ActionsDelta)));
            if (r.PlanChanged) parts.Add(T.Get("c.delta.plan"));
            if (r.CoachRaised) parts.Add(T.Get("c.delta.coach"));
            if (r.NeedsChoice) parts.Add(T.Get("c.week.promo_choice"));
            string s = string.Join(" · ", parts);
            if (!string.IsNullOrEmpty(r.RumorKey)) s += "\n" + T.Get(r.RumorKey);
            return s;
        }

        public override void OnDigit(int n)
        {
            var w = C.GetWeekView();
            if (w.PromoChoicePending)
            {
                if (n == 1) Do(C.ChoosePromoReward(WeekPromoReward.Feud));
                else if (n == 2) Do(C.ChoosePromoReward(WeekPromoReward.Hype));
                return;
            }
            if (w.ActionsLeft <= 0) return;
            switch (n)
            {
                case 1: Do(C.Train(false)); break;
                case 2: if (w.Money >= CareerRules.TrainerCost) Do(C.Train(true)); break;
                case 3: if (w.CanRecover) Do(C.Recover(WorstZone(w), false)); break;
                case 4: Do(C.Promo()); break;
                case 5: Do(C.Backstage()); break;
                case 6: Do(C.SideJob()); break;
            }
        }

        public override void OnConfirm()
        {
            if (C.State.PromoChoicePending) return;
            Host.LastWeekResult = null;
            C.EndWeek();
            Host.Save();
        }
    }
}
