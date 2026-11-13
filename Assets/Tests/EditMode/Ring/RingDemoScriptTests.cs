using System;
using System.Collections.Generic;
using System.Linq;
using Game.Core.Ring;
using Game.Core.Ring.Career;
using Game.Core.Ring.Match;
using Game.Gameplay.Ring.Stage;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Сценарії демо під нове ядро (Ф2, крок 9; ZONES.md «Чого бракує»): кожен грається справжнім рушієм до кінця матчу, кінцівки —
    /// ті, що дає ядро, кожен прийом каталогу видно на трьох ступенях, у планах кліпів немає бігу, прийом із канатів чи кута — зі
    /// склейкою. Підстановки кліпів — виводом (храповик таблиці — RingBeatStandInTests).
    /// </summary>
    public sealed class RingDemoScriptTests
    {
        private static MoveCatalog Moves() { return CareerKit.Content().Moves; }

        private static IEnumerable<MatchEvent> Shown(DemoMatch d) { return d.Events.Skip(d.StartCount); }

        /// <summary>
        /// Храповик підстановок кліпів у планах усіх сценаріїв (09.11.2026, 204ba0c: 101 разом, 16 різних). Лише спадає: коли
        /// кліпи «Кліпів» (P1–P3) зайдуть у таблицю — знизити до нового числа; критерій М1 — 0.
        /// Ф2 кроки 5 і 7 (09.11.2026): різних 16 → 14 (кліпи P1–P2 прибрали замок ноги-гамерлок і три тарани замість стрибків,
        /// додали дві чесні: план на куті знизу замість Top_Rope_Perch_Loop і невдачу больового на лікоть). Разом 101 → 103 —
        /// не нові заглушки: «Затягнути в кут» і «Таран плечем» тепер зі склейкою, а підготовка сценаріїв бере лише удари в центрі
        /// (<c>RingDemoScript.All</c>), тож у черзі частіше випадає дропкік-підстановка (6 → 13).
        /// 12.11.2026 — новий зміст без кліпів, не нові заглушки старого: ґіміки 5–8 «Кар'єри» (a3fd3fd5) дали 16 прийомів, і демо
        /// грає кожен (сценаріїв 59); 14 з них — підстановкою (CLIP_NEEDS.md §11). Разом 103 → 181, різних 14 → 28 (ПМ).
        /// </summary>
        private const int StandInCeiling = 181;
        private const int StandInKindsCeiling = 28;

        /// <summary>
        /// Прийомів у каталозі гри (`Ring/career_moves`) — не менше; випадкова втрата прийому з JSON — червоний тест.
        /// 12.11.2026: 24 → 40 (ґіміки 5–8).
        /// </summary>
        private const int CatalogMovesFloor = 40;

        [Test]
        public void EverySpec_PlaysToTheEnd_ThroughTheRealEngine()
        {
            var moves = Moves();
            var specs = RingDemoScript.Specs(moves);
            Assert.GreaterOrEqual(moves.All.Count, CatalogMovesFloor, "прийоми каталогу гри зникли");
            Assert.AreEqual(11 + moves.All.Count + moves.All.Count(m => m.Action == MatchActionId.Finisher), specs.Count);
            int standIns = 0;
            foreach (var spec in specs)
            {
                var d = RingDemoScript.Play(spec, moves);
                Assert.IsTrue(d.Engine.IsOver, spec + ": матч не скінчився");
                Assert.AreEqual(1, d.StartCount, spec + ": старт рушія — один обмін");
                Assert.AreEqual(MatchEventKind.Exchange, d.Events[0].Kind);
                int si = Shown(d).Sum(ev => RingClipTable.StandIns(RingClipTable.Plan(ev)));
                standIns += si;
                TestContext.WriteLine(spec + ": кроків " + d.Steps.Count + ", подій " + d.Events.Count + ", кінець " + d.Engine.Result.Kind
                                      + " / " + d.Engine.Result.Winner + ", підстановок кліпів " + si);
            }
            TestContext.WriteLine("сценаріїв " + specs.Count + ", підстановок кліпів разом " + standIns + " (критерій М1 — 0, після кліпів «Кліпів»)");
            Assert.LessOrEqual(standIns, StandInCeiling, "підстановок кліпів у демо побільшало — храповик лише спадає");
        }

        [TestCase("pin 1", MatchEndKind.Pin, MatchWinner.Player)]
        [TestCase("pin 2", MatchEndKind.Pin, MatchWinner.Player)]
        [TestCase("pin 3", MatchEndKind.Pin, MatchWinner.Player)]
        [TestCase("outside", MatchEndKind.Pin, MatchWinner.Player)]
        [TestCase("warn", MatchEndKind.Pin, MatchWinner.Player)]
        [TestCase("sub", MatchEndKind.Submission, MatchWinner.Player)]
        [TestCase("end countout", MatchEndKind.CountOut, MatchWinner.Player)]
        [TestCase("end double", MatchEndKind.DoubleCountOut, MatchWinner.Draw)]
        [TestCase("end dq", MatchEndKind.Disqualification, MatchWinner.Player)]
        [TestCase("nodq", MatchEndKind.Pin, MatchWinner.Player)]
        [TestCase("exchange", MatchEndKind.Pin, MatchWinner.Player)]
        public void Endings_AreTheEnginesOwn(string spec, MatchEndKind kind, MatchWinner winner)
        {
            var d = RingDemoScript.Play(spec, Moves());
            Assert.AreEqual(kind, d.Engine.Result.Kind, spec);
            Assert.AreEqual(winner, d.Engine.Result.Winner, spec);
            Assert.AreEqual(MatchEventKind.MatchEnd, d.Events[d.Events.Count - 1].Kind, spec + ": журнал закінчується кінцем матчу");
        }

        [Test]
        public void CountOut_LoserLiesOutside_DoubleCountOut_BothStillOutside()
        {
            var c = RingDemoScript.Play("end countout", Moves()).Engine.State;
            Assert.IsTrue(c.Outside, "відлік — пара за рингом");
            Assert.AreEqual(Posture.Down, c.Rival.Posture, "той, хто програв, лежить");
            Assert.AreEqual(Posture.Standing, c.Player.Posture);
            var dbl = RingDemoScript.Play("end double", Moves()).Engine.State;
            Assert.IsTrue(dbl.Outside);
            Assert.GreaterOrEqual(dbl.OutsideCount, MatchRules.OutsideCountLimit);
        }

        [TestCase("pin 1", RollTier.Strong)]
        [TestCase("pin 2", RollTier.Partial)]
        [TestCase("pin 3", RollTier.Miss)]
        public void Pin_KicksOutAtOneTwoOrThree(string spec, RollTier first)
        {
            var d = RingDemoScript.Play(spec, Moves());
            var kicks = d.Events.Where(ev => ev.Kind == MatchEventKind.Kickout).ToList();
            Assert.AreEqual(first, kicks[0].Tier, spec + ": перше утримання");
            Assert.AreEqual(RollTier.Miss, kicks[kicks.Count - 1].Tier, spec + ": кінець — «три»");
            Assert.AreEqual(first == RollTier.Miss ? 1 : 2, kicks.Count);
        }

        [Test]
        public void Sub_HoldsOn_Escapes_ThenTapsOut()
        {
            var d = RingDemoScript.Play("sub", Moves());
            var endure = d.Events.Where(ev => ev.Kind == MatchEventKind.Endure).Select(ev => ev.Tier).ToArray();
            CollectionAssert.AreEqual(new[] { RollTier.Partial, RollTier.Strong, RollTier.Miss }, endure);
        }

        [Test]
        public void EveryCatalogMove_IsShownOnAllThreeTiers_WithItsKey()
        {
            var moves = Moves();
            foreach (var m in moves.All)
            {
                var d = RingDemoScript.Play("move " + m.Id, moves);
                // саме кроки-показ, а не підготовка (вона теж може грати цей прийом на 10+) — рецензія 204ba0c
                Assert.AreEqual(3, d.Featured.Count, "move " + m.Id + ": три кидки-показ");
                foreach (int i in d.Featured)
                {
                    var ev = d.Events[i];
                    Assert.AreEqual(MatchEventKind.Action, ev.Kind, "move " + m.Id);
                    Assert.AreEqual(m.Id, ev.MoveKey, "move " + m.Id);
                    Assert.AreEqual(MatchSide.Player, ev.Actor, "move " + m.Id);
                }
                CollectionAssert.AreEqual(new[] { RollTier.Strong, RollTier.Partial, RollTier.Miss }, d.Featured.Select(i => d.Events[i].Tier).ToArray(),
                    "move " + m.Id + ": показ — 10+, 7–9, провал по черзі");
                Assert.IsTrue(d.Events.Where(ev => ev.MoveKey == m.Id).All(ev => ev.Staging == m.Staging), "move " + m.Id + ": постановка прийому в подіях");
                Assert.AreEqual(MatchEndKind.Pin, d.Engine.Result.Kind, "move " + m.Id + ": кінцівка — утримання");
                Assert.AreEqual(MatchWinner.Player, d.Engine.Result.Winner, "move " + m.Id);
            }
        }

        [Test]
        public void FinisherSpec_EndsWithThatFinisher_AndAPin()
        {
            var moves = Moves();
            var fins = moves.All.Where(m => m.Action == MatchActionId.Finisher).ToList();
            Assert.IsNotEmpty(fins);
            foreach (var m in fins)
            {
                var d = RingDemoScript.Play("finisher " + m.Id, moves);
                var last = d.Events.Last(ev => ev.Kind == MatchEventKind.Action && ev.Action == MatchActionId.Finisher);
                Assert.AreEqual(m.Id, last.MoveKey);
                Assert.AreEqual(RollTier.Strong, last.Tier);
                Assert.AreEqual(MatchEndKind.Pin, d.Engine.Result.Kind);
                Assert.AreEqual(MatchWinner.Player, d.Engine.Result.Winner);
            }
        }

        [Test]
        public void Outside_GoesOutAndBack_ItemsOnlyWithoutDq()
        {
            var moves = Moves();
            var o = RingDemoScript.Play("outside", moves);
            var places = o.Events.SelectMany(ev => ev.Effects).Where(fx => fx.Kind == MatchEffectKind.Place).Select(fx => fx.Amount).ToArray();
            CollectionAssert.AreEqual(new[] { 1, 0, 1, 0 }, places, "двічі за ринг і двічі назад — склейками");
            Assert.IsFalse(o.Events.Any(ev => ev.Action == MatchActionId.Weapon), "предмети — лише «без DQ»");
            var leave = o.Events.Where(ev => ev.Kind == MatchEventKind.Action && ev.Action == MatchActionId.LeaveRing).Select(ev => ev.Tier).ToArray();
            CollectionAssert.AreEqual(new[] { RollTier.Miss, RollTier.Partial, RollTier.Strong }, leave);

            var n = RingDemoScript.Play("nodq", moves);
            Assert.AreEqual(MatchStipulations.NoDq, n.Engine.Setup.Stipulations);
            var weapon = n.Events.Where(ev => ev.Kind == MatchEventKind.Action && ev.Action == MatchActionId.Weapon).Select(ev => ev.Tier).ToArray();
            CollectionAssert.AreEqual(new[] { RollTier.Strong, RollTier.Partial, RollTier.Miss }, weapon);
            Assert.IsFalse(n.Events.SelectMany(ev => ev.Effects).Any(fx => fx.Kind == MatchEffectKind.Count), "без DQ відліку немає");
        }

        [Test]
        public void Warn_OneWarning_Dq_Three()
        {
            Func<string, int> warnings = spec => RingDemoScript.Play(spec, Moves()).Events.SelectMany(ev => ev.Effects)
                .Count(fx => fx.Kind == MatchEffectKind.Warning && fx.Who == MatchSide.Rival);
            Assert.AreEqual(1, warnings("warn"));
            Assert.AreEqual(MatchRules.WarningsToDq, warnings("end dq"));
        }

        [Test]
        public void Exchange_RivalStarts_Reversal79_ReExchanges_Reversal10_FloorsAttacker()
        {
            var d = RingDemoScript.Play("exchange", Moves());
            Assert.AreEqual(MatchSide.Rival, d.Events[0].Actor, "обмін на старті бере суперник");
            var log = d.Events.ToList();
            int rev = log.FindIndex(ev => ev.Kind == MatchEventKind.Reverse && ev.Tier == RollTier.Partial);
            Assert.GreaterOrEqual(rev, 0, "реверс 7–9");
            var again = log.Skip(rev + 1).First(ev => ev.Kind == MatchEventKind.Exchange);
            Assert.AreEqual(MatchSide.Player, again.Actor, "після реверсу 7–9 — знову Обмін, бере гравець");
            Assert.IsTrue(log.Any(ev => ev.Kind == MatchEventKind.Reverse && ev.Tier == RollTier.Strong), "реверс 10+");
        }

        [Test]
        public void Plans_HaveNoRunning_AndStagedMovesCut()
        {
            var moves = Moves();
            var notes = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var spec in RingDemoScript.Specs(moves))
            {
                var d = RingDemoScript.Play(spec, moves);
                foreach (var ev in Shown(d))
                {
                    var plan = RingClipTable.Plan(ev);
                    for (int i = 0; i < plan.Count; i++)
                    {
                        var st = plan[i];
                        foreach (var clip in st.AllClips())
                            Assert.IsFalse(RingBeatTests.RunsAcross(plan, i, clip),
                                spec + ": " + ev.Kind + " " + ev.Action + " грає біг «" + clip + "» — бігу по рингу немає (08.11.2026)");
                        if (st.IsStandIn) { int n; notes.TryGetValue(st.Note, out n); notes[st.Note] = n + 1; }
                    }
                    bool staged = ev.Kind == MatchEventKind.Action && ev.Staging != MoveStaging.Center && !ev.Outside
                                  && ev.Action != MatchActionId.Pin && ev.Action != MatchActionId.GetUp;
                    if (staged)
                        Assert.IsTrue(plan.Any(st => st.Kind == StageStepKind.Cut),
                            spec + ": " + ev.Action + " «" + ev.MoveKey + "» з постановкою " + ev.Staging + " — без склейки");
                }
            }
            foreach (var kv in notes) TestContext.WriteLine("— " + kv.Key + " × " + kv.Value);
            Assert.LessOrEqual(notes.Count, StandInKindsCeiling, "різних підстановок кліпів побільшало — храповик лише спадає");
        }

        /// <summary>
        /// Модифікатор, з яким сценарій назвав суму (читачі ядра для показу шансів), — той самий, що рушій записав у подію кидка.
        /// Похибка ±1 у <see cref="MatchEngine.KickoutModifier"/> / <see cref="MatchEngine.EndureModifier"/> / Describe ступеня
        /// не змінює, тож лише ця звірка її ловить (рецензія 204ba0c).
        /// </summary>
        [Test]
        public void RollMods_AreTheModifiersTheEngineRolledWith()
        {
            var moves = Moves();
            var kinds = new[]
            {
                new KeyValuePair<string, Func<MatchEvent, bool>>("m:kick:", ev => ev.Kind == MatchEventKind.Kickout),
                new KeyValuePair<string, Func<MatchEvent, bool>>("m:endure:", ev => ev.Kind == MatchEventKind.Endure),
                new KeyValuePair<string, Func<MatchEvent, bool>>("m:rev:", ev => ev.Kind == MatchEventKind.Reverse),
                new KeyValuePair<string, Func<MatchEvent, bool>>("m:act:", ev => ev.Kind == MatchEventKind.Action && ev.HasRoll),
            };
            int checkedRolls = 0;
            foreach (var spec in RingDemoScript.Specs(moves))
            {
                var d = RingDemoScript.Play(spec, moves);
                foreach (var k in kinds)
                {
                    var mods = d.RollMods.Where(x => x.Key.StartsWith(k.Key, StringComparison.Ordinal)).Select(x => x.Value).ToArray();
                    var evs = d.Events.Where(k.Value).Select(ev => ev.Modifier).ToArray();
                    CollectionAssert.AreEqual(evs, mods, spec + ": потік " + k.Key + " — модифікатори сценарію й рушія");
                    checkedRolls += mods.Length;
                }
            }
            Assert.Greater(checkedRolls, 0);
            TestContext.WriteLine("звірено кидків: " + checkedRolls);
        }

        [Test]
        public void SameSpec_SameLog()
        {
            var moves = Moves();
            Func<DemoMatch, string> sig = d => string.Join(";", d.Events.Select(ev => ev.Kind + "/" + ev.Actor + "/" + ev.Action + "/" + ev.Tier + "/" + ev.Total));
            foreach (var spec in new[] { "exchange", "sub", "outside", "move " + moves.All[0].Id })
                Assert.AreEqual(sig(RingDemoScript.Play(spec, moves)), sig(RingDemoScript.Play(spec, moves)), spec);
        }

        [Test]
        public void Specs_AllExpands_UnknownThrows()
        {
            var moves = Moves();
            CollectionAssert.AreEqual(RingDemoScript.Specs(moves), RingDemoScript.Expand("all", moves));
            CollectionAssert.AreEqual(new[] { "pin 2" }, RingDemoScript.Expand("pin 2", moves));
            Assert.Throws<ArgumentException>(() => RingDemoScript.Play("all", moves));
            Assert.Throws<ArgumentException>(() => RingDemoScript.Play("dance", moves));
            Assert.Throws<ArgumentException>(() => RingDemoScript.Play("move nope", moves));
            Assert.Throws<ArgumentException>(() => RingDemoScript.Play("pin 4", moves));
            Assert.Throws<ArgumentException>(() => RingDemoScript.Play("finisher " + moves.All.First(m => m.Action == MatchActionId.Strike).Id, moves));
        }

        [Test]
        public void DemoDice_GivesTheNamedTotal_AsTwoDice()
        {
            for (int t = 2; t <= 12; t++)
            {
                int want = t;
                var dice = new DemoDice(s => want);
                Assert.AreEqual(t, TwoD6.Roll(dice, "m:act:x"), "сума " + t);
                Assert.AreEqual(1, dice.Rolls);
            }
            var clamp = new DemoDice(s => 40);
            Assert.AreEqual(12, TwoD6.Roll(clamp, "m:act:x"));
        }
    }
}
