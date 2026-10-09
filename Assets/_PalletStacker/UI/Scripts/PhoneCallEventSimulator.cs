using System.Collections;
using ElectricPalletStackers.Ble;
using ElectricPalletStackers.Gameplay;
using ElectricPalletStackers.NPCs;
using ElectricPalletStackers.PalletStackers;
using UnityEngine;

namespace ElectricPalletStackers.UI
{
    [DisallowMultipleComponent]
    public sealed class PhoneCallEventSimulator : MonoBehaviour, IGameResettable
    {
        [Header("References")]
        [SerializeField] private AppManager _appManager;
        [SerializeField] private PhoneCallPanel _phoneCallPanel;
        [SerializeField] private PalletStackerRigidbodyMotor _vehicleMotor;
        [SerializeField] private Rigidbody _vehicleBody;
        [SerializeField] private PalletStackerCollisionReporter _collisionReporter;
        [SerializeField] private NpcPopulationController _npcPopulation;

        [Header("Call")]
        [SerializeField] private string _callerDisplay = "0123456789";
        [SerializeField, Min(0.1f)] private float _conversationDuration = 10f;
        [SerializeField, Min(0f)] private float _movingSpeedThreshold = 0.1f;

        private Coroutine _callRoutine;
        private NpcVehicleRammer _activeNpcApproach;
        private bool _activeConversation;
        private bool _accidentTriggered;
        private bool _triggeredThisRound;

        public float ConversationDuration => _conversationDuration;
        public bool IsRinging => _phoneCallPanel != null && _phoneCallPanel.IsRinging;
        public bool IsInCall => _activeConversation;

        private void Awake() => ResolveReferences();

        private void OnEnable()
        {
            ResolveReferences();
            if (_phoneCallPanel != null) _phoneCallPanel.CallAccepted += HandleCallAccepted;
            if (_appManager != null)
            {
                _appManager.RoundStarted += HandleRoundStarted;
                _appManager.StateChanged += HandleGameStateChanged;
            }
        }

        private void OnDisable()
        {
            if (_phoneCallPanel != null) _phoneCallPanel.CallAccepted -= HandleCallAccepted;
            if (_appManager != null)
            {
                _appManager.RoundStarted -= HandleRoundStarted;
                _appManager.StateChanged -= HandleGameStateChanged;
            }
            ResetCallState();
        }

        private void Update()
        {
            if (!_activeConversation || _accidentTriggered) return;
            bool moving = IsVehicleMoving();
            if (moving && _activeNpcApproach == null) BeginNpcApproach();
            _activeNpcApproach?.SetVehicleMoving(moving);
        }

        public void TriggerIncomingCall()
        {
            ResolveReferences();
            if (_triggeredThisRound ||
                (_appManager != null && _appManager.CurrentState != GameState.Playing)) return;

            _triggeredThisRound = true;
            _activeConversation = false;
            _accidentTriggered = false;
            _phoneCallPanel?.ShowIncomingCall(_callerDisplay);
            // An unanswered call intentionally rings forever and raises pedestrian pressure.
            _npcPopulation?.SetCrowdPressure(true);
        }

        public void ResetState()
        {
            _triggeredThisRound = false;
            _accidentTriggered = false;
            ResetCallState();
        }

        [ContextMenu("Simulate Call Now")]
        private void SimulateCallNow()
        {
            _triggeredThisRound = false;
            TriggerIncomingCall();
        }

        private void HandleRoundStarted() => ResetState();

        private void HandleGameStateChanged(GameState previousState, GameState nextState)
        {
            if (nextState != GameState.Playing) ResetCallState();
        }

        private void HandleCallAccepted()
        {
            if (_activeConversation) return;
            _npcPopulation?.SetCrowdPressure(false);
            _activeConversation = true;
            bool moving = IsVehicleMoving();
            if (moving) BeginNpcApproach();
            _activeNpcApproach?.SetVehicleMoving(moving);
            CancelCallRoutine();
            _callRoutine = StartCoroutine(CompleteConversationAfter(_conversationDuration));
        }

