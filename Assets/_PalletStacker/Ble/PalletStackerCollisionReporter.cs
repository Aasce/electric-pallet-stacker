using System;
using ElectricPalletStackers.PalletStackers;
using UnityEngine;
using UnityEngine.Events;

namespace ElectricPalletStackers.Ble
{
    [DisallowMultipleComponent]
    public sealed class PalletStackerCollisionReporter : MonoBehaviour
    {
        [SerializeField] private PalletStackerCollisionSender _collisionSender;
        [SerializeField] private LayerMask _collisionLayers = ~0;
        [Tooltip("Minimum velocity into a contact surface. Tangential sliding velocity is ignored.")]
        [SerializeField, Min(0f)] private float _minimumRelativeSpeed;
        [Tooltip("Prevents the floor/support surface from being reported as an obstacle collision.")]
        [SerializeField] private bool _ignoreGroundLikeContacts = true;
        [Tooltip("Contacts whose normal is this aligned with world up are treated as ground-like.")]
        [SerializeField, Range(0f, 1f)] private float _groundNormalDotThreshold = 0.7f;
        [Tooltip("Fork colliders that may touch PalletStackerLoad cargo without causing a loss. Their contacts with every other object are still reported.")]
        [SerializeField] private Collider[] _forkCargoSafeColliders;
        [SerializeField] private UnityEvent _onLocalCollisionDetected;

        public event Action CollisionDetected;

        public void Bind(PalletStackerCollisionSender collisionSender)
        {
            _collisionSender = collisionSender;
        }

        private void OnCollisionEnter(Collision collision)
        {
            int layerMask = 1 << collision.gameObject.layer;
            if ((_collisionLayers.value & layerMask) == 0) return;

            float impactSpeed = GetMaximumObstacleImpactSpeed(collision);
            if (impactSpeed < _minimumRelativeSpeed) return;

            Debug.LogWarning(
                $"[PALLET STACKER] Collision interlock triggered by '{collision.gameObject.name}' " +
                $"on layer {LayerMask.LayerToName(collision.gameObject.layer)} " +
                $"at {impactSpeed:0.00} m/s.",
                this);
            ReportCollision();
        }

        private float GetMaximumObstacleImpactSpeed(Collision collision)
        {
            float maximumImpactSpeed = 0f;
            bool foundObstacleContact = false;

            for (int index = 0; index < collision.contactCount; index++)
            {
                ContactPoint contact = collision.GetContact(index);
                if (IsSafeForkCargoContact(contact)) continue;

                Vector3 normal = contact.normal.normalized;
                float upAlignment = Vector3.Dot(normal, Vector3.up);
                if (_ignoreGroundLikeContacts && upAlignment >= _groundNormalDotThreshold) continue;

                foundObstacleContact = true;
                float normalImpactSpeed = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, normal));
                maximumImpactSpeed = Mathf.Max(maximumImpactSpeed, normalImpactSpeed);
            }

            return foundObstacleContact ? maximumImpactSpeed : 0f;
        }

        private bool IsSafeForkCargoContact(ContactPoint contact)
        {
            if (_forkCargoSafeColliders == null) return false;

            for (int index = 0; index < _forkCargoSafeColliders.Length; index++)
            {
                Collider forkCollider = _forkCargoSafeColliders[index];
                if (forkCollider == null) continue;

                Collider otherCollider = null;
                if (contact.thisCollider == forkCollider) otherCollider = contact.otherCollider;
                else if (contact.otherCollider == forkCollider) otherCollider = contact.thisCollider;

                if (otherCollider != null &&
                    otherCollider.GetComponentInParent<PalletStackerLoad>() != null)
                    return true;
            }

            return false;
        }

        [ContextMenu("Simulate Vehicle Collision")]
        public void ReportCollision()
        {
            // Local gameplay stops before BLE confirmation to avoid network latency in the safety path.
            CollisionDetected?.Invoke();
            _onLocalCollisionDetected?.Invoke();
            _collisionSender?.SendCollision();
        }
    }
}
