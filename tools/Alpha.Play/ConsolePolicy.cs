using System;
using System.Collections.Generic;
using Game.Core.Expeditions;
using Game.Core.Loop;
using Game.Core.Session;
using Game.Core.Session.Bots;
using Game.Core.Session.Views;

namespace Alpha.Play
{
    /// <summary>
    /// Ручна гра консолі (§1.1: "Консоль грає бої лише Автобоєм") — той самий
    /// <see cref="IBotPolicy"/>, що й п'ять політик пакету D2, лише питає
    /// гравця замість власного "характеру". <see cref="BotRunner"/> веде обидва
    /// режими (--auto і ручний) ОДНИМ кодом (§1.1 "боти в ядрі" + єдиний водій
    /// для тестів/автопрогону/tools/*) — консоль лише підставляє іншу політику.
    /// Розстановка/вилазка лишаються автоматичними (BotSupport.DefaultAssignments,
    /// обережна Quiet-вилазка) — повноцінний ручний екран цих двох належить
    /// Unity (Фаза E, §5 E1 AssignmentScreen/ExpeditionScreen), не консолі.
    /// </summary>
    internal sealed class ConsolePolicy : IBotPolicy
    {
        public string Name => "Console";

        public IncidentPath ChooseIncidentPath(PendingOfferView offer)
        {
            if (offer?.Options == null || offer.Options.Count == 0)
            {
                Console.Write("  " + (offer?.Kind ?? "рішення") + " — кров'ю? (т/н): ");
                var raw = Console.ReadLine();
                return !string.IsNullOrEmpty(raw) && raw.Trim().StartsWith("т", StringComparison.OrdinalIgnoreCase)
                    ? IncidentPath.Bloody : IncidentPath.Quiet;
            }

            Console.WriteLine();
            Console.WriteLine("  ПОДІЯ: " + offer.TopicId + (offer.IsCrisis ? "   [криза]" : ""));
            for (int i = 0; i < offer.Options.Count; i++)
            {
                var o = offer.Options[i];
                string who = o.HasCandidate ? o.BestActorId : "нема кого";
                Console.WriteLine("   " + (i + 1) + ") " + (o.Path == IncidentPathView.Bloody ? "кров'ю" : "тихо") +
                    ": " + o.SkillKey + " проти " + o.Threshold + ", візьметься " + who + ", очікування — " + o.ExpectedBand);
            }
            Console.Write("  вибір (1.." + offer.Options.Count + "): ");
            var line = Console.ReadLine();
            if (int.TryParse(line, out var choice) && choice >= 1 && choice <= offer.Options.Count)
                return (IncidentPath)(int)offer.Options[choice - 1].Path;
            return IncidentPath.Quiet;
        }

        public int ChooseQuestOption(QuestOfferView offer)
        {
            if (offer?.Options == null || offer.Options.Count == 0) return 0;
            Console.WriteLine("  ПРОПОЗИЦІЯ: " + (offer.QuestId ?? offer.TopicId));
            for (int i = 0; i < offer.Options.Count; i++)
                Console.WriteLine("   " + (i + 1) + ") " + offer.Options[i].TextKey);
            Console.Write("  вибір (1.." + offer.Options.Count + "): ");
            var line = Console.ReadLine();
            if (int.TryParse(line, out var choice) && choice >= 1 && choice <= offer.Options.Count)
                return choice - 1;
            return 0;
        }

        public bool ChoosePatrol(SessionView view)
        {
            Console.Write("  ніч: патрулювати чи спати? (п/с): ");
            var line = Console.ReadLine();
            return !string.IsNullOrEmpty(line) && line.Trim().StartsWith("п", StringComparison.OrdinalIgnoreCase);
        }

        public IReadOnlyDictionary<string, string> ChooseAssignments(RosterView roster, CityView city)
            => BotSupport.DefaultAssignments(roster);

        public ExpeditionChoice? ChooseExpedition(SessionView view)
        {
            return new ExpeditionChoice("outskirts", ExpeditionApproach.Quiet,
                new[] { GameSession.ProtagonistId, "maksym", "myroslava" }, 4);
        }

        public CombatAction ChooseCombatAction(BattleView battle) => new CombatAction(CombatIntent.AttackNearest);

        /// <summary>Завжди true — консоль грає бої лише «Автобоєм» (§1.1).</summary>
        public bool ChooseAutoResolve(BattleView battle) => true;
    }
}
