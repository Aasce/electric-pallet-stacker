using System;
using ElectricPalletStackers.PalletStackers;
using UnityEngine;

namespace ElectricPalletStackers.Gameplay
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerCollisionFailureSource : MonoBehaviour, IGameFailureSource, IGameRoundParticipant
    {
        [SerializeField] private LayerMask _collisionLayers = ~0;
        [Tooltip("Contacts this aligned with world up are treated as floor/support contacts.")]
        [SerializeField, Range(0f, 1f)] private float _groundNormalDotThreshold = 0.7f;

        private bool _roundActive;

        public event Action FailureRequested;

        public void PrepareRound()
        {
            _roundActive = true;
        }

        public void FinishRound(GameState result)
        {
            _roundActive = false;
        }

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (!_roundActive || hit.collider == null) return;

            int layerMask = 1 << hit.collider.gameObject.layer;
            if ((_collisionLayers.value & layerMask) == 0) return;
            if (Vector3.Dot(hit.normal.normalized, Vector3.up) >= _groundNormalDotThreshold) return;

            // The operator follows the stacker closely by design; touching the controlled
            // vehicle itself is not an environment collision.
            if (hit.collider.GetComponentInParent<PalletStacker>() != null) return;

            _roundActive = false;
            Debug.LogWarning(
                $"[PLAYER] Collision failure triggered by '{hit.collider.name}' " +
                $"on layer {LayerMask.LayerToName(hit.collider.gameObject.layer)}.",
                this);
            FailureRequested?.Invoke();
        }
    }
}
