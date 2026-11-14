using System;
using System.Collections.Generic;
using Game.Core.Ring;
using Game.Core.Ring.Career;

namespace Game.Gameplay.Ring.Models
{
    /// <summary>Порада розвитку для показу: напис кнопки, пояснення під нею, сталий ключ фокуса й шлях перка (колір смужки).</summary>
    public sealed class DevRecLine
    {
        public DevAdvice Advice;
        public string Title;
        public string Why;
        public string PadKey;
        /// <summary>Шлях перка; null — загальний перк, прийом чи атрибут.</summary>
        public RingPath? Path;
    }

    /// <summary>
    /// Рядки екранів кар'єри зони «Меню», що читають ядро «Кар'єри» (запит ПМ 11.11.2026): чому поразка не дала популярності
    /// (P1-6), що радить «Радимо зараз» (P2-2), чи показувати підсумок сезону (3-P2-6). Чистий C# — тестується без Unity
    /// (<c>CareerScreenLinesTests</c>); екрани лише кладуть ці рядки в наявні складники.
    /// </summary>
    public static class CareerScreenLines
    {
        /// <summary>
        /// Підсумок вечора: чому поразка не дала популярності. 1 — ажіотаж нижче <see cref="CareerRules.LossPopularityHype"/>,
        /// 2 — поразка цього сезону вже дала популярність; 0 — нічого не показувати (null).
        /// </summary>
        public static string LossPopularity(RingText t, ShowResult r)
        {
            if (r == null) return null;
            switch (r.LossPopularityBlocked)
            {
                case 1: return t.Format("c.result.loss_pop.hype", CareerRules.LossPopularityHype, r.Hype);
                case 2: return t.Get("c.result.loss_pop.season");
                default: return null;
            }
        }

        /// <summary>
        /// Кінець сезону: повний підсумок (рекорд, популярність, гроші, ворожнеча, пояс) — лише коли він остаточний; поки
        /// титульний шанс відкритий, паперовий звіт показує тільки його (підсумок після матчу чи відмови інакше показувався двічі).
        /// </summary>
        public static bool SeasonSummaryShown(CareerSession c) { return c.SeasonSummaryFinal; }

        /// <summary>
        /// «Радимо зараз»: поради ядра (<see cref="DevelopmentAdvice.For"/> — від ролі, ґіміка й минулого матчу; очок багато — на
        /// одну більше). Пояснення — ключ поради (<see cref="DevAdvice.WhyKey"/>), а без нього — як раніше: опис перка,
        /// «новий прийом для дії…», навіщо атрибут.
        /// </summary>
        public static List<DevRecLine> DevRecs(RingText t, CareerSession c, int maxRecs)
        {
            var res = new List<DevRecLine>();
            foreach (var a in DevelopmentAdvice.For(c, maxRecs))
            {
                var l = new DevRecLine { Advice = a };
                switch (a.Kind)
                {
                    case AdviceKind.Perk:
                    {
                        var p = PerkCatalog.Find(a.Id);
                        l.Title = RingLabels.PerkName(t, a.Id) + " · " + a.Cost;
                        l.Why = a.WhyKey != null ? t.Get(a.WhyKey) : RingLabels.PerkDesc(t, a.Id);
                        l.Path = p != null ? p.Path : null;
                        l.PadKey = "rec:perk:" + a.Id;
                        break;
                    }
                    case AdviceKind.Move:
                    {
                        var m = c.Content.Moves.Get(a.Id);
                        l.Title = t.Get(m.NameKey) + " · " + a.Cost;
                        l.Why = a.WhyKey != null ? t.Get(a.WhyKey) : t.Format("c.dev.move.why", RingLabels.ActionName(t, m.Action));
                        l.PadKey = "rec:move:" + a.Id;
                        break;
                    }
                    default:
                    {
                        int v = c.Player.Get(a.Attr);
                        l.Title = RingLabels.AttrName(t, a.Attr) + " " + RingChances.Signed(v) + " → " + RingChances.Signed(v + 1) + " · " + a.Cost;
                        l.Why = a.WhyKey != null ? t.Get(a.WhyKey) : t.Get("c.dev.attr.why." + (int)a.Attr);
                        l.PadKey = "rec:attr:" + (int)a.Attr;
                        break;
                    }
                }
                res.Add(l);
            }
            return res;
        }

        // ---------------- федерації (П5 «Кар'єри», запит ПМ 12.11.2026) ----------------

