using Game.Gameplay;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Режисер ходу ворога (docs/COMBAT_V2.md §5, §7) — головна знахідка
    /// власника («Я походив своїми чуваками і коли наступив хід опонентів
    /// гра тупо зупинилась»): без цього класу презентер ніколи не кличе
    /// <c>GameSession.CombatAiStepOneAction()</c> сам. Тести тут — таймер
    /// паузи (звичайний/прискорений), запобіжники (>40 дій того самого
    /// юніта, 3 дії без зміни стану), скидання лічильників на новий хід.
    /// </summary>
    public class BattleTurnDirectorTests
    {
        [Test]
        public void Tick_NotAiTurn_NeverSteps_EvenAfterLongTime()
        {
            var director = new BattleTurnDirector();
            var action = director.Tick(10f, isAiTurn: false, enemyAiEnabled: true, isBusy: false, fastMode: false, currentUnitId: "e1");
            Assert.AreEqual(BattleTurnDirectorAction.None, action);
        }

        [Test]
        public void Tick_AiDisabled_NeverSteps()
        {
            var director = new BattleTurnDirector();
            var action = director.Tick(10f, isAiTurn: true, enemyAiEnabled: false, isBusy: false, fastMode: false, currentUnitId: "e1");
            Assert.AreEqual(BattleTurnDirectorAction.None, action);
        }

        [Test]
        public void Tick_Busy_NeverSteps_RegardlessOfElapsedTime()
        {
            var director = new BattleTurnDirector();
            var action = director.Tick(10f, isAiTurn: true, enemyAiEnabled: true, isBusy: true, fastMode: false, currentUnitId: "e1");
            Assert.AreEqual(BattleTurnDirectorAction.None, action);
        }

        [Test]
        public void Tick_BeforeDelayElapses_ReturnsNone()
        {
            var director = new BattleTurnDirector();
            var action = director.Tick(BattleTurnDirector.NormalDelaySeconds - 0.01f, true, true, false, false, "e1");
            Assert.AreEqual(BattleTurnDirectorAction.None, action);
        }

        [Test]
        public void Tick_AfterDelayElapses_ReturnsStepAi()
        {
            var director = new BattleTurnDirector();
            var action = director.Tick(BattleTurnDirector.NormalDelaySeconds + 0.001f, true, true, false, false, "e1");
            Assert.AreEqual(BattleTurnDirectorAction.StepAi, action);
        }

        [Test]
        public void Tick_AccumulatesAcrossFrames_UntilDelayReached()
        {
            var director = new BattleTurnDirector();
            float half = BattleTurnDirector.NormalDelaySeconds / 2f;
            Assert.AreEqual(BattleTurnDirectorAction.None, director.Tick(half, true, true, false, false, "e1"));
            Assert.AreEqual(BattleTurnDirectorAction.None, director.Tick(half - 0.001f, true, true, false, false, "e1"));
            Assert.AreEqual(BattleTurnDirectorAction.StepAi, director.Tick(0.01f, true, true, false, false, "e1"));
        }

        [Test]
        public void Tick_FastMode_UsesShorterDelay()
        {
            var director = new BattleTurnDirector();
            // Довше за швидку паузу, але коротше за звичайну — крок мав би
            // статись лише в прискореному режимі.
            float dt = (BattleTurnDirector.FastDelaySeconds + BattleTurnDirector.NormalDelaySeconds) / 2f;
            var action = director.Tick(dt, true, true, false, fastMode: true, currentUnitId: "e1");
            Assert.AreEqual(BattleTurnDirectorAction.StepAi, action);
        }

        [Test]
        public void Tick_TimerResets_AfterEachStep()
        {
            var director = new BattleTurnDirector();
            Assert.AreEqual(BattleTurnDirectorAction.StepAi,
                director.Tick(BattleTurnDirector.NormalDelaySeconds + 0.01f, true, true, false, false, "e1"));
            director.RecordStepOutcome("sig1");
            // Одразу після кроку — таймер щойно скинутий, ще зарано.
            Assert.AreEqual(BattleTurnDirectorAction.None,
                director.Tick(0.01f, true, true, false, false, "e1"));
        }

        [Test]
        public void Tick_TimerDoesNotAccumulate_WhileBusy()
        {
            var director = new BattleTurnDirector();
            director.Tick(BattleTurnDirector.NormalDelaySeconds - 0.01f, true, true, false, false, "e1");
            // Такти йдуть довго — пауза не повинна тікати в цей час.
            director.Tick(5f, true, true, isBusy: true, fastMode: false, currentUnitId: "e1");
            // Ще один невеликий кадр після того, як такти скінчились — досі зарано,
            // бо busy-кадр скинув таймер, а не додав до нього.
            Assert.AreEqual(BattleTurnDirectorAction.None,
                director.Tick(0.02f, true, true, false, false, "e1"));
        }

        [Test]
        public void Tick_NewUnit_ResetsSafetyCounters()
        {
            var director = new BattleTurnDirector();
            for (int i = 0; i < BattleTurnDirector.MaxActionsForSameUnit - 1; i++)
            {
                Assert.AreEqual(BattleTurnDirectorAction.StepAi,
                    director.Tick(BattleTurnDirector.NormalDelaySeconds + 0.01f, true, true, false, false, "e1"));
                director.RecordStepOutcome("sig" + i); // сигнатура завжди змінюється — «немає змін» не спрацює тут
            }

            // Новий юніт ходить — лічильник кроків МАЄ скинутися, а не
            // продовжити з 39 і форснути кінець ходу на першому ж кроці.
            var action = director.Tick(BattleTurnDirector.NormalDelaySeconds + 0.01f, true, true, false, false, "e2");
            Assert.AreEqual(BattleTurnDirectorAction.StepAi, action);
        }

        [Test]
        public void Tick_MoreThanMaxActionsForSameUnit_ForcesEndTurn()
        {
            var director = new BattleTurnDirector();
            for (int i = 0; i < BattleTurnDirector.MaxActionsForSameUnit; i++)
            {
                var action = director.Tick(BattleTurnDirector.NormalDelaySeconds + 0.01f, true, true, false, false, "e1");
                Assert.AreEqual(BattleTurnDirectorAction.StepAi, action, "крок " + i);
                director.RecordStepOutcome("sig" + i);
            }

            var forced = director.Tick(BattleTurnDirector.NormalDelaySeconds + 0.01f, true, true, false, false, "e1");
            Assert.AreEqual(BattleTurnDirectorAction.ForceEndTurn, forced);
            Assert.IsNotNull(director.LastWarning);
        }

        [Test]
        public void Tick_ThreeActionsWithoutChange_ForcesEndTurn_EvenWithFewSteps()
        {
            var director = new BattleTurnDirector();
            // Перший крок лише встановлює базову сигнатуру (нема з чим
            // порівнювати) — «без змін» рахується від НАСТУПНОГО кроку:
            // трьом підряд «нічого не змінилось» відповідають чотири виклики
            // з тим самим підписом.
            director.Tick(BattleTurnDirector.NormalDelaySeconds + 0.01f, true, true, false, false, "e1");
            director.RecordStepOutcome("same"); // база
            director.Tick(BattleTurnDirector.NormalDelaySeconds + 0.01f, true, true, false, false, "e1");
            director.RecordStepOutcome("same"); // без змін №1
            director.Tick(BattleTurnDirector.NormalDelaySeconds + 0.01f, true, true, false, false, "e1");
            director.RecordStepOutcome("same"); // без змін №2
            director.Tick(BattleTurnDirector.NormalDelaySeconds + 0.01f, true, true, false, false, "e1");
            director.RecordStepOutcome("same"); // без змін №3 — запобіжник має спрацювати на наступному тіку

            var forced = director.Tick(BattleTurnDirector.NormalDelaySeconds + 0.01f, true, true, false, false, "e1");
            Assert.AreEqual(BattleTurnDirectorAction.ForceEndTurn, forced);
        }

        [Test]
        public void Tick_ChangeBreaksTheNoChangeStreak()
        {
            var director = new BattleTurnDirector();
            director.Tick(BattleTurnDirector.NormalDelaySeconds + 0.01f, true, true, false, false, "e1");
            director.RecordStepOutcome("same");
            director.Tick(BattleTurnDirector.NormalDelaySeconds + 0.01f, true, true, false, false, "e1");
            director.RecordStepOutcome("same"); // 2 без змін — ще не запобіжник
            director.Tick(BattleTurnDirector.NormalDelaySeconds + 0.01f, true, true, false, false, "e1");
            director.RecordStepOutcome("different"); // зміна — лічильник обнулився

            var action = director.Tick(BattleTurnDirector.NormalDelaySeconds + 0.01f, true, true, false, false, "e1");
            Assert.AreEqual(BattleTurnDirectorAction.StepAi, action);
        }

        [Test]
        public void Reset_ClearsAllState()
        {
            var director = new BattleTurnDirector();
            for (int i = 0; i < BattleTurnDirector.MaxActionsForSameUnit; i++)
            {
                director.Tick(BattleTurnDirector.NormalDelaySeconds + 0.01f, true, true, false, false, "e1");
                director.RecordStepOutcome("sig" + i);
            }
            director.Reset();

            var action = director.Tick(BattleTurnDirector.NormalDelaySeconds + 0.01f, true, true, false, false, "e1");
            Assert.AreEqual(BattleTurnDirectorAction.StepAi, action, "після Reset той самий юніт не мав би одразу впертись у запобіжник");
        }

        [Test]
        public void LastWarning_IsNullOnNormalStep()
        {
            var director = new BattleTurnDirector();
            director.Tick(BattleTurnDirector.NormalDelaySeconds + 0.01f, true, true, false, false, "e1");
            Assert.IsNull(director.LastWarning);
        }
    }
}
