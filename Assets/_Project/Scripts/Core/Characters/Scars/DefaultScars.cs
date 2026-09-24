using System.Collections.Generic;
using Game.Core.Stats;

namespace Game.Core.Characters.Scars
{
    /// <summary>
    /// Контент вечного трека шрамов (R16/G10): раньше <see cref="ScarTrack"/>
    /// существовал, но ранение до шрама не доходило нигде — трек стоял пустым
    /// всю кампанию. Здесь же — и сам контент (id/ключи, без украинского
    /// текста: он в <c>UkrainianText</c>, Core только ключи, R7), и единственная
    /// точка решения «какой шрам».
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
        /// Порядок объявления здесь и есть порядок выбора (см. <see cref="PickNext"/>):
        /// без него «детерминированный выбор» нечем было бы гарантировать.
        /// </summary>
        public static IReadOnlyList<ScarDefinition> All() => new List<ScarDefinition>
        {
            OneEyed(), Limp(), BrokenHand(), Haunted()
        };

        /// <summary>
        /// Детерминированный выбор шрама (R16): никакого броска. Берётся первый
        /// по каталогу шрам, которого у напарника ещё нет и чей минимальный тир
        /// не выше тира этой раны — очередь шрамов одна на всех и не зависит от
        /// повода (инцидент, вылазка, позже — бой).
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
        /// Единая точка присвоения (R16/G10): и рана с поста (<c>RosterAdapter.Wound</c>),
        /// и рана с вылазки (<c>ExpeditionRunner.Complete</c>) идут через неё. Бой
        /// (когда появится) обязан ранить только через <c>RosterAdapter.Wound</c> (R5)
        /// и таким образом тоже попасть сюда — второй копии этой логики не будет.
        /// </summary>
        public static bool TryGrant(Companion companion, WoundTier tier, out ScarDefinition granted)
        {
            granted = PickNext(companion, tier);
            return granted != null && companion.Scars.Add(granted);
        }
    }
}
