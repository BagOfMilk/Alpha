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
        /// пропозицію наступним іде новий сезон, тож «діє з наступного сезону» — правда, і папір не тримає разом контракт і пізню роль.
        /// </summary>
        public static bool LateRoleShown(CareerSession c, List<SeasonEndAction> actions)
        {
            return c.CanChooseLateRole && actions.Count == 1 && actions[0] == SeasonEndAction.NextSeason;
        }

        /// <summary>
        /// №11: рядок під вибором пізньої ролі — обрана словами («Ікона: +1 ажіотажу на старті кожного матчу. Діє з наступного сезону.»);
        /// без ролі — як працює вибір (<c>c.late.hint</c>). Обрану видно й без кольору смужки кнопки.
        /// </summary>
        public static string LateRoleNote(RingText t, LateRoleModel m)
        {
            foreach (var r in m.Rows)
                if (r.Selected && r.Role != LateRole.None)
                {
                    string d = r.Description.TrimEnd();
                    if (!d.EndsWith(".", StringComparison.Ordinal)) d += ".";
                    return r.Name + ": " + d + " " + t.Get("c.late.from_next");
                }
            return m.HintText;
        }

        /// <summary>Чернетка героя перед створенням: дивізіон (P2-6) — зі статі тіла (body_f_* — жіночий), суперники своєї статі.</summary>
        public static void SetDivision(CharacterDraft d, string body) { d.Female = CareerLooks.IsFemaleBody(body); }
    }

    /// <summary>Дія нижнього ряду кінця сезону (<see cref="CareerScreenLines.SeasonEndActions"/>).</summary>
    public enum SeasonEndAction { TitleMatch = 0, SkipTitle = 1, AcceptOffer = 2, DeclineOffer = 3, NextSeason = 4, ReadContract = 5 }
}
