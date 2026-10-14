using ElectricPalletStackers.Ble;
using UnityEngine;

namespace ElectricPalletStackers.NPCs
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CapsuleCollider))]
    public sealed class NpcAgent : MonoBehaviour
    {
        private enum MovementState { Paused, Moving, Waiting, YieldingToVehicle, Scripted }

        [Header("Waypoint movement")]
        [SerializeField, Min(0.1f)] private float _speed = 1.35f;
        [SerializeField, Min(1f)] private float _turnSpeed = 540f;
        [SerializeField, Min(0.01f)] private float _arrivalDistance = 0.12f;
        [Header("Vehicle awareness")]
        [SerializeField, Min(0.25f)] private float _vehiclePathHalfWidth = 1.5f;
        [SerializeField, Min(0.25f)] private float _vehicleClearance = 2.5f;
        [Header("Optional animation parameter")]
        [SerializeField] private Animator _animator;
        [SerializeField] private string _walkingBool = "Walking";

        private NpcPopulationController _population;
        private NpcWaypointRoute _route;
        private Transform _vehicleTransform;
        private Rigidbody _vehicleBody;
        private PalletStackerCollisionReporter _collisionReporter;
        private MovementState _state = MovementState.Paused;
        private int _waypointIndex;
        private int _direction = 1;
        private float _waitUntil;
        private float _hornMinimumWaitUntil;
        private float _speedMultiplier = 1f;
        private bool _roundActive;
        private int _walkingHash;
        private bool _hasWalkingParameter;
        private bool _vehicleCollisionReported;

        public bool IsAvailableForPhoneEvent =>
            isActiveAndEnabled && _roundActive && _state != MovementState.Scripted;
        public NpcWaypointRoute Route => _route;

        private void Reset() => _animator = GetComponentInChildren<Animator>();

        private void Awake()
        {
            // NPC locomotion is transform-driven. A solid collider can inject large
            // impulses into the controlled vehicle, while a trigger still detects contact.
            CapsuleCollider bodyCollider = GetComponent<CapsuleCollider>();
            if (bodyCollider != null) bodyCollider.isTrigger = true;

            if (_animator == null) _animator = GetComponentInChildren<Animator>();
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
            Transform vehicleTransform,
            Rigidbody vehicleBody,
            NpcWaypointRoute route,
            int startingWaypoint)
        {
            _population = population;
            _vehicleTransform = vehicleTransform;
            _vehicleBody = vehicleBody;
            _collisionReporter = vehicleBody != null
                ? vehicleBody.GetComponentInChildren<PalletStackerCollisionReporter>(true)
                : null;
            _route = route;
            _waypointIndex = route != null && route.Count > 0
                ? Mathf.Abs(startingWaypoint) % route.Count
                : 0;
            _direction = 1;
        }

        public void ResetForRound(bool roundActive)
        {
            _roundActive = roundActive;
            _vehicleCollisionReported = false;
            _hornMinimumWaitUntil = 0f;
            _state = roundActive ? MovementState.Moving : MovementState.Paused;
            _direction = 1;
            if (_route != null && _route.TryGetWaypoint(_waypointIndex, out Vector3 position))
            {
                transform.position = position;
                int nextIndex = _route.GetNextIndex(_waypointIndex, ref _direction);
                if (_route.TryGetWaypoint(nextIndex, out Vector3 next)) Face(next - position, true);
            }
            UpdateAnimation();
        }

        public void SetRoundActive(bool active)
        {
            if (active && !_roundActive) _vehicleCollisionReported = false;
            _roundActive = active;
            if (!active) _state = MovementState.Paused;
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

            transform.position = Vector3.MoveTowards(
                transform.position,
                destination,
                _speed * _speedMultiplier * Time.deltaTime);
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
            if (_vehicleTransform == null || _route == null ||
                !_route.TryGetWaypoint(_waypointIndex, out Vector3 destination)) return false;
            Vector3 segment = Vector3.ProjectOnPlane(destination - transform.position, Vector3.up);
            float length = segment.magnitude;
            if (length < 0.01f) return false;
            Vector3 direction = segment / length;
            Vector3 toVehicle = Vector3.ProjectOnPlane(
                _vehicleTransform.position - transform.position,
                Vector3.up);
            float along = Vector3.Dot(toVehicle, direction);
            // Only react to the vehicle in front of the NPC, on the remaining part of
            // the current waypoint edge, and close enough to actually block movement.
            if (along <= 0f || along > length || along > _vehicleClearance) return false;
            float lateral = (toVehicle - direction * along).magnitude;
            return lateral <= _vehiclePathHalfWidth;
        }

        private void FaceVehicle()
        {
            if (_vehicleTransform == null) return;
            Face(_vehicleTransform.position - transform.position, false);
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
            if (!_roundActive || !IsVehicleCollider(other)) return;
            TryReportVehicleCollision();
        }

        private bool IsVehicleCollider(Collider other)
        {
            if (other == null || _vehicleBody == null) return false;
            if (other.attachedRigidbody == _vehicleBody) return true;

            Transform otherTransform = other.transform;
            Transform vehicleRoot = _vehicleBody.transform;
            return otherTransform == vehicleRoot || otherTransform.IsChildOf(vehicleRoot);
        }

        internal bool TryReportVehicleCollision(PalletStackerCollisionReporter fallback = null)
        {
            if (_vehicleCollisionReported) return true;
            if (!_roundActive) return false;

            PalletStackerCollisionReporter reporter = _collisionReporter != null
                ? _collisionReporter
                : fallback;
            if (reporter == null) return false;

            _vehicleCollisionReported = true;
            reporter.ReportCollision();
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
    }
}
