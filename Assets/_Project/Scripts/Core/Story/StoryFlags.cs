using System;
using System.Collections.Generic;

namespace Game.Core.Story
{
    /// <summary>
    /// Сюжетні прапори (R3): іменовані булеві позначки на кшталт «бачив пропозицію
    /// Тугара» або «Мирослава посіяна як майбутня втікачка».
    ///
    /// ЧИСТИЙ КОНТЕЙНЕР. Раніше в проєкті одна й та сама концепція заводилась
    /// двічі (Core/Story і Core/Loop) — конфлікт знято рішенням інтегратора:
    /// контейнер живе тут, персистентність дає властивість DayProcessor.Flags
    /// (Foundation/A1), а ПОДІЇ на зміну прапора не емітить ні контейнер, ні
    /// властивість — це робота GameSession (D1), коли він з'явиться. До цього
    /// моменту прапор змінюється мовчки: емітити подію самому раніше — значить
    /// емітити її до того, як хоч один слухач вміє з нею поводитися.
    /// </summary>
    public sealed class StoryFlags : Loop.IStateBlob
    {
        private readonly Dictionary<string, bool> _flags = new Dictionary<string, bool>(StringComparer.Ordinal);

        public bool Get(string flagId)
        {
            if (string.IsNullOrEmpty(flagId)) return false;
            bool v;
            return _flags.TryGetValue(flagId, out v) && v;
        }

        /// <summary>Виставити прапор. Сам нічого не емітить — див. клас.</summary>
        public void Set(string flagId, bool value = true)
        {
            if (string.IsNullOrEmpty(flagId)) return;
            _flags[flagId] = value;
        }

        /// <summary>Лише виставлені (true) прапори, за іменем — детермінований порядок.</summary>
        public string CaptureState()
        {
            var set = new List<string>();
            foreach (var pair in _flags)
                if (pair.Value) set.Add(pair.Key);
            set.Sort(StringComparer.Ordinal);
            return string.Join(",", set.ToArray());
        }

        public void RestoreState(string blob)
        {
            _flags.Clear();
            if (string.IsNullOrEmpty(blob)) return;

            foreach (var id in blob.Split(','))
                if (!string.IsNullOrEmpty(id)) _flags[id] = true;
        }
    }
}
