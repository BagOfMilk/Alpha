using System.Text;
using Game.Core;
using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Combat;
using Game.Core.Economy;
using Game.Core.Threats;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Скрытые шкалы в действии (Эпик 11): фоновый тик Напряжения от тира города,
    /// скрытые дельты «квестовых выборов», инциденты по полосам с резолвом через
    /// позиции (US-8.2: пустая позиция → худший исход), непредотвратимые кризисы,
    /// одноразовый пороговый всплеск и телеграфия БЕЗ чисел (US-17.2) — только
    /// полосы и амбиент-реплики. Повесь на пустой GameObject, нажми Play.
    /// </summary>
    public sealed class ThreatsDemo : MonoBehaviour
    {
        [Tooltip("Необязательно. Если пусто — дефолтный баланс из кода.")]
        public BalanceConfigAsset balanceAsset;

        public int seed = 42;
        public bool runOnStart = true;

        private void Start()
        {
            if (runOnStart) RunThreatsLoop();
        }

        [ContextMenu("Run Threats Loop")]
        public void RunThreatsLoop()
        {
            var cfg = balanceAsset != null ? balanceAsset.ToConfig() : new BalanceConfig();

            // База: переговорщик в совете (Убеждение), техник в мастерской (Механика),
            // а СКЛАД (Выживание) пуст — кражи пойдут по худшему исходу.
            var roster = new Roster();
            var baseState = new BaseState(roster, new ResourceLedger(), cfg);
            foreach (var bg in DefaultContent.AllBackgrounds())
                roster.Add(bg.CreateInstance(bg.Id, cfg));
            roster.Get("leader").IsProtagonist = true;
            foreach (var slot in DefaultContent.AllSlots())
                baseState.AddSlot(slot);
            baseState.TryAssign("negotiator", "council_seat");
            baseState.TryAssign("technician", "workshop_bench");

            var threats = new ThreatSystem(cfg, new SeededRng(seed),
                DefaultContent.IncidentPool(), DefaultContent.TensionSpikes());
            baseState.AttachThreats(threats);

            Debug.Log("=== ГОРОД, тир " + baseState.CityTier + ". Совет и мастерская укомплектованы, склад ПУСТ ===");

            // 1. Спокойная декада: фон тикает еле-еле (мягкое давление времени).
            Report(baseState.AdvanceDays(10), threats, "Спокойная декада");

            // 2. Сюжетные выборы — последствия скрыты (валентность не показывается).
            threats.ApplyHiddenDelta(baseState, 14);
            threats.ApplyHiddenDelta(baseState, 12);
            Debug.Log("Два жёстких квестовых выбора… город отреагирует — но чем именно, заранее не видно.");
            Report(baseState.AdvanceDays(10), threats, "Декада после выборов");

            // 3. Ещё выбор + рост города: фон выше, инциденты чаще и серьёзнее.
            baseState.AdvanceCityTier();
            threats.ApplyHiddenDelta(baseState, 16);
            Report(baseState.AdvanceDays(12), threats, "Город растёт (тир 2), дела хуже");

            // 4. Дожимаем до Critical — где-то здесь сработает порог 75 («Удар по своим»).
            threats.ApplyHiddenDelta(baseState, 18);
            Report(baseState.AdvanceDays(8), threats, "На грани");

            // 5. Исход крупного квеста снизил накал (контригра: Облава/Храм — итерация совета).
            threats.ApplyHiddenDelta(baseState, -25);
            Report(baseState.AdvanceDays(6), threats, "После разрядки");

            // 6. «Готовность» копится к финалу (число тоже скрыто).
            threats.Readiness.AddPreparation();
            threats.Readiness.AddPreparation();
            threats.Readiness.AddFortification();
            Debug.Log($"Подготовка к внешней угрозе: полоса Готовности — {threats.Readiness.Band}");

            var roll = new StringBuilder("=== РОСТЕР ПОСЛЕ СОБЫТИЙ ===\n");
            foreach (var c in roster.All)
                roll.AppendLine($"  {c.DisplayName}: {c.Status}");
            Debug.Log(roll.ToString());
        }

        private static void Report(CycleReport r, ThreatSystem threats, string title)
        {
            var sb = new StringBuilder($"=== {title}: дни {r.FromDay}→{r.ToDay} · обстановка: {r.TensionBand} ===\n");

            var ambient = DefaultContent.AmbientSignals(r.TensionBand);
            sb.AppendLine($"«{ambient[r.ToDay % ambient.Length]}»"); // фоновый сигнал без чисел

            if (r.Incidents.Count == 0) sb.AppendLine("Инцидентов не было.");
            foreach (var inc in r.Incidents)
            {
                sb.Append($"  ⚠ {inc.DisplayName} [{inc.Severity}] — ");
                sb.Append(inc.ResolvedById == null
                    ? "никто не держит пост: худший исход"
                    : $"{inc.ResolvedById}: {(inc.Success ? "разрулил" : "не справился")}");
                if (inc.CrisisApplied == CrisisEffect.PopulationExodus) sb.Append(" · ЛЮДИ УХОДЯТ ИЗ ГОРОДА");
                if (inc.CrisisApplied == CrisisEffect.KillCompanion) sb.Append($" · ПОГИБ: {inc.CrisisVictimId}");
                if (inc.CrisisApplied == CrisisEffect.HostileFaction) sb.Append(" · в городе подняла голову новая группировка");
                sb.AppendLine();
            }
            sb.Append($"Население: {r.Population}");
            Debug.Log(sb.ToString());
        }
    }
}