        /// <summary>
        /// Кінець сезону, нижній ряд: спершу титульний шанс («Титульний матч» / «Пропустити»), потім пропозиція вищої федерації
        /// («Прийняти контракт» / «Лишитися» — без них гравець застрягав на місцевій під стелею популярності), і лише після рішення —
        /// «Новий сезон». Перша дія — золота плита (Enter), решта — плитки ліворуч. Порожньо — дій немає (іде прощальний матч).
        /// </summary>
        public static List<SeasonEndAction> SeasonEndActions(CareerSession c)
        {
            var l = new List<SeasonEndAction>();
            if (c.CanStartTitleMatch) { l.Add(SeasonEndAction.TitleMatch); l.Add(SeasonEndAction.SkipTitle); }
            else if (c.OfferAvailable)
            {
                l.Add(SeasonEndAction.AcceptOffer); l.Add(SeasonEndAction.DeclineOffer);
                // №8 (§6.4): дрібний шрифт — прочитати до рішення; раз на пропозицію, без менеджерки (вона читає сама)
                var k = c.GetOfferContract();
                if (k != null && k.CanRead) l.Add(SeasonEndAction.ReadContract);
            }
            else if (c.CanStartNextSeason) l.Add(SeasonEndAction.NextSeason);
            return l;
        }

        /// <summary>
        /// Рядок звільнення в підсумку сезону (null — контракт продовжено): на місцевій — «на нулі, назад на мінімум»
        /// (<c>season.fired</c>), у вищій федерації — пониження (<c>c.season.demoted</c> через <see cref="SeasonOfferModel.StatusText"/>).
        /// </summary>
        public static string FiredLine(RingText t, CareerSession c, SeasonOfferModel offer)
        {
            var rep = c.State.Report;
            if (rep == null || !rep.Fired) return null;
            return c.FederationTier > 1 ? offer.StatusText : t.Get("season.fired");
        }

        /// <summary>Блок пропозиції на папері підсумку: підсумок остаточний і контракт продовжено (звільненому — рядок звільнення).</summary>
        public static bool OfferShown(CareerSession c, SeasonOfferModel offer)
        {
            var rep = c.State.Report;
            return offer.Visible && rep != null && !rep.Fired;
        }

        /// <summary>
        /// Ця відмова — друга чи далі поспіль: букер ображається, довіра −1 (<see cref="CareerSession.DeclineOffer"/>; серію обриває лише
        /// сезон без явної відмови, тож третя поспіль теж коштує довіри — у тексті ризику немає «друга»).
        /// </summary>
        public static bool DeclineOffends(CareerSession c) { return c.State.OfferDeclines + 1 >= CareerRules.DeclinesToOffend; }

        /// <summary>Плитка «Лишитися», опис: пропозиція повториться (одним рядком — під ним може стати ризик).</summary>
        public static string DeclineEffect(RingText t) { return t.Get("c.offer.decline.again"); }

        /// <summary>Плитка «Лишитися», ризик (сигнальним): відмова поспіль — довіра −1; null — перша.</summary>
        public static string DeclineRisk(RingText t, CareerSession c) { return DeclineOffends(c) ? t.Get("c.offer.decline.offend") : null; }

        /// <summary>
        /// Плитка «Пропустити титульний матч», опис (ПМ 12.11.2026, кадр слоту 28: плитка без наслідку): сезон закривається без
        /// титульного матчу (<see cref="CareerSession.SkipTitle"/> ставить TitleDone).
        /// </summary>
        public static string SkipTitleEffect(RingText t) { return t.Get("c.season.title_skip.effect"); }

        /// <summary>
        /// Та сама плитка, ризик (сигнальним): чемпіон, що не захищає пояс, втрачає довіру букера — те саме правило, що в ядрі
        /// (<see cref="CareerSession.SkipTitle"/>: TitleEligible і чемпіон → довіра −1); null — не чемпіон.
        /// </summary>
        public static string SkipTitleRisk(RingText t, CareerSession c)
        {
            var r = c.State.Report;
            return r != null && r.TitleEligible && !r.TitleDone && c.IsChampion ? t.Get("c.season.title_skip.champion") : null;
        }

        /// <summary>
        /// Рядок «Очок розвитку: N» над порадами розвитку. Шапка, що вже показує «ОЧКІВ N» (права колонка підсумку матчу), — без
        /// нього (ПМ 12.11.2026, кадр 4:3 слоту 28: те саме число двічі); шухляда тижня й кінець сезону числа в шапці не мають.
        /// </summary>
        public static string DevPointsLine(RingText t, CareerSession c, bool headerShowsPoints)
        {
            return headerShowsPoints ? null : t.Format("dev.points", c.DevPoints);
        }

