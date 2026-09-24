using System;
using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Characters;
using Game.Core.Randomness;

namespace Game.Core.Combat
{
    /// <summary>
    /// Источник боевого юнита игрока: напарник + оружие (Combat не читает
    /// Companion.Equipment — Items/Б3 в этом рабочем дереве не существует) +
    /// признак сюжетной защиты от смерти (UnitProfile.ProtectedFromDeath).
    /// Вызывающий (D1) резолвит это из Roster/GameSession; Combat — нет.
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
    /// Собирает готовый CombatState из BattleSetup — единственная точка, где
    /// данные (id напарника/id EnemyDefinition) превращаются в живые
    /// CombatUnit. Комбат этим не трогает Roster/BaseState напрямую: id
    /// резолвит вызывающий через переданные делегаты (§1.1 — «дункан просит
    /// бій по даним»), поэтому Combat тестируется без единого другого пакета.
    /// </summary>
    public static class CombatBattleBuilder
    {
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
