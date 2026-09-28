using System.Collections;
using ElectricPalletStackers.Ble;
using ElectricPalletStackers.Gameplay;
using ElectricPalletStackers.NPCs;
using ElectricPalletStackers.PalletStackers;
using UnityEngine;

namespace ElectricPalletStackers.UI
{
    [DisallowMultipleComponent]
    public sealed class PhoneCallEventSimulator : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private AppManager _appManager;
        [SerializeField] private PhoneCallPanel _phoneCallPanel;
        [SerializeField] private PalletStackerRigidbodyMotor _vehicleMotor;
        [SerializeField] private Rigidbody _vehicleBody;
        [SerializeField] private PalletStackerCollisionReporter _collisionReporter;

        [Header("Call Timing")]
        [SerializeField] private string _callerDisplay = "0123456789";
        [SerializeField] private Vector2 _initialDelayRange = new(5f, 10f);
        [SerializeField] private Vector2 _ringDurationRange = new(10f, 15f);
        [SerializeField] private Vector2 _conversationDurationRange = new(5f, 10f);
        [SerializeField, Min(0f)] private float _rejectRetryDelay = 5f;

        [Header("Safety Consequence")]
        [SerializeField, Min(0f)] private float _movingSpeedThreshold = 0.1f;

        private Coroutine _callRoutine;
        private NpcVehicleRammer _activeNpcApproach;
        private float _retryAt = -1f;
        private bool _eventScheduledThisRound;
        private bool _activeConversation;
        private bool _accidentTriggered;

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            ResolveReferences();
            if (_phoneCallPanel != null)
            {
                _phoneCallPanel.CallAccepted += HandleCallAccepted;
                _phoneCallPanel.CallRejected += HandleCallRejected;
                _phoneCallPanel.CallEndedByUser += HandleCallEndedByUser;
            }

            if (_appManager == null) return;

            _appManager.RoundStarted += HandleRoundStarted;
            _appManager.StateChanged += HandleGameStateChanged;
            if (_appManager.CurrentState == GameState.Playing) ScheduleInitialCall();
        }

        private void OnDisable()
        {
            if (_phoneCallPanel != null)
            {
                _phoneCallPanel.CallAccepted -= HandleCallAccepted;
                _phoneCallPanel.CallRejected -= HandleCallRejected;
                _phoneCallPanel.CallEndedByUser -= HandleCallEndedByUser;
            }

            if (_appManager != null)
            {
                _appManager.RoundStarted -= HandleRoundStarted;
                _appManager.StateChanged -= HandleGameStateChanged;
            }

            CancelCallRoutine();
            CancelRetryCall();
            StopNpcApproach();
        }

        [ContextMenu("Simulate Call Now")]
        public void SimulateCallNow()
        {
            ResolveReferences();
            CancelCallRoutine();
            CancelRetryCall();
            BeginRinging();
        }

        private void Update()
        {
            if (_retryAt >= 0f && Time.realtimeSinceStartup >= _retryAt)
            {
                _retryAt = -1f;
                if (_appManager == null || _appManager.CurrentState == GameState.Playing)
                    BeginRinging();
            }

            if (!_activeConversation || _accidentTriggered)
            {
                StopNpcApproach();
                return;
            }

            if (IsVehicleMoving())
                BeginNpcApproach();
            else
                StopNpcApproach();
        }

        private void HandleRoundStarted()
        {
            _eventScheduledThisRound = false;
            _accidentTriggered = false;
            StopNpcApproach();
            ScheduleInitialCall();
        }

        private void HandleGameStateChanged(GameState previousState, GameState nextState)
        {
            if (nextState == GameState.Playing) return;

            CancelCallRoutine();
            CancelRetryCall();
            _activeConversation = false;
            StopNpcApproach();
            _phoneCallPanel?.HideImmediate();
        }

        private void ScheduleInitialCall()
        {
            if (_eventScheduledThisRound) return;

            _eventScheduledThisRound = true;
            CancelCallRoutine();
            CancelRetryCall();
            _phoneCallPanel?.HideImmediate();
            _callRoutine = StartCoroutine(ShowCallAfterDelay(RandomInRange(_initialDelayRange)));
        }

        private IEnumerator ShowCallAfterDelay(float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            _callRoutine = null;

            if (_appManager != null && _appManager.CurrentState != GameState.Playing) yield break;
            BeginRinging();
        }

        private void BeginRinging()
        {
            _activeConversation = false;
            _phoneCallPanel?.ShowIncomingCall(_callerDisplay);
            CancelCallRoutine();
            _callRoutine = StartCoroutine(HideUnansweredCallAfter(RandomInRange(_ringDurationRange)));
        }

        private IEnumerator HideUnansweredCallAfter(float duration)
        {
            yield return new WaitForSecondsRealtime(duration);
            _callRoutine = null;
            _phoneCallPanel?.Hide();
            ScheduleRetryCall();
        }

        private void HandleCallAccepted()
        {
            CancelCallRoutine();
            _activeConversation = true;
            _callRoutine = StartCoroutine(CompleteConversationAfter(
                RandomInRange(_conversationDurationRange)));
        }

        private IEnumerator CompleteConversationAfter(float duration)
        {
            yield return new WaitForSecondsRealtime(duration);
            _callRoutine = null;
            _activeConversation = false;
            StopNpcApproach();
            _phoneCallPanel?.FinishCall();
        }

        private void HandleCallRejected()
        {
            CancelCallRoutine();
            _activeConversation = false;
            StopNpcApproach();
            ScheduleRetryCall();
        }

        private void HandleCallEndedByUser()
        {
            CancelCallRoutine();
            _activeConversation = false;
            StopNpcApproach();
            ScheduleRetryCall();
        }

        private void ScheduleRetryCall()
        {
            _retryAt = Time.realtimeSinceStartup + _rejectRetryDelay;
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
            if (_activeNpcApproach != null) return;

            NpcAgent closestNpc = FindClosestNpc();
            if (closestNpc == null || _vehicleBody == null)
            {
                Debug.LogWarning("No active NPC was available for the phone-distraction accident.", this);
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
            CancelRetryCall();
            _phoneCallPanel?.FinishCall();
        }

        private NpcAgent FindClosestNpc()
        {
            if (_vehicleBody == null) return null;

            NpcAgent[] npcs = FindObjectsByType<NpcAgent>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            NpcAgent closest = null;
            float closestSqrDistance = float.PositiveInfinity;
            for (int index = 0; index < npcs.Length; index++)
            {
                NpcAgent npc = npcs[index];
                if (npc == null || npc.Agent == null || !npc.Agent.isOnNavMesh) continue;

                float sqrDistance = (npc.transform.position - _vehicleBody.position).sqrMagnitude;
                if (sqrDistance >= closestSqrDistance) continue;
                closestSqrDistance = sqrDistance;
                closest = npc;
            }

            return closest;
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
        }

        private void CancelCallRoutine()
        {
            if (_callRoutine == null) return;
            StopCoroutine(_callRoutine);
            _callRoutine = null;
        }

        private void CancelRetryCall()
        {
            _retryAt = -1f;
        }

        private static float RandomInRange(Vector2 range)
        {
            return Random.Range(Mathf.Min(range.x, range.y), Mathf.Max(range.x, range.y));
        }
    }
}
