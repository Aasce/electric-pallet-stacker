using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricPalletStackers.UI
{
    /// <summary>
    /// Prefab-authored world-space warning that stays directly in front of the
    /// player's view and renders directional proximity indicators around its center.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CollisionWarningHudView : MonoBehaviour
    {
        private const int MaximumDirectionArrows = 3;

        [SerializeField] private Canvas _canvas;
        [SerializeField] private RectTransform _warningPanel;
        [SerializeField] private RectTransform _arrowsRoot;

        [Header("World Placement")]
        [Tooltip("Camera-local position. X/Y = 0 keeps the warning centered between the eyes.")]
        [SerializeField] private Vector3 _viewOffset = new(0f, 0f, 1.2f);

        [Header("Direction Indicators")]
        [SerializeField] private Image[] _directionArrows = new Image[3];
        [SerializeField, Min(0f)] private float _indicatorRadius = 200f;
        [SerializeField] private Vector2 _indicatorSize = new(100f, 100f);
        [SerializeField, Range(0f, 360f)] private float _farHueDegrees = 60f;
        [SerializeField, Range(0f, 360f)] private float _nearHueDegrees = 10f;

        private Camera _viewCamera;

        private void Awake()
        {
            ResolveReferences();
            ConfigureCanvas();
            ApplyIndicatorLayout();
            SetVisible(false);
        }

        private void LateUpdate()
        {
            FollowView();
        }

        private void OnValidate()
        {
            ResolveReferences();
            _indicatorRadius = Mathf.Max(0f, _indicatorRadius);
            _indicatorSize.x = Mathf.Max(1f, _indicatorSize.x);
            _indicatorSize.y = Mathf.Max(1f, _indicatorSize.y);
            ConfigureCanvas();
            CenterPanel();
            ApplyIndicatorLayout();
        }

        public void ConfigureFor(Camera viewCamera)
        {
            if (viewCamera == null) return;

            _viewCamera = viewCamera;
            ConfigureCanvas();
            FollowView();
        }

        public void SetWarnings(
            IReadOnlyList<Vector2> directions,
            IReadOnlyList<float> proximities)
        {
            ResolveReferences();
            int count = Mathf.Min(
                _directionArrows?.Length ?? 0,
                Mathf.Min(directions?.Count ?? 0, proximities?.Count ?? 0));

            SetVisible(count > 0);
            if (_directionArrows == null) return;

            for (int index = 0; index < _directionArrows.Length; index++)
            {
                Image arrow = _directionArrows[index];
                if (arrow == null) continue;

                bool isVisible = index < count;
                arrow.gameObject.SetActive(isVisible);
                if (!isVisible) continue;

                Vector2 direction = directions[index].normalized;
                RectTransform arrowRect = arrow.rectTransform;
                arrowRect.sizeDelta = _indicatorSize;
                arrowRect.anchoredPosition = direction * _indicatorRadius;
                arrowRect.localEulerAngles = new Vector3(
                    0f,
                    0f,
                    Vector2.SignedAngle(Vector2.up, direction));

                float proximity = Mathf.Clamp01(proximities[index]);
                Color.RGBToHSV(arrow.color, out _, out float saturation, out float value);
                Color color = Color.HSVToRGB(
                    Mathf.Lerp(_farHueDegrees, _nearHueDegrees, proximity) / 360f,
                    saturation,
                    value);
                color.a = arrow.color.a;
                arrow.color = color;
            }
        }

        public void SetVisible(bool visible)
        {
            if (_warningPanel != null && _warningPanel.gameObject.activeSelf != visible)
                _warningPanel.gameObject.SetActive(visible);
            if (_arrowsRoot != null && _arrowsRoot.gameObject.activeSelf != visible)
                _arrowsRoot.gameObject.SetActive(visible);
        }

        private void ConfigureCanvas()
        {
            if (_canvas == null) return;
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.worldCamera = _viewCamera;
            _canvas.overrideSorting = true;
            _canvas.sortingOrder = short.MaxValue;
        }

        private void FollowView()
        {
            if (_viewCamera == null) return;

            Transform view = _viewCamera.transform;
            transform.SetPositionAndRotation(
                view.TransformPoint(_viewOffset),
                view.rotation);
        }

        private void CenterPanel()
        {
            if (_warningPanel == null) return;
            _warningPanel.anchorMin = new Vector2(0.5f, 0.5f);
            _warningPanel.anchorMax = new Vector2(0.5f, 0.5f);
            _warningPanel.pivot = new Vector2(0.5f, 0.5f);
            _warningPanel.anchoredPosition = Vector2.zero;
            _warningPanel.localRotation = Quaternion.identity;
        }

        private void ApplyIndicatorLayout()
        {
            if (_directionArrows == null) return;

            foreach (Image arrow in _directionArrows)
            {
                if (arrow == null) continue;
                arrow.raycastTarget = false;
                arrow.preserveAspect = true;
                arrow.rectTransform.sizeDelta = _indicatorSize;
            }
        }

        private void ResolveReferences()
        {
            if (_canvas == null) _canvas = GetComponent<Canvas>();

            if (_warningPanel == null)
                _warningPanel = transform.Find("Warning Panel") as RectTransform;
            if (_arrowsRoot == null)
                _arrowsRoot = transform.Find("Arrows") as RectTransform;

            bool needsArrowLookup = _directionArrows == null || _directionArrows.Length == 0;
            if (!needsArrowLookup)
            {
                foreach (Image arrow in _directionArrows)
                {
                    if (arrow != null) continue;
                    needsArrowLookup = true;
                    break;
                }
            }

            if (!needsArrowLookup || _arrowsRoot == null) return;

            Image[] foundArrows = _arrowsRoot.GetComponentsInChildren<Image>(true);
            int arrowCount = Mathf.Min(MaximumDirectionArrows, foundArrows.Length);
            _directionArrows = new Image[arrowCount];
            for (int index = 0; index < arrowCount; index++)
                _directionArrows[index] = foundArrows[index];
        }
    }
}
