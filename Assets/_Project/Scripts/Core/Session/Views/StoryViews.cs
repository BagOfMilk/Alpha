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
    }
}
