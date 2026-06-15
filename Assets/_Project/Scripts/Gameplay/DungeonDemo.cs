using System.Collections.Generic;
using System.Text;
using Game.Core;
using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Combat;
using Game.Core.Dungeons;
using Game.Core.Economy;
using Game.Core.Items;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Данж push-your-luck (Эпик 12) в консоли. Отряд лезет вглубь: бои (через
    /// CombatState), схроны, проверки, события; добыча копится в «незабанкованное».
    /// На заданной глубине отряд экстрактится (банк в город) — или вайп в бою губит
    /// всё незабанкованное. Сид фиксирован — прогон воспроизводим.
    /// </summary>
    public sealed class DungeonDemo : MonoBehaviour
    {
        [Tooltip("Необязательно. Если пусто — дефолтный баланс из кода.")]
        public BalanceConfigAsset balanceAsset;
        public int seed = 42;
        [Min(1)] public int extractAtDepth = 4;
        public bool runOnStart = true;

        private void Start()
        {
            if (runOnStart) RunDungeon();
        }

        [ContextMenu("Run Dungeon")]
        public void RunDungeon()
        {
            var cfg = balanceAsset != null ? balanceAsset.ToConfig() : new BalanceConfig();

            var squad = new List<Companion>
            {
                DefaultContent.Marksman().CreateInstance("marksman", cfg),
                DefaultContent.Brawler().CreateInstance("brawler", cfg),
                DefaultContent.Medic().CreateInstance("medic", cfg),
                DefaultContent.Leader().CreateInstance("leader", cfg)
            };
            var abilities = DefaultContent.AbilityCatalog();

            var ledger = new ResourceLedger();
            var baseState = new BaseState(new Roster(), ledger, cfg);
            var run = DefaultDungeon.NewRun(seed);

            var log = new StringBuilder("=== ДАНЖ (сид " + seed + ") ===\n");
            int guard = 50;
            while (run.Active && guard-- > 0)
            {
                var room = run.CurrentRoom;
                if (room.Type == RoomType.Combat)
                {
                    bool won = RunRoomCombat(room, squad, abilities, cfg, seed + run.Depth);
                    var res = run.ReportCombat(won);
                    log.AppendLine($"  Глуб.{room.Depth} [{room.DisplayName}] враги×{room.Enemies.Count}{(room.Elite ? " ЭЛИТА" : "")} → " +
                                   (won ? $"победа (+{res.GainedGold} зол, +{res.GainedBuilding}стр/+{res.GainedCrafting}крф)" : "ВАЙП"));
                    if (!won) break;
                }
                else
                {
                    var res = run.ResolveRoom(squad);
                    log.AppendLine($"  Глуб.{room.Depth} [{room.DisplayName}] → " +
                                   $"+{res.GainedGold} зол, +{res.ItemsGained} предм." +
                                   (res.CheckResolved ? (res.CheckSuccess ? " (проверка ✓)" : " (проверка ✗)") : "") +
                                   (string.IsNullOrEmpty(res.Note) ? "" : $" [{res.Note}]"));
                }

                if (run.Depth >= extractAtDepth) break;
                run.Push();
            }

            log.AppendLine($"  (угроза к концу: {run.Threat}; незабанковано: {run.UnbankedGold} зол, {run.UnbankedItems.Count} предм.)");

            if (run.Active)
            {
                var rep = run.Extract(baseState);
                log.AppendLine($"=== ЭКСТРАКТ с глубины {rep.DepthReached}: +{rep.Gold} зол, +{rep.Building} стр., " +
                               $"+{rep.Crafting} крф., предметов {rep.Items.Count} ===");
            }
            else
            {
                log.AppendLine($"=== {run.Outcome}: добыча потеряна ===");
            }
            log.AppendLine($"Город: золото {ledger.Get(ResourceType.Gold)}, строит. {ledger.Get(ResourceType.BuildingMaterial)}, " +
                           $"крафт. {ledger.Get(ResourceType.CraftingMaterial)}");
            Debug.Log(log.ToString());
        }

        /// <summary>Один бой комнаты: отряд (свежие HP) против врагов комнаты на мини-арене.</summary>
        private static bool RunRoomCombat(DungeonRoom room, List<Companion> squad,
                                          List<AbilityDefinition> abilities, BalanceConfig cfg, int rngSeed)
        {
            int rows = Mathf.Max(squad.Count, room.Enemies.Count) + 1;
            var map = new GridMap(12, rows);
            var cs = new CombatState(map, cfg, new SeededRng(rngSeed));

            for (int i = 0; i < squad.Count; i++)
                cs.AddUnit(CombatUnit.FromCompanion(squad[i], Armory(squad[i]), cfg, abilities), new GridPos(1, i));
            for (int i = 0; i < room.Enemies.Count; i++)
                cs.AddUnit(CombatUnit.FromEnemy(room.Enemies[i], $"e{i}"), new GridPos(10, i));

            cs.Begin();
            CombatDemo.AutoBattle(cs, cfg, 600);
            return cs.Outcome == CombatOutcome.Victory;
        }

        private static WeaponDefinition Armory(Companion c)
        {
            switch (c.Id)
            {
                case "brawler": return DefaultContent.Machete();
                case "medic": return DefaultContent.Pistol();
                default: return DefaultContent.Rifle();
            }
        }
    }
}
