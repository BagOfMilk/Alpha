using Game.Core.Balance;
using Game.Core.Loop;
using Game.Core.Signals;

namespace Game.Core.Base
{
    /// <summary>
    /// Люди приходят и уходят, и от них растёт село (Поправка №6.3–6.4).
    ///
    /// ЧТО СЛЫШНО, А ЧТО НЕТ. Медленный естественный прирост и таверна —
    /// тихие: сигнал на каждого нового человека превратился бы в обои. Но
    /// когда людность переходит в другую полосу, это слышно (инвариант 4:
    /// немого перехода полосы не бывает). Уход людей слышен всегда — это
    /// следствие, которое игрок обязан заметить. Смена тира слышна всегда.
    /// </summary>
    public sealed class PopulationStep : IDayStep
    {
        private readonly CityWorks _works;

        public PopulationStep(CityWorks works)
        {
            _works = works;
        }

        public int Order => DayStepOrder.Population;

        public void Execute(DayContext ctx)
        {
            if (ctx.IsNight || ctx.Population == null) return;

            var cfg = ctx.Balance.City;
            int bandBefore = ctx.Population.CrowdBand;

            // ---- приход: тихий ----
            int arrived = 0;
            if (cfg.NaturalGrowthEveryDays > 0 && ctx.Day % cfg.NaturalGrowthEveryDays == 0) arrived++;
            if (_works != null && _works.Has(DefaultBuildings.Tavern)) arrived += cfg.TavernArrivalsPerDay;
            if (arrived > 0) ctx.Population.Add(arrived);

            // ---- уход: слышен всегда ----
            if (ctx.IsHungry) Leave(ctx, cfg.HungryDepartures, "hunger");
            if (ctx.Fear != null && ctx.Fear.IsAfraid(ctx.Day)) Leave(ctx, cfg.FearDepartures, "fear");

            int bandAfter = ctx.Population.CrowdBand;
            if (bandAfter != bandBefore)
                ctx.CityEvents.Add(new CityEvent("city.crowd." + bandAfter, SignalUrgency.Notable,
                    bandAfter > bandBefore ? "dir:up" : "dir:down"));

            // ---- тир: только вверх, слышен всегда ----
            int next = NextTier(ctx.Tier, ctx.Population.Count, _works, cfg);
            if (next > ctx.Tier)
            {
                ctx.RaiseTierTo = next;
                ctx.CityEvents.Add(new CityEvent("city.tier." + next, SignalUrgency.Imminent, "tier:" + next));
            }
        }

        private static void Leave(DayContext ctx, int people, string reason)
        {
            if (people <= 0) return;

            int gone = ctx.Population.Remove(people);
            if (gone > 0)
                ctx.CityEvents.Add(new CityEvent("city.people.left", SignalUrgency.Alarming,
                    "reason:" + reason, "count:" + gone));
        }

        /// <summary>
        /// Тир растёт от людей И ключевого здания (Поправка №6.4, GDD US-7.6).
        /// Одних людей мало — толпа без таверны не село; одного здания мало —
        /// таверна в пустом хуторе не делает его селом. Порог по одной ступени
        /// за сутки: перепрыгнуть тир нельзя, как и ступень предвестника.
        /// </summary>
        internal static int NextTier(int tier, int population, CityWorks works, CityBalance cfg)
        {
            if (works == null || cfg == null || cfg.TierPopulation == null) return tier;

            int index = tier - 1; // порог следующего тира
            if (index < 0 || index >= cfg.TierPopulation.Length) return tier;
            if (population < cfg.TierPopulation[index]) return tier;

            bool keyBuilding;
            switch (tier + 1)
            {
                case 2: keyBuilding = works.Has(DefaultBuildings.Tavern); break;
                case 3: keyBuilding = works.Has(DefaultBuildings.Temple) || works.Has(DefaultBuildings.Market); break;
                case 4: keyBuilding = works.Has(DefaultBuildings.Fortifications); break;
                default: keyBuilding = false; break;
            }

            return keyBuilding ? tier + 1 : tier;
        }
    }
}
