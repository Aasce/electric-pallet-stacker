using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace ElectricPalletStackers.UI
{
    /// <summary>Shared poke/hand-grab setup for movable world-space panels.</summary>
    [DisallowMultipleComponent]
    public sealed class WorldSpacePanelGrab : MonoBehaviour
    {
        [Header("Inspector References")]
        [SerializeField] private Collider _grabCollider;
        [SerializeField] private XRGrabInteractable _grabInteractable;
        [Tooltip("Detach this view from its layout parent when a hand starts moving it.")]
        [SerializeField] private bool _detachFromParentOnGrab = true;

        private bool _isSubscribed;

        public bool IsGrabbed => _grabInteractable != null && _grabInteractable.isSelected;

        private void OnEnable()
        {
            if (_grabInteractable == null || _isSubscribed) return;
            _grabInteractable.selectEntered.AddListener(HandleGrabStarted);
            _isSubscribed = true;
        }

        private void OnDisable()
        {
            if (_grabInteractable == null || !_isSubscribed) return;
            _grabInteractable.selectEntered.RemoveListener(HandleGrabStarted);
            _isSubscribed = false;
        }

        public void SetEnabled(bool enabled)
        {
            if (_grabCollider != null) _grabCollider.enabled = enabled;
            if (_grabInteractable != null) _grabInteractable.enabled = enabled;
        }

        private void HandleGrabStarted(SelectEnterEventArgs _)
        {
            if (_detachFromParentOnGrab && transform.parent != null)
                transform.SetParent(null, true);
        }
    }
}