        /// <summary>Кнопка костюма у «Витратах»: ціна — рівня федерації (<see cref="CareerSession.CostumePrice"/>), а не стала 60.</summary>
        public static string CostumeButton(RingText t, CareerSession c)
        {
            return c.State.CostumeBought ? t.Get("c.shop.costume.owned") : t.Format("c.shop.costume", c.CostumePrice);
        }

        /// <summary>
        /// Кнопка тренера <paramref name="coach"/> (індекс <see cref="CareerRules.Coaches"/>): найнятий — прогрес із кроком цього тренера
        /// (<see cref="CareerSession.CoachStepFor"/>: витривалість з сезону 4 довша, §5.5), ні — плата за тиждень. Крок — той, що списує
        /// тренування: стала <see cref="CareerRules.CoachProgressStep"/> з сезону 4 показувала 2/3 там, де до +1 треба 4.
        /// </summary>
        public static string CoachButton(RingText t, CareerSession c, int coach)
        {
            string name = RingLabels.AttrName(t, CareerRules.Coaches[coach]);
            return c.State.Coach == coach
                ? t.Format("c.shop.coach.hired", name, c.State.CoachProgress, c.CoachStepFor(coach))
                : t.Format("c.shop.coach", name, CareerRules.CoachWeeklyFee);
        }

        /// <summary>Підказка тренера: скільки тренувань на +1 — крок цього тренера (<see cref="CareerSession.CoachStepFor"/>).</summary>
        public static string CoachHint(RingText t, CareerSession c, int coach)
        {
            int step = c.CoachStepFor(coach);   // з сезону 7 у тренера витривалості — 5: «За 5 тренувань», не «тренування»
            string hint = t.Format(PluralMany(step) ? "c.shop.coach.desc.many" : "c.shop.coach.desc", RingLabels.AttrName(t, CareerRules.Coaches[coach]), step);
            return CareerRules.Coaches[coach] == RingAttr.Stamina ? WithReason(hint, AgeingTail(t, c.State.Season, true)) : hint;   // Б5
        }

        /// <summary>
        /// Підказка кнопки атрибута в повному списку розвитку: ціна — та, що спише покупка (<see cref="CareerSession.AttrCost"/>:
        /// витривалість з сезону 4 дорожча, §5.5), навіщо атрибут і, якщо купити не можна, чому (<paramref name="reason"/> — код
        /// <c>dev.reason.*</c>, null — можна). Атрибут на максимумі (<see cref="AttrAtMax"/>, ПМ 13.11.2026, п. 6) — без ціни: купувати
        /// нічого, тож «+1 (2 оч.)» поруч із «Досягнуто максимуму» лише плутало; причина — максимум, хоч би який код дав виклик (на
        /// етапі без розвитку <see cref="CareerSession.CanBuyAttr"/> каже «stage» раніше за «max»).
        /// </summary>
        public static string AttrBuyHint(RingText t, CareerSession c, RingAttr a, string reason)
        {
            string why = t.Get("c.dev.attr.why." + (int)a);
            if (AttrAtMax(c, a)) return WithReason(why, t.Get("dev.reason.max"));
            string hint = t.Format("dev.attr.buy", c.AttrCost(a)) + " — " + why;
            string no = string.IsNullOrEmpty(reason) ? null : t.Get("dev.reason." + reason);
            string age = a == RingAttr.Stamina ? AgeingTail(t, c.State.Season, false) : null;   // Б5: чому дорожча
            if (age == null) return WithReason(hint, no);
            hint = WithReason(hint, age);
            return no == null ? hint : hint + " · " + no;   // «оч.» — скорочення: крапку перед причиною не зрізати (WithReason зрізав би)
        }

        /// <summary>
        /// Б5 (§5.5, власник 13.11.2026: «Так, усі 7»): хвіст підказки витривалості й тренера витривалості з сезону 4 — «тіло старіє:
        /// +1 оч.» / «тіло старіє: +1 тренування» (надбавка <see cref="Ageing.StaminaSurcharge"/>); до сезону 4 — null, М1 без змін.
        /// </summary>
        public static string AgeingTail(RingText t, int season, bool coach)
        {
            int s = Ageing.StaminaSurcharge(season);
            return s <= 0 ? null : t.Format(coach ? "c.age.coach" : "c.age.attr", s);
        }

