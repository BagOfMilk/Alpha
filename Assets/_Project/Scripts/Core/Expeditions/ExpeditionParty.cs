using System.Collections.Generic;
using System.Text;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Loop;

namespace Game.Core.Expeditions
{
    /// <summary>
    /// Выход партии и возвращение (Поправка №5.6 п. 4).
    ///
    /// Главное здесь — ЦЕНА: уходя, партия освобождает посты, и город остаётся
    /// без рук. Пока выход не звал Unassign, вылазка была бесплатной: люди
    /// уходили и одновременно работали дома (чеклист §3 стр. 15).
    ///
    /// Возвращение НЕ назначает никого обратно. Это не забывчивость: расстановка
    /// — решение игрока, и после вылазки он принимает его заново, уже зная, что
    /// случилось в городе без этих троих.
    /// </summary>
    public sealed class ExpeditionParty : IStateBlob
    {
        private readonly List<string> _away = new List<string>();

        /// <summary>Кто где стоял до выхода — чтобы показать игроку, что опустело.</summary>
        private readonly Dictionary<string, string> _vacated = new Dictionary<string, string>();

        /// <summary>Сколько суток партии ещё идти. 0 — все дома.</summary>
        public int DaysRemaining { get; private set; }

        public IReadOnlyList<string> Away => _away;
        public bool IsAway => _away.Count > 0;

        /// <summary>Посты, опустевшие из-за вылазки: город о них узнаёт пометкой, не попапом.</summary>
        public IReadOnlyCollection<string> VacatedPositions => _vacated.Values;

        /// <summary>
        /// Партия уходит на N суток. Каждый уходящий снимается с поста, и пост
        /// остаётся пустым — именно это и есть цена вылазки.
        /// </summary>
        public bool Depart(BaseState baseState, IEnumerable<string> companionIds, int days)
        {
            if (baseState == null || companionIds == null || IsAway) return false;

            foreach (var id in companionIds)
            {
                var companion = baseState.Roster.Get(id);
                if (companion == null || companion.IsDead) continue;

                if (!string.IsNullOrEmpty(companion.AssignedSlotId))
                {
                    _vacated[id] = companion.AssignedSlotId;
                    baseState.Unassign(companion.AssignedSlotId);
                }
                companion.Status = CompanionStatus.OnMission;
                _away.Add(id);
            }

            if (_away.Count == 0) return false;
            DaysRemaining = days < 1 ? 1 : days;
            return true;
        }

        /// <summary>Сутки в пути. Возвращает true, когда партия дошла до дома.</summary>
        public bool TickDay()
        {
            if (!IsAway) return false;
            if (DaysRemaining > 0) DaysRemaining--;
            return DaysRemaining == 0;
        }

        /// <summary>
        /// Партия дома. Посты НЕ восстанавливаются: расстановка — решение игрока,
        /// и он принимает его заново.
        /// </summary>
        public IReadOnlyList<string> Return(BaseState baseState)
        {
            var returned = new List<string>(_away);
            if (baseState != null)
                foreach (var id in returned)
                {
                    var companion = baseState.Roster.Get(id);
                    if (companion == null || companion.IsDead) continue;
                    if (companion.Status == CompanionStatus.OnMission)
                        companion.Status = CompanionStatus.Idle;
                }

            _away.Clear();
            _vacated.Clear();
            DaysRemaining = 0;
            return returned;
        }

        // ---- слепок ----

        public string CaptureState()
        {
            if (!IsAway) return string.Empty;

            var sb = new StringBuilder();
            sb.Append(DaysRemaining);
            for (int i = 0; i < _away.Count; i++)
            {
                sb.Append(',').Append(_away[i]);
                sb.Append('>').Append(_vacated.TryGetValue(_away[i], out var slot) ? slot : "");
            }
            return sb.ToString();
        }

        public void RestoreState(string blob)
        {
            _away.Clear();
            _vacated.Clear();
            DaysRemaining = 0;
            if (string.IsNullOrEmpty(blob)) return;

            var parts = blob.Split(',');
            int days;
            if (!int.TryParse(parts[0], out days)) return;
            DaysRemaining = days;

            for (int i = 1; i < parts.Length; i++)
            {
                var pair = parts[i].Split('>');
                if (pair.Length == 0 || string.IsNullOrEmpty(pair[0])) continue;
                _away.Add(pair[0]);
                if (pair.Length > 1 && !string.IsNullOrEmpty(pair[1])) _vacated[pair[0]] = pair[1];
            }
        }
    }
}
