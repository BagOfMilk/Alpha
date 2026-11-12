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
        /// <summary>
        /// Підсилити суперника на <paramref name="steps"/> щаблів (сила за місцем у картці): кожен щабель — +1 першому атрибуту
        /// з черги, що ще нижче межі: атрибут фінішера, Витривалість (вириватися), Техніка (реверси, перший хід), Сила.
        /// Повертає, скільки щаблів лягло (на межі всіх атрибутів і без mods — менше). Детерміновано.
        /// </summary>
        public static int Strengthen(WrestlerStats s, int steps) { return Strengthen(s, steps, null); }

        /// <summary>
        /// Те саме, а щаблі понад межу атрибутів переходять у витримку суперника <paramref name="mods"/> (коли не null): по черзі
        /// +1 стартового імпульсу (раніше до фінішера), +1 до «Вирватися», «залізна щелепа» (поріг ушкодження +1), +1 жетон реверсу.
        /// </summary>
        public static int Strengthen(WrestlerStats s, int steps, MatchModifiers mods)
        {
            // Лише атрибути, що б'ють у кожному матчі; Спритність і Харизма (політ, дражнити) майже не додають сили — замір 06.11.2026:
            // пізні щаблі туди не робили суперника сильнішим. Далі — витримка й імпульс (mods).
            var order = new[] { s.FinisherAttr, RingAttr.Stamina, RingAttr.Technique, RingAttr.Strength };
            int applied = 0;
            for (int i = 0; i < steps; i++)
            {
                bool done = false;
                foreach (var a in order)
                {
                    if (s.Get(a) >= WrestlerStats.MaxAttr) continue;
                    s.Set(a, s.Get(a) + 1);
                    done = true;
                    break;
                }
                if (!done)
                {
                    if (mods == null) break;
                    switch (applied % 4)
                    {
                        case 0: mods.StartMomentumBonus++; break;
                        case 1: mods.KickOutBonus++; break;
                        case 2: mods.DamageThresholdShift++; break;
                        default: mods.ExtraReversalTokens++; break;
                    }
                }
                applied++;
            }
            return applied;
        }

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
        public static int TargetRank(int popularity) { return TargetRank(popularity, 0, CareerRules.MaxPopularity); }

        /// <summary>
        /// Рівень «рівного» суперника у федерації (П5): популярність між підлогою й стелею федерації → ранг 1…6 її ростеру.
        /// </summary>
        public static int TargetRank(int popularity, int floor, int ceiling)
        {
            int span = Math.Max(1, ceiling - floor);
            int r = 1 + (popularity - floor) * 5 / span;
            return Math.Max(1, Math.Min(6, r));
        }

        public static int Fee(CardPosition pos, bool big) { return Fee(pos, big, 100); }

        /// <summary>Гонорар за місцем у картці з множником федерації (П5: ×1 / ×2 / ×3,5 / ×6 — у відсотках).</summary>
        public static int Fee(CardPosition pos, bool big, int feePercent)
        {
            int f = CareerRules.FeeByPosition[(int)pos] * feePercent / 100;
            return big ? f * CareerRules.BigShowFeePercent / 100 : f;
        }

        /// <summary>Суперник звичайного шоу: близький за рівнем, із продовженням гарячих ворожнеч, не той самий двічі поспіль.</summary>
        public static NpcDef ChooseWeekly(IReadOnlyList<NpcDef> pool, FederationDef fed, CareerState st, int popularity, bool hammerAvailable, Func<string, int> feudLevel)
        {
            return Choose(pool, fed, st, popularity, hammerAvailable, feudLevel, null, st.Week);
        }

        /// <summary>
        /// Суперник великого шоу після Молота: найгарячіша ворожнеча (рівень ≥ 1), інакше найближчий за рівнем. Чемпіон-гравець
        /// захищає пояс від найгарячішого претендента.
        /// </summary>
        public static NpcDef ChooseBig(IReadOnlyList<NpcDef> pool, FederationDef fed, CareerState st, int popularity, bool hammerAvailable, Func<string, int> feudLevel)
        {
            NpcDef best = null; int bestLevel = -1;
            foreach (var n in pool)
            {
                if (!Eligible(n, st, hammerAvailable)) continue;
                int lv = feudLevel(n.Id);
                if (lv > bestLevel || (lv == bestLevel && best != null && n.Rank > best.Rank)) { best = n; bestLevel = lv; }
            }
            if (best != null && bestLevel >= 1) return best;
            return Choose(pool, fed, st, popularity, hammerAvailable, feudLevel, null, st.Week + 100);
        }

        /// <summary>
        /// Суперник титульного матчу: чинний чемпіон дивізіону героя (NPC); якщо пояс у гравця чи вакантний — найгарячіший
        /// претендент дивізіону.
        /// </summary>
        public static NpcDef ChooseTitle(IReadOnlyList<NpcDef> pool, CareerState st, bool hammerAvailable, Func<string, int> feudLevel)
        {
            foreach (var n in pool) if (n.Id == st.BeltHolder) return n;
            NpcDef best = null; int bestKey = -1;
            foreach (var n in pool)
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

        private static NpcDef Choose(IReadOnlyList<NpcDef> pool, FederationDef fed, CareerState st, int popularity, bool hammerAvailable,
            Func<string, int> feudLevel, string exclude, int salt)
        {
            int target = TargetRank(popularity, fed.Floor, fed.Ceiling);
            NpcDef best = null; int bestScore = int.MinValue;
            foreach (var n in pool)
            {
                if (!Eligible(n, st, hammerAvailable) || n.Id == exclude) continue;
                int score = 20 - 3 * Math.Abs(n.Rank - target) + 2 * feudLevel(n.Id) + Mix(st.Seed, salt, n.Rank) % 3;
                if (n.Id == st.LastOpponent) score -= 8;
                if (st.Grudges.Contains(n.Id)) score += CareerRules.RematchBookingScore;
                if (score > bestScore) { best = n; bestScore = score; }
            }
            return best;
        }

        /// <summary>
        /// План букера: довіра 0–1 — плану немає; якщо суперник помітно сильніший за цільового — «програй чисто» (із довірою ≥ 3 м'якше:
        /// «не програй чисто»); помітно слабший — «виграй чисто»; інакше «просто виграй».
        /// </summary>
        public static BookerPlan PlanFor(NpcDef opp, int popularity, int trust) { return PlanFor(opp, popularity, trust, 0, CareerRules.MaxPopularity); }

        public static BookerPlan PlanFor(NpcDef opp, int popularity, int trust, int floor, int ceiling)
        {
            if (trust < 2) return BookerPlan.None;
            int target = TargetRank(popularity, floor, ceiling);
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
        public static bool TitleEligible(IReadOnlyList<NpcDef> pool, CareerState st, int trust, bool hammerAvailable, Func<string, int> feudLevel)
        {
            if (st.BeltHolder == CareerState.PlayerHolder) return true;
            if (trust >= CareerRules.TitleShotTrust) return true;
            foreach (var n in pool)
            {
                if (n.Id == CareerContent.HammerId && !hammerAvailable) continue;
                if (feudLevel(n.Id) >= CareerRules.MaxFeud) return true;
            }
            return false;
        }
    }
}
