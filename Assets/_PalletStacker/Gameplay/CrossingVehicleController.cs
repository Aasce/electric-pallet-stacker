using System;
using UnityEngine;

namespace ElectricPalletStackers.Gameplay
{
    /// <summary>
    /// Controls the pedestrian authored at the Cross phase. The legacy class name is
    /// intentionally retained so existing scene and checkpoint references stay valid.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Electric Pallet Stackers/Gameplay/Crossing Pedestrian Controller")]
    public sealed class CrossingVehicleController : MonoBehaviour, IGameFailureReasonProvider
    {
        [Header("Scene References")]
        [Tooltip("The NPC prefab instance placed directly in the scene. It remains visible at Waypoint 01 before and after a successful safety check.")]
        [SerializeField] private Transform _pedestrian;
        [Tooltip("Ordered walking points. Element 0 is the standing pose; walking starts toward element 1.")]
        [SerializeField] private Transform[] _waypoints;

        [Header("Walking")]
        [SerializeField, Min(0.1f)] private float _speed = 2.025f;
        [SerializeField, Min(1f)] private float _rotationSpeedDegrees = 540f;
        [SerializeField, Min(0.01f)] private float _arrivalDistance = 0.12f;

        [Header("NPC Presentation")]
        [SerializeField] private CapsuleCollider _pedestrianCollider;
        [SerializeField] private Animator _animator;
        [SerializeField] private string _walkingBool = "Walking";

        private bool _crossing;
        private bool _hasCompletedRoute;
        private bool _suppressed;
        private int _nextWaypointIndex;
        private bool _configurationErrorReported;
        private int _walkingHash;
        private bool _hasWalkingParameter;

        public bool IsCrossing => _crossing;
        public bool HasCompletedRoute => _hasCompletedRoute;
        public bool IsSuppressed => _suppressed;
        public GameFailureReason FailureReason => GameFailureReason.PedestrianCollision;
        public event Action CrossingStarted;
        public event Action<int> WaypointReached;
        public event Action CrossingCompleted;

        private void Awake()
        {
            CachePedestrianComponents();
            ResetVehicle();
        }

        private void Update()
        {
            if (!_crossing || !HasRequiredReferences()) return;

            Transform targetWaypoint = _waypoints[_nextWaypointIndex];
            Vector3 currentPosition = _pedestrian.position;
            Vector3 targetPosition = targetWaypoint.position;
            Vector3 nextPosition = Vector3.MoveTowards(
                currentPosition,
                targetPosition,
                _speed * Time.deltaTime);
            Vector3 direction = Vector3.ProjectOnPlane(
                targetPosition - currentPosition,
                Vector3.up);
            if (direction.sqrMagnitude > 0.001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(
                    direction.normalized,
                    Vector3.up);
                _pedestrian.rotation = Quaternion.RotateTowards(
                    _pedestrian.rotation,
                    targetRotation,
                    _rotationSpeedDegrees * Time.deltaTime);
            }

            _pedestrian.position = nextPosition;
            if ((nextPosition - targetPosition).sqrMagnitude >
                _arrivalDistance * _arrivalDistance) return;

            _pedestrian.position = targetPosition;
            int reachedWaypointIndex = _nextWaypointIndex;
            _nextWaypointIndex++;
            WaypointReached?.Invoke(reachedWaypointIndex);
            if (_nextWaypointIndex < _waypoints.Length) return;

            _crossing = false;
            _hasCompletedRoute = true;
            UpdateAnimation();
            CrossingCompleted?.Invoke();
        }

        public void BeginCrossing()
        {
            if (_suppressed || _crossing || _hasCompletedRoute || !HasRequiredReferences()) return;

            _nextWaypointIndex = Mathf.Clamp(
                _nextWaypointIndex,
                1,
                _waypoints.Length - 1);
            EnsurePedestrianVisible();
            _crossing = true;
            UpdateAnimation();
            CrossingStarted?.Invoke();
        }

        /// <summary>
        /// Arms the pedestrian prefab already placed in the scene without spawning,
        /// moving, or hiding it.
        /// </summary>
        public void ArmForHazard()
        {
            _suppressed = false;
            _crossing = false;
            _hasCompletedRoute = false;
            _nextWaypointIndex = 1;
            if (!HasRequiredReferences()) return;

            EnsurePedestrianVisible();
            FaceNextWaypoint(true);
            UpdateAnimation();
        }

        /// <summary>
        /// Cancels the hazard after a successful safety check while leaving the NPC
        /// visible and stationary at its authored start position.
        /// </summary>
        public void SuppressVehicle()
        {
            _suppressed = true;
            _crossing = false;
            _hasCompletedRoute = true;
            EnsurePedestrianVisible();
            UpdateAnimation();
        }

        public void StopVehicle()
        {
            _crossing = false;
            UpdateAnimation();
        }

        [ContextMenu("Reset Pedestrian To First Waypoint")]
        public void ResetVehicle()
        {
            _crossing = false;
            _hasCompletedRoute = false;
            _suppressed = false;
            _nextWaypointIndex = 1;
            if (!HasRequiredReferences()) return;

            EnsurePedestrianVisible();
            _pedestrian.position = _waypoints[0].position;
            FaceNextWaypoint(true);
            UpdateAnimation();
            Physics.SyncTransforms();
        }

