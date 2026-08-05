using Game.Core.Combat;
using Game.Core.Expeditions;
using Game.Core.Quests;
using Game.Core.Saves;

namespace Game.Gameplay
{
    /// <summary>ЧЕЙ бой идёт в Battle-сцене: от этого зависит, как резолвится исход.</summary>
    public enum PendingBattleKind
    {
        None = 0,
        Expedition = 1, // бой вылазки (или финал) → ConcludeExpedition + отчёт возвращения
        Quest = 2       // боевой этап квеста → QuestRun.ResolveCombat
    }

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

        /// <summary>
        /// Хроника и телеметрия этого отчёта уже записаны. Экран отчёта рисуется
        /// заново при каждом пересоздании контроллера, а строки ленты и события
        /// воронки — не идемпотентны: без флага один поход множил бы их на каждый
        /// показ панели.
        /// </summary>
        public static bool LastReportNarrated;

        /// <summary>Идущий бой — ФИНАЛЬНАЯ битва: исход резолвится FinalBattle.Resolve.</summary>
        public static bool PendingFinale;

        /// <summary>Бонус золота от «Снаряжения экспедиции» (совет) — банкуется при победе.</summary>
        public static int PendingBuffGold;

        /// <summary>
        /// Активный прогон квеста (пролог/доска, US-14.3/17.1): живёт МЕЖДУ сценами —
        /// бой квестового этапа идёт в Battle, остальные этапы играются в Campaign.
        /// null — квест не идёт. Battle-контроллер отличает квестовый бой от вылазки
        /// по BattleKind. ХРАНИТСЯ В КАМПАНИИ, а не здесь: прогон обязан переживать
        /// и смену сцены, и сейв — иначе загрузка начинала квест заново, а уже
        /// применённые последствия выбора оставались (их фармили перезагрузкой).
        /// </summary>
        public static QuestRun PendingQuest
        {
            get => Campaign != null ? Campaign.ActiveQuest : null;
            set { if (Campaign != null) Campaign.SetActiveQuest(value, PendingArcId); }
        }

        /// <summary>Отчёт последнего шага квеста (реплики/чек) — для панели квеста после боя.</summary>
        public static QuestStepReport LastQuestStep;

        /// <summary>Id арки, чья глава сейчас играется (US-9.5); null — обычный квест.</summary>
        public static string PendingArcId
        {
            get => Campaign != null ? Campaign.ActiveArcId : null;
            set { if (Campaign != null) Campaign.SetActiveQuest(Campaign.ActiveQuest, value); }
        }

        /// <summary>
        /// Кто именно вышел боссом расплаты (US-9.4): пейоф адресный, иначе оба
        /// пути победы («бой» и «закрытие квеста») снимали бы «первого в очереди» —
        /// и второй перебежчик выбывал без боя, отдав гир даром.
        /// </summary>
        public static string PendingBossId;

        /// <summary>
        /// Пейоф расплаты уже применён в Battle-сцене — Campaign-сцене остаётся
        /// только СООБЩИТЬ о нём (хроника/телеметрия), а не применять второй раз.
        /// </summary>
        public static bool BossPayoffApplied;

        /// <summary>
        /// Хроника города: живёт в статике, а не в контроллере — иначе лента
        /// обнулялась КАЖДЫМ походом в Battle-сцену (контроллер пересоздаётся),
        /// и события дороги «туда» терялись, не показавшись игроку ни разу.
        /// </summary>
        public static readonly System.Collections.Generic.List<string> Chronicle =
            new System.Collections.Generic.List<string>();

        /// <summary>Начало другой кампании (новая игра/загрузка): чужая лента не протекает.</summary>
        public static void ResetChronicle() => Chronicle.Clear();

        /// <summary>
        /// Тип идущего боя. Раньше Battle-сцена угадывала его по «висит ли квест», и
        /// отложенный квест перехватывал исход боя ВЫЛАЗКИ: ConcludeExpedition не
        /// вызывался, ActiveExpedition залипал навсегда (вылазки и финал становились
        /// недоступны), а боевой этап квеста «проходился» чужим боем.
        /// </summary>
        public static PendingBattleKind BattleKind;

        public static bool HasPendingBattle => PendingBattle != null;

        public static void ClearBattle()
        {
            PendingBattle = null;
            PendingExpedition = null;
            PendingFinale = false;
            PendingBuffGold = 0;
            BattleKind = PendingBattleKind.None;
            // PendingQuest НЕ чистится: квест продолжается в Campaign-сцене после боя.
        }

        public static void ClearQuest()
        {
            PendingQuest = null;
            LastQuestStep = null;
            PendingArcId = null;
            PendingBossId = null;
            BossPayoffApplied = false;
        }

        public static void Reset()
        {
            Campaign = null;
            LastReport = null;
            LastReportNarrated = false;
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

        /// <summary>
        /// Чекпойнт на ВЫХОДЕ вылазки (айронмен, US-16.1): фиксирует само решение
        /// идти. Помечается «бой не доигран» — загрузка резолвит вылазку отступлением,
        /// а не возвращает отряд домой бесплатно. Без этого выход из игры посреди
        /// проигрышного боя откатывал решение и не ловился телеметрией save-scum.
        /// </summary>
        public static void WriteDepartCheckpoint(Campaign campaign)
        {
            if (campaign == null) return;
            var data = SaveSystem.Capture(campaign);
            data.expeditionUnresolved = true;
            SaveSerializer.SaveToFile(data, SaveSerializer.AutosavePath);
        }
    }
}
