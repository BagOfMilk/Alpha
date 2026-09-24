using Game.Core.World;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Форсована тест-криза доби 5 «Вогонь на в'їзді» (R10, §7.14
    /// TEST_BUILD.md) — авторський скриптований прев'ю-тригер, явно позначений
    /// у коді як не-механіка постійної гри (природний Пульс не встигає за 5
    /// діб — §9.2). Лінія: Warn → OpenReactionWindow → Mitigate?/Bite.
    /// </summary>
    public class ForcedCrisisSourceTests
    {
        [Test]
        public void FullLine_Warn_Window_Bite_Unmitigated()
        {
            var crisis = new ForcedCrisisSource(warnDay: 5);

            Assert.IsTrue(crisis.Warn(5));
            Assert.AreEqual(CrisisPhase.Warned, crisis.Phase);

            Assert.IsTrue(crisis.OpenReactionWindow());
            Assert.AreEqual(CrisisPhase.WindowOpen, crisis.Phase);

            Assert.IsTrue(crisis.Bite());
            Assert.AreEqual(CrisisPhase.Resolved, crisis.Phase);
            Assert.IsTrue(crisis.HasBitten);
            Assert.IsFalse(crisis.WasMitigated, "ніхто не відреагував — укус на повну (crisis.test.unmitigated)");
        }

        [Test]
        public void MitigatedVariant_PlayerReactsInsideWindow()
        {
            var crisis = new ForcedCrisisSource(warnDay: 5);
            crisis.Warn(5);
            crisis.OpenReactionWindow();

            Assert.IsTrue(crisis.Mitigate(), "хук мітигації — D1 викликає його, коли гравець реагує");
            Assert.IsTrue(crisis.Bite());

            Assert.IsTrue(crisis.WasMitigated);
            Assert.IsTrue(crisis.HasBitten, "мітигація пом'якшує укус (§3.5: «м'який укус»), а не скасовує його зовсім");
        }

        [Test]
        public void Warn_WrongDay_DoesNothing()
        {
            var crisis = new ForcedCrisisSource(warnDay: 5);
            Assert.IsFalse(crisis.Warn(3), "поза скриптованою добою попередження не звучить");
            Assert.AreEqual(CrisisPhase.Idle, crisis.Phase);
        }

        [Test]
        public void OpenReactionWindow_WithoutWarnFirst_Fails()
        {
            var crisis = new ForcedCrisisSource(warnDay: 5);
            Assert.IsFalse(crisis.OpenReactionWindow(), "вікно не може відкритись до попередження (US-11.2)");
        }

        [Test]
        public void Mitigate_OutsideOpenWindow_Fails()
        {
            var crisis = new ForcedCrisisSource(warnDay: 5);
            Assert.IsFalse(crisis.Mitigate(), "до попередження мітигації нема чого пом'якшувати");

            crisis.Warn(5);
            Assert.IsFalse(crisis.Mitigate(), "вікно ще не відкрите — Warned недостатньо");
        }

        [Test]
        public void Mitigate_AfterBite_Fails()
        {
            var crisis = new ForcedCrisisSource(warnDay: 5);
            crisis.Warn(5);
            crisis.OpenReactionWindow();
            crisis.Bite();

            Assert.IsFalse(crisis.Mitigate(), "вікно чесно закривається биттям — мітигація заднім числом неможлива");
        }

        [Test]
        public void Bite_Twice_SecondCallFails()
        {
            var crisis = new ForcedCrisisSource(warnDay: 5);
            crisis.Warn(5);
            crisis.OpenReactionWindow();
            Assert.IsTrue(crisis.Bite());
            Assert.IsFalse(crisis.Bite(), "криза бʼє рівно раз");
        }

        [Test]
        public void SaveRoundTrip_PreservesPhaseAndFlags()
        {
            var crisis = new ForcedCrisisSource(warnDay: 5);
            crisis.Warn(5);
            crisis.OpenReactionWindow();
            crisis.Mitigate();
            crisis.Bite();

            var blob = crisis.CaptureState();

            var restored = new ForcedCrisisSource(warnDay: 5);
            restored.RestoreState(blob);

            Assert.AreEqual(crisis.Phase, restored.Phase);
            Assert.AreEqual(crisis.WasMitigated, restored.WasMitigated);
            Assert.AreEqual(crisis.HasBitten, restored.HasBitten);
        }

        [Test]
        public void Announces_IsAlwaysTrue()
        {
            // US-11.2: скриптована криза зобов'язана попередити — на відміну від
            // OpeningContent.ScriptedSource (Announces=false), яка є постановкою
            // про саму себе.
            Assert.IsTrue(ForcedCrisisSource.Announces);
        }
    }
}
