using System.Collections.Generic;
using Game.Core.Characters;
using Game.Core.Scenes;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Формат сцены (Поправка №5.8). Ради этих проверок сцена и сделана
    /// данными: постановку видно до редактора.
    /// </summary>
    public class SceneTests
    {
        private static List<CharacterCard> Cast()
        {
            var cast = OpeningCast.All();
            cast.Add(OpeningScenes.Protagonist());
            return cast;
        }

        [Test]
        public void OpeningScene_IsValid()
        {
            Assert.IsTrue(SceneValidator.IsValid(OpeningScenes.NeighbourWithADemand(), Cast(), out var problem),
                problem);
        }

        [Test]
        public void EveryResolutionScene_IsValid()
        {
            foreach (var key in new[] { "best", "good", "base", "worst" })
                Assert.IsTrue(SceneValidator.IsValid(OpeningScenes.PassResolution(key), Cast(), out var problem),
                    problem);
        }

        /// <summary>Голос из ниоткуда: реплика раньше первого плана.</summary>
        [Test]
        public void LineBeforeAnyShot_IsRefused()
        {
            var scene = new Scene("bad.voice")
                .Step(SceneStep.Line("tuhar", "key"))
                .Step(SceneStep.Transition("out"));

            var problems = SceneValidator.Validate(scene, Cast());
            CollectionAssert.IsNotEmpty(problems);
            StringAssert.Contains("реплика раньше первого плана", string.Join("; ", problems));
        }

        [Test]
        public void ParticipantWithoutCard_IsRefused()
        {
            var scene = new Scene("bad.stranger")
                .Step(SceneStep.Shot("nobody", ShotFraming.Close))
                .Step(SceneStep.Line("nobody", "key"))
                .Step(SceneStep.Transition("out"));

            var problems = SceneValidator.Validate(scene, Cast());
            StringAssert.Contains("без карточки персонажа", string.Join("; ", problems));
        }

        /// <summary>
        /// Говорит тот, кого видно. Голос за кадром — законный приём, но он
        /// объявляется пустым планом, а не получается из забытого.
        /// </summary>
        [Test]
        public void SpeakerOutOfFrame_IsRefused()
        {
            var scene = new Scene("bad.offscreen")
                .Step(SceneStep.Shot("tuhar", ShotFraming.Close))
                .Step(SceneStep.Line("zakhar", "key"))
                .Step(SceneStep.Transition("out"));

            StringAssert.Contains("в кадре не он",
                string.Join("; ", SceneValidator.Validate(scene, Cast())));
        }

        [Test]
        public void VoiceOverEmptyShot_IsAllowed()
        {
            var scene = new Scene("ok.voiceover")
                .Step(SceneStep.Shot(null, ShotFraming.Empty))
                .Step(SceneStep.Line("zakhar", "key"))
                .Step(SceneStep.Transition("out"));

            Assert.IsTrue(SceneValidator.IsValid(scene, Cast(), out var problem), problem);
        }

        [Test]
        public void SceneWithoutTransition_IsRefused()
        {
            var scene = new Scene("bad.endless")
                .Step(SceneStep.Shot("tuhar", ShotFraming.Close))
                .Step(SceneStep.Line("tuhar", "key"));

            StringAssert.Contains("не кончается переходом",
                string.Join("; ", SceneValidator.Validate(scene, Cast())));
        }

        /// <summary>Шаги после перехода не проиграются — это молчаливая потеря постановки.</summary>
        [Test]
        public void StepsAfterTransition_AreRefused()
        {
            var scene = new Scene("bad.tail")
                .Step(SceneStep.Shot("tuhar", ShotFraming.Close))
                .Step(SceneStep.Transition("out"))
                .Step(SceneStep.Line("tuhar", "key"));

            StringAssert.Contains("не проиграется никогда",
                string.Join("; ", SceneValidator.Validate(scene, Cast())));
        }

        [Test]
        public void TwoShotWithoutSecondActor_IsRefused()
        {
            var scene = new Scene("bad.two")
                .Step(SceneStep.Shot("tuhar", ShotFraming.Two))
                .Step(SceneStep.Transition("out"));

            StringAssert.Contains("второго участника нет",
                string.Join("; ", SceneValidator.Validate(scene, Cast())));
        }

        /// <summary>Антагонист заявлен ДО боя — иначе финал не расплата, а сюрприз.</summary>
        [Test]
        public void OpeningScene_IntroducesTheNamedAntagonist()
        {
            var scene = OpeningScenes.NeighbourWithADemand();
            CollectionAssert.Contains(scene.Participants(), OpeningCast.TuharVovk().Id);
        }

        [Test]
        public void SceneCarriesKeysNotText()
        {
            foreach (var step in OpeningScenes.NeighbourWithADemand().Steps)
            {
                if (step.Kind != SceneStepKind.Line) continue;
                Assert.IsFalse(step.Key.Contains(" "),
                    $"«{step.Key}» похоже на текст, а не на ключ: реплики живут в таблицах");
            }
        }
    }
}
