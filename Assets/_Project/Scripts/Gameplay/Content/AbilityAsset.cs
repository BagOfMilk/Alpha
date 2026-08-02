using Game.Core.Combat;
using UnityEngine;

namespace Game.Gameplay.Content
{
    /// <summary>SO-обёртка способности (US-18.1/3.9) — общий пул игрока и врагов (US-3.14).</summary>
    [CreateAssetMenu(menuName = "Alpha/Content/Ability", fileName = "ability")]
    public sealed class AbilityAsset : ScriptableObject
    {
        public AbilityDefinition ability = new AbilityDefinition();
        public AbilityDefinition ToDefinition() => ability;
    }
}
