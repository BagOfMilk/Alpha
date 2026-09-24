using System.Collections.Generic;
using Game.Core.Characters.Creation;

namespace Game.Core.Session.Views
{
    public sealed class FactionSummary
    {
        public string Id;
        public string DisplayName;
        public string Band;
    }

    public sealed class FactionsView
    {
        public IReadOnlyList<FactionSummary> Factions;
    }

    public sealed class ReadinessView
    {
        public string Band;
        public int MilestonesReached;
        public int MilestonesTotal;
    }

    public sealed class SkillChangeView
    {
        public string SkillKey;
        public int From;
        public int To;
    }

    public sealed class BuildPlannerView
    {
        public string CompanionId;
        public int PointsAvailable;
        public IReadOnlyList<SkillChangeView> Preview;
    }

    public sealed class ProtagonistCreationView
    {
        public string Name;
        public Gender Gender;
        public string BackgroundId;
        public IReadOnlyList<string> AvailableBackgrounds;
    }

    public sealed class SummaryView
    {
        public IReadOnlyList<CompanionSummary> FinalRoster;
        public IReadOnlyList<string> BuiltBuildings;
        public EconomyView Wallet;
        public IReadOnlyList<FactionSummary> Factions;
        public string FinaleOutcomeKey;
    }

    public sealed class SceneStepView
    {
        public string ActorId, SecondActorId, SpeakerId, LineKey, EffectKey;
        public bool IsFinished;
        public string TransitionKey;

        /// <summary>
        /// Поправка №7.8: сцена стоит на выборе реплики — <see cref="Options"/>
        /// несёт варианты (реюз <see cref="DecisionOptionView"/> — та же форма,
        /// что у пропозиції квесту/інциденту: ключ тексту, скіл/поріг/полоса-
        /// прев'ю, жодного прихованого числа, R17). Команда розв'язку —
        /// <c>GameSession.ChooseSceneOption(int)</c>.
        /// </summary>
        public bool IsChoice;

        /// <summary>Id поточного вибору (для журналу механік/ботів) — пусто, якщо IsChoice=false.</summary>
        public string ChoiceId;

        public IReadOnlyList<DecisionOptionView> Options;
    }

    /// <summary>
    /// Журнал механік для тестера (Поправка №7.8): які механіки вже
    /// трапились у цій партії, які ще ні, і як їх викликати. Дані,
    /// обчислені з кумулятивних ключів подій сесії — жодного прихованого
    /// числа.
    /// </summary>
    public sealed class MechanicJournalEntryView
    {
        public string Id;
        public string TitleKey;
        public string HintKey;
        public bool Seen;
    }
}
