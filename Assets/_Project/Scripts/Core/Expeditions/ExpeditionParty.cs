using System.Collections.Generic;
using System.Globalization;
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

        /// <summary>
        /// Результат, замороженный в момент отправки (R15/B7): резолв
        /// вызывается ровно один раз — при <see cref="Base.ExpeditionRunner.Depart"/>,
        /// а не при возвращении. До возврата партии он просто лежит здесь и
        /// переживает сохранение/загрузку (<see cref="CaptureState"/>). Для
        /// подхода Delve остаётся null — данж резолвится своим потоком (D1+B2).
        /// </summary>
        public ExpeditionResult PendingResult { get; private set; }

        /// <summary>Кладёт результат в блоб партии. Вызывается один раз, сразу после Depart.</summary>
        public void FreezeResult(ExpeditionResult result) => PendingResult = result;

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
            ExpeditionResult discarded;
            return Return(baseState, out discarded);
        }

        /// <summary>
        /// Тот же возврат, но заодно отдаёт и очищает замороженный результат
        /// (R15): вызывающий (сегодня — тест, дальше — D1) передаёт его в
        /// <see cref="Base.ExpeditionRunner.Complete"/> ровно один раз.
        /// </summary>
        public IReadOnlyList<string> Return(BaseState baseState, out ExpeditionResult result)
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

            result = PendingResult;
            PendingResult = null;
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

            // Замороженный результат (R15) — с ПРЕФИКСОМ ДЛИНЫ, а не за
            // отдельным разделителем ';'. Составной сейв (Core/Loop/
            // SettlementSave.cs, ведёт исключительно Foundation/A1) делит ВЕСЬ
            // слепок по ';' одним проходом ДО того, как отдать значение поля
            // party= сюда — поэтому любой ';' внутри собственного блоба
            // партии обрезал бы хвост молча, и замороженный результат
            // терялся бы при восстановлении через настоящий путь сохранения
            // (найдено ревью пакета B7: сейв посреди вылазки через
            // DayProcessor.SaveState теряет результат, хотя изолированный
            // CaptureState()/RestoreState() этого не показывает). Префикс
            // длины делает разбор нечувствительным к содержимому результата:
            // что бы в нём ни было, ниже читается ровно len символов, а не
            // ищется разделитель.
            if (PendingResult != null)
            {
                string resultBlob = PendingResult.ToBlob();
                sb.Append('^').Append(resultBlob.Length.ToString(CultureInfo.InvariantCulture))
                  .Append('^').Append(resultBlob);
            }

            return sb.ToString();
        }

        public void RestoreState(string blob)
        {
            _away.Clear();
            _vacated.Clear();
            DaysRemaining = 0;
            PendingResult = null;
            if (string.IsNullOrEmpty(blob)) return;

            string head = blob;
            int caret = blob.IndexOf('^');
            if (caret >= 0)
            {
                head = blob.Substring(0, caret);
                int secondCaret = blob.IndexOf('^', caret + 1);
                if (secondCaret > caret)
                {
                    int len;
                    if (int.TryParse(blob.Substring(caret + 1, secondCaret - caret - 1),
                            NumberStyles.Integer, CultureInfo.InvariantCulture, out len) &&
                        len >= 0 && secondCaret + 1 + len <= blob.Length)
                    {
                        PendingResult = ExpeditionResult.FromBlob(blob.Substring(secondCaret + 1, len));
                    }
                }
            }

            var parts = head.Split(',');
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
