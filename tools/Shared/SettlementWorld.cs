using System.Collections.Generic;
using Game.Core;
using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Checks;
using Game.Core.Economy;
using Game.Core.Loop;
using Game.Core.Settlement;
using Game.Core.Stats;
using Game.Core.Pressure;
using Game.Core.World;

namespace Alpha.Shared
{
    /// <summary>
    /// Одно поселение на два инструмента: харнес меряет им темп, консольная
    /// сборка даёт в нём играть.
    ///
    /// Общий файл, а не две копии, ровно по причине из FIRST_HOUR §4 п. 6:
    /// если срез и замер строят мир по-разному, замеренные числа относятся не к
    /// той игре, в которую играли.
    /// </summary>
    public static class SettlementWorld
    {
        public static readonly string[] Positions =
        {
            "storehouse_dock", "settlement_market", "settlement_farms",
            "infirmary_bed", "council_seat", "scouting_post", "workshop_bench"
        };

        /// <summary>Кто по умолчанию уходит в вылазку: склад, разведка, совет.</summary>
        public static readonly string[] PartyIds = { "guard", "scout", "elder" };

        public static BalanceConfig Balance() => new BalanceConfig();

        /// <summary>
        /// Шесть напарников: специалист на каждый домен плюс два середняка.
        /// Числа скромные — это хутор, а не элита.
        ///
        /// Навык даётся под ДОМЕН ПОСТА, а не под выработку слота: доклад со
        /// склада идёт по Выживанию, с рынка — по Торговле, из лазарета — по
        /// Медицине, совет разбирает дела словом.
        /// </summary>
        public static Roster BuildRoster()
        {
            var cfg = Balance();
            var roster = new Roster();
            var baseState = new BaseState(roster, new ResourceLedger(), cfg);
            LastBase = baseState;
            foreach (var slot in DefaultContent.AllSlots()) baseState.AddSlot(slot);

            Put(baseState, "guard", SkillType.Survival, 8, Positions[0]);
            Put(baseState, "trader", SkillType.Trade, 7, Positions[1]);
            Put(baseState, "farmer", SkillType.Survival, 6, Positions[2]);
            Put(baseState, "medic", SkillType.Medicine, 7, Positions[3]);
            Put(baseState, "elder", SkillType.Persuade, 6, Positions[4]);
            Put(baseState, "scout", SkillType.Survival, 6, Positions[5]);
            return roster;
        }

        /// <summary>
        /// Ставит бойца на пост ТЕМ ЖЕ путём, что и игра: BaseState.TryAssign.
        /// Внутренний сеттер AssignedSlotId сюда не годится — его ядро открывает
        /// только тестам и харнесу, а консольная сборка это игра (инвариант 3).
        /// </summary>
        private static void Put(BaseState baseState, string id, SkillType skill, int value, string position)
        {
            var arch = new CompanionArchetype(id, id);
            arch.SetSkill(skill, value);
            baseState.Roster.Add(arch.CreateInstance(id));

            // Стартовые посты среза считаются уже открытыми: срез начинается с
            // работающей общины, а не со стройки. Док склада по умолчанию закрыт
            // (его открывают за ресурсы), и без этой строки назначение молча
            // проваливалось — склад стоял без человека, а мир об этом не говорил.
            var slot = baseState.GetSlot(position);
            if (slot != null) slot.Unlocked = true;

            var result = baseState.TryAssign(id, position);
            if (result != AssignmentResult.Success)
                throw new System.InvalidOperationException(
                    $"Стартовая расстановка сорвалась: {id} -> {position} ({result}). " +
                    "Тихая неудача здесь означает пост без человека на весь прогон.");
        }

        /// <summary>База последнего построенного мира: партии нужно снимать людей с постов.</summary>
        public static BaseState LastBase { get; private set; }

        public static DayProcessor BuildProcessor(int tier, Roster roster = null)
        {
            var cfg = Balance();
            roster = roster ?? BuildRoster();

            var adapter = new RosterAdapter(roster);

            var pulse = new WorldPulse(cfg.Pulse);
            foreach (var source in DefaultPressureSources.All()) pulse.AddSource(source);

            return new DayProcessor(new TensionState(cfg.Tension), cfg, DayProcessor.DefaultSteps())
            {
                Tier = tier,
                Roster = adapter,
                Casualties = adapter,
                Population = new PopulationState(),
                Pulse = pulse,
                Incidents = DefaultIncidents.BuildTable(),
                Repeats = new RepeatTracker(),
                PostDomains = new[]
                {
                    new PostDomain(Positions[0], "склад", SkillKeys.Survival, 5),
                    new PostDomain(Positions[1], "рынок", SkillKeys.Trade, 5),
                    new PostDomain(Positions[3], "лазарет", SkillKeys.Medicine, 5)
                }
            };
        }

    }
}