        /// <summary>
        /// Б5: рядок кінця сезону — лише на останньому кроці («Новий сезон») і лише коли надбавка наступного сезону росте (кінець
        /// сезонів 3 і 6): «Тіло старіє: витривалість — 3 оч., тренер — 4 тренування.» Ціни — з сесії (<see cref="CareerSession.AttrCost"/>,
        /// <see cref="CareerSession.CoachStepFor"/>) плюс приріст надбавки (<see cref="Ageing.StaminaSurcharge"/>). Текст — один рядок
        /// (ПМ 14.11.2026: першого тексту У0 було два рядки, і з контрактом та пізньою роллю папір кінця сезону сягав канатів на
        /// 16 одиниць); на екран — після У0 арт-директора (власник може переграти).
        /// </summary>
        public static string AgeingNextLine(RingText t, CareerSession c, List<SeasonEndAction> actions)
        {
            if (c == null || actions == null || actions.Count != 1 || actions[0] != SeasonEndAction.NextSeason) return null;
            int grow = Ageing.StaminaSurcharge(c.State.Season + 1) - Ageing.StaminaSurcharge(c.State.Season);
            if (grow <= 0) return null;
            int coach = c.CoachStepFor(Array.IndexOf(CareerRules.Coaches, RingAttr.Stamina)) + grow;   // кінець сезону 6: 5 — «5 тренувань»
            return t.Format(PluralMany(coach) ? "c.age.next.many" : "c.age.next", c.AttrCost(RingAttr.Stamina) + grow, coach);
        }

        /// <summary>
        /// Число вимагає родового множини («5 тренувань»), а не «тренування» (1, 2–4, 21–24): 0, 5–20, 25–30, 11–14 у кожній сотні.
        /// Ключі з «.many» — той самий текст у цій формі.
        /// </summary>
        public static bool PluralMany(int n)
        {
            n = Math.Abs(n);
            int d = n % 10, h = n % 100;
            return d == 0 || d >= 5 || (h >= 11 && h <= 14);
        }

        /// <summary>Атрибут уже на стелі (<see cref="WrestlerStats.MaxAttr"/>): +1 не буде — плитка й підказка без «→ +4» і без ціни.</summary>
        public static bool AttrAtMax(CareerSession c, RingAttr a) { return c.Player.Get(a) >= WrestlerStats.MaxAttr; }

        /// <summary>
        /// Напис плитки атрибута в повному списку розвитку: «Сила +2 → +3» — «було → стане»; на максимумі — «Сила +3 · макс.»
        /// (ПМ 13.11.2026, п. 6: тьмяна «+3 → +4» обіцяла рівень, якого немає). Той самий знак, що й в інших рядках розвитку
        /// (<see cref="RingChances.Signed"/>).
        /// </summary>
        public static string AttrButton(RingText t, CareerSession c, RingAttr a) { return AttrTile(t, RingLabels.AttrName(t, a), c.Player.Get(a)); }

        /// <summary>
        /// Той самий напис за назвою й значенням — для екранів зі своєю моделлю рядка (кінцівка ворожнечі, <c>AttrRow</c>; рев'ю
        /// 13.11.2026: там теж стояло «+3 → +4»).
        /// </summary>
        public static string AttrTile(RingText t, string name, int value)
        {
            string head = name + " " + RingChances.Signed(value);
            return value >= WrestlerStats.MaxAttr ? head + " · " + t.Get("c.dev.attr.max") : head + " → " + RingChances.Signed(value + 1);
        }

        /// <summary>
        /// Рядок тіла: травмовані зони — скільки тижнів лишилось, ушкоджені — скільки до травми з порогом зони (<see cref="InjuryView.Threshold"/>:
        /// 3, у Хардкорника 4 — «2/3» там збрехало б), ціле — «Тіло ціле».
        /// </summary>
        public static string InjuryText(RingText t, List<InjuryView> injuries)
        {
            var parts = new List<string>();
            foreach (var i in injuries)
            {
                string zone = t.Get("bodyzone." + (int)i.Zone);
                if (i.WeeksLeft > 0) parts.Add(ScarTail(t, t.Format("c.injury.weeks", zone, i.WeeksLeft), i.ScarWeeks));
                else if (i.ZoneDamage > 0) parts.Add(ScarTail(t, t.Format("c.injury.dmg", zone, i.ZoneDamage, i.Threshold), i.ScarWeeks));
                else if (i.ScarWeeks > 0) parts.Add(t.Format("c.injury.scar_only", zone, i.ScarWeeks));   // Б5: здорова зона зі слідом
            }
            return parts.Count == 0 ? t.Get("c.injury.none") : string.Join(" · ", parts);
        }

