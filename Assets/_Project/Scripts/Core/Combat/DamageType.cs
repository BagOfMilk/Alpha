using System;
using System.Collections.Generic;

namespace Game.Core.Combat
{
    /// <summary>
    /// Легка вісь типів урону: множник на влучанні за резист/вразливістю
    /// цілі. НЕЗАЛЕЖНА від броні (броня — флет-зниження + пробиття/Шред). True —
    /// типless-урон (кровотеча), множники не застосовуються.
    /// </summary>
    public enum DamageType
    {
        True = 0,      // без типу: кровотеча та ін. — множники не застосовуються
        Ballistic = 1, // балістичний (стріли, метальне)
        Fire = 2,      // вогонь (може накладати Підпал)
        Toxin = 3,     // токсин (може накладати Отруту)
        Energy = 4     // енергія (ПЛЕЙСХОЛДЕР під майбутній контент)
    }

    /// <summary>
    /// Профіль резист/вразливість юніта: множник урону per тип. Немає запису = ×1.
    /// Резист ×0.5–0.75, вразливість ×1.25–1.5 (ПЛЕЙСХОЛДЕРИ, крутяться балансом).
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
