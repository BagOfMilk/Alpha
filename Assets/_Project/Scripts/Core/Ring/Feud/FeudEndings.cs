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
    /// Перебір усіх шляхів по порядку кроків ворожнечі (усі доступні варіанти × усі три ступені кожного кидка × усі комбінації
    /// нагород) і абстрактних результатів кожного матчу (gdd §4.2 п.4, §5.2). Проміжні матчі — невеликий набір характерних
    /// результатів (перемога, поразка, нічия, чисто/брудно, вплив на шляхи й план), розв'язка — повний перебір. Мемоізація за
    /// серіалізованим канонічним станом; ліміт станів проти вибуху.
    /// </summary>
    public static class FeudReachability
    {
        public const int DefaultStateLimit = 300000;

        public static ReachabilityReport Analyze(FeudScript script, int stateLimit = DefaultStateLimit)
        {
            var rep = new ReachabilityReport();
            if (script.Flow.Count == 0)
            {
                rep.DeadEnds.Add("flow: порожній порядок кроків");
                return rep;
            }
            var reads = new Ahead(script);
            var visited = new HashSet<string>();
            var finals = new HashSet<string>();
            var work = new Stack<Item>();
            Enter(script, 0, script.NewState(), work, rep, finals, reads);

            while (work.Count > 0)
            {
                var it = work.Pop();
                it.State = reads.Canon(it.State, it.Step, it.Node);
                string key = it.Step + "|" + it.Node + "|" + it.State.CaptureState();
                if (!visited.Add(key)) continue;
                if (++rep.StatesVisited > stateLimit) { rep.Truncated = true; break; }
                var promo = script.Promos[script.Flow[it.Step].Index];
                var r = new PromoRunner(script, promo, it.State.Clone(), it.Node);
                var n = r.Current;
                switch (n.Type)
                {
                    case PromoNodeType.End:
                        Enter(script, it.Step + 1, r.State, work, rep, finals, reads);
                        break;
                    case PromoNodeType.Line:
                        r.Advance();
                        work.Push(new Item { Step = it.Step, Node = r.Current.Id, State = r.State });
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
                                work.Push(new Item { Step = it.Step, Node = r2.Current.Id, State = r2.State });
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
                                work.Push(new Item { Step = it.Step, Node = probe.Current.Id, State = probe.State });
                                continue;
                            }
                            foreach (var combo in Combinations(n.Rewards, probe.RewardsToPick))
                            {
                                var r3 = new PromoRunner(script, promo, it.State.Clone(), it.Node);
                                r3.ApplyCheckTier(tier);
                                r3.ChooseRewards(combo);
                                work.Push(new Item { Step = it.Step, Node = r3.Current.Id, State = r3.State });
                            }
                        }
                        break;
                }
            }
            rep.FinalStates = finals.Count;
            return rep;
        }

        private sealed class Item { public int Step; public string Node; public FeudState State; }

        /// <summary>
        /// Увійти в крок порядку: промо — стартовий вузол (запускає раннер, який проходить branch сам); матч — розгалуження за
        /// абстрактними результатами й перехід до наступного кроку; розв'язка — кінцівка.
        /// </summary>
        private static void Enter(FeudScript script, int step, FeudState state, Stack<Item> work, ReachabilityReport rep,
                                  HashSet<string> finals, Ahead reads)
        {
            if (step >= script.Flow.Count) return;
            var st = script.Flow[step];
            if (!st.IsMatch)
            {
                var probe = new PromoRunner(script, script.Promos[st.Index], state.Clone());
                work.Push(new Item { Step = step, Node = probe.Current.Id, State = state });
                return;
            }
            var def = script.Matches[st.Index];
            foreach (var res in def.Decisive ? FinalResults() : MiddleResults())
            {
                var s = state.Clone();
                var outcome = s.CompleteMatch(res, def);
                if (def.Decisive)
                {
                    // Ворожнеча скінчена: Resolve читає стан після підсумку (домінантний шлях).
                    string fkey = s.CaptureState() + "|" + res.Winner + res.FinalHype + res.Kind + outcome;
                    if (finals.Add(fkey)) rep.Reached.Add(FeudEndings.Resolve(s, res, outcome));
                }
                else Enter(script, step + 1, reads.Canon(s, step + 1, null), work, rep, finals, reads);
            }
        }

        /// <summary>
        /// Що читають умови сценарію ВІД кроку step і далі (змінні, прапори, чи є умови на шляхи), і скільки очок кожного шляху
        /// ще можна здобути (верхня межа). Це дає канонічний стан: усе, чого вже ніхто не читає, на кінцівку не впливає.
        /// </summary>
        private sealed class Ahead
        {
            private sealed class Reads { public readonly HashSet<string> Vars = new HashSet<string>(); public readonly HashSet<string> Flags = new HashSet<string>(); public bool Paths; }
            private readonly FeudScript _script;
            private readonly Reads[] _reads;
            private readonly int[] _gain;
            private readonly Dictionary<string, Reads> _nodeReads = new Dictionary<string, Reads>();
            private readonly Dictionary<string, int> _nodeGain = new Dictionary<string, int>();

            public Ahead(FeudScript script)
            {
                _script = script;
                int n = script.Flow.Count;
                _reads = new Reads[n + 1];
                _gain = new int[n + 1];
                _reads[n] = new Reads();
                for (int i = n - 1; i >= 0; i--)
                {
                    var r = new Reads();
                    foreach (var v in _reads[i + 1].Vars) r.Vars.Add(v);
                    foreach (var f in _reads[i + 1].Flags) r.Flags.Add(f);
                    r.Paths = _reads[i + 1].Paths;
                    var st = script.Flow[i];
                    int gain = 0;
                    if (st.IsMatch)
                    {
                        foreach (var rule in script.Matches[st.Index].Plans) ReadCond(r, rule.Requires);
                        gain = 1;   // підсумок матчу додає шляху щонайбільше +1 (брудні → Лиходій, «продати» → Герой, звернення → Бунтар)
                    }
                    else
                    {
                        foreach (var nd in script.Promos[st.Index].Nodes)
                        {
                            if (nd.Options != null) foreach (var o in nd.Options) ReadCond(r, o.Requires);
                            if (nd.Cases != null) foreach (var c in nd.Cases) ReadCond(r, c.Requires);
                            gain += PathGain(nd);
                        }
                    }
                    _reads[i] = r;
                    _gain[i] = _gain[i + 1] + gain;
                }
            }

            private static void ReadCond(Reads r, IEnumerable<FeudCondition> conds)
            {
                if (conds == null) return;
                foreach (var c in conds)
                {
                    if (c.Kind == ConditionKind.Var) r.Vars.Add(c.Key);
                    if (c.Kind == ConditionKind.Path) r.Paths = true;
                    if (c.Kind == ConditionKind.Flag || c.Kind == ConditionKind.NotFlag) r.Flags.Add(c.Key);
                    if (c.Kind == ConditionKind.Open) r.Flags.Add(FeudVars.ClosedFlag(c.Key));
                }
            }

            /// <summary>Скільки очок шляху вузол може дати за один прохід: вибір — найбільше з варіантів, кидок — усі нагороди (з запасом).</summary>
            private static int PathGain(PromoNode n)
            {
                int best = 0;
                if (n.Type == PromoNodeType.Choice)
                {
                    foreach (var o in n.Options) best = Math.Max(best, Sum(o.Effects));
                }
                else if (n.Type == PromoNodeType.Check)
                {
                    int all = 0;
                    foreach (var r in n.Rewards) all += Sum(r.Effects);
                    best = Math.Max(all, Sum(n.Fail));
                }
                return best;
            }

            private static int Sum(List<FeudEffect> effects)
            {
                int s = 0;
                if (effects != null) foreach (var e in effects) if (e.Kind == EffectKind.Path && e.Amount > 0) s += e.Amount;
                return s;
            }

            /// <summary>Що читають умови від вузла node промо кроку step і далі (досяжні вузли цього промо + пізніші кроки).</summary>
            private Reads ReadsFrom(int step, string node)
            {
                string key = step + "|" + node;
                Reads r;
                if (_nodeReads.TryGetValue(key, out r)) return r;
                r = new Reads();
                var after = _reads[step + 1];
                foreach (var v in after.Vars) r.Vars.Add(v);
                foreach (var f in after.Flags) r.Flags.Add(f);
                r.Paths = after.Paths;
                var promo = _script.Promos[_script.Flow[step].Index];
                var seen = new HashSet<string>();
                var stack = new Stack<string>();
                stack.Push(node);
                seen.Add(node);
                while (stack.Count > 0)
                {
                    var nd = promo.Find(stack.Pop());
                    if (nd == null) continue;
                    if (nd.Options != null) foreach (var o in nd.Options) ReadCond(r, o.Requires);
                    if (nd.Cases != null) foreach (var c in nd.Cases) ReadCond(r, c.Requires);
                    foreach (var t in nd.Targets()) if (t != null && seen.Add(t)) stack.Push(t);
                }
                _nodeReads[key] = r;
                return r;
            }

            /// <summary>Скільки очок шляху ще можна здобути від вузла node: найдовший маршрут по промо + пізніші кроки.</summary>
            private int GainFrom(int step, string node)
            {
                var promo = _script.Promos[_script.Flow[step].Index];
                return _gain[step + 1] + Longest(step, promo, node, new HashSet<string>());
            }

            private int Longest(int step, PromoDef promo, string node, HashSet<string> onPath)
            {
                string key = step + "|" + node;
                int memo;
                if (_nodeGain.TryGetValue(key, out memo)) return memo;
                var nd = promo.Find(node);
                if (nd == null || !onPath.Add(node)) return 0;
                int best = 0;
                foreach (var t in nd.Targets()) if (t != null) best = Math.Max(best, Longest(step, promo, t, onPath));
                onPath.Remove(node);
                int total = PathGain(nd) + best;
                _nodeGain[key] = total;
                return total;
            }

            /// <summary>
            /// Канонічний стан для мемоізації перед кроком step: змінні й прапори, яких уже не читає жодна умова, на кінцівку не
            /// впливають (кінцівка — план, домінантний шлях і абстрактні матчі), тож обнуляються; умови матчу й напарник кінцівку не
            /// міняють. Без умов на шлях очки шляхів зводяться до різниці з лідером, а шлях, що відстав на більше, ніж ще можна
            /// надолужити, — до «безнадійно позаду»: домінантність від цього не змінюється, а станів стає в рази менше.
            /// </summary>
            public FeudState Canon(FeudState s, int step, string node)
            {
                Reads reads;
                int gain;
                if (step >= _script.Flow.Count) { reads = _reads[_script.Flow.Count]; gain = 0; }
                else if (_script.Flow[step].IsMatch) { reads = _reads[step]; gain = _gain[step]; }
                else
                {
                    if (node == null) node = _script.Promos[_script.Flow[step].Index].Start;
                    reads = ReadsFrom(step, node);
                    gain = GainFrom(step, node);
                }
                foreach (var v in new[] { FeudVars.Audience, FeudVars.Heat, FeudVars.Momentum, FeudVars.Trust, FeudVars.Locker })
                    if (!reads.Vars.Contains(v)) s.SetVar(v, 0);
                s.DevPoints = 0;
                s.Stipulations = MatchStipulations.None;
                s.HasAlly = false;
                s.Flags.RemoveWhere(f => !reads.Flags.Contains(f));
                if (!reads.Paths)
                {
                    int slack = gain + 1;
                    int max = 0;
                    for (int i = 0; i < FeudState.PathCount; i++) max = Math.Max(max, s.GetPath((RingPath)i));
                    for (int i = 0; i < FeudState.PathCount; i++)
                    {
                        int behind = max - s.GetPath((RingPath)i);
                        if (behind > slack) s.AddPath((RingPath)i, behind - slack);   // підтягнути до межі «не надолужити»
                    }
                    int min = int.MaxValue;
                    for (int i = 0; i < FeudState.PathCount; i++) min = Math.Min(min, s.GetPath((RingPath)i));
                    for (int i = 0; i < FeudState.PathCount; i++) s.AddPath((RingPath)i, -min);
                }
                return s;
            }
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

        private static MatchResult R(MatchWinner w, MatchEndKind k, int hype, int dirty, int sells, int kick)
        {
            return new MatchResult { Winner = w, Kind = k, FinalHype = hype, PlayerDirtyMoves = dirty, PlayerSells = sells, PlayerKickoutsAtTwo = kick, Rounds = 10 };
        }

        /// <summary>Характерні результати проміжного матчу: перемога/поразка/нічия, чисто й брудно, ажіотаж, вплив на шляхи й план.</summary>
        private static IEnumerable<MatchResult> MiddleResults()
        {
            yield return R(MatchWinner.Player, MatchEndKind.Pin, 8, 0, 0, 0);
            yield return R(MatchWinner.Player, MatchEndKind.Pin, 3, 2, 0, 0);
            yield return R(MatchWinner.Player, MatchEndKind.Pin, 8, 0, 2, 0);
            yield return R(MatchWinner.Player, MatchEndKind.Disqualification, 3, 0, 0, 0);
            yield return R(MatchWinner.Player, MatchEndKind.Pin, 8, 2, 2, 1);
            yield return R(MatchWinner.Rival, MatchEndKind.Pin, 8, 0, 2, 1);
            yield return R(MatchWinner.Rival, MatchEndKind.Pin, 3, 2, 0, 0);
            yield return R(MatchWinner.Rival, MatchEndKind.Disqualification, 8, 0, 0, 0);
            yield return R(MatchWinner.Rival, MatchEndKind.CountOut, 3, 2, 2, 1);
            yield return R(MatchWinner.Draw, MatchEndKind.TimeLimit, 8, 0, 0, 0);
            yield return R(MatchWinner.Draw, MatchEndKind.TimeLimit, 3, 2, 2, 0);
            yield return R(MatchWinner.Draw, MatchEndKind.DoubleCountOut, 3, 0, 0, 0);
        }

        /// <summary>Усі абстрактні результати розв'язки: переможець × ажіотаж × брудні × «продати» × вирвані «на два».</summary>
        private static IEnumerable<MatchResult> FinalResults()
        {
            foreach (MatchWinner w in new[] { MatchWinner.Player, MatchWinner.Rival, MatchWinner.Draw })
                foreach (int hype in new[] { 3, 8 })
                    foreach (int dirty in new[] { 0, 2 })
                        foreach (int sells in new[] { 0, 2 })
                            foreach (int kick in new[] { 0, 1 })
                                yield return R(w, w == MatchWinner.Draw ? MatchEndKind.TimeLimit : MatchEndKind.Pin, hype, dirty, sells, kick);
        }
    }
}
