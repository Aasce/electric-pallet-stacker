using System;
using UnityEngine;

namespace ElectricPalletStackers.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class CrossingVehicleController : MonoBehaviour, IGameFailureReasonProvider
    {
        [Header("Scene References")]
        [Tooltip("The Rigidbody on the crossing vehicle instance. Do not add a second Rigidbody to this manager GameObject.")]
        [SerializeField] private Rigidbody _vehicleBody;
        [Tooltip("Ordered route points owned by Phase Anchors/Cross. Element 0 is the reset pose; the final element is the route end.")]
        [SerializeField] private Transform[] _waypoints;
        [Tooltip("First endpoint of the allowed hazard spawn segment.")]
        [SerializeField] private Transform _spawnRangeStart;
        [Tooltip("Second endpoint of the allowed hazard spawn segment.")]
        [SerializeField] private Transform _spawnRangeEnd;

        [Header("Motion")]
        [SerializeField, Min(0.1f)] private float _speed = 3f;
        [SerializeField, Min(1f)] private float _rotationSpeedDegrees = 180f;
        [SerializeField, Min(0.01f)] private float _arrivalDistance = 0.05f;

        private bool _crossing;
        private bool _hasCompletedRoute;
        private bool _suppressed;
        private int _nextWaypointIndex;
        private bool _configurationErrorReported;

        public bool IsCrossing => _crossing;
        public bool HasCompletedRoute => _hasCompletedRoute;
        public bool IsSuppressed => _suppressed;
        public GameFailureReason FailureReason => GameFailureReason.IntersectionVehicleCollision;
        public event Action CrossingStarted;
        public event Action<int> WaypointReached;
        public event Action CrossingCompleted;

        private void FixedUpdate()
        {
            if (!_crossing || !HasRequiredReferences()) return;

            Transform targetWaypoint = _waypoints[_nextWaypointIndex];
            Vector3 next = Vector3.MoveTowards(
                _vehicleBody.position,
                targetWaypoint.position,
                _speed * Time.fixedDeltaTime);
            Vector3 direction = Vector3.ProjectOnPlane(
                targetWaypoint.position - _vehicleBody.position,
                Vector3.up);
            if (direction.sqrMagnitude > 0.001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(
                    direction.normalized,
                    Vector3.up);
                Quaternion nextRotation = Quaternion.RotateTowards(
                    _vehicleBody.rotation,
                    targetRotation,
                    _rotationSpeedDegrees * Time.fixedDeltaTime);
                _vehicleBody.MoveRotation(nextRotation);
            }

            _vehicleBody.MovePosition(next);
            if ((next - targetWaypoint.position).sqrMagnitude >
                _arrivalDistance * _arrivalDistance) return;

            _vehicleBody.MovePosition(targetWaypoint.position);
            int reachedWaypointIndex = _nextWaypointIndex;
            _nextWaypointIndex++;
            WaypointReached?.Invoke(reachedWaypointIndex);
            if (_nextWaypointIndex < _waypoints.Length) return;

            _crossing = false;
            _hasCompletedRoute = true;
            CrossingCompleted?.Invoke();
        }

        public void BeginCrossing()
        {
            if (_suppressed || _crossing || _hasCompletedRoute || !HasRequiredReferences()) return;

            _nextWaypointIndex = Mathf.Clamp(
                _nextWaypointIndex,
                1,
                _waypoints.Length - 1);
            SetVehicleActive(true);
            _crossing = true;
            CrossingStarted?.Invoke();
        }

        public void SpawnForHazard(Vector3 playerPosition)
        {
            _suppressed = false;
            _crossing = false;
            _hasCompletedRoute = false;
            _nextWaypointIndex = 1;
            if (!HasRequiredReferences()) return;

            Vector3 spawnPosition = GetClampedSpawnPosition(playerPosition);
            Quaternion spawnRotation = _waypoints[0].rotation;
            Vector3 routeDirection = Vector3.ProjectOnPlane(
                _waypoints[1].position - spawnPosition,
                Vector3.up);
            if (routeDirection.sqrMagnitude > 0.001f)
                spawnRotation = Quaternion.LookRotation(routeDirection.normalized, Vector3.up);

            SetVehicleActive(true);
            RigidbodyPoseUtility.Restore(_vehicleBody, spawnPosition, spawnRotation, true);
            Physics.SyncTransforms();
        }

        public void SuppressVehicle()
        {
            _suppressed = true;
            _crossing = false;
            _hasCompletedRoute = true;
            StopBodyMotion();
            SetVehicleActive(false);
        }

        public void StopVehicle()
        {
            _crossing = false;
            StopBodyMotion();
        }

        [ContextMenu("Reset Vehicle To First Waypoint")]
        public void ResetVehicle()
        {
            _crossing = false;
            _hasCompletedRoute = false;
            _suppressed = false;
            _nextWaypointIndex = 1;
            if (!HasRequiredReferences()) return;

            SetVehicleActive(true);
            StopBodyMotion();
            _vehicleBody.position = _waypoints[0].position;
            _vehicleBody.rotation = _waypoints[0].rotation;
            SetVehicleActive(false);
            Physics.SyncTransforms();
        }

        public CheckpointState CaptureCheckpointState()
        {
            return new CheckpointState(
                _vehicleBody != null ? _vehicleBody.position : transform.position,
                _vehicleBody != null ? _vehicleBody.rotation : transform.rotation,
                _nextWaypointIndex,
                _crossing,
                _hasCompletedRoute,
                _suppressed,
                _vehicleBody != null && _vehicleBody.gameObject.activeSelf);
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
            RigidbodyPoseUtility.Restore(
                _vehicleBody,
                checkpoint.Position,
                checkpoint.Rotation,
                !_crossing);
            SetVehicleActive(checkpoint.VehicleWasActive && !_suppressed);
            Physics.SyncTransforms();
        }

        private Vector3 GetClampedSpawnPosition(Vector3 playerPosition)
        {
            if (_spawnRangeStart == null || _spawnRangeEnd == null)
                return _waypoints[0].position;

            Vector3 start = _spawnRangeStart.position;
            Vector3 segment = _spawnRangeEnd.position - start;
            float sqrLength = segment.sqrMagnitude;
            if (sqrLength < 0.0001f) return start;

            float t = Mathf.Clamp01(Vector3.Dot(playerPosition - start, segment) / sqrLength);
            return start + segment * t;
        }

        private void StopBodyMotion()
        {
            if (_vehicleBody == null || _vehicleBody.isKinematic) return;
            _vehicleBody.linearVelocity = Vector3.zero;
            _vehicleBody.angularVelocity = Vector3.zero;
        }

        private void SetVehicleActive(bool active)
        {
            if (_vehicleBody != null && _vehicleBody.gameObject.activeSelf != active)
                _vehicleBody.gameObject.SetActive(active);
        }

        private bool HasRequiredReferences()
        {
            bool hasRoute = _waypoints != null && _waypoints.Length >= 2;
            if (_vehicleBody != null && hasRoute)
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

            if (_vehicleBody != null && hasRoute) return true;

            if (!_configurationErrorReported)
            {
                Debug.LogError(
                    "CrossingVehicleController is not configured. Assign Vehicle Body " +
                    "and at least two ordered Waypoints in the Inspector.",
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
                bool vehicleWasActive)
            {
                Position = position;
                Rotation = rotation;
                NextWaypointIndex = nextWaypointIndex;
                IsCrossing = isCrossing;
                HasCompletedRoute = hasCompletedRoute;
                IsSuppressed = isSuppressed;
                VehicleWasActive = vehicleWasActive;
            }

            public Vector3 Position { get; }
            public Quaternion Rotation { get; }
            public int NextWaypointIndex { get; }
            public bool IsCrossing { get; }
            public bool HasCompletedRoute { get; }
            public bool IsSuppressed { get; }
            public bool VehicleWasActive { get; }
        }
    }
}
