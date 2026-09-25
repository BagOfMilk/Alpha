using System.Collections.Generic;
using Game.Core;
using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Economy;
using Game.Core.Stats;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Загорожі на стартовий контент. Перевіряється не «краса чисел», а те, що
    /// контент узагалі не може вийти за власні правила: шкали, рівні
    /// бюджети архетипів і розділення осей у формулі виробітку.
    /// </summary>
    public class DefaultContentTests
    {
        private static BalanceConfig Cfg() => new BalanceConfig();

        [Test]
        public void EveryArchetype_StaysInsideScales()
        {
            var cfg = Cfg();
            var problems = new List<string>();
            foreach (var a in DefaultContent.AllArchetypes())
                problems.AddRange(StatScales.Violations(a.Attributes, a.Skills, cfg, a.Id));

            CollectionAssert.IsEmpty(problems, string.Join("; ", problems));
        }

        /// <summary>
        /// Рівний бюджет — єдине, що робить архетипи порівнюваними.
        /// Без цієї перевірки перекіс вповзає по одній цифрі за правку.
        /// </summary>
        [Test]
        public void EveryArchetype_HasTheSameBudget()
        {
            foreach (var a in DefaultContent.AllArchetypes())
            {
                Assert.AreEqual(18, a.Attributes.Total, $"{a.Id}: бюджет атрибутов");
                Assert.AreEqual(12, a.Skills.TotalPoints, $"{a.Id}: бюджет скилов");
            }
        }

        /// <summary>
        /// Профільний архетип зобов'язаний бути найкращим на своєму слоті. Саме це
        /// ламалося, коли бойовий стат у шкалі 0–100 потрапляв у формулу поряд зі
        /// схильностями: Командир обганяв Розвідника на розвідпосту.
        /// </summary>
        [Test]
        public void OnEverySlot_TheProfileArchetypeWins()
        {
            var cfg = Cfg();
            var archetypes = DefaultContent.AllArchetypes();

            foreach (var slot in DefaultContent.AllSlots())
            {
                if (slot.PrimarySkill == SkillType.None) continue;

                Companion best = null;
                double bestOut = double.MinValue;
                int bestSkill = -1;

                foreach (var a in archetypes)
                {
                    var c = a.CreateInstance(a.Id + "_1", cfg);
                    double output = ProductionCalculator.RawOutput(c, slot, cfg);
                    if (output > bestOut) { bestOut = output; best = c; }
                    bestSkill = System.Math.Max(bestSkill, c.Skill(slot.PrimarySkill));
                }

                // Порівнюємо не імена, а скіл переможця: при рівному виробітку
                // «хто саме перший» — питання порядку в списку, а от обгін
                // людиною з меншим ремеслом — завжди помилка контенту.
                Assert.AreEqual(bestSkill, best.Skill(slot.PrimarySkill),
                    $"«{slot.DisplayName}»: лучшим оказался {best.DisplayName} с профильным скилом " +
                    $"{best.Skill(slot.PrimarySkill)}, хотя в ростере есть {bestSkill}");
            }
        }

        /// <summary>
        /// Несуче правило економіки (Е6.2): місто не виробляє матеріалів
        /// ніде і ніколи. Перевірка стоїть на КОНТЕНТІ, а не на пам'яті: слот
        /// з виходом Materials пройти непоміченим не може.
        ///
        /// Золото під заборону не потрапляє — Поправка №4.1: це валюта, і Ринок
        /// за US-7.1 його виробляє.
        /// </summary>
        [Test]
        public void NoCitySlot_ProducesMaterials()
        {
            foreach (var slot in DefaultContent.AllSlots())
                Assert.AreNotEqual(ResourceType.Materials, slot.OutputResource,
                    $"«{slot.DisplayName}» печатает материалы в городе, а Э6.2 это запрещает");
        }

        /// <summary>
        /// Місто виробляє рівно дві речі: їжу і золото (Поправка №4 і №4.1).
        /// Список закритий — третій міський кран має ламати цей тест, а не
        /// тихо з'являтися в контенті.
        /// </summary>
        [Test]
        public void CityProduces_OnlyFoodAndGold()
        {
            var allowed = new[] { ResourceType.Food, ResourceType.Gold };
            foreach (var slot in DefaultContent.AllSlots())
            {
                if (slot.OutputKind != SlotOutputKind.Resource) continue;
                CollectionAssert.Contains(allowed, slot.OutputResource,
                    $"«{slot.DisplayName}» производит {slot.OutputResource} — список городских кранов закрыт");
            }
        }

        /// <summary>
        /// Слот бере скіл як основу і атрибут як додаток — різними типами.
        /// Перевірка тримає правило на контенті: переплутати їх місцями не можна,
        /// але можна забути задати взагалі, і тоді слот тихо працює від бази.
        /// </summary>
        [Test]
        public void EveryProducingSlot_DeclaresBothAxes()
        {
            foreach (var slot in DefaultContent.AllSlots())
            {
                Assert.AreNotEqual(SkillType.None, slot.PrimarySkill, $"«{slot.DisplayName}»: нет профильного скила");
                Assert.AreNotEqual(AttributeType.None, slot.SecondaryAttribute, $"«{slot.DisplayName}»: нет вторичного атрибута");

                // Слот без виходу зобов'язаний оголосити це явно, а не залишити
                // OutputResource = None при OutputKind = Resource: інакше
                // «нічого не виробляє» і «забули проставити ресурс»
                // виглядають однаково.
                if (slot.OutputKind == SlotOutputKind.Resource)
                    Assert.AreNotEqual(ResourceType.None, slot.OutputResource,
                        $"«{slot.DisplayName}»: Resource без ресурса — нужен SlotOutputKind.None");
            }
        }

        /// <summary>
        /// Трейти і шрами змінюють виробіток, хоча в ProductionCalculator про них
        /// немає жодного рядка: US-8.1 закривається тим, що формула читає знімок.
        /// </summary>
        [Test]
        public void TraitsAndScars_MoveProduction_WithoutTouchingTheFormula()
        {
            var cfg = Cfg();
            var slot = DefaultContent.AllSlots().Find(s => s.Id == "workshop_bench");
            var comp = DefaultContent.Engineer().CreateInstance("eng_1", cfg);

            double before = ProductionCalculator.RawOutput(comp, slot, cfg);

            comp.Traits.TryAdd(new Game.Core.Characters.Traits.TraitDefinition("handy", "Рукастый")
                .WithModifier(StatKey.Mechanics, 2));
            double withTrait = ProductionCalculator.RawOutput(comp, slot, cfg);

            comp.Scars.Add(new Game.Core.Characters.Scars.ScarDefinition("shaky", "Дрожь в руках")
                .WithModifier(StatKey.Mechanics, -3));
            double withScar = ProductionCalculator.RawOutput(comp, slot, cfg);

            Assert.AreEqual(before + 2 * slot.OutputPerPrimaryPoint, withTrait, 1e-9);
            Assert.AreEqual(before - 1 * slot.OutputPerPrimaryPoint, withScar, 1e-9);
        }
    }
}
