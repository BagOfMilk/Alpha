using System.Linq;
using Game.Core.Combat;
using Game.Core.Session;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Побічний фікс, знайдений при ревізії Поправки №15.1 (пізніше
    /// приєднання): <c>RosterAdapter.CaptureState</c> навмисно НЕ пише
    /// скіли/атрибути — «Імена, стати й картки приходять із контенту»
    /// (коментар класу). Це правда для іменного касту (фіксований
    /// архетип), але НЕ для протагоніста: його скіли/атрибути — вибір
    /// гравця (<c>ConfirmCreation</c> → <c>ProtagonistCreation.Apply</c>),
    /// застосований ОДИН РАЗ поверх збірного старт-плейсхолдера
    /// (<c>FirstHourWorld.Build</c>: усі атрибути й чотири скіли по 4).
    /// <c>RestoreFromBlob</c> у СВІЖУ сесію не повторює <c>ConfirmCreation</c>
    /// (навмисно — інакше стер би прогрес білд-планувальника, US-2.3/R11),
    /// тож без сьомого/восьмого поля запису протагоніста в "ros=" він
    /// після «Продовжити» тихо відкочувався на плейсхолдер (Сила/
    /// Спритність/Кмітливість/Воля=4, Виживання=4 замість обраних
    /// передісторією 3) — розбіжність, яку виявив
    /// <c>FreshSessionRestoreTests.FreshRestore_SplitInFreePlay_
    /// MatchesUninterruptedRun</c>, щойно Поправка №15.1 почала змінювати,
    /// хто саме працює на посту (більше живих напарників — інша
    /// розстановка — інший виробіток, і плейсхолдерна Виживання=4 замість
    /// обраної 3 стала видимою різницею у виробітку їжі).
    /// </summary>
    public class ProtagonistStatsPersistenceTests
    {
        [Test]
        public void ProtagonistSkillsAndAttributes_SurviveSave_IntoAFreshSession()
        {
            var s = new GameSession();
            s.NewGame(new NewGameOptions { SkipCreation = false, HitRule = HitRuleKind.Threshold });
            Assert.AreEqual(SessionState.Creation, s.State);
            s.SetProtagonistBackground("warrior");
            s.ConfirmCreation();
            Assert.AreEqual(SessionState.Scene, s.State);
            var step = s.AdvanceScene();
            while (!step.IsFinished)
                step = step.IsChoice ? s.ChooseSceneOption(0) : s.AdvanceScene();
            Assert.AreEqual(SessionState.Morning, s.State);

            int survivalBefore = SurvivalOf(s);

            // Плейсхолдер FirstHourWorld.Build дає Виживання=4 всім чотирьом
            // атрибутам і чотирьом бойовим/соц скілам — warrior-передісторія
            // (Backgrounds.Warrior) перезаписує Виживання на 3. Якщо тест сам
            // зламався (плейсхолдер і передісторія раптом зійшлись), число
            // нижче доведеться поправити разом із Backgrounds.Warrior.
            Assert.AreEqual(3, survivalBefore, "тест сам зламався: warrior більше не дає Виживання=3");

            string blob = s.SaveState(0);

            var fresh = new GameSession();
            fresh.NewGame(new NewGameOptions { SkipCreation = true, HitRule = HitRuleKind.Threshold });
            fresh.RestoreFromBlob(blob);

            int survivalAfter = SurvivalOf(fresh);
            Assert.AreEqual(survivalBefore, survivalAfter,
                "Виживання протагоніста (обране передісторією) мусить пережити SaveState/RestoreFromBlob у СВІЖУ сесію — " +
                "не відкочуватися на плейсхолдер FirstHourWorld.Build");
        }

        private static int SurvivalOf(GameSession s)
        {
            var sheet = s.GetCharacterSheet(GameSession.ProtagonistId);
            Assert.IsNotNull(sheet);
            var line = sheet.Skills.First(l => l.SkillKey == "survival");
            return line.Score;
        }
    }
}
