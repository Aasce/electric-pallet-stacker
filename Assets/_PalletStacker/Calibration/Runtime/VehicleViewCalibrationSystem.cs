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
        [Header("Vehicle Alignment")]
        [SerializeField, Min(0f)] private float _cameraHeightAboveGround = 1.5f;
        [Header("Calibration Panel")]
        [Tooltip("Inspector option only. Keeps the calibration panel at the same headset-relative pose.")]
        [SerializeField] private bool _followCamera;
        [SerializeField, Min(0.001f)] private float _positionStep = 0.01f;
        private bool _panelParentedToHeadset;
        private bool _needsInitialPanelPlacement;

        private void Awake()
        {
            if (_calibrationPanel == null) return;
            _needsInitialPanelPlacement = true;
            _calibrationPanel.SetParent(null, true);
            _panelParentedToHeadset = false;
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

            bool shouldParentToHeadset = _followCamera;
            if (shouldParentToHeadset == _panelParentedToHeadset) return;
            _calibrationPanel.SetParent(shouldParentToHeadset ? _headset : null, true);
            _panelParentedToHeadset = shouldParentToHeadset;
        }

        public void AlignToVehicle()
        {
            if (_headset == null || _cameraOffset == null || _vehicleForward == null) return;
            Vector3 cameraPosition = _headset.position;
            Vector3 headsetForward = Vector3.ProjectOnPlane(_headset.forward, Vector3.up);
            Vector3 vehicleForward = Vector3.ProjectOnPlane(_vehicleForward.forward, Vector3.up);
            if (headsetForward.sqrMagnitude < 0.0001f || vehicleForward.sqrMagnitude < 0.0001f) return;
            float yawDelta = Vector3.SignedAngle(headsetForward, vehicleForward, Vector3.up);
            _cameraOffset.rotation = Quaternion.AngleAxis(yawDelta, Vector3.up) * _cameraOffset.rotation;
            cameraPosition.y = GetVehicleGroundHeight() + _cameraHeightAboveGround;
            _cameraOffset.position += cameraPosition - _headset.position;
        }

        public void AdjustOffset(Vector2 localDirection)
        {
            if (_cameraOffset == null) return;
            Vector3 position = _cameraOffset.localPosition;
            position.x += localDirection.x * _positionStep;
            position.y += localDirection.y * _positionStep;
            _cameraOffset.localPosition = position;
        }

        private void PlaceCalibrationPanelInReach()
        {
            Quaternion yawRotation = Quaternion.Euler(0f, _headset.eulerAngles.y, 0f);
            _calibrationPanel.position = _headset.position + yawRotation * new Vector3(-0.06f, -0.03f, 0.55f);
            _calibrationPanel.rotation = yawRotation;
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
