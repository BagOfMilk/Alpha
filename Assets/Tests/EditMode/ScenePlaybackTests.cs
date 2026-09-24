using Game.Core.Characters;
using Game.Core.Scenes;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Проигрывание сцены (Поправка №5.8). Живёт в ядре, поэтому «кто сейчас в
    /// кадре и что он говорит» проверяется без редактора — ровно так же, как
    /// сама сцена проверяется без него валидатором.
    /// </summary>
    public class ScenePlaybackTests
    {
        /// <summary>План держится, пока его не сменят: иначе говорящий исчезал бы после первой фразы.</summary>
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

        /// <summary>Реплика — событие: она не тянется в следующий кадр.</summary>
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
        /// Доигрывает сцену до конца, на каждом Choice-шаге беря вариант 0
        /// (Поправка №7.8): <see cref="ScenePlayback.Next"/> сам не движется
        /// дальше выбора (короткочасно возвращает тот же кадр, пока не придёт
        /// <see cref="ScenePlayback.Choose"/>) — без этого `while (play.Next())`
        /// висел бы вечно, ровно так, как повис бы наивный вызывающий, не
        /// умеющий выбирать (см. комментарий у <see cref="ScenePlayback.IsAwaitingChoice"/>).
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

        /// <summary>Сцена открытия проигрывается целиком и заканчивается переходом в узел.</summary>
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

        /// <summary>Каждый говорящий в открытии виден: в кадре он или объявлен голосом за кадром.</summary>
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
