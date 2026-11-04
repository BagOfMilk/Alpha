using System;
using System.Collections.Generic;

namespace Game.Core.Ring.Career
{
    /// <summary>
    /// Букер федерації (TRPG_DESIGN §6.4, §8): складає картку тижня — суперник, місце в картці, план, умови, гонорар.
    /// Детермінований: жодного кубика, лише хеш (зерно кар'єри, тиждень, борець) — тож вибір не залежить від порядку дій гравця.
    /// Споживає довіру (план є з довірою ≥ 2; з ≥ 3 «м'якший» — не програй чисто) і популярність (місце в картці, рівень суперника).
    /// </summary>
    public static class ShowBooker
    {
        /// <summary>Хеш (splitmix) — замість System.Random: те саме зерно й аргументи дають те саме число.</summary>
        public static int Mix(ulong seed, int a, int b)
        {
            unchecked
            {
                ulong z = seed + 0x9E3779B97F4A7C15UL * (ulong)(a * 31 + b + 1);
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                z ^= z >> 31;
                return (int)(z & 0x7FFFFFFF);
            }
        }

        /// <summary>Рівень суперника, якого букер вважає «рівним» для такої популярності: 0 → 1, 10 → 6.</summary>
        public static int TargetRank(int popularity)
        {
            int r = 1 + popularity * 5 / CareerRules.MaxPopularity;
            return Math.Max(1, Math.Min(6, r));
        }

        public static int Fee(CardPosition pos, bool big)
        {
            int f = CareerRules.FeeByPosition[(int)pos];
            return big ? f * CareerRules.BigShowFeeMultiplier : f;
        }

        /// <summary>Суперник звичайного шоу: близький за рівнем, із продовженням гарячих ворожнеч, не той самий двічі поспіль.</summary>
        public static NpcDef ChooseWeekly(CareerContent content, CareerState st, int popularity, bool hammerAvailable, Func<string, int> feudLevel)
        {
            return Choose(content, st, popularity, hammerAvailable, feudLevel, null, st.Week);
        }

        /// <summary>
        /// Суперник великого шоу після Молота: найгарячіша ворожнеча (рівень ≥ 1), інакше найближчий за рівнем. Чемпіон-гравець
        /// захищає пояс від найгарячішого претендента.
        /// </summary>
        public static NpcDef ChooseBig(CareerContent content, CareerState st, int popularity, bool hammerAvailable, Func<string, int> feudLevel)
        {
            NpcDef best = null; int bestLevel = -1;
            foreach (var n in content.Roster)
            {
                if (!Eligible(n, st, hammerAvailable)) continue;
                int lv = feudLevel(n.Id);
                if (lv > bestLevel || (lv == bestLevel && best != null && n.Rank > best.Rank)) { best = n; bestLevel = lv; }
            }
            if (best != null && bestLevel >= 1) return best;
            return Choose(content, st, popularity, hammerAvailable, feudLevel, null, st.Week + 100);
        }

        /// <summary>Суперник титульного матчу: чинний чемпіон (NPC); якщо пояс у гравця — найгарячіший претендент.</summary>
        public static NpcDef ChooseTitle(CareerContent content, CareerState st, bool hammerAvailable, Func<string, int> feudLevel)
        {
            if (st.BeltHolder != CareerState.PlayerHolder)
            {
                var holder = content.FindNpc(st.BeltHolder);
                if (holder != null) return holder;
            }
            NpcDef best = null; int bestKey = -1;
            foreach (var n in content.Roster)
            {
                if (n.Id == st.BeltHolder) continue;
                if (n.Id == CareerContent.HammerId && !hammerAvailable) continue;
                int key = feudLevel(n.Id) * 10 + n.Rank;
                if (key > bestKey) { best = n; bestKey = key; }
            }
            return best;
        }

        private static bool Eligible(NpcDef n, CareerState st, bool hammerAvailable)
        {
            if (n.Id == CareerContent.HammerId && !hammerAvailable) return false;
            return true;
        }

        private static NpcDef Choose(CareerContent content, CareerState st, int popularity, bool hammerAvailable, Func<string, int> feudLevel,
            string exclude, int salt)
        {
            int target = TargetRank(popularity);
            NpcDef best = null; int bestScore = int.MinValue;
            foreach (var n in content.Roster)
            {
                if (!Eligible(n, st, hammerAvailable) || n.Id == exclude) continue;
                int score = 20 - 3 * Math.Abs(n.Rank - target) + 2 * feudLevel(n.Id) + Mix(st.Seed, salt, n.Rank) % 3;
                if (n.Id == st.LastOpponent) score -= 8;
                if (score > bestScore) { best = n; bestScore = score; }
            }
            return best;
        }

        /// <summary>
        /// План букера: довіра 0–1 — плану немає; якщо суперник помітно сильніший за цільового — «програй чисто» (із довірою ≥ 3 м'якше:
        /// «не програй чисто»); помітно слабший — «виграй чисто»; інакше «просто виграй».
        /// </summary>
        public static BookerPlan PlanFor(NpcDef opp, int popularity, int trust)
        {
            if (trust < 2) return BookerPlan.None;
            int target = TargetRank(popularity);
            if (opp.Rank >= target + 2) return trust >= 3 ? BookerPlan.NoCleanLoss : BookerPlan.LoseClean;
            if (opp.Rank <= target - 2) return BookerPlan.WinClean;
            return BookerPlan.WinAny;
        }

        /// <summary>Умови матчу: ворожнеча 3+ — «без дискваліфікації» (§9). Решта — одиночний.</summary>
        public static MatchStipulations StipulationsFor(int feudLevel)
        {
            return feudLevel >= CareerRules.NoDqFeudLevel ? MatchStipulations.NoDq : MatchStipulations.None;
        }

        /// <summary>Чи заслуговує гравець титульного шансу: довіра ≥ 4, ворожнеча 4 з будь-ким або чемпіон (захист).</summary>
        public static bool TitleEligible(CareerContent content, CareerState st, int trust, bool hammerAvailable, Func<string, int> feudLevel)
        {
            if (st.BeltHolder == CareerState.PlayerHolder) return true;
            if (trust >= CareerRules.TitleShotTrust) return true;
            foreach (var n in content.Roster)
            {
                if (n.Id == CareerContent.HammerId && !hammerAvailable) continue;
                if (feudLevel(n.Id) >= CareerRules.MaxFeud) return true;
            }
            return false;
        }
    }
}
