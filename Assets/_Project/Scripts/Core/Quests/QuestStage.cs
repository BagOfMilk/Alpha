using System.Collections.Generic;
using Game.Core.Checks;
using Game.Core.Factions;
using Game.Core.Stats;

namespace Game.Core.Quests
{
    public enum QuestStageKind
    {
        Check = 0,   // детерминированная проверка (скил/соц) с ветвлением
        Choice = 1,  // выбор игрока с последствиями
        Combat = 2,  // бой (ведёт вызывающий код через CombatState)
        Outcome = 3  // терминал: успех/провал + награда
    }

    /// <summary>Видимая реакция напарника на выбор (US-10.3: социальная рябь читается на глазах).</summary>
    public sealed class CompanionReaction
    {
        public string CompanionId;
        public int LoyaltyDelta;
        public string Line;

        public CompanionReaction(string companionId, int loyaltyDelta, string line = null)
        {
            CompanionId = companionId;
            LoyaltyDelta = loyaltyDelta;
            Line = line;
        }
    }

    /// <summary>Вариант выбора: опц. гейт (скил/фракция/трейт), соц-последствия, реакции, переход.</summary>
    public sealed class QuestOption
    {
        public string Label;
        public SkillType RequiresSkill = SkillType.None;
        public int RequiresSkillLevel;
        public string RequiresFaction;
        public FactionBand RequiresBand = FactionBand.Neutral;

        /// <summary>Трейты открывают/закрывают особые опции (US-2.6): нужен носитель трейта…</summary>
        public string RequiresTraitId;
        /// <summary>…или наоборот — носитель трейта в отряде закрывает вариант.</summary>
        public string BlockedByTraitId;

        public SocialConsequence Consequence;
        public readonly List<CompanionReaction> Reactions = new List<CompanionReaction>();
        public int Next;

        public QuestOption(string label, int next)
        {
            Label = label;
            Next = next;
        }

        public QuestOption GateSkill(SkillType skill, int level) { RequiresSkill = skill; RequiresSkillLevel = level; return this; }
        public QuestOption GateFaction(string factionId, FactionBand band) { RequiresFaction = factionId; RequiresBand = band; return this; }
        public QuestOption GateTrait(string traitId) { RequiresTraitId = traitId; return this; }
        public QuestOption BlockTrait(string traitId) { BlockedByTraitId = traitId; return this; }
        public QuestOption With(SocialConsequence consequence) { Consequence = consequence; return this; }
        public QuestOption React(string companionId, int loyaltyDelta, string line = null)
        {
            Reactions.Add(new CompanionReaction(companionId, loyaltyDelta, line));
            return this;
        }
    }

    /// <summary>
    /// Этап авторской миссии (Эпик 13). Ветвление — по индексам в Stages квеста.
    /// Честность проверок (US-13.2): летальный провал только телеграфирован
    /// (<see cref="Lethal"/>) и обходим; утилитарный (<see cref="Utility"/>) даёт
    /// мягкий сетбэк + альтернативный путь (OnFailure), а не тупик.
    /// </summary>
    public sealed class QuestStage
    {
        public string Id;
        public string Text;
        public QuestStageKind Kind;

        // -- Check --
        public bool IsSocial;
        public SkillType CheckSkill = SkillType.None;
        public CheckApproach Approach;
        public int Threshold;
        public bool Lethal;   // телеграф: провал летален (US-13.2) — обязан быть обходим
        public bool Utility;  // утилитарная: soft-fail → альтернатива
        public int OnSuccess;
        public int OnFailure;
        public SocialConsequence FailureConsequence;

        // -- Choice --
        public readonly List<QuestOption> Options = new List<QuestOption>();

        // -- Combat --
        public string EncounterId;
        public int OnWin;
        public int OnLoss;

        // -- Outcome --
        public bool Success;
        public QuestReward Reward;

        public bool IsTerminal => Kind == QuestStageKind.Outcome;

        // ---- Фабрики ----
        public static QuestStage SkillCheck(string id, string text, SkillType skill, int threshold, int onSuccess, int onFailure)
            => new QuestStage { Id = id, Text = text, Kind = QuestStageKind.Check, CheckSkill = skill, Threshold = threshold, OnSuccess = onSuccess, OnFailure = onFailure };

        public static QuestStage SocialCheck(string id, string text, CheckApproach approach, int threshold, int onSuccess, int onFailure)
            => new QuestStage { Id = id, Text = text, Kind = QuestStageKind.Check, IsSocial = true, Approach = approach, Threshold = threshold, OnSuccess = onSuccess, OnFailure = onFailure };

        public static QuestStage ChoiceStage(string id, string text)
            => new QuestStage { Id = id, Text = text, Kind = QuestStageKind.Choice };

        public static QuestStage CombatStage(string id, string text, string encounterId, int onWin, int onLoss)
            => new QuestStage { Id = id, Text = text, Kind = QuestStageKind.Combat, EncounterId = encounterId, OnWin = onWin, OnLoss = onLoss };

        public static QuestStage OutcomeStage(string id, string text, bool success, QuestReward reward = null)
            => new QuestStage { Id = id, Text = text, Kind = QuestStageKind.Outcome, Success = success, Reward = reward };

        public QuestStage AsLethal() { Lethal = true; return this; }
        public QuestStage AsUtility() { Utility = true; return this; }
        public QuestStage FailCost(SocialConsequence consequence) { FailureConsequence = consequence; return this; }
        public QuestStage Option(QuestOption option) { Options.Add(option); return this; }
    }
}
