using ElectricPalletStackers.PalletStackers;
using TMPro;
using UnityEngine;

namespace ElectricPalletStackers.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class CrossSafetyPhaseController : MonoBehaviour
    {
        private enum PhaseState
        {
            Idle,
            WaitingForStop,
            ConfirmingDirections,
            HazardActive,
            Completed
        }

        [Header("Flow")]
        [SerializeField] private AppManager _appManager;
        [SerializeField] private TrainingExperienceController _trainingExperience;

        [Header("Player")]
        [SerializeField] private Rigidbody _playerVehicle;
        [SerializeField] private PalletStackerRigidbodyMotor _playerMotor;
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
        [SerializeField, Range(0.3f, 0.8f)] private float _targetDistance = 0.45f;
        [Tooltip("Vertical offset from the player's view. Negative values place spheres below eye level.")]
        [SerializeField, Range(-0.5f, 0.25f)] private float _targetVerticalOffset = -0.18f;

        [Header("Stop validation")]
        [SerializeField, Min(0.01f)] private float _stoppedSpeedThreshold = 0.05f;
        [SerializeField, Min(0f)] private float _stoppedSettleDuration = 0.35f;

        [Header("Instruction UI")]
        [SerializeField] private GameObject _instructionPanel;
        [SerializeField] private TMP_Text _instructionText;

        [Header("Unsafe crossing vehicle")]
        [SerializeField] private CrossingVehicleController _crossingVehicle;

        private PhaseState _state;
        private Vector3[] _fixedDirections;
        private Collider[] _playerColliders;
        private float _stoppedDuration;

        public bool HasCompleted => _state == PhaseState.Completed;
        public bool IsRunning => _state == PhaseState.WaitingForStop ||
                                 _state == PhaseState.ConfirmingDirections;

        private void Awake()
        {
            if (_playerVehicle != null)
                _playerColliders = _playerVehicle.GetComponentsInChildren<Collider>(true);
            CacheFixedDirections();
            ConfigureTargets();
            ConfigureInstructionPanel();
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
            HidePresentation();
        }

        private void Update()
        {
            if (_playerVehicle == null ||
                (_appManager != null && _appManager.CurrentState != GameState.Playing) ||
                _state == PhaseState.Completed || _state == PhaseState.HazardActive)
                return;

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
            if (_state != PhaseState.ConfirmingDirections || _playerView == null ||
                _directionTargets == null || _fixedDirections == null) return;

            Vector3 origin = _playerView.position + Vector3.up * _targetVerticalOffset;
            int count = Mathf.Min(_directionTargets.Length, _fixedDirections.Length);
            for (int index = 0; index < count; index++)
            {
                CrossDirectionPokeTarget target = _directionTargets[index];
                if (target == null) continue;
                target.transform.position = origin + _fixedDirections[index] * _targetDistance;
                target.transform.rotation = Quaternion.LookRotation(
                    _fixedDirections[index],
                    Vector3.up);
            }
        }

        public void NotifyDirectionConfirmed(CrossDirectionPokeTarget confirmedTarget)
        {
            if (_state != PhaseState.ConfirmingDirections || confirmedTarget == null) return;

            for (int index = 0; index < _directionTargets.Length; index++)
            {
                CrossDirectionPokeTarget target = _directionTargets[index];
                if (target != null && !target.IsConfirmed) return;
            }
            CompleteSafetyCheck();
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
            Debug.Log("[Cross Safety] Vehicle stopped. Direction targets are now active.", this);
        }

        private void CompleteSafetyCheck()
        {
            _state = PhaseState.Completed;
            HidePresentation();
            _crossingVehicle?.SuppressVehicle();

            if (_trainingExperience == null) return;
            if (_trainingExperience.CurrentStage == TrainingExperienceStage.NavigateCorner)
                _trainingExperience.ReachCheckpoint(ExperienceCheckpoint.Corner);
            if (_trainingExperience.CurrentStage == TrainingExperienceStage.YieldIntersection)
                _trainingExperience.ReachCheckpoint(ExperienceCheckpoint.YieldIntersection);
        }

        private void TriggerUnsafeCrossing()
        {
            _state = PhaseState.HazardActive;
            HidePresentation();
            Debug.LogWarning("[Cross Safety] Unsafe crossing detected. Spawning the NPC hazard.", this);
            if (_crossingVehicle == null || _playerVehicle == null) return;
            _crossingVehicle.SpawnForHazard(_playerVehicle.worldCenterOfMass);
            _crossingVehicle.BeginCrossing();
        }

        private bool IsVehicleStopped()
        {
            float motorSpeed = _playerMotor != null ? Mathf.Abs(_playerMotor.CurrentSpeed) : 0f;
            Vector3 planarVelocity = Vector3.ProjectOnPlane(_playerVehicle.linearVelocity, Vector3.up);
            return motorSpeed <= _stoppedSpeedThreshold &&
                   planarVelocity.sqrMagnitude <= _stoppedSpeedThreshold * _stoppedSpeedThreshold;
        }

        private bool IsPhaseEligible()
        {
            if (_trainingExperience == null) return true;
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
                _trainingExperience.CurrentStage >= TrainingExperienceStage.PhoneIntersection)
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
                _directionTargets[index]?.Configure(this);
        }

        private void ConfigureInstructionPanel()
        {
            if (_instructionPanel == null) return;
            Canvas panelCanvas = _instructionPanel.GetComponent<Canvas>();
            if (panelCanvas == null) return;

            panelCanvas.overrideSorting = true;
            panelCanvas.sortingOrder = short.MaxValue;
        }

        private void HidePresentation()
        {
            if (_instructionPanel != null) _instructionPanel.SetActive(false);
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
