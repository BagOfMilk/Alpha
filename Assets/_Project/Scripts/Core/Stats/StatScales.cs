using System.Collections.Generic;
using Game.Core.Balance;

namespace Game.Core.Stats
{
    /// <summary>
    /// Межі шкал і їх перевірка.
    ///
    /// Навіщо окремий тип: у моделі ітерації 1 бойові стати жили в шкалі 0–100,
    /// а рольові схильності в 0–7, і обидві потрапляли в одну формулу виробітку —
    /// через це Командир на розвідпосту обганяв профільного Розвідника.
    /// Тут шкали оголошені явно і перевірювані, щоб це не повторилося.
    /// </summary>
    public static class StatScales
    {
        /// <summary>Атрибут поза 1–10 або скіл поза 0–10 — помилка контенту, а не смак.</summary>
        public static bool IsValid(AttributeType a, int value, BalanceConfig cfg)
            => a == AttributeType.None || (value >= cfg.MinAttribute && value <= cfg.MaxAttribute);

        public static bool IsValid(SkillType s, int value, BalanceConfig cfg)
            => s == SkillType.None || (value >= 0 && value <= cfg.MaxSkillLevel);

        public static int ClampAttribute(int value, BalanceConfig cfg)
            => value < cfg.MinAttribute ? cfg.MinAttribute : (value > cfg.MaxAttribute ? cfg.MaxAttribute : value);

        public static int ClampSkill(int value, BalanceConfig cfg)
            => value < 0 ? 0 : (value > cfg.MaxSkillLevel ? cfg.MaxSkillLevel : value);

        /// <summary>
        /// Перелічує все, що вилізло за шкали. Порожній список — контент у порядку.
        /// Використовується тестом-парканом по стартовому контенту.
        /// </summary>
        public static List<string> Violations(AttributeSet attrs, SkillSet skills, BalanceConfig cfg, string ownerId = null)
        {
            var problems = new List<string>();
            string who = string.IsNullOrEmpty(ownerId) ? "" : ownerId + ": ";

            if (attrs != null)
                for (int i = 0; i < Attributes.All.Length; i++)
                {
                    var a = Attributes.All[i];
                    int v = attrs[a];
                    if (!IsValid(a, v, cfg))
                        problems.Add($"{who}{Attributes.DisplayName(a)} = {v}, шкала {cfg.MinAttribute}–{cfg.MaxAttribute}");
                }

            if (skills != null)
                for (int i = 0; i < Skills.All.Length; i++)
                {
                    var s = Skills.All[i];
                    int v = skills[s];
                    if (!IsValid(s, v, cfg))
                        problems.Add($"{who}{Skills.DisplayName(s)} = {v}, шкала 0–{cfg.MaxSkillLevel}");
                }

            return problems;
        }
    }
}
