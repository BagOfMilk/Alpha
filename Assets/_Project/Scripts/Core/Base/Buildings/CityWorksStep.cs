using Game.Core.Economy;
using Game.Core.Loop;
using Game.Core.Pressure;
using Game.Core.Signals;

namespace Game.Core.Base
{
    /// <summary>
    /// Міські роботи як крок дня: укази ради, будівництво, дія
    /// побудованого (Поправка №6).
    ///
    /// Стоїть на DayStepOrder.Construction — першим після годинника, до Напруги:
    /// облава і храм зобов'язані увійти в сьогоднішню смугу, а не у вчорашню.
    ///
    /// Усе зниження Напруги йде через драйвери ЗАКРИТОГО списку
    /// (інваріант 5): CouncilRaid, TempleAura, Fortifications. Нових драйверів
    /// Поправка №6 не додає — вона нарешті викликає ті, що роками стояли в
    /// переліку без жодного місця виклику.
    /// </summary>
    public sealed class CityWorksStep : IDayStep
    {
        private readonly CityWorks _works;
        private readonly BaseState _state;

        public CityWorksStep(CityWorks works, BaseState state)
        {
            _works = works;
            _state = state;
        }

        public int Order => DayStepOrder.Construction;

        public void Execute(DayContext ctx)
        {
            // Будівництво і накази — денна робота. Ніч належить тим самим добам,
            // і другий прохід подвоїв би і будівництво, і облаву.
            if (_works == null || ctx.IsNight) return;

            var tension = ctx.Balance.Tension;

            // ---- накази ради ----
            if (_works.TakeRaid(ctx.Day))
            {
                ctx.Tension.Apply(TensionDriver.CouncilRaid, tension.RaidDelta, "council:raid");
                ctx.CityEvents.Add(new CityEvent("council.raid", SignalUrgency.Alarming));
            }

            Arrive(ctx, _works.TakeSettlers(ctx.Day), "council");
            Arrive(ctx, _works.TakeArrivals(), "expedition");

            // ---- B5 ревью-фікс (major): Напруга Указу застосовується ТУТ, тим
            //      самим прийомом, яким Облава вище кладе CouncilRaid прямо в
            //      ctx.Tension, а не через DayProcessor.QueueExternal — та
            //      черга не входить у SettlementSave, і «Указ → SaveState →
            //      завантаження» (обидва легальні в Morning) тихо з'їдало б сплачений
            //      зсув (див. CityWorks._pendingCouncilEdictTension) ----
            int councilEdictTension = _works.TakeCouncilEdictTension();
            if (councilEdictTension != 0)
                ctx.Tension.Apply(TensionDriver.CouncilEdict, councilEdictTension, "council:decree");

            // ---- B5: Указ/Дипломатія/Підготовка/Спорядження застосовуються ОДРАЗУ
            //      (поза конвеєром, див. CityWorks.OrderDecree і сусідів), але
            //      оголошуються тут же, у перший денний крок після замовлення —
            //      інакше вони рухали б місто мовчки (ревью-фікс §2 рядок 15) ----
            var councilAnnouncements = _works.TakeCouncilAnnouncements();
            if (councilAnnouncements != null)
                foreach (var evt in councilAnnouncements)
                    ctx.CityEvents.Add(evt);

            // ---- B5: виплата Інвестиції — розтягнута по добах, тому тут,
            //      а не в момент замовлення (див. CityWorks.OrderInvestment) ----
            int investmentPayout = _works.TakeInvestmentPayout();
            if (investmentPayout > 0)
            {
                _state.Resources.Add(ResourceType.Gold, investmentPayout);
                ctx.CityEvents.Add(new CityEvent("council.invest.payout", SignalUrgency.Notable,
                    "amount:" + investmentPayout));
            }

            // ---- будівництво ----
            foreach (var id in _works.AdvanceConstruction())
            {
                _works.ApplyToSlots(_state);
                ctx.CityEvents.Add(new CityEvent("city.built." + id, SignalUrgency.Notable, "building:" + id));
            }

            // ---- дія побудованого: щодоби, тихо, за своїм драйвером ----
            if (_works.Has(DefaultBuildings.Temple))
                ctx.Tension.ApplyFractional(TensionDriver.TempleAura, tension.TempleDrainPerDay, "temple");
            if (_works.Has(DefaultBuildings.Fortifications))
                ctx.Tension.ApplyFractional(TensionDriver.Fortifications, tension.FortificationDrainPerDay, "fortifications");
        }

        private static void Arrive(DayContext ctx, int people, string reason)
        {
            if (people <= 0 || ctx.Population == null) return;

            ctx.Population.Add(people);
            ctx.CityEvents.Add(new CityEvent("city.people.arrived", SignalUrgency.Notable,
                "reason:" + reason, "count:" + people));
        }
    }
}
