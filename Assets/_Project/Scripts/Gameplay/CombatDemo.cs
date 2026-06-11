using System.Collections.Generic;
using System.Text;
using Game.Core;
using Game.Core.Balance;
using Game.Core.Combat;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Вертикальный срез боя из шлюза валидации GDD (Прил. В): стычка 4 против ~6,
    /// авто-бой без UI. Повесь на пустой GameObject, нажми Play — в консоль уйдёт
    /// порядок инициативы и полный лог боя (укрытия, граза, криты, Шред, Подавление,
    /// Кровотечение, даун + стабилизация медиком, Strike-метр) с итогом.
    ///
    /// Сид фиксирован — бой воспроизводим: удобно крутить числа в BalanceConfig
    /// и сравнивать исходы одного и того же боя.
    /// </summary>
    public sealed class CombatDemo : MonoBehaviour
    {
        [Tooltip("Необязательно. Если пусто — дефолтный баланс из кода.")]
        public BalanceConfigAsset balanceAsset;

        public int seed = 42;
        [Min(50)] public int actionBudget = 600;
        public bool runOnStart = true;

        private void Start()
        {
            if (runOnStart) RunSkirmish();
        }

        [ContextMenu("Run Skirmish")]
        public void RunSkirmish()
        {
            var cfg = balanceAsset != null ? balanceAsset.ToConfig() : new BalanceConfig();
            var cs = BuildSkirmish(cfg, seed);

            var order = new StringBuilder("=== ПОРЯДОК ИНИЦИАТИВЫ ===\n");
            foreach (var u in cs.TurnOrder)
                order.AppendLine($"  {u.Profile.Initiative,2} · {u.Profile.DisplayName} ({(u.Side == Side.Player ? "отряд" : "враг")})");
            Debug.Log(order.ToString());

            AutoBattle(cs, cfg, actionBudget);

            var log = new StringBuilder("=== ЛОГ БОЯ ===\n");
            foreach (var line in cs.Log) log.AppendLine(line);
            Debug.Log(log.ToString());

            var summary = new StringBuilder($"=== ИТОГ: {cs.Outcome} (раунд {cs.Round}) ===\n");
            foreach (var u in cs.Units)
                summary.AppendLine($"  {u.Profile.DisplayName}: {u.LifeState}, HP {u.Hp}/{u.Profile.MaxHp}");
            Debug.Log(summary.ToString());
        }

        /// <summary>Карта 12×8 с укрытиями и стеной — общая арена демо-стычек.</summary>
        public static GridMap BuildArena()
        {
            var map = new GridMap(12, 8);
            // Сплошная стена в центре — рвёт линии обзора.
            map.SetWall(new GridPos(6, 3));
            map.SetWall(new GridPos(6, 4));
            // Укрытия: у отряда — полу, у врагов — полу и полное.
            map.SetCover(new GridPos(3, 2), Direction.East, CoverType.Half);
            map.SetCover(new GridPos(3, 5), Direction.East, CoverType.Half);
            map.SetCover(new GridPos(8, 2), Direction.West, CoverType.Half);
            map.SetCover(new GridPos(8, 5), Direction.West, CoverType.Full);
            return map;
        }

        /// <summary>Стартовые позиции отряда на левом краю арены.</summary>
        public static readonly GridPos[] SquadSpawns =
        {
            new GridPos(1, 2), new GridPos(1, 3), new GridPos(1, 4), new GridPos(1, 5)
        };

        /// <summary>Враги демо: Танк + 2 Застрельщика + Контролёр + 2 Прорыва (база-4 роли).</summary>
        public static void AddDefaultEnemies(CombatState cs)
        {
            cs.AddUnit(CombatUnit.FromEnemy(DefaultContent.RaiderBruiser(), "e_bruiser"), new GridPos(10, 3));
            cs.AddUnit(CombatUnit.FromEnemy(DefaultContent.ScavGunner(), "e_gunner1"), new GridPos(10, 2));
            cs.AddUnit(CombatUnit.FromEnemy(DefaultContent.ScavGunner(), "e_gunner2"), new GridPos(10, 5));
            cs.AddUnit(CombatUnit.FromEnemy(DefaultContent.RustDrone(), "e_drone"), new GridPos(10, 4));
            cs.AddUnit(CombatUnit.FromEnemy(DefaultContent.FeralGhoul(), "e_ghoul1"), new GridPos(10, 1));
            cs.AddUnit(CombatUnit.FromEnemy(DefaultContent.FeralGhoul(), "e_ghoul2"), new GridPos(10, 6));
        }

        /// <summary>Собирает стычку 4 против 6: свежие напарники из бэкграундов против дефолтных врагов.</summary>
        public static CombatState BuildSkirmish(BalanceConfig cfg, int seed)
        {
            var cs = new CombatState(BuildArena(), cfg, new SeededRng(seed));

            var marksman = DefaultContent.Marksman().CreateInstance("marksman", cfg);
            var brawler = DefaultContent.Brawler().CreateInstance("brawler", cfg);
            var medic = DefaultContent.Medic().CreateInstance("medic", cfg);
            var leader = DefaultContent.Leader().CreateInstance("leader", cfg);
            cs.AddUnit(CombatUnit.FromCompanion(marksman, DefaultContent.Rifle(), cfg), SquadSpawns[0]);
            cs.AddUnit(CombatUnit.FromCompanion(brawler, DefaultContent.Machete(), cfg), SquadSpawns[1]);
            cs.AddUnit(CombatUnit.FromCompanion(medic, DefaultContent.Pistol(), cfg), SquadSpawns[2]);
            cs.AddUnit(CombatUnit.FromCompanion(leader, DefaultContent.Rifle(), cfg), SquadSpawns[3]);

            AddDefaultEnemies(cs);
            cs.Begin();
            return cs;
        }

        /// <summary>
        /// Наивная политика для авто-боя (это драйвер демо, НЕ боевой ИИ): медик
        /// спасает даун-союзника рядом, остальные бьют ближайшего врага или
        /// сближаются. Бюджет действий страхует от вечного боя.
        /// </summary>
        public static void AutoBattle(CombatState cs, BalanceConfig cfg, int budget)
        {
            while (cs.Outcome == CombatOutcome.Ongoing && budget-- > 0)
            {
                var u = cs.Current;
                if (u == null || !u.IsActive) { cs.EndTurn(); continue; }

                // 1. Медик: стабилизировать падшего союзника (рядом — сразу, иначе идти к нему).
                if (u.Profile.MedicineSkill >= 1)
                {
                    var downed = Nearest(cs, u, sameSide: true, state: UnitLifeState.Downed);
                    if (downed != null)
                    {
                        if (GridPos.Chebyshev(u.Pos, downed.Pos) <= 1)
                        {
                            if (cs.Stabilize(downed.Id) == CombatActionResult.Success) continue;
                        }
                        else if (TryStepToward(cs, u, downed.Pos)) continue;
                    }
                }

                // 2. Ближайшая активная цель.
                var target = Nearest(cs, u, sameSide: false, state: UnitLifeState.Active);
                if (target == null) { cs.EndTurn(); continue; }

                // 3. Атака (Strike — если метр полон); иначе сближение.
                bool strike = u.StrikeMeter >= cfg.StrikeGuaranteeAt;
                var atk = cs.Attack(target.Id, strike);
                if (atk == CombatActionResult.Success)
                {
                    if (u.Weapon == null || u.Ap < u.Weapon.ApCost) cs.EndTurn();
                    continue;
                }
                if (atk == CombatActionResult.NotEnoughAp) { cs.EndTurn(); continue; }

                if (!TryStepToward(cs, u, target.Pos)) cs.EndTurn();
            }
        }

        private static CombatUnit Nearest(CombatState cs, CombatUnit from, bool sameSide, UnitLifeState state)
        {
            CombatUnit best = null;
            int bestDist = int.MaxValue;
            foreach (var u in cs.Units)
            {
                if (u == from || u.LifeState != state) continue;
                if (sameSide != (u.Side == from.Side)) continue;
                int d = GridPos.Chebyshev(from.Pos, u.Pos);
                if (d < bestDist) { bestDist = d; best = u; }
            }
            return best;
        }

        /// <summary>Шаг в достижимый тайл, строго сокращающий дистанцию до цели (анти-осцилляция).</summary>
        private static bool TryStepToward(CombatState cs, CombatUnit u, GridPos goal)
        {
            int curDist = GridPos.Chebyshev(u.Pos, goal);
            GridPos? best = null;
            int bestDist = curDist;
            int bestCost = int.MaxValue;
            foreach (var kv in cs.ReachableFor(u))
            {
                int d = GridPos.Chebyshev(kv.Key, goal);
                if (d < bestDist || (d == bestDist && d < curDist && kv.Value < bestCost))
                {
                    best = kv.Key;
                    bestDist = d;
                    bestCost = kv.Value;
                }
            }
            return best.HasValue && cs.Move(best.Value) == CombatActionResult.Success;
        }
    }
}
