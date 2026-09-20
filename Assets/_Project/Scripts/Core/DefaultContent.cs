using System.Collections.Generic;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Economy;
using Game.Core.Stats;

namespace Game.Core
{
    /// <summary>
    /// Дефолтный контент в коде: архетипы напарников и слоты базы. Это «затравка»
    /// для прототипа — позже её заменят/дополнят ScriptableObject-ассеты, но
    /// благодаря этому проект играбелен сразу, без ручной настройки в редакторе.
    ///
    /// Бюджеты стартовых архетипов одинаковы: 18 очков атрибутов и 12 очков
    /// скилов на каждого. Одинаковый бюджет — единственный способ сравнивать
    /// архетипы между собой, не пересчитывая их каждый раз вручную.
    /// </summary>
    public static class DefaultContent
    {
        // ---- Архетипы напарников ----

        public static CompanionArchetype Soldier()
        {
            var a = new CompanionArchetype("soldier", "Боец");
            a.SetAttribute(AttributeType.Strength, 6);
            a.SetAttribute(AttributeType.Agility, 5);
            a.SetAttribute(AttributeType.Wits, 3);
            a.SetAttribute(AttributeType.Will, 4);
            a.SetSkill(SkillType.Ranged, 5);
            a.SetSkill(SkillType.Melee, 3);
            a.SetSkill(SkillType.Tactics, 2);
            a.SetSkill(SkillType.Survival, 2);
            a.SetGrowth(SkillType.Ranged, 3);
            a.SetGrowth(SkillType.Melee, 2);
            a.SetGrowth(SkillType.Tactics, 1);
            a.SetGrowth(SkillType.Survival, 1);
            return a;
        }

        public static CompanionArchetype Engineer()
        {
            var a = new CompanionArchetype("engineer", "Инженер");
            a.SetAttribute(AttributeType.Strength, 4);
            a.SetAttribute(AttributeType.Agility, 4);
            a.SetAttribute(AttributeType.Wits, 6);
            a.SetAttribute(AttributeType.Will, 4);
            a.SetSkill(SkillType.Mechanics, 6);
            a.SetSkill(SkillType.Lockpick, 3);
            a.SetSkill(SkillType.Ranged, 2);
            a.SetSkill(SkillType.Survival, 1);
            a.SetGrowth(SkillType.Mechanics, 3);
            a.SetGrowth(SkillType.Lockpick, 2);
            a.SetGrowth(SkillType.Survival, 1);
            return a;
        }

        public static CompanionArchetype Scientist()
        {
            var a = new CompanionArchetype("scientist", "Учёный");
            a.SetAttribute(AttributeType.Strength, 3);
            a.SetAttribute(AttributeType.Agility, 3);
            a.SetAttribute(AttributeType.Wits, 7);
            a.SetAttribute(AttributeType.Will, 5);
            a.SetSkill(SkillType.Mechanics, 4);
            a.SetSkill(SkillType.Medicine, 4);
            a.SetSkill(SkillType.Persuade, 3);
            a.SetSkill(SkillType.Lockpick, 1);
            a.SetGrowth(SkillType.Mechanics, 3);
            a.SetGrowth(SkillType.Medicine, 2);
            a.SetGrowth(SkillType.Persuade, 1);
            return a;
        }

        public static CompanionArchetype Medic()
        {
            var a = new CompanionArchetype("medic", "Медик");
            a.SetAttribute(AttributeType.Strength, 4);
            a.SetAttribute(AttributeType.Agility, 4);
            a.SetAttribute(AttributeType.Wits, 5);
            a.SetAttribute(AttributeType.Will, 5);
            a.SetSkill(SkillType.Medicine, 6);
            a.SetSkill(SkillType.Survival, 3);
            a.SetSkill(SkillType.Persuade, 2);
            a.SetSkill(SkillType.Ranged, 1);
            a.SetGrowth(SkillType.Medicine, 3);
            a.SetGrowth(SkillType.Survival, 2);
            a.SetGrowth(SkillType.Persuade, 1);
            return a;
        }

        public static CompanionArchetype Scout()
        {
            var a = new CompanionArchetype("scout", "Разведчик");
            a.SetAttribute(AttributeType.Strength, 4);
            a.SetAttribute(AttributeType.Agility, 6);
            a.SetAttribute(AttributeType.Wits, 5);
            a.SetAttribute(AttributeType.Will, 3);
            a.SetSkill(SkillType.Survival, 5);
            a.SetSkill(SkillType.Ranged, 3);
            a.SetSkill(SkillType.Lockpick, 2);
            a.SetSkill(SkillType.Melee, 2);
            a.SetGrowth(SkillType.Survival, 3);
            a.SetGrowth(SkillType.Ranged, 2);
            a.SetGrowth(SkillType.Lockpick, 1);
            return a;
        }

        // Торговля у Командира не для красоты: рынок и погрузочный док стоят на
        // ней, и без неё в стартовом ростере на этих позициях профиля нет вовсе —
        // лучшим торговцем оказывался тот, у кого просто выше Смекалка.
        public static CompanionArchetype Leader()
        {
            var a = new CompanionArchetype("leader", "Командир");
            a.SetAttribute(AttributeType.Strength, 4);
            a.SetAttribute(AttributeType.Agility, 4);
            a.SetAttribute(AttributeType.Wits, 5);
            a.SetAttribute(AttributeType.Will, 5);
            a.SetSkill(SkillType.Persuade, 4);
            a.SetSkill(SkillType.Trade, 4);
            a.SetSkill(SkillType.Tactics, 3);
            a.SetSkill(SkillType.Intimidate, 1);
            a.SetGrowth(SkillType.Persuade, 3);
            a.SetGrowth(SkillType.Trade, 2);
            a.SetGrowth(SkillType.Tactics, 1);
            return a;
        }

