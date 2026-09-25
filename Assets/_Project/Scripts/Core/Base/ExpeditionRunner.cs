using System;
using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Characters;
using Game.Core.Characters.Scars;
using Game.Core.Checks;
using Game.Core.Economy;
using Game.Core.Expeditions;

namespace Game.Core.Base
{
    /// <summary>Чому загін не вийшов.</summary>
    public enum DispatchResult
    {
        Success = 0,
        NoSuchSite = 1,
        EmptyParty = 2,
        PartyTooLarge = 3,
        UnknownCompanion = 4,
        CompanionUnavailable = 5,  // мертвий, поранений, уже у вилазці або ворожий (Antagonist)
        DuplicateCompanion = 6,    // та сама людина двічі у списку
        PartyAlreadyAway = 7       // минулий загін ще не повернувся (R15: партія одна)
    }

    /// <summary>
    /// Міст «база ↔ вилазка». Шов вузький навмисно: вилазка не знає про слоти
    /// і гаманець, база не знає, як рахується підсумок.
    ///
    /// Відправлення звільняє позицію одразу (US-8.3): пост, який нікому
    /// тримати, — це ціна вилазки, і платиться вона того самого дня, а не по
    /// поверненню.
    /// </summary>
    public static class ExpeditionRunner
    {
        /// <summary>
        /// Єдина точка входу вилазки (R15, закриває D10): валідує склад →
        /// <see cref="ExpeditionParty.Depart"/> → якщо підхід не Delve, тут же
        /// РІВНО ОДИН РАЗ резолвить підсумок (<see cref="ExpeditionResolver.Resolve"/>)
        /// і заморожує його в блобі партії (<see cref="ExpeditionParty.FreezeResult"/>).
        /// Далі до повернення до підсумку ніхто не торкається — тому сейв
        /// посеред вилазки і звичайне продовження дають той самий результат.
        ///
        /// Для Delve резолв НЕ викликається: далі вилазка грається кімнатами
        /// данжа (Core/Dungeons, B2), а диспетчинг у данж веде D1.
        /// </summary>
        public static DispatchResult Depart(BaseState state, ExpeditionParty party, ExpeditionSite site,
            ExpeditionApproach approach, IReadOnlyList<string> companionIds, int days,
            SiteLedger ledger, BalanceConfig cfg = null)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (party == null) throw new ArgumentNullException(nameof(party));
            if (site == null) return DispatchResult.NoSuchSite;
            if (companionIds == null || companionIds.Count == 0) return DispatchResult.EmptyParty;
            if (companionIds.Count > state.Balance.ExpeditionPartyMax) return DispatchResult.PartyTooLarge;
            if (party.IsAway) return DispatchResult.PartyAlreadyAway;

            // Спочатку перевіряємо всіх, потім міняємо хоч когось: загін іде
            // цілком або не йде зовсім, інакше половина ростера лишилась би
            // знятою з постів через одного мертвого у списку.
            var chosen = new List<Companion>(companionIds.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < companionIds.Count; i++)
            {
                var c = state.Roster.Get(companionIds[i]);
                if (c == null) return DispatchResult.UnknownCompanion;

                // Одна людина двічі у списку — не безневинна одруківка: вона
                // додала б половину себе до сили загону і отримала б подвійну
                // рану на поверненні. Дедуплікація мовчки приховала б помилку
                // викликача, тому відмова явна.
                if (!seen.Add(c.Id)) return DispatchResult.DuplicateCompanion;

                if (c.IsDead || c.IsInjured || c.Status == CompanionStatus.OnMission || IsAntagonist(c.Status))
                    return DispatchResult.CompanionUnavailable;
                chosen.Add(c);
            }

            if (!party.Depart(state, companionIds, days))
                return DispatchResult.CompanionUnavailable;

            // Delve пропускає резолв (R15/§4.11): далі — кімнати данжа.
            if (approach != ExpeditionApproach.Delve)
            {
                var actors = new List<ISettlementActor>(chosen.Count);
                for (int i = 0; i < chosen.Count; i++)
                    actors.Add(new CompanionActorAdapter(chosen[i], false, cfg ?? state.Balance));

                var result = ExpeditionResolver.Resolve(site, approach, actors, ledger, cfg ?? state.Balance);
                party.FreezeResult(result);
            }

            return DispatchResult.Success;
        }

        /// <summary>
        /// Допуск до вилазки виключає ворожих (R2/B4): перевірка за ІМЕНЕМ
        /// статусу, а не за значенням enum — <c>CompanionStatus.Antagonist</c> у
        /// цьому робочому дереві ще не існує (його заводить паралельний
        /// пакет B4), і код зобов'язаний лишитись вірним без правки, коли він
        /// з'явиться після мерджу. Сьогодні метод завжди повертає false — це
        /// очікувано, не заглушка «на майбутнє без ефекту зараз»: ворожих
        /// напарників у цьому дереві ще немає зовсім.
        /// </summary>
        private static bool IsAntagonist(CompanionStatus status) =>
            string.Equals(status.ToString(), "Antagonist", StringComparison.Ordinal);

        /// <summary>
        /// Повернення загону: здобич у гаманець, рани на людей, статуси назад.
        ///
        /// Матеріали потрапляють у гру ТІЛЬКИ звідси — це і є кран, якого
        /// вимагає Е6.2 і Додаток А. Другого входу немає, і його відсутність
        /// перевіряється тестом.
        /// </summary>
        public static void Complete(BaseState state, ExpeditionResult result, CityWorks works = null)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (result == null) return;

            if (result.Materials > 0) state.Resources.Add(ResourceType.Materials, result.Materials);
            if (result.Gold > 0) state.Resources.Add(ResourceType.Gold, result.Gold);

            // Знайдені люди входять у місто найближчою добою, а не зараз:
            // повернення загону йде між фазами, і приріст без сигналу був би
            // тихою зміною числа (Поправка №6.3).
            if (works != null && result.People > 0) works.QueueArrivals(result.People);

            var wounded = new HashSet<string>();
            for (int i = 0; i < result.Wounded.Count; i++)
            {
                var w = result.Wounded[i];
                var c = state.Roster.Get(w.ActorId);
                if (c == null || c.IsDead) continue;

                c.InjuryPoints += PointsFor(w.Tier, state);
                c.Status = CompanionStatus.Injured;
                wounded.Add(w.ActorId);

                // Рана з вилазки (R16/G10): та сама єдина точка рішення, що і
                // у RosterAdapter.Wound — DefaultScars.TryGrant, а не друга
                // копія правила «Серйозна+ дає шрам».
                DefaultScars.TryGrant(c, w.Tier, out _);
            }

            for (int i = 0; i < result.PartyIds.Count; i++)
            {
                if (wounded.Contains(result.PartyIds[i])) continue;
                var c = state.Roster.Get(result.PartyIds[i]);
                if (c == null || c.IsDead) continue;
                c.Status = CompanionStatus.Idle;
            }
        }

        // Критичний тір вилазка не видає: його джерело — бій, якого ще
        // немає. Гілка під нього не заводиться заздалегідь — недосяжний case виглядає
        // як покриття, якого насправді немає.
        private static double PointsFor(WoundTier tier, BaseState state)
        {
            switch (tier)
            {
                case WoundTier.Light: return state.Balance.ExpeditionLightWoundPoints;
                case WoundTier.Serious: return state.Balance.ExpeditionSeriousWoundPoints;
                default: return 0;
            }
        }
    }
}
