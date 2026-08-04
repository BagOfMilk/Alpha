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
        public const string NoEffectYetMark = " — эффект позже";

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

        // Квесты (US-14.3/17.1)
        public const string QuestToBattle = "В бой";
        public const string QuestContinue = "Продолжить";
        public const string QuestCheckPrefix = "Проверка: ";
        public const string QuestLethalMark = "⚠ Смертельная ставка: провал может стоить жизни.";
        public const string QuestInWork = " (в работе)";
        public const string QuestCompletedPrefix = "Завершено: ";
        public const string QuestBusyNote = "Сначала закончи текущее дело.";
        public const string QuestNoSquad = "Некому идти: бойцы выбыли или на лечении. Вернись позже.";
        public const string IntroAck = "Осмотреться в поселении";
        public const string GateNeedSkill = " (нужно: {0} ≥ {1})";
        public const string GateNeedFaction = " (нужна репутация: {0} ≥ {1})";
        public const string GateNeedTrait = " (нужен трейт: {0})";
        public const string GateBlockedByTrait = " (закрыто трейтом: {0})";
        public const string GateNeedAlive = " ({0} погиб)";

        // Персонаж (US-2.3/5.1: трата очков навыков — необратима)
        public const string CharacterTitle = "Боец";
        public const string CharacterBack = "В город";
        public const string SkillPointsPool = "Нераспределённых очков: ";
        public const string NoSkillPoints = "Очки навыков приходят с уровнями (вылазки, квесты, дежурства).";
        public const string SpendIrreversible = "Вложение необратимо — респека нет.";
        public const string UnlocksPerk = " → перк «{0}»";
        public const string UnlocksAbility = " → приём «{0}»";
        public const string XpLine = "Уровень {0} · опыт {1}/{2}";
        public const string XpMaxLine = "Уровень {0} (максимум)";
        public const string TraitsNone = "—";
        public const string SkillAtCap = " — максимум";
        public const string SkillOnlyAccuracy = " → дальше только точность и проверки";
        public const string NoTrainDead = "Боец погиб. Очки навыков не тратятся.";
        public const string NoTrainAntagonist = "Ушёл в антагонисты. Очки навыков не тратятся.";
        public const string ToMainMenu = "В главное меню";
        public const string SavedToAutosave = "Кампания записана в автосейв — «Продолжить» вернёт в город.";

        /// <summary>Русские имена навыков (enum — идентификатор, не текст для игрока).</summary>
        public static string SkillName(Game.Core.Stats.SkillType skill)
        {
            switch (skill)
            {
                case Game.Core.Stats.SkillType.Ranged: return "Стрелковое";
                case Game.Core.Stats.SkillType.Melee: return "Ближний бой";
                case Game.Core.Stats.SkillType.Tactics: return "Тактика";
                case Game.Core.Stats.SkillType.Hacking: return "Взлом";
                case Game.Core.Stats.SkillType.Mechanics: return "Механика";
                case Game.Core.Stats.SkillType.Survival: return "Выживание";
                case Game.Core.Stats.SkillType.Medicine: return "Медицина";
                case Game.Core.Stats.SkillType.Persuasion: return "Убеждение";
                case Game.Core.Stats.SkillType.Intimidation: return "Запугивание";
                case Game.Core.Stats.SkillType.Trade: return "Торговля";
                default: return skill.ToString();
            }
        }

        public static string AttributeName(Game.Core.Stats.AttributeType attribute)
        {
            switch (attribute)
            {
                case Game.Core.Stats.AttributeType.Strength: return "Сила";
                case Game.Core.Stats.AttributeType.Agility: return "Ловкость";
                case Game.Core.Stats.AttributeType.Wits: return "Смекалка";
                case Game.Core.Stats.AttributeType.Will: return "Воля";
                default: return attribute.ToString();
            }
        }
    }
}
