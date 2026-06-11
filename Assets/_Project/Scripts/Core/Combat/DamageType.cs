using System;
using System.Collections.Generic;

namespace Game.Core.Combat
{
    /// <summary>
    /// Лёгкая ось типов урона (US-3.12): множитель на попадании по резист/уязвимости
    /// цели. НЕЗАВИСИМА от брони (броня — флэт-снижение + пробитие/Шред). True —
    /// типless-урон (кровотечение), множители не применяются.
    /// </summary>
    public enum DamageType
    {
        True = 0,      // без типа: кровотечение и пр. — множители не применяются
        Ballistic = 1, // баллистический
        Fire = 2,      // огонь (может накладывать Поджог)
        Toxin = 3,     // токсин (может накладывать Яд)
        Energy = 4     // энергия
        // + слот под постапок-тип (ПЛЕЙСХОЛДЕР, добавится контентом)
    }

    /// <summary>
    /// Профиль резист/уязвимость юнита: множитель урона per тип. Нет записи = ×1.
    /// Резист ×0.5–0.75, уязвимость ×1.25–1.5 (ПЛЕЙСХОЛДЕРЫ из Прил. Б).
    /// </summary>
    [Serializable]
    public sealed class ResistProfile
    {
        private readonly Dictionary<DamageType, double> _multipliers = new Dictionary<DamageType, double>();

        public double Multiplier(DamageType type)
            => type != DamageType.True && _multipliers.TryGetValue(type, out var m) ? m : 1.0;

        public ResistProfile With(DamageType type, double multiplier)
        {
            if (type != DamageType.True) _multipliers[type] = multiplier;
            return this;
        }
    }
}
