using UnityEngine;

namespace ElectricPalletStackers.UI
{
    /// <summary>
    /// Owns only the world pose and grab state of the phone call view.
    /// Every scene dependency is assigned explicitly in the Inspector.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PhoneCallWorldPlacement : MonoBehaviour
    {
        [Header("Scene References")]
        [SerializeField] private Transform _panelRoot;
        [SerializeField] private Transform _viewer;
        [SerializeField] private WorldSpacePanelGrab _panelGrab;

        [Header("In Front Of Viewer")]
        [Tooltip("Offset in the viewer's yaw space. Positive Z is in front of the viewer.")]
        [SerializeField] private Vector3 _viewerYawOffset = new(-0.06f, -0.03f, 0.55f);

        [Header("Occlude Road Blocker")]
        [Tooltip("Local offset from the configured blocker target, normally aimed at its torso or head.")]
        [SerializeField] private Vector3 _blockerTargetLocalOffset = new(0f, 1.35f, 0f);
        [SerializeField, Range(0.1f, 0.9f)] private float _lineRatio = 0.35f;
        [SerializeField, Min(0.1f)] private float _maximumDistanceFromViewer = 0.65f;
        [SerializeField] private float _verticalOffset = -0.16f;

        private Transform _occlusionTarget;
        private bool _isVisible;
        private bool _configurationErrorReported;

        public Transform Viewer => _viewer;
        public Transform OcclusionTarget => _occlusionTarget;

        private void OnEnable()
        {
            Hide();
        }

        private void LateUpdate()
        {
            if (!_isVisible || !HasRequiredReferences()) return;

            if (_occlusionTarget != null)
            {
                PlaceBetweenViewerAndBlocker();
                return;
            }

            PlaceInFrontOfViewer();
        }

        public void ShowInFrontOfViewer()
        {
            if (!HasRequiredReferences()) return;

            _occlusionTarget = null;
            _isVisible = true;
            _panelGrab?.SetEnabled(true);
            PlaceInFrontOfViewer();
        }

        public void ShowOccluding(Transform blockerTarget)
        {
            if (!HasRequiredReferences()) return;
            if (blockerTarget == null)
            {
                Debug.LogError(
                    "PhoneCallWorldPlacement requires an explicit blocker target " +
                    "to enter occlusion mode.",
                    this);
                ShowInFrontOfViewer();
                return;
            }

            _occlusionTarget = blockerTarget;
            _isVisible = true;
            _panelGrab?.SetEnabled(false);
            PlaceBetweenViewerAndBlocker();
        }

        public void Hide()
        {
            _isVisible = false;
            _occlusionTarget = null;
            _panelGrab?.SetEnabled(false);
        }

        private void PlaceInFrontOfViewer()
        {
            if (_panelGrab != null && _panelGrab.IsGrabbed)
            {
                Quaternion inverseYaw = Quaternion.Inverse(GetViewerYawRotation());
                _viewerYawOffset = inverseYaw * (_panelRoot.position - _viewer.position);
                return;
            }

            Quaternion viewerYaw = GetViewerYawRotation();
            _panelRoot.SetPositionAndRotation(
                _viewer.position + viewerYaw * _viewerYawOffset,
                viewerYaw);
        }

        private void PlaceBetweenViewerAndBlocker()
        {
            Vector3 targetPoint = _occlusionTarget.TransformPoint(
                _blockerTargetLocalOffset);
            Vector3 toTarget = targetPoint - _viewer.position;
            float targetDistance = toTarget.magnitude;
            if (targetDistance < 0.01f) return;

            Vector3 direction = toTarget / targetDistance;
            float distance = Mathf.Min(
                _maximumDistanceFromViewer,
                targetDistance * _lineRatio);
            Vector3 position = _viewer.position + direction * distance +
                               Vector3.up * _verticalOffset;

            Vector3 planarDirection = Vector3.ProjectOnPlane(direction, Vector3.up);
            Quaternion rotation = planarDirection.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(planarDirection.normalized, Vector3.up)
                : GetViewerYawRotation();
            _panelRoot.SetPositionAndRotation(position, rotation);
        }

        private Quaternion GetViewerYawRotation()
        {
            return Quaternion.Euler(0f, _viewer.eulerAngles.y, 0f);
        }

        private bool HasRequiredReferences()
        {
            if (_panelRoot != null && _viewer != null) return true;

            if (!_configurationErrorReported)
            {
                Debug.LogError(
                    "PhoneCallWorldPlacement is not configured. Assign Panel Root and " +
                    "Viewer in the Inspector.",
                    this);
                _configurationErrorReported = true;
            }

            _isVisible = false;
            return false;
        }
    }
}
