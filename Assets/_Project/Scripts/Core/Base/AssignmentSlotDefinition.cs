using System;
using System.Collections.Generic;
using Game.Core.Economy;
using Game.Core.Stats;

namespace Game.Core.Base
{
    public enum SlotOutputKind
    {
        Resource = 0, // производит ресурс в общий кошелёк
        Healing = 1,  // лечит раненых напарников (лазарет)
        Passive = 2   // даёт именованный пассивный бонус (мораль, скидки и т.п.)
    }

    /// <summary>
    /// Описание позиции на базе, на которую можно назначить напарника.
    /// Полностью data-driven: что производит, от каких склонностей зависит и с
    /// какими коэффициентами. Баланс роли = эти числа.
    /// </summary>
    [Serializable]
    public sealed class AssignmentSlotDefinition
    {
        public string Id;
        public string DisplayName;
        public BaseSectionType Section;

        public SlotOutputKind OutputKind = SlotOutputKind.Resource;

        /// <summary>Ресурс, который производит слот (если OutputKind = Resource).</summary>
        public ResourceType OutputResource = ResourceType.None;

        /// <summary>Ключ пассивного бонуса (если OutputKind = Passive), напр. "Morale".</summary>
        public string PassiveBonusId;

        // ---- Формула выработки за цикл ----
        // output = Base + Skill*PerPrimary + Attribute*PerSecondary
        //
        // Первичное — СКИЛ, вторичное — АТРИБУТ, и это разные типы намеренно.
        // Раньше оба поля были одним enum, и в формулу выработки прилетал боевой
        // стат в шкале 0–100 против склонности в 0–7: командир обгонял на
        // разведпосту профильного разведчика. Теперь несопоставимые шкалы в
        // формулу просто не проходят — сигнатура не пропустит.

        /// <summary>Ремесло позиции: чему человек обучен (шкала 0–10).</summary>
        public SkillType PrimarySkill = SkillType.None;

        /// <summary>Природная сторона той же работы (шкала 1–10).</summary>
        public AttributeType SecondaryAttribute = AttributeType.None;
        public double BaseOutput;
        public double OutputPerPrimaryPoint = 1.0;
        public double OutputPerSecondaryPoint = 0.5;

        /// <summary>Открыт ли слот изначально (false — требует постройки/разблокировки).</summary>
        public bool UnlockedByDefault = true;

        /// <summary>
        /// Цена разблокировки для закрытых слотов. Пусто — слот открыть нельзя
        /// (заглушка под будущую стройку), поэтому у каждого закрытого слота цена
        /// должна быть проставлена явно.
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
