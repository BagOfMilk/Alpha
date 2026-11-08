using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Game.Core.Ring;
using Game.Core.Ring.Match;
using Game.Gameplay.Combat;
using Game.Gameplay.Ring.Models;
using Game.Gameplay.Ring.Stage;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Такт матчу (огляд 08.11.2026, «текст не випереджає тіло»): виноска, звук і рядок журналу — на мітці контакту кліпа,
    /// а не за фіксованою затримкою від початку події; утримання — на долонях рефері. Мітки — <c>ring_anims_events.txt</c>.
    /// </summary>
    public class RingBeatTests
    {
        private static string Root() { return Path.GetDirectoryName(Application.dataPath); }

        private static RingClipEvents Events()
        {
            string dir = Path.Combine(Root(), "Assets", "Art", "Animations");
            var e = RingClipEvents.Parse(File.ReadAllText(Path.Combine(dir, "ring_anims_events.txt")));
            e.Add(File.ReadAllText(Path.Combine(dir, "ring_anims_f_events.txt")));
            return e;
        }

        /// <summary>Довжини кліпів з маніфесту рингу: ім'я → с.</summary>
        private static Dictionary<string, float> Lengths()
        {
            var d = new Dictionary<string, float>();
            string dir = Path.Combine(Root(), "Assets", "Art", "Animations");
            foreach (var f in new[] { "ring_anims_manifest.txt", "ring_promo_manifest.txt", "ring_entrance_manifest.txt" })
                foreach (var raw in File.ReadAllLines(Path.Combine(dir, f)))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    var p = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (p.Length < 9) continue;
                    d[p[0]] = float.Parse(p[2], CultureInfo.InvariantCulture);
                }
            return d;
        }

        private static float Len(Dictionary<string, float> L, string clip)
        {
            float v;
            if (clip == null) return 0f;                   // хода — петля без кліпа такту
            if (L.TryGetValue(clip + "_A", out v)) return v;
            if (L.TryGetValue(clip, out v)) return v;
            return 0f;
        }

        /// <summary>Усі події, які може дати рушій (вид × дія × ступінь, з попередженням і без, «підвівся» і без).</summary>
        private static IEnumerable<MatchEvent> AllEvents()
        {
            var kinds = new[] { MatchEventKind.Action, MatchEventKind.Reverse, MatchEventKind.Kickout, MatchEventKind.Endure,
                MatchEventKind.Rescue, MatchEventKind.MatchEnd, MatchEventKind.Sell, MatchEventKind.TurnEnd };
            foreach (var k in kinds)
                foreach (MatchActionId a in Enum.GetValues(typeof(MatchActionId)))
                    foreach (var t in new[] { RollTier.Miss, RollTier.Partial, RollTier.Strong })
                        foreach (bool outside in new[] { false, true })
                            for (int fx = 0; fx < 3; fx++)
                            {
                                var ev = new MatchEvent { Kind = k, Action = a, Tier = t, HasRoll = true, Actor = MatchSide.Player, Outside = outside };
                                if (fx == 1) ev.Effects.Add(new MatchEffect(MatchEffectKind.Warning, MatchSide.Player, 1));
                                if (fx == 2) ev.Effects.Add(new MatchEffect(MatchEffectKind.Stand, MatchSide.Rival, 0));
                                yield return ev;
                            }
        }

        /// <summary>
        /// Події прийомів каталогу кар'єри (<c>career_moves.json</c>): ключ, дія й постановка (центр, канати, кут) — як їх дає рушій
        /// через <see cref="MoveHint"/>; кожен прийом у трьох ступенях, з «підвівся» і без.
        /// </summary>
        private static IEnumerable<MatchEvent> CatalogEvents()
        {
            foreach (var m in CareerKit.Content().Moves.All)
                foreach (var t in new[] { RollTier.Miss, RollTier.Partial, RollTier.Strong })
                    for (int fx = 0; fx < 2; fx++)
                    {
                        var ev = new MatchEvent { Kind = MatchEventKind.Action, Action = m.Action, Tier = t, HasRoll = true, Actor = MatchSide.Player,
                            MoveKey = m.Id, Staging = m.Staging };
                        if (fx == 1) ev.Effects.Add(new MatchEffect(MatchEffectKind.Stand, MatchSide.Rival, 0));
                        yield return ev;
                    }
        }

        private static string Describe(MatchEvent ev, List<StageStep> steps)
        {
            return ev.Kind + "/" + ev.Action + (ev.MoveKey != null ? "[" + ev.MoveKey + "@" + ev.Staging + "]" : "") + "/" + ev.Tier + ": "
                + string.Join(" + ", steps.Select(s => s.Kind + ":" + (RingBeats.StepClip(s) ?? s.Clip)));
        }

        [Test]
        public void Events_BothSetsParse_AndCarryTheMarksTheBeatNeeds()
        {
            var e = Events();
            Assert.Greater(e.Count, 100, "мітки подій прочитано");
            Assert.AreEqual(0.42f, e.Time("Forearm_Strike", "contact"), 1e-4f);
            Assert.AreEqual(1.05f, e.Time("Ref_Count_Pin_2", "count_1"), 1e-4f);
            Assert.AreEqual(2.0f, e.Time("Ref_Count_Pin_2", "count_2"), 1e-4f);
            Assert.AreEqual(2.2f, e.Time("Ref_Count_Pin_2", "kickout"), 1e-4f);
            Assert.AreEqual(2.95f, e.Time("Ref_Count_Pin_3", "count_3"), 1e-4f);
            Assert.Less(e.Time("Forearm_Strike", "no_such_event"), 0f);
        }

        /// <summary>
        /// Охоронець (1): кожен кліп таблиці «подія → кліпи», що має мітку contact, бере такт саме з мітки — і такт стоїть на
        /// першому кроці з міткою удару; мить такту лежить усередині кліпа; кожен кліп таблиці є в маніфесті.
        /// </summary>
        [Test]
        public void EveryTableClip_WithAContactMark_TakesTheBeatFromIt()
        {
            var ev = Events();
            var L = Lengths();
            int contactPlans = 0;
            foreach (var e in AllEvents().Concat(CatalogEvents()))
            {
                var steps = RingClipTable.Plan(e);
                var plan = RingBeats.For(steps, ev, n => Len(L, n));
                string what = Describe(e, steps);
                if (steps.Count == 0) { Assert.AreEqual(-1, plan.Step, what); continue; }
                Assert.That(plan.Step, Is.InRange(0, steps.Count - 1), what);
                foreach (var st in steps)
                {
                    string c = RingBeats.StepClip(st);
                    if (c != null) Assert.Greater(Len(L, c), 0f, "кліпа немає в маніфесті: " + c + " — " + what);
                    foreach (var any in st.AllClips())
                        Assert.IsTrue(L.ContainsKey(any), "кліпа кроку немає в маніфесті: " + any + " — " + what);
                }
                if (plan.IsCount) continue;
                int firstImpact = -1;
                bool anyMain = steps.Any(x => !x.Aside);
                for (int i = 0; i < steps.Count && firstImpact < 0; i++)
                {
                    float t; BeatSource s;
                    if (anyMain && steps[i].Aside) continue;           // крок-наслідок такту не несе
                    if (RingBeats.ImpactMark(RingBeats.StepClip(steps[i]), ev, out t, out s)) firstImpact = i;
                }
                if (anyMain) Assert.IsFalse(steps[plan.Step].Aside, "такт не на кроці-наслідку: " + what);
                if (firstImpact >= 0) Assert.AreEqual(firstImpact, plan.Step, "такт — на першому кроці з міткою удару: " + what);
                string beatClip = RingBeats.StepClip(steps[plan.Step]);
                float contact = ev.Time(RingBeats.MarkBase(beatClip), "contact");
                if (contact >= 0f)
                {
                    contactPlans++;
                    Assert.AreEqual(BeatSource.Contact, plan.Source, what);
                    Assert.AreEqual(contact, plan.ClipTime, 1e-5f, "такт — рівно на мітці contact: " + what);
                }
                float len = Len(L, beatClip);
                if (len > 0f) Assert.Less(plan.ClipTime, len, "такт усередині кліпа: " + what);
                Assert.GreaterOrEqual(plan.ClipTime, 0f, what);
            }
            Assert.Greater(contactPlans, 50, "події з міткою contact перевірено");
        }

        /// <summary>Кліпи таблиці без мітки беруть виміряну мить (а не запасне правило), якщо вона є; правило — лише для решти.</summary>
        [Test]
        public void ClipsWithoutAMark_UseTheMeasuredMoment_OrTheWrittenRule()
        {
            var ev = Events();
            var L = Lengths();
            foreach (var e in AllEvents())
            {
                var steps = RingClipTable.Plan(e);
                if (steps.Count == 0) continue;
                var plan = RingBeats.For(steps, ev, n => Len(L, n));
                if (plan.Source != BeatSource.Rule) continue;
                foreach (var st in steps)
                {
                    float m;
                    if (st.Aside) continue;
                    string c = RingBeats.StepClip(st);
                    Assert.IsFalse(c != null && RingBeats.Measured.TryGetValue(c, out m),
                        "є виміряна мить, а взято правило: " + Describe(e, steps));
                }
                float len = Len(L, plan.Clip);
                if (steps[plan.Step].Once && len > 0f) len = Math.Min(len, 1.7f);
                Assert.AreEqual(RingBeats.Rule(len), plan.ClipTime, 1e-5f, Describe(e, steps));
            }
            foreach (var kv in RingBeats.Measured)
            {
                float len = Len(L, kv.Key);
                Assert.Greater(len, 0f, "виміряний кліп є в маніфесті: " + kv.Key);
                Assert.Less(kv.Value, len, "виміряна мить усередині кліпа: " + kv.Key);
            }
        }

        /// <summary>
        /// Рішення власника 08.11.2026 (TRPG_DESIGN §4.4, §4.12): прийом із канатів чи кута — склейкою. План 1 — нападник уже на
        /// канаті чи куті (крок <see cref="StageStepKind.Cut"/>, рівно один і першим), план 2 — приземлення пари в центрі. Склейка
        /// такту не несе: такт — на контакті приземлення. Прийом у центрі склейки не має зовсім.
        /// </summary>
        [Test]
        public void MovesFromRopesOrCorner_CutFirst_BeatOnTheLanding()
        {
            var ev = Events();
            var L = Lengths();
            int cuts = 0;
            foreach (var e in CatalogEvents())
            {
                var steps = RingClipTable.Plan(e);
                string what = Describe(e, steps);
                int n = steps.Count(s => s.Kind == StageStepKind.Cut);
                if (e.Staging == MoveStaging.Center && e.Action != MatchActionId.TopRope)
                {
                    Assert.AreEqual(0, n, "прийом у центрі — без склейки: " + what);
                    continue;
                }
                cuts++;
                Assert.AreEqual(1, n, "рівно одна склейка: " + what);
                Assert.AreEqual(StageStepKind.Cut, steps[0].Kind, "склейка — першим планом: " + what);
                Assert.AreEqual(e.Actor, steps[0].Actor, "на канаті — нападник: " + what);
                Assert.AreNotEqual(MoveStaging.Center, steps[0].Perch, what);
                if (e.Staging != MoveStaging.Center) Assert.AreEqual(e.Staging, steps[0].Perch, "місце плану 1 — з постановки прийому: " + what);
                Assert.IsNull(RingBeats.StepClip(steps[0]), "склейка такту не несе: " + what);
                Assert.Greater(steps.Count, 1, "після склейки — приземлення в центрі: " + what);
                var plan = RingBeats.For(steps, ev, n2 => Len(L, n2));
                Assert.GreaterOrEqual(plan.Step, 1, "такт — на приземленні, не на склейці: " + what);
            }
            Assert.Greater(cuts, 3, "у каталозі є прийоми з канатів і кута");
        }

        /// <summary>
        /// Бій за рингом склейкою (власник 08.11.2026): вихід — дія в центрі, далі склейка місця (пара вже за рингом); повернення —
        /// лише склейка (пара знову на рингу). Склейка такту не несе; прийоми за рингом — без склейок «канати/кут».
        /// </summary>
        [Test]
        public void LeaveAndReturn_CutThePlace_WithoutRunningOrClimbing()
        {
            var ev = Events();
            var L = Lengths();
            foreach (var t in new[] { RollTier.Partial, RollTier.Strong })
            {
                var e = new MatchEvent { Kind = MatchEventKind.Action, Action = MatchActionId.LeaveRing, Tier = t, HasRoll = true, Actor = MatchSide.Player };
                var steps = RingClipTable.Plan(e);
                Assert.AreEqual(StageStepKind.Place, steps[steps.Count - 1].Kind, Describe(e, steps));
                Assert.IsTrue(steps[steps.Count - 1].Outside, "після склейки — за рингом");
                Assert.AreEqual(1, steps.Count(s => s.Kind == StageStepKind.Place));
                var plan = RingBeats.For(steps, ev, n => Len(L, n));
                Assert.AreNotEqual(StageStepKind.Place, steps[plan.Step].Kind, "такт — на поштовху, не на склейці");
            }
            var miss = RingClipTable.Plan(new MatchEvent { Kind = MatchEventKind.Action, Action = MatchActionId.LeaveRing, Tier = RollTier.Miss, HasRoll = true });
            Assert.IsFalse(miss.Any(s => s.Kind == StageStepKind.Place), "не викинув — лишаються на рингу");
            var back = RingClipTable.Plan(new MatchEvent { Kind = MatchEventKind.Action, Action = MatchActionId.ReturnToRing, Outside = true });
            Assert.AreEqual(1, back.Count);
            Assert.AreEqual(StageStepKind.Place, back[0].Kind);
            Assert.IsFalse(back[0].Outside, "склейка: знову на рингу");
            Assert.Greater(back[0].Hold, 0f);
            foreach (var e in CatalogEvents())
            {
                e.Outside = true;
                Assert.IsFalse(RingClipTable.Plan(e).Any(s => s.Kind == StageStepKind.Cut), "за рингом склейок канатів і кута немає: " + e.MoveKey);
            }
        }

        /// <summary>
        /// Охоронець рішення 08.11.2026 «усе в центрі рингу»: жоден крок режисури не грає ходу, бігу чи кидка в канати — ні для
        /// подій рушія, ні для прийомів каталогу. Кожен прийом каталогу має кліпи в таблиці.
        /// </summary>
        [Test]
        public void NoStep_WalksOrRunsAcrossTheRing_AndEveryCatalogMoveHasClips()
        {
            var L = Lengths();
            foreach (var e in AllEvents().Concat(CatalogEvents()))
            {
                var steps = RingClipTable.Plan(e);
                foreach (var st in steps)
                    foreach (var c in st.AllClips())
                    {
                        Assert.IsFalse(c.StartsWith("Walk_") || c.StartsWith("Run_") || c.StartsWith("Whip_"),
                            "хода чи біг по рингу: " + c + " — " + Describe(e, steps));
                        Assert.IsTrue(L.ContainsKey(c), "кліпа кроку немає в маніфесті: " + c + " — " + Describe(e, steps));
                    }
            }
            foreach (var m in CareerKit.Content().Moves.All)
                Assert.IsTrue(RingClipTable.KnowsMove(m.Id), "прийом каталогу без кліпів у таблиці: " + m.Id);
        }

        /// <summary>Охоронець (2а): кікаут відкривається не раніше «два» рефері; «три» — на третій долоні.</summary>
        [Test]
        public void Kickout_IsRevealedNoEarlierThanTheRefereesTwo()
        {
            var ev = Events();
            var L = Lengths();
            foreach (var t in new[] { RollTier.Miss, RollTier.Partial, RollTier.Strong })
            {
                var e = new MatchEvent { Kind = MatchEventKind.Kickout, Tier = t, HasRoll = true, Actor = MatchSide.Rival };
                var steps = RingClipTable.Plan(e);
                var plan = RingBeats.For(steps, ev, n => Len(L, n));
                Assert.IsTrue(plan.IsCount, "утримання — відлік рефері: " + t);
                Assert.Greater(plan.Count1, 0f, t.ToString());
                Assert.Greater(plan.Count2, plan.Count1, t.ToString());
                Assert.GreaterOrEqual(plan.Final, plan.Count2, "розв'язка (і рядок журналу) — не раніше «два»: " + t);
                if (t == RollTier.Miss) Assert.AreEqual(ev.Time("Ref_Count_Pin_3", "count_3"), plan.Final, 1e-5f, "«три» — на третій долоні");
                Assert.Less(plan.Final, Len(L, plan.Clip), t.ToString());
            }
        }

        /// <summary>Охоронець (2б): журнал не показує подію, такт якої ще не настав (межа — номер показаної події).</summary>
        [Test]
        public void Log_DoesNotShowAnEventWhoseBeatHasNotCome()
        {
            var setup = new MatchSetup
            {
                Player = new WrestlerStats("p", "p"), Rival = new WrestlerStats("r", "r"), StartHype = 4,
                PlayerRole = RingPath.Face, RivalRole = RingPath.Heel
            };
            var engine = new MatchEngine(setup, new SeededDiceRoller(11));
            var m = new MatchScreenModel(engine, RingText.FromJson("{}"));
            int steps = 0;
            while (!m.IsOver && ++steps < MatchAutoplay.MaxSteps)
            {
                switch (m.Input)
                {
                    case MatchInput.Ai: m.StepAi(); break;
                    case MatchInput.PlayerAction: m.Perform(MatchAi.ChooseAction(engine)); break;
                    case MatchInput.PlayerReaction: m.React(MatchAi.ChooseReaction(engine)); break;
                    case MatchInput.PlayerBoost: m.Commit(MatchAi.ChooseBoost(engine)); break;
                }
            }
            var log = engine.State.Log;
            Assert.Greater(log.Count, 5, "матч дав події");
            Assert.AreEqual(m.LogTexts(), m.LogTexts(int.MaxValue), "без межі — усе, як раніше");
            for (int shown = -1; shown <= log[log.Count - 1].Seq; shown++)
            {
                var lines = m.LogTexts(shown);
                var visible = log.Where(e => e.Seq <= shown).ToList();
                if (visible.Count == 0) { Assert.IsEmpty(lines, "до першого такту журнал порожній"); continue; }
                Assert.AreEqual(m.EventText(visible[visible.Count - 1]), lines[lines.Count - 1], "останній рядок — остання показана подія (межа " + shown + ")");
                foreach (var e in log.Where(x => x.Seq > shown))
                    Assert.IsFalse(lines.Contains(m.EventText(e)) && !visible.Any(v => m.EventText(v) == m.EventText(e)),
                        "рядок події " + e.Seq + " видно до її такту (межа " + shown + ")");
            }
        }
    }
}