        public bool IsTouchingAny(Collider[] targets)
        {
            if (!_crossing || targets == null || targets.Length == 0) return false;
            CachePedestrianComponents();
            if (_pedestrianCollider == null || !_pedestrianCollider.enabled ||
                !_pedestrianCollider.gameObject.activeInHierarchy) return false;

            for (int index = 0; index < targets.Length; index++)
            {
                Collider target = targets[index];
                if (target == null || !target.enabled || target.isTrigger ||
                    !target.gameObject.activeInHierarchy) continue;
                if (!_pedestrianCollider.bounds.Intersects(target.bounds)) continue;

                if (Physics.ComputePenetration(
                        _pedestrianCollider,
                        _pedestrianCollider.transform.position,
                        _pedestrianCollider.transform.rotation,
                        target,
                        target.transform.position,
                        target.transform.rotation,
                        out _,
                        out _))
                    return true;
            }

            return false;
        }

        public CheckpointState CaptureCheckpointState()
        {
            Transform pose = _pedestrian != null ? _pedestrian : transform;
            return new CheckpointState(
                pose.position,
                pose.rotation,
                _nextWaypointIndex,
                _crossing,
                _hasCompletedRoute,
                _suppressed,
                _pedestrian != null && _pedestrian.gameObject.activeSelf);
        }

        public void RestoreCheckpointState(CheckpointState checkpoint)
        {
            _crossing = checkpoint.IsCrossing;
            _hasCompletedRoute = checkpoint.HasCompletedRoute;
            _suppressed = checkpoint.IsSuppressed;
            _nextWaypointIndex = checkpoint.NextWaypointIndex;
            if (!HasRequiredReferences()) return;

            _nextWaypointIndex = Mathf.Clamp(
                _nextWaypointIndex,
                1,
                _waypoints.Length);
            EnsurePedestrianVisible();
            _pedestrian.SetPositionAndRotation(checkpoint.Position, checkpoint.Rotation);
            _pedestrian.gameObject.SetActive(true);
            UpdateAnimation();
            Physics.SyncTransforms();
        }

        private void CachePedestrianComponents()
        {
            if (_pedestrian == null) return;
            if (_pedestrianCollider == null)
                _pedestrianCollider = _pedestrian.GetComponentInChildren<CapsuleCollider>(true);
            if (_animator == null)
                _animator = _pedestrian.GetComponentInChildren<Animator>(true);

            _walkingHash = Animator.StringToHash(_walkingBool);
            _hasWalkingParameter = false;
            if (_animator == null) return;
            foreach (AnimatorControllerParameter parameter in _animator.parameters)
            {
                if (parameter.nameHash != _walkingHash ||
                    parameter.type != AnimatorControllerParameterType.Bool) continue;
                _hasWalkingParameter = true;
                break;
            }
        }

        private void EnsurePedestrianVisible()
        {
            if (_pedestrian == null) return;

            _pedestrian.gameObject.SetActive(true);
            if (_pedestrianCollider != null)
            {
                _pedestrianCollider.enabled = true;
                _pedestrianCollider.isTrigger = true;
            }
            if (_animator != null) _animator.enabled = true;
        }

        private void FaceNextWaypoint(bool immediate)
        {
            if (_pedestrian == null || _waypoints == null || _waypoints.Length < 2 ||
                _waypoints[0] == null || _waypoints[1] == null) return;

            Vector3 direction = Vector3.ProjectOnPlane(
                _waypoints[1].position - _pedestrian.position,
                Vector3.up);
            Quaternion target = direction.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(direction.normalized, Vector3.up)
                : _waypoints[0].rotation;
            _pedestrian.rotation = immediate
                ? target
                : Quaternion.RotateTowards(
                    _pedestrian.rotation,
                    target,
                    _rotationSpeedDegrees * Time.deltaTime);
        }

        private void UpdateAnimation()
        {
            if (_animator != null && _hasWalkingParameter)
                _animator.SetBool(_walkingHash, _crossing);
        }

        private bool HasRequiredReferences()
        {
            CachePedestrianComponents();
            bool hasRoute = _waypoints != null && _waypoints.Length >= 2;
            if (_pedestrian != null && hasRoute)
            {
                for (int index = 0; index < _waypoints.Length; index++)
                {
                    if (_waypoints[index] == null)
                    {
                        hasRoute = false;
                        break;
                    }
                }
            }

            if (_pedestrian != null && _pedestrianCollider != null && hasRoute) return true;

            if (!_configurationErrorReported)
            {
                Debug.LogError(
                    "Crossing pedestrian is not configured. Assign the Pedestrian, " +
                    "its Capsule Collider, and at least two ordered Waypoints in the Inspector.",
                    this);
                _configurationErrorReported = true;
            }

            _crossing = false;
            return false;
        }

        private void OnDrawGizmosSelected()
        {
            if (_waypoints == null || _waypoints.Length == 0) return;

            Gizmos.color = Color.cyan;
            for (int index = 0; index < _waypoints.Length; index++)
            {
                Transform waypoint = _waypoints[index];
                if (waypoint == null) continue;

                Gizmos.DrawWireSphere(waypoint.position, 0.2f);
                if (index == 0 || _waypoints[index - 1] == null) continue;
                Gizmos.DrawLine(_waypoints[index - 1].position, waypoint.position);
            }
        }

        public readonly struct CheckpointState
        {
            public CheckpointState(
                Vector3 position,
                Quaternion rotation,
                int nextWaypointIndex,
                bool isCrossing,
                bool hasCompletedRoute,
                bool isSuppressed,
                bool pedestrianWasActive)
            {
                Position = position;
                Rotation = rotation;
                NextWaypointIndex = nextWaypointIndex;
                IsCrossing = isCrossing;
                HasCompletedRoute = hasCompletedRoute;
                IsSuppressed = isSuppressed;
                PedestrianWasActive = pedestrianWasActive;
            }

            public Vector3 Position { get; }
            public Quaternion Rotation { get; }
            public int NextWaypointIndex { get; }
            public bool IsCrossing { get; }
            public bool HasCompletedRoute { get; }
            public bool IsSuppressed { get; }
            public bool PedestrianWasActive { get; }
        }
    }
}
