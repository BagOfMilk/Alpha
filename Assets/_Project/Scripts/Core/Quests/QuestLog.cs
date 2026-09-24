using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Game.Core.Quests
{
    /// <summary>
    /// Журнал квестів: тримає активні прогони за їх визначеннями (пул контенту
    /// реєструється окремо — сейв зберігає лише id, самі визначення живуть у
    /// коді/ассетах, як і скрізь у проекті).
    ///
    /// <see cref="Loop.IStateBlob"/>: слепок — рядок «id&gt;етап&gt;стан», записи
    /// через кому, відсортовані за id (детермінований слепок, як у
    /// RosterAdapter/StoryFlags).
    /// </summary>
    public sealed class QuestLog : Loop.IStateBlob
    {
        private readonly Dictionary<string, QuestDefinition> _defsById = new Dictionary<string, QuestDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, QuestRun> _runs = new Dictionary<string, QuestRun>(StringComparer.Ordinal);

        public QuestLog(IEnumerable<QuestDefinition> pool = null)
        {
            RegisterPool(pool);
        }

        /// <summary>Додати визначення квестів у пул (можна викликати кілька разів).</summary>
        public void RegisterPool(IEnumerable<QuestDefinition> pool)
        {
            if (pool == null) return;
            foreach (var def in pool)
                if (def != null && !string.IsNullOrEmpty(def.Id))
                    _defsById[def.Id] = def;
        }

        public QuestDefinition DefinitionOf(string questId)
        {
            QuestDefinition def;
            return questId != null && _defsById.TryGetValue(questId, out def) ? def : null;
        }

        /// <summary>Почати прогін квесту. null, якщо визначення нема в пулі або квест уже йде.</summary>
        public QuestRun Start(string questId)
        {
            if (string.IsNullOrEmpty(questId) || _runs.ContainsKey(questId)) return null;

            QuestDefinition def;
            if (!_defsById.TryGetValue(questId, out def)) return null;

            var run = new QuestRun(def);
            _runs[questId] = run;
            return run;
        }

        public QuestRun Get(string questId)
        {
            QuestRun run;
            return !string.IsNullOrEmpty(questId) && _runs.TryGetValue(questId, out run) ? run : null;
        }

        public bool IsKnown(string questId) => !string.IsNullOrEmpty(questId) && _runs.ContainsKey(questId);

        /// <summary>Усі прогони, що зараз активні (детермінований порядок — за id).</summary>
        public IReadOnlyList<QuestRun> Active
        {
            get
            {
                var ids = new List<string>(_runs.Keys);
                ids.Sort(StringComparer.Ordinal);
                var result = new List<QuestRun>();
                for (int i = 0; i < ids.Count; i++)
                    if (_runs[ids[i]].IsActive) result.Add(_runs[ids[i]]);
                return result;
            }
        }

        // ---- слепок (IStateBlob) ----

        public string CaptureState()
        {
            var ids = new List<string>(_runs.Keys);
            ids.Sort(StringComparer.Ordinal);

            var sb = new StringBuilder();
            for (int i = 0; i < ids.Count; i++)
            {
                var run = _runs[ids[i]];
                if (sb.Length > 0) sb.Append(',');
                sb.Append(ids[i]).Append('>')
                  .Append(run.CurrentIndex.ToString(CultureInfo.InvariantCulture)).Append('>')
                  .Append(((int)run.State).ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Відновлення потребує пулу визначень: квест із сейву, якого більше
        /// нема в пулі (контент-патч), тихо відкидається — той самий приём, що
        /// й у колишньому QuestLog.Restore.
        /// </summary>
        public void RestoreState(string blob)
        {
            _runs.Clear();
            if (string.IsNullOrEmpty(blob)) return;

            foreach (var entry in blob.Split(','))
            {
                if (string.IsNullOrEmpty(entry)) continue;
                var f = entry.Split('>');
                if (f.Length < 3) continue;

                QuestDefinition def;
                if (!_defsById.TryGetValue(f[0], out def)) continue;

                int index, state;
                if (!int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out index)) continue;
                if (!int.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out state)) continue;

                var run = new QuestRun(def);
                run.RestoreTo(index, (QuestState)state);
                _runs[f[0]] = run;
            }
        }
    }
}
