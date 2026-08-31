using System.Collections.Generic;

namespace Game.Core.Checks
{
    /// <summary>
    /// Порт к персонажу как к «источнику числа для проверки».
    /// Реализуется адаптером над нынешним Companion и над будущей моделью —
    /// городской слой разницы не заметит.
    /// </summary>
    public interface ISettlementActor
    {
        string Id { get; }

        /// <summary>В поселении и способен участвовать: не в экспедиции и не мёртв.</summary>
        bool IsPresentInSettlement { get; }

        /// <summary>Протагонист — валидный кандидат на резолв событий (US-8.2).</summary>
        bool IsProtagonist { get; }

        /// <summary>Id позиции, которую держит; null — свободен.</summary>
        string HeldPositionId { get; }

        /// <summary>Значение навыка вместе с контекстным атрибутом подхода.</summary>
        int GetCheckValue(SkillKey skill);

        /// <summary>Флэт-модификатор от трейтов (US-2.6). Пока всегда 0 — трейтов нет.</summary>
        int GetTraitModifier(SkillKey skill);
    }

    /// <summary>Порт к ростеру: кто сегодня в городе и может отвечать за события.</summary>
    public interface IRosterView
    {
        IReadOnlyList<ISettlementActor> PresentActors { get; }
        ISettlementActor Protagonist { get; }
    }
}
