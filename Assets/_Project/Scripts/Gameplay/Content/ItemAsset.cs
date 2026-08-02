using Game.Core.Items;
using Game.Core.Stats;
using UnityEngine;

namespace Game.Gameplay.Content
{
    /// <summary>
    /// SO-обёртка предмета (US-18.1/6.1). Интерфейс IItemEffect не сериализуется —
    /// сигнатурный эффект именного задаётся DTO-полями и собирается в ToDefinition().
    /// </summary>
    [CreateAssetMenu(menuName = "Alpha/Content/Item", fileName = "item")]
    public sealed class ItemAsset : ScriptableObject
    {
        public ItemDefinition definition = new ItemDefinition();

        [Header("Уникальный эффект именного (US-6.1; интерфейс не сериализуется — DTO)")]
        public bool hasSignatureEffect;
        public string signatureName;
        public StatModifier[] signatureModifiers = new StatModifier[0];

        public ItemDefinition ToDefinition()
        {
            // Unity не сериализует null для [Serializable]-классов: у не-оружия поле
            // Weapon коллапсирует в «фантомный» дефолт с пустым Id — обнуляем.
            if (definition.Weapon != null && string.IsNullOrEmpty(definition.Weapon.Id))
                definition.Weapon = null;

            if (hasSignatureEffect && definition.Effect == null)
                definition.Effect = new SignatureEffect(signatureName, signatureModifiers);
            return definition;
        }
    }
}
