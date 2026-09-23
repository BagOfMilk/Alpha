using System;
using System.Collections.Generic;

namespace Game.Core.Story
{
    /// <summary>
    /// Сюжетные флаги (R3): именованные булевы отметки вроде «видел предложение
    /// Тугара» или «Мирослава посеяна как будущая беглянка».
    ///
    /// ЧИСТЫЙ КОНТЕЙНЕР. Раньше в проекте одна и та же концепция заводилась
    /// дважды (Core/Story и Core/Loop) — конфликт снят решением интегратора:
    /// контейнер живёт здесь, персистентность даёт свойство DayProcessor.Flags
    /// (Foundation/A1), а СОБЫТИЯ на смену флага не эмитит ни контейнер, ни
    /// свойство — это работа GameSession (D1), когда он появится. До этого
    /// момента флаг меняется молча: раньше эмитить событие самому — значит
    /// эмитить его до того, как хоть один слушатель умеет с ним обращаться.
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

        /// <summary>Выставить флаг. Сам ничего не эмитит — см. класс.</summary>
        public void Set(string flagId, bool value = true)
        {
            if (string.IsNullOrEmpty(flagId)) return;
            _flags[flagId] = value;
        }

        /// <summary>Только выставленные (true) флаги, по имени — детерминированный порядок.</summary>
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
