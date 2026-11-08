using System;
using System.Collections.Generic;
using Game.Core.Ring;

namespace Game.Gameplay.Ring.Models
{
    /// <summary>
    /// Схвалений звук гри (рішення власника 08.11.2026: «Выключи все звуки пока я не скажу обратного из всех будущих
    /// сборок. Собери мне библиотеку всех треков где я смогу их заапрувить и тогда они идут в игру, если реджект нужны
    /// еще 3 пока какую-то не выберу»). Грає лише кліп, назву якого (ім'я файлу без розширення) власник схвалив у
    /// бібліотеці звуку; список — <c>Resources/Ring/sound_approvals.json</c>. Порожній список — уся гра мовчить.
    /// Будь-яка помилка читання — теж тиша: звук без схвалення гірший за тишу.
    /// </summary>
    public sealed class RingSoundApproval
    {
        private readonly HashSet<string> _approved;

        private RingSoundApproval(HashSet<string> approved) { _approved = approved; }

        public static readonly RingSoundApproval None = new RingSoundApproval(new HashSet<string>(StringComparer.Ordinal));

        /// <summary>Скільки кліпів схвалено.</summary>
        public int Count { get { return _approved.Count; } }

        /// <summary>Гучність слухача на старті: поки нічого не схвалено — 0 (тиша навіть для коду, що обійшов ворота).</summary>
        public float ListenerVolume { get { return _approved.Count == 0 ? 0f : 1f; } }

        /// <summary>Чи можна грати кліп з цією назвою.</summary>
        public bool Allows(string clipName)
        {
            return !string.IsNullOrEmpty(clipName) && _approved.Contains(clipName);
        }

        public IEnumerable<string> Approved { get { return _approved; } }

        /// <summary>Читає <c>{"approved": ["назва_кліпу", …]}</c>. Зіпсований файл чи не той формат — нічого не схвалено.</summary>
        public static RingSoundApproval Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return None;
            try
            {
                var o = MiniJson.ParseObject(json);
                object list;
                if (!o.TryGetValue("approved", out list)) return None;
                var items = list as List<object>;
                if (items == null) return None;
                var set = new HashSet<string>(StringComparer.Ordinal);
                foreach (var it in items)
                {
                    var s = it as string;
                    if (!string.IsNullOrEmpty(s)) set.Add(s.Trim());
                }
                return set.Count == 0 ? None : new RingSoundApproval(set);
            }
            catch (Exception)
            {
                return None;
            }
        }
    }
}
