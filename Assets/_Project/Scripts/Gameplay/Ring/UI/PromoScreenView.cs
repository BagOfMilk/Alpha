using Game.Core.Ring;
using Game.Gameplay.Ring.Models;
using Game.Gameplay.Ring.Stage;
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
            Root.style.backgroundColor = RingStageHub.Has3D ? Color.clear : RingUi.Bg;
            RingUi.HudPad(Root);
            Root.style.justifyContent = Justify.SpaceBetween;

            // Шапка промо: назва й місце зліва, шкали ворожнечі справа — видно, що кожен вибір щось змінює.
            var top = RingUi.Row();
            top.style.justifyContent = Justify.SpaceBetween;
            top.style.alignItems = Align.FlexStart;
            var head = RingUi.Column();
            head.Add(RingUi.Lbl(t.Format("promo.act", _m.Act), 12f, RingUi.Dim));
            head.Add(RingUi.Lbl(_m.TitleText, 18f, RingUi.Gold, true));
            head.Add(RingUi.Lbl(_m.PlaceText, 12f, RingUi.Dim));
            // Назва на напівпрозорій плашці в куті: не налазить на ринг у кадрі (3D під UI).
            if (RingStageHub.Has3D)
            {
                head.style.backgroundColor = RingTheme.WithAlpha(RingTheme.Bg, BrandTokens.AlphaGlass);
                head.style.paddingLeft = head.style.paddingRight = 12f;
                head.style.paddingTop = head.style.paddingBottom = 8f;
                head.style.borderTopLeftRadius = head.style.borderTopRightRadius =
                    head.style.borderBottomLeftRadius = head.style.borderBottomRightRadius = 6f;
                head.style.maxWidth = 330f;
                head.style.flexShrink = 1f;
            }
            top.Add(head);
            top.Add(BuildStatus());
            Root.Add(top);

            // Вікно діалогу.
            var box = RingUi.Card(16f);
            box.style.width = new Length(72f, LengthUnit.Percent);
            box.style.maxWidth = 1100f;
            box.style.minWidth = 560f;
            box.style.alignSelf = Align.Center;
            box.style.backgroundColor = RingTheme.WithAlpha(RingTheme.Bg, RingStageHub.Has3D ? BrandTokens.AlphaGlass : BrandTokens.AlphaPlate);
            Root.Add(box);

            var kind = _m.Kind;
            if (kind == PromoViewKind.Line)
            {
                _lastSpeaker = _m.SpeakerText;
                _lastLine = _m.LineText;
                _lastNarrator = _m.IsNarrator;
                // Камера діалогу: мовець вузла промо -> постать на рингу.
                var stage = RingStageHub.Current;
                if (stage != null) stage.SetPromoSpeaker(_m.SpeakerId);
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

        // ---------- шкали ворожнечі: компактно в кутку, зміна — коротким «+1 Популярність» ----------

        private sealed class Toast
        {
            public string Text;
            public Color Color;
            public float Left;
        }

        private readonly System.Collections.Generic.List<Toast> _toasts = new System.Collections.Generic.List<Toast>();
        private VisualElement _toastHost;
        private const float ToastSeconds = 2.4f;

        public override void Tick(float dt)
        {
            if (_toasts.Count == 0) return;
            for (int i = _toasts.Count - 1; i >= 0; i--)
            {
                _toasts[i].Left -= dt;
                if (_toasts[i].Left <= 0f) _toasts.RemoveAt(i);
            }
            RebuildToasts();
        }

        private void RebuildToasts()
        {
            if (_toastHost == null) return;
            _toastHost.Clear();
            foreach (var tt in _toasts)
            {
                var l = RingUi.Lbl(tt.Text, 17f, tt.Color, true);
                l.style.opacity = Mathf.Clamp01(tt.Left / 0.7f);
                l.style.marginTop = 3f;
                l.style.unityTextAlign = TextAnchor.MiddleRight;
                _toastHost.Add(l);
            }
        }

        private VisualElement BuildStatus()
        {
            var meters = _feud.Meters();
            var paths = _feud.Paths();
            var now = new System.Collections.Generic.Dictionary<string, int>();
            var names = new System.Collections.Generic.Dictionary<string, string>();
            var colors = new System.Collections.Generic.Dictionary<string, Color>();
            foreach (var r in meters) { now[r.Id] = r.Value; names[r.Id] = r.Name; colors[r.Id] = RingUi.Gold; }
            foreach (var p in paths) { now["path." + p.Path] = p.Points; names["path." + p.Path] = p.Name; colors["path." + p.Path] = RingUi.PathColor(p.Path); }
            // Зміна від попереднього малювання — тост, що гасне сам.
            foreach (var kv in now)
            {
                int before;
                if (_seen.TryGetValue(kv.Key, out before) && before != kv.Value)
                {
                    int d = kv.Value - before;
                    _toasts.Add(new Toast { Text = RingChances.Signed(d) + " " + names[kv.Key], Color = d > 0 ? RingUi.Good : RingUi.Bad, Left = ToastSeconds });
                }
            }
            foreach (var kv in now) _seen[kv.Key] = kv.Value;

            var col = RingUi.Column();
            col.style.alignItems = Align.FlexEnd;
            col.pickingMode = PickingMode.Ignore;
            var chips = RingUi.Row();
            chips.style.flexWrap = Wrap.Wrap;
            chips.style.justifyContent = Justify.FlexEnd;
            chips.style.maxWidth = 620f;
            // Під час промо — лише числа (шкали з максимумом і ранги — на екрані ворожнечі): менше тексту поверх сцени.
            foreach (var r in meters) chips.Add(Chip(r.Name, r.Min < 0 ? RingChances.Signed(r.Value) : r.Value.ToString(), RingUi.Dim));
            col.Add(chips);
            var pathChip = RingUi.Row();
            StyleChip(pathChip);
            foreach (var p in paths)
            {
                var mk = RingUi.Lbl(p.Name, 13f, RingUi.PathColor(p.Path), p.Dominant);
                mk.style.marginRight = 4f;
                pathChip.Add(mk);
                var pt = RingUi.Lbl(p.Points.ToString(), 13f, RingUi.Text, true);
                pt.style.marginRight = 12f;
                pathChip.Add(pt);
            }
            col.Add(pathChip);
            _toastHost = RingUi.Column();
            _toastHost.style.alignItems = Align.FlexEnd;
            _toastHost.pickingMode = PickingMode.Ignore;
            col.Add(_toastHost);
            RebuildToasts();
            return col;
        }

        private static void StyleChip(VisualElement c)
        {
            c.style.backgroundColor = RingTheme.WithAlpha(RingTheme.Panel, BrandTokens.AlphaUnderlay);
            c.style.borderTopLeftRadius = 10f; c.style.borderTopRightRadius = 10f;
            c.style.borderBottomLeftRadius = 10f; c.style.borderBottomRightRadius = 10f;
            c.style.paddingLeft = 9f; c.style.paddingRight = 9f; c.style.paddingTop = 3f; c.style.paddingBottom = 3f;
            c.style.marginLeft = 5f; c.style.marginBottom = 5f;
        }

        private static VisualElement Chip(string name, string value, Color nameColor)
        {
            var c = RingUi.Row();
            StyleChip(c);
            c.Add(RingUi.Lbl(name + " ", 13f, nameColor));
            c.Add(RingUi.Lbl(value, 13f, RingUi.Text, true));
            return c;
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
            d.Add(RingUi.Lbl(value.ToString(), 28f, RingTheme.Bg, true));
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
                case PromoViewKind.Check: _m.Roll(Ctx.PromoDice ?? Ctx.Campaign.Dice); break;
                case PromoViewKind.Rewards:
                    if (!_m.CanConfirmRewards) return;
                    _m.ConfirmRewards();
                    break;
                case PromoViewKind.Result: _m.AcknowledgeResult(); break;
                case PromoViewKind.Finished:
                    // Сцена закулісся кар'єри закривається своїм методом (наслідки, витрачена дія тижня).
                    if (Ctx.FinishPromo != null) Ctx.FinishPromo(); else Ctx.Campaign.FinishPromo();
                    return;
                default: return;
            }
            Refresh();
        }
    }
}
