using System.Collections.Generic;

namespace Game.Gameplay.Ring.Models
{
    /// <summary>Кандидат на фокус у матчі: дія, «продати/реверс» чи варіант докрутки.</summary>
    public sealed class MatchFocusItem
    {
        /// <summary>Стабільний ключ (не індекс): екран перебудовується на кожен крок, фокус лишається на тому ж.</summary>
        public string Id;
        public string Label;
        public bool Available;
        /// <summary>Текст підказки — той самий, що при наведенні мишею.</summary>
        public string Hint;
        /// <summary>Гаряча цифра (0 — без).</summary>
        public int Number;
    }

    /// <summary>
    /// Фокус на діях матчу без миші (клавіатура, геймпад): рух із обгорткою, недоступні пропускаються, фокус зберігається за Id
    /// після оновлення рядків. Чиста модель: кнопки й підсвітку малює екран, цей клас лише каже, що в фокусі й що написати в підказці.
    /// </summary>
    public sealed class MatchFocusModel
    {
        private readonly List<MatchFocusItem> _items = new List<MatchFocusItem>();
        private int _index = -1;

        /// <summary>Підказка, коли ніщо не в фокусі.</summary>
        public string DefaultHint = "";

        public IReadOnlyList<MatchFocusItem> Items { get { return _items; } }

        /// <summary>Індекс сфокусованого (−1 — нічого).</summary>
        public int Index { get { return _index; } }

        public MatchFocusItem Focused { get { return _index >= 0 && _index < _items.Count ? _items[_index] : null; } }

        public string FocusedId { get { var f = Focused; return f == null ? null : f.Id; } }

        /// <summary>Підказка сфокусованого — слово в слово та, що при наведенні мишею; без фокуса — <see cref="DefaultHint"/>.</summary>
        public string FocusHintText { get { var f = Focused; return f == null ? DefaultHint : f.Hint; } }

        /// <summary>Нові кандидати: фокус лишається на тому ж Id; зник чи недоступний — перший доступний (або нічого).</summary>
        public void Set(IList<MatchFocusItem> items)
        {
            string keep = FocusedId;
            _items.Clear();
            if (items != null) _items.AddRange(items);
            _index = -1;
            if (keep != null)
                for (int i = 0; i < _items.Count; i++)
                    if (_items[i].Id == keep && _items[i].Available) { _index = i; break; }
            if (_index < 0) _index = Next(-1, +1);
        }

        /// <summary>Поставити фокус на Id (наприклад, під мишею); false — такого немає чи він недоступний.</summary>
        public bool FocusId(string id)
        {
            for (int i = 0; i < _items.Count; i++)
                if (_items[i].Id == id && _items[i].Available) { _index = i; return true; }
            return false;
        }

        public void Clear() { _index = -1; }

        /// <summary>
        /// Рух фокуса: Вгору/Вліво/Таб назад — попередній, Вниз/Вправо/Таб вперед — наступний; по колу, недоступні пропускаються.
        /// Решта дій (Підтвердити, Назад, Журнал) — не рух: false.
        /// </summary>
        public bool Move(RingAction a)
        {
            int dir;
            switch (a)
            {
                case RingAction.Up: case RingAction.Left: case RingAction.TabPrev: dir = -1; break;
                case RingAction.Down: case RingAction.Right: case RingAction.TabNext: dir = +1; break;
                default: return false;
            }
            int n = Next(_index, dir);
            if (n >= 0) _index = n;
            return true;
        }

        private int Next(int from, int dir)
        {
            int c = _items.Count;
            if (c == 0) return -1;
            // без фокуса: вперед — з першого, назад — з останнього
            int i = from < 0 ? (dir > 0 ? -1 : c) : from;
            for (int k = 0; k < c; k++)
            {
                i = ((i + dir) % c + c) % c;
                if (_items[i].Available) return i;
            }
            return -1;
        }
    }
}
