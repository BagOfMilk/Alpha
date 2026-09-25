using System.Collections.Generic;
using Game.Core.Expeditions;
using Game.Core.Loop;
using Game.Core.Session.Views;

namespace Game.Core.Session.Bots
{
    /// <summary>Завжди криваво, атакує (§4.10): Bloody на кожному розгалуженні; «Автобій» за замовчуванням для темпу.</summary>
    public sealed class BloodyPolicy : IBotPolicy
    {
        public string Name => "Bloody";

        public IncidentPath ChooseIncidentPath(PendingOfferView offer) => IncidentPath.Bloody;

        public int ChooseQuestOption(QuestOfferView offer)
        {
            if (offer?.Options == null || offer.Options.Count == 0) return 0;
            if (offer.QuestId == BotSupport.DungeonEventQuestId)
                return BotSupport.ClampIndex(0, offer.Options.Count); // "забрати все зерно" — жадібний/кривавий норов
            return 0;
        }

        public int ChooseSceneOption(SceneStepView step) => BotSupport.ChooseSceneAggressive(step);

        public bool ChoosePatrol(SessionView view) => true; // завжди насторожі

        public IReadOnlyDictionary<string, string> ChooseAssignments(RosterView roster, CityView city)
            => BotSupport.DefaultAssignments(roster);

        public ExpeditionChoice? ChooseExpedition(SessionView view)
        {
            return new ExpeditionChoice("outskirts", ExpeditionApproach.Forceful,
                new[] { GameSession.ProtagonistId, "maksym", "myroslava" }, 2);
        }

        public CombatAction ChooseCombatAction(BattleView battle) => new CombatAction(CombatIntent.AttackNearest);

        public bool ChooseAutoResolve(BattleView battle) => true;

        public bool ChoosePushDeeper(DungeonView view) => false;
    }
}
