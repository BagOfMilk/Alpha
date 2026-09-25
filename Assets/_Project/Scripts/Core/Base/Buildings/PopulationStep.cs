using Game.Core.Balance;
using Game.Core.Loop;
using Game.Core.Signals;

namespace Game.Core.Base
{
    /// <summary>
    /// Люди приходять і йдуть, і від них росте село (Поправка №6.3–6.4).
    ///
    /// ЩО ЧУТНО, А ЩО НІ. Повільний природний приріст і таверна —
    /// тихі: сигнал на кожну нову людину перетворився б на шпалери. Але
    /// коли людність переходить в іншу смугу, це чутно (інваріант 4:
    /// німого переходу смуги не буває). Відхід людей чутний завжди — це
    /// наслідок, який гравець зобов'язаний помітити. Зміна тіра чутна завжди.
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

            // ---- прихід: тихий ----
            int arrived = 0;
            if (cfg.NaturalGrowthEveryDays > 0 && ctx.Day % cfg.NaturalGrowthEveryDays == 0) arrived++;
            if (_works != null && _works.Has(DefaultBuildings.Tavern)) arrived += cfg.TavernArrivalsPerDay;
            if (arrived > 0) ctx.Population.Add(arrived);

            // ---- відхід: чутний завжди ----
            if (ctx.IsHungry) Leave(ctx, cfg.HungryDepartures, "hunger");
            if (ctx.Fear != null && ctx.Fear.IsAfraid(ctx.Day)) Leave(ctx, cfg.FearDepartures, "fear");

            int bandAfter = ctx.Population.CrowdBand;
            if (bandAfter != bandBefore)
                ctx.CityEvents.Add(new CityEvent("city.crowd." + bandAfter, SignalUrgency.Notable,
                    bandAfter > bandBefore ? "dir:up" : "dir:down"));

            // ---- тір: тільки вгору, чутний завжди ----
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
        /// Тір росте від людей І ключової будівлі (Поправка №6.4, GDD US-7.6).
        /// Самих людей мало — натовп без таверни не село; самої будівлі мало —
        /// таверна в порожньому хуторі не робить його селом. Поріг по одному ступеню
        /// за добу: перестрибнути тір не можна, як і ступінь передвісника.
        /// </summary>
        internal static int NextTier(int tier, int population, CityWorks works, CityBalance cfg)
        {
            if (works == null || cfg == null || cfg.TierPopulation == null) return tier;

            int index = tier - 1; // поріг наступного тіра
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
