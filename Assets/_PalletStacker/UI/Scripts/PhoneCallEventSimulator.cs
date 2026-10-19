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
        [SerializeField] private PhoneCallWorldPlacement _phonePlacement;
        [SerializeField] private PalletStackerRigidbodyMotor _vehicleMotor;
        [SerializeField] private Rigidbody _vehicleBody;
        [SerializeField] private NpcPopulationController _npcPopulation;
        [SerializeField] private PalletStackerForkCollisionSensor _forkCollisionSensor;
        [Tooltip("Collider on the fork sensor. Assigned explicitly so runtime code never searches for it.")]
        [SerializeField] private Collider _forkCollider;
        [Tooltip("The player's head/view transform, assigned in the Inspector.")]
        [SerializeField] private Transform _playerView;

        [Header("Call")]
        [SerializeField] private string _callerDisplay = "0123456789";
        [SerializeField] private bool _applyCrowdPressureWhileRinging = true;
        [Tooltip("Spawn the road-blocking NPC as soon as the phone starts ringing.")]
        [SerializeField] private bool _spawnBlockerOnIncomingCall = true;

        [Header("Phone distraction NPC")]
        [SerializeField] private NpcAgent _phoneNpcPrefab;
        [SerializeField, Min(0f)] private float _npcFrontClearance = 4f;
        [SerializeField, Min(0f)] private float _npcBehindPlayerClearance = 4f;
        [SerializeField, Min(0f)] private float _minimumCollisionGrace = 1f;
        [SerializeField, Min(0f)] private float _maximumCollisionGrace = 3f;
        [SerializeField, Min(0f)] private float _travelDirectionThreshold = 0.05f;
        [SerializeField, Min(0.1f)] private float _groundProbeHeight = 3f;
        [SerializeField, Min(0.1f)] private float _groundProbeDistance = 8f;
        [SerializeField, Min(0f)] private float _maximumGroundHeightDifference = 0.75f;
        [SerializeField] private LayerMask _groundLayers = ~0;

        private Coroutine _collisionGraceRoutine;
        private NpcAgent _activePhoneNpc;
        private bool _activeConversation;
        private bool _phoneNpcImpactOccurred;
        private bool _hasTriggerEntryDirection;
        private float _callTravelSign = 1f;

        public bool IsRinging => _phoneCallPanel != null && _phoneCallPanel.IsRinging;
        public bool IsInCall => _activeConversation;

        public event System.Action CallCompleted;

        private void OnEnable()
        {
            if (_phoneCallPanel != null)
            {
                _phoneCallPanel.CallAccepted += HandleCallAccepted;
                _phoneCallPanel.CallEnded += HandleCallEnded;
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
            if (_appManager != null)
            {
                _appManager.RoundStarted -= HandleRoundStarted;
                _appManager.StateChanged -= HandleGameStateChanged;
            }
            _hasTriggerEntryDirection = false;
            ResetCallState();
        }

        private void LateUpdate()
        {
            if (_activePhoneNpc == null || _activeConversation || !IsRinging) return;
            UpdatePhoneNpcFollowPose();
        }

        public void TriggerIncomingCall()
        {
            if ((_appManager != null && _appManager.CurrentState != GameState.Playing) ||
                IsRinging || _activeConversation) return;

            ResetCallState();
            BeginIncomingCall();
        }

        public void CaptureTriggerEntryDirection()
        {
            // The trigger entry chooses the blocker side. Keep that side while the
            // vehicle remains in the zone, even if the driver reverses direction.
            _callTravelSign = ResolveTravelSign(_callTravelSign);
            _hasTriggerEntryDirection = true;

            if (_activePhoneNpc != null && !_activeConversation && IsRinging)
                UpdatePhoneNpcFollowPose();
        }

        private void BeginIncomingCall()
        {
            _activeConversation = false;
            _phoneNpcImpactOccurred = false;
            if (!_hasTriggerEntryDirection)
                _callTravelSign = ResolveTravelSign(1f);
            if (_spawnBlockerOnIncomingCall) SpawnPhoneNpc();
            _phoneCallPanel?.ShowIncomingCall(_callerDisplay);
            if (_activePhoneNpc != null)
                _phonePlacement?.ShowOccluding(_activePhoneNpc.transform);
            else
                _phonePlacement?.ShowInFrontOfViewer();
            // An unanswered call intentionally rings forever and raises pedestrian pressure.
            if (_applyCrowdPressureWhileRinging)
                _npcPopulation?.SetCrowdPressure(true);
        }

        public void ResetState()
        {
            _hasTriggerEntryDirection = false;
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
            _hasTriggerEntryDirection = false;
            RestartIncomingCall();
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
            if (_activeConversation ||
                (_appManager != null && _appManager.CurrentState != GameState.Playing)) return;
            if (_applyCrowdPressureWhileRinging)
                _npcPopulation?.SetCrowdPressure(false);
            if (_spawnBlockerOnIncomingCall && _activePhoneNpc == null) SpawnPhoneNpc();
            UpdatePhoneNpcFollowPose();
            _activeConversation = true;
            if (_activePhoneNpc != null)
                _phonePlacement?.ShowOccluding(_activePhoneNpc.transform);
            StartCollisionGrace(_activePhoneNpc);
        }

        private void HandleCallEnded()
        {
            CompleteCall();
        }

        private void CompleteCall()
        {
            if (!_activeConversation || _phoneNpcImpactOccurred ||
                (_appManager != null && _appManager.CurrentState != GameState.Playing)) return;

            _activeConversation = false;
            DespawnPhoneNpc();
            if (_phoneCallPanel != null && _phoneCallPanel.State != PhoneCallState.Hidden)
                _phoneCallPanel.FinishCall();
            _phonePlacement?.Hide();
            CallCompleted?.Invoke();
        }

        private void SpawnPhoneNpc()
        {
            DespawnPhoneNpc();
            if (_vehicleBody == null) return;

            NpcAgent prefab = _phoneNpcPrefab;
            if (prefab == null)
            {
                Debug.LogWarning("No NPC prefab is configured for the phone distraction event.", this);
                return;
            }

            Vector3 travelDirection = GetVehicleForward() * _callTravelSign;
            Vector3 spawnPosition = CalculateNpcPosition();
            Quaternion spawnRotation = Quaternion.LookRotation(-travelDirection, Vector3.up);

            _activePhoneNpc = Instantiate(prefab, spawnPosition, spawnRotation, transform);
            _activePhoneNpc.name = "Phone Call Hazard NPC";
            _activePhoneNpc.SetCollisionFailureReason(GameFailureReason.PhoneDistractionCollision);
            _activePhoneNpc.VehicleCollisionStarted += HandlePhoneNpcCollisionStarted;
            _activePhoneNpc.Initialize(
                null,
                _npcPopulation != null ? _npcPopulation.VehicleTarget : null,
                null,
                0);
            _activePhoneNpc.SetRoundActive(true);
            _activePhoneNpc.BeginScriptedMovement();
            _activePhoneNpc.SetVehicleCollisionEnabled(false);
        }

        private void UpdatePhoneNpcFollowPose()
        {
            if (_activePhoneNpc == null || _vehicleBody == null) return;

            Vector3 travelDirection = GetVehicleForward() * _callTravelSign;
            Quaternion rotation = Quaternion.LookRotation(-travelDirection, Vector3.up);
            _activePhoneNpc.transform.SetPositionAndRotation(
                CalculateNpcPosition(),
                rotation);
        }

        private void StartCollisionGrace(NpcAgent expectedNpc)
        {
            CancelCollisionGraceRoutine();
            if (expectedNpc == null) return;

            expectedNpc.SetVehicleCollisionEnabled(false);
            float minimumGrace = Mathf.Min(_minimumCollisionGrace, _maximumCollisionGrace);
            float maximumGrace = Mathf.Max(_minimumCollisionGrace, _maximumCollisionGrace);
            float graceDuration = Random.Range(minimumGrace, maximumGrace);
            _collisionGraceRoutine = StartCoroutine(EnableNpcCollisionAfter(
                graceDuration,
                expectedNpc));
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
            if (expectedNpc == null || expectedNpc != _activePhoneNpc)
                yield break;
            expectedNpc.SetVehicleCollisionEnabled(true);
        }

        private float ResolveTravelSign(float fallbackSign)
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

            return fallbackSign < 0f ? -1f : 1f;
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
                if (_forkCollider != null && _forkCollider.enabled)
                {
                    Bounds bounds = _forkCollider.bounds;
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
            CancelCollisionGraceRoutine();
            _activeConversation = false;
            _activePhoneNpc?.SetVehicleCollisionEnabled(false);
            _phoneCallPanel?.FinishCall();
            _phonePlacement?.Hide();
        }

        private void CancelCollisionGraceRoutine()
        {
            if (_collisionGraceRoutine == null) return;
            StopCoroutine(_collisionGraceRoutine);
            _collisionGraceRoutine = null;
        }

        private void ResetCallState()
        {
            _activeConversation = false;
            _phoneNpcImpactOccurred = false;
            DespawnPhoneNpc();
            if (_applyCrowdPressureWhileRinging)
                _npcPopulation?.SetCrowdPressure(false);
            _phoneCallPanel?.HideImmediate();
            _phonePlacement?.Hide();
        }

    }
}
