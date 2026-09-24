using System;
using System.Collections.Generic;

namespace Game.Core.Combat
{
    /// <summary>Итог тактического боя, как его видит городской луп.</summary>
    public enum BattleOutcome
    {
        Victory = 0,
        Defeat = 1,
        Retreat = 2,
        Draw = 3
    }

    /// <summary>
    /// Что случилось с одним юнитом отряда игрока. SourceCompanionId — единственная
    /// связь с ростером; сам Combat роль/пост/статус напарника НЕ трогает (§1.1:
    /// «бій ранить ЛИШЕ через RosterAdapter.Wound», Б7). Для юнитов без
    /// SourceCompanionId (обычные враги) запись не создаётся — раны считаются
    /// только по стороне игрока.
    /// </summary>
    public sealed class BattleCasualty
    {
        public readonly string CompanionId;
        public readonly int HpLost;
        public readonly bool Downed;
        public readonly bool Dead;

        public BattleCasualty(string companionId, int hpLost, bool downed, bool dead)
        {
            CompanionId = companionId;
            HpLost = hpLost;
            Downed = downed;
            Dead = dead;
        }
    }

    /// <summary>
    /// Выход тактического боя обратно в луп (§2 таблицы «одна гра», строка 30):
    /// Outcome маппится на OutcomeBand вызывающим (PassVanguardOutcome/DungeonRun/
    /// Finale — все D1), а не здесь — у каждого вызывающего своя лестница
    /// разъяснений. BattleResult сам ростер не трогает и не выдаёт шрамов: это
    /// прямо запрещено пакету Б1, Р5 отдан RosterAdapter.Wound.
    /// </summary>
    public sealed class BattleResult
    {
        public BattleOutcome Outcome;
        public int Rounds;
        public IReadOnlyList<BattleCasualty> Casualties;

        /// <summary>Id боевых юнитов игрока, которые дожили и остались на ногах (Active) — для сборки партии обратно.</summary>
        public IReadOnlyList<string> SurvivingCompanionIds;

        /// <summary>
        /// Строит BattleResult из завершённого CombatState. Бросает, если бой
        /// ещё Ongoing — вызывающий обязан дождаться исхода (Victory/Defeat/
        /// Retreat/Draw), у комбата нет «наполовину сыгранного» результата.
        /// </summary>
        public static BattleResult From(CombatState cs)
        {
            if (cs == null) throw new ArgumentNullException(nameof(cs));
            if (cs.Outcome == CombatOutcome.Ongoing)
                throw new InvalidOperationException("BattleResult.From вызван до конца боя (Outcome == Ongoing)");

            var casualties = new List<BattleCasualty>();
            var survivors = new List<string>();
            foreach (var u in cs.Units)
            {
                if (u.Side != Side.Player || string.IsNullOrEmpty(u.SourceCompanionId)) continue;

                int hpLost = Math.Max(0, u.Profile.MaxHp - u.Hp);
                bool downed = u.LifeState == UnitLifeState.Downed || u.LifeState == UnitLifeState.Stabilized;
                bool dead = u.LifeState == UnitLifeState.Dead;
                if (hpLost > 0 || downed || dead)
                    casualties.Add(new BattleCasualty(u.SourceCompanionId, hpLost, downed, dead));

                if (u.LifeState == UnitLifeState.Active) survivors.Add(u.SourceCompanionId);
            }

            return new BattleResult
            {
                Outcome = MapOutcome(cs.Outcome),
                Rounds = cs.Round,
                Casualties = casualties,
                SurvivingCompanionIds = survivors
            };
        }

        private static BattleOutcome MapOutcome(CombatOutcome outcome)
        {
            switch (outcome)
            {
                case CombatOutcome.Victory: return BattleOutcome.Victory;
                case CombatOutcome.Defeat: return BattleOutcome.Defeat;
                case CombatOutcome.Retreat: return BattleOutcome.Retreat;
                default: return BattleOutcome.Draw; // Draw и любой будущий предохранитель — не «чистая» победа
            }
        }
    }
}
