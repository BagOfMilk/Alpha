using System.Collections.Generic;
using System.Text;
using Game.Core.Loop;

namespace Game.Core.Signals
{
    /// <summary>
    /// Память композитора: когда каждый ключ реплики показывался в последний раз.
    ///
    /// Зачем: без неё композитор не имеет памяти между днями, и в спокойном
    /// городе он честно выдаёт одни и те же четыре ключа каждые сутки. Прогон
    /// кампании показал результат: самый частый ключ появлялся около двухсот раз
    /// при трёх десятках уникальных ключей на всю игру. Именно так «слой
    /// сигналов» превращается в обои, которые перестают читать.
    ///
    /// Механика простая и детерминированная: при равной срочности вперёд идёт
    /// то, чего игрок дольше не слышал. Ничего не запрещается совсем — редкое
    /// просто перестаёт вытесняться частым.
    /// </summary>
    public sealed class SignalMemory : IStateBlob
    {
        private readonly Dictionary<string, int> _lastShown = new Dictionary<string, int>();

        /// <summary>Сколько суток назад ключ звучал. Никогда не звучал — «бесконечно давно».</summary>
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
