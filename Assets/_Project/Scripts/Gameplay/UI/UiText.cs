namespace Game.Gameplay.UI
{
    /// <summary>
    /// ВСЕ строки кампанийного UI — в одном месте (дисциплина локализации без
    /// локализации: код экранов оперирует ключами-константами, замена на таблицы
    /// Localization позже — механическая). Тексты Core (контент) сюда не входят.
    /// </summary>
    public static class UiText
    {
        // Меню
        public const string NewGame = "Новая игра";
        public const string Ironman = "Айронмен (протагонист смертен, ачивки)";
        public const string ContinueAutosave = "Продолжить (автосейв)";
        public const string SlotPrefix = "Слот ";
        public const string EmptySlot = " — пусто";
        public const string BattleSandbox = "Песочница боя";

        // Создание протагониста
        public const string CreateTitle = "Создание протагониста-лидера";
        public const string PickBackground = "Бэкграунд (осмысленно разный старт):";
        public const string AttrPoints = "Очки атрибутов: ";
        public const string SkillPoints = "Очки скилов: ";
        public const string StartCampaign = "Начать кампанию";
        public const string NamePlaceholder = "Имя лидера";

        // Город
        public const string WaitDay = "Ждать день";
        public const string ToWorldMap = "Карта мира";
        public const string SaveToSlot = "Сохранить в слот ";
        public const string CouncilTitle = "Совет";
        public const string BuildTitle = "Стройка";
        public const string RosterTitle = "Ростер";
        public const string AssignTitle = "Позиции";
        public const string Unassigned = "— пусто —";
        public const string BuiltMark = " (построено)";
        public const string InProgressMark = " (строится)";

        // Карта / вылазка
        public const string MapTitle = "Карта мира: выбери точку";
        public const string LockedNodes = "Недоступно (тир города/сюжет):";
        public const string SquadTitle = "Отряд (до 4):";
        public const string Depart = "Выступить";
        public const string BackToCity = "В город";

        // Отчёт возвращения
        public const string ReportTitle = "Возвращение отряда";
        public const string ReportVictory = "Победа. Отряд возвращается с добычей.";
        public const string ReportDefeat = "Поражение. Отряд отступил с потерями.";
        public const string GameOverIronman = "КОНЕЦ КАМПАНИИ: лидер погиб (айронмен).";
        public const string CampaignWon = "ШТУРМ ОТБИТ. Город выстоял — кампания выиграна.";
        public const string CampaignLost = "ОБОРОНА ПАЛА. Кампания окончена.";

        // Бой (кампанийный)
        public const string ContinueAfterBattle = "Продолжить";
    }
}
