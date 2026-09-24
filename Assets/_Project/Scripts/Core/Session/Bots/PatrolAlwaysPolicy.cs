using System.Collections.Generic;
using Game.Core.Expeditions;
using Game.Core.Loop;
using Game.Core.Session.Views;

namespace Game.Core.Session.Bots
{
    /// <summary>Завжди патрулює (§4.10): вартовий, що ніколи не спить — незалежна перевірка "night.forewarn лише коли patrol=true" з протилежного боку (patrol завжди true).</summary>
    public sealed class PatrolAlwaysPolicy : IBotPolicy
    {
        public string Name => "PatrolAlways";

        public IncidentPath ChooseIncidentPath(PendingOfferView offer) => IncidentPath.Quiet;

        public int ChooseQuestOption(QuestOfferView offer)
        {
            if (offer?.Options == null || offer.Options.Count == 0) return 0;
            if (offer.QuestId == BotSupport.DungeonEventQuestId)
                return BotSupport.ClampIndex(1, offer.Options.Count);
            return 0;
        }

        public bool ChoosePatrol(SessionView view) => true;

        public IReadOnlyDictionary<string, string> ChooseAssignments(RosterView roster, CityView city)
            => BotSupport.DefaultAssignments(roster);

        public ExpeditionChoice? ChooseExpedition(SessionView view)
        {
            // Дальній тракт (поріг 7 — вищий за "outskirts"): дає полосам
            // виходу вилазки шанс лягти інакше, ніж у решти політик (§6.1
            // рядок 5 — різноманіття полос наслідку за весь прогін).
            return new ExpeditionChoice("far_highway", ExpeditionApproach.Quiet,
                new[] { GameSession.ProtagonistId, "maksym", "myroslava" }, 8);
        }

        public CombatAction ChooseCombatAction(BattleView battle) => new CombatAction(CombatIntent.AttackNearest);

        public bool ChooseAutoResolve(BattleView battle) => true;
    }
}
