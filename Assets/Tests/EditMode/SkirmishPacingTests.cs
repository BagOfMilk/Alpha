using System;
using System.Collections.Generic;
using System.Linq;
using Game.Core.Base;
using Game.Core.Combat;
using Game.Core.Dungeons;
using Game.Core.Expeditions;
using Game.Core.Loop;
using Game.Core.Randomness;
using Game.Core.Session;
using Game.Core.Session.Bots;
using Game.Core.Session.Views;
using Game.Gameplay.Combat;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Темп МАЛИХ боїв (Поправка №11, дослівно власника: «Бої більше 5 раундів
    /// тільки при маштабних івентах або битвах» + «обрізання на 5-му раунді -
    /// обрізання не треба, це дизайнерсьеке рішення, щоб данжі були побудовані
    /// так що саме бої в ниж були короткими але тяжкими»). Це ПРАВИЛО ДЛЯ
    /// КОНТЕНТУ, не механічний ліміт: <see cref="Balance.CombatBalance.RoundCap"/>
    /// лишається технічним запобіжником (40 раундів) — тут ми лише
    /// перевіряємо, що АВТОРСЬКИЙ КОНТЕНТ малих боїв (бойові кімнати данжів +
    /// тренувальний бій) сам розв'язується за жменю раундів, а не мовчки
    /// вповзає в запобіжник.
    ///
    /// Великі бої (вузол 1 <c>PassVanguardBloody</c>, фінал <c>FinaleAssault</c>)
    /// цим правилом НЕ покриті і тут не перевіряються — на них дозволено
    /// довше (Поправка №11).
    ///
    /// Кімнати беруться з <see cref="DefaultDungeon.KnownSiteIds"/>/<see cref="DefaultDungeon.Rooms"/>
    /// (не зашиті рядками), тож нова бойова кімната будь-якого майбутнього
    /// сайту автоматично потрапляє під цей самий охоронець.
    /// </summary>
    public class SkirmishPacingTests
    {
        // ---- Плейсхолдери цілей (Поправка №11, підбирається харнесом) ----

        /// <summary>«Коротко» — розв'язка за не більш ніж стільки раундів.</summary>
        private const int ShortRoundsCap = 5;

        /// <summary>Скільки Percent-сідів прогонити на кожен (кімната, політика) — детермінований SeededDiceRoller, сіди 1..20.</summary>
        private const int SeedCount = 20;

        /// <summary>Грамотна гра, ThresholdRule (один детермінований прогін): має бути чистою перемогою.</summary>
        private const string GraftedThresholdExpectedOutcome = "Victory";

        /// <summary>Грамотна гра, PercentRule: перемога не менш ніж у цій частці сідів.</summary>
        private const double GraftedPercentVictoryRateMin = 0.85;

        /// <summary>
        /// Грамотна гра: медіана втрати HP загону (частка від сумарного MaxHp) — «тяжко».
        /// 0.30, а не 0.35: у загоні з трьох бійців із близьким HP втрата йде
        /// сходинками по одному бійцю (~32% на кожного, хто впав). Поріг 0.35
        /// вимагав би «двоє впали в більшості боїв» — це вже розгром, а не
        /// «тяжко». 0.30 = «у типовому бою один боєць падає».
        /// </summary>
        private const double GraftedMedianHpLossMin = 0.30;

        /// <summary>Грамотна гра, PercentRule: хоч один боєць падає (Downed/Dead) не менш ніж у цій частці сідів.</summary>
        private const double GraftedFellRateMin = 0.20;

        /// <summary>Наївна гра: поразка АБО падіння бійця не менш ніж у цій частці прогонів (Threshold-прогін + всі Percent-сіди разом).</summary>
        private const double NaiveFailureOrFallRateMin = 0.50;

        /// <summary>Реалістична партія делве (§3.0 FIRST_HOUR): протагоніст + двоє напарників у полі — той самий склад, що реально йде і в "Покинутий табір авангарду" (доба 4), і в вільну гру "Старого скиту" (третього кандидата в цій зрізовій збірці просто нема — обидва боти й гравець ведуть туди тих самих трьох).</summary>
        private static readonly IReadOnlyList<string> Party = FirstHourWorld.PartyIds;

        // =====================================================================
        // Кейси: усі бойові кімнати всіх відомих данжів (генерується з контенту)
        // =====================================================================

        private static IEnumerable<TestCaseData> CombatRoomCases()
        {
            foreach (var siteId in DefaultDungeon.KnownSiteIds)
            {
                var rooms = DefaultDungeon.Rooms(siteId);
                if (rooms == null) continue;
                foreach (var room in rooms)
                    if (room.Kind == DungeonRoomKind.Combat)
                        yield return new TestCaseData(siteId, room.Id).SetName("{m}(" + siteId + "/" + room.Id + ")");
            }
        }

        [Test, TestCaseSource(nameof(CombatRoomCases))]
        public void CompetentPlay_ClearsRoomShort_ButPaysARealPrice(string siteId, string roomId)
        {
            // ThresholdRule — один детермінований прогін: сам факт "коротко і
            // виграно" не залежить від сіда (кубик тут не кидається взагалі).
            var thresholdRun = RunRoomBattle(siteId, roomId, HitRuleKind.Threshold, seed: 1, naive: false);
            Assert.LessOrEqual(thresholdRun.Rounds, ShortRoundsCap,
                $"{siteId}/{roomId}: грамотна гра під ThresholdRule мала розв'язатись за <= {ShortRoundsCap} раундів, вийшло {thresholdRun.Rounds}");
            Assert.AreEqual(GraftedThresholdExpectedOutcome, thresholdRun.Outcome,
                $"{siteId}/{roomId}: грамотна гра під ThresholdRule мала завершитись перемогою");

            // PercentRule — 20 сідів: перевіряємо і "коротко" (КОЖЕН прогін), і
            // "тяжко, але виграшно" (агреговано за вибіркою).
            var rounds = new List<int>();
            var hpLossFractions = new List<double>();
            int victories = 0, fellCount = 0;

            for (int seed = 1; seed <= SeedCount; seed++)
            {
                var run = RunRoomBattle(siteId, roomId, HitRuleKind.Percent, seed, naive: false);
                Assert.LessOrEqual(run.Rounds, ShortRoundsCap,
                    $"{siteId}/{roomId} seed {seed}: грамотна гра під PercentRule мала розв'язатись за <= {ShortRoundsCap} раундів, вийшло {run.Rounds}");

                rounds.Add(run.Rounds);
                hpLossFractions.Add(run.PartyHpLossFraction);
                if (run.Outcome == "Victory") victories++;
                if (run.AnyPartyMemberFell) fellCount++;
            }

            double victoryRate = victories / (double)SeedCount;
            double fellRate = fellCount / (double)SeedCount;
            double medianHpLoss = Median(hpLossFractions);

            Assert.GreaterOrEqual(victoryRate, GraftedPercentVictoryRateMin,
                $"{siteId}/{roomId}: грамотна гра мала виграти не менш ніж {GraftedPercentVictoryRateMin:P0} Percent-сідів, вийшло {victoryRate:P0} ({victories}/{SeedCount})");
            Assert.GreaterOrEqual(medianHpLoss, GraftedMedianHpLossMin,
                $"{siteId}/{roomId}: медіана втрати HP загону мала бути не менш ніж {GraftedMedianHpLossMin:P0} (тяжко), вийшло {medianHpLoss:P0}");
            Assert.GreaterOrEqual(fellRate, GraftedFellRateMin,
                $"{siteId}/{roomId}: хоч один боєць мав падати (Downed/Dead) не менш ніж у {GraftedFellRateMin:P0} Percent-прогонів, вийшло {fellRate:P0} ({fellCount}/{SeedCount})");
        }

        [Test, TestCaseSource(nameof(CombatRoomCases))]
        public void NaivePlay_StillResolvesShort_ButOftenFails(string siteId, string roomId)
        {
            var rounds = new List<int>();
            int failuresOrFalls = 0, total = 0;

            void Record(RunMetrics run)
            {
                rounds.Add(run.Rounds);
                total++;
                if (run.Outcome != "Victory" || run.AnyPartyMemberFell) failuresOrFalls++;
            }

            // ThresholdRule — один прогін тим самим наївним водієм.
            Record(RunRoomBattle(siteId, roomId, HitRuleKind.Threshold, seed: 1, naive: true));

            // PercentRule — 20 сідів.
            for (int seed = 1; seed <= SeedCount; seed++)
                Record(RunRoomBattle(siteId, roomId, HitRuleKind.Percent, seed, naive: true));

            double medianRounds = Median(rounds.Select(r => (double)r).ToList());
            double failRate = failuresOrFalls / (double)total;

            Assert.LessOrEqual(medianRounds, ShortRoundsCap,
                $"{siteId}/{roomId}: наївна гра мала мати медіану раундів <= {ShortRoundsCap}, вийшло {medianRounds}");
            Assert.GreaterOrEqual(failRate, NaiveFailureOrFallRateMin,
                $"{siteId}/{roomId}: наївна гра мала програвати АБО втрачати бійця не менш ніж у {NaiveFailureOrFallRateMin:P0} прогонів, вийшло {failRate:P0} ({failuresOrFalls}/{total})");
        }

        // ---- Тренувальний бій (пісочниця, титульне меню) — лише "коротко" ----

        [Test]
        public void TrainingBattle_IsShort_UnderBothHitRules()
        {
            var thresholdCs = DefaultCombatContent.Training(hitRule: HitRuleKind.Threshold);
            CombatAi.AutoResolve(thresholdCs, 400);
            Assert.AreNotEqual(CombatOutcome.Ongoing, thresholdCs.Outcome, "тренувальний бій мав завершитись, а не впертись у запобіжник");
            Assert.LessOrEqual(thresholdCs.Round, ShortRoundsCap,
                $"тренувальний бій (ThresholdRule) мав розв'язатись за <= {ShortRoundsCap} раундів, вийшло {thresholdCs.Round}");

            for (int seed = 1; seed <= SeedCount; seed++)
            {
                var cs = DefaultCombatContent.Training(hitRule: HitRuleKind.Percent, roller: new SeededDiceRoller((ulong)seed));
                CombatAi.AutoResolve(cs, 400);
                Assert.AreNotEqual(CombatOutcome.Ongoing, cs.Outcome, $"тренувальний бій (seed {seed}) мав завершитись, а не впертись у запобіжник");
                Assert.LessOrEqual(cs.Round, ShortRoundsCap,
                    $"тренувальний бій (PercentRule, seed {seed}) мав розв'язатись за <= {ShortRoundsCap} раундів, вийшло {cs.Round}");
            }
        }

        // =====================================================================
        // Харнес
        // =====================================================================

        private struct RunMetrics
        {
            public int Rounds;
            public string Outcome; // "Victory"|"Defeat"|"Retreat"|"Draw"
            public double PartyHpLossFraction; // сумарна втрата HP загону / сумарний MaxHp загону на вході в бій
            public bool AnyPartyMemberFell; // хоч один боєць загону дійшов до 0 HP (Downed або Dead) до кінця бою
        }

        private static RunMetrics RunRoomBattle(string siteId, string roomId, HitRuleKind hitRule, int seed, bool naive)
        {
            var s = NewSession(hitRule, seed);
            EnterCombatRoom(s, siteId, roomId);
            Assert.AreEqual(SessionState.Battle, s.State, $"{siteId}/{roomId}: Bloody шлях мав відкрити бій");
            return ResolveBattle(s, naive);
        }

        private static GameSession NewSession(HitRuleKind hitRule, int seed)
        {
            // RestoreState усередині NewGame однаково сідує кубик за
            // NewGameOptions.Seed (див. GameSession.NewGame) — конструкторський
            // екземпляр SeededDiceRoller лише постачає роллер, сама точка
            // відліку приходить із Seed нижче (той самий контракт, що й
            // GameSessionTests/CombatDeterminismTests).
            IDiceRoller roller = new SeededDiceRoller((ulong)Math.Max(1, seed));
            var s = new GameSession(roller);
            s.NewGame(new NewGameOptions
            {
                SkipCreation = true,
                HitRule = hitRule,
                Seed = (ulong)seed,
                TestBuildOneDayConstruction = true
            });
            FastForwardOpeningToMorning(s);
            return s;
        }

        private static void FastForwardOpeningToMorning(GameSession s)
        {
            Assert.AreEqual(SessionState.Scene, s.State);
            var step = s.AdvanceScene();
            while (!step.IsFinished)
                step = step.IsChoice ? s.ChooseSceneOption(0) : s.AdvanceScene();
            Assert.AreEqual(SessionState.Morning, s.State);
        }

        /// <summary>
        /// Заводить відряд у данж (Delve, той самий вхід, що й реальна гра) і
        /// доводить його ДО заданої бойової кімнати: кімнати ПЕРЕД нею
        /// проходяться тихо (не наша ціль — не міряємо їх), і якщо тихий
        /// обхід чужої бойової кімнати все ж зірвався в бій (Worst-полоса),
        /// той бій автопроходиться і не рахується — важлива лише кімната
        /// <paramref name="roomId"/>.
        /// </summary>
        private static void EnterCombatRoom(GameSession s, string siteId, string roomId)
        {
            var dispatch = s.DepartExpedition(siteId, ExpeditionApproach.Delve, Party, 2);
            Assert.AreEqual(DispatchResult.Success, dispatch, $"делве у {siteId} мав вдатись");

            var rooms = DefaultDungeon.Rooms(siteId);
            for (int i = 0; i < rooms.Count; i++)
            {
                var room = rooms[i];
                if (room.Id == roomId)
                {
                    Assert.AreEqual(DungeonRoomKind.Combat, room.Kind, $"{roomId} мала бути бойовою кімнатою");
                    var duringBattle = s.ResolveDungeonRoom(IncidentPath.Bloody);
                    Assert.IsNull(duringBattle, $"{siteId}/{roomId}: Bloody шлях бойової кімнати мав підвісити сесію в бій");
                    return;
                }

                s.ResolveDungeonRoom(IncidentPath.Quiet);
                if (s.State == SessionState.Battle) s.CombatAutoResolve(); // не наша кімната — просто прибрати бій з дороги
                if (i < rooms.Count - 1 && s.State == SessionState.Dungeon) s.PushDeeper();
            }

            Assert.Fail($"кімната {roomId} не знайдена серед кімнат {siteId}");
        }

        /// <summary>Наївний хід: атакуй найближчого ворога, інакше зближуйся, інакше завершуй хід — той самий БотRunner.ExecuteCombatAction (мінус галузь Overwatch, недоступна дефолтному наміру AttackNearest), веде ОБИДВІ сторони незалежно від того, чий зараз хід.</summary>
        private static void NaiveCombatStep(GameSession s)
        {
            var view = s.GetBattleView();
            var current = BotSupport.FindCurrent(view);
            if (current == null) { s.CombatEndTurn(); return; }

            var target = BotSupport.FindNearestOpposite(view, current);
            if (target == null) { s.CombatEndTurn(); return; }

            if (s.CombatAttack(target.Id) == CombatActionResult.Success) return;

            var step = BotSupport.StepToward(view, current, target.Pos);
            if (step.HasValue)
            {
                var dest = new GridPos(step.Value.X, step.Value.Y);
                if (s.CombatMove(dest) == CombatActionResult.Success) return;
            }

            s.CombatEndTurn();
        }

        private static RunMetrics ResolveBattle(GameSession s, bool naive)
        {
            if (naive)
            {
                int guard = 0;
                while (s.State == SessionState.Battle && guard++ < 4000) NaiveCombatStep(s);
                Assert.AreNotEqual(SessionState.Battle, s.State, "наївний бій мав завершитись до запобіжника guard, а не зависнути");
            }
            else
            {
                s.CombatAutoResolve();
            }

            string outcome = null;
            int rounds = 0;
            foreach (var e in s.DayLog)
            {
                if (e.Key != "combat.autoresolved" && e.Key != "combat.battle.resolved") continue;
                outcome = e.Args["outcome"];
                rounds = int.Parse(e.Args["rounds"], System.Globalization.CultureInfo.InvariantCulture);
            }
            Assert.IsNotNull(outcome, "бій мав залишити combat.autoresolved/combat.battle.resolved у DayLog");

            var view = s.LastResolvedBattleView;
            Assert.IsNotNull(view, "LastResolvedBattleView мав містити останній кадр щойно розв'язаного бою");

            int maxHpSum = 0, hpLost = 0;
            bool anyFell = false;
            foreach (var u in view.Units)
            {
                if (u.Side != "Player") continue; // лише загін гравця — FromDefector/Enemy сюди не рахуються
                maxHpSum += u.HpMax;
                hpLost += Math.Max(0, u.HpMax - u.Hp);
                if (u.Hp <= 0) anyFell = true; // Active ніколи не має 0 HP — 0 тут завжди означає Downed/Stabilized/Dead
            }

            return new RunMetrics
            {
                Rounds = rounds,
                Outcome = outcome,
                PartyHpLossFraction = maxHpSum > 0 ? (double)hpLost / maxHpSum : 0.0,
                AnyPartyMemberFell = anyFell
            };
        }

        private static double Median(List<double> xs)
        {
            var sorted = xs.OrderBy(x => x).ToList();
            int n = sorted.Count;
            if (n == 0) return 0;
            return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
        }
    }
}
