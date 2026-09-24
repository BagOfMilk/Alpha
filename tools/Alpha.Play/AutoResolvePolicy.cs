using System.Collections.Generic;
using Game.Core.Loop;
using Game.Core.Session.Bots;
using Game.Core.Session.Views;

namespace Alpha.Play
{
    /// <summary>
    /// Фікс-ревью пакета D2 (minor): §1.1 вимагає, щоб консоль грала бої ЛИШЕ
    /// «Автобоєм» незалежно від того, яка політика веде решту доби. Раніше
    /// <c>--auto</c> підставляв готову політику (Pacifist/Bloody) напряму —
    /// <c>PacifistPolicy.ChooseAutoResolve</c> повертає false, тож якби вона
    /// колись і дійшла до справжнього покрокового бою, консоль тихо зламала б
    /// власну гарантію §1.1. Декоратор форсує «Автобій», лишаючи решту
    /// характеру (шлях/вилазка/розстановка/патруль/делве) незмінною.
    /// </summary>
    internal sealed class AutoResolvePolicy : IBotPolicy
    {
        private readonly IBotPolicy _inner;

        public AutoResolvePolicy(IBotPolicy inner)
        {
            _inner = inner;
        }

        public string Name => _inner.Name;

        public IncidentPath ChooseIncidentPath(PendingOfferView offer) => _inner.ChooseIncidentPath(offer);

        public int ChooseQuestOption(QuestOfferView offer) => _inner.ChooseQuestOption(offer);

        public bool ChoosePatrol(SessionView view) => _inner.ChoosePatrol(view);

        public IReadOnlyDictionary<string, string> ChooseAssignments(RosterView roster, CityView city)
            => _inner.ChooseAssignments(roster, city);

        public ExpeditionChoice? ChooseExpedition(SessionView view) => _inner.ChooseExpedition(view);

        public CombatAction ChooseCombatAction(BattleView battle) => _inner.ChooseCombatAction(battle);

        /// <summary>Завжди true — консоль (і --auto теж) грає бої лише «Автобоєм» (§1.1).</summary>
        public bool ChooseAutoResolve(BattleView battle) => true;

        public bool ChoosePushDeeper(DungeonView view) => _inner.ChoosePushDeeper(view);
    }
}
