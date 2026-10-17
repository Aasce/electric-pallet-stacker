using System;
using ElectricPalletStackers.PalletStackers;
using TMPro;
using UnityEngine;
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
        [Tooltip("Entering this trigger displays the stop-and-check instruction.")]
        [SerializeField] private Collider _instructionZone;
        [Tooltip("Entering this trigger before confirmation spawns the crossing vehicle hazard.")]
        [SerializeField] private Collider _dangerZone;

        [Header("Direction confirmation")]
        [Tooltip("Origin used with the Forward / Turn Right marker positions to derive fixed world directions.")]
        [SerializeField] private Transform _directionRoot;
        [Tooltip("The target GameObjects under Confirm Directions. Supports two or three directions.")]
        [SerializeField] private CrossDirectionPokeTarget[] _directionTargets;
        [Tooltip("Reach distance from the player's view to every poke sphere.")]
        [SerializeField, Range(0.3f, 0.8f)] private float _targetDistance = 0.5625f;
        [Tooltip("Moves the centre shared by all direction targets slightly forward, making side targets easier to see without a full 90-degree head turn.")]
        [SerializeField, Min(0f)] private float _targetForwardOffset = 0.2f;
        [Tooltip("Additional forward-axis adjustment for the Forward target.")]
        [SerializeField] private float _forwardTargetForwardAdjustment = -0.2f;
        [Tooltip("Additional forward-axis adjustment for the Turn Right target.")]
        [SerializeField] private float _turnRightTargetForwardAdjustment = 0.1f;
        [Tooltip("Vertical offset from the player's view. Negative values place spheres below eye level.")]
        [SerializeField, Range(-0.5f, 0.25f)] private float _targetVerticalOffset = -0.18f;
        [Tooltip("How long the final confirmed target remains visible before all direction targets are hidden.")]
        [SerializeField, Min(0f)] private float _finalConfirmationVisibleDuration = 1f;

        [Header("Stop validation")]
        [SerializeField, Min(0.01f)] private float _stoppedSpeedThreshold = 0.05f;
        [SerializeField, Min(0f)] private float _stoppedSettleDuration = 0.35f;
        [Tooltip("Minimum signed travel speed used to remember whether the player was moving forward or reversing before stopping.")]
        [SerializeField, Min(0.01f)] private float _movementDirectionThreshold = 0.05f;

        [Header("Instruction UI")]
        [SerializeField] private GameObject _instructionPanel;
        [SerializeField] private TMP_Text _instructionText;

        [Header("Unsafe crossing vehicle")]
        [SerializeField] private CrossingVehicleController _crossingVehicle;

        [Header("Unsafe crossing presentation")]
        [SerializeField] private GameObject _dangerPanel;
        [SerializeField] private GameObject _footAccidentCutscenePanel;
        [SerializeField] private VideoPlayer _footAccidentVideoPlayer;
        [SerializeField, Min(0.1f)] private float _missingVideoFallbackDuration = 3f;

        private PhaseState _state;
        private Vector3[] _fixedDirections;
        private Collider[] _playerColliders;
        private float _stoppedDuration;
        private float _finalConfirmationElapsed;
        private float _cutsceneElapsed;
        private bool _hazardWasReversing;
        private int _promptedDirectionIndex;

        public bool HasCompleted => _state == PhaseState.Completed;
        public bool IsRunning => _state == PhaseState.WaitingForStop ||
                                 _state == PhaseState.ConfirmingDirections ||
                                 _state == PhaseState.CompletingDirections;
        public event Action<GameFailureReason> FailureRequested;

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
                UpdateHazardTravelDirection();
                if ((_crossingVehicle == null || _crossingVehicle.IsCrossing) &&
                    _hazardWasReversing &&
                    IsVehicleStopped())
                    BeginFootAccidentCutscene();
                return;
            }

            if (!IsPhaseEligible()) return;

            if (IsVehicleInsideZone(_dangerZone))
            {
                TriggerUnsafeCrossing();
                return;
            }

            if (_state == PhaseState.Idle && IsVehicleInsideZone(_instructionZone))
                BeginSafetyCheck();

            if (_state == PhaseState.WaitingForStop)
            {
                if (IsVehicleStopped())
                {
                    _stoppedDuration += Time.deltaTime;
                    if (_stoppedDuration >= _stoppedSettleDuration) ShowDirectionTargets();
                }
                else _stoppedDuration = 0f;
                return;
            }

            if (_state == PhaseState.ConfirmingDirections && !IsVehicleStopped())
                TriggerUnsafeCrossing();
        }

        private void LateUpdate()
        {
            if ((_state != PhaseState.ConfirmingDirections &&
                 _state != PhaseState.CompletingDirections) ||
                _playerView == null ||
                _directionTargets == null || _fixedDirections == null) return;

            Vector3 forwardDirection = _fixedDirections.Length > 0
                ? _fixedDirections[0]
                : Vector3.ProjectOnPlane(_playerView.forward, Vector3.up).normalized;
            Vector3 origin = _playerView.position +
                             Vector3.up * _targetVerticalOffset +
                             forwardDirection * _targetForwardOffset;
            int count = Mathf.Min(_directionTargets.Length, _fixedDirections.Length);
            for (int index = 0; index < count; index++)
            {
                CrossDirectionPokeTarget target = _directionTargets[index];
                if (target == null) continue;
                float forwardAdjustment = index switch
                {
                    0 => _forwardTargetForwardAdjustment,
                    1 => _turnRightTargetForwardAdjustment,
                    _ => 0f
                };
                target.transform.position = origin +
                                            _fixedDirections[index] * _targetDistance +
                                            forwardDirection * forwardAdjustment;
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
            if (_instructionPanel != null) _instructionPanel.SetActive(true);
            SetTargetsActive(false, true);
            Debug.Log("[Cross Safety] Entered instruction zone. Waiting for the player to stop.", this);
        }

        private void ShowDirectionTargets()
        {
            _state = PhaseState.ConfirmingDirections;
            SetTargetsActive(true, true);
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
            _hazardWasReversing = GetVehicleTravelDirection() < 0;
            HidePresentation();
            if (_dangerPanel != null) _dangerPanel.SetActive(true);
            Debug.LogWarning(
                "[Cross Safety] Unsafe crossing detected. Spawning the NPC-driven vehicle hazard.",
                this);
            if (_crossingVehicle == null || _playerVehicle == null) return;
            _crossingVehicle.SpawnForHazard(_playerVehicle.worldCenterOfMass);
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

            Debug.LogWarning(
                "[Cross Safety] The player stopped while reversing after entering the hazard. " +
                "Playing the operator foot accident cutscene.",
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

        private void UpdateHazardTravelDirection()
        {
            int travelDirection = GetVehicleTravelDirection();
            if (travelDirection < 0)
                _hazardWasReversing = true;
            else if (travelDirection > 0)
                _hazardWasReversing = false;
        }

        private int GetVehicleTravelDirection()
        {
            float signedSpeed = _playerMotor != null ? _playerMotor.CurrentSpeed : 0f;
            if (Mathf.Abs(signedSpeed) <= _movementDirectionThreshold && _playerVehicle != null)
            {
                Vector3 forward = _playerMotor != null
                    ? _playerMotor.WorldForward
                    : Vector3.ProjectOnPlane(_playerVehicle.transform.forward, Vector3.up).normalized;
                signedSpeed = Vector3.Dot(
                    Vector3.ProjectOnPlane(_playerVehicle.linearVelocity, Vector3.up),
                    forward);
            }

            if (signedSpeed < -_movementDirectionThreshold) return -1;
            if (signedSpeed > _movementDirectionThreshold) return 1;
            return 0;
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
            _cutsceneElapsed = 0f;
            _hazardWasReversing = false;
            HidePresentation();
        }

        private void HandleGameStateChanged(GameState previous, GameState next)
        {
            if (next == GameState.WaitingToStart)
            {
                _state = PhaseState.Idle;
                _hazardWasReversing = false;
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
                _hazardWasReversing = false;
                _crossingVehicle?.SuppressVehicle();
            }
            else
            {
                _state = PhaseState.Idle;
                _hazardWasReversing = false;
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
            if (_instructionZone != null)
            {
                Gizmos.color = new Color(0.1f, 0.75f, 1f, 0.8f);
                Gizmos.DrawWireCube(_instructionZone.bounds.center, _instructionZone.bounds.size);
            }
            if (_dangerZone != null)
            {
                Gizmos.color = new Color(1f, 0.2f, 0.1f, 0.8f);
                Gizmos.DrawWireCube(_dangerZone.bounds.center, _dangerZone.bounds.size);
            }
        }
    }
}
