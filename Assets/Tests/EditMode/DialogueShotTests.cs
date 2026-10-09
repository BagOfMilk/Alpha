using System.Collections.Generic;
using Game.Core.Combat;
using Game.Core.Scenes;
using Game.Core.Session;
using Game.Gameplay.UI;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Діалог у світі (власник, 08.10.2026: «зробити діалоги між персонажами як в baldursgayte 3»; «Камера в світі,
    /// як у BG3»): кадр сцени доходить до показу, режисер знімає того, хто говорить, і тримає правило 180°.
    /// </summary>
    public class DialogueShotTests
    {
        private const string Hero = GameSession.ProtagonistId;
        private static readonly DialogueVec HeroHead = new DialogueVec(0f, 0.65f, 0f);
        private static readonly DialogueVec OtherHead = new DialogueVec(0.6f, 0.65f, 0.2f);

        [Test]
        public void SceneView_CarriesFraming_FromTheScript()
        {
            var s = new GameSession();
            s.NewGame(new NewGameOptions { SkipCreation = true, HitRule = HitRuleKind.Threshold });
            var seen = new HashSet<ShotFraming>();
            var step = s.AdvanceScene();
            for (int guard = 0; guard < 200 && !step.IsFinished; guard++)
            {
                seen.Add(step.Framing);
                step = step.IsChoice ? s.ChooseSceneOption(0) : s.AdvanceScene();
            }
            Assert.That(seen, Does.Contain(ShotFraming.Close), "сцена відкриття знімає крупні плани — показ має їх бачити");
            Assert.That(seen, Does.Contain(ShotFraming.Two), "і подвійні");
        }

        [Test]
        public void Plan_SpeakerInTwoShot_IsFilmedOverTheListenersShoulder()
        {
            var shot = DialogueDirector.Plan(ShotFraming.Two, "tuhar", Hero, "tuhar", Hero);
            Assert.AreEqual(DialogueShotKind.OverShoulder, shot.Kind);
            Assert.AreEqual("tuhar", shot.SubjectId);
            Assert.AreEqual(Hero, shot.OtherId);
        }

        [Test]
        public void Plan_SpeakerOutsideTheShot_GetsACloseUp()
        {
            // Порожній план сцени відкриття, а говорять прибульці — у BG3 видно того, хто говорить.
            var shot = DialogueDirector.Plan(ShotFraming.Empty, null, null, "keeper", Hero);
            Assert.AreEqual(DialogueShotKind.Close, shot.Kind);
            Assert.AreEqual("keeper", shot.SubjectId);
            Assert.AreEqual(Hero, shot.OtherId);
        }

        [Test]
        public void Plan_EmptyShotWithoutSpeaker_IsWide_AndNoShotKeepsThePrevious()
        {
            Assert.AreEqual(DialogueShotKind.Wide, DialogueDirector.Plan(ShotFraming.Empty, null, null, null, Hero).Kind);
            Assert.IsNull(DialogueDirector.Plan(ShotFraming.None, null, null, null, Hero), "без плану — лишаємо попередній кадр");
        }

        [Test]
        public void Plan_CloseOnHero_HasNoOtherFromTheHeroItself()
        {
            var shot = DialogueDirector.Plan(ShotFraming.Close, Hero, null, null, Hero);
            Assert.AreEqual(DialogueShotKind.Close, shot.Kind);
            Assert.AreEqual(Hero, shot.SubjectId);
            Assert.IsNull(shot.OtherId);
        }

        [Test]
        public void Pose_BothCloseUps_StayOnOneSideOfTheLine()
        {
            // Правило 180°: крупний героя і крупний співрозмовника знято з одного боку осі.
            var onOther = DialogueDirector.Pose(DialogueShotKind.Close, OtherHead, HeroHead, HeroHead, OtherHead, 0.7f);
            var onHero = DialogueDirector.Pose(DialogueShotKind.Close, HeroHead, OtherHead, HeroHead, OtherHead, 0.7f);
            float a = DialogueDirector.SideOf(onOther.Position, HeroHead, OtherHead);
            float b = DialogueDirector.SideOf(onHero.Position, HeroHead, OtherHead);
            Assert.That(a * b, Is.GreaterThan(0f), "камера перескочила вісь між склейками");
            var over = DialogueDirector.Pose(DialogueShotKind.OverShoulder, OtherHead, HeroHead, HeroHead, OtherHead, 0.7f);
            Assert.That(DialogueDirector.SideOf(over.Position, HeroHead, OtherHead) * a, Is.GreaterThan(0f));
        }

        [Test]
        public void Pose_CloseUp_IsNearTheFace_AndLooksAtIt()
        {
            var pose = DialogueDirector.Pose(DialogueShotKind.Close, OtherHead, HeroHead, HeroHead, OtherHead, 0.7f);
            float d = (pose.Position - OtherHead).FlatLength;
            Assert.That(d, Is.InRange(0.4f, 0.8f), "крупний — менш ніж зріст від обличчя");
            Assert.That((pose.LookAt - OtherHead).FlatLength, Is.LessThan(0.01f));
            Assert.AreEqual(DialogueDirector.CloseFov, pose.Fov);
        }

        [Test]
        public void Pose_SecondCloseUp_DiffersButKeepsTheSide()
        {
            // Повернення до тієї самої людини — інший кадр, але з того самого боку осі (правило 180°).
            var a = DialogueDirector.Pose(DialogueShotKind.Close, OtherHead, HeroHead, HeroHead, OtherHead, 0.7f, 0);
            var b = DialogueDirector.Pose(DialogueShotKind.Close, OtherHead, HeroHead, HeroHead, OtherHead, 0.7f, 1);
            Assert.That((a.Position - b.Position).FlatLength, Is.GreaterThan(0.05f));
            Assert.That(DialogueDirector.SideOf(a.Position, HeroHead, OtherHead) * DialogueDirector.SideOf(b.Position, HeroHead, OtherHead),
                Is.GreaterThan(0f));
            Assert.That((b.Position - OtherHead).FlatLength, Is.LessThan((a.Position - OtherHead).FlatLength), "другий — ближче");
        }

        [Test]
        public void Pose_OverShoulder_SitsBehindTheListener()
        {
            var pose = DialogueDirector.Pose(DialogueShotKind.OverShoulder, OtherHead, HeroHead, HeroHead, OtherHead, 0.7f);
            Assert.That((pose.Position - HeroHead).FlatLength, Is.LessThan((pose.Position - OtherHead).FlatLength),
                "камера ближче до слухача, ніж до мовця");
        }

        [Test]
        public void Pose_IsDeterministic_AndPushInMovesTowardTheFace()
        {
            var a = DialogueDirector.Pose(DialogueShotKind.Two, HeroHead, OtherHead, HeroHead, OtherHead, 0.7f);
            var b = DialogueDirector.Pose(DialogueShotKind.Two, HeroHead, OtherHead, HeroHead, OtherHead, 0.7f);
            Assert.AreEqual(a.Position.X, b.Position.X);
            Assert.AreEqual(a.Position.Z, b.Position.Z);
            var start = DialogueDirector.PushIn(a, 0f);
            var end = DialogueDirector.PushIn(a, 1f);
            Assert.That((end - a.LookAt).FlatLength, Is.LessThan((start - a.LookAt).FlatLength));
        }
    }
}
