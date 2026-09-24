using System;
using System.Collections.Generic;

namespace Game.Core.Combat
{
    /// <summary>
    /// Индивидуальная инициатива вперемешку: каждый юнит ходит по своей
    /// инициативе, порядок виден (<see cref="Order"/>), союзники и враги чередуются
    /// естественно. Тай-брейк стабильный — по порядку добавления. Сама очередь не
    /// знает правил жизни/смерти: пропуски решает CombatState.
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
            // Стабильная сортировка: инициатива по убыванию, при равенстве — порядок добавления.
            indexed.Sort((a, b) => a.unit.Profile.Initiative != b.unit.Profile.Initiative
                ? b.unit.Profile.Initiative.CompareTo(a.unit.Profile.Initiative)
                : a.seq.CompareTo(b.seq));
            foreach (var (unit, _) in indexed) _order.Add(unit);
        }

        /// <summary>Переходит к следующему юниту; на обороте очереди начинается новый раунд.</summary>
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
