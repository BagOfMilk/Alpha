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
            Assert.AreEqual(RingCue.Slam, RingSoundMap.ForEvent("Action", "Weapon", true, RollTier.Miss, true, RingProp.Table),
                "≤6 — нападник падає сам (Bump_Back); замах повз і бар'єр — з кліпом Outside_Weapon_Miss");
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
            foreach (var a in new[] { "Throw", "Finisher" })
                Assert.AreEqual(RingCue.Slam, RingSoundMap.ForEvent("Action", a, true, RollTier.Miss, false), a + ": падіння");
            // промінь надії не підводить цілком: провал — як 7–9 (суперник лягає)
            Assert.AreEqual(RingSoundMap.ForEvent("Action", "HopeSpot", true, RollTier.Partial, false),
                RingSoundMap.ForEvent("Action", "HopeSpot", true, RollTier.Miss, false), "надія: провал звучить як 7–9");
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
        public void SecondTouch_ByEvent()
        {
            // Другий дотик режисер грає на мітці touch_2 кліпа: що звучить — подія, де — мітка (без неї тиша).
            // Предмет: 7–9 — нападник зачепив себе; ≤6 — поки падає сам на такті (Bump_Back), бар'єр — з кліпом Outside_Weapon_Miss.
            Assert.AreEqual(RingCue.Hit, RingSoundMap.FollowUpOf("Action", "Weapon", true, RollTier.Partial));
            Assert.AreEqual(RingCue.None, RingSoundMap.FollowUpOf("Action", "Weapon", true, RollTier.Miss), "≤6 — без другого дотику (знахідка 6а)");
            Assert.AreEqual(RingCue.None, RingSoundMap.FollowUpOf("Action", "Weapon", true, RollTier.Strong));
            // Стрибок із каната на того, хто стоїть: перехоплення — такт, падіння — другий дотик (знахідка 1).
            Assert.AreEqual(RingCue.Land, RingSoundMap.FollowUpOf("Action", "TopRope", true, RollTier.Partial), "7–9: обоє падають (Springboard_Land_Catch)");
            Assert.AreEqual(RingCue.Slam, RingSoundMap.FollowUpOf("Action", "TopRope", true, RollTier.Miss), "провал: нападника кинули (Springboard_Land_Counter)");
            Assert.AreEqual(RingCue.None, RingSoundMap.FollowUpOf("Action", "TopRope", true, RollTier.Strong), "10+: приземлення на суперника — сам такт");
            // Таран: удар плечем — такт, падіння суперника — другий дотик (знахідка 6б); провал відчайдушного — падає сам на такті.
            foreach (var t in new[] { RollTier.Strong, RollTier.Partial })
                Assert.AreEqual(RingCue.Slam, RingSoundMap.FollowUpOf("Action", "Desperate", true, t), "відчайдушний " + t);
            Assert.AreEqual(RingCue.None, RingSoundMap.FollowUpOf("Action", "Desperate", true, RollTier.Miss));
            foreach (RollTier t in Enum.GetValues(typeof(RollTier)))
                Assert.AreEqual(RingCue.Slam, RingSoundMap.FollowUpOf("Action", "HopeSpot", true, t), "промінь надії " + t + " (провал — план 7–9)");
            // Фінішер-удар: ласо — такт, падіння — другий дотик (знахідка 6в); інші фінішери — падіння на самому такті.
            foreach (var t in new[] { RollTier.Strong, RollTier.Partial })
                Assert.AreEqual(RingCue.Slam, RingSoundMap.FollowUpOf(Act(MatchActionId.Finisher, t, false, "grand_finale")), "grand_finale " + t);
            Assert.AreEqual(RingCue.None, RingSoundMap.FollowUpOf(Act(MatchActionId.Finisher, RollTier.Miss, false, "grand_finale")), "провал — Bump_Back");
            Assert.AreEqual(RingCue.None, RingSoundMap.FollowUpOf(Act(MatchActionId.Finisher, RollTier.Strong, false, "crusher")));
            Assert.AreEqual(RingCue.None, RingSoundMap.FollowUpOf(Act(MatchActionId.Finisher, RollTier.Strong)));
            Assert.AreEqual(RingCue.None, RingSoundMap.FollowUpOf("Action", "Weapon", false, RollTier.Partial), "без кидка ступінь не читається");
            Assert.AreEqual(RingCue.None, RingSoundMap.FollowUpOf("Sell", "HopeSpot", true, RollTier.Partial), "продаж — не дія");
            Assert.AreEqual(RingCue.None, RingSoundMap.FollowUpOf("Action", "Strike", true, RollTier.Miss));
            Assert.AreEqual(RingCue.None, RingSoundMap.FollowUpOf(null));
            Assert.AreEqual(RingCue.None, RingSoundMap.FollowUpOf(new MatchEvent()), "порожня подія — без другого дотику");
        }

        [Test]
        public void SecondTouch_NeverRepeatsTheBeatOfTheSameEvent()
        {
            // Режисер кличе FollowUp на старті кроку з затримкою до мітки, а звук вирішується в мить звуку — такт уже прозвучав.
            var trail = new RingSoundTrail();
            int seq = 10;
            Func<MatchActionId, RollTier, MatchEvent> ev = (a, t) => { var e = Act(a, t); e.Seq = seq++; return e; };
            // кліп такту від режисера: перехоплення стрибка — удар, падіння — другим дотиком
            var catchEv = ev(MatchActionId.TopRope, RollTier.Partial);
            Assert.AreEqual(RingCue.Hit, trail.Next(catchEv, RingProp.Chair, "Springboard_Land_Catch"), "перехоплення — удар тіла в тіло");
            Assert.AreEqual(RingCue.Land, trail.SecondTouch(catchEv), "падіння обох після перехоплення");
            var counterEv = ev(MatchActionId.TopRope, RollTier.Miss);
            Assert.AreEqual(RingCue.Hit, trail.Next(counterEv, RingProp.Chair, "Springboard_Land_Counter_A"), "половина пари — та сама база");
            Assert.AreEqual(RingCue.Slam, trail.SecondTouch(counterEv));
            // без кліпа такту (режисер ще не подає) — старий звук на перехопленні, і другий дотик його не дублює
            var oldCatch = ev(MatchActionId.TopRope, RollTier.Partial);
            Assert.AreEqual(RingCue.Land, trail.Next(oldCatch));
            Assert.AreEqual(RingCue.None, trail.SecondTouch(oldCatch), "два Land на одній події — ні");
            var oldCounter = ev(MatchActionId.TopRope, RollTier.Miss);
            Assert.AreEqual(RingCue.Slam, trail.Next(oldCounter));
            // між тактом і міткою режисер встигає подати наступні події — пам'ять тримає кілька тактів
            trail.Next(ev(MatchActionId.Strike, RollTier.Strong));
            trail.Next(ev(MatchActionId.Taunt, RollTier.Strong));
            Assert.AreEqual(RingCue.None, trail.SecondTouch(oldCounter), "два Slam на одній події — ні (застереження ПМ)");
            // різні звуки — обидва
            var weapon = ev(MatchActionId.Weapon, RollTier.Partial);
            Assert.AreEqual(RingCue.WeaponChair, trail.Next(weapon));
            Assert.AreEqual(RingCue.Hit, trail.SecondTouch(weapon));
            var hope = ev(MatchActionId.HopeSpot, RollTier.Miss);
            Assert.AreEqual(RingCue.Hit, trail.Next(hope), "промінь надії: удар плечем");
            Assert.AreEqual(RingCue.Slam, trail.SecondTouch(hope), "суперник падає");
            // такту ще не було — другий дотик грає; копія події з тим самим номером — та сама подія
            var lone = ev(MatchActionId.Desperate, RollTier.Strong);
            Assert.AreEqual(RingCue.Slam, trail.SecondTouch(lone));
            var copy = Act(MatchActionId.TopRope, RollTier.Partial);
            copy.Seq = oldCatch.Seq;
            Assert.AreEqual(RingCue.None, trail.SecondTouch(copy));
            // кліп перехоплення міняє лише звук дії
            var sell = new MatchEvent { Kind = MatchEventKind.Sell, Action = MatchActionId.TopRope, HasRoll = true, Tier = RollTier.Partial, Seq = seq++ };
            Assert.AreEqual(RingCue.CrowdBed, RingSoundMap.ForEvent(sell, RingProp.Chair, null, "Springboard_Land_Catch"));
            Assert.AreEqual(RingCue.Land, RingSoundMap.ForEvent(Act(MatchActionId.TopRope, RollTier.Partial), RingProp.Chair, null, "Land_Swallow_Knees"),
                "на лежачого — пірнання, такт на приземленні");
            Assert.IsFalse(RingSoundMap.IsCatchBeat(null));
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
            // за рингом кидок захисника — як дія Throw там: у бар'єр; обмін захватами — той самий
            Assert.AreEqual(RingCue.BarrierSlam, RingSoundMap.ForEvent("Reverse", "Throw", true, RollTier.Strong, true));
            Assert.AreEqual(RingCue.Lockup, RingSoundMap.ForEvent("Reverse", "Throw", true, RollTier.Partial, true));
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

        [Test]
        public void AerialFinisher_SoundsLikeALanding()
        {
            // Повітряний фінішер (meteor, з кута): стрибок Air_Meteor → Land_Meteor (Ф2 крок 7) — звук як у стрибка з канатів (TopRope).
            foreach (var t in new[] { RollTier.Strong, RollTier.Partial })
            {
                Assert.AreEqual(RingCue.Land, RingSoundMap.ForEvent(Act(MatchActionId.Finisher, t, false, "meteor")), "meteor " + t);
                Assert.AreEqual(RingSoundMap.ForEvent(Act(MatchActionId.TopRope, t)), RingSoundMap.ForEvent(Act(MatchActionId.Finisher, t, false, "meteor")),
                    "повітряний фінішер звучить як стрибок з канатів");
            }
            Assert.AreEqual(RingCue.Slam, RingSoundMap.ForEvent(Act(MatchActionId.Finisher, RollTier.Miss, false, "meteor")), "провал — падіння (Bump_Back)");
            Assert.AreEqual(RingCue.Hit, RingSoundMap.ForEvent(Act(MatchActionId.Strike, RollTier.Strong, false, "meteor")), "ключ змінює лише фінішер");
            var catalog = MoveCatalog.Load(CareerKit.Read("career_moves.json"));
            var expected = catalog.All.Where(m => m.Action == MatchActionId.Finisher && m.Category == MoveCategory.Aerial).Select(m => m.Id).OrderBy(s => s).ToList();
            CollectionAssert.AreEqual(expected, RingSoundMap.AerialFinishers.OrderBy(s => s).ToList(), "повітряні фінішери каталогу й таблиці звуку");
            // кожен фінішер каталогу має свій звук: больовий — захват, повітряний — приземлення, удар — удар (падіння другим дотиком),
            // решта — падіння суперника
            foreach (var m in catalog.All.Where(x => x.Action == MatchActionId.Finisher))
            {
                var want = m.Category == MoveCategory.Submission ? RingCue.HoldStrain : m.Category == MoveCategory.Aerial ? RingCue.Land
                    : RingSoundMap.StrikeFinishers.Contains(m.Id) ? RingCue.Hit : RingCue.Slam;
                Assert.AreEqual(want, RingSoundMap.ForEvent(Act(MatchActionId.Finisher, RollTier.Strong, false, m.Id)), m.Id + " (" + m.Category + ")");
            }
        }

        // Мітки обох наборів; extra — додаткові рядки до чоловічого (Add не чіпає кліпів, що вже є, тож лише разом із ним).
        private static RingClipEvents Marks(string extra = null)
        {
            string dir = Path.Combine(Root(), "Assets", "Art", "Animations");
            var marks = RingClipEvents.Parse(File.ReadAllText(Path.Combine(dir, "ring_anims_events.txt")) + "\n" + (extra ?? ""));
            marks.Add(File.ReadAllText(Path.Combine(dir, "ring_anims_f_events.txt")));
            return marks;
        }

        // Кінцеві вузли половин пари з маніфесту набору рингу: кліп → (вузол A, вузол B).
        private static Dictionary<string, string[]> EndNodes()
        {
            var d = new Dictionary<string, string[]>();
            string path = Path.Combine(Root(), "Assets", "Art", "Animations", "ring_anims_manifest.txt");
            foreach (var line in File.ReadAllLines(path))
            {
                var f = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (f.Length < 9 || !(f[0].EndsWith("_A") || f[0].EndsWith("_B"))) continue;
                string b = f[0].Substring(0, f[0].Length - 2);
                string[] n;
                if (!d.TryGetValue(b, out n)) d[b] = n = new string[2];
                n[f[0].EndsWith("_A") ? 0 : 1] = f[f.Length - 1];
            }
            return d;
        }

        private static bool Falls(string node) { return node != null && (node.StartsWith("lying") || node.StartsWith("pinned")); }

        // Усі події, які показує таблиця кліпів: дії й прийоми каталогу × ступені × місце пари × лежить суперник, і решта видів подій.
        private static IEnumerable<KeyValuePair<MatchEvent, PlanContext>> AllPlannedEvents()
        {
            var catalog = MoveCatalog.Load(CareerKit.Read("career_moves.json"));
            var ctxs = new[] { default(PlanContext), new PlanContext { VictimDown = true } };
            foreach (var c in ctxs)
                foreach (RollTier t in Enum.GetValues(typeof(RollTier)))
                    foreach (var o in new[] { false, true })
                    {
                        foreach (MatchActionId a in Enum.GetValues(typeof(MatchActionId)))
                            yield return new KeyValuePair<MatchEvent, PlanContext>(new MatchEvent { Kind = MatchEventKind.Action, Actor = MatchSide.Player,
                                Action = a, HasRoll = true, Tier = t, Outside = o }, c);
                        foreach (var m in catalog.All)
                            yield return new KeyValuePair<MatchEvent, PlanContext>(new MatchEvent { Kind = MatchEventKind.Action, Actor = MatchSide.Player,
                                Action = m.Action, HasRoll = true, Tier = t, MoveKey = m.Id, Staging = m.Staging, Outside = o }, c);
                        foreach (MatchEventKind k in Enum.GetValues(typeof(MatchEventKind)))
                            if (k != MatchEventKind.Action)
                                yield return new KeyValuePair<MatchEvent, PlanContext>(new MatchEvent { Kind = k, Actor = MatchSide.Player,
                                    Action = MatchActionId.Throw, HasRoll = true, Tier = t, Outside = o }, c);
                    }
        }

        private static string Title(MatchEvent e, PlanContext c)
        {
            return e.Kind + " " + (e.MoveKey ?? e.Action.ToString()) + " " + e.Tier + (e.Outside ? " за рингом" : "") + (c.VictimDown ? " (лежачий)" : "");
        }

        [Test]
        public void SameBeatClip_SameSound()
        {
            // Знахідки 6а і 6б (09.11.2026): свист предмета ≤6 лягав на падіння Bump_Back, де кожна інша подія звучить падінням;
            // промінь надії — Slam, відчайдушний — Hit на тому самому контакті тарана. Правило: власний кліп (не підстановка «бракує …»)
            // на тому самому такті звучить однаково. За рингом — окремо: там кліп поки той самий, а бар'єр задумано (EVENT_SOUND).
            var marks = Marks();
            var groups = new Dictionary<string, Dictionary<RingCue, string>>();
            foreach (var kv in AllPlannedEvents())
            {
                var e = kv.Key;
                var steps = RingClipTable.Plan(e, kv.Value);
                var bp = RingBeats.For(steps, marks, _ => 0f);
                if (bp.Step < 0 || bp.IsCount || bp.Clip == null) continue;
                var st = steps[bp.Step];
                if (st.Note != null && st.Note.Contains("бракує")) continue;
                string key = bp.Clip + (st.Kind == StageStepKind.Cut ? " [склейка]" : "") + (e.Outside ? " [за рингом]" : "");
                var cue = RingSoundMap.ForEvent(e, RingProp.Chair, null, bp.Clip);
                Dictionary<RingCue, string> g;
                if (!groups.TryGetValue(key, out g)) groups[key] = g = new Dictionary<RingCue, string>();
                if (!g.ContainsKey(cue)) g[cue] = Title(e, kv.Value);
            }
            var bad = groups.Where(g => g.Value.Count > 1)
                .Select(g => g.Key + ": " + string.Join(" / ", g.Value.Select(c => c.Key + " ← " + c.Value))).ToList();
            Assert.IsEmpty(bad, "той самий кліп такту — різні звуки:\n" + string.Join("\n", bad));
            Assert.Greater(groups.Count, 30, "перебір дійшов до кліпів");
            Assert.IsTrue(groups.ContainsKey("Center_ShoulderBlock") && groups["Center_ShoulderBlock"].ContainsKey(RingCue.Hit), "таран — удар");
            Assert.IsTrue(groups.ContainsKey("Bump_Back [за рингом]") && groups["Bump_Back [за рингом]"].Keys.Single() == RingCue.Slam, "падіння за рингом");
        }

        // Мітки touch_2, які зона звуку чекає від «Кліпів» (CLIP_NEEDS §9 і запит звуку 09.11.2026), і звук на них. Час — будь-який
        // після такту: тест перевіряє, ДЕ режисер зіграє другий дотик (крок виконавця з міткою) і ЩО прозвучить.
        // Мітка другого дотику — RingMatchDirector.FollowUpMark (Stage/Unity, у тести не входить).
        private const string Touch2 = "touch_2";

        // Хто падає на другому дотику: половина пари з маніфесту (0 — A, виконавець; 1 — B, суперник), що кінчається лежачи.
        private static readonly Dictionary<string, int> SecondTouchFaller = new Dictionary<string, int>
        {
            { "Springboard_Land_Catch", 1 }, { "Springboard_Land_Counter", 0 }, { "Center_Clothesline_Flip", 1 },
            { "Center_ShoulderBlock", 1 }, { "Center_ShoulderBlock_Heavy", 1 },
        };

        private static readonly Dictionary<string, RingCue> ExpectedSecondTouch = new Dictionary<string, RingCue>
        {
            { "Springboard_Land_Catch", RingCue.Land },     // 7–9: після перехоплення падають обоє (sell 0,897)
            { "Springboard_Land_Counter", RingCue.Slam },   // провал: нападника кинули на мат (другий contact 0,879)
            { "Center_Clothesline_Flip", RingCue.Slam },    // grand_finale: після ласо суперник перевертається й падає
            { "Center_ShoulderBlock", RingCue.Slam },       // таран 7–9 (відчайдушний, промінь надії): суперник падає на спину
            { "Center_ShoulderBlock_Heavy", RingCue.Slam }, // таран 10+
        };

        [Test]
        public void SecondTouch_LandsOnTheFall()
        {
            // Режисер (RingMatchDirector.ArmFollowUp): крок пари чи соло виконавця події, у кліпа якого є мітка touch_2, — другий дотик
            // FollowUpOf. Тест ставить очікувані мітки поверх справжніх і перебирає всі події таблиці кліпів.
            var real = Marks();
            var extra = new System.Text.StringBuilder();
            foreach (var c in ExpectedSecondTouch.Keys)
                if (real.Time(c, Touch2) < 0f) extra.AppendLine(c + " " + Touch2 + " 2.000 - -");
            var marks = Marks(extra.ToString());
            foreach (var c in ExpectedSecondTouch.Keys) Assert.GreaterOrEqual(marks.Time(c, Touch2), 0f, c + ": мітка для перевірки");
            var nodes = EndNodes();
            var reached = new HashSet<string>();
            var bad = new List<string>();
            foreach (var kv in AllPlannedEvents())
            {
                var e = kv.Key;
                if (RingSoundMap.FollowUpOf(e) == RingCue.None) continue;
                var steps = RingClipTable.Plan(e, kv.Value);
                var at = steps.Where(st => (st.Kind == StageStepKind.Pair || st.Kind == StageStepKind.Solo) && st.Actor == e.Actor
                    && marks.Time(RingBeats.MarkBase(st.Clip), Touch2) >= 0f).ToList();
                string title = Title(e, kv.Value);
                if (at.Count == 0) continue;                                  // мітки немає — тиша, а не звук не в такт
                if (at.Count > 1) { bad.Add(title + ": другий дотик двічі (" + string.Join(", ", at.Select(x => x.Clip)) + ")"); continue; }
                string clip = at[0].Clip;
                reached.Add(clip);
                var second = RingSoundMap.FollowUpOf(e);
                RingCue want;
                if (!ExpectedSecondTouch.TryGetValue(clip, out want)) { bad.Add(title + ": мітка touch_2 у «" + clip + "» без рядка ExpectedSecondTouch"); continue; }
                if (second != want) bad.Add(title + ": на «" + clip + "» " + second + ", а чекали " + want);
                var bp = RingBeats.For(steps, marks, _ => 0f);
                var beatCue = RingSoundMap.ForEvent(e, RingProp.Chair, null, bp.Clip);
                if (beatCue == second) bad.Add(title + ": другий дотик повторює такт (" + second + " на «" + bp.Clip + "»)");
                string[] n;
                int who;
                if ((second == RingCue.Slam || second == RingCue.Land)
                    && (!nodes.TryGetValue(clip, out n) || !SecondTouchFaller.TryGetValue(clip, out who) || !Falls(n[who])))
                    bad.Add(title + ": у «" + clip + "» не лягає той, чиє падіння звучить " + second);
            }
            foreach (var c in ExpectedSecondTouch.Keys)
                if (!reached.Contains(c)) bad.Add("«" + c + "»: мітку touch_2 не озвучує жодна подія");
            CollectionAssert.AreEquivalent(ExpectedSecondTouch.Keys, SecondTouchFaller.Keys, "у кожного другого дотику — хто падає");
            // справжні мітки touch_2 (коли «Кліпи» їх додадуть) — кожна має звук: інакше мітка мертва
            foreach (var c in real.Clips)
                if (real.Time(c, Touch2) >= 0f && !reached.Contains(c) && !ExpectedSecondTouch.ContainsKey(c))
                    bad.Add("справжня мітка touch_2 у «" + c + "» — вирішити її звук (FollowUpOf і ExpectedSecondTouch)");
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        [Test]
        public void StrikeFinisher_HitThenFall()
        {
            // grand_finale — власний кліп Center_Clothesline_Flip: такт на ласо (contact 0,676) — удар, суперник падає пізніше — другий
            // дотик Slam (знахідка 6в). Розійдеться кліп у таблиці кліпів — тест червоний: спершу вирішити звук.
            var marks = Marks();
            var catalog = MoveCatalog.Load(CareerKit.Read("career_moves.json"));
            foreach (var key in RingSoundMap.StrikeFinishers)
            {
                var m = catalog.All.SingleOrDefault(x => x.Id == key);
                Assert.IsNotNull(m, key + " — у каталозі прийомів");
                Assert.AreEqual(MatchActionId.Finisher, m.Action, key);
                foreach (var t in new[] { RollTier.Strong, RollTier.Partial })
                {
                    var e = Act(MatchActionId.Finisher, t, false, key);
                    var steps = RingClipTable.Plan(e, default(PlanContext));
                    var bp = RingBeats.For(steps, marks, _ => 0f);
                    Assert.AreEqual("Center_Clothesline_Flip", bp.Clip, key + " " + t + ": такт");
                    Assert.IsTrue(steps[bp.Step].Note == null || !steps[bp.Step].Note.Contains("бракує"), key + ": власний кліп, не підстановка");
                    Assert.AreEqual(RingCue.Hit, RingSoundMap.ForEvent(e), key + " " + t + ": ласо — удар");
                    Assert.AreEqual(RingCue.Slam, RingSoundMap.FollowUpOf(e), key + " " + t + ": падіння — другим дотиком");
                }
                Assert.AreEqual(RingCue.Slam, RingSoundMap.ForEvent(Act(MatchActionId.Finisher, RollTier.Miss, false, key)), key + ": провал — падіння (Bump_Back)");
            }
            Assert.AreEqual(RingCue.Hit, RingSoundMap.ForEvent(Act(MatchActionId.Strike, RollTier.Strong, false, "grand_finale")), "ключ змінює лише фінішер");
        }

        [Test]
        public void Rescue_DoesNotPopTwice()
        {
            // Розрив рахунку напарником: вибух залу дає режисер на розв'язці відліку (CrowdPop, CountBeats); подія Rescue показує лише
            // рефері, якого відволікли (Ref_Flinch), — удару немає, тож гул залу, а не другий Pop (знахідка 5а).
            var ev = new MatchEvent { Kind = MatchEventKind.Rescue, Actor = MatchSide.Player, HasRoll = true, Tier = RollTier.Strong };
            Assert.AreEqual(RingCue.CrowdBed, RingSoundMap.ForEvent(ev));
            var steps = RingClipTable.Plan(ev);
            Assert.IsTrue(steps.All(st => st.Kind == StageStepKind.Ref), "Rescue — лише рефері; вийде напарник (CLIP_NEEDS §7) — дати удар");
        }

        // Що показує кліп провалу → який звук (RingClipTable). Новий кліп провалу без рядка тут — червоний тест: спершу вирішити звук.
        private static readonly Dictionary<string, RingCue> MissClipSound = new Dictionary<string, RingCue>
        {
            { "Center_Sidestep", RingCue.Whiff },          // ухил: контакту немає
            { "Lockup_Stalemate", RingCue.Lockup },        // нічия в захваті
            { "Bump_Back", RingCue.Slam },                 // падіння
            { "Idle_Hurt_Body_Loop", RingCue.CrowdBed },   // борцю боляче: ні удару, ні оплесків
            // Ф2 кроки 5 і 7 (ПМ, 09.11.2026; зона звуку підтвердила того ж дня за мітками кліпів): больовий не взяв — спроба захвату
            // ноги, яку відбили поштовхом стопи (contact 0,916, падіння немає) — той самий Lockup, що мав Lockup_Stalemate; стрибок повз —
            // нападник тулубом об мат (contact 0,463) — той самий Slam.
            { "LegLock_Enter_Fail", RingCue.Lockup },
            { "Land_Swallow_Miss", RingCue.Slam },
            // Прийоми каталогу (ключ MoveKey, KeyedMoves_SoundAtTheBeat): таран із канатів повз — ухил (контакту немає); метеор повз —
            // нападник сам падає на мат; стрибок із каната перехоплено — такт на перехопленні (удар тіла в тіло), а що нападника кинули
            // на мат — другий дотик Slam (SecondTouch_LandsOnTheFall).
            { "Charge_Sidestep", RingCue.Whiff },
            { "Land_Meteor_Miss", RingCue.Slam },
            { "Springboard_Land_Counter", RingCue.Hit },
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
                    if ((st.Kind == StageStepKind.Pair || st.Kind == StageStepKind.Solo) && !st.Aside) last = st;   // крок-наслідок (встає з колін) звуку провалу не несе
                if (last == null) continue;                       // кроків пари немає (утримання — у Kickout, союзник — рефері)
                var cue = RingSoundMap.ForEvent(ev);
                if (last.Clip == "Idle_Tired_Loop")
                {
                    // той самий кліп, що в «відновитися» — і звук той самий
                    Assert.AreEqual(RingSoundMap.ForEvent(Act(MatchActionId.Recover, RollTier.Strong)), cue, a + ": " + last.Clip);
                    continue;
                }
                if (a == MatchActionId.HopeSpot)
                {
                    // Ядро: промінь надії не підводить цілком — провал рахується як 7–9. Таблиця кліпів грає той самий план, що 7–9,
                    // тож і звук той самий. Розійдуться плани — тест червоний: спершу вирішити звук провалу.
                    var partial = Act(a, RollTier.Partial);
                    CollectionAssert.AreEqual(RingClipTable.Plan(partial).Select(s => s.Clip).ToList(),
                        RingClipTable.Plan(ev).Select(s => s.Clip).ToList(), "надія: провал грається планом 7–9");
                    Assert.AreEqual(RingSoundMap.ForEvent(partial), cue, "надія: провал звучить як 7–9");
                    continue;
                }
                RingCue want;
                Assert.IsTrue(MissClipSound.TryGetValue(last.Clip, out want), a + ": новий кліп провалу «" + last.Clip + "» — вирішити його звук");
                Assert.AreEqual(want, cue, a + ": провал показує «" + last.Clip + "»");
                // Нападник падає сам (Bump_Back, зокрема предмет ≤6 — знахідка 6а): падіння — сам такт, другого дотику немає.
                if (last.Clip == "Bump_Back") Assert.AreEqual(RingCue.None, RingSoundMap.FollowUpOf(ev), a + ": падіння вже на такті");
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
        public void KeyedMoves_SoundAtTheBeat()
        {
            // Прийоми каталогу (Ф2 кроки 5 і 7 — власні ланцюги кліпів RingClipTable.PlanMove): звук відповідає кліпу ТАКТУ (RingBeats —
            // перша мітка удару), бо подія звучить саме там. Такт на склейці (удар тілом об канат чи кут, StageStep.Impact): звук місця дає
            // режисер (RopeRebound / CornerHit), подія — канат: гул залу (удару немає), кут: удар тілом. Провал — за таблицею кліпів провалу.
            string dir = Path.Combine(Root(), "Assets", "Art", "Animations");
            var marks = RingClipEvents.Parse(File.ReadAllText(Path.Combine(dir, "ring_anims_events.txt")));
            marks.Add(File.ReadAllText(Path.Combine(dir, "ring_anims_f_events.txt")));
            var catalog = MoveCatalog.Load(CareerKit.Read("career_moves.json"));
            var contexts = new[] { default(PlanContext), new PlanContext { VictimDown = true } };
            int place = 0, misses = 0;
            foreach (var m in catalog.All)
                foreach (RollTier t in Enum.GetValues(typeof(RollTier)))
                    foreach (var ctx in contexts)
                    {
                        var ev = new MatchEvent { Kind = MatchEventKind.Action, Action = m.Action, HasRoll = true, Tier = t, MoveKey = m.Id, Staging = m.Staging };
                        var steps = RingClipTable.Plan(ev, ctx);
                        var beat = RingBeats.For(steps, marks, _ => 0f);
                        if (beat.Step < 0 || beat.IsCount) continue;
                        string at = m.Id + " " + t + (ctx.VictimDown ? " (лежачий)" : "") + ": такт на «" + beat.Clip + "»";
                        // кліп такту — як режисер подаватиме його (запит звуку ПМ, 09.11.2026: BeatNow → OnEvent(..., _plan.Clip)); доки
                        // не подає, перехоплення стрибка звучить старим Land / Slam (SecondTouch_NeverRepeatsTheBeatOfTheSameEvent)
                        var cue = RingSoundMap.ForEvent(ev, RingProp.Chair, null, beat.Clip);
                        var st = steps[beat.Step];
                        if (st.Kind == StageStepKind.Cut)
                        {
                            place++;
                            Assert.IsTrue(st.Impact, at + " — склейка без такту");
                            Assert.AreEqual(st.Perch == MoveStaging.Corner ? RingCue.Hit : RingCue.CrowdBed, cue, at + " (місце " + st.Perch + ")");
                            continue;
                        }
                        if (t != RollTier.Miss) continue;
                        misses++;
                        RingCue want;
                        Assert.IsTrue(MissClipSound.TryGetValue(beat.Clip, out want), at + " — новий кліп провалу, вирішити його звук");
                        Assert.AreEqual(want, cue, at);
                    }
            Assert.Greater(place, 0, "є такти на канаті й у куті");
            Assert.Greater(misses, 0, "є провали прийомів каталогу");
            // 10+ «до канатів» — такт не на канаті, а на зустрічному ударі в центрі (Counter_*): удар
            Assert.AreEqual(RingCue.Hit, RingSoundMap.ForEvent(Act(MatchActionId.Strike, RollTier.Strong, false, "whip")), "whip 10+ — зустрічний удар");
            // за рингом канатів і кута немає — план загальний, і звук теж
            Assert.AreEqual(RingSoundMap.ForEvent(Act(MatchActionId.Strike, RollTier.Miss, true)), RingSoundMap.ForEvent(Act(MatchActionId.Strike, RollTier.Miss, true, "corner_drag")));
            Assert.AreEqual(RingSoundMap.ForEvent(Act(MatchActionId.Strike, RollTier.Partial, true)), RingSoundMap.ForEvent(Act(MatchActionId.Strike, RollTier.Partial, true, "whip")));
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
