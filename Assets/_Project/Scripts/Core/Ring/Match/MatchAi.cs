using System;

namespace Game.Core.Ring.Match
{
    /// <summary>
    /// Суперник: детермінований перелік пріоритетів (ланцюг ініціативи, TRPG_DESIGN §4.5). Працює через той самий публічний API
    /// рушія, що й гравець: GetAvailableActions / Perform / React / Commit — жодного доступу в обхід. Враховує «Сили»: ризикує лише з
    /// запасом, на нулі переводить подих (інакше −1 до кожного кидка). Нескінченного ланцюга не буде — є ліміт матчу.
    /// </summary>
    public static class MatchAi
    {
        /// <summary>Запас «Сил» понад ціну, з яким ШІ йде на ризикований трюк чи кидок.</summary>
        public const int StaminaReserve = 1;

        public static MatchActionId ChooseAction(MatchEngine e)
        {
            var me = e.State.Actor; var opp = e.State.Other;
            var opts = e.GetAvailableActions();
            // «Промінь надії» — лише коли матч його ввімкнув (кар'єра); сильно відстаючий ШІ вживає його одразу.
            if (e.Describe(MatchActionId.HopeSpot).Available) return MatchActionId.HopeSpot;
            if (Ok(opts, MatchActionId.Finisher)) return MatchActionId.Finisher;
            if (Ok(opts, MatchActionId.Pin) && opp.Damage >= MatchRules.AiPinDamage) return MatchActionId.Pin;
            if (Ok(opts, MatchActionId.Submission) && opp.Damage >= MatchRules.AiSubmissionDamage && Spare(me, MatchActionId.Submission))
                return MatchActionId.Submission;
            if (Ok(opts, MatchActionId.GetUp)) return MatchActionId.GetUp;
            // видихся — краще віддати ініціативу, ніж бити з −1
            if (me.Stamina <= 0) return MatchActionId.CatchBreath;
            if (Ok(opts, MatchActionId.Desperate)) return MatchActionId.Desperate;
            if (Ok(opts, MatchActionId.TopRope) && Spare(me, MatchActionId.TopRope) && opp.Posture != Posture.Standing)
                return MatchActionId.TopRope;
            if (Ok(opts, MatchActionId.Throw) && Spare(me, MatchActionId.Throw)) return MatchActionId.Throw;
            if (Ok(opts, MatchActionId.Strike)) return MatchActionId.Strike;
            if (Ok(opts, MatchActionId.Taunt) && e.State.Hype < MatchRules.FinisherHype + 2) return MatchActionId.Taunt;
            var role = me.Side == MatchSide.Player ? e.Setup.PlayerRole : e.Setup.RivalRole;
            if (role == RingPath.Heel && Ok(opts, MatchActionId.Dirty)) return MatchActionId.Dirty;
            if (Ok(opts, MatchActionId.Submission) && Spare(me, MatchActionId.Submission)) return MatchActionId.Submission;
            return MatchActionId.CatchBreath;
        }

        private static bool Spare(Fighter me, MatchActionId id) { return me.Stamina >= MatchRules.Cost(id) + StaminaReserve; }

        /// <summary>Реверс, якщо є жетон і прийом — фінішер або ушкодження вже ≥ 6, інакше продати.</summary>
        public static ReactionChoice ChooseReaction(MatchEngine e)
        {
            var pr = e.PendingReaction;
            var def = e.State.Get(pr.Defender);
            bool can = def.ReversalTokens > 0 && def.Posture != Posture.Stunned;
            if (can && (pr.Action == MatchActionId.Finisher || def.Damage >= MatchRules.AiReverseDamage)) return ReactionChoice.Reverse;
            return ReactionChoice.Sell;
        }

        /// <summary>Докрутка лише щоб урятувати хід від провалу, і не з-під порога фінішера.</summary>
        public static int ChooseBoost(MatchEngine e)
        {
            var pr = e.PendingRoll;
            if (pr.PointsToPartial <= 0) return 0;
            int left = pr.Impulse - pr.PointsToPartial;
            if (pr.Action == MatchActionId.Finisher || left >= MatchRules.FinisherMomentum || pr.Impulse < MatchRules.FinisherMomentum - 1)
                return pr.PointsToPartial;
            return 0;
        }

        /// <summary>Один крок ШІ за фазою рушія (дія, реакція або фіксація кидка).</summary>
        public static void Step(MatchEngine e)
        {
            switch (e.Phase)
            {
                case MatchPhase.AwaitAction:
                    var id = ChooseAction(e);
                    if (id == MatchActionId.CatchBreath) e.EndTurn(); else e.Perform(id);
                    break;
                case MatchPhase.AwaitReaction: e.React(ChooseReaction(e)); break;
                case MatchPhase.AwaitBoost: e.Commit(ChooseBoost(e)); break;
            }
        }

        private static bool Ok(System.Collections.Generic.IReadOnlyList<ActionOption> opts, MatchActionId id)
        {
            for (int i = 0; i < opts.Count; i++) if (opts[i].Id == id) return opts[i].Available;
            return false;
        }
    }

    /// <summary>Допоміжне для тестів і симуляцій: грає матч до кінця.</summary>
    public static class MatchAutoplay
    {
        public const int MaxSteps = 20000;

        /// <summary>true — ШІ веде обох; false — ШІ веде лише суперника, а гравець пасивний (переводить подих і продає).</summary>
        public static MatchResult Run(MatchEngine e, bool aiForBothSides)
        {
            int steps = 0;
            while (!e.IsOver)
            {
                if (++steps > MaxSteps) throw new InvalidOperationException("Матч не закінчився за " + MaxSteps + " кроків");
                MatchSide who = ActorOfPhase(e);
                if (aiForBothSides || who == MatchSide.Rival) { MatchAi.Step(e); continue; }
                switch (e.Phase)
                {
                    case MatchPhase.AwaitAction: e.EndTurn(); break;
                    case MatchPhase.AwaitReaction: e.React(ReactionChoice.Sell); break;
                    case MatchPhase.AwaitBoost: e.Commit(0); break;
                }
            }
            return e.Result;
        }

        private static MatchSide ActorOfPhase(MatchEngine e)
        {
            if (e.Phase == MatchPhase.AwaitReaction) return e.PendingReaction.Defender;
            if (e.Phase == MatchPhase.AwaitBoost) return e.PendingRoll.Actor;
            return e.CurrentActor;
        }
    }
}
