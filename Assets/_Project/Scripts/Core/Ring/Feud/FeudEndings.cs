using System;
using System.Collections.Generic;

namespace Game.Core.Ring
{
    /// <summary>Шість кінцівок ворожнечі (gdd §5.3).</summary>
    public enum FeudEnding
    {
        OwnScript = 0,
        Outcast = 1,
        CrowdFavorite = 2,
        HatedHeel = 3,
        HonorableLoss = 4,
        Forgotten = 5
    }

    public static class FeudEndings
    {
        public static readonly FeudEnding[] All =
        {
            FeudEnding.OwnScript, FeudEnding.Outcast, FeudEnding.CrowdFavorite,
            FeudEnding.HatedHeel, FeudEnding.HonorableLoss, FeudEnding.Forgotten
        };

        /// <summary>Ключ назви кінцівки в рядках.</summary>
        public static string TitleKey(FeudEnding e)
        {
            switch (e)
            {
                case FeudEnding.OwnScript: return "ending.own_script";
                case FeudEnding.Outcast: return "ending.outcast";
                case FeudEnding.CrowdFavorite: return "ending.crowd_favorite";
                case FeudEnding.HatedHeel: return "ending.hated_heel";
                case FeudEnding.HonorableLoss: return "ending.honorable_loss";
                default: return "ending.forgotten";
            }
        }

        /// <summary>
        /// Кінцівка за таблицею gdd §5.3 — перша, що виконалась. state — стан ПІСЛЯ ApplyMatchResult (домінантний шлях уже з матчем),
        /// planOutcome — те, що повернув ApplyMatchResult.
        /// </summary>
        public static FeudEnding Resolve(FeudState state, MatchResult result, FeudPlanOutcome planOutcome)
        {
            bool won = result.Winner == MatchWinner.Player;
            if (planOutcome == FeudPlanOutcome.Swerved) return won ? FeudEnding.OwnScript : FeudEnding.Outcast;
            if (won) return state.DominantPath() == RingPath.Face ? FeudEnding.CrowdFavorite : FeudEnding.HatedHeel;
            if (result.FinalHype >= 7) return FeudEnding.HonorableLoss;
            return FeudEnding.Forgotten;
        }
    }

    /// <summary>Підсумок перебору досяжності кінцівок.</summary>
    public sealed class ReachabilityReport
    {
        public readonly HashSet<FeudEnding> Reached = new HashSet<FeudEnding>();
        /// <summary>Описи тупиків (вузол без доступного варіанта тощо); порожньо — тупиків немає.</summary>
        public readonly List<string> DeadEnds = new List<string>();
        /// <summary>Перебір обрізано ліміт станів.</summary>
        public bool Truncated;
        public int StatesVisited;
        public int FinalStates;

        public bool HasDeadEnds => DeadEnds.Count > 0;

        public List<FeudEnding> Unreachable()
        {
            var l = new List<FeudEnding>();
            foreach (var e in FeudEndings.All) if (!Reached.Contains(e)) l.Add(e);
            return l;
        }
    }

    /// <summary>
    /// Перебір усіх шляхів по промо (усі доступні варіанти × усі три ступені кожного кидка × усі комбінації нагород) і всіх
    /// абстрактних результатів матчу (gdd §4.2 п.4). Мемоізація за серіалізованим станом; ліміт станів проти вибуху.
    /// </summary>
    public static class FeudReachability
    {
        public const int DefaultStateLimit = 300000;

        public static ReachabilityReport Analyze(FeudScript script, int stateLimit = DefaultStateLimit)
        {
            var rep = new ReachabilityReport();
            if (script.Promos.Count == 0)
            {
                Finish(script.NewState(), rep, new HashSet<string>());
                return rep;
            }
            var reads = ReadsOf(script);
            var visited = new HashSet<string>();
            var finals = new HashSet<string>();
            var work = new Stack<Item>();
            work.Push(new Item { Promo = 0, Node = script.Promos[0].Start, State = script.NewState() });

            while (work.Count > 0)
            {
                var it = work.Pop();
                it.State = Canon(it.State, reads);
                string key = it.Promo + "|" + it.Node + "|" + it.State.CaptureState();
                if (!visited.Add(key)) continue;
                if (++rep.StatesVisited > stateLimit) { rep.Truncated = true; break; }

                var promo = script.Promos[it.Promo];
                var r = new PromoRunner(script, promo, it.State.Clone(), it.Node);
                var n = r.Current;
                switch (n.Type)
                {
                    case PromoNodeType.End:
                        if (it.Promo + 1 < script.Promos.Count)
                            work.Push(new Item { Promo = it.Promo + 1, Node = script.Promos[it.Promo + 1].Start, State = r.State });
                        else Finish(r.State, rep, finals);
                        break;
                    case PromoNodeType.Line:
                        r.Advance();
                        work.Push(new Item { Promo = it.Promo, Node = r.Current.Id, State = r.State });
                        break;
                    case PromoNodeType.Choice:
                        {
                            bool any = false;
                            foreach (var v in r.AvailableOptions())
                            {
                                if (v.Locked) continue;
                                any = true;
                                var r2 = new PromoRunner(script, promo, it.State.Clone(), it.Node);
                                r2.Choose(v.Option.Id);
                                work.Push(new Item { Promo = it.Promo, Node = r2.Current.Id, State = r2.State });
                            }
                            if (!any) rep.DeadEnds.Add(promo.Id + "/" + n.Id + ": немає доступного варіанта");
                            break;
                        }
                    case PromoNodeType.Check:
                        foreach (RollTier tier in new[] { RollTier.Miss, RollTier.Partial, RollTier.Strong })
                        {
                            var probe = new PromoRunner(script, promo, it.State.Clone(), it.Node);
                            probe.ApplyCheckTier(tier);
                            if (!probe.AwaitingRewards)
                            {
                                work.Push(new Item { Promo = it.Promo, Node = probe.Current.Id, State = probe.State });
                                continue;
                            }
                            foreach (var combo in Combinations(n.Rewards, probe.RewardsToPick))
                            {
                                var r3 = new PromoRunner(script, promo, it.State.Clone(), it.Node);
                                r3.ApplyCheckTier(tier);
                                r3.ChooseRewards(combo);
                                work.Push(new Item { Promo = it.Promo, Node = r3.Current.Id, State = r3.State });
                            }
                        }
                        break;
                }
            }
            rep.FinalStates = finals.Count;
            return rep;
        }

