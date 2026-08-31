using System.Collections.Generic;
using Game.Core.Balance;

namespace Game.Core.Stats
{
    /// <summary>
    /// Границы шкал и их проверка.
    ///
    /// Зачем отдельный тип: в модели итерации 1 боевые статы жили в шкале 0–100,
    /// а ролевые склонности в 0–7, и обе попадали в одну формулу выработки —
    /// из-за этого Командир на разведпосту обгонял профильного Разведчика.
    /// Здесь шкалы объявлены явно и проверяемы, чтобы это не повторилось.
    /// </summary>
    public static class StatScales
    {
        /// <summary>Атрибут вне 1–10 или скил вне 0–10 — ошибка контента, а не вкус.</summary>
        public static bool IsValid(AttributeType a, int value, BalanceConfig cfg)
            => a == AttributeType.None || (value >= cfg.MinAttribute && value <= cfg.MaxAttribute);

        public static bool IsValid(SkillType s, int value, BalanceConfig cfg)
            => s == SkillType.None || (value >= 0 && value <= cfg.MaxSkillLevel);

        public static int ClampAttribute(int value, BalanceConfig cfg)
            => value < cfg.MinAttribute ? cfg.MinAttribute : (value > cfg.MaxAttribute ? cfg.MaxAttribute : value);

        public static int ClampSkill(int value, BalanceConfig cfg)
            => value < 0 ? 0 : (value > cfg.MaxSkillLevel ? cfg.MaxSkillLevel : value);

        /// <summary>
        /// Перечисляет всё, что вылезло за шкалы. Пустой список — контент в порядке.
        /// Используется тестом-забором по стартовому контенту.
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
