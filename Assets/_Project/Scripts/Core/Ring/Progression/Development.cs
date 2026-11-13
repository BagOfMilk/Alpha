using System;
using System.Collections.Generic;

namespace Game.Core.Ring
{
    /// <summary>Перк (gdd §6): шлях і ранг (або загальний), ціна в очках розвитку.</summary>
    public sealed class PerkDef
    {
        public string Id;
        /// <summary>Ключ назви в рядках: perk.&lt;id&gt;.</summary>
        public string NameKey;
        /// <summary>Шлях; null — загальний перк.</summary>
        public RingPath? Path;
        public int Rank;
        public int Cost;
        public bool IsGeneral => !Path.HasValue;
    }

    public static class PerkCatalog
    {
        public const string SecondWind = "second_wind";
        public const string CrowdFavorite = "crowd_favorite";
        public const string EyesBehind = "eyes_behind";
        public const string ColdBlood = "cold_blood";
        public const string OwnScript = "own_script";
        public const string NoNet = "no_net";
        public const string Veteran = "veteran";
        public const string IronJaw = "iron_jaw";
        // Перки з дією (06.11.2026, пункт 2 плану лупу): міняють спосіб гри, а не цифру. Детерміновано, без нового кубика.
        public const string Comeback = "comeback";        // Герой: друге вирване утримання за матч — жетон реверсу й імпульс до фінішера
        public const string HeroOfCrowd = "hero_of_crowd"; // Герой, вінець: при ажіотажі ≥ 7 «продати» +1 імпульсу, «Вирватися» +1
        public const string BribedRef = "bribed_ref";      // Лиходій: раз за матч рефері «не дорахував» — утримання зупиняється на «два»
        public const string DirtyGame = "dirty_game";      // Лиходій, вінець: раз за матч брудний прийом без попередження й без мінуса ажіотажу
        public const string PlanBreaker = "plan_breaker";  // Бунтар: звернутий план букера — +1 популярності й +1 очко розвитку
        public const string Improvise = "improvise";       // Бунтар, вінець: раз за матч провал (не фінішер) стає «частково»

        public static readonly IReadOnlyList<PerkDef> All = new List<PerkDef>
        {
            P(SecondWind, RingPath.Face, 1, 1),
            P(CrowdFavorite, RingPath.Face, 2, 1),
            P(Comeback, RingPath.Face, 2, 2),
            P(HeroOfCrowd, RingPath.Face, 3, 3),
            P(EyesBehind, RingPath.Heel, 1, 1),
            P(ColdBlood, RingPath.Heel, 2, 1),
            P(BribedRef, RingPath.Heel, 2, 2),
            P(DirtyGame, RingPath.Heel, 3, 3),
            P(OwnScript, RingPath.Rebel, 1, 1),
            P(NoNet, RingPath.Rebel, 2, 1),
            P(PlanBreaker, RingPath.Rebel, 2, 2),
            P(Improvise, RingPath.Rebel, 3, 3),
            new PerkDef { Id = Veteran, NameKey = "perk." + Veteran, Path = null, Rank = 0, Cost = 2 },
            new PerkDef { Id = IronJaw, NameKey = "perk." + IronJaw, Path = null, Rank = 0, Cost = 2 }
        };

        private static PerkDef P(string id, RingPath path, int rank, int cost) =>
            new PerkDef { Id = id, NameKey = "perk." + id, Path = path, Rank = rank, Cost = cost };

        public static PerkDef Find(string id)
        {
            foreach (var p in All) if (p.Id == id) return p;
            return null;
        }
    }

    /// <summary>Розвиток наприкінці ворожнечі (gdd §6): очки, ранги шляху, купівля, поправки матчу.</summary>
    public static class Development
    {
        /// <summary>Ціна +1 до атрибута.</summary>
        public const int AttrCost = 2;
        public const int RankPointsStep = 2;
        /// <summary>Ранг 3 — «вінець» шляху (~6 очок шляху), перк, що визначає стиль. ПЛЕЙСХОЛДЕР (06.11.2026).</summary>
        public const int MaxRank = 3;

        /// <summary>Ранг шляху за очками: 2 → 1, 4 → 2, 6 → 3.</summary>
        public static int PathRank(int pathPoints)
        {
            int r = pathPoints / RankPointsStep;
            return r > MaxRank ? MaxRank : r;
        }

