using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;
using UnityEngine.XR.Interaction.Toolkit.Attachment;

namespace ElectricPalletStackers.UI
{
    /// <summary>Shared poke/hand-grab setup for movable world-space panels.</summary>
    [DisallowMultipleComponent]
    public sealed class WorldSpacePanelGrab : MonoBehaviour
    {
        [SerializeField] private Collider _grabCollider;
        [SerializeField] private XRGrabInteractable _grabInteractable;

        public void Initialize()
        {
            if (_grabCollider == null) _grabCollider = GetComponent<Collider>();
            if (_grabInteractable == null) _grabInteractable = GetComponent<XRGrabInteractable>();
            if (_grabInteractable != null && _grabCollider != null && !_grabInteractable.colliders.Contains(_grabCollider))
                _grabInteractable.colliders.Add(_grabCollider);
            Rigidbody body = GetComponent<Rigidbody>();
            if (body != null)
            {
                body.useGravity = false;
                body.isKinematic = true;
            }
            ConfigureHandGrabInteractors();
        }

        public void SetEnabled(bool enabled)
        {
            if (_grabCollider != null) _grabCollider.enabled = enabled;
            if (_grabInteractable != null) _grabInteractable.enabled = enabled;
        }

        private static void ConfigureHandGrabInteractors()
        {
            NearFarInteractor[] interactors = FindObjectsByType<NearFarInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (NearFarInteractor interactor in interactors)
            {
                Transform hand = interactor != null ? interactor.transform.parent : null;
                if (hand == null || !hand.name.EndsWith("Hand", StringComparison.Ordinal)) continue;
                Transform aimPose = hand.Find("Aim Pose");
                if (aimPose == null) continue;
                interactor.attachTransform = aimPose;
                SphereInteractionCaster sphereCaster = interactor.GetComponent<SphereInteractionCaster>();
                if (sphereCaster != null) sphereCaster.castOrigin = aimPose;
                CurveInteractionCaster curveCaster = interactor.GetComponent<CurveInteractionCaster>();
                if (curveCaster != null) curveCaster.castOrigin = aimPose;
                InteractionAttachController attachController = interactor.GetComponent<InteractionAttachController>();
                if (attachController != null) attachController.transformToFollow = aimPose;
            }
        }
    }
}
