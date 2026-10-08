using UnityEngine;
using ElectricPalletStackers.PalletStackers;

namespace ElectricPalletStackers.Calibration
{
    /// <summary>Applies calibration commands to the XR rig. UI components only forward input.</summary>
    [DisallowMultipleComponent]
    public sealed class VehicleViewCalibrationSystem : MonoBehaviour
    {
        [SerializeField] private Transform _cameraOffset;
        [SerializeField] private Transform _headset;
        [SerializeField] private Transform _vehicleForward;
        [Header("Calibration Panel")]
        [Tooltip("Inspector option only. Keeps the calibration panel at the same headset-relative pose.")]
        [SerializeField] private bool _followCamera;
        [SerializeField, Min(0.001f)] private float _positionStep = 0.01f;
        private Transform _calibrationPanel;
        private bool _panelParentedToHeadset;

        private void Awake()
        {
            if (_headset == null && Camera.main != null) _headset = Camera.main.transform;
            if (_cameraOffset == null && _headset != null) _cameraOffset = _headset.parent;
            if (_vehicleForward == null)
            {
                PalletStacker vehicle = FindFirstObjectByType<PalletStacker>();
                if (vehicle != null) _vehicleForward = vehicle.transform;
            }
        }

        private void LateUpdate()
        {
            if (_calibrationPanel == null || _headset == null || _followCamera == _panelParentedToHeadset) return;
            _calibrationPanel.SetParent(_followCamera ? _headset : null, true);
            _panelParentedToHeadset = _followCamera;
        }

        public void AlignToVehicle()
        {
            ResolveReferences();
            if (_headset == null || _cameraOffset == null || _vehicleForward == null) return;
            Vector3 cameraPosition = _headset.position;
            Vector3 headsetForward = Vector3.ProjectOnPlane(_headset.forward, Vector3.up);
            Vector3 vehicleForward = Vector3.ProjectOnPlane(_vehicleForward.forward, Vector3.up);
            if (headsetForward.sqrMagnitude < 0.0001f || vehicleForward.sqrMagnitude < 0.0001f) return;
            float yawDelta = Vector3.SignedAngle(headsetForward, vehicleForward, Vector3.up);
            _cameraOffset.rotation = Quaternion.AngleAxis(yawDelta, Vector3.up) * _cameraOffset.rotation;
            cameraPosition.y = GetVehicleGroundHeight() + 1.5f;
            _cameraOffset.position += cameraPosition - _headset.position;
        }

        public void AdjustOffset(Vector2 localDirection)
        {
            ResolveReferences();
            if (_cameraOffset == null) return;
            Vector3 position = _cameraOffset.localPosition;
            position.x += localDirection.x * _positionStep;
            position.y += localDirection.y * _positionStep;
            _cameraOffset.localPosition = position;
        }

        public void SetCalibrationPanel(Transform panel)
        {
            _calibrationPanel = panel;
        }

        private float GetVehicleGroundHeight()
        {
            Vector3 origin = _vehicleForward.position + Vector3.up * 5f;
            RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 20f, ~0, QueryTriggerInteraction.Ignore);
            float closestDistance = float.PositiveInfinity;
            float groundHeight = _vehicleForward.position.y;
            for (int i = 0; i < hits.Length; i++)
            {
                Transform hitTransform = hits[i].transform;
                if (hitTransform == _vehicleForward || hitTransform.IsChildOf(_vehicleForward)) continue;
                if (hits[i].distance >= closestDistance) continue;
                closestDistance = hits[i].distance;
                groundHeight = hits[i].point.y;
            }
            return groundHeight;
        }

        private void ResolveReferences()
        {
            if (_headset == null && Camera.main != null) _headset = Camera.main.transform;
            if (_cameraOffset == null && _headset != null) _cameraOffset = _headset.parent;
            if (_vehicleForward == null)
            {
                PalletStacker vehicle = FindFirstObjectByType<PalletStacker>();
                if (vehicle != null) _vehicleForward = vehicle.transform;
            }
        }
    }
}
