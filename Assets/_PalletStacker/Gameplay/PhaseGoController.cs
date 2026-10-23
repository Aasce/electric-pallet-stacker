using ElectricPalletStackers.NPCs;
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

        [Header("Cross")]
        [SerializeField] private Transform _crossMarker;
        [SerializeField, Min(0.1f)] private float _crossActivationDistance = 6.5f;
        [SerializeField] private CrossingVehicleController _crossingVehicle;
        [Tooltip("When assigned, the dedicated stop-and-point safety flow owns the Cross phase.")]
        [SerializeField] private CrossSafetyPhaseController _crossSafetyPhase;

        [Header("Phone call")]
        [SerializeField] private Collider _phoneZone;
        [SerializeField] private PhoneCallEventSimulator _phoneCallEvent;

        [Header("NPC area")]
        [SerializeField] private Collider _npcZone;
        [Tooltip("The NPC Moving phase completes only after the player vehicle enters this trigger.")]
        [SerializeField] private Collider _npcCompletedZone;
        [SerializeField] private NpcPopulationController _npcPopulation;

        private bool _crossTriggered;
        private bool _phoneTriggered;
        private bool _npcTriggered;
        private bool _npcCompleted;
        private bool _wasInsidePhoneZone;

        private void Awake()
        {
            if (_checkpointController == null)
                _checkpointController = FindFirstObjectByType<TrainingCheckpointController>();
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
            if (_crossSafetyPhase == null && !_crossTriggered && IsNearCross(vehiclePosition))
                TriggerCross();
            bool isInsidePhoneZone = IsInsideZone(_phoneZone, vehiclePosition);
            if (isInsidePhoneZone && !_wasInsidePhoneZone)
                _phoneCallEvent?.CaptureTriggerEntryDirection();
            _wasInsidePhoneZone = isInsidePhoneZone;
            if (!_phoneTriggered &&
                CanTriggerPhoneCall() &&
                isInsidePhoneZone) TriggerPhoneCall();
            if (!_npcTriggered && IsInsideZone(_npcZone, vehiclePosition)) TriggerNpcArea();
            if (!_npcCompleted && IsInsideZone(_npcCompletedZone, vehiclePosition))
                CompleteNpcArea();
        }

        private bool IsNearCross(Vector3 vehiclePosition)
        {
            if (_crossMarker == null) return false;
            Vector3 offset = Vector3.ProjectOnPlane(
                vehiclePosition - _crossMarker.position,
                Vector3.up);
            return offset.sqrMagnitude <= _crossActivationDistance * _crossActivationDistance;
        }

        private static bool IsInsideZone(Collider zone, Vector3 position)
        {
            if (zone == null || !zone.enabled || !zone.gameObject.activeInHierarchy) return false;
            Vector3 closestPoint = zone.ClosestPoint(position);
            return (closestPoint - position).sqrMagnitude <= 0.0001f;
        }

        private void TriggerCross()
        {
            _crossTriggered = true;
            bool handledByTraining = false;
            if (_trainingExperience != null)
            {
                if (_trainingExperience.CurrentStage == TrainingExperienceStage.NavigateCorner)
                    _trainingExperience.ReachCheckpoint(ExperienceCheckpoint.Corner);
                if (_trainingExperience.CurrentStage == TrainingExperienceStage.YieldIntersection)
                    handledByTraining = _trainingExperience.ReachCheckpoint(
                        ExperienceCheckpoint.YieldIntersection);
            }
            if (!handledByTraining) _crossingVehicle?.BeginCrossing();
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
            // The NavigateCorner checkpoint is captured before the player enters the
            // NPC zone, so restoring it correctly deactivates the population. Re-arm
            // the zone trigger as well or those NPCs can never be activated again.
            _npcTriggered = stage > TrainingExperienceStage.NavigateCorner;
            _npcCompleted = stage > TrainingExperienceStage.NavigateCorner;
            _phoneTriggered = stage >= TrainingExperienceStage.PhoneCall;
        }

        private void TriggerNpcArea()
        {
            _checkpointController?.CapturePhaseEntryDirection();
            _npcTriggered = true;
            _npcPopulation?.ActivatePopulation();
        }

        private void CompleteNpcArea()
        {
            if (_trainingExperience == null)
            {
                _npcCompleted = true;
                _checkpointController?.CapturePhaseEntryDirection();
                return;
            }

            _npcCompleted = _trainingExperience.CompleteNpcMovingPhase() ||
                            _trainingExperience.CurrentStage >
                            TrainingExperienceStage.NavigateCorner;
            if (_npcCompleted)
                _checkpointController?.CapturePhaseEntryDirection();
        }

        private bool CanTriggerPhoneCall()
        {
            if (_trainingExperience == null) return _npcCompleted;

            TrainingExperienceStage stage = _trainingExperience.CurrentStage;
            return stage == TrainingExperienceStage.PhoneIntersection;
        }

        private void ResetPhase()
        {
            _crossTriggered = false;
            _phoneTriggered = false;
            _npcTriggered = false;
            _npcCompleted = false;
            _wasInsidePhoneZone = false;
            _crossingVehicle?.ResetVehicle();
        }

        private void OnDrawGizmosSelected()
        {
            if (_crossMarker == null) return;
            Gizmos.color = new Color(1f, 0.65f, 0.1f, 0.8f);
            Gizmos.DrawWireSphere(_crossMarker.position, _crossActivationDistance);
        }
    }
}