        /// <summary>Очки за матч: 1 + перемога + (ажіотаж ≥ 7) + (план виконано або вдалий звіт). gdd §6.</summary>
        public static int PointsFor(MatchResult r, FeudPlanOutcome plan)
        {
            int pts = 1;
            if (r.Winner == MatchWinner.Player) pts++;
            if (r.FinalHype >= 7) pts++;
            if (plan == FeudPlanOutcome.Fulfilled || (plan == FeudPlanOutcome.Swerved && r.FinalHype >= 7)) pts++;
            return pts;
        }

        public static bool CanBuyPerk(FeudState s, string perkId, out string reason)
        {
            var p = PerkCatalog.Find(perkId);
            if (p == null) { reason = "unknown"; return false; }
            if (s.Perks.Contains(perkId)) { reason = "owned"; return false; }
            if (p.Path.HasValue && PathRank(s.GetPath(p.Path.Value)) < p.Rank) { reason = "rank"; return false; }
            if (s.DevPoints < p.Cost) { reason = "points"; return false; }
            reason = null;
            return true;
        }

        public static bool BuyPerk(FeudState s, string perkId)
        {
            string reason;
            if (!CanBuyPerk(s, perkId, out reason)) return false;
            s.DevPoints -= PerkCatalog.Find(perkId).Cost;
            s.Perks.Add(perkId);
            return true;
        }

        /// <summary>
        /// Ціна +1 до атрибута для цього стану: <see cref="AttrCost"/>, а витривалість — з надбавкою старіння
        /// (<see cref="FeudState.StaminaSurcharge"/>, §5.5). Єдине джерело ціни для кар'єри, кампанії й екранів.
        /// </summary>
        public static int AttrCostFor(FeudState s, RingAttr a)
        {
            return AttrCost + (a == RingAttr.Stamina ? Math.Max(0, s.StaminaSurcharge) : 0);
        }

        public static bool CanBuyAttr(FeudState s, RingAttr a, out string reason)
        {
            if (s.Player.Get(a) >= WrestlerStats.MaxAttr) { reason = "max"; return false; }
            if (s.DevPoints < AttrCostFor(s, a)) { reason = "points"; return false; }
            reason = null;
            return true;
        }

        public static bool BuyAttr(FeudState s, RingAttr a)
        {
            string reason;
            if (!CanBuyAttr(s, a, out reason)) return false;
            s.DevPoints -= AttrCostFor(s, a);
            s.Player.Set(a, s.Player.Get(a) + 1);
            return true;
        }

        /// <summary>Поправки матчу від куплених перків (gdd §6). Бонус промо «Свій сценарій» — PromoRollBonus.</summary>
        public static MatchModifiers ToModifiers(IEnumerable<string> perks)
        {
            var m = new MatchModifiers();
            if (perks == null) return m;
            foreach (var id in perks)
            {
                switch (id)
                {
                    case PerkCatalog.SecondWind: m.KickOutBonus += 1; break;
                    case PerkCatalog.CrowdFavorite: m.StartHypeBonus += 2; break;
                    case PerkCatalog.EyesBehind: m.DirtyNoWarningOnPartial = true; break;
                    case PerkCatalog.ColdBlood: m.StartMomentumBonus += 2; break;
                    case PerkCatalog.NoNet: m.RiskyMoveBonus += 1; break;
                    case PerkCatalog.Veteran: m.ExtraReversalTokens += 1; break;
                    case PerkCatalog.IronJaw: m.DamageThresholdShift += 2; break;
                    case PerkCatalog.Comeback: m.Comeback = true; break;
                    case PerkCatalog.HeroOfCrowd: m.HeroOfCrowd = true; break;
                    case PerkCatalog.BribedRef: m.BribedRef = true; break;
                    case PerkCatalog.DirtyGame: m.DirtyGame = true; break;
                    case PerkCatalog.Improvise: m.Improvise = true; break;
                }
            }
            return m;
        }

        /// <summary>«Свій сценарій»: +1 до кидків промо.</summary>
        public static int PromoRollBonus(IEnumerable<string> perks)
        {
            if (perks == null) return 0;
            int b = 0;
            foreach (var id in perks) if (id == PerkCatalog.OwnScript) b++;
            return b;
        }
    }
}
