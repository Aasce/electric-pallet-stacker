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
        [SerializeField] private NpcPopulationController _npcPopulation;

        private bool _crossTriggered;
        private bool _phoneTriggered;
        private bool _npcTriggered;

        private void OnEnable()
        {
            if (_appManager != null) _appManager.RoundStarted += ResetPhase;
        }

        private void OnDisable()
        {
            if (_appManager != null) _appManager.RoundStarted -= ResetPhase;
        }

        private void Update()
        {
            if (_playerVehicle == null ||
                (_appManager != null && _appManager.CurrentState != GameState.Playing)) return;

            Vector3 vehiclePosition = _playerVehicle.worldCenterOfMass;
            if (_crossSafetyPhase == null && !_crossTriggered && IsNearCross(vehiclePosition))
                TriggerCross();
            if (!_phoneTriggered && IsInsideZone(_phoneZone, vehiclePosition)) TriggerPhoneCall();
            if (!_npcTriggered && IsInsideZone(_npcZone, vehiclePosition)) TriggerNpcArea();
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
        }

        private void TriggerPhoneCall()
        {
            _phoneTriggered = true;
            bool handledByTraining = _trainingExperience != null &&
                                     _trainingExperience.ReachCheckpoint(
                                         ExperienceCheckpoint.PhoneIntersection);
            if (!handledByTraining) _phoneCallEvent?.TriggerIncomingCall();
        }

        private void TriggerNpcArea()
        {
            _npcTriggered = true;
            _npcPopulation?.ActivatePopulation();
        }

        private void ResetPhase()
        {
            _crossTriggered = false;
            _phoneTriggered = false;
            _npcTriggered = false;
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
