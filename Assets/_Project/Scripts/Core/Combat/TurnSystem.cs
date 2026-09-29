using System;
using System.Collections.Generic;

namespace Game.Core.Combat
{
    /// <summary>
    /// Індивідуальна ініціатива впереміш: кожен юніт ходить за своєю
    /// ініціативою, порядок видно (<see cref="Order"/>), союзники і вороги чергуються
    /// природно. Тай-брейк стабільний — за порядком додавання. Сама черга не
    /// знає правил життя/смерті: пропуски вирішує CombatState.
    ///
    /// Старт бою (Поправка №14.1): якщо задано сторону першого раунду, у
    /// раунді 1 усі її юніти ходять раніше за інших (кожна сторона — за
    /// власною ініціативою); з раунду 2 — звичайна черга впереміш. Зайвих
    /// ходів не буває: у раунді 1 кожен ходить рівно раз.
    /// </summary>
    public sealed class TurnSystem
    {
        /// <summary>Звичайна черга впереміш — з раунду 2 (або з раунду 1, якщо переваги немає).</summary>
        private readonly List<CombatUnit> _baseOrder = new List<CombatUnit>();

        /// <summary>Черга поточного раунду: у раунді 1 може бути переставлена на користь однієї сторони.</summary>
        private readonly List<CombatUnit> _order = new List<CombatUnit>();
        private int _index;

        public int Round { get; private set; } = 1;

        /// <summary>Черга поточного раунду.</summary>
        public IReadOnlyList<CombatUnit> Order => _order;

        /// <summary>Черга наступних раундів — звичайна, впереміш (колесо черги показує її після межі раунду).</summary>
        public IReadOnlyList<CombatUnit> NextRoundOrder => _baseOrder;

        public CombatUnit Current => _order.Count > 0 ? _order[_index] : null;

        public TurnSystem(IEnumerable<CombatUnit> units) : this(units, null) { }

        /// <param name="firstRoundSide">Сторона, що в раунді 1 ходить цілком першою; null — звичайна черга.</param>
        public TurnSystem(IEnumerable<CombatUnit> units, Side? firstRoundSide)
        {
            if (units == null) throw new ArgumentNullException(nameof(units));

            var indexed = new List<(CombatUnit unit, int seq)>();
            int seq = 0;
            foreach (var u in units) indexed.Add((u, seq++));
            // Стабільне сортування: ініціатива за спаданням, при рівності — порядок додавання.
            indexed.Sort((a, b) => a.unit.Profile.Initiative != b.unit.Profile.Initiative
                ? b.unit.Profile.Initiative.CompareTo(a.unit.Profile.Initiative)
                : a.seq.CompareTo(b.seq));
            foreach (var (unit, _) in indexed) _baseOrder.Add(unit);

            if (firstRoundSide.HasValue)
            {
                foreach (var u in _baseOrder) if (u.Side == firstRoundSide.Value) _order.Add(u);
                foreach (var u in _baseOrder) if (u.Side != firstRoundSide.Value) _order.Add(u);
            }
            else
            {
                _order.AddRange(_baseOrder);
            }
        }

        /// <summary>
        /// Юніт, що з'явився посеред бою (підкріплення, Поправка №14.4): стає в
        /// звичайну чергу за ініціативою (при рівній — після наявних), а в
        /// поточному раунді ходить не раніше, ніж після того, хто ходить зараз.
        /// </summary>
        public void AddLate(CombatUnit unit)
        {
            if (unit == null || _baseOrder.Contains(unit)) return;
            _baseOrder.Insert(SortedIndex(_baseOrder, unit, 0), unit);
            _order.Insert(SortedIndex(_order, unit, _index + 1), unit);
        }

        private static int SortedIndex(List<CombatUnit> list, CombatUnit unit, int minIndex)
        {
            int i = Math.Min(Math.Max(0, minIndex), list.Count);
            while (i < list.Count && list[i].Profile.Initiative >= unit.Profile.Initiative) i++;
            return i;
        }

        /// <summary>Переходить до наступного юніта; на обороті черги починається новий раунд.</summary>
        public CombatUnit Advance()
        {
            if (_order.Count == 0) return null;
            _index++;
            if (_index >= _order.Count)
            {
                _index = 0;
                Round++;
                // Перевага першого раунду діє лише раз — далі звичайна черга.
                _order.Clear();
                _order.AddRange(_baseOrder);
            }
            return Current;
        }
    }
}
