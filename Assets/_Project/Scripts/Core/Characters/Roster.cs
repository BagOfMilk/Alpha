using System.Collections.Generic;

namespace Game.Core.Characters
{
    /// <summary>
    /// Список всех напарников игрока с быстрым доступом по Id.
    /// </summary>
    public sealed class Roster
    {
        private readonly Dictionary<string, Companion> _byId = new Dictionary<string, Companion>();
        private readonly List<Companion> _ordered = new List<Companion>();

        public IReadOnlyList<Companion> All => _ordered;
        public int Count => _ordered.Count;

        public void Add(Companion companion)
        {
            if (companion == null || _byId.ContainsKey(companion.Id)) return;
            _byId.Add(companion.Id, companion);
            _ordered.Add(companion);
        }

        public bool Remove(string id)
        {
            if (id == null || !_byId.TryGetValue(id, out var c)) return false;
            _byId.Remove(id);
            _ordered.Remove(c);
            return true;
        }

        public Companion Get(string id)
        {
            return id != null && _byId.TryGetValue(id, out var c) ? c : null;
        }

        public bool Contains(string id) => id != null && _byId.ContainsKey(id);
    }
}
