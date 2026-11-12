using System;
using System.Collections.Generic;
using Game.Core.Randomness;
using Game.Core.Ring.Match;

namespace Game.Core.Ring
{
    /// <summary>Чим скінчити матч: перемогти, програти чи як вийде.</summary>
    public enum MatchAim { Any = 0, Win = 1, Lose = 2 }

    /// <summary>Як битися: чисто (без брудних і без виходу за ринг), брудно (щонайменше два брудні прийоми) чи як вийде.</summary>
    public enum MatchStyle { Any = 0, Clean = 1, Dirty = 2 }

    /// <summary>Яким лишити ажіотаж фіналу: високим (≥ 7, поріг кінцівки), низьким (&lt; 7) чи як вийде.</summary>
    public enum HypeAim { Any = 0, High = 1, Low = 2 }

    /// <summary>Ціль бота на матч (огляд QA, <c>-ring-shot-ending</c>): результат, стиль, ажіотаж і чи продавати.</summary>
    public struct MatchGoal
    {
        public MatchAim Aim;
        public MatchStyle Style;
        public HypeAim Hype;
        /// <summary>Продавати удари, доки продажів менше за <see cref="FeudBot.SellsForFace"/> (шлях героя).</summary>
        public bool Sell;

        public override string ToString()
        {
            return Aim.ToString().ToLowerInvariant() + "/" + Style.ToString().ToLowerInvariant() + "/hype-" + Hype.ToString().ToLowerInvariant()
                   + (Sell ? "/sell" : "");
        }
    }

    /// <summary>
    /// Бот кінцівок для огляду QA (п. 8 П5, 11.11.2026): веде ворожнечу до заданої кінцівки gdd §5.3 — обирає репліки плану
    /// (через стан після варіанта й план за замовчуванням наступного матчу), нагороди кидка, ціль кожного матчу і хід гравця в
    /// матчі. Працює лише через публічні API промо й рушія, як і <see cref="MatchAi"/>; правил гри не змінює. Хост огляду
    /// (<c>RingGameBootstrap.Career.cs</c>) тисне ті самі цифри, що й гравець; тест <c>FeudBotTests</c> доводить, що всі шість
    /// кінцівок досяжні цим ботом на справжньому сценарії.
    /// </summary>
    public static class FeudBot
    {
        /// <summary>Поріг ажіотажу фіналу для «Чесної поразки» (<see cref="FeudEndings.Resolve"/>).</summary>
        public const int HighHype = 7;
        /// <summary>З яким ажіотажем «висока» поразка перестає битися: запас понад поріг на прийоми суперника й повтори.</summary>
        public const int HighHypeStop = 8;
        /// <summary>Продажів у матчі для +1 до шляху героя (<see cref="FeudState"/>).</summary>
        public const int SellsForFace = 2;
        /// <summary>Брудних прийомів у матчі для +1 до шляху лиходія.</summary>
        public const int DirtyForHeel = 2;

        // ---------------- назви кінцівок для прапорця запуску ----------------

        private static readonly string[] EndingNames = { "own_script", "outcast", "crowd_favorite", "hated_heel", "honorable_loss", "forgotten" };

        public static string EndingName(FeudEnding e) { return EndingNames[(int)e]; }

        /// <summary>Назва кінцівки з прапорця: <c>own_script</c>, <c>hated-heel</c>, <c>HatedHeel</c> чи номер 0–5.</summary>
        public static bool TryParseEnding(string s, out FeudEnding e)
        {
            e = FeudEnding.CrowdFavorite;
            if (string.IsNullOrEmpty(s)) return false;
            string k = s.Trim().ToLowerInvariant().Replace('-', '_');
            int n;
            if (int.TryParse(k, out n) && n >= 0 && n < EndingNames.Length) { e = (FeudEnding)n; return true; }
            for (int i = 0; i < EndingNames.Length; i++)
                if (EndingNames[i] == k || EndingNames[i].Replace("_", "") == k) { e = (FeudEnding)i; return true; }
            return false;
        }

        // ---------------- ціль матчу ----------------

