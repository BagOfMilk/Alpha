using System;
using System.Collections.Generic;

namespace Game.Core.Combat
{
    /// <summary>Підсумок тактичного бою, як його бачить міський луп.</summary>
    public enum BattleOutcome
    {
        Victory = 0,
        Defeat = 1,
        Retreat = 2,
        Draw = 3
    }

    /// <summary>
    /// Що сталося з одним юнітом загону гравця. SourceCompanionId — єдиний
    /// зв'язок з ростером; сам Combat роль/пост/статус напарника НЕ чіпає (§1.1:
    /// «бій ранить ЛИШЕ через RosterAdapter.Wound», Б7). Для юнітів без
    /// SourceCompanionId (звичайні вороги) запис не створюється — рани рахуються
    /// тільки по стороні гравця.
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
    /// Вихід тактичного бою назад у луп (§2 таблиці «одна гра», рядок 30):
    /// Outcome мапиться на OutcomeBand викликачем (PassVanguardOutcome/DungeonRun/
    /// Finale — усі D1), а не тут — у кожного викликача своя драбина
    /// роз'яснень. BattleResult сам ростер не чіпає і не видає шрамів: це
    /// прямо заборонено пакету Б1, Р5 віддано RosterAdapter.Wound.
    /// </summary>
    /// <summary>Доля того, хто здався (Поправка №14.2).</summary>
    public enum SurrenderFate
    {
        Release = 0,
        Capture = 1,
        Execute = 2
    }

    /// <summary>Ворог, що здався в бою (Поправка №14.2): кого, якого рангу, чи можна переманити.</summary>
    public sealed class SurrenderedEnemy
    {
        public string UnitId;
        public string EnemyDefinitionId;
        public string DisplayName;
        public EnemyRank Rank;
        public bool NeverRecruitable;

        /// <summary>Пощаджений звалений («Милосердя на полі») — одразу полонений, долю не питають.</summary>
        public bool Spared;
    }

    public sealed class BattleResult
    {
        public BattleOutcome Outcome;
        public int Rounds;
        public IReadOnlyList<BattleCasualty> Casualties;

        /// <summary>Id бойових юнітів гравця, які дожили і лишилися на ногах (Active) — для збірки партії назад.</summary>
        public IReadOnlyList<string> SurvivingCompanionIds;

        /// <summary>Вороги, що здалися (Поправка №14.2) — їхню долю вирішують після бою.</summary>
        public IReadOnlyList<SurrenderedEnemy> SurrenderedEnemies = Array.Empty<SurrenderedEnemy>();

        /// <summary>Порядок падіння бійців загону — id напарників (Поправка №14.7: хто впав останнім, той рятується).</summary>
        public IReadOnlyList<string> FallOrderCompanionIds = Array.Empty<string>();

        /// <summary>
        /// Будує BattleResult із завершеного CombatState. Кидає, якщо бій
        /// ще Ongoing — викликач зобов'язаний дочекатися наслідку (Victory/Defeat/
        /// Retreat/Draw), у комбату немає «напівзіграного» результату.
        /// </summary>
        public static BattleResult From(CombatState cs)
        {
            if (cs == null) throw new ArgumentNullException(nameof(cs));
            if (cs.Outcome == CombatOutcome.Ongoing)
                throw new InvalidOperationException("Підсумок бою запитано, поки бій ще триває.");

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

            // Хто здався, і — після перемоги — хто впав, але міг здатися («Милосердя на полі»):
            // бій скінчився, звалений у твоїх руках, його долю вирішуєш на панелі результату.
            var surrendered = new List<SurrenderedEnemy>();
            foreach (var u in cs.Units)
                if (u.Side == Side.Enemy && (u.LifeState == UnitLifeState.Surrendered
                    || (cs.Outcome == CombatOutcome.Victory && u.LifeState == UnitLifeState.Downed && u.Profile.CanSurrender)))
                    surrendered.Add(new SurrenderedEnemy
                    {
                        UnitId = u.Id,
                        EnemyDefinitionId = u.EnemyDefinitionId,
                        DisplayName = u.Profile.DisplayName,
                        Rank = u.Profile.Rank,
                        NeverRecruitable = u.Profile.NeverRecruitable,
                        Spared = u.Spared
                    });

            var fallOrder = new List<string>();
            foreach (var unitId in cs.FallOrder)
            {
                var u = cs.GetUnit(unitId);
                if (u != null && !string.IsNullOrEmpty(u.SourceCompanionId)) fallOrder.Add(u.SourceCompanionId);
            }

            return new BattleResult
            {
                Outcome = MapOutcome(cs.Outcome),
                Rounds = cs.Round,
                Casualties = casualties,
                SurvivingCompanionIds = survivors,
                SurrenderedEnemies = surrendered,
                FallOrderCompanionIds = fallOrder
            };
        }

        private static BattleOutcome MapOutcome(CombatOutcome outcome)
        {
            switch (outcome)
            {
                case CombatOutcome.Victory: return BattleOutcome.Victory;
                case CombatOutcome.Defeat: return BattleOutcome.Defeat;
                case CombatOutcome.Retreat: return BattleOutcome.Retreat;
                default: return BattleOutcome.Draw; // Draw і будь-який майбутній запобіжник — не «чиста» перемога
            }
        }
    }
}
