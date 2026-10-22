using ElectricPalletStackers.Audio;
using ElectricPalletStackers.Gameplay;
using UnityEngine;

namespace ElectricPalletStackers.NPCs
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CapsuleCollider))]
    public sealed class NpcAgent : MonoBehaviour, IGameFailureReasonProvider
    {
        private enum MovementState { Paused, Moving, Waiting, YieldingToVehicle, Scripted }

        [Header("Waypoint movement")]
        [SerializeField, Min(0.1f)] private float _speed = 1.35f;
        [SerializeField, Min(1f)] private float _turnSpeed = 540f;
        [SerializeField, Min(0.01f)] private float _arrivalDistance = 0.12f;
        [Header("Vehicle awareness")]
        [SerializeField] private CapsuleCollider _bodyCollider;
        [SerializeField, Min(0f)] private float _pathCollisionPadding = 0.05f;
        [SerializeField, Min(0.25f)] private float _vehicleClearance = 2.5f;
        [SerializeField] private GameFailureReason _collisionFailureReason =
            GameFailureReason.PedestrianCollision;
        [Header("Optional animation parameter")]
        [SerializeField] private Animator _animator;
        [SerializeField] private string _walkingBool = "Walking";
        [Header("Footsteps")]
        [SerializeField, Min(0.1f)] private float _stepDistance = 0.72f;

        private NpcPopulationController _population;
        private NpcWaypointRoute _route;
        private NpcVehicleCollisionTarget _vehicleTarget;
        private MovementState _state = MovementState.Paused;
        private int _startingWaypointIndex;
        private int _waypointIndex;
        private int _direction = 1;
        private float _waitUntil;
        private float _hornMinimumWaitUntil;
        private float _speedMultiplier = 1f;
        private bool _roundActive;
        private int _walkingHash;
        private bool _hasWalkingParameter;
        private bool _vehicleCollisionReported;
        private bool _vehicleCollisionEnabled = true;
        private float _distanceSinceStep;

        public bool IsAvailableForPhoneEvent =>
            isActiveAndEnabled && _roundActive && _state != MovementState.Scripted;
        public NpcWaypointRoute Route => _route;
        public GameFailureReason FailureReason => _collisionFailureReason;
        public event System.Action VehicleCollisionStarted;

        private void Awake()
        {
            // NPC locomotion is transform-driven. A solid collider can inject large
            // impulses into the controlled vehicle, while a trigger still detects contact.
            if (_bodyCollider != null) _bodyCollider.isTrigger = true;

            _walkingHash = Animator.StringToHash(_walkingBool);
            if (_animator == null) return;
            foreach (AnimatorControllerParameter parameter in _animator.parameters)
            {
                if (parameter.nameHash != _walkingHash ||
                    parameter.type != AnimatorControllerParameterType.Bool) continue;
                _hasWalkingParameter = true;
                break;
            }
        }

        private void Update()
        {
            if (!_roundActive || _route == null || _route.Count == 0) return;
            switch (_state)
            {
                case MovementState.Moving:
                    UpdateMovement();
                    break;
                case MovementState.Waiting:
                    if (Time.time >= _waitUntil) _state = MovementState.Moving;
                    break;
                case MovementState.YieldingToVehicle:
                    UpdateVehicleYield();
                    break;
            }
            UpdateAnimation();
        }

        public void Initialize(
            NpcPopulationController population,
            NpcVehicleCollisionTarget vehicleTarget,
            NpcWaypointRoute route,
            int startingWaypoint)
        {
            _population = population;
            _vehicleTarget = vehicleTarget;
            _route = route;
            _startingWaypointIndex = route != null && route.Count > 0
                ? Mathf.Abs(startingWaypoint) % route.Count
                : 0;
            _waypointIndex = _startingWaypointIndex;
            _direction = 1;
        }

        public void ResetForRound(bool roundActive)
        {
            _roundActive = roundActive;
            _vehicleCollisionReported = false;
            _vehicleCollisionEnabled = true;
            _hornMinimumWaitUntil = 0f;
            _state = roundActive ? MovementState.Moving : MovementState.Paused;
            _distanceSinceStep = 0f;
            _direction = 1;
            _waypointIndex = _startingWaypointIndex;
            if (_route != null && _route.TryGetWaypoint(_waypointIndex, out Vector3 position))
            {
                transform.position = position;
                int nextIndex = _route.GetNextIndex(_waypointIndex, ref _direction);
                if (_route.TryGetWaypoint(nextIndex, out Vector3 next)) Face(next - position, true);
            }
            UpdateAnimation();
        }

        internal CheckpointPose CaptureCheckpointPose()
        {
            return new CheckpointPose(
                transform.position,
                transform.rotation,
                _waypointIndex,
                _direction,
                _speedMultiplier);
        }

        internal void RestoreCheckpointPose(CheckpointPose checkpoint)
        {
            _roundActive = false;
            _vehicleCollisionReported = false;
            _vehicleCollisionEnabled = false;
            _hornMinimumWaitUntil = 0f;
            _waitUntil = 0f;
            _state = MovementState.Paused;
            _distanceSinceStep = 0f;
            _waypointIndex = checkpoint.WaypointIndex;
            _direction = checkpoint.Direction;
            _speedMultiplier = checkpoint.SpeedMultiplier;
            transform.SetPositionAndRotation(checkpoint.Position, checkpoint.Rotation);
            UpdateAnimation();
        }

        public void SetRoundActive(bool active)
        {
            if (active && !_roundActive) _vehicleCollisionReported = false;
            _roundActive = active;
            if (!active)
            {
                _state = MovementState.Paused;
                _distanceSinceStep = 0f;
            }
            else if (_state == MovementState.Paused) _state = MovementState.Moving;
            UpdateAnimation();
        }

        public void SetCrowdPressure(bool active, float busySpeedMultiplier)
        {
            _speedMultiplier = active ? Mathf.Max(1f, busySpeedMultiplier) : 1f;
            if (active && _state == MovementState.Waiting) _state = MovementState.Moving;
        }

        public void ReactToHorn(float minimumStopDuration)
        {
            if (!_roundActive || _state == MovementState.Scripted ||
                !IsVehicleBlockingCurrentEdge()) return;
            _hornMinimumWaitUntil = Mathf.Max(
                _hornMinimumWaitUntil,
                Time.time + Mathf.Max(0f, minimumStopDuration));
            _state = MovementState.YieldingToVehicle;
            UpdateAnimation();
        }

        public void BeginScriptedMovement()
        {
            _state = MovementState.Scripted;
            UpdateAnimation();
        }

        public void SetVehicleCollisionEnabled(bool enabled)
        {
            _vehicleCollisionEnabled = enabled;
            if (enabled) _vehicleCollisionReported = false;
        }

        public void SetCollisionFailureReason(GameFailureReason reason)
        {
            _collisionFailureReason = reason;
        }

        public void ResumeNormalBehaviour()
        {
            if (!_roundActive) return;
            _state = MovementState.Moving;
            UpdateAnimation();
        }

        private void UpdateMovement()
        {
            if (!_route.TryGetWaypoint(_waypointIndex, out Vector3 destination)) return;
            if (IsVehicleBlockingCurrentEdge())
            {
                _state = MovementState.YieldingToVehicle;
                FaceVehicle();
                return;
            }

            Vector3 delta = Vector3.ProjectOnPlane(destination - transform.position, Vector3.up);
            if (delta.sqrMagnitude <= _arrivalDistance * _arrivalDistance)
            {
                transform.position = new Vector3(destination.x, transform.position.y, destination.z);
                if (_route.IsTerminalIndex(_waypointIndex, _direction))
                {
                    _state = MovementState.Paused;
                    UpdateAnimation();
                    return;
                }
                _waypointIndex = _route.GetNextIndex(_waypointIndex, ref _direction);
                float wait = _population != null && _population.IsCrowdPressureActive
                    ? 0f
                    : _route.RandomWait;
                if (wait > 0f)
                {
                    _waitUntil = Time.time + wait;
                    _state = MovementState.Waiting;
                }
                return;
            }

            Vector3 previousPosition = transform.position;
            transform.position = Vector3.MoveTowards(
                transform.position,
                destination,
                _speed * _speedMultiplier * Time.deltaTime);
            UpdateFootsteps(Vector3.Distance(previousPosition, transform.position));
            Face(delta, false);
        }

        private void UpdateVehicleYield()
        {
            if (IsVehicleBlockingCurrentEdge())
            {
                FaceVehicle();
                return;
            }
            if (Time.time < _hornMinimumWaitUntil) return;
            _state = MovementState.Moving;
        }

        private bool IsVehicleBlockingCurrentEdge()
        {
            if (_vehicleTarget == null || _route == null ||
                !_route.TryGetWaypoint(_waypointIndex, out Vector3 destination)) return false;
            float radius = _bodyCollider != null
                ? Mathf.Max(_bodyCollider.bounds.extents.x, _bodyCollider.bounds.extents.z)
                  + _pathCollisionPadding
                : _pathCollisionPadding;
            if (!_vehicleTarget.TryGetFirstPathCollision(
                    transform.position,
                    destination,
                    radius,
                    out Vector3 collisionPoint)) return false;

            Vector3 toCollision = Vector3.ProjectOnPlane(
                collisionPoint - transform.position,
                Vector3.up);
            return toCollision.magnitude <= _vehicleClearance;
        }

        private void FaceVehicle()
        {
            if (_vehicleTarget == null) return;
            Face(_vehicleTarget.transform.position - transform.position, false);
        }

        private void OnTriggerEnter(Collider other)
        {
            HandleVehicleTrigger(other);
        }

        private void OnTriggerStay(Collider other)
        {
            HandleVehicleTrigger(other);
        }

        private void HandleVehicleTrigger(Collider other)
        {
            if (!_roundActive || !_vehicleCollisionEnabled || !IsVehicleCollider(other)) return;
            TryReportVehicleCollision();
        }

        private bool IsVehicleCollider(Collider other)
        {
            return _vehicleTarget != null && _vehicleTarget.ContainsContactCollider(other);
        }

        internal bool TryReportVehicleCollision()
        {
            if (_vehicleCollisionReported) return true;
            if (!_roundActive) return false;
            if (_vehicleTarget == null) return false;

            _vehicleCollisionReported = true;
            VehicleCollisionStarted?.Invoke();
            if (!_vehicleTarget.ReportNpcCollision(this))
            {
                _vehicleCollisionReported = false;
                return false;
            }
            return true;
        }

        private void Face(Vector3 direction, bool immediate)
        {
            Vector3 planar = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (planar.sqrMagnitude < 0.001f) return;
            Quaternion target = Quaternion.LookRotation(planar.normalized, Vector3.up);
            transform.rotation = immediate
                ? target
                : Quaternion.RotateTowards(transform.rotation, target, _turnSpeed * Time.deltaTime);
        }

        private void UpdateAnimation()
        {
            if (_animator != null && _hasWalkingParameter)
                _animator.SetBool(_walkingHash, _roundActive && _state == MovementState.Moving);
        }

        private void UpdateFootsteps(float travelledDistance)
        {
            _distanceSinceStep += Mathf.Max(0f, travelledDistance);
            float stepDistance = Mathf.Max(0.1f, _stepDistance);
            while (_distanceSinceStep >= stepDistance)
            {
                _distanceSinceStep -= stepDistance;
                AudioManager.Instance?.Play(PalletStackerAudioKeys.HumanWalking, transform);
            }
        }

        internal readonly struct CheckpointPose
        {
            public CheckpointPose(
                Vector3 position,
                Quaternion rotation,
                int waypointIndex,
                int direction,
                float speedMultiplier)
            {
                Position = position;
                Rotation = rotation;
                WaypointIndex = waypointIndex;
                Direction = direction;
                SpeedMultiplier = speedMultiplier;
            }

            public Vector3 Position { get; }
            public Quaternion Rotation { get; }
            public int WaypointIndex { get; }
            public int Direction { get; }
            public float SpeedMultiplier { get; }
        }
    }
}
