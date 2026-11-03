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
            plan.Add(RingUi.Heading(t.Get("feud.section.plan")));
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
            next.Add(RingUi.Lbl(t.Get("ui.hint.keys"), 14f, RingUi.Dim));
            right.Add(next);
            body.Add(right);

            Root.Add(body);
        }

        public override void OnConfirm()
        {
            var c = Ctx.Campaign;
            if (c.Stage == RingStage.Overview) c.StartPromo();
            else if (c.Stage == RingStage.MatchPrep) c.StartMatch();
        }
    }
}
