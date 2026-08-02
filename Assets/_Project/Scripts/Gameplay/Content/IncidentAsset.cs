using Game.Core.Threats;
using UnityEngine;

namespace Game.Gameplay.Content
{
    /// <summary>SO-обёртка инцидента «Напряжения» (US-18.1/11.3).</summary>
    [CreateAssetMenu(menuName = "Alpha/Content/Incident", fileName = "incident")]
    public sealed class IncidentAsset : ScriptableObject
    {
        public IncidentDefinition incident = new IncidentDefinition();
        public IncidentDefinition ToDefinition() => incident;
    }
}
