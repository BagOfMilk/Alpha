using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Game.Core.Ring;
using Game.Core.Ring.Match;
using Game.Core.Ring.Career;
using Game.Gameplay.Combat;
using Game.Gameplay.Ring.Models;
using Game.Gameplay.Ring.Stage;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Таблиця «подія → звук» (критерій М1, ПМ 08.11.2026: «таблиця «подія → звук» без порожніх подій (удар, гонг, рахунок, натовп,
    /// QTE, вихід)»): кожне значення переліків подій і дій ядра має звук, кожен звук — слоти бібліотеки, слоти — справжні файли
    /// або нові слоти бібліотеки звуку, документ таблиці не відстає. Імпорт звуку рингу — моно (пакет П4). Бій за рингом (запит ПМ
    /// 08.11.2026: предмет, удар і кидок у бар'єр, склейка місця, відлік) — за назвами переробки бою, тож тести ходять і з основним ядром.
    /// </summary>
    public class RingSoundMapTests
    {
        private static string Root() { return Path.GetDirectoryName(Application.dataPath); }

        [Test]
        public void EveryEventKind_HasASound()
        {
            var empty = new List<string>();
            foreach (MatchEventKind k in Enum.GetValues(typeof(MatchEventKind)))
                foreach (MatchActionId a in Enum.GetValues(typeof(MatchActionId)))
                    if (RingSoundMap.ForEvent(k, a) == RingCue.None) empty.Add(k + "/" + a);
            Assert.IsEmpty(empty, "події без звуку:\n" + string.Join("\n", empty));
        }

        [Test]
        public void EveryAction_HasASound()
        {
            var empty = Enum.GetValues(typeof(MatchActionId)).Cast<MatchActionId>().Where(a => RingSoundMap.ForAction(a) == RingCue.None).ToList();
            Assert.IsEmpty(empty, "дії без звуку: " + string.Join(", ", empty));
        }

        [Test]
        public void EveryCue_PlaysFromLibrarySlots()
        {
            foreach (var c in RingSoundMap.AllCues())
                Assert.Greater(RingSoundMap.SlotsOf(c).Count, 0, "звук без слотів бібліотеки: " + c);
            Assert.AreEqual(0, RingSoundMap.SlotsOf(RingCue.None).Count);
        }

        [Test]
        public void M1_RequiredEvents_HaveSounds()
        {
            // удар, гонг (дзвін рингу), рахунок рефері, натовп, QTE, вихід борця
            Assert.AreEqual(RingCue.Hit, RingSoundMap.ForAction(MatchActionId.Strike), "удар");
            Assert.AreEqual(RingCue.BellEnd, RingSoundMap.ForEvent(MatchEventKind.MatchEnd, MatchActionId.Pin), "дзвін кінця");
            Assert.AreEqual(RingCue.MatSlap, RingSoundMap.ForEvent(MatchEventKind.Kickout, MatchActionId.Pin), "рахунок — долоня рефері");
            Assert.AreEqual(RingCue.CrowdBed, RingSoundMap.ForEvent(MatchEventKind.TurnEnd, MatchActionId.CatchBreath), "натовп — гул залу");
            foreach (var c in new[] { RingCue.BellStart, RingCue.BellEnd, RingCue.MatSlap, RingCue.CrowdBed, RingCue.Pop, RingCue.QtePrompt,
                         RingCue.QteHit, RingCue.QteMiss, RingCue.Entrance, RingCue.Hit })
                Assert.Greater(RingSoundMap.SlotsOf(c).Count, 0, c.ToString());
        }

        [Test]
        public void EveryEffectKind_IsConsidered()
        {
            // Новий наслідок ядра має з'явитись у таблиці: зі звуком або свідомо без (звучить дією, що його спричинила).
            var missing = Enum.GetValues(typeof(MatchEffectKind)).Cast<MatchEffectKind>().Where(k => !RingSoundMap.HasEffect(k.ToString())).ToList();
            Assert.IsEmpty(missing, "наслідки без рядка таблиці: " + string.Join(", ", missing));
            Assert.AreEqual(RingCue.CountOut, RingSoundMap.ForEffect(MatchEffectKind.Count), "відлік за рингом");
        }

        [Test]
        public void OutsideFight_HasSounds_ByName()
        {
            // Переробка бою (claude/ring-combat-overhaul): дія Weapon, наслідок Place, поле MatchEvent.Outside.
            Assert.IsTrue(RingSoundMap.HasAction("Weapon"));
            Assert.AreEqual(RingCue.CrowdSurge, RingSoundMap.ForEffect("Place"), "склейка місця");
            Assert.AreEqual(RingCue.CountOut, RingSoundMap.ForEffect("Count"), "відлік за рингом");
            // предмет — docs/ring/CLIP_NEEDS.md, TRPG_DESIGN §4.4 (ПМ 08.11.2026): 10+ — реквізит режисера, 7–9 — стілець, ≤6 — замах повз
            Assert.AreEqual(RingCue.WeaponChair, RingSoundMap.ForEvent("Action", "Weapon", true, RollTier.Strong, true), "10+ без реквізиту — стілець");
            Assert.AreEqual(RingCue.WeaponChair, RingSoundMap.ForEvent("Action", "Weapon", true, RollTier.Strong, true, RingProp.Chair));
            Assert.AreEqual(RingCue.WeaponTable, RingSoundMap.ForEvent("Action", "Weapon", true, RollTier.Strong, true, RingProp.Table));
            Assert.AreEqual(RingCue.WeaponChair, RingSoundMap.ForEvent("Action", "Weapon", true, RollTier.Partial, true, RingProp.Table), "7–9 — завжди стілець");
            Assert.AreEqual(RingCue.Whiff, RingSoundMap.ForEvent("Action", "Weapon", true, RollTier.Miss, true, RingProp.Table), "≤6 — замах повз");
            Assert.AreEqual(RingCue.WeaponChair, RingSoundMap.ForEvent("Action", "Weapon", false, RollTier.Miss, true, RingProp.Table), "без кидка — стілець");
            // за рингом удар і кидок ідуть у бар'єр; на рингу — як завжди
            Assert.AreEqual(RingCue.BarrierHit, RingSoundMap.ForEvent("Action", "Strike", true, RollTier.Partial, true));
            Assert.AreEqual(RingCue.BarrierSlam, RingSoundMap.ForEvent("Action", "Throw", true, RollTier.Strong, true));
            Assert.AreEqual(RingCue.Hit, RingSoundMap.ForEvent("Action", "Strike", true, RollTier.Partial, false));
            Assert.AreEqual(RingCue.Slam, RingSoundMap.ForEvent("Action", "Throw", true, RollTier.Strong, false));
            // вихід і повернення: падіння на підлогу / закочування; не викинув — пара в захваті
            Assert.AreEqual(RingCue.FloorThud, RingSoundMap.ForEvent("Action", "LeaveRing", true, RollTier.Strong, false));
            Assert.AreEqual(RingCue.Lockup, RingSoundMap.ForEvent("Action", "LeaveRing", true, RollTier.Miss, false));
            Assert.AreEqual(RingCue.CanvasShuffle, RingSoundMap.ForEvent("Action", "ReturnToRing", false, RollTier.Strong, true));
            foreach (var c in new[] { RingCue.BarrierHit, RingCue.BarrierSlam, RingCue.WeaponChair, RingCue.WeaponTable, RingCue.CountOut, RingCue.CrowdSurge })
                Assert.Greater(RingSoundMap.SlotsOf(c).Count, 0, c.ToString());
        }

        [Test]
        public void Miss_SoundsLikeWhatIsOnScreen()
        {
            // Удар із провалом — суперник ухиляється (Center_Sidestep): контакту немає, удар по тілу не звучить.
            Assert.AreEqual(RingCue.Whiff, RingSoundMap.ForEvent("Action", "Strike", true, RollTier.Miss, false));
            Assert.AreEqual(RingCue.CrowdBed, RingSoundMap.ForEvent("Sell", "Strike", true, RollTier.Miss, false), "продаж — гул залу, не удар");
            Assert.AreEqual(RingCue.Whiff, RingSoundMap.ForEvent("Action", "Dirty", true, RollTier.Miss, false));
            Assert.AreEqual(RingCue.Slam, RingSoundMap.ForEvent("Action", "Desperate", true, RollTier.Miss, false), "відчайдушний: сам падає");
            Assert.AreEqual(RingCue.Hit, RingSoundMap.ForEvent("Action", "Strike", false, RollTier.Miss, false), "без кидка ступінь не читається");
            // Решта провалів — за RingClipTable: пара в захваті (Lockup_Stalemate), нападник падає сам (Bump_Back), провокація — без оплесків.
            Assert.AreEqual(RingCue.Lockup, RingSoundMap.ForEvent("Action", "Submission", true, RollTier.Miss, false), "больовий: нічия в захваті");
            Assert.AreEqual(RingCue.HoldStrain, RingSoundMap.ForEvent("Action", "Submission", true, RollTier.Partial, false));
            Assert.AreEqual(RingCue.Slam, RingSoundMap.ForEvent("Action", "TopRope", true, RollTier.Miss, false), "стрибок не вдався: падає сам");
            Assert.AreEqual(RingCue.Land, RingSoundMap.ForEvent("Action", "TopRope", true, RollTier.Strong, false));
            Assert.AreEqual(RingCue.CrowdBed, RingSoundMap.ForEvent("Action", "Taunt", true, RollTier.Miss, false), "провокація не вдалась: оплесків немає");
            Assert.AreEqual(RingCue.Pop, RingSoundMap.ForEvent("Action", "Taunt", true, RollTier.Strong, false));
            foreach (var a in new[] { "Throw", "HopeSpot", "Finisher" })
                Assert.AreEqual(RingCue.Slam, RingSoundMap.ForEvent("Action", a, true, RollTier.Miss, false), a + ": падіння");
            // підйом з провалом — той самий кліп, що в «відновитися» (Idle_Tired_Loop), тож і звук той самий
            Assert.AreEqual(RingSoundMap.ForEvent("Action", "Recover", true, RollTier.Strong, false),
                RingSoundMap.ForEvent("Action", "GetUp", true, RollTier.Miss, false), "підйом з провалом звучить як «відновитися»");
        }

        [Test]
        public void RealEvent_GoesThroughTheSameTable()
        {
            // Подія матчу (не назви) — той самий шлях: ступінь кидка уточнює звук. Поля — спільні для обох ядер.
            Assert.AreEqual(RingCue.Whiff, RingSoundMap.ForEvent(new MatchEvent { Kind = MatchEventKind.Action, Action = MatchActionId.Strike, HasRoll = true, Tier = RollTier.Miss }));
            Assert.AreEqual(RingCue.Hit, RingSoundMap.ForEvent(new MatchEvent { Kind = MatchEventKind.Action, Action = MatchActionId.Strike, HasRoll = true, Tier = RollTier.Strong }));
            Assert.AreEqual(RingCue.Lockup, RingSoundMap.ForEvent(new MatchEvent { Kind = MatchEventKind.Action, Action = MatchActionId.Submission, HasRoll = true, Tier = RollTier.Miss }));
            Assert.AreEqual(RingCue.CrowdBed, RingSoundMap.ForEvent(new MatchEvent { Kind = MatchEventKind.Action, Action = MatchActionId.Taunt, HasRoll = true, Tier = RollTier.Miss }));
            Assert.AreEqual(RingCue.Slam, RingSoundMap.ForEvent(new MatchEvent { Kind = MatchEventKind.Action, Action = MatchActionId.TopRope, HasRoll = true, Tier = RollTier.Miss }));
            Assert.AreEqual(RingCue.None, RingSoundMap.ForEvent(null));
        }

        [Test]
        public void Weapon_SecondTouch_IsSelfHitOrBarrier()
        {
            // Другий дотик режисер кличе на мітці кліпа: 7–9 — нападник зачепив себе, ≤6 — після замаху сам у бар'єр; 10+ — без нього.
            Assert.AreEqual(RingCue.Hit, RingSoundMap.FollowUpOf("Action", "Weapon", true, RollTier.Partial));
            Assert.AreEqual(RingCue.BarrierSlam, RingSoundMap.FollowUpOf("Action", "Weapon", true, RollTier.Miss));
            Assert.AreEqual(RingCue.None, RingSoundMap.FollowUpOf("Action", "Weapon", true, RollTier.Strong));
            Assert.AreEqual(RingCue.None, RingSoundMap.FollowUpOf("Action", "Weapon", false, RollTier.Miss), "без кидка ступінь не читається");
            Assert.AreEqual(RingCue.None, RingSoundMap.FollowUpOf("Sell", "Weapon", true, RollTier.Miss), "продаж — не дія");
            Assert.AreEqual(RingCue.None, RingSoundMap.FollowUpOf("Action", "Strike", true, RollTier.Miss));
            Assert.AreEqual(RingCue.None, RingSoundMap.FollowUpOf(null));
            Assert.AreEqual(RingCue.None, RingSoundMap.FollowUpOf(new MatchEvent()), "порожня подія — без другого дотику");
        }

        private static MatchEvent Act(MatchActionId a, RollTier t, bool outside = false, string moveKey = null)
        {
            return new MatchEvent { Kind = MatchEventKind.Action, Action = a, HasRoll = true, Tier = t, Outside = outside, MoveKey = moveKey };
        }

        [Test]
        public void Outside_GoesThroughTheRealEvent()
        {
            // Поле MatchEvent.Outside читається напряму (гілка звуку стоїть на гілці бою): за рингом удар і кидок — у бар'єр.
            Assert.AreEqual(RingCue.BarrierHit, RingSoundMap.ForEvent(Act(MatchActionId.Strike, RollTier.Strong, true)));
            Assert.AreEqual(RingCue.BarrierSlam, RingSoundMap.ForEvent(Act(MatchActionId.Throw, RollTier.Partial, true)));
            Assert.AreEqual(RingCue.Hit, RingSoundMap.ForEvent(Act(MatchActionId.Strike, RollTier.Strong)), "на рингу — як завжди");
            // провал важить більше за місце: за рингом промах удару — теж ухил, невдалий кидок — падіння
            Assert.AreEqual(RingCue.Whiff, RingSoundMap.ForEvent(Act(MatchActionId.Strike, RollTier.Miss, true)));
            Assert.AreEqual(RingCue.Slam, RingSoundMap.ForEvent(Act(MatchActionId.Throw, RollTier.Miss, true)));
            Assert.IsFalse(new MatchEvent().Outside, "нова подія — на рингу");
        }

        [Test]
        public void Reverse_SoundsByTier()
        {
            // RingClipTable.Plan: 10+ — захисник кидає (Bodyslam), 7–9 — обмін захватами (Lockup_Exchange), провал — кроків немає.
            Func<RollTier, MatchEvent> rev = t => new MatchEvent { Kind = MatchEventKind.Reverse, Action = MatchActionId.Throw, HasRoll = true, Tier = t };
            Assert.AreEqual(RingCue.Slam, RingSoundMap.ForEvent(rev(RollTier.Strong)));
            Assert.AreEqual(RingCue.Lockup, RingSoundMap.ForEvent(rev(RollTier.Partial)));
            Assert.AreEqual(RingCue.CrowdBed, RingSoundMap.ForEvent(rev(RollTier.Miss)), "провал реверсу — на екрані нічого, удару немає");
            Assert.AreEqual(RingCue.Slam, RingSoundMap.ForEvent(MatchEventKind.Reverse, MatchActionId.Throw), "без кидка — кидок");
        }

        [Test]
        public void Exchange_AfterPartialReverse_IsNotASecondLockup()
        {
            // Рішення ПМ 08.11.2026 (docs/ring/F2_DIRECTOR.md п. 7, гілка claude/ring-combat-overhaul): реверс 7–9 уже показує обмін
            // захватами (Lockup_Exchange); Exchange, що ядро видає слідом у тому ж виклику, лише вирішує ініціативу — другого Lockup
            // немає. Ініціатива й гравцю, й супернику.
            foreach (var dice in new[] { new[] { 8, 2, 12 }, new[] { 8, 12, 2 } })
            {
                var e = MatchKit.Engine(true, dice);
                e.Perform(MatchActionId.Throw);
                e.React(ReactionChoice.Reverse);
                var log = e.State.Log;
                Assert.AreEqual(MatchEventKind.Exchange, log[0].Kind, "журнал починається обміном на старті");
                Assert.AreEqual(RingCue.Lockup, RingSoundMap.ForEvent(log[0], RingProp.Chair, null), "обмін на старті — зчіпка");
                int r = log.ToList().FindIndex(x => x.Kind == MatchEventKind.Reverse);
                Assert.Greater(r, 0);
                Assert.AreEqual(RollTier.Partial, log[r].Tier);
                Assert.AreEqual(MatchEventKind.Exchange, log[r + 1].Kind, "ядро видає обмін одразу за реверсом 7–9");
                Assert.AreEqual(RingCue.Lockup, RingSoundMap.ForEvent(log[r], RingProp.Chair, log[r - 1]), "реверс 7–9 — обмін захватами");
                Assert.AreEqual(RingCue.CrowdBed, RingSoundMap.ForEvent(log[r + 1], RingProp.Chair, log[r]), "обмін слідом — без другого Lockup");
                int lockups = 0;
                for (int i = r; i < log.Count; i++)
                    if (RingSoundMap.ForEvent(log[i], RingProp.Chair, log[i - 1]) == RingCue.Lockup) lockups++;
                Assert.AreEqual(1, lockups, "від реверсу до кінця виклику — одна зчіпка");
                // Те саме через слід подій, як режисер подає журнал у RingSound.OnEvent (обмін на старті — до відкриття екрана).
                var trail = new RingSoundTrail();
                var heard = new List<RingCue>();
                for (int i = 1; i < log.Count; i++) heard.Add(trail.Next(log[i]));
                Assert.AreEqual(1, heard.Count(c => c == RingCue.Lockup), "слід: одна зчіпка — на реверсі");
                Assert.AreEqual(RingCue.CrowdBed, heard[r], "слід: обмін слідом за реверсом — гул залу");
            }

            // Лише відразу за реверсом 7–9: інший ступінь, пропуск у журналі чи немає попередньої — звичайний обмін.
            Func<RollTier, int, MatchEvent> rev = (t, seq) => new MatchEvent { Seq = seq, Kind = MatchEventKind.Reverse, Action = MatchActionId.Throw, HasRoll = true, Tier = t };
            var ex = new MatchEvent { Seq = 6, Kind = MatchEventKind.Exchange, Action = MatchActionId.CatchBreath, HasRoll = true };
            Assert.IsTrue(RingSoundMap.IsExchangeAfterPartialReverse(ex, rev(RollTier.Partial, 5)));
            Assert.AreEqual(RingCue.Lockup, RingSoundMap.ForEvent(ex, RingProp.Chair, rev(RollTier.Partial, 4)), "не відразу за реверсом");
            Assert.AreEqual(RingCue.Lockup, RingSoundMap.ForEvent(ex, RingProp.Chair, rev(RollTier.Strong, 5)));
            Assert.AreEqual(RingCue.Lockup, RingSoundMap.ForEvent(ex, RingProp.Chair, rev(RollTier.Miss, 5)));
            Assert.AreEqual(RingCue.Lockup, RingSoundMap.ForEvent(ex, RingProp.Chair, null));
            var noRoll = rev(RollTier.Partial, 5); noRoll.HasRoll = false;
            Assert.AreEqual(RingCue.Lockup, RingSoundMap.ForEvent(ex, RingProp.Chair, noRoll), "реверс без кидка — ступінь не читається");
            var act = Act(MatchActionId.Throw, RollTier.Partial); act.Seq = 5;
            Assert.AreEqual(RingCue.Lockup, RingSoundMap.ForEvent(ex, RingProp.Chair, act), "перед обміном дія 7–9, а не реверс");
            // попередня подія не міняє звуку інших подій
            Assert.AreEqual(RingCue.Lockup, RingSoundMap.ForEvent(rev(RollTier.Partial, 6), RingProp.Chair, rev(RollTier.Partial, 5)));
            Assert.AreEqual(RingCue.Hit, RingSoundMap.ForEvent(Act(MatchActionId.Strike, RollTier.Strong), RingProp.Chair, rev(RollTier.Partial, -1)));
            Assert.AreEqual(RingCue.None, RingSoundMap.ForEvent(null, RingProp.Chair, rev(RollTier.Partial, 5)));
        }

        [Test]
        public void Exchange_AfterPartialReverse_InWholeMatches()
        {
            // Повні матчі (ШІ за обидва боки, справжній кубик) крізь слід подій — так, як режисер подає журнал у RingSound.OnEvent:
            // з події після відкриття екрана (обмін на старті вже показаний), без Kickout і MatchEnd (чинний режисер) і з ними (Ф2).
            // Кожен обмін одразу за реверсом 7–9 — без другого Lockup; будь-який інший обмін — зчіпка.
            int pairs = 0;
            for (int seed = 1; seed <= 200; seed++)
            {
                var e = new MatchEngine(MatchKit.Setup(), new SeededDiceRoller((ulong)seed));
                MatchAutoplay.Run(e, true);
                var log = e.State.Log;
                var now = new RingSoundTrail();
                var f2 = new RingSoundTrail();
                for (int i = 1; i < log.Count; i++)
                {
                    var ev = log[i];
                    var withAll = f2.Next(ev);
                    bool fed = ev.Kind != MatchEventKind.Kickout && ev.Kind != MatchEventKind.MatchEnd;
                    var heard = fed ? now.Next(ev) : withAll;
                    if (ev.Kind != MatchEventKind.Exchange) continue;
                    var prev = log[i - 1];
                    bool after = prev.Kind == MatchEventKind.Reverse && prev.HasRoll && prev.Tier == RollTier.Partial;
                    var want = after ? RingCue.CrowdBed : RingCue.Lockup;
                    if (after) pairs++;
                    Assert.AreEqual(want, heard, "seed " + seed + ", подія " + i + ": чинний режисер");
                    Assert.AreEqual(want, withAll, "seed " + seed + ", подія " + i + ": режисер Ф2");
                }
            }
            Assert.Greater(pairs, 0, "у прогоні є реверси 7–9 з обміном слідом");
        }

        [Test]
        public void HoldFinisher_SoundsLikeAHold()
        {
            // Фінішер-больовий на екрані — захват (iron_clamp — Chain_Headlock_Crank): скрип захвату, а не падіння.
            Assert.AreEqual(RingCue.HoldStrain, RingSoundMap.ForEvent(Act(MatchActionId.Finisher, RollTier.Strong, false, "iron_clamp")));
            Assert.AreEqual(RingCue.HoldStrain, RingSoundMap.ForEvent(Act(MatchActionId.Finisher, RollTier.Partial, false, "iron_clamp")));
            Assert.AreEqual(RingCue.Slam, RingSoundMap.ForEvent(Act(MatchActionId.Finisher, RollTier.Miss, false, "iron_clamp")), "провал — падіння (Bump_Back)");
            Assert.AreEqual(RingCue.Slam, RingSoundMap.ForEvent(Act(MatchActionId.Finisher, RollTier.Strong, false, "crusher")));
            Assert.AreEqual(RingCue.Slam, RingSoundMap.ForEvent(Act(MatchActionId.Finisher, RollTier.Strong)));
            // набір звірено з каталогом прийомів: кожен фінішер категорії «больовий» — і лише він
            var catalog = MoveCatalog.Load(CareerKit.Read("career_moves.json"));
            var expected = catalog.All.Where(m => m.Action == MatchActionId.Finisher && m.Category == MoveCategory.Submission).Select(m => m.Id).OrderBy(s => s).ToList();
            CollectionAssert.AreEqual(expected, RingSoundMap.HoldFinishers.OrderBy(s => s).ToList(), "фінішери-больові каталогу й таблиці звуку");
        }

        // Що показує кліп провалу → який звук (RingClipTable). Новий кліп провалу без рядка тут — червоний тест: спершу вирішити звук.
        private static readonly Dictionary<string, RingCue> MissClipSound = new Dictionary<string, RingCue>
        {
            { "Center_Sidestep", RingCue.Whiff },          // ухил: контакту немає
            { "Lockup_Stalemate", RingCue.Lockup },        // нічия в захваті
            { "Bump_Back", RingCue.Slam },                 // падіння
            { "Idle_Hurt_Body_Loop", RingCue.CrowdBed },   // борцю боляче: ні удару, ні оплесків
        };

        [Test]
        public void Misses_FollowTheClipTable()
        {
            // Кожна дія з провалом: звук відповідає останньому кліпу пари чи соло з RingClipTable.Plan.
            foreach (MatchActionId a in Enum.GetValues(typeof(MatchActionId)))
            {
                var ev = Act(a, RollTier.Miss);
                StageStep last = null;
                foreach (var st in RingClipTable.Plan(ev))
                    if (st.Kind == StageStepKind.Pair || st.Kind == StageStepKind.Solo) last = st;
                if (last == null) continue;                       // кроків пари немає (утримання — у Kickout, союзник — рефері)
                var cue = RingSoundMap.ForEvent(ev);
                if (last.Clip == "Idle_Tired_Loop")
                {
                    // той самий кліп, що в «відновитися» — і звук той самий
                    Assert.AreEqual(RingSoundMap.ForEvent(Act(MatchActionId.Recover, RollTier.Strong)), cue, a + ": " + last.Clip);
                    continue;
                }
                RingCue want;
                Assert.IsTrue(MissClipSound.TryGetValue(last.Clip, out want), a + ": новий кліп провалу «" + last.Clip + "» — вирішити його звук");
                // Предмет ≤6 — за порядком ПМ: замах повз (Whiff), падіння в бар'єр — другим дотиком на мітці кліпа.
                if (a == MatchActionId.Weapon) { Assert.AreEqual(RingCue.Whiff, cue); Assert.AreEqual(RingCue.BarrierSlam, RingSoundMap.FollowUpOf(ev)); continue; }
                Assert.AreEqual(want, cue, a + ": провал показує «" + last.Clip + "»");
            }
            // реверс: той самий принцип за ступенями
            foreach (var a in new[] { MatchActionId.Throw, MatchActionId.TopRope, MatchActionId.Finisher })
                foreach (RollTier t in Enum.GetValues(typeof(RollTier)))
                {
                    var ev = new MatchEvent { Kind = MatchEventKind.Reverse, Action = a, HasRoll = true, Tier = t };
                    var steps = RingClipTable.Plan(ev);
                    var cue = RingSoundMap.ForEvent(ev);
                    if (steps.Count == 0) { Assert.AreEqual(RingCue.CrowdBed, cue, "реверс без кроків — без удару"); continue; }
                    var clip = steps[steps.Count - 1].Clip;
                    Assert.AreEqual(clip == "Bodyslam" ? RingCue.Slam : RingCue.Lockup, cue, "реверс " + t + ": " + clip);
                }
        }

        [Test]
        public void Flourish_IsAQteHit_ByName()
        {
            // Переробка бою (claude/ring-combat-overhaul) додає подію Flourish — вдале QTE; таблиця ключована назвами, тож готова до злиття.
            Assert.IsTrue(RingSoundMap.HasKind("Flourish"));
            foreach (MatchEventKind k in Enum.GetValues(typeof(MatchEventKind)))
                Assert.IsTrue(RingSoundMap.HasKind(k.ToString()), "вид події без рядка таблиці: " + k);
        }

        [Test]
        public void Sell_DoesNotRepeatTheHit()
        {
            // Продаж іде ДО кидка прийому: удар звучить на самій дії, а не двічі (і не до контакту).
            Assert.AreEqual(RingCue.CrowdBed, RingSoundMap.ForEvent(MatchEventKind.Sell, MatchActionId.Strike));
            Assert.AreEqual(RingCue.Hit, RingSoundMap.ForEvent(MatchEventKind.Action, MatchActionId.Strike));
        }

        private static HashSet<string> SceneClips()
        {
            var set = new HashSet<string>();
            string dir = Path.Combine(Root(), "Assets", "ThirdParty", "CC0", "Audio", "Ring");
            foreach (var f in Directory.GetFiles(dir))
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext == ".wav" || ext == ".ogg" || ext == ".mp3") set.Add(Path.GetFileNameWithoutExtension(f));
            }
            return set;
        }

        [Test]
        public void Slots_AreSceneClipsOrNewLibrarySlots()
        {
            var scene = SceneClips();
            string builder = File.ReadAllText(Path.Combine(Root(), "tools", "ring_audio", "library", "build_library.py"));
            var bad = new List<string>();
            foreach (var c in RingSoundMap.AllCues())
                foreach (var s in RingSoundMap.SlotsOf(c))
                {
                    bool isNew = RingSoundMap.NewSlots.Contains(s);
                    if (scene.Contains(s) == isNew) bad.Add(s + (isNew ? " — новий слот, але файл є в наборі сцени" : " — немає файлу в наборі сцени"));
                    if (!builder.Contains("\"" + s + "\"") && !builder.Contains("'" + s + "'")) bad.Add(s + " — слота немає в бібліотеці звуку (build_library.py)");
                }
            foreach (var s in RingSoundMap.NewSlots)
                if (!RingSoundMap.AllCues().Any(c => RingSoundMap.SlotsOf(c).Contains(s))) bad.Add(s + " — новий слот не грає жодна подія");
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        [Test]
        public void EventTableDocument_ListsEveryRow()
        {
            string doc = File.ReadAllText(Path.Combine(Root(), "docs", "ring", "audio", "EVENT_SOUND.md"));
            var missing = new List<string>();
            foreach (MatchEventKind k in Enum.GetValues(typeof(MatchEventKind)))
                if (!doc.Contains("`" + k + "`")) missing.Add("подія " + k);
            foreach (MatchActionId a in Enum.GetValues(typeof(MatchActionId)))
                if (!doc.Contains("`" + a + "`")) missing.Add("дія " + a);
            foreach (MatchEffectKind e in Enum.GetValues(typeof(MatchEffectKind)))
                if (RingSoundMap.ForEffect(e) != RingCue.None && !doc.Contains("`" + e + "`")) missing.Add("наслідок " + e);
            // переробка бою: назви, яких у основному ядрі ще немає
            foreach (var n in new[] { "Weapon", "Place", "Count", "Outside", "DoubleCountOut", "Flourish" })
                if (!doc.Contains("`" + n + "`")) missing.Add("переробка бою: " + n);
            foreach (var c in RingSoundMap.AllCues())
                foreach (var s in RingSoundMap.SlotsOf(c))
                    if (!doc.Contains(s)) missing.Add("слот " + s);
            Assert.IsEmpty(missing, "docs/ring/audio/EVENT_SOUND.md відстає від RingSoundMap: " + string.Join(", ", missing));
        }

        private static readonly Regex Field = new Regex(@"^\s*(\w+): ([^\r\n]*)", RegexOptions.Multiline);

        private static Dictionary<string, string> Meta(string path)
        {
            var d = new Dictionary<string, string>();
            foreach (Match m in Field.Matches(File.ReadAllText(path)))
                if (!d.ContainsKey(m.Groups[1].Value)) d[m.Groups[1].Value] = m.Groups[2].Value.Trim();
            return d;
        }

        private static string Get(Dictionary<string, string> m, string key) { string v; return m.TryGetValue(key, out v) ? v : null; }

        [Test]
        public void RingSound_IsImportedMono()
        {
            var bad = new List<string>();
            int n = 0;
            foreach (var rel in new[] { "Assets/ThirdParty/CC0/Audio/Ring", "Assets/Art/Ring/Sound" })
            {
                string dir = Path.Combine(Root(), rel);
                if (!Directory.Exists(dir)) continue;
                foreach (var f in Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories))
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext != ".wav" && ext != ".ogg" && ext != ".mp3") continue;
                    n++;
                    string name = Path.GetFileNameWithoutExtension(f);
                    if (!File.Exists(f + ".meta")) { bad.Add(name + ": немає .meta"); continue; }
                    var m = Meta(f + ".meta");
                    bool music = name.StartsWith("music_"), bed = name == "crowd_shouting" || name == "applause_hall";
                    string load = music ? "2" : bed ? "1" : "0";
                    if (Get(m, "forceToMono") != "1") bad.Add(name + ": не моно");
                    if (Get(m, "loadType") != load) bad.Add(name + ": loadType " + Get(m, "loadType") + " замість " + load);
                    if (Get(m, "compressionFormat") != "1") bad.Add(name + ": не Vorbis");
                    if (!music && Get(m, "preloadAudioData") != "1") bad.Add(name + ": короткий звук без попереднього завантаження (затримка на першому контакті)");
                }
            }
            Assert.GreaterOrEqual(n, 20, "звук рингу знайдено");
            Assert.IsEmpty(bad, "імпорт звуку рингу розійшовся з Editor/RingSoundImport:\n" + string.Join("\n", bad));
            string src = File.ReadAllText(Path.Combine(Root(), "Assets", "_Project", "Scripts", "Editor", "RingSoundImport.cs"));
            StringAssert.Contains("forceToMono = true", src, "правило імпорту рингу — моно");
        }
    }
}