        /// <summary>
        /// Б5 (§5.5): слід травми — з сезону 4 (<see cref="InjuryView.ScarWeeks"/>; до того 0, М1 без змін). Травмована зона — «Ноги:
        /// травма, ще 3 тиж. (слід: +1)»: поточна травма довша на стільки; ушкоджена — той самий хвіст: нова травма буде довшою.
        /// </summary>
        public static string ScarTail(RingText t, string line, int scarWeeks)
        {
            return scarWeeks > 0 ? line + " " + t.Format("c.injury.scar", scarWeeks) : line;
        }

        /// <summary>
        /// Б6 (власник 13.11.2026, «Так, усі 7», варіант А): сцена закулісся вписала суперника найближчого шоу
        /// (<see cref="WeekActionResult.OpponentChanged"/>) — частина рядка результату «суперник на шоу — {0}» з ім'ям із картки, вже
        /// переписаної ядром. «На шоу — …», а не «змінився»: прапорець стоїть і тоді, коли обіцяний суперник уже був у картці.
        /// Відкладену розплату (велике шоу, титул) у М1 не показуємо. Немає прапорця чи імені — null.
        /// </summary>
        public static string OpponentLine(RingText t, WeekActionResult r, string opponentName)
        {
            return r != null && r.OpponentChanged && !string.IsNullOrEmpty(opponentName) ? t.Format("c.delta.opponent", opponentName) : null;
        }

        /// <summary>Смужка зони тіла на стрічці героя: сегментів — поріг травми зони, заповнено — ушкодження (травма — уся), тижні травми.</summary>
        public struct ZoneMeter
        {
            public int Segments;
            public int Filled;
            public int WeeksLeft;
        }

        /// <summary>
        /// Смужка зони <paramref name="zone"/>: сегментів стільки, скільки поріг травми (<see cref="InjuryView.Threshold"/>: 3, у
        /// Хардкорника 4 — стала 3 показувала б «3/3» і повну смужку там, де ядро травмує лише на четвертому ушкодженні; рев'ю 13.11.2026),
        /// заповнено — ушкодження, травмована зона — повністю. Зони немає у виді — порожня смужка зі звичайним порогом.
        /// </summary>
        public static ZoneMeter ZoneMeterFor(List<InjuryView> injuries, BodyZone zone)
        {
            var m = new ZoneMeter { Segments = CareerRules.InjuryAtZoneDamage };
            if (injuries == null) return m;
            foreach (var i in injuries)
            {
                if (i.Zone != zone) continue;
                m.Segments = Math.Max(1, i.Threshold);
                m.WeeksLeft = i.WeeksLeft;
                m.Filled = i.WeeksLeft > 0 ? m.Segments : Math.Max(0, Math.Min(m.Segments, i.ZoneDamage));
            }
            return m;
        }

        /// <summary>Підпис смужки зони: травма — «N тиж.», інакше «ушкодження/поріг» («2/4» у Хардкорника).</summary>
        public static string ZoneMeterText(RingText t, ZoneMeter m)
        {
            return m.WeeksLeft > 0 ? t.Format("c.hub.weeks", m.WeeksLeft) : m.Filled + "/" + m.Segments;
        }

        /// <summary>Рядок «Витрат»: життя й дорога цієї федерації за тиждень (<see cref="CareerSession.WeeklyCost"/>, наприкінці тижня).</summary>
        public static string LivingLine(RingText t, CareerSession c) { return t.Format("c.shop.living", c.WeeklyCost); }

        /// <summary>
        /// Шкала популярності в ресурсах: до стелі федерації (<see cref="CareerSession.PopularityCeiling"/>) — повна шкала означає «час
        /// вище». Якщо популярність вища за стелю (старий сейв), шкала не обрізає її.
        /// </summary>
        public static int PopularityMax(CareerSession c) { return Math.Max(c.PopularityCeiling, c.Popularity); }

        /// <summary>
        /// Заголовок картки вечора: прощальний матч чемпіона (<see cref="ShowCardView.IsFarewell"/>, після «Прийняти контракт») —
        /// своїм рядком, бо це не титул і не звичайне велике шоу (ПМ 12.11.2026); далі — титул, велике шоу, тижневе.
        /// </summary>
        public static string CardHeading(RingText t, ShowCardView c)
        {
            return t.Get(c.IsFarewell ? "c.card.farewell" : c.IsTitle ? "c.card.title_match" : c.IsBig ? "c.card.big" : "c.card.weekly");
        }

