using UnityEngine;

namespace Game.Gameplay.Content
{
    /// <summary>SO-обёртка шрама (US-18.1).</summary>
    [CreateAssetMenu(menuName = "Alpha/Content/Scar", fileName = "scar")]
    public sealed class ScarAsset : ScriptableObject
    {
        public Game.Core.Health.Scar scar = new Game.Core.Health.Scar();
        public Game.Core.Health.Scar ToDefinition() => scar;
    }
}