        public static List<CompanionArchetype> AllArchetypes()
        {
            return new List<CompanionArchetype>
            {
                Soldier(), Engineer(), Scientist(), Medic(), Scout(), Leader()
            };
        }

        // ---- Слоты базы ----
        //
        // Первичное — скил, вторичное — атрибут: ремесло и природная сторона
        // одной и той же работы. Коэффициенты прежние, поменялись только оси,
        // поэтому выработка сопоставима с числами итерации 1.

        public static List<AssignmentSlotDefinition> AllSlots()
        {
            var slots = new List<AssignmentSlotDefinition>();

            // Совет — пассивный бонус морали: кто умеет говорить с людьми.
            slots.Add(new AssignmentSlotDefinition("council_seat", "Место в совете", BaseSectionType.Council)
            {
                OutputKind = SlotOutputKind.Passive,
                PassiveBonusId = "Morale",
                PrimarySkill = SkillType.Persuade,
                SecondaryAttribute = AttributeType.Will,
                BaseOutput = 2, OutputPerPrimaryPoint = 1.0, OutputPerSecondaryPoint = 0.5
            });

            // Фермы — еда. Землю кормят те, кто умеет её читать.
            slots.Add(new AssignmentSlotDefinition("settlement_farms", "Фермы поселения", BaseSectionType.Settlement)
            {
                OutputKind = SlotOutputKind.Resource, OutputResource = ResourceType.Food,
                PrimarySkill = SkillType.Survival, SecondaryAttribute = AttributeType.Will,
                BaseOutput = 6, OutputPerPrimaryPoint = 1.0, OutputPerSecondaryPoint = 0.5
            });
            slots.Add(new AssignmentSlotDefinition("settlement_market", "Рынок поселения", BaseSectionType.Settlement)
            {
                OutputKind = SlotOutputKind.Resource, OutputResource = ResourceType.Supplies,
                PrimarySkill = SkillType.Trade, SecondaryAttribute = AttributeType.Wits,
                BaseOutput = 4, OutputPerPrimaryPoint = 1.0, OutputPerSecondaryPoint = 0.5
            });

            // Мастерская — материалы.
            slots.Add(new AssignmentSlotDefinition("workshop_bench", "Верстак мастерской", BaseSectionType.Workshop)
            {
                OutputKind = SlotOutputKind.Resource, OutputResource = ResourceType.Materials,
                PrimarySkill = SkillType.Mechanics, SecondaryAttribute = AttributeType.Wits,
                BaseOutput = 3, OutputPerPrimaryPoint = 1.2, OutputPerSecondaryPoint = 0.4
            });

            // Лаборатория — исследования. По US-7.1 у лаборатории одна функция,
            // крафт аугмента, поэтому ремесло здесь механика: скила «наука» в
            // GDD нет, и выдумывать его под один слот дороже, чем признать
            // лабораторию мастерской потоньше.
            slots.Add(new AssignmentSlotDefinition("lab_station", "Исследовательский стол", BaseSectionType.Laboratory)
            {
                OutputKind = SlotOutputKind.Resource, OutputResource = ResourceType.Research,
                PrimarySkill = SkillType.Mechanics, SecondaryAttribute = AttributeType.Wits,
                BaseOutput = 2, OutputPerPrimaryPoint = 1.3, OutputPerSecondaryPoint = 0.3
            });

            // Лазарет — лечение (Healing).
            slots.Add(new AssignmentSlotDefinition("infirmary_bed", "Койка лазарета", BaseSectionType.Infirmary)
            {
                OutputKind = SlotOutputKind.Healing,
                PrimarySkill = SkillType.Medicine, SecondaryAttribute = AttributeType.Wits,
                BaseOutput = 4, OutputPerPrimaryPoint = 2.0, OutputPerSecondaryPoint = 0.5
            });

            // Склад — припасы (изначально закрыт, открывается за ресурсы).
            slots.Add(new AssignmentSlotDefinition("storehouse_dock", "Погрузочный док", BaseSectionType.Storehouse)
            {
                OutputKind = SlotOutputKind.Resource, OutputResource = ResourceType.Supplies,
                PrimarySkill = SkillType.Trade, SecondaryAttribute = AttributeType.Strength,
                BaseOutput = 3, OutputPerPrimaryPoint = 1.0, OutputPerSecondaryPoint = 0.4,
                UnlockedByDefault = false,
                // Цена постройки. Пока это единственный слив ресурсов кроме прокорма —
                // до появления полноценной стройки и крафта (Э6, Э7).
                UnlockCost = new Dictionary<ResourceType, int>
                {
                    { ResourceType.Supplies, 40 },
                    { ResourceType.Materials, 25 }
                }
            });

            // Разведпост — интел. Разведка в поле — это выживание, отдельного
            // скила разведки в GDD тоже нет.
            slots.Add(new AssignmentSlotDefinition("scouting_post", "Разведпост", BaseSectionType.ScoutingPost)
            {
                OutputKind = SlotOutputKind.Resource, OutputResource = ResourceType.Intel,
                PrimarySkill = SkillType.Survival, SecondaryAttribute = AttributeType.Wits,
                BaseOutput = 1, OutputPerPrimaryPoint = 1.0, OutputPerSecondaryPoint = 0.2
            });

            return slots;
        }
    }
}
