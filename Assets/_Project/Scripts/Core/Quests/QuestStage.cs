using System.Collections.Generic;
using Game.Core.Checks;

namespace Game.Core.Quests
{
    /// <summary>Що робить етап квесту.</summary>
    public enum QuestStageKind
    {
        /// <summary>Детермінована перевірка через існуючий CheckResolver — 4 полоси з першого рядка (R6), не бінарний успіх.</summary>
        Check = 0,
        /// <summary>Вибір гравця: кожен варіант — свій перехід і свій наслідок.</summary>
        Choice = 1,
        /// <summary>Термінал: квест завершився (Succeeded/Failed) з фінальним наслідком.</summary>
        Outcome = 2
    }

    /// <summary>
    /// Варіант вибору на етапі-Choice. Гейтинг — за бажанням: опційний прапор
    /// (флаг STORY, вже виставлений раніше) відкриває/закриває варіант. Гейтинг
    /// за скілом/фракцією свідомо не заведений: жоден квест цієї сборки його не
    /// потребує, а фракції (B5) у цьому робочому дереві ще не існують.
    /// </summary>
    public sealed class QuestOption
    {
        public string TextKey;
        public int Next;
        public QuestConsequence Consequence;

        /// <summary>Варіант доступний, лише якщо цей прапор УЖЕ стоїть (null — завжди доступний).</summary>
        public string RequiresFlag;
        /// <summary>Варіант закритий, якщо цей прапор УЖЕ стоїть.</summary>
        public string BlockedByFlag;

        public QuestOption(string textKey, int next, QuestConsequence consequence = null)
        {
            TextKey = textKey;
            Next = next;
            Consequence = consequence;
        }

        public QuestOption GateFlag(string flagId) { RequiresFlag = flagId; return this; }
        public QuestOption BlockFlag(string flagId) { BlockedByFlag = flagId; return this; }

        public bool IsAvailable(Story.StoryFlags flags)
        {
            if (!string.IsNullOrEmpty(RequiresFlag) && (flags == null || !flags.Get(RequiresFlag))) return false;
            if (!string.IsNullOrEmpty(BlockedByFlag) && flags != null && flags.Get(BlockedByFlag)) return false;
            return true;
        }
    }

    /// <summary>
    /// Один етап квесту (Core/Quests, R6). Текстового вмісту не несе — лише
    /// ключі (R7): Core не показує гравцю нічого, крім даних.
    /// </summary>
    public sealed class QuestStage
    {
        public string Id;
        public string TextKey;
        public QuestStageKind Kind;

        // ---- Check ----
        public SkillKey CheckSkill;
        public ApproachForm Approach = ApproachForm.Neutral;
        public int Threshold;
        /// <summary>Тема для лічильника повторів (IRepeatTracker). Порожньо — береться "<questId>.<stageId>".</summary>
        public string TopicId;
        public string RequiredPositionId;

        /// <summary>
        /// Куди веде кожна з 4 полос (індекс = (int)OutcomeBand: Worst,Base,Good,Best).
        /// Немає "успіху/провалу" бінарно (R6) — є лестниця, як у CheckResolver.
        /// </summary>
        public int[] NextByBand = { -1, -1, -1, -1 };

        /// <summary>Наслідок для КОЖНОЇ з 4 полос (може бути null — тоді порожній).</summary>
        public QuestConsequence[] ConsequenceByBand = new QuestConsequence[4];

        // ---- Choice ----
        public readonly List<QuestOption> Options = new List<QuestOption>();

        // ---- Outcome (термінал) ----
        public bool Success;
        public QuestConsequence Consequence;

        public bool IsTerminal => Kind == QuestStageKind.Outcome;

        // ---- фабрики ----

        public static QuestStage Check(string id, string textKey, SkillKey skill, int threshold,
            ApproachForm approach, int[] nextByBand, QuestConsequence[] consequenceByBand = null,
            string topicId = null, string requiredPositionId = null)
        {
            return new QuestStage
            {
                Id = id,
                TextKey = textKey,
                Kind = QuestStageKind.Check,
                CheckSkill = skill,
                Threshold = threshold,
                Approach = approach,
                TopicId = topicId,
                RequiredPositionId = requiredPositionId,
                NextByBand = nextByBand ?? new[] { -1, -1, -1, -1 },
                ConsequenceByBand = consequenceByBand ?? new QuestConsequence[4]
            };
        }

        public static QuestStage ChoiceStage(string id, string textKey)
            => new QuestStage { Id = id, TextKey = textKey, Kind = QuestStageKind.Choice };

        public QuestStage Option(QuestOption option)
        {
            if (option != null) Options.Add(option);
            return this;
        }

        public static QuestStage OutcomeStage(string id, string textKey, bool success, QuestConsequence consequence = null)
            => new QuestStage { Id = id, TextKey = textKey, Kind = QuestStageKind.Outcome, Success = success, Consequence = consequence };
    }
}
