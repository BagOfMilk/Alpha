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
    }
}
