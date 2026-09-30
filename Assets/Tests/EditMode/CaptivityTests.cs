using System.Collections.Generic;
using System.Linq;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Combat;
using Game.Core.Prisoners;
using Game.Core.Session.Views;
using Game.Core.Session;
using Game.Core.Session.Bots;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Трек C6 — поразка → полон (Поправка №14.7; власник, 29.09.2026: «Але десь
    /// половина має втекти», протагоніст у полон — «ні не може»). Хто втікає —
    /// детерміновано за порядком падіння; бранець вибуває з громади; годинник полону
    /// звучить полосами і тягне до зради; викуп, перемовини і рейд повертають;
    /// усе переживає відновлення у свіжу сесію.
    /// </summary>
    public class CaptivityTests
    {
        // ---------------- хто втікає ----------------

        [Test]
        public void Defeat_PartyOfThree_ProtagonistAndTheLastToFallEscape()
        {
            var split = CaptivityRules.Split(new[] { "hero", "a", "b" }, new HashSet<string>(),
                new[] { "a", "hero", "b" }, "hero", retreat: false);
            CollectionAssert.AreEquivalent(new[] { "hero", "b" }, split.Escaped, "протагоніст і той, хто впав пізніше");
            CollectionAssert.AreEqual(new[] { "a" }, split.Captured);
        }

        [Test]
        public void Defeat_HalfRoundedUp_Escapes_AndTheProtagonistIsNeverTaken()
        {
            var four = CaptivityRules.Split(new[] { "hero", "a", "b", "c" }, new HashSet<string>(),
                new[] { "hero", "a", "b", "c" }, "hero", retreat: false);
            Assert.AreEqual(2, four.Escaped.Count, "⌈4/2⌉");
            CollectionAssert.Contains(four.Escaped, "hero", "навіть якщо впав першим");
            CollectionAssert.Contains(four.Escaped, "c", "друге місце — хто впав останнім");

            var alone = CaptivityRules.Split(new[] { "hero" }, new HashSet<string>(), new[] { "hero" }, "hero", false);
            CollectionAssert.AreEqual(new[] { "hero" }, alone.Escaped);
            Assert.AreEqual(0, alone.Captured.Count);
        }

        [Test]
        public void Retreat_StandingCarryOneEach_AndIsNeverWorseThanFightingToTheEnd()
        {
            var ids = new[] { "hero", "a", "b", "c", "d" };
            // Перебір: хто стоїть на момент відступу (кожна підмножина), порядок падіння — решта за списком.
            for (int mask = 0; mask < 1 << ids.Length; mask++)
            {
                var standing = new HashSet<string>();
                var fallen = new List<string>();
                for (int i = 0; i < ids.Length; i++)
                    if ((mask & (1 << i)) != 0) standing.Add(ids[i]); else fallen.Add(ids[i]);

                var retreat = CaptivityRules.Split(ids, standing, fallen, "hero", retreat: true);
                var defeat = CaptivityRules.Split(ids, new HashSet<string>(), fallen, "hero", retreat: false);
                Assert.GreaterOrEqual(retreat.Escaped.Count, defeat.Escaped.Count, "маска " + mask);
                CollectionAssert.Contains(retreat.Escaped, "hero");
                foreach (var s in standing) CollectionAssert.Contains(retreat.Escaped, s, "на ногах — виходять");
                Assert.AreEqual(ids.Length, retreat.Escaped.Count + retreat.Captured.Count);
            }

            var oneStanding = CaptivityRules.Split(new[] { "hero", "a", "b" }, new HashSet<string> { "hero" },
                new[] { "a", "b" }, "hero", retreat: true);
            CollectionAssert.AreEquivalent(new[] { "hero", "b" }, oneStanding.Escaped, "виніс того, хто впав останнім");
        }

        // ---------------- реєстр полону ----------------

        [Test]
        public void Ledger_ClockGoesThroughBands_WithASignalEachTime_AndCostsMoreLoyaltyLater()
        {
            var ledger = new CaptivityLedger();
            var c = ledger.Take("a", "enemy.tuhar_boyar", 1, ledger.NewGroupId(1), new[] { "enemy.tuhar_boyar" }, 1);
            var bands = new List<CaptivityBand>();
            int signals = 0;
            int previousDelta = 0;
            for (int day = 2; day <= 9; day++)
            {
                signals += ledger.Tick(day).Count;
                bands.Add(ledger.BandOf(c));
                Assert.LessOrEqual(ledger.LoyaltyDeltaFor(c), previousDelta, "що довше в полоні — то дорожче");
                previousDelta = ledger.LoyaltyDeltaFor(c);
            }
            Assert.AreEqual(CaptivityBand.Breaking, bands.Last());
            Assert.AreEqual(2, signals, "Holding → Worn → Breaking: дві зміни полоси — два сигнали (інваріант 4)");
        }

        [Test]
        public void Ledger_RansomDependsOnRankAndTrade_TalkOnRank_AndSurvivesCaptureRestore()
        {
            var a = new CaptivityLedger();
            var grunt = a.Take("a", "enemy.horde_scout", 0, "g1x1", new[] { "enemy.horde_scout", "enemy.horde_skirmisher" }, 3);
            var boss = a.Take("b", "enemy.burunda", 2, "g1x1", new[] { "enemy.burunda" }, 3);
            Assert.Less(a.RansomFor(grunt, 0), a.RansomFor(boss, 0));
            Assert.Less(a.RansomFor(grunt, 4), a.RansomFor(grunt, 0), "Торгівля збиває ціну");
            Assert.Less(a.TalkThreshold(grunt), a.TalkThreshold(boss));
            a.Tick(7);

            var b = new CaptivityLedger();
            b.RestoreState(a.CaptureState());
            Assert.AreEqual(2, b.All.Count);
            Assert.AreEqual(a.BandOf(grunt), b.BandOf(b.Get("a")));
            CollectionAssert.AreEqual(grunt.EnemyGroup, b.Get("a").EnemyGroup);
            StringAssert.DoesNotContain(";", a.CaptureState());
            StringAssert.DoesNotContain("=", a.CaptureState());
        }

        // ---------------- сесія ----------------

        private static GameSession Morning()
        {
            var s = new GameSession();
            s.NewGame(new NewGameOptions { SkipCreation = true, HitRule = HitRuleKind.Threshold });
            var step = s.AdvanceScene();
            while (!step.IsFinished) step = step.IsChoice ? s.ChooseSceneOption(0) : s.AdvanceScene();
            return s;
        }

        /// <summary>Будь-хто з ростеру, крім протагоніста (склад прибульців залежить від виборів — №12.9).</summary>
        private static CompanionSummary SomeCompanion(GameSession s) =>
            s.GetRosterView().Companions.First(c => c.Id != GameSession.ProtagonistId && c.Status != CompanionStatus.Dead);

        [Test]
        public void Session_Captive_LeavesTheSettlement_AndTheProtagonistIsNeverTaken()
        {
            var s = Morning();
            Assert.IsFalse(s.TakeCaptive(GameSession.ProtagonistId, "enemy.horde_scout", 0, null, new[] { "enemy.horde_scout" }));

            var who = SomeCompanion(s);
            string slot = who.AssignedSlotId;
            Assert.IsTrue(s.TakeCaptive(who.Id, "enemy.horde_scout", 0, null, new[] { "enemy.horde_scout" }));

            var after = s.GetRosterView().Companions.Single(c => c.Id == who.Id);
            Assert.AreEqual(CompanionStatus.Captive, after.Status);
            Assert.IsNull(after.AssignedSlotId, "пост звільнено");
            if (slot != null)
                Assert.AreEqual(AssignmentResult.CompanionUnavailable, s.Assign(who.Id, slot), "бранця на пост не поставиш");
            Assert.IsTrue(s.DayLog.Any(e => e.Key == "companion.captured"));

            var view = s.GetCaptivesView().Single();
            Assert.AreEqual("Holding", view.Band);
            Assert.AreEqual(view.BestPersuade >= view.TalkThreshold, view.CanTalk, "поріг видно до кліку");
        }

        [Test]
        public void Session_LongCaptivity_SignalsBands_AndEndsInDefection()
        {
            var s = Morning();
            var who = SomeCompanion(s);
            s.TakeCaptive(who.Id, "enemy.tuhar_boyar", 1, null, new[] { "enemy.tuhar_boyar" });

            var log = new List<GameEvent>();
            BotRunner.Drive(s, new HomebodyPolicy(), 25, log);

            Assert.IsTrue(log.Any(e => e.Key == "captivity.band.worn"), "годинник звучить у стрічці");
            Assert.IsTrue(log.Any(e => e.Key == "captivity.band.breaking"));
            Assert.IsTrue(log.Any(e => e.Key == "companion.defected"));
            var after = s.GetRosterView().Companions.Single(c => c.Id == who.Id);
            Assert.AreEqual(CompanionStatus.Antagonist, after.Status, "кинутий у полоні переходить на їхній бік (наявна зрада)");
            Assert.AreEqual(0, s.GetCaptivesView().Count, "зрадник — не бранець");
        }

        [Test]
        public void Session_Ransom_BringsHimHome_ForTheShownPrice()
        {
            var s = Morning();
            var who = SomeCompanion(s);
            s.TakeCaptive(who.Id, "enemy.horde_scout", 0, null, new[] { "enemy.horde_scout" });
            var view = s.GetCaptivesView().Single();
            int gold = s.GetEconomyView().Gold;
            Assume.That(view.CanAffordRansom, "стартового золота має вистачити на викуп рядового");

            Assert.IsTrue(s.RansomCaptive(who.Id));
            Assert.AreEqual(gold - view.RansomGold, s.GetEconomyView().Gold);
            Assert.AreNotEqual(CompanionStatus.Captive, s.GetRosterView().Companions.Single(c => c.Id == who.Id).Status);
            Assert.AreEqual(0, s.GetCaptivesView().Count);
            Assert.IsTrue(s.DayLog.Any(e => e.Key == "companion.rescued.ransom"));
        }

        [Test]
        public void Session_Raid_IsABloodyBattle_ThatFreesTheWholeGroupOnVictory()
        {
            var s = Morning();
            var who = SomeCompanion(s);
            s.TakeCaptive(who.Id, "enemy.horde_scout", 0, null, new[] { "enemy.horde_scout" });
            var view = s.GetCaptivesView().Single();
            Assert.IsTrue(view.CanRaid);
            CollectionAssert.Contains(view.RaidCandidateIds, GameSession.ProtagonistId);
            CollectionAssert.DoesNotContain(view.RaidCandidateIds, who.Id, "бранець у рейд не йде");

            // Склад обирає гравець (власник: «ЗВІСНО ГРАВЦЕМ»): чужий, бранець, завеликий загін — відмова.
            Assert.IsFalse(s.RaidCaptors(who.Id, new[] { who.Id }));
            Assert.IsFalse(s.RaidCaptors(who.Id, new string[0]));
            Assert.IsFalse(s.RaidCaptors(who.Id, view.RaidCandidateIds.Concat(new[] { "nobody" }).ToList()));
            Assert.AreEqual(SessionState.Morning, s.State);

            var chosen = view.RaidCandidateIds.Take(3).ToList();
            Assert.IsTrue(s.RaidCaptors(who.Id, chosen));
            CollectionAssert.AreEquivalent(chosen.Select(id => "u_" + id),
                s.GetBattleView().Units.Where(u => u.Side == "Player").Select(u => u.Id), "у бій іде рівно обраний склад");
            Assert.AreEqual(SessionState.Battle, s.State);
            Assert.AreEqual("FirstStrike", s.GetBattleView().Opening, "свідомо обраний кривавий шлях — загін першим (№14.1)");
            s.CombatAutoResolve();

            Assert.AreEqual(SessionState.Morning, s.State);
            Assert.AreEqual(0, s.GetCaptivesView().Count, "один розвідник проти загону — рейд вдався");
            Assert.IsTrue(s.DayLog.Any(e => e.Key == "companion.rescued.raid"));
        }

        [Test]
        public void Session_FailedRaid_TakesMore_ButNeverTheProtagonist()
        {
            var s = Morning();
            var who = SomeCompanion(s);
            var heavy = new[] { "enemy.burunda", "enemy.horde_vanguard", "enemy.horde_vanguard", "enemy.forest_bandit" };
            s.TakeCaptive(who.Id, "enemy.burunda", 2, null, heavy);
            var party = s.GetCaptivesView().Single().RaidCandidateIds.Take(3).ToList();
            int partySize = party.Count;

            s.RaidCaptors(who.Id, party);
            s.CombatAutoResolve();

            var captives = s.GetCaptivesView();
            Assert.IsFalse(captives.Any(c => c.CompanionId == GameSession.ProtagonistId), "«ні не може»");
            if (captives.Count > 1)
            {
                Assert.IsTrue(captives.All(c => c.CaptorEnemyId == "enemy.burunda"), "нові бранці — до того самого загону");
                Assert.LessOrEqual(captives.Count - 1, partySize / 2, "утікає щонайменше половина рейду");
            }
            else
                Assert.Inconclusive("рейд проти важкого загону несподівано вдався — перевірити баланс, тест не про це");
        }

        [Test]
        public void DungeonDefeat_TakesTheDownedHome_AsCaptives_AfterThePartyReturns()
        {
            var s = Morning();
            var party = new List<string> { GameSession.ProtagonistId };
            party.AddRange(s.GetRosterView().Companions
                .Where(c => c.Id != GameSession.ProtagonistId && (c.Status == CompanionStatus.Idle || c.Status == CompanionStatus.Assigned))
                .Select(c => c.Id).Take(2));
            Assume.That(party.Count, Is.EqualTo(3), "потрібен загін із трьох");
            s.DepartExpedition(Game.Core.Dungeons.DefaultDungeon.AbandonedCamp, Game.Core.Expeditions.ExpeditionApproach.Delve, party, 2);
            s.ResolveDungeonRoom(Game.Core.Loop.IncidentPath.Bloody);
            Assert.AreEqual(SessionState.Battle, s.State);

            // Загін не б'ється — ворог доводить бій до кінця.
            for (int guard = 0; guard < 2000 && s.State == SessionState.Battle; guard++)
            {
                if (s.GetBattleView().IsAiTurn) s.CombatAiStepOneAction();
                else s.CombatEndTurn();
            }
            Assert.AreEqual("Defeat", s.LastResolvedBattleView.Outcome);
            Assert.AreEqual(SessionState.Morning, s.State, "вайп — додому");

            var roster = s.GetRosterView().Companions;
            var living = party.Where(id => roster.Single(c => c.Id == id).Status != CompanionStatus.Dead).ToList();
            var captives = s.GetCaptivesView();
            Assert.IsFalse(captives.Any(c => c.CompanionId == GameSession.ProtagonistId), "«ні не може»");
            Assert.AreEqual(living.Count / 2, captives.Count, "утікає ⌈n/2⌉ живих, решта — у полоні");
            foreach (var c in captives)
                Assert.AreEqual(CompanionStatus.Captive, roster.Single(r => r.Id == c.CompanionId).Status,
                    "повернення загону не переписало статус бранця");
        }

        [Test]
        public void Session_Captivity_SurvivesAFreshRestore()
        {
            var s = Morning();
            var who = SomeCompanion(s);
            s.TakeCaptive(who.Id, "enemy.tuhar_boyar", 1, null, new[] { "enemy.tuhar_boyar", "enemy.horde_scout" });
            BotRunner.Drive(s, new HomebodyPolicy(), 3);
            var before = s.GetCaptivesView().Single();

            string blob = s.SaveState(0);
            var fresh = new GameSession();
            fresh.NewGame(new NewGameOptions { SkipCreation = true, HitRule = HitRuleKind.Threshold });
            fresh.RestoreFromBlob(blob);

            var after = fresh.GetCaptivesView().Single();
            Assert.AreEqual(before.CompanionId, after.CompanionId);
            Assert.AreEqual(before.Band, after.Band);
            CollectionAssert.AreEqual(before.RaidEnemyIds, after.RaidEnemyIds);
            Assert.AreEqual(CompanionStatus.Captive, fresh.GetRosterView().Companions.Single(c => c.Id == who.Id).Status);
        }
    }
}
