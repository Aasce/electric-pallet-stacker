using ElectricPalletStackers.Localization;
using UnityEngine;

namespace ElectricPalletStackers.UI
{
    /// <summary>
    /// Owns the shared, camera-relative notification panel used by vehicle
    /// safety interlocks. Reusing one instance prevents warnings from stacking.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NotificationPanelPresenter : MonoBehaviour
    {
        [SerializeField] private GameObject _notificationPrefab;
        [SerializeField, Min(0.1f)] private float _holdSeconds = 2.5f;

        private GameObject _instance;
        private LocalizationManager _localizationManager;
        private LocalizedText _localizedText;
        private float _lastShownTime = float.NegativeInfinity;

        private void OnDisable()
        {
            Hide();
        }

        private void OnDestroy()
        {
            if (_localizationManager != null && _localizedText != null)
                _localizationManager.Unregister(_localizedText);
            if (_instance != null) Destroy(_instance);
        }

        private void LateUpdate()
        {
            if (_instance == null ||
                !_instance.activeSelf ||
                Time.unscaledTime - _lastShownTime <= _holdSeconds)
                return;

            Hide();
        }

        public void Show(string localizationKey)
        {
            if (string.IsNullOrWhiteSpace(localizationKey)) return;

            EnsureInstance();
            if (_instance == null) return;

            Camera viewCamera = Camera.main;
            if (viewCamera != null)
            {
                Canvas canvas = _instance.GetComponent<Canvas>();
                if (canvas != null) canvas.worldCamera = viewCamera;

                TargetFollower follower = _instance.GetComponent<TargetFollower>();
                follower?.SetTarget(viewCamera.transform);
            }

            _localizedText?.SetKey(
                localizationKey,
                _localizationManager != null ? _localizationManager.Service : null);
            _lastShownTime = Time.unscaledTime;
            if (!_instance.activeSelf) _instance.SetActive(true);
        }

        public void Hide()
        {
            if (_instance != null && _instance.activeSelf) _instance.SetActive(false);
        }

        private void EnsureInstance()
        {
            if (_instance != null || _notificationPrefab == null) return;

            _instance = Instantiate(_notificationPrefab);
            _instance.name = "Vehicle Safety Notification Panel";

            Camera viewCamera = Camera.main;
            Canvas canvas = _instance.GetComponent<Canvas>();
            if (canvas != null)
            {
                canvas.worldCamera = viewCamera;
                canvas.overrideSorting = true;
                canvas.sortingOrder = short.MaxValue - 5;
            }

            TargetFollower follower = _instance.GetComponent<TargetFollower>();
            if (follower != null && viewCamera != null)
                follower.SetTarget(viewCamera.transform);

            _localizedText = _instance.GetComponentInChildren<LocalizedText>(true);
            _localizationManager = FindFirstObjectByType<LocalizationManager>();
            if (_localizedText != null) _localizationManager?.Register(_localizedText);

            _instance.SetActive(false);
        }
    }
}
