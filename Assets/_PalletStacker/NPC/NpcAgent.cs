using System;
using UnityEngine;
using UnityEngine.AI;

namespace ElectricPalletStackers.NPCs
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(CapsuleCollider))]
    public sealed class NpcAgent : MonoBehaviour
    {
        private enum BehaviourState
        {
            Paused,
            Wandering,
            MovingToEdge,
            IdleAtEdge,
            MovingToConversation,
            WaitingForConversation,
            Talking,
            EvadingVehicle
        }

        [Header("References")]
        [SerializeField] private NavMeshAgent _agent;
        [SerializeField] private Animator _animator;

        [Header("Wandering")]
        [SerializeField, Min(1f)] private float _wanderDistance = 12f;
        [SerializeField, Range(0f, 1f)] private float _edgeIdleChance = 0.35f;
        [SerializeField] private Vector2 _edgeIdleDuration = new(2f, 5f);
        [SerializeField, Min(0.01f)] private float _arrivalDistance = 0.2f;

        [Header("Vehicle awareness")]
        [SerializeField, Min(0.5f)] private float _vehicleAwarenessRadius = 6f;
        [SerializeField, Min(0.25f)] private float _vehiclePathHalfWidth = 1.5f;
        [SerializeField, Min(0f)] private float _vehicleMinimumSpeed = 0.2f;
        [SerializeField, Min(0f)] private float _vehicleClearDelay = 0.75f;
        [SerializeField, Min(0.5f)] private float _stationaryVehicleClearance = 2.5f;
        [SerializeField, Min(0.5f)] private float _evadeDistance = 2.5f;

        [Header("Optional animation parameters")]
        [SerializeField] private string _walkingBool = "Walking";
        [SerializeField] private string _talkingBool = "Talking";

        private NpcPopulationController _population;
        private Transform _vehicleTransform;
        private Rigidbody _vehicleBody;
        private Collider[] _vehicleColliders;
        private NpcAgent _conversationPartner;
        private BehaviourState _state = BehaviourState.Paused;
        private float _stateDeadline;
        private float _conversationDuration;
        private float _vehicleClearAt;
        private float _nextVehicleEvadeTime;
        private float _nextConversationTime;
        private bool _roundActive;
        private bool _waitingForVehicle;
        private int _walkingBoolHash;
        private int _talkingBoolHash;
        private bool _hasWalkingBool;
        private bool _hasTalkingBool;

        public NavMeshAgent Agent => _agent;
        public bool CanStartConversation =>
            _roundActive &&
            !_waitingForVehicle &&
            Time.time >= _nextConversationTime &&
            (_state == BehaviourState.Wandering || _state == BehaviourState.IdleAtEdge);

        public NpcAgent ConversationPartner => _conversationPartner;
        public bool IsConversationReady => _state == BehaviourState.WaitingForConversation;

        private void Reset()
        {
            _agent = GetComponent<NavMeshAgent>();
            _animator = GetComponentInChildren<Animator>();
        }

        private void Awake()
        {
            if (_agent == null) _agent = GetComponent<NavMeshAgent>();
            if (_animator == null) _animator = GetComponentInChildren<Animator>();
            CacheAnimatorParameters();
        }

        private void OnDisable()
        {
            ClearConversation(null, 0f);
        }

        private void Update()
        {
            if (!_roundActive || _agent == null || !_agent.isOnNavMesh) return;

            UpdateVehicleResponse();
            if (_waitingForVehicle)
            {
                UpdateAnimation();
                return;
            }

            switch (_state)
            {
                case BehaviourState.Wandering:
                    if (HasArrived()) HandleWanderArrival();
                    break;

                case BehaviourState.MovingToEdge:
                    if (HasArrived()) BeginEdgeIdle();
                    break;

                case BehaviourState.IdleAtEdge:
                    if (Time.time >= _stateDeadline) BeginWandering();
                    break;

                case BehaviourState.MovingToConversation:
                    if (HasArrived()) BecomeConversationReady();
                    break;

                case BehaviourState.WaitingForConversation:
                    if (_conversationPartner == null)
                        BeginWandering();
                    else if (_conversationPartner.IsConversationReady)
                        StartConversationPair();
                    break;

                case BehaviourState.Talking:
                    FaceConversationPartner();
                    if (Time.time >= _stateDeadline) EndConversation();
                    break;

                case BehaviourState.EvadingVehicle:
                    if (HasArrived()) BeginWandering();
                    break;
            }

            UpdateAnimation();
        }

        public void Initialize(
            NpcPopulationController population,
            Transform vehicleTransform,
            Rigidbody vehicleBody)
        {
            _population = population;
            _vehicleTransform = vehicleTransform;
            _vehicleBody = vehicleBody;
            _vehicleColliders = _vehicleBody != null
                ? _vehicleBody.GetComponentsInChildren<Collider>()
                : null;
            if (_agent != null) _agent.avoidancePriority = UnityEngine.Random.Range(20, 81);
            _nextConversationTime = Time.time + UnityEngine.Random.Range(3f, 8f);
        }

        public void ResetForRound(Vector3 position, bool roundActive)
        {
            ClearConversation(null, 0f);
            _waitingForVehicle = false;
            _roundActive = roundActive;

            if (_agent != null && _agent.isOnNavMesh)
                _agent.Warp(position);
            else
                transform.position = position;

            if (_agent != null && _agent.isOnNavMesh)
            {
                _agent.ResetPath();
                _agent.isStopped = !roundActive;
            }

            _state = roundActive ? BehaviourState.Wandering : BehaviourState.Paused;
            _nextConversationTime = Time.time + UnityEngine.Random.Range(3f, 8f);
            if (roundActive) BeginWandering();
            UpdateAnimation();
        }

        public void SetRoundActive(bool active)
        {
            _roundActive = active;
            _waitingForVehicle = false;

            if (!active)
            {
                ClearConversation(null, 0f);
                _state = BehaviourState.Paused;
                StopAtCurrentPosition();
            }
            else if (_state == BehaviourState.Paused)
            {
                BeginWandering();
            }

            UpdateAnimation();
        }

        public void BeginConversation(
            NpcAgent partner,
            Vector3 destination,
            float duration)
        {
            if (!CanStartConversation || partner == null) return;

            _conversationPartner = partner;
            _conversationDuration = duration;
            _waitingForVehicle = false;
            MoveTo(destination, BehaviourState.MovingToConversation);
        }

        public void ClearConversation(NpcAgent expectedPartner, float cooldown)
        {
            if (expectedPartner != null && _conversationPartner != expectedPartner) return;

            _conversationPartner = null;
            _conversationDuration = 0f;
            _nextConversationTime = Mathf.Max(_nextConversationTime, Time.time + cooldown);
        }

        private void BeginWandering()
        {
            if (!_roundActive || _population == null) return;

            if (_population.TryGetWanderPoint(this, _wanderDistance, out Vector3 destination))
            {
                MoveTo(destination, BehaviourState.Wandering);
                return;
            }

            _state = BehaviourState.IdleAtEdge;
            _stateDeadline = Time.time + 1f;
            StopAtCurrentPosition();
        }

        private void HandleWanderArrival()
        {
            if (UnityEngine.Random.value <= _edgeIdleChance &&
                _population.TryGetEdgeStopPoint(this, transform.position, out Vector3 edgePoint))
            {
                MoveTo(edgePoint, BehaviourState.MovingToEdge);
            }
            else
            {
                BeginWandering();
            }
        }

        private void BeginEdgeIdle()
        {
            _state = BehaviourState.IdleAtEdge;
            _stateDeadline = Time.time + RandomInRange(_edgeIdleDuration);
            StopAtCurrentPosition();
        }

        private void BecomeConversationReady()
        {
            _state = BehaviourState.WaitingForConversation;
            StopAtCurrentPosition();
            FaceConversationPartner();
        }

        private void StartConversationPair()
        {
            if (_conversationPartner == null) return;

            float duration = Mathf.Max(_conversationDuration, _conversationPartner._conversationDuration);
            float deadline = Time.time + duration;
            _state = BehaviourState.Talking;
            _stateDeadline = deadline;
            _conversationPartner._state = BehaviourState.Talking;
            _conversationPartner._stateDeadline = deadline;
            FaceConversationPartner();
            _conversationPartner.FaceConversationPartner();
        }

        private void EndConversation()
        {
            NpcAgent partner = _conversationPartner;
            ClearConversation(partner, _population.ConversationCooldown);

            if (partner != null)
            {
                partner.ClearConversation(this, _population.ConversationCooldown);
                if (partner._roundActive) partner.BeginWandering();
            }

            BeginWandering();
        }

        private void CancelConversationForVehicle()
        {
            NpcAgent partner = _conversationPartner;
            ClearConversation(partner, _population.ConversationCooldown);

            if (partner != null)
            {
                partner.ClearConversation(this, _population.ConversationCooldown);
                if (partner._roundActive) partner.BeginWandering();
            }
        }

        private void UpdateVehicleResponse()
        {
            bool threatened = IsVehicleThreat(out Vector3 vehicleVelocity);
            if (threatened)
            {
                _vehicleClearAt = Time.time + _vehicleClearDelay;

                if (IsStandingState())
                {
                    CancelConversationForVehicle();
                    if (_population.TryGetVehicleEvadePoint(
                            this,
                            vehicleVelocity,
                            _evadeDistance,
                            out Vector3 evadePoint))
                    {
                        _waitingForVehicle = false;
                        MoveTo(evadePoint, BehaviourState.EvadingVehicle);
                    }
                    else
                    {
                        HoldForVehicle();
                    }
                }
                else if (_state != BehaviourState.EvadingVehicle)
                {
                    HoldForVehicle();
                }
            }
            else if (Time.time >= _nextVehicleEvadeTime &&
                     IsStationaryVehicleBlocking(out Vector3 avoidanceDirection))
            {
                CancelConversationForVehicle();
                if (_population.TryGetVehicleEvadePoint(
                        this,
                        avoidanceDirection,
                        _evadeDistance,
                        out Vector3 evadePoint))
                {
                    _nextVehicleEvadeTime = Time.time + 1.5f;
                    _waitingForVehicle = false;
                    MoveTo(evadePoint, BehaviourState.EvadingVehicle);
                }
            }
            else if (_waitingForVehicle && Time.time >= _vehicleClearAt)
            {
                _waitingForVehicle = false;
                if (_agent.isOnNavMesh) _agent.isStopped = false;
            }
        }

        private bool IsVehicleThreat(out Vector3 vehicleVelocity)
        {
            vehicleVelocity = Vector3.zero;
            if (_vehicleTransform == null) return false;

            if (_vehicleBody != null)
                vehicleVelocity = Vector3.ProjectOnPlane(_vehicleBody.linearVelocity, Vector3.up);

            if (vehicleVelocity.sqrMagnitude < _vehicleMinimumSpeed * _vehicleMinimumSpeed)
                return false;

            Vector3 toNpc = Vector3.ProjectOnPlane(
                transform.position - _vehicleTransform.position,
                Vector3.up);
            float distance = toNpc.magnitude;
            if (distance > _vehicleAwarenessRadius) return false;

            Vector3 travelDirection = vehicleVelocity.normalized;
            float forwardDistance = Vector3.Dot(toNpc, travelDirection);
            if (forwardDistance < -0.5f || forwardDistance > _vehicleAwarenessRadius) return false;

            Vector3 lateralOffset = toNpc - travelDirection * forwardDistance;
            return lateralOffset.magnitude <= _vehiclePathHalfWidth;
        }

        private bool IsStationaryVehicleBlocking(out Vector3 avoidanceDirection)
        {
            avoidanceDirection = Vector3.zero;
            if (_vehicleTransform == null || _agent == null) return false;

            Vector3 vehicleVelocity = _vehicleBody != null
                ? Vector3.ProjectOnPlane(_vehicleBody.linearVelocity, Vector3.up)
                : Vector3.zero;
            if (vehicleVelocity.sqrMagnitude >= _vehicleMinimumSpeed * _vehicleMinimumSpeed)
                return false;

            Vector3 closestVehiclePoint = GetClosestVehiclePoint(transform.position);
            Vector3 toVehicle = Vector3.ProjectOnPlane(
                closestVehiclePoint - transform.position,
                Vector3.up);
            if (toVehicle.sqrMagnitude > _stationaryVehicleClearance * _stationaryVehicleClearance)
                return false;

            if (toVehicle.sqrMagnitude < 0.001f)
            {
                toVehicle = Vector3.ProjectOnPlane(
                    _vehicleTransform.position - transform.position,
                    Vector3.up);
            }

            Vector3 desiredTravel = Vector3.ProjectOnPlane(_agent.desiredVelocity, Vector3.up);
            if (!IsStandingState() &&
                (desiredTravel.sqrMagnitude < 0.01f ||
                 Vector3.Dot(desiredTravel.normalized, toVehicle.normalized) < 0.2f))
                return false;

            avoidanceDirection = desiredTravel.sqrMagnitude > 0.01f
                ? desiredTravel
                : Vector3.ProjectOnPlane(_vehicleTransform.forward, Vector3.up);
            return avoidanceDirection.sqrMagnitude > 0.001f;
        }

        private Vector3 GetClosestVehiclePoint(Vector3 position)
        {
            Vector3 closestPoint = _vehicleTransform != null
                ? _vehicleTransform.position
                : position;
            float closestSqrDistance = float.PositiveInfinity;

            if (_vehicleColliders == null) return closestPoint;

            for (int index = 0; index < _vehicleColliders.Length; index++)
            {
                Collider vehicleCollider = _vehicleColliders[index];
                if (vehicleCollider == null || !vehicleCollider.enabled || vehicleCollider.isTrigger)
                    continue;

                Vector3 candidate = vehicleCollider.ClosestPoint(position);
                float sqrDistance = (candidate - position).sqrMagnitude;
                if (sqrDistance >= closestSqrDistance) continue;

                closestSqrDistance = sqrDistance;
                closestPoint = candidate;
            }

            return closestPoint;
        }

        private bool IsStandingState()
        {
            return _state == BehaviourState.IdleAtEdge ||
                   _state == BehaviourState.WaitingForConversation ||
                   _state == BehaviourState.Talking;
        }

        private void HoldForVehicle()
        {
            _waitingForVehicle = true;
            if (_agent != null && _agent.isOnNavMesh) _agent.isStopped = true;
        }

        private void MoveTo(Vector3 destination, BehaviourState state)
        {
            if (_agent == null || !_agent.isOnNavMesh) return;

            _state = state;
            _agent.isStopped = false;
            _agent.SetDestination(destination);
        }

        private void StopAtCurrentPosition()
        {
            if (_agent == null || !_agent.isOnNavMesh) return;
            _agent.ResetPath();
            _agent.isStopped = true;
        }

        private bool HasArrived()
        {
            if (_agent.pathPending) return false;
            if (_agent.remainingDistance > Mathf.Max(_arrivalDistance, _agent.stoppingDistance)) return false;
            return !_agent.hasPath || _agent.velocity.sqrMagnitude < 0.01f;
        }

        private void FaceConversationPartner()
        {
            if (_conversationPartner == null) return;

            Vector3 direction = Vector3.ProjectOnPlane(
                _conversationPartner.transform.position - transform.position,
                Vector3.up);
            if (direction.sqrMagnitude < 0.001f) return;

            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                Quaternion.LookRotation(direction.normalized, Vector3.up),
                360f * Time.deltaTime);
        }

        private void CacheAnimatorParameters()
        {
            if (_animator == null) return;

            _walkingBoolHash = Animator.StringToHash(_walkingBool);
            _talkingBoolHash = Animator.StringToHash(_talkingBool);

            foreach (AnimatorControllerParameter parameter in _animator.parameters)
            {
                if (parameter.type != AnimatorControllerParameterType.Bool) continue;
                if (parameter.nameHash == _walkingBoolHash) _hasWalkingBool = true;
                if (parameter.nameHash == _talkingBoolHash) _hasTalkingBool = true;
            }
        }

        private void UpdateAnimation()
        {
            if (_animator == null) return;

            bool walking = _roundActive && !_waitingForVehicle && _agent != null &&
                           _agent.isOnNavMesh && _agent.velocity.sqrMagnitude > 0.01f;
            if (_hasWalkingBool) _animator.SetBool(_walkingBoolHash, walking);
            if (_hasTalkingBool) _animator.SetBool(_talkingBoolHash, _state == BehaviourState.Talking);
        }

        private static float RandomInRange(Vector2 range)
        {
            float minimum = Mathf.Min(range.x, range.y);
            float maximum = Mathf.Max(range.x, range.y);
            return UnityEngine.Random.Range(minimum, maximum);
        }
    }
}
