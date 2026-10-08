using System;
using System.Collections.Generic;
using Game.Core.Session.Views;

namespace Game.Gameplay.UI
{
    /// <summary>
    /// Прев'ю витрати ОД (подача бою П9, docs/research/RT_COMBAT_PRESENTATION.md): скільки очок дій лишиться після
    /// наведеної дії і які здібності, доступні зараз, після неї стануть недоступні. Чистий C#, лише читає вид бою;
    /// охоронець — <c>BattlePresentationTests</c>.
    /// </summary>
    public static class ApForecast
    {
        /// <summary>ОД після дії ціною <paramref name="cost"/> (не нижче нуля).</summary>
        public static int Left(int currentAp, int cost) => Math.Max(0, currentAp - Math.Max(0, cost));

        /// <summary>
        /// Id здібностей, які можна застосувати зараз (без відкату і з досить ОД), але не вистачить після витрати
        /// до <paramref name="apLeft"/>. Порядок — як на панелі. <paramref name="exceptAbilityId"/> — сама дія,
        /// що витрачає ОД (її не перелічуємо).
        /// </summary>
        public static List<string> NewlyUnaffordable(IReadOnlyList<BattleAbilityView> abilities, int currentAp, int apLeft,
                                                     string exceptAbilityId = null)
        {
            var result = new List<string>();
            if (abilities == null) return result;
            foreach (var ability in abilities)
            {
                if (ability == null || ability.CooldownRemaining > 0) continue;
                if (string.Equals(ability.Id, exceptAbilityId, StringComparison.Ordinal)) continue;
                if (currentAp >= ability.ApCost && apLeft < ability.ApCost) result.Add(ability.Id);
            }
            return result;
        }
    }
}