        private IEnumerator CompleteConversationAfter(float duration)
        {
            yield return new WaitForSecondsRealtime(duration);
            _callRoutine = null;
            _activeConversation = false;
            StopNpcApproach();
            _phoneCallPanel?.FinishCall();
        }

        private bool IsVehicleMoving()
        {
            if (_vehicleMotor != null && Mathf.Abs(_vehicleMotor.CurrentSpeed) > _movingSpeedThreshold)
                return true;
            if (_vehicleBody == null) return false;
            Vector3 planarVelocity = Vector3.ProjectOnPlane(_vehicleBody.linearVelocity, Vector3.up);
            return planarVelocity.sqrMagnitude > _movingSpeedThreshold * _movingSpeedThreshold;
        }

        private void BeginNpcApproach()
        {
            if (_activeNpcApproach != null || _vehicleBody == null) return;
            NpcAgent closestNpc = _npcPopulation != null
                ? _npcPopulation.FindClosestAvailableNpc(_vehicleBody.position)
                : FindClosestNpc();
            if (closestNpc == null)
            {
                Debug.LogWarning("No active waypoint NPC was available for the phone distraction event.", this);
                return;
            }

            _activeNpcApproach = closestNpc.GetComponent<NpcVehicleRammer>();
            if (_activeNpcApproach == null)
                _activeNpcApproach = closestNpc.gameObject.AddComponent<NpcVehicleRammer>();
            _activeNpcApproach.BeginApproach(
                closestNpc,
                _vehicleBody,
                _vehicleMotor,
                _collisionReporter,
                _movingSpeedThreshold,
                HandleNpcImpact);
        }

        private NpcAgent FindClosestNpc()
        {
            NpcAgent[] npcs = FindObjectsByType<NpcAgent>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            NpcAgent closest = null;
            float closestSqrDistance = float.PositiveInfinity;
            for (int index = 0; index < npcs.Length; index++)
            {
                NpcAgent npc = npcs[index];
                if (npc == null || !npc.IsAvailableForPhoneEvent) continue;
                float sqrDistance = (npc.transform.position - _vehicleBody.position).sqrMagnitude;
                if (sqrDistance >= closestSqrDistance) continue;
                closestSqrDistance = sqrDistance;
                closest = npc;
            }
            return closest;
        }

        private void StopNpcApproach()
        {
            if (_activeNpcApproach == null) return;
            _activeNpcApproach.CancelApproach();
            _activeNpcApproach = null;
        }

        private void HandleNpcImpact()
        {
            _activeNpcApproach = null;
            _accidentTriggered = true;
            _activeConversation = false;
            CancelCallRoutine();
            _phoneCallPanel?.FinishCall();
        }

        private void ResetCallState()
        {
            CancelCallRoutine();
            _activeConversation = false;
            StopNpcApproach();
            _npcPopulation?.SetCrowdPressure(false);
            _phoneCallPanel?.HideImmediate();
        }

        private void ResolveReferences()
        {
            if (_appManager == null)
                _appManager = FindFirstObjectByType<AppManager>(FindObjectsInactive.Include);
            if (_phoneCallPanel == null)
                _phoneCallPanel = FindFirstObjectByType<PhoneCallPanel>(FindObjectsInactive.Include);
            if (_vehicleMotor == null)
                _vehicleMotor = FindFirstObjectByType<PalletStackerRigidbodyMotor>(FindObjectsInactive.Include);
            if (_vehicleBody == null && _vehicleMotor != null)
                _vehicleBody = _vehicleMotor.GetComponent<Rigidbody>();
            if (_collisionReporter == null)
                _collisionReporter = FindFirstObjectByType<PalletStackerCollisionReporter>(FindObjectsInactive.Include);
            if (_npcPopulation == null)
                _npcPopulation = FindFirstObjectByType<NpcPopulationController>(FindObjectsInactive.Include);
        }

        private void CancelCallRoutine()
        {
            if (_callRoutine == null) return;
            StopCoroutine(_callRoutine);
            _callRoutine = null;
        }
    }
}
