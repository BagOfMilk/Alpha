using System.Collections.Generic;
using Game.Gameplay.Ring.Stage;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Живе очікування (власник 14.11.2026: «добавь более живие айдл анимации…»): вставки не наступають на біт, не йдуть
    /// підряд, не повторюють жест, не синхронні в суперників і детерміновані.
    /// </summary>
    public sealed class RingIdleLifePlanTests
    {
        private const float Free = float.PositiveInfinity;

        /// <summary>Стояння довжиною n петель: які вставки зіграли (null — звичайна петля).</summary>
        private static List<string> Stand(int actor, IdleLifeRole role, int gimmick, int n, float window = Free)
        {
            var got = new List<string>();
            int last = -100;
            string lastClip = null;
            for (int loop = 0; loop < n; loop++)
            {
                string c = RingIdleLifePlan.Pick(actor, role, gimmick, loop, last, lastClip, window, true);
                got.Add(c);
                if (c != null) { last = loop; lastClip = c; }
            }
            return got;
        }

        [Test]
        public void NotAllowed_NeverInserts()
        {
            for (int loop = 0; loop < 200; loop++)
                Assert.IsNull(RingIdleLifePlan.Pick(1, IdleLifeRole.Wrestler, 3, loop, -100, null, Free, false));
        }

        [Test]
        public void ShortWindow_NeverInserts()
        {
            float tooShort = RingIdleLifePlan.WrestlerLoop + RingIdleLifePlan.Margin - 0.01f;
            for (int loop = 0; loop < 200; loop++)
            {
                Assert.IsNull(RingIdleLifePlan.Pick(1, IdleLifeRole.Wrestler, 3, loop, -100, null, tooShort, true));
                Assert.IsNull(RingIdleLifePlan.Pick(2, IdleLifeRole.Referee, -1, loop, -100, null, RingIdleLifePlan.RefereeLoop, true));
                Assert.IsNull(RingIdleLifePlan.Pick(2, IdleLifeRole.Referee, -1, loop, -100, null, float.NaN, true));
            }
        }

        [Test]
        public void WindowExactlyLoopPlusMargin_IsEnough()
        {
            float exact = RingIdleLifePlan.WrestlerLoop + RingIdleLifePlan.Margin;
            int got = 0;
            for (int loop = 0; loop < 200; loop++)
                if (RingIdleLifePlan.Pick(1, IdleLifeRole.Wrestler, 3, loop, -100, null, exact, true) != null) got++;
            Assert.AreEqual(200, got, "вікно рівно «вставка + запас» — вставка дозволена (після довгої паузи — напевно)");
        }

        [Test]
        public void FirstBoundaryOfAStand_IsAlwaysAPlainLoop()
        {
            // Виконавець на початку стояння ставить «остання вставка = поточна петля»: на першій межі проміжок 1.
            for (int actor = 0; actor < 3; actor++)
                for (int loop = 0; loop < 300; loop++)
                    Assert.IsNull(RingIdleLifePlan.Pick(actor, IdleLifeRole.Wrestler, -1, loop + 1, loop, null, Free, true));
        }

        [Test]
        public void UnknownGimmick_UsesEqualWeights()
        {
            for (int i = 0; i < RingIdleLifePlan.WrestlerClips.Length; i++)
            {
                Assert.AreEqual(1, RingIdleLifePlan.Weight(IdleLifeRole.Wrestler, -1, i));
                Assert.AreEqual(1, RingIdleLifePlan.Weight(IdleLifeRole.Wrestler, 99, i));
            }
            var seen = new HashSet<string>();
            foreach (var c in Stand(0, IdleLifeRole.Wrestler, 99, 600)) if (c != null) seen.Add(c);
            Assert.AreEqual(RingIdleLifePlan.WrestlerClips.Length, seen.Count, "без ґіміка грають усі жести");
        }

        [Test]
        public void Hash_IsTheSameOnEveryMachine()
        {
            // Еталон пораховано окремо (32-бітна арифметика без переповнень платформи): зміна формули змінить рух у всіх матчах.
            Assert.AreEqual(1055093019u, RingIdleLifePlan.Hash(0, 1));
            Assert.AreEqual(3392854406u, RingIdleLifePlan.Hash(1, 1));
            Assert.AreEqual(3926017556u, RingIdleLifePlan.Hash(2, 7));
            Assert.AreEqual(303560709u, RingIdleLifePlan.Hash(0, 123));
            Assert.AreEqual(967117349u, RingIdleLifePlan.Hash(-1, 5));
        }

        [Test]
        public void BaseLoops_AreWaitingLoopsOfTheIdlePhaseModel()
        {
            // Дві моделі живого очікування (фаза й темп петель — RingIdlePhase; вставки — тут) говорять про ті самі петлі.
            Assert.IsTrue(RingIdlePhase.IsWaitingLoop(RingIdleLifePlan.LoopClip(IdleLifeRole.Wrestler)));
            Assert.IsTrue(RingIdlePhase.IsWaitingLoop(RingIdleLifePlan.LoopClip(IdleLifeRole.Referee)));
        }

        [Test]
        public void NoTwoInsertsInARow_AndNeverSameGestureTwice()
        {
            for (int actor = 0; actor < 3; actor++)
                for (int g = -1; g < 8; g++)
                {
                    var s = Stand(actor, IdleLifeRole.Wrestler, g, 300);
                    string prev = null;
                    for (int i = 1; i < s.Count; i++)
                    {
                        Assert.IsFalse(s[i] != null && s[i - 1] != null, "дві вставки підряд, петля " + i);
                        if (s[i] == null) continue;
                        Assert.AreNotEqual(prev, s[i], "той самий жест двічі поспіль");
                        prev = s[i];
                    }
                }
        }

        [Test]
        public void AfterThreePlainLoops_InsertIsCertain()
        {
            for (int actor = 0; actor < 5; actor++)
                for (int loop = 10; loop < 300; loop++)
                    Assert.IsNotNull(RingIdleLifePlan.Pick(actor, IdleLifeRole.Wrestler, -1, loop, loop - 4, null, Free, true));
        }

        [Test]
        public void Rate_IsLivelyButNotConstant()
        {
            var s = Stand(0, IdleLifeRole.Wrestler, -1, 400);
            int n = 0;
            foreach (var c in s) if (c != null) n++;
            double share = n / 400.0;
            Assert.IsTrue(share > 0.25 && share < 0.5, "частка петель із вставкою " + share);
        }

        [Test]
        public void Deterministic_SameInputsSameResult()
        {
            CollectionAssert.AreEqual(Stand(7, IdleLifeRole.Wrestler, 5, 120), Stand(7, IdleLifeRole.Wrestler, 5, 120));
        }

        [Test]
        public void RivalsAreNotInSync()
        {
            var a = Stand(0, IdleLifeRole.Wrestler, -1, 300);
            var b = Stand(1, IdleLifeRole.Wrestler, -1, 300);
            int same = 0, both = 0;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i] != null && b[i] != null) both++;
                if (a[i] != null && a[i] == b[i]) same++;
            }
            Assert.IsTrue(same < 15, "той самий жест одночасно в обох: " + same);
            Assert.IsTrue(both < 60, "вставки одночасно в обох: " + both);
        }

        [Test]
        public void RoleUsesOwnGestures_AndGimmickWeightsRespected()
        {
            foreach (var c in Stand(2, IdleLifeRole.Referee, -1, 300))
                if (c != null) Assert.That(RingIdleLifePlan.RefereeClips, Has.Member(c));
            var seen = new HashSet<string>();
            foreach (var c in Stand(3, IdleLifeRole.Wrestler, -1, 600))
                if (c != null) { Assert.That(RingIdleLifePlan.WrestlerClips, Has.Member(c)); seen.Add(c); }
            Assert.AreEqual(RingIdleLifePlan.WrestlerClips.Length, seen.Count, "без ґіміка мають траплятися всі жести");
            // Літун (1) не стискає кулаки, Силач (7) не підстрибує — вага 0
            foreach (var c in Stand(4, IdleLifeRole.Wrestler, 1, 600)) Assert.AreNotEqual("Stance_Life_Fists", c);
            foreach (var c in Stand(5, IdleLifeRole.Wrestler, 7, 600)) Assert.AreNotEqual("Stance_Life_Bounce", c);
        }

        [Test]
        public void WeightsTable_MatchesGestureList()
        {
            Assert.AreEqual(8, RingIdleLifePlan.GimmickWeights.Length, "рядок на кожен ґімік кар'єри");
            foreach (var row in RingIdleLifePlan.GimmickWeights)
            {
                Assert.AreEqual(RingIdleLifePlan.WrestlerClips.Length, row.Length);
                int nonZero = 0;
                foreach (var w in row) if (w > 0) nonZero++;
                Assert.IsTrue(nonZero >= 6, "ґіміку лишається щонайменше 6 жестів — інакше видно повтори");
            }
        }
    }
}
