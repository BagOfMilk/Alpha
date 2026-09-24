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
    /// <summary>Почему отряд не вышел.</summary>
    public enum DispatchResult
    {
        Success = 0,
        NoSuchSite = 1,
        EmptyParty = 2,
        PartyTooLarge = 3,
        UnknownCompanion = 4,
        CompanionUnavailable = 5,  // мёртв, ранен, уже в вылазке или враждебен (Antagonist)
        DuplicateCompanion = 6,    // один и тот же человек дважды в списке
        PartyAlreadyAway = 7       // прошлый отряд ещё не вернулся (R15: партия одна)
    }

    /// <summary>
    /// Мост «база ↔ вылазка». Шов узкий намеренно: вылазка не знает про слоты
    /// и кошелёк, база не знает, как считается исход.
    ///
    /// Отправка освобождает позицию сразу (US-8.3): пост, который некому
    /// держать, — это цена вылазки, и платится она в тот же день, а не по
    /// возвращении.
    /// </summary>
    public static class ExpeditionRunner
    {
        /// <summary>
        /// Единая точка входа вылазки (R15, закрывает D10): валидирует состав →
        /// <see cref="ExpeditionParty.Depart"/> → если подход не Delve, тут же
        /// РОВНО ОДИН РАЗ резолвит исход (<see cref="ExpeditionResolver.Resolve"/>)
        /// и замораживает его в блобе партии (<see cref="ExpeditionParty.FreezeResult"/>).
        /// Дальше до возвращения к исходу никто не притрагивается — поэтому сейв
        /// посреди вылазки и обычное продолжение дают один и тот же результат.
        ///
        /// Для Delve резолв НЕ вызывается: дальше вылазка играется комнатами
        /// данжа (Core/Dungeons, B2), а диспетчинг в данж ведёт D1.
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

            // Сначала проверяем всех, потом меняем хоть кого-то: отряд уходит
            // целиком или не уходит вовсе, иначе половина ростера осталась бы
            // снятой с постов из-за одного мёртвого в списке.
            var chosen = new List<Companion>(companionIds.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < companionIds.Count; i++)
            {
                var c = state.Roster.Get(companionIds[i]);
                if (c == null) return DispatchResult.UnknownCompanion;

                // Один человек дважды в списке — не безобидная опечатка: он
                // добавил бы половину себя к силе отряда и получил бы двойную
                // рану на возврате. Дедупликация молча скрыла бы ошибку
                // вызывающего, поэтому отказ явный.
                if (!seen.Add(c.Id)) return DispatchResult.DuplicateCompanion;

                if (c.IsDead || c.IsInjured || c.Status == CompanionStatus.OnMission || IsAntagonist(c.Status))
                    return DispatchResult.CompanionUnavailable;
                chosen.Add(c);
            }

            if (!party.Depart(state, companionIds, days))
                return DispatchResult.CompanionUnavailable;

            // Delve пропускает резолв (R15/§4.11): дальше — комнаты данжа.
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
        /// Допуск к вылазке исключает враждебных (R2/B4): проверка по ИМЕНИ
        /// статуса, а не по значению enum — <c>CompanionStatus.Antagonist</c> в
        /// этом рабочем дереве ещё не существует (его заводит параллельный
        /// пакет B4), и код обязан остаться верным без правки, когда он
        /// появится после мерджа. Сегодня метод всегда возвращает false — это
        /// ожидаемо, не заглушка «на будущее без эффекта сейчас»: враждебных
        /// напарников в этом дереве ещё нет вовсе.
        /// </summary>
        private static bool IsAntagonist(CompanionStatus status) =>
            string.Equals(status.ToString(), "Antagonist", StringComparison.Ordinal);

        /// <summary>
        /// Возврат отряда: добыча в кошелёк, раны на людей, статусы назад.
        ///
        /// Материалы попадают в игру ТОЛЬКО отсюда — это и есть кран, которого
        /// требует Э6.2 и Приложение А. Второго входа нет, и его отсутствие
        /// проверяется тестом.
        /// </summary>
        public static void Complete(BaseState state, ExpeditionResult result, CityWorks works = null)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (result == null) return;

            if (result.Materials > 0) state.Resources.Add(ResourceType.Materials, result.Materials);
            if (result.Gold > 0) state.Resources.Add(ResourceType.Gold, result.Gold);

            // Найденные люди входят в город ближайшими сутками, а не сейчас:
            // возврат отряда идёт между фазами, и прирост без сигнала был бы
            // тихим изменением числа (Поправка №6.3).
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

                // Рана с вылазки (R16/G10): та же единая точка решения, что и
                // у RosterAdapter.Wound — DefaultScars.TryGrant, а не вторая
                // копия правила «Серьёзная+ даёт шрам».
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

        // Критический тир вылазка не выдаёт: его источник — бой, которого ещё
        // нет. Ветка под него не заводится заранее — недостижимый case выглядит
        // как покрытие, которого на деле нет.
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
