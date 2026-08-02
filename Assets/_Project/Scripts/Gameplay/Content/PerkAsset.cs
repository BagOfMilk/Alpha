using Game.Core.Characters;
using UnityEngine;

namespace Game.Gameplay.Content
{
    /// <summary>SO-обёртка перка (US-18.1/3.10).</summary>
    [CreateAssetMenu(menuName = "Alpha/Content/Perk", fileName = "perk")]
    public sealed class PerkAsset : ScriptableObject
    {
        public PerkDefinition perk = new PerkDefinition();
        public PerkDefinition ToDefinition() => perk;
    }
}
