using UnityEngine;

namespace ElectricPalletStackers.PalletStackers
{
    /// <summary>
    /// Explicit references owned by the instantiated turn-vignette view.
    /// Keep this component on the vignette View root.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TurnVignetteView : MonoBehaviour
    {
        [SerializeField] private Renderer _targetRenderer;
        [Tooltip("Optional sample controller disabled when the pallet-stacker follower drives the material.")]
        [SerializeField] private MonoBehaviour _sampleController;

        public Renderer TargetRenderer => _targetRenderer;

        public void DisableSampleController()
        {
            if (_sampleController != null) _sampleController.enabled = false;
        }
    }
}
