using ElectricPalletStackers.Localization;
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
        [Tooltip("Uses the regular Cross Safety instruction panel prefab directly; no prefab variant is required.")]
        [SerializeField] private GameObject _alignmentWarningPrefab;
        [SerializeField, Min(0.1f)] private float _warningHoldSeconds = 2.5f;

        private GameObject _alignmentWarningInstance;
        private LocalizationManager _localizationManager;
        private LocalizedText _warningText;
        private float _lastWarningTime = float.NegativeInfinity;

        private void OnEnable()
        {
            if (_loadHandler != null)
                _loadHandler.UnsafePickupAttempted += HandleUnsafePickupAttempted;
        }

        private void OnDisable()
        {
            if (_loadHandler != null)
                _loadHandler.UnsafePickupAttempted -= HandleUnsafePickupAttempted;
            HideAlignmentWarning();
        }

        private void OnDestroy()
        {
            if (_localizationManager != null && _warningText != null)
                _localizationManager.Unregister(_warningText);
            if (_alignmentWarningInstance != null) Destroy(_alignmentWarningInstance);
        }

        private void LateUpdate()
        {
            if (_alignmentWarningInstance == null ||
                !_alignmentWarningInstance.activeSelf ||
                Time.unscaledTime - _lastWarningTime <= _warningHoldSeconds)
                return;

            HideAlignmentWarning();
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

                if (load.IsForkHazardCollider(other) || IsPalletSideContact(other, load))
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
            _loadHandler?.SuppressVehicleForces(load);
            ShowAlignmentWarning();
        }

        private void ShowAlignmentWarning()
        {
            EnsureAlignmentWarning();
            if (_alignmentWarningInstance == null) return;

            Camera viewCamera = Camera.main;
            if (viewCamera != null)
            {
                Canvas canvas = _alignmentWarningInstance.GetComponent<Canvas>();
                if (canvas != null) canvas.worldCamera = viewCamera;

                TargetFollower follower = _alignmentWarningInstance.GetComponent<TargetFollower>();
                follower?.SetTarget(viewCamera.transform);
            }

            _lastWarningTime = Time.unscaledTime;
            if (!_alignmentWarningInstance.activeSelf)
                _alignmentWarningInstance.SetActive(true);
        }

        private void HideAlignmentWarning()
        {
            if (_alignmentWarningInstance != null && _alignmentWarningInstance.activeSelf)
                _alignmentWarningInstance.SetActive(false);
        }

        private void EnsureAlignmentWarning()
        {
            if (_alignmentWarningInstance != null || _alignmentWarningPrefab == null) return;

            _alignmentWarningInstance = Instantiate(_alignmentWarningPrefab);
            _alignmentWarningInstance.name = "Fork Alignment Warning Panel";

            Camera viewCamera = Camera.main;
            Canvas canvas = _alignmentWarningInstance.GetComponent<Canvas>();
            if (canvas != null)
            {
                canvas.worldCamera = viewCamera;
                canvas.overrideSorting = true;
                canvas.sortingOrder = short.MaxValue - 5;
            }

            TargetFollower follower = _alignmentWarningInstance.GetComponent<TargetFollower>();
            if (follower != null && viewCamera != null)
                follower.SetTarget(viewCamera.transform);

            _warningText = _alignmentWarningInstance.GetComponentInChildren<LocalizedText>(true);
            _localizationManager = FindFirstObjectByType<LocalizationManager>();
            if (_warningText != null)
            {
                _warningText.SetKey(
                    LocalizationKeys.PalletHandling.AlignmentWarning,
                    _localizationManager != null ? _localizationManager.Service : null);
                _localizationManager?.Register(_warningText);
            }

            _alignmentWarningInstance.SetActive(false);
        }

    }
}
