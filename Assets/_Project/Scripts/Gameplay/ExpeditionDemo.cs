using System.Text;
using Game.Core;
using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Combat;
using Game.Core.Economy;
using Game.Core.Expeditions;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Полная петля Эпика 1 в консоли: ГОРОД (назначения) → отправка отряда
    /// (позиции пустеют, US-8.3) → ПУТЕШЕСТВИЕ (календарь идёт) → БОЙ (срез 4×6) →
    /// ВОЗВРАТ (лут, смерти, ранения/шрамы в ростер, XP) → лечение в Лазарете в днях.
    /// Повесь на пустой GameObject, нажми Play. Сид фиксирован — прогон воспроизводим.
    /// </summary>
    public sealed class ExpeditionDemo : MonoBehaviour
    {
        [Tooltip("Необязательно. Если пусто — дефолтный баланс из кода.")]
        public BalanceConfigAsset balanceAsset;

        public int seed = 42;
        public bool runOnStart = true;

        private void Start()
        {
            if (runOnStart) RunExpeditionLoop();
        }

        [ContextMenu("Run Expedition Loop")]
        public void RunExpeditionLoop()
        {
            var cfg = balanceAsset != null ? balanceAsset.ToConfig() : new BalanceConfig();

            // --- ГОРОД: ростер (лидер — протагонист) и назначения на позиции ---
            var roster = new Roster();
            var ledger = new ResourceLedger();
            var baseState = new BaseState(roster, ledger, cfg);
            foreach (var bg in DefaultContent.AllBackgrounds())
            {
                var companion = bg.CreateInstance(bg.Id, cfg);
                companion.RefreshPerks(DefaultContent.PerkCatalog()); // пассивы по порогам скилов
                roster.Add(companion);
            }
            roster.Get("leader").IsProtagonist = true;
            foreach (var slot in DefaultContent.AllSlots())
                baseState.AddSlot(slot);

            baseState.TryAssign("medic", "infirmary_bed");
            baseState.TryAssign("technician", "workshop_bench");
            baseState.TryAssign("negotiator", "council_seat");
            Debug.Log("=== ГОРОД (день 0) ===\nНа позициях: медик → Лазарет, техник → Мастерская, переговорщик → Совет");

            // --- ОТПРАВКА: медик уходит в отряд — койка Лазарета пустеет ---
            var plan = new ExpeditionPlan("ruins", "Руины водонапорной станции")
            {
                TravelDaysOut = 2, TravelDaysBack = 2,
                RewardGold = 120, RewardBuildingMaterial = 8, RewardCraftingMaterial = 5
            };
            var expedition = new Expedition(baseState, plan, cfg);
            var send = expedition.TrySend(new[] { "marksman", "brawler", "medic", "leader" });
            Debug.Log($"Отправка отряда [{string.Join(", ", expedition.SquadIds)}] → {send}\n" +
                      $"Койка Лазарета занята: {baseState.GetSlot("infirmary_bed").IsOccupied} (медик снят с поста)");

            var travel = expedition.Depart();
            Debug.Log($"=== ПУТЕШЕСТВИЕ: дни {travel.FromDay}→{travel.ToDay} ({plan.DisplayName}) ===");

            // --- БОЙ: отряд экспедиции против дефолтных врагов на общей арене ---
            var cs = new CombatState(CombatDemo.BuildArena(), cfg, new SeededRng(seed));
            var units = expedition.BuildCombatUnits(Armory, DefaultContent.AbilityCatalog());
            for (int i = 0; i < units.Count; i++)
                cs.AddUnit(units[i], CombatDemo.SquadSpawns[i % CombatDemo.SquadSpawns.Length]);
            CombatDemo.AddDefaultEnemies(cs);
            cs.Begin();
            CombatDemo.AutoBattle(cs, cfg, 600);

            var tail = new StringBuilder("=== БОЙ (хвост лога) ===\n");
            int from = Mathf.Max(0, cs.Log.Count - 12);
            for (int i = from; i < cs.Log.Count; i++) tail.AppendLine(cs.Log[i]);
            Debug.Log(tail.ToString());

            // --- ВОЗВРАТ: последствия в ростер + банк лута + дорога домой ---
            var report = expedition.Conclude(cs);
            var sb = new StringBuilder($"=== ВОЗВРАТ: {report.Outcome}, дней в пути {report.TravelDaysTotal} ===\n");
            sb.AppendLine($"Банк: +{report.GoldBanked} золота, +{report.BuildingMaterialBanked} строит., +{report.CraftingMaterialBanked} крафт.");
            foreach (var oc in report.Companions)
            {
                var c = roster.Get(oc.CompanionId);
                sb.Append($"  {c.DisplayName}: ");
                if (oc.Died) { sb.AppendLine("ПОГИБ"); continue; }
                sb.Append(oc.Injury == Game.Core.Health.InjuryTier.None ? "цел" : $"ранение {oc.Injury} ({c.RecoveryDaysRemaining} дн.)");
                if (oc.ScarId != null) sb.Append($", шрам «{oc.ScarId}»");
                sb.AppendLine();
            }
            if (report.LeveledUp.Count > 0) sb.AppendLine($"Уровень подняли: {string.Join(", ", report.LeveledUp)}");
            if (report.RecoveredOnReturn.Count > 0) sb.AppendLine($"Вылечились в дороге: {string.Join(", ", report.RecoveredOnReturn)}");
            if (report.GameOver) sb.AppendLine("!!! GAME OVER: протагонист погиб (айронмен)");
            Debug.Log(sb.ToString());

            // --- ЛЕЧЕНИЕ ДОМА: медик возвращается на койку (если цел) и тянет раненых ---
            baseState.TryAssign("medic", "infirmary_bed");
            var home = baseState.AdvanceDays(6);
            var final = new StringBuilder($"=== ДОМА: дни {home.FromDay}→{home.ToDay} ===\n");
            if (home.Recovered.Count > 0) final.AppendLine($"Вылечились: {string.Join(", ", home.Recovered)}");
            foreach (var c in roster.All)
                final.AppendLine($"  {c.DisplayName}: {c.Status}" +
                                 (c.IsInjured ? $" (ещё {System.Math.Ceiling(c.RecoveryDaysRemaining)} дн.)" : "") +
                                 (c.Scars.Count > 0 ? $", шрамов: {c.Scars.Count}" : ""));
            final.Append($"Ресурсы: {ledger.Get(ResourceType.Gold)} зол., " +
                         $"{ledger.Get(ResourceType.BuildingMaterial)} строит., {ledger.Get(ResourceType.CraftingMaterial)} крафт.");
            Debug.Log(final.ToString());
        }

        /// <summary>Оружейная демо: подбор ствола по бэкграунду.</summary>
        private static WeaponDefinition Armory(Companion c)
        {
            switch (c.Id)
            {
                case "marksman": return DefaultContent.Rifle();
                case "brawler": return DefaultContent.Machete();
                case "medic": return DefaultContent.Pistol();
                case "leader": return DefaultContent.Rifle();
                default: return DefaultContent.Pistol();
            }
        }
    }
}
