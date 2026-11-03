using Game.Core.Ring;
using Game.Core.Ring.Match;
using Game.Gameplay.Ring.Models;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Gameplay.Ring.UI
{
    /// <summary>
    /// Екран матчу (gdd §2) без 3D: зверху раунд, ажіотаж, відлік, рефері, очки дій; посередині картки борців і журнал;
    /// знизу список дій з ціною, атрибутом і трьома відсотками (недоступні — сірі з причиною). Реакція на прийом і докрутка
    /// імпульсом — модальні. Хід суперника веде ШІ з паузою, щоб гравець встигав бачити, що відбувається.
    /// </summary>
    public sealed class MatchScreenView : RingScreenView
    {
        /// <summary>Пауза між діями ШІ, с.</summary>
        public const float AiPause = 0.6f;

        private readonly MatchScreenModel _m;
        private float _aiTimer;

        public MatchScreenView(RingUiContext ctx, MatchEngine engine) : base(ctx)
        {
            _m = new MatchScreenModel(engine, ctx.Text);
        }

        // ---------- кадр: хід суперника ----------

        public override void Tick(float dt)
        {
            if (_m.Input != MatchInput.Ai) { _aiTimer = 0f; return; }
            _aiTimer += dt;
            if (_aiTimer < AiPause) return;
            _aiTimer = 0f;
            _m.StepAi();
            Refresh();
        }

        // ---------- малювання ----------

        public override void Refresh()
        {
            var t = Ctx.Text;
            Root.Clear();
            RingUi.HudPad(Root);

            Root.Add(BuildTopBar());

            var mid = RingUi.Row();
            RingUi.Grow(mid, 1f);
            mid.style.marginTop = 10f;
            mid.style.marginBottom = 10f;
            var pc = BuildCard(_m.Card(MatchSide.Player), RingUi.Face);
            pc.style.marginRight = 10f;
            mid.Add(pc);
            var rc = BuildCard(_m.Card(MatchSide.Rival), RingUi.Heel);
            rc.style.marginRight = 10f;
            mid.Add(rc);
            mid.Add(BuildLog());
            Root.Add(mid);

            Root.Add(BuildBottom());

            if (_m.Input == MatchInput.PlayerReaction) Root.Add(BuildReactionModal());
            else if (_m.Input == MatchInput.PlayerBoost) Root.Add(BuildBoostModal());
        }

        private VisualElement BuildTopBar()
        {
            var bar = RingUi.Card(8f);
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.alignItems = Align.Center;
            bar.style.justifyContent = Justify.SpaceBetween;

            bar.Add(RingUi.Lbl(_m.RoundText, 20f, RingUi.Gold, true));

            var hype = RingUi.Column();
            RingUi.Grow(hype, 1f);
            hype.style.marginLeft = 20f; hype.style.marginRight = 20f;
            hype.Add(RingUi.Lbl(_m.HypeText, 17f));
            hype.Add(RingUi.Bar(_m.Hype / (float)_m.HypeMax, RingUi.Warn, 10f));
            bar.Add(hype);

            var info = RingUi.Column();
            info.style.marginRight = 20f;
            info.Add(RingUi.Lbl(_m.RefereeText, 15f, RingUi.Dim));
            string count = _m.CountText;
            if (count.Length > 0) info.Add(RingUi.Lbl(count, 15f, RingUi.Bad));
            bar.Add(info);

            var turn = RingUi.Column();
            turn.style.alignItems = Align.FlexEnd;
            turn.Add(RingUi.Lbl(_m.TurnText, 20f, _m.Input == MatchInput.Ai ? RingUi.Heel : RingUi.Face, true));
            turn.Add(RingUi.Lbl(_m.ApText, 16f, RingUi.Dim));
            bar.Add(turn);
            return bar;
        }

        private VisualElement BuildCard(FighterCardModel c, Color accent)
        {
            var card = RingUi.Card();
            RingUi.Grow(card, 1f);
            card.style.borderTopWidth = 3f;
            card.style.borderTopColor = accent;
            card.Add(RingUi.Lbl(c.Name, 22f, accent, true));

            card.Add(RingUi.Lbl(c.DamageText, 17f));
            // Ушкодження з порогами: червона смужка, пороги штрафу видно заздалегідь.
            card.Add(RingUi.Bar(c.Damage / (float)c.DamageMax, c.Penalty > 0 ? RingUi.Bad : RingUi.Warn, 12f));
            var th = RingUi.Row();
            th.style.marginBottom = 8f;
            th.Add(RingUi.Lbl(Ctx.Text.Format("match.card.thresholds", c.ExhaustedAt, c.EdgeAt), 13f, RingUi.Dim));
            card.Add(th);
            if (c.PenaltyText.Length > 0) card.Add(RingUi.Lbl(c.PenaltyText, 16f, RingUi.Bad, true));

            card.Add(RingUi.Lbl(c.MomentumText, 17f));
            var mom = RingUi.Bar(c.Momentum / (float)c.MomentumMax, accent, 10f);
            RingUi.Gap(mom, 8f);
            card.Add(mom);

            card.Add(RingUi.Lbl(c.PostureText + "  ·  " + c.ZoneText, 18f, RingUi.Text, true));
            card.Add(RingUi.Lbl(c.TokensText, 16f, RingUi.Dim));
            if (c.WarningsText.Length > 0) card.Add(RingUi.Lbl(c.WarningsText, 16f, RingUi.Warn));
            return card;
        }

        private VisualElement BuildLog()
        {
            var t = Ctx.Text;
            var log = RingUi.Card();
            RingUi.Grow(log, 1.3f);
            log.Add(RingUi.Heading(t.Get("match.log.title")));
            var lines = _m.LogTexts();
            for (int i = 0; i < lines.Count; i++)
            {
                bool last = i == lines.Count - 1;
                var l = RingUi.Lbl(lines[i], 15f, last ? RingUi.Text : RingUi.Dim);
                RingUi.Gap(l, 4f);
                log.Add(l);
            }
            return log;
        }

        private VisualElement BuildBottom()
        {
            var t = Ctx.Text;
            var bottom = RingUi.Card(10f);
            if (_m.IsOver)
            {
                bottom.Add(RingUi.Lbl(t.Get("match.result.title"), 22f, RingUi.Gold, true));
                foreach (var l in _m.ResultLines()) bottom.Add(RingUi.Lbl(l, 18f));
                bottom.Add(RingUi.PrimaryButton(t.Get("ui.next"), OnConfirm));
                return bottom;
            }

            bottom.Add(RingUi.Lbl(t.Get("match.actions.title"), 16f, RingUi.Dim));
            if (_m.Input == MatchInput.Ai)
            {
                bottom.Add(RingUi.Lbl(t.Get("ui.ai.thinking"), 18f, RingUi.Dim));
                return bottom;
            }
            if (_m.Input != MatchInput.PlayerAction) return bottom;

            var grid = RingUi.Row();
            grid.style.flexWrap = Wrap.Wrap;
            foreach (var a in _m.Actions())
            {
                var id = a.Id;
                string head = (a.Number > 0 ? a.Number + ". " : "·  ") + a.Name + "  —  " + a.CostText
                              + (a.AttrText.Length > 0 ? "  ·  " + a.AttrText + " " + a.ModifierText : "");
                string second = !a.Available ? a.ReasonText
                    : a.HasRoll ? t.Format(a.RollIsOpponents ? "match.action.chances.opp" : "match.action.chances", a.Percents[0], a.Percents[1], a.Percents[2])
                    : "";
                string text = second.Length > 0 ? head + "\n" + second : head;
                var b = RingUi.MakeButton(text, () => { _m.Perform(id); Refresh(); }, a.Available,
                    a.IsBig ? RingUi.Warn : (Color?)null, 15f);
                b.style.width = new Length(32.5f, LengthUnit.Percent);
                b.style.marginRight = 8f;
                grid.Add(b);
            }
            bottom.Add(grid);
            return bottom;
        }

        // ---------- модальні ----------

        private VisualElement BuildReactionModal()
        {
            var t = Ctx.Text;
            VisualElement panel;
            var shade = RingUi.Modal(out panel);
            var r = _m.Reaction();
            panel.Add(RingUi.Heading(r.PromptText));
            panel.Add(RingUi.MakeButton("1. " + r.SellText, () => { _m.React(ReactionChoice.Sell); Refresh(); }, true, RingUi.Face, 19f));
            panel.Add(RingUi.MakeButton("2. " + r.ReverseText, () => { _m.React(ReactionChoice.Reverse); Refresh(); }, r.CanReverse, RingUi.Heel, 19f));
            return shade;
        }

        private VisualElement BuildBoostModal()
        {
            var t = Ctx.Text;
            VisualElement panel;
            var shade = RingUi.Modal(out panel);
            panel.Add(RingUi.Heading(_m.BoostPrompt));
            foreach (var o in _m.BoostOptions())
            {
                int points = o.Points;
                panel.Add(RingUi.MakeButton(o.Number + ". " + o.Text, () => { _m.Commit(points); Refresh(); }, true,
                    RingUi.TierColor(o.Tier), 19f));
            }
            return shade;
        }

        // ---------- клавіші ----------

        public override void OnDigit(int n)
        {
            bool done = false;
            switch (_m.Input)
            {
                case MatchInput.PlayerAction: done = _m.PerformNumber(n); break;
                case MatchInput.PlayerReaction:
                    done = n == 1 ? _m.React(ReactionChoice.Sell) : n == 2 && _m.React(ReactionChoice.Reverse);
                    break;
                case MatchInput.PlayerBoost: done = _m.CommitNumber(n); break;
            }
            if (done) Refresh();
        }

        public override void OnConfirm()
        {
            if (!_m.IsOver) return;
            Ctx.Campaign.FinishMatch();
        }
    }
}
