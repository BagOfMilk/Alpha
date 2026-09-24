using System.Collections.Generic;
using Game.Core.Signals;
using Game.Core.World;

namespace Game.Core.Session.Views
{
    /// <summary>
    /// Публічний підсумок фази дня (§4.1 TEST_BUILD.md: повертається
    /// <c>AdvanceDay</c>/<c>ResolveIncident</c>/<c>ResolveQuestChoice</c>/
    /// <c>ReactToCrisis</c>/<c>AdvanceNight</c>/<c>ResolveFinale</c>).
    ///
    /// Обгортка над <see cref="Loop.DayReport"/>: несе лише те, що вже й так
    /// публічне там (<c>Signals</c>/<c>Incidents</c> — безпечні типи без
    /// прихованих чисел), а <c>Pending</c> перетворений у View-тип, щоб
    /// сесійний контракт не змушував Game.Gameplay знати про
    /// <see cref="Loop.PendingDecision"/>/<see cref="Loop.IncidentPath"/> напряму.
    /// </summary>
    public sealed class DayReportView
    {
        public int Day;
        public Loop.DayPhase Phase;
        public SignalDigest Signals;
        public IReadOnlyList<IncidentOutcome> Incidents;
        public PendingOfferView Pending;

        public bool AwaitsDecision => Pending != null;
    }
}
