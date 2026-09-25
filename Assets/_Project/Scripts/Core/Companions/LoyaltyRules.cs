using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;

namespace Game.Core.Companions
{
    /// <summary>
    /// Таблица наслідків Лояльності (R2, docs/TEST_BUILD.md §3.7): які вибори
    /// гравця зсувають чию лояльність. Іменовані точки входу замість магічних
    /// чисел, розкидоних по пакетах B6/D1, що будуть звати ці методи —
    /// цифри самі живуть у <see cref="CompanionSocialBalance"/> (R14).
    ///
    /// internal: всі методи повертають <see cref="LoyaltyChange"/>, що несе
    /// сире Delta (інваріант 3) — той самий контур укриття, що й у
    /// <see cref="Companion.ApplyLoyaltyDelta"/>. Game.Gameplay не бачить ні
    /// клас, ні числа; дістається лише через майбутній GameSession-фасад (D1),
    /// що конвертує в подію «loyalty.band_changed» (без сирого delta).
    /// </summary>
    internal static class LoyaltyRules
    {
        private static readonly CompanionSocialBalance DefaultSocial = new CompanionSocialBalance();

        /// <summary>
        /// Кривавий шлях (Поправка №1, "PlaystyleBlood"): б'є по лояльності
        /// мирно-ціннісних (mercy/order), полегшує жорстко-ціннісним
        /// (ruthless/freedom). Проходить по всьому присутньому складу — той
        /// самий генеричний механізм, що працює на будь-якій "Ж"-точці
        /// кампанії (§2 рядок 23: "похідне з майже всіх рішень").
        /// </summary>
        internal static List<LoyaltyChange> OnBloodyChoice(Roster roster, BalanceConfig cfg, string sourceId = "playstyle:bloody")
        {
            var result = new List<LoyaltyChange>();
            if (roster == null) return result;
            var social = cfg?.CompanionSocial ?? DefaultSocial;

            foreach (var c in roster.All)
            {
                if (c.IsDead || c.Status == CompanionStatus.Antagonist) continue;

                bool valuesPeace = HasValue(c, DefaultValues.Mercy) || HasValue(c, DefaultValues.Order);
                bool valuesRuthless = HasValue(c, DefaultValues.Ruthless) || HasValue(c, DefaultValues.Freedom);

                int delta = valuesRuthless ? social.BloodyChoiceRuthlessRelief
                    : (valuesPeace ? -social.BloodyChoiceValuedHit : 0);
                if (delta == 0) continue;

                result.Add(c.ApplyLoyaltyDelta(delta, sourceId));
            }
            return result;
        }

        /// <summary>Ігнорована прохання/квест-етап конкретного напарника.</summary>
        internal static LoyaltyChange OnRequestIgnored(Companion requester, BalanceConfig cfg, string sourceId = "request_ignored")
        {
            var social = cfg?.CompanionSocial ?? DefaultSocial;
            return requester.ApplyLoyaltyDelta(-social.RequestIgnoredHit, sourceId);
        }

        /// <summary>
        /// Гравець урятував/захистив напарника у власній біді (напр. вузол 1
        /// "Найкраща"/"Хороша" для Мирослави, docs/TEST_BUILD.md §3.1). Величину
        /// (delta) задає викликаючий пакет (B6 знає конкретні "+15"/"+5" зі
        /// сценарію) — тут лише дефолт на випадок, коли конкретики нема.
        /// </summary>
        internal static LoyaltyChange OnCompanionProtected(Companion c, BalanceConfig cfg, int? magnitude = null, string sourceId = "companion_protected")
        {
            var social = cfg?.CompanionSocial ?? DefaultSocial;
            int delta = magnitude ?? social.CompanionProtectedBonus;
            return c.ApplyLoyaltyDelta(delta, sourceId);
        }

        /// <summary>Дзеркало <see cref="OnCompanionProtected"/>: напарника покинули у власній біді.</summary>
        internal static LoyaltyChange OnCompanionAbandoned(Companion c, BalanceConfig cfg, int? magnitude = null, string sourceId = "companion_abandoned")
        {
            var social = cfg?.CompanionSocial ?? DefaultSocial;
            int delta = -(magnitude ?? social.CompanionAbandonedHit);
            return c.ApplyLoyaltyDelta(delta, sourceId);
        }

        /// <summary>
        /// Аудит G25: мораль ради вмирала, бо ніхто не читав пасивний бонус
        /// "Morale" з council_seat (<see cref="Game.Core.Base.CycleReport.PassiveBonuses"/>).
        /// D1 передає <see cref="CycleReport"/> сюди після кожного циклу
        /// (§1.1: "D1 передає CycleReport"); поки цей виклик нікуди не
        /// підключено — шов для D1 (seamsForD1 звіту пакета).
        /// </summary>
        internal static List<LoyaltyChange> OnMorale(CycleReport report, Roster roster, BalanceConfig cfg)
        {
            var result = new List<LoyaltyChange>();
            if (report == null || roster == null) return result;
            if (!report.PassiveBonuses.TryGetValue("Morale", out var morale) || morale <= 0) return result;

            var social = cfg?.CompanionSocial ?? DefaultSocial;
            foreach (var c in roster.All)
            {
                if (c.IsDead || c.Status == CompanionStatus.Antagonist) continue;
                result.Add(c.ApplyLoyaltyDelta(social.MoraleLoyaltyBonus, "council_seat:morale"));
            }
            return result;
        }

        private static bool HasValue(Companion c, string tag)
        {
            var values = c.Traits.Values;
            for (int i = 0; i < values.Count; i++)
                if (values[i] == tag) return true;
            return false;
        }
    }
}
