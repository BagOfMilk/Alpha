using System.Text;
using Game.Core;
using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Checks;
using Game.Core.Economy;
using Game.Core.Health;
using Game.Core.Stats;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Играбельная демонстрация реалайн-фундамента (GDD v6) без UI: повесь на пустой
    /// GameObject в сцене, нажми Play — в консоль выведется лист персонажа (атрибуты →
    /// производные, скилы, трейты), петля времени базы (лечение в днях, стройка,
    /// рост населения), пример шрама от Серьёзного ранения и пара детерминированных
    /// проверок (обычная + соц). Это «исполняемая спецификация» новой модели —
    /// удобно сверять баланс на глаз до интерфейса.
    /// </summary>
    public sealed class BaseGameDemo : MonoBehaviour
    {
        [Tooltip("Необязательно. Если пусто — берутся дефолтные числа баланса из кода.")]
        public BalanceConfigAsset balanceAsset;

        [Min(2)] public int daysToSimulate = 12;
        public bool runOnStart = true;

        private void Start()
        {
            if (runOnStart) RunSimulation();
        }

        [ContextMenu("Run Simulation")]
        public void RunSimulation()
        {
            var cfg = balanceAsset != null ? balanceAsset.ToConfig() : new BalanceConfig();
            var roster = new Roster();
            var ledger = new ResourceLedger();
            var baseState = new BaseState(roster, ledger, cfg);

            foreach (var bg in DefaultContent.AllBackgrounds())
                roster.Add(bg.CreateInstance(bg.Id, cfg));
            foreach (var slot in DefaultContent.AllSlots())
                baseState.AddSlot(slot);

            // Листы персонажа: атрибуты → производные (через единый агрегатор).
            Debug.Log(CharacterSheet(roster.Get("marksman"), cfg));
            Debug.Log(CharacterSheet(roster.Get("medic"), cfg));

            // Расстановка по позициям (по профилю). Медик в Лазарет ускоряет лечение.
            baseState.TryAssign("medic", "infirmary_bed");
            baseState.TryAssign("technician", "workshop_bench");
            baseState.TryAssign("negotiator", "council_seat");

            // Возврат с вылазки: золото + 2 материала (база материалы НЕ производит).
            ledger.Add(ResourceType.Gold, 120);
            ledger.Add(ResourceType.BuildingMaterial, 8);
            ledger.Add(ResourceType.CraftingMaterial, 5);

            // Кто-то вернулся раненым — Серьёзное ранение метит вечным шрамом.
            var brawler = roster.Get("brawler");
            var scar = DefaultContent.OneEye();
            bool scarred = brawler.ApplyInjury(InjuryTier.Serious, cfg, scar);
            Debug.Log($"{brawler.DisplayName}: Серьёзное ранение → {brawler.RecoveryDaysRemaining} дн. восстановления" +
                      $", шрам: {(scarred ? scar.DisplayName : "—")}");

            // Стройка специального здания (Рынок) — по завершении откроет позицию торговца.
            baseState.StartConstruction(new Construction(
                "build_market", "Рынок", BaseSectionType.Market, cfg.ConstructionSmallDays, "market_stall"));

            // Петля времени: лечение в днях, достройка, население.
            var sb = new StringBuilder("=== ПЕТЛЯ ВРЕМЕНИ (по 2 дня) ===\n");
            for (int i = 0; i < daysToSimulate / 2; i++)
            {
                var r = baseState.AdvanceDays(2);
                sb.Append($"Дни {r.FromDay}->{r.ToDay}: население {r.Population}");
                if (r.Recovered.Count > 0) sb.Append($" | вылечились: {string.Join(", ", r.Recovered)}");
                if (r.ConstructionCompleted.Count > 0) sb.Append($" | достроено: {string.Join(", ", r.ConstructionCompleted)}");
                sb.Append('\n');
            }
            Debug.Log(sb.ToString());
            Debug.Log($"Рынок построен → позиция торговца открыта: {baseState.GetSlot("market_stall").Unlocked}");

            // Детерминированные проверки среди присутствующих (лучший релевантный + трейты).
            var present = new[] { roster.Get("technician"), roster.Get("negotiator") };
            var hack = CheckResolver.Resolve(present, SkillType.Hacking, 2);
            Debug.Log($"Проверка Взлом (порог 2): {(hack.Success ? "УСПЕХ" : "ПРОВАЛ")}" +
                      $" — значение {hack.Value}, вывез: {hack.ResolvedById ?? "никто"}");

            var talk = CheckResolver.ResolveSocial(present, CheckApproach.Persuade, 7);
            Debug.Log($"Соц-проверка Убеждение (порог 7): {(talk.Success ? "УСПЕХ" : "ПРОВАЛ")}" +
                      $" — значение {talk.Value}, вывез: {talk.ResolvedById ?? "никто"}");

            Debug.Log(Wallet(ledger));
        }

        private static string CharacterSheet(Companion c, BalanceConfig cfg)
        {
            if (c == null) return "(нет напарника)";
            var sb = new StringBuilder();
            sb.AppendLine($"=== {c.DisplayName} (ур.{c.Level}) ===");
            sb.AppendLine($"Атрибуты: Сила {c.GetAttribute(AttributeType.Strength)}, " +
                          $"Ловкость {c.GetAttribute(AttributeType.Agility)}, " +
                          $"Смекалка {c.GetAttribute(AttributeType.Wits)}, " +
                          $"Воля {c.GetAttribute(AttributeType.Will)}");

            var d = c.EffectiveDerived(cfg);
            sb.AppendLine($"Производные: HP {d[DerivedStat.MaxHp]}, AP {d[DerivedStat.ActionPoints]}, " +
                          $"Точность {d[DerivedStat.Accuracy]}, Защита {d[DerivedStat.Defense]}, " +
                          $"Инициатива {d[DerivedStat.Initiative]}, Крит {d[DerivedStat.CritChance]}%");

            var skills = new StringBuilder();
            foreach (var kv in c.Skills.Levels) skills.Append($"{kv.Key} {kv.Value}  ");
            sb.AppendLine($"Скилы: {(skills.Length == 0 ? "—" : skills.ToString().TrimEnd())}");

            var traits = new StringBuilder();
            foreach (var t in c.Traits.Traits) traits.Append($"[{t.DisplayName}] ");
            sb.Append($"Трейты ({c.Traits.UsedSlots}/{c.Traits.MaxSlots}): {(traits.Length == 0 ? "—" : traits.ToString().TrimEnd())}");
            return sb.ToString();
        }

        private static string Wallet(ResourceLedger ledger)
        {
            var sb = new StringBuilder("=== РЕСУРСЫ ОТРЯДА ===\n");
            sb.AppendLine($"  Золото: {ledger.Get(ResourceType.Gold)}");
            sb.AppendLine($"  Строительный компонент: {ledger.Get(ResourceType.BuildingMaterial)}");
            sb.Append($"  Крафтовый компонент: {ledger.Get(ResourceType.CraftingMaterial)}");
            return sb.ToString();
        }
    }
}
