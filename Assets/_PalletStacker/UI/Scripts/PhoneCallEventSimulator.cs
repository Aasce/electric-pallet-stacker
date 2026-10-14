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
        [SerializeField] private PalletStackerForkCollisionSensor _forkCollisionSensor;
        [Tooltip("Defaults to the main camera and represents the player's head position.")]
        [SerializeField] private Transform _playerView;

        [Header("Call")]
        [SerializeField] private string _callerDisplay = "0123456789";
        [SerializeField, Min(0.1f)] private float _conversationDuration = 10f;

        [Header("Phone distraction NPC")]
        [Tooltip("Falls back to the population controller's NPC prefab when empty.")]
        [SerializeField] private NpcAgent _phoneNpcPrefab;
        [SerializeField, Min(0f)] private float _npcFrontClearance = 1.25f;
        [SerializeField, Min(0f)] private float _npcBehindPlayerClearance = 1.25f;
        [SerializeField, Min(0f)] private float _minimumCollisionGrace = 1f;
        [SerializeField, Min(0f)] private float _maximumCollisionGrace = 3f;
        [SerializeField, Min(0f)] private float _travelDirectionThreshold = 0.05f;
        [SerializeField, Min(0.1f)] private float _groundProbeHeight = 3f;
        [SerializeField, Min(0.1f)] private float _groundProbeDistance = 8f;
        [SerializeField, Min(0f)] private float _maximumGroundHeightDifference = 0.75f;
        [SerializeField] private LayerMask _groundLayers = ~0;

        private Coroutine _callRoutine;
        private Coroutine _collisionGraceRoutine;
        private NpcAgent _activePhoneNpc;
        private Transform _ringingTargetAnchor;
        private Vector3 _plannedNpcPosition;
        private bool _activeConversation;
        private bool _hasPlannedNpcPosition;
        private bool _phoneNpcImpactOccurred;
        private bool _triggeredThisRound;
        private float _callTravelSign = 1f;

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

        public void TriggerIncomingCall()
        {
            ResolveReferences();
            if (_triggeredThisRound ||
                (_appManager != null && _appManager.CurrentState != GameState.Playing)) return;

            _triggeredThisRound = true;
            _activeConversation = false;
            _phoneNpcImpactOccurred = false;
            _callTravelSign = ResolveTravelSign();
            PreparePlannedNpcPosition();
            _phoneCallPanel?.ShowIncomingCall(_callerDisplay);
            _phoneCallPanel?.SetOcclusionTarget(_ringingTargetAnchor);
            // An unanswered call intentionally rings forever and raises pedestrian pressure.
            _npcPopulation?.SetCrowdPressure(true);
        }

        public void ResetState()
        {
            _triggeredThisRound = false;
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
            if (nextState == GameState.Lost && _phoneNpcImpactOccurred)
            {
                StopCallKeepingImpactedNpc();
                return;
            }

            if (nextState != GameState.Playing)
            {
                ResetCallState();
                return;
            }

            if (previousState == GameState.Lost && _activePhoneNpc != null)
                ResetState();
        }

        private void HandleCallAccepted()
        {
            if (_activeConversation) return;
            _npcPopulation?.SetCrowdPressure(false);
            _activeConversation = true;
            SpawnPhoneNpc();
            _phoneCallPanel?.SetOcclusionTarget(_activePhoneNpc != null
                ? _activePhoneNpc.transform
                : null);
            ClearRingingTargetAnchor();
            CancelCallRoutine();
            _callRoutine = StartCoroutine(CompleteConversationAfter(_conversationDuration));
        }

        private IEnumerator CompleteConversationAfter(float duration)
        {
            yield return new WaitForSecondsRealtime(duration);
            _callRoutine = null;
            _activeConversation = false;
            ClearRingingTargetAnchor();
            DespawnPhoneNpc();
            _phoneCallPanel?.FinishCall();
        }

        private void SpawnPhoneNpc()
        {
            DespawnPhoneNpc();
            if (_vehicleBody == null) return;

            NpcAgent prefab = _phoneNpcPrefab != null
                ? _phoneNpcPrefab
                : _npcPopulation != null ? _npcPopulation.NpcPrefab : null;
            if (prefab == null)
            {
                Debug.LogWarning("No NPC prefab is configured for the phone distraction event.", this);
                return;
            }

            Vector3 travelDirection = GetVehicleForward() * _callTravelSign;
            Vector3 spawnPosition = _hasPlannedNpcPosition
                ? _plannedNpcPosition
                : CalculateNpcPosition();
            Quaternion spawnRotation = Quaternion.LookRotation(-travelDirection, Vector3.up);

            _activePhoneNpc = Instantiate(prefab, spawnPosition, spawnRotation, transform);
            _activePhoneNpc.name = "Phone Call Hazard NPC";
            _activePhoneNpc.VehicleCollisionStarted += HandlePhoneNpcCollisionStarted;
            _activePhoneNpc.Initialize(
                null,
                _npcPopulation != null ? _npcPopulation.VehicleTarget : null,
                null,
                0);
            _activePhoneNpc.SetRoundActive(true);
            _activePhoneNpc.BeginScriptedMovement();
            _activePhoneNpc.SetVehicleCollisionEnabled(false);

            float minimumGrace = Mathf.Min(_minimumCollisionGrace, _maximumCollisionGrace);
            float maximumGrace = Mathf.Max(_minimumCollisionGrace, _maximumCollisionGrace);
            float graceDuration = Random.Range(minimumGrace, maximumGrace);
            _collisionGraceRoutine = StartCoroutine(EnableNpcCollisionAfter(
                graceDuration,
                _activePhoneNpc));
        }

        private void PreparePlannedNpcPosition()
        {
            _plannedNpcPosition = CalculateNpcPosition();
            _hasPlannedNpcPosition = true;

            if (_ringingTargetAnchor == null)
            {
                GameObject anchorObject = new("Phone Call NPC Target");
                _ringingTargetAnchor = anchorObject.transform;
                _ringingTargetAnchor.SetParent(transform, true);
            }
            _ringingTargetAnchor.position = _plannedNpcPosition;
        }

        private Vector3 CalculateNpcPosition()
        {
            Vector3 vehicleForward = GetVehicleForward();
            Vector3 position = _callTravelSign >= 0f
                ? GetPointInFrontOfForks(vehicleForward)
                : GetPointBehindPlayer(vehicleForward);
            SnapToGround(ref position);
            return position;
        }

        private IEnumerator EnableNpcCollisionAfter(float delay, NpcAgent expectedNpc)
        {
            yield return new WaitForSecondsRealtime(delay);
            _collisionGraceRoutine = null;
            if (!_activeConversation || expectedNpc == null || expectedNpc != _activePhoneNpc)
                yield break;
            expectedNpc.SetVehicleCollisionEnabled(true);
        }

        private float ResolveTravelSign()
        {
            if (_vehicleMotor != null)
            {
                float signedSpeed = Mathf.Abs(_vehicleMotor.CurrentSpeed) > _travelDirectionThreshold
                    ? _vehicleMotor.CurrentSpeed
                    : _vehicleMotor.TargetSpeed;
                if (Mathf.Abs(signedSpeed) > _travelDirectionThreshold)
                    return Mathf.Sign(signedSpeed);
            }

            if (_vehicleBody != null)
            {
                Vector3 planarVelocity = Vector3.ProjectOnPlane(
                    _vehicleBody.linearVelocity,
                    Vector3.up);
                float signedVelocity = Vector3.Dot(planarVelocity, GetVehicleForward());
                if (Mathf.Abs(signedVelocity) > _travelDirectionThreshold)
                    return Mathf.Sign(signedVelocity);
            }

            return 1f;
        }

        private Vector3 GetVehicleForward()
        {
            Vector3 forward = _vehicleMotor != null
                ? Vector3.ProjectOnPlane(_vehicleMotor.WorldForward, Vector3.up)
                : Vector3.ProjectOnPlane(_vehicleBody.transform.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            return forward.normalized;
        }

        private Vector3 GetPointInFrontOfForks(Vector3 vehicleForward)
        {
            if (_forkCollisionSensor != null)
            {
                Collider forkCollider = _forkCollisionSensor.GetComponent<Collider>();
                if (forkCollider != null && forkCollider.enabled)
                {
                    Bounds bounds = forkCollider.bounds;
                    float projectedExtent = Mathf.Abs(vehicleForward.x) * bounds.extents.x +
                                            Mathf.Abs(vehicleForward.z) * bounds.extents.z;
                    Vector3 forkTip = bounds.center + vehicleForward * projectedExtent;
                    forkTip.y = GetExpectedGroundHeight();
                    return forkTip + vehicleForward * _npcFrontClearance;
                }

                Vector3 forkPosition = _forkCollisionSensor.transform.position;
                forkPosition.y = GetExpectedGroundHeight();
                return forkPosition + vehicleForward * _npcFrontClearance;
            }

            Debug.LogWarning("Fork collision sensor is missing; using vehicle origin for phone NPC placement.", this);
            Vector3 fallback = _vehicleBody.position;
            fallback.y = GetExpectedGroundHeight();
            return fallback + vehicleForward * _npcFrontClearance;
        }

        private Vector3 GetPointBehindPlayer(Vector3 vehicleForward)
        {
            if (_playerView == null && Camera.main != null) _playerView = Camera.main.transform;
            Vector3 playerPosition = _playerView != null
                ? _playerView.position
                : _vehicleBody.position;
            playerPosition.y = GetExpectedGroundHeight();
            return playerPosition - vehicleForward * _npcBehindPlayerClearance;
        }

        private void SnapToGround(ref Vector3 position)
        {
            float expectedGroundHeight = GetExpectedGroundHeight();
            position.y = expectedGroundHeight;
            Vector3 origin = new(
                position.x,
                expectedGroundHeight + _groundProbeHeight,
                position.z);
            RaycastHit[] hits = Physics.RaycastAll(
                origin,
                Vector3.down,
                _groundProbeHeight + _groundProbeDistance,
                _groundLayers,
                QueryTriggerInteraction.Ignore);
            float closestHeightDifference = float.PositiveInfinity;
            for (int index = 0; index < hits.Length; index++)
            {
                RaycastHit hit = hits[index];
                if (hit.collider == null || IsVehicleCollider(hit.collider)) continue;
                float heightDifference = Mathf.Abs(hit.point.y - expectedGroundHeight);
                if (heightDifference > _maximumGroundHeightDifference ||
                    heightDifference >= closestHeightDifference) continue;
                closestHeightDifference = heightDifference;
                position.y = hit.point.y;
            }
        }

        private float GetExpectedGroundHeight()
        {
            return _vehicleBody != null ? _vehicleBody.transform.position.y : 0f;
        }

        private bool IsVehicleCollider(Collider collider)
        {
            if (collider == null || _vehicleBody == null) return false;
            if (collider.attachedRigidbody == _vehicleBody) return true;
            Transform vehicleRoot = _vehicleBody.transform;
            return collider.transform == vehicleRoot || collider.transform.IsChildOf(vehicleRoot);
        }

        private void DespawnPhoneNpc()
        {
            CancelCollisionGraceRoutine();
            _phoneCallPanel?.SetOcclusionTarget(null);
            if (_activePhoneNpc == null) return;

            GameObject npcObject = _activePhoneNpc.gameObject;
            _activePhoneNpc.VehicleCollisionStarted -= HandlePhoneNpcCollisionStarted;
            _activePhoneNpc = null;
            npcObject.SetActive(false);
            Destroy(npcObject);
        }

        private void HandlePhoneNpcCollisionStarted()
        {
            _phoneNpcImpactOccurred = true;
            StopCallKeepingImpactedNpc();
        }

        private void StopCallKeepingImpactedNpc()
        {
            CancelCallRoutine();
            CancelCollisionGraceRoutine();
            _activeConversation = false;
            ClearRingingTargetAnchor();
            _activePhoneNpc?.SetVehicleCollisionEnabled(false);
            _phoneCallPanel?.SetOcclusionTarget(null);
            _phoneCallPanel?.FinishCall();
        }

        private void ClearRingingTargetAnchor()
        {
            _hasPlannedNpcPosition = false;
            if (_ringingTargetAnchor == null) return;

            GameObject anchorObject = _ringingTargetAnchor.gameObject;
            _ringingTargetAnchor = null;
            anchorObject.SetActive(false);
            Destroy(anchorObject);
        }

        private void CancelCollisionGraceRoutine()
        {
            if (_collisionGraceRoutine == null) return;
            StopCoroutine(_collisionGraceRoutine);
            _collisionGraceRoutine = null;
        }

        private void ResetCallState()
        {
            CancelCallRoutine();
            _activeConversation = false;
            _phoneNpcImpactOccurred = false;
            ClearRingingTargetAnchor();
            DespawnPhoneNpc();
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
            if (_forkCollisionSensor == null && _vehicleBody != null)
                _forkCollisionSensor = _vehicleBody.GetComponentInChildren<PalletStackerForkCollisionSensor>(true);
            if (_playerView == null && Camera.main != null)
                _playerView = Camera.main.transform;
        }

        private void CancelCallRoutine()
        {
            if (_callRoutine == null) return;
            StopCoroutine(_callRoutine);
            _callRoutine = null;
        }
    }
}
