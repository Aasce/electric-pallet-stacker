using UnityEngine;

namespace ElectricPalletStackers.Gameplay
{
    /// <summary>
    /// Owns only the visual state of a pallet destination marker.
    /// Keep this component on the destination View GameObject and assign every
    /// renderer explicitly in the Inspector.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PalletDestinationView : MonoBehaviour
    {
        [SerializeField] private Renderer[] _renderers;

        public void SetVisible(bool visible)
        {
            if (_renderers == null) return;

            for (int index = 0; index < _renderers.Length; index++)
            {
                Renderer target = _renderers[index];
                if (target != null) target.enabled = visible;
            }
        }
    }
}
