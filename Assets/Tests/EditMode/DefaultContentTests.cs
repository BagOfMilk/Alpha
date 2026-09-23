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
    /// Заборы на стартовый контент. Проверяется не «красота чисел», а то, что
    /// контент вообще не может выйти за собственные правила: шкалы, равные
    /// бюджеты архетипов и разделение осей в формуле выработки.
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
        /// Равный бюджет — единственное, что делает архетипы сравнимыми.
        /// Без этой проверки перекос вползает по одной цифре за правку.
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
        /// Профильный архетип обязан быть лучшим на своём слоте. Ровно это
        /// ломалось, когда боевой стат в шкале 0–100 попадал в формулу рядом со
        /// склонностями: Командир обгонял Разведчика на разведпосту.
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

                // Сравниваем не имена, а скил победителя: при равной выработке
                // «кто именно первый» — вопрос порядка в списке, а вот обгон
                // человеком с меньшим ремеслом — всегда ошибка контента.
                Assert.AreEqual(bestSkill, best.Skill(slot.PrimarySkill),
                    $"«{slot.DisplayName}»: лучшим оказался {best.DisplayName} с профильным скилом " +
                    $"{best.Skill(slot.PrimarySkill)}, хотя в ростере есть {bestSkill}");
            }
        }

        /// <summary>
        /// Несущее правило экономики (Э6.2): город не производит материалов
        /// нигде и никогда. Проверка стоит на КОНТЕНТЕ, а не на памяти: слот
        /// с выходом Materials пройти незамеченным не может.
        ///
        /// Золото под запрет не попадает — Поправка №4.1: это валюта, и Рынок
        /// по US-7.1 её производит.
        /// </summary>
        [Test]
        public void NoCitySlot_ProducesMaterials()
        {
            foreach (var slot in DefaultContent.AllSlots())
                Assert.AreNotEqual(ResourceType.Materials, slot.OutputResource,
                    $"«{slot.DisplayName}» печатает материалы в городе, а Э6.2 это запрещает");
        }

        /// <summary>
        /// Город производит ровно две вещи: еду и золото (Поправка №4 и №4.1).
        /// Список закрыт — третий городской кран должен ломать этот тест, а не
        /// тихо появляться в контенте.
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
        /// Слот берёт скил как основу и атрибут как добавку — разными типами.
        /// Проверка держит правило на контенте: перепутать их местами нельзя,
        /// но можно забыть задать вовсе, и тогда слот тихо работает от базы.
        /// </summary>
        [Test]
        public void EveryProducingSlot_DeclaresBothAxes()
        {
            foreach (var slot in DefaultContent.AllSlots())
            {
                Assert.AreNotEqual(SkillType.None, slot.PrimarySkill, $"«{slot.DisplayName}»: нет профильного скила");
                Assert.AreNotEqual(AttributeType.None, slot.SecondaryAttribute, $"«{slot.DisplayName}»: нет вторичного атрибута");

                // Слот без выхода обязан объявить это явно, а не оставить
                // OutputResource = None при OutputKind = Resource: иначе
                // «ничего не производит» и «забыли проставить ресурс»
                // выглядят одинаково.
                if (slot.OutputKind == SlotOutputKind.Resource)
                    Assert.AreNotEqual(ResourceType.None, slot.OutputResource,
                        $"«{slot.DisplayName}»: Resource без ресурса — нужен SlotOutputKind.None");
            }
        }

        /// <summary>
        /// Трейты и шрамы меняют выработку, хотя в ProductionCalculator про них
        /// нет ни строки: US-8.1 закрывается тем, что формула читает снапшот.
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
