using System;
using Game.Core.Balance;
using Game.Core.Characters;
using Game.Core.Stats;

namespace Game.Core.Base
{
    /// <summary>
    /// Чистая формула выработки одного слота за один цикл. Вынесена отдельно,
    /// чтобы и игра, и UI-предпросмотр («сколько даст этот напарник здесь»),
    /// и тесты использовали ровно одну формулу.
    /// </summary>
    public static class ProductionCalculator
    {
        /// <summary>
        /// Сырое (до глобальных множителей) значение выработки напарника в слоте.
        /// </summary>
        public static double RawOutput(Companion companion, AssignmentSlotDefinition def)
        {
            if (companion == null || def == null) return 0;

            double output = def.BaseOutput;
            if (def.PrimaryAptitude != StatType.None)
                output += companion.GetStat(def.PrimaryAptitude) * def.OutputPerPrimaryPoint;
            if (def.SecondaryAptitude != StatType.None)
                output += companion.GetStat(def.SecondaryAptitude) * def.OutputPerSecondaryPoint;

            return output < 0 ? 0 : output;
        }

        /// <summary>
        /// Итоговая выработка за цикл с учётом глобального множителя и штрафа за
        /// ранение. Округляется к ближайшему целому (ресурсы — целые).
        /// </summary>
        public static int OutputPerCycle(Companion companion, AssignmentSlotDefinition def, BalanceConfig cfg)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            double output = RawOutput(companion, def) * cfg.GlobalProductionMultiplier;
            if (companion != null && companion.IsInjured)
                output *= cfg.InjuredProductionMultiplier;
            return (int)Math.Round(output);
        }

        /// <summary>
        /// Опыт роли за цикл с учётом «хорошего соответствия»: если основной стат
        /// напарника под роль высок, опыт идёт быстрее (поощряет правильную
        /// расстановку людей по местам).
        /// </summary>
        public static int RoleXpPerCycle(Companion companion, AssignmentSlotDefinition def, BalanceConfig cfg)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            double xp = cfg.RoleXpPerCycle;
            if (companion != null && def != null && def.PrimaryAptitude != StatType.None
                && companion.GetStat(def.PrimaryAptitude) >= cfg.AptitudeMatchThreshold)
            {
                xp *= cfg.WellSuitedXpMultiplier;
            }
            return (int)Math.Round(xp);
        }
    }
}
