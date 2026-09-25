using System;
using System.Collections.Generic;

namespace Game.Core.Combat
{
    /// <summary>
    /// Індивідуальна ініціатива впереміш: кожен юніт ходить за своєю
    /// ініціативою, порядок видно (<see cref="Order"/>), союзники і вороги чергуються
    /// природно. Тай-брейк стабільний — за порядком додавання. Сама черга не
    /// знає правил життя/смерті: пропуски вирішує CombatState.
    /// </summary>
    public sealed class TurnSystem
    {
        private readonly List<CombatUnit> _order = new List<CombatUnit>();
        private int _index;

        public int Round { get; private set; } = 1;
        public IReadOnlyList<CombatUnit> Order => _order;
        public CombatUnit Current => _order.Count > 0 ? _order[_index] : null;

        public TurnSystem(IEnumerable<CombatUnit> units)
        {
            if (units == null) throw new ArgumentNullException(nameof(units));

            var indexed = new List<(CombatUnit unit, int seq)>();
            int seq = 0;
            foreach (var u in units) indexed.Add((u, seq++));
            // Стабільне сортування: ініціатива за спаданням, при рівності — порядок додавання.
            indexed.Sort((a, b) => a.unit.Profile.Initiative != b.unit.Profile.Initiative
                ? b.unit.Profile.Initiative.CompareTo(a.unit.Profile.Initiative)
                : a.seq.CompareTo(b.seq));
            foreach (var (unit, _) in indexed) _order.Add(unit);
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
            }
            return Current;
        }
    }
}
