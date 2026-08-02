using Game.Core.Characters;
using UnityEngine;

namespace Game.Gameplay.Content
{
    /// <summary>SO-обёртка бэкграунда (US-18.1/2.7).</summary>
    [CreateAssetMenu(menuName = "Alpha/Content/Background", fileName = "background")]
    public sealed class BackgroundAsset : ScriptableObject
    {
        public Background background = new Background();
        public Background ToDefinition() => background;
    }
}
