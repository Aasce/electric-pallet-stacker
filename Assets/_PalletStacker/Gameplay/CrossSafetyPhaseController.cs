using System;
using ElectricPalletStackers.Audio;
using ElectricPalletStackers.Localization;
using ElectricPalletStackers.PalletStackers;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Video;

namespace ElectricPalletStackers.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class CrossSafetyPhaseController : MonoBehaviour, IGameFailureSource
    {
        private enum PhaseState
        {
            Idle,
            WaitingForStop,
            ConfirmingDirections,
            CompletingDirections,
            HazardActive,
            FootAccidentCutscene,
            Completed
        }

        [Header("Flow")]
        [SerializeField] private AppManager _appManager;
        [SerializeField] private TrainingExperienceController _trainingExperience;

        [Header("Player")]
        [SerializeField] private Rigidbody _playerVehicle;
        [SerializeField] private PalletStackerRigidbodyMotor _playerMotor;
        [SerializeField] private PalletStackerControlDriver _playerControlDriver;
        [SerializeField] private Transform _playerView;

        [Header("Cross zones")]
        [Tooltip("Entering this trigger displays the stop-and-check instruction panel.")]
        [SerializeField] private Collider _panelZone;
        [Tooltip("Entering this trigger starts the stop-and-direction-confirmation flow.")]
        [FormerlySerializedAs("_instructionZone")]
        [SerializeField] private Collider _safetyConfirmZone;
        [Tooltip("Entering this trigger before confirmation starts the waiting pedestrian crossing hazard.")]
        [SerializeField] private Collider _dangerZone;

        [Header("Direction confirmation")]
        [Tooltip("Origin used with the Turn Left / Turn Right marker positions to derive fixed world directions.")]
        [SerializeField] private Transform _directionRoot;
        [Tooltip("The Turn Left and Turn Right target GameObjects under Confirm Directions.")]
        [SerializeField] private CrossDirectionPokeTarget[] _directionTargets;
        [Tooltip("Reach distance from the player's view to every poke sphere.")]
        [SerializeField, Range(0.3f, 0.8f)] private float _targetDistance = 0.5625f;
        [Tooltip("Moves the centre shared by all direction targets slightly forward, making side targets easier to see without a full 90-degree head turn.")]
        [SerializeField, Min(0f)] private float _targetForwardOffset = 0.2f;
        [Tooltip("Vertical offset from the player's view. Negative values place spheres below eye level.")]
        [SerializeField, Range(-0.5f, 0.25f)] private float _targetVerticalOffset = -0.18f;
        [Tooltip("How long the final confirmed target remains visible before all direction targets are hidden.")]
        [SerializeField, Min(0f)] private float _finalConfirmationVisibleDuration = 1f;

        [Header("Stop validation")]
        [SerializeField, Min(0.01f)] private float _stoppedSpeedThreshold = 0.05f;
        [SerializeField, Min(0f)] private float _stoppedSettleDuration = 0.35f;
        [Header("Instruction UI")]
        [SerializeField] private GameObject _instructionPanel;
        [SerializeField] private TMP_Text _instructionText;

        [Header("Unsafe crossing pedestrian")]
        [SerializeField] private CrossingVehicleController _crossingVehicle;

        [Header("Unsafe crossing presentation")]
        [SerializeField] private GameObject _dangerPanel;
        [SerializeField] private GameObject _footAccidentCutscenePanel;
        [SerializeField] private VideoPlayer _footAccidentVideoPlayer;
        [Tooltip("Maximum time after an unsafe entry before the cutscene starts, regardless of the pedestrian's route progress.")]
        [SerializeField, Min(0f)] private float _unsafeCutsceneDelay = 2f;
        [SerializeField, Min(0.1f)] private float _missingVideoFallbackDuration = 3f;

        private PhaseState _state;
        private Vector3[] _fixedDirections;
        private Collider[] _playerColliders;
        private float _stoppedDuration;
        private float _finalConfirmationElapsed;
        private float _unsafeElapsed;
        private float _cutsceneElapsed;
        private int _promptedDirectionIndex;

        public bool HasCompleted => _state == PhaseState.Completed;
        public bool IsRunning => _state == PhaseState.WaitingForStop ||
                                 _state == PhaseState.ConfirmingDirections ||
                                 _state == PhaseState.CompletingDirections;
        public event Action<GameFailureReason> FailureRequested;
        public event Action ApproachInstructionPresented;
        public event Action SafetyInstructionPresented;
        public event Action DangerPresented;

        private void Awake()
        {
            if (_playerVehicle != null)
                _playerColliders = _playerVehicle.GetComponentsInChildren<Collider>(true);
            CacheFixedDirections();
            ConfigureTargets();
            ConfigurePresentationPanels();
            HidePresentation();
        }

        private void OnEnable()
        {
            if (_appManager != null)
            {
                _appManager.RoundStarted += HandleRoundStarted;
                _appManager.StateChanged += HandleGameStateChanged;
            }
            if (_trainingExperience != null)
                _trainingExperience.StageChanged += HandleStageChanged;
            if (_footAccidentVideoPlayer != null)
            {
                _footAccidentVideoPlayer.loopPointReached += HandleFootAccidentVideoFinished;
                _footAccidentVideoPlayer.errorReceived += HandleFootAccidentVideoError;
            }
        }

        private void OnDisable()
        {
            if (_appManager != null)
            {
                _appManager.RoundStarted -= HandleRoundStarted;
                _appManager.StateChanged -= HandleGameStateChanged;
            }
            if (_trainingExperience != null)
                _trainingExperience.StageChanged -= HandleStageChanged;
            if (_footAccidentVideoPlayer != null)
            {
                _footAccidentVideoPlayer.loopPointReached -= HandleFootAccidentVideoFinished;
                _footAccidentVideoPlayer.errorReceived -= HandleFootAccidentVideoError;
            }
            HidePresentation();
        }

        private void Update()
        {
            if ((_appManager != null && _appManager.CurrentState != GameState.Playing) ||
                _state == PhaseState.Completed)
                return;

            if (_state == PhaseState.CompletingDirections)
            {
                _finalConfirmationElapsed += Time.deltaTime;
                if (_finalConfirmationElapsed >= _finalConfirmationVisibleDuration)
                    CompleteSafetyCheck();
                return;
            }

            if (_playerVehicle == null) return;

            if (_state == PhaseState.FootAccidentCutscene)
            {
                UpdateFootAccidentCutscene();
                return;
            }

            if (_state == PhaseState.HazardActive)
            {
                _unsafeElapsed += Time.deltaTime;
                if (IsVehicleStopped() || _unsafeElapsed >= _unsafeCutsceneDelay)
                {
                    BeginFootAccidentCutscene();
                    return;
                }
                return;
            }

            if (!IsPhaseEligible()) return;

            if (IsVehicleInsideZone(_dangerZone))
            {
                TriggerUnsafeCrossing();
                return;
            }

            if (_state == PhaseState.Idle)
            {
                if (IsVehicleInsideZone(_panelZone))
                {
                    ShowApproachInstruction();
                }
                else
                    HideInstructionPanel();

                if (IsVehicleInsideZone(_safetyConfirmZone))
                    BeginSafetyCheck();
            }

            if (_state == PhaseState.WaitingForStop)
            {
                if (!IsVehicleInsideZone(_safetyConfirmZone))
                {
                    _stoppedDuration = 0f;
                    HideInstructionPanel();
                    return;
                }

                ShowSafetyInstruction();
                if (IsVehicleStopped())
                {
                    _stoppedDuration += Time.deltaTime;
                    if (_stoppedDuration >= _stoppedSettleDuration) ShowDirectionTargets();
                }
                else _stoppedDuration = 0f;
                return;
            }

            if (_state == PhaseState.ConfirmingDirections && !IsVehicleStopped())
            {
                if (IsVehicleMovingForward())
                    TriggerUnsafeCrossing();
                else
                    CancelSafetyCheckForRetreat();
            }
        }

        private void LateUpdate()
        {
            if ((_state != PhaseState.ConfirmingDirections &&
                 _state != PhaseState.CompletingDirections) ||
                _playerView == null ||
                _directionTargets == null || _fixedDirections == null) return;

            Vector3 forwardDirection = _directionRoot != null
                ? Vector3.ProjectOnPlane(_directionRoot.right, Vector3.up).normalized
                : Vector3.ProjectOnPlane(_playerView.forward, Vector3.up).normalized;
            Vector3 origin = _playerView.position +
                             Vector3.up * _targetVerticalOffset +
                             forwardDirection * _targetForwardOffset;
            int count = Mathf.Min(_directionTargets.Length, _fixedDirections.Length);
            for (int index = 0; index < count; index++)
            {
                CrossDirectionPokeTarget target = _directionTargets[index];
                if (target == null) continue;
                target.transform.position = origin +
                                            _fixedDirections[index] * _targetDistance;
                target.transform.rotation = Quaternion.LookRotation(
                    _fixedDirections[index],
                    Vector3.up);
                target.FaceInstructionLabel(_playerView);
            }
        }

        public void NotifyDirectionConfirmed(CrossDirectionPokeTarget confirmedTarget)
        {
            if (_state != PhaseState.ConfirmingDirections || confirmedTarget == null) return;

            AdvanceDirectionPrompt(confirmedTarget);

            for (int index = 0; index < _directionTargets.Length; index++)
            {
                CrossDirectionPokeTarget target = _directionTargets[index];
                if (target != null && !target.IsConfirmed) return;
            }

            _state = PhaseState.CompletingDirections;
            _finalConfirmationElapsed = 0f;
        }

        private void BeginSafetyCheck()
        {
            _state = PhaseState.WaitingForStop;
            _stoppedDuration = 0f;
            SetTargetsActive(false, true);
            ShowSafetyInstruction(true);
            Debug.Log("[Cross Safety] Entered safety-confirm zone. Waiting for the player to stop.", this);
        }

        private void ShowApproachInstruction()
        {
            if (_instructionPanel == null || _instructionPanel.activeSelf) return;

            SetInstruction(
                LocalizationKeys.CrossSafety.ApproachInstruction,
                "Intersection ahead. Slow down and prepare to stop for a safety check.");
            _instructionPanel.SetActive(true);
            ApproachInstructionPresented?.Invoke();
        }

        private void ShowSafetyInstruction(bool announce = false)
        {
            if (_instructionPanel == null) return;

            bool wasVisible = _instructionPanel.activeSelf;
            SetInstruction(
                LocalizationKeys.CrossSafety.Instruction,
                "Stop, point, and confirm it is safe to proceed.");
            _instructionPanel.SetActive(true);
            if (announce || !wasVisible) SafetyInstructionPresented?.Invoke();
        }

        private void HideInstructionPanel()
        {
            if (_instructionPanel != null) _instructionPanel.SetActive(false);
        }

        private void CancelSafetyCheckForRetreat()
        {
            _state = PhaseState.WaitingForStop;
            _stoppedDuration = 0f;
            _finalConfirmationElapsed = 0f;
            SetTargetsActive(false, true);

            if (IsVehicleInsideZone(_safetyConfirmZone))
                ShowSafetyInstruction();
            else
                HideInstructionPanel();
        }

        private void SetInstruction(string localizationKey, string fallback)
        {
            if (_instructionText == null) return;

            LocalizationManager localizationManager =
                FindFirstObjectByType<LocalizationManager>();
            ILocalizationService localization = localizationManager?.Service;
            LocalizedText localizedText = _instructionText.GetComponent<LocalizedText>();
            if (localizedText != null)
            {
                localizedText.SetKey(localizationKey, localization);
                if (localization == null) _instructionText.text = fallback;
                return;
            }

            _instructionText.text = localization != null
                ? localization.GetText(localizationKey)
                : fallback;
        }

        private void ShowDirectionTargets()
        {
            _state = PhaseState.ConfirmingDirections;
            SetTargetsActive(true, true);
            if (_instructionPanel != null) _instructionPanel.SetActive(false);
            ShowFirstDirectionPrompt();
            Debug.Log("[Cross Safety] Vehicle stopped. Direction targets are now active.", this);
        }

        private void ShowFirstDirectionPrompt()
        {
            _promptedDirectionIndex = 0;
            SetAllDirectionPromptsVisible(false);
            if (_directionTargets != null && _directionTargets.Length > 0)
                _directionTargets[0]?.SetInstructionVisible(true);
        }

        private void AdvanceDirectionPrompt(CrossDirectionPokeTarget confirmedTarget)
        {
            if (_directionTargets == null ||
                _promptedDirectionIndex < 0 ||
                _promptedDirectionIndex >= _directionTargets.Length ||
                _directionTargets[_promptedDirectionIndex] != confirmedTarget)
                return;

            confirmedTarget.SetInstructionVisible(false);
            _promptedDirectionIndex++;
            if (_promptedDirectionIndex < _directionTargets.Length)
                _directionTargets[_promptedDirectionIndex]?.SetInstructionVisible(true);
        }

        private void SetAllDirectionPromptsVisible(bool visible)
        {
            if (_directionTargets == null) return;
            for (int index = 0; index < _directionTargets.Length; index++)
                _directionTargets[index]?.SetInstructionVisible(visible);
        }

        private void CompleteSafetyCheck()
        {
            _state = PhaseState.Completed;
            HidePresentation();
            if (_trainingExperience != null)
                _trainingExperience.CompleteCrossSafetyCheck();
            else
                _crossingVehicle?.SuppressVehicle();
        }

        private void TriggerUnsafeCrossing()
        {
            _state = PhaseState.HazardActive;
            _unsafeElapsed = 0f;
            HidePresentation();
            if (_dangerPanel != null) _dangerPanel.SetActive(true);
            DangerPresented?.Invoke();
            Debug.LogWarning(
                "[Cross Safety] Unsafe crossing detected. The waiting pedestrian is crossing.",
                this);
            if (_crossingVehicle == null || _playerVehicle == null) return;
            _crossingVehicle.ArmForHazard();
            _crossingVehicle.BeginCrossing();
        }

        private void BeginFootAccidentCutscene()
        {
            _state = PhaseState.FootAccidentCutscene;
            _cutsceneElapsed = 0f;
            if (_dangerPanel != null) _dangerPanel.SetActive(false);
            if (_instructionPanel != null) _instructionPanel.SetActive(false);
            SetTargetsActive(false, false);

            _playerControlDriver?.SetGameplayInterlock(true);
            _playerMotor?.StopImmediately();
            _crossingVehicle?.StopVehicle();

            if (_footAccidentCutscenePanel != null)
                _footAccidentCutscenePanel.SetActive(true);
            if (_footAccidentVideoPlayer != null && _footAccidentVideoPlayer.clip != null)
            {
                _footAccidentVideoPlayer.time = 0d;
                _footAccidentVideoPlayer.Play();
            }

            AudioManager.Instance?.Play(PalletStackerAudioKeys.HumanScream);

            Debug.LogWarning(
                "[Cross Safety] Unsafe-entry delay elapsed or the player stopped early. " +
                "Playing the accident cutscene.",
                this);
        }

        private void UpdateFootAccidentCutscene()
        {
            if (_footAccidentVideoPlayer != null && _footAccidentVideoPlayer.clip != null) return;

            _cutsceneElapsed += Time.deltaTime;
            if (_cutsceneElapsed >= _missingVideoFallbackDuration)
                FinishFootAccidentCutscene();
        }

        private void HandleFootAccidentVideoFinished(VideoPlayer source)
        {
            if (source == _footAccidentVideoPlayer) FinishFootAccidentCutscene();
        }

        private void HandleFootAccidentVideoError(VideoPlayer source, string message)
        {
            if (source != _footAccidentVideoPlayer) return;
            Debug.LogWarning($"[Cross Safety] Cutscene video failed: {message}", this);
            FinishFootAccidentCutscene();
        }

        private void FinishFootAccidentCutscene()
        {
            if (_state != PhaseState.FootAccidentCutscene) return;

            if (_footAccidentVideoPlayer != null) _footAccidentVideoPlayer.Stop();
            if (_footAccidentCutscenePanel != null)
                _footAccidentCutscenePanel.SetActive(false);

            _state = PhaseState.HazardActive;
            FailureRequested?.Invoke(GameFailureReason.OperatorFootRunOver);
        }

        private bool IsVehicleStopped()
        {
            float motorSpeed = _playerMotor != null ? Mathf.Abs(_playerMotor.CurrentSpeed) : 0f;
            Vector3 planarVelocity = Vector3.ProjectOnPlane(_playerVehicle.linearVelocity, Vector3.up);
            return motorSpeed <= _stoppedSpeedThreshold &&
                   planarVelocity.sqrMagnitude <= _stoppedSpeedThreshold * _stoppedSpeedThreshold;
        }

        private bool IsVehicleMovingForward()
        {
            if (_playerMotor != null &&
                Mathf.Abs(_playerMotor.CurrentSpeed) > _stoppedSpeedThreshold)
                return _playerMotor.CurrentSpeed > 0f;

            Vector3 planarVelocity = Vector3.ProjectOnPlane(
                _playerVehicle.linearVelocity,
                Vector3.up);
            Vector3 forward = _playerMotor != null
                ? _playerMotor.WorldForward
                : Vector3.ProjectOnPlane(_playerVehicle.transform.forward, Vector3.up).normalized;
            return Vector3.Dot(planarVelocity, forward) > 0f;
        }

        private bool IsPhaseEligible()
        {
            if (_trainingExperience == null) return true;
            if (_trainingExperience.HasCompletedCrossSafety) return false;
            return _trainingExperience.CurrentStage > TrainingExperienceStage.WaitingToStart &&
                   _trainingExperience.CurrentStage < TrainingExperienceStage.PhoneIntersection;
        }

        private bool IsVehicleInsideZone(Collider zone)
        {
            if (zone == null || !zone.enabled || !zone.gameObject.activeInHierarchy) return false;

            Vector3 centerOfMass = _playerVehicle.worldCenterOfMass;
            Vector3 closestPoint = zone.ClosestPoint(centerOfMass);
            if ((closestPoint - centerOfMass).sqrMagnitude <= 0.0001f) return true;

            if (_playerColliders == null || _playerColliders.Length == 0)
                _playerColliders = _playerVehicle.GetComponentsInChildren<Collider>(true);

            Bounds zoneBounds = zone.bounds;
            for (int index = 0; index < _playerColliders.Length; index++)
            {
                Collider playerCollider = _playerColliders[index];
                if (playerCollider == null || !playerCollider.enabled ||
                    !playerCollider.gameObject.activeInHierarchy || playerCollider.isTrigger)
                    continue;

                if (zoneBounds.Intersects(playerCollider.bounds)) return true;
            }
            return false;
        }

        private void HandleRoundStarted()
        {
            _state = PhaseState.Idle;
            _stoppedDuration = 0f;
            _finalConfirmationElapsed = 0f;
            _unsafeElapsed = 0f;
            _cutsceneElapsed = 0f;
            HidePresentation();
        }

        private void HandleGameStateChanged(GameState previous, GameState next)
        {
            if (next == GameState.WaitingToStart)
            {
                _state = PhaseState.Idle;
                HidePresentation();
                return;
            }

            if (next == GameState.Lost)
            {
                HidePresentation();
                _crossingVehicle?.StopVehicle();
                return;
            }

            if (next == GameState.Playing && previous == GameState.Lost)
                SynchronizeWithCurrentStage();
        }

        private void HandleStageChanged(
            TrainingExperienceStage previous,
            TrainingExperienceStage next)
        {
            if (next >= TrainingExperienceStage.PhoneIntersection)
            {
                _state = PhaseState.Completed;
                HidePresentation();
                _crossingVehicle?.SuppressVehicle();
            }
        }

        private void SynchronizeWithCurrentStage()
        {
            if (_trainingExperience != null &&
                (_trainingExperience.HasCompletedCrossSafety ||
                 _trainingExperience.CurrentStage >= TrainingExperienceStage.PhoneIntersection))
            {
                _state = PhaseState.Completed;
                _crossingVehicle?.SuppressVehicle();
            }
            else
            {
                _state = PhaseState.Idle;
                _crossingVehicle?.ResetVehicle();
            }
            HidePresentation();
        }

        private void CacheFixedDirections()
        {
            if (_directionTargets == null)
            {
                _fixedDirections = System.Array.Empty<Vector3>();
                return;
            }

            _fixedDirections = new Vector3[_directionTargets.Length];
            Vector3 origin = _directionRoot != null ? _directionRoot.position : transform.position;
            for (int index = 0; index < _directionTargets.Length; index++)
            {
                CrossDirectionPokeTarget target = _directionTargets[index];
                Vector3 direction = target != null
                    ? Vector3.ProjectOnPlane(target.transform.position - origin, Vector3.up)
                    : Vector3.zero;
                if (direction.sqrMagnitude < 0.001f)
                {
                    float angle = _directionTargets.Length > 0
                        ? index * 360f / _directionTargets.Length
                        : 0f;
                    direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                }
                _fixedDirections[index] = direction.normalized;
            }
        }

        private void ConfigureTargets()
        {
            if (_directionTargets == null) return;
            for (int index = 0; index < _directionTargets.Length; index++)
                _directionTargets[index]?.Configure(this, _playerColliders);
        }

        private void ConfigurePresentationPanels()
        {
            ConfigurePresentationPanel(_instructionPanel);
            ConfigurePresentationPanel(_dangerPanel);
            ConfigurePresentationPanel(_footAccidentCutscenePanel);
        }

        private static void ConfigurePresentationPanel(GameObject panel)
        {
            if (panel == null) return;
            Canvas panelCanvas = panel.GetComponent<Canvas>();
            if (panelCanvas == null) return;

            panelCanvas.overrideSorting = true;
            panelCanvas.sortingOrder = short.MaxValue - 10;
        }

        private void HidePresentation()
        {
            if (_instructionPanel != null) _instructionPanel.SetActive(false);
            if (_dangerPanel != null) _dangerPanel.SetActive(false);
            if (_footAccidentVideoPlayer != null) _footAccidentVideoPlayer.Stop();
            if (_footAccidentCutscenePanel != null)
                _footAccidentCutscenePanel.SetActive(false);
            SetAllDirectionPromptsVisible(false);
            SetTargetsActive(false, false);
        }

        private void SetTargetsActive(bool active, bool reset)
        {
            if (_directionTargets == null) return;
            for (int index = 0; index < _directionTargets.Length; index++)
            {
                CrossDirectionPokeTarget target = _directionTargets[index];
                if (target == null) continue;
                if (reset) target.ResetTarget();
                target.gameObject.SetActive(active);
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (_panelZone != null)
            {
                Gizmos.color = new Color(0.1f, 0.75f, 1f, 0.8f);
                Gizmos.DrawWireCube(_panelZone.bounds.center, _panelZone.bounds.size);
            }
            if (_safetyConfirmZone != null)
            {
                Gizmos.color = new Color(0.3f, 1f, 0.35f, 0.8f);
                Gizmos.DrawWireCube(
                    _safetyConfirmZone.bounds.center,
                    _safetyConfirmZone.bounds.size);
            }
            if (_dangerZone != null)
            {
                Gizmos.color = new Color(1f, 0.2f, 0.1f, 0.8f);
                Gizmos.DrawWireCube(_dangerZone.bounds.center, _dangerZone.bounds.size);
            }
        }
    }
}