        /// <summary>
        /// Рядок картки прощального матчу: віддати пояс чисто — і вища федерація зустріне з довірою <see cref="CareerRules.FarewellTrust"/>
        /// (null — не прощальний матч).
        /// </summary>
        public static string FarewellNote(RingText t, CareerSession s, ShowCardView c)
        {
            if (!c.IsFarewell) return null;
            var to = s.FederationTier < s.Content.Federations.Count ? s.Content.Federation(s.FederationTier + 1) : null;
            return t.Format("c.card.farewell.note", to == null ? "" : t.Get(to.NameKey), CareerRules.FarewellTrust);
        }

        /// <summary>
        /// Опис і причина недоступності одним рядком підказки: «опис · причина». Кінцева крапка опису перед роздільником знімається
        /// (огляд слоту 31: «…ажіотажу. · Ранг шляху замалий»). Без причини — опис як є.
        /// </summary>
        public static string WithReason(string desc, string reason)
        {
            if (string.IsNullOrEmpty(reason)) return desc;
            if (string.IsNullOrEmpty(desc)) return reason;
            string d = desc.TrimEnd();
            if (d.EndsWith(".", StringComparison.Ordinal) && !d.EndsWith("..", StringComparison.Ordinal)) d = d.Substring(0, d.Length - 1);
            return d + " · " + reason;
        }

        // ---------------- пакет «Так, усі 11» (власник, 12.11.2026): №7, №8, №11 ----------------

        /// <summary>
        /// №7: рядок афіші «переріс щабель» (<see cref="ShowCardView.IsOutgrown"/>: виріс, а підвищення ще не було — букер ставить
        /// слабшого суперника). Реванш має свій рядок, тож разом не буває (ядро ставить IsOutgrown лише без реваншу). null — ні.
        /// </summary>
        public static string OutgrownLine(RingText t, ShowCardView c) { return c != null && c.IsOutgrown ? t.Get("c.hub.outgrown") : null; }

        /// <summary>
        /// №11: чому плитка «Тренування» (чи «З тренером») вимкнена: дій тижня немає; зірка поза рингом уже тренувалась цього тижня
        /// (<see cref="WeekView.StarTrainingUsed"/>, §5.3); тренеру бракує грошей. null — плитка доступна. На плитці — коротко
        /// (<c>c.week.reason.star</c>, ≤ 2 рядки на найвужчій): повна причина ядра (<c>week.reason.star_training</c>) — у відгуку дії.
        /// </summary>
        public static string TrainReason(RingText t, WeekView w, bool withCoach)
        {
            if (w.ActionsLeft <= 0) return t.Get("c.hub.no_actions");
            if (w.StarTrainingUsed) return t.Get("c.week.reason.star");
            if (withCoach && w.Money < CareerRules.TrainerCost) return t.Format("c.hub.need_money", CareerRules.TrainerCost - w.Money);
            return null;
        }

        /// <summary>
        /// №8: чому плитка «Підробіток» вимкнена: дій немає чи тримає виключне право (§6.4). null — доступна. На плитці — коротко
        /// (<c>c.week.reason.exclusive</c>); повна причина (<c>week.reason.exclusive</c>) — у відгуку дії й у підказці «Виїзного шоу».
        /// </summary>
        public static string SideJobReason(RingText t, WeekView w, bool exclusive)
        {
            if (w.ActionsLeft <= 0) return t.Get("c.hub.no_actions");
            return exclusive ? t.Get("c.week.reason.exclusive") : null;
        }

        /// <summary>№8: підказка «Виїзного шоу» у «Витратах» — під виключним правом із причиною, чому кнопка вимкнена.</summary>
        public static string AwayHint(RingText t, CareerSession c)
        {
            string desc = t.Get("c.shop.away.desc");
            return c.ExclusiveActive && !c.State.AwayShow ? WithReason(desc, t.Get("week.reason.exclusive")) : desc;
        }

        /// <summary>№8: кнопка «Звільнити менеджерку» у «Витратах» — лише коли вона є.</summary>
        public static bool FireManagerShown(CareerSession c) { return c.HasManager; }

        /// <summary>№8: що робить менеджерка й що буде після звільнення (частка — <see cref="CareerRules.ManagerFeePercent"/>).</summary>
        public static string FireManagerHint(RingText t) { return t.Format("c.shop.manager.fire.desc", CareerRules.ManagerFeePercent); }

