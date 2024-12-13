using Core.Util;
using Unity.Properties;
using UnityEngine;

namespace UI.States
{
    [CreateAssetMenu]
    public class InspectorStackState : ResettingScriptableObject
    {
        #region Properties

        // Stack enabled state.
        [CreateProperty]
        public bool IsEnabled => ProbeManager.ActiveProbeManager;

        #endregion
    }
}
