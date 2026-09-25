using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Loop;

namespace Game.Core.Expeditions
{
    /// <summary>
    /// Вихід партії і повернення (Поправка №5.6 п. 4).
    ///
    /// Головне тут — ЦІНА: йдучи, партія звільняє пости, і місто лишається
    /// без рук. Поки вихід не кликав Unassign, вилазка була безкоштовною: люди
    /// йшли й одночасно працювали вдома (чеклист §3 стор. 15).
    ///
    /// Повернення НЕ призначає нікого назад. Це не забудькуватість: розстановка
    /// — рішення гравця, і після вилазки він приймає його заново, вже знаючи, що
    /// сталося в місті без цих трьох.
    /// </summary>
    public sealed class ExpeditionParty : IStateBlob
    {
        private readonly List<string> _away = new List<string>();

        /// <summary>Хто де стояв до виходу — щоб показати гравцю, що спорожніло.</summary>
        private readonly Dictionary<string, string> _vacated = new Dictionary<string, string>();

        /// <summary>Скільки діб партії ще йти. 0 — усі вдома.</summary>
        public int DaysRemaining { get; private set; }

        public IReadOnlyList<string> Away => _away;
        public bool IsAway => _away.Count > 0;

        /// <summary>
        /// Результат, заморожений у момент відправки (R15/B7): резолв
        /// викликається рівно один раз — при <see cref="Base.ExpeditionRunner.Depart"/>,
        /// а не при поверненні. До повернення партії він просто лежить тут і
        /// переживає збереження/завантаження (<see cref="CaptureState"/>). Для
        /// підходу Delve лишається null — данж резолвиться своїм потоком (D1+B2).
        /// </summary>
        public ExpeditionResult PendingResult { get; private set; }

        /// <summary>Кладе результат у блоб партії. Викликається один раз, одразу після Depart.</summary>
        public void FreezeResult(ExpeditionResult result) => PendingResult = result;

        /// <summary>Пости, що спорожніли через вилазку: місто про них дізнається позначкою, не попапом.</summary>
        public IReadOnlyCollection<string> VacatedPositions => _vacated.Values;

        /// <summary>
        /// Партія йде на N діб. Кожен, хто йде, знімається з поста, і пост
        /// лишається порожнім — саме це і є ціна вилазки.
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

        /// <summary>Доба в дорозі. Повертає true, коли партія дійшла додому.</summary>
        public bool TickDay()
        {
            if (!IsAway) return false;
            if (DaysRemaining > 0) DaysRemaining--;
            return DaysRemaining == 0;
        }

        /// <summary>
        /// Партія вдома. Пости НЕ відновлюються: розстановка — рішення гравця,
        /// і він приймає його заново.
        /// </summary>
        public IReadOnlyList<string> Return(BaseState baseState)
        {
            ExpeditionResult discarded;
            return Return(baseState, out discarded);
        }

        /// <summary>
        /// Те саме повернення, але заразом віддає і очищає заморожений результат
        /// (R15): викликач (сьогодні — тест, далі — D1) передає його в
        /// <see cref="Base.ExpeditionRunner.Complete"/> рівно один раз.
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

        // ---- зліпок ----

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

            // Заморожений результат (R15) — з ПРЕФІКСОМ ДОВЖИНИ, а не за
            // окремим роздільником ';'. Складений сейв (Core/Loop/
            // SettlementSave.cs, веде виключно Foundation/A1) ділить УВЕСЬ
            // зліпок по ';' одним проходом ДО того, як віддати значення поля
            // party= сюди — тому будь-який ';' усередині власного блоба
            // партії обрізав би хвіст мовчки, і заморожений результат
            // губився б при відновленні через справжній шлях збереження
            // (знайдено рев'ю пакета B7: сейв посеред вилазки через
            // DayProcessor.SaveState губить результат, хоча ізольований
            // CaptureState()/RestoreState() цього не показує). Префікс
            // довжини робить розбір нечутливим до вмісту результату:
            // що б у ньому не було, нижче читається рівно len символів, а не
            // шукається роздільник.
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
