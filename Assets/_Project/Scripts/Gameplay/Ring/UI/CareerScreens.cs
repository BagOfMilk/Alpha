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

        /// <summary>Компактна однорядкова кнопка; <paramref name="hint"/> — у рядок-підказку при наведенні.</summary>
        protected Button Small(string text, Action onClick, bool enabled, Color? accent, string hint = null)
        {
            var b = RingUi.MakeButton(text, onClick, enabled, accent, 13f);
            HudPad(b, 3f, 8f);
            b.style.marginBottom = 3f;
            b.style.borderLeftWidth = 3f;
            if (!string.IsNullOrEmpty(hint))
            {
                b.RegisterCallback<PointerEnterEvent>(e => { if (HintLine != null) { HintLine.text = hint; HintLine.style.color = RingUi.Text; } });
                b.RegisterCallback<PointerLeaveEvent>(e => { if (HintLine != null) { HintLine.text = ""; } });
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
        /// Рекомендація: доступний перк (найдешевший), доступний прийом (спершу свого ґіміка), найслабший атрибут, який можна купити.
        /// </summary>
        protected VisualElement DevelopmentPanel(Action changed, int maxRecs = 3)
        {
            var card = Glass(8f);
            card.Add(Section(T.Format("dev.points", C.DevPoints)));

            var recs = new List<VisualElement>();
            string gimmick = C.Content.GimmickOf(C.State.Gimmick).Key;
            int all = 0;
            string perkId = null; int perkCost = int.MaxValue; RingPath? perkPath = null;
            foreach (var p in PerkCatalog.All)
            {
                if (C.Feud.State.Perks.Contains(p.Id)) continue;
                all++;
                string r;
                if (C.CanBuyPerk(p.Id, out r) && p.Cost < perkCost) { perkId = p.Id; perkCost = p.Cost; perkPath = p.Path; }
            }
            MoveDef bestMove = null;
            foreach (var m in C.Content.Moves.All)
            {
                if (!m.IsLearnable || C.KnowsMove(m.Id)) continue;
                if (m.GimmickId != null && m.GimmickId != gimmick) continue;
                all++;
                string r;
                if (!C.CanLearnMove(m.Id, out r)) continue;
                if (bestMove == null || (m.GimmickId != null && bestMove.GimmickId == null)) bestMove = m;
            }
            int bestAttr = -1;
            for (int i = 0; i < 5; i++)
            {
                all++;
                string r;
                if (!C.CanBuyAttr((RingAttr)i, out r)) continue;
                if (bestAttr < 0 || C.Player.Get((RingAttr)i) < C.Player.Get((RingAttr)bestAttr)) bestAttr = i;
            }
            if (perkId != null)
            {
                string id = perkId;
                recs.Add(Rec(RingLabels.PerkName(T, id) + " · " + perkCost, RingLabels.PerkDesc(T, id),
                    perkPath.HasValue ? RingUi.PathColor(perkPath.Value) : RingUi.Gold, () => { C.BuyPerk(id); changed(); }));
            }
            if (bestMove != null)
            {
                string id = bestMove.Id;
                recs.Add(Rec(T.Get(bestMove.NameKey) + " · " + bestMove.Cost, T.Format("c.dev.move.why", RingLabels.ActionName(T, bestMove.Action)),
                    RingUi.Warn, () => { C.LearnMove(id); changed(); }));
            }
            if (bestAttr >= 0)
            {
                var a = (RingAttr)bestAttr;
                int v = C.Player.Get(a);
                recs.Add(Rec(RingLabels.AttrName(T, a) + " " + Signed(v) + " → " + Signed(v + 1) + " · " + Development.AttrCost,
                    T.Get("c.dev.attr.why." + bestAttr), RingUi.Text, () => { C.BuyAttr(a); changed(); }));
            }
            if (recs.Count > maxRecs) recs.RemoveRange(maxRecs, recs.Count - maxRecs);
            if (recs.Count > 0)
            {
                card.Add(RingUi.Lbl(T.Get("c.dev.recommended"), 12f, RingUi.Dim));
                foreach (var r in recs) card.Add(r);
            }
            else card.Add(RingUi.Lbl(T.Get("c.dev.none"), 12f, RingUi.Dim));

            var more = Small((_devAll ? "▾ " : "▸ ") + T.Format("c.dev.all", all), () => { _devAll = !_devAll; changed(); }, true, null);
            more.style.marginTop = 4f;
            card.Add(more);
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
                b.style.marginRight = 4f;
                prow.Add(b);
                pc++;
            }
            if (pc > 0) { card.Add(Section(T.Get("dev.section.perks"))); card.Add(prow); }
            return card;
        }

        /// <summary>Рекомендація: кнопка-назва з ціною і під нею пояснення одним рядком (видно одразу, без наведення).</summary>
        private VisualElement Rec(string title, string why, Color accent, Action buy)
        {
            var box = RingUi.Column();
            box.style.marginBottom = 4f;
            var b = Small(title, buy, true, accent);
            b.style.marginBottom = 0f;
            box.Add(b);
            var l = RingUi.Lbl(why, BrandTokens.TypeMinInfo, RingUi.Dim);
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
