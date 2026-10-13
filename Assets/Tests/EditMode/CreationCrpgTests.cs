using System.Linq;
using Game.Core.Characters;
using Game.Core.Combat;
using Game.Core.Session;
using Game.Core.Stats;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Створення героя як у CRPG (плейтест 13.10.2026: «ЧОму ти не зробив як в інших Сrpg…»; про атрибути — «Вони поки ні
    /// на шо не впливають, можеш додать 4 для візуалу»): клас обирають окремо від передісторії, а чотири атрибути
    /// перекладають очко за очком у межах бюджету — сума атрибутів передісторії не росте ніколи. Обране переживає
    /// збереження у свіжу сесію.
    /// </summary>
    public class CreationCrpgTests
    {
        private static GameSession NewCreation()
        {
            var s = new GameSession();
            s.NewGame(new NewGameOptions { SkipCreation = false, HitRule = HitRuleKind.Threshold, Seed = 3 });
            Assert.AreEqual(SessionState.Creation, s.State);
            return s;
        }

        private static int Attr(GameSession s, string key) =>
            s.GetCharacterSheet(GameSession.ProtagonistId).Attributes.First(l => l.AttributeKey == key).Score;

        private static CompanionClass ClassOf(GameSession s) =>
            s.GetRosterView().Companions.First(c => c.Id == GameSession.ProtagonistId).Class;

        [Test]
        public void Class_IsChosenSeparately_FromBackground()
        {
            var s = NewCreation();
            s.SetProtagonistBackground("warrior");
            Assert.AreEqual(CompanionClass.Brawler, s.GetProtagonistCreationView().Class, "без вибору — клас передісторії");
            Assert.AreEqual(4, s.GetProtagonistCreationView().AvailableClasses.Count, "усі чотири класи (Поправка №15)");
            s.SetProtagonistClass(CompanionClass.Shooter);
            Assert.AreEqual(CompanionClass.Shooter, s.GetProtagonistCreationView().Class);
            s.ConfirmCreation();
            Assert.AreEqual(CompanionClass.Shooter, ClassOf(s), "Стрілець досяжний, хоч жодна передісторія його не дає");
        }

        [Test]
        public void Attributes_MovePointByPoint_SumNeverGrows()
        {
            var s = NewCreation();
            s.SetProtagonistBackground("warrior"); // Сила 6, Спритність 4, Кмітливість 2, Воля 4
            Assert.IsFalse(s.AdjustProtagonistAttribute(AttributeType.Strength, 1), "підняти без вільного очка не можна");
            Assert.IsTrue(s.AdjustProtagonistAttribute(AttributeType.Wits, -1));
            Assert.AreEqual(1, s.GetProtagonistCreationView().FreePoints);
            Assert.IsFalse(s.AdjustProtagonistAttribute(AttributeType.Wits, -1), "нижче 1 не можна");
            Assert.IsTrue(s.AdjustProtagonistAttribute(AttributeType.Strength, 1));
            Assert.AreEqual(0, s.GetProtagonistCreationView().FreePoints);

            var view = s.GetProtagonistCreationView();
            Assert.AreEqual(16, view.Attributes.Sum(a => a.Value), "сума атрибутів передісторії не змінилась");
            s.ConfirmCreation();
            Assert.AreEqual(7, Attr(s, "strength"));
            Assert.AreEqual(1, Attr(s, "wits"));
        }

        [Test]
        public void Attributes_BudgetCapsHowManyPointsMove()
        {
            var s = NewCreation();
            s.SetProtagonistBackground("healer"); // Сила 3, Спритність 2, Кмітливість 4, Воля 7
            for (int i = 0; i < GameSession.CreationAttributeBudget; i++)
                Assert.IsTrue(s.AdjustProtagonistAttribute(AttributeType.Will, -1));
            Assert.IsFalse(s.AdjustProtagonistAttribute(AttributeType.Wits, -1), "більше бюджету не перекласти");
            Assert.IsTrue(s.AdjustProtagonistAttribute(AttributeType.Agility, 1));
            Assert.IsTrue(s.AdjustProtagonistAttribute(AttributeType.Agility, -1), "повернути щойно додане очко можна завжди");
            Assert.IsTrue(s.AdjustProtagonistAttribute(AttributeType.Will, 1), "скасувати зниження теж");
        }

        [Test]
        public void ChangingBackground_ResetsRedistribution()
        {
            var s = NewCreation();
            s.SetProtagonistBackground("warrior");
            s.AdjustProtagonistAttribute(AttributeType.Wits, -1);
            s.SetProtagonistBackground("trader");
            var view = s.GetProtagonistCreationView();
            Assert.AreEqual(0, view.FreePoints);
            Assert.AreEqual(0, view.PointsMoved);
            Assert.AreEqual(6, view.Attributes.First(a => a.Id == AttributeType.Wits.ToString()).Value);
        }

        [Test]
        public void ChosenClassAndAttributes_SurviveSave_IntoAFreshSession()
        {
            var s = NewCreation();
            s.SetProtagonistBackground("trader");
            s.SetProtagonistClass(CompanionClass.Healer);
            s.AdjustProtagonistAttribute(AttributeType.Strength, -1);
            s.AdjustProtagonistAttribute(AttributeType.Will, 1);
            s.ConfirmCreation();
            var step = s.AdvanceScene();
            while (!step.IsFinished)
                step = step.IsChoice ? s.ChooseSceneOption(0) : s.AdvanceScene();

            string blob = s.SaveState(0);
            var fresh = new GameSession();
            fresh.NewGame(new NewGameOptions { SkipCreation = true, HitRule = HitRuleKind.Threshold });
            fresh.RestoreFromBlob(blob);

            Assert.AreEqual(CompanionClass.Healer, ClassOf(fresh));
            Assert.AreEqual(1, Attr(fresh, "strength"));
            Assert.AreEqual(5, Attr(fresh, "will"));
        }
    }
}
