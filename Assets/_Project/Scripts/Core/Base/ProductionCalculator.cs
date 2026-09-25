using System;
using Game.Core.Balance;
using Game.Core.Characters;
using Game.Core.Stats;

namespace Game.Core.Base
{
    /// <summary>
    /// Чиста формула вироблення одного слота за один цикл. Винесена окремо,
    /// щоб і гра, і UI-передперегляд («скільки дасть цей напарник тут»),
    /// і тести використовували рівно одну формулу.
    ///
    /// Числа беруться з резолвнутого знімка, а не з полів напарника. Звідси
    /// безкоштовно випливає US-8.1 «стати й характер підсилюють будівлю»: трейт
    /// «Рукастий» і шрам «Одноокий» змінюють вироблення, не додавши сюди ні
    /// рядка — вони вже враховані агрегатором.
    /// </summary>
    public static class ProductionCalculator
    {
        /// <summary>
        /// Сире (до глобальних множників) значення вироблення напарника в слоті.
        /// </summary>
        public static double RawOutput(Companion companion, AssignmentSlotDefinition def, BalanceConfig cfg)
        {
            if (companion == null || def == null) return 0;

            StatSnapshot stats = companion.Resolve(cfg);
            double output = def.BaseOutput;
            if (def.PrimarySkill != SkillType.None)
                output += stats.Get(StatKeys.Of(def.PrimarySkill)) * def.OutputPerPrimaryPoint;
            if (def.SecondaryAttribute != AttributeType.None)
                output += stats.Get(StatKeys.Of(def.SecondaryAttribute)) * def.OutputPerSecondaryPoint;

            return output < 0 ? 0 : output;
        }

        /// <summary>
        /// Підсумкове вироблення за цикл з урахуванням глобального множника і штрафу за
        /// поранення. Округляється до найближчого цілого (ресурси — цілі).
        /// </summary>
        public static int OutputPerCycle(Companion companion, AssignmentSlotDefinition def, BalanceConfig cfg)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            double output = RawOutput(companion, def, cfg) * cfg.GlobalProductionMultiplier;
            if (companion != null && companion.IsInjured)
                output *= cfg.InjuredProductionMultiplier;
            return (int)Math.Round(output);
        }

        /// <summary>
        /// Досвід ролі за цикл з урахуванням «доброї відповідності»: якщо профільний
        /// скіл напарника дотягує до порогу, досвід іде швидше (заохочує
        /// правильну розстановку людей по місцях).
        ///
        /// Поріг тепер осмислений: половина майстерності на єдиній шкалі 0–10.
        /// </summary>
        public static int RoleXpPerCycle(Companion companion, AssignmentSlotDefinition def, BalanceConfig cfg)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            double xp = cfg.RoleXpPerCycle;
            if (companion != null && def != null && def.PrimarySkill != SkillType.None
                && companion.Resolve(cfg).GetInt(StatKeys.Of(def.PrimarySkill)) >= cfg.SkillMatchThreshold)
            {
                xp *= cfg.WellSuitedXpMultiplier;
            }
            return (int)Math.Round(xp);
        }
    }
}
