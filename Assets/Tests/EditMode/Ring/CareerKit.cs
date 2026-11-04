using System;
using System.Collections.Generic;
using System.IO;
using Game.Core.Randomness;
using Game.Core.Ring;
using Game.Core.Ring.Career;
using Game.Core.Ring.Match;
using Game.Gameplay.Combat;

namespace Game.Tests.EditMode
{
    /// <summary>Спільне для тестів кар'єри: вміст із реальних JSON, нова сесія, бот-гравець, що веде кар'єру через публічний фасад.</summary>
    internal static class CareerKit
    {
        public static string Read(string file) { return File.ReadAllText(Path.Combine(RingCampaignDriver.Dir(), file)); }

        public static CareerContent Content()
        {
            return CareerContent.Load(Read("feud_hammer.json"), Read("career_moves.json"), Read("career_gimmicks.json"), Read("career_roster.json"));
        }

        public static CharacterDraft Draft(Gimmick g = Gimmick.Monster, CareerRole role = CareerRole.Hero, RingAttr free = RingAttr.Strength, string name = "Тест")
        {
            return new CharacterDraft { Gimmick = g, Role = role, FreePoint = free, RingName = name, CostumeColor = 2 };
        }

        /// <summary>Свіжа сесія на титулі.</summary>
        public static CareerSession NewAtTitle(ulong seed) { return CareerSession.New(Content(), new SeededDiceRoller(seed), seed); }

        /// <summary>Свіжа сесія на етапі «Створення» (титул пройдено).</summary>
        public static CareerSession NewAtCreate(ulong seed, CareerContent content = null)
        {
            var s = CareerSession.New(content ?? Content(), new SeededDiceRoller(seed), seed);
            s.StartNewGame();
            return s;
        }

        /// <summary>Сесія зі створеним героєм: тиждень 1 сезону 1.</summary>
        public static CareerSession NewHero(ulong seed, Gimmick g = Gimmick.Monster, CareerRole role = CareerRole.Hero, RingAttr free = RingAttr.Strength)
        {
            var s = NewAtCreate(seed);
            s.CreateHero(Draft(g, role, free));
            return s;
        }

        /// <summary>Підібрати значення Seed, коли потрібна інша сесія з тим самим зерном кубика і вмістом (для відновлення).</summary>
        public static CareerSession Fresh(ulong seed, CareerContent content)
        {
            return CareerSession.New(content, new SeededDiceRoller(seed), seed);
        }
    }

    /// <summary>Підсумок прогону бота: які етапи, фази й дії траплялись, скільки діб, найгірші значення шкал.</summary>
    internal sealed class CareerRunReport
    {
        public readonly HashSet<CareerStage> Stages = new HashSet<CareerStage>();
        public readonly HashSet<ShowPhase> Phases = new HashSet<ShowPhase>();
        public readonly int[] Actions = new int[5];
        public int Shows, Wins, Losses, Withdrawals, Titles, Fired, Injuries, HopeSpots, TitleMatches, Strained;
        public int MinMoney = int.MaxValue, MinPopularity = 99, MaxPopularity = -1, MaxMoney;
        public int Steps;
    }

    /// <summary>
    /// Бот-кар'єрист: вибір дій тижня крутиться за варіантом (щоб різні прогони бачили різні шляхи), матч веде через прийоми каталогу,
    /// велике шоу ворожнечі проходить екранами кампанії, титульний шанс — за варіантом.
    /// </summary>
    internal static class CareerBot
    {
        private static readonly WeekActionKind[][] Plans =
        {
            new[] { WeekActionKind.Training, WeekActionKind.Promo, WeekActionKind.Backstage },
            new[] { WeekActionKind.SideJob, WeekActionKind.Recovery, WeekActionKind.Training },
            new[] { WeekActionKind.Promo, WeekActionKind.Training, WeekActionKind.SideJob },
            new[] { WeekActionKind.Backstage, WeekActionKind.Backstage, WeekActionKind.Promo },
            new[] { WeekActionKind.Training, WeekActionKind.Training, WeekActionKind.Recovery }
        };

