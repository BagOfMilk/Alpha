using System.Collections.Generic;
using Game.Core.Expeditions;
using Game.Core.Loop;
using Game.Core.Session.Views;

namespace Game.Core.Session.Bots
{
    /// <summary>
    /// Рачительний хозяин (§4.10, "переносить Steward.Act"): обережний тихий
    /// шлях, коли є кому взятися, кровавий — лише як останній засіб, коли тихого
    /// кандидата нема. Розстановка — за замовчуванням (§4.2 не показує скілів
    /// назовні, справжній <c>Steward.Staff</c> лишається internal-інструментом
    /// самого ядра, не бота); економіка — обережна вилазка Quiet.
    /// </summary>
    public sealed class StewardPolicy : IBotPolicy
    {
        public string Name => "Steward";

        public IncidentPath ChooseIncidentPath(PendingOfferView offer)
        {
            if (offer?.Options != null)
            {
                foreach (var o in offer.Options)
                    if (o.Path == IncidentPathView.Quiet && o.HasCandidate) return IncidentPath.Quiet;
                foreach (var o in offer.Options)
                    if (o.Path == IncidentPathView.Bloody && o.HasCandidate) return IncidentPath.Bloody;
            }
            return IncidentPath.Quiet; // немає кандидата — краще Найгірша, ніж бій наосліп
        }

        public int ChooseQuestOption(QuestOfferView offer)
        {
            if (offer?.Options == null || offer.Options.Count == 0) return 0;
            if (offer.QuestId == BotSupport.DungeonEventQuestId)
                return BotSupport.ClampIndex(1, offer.Options.Count); // обережний вибір "менше, спалити слід"
            return 0;
        }

        /// <summary>Чергується день через день (парність доби) — гарантує в одному прогоні і "patrol=true", і "patrol=false" (§6.1 рядок 7: "лише коли patrol=true У ТОЙ САМИЙ прогін").</summary>
        public bool ChoosePatrol(SessionView view) => view != null && view.Day % 2 == 0;

        public IReadOnlyDictionary<string, string> ChooseAssignments(RosterView roster, CityView city)
            => BotSupport.DefaultAssignments(roster);

        public ExpeditionChoice? ChooseExpedition(SessionView view)
        {
            return new ExpeditionChoice("outskirts", ExpeditionApproach.Quiet,
                new[] { GameSession.ProtagonistId, "maksym", "myroslava" }, 4);
        }

        public CombatAction ChooseCombatAction(BattleView battle) => new CombatAction(CombatIntent.AttackNearest);

        public bool ChooseAutoResolve(BattleView battle) => true;

        public bool ChoosePushDeeper(DungeonView view) => false;
    }
}
