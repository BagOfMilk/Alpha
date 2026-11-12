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
            else if (c.OfferAvailable) { l.Add(SeasonEndAction.AcceptOffer); l.Add(SeasonEndAction.DeclineOffer); }
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

        /// <summary>Чернетка героя перед створенням: дивізіон (P2-6) — зі статі тіла (body_f_* — жіночий), суперники своєї статі.</summary>
        public static void SetDivision(CharacterDraft d, string body) { d.Female = CareerLooks.IsFemaleBody(body); }
    }

    /// <summary>Дія нижнього ряду кінця сезону (<see cref="CareerScreenLines.SeasonEndActions"/>).</summary>
    public enum SeasonEndAction { TitleMatch = 0, SkipTitle = 1, AcceptOffer = 2, DeclineOffer = 3, NextSeason = 4 }
}
