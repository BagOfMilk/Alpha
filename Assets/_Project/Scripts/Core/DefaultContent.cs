using System.Collections.Generic;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Economy;
using Game.Core.Stats;

namespace Game.Core
{
    /// <summary>
    /// Дефолтний контент у коді: архетипи напарників і слоти бази. Це «затравка»
    /// для прототипу — пізніше її замінять/доповнять ScriptableObject-асети, але
    /// завдяки цьому проєкт грайбельний одразу, без ручного налаштування в редакторі.
    ///
    /// Бюджети стартових архетипів однакові: 18 очок атрибутів і 12 очок
    /// скілів на кожного. Однаковий бюджет — єдиний спосіб порівнювати
    /// архетипи між собою, не перераховуючи їх щоразу вручну.
    /// </summary>
    public static class DefaultContent
    {
        // ---- Архетипи напарників ----

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

        // Торгівля в Командира не для краси: ринок і погрузочний док стоять на
        // ній, і без неї в стартовому ростері на цих позиціях профілю нема взагалі —
        // найкращим торговцем виявлявся той, у кого просто вища Кмітливість.
        public static CompanionArchetype Leader()
        {
            var a = new CompanionArchetype("leader", "Командир");
            a.SetAttribute(AttributeType.Strength, 4);
            a.SetAttribute(AttributeType.Agility, 4);
            a.SetAttribute(AttributeType.Wits, 5);
            a.SetAttribute(AttributeType.Will, 5);
            a.SetSkill(SkillType.Persuade, 5);
            a.SetSkill(SkillType.Trade, 4);
            a.SetSkill(SkillType.Tactics, 2);
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

        // ---- Слоти бази ----
        //
        // Первинне — скіл, вторинне — атрибут: ремесло і природна сторона
        // однієї й тієї самої роботи. Коефіцієнти незмінні, змінилися лише осі,
        // тому виробіток можна порівнювати з числами ітерації 1.

        public static List<AssignmentSlotDefinition> AllSlots()
        {
            var slots = new List<AssignmentSlotDefinition>();

            // Рада — пасивний бонус моралі: хто вміє говорити з людьми.
            slots.Add(new AssignmentSlotDefinition("council_seat", "Место в совете", BaseSectionType.Council)
            {
                OutputKind = SlotOutputKind.Passive,
                PassiveBonusId = "Morale",
                PrimarySkill = SkillType.Persuade,
                SecondaryAttribute = AttributeType.Will,
                BaseOutput = 2, OutputPerPrimaryPoint = 1.0, OutputPerSecondaryPoint = 0.5
            });

            // Ферми — їжа. Землю годують ті, хто вміє її читати.
            slots.Add(new AssignmentSlotDefinition("settlement_farms", "Фермы поселения", BaseSectionType.Settlement)
            {
                OutputKind = SlotOutputKind.Resource, OutputResource = ResourceType.Food,
                PrimarySkill = SkillType.Survival, SecondaryAttribute = AttributeType.Will,
                BaseOutput = 6, OutputPerPrimaryPoint = 1.0, OutputPerSecondaryPoint = 0.5
            });
            slots.Add(new AssignmentSlotDefinition("settlement_market", "Рынок поселения", BaseSectionType.Settlement)
            {
                OutputKind = SlotOutputKind.Resource, OutputResource = ResourceType.Gold,
                PrimarySkill = SkillType.Trade, SecondaryAttribute = AttributeType.Wits,
                BaseOutput = 4, OutputPerPrimaryPoint = 1.0, OutputPerSecondaryPoint = 0.5
            });

            // Майстерня — крафт. Матеріалів НЕ виробляє: за Е6.2 міського
            // виробництва матеріалів нема взагалі, джерело лише вилазки. Поки
            // крафт не написано, позиція дає рольовий досвід і нічого більше —
            // це чесніше, ніж друкувати компонент, який місто друкувати не
            // вправі.
            slots.Add(new AssignmentSlotDefinition("workshop_bench", "Верстак мастерской", BaseSectionType.Workshop)
            {
                OutputKind = SlotOutputKind.None,
                PrimarySkill = SkillType.Mechanics, SecondaryAttribute = AttributeType.Wits,
                BaseOutput = 3, OutputPerPrimaryPoint = 1.2, OutputPerSecondaryPoint = 0.4
            });

            // Лабораторія — крафт аугмента (US-6.4, позначений [ПІЗНІШЕ]). Очок
            // досліджень у GDD нема жодної згадки, тому вихід знято.
            // Ремесло тут механіка: скіла «наука» у GDD теж нема, і вигадувати
            // його під один слот дорожче, ніж визнати лабораторію дещо тоншою
            // майстернею.
            slots.Add(new AssignmentSlotDefinition("lab_station", "Исследовательский стол", BaseSectionType.Laboratory)
            {
                OutputKind = SlotOutputKind.None,
                PrimarySkill = SkillType.Mechanics, SecondaryAttribute = AttributeType.Wits,
                BaseOutput = 2, OutputPerPrimaryPoint = 1.3, OutputPerSecondaryPoint = 0.3
            });

            // Лазарет — лікування (Healing).
            slots.Add(new AssignmentSlotDefinition("infirmary_bed", "Койка лазарета", BaseSectionType.Infirmary)
            {
                OutputKind = SlotOutputKind.Healing,
                PrimarySkill = SkillType.Medicine, SecondaryAttribute = AttributeType.Wits,
                BaseOutput = 4, OutputPerPrimaryPoint = 2.0, OutputPerSecondaryPoint = 0.5
            });

            // Склад — припаси (спочатку закритий, відкривається за ресурси).
            slots.Add(new AssignmentSlotDefinition("storehouse_dock", "Погрузочный док", BaseSectionType.Storehouse)
            {
                OutputKind = SlotOutputKind.Resource, OutputResource = ResourceType.Gold,
                PrimarySkill = SkillType.Trade, SecondaryAttribute = AttributeType.Strength,
                BaseOutput = 3, OutputPerPrimaryPoint = 1.0, OutputPerSecondaryPoint = 0.4,
                UnlockedByDefault = false,
                // Ціна побудови. Док — не саме будівля Складу, а її апгрейд,
                // тому за US-7.2 він гейтиться будівельним компонентом, а не
                // самим золотом, як ядра-будівлі. Поки це єдиний злив
                // матеріалів — до появи будівництва і крафту (Е6, Е7).
                UnlockCost = new Dictionary<ResourceType, int>
                {
                    { ResourceType.Gold, 40 },
                    { ResourceType.Materials, 25 }
                }
            });

            // Розвідпост. Інтела як ресурсу в GDD нема, вихід знято. Сам слот
            // НЕ видаляється: його id — RelevantPositionId двох інцидентів, і за
            // ним детерміновано вибирається жертва (IncidentResolver). Прибрати
            // слот означає мовчки відкотити вибір жертви на «перший за алфавітом».
            slots.Add(new AssignmentSlotDefinition("scouting_post", "Разведпост", BaseSectionType.ScoutingPost)
            {
                OutputKind = SlotOutputKind.None,
                PrimarySkill = SkillType.Survival, SecondaryAttribute = AttributeType.Wits,
                BaseOutput = 1, OutputPerPrimaryPoint = 1.0, OutputPerSecondaryPoint = 0.2
            });

            return slots;
        }
    }
}
