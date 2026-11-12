namespace Game.Core.Ring
{
    /// <summary>
    /// Пізня роль борця (TRPG_DESIGN §5.3, черга П5 від ПМ 12.11.2026): «Пізні ролі (після першого сезону): Ікона (+1 ажіотажу на
    /// старті матчу), Легенда (додатковий жетон реверсу), Зірка поза рингом (промо дають гроші, але менше часу на тренування)».
    /// Одна на раз; обирають на екрані кінця сезону (з кінця першого), змінити можна на кожному наступному. Числа — ПЛЕЙСХОЛДЕР.
    /// </summary>
    public enum LateRole { None = 0, Icon = 1, Legend = 2, OffRingStar = 3 }

    public static class LateRoles
    {
        /// <summary>Ролі на вибір (без «немає»), у порядку показу.</summary>
        public static readonly LateRole[] All = { LateRole.Icon, LateRole.Legend, LateRole.OffRingStar };

        /// <summary>Ікона: ажіотаж на старті кожного матчу.</summary>
        public const int IconStartHype = 1;
        /// <summary>Легенда: жетонів реверсу в кожному матчі.</summary>
        public const int LegendReversalTokens = 1;
        /// <summary>Зірка поза рингом: гроші за промо — «10+» і «7–9»; на «≤ 6» — нічого.</summary>
        public const int StarPromoMoneyStrong = 20;
        public const int StarPromoMoneyPartial = 10;
        /// <summary>Зірка поза рингом: тренувань на тиждень («менше часу на тренування»).</summary>
        public const int StarTrainingsPerWeek = 1;

        /// <summary>Поправки матчу від пізньої ролі — поверх перків (той самий збирач, «один ефект — одна система»).</summary>
        public static void ApplyTo(MatchModifiers m, LateRole r)
        {
            if (m == null) return;
            if (r == LateRole.Icon) m.StartHypeBonus += IconStartHype;
            else if (r == LateRole.Legend) m.ExtraReversalTokens += LegendReversalTokens;
        }

        /// <summary>Ключ назви: c.late.icon / c.late.legend / c.late.star / c.late.none (опис — той самий ключ + «.desc»).</summary>
        public static string Key(LateRole r)
        {
            switch (r)
            {
                case LateRole.Icon: return "c.late.icon";
                case LateRole.Legend: return "c.late.legend";
                case LateRole.OffRingStar: return "c.late.star";
                default: return "c.late.none";
            }
        }
    }
}
