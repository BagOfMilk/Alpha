using System;
using System.Collections.Generic;
using Game.Core.Characters;
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
        CompanionUnavailable = 5   // мёртв, ранен или уже в вылазке
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
        public static DispatchResult Send(BaseState state, ExpeditionSite site,
            IReadOnlyList<string> companionIds, out List<ISettlementActor> party)
        {
            party = null;
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (site == null) return DispatchResult.NoSuchSite;
            if (companionIds == null || companionIds.Count == 0) return DispatchResult.EmptyParty;
            if (companionIds.Count > state.Balance.ExpeditionPartyMax) return DispatchResult.PartyTooLarge;

            // Сначала проверяем всех, потом меняем хоть кого-то: отряд уходит
            // целиком или не уходит вовсе, иначе половина ростера осталась бы
            // снятой с постов из-за одного мёртвого в списке.
            var chosen = new List<Companion>(companionIds.Count);
            for (int i = 0; i < companionIds.Count; i++)
            {
                var c = state.Roster.Get(companionIds[i]);
                if (c == null) return DispatchResult.UnknownCompanion;
                if (c.IsDead || c.IsInjured || c.Status == CompanionStatus.OnMission)
                    return DispatchResult.CompanionUnavailable;
                chosen.Add(c);
            }

            party = new List<ISettlementActor>(chosen.Count);
            for (int i = 0; i < chosen.Count; i++)
            {
                var c = chosen[i];
                if (c.IsAssigned) state.Unassign(c.AssignedSlotId);
                c.Status = CompanionStatus.OnMission;
                party.Add(new CompanionActorAdapter(c, false, state.Balance));
            }

            return DispatchResult.Success;
        }

        /// <summary>
        /// Возврат отряда: добыча в кошелёк, раны на людей, статусы назад.
        ///
        /// Материалы попадают в игру ТОЛЬКО отсюда — это и есть кран, которого
        /// требует Э6.2 и Приложение А. Второго входа нет, и его отсутствие
        /// проверяется тестом.
        /// </summary>
        public static void Complete(BaseState state, ExpeditionResult result)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (result == null) return;

            if (result.Materials > 0) state.Resources.Add(ResourceType.Materials, result.Materials);
            if (result.Gold > 0) state.Resources.Add(ResourceType.Gold, result.Gold);

            var wounded = new HashSet<string>();
            for (int i = 0; i < result.Wounded.Count; i++)
            {
                var w = result.Wounded[i];
                var c = state.Roster.Get(w.ActorId);
                if (c == null || c.IsDead) continue;

                c.InjuryPoints += PointsFor(w.Tier, state);
                c.Status = CompanionStatus.Injured;
                wounded.Add(w.ActorId);
            }

            for (int i = 0; i < result.PartyIds.Count; i++)
            {
                if (wounded.Contains(result.PartyIds[i])) continue;
                var c = state.Roster.Get(result.PartyIds[i]);
                if (c == null || c.IsDead) continue;
                c.Status = CompanionStatus.Idle;
            }
        }

        private static double PointsFor(WoundTier tier, BaseState state)
        {
            switch (tier)
            {
                case WoundTier.Light: return state.Balance.ExpeditionLightWoundPoints;
                case WoundTier.Serious: return state.Balance.ExpeditionSeriousWoundPoints;
                case WoundTier.Critical: return state.Balance.ExpeditionSeriousWoundPoints * 2;
                default: return 0;
            }
        }
    }
}
