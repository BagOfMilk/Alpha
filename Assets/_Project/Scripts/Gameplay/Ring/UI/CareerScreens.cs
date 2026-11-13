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

        /// <summary>
        /// Огляд <c>-ring-shot-dev-all</c> (рев'ю слоту 30): плити розвитку відкриваються з розгорнутим «Усе (N)», прокручуються до
        /// повного списку, а рядок-підказка підвалу показує найдовшу підказку списку — кадр найгіршого випадку сталої висоти. Без
        /// прапорця — як у грі: згорнуто, підказка лише при наведенні.
        /// </summary>
        public static bool DevAllForShots
        {
            get
            {
                if (!_devAllForShots.HasValue) _devAllForShots = RingLaunchArgs.Has("-ring-shot-dev-all");
                return _devAllForShots.Value;
            }
        }
        private static bool? _devAllForShots;
        /// <summary>Найдовша підказка повного списку останньої побудови (лише для <see cref="DevAllForShots"/>).</summary>
        private string _shotHint;

        protected CareerScreenView(RingUiContext ctx) : base(ctx)
        {
            if (RingStageHub.Has3D) Root.style.backgroundColor = Color.clear;
        }

        /// <summary>Екрани кар'єри B03 v2 проходяться без миші (P3 геймпад, 11.11.2026): усі дії — цілі фокусу.</summary>
        protected override bool PadTargets { get { return true; } }

        /// <summary>
        /// Огляд (RingMenuShots, слот 32): ціль фокусу без миші, чий ключ задовольняє <paramref name="match"/> («dev:attr:4»,
        /// «shop:coach:1», вкладка «tab:…»); null — на екрані такої немає. Бот кличе <see cref="IPadTarget.PadHot"/> /
        /// <see cref="IPadTarget.PadActivate"/> — той самий підсвіт і рядок-підказка, що фокус без миші й наведення.
        /// </summary>
        internal IPadTarget PadForShots(Func<string, bool> match) { return FindPad(Root, match); }

        private static IPadTarget FindPad(VisualElement e, Func<string, bool> match)
        {
            if (e == null) return null;
            var t = e as IPadTarget ?? PadMark.Of(e);
            if (t != null && t.PadKey != null && match(t.PadKey)) return t;
            int n = e.childCount;
            for (int i = 0; i < n; i++)
            {
                var r = FindPad(e[i], match);
                if (r != null) return r;
            }
            return null;
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

        /// <summary>
        /// Підвал плити розвитку (кадри слоту 30, 12.11.2026): на кінці сезону край прокрутки різав «▸ Усе (19)» навпіл, а рядок-підказка
        /// повного списку лежав у прокрутці під ним — поза видимим. Тепер обидва під прокруткою: перемикач (його кладе сюди
        /// <see cref="DevelopmentPanel"/>, moreTo), під ним — підказка на всю ширину (<see cref="DevFooterHint"/>). Підвал не стискається —
        /// стискається прокрутка.
        /// </summary>
        protected static VisualElement DevFooter(float padLeft, float padRight, float padBottom)
        {
            var foot = RingUi.Column();
            foot.style.flexShrink = 0f;
            foot.style.alignItems = Align.FlexStart;
            foot.style.paddingLeft = padLeft; foot.style.paddingRight = padRight; foot.style.paddingBottom = padBottom;
            return foot;
        }

        /// <summary>
        /// Рядок-підказка підвалу — лише коли розгорнуто «Усе»: підказки при наведенні дає тільки повний список (у порад і перемикача
        /// їх немає), тож згорнутий підвал — без порожньої смуги. Під перемикачем на всю ширину, сталої висоти
        /// (<see cref="CareerTileLayout.DevFootHintH"/>): поруч із «Усе (N)» опис перка займав 3–5 рядків, підвал ріс при кожному
        /// наведенні, а прокрутка стрибала (рев'ю слоту 30).
        /// </summary>
        protected void DevFooterHint(VisualElement foot)
        {
            if (!_devAll) { HintLine = null; return; }
            var hint = MakeHint();
            float h = CareerTileLayout.DevFootHintH;
            hint.style.minHeight = h; hint.style.height = h; hint.style.maxHeight = h;
            hint.style.marginTop = 0f;
            hint.style.paddingTop = CareerTileLayout.DevFootHintPadTop;
            hint.style.alignSelf = Align.Stretch;
            hint.style.flexShrink = 0f;
            hint.style.overflow = Overflow.Hidden;
            if (DevAllForShots && _shotHint != null) { hint.text = TextBreak.GlueDashes(_shotHint); hint.style.color = RingUi.Text; }
            foot.Add(hint);
        }

        /// <summary>
        /// Щойно розгорнутий «Усе (N)» (рев'ю слоту 30): перемикач — у підвалі під прокруткою, а екран після натискання перебудовується з
        /// прокруткою на нулі, тож повний список лежав би під згином, нижче за перемикач. Після розкладки прокрутка стає на його початок.
        /// Лише після натискання перемикача: після покупки в списку фокус без миші сам тримає свою кнопку на видноті.
        /// </summary>
        protected void ScrollToDevAll(ScrollView scroll)
        {
            var mark = DevAllStart;
            DevAllStart = null;
            if (mark == null || scroll == null) return;
            RowSnap(scroll, () => MarkTop(mark, scroll), true);   // на межу рядка: над «АТРИБУТИ» — без зрізу поради (п. 2)
        }

        /// <summary>Верх мітки в координатах вмісту прокрутки (від поточного зсуву не залежить).</summary>
        protected static float MarkTop(VisualElement mark, ScrollView scroll) { return mark.ChangeCoordinatesTo(scroll.contentContainer, Vector2.zero).y; }

        /// <summary>
        /// Класи для прокрутки на межу рядка (<see cref="RowSnap"/>): заголовок розділу (над нижнім краєм без свого рядка не лишається)
        /// і неподільна група (порада — назва з поясненням під нею).
        /// </summary>
        internal const string RowHeadClass = "ring-row-head", RowUnitClass = "ring-row-unit";

        /// <summary>
        /// Прокрутка на межу рядка (ПМ 13.11.2026, п. 2 і 5; кадри слоту 32): після розкладки — зсув <paramref name="want"/> (початок
        /// повного списку, зсув до перебудови, 0), вирівняний на початок рядка, а нижній край вікна — на кінець останнього цілого
        /// рядка: нижній відступ прокрутки росте на запас (<see cref="CareerTileLayout.SnapRows"/>; вікно коротшає, зовнішня висота
        /// та сама). На с4 (1080) щойно розгорнутий «Усе» стояв на межі прокрутки, і під «РОЗВИТОК» висів зріз «Залізна щелепа · 2»;
        /// на 4:3 нижній край різав «ПЕРКИ» під перемикачем. Прив'язка до <paramref name="want"/> тримається на кожній новій розкладці
        /// (шухляду ще звужує <c>Fit</c>, підказка в шапці стає довшою), доки гравець не прокрутить сам (коліщатко, смуга, фокус без
        /// миші — зсув уже не той, що поставлено тут); далі — лише чистий нижній край від його зсуву: прокручене рукою не стрибає.
        /// <paramref name="mark"/> — бажаний зсув є початком щойно розгорнутого списку (може лежати нижче межі прокрутки), інакше —
        /// зсув до перебудови (не далі межі).
        /// </summary>
        protected static void RowSnap(ScrollView scroll, Func<float> want, bool mark)
        {
            if (scroll == null) return;
            float basePad = float.NaN;   // відступ, який дав екран (стиль першої розкладки, з USS чи з коду); запас — поверх нього
            float extra = 0f, target = 0f, lastSet = float.NaN;
            bool anchored = true, pending = false;
            EventCallback<GeometryChangedEvent> relayout = e =>
            {
                if (float.IsNaN(basePad)) basePad = scroll.resolvedStyle.paddingBottom;
                // запас, з яким зроблена саме ця розкладка (стиль, щойно змінений тут, ще не розкладено)
                float applied = scroll.resolvedStyle.paddingBottom - basePad;
                float view = scroll.contentViewport.layout.height + applied, contentH = scroll.contentContainer.layout.height;
                if (float.IsNaN(view) || float.IsNaN(contentH) || float.IsNaN(applied) || view <= 0f) return;
                // гравець прокрутив сам — прив'язку знято
                if (anchored && !pending && !float.IsNaN(lastSet) && Mathf.Abs(scroll.scrollOffset.y - lastSet) > CareerTileLayout.RowEps) anchored = false;
                var mode = !anchored ? RowSnapMode.Keep : mark ? RowSnapMode.Mark : RowSnapMode.Restore;
                float add, t = CareerTileLayout.SnapRows(RowsOf(scroll.contentContainer), contentH, view, anchored ? want() : scroll.scrollOffset.y, mode, out add);
                if (Mathf.Abs(add - extra) > CareerTileLayout.RowEps) { extra = add; scroll.style.paddingBottom = basePad + extra; }
                if (!anchored || Mathf.Abs(applied - extra) > CareerTileLayout.RowEps) return;   // зсув — коли межі прокрутки вже з новим запасом
                target = t;
                if (pending) return;
                if (Mathf.Abs(scroll.scrollOffset.y - t) <= CareerTileLayout.RowEps) { lastSet = scroll.scrollOffset.y; return; }
                pending = true;
                scroll.schedule.Execute(() =>
                {
                    pending = false;
                    scroll.scrollOffset = new Vector2(scroll.scrollOffset.x, target);
                    lastSet = scroll.scrollOffset.y;
                });
            };
            scroll.RegisterCallback(relayout);
            scroll.contentViewport.RegisterCallback(relayout);
            scroll.contentContainer.RegisterCallback(relayout);
        }

        /// <summary>Рядки вмісту прокрутки: кнопки й рядки тексту (і неподільні групи) у координатах вмісту, злиті за висотою.</summary>
        private static List<ScrollRow> RowsOf(VisualElement content)
        {
            var atoms = new List<ScrollRow>();
            CollectRows(content, content, atoms);
            return CareerTileLayout.MergeRows(atoms);
        }

        private static void CollectRows(VisualElement e, VisualElement content, List<ScrollRow> atoms)
        {
            int n = e.childCount;
            for (int i = 0; i < n; i++)
            {
                var c = e[i];
                if (c.resolvedStyle.display == DisplayStyle.None || c.resolvedStyle.position == Position.Absolute) continue;   // фон, рамки — не рядки
                if (c is TextElement || c.ClassListContains(RowUnitClass))
                {
                    float h = c.layout.height;
                    if (float.IsNaN(h) || h <= CareerTileLayout.RowEps) continue;
                    float top = c.ChangeCoordinatesTo(content, Vector2.zero).y;
                    atoms.Add(new ScrollRow(top, top + h, c.ClassListContains(RowHeadClass)));
                }
                else CollectRows(c, content, atoms);
            }
        }

        private ScrollView _devKeptScroll;
        private Vector2 _devKeptOffset;
        private object _devKeptOwner;

        /// <summary>
        /// Перед <c>Root.Clear()</c> екрана з плитою розвитку: запам'ятати, де стоїть її прокрутка (огляд ПМ слоту 31, d_a1080 019→020:
        /// покупка в розгорнутому «Усе (18)» перебудовувала кінець сезону, і прокрутка скидалася вгору — місце в довгому списку губилося).
        /// </summary>
        protected void RememberDevScroll()
        {
            if (_devKeptScroll != null) _devKeptOffset = _devKeptScroll.scrollOffset;
            _devKeptScroll = null;
        }

        /// <summary>
        /// Прокрутка плити розвитку після перебудови: щойно розгорнутий «Усе» — до початку повного списку (<see cref="ScrollToDevAll"/>);
        /// інакше — той самий зсув, що до перебудови, якщо екран той самий (<paramref name="owner"/>: сезон, підсумок шоу). Новий екран —
        /// з верху. Те саме, що шухляда тижня робить своїм зсувом.
        /// </summary>
        protected void KeepDevScroll(ScrollView scroll, object owner)
        {
            bool same = Equals(owner, _devKeptOwner);
            _devKeptOwner = owner;
            _devKeptScroll = scroll;
            var restore = same ? _devKeptOffset : Vector2.zero;
            _devKeptOffset = Vector2.zero;
            if (DevAllStart != null) { ScrollToDevAll(scroll); return; }
            RowSnap(scroll, () => restore.y, false);   // і з верху: нижній край — на межі рядка (п. 2)
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
                if (DevAllForShots && (_shotHint == null || hint.Length > _shotHint.Length)) _shotHint = hint;
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

        /// <summary>Заголовок розділу; у прокрутці на межу рядка — заголовок (<see cref="RowHeadClass"/>): без свого рядка над краєм не лишається.</summary>
        protected static Label Section(string text)
        {
            var l = RingUi.Section(text);
            l.AddToClassList(RowHeadClass);
            return l;
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

        /// <summary>Рядок тіла — <see cref="CareerScreenLines.InjuryText"/> (поріг травми зони з виду: у Хардкорника 4).</summary>
        protected string InjuryText(List<InjuryView> injuries) { return CareerScreenLines.InjuryText(T, injuries); }

        /// <summary>Розвиток: атрибути, нові прийоми, перки — компактні кнопки, описи в рядку-підказці.</summary>
        private bool _devAll = DevAllForShots;
        /// <summary>«Усе (N)» щойно розгорнуто — плита прокрутить до початку повного списку (<see cref="ScrollToDevAll"/>).</summary>
        private bool _devAllOpened = DevAllForShots;
        /// <summary>Початок повного списку, до якого прокрутити після розкладки; null — не прокручувати.</summary>
        protected VisualElement DevAllStart;

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
            _shotHint = null;
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
                var recHead = RingUi.Lbl(T.Get("c.dev.recommended"), 12f, RingUi.Dim);
                recHead.AddToClassList(RowHeadClass);
                card.Add(recHead);
                foreach (var r in recs) card.Add(r);
            }
            else card.Add(RingUi.Lbl(T.Get("c.dev.none"), 12f, RingUi.Dim));

            var more = RingUi.PadKey(Small((_devAll ? "▾ " : "▸ ") + T.Format("c.dev.all", all), () => { _devAll = !_devAll; _devAllOpened = _devAll; changed(); }, true, null), "dev:all");
            more.style.marginTop = CareerTileLayout.DevFootTop;
            if (moreTo != null) { more.style.marginBottom = 0f; more.style.flexShrink = 0f; moreTo.Add(more); }
            else card.Add(more);
            bool opened = _devAllOpened;
            _devAllOpened = false;
            DevAllStart = null;
            if (!_devAll) return card;

            var attrs = RingUi.Row();
            attrs.style.flexWrap = Wrap.Wrap;
            for (int i = 0; i < 5; i++)
            {
                var a = (RingAttr)i;
                string reason;
                bool can = C.CanBuyAttr(a, out reason);
                var b = Small(CareerScreenLines.AttrButton(T, C, a), () => { C.BuyAttr(a); changed(); }, can, null,   // на максимумі — «· макс.» (п. 6)
                    CareerScreenLines.AttrBuyHint(T, C, a, can ? null : reason));   // ціна — та, що спише покупка (з сезону 4 витривалість дорожча)
                RingUi.PadKey(b, "dev:attr:" + i);
                b.style.marginRight = 4f;
                attrs.Add(b);
            }
            var attrsHead = Section(T.Get("c.dev.attrs"));
            if (opened) DevAllStart = attrsHead;
            card.Add(attrsHead);
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
                    CareerScreenLines.WithReason(RingLabels.PerkDesc(T, id), can ? null : T.Get("dev.reason." + reason)));
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
            box.AddToClassList(RowUnitClass);   // назва й пояснення — один рядок прокрутки: край не відрізає пояснення від назви
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
