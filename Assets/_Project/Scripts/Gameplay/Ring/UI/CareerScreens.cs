using System;
using System.Collections.Generic;
using Game.Core.Ring;
using Game.Core.Ring.Career;
using Game.Core.Ring.Match;
using Game.Gameplay.Ring.Models;
using Game.Gameplay.Ring.Stage;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Gameplay.Ring.UI
{
    /// <summary>Що екрани кар'єри просять у хоста: сейв, вихід, вигляд героя (тіло й зачіска — лише 3D), результат останньої дії тижня.</summary>
    public interface ICareerHost
    {
        bool HasSave { get; }
        void NewGame();
        void ContinueGame();
        void Save();
        void Quit();
        void ToggleFullscreen();
        int HeroBody { get; set; }
        int HeroHair { get; set; }
        WeekActionResult LastWeekResult { get; set; }
    }

    /// <summary>Основа екранів кар'єри: прозорий корінь над 3D, доступ до кар'єри й хоста.</summary>
    public abstract class CareerScreenView : RingScreenView
    {
        protected CareerSession C { get { return Ctx.Career; } }
        protected ICareerHost Host { get { return Ctx.Host; } }
        protected RingText T { get { return Ctx.Text; } }

        /// <summary>Огляд вигляду (-ring-shots): згорнуті «Розвиток» і «Витрати» знімаються розгорнутими.</summary>
        public static bool ExpandForShots;

        protected CareerScreenView(RingUiContext ctx) : base(ctx)
        {
            if (RingStageHub.Has3D) Root.style.backgroundColor = Color.clear;
        }

        /// <summary>Екрани кар'єри B03 v2 проходяться без миші (P3 геймпад, 11.11.2026): усі дії — цілі фокусу.</summary>
        protected override bool PadTargets { get { return true; } }

        protected VisualElement Glass(float pad = 10f)
        {
            var c = RingUi.Card(pad);
            RingUi.Glass(c, BrandTokens.AlphaGlass);
            return c;
        }

        /// <summary>Рядок-підказка екрана: пояснення дії з'являється тут при наведенні (кнопки — однорядкові).</summary>
        protected Label HintLine;

        protected Label MakeHint()
        {
            HintLine = RingUi.HintLine();
            return HintLine;
        }

        /// <summary>
        /// Підвал плити розвитку (кадри слоту 30, 12.11.2026): на кінці сезону край прокрутки різав «▸ Усе (19)» навпіл, а рядок-підказка
        /// повного списку лежав у прокрутці під ним — поза видимим. Тепер обидва під прокруткою, в один рядок: перемикач кладе сюди
        /// <see cref="DevelopmentPanel"/> (moreTo), підказку — <see cref="DevFooterHint"/>. Підвал не стискається — стискається прокрутка.
        /// </summary>
        protected static VisualElement DevFooter(float padLeft, float padRight, float padBottom)
        {
            var foot = RingUi.Row();
            foot.style.flexShrink = 0f;
            foot.style.alignItems = Align.FlexStart;
            foot.style.paddingLeft = padLeft; foot.style.paddingRight = padRight; foot.style.paddingBottom = padBottom;
            return foot;
        }

        /// <summary>Рядок-підказка підвалу — праворуч від «Усе (N)», перший рядок на висоті напису кнопки, без порожньої смуги (minHeight 0).</summary>
        protected void DevFooterHint(VisualElement foot)
        {
            var hint = MakeHint();
            hint.style.minHeight = 0f;
            hint.style.marginTop = CareerTileLayout.DevFootTop;
            hint.style.paddingTop = 4f;
            hint.style.marginLeft = 12f;
            hint.style.flexGrow = 1f; hint.style.flexShrink = 1f; hint.style.minWidth = 0f;
            foot.Add(hint);
        }

        /// <summary>Компактна однорядкова кнопка; <paramref name="hint"/> — у рядок-підказку при наведенні.</summary>
        protected Button Small(string text, Action onClick, bool enabled, Color? accent, string hint = null)
        {
            var b = RingUi.MakeButton(text, onClick, enabled, accent, 13f);
            HudPad(b, 3f, 8f);
            b.style.marginBottom = 3f;
            b.style.borderLeftWidth = 3f;
            if (!string.IsNullOrEmpty(hint))
            {
                PadFocus.Hover(b, () => { if (HintLine != null) { HintLine.text = TextBreak.GlueDashes(hint); HintLine.style.color = RingUi.Text; } },
                    () => { if (HintLine != null) HintLine.text = ""; });
                // фокус без миші показує той самий опис (і причину недоступності), що й наведення (аудит P3 геймпад, 11.11.2026)
                PadMark.AddHot(b, on => { if (HintLine == null) return; HintLine.text = on ? TextBreak.GlueDashes(hint) : ""; if (on) HintLine.style.color = RingUi.Text; });
            }
            return b;
        }

        private static void HudPad(VisualElement e, float v, float h)
        {
            e.style.paddingTop = v; e.style.paddingBottom = v; e.style.paddingLeft = h; e.style.paddingRight = h;
        }

        protected static Label Section(string text) { return RingUi.Section(text); }

        protected string PlanText(BookerPlan p) { return RingLabels.PlanName(T, p); }

        protected string StipText(MatchStipulations s)
        {
            if (s == MatchStipulations.None) return T.Get("stip.none");
            var parts = new List<string>();
            if ((s & MatchStipulations.NoDq) != 0) parts.Add(T.Get("stip.no_dq"));
            if ((s & MatchStipulations.NoAlly) != 0) parts.Add(T.Get("stip.no_ally"));
            return string.Join(", ", parts);
        }

        protected string InjuryText(List<InjuryView> injuries)
        {
            var parts = new List<string>();
            foreach (var i in injuries)
            {
                if (i.WeeksLeft > 0) parts.Add(T.Format("c.injury.weeks", T.Get("bodyzone." + (int)i.Zone), i.WeeksLeft));
                else if (i.ZoneDamage > 0) parts.Add(T.Format("c.injury.dmg", T.Get("bodyzone." + (int)i.Zone), i.ZoneDamage));
            }
            return parts.Count == 0 ? T.Get("c.injury.none") : string.Join(" · ", parts);
        }

        /// <summary>Розвиток: атрибути, нові прийоми, перки — компактні кнопки, описи в рядку-підказці.</summary>
        private bool _devAll;

        /// <summary>
        /// Розвиток: спершу до трьох рекомендацій, кожна з поясненням одним рядком (огляд фану 06.11.2026, R6/F7: ~16 кнопок без
        /// пояснень, «Спритність −1» на кнопці), далі «Усе (N)» — повний список. Атрибут — «було → стане».
        /// Рекомендації — від ядра «Кар'єри» (<c>DevelopmentAdvice.For</c>, P2-2, 11.11.2026): від ролі, ґіміка й того, чого бракувало
        /// в минулому матчі; очок багато — на одну більше. Рядки — <see cref="CareerScreenLines.DevRecs"/>.
        /// <paramref name="moreTo"/> — куди стати перемикачу «Усе (N)»: плити розвитку ставлять його в підвал під прокруткою
        /// (<see cref="DevFooter"/>), щоб край прокрутки його не різав; без нього — останнім у картці порад.
        /// </summary>
        protected VisualElement DevelopmentPanel(Action changed, int maxRecs = 3, bool headerShowsPoints = false, VisualElement moreTo = null)
        {
            var card = Glass(8f);
            string points = CareerScreenLines.DevPointsLine(T, C, headerShowsPoints);
            if (points != null) card.Add(Section(points));

            string gimmick = C.Content.GimmickOf(C.State.Gimmick).Key;
            int all = 5;   // атрибути — завжди п'ять
            foreach (var p in PerkCatalog.All)
                if (!C.Feud.State.Perks.Contains(p.Id)) all++;
            foreach (var m in C.Content.Moves.All)
                if (m.IsLearnable && !C.KnowsMove(m.Id) && (m.GimmickId == null || m.GimmickId == gimmick)) all++;

            var recs = new List<VisualElement>();
            foreach (var l in CareerScreenLines.DevRecs(T, C, maxRecs))
            {
                var a = l.Advice;
                Color accent;
                Action buy;
                switch (a.Kind)
                {
                    case AdviceKind.Perk:
                        accent = l.Path.HasValue ? RingUi.PathColor(l.Path.Value) : RingUi.Gold;
                        buy = () => { C.BuyPerk(a.Id); changed(); };
                        break;
                    case AdviceKind.Move:
                        accent = RingUi.Warn;
                        buy = () => { C.LearnMove(a.Id); changed(); };
                        break;
                    default:
                        accent = RingUi.Text;
                        buy = () => { C.BuyAttr(a.Attr); changed(); };
                        break;
                }
                recs.Add(Rec(l.Title, l.Why, accent, buy, l.PadKey));
            }
            if (recs.Count > 0)
            {
                card.Add(RingUi.Lbl(T.Get("c.dev.recommended"), 12f, RingUi.Dim));
                foreach (var r in recs) card.Add(r);
            }
            else card.Add(RingUi.Lbl(T.Get("c.dev.none"), 12f, RingUi.Dim));

            var more = RingUi.PadKey(Small((_devAll ? "▾ " : "▸ ") + T.Format("c.dev.all", all), () => { _devAll = !_devAll; changed(); }, true, null), "dev:all");
            more.style.marginTop = CareerTileLayout.DevFootTop;
            if (moreTo != null) { more.style.marginBottom = 0f; more.style.flexShrink = 0f; moreTo.Add(more); }
            else card.Add(more);
            if (!_devAll) return card;

            var attrs = RingUi.Row();
            attrs.style.flexWrap = Wrap.Wrap;
            for (int i = 0; i < 5; i++)
            {
                var a = (RingAttr)i;
                string reason;
                bool can = C.CanBuyAttr(a, out reason);
                int v = C.Player.Get(a);
                var b = Small(RingLabels.AttrName(T, a) + " " + Signed(v) + " → " + Signed(v + 1), () => { C.BuyAttr(a); changed(); }, can, null,
                    T.Format("dev.attr.buy", Development.AttrCost) + " — " + T.Get("c.dev.attr.why." + i) + (can ? "" : " · " + T.Get("dev.reason." + reason)));
                RingUi.PadKey(b, "dev:attr:" + i);
                b.style.marginRight = 4f;
                attrs.Add(b);
            }
            card.Add(Section(T.Get("c.dev.attrs")));
            card.Add(attrs);
            var mrow = RingUi.Row();
            mrow.style.flexWrap = Wrap.Wrap;
            int mc = 0, pc = 0;
            foreach (var m in C.Content.Moves.All)
            {
                if (!m.IsLearnable || C.KnowsMove(m.Id)) continue;
                if (m.GimmickId != null && m.GimmickId != gimmick) continue;
                string reason;
                bool can = C.CanLearnMove(m.Id, out reason);
                string id = m.Id;
                var b = Small(T.Get(m.NameKey) + " · " + m.Cost, () => { C.LearnMove(id); changed(); }, can, RingUi.Warn,
                    T.Format("c.dev.move.why", RingLabels.ActionName(T, m.Action)) + " — " + T.Format("dev.cost", m.Cost));
                RingUi.PadKey(b, "dev:move:" + id);
                b.style.marginRight = 4f;
                mrow.Add(b);
                mc++;
            }
            if (mc > 0) { card.Add(Section(T.Get("c.dev.moves"))); card.Add(mrow); }
            var prow = RingUi.Row();
            prow.style.flexWrap = Wrap.Wrap;
            foreach (var p in PerkCatalog.All)
            {
                if (C.Feud.State.Perks.Contains(p.Id)) continue;
                string reason;
                bool can = C.CanBuyPerk(p.Id, out reason);
                string id = p.Id;
                var b = Small(RingLabels.PerkName(T, id) + " · " + p.Cost, () => { C.BuyPerk(id); changed(); }, can,
                    p.Path.HasValue ? RingUi.PathColor(p.Path.Value) : RingUi.Gold,
                    RingLabels.PerkDesc(T, id) + (can ? "" : " · " + T.Get("dev.reason." + reason)));
                RingUi.PadKey(b, "dev:perk:" + id);
                b.style.marginRight = 4f;
                prow.Add(b);
                pc++;
            }
            if (pc > 0) { card.Add(Section(T.Get("dev.section.perks"))); card.Add(prow); }
            return card;
        }

        /// <summary>
        /// Рекомендація: кнопка-назва з ціною і під нею пояснення одним рядком (видно одразу, без наведення). Ключ фокуса сталий
        /// («rec:…»): після покупки напис міняється, а фокус без миші лишається на місці.
        /// </summary>
        private VisualElement Rec(string title, string why, Color accent, Action buy, string padKey)
        {
            var box = RingUi.Column();
            box.style.marginBottom = 4f;
            var b = RingUi.PadKey(Small(title, buy, true, accent), padKey);
            b.style.marginBottom = 0f;
            box.Add(b);
            var l = RingUi.Lbl(TextBreak.GlueDashes(why), BrandTokens.TypeMinInfo, RingUi.Dim);   // тире рядка не починає (TextBreak)
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.flexShrink = 0f;   // рядок росте разом із підказкою, а не підлазить під наступну кнопку
            box.style.flexShrink = 0f;
            l.style.marginLeft = 6f;
            box.Add(l);
            return box;
        }

        protected static string Signed(int v) { return v > 0 ? "+" + v : v < 0 ? "\u2212" + (-v) : "0"; }
    }

    // ============================ титул ============================
    // Титул — Menus/TitleMenuView.cs (ТЗ B01 v2 / B08, напрям «Енергія»); старий TitleScreenView прибрано 08.11.2026 — ніхто
    // його не створював.

    // ============================ створення ============================
    // Створення борця — Career/CreateScreenView.cs (ТЗ B03 v2 §7, мова «Арена», 08.11.2026).

    // ============================ тиждень ============================
    // Хаб тижня — окремий файл Career/WeekScreenView.cs (ТЗ B03 v2, мова «Арена», 08.11.2026).

    // ============================ вечір і кінець сезону ============================
    // Картка вечора, підсумок і кінець сезону — Career/ShowScreens.cs (ТЗ B03 v2 §6, §8, мова «Арена», 08.11.2026).
}
