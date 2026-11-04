using Game.Core.Ring;
using Game.Gameplay.Ring.Models;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Gameplay.Ring.UI
{
    /// <summary>
    /// Екран «кінцівка + розвиток» (gdd §5.3, §6): назва кінцівки й підсумок матчу, очки розвитку, ранги шляхів, перки за
    /// шляхами з кнопкою купівлі, +1 до атрибута, «Реванш» (той самий матч із купленим) і «Заново».
    /// </summary>
    public sealed class EndingScreenView : RingScreenView
    {
        private readonly DevelopmentScreenModel _m;
        private bool _opened;

        public EndingScreenView(RingUiContext ctx) : base(ctx)
        {
            _m = new DevelopmentScreenModel(ctx.Campaign, ctx.Text);
        }

        public override void Refresh()
        {
            var t = Ctx.Text;
            if (!_opened && Ctx.Campaign.Stage == RingStage.Ending)
            {
                Ctx.Campaign.OpenDevelopment();
                _opened = true;
            }
            Root.Clear();
            RingUi.HudPad(Root);

            // Шапка: кінцівка.
            var head = RingUi.Column();
            RingUi.Gap(head, 10f);
            head.Add(RingUi.Title(_m.EndingTitle));
            head.Add(RingUi.Lbl(_m.EndingDesc, 18f, RingUi.Dim));
            Root.Add(head);

            var body = RingUi.Row();
            RingUi.Grow(body, 1f);

            // Ліва колонка: підсумки.
            var left = RingUi.Card();
            RingUi.Grow(left, 0.8f);
            left.style.marginRight = 12f;
            left.Add(RingUi.Heading(t.Get("dev.section.result")));
            foreach (var l in _m.ResultLines()) left.Add(RingUi.Lbl(l, 18f));
            left.Add(RingUi.Lbl(_m.PlanText, 17f, RingUi.Gold));
            var matchLines = _m.MatchLines();
            if (matchLines.Count > 1)
            {
                var mt = RingUi.Heading(t.Get("dev.section.matches"));
                mt.style.marginTop = 12f;
                left.Add(mt);
                foreach (var l in matchLines) left.Add(RingUi.Lbl(l, 17f));
            }
            if (_m.HasRematchResult)
            {
                var rt = RingUi.Heading(t.Get("dev.section.rematch"));
                rt.style.marginTop = 12f;
                left.Add(rt);
                foreach (var l in _m.RematchLines()) left.Add(RingUi.Lbl(l, 18f));
            }
            var pt = RingUi.Heading(t.Get("dev.section.paths"));
            pt.style.marginTop = 12f;
            left.Add(pt);
            foreach (var p in _m.Paths())
                left.Add(RingUi.Lbl(p.Name + "  ·  " + p.RankText + "  (" + p.Points + ")", 17f, RingUi.PathColor(p.Path)));
            body.Add(left);

            // Середня: перки.
            var mid = RingUi.Card();
            RingUi.Grow(mid, 1.4f);
            mid.style.marginRight = 12f;
            mid.Add(RingUi.Heading(t.Get("dev.section.perks")));
            var scroll = RingUi.Scroll();
            foreach (var r in _m.Perks()) scroll.Add(PerkButton(r));
            mid.Add(scroll);
            body.Add(mid);

            // Права: очки і атрибути, реванш.
            var right = RingUi.Card();
            RingUi.Grow(right, 0.9f);
            right.Add(RingUi.Lbl(_m.PointsText, 22f, RingUi.Gold, true));
            var at = RingUi.Heading(t.Get("dev.section.attrs"));
            at.style.marginTop = 10f;
            right.Add(at);
            foreach (var a in _m.Attrs())
            {
                var attr = a.Attr;
                string text = a.Name + "  " + a.ValueText + "   " + (a.CanBuy ? a.BuyText : a.ReasonText);
                right.Add(RingUi.MakeButton(text, () => { _m.BuyAttr(attr); Refresh(); }, a.CanBuy, null, 17f));
            }
            if (Ctx.AfterDevelopment != null)
            {
                // Кар'єра: ворожнеча — лише крок великого шоу; далі підсумок вечора (реванш і «Заново» — у зрізі).
                var space = RingUi.Lbl("", 8f);
                right.Add(space);
                right.Add(RingUi.PrimaryButton(Ctx.Text.Get("ui.next"), OnConfirm));
            }
            else if (_m.IsFinal)
            {
                var hint = RingUi.Lbl(t.Get("dev.rematch.hint"), 14f, RingUi.Dim);
                hint.style.marginTop = 10f;
                right.Add(hint);
                right.Add(RingUi.PrimaryButton(t.Get("ui.rematch"), OnRematch, _m.CanRematch));
                var restart = RingUi.MakeButton(t.Get("ui.restart"), () => { if (Ctx.Restart != null) Ctx.Restart(); }, true, RingUi.Dim, 17f);
                restart.style.unityTextAlign = TextAnchor.MiddleCenter;
                right.Add(restart);
            }
            else
            {
                var space = RingUi.Lbl("", 8f);
                right.Add(space);
                right.Add(RingUi.PrimaryButton(_m.ContinueText, OnConfirm, _m.CanContinue));
            }
            body.Add(right);

            Root.Add(body);
        }

        private VisualElement PerkButton(PerkRow r)
        {
            var t = Ctx.Text;
            string status = r.State == PerkState.Owned ? t.Get("dev.owned")
                : r.State == PerkState.Available ? r.CostText
                : r.ReasonText + "  (" + r.CostText + ")";
            string text = r.Name + "  ·  " + r.PathText + "\n" + r.Desc + "\n" + status;
            string id = r.Id;
            Color? accent = r.IsGeneral ? RingUi.Gold : RingUi.PathColor(r.Path);
            bool can = r.State == PerkState.Available;
            var b = RingUi.MakeButton(text, () => { _m.BuyPerk(id); Refresh(); }, can, r.State == PerkState.Owned ? RingUi.Good : accent, 16f);
            return b;
        }

        /// <summary>Enter/Пробіл/кнопка: між матчами — далі за сюжетом; після розв'язки нічого (реванш чи «Заново» — кнопками).</summary>
        public override void OnConfirm()
        {
            if (Ctx.AfterDevelopment != null) { Ctx.AfterDevelopment(); return; }
            if (_m.CanContinue) _m.Continue();
        }

        private void OnRematch()
        {
            if (!_m.CanRematch) return;
            Ctx.Campaign.StartRematch();
        }
    }
}
