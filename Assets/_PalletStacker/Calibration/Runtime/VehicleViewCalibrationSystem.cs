using ElectricPalletStackers.UI;
using UnityEngine;

namespace ElectricPalletStackers.Calibration
{
    /// <summary>Applies calibration commands to the XR rig. UI components only forward input.</summary>
    [DefaultExecutionOrder(10000)]
    [DisallowMultipleComponent]
    public sealed class VehicleViewCalibrationSystem : MonoBehaviour
    {
        [SerializeField] private Transform _cameraOffset;
        [SerializeField] private Transform _headset;
        [SerializeField] private Transform _vehicleForward;
        [SerializeField] private Transform _calibrationPanel;
        [SerializeField] private WorldSpacePanelGrab _calibrationPanelGrab;
        [Header("Vehicle Alignment")]
        [SerializeField] private Vector3 _headsetPositionRelativeToVehicle = new(0f, 0f, -1f);
        [SerializeField, Min(0f)] private float _cameraHeightAboveGround = 1.7f;
        [Header("Calibration Panel")]
        [SerializeField] private Vector3 _panelHeadsetOffset = new(-0.06f, -0.03f, 0.55f);
        [SerializeField, Min(0.001f)] private float _positionStep = 0.015f;
        private bool _needsInitialPanelPlacement;

        private void Awake()
        {
            if (_calibrationPanel == null) return;
            _needsInitialPanelPlacement = true;
            _calibrationPanel.SetParent(null, true);
        }

        private void LateUpdate()
        {
            if (_calibrationPanel == null) return;
            if (_headset == null) return;

            // Wait until LateUpdate so XR origin/follower setup has settled for this frame.
            if (_needsInitialPanelPlacement)
            {
                PlaceCalibrationPanelInReach();
                _needsInitialPanelPlacement = false;
            }

            // Follow headset yaw only so the panel faces the user while remaining
            // upright and independent from headset pitch/roll.
            _calibrationPanel.rotation = GetHeadsetYawRotation();

            // Keep the panel movable while held and retain the new relative position
            // after release.
            if (_calibrationPanelGrab != null && _calibrationPanelGrab.IsGrabbed)
            {
                _panelHeadsetOffset = _headset.InverseTransformPoint(_calibrationPanel.position);
                return;
            }

            _calibrationPanel.position = _headset.TransformPoint(_panelHeadsetOffset);
        }

        public void AlignToVehicle()
        {
            if (_headset == null || _cameraOffset == null || _vehicleForward == null) return;
            Vector3 headsetForward = Vector3.ProjectOnPlane(_headset.forward, Vector3.up);
            Vector3 vehicleForward = Vector3.ProjectOnPlane(_vehicleForward.forward, Vector3.up);
            if (headsetForward.sqrMagnitude < 0.0001f || vehicleForward.sqrMagnitude < 0.0001f) return;

            float yawDelta = Vector3.SignedAngle(headsetForward, vehicleForward, Vector3.up);
            _cameraOffset.rotation = Quaternion.AngleAxis(yawDelta, Vector3.up) * _cameraOffset.rotation;

            Vector3 targetHeadsetPosition = _vehicleForward.TransformPoint(_headsetPositionRelativeToVehicle);
            targetHeadsetPosition.y = GetVehicleGroundHeight() + _cameraHeightAboveGround;

            // The headset pose is owned by XR tracking. Move only the offset so the
            // tracked camera lands exactly at the vehicle-relative alignment target.
            _cameraOffset.position += targetHeadsetPosition - _headset.position;
        }

        public void AdjustOffset(Vector3 cameraLocalDirection)
        {
            if (_cameraOffset == null || _headset == null) return;

            Vector3 cameraForward = Vector3.ProjectOnPlane(_headset.forward, Vector3.up).normalized;
            Vector3 cameraRight = Vector3.ProjectOnPlane(_headset.right, Vector3.up).normalized;
            if (cameraForward.sqrMagnitude < 0.0001f) cameraForward = _cameraOffset.forward;
            if (cameraRight.sqrMagnitude < 0.0001f) cameraRight = _cameraOffset.right;

            Vector3 worldDelta =
                cameraRight * cameraLocalDirection.x +
                Vector3.up * cameraLocalDirection.y +
                cameraForward * cameraLocalDirection.z;
            _cameraOffset.position += worldDelta * _positionStep;
        }

        private void PlaceCalibrationPanelInReach()
        {
            Quaternion yawRotation = GetHeadsetYawRotation();
            _calibrationPanel.position = _headset.position + yawRotation * _panelHeadsetOffset;
            _calibrationPanel.rotation = yawRotation;
            _panelHeadsetOffset = _headset.InverseTransformPoint(_calibrationPanel.position);
        }

        private Quaternion GetHeadsetYawRotation()
        {
            return Quaternion.Euler(0f, _headset.eulerAngles.y, 0f);
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
    }
}
