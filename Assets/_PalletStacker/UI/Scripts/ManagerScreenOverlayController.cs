using System.Collections.Generic;
using ElectricPalletStackers.Gameplay;
using ElectricPalletStackers.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricPalletStackers.UI
{
    [DisallowMultipleComponent]
    public sealed class ManagerScreenOverlayController : MonoBehaviour
    {
        [Header("Scene Services")]
        [SerializeField] private UIController _uiController;
        [SerializeField] private LocalizationManager _localizationManager;
        [SerializeField] private AppManager _appManager;
        [SerializeField] private TrainingCheckpointController _checkpointController;

        [Header("Panel")]
        [SerializeField] private GameObject _modalRoot;

        [Header("Buttons")]
        [SerializeField] private Button _settingsButton;
        [SerializeField] private Button _backdropButton;
        [SerializeField] private Button _closeButton;
        [SerializeField] private Button _resetStateButton;
        [SerializeField] private Button _resetGameButton;
        [SerializeField] private Michsky.UI.Reach.Dropdown _languageDropdown;

        [Header("Labels")]
        [SerializeField] private TMP_Text _titleLabel;
        [SerializeField] private TMP_Text _resetStateLabel;
        [SerializeField] private TMP_Text _resetGameLabel;

        private readonly Dictionary<TMP_Text, TMP_FontAsset> _defaultFonts = new();
        private bool _isOpen;

        private void Awake()
        {
            ResolveReferences();
            _localizationManager?.Initialize();
            CacheDefaultFonts();
            SetPanelOpen(false);
            RefreshLocalizedText();
            RefreshResetStateButton();
        }

        private void ResolveReferences()
        {
            _uiController ??= FindFirstObjectByType<UIController>();
            _localizationManager ??= FindFirstObjectByType<LocalizationManager>();
            _appManager ??= FindFirstObjectByType<AppManager>();
            _checkpointController ??= FindFirstObjectByType<TrainingCheckpointController>();

            _modalRoot ??= FindChild("Modal Root")?.gameObject;
            _settingsButton ??= GetChildComponent<Button>("Manager Hover Target/Settings Button");
            _backdropButton ??= GetChildComponent<Button>("Modal Root/Click Outside To Close");
            _closeButton ??= GetChildComponent<Button>("Modal Root/Manager Panel/Close Button");
            _resetStateButton ??= GetChildComponent<Button>(
                "Modal Root/Manager Panel/Vertical Actions/Reset State");
            _resetGameButton ??= GetChildComponent<Button>(
                "Modal Root/Manager Panel/Vertical Actions/Reset Game");
            _languageDropdown ??= GetChildComponent<Michsky.UI.Reach.Dropdown>(
                "Modal Root/Manager Panel/Vertical Actions/Language Dropdown");
            _titleLabel ??= GetChildComponent<TMP_Text>("Modal Root/Manager Panel/Header");
            _resetStateLabel ??= GetChildComponent<TMP_Text>(
                "Modal Root/Manager Panel/Vertical Actions/Reset State/Label");
            _resetGameLabel ??= GetChildComponent<TMP_Text>(
                "Modal Root/Manager Panel/Vertical Actions/Reset Game/Label");
        }

        private Transform FindChild(string path)
        {
            return transform.Find(path);
        }

        private T GetChildComponent<T>(string path) where T : Component
        {
            return FindChild(path)?.GetComponent<T>();
        }

        private void OnEnable()
        {
            if (_settingsButton != null) _settingsButton.onClick.AddListener(TogglePanel);
            if (_backdropButton != null) _backdropButton.onClick.AddListener(ClosePanel);
            if (_closeButton != null) _closeButton.onClick.AddListener(ClosePanel);
            if (_resetStateButton != null) _resetStateButton.onClick.AddListener(ResetCurrentPhase);
            if (_resetGameButton != null) _resetGameButton.onClick.AddListener(RestartGame);
            if (_languageDropdown != null) _languageDropdown.onValueChanged.AddListener(HandleLanguageSelected);

            if (_localizationManager != null)
                _localizationManager.LanguageChanged += HandleLanguageChanged;

            RefreshLocalizedText();
            RefreshResetStateButton();
        }

        private void OnDisable()
        {
            if (_settingsButton != null) _settingsButton.onClick.RemoveListener(TogglePanel);
            if (_backdropButton != null) _backdropButton.onClick.RemoveListener(ClosePanel);
            if (_closeButton != null) _closeButton.onClick.RemoveListener(ClosePanel);
            if (_resetStateButton != null) _resetStateButton.onClick.RemoveListener(ResetCurrentPhase);
            if (_resetGameButton != null) _resetGameButton.onClick.RemoveListener(RestartGame);
            if (_languageDropdown != null) _languageDropdown.onValueChanged.RemoveListener(HandleLanguageSelected);

            if (_localizationManager != null)
                _localizationManager.LanguageChanged -= HandleLanguageChanged;
        }

        private void Update()
        {
            RefreshResetStateButton();
        }

        private void TogglePanel()
        {
            if (_uiController != null && _uiController.IsBlackoutTransitionInProgress) return;
            SetPanelOpen(!_isOpen);
        }

        private void ClosePanel()
        {
            SetPanelOpen(false);
        }

        private void ResetCurrentPhase()
        {
            if (!CanResetCurrentPhase()) return;
            SetPanelOpen(false);
            _uiController?.RequestPhaseResetWithBlackout();
        }

        private void RestartGame()
        {
            SetPanelOpen(false);
            _uiController?.RequestGameRestartFromManager();
        }

        private void HandleLanguageSelected(int index)
        {
            ChangeLanguage(index switch
            {
                0 => LanguageCode.En,
                1 => LanguageCode.Vi,
                2 => LanguageCode.Ja,
                _ => LanguageCode.En
            });
        }

        private void ChangeLanguage(LanguageCode language)
        {
            if (_uiController != null)
                _uiController.ChangeLanguageFromManager(language);
            else
                _localizationManager?.SetLanguage(language);
        }

        private bool CanResetCurrentPhase()
        {
            if (_uiController != null && _uiController.IsBlackoutTransitionInProgress) return false;
            if (_checkpointController == null || !_checkpointController.HasCheckpoint || _appManager == null)
                return false;

            return _appManager.CurrentState == GameState.Playing ||
                   _appManager.CurrentState == GameState.Lost;
        }

        private void RefreshResetStateButton()
        {
            if (_resetStateButton != null)
                _resetStateButton.interactable = CanResetCurrentPhase();
        }

        private void SetPanelOpen(bool open)
        {
            _isOpen = open;

            if (_modalRoot != null && _modalRoot.activeSelf != open)
                _modalRoot.SetActive(open);

            if (open && _localizationManager != null)
                SyncLanguageDropdown(_localizationManager.CurrentLanguage);

            Transform hoverTarget = _settingsButton != null ? _settingsButton.transform.parent : null;
            if (hoverTarget != null && hoverTarget.gameObject.activeSelf == open)
                hoverTarget.gameObject.SetActive(!open);
        }

        private void SyncLanguageDropdown(LanguageCode language)
        {
            if (_languageDropdown == null) return;

            int index = language switch
            {
                LanguageCode.En => 0,
                LanguageCode.Vi => 1,
                LanguageCode.Ja => 2,
                _ => 0
            };
            _languageDropdown.SetDropdownIndex(index);
        }

        private void HandleLanguageChanged(LanguageCode language)
        {
            RefreshLocalizedText();
            if (_languageDropdown != null && _languageDropdown.gameObject.activeInHierarchy)
                SyncLanguageDropdown(language);
        }

        private void RefreshLocalizedText()
        {
            if (_localizationManager == null) return;
            _localizationManager.Initialize();

            ApplyText(_titleLabel, LocalizationKeys.Manager.Title);
            ApplyText(_resetStateLabel, LocalizationKeys.Manager.ResetState);
            ApplyText(_resetGameLabel, LocalizationKeys.Manager.ResetGame);
        }

        private void CacheDefaultFonts()
        {
            CacheDefaultFont(_titleLabel);
            CacheDefaultFont(_resetStateLabel);
            CacheDefaultFont(_resetGameLabel);
        }

        private void CacheDefaultFont(TMP_Text label)
        {
            if (label != null && !_defaultFonts.ContainsKey(label))
                _defaultFonts.Add(label, label.font);
        }

        private void ApplyText(TMP_Text label, string key)
        {
            if (label == null || _localizationManager == null) return;
            label.text = _localizationManager.GetText(key);

            TMP_FontAsset overrideFont = _localizationManager.Service?.GetFontOverride();
            label.font = overrideFont != null
                ? overrideFont
                : _defaultFonts.TryGetValue(label, out TMP_FontAsset defaultFont)
                    ? defaultFont
                    : label.font;
        }
    }
}