        private sealed class Item { public int Promo; public string Node; public FeudState State; }

        /// <summary>Що читають умови сценарію: змінні за іменем і чи є умови на очки шляху.</summary>
        private sealed class Reads { public readonly HashSet<string> Vars = new HashSet<string>(); public readonly HashSet<string> Flags = new HashSet<string>(); public bool Paths; }

        private static Reads ReadsOf(FeudScript script)
        {
            var r = new Reads();
            foreach (var p in script.Promos)
                foreach (var n in p.Nodes)
                    if (n.Options != null)
                        foreach (var o in n.Options)
                            if (o.Requires != null)
                                foreach (var c in o.Requires)
                                {
                                    if (c.Kind == ConditionKind.Var) r.Vars.Add(c.Key);
                                    if (c.Kind == ConditionKind.Path) r.Paths = true;
                                    if (c.Kind == ConditionKind.Flag || c.Kind == ConditionKind.NotFlag) r.Flags.Add(c.Key);
                                    if (c.Kind == ConditionKind.Open) r.Flags.Add(FeudVars.ClosedFlag(c.Key));
                                }
            return r;
        }

        /// <summary>
        /// Канонічний стан для мемоізації: змінні й прапори, яких не читає жодна умова, на кінцівку не впливають (кінцівка — план,
        /// домінантний шлях і абстрактний матч), тож обнуляються; без умов на шлях очки шляхів зсуваються до мінімуму —
        /// домінантність від спільного зсуву не залежить. Без цього перебір справжньої ворожнечі вибухав за 300 тис. станів.
        /// </summary>
        private static FeudState Canon(FeudState s, Reads reads)
        {
            foreach (var v in new[] { FeudVars.Audience, FeudVars.Heat, FeudVars.Momentum, FeudVars.Trust, FeudVars.Locker })
                if (!reads.Vars.Contains(v)) s.SetVar(v, 0);
            s.DevPoints = 0;
            s.Flags.RemoveWhere(f => !reads.Flags.Contains(f));
            if (!reads.Paths)
            {
                int min = int.MaxValue;
                for (int i = 0; i < FeudState.PathCount; i++) min = Math.Min(min, s.GetPath((RingPath)i));
                for (int i = 0; i < FeudState.PathCount; i++) s.AddPath((RingPath)i, -min);
            }
            return s;
        }

        private static IEnumerable<List<string>> Combinations(List<PromoReward> rewards, int k)
        {
            var idx = new int[k];
            return CombHelper(rewards, idx, 0, 0);
        }

        private static IEnumerable<List<string>> CombHelper(List<PromoReward> rewards, int[] idx, int pos, int start)
        {
            if (pos == idx.Length)
            {
                var l = new List<string>();
                foreach (var i in idx) l.Add(rewards[i].Id);
                yield return l;
                yield break;
            }
            for (int i = start; i < rewards.Count; i++)
            {
                idx[pos] = i;
                foreach (var c in CombHelper(rewards, idx, pos + 1, i + 1)) yield return c;
            }
        }

        /// <summary>Усі абстрактні результати матчу для кінцевого стану промо → досяжні кінцівки.</summary>
        private static void Finish(FeudState final, ReachabilityReport rep, HashSet<string> finals)
        {
            if (!finals.Add(final.CaptureState())) return;
            foreach (MatchWinner w in new[] { MatchWinner.Player, MatchWinner.Rival, MatchWinner.Draw })
                foreach (int hype in new[] { 3, 8 })
                    foreach (int dirty in new[] { 0, 2 })
                        foreach (int sells in new[] { 0, 2 })
                            foreach (int kick in new[] { 0, 1 })
                            {
                                var res = new MatchResult
                                {
                                    Winner = w,
                                    Kind = w == MatchWinner.Draw ? MatchEndKind.TimeLimit : MatchEndKind.Pin,
                                    FinalHype = hype,
                                    PlayerDirtyMoves = dirty,
                                    PlayerSells = sells,
                                    PlayerKickoutsAtTwo = kick,
                                    Rounds = 10
                                };
                                var st = final.Clone();
                                var outcome = st.ApplyMatchResult(res);
                                rep.Reached.Add(FeudEndings.Resolve(st, res, outcome));
                            }
        }
    }
}
