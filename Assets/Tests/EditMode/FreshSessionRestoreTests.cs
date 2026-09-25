using System.Collections.Generic;
using Game.Core.Session;
using Game.Core.Session.Bots;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Блокер-фікс (знайдено 25.09.2026, лід, репродуковано): відновлення
    /// сейву у СВІЖИЙ екземпляр <see cref="GameSession"/> (<c>ContinueGame</c>/
    /// <c>RestoreFromBlob</c> на щойно сконструйованій сесії — саме так працює
    /// справжнє «Продовжити» після перезапуску застосунку) розходилось із
    /// безперервною грою. На ТІЙ САМІЙ сесії (<c>LoadState</c> без нового
    /// <c>GameSession</c>, як робить <c>TestBuildTensionPaceTests.SaveLoad_
    /// MidFreePlay_PreservesTrajectory</c>) дірка не видна: живі об'єкти
    /// (<c>AssignmentSlot</c>, зареєстровані визначення квестів) лишаються
    /// на місці незалежно від сейву.
    ///
    /// Причини (усі — «дві бухгалтерії одного факту», де СВІЖИЙ інстанс бачив
    /// лише одну з них):
    /// 1. <c>AssignmentSlot.AssignedCompanionId</c> (хто на посту з БОКУ
    ///    слота, <see cref="Game.Core.Base.BaseState.AdvanceCycle"/> читає
    ///    саме це поле) ніколи не входив у слепок — лише <c>Companion.
    ///    AssignedSlotId</c> (бік напарника, RosterAdapter). На свіжому
    ///    <c>BaseState</c> кожен слот починається порожнім: пости виробляли
    ///    не тому/нічого. Фікс — <see cref="Game.Core.Base.BaseState.
    ///    RestoreSlotOccupancy"/>, покликаний з <c>GameSession.ApplySave</c>.
    /// 2. <c>BaseState.WasHungryLastCycle</c> (штраф виробітку/XP наступного
    ///    циклу, Поправка №4) не персистився взагалі — загрузка безкарно
    ///    знімала голод, накладений прямо перед сейвом.
    /// 3. Квест минулої (вже НЕ InProgress) глави арки — <c>QuestLog.
    ///    RestoreState</c> тихо відкидає прогін, чиє визначення нема в пулі,
    ///    а на свіжому інстансі пул знав лише про InProgress главу
    ///    (<c>ReattachInProgressArcChapterQuests</c> раніше не дивився на
    ///    минулі).
    ///
    /// Ці тести порівнюють ПОВНУ стрічку подій (не лише Напругу) двох
    /// прогонів з тим самим ботом: (a) безперервного і (b) розділеного
    /// SaveState/RestoreFromBlob-ом у НОВИЙ <see cref="GameSession"/>.
    /// Порівняння починається з доби ПІСЛЯ точки збереження: у самій добі
    /// збереження в <c>DayLog</c> живої сесії лишається "хвіст" ночі, яка щe
    /// не була очищена (ClearDayLog чистить ЛИШЕ на початку AdvanceDay/
    /// AdvanceNight, а не при вході в Morning) — сейв цей ефемерний хвіст
    /// свідомо не несе (це не частина стану, лише поточний UI-тікер), тож
    /// його відсутність на свіжому інстансі — не розбіжність.
    /// </summary>
    public class FreshSessionRestoreTests
    {
        [Test]
        public void FreshRestore_Homebody_MatchesUninterruptedRun()
        {
            AssertFreshRestoreMatchesUninterrupted(new HomebodyPolicy(), suppressCouncilRoutine: true,
                daysBeforeSave: 12, daysAfterSave: 14);
        }

        [Test]
        public void FreshRestore_StewardWithExpeditionsAndCouncil_MatchesUninterruptedRun()
        {
            AssertFreshRestoreMatchesUninterrupted(new StewardPolicy(), suppressCouncilRoutine: false,
                daysBeforeSave: 20, daysAfterSave: 20);
        }

        /// <summary>Розділення ПОСЕРЕД скриптованих перших п'яти діб (§3 TEST_BUILD.md) — до форсованої кризи доби 5.</summary>
        [Test]
        public void FreshRestore_SplitDuringScriptedDays_MatchesUninterruptedRun()
        {
            AssertFreshRestoreMatchesUninterrupted(new HomebodyPolicy(), suppressCouncilRoutine: true,
                daysBeforeSave: 3, daysAfterSave: 10);
        }

        /// <summary>Розділення глибоко у вільній грі (після фіналу доби 5) — інший стан сесії (FreePlay), ніж Morning у решти тестів цього файлу.</summary>
        [Test]
        public void FreshRestore_SplitInFreePlay_MatchesUninterruptedRun()
        {
            AssertFreshRestoreMatchesUninterrupted(new StewardPolicy(), suppressCouncilRoutine: false,
                daysBeforeSave: 40, daysAfterSave: 20);
        }

        private static void AssertFreshRestoreMatchesUninterrupted(IBotPolicy policy, bool suppressCouncilRoutine,
            int daysBeforeSave, int daysAfterSave)
        {
            var options = new NewGameOptions();
            var source = new GameSession(options.Roller);
            source.NewGame(options);
            BotRunner.Drive(source, policy, daysBeforeSave, suppressCouncilRoutine: suppressCouncilRoutine);

            int splitDay = source.CurrentView.Day;
            string blob = source.SaveState(0);

            // Свіжий екземпляр — саме так працює справжнє "Продовжити" після
            // перезапуску застосунку (ContinueGame/RestoreFromBlob), а НЕ
            // LoadState на тій самій живій сесії.
            var target = new GameSession(new NewGameOptions().Roller);
            target.NewGame(new NewGameOptions());
            target.RestoreFromBlob(blob);

            var srcLog = new List<GameEvent>();
            var tgtLog = new List<GameEvent>();
            BotRunner.Drive(source, policy, daysAfterSave, fullLog: srcLog, suppressCouncilRoutine: suppressCouncilRoutine);
            BotRunner.Drive(target, policy, daysAfterSave, fullLog: tgtLog, suppressCouncilRoutine: suppressCouncilRoutine);

            var srcTail = FilterAfterSplit(srcLog, splitDay);
            var tgtTail = FilterAfterSplit(tgtLog, splitDay);

            CollectionAssert.AreEqual(srcTail, tgtTail,
                "після SaveState/RestoreFromBlob у СВІЖИЙ інстанс подальші доби мусять дати ТУ САМУ стрічку подій, " +
                "що безперервний прогін тим самим ботом — інакше пости/голод/квести минулих глав арки тихо " +
                "розійшлись при відновленні");

            Assert.AreEqual(source.DebugTensionValue, target.DebugTensionValue,
                "прихована Напруга мусить лишитись тією самою — це найчутливіший інтегратор усіх драйверів дня");
        }

        /// <summary>
        /// Прибирає game.saved/game.loaded (сейв — не подія самого світу) і
        /// все, що датовано ДО/В добу збереження: DayLog живої сесії в
        /// момент SaveState ще несе неочищений хвіст ночі попередньої фази
        /// (ефемерний UI-тікер, свідомо поза слепком) — на свіжому інстансі
        /// його просто нема, і це коректно, а не розбіжність.
        /// </summary>
        private static List<string> FilterAfterSplit(List<GameEvent> log, int splitDay)
        {
            var result = new List<string>();
            foreach (var e in log)
            {
                if (e.Day <= splitDay) continue;
                if (e.Key == "game.saved" || e.Key == "game.loaded") continue;

                var parts = new List<string>();
                foreach (var kv in e.Args) parts.Add(kv.Key + "=" + kv.Value);
                parts.Sort(System.StringComparer.Ordinal);
                result.Add(e.Day + " " + e.Phase + " " + e.Key + " " + string.Join(",", parts));
            }
            return result;
        }
    }
}
