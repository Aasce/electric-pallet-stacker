using ElectricPalletStackers.PalletStackers;
using UnityEngine;

namespace ElectricPalletStackers.Ble
{
    /// <summary>
    /// Adds a trigger that follows the moving fork mesh and forwards unsafe
    /// contacts to the vehicle collision reporter. PalletStackerLoad cargo is
    /// intentionally allowed so the forks can enter and lift it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PalletStackerForkCollisionSensor : MonoBehaviour
    {
        [SerializeField] private BoxCollider _sensor;
        [SerializeField] private PalletStackerCollisionReporter _collisionReporter;
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
            _collisionReporter.ReportCollision();
        }

        private bool ShouldIgnore(Collider other)
        {
            if (other == null || other == _sensor) return true;
            if (other.isTrigger) return true;

            int layerMask = 1 << other.gameObject.layer;
            if ((_collisionLayers.value & layerMask) == 0) return true;
            if (IsVehicleCollider(other)) return true;
            if (other.GetComponentInParent<PalletStackerLoad>() != null) return true;

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