        /// <summary>
        /// Ціль матчу під кінцівку. Вирішальний матч ворожнечі — сама кінцівка за таблицею §5.3 з урахуванням плану, яким він піде
        /// (звернути план можна лише «проти» нього). Інші матчі (проміжні, щотижневі, титульні) — виконати план букера (звернутий план
        /// додає шлях бунтаря й ламає домінантний шлях) і стилем накопичити потрібний шлях: героєві — чисто з продажем, лиходієві —
        /// брудно.
        /// </summary>
        public static MatchGoal GoalFor(FeudEnding target, bool decisive, BookerPlan plan)
        {
            var g = new MatchGoal();
            if (target == FeudEnding.CrowdFavorite) { g.Style = MatchStyle.Clean; g.Sell = true; }
            else if (target == FeudEnding.HatedHeel) g.Style = MatchStyle.Dirty;
            if (!decisive)
            {
                g.Aim = plan == BookerPlan.LoseClean ? MatchAim.Lose : MatchAim.Win;
                return g;
            }
            switch (target)
            {
                case FeudEnding.OwnScript:
                case FeudEnding.CrowdFavorite:
                case FeudEnding.HatedHeel:
                    g.Aim = MatchAim.Win;
                    break;
                case FeudEnding.Outcast:
                    // «Не програй чисто» звертається лише чистою поразкою; інші плани — будь-якою.
                    g.Aim = MatchAim.Lose;
                    if (plan == BookerPlan.NoCleanLoss) g.Style = MatchStyle.Clean;
                    break;
                case FeudEnding.HonorableLoss:
                    g.Aim = MatchAim.Lose; g.Hype = HypeAim.High; g.Style = MatchStyle.Clean;
                    break;
                default:
                    g.Aim = MatchAim.Lose; g.Hype = HypeAim.Low;
                    break;
            }
            return g;
        }

        /// <summary>Чи веде цей план вирішального матчу до кінцівки за якогось результату (інакше ціль недосяжна з цього стану).</summary>
        public static bool PlanAllows(FeudEnding target, BookerPlan plan)
        {
            switch (target)
            {
                case FeudEnding.OwnScript: return plan == BookerPlan.LoseClean;
                case FeudEnding.Outcast: return plan == BookerPlan.WinClean || plan == BookerPlan.WinAny || plan == BookerPlan.NoCleanLoss;
                case FeudEnding.CrowdFavorite:
                case FeudEnding.HatedHeel: return plan != BookerPlan.LoseClean;
                default: return plan == BookerPlan.LoseClean || plan == BookerPlan.None || plan == BookerPlan.NoCleanLoss;
            }
        }

        /// <summary>Порядок переваги планів для наступного матчу (перший — найкращий).</summary>
        public static BookerPlan[] PlanOrder(FeudEnding target, bool decisive)
        {
            if (decisive)
                switch (target)
                {
                    case FeudEnding.OwnScript: return new[] { BookerPlan.LoseClean };
                    case FeudEnding.Outcast: return new[] { BookerPlan.WinAny, BookerPlan.WinClean, BookerPlan.NoCleanLoss };
                    case FeudEnding.CrowdFavorite: return new[] { BookerPlan.WinClean, BookerPlan.WinAny, BookerPlan.None, BookerPlan.NoCleanLoss };
                    case FeudEnding.HatedHeel: return new[] { BookerPlan.WinAny, BookerPlan.None, BookerPlan.NoCleanLoss, BookerPlan.WinClean };
                    default: return new[] { BookerPlan.LoseClean, BookerPlan.None };
                }
            // Проміжний: план, який ціль матчу виконує найпевніше (виконаний план — +1 довіри, звернутий — шлях бунтаря).
            switch (target)
            {
                case FeudEnding.CrowdFavorite:
                    return new[] { BookerPlan.WinClean, BookerPlan.NoCleanLoss, BookerPlan.WinAny, BookerPlan.None, BookerPlan.LoseClean };
                case FeudEnding.HatedHeel:
                    return new[] { BookerPlan.NoCleanLoss, BookerPlan.WinAny, BookerPlan.None, BookerPlan.WinClean, BookerPlan.LoseClean };
                default:
                    return new[] { BookerPlan.NoCleanLoss, BookerPlan.WinAny, BookerPlan.WinClean, BookerPlan.None, BookerPlan.LoseClean };
            }
        }

