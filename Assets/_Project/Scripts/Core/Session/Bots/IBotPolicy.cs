using System.Collections.Generic;
using Game.Core.Expeditions;
using Game.Core.Loop;
using Game.Core.Session.Views;

namespace Game.Core.Session.Bots
{
    /// <summary>
    /// Стратегія бота на один хід тактичного бою (§4.10 TEST_BUILD.md). Сам
    /// <see cref="BattleView"/> не каже, хто зараз "поточний" юніт (§4.2.1 такого
    /// поля не несе) — <c>BotRunner</c> вгадує його за єдиним надійним сигналом
    /// (єдина ЗАЙНЯТА клітинка серед <see cref="BattleView.ReachableTiles"/> — це
    /// клітинка того, хто ходить: чужі юніти стоять на СВОЇХ тайлах і в прохідні
    /// не входять), а Intent лише задає, ЩО він хоче зробити зі своїм ходом.
    /// Якщо погана видимість не дає визначити поточного юніта (0 AP лишилось) —
    /// BotRunner просто завершує хід, як і зробив би обережний гравець.
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
    }
}
