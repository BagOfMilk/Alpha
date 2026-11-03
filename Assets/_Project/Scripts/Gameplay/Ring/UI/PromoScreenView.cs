using Game.Core.Ring;
using Game.Gameplay.Ring.Models;
using Game.Gameplay.UI.Toolkit;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Gameplay.Ring.UI
{
    /// <summary>
    /// Екран промо (gdd §4): унизу вікно діалогу — мовець, репліка, варіанти з номерами 1–9 (сірі — з причиною, значок шляху
    /// Г/Л/Б), кидок із трьома відсотками ДО кидка, два кубики й ступінь після, вибір нагород із лічильником.
    /// Тримає в пам'яті останню репліку, щоб вибір і кидок не лишались без контексту.
    /// </summary>
    public sealed class PromoScreenView : RingScreenView
    {
        private readonly PromoScreenModel _m;
        private string _lastSpeaker = "";
        private string _lastLine = "";
        private bool _lastNarrator;
        private readonly FeudScreenModel _feud;
        // Значення шкал на початку промо і при попередньому малюванні: зміна від останнього вибору підсвічується.
        private readonly System.Collections.Generic.Dictionary<string, int> _seen = new System.Collections.Generic.Dictionary<string, int>();
        private readonly System.Collections.Generic.Dictionary<string, int> _delta = new System.Collections.Generic.Dictionary<string, int>();

        public PromoScreenView(RingUiContext ctx, PromoRunner runner) : base(ctx)
        {
            _m = new PromoScreenModel(runner, ctx.Text);
            _feud = new FeudScreenModel(ctx.Campaign, ctx.Text);
        }

        public override void Refresh()
        {
            var t = Ctx.Text;
            Root.Clear();
            RingUi.HudPad(Root);
            Root.style.justifyContent = Justify.SpaceBetween;

            // Шапка промо: назва й місце зліва, шкали ворожнечі справа — видно, що кожен вибір щось змінює.
            var top = RingUi.Row();
            top.style.justifyContent = Justify.SpaceBetween;
            top.style.alignItems = Align.FlexStart;
            var head = RingUi.Column();
            head.Add(RingUi.Lbl(t.Format("promo.act", _m.Act), 17f, RingUi.Dim));
            head.Add(RingUi.Title(_m.TitleText));
            head.Add(RingUi.Lbl(_m.PlaceText, 18f, RingUi.Dim));
            top.Add(head);
            top.Add(BuildStatus());
            Root.Add(top);

            // Вікно діалогу.
            var box = RingUi.Card(16f);
            box.style.width = new Length(72f, LengthUnit.Percent);
            box.style.maxWidth = 1100f;
            box.style.minWidth = 560f;
            box.style.alignSelf = Align.Center;
            box.style.borderTopWidth = 2f;
            box.style.borderTopColor = RingUi.Gold;
            box.style.backgroundColor = new Color(0.05f, 0.045f, 0.06f, 0.94f);
            Root.Add(box);

            var kind = _m.Kind;
            if (kind == PromoViewKind.Line)
            {
                _lastSpeaker = _m.SpeakerText;
                _lastLine = _m.LineText;
                _lastNarrator = _m.IsNarrator;
            }
            bool context = kind != PromoViewKind.Line;
            if (_lastLine.Length > 0)
            {
                if (_lastSpeaker.Length > 0) box.Add(RingUi.Lbl(_lastSpeaker, 20f, context ? RingUi.Dim : RingUi.Gold, true));
                var line = RingUi.Lbl(_lastLine, context ? 17f : 20f, context ? RingUi.Dim : RingUi.Text);
                if (_lastNarrator) line.style.unityFontStyleAndWeight = FontStyle.Italic;
                RingUi.Gap(line, 12f);
                box.Add(line);
            }

            switch (kind)
            {
                case PromoViewKind.Line:
                    box.Add(RingUi.PrimaryButton(t.Get("ui.next"), OnConfirm));
                    break;
                case PromoViewKind.Choice:
                    BuildChoices(box);
                    break;
                case PromoViewKind.Check:
                    BuildCheck(box);
                    break;
                case PromoViewKind.Rewards:
                    BuildRoll(box);
                    BuildRewards(box);
                    break;
                case PromoViewKind.Result:
                    BuildRoll(box);
                    box.Add(RingUi.PrimaryButton(t.Get("ui.next"), OnConfirm));
                    break;
                case PromoViewKind.Finished:
                    box.Add(RingUi.PrimaryButton(t.Get("ui.next"), OnConfirm));
                    break;
            }
            box.Add(RingUi.Lbl(t.Get("ui.hint.keys"), 14f, RingUi.Dim));
        }

        // ---------- шкали ворожнечі ----------

        private VisualElement BuildStatus()
        {
            var meters = _feud.Meters();
            var paths = _feud.Paths();
            var now = new System.Collections.Generic.Dictionary<string, int>();
            foreach (var r in meters) now[r.Id] = r.Value;
            foreach (var p in paths) now["path." + p.Path] = p.Points;
            // Підсвічується лише остання зміна: щойно щось змінилось, старі дельти скидаються.
            bool anyChange = false;
            foreach (var kv in now)
            {
                int before;
                if (_seen.TryGetValue(kv.Key, out before) && before != kv.Value)
                {
                    if (!anyChange) { _delta.Clear(); anyChange = true; }
                    _delta[kv.Key] = kv.Value - before;
                }
            }
            foreach (var kv in now) _seen[kv.Key] = kv.Value;

            var card = RingUi.Card(10f);
            card.style.minWidth = 300f;
            foreach (var r in meters) card.Add(StatusLine(r.Id, r.Name, r.ValueText, null));
            foreach (var p in paths) card.Add(StatusLine("path." + p.Path, p.Name, p.RankText, RingUi.PathColor(p.Path)));
            return card;
        }

        private VisualElement StatusLine(string id, string name, string valueText, Color? nameColor)
        {
            int d;
            bool changed = _delta.TryGetValue(id, out d) && d != 0;
            var row = RingUi.Row();
            row.style.justifyContent = Justify.SpaceBetween;
            row.Add(RingUi.Lbl(name, 15f, nameColor ?? RingUi.Dim));
            var right = RingUi.Row();
            if (changed) right.Add(RingUi.Lbl(RingChances.Signed(d) + "  ", 15f, d > 0 ? RingUi.Good : RingUi.Bad, true));
            right.Add(RingUi.Lbl(valueText, 15f, changed ? RingUi.Text : RingUi.Dim, changed));
            row.Add(right);
            return row;
        }

        // ---------- вибір реплік ----------

        private void BuildChoices(VisualElement box)
        {
            foreach (var r in _m.Choices())
            {
                string mark = r.HasPath ? "[" + r.PathMark + "] " : "";
                string text = (r.Locked ? "·  " : r.Number + ". ") + mark + r.Text;
                if (r.Locked) text += "\n" + r.ReasonText;
                string id = r.Id;
                var b = RingUi.MakeButton(text, () => { _m.Choose(id); Refresh(); }, !r.Locked,
                    r.HasPath ? RingUi.PathColor(r.Path) : (Color?)null, 18f);
                box.Add(b);
            }
        }

        // ---------- кидок ----------

        private void BuildCheck(VisualElement box)
        {
            var t = Ctx.Text;
            box.Add(RingUi.Heading(t.Format("promo.check.title", _m.CheckAttrText, _m.CheckModifierText)));
            var p = _m.ChancePercents();
            box.Add(RingUi.Lbl(t.Get("promo.check.chances"), 15f, RingUi.Dim));
            var row = RingUi.Row();
            RingUi.Gap(row, 8f);
            row.Add(Chance(t.Format("promo.chance.miss", p[0]), p[0], RingUi.Bad));
            row.Add(Chance(t.Format("promo.chance.partial", p[1]), p[1], RingUi.Warn));
            row.Add(Chance(t.Format("promo.chance.strong", p[2]), p[2], RingUi.Good));
            box.Add(row);
            box.Add(RingUi.PrimaryButton(t.Get("ui.roll"), OnConfirm));
        }

        private static VisualElement Chance(string text, int percent, Color color)
        {
            var e = RingUi.Column();
            RingUi.Grow(e, 1f);
            e.style.marginRight = 10f;
            e.Add(RingUi.Lbl(text, 17f, color, true));
            e.Add(RingUi.Bar(percent / 100f, color, 8f));
            return e;
        }

        private void BuildRoll(VisualElement box)
        {
            var res = _m.Result;
            if (res == null) return;
            var t = Ctx.Text;
            var row = RingUi.Row();
            row.style.alignItems = Align.Center;
            RingUi.Gap(row, 8f);
            row.Add(Die(res.Die1));
            row.Add(Die(res.Die2));
            var info = RingUi.Column();
            info.style.marginLeft = 12f;
            info.Add(RingUi.Lbl(res.TotalText, 18f));
            info.Add(RingUi.Lbl(res.TierText, 22f, RingUi.TierColor(res.Tier), true));
            row.Add(info);
            box.Add(row);
        }

        private static VisualElement Die(int value)
        {
            var d = RingUi.Column();
            d.style.width = 48f;
            d.style.height = 48f;
            d.style.marginRight = 8f;
            d.style.justifyContent = Justify.Center;
            d.style.alignItems = Align.Center;
            d.style.backgroundColor = RingUi.Text;
            d.style.borderTopLeftRadius = 8f; d.style.borderTopRightRadius = 8f;
            d.style.borderBottomLeftRadius = 8f; d.style.borderBottomRightRadius = 8f;
            d.Add(RingUi.Lbl(value.ToString(), 28f, new Color(0.08f, 0.07f, 0.08f, 1f), true));
            return d;
        }

        // ---------- нагороди ----------

        private void BuildRewards(VisualElement box)
        {
            var t = Ctx.Text;
            box.Add(RingUi.Heading(t.Get("promo.rewards.title")));
            box.Add(RingUi.Lbl(t.Format("promo.rewards.count", _m.RewardsPicked, _m.RewardsToPick), 16f, RingUi.Dim));
            foreach (var r in _m.Rewards())
            {
                string id = r.Id;
                string text = (r.Selected ? "[x] " : "[  ] ") + r.Number + ". " + r.Text;
                var b = RingUi.MakeButton(text, () => { _m.ToggleReward(id); Refresh(); }, true,
                    r.Selected ? RingUi.Good : RingUi.Border, 18f);
                box.Add(b);
            }
            box.Add(RingUi.PrimaryButton(t.Get("ui.confirm"), OnConfirm, _m.CanConfirmRewards));
        }

        // ---------- клавіші ----------

        public override void OnDigit(int n)
        {
            switch (_m.Kind)
            {
                case PromoViewKind.Choice:
                    if (_m.ChooseNumber(n)) Refresh();
                    break;
                case PromoViewKind.Rewards:
                    if (_m.ToggleRewardNumber(n)) Refresh();
                    break;
            }
        }

        public override void OnConfirm()
        {
            switch (_m.Kind)
            {
                case PromoViewKind.Line: _m.Advance(); break;
                case PromoViewKind.Check: _m.Roll(Ctx.Campaign.Dice); break;
                case PromoViewKind.Rewards:
                    if (!_m.CanConfirmRewards) return;
                    _m.ConfirmRewards();
                    break;
                case PromoViewKind.Result: _m.AcknowledgeResult(); break;
                case PromoViewKind.Finished:
                    Ctx.Campaign.FinishPromo();
                    return;
                default: return;
            }
            Refresh();
        }
    }
}
