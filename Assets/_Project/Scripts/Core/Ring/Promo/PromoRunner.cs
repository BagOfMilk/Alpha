using System;
using System.Collections.Generic;
using Game.Core.Randomness;

namespace Game.Core.Ring
{
    /// <summary>Варіант вибору, як його бачить гравець: доступний або сірий із причиною (gdd §4.1).</summary>
    public sealed class PromoOptionView
    {
        public PromoOption Option;
        public bool Locked;
        /// <summary>Перша невиконана умова (для сірого варіанта); null, якщо доступний.</summary>
        public FeudCondition Reason;
    }

    /// <summary>
    /// Курсор по графу одного промо. Кожен вибір і нагорода застосовують ефекти до FeudState. Рядки не знає — віддає ключі.
    /// Потік: line → Advance(); choice → AvailableOptions()/Choose(); check → PreviewChances()/RollCheck()/ChooseRewards(); end → IsFinished.
    /// </summary>
    public sealed class PromoRunner
    {
        public FeudScript Script { get; }
        public PromoDef Promo { get; }
        public FeudState State { get; }
        public PromoNode Current { get; private set; }

        private bool _rolled;

        /// <summary>Ступінь останнього кидка (лишається й після переходу далі, щоб UI показав результат; null, доки не кидали).</summary>
        public RollTier? LastTier { get; private set; }
        /// <summary>Сума останнього кидка 2d6 + модифікатор (0, якщо ступінь задано напряму).</summary>
        public int LastTotal { get; private set; }
        /// <summary>Кидок зроблено, і треба обрати нагороди.</summary>
        public bool AwaitingRewards { get; private set; }
        /// <summary>Скільки нагород треба обрати (рівно).</summary>
        public int RewardsToPick { get; private set; }

        public PromoRunner(FeudScript script, PromoDef promo, FeudState state, string startNodeId = null)
        {
            Script = script ?? throw new ArgumentNullException(nameof(script));
            Promo = promo ?? throw new ArgumentNullException(nameof(promo));
            State = state ?? throw new ArgumentNullException(nameof(state));
            Current = Node(startNodeId ?? promo.Start);
        }

        public bool IsFinished => Current.Type == PromoNodeType.End;

        private PromoNode Node(string id)
        {
            var n = Promo.Find(id);
            if (n == null) throw new InvalidOperationException("промо " + Promo.Id + ": немає вузла '" + id + "'");
            return n;
        }

        private void GoTo(string id)
        {
            Current = Node(id);
            _rolled = false;
            AwaitingRewards = false;
            RewardsToPick = 0;
        }

        private void Require(PromoNodeType t)
        {
            if (Current.Type != t) throw new InvalidOperationException("вузол " + Current.Id + " має тип " + Current.Type + ", а не " + t);
        }

        // ---------- line ----------

        public void Advance()
        {
            Require(PromoNodeType.Line);
            GoTo(Current.Next);
        }

        // ---------- choice ----------

        /// <summary>Варіанти для показу. Недоступні — locked із причиною; hidden і закриті гілки (умова open) — не показуються.</summary>
        public List<PromoOptionView> AvailableOptions()
        {
            Require(PromoNodeType.Choice);
            var res = new List<PromoOptionView>();
            foreach (var o in Current.Options)
            {
                var failed = State.FirstFailed(o.Requires);
                if (failed == null) { res.Add(new PromoOptionView { Option = o }); continue; }
                if (o.Hidden) continue;
                bool closed = false;
                foreach (var c in o.Requires) if (c.Kind == ConditionKind.Open && !State.Check(c)) closed = true;
                if (closed) continue;
                res.Add(new PromoOptionView { Option = o, Locked = true, Reason = failed });
            }
            return res;
        }

        public void Choose(string optionId)
        {
            Require(PromoNodeType.Choice);
            PromoOption chosen = null;
            foreach (var v in AvailableOptions())
                if (v.Option.Id == optionId) { if (v.Locked) throw new InvalidOperationException("варіант '" + optionId + "' недоступний"); chosen = v.Option; }
            if (chosen == null) throw new InvalidOperationException("немає варіанта '" + optionId + "'");
            State.Apply(chosen.Effects);
            GoTo(chosen.Next);
        }

        // ---------- check ----------

        /// <summary>Модифікатор кидка: атрибут гравця + mod вузла + бонус перка «Свій сценарій».</summary>
        public int CheckModifier()
        {
            Require(PromoNodeType.Check);
            return State.Player.Get(Current.Attr) + Current.Mod + Development.PromoRollBonus(State.Perks);
        }

        /// <summary>Шанси трьох ступенів у тридцять шостих [провал, 7–9, 10+] — показуються до кидка.</summary>
        public int[] PreviewChances() => TwoD6.ChancesOf36(CheckModifier());

        public RollTier RollCheck(IDiceRoller dice)
        {
            Require(PromoNodeType.Check);
            if (_rolled) throw new InvalidOperationException("кидок уже зроблено");
            int total = TwoD6.Roll(dice, "promo:" + Promo.Id + ":" + Current.Id) + CheckModifier();
            var tier = TwoD6.TierOf(total);
            ApplyCheckTier(tier);
            LastTotal = total;
            return tier;
        }

        /// <summary>
        /// Застосувати ступінь кидка напряму (для тестів і перебору досяжності). ≤6 — ефекти fail і перехід на next6 (або next);
        /// 10+/7–9 — чекаємо вибору pick10/pick7 нагород.
        /// </summary>
        public void ApplyCheckTier(RollTier tier)
        {
            Require(PromoNodeType.Check);
            if (_rolled) throw new InvalidOperationException("кидок уже зроблено");
            var n = Current;
            LastTier = tier;
            LastTotal = 0;
            if (tier == RollTier.Miss)
            {
                State.Apply(n.Fail);
                GoTo(n.Next6 ?? n.Next);
                return;
            }
            int pick = Math.Min(tier == RollTier.Strong ? n.Pick10 : n.Pick7, n.Rewards.Count);
            if (pick <= 0) { GoTo(n.Next); return; }
            _rolled = true;
            AwaitingRewards = true;
            RewardsToPick = pick;
        }

        /// <summary>Обрати рівно RewardsToPick різних нагород за id; ефекти застосовуються, перехід на next.</summary>
        public void ChooseRewards(IList<string> rewardIds)
        {
            Require(PromoNodeType.Check);
            if (!AwaitingRewards) throw new InvalidOperationException("нагороди зараз не вибираються");
            if (rewardIds == null || rewardIds.Count != RewardsToPick)
                throw new ArgumentException("потрібно обрати рівно " + RewardsToPick + " нагород");
            var picked = new List<PromoReward>();
            var seen = new HashSet<string>();
            foreach (var id in rewardIds)
            {
                if (!seen.Add(id)) throw new ArgumentException("нагорода '" + id + "' обрана двічі");
                PromoReward found = null;
                foreach (var r in Current.Rewards) if (r.Id == id) found = r;
                if (found == null) throw new ArgumentException("немає нагороди '" + id + "'");
                picked.Add(found);
            }
            foreach (var r in picked) State.Apply(r.Effects);
            GoTo(Current.Next);
        }
    }
}
