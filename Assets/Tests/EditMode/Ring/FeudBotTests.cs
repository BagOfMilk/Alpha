using System;
using System.Collections.Generic;
using System.Text;
using Game.Core.Ring;
using Game.Core.Ring.Career;
using Game.Core.Ring.Match;
using Game.Gameplay.Combat;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Бот кінцівок огляду QA (п. 8 П5, <c>-ring-shot-ending</c>): таблиця «ціль матчу → кінцівка» узгоджена з
    /// <see cref="FeudEndings.Resolve"/>, і бот доводить кожну з шести кінцівок на справжньому сценарії — у самій ворожнечі й у
    /// повній кар'єрі (тиждень, шоу, сцени закулісся, велике шоу з Молотом), справжнім рушієм матчу.
    /// </summary>
    public class FeudBotTests
    {
        [Test]
        public void Ending_ParsesSnakeKebabPascalAndNumber()
        {
            foreach (var e in FeudEndings.All)
            {
                FeudEnding p;
                Assert.IsTrue(FeudBot.TryParseEnding(FeudBot.EndingName(e), out p)); Assert.AreEqual(e, p);
                Assert.IsTrue(FeudBot.TryParseEnding(FeudBot.EndingName(e).Replace('_', '-'), out p)); Assert.AreEqual(e, p);
                Assert.IsTrue(FeudBot.TryParseEnding(e.ToString(), out p)); Assert.AreEqual(e, p);
                Assert.IsTrue(FeudBot.TryParseEnding(((int)e).ToString(), out p)); Assert.AreEqual(e, p);
            }
            FeudEnding x;
            Assert.IsFalse(FeudBot.TryParseEnding("", out x));
            Assert.IsFalse(FeudBot.TryParseEnding("happy_end", out x));
            Assert.IsFalse(FeudBot.TryParseEnding("6", out x));
        }

        /// <summary>
        /// Для кожної кінцівки й кожного плану з <see cref="FeudBot.PlanOrder"/> вирішального матчу результат, якого бот домагається
        /// (<see cref="FeudBot.GoalFor"/>), дає саме цю кінцівку — за тією самою оцінкою плану й таблицею §5.3, що й гра.
        /// </summary>
        [Test]
        public void DecisiveGoal_ForEveryPreferredPlan_ResolvesToTheTarget()
        {
            var def = new MatchDef { Id = "m3", Act = 3, Decisive = true };
            foreach (var target in FeudEndings.All)
                foreach (var plan in FeudBot.PlanOrder(target, true))
                {
                    Assert.IsTrue(FeudBot.PlanAllows(target, plan), target + ": план " + plan + " у порядку, але не веде до кінцівки");
                    var g = FeudBot.GoalFor(target, true, plan);
                    var s = new FeudState();
                    if (target == FeudEnding.HatedHeel) s.AddPath(RingPath.Heel, 1);
                    s.Plan = plan;
                    var r = ResultOf(g);
                    var outcome = s.CompleteMatch(r, def);
                    Assert.AreEqual(target, FeudEndings.Resolve(s, r, outcome), target + " / план " + plan + " / ціль " + g);
                }
        }

        /// <summary>Результат, якого домагається ціль: перемога/поразка утриманням, брудні й продажі за стилем, ажіотаж за ціллю.</summary>
        private static MatchResult ResultOf(MatchGoal g)
        {
            return new MatchResult
            {
                Winner = g.Aim == MatchAim.Lose ? MatchWinner.Rival : MatchWinner.Player,
                Kind = MatchEndKind.Pin,
                FinalHype = g.Hype == HypeAim.High ? FeudBot.HighHypeStop : g.Hype == HypeAim.Low ? 3 : 5,
                PlayerDirtyMoves = g.Style == MatchStyle.Dirty ? FeudBot.DirtyForHeel : 0,
                PlayerSells = g.Sell ? FeudBot.SellsForFace : 0,
                PlayerKickoutsAtTwo = 1
            };
        }

        private const int Seeds = 12;

        /// <summary>
        /// Повна кар'єра: якщо на <see cref="Seeds"/> зернах кінцівки немає, бот пробує далі — до стількох зерен. Охоронець — про
        /// досяжність, а не про частоту: «Улюбленця публіки» бот кінцівок дістає рідко (вирішальний матч проти Молота, що виріс із гравцем,
        /// стилем «чисто й продавай»: 1–2 з 24 зерен; рев'ю ПМ 12.11.2026 — піки сили зсунули траєкторії, і на 12 зернах влучань стало 0).
        /// Недосяжна кінцівка й далі червона: 0 із <see cref="MaxCareerSeeds"/>.
        /// </summary>
        private const int MaxCareerSeeds = 36;

        /// <summary>Сама ворожнеча (без кар'єри), справжній рушій: кожна ціль хоч раз досягнута на 12 зернах.</summary>
        [Test]
        public void Campaign_EveryEnding_ReachedByTheBot()
        {
            var sb = new StringBuilder();
            var missed = new List<FeudEnding>();
            foreach (var target in FeudEndings.All)
            {
                int hit = 0;
                var got = new Dictionary<FeudEnding, int>();
                for (int v = 0; v < Seeds; v++)
                {
                    var c = RingCampaignDriver.NewCampaign((ulong)(500 + v));
                    var e = FeudBotDriver.PlayCampaign(c, target);
                    got[e] = got.TryGetValue(e, out var n) ? n + 1 : 1;
                    if (e == target) hit++;
                }
                sb.AppendLine(target + ": " + hit + "/" + Seeds + " — " + Describe(got));
                if (hit == 0) missed.Add(target);
            }
            TestContext.Out.WriteLine(sb.ToString());
            Assert.IsEmpty(missed, "бот не дійшов до: " + string.Join(", ", missed) + "\n" + sb);
        }

        /// <summary>
        /// Повна кар'єра (як огляд у Ring.exe): тижні, шоу, сцени закулісся й три великі шоу з Молотом — бот веде до кожної кінцівки;
        /// роль героя на створенні чергується (герой/лиходій), тож кінцівка не залежить від вибору на створенні.
        /// </summary>
        [Test]
        public void Career_EveryEnding_ReachedByTheBot()
        {
            var sb = new StringBuilder();
            var missed = new List<FeudEnding>();
            foreach (var target in FeudEndings.All)
            {
                int hit = 0, v = 0;
                var got = new Dictionary<string, int>();
                for (; v < Seeds || (hit == 0 && v < MaxCareerSeeds); v++)
                {
                    var s = CareerKit.NewAtTitle((ulong)(700 + v));
                    var e = FeudBotDriver.PlayCareer(s, v, target);
                    string k = e.HasValue ? e.Value.ToString() : "немає";
                    got[k] = got.TryGetValue(k, out var n) ? n + 1 : 1;
                    if (e == target) hit++;
                }
                sb.AppendLine(target + ": " + hit + "/" + v + " — " + Describe(got));
                if (hit == 0) missed.Add(target);
            }
            TestContext.Out.WriteLine(sb.ToString());
            Assert.IsEmpty(missed, "бот не дійшов до: " + string.Join(", ", missed) + "\n" + sb);
        }

        /// <summary>
        /// П5 (власник 11.11.2026, варіант А): Молот, сюжет Ліни й шість кінцівок їдуть разом із гравцем у вищу федерацію. Наприкінці
        /// першого сезону гравця переводять на рівень вище (ключі виставлено, чемпіон — через прощальний матч); розв'язка — уже там.
        /// </summary>
        [Test]
        public void Career_EveryEnding_ReachedByTheBot_AfterAMoveUp()
        {
            var sb = new StringBuilder();
            var missed = new List<FeudEnding>();
            foreach (var target in FeudEndings.All)
            {
                int hit = 0, v = 0;
                for (; v < Seeds || (hit == 0 && v < MaxCareerSeeds); v++)
                {
                    var s = CareerKit.NewAtTitle((ulong)(700 + v));
                    var e = FeudBotDriver.PlayCareer(s, v, target, atSeasonEnd: MoveUpAfterSeasonOne);
                    Assert.GreaterOrEqual(s.FederationTier, 2, "перехід відбувся");
                    if (e == target) hit++;
                }
                sb.AppendLine(target + ": " + hit + "/" + v);
                if (hit == 0) missed.Add(target);
            }
            TestContext.Out.WriteLine(sb.ToString());
            Assert.IsEmpty(missed, "після переходу бот не дійшов до: " + string.Join(", ", missed) + "\n" + sb);
        }

        private static void MoveUpAfterSeasonOne(CareerSession s)
        {
            if (s.State.Season != 1 || s.FederationTier != 1 || !s.SeasonSummaryFinal || s.State.OfferDecision != 0 || s.State.Farewell) return;
            // ключі виставлено — і звільнення (воно з тієї самої популярності, порахованої до цього): бот кінцівок лиходія інколи
            // закінчує S1 з популярністю 0, а тест — про ворожнечу після переходу, не про звільнення
            s.State.Report.Fired = false;
            s.Feud.State.Audience = s.PopularityCeiling;
            s.Feud.State.Trust = Math.Max(s.Trust, CareerRules.OfferTrust);
            Assert.IsTrue(s.OfferAvailable);
            s.AcceptOffer();
        }

        private static string Describe<T>(Dictionary<T, int> d)
        {
            var parts = new List<string>();
            foreach (var kv in d) parts.Add(kv.Key + " " + kv.Value);
            parts.Sort(StringComparer.Ordinal);
            return string.Join(", ", parts);
        }
    }

    /// <summary>Водій бота кінцівок для тестів: те саме, що хост огляду, — через публічні фасади кампанії й кар'єри.</summary>
    internal static class FeudBotDriver
    {
        public static void PlayMatch(MatchEngine e, MatchGoal g)
        {
            int steps = 0;
            while (!e.IsOver)
            {
                if (++steps > MatchAutoplay.MaxSteps) throw new InvalidOperationException("матч не закінчився, ціль " + g);
                FeudBot.Step(e, g);
            }
        }

        public static FeudEnding PlayCampaign(RingCampaign c, FeudEnding target)
        {
            for (int guard = 0; guard < 5000; guard++)
            {
                switch (c.Stage)
                {
                    case RingStage.Overview: c.StartPromo(); break;
                    case RingStage.Promo:
                        if (c.CurrentPromoRunner.IsFinished) c.FinishPromo();
                        else FeudBot.StepPromo(c.CurrentPromoRunner, target, FeudBot.NextMatch(c), c.Dice);
                        break;
                    case RingStage.MatchPrep:
                        bool decisive = c.CurrentMatchDef.Decisive;
                        var e = c.StartMatch();
                        PlayMatch(e, FeudBot.GoalFor(target, decisive, c.State.Plan));
                        c.FinishMatch();
                        break;
                    case RingStage.Development: c.ContinueAfterDevelopment(); break;
                    case RingStage.Ending: return c.Ending;
                    default: throw new InvalidOperationException("етап " + c.Stage);
                }
            }
            throw new InvalidOperationException("ворожнеча не дійшла до кінцівки");
        }

        /// <summary>Кар'єра до розв'язки ворожнечі (або 5 сезонів); кінцівка — null, якщо розв'язки не було.</summary>
        /// <param name="goal">Ціль матчу замість <see cref="FeudBot.GoalFor"/> (тести правил кар'єри); null — ціль кінцівки.</param>
        /// <param name="onResult">Після кожного зіграного матчу — його підсумок.</param>
        /// <param name="untilSeason">Грати до кінця цього сезону навіть після розв'язки ворожнечі; 0 — до розв'язки.</param>
        public static FeudEnding? PlayCareer(CareerSession s, int variant, FeudEnding target, Func<CareerSession, MatchGoal?> goal = null,
                                             Action<ShowResult> onResult = null, int untilSeason = 0, Action<CareerSession> atSeasonEnd = null)
        {
            var rep = new CareerRunReport();
            for (int guard = 0; guard < 8000; guard++)
            {
                bool started = s.Stage != CareerStage.Title && s.Stage != CareerStage.Create;
                if (untilSeason > 0 ? started && s.State.Season > untilSeason : s.State.FeudResolved || (started && s.State.Season > 5)) break;
                switch (s.Stage)
                {
                    case CareerStage.Week: DoWeek(s, variant, target); break;
                    case CareerStage.Show: DoShow(s, target, goal, onResult); break;
                    default:
                        if (s.Stage == CareerStage.SeasonEnd && atSeasonEnd != null)
                        {
                            atSeasonEnd(s);
                            if (s.Stage != CareerStage.SeasonEnd) break;   // прощальний матч чемпіона — далі як шоу
                        }
                        CareerBot.Step(s, variant, rep);
                        break;
                }
            }
            return s.Feud.HasEnding ? s.Feud.Ending : (FeudEnding?)null;
        }

        private static readonly WeekActionKind[] WeekPlan = { WeekActionKind.Training, WeekActionKind.Backstage, WeekActionKind.Promo };

        private static void DoWeek(CareerSession s, int variant, FeudEnding target)
        {
            foreach (var m in s.Content.Moves.All) if (s.LearnMove(m.Id)) break;
            foreach (RingAttr a in new[] { RingAttr.Strength, RingAttr.Agility, RingAttr.Technique, RingAttr.Stamina, RingAttr.Charisma })
                if (s.BuyAttr(a)) break;
            foreach (var p in PerkCatalog.All) if (s.BuyPerk(p.Id)) break;
            for (int i = 0; i < WeekPlan.Length && s.State.ActionsLeft > 0; i++)
            {
                WeekActionResult r = null;
                switch (WeekPlan[(i + variant) % WeekPlan.Length])
                {
                    case WeekActionKind.Training: r = s.Train(false); break;
                    case WeekActionKind.Promo: r = s.Promo(); break;
                    case WeekActionKind.Backstage:
                        r = s.Backstage();
                        if (r.NeedsScene) PlayScene(s, target);
                        break;
                }
                if (r != null && r.NeedsChoice) s.ChoosePromoReward((WeekPromoReward)(variant % 2));
            }
            s.EndWeek();
        }

        /// <summary>Сцена закулісся ботом кінцівок: варіанти — під шлях кінцівки, кидки — кубиком кар'єри.</summary>
        private static void PlayScene(CareerSession s, FeudEnding target)
        {
            var r = s.CurrentBackstage;
            for (int guard = 0; !r.IsFinished; guard++)
            {
                if (guard > 200) throw new InvalidOperationException("сцена не закінчується");
                if (r.Current.Type == PromoNodeType.Check && !r.AwaitingRewards) s.BackstageRollCheck();
                else FeudBot.StepPromo(r, target, null, s.Dice);
            }
            s.FinishBackstage();
        }

        private static void DoShow(CareerSession s, FeudEnding target, Func<CareerSession, MatchGoal?> goal, Action<ShowResult> onResult)
        {
            switch (s.State.Phase)
            {
                case ShowPhase.Card: s.StartShow(); break;
                case ShowPhase.Match:
                    PlayCareerMatch(s, GoalOf(s, target, goal));
                    var wr = s.FinishMatch();
                    if (onResult != null) onResult(wr);
                    break;
                case ShowPhase.Feud:
                    var c = s.Feud;
                    switch (c.Stage)
                    {
                        case RingStage.Overview: c.StartPromo(); break;
                        case RingStage.Promo:
                            if (c.CurrentPromoRunner.IsFinished) c.FinishPromo();
                            else FeudBot.StepPromo(c.CurrentPromoRunner, target, FeudBot.NextMatch(c), c.Dice);
                            break;
                        case RingStage.MatchPrep:
                            s.StartFeudMatch();
                            PlayCareerMatch(s, GoalOf(s, target, goal));
                            var fr = s.FinishMatch();
                            if (onResult != null) onResult(fr);
                            break;
                        default: s.FinishBigShow(); break;
                    }
                    break;
                case ShowPhase.Result: s.ContinueFromShow(); break;
            }
        }

        private static MatchGoal GoalOf(CareerSession s, FeudEnding target, Func<CareerSession, MatchGoal?> goal)
        {
            var g = goal == null ? null : goal(s);
            return g ?? FeudBot.GoalFor(target, s.CurrentMatchDecisive, s.CurrentMatchPlan);
        }

        /// <summary>Матч кар'єри як в огляді: дію обирає бот кінцівок, виконує вивчений прийом із цією дією (без повтору, якщо є).</summary>
        private static void PlayCareerMatch(CareerSession s, MatchGoal g)
        {
            var e = s.Engine;
            int steps = 0;
            while (!e.IsOver)
            {
                if (++steps > MatchAutoplay.MaxSteps) throw new InvalidOperationException("матч не закінчився, ціль " + g);
                if (e.Phase == MatchPhase.AwaitAction && e.CurrentActor == MatchSide.Player)
                {
                    var id = FeudBot.ChooseAction(e, g);
                    MoveOption pick = null;
                    foreach (var o in s.GetMoveOptions())
                        if (o.Move.Action == id && o.Option.Available && (pick == null || (pick.RepeatWarning && !o.RepeatWarning))) pick = o;
                    if (pick != null) s.PerformMove(pick.Move.Id);
                    else if (id == MatchActionId.Finisher) e.Perform(id, e.Setup.FinisherOf(MatchSide.Player));
                    else s.PerformAction(id);
                    continue;
                }
                FeudBot.Step(e, g);
            }
        }
    }
}
