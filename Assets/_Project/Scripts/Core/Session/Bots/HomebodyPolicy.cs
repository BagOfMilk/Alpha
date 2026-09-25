using System.Collections.Generic;
using Game.Core.Loop;
using Game.Core.Session.Views;

namespace Game.Core.Session.Bots
{
    /// <summary>
    /// Домосід: ті самі мирні рішення, що й <see cref="PacifistPolicy"/>, але
    /// жодної вилазки — громада сита, пости не пустіють. Модель живого
    /// тестера (і водія Unity-туру <c>-autoplay-long</c>, який ходить у
    /// вилазку лише раз), а не бота, що безперервно шле трьох людей на
    /// чотири доби й голодує з восьмої доби.
    ///
    /// Навіщо окремо: 25.09.2026 темп Напруги тестової збірки підібрали під
    /// <see cref="PacifistPolicy"/> — і він тримався на голоді (+8 за добу).
    /// Водій Unity-туру не голодував і побачив першу смугу лише близько
    /// тридцятої доби, великого бунту — жодного. Еталон темпу 15/20/25
    /// (Поправка №7.9) — ситий гравець, який нічого не робить з Напругою;
    /// голод, кров і порожні пости його прискорюють, Облава/Храм/Укріплення
    /// — відсувають.
    /// </summary>
    public sealed class HomebodyPolicy : IBotPolicy
    {
        private readonly PacifistPolicy _manners = new PacifistPolicy();

        public string Name => "Homebody";

        public IncidentPath ChooseIncidentPath(PendingOfferView offer) => _manners.ChooseIncidentPath(offer);

        public int ChooseQuestOption(QuestOfferView offer) => _manners.ChooseQuestOption(offer);

        public int ChooseSceneOption(SceneStepView step) => _manners.ChooseSceneOption(step);

        public bool ChoosePatrol(SessionView view) => _manners.ChoosePatrol(view);

        public IReadOnlyDictionary<string, string> ChooseAssignments(RosterView roster, CityView city)
            => _manners.ChooseAssignments(roster, city);

        /// <summary>Нікуди не ходить: пости зайняті, ферми працюють.</summary>
        public ExpeditionChoice? ChooseExpedition(SessionView view) => null;

        public CombatAction ChooseCombatAction(BattleView battle) => _manners.ChooseCombatAction(battle);

        public bool ChooseAutoResolve(BattleView battle) => _manners.ChooseAutoResolve(battle);

        public bool ChoosePushDeeper(DungeonView view) => _manners.ChoosePushDeeper(view);
    }
}
