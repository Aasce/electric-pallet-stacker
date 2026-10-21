using ElectricPalletStackers.PalletStackers;
using UnityEngine;

namespace ElectricPalletStackers.Ble
{
    /// <summary>
    /// Adds a trigger that follows the moving fork mesh and forwards unsafe
    /// contacts to the vehicle collision reporter. A pallet may only overlap
    /// the forks after the load handler has accepted and attached it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PalletStackerForkCollisionSensor : MonoBehaviour
    {
        [SerializeField] private BoxCollider _sensor;
        [SerializeField] private PalletStackerCollisionReporter _collisionReporter;
        [SerializeField] private PalletStackerLoadHandler _loadHandler;
        [SerializeField] private Transform _vehicleRoot;
        [SerializeField] private LayerMask _collisionLayers = ~0;
        [Tooltip("Ignores a support surface whose top is level with the bottom of the forks.")]
        [SerializeField] private bool _ignoreGroundLikeContacts = true;
        [SerializeField, Min(0f)] private float _groundClearance = 0.03f;

        private void OnTriggerEnter(Collider other)
        {
            if (_collisionReporter == null || ShouldIgnore(other)) return;

            Debug.LogWarning(
                $"[PALLET STACKER] Fork collision interlock triggered by '{other.gameObject.name}'.",
                this);
            _collisionReporter.ReportCollision(other);
        }

        private bool ShouldIgnore(Collider other)
        {
            if (other == null || other == _sensor) return true;
            if (other.isTrigger) return true;

            int layerMask = 1 << other.gameObject.layer;
            if ((_collisionLayers.value & layerMask) == 0) return true;
            if (IsVehicleCollider(other)) return true;

            PalletStackerLoad load = other.GetComponentInParent<PalletStackerLoad>();
            if (load != null)
            {
                if (!load.OwnsCollider(other)) return true;
                if (_loadHandler != null && _loadHandler.HeldLoad == load) return true;

                // Fork contact with the pallet body is expected while entering the
                // pocket or supporting it from below/above. Only a base strike is unsafe.
                return !load.IsForkHazardCollider(other);
            }

            return _ignoreGroundLikeContacts && IsGroundLikeContact(other);
        }

        private bool IsVehicleCollider(Collider other)
        {
            if (_vehicleRoot == null) return false;
            if (other.transform.IsChildOf(_vehicleRoot)) return true;

            Rigidbody attachedBody = other.attachedRigidbody;
            return attachedBody != null && attachedBody.transform.IsChildOf(_vehicleRoot);
        }

        private bool IsGroundLikeContact(Collider other)
        {
            if (_sensor == null) return false;
            return other.bounds.max.y <= _sensor.bounds.min.y + _groundClearance;
        }

    }
}
