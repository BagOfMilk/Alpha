using System.Collections.Generic;
using System.Text;
using Game.Core.Loop;

namespace Game.Core.Signals
{
    /// <summary>
    /// Пам'ять композитора: коли кожен ключ репліки показувався востаннє.
    ///
    /// Навіщо: без неї композитор не має пам'яті між днями, і в спокійному
    /// місті він чесно видає ті самі чотири ключі щодоби. Прогін кампанії
    /// показав результат: найчастіший ключ з'являвся близько двохсот разів
    /// при трьох десятках унікальних ключів на всю гру. Саме так «шар
    /// сигналів» перетворюється на шпалери, які перестають читати.
    ///
    /// Механіка проста і детермінована: за рівної терміновості вперед іде
    /// те, чого гравець довше не чув. Нічого не забороняється зовсім — рідкісне
    /// просто перестає витіснятися частим.
    /// </summary>
    public sealed class SignalMemory : IStateBlob
    {
        private readonly Dictionary<string, int> _lastShown = new Dictionary<string, int>();

        /// <summary>Скільки діб тому ключ звучав. Ніколи не звучав — «нескінченно давно».</summary>
        internal int Staleness(string topicId, int day)
        {
            int last;
            if (string.IsNullOrEmpty(topicId) || !_lastShown.TryGetValue(topicId, out last))
                return int.MaxValue;
            return day - last;
        }

        internal void Remember(IReadOnlyList<SignalRequest> shown, int day)
        {
            if (shown == null) return;
            for (int i = 0; i < shown.Count; i++)
                if (!string.IsNullOrEmpty(shown[i].TopicId))
                    _lastShown[shown[i].TopicId] = day;
        }

        public string CaptureState()
        {
            var sb = new StringBuilder();
            foreach (var pair in _lastShown)
            {
                if (sb.Length > 0) sb.Append('~');
                sb.Append(pair.Key).Append('#').Append(pair.Value);
            }
            return sb.ToString();
        }

        public void RestoreState(string blob)
        {
            _lastShown.Clear();
            if (string.IsNullOrEmpty(blob)) return;

            foreach (var entry in blob.Split('~'))
            {
                int hash = entry.IndexOf('#');
                if (hash <= 0) continue;

                int day;
                if (int.TryParse(entry.Substring(hash + 1), out day))
                    _lastShown[entry.Substring(0, hash)] = day;
            }
        }
    }
}
