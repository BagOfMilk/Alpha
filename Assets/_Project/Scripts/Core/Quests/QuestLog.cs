using System.Collections.Generic;
using Game.Core.Characters;
using Game.Core.Factions;

namespace Game.Core.Quests
{
    public enum QuestStatus { Available = 0, Active = 1, Completed = 2 }

    /// <summary>
    /// Журнал квестов: собирает доступные из разных источников (US-14.3), держит
    /// активные/завершённые. Гейтинг входа — флаги/полоса фракции/скил (лучший в
    /// ростере). Инциденты «Напряжения» тоже порождают квесты (источник
    /// TensionIncident) — петля Эпика 11 ↔ контент.
    /// </summary>
    public sealed class QuestLog
    {
        private readonly Dictionary<string, QuestStatus> _status = new Dictionary<string, QuestStatus>();
        private readonly Dictionary<string, QuestDefinition> _byId = new Dictionary<string, QuestDefinition>();
        private readonly List<QuestDefinition> _available = new List<QuestDefinition>();
        private readonly List<QuestDefinition> _active = new List<QuestDefinition>();
        private readonly List<QuestDefinition> _completed = new List<QuestDefinition>();

        public IReadOnlyList<QuestDefinition> Available => _available;
        public IReadOnlyList<QuestDefinition> Active => _active;
        public IReadOnlyList<QuestDefinition> Completed => _completed;

        public QuestStatus? StatusOf(string id) => id != null && _status.TryGetValue(id, out var s) ? s : (QuestStatus?)null;

        /// <summary>Открывает в «доступные» те квесты пула, чей гейт пройден и которые ещё не видели.</summary>
        public void CollectFrom(IEnumerable<QuestDefinition> pool, FactionRegistry factions = null,
                                ICollection<string> flags = null, IReadOnlyList<Companion> roster = null)
        {
            if (pool == null) return;
            foreach (var def in pool)
            {
                if (def == null || _status.ContainsKey(def.Id)) continue;
                if (!GateOpen(def, factions, flags, roster)) continue;
                MakeAvailable(def);
            }
        }

        /// <summary>Квест от инцидента «Напряжения» (US-14.3): входит сразу доступным.</summary>
        public QuestDefinition SpawnFromIncident(QuestDefinition def)
        {
            if (def == null || _status.ContainsKey(def.Id)) return def;
            MakeAvailable(def);
            return def;
        }

        public bool Start(string id)
        {
            if (StatusOf(id) != QuestStatus.Available) return false;
            var def = _byId[id];
            _available.Remove(def);
            _active.Add(def);
            _status[id] = QuestStatus.Active;
            return true;
        }

        public bool Complete(string id)
        {
            if (StatusOf(id) != QuestStatus.Active) return false;
            var def = _byId[id];
            _active.Remove(def);
            _completed.Add(def);
            _status[id] = QuestStatus.Completed;
            return true;
        }

        public bool GateOpen(QuestDefinition def, FactionRegistry factions,
                             ICollection<string> flags, IReadOnlyList<Companion> roster)
        {
            if (def == null) return false;
            if (!string.IsNullOrEmpty(def.RequiresFlag) && (flags == null || !flags.Contains(def.RequiresFlag))) return false;
            // Пройденный контент не предлагается заново (анти-ферма наград после загрузки).
            if (!string.IsNullOrEmpty(def.BlockedByFlag) && flags != null && flags.Contains(def.BlockedByFlag)) return false;
            if (!string.IsNullOrEmpty(def.RequiresFaction))
            {
                if (factions == null || !factions.AtLeast(def.RequiresFaction, def.RequiresBand)) return false;
            }
            if (def.RequiresSkill != Stats.SkillType.None)
            {
                int best = 0;
                if (roster != null)
                    for (int i = 0; i < roster.Count; i++)
                    {
                        var c = roster[i];
                        if (c != null && c.IsAlive) { int v = c.GetSkill(def.RequiresSkill); if (v > best) best = v; }
                    }
                if (best < def.RequiresSkillLevel) return false;
            }
            return true;
        }

        private void MakeAvailable(QuestDefinition def)
        {
            _byId[def.Id] = def;
            _status[def.Id] = QuestStatus.Available;
            _available.Add(def);
        }
    }
}
