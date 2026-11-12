using System.Collections.Generic;
using Game.Core.Ring.Match;
using NUnit.Framework;

namespace Game.Tests.EditMode.Ring
{
    /// <summary>Місце рефері: не перед парою на кіноплані фінішера й не на тілі на відліку (огляд 8f, 11.11.2026).</summary>
    public class MatchRefPlacementTests
    {
        private static List<RefPlacement.Seg> Pair()
        {
            return new List<RefPlacement.Seg> { new RefPlacement.Seg(-0.4f, 0f, -0.4f, 0f), new RefPlacement.Seg(0.4f, 0f, 0.4f, 0f) };
        }

        [Test]
        public void BehindPair_IsBehindRelativeToCamera_AndClearOfBodies()
        {
            float x, z;
            // камера дивиться вздовж +Z: рефері має бути на +Z від пари
            Assert.IsTrue(RefPlacement.BehindPair(0f, 0f, 0f, 1f, Pair(), 1.75f, 0.85f, out x, out z));
            Assert.GreaterOrEqual(z, RefPlacement.MinBehind);
            Assert.GreaterOrEqual(RefPlacement.MinDist(x, z, Pair()), 0.85f);
        }

        [Test]
        public void BehindPair_SkipsSpotsOnABody()
        {
            // тіло лежить саме там, де стояв би рефері першим вибором (глибина 1,15, збоку 0,9)
            var bodies = Pair();
            bodies.Add(new RefPlacement.Seg(-0.9f, 1.15f, -0.9f, 1.15f));
            bodies.Add(new RefPlacement.Seg(0.9f, 1.15f, 0.9f, 1.15f));
            float x, z;
            Assert.IsTrue(RefPlacement.BehindPair(0f, 0f, 0f, 1f, bodies, 1.75f, 0.85f, out x, out z));
            Assert.GreaterOrEqual(RefPlacement.MinDist(x, z, bodies), 0.85f);
        }

        [Test]
        public void BehindPair_FlipsWithCamera()
        {
            float x, z;
            Assert.IsTrue(RefPlacement.BehindPair(0f, 0f, 0f, -1f, Pair(), 1.75f, 0.85f, out x, out z));
            Assert.LessOrEqual(z, -RefPlacement.MinBehind);
        }

        [Test]
        public void BehindPair_NoRoom_ReturnsFalse()
        {
            float x, z;
            // пара біля канатів з боку «за»: у межах рингу позаду місця немає
            Assert.IsFalse(RefPlacement.BehindPair(0f, 1.6f, 0f, 1f, Pair(), 1.75f, 0.85f, out x, out z));
        }

        [Test]
        public void PushClear_KneeOnThigh_MovesOutOfBody()
        {
            // тіло лежить уздовж X (голова −0,9, ступні +0,9); рефері стоїть на стегні, лицем до голови
            var body = new List<RefPlacement.Seg> { new RefPlacement.Seg(-0.9f, 0f, 0.9f, 0f) };
            float x = 0.5f, z = 0.1f;
            bool ok = RefPlacement.PushClear(ref x, ref z, -1f, 0f, 0.25f, body, 0.35f, 1.75f);
            Assert.IsTrue(ok);
            Assert.GreaterOrEqual(RefPlacement.MinDist(x, z, body), 0.35f - 1e-3f);
            Assert.GreaterOrEqual(RefPlacement.MinDist(x - 0.25f, z, body), 0.35f - 1e-3f);
        }

        [Test]
        public void PushClear_AlreadyClear_DoesNotMove()
        {
            var body = new List<RefPlacement.Seg> { new RefPlacement.Seg(-0.9f, 0f, 0.9f, 0f) };
            float x = 0f, z = 0.7f;
            Assert.IsTrue(RefPlacement.PushClear(ref x, ref z, 0f, -1f, 0.25f, body, 0.35f, 1.75f));
            Assert.AreEqual(0f, x, 1e-5f);
            Assert.AreEqual(0.7f, z, 1e-5f);
        }
    }
}
