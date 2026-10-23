using System;
using ElectricPalletStackers.Audio;
using UnityEngine;

namespace ElectricPalletStackers.Gameplay
{
    /// <summary>
    /// Controls the pedestrian authored at the Cross phase. The legacy class name is
    /// intentionally retained so existing scene and checkpoint references stay valid.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Electric Pallet Stackers/Gameplay/Crossing Pedestrian Controller")]
    public sealed class CrossingVehicleController : MonoBehaviour
    {
        [Header("Scene References")]
        [Tooltip("The NPC prefab instance placed directly in the scene.")]
        [SerializeField] private Transform _pedestrian;
        [Tooltip("Ordered walking points. Element 0 is the authored indoor start pose.")]
        [SerializeField] private Transform[] _waypoints;

        [Header("Walking")]
        [SerializeField, Min(0.1f)] private float _speed = 2.025f;
        [SerializeField, Min(1f)] private float _rotationSpeedDegrees = 540f;
        [SerializeField, Min(0.01f)] private float _arrivalDistance = 0.12f;

        [Header("Door")]
        [Tooltip("Door hinge transform. Assign the authored Anchor directly in the Inspector.")]
        [SerializeField] private Transform _doorAnchor;
        [SerializeField, Min(0)] private int _openDoorWaypointIndex = 1;
        [SerializeField] private float _closedDoorAngleY;
        [SerializeField] private float _openDoorAngleY = 80f;
        [SerializeField, Min(0.01f)] private float _doorOpenDuration = 0.75f;

        [Header("Footsteps")]
        [Tooltip("Distance travelled between footstep sounds.")]
        [SerializeField, Min(0.1f)] private float _stepDistance = 0.72f;

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
        private Animator _cachedSpeedAnimator;
        private float _baseAnimatorSpeed = 1f;
        private float _distanceSinceStep;
        private Vector3 _doorClosedEuler;
        private bool _isOpeningDoor;
        private float _doorOpenElapsed;

        public bool IsCrossing => _crossing;
        public bool HasCompletedRoute => _hasCompletedRoute;
        public bool IsSuppressed => _suppressed;
        public event Action CrossingStarted;
        public event Action<int> WaypointReached;
        public event Action CrossingCompleted;

        private void Awake()
        {
            CachePedestrianComponents();
            InitializeDoor();
            ResetVehicle();
        }

        private void Update()
        {
            if (!_crossing || !HasRequiredReferences()) return;

            if (_isOpeningDoor)
            {
                UpdateDoorOpening();
                return;
            }

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
            UpdateFootsteps(Vector3.Distance(currentPosition, nextPosition));
            if ((nextPosition - targetPosition).sqrMagnitude >
                _arrivalDistance * _arrivalDistance) return;

            _pedestrian.position = targetPosition;
            int reachedWaypointIndex = _nextWaypointIndex;
            _nextWaypointIndex++;
            WaypointReached?.Invoke(reachedWaypointIndex);
            if (reachedWaypointIndex == _openDoorWaypointIndex)
            {
                BeginDoorOpening();
                return;
            }
            if (_nextWaypointIndex < _waypoints.Length) return;

            CompleteRoute();
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
            _distanceSinceStep = 0f;
            UpdateAnimation();
            CrossingStarted?.Invoke();
        }

        /// <summary>
        /// Arms the pedestrian prefab and restores its authored indoor start pose.
        /// </summary>
        public void ArmForHazard()
        {
            _suppressed = false;
            _crossing = false;
            _hasCompletedRoute = false;
            _nextWaypointIndex = 1;
            ResetMovementTiming();
            if (!HasRequiredReferences()) return;

            EnsurePedestrianVisible();
            _pedestrian.position = _waypoints[0].position;
            ResetDoor();
            FaceNextWaypoint(true);
            UpdateAnimation();
            Physics.SyncTransforms();
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
            ResetMovementTiming();
            EnsurePedestrianVisible();
            if (HasRequiredReferences())
            {
                _pedestrian.position = _waypoints[0].position;
                FaceNextWaypoint(true);
            }
            ResetDoor();
            UpdateAnimation();
            Physics.SyncTransforms();
        }

        public void StopVehicle()
        {
            _crossing = false;
            ResetMovementTiming();
            UpdateAnimation();
        }

        [ContextMenu("Reset Pedestrian To First Waypoint")]
        public void ResetVehicle()
        {
            _crossing = false;
            _hasCompletedRoute = false;
            _suppressed = false;
            _nextWaypointIndex = 1;
            ResetMovementTiming();
            if (!HasRequiredReferences()) return;

            EnsurePedestrianVisible();
            _pedestrian.position = _waypoints[0].position;
            ResetDoor();
            FaceNextWaypoint(true);
            UpdateAnimation();
            Physics.SyncTransforms();
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
                _pedestrian != null && _pedestrian.gameObject.activeSelf,
                _doorAnchor != null ? _doorAnchor.localRotation : Quaternion.identity);
        }