        /// <summary>Шлях, який кінцівці треба накопичити (лише «Улюбленець» і «Лиходій» залежать від домінантного шляху).</summary>
        private static int PathScore(FeudEnding target, RingPath p, int amount)
        {
            switch (target)
            {
                case FeudEnding.CrowdFavorite: return amount * (p == RingPath.Face ? 2 : p == RingPath.Heel ? -1 : -2);
                case FeudEnding.HatedHeel: return amount * (p == RingPath.Heel ? 2 : p == RingPath.Rebel ? 1 : -2);
                default: return 0;
            }
        }

        private static int EffectsScore(FeudEnding target, IEnumerable<FeudEffect> effects)
        {
            int s = 0;
            foreach (var e in effects)
            {
                if (e.Kind == EffectKind.Path)
                {
                    RingPath p;
                    if (FeudVars.TryPath(e.Key, out p)) s += PathScore(target, p, e.Amount);
                }
                // довіра відкриває репліки плану з умовою «довіра ≥ 3»
                else if (e.Kind == EffectKind.Var && e.Key == FeudVars.Trust) s += 2 * e.Amount;
                else if (e.Kind == EffectKind.Var && e.Key == FeudVars.Audience) s += e.Amount;
            }
            return s;
        }

        // ---------------- промо ----------------

        /// <summary>Наступний матч ворожнечі після поточного кроку (null — матчів далі немає).</summary>
        public static MatchDef NextMatch(RingCampaign c)
        {
            if (c == null) return null;
            for (int i = c.StepIndex; i < c.Script.Flow.Count; i++)
                if (c.Script.Flow[i].IsMatch) return c.Script.Matches[c.Script.Flow[i].Index];
            return null;
        }

        /// <summary>
        /// Варіант репліки під кінцівку: лише незаблоковані; спершу — план, яким піде наступний матч після цього варіанта (стан
        /// після ефектів + план за замовчуванням матчу з даних), за <see cref="PlanOrder"/>; далі — шлях і довіра з ефектів; рівні —
        /// перший за порядком. <paramref name="nextMatch"/> null (сцена закулісся) — план щотижневого матчу, не вирішального.
        /// </summary>
        public static string ChooseOption(PromoRunner r, FeudEnding target, MatchDef nextMatch)
        {
            bool decisive = nextMatch != null && nextMatch.Decisive;
            var order = PlanOrder(target, decisive);
            string best = null;
            int bestScore = int.MinValue;
            foreach (var v in r.AvailableOptions())
            {
                if (v.Locked) continue;
                var o = v.Option;
                bool setsPlan = false;
                foreach (var e in o.Effects)
                    if (e.Kind == EffectKind.Plan || (e.Kind == EffectKind.Career && e.Key == "plan")) setsPlan = true;
                int score = EffectsScore(target, o.Effects);
                if (o.Path.HasValue) score += PathScore(target, o.Path.Value, 1);
                if (setsPlan)
                {
                    BookerPlan plan = PlanAfter(r.State, o, nextMatch);
                    int rank = Array.IndexOf(order, plan);
                    // план, що веде до кінцівки, важить більше за будь-який шлях; поза порядком — найгірше
                    score += rank < 0 ? -1000 : (order.Length - rank) * 100;
                }
                if (score > bestScore) { bestScore = score; best = o.Id; }
            }
            return best;
        }

        /// <summary>План наступного матчу, якщо обрати варіант: ефекти на копії стану, далі план за замовчуванням із даних матчу.</summary>
        private static BookerPlan PlanAfter(FeudState state, PromoOption o, MatchDef nextMatch)
        {
            foreach (var e in o.Effects)
            {
                BookerPlan cp;
                if (e.Kind == EffectKind.Career && e.Key == "plan" && FeudVars.TryPlan(e.Target, out cp)) return cp;
            }
            var s = state.Clone();
            s.Apply(o.Effects);
            s.PrepareMatch(nextMatch);
            return s.Plan;
        }

        /// <summary>Номер варіанта на екрані (1…N, як <c>PromoScreenModel.Choices</c>: заблоковані теж мають номер); 0 — немає.</summary>
        public static int OptionNumber(PromoRunner r, string optionId)
        {
            int i = 1;
            foreach (var v in r.AvailableOptions())
            {
                if (v.Option.Id == optionId) return i;
                i++;
            }
            return 0;
        }

