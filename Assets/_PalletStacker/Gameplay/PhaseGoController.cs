using ElectricPalletStackers.UI;
using UnityEngine;

namespace ElectricPalletStackers.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class PhaseGoController : MonoBehaviour
    {
        [Header("Round")]
        [SerializeField] private AppManager _appManager;
        [SerializeField] private TrainingExperienceController _trainingExperience;
        [SerializeField] private TrainingCheckpointController _checkpointController;
        [SerializeField] private Rigidbody _playerVehicle;

        [Header("Phase boundaries")]
        [SerializeField] private Collider _pickupPalletZone;
        [SerializeField] private Collider _crossZone;
        [SerializeField] private CrossingVehicleController _crossingVehicle;
        [Tooltip("The dedicated safety flow that owns Cross when assigned.")]
        [SerializeField] private CrossSafetyPhaseController _crossSafetyPhase;

        [Header("Phone call")]
        [SerializeField] private Collider _phoneZone;
        [SerializeField] private PhoneCallEventSimulator _phoneCallEvent;

        private bool _crossTriggered;
        private bool _phoneTriggered;
        private bool _wasInsidePhoneZone;
        private Collider[] _playerColliders;

        private void Awake()
        {
            if (_checkpointController == null)
                _checkpointController = FindFirstObjectByType<TrainingCheckpointController>();
            if (_playerVehicle != null)
                _playerColliders = _playerVehicle.GetComponentsInChildren<Collider>(true);
        }

        private void OnEnable()
        {
            if (_appManager != null)
            {
                _appManager.RoundStarted += ResetPhase;
                _appManager.StateChanged += HandleGameStateChanged;
            }
        }

        private void OnDisable()
        {
            if (_appManager != null)
            {
                _appManager.RoundStarted -= ResetPhase;
                _appManager.StateChanged -= HandleGameStateChanged;
            }
            _wasInsidePhoneZone = false;
        }

        private void Update()
        {
            if (_playerVehicle == null ||
                (_appManager != null && _appManager.CurrentState != GameState.Playing)) return;

            Vector3 vehiclePosition = _playerVehicle.worldCenterOfMass;
            if (_trainingExperience != null &&
                _trainingExperience.CurrentStage == TrainingExperienceStage.Start &&
                IsInsideZone(_pickupPalletZone, vehiclePosition) &&
                _trainingExperience.BeginPickupPalletPhase())
                _checkpointController?.CapturePhaseEntryDirection();
            if (!_crossTriggered && IsInsideZone(_crossZone, vehiclePosition))
                TriggerCross();

            bool isInsidePhoneZone = IsInsideZone(_phoneZone, vehiclePosition);
            if (isInsidePhoneZone && !_wasInsidePhoneZone)
                _phoneCallEvent?.CaptureTriggerEntryDirection();
            _wasInsidePhoneZone = isInsidePhoneZone;
            if (!_phoneTriggered &&
                CanTriggerPhoneCall() &&
                isInsidePhoneZone) TriggerPhoneCall();
        }

        private bool IsInsideZone(Collider zone, Vector3 position)
        {
            if (zone == null || !zone.enabled || !zone.gameObject.activeInHierarchy) return false;
            Vector3 closestPoint = zone.ClosestPoint(position);
            if ((closestPoint - position).sqrMagnitude <= 0.0001f) return true;

            if (_playerColliders == null || _playerColliders.Length == 0)
            {
                if (_playerVehicle == null) return false;
                _playerColliders = _playerVehicle.GetComponentsInChildren<Collider>(true);
            }

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

        private void TriggerCross()
        {
            _crossTriggered = true;
            bool handledByTraining = _trainingExperience != null &&
                                     _trainingExperience.BeginCrossPhase();
            if (!handledByTraining || _crossSafetyPhase == null)
            {
                _crossingVehicle?.BeginCrossing();
                if (handledByTraining)
                    _trainingExperience.CompleteCrossPhaseWithoutSafetyCheck();
            }
            _checkpointController?.CapturePhaseEntryDirection();
        }

        private void TriggerPhoneCall()
        {
            if (_trainingExperience != null)
            {
                _phoneTriggered = _trainingExperience.BeginPhoneCall();
                if (_phoneTriggered)
                    _checkpointController?.CapturePhaseEntryDirection();
                return;
            }

            _phoneTriggered = true;
            _phoneCallEvent?.TriggerIncomingCall();
            _checkpointController?.CapturePhaseEntryDirection();
        }

        private void HandleGameStateChanged(GameState previous, GameState next)
        {
            if (previous != GameState.Lost || next != GameState.Playing) return;

            _wasInsidePhoneZone = false;
            if (_trainingExperience == null) return;

            TrainingExperienceStage stage = _trainingExperience.CurrentStage;
            _phoneTriggered = stage >= TrainingExperienceStage.PhoneCall;
        }

        private bool CanTriggerPhoneCall()
        {
            if (_trainingExperience == null) return true;

            TrainingExperienceStage stage = _trainingExperience.CurrentStage;
            return stage == TrainingExperienceStage.PhoneIntersection;
        }

        private void ResetPhase()
        {
            _crossTriggered = false;
            _phoneTriggered = false;
            _wasInsidePhoneZone = false;
            _crossingVehicle?.ResetVehicle();
        }

        private void OnDrawGizmosSelected()
        {
            if (_crossZone == null) return;
            Gizmos.color = new Color(1f, 0.65f, 0.1f, 0.8f);
            Gizmos.DrawWireCube(_crossZone.bounds.center, _crossZone.bounds.size);
        }
    }
}
