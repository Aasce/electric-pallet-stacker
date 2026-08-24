using System;
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
        [Tooltip("Contacts whose normal is this aligned with world up/down are treated as ground-like.")]
        [SerializeField, Range(0f, 1f)] private float _groundNormalDotThreshold = 0.7f;
        [SerializeField] private UnityEvent _onLocalCollisionDetected;

        public event Action CollisionDetected;

        private void Awake()
        {
            if (_collisionSender == null) _collisionSender = GetComponentInParent<PalletStackerCollisionSender>();
            if (_collisionSender == null) _collisionSender = FindFirstObjectByType<PalletStackerCollisionSender>();
        }

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

            ReportCollision();
        }

        private float GetMaximumObstacleImpactSpeed(Collision collision)
        {
            float maximumImpactSpeed = 0f;
            bool foundObstacleContact = false;

            for (int index = 0; index < collision.contactCount; index++)
            {
                Vector3 normal = collision.GetContact(index).normal.normalized;
                float upAlignment = Mathf.Abs(Vector3.Dot(normal, Vector3.up));
                if (_ignoreGroundLikeContacts && upAlignment >= _groundNormalDotThreshold) continue;

                foundObstacleContact = true;
                float normalImpactSpeed = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, normal));
                maximumImpactSpeed = Mathf.Max(maximumImpactSpeed, normalImpactSpeed);
            }

            return foundObstacleContact ? maximumImpactSpeed : 0f;
        }

        private void OnValidate()
        {
            _minimumRelativeSpeed = Mathf.Max(0f, _minimumRelativeSpeed);
            _groundNormalDotThreshold = Mathf.Clamp01(_groundNormalDotThreshold);
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