        public static CareerRunReport Play(CareerSession s, int seasons, int variant)
        {
            var rep = new CareerRunReport();
            int guard = 0;
            while (guard++ < 3000)
            {
                rep.Stages.Add(s.Stage);
                if (s.Stage != CareerStage.Title && s.Stage != CareerStage.Create && s.State.Season > seasons) break;
                Step(s, variant, rep);
            }
            if (guard >= 3000) throw new InvalidOperationException("Кар'єра не закінчилась за 3000 кроків, етап " + s.Stage + "/" + s.State.Phase);
            return rep;
        }

        /// <summary>Один крок бота: одна дія етапу (тиждень цілком, одна фаза шоу, один крок кінця сезону).</summary>
        public static void Step(CareerSession s, int variant, CareerRunReport rep)
        {
            rep.Steps++;
            switch (s.Stage)
            {
                case CareerStage.Title: s.StartNewGame(); break;
                case CareerStage.Create:
                    var gim = (Gimmick)(variant % 4);
                    var baseAttr = s.Content.GimmickOf(gim).Attr;
                    int free = variant % 5;
                    while (baseAttr[free] >= WrestlerStats.MaxAttr) free = (free + 1) % 5;
                    s.CreateHero(CareerKit.Draft(gim, variant % 2 == 0 ? CareerRole.Hero : CareerRole.Villain, (RingAttr)free, "Бот" + variant));
                    break;
                case CareerStage.Week: DoWeek(s, variant, rep); break;
                case CareerStage.Show: DoShow(s, variant, rep); break;
                case CareerStage.SeasonEnd: DoSeasonEnd(s, variant, rep); break;
            }
            rep.MinMoney = Math.Min(rep.MinMoney, s.Money);
            rep.MaxMoney = Math.Max(rep.MaxMoney, s.Money);
            rep.MinPopularity = Math.Min(rep.MinPopularity, s.Popularity);
            rep.MaxPopularity = Math.Max(rep.MaxPopularity, s.Popularity);
        }

        private static void DoWeek(CareerSession s, int variant, CareerRunReport rep)
        {
            if (s.State.Strained) rep.Strained++;
            // розвиток: вивчити прийом / купити атрибут / перк, коли вистачає очок
            foreach (var m in s.Content.Moves.All) if (s.LearnMove(m.Id)) break;
            foreach (RingAttr a in new[] { RingAttr.Strength, RingAttr.Agility, RingAttr.Technique, RingAttr.Stamina, RingAttr.Charisma })
                if (s.BuyAttr(a)) break;
            foreach (var p in PerkCatalog.All) if (s.BuyPerk(p.Id)) break;

            var plan = Plans[(variant + s.State.Week) % Plans.Length];
            for (int i = 0; i < plan.Length && s.State.ActionsLeft > 0; i++)
            {
                var kind = plan[i];
                WeekActionResult r = null;
                switch (kind)
                {
                    case WeekActionKind.Training: r = s.Train(s.Money > 40 && variant % 2 == 0); break;
                    case WeekActionKind.Recovery:
                        for (int z = 0; z < 3 && (r == null || !r.Ok); z++) r = s.Recover((BodyZone)z, s.Money > 50);
                        break;
                    case WeekActionKind.Promo: r = s.Promo(); break;
                    case WeekActionKind.Backstage: r = s.Backstage(); break;
                    case WeekActionKind.SideJob: r = s.SideJob(); break;
                }
                if (r.Ok) rep.Actions[(int)kind]++;
                if (r.NeedsChoice) s.ChoosePromoReward((WeekPromoReward)((variant + s.State.Week) % 2));
            }
            s.EndWeek();
        }

        private static void DoShow(CareerSession s, int variant, CareerRunReport rep)
        {
            rep.Phases.Add(s.State.Phase);
            switch (s.State.Phase)
            {
                case ShowPhase.Card:
                    if (s.GetShowCard().CanWithdraw && variant % 2 == 1 && s.State.Week % 2 == 0) { s.WithdrawFromShow(); rep.Withdrawals++; break; }
                    s.StartShow();
                    break;
                case ShowPhase.Match:
                    PlayMatch(s, variant, rep);
                    s.FinishMatch();
                    Record(s, rep);
                    break;
                case ShowPhase.Feud: DriveFeud(s, variant, rep); break;
                case ShowPhase.Result:
                    s.ContinueFromShow();
                    break;
            }
        }

