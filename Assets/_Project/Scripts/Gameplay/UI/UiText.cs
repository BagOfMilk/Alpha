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
        public const string QuestNoSquad = "Некому идти: бойцы выбыли или на лечении. Отложи дело и подлечи отряд.";
        public const string QuestPostpone = "Отложить (в город)";
        public const string QuestResume = "Продолжить: {0}";
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

        // Снаряжение и крафт (US-6.1/6.2/6.3)
        public const string Equip = "Надеть";
        public const string Unequip = "Снять";
        public const string SlotEmpty = "— пусто —";
        public const string StashEmpty = "Сташ пуст: трофеи приносят вылазки и квесты.";
        public const string CraftHint = "Улучшение: {0} крафт-компонента (в наличии {1}).";
        public const string CraftNeedsWorkshop = "Улучшение недоступно: нужна Мастерская.";
        public const string CraftUpgrade = "Улучшить";
        public const string CraftFailed = "Не вышло: ";

        // Хроника города (US-8.2/11.x)
        public const string ChronicleTitle = "Хроника";
        public const string ChronicleEmpty = "Пока тихо.";
        public const string IncidentResolved = "улажено";
        public const string IncidentFailed = "провал";
        public const string IncidentNobody = "пост пустовал";
        public const string ChronicleRecovered = "снова в строю";
        public const string ChronicleBuilt = "достроено:";
        public const string ChronicleCityGrew = "ГОРОД ВЫРОС: тир {0} — открылись новые точки на карте";

        // Фракции и влияние (Эпик 10)
        public const string FactionsTitle = "Отношения";
        public const string InfluenceLine = "Влияние: {0} · репутация города: {1}";
        public const string DiplomacyTarget = "Дипломатия: кого тянем";
        public const string CouncilResultOk = "Готово: ";
        public const string CouncilResultFail = "Отказ: ";

        // Карта: причины блокировки узла (US-1.1/17.3)
        public const string LockNeedTier = "нужен тир {0}";
        public const string LockNeedStory = "по сюжету";
        public const string LockDone = "пройдено";

        /// <summary>Полосы отношений с фракцией (enum — идентификатор, не текст игроку).</summary>
        public static string FactionBandName(Game.Core.Factions.FactionBand band)
        {
            switch (band)
            {
                case Game.Core.Factions.FactionBand.Hostile: return "враждебны";
                case Game.Core.Factions.FactionBand.Cold: return "холодны";
                case Game.Core.Factions.FactionBand.Neutral: return "нейтральны";
                case Game.Core.Factions.FactionBand.Warm: return "расположены";
                case Game.Core.Factions.FactionBand.Allied: return "союзники";
                default: return band.ToString();
            }
        }

        /// <summary>Полосы репутации города (US-17.2: наружу — полоса, не число).</summary>
        public static string RepBandName(Game.Core.Factions.ReputationBand band)
        {
            switch (band)
            {
                case Game.Core.Factions.ReputationBand.Unknown: return "нас не знают";
                case Game.Core.Factions.ReputationBand.Known: return "о нас слышали";
                case Game.Core.Factions.ReputationBand.Respected: return "нас уважают";
                case Game.Core.Factions.ReputationBand.Renowned: return "о нас говорят везде";
                default: return band.ToString();
            }
        }

        public static string RarityName(Game.Core.Items.Rarity rarity)
        {
            switch (rarity)
            {
                case Game.Core.Items.Rarity.Common: return "обычн.";
                case Game.Core.Items.Rarity.Uncommon: return "необычн.";
                case Game.Core.Items.Rarity.Rare: return "редк.";
                case Game.Core.Items.Rarity.Epic: return "эпич.";
                default: return rarity.ToString();
            }
        }

        public static string CrisisName(Game.Core.Threats.CrisisEffect crisis)
        {
            switch (crisis)
            {
                case Game.Core.Threats.CrisisEffect.PopulationExodus: return "отток населения";
                case Game.Core.Threats.CrisisEffect.KillCompanion: return "погиб напарник";
                case Game.Core.Threats.CrisisEffect.HostileFaction: return "фракция встала против города";
                default: return crisis.ToString();
            }
        }

        /// <summary>Почему действие совета не прошло — человеческим языком.</summary>
        public static string CouncilFailReason(Game.Core.Council.CouncilActionResult result)
        {
            switch (result)
            {
                case Game.Core.Council.CouncilActionResult.CannotAffordGold: return "не хватает золота";
                case Game.Core.Council.CouncilActionResult.CannotAffordInfluence: return "не хватает влияния";
                case Game.Core.Council.CouncilActionResult.OnCooldown: return "ещё не готовы";
                case Game.Core.Council.CouncilActionResult.Locked: return "недоступно";
                case Game.Core.Council.CouncilActionResult.InvalidTarget: return "не та цель";
                default: return result.ToString();
            }
        }

        /// <summary>Русские имена слотов экипировки.</summary>
        public static string SlotName(Game.Core.Items.EquipSlot slot)
        {
            switch (slot)
            {
                case Game.Core.Items.EquipSlot.Weapon: return "Оружие";
                case Game.Core.Items.EquipSlot.Armor: return "Броня";
                case Game.Core.Items.EquipSlot.Accessory: return "Аксессуар";
                default: return slot.ToString();
            }
        }

        /// <summary>Короткие имена производных статов — для строк предметов.</summary>
        public static string StatShort(Game.Core.Stats.DerivedStat stat)
        {
            switch (stat)
            {
                case Game.Core.Stats.DerivedStat.MaxHp: return "HP";
                case Game.Core.Stats.DerivedStat.ActionPoints: return "AP";
                case Game.Core.Stats.DerivedStat.Accuracy: return "точн.";
                case Game.Core.Stats.DerivedStat.Defense: return "защ.";
                case Game.Core.Stats.DerivedStat.Initiative: return "иниц.";
                case Game.Core.Stats.DerivedStat.CritChance: return "крит";
                case Game.Core.Stats.DerivedStat.Armor: return "броня";
                case Game.Core.Stats.DerivedStat.Resolve: return "воля";
                case Game.Core.Stats.DerivedStat.Carry: return "перенос";
                default: return stat.ToString();
            }
        }

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
