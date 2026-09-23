using Game.Core.Balance;
using Game.Core.Characters;
using Game.Core.Economy;
using Game.Core.Loop;
using Game.Core.Pressure;
using Game.Core.Stats;

namespace Game.Core.Base
{
    /// <summary>
    /// Рачительный хозяин: простая политика «хорошей игры» (Поправка №6.5).
    ///
    /// ЗАЧЕМ ОНА В ЯДРЕ. Приёмка Поправки №6 звучит так: при хорошей игре
    /// кампания не обязана кончаться Расколом. Чтобы это было утверждением
    /// теста, а не надеждой, «хорошая игра» должна быть записана кодом — один
    /// раз, и одинаково для тестов, харнеса и плёнки суток в сцене.
    ///
    /// Это НЕ искусственный интеллект игрока и не подсказка ему. Политика
    /// нарочно простая: строить по порядку, что по карману; звать облаву, когда
    /// на улицах ропот; принимать людей, когда еды с запасом.
    /// </summary>
    public sealed class Steward
    {
        /// <summary>Порядок стройки: сначала лечить, потом звать людей, потом успокаивать.</summary>
        public string[] BuildPriority =
        {
            DefaultBuildings.Infirmary,
            DefaultBuildings.Tavern,
            DefaultBuildings.Temple,
            DefaultBuildings.Market,
            DefaultBuildings.Fortifications,
            DefaultBuildings.Workshop
        };

        /// <summary>Сколько еды держать в запасе, прежде чем кормить новых людей.</summary>
        public int FoodReserve = 40;

        /// <summary>С какой полосы звать облаву. Раньше — тратить золото на тишину.</summary>
        public TensionBand RaidFrom = TensionBand.Murmur;

        /// <summary>Один ход хозяина между сутками. Возвращает, что было заказано.</summary>
        public string Act(CityWorks works, BaseState state, DayProcessor processor, BalanceConfig balance)
        {
            if (works == null || state == null || processor == null || balance == null) return null;

            string did = Staff(state);

            // Одна стройка за раз по порядку: первое, что по карману.
            foreach (var id in BuildPriority)
            {
                if (works.Has(id) || works.IsBuilding(id)) continue;
                if (works.Order(id, state) == BuildOrderResult.Started)
                {
                    did = (did == null ? "" : did + " ") + "build:" + id;
                    break;
                }
            }

            if (processor.Tension.Band >= RaidFrom &&
                works.OrderRaid(state, processor.CurrentDay + 1, balance) == CouncilOrderResult.Queued)
                did = (did == null ? "" : did + " ") + "raid";

            if (state.Resources.Get(ResourceType.Food) >= balance.City.SettlersFoodCost + FoodReserve &&
                works.OrderSettlers(state, processor.CurrentDay + 1, balance) == CouncilOrderResult.Queued)
                did = (did == null ? "" : did + " ") + "settlers";

            return did;
        }

        /// <summary>
        /// Ставит свободных людей на открытые пустые посты.
        ///
        /// ЗАЧЕМ. Здание открывает пост, но само его не занимает. Плёнка суток
        /// показала цену этого пробела: лазарет стоял с четвёртых суток, а на
        /// семнадцатую ночь «Больной ребёнок» кончился скверно — «на посту
        /// никого не было». Лекарь всё это время сидел без дела.
        ///
        /// Правило простое и детерминированное: посты по порядку, на каждый —
        /// свободный с наибольшим профильным навыком, при равенстве — первый по
        /// ростеру. Ставит только того, кто в деле хоть что-то смыслит (навык
        /// не ниже единицы): иначе лекарь ушёл бы в поле раньше, чем откроется
        /// лазарет. Занятых не переставляет: это решение игрока, а не хозяина.
        /// Возвращает «staff:&lt;кто&gt;@&lt;пост&gt;» через пробел или null.
        /// </summary>
        public static string Staff(BaseState state)
        {
            if (state == null) return null;

            string did = null;

            foreach (var slot in state.Slots)
            {
                if (!slot.Unlocked || slot.IsOccupied) continue;

                Companion best = null;
                foreach (var companion in state.Roster.All)
                {
                    if (companion.IsAssigned || companion.Status != CompanionStatus.Idle) continue;
                    if (SkillFor(companion, slot) < 1) continue;
                    if (best == null || SkillFor(companion, slot) > SkillFor(best, slot))
                        best = companion;
                }

                if (best != null && state.TryAssign(best.Id, slot.Id) == AssignmentResult.Success)
                    did = (did == null ? "" : did + " ") + "staff:" + best.Id + "@" + slot.Id;
            }

            return did;
        }

        private static int SkillFor(Companion companion, AssignmentSlot slot)
        {
            var skill = slot.Definition.PrimarySkill;
            return skill == SkillType.None ? 0 : companion.Skill(skill);
        }
    }
}
