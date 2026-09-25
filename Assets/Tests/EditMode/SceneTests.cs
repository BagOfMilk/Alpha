using System.Collections.Generic;
using Game.Core.Characters;
using Game.Core.Companions;
using Game.Core.Scenes;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Формат сцени (Поправка №5.8). Заради цих перевірок сцена й зроблена
    /// даними: постановку видно до редактора.
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

        /// <summary>
        /// Адверсаріал-огляд Поправки №7.8: GameSession.BeginScene НІКОЛИ не
        /// кличе SceneValidator сам (лише проходить сценарій) — жодна з шести
        /// нових Choice-сцен (арки Мирослави/Максима, конфронтація зради,
        /// тиха перевірка, рада Захара) до цього тесту не мала жодної
        /// автоматичної перевірки постановки (карток/міток/наслідків
        /// кожного варіанту): усе покриття GameSessionTests обирає ЗАВЖДИ
        /// конкретний варіант, а не проганяє валідатор по всіх гілках.
        /// </summary>
        [Test]
        public void EveryNewCompanionScene_Poprawka78_IsValid()
        {
            var scenes = new Dictionary<string, Scene>
            {
                ["arc.myroslava.ch1"] = CompanionScenes.MyroslavaTrustArc(),
                ["arc.myroslava.ch2"] = CompanionScenes.MyroslavaEpilogue(),
                ["arc.maksym.ch2"] = CompanionScenes.MaksymEpilogue(),
                ["scene.myroslava.confrontation"] = CompanionScenes.MyroslavaConfrontation(),
                ["scene.myroslava.checkup"] = CompanionScenes.MyroslavaTrustCheckup(),
                ["scene.zakhar.council"] = CompanionScenes.ZakharCouncil(),
            };

            foreach (var kv in scenes)
                Assert.IsTrue(SceneValidator.IsValid(kv.Value, Cast(), out var problem),
                    kv.Key + ": " + problem);
        }

        /// <summary>Голос нізвідки: репліка раніше першого плану.</summary>
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
        /// Говорить той, кого видно. Голос за кадром — законний прийом, але він
        /// оголошується порожнім планом, а не виходить із забутого.
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

        /// <summary>Кроки після переходу не програються — це мовчазна втрата постановки.</summary>
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

        /// <summary>Антагоніст заявлений ДО бою — інакше фінал не розплата, а сюрприз.</summary>
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
