using System.Collections.Generic;
using System.Text;
using Game.Core;
using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Combat;
using Game.Core.Economy;
using Game.Core.Factions;
using Game.Core.Quests;
using Game.Core.Threats;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Квесты и сюжетные миссии (Эпики 13–14) в консоли. Прогон «Пропавшего
    /// каравана»: утилитарная проверка → выбор с ВИДИМОЙ рябью лояльности и СКРЫТОЙ
    /// рябью Напряжения → бой → исход с наградой. Потом — телеграф обходимой леталки
    /// (US-13.2) и цепочка дублёров спайна (US-14.1: понёс бит → пал → понёс дублёр).
    /// Сид фиксирован — прогон воспроизводим.
    /// </summary>
    public sealed class QuestsDemo : MonoBehaviour
    {
        [Tooltip("Необязательно. Если пусто — дефолтный баланс из кода.")]
        public BalanceConfigAsset balanceAsset;
        public int seed = 42;
        public bool runOnStart = true;

        private void Start()
        {
            if (runOnStart) RunQuests();
        }

        [ContextMenu("Run Quests")]
        public void RunQuests()
        {
            var cfg = balanceAsset != null ? balanceAsset.ToConfig() : new BalanceConfig();

            // --- Мир: ростер, фракции, скрытые угрозы, флаги ---
            var roster = new Roster();
            foreach (var bg in DefaultContent.AllBackgrounds())
                roster.Add(bg.CreateInstance(bg.Id, cfg));
            roster.Get("leader").IsProtagonist = true;
            var baseState = new BaseState(roster, new ResourceLedger(), cfg);
            var factions = DefaultFactions.NewRegistry(startingInfluence: 1);
            var threats = new ThreatSystem(cfg, new SeededRng(seed), DefaultContent.IncidentPool());
            var flags = new HashSet<string>();

            // --- Журнал: собираем доступные квесты из источников ---
            var log = new QuestLog();
            log.CollectFrom(DefaultQuests.All(), factions, flags, roster.All);
            var avail = new StringBuilder("=== ДОСКА КВЕСТОВ ===\n");
            foreach (var q in log.Available) avail.AppendLine($"  • [{q.Source}] {q.Title}");
            Debug.Log(avail.ToString());

            // ================= ПРОПАВШИЙ КАРАВАН =================
            var def = DefaultQuests.LostCaravan();
            log.Start(def.Id);
            var run = new QuestRun(def, baseState, cfg, factions, threats, flags);
            var sb = new StringBuilder($"=== КВЕСТ: {def.Title} ===\n«{def.GiverFlavor}»\n");

            // 0 — утилитарная проверка «выследить».
            var s0 = run.ResolveCheck(roster.All);
            sb.AppendLine(Line(s0));

            // 1 — выбор: идём силовым путём (видны реакции напарников).
            sb.AppendLine($"Развилка: «{run.Current.Text}»");
            var s1 = run.Choose(1, roster.All); // «Отбить силой»
            sb.AppendLine($"  Выбор: {def.StageAt(1).Options[1].Label}");
            foreach (var line in s1.ReactionLines) sb.AppendLine("   ↳ " + line);
            sb.AppendLine("  (эффект на Напряжение скрыт — узнаешь по последствиям)");

            // 3 — бой ведёт боевое ядро; исход возвращаем в квест.
            if (run.Current != null && run.Current.Kind == QuestStageKind.Combat)
            {
                bool won = RunSkirmish(roster, cfg, seed);
                sb.AppendLine($"  Бой «{run.Current.Text}» → {(won ? "победа" : "поражение")}");
                var s3 = run.ResolveCombat(won);
                foreach (var n in s3.Notes) sb.AppendLine("   • " + n);
            }

            sb.AppendLine($"ИТОГ: {run.State}. Золото базы: {baseState.Resources.Get(ResourceType.Gold)}, " +
                          $"в сташе предметов: {baseState.Inventory.Count}");
            sb.AppendLine($"Фракции: Гарнизон {factions.BandOf(DefaultFactions.Garrison)}, " +
                          $"Вольные {factions.BandOf(DefaultFactions.FreeFolk)}; репутация {factions.RepBand}; " +
                          $"обстановка {threats.Tension.Band}");
            sb.AppendLine($"Лояльность: переговорщик {roster.Get("negotiator").LoyaltyBand}, " +
                          $"боец {roster.Get("brawler").LoyaltyBand}");
            if (flags.Contains("caravan_saved")) sb.AppendLine("Флаг: caravan_saved (спайн это запомнит)");
            Debug.Log(sb.ToString());

            // ================= МИННЫЙ ПРОХОД: честная обходимая леталка =================
            var mp = DefaultQuests.MinedPass();
            var mpRun = new QuestRun(mp, baseState, cfg, factions, threats, flags);
            var mpSb = new StringBuilder($"=== КВЕСТ: {mp.Title} ===\nРазвилка: «{mpRun.Current.Text}»\n");
            // Идём в рискованную ветку, чтобы показать ТЕЛЕГРАФ леталки…
            mpRun.Choose(0, roster.All);
            mpSb.AppendLine($"  Впереди проверка «{mpRun.Current.Id}» — ЛЕТАЛЬНА: {mpRun.Current.Lethal} (порог {mpRun.Current.Threshold}, телеграф заранее)");
            var defuse = mpRun.ResolveCheck(roster.All); // техник с Механикой обычно вывозит
            mpSb.AppendLine("  " + Line(defuse));
            mpSb.AppendLine($"  ИТОГ: {mpRun.State} (леталка была ОБХОДИМА — длинный путь без риска, US-13.2)");
            Debug.Log(mpSb.ToString());

            // ================= ДУБЛЁРЫ СПАЙНА (US-14.1) =================
            var beat = DefaultQuests.Act1Briefing();
            var bSb = new StringBuilder("=== СПАЙН: бит акта 1, цепочка дублёров ===\n");
            var d1 = DelivererChain.Resolve(beat, roster);
            bSb.AppendLine($"  Бит несёт: {Carrier(d1, roster)}");
            roster.Get("leader").Kill(); // протагонист пал…
            var d2 = DelivererChain.Resolve(beat, roster);
            bSb.AppendLine($"  Лидер пал → бит подхватывает: {Carrier(d2, roster)} (understudy, спайн не залочился)");
            Debug.Log(bSb.ToString());
        }

        private static string Line(QuestStepReport r)
        {
            string who = r.ResolvedById ?? "—";
            string verdict = r.CheckSuccess ? "успех" : (r.WasUtility ? "провал (мягкий сетбэк, путь не закрыт)" : "провал");
            return $"Проверка «{r.StageId}»: {verdict} [{r.CheckValue} vs порог {r.Threshold}, лучший: {who}]";
        }

        private static string Carrier(BeatDelivery d, Roster roster)
            => d.IsNarratorFallback ? d.Text : (roster.Get(d.DelivererId)?.DisplayName ?? d.DelivererId);

        /// <summary>Короткая стычка для боевого этапа: 2 напарника против 2 рейдеров.</summary>
        private static bool RunSkirmish(Roster roster, BalanceConfig cfg, int seed)
        {
            var map = new GridMap(8, 3);
            var cs = new CombatState(map, cfg, new SeededRng(seed));
            var abilities = DefaultContent.AbilityCatalog();
            cs.AddUnit(CombatUnit.FromCompanion(roster.Get("marksman"), DefaultContent.Rifle(), cfg, abilities), new GridPos(0, 1));
            cs.AddUnit(CombatUnit.FromCompanion(roster.Get("brawler"), DefaultContent.Machete(), cfg, abilities), new GridPos(0, 0));
            cs.AddUnit(CombatUnit.FromEnemy(DefaultContent.ScavGunner(), "r1"), new GridPos(7, 1));
            cs.AddUnit(CombatUnit.FromEnemy(DefaultContent.ScavGunner(), "r2"), new GridPos(7, 2));
            cs.Begin();
            CombatAi.AutoResolve(cs, 200);
            return cs.Outcome == CombatOutcome.Victory;
        }
    }
}
