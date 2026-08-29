using System.Text;
using Game.Core;
using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Economy;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Играбельная демонстрация модуля «База и напарники» без UI: повесь на
    /// пустой GameObject в сцене, нажми Play — в консоль выведется расстановка
    /// напарников по слотам и отчёт по каждому циклу (день за днём).
    ///
    /// Это «исполняемая спецификация» механики: видно, как статы напарников
    /// превращаются в ресурсы, как капает ролевой опыт и идут уровни. Удобно
    /// сверять баланс на глаз, прежде чем строить интерфейс.
    /// </summary>
    public sealed class BaseGameDemo : MonoBehaviour
    {
        [Tooltip("Необязательно. Если пусто — берутся дефолтные числа баланса из кода.")]
        public BalanceConfigAsset balanceAsset;

        [Min(1)] public int cyclesToSimulate = 10;
        public bool runOnStart = true;

        private BaseState _base;

        private void Start()
        {
            if (runOnStart) RunSimulation();
        }

        [ContextMenu("Run Simulation")]
        public void RunSimulation()
        {
            // Ассет с числами баланса, если он положен в инспектор; иначе дефолты из кода.
            var balance = balanceAsset != null ? balanceAsset.ToConfig() : new BalanceConfig();
            _base = BuildBase(balance, out var assignments);

            var sb = new StringBuilder();
            sb.AppendLine("=== СТАРТОВАЯ РАССТАНОВКА ===");
            foreach (var (companionId, slotId) in assignments)
            {
                var c = _base.Roster.Get(companionId);
                var slot = _base.GetSlot(slotId);
                sb.AppendLine($"  {c.DisplayName} (ур.{c.Level}) → {slot.Definition.DisplayName}");
            }
            Debug.Log(sb.ToString());

            for (int i = 0; i < cyclesToSimulate; i++)
            {
                var report = _base.AdvanceCycle();
                Debug.Log(FormatReport(report));
            }

            Debug.Log(FormatWallet(_base.Resources));
        }

        /// <summary>Собирает базу из дефолтного контента на дефолтных числах баланса.</summary>
        public static BaseState BuildBase(out System.Collections.Generic.List<(string, string)> assignments)
        {
            return BuildBase(new BalanceConfig(), out assignments);
        }

        /// <summary>
        /// Собирает базу из дефолтного контента на переданных числах баланса и делает
        /// разумную расстановку. Числа приходят снаружи, чтобы их можно было крутить
        /// ассетом <see cref="BalanceConfigAsset"/>, не трогая код.
        /// </summary>
        public static BaseState BuildBase(BalanceConfig balance, out System.Collections.Generic.List<(string, string)> assignments)
        {
            if (balance == null) balance = new BalanceConfig();
            var roster = new Roster();
            var ledger = new ResourceLedger();
            var state = new BaseState(roster, ledger, balance);

            foreach (var slotDef in DefaultContent.AllSlots())
                state.AddSlot(slotDef);

            // По одному напарнику каждого архетипа.
            int n = 1;
            foreach (var arch in DefaultContent.AllArchetypes())
                roster.Add(arch.CreateInstance($"{arch.Id}_{n++}"));

            // Стартовый запас еды, чтобы поселение не голодало с первого дня.
            ledger.Add(ResourceType.Food, 20);

            // Расставляем людей по их сильным сторонам.
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
