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

        protected CareerScreenView(RingUiContext ctx) : base(ctx)
        {
            if (RingStageHub.Has3D) Root.style.backgroundColor = Color.clear;
        }

        protected VisualElement Glass(float pad = 14f)
        {
            var c = RingUi.Card(pad);
            RingUi.Glass(c, 0.86f);
            return c;
        }

        protected static Label Chip(string text, Color color)
        {
            var l = RingUi.Lbl(text, 16f, color, true);
            l.style.backgroundColor = new Color(0.08f, 0.07f, 0.09f, 0.82f);
            l.style.paddingLeft = 10f; l.style.paddingRight = 10f; l.style.paddingTop = 4f; l.style.paddingBottom = 4f;
            l.style.marginRight = 8f; l.style.marginBottom = 6f;
            l.style.borderTopLeftRadius = 4f; l.style.borderTopRightRadius = 4f;
            l.style.borderBottomLeftRadius = 4f; l.style.borderBottomRightRadius = 4f;
            return l;
        }

        protected string NpcName(string id)
        {
            var n = C.Content.FindNpc(id);
            return n != null ? T.Get(n.NameKey) : id;
        }

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

        /// <summary>Шапка кар'єри: ім'я, сезон/тиждень, гроші, популярність, довіра, очки розвитку, пояс.</summary>
        protected VisualElement Header()
        {
            var col = RingUi.Column();
            col.style.marginBottom = 10f;
            var v = C.GetCareerView();
            var title = RingUi.Row();
            title.style.alignItems = Align.FlexEnd;
            title.Add(RingUi.Lbl(v.RingName, 30f, RingUi.Gold, true));
            var sub = RingUi.Lbl("  " + T.Get("gimmick." + C.Content.GimmickOf(v.Gimmick).Key) + " · " + RingLabels.PathName(T, v.Role)
                + (v.IsChampion ? " · " + T.Get("c.champion") : ""), 17f, RingUi.Dim);
            sub.style.marginBottom = 4f;
            title.Add(sub);
            col.Add(title);
            var chips = RingUi.Row();
            chips.style.flexWrap = Wrap.Wrap;
            chips.style.marginTop = 4f;
            chips.Add(Chip(T.Format("c.season_week", v.Season, v.Week, CareerRules.WeeksPerSeason), RingUi.Text));
            chips.Add(Chip(T.Format("c.money", v.Money), RingUi.Gold));
            chips.Add(Chip(T.Format("c.popularity", v.Popularity, CareerRules.MaxPopularity), RingUi.Face));
            chips.Add(Chip(T.Format("c.trust", v.Trust, CareerRules.MaxTrust), RingUi.Text));
            chips.Add(Chip(T.Format("c.devpoints", v.DevPoints), RingUi.Good));
            col.Add(chips);
            col.Add(RingUi.Lbl(InjuryText(v.Injuries), 15f, RingUi.Dim));
            return col;
        }

        /// <summary>Розвиток: атрибути, нові прийоми, перки (тиждень і кінець сезону).</summary>
        protected VisualElement DevelopmentPanel(Action changed)
        {
            var card = Glass(12f);
            card.Add(RingUi.Heading(T.Format("dev.points", C.DevPoints)));
            var attrs = RingUi.Row();
            attrs.style.flexWrap = Wrap.Wrap;
            for (int i = 0; i < 5; i++)
            {
                var a = (RingAttr)i;
                string reason;
                bool can = C.CanBuyAttr(a, out reason);
                var b = RingUi.MakeButton(RingLabels.AttrName(T, a) + " " + Signed(C.Player.Get(a)) + "   " + T.Format("dev.attr.buy", Development.AttrCost),
                    () => { C.BuyAttr(a); changed(); }, can, null, 15f);
                b.style.marginRight = 6f;
                attrs.Add(b);
            }
            card.Add(attrs);
            var moves = RingUi.Heading(T.Get("c.dev.moves"));
            moves.style.marginTop = 6f;
            card.Add(moves);
            var mrow = RingUi.Row();
            mrow.style.flexWrap = Wrap.Wrap;
            foreach (var m in C.Content.Moves.All)
            {
                if (!m.IsLearnable || C.KnowsMove(m.Id)) continue;
                if (m.GimmickId != null && m.GimmickId != C.Content.GimmickOf(C.State.Gimmick).Key) continue;
                string reason;
                bool can = C.CanLearnMove(m.Id, out reason);
                string id = m.Id;
                var b = RingUi.MakeButton(T.Get(m.NameKey) + " · " + T.Format("dev.cost", m.Cost), () => { C.LearnMove(id); changed(); }, can, RingUi.Warn, 15f);
                b.style.marginRight = 6f;
                mrow.Add(b);
            }
            card.Add(mrow);
            var perks = RingUi.Heading(T.Get("dev.section.perks"));
            perks.style.marginTop = 6f;
            card.Add(perks);
            var prow = RingUi.Row();
            prow.style.flexWrap = Wrap.Wrap;
            foreach (var p in PerkCatalog.All)
            {
                if (C.Feud.State.Perks.Contains(p.Id)) continue;
                string reason;
                bool can = C.CanBuyPerk(p.Id, out reason);
                string id = p.Id;
                var b = RingUi.MakeButton(RingLabels.PerkName(T, id) + " · " + T.Format("dev.cost", p.Cost) + "\n" + RingLabels.PerkDesc(T, id),
                    () => { C.BuyPerk(id); changed(); }, can, p.Path.HasValue ? RingUi.PathColor(p.Path.Value) : RingUi.Gold, 14f);
                b.style.marginRight = 6f;
                b.style.maxWidth = 300f;
                prow.Add(b);
            }
            card.Add(prow);
            return card;
        }

        protected static string Signed(int v) { return v > 0 ? "+" + v : v.ToString(); }
    }

    // ============================ титул ============================

    /// <summary>Головне меню після вступу: продовжити, нова кар'єра, повний екран, вихід. Позаду — зала з титантроном.</summary>
    public sealed class TitleScreenView : CareerScreenView
    {
        public TitleScreenView(RingUiContext ctx) : base(ctx) { }

        public override void Refresh()
        {
            Root.Clear();
            RingUi.HudPad(Root);
            Root.style.justifyContent = Justify.FlexEnd;
            var box = Glass(20f);
            box.style.maxWidth = 460f;
            box.Add(RingUi.Lbl(T.Get("c.game.title"), 44f, RingUi.Gold, true));
            box.Add(RingUi.Lbl(T.Get("c.game.sub"), 17f, RingUi.Dim));
            var gap = new VisualElement(); gap.style.height = 14f; box.Add(gap);
            int n = 1;
            if (Host.HasSave) box.Add(RingUi.MakeButton(n++ + ". " + T.Get("c.menu.continue"), Host.ContinueGame, true, RingUi.Gold, 20f));
            box.Add(RingUi.MakeButton(n++ + ". " + T.Get("c.menu.new"), Host.NewGame, true, RingUi.Good, 20f));
            box.Add(RingUi.MakeButton(n++ + ". " + T.Get("c.menu.fullscreen"), Host.ToggleFullscreen, true, RingUi.Dim, 18f));
            box.Add(RingUi.MakeButton(n + ". " + T.Get("c.menu.quit"), Host.Quit, true, RingUi.Bad, 18f));
            Root.Add(box);
        }

        public override void OnDigit(int n)
        {
            if (!Host.HasSave) n++;
            switch (n)
            {
                case 1: Host.ContinueGame(); break;
                case 2: Host.NewGame(); break;
                case 3: Host.ToggleFullscreen(); break;
                case 4: Host.Quit(); break;
            }
        }

        public override void OnConfirm() { if (Host.HasSave) Host.ContinueGame(); else Host.NewGame(); }
    }

    // ============================ створення ============================

    /// <summary>Створення борця (TRPG_DESIGN §5.1): ґімік, роль, +1 вільний бал, ім'я на рингу, колір костюма, тіло й зачіска.</summary>
    public sealed class CreateScreenView : CareerScreenView
    {
        private readonly CharacterDraft _d = new CharacterDraft { RingName = "", FreePoint = RingAttr.Strength };
        private readonly CreationView _v;

        public CreateScreenView(RingUiContext ctx) : base(ctx)
        {
            _v = C.GetCreationView();
            _d.RingName = T.Get("c.create.default_name");
            _d.Gimmick = Gimmick.Monster;
            _d.FreePoint = RingAttr.Stamina;
        }

        private GimmickView Current { get { foreach (var g in _v.Gimmicks) if (g.Def.Id == _d.Gimmick) return g; return _v.Gimmicks[0]; } }

        public override void Refresh()
        {
            Root.Clear();
            RingUi.HudPad(Root);
            Root.Add(RingUi.Title(T.Get("career.stage.1")));
            var body = RingUi.Row();
            RingUi.Grow(body, 1f);

            // Ліворуч: ґімік і роль.
            var left = Glass();
            RingUi.Grow(left, 1f);
            left.style.marginRight = 12f;
            left.Add(RingUi.Heading(T.Get("c.create.gimmick")));
            foreach (var g in _v.Gimmicks)
            {
                var gid = g.Def.Id;
                bool sel = gid == _d.Gimmick;
                string attrs = AttrLine(g.BaseAttr);
                left.Add(RingUi.MakeButton((sel ? "▸ " : "") + T.Get(g.Def.NameKey) + "   " + attrs + "\n" + T.Get(g.Def.NameKey + ".desc"),
                    () => { _d.Gimmick = gid; Refresh(); }, true, sel ? RingUi.Gold : RingUi.Border, 16f));
            }
            var roleH = RingUi.Heading(T.Get("c.create.role"));
            roleH.style.marginTop = 8f;
            left.Add(roleH);
            var roles = RingUi.Row();
            foreach (var r in new[] { CareerRole.Hero, CareerRole.Villain })
            {
                var rr = r;
                bool sel = r == _d.Role;
                string key = r == CareerRole.Hero ? "role.hero" : "role.villain";
                var b = RingUi.MakeButton((sel ? "▸ " : "") + T.Get(key) + "\n" + T.Get(key + ".desc"), () => { _d.Role = rr; Refresh(); }, true,
                    r == CareerRole.Hero ? RingUi.Face : RingUi.Heel, 15f);
                RingUi.Grow(b, 1f);
                b.style.marginRight = 6f;
                roles.Add(b);
            }
            left.Add(roles);
            body.Add(left);

            // Праворуч: бал, ім'я, вигляд, почати.
            var right = Glass();
            RingUi.Grow(right, 1f);
            right.Add(RingUi.Heading(T.Get("c.create.freepoint")));
            var pts = RingUi.Row();
            pts.style.flexWrap = Wrap.Wrap;
            var baseAttr = Current.BaseAttr;
            for (int i = 0; i < 5; i++)
            {
                var a = (RingAttr)i;
                bool sel = a == _d.FreePoint;
                bool ok = baseAttr[i] + 1 <= _v.MaxAttr;
                var b = RingUi.MakeButton((sel ? "▸ " : "") + RingLabels.AttrName(T, a) + " " + Signed(baseAttr[i] + (sel ? 1 : 0)),
                    () => { _d.FreePoint = a; Refresh(); }, ok, sel ? RingUi.Good : RingUi.Border, 15f);
                b.style.marginRight = 6f;
                pts.Add(b);
            }
            right.Add(pts);

            var nameH = RingUi.Heading(T.Get("c.create.name"));
            nameH.style.marginTop = 8f;
            right.Add(nameH);
            var field = new TextField { value = _d.RingName, maxLength = _v.MaxNameLength };
            field.style.fontSize = 18f;
            field.style.color = new Color(0.08f, 0.07f, 0.09f);
            field.RegisterValueChangedCallback(e => _d.RingName = e.newValue);
            right.Add(field);

            var costH = RingUi.Heading(T.Get("c.create.costume"));
            costH.style.marginTop = 8f;
            right.Add(costH);
            var sw = RingUi.Row();
            for (int i = 0; i < _v.CostumeCount; i++)
            {
                int ci = i;
                var s = new Button(() => { _d.CostumeColor = ci; Refresh(); }) { text = "" };
                int rgb = CareerLooks.CostumeRgb(i);
                s.style.width = 34f; s.style.height = 34f;
                s.style.marginRight = 6f;
                s.style.backgroundColor = new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);
                float bw = i == _d.CostumeColor ? 3f : 1f;
                s.style.borderTopWidth = bw; s.style.borderBottomWidth = bw; s.style.borderLeftWidth = bw; s.style.borderRightWidth = bw;
                var bc = i == _d.CostumeColor ? RingUi.Gold : RingUi.Border;
                s.style.borderTopColor = bc; s.style.borderBottomColor = bc; s.style.borderLeftColor = bc; s.style.borderRightColor = bc;
                sw.Add(s);
            }
            right.Add(sw);

            var lookH = RingUi.Heading(T.Get("c.create.look"));
            lookH.style.marginTop = 8f;
            right.Add(lookH);
            right.Add(Cycler(T.Get("c.create.body") + ": " + BodyName(CareerLooks.HeroBodies[Host.HeroBody]),
                d => Host.HeroBody = (Host.HeroBody + d + CareerLooks.HeroBodies.Length) % CareerLooks.HeroBodies.Length));
            right.Add(Cycler(T.Get("c.create.hair") + ": " + HairName(CareerLooks.HeroHair[Host.HeroHair]),
                d => Host.HeroHair = (Host.HeroHair + d + CareerLooks.HeroHair.Length) % CareerLooks.HeroHair.Length));

            var movesH = RingUi.Heading(T.Get("c.create.moves"));
            movesH.style.marginTop = 8f;
            right.Add(movesH);
            var names = new List<string>();
            foreach (var m in Current.StartMoves) names.Add(T.Get(m.NameKey));
            right.Add(RingUi.Lbl(string.Join(", ", names), 14f, RingUi.Dim));

            string reason;
            bool can = C.CanCreate(_d, out reason);
            right.Add(RingUi.PrimaryButton(T.Get("c.create.start"), OnConfirm, can));
            if (!can) right.Add(RingUi.Lbl(T.Get("create.reason." + reason), 14f, RingUi.Bad));
            body.Add(right);
            Root.Add(body);
            ApplyLook();
        }

        private VisualElement Cycler(string text, Action<int> step)
        {
            var row = RingUi.Row();
            row.style.alignItems = Align.Center;
            var l = RingUi.MakeButton("◀", () => { step(-1); Refresh(); }, true, RingUi.Border, 15f);
            var r = RingUi.MakeButton("▶", () => { step(1); Refresh(); }, true, RingUi.Border, 15f);
            var lbl = RingUi.Lbl(text, 16f);
            lbl.style.marginLeft = 8f; lbl.style.marginRight = 8f;
            RingUi.Grow(lbl, 1f);
            row.Add(l); row.Add(lbl); row.Add(r);
            return row;
        }

        private string BodyName(string body)
        {
            string sex = body.StartsWith("body_f_") ? "f" : "m";
            string culture = body.Substring(7);
            return T.Get("c.look.sex." + sex) + ", " + T.Get("c.look.culture." + culture);
        }

        private string HairName(string hair)
        {
            return string.IsNullOrEmpty(hair) ? T.Get("c.look.hair.none") : T.Get("c.look.hair." + hair);
        }

        private string AttrLine(int[] a)
        {
            var parts = new List<string>();
            for (int i = 0; i < a.Length; i++) if (a[i] != 0) parts.Add(RingLabels.AttrName(T, (RingAttr)i) + " " + Signed(a[i]));
            return string.Join(", ", parts);
        }

        /// <summary>Показати героя на рингу таким, яким його збирають (тіло, зачіска, колір костюма).</summary>
        private void ApplyLook()
        {
            var stage = RingStageHub.Current;
            if (stage == null) return;
            var hammer = CareerLooks.ForNpc(CareerContent.HammerId);
            stage.SetWrestlers(CareerLooks.HeroBodies[Host.HeroBody], CareerLooks.CostumeRgb(_d.CostumeColor), CareerLooks.HeroHair[Host.HeroHair],
                hammer.Body, hammer.Rgb, hammer.Hair, hammer.Scale);
        }

        public override void OnDigit(int n)
        {
            if (n >= 1 && n <= 4) { _d.Gimmick = (Gimmick)(n - 1); Refresh(); }
        }

        public override void OnConfirm()
        {
            string reason;
            if (!C.CanCreate(_d, out reason)) return;
            C.CreateHero(_d);
            Host.Save();
        }
    }

    // ============================ тиждень ============================

    /// <summary>
    /// Тиждень (§6.1): три дії картками, результат останньої, картка вечора (суперник, місце, план, умови, гонорар), розвиток і
    /// «Перейти до вечора». Позаду — закулісся (роздягальня з букером).
    /// </summary>
    public sealed class WeekScreenView : CareerScreenView
    {
        private bool _dev;

        public WeekScreenView(RingUiContext ctx) : base(ctx) { }

        public override void Refresh()
        {
            Root.Clear();
            RingUi.HudPad(Root);
            Root.Add(Header());
            var w = C.GetWeekView();
            var body = RingUi.Row();
            RingUi.Grow(body, 1f);

            var left = RingUi.Column();
            RingUi.Grow(left, 1.1f);
            left.style.marginRight = 12f;
            var acts = Glass();
            acts.Add(RingUi.Heading(T.Format("c.week.actions", w.ActionsLeft, w.ActionsTotal)));
            if (w.Strained) acts.Add(RingUi.Lbl(T.Get("week.strained"), 14f, RingUi.Bad));
            if (w.PromoChoicePending)
            {
                acts.Add(RingUi.Lbl(T.Get("c.week.promo_choice"), 16f, RingUi.Warn, true));
                acts.Add(RingUi.MakeButton("1. " + T.Get("week.promo.reward.0"), () => Do(C.ChoosePromoReward(WeekPromoReward.Feud)), true, RingUi.Heel, 16f));
                acts.Add(RingUi.MakeButton("2. " + T.Get("week.promo.reward.1"), () => Do(C.ChoosePromoReward(WeekPromoReward.Hype)), true, RingUi.Warn, 16f));
            }
            else
            {
                bool any = w.ActionsLeft > 0;
                acts.Add(ActionBtn(1, "week.action.0", () => C.Train(false), any));
                acts.Add(ActionBtn(2, "c.week.train_coach", () => C.Train(true), any && w.Money >= CareerRules.TrainerCost));
                acts.Add(ActionBtn(3, "week.action.1", () => C.Recover(WorstZone(w), false), any && w.CanRecover));
                acts.Add(ActionBtn(4, "week.action.2", C.Promo, any));
                acts.Add(ActionBtn(5, "week.action.3", C.Backstage, any));
                acts.Add(ActionBtn(6, "week.action.4", C.SideJob, any));
            }
            left.Add(acts);
            var last = Host.LastWeekResult;
            if (last != null)
            {
                var res = Glass(10f);
                res.style.marginTop = 8f;
                res.Add(RingUi.Lbl(ResultText(last), 16f, last.Ok ? RingUi.Text : RingUi.Bad));
                left.Add(res);
            }
            body.Add(left);

            var right = RingUi.Column();
            RingUi.Grow(right, 1f);
            right.Add(CardPanel(w.Card, w.IsBigShowWeek));
            var dev = RingUi.MakeButton((_dev ? "▾ " : "▸ ") + T.Get("dev.title") + " · " + T.Format("dev.points", w.DevPoints), () => { _dev = !_dev; Refresh(); }, true, RingUi.Good, 16f);
            dev.style.marginTop = 8f;
            right.Add(dev);
            if (_dev) right.Add(DevelopmentPanel(Refresh));
            right.Add(RingUi.PrimaryButton(T.Get("week.end"), OnConfirm, !w.PromoChoicePending));
            body.Add(right);
            Root.Add(body);
        }

        private Button ActionBtn(int n, string key, Func<WeekActionResult> act, bool enabled)
        {
            string desc = key.StartsWith("week.action.") ? T.Get(key + ".desc") : T.Get(key + ".desc");
            return RingUi.MakeButton(n + ". " + T.Get(key) + "\n" + desc, () => Do(act()), enabled, RingUi.Gold, 15f);
        }

        private static BodyZone WorstZone(WeekView w)
        {
            BodyZone best = BodyZone.Body; int score = -1;
            foreach (var i in w.Injuries)
            {
                int s = i.WeeksLeft * 10 + i.ZoneDamage;
                if (s > score) { score = s; best = i.Zone; }
            }
            return best;
        }

        private void Do(WeekActionResult r)
        {
            Host.LastWeekResult = r;
            Host.Save();
            Refresh();
        }

        private string ResultText(WeekActionResult r)
        {
            if (!r.Ok) return T.Get("week.reason." + r.Reason);
            var parts = new List<string> { T.Get("week.action." + (int)r.Kind) };
            if (r.HasRoll) parts.Add(T.Format("c.roll", r.Roll, Signed(r.Modifier), r.Total, RingLabels.TierName(T, r.Tier)));
            if (r.MoneyDelta != 0) parts.Add(T.Format("c.delta.money", Signed(r.MoneyDelta)));
            if (r.DevPointsDelta != 0) parts.Add(T.Format("c.delta.dev", Signed(r.DevPointsDelta)));
            if (r.TrustDelta != 0) parts.Add(T.Format("c.delta.trust", Signed(r.TrustDelta)));
            if (r.FeudDelta != 0) parts.Add(T.Format("c.delta.feud", Signed(r.FeudDelta)));
            if (r.HypeBonusDelta != 0) parts.Add(T.Format("c.delta.hype", Signed(r.HypeBonusDelta)));
            if (r.RivalBoosted) parts.Add(T.Get("c.delta.rival"));
            if (r.InjuredZone >= 0) parts.Add(T.Format("c.delta.injury", T.Get("bodyzone." + r.InjuredZone)));
            if (r.NeedsChoice) parts.Add(T.Get("c.week.promo_choice"));
            string s = string.Join(" · ", parts);
            if (!string.IsNullOrEmpty(r.RumorKey)) s += "\n" + T.Get(r.RumorKey);
            return s;
        }

        private VisualElement CardPanel(ShowCardView c, bool big)
        {
            var p = Glass();
            p.Add(RingUi.Heading(T.Get(c.IsTitle ? "c.card.title_match" : big ? "c.card.big" : "c.card.weekly")));
            if (c.Opponent != null)
            {
                p.Add(RingUi.Lbl(T.Format("c.card.vs", T.Get(c.Opponent.NameKey)), 20f, RingUi.Text, true));
                p.Add(RingUi.Lbl(T.Get(c.Opponent.BioKey), 14f, RingUi.Dim));
                p.Add(RingUi.Lbl(T.Get("gimmick." + C.Content.GimmickOf(c.Opponent.Gimmick).Key) + " · " + RingLabels.PathName(T, c.Opponent.Path)
                    + " · " + T.Format("c.card.feud", c.OpponentFeudLevel), 14f, RingUi.Dim));
            }
            p.Add(RingUi.Lbl(T.Get("card.pos." + (int)c.Position) + " · " + T.Format("c.card.fee", c.Fee, c.WinBonus, c.HypeBonus), 15f, RingUi.Gold));
            if (!c.IsFeud) p.Add(RingUi.Lbl(T.Get("c.card.plan") + ": " + PlanText(c.Plan), 15f));
            p.Add(RingUi.Lbl(T.Get("c.card.stip") + ": " + StipText(c.Stipulations), 15f));
            if (c.PromoHypeBonus != 0) p.Add(RingUi.Lbl(T.Format("c.card.promo_bonus", Signed(c.PromoHypeBonus)), 15f, RingUi.Warn));
            return p;
        }

        public override void OnDigit(int n)
        {
            var w = C.GetWeekView();
            if (w.PromoChoicePending)
            {
                if (n == 1) Do(C.ChoosePromoReward(WeekPromoReward.Feud));
                else if (n == 2) Do(C.ChoosePromoReward(WeekPromoReward.Hype));
                return;
            }
            if (w.ActionsLeft <= 0) return;
            switch (n)
            {
                case 1: Do(C.Train(false)); break;
                case 2: if (w.Money >= CareerRules.TrainerCost) Do(C.Train(true)); break;
                case 3: if (w.CanRecover) Do(C.Recover(WorstZone(w), false)); break;
                case 4: Do(C.Promo()); break;
                case 5: Do(C.Backstage()); break;
                case 6: Do(C.SideJob()); break;
            }
        }

        public override void OnConfirm()
        {
            if (C.State.PromoChoicePending) return;
            Host.LastWeekResult = null;
            C.EndWeek();
            Host.Save();
        }
    }

    // ============================ вечір: картка й підсумок ============================

    /// <summary>Картка вечора перед виходом: суперник, місце, план, умови; «На ринг» або «Зійти з вечора» (травмованому).</summary>
    public sealed class ShowCardScreenView : CareerScreenView
    {
        private readonly Action _start;

        public ShowCardScreenView(RingUiContext ctx, Action start) : base(ctx) { _start = start; }

        public override void Refresh()
        {
            Root.Clear();
            RingUi.HudPad(Root);
            Root.style.justifyContent = Justify.FlexEnd;
            var c = C.GetShowCard();
            var p = Glass(18f);
            p.style.maxWidth = 640f;
            p.Add(RingUi.Lbl(T.Get(c.IsTitle ? "c.card.title_match" : c.IsBig ? "c.card.big" : "c.card.weekly"), 16f, RingUi.Dim));
            if (c.Opponent != null) p.Add(RingUi.Lbl(T.Format("c.card.vs", T.Get(c.Opponent.NameKey)), 30f, RingUi.Gold, true));
            p.Add(RingUi.Lbl(T.Get("card.pos." + (int)c.Position) + " · " + T.Format("c.card.fee", c.Fee, c.WinBonus, c.HypeBonus), 16f));
            if (!c.IsFeud) p.Add(RingUi.Lbl(T.Get("c.card.plan") + ": " + PlanText(c.Plan), 16f));
            p.Add(RingUi.Lbl(T.Get("c.card.stip") + ": " + StipText(c.Stipulations), 16f));
            p.Add(RingUi.Lbl(InjuryText(c.Injuries), 15f, RingUi.Dim));
            p.Add(RingUi.PrimaryButton(T.Get(c.IsFeud ? "c.card.go_feud" : "c.card.go"), OnConfirm));
            if (c.CanWithdraw) p.Add(RingUi.MakeButton(T.Get("c.card.withdraw"), () => { C.WithdrawFromShow(); Host.Save(); }, true, RingUi.Bad, 16f));
            Root.Add(p);
        }

        public override void OnConfirm() { if (_start != null) _start(); }
    }

    /// <summary>Підсумок вечора: результат, гонорар, шкали до/після, травма, очки розвитку, план, пояс.</summary>
    public sealed class ShowResultScreenView : CareerScreenView
    {
        public ShowResultScreenView(RingUiContext ctx) : base(ctx) { }

        public override void Refresh()
        {
            Root.Clear();
            RingUi.HudPad(Root);
            Root.style.justifyContent = Justify.FlexEnd;
            var r = C.GetShowResult();
            var p = Glass(18f);
            p.style.maxWidth = 680f;
            if (r == null) { p.Add(RingUi.PrimaryButton(T.Get("ui.next"), OnConfirm)); Root.Add(p); return; }
            if (r.Withdrew) p.Add(RingUi.Lbl(T.Get("show.withdrew"), 28f, RingUi.Warn, true));
            else
            {
                string head = r.Winner == MatchWinner.Player ? "show.won" : r.Winner == MatchWinner.Rival ? "show.lost" : "show.draw";
                p.Add(RingUi.Lbl(T.Get(head) + " — " + NpcName(r.OpponentId), 28f, r.Winner == MatchWinner.Player ? RingUi.Good : RingUi.Bad, true));
                p.Add(RingUi.Lbl(RingLabels.EndKind(T, r.EndKind) + " · " + T.Format("c.result.rounds", r.Rounds) + " · " + T.Format("c.result.hype", r.Hype), 16f, RingUi.Dim));
                if (r.TitleWon) p.Add(RingUi.Lbl(T.Get("show.belt_won"), 24f, RingUi.Gold, true));
                p.Add(RingUi.Lbl(T.Format("c.result.fee", r.TotalFee, r.Fee, r.FeeWinBonus + r.FeeHypeBonus), 17f, RingUi.Gold));
                if (!r.IsTitle && !r.IsFeud) p.Add(RingUi.Lbl(RingLabels.PlanOutcome(T, r.PlanOutcome), 16f));
                if (r.DevPointsGained != 0) p.Add(RingUi.Lbl(T.Format("c.delta.dev", Signed(r.DevPointsGained)), 16f, RingUi.Good));
                if (r.InjuredZone >= 0) p.Add(RingUi.Lbl(T.Format("c.result.injury", T.Get("bodyzone." + r.InjuredZone), r.InjuryWeeks), 16f, RingUi.Bad));
            }
            p.Add(RingUi.Lbl(T.Format("c.result.pop", r.PopularityBefore, r.PopularityAfter) + " · " + T.Format("c.result.trust", r.TrustBefore, r.TrustAfter), 16f));
            p.Add(RingUi.PrimaryButton(T.Get("ui.next"), OnConfirm));
            Root.Add(p);
        }

        public override void OnConfirm()
        {
            C.ContinueFromShow();
            Host.Save();
        }
    }

    // ============================ кінець сезону ============================

    /// <summary>Підсумок сезону (§6.2, §6.4): популярність, довіра, гроші; звільнення; титульний шанс чи захист пояса; новий сезон.</summary>
    public sealed class SeasonEndScreenView : CareerScreenView
    {
        public SeasonEndScreenView(RingUiContext ctx) : base(ctx) { }

        public override void Refresh()
        {
            Root.Clear();
            RingUi.HudPad(Root);
            Root.Add(Header());
            var rep = C.GetSeasonReport();
            var body = RingUi.Row();
            RingUi.Grow(body, 1f);
            var left = Glass(16f);
            RingUi.Grow(left, 1f);
            left.style.marginRight = 12f;
            left.Add(RingUi.Title(T.Format("c.season.title", rep.Season)));
            left.Add(RingUi.Lbl(T.Format("c.season.line", rep.Popularity, rep.Trust, rep.Money), 18f));
            if (rep.Fired) left.Add(RingUi.Lbl(T.Get("season.fired"), 18f, RingUi.Bad, true));
            if (rep.TitleDone) left.Add(RingUi.Lbl(T.Get(rep.TitleWon || rep.BeltIsPlayers ? "c.season.belt_yes" : "c.season.belt_no"), 18f, rep.BeltIsPlayers ? RingUi.Gold : RingUi.Dim));
            else if (rep.TitleEligible) left.Add(RingUi.Lbl(T.Get(rep.IsDefense ? "season.defense" : "season.title_shot"), 18f, RingUi.Gold, true));
            if (C.CanStartTitleMatch)
            {
                left.Add(RingUi.PrimaryButton(T.Get("c.season.title_go"), () => { C.StartTitleMatch(); Host.Save(); }));
                left.Add(RingUi.MakeButton(T.Get("c.season.title_skip"), () => { C.SkipTitle(); Host.Save(); Refresh(); }, true, RingUi.Dim, 16f));
            }
            if (C.CanStartNextSeason) left.Add(RingUi.PrimaryButton(T.Get("season.next"), OnConfirm));
            body.Add(left);
            var right = RingUi.Column();
            RingUi.Grow(right, 1f);
            right.Add(DevelopmentPanel(Refresh));
            body.Add(right);
            Root.Add(body);
        }

        public override void OnConfirm()
        {
            if (C.CanStartTitleMatch) { C.StartTitleMatch(); Host.Save(); return; }
            if (!C.CanStartNextSeason) return;
            C.StartNextSeason();
            Host.Save();
        }
    }
}
