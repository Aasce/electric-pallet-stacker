using System;
using ElectricPalletStackers.Calibration;
using ElectricPalletStackers.Gameplay;
using ElectricPalletStackers.Gameplay.Checklist;
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
        [SerializeField] private GameFailureMessageCatalog _failureMessageCatalog;

        [Header("UI Flow")]
        [SerializeField] private UIInputRouter _inputRouter;
        [SerializeField] private UISelectLanguagePanel _selectLanguagePanel;
        [SerializeField] private UICalibrationPanel _calibrationInfoPanel;
        [SerializeField] private CalibrationInputPanel _calibrationPanel;
        [SerializeField] private VehicleViewCalibrationSystem _calibrationSystem;
        [SerializeField] private UIWelcomePanel _welcomePanel;
        [SerializeField] private UIGuidePanel _guidePanel;
        [SerializeField] private UIPpePanel _ppePanel;
        [SerializeField] private PpeEquipmentPhase _ppeEquipmentPhase;
        [SerializeField] private UIChecklistPanel _checklistPanel;
        [SerializeField] private VehicleChecklistController _vehicleChecklist;
        [SerializeField] private UICompletedPanel _completedPanel;
        [SerializeField] private UIFailedPanel _failedPanel;
        [SerializeField] private UIResetGamePanel _resetGamePanel;

        [Header("Flow Timing")]
        [SerializeField, Min(0f)] private float _welcomeNavigationDelay = 2f;

        private float _navigationInputUnlockedAt = float.NegativeInfinity;

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
        public event Action ContinueRequested;
        public event Action RestartGameRequested;
        public event Action ContinueGameRequested;
        public event Action ResetPhaseRequested;

        private void Awake()
        {
            if (_localizationManager == null)
            {
                Debug.LogError("UIController requires a LocalizationManager assigned in the Inspector.", this);
                enabled = false;
                return;
            }

            if (_failureMessageCatalog == null)
            {
                Debug.LogError("UIController requires a GameFailureMessageCatalog assigned in the Inspector.", this);
                enabled = false;
                return;
            }

            _localizationManager.Initialize();
            if (_selectLanguagePanel != null)
                _selectLanguagePanel.FocusChanged += HandleLanguageFocusChanged;
            if (_ppeEquipmentPhase != null)
                _ppeEquipmentPhase.Equipped += HandlePpeEquipped;
            _vehicleChecklist.Completed += HandleVehicleChecklistCompleted;
            if (_calibrationPanel != null)
            {
                _calibrationPanel.AlignRequested += HandleAlignRequested;
                _calibrationPanel.OffsetAdjustmentRequested += HandleOffsetAdjustmentRequested;
            }
            _failedPanel.ActionConfirmed += HandleFailureActionConfirmed;
            if (_resetGamePanel != null)
                _resetGamePanel.ActionConfirmed += HandleResetGameActionConfirmed;

            _selectLanguagePanel?.HideImmediate();
            _calibrationInfoPanel?.HideImmediate();
            _calibrationPanel?.HideImmediate();
            _welcomePanel?.HideImmediate();
            _guidePanel?.HideImmediate();
            _ppePanel?.HideImmediate();
            _checklistPanel?.HideImmediate();
            _ppeEquipmentPhase?.EndPhase();
            _vehicleChecklist.EndPhase();
            _completedPanel?.HideImmediate();
            _failedPanel.HideImmediate();
            _resetGamePanel?.HideImmediate();

            CurrentState = UIFlowState.CalibrateVehicleView;
            _inputRouter?.ResetForPanel();
            if (CurrentState == UIFlowState.CalibrateVehicleView)
            {
                _calibrationInfoPanel?.Show();
                _calibrationPanel?.Show();
            }
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
            _inputRouter.EmergencyStopHeld += HandleEmergencyStopHeld;
        }

        private void OnDisable()
        {
            if (_inputRouter == null) return;
            _inputRouter.NavigateNext -= HandleNavigateNext;
            _inputRouter.NavigatePrevious -= HandleNavigatePrevious;
            _inputRouter.Confirm -= HandleConfirm;
            _inputRouter.EmergencyStopHeld -= HandleEmergencyStopHeld;
        }

        private void OnDestroy()
        {
            if (_selectLanguagePanel != null)
                _selectLanguagePanel.FocusChanged -= HandleLanguageFocusChanged;
            if (_ppeEquipmentPhase != null)
                _ppeEquipmentPhase.Equipped -= HandlePpeEquipped;
            _vehicleChecklist.Completed -= HandleVehicleChecklistCompleted;
            if (_calibrationPanel != null)
            {
                _calibrationPanel.AlignRequested -= HandleAlignRequested;
                _calibrationPanel.OffsetAdjustmentRequested -= HandleOffsetAdjustmentRequested;
            }
            _failedPanel.ActionConfirmed -= HandleFailureActionConfirmed;
            if (_resetGamePanel != null)
                _resetGamePanel.ActionConfirmed -= HandleResetGameActionConfirmed;
        }

        private void HandleNavigateNext()
        {
            if (!CanProcessNavigationInput()) return;

            if (CurrentState == UIFlowState.SelectLanguage)
                _selectLanguagePanel.NavigateNext();
            else if (CurrentState == UIFlowState.Failed)
                _failedPanel.NavigateNext();
            else if (CurrentState == UIFlowState.ResetGame)
                _resetGamePanel.NavigateNext();
        }

        private void HandleNavigatePrevious()
        {
            if (!CanProcessNavigationInput()) return;

            if (CurrentState == UIFlowState.SelectLanguage)
                _selectLanguagePanel.NavigatePrevious();
            else if (CurrentState == UIFlowState.Failed)
                _failedPanel.NavigatePrevious();
            else if (CurrentState == UIFlowState.ResetGame)
                _resetGamePanel.NavigatePrevious();
        }

        private void HandleConfirm()
        {
            if (!CanProcessNavigationInput()) return;

            ConfirmCurrentPanel();
        }

        private bool CanProcessNavigationInput()
        {
            return CurrentState != UIFlowState.Welcome ||
                   Time.unscaledTime >= _navigationInputUnlockedAt;
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
                    TransitionTo(UIFlowState.Guide);
                    break;
                case UIFlowState.Guide:
                    TransitionTo(UIFlowState.PpeEquipment);
                    break;
                case UIFlowState.Completed:
                    ResetRequested?.Invoke();
                    TransitionTo(UIFlowState.Welcome);
                    break;
                case UIFlowState.Failed:
                    _failedPanel.ConfirmSelection();
                    break;
                case UIFlowState.ResetGame:
                    _resetGamePanel.ConfirmSelection();
                    break;
            }
        }

        public void ShowCompleted()
        {
            TransitionTo(UIFlowState.Completed);
        }

        public void ShowFailed(GameFailureReason reason)
        {
            _failedPanel.SetFailureReason(
                reason,
                _failureMessageCatalog,
                _localizationManager.CurrentLanguage,
                _localizationManager.Service);
            TransitionTo(UIFlowState.Failed);
        }

        private void HandleLanguageFocusChanged(LanguageCode language)
        {
            FocusedLanguage = language;
            _localizationManager?.SetLanguage(language);
            LanguagePreviewChanged?.Invoke(language);
        }

        private void HandlePpeEquipped()
        {
            if (CurrentState == UIFlowState.PpeEquipment)
                TransitionTo(UIFlowState.VehicleChecklist);
        }

        private void HandleVehicleChecklistCompleted()
        {
            if (CurrentState == UIFlowState.VehicleChecklist)
                TransitionTo(UIFlowState.Hidden);
        }

        private void HandleEmergencyStopHeld()
        {
            if (CurrentState == UIFlowState.Hidden)
                TransitionTo(UIFlowState.ResetGame);
        }

        private void HandleAlignRequested() => _calibrationSystem?.AlignToVehicle();
        private void HandleOffsetAdjustmentRequested(Vector3 direction) => _calibrationSystem?.AdjustOffset(direction);
        private void TransitionTo(UIFlowState nextState)
        {
            bool capturedBefore = IsInputCaptured;

            _selectLanguagePanel?.Hide();
            _calibrationInfoPanel?.Hide();
            _calibrationPanel?.Hide();
            _welcomePanel?.Hide();
            _guidePanel?.Hide();
            _ppePanel?.Hide();
            _checklistPanel?.Hide();
            _ppeEquipmentPhase?.EndPhase();
            _vehicleChecklist.EndPhase();
            _completedPanel?.Hide();
            _failedPanel?.Hide();
            _resetGamePanel?.Hide();

            CurrentState = nextState;
            _navigationInputUnlockedAt = nextState == UIFlowState.Welcome
                ? Time.unscaledTime + _welcomeNavigationDelay
                : float.NegativeInfinity;
            _inputRouter?.ResetForPanel();

            switch (nextState)
            {
                case UIFlowState.CalibrateVehicleView:
                    _calibrationInfoPanel?.Show();
                    _calibrationPanel?.Show();
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
                case UIFlowState.PpeEquipment:
                    _ppePanel?.Show();
                    _ppeEquipmentPhase?.BeginPhase();
                    break;
                case UIFlowState.VehicleChecklist:
                    _checklistPanel?.Show();
                    _vehicleChecklist.BeginPhase();
                    break;
                case UIFlowState.Completed:
                    _completedPanel?.Show();
                    break;
                case UIFlowState.Failed:
                    _failedPanel?.Show();
                    break;
                case UIFlowState.ResetGame:
                    _resetGamePanel?.Show();
                    break;
            }

            PanelChanged?.Invoke(nextState);
            if (capturedBefore != IsInputCaptured)
                InputCaptureChanged?.Invoke(IsInputCaptured);
        }

        private void HandleFailureActionConfirmed(FailedPanelAction action)
        {
            if (action == FailedPanelAction.ContinueFromCheckpoint)
            {
                ContinueRequested?.Invoke();
                TransitionTo(UIFlowState.Hidden);
                return;
            }

            ResetRequested?.Invoke();
            TransitionTo(UIFlowState.Welcome);
        }

        private void HandleResetGameActionConfirmed(ResetGameAction action)
        {
            switch (action)
            {
                case ResetGameAction.Restart:
                    RestartGameRequested?.Invoke();
                    TransitionTo(UIFlowState.CalibrateVehicleView);
                    break;
                case ResetGameAction.ResetPhase:
                    ResetPhaseRequested?.Invoke();
                    TransitionTo(UIFlowState.Hidden);
                    break;
                default:
                    ContinueGameRequested?.Invoke();
                    TransitionTo(UIFlowState.Hidden);
                    break;
            }
        }

    }
}
