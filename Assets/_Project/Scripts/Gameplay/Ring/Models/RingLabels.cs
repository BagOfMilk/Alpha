using Game.Core.Ring;
using Game.Core.Ring.Match;

namespace Game.Gameplay.Ring.Models
{
    /// <summary>Ключі тексту для перелічень ядра: усі назви живуть у ui_uk.json, тут лише відповідність «значення → ключ».</summary>
    public static class RingLabels
    {
        public static string AttrName(RingText t, RingAttr a) { return t.Get("attr." + FeudVars.AttrName(a)); }
        public static string PathName(RingText t, RingPath p) { return t.Get("path." + FeudVars.PathName(p)); }
        public static string PathMark(RingText t, RingPath p) { return t.Get("path." + FeudVars.PathName(p) + ".mark"); }
        public static string Var(RingText t, string name) { return t.Get("var." + name); }

        /// <summary>
        /// Бонуси характеристик окремими частинами: «Сила +2», «Спритність −1», «Витривалість +2» (плитка ґіміка на створенні
        /// героя). Плитка кладе їх рядком із переносом, кожну без переносу всередині — тож рядок ламається лише між
        /// характеристиками, а назва не відривається від числа (ПМ 11.11.2026: «жодного розриву посеред слова… опис — не більше
        /// двох рядків»; три рядки через «\n» не влазили, а нерозривного пробілу у Fixel немає). Нульові пропускає. Ті самі
        /// частини міряє охоронець CareerTextFitTests.
        /// </summary>
        public static string[] AttrBonusParts(RingText t, int[] a)
        {
            var parts = new System.Collections.Generic.List<string>();
            for (int i = 0; i < a.Length; i++) if (a[i] != 0) parts.Add(AttrName(t, (RingAttr)i) + " " + RingChances.Signed(a[i]));
            return parts.ToArray();
        }

        public static string PlanName(RingText t, BookerPlan p)
        {
            switch (p)
            {
                case BookerPlan.WinClean: return t.Get("plan.win_clean");
                case BookerPlan.LoseClean: return t.Get("plan.lose_clean");
                case BookerPlan.WinAny: return t.Get("plan.win_any");
                case BookerPlan.NoCleanLoss: return t.Get("plan.no_clean_loss");
                default: return t.Get("plan.none");
            }
        }

        public static string PlanOutcome(RingText t, FeudPlanOutcome o)
        {
            switch (o)
            {
                case FeudPlanOutcome.Fulfilled: return t.Get("planout.fulfilled");
                case FeudPlanOutcome.Swerved: return t.Get("planout.swerved");
                default: return t.Get("planout.neither");
            }
        }

        public static string TierName(RingText t, RollTier tier)
        {
            switch (tier)
            {
                case RollTier.Strong: return t.Get("tier.strong");
                case RollTier.Partial: return t.Get("tier.partial");
                default: return t.Get("tier.miss");
            }
        }

        public static string PostureName(RingText t, Posture p) { return t.Get("posture." + (int)p); }
        public static string ActionName(RingText t, MatchActionId id) { return t.Get("action." + (int)id); }
        public static string Unavailable(RingText t, UnavailableReason r)
        {
            if (r == UnavailableReason.TooEarly) return t.Format("match.unavail.too_early", MatchRules.FinisherMinRound);
            return t.Get("unavail." + (int)r);
        }
        public static string EndKind(RingText t, MatchEndKind k) { return t.Get("endkind." + (int)k); }
        public static string WinnerName(RingText t, MatchWinner w) { return t.Get("winner." + (int)w); }
        public static string EndingName(RingText t, FeudEnding e) { return t.Get(FeudEndings.TitleKey(e)); }
        public static string EndingDesc(RingText t, FeudEnding e) { return t.Get(FeudEndings.TitleKey(e) + ".desc"); }
        public static string PerkName(RingText t, string id) { return t.Get("perk." + id); }
        public static string PerkDesc(RingText t, string id) { return t.Get("perk." + id + ".desc"); }

        /// <summary>Етап ажіотажу: холодний 0–3, гуде 4–6, палає 7–9, на ногах 10.</summary>
        public static int HypeStage(int hype) { return hype >= 10 ? 3 : hype >= 7 ? 2 : hype >= 4 ? 1 : 0; }
        public static string HypeStageText(RingText t, int hype) { return t.Get("hype.stage." + HypeStage(hype)); }

    }
}
