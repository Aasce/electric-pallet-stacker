using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace ElectricPalletStackers.PalletStackers
{
    /// <summary>
    /// Detects environment colliders around the vehicle and displays their horizontal
    /// directions on a camera-space HUD. The HUD is rendered by a URP overlay camera so
    /// world geometry can never occlude it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PalletStackerProximityWarning : MonoBehaviour
    {
        private const string AlwaysOnTopLayerName = "Always On Top";
        private const string OverlayCameraName = "Always On Top UI Camera";
        private const float SameDirectionDotThreshold = 0.94f;

        [Header("Detection")]
        [SerializeField] private BoxCollider _warningTrigger;
        [SerializeField] private LayerMask _environmentLayers = 1 << 8;

        [Header("HUD")]
        [SerializeField, Range(1, 3)] private int _maximumWarnings = 3;
        [SerializeField, Range(0.05f, 0.48f)] private float _screenRadius = 0.34f;
        [SerializeField] private Vector2 _warningImageSize = new(56f, 84f);
        [SerializeField] private Color _warningColor = new(1f, 0.35f, 0.08f, 0.9f);

        private readonly HashSet<Collider> _contacts = new();
        private readonly List<WarningCandidate> _candidates = new();
        private readonly List<Vector2> _visibleDirections = new(3);
        private CollisionWarningHud _hud;

        private readonly struct WarningCandidate
        {
            public WarningCandidate(Vector2 direction, float distanceSquared)
            {
                Direction = direction;
                DistanceSquared = distanceSquared;
            }

            public Vector2 Direction { get; }
            public float DistanceSquared { get; }
        }

        private void Awake()
        {
            ResolveTrigger();
        }

        private void LateUpdate()
        {
            if (_warningTrigger == null) return;

            Camera viewCamera = Camera.main;
            if (viewCamera == null)
            {
                _hud?.HideAll();
                return;
            }

            if (_hud == null || _hud.BaseCamera != viewCamera)
            {
                _hud?.Destroy();
                _hud = CollisionWarningHud.Create(
                    viewCamera,
                    _maximumWarnings,
                    _warningImageSize,
                    _warningColor,
                    AlwaysOnTopLayerName,
                    OverlayCameraName);
            }

            if (_hud == null) return;

            RemoveInvalidContacts();
            BuildVisibleDirections(viewCamera);
            _hud.SetDirections(_visibleDirections, _screenRadius);
        }

        private void OnDisable()
        {
            _contacts.Clear();
            _hud?.HideAll();
        }

        private void OnDestroy()
        {
            _hud?.Destroy();
            _hud = null;
        }

        private void OnTriggerEnter(Collider other)
        {
            Track(other);
        }

        private void OnTriggerStay(Collider other)
        {
            // Rebuild the contact set after a domain reload while objects are already overlapping.
            Track(other);
        }

        private void OnTriggerExit(Collider other)
        {
            if (other != null) _contacts.Remove(other);
        }

        private void OnValidate()
        {
            _maximumWarnings = Mathf.Clamp(_maximumWarnings, 1, 3);
            _screenRadius = Mathf.Clamp(_screenRadius, 0.05f, 0.48f);
            _warningImageSize.x = Mathf.Max(1f, _warningImageSize.x);
            _warningImageSize.y = Mathf.Max(1f, _warningImageSize.y);

            ResolveTrigger();
            if (_warningTrigger != null) _warningTrigger.isTrigger = true;
        }

        private void OnDrawGizmosSelected()
        {
            if (_warningTrigger == null) return;

            Matrix4x4 oldMatrix = Gizmos.matrix;
            Color oldColor = Gizmos.color;
            Gizmos.matrix = _warningTrigger.transform.localToWorldMatrix;
            Gizmos.color = new Color(_warningColor.r, _warningColor.g, _warningColor.b, 0.35f);
            Gizmos.DrawWireCube(_warningTrigger.center, _warningTrigger.size);
            Gizmos.matrix = oldMatrix;
            Gizmos.color = oldColor;
        }

        private void ResolveTrigger()
        {
            if (_warningTrigger != null) return;

            BoxCollider[] colliders = GetComponents<BoxCollider>();
            foreach (BoxCollider boxCollider in colliders)
            {
                if (!boxCollider.isTrigger) continue;
                _warningTrigger = boxCollider;
                break;
            }
        }

        private void Track(Collider other)
        {
            if (other == null || other == _warningTrigger) return;
            int otherLayerMask = 1 << other.gameObject.layer;
            if ((_environmentLayers.value & otherLayerMask) == 0) return;
            _contacts.Add(other);
        }

        private void RemoveInvalidContacts()
        {
            _contacts.RemoveWhere(contact =>
                contact == null
                || !contact.enabled
                || !contact.gameObject.activeInHierarchy
                || (_environmentLayers.value & (1 << contact.gameObject.layer)) == 0);
        }

        private void BuildVisibleDirections(Camera viewCamera)
        {
            _candidates.Clear();
            _visibleDirections.Clear();
            if (_contacts.Count == 0) return;

            Vector3 origin = _warningTrigger.transform.TransformPoint(_warningTrigger.center);
            Vector3 cameraForward = Vector3.ProjectOnPlane(viewCamera.transform.forward, Vector3.up);
            Vector3 cameraRight = Vector3.ProjectOnPlane(viewCamera.transform.right, Vector3.up);

            if (cameraForward.sqrMagnitude < 0.0001f) cameraForward = transform.forward;
            if (cameraRight.sqrMagnitude < 0.0001f) cameraRight = transform.right;
            cameraForward.Normalize();
            cameraRight.Normalize();

            foreach (Collider contact in _contacts)
            {
                Vector3 closestPoint = contact.ClosestPoint(origin);
                Vector3 offset = closestPoint - origin;
                if (offset.sqrMagnitude < 0.0001f)
                    offset = contact.bounds.center - origin;

                Vector3 horizontalOffset = Vector3.ProjectOnPlane(offset, Vector3.up);
                if (horizontalOffset.sqrMagnitude < 0.0001f) continue;

                float distanceSquared = horizontalOffset.sqrMagnitude;
                horizontalOffset.Normalize();
                Vector2 screenDirection = new(
                    Vector3.Dot(horizontalOffset, cameraRight),
                    Vector3.Dot(horizontalOffset, cameraForward));
                if (screenDirection.sqrMagnitude < 0.0001f) continue;

                _candidates.Add(new WarningCandidate(screenDirection.normalized, distanceSquared));
            }

            _candidates.Sort((left, right) => left.DistanceSquared.CompareTo(right.DistanceSquared));
            int count = Mathf.Min(_maximumWarnings, 3);
            foreach (WarningCandidate candidate in _candidates)
            {
                bool duplicatesVisibleDirection = false;
                foreach (Vector2 visibleDirection in _visibleDirections)
                {
                    if (Vector2.Dot(candidate.Direction, visibleDirection) < SameDirectionDotThreshold) continue;
                    duplicatesVisibleDirection = true;
                    break;
                }

                if (duplicatesVisibleDirection) continue;
                _visibleDirections.Add(candidate.Direction);
                if (_visibleDirections.Count >= count) break;
            }
        }

        private sealed class CollisionWarningHud
        {
            private readonly GameObject _root;
            private readonly RectTransform _canvasRect;
            private readonly RectTransform[] _warningRects;

            private CollisionWarningHud(
                Camera baseCamera,
                GameObject root,
                RectTransform canvasRect,
                RectTransform[] warningRects)
            {
                BaseCamera = baseCamera;
                _root = root;
                _canvasRect = canvasRect;
                _warningRects = warningRects;
            }

            public Camera BaseCamera { get; }

            public static CollisionWarningHud Create(
                Camera baseCamera,
                int warningCount,
                Vector2 warningSize,
                Color warningColor,
                string layerName,
                string overlayCameraName)
            {
                int uiLayer = LayerMask.NameToLayer(layerName);
                if (uiLayer < 0)
                {
                    Debug.LogError($"Collision warning HUD requires a '{layerName}' layer.");
                    return null;
                }

                Camera overlayCamera = CreateOverlayCamera(baseCamera, uiLayer, overlayCameraName);
                if (overlayCamera == null) return null;

                GameObject canvasObject = new("Collision Warning Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
                canvasObject.layer = uiLayer;
                canvasObject.transform.SetParent(overlayCamera.transform, false);

                Canvas canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = overlayCamera;
                canvas.planeDistance = Mathf.Max(overlayCamera.nearClipPlane + 0.01f, 0.05f);
                canvas.overrideSorting = true;
                canvas.sortingOrder = short.MaxValue;

                CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f;

                RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
                RectTransform[] warningRects = new RectTransform[Mathf.Clamp(warningCount, 1, 3)];
                for (int index = 0; index < warningRects.Length; index++)
                {
                    GameObject warningObject = new($"Collision Warning {index + 1}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                    warningObject.layer = uiLayer;
                    warningObject.transform.SetParent(canvasRect, false);

                    RectTransform warningRect = warningObject.GetComponent<RectTransform>();
                    warningRect.anchorMin = new Vector2(0.5f, 0.5f);
                    warningRect.anchorMax = new Vector2(0.5f, 0.5f);
                    warningRect.pivot = new Vector2(0.5f, 0.5f);
                    warningRect.sizeDelta = warningSize;

                    Image warningImage = warningObject.GetComponent<Image>();
                    warningImage.sprite = null;
                    warningImage.color = warningColor;
                    warningImage.raycastTarget = false;
                    warningObject.SetActive(false);
                    warningRects[index] = warningRect;
                }

                return new CollisionWarningHud(baseCamera, canvasObject, canvasRect, warningRects);
            }

            public void SetDirections(IReadOnlyList<Vector2> directions, float normalizedRadius)
            {
                float width = _canvasRect.rect.width;
                float height = _canvasRect.rect.height;
                float radius = Mathf.Min(width, height) * normalizedRadius;
                if (radius <= 0f) radius = 1080f * normalizedRadius;

                for (int index = 0; index < _warningRects.Length; index++)
                {
                    RectTransform warningRect = _warningRects[index];
                    bool isVisible = index < directions.Count;
                    warningRect.gameObject.SetActive(isVisible);
                    if (!isVisible) continue;

                    Vector2 direction = directions[index];
                    warningRect.anchoredPosition = direction * radius;
                    warningRect.localEulerAngles = new Vector3(0f, 0f, Vector2.SignedAngle(Vector2.up, direction));
                }
            }

            public void HideAll()
            {
                foreach (RectTransform warningRect in _warningRects)
                    if (warningRect != null) warningRect.gameObject.SetActive(false);
            }

            public void Destroy()
            {
                if (_root != null) Object.Destroy(_root);
            }

            private static Camera CreateOverlayCamera(Camera baseCamera, int uiLayer, string cameraName)
            {
                Transform existing = baseCamera.transform.Find(cameraName);
                Camera overlayCamera = existing != null ? existing.GetComponent<Camera>() : null;

                if (overlayCamera == null)
                {
                    GameObject cameraObject = new(cameraName, typeof(Camera));
                    cameraObject.transform.SetParent(baseCamera.transform, false);
                    overlayCamera = cameraObject.GetComponent<Camera>();
                    overlayCamera.CopyFrom(baseCamera);
                }

                overlayCamera.gameObject.layer = uiLayer;
                overlayCamera.cullingMask = 1 << uiLayer;
                overlayCamera.clearFlags = CameraClearFlags.Nothing;
                overlayCamera.useOcclusionCulling = false;
                overlayCamera.enabled = true;

                UniversalAdditionalCameraData overlayData = overlayCamera.GetComponent<UniversalAdditionalCameraData>();
                if (overlayData == null)
                    overlayData = overlayCamera.gameObject.AddComponent<UniversalAdditionalCameraData>();
                overlayData.renderType = CameraRenderType.Overlay;
                overlayData.renderShadows = false;

                UniversalAdditionalCameraData baseData = baseCamera.GetComponent<UniversalAdditionalCameraData>();
                if (baseData == null)
                    baseData = baseCamera.gameObject.AddComponent<UniversalAdditionalCameraData>();
                if (!baseData.cameraStack.Contains(overlayCamera))
                    baseData.cameraStack.Add(overlayCamera);

                // The base camera renders the world; this dedicated layer is rendered only
                // by the final overlay camera, after depth from the world has been resolved.
                baseCamera.cullingMask &= ~(1 << uiLayer);
                return overlayCamera;
            }
        }
    }
}
