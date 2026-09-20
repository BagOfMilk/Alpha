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
    ///
    /// Числа берутся из резолвнутого снапшота, а не из полей напарника. Отсюда
    /// бесплатно следует US-8.1 «статы и характер усиливают здание»: трейт
    /// «Рукастый» и шрам «Одноглазый» меняют выработку, не добавив сюда ни
    /// строки — они уже учтены агрегатором.
    /// </summary>
    public static class ProductionCalculator
    {
        /// <summary>
        /// Сырое (до глобальных множителей) значение выработки напарника в слоте.
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
        /// Итоговая выработка за цикл с учётом глобального множителя и штрафа за
        /// ранение. Округляется к ближайшему целому (ресурсы — целые).
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
        /// Опыт роли за цикл с учётом «хорошего соответствия»: если профильный
        /// скил напарника дотягивает до порога, опыт идёт быстрее (поощряет
        /// правильную расстановку людей по местам).
        ///
        /// Порог теперь осмыслен: половина мастерства на единственной шкале 0–10.
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
