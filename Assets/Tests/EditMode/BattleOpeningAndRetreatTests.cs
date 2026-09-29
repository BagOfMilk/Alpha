using System.Collections.Generic;
using System.Linq;
using Game.Core.Balance;
using Game.Core.Checks;
using Game.Core.Combat;
using Game.Core.Dungeons;
using Game.Core.Loop;
using Game.Core.Session;
using Game.Core.Session.Views;
using Game.Gameplay.UI;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Трек C2 (Поправка №14.1 і №14.7; ROADMAP B13). Власник, 29.09.2026: старт
    /// бою залежить від підходу («Супер, але додай більше варіантів, може навіть
    /// поранення…» → старт пораненими), і з бою є вихід — відступ. Детерміновано:
    /// хто перший у раунді 1, з чим загін виходить на поле і що коштує відступ —
    /// твердження тестів.
    /// </summary>
    public class BattleOpeningAndRetreatTests
    {
        private static readonly BalanceConfig Cfg = new BalanceConfig();

        private static CombatUnit U(string id, Side side, int init, int hp = 20)
        {
            var p = new UnitProfile
            {
                DisplayName = id, MaxHp = hp, MaxAp = 8, Accuracy = 70,
                Initiative = init, MoveApPerTile = 1
            };
            return new CombatUnit(id, side, p, null);
        }

        /// <summary>Троє своїх і двоє чужих; вороги швидші за всіх — без переваги вони ходять першими.</summary>
        private static CombatState Battle(BattleOpening opening)
        {
            var cs = new CombatState(new GridMap(12, 3), Cfg, new ThresholdRule(Cfg), null);
            cs.AddUnit(U("p1", Side.Player, 5), new GridPos(0, 0));
            cs.AddUnit(U("p2", Side.Player, 4), new GridPos(0, 1));
            cs.AddUnit(U("p3", Side.Player, 3), new GridPos(0, 2));
            cs.AddUnit(U("e1", Side.Enemy, 9), new GridPos(11, 0));
            cs.AddUnit(U("e2", Side.Enemy, 8), new GridPos(11, 1));
            cs.Begin(opening);
            return cs;
        }

        private static List<string> Ids(IReadOnlyList<CombatUnit> units) => units.Select(u => u.Id).ToList();

        // ---------------- черга першого раунду ----------------

        [Test]
        public void Encounter_KeepsPlainInitiative()
        {
            var cs = Battle(BattleOpening.Encounter);
            CollectionAssert.AreEqual(new[] { "e1", "e2", "p1", "p2", "p3" }, Ids(cs.TurnOrder));
            Assert.AreEqual("e1", cs.Current.Id);
            Assert.IsFalse(cs.Journal.Any(e => e.Key.StartsWith("combat.log.opening.")), "зустрічний бій без рядка старту");
        }

        [Test]
        public void FirstStrike_WholeSquadActsFirstInRoundOne_ThenPlainInitiative()
        {
            var cs = Battle(BattleOpening.FirstStrike);

            CollectionAssert.AreEqual(new[] { "p1", "p2", "p3", "e1", "e2" }, Ids(cs.TurnOrder));
            CollectionAssert.AreEqual(new[] { "e1", "e2", "p1", "p2", "p3" }, Ids(cs.NextRoundTurnOrder),
                "з раунду 2 — звичайна черга впереміш");
            Assert.AreEqual("p1", cs.Current.Id);
            Assert.IsTrue(cs.Journal.Any(e => e.Key == CombatLogKeys.OpeningFirstStrike));

            // Раунд 1 до кінця: кожен ходить рівно раз — зайвих ходів немає.
            var actors = new List<string>();
            while (cs.Round == 1 && cs.Outcome == CombatOutcome.Ongoing)
            {
                actors.Add(cs.Current.Id);
                cs.EndTurn();
            }
            CollectionAssert.AreEqual(new[] { "p1", "p2", "p3", "e1", "e2" }, actors);
            Assert.AreEqual("e1", cs.Current.Id, "раунд 2 починає найшвидший — перевага першого раунду згоріла");
        }

        [Test]
        public void Spotted_EnemiesActFirst_EvenWhenSlower()
        {
            var cs = new CombatState(new GridMap(12, 2), Cfg, new ThresholdRule(Cfg), null);
            cs.AddUnit(U("fast_hero", Side.Player, 10), new GridPos(0, 0));
            cs.AddUnit(U("slow_enemy", Side.Enemy, 1), new GridPos(11, 0));
            cs.Begin(BattleOpening.Spotted);

            Assert.AreEqual("slow_enemy", cs.Current.Id);
            CollectionAssert.AreEqual(new[] { "fast_hero", "slow_enemy" }, Ids(cs.NextRoundTurnOrder));
        }

        [Test]
        public void Ambush_SquadFirst_AndEnemiesMarkedForExactlyOneTurn()
        {
            var cs = Battle(BattleOpening.Ambush);

            Assert.AreEqual(Side.Player, cs.Current.Side);
            foreach (var e in cs.Units.Where(u => u.Side == Side.Enemy))
            {
                var marked = e.GetStatus(StatusType.Marked);
                Assert.IsNotNull(marked, e.Id + ": вороги в засідці позначені (наявний стан, №14.1)");
                Assert.AreEqual(Cfg.Combat.AmbushMarkedTurns, marked.RemainingTurns, "лише на перший раунд, без скорочення Волею");
            }
            Assert.IsTrue(cs.Units.Where(u => u.Side == Side.Player).All(u => u.GetStatus(StatusType.Marked) == null));
        }

        [Test]
        public void UnderFire_EnemiesFirst_TwoWoundedAndBleeding_ThirdUntouched()
        {
            var cs = Battle(BattleOpening.UnderFire);

            Assert.AreEqual(Side.Enemy, cs.Current.Side);
            int loss = 20 * Cfg.Combat.UnderFireHpLossPercent / 100;
            foreach (var id in new[] { "p1", "p2" })
            {
                var u = cs.GetUnit(id);
                Assert.AreEqual(20 - loss, u.Hp, id + ": старт пораненим");
                Assert.IsNotNull(u.GetStatus(StatusType.Bleeding), id + ": кровоточить");
            }
            var third = cs.GetUnit("p3");
            Assert.AreEqual(20, third.Hp);
            Assert.IsNull(third.GetStatus(StatusType.Bleeding));
            Assert.IsTrue(cs.Journal.Any(e => e.Key == CombatLogKeys.OpeningUnderFire));
        }

        [Test]
        public void UnderFire_NeverKnocksAnyoneDownBeforeTheFight()
        {
            var cs = new CombatState(new GridMap(12, 2), Cfg, new ThresholdRule(Cfg), null);
            cs.AddUnit(U("frail", Side.Player, 5, hp: 1), new GridPos(0, 0));
            cs.AddUnit(U("e", Side.Enemy, 9), new GridPos(11, 0));
            cs.Begin(BattleOpening.UnderFire);

            Assert.AreEqual(1, cs.GetUnit("frail").Hp, "перший залп поранить, але не валить");
            Assert.IsTrue(cs.GetUnit("frail").IsActive);
        }

        // ---------------- данж: вибір старту і прогноз до вибору ----------------

        private sealed class Actor : ISettlementActor
        {
            public string Id { get; set; }
            public bool IsPresentInSettlement { get; set; } = true;
            public bool IsProtagonist { get; set; }
            public string HeldPositionId { get; set; }
            public int Value { get; set; }
            public int GetCheckValue(SkillKey skill, ApproachForm approach = ApproachForm.Neutral) => Value;
            public int GetTraitModifier(SkillKey skill) => 0;
        }

        private static IReadOnlyList<ISettlementActor> Party(int value)
            => new List<ISettlementActor> { new Actor { Id = "a", Value = value } };

        private static DungeonRun CampRun()
            => DefaultDungeon.Start(DefaultDungeon.AbandonedCamp, new[] { "protagonist", "maksym", "myroslava" }, new BalanceConfig());

        [Test]
        public void Dungeon_BloodyPath_ScoutedSquadAmbushes_OthersStrikeFirst()
        {
            Assert.AreEqual(DungeonBattleStart.Ambush, CampRun().PreviewBloodyStart(Party(10)),
                "загін, що міг би прокрастися, нападає із засідки");
            Assert.AreEqual(DungeonBattleStart.FirstStrike, CampRun().PreviewBloodyStart(Party(0)));

            var run = CampRun();
            var res = run.ResolveRoom(IncidentPath.Bloody, Party(10));
            Assert.IsTrue(res.NeedsBattle);
            Assert.AreEqual(DungeonBattleStart.Ambush, run.PendingBattle.Start, "той самий розрахунок, що в прогнозі");
        }

        [Test]
        public void Dungeon_QuietFail_Spotted_ButUnderFireWhenTheSiteIsAlreadyDangerous()
        {
            var calm = CampRun();
            Assert.AreEqual(DungeonBattleStart.Spotted, calm.PreviewQuietFailStart());
            var res = calm.ResolveRoom(IncidentPath.Quiet, Party(0));
            Assert.IsTrue(res.NeedsBattle, "слабкий загін не прослизнув");
            Assert.AreEqual(DungeonBattleStart.Spotted, calm.PendingBattle.Start);

            // Полоса загрози на вході в кімнату — Dangerous (eb:2 у зліпку).
            var deep = CampRun();
            deep.RestoreState(deep.CaptureState().Replace("|eb:0", "|eb:2"));
            Assert.AreEqual(DungeonBattleStart.UnderFire, deep.PreviewQuietFailStart());
        }

        [Test]
        public void Dungeon_PendingStart_SurvivesSaveRestore()
        {
            var run = CampRun();
            run.ResolveRoom(IncidentPath.Bloody, Party(10));

            var restored = CampRun();
            restored.RestoreState(run.CaptureState());

            Assert.IsTrue(restored.AwaitingBattle);
            Assert.AreEqual(DungeonBattleStart.Ambush, restored.PendingBattle.Start,
                "відновлений прогін не забуває, як почнеться бій");
        }

        [Test]
        public void Dungeon_RetreatFromBattle_LeavesTheSite_NotAWipe()
        {
            var run = CampRun();
            run.ResolveRoom(IncidentPath.Bloody, Party(0));

            var rep = run.RetreatFromBattle();

            Assert.AreEqual(DungeonOutcome.Abandoned, run.Outcome, "відступ — обережний вихід, а не вайп");
            Assert.IsFalse(run.AwaitingBattle);
            Assert.AreEqual(0, run.UnbankedGold + run.UnbankedBuildComponent + run.UnbankedCraftComponent);
            Assert.AreEqual(1, rep.DepthReached);
        }

        // ---------------- GameSession: відступ і вигляд бою ----------------

        private static NewGameOptions SkipCreation()
            => new NewGameOptions { SkipCreation = true, HitRule = HitRuleKind.Threshold };

        private static void ToMorning(GameSession s)
        {
            var step = s.AdvanceScene();
            while (!step.IsFinished)
                step = step.IsChoice ? s.ChooseSceneOption(0) : s.AdvanceScene();
            Assert.AreEqual(SessionState.Morning, s.State);
        }

        private static void EndTurnsUntilPlayer(GameSession s)
        {
            for (int guard = 0; guard < 20; guard++)
            {
                var v = s.GetBattleView();
                if (v == null || !v.IsAiTurn) return;
                s.CombatAiStepOneAction();
            }
        }

        [Test]
        public void Training_Retreat_OnOwnTurn_EndsTheFight_WithoutConsequences()
        {
            var s = new GameSession();
            s.NewTrainingBattle(new TrainingBattleOptions { HitRule = HitRuleKind.Threshold });
            EndTurnsUntilPlayer(s);

            var view = s.GetBattleView();
            Assert.AreEqual("Encounter", view.Opening);
            Assert.AreEqual("ui.battle.retreat.consequence.training", view.RetreatConsequenceKey);
            Assert.IsNotNull(view.NextRoundOrder);

            Assert.AreEqual(CombatActionResult.Success, s.CombatRetreat());
            Assert.IsNull(s.GetBattleView(), "бій розв'язано");
            Assert.AreEqual("Retreat", s.LastResolvedBattleView.Outcome);
        }

        [Test]
        public void DungeonBattle_Retreat_GoesHome_WithoutLoot_AndStartIsShownBeforeChoice()
        {
            var s = new GameSession();
            s.NewGame(SkipCreation());
            ToMorning(s);
            s.DepartExpedition(DefaultDungeon.AbandonedCamp, Game.Core.Expeditions.ExpeditionApproach.Delve,
                new[] { "protagonist", "maksym", "myroslava" }, 2);

            var room = s.GetDungeonView().CurrentRoom;
            Assert.IsNotNull(room.BloodyOpening, "прогноз старту видно ДО вибору шляху (UI-02)");
            Assert.IsNotNull(room.QuietFailOpening);

            s.ResolveDungeonRoom(IncidentPath.Bloody);
            Assert.AreEqual(SessionState.Battle, s.State);
            var battle = s.GetBattleView();
            Assert.AreEqual(room.BloodyOpening, battle.Opening, "прогноз = застосований старт");
            Assert.AreEqual("ui.battle.retreat.consequence.dungeon", battle.RetreatConsequenceKey);

            EndTurnsUntilPlayer(s);
            int goldBefore = s.GetEconomyView().Gold;
            Assert.AreEqual(CombatActionResult.Success, s.CombatRetreat());

            Assert.AreEqual(SessionState.Morning, s.State, "відступ із данжу — додому, а не «кімнату пройдено»");
            Assert.IsNull(s.GetDungeonView());
            Assert.AreEqual(goldBefore, s.GetEconomyView().Gold, "лут кімнати відступ не дає");
        }

        [Test]
        public void Retreat_IsRejected_OnTheEnemysTurn()
        {
            var s = new GameSession();
            s.NewTrainingBattle(new TrainingBattleOptions { HitRule = HitRuleKind.Threshold });

            // Свої ходи — пропускаємо, доки хід не перейде до ворога (ШІ веде
            // презентер, сесія сама його не крутить).
            for (int guard = 0; guard < 10 && !s.GetBattleView().IsAiTurn; guard++)
                s.CombatEndTurn();
            Assert.IsTrue(s.GetBattleView().IsAiTurn, "у тренуванні є ворожий хід");

            Assert.AreEqual(CombatActionResult.InvalidAction, s.CombatRetreat(), "відступ — лише у свій хід");
            Assert.IsNotNull(s.GetBattleView(), "бій триває");
        }

        // ---------------- колесо черги: наступний раунд іде звичайною чергою ----------------

        [Test]
        public void Wheel_AfterRoundOneOverride_ShowsPlainOrderForTheNextRound()
        {
            var units = new[] { "p1", "p2", "e1" }.Select(id => new BattleUnitView { Id = id, Side = id[0] == 'p' ? "Player" : "Enemy" }).ToArray();
            var view = new BattleView
            {
                Round = 1,
                CurrentUnitId = "p1",
                InitiativeOrder = new List<string> { "p1", "p2", "e1" },   // перший удар
                NextRoundOrder = new List<string> { "e1", "p1", "p2" },    // звичайна черга
                Units = units
            };

            var wheel = TurnWheelModel.Build(view);

            CollectionAssert.AreEqual(new[] { "p1", "p2", "e1", "e1", "p1", "p2" }, wheel.Slots.Select(s => s.UnitId).ToArray());
            Assert.AreEqual(3, wheel.NextRoundStartsAt);
        }
    }
}
