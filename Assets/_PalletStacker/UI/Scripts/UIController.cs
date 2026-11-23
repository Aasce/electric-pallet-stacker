using System;
using Cysharp.Threading.Tasks;
using ElectricPalletStackers.Ble;
using ElectricPalletStackers.Calibration;
using ElectricPalletStackers.Gameplay;
using ElectricPalletStackers.Gameplay.Checklist;
using ElectricPalletStackers.Localization;
using ElectricPalletStackers.Presentation;
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
        [SerializeField] private PalletStackerBleClient _bleClient;

        [Header("UI Flow")]
        [SerializeField] private UIInputRouter _inputRouter;
        [SerializeField] private UISelectLanguagePanel _selectLanguagePanel;
        [SerializeField] private CalibrationInputPanel _calibrationPanel;
        [SerializeField] private VehicleViewCalibrationSystem _calibrationSystem;
        [SerializeField] private UIWelcomePanel _welcomePanel;
        [SerializeField] private UIPpePanel _ppePanel;
        [SerializeField] private PpeEquipmentPhase _ppeEquipmentPhase;
        [SerializeField] private UIChecklistPanel _checklistPanel;
        [SerializeField] private VehicleChecklistController _vehicleChecklist;
        [SerializeField] private UICompletedPanel _completedPanel;
        [SerializeField] private UIFailedPanel _failedPanel;
        [SerializeField] private UIResetGamePanel _resetGamePanel;

        public UIFlowState CurrentState { get; private set; } = UIFlowState.SelectLanguage;
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

        private Coroutine _checkpointRestoreRoutine;
        private Coroutine _managerGameRestartRoutine;

        public bool IsBlackoutTransitionInProgress =>
            _checkpointRestoreRoutine != null || _managerGameRestartRoutine != null;

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
            {
                _selectLanguagePanel.FocusChanged += HandleLanguageFocusChanged;
                _selectLanguagePanel.Confirmed += HandleLanguageConfirmed;
            }
            if (_ppeEquipmentPhase != null)
                _ppeEquipmentPhase.Equipped += HandlePpeEquipped;
            if (_vehicleChecklist != null)
                _vehicleChecklist.Completed += HandleVehicleChecklistCompleted;
            if (_calibrationPanel != null)
            {
                _calibrationPanel.AlignRequested += HandleAlignRequested;
                _calibrationPanel.OffsetAdjustmentRequested += HandleOffsetAdjustmentRequested;
                _calibrationPanel.ResetSteeringRequested += HandleSteeringResetRequested;
                _calibrationPanel.ContinueRequested += HandleCalibrationContinueRequested;
            }
            if (_bleClient != null)
                _bleClient.RecenterAvailabilityChanged += HandleRecenterAvailabilityChanged;
            if (_welcomePanel != null)
                _welcomePanel.ContinueRequested += HandleWelcomeContinueRequested;
            if (_completedPanel != null)
                _completedPanel.ContinueRequested += HandleCompletedContinueRequested;
            if (_failedPanel != null)
                _failedPanel.ActionConfirmed += HandleFailureActionConfirmed;
            if (_resetGamePanel != null)
                _resetGamePanel.ActionConfirmed += HandleResetGameActionConfirmed;

            _selectLanguagePanel?.HideImmediate();
            _calibrationPanel?.HideImmediate();
            _welcomePanel?.HideImmediate();
            _ppePanel?.HideImmediate();
            _checklistPanel?.HideImmediate();
            _ppeEquipmentPhase?.EndPhase();
            _vehicleChecklist?.EndPhase();
            _completedPanel?.HideImmediate();
            _failedPanel?.HideImmediate();
            _resetGamePanel?.HideImmediate();

            CurrentState = UIFlowState.SelectLanguage;
            if (_selectLanguagePanel != null)
            {
                FocusedLanguage = _selectLanguagePanel.FocusedLanguage;
                _selectLanguagePanel.Show();
                BlackoutSystem.Instance?.SetWorldVisibleOnly(_selectLanguagePanel.transform);
            }
        }

        private void OnEnable()
        {
            if (_inputRouter != null)
                _inputRouter.EmergencyStopHeld += HandleEmergencyStopHeld;

            if (CurrentState == UIFlowState.SelectLanguage && _selectLanguagePanel != null)
                BlackoutSystem.Instance?.SetWorldVisibleOnly(_selectLanguagePanel.transform);

            RefreshCalibrationBleControls();
        }

        private void OnDisable()
        {
            if (_inputRouter != null)
                _inputRouter.EmergencyStopHeld -= HandleEmergencyStopHeld;

            bool cancelManagerBlackout = _checkpointRestoreRoutine != null ||
                                         _managerGameRestartRoutine != null;
            if (_checkpointRestoreRoutine != null)
            {
                StopCoroutine(_checkpointRestoreRoutine);
                _checkpointRestoreRoutine = null;
            }
            if (_managerGameRestartRoutine != null)
            {
                StopCoroutine(_managerGameRestartRoutine);
                _managerGameRestartRoutine = null;
            }
            BlackoutSystem.Instance?.RestoreWorld();
            if (cancelManagerBlackout)
                BlackoutSystem.Instance?.ResetFade();
        }

        private void OnDestroy()
        {
            BlackoutSystem.Instance?.RestoreWorld();
            if (_selectLanguagePanel != null)
            {
                _selectLanguagePanel.FocusChanged -= HandleLanguageFocusChanged;
                _selectLanguagePanel.Confirmed -= HandleLanguageConfirmed;
            }
            if (_ppeEquipmentPhase != null)
                _ppeEquipmentPhase.Equipped -= HandlePpeEquipped;
            if (_vehicleChecklist != null)
                _vehicleChecklist.Completed -= HandleVehicleChecklistCompleted;
            if (_calibrationPanel != null)
            {
                _calibrationPanel.AlignRequested -= HandleAlignRequested;
                _calibrationPanel.OffsetAdjustmentRequested -= HandleOffsetAdjustmentRequested;
                _calibrationPanel.ResetSteeringRequested -= HandleSteeringResetRequested;
                _calibrationPanel.ContinueRequested -= HandleCalibrationContinueRequested;
            }
            if (_bleClient != null)
                _bleClient.RecenterAvailabilityChanged -= HandleRecenterAvailabilityChanged;
            if (_welcomePanel != null)
                _welcomePanel.ContinueRequested -= HandleWelcomeContinueRequested;
            if (_completedPanel != null)
                _completedPanel.ContinueRequested -= HandleCompletedContinueRequested;
            if (_failedPanel != null)
                _failedPanel.ActionConfirmed -= HandleFailureActionConfirmed;
            if (_resetGamePanel != null)
                _resetGamePanel.ActionConfirmed -= HandleResetGameActionConfirmed;
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

        public void ChangeLanguageFromManager(LanguageCode language)
        {
            FocusedLanguage = language;
            SelectedLanguage = language;
            _selectLanguagePanel?.RememberLanguage(language);
            _localizationManager?.SetLanguage(language);
            LanguagePreviewChanged?.Invoke(language);
        }

        public void RequestGameRestartFromManager()
        {
            if (IsBlackoutTransitionInProgress) return;
            _managerGameRestartRoutine = StartCoroutine(RestartGameWithBlackout());
        }

        private System.Collections.IEnumerator RestartGameWithBlackout()
        {
            BlackoutSystem blackout = BlackoutSystem.Instance;
            if (blackout != null) yield return blackout.FadeToBlack();
            RestartGameRequested?.Invoke();
            TransitionTo(UIFlowState.SelectLanguage);
            yield return new WaitForEndOfFrame();
            if (blackout != null) yield return blackout.FadeFromBlack();
            _managerGameRestartRoutine = null;
        }

        public void RequestPhaseResetWithBlackout()
        {
            StartCheckpointRestoreWithBlackout(continueFromCheckpoint: false);
        }

        private void RequestCheckpointContinueWithBlackout()
        {
            StartCheckpointRestoreWithBlackout(continueFromCheckpoint: true);
        }

        private void StartCheckpointRestoreWithBlackout(bool continueFromCheckpoint)
        {
            if (IsBlackoutTransitionInProgress ||
                (CurrentState != UIFlowState.Hidden &&
                 CurrentState != UIFlowState.Failed &&
                 CurrentState != UIFlowState.ResetGame)) return;

            _checkpointRestoreRoutine = StartCoroutine(
                RestoreCheckpointWithBlackout(continueFromCheckpoint));
        }

        private System.Collections.IEnumerator RestoreCheckpointWithBlackout(bool continueFromCheckpoint)
        {
            BlackoutSystem blackout = BlackoutSystem.Instance;
            if (blackout != null) yield return blackout.FadeToBlack();
            blackout?.HideWorld();

            // Keep the fully black frame up while the checkpoint restores its poses.
            yield return new WaitForEndOfFrame();
            try
            {
                if (continueFromCheckpoint)
                    ContinueRequested?.Invoke();
                else
                    ResetPhaseRequested?.Invoke();

                if (CurrentState != UIFlowState.Hidden)
                {
                    if (CurrentState == UIFlowState.Failed)
                        _failedPanel?.HideImmediate();
                    TransitionTo(UIFlowState.Hidden);
                }
                yield return new WaitForEndOfFrame();
            }
            finally
            {
                blackout?.RestoreWorld();
            }

            // Render the restored scene behind the black overlay before revealing it.
            yield return new WaitForEndOfFrame();
            if (blackout != null) yield return blackout.FadeFromBlack();
            _checkpointRestoreRoutine = null;
        }

        private void HandleLanguageFocusChanged(LanguageCode language)
        {
            FocusedLanguage = language;
            _localizationManager?.SetLanguage(language);
            LanguagePreviewChanged?.Invoke(language);
        }

        private void HandleLanguageConfirmed(LanguageCode language)
        {
            if (CurrentState != UIFlowState.SelectLanguage) return;

            FocusedLanguage = language;
            SelectedLanguage = language;
            _localizationManager?.SetLanguage(language);
            LanguageConfirmed?.Invoke(language);
            TransitionTo(UIFlowState.CalibrateVehicleView);
        }

        private void HandleCalibrationContinueRequested()
        {
            if (CurrentState == UIFlowState.CalibrateVehicleView)
                TransitionTo(UIFlowState.Welcome);
        }

        private void HandleWelcomeContinueRequested()
        {
            if (CurrentState == UIFlowState.Welcome)
                TransitionTo(UIFlowState.PpeEquipment);
        }

        private void HandleCompletedContinueRequested()
        {
            if (CurrentState != UIFlowState.Completed) return;

            ResetRequested?.Invoke();
            TransitionTo(UIFlowState.CalibrateVehicleView);
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
        private void HandleSteeringResetRequested()
        {
            if (_bleClient == null) return;
            RecenterSteeringAndRefreshAsync().Forget();
        }

        private async UniTask RecenterSteeringAndRefreshAsync()
        {
            RefreshCalibrationBleControls();
            await _bleClient.RecenterSteeringAsync();
            RefreshCalibrationBleControls();
        }

        private void HandleRecenterAvailabilityChanged(bool canRecenter)
        {
            RefreshCalibrationBleControls();
        }

        private void RefreshCalibrationBleControls()
        {
            bool isCalibrationVisible = CurrentState == UIFlowState.CalibrateVehicleView;
            bool resetInProgress = isCalibrationVisible && _bleClient != null && _bleClient.IsRecenterInProgress;
            bool canReset = isCalibrationVisible && _bleClient != null && _bleClient.CanRecenter;
            _calibrationPanel?.SetSteeringResetState(canReset, resetInProgress);
        }

        private void TransitionTo(UIFlowState nextState)
        {
            bool capturedBefore = IsInputCaptured;

            _selectLanguagePanel?.Hide();
            _calibrationPanel?.Hide();
            _welcomePanel?.Hide();
            _ppePanel?.Hide();
            _checklistPanel?.Hide();
            _ppeEquipmentPhase?.EndPhase();
            _vehicleChecklist?.EndPhase();
            _completedPanel?.Hide();
            _failedPanel?.Hide();
            _resetGamePanel?.Hide();

            if (nextState != UIFlowState.SelectLanguage)
                BlackoutSystem.Instance?.RestoreWorld();

            CurrentState = nextState;

            switch (nextState)
            {
                case UIFlowState.CalibrateVehicleView:
                    _calibrationSystem?.BeginCalibration();
                    _calibrationPanel?.Show();
                    break;
                case UIFlowState.SelectLanguage:
                    _selectLanguagePanel?.Show();
                    if (_selectLanguagePanel != null)
                        BlackoutSystem.Instance?.SetWorldVisibleOnly(_selectLanguagePanel.transform);
                    break;
                case UIFlowState.Welcome:
                    _welcomePanel?.Show();
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

            RefreshCalibrationBleControls();
            PanelChanged?.Invoke(nextState);
            if (capturedBefore != IsInputCaptured)
                InputCaptureChanged?.Invoke(IsInputCaptured);
        }

        private void HandleFailureActionConfirmed(FailedPanelAction action)
        {
            if (action == FailedPanelAction.ContinueFromCheckpoint)
            {
                RequestCheckpointContinueWithBlackout();
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
                    RequestGameRestartFromManager();
                    break;
                case ResetGameAction.ResetPhase:
                    RequestPhaseResetWithBlackout();
                    break;
                default:
                    ContinueGameRequested?.Invoke();
                    TransitionTo(UIFlowState.Hidden);
                    break;
            }
        }

    }
}
