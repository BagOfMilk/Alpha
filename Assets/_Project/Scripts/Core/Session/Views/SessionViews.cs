using System.Collections.Generic;
using Game.Core.Characters;
using Game.Core.Loop;

namespace Game.Core.Session.Views
{
    /// <summary>
    /// Зведений публічний стан сесії (§4.2 TEST_BUILD.md). Жодного прихованого
    /// числа: <c>TensionBand</c>/<c>CrowdBand</c> — рядки-ключі (R7), не
    /// сире значення шкали (інваріант 3) — див. allow-list §4.9.
    /// </summary>
    public sealed class SessionView
    {
        public SessionState State;
        public int Day;
        public DayPhase Phase;
        public int Tier;
        public string CrowdBand;
        public string TensionBand;
        public int DaysInBand;
        public bool IsFreePlay;
        public bool IsPatrolling;
    }

    /// <summary>Гаманець — єдині числа, дозволені allow-list'ом (R17).</summary>
    public sealed class EconomyView
    {
        public int Gold;
        public int Materials;
        public int Food;
    }

    public sealed class BuildingView
    {
        public string Id;
        public int StageOf; // 0..5
    }

    public sealed class CityView
    {
        public IReadOnlyList<BuildingView> Built;
        public IReadOnlyList<BuildingView> InProgress;
        public bool RaidReady;
        public bool SettlersReady;

        /// <summary>Тест-збірка (Поправка №7.8, п.3): кожна будівля будується рівно 1 добу, а не за <c>BuildingDefinition.Days</c> — картка вкладки «Будівлі» показує саме цей ефективний термін, коли прапорець true.</summary>
        public bool TestBuildOneDayConstruction;

        /// <summary>
        /// Id відкритих постів. Закритий пост (його будівля ще не стоїть) не
        /// приймає людей — вкладка «Пости» показує причину замість кнопок
        /// «Призначити», які раніше мовчки нічого не робили.
        /// </summary>
        public IReadOnlyList<string> OpenPosts;
    }

    public sealed class RosterView
    {
        public IReadOnlyList<CompanionSummary> Companions;
    }

    public sealed class CompanionSummary
    {
        public string Id;
        public string DisplayName;
        public CompanionStatus Status;
        public string AssignedSlotId;
        public int Level;

        /// <summary>null — не напарник (напр. фольклорний NPC без Loyalty на карті).</summary>
        public LoyaltyBand? Loyalty;

        public IReadOnlyList<string> Equipped;
        public int ScarCount;
    }

    public sealed class SignalLineView
    {
        public string Channel;
        public string TopicId;
        public IReadOnlyList<string> Tags;
    }

    public sealed class SignalsFeed
    {
        public IReadOnlyList<SignalLineView> Lines;
    }

    public sealed class SaveSlotView
    {
        public int Slot;
        public bool Occupied;
        public string Headline;
        public int Day;
    }
}
