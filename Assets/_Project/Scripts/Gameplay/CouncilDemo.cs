using System.Collections.Generic;
using System.Text;
using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Combat;
using Game.Core.Council;
using Game.Core.Economy;
using Game.Core.Factions;
using Game.Core.Threats;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Совет + фракции (Эпики 8.4/10) в консоли. Повесь на пустой GameObject, Play.
    /// Показывает, как действия совета бьют по разным системам: Облава гасит
    /// Напряжение, но злит Вольных; Дипломатия тянет фракцию; Подготовка растит
    /// Готовность; Инвестиция капает золото по дням. Наружу — ТОЛЬКО полосы и
    /// реплики, чисел нет (US-17.2). Сид фиксирован — прогон воспроизводим.
    /// </summary>
    public sealed class CouncilDemo : MonoBehaviour
    {
        [Tooltip("Необязательно. Если пусто — дефолтный баланс из кода.")]
        public BalanceConfigAsset balanceAsset;
        public bool runOnStart = true;

        private void Start()
        {
            if (runOnStart) RunCouncilLoop();
        }

        [ContextMenu("Run Council Loop")]
        public void RunCouncilLoop()
        {
            var cfg = balanceAsset != null ? balanceAsset.ToConfig() : new BalanceConfig();

            var ledger = new ResourceLedger();
            ledger.Add(ResourceType.Gold, 200);
            var baseState = new BaseState(new Game.Core.Characters.Roster(), ledger, cfg);

            // Стартовое Напряжение «город на нервах», чтобы Облава была видна по полосе.
            var threats = new ThreatSystem(cfg, new SeededRng(7), new List<IncidentDefinition>(), null, startingTension: 55);
            baseState.AttachThreats(threats);

            var factions = DefaultFactions.NewRegistry(startingInfluence: 6);
            var council = DefaultCouncil.NewCouncil(factions, ledger, threats, baseState);

            Debug.Log("=== СОВЕТ: старт ===\n" + Snapshot(factions, threats, ledger));

            Step(council, factions, threats, ledger, DefaultCouncil.Raid, null,
                "Облава: давим преступность (Напряжение вниз), но Вольные этого не простят");
            Step(council, factions, threats, ledger, DefaultCouncil.Diplomacy, DefaultFactions.FreeFolk,
                "Дипломатия: пробуем задобрить Вольных после облавы");
            Step(council, factions, threats, ledger, DefaultCouncil.Prepare, null,
                "Подготовка к угрозе: укрепляем Готовность к финалу");
            Step(council, factions, threats, ledger, DefaultCouncil.Investment, null,
                "Инвестиция: вложили золото — доход пойдёт по дням");

            // Социальный выбор из квеста: единый эффект по нескольким системам.
            new SocialConsequence()
                .Faction(DefaultFactions.Commune, +6).Reputation(+4).Tension(-6).Influence(+2)
                .Apply(factions, baseState, threats);
            Debug.Log("Квест-выбор «помочь Общине»: рябь сразу по фракциям/репутации/влиянию (и скрыто — по Напряжению)");

            // Прокрутка времени: КД остывают, инвестиция капает золото.
            council.TickDays(8);
            Debug.Log("=== Прошло 8 дней (КД остыли, инвестиция капнула) ===\n" + Snapshot(factions, threats, ledger));
        }

        private static void Step(Council council, FactionRegistry f, ThreatSystem t, ResourceLedger l,
                                 string actionId, string target, string narration)
        {
            var r = council.Execute(actionId, target);
            var sb = new StringBuilder();
            sb.AppendLine($"— {narration}");
            sb.AppendLine($"  Действие «{actionId}» → {r.Result}" + (r.Success ? $" (КД {r.CooldownDays} дн.)" : ""));
            if (r.Success) sb.Append(Snapshot(f, t, l));
            Debug.Log(sb.ToString());
        }

        /// <summary>Качественный срез: полосы, не числа (US-17.2).</summary>
        private static string Snapshot(FactionRegistry f, ThreatSystem t, ResourceLedger l)
        {
            var sb = new StringBuilder();
            sb.Append("  Город: ");
            foreach (var s in f.Standings) sb.Append($"{s.Faction.DisplayName}={s.Band}  ");
            sb.AppendLine();
            sb.AppendLine($"  Репутация: {f.RepBand} | Влияние: {f.Influence} | Золото: {l.Get(ResourceType.Gold)}");
            sb.Append($"  Напряжение: {t.Tension.Band} | Готовность: {t.Readiness.Band}");
            return sb.ToString();
        }
    }
}
