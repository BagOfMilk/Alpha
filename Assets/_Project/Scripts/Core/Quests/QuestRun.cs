using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Checks;
using Game.Core.Economy;
using Game.Core.Factions;
using Game.Core.Items;
using Game.Core.Threats;

namespace Game.Core.Quests
{
    public enum QuestState { Active = 0, Succeeded = 1, Failed = 2 }

    /// <summary>Что произошло на шаге квеста — для UI/лога (число Напряжения наружу не отдаём).</summary>
    public sealed class QuestStepReport
    {
        public string StageId;
        public QuestStageKind Kind;
        public string Text;

        public bool Accepted = true;       // для выбора: прошёл ли гейт варианта
        public bool CheckSuccess;
        public string ResolvedById;        // кто «вывез» проверку
        public int CheckValue;
        public int Threshold;
        public bool WasLethal;             // провал этой проверки был летальным (телеграф US-13.2)
        public bool WasUtility;
        public string CasualtyId;          // пострадавший на летальном провале (US-13.2)
        public bool CasualtyDied;          // true — погиб насовсем; false — Критическое ранение

        public int ChosenOption = -1;
        public readonly List<string> ReactionLines = new List<string>();

        public int NextIndex;
        public bool Terminal;
        public bool QuestSucceeded;
        public readonly List<string> Notes = new List<string>();
    }

    /// <summary>
    /// Прохождение авторской миссии (Эпики 13–14): детерминированный автомат по
    /// этапам. Проверки — через CheckResolver (US-2.6, кости нет); выборы применяют
    /// SocialConsequence + реакции лояльности (US-10.3); терминал начисляет награду
    /// (XP/золото/материалы/именные предметы + соц-часть). Бой ведёт вызывающий код
    /// и возвращает исход в ResolveCombat. Внешние системы null-терпимы.
    /// </summary>
    public sealed class QuestRun
    {
        private readonly BaseState _base;
        private readonly FactionRegistry _factions;
        private readonly ThreatSystem _threats;
        private readonly ICollection<string> _flags;
        private readonly BalanceConfig _cfg;

        public QuestDefinition Def { get; }
        public int CurrentIndex { get; private set; }
        public QuestState State { get; private set; } = QuestState.Active;

        public QuestRun(QuestDefinition def, BaseState baseState, BalanceConfig cfg,
                        FactionRegistry factions = null, ThreatSystem threats = null,
                        ICollection<string> flags = null)
        {
            Def = def ?? throw new System.ArgumentNullException(nameof(def));
            _base = baseState;
            _cfg = cfg;
            _factions = factions;
            _threats = threats;
            _flags = flags;
            CurrentIndex = def.StartIndex;
        }

        public QuestStage Current => Def.StageAt(CurrentIndex);
        public bool IsActive => State == QuestState.Active;

        /// <summary>
        /// Восстановление прогона из сейва (v6): этап и состояние — как были.
        /// Награду терминала НЕ переигрывает (Finalize не зовётся): она уже была
        /// начислена и лежит в том же сейве.
        /// </summary>
        internal void RestoreTo(int index, QuestState state)
        {
            CurrentIndex = index;
            State = state;
        }

