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

        /// <summary>
        /// Восстановление журнала из сейва: статусы по id накатываются на СВЕЖИЙ пул
        /// определений (контент живёт в коде/ассетах, сейв хранит только id). Квест
        /// из сейва, которого больше нет в пуле, тихо отбрасывается (контент-патч).
        /// «Доступные» дополнительно прогоняются через гейты (как CollectFrom):
        /// демотированный квест с уже стоящим BlockedByFlag не возвращается на доску
        /// (анти-ферма: спайн с вехой финала не предлагается заново). Completed
        /// восстанавливаются без проверки.
        /// </summary>
        public void Restore(IEnumerable<QuestDefinition> pool,
                            IEnumerable<string> availableIds,
                            IEnumerable<string> activeIds,
                            IEnumerable<string> completedIds,
                            FactionRegistry factions = null,
                            ICollection<string> flags = null,
                            IReadOnlyList<Companion> roster = null)
        {
            _status.Clear(); _byId.Clear();
            _available.Clear(); _active.Clear(); _completed.Clear();

            var poolById = new Dictionary<string, QuestDefinition>();
            if (pool != null)
                foreach (var d in pool)
                    if (d != null) poolById[d.Id] = d;

            void Place(IEnumerable<string> ids, List<QuestDefinition> bucket, QuestStatus status, bool gated)
            {
                if (ids == null) return;
                foreach (var id in ids)
                {
                    if (string.IsNullOrEmpty(id) || _status.ContainsKey(id)) continue;
                    if (!poolById.TryGetValue(id, out var def)) continue;
                    if (gated && !GateOpen(def, factions, flags, roster)) continue;
                    _byId[id] = def;
                    _status[id] = status;
                    bucket.Add(def);
                }
            }

            Place(availableIds, _available, QuestStatus.Available, gated: true);
            Place(activeIds, _active, QuestStatus.Active, gated: true);
            Place(completedIds, _completed, QuestStatus.Completed, gated: false);
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
