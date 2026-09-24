using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Checks;
using Game.Core.Dungeons;
using Game.Core.Economy;
using Game.Core.Loop;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Данж (Core/Dungeons, R4): повністю детермінований push-your-luck — жодної
    /// випадковості, кімнати авторські й у фіксованому порядку. Тихий обхід
    /// кожної бойової кімнати (Поправка №1), кроваво/провалений тихий шлях лише
    /// ПРОСЯТЬ бій (BattleRequest) і чекають ReportCombat — самі бою не ведуть.
    /// Лут банкується ЛИШЕ на Extract; вайп (Worst у бою) незабанковане знищує.
    /// </summary>
    public class DungeonTests
    {
        private sealed class FakeActor : ISettlementActor
        {
            public string Id { get; set; }
            public bool IsPresentInSettlement { get; set; } = true;
            public bool IsProtagonist { get; set; }
            public string HeldPositionId { get; set; }

            /// <summary>Значення за замовчуванням на БУДЬ-ЯКИЙ навик (зручно для тестів,
            /// де конкретний навик не важливий). <see cref="Skills"/> — точкові
            /// перевизначення для тестів, де важливо розрізнити навики (OR-логіка).</summary>
            public int Value { get; set; }
            public Dictionary<string, int> Skills { get; } = new Dictionary<string, int>();

            public int GetCheckValue(SkillKey skill)
                => Skills.TryGetValue(skill.Id, out var v) ? v : Value;
            public int GetTraitModifier(SkillKey skill) => 0;
        }

        private static BalanceConfig Cfg() => new BalanceConfig();

        private static IReadOnlyList<ISettlementActor> Squad(params int[] survivalValues)
        {
            var list = new List<ISettlementActor>();
            for (int i = 0; i < survivalValues.Length; i++)
                list.Add(new FakeActor { Id = "s" + i, Value = survivalValues[i] });
            return list;
        }

        private static DungeonRun NewCampRun(BalanceConfig cfg = null)
            => DefaultDungeon.Start(DefaultDungeon.AbandonedCamp, new[] { "protagonist", "maksym", "myroslava" },
                cfg ?? Cfg());

        // ---- Авторський контент: рівно ці кімнати, у цьому порядку (R4 acceptance) ----

        [Test]
        public void AbandonedCamp_ReturnsThreeAuthoredRoomsInOrder()
        {
            var rooms = DefaultDungeon.Rooms(DefaultDungeon.AbandonedCamp);

            Assert.AreEqual(3, rooms.Count);
            Assert.AreEqual(DungeonRoomKind.Combat, rooms[0].Kind);
            Assert.AreEqual(DungeonRoomKind.Cache, rooms[1].Kind);
            Assert.AreEqual(DungeonRoomKind.Event, rooms[2].Kind);
            Assert.AreEqual("scout_horn", rooms[1].NamedItemId);
            Assert.GreaterOrEqual(rooms[2].EventOptions.Count, 2, "у події є вибір (US-12.3)");
        }

        [Test]
        public void EveryCombatRoom_HasAtLeastOneQuietBypass()
        {
            foreach (var siteId in DefaultDungeon.KnownSiteIds)
            {
                var rooms = DefaultDungeon.Rooms(siteId);
                foreach (var room in rooms)
                {
                    if (room.Kind != DungeonRoomKind.Combat) continue;
                    Assert.Greater(room.QuietChecks.Count, 0,
                        $"{siteId}/{room.Id}: кожна бойова кімната має тихий обхід (Поправка №1)");
                }
            }
        }

        [Test]
        public void UnknownSiteId_ReturnsNull()
        {
            Assert.IsNull(DefaultDungeon.Rooms("no_such_site"));
        }

        // ---- Тихий обхід — БЕЗ жодного бою (ключова вимога пакета) ----

        [Test]
        public void QuietBypass_Succeeds_NoBattleEverRequested()
        {
            var run = NewCampRun();
            // Survival=13 з запасом перекриває поріг 5 + штраф Threat (Tense=+1) = 6.
            var res = run.ResolveRoom(IncidentPath.Quiet, Squad(13));

            Assert.IsTrue(res.Bypassed);
            Assert.IsTrue(res.Cleared);
            Assert.IsFalse(res.NeedsBattle);
            Assert.IsFalse(run.AwaitingBattle, "тихий обхід не веде до бою взагалі");
            Assert.IsNull(run.PendingBattle);
            Assert.AreEqual(0, res.GainedMaterials, "0 ризику — 0 нагороди за обхід");
            Assert.AreEqual(OutcomeBand.Best, res.QuietBand);
        }

        [Test]
        public void QuietBypass_UsesBestOfEitherSkill_PersuadeAlone()
        {
            // Обидва навики нульові за замовчуванням (Value=0); у strongPersuade
            // явно перевизначений ЛИШЕ Persuade. Якби OR-логіка мовчки брала
            // тільки Survival, тест впав би — це і перевіряється.
            var weakSurvival = new FakeActor { Id = "weak", Value = 0 };
            var strongPersuade = new FakeActor { Id = "strong", Value = 0 };
            strongPersuade.Skills[SkillKeys.Persuade.Id] = 9;
            var run = NewCampRun();

            var res = run.ResolveRoom(IncidentPath.Quiet, new List<ISettlementActor> { weakSurvival, strongPersuade });

            Assert.IsTrue(res.Bypassed, "переконання окремо теж проходить (амендмент 1 — АБО)");
        }

        [Test]
        public void QuietBypass_Fails_ForcesBattle_NeverResolvesItself()
        {
            var run = NewCampRun();
            var res = run.ResolveRoom(IncidentPath.Quiet, Squad(0, 0)); // обидва навики нульові

            Assert.AreEqual(OutcomeBand.Worst, res.QuietBand);
            Assert.IsTrue(res.NeedsBattle);
            Assert.IsFalse(res.Cleared, "провалений тихий шлях НЕ резолвиться сам");
            Assert.IsTrue(run.AwaitingBattle);
            Assert.IsNotNull(run.PendingBattle);
            Assert.AreEqual("arena_camp_yard_8x8", run.PendingBattle.ArenaKey);
            CollectionAssert.AreEqual(new[] { "horde_skirmisher", "horde_skirmisher" }, run.PendingBattle.EnemyIds);
            CollectionAssert.AreEqual(new[] { "protagonist", "maksym", "myroslava" }, run.PendingBattle.PartyIds);
        }

        [Test]
        public void BloodyChoice_EntersAwaitingBattle_WithoutTryingAnyCheck()
        {
            var run = NewCampRun();
            var res = run.ResolveRoom(IncidentPath.Bloody, Squad(9)); // сила відряду тут не має значення

            Assert.IsTrue(res.NeedsBattle);
            Assert.IsNull(res.QuietBand, "кровавий шлях не чіпає тиху перевірку взагалі");
            Assert.IsTrue(run.AwaitingBattle);
        }

        [Test]
        public void ResolveRoom_WhileAwaitingBattle_Throws()
        {
            var run = NewCampRun();
            run.ResolveRoom(IncidentPath.Bloody, Squad(9));

            Assert.Throws<System.InvalidOperationException>(() => run.ResolveRoom(IncidentPath.Quiet, Squad(9)));
        }

        // ---- Бій: перемога дає лут, Worst — вайп ----

        [Test]
        public void ReportCombat_Victory_GrantsLootAndClearsRoom()
        {
            var run = NewCampRun();
            run.ResolveRoom(IncidentPath.Bloody, Squad(9));

            var res = run.ReportCombat(OutcomeBand.Good, new[] { "maksym" });

            Assert.IsTrue(res.Cleared);
            Assert.IsFalse(res.Wiped);
            Assert.IsFalse(run.AwaitingBattle);
            Assert.Greater(run.UnbankedMaterials, 0, "перемога у бою за кімнату 1 теж дає видобуток");
        }

        [Test]
        public void ReportCombat_Worst_WipesRun_LosesAllUnbanked()
        {
            var run = NewCampRun();

            // Спершу тихо пройти б не вдасться -- ідемо кроваво і програємо.
            run.ResolveRoom(IncidentPath.Bloody, Squad(9));
            var res = run.ReportCombat(OutcomeBand.Worst, new[] { "protagonist", "maksym", "myroslava" });

            Assert.IsTrue(res.Wiped);
            Assert.AreEqual(DungeonOutcome.Wiped, run.Outcome);
            Assert.AreEqual(0, run.UnbankedMaterials, "незабанковане втрачено");
            Assert.AreEqual(0, run.UnbankedGold);
            Assert.IsFalse(run.Active);

            var baseState = new BaseState(new Roster(), new ResourceLedger(), new BalanceConfig());
            Assert.Throws<System.InvalidOperationException>(() => run.Extract(baseState),
                "екстракт неможливий після вайпу");
        }

        [Test]
        public void ReportCombat_WhileNotAwaitingBattle_Throws()
        {
            var run = NewCampRun();
            Assert.Throws<System.InvalidOperationException>(
                () => run.ReportCombat(OutcomeBand.Good, System.Array.Empty<string>()));
        }

        // ---- Cache: гарантований лут + іменний предмет ----

        [Test]
        public void Cache_GrantsGuaranteedMaterialsAndNamedItem_NoCheckInvolved()
        {
            var run = NewCampRun();
            run.ResolveRoom(IncidentPath.Quiet, Squad(9)); // проходимо кімнату 1 без бою
            run.Push();

            var res = run.ResolveRoom(IncidentPath.Quiet, Squad(0)); // Cache — шлях байдужий, гарантовано

            Assert.IsTrue(res.Cleared);
            Assert.AreEqual(3, res.GainedMaterials);
            CollectionAssert.Contains(res.GrantedItemIds, "scout_horn");
            CollectionAssert.Contains(run.UnbankedItemIds, "scout_horn");
        }

        // ---- Push: семантика і зростання Threat ----

        [Test]
        public void Push_RequiresCurrentRoomCleared()
        {
            var run = NewCampRun();
            Assert.Throws<System.InvalidOperationException>(() => run.Push());
        }

        [Test]
        public void Push_RaisesThreat_OnEveryStep_IncludingTheFirst()
        {
            var run = NewCampRun();
            int perPush = Cfg().Dungeon.ThreatPerPush;

            // Threat -- internal: тест у тій самій збірці (InternalsVisibleTo).
            Assert.AreEqual(perPush, run.Threat, "вхід у першу кімнату теж коштує Threat");

            run.ResolveRoom(IncidentPath.Quiet, Squad(9));
            run.Push();
            Assert.AreEqual(perPush * 2, run.Threat);
        }

        [Test]
        public void Push_PastLastRoom_Throws()
        {
            var run = NewCampRun();
            run.ResolveRoom(IncidentPath.Quiet, Squad(9));
            run.Push();
            run.ResolveRoom(IncidentPath.Quiet, Squad(0)); // Cache
            run.Push();
            run.ResolveEvent(1); // Event, останню кімнату пройдено

            Assert.Throws<System.InvalidOperationException>(() => run.Push(),
                "після останньої кімнати лишається лише Extract/Abandon");
        }

        // ---- Event: жадібно проти обережно ----

        [Test]
        public void ResolveEvent_Greedy_TradesThreatForMaterials_AndCausesFear()
        {
            var run = RunAtRoom3();
            int threatBefore = run.Threat;

            var res = run.ResolveEvent(0); // "greedy"

            Assert.AreEqual("greedy", res.EventOptionId);
            Assert.AreEqual(5, res.GainedMaterials);
            Assert.Greater(run.Threat, threatBefore);
            Assert.IsTrue(res.ThreatBandChanged || run.Threat > threatBefore);
            Assert.IsTrue(res.Consequence.CausedFear);
            Assert.AreEqual(-5, res.Consequence.FactionDeltas["tuhar_boyars"]);
            CollectionAssert.Contains(res.Consequence.FlagsToSet, "abandoned_camp_grain_taken");
        }

        [Test]
        public void ResolveEvent_Cautious_KeepsThreatQuiet_NoFear()
        {
            var run = RunAtRoom3();
            int threatBefore = run.Threat;

            var res = run.ResolveEvent(1); // "cautious"

            Assert.AreEqual("cautious", res.EventOptionId);
            Assert.AreEqual(threatBefore, run.Threat, "обережний варіант Threat не піднімає");
            Assert.IsFalse(res.Consequence.CausedFear);
            Assert.AreEqual(2, res.GainedMaterials);
        }

        [Test]
        public void ResolveEvent_OnNonEventRoom_Throws()
        {
            var run = NewCampRun();
            Assert.Throws<System.InvalidOperationException>(() => run.ResolveEvent(0));
        }

        [Test]
        public void ResolveRoom_OnEventRoom_Throws()
        {
            var run = RunAtRoom3();
            Assert.Throws<System.InvalidOperationException>(() => run.ResolveRoom(IncidentPath.Quiet, Squad(9)));
        }

        // ---- Extract / Abandon ----

        [Test]
        public void Extract_BanksUnbankedIntoBaseState_ThenClearsIt()
        {
            var run = NewCampRun();
            run.ResolveRoom(IncidentPath.Quiet, Squad(9));
            run.Push();
            run.ResolveRoom(IncidentPath.Quiet, Squad(0)); // Cache: +3 матеріали, +horn

            var baseState = new BaseState(new Roster(), new ResourceLedger(), new BalanceConfig());
            int expectMaterials = run.UnbankedMaterials;
            int expectGold = run.UnbankedGold;

            var rep = run.Extract(baseState);

            Assert.AreEqual(DungeonOutcome.Extracted, run.Outcome);
            Assert.AreEqual(expectMaterials, baseState.Resources.Get(ResourceType.Materials));
            Assert.AreEqual(expectGold, baseState.Resources.Get(ResourceType.Gold));
            Assert.AreEqual(expectMaterials, rep.Materials);
            CollectionAssert.Contains(rep.ItemIds, "scout_horn");
            Assert.AreEqual(0, run.UnbankedMaterials, "незабанковане очищено");
            Assert.IsFalse(run.Active);
        }

        [Test]
        public void Abandon_ForfeitsUnbanked_ButIsNotAWipe()
        {
            var run = NewCampRun();
            run.ResolveRoom(IncidentPath.Quiet, Squad(9));
            run.Push();
            run.ResolveRoom(IncidentPath.Quiet, Squad(0));
            Assert.Greater(run.UnbankedMaterials, 0);

            run.Abandon();

            Assert.AreEqual(DungeonOutcome.Abandoned, run.Outcome);
            Assert.AreNotEqual(DungeonOutcome.Wiped, run.Outcome, "обережний вихід — не вайп");
            Assert.AreEqual(0, run.UnbankedMaterials);
        }

        // ---- Детермінізм ----

        [Test]
        public void SameInputs_ProduceIdenticalResolutionSequence()
        {
            var runA = NewCampRun();
            var runB = NewCampRun();

            var r1A = runA.ResolveRoom(IncidentPath.Quiet, Squad(9));
            var r1B = runB.ResolveRoom(IncidentPath.Quiet, Squad(9));
            Assert.AreEqual(r1A.Bypassed, r1B.Bypassed);
            Assert.AreEqual(r1A.QuietBand, r1B.QuietBand);

            runA.Push(); runB.Push();
            var r2A = runA.ResolveRoom(IncidentPath.Quiet, Squad(0));
            var r2B = runB.ResolveRoom(IncidentPath.Quiet, Squad(0));
            Assert.AreEqual(r2A.GainedMaterials, r2B.GainedMaterials);
            CollectionAssert.AreEqual(r2A.GrantedItemIds, r2B.GrantedItemIds);

            runA.Push(); runB.Push();
            var r3A = runA.ResolveEvent(0);
            var r3B = runB.ResolveEvent(0);
            Assert.AreEqual(r3A.GainedMaterials, r3B.GainedMaterials);
            Assert.AreEqual(runA.Threat, runB.Threat, "той самий шлях -- той самий Threat, без жодного кубика");
        }

        // ---- Сейв: капчур/рестор ----

        [Test]
        public void CaptureState_RestoreState_RoundTripsProgress()
        {
            var run = NewCampRun();
            run.ResolveRoom(IncidentPath.Quiet, Squad(9));
            run.Push();
            run.ResolveRoom(IncidentPath.Quiet, Squad(0)); // Cache: matéria+horn незабанковані

            var blob = run.CaptureState();

            var restored = NewCampRun(); // свіжий інстанс з тими самими авторськими кімнатами
            restored.RestoreState(blob);

            Assert.AreEqual(run.Outcome, restored.Outcome);
            Assert.AreEqual(run.Depth, restored.Depth);
            Assert.AreEqual(run.RoomsCleared, restored.RoomsCleared);
            Assert.AreEqual(run.CurrentCleared, restored.CurrentCleared);
            Assert.AreEqual(run.AwaitingBattle, restored.AwaitingBattle);
            Assert.AreEqual(run.Threat, restored.Threat);
            Assert.AreEqual(run.UnbankedMaterials, restored.UnbankedMaterials);
            Assert.AreEqual(run.UnbankedGold, restored.UnbankedGold);
            CollectionAssert.AreEqual(run.UnbankedItemIds, restored.UnbankedItemIds);
        }

        [Test]
        public void CaptureState_RestoreState_PreservesAwaitingBattle()
        {
            var run = NewCampRun();
            run.ResolveRoom(IncidentPath.Bloody, Squad(9));
            var blob = run.CaptureState();

            var restored = NewCampRun();
            restored.RestoreState(blob);

            Assert.IsTrue(restored.AwaitingBattle);
            Assert.IsNotNull(restored.PendingBattle);
            Assert.AreEqual(run.PendingBattle.ArenaKey, restored.PendingBattle.ArenaKey);
        }

        // ---- Другий, менший данж (вільна гра) ----

        [Test]
        public void OldHermitage_IsASmallerIndependentSite()
        {
            var rooms = DefaultDungeon.Rooms(DefaultDungeon.OldHermitage);
            Assert.AreEqual(2, rooms.Count);
            Assert.AreEqual(DungeonRoomKind.Combat, rooms[0].Kind);
            Assert.Greater(rooms[0].QuietChecks.Count, 0);
        }

        // ---- допоміжне ----

        private static DungeonRun RunAtRoom3()
        {
            var run = NewCampRun();
            run.ResolveRoom(IncidentPath.Quiet, Squad(9));
            run.Push();
            run.ResolveRoom(IncidentPath.Quiet, Squad(0));
            run.Push();
            return run;
        }
    }
}
