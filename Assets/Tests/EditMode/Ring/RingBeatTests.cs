using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Game.Core.Ring;
using Game.Core.Ring.Career;
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
        /// Рішення власника 08.11.2026 (TRPG_DESIGN §4.4, §4.12): прийом із канатів чи кута — склейкою, рівно однією, з місцем
        /// постановки прийому. Перед склейкою — хіба що кидок у центрі, що обривається на <c>release</c> (жертва ще не побігла).
        /// На канаті чи в куті — нападник; кинутий — жертва, а на провалі (реверс) — нападник. Склейка несе такт лише тоді, коли
        /// удар об канат чи кут і є ударом події (<see cref="StageStep.Impact"/>, на мітці початку склейки); інакше такт — на
        /// дії в центрі після неї. Прийом у центрі склейки не має зовсім.
        /// </summary>
        [Test]
        public void MovesFromRopesOrCorner_CutOnceAtTheirPlace_BeatOnTheHit()
        {
            var ev = Events();
            var L = Lengths();
            int cuts = 0, impacts = 0;
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
                int ci = steps.FindIndex(s => s.Kind == StageStepKind.Cut);
                var cut = steps[ci];
                for (int i = 0; i < ci; i++)
                {
                    Assert.AreEqual(StageStepKind.Pair, steps[i].Kind, "до склейки — лише кидок пари: " + what);
                    Assert.AreEqual("release", steps[i].StopAt, "кидок до склейки обривається на release: " + what);
                    Assert.Greater(ev.Time(steps[i].Clip, "release"), 0f, "у кидка є мітка release: " + what);
                }
                MatchSide onRope = ci == 0 || e.Tier == RollTier.Miss ? e.Actor : RingClipTable.Opp(e.Actor);
                Assert.AreEqual(onRope, cut.Actor, "на канаті чи в куті — " + onRope + ": " + what);
                Assert.AreNotEqual(MoveStaging.Center, cut.Perch, what);
                if (e.Staging != MoveStaging.Center) Assert.AreEqual(e.Staging, cut.Perch, "місце склейки — з постановки прийому: " + what);
                float from = cut.From != null ? RingBeats.MarkTime(ev, cut.Clip, cut.From) : 0f;
                Assert.GreaterOrEqual(from, 0f, "мітка початку склейки є в кліпі: " + what);
                if (cut.To != null) Assert.Greater(RingBeats.MarkTime(ev, cut.Clip, cut.To), from, "склейка кінчається після початку: " + what);
                Assert.Greater(steps.Count, ci + 1, "після склейки — дія в центрі: " + what);
                var plan = RingBeats.For(steps, ev, n2 => Len(L, n2));
                if (cut.Impact)
                {
                    impacts++;
                    Assert.AreEqual(ci, plan.Step, "удар об канат чи кут — такт склейки: " + what);
                    Assert.AreEqual(from, plan.ClipTime, 1e-5f, "такт — на мітці початку склейки: " + what);
                }
                else
                {
                    Assert.IsNull(RingBeats.StepClip(cut), "склейка без удару такту не несе: " + what);
                    Assert.Greater(plan.Step, ci, "такт — на дії після склейки: " + what);
                }
            }
            Assert.Greater(cuts, 10, "у каталозі є прийоми з канатів і кута");
            Assert.Greater(impacts, 2, "кидок у кут і в канати 7–9 — удар склейкою");
        }

        /// <summary>
        /// Кліп кроку — хода чи біг по рингу (рішення 08.11.2026). Кидок у канати чи кут (<c>Whip_*</c>) — лише до мітки
        /// <c>release</c> з подальшою склейкою (жертва вже на канаті чи в куті) або половиною самої склейки «в кут» від
        /// <c>corner_hit</c>: бігу через ринг на екрані немає.
        /// </summary>
        internal static bool RunsAcross(IList<StageStep> plan, int i, string clip)
        {
            if (clip.StartsWith("Walk_") || clip.StartsWith("Run_")) return true;
            if (!clip.StartsWith("Whip_")) return false;
            var st = plan[i];
            if (st.Kind == StageStepKind.Pair && st.StopAt == "release" && i + 1 < plan.Count && plan[i + 1].Kind == StageStepKind.Cut) return false;
            if (st.Kind == StageStepKind.Cut && st.From == "corner_hit") return false;
            return true;
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
        /// Охоронець рішення 08.11.2026 «усе в центрі рингу»: жоден крок режисури не грає ходу чи бігу через ринг — ні для подій
        /// рушія, ні для прийомів каталогу (кидок у канати чи кут — лише до склейки, <see cref="RunsAcross"/>). Кожен прийом
        /// каталогу має кліпи в таблиці.
        /// </summary>
        [Test]
        public void NoStep_WalksOrRunsAcrossTheRing_AndEveryCatalogMoveHasClips()
        {
            var L = Lengths();
            int whips = 0;
            foreach (var c0 in Contexts())
                foreach (var e in AllEvents().Concat(CatalogEvents()))
                {
                    var steps = RingClipTable.Plan(e, c0);
                    for (int i = 0; i < steps.Count; i++)
                        foreach (var c in steps[i].AllClips())
                        {
                            Assert.IsFalse(RunsAcross(steps, i, c), "хода чи біг по рингу: " + c + " — " + Describe(e, steps));
                            Assert.IsTrue(L.ContainsKey(c), "кліпа кроку немає в маніфесті: " + c + " — " + Describe(e, steps));
                            if (c.StartsWith("Whip_")) whips++;
                        }
                }
            Assert.Greater(whips, 0, "кидки в канати й кут є в планах (охоронець не порожній)");
            // Сам охоронець: кидок без склейки слідом — біг.
            var bare = new List<StageStep> { StageStep.Pair(MatchSide.Player, "Whip_Ropes", StageCam.Close) };
            Assert.IsTrue(RunsAcross(bare, 0, "Whip_Ropes_B"), "кидок у канати без склейки — біг через ринг");
            Assert.IsTrue(RunsAcross(new List<StageStep> { StageStep.Solo(MatchSide.Player, "Run_Fwd_Loop", StageCam.Close) }, 0, "Run_Fwd_Loop"));
            foreach (var m in CareerKit.Content().Moves.All)
                Assert.IsTrue(RingClipTable.KnowsMove(m.Id), "прийом каталогу без кліпів у таблиці: " + m.Id);
        }

        /// <summary>Підписні прийоми таблиці (накриття після них — кульмінація) — рівно ті, що каталог позначає kind signature.</summary>
        [Test]
        public void SignatureMoves_AreTheCatalogSignatures()
        {
            int n = 0;
            foreach (var m in CareerKit.Content().Moves.All)
            {
                Assert.AreEqual(m.Kind == MoveKind.Signature, RingClipTable.IsSignature(m.Id), m.Id);
                if (m.Kind == MoveKind.Signature) n++;
            }
            Assert.Greater(n, 3, "у каталозі є підписні прийоми");
            var fin = new MatchEvent { Kind = MatchEventKind.Action, Action = MatchActionId.Finisher, Actor = MatchSide.Player, Tier = RollTier.Partial };
            Assert.IsTrue(RingClipTable.IsClimax(fin, MatchSide.Player), "фінішер — кульмінація");
            Assert.IsFalse(RingClipTable.IsClimax(fin, MatchSide.Rival), "чужий фінішер — ні");
            fin.Tier = RollTier.Miss;
            Assert.IsFalse(RingClipTable.IsClimax(fin, MatchSide.Player), "фінішер, що не вдався, — ні");
            Assert.IsFalse(RingClipTable.IsClimax(null, MatchSide.Player));
        }

        /// <summary>
        /// «Удар головою» Хардкорника — власний кліп у центрі. За рингом удар звучить ударом об бар'єр, а кліп бар'єра не показує
        /// (і брудний прийом на тому самому кліпі звучить звичайним ударом) — там це підстановка.
        /// </summary>
        [Test]
        public void Headbutt_OwnClipInCenter_StandInOutside()
        {
            foreach (var t in new[] { RollTier.Partial, RollTier.Strong })
            {
                var e = new MatchEvent { Kind = MatchEventKind.Action, Action = MatchActionId.Strike, Tier = t, HasRoll = true, Actor = MatchSide.Player, MoveKey = "headbutt" };
                var inside = RingClipTable.Plan(e);
                Assert.AreEqual("Center_Headbutt", inside.Single(s => s.Kind == StageStepKind.Pair).Clip, t.ToString());
                Assert.IsFalse(inside.Any(s => s.IsStandIn), "у центрі — власний кліп: " + t);
                e.Outside = true;
                Assert.IsTrue(RingClipTable.Plan(e).Any(s => s.IsStandIn), "за рингом — підстановка: " + t);
            }
        }

        /// <summary>
        /// Охоронець звірки зі звуком (F2_DIRECTOR п. 1): промінь надії не підводить цілком — ядро на провалі кладе суперника,
        /// як на 7–9. Отже провал грається тим самим планом, що й 7–9: пара від нападника, жодного «Bump_Back» (падає сам).
        /// </summary>
        [Test]
        public void HopeSpot_Miss_PlaysLikePartial_AndTheAttackerNeverFallsAlone()
        {
            // Без ключа — як дає рушій (надія пропонується окремо); з ключами каталогу — щоб гілка таблиці прийомів теж трималась.
            foreach (var key in new string[] { null }.Concat(CareerKit.Content().Moves.All.Select(m => m.Id)))
            {
                Func<RollTier, MatchEvent> ev = t => new MatchEvent
                {
                    Kind = MatchEventKind.Action, Action = MatchActionId.HopeSpot, Tier = t, HasRoll = true, Actor = MatchSide.Player, MoveKey = key
                };
                var miss = RingClipTable.Plan(ev(RollTier.Miss));
                var partial = RingClipTable.Plan(ev(RollTier.Partial));
                string what = Describe(ev(RollTier.Miss), miss);
                if (key == null)
                    Assert.IsTrue(miss.Any(s => s.Kind == StageStepKind.Pair && s.Actor == MatchSide.Player), "пара від нападника: " + what);
                Assert.IsFalse(miss.Any(s => s.AllClips().Contains("Bump_Back")), "нападник не падає сам: " + what);
                CollectionAssert.AreEqual(partial.Select(s => s.Kind + ":" + s.Actor + ":" + s.Clip),
                    miss.Select(s => s.Kind + ":" + s.Actor + ":" + s.Clip), "провал = 7–9: " + what);
            }
            // Відчайдушний прийом надією не є: його провал і далі кладе нападника (ядро: Dmg(me), GoDown(me)).
            var desperate = RingClipTable.Plan(new MatchEvent { Kind = MatchEventKind.Action, Action = MatchActionId.Desperate, Tier = RollTier.Miss, HasRoll = true, Actor = MatchSide.Player });
            Assert.IsTrue(desperate.Any(s => s.AllClips().Contains("Bump_Back")), "відчайдушний провал — падає сам");
        }

        private static MatchEvent Kick(RollTier t) { return new MatchEvent { Kind = MatchEventKind.Kickout, Tier = t, HasRoll = true, Actor = MatchSide.Rival }; }

        /// <summary>
        /// Охоронець (2а), утримання P1-1 (<c>F2_DIRECTOR.md</c>): рахунок рефері за ступенем ядра — 10+ вирвався на «раз» («два»
        /// немає), 7–9 — на «два», 7–9 після фінішера чи підписного — «два з половиною», провал — «три» на третій долоні. Розв'язка (і
        /// рядок журналу) — на мітці рефері, усередині кліпа; відлік стартує рівно один раз.
        /// </summary>
        [Test]
        public void Kickout_CountsByTier_TheRevealIsOnTheReferee()
        {
            var ev = Events();
            var L = Lengths();
            Func<MatchEvent, PlanContext, BeatPlan> beat = (e, c) => RingBeats.For(RingClipTable.Plan(e, c), ev, n => Len(L, n));
            var none = default(PlanContext);

            var strong = beat(Kick(RollTier.Strong), none);
            Assert.AreEqual("Ref_Count_Pin_1_Slow", strong.Clip);
            Assert.Greater(strong.Count1, 0f);
            Assert.Less(strong.Count2, 0f, "10+ — вирвався на «раз», «два» немає");
            Assert.AreEqual(ev.Time(strong.Clip, "kickout"), strong.Final, 1e-5f);
            Assert.Greater(strong.Final, strong.Count1);
            Assert.IsFalse(strong.Three);

            var partial = beat(Kick(RollTier.Partial), none);
            Assert.AreEqual("Ref_Count_Pin_2_Slow", partial.Clip);
            Assert.Greater(partial.Count2, partial.Count1);
            Assert.GreaterOrEqual(partial.Final, partial.Count2, "7–9 — не раніше «два»");
            Assert.IsFalse(partial.Three);

            var climaxCtx = new PlanContext { LastMove = new MatchEvent { Kind = MatchEventKind.Action, Action = MatchActionId.Finisher, Actor = MatchSide.Player, Tier = RollTier.Strong } };
            var climax = beat(Kick(RollTier.Partial), climaxCtx);
            Assert.AreEqual("Ref_Count_Pin_2_Half", climax.Clip, "після фінішера — «два з половиною»");
            Assert.Greater(climax.Final, partial.Final, "кульмінація тягне рахунок довше");

            var miss = beat(Kick(RollTier.Miss), none);
            Assert.AreEqual("Ref_Count_Pin_3_Slow", miss.Clip);
            Assert.IsTrue(miss.Three);
            Assert.AreEqual(ev.Time("Ref_Count_Pin_3_Slow", "count_3"), miss.Final, 1e-5f, "«три» — на третій долоні");

            var rescue = beat(Kick(RollTier.Miss), new PlanContext { Next = new MatchEvent { Kind = MatchEventKind.Rescue, Actor = MatchSide.Rival } });
            Assert.IsFalse(rescue.Three, "напарник розриває рахунок — «три» немає");

            var bribe = Kick(RollTier.Partial); bribe.Perk = PerkCatalog.BribedRef;
            var bribed = RingClipTable.Plan(bribe);
            Assert.IsFalse(bribed.Any(s => s.Clip != null && s.Clip.StartsWith("Cover_Kickout")), "підкуп: рефері зупинився — кікауту немає");
            Assert.IsTrue(bribed.Any(s => s.Clip == "Kneel_Disbelief"), "нападник не вірить");

            foreach (var p in new[] { strong, partial, climax, miss, rescue })
                Assert.Less(p.Final, Len(L, p.Clip), "розв'язка — усередині кліпа рефері");
            foreach (var c in new[] { none, climaxCtx, new PlanContext { Held = RingClipTable.HeldPin, HeldBy = MatchSide.Player } })
                foreach (var t in new[] { RollTier.Miss, RollTier.Partial, RollTier.Strong })
                    Assert.AreEqual(1, RingClipTable.Plan(Kick(t), c).Count(s => s.Count), "відлік стартує рівно один раз: " + t);
        }

        /// <summary>
        /// Кікаут борців синхронний з рефері: петля утримання кінчається так, що мітка kickout кліпа кікауту лягає рівно на мітку
        /// kickout рефері (<see cref="RingClipTable.CountUntil"/> — те саме, чим чекає режисер). «Три» — долоня на маті, далі
        /// пауза й нападник відпускає.
        /// </summary>
        [Test]
        public void Kickout_LandsOnTheReferees_Kickout()
        {
            var ev = Events();
            int synced = 0;
            var ctxs = new[]
            {
                default(PlanContext),
                new PlanContext { LastMove = new MatchEvent { Kind = MatchEventKind.Action, Action = MatchActionId.Finisher, Actor = MatchSide.Player, Tier = RollTier.Strong } },
                new PlanContext { Held = RingClipTable.HeldPin, HeldBy = MatchSide.Player },
            };
            foreach (var c in ctxs)
                foreach (var t in new[] { RollTier.Miss, RollTier.Partial, RollTier.Strong })
                {
                    var steps = RingClipTable.Plan(Kick(t), c);
                    string what = Describe(Kick(t), steps);
                    string count = steps.First(s => s.Count).RefClip;
                    int hi = steps.FindIndex(s => s.Clip == "Cover_Hold_Loop");
                    Assert.GreaterOrEqual(hi, 0, what);
                    var hold = steps[hi];
                    var next = steps[hi + 1];
                    Assert.IsTrue(hold.Chain && next.Chain, "утримання й розв'язка — ланцюгом пари: " + what);
                    float until = RingClipTable.CountUntil(hold, next, count, ev);
                    Assert.GreaterOrEqual(until, 0f, "петля утримання знає, коли кінчитись: " + what);
                    if (next.Clip.StartsWith("Cover_Kickout"))
                    {
                        synced++;
                        Assert.AreEqual(ev.Time(count, "kickout"), until + ev.Time(next.Clip, "kickout"), 1e-4f, "кікаут — на мітці рефері: " + what);
                    }
                    else if (ev.Time(count, "count_3") >= 0f)
                        Assert.Greater(until, ev.Time(count, "count_3"), "«три» — спершу долоня на маті: " + what);
                    else
                        Assert.AreEqual(ev.Time(count, "kickout"), until, 1e-4f, "рефері зупинився — нападник відпускає: " + what);
                    bool pinned = c.Held == RingClipTable.HeldPin;
                    Assert.AreEqual(!pinned, steps[0].Clip == "Cover_Enter", "накриття — лише якщо пара ще не в утриманні: " + what);
                    Assert.AreEqual(RingClipTable.KneelUp, steps[steps.Count - 1].Clip, "нападник встає з колін: " + what);
                }
            Assert.Greater(synced, 4, "кікаути перевірено");
        }

        /// <summary>
        /// «Терпіти» (P1-2): 10+ — тримає неповно → вихід, 7–9 — дотиск → тягнеться → вихід, провал — дотиск (після підписного —
        /// ефектний) → стукає. Рефері питає про здачу першим кроком; такт — на тягнеться, стукає чи вихід; родина — з пози, що
        /// тримається, а без неї — з прийому.
        /// </summary>
        [Test]
        public void Endure_PlaysTheHoldByTier()
        {
            var ev = Events();
            var L = Lengths();
            foreach (var key in new[] { null, "armbar", "leg_lock", "spider_lock", "crossface", "bear_hug" })
                foreach (var held in new[] { null, RingClipTable.HeldArmbar, RingClipTable.HeldLegLock })
                    foreach (var t in new[] { RollTier.Miss, RollTier.Partial, RollTier.Strong })
                    {
                        var e = new MatchEvent { Kind = MatchEventKind.Endure, Tier = t, HasRoll = true, Actor = MatchSide.Rival, MoveKey = key };
                        var steps = RingClipTable.Plan(e, new PlanContext { Held = held, HeldBy = MatchSide.Player });
                        string what = Describe(e, steps) + " тримає " + (held ?? "—");
                        string k = held == RingClipTable.HeldArmbar ? "Armbar" : held == RingClipTable.HeldLegLock ? "LegLock" : RingClipTable.HoldFamily(key);
                        string[] want = t == RollTier.Strong ? new[] { k + "_Hold_Partial_Loop", k + "_Escape" }
                            : t == RollTier.Partial ? new[] { k + "_Crank", k + "_Reach", k + "_Escape" }
                            : new[] { key == "spider_lock" ? k + "_Crank_Hype" : k + "_Crank", k + "_Tap" };
                        CollectionAssert.AreEqual(want.Concat(new[] { RingClipTable.KneelUp }), steps.Select(s => s.Clip), what);
                        Assert.IsTrue(steps.All(s => s.Actor == MatchSide.Player), "больовий тримає нападник: " + what);
                        Assert.AreEqual("Ref_Ask_Submit_Loop", steps[0].RefClip, "рефері питає про здачу: " + what);
                        Assert.AreEqual(held != null, steps[0].Chain, "з пози, що тримається, — ланцюгом: " + what);
                        var plan = RingBeats.For(steps, ev, n => Len(L, n));
                        string beatClip = t == RollTier.Strong ? k + "_Escape" : t == RollTier.Partial ? k + "_Reach" : k + "_Tap";
                        Assert.AreEqual(beatClip, plan.Clip, "такт: " + what);
                        Assert.AreNotEqual(BeatSource.Rule, plan.Source, "такт — з мітки кліпа: " + what);
                    }
            Assert.IsTrue(RingClipTable.IsSignature("spider_lock"));
        }

        /// <summary>
        /// Поза, що тримається між подіями: накриття продовжує лише утримання суперника чи дія «утримання» того, хто накрив;
        /// больовий — лише «терпіти» суперника. Інакше нападник відпускає (кроки-наслідки, такту не несуть). Позу дає кінцевий вузол
        /// пари з маніфесту.
        /// </summary>
        [Test]
        public void HeldPose_ContinuesOnlyIntoItsOwnEvent_ElseReleased()
        {
            var L = Lengths();
            var P = MatchSide.Player; var R = MatchSide.Rival;
            Assert.IsTrue(RingClipTable.Continues(Kick(RollTier.Partial), RingClipTable.HeldPin, P));
            Assert.IsFalse(RingClipTable.Continues(new MatchEvent { Kind = MatchEventKind.Kickout, Actor = P }, RingClipTable.HeldPin, P), "свій кікаут — ні");
            Assert.IsTrue(RingClipTable.Continues(new MatchEvent { Kind = MatchEventKind.Action, Action = MatchActionId.Pin, Actor = P }, RingClipTable.HeldPin, P));
            Assert.IsFalse(RingClipTable.Continues(new MatchEvent { Kind = MatchEventKind.Action, Action = MatchActionId.Pin, Actor = R }, RingClipTable.HeldPin, P));
            var endure = new MatchEvent { Kind = MatchEventKind.Endure, Actor = R };
            Assert.IsTrue(RingClipTable.Continues(endure, RingClipTable.HeldLegLock, P));
            Assert.IsTrue(RingClipTable.Continues(endure, RingClipTable.HeldArmbar, P));
            Assert.IsFalse(RingClipTable.Continues(endure, RingClipTable.HeldPin, P), "больовий не продовжує накриття");
            Assert.IsFalse(RingClipTable.Continues(Kick(RollTier.Partial), RingClipTable.HeldLegLock, P), "утримання не продовжує больовий");
            Assert.IsFalse(RingClipTable.Continues(new MatchEvent { Kind = MatchEventKind.Action, Action = MatchActionId.Strike, Actor = P }, RingClipTable.HeldPin, P));
            Assert.IsFalse(RingClipTable.Continues(Kick(RollTier.Partial), null, P));
            Assert.IsFalse(RingClipTable.Continues(null, RingClipTable.HeldPin, P));

            Assert.IsEmpty(RingClipTable.Release(null, P));
            foreach (var kv in new Dictionary<string, string> { { RingClipTable.HeldPin, "Cover_Release" }, { RingClipTable.HeldArmbar, "Armbar_Escape" }, { RingClipTable.HeldLegLock, "LegLock_Escape" } })
            {
                var rel = RingClipTable.Release(kv.Key, P);
                Assert.AreEqual(kv.Value, rel[0].Clip);
                Assert.IsTrue(rel[0].Chain, "відпускає з тієї самої пози — ланцюгом");
                Assert.IsTrue(rel.All(s => s.Aside && s.Actor == P), "кроки-наслідки того, хто тримав");
                Assert.AreEqual(RingClipTable.KneelUp, rel[rel.Count - 1].Clip);
                foreach (var st in rel) foreach (var c in st.AllClips()) Assert.IsTrue(L.ContainsKey(c), c);
            }

            Assert.AreEqual(RingClipTable.HeldPin, RingClipTable.HeldByNode("pinned_A"));
            Assert.AreEqual(RingClipTable.HeldLegLock, RingClipTable.HeldByNode("leglock_A"));
            Assert.AreEqual(RingClipTable.HeldArmbar, RingClipTable.HeldByNode("armbar_A"));
            Assert.IsNull(RingClipTable.HeldByNode("kneel_up"));
            Assert.IsNull(RingClipTable.HeldByNode("-"));
            Assert.IsNull(RingClipTable.HeldByNode(null));

            // Вхід у больовий лишає пару в позі больового (наступна подія — «терпіти»), стрибок 10+ — у накритті.
            var N = Nodes();
            Func<List<StageStep>, string> heldAfter = steps =>
            {
                var last = steps.LastOrDefault(s => s.Kind == StageStepKind.Pair);   // як режисер: остання зіграна пара
                return last == null ? null : RingClipTable.HeldByNode(N[last.Clip + "_A"][1]);
            };
            // Больові ґіміків 5–8 без своїх кліпів — на лікоть, вхід із позначкою підстановки (не загальна пара таблиці: інакше
            // «терпіти» слідом починалося б з пози стоячи).
            foreach (var kv in new Dictionary<string, string> { { "armbar", RingClipTable.HeldArmbar }, { "leg_lock", RingClipTable.HeldLegLock },
                { "spider_lock", RingClipTable.HeldLegLock }, { "crossface", RingClipTable.HeldArmbar }, { "bear_hug", RingClipTable.HeldArmbar } })
            {
                var sub = RingClipTable.Plan(new MatchEvent { Kind = MatchEventKind.Action, Action = MatchActionId.Submission, Tier = RollTier.Partial, Actor = P, MoveKey = kv.Key });
                Assert.AreEqual(kv.Value, heldAfter(sub), kv.Key);
                bool standIn = kv.Key == "crossface" || kv.Key == "bear_hug";
                Assert.AreEqual(standIn, sub.Any(s => s.IsStandIn), "підстановка больового позначена: " + kv.Key);
                Assert.AreEqual(standIn, RingClipTable.IsHoldStandIn(kv.Key), kv.Key);
            }
            var meteor = RingClipTable.Plan(new MatchEvent { Kind = MatchEventKind.Action, Action = MatchActionId.Finisher, Tier = RollTier.Strong, Actor = P, MoveKey = "meteor", Staging = MoveStaging.Corner });
            Assert.AreEqual(RingClipTable.HeldPin, heldAfter(meteor), "«Метеор» 10+ — у накритті, далі відлік");
            Assert.IsNull(heldAfter(RingClipTable.Plan(Kick(RollTier.Partial))), "після кікауту пару відпущено");
        }

        /// <summary>Вузли кліпів з маніфесту: ім'я → {з вузла, у вузол} («-» — будь-який).</summary>
        private static Dictionary<string, string[]> Nodes()
        {
            var d = new Dictionary<string, string[]>();
            string dir = Path.Combine(Root(), "Assets", "Art", "Animations");
            foreach (var raw in File.ReadAllLines(Path.Combine(dir, "ring_anims_manifest.txt")))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                var p = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length < 10) continue;
                d[p[0]] = new[] { p[8], p[9] };
            }
            return d;
        }

        /// <summary>
        /// Безперервність пози в межах події: крок ланцюга (і крок-наслідок одного борця) починається з того вузла, де його борець
        /// скінчив попередній крок, — режисер не перескакує позою. Склейка — свідомий розрив (вузол невідомий), падіння жертви на
        /// плані 1 стрибка кладе її на спину. Перевірено всі події рушія, прийоми каталогу, утримання й больові з позою.
        /// </summary>
        [Test]
        public void Plans_ChainPosesWithoutJumps()
        {
            var N = Nodes();
            var P = MatchSide.Player;
            var cases = new List<KeyValuePair<MatchEvent, PlanContext>>();
            foreach (var c0 in Contexts())
                foreach (var e in AllEvents().Concat(CatalogEvents())) cases.Add(new KeyValuePair<MatchEvent, PlanContext>(e, c0));
            foreach (var t in new[] { RollTier.Miss, RollTier.Partial, RollTier.Strong })
            {
                cases.Add(new KeyValuePair<MatchEvent, PlanContext>(Kick(t), new PlanContext { Held = RingClipTable.HeldPin, HeldBy = P }));
                foreach (var held in new[] { RingClipTable.HeldArmbar, RingClipTable.HeldLegLock })
                    cases.Add(new KeyValuePair<MatchEvent, PlanContext>(new MatchEvent { Kind = MatchEventKind.Endure, Tier = t, Actor = MatchSide.Rival }, new PlanContext { Held = held, HeldBy = P }));
            }
            int checkedSteps = 0;
            foreach (var kv in cases)
            {
                var e = kv.Key; var c = kv.Value;
                var steps = RingClipTable.Plan(e, c);
                string what = Describe(e, steps);
                var node = new Dictionary<MatchSide, string>();
                if (c.Held != null)
                {
                    string h = c.Held == RingClipTable.HeldPin ? "pinned" : c.Held == RingClipTable.HeldArmbar ? "armbar" : "leglock";
                    node[c.HeldBy] = h + "_A"; node[RingClipTable.Opp(c.HeldBy)] = h + "_B";
                }
                else
                {
                    node[e.Actor] = c.ActorNode; node[RingClipTable.Opp(e.Actor)] = c.VictimNode;   // поза з дзеркала показу
                }
                Action<MatchSide, string, string> expect = (who, from, clip) =>
                {
                    string have;
                    if (from == "-" || !node.TryGetValue(who, out have) || have == null) return;
                    checkedSteps++;
                    Assert.AreEqual(have, from, who + " починає «" + clip + "» не з того вузла: " + what);
                };
                for (int i = 0; i < steps.Count; i++)
                {
                    var st = steps[i];
                    MatchSide a = st.Actor, b = RingClipTable.Opp(a);
                    switch (st.Kind)
                    {
                        case StageStepKind.Pair:
                            if (st.Chain)
                            {
                                expect(a, N[st.Clip + "_A"][0], st.Clip + "_A");
                                expect(b, N[st.Clip + "_B"][0], st.Clip + "_B");
                            }
                            node[a] = Known(N[st.Clip + "_A"][1]); node[b] = Known(N[st.Clip + "_B"][1]);
                            if (st.StopAt != null) { node[a] = null; node[b] = null; }   // обірваний кліп — вузол посередині
                            break;
                        case StageStepKind.Solo:
                            if (st.AnchorPair != null) { node[a] = null; break; }       // стрибок у повітрі — далі приземлення
                            if (st.Aside && !st.CutIn) expect(a, N[st.Clip][0], st.Clip);   // склейка кадру — свідомий розрив
                            node[a] = Known(N[st.Clip][1]);
                            break;
                        case StageStepKind.Cut:
                            node[a] = null;
                            if (st.DropVictim) node[b] = "lying_back";
                            break;
                    }
                }
            }
            Assert.Greater(checkedSteps, 100, "переходи пози перевірено");
        }

        private static string Known(string node) { return node == "-" ? null : node; }

        /// <summary>
        /// Контексти показу, якими живий режисер кликає таблицю (<see cref="PlanContext"/>): без нього, суперник лежить на спині
        /// чи обличчям донизу, виконавець на колінах чи обличчям донизу. Гілки таблиці за позою (стрибок на лежачого, вхід
        /// склейкою, підйом з колін) інакше тести не проходять (рецензія Ф2 кроків 5 і 7).
        /// </summary>
        private static IEnumerable<PlanContext> Contexts()
        {
            yield return default(PlanContext);
            yield return new PlanContext { VictimDown = true, VictimNode = "lying_back" };
            yield return new PlanContext { VictimDown = true, VictimNode = StageStep.LyingFront };
            yield return new PlanContext { VictimNode = "kneel_up" };
            yield return new PlanContext { ActorNode = "kneel_up" };
            yield return new PlanContext { ActorNode = StageStep.LyingFront };
        }

        private static bool Flips(string have, string from)
        {
            return (have == StageStep.LyingFront && from == "lying_back") || (have == "lying_back" && from == StageStep.LyingFront);
        }

        /// <summary>
        /// Рецензія Ф2 кроків 5 і 7: борець, що лежить обличчям донизу (фліп, вихід із больового на лікоть, стрибок повз), не
        /// перевертається на спину на очах. Кліп, що починається на спині (накриття, захват ноги, приземлення, підйом), заходить
        /// на нього лише склейкою кадру (<see cref="StageStep.CutIn"/>, за коренями) або після склейки на канат чи кут, під якою
        /// тіло стає на спину (<see cref="StageStep.DropVictim"/>). Кліпа перевороту немає (<c>CLIP_NEEDS.md</c> §9).
        /// </summary>
        [Test]
        public void FaceDownBody_NeverFlipsInView()
        {
            var N = Nodes();
            int guarded = 0;
            var events = AllEvents().Concat(CatalogEvents()).Concat(new[] { Kick(RollTier.Miss), Kick(RollTier.Partial), Kick(RollTier.Strong) }).ToList();
            foreach (var c in Contexts())
                foreach (var e in events)
                {
                    var steps = RingClipTable.Plan(e, c);
                    string what = Describe(e, steps) + " [виконавець " + (c.ActorNode ?? "?") + ", суперник " + (c.VictimNode ?? "?") + "]";
                    var node = new Dictionary<MatchSide, string> { { e.Actor, c.ActorNode }, { RingClipTable.Opp(e.Actor), c.VictimNode } };
                    foreach (var st in steps)
                    {
                        MatchSide a = st.Actor, b = RingClipTable.Opp(a);
                        bool cutIn = st.CutIn || st.ByRoots;
                        switch (st.Kind)
                        {
                            case StageStepKind.Pair:
                                foreach (var side in new[] { a, b })
                                {
                                    string from = N[st.Clip + (side == a ? "_A" : "_B")][0];
                                    if (!Flips(node[side], from)) continue;
                                    guarded++;
                                    Assert.IsTrue(cutIn, side + " лежить " + node[side] + ", а «" + st.Clip + "» починає з " + from + " — без склейки: " + what);
                                }
                                node[a] = st.StopAt != null ? null : Known(N[st.Clip + "_A"][1]);
                                node[b] = st.StopAt != null ? null : Known(N[st.Clip + "_B"][1]);
                                break;
                            case StageStepKind.Solo:
                                if (st.AnchorPair != null) { node[a] = null; break; }
                                if (Flips(node[a], N[st.Clip][0]))
                                {
                                    guarded++;
                                    Assert.IsTrue(cutIn, a + " лежить " + node[a] + ", а «" + st.Clip + "» починає з " + N[st.Clip][0] + " — без склейки: " + what);
                                }
                                node[a] = Known(N[st.Clip][1]);
                                break;
                            case StageStepKind.Cut:
                                node[a] = null;
                                if (st.DropVictim) node[b] = "lying_back";
                                break;
                        }
                    }
                }
            Assert.Greater(guarded, 10, "переходи з живота на спину перевірено (накриття, захват ноги, підйом)");
            // Приклад рецензії: фліп-фінішер кладе обличчям донизу, далі утримання — накриття склейкою.
            var kick = RingClipTable.Plan(Kick(RollTier.Partial), new PlanContext { ActorNode = StageStep.LyingFront });
            Assert.IsTrue(kick[0].Clip == "Cover_Enter" && kick[0].CutIn, "накриття лежачого обличчям донизу — склейкою");
            var dive = RingClipTable.Plan(new MatchEvent { Kind = MatchEventKind.Action, Action = MatchActionId.TopRope, Tier = RollTier.Strong, Actor = MatchSide.Player,
                MoveKey = "swallow_dive", Staging = MoveStaging.Corner }, new PlanContext { VictimDown = true, VictimNode = StageStep.LyingFront });
            Assert.IsTrue(dive[0].Kind == StageStepKind.Cut && dive[0].DropVictim, "стрибок на лежачого обличчям донизу — під склейкою стає на спину");
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
