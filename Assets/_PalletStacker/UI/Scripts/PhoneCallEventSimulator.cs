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
        [SerializeField] private bool _applyCrowdPressureWhileRinging = true;
        [Tooltip("Spawn the road-blocking NPC as soon as the phone starts ringing.")]
        [SerializeField] private bool _spawnBlockerOnIncomingCall = true;
        [SerializeField, Min(0f)] private float _minimumOngoingDuration = 8f;
        [SerializeField, Min(0f)] private float _maximumOngoingDuration = 10f;
        [SerializeField, Min(0f)] private float _endedDisplayDuration = 1f;

        [Header("Phone distraction NPC")]
        [SerializeField] private NpcAgent _phoneNpcPrefab;
        [SerializeField, Min(0f)] private float _npcFrontClearance = 4f;
        [SerializeField, Min(0f)] private float _npcBehindPlayerClearance = 4f;
        [SerializeField, Min(0f)] private float _travelDirectionThreshold = 0.05f;
        [Tooltip("Smallest signed motor speed/input that counts as movement after answering.")]
        [SerializeField, Min(0.0001f)] private float _acceptedMovementThreshold = 0.005f;
        [Tooltip("NPC overlaps the vehicle edge by this amount so contact is reliable without placing it inside the vehicle.")]
        [SerializeField, Min(0f)] private float _impactOverlap = 0.05f;
        [Tooltip("Maximum time from movement detection until the phone-distraction failure is reported.")]
        [SerializeField, Range(0.05f, 0.5f)] private float _acceptedCollisionTimeout = 0.5f;
        [SerializeField, Min(0.1f)] private float _groundProbeHeight = 3f;
        [SerializeField, Min(0.1f)] private float _groundProbeDistance = 8f;
        [SerializeField, Min(0f)] private float _maximumGroundHeightDifference = 0.75f;
        [SerializeField] private LayerMask _groundLayers = ~0;

        private Coroutine _collisionDeliveryRoutine;
        private Coroutine _automaticEndRoutine;
        private Coroutine _endedDisplayRoutine;
        private NpcAgent _activePhoneNpc;
        private bool _activeConversation;
        private bool _callResolved = true;
        private bool _phoneNpcImpactOccurred;
        private bool _collisionDeliveryStarted;
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
            if (_activePhoneNpc == null) return;

            bool movementIsUnsafe = _phoneCallPanel != null &&
                                    (_phoneCallPanel.State == PhoneCallState.InCall ||
                                     _phoneCallPanel.State == PhoneCallState.Ended);
            if (movementIsUnsafe)
            {
                if (!_collisionDeliveryStarted && TryGetActiveTravelSign(out float travelSign))
                    TriggerAcceptedMovementCollision(travelSign);
            }

            if (IsRinging && !_collisionDeliveryStarted) UpdatePhoneNpcFollowPose();
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
            _callResolved = false;
            _phoneNpcImpactOccurred = false;
            if (!_hasTriggerEntryDirection)
                _callTravelSign = ResolveTravelSign(1f);
            if (_spawnBlockerOnIncomingCall) SpawnPhoneNpc();
            _phoneCallPanel?.ShowIncomingCall();
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
            if (nextState == GameState.Lost)
            {
                if (_phoneNpcImpactOccurred) StopCallKeepingImpactedNpc();
                else ResetCallState();
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
            if (_callResolved ||
                (_appManager != null && _appManager.CurrentState != GameState.Playing)) return;

            _activeConversation = true;
            _activePhoneNpc?.SetVehicleCollisionEnabled(false);
            if (_applyCrowdPressureWhileRinging)
                _npcPopulation?.SetCrowdPressure(false);
            StartAutomaticEndTimer();
        }

        private void HandleCallEnded()
        {
            BeginEndedPhase();
        }

        private void StartAutomaticEndTimer()
        {
            CancelAutomaticEndRoutine();
            float minimum = Mathf.Max(0f, _minimumOngoingDuration);
            float maximum = Mathf.Max(minimum, _maximumOngoingDuration);
            _automaticEndRoutine = StartCoroutine(EndAutomaticallyAfter(Random.Range(minimum, maximum)));
        }

        private IEnumerator EndAutomaticallyAfter(float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            _automaticEndRoutine = null;
            BeginEndedPhase();
        }

        private void BeginEndedPhase()
        {
            if (_callResolved || _phoneNpcImpactOccurred ||
                _phoneCallPanel == null || !_phoneCallPanel.IsVisible ||
                _phoneCallPanel.State == PhoneCallState.Ended ||
                (_appManager != null && _appManager.CurrentState != GameState.Playing)) return;

            _activeConversation = false;
            CancelAutomaticEndRoutine();
            if (_applyCrowdPressureWhileRinging)
                _npcPopulation?.SetCrowdPressure(false);
            _phoneCallPanel.ShowEnded();
            _endedDisplayRoutine = StartCoroutine(CompleteAfterEndedDisplay());
        }

        private IEnumerator CompleteAfterEndedDisplay()
        {
            yield return new WaitForSecondsRealtime(Mathf.Max(0f, _endedDisplayDuration));
            _endedDisplayRoutine = null;
            CompleteCall();
        }

        private void CompleteCall()
        {
            if (_callResolved || _phoneNpcImpactOccurred ||
                (_appManager != null && _appManager.CurrentState != GameState.Playing)) return;

            _callResolved = true;
            _activeConversation = false;
            CancelCallTimingRoutines();
            DespawnPhoneNpc();
            if (_applyCrowdPressureWhileRinging)
                _npcPopulation?.SetCrowdPressure(false);
            _phoneCallPanel?.HideImmediate();
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

        private void TriggerAcceptedMovementCollision(float travelSign)
        {
            if (_activePhoneNpc == null || _collisionDeliveryStarted) return;

            _collisionDeliveryStarted = true;
            _callTravelSign = travelSign < 0f ? -1f : 1f;

            Vector3 travelDirection = GetVehicleForward() * _callTravelSign;
            Quaternion impactRotation = Quaternion.LookRotation(-travelDirection, Vector3.up);
            _activePhoneNpc.transform.rotation = impactRotation;
            Physics.SyncTransforms();
            Vector3 impactPosition = CalculateAcceptedImpactPosition(travelDirection);
            _activePhoneNpc.transform.SetPositionAndRotation(impactPosition, impactRotation);
            _activePhoneNpc.SetVehicleCollisionEnabled(true);
            Physics.SyncTransforms();

            CancelCollisionDeliveryRoutine();
            _collisionDeliveryRoutine = StartCoroutine(
                GuaranteeAcceptedCallCollision(_activePhoneNpc));
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

        private IEnumerator GuaranteeAcceptedCallCollision(NpcAgent expectedNpc)
        {
            float deadline = Time.realtimeSinceStartup + Mathf.Min(0.5f, _acceptedCollisionTimeout);
            while (!_phoneNpcImpactOccurred && expectedNpc != null &&
                   expectedNpc == _activePhoneNpc && Time.realtimeSinceStartup < deadline)
            {
                yield return new WaitForFixedUpdate();
            }

            _collisionDeliveryRoutine = null;
            if (!_phoneNpcImpactOccurred && expectedNpc != null && expectedNpc == _activePhoneNpc)
                expectedNpc.TryReportVehicleCollision();
        }

        private Vector3 CalculateAcceptedImpactPosition(Vector3 travelDirection)
        {
            travelDirection = Vector3.ProjectOnPlane(travelDirection, Vector3.up).normalized;
            if (travelDirection.sqrMagnitude < 0.001f) travelDirection = Vector3.forward;

            Collider npcCollider = FindNpcCollider();
            float npcExtent = npcCollider != null
                ? GetProjectedExtent(npcCollider.bounds, travelDirection)
                : 0.35f;
            Vector3 npcPlanarCenterOffset = npcCollider != null
                ? Vector3.ProjectOnPlane(
                    npcCollider.bounds.center - _activePhoneNpc.transform.position,
                    Vector3.up)
                : Vector3.zero;

            Vector3 edgePoint;
            if (_callTravelSign >= 0f)
            {
                edgePoint = GetForkEdgePoint(travelDirection);
            }
            else
            {
                // The driver stands at the rear of this vehicle, so reverse hazards
                // are measured from the player rather than from a body-collider bound.
                edgePoint = _playerView != null ? _playerView.position : _vehicleBody.position;
                edgePoint.y = GetExpectedGroundHeight();
            }

            float centerDistance = Mathf.Max(0f, npcExtent - _impactOverlap);
            Vector3 position = edgePoint + travelDirection * centerDistance - npcPlanarCenterOffset;
            SnapToGround(ref position);
            return position;
        }

        private Collider FindNpcCollider()
        {
            if (_activePhoneNpc == null) return null;
            Collider[] colliders = _activePhoneNpc.GetComponentsInChildren<Collider>(true);
            for (int index = 0; index < colliders.Length; index++)
            {
                Collider candidate = colliders[index];
                if (candidate != null && candidate.enabled) return candidate;
            }
            return null;
        }

        private Vector3 GetForkEdgePoint(Vector3 travelDirection)
        {
            if (_forkCollider != null && _forkCollider.enabled)
            {
                Bounds bounds = _forkCollider.bounds;
                Vector3 edge = bounds.center +
                               travelDirection * GetProjectedExtent(bounds, travelDirection);
                edge.y = GetExpectedGroundHeight();
                return edge;
            }

            Vector3 fallback = _forkCollisionSensor != null
                ? _forkCollisionSensor.transform.position
                : _vehicleBody.position;
            fallback.y = GetExpectedGroundHeight();
            return fallback;
        }

        private static float GetProjectedExtent(Bounds bounds, Vector3 direction)
        {
            Vector3 extents = bounds.extents;
            return Mathf.Abs(direction.x) * extents.x +
                   Mathf.Abs(direction.y) * extents.y +
                   Mathf.Abs(direction.z) * extents.z;
        }

        private bool TryGetActiveTravelSign(out float travelSign)
        {
            if (_vehicleMotor != null)
            {
                float signedMovement = Mathf.Abs(_vehicleMotor.TargetSpeed) > _acceptedMovementThreshold
                    ? _vehicleMotor.TargetSpeed
                    : _vehicleMotor.CurrentSpeed;
                if (Mathf.Abs(signedMovement) > _acceptedMovementThreshold)
                {
                    travelSign = Mathf.Sign(signedMovement);
                    return true;
                }
            }

            if (_vehicleBody != null)
            {
                Vector3 planarVelocity = Vector3.ProjectOnPlane(_vehicleBody.linearVelocity, Vector3.up);
                float signedVelocity = Vector3.Dot(planarVelocity, GetVehicleForward());
                if (Mathf.Abs(signedVelocity) > _acceptedMovementThreshold)
                {
                    travelSign = Mathf.Sign(signedVelocity);
                    return true;
                }
            }

            travelSign = 0f;
            return false;
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
            CancelCollisionDeliveryRoutine();
            _collisionDeliveryStarted = false;
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
            CancelCallTimingRoutines();
            CancelCollisionDeliveryRoutine();
            _activeConversation = false;
            _activePhoneNpc?.SetVehicleCollisionEnabled(false);
            _phoneCallPanel?.HideImmediate();
            _phonePlacement?.Hide();
        }

        private void CancelCallTimingRoutines()
        {
            CancelAutomaticEndRoutine();
            if (_endedDisplayRoutine == null) return;
            StopCoroutine(_endedDisplayRoutine);
            _endedDisplayRoutine = null;
        }

        private void CancelAutomaticEndRoutine()
        {
            if (_automaticEndRoutine == null) return;
            StopCoroutine(_automaticEndRoutine);
            _automaticEndRoutine = null;
        }

        private void CancelCollisionDeliveryRoutine()
        {
            if (_collisionDeliveryRoutine == null) return;
            StopCoroutine(_collisionDeliveryRoutine);
            _collisionDeliveryRoutine = null;
        }

        private void ResetCallState()
        {
            CancelCallTimingRoutines();
            _activeConversation = false;
            _callResolved = true;
            _phoneNpcImpactOccurred = false;
            _collisionDeliveryStarted = false;
            DespawnPhoneNpc();
            if (_applyCrowdPressureWhileRinging)
                _npcPopulation?.SetCrowdPressure(false);
            _phoneCallPanel?.HideImmediate();
            _phonePlacement?.Hide();
        }

    }
}
