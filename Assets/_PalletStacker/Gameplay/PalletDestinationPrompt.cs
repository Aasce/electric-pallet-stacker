using ElectricPalletStackers.PalletStackers;
using UnityEngine;

namespace ElectricPalletStackers.Gameplay
{
    /// <summary>
    /// Positions and faces the serialized world-space destination prompt, and hides
    /// it while the target pallet bounds intersect the destination's expanded bounds.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PalletDestinationPrompt : MonoBehaviour
    {
        [Header("Proximity")]
        [SerializeField] private BoxCollider _destinationBounds;
        [Tooltip("Additional total size applied to the destination bounds with Bounds.Expand.")]
        [SerializeField] private Vector3 _destinationBoundsExtension = Vector3.one;

        [Header("World UI")]
        [SerializeField] private RectTransform _labelRoot;
        [SerializeField] private Canvas _canvas;
        [Tooltip("Offset from the destination bounds center, in destination-local space.")]
        [SerializeField] private Vector3 _positionOffset = new(0f, 1.6f, 0f);

        private PalletStackerLoad _targetPallet;
        private bool _requestedVisible;

        private void Awake()
        {
            if (_canvas == null && _labelRoot != null)
                _canvas = _labelRoot.GetComponent<Canvas>();

            SetVisible(false);
        }

        private void LateUpdate()
        {
            if (_labelRoot == null || !_requestedVisible) return;

            bool shouldShow = !IsTargetPalletNearDestination();
            if (_labelRoot.gameObject.activeSelf != shouldShow)
                _labelRoot.gameObject.SetActive(shouldShow);
            if (!shouldShow) return;

            Bounds destinationBounds = GetDestinationWorldBounds();
            _labelRoot.position = destinationBounds.center + transform.TransformVector(_positionOffset);

            Camera viewCamera = Camera.main;
            if (viewCamera == null) return;
            if (_canvas != null && _canvas.worldCamera != viewCamera)
                _canvas.worldCamera = viewCamera;

            Vector3 towardCamera = Vector3.ProjectOnPlane(
                viewCamera.transform.position - _labelRoot.position,
                Vector3.up);
            if (towardCamera.sqrMagnitude > 0.0001f)
            {
                // World-space Canvas faces local -Z, so its forward axis points away
                // from the camera while its visible face points toward the camera.
                _labelRoot.rotation = Quaternion.LookRotation(-towardCamera, Vector3.up);
            }
        }

        public void SetTargetPallet(PalletStackerLoad pallet)
        {
            _targetPallet = pallet;
        }

        public void SetVisible(bool visible)
        {
            _requestedVisible = visible;
            if (_labelRoot == null) return;

            bool shouldShow = visible && !IsTargetPalletNearDestination();
            if (_labelRoot.gameObject.activeSelf != shouldShow)
                _labelRoot.gameObject.SetActive(shouldShow);
        }

        private bool IsTargetPalletNearDestination()
        {
            if (_targetPallet == null || !_targetPallet.gameObject.activeInHierarchy ||
                !TryGetPalletWorldBounds(_targetPallet, out Bounds palletBounds))
                return false;

            Bounds destinationBounds = GetDestinationWorldBounds();
            destinationBounds.Expand(Vector3.Max(Vector3.zero, _destinationBoundsExtension));
            return destinationBounds.Intersects(palletBounds);
        }

        private bool TryGetPalletWorldBounds(PalletStackerLoad pallet, out Bounds bounds)
        {
            bounds = default;
            if (pallet == null) return false;

            Collider[] colliders = pallet.CollisionRoot.GetComponentsInChildren<Collider>(false);
            bool hasBounds = false;
            for (int index = 0; index < colliders.Length; index++)
            {
                Collider candidate = colliders[index];
                if (candidate == null || !candidate.enabled) continue;

                if (!hasBounds)
                {
                    bounds = candidate.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(candidate.bounds);
                }
            }

            return hasBounds;
        }

        private Bounds GetDestinationWorldBounds()
        {
            if (_destinationBounds == null)
                return new Bounds(transform.position, Vector3.zero);

            Transform boundsTransform = _destinationBounds.transform;
            Vector3 halfSize = _destinationBounds.size * 0.5f;
            Vector3 center = _destinationBounds.center;
            Vector3 firstCorner = boundsTransform.TransformPoint(center - halfSize);
            Bounds bounds = new(firstCorner, Vector3.zero);

            for (int corner = 1; corner < 8; corner++)
            {
                Vector3 localCorner = center + new Vector3(
                    (corner & 1) == 0 ? -halfSize.x : halfSize.x,
                    (corner & 2) == 0 ? -halfSize.y : halfSize.y,
                    (corner & 4) == 0 ? -halfSize.z : halfSize.z);
                bounds.Encapsulate(boundsTransform.TransformPoint(localCorner));
            }

            return bounds;
        }
    }
}
