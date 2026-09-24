using System.Collections.Generic;
using Game.Core.Dungeons;
using Game.Core.Expeditions;
using Game.Core.Loop;
using Game.Core.Session.Views;

namespace Game.Core.Session.Bots
{
    /// <summary>
    /// Завжди штовхає данж глибше, поки не Wipe (§4.10): щоранку, тільки-но
    /// відряд вільний, іде в "Покинутий табір авангарду" (Delve) — BotRunner
    /// читає <see cref="System.Type"/> цієї політики, щоб після кожної кімнати
    /// PushDeeper() замість ExtractDungeon(), доки лишаються кімнати.
    /// </summary>
    public sealed class DelveGreedyPolicy : IBotPolicy
    {
        public string Name => "DelveGreedy";

        public IncidentPath ChooseIncidentPath(PendingOfferView offer) => IncidentPath.Quiet; // обходить бій, щоб іти глибше швидше

        public int ChooseQuestOption(QuestOfferView offer)
        {
            if (offer?.Options == null || offer.Options.Count == 0) return 0;
            if (offer.QuestId == BotSupport.DungeonEventQuestId)
                return BotSupport.ClampIndex(0, offer.Options.Count); // "забрати все зерно" — жадібність за іменем політики
            return 0;
        }

        public bool ChoosePatrol(SessionView view) => false;

        public IReadOnlyDictionary<string, string> ChooseAssignments(RosterView roster, CityView city)
            => BotSupport.DefaultAssignments(roster);

        public ExpeditionChoice? ChooseExpedition(SessionView view)
        {
            return new ExpeditionChoice(DefaultDungeon.AbandonedCamp, ExpeditionApproach.Delve,
                new[] { GameSession.ProtagonistId, "maksym", "myroslava" }, 2);
        }

        public CombatAction ChooseCombatAction(BattleView battle) => new CombatAction(CombatIntent.AttackNearest);

        public bool ChooseAutoResolve(BattleView battle) => true;

        /// <summary>Жадібність за іменем політики — завжди штовхає глибше, доки в данжі лишаються кімнати.</summary>
        public bool ChoosePushDeeper(DungeonView view) => true;
    }
}
