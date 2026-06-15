using System.Text;
using Game.Core;
using Game.Core.Balance;
using Game.Core.Combat;
using Game.Core.Economy;
using Game.Core.Items;
using Game.Core.Stats;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Лут / гир / крафт (Эпик 6) в консоли. Повесь на пустой GameObject, Play.
    /// Показывает: дроп с сидом (редкость множит роллы) → экипировка → изменение
    /// производных через единый агрегатор → бой с уникальным проком именной пушки →
    /// крафт-апгрейд за крафтовый компонент. Сид фиксирован — прогон воспроизводим.
    /// </summary>
    public sealed class ItemsDemo : MonoBehaviour
    {
        [Tooltip("Необязательно. Если пусто — дефолтный баланс из кода.")]
        public BalanceConfigAsset balanceAsset;
        public int seed = 42;
        public bool runOnStart = true;

        private void Start()
        {
            if (runOnStart) RunItemsLoop();
        }

        [ContextMenu("Run Items Loop")]
        public void RunItemsLoop()
        {
            var cfg = balanceAsset != null ? balanceAsset.ToConfig() : new BalanceConfig();
            var rng = new SeededRng(seed);

            // --- ДРОП: пять предметов из таблицы (редкость множит магнитуду роллов) ---
            var table = DefaultItems.DropTable();
            var drops = new StringBuilder("=== ДРОП (сид " + seed + ") ===\n");
            for (int i = 0; i < 5; i++)
            {
                var it = LootGenerator.Roll(table, rng);
                drops.AppendLine($"  {it.DisplayName} [{it.Rarity}]: {DescribeMods(it)}");
            }
            Debug.Log(drops.ToString());

            // --- ЭКИПИРОВКА: производные до/после через единый агрегатор ---
            var hero = DefaultContent.Marksman().CreateInstance("marksman", cfg);
            Debug.Log($"=== {hero.DisplayName}: до экипировки ===\n  {DerivedLine(hero, cfg)}");

            hero.Equipment.Equip(new ItemInstance(DefaultItems.ArmorVest(), Rarity.Rare, rng));
            hero.Equipment.Equip(ItemInstance.NamedFrom(DefaultItems.Widowmaker())); // именная винтовка
            Debug.Log($"=== после: Эгида-бронежилет + «Вдоводел» ===\n  {DerivedLine(hero, cfg)}\n" +
                      $"  Оружие: {hero.Equipment.EquippedWeapon.DisplayName} (прок: {hero.Equipment.EquippedWeapon.StatusOnHit})");

            // --- БОЙ: уникальный прок именной пушки (Кровотечение) ---
            var map = new GridMap(8, 1);
            var cs = new CombatState(map, cfg, new SeededRng(seed));
            var heroUnit = CombatUnit.FromCompanion(hero, hero.Equipment.EquippedWeapon, cfg);
            var enemy = CombatUnit.FromEnemy(DefaultContent.ScavGunner(), "e_gunner");
            cs.AddUnit(heroUnit, new GridPos(0, 0));
            cs.AddUnit(enemy, new GridPos(5, 0));
            cs.Begin();
            int guard = 8;
            while (cs.Outcome == CombatOutcome.Ongoing && guard-- > 0)
            {
                if (cs.Current == heroUnit && cs.Current.IsActive) { cs.Attack("e_gunner"); cs.EndTurn(); }
                else cs.EndTurn();
                if (enemy.HasStatus(StatusType.Bleeding)) break;
            }
            var log = new StringBuilder("=== БОЙ (прок именной винтовки) ===\n");
            foreach (var line in cs.Log) log.AppendLine("  " + line);
            Debug.Log(log.ToString());

            // --- КРАФТ: апгрейд обычного предмета за крафтовый компонент ---
            var ledger = new ResourceLedger();
            ledger.Add(ResourceType.CraftingMaterial, 10);
            var scope = new ItemInstance(DefaultItems.TargetingScope(), Rarity.Common, new SeededRng(seed));
            var before = DescribeMods(scope);
            var res = CraftSystem.TryUpgrade(scope, ledger, craftingCost: 3, new SeededRng(seed + 1));
            Debug.Log($"=== КРАФТ ===\n  {scope.DisplayName}: {before}  →  [{res}] {scope.Rarity}: {DescribeMods(scope)}\n" +
                      $"  Крафтовый компонент: {ledger.Get(ResourceType.CraftingMaterial)}");
        }

        private static string DescribeMods(ItemInstance item)
        {
            var sb = new StringBuilder();
            foreach (var m in item.Modifiers()) sb.Append($"{m.Stat} +{(int)m.Value}  ");
            return sb.Length > 0 ? sb.ToString().TrimEnd() : "(без статов)";
        }

        private static string DerivedLine(Game.Core.Characters.Companion c, BalanceConfig cfg)
        {
            var d = c.EffectiveDerived(cfg);
            return $"HP {d[DerivedStat.MaxHp]} · Точность {d[DerivedStat.Accuracy]} · " +
                   $"Броня {d[DerivedStat.Armor]} · Крит {d[DerivedStat.CritChance]}%";
        }
    }
}
