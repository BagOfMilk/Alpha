using System.Collections.Generic;
using Game.Core.Expeditions;
using Game.Core.Loop;
using Game.Core.Session.Views;

namespace Game.Core.Session.Bots
{
    /// <summary>
    /// Стратегія бота на один хід тактичного бою (§4.10 TEST_BUILD.md).
    /// <see cref="BattleView.CurrentUnitId"/> каже, хто зараз "поточний" юніт
    /// (фікс-ревью D2 — раніше поля не було, <c>BotRunner</c> вгадував його за
    /// ReachableTiles-евристикою, яка ніколи не спрацьовувала: Pathfinder не
    /// включає власний тайл юніта в Reachable), а Intent лише задає, ЩО він
    /// хоче зробити зі своїм ходом. Якщо активного юніта немає (бій завершився)
    /// — BotRunner просто завершує хід, як і зробив би обережний гравець.
    /// </summary>
    public enum CombatIntent
    {
        /// <summary>Атакувати найближчого ворога; не дістати — підійти ближче.</summary>
        AttackNearest,

        /// <summary>Стати в дозор напроти найближчого ворога, якщо не можна вдарити зараз.</summary>
        Overwatch,

        /// <summary>Нічого не робити цим ходом (пасивна політика поза бойовими вузлами).</summary>
        EndTurn
    }

    /// <summary>Рішення політики на один хід бою (§4.10) — BotRunner перекладає його у конкретні Combat*-команди GameSession.</summary>
    public sealed class CombatAction
    {
        public CombatIntent Intent;

        public CombatAction(CombatIntent intent = CombatIntent.AttackNearest)
        {
            Intent = intent;
        }
    }

    /// <summary>Що і куди відправити у вилазку (§4.10) — Delve (данж) так само легальний вибір, як Quiet/Forceful.</summary>
    public struct ExpeditionChoice
    {
        public readonly string SiteId;
        public readonly ExpeditionApproach Approach;
        public readonly IReadOnlyList<string> CompanionIds;
        public readonly int Days;

        public ExpeditionChoice(string siteId, ExpeditionApproach approach, IReadOnlyList<string> companionIds, int days)
        {
            SiteId = siteId;
            Approach = approach;
            CompanionIds = companionIds;
            Days = days;
        }
    }

    /// <summary>
    /// Політика бота (§4.10 TEST_BUILD.md, §1.1: "Боти — у ядрі", бо їх однаково
    /// споживають тести Unity, автопрогін і <c>tools/*</c>). Веде
    /// <see cref="GameSession"/> ВИКЛЮЧНО крізь публічні команди — сама політика
    /// бачить лише View-шар (§4.2), ніколи внутрішній стан сесії; <c>BotRunner</c>
    /// (не політика) звертається до GameSession напряму. Чистий C#, жодної
    /// випадковості (інваріант 1) — усі рішення детерміновані даними View.
    /// </summary>
    public interface IBotPolicy
    {
        /// <summary>Коротке ім'я для звітів/трас (напр. "Steward", "Pacifist").</summary>
        string Name { get; }

        /// <summary>Тихий чи кровавий шлях на будь-якій точці рішення (інцидент/данж-кімната/криза/фінал — BotRunner передає синтетичний офер для тих, що не мають природного PendingOfferView).</summary>
        IncidentPath ChooseIncidentPath(PendingOfferView offer);

        /// <summary>Індекс варіанту для квесту-вибору АБО події данжу (Type=="Event") — 0, якщо Options порожній.</summary>
        int ChooseQuestOption(QuestOfferView offer);

        /// <summary>
        /// Індекс варіанту сценового вибору (Поправка №7.8, Choice-крок
        /// портретної сцени — <see cref="SceneStepView.Options"/>) — 0, якщо
        /// Options порожній. Той самий "характер", що й
        /// <see cref="ChooseIncidentPath"/>: BloodyPolicy тягнеться до
        /// варіанту з перевіркою Залякування (Form=="Intimidate") або, як
        /// нема такого, до найризикованішого (останнього) варіанту;
        /// PacifistPolicy — до варіанту з перевіркою Переконання
        /// (Form=="Persuade"); решта політик — до першого виборного (як
        /// StewardPolicy.ChooseIncidentPath за замовчуванням бере тихий шлях).
        /// </summary>
        int ChooseSceneOption(SceneStepView step);

        /// <summary>Патрулювати цю ніч чи спати.</summary>
        bool ChoosePatrol(SessionView view);

        /// <summary>Кого на які пости поставити цього ранку: companionId -> slotId.</summary>
        IReadOnlyDictionary<string, string> ChooseAssignments(RosterView roster, CityView city);

        /// <summary>Відправити відряд цього ранку (null — нікуди не йдемо).</summary>
        ExpeditionChoice? ChooseExpedition(SessionView view);

        /// <summary>Що робити поточним ходом бою, коли бій розігрується покроково (не автобоєм).</summary>
        CombatAction ChooseCombatAction(BattleView battle);

        /// <summary>true — натиснути «Автобій» одразу, як бій розпочався; false — грати покроково.</summary>
        bool ChooseAutoResolve(BattleView battle);

        /// <summary>
        /// Фікс-ревью пакета D2 (minor): чи штовхати данж глибше (PushDeeper)
        /// після щойно розв'язаної кімнати, замість витягу (ExtractDungeon).
        /// Раніше <c>BotRunner.DoDungeonRoutine</c> вирішував це через
        /// <c>policy is DelveGreedyPolicy</c> — типова перевірка конкретного
        /// класу, яку жодна СТОРОННЯ реалізація <see cref="IBotPolicy"/> не
        /// могла б задіяти. Тепер це явний член контракту: true в
        /// <c>DelveGreedyPolicy</c>, false в решті.
        /// </summary>
        bool ChoosePushDeeper(DungeonView view);
    }
}
