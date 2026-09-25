using System.Collections.Generic;
using Game.Core.Stats;

namespace Game.Core.Characters.Scars
{
    /// <summary>
    /// Контент вічного треку шрамів (R16/G10): раніше <see cref="ScarTrack"/>
    /// існував, але поранення до шраму не доходило ніде — трек стояв порожнім
    /// усю кампанію. Тут же — і сам контент (id/ключі, без українського
    /// тексту: він у <c>UkrainianText</c>, Core лише ключі, R7), і єдина
    /// точка рішення «який шрам».
    /// </summary>
    public static class DefaultScars
    {
        public static ScarDefinition OneEyed() =>
            new ScarDefinition("one_eyed", "Одноглазый", WoundTier.Serious)
                .WithModifier(StatKeys.Of(DerivedStat.Accuracy), -1.0);

        public static ScarDefinition Limp() =>
            new ScarDefinition("limp", "Хромой", WoundTier.Serious)
                .WithModifier(StatKeys.Of(DerivedStat.MoveApPerTile), 1.0);

        public static ScarDefinition BrokenHand() =>
            new ScarDefinition("broken_hand", "Перебитая рука", WoundTier.Critical)
                .WithModifier(StatKeys.Of(SkillType.Melee), -1.0);

        public static ScarDefinition Haunted() =>
            new ScarDefinition("haunted", "Опалённый страхом", WoundTier.Critical)
                .WithModifier(StatKeys.Of(DerivedStat.StatusDurationReduction), -0.25);

        /// <summary>
        /// Порядок оголошення тут і є порядком вибору (див. <see cref="PickNext"/>):
        /// без нього «детермінований вибір» не було б чим гарантувати.
        /// </summary>
        public static IReadOnlyList<ScarDefinition> All() => new List<ScarDefinition>
        {
            OneEyed(), Limp(), BrokenHand(), Haunted()
        };

        /// <summary>
        /// Детермінований вибір шраму (R16): жодного кидка. Береться перший
        /// за каталогом шрам, якого в напарника ще немає і чий мінімальний тір
        /// не вищий за тір цієї рани — черга шрамів одна на всіх і не залежить від
        /// приводу (інцидент, вилазка, пізніше — бій).
        /// </summary>
        public static ScarDefinition PickNext(Companion companion, WoundTier tier)
        {
            if (companion == null || tier < WoundTier.Serious) return null;

            var all = All();
            for (int i = 0; i < all.Count; i++)
            {
                var scar = all[i];
                if (tier >= scar.MinTier && !companion.Scars.Has(scar.Id)) return scar;
            }
            return null;
        }

        /// <summary>
        /// Єдина точка присвоєння (R16/G10): і рана з посту (<c>RosterAdapter.Wound</c>),
        /// і рана з вилазки (<c>ExpeditionRunner.Complete</c>) ідуть через неї. Бій
        /// (коли з'явиться) зобов'язаний ранити тільки через <c>RosterAdapter.Wound</c> (R5)
        /// і таким чином теж потрапити сюди — другої копії цієї логіки не буде.
        /// </summary>
        public static bool TryGrant(Companion companion, WoundTier tier, out ScarDefinition granted)
        {
            granted = PickNext(companion, tier);
            return granted != null && companion.Scars.Add(granted);
        }
    }
}
