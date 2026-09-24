using System.Collections.Generic;
using Game.Core.Expeditions;
using Game.Core.Loop;
using Game.Core.Session.Views;

namespace Game.Core.Session.Bots
{
    /// <summary>
    /// Завжди тихо, уникає бою (§4.10): де є вибір — Quiet, куди не глянь.
    /// Ніколи не жмуть «Автобій» без потреби — коли бій усе ж трапляється
    /// (провалений тихий обхід і подібне), грає його ПОКРОКОВО (BotRunner
    /// сам перекладає <see cref="CombatIntent.Overwatch"/> у CombatMove/
    /// CombatAttack/CombatEnterOverwatch) — саме ця політика і є "хоча б одна",
    /// що доводить ці три команди GameSession живими (не лише "Автобоєм").
    /// </summary>
    public sealed class PacifistPolicy : IBotPolicy
    {
        public string Name => "Pacifist";

        public IncidentPath ChooseIncidentPath(PendingOfferView offer) => IncidentPath.Quiet;

        public int ChooseQuestOption(QuestOfferView offer)
        {
            if (offer?.Options == null || offer.Options.Count == 0) return 0;
            if (offer.QuestId == BotSupport.DungeonEventQuestId)
                return BotSupport.ClampIndex(1, offer.Options.Count); // "менше, спалити слід" — без нового страху
            return 0;
        }

        public int ChooseSceneOption(SceneStepView step) => BotSupport.ChooseScenePersuasive(step);

        public bool ChoosePatrol(SessionView view) => false; // мінімальна активність уночі

        public IReadOnlyDictionary<string, string> ChooseAssignments(RosterView roster, CityView city)
            => BotSupport.DefaultAssignments(roster);

        public ExpeditionChoice? ChooseExpedition(SessionView view)
        {
            return new ExpeditionChoice("outskirts", ExpeditionApproach.Quiet,
                new[] { GameSession.ProtagonistId, "maksym", "myroslava" }, 4);
        }

        public CombatAction ChooseCombatAction(BattleView battle) => new CombatAction(CombatIntent.Overwatch);

        public bool ChooseAutoResolve(BattleView battle) => false;

        public bool ChoosePushDeeper(DungeonView view) => false;
    }
}
