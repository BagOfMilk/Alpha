using Game.Core.Economy;
using Game.Core.Loop;
using Game.Core.Pressure;
using Game.Core.Signals;

namespace Game.Core.Base
{
    /// <summary>
    /// Городские работы как шаг дня: приказы совета, стройка, действие
    /// построенного (Поправка №6).
    ///
    /// Стоит на DayStepOrder.Construction — первым после часов, до Напряжения:
    /// облава и храм обязаны войти в сегодняшнюю полосу, а не во вчерашнюю.
    ///
    /// Всё снижение Напряжения идёт через драйверы ЗАКРЫТОГО списка
    /// (инвариант 5): CouncilRaid, TempleAura, Fortifications. Новых драйверов
    /// Поправка №6 не добавляет — она наконец вызывает те, что годами стояли в
    /// перечислении без единого места вызова.
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
            // Стройка и приказы — дневная работа. Ночь принадлежит тем же суткам,
            // и второй проход удвоил бы и стройку, и облаву.
            if (_works == null || ctx.IsNight) return;

            var tension = ctx.Balance.Tension;

            // ---- приказы совета ----
            if (_works.TakeRaid(ctx.Day))
            {
                ctx.Tension.Apply(TensionDriver.CouncilRaid, tension.RaidDelta, "council:raid");
                ctx.CityEvents.Add(new CityEvent("council.raid", SignalUrgency.Alarming));
            }

            Arrive(ctx, _works.TakeSettlers(ctx.Day), "council");
            Arrive(ctx, _works.TakeArrivals(), "expedition");

            // ---- B5 ревью-фикс (major): Напруга Указа применяется ЗДЕСЬ, тем
            //      же приёмом, каким Облава выше кладёт CouncilRaid прямо в
            //      ctx.Tension, а не через DayProcessor.QueueExternal — та
            //      очередь не входит в SettlementSave, и «Указ → SaveState →
            //      загрузка» (оба легальны в Morning) тихо съедало бы уплаченный
            //      сдвиг (см. CityWorks._pendingCouncilEdictTension) ----
            int councilEdictTension = _works.TakeCouncilEdictTension();
            if (councilEdictTension != 0)
                ctx.Tension.Apply(TensionDriver.CouncilEdict, councilEdictTension, "council:decree");

            // ---- B5: Указ/Дипломатия/Підготовка/Спорядження применяются СРАЗУ
            //      (вне конвейера, см. CityWorks.OrderDecree и соседей), но
            //      объявляются здесь же, в первый дневной шаг после заказа —
            //      иначе они двигали бы город молча (ревью-фикс §2 стр. 15) ----
            var councilAnnouncements = _works.TakeCouncilAnnouncements();
            if (councilAnnouncements != null)
                foreach (var evt in councilAnnouncements)
                    ctx.CityEvents.Add(evt);

            // ---- B5: выплата Инвестиции — растянута по суткам, поэтому здесь,
            //      а не в момент заказа (см. CityWorks.OrderInvestment) ----
            int investmentPayout = _works.TakeInvestmentPayout();
            if (investmentPayout > 0)
            {
                _state.Resources.Add(ResourceType.Gold, investmentPayout);
                ctx.CityEvents.Add(new CityEvent("council.invest.payout", SignalUrgency.Notable,
                    "amount:" + investmentPayout));
            }

            // ---- стройка ----
            foreach (var id in _works.AdvanceConstruction())
            {
                _works.ApplyToSlots(_state);
                ctx.CityEvents.Add(new CityEvent("city.built." + id, SignalUrgency.Notable, "building:" + id));
            }

            // ---- действие построенного: каждые сутки, тихо, по своему драйверу ----
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
