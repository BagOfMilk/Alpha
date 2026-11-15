using System;
using System.Collections.Generic;
using Game.Core.Ring;
using Game.Core.Ring.Career;
using Game.Core.Ring.Match;
using Game.Gameplay.Ring.Models;
using Game.Gameplay.Ring.UI.Menus;
using UnityEngine;
using UnityEngine.UIElements;
// У Unity 6.4 є і UnityEngine.PointerType (IMGUI), і UnityEngine.UIElements.PointerType — без псевдоніма CS0104 (ПМ 15.11.2026).
using PointerType = UnityEngine.UIElements.PointerType;

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
        private readonly List<int> _tileKeys = new List<int>();   // ключ плитки (цифра) поруч із _tiles — для огляду меню
        private Label _hint;
        private VisualElement _row, _gold, _weekPlate, _last, _ropes, _header, _drawer, _poster;
        // шухляда над афішею (ПМ 13.11.2026): шапка й підвал шухляди — для найменшої прокрутки; рядки умов афіші згортаються, коли під
        // ПОВНОЮ афішею не влазить навіть мінімум шухляди (CareerTileLayout.DrawerOverPoster); висота схованих рядків — з останньої
        // розкладки розгорнутої афіші
        private VisualElement _drawerHead, _drawerFoot, _posterDetails;
        private bool _posterFolded;
        private float _detailsH;
        /// <summary>На афіші є наліпка форми суперника, що звисає з її низу (межа шухляди — від наліпки).</summary>
        private bool _posterSticker;
        private string _hintDefault = "";
        private bool _hintWarn;
        private ScrollView _drawerScroll;
        private Vector2 _drawerOffset;
        // стрічка дії (АД 15.11.2026): рядок дельт міряється раз на результат — перебудова екрана (шухляда, покупка) бере готовий рядок і
        // не програє появу вдруге (стрічка не блимає)
        private readonly MenuAnimator _anim = new MenuAnimator();
        private VisualElement _animBox;
        private WeekActionResult _stripFor;
        private string _stripDeltas;
        private List<string> _stripHidden;

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
            _tiles.Clear(); _tileKeys.Clear();
            var v = C.GetCareerView();
            var w = C.GetWeekView();
            if (!_accordionSet)
            {
                _accordionSet = true;
                // межа самовідкриття — два ряди плиток, тобто ширина панелі в її одиницях: до розкладки — з кадру (екрани кар'єри
                // завжди в еталоні висоти 900, «Розмір інтерфейсу» їх не масштабує), а не в пікселях
                float pw = Root.layout.width;
                if ((float.IsNaN(pw) || pw <= 0f) && Screen.height > 0) pw = Screen.width * CareerTileLayout.PanelH / Screen.height;
                _dev = CareerTileLayout.WeekDrawerAutoOpen(pw, w.DevPoints);
                _shop = ExpandForShots && !_dev;
            }
            Root.Add(ShowKit.PlayerStrip(C, T, v));
            Root.Add(ShowKit.Resources(T, v, CareerScreenLines.PopularityMax(C)));
            _weekPlate = WeekPlate(v);
            Root.Add(_weekPlate);
            Root.Add(ShowKit.RivalStrip(C, T, w.Card.Opponent, w.Card.OpponentFeudLevel, true));
            _posterSticker = false;
            _posterFolded = false;
            _detailsH = 0f;
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
                // №11: зірка поза рингом тренується раз на тиждень — обидві плитки тренування вимкнені з причиною (§5.3)
                string trainWhy = CareerScreenLines.TrainReason(T, w, false), coachWhy = CareerScreenLines.TrainReason(T, w, true);
                AddTile(1, T.Get("week.action.0"), T.Format("c.week.pay.train", CareerRules.TrainingPoints), null, trainWhy == null, trainWhy,
                    () => Do(C.Train(false)), T.Get("week.action.0.desc"));
                AddTile(2, T.Get("c.week.train_coach"), T.Format("c.week.pay.coach", CareerRules.TrainingPoints + CareerRules.TrainerExtraPoints, CareerRules.TrainerCost),
                    null, coachWhy == null, coachWhy, () => Do(C.Train(true)), T.Get("c.week.train_coach.desc"));
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
                // №8: під виключним правом підробіток заборонено (§6.4) — плитка вимкнена з причиною
                string jobWhy = CareerScreenLines.SideJobReason(T, w, C.ExclusiveActive);
                AddTile(6, T.Get("week.action.4"), T.Format("c.hub.pay.job", job), T.Format("c.hub.risk", Pct(Sum36AtMost(CareerRules.SideJobInjuryAtMost))),
                    jobWhy == null, jobWhy, () => Do(C.SideJob()), T.Get("week.action.4.desc"));
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
            // повна плита лише на 21:9: на 16:9 з нею шість плиток по 183 — легенда шансів лягала на скіс (CareerTileLayout.FullGoldFrom)
            float gw = CareerTileLayout.GoldWidth(width);
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
            // стрічка дії — у лівому низу на базовій лінії шухляди, над канатами й «ДІЇ ТИЖНЯ» (АД 15.11.2026)
            if (_last != null) _last.style.bottom = CareerTileLayout.DrawerBottom + lift;
            if (_drawer != null)
            {
                _drawer.style.width = CareerTileLayout.WeekDrawerWidth(width, Root.layout.height);
                _drawer.style.bottom = CareerTileLayout.DrawerBottom + lift;
                // верх шухляди не вище за низ афіші з просвітом, решта — прокруткою (огляд слоту 33, ПМ 13.11.2026). Колишній відкат
                // «менше 150 під афішею — до стрічки суперника» клав «Розвиток» на картку з трьома рядками умов (4:3, s33 p11 006–011).
                // Не влазить і мінімум шухляди — афіша згортає рядки умов, доки шухляда відкрита.
                float height = Root.layout.height;
                float bottomY = height - (CareerTileLayout.DrawerBottom + lift);
                bool posterReady = _poster != null && !float.IsNaN(_poster.layout.height) && _poster.layout.height > 0f;
                if (posterReady && !float.IsNaN(height))
                {
                    // рішення — за повною висотою афіші (з рядками, навіть сховані): однакове в обох станах, тож не коливається й
                    // розгортає картку, щойно проміжний прохід розкладки минув (кадр слоту 34, a_s1 4:3)
                    if (!_posterFolded && _posterDetails != null && !float.IsNaN(_posterDetails.layout.height)) _detailsH = _posterDetails.layout.height;
                    bool fold;
                    // над одним рядом плиток — ще й під обличчям букерки (АД 15.11.2026, рішення (а))
                    float max = CareerTileLayout.WeekDrawerMaxHeight(bottomY, height, twoRows, _poster.layout.y + _poster.layout.height, _posterFolded,
                        _detailsH, _posterSticker, _dev, out fold);
                    if (fold != _posterFolded) FoldPoster(fold);
                    _drawer.style.maxHeight = max;
                    if (_drawerScroll != null)
                        _drawerScroll.style.minHeight = CareerTileLayout.DrawerScrollMin(max, _drawerHead != null ? _drawerHead.layout.height : float.NaN,
                            _drawerFoot != null ? _drawerFoot.layout.height : 0f);
                }
            }
            if (_weekPlate != null)
            {
                if (narrow) { _weekPlate.style.left = ArenaKit.Edge; _weekPlate.style.marginLeft = 0f; _weekPlate.style.top = ArenaKit.SecondY + 104f + 14f; }
                else { _weekPlate.style.left = Length.Percent(50f); _weekPlate.style.marginLeft = -175f; _weekPlate.style.top = ArenaKit.Top; }
            }
        }

        /// <summary>Рядки умов афіші (пункти під планом) — сховати, доки шухляда відкрита й під афішею їй не вистачає місця.</summary>
        private void FoldPoster(bool fold)
        {
            _posterFolded = fold;
            if (_posterDetails != null) _posterDetails.style.display = fold ? DisplayStyle.None : DisplayStyle.Flex;
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
            _tileKeys.Add(key);
            _row.Add(t);
            return t;
        }

        /// <summary>
        /// Огляд меню (<c>RingMenuShots</c>): навести на плитку з ключем <paramref name="key"/> так само, як курсор (підсвіт і опис у
        /// рядку-підказці шапки), чи прибрати наведення. false — такої плитки на тижні немає.
        /// </summary>
        internal bool HoverTileForShots(int key, bool enter)
        {
            int i = _tileKeys.IndexOf(key);
            if (i < 0) return false;
            var t = _tiles[i];
            if (enter) { if (t.OnHover != null) t.OnHover(); }
            else if (t.OnUnhover != null) t.OnUnhover();
            return true;
        }

        /// <summary>Рядок-підказка шапки ряду: підкладка лише під текстом (порожня підкладка читалась як зайва смуга — кадр 08.11.2026).</summary>
        private void SetHint(string text)
        {
            if (_hint == null) return;
            _hint.text = TextBreak.GlueDashes(text) ?? "";
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
            // без плану й умов — саме значення, без мітки («Плану букера немає · Умов немає», ПМ 13.11.2026)
            string plan = (c.IsFeud ? "" : CareerScreenLines.PlanLine(T, c.Plan) + " · ") + StipText(c.Stipulations);
            col.Add(MenuKit.Body(plan, BrandTokens.TypeMinInfo, RingTheme.Bg, true));
            // рядки умов — окремим блоком: шухляда, якій під афішею не вистачає місця, згортає саме їх (FoldPoster)
            var details = new VisualElement();
            details.pickingMode = PickingMode.Ignore;
            _posterDetails = details;
            if (c.PromoHypeBonus != 0) details.Add(Bullet(T.Format("c.card.promo_bonus", Signed(c.PromoHypeBonus))));
            if (c.IsRematch) details.Add(Bullet(T.Get("c.hub.rematch")));
            string outgrown = CareerScreenLines.OutgrownLine(T, c);   // №7: переріс щабель — букер ставить легший матч
            if (outgrown != null) details.Add(Bullet(outgrown));
            // P2-4 (8c3f16b): до росту гравця — нейтральний рядок «за місцем у картці», без червоної наліпки
            if (c.Opponent != null && c.RivalSteps > 0 && !ShowKit.RivalInForm(C)) details.Add(Bullet(ShowKit.RivalStepsText(T, c)));
            // №8 і №11: що тримає цей тиждень — пастка контракту (гонорар урізаний, без виїзних і підробітку) і пізня роль
            string exclusive = new SeasonOfferModel(C, T).ExclusiveText;
            if (exclusive != null) details.Add(Bullet(exclusive));
            string late = new LateRoleModel(C, T).CurrentText;
            if (late != null) details.Add(Bullet(late));
            col.Add(details);
            box.Add(col);
            if (c.Opponent != null && c.RivalSteps > 0 && ShowKit.RivalInForm(C))
            {
                var st = new Sticker(T.Format("c.hub.form", c.RivalSteps));
                st.style.position = Position.Absolute;
                st.style.left = 26f; st.style.bottom = -CareerTileLayout.PosterStickerHang;
                box.Add(st);
                _posterSticker = true;
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

        /// <summary>
        /// Стрічка дії тижня (АД 15.11.2026, варіант (А)): лівий низ на базовій лінії шухляди (bottom ставить Fit), ширина за вмістом до
        /// колонки грошей, стилем панелі грошей. Рівно два рядки: «Дія · Результат» словом і дельти за важливістю з «· ще N»; колір —
        /// лише смуга (Gold — вдалося, Signal — ні), текст завжди Text. Наведення, фокус геймпада й дотик показують сховане в рядку-підказці.
        /// </summary>
        private VisualElement LastResult(WeekActionResult r)
        {
            bool fresh = !ReferenceEquals(r, _stripFor);
            if (fresh) { _stripFor = r; _stripDeltas = null; _stripHidden = null; }
            var box = new WeekResultStrip(() => { string h = StripHover(); if (!string.IsNullOrEmpty(h)) SetHint(h); }, () => SetHint(_hintDefault));
            ArenaKit.Place(box, ArenaKit.Edge, null, null, CareerTileLayout.DrawerBottom);
            box.style.maxWidth = ArenaKit.StripW;
            box.style.overflow = Overflow.Visible;
            box.Add(new ArenaPlate { Fill = RingTheme.WithAlpha(RingTheme.Panel, BrandTokens.AlphaPanel), Stripe = CareerScreenLines.WeekResultSucceeded(r) ? RingTheme.Gold : RingTheme.Signal });
            var head = MenuKit.Body(CareerScreenLines.WeekResultHead(T, r), BrandTokens.TypeMinInfo, RingTheme.Text);
            head.style.paddingLeft = CareerTileLayout.WeekStripPadL; head.style.paddingRight = CareerTileLayout.WeekStripPadR; head.style.paddingTop = 7f;
            box.Add(head);
            var line2 = MenuKit.Body(_stripDeltas ?? "", BrandTokens.TypeMinInfo, RingTheme.Text);
            line2.style.paddingLeft = CareerTileLayout.WeekStripPadL; line2.style.paddingRight = CareerTileLayout.WeekStripPadR; line2.style.paddingBottom = 8f;
            line2.style.whiteSpace = WhiteSpace.NoWrap; line2.style.overflow = Overflow.Hidden; line2.style.textOverflow = TextOverflow.Ellipsis;   // рівно один рядок
            box.Add(line2);
            var deltas = CareerScreenLines.WeekResultDeltas(T, r, OpponentName(), HeroForms.ShowsHeroine(T));
            if (deltas.Count == 0) { _stripDeltas = ""; _stripHidden = new List<string>(); }
            line2.style.display = deltas.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            // дельт немає (відновлення без лікаря: ядро не пише зону в результат) — нижнє поле бере рядок 1
            head.style.paddingBottom = deltas.Count == 0 ? 8f : 0f;
            if (_stripDeltas != null) { if (fresh) Appear(box); return box; }
            // рядок дельт міряється шрифтом стрічки після першої розкладки; до того стрічку не видно (жодного кадру з неурізаним рядком)
            box.style.opacity = 0f;
            var probe = MenuKit.Body("", BrandTokens.TypeMinInfo, RingTheme.Text);
            probe.style.position = Position.Absolute; probe.style.left = 0f; probe.style.top = 0f;
            probe.style.visibility = Visibility.Hidden;
            box.Add(probe);
            Func<string, bool> fits = s => probe.MeasureTextSize(TextBreak.GlueDashes(s), 0f, VisualElement.MeasureMode.Undefined, 0f, VisualElement.MeasureMode.Undefined).x
                <= CareerTileLayout.WeekStripTextW;
            EventCallback<GeometryChangedEvent> fit = null;
            fit = e =>
            {
                if (_stripDeltas != null || !ReferenceEquals(r, _stripFor)) return;
                float em = probe.MeasureTextSize("Ш", 0f, VisualElement.MeasureMode.Undefined, 0f, VisualElement.MeasureMode.Undefined).x;
                if (float.IsNaN(em) || em <= 0f) return;   // шрифт ще не розкладено
                string line;
                bool clipped;   // найважливіша не влазить і з хвостом (довге ім'я суперника Б6) — кінець «…», ціла — у підказці
                int k = CareerScreenLines.StripParts(T, deltas, fits, out line, out clipped);
                _stripDeltas = line;
                _stripHidden = deltas.GetRange(k, deltas.Count - k);
                line2.text = TextBreak.GlueDashes(line);
                box.UnregisterCallback(fit);
                Appear(box);
            };
            box.RegisterCallback(fit);
            return box;
        }

        /// <summary>Поява стрічки — токенами руху плит меню (поява й зсув зліва), лише раз на результат; «Менше руху» — одразу.</summary>
        private void Appear(VisualElement box)
        {
            if (_animBox != null) _anim.Forget(_animBox);
            _animBox = box;
            if (MenuAnimator.Reduced) { box.style.opacity = 1f; return; }
            _anim.FromTo(box, MenuProp.Opacity, 0f, 1f, BrandTokens.MotionMenuPlateInMs, Ease.OutCubic);
            _anim.FromTo(box, MenuProp.X, -40f, 0f, BrandTokens.MotionMenuPlateInMs, Ease.OutCubic);
        }

        /// <summary>Підказка стрічки: лише сховане — кидок і «ще N» дельти, не довше двох рядків найвужчої шапки.</summary>
        private string StripHover()
        {
            var r = _stripFor;
            if (r == null || _hint == null || _stripHidden == null) return null;
            Func<string, bool> fits = s => _hint.MeasureTextSize(TextBreak.GlueDashes(s), CareerTileLayout.WeekHintNarrowest, VisualElement.MeasureMode.Exactly,
                0f, VisualElement.MeasureMode.Undefined).y <= CareerTileLayout.HintMaxHeight - CareerTileLayout.HintPadV + 0.5f;
            return CareerScreenLines.WeekResultHover(T, CareerScreenLines.WeekResultRoll(T, r), _stripHidden, fits);
        }

        public override void Tick(float unscaledDeltaTime)
        {
            _anim.Tick(unscaledDeltaTime * 1000f);
        }

        // ---------------- низ: шапка ряду, шухляда, плитки ----------------

        private VisualElement RowHeader(WeekView w)
        {
            var row = ArenaKit.Abs(ArenaKit.Edge, null, ArenaKit.Edge, ArenaKit.HeaderBottom, null, CareerTileLayout.WeekHeaderH);
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
            // чутка останньої дії стоїть тут до наступної (АД 15.11.2026: гравець не має її шукати)
            _hintDefault = CareerScreenLines.WeekHintDefault(promo, strained, CareerScreenLines.WeekRumor(T, Host.LastWeekResult));
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
            // вкладки не стискаються: на 4:3 підказка дії в два рядки тиснула їх, і цифри «0»/«4» лягали на скіс (m4x3 050, слот 31);
            // стискається лише підказка (flexShrink 1, minWidth 0)
            var devTab = new TabPlate(T.Get("dev.title"), w.DevPoints, _dev, () => { _dev = !_dev; if (_dev) _shop = false; _drawerScroll = null; _drawerOffset = Vector2.zero; Refresh(); }, w.DevPoints > 0);
            var shopTab = new TabPlate(T.Get("c.hub.shop"), ShopAvailable(), _shop, () => { _shop = !_shop; if (_shop) _dev = false; _drawerScroll = null; _drawerOffset = Vector2.zero; Refresh(); }, false);
            devTab.style.flexShrink = 0f; shopTab.style.flexShrink = 0f;
            row.Add(devTab);
            row.Add(shopTab);
            return row;
        }

        /// <summary>Шухляда «Розвиток»/«Витрати» (B03 §3): виростає вгору над шапкою ряду, ряд дій не рухається; закривають вкладка й Esc (<see cref="OnBack"/>).</summary>
        private VisualElement Drawer()
        {
            // над канатами (огляд слоту 30); ширину на 4:3 (472) ставить Fit
            var box = ArenaKit.Abs(null, null, ArenaKit.Edge, CareerTileLayout.DrawerBottom, CareerTileLayout.WeekDrawerW, null);
            box.style.maxHeight = CareerTileLayout.DrawerMaxH;   // уточнює Fit: не вище за низ афіші з просвітом
            box.style.overflow = Overflow.Visible;
            box.style.flexDirection = FlexDirection.Column;
            box.pickingMode = PickingMode.Position;
            box.Add(new ArenaPlate { Fill = RingTheme.WithAlpha(RingTheme.Panel, BrandTokens.AlphaPlate), Stripe = RingTheme.Gold, StripeW = 6f,
                SlantCapH = CareerTileLayout.PlateSlantCapH });
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
            _drawerHead = top;
            top.RegisterCallback<GeometryChangedEvent>(e => Fit());   // дворядкова підказка — менша прокрутка, підвал не на канатах
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            ArenaKit.ThinScroller(scroll);
            scroll.style.flexShrink = 1f; scroll.style.minHeight = CareerTileLayout.DevScrollMinH;
            scroll.style.paddingLeft = CareerTileLayout.DrawerPadLeft; scroll.style.paddingRight = CareerTileLayout.DrawerPadRight;
            scroll.style.paddingBottom = _dev ? CareerTileLayout.DrawerScrollPadBottom : 10f;
            // «▸ Усе (N)» — у підвалі під прокруткою, як на плитах розвитку: край прокрутки і канат різали його (огляд слоту 30, 4:3);
            // рядок-підказка шухляди — у шапці, тож підвал лише з перемикачем
            var foot = _dev ? DevFooter(CareerTileLayout.DrawerPadLeft + CareerTileLayout.DevCardPad, CareerTileLayout.DrawerPadRight + CareerTileLayout.DevCardPad,
                CareerTileLayout.DevFootPadBottom) : null;
            var content = _dev ? DevelopmentPanel(Refresh, 2, moreTo: foot) : ShopPanel();
            content.style.backgroundColor = Color.clear;
            content.style.borderTopWidth = 0f; content.style.borderBottomWidth = 0f; content.style.borderLeftWidth = 0f; content.style.borderRightWidth = 0f;
            scroll.Add(content);
            box.Add(scroll);
            if (foot != null) box.Add(foot);
            _drawerFoot = foot;
            _drawerScroll = scroll;
            // щойно розгорнутий «Усе» — до початку повного списку, інакше — той самий зсув, що до перебудови; обидва — на межу рядка
            // (ПМ 13.11.2026, п. 2 і 5: зріз поради над «АТРИБУТИ» на с4, «ПЕРКИ» під краєм на 4:3), і в «Витратах» теж
            var mark = foot != null ? DevAllStart : null;
            DevAllStart = null;
            var restore = _drawerOffset;
            RowSnap(scroll, () => mark != null ? MarkTop(mark, scroll) : restore.y, mark != null);
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
            // ціна — рівня федерації (C.CostumePrice), а не стала 60 з правил: з регіональної напис був меншим за списане (ПМ 12.11.2026)
            row.Add(ShopBtn("costume", CareerScreenLines.CostumeButton(T, C),
                () => C.BuyCostume(), C.CanBuyCostume, T.Get("c.shop.costume.desc")));
            for (int i = 0; i < CareerRules.Coaches.Length; i++)
            {
                int k = i;
                bool hired = st.Coach == i;
                // крок — цього тренера (C.CoachStepFor): тренер витривалості з сезону 4 довший, стала з правил тут збрехала б (ПМ 12.11.2026)
                row.Add(ShopBtn("coach:" + i, CareerScreenLines.CoachButton(T, C, k),
                    () => C.HireCoach(hired ? -1 : k), hired || C.Money >= CareerRules.CoachWeeklyFee, CareerScreenLines.CoachHint(T, C, k)));
            }
            BodyZone worst = BodyZone.Body; int score = -1;
            for (int z = 0; z < 3; z++) { int sc = st.Injury[z] * 10 + st.ZoneDamage[z]; if (sc > score) { score = sc; worst = (BodyZone)z; } }
            row.Add(ShopBtn("clinic", T.Format("c.shop.clinic", CareerRules.ClinicCost), () => C.Clinic(worst), C.CanClinic(worst), T.Get("c.shop.clinic.desc")));
            row.Add(ShopBtn("insurance", st.InsuredSeason == st.Season ? T.Get("c.shop.insurance.owned") : T.Format("c.shop.insurance", CareerRules.InsuranceCost),
                () => C.BuyInsurance(), C.CanBuyInsurance, T.Get("c.shop.insurance.desc")));
            row.Add(ShopBtn("away", st.AwayShow ? T.Get("c.shop.away.on") : T.Format("c.shop.away", CareerRules.AwayTravelCost), () => C.GoAway(), C.CanGoAway,
                CareerScreenLines.AwayHint(T, C)));   // №8: під виключним правом — з причиною, чому вимкнено
            // №8: менеджерку можна звільнити (ядро «Кар'єри», FireManager); посеред сцени закулісся — ні
            if (CareerScreenLines.FireManagerShown(C))
                row.Add(ShopBtn("manager", T.Get("c.shop.manager.fire"), () => C.FireManager(), !C.GetWeekView().BackstageOpen,
                    CareerScreenLines.FireManagerHint(T)));
            card.Add(row);
            // житло й дорога цієї федерації — списуються наприкінці тижня; у вищій федерації дорожче (C.WeeklyCost)
            var living = MenuKit.Body(CareerScreenLines.LivingLine(T, C), BrandTokens.TypeMinInfo, RingTheme.TextDim);
            living.style.marginTop = 6f;
            card.Add(living);
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
                    return CareerScreenLines.ScarTail(T, T.Format("c.injury.tile", T.Get("bodyzone." + (int)i.Zone), C.State.InjuryPenaltyFor(i.Zone), i.WeeksLeft),
                        i.ScarWeeks);   // Б5: слід — з сезону 4
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

        /// <summary>Ім'я суперника найближчого шоу з картки тижня (як «Суперник: …» на картці вечора); немає картки — null.</summary>
        /// <summary>
        /// Огляд меню (<c>RingMenuShots</c>): ім'я в рядку суперника Б6 стрічки дії замість суперника картки — кадр найгіршого випадку
        /// (найширше ім'я ростера); null — суперник картки. Гра його не ставить.
        /// </summary>
        internal static string OpponentNameForShots;

        private string OpponentName()
        {
            if (OpponentNameForShots != null) return OpponentNameForShots;
            var w = C.GetWeekView();
            return w != null && w.Card != null && w.Card.Opponent != null ? T.Get(w.Card.Opponent.NameKey) : null;
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
            // клавіші — ті самі умови, що й плитки (вимкнена плитка цифрою не натискається)
            switch (n)
            {
                case 1: if (CareerScreenLines.TrainReason(T, w, false) == null) Do(C.Train(false)); break;
                case 2: if (CareerScreenLines.TrainReason(T, w, true) == null) Do(C.Train(true)); break;
                case 3: if (w.CanRecover) Do(C.Recover(WorstZone(w), false)); break;
                case 4: Do(C.Promo()); break;
                case 5: Do(C.Backstage()); break;
                case 6: if (CareerScreenLines.SideJobReason(T, w, C.ExclusiveActive) == null) Do(C.SideJob()); break;
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

    /// <summary>
    /// Стрічка дії тижня як ціль фокусу (АД 15.11.2026): нічого не натискає, але фокус геймпада, наведення миші й дотик показують те, що
    /// в ній сховано (кидок і «ще N»), у рядку-підказці шапки — тією самою дорогою, що описи плиток.
    /// </summary>
    internal sealed class WeekResultStrip : VisualElement, IPadTarget
    {
        private readonly Action _show, _hide;
        private VisualElement _frame;

        public WeekResultStrip(Action show, Action hide)
        {
            _show = show; _hide = hide;
            pickingMode = PickingMode.Position;
            PadFocus.Hover(this, _show, _hide);
            // дотик: торкнулися стрічки — показали (наведення пальцем немає)
            RegisterCallback<PointerDownEvent>(e => { if (e.pointerType == PointerType.touch && _show != null) _show(); });
        }

        bool IPadTarget.PadEnabled { get { return false; } }
        string IPadTarget.PadKey { get { return "week:last"; } }
        int IPadTarget.PadRank { get { return 5; } }
        // фокус геймпада видно рамкою (як PadMark), навіть коли схованого немає й рядок-підказка не змінюється
        void IPadTarget.PadHot(bool on)
        {
            if (on)
            {
                if (_frame == null) { _frame = PadFocus.Frame(); Add(_frame); }
                if (_show != null) _show();
            }
            else
            {
                if (_frame != null) { _frame.RemoveFromHierarchy(); _frame = null; }
                if (_hide != null) _hide();
            }
        }
        void IPadTarget.PadActivate() { }
        bool IPadTarget.PadStep(int delta) { return false; }
    }
}