        public void RestoreCheckpointState(CheckpointState checkpoint)
        {
            _crossing = checkpoint.IsCrossing;
            _hasCompletedRoute = checkpoint.HasCompletedRoute;
            _suppressed = checkpoint.IsSuppressed;
            _nextWaypointIndex = checkpoint.NextWaypointIndex;
            ResetMovementTiming();
            if (!HasRequiredReferences()) return;

            _nextWaypointIndex = Mathf.Clamp(
                _nextWaypointIndex,
                1,
                _waypoints.Length);
            EnsurePedestrianVisible();
            _pedestrian.SetPositionAndRotation(checkpoint.Position, checkpoint.Rotation);
            _pedestrian.gameObject.SetActive(true);
            if (_doorAnchor != null) _doorAnchor.localRotation = checkpoint.DoorLocalRotation;
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

            if (_animator != _cachedSpeedAnimator)
            {
                _cachedSpeedAnimator = _animator;
                _baseAnimatorSpeed = _animator != null ? _animator.speed : 1f;
            }

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

        private void InitializeDoor()
        {
            if (_doorAnchor == null) return;

            Vector3 currentEuler = _doorAnchor.localEulerAngles;
            _doorClosedEuler = new Vector3(
                currentEuler.x,
                _closedDoorAngleY,
                currentEuler.z);
        }

        private void BeginDoorOpening()
        {
            _isOpeningDoor = true;
            _doorOpenElapsed = 0f;
            SetDoorAngle(_closedDoorAngleY);
            UpdateAnimation();
        }

        private void UpdateDoorOpening()
        {
            _doorOpenElapsed += Time.deltaTime;
            float duration = Mathf.Max(0.01f, _doorOpenDuration);
            float progress = Mathf.Clamp01(_doorOpenElapsed / duration);
            SetDoorAngle(Mathf.LerpAngle(_closedDoorAngleY, _openDoorAngleY, progress));
            if (progress < 1f) return;

            _isOpeningDoor = false;
            UpdateAnimation();
            if (_nextWaypointIndex >= _waypoints.Length) CompleteRoute();
        }

        private void ResetDoor()
        {
            _isOpeningDoor = false;
            _doorOpenElapsed = 0f;
            SetDoorAngle(_closedDoorAngleY);
        }

        private void SetDoorAngle(float angleY)
        {
            if (_doorAnchor == null) return;
            _doorAnchor.localRotation = Quaternion.Euler(
                _doorClosedEuler.x,
                angleY,
                _doorClosedEuler.z);
        }

        private void CompleteRoute()
        {
            _crossing = false;
            _hasCompletedRoute = true;
            ResetMovementTiming();
            UpdateAnimation();
            CrossingCompleted?.Invoke();
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
                _animator.SetBool(_walkingHash, _crossing && !_isOpeningDoor);
            if (_animator == null) return;
            _animator.speed = _baseAnimatorSpeed;
        }

        private void UpdateFootsteps(float travelledDistance)
        {
            _distanceSinceStep += Mathf.Max(0f, travelledDistance);
            float stepDistance = Mathf.Max(0.1f, _stepDistance);
            while (_distanceSinceStep >= stepDistance)
            {
                _distanceSinceStep -= stepDistance;
                AudioManager.Instance?.Play(PalletStackerAudioKeys.HumanWalking, _pedestrian);
            }
        }

        private void ResetMovementTiming()
        {
            _distanceSinceStep = 0f;
            _isOpeningDoor = false;
            _doorOpenElapsed = 0f;
            if (_animator != null) _animator.speed = _baseAnimatorSpeed;
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

            bool hasDoor = _doorAnchor != null &&
                           _waypoints != null &&
                           _openDoorWaypointIndex > 0 &&
                           _openDoorWaypointIndex < _waypoints.Length;
            if (_pedestrian != null && _pedestrianCollider != null && hasRoute && hasDoor)
                return true;

            if (!_configurationErrorReported)
            {
                Debug.LogError(
                    "Crossing pedestrian is not configured. Assign the Pedestrian, " +
                    "its Capsule Collider, the Door Anchor, and ordered Waypoints including " +
                    "the open-door point in the Inspector.",
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
                bool pedestrianWasActive,
                Quaternion doorLocalRotation)
            {
                Position = position;
                Rotation = rotation;
                NextWaypointIndex = nextWaypointIndex;
                IsCrossing = isCrossing;
                HasCompletedRoute = hasCompletedRoute;
                IsSuppressed = isSuppressed;
                PedestrianWasActive = pedestrianWasActive;
                DoorLocalRotation = doorLocalRotation;
            }

            public Vector3 Position { get; }
            public Quaternion Rotation { get; }
            public int NextWaypointIndex { get; }
            public bool IsCrossing { get; }
            public bool HasCompletedRoute { get; }
            public bool IsSuppressed { get; }
            public bool PedestrianWasActive { get; }
            public Quaternion DoorLocalRotation { get; }
        }
    }
}
