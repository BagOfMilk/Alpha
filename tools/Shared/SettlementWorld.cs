using Game.Core.Balance;
using Game.Core.Session;

namespace Alpha.Shared
{
    /// <summary>
    /// Одно поселение на два инструмента: харнес меряет им темп, консольная
    /// сборка даёт в нём играть.
    ///
    /// ТОНКИЙ ДЕЛЕГАТ (Поправка №7, аудит G8/G9/G15): сама постройка мира
    /// переехала в ядро — <see cref="FirstHourWorld"/> — чтобы Unity,
    /// консольная сборка и харнес темпа собирали ОДИН и тот же мир, а не три
    /// похожих копии, которые неизбежно разъезжаются. Этот файл больше не
    /// знает, из кого состоит ростер и какие посты у общины — только зовёт
    /// FirstHourWorld.Build и подставляет свои константы там, где инструментам
    /// нужна голая строка (id поста, id партии), а не тип ядра.
    /// </summary>
    public static class SettlementWorld
    {
        /// <summary>Семь постов общины — та же раскладка, что у FirstHourWorld.</summary>
        public static readonly string[] Positions = FirstHourWorld.Positions;

        /// <summary>Кто по умолчанию уходит в вилазку доби 4: протагонист, Максим, Мирослава.</summary>
        public static readonly string[] PartyIds = FirstHourWorld.PartyIds;

        /// <summary>Мир последнего построения — партии и инструментам нужно снимать людей с постов.</summary>
        public static FirstHourWorld Last { get; private set; }

        public static BalanceConfig Balance() => new BalanceConfig();

        /// <summary>
        /// Собрать мир первого часа. <paramref name="requirePlayerDecision"/> —
        /// консольная сборка спрашивает игрока, харнес темпа меряет в
        /// автономном режиме (тихий путь по умолчанию).
        /// </summary>
        public static FirstHourWorld Build(int tier = 1, bool requirePlayerDecision = false)
        {
            var world = FirstHourWorld.Build(tier, requirePlayerDecision, Balance());
            Last = world;
            return world;
        }
    }
}