        // ---- Проверка (US-13.2) ----
        public QuestStepReport ResolveCheck(IReadOnlyList<Companion> participants)
        {
            var stage = Current;
            var report = NewReport(stage);
            if (stage == null || stage.Kind != QuestStageKind.Check) { report.Notes.Add("не этап проверки"); return report; }

            var result = stage.IsSocial
                ? CheckResolver.ResolveSocial(participants, stage.Approach, stage.Threshold)
                : CheckResolver.Resolve(participants, stage.CheckSkill, stage.Threshold);

            report.CheckSuccess = result.Success;
            report.ResolvedById = result.ResolvedById;
            report.CheckValue = result.Value;
            report.Threshold = stage.Threshold;
            report.WasLethal = stage.Lethal;
            report.WasUtility = stage.Utility;

            if (!result.Success && stage.FailureConsequence != null)
                stage.FailureConsequence.Apply(_factions, _base, _threats, _flags); // мягкий сетбэк

            // Летальная ставка (US-13.2): телеграфированный провал СТОИТ жизни тому,
            // кто вёл проверку. Протагонист вне айронмена бессмертен (как в бою) —
            // получает Критическое ранение вместо смерти.
            if (stage.Lethal && !result.Success && result.ResolvedById != null && _base != null)
            {
                var victim = _base.Roster.Get(result.ResolvedById);
                if (victim != null && victim.IsAlive)
                {
                    report.CasualtyId = victim.Id;
                    // Инвариант «на посту ⇔ доступен»: снимаем с позиции ДО выбытия.
                    // Companion.Kill() чистит только AssignedSlotId бойца, сам слот
                    // продолжает держать AssignedCompanionId — ср. ThreatSystem.ApplyCrisis
                    // и DefectionSystem, где это делают руками по той же причине.
                    if (victim.IsAssigned && _base != null) _base.Unassign(victim.AssignedSlotId);
                    bool canDie = !victim.IsProtagonist || (_cfg != null && _cfg.Ironman);
                    if (canDie)
                    {
                        victim.Kill();
                        _base.Inventory.RecoverGearFrom(victim); // гир возвращается с телом
                        report.CasualtyDied = true;
                        report.Notes.Add($"✝ {victim.DisplayName} погиб(ла) — насовсем.");
                    }
                    else
                    {
                        victim.ApplyInjury(Health.InjuryTier.Critical, _cfg ?? new BalanceConfig(), null);
                        report.Notes.Add($"{victim.DisplayName}: Критическое ранение.");
                    }
                }
            }

            GoTo(result.Success ? stage.OnSuccess : stage.OnFailure, report);
            return report;
        }

        // ---- Выбор (US-10.3) ----
        public bool OptionAvailable(QuestOption option, IReadOnlyList<Companion> participants)
        {
            if (option == null) return false;
            if (option.RequiresSkill != Stats.SkillType.None)
            {
                int best = BestSkill(participants, option.RequiresSkill);
                if (best < option.RequiresSkillLevel) return false;
            }
            if (!string.IsNullOrEmpty(option.RequiresFaction))
            {
                if (_factions == null || !_factions.AtLeast(option.RequiresFaction, option.RequiresBand)) return false;
            }
            // Трейты открывают/закрывают особые опции (US-2.6/10.2).
            if (!string.IsNullOrEmpty(option.RequiresTraitId) && !AnyoneHasTrait(participants, option.RequiresTraitId))
                return false;
            if (!string.IsNullOrEmpty(option.BlockedByTraitId) && AnyoneHasTrait(participants, option.BlockedByTraitId))
                return false;
            // Опция про конкретного напарника закрывается его выбытием (пролог:
            // «прикрыть переговорщика» бессмысленно, если он погиб в бою этапа —
            // как и если он уже ушёл к врагу).
            if (!string.IsNullOrEmpty(option.RequiresAliveCompanionId))
            {
                var target = _base?.Roster.Get(option.RequiresAliveCompanionId);
                if (target == null || !target.IsOnPlayerSide) return false;
            }
            return true;
        }

        private static bool AnyoneHasTrait(IReadOnlyList<Companion> participants, string traitId)
        {
            if (participants == null) return false;
            for (int i = 0; i < participants.Count; i++)
            {
                var c = participants[i];
                // Репутация ушедшего к врагу на игрока не работает: его трейт опцию не открывает.
                if (c != null && c.IsOnPlayerSide && c.Traits.Contains(traitId)) return true;
            }
            return false;
        }

