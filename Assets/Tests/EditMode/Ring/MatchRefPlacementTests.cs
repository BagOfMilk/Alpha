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

        // Відлік (слот 33, 13.11.2026): захисник лежить уздовж X, голова на −0,9, таз на 0, ступні на +0,95; плечі — у (−0,7, 0).
        private const float Reach = 0.62f, KneeFwd = 0.25f, LeanFwd = 0.4f, Clear = 0.35f, LeanClear = 0.2f, Lim = 1.84f;
        private static readonly RefPlacement.Seg DefenderUpper = new RefPlacement.Seg(-0.9f, 0f, 0f, 0f);
        private static readonly RefPlacement.Seg Defender = new RefPlacement.Seg(-0.9f, 0f, 0.95f, 0f);

        [Test]
        public void CoverBody_CrossesTheDefenderChest_AndGoesBeyond()
        {
            // нападник накриває збоку (таз на z = −0,5 біля грудей): тулуб іде через вісь захисника на +0,45 за неї, ноги — назад
            var c = RefPlacement.CoverBody(-0.4f, -0.5f, DefenderUpper, 0.45f, 0.9f);
            Assert.AreEqual(-0.4f, c.Bx, 1e-4f);
            Assert.AreEqual(0.45f, c.Bz, 1e-4f, "голова й плечі — за віссю захисника");
            Assert.AreEqual(-1.4f, c.Az, 1e-4f, "ноги — від захисника");
            // таз точно на осі (сидить верхи) — напрям невідомий, точка
            var p = RefPlacement.CoverBody(-0.4f, 0f, DefenderUpper, 0.45f, 0.9f);
            Assert.AreEqual(p.Ax, p.Bx); Assert.AreEqual(p.Az, p.Bz);
        }

        [Test]
        public void CountSpot_FreeSide_IsBesideTheShoulders()
        {
            // нападник на ногах захисника (далеко від плечей): рефері — перпендикулярно до плечей з протилежного боку
            var bodies = new List<RefPlacement.Seg> { Defender, RefPlacement.CoverBody(0.6f, -0.5f, DefenderUpper, 0.45f, 0.9f) };
            float x, z;
            Assert.IsTrue(RefPlacement.CountSpot(-0.7f, 0f, 0f, 1f, -1f, 0f, bodies, Reach, KneeFwd, LeanFwd, Clear, LeanClear, Lim, out x, out z));
            Assert.AreEqual(-0.7f, x, 1e-4f);
            Assert.AreEqual(Reach, z, 1e-4f);
        }

        [Test]
        public void CountSpot_CoverAcrossTheChest_KeepsTheRefOffTheAttackersHead()
        {
            // накриття поперек грудей (k_ending 127–130 слоту 33): голова нападника виходить на бік рефері. Старе місце
            // (перпендикуляр від плечей, нападник — точка таза) лежало впритул до неї.
            var cover = RefPlacement.CoverBody(-0.55f, -0.5f, DefenderUpper, 0.45f, 0.9f);
            var bodies = new List<RefPlacement.Seg> { Defender, cover };
            Assert.Less(RefPlacement.MinDist(-0.7f, Reach, bodies), Clear, "передумова: старе місце — на голові нападника");

            float x, z;
            Assert.IsTrue(RefPlacement.CountSpot(-0.7f, 0f, 0f, 1f, -1f, 0f, bodies, Reach, KneeFwd, LeanFwd, Clear, LeanClear, Lim, out x, out z));
            Assert.Less(x, -0.7f, "зсув до голови захисника");
            float fx = -0.7f - x, fz = -z, fl = (float)System.Math.Sqrt(fx * fx + fz * fz);
            fx /= fl; fz /= fl;
            Assert.GreaterOrEqual(RefPlacement.MinDist(x, z, bodies), Clear);
            Assert.GreaterOrEqual(RefPlacement.MinDist(x + fx * KneeFwd, z + fz * KneeFwd, bodies), Clear, "коліна");
            Assert.GreaterOrEqual(RefPlacement.MinDist(x + fx * LeanFwd, z + fz * LeanFwd, bodies), LeanClear, "голова в нахилі на долоні");
        }

        [Test]
        public void CountSpot_NoRoom_ReturnsFalse()
        {
            // плечі біля канатів (з того боку — за межею), з другого впритул лежить нападник, зсув до голови — у кут
            var bodies = new List<RefPlacement.Seg> { new RefPlacement.Seg(1.7f, 1.7f, 0.2f, 1.7f), new RefPlacement.Seg(0.2f, 1.1f, 1.7f, 1.1f) };
            float x, z;
            Assert.IsFalse(RefPlacement.CountSpot(1.6f, 1.7f, 0f, 1f, 1f, 0f, bodies, Reach, KneeFwd, LeanFwd, Clear, LeanClear, 1.75f, out x, out z));
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
