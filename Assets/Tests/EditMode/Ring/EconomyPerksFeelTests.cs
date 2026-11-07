using System;
using System.Collections.Generic;
using Game.Core.Ring;
using Game.Core.Ring.Career;
using Game.Core.Ring.Match;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Перки з дією й економіка з вибором (06.11.2026, пункти 2 і 3 плану лупу; docs/ring/fun_audit_2026-11-06.md §6).
    /// Кар'єру веде гравець-бот, що купує розумно (костюм, тренер ростера, страховка, клініка, виїзне шоу, коли бракує грошей) і
    /// витрачає очки розвитку одразу після кожного матчу; матчі за обидві сторони — однаковий ШІ.
    /// </summary>
    public sealed class EconomyPerksFeelTests
    {
        public sealed class Econ
        {
            public int Weeks, ShortWeeks, Seasons, UnspentSum, UnspentMax, MoneyS1Sum, Careers, Costumes, CoachWeeks, Insured, Clinics, Away;
        }

        private static void AiPlay(MatchEngine e)
        {
            int guard = 0;
            while (!e.IsOver && guard++ < 4000) MatchAi.Step(e);
        }

        /// <summary>Витратити очки: доступний перк (найдешевший), далі найслабший атрибут, далі прийом.</summary>
        public static void SpendPoints(CareerSession s)
        {
            for (int guard = 0; guard < 40; guard++)
            {
                string reason, best = null; int cost = int.MaxValue;
                foreach (var p in PerkCatalog.All)
                    if (s.CanBuyPerk(p.Id, out reason) && p.Cost < cost) { best = p.Id; cost = p.Cost; }
                if (best != null) { s.BuyPerk(best); continue; }
                int attr = -1;
                for (int i = 0; i < 5; i++)
                    if (s.CanBuyAttr((RingAttr)i, out reason) && (attr < 0 || s.Player.Get((RingAttr)i) < s.Player.Get((RingAttr)attr))) attr = i;
                if (attr >= 0) { s.BuyAttr((RingAttr)attr); continue; }
                string move = null;
                foreach (var m in s.Content.Moves.All) if (s.CanLearnMove(m.Id, out reason)) { move = m.Id; break; }
                if (move != null) { s.LearnMove(move); continue; }
                break;
            }
        }

        /// <summary>Покупки тижня (не займають дій): що бот вважає вигідним при поточних грошах.</summary>
        private static void Shop(CareerSession s, Econ e)
        {
            int buffer = CareerRules.LivingCost;
            if (s.CanBuyCostume && s.Money >= CareerRules.CostumeCost + buffer) { s.BuyCostume(); e.Costumes++; }
            if (s.CanBuyInsurance && s.Money >= CareerRules.InsuranceCost + buffer) { s.BuyInsurance(); e.Insured++; }
            for (int z = 0; z < 3; z++)
                if (s.State.Injury[z] > 0 && s.CanClinic((BodyZone)z) && s.Money >= CareerRules.ClinicCost + buffer) { s.Clinic((BodyZone)z); e.Clinics++; }
            if (s.State.Coach < 0 && s.Money >= CareerRules.CoachWeeklyFee * 2 + buffer)
            {
                // Тренер на найслабший з трьох атрибутів ростера.
                int best = 0;
                for (int i = 1; i < CareerRules.Coaches.Length; i++)
                    if (s.Player.Get(CareerRules.Coaches[i]) < s.Player.Get(CareerRules.Coaches[best])) best = i;
                s.HireCoach(best);
            }
            // Виїзд — коли бракує грошей і він окупається на цьому місці картки (у середині й вище).
            if (s.CanGoAway && s.State.Position >= CardPosition.Midcard && s.Money < CareerRules.LivingCost + CareerRules.CoachWeeklyFee + CareerRules.AwayTravelCost)
            { s.GoAway(); e.Away++; }
        }

        public static Econ Run(int careers, int seasons)
        {
            var e = new Econ { Careers = careers };
            for (int i = 0; i < careers; i++)
            {
                var s = CareerKit.NewHero((ulong)(7000 + i));
                var rep = new CareerRunReport();
                int guard = 0, lastWeekSeen = -1;
                while (s.State.Season <= seasons && guard++ < 4000)
                {
                    if (s.Stage == CareerStage.SeasonEnd)
                    {
                        SpendPoints(s);
                        e.Seasons++;
                        e.UnspentSum += s.DevPoints; e.UnspentMax = Math.Max(e.UnspentMax, s.DevPoints);
                        // Вільні гроші: мінус обов'язкове наступного тижня (проживання й тренер, якщо найнятий).
                        if (s.State.Season == 1) e.MoneyS1Sum += Math.Max(0, s.Money - CareerRules.LivingCost - (s.State.Coach >= 0 ? CareerRules.CoachWeeklyFee : 0));
                        if (s.CanStartTitleMatch) s.SkipTitle(); else s.StartNextSeason();
                        continue;
                    }
                    if (s.Stage == CareerStage.Week)
                    {
                        int key = s.State.Season * 100 + s.State.Week;
                        if (key != lastWeekSeen)
                        {
                            lastWeekSeen = key;
                            e.Weeks++;
                            // Нестача: не вистачає на тиждень життя разом із тренером — гроші програють часу чи травмі.
                            if (s.State.Strained || s.Money < CareerRules.LivingCost + CareerRules.CoachWeeklyFee) e.ShortWeeks++;
                            Shop(s, e);
                            if (s.State.Coach >= 0) e.CoachWeeks++;
                            SpendPoints(s);
                        }
                        CareerBot.Step(s, i % 8, rep);
                        continue;
                    }
                    switch (s.State.Phase)
                    {
                        case ShowPhase.Card: s.StartShow(); break;
                        case ShowPhase.Match: AiPlay(s.Engine); s.FinishMatch(); SpendPoints(s); break;
                        case ShowPhase.Feud:
                            if (s.Feud.Stage == RingStage.MatchPrep) { AiPlay(s.StartFeudMatch()); s.FinishMatch(); SpendPoints(s); }
                            else CareerBot.Step(s, i % 8, rep);
                            break;
                        case ShowPhase.Result: SpendPoints(s); s.ContinueFromShow(); break;
                    }
                }
            }
            return e;
        }

        /// <summary>
        /// А/Б перка з дією в його ситуації: ті самі зерна, тиждень 1; ситуацію визначає матч БЕЗ перка (напр. «двічі вирвався»),
        /// і в цих матчах порівнюються перемоги з перком і без. Повертає (перемог з перком, без, матчів у ситуації).
        /// </summary>
        public static int[] PerkAb(string perk, CareerRole role, int matches, Func<MatchEngine, bool> situation)
        {
            int with = 0, without = 0, n = 0;
            for (int i = 0; i < matches; i++)
            {
                var b = Play(i, role, null);
                if (!situation(b)) continue;
                n++;
                if (b.Result.Winner == MatchWinner.Player) without++;
                if (Play(i, role, perk).Result.Winner == MatchWinner.Player) with++;
            }
            return new[] { with, without, n };
        }

        private static MatchEngine Play(int i, CareerRole role, string perk)
        {
            var s = CareerKit.NewHero((ulong)(11000 + i), Gimmick.Monster, role);
            if (perk != null) s.Feud.State.Perks.Add(perk);
            s.EndWeek();
            var e = s.StartShow();
            AiPlay(e);
            return e;
        }

        [Test]
        public void Economy_HasChoice_AndPointsGetSpent()
        {
            var e = Run(300, 3);
            string rep = string.Format(
                "тижнів {0}, з нестачею {1:P0}; вільних грошей на кінець сезону 1 у середньому {2}; невитрачених очок на кінець сезону " +
                "у середньому {3:F1}, найбільше {4}; костюмів {5}, тижнів із тренером {6:P0}, страховок {7}, клінік {8}, виїзних шоу {9}",
                e.Weeks, (double)e.ShortWeeks / e.Weeks, e.MoneyS1Sum / Math.Max(1, e.Careers), (double)e.UnspentSum / Math.Max(1, e.Seasons),
                e.UnspentMax, e.Costumes, (double)e.CoachWeeks / e.Weeks, e.Insured, e.Clinics, e.Away);
            TestContext.WriteLine(rep);
            Console.WriteLine("[econ] " + rep);
            double shortShare = (double)e.ShortWeeks / e.Weeks;
            Assert.That(shortShare, Is.InRange(0.25, 0.40), "тижнів з нестачею грошей — 25–40 %");
            Assert.LessOrEqual(e.MoneyS1Sum / e.Careers, 110, "вільних грошей на кінець сезону 1 — не більше ~100");
            Assert.LessOrEqual(e.UnspentSum / Math.Max(1, e.Seasons), 5, "невитрачених очок на кінець сезону — не більше 5");
        }

        [Test]
        public void ActionPerks_ChangeTheirSituation()
        {
            var sb = new System.Text.StringBuilder();
            var cases = new[]
            {
                Tuple.Create(PerkCatalog.Comeback, CareerRole.Hero, (Func<MatchEngine, bool>)(e => e.State.Player.KickoutsDone >= MatchRules.ComebackKickouts)),
                Tuple.Create(PerkCatalog.HeroOfCrowd, CareerRole.Hero, (Func<MatchEngine, bool>)(e => e.State.PlayerSells > 0 && e.Result.FinalHype >= MatchRules.HeroOfCrowdHype)),
                Tuple.Create(PerkCatalog.BribedRef, CareerRole.Villain, (Func<MatchEngine, bool>)(e => e.Result.Winner == MatchWinner.Rival && e.Result.Kind == MatchEndKind.Pin)),
                Tuple.Create(PerkCatalog.DirtyGame, CareerRole.Villain, (Func<MatchEngine, bool>)(e => e.State.Player.Warnings > 0)),
                Tuple.Create(PerkCatalog.Improvise, CareerRole.Hero, (Func<MatchEngine, bool>)(e =>
                {
                    foreach (var ev in e.State.Log)
                        if (ev.Kind == MatchEventKind.Action && ev.Actor == MatchSide.Player && ev.HasRoll && ev.Tier == RollTier.Miss
                            && ev.Action != MatchActionId.Finisher) return true;
                    return false;
                })),
            };
            var gains = new Dictionary<string, double>();
            foreach (var c in cases)
            {
                var r = PerkAb(c.Item1, c.Item2, 1000, c.Item3);
                double gain = r[2] == 0 ? 0 : 100.0 * (r[0] - r[1]) / r[2];
                gains[c.Item1] = gain;
                sb.AppendFormat("{0}: у ситуації {1} матчів, з перком {2}, без {3} (Δ {4:+0.0;-0.0} п.п.); ", c.Item1, r[2], r[0], r[1], gain);
            }
            TestContext.WriteLine(sb.ToString());
            Console.WriteLine("[perks] " + sb);
            // Кожен шлях має перк з дією, що виграє у своїй ситуації ≥ 5 п.п. Замір 06.11.2026: «Герой залу» +5,9,
            // «Підкуплений суддя» +55,2, «Імпровізація» +6,0 («Камбек» +0, «Брудна гра» +2,3 — ситуаційні, слабші).
            Assert.GreaterOrEqual(gains[PerkCatalog.HeroOfCrowd], 5.0, "Герой: перк з дією");
            Assert.GreaterOrEqual(gains[PerkCatalog.BribedRef], 5.0, "Лиходій: перк з дією");
            Assert.GreaterOrEqual(gains[PerkCatalog.Improvise], 5.0, "Бунтар: перк з дією");
        }
    }
}
