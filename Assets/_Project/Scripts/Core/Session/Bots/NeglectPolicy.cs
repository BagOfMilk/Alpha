using System.Collections.Generic;
using Game.Core.Expeditions;
using Game.Core.Loop;
using Game.Core.Session.Views;

namespace Game.Core.Session.Bots
{
    /// <summary>
    /// «Шкідник» (§3.в замірів темпу Напруги, Поправка №7): найгірша чесна гра —
    /// кроваво на кожному розгалуженні, порожні пости (жодного призначення),
    /// без патруля, найагресивніший варіант сцен. Разом із
    /// <c>BotRunner(suppressCouncilRoutine: true)</c> (немає Облави, немає
    /// Храму/Укріплень) великий кризис має вдарити ЯВНО РАНІШЕ еталонного
    /// "безрукого" прогону (ціль — доба ≤20 замість ~25).
    ///
    /// На відміну від <see cref="BloodyPolicy"/> — та все ще призначає пости
    /// за замовчуванням і завжди патрулює вночі (обережність, не занедбання);
    /// ця політика навмисно НЕ робить жодного з двох.
    /// </summary>
    public sealed class NeglectPolicy : IBotPolicy
    {
        public string Name => "Neglect";

        public IncidentPath ChooseIncidentPath(PendingOfferView offer) => IncidentPath.Bloody;

        public int ChooseQuestOption(QuestOfferView offer)
        {
            if (offer?.Options == null || offer.Options.Count == 0) return 0;
            if (offer.QuestId == BotSupport.DungeonEventQuestId)
                return BotSupport.ClampIndex(0, offer.Options.Count); // "забрати все зерно" — жадібно
            return 0;
        }

        public int ChooseSceneOption(SceneStepView step) => BotSupport.ChooseSceneAggressive(step);

        /// <summary>Ніколи не патрулює — ніч без нагляду (Поправка №3.9: сигнали ночі проспані).</summary>
        public bool ChoosePatrol(SessionView view) => false;

        /// <summary>Пости лишаються порожніми — жодного призначення (на відміну від решти політик).</summary>
        public IReadOnlyDictionary<string, string> ChooseAssignments(RosterView roster, CityView city) => null;

        public ExpeditionChoice? ChooseExpedition(SessionView view)
        {
            return new ExpeditionChoice("outskirts", ExpeditionApproach.Forceful,
                new[] { GameSession.ProtagonistId, "maksym", "myroslava" }, 2);
        }

        public CombatAction ChooseCombatAction(BattleView battle) => new CombatAction(CombatIntent.AttackNearest);

        public bool ChooseAutoResolve(BattleView battle) => true;

        public bool ChoosePushDeeper(DungeonView view) => true; // жадібно, без обережності
    }
}