        /// <summary>
        /// №8: рядок гонорару в підсумку вечора. Ядро вже відняло частку менеджерки від ставки (<see cref="ShowResult.Fee"/> — після
        /// частки), тож ставка тут — до частки, а частка — окремо: «ставка 60 + бонуси 20 · менеджерка −12», і сума збігається з
        /// числом праворуч (<see cref="ShowResult.TotalFee"/>).
        /// </summary>
        public static string FeeSplitLine(RingText t, ShowResult r)
        {
            string s = t.Format("c.hub.fee_split", r.Fee + r.ManagerCut, r.FeeWinBonus + r.FeeHypeBonus);
            return r.ManagerCut > 0 ? s + " · " + t.Format("c.result.manager", r.ManagerCut) : s;
        }

        /// <summary>№11: поворот ролі після сцени закулісся (§5.3) у рядку відгуку дії: «поворот ролі: Лиходійка». null — не було.</summary>
        public static string TurnLine(RingText t, WeekActionResult r, bool female)
        {
            return r != null && r.TurnedTo.HasValue ? t.Format("c.delta.turn", RingLabels.PathName(t, r.TurnedTo.Value, female)) : null;
        }

        /// <summary>
        /// №11: блок «Пізня роль» на папері кінця сезону — лише на останньому кроці («Новий сезон»): після титульного шансу й рішення про
        /// пропозицію наступним іде новий сезон, тож «з наступного сезону» — правда, і папір не тримає разом контракт і пізню роль.
        /// </summary>
        public static bool LateRoleShown(CareerSession c, List<SeasonEndAction> actions)
        {
            return c.CanChooseLateRole && actions.Count == 1 && actions[0] == SeasonEndAction.NextSeason;
        }

        /// <summary>
        /// №11 (043f2706: позначка Selected — роль на НАСТУПНИЙ сезон): заголовок блоку «Пізня роль» — чинна роль
        /// (<see cref="LateRoleModel.CurrentText"/>, «Пізня роль: Ікона»), без ролі — просто «Пізня роль». Так золота смужка кнопки
        /// (обрана на наступний) не видає себе за чинну: чинна названа окремо, над кнопками.
        /// </summary>
        public static string LateRoleTitle(LateRoleModel m)
        {
            return m.CurrentText ?? m.TitleText;
        }

        /// <summary>
        /// №11: рядок під вибором пізньої ролі. Обрана на наступний сезон інша, ніж чинна, — «З наступного сезону: Легенда. Додатковий
        /// жетон реверсу в кожному матчі.» (<see cref="LateRoleModel.NextText"/> і опис; «без ролі» — без опису, він повторює назву).
        /// Та сама, що чинна, — що вона дає; без ролі взагалі — як працює вибір (<c>c.late.hint</c>). Обрану видно й без кольору смужки.
        /// </summary>
        public static string LateRoleNote(RingText t, LateRoleModel m)
        {
            LateRoleModel.Row sel = null;
            foreach (var r in m.Rows)
                if (r.Selected) { sel = r; break; }
            string next = m.NextText;
            if (next != null)
                return sel == null || sel.Role == LateRole.None ? next + "." : next + ". " + Sentence(sel.Description);
            if (sel == null || sel.Role == LateRole.None) return m.HintText;
            return Sentence(sel.Description);
        }

        private static string Sentence(string s)
        {
            s = s.TrimEnd();
            return s.EndsWith(".", StringComparison.Ordinal) ? s : s + ".";
        }

        /// <summary>Чернетка героя перед створенням: дивізіон (P2-6) — зі статі тіла (body_f_* — жіночий), суперники своєї статі.</summary>
        public static void SetDivision(CharacterDraft d, string body) { d.Female = CareerLooks.IsFemaleBody(body); }

        /// <summary>
        /// Скільки вільних балів заготовки стоїть в атрибуті <paramref name="a"/>: перший — у <see cref="CharacterDraft.FreePoint"/>,
        /// другий (лише коли балів два, Самородок) — у <see cref="CharacterDraft.FreePoint2"/>, а без нього — туди ж, що й перший
        /// (так само рахує ядро, <c>CareerSession.AttrsWithFreePoints</c>).
        /// </summary>
        public static int FreePointsIn(CharacterDraft d, int freePoints, RingAttr a)
        {
            if (d == null || freePoints <= 0) return 0;
            int n = d.FreePoint == a ? 1 : 0;
            if (freePoints >= 2 && (d.FreePoint2 ?? d.FreePoint) == a) n++;
            return n;
        }

