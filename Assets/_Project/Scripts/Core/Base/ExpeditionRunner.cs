using System;
using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Characters;
using Game.Core.Characters.Scars;
using Game.Core.Checks;
using Game.Core.Economy;
using Game.Core.Expeditions;
using Game.Core.World;

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
        PartyAlreadyAway = 7,      // минулий загін ще не повернувся (R15: партія одна)

        // ---- Поправка №8.3 (M1.6): заступник на пост того, хто йде ----
        /// <summary>
        /// У збори входить людина з відкритого поста, є кому її замінити, а
        /// гравець не обрав ні заступника, ні «лишити пост порожнім» — без
        /// мовчазного автопризначення (Поправка №8.3, власник: «Гравець
        /// призначає заступника вручну при зборах»).
        /// </summary>
        SubstituteNotChosen = 8,

        /// <summary>Заступник не годиться: не вільний, у загоні, вказаний двічі, або пост не звільняється цим загоном.</summary>
        SubstituteInvalid = 9
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
        /// Чи може цей склад піти зараз — ТІ САМІ відмови, що й у
        /// <see cref="Depart"/>, але без жодної зміни стану (M1.6: збори
        /// перевіряють заступників лише для законного загону, тож порядок
        /// відмов не залежить від того, хто питає).
        /// </summary>
        public static DispatchResult CheckParty(BaseState state, ExpeditionParty party, IReadOnlyList<string> companionIds)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (party == null) throw new ArgumentNullException(nameof(party));
            if (companionIds == null || companionIds.Count == 0) return DispatchResult.EmptyParty;
            if (companionIds.Count > state.Balance.ExpeditionPartyMax) return DispatchResult.PartyTooLarge;
            if (party.IsAway) return DispatchResult.PartyAlreadyAway;

            // Спочатку перевіряємо всіх, потім міняємо хоч когось: загін іде
            // цілком або не йде зовсім, інакше половина ростера лишилась би
            // знятою з постів через одного мертвого у списку.
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

                if (c.IsDead || c.IsInjured || c.Status == CompanionStatus.OnMission || IsAntagonist(c.Status) || c.IsCaptive)
                    return DispatchResult.CompanionUnavailable;
            }
            return DispatchResult.Success;
        }

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
            SiteLedger ledger, BalanceConfig cfg = null, WeatherKind weather = WeatherKind.Clear)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (party == null) throw new ArgumentNullException(nameof(party));
            if (site == null) return DispatchResult.NoSuchSite;
            var legality = CheckParty(state, party, companionIds);
            if (legality != DispatchResult.Success) return legality;

            var chosen = new List<Companion>(companionIds.Count);
            for (int i = 0; i < companionIds.Count; i++) chosen.Add(state.Roster.Get(companionIds[i]));

            if (!party.Depart(state, companionIds, days))
                return DispatchResult.CompanionUnavailable;

            // Delve пропускає резолв (R15/§4.11): далі — кімнати данжа.
            if (approach != ExpeditionApproach.Delve)
            {
                var actors = new List<ISettlementActor>(chosen.Count);
                for (int i = 0; i < chosen.Count; i++)
                    actors.Add(new CompanionActorAdapter(chosen[i], false, cfg ?? state.Balance));

                var result = ExpeditionResolver.Resolve(site, approach, actors, ledger, cfg ?? state.Balance, weather);
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
        /// Обидва компоненти (будівельний і крафтовий, Поправка №12.5)
        /// потрапляють у гру з вилазки звідси — це і є кран, якого
        /// вимагає Е6.2 і Додаток А. Другого входу немає, і його відсутність
        /// перевіряється тестом.
        /// </summary>
        public static void Complete(BaseState state, ExpeditionResult result, CityWorks works = null)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (result == null) return;

            if (result.BuildComponent > 0) state.Resources.Add(ResourceType.BuildComponent, result.BuildComponent);
            if (result.CraftComponent > 0) state.Resources.Add(ResourceType.CraftComponent, result.CraftComponent);
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
                // M1.6: хто поранений дорогою (криза в місті, бій у данжі) —
                // лишається пораненим, а не «видужує» самим поверненням; полон
                // і зрада, що сталися в данжі, повернення не скасовує (№14.7).
                if (c.IsCaptive || c.Status == CompanionStatus.Captive || c.Status == CompanionStatus.Antagonist) continue;
                c.Status = c.IsInjured ? CompanionStatus.Injured : CompanionStatus.Idle;
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
