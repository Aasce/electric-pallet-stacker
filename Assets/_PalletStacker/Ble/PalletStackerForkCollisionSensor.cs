using ElectricPalletStackers.PalletStackers;
using ElectricPalletStackers.UI;
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
        [Tooltip("Separating directions less aligned with the pallet's up axis count as side contacts.")]
        [SerializeField, Range(0f, 1f)] private float _verticalContactDotThreshold = 0.7f;
        [Tooltip("Fallback tolerance at the pallet's top and bottom edges when penetration data is unavailable.")]
        [SerializeField, Min(0f)] private float _palletSideEdgeTolerance = 0.01f;

        [Header("Alignment warning")]
        [SerializeField] private NotificationPanelPresenter _notifications;

        private void OnEnable()
        {
            if (_loadHandler != null)
                _loadHandler.UnsafePickupAttempted += HandleUnsafePickupAttempted;
        }

        private void OnDisable()
        {
            if (_loadHandler != null)
                _loadHandler.UnsafePickupAttempted -= HandleUnsafePickupAttempted;
        }

        private void OnTriggerEnter(Collider other)
        {
            EvaluateContact(other, true);
        }

        private void OnTriggerStay(Collider other)
        {
            EvaluateContact(other, false);
        }

        private void EvaluateContact(Collider other, bool reportEnvironmentCollision)
        {
            if (other == null || other == _sensor || other.isTrigger) return;

            int layerMask = 1 << other.gameObject.layer;
            if ((_collisionLayers.value & layerMask) == 0 || IsVehicleCollider(other)) return;

            PalletStackerLoad load = other.GetComponentInParent<PalletStackerLoad>();
            if (load != null)
            {
                if (!load.OwnsCollider(other)) return;

                _loadHandler?.SuppressVehicleForces(load);
                if (_loadHandler != null && _loadHandler.HeldLoad == load) return;
                if (load.Body != null && load.Body.linearVelocity.y < -0.1f) return;

                if (IsPalletSideContact(other, load))
                    ShowAlignmentWarning();
                return;
            }

            if (!reportEnvironmentCollision ||
                (_ignoreGroundLikeContacts && IsGroundLikeContact(other)) ||
                _collisionReporter == null)
                return;

            Debug.LogWarning(
                $"[PALLET STACKER] Fork collision interlock triggered by '{other.gameObject.name}'.",
                this);
            _collisionReporter.ReportCollision(other);
        }

        private bool IsPalletSideContact(Collider palletCollider, PalletStackerLoad load)
        {
            if (_sensor == null || palletCollider == null || load == null) return false;

            Vector3 palletUp = load.transform.up.normalized;
            if (palletUp.sqrMagnitude <= Mathf.Epsilon) return false;

            if (Physics.ComputePenetration(
                    _sensor,
                    _sensor.transform.position,
                    _sensor.transform.rotation,
                    palletCollider,
                    palletCollider.transform.position,
                    palletCollider.transform.rotation,
                    out Vector3 separationDirection,
                    out _))
            {
                float verticalAlignment = Mathf.Abs(
                    Vector3.Dot(separationDirection.normalized, palletUp));
                return verticalAlignment < _verticalContactDotThreshold;
            }

            GetProjectionRange(palletCollider, palletUp, out float palletBottom, out float palletTop);
            float forkCenterHeight = Vector3.Dot(
                _sensor.transform.TransformPoint(_sensor.center),
                palletUp);

            return forkCenterHeight > palletBottom + _palletSideEdgeTolerance &&
                   forkCenterHeight < palletTop - _palletSideEdgeTolerance;
        }

        private static void GetProjectionRange(Collider collider, Vector3 axis, out float minimum, out float maximum)
        {
            Vector3 center;
            float extent;

            if (collider is BoxCollider box)
            {
                Transform boxTransform = box.transform;
                center = boxTransform.TransformPoint(box.center);
                Vector3 halfSize = box.size * 0.5f;
                Vector3 axisX = boxTransform.TransformVector(Vector3.right * halfSize.x);
                Vector3 axisY = boxTransform.TransformVector(Vector3.up * halfSize.y);
                Vector3 axisZ = boxTransform.TransformVector(Vector3.forward * halfSize.z);
                extent = Mathf.Abs(Vector3.Dot(axis, axisX)) +
                         Mathf.Abs(Vector3.Dot(axis, axisY)) +
                         Mathf.Abs(Vector3.Dot(axis, axisZ));
            }
            else
            {
                Bounds bounds = collider.bounds;
                center = bounds.center;
                Vector3 boundsExtents = bounds.extents;
                extent = Mathf.Abs(axis.x) * boundsExtents.x +
                         Mathf.Abs(axis.y) * boundsExtents.y +
                         Mathf.Abs(axis.z) * boundsExtents.z;
            }

            float projectedCenter = Vector3.Dot(center, axis);
            minimum = projectedCenter - extent;
            maximum = projectedCenter + extent;
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

        private void HandleUnsafePickupAttempted(PalletStackerLoad load)
        {
            if (load == null) return;
            if (_loadHandler != null && _loadHandler.HeldLoad == load) return;
            if (load.Body != null && load.Body.linearVelocity.y < -0.1f) return;
            _loadHandler?.SuppressVehicleForces(load);
            ShowAlignmentWarning();
        }

        private void ShowAlignmentWarning()
        {
            _notifications?.Show(
                ElectricPalletStackers.Localization.LocalizationKeys.PalletHandling.AlignmentWarning);
        }

    }
}