        public QuestStepReport Choose(int optionIndex, IReadOnlyList<Companion> participants)
        {
            var stage = Current;
            var report = NewReport(stage);
            if (stage == null || stage.Kind != QuestStageKind.Choice) { report.Notes.Add("не этап выбора"); return report; }
            if (optionIndex < 0 || optionIndex >= stage.Options.Count) { report.Accepted = false; report.Notes.Add("нет такого варианта"); return report; }

            var option = stage.Options[optionIndex];
            if (!OptionAvailable(option, participants)) { report.Accepted = false; report.Notes.Add("вариант недоступен (гейт)"); return report; }

            report.ChosenOption = optionIndex;
            option.Consequence?.Apply(_factions, _base, _threats, _flags);

            // Видимая рябь: реакции напарников по лояльности (US-10.3).
            // Выбывшие не реагируют: ни реплики трупа, ни лояльности того, кто ушёл к врагу.
            for (int i = 0; i < option.Reactions.Count; i++)
            {
                var r = option.Reactions[i];
                var comp = _base?.Roster.Get(r.CompanionId);
                if (comp == null || !comp.IsOnPlayerSide) continue;
                comp.AdjustLoyalty(r.LoyaltyDelta);
                string verb = r.LoyaltyDelta >= 0 ? "одобряет" : "осуждает";
                report.ReactionLines.Add(r.Line ?? $"{comp.DisplayName} {verb} ({comp.LoyaltyBand})");
            }

            GoTo(option.Next, report);
            return report;
        }

        // ---- Бой (ведёт вызывающий) ----
        public QuestStepReport ResolveCombat(bool won)
        {
            var stage = Current;
            var report = NewReport(stage);
            if (stage == null || stage.Kind != QuestStageKind.Combat) { report.Notes.Add("не этап боя"); return report; }
            GoTo(won ? stage.OnWin : stage.OnLoss, report);
            return report;
        }

        // ---- Внутренности ----
        private void GoTo(int index, QuestStepReport report)
        {
            CurrentIndex = index;
            report.NextIndex = index;
            var next = Current;
            if (next != null && next.Kind == QuestStageKind.Outcome)
                Finalize(next, report);
        }

        private void Finalize(QuestStage outcome, QuestStepReport report)
        {
            State = outcome.Success ? QuestState.Succeeded : QuestState.Failed;
            report.Terminal = true;
            report.QuestSucceeded = outcome.Success;
            report.Notes.Add(outcome.Text ?? (outcome.Success ? "Успех" : "Провал"));
            if (outcome.Reward != null) ApplyReward(outcome.Reward, report);
        }

        private void ApplyReward(QuestReward reward, QuestStepReport report)
        {
            if (_base != null)
            {
                if (reward.Gold != 0) _base.Resources.Add(ResourceType.Gold, reward.Gold);
                if (reward.BuildingMaterial != 0) _base.Resources.Add(ResourceType.BuildingMaterial, reward.BuildingMaterial);
                if (reward.CraftingMaterial != 0) _base.Resources.Add(ResourceType.CraftingMaterial, reward.CraftingMaterial);

                for (int i = 0; i < reward.NamedItems.Count; i++)
                    _base.Inventory.Add(ItemInstance.NamedFrom(reward.NamedItems[i]));

                if (reward.Xp > 0 && _cfg != null)
                    foreach (var c in _base.Roster.All)
                        if (c.IsOnPlayerSide) c.GainXp(reward.Xp, _cfg); // квесты дают XP без гринда боёв (US-5.1); перебежчик не растёт на наших делах
            }

            reward.Social?.Apply(_factions, _base, _threats, _flags);
            if (reward.Xp > 0) report.Notes.Add($"+{reward.Xp} XP");
            if (reward.Gold != 0) report.Notes.Add($"+{reward.Gold} золота");
        }

        private QuestStepReport NewReport(QuestStage stage) => new QuestStepReport
        {
            StageId = stage?.Id,
            Kind = stage?.Kind ?? QuestStageKind.Outcome,
            Text = stage?.Text
        };

        private static int BestSkill(IReadOnlyList<Companion> participants, Stats.SkillType skill)
        {
            int best = 0;
            if (participants != null)
                for (int i = 0; i < participants.Count; i++)
                {
                    var c = participants[i];
                    if (c == null || !c.IsOnPlayerSide) continue; // как в CheckResolver: скил перебежчика не наш
                    int v = c.GetSkill(skill) + c.CheckModifierFor(skill);
                    if (v > best) best = v;
                }
            return best;
        }
    }
}