        private static void Record(CareerSession s, CareerRunReport rep)
        {
            var r = s.State.Last;
            if (r == null || !r.Played) return;
            rep.Shows++;
            if (r.Winner == MatchWinner.Player) rep.Wins++; else if (r.Winner == MatchWinner.Rival) rep.Losses++;
            if (r.TitleWon) rep.Titles++;
            if (r.IsTitle) rep.TitleMatches++;
            if (r.InjuredZone >= 0) rep.Injuries++;
        }

        private static void DriveFeud(CareerSession s, int variant, CareerRunReport rep)
        {
            var c = s.Feud;
            switch (c.Stage)
            {
                case RingStage.Overview: c.StartPromo(); break;
                case RingStage.Promo:
                    if (c.CurrentPromoRunner.IsFinished) c.FinishPromo(); else RingCampaignDriver.StepNode(c);
                    break;
                case RingStage.MatchPrep:
                    s.StartFeudMatch();
                    PlayMatch(s, variant, rep);
                    s.FinishMatch();
                    Record(s, rep);
                    break;
                case RingStage.Ending:
                case RingStage.Development:
                    if (s.State.Last != null) s.FinishBigShow(); else throw new InvalidOperationException("Ворожнеча в розвитку без підсумку матчу");
                    break;
                default: throw new InvalidOperationException("Ворожнеча на етапі " + c.Stage);
            }
        }

        private static void DoSeasonEnd(CareerSession s, int variant, CareerRunReport rep)
        {
            var r = s.GetSeasonReport();
            if (r.Fired) rep.Fired++;
            if (s.CanStartTitleMatch)
            {
                if (variant % 3 != 0) s.StartTitleMatch(); else s.SkipTitle();
                return;
            }
            s.StartNextSeason();
        }

        /// <summary>Повний матч гравця: прийоми каталогу за пріоритетом (з ротацією, щоб не повторюватись), промінь надії, фінішер, утримання.</summary>
        public static void PlayMatch(CareerSession s, int variant, CareerRunReport rep)
        {
            var e = s.Engine;
            int steps = 0;
            while (!e.IsOver)
            {
                if (++steps > 6000) throw new InvalidOperationException("Матч не закінчився");
                switch (e.Phase)
                {
                    case MatchPhase.AwaitAction:
                        if (e.CurrentActor == MatchSide.Rival) MatchAi.Step(e); else PlayerAction(s, e, variant, rep);
                        break;
                    case MatchPhase.AwaitReaction:
                        if (e.PendingReaction.Defender == MatchSide.Rival) MatchAi.Step(e);
                        else e.React(variant % 2 == 0 ? ReactionChoice.Sell : MatchAi.ChooseReaction(e));
                        break;
                    case MatchPhase.AwaitBoost:
                        if (e.PendingRoll.Actor == MatchSide.Rival) MatchAi.Step(e); else e.Commit(MatchAi.ChooseBoost(e));
                        break;
                }
            }
        }

        private static void PlayerAction(CareerSession s, MatchEngine e, int variant, CareerRunReport rep)
        {
            if (e.HopeSpotAvailable) { s.PerformAction(MatchActionId.HopeSpot); if (rep != null) rep.HopeSpots++; return; }
            var moves = s.GetMoveOptions();
            var uni = s.GetUniversalOptions();
            // 1) фінішер, утримання, 2) встати, 3) прийоми з ротацією за раундом
            foreach (var m in moves) if (m.Move.Kind == MoveKind.Finisher && m.Option.Available) { s.PerformMove(m.Move.Id); return; }
            foreach (var u in uni) if (u.Id == MatchActionId.Pin && u.Available) { s.PerformAction(u.Id); return; }
            foreach (var u in uni) if (u.Id == MatchActionId.GetUp && u.Available) { s.PerformAction(u.Id); return; }
            var avail = new List<MoveOption>();
            foreach (var m in moves) if (m.Option.Available && m.Move.Action != MatchActionId.Dirty) avail.Add(m);
            if (avail.Count > 0)
            {
                int pick = (e.Round * 7 + e.Ap + variant) % avail.Count;
                s.PerformMove(avail[pick].Move.Id);
                return;
            }
            s.PerformAction(MatchActionId.CatchBreath);
        }
    }
}
