using System;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricPalletStackers.UI
{
    [DisallowMultipleComponent]
    public sealed class UIGuidePanel : UIPanel
    {
        [SerializeField] private ScrollRect _scrollRect;
        [SerializeField] private UIInputRouter _inputRouter;
        [SerializeField] private Button _okButton;
        [SerializeField, Min(0f)] private float _scrollSpeed = 0.65f;

        [Header("PPE Equipment")]
        [SerializeField] private GameObject _equipmentPrefab;
        [SerializeField] private Camera _previewCamera;
        [SerializeField] private Vector3 _previewLocalPosition = new(0.3f, -0.08f, 0.8f);
        [SerializeField] private Vector3 _previewLocalEulerAngles = new(10f, -25f, 0f);
        [SerializeField, Min(0.01f)] private float _previewSize = 0.3f;
        [SerializeField] private float _previewRotationSpeed = 24f;

        private GameObject _equipmentPreview;

        public event Action Confirmed;
        public bool IsEquipped { get; private set; }

        protected override void Awake()
        {
            base.Awake();
            if (_okButton != null) _okButton.onClick.AddListener(HandleOkClicked);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            RemoveEquipmentPreview();
        }

        private void OnDestroy()
        {
            if (_okButton != null) _okButton.onClick.RemoveListener(HandleOkClicked);
            RemoveEquipmentPreview();
        }

        private void Update()
        {
            if (_equipmentPreview != null && !Mathf.Approximately(_previewRotationSpeed, 0f))
                _equipmentPreview.transform.Rotate(
                    Vector3.up,
                    _previewRotationSpeed * Time.unscaledDeltaTime,
                    Space.Self);

            if (_scrollRect == null || _inputRouter == null) return;

            float position = _scrollRect.verticalNormalizedPosition +
                             _inputRouter.LatestTravelAxis * _scrollSpeed * Time.unscaledDeltaTime;
            _scrollRect.verticalNormalizedPosition = Mathf.Clamp01(position);
        }

        public override void ResetView()
        {
            if (_scrollRect != null) _scrollRect.verticalNormalizedPosition = 1f;
            IsEquipped = false;
            if (_okButton != null) _okButton.interactable = true;
            RemoveEquipmentPreview();
            CreateEquipmentPreview();
        }

        public void Equip()
        {
            if (IsEquipped) return;

            IsEquipped = true;
            if (_okButton != null) _okButton.interactable = false;
            RemoveEquipmentPreview();
            Confirmed?.Invoke();
        }

        private void HandleOkClicked() => Equip();

        private void CreateEquipmentPreview()
        {
            if (_equipmentPrefab == null) return;

            Camera camera = _previewCamera != null ? _previewCamera : Camera.main;
            if (camera == null)
            {
                Debug.LogWarning("Cannot show the PPE preview because no preview camera is available.", this);
                return;
            }

            _equipmentPreview = Instantiate(_equipmentPrefab, camera.transform);
            _equipmentPreview.name = $"{_equipmentPrefab.name} (PPE Preview)";
            Transform previewTransform = _equipmentPreview.transform;
            previewTransform.localPosition = _previewLocalPosition;
            previewTransform.localRotation = Quaternion.Euler(_previewLocalEulerAngles);
            NormalizePreviewSize();

            foreach (Collider previewCollider in _equipmentPreview.GetComponentsInChildren<Collider>(true))
                previewCollider.enabled = false;
            foreach (Rigidbody previewBody in _equipmentPreview.GetComponentsInChildren<Rigidbody>(true))
            {
                previewBody.isKinematic = true;
                previewBody.detectCollisions = false;
            }
        }

        private void NormalizePreviewSize()
        {
            Renderer[] renderers = _equipmentPreview.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return;

            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
                bounds.Encapsulate(renderers[index].bounds);

            float largestDimension = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (largestDimension <= Mathf.Epsilon) return;

            _equipmentPreview.transform.localScale *= _previewSize / largestDimension;
        }

        private void RemoveEquipmentPreview()
        {
            if (_equipmentPreview == null) return;

            if (Application.isPlaying) Destroy(_equipmentPreview);
            else DestroyImmediate(_equipmentPreview);
            _equipmentPreview = null;
        }
    }
}
