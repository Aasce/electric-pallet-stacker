using System;
using ElectricPalletStackers.Localization;
using ElectricPalletStackers.SpatialAnchors;
using UnityEngine;

namespace ElectricPalletStackers.UI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(LocalizationManager))]
    public sealed class UIController : MonoBehaviour
    {
        [Header("Services")]
        [SerializeField] private LocalizationManager _localizationManager;
        [SerializeField] private PersistentSpatialAnchorRuntime _spatialAnchorRuntime;

        [Header("UI Flow")]
        [SerializeField] private UIInputRouter _inputRouter;
        [SerializeField] private UISelectLanguagePanel _selectLanguagePanel;
        [SerializeField] private UIWelcomePanel _welcomePanel;
        [SerializeField] private UIGuidePanel _guidePanel;
        [SerializeField] private UICompletedPanel _completedPanel;
        [SerializeField] private UIFailedPanel _failedPanel;

        public UIFlowState CurrentState { get; private set; } = UIFlowState.SpatialAnchorSetup;
        public bool IsInputCaptured => CurrentState != UIFlowState.Hidden;
        public LanguageCode FocusedLanguage { get; private set; } = LanguageCode.En;
        public LanguageCode SelectedLanguage { get; private set; } = LanguageCode.En;

        public event Action<UIFlowState> PanelChanged;
        public event Action<bool> InputCaptureChanged;
        public event Action<LanguageCode> LanguagePreviewChanged;
        public event Action<LanguageCode> LanguageConfirmed;
        /// <summary>
        /// Placeholder hook for systems that need to reset transient state before a new run.
        /// </summary>
        public event Action ResetRequested;

        private void Awake()
        {
            if (_localizationManager == null)
                _localizationManager = GetComponent<LocalizationManager>();
            if (_localizationManager == null)
                _localizationManager = gameObject.AddComponent<LocalizationManager>();
            _localizationManager.Initialize();

            if (_spatialAnchorRuntime == null)
                _spatialAnchorRuntime = GetComponent<PersistentSpatialAnchorRuntime>();
            if (_spatialAnchorRuntime == null)
                _spatialAnchorRuntime = gameObject.AddComponent<PersistentSpatialAnchorRuntime>();
            _spatialAnchorRuntime.ReadyForApp += HandleSpatialAnchorReady;
            _spatialAnchorRuntime.PlacementStarted += HandleSpatialAnchorPlacementStarted;

            if (_selectLanguagePanel != null)
                _selectLanguagePanel.FocusChanged += HandleLanguageFocusChanged;

            _selectLanguagePanel?.HideImmediate();
            _welcomePanel?.HideImmediate();
            _guidePanel?.HideImmediate();
            _completedPanel?.HideImmediate();
            _failedPanel?.HideImmediate();

            CurrentState = UIFlowState.SpatialAnchorSetup;
            _inputRouter?.ResetForPanel();
            if (_selectLanguagePanel != null)
                FocusedLanguage = _selectLanguagePanel.FocusedLanguage;
        }

        private void OnEnable()
        {
            if (_inputRouter == null) return;
            _inputRouter.NavigateNext += HandleNavigateNext;
            _inputRouter.NavigatePrevious += HandleNavigatePrevious;
            _inputRouter.Confirm += HandleConfirm;
        }

        private void OnDisable()
        {
            if (_inputRouter == null) return;
            _inputRouter.NavigateNext -= HandleNavigateNext;
            _inputRouter.NavigatePrevious -= HandleNavigatePrevious;
            _inputRouter.Confirm -= HandleConfirm;
        }

        private void OnDestroy()
        {
            if (_selectLanguagePanel != null)
                _selectLanguagePanel.FocusChanged -= HandleLanguageFocusChanged;
            if (_spatialAnchorRuntime != null)
            {
                _spatialAnchorRuntime.ReadyForApp -= HandleSpatialAnchorReady;
                _spatialAnchorRuntime.PlacementStarted -= HandleSpatialAnchorPlacementStarted;
            }
        }

        private void HandleNavigateNext()
        {
            if (CurrentState == UIFlowState.SelectLanguage)
                _selectLanguagePanel?.NavigateNext();
        }

        private void HandleNavigatePrevious()
        {
            if (CurrentState == UIFlowState.SelectLanguage)
                _selectLanguagePanel?.NavigatePrevious();
        }

        private void HandleConfirm()
        {
            switch (CurrentState)
            {
                case UIFlowState.SelectLanguage:
                    SelectedLanguage = FocusedLanguage;
                    LanguageConfirmed?.Invoke(SelectedLanguage);
                    TransitionTo(UIFlowState.Welcome);
                    break;
                case UIFlowState.Welcome:
                    TransitionTo(UIFlowState.Guide);
                    break;
                case UIFlowState.Guide:
                    TransitionTo(UIFlowState.Hidden);
                    break;
                case UIFlowState.Completed:
                case UIFlowState.Failed:
                    ResetRequested?.Invoke();
                    TransitionTo(UIFlowState.Welcome);
                    break;
            }
        }

        public void ShowCompleted()
        {
            TransitionTo(UIFlowState.Completed);
        }

        public void ShowFailed()
        {
            TransitionTo(UIFlowState.Failed);
        }

        private void HandleLanguageFocusChanged(LanguageCode language)
        {
            FocusedLanguage = language;
            _localizationManager?.SetLanguage(language);
            LanguagePreviewChanged?.Invoke(language);
        }

        private void HandleSpatialAnchorReady()
        {
            if (CurrentState == UIFlowState.SpatialAnchorSetup)
                TransitionTo(UIFlowState.SelectLanguage);
        }

        private void HandleSpatialAnchorPlacementStarted()
        {
            if (CurrentState != UIFlowState.SpatialAnchorSetup)
                TransitionTo(UIFlowState.SpatialAnchorSetup);
        }

        private void TransitionTo(UIFlowState nextState)
        {
            bool capturedBefore = IsInputCaptured;

            _selectLanguagePanel?.Hide();
            _welcomePanel?.Hide();
            _guidePanel?.Hide();
            _completedPanel?.Hide();
            _failedPanel?.Hide();

            CurrentState = nextState;
            _inputRouter?.ResetForPanel();
            _spatialAnchorRuntime?.SetReplacementAllowed(nextState != UIFlowState.Hidden &&
                                                        nextState != UIFlowState.SpatialAnchorSetup);

            switch (nextState)
            {
                case UIFlowState.SpatialAnchorSetup:
                    break;
                case UIFlowState.SelectLanguage:
                    _selectLanguagePanel?.Show();
                    break;
                case UIFlowState.Welcome:
                    _welcomePanel?.Show();
                    break;
                case UIFlowState.Guide:
                    _guidePanel?.Show();
                    break;
                case UIFlowState.Completed:
                    _completedPanel?.Show();
                    break;
                case UIFlowState.Failed:
                    _failedPanel?.Show();
                    break;
            }

            PanelChanged?.Invoke(nextState);
            if (capturedBefore != IsInputCaptured)
                InputCaptureChanged?.Invoke(IsInputCaptured);
        }
    }
}
