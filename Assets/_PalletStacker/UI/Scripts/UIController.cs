using System;
using ElectricPalletStackers.Calibration;
using ElectricPalletStackers.Localization;
using UnityEngine;

namespace ElectricPalletStackers.UI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(LocalizationManager))]
    public sealed class UIController : MonoBehaviour
    {
        [Header("Services")]
        [SerializeField] private LocalizationManager _localizationManager;

        [Header("UI Flow")]
        [SerializeField] private UIInputRouter _inputRouter;
        [SerializeField] private UISelectLanguagePanel _selectLanguagePanel;
        [SerializeField] private CalibrationInputPanel _calibrationPanel;
        [SerializeField] private VehicleViewCalibrationSystem _calibrationSystem;
        [SerializeField] private UIWelcomePanel _welcomePanel;
        [SerializeField] private UIGuidePanel _guidePanel;
        [SerializeField] private UICompletedPanel _completedPanel;
        [SerializeField] private UIFailedPanel _failedPanel;

        public UIFlowState CurrentState { get; private set; } = UIFlowState.CalibrateVehicleView;
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
            if (_selectLanguagePanel != null)
                _selectLanguagePanel.FocusChanged += HandleLanguageFocusChanged;
            if (_guidePanel != null)
                _guidePanel.Confirmed += ConfirmCurrentPanel;
            if (_calibrationPanel != null)
            {
                _calibrationPanel.AlignRequested += HandleAlignRequested;
                _calibrationPanel.OffsetAdjustmentRequested += HandleOffsetAdjustmentRequested;
            }
            _selectLanguagePanel?.HideImmediate();
            _calibrationPanel?.HideImmediate();
            _welcomePanel?.HideImmediate();
            _guidePanel?.HideImmediate();
            _completedPanel?.HideImmediate();
            _failedPanel?.HideImmediate();

            CurrentState = UIFlowState.CalibrateVehicleView;
            _inputRouter?.ResetForPanel();
            if (CurrentState == UIFlowState.CalibrateVehicleView) _calibrationPanel?.Show();
            else _selectLanguagePanel?.Show();
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
            if (_guidePanel != null)
                _guidePanel.Confirmed -= ConfirmCurrentPanel;
            if (_calibrationPanel != null)
            {
                _calibrationPanel.AlignRequested -= HandleAlignRequested;
                _calibrationPanel.OffsetAdjustmentRequested -= HandleOffsetAdjustmentRequested;
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
            ConfirmCurrentPanel();
        }

        public void ConfirmCurrentPanel()
        {
            switch (CurrentState)
            {
                case UIFlowState.CalibrateVehicleView:
                    TransitionTo(UIFlowState.SelectLanguage);
                    break;
                case UIFlowState.SelectLanguage:
                    SelectedLanguage = FocusedLanguage;
                    LanguageConfirmed?.Invoke(SelectedLanguage);
                    TransitionTo(UIFlowState.Welcome);
                    break;
                case UIFlowState.Welcome:
                    TransitionTo(UIFlowState.PpePreparation);
                    break;
                case UIFlowState.PpePreparation:
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

        private void HandleAlignRequested() => _calibrationSystem?.AlignToVehicle();
        private void HandleOffsetAdjustmentRequested(Vector2 direction) => _calibrationSystem?.AdjustOffset(direction);
        private void TransitionTo(UIFlowState nextState)
        {
            bool capturedBefore = IsInputCaptured;

            _selectLanguagePanel?.Hide();
            _calibrationPanel?.Hide();
            _welcomePanel?.Hide();
            _guidePanel?.Hide();
            _completedPanel?.Hide();
            _failedPanel?.Hide();

            CurrentState = nextState;
            _inputRouter?.ResetForPanel();

            switch (nextState)
            {
                case UIFlowState.CalibrateVehicleView:
                    _calibrationPanel?.Show();
                    break;
                case UIFlowState.SelectLanguage:
                    _selectLanguagePanel?.Show();
                    break;
                case UIFlowState.Welcome:
                    _welcomePanel?.Show();
                    break;
                case UIFlowState.PpePreparation:
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
