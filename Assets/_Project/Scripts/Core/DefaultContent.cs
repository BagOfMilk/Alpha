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
    /// Здесь же удобно держать стартовые числа баланса на одном экране.
    /// </summary>
    public static class DefaultContent
    {
        // ---- Архетипы напарников ----

        public static CompanionArchetype Soldier()
        {
            var a = new CompanionArchetype("soldier", "Боец");
            a.BaseStats.Set(StatType.Health, 8);
            a.BaseStats.Set(StatType.Aim, 65);
            a.BaseStats.Set(StatType.Mobility, 5);
            a.BaseStats.Set(StatType.Will, 30);
            a.BaseStats.Set(StatType.Survival, 4);
            a.Growth.SetWeight(StatType.Aim, 3);
            a.Growth.SetWeight(StatType.Health, 2);
            a.Growth.SetWeight(StatType.Will, 1);
            a.Growth.SetWeight(StatType.Survival, 1);
            return a;
        }

        public static CompanionArchetype Engineer()
        {
            var a = new CompanionArchetype("engineer", "Инженер");
            a.BaseStats.Set(StatType.Health, 6);
            a.BaseStats.Set(StatType.Aim, 55);
            a.BaseStats.Set(StatType.Engineering, 6);
            a.BaseStats.Set(StatType.Tech, 5);
            a.BaseStats.Set(StatType.Logistics, 3);
            a.Growth.SetWeight(StatType.Engineering, 3);
            a.Growth.SetWeight(StatType.Tech, 2);
            a.Growth.SetWeight(StatType.Logistics, 1);
            return a;
        }

        public static CompanionArchetype Scientist()
        {
            var a = new CompanionArchetype("scientist", "Учёный");
            a.BaseStats.Set(StatType.Health, 5);
            a.BaseStats.Set(StatType.Science, 7);
            a.BaseStats.Set(StatType.Tech, 4);
            a.BaseStats.Set(StatType.Will, 35);
            a.Growth.SetWeight(StatType.Science, 3);
            a.Growth.SetWeight(StatType.Tech, 1);
            a.Growth.SetWeight(StatType.Will, 1);
            return a;
        }

        public static CompanionArchetype Medic()
        {
            var a = new CompanionArchetype("medic", "Медик");
            a.BaseStats.Set(StatType.Health, 6);
            a.BaseStats.Set(StatType.Aim, 50);
            a.BaseStats.Set(StatType.Medicine, 6);
            a.BaseStats.Set(StatType.Survival, 3);
            a.Growth.SetWeight(StatType.Medicine, 3);
            a.Growth.SetWeight(StatType.Survival, 1);
            a.Growth.SetWeight(StatType.Health, 1);
            return a;
        }

        public static CompanionArchetype Scout()
        {
            var a = new CompanionArchetype("scout", "Разведчик");
            a.BaseStats.Set(StatType.Health, 6);
            a.BaseStats.Set(StatType.Aim, 60);
            a.BaseStats.Set(StatType.Mobility, 7);
            a.BaseStats.Set(StatType.Scouting, 6);
            a.BaseStats.Set(StatType.Survival, 5);
            a.Growth.SetWeight(StatType.Scouting, 3);
            a.Growth.SetWeight(StatType.Mobility, 1);
            a.Growth.SetWeight(StatType.Aim, 1);
            return a;
        }

        public static CompanionArchetype Leader()
        {
            var a = new CompanionArchetype("leader", "Командир");
            a.BaseStats.Set(StatType.Health, 7);
            a.BaseStats.Set(StatType.Aim, 60);
            a.BaseStats.Set(StatType.Leadership, 7);
            a.BaseStats.Set(StatType.Charisma, 5);
            a.BaseStats.Set(StatType.Will, 45);
            a.Growth.SetWeight(StatType.Leadership, 3);
            a.Growth.SetWeight(StatType.Charisma, 2);
            a.Growth.SetWeight(StatType.Will, 1);
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

        public static List<AssignmentSlotDefinition> AllSlots()
        {
            var slots = new List<AssignmentSlotDefinition>();

            // Совет — пассивный бонус морали от лидерства.
            slots.Add(new AssignmentSlotDefinition("council_seat", "Место в совете", BaseSectionType.Council)
            {
                OutputKind = SlotOutputKind.Passive,
                PassiveBonusId = "Morale",
                PrimaryAptitude = StatType.Leadership,
                SecondaryAptitude = StatType.Charisma,
                BaseOutput = 2, OutputPerPrimaryPoint = 1.0, OutputPerSecondaryPoint = 0.5
            });

            // Поселение — производит еду и немного припасов через харизму/логистику.
            slots.Add(new AssignmentSlotDefinition("settlement_farms", "Фермы поселения", BaseSectionType.Settlement)
            {
                OutputKind = SlotOutputKind.Resource, OutputResource = ResourceType.Food,
                PrimaryAptitude = StatType.Logistics, SecondaryAptitude = StatType.Charisma,
                BaseOutput = 6, OutputPerPrimaryPoint = 1.0, OutputPerSecondaryPoint = 0.5
            });
            slots.Add(new AssignmentSlotDefinition("settlement_market", "Рынок поселения", BaseSectionType.Settlement)
            {
                OutputKind = SlotOutputKind.Resource, OutputResource = ResourceType.Supplies,
                PrimaryAptitude = StatType.Charisma, SecondaryAptitude = StatType.Logistics,
                BaseOutput = 4, OutputPerPrimaryPoint = 1.0, OutputPerSecondaryPoint = 0.5
            });

            // Мастерская — материалы.
            slots.Add(new AssignmentSlotDefinition("workshop_bench", "Верстак мастерской", BaseSectionType.Workshop)
            {
                OutputKind = SlotOutputKind.Resource, OutputResource = ResourceType.Materials,
                PrimaryAptitude = StatType.Engineering, SecondaryAptitude = StatType.Tech,
                BaseOutput = 3, OutputPerPrimaryPoint = 1.2, OutputPerSecondaryPoint = 0.4
            });

            // Лаборатория — исследования.
            slots.Add(new AssignmentSlotDefinition("lab_station", "Исследовательский стол", BaseSectionType.Laboratory)
            {
                OutputKind = SlotOutputKind.Resource, OutputResource = ResourceType.Research,
                PrimaryAptitude = StatType.Science, SecondaryAptitude = StatType.Tech,
                BaseOutput = 2, OutputPerPrimaryPoint = 1.3, OutputPerSecondaryPoint = 0.3
            });

            // Лазарет — лечение (Healing).
            slots.Add(new AssignmentSlotDefinition("infirmary_bed", "Койка лазарета", BaseSectionType.Infirmary)
            {
                OutputKind = SlotOutputKind.Healing,
                PrimaryAptitude = StatType.Medicine, SecondaryAptitude = StatType.Survival,
                BaseOutput = 4, OutputPerPrimaryPoint = 2.0, OutputPerSecondaryPoint = 0.5
            });

            // Склад — припасы/логистика (изначально закрыт, требует постройки).
            slots.Add(new AssignmentSlotDefinition("storehouse_dock", "Погрузочный док", BaseSectionType.Storehouse)
            {
                OutputKind = SlotOutputKind.Resource, OutputResource = ResourceType.Supplies,
                PrimaryAptitude = StatType.Logistics, SecondaryAptitude = StatType.Engineering,
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

            // Разведпост — интел.
            slots.Add(new AssignmentSlotDefinition("scouting_post", "Разведпост", BaseSectionType.ScoutingPost)
            {
                OutputKind = SlotOutputKind.Resource, OutputResource = ResourceType.Intel,
                PrimaryAptitude = StatType.Scouting, SecondaryAptitude = StatType.Will,
                BaseOutput = 1, OutputPerPrimaryPoint = 1.0, OutputPerSecondaryPoint = 0.2
            });

            return slots;
        }
    }
}