        /// <summary>
        /// Клік по рядку характеристики афіші (Б4, власник 13.11.2026: «Так, усі 7»). Один вільний бал — переходить сюди. Два
        /// (Самородок) — бал ставиться сюди, а найдавніший із двох переходить: <see cref="CharacterDraft.FreePoint"/> — давніший,
        /// <see cref="CharacterDraft.FreePoint2"/> — новіший; двічі по тому самому атрибуту — обидва там (+2). Вище стелі
        /// <paramref name="max"/> бал не ставиться — заготовка не змінюється, false.
        /// </summary>
        public static bool PlaceFreePoint(CharacterDraft d, int freePoints, int baseValue, int max, RingAttr a)
        {
            if (d == null || freePoints <= 0) return false;
            if (freePoints < 2)
            {
                if (baseValue + 1 > max) return false;
                d.FreePoint = a;
                d.FreePoint2 = null;
                return true;
            }
            RingAttr newer = d.FreePoint2 ?? d.FreePoint;
            int here = (newer == a ? 1 : 0) + 1;
            if (baseValue + here > max) return false;
            d.FreePoint = newer;
            d.FreePoint2 = a;
            return true;
        }

        /// <summary>
        /// Частини ефекту плитки ґіміка на створенні: бонуси основи (<see cref="RingLabels.AttrBonusParts"/>) у порядку
        /// <see cref="CareerTileLayout.CreateEffectOrder"/> (Ш-1: найдовша — остання, підписна — перша) і, у ґіміка з двома вільними
        /// балами, рядок «+2 вільні бали» (арт-директор 14.11.2026: у Самородка всі основи 0 — плитка була порожня; той самий кегль і
        /// колір, що в бонусів, без золота).
        /// </summary>
        public static List<string> CreateTileParts(RingText t, int[] attrs, int freePoints)
        {
            var parts = attrs != null ? RingLabels.AttrBonusParts(t, attrs) : new string[0];
            var values = new List<int>();
            if (attrs != null) foreach (int v in attrs) if (v != 0) values.Add(v);   // той самий порядок, що в AttrBonusParts
            var res = CareerTileLayout.CreateEffectOrder(parts, values);
            if (freePoints >= 2) res.Add(t.Get("c.create.tile.freepoints"));
            return res;
        }

        // ---------------- огляд слоту 33 (ПМ 13.11.2026): порожнє значення — без мітки-тавтології ----------------

        /// <summary>
        /// Рядок плану букера: «План букера: Виграй чисто»; без плану — «Плану букера немає» (було «План букера: Плану немає»,
        /// кадри g_heroine_feud_4x3 016, k_champdecl_1080 022, m4x3 050).
        /// </summary>
        public static string PlanLine(RingText t, BookerPlan p)
        {
            return p == BookerPlan.None ? t.Get("c.card.plan.none") : t.Get("c.card.plan") + ": " + RingLabels.PlanName(t, p);
        }

        /// <summary>
        /// Рядок умов матчу з міткою: «Умови: Без дискваліфікації»; без умов — лише «Умов немає» (було «Умови: Умов немає», «Умови
        /// матчу: Умов немає» — кадри картки шоу й огляду ворожнечі). <paramref name="parts"/> — назви умов; порожньо чи лише
        /// «stip.none» — умов немає.
        /// </summary>
        public static string StipLine(RingText t, string label, IList<string> parts)
        {
            string none = t.Get("stip.none");
            if (parts == null || parts.Count == 0 || (parts.Count == 1 && parts[0] == none)) return none;
            return label + ": " + string.Join(", ", parts);
        }

        /// <summary>
        /// Ім'я на рингу в полі створення за статтю тіла (ПМ 13.11.2026, п. 6): героїні — «Новачка», героєві — «Новачок». Лише поки в
        /// полі ім'я за замовчуванням (будь-якої статі); своє ім'я гравця не чіпаємо. «.fem», а не «.f»: рід за тілом на екрані
        /// створення, а не глобальна заміна HeroForms (виняток «c.create.default_name» у HeroFormsTests лишається).
        /// </summary>
        public static string FollowDefaultName(RingText t, string current, bool female)
        {
            string male = t.Get("c.create.default_name"), fem = t.Get("c.create.default_name.fem");
            if (current != null && current != male && current != fem) return current;
            return female ? fem : male;
        }
    }

    /// <summary>Дія нижнього ряду кінця сезону (<see cref="CareerScreenLines.SeasonEndActions"/>).</summary>
    public enum SeasonEndAction { TitleMatch = 0, SkipTitle = 1, AcceptOffer = 2, DeclineOffer = 3, NextSeason = 4, ReadContract = 5 }
}
