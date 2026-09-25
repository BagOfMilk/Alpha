using System;
using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Characters;
using Game.Core.Randomness;

namespace Game.Core.Combat
{
    /// <summary>
    /// Джерело бойового юніта гравця: напарник + зброя (Combat не читає
    /// Companion.Equipment — Items/Б3 у цьому робочому дереві не існує) +
    /// ознака сюжетного захисту від смерті (UnitProfile.ProtectedFromDeath).
    /// Викликач (D1) резолвить це з Roster/GameSession; Combat — ні.
    /// </summary>
    public sealed class PlayerUnitSource
    {
        public Companion Companion;
        public WeaponDefinition Weapon;
        public bool ProtectedFromDeath;

        public PlayerUnitSource(Companion companion, WeaponDefinition weapon, bool protectedFromDeath = false)
        {
            Companion = companion;
            Weapon = weapon;
            ProtectedFromDeath = protectedFromDeath;
        }
    }

    /// <summary>
    /// Збирає готовий CombatState із BattleSetup — єдина точка, де
    /// дані (id напарника/id EnemyDefinition) перетворюються на живі
    /// CombatUnit. Комбат цим не чіпає Roster/BaseState напряму: id
    /// резолвить викликач через передані делегати (§1.1 — «дункан просить
    /// бій по даним»), тому Combat тестується без жодного іншого пакета.
    /// </summary>
    public static class CombatBattleBuilder
    {
        /// <summary>
        /// roller — єдине джерело випадковості на весь бій (R1: Core сам
        /// його не створює і не зберігає сід — BattleSetup сіда не несе). Для
        /// HitRule=Threshold можна передати null (ThresholdRule його не читає);
        /// для HitRule=Percent викликач (D1/GameSession) зобов'язаний передати
        /// готовий IDiceRoller (у проді — Gameplay.Combat.SeededDiceRoller,
        /// побудований із NewGameOptions.Seed) і тримати той самий екземпляр
        /// протягом бою — інакше детермінізм «той самий сід — той самий бій» рветься.
        /// </summary>
        public static CombatState Build(BattleSetup setup, BalanceConfig cfg,
            Func<string, PlayerUnitSource> resolvePlayerUnit,
            Func<string, EnemyDefinition> resolveEnemyDefinition,
            IDiceRoller roller,
            IEnumerable<AbilityDefinition> abilityCatalog = null)
        {
            if (setup == null) throw new ArgumentNullException(nameof(setup));
            if (resolvePlayerUnit == null) throw new ArgumentNullException(nameof(resolvePlayerUnit));
            if (resolveEnemyDefinition == null) throw new ArgumentNullException(nameof(resolveEnemyDefinition));
            cfg = cfg ?? new BalanceConfig();

            var map = new GridMap(setup.Width, setup.Height);
            foreach (var wall in setup.Walls) map.SetWall(wall.Pos);
            foreach (var cover in setup.Cover) map.SetCover(cover.Pos, cover.Side, cover.Cover);

            IHitRule rule = setup.HitRule == HitRuleKind.Percent
                ? (IHitRule)new PercentRule(cfg)
                : new ThresholdRule(cfg);
            var cs = new CombatState(map, cfg, rule, roller);

            foreach (var spawn in setup.PlayerUnits)
            {
                var source = resolvePlayerUnit(spawn.CompanionId);
                if (source?.Companion == null) continue;
                var unit = CombatUnit.FromCompanion(source.Companion, source.Weapon, cfg,
                    source.ProtectedFromDeath, abilityCatalog);
                cs.AddUnit(unit, spawn.Pos);
            }

            if (!string.IsNullOrEmpty(setup.DefectorCompanionId))
            {
                var source = resolvePlayerUnit(setup.DefectorCompanionId);
                if (source?.Companion != null)
                {
                    var unit = CombatUnit.FromDefector(source.Companion, source.Weapon, cfg, abilityCatalog);
                    cs.AddUnit(unit, setup.DefectorPos);
                }
            }

            int enemySeq = 0;
            foreach (var spawn in setup.EnemyUnits)
            {
                var def = resolveEnemyDefinition(spawn.EnemyDefinitionId);
                if (def == null) continue;
                var unit = CombatUnit.FromEnemy(def, def.Id + "#" + enemySeq++);
                cs.AddUnit(unit, spawn.Pos);
            }

            cs.Begin();
            return cs;
        }
    }
}
