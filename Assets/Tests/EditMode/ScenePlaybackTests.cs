using Game.Core.Characters;
using Game.Core.Scenes;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Програвання сцени (Поправка №5.8). Живе в ядрі, тому «хто зараз у
    /// кадрі і що він каже» перевіряється без редактора — рівно так само, як
    /// сама сцена перевіряється без нього валідатором.
    /// </summary>
    public class ScenePlaybackTests
    {
        /// <summary>План тримається, поки його не змінять: інакше той, хто говорить, зникав би після першої фрази.</summary>
        [Test]
        public void ShotHolds_UntilAnotherShotReplacesIt()
        {
            var scene = new Scene("t")
                .Step(SceneStep.Shot("tuhar", ShotFraming.Close))
                .Step(SceneStep.Line("tuhar", "a"))
                .Step(SceneStep.Line("tuhar", "b"))
                .Step(SceneStep.Transition("out"));

            var play = new ScenePlayback(scene);

            play.Next();
            Assert.AreEqual("tuhar", play.Current.ActorId);

            play.Next();
            Assert.AreEqual("tuhar", play.Current.ActorId, "план не стёрся репликой");
            Assert.AreEqual("a", play.Current.LineKey);

            play.Next();
            Assert.AreEqual("tuhar", play.Current.ActorId);
            Assert.AreEqual("b", play.Current.LineKey);
        }

        /// <summary>Репліка — подія: вона не тягнеться в наступний кадр.</summary>
        [Test]
        public void LineLastsExactlyOneStep()
        {
            var scene = new Scene("t")
                .Step(SceneStep.Shot("a", ShotFraming.Close))
                .Step(SceneStep.Line("a", "key"))
                .Step(SceneStep.Beat(1.0))
                .Step(SceneStep.Transition("out"));

            var play = new ScenePlayback(scene);
            play.Next();
            play.Next();
            Assert.AreEqual("key", play.Current.LineKey);

            play.Next();
            Assert.IsNull(play.Current.LineKey, "реплика осталась висеть на паузе");
            Assert.AreEqual(1.0, play.HoldSeconds, 1e-9);
        }

        [Test]
        public void Transition_FinishesAndIsRemembered()
        {
            var scene = new Scene("t")
                .Step(SceneStep.Shot("a", ShotFraming.Close))
                .Step(SceneStep.Transition("to.next"));

            var play = new ScenePlayback(scene);
            Assert.IsTrue(play.Next());
            Assert.IsFalse(play.Next(), "переход заканчивает сцену");
            Assert.IsTrue(play.IsFinished);
            Assert.AreEqual("to.next", play.TransitionKey);
        }

        [Test]
        public void EffectIsReported_Once()
        {
            var scene = new Scene("t")
                .Step(SceneStep.Shot("a", ShotFraming.Close))
                .Step(SceneStep.Effect("sfx.door"))
                .Step(SceneStep.Line("a", "key"))
                .Step(SceneStep.Transition("out"));

            var play = new ScenePlayback(scene);
            play.Next();
            play.Next();
            Assert.AreEqual("sfx.door", play.Current.EffectKey);

            play.Next();
            Assert.IsNull(play.Current.EffectKey, "эффект сработал дважды");
        }

        /// <summary>
        /// Дограє сцену до кінця, на кожному Choice-кроці беручи варіант 0
        /// (Поправка №7.8): <see cref="ScenePlayback.Next"/> сам не рухається
        /// далі вибору (короткочасно повертає той самий кадр, поки не прийде
        /// <see cref="ScenePlayback.Choose"/>) — без цього `while (play.Next())`
        /// висів би вічно, рівно так, як повис би наївний викликач, який не
        /// вміє вибирати (див. коментар у <see cref="ScenePlayback.IsAwaitingChoice"/>).
        /// </summary>
        private static int RunToEnd(ScenePlayback play)
        {
            int steps = 0;
            bool advanced = play.Next();
            while (advanced)
            {
                steps++;
                if (play.IsAwaitingChoice) play.Choose(0);
                advanced = play.Next();
            }
            return steps;
        }

        /// <summary>Сцена відкриття програється цілком і закінчується переходом у вузол.</summary>
        [Test]
        public void OpeningScene_PlaysToItsTransition()
        {
            var play = new ScenePlayback(OpeningScenes.NeighbourWithADemand());

            int steps = RunToEnd(play);

            Assert.Greater(steps, 5, "сцена открытия короче, чем поставлена");
            Assert.IsTrue(play.IsFinished);
            Assert.AreEqual("to.node1.pass", play.TransitionKey,
                "открытие обязано вести в узел первых суток");
        }

        /// <summary>Кожен, хто говорить у відкритті, видимий: у кадрі він або оголошений голосом за кадром.</summary>
        [Test]
        public void EveryOpeningSpeaker_IsOnScreenOrOffScreenByDesign()
        {
            var play = new ScenePlayback(OpeningScenes.NeighbourWithADemand());
            bool advanced = play.Next();
            while (advanced)
            {
                var frame = play.Current;
                bool voiceOver = string.IsNullOrEmpty(frame.SpeakerId) || frame.Framing == ShotFraming.Empty;
                if (!voiceOver)
                    Assert.IsTrue(frame.SpeakerId == frame.ActorId || frame.SpeakerId == frame.SecondActorId,
                        $"говорит {frame.SpeakerId}, а в кадре {frame.ActorId}");

                if (play.IsAwaitingChoice) play.Choose(0);
                advanced = play.Next();
            }
        }
    }
}
