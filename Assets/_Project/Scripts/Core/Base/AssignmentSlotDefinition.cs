using System;
using System.Collections.Generic;
using Game.Core.Economy;
using Game.Core.Stats;

namespace Game.Core.Base
{
    public enum SlotOutputKind
    {
        Resource = 0, // виробляє ресурс у спільний гаманець
        Healing = 1,  // лікує поранених напарників (лазарет)
        Passive = 2,  // дає іменований пасивний бонус (мораль, знижки тощо)
        None = 3      // позиція є, виходу немає: механіка будівлі ще не написана
    }

    /// <summary>
    /// Опис позиції на базі, на яку можна призначити напарника.
    /// Повністю data-driven: що виробляє, від яких схильностей залежить і з
    /// якими коефіцієнтами. Баланс ролі = ці числа.
    /// </summary>
    [Serializable]
    public sealed class AssignmentSlotDefinition
    {
        public string Id;
        public string DisplayName;
        public BaseSectionType Section;

        public SlotOutputKind OutputKind = SlotOutputKind.Resource;

        /// <summary>Ресурс, який виробляє слот (якщо OutputKind = Resource).</summary>
        public ResourceType OutputResource = ResourceType.None;

        /// <summary>Ключ пасивного бонусу (якщо OutputKind = Passive), напр. "Morale".</summary>
        public string PassiveBonusId;

        // ---- Формула виробітку за цикл ----
        // output = Base + Skill*PerPrimary + Attribute*PerSecondary
        //
        // Первинне — СКІЛ, вторинне — АТРИБУТ, і це різні типи навмисно.
        // Раніше обидва поля були одним enum, і у формулу виробітку прилітав бойовий
        // стат у шкалі 0–100 проти схильності в 0–7: командир обганяв на
        // розвідпосту профільного розвідника. Тепер непорівнянні шкали у
        // формулу просто не проходять — сигнатура не пропустить.

        /// <summary>Ремесло позиції: чому людина навчена (шкала 0–10).</summary>
        public SkillType PrimarySkill = SkillType.None;

        /// <summary>Природна сторона тієї самої роботи (шкала 1–10).</summary>
        public AttributeType SecondaryAttribute = AttributeType.None;
        public double BaseOutput;
        public double OutputPerPrimaryPoint = 1.0;
        public double OutputPerSecondaryPoint = 0.5;

        /// <summary>Чи відкритий слот спочатку (false — потребує будівництва/розблокування).</summary>
        public bool UnlockedByDefault = true;

        /// <summary>
        /// Ціна розблокування для закритих слотів. Порожньо — слот відкрити не можна
        /// (заглушка під майбутню стройку), тому в кожного закритого слота ціна
        /// має бути проставлена явно.
        /// </summary>
        public Dictionary<ResourceType, int> UnlockCost;

        public AssignmentSlotDefinition() { }

        public AssignmentSlotDefinition(string id, string displayName, BaseSectionType section)
        {
            Id = id;
            DisplayName = displayName;
            Section = section;
        }
    }
}