        /// <summary>Нагороди кидка під кінцівку: рівно <see cref="PromoRunner.RewardsToPick"/>, за оцінкою ефектів, рівні — за порядком.</summary>
        public static List<string> ChooseRewards(PromoRunner r, FeudEnding target)
        {
            var all = new List<PromoReward>(r.Current.Rewards);
            var scores = new Dictionary<string, int>();
            foreach (var w in all) scores[w.Id] = EffectsScore(target, w.Effects);
            var sorted = new List<PromoReward>(all);
            // стабільне сортування: за оцінкою, рівні — за порядком у даних
            sorted.Sort((a, b) =>
            {
                int c = scores[b.Id].CompareTo(scores[a.Id]);
                return c != 0 ? c : all.IndexOf(a).CompareTo(all.IndexOf(b));
            });
            var ids = new List<string>();
            for (int i = 0; i < sorted.Count && ids.Count < r.RewardsToPick; i++) ids.Add(sorted[i].Id);
            return ids;
        }

        /// <summary>Номер нагороди на екрані (1…N за порядком у вузлі, як <c>PromoScreenModel.Rewards</c>); 0 — немає.</summary>
        public static int RewardNumber(PromoRunner r, string rewardId)
        {
            for (int i = 0; i < r.Current.Rewards.Count; i++) if (r.Current.Rewards[i].Id == rewardId) return i + 1;
            return 0;
        }

        /// <summary>Один крок промо ботом: репліка — далі, вибір — <see cref="ChooseOption"/>, кидок — кубиком, нагороди — <see cref="ChooseRewards"/>.</summary>
        public static void StepPromo(PromoRunner r, FeudEnding target, MatchDef nextMatch, IDiceRoller dice)
        {
            switch (r.Current.Type)
            {
                case PromoNodeType.Line: r.Advance(); break;
                case PromoNodeType.Choice:
                    var id = ChooseOption(r, target, nextMatch);
                    if (id == null) throw new InvalidOperationException("промо " + r.Promo.Id + ", вузол " + r.Current.Id + ": усі варіанти заблоковані");
                    r.Choose(id);
                    break;
                case PromoNodeType.Check:
                    if (r.AwaitingRewards) r.ChooseRewards(ChooseRewards(r, target));
                    else r.RollCheck(dice);
                    break;
                default: throw new InvalidOperationException("промо " + r.Promo.Id + " уже скінчилось");
            }
        }

        // ---------------- хід у матчі ----------------

        /// <summary>
        /// Дія гравця під ціль. Перемога — дії <see cref="MatchAi"/> з поправками стилю: чисто — без брудних і без виходу за ринг
        /// (перемога відліком не чиста); брудно — два брудні прийоми, поки попереджень менше за два (третє — дискваліфікація).
        /// Поразка — не закінчувати матч самому: «висока» б'ється без утримань і фінішерів, доки ажіотаж &lt; <see cref="HighHypeStop"/>,
        /// далі віддає ініціативу; «низька» гасить ажіотаж (брудний прийом героя −2, «Відновитись» −1) і віддає ініціативу.
        /// </summary>
        public static MatchActionId ChooseAction(MatchEngine e, MatchGoal g)
        {
            if (e.CurrentActor != MatchSide.Player || g.Aim == MatchAim.Any && g.Style == MatchStyle.Any && g.Hype == HypeAim.Any)
                return MatchAi.ChooseAction(e);
            var me = e.State.Player;
            var opts = e.GetAvailableActions();
            var role = e.Setup.PlayerRole;
            if (g.Aim == MatchAim.Lose)
            {
                if (g.Hype == HypeAim.Low)
                {
                    if (role == RingPath.Face && Ok(opts, MatchActionId.Dirty) && e.State.Hype >= 2) return MatchActionId.Dirty;
                    if (Ok(opts, MatchActionId.Recover) && e.State.Hype > 0) return MatchActionId.Recover;
                    if (e.State.Outside && Ok(opts, MatchActionId.ReturnToRing)) return MatchActionId.ReturnToRing;
                    return MatchActionId.CatchBreath;
                }
                if (g.Hype == HypeAim.High && e.State.Hype < HighHypeStop)
                {
                    if (e.HopeSpotAvailable) return MatchActionId.HopeSpot;
                    if (Ok(opts, MatchActionId.GetUp)) return MatchActionId.GetUp;
                    if (e.State.Outside && Ok(opts, MatchActionId.ReturnToRing)) return MatchActionId.ReturnToRing;
                    if (me.Stamina <= 0) return MatchActionId.CatchBreath;
                    if (Ok(opts, MatchActionId.TopRope) && Spare(me, MatchActionId.TopRope)) return MatchActionId.TopRope;
                    if (Ok(opts, MatchActionId.Throw) && Spare(me, MatchActionId.Throw)) return MatchActionId.Throw;
                    if (Ok(opts, MatchActionId.Taunt)) return MatchActionId.Taunt;
                    if (Ok(opts, MatchActionId.Strike)) return MatchActionId.Strike;
                    return MatchActionId.CatchBreath;
                }
                if (e.State.Outside && Ok(opts, MatchActionId.ReturnToRing)) return MatchActionId.ReturnToRing;
                return MatchActionId.CatchBreath;
            }
            // перемога або «як вийде»
            if (g.Style == MatchStyle.Dirty && e.State.PlayerDirtyMoves < DirtyForHeel && me.Warnings < MatchRules.WarningsToDq - 1
                && Ok(opts, MatchActionId.Dirty) && !Ok(opts, MatchActionId.Finisher))
                return MatchActionId.Dirty;
            var id = MatchAi.ChooseAction(e);
            if (g.Style == MatchStyle.Clean && (id == MatchActionId.Dirty || id == MatchActionId.LeaveRing))
            {
                if (Ok(opts, MatchActionId.Strike)) return MatchActionId.Strike;
                if (Ok(opts, MatchActionId.Throw)) return MatchActionId.Throw;
                return MatchActionId.CatchBreath;
            }
            if (g.Style == MatchStyle.Dirty && id == MatchActionId.Dirty && me.Warnings >= MatchRules.WarningsToDq - 1)
                return Ok(opts, MatchActionId.Strike) ? MatchActionId.Strike : MatchActionId.CatchBreath;
            return id;
        }

