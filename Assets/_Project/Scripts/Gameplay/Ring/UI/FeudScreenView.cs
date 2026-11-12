using Game.Core.Ring;
using Game.Gameplay.Ring.Models;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Gameplay.Ring.UI
{
    /// <summary>
    /// Екран ворожнечі (gdd §3): шкали, шляхи, план букера, умови матчу, напарник і «Далі» — до наступного промо або на матч.
    /// Показується між промо й перед матчем: гравець бачить, що зробили його вибори.
    /// </summary>
    public sealed class FeudScreenView : RingScreenView
    {
        /// <summary>Проходиться без миші (P3 геймпад, 11.11.2026): усі дії — кнопки RingUi, цілі фокусу.</summary>
        protected override bool PadTargets { get { return true; } }

        private readonly FeudScreenModel _m;

        public FeudScreenView(RingUiContext ctx) : base(ctx)
        {
            _m = new FeudScreenModel(ctx.Campaign, ctx.Text);
        }

        public override void Refresh()
        {
            var t = Ctx.Text;
            Root.Clear();
            RingUi.HudPad(Root);
            if (Ctx.Career != null) { RefreshCompact(); return; }

            var header = RingUi.Column();
            RingUi.Gap(header, 12f);
            header.Add(RingUi.Title(_m.TitleText));
            header.Add(RingUi.Lbl(_m.VersusText, 22f, RingUi.Text, true));
            header.Add(RingUi.Lbl(_m.ProgressText, 18f, RingUi.Dim));
            Root.Add(header);

            var body = RingUi.Row();
            RingUi.Grow(body, 1f);

            // Ліворуч: шкали й шляхи.
            var left = RingUi.Card();
            RingUi.Grow(left, 1f);
            left.style.marginRight = 12f;
            left.Add(RingUi.Heading(t.Get("feud.section.meters")));
            foreach (var r in _m.Meters())
            {
                var row = RingUi.Row();
                row.style.justifyContent = Justify.SpaceBetween;
                row.Add(RingUi.Lbl(r.Name, 18f));
                row.Add(RingUi.Lbl(r.ValueText, 18f, RingUi.Gold, true));
                left.Add(row);
                float span = r.Max - r.Min;
                var bar = RingUi.Bar(span <= 0 ? 0f : (r.Value - r.Min) / span, RingUi.Gold);
                RingUi.Gap(bar, 10f);
                left.Add(bar);
            }
            var pathsTitle = RingUi.Heading(t.Get("feud.section.paths"));
            pathsTitle.style.marginTop = 8f;
            left.Add(pathsTitle);
            foreach (var p in _m.Paths())
            {
                var row = RingUi.Row();
                row.style.justifyContent = Justify.SpaceBetween;
                var name = RingUi.Lbl(p.Mark + "  " + p.Name + (p.Dominant ? "  · " + t.Get("feud.dominant") : ""), 18f,
                    RingUi.PathColor(p.Path), p.Dominant);
                row.Add(name);
                row.Add(RingUi.Lbl(p.RankText, 16f, RingUi.Dim));
                RingUi.Gap(row, 6f);
                left.Add(row);
            }
            body.Add(left);

            // Праворуч: план, умови, напарник, наступний крок.
            var right = RingUi.Column();
            RingUi.Grow(right, 1f);

            var plan = RingUi.Card();
            RingUi.Gap(plan, 12f);
            plan.Add(RingUi.Heading(_m.PlanSectionText));
            plan.Add(RingUi.Lbl(_m.PlanText, 18f));
            var stipTitle = RingUi.Heading(t.Get("feud.section.stip"));
            stipTitle.style.marginTop = 10f;
            plan.Add(stipTitle);
            foreach (var s in _m.StipulationTexts()) plan.Add(RingUi.Lbl(s, 18f));
            var allyTitle = RingUi.Heading(t.Get("feud.section.ally"));
            allyTitle.style.marginTop = 10f;
            plan.Add(allyTitle);
            plan.Add(RingUi.Lbl(_m.AllyText, 18f));
            right.Add(plan);

            var next = RingUi.Card();
            RingUi.Grow(next, 1f);
            next.Add(RingUi.Heading(t.Get("feud.section.next")));
            if (_m.HasNextPromo)
            {
                next.Add(RingUi.Lbl(_m.NextTitleText, 20f, RingUi.Text, true));
                next.Add(RingUi.Lbl(_m.NextPlaceText, 17f, RingUi.Dim));
            }
            else
            {
                next.Add(RingUi.Lbl(_m.ProgressText, 18f));
            }
            next.Add(RingUi.PrimaryButton(_m.ContinueText, OnConfirm));
            next.Add(RingUi.Lbl(ArenaKit.ChooseHint(t), 14f, RingUi.Dim));
            right.Add(next);
            body.Add(right);

            Root.Add(body);
        }

        /// <summary>Кар'єра: вузька колонка праворуч — шапка, шкали тонкими смужками, шляхи одним рядком, план і умови, «Далі».</summary>
        private void RefreshCompact()
        {
            var t = Ctx.Text;
            var col = RingUi.SideColumn(Root);
            var head = RingUi.GlassCard();
            head.Add(RingUi.Lbl(_m.TitleText, 16f, RingUi.Gold, true));
            head.Add(RingUi.Lbl(_m.VersusText, 13f, RingUi.Text, true));
            head.Add(RingUi.Lbl(_m.ProgressText, 12f, RingUi.Dim));
            col.Add(head);

            var meters = RingUi.GlassCard();
            meters.style.marginTop = 6f;
            meters.Add(RingUi.Section(t.Get("feud.section.meters")));
            foreach (var r in _m.Meters())
            {
                float span = r.Max - r.Min;
                meters.Add(RingUi.MeterRow(r.Name, r.ValueText, span <= 0 ? 0f : (r.Value - r.Min) / span, RingUi.Gold));
            }
            var paths = RingUi.Row();
            paths.style.flexWrap = Wrap.Wrap;
            foreach (var p in _m.Paths())
            {
                var l = RingUi.Lbl(p.Mark + " " + p.Name + " · " + p.RankText, 12f, RingUi.PathColor(p.Path), p.Dominant);
                l.style.marginRight = 12f;
                paths.Add(l);
            }
            meters.Add(RingUi.Section(t.Get("feud.section.paths")));
            meters.Add(paths);
            col.Add(meters);

            var plan = RingUi.GlassCard();
            plan.style.marginTop = 6f;
            plan.Add(RingUi.Section(_m.PlanSectionText));
            plan.Add(RingUi.Lbl(_m.PlanText, 12f));
            plan.Add(RingUi.Lbl(t.Get("feud.section.stip") + ": " + string.Join(", ", _m.StipulationTexts()), 12f));
            plan.Add(RingUi.Lbl(t.Get("feud.section.ally") + ": " + _m.AllyText, 12f));
            if (_m.HasNextPromo)
            {
                plan.Add(RingUi.Section(t.Get("feud.section.next")));
                plan.Add(RingUi.Lbl(_m.NextTitleText, 13f, RingUi.Text, true));
                plan.Add(RingUi.Lbl(_m.NextPlaceText, 12f, RingUi.Dim));
            }
            var go = RingUi.PrimaryButton(_m.ContinueText, OnConfirm);
            go.style.fontSize = 16f;
            plan.Add(go);
            col.Add(plan);
        }

        public override void OnConfirm()
        {
            var c = Ctx.Campaign;
            if (c.Stage == RingStage.Overview) c.StartPromo();
            else if (c.Stage == RingStage.MatchPrep)
            {
                if (Ctx.StartFeudMatch != null) Ctx.StartFeudMatch(); else c.StartMatch();
            }
        }
    }
}
