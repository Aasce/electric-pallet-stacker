using System.Collections;
using ElectricPalletStackers.Ble;
using ElectricPalletStackers.Gameplay;
using ElectricPalletStackers.PalletStackers;
using UnityEngine;
using UnityEngine.Video;

namespace ElectricPalletStackers.UI
{
    [DisallowMultipleComponent]
    public sealed class PhoneCallEventSimulator : MonoBehaviour, IGameResettable
    {
        [Header("References")]
        [SerializeField] private AppManager _appManager;
        [SerializeField] private PhoneCallPanel _phoneCallPanel;
        [SerializeField] private PhoneCallChoicePanel _phoneChoicePanel;
        [SerializeField] private PhoneCallWorldPlacement _phonePlacement;
        [SerializeField] private PalletStackerControlDriver _controlDriver;
        [SerializeField] private PalletStackerRigidbodyMotor _vehicleMotor;
        [SerializeField] private PalletStackerCollisionReporter _collisionReporter;

        [Header("Call")]
        [SerializeField, Min(0f)] private float _endedDisplayDuration = 1f;

        [Header("Move while answering cutscene")]
        [SerializeField] private GameObject _movementCutscenePanel;
        [SerializeField] private VideoPlayer _movementCutsceneVideoPlayer;
        [SerializeField, Min(0.1f)] private float _missingVideoFallbackDuration = 3f;

        private Coroutine _endedDisplayRoutine;
        private bool _activeConversation;
        private bool _callResolved = true;
        private bool _movementFailureStarted;
        private bool _movementCutscenePlaying;
        private bool _ownsVehicleInterlock;
        private float _cutsceneElapsed;

        public bool IsRinging => _phoneCallPanel != null && _phoneCallPanel.IsRinging;
        public bool IsInCall => _activeConversation;

        public event System.Action CallCompleted;

        private void Awake()
        {
            _phoneCallPanel?.SetDirectInteractionEnabled(false);
            ConfigureCutscenePanel();
            HideMovementCutscene();
            _phoneChoicePanel?.HideImmediate();
        }

        private void OnEnable()
        {
            if (_phoneCallPanel != null)
            {
                _phoneCallPanel.CallAccepted += HandleCallAccepted;
                _phoneCallPanel.CallEnded += HandleCallEnded;
            }
            if (_phoneChoicePanel != null)
            {
                _phoneChoicePanel.StopAndListenSelected += HandleStopAndListenSelected;
                _phoneChoicePanel.MoveWhileListeningSelected += HandleMoveWhileListeningSelected;
            }
            if (_movementCutsceneVideoPlayer != null)
            {
                _movementCutsceneVideoPlayer.loopPointReached += HandleMovementCutsceneFinished;
                _movementCutsceneVideoPlayer.errorReceived += HandleMovementCutsceneError;
            }
            if (_appManager != null)
            {
                _appManager.RoundStarted += HandleRoundStarted;
                _appManager.StateChanged += HandleGameStateChanged;
            }
        }

        private void OnDisable()
        {
            if (_phoneCallPanel != null)
            {
                _phoneCallPanel.CallAccepted -= HandleCallAccepted;
                _phoneCallPanel.CallEnded -= HandleCallEnded;
            }
            if (_phoneChoicePanel != null)
            {
                _phoneChoicePanel.StopAndListenSelected -= HandleStopAndListenSelected;
                _phoneChoicePanel.MoveWhileListeningSelected -= HandleMoveWhileListeningSelected;
            }
            if (_movementCutsceneVideoPlayer != null)
            {
                _movementCutsceneVideoPlayer.loopPointReached -= HandleMovementCutsceneFinished;
                _movementCutsceneVideoPlayer.errorReceived -= HandleMovementCutsceneError;
            }
            if (_appManager != null)
            {
                _appManager.RoundStarted -= HandleRoundStarted;
                _appManager.StateChanged -= HandleGameStateChanged;
            }
            ResetCallState();
        }

        private void Update()
        {
            if (!_movementCutscenePlaying ||
                (_movementCutsceneVideoPlayer != null &&
                 _movementCutsceneVideoPlayer.clip != null)) return;

            _cutsceneElapsed += Time.unscaledDeltaTime;
            if (_cutsceneElapsed >= _missingVideoFallbackDuration)
                FinishMovementCutscene();
        }

        public void TriggerIncomingCall()
        {
            if ((_appManager != null && _appManager.CurrentState != GameState.Playing) ||
                IsRinging || _activeConversation || _movementCutscenePlaying) return;

            ResetCallState();
            BeginIncomingCall();
        }

        // Kept for the existing phone trigger. Direction no longer affects this event.
        public void CaptureTriggerEntryDirection()
        {
        }

        public void ResetState()
        {
            ResetCallState();
        }

        public void RestartIncomingCall()
        {
            if (_appManager != null && _appManager.CurrentState != GameState.Playing) return;
            ResetCallState();
            BeginIncomingCall();
        }

        [ContextMenu("Simulate Call Now")]
        private void SimulateCallNow()
        {
            RestartIncomingCall();
        }

        private void BeginIncomingCall()
        {
            _activeConversation = false;
            _callResolved = false;
            _movementFailureStarted = false;
            AcquireVehicleInterlock();
            _phoneCallPanel?.SetDirectInteractionEnabled(false);
            _phoneCallPanel?.ShowIncomingCall();
            _phoneChoicePanel?.Show();
            _phonePlacement?.ShowInFrontOfViewer();
        }

