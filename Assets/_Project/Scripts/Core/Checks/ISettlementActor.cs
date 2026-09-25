using System.Collections.Generic;

namespace Game.Core.Checks
{
    /// <summary>
    /// Порт до персонажа як до «джерела числа для перевірки».
    /// Реалізується адаптером над теперішнім Companion і над майбутньою моделлю —
    /// міський шар різниці не помітить.
    /// </summary>
    public interface ISettlementActor
    {
        string Id { get; }

        /// <summary>У поселенні і здатний брати участь: не у вилазці і не мертвий.</summary>
        bool IsPresentInSettlement { get; }

        /// <summary>Протагоніст — валідний кандидат на резолв подій (US-8.2).</summary>
        bool IsProtagonist { get; }

        /// <summary>Id позиції, яку тримає; null — вільний.</summary>
        string HeldPositionId { get; }

        /// <summary>
        /// Значення навички разом із контекстним атрибутом підходу (G16/GDD:98):
        /// Переконання і Торгівля добирають Кмітливість, Залякування — Волю; для
        /// <see cref="ApproachForm.Neutral"/> (утилітарні перевірки) атрибут не
        /// додається зовсім. Параметр необов'язковий навмисно — викликачі,
        /// яким підхід не важливий (вилазка б'ється утилітарними скілами),
        /// продовжують працювати без змін.
        /// </summary>
        int GetCheckValue(SkillKey skill, ApproachForm approach = ApproachForm.Neutral);

        /// <summary>
        /// Флет-модифікатор поверх голої навички (US-2.6): трейти, шрами, перки.
        /// Відокремлений від GetCheckValue не заради краси, а щоб перевірка могла
        /// показати гравцю «навичка 5, характер +2» — і щоб адаптер фізично
        /// не міг порахувати те саме двічі.
        /// </summary>
        int GetTraitModifier(SkillKey skill);
    }

    /// <summary>Порт до ростеру: хто сьогодні в місті і може відповідати за події.</summary>
    public interface IRosterView
    {
        IReadOnlyList<ISettlementActor> PresentActors { get; }
        ISettlementActor Protagonist { get; }
    }
}
