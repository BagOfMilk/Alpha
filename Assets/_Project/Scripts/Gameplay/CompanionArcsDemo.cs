using System.Collections.Generic;
using System.Text;
using Game.Core;
using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Companions;
using Game.Core.Economy;
using Game.Core.Factions;
using Game.Core.Quests;
using Game.Core.Threats;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Личные квестлайны напарников (US-9.5) в консоли: глава арки заперта низкой
    /// лояльностью → растёт лояльность → глава открывается → играется через QuestRun
    /// → завершение открывает следующую (за флагом) → завершение арки. И обрыв арки
    /// при гибели напарника. Сид фиксирован.
    /// </summary>
    public sealed class CompanionArcsDemo : MonoBehaviour
    {
        [Tooltip("Необязательно. Если пусто — дефолтный баланс из кода.")]
        public BalanceConfigAsset balanceAsset;
        public bool runOnStart = true;

        private void Start()
        {
            if (runOnStart) RunArcs();
        }

        [ContextMenu("Run Arcs")]
        public void RunArcs()
        {
            var cfg = balanceAsset != null ? balanceAsset.ToConfig() : new BalanceConfig();
            var roster = new Roster();
            foreach (var bg in DefaultContent.AllBackgrounds())
                roster.Add(bg.CreateInstance(bg.Id, cfg));
            var baseState = new BaseState(roster, new ResourceLedger(), cfg);
            var factions = DefaultFactions.NewRegistry();
            var threats = new ThreatSystem(cfg, new Game.Core.Combat.SeededRng(1), DefaultContent.IncidentPool());
            var flags = new HashSet<string>();

            var medic = roster.Get("medic");
            var arc = DefaultArcs.MedicOldDebt();
            var run = new CompanionArcRun(arc, flags);
            var sb = new StringBuilder($"=== АРКА: {arc.Title} ({medic.DisplayName}) ===\n");

            // Лояльность ниже Steady → глава заперта.
            while (medic.LoyaltyBand >= LoyaltyBand.Steady) medic.AdjustLoyalty(-10);
            run.Refresh(medic);
            sb.AppendLine($"Лояльность {medic.LoyaltyBand}: глава 1 — {run.State} (гейт лояльности)");

            // Поднимаем лояльность до Steady → глава открывается.
            while (medic.LoyaltyBand < LoyaltyBand.Steady) medic.AdjustLoyalty(+10);
            run.Refresh(medic);
            sb.AppendLine($"Лояльность {medic.LoyaltyBand}: глава 1 — {run.State}");

            // Глава 1 — играется через QuestRun.
            run.Begin(medic);
            var s1 = PlayChapter(run.CurrentChapter, baseState, cfg, factions, threats, flags, roster);
            sb.AppendLine($"  «{run.CurrentChapter.Quest.Title}» → {s1}");
            run.CompleteChapter();
            sb.AppendLine($"Глава 1 завершена. Глава 2 — {StateAfterRefresh(run, medic)} (нужна полоса Devoted + флаг)");

            // Глава 2 требует Devoted — поднимаем.
            while (medic.LoyaltyBand < LoyaltyBand.Devoted) medic.AdjustLoyalty(+10);
            run.Refresh(medic);
            sb.AppendLine($"Лояльность {medic.LoyaltyBand}: глава 2 — {run.State}");
            run.Begin(medic);
            var s2 = PlayChapter(run.CurrentChapter, baseState, cfg, factions, threats, flags, roster);
            sb.AppendLine($"  «{run.CurrentChapter.Quest.Title}» → {s2}");
            run.CompleteChapter();
            sb.AppendLine($"АРКА: {run.State}. Золото базы: {baseState.Resources.Get(ResourceType.Gold)}");
            Debug.Log(sb.ToString());

            // Обрыв арки при гибели (US-9.5).
            var brawler = roster.Get("brawler");
            var arc2 = new CompanionArc("arc_x", "brawler", "Незаконченное")
                .Chapter(new ArcChapter("ch1", StubQuest()).Loyalty(LoyaltyBand.Wary));
            var run2 = new CompanionArcRun(arc2, new HashSet<string>());
            run2.Refresh(brawler);
            string before = run2.State.ToString();
            brawler.Kill();
            run2.Refresh(brawler);
            Debug.Log($"=== ОБРЫВ ===\n  Арка бойца была {before}; боец погиб → {run2.State} (смерть обрывает арку, US-9.5)");
        }

        private static QuestState PlayChapter(ArcChapter chapter, BaseState baseState, BalanceConfig cfg,
                                              FactionRegistry factions, ThreatSystem threats,
                                              ICollection<string> flags, Roster roster)
        {
            var qr = new QuestRun(chapter.Quest, baseState, cfg, factions, threats, flags);
            int guard = 12;
            while (qr.IsActive && guard-- > 0)
            {
                var stage = qr.Current;
                if (stage == null) break;
                switch (stage.Kind)
                {
                    case QuestStageKind.Check: qr.ResolveCheck(roster.All); break;
                    case QuestStageKind.Choice: qr.Choose(0, roster.All); break;
                    case QuestStageKind.Combat: qr.ResolveCombat(true); break;
                    default: guard = 0; break;
                }
            }
            return qr.State;
        }

        private static string StateAfterRefresh(CompanionArcRun run, Companion c)
        {
            run.Refresh(c);
            return run.State.ToString();
        }

        private static QuestDefinition StubQuest() =>
            new QuestDefinition("stub", "Заглушка", QuestSource.RandomEvent)
                .Stage(QuestStage.OutcomeStage("end", "", true));
    }
}