        private void HandleStopAndListenSelected()
        {
            if (_callResolved || _movementFailureStarted ||
                (_appManager != null && _appManager.CurrentState != GameState.Playing)) return;

            _phoneChoicePanel?.Hide();
            _phoneCallPanel?.AcceptCall();
        }

        private void HandleMoveWhileListeningSelected()
        {
            if (_callResolved || _movementFailureStarted ||
                (_appManager != null && _appManager.CurrentState != GameState.Playing)) return;

            _movementFailureStarted = true;
            _activeConversation = false;
            CancelEndedDisplayRoutine();
            _phoneChoicePanel?.HideImmediate();
            _phoneCallPanel?.HideImmediate();
            _phonePlacement?.Hide();
            BeginMovementCutscene();
        }

        private void HandleCallAccepted()
        {
            if (_callResolved || _movementFailureStarted ||
                (_appManager != null && _appManager.CurrentState != GameState.Playing)) return;

            _activeConversation = true;
        }

        private void HandleCallEnded()
        {
            BeginEndedPhase();
        }

        private void BeginEndedPhase()
        {
            if (_callResolved || _movementFailureStarted ||
                _phoneCallPanel == null || !_phoneCallPanel.IsVisible ||
                _phoneCallPanel.State == PhoneCallState.Ended ||
                (_appManager != null && _appManager.CurrentState != GameState.Playing)) return;

            _activeConversation = false;
            _phoneCallPanel.ShowEnded();
            CancelEndedDisplayRoutine();
            _endedDisplayRoutine = StartCoroutine(CompleteAfterEndedDisplay());
        }

        private IEnumerator CompleteAfterEndedDisplay()
        {
            yield return new WaitForSecondsRealtime(Mathf.Max(0f, _endedDisplayDuration));
            _endedDisplayRoutine = null;
            CompleteCall();
        }

        private void CompleteCall()
        {
            if (_callResolved || _movementFailureStarted ||
                (_appManager != null && _appManager.CurrentState != GameState.Playing)) return;

            _callResolved = true;
            _activeConversation = false;
            _phoneChoicePanel?.HideImmediate();
            _phoneCallPanel?.HideImmediate();
            _phonePlacement?.Hide();
            ReleaseVehicleInterlock();
            CallCompleted?.Invoke();
        }

        private void BeginMovementCutscene()
        {
            _movementCutscenePlaying = true;
            _cutsceneElapsed = 0f;
            AcquireVehicleInterlock();
            ConfigureCutscenePanel();
            if (_movementCutscenePanel != null)
                _movementCutscenePanel.SetActive(true);

            if (_movementCutsceneVideoPlayer != null &&
                _movementCutsceneVideoPlayer.clip != null)
            {
                _movementCutsceneVideoPlayer.time = 0d;
                _movementCutsceneVideoPlayer.Play();
            }
        }

        private void HandleMovementCutsceneFinished(VideoPlayer source)
        {
            if (source == _movementCutsceneVideoPlayer) FinishMovementCutscene();
        }

        private void HandleMovementCutsceneError(VideoPlayer source, string message)
        {
            if (source != _movementCutsceneVideoPlayer) return;
            Debug.LogWarning($"Phone distraction cutscene failed: {message}", this);
            FinishMovementCutscene();
        }

        private void FinishMovementCutscene()
        {
            if (!_movementCutscenePlaying) return;

            _movementCutscenePlaying = false;
            HideMovementCutscene();
            if (_collisionReporter != null)
                _collisionReporter.ReportCollision(GameFailureReason.PhoneDistractionCollision);
            else
                Debug.LogError(
                    "Phone distraction cutscene requires a collision reporter to finish the failure.",
                    this);
        }

        private void AcquireVehicleInterlock()
        {
            if (!_ownsVehicleInterlock)
            {
                _controlDriver?.SetPhoneCallInterlock(true);
                _ownsVehicleInterlock = true;
            }
            _vehicleMotor?.StopImmediately();
        }

        private void ReleaseVehicleInterlock()
        {
            if (!_ownsVehicleInterlock) return;
            _controlDriver?.SetPhoneCallInterlock(false);
            _ownsVehicleInterlock = false;
        }

        private void ConfigureCutscenePanel()
        {
            if (_movementCutscenePanel == null) return;
            Canvas canvas = _movementCutscenePanel.GetComponent<Canvas>();
            if (canvas == null) return;
            canvas.overrideSorting = true;
            canvas.sortingOrder = short.MaxValue - 10;
        }

        private void HideMovementCutscene()
        {
            if (_movementCutsceneVideoPlayer != null)
                _movementCutsceneVideoPlayer.Stop();
            if (_movementCutscenePanel != null)
                _movementCutscenePanel.SetActive(false);
        }

        private void CancelEndedDisplayRoutine()
        {
            if (_endedDisplayRoutine == null) return;
            StopCoroutine(_endedDisplayRoutine);
            _endedDisplayRoutine = null;
        }

        private void HandleRoundStarted()
        {
            ResetCallState();
        }

        private void HandleGameStateChanged(GameState previousState, GameState nextState)
        {
            if (nextState != GameState.Playing)
            {
                ResetCallState();
                return;
            }

            if (previousState == GameState.Lost)
                ResetCallState();
        }

        private void ResetCallState()
        {
            CancelEndedDisplayRoutine();
            _activeConversation = false;
            _callResolved = true;
            _movementFailureStarted = false;
            _movementCutscenePlaying = false;
            _cutsceneElapsed = 0f;
            _phoneChoicePanel?.HideImmediate();
            _phoneCallPanel?.HideImmediate();
            _phonePlacement?.Hide();
            HideMovementCutscene();
            ReleaseVehicleInterlock();
        }
    }
}
