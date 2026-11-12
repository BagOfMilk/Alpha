using Game.Core.Ring;
using Game.Core.Ring.Match;

namespace Game.Gameplay.Ring.Models
{
    /// <summary>Ключі тексту для перелічень ядра: усі назви живуть у ui_uk.json, тут лише відповідність «значення → ключ».</summary>
    public static class RingLabels
    {
        public static string AttrName(RingText t, RingAttr a) { return t.Get("attr." + FeudVars.AttrName(a)); }
        public static string PathName(RingText t, RingPath p) { return t.Get("path." + FeudVars.PathName(p)); }
        /// <summary>
        /// Назва шляху за статтю борця (P2-6, ПМ 12.11.2026): «Героїня / Лиходійка / Бунтарка» — ключі «path.&lt;шлях&gt;.fem». Не «.f»:
        /// ту HeroForms підмінила б для обох борців на одному екрані.
        /// </summary>
        public static string PathName(RingText t, RingPath p, bool female) { return female ? t.Get("path." + FeudVars.PathName(p) + ".fem") : PathName(t, p); }
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

        // ---------- рід борця на табло (P2-6, ПМ 12.11.2026) ----------

        /// <summary>
        /// Суфікс жіночої форми слова про борця на табло матчу: «Суперниця», «ВИРВАЛАСЯ!», «Приголомшена», «підвелася». Не «.f» і не
        /// «.rf»: ті <see cref="HeroForms"/> підмінює для всього екрана, а на табло двоє борців, і рід береться від того, про кого слово.
        /// </summary>
        public const string WomanSuffix = ".w";

        /// <summary>Борець — жінка: стать зі стану <see cref="HeroForms"/> (останній Apply хоста перед екраном матчу). Суперниця — лише
        /// разом із героїнею (суперник своєї статі; героїня проти Молота — він чоловік).</summary>
        public static bool IsWoman(RingText t, MatchSide side) { return side == MatchSide.Player ? HeroForms.ShowsHeroine(t) : HeroForms.ShowsRivalWoman(t); }

        public static MatchSide Other(MatchSide side) { return side == MatchSide.Player ? MatchSide.Rival : MatchSide.Player; }

        /// <summary>Текст ключа в роді борця <paramref name="side"/>: «ключ.w», якщо борець — жінка і форма є; інакше базовий.</summary>
        public static string ForSide(RingText t, string key, MatchSide side) { return t.Get(SideKey(t, key, side)); }

        public static string ForSideFormat(RingText t, string key, MatchSide side, params object[] args) { return t.Format(SideKey(t, key, side), args); }

        private static string SideKey(RingText t, string key, MatchSide side)
        {
            string w = key + WomanSuffix;
            return IsWoman(t, side) && t.Has(w) ? w : key;
        }

        public static string PostureName(RingText t, Posture p) { return t.Get("posture." + (int)p); }
        /// <summary>Стан борця <paramref name="side"/> у його роді («Приголомшена»).</summary>
        public static string PostureName(RingText t, Posture p, MatchSide side) { return ForSide(t, "posture." + (int)p, side); }
        public static string ActionName(RingText t, MatchActionId id) { return t.Get("action." + (int)id); }
        public static string Unavailable(RingText t, UnavailableReason r)
        {
            if (r == UnavailableReason.TooEarly) return t.Format("match.unavail.too_early", MatchRules.FinisherMinRound);
            return t.Get("unavail." + (int)r);
        }
        /// <summary>
        /// Причина недоступності дії борця <paramref name="actor"/>: «Суперниця не лежить» — у роді його суперника. Рід самого борця
        /// («Ти ще не виснажена») — через «.f» <see cref="HeroForms"/>: дії на табло обирає лише гравець.
        /// </summary>
        public static string Unavailable(RingText t, UnavailableReason r, MatchSide actor)
        {
            if (r == UnavailableReason.TooEarly) return t.Format("match.unavail.too_early", MatchRules.FinisherMinRound);
            return ForSide(t, "unavail." + (int)r, Other(actor));
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
