using System.Collections.Generic;
using Game.Core.Ring;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Канати рингу з фізикою (власник, 29.10.2026: «Канати мають мати фізику»): натягнутий канат майже не провисає,
    /// тіло прогинає його й не проходить крізь нього, після відскоку канат гойдається й заспокоюється, кінці на стійках,
    /// однаковий вхід — однаковий канат. Розміри — ринг 16 футів, середній канат.
    /// </summary>
    public class RopeSimTests
    {
        private static readonly RopeVec Left = new RopeVec(-2.36f, 2.36f, 0.86f);
        private static readonly RopeVec Right = new RopeVec(2.36f, 2.36f, 0.86f);
        private static readonly RopeVec Out = new RopeVec(0f, 1f, 0f);

        private static void Run(RopeSim rope, float seconds, IReadOnlyList<RopeCapsule> bodies = null)
        {
            var steps = (int)(seconds * 60f + 0.5f);
            for (var i = 0; i < steps; i++) rope.Step(bodies);
        }

        // поперек борця (лінія таза) на висоті середнього каната, центр за лінією каната на depth
        private static RopeCapsule Back(float depth) =>
            new RopeCapsule(new RopeVec(-0.2f, 2.36f + depth, 0.88f), new RopeVec(0.2f, 2.36f + depth, 0.88f), 0.14f);

        // спина заходить зсередини рингу (за 0,2 м до каната) за лінію каната на 0,28 м (Move_Rope_Rebound, ROPE_SINK)
        private static void PressBack(RopeSim rope, int steps = 18)
        {
            for (var i = 0; i <= steps; i++) rope.Step(new[] { Back(-0.2f + 0.48f * i / steps) });
        }

        private static float Clearance(RopeSim rope, RopeCapsule c)
        {
            // відрізки каната, а не лише точки: тіло не має пролазити між рідкими точками дешевого режиму
            var best = float.MaxValue;
            var ab = c.B - c.A;
            for (var i = 0; i < rope.Count - 1; i++)
                for (var k = 0; k <= 20; k++)
                {
                    var pt = RopeVec.Lerp(rope[i], rope[i + 1], k / 20f);
                    var t = System.Math.Max(0f, System.Math.Min(1f, RopeVec.Dot(pt - c.A, ab) / RopeVec.Dot(ab, ab)));
                    best = System.Math.Min(best, (pt - (c.A + ab * t)).Length - c.Radius);
                }
            return best;
        }

        [Test]
        public void Idle_TautRope_SagsUnderTwoCentimetres()
        {
            var rope = new RopeSim(Left, Right, Out);
            Run(rope, 3f);
            Assert.That(rope.MaxDeflection(), Is.LessThan(0.02f));
        }

        [Test]
        public void BackIntoRopes_BendsRope_AndNeverPassesThroughBody()
        {
            foreach (var settings in new[] { new RopeSettings(), RopeSettings.Low() })
            {
                var rope = new RopeSim(Left, Right, Out, settings);
                for (var i = 0; i <= 18; i++)
                {
                    var body = new[] { Back(-0.2f + 0.48f * i / 18f) };
                    rope.Step(body);
                    Assert.That(Clearance(rope, body[0]), Is.GreaterThan(-0.005f), "канат пройшов крізь тіло, крок " + i);
                }
                Assert.That(rope.MaxDeflection(), Is.GreaterThan(0.25f), "канат не прогнувся під спиною");
            }
        }

        [Test]
        public void AfterRebound_RopeSwingsAndSettles()
        {
            var rope = new RopeSim(Left, Right, Out);
            PressBack(rope);
            Run(rope, 0.15f, new[] { Back(0.28f) });
            Run(rope, 0.1f);
            Assert.That(rope.MaxDeflection(), Is.GreaterThan(0.03f), "канат має гойднутись після відриву");
            Run(rope, 2.5f);
            Assert.That(rope.MaxDeflection(), Is.LessThan(0.025f), "канат не заспокоївся");
        }

        [Test]
        public void Ends_StayOnPosts()
        {
            var rope = new RopeSim(Left, Right, Out);
            PressBack(rope);
            rope.Hit(0.5f, new RopeVec(0f, 3f, 0f));
            Run(rope, 0.5f);
            Assert.That((rope[0] - Left).Length, Is.LessThan(1e-6f));
            Assert.That((rope[rope.Count - 1] - Right).Length, Is.LessThan(1e-6f));
        }

        [Test]
        public void SameInput_SameRope()
        {
            var a = new RopeSim(Left, Right, Out);
            var b = new RopeSim(Left, Right, Out);
            foreach (var r in new[] { a, b })
            {
                PressBack(r);
                r.Hit(0.3f, new RopeVec(0f, 1.5f, -0.5f));
                Run(r, 0.7f);
            }
            for (var i = 0; i < a.Count; i++)
                Assert.That(a[i].X == b[i].X && a[i].Y == b[i].Y && a[i].Z == b[i].Z, "точка " + i);
        }

        [Test]
        public void Hit_FromClipEvent_PushesRopeOutward()
        {
            var rope = new RopeSim(Left, Right, Out);
            rope.Hit(0.5f, new RopeVec(0f, 2f, 0f));
            Run(rope, 0.1f);
            Assert.That(rope[rope.Count / 2].Y, Is.GreaterThan(Left.Y + 0.03f));
        }
    }
}
