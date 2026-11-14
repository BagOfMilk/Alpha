using Game.Gameplay.Ring.Stage;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Геометрія вимірювача контакту (QA, критерій М1 №4 «взаємопроникнення &lt; 3 см»): якщо відстань між осями капсул
    /// рахується неправильно, вочер або мовчить про тіло в тілі, або кричить на повітря.
    /// </summary>
    public sealed class RingCapsuleTests
    {
        private static CapPoint P(double x, double y, double z) { return new CapPoint(x, y, z); }

        [Test]
        public void Parallel_Segments_DistanceIsGap()
        {
            Assert.AreEqual(0.5, RingCapsules.SegmentDistance(P(0, 0, 0), P(0, 1, 0), P(0.5, 0, 0), P(0.5, 1, 0)), 1e-9);
        }

        [Test]
        public void Crossing_Segments_DistanceIsZero()
        {
            Assert.AreEqual(0.0, RingCapsules.SegmentDistance(P(-1, 0, 0), P(1, 0, 0), P(0, -1, 0), P(0, 1, 0)), 1e-9);
        }

        [Test]
        public void Skew_Segments_DistanceIsOffsetAlongThirdAxis()
        {
            Assert.AreEqual(0.3, RingCapsules.SegmentDistance(P(-1, 0, 0), P(1, 0, 0), P(0, -1, 0.3), P(0, 1, 0.3)), 1e-9);
        }

        [Test]
        public void Endpoints_AreClamped_NotInfiniteLines()
        {
            // Прямі перетинаються в (2,0,0), але відрізки закінчуються раніше: найближчі кінці (1,0,0) і (2,-1,0).
            double d = RingCapsules.SegmentDistance(P(0, 0, 0), P(1, 0, 0), P(2, -1, 0), P(2, -3, 0));
            Assert.AreEqual(System.Math.Sqrt(2), d, 1e-9);
        }

        [Test]
        public void Degenerate_PointSegment_Works()
        {
            Assert.AreEqual(0.2, RingCapsules.PointSegmentDistance(P(0.5, 0.2, 0), P(0, 0, 0), P(1, 0, 0)), 1e-9);
            Assert.AreEqual(1.0, RingCapsules.SegmentDistance(P(0, 0, 0), P(0, 0, 0), P(0, 1, 0), P(0, 1, 0)), 1e-9);
        }

        [Test]
        public void Penetration_PositiveWhenOverlapping_NegativeWhenGap()
        {
            // Два «передпліччя» радіусом 5 см, осі за 6 см — проникнення 4 см (понад поріг 3 см).
            Assert.AreEqual(0.04, RingCapsules.Penetration(P(0, 0, 0), P(0, 0.3, 0), 0.05, P(0.06, 0, 0), P(0.06, 0.3, 0), 0.05), 1e-9);
            // Осі за 15 см — зазор 5 см.
            Assert.AreEqual(-0.05, RingCapsules.Penetration(P(0, 0, 0), P(0, 0.3, 0), 0.05, P(0.15, 0, 0), P(0.15, 0.3, 0), 0.05), 1e-9);
        }
    }
}
