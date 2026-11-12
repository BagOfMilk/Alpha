using System.Collections.Generic;
using Game.Core.Ring;
using Game.Core.Ring.Match;
using Game.Gameplay.Ring.Models;
using Game.Gameplay.Ring.Stage;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Gameplay.Ring.UI
{
    /// <summary>
    /// Екран матчу (gdd §2), сучасний і небагатослівний: ринг — головне. Угорі по боках плашки борців (ім'я, смужки
    /// ушкодження й імпульсу, стан і зона коротко), по центру тонка смуга раунду й ажіотажу; знизу — 2–3 останні події
    /// журналу (повний — L), рядок підказки й ряд кнопок ЛИШЕ доступних дій: ціна ОД точками, шанси трикольоровою
    /// смужкою (відсотки — у підказці при наведенні). Недоступні згорнуті в «ще N недоступно». Реакція на прийом і
    /// докрутка імпульсом — компактні модальні. Хід суперника веде ШІ з паузою (чекає, поки 3D-режисер дограє кліп).
    /// </summary>
    public sealed class MatchScreenView : RingScreenView
    {
        /// <summary>Пауза між діями ШІ, с.</summary>
        public const float AiPause = 0.6f;

        private static readonly Color Glass = RingTheme.WithAlpha(RingTheme.Panel, BrandTokens.AlphaGlass);
        private static readonly Color GlassHi = RingTheme.WithAlpha(RingTheme.PanelRaised, BrandTokens.AlphaPanel);

        private readonly MatchScreenModel _m;
        private readonly RingCallout _call = new RingCallout();
        private int _beatSeen = -1;
        private float _overAt = -1f;
        private float _aiTimer;
        private bool _wasBusy;
        /// <summary>Хід щойно був за суперником, а 3D ще дограє його кліпи — нижня панель пише, чий хід.</summary>
        private bool _rivalTail;
        private FighterCardModel _snapP, _snapR;
        private bool _showUnavailable;
        private bool _showFullLog;
        private Label _hint;

        public MatchScreenView(RingUiContext ctx, MatchEngine engine) : base(ctx)
        {
            _m = new MatchScreenModel(engine, ctx.Text, ctx.Career);
        }

        // ---------- кадр: хід суперника ----------

        /// <summary>
        /// Кінець матчу — спершу момент (виноска «ПЕРЕМОГА/ПОРАЗКА», рефері, зал), лише тоді цифри (огляд 06.11.2026:
        /// результат був відомістю одразу, поки 3D ще дограє утримання).
        /// </summary>
        public const float ResultMoment = 2.6f;

        private bool HoldingResult
        {
            get
            {
                if (!_m.IsOver || !RingStageHub.Has3D) return false;
                if (_overAt < 0f) return true;
                return RingStageHub.Busy || Time.unscaledTime - _overAt < ResultMoment;
            }
        }

        /// <summary>
        /// Хід гравця відкривається, коли 3D дограло: ядро передає хід миттєво, і кнопки («Встати», «Вийти з рингу»…)
        /// світились, поки фінішер суперника ще летів (плейтест 08.11.2026, P1-4). Без 3D — одразу.
        /// </summary>
        private bool WaitStage { get { return RingStageHub.Busy && !_m.IsOver; } }

        public override void Tick(float dt)
        {
            TickBeat(dt);
            if (_m.Input == MatchInput.Ai) _rivalTail = true;
            else if (!RingStageHub.Busy) _rivalTail = false;
            if (_m.Input != MatchInput.Ai) { _aiTimer = 0f; return; }
            // Хід суперника чекає, поки 3D-режисер дограє поточний кліп (потім ще звичайна пауза).
            if (RingStageHub.Busy) { _aiTimer = 0f; return; }
            _aiTimer += dt;
            if (_aiTimer < AiPause) return;
            _aiTimer = 0f;
            _m.StepAi();
            Refresh();
        }

        /// <summary>Новий такт режисера → виноска; кінець матчу — момент, потім цифри.</summary>
        private void TickBeat(float dt)
        {
            var st = RingStageHub.Current;
            if (st != null && st.BeatSeq != _beatSeen)
            {
                bool first = _beatSeen < 0;
                _beatSeen = st.BeatSeq;
                if (!first && st.BeatEvent != null) RingCallouts.Push(_call, st.BeatEvent, st.BeatPhase, Ctx.Text, _m.IsOver ? _m.Engine.Result.Winner : (MatchWinner?)null);
                // Такт останньої події кроку — табло наздоганяє кадр саме на контакті (а не після всієї серії кліпів).
                var log = _m.Engine.State.Log;
                if (!first && st.BeatEvent != null && log.Count > 0 && st.BeatEvent.Seq >= log[log.Count - 1].Seq && st.ShownSeq >= st.BeatEvent.Seq) { _snapP = null; Refresh(); }
            }
            // Журнал відкриває рядок на такті його події (не раніше).
            int shown = Shown;
            if (shown != _shownSeen) { _shownSeen = shown; Refresh(); }
            // Режисер дограв — табло наздоганяє кадр (стан ядра показуємо, коли 3D його вже показав).
            bool busy = RingStageHub.Busy;
            if (_wasBusy && !busy) { _wasBusy = false; Refresh(); }
            _wasBusy = busy;
            bool wasHolding = HoldingResult;
            if (_m.IsOver && _overAt < 0f && !RingStageHub.Busy) _overAt = Time.unscaledTime;
            // Ігровий час, а не реальний: виноска йде в такт кліпу за будь-якого темпу (огляд на ×3), сповільнення фінішера
            // розтягує й «ФІНІШЕР!».
            _call.Tick(Time.deltaTime);
            if (wasHolding && !HoldingResult) Refresh();
        }

        // ---------- малювання ----------

        public override void Refresh()
        {
            Root.Clear();
            bool stage = RingStageHub.Has3D;
            Root.style.backgroundColor = stage ? Color.clear : RingUi.Bg;
            Root.style.justifyContent = Justify.SpaceBetween;
            Root.style.paddingLeft = 22f; Root.style.paddingRight = 22f;
            Root.style.paddingTop = 14f; Root.style.paddingBottom = 14f;

            Root.Add(BuildTop());

            var bottom = RingUi.Column();
            bottom.pickingMode = PickingMode.Ignore;
            if (_m.IsOver) { if (!HoldingResult) bottom.Add(BuildResult()); }
            else
            {
                bottom.Add(BuildLogStrip());
                bottom.Add(BuildActions());
            }
            Root.Add(bottom);

            if (!WaitStage && _m.Input == MatchInput.PlayerReaction) Root.Add(BuildReactionModal());
            else if (!WaitStage && _m.Input == MatchInput.PlayerBoost) Root.Add(BuildBoostModal());
            if (_showFullLog) Root.Add(BuildFullLog());
            Root.Add(_call.Layer);
        }

        private static VisualElement Panel(float pad = 8f, float alpha = -1f)
        {
            var e = RingUi.Column();
            e.style.backgroundColor = alpha >= 0f ? RingTheme.WithAlpha(Glass, alpha) : Glass;
            e.style.borderTopLeftRadius = 8f; e.style.borderTopRightRadius = 8f;
            e.style.borderBottomLeftRadius = 8f; e.style.borderBottomRightRadius = 8f;
            e.style.paddingLeft = pad + 3f; e.style.paddingRight = pad + 3f;
            e.style.paddingTop = pad; e.style.paddingBottom = pad;
            return e;
        }

        // ---------- верх: плашки борців і смуга раунду ----------

        private VisualElement BuildTop()
        {
            var row = RingUi.Row();
            row.style.alignItems = Align.FlexStart;
            row.pickingMode = PickingMode.Ignore;
            // Ядро рахує миттєво, 3D відстає на кліп: поки режисер грає, плашки показують стан до події (огляд фану 06.11.2026,
            // R8/F4: «Лежить», а постать ще стоїть і кидає).
            bool lag = RingStageHub.Busy && _snapP != null;
            if (!lag) { _snapP = _m.Card(MatchSide.Player); _snapR = _m.Card(MatchSide.Rival); }
            var left = BuildPlate(_snapP, RingUi.Face);
            var right = BuildPlate(_snapR, RingUi.Heel);
            row.Add(left);
            var mid = BuildRoundStrip();
            RingUi.Grow(mid, 1f);
            mid.style.marginLeft = 12f; mid.style.marginRight = 12f;
            row.Add(mid);
            row.Add(right);
            return row;
        }

        private VisualElement BuildPlate(FighterCardModel c, Color accent)
        {
            var p = Panel(7f);
            p.style.width = Length.Percent(26f);
            p.style.flexShrink = 0f;
            p.style.borderLeftWidth = 3f;
            p.style.borderLeftColor = accent;

            var head = RingUi.Row();
            head.style.justifyContent = Justify.SpaceBetween;
            head.style.alignItems = Align.Center;
            head.Add(RingUi.Lbl(c.Name, 17f, accent, true));
            // Стан і «Сили» — коротко: «Стоїть · Сили 7/10».
            head.Add(RingUi.Lbl(c.PostureText + " · " + c.StaminaText, 13f, RingUi.Dim));
            p.Add(head);

            p.Add(BarLine(c.Damage, c.DamageMax, c.Penalty > 0 ? RingUi.Bad : RingUi.Warn, 8f, Ctx.Text.Get("match.card.short.damage"), c.Damage + "/" + c.DamageMax));
            p.Add(BarLine(c.Momentum, c.MomentumMax, accent, 6f, Ctx.Text.Get("match.card.short.impulse"), c.Momentum + "/" + c.MomentumMax));

            string extra = "";
            if (c.PenaltyText.Length > 0) extra += c.PenaltyText;
            if (c.WarningsText.Length > 0) extra += (extra.Length > 0 ? "  " : "") + c.WarningsText;
            if (c.Tokens > 0) extra += (extra.Length > 0 ? "  " : "") + c.TokensText;
            if (extra.Length > 0)
            {
                var l = RingUi.Lbl(extra, 12f, c.PenaltyText.Length > 0 ? RingUi.Bad : RingUi.Warn);
                l.style.marginTop = 3f;
                p.Add(l);
            }
            return p;
        }

        private static VisualElement BarLine(int v, int max, Color fill, float h, string label, string value)
        {
            var row = RingUi.Row();
            row.style.alignItems = Align.Center;
            row.style.marginTop = 4f;
            var lab = RingUi.Lbl(label, 11f, RingUi.Dim);
            lab.style.width = 76f;
            row.Add(lab);
            var bar = RingUi.Bar(max > 0 ? v / (float)max : 0f, fill, h);
            RingUi.Grow(bar, 1f);
            bar.style.borderTopWidth = 0f; bar.style.borderBottomWidth = 0f; bar.style.borderLeftWidth = 0f; bar.style.borderRightWidth = 0f;
            bar.style.borderTopLeftRadius = 4f; bar.style.borderTopRightRadius = 4f;
            bar.style.borderBottomLeftRadius = 4f; bar.style.borderBottomRightRadius = 4f;
            row.Add(bar);
            var val = RingUi.Lbl(value, 12f, RingUi.Text);
            val.style.width = 40f;
            val.style.unityTextAlign = TextAnchor.MiddleRight;
            row.Add(val);
            return row;
        }

        private VisualElement BuildRoundStrip()
        {
            var p = Panel(6f, 0.62f);
            p.style.alignItems = Align.Center;
            var top = RingUi.Row();
            top.style.justifyContent = Justify.Center;
            top.Add(RingUi.Lbl(_m.RoundText, 15f, RingUi.Gold, true));
            string init = _m.InitiativeText;
            if (init.Length > 0) { var c = RingUi.Lbl("   " + init, 13f, RingUi.Gold); top.Add(c); }
            p.Add(top);
            var hype = RingUi.Bar(_m.Hype / (float)_m.HypeMax, RingUi.Warn, 5f);
            hype.style.width = Length.Percent(100f);
            hype.style.marginTop = 4f; hype.style.marginBottom = 3f;
            p.Add(hype);
            p.Add(RingUi.Lbl(_m.HypeText + " · " + _m.RefereeText, 12f, RingUi.Dim));
            return p;
        }

        // ---------- низ: журнал, підказка, дії ----------

        /// <summary>До якої події журнал уже можна показати: 3D відкриває рядок на такті (без 3D — усе).</summary>
        private static int Shown { get { return RingStageHub.Has3D ? RingStageHub.Current.ShownSeq : int.MaxValue; } }

        private int _shownSeen = int.MinValue;

        private VisualElement BuildLogStrip()
        {
            var t = Ctx.Text;
            var lines = _m.LogTexts(Shown);
            var box = Panel(6f, 0.55f);
            box.style.alignSelf = Align.FlexStart;
            box.style.maxWidth = Length.Percent(46f);
            box.style.marginBottom = 8f;
            int from = Mathf.Max(0, lines.Count - 3);
            for (int i = from; i < lines.Count; i++)
            {
                bool last = i == lines.Count - 1;
                box.Add(RingUi.Lbl(lines[i], 14f, last ? RingUi.Text : RingUi.Dim));
            }
            var more = RingUi.Lbl(t.Get("match.log.hint"), 11f, RingUi.Dim);
            more.style.marginTop = 2f;
            box.Add(more);
            box.RegisterCallback<ClickEvent>(e => { _showFullLog = true; Refresh(); });
            return box;
        }

        private VisualElement BuildActions()
        {
            var t = Ctx.Text;
            var wrap = Panel(8f);
            wrap.style.backgroundColor = Glass;

            if (_m.Input == MatchInput.Ai)
            {
                // Не «думає»: хід суперника йде кліпами на рингу — пишемо, чий хід (огляд 06.11.2026 — довге «Суперник думає…»).
                wrap.Add(RingUi.Lbl(t.Format("match.rival.turn", _m.Card(MatchSide.Rival).Name), 15f, RingUi.Heel, true));
                return wrap;
            }
            if (_m.Input != MatchInput.PlayerAction) return wrap;
            if (WaitStage)
            {
                // Кліпи суперника ще йдуть — його хід на екрані триває; власні кліпи гравця — панель порожня до кінця.
                if (_rivalTail) wrap.Add(RingUi.Lbl(t.Format("match.rival.turn", _m.Card(MatchSide.Rival).Name), 15f, RingUi.Heel, true));
                else { var none = new VisualElement(); none.pickingMode = PickingMode.Ignore; return none; }
                return wrap;
            }

            _hint = RingUi.Lbl(t.Get("match.hint.default"), 14f, RingUi.Dim);
            _hint.style.marginBottom = 6f;
            wrap.Add(_hint);

            var row = RingUi.Row();
            row.style.flexWrap = Wrap.Wrap;
            row.style.alignItems = Align.Stretch;
            MatchActionRow endTurn = null;
            var unavailable = new List<MatchActionRow>();
            foreach (var a in _m.Actions())
            {
                if (a.Id == MatchActionId.CatchBreath) { endTurn = a; continue; }
                if (!a.Available) { unavailable.Add(a); continue; }
                row.Add(ActionButton(a));
            }
            if (endTurn != null && endTurn.Available) row.Add(EndTurnButton(endTurn));
            wrap.Add(row);

            if (unavailable.Count > 0)
            {
                var chip = RingUi.Lbl(_showUnavailable ? t.Get("match.unavail.hide") : t.Format("match.unavail.more", unavailable.Count), 13f, RingUi.Dim);
                chip.style.marginTop = 6f;
                chip.RegisterCallback<ClickEvent>(e => { _showUnavailable = !_showUnavailable; Refresh(); });
                wrap.Add(chip);
                if (_showUnavailable)
                {
                    var list = RingUi.Row();
                    list.style.flexWrap = Wrap.Wrap;
                    foreach (var a in unavailable)
                    {
                        var l = RingUi.Lbl(a.Name + " — " + a.ReasonText, 12f, RingUi.Disabled);
                        l.style.marginRight = 14f; l.style.marginTop = 3f;
                        list.Add(l);
                    }
                    wrap.Add(list);
                }
            }
            return wrap;
        }

        private VisualElement ActionButton(MatchActionRow a)
        {
            var t = Ctx.Text;
            var b = RingUi.Column();
            b.style.backgroundColor = GlassHi;
            b.style.borderTopLeftRadius = 6f; b.style.borderTopRightRadius = 6f;
            b.style.borderBottomLeftRadius = 6f; b.style.borderBottomRightRadius = 6f;
            b.style.paddingLeft = 9f; b.style.paddingRight = 9f; b.style.paddingTop = 6f; b.style.paddingBottom = 6f;
            b.style.marginRight = 6f; b.style.marginBottom = 6f;
            b.style.minWidth = 138f;
            if (a.IsBig) { b.style.borderBottomWidth = 0f; b.style.borderLeftWidth = 3f; b.style.borderLeftColor = RingUi.Warn; }

            var top = RingUi.Row();
            top.style.justifyContent = Justify.SpaceBetween;
            top.style.alignItems = Align.Center;
            top.Add(RingUi.Lbl((a.Number > 0 ? a.Number + "  " : "") + a.Name, 15f, RingUi.Text, true));
            var dots = RingUi.Lbl(Dots(a.Cost), 13f, RingUi.Gold);
            dots.style.marginLeft = 8f;
            top.Add(dots);
            b.Add(top);

            if (a.HasRoll && a.Percents.Length == 3)
            {
                var strip = RingUi.Row();
                strip.style.height = 4f;
                strip.style.marginTop = 5f;
                strip.style.borderTopLeftRadius = 2f; strip.style.borderTopRightRadius = 2f;
                strip.style.borderBottomLeftRadius = 2f; strip.style.borderBottomRightRadius = 2f;
                strip.style.overflow = Overflow.Hidden;
                strip.Add(Seg(a.Percents[0], RingUi.Bad));
                strip.Add(Seg(a.Percents[1], RingUi.Warn));
                strip.Add(Seg(a.Percents[2], RingUi.Good));
                b.Add(strip);
            }
            else
            {
                var gap = new VisualElement();
                gap.style.height = 4f; gap.style.marginTop = 5f;
                b.Add(gap);
            }

            string hint = HintText(a);
            b.RegisterCallback<PointerEnterEvent>(e => { b.style.backgroundColor = RingUi.PanelHover; if (_hint != null) { _hint.text = hint; _hint.style.color = RingUi.Text; } });
            b.RegisterCallback<PointerLeaveEvent>(e => { b.style.backgroundColor = GlassHi; if (_hint != null) { _hint.text = t.Get("match.hint.default"); _hint.style.color = RingUi.Dim; } });
            b.RegisterCallback<ClickEvent>(e => { _m.PerformRow(a); Refresh(); });
            return b;
        }

        private VisualElement EndTurnButton(MatchActionRow a)
        {
            var b = RingUi.Column();
            b.style.backgroundColor = RingTheme.WithAlpha(RingTheme.Hover, BrandTokens.AlphaPanel);
            b.style.borderTopLeftRadius = 6f; b.style.borderTopRightRadius = 6f;
            b.style.borderBottomLeftRadius = 6f; b.style.borderBottomRightRadius = 6f;
            b.style.paddingLeft = 12f; b.style.paddingRight = 12f; b.style.paddingTop = 6f; b.style.paddingBottom = 6f;
            b.style.marginBottom = 6f;
            b.style.justifyContent = Justify.Center;
            b.Add(RingUi.Lbl((a.Number > 0 ? a.Number + "  " : "") + Ctx.Text.Get("match.endturn.short"), 15f, RingUi.Gold, true));
            string hint = Ctx.Text.Get("match.endturn");
            b.RegisterCallback<PointerEnterEvent>(e => { if (_hint != null) { _hint.text = hint; _hint.style.color = RingUi.Text; } });
            b.RegisterCallback<PointerLeaveEvent>(e => { if (_hint != null) { _hint.text = Ctx.Text.Get("match.hint.default"); _hint.style.color = RingUi.Dim; } });
            b.RegisterCallback<ClickEvent>(e => { _m.PerformRow(a); Refresh(); });
            return b;
        }

        private string HintText(MatchActionRow a)
        {
            var t = Ctx.Text;
            string s = a.Name + "  ·  " + a.CostText;
            if (a.AttrText.Length > 0) s += "  ·  " + a.AttrText + " " + a.ModifierText;
            if (a.HasRoll && a.Percents.Length == 3)
                s += "   |   " + MatchScreenModel.ChancesShort(t, a);
            return s;
        }

        private static string Dots(int n)
        {
            if (n <= 0) return "";
            return new string('●', Mathf.Min(n, 6));
        }

        private static VisualElement Seg(int percent, Color c)
        {
            var e = new VisualElement();
            e.style.flexGrow = Mathf.Max(1, percent);
            e.style.flexBasis = 0f;
            e.style.backgroundColor = c;
            return e;
        }

        private VisualElement BuildResult()
        {
            var t = Ctx.Text;
            var bottom = Panel(12f, 0.88f);
            bottom.Add(RingUi.Lbl(t.Get("match.result.title"), 22f, RingUi.Gold, true));
            foreach (var l in _m.ResultLines()) bottom.Add(RingUi.Lbl(l, 17f));
            bottom.Add(RingUi.PrimaryButton(t.Get("ui.next"), OnConfirm));
            return bottom;
        }

        // ---------- модальні ----------

        private static VisualElement ModalShade(out VisualElement panel)
        {
            var shade = RingUi.Column();
            shade.style.position = Position.Absolute;
            shade.style.left = 0f; shade.style.right = 0f; shade.style.top = 0f; shade.style.bottom = 0f;
            shade.style.backgroundColor = RingTheme.WithAlpha(RingTheme.Scrim, 0.28f);
            shade.style.justifyContent = Justify.FlexEnd;
            shade.style.alignItems = Align.Center;
            shade.style.paddingBottom = 150f;
            panel = Panel(14f, 0.9f);
            panel.style.width = new Length(46f, LengthUnit.Percent);
            panel.style.minWidth = 380f;
            panel.style.maxWidth = 680f;
            shade.Add(panel);
            return shade;
        }

        private VisualElement BuildReactionModal()
        {
            VisualElement panel;
            var shade = ModalShade(out panel);
            var r = _m.Reaction();
            panel.Add(RingUi.Heading(r.PromptText));
            panel.Add(RingUi.MakeButton("1. " + r.SellText, () => { _m.React(ReactionChoice.Sell); Refresh(); }, true, RingUi.Face, 17f));
            panel.Add(RingUi.MakeButton("2. " + r.ReverseText, () => { _m.React(ReactionChoice.Reverse); Refresh(); }, r.CanReverse, RingUi.Heel, 17f));
            return shade;
        }

        private VisualElement BuildBoostModal()
        {
            VisualElement panel;
            var shade = ModalShade(out panel);
            panel.Add(RingUi.Heading(_m.BoostPrompt));
            foreach (var o in _m.BoostOptions())
            {
                int points = o.Points;
                panel.Add(RingUi.MakeButton(o.Number + ". " + o.Text, () => { _m.Commit(points); Refresh(); }, true,
                    RingUi.TierColor(o.Tier), 17f));
            }
            return shade;
        }

        private VisualElement BuildFullLog()
        {
            var t = Ctx.Text;
            var shade = RingUi.Column();
            shade.style.position = Position.Absolute;
            shade.style.left = 0f; shade.style.right = 0f; shade.style.top = 0f; shade.style.bottom = 0f;
            shade.style.backgroundColor = RingTheme.WithAlpha(RingTheme.Scrim, BrandTokens.AlphaShade);
            shade.style.justifyContent = Justify.Center;
            shade.style.alignItems = Align.Center;
            var p = Panel(14f, 0.94f);
            p.style.width = Length.Percent(60f);
            p.style.maxWidth = 820f;
            p.Add(RingUi.Heading(t.Get("match.log.full")));
            foreach (var l in _m.LogTexts(Shown)) p.Add(RingUi.Lbl(l, 15f, RingUi.Text));
            p.Add(RingUi.Lbl("L", 12f, RingUi.Dim));
            shade.Add(p);
            shade.RegisterCallback<ClickEvent>(e => { _showFullLog = false; Refresh(); });
            return shade;
        }

        // ---------- клавіші ----------

        public override void OnToggleLog()
        {
            _showFullLog = !_showFullLog;
            Refresh();
        }

        public override void OnDigit(int n)
        {
            if (WaitStage) return;
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
            if (!_m.IsOver || HoldingResult) return;
            if (Ctx.FinishMatch != null) Ctx.FinishMatch(); else Ctx.Campaign.FinishMatch();
        }
    }
}
