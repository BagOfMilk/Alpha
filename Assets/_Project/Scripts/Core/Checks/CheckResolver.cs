using System;
using System.Collections.Generic;
using Game.Core.Characters;
using Game.Core.Stats;

namespace Game.Core.Checks
{
    /// <summary>Подход к соц-проверке — задаёт соц-скил и контекстный атрибут (US-2.6).</summary>
    public enum CheckApproach
    {
        Persuade = 0,    // Убеждение + Смекалка
        Intimidate = 1,  // Запугивание + Сила/Воля (берётся лучшее)
        Trade = 2        // Торговля + Смекалка
    }

    /// <summary>Итог детерминированной проверки.</summary>
    public readonly struct CheckResult
    {
        public readonly bool Success;
        public readonly int Value;            // лучшее достигнутое значение среди присутствующих
        public readonly int Threshold;
        public readonly string ResolvedById;  // кто «вывез» проверку (null, если некем)

        public CheckResult(bool success, int value, int threshold, string resolvedById)
        {
            Success = success;
            Value = value;
            Threshold = threshold;
            ResolvedById = resolvedById;
        }
    }

    /// <summary>
    /// Детерминированные проверки (GDD §2.4 US-2.6): скил ≥ порога = успех. Костей
    /// нет — провал есть следствие подготовки. Проверку проходит ЛУЧШИЙ релевантный
    /// результат среди присутствующих; трейты и шрамы дают флэт-модификатор; порог
    /// известен заранее (показывается в UI).
    /// </summary>
    public static class CheckResolver
    {
        /// <summary>Проверка по конкретному скилу (утилита/бой): значение = скил + трейты/шрамы.</summary>
        public static CheckResult Resolve(IEnumerable<Companion> participants, SkillType skill, int threshold)
        {
            int best = 0;
            string bestId = null;
            bool any = false;
            if (participants != null)
            {
                foreach (var c in participants)
                {
                    if (c == null || !c.IsOnPlayerSide) continue; // ушедший к врагу проверки не вывозит (US-9.4)
                    int v = c.GetSkill(skill) + c.CheckModifierFor(skill);
                    if (!any || v > best) { best = v; bestId = c.Id; any = true; }
                }
            }
            return new CheckResult(any && best >= threshold, any ? best : 0, threshold, bestId);
        }

        /// <summary>Соц-проверка: значение = соц-скил + контекстный атрибут под подход + трейты/шрамы.</summary>
        public static CheckResult ResolveSocial(IEnumerable<Companion> participants, CheckApproach approach, int threshold)
        {
            SkillType skill = SocialSkill(approach);
            int best = 0;
            string bestId = null;
            bool any = false;
            if (participants != null)
            {
                foreach (var c in participants)
                {
                    if (c == null || !c.IsOnPlayerSide) continue; // см. Resolve: антагонист за игрока не говорит
                    int v = c.GetSkill(skill) + ContextualAttribute(approach, c) + c.CheckModifierFor(skill);
                    if (!any || v > best) { best = v; bestId = c.Id; any = true; }
                }
            }
            return new CheckResult(any && best >= threshold, any ? best : 0, threshold, bestId);
        }

        public static SkillType SocialSkill(CheckApproach approach)
        {
            switch (approach)
            {
                case CheckApproach.Persuade:   return SkillType.Persuasion;
                case CheckApproach.Intimidate: return SkillType.Intimidation;
                case CheckApproach.Trade:      return SkillType.Trade;
                default:                       return SkillType.None;
            }
        }

        private static int ContextualAttribute(CheckApproach approach, Companion c)
        {
            switch (approach)
            {
                case CheckApproach.Intimidate: // Сила/Воля — берём лучшее
                    return Math.Max(c.GetAttribute(AttributeType.Strength), c.GetAttribute(AttributeType.Will));
                case CheckApproach.Persuade:   // Смекалка
                case CheckApproach.Trade:      // Смекалка
                    return c.GetAttribute(AttributeType.Wits);
                default:
                    return 0;
            }
        }
    }
}
