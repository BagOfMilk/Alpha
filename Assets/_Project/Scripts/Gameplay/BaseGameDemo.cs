using System.Text;
using Game.Core;
using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Economy;
using Game.Core.Loop;
using Game.Core.Pressure;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Грайбельна демонстрація модуля «База і напарники» без UI: повісь на
    /// порожній GameObject у сцені, натисни Play — у консоль виведеться розстановка
    /// напарників по слотах і звіт по кожному циклу (день за днем).
    ///
    /// Це «виконувана специфікація» механіки: видно, як стати напарників
    /// перетворюються на ресурси, як капає рольовий досвід і йдуть рівні. Зручно
    /// звіряти баланс на око, перш ніж будувати інтерфейс.
    /// </summary>
    public sealed class BaseGameDemo : MonoBehaviour
    {
        [Tooltip("Необязательно. Если пусто — берутся дефолтные числа баланса из кода.")]
        public BalanceConfigAsset balanceAsset;

        [Min(1)] public int cyclesToSimulate = 10;
        public bool runOnStart = true;

        private SettlementCycle _cycle;

        private void Start()
        {
            if (runOnStart) RunSimulation();
        }

        [ContextMenu("Run Simulation")]
        public void RunSimulation()
        {
            // Ассет із числами балансу, якщо він покладений в інспектор; інакше дефолти з коду.
            var balance = balanceAsset != null ? balanceAsset.ToConfig() : new BalanceConfig();
            var state = BuildBase(balance, out var assignments);

            // Час рухає тільки конвеєр: покликати виробництво в обхід нього
            // більше не можна — AdvanceCycle закритий для цієї збірки.
            var production = new ProductionStep(state);
            var processor = new DayProcessor(new TensionState(balance.Tension), balance,
                SettlementCycle.BuildSteps(production));
            _cycle = new SettlementCycle(state, processor, production);

            var sb = new StringBuilder();
            sb.AppendLine("=== СТАРТОВАЯ РАССТАНОВКА ===");
            foreach (var (companionId, slotId) in assignments)
            {
                var c = state.Roster.Get(companionId);
                var slot = state.GetSlot(slotId);
                sb.AppendLine($"  {c.DisplayName} (ур.{c.Level}) → {slot.Definition.DisplayName}");
            }
            Debug.Log(sb.ToString());

            for (int i = 0; i < cyclesToSimulate; i++)
            {
                _cycle.AdvanceDay();
                Debug.Log(FormatReport(_cycle.Production.LastReport));
            }

            Debug.Log(FormatWallet(state.Resources));
        }

        /// <summary>Збирає базу з дефолтного контенту на дефолтних числах балансу.</summary>
        public static BaseState BuildBase(out System.Collections.Generic.List<(string, string)> assignments)
        {
            return BuildBase(new BalanceConfig(), out assignments);
        }

        /// <summary>
        /// Збирає базу з дефолтного контенту на переданих числах балансу і робить
        /// розумну розстановку. Числа приходять ззовні, щоб їх можна було крутити
        /// ассетом <see cref="BalanceConfigAsset"/>, не чіпаючи код.
        /// </summary>
        public static BaseState BuildBase(BalanceConfig balance, out System.Collections.Generic.List<(string, string)> assignments)
        {
            if (balance == null) balance = new BalanceConfig();
            var roster = new Roster();
            var ledger = new ResourceLedger();
            var state = new BaseState(roster, ledger, balance);

            foreach (var slotDef in DefaultContent.AllSlots())
                state.AddSlot(slotDef);

            // По одному напарнику кожного архетипу.
            int n = 1;
            foreach (var arch in DefaultContent.AllArchetypes())
                roster.Add(arch.CreateInstance($"{arch.Id}_{n++}"));

            // Стартовий запас їжі, щоб поселення не голодувало з першого дня.
            ledger.Add(ResourceType.Food, 20);

            // Розставляємо людей за їхніми сильними сторонами.
            assignments = new System.Collections.Generic.List<(string, string)>
            {
                ("leader_6", "council_seat"),
                ("engineer_2", "workshop_bench"),
                ("scientist_3", "lab_station"),
                ("medic_4", "infirmary_bed"),
                ("scout_5", "scouting_post"),
                ("soldier_1", "settlement_farms"),
            };
            foreach (var (companionId, slotId) in assignments)
                state.TryAssign(companionId, slotId);

            return state;
        }

        private static string FormatReport(CycleReport r)
        {
            var sb = new StringBuilder();
            if (r == null) return "— день прошёл без производства";
            sb.Append($"— День {r.Cycle}: ");
            if (r.Produced.Count == 0 && r.PassiveBonuses.Count == 0)
                sb.Append("ничего не произведено");
            foreach (var kv in r.Produced)
                sb.Append($"+{kv.Value} {kv.Key}  ");
            foreach (var kv in r.PassiveBonuses)
                sb.Append($"[{kv.Key} +{kv.Value}]  ");
            if (r.LeveledUp.Count > 0)
                sb.Append($"| LVL UP: {string.Join(", ", r.LeveledUp)} ");
            if (r.FoodShortage)
                sb.Append("| ⚠ НЕХВАТКА ЕДЫ ");
            return sb.ToString();
        }

        private static string FormatWallet(ResourceLedger ledger)
        {
            var sb = new StringBuilder("=== ИТОГ ПО РЕСУРСАМ ===\n");
            foreach (ResourceType r in System.Enum.GetValues(typeof(ResourceType)))
            {
                if (r == ResourceType.None) continue;
                sb.AppendLine($"  {r}: {ledger.Get(r)}");
            }
            return sb.ToString();
        }
    }
}
