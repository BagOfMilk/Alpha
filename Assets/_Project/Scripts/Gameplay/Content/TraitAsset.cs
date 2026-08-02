using Game.Core.Traits;
using UnityEngine;

namespace Game.Gameplay.Content
{
    /// <summary>SO-обёртка трейта (US-18.1). Класс = имя файла — иначе Unity теряет m_Script.</summary>
    [CreateAssetMenu(menuName = "Alpha/Content/Trait", fileName = "trait")]
    public sealed class TraitAsset : ScriptableObject
    {
        public Trait trait = new Trait();
        public Trait ToDefinition() => trait;
    }
}
