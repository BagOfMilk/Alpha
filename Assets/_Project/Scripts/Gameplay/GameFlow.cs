using Game.Core.Combat;
using Game.Core.Expeditions;
using Game.Core.Quests;
using Game.Core.Saves;

namespace Game.Gameplay
{
    /// <summary>
    /// Контекст игровой сессии между сценами (Campaign ↔ Battle): чистые статики —
    /// переживают LoadScene без DontDestroyOnLoad. Кампания одна на сессию;
    /// PendingBattle несёт бой вылазки в Battle-сцену и результат обратно.
    /// </summary>
    public static class GameFlow
    {
        /// <summary>Текущая кампания (null — сессии нет, экраны показывают меню/песочницу).</summary>
        public static Campaign Campaign;

        /// <summary>Бой вылазки, ожидающий Battle-сцену (null — Battle работает песочницей).</summary>
        public static CombatState PendingBattle;

        /// <summary>Вылазка, чей бой идёт (для ConcludeExpedition по возвращении).</summary>
        public static Expedition PendingExpedition;

        /// <summary>Итог последней вылазки — для экрана отчёта возвращения.</summary>
        public static ExpeditionReport LastReport;

        /// <summary>Идущий бой — ФИНАЛЬНАЯ битва: исход резолвится FinalBattle.Resolve.</summary>
        public static bool PendingFinale;

        /// <summary>Бонус золота от «Снаряжения экспедиции» (совет) — банкуется при победе.</summary>
        public static int PendingBuffGold;

        /// <summary>
        /// Активный прогон квеста (пролог/доска, US-14.3/17.1): живёт МЕЖДУ сценами —
        /// бой квестового этапа идёт в Battle, остальные этапы играются в Campaign.
        /// null — квест не идёт. Battle-контроллер отличает квестовый бой от вылазки
        /// по PendingQuest != null (у вылазки вместо него PendingExpedition).
        /// </summary>
        public static QuestRun PendingQuest;

        /// <summary>Отчёт последнего шага квеста (реплики/чек) — для панели квеста после боя.</summary>
        public static QuestStepReport LastQuestStep;

        /// <summary>
        /// Хроника города: живёт в статике, а не в контроллере — иначе лента
        /// обнулялась КАЖДЫМ походом в Battle-сцену (контроллер пересоздаётся),
        /// и события дороги «туда» терялись, не показавшись игроку ни разу.
        /// </summary>
        public static readonly System.Collections.Generic.List<string> Chronicle =
            new System.Collections.Generic.List<string>();

        /// <summary>Начало другой кампании (новая игра/загрузка): чужая лента не протекает.</summary>
        public static void ResetChronicle() => Chronicle.Clear();

        public static bool HasPendingBattle => PendingBattle != null;

        public static void ClearBattle()
        {
            PendingBattle = null;
            PendingExpedition = null;
            PendingFinale = false;
            PendingBuffGold = 0;
            // PendingQuest НЕ чистится: квест продолжается в Campaign-сцене после боя.
        }

        public static void ClearQuest()
        {
            PendingQuest = null;
            LastQuestStep = null;
        }

        public static void Reset()
        {
            Campaign = null;
            LastReport = null;
            ClearBattle();
            ClearQuest();
            ResetChronicle();
        }
    }

    /// <summary>
    /// Автосейв (итерация 16): пишется в фиксированные моменты — ход времени и
    /// отправка вылазки. В айронмене автосейв — ЕДИНСТВЕННЫЙ сейв (канон US-16.1:
    /// свободный quicksave в вылазке заблокирован, а автосейв фиксирует решения).
    /// </summary>
    public static class AutoSave
    {
        public static void Write(Campaign campaign)
        {
            if (campaign == null) return;
            SaveSerializer.SaveToFile(SaveSystem.Capture(campaign), SaveSerializer.AutosavePath);
        }
    }
}