        /// <summary>Реакція гравця: продати, доки продажів мало (ціль героя), або поразка — продати завжди; інакше — як <see cref="MatchAi"/>.</summary>
        public static ReactionChoice ChooseReaction(MatchEngine e, MatchGoal g)
        {
            var pr = e.PendingReaction;
            if (pr.Defender != MatchSide.Player) return MatchAi.ChooseReaction(e);
            if (g.Aim == MatchAim.Lose) return ReactionChoice.Sell;
            if (g.Sell && e.State.PlayerSells < SellsForFace && pr.Action != MatchActionId.Finisher) return ReactionChoice.Sell;
            return MatchAi.ChooseReaction(e);
        }

        /// <summary>Докрутка гравця: поразка не рятує кидків (крім «високої» — їй потрібні влучні прийоми); інакше — як <see cref="MatchAi"/>.</summary>
        public static int ChooseBoost(MatchEngine e, MatchGoal g)
        {
            if (e.PendingRoll.Actor == MatchSide.Player && g.Aim == MatchAim.Lose && g.Hype != HypeAim.High) return 0;
            return MatchAi.ChooseBoost(e);
        }

        /// <summary>Один крок за фазою рушія: гравець — під ціль, суперник — <see cref="MatchAi"/>.</summary>
        public static void Step(MatchEngine e, MatchGoal g)
        {
            switch (e.Phase)
            {
                case MatchPhase.AwaitAction:
                    if (e.CurrentActor != MatchSide.Player) { MatchAi.Step(e); return; }
                    var id = ChooseAction(e, g);
                    if (id == MatchActionId.CatchBreath) e.EndTurn();
                    else e.Perform(id, id == MatchActionId.Finisher ? e.Setup.FinisherOf(e.CurrentActor) : null);
                    break;
                case MatchPhase.AwaitReaction: e.React(ChooseReaction(e, g)); break;
                case MatchPhase.AwaitBoost: e.Commit(ChooseBoost(e, g)); break;
            }
        }

        private static bool Spare(Fighter me, MatchActionId id) { return me.Stamina >= MatchRules.Cost(id) + MatchAi.StaminaReserve; }

        private static bool Ok(IReadOnlyList<ActionOption> opts, MatchActionId id)
        {
            for (int i = 0; i < opts.Count; i++) if (opts[i].Id == id) return opts[i].Available;
            return false;
        }
    }
}
