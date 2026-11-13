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
    /// Спільні складники вечірніх екранів кар'єри (ТЗ B03 v2 §6, §8, мова «Арена»): стрічка гравця із зонами тіла, ресурси,
    /// стрічка суперника, рядок дій унизу праворуч (золота плита, плитки зліва від неї).
    /// </summary>
    internal static class ShowKit
    {
        public static VisualElement PlayerStrip(CareerSession c, RingText t, CareerView v)
        {
            var box = ArenaKit.Abs(ArenaKit.Edge, ArenaKit.Top, null, null, ArenaKit.StripW, ArenaKit.StripH);
            box.style.overflow = Overflow.Visible;
            box.Add(new ArenaPlate { Fill = RingTheme.WithAlpha(RingTheme.Panel, BrandTokens.AlphaPanel), Outline = ArenaKit.RoleColor(v.Role), Stripe = ArenaKit.RoleColor(v.Role) });
            var name = ArenaKit.Big(v.RingName, 40f, RingTheme.Text);
            ArenaKit.Place(name, 24f, 8f, null, null);
            box.Add(name);
            var tag = ArenaKit.Row();
            ArenaKit.Place(tag, null, 12f, 34f, null);
            // пробіл після «·» — на початку наступного напису: кінцевий пробіл напис не малює («ЛЕТУН ·ГЕРОЙ» проти «ГЕРОЙ · ЛЕТУН»
            // суперника — кадри слоту 27, 09.11.2026)
            var gimmick = ArenaKit.Tag(t.Get("gimmick." + c.Content.GimmickOf(v.Gimmick).Key) + " ·", RingTheme.TextDim);
            tag.Add(gimmick);
            tag.Add(ArenaKit.Tag(" " + RingLabels.PathName(t, v.Role, HeroForms.ShowsHeroine(t)), ArenaKit.RoleText(v.Role)));
            if (v.IsChampion) tag.Add(ArenaKit.Tag(" · " + t.Get("c.champion"), RingTheme.Gold));
            box.Add(tag);
            ArenaKit.FitNameAndTag(box, name, tag, gimmick, true, 40f, 26f);
            var zones = new VisualElement();
            ArenaKit.Place(zones, 24f, 54f, null, null);
            zones.pickingMode = PickingMode.Ignore;
            var doll = new InjuryDoll();
            for (int z = 0; z < 3; z++)
            {
                // сегментів — поріг травми зони з виду (у Хардкорника 4), а не стала 3 (рев'ю 13.11.2026 — той самий клас, що А2)
                var meter = CareerScreenLines.ZoneMeterFor(v.Injuries, (BodyZone)z);
                int filled = meter.Filled;
                var r = ArenaKit.Row();
                r.style.height = 17f;
                var l = ArenaKit.Tag(t.Get("bodyzone." + z), RingTheme.TextDim);
                l.style.width = 92f;
                r.Add(l);
                r.Add(ArenaKit.Segments(meter.Segments, filled, RingTheme.Signal, 66f, 11f));
                var val = MenuKit.Display(CareerScreenLines.ZoneMeterText(t, meter), BrandTokens.TypeLabel, filled > 0 ? RingTheme.Text : RingTheme.TextDim, RingFontRole.Display900, 0.02f);
                val.style.marginLeft = 10f;
                r.Add(val);
                zones.Add(r);
                doll.Set(z, filled > 0);
            }
            box.Add(zones);
            ArenaKit.Place(doll, null, 34f, 48f, null);   // нижче мітки ролі: голова ляльки торкалась її кінця (рев'ю 08.11.2026)
            box.Add(doll);
            return box;
        }

        /// <summary>
        /// Гроші, популярність і довіра. <paramref name="popMax"/> — шкала популярності до стелі федерації
        /// (<see cref="CareerScreenLines.PopularityMax"/>, ПМ 12.11.2026): на місцевій 4, а не 10 — повна шкала означає «час вище».
        /// </summary>
        public static VisualElement Resources(RingText t, CareerView v, int popMax)
        {
            var box = ArenaKit.Abs(ArenaKit.Edge, ArenaKit.SecondY, null, null, ArenaKit.StripW, 104f);
            box.style.overflow = Overflow.Visible;
            box.Add(new ArenaPlate { Fill = RingTheme.WithAlpha(RingTheme.Panel, BrandTokens.AlphaPanel), Stripe = ArenaKit.RoleColor(v.Role) });
            var money = ArenaKit.Row();
            ArenaKit.Place(money, 24f, 6f, null, null);
            money.style.alignItems = Align.FlexEnd;
            money.Add(ArenaKit.Money(v.Money, 40f, RingTheme.Text));
            var ml = ArenaKit.Tag(t.Get("c.hub.money"), RingTheme.TextDim);
            ml.style.marginLeft = 10f; ml.style.marginBottom = 6f;
            money.Add(ml);
            box.Add(money);
            box.Add(MeterLine(t.Get("c.hub.pop"), v.Popularity, popMax, 56f));
            box.Add(MeterLine(t.Get("c.hub.trust"), v.Trust, CareerRules.MaxTrust, 78f));
            return box;
        }

        private static VisualElement MeterLine(string name, int value, int max, float top)
        {
            var r = ArenaKit.Row();
            ArenaKit.Place(r, 24f, top, null, null);
            var l = ArenaKit.Tag(name, RingTheme.TextDim);
            l.style.width = 170f;
            r.Add(l);
            r.Add(ArenaKit.Segments(max, value, RingTheme.Text, max * 13f, 12f));
            var val = MenuKit.Display(value + "/" + max, BrandTokens.TypeLabel, RingTheme.Text, RingFontRole.Display900, 0.02f);
            val.style.marginLeft = 10f;
            r.Add(val);
            return r;
        }

        /// <summary>Стрічка суперника (праворуч): прізвисько великим, роль · ґімік, справжнє ім'я; ворожнеча — лише коли відома.</summary>
        public static VisualElement RivalStrip(CareerSession c, RingText t, NpcDef npc, int feudLevel, bool showFeud)
        {
            var box = ArenaKit.Abs(null, ArenaKit.Top, ArenaKit.Edge, null, ArenaKit.StripW, ArenaKit.StripH);
            box.style.overflow = Overflow.Visible;
            Color role = npc != null ? ArenaKit.RoleColor(npc.Path) : RingTheme.Border;
            box.Add(new ArenaPlate { Fill = RingTheme.WithAlpha(RingTheme.Panel, BrandTokens.AlphaPanel), Outline = role, Stripe = role, Mirror = true });
            if (npc == null) return box;
            string nick, name;
            ArenaKit.SplitNick(t.Get(npc.NameKey), out nick, out name);
            var nl = ArenaKit.Big(nick, 40f, RingTheme.Text);
            ArenaKit.Place(nl, null, 8f, 24f, null);
            box.Add(nl);
            var tag = ArenaKit.Row();
            ArenaKit.Place(tag, 40f, 12f, null, null);
            tag.Add(ArenaKit.Tag(RingLabels.PathName(t, npc.Path, npc.Female), ArenaKit.RoleText(npc.Path)));   // рід суперника — свій (P2-6)
            var rg = ArenaKit.Tag(" · " + t.Get("gimmick." + c.Content.GimmickOf(npc.Gimmick).Key), RingTheme.TextDim);
            tag.Add(rg);
            box.Add(tag);
            ArenaKit.FitNameAndTag(box, nl, tag, rg, false, 40f, 26f);
            if (!string.IsNullOrEmpty(name))
            {
                var real = ArenaKit.Tag(name, RingTheme.TextDim);
                ArenaKit.Place(real, null, 54f, 24f, null);
                box.Add(real);
            }
            if (showFeud)
            {
                var feud = ArenaKit.Row();
                ArenaKit.Place(feud, null, 78f, 24f, null);
                feud.Add(ArenaKit.Tag(t.Get("c.hub.feud"), RingTheme.TextDim));
                var seg = ArenaKit.Segments(CareerRules.MaxFeud, feudLevel, RingTheme.Signal, 120f, 12f);
                seg.style.marginLeft = 12f;
                feud.Add(seg);
                var fv = MenuKit.Display(feudLevel + "/" + CareerRules.MaxFeud, BrandTokens.TypeLabel, RingTheme.Text, RingFontRole.Display900, 0.02f);
                fv.style.marginLeft = 10f;
                feud.Add(fv);
                box.Add(feud);
            }
            return box;
        }

        /// <summary>Квадратний маркер рядка квитанції (золото — звичайний, сигнал — втрата).</summary>
        public static VisualElement Marker(Color fill)
        {
            var sq = new VisualElement();
            sq.style.width = 13f; sq.style.height = 13f; sq.style.marginRight = 12f; sq.style.flexShrink = 0f;
            sq.style.backgroundColor = fill;
            sq.style.borderTopWidth = 2f; sq.style.borderBottomWidth = 2f; sq.style.borderLeftWidth = 2f; sq.style.borderRightWidth = 2f;
            sq.style.borderTopColor = RingTheme.Bg; sq.style.borderBottomColor = RingTheme.Bg; sq.style.borderLeftColor = RingTheme.Bg; sq.style.borderRightColor = RingTheme.Bg;
            sq.pickingMode = PickingMode.Ignore;
            return sq;
        }

        public static VisualElement Rule()
        {
            var line = new VisualElement();
            line.style.height = 2f; line.style.backgroundColor = RingTheme.Bg; line.style.marginTop = 8f; line.style.marginBottom = 8f;
            line.pickingMode = PickingMode.Ignore;
            return line;
        }

        public static VisualElement Bullet(string text)
        {
            var r = ArenaKit.Row();
            r.style.marginTop = 4f;
            r.style.alignItems = Align.FlexStart;
            var sq = Marker(RingTheme.Gold);
            sq.style.width = 11f; sq.style.height = 11f; sq.style.marginTop = 4f; sq.style.marginRight = 8f;
            r.Add(sq);
            var l = MenuKit.Body(text, BrandTokens.TypeMinInfo, RingTheme.Bg, true);
            l.style.flexShrink = 1f;
            r.Add(l);
            return r;
        }

        /// <summary>
        /// Червона наліпка «У формі · сильніший на N» — лише коли гравець справді виріс (плейтест 08.11.2026, P2-4, 8c3f16b:
        /// попередження вже на першому тижні новачок читав як «щось не так»). До того — нейтральний рядок <see cref="RivalStepsText"/>.
        /// </summary>
        public static bool RivalInForm(CareerSession c) { return c.PlayerGrowth() > 0; }

        /// <summary>Нейтрально: «Суперник під твоє місце в картці: +N до сили» або «Сильний суперник: +N» (ворожнеча, титул).</summary>
        public static string RivalStepsText(RingText t, ShowCardView card)
        {
            return t.Format(card.IsFeud || card.IsTitle ? "c.card.rival_strong" : "c.card.rival_place", card.RivalSteps);
        }
    }

    /// <summary>Ряд дій унизу праворуч, збирається справа наліво (золота плита, далі плитки); вузька панель (4:3) — вужчі плити.</summary>
    internal sealed class ShowBottomRow
    {
        private readonly List<VisualElement> _els = new List<VisualElement>();
        private readonly List<float> _wide = new List<float>();
        private readonly List<float> _narrow = new List<float>();

        public void Add(VisualElement parent, VisualElement el, float wide, float narrow)
        {
            ArenaKit.Place(el, null, null, ArenaKit.Edge, ArenaKit.RowBottom, wide, ArenaKit.TileH);
            el.style.marginRight = 0f;   // ActionTile має власний відступ ряду — тут позиція абсолютна (рев'ю 08.11.2026)
            parent.Add(el);
            _els.Add(el); _wide.Add(wide); _narrow.Add(narrow);
        }

        public void Fit(float panelWidth)
        {
            if (float.IsNaN(panelWidth) || panelWidth <= 0f) return;
            bool narrow = panelWidth < ArenaKit.NarrowWidth;
            float right = ArenaKit.Edge;
            for (int i = 0; i < _els.Count; i++)
            {
                float w = narrow ? _narrow[i] : _wide[i];
                _els[i].style.right = right;
                _els[i].style.width = w;
                right += w + CareerTileLayout.ShowRowGap;
            }
        }
    }

    /// <summary>Трикутник зміни шкали (▲ / ▼) — малюється, а не шрифтом (display не має цих знаків).</summary>
    internal sealed class DeltaTriangle : VisualElement
    {
        private readonly bool _up;
        private readonly Color _color;

        public DeltaTriangle(bool up, Color color)
        {
            _up = up; _color = color;
            style.width = 18f; style.height = 16f; style.marginLeft = 10f; style.flexShrink = 0f;
            pickingMode = PickingMode.Ignore;
            generateVisualContent = Draw;
        }

        private void Draw(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            if (_up) MenuShapes.Polygon(p, new[] { new Vector2(9f, 1f), new Vector2(18f, 15f), new Vector2(0f, 15f) }, _color);
            else MenuShapes.Polygon(p, new[] { new Vector2(0f, 1f), new Vector2(18f, 1f), new Vector2(9f, 15f) }, _color);
        }
    }

    // ============================ вечір: картка ============================

    /// <summary>Картка вечора перед виходом (B03 §8): афіша суперника, золота плита «Вийти на ринг», «Зійти з вечора» — плитка поруч.</summary>
    public sealed class ShowCardScreenView : CareerScreenView
    {
        private readonly Action _start;
        private ShowBottomRow _bar = new ShowBottomRow();

        public ShowCardScreenView(RingUiContext ctx, Action start) : base(ctx)
        {
            _start = start;
            Root.RegisterCallback<GeometryChangedEvent>(e => _bar.Fit(Root.layout.width));
        }

        public override void Refresh()
        {
            Root.Clear();
            Root.style.paddingLeft = 0f; Root.style.paddingRight = 0f; Root.style.paddingTop = 0f; Root.style.paddingBottom = 0f;
            _bar = new ShowBottomRow();
            var bar = _bar;
            var c = C.GetShowCard();
            var v = C.GetCareerView();
            Root.Add(ShowKit.PlayerStrip(C, T, v));
            Root.Add(ShowKit.Resources(T, v, CareerScreenLines.PopularityMax(C)));
            Root.Add(ShowKit.RivalStrip(C, T, c.Opponent, c.OpponentFeudLevel, true));
            Root.Add(Poster(c));

            var ropes = new RopeLine();
            ArenaKit.Place(ropes, ArenaKit.Edge, null, ArenaKit.Edge, ArenaKit.RopesBottom, null, 22f);
            Root.Add(ropes);

            // коротке слово на плиті (повна фраза «Велике шоу: ворожнеча з Молотом» не влазила); шоу названо на афіші
            var gold = new GoldPlate(T.Get(c.IsFeud ? "c.hub.card.go_feud" : "c.card.go"), ArenaKit.ConfirmHint(T), OnConfirm, true);
            bar.Add(Root, gold, ArenaKit.GoldW, ArenaKit.GoldWNarrow);
            if (c.CanWithdraw)
            {
                var wd = new ActionTile(0, T.Get("c.card.withdraw"), "", null, true);
                wd.Accent = RingTheme.Signal;
                wd.PadRankValue = 6;     // фокус без миші стає на «Вийти на ринг», не на відмову
                wd.OnClick = () => { C.WithdrawFromShow(); Host.Save(); };
                bar.Add(Root, wd, CareerTileLayout.WithdrawTileW, CareerTileLayout.WithdrawTileWNarrow);
            }
            _bar.Fit(Root.layout.width);
        }

        private VisualElement Poster(ShowCardView c)
        {
            var box = ArenaKit.Abs(null, ArenaKit.SecondY, ArenaKit.Edge, null, ArenaKit.StripW, null);
            box.style.overflow = Overflow.Visible;
            box.Add(new PaperSheet { Torn = true });
            var col = new VisualElement();
            col.pickingMode = PickingMode.Ignore;
            col.style.paddingLeft = 24f; col.style.paddingRight = 22f; col.style.paddingTop = 14f; col.style.paddingBottom = 22f;
            var head = ArenaKit.Row();
            head.style.alignItems = Align.FlexStart;
            var left = new VisualElement();
            left.pickingMode = PickingMode.Ignore;
            left.style.flexShrink = 1f;
            left.Add(MenuKit.Display(CareerScreenLines.CardHeading(T, c), 25f, RingTheme.Bg, RingFontRole.Display800, 0.02f));
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
            col.Add(ShowKit.Rule());
            if (c.Opponent != null)
            {
                var vs = MenuKit.Body(T.Format("c.card.vs", T.Get(c.Opponent.NameKey)), 20f, RingTheme.Bg, true);
                vs.style.marginBottom = 4f;
                col.Add(vs);
            }
            col.Add(MenuKit.Body(T.Format("c.card.fee", c.Fee, c.WinBonus, c.HypeBonus), BrandTokens.TypeMinInfo, RingTheme.Bg, true));
            // прощальний матч чемпіона: віддай пояс — і вища федерація зустріне краще (ПМ 12.11.2026)
            string farewell = CareerScreenLines.FarewellNote(T, C, c);
            if (farewell != null) col.Add(ShowKit.Bullet(farewell));
            if (!c.IsFeud) col.Add(ShowKit.Bullet(T.Get("c.card.plan") + ": " + PlanText(c.Plan)));
            col.Add(ShowKit.Bullet(T.Get("c.card.stip") + ": " + StipText(c.Stipulations)));
            if (c.PromoHypeBonus != 0) col.Add(ShowKit.Bullet(T.Format("c.card.promo_bonus", Signed(c.PromoHypeBonus))));
            if (c.IsRematch) col.Add(ShowKit.Bullet(T.Get("c.hub.rematch")));
            string outgrown = CareerScreenLines.OutgrownLine(T, c);   // №7: переріс щабель — букер ставить легший матч
            if (outgrown != null) col.Add(ShowKit.Bullet(outgrown));
            // P2-4 (8c3f16b): до росту гравця — нейтральний рядок «за місцем у картці», без червоної наліпки
            if (c.Opponent != null && c.RivalSteps > 0 && !ShowKit.RivalInForm(C)) col.Add(ShowKit.Bullet(ShowKit.RivalStepsText(T, c)));
            var inj = MenuKit.Body(InjuryText(c.Injuries), BrandTokens.TypeMinInfo, RingTheme.Ash);
            inj.style.marginTop = 8f;
            col.Add(inj);
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

        public override void OnConfirm() { if (_start != null) _start(); }
    }

    // ============================ вечір: підсумок ============================

    /// <summary>Підсумок вечора (B03 §6): стрічки двох борців, табличка кінцівки з ажіотажем, слово результату, квитанція, розвиток, «Далі».</summary>
    public sealed class ShowResultScreenView : CareerScreenView
    {
        private bool _dev = ExpandForShots;
        private ShowBottomRow _bar = new ShowBottomRow();

        private VisualElement _end, _hype, _devBox, _devCol;

        public ShowResultScreenView(RingUiContext ctx) : base(ctx)
        {
            Root.RegisterCallback<GeometryChangedEvent>(e => Fit());
        }

        /// <summary>Esc згортає «Розвиток»; інакше — пауза.</summary>
        public override bool OnBack()
        {
            if (!_dev) return false;
            _dev = false;
            Refresh();
            return true;
        }

        /// <summary>
        /// Вузька панель (4:3): між стрічками лише ~220 одиниць — табличка кінцівки й ажіотаж переходять у праву колонку під
        /// стрічку суперника, плита розвитку — нижче (рев'ю 08.11.2026: на 1200 перекривали обидві стрічки).
        /// </summary>
        private void Fit()
        {
            float width = Root.layout.width;
            _bar.Fit(width);
            if (float.IsNaN(width) || width <= 0f) return;
            bool narrow = width < ArenaKit.NarrowWidth;
            foreach (var e in new[] { _end, _hype })
            {
                if (e == null) continue;
                if (narrow) { e.style.left = StyleKeyword.Auto; e.style.right = ArenaKit.Edge; e.style.marginLeft = 0f; e.style.width = ArenaKit.StripW; }
                else { e.style.right = StyleKeyword.Auto; e.style.left = Length.Percent(50f); e.style.marginLeft = -175f; e.style.width = 350f; }
            }
            if (_end != null) _end.style.top = narrow ? ArenaKit.SecondY : ArenaKit.Top;
            if (_hype != null) _hype.style.top = narrow ? ArenaKit.SecondY + 80f : ArenaKit.Top + 80f;
            if (_devBox != null)
            {
                float top = narrow && _end != null ? CareerTileLayout.ResultDevTopNarrow : ArenaKit.SecondY;
                _devBox.style.top = top;
                // плита — до канатів (відлік знизу, тож від висоти панелі): на 4:3 повна плита лягала на канати (кадр слоту 30)
                if (_devCol != null) _devCol.style.maxHeight = CareerTileLayout.DevPlateMaxH(top, Root.layout.height);
            }
        }

        public override void Refresh()
        {
            RememberDevScroll();
            Root.Clear();
            Root.style.paddingLeft = 0f; Root.style.paddingRight = 0f; Root.style.paddingTop = 0f; Root.style.paddingBottom = 0f;
            _bar = new ShowBottomRow();
            _end = null; _hype = null; _devBox = null; _devCol = null;
            var r = C.GetShowResult();
            var gold = new GoldPlate(T.Get("ui.next"), ArenaKit.ConfirmHint(T), OnConfirm, true);
            if (r == null)
            {
                _bar.Add(Root, gold, ArenaKit.GoldW, ArenaKit.GoldWNarrow);
                _bar.Fit(Root.layout.width);
                return;
            }
            var v = C.GetCareerView();
            Root.Add(ShowKit.PlayerStrip(C, T, v));
            if (!r.Withdrew)
            {
                _end = EndPlate(r);
                _hype = HypeInset(r);
                Root.Add(_end);
                Root.Add(_hype);
                var npc = C.Content.FindNpc(r.OpponentId);
                Root.Add(ShowKit.RivalStrip(C, T, npc, 0, false));
            }
            Root.Add(WordPlate(r));
            Root.Add(Receipt(r, v));
            if (C.DevPoints > 0) { _devBox = DevPlate(); Root.Add(_devBox); }

            var ropes = new RopeLine();
            ArenaKit.Place(ropes, ArenaKit.Edge, null, ArenaKit.Edge, ArenaKit.RopesBottom, null, 22f);
            Root.Add(ropes);
            _bar.Add(Root, gold, ArenaKit.GoldW, ArenaKit.GoldWNarrow);
            Fit();
        }

        /// <summary>Центр угорі: кінцівка матчу і раунди на паперовій табличці.</summary>
        private VisualElement EndPlate(ShowResult r)
        {
            var box = new VisualElement();
            box.style.position = Position.Absolute;
            box.style.left = Length.Percent(50f); box.style.marginLeft = -175f;
            box.style.top = ArenaKit.Top; box.style.width = 350f; box.style.height = 70f;
            box.style.overflow = Overflow.Visible;
            box.pickingMode = PickingMode.Ignore;
            box.Add(new PaperSheet { Torn = false, Chamfer = false });
            var row = ArenaKit.Row();
            ArenaKit.Place(row, 26f, 4f, 20f, 4f);
            var kind = MenuKit.Display(RingLabels.EndKind(T, r.EndKind), 22f, RingTheme.Bg, RingFontRole.Display800, 0.04f);
            kind.style.whiteSpace = WhiteSpace.Normal;
            kind.style.flexShrink = 1f;
            row.Add(kind);
            row.Add(ArenaKit.Spacer());
            var num = ArenaKit.Big(r.Rounds.ToString(), 58f, RingTheme.Bg, true);
            row.Add(num);
            var rl = MenuKit.Display(T.Get("c.hub.rounds"), BrandTokens.TypeLabel, RingTheme.Bg, RingFontRole.Display800, 0.1f);
            rl.style.marginLeft = 8f; rl.style.marginTop = 18f;
            row.Add(rl);
            box.Add(row);
            return box;
        }

        /// <summary>Під табличкою — темна вставка з ажіотажем матчу (10 сегментів).</summary>
        private VisualElement HypeInset(ShowResult r)
        {
            var box = new VisualElement();
            box.style.position = Position.Absolute;
            box.style.left = Length.Percent(50f); box.style.marginLeft = -175f;
            box.style.top = ArenaKit.Top + 80f; box.style.width = 350f; box.style.height = 40f;
            box.style.overflow = Overflow.Visible;
            box.pickingMode = PickingMode.Ignore;
            box.Add(new ArenaPlate { Fill = RingTheme.WithAlpha(RingTheme.Bg, BrandTokens.AlphaUnderlay), Shadow = false, SlantBoth = true });
            var row = ArenaKit.Row();
            ArenaKit.Place(row, 30f, 0f, 30f, 0f);
            row.style.justifyContent = Justify.Center;
            row.Add(ArenaKit.Tag(T.Get("c.hub.hype"), RingTheme.TextDim));
            int hype = Math.Max(0, Math.Min(10, r.Hype));
            var seg = ArenaKit.Segments(10, hype, RingTheme.Hype, 150f, 14f);
            seg.style.marginLeft = 14f;
            row.Add(seg);
            var val = MenuKit.Display(r.Hype + "/10", BrandTokens.TypeLabel, RingTheme.Text, RingFontRole.Display900, 0.02f);
            val.style.marginLeft = 10f;
            row.Add(val);
            box.Add(row);
            return box;
        }

        /// <summary>Слово вечора під стрічкою гравця: золота плита з кармінною жорсткою тінню / темна «ПОРАЗКА» / піднята «НІЧИЯ».</summary>
        private VisualElement WordPlate(ShowResult r)
        {
            var box = ArenaKit.Abs(ArenaKit.Edge, ArenaKit.SecondY, null, null, ArenaKit.StripW, WordH);
            box.style.overflow = Overflow.Visible;
            string text; Color fill, ink; bool win = false;
            float size = 64f;
            if (r.Withdrew) { text = T.Get("show.withdrew"); fill = RingTheme.PanelRaised; ink = RingTheme.Text; size = 36f; }
            else if (r.Winner == MatchWinner.Player) { text = T.Get("show.won"); fill = RingTheme.Gold; ink = RingTheme.Bg; win = true; }
            else if (r.Winner == MatchWinner.Rival) { text = T.Get("show.lost"); fill = RingTheme.DefeatPlate; ink = RingTheme.Defeat; }
            else { text = T.Get("show.draw"); fill = RingTheme.PanelRaised; ink = RingTheme.Text; }
            if (win)
            {
                var hard = new ArenaPlate { Fill = RingTheme.Crimson, Shadow = false };
                hard.style.left = 9f; hard.style.top = 9f; hard.style.right = -9f; hard.style.bottom = -9f;
                box.Add(hard);
            }
            box.Add(new ArenaPlate { Fill = fill, Shadow = !win, Mirror = true });
            if (!r.Withdrew && r.InjuredZone >= 0)
            {
                // наліпка травми висить на нижньому краї слова (не в квитанції: та впиралась у канати — рев'ю 08.11.2026)
                var st = new Sticker(T.Format("c.result.injury", T.Get("bodyzone." + r.InjuredZone), r.InjuryWeeks));
                st.style.position = Position.Absolute;
                st.style.right = 30f; st.style.bottom = -22f;
                box.Add(st);
            }
            var word = ArenaKit.Big(text, size, ink);
            word.style.whiteSpace = WhiteSpace.Normal;
            ArenaKit.Place(word, 28f, null, 40f, null);
            word.style.top = size > 40f ? 12f : 26f;
            box.Add(word);
            return box;
        }

        /// <summary>Висота плити слова вечора: нижча за стрічку, щоб квитанція не впиралась у канати (кадр 08.11.2026).</summary>
        private const float WordH = 92f;

        private VisualElement Receipt(ShowResult r, CareerView v)
        {
            bool beltLine = r.TitleWon && !r.Withdrew;
            float top = ArenaKit.SecondY + WordH + 16f + (!r.Withdrew && r.InjuredZone >= 0 ? 20f : 0f);
            if (beltLine)
            {
                var belt = ArenaKit.Abs(ArenaKit.Edge, top, null, null, ArenaKit.StripW, 38f);
                belt.style.overflow = Overflow.Visible;
                belt.Add(new ArenaPlate { Fill = RingTheme.WithAlpha(RingTheme.Bg, BrandTokens.AlphaUnderlay), Stripe = RingTheme.Gold, StripeW = 6f, Shadow = false });
                var bl = ArenaKit.Big(T.Get("show.belt_won"), 25f, RingTheme.Gold);
                ArenaKit.Place(bl, 24f, 4f, null, null);
                belt.Add(bl);
                Root.Add(belt);
                top += 50f;
            }
            var box = ArenaKit.Abs(ArenaKit.Edge, top, null, null, ArenaKit.StripW, null);
            box.style.overflow = Overflow.Visible;
            box.Add(new PaperSheet { Torn = true });
            var col = new VisualElement();
            col.pickingMode = PickingMode.Ignore;
            col.style.paddingLeft = 26f; col.style.paddingRight = 24f; col.style.paddingTop = 10f; col.style.paddingBottom = 18f;

            var head = ArenaKit.Row();
            head.Add(MenuKit.Display(T.Get("c.hub.recap"), 24f, RingTheme.Bg, RingFontRole.Display800, 0.03f));
            head.Add(ArenaKit.Spacer());
            head.Add(MenuKit.Display(T.Get("c.hub.week") + " " + v.Week, BrandTokens.TypeLabel, RingTheme.Ash, RingFontRole.Display700, 0.1f));
            col.Add(head);

            // Стовп «програти й стати зіркою»: зростання популярності після поразки — окремий поп.
            bool lostLoved = !r.Withdrew && r.Winner != MatchWinner.Player && r.PopularityAfter > r.PopularityBefore;
            if (lostLoved)
            {
                var pop = new VisualElement();
                pop.style.backgroundColor = RingTheme.Bg;
                pop.style.paddingLeft = 14f; pop.style.paddingRight = 14f; pop.style.paddingTop = 6f; pop.style.paddingBottom = 7f;
                pop.style.marginTop = 8f;
                pop.pickingMode = PickingMode.Ignore;
                // речення й числа — окремими написами: «3 → 5» переноситься цілим, а не «3» в одному рядку й «→ 5» в іншому
                // (кадр 1080, 08.11.2026; нерозривного пробілу у Fixel немає)
                pop.style.flexDirection = FlexDirection.Row;
                pop.style.flexWrap = Wrap.Wrap;
                var pl = MenuKit.Body(T.Get("c.result.lost_but_loved"), 17f, RingTheme.Gold, true);
                pl.style.whiteSpace = WhiteSpace.Normal;
                pl.style.marginRight = 5f;
                pl.style.flexShrink = 1f;
                pop.Add(pl);
                var pn = MenuKit.Body(T.Format("c.result.lost_but_loved.pop", r.PopularityBefore, r.PopularityAfter), 17f, RingTheme.Gold, true);
                pn.style.whiteSpace = WhiteSpace.NoWrap;
                pop.Add(pn);
                col.Add(pop);
            }

            col.Add(ShowKit.Rule());
            if (!r.Withdrew)
            {
                var feeRow = ArenaKit.Row();
                feeRow.Add(ShowKit.Marker(RingTheme.Gold));
                var fl = new VisualElement();
                fl.pickingMode = PickingMode.Ignore;
                fl.style.flexShrink = 1f;
                fl.Add(ArenaKit.Tag(T.Get("c.hub.fee"), RingTheme.Ash));
                // №8: з менеджеркою — ставка до її частки і частка окремо (сума = число праворуч)
                var split = MenuKit.Body(CareerScreenLines.FeeSplitLine(T, r), BrandTokens.TypeMinInfo, RingTheme.Bg, true);
                split.style.whiteSpace = WhiteSpace.Normal;
                fl.Add(split);
                feeRow.Add(fl);
                feeRow.Add(ArenaKit.Spacer());
                feeRow.Add(ArenaKit.Money(r.TotalFee, 36f, RingTheme.Bg));
                col.Add(feeRow);
                col.Add(ShowKit.Rule());
            }

            col.Add(ScaleRow(T.Get("c.hub.pop"), r.PopularityBefore, r.PopularityAfter, !lostLoved));
            // поразка не дала популярності — чому (ядро «Кар'єри», P1-6, 11.11.2026: ажіотаж нижче порога чи вже цього сезону)
            string lossPop = CareerScreenLines.LossPopularity(T, r);
            if (lossPop != null) col.Add(ShowKit.Bullet(lossPop));
            // довіра — лише якщо змінилась («1 → 1» — шум; плейтест 08.11.2026, P3, 8c3f16b)
            if (r.TrustAfter != r.TrustBefore) col.Add(ScaleRow(T.Get("c.hub.trust"), r.TrustBefore, r.TrustAfter, true));
            if (!r.Withdrew && r.DevPointsGained != 0)
            {
                var dr = ArenaKit.Row();
                dr.style.marginTop = 6f;
                dr.Add(ShowKit.Marker(RingTheme.Gold));
                var dl = new VisualElement();
                dl.pickingMode = PickingMode.Ignore;
                dl.style.flexShrink = 1f;
                dl.Add(ArenaKit.Tag(T.Get("c.hub.dev_gain"), RingTheme.Ash));
                dl.Add(MenuKit.Body(T.Format("c.hub.dev_total", C.DevPoints), BrandTokens.TypeMinInfo, RingTheme.Bg, true));
                dr.Add(dl);
                dr.Add(ArenaKit.Spacer());
                dr.Add(ArenaKit.Big(Signed(r.DevPointsGained), 32f, RingTheme.Bg));
                col.Add(dr);
            }
            if (!r.Withdrew && !r.IsTitle && !r.IsFeud)
            {
                var pr = ArenaKit.Row();
                pr.style.marginTop = 6f;
                pr.Add(ShowKit.Marker(RingTheme.Gold));
                pr.Add(ArenaKit.Tag(T.Get("c.card.plan"), RingTheme.Ash));
                pr.Add(ArenaKit.Spacer());
                var po = MenuKit.Body(RingLabels.PlanOutcome(T, r.PlanOutcome), BrandTokens.TypeMinInfo, RingTheme.Bg, true);
                po.style.whiteSpace = WhiteSpace.Normal;
                po.style.flexShrink = 1f;
                pr.Add(po);
                col.Add(pr);
            }
            box.Add(col);
            return box;
        }

        /// <summary>Рядок шкали «до → після» з трикутником зміни; <paramref name="plain"/> = false — коли поп поразки вже показав зростання.</summary>
        private VisualElement ScaleRow(string label, int before, int after, bool plain)
        {
            var row = ArenaKit.Row();
            row.style.marginTop = 3f;
            row.Add(ShowKit.Marker(after < before ? RingTheme.Signal : RingTheme.Gold));
            row.Add(ArenaKit.Tag(label, RingTheme.Ash));
            row.Add(ArenaKit.Spacer());
            row.Add(ArenaKit.Big(before.ToString(), 27f, RingTheme.Bg, true));
            var arrow = MenuKit.Body("→", 26f, RingTheme.Bg, true);
            arrow.style.marginLeft = 6f; arrow.style.marginRight = 6f;
            row.Add(arrow);
            row.Add(ArenaKit.Big(after.ToString(), 27f, RingTheme.Bg, true));
            if (after > before) row.Add(new DeltaTriangle(true, RingTheme.Bg));
            else if (after < before) row.Add(new DeltaTriangle(false, RingTheme.Crimson));
            else { var gap = new VisualElement(); gap.style.width = 28f; gap.pickingMode = PickingMode.Ignore; row.Add(gap); }
            return row;
        }

        /// <summary>Права колонка: «РОЗВИТОК · ОЧКІВ N» — шапка розгортає й згортає акордеон з порадами (DevelopmentPanel).</summary>
        private VisualElement DevPlate()
        {
            var box = ArenaKit.Abs(null, ArenaKit.SecondY, ArenaKit.Edge, null, ArenaKit.StripW, null);
            box.style.overflow = Overflow.Visible;
            box.pickingMode = PickingMode.Position;
            // щільна (AlphaPlate) і з фаскою лише в куті: крізь 0,86 і поза скосом на всю висоту «ЗГОРНУТИ» тонуло в сорочці рефері (огляд
            // слоту 30, 4:3)
            box.Add(new ArenaPlate { Fill = RingTheme.WithAlpha(RingTheme.Panel, BrandTokens.AlphaPlate), Stripe = RingTheme.Gold, Mirror = true,
                SlantCapH = CareerTileLayout.PlateSlantCapH });
            var col = new VisualElement();
            col.style.paddingLeft = CareerTileLayout.ResultDevPadLeft; col.style.paddingRight = CareerTileLayout.ResultDevPadRight;
            col.style.paddingTop = CareerTileLayout.ResultDevPadTop; col.style.paddingBottom = CareerTileLayout.ResultDevPadBottom;
            col.style.maxHeight = CareerTileLayout.DevPlateMaxH(ArenaKit.SecondY, Root.layout.height);   // Fit уточнює за верхом плити
            _devCol = col;
            var head = ArenaKit.Row();
            head.style.flexShrink = 0f;
            head.pickingMode = PickingMode.Position;
            var titleCol = new VisualElement();
            titleCol.pickingMode = PickingMode.Ignore;
            var title = MenuKit.Display(T.Get("dev.title"), CareerTileLayout.ResultDevTitleSize, RingTheme.Text, RingFontRole.Display900, 0.02f);
            titleCol.Add(title);
            titleCol.Add(ArenaKit.Tag(T.Get(_dev ? "c.hub.collapse" : "c.hub.expand"), RingTheme.TextDim));
            head.Add(titleCol);
            head.Add(ArenaKit.Spacer());
            var pl = ArenaKit.Tag(T.Get("c.hub.points"), RingTheme.TextDim);
            pl.style.marginRight = 10f; pl.style.marginTop = 10f;
            head.Add(pl);
            head.Add(ArenaKit.Big(C.DevPoints.ToString(), CareerTileLayout.ResultDevPointsSize, RingTheme.Gold));
            head.RegisterCallback<PointerDownEvent>(e => { if (e.button == 0) { _dev = !_dev; Refresh(); } });
            PadMark.Mark(head, () => { _dev = !_dev; Refresh(); }, "devhead", 4);   // розгорнути «Розвиток» без миші
            col.Add(head);
            if (_dev)
            {
                var scroll = new ScrollView(ScrollViewMode.Vertical);
                scroll.style.maxHeight = 380f;
                scroll.style.flexShrink = 1f; scroll.style.minHeight = CareerTileLayout.DevScrollMinH;   // до канатів стискається лише прокрутка
                ArenaKit.ThinScroller(scroll);   // смуга в стилі «Арена» (кадри слоту 27)
                // «Усе (N)» і рядок-підказка (лише розгорнутий список) — у підвалі під прокруткою (кадри й рев'ю слоту 30), на вертикалі порад
                var foot = DevFooter(CareerTileLayout.DevCardPad, CareerTileLayout.DevCardPad, 0f);   // низ — відступ колонки (12)
                var content = DevelopmentPanel(Refresh, 2, headerShowsPoints: true, moreTo: foot);   // «ОЧКІВ N» уже в шапці
                content.style.backgroundColor = Color.clear;
                content.style.borderTopWidth = 0f; content.style.borderBottomWidth = 0f; content.style.borderLeftWidth = 0f; content.style.borderRightWidth = 0f;
                scroll.Add(content);
                col.Add(scroll);
                DevFooterHint(foot);
                col.Add(foot);
                KeepDevScroll(scroll, C.GetShowResult());   // щойно розгорнутий «Усе» — до початку повного списку; інакше — де була
            }
            box.Add(col);
            return box;
        }

        public override void OnConfirm()
        {
            C.ContinueFromShow();
            Host.Save();
        }
    }

    // ============================ кінець сезону ============================

    /// <summary>Підсумок сезону (B03 §8): стрічка гравця й ресурси, паперовий звіт, розвиток на темній плиті, золота плита «Титульний матч» / «Новий сезон».</summary>
    public sealed class SeasonEndScreenView : CareerScreenView
    {
        private ShowBottomRow _bar = new ShowBottomRow();
        private SeasonOfferModel _offer;
        private VisualElement _devBox;

        public SeasonEndScreenView(RingUiContext ctx) : base(ctx)
        {
            Root.RegisterCallback<GeometryChangedEvent>(e =>
            {
                _bar.Fit(Root.layout.width);
                // канати — відлік знизу: розмір інтерфейсу міняє висоту панелі, а з нею — місце плити розвитку
                if (_devBox != null) _devBox.style.maxHeight = CareerTileLayout.DevPlateMaxH(CareerTileLayout.SeasonDevTop, Root.layout.height);
            });
        }

        public override void Refresh()
        {
            RememberDevScroll();   // покупка в «Усе (N)» перебудовує екран — прокрутка плити розвитку лишається на місці (огляд слоту 31)
            Root.Clear();
            Root.style.paddingLeft = 0f; Root.style.paddingRight = 0f; Root.style.paddingTop = 0f; Root.style.paddingBottom = 0f;
            _bar = new ShowBottomRow();
            _offer = new SeasonOfferModel(C, T);
            var rep = C.GetSeasonReport();
            var v = C.GetCareerView();
            Root.Add(ShowKit.PlayerStrip(C, T, v));
            Root.Add(ShowKit.Resources(T, v, CareerScreenLines.PopularityMax(C)));
            Root.Add(Report(rep));
            Root.Add(DevPlate());

            var ropes = new RopeLine();
            ArenaKit.Place(ropes, ArenaKit.Edge, null, ArenaKit.Edge, ArenaKit.RopesBottom, null, 22f);
            Root.Add(ropes);

            // Нижній ряд — одна послідовність рішень (CareerScreenLines.SeasonEndActions): титульний шанс → пропозиція вищої
            // федерації («Прийняти контракт» / «Лишитися», ПМ 12.11.2026: без них гравець застрягав на місцевій) → новий сезон.
            // Перша дія — золота плита (Enter), решта — плитки ліворуч.
            var actions = CareerScreenLines.SeasonEndActions(C);
            for (int i = 0; i < actions.Count; i++) AddAction(actions[i], i == 0);
            _bar.Fit(Root.layout.width);
        }

        private void AddAction(SeasonEndAction a, bool first)
        {
            switch (a)
            {
                case SeasonEndAction.TitleMatch: Gold(T.Get("c.season.title_go"), first); break;
                case SeasonEndAction.AcceptOffer: Gold(_offer.AcceptText, first); break;
                case SeasonEndAction.NextSeason: Gold(T.Get("season.next"), first); break;
                case SeasonEndAction.SkipTitle:
                    Tile(a, T.Get("c.season.title_skip"), CareerScreenLines.SkipTitleEffect(T), CareerScreenLines.SkipTitleRisk(T, C)); break;
                case SeasonEndAction.DeclineOffer:
                    // що буде — «повториться»; друга відмова поспіль коштує довіри — ризиком (сигнальним) під ним
                    Tile(a, _offer.DeclineText, CareerScreenLines.DeclineEffect(T), CareerScreenLines.DeclineRisk(T, C));
                    break;
                case SeasonEndAction.ReadContract:
                    // №8 (§6.4): дрібний шрифт — кидок Техніки, шанси до кидка; що видно — на папері (рядок дрібного шрифту)
                    Tile(a, _offer.ReadText, _offer.ReadChanceText, null);
                    break;
            }
        }

        private void Gold(string word, bool first)
        {
            _bar.Add(Root, new GoldPlate(word, first ? ArenaKit.ConfirmHint(T) : "", OnConfirm, true), ArenaKit.GoldW, ArenaKit.GoldWNarrow);
        }

        private void Tile(SeasonEndAction a, string title, string effect, string risk)
        {
            var t = new ActionTile(0, title, effect, risk, true);
            t.Accent = RingTheme.TextDim;
            t.PadRankValue = 6;
            t.OnClick = () => Run(a);
            _bar.Add(Root, t, CareerTileLayout.TitleSkipTileW, CareerTileLayout.TitleSkipTileWNarrow);
        }

        /// <summary>
        /// Дія ряду. Титульний матч і прощальний матч чемпіона (AcceptOffer чемпіона) переводять кар'єру в шоу — екран міняє хост за
        /// стадією; решта лишає кінець сезону — перемалювати тут. Після переходу не-чемпіона стадія та сама (SeasonEnd), а
        /// GetSeasonReport на шоу кинув би виняток — тому Refresh лише на кінці сезону.
        /// </summary>
        private void Run(SeasonEndAction a)
        {
            switch (a)
            {
                case SeasonEndAction.TitleMatch: C.StartTitleMatch(); break;
                case SeasonEndAction.SkipTitle: C.SkipTitle(); break;
                case SeasonEndAction.AcceptOffer: C.AcceptOffer(); break;
                case SeasonEndAction.DeclineOffer: C.DeclineOffer(); break;
                case SeasonEndAction.NextSeason: C.StartNextSeason(); break;
                case SeasonEndAction.ReadContract: _offer.Read(); break;
            }
            Host.Save();
            if (a != SeasonEndAction.NextSeason && C.Stage == CareerStage.SeasonEnd) Refresh();
        }

        private VisualElement Report(SeasonReport rep)
        {
            var box = ArenaKit.Abs(null, ArenaKit.Top, ArenaKit.Edge, null, 520f, null);
            box.style.overflow = Overflow.Visible;
            box.Add(new PaperSheet { Torn = true });
            var col = new VisualElement();
            col.pickingMode = PickingMode.Ignore;
            col.style.paddingLeft = 30f; col.style.paddingRight = 26f; col.style.paddingTop = 16f; col.style.paddingBottom = 24f;
            var title = MenuKit.Display(T.Format("c.season.title", rep.Season), 32f, RingTheme.Bg, RingFontRole.Display900, 0.02f);
            title.style.whiteSpace = WhiteSpace.Normal;
            col.Add(title);
            // федерація й рівень: «Залізна лава · рівень 1 з 4» (П5, ПМ 12.11.2026)
            var fed = MenuKit.Body(_offer.FederationText, BrandTokens.TypeMinInfo, RingTheme.Bg, true);
            fed.style.whiteSpace = WhiteSpace.Normal;
            fed.style.marginTop = 2f;
            col.Add(fed);
            col.Add(ShowKit.Rule());
            // Поки титульний шанс відкритий — лише він (3-P2-6, 11.11.2026): шкали ще зміняться після матчу чи відмови, а
            // підсумок показувався б двічі — до і після.
            if (!CareerScreenLines.SeasonSummaryShown(C))
            {
                col.Add(ShowKit.Bullet(T.Get(rep.IsDefense ? "season.defense" : "season.title_shot")));
                box.Add(col);
                return box;
            }
            // Підсумок сезону (плейтест 08.11.2026, P1-7, 8c3f16b): рекорд, популярність «було → стало», ворожнеча з Молотом,
            // умова титулу.
            var rec = C.State.SeasonRecord;
            var record = MenuKit.Body(T.Format("c.season.record", rec[0], rec[1], rec[2]), 19f, RingTheme.Bg, true);
            record.style.whiteSpace = WhiteSpace.Normal;
            col.Add(record);
            if (C.State.SeasonPopStart >= 0)
                col.Add(ShowKit.Bullet(T.Format("c.season.pop", C.State.SeasonPopStart, rep.Popularity)));
            var line = MenuKit.Body(T.Format("c.season.line", rep.Popularity, rep.Trust, rep.Money), BrandTokens.TypeMinInfo, RingTheme.Bg, true);
            line.style.whiteSpace = WhiteSpace.Normal;
            line.style.marginTop = 6f;
            col.Add(line);
            var hammer = C.Content.FindNpc(CareerContent.HammerId);
            if (hammer != null)
            {
                string hn = T.Get(hammer.NameKey);
                col.Add(ShowKit.Bullet(C.State.FeudResolved ? T.Format("c.season.feud_done", hn)
                    : T.Format("c.season.feud", hn, C.FeudLevel(CareerContent.HammerId), CareerRules.MaxFeud)));
            }
            // звільнення: на місцевій — назад на мінімум, у вищій — пониження «куди» (c.season.demoted, через модель; ПМ 12.11.2026)
            string fired = CareerScreenLines.FiredLine(T, C, _offer);
            if (fired != null)
            {
                var f = MenuKit.Body(fired, BrandTokens.TypeBody, RingTheme.Crimson, true);
                f.style.whiteSpace = WhiteSpace.Normal;
                f.style.marginTop = 10f;
                col.Add(f);
            }
            if (rep.TitleDone)
            {
                col.Add(ShowKit.Bullet(T.Get(rep.TitleWon || rep.BeltIsPlayers ? "c.season.belt_yes" : "c.season.belt_no")));
            }
            else if (rep.TitleEligible)
                col.Add(ShowKit.Bullet(T.Get(rep.IsDefense ? "season.defense" : "season.title_shot")));
            else if (!rep.Fired)
                col.Add(ShowKit.Bullet(T.Format("c.season.goal", CareerRules.TitleShotTrust, CareerRules.MaxFeud)));
            if (CareerScreenLines.OfferShown(C, _offer)) OfferBlock(col, _offer);
            if (CareerScreenLines.LateRoleShown(C, CareerScreenLines.SeasonEndActions(C))) LateRoleBlock(col);
            box.Add(col);
            return box;
        }

        /// <summary>
        /// №11 (§5.3): вибір пізньої ролі — на останньому кроці кінця сезону, перед «Новим сезоном». Кнопки ролей одним рядком (обрана —
        /// із золотою смужкою — обрана на НАСТУПНИЙ сезон), чинна — у заголовку (<see cref="CareerScreenLines.LateRoleTitle"/>), під
        /// кнопками — «З наступного сезону: …», коли обрана інша, ніж чинна (<see cref="CareerScreenLines.LateRoleNote"/>).
        /// </summary>
        private void LateRoleBlock(VisualElement col)
        {
            var m = new LateRoleModel(C, T);
            col.Add(ShowKit.Rule());
            col.Add(MenuKit.Body(CareerScreenLines.LateRoleTitle(m), BrandTokens.TypeBody, RingTheme.Bg, true));
            var chips = ArenaKit.Row();
            chips.style.flexWrap = Wrap.Wrap;
            chips.style.marginTop = 6f;
            chips.pickingMode = PickingMode.Ignore;
            foreach (var r in m.Rows)
            {
                var role = r.Role;
                var b = Small(r.Name, () => { if (m.Choose(role)) Host.Save(); Refresh(); }, true, r.Selected ? RingTheme.Gold : RingTheme.Ash, r.Description);
                RingUi.PadKey(b, "late:" + (int)role);
                b.style.marginRight = 4f;
                b.style.flexShrink = 0f;
                b.style.whiteSpace = WhiteSpace.NoWrap;
                chips.Add(b);
            }
            col.Add(chips);
            var note = MenuKit.Body(CareerScreenLines.LateRoleNote(T, m), BrandTokens.TypeMinInfo, RingTheme.Bg, true);
            note.style.whiteSpace = WhiteSpace.Normal;
            note.style.marginTop = 2f;
            col.Add(note);
        }

        /// <summary>
        /// Пропозиція вищої федерації (П5, правило А2 «два ключі»): до рішення — заголовок і два ключі з галочкою, коли виконано, і
        /// карміновим «чого бракує» під ним; «+ пояс» біля букера; після рішення ключів немає — лише рядок стану
        /// (<see cref="SeasonOfferModel.StatusText"/>: кличуть, прощальний матч, відмова, новий контракт).
        /// </summary>
        private static void OfferBlock(VisualElement col, SeasonOfferModel o)
        {
            col.Add(ShowKit.Rule());
            if (o.ShowKeys)
            {
                var head = MenuKit.Body(o.TitleText, BrandTokens.TypeBody, RingTheme.Bg, true);
                head.style.whiteSpace = WhiteSpace.Normal;
                col.Add(head);
                if (!o.IsTop)
                {
                    col.Add(KeyLine(o.AudienceText, null, o.AudienceOk, o.AudienceHint));
                    col.Add(KeyLine(o.BookerText, o.BeltMark, o.BookerOk, o.BookerHint));
                }
            }
            string status = o.StatusText;
            if (!string.IsNullOrEmpty(status))
            {
                var s = MenuKit.Body(status, BrandTokens.TypeMinInfo, RingTheme.Bg, true);
                s.style.whiteSpace = WhiteSpace.Normal;
                s.style.marginTop = 8f;
                col.Add(s);
            }
            if (!o.ShowContract) return;
            // №8 (§6.4): контракт до рішення — аванс, що видно в дрібному шрифті (насторожує — кармін), хто перевіряв, штраф пастки
            col.Add(ShowKit.Bullet(o.AdvanceText));
            col.Add(ContractLine(o.FinePrintText, o.FinePrintWarns));
            if (!string.IsNullOrEmpty(o.ManagerNote)) col.Add(ContractLine(o.ManagerNote, false));
            if (!string.IsNullOrEmpty(o.PenaltyText)) col.Add(ContractLine(o.PenaltyText, true));
        }

        /// <summary>Рядок контракту під пунктами: дрібним жирним, сигнальний — карміном (пункт пастки, штраф).</summary>
        private static VisualElement ContractLine(string text, bool warn)
        {
            var l = MenuKit.Body(text, BrandTokens.TypeMinInfo, warn ? RingTheme.Crimson : RingTheme.Bg, true);
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.marginTop = 4f;
            l.style.marginLeft = CareerTileLayout.BulletIndent;
            return l;
        }

        private static VisualElement KeyLine(string text, string mark, bool ok, string hint)
        {
            var box = new VisualElement();
            box.pickingMode = PickingMode.Ignore;
            box.style.marginTop = 6f;
            var row = ArenaKit.Row();
            row.style.alignItems = Align.Center;
            // місце галочки — завжди (рядки рівні); сама галочка — лише коли ключ виконано
            var slot = new VisualElement();
            slot.pickingMode = PickingMode.Ignore;
            slot.style.width = CareerTileLayout.CheckW; slot.style.height = CareerTileLayout.CheckH;
            slot.style.marginRight = 8f; slot.style.flexShrink = 0f;
            if (ok) slot.Add(new CheckMark { Ink = RingTheme.Bg });
            row.Add(slot);
            row.Add(MenuKit.Body(text, BrandTokens.TypeBody, RingTheme.Bg, true));
            if (!string.IsNullOrEmpty(mark))
            {
                var m = ArenaKit.Tag(mark, RingTheme.Bg);
                m.style.marginLeft = 10f;
                row.Add(m);
            }
            box.Add(row);
            if (!string.IsNullOrEmpty(hint))
            {
                var h = MenuKit.Body(hint, BrandTokens.TypeMinInfo, RingTheme.Crimson, true);
                h.style.whiteSpace = WhiteSpace.Normal;
                h.style.marginLeft = CareerTileLayout.CheckW + 8f;
                box.Add(h);
            }
            return box;
        }

        /// <summary>
        /// Плита розвитку кінця сезону: шапка «РОЗВИТОК · ОЧКІВ N», прокрутка порад, підвал «Усе (N)» (розгорнутий — з рядком-підказкою).
        /// Кадри слоту 30 (12.11.2026): прокрутка висотою 370 різала «▸ Усе (19)» навпіл, підказка повного списку лежала під ним
        /// поза видимим. Тепер плита — до канатів (<see cref="CareerTileLayout.DevPlateMaxH"/>, від висоти панелі), стискається
        /// лише прокрутка; число очок — у шапці, як на підсумку матчу: рядок «Очок розвитку: N» у картці з'їдав висоту третьої
        /// поради. Три поради з найдовшими поясненнями й підвал уміщаються — CareerTextFitTests.SeasonEnd_DevPlate_…
        /// </summary>
        private VisualElement DevPlate()
        {
            var box = ArenaKit.Abs(ArenaKit.Edge, CareerTileLayout.SeasonDevTop, null, null, ArenaKit.StripW, null);
            box.style.maxHeight = CareerTileLayout.DevPlateMaxH(CareerTileLayout.SeasonDevTop, Root.layout.height);
            box.style.flexDirection = FlexDirection.Column;
            box.style.overflow = Overflow.Visible;
            box.pickingMode = PickingMode.Position;
            box.Add(new ArenaPlate { Fill = RingTheme.WithAlpha(RingTheme.Panel, BrandTokens.AlphaPlate), Stripe = RingTheme.Gold,
                SlantCapH = CareerTileLayout.PlateSlantCapH });   // фаска лише в куті: підвал на всю ширину не виходить за заливку
            float right = CareerTileLayout.DevPadRight + CareerTileLayout.DevCardPad;   // число й підвал — на вертикалі порад картки
            var head = ArenaKit.Row();
            head.style.flexShrink = 0f;
            head.style.paddingLeft = CareerTileLayout.DevPadLeft; head.style.paddingRight = right;
            head.style.paddingTop = CareerTileLayout.DevHeadPadTop; head.style.marginBottom = CareerTileLayout.DevHeadGap;
            head.Add(MenuKit.Display(T.Get("dev.title"), CareerTileLayout.DevTitleSize, RingTheme.Text, RingFontRole.Display900, 0.02f));
            head.Add(ArenaKit.Spacer());
            var pl = ArenaKit.Tag(T.Get("c.hub.points"), RingTheme.TextDim);
            pl.style.marginRight = 8f;
            head.Add(pl);
            head.Add(ArenaKit.Big(C.DevPoints.ToString(), CareerTileLayout.DevPointsSize, RingTheme.Gold));
            box.Add(head);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            ArenaKit.ThinScroller(scroll);   // системна сіра смуга зі стрілками випадала зі стилю (кадри слоту 27)
            scroll.style.flexShrink = 1f; scroll.style.minHeight = CareerTileLayout.DevScrollMinH;
            scroll.style.paddingLeft = CareerTileLayout.DevPadLeft; scroll.style.paddingRight = CareerTileLayout.DevPadRight;
            scroll.style.paddingBottom = CareerTileLayout.DevScrollPadBottom;
            var foot = DevFooter(CareerTileLayout.DevPadLeft + CareerTileLayout.DevCardPad, right, CareerTileLayout.DevFootPadBottom);
            var content = DevelopmentPanel(Refresh, 2, headerShowsPoints: true, moreTo: foot);   // «ОЧКІВ N» — у шапці
            content.style.backgroundColor = Color.clear;
            content.style.borderTopWidth = 0f; content.style.borderBottomWidth = 0f; content.style.borderLeftWidth = 0f; content.style.borderRightWidth = 0f;
            scroll.Add(content);
            box.Add(scroll);
            DevFooterHint(foot);
            box.Add(foot);
            KeepDevScroll(scroll, C.State.Season);   // щойно розгорнутий «Усе» — до початку повного списку; інакше — де була
            _devBox = box;
            return box;
        }

        /// <summary>Enter / A — перша дія ряду (золота плита).</summary>
        public override void OnConfirm()
        {
            var actions = CareerScreenLines.SeasonEndActions(C);
            if (actions.Count > 0) Run(actions[0]);
        }
    }
}
