using System;
using ElectricPalletStackers.Ble;
using ElectricPalletStackers.PalletStackers;
using UnityEngine;
using UnityEngine.AI;

namespace ElectricPalletStackers.NPCs
{
    [DisallowMultipleComponent]
    public sealed class NpcVehicleRammer : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float _impactDistance = 0.05f;
        [SerializeField, Min(0f)] private float _frontClearance = 0.15f;
        [SerializeField, Min(0.05f)] private float _destinationRefreshDistance = 0.1f;

        private NpcAgent _npc;
        private NavMeshAgent _agent;
        private Rigidbody _vehicleBody;
        private PalletStackerRigidbodyMotor _vehicleMotor;
        private PalletStackerCollisionReporter _collisionReporter;
        private Collider[] _npcColliders;
        private Collider[] _vehicleColliders;
        private Action _impactHandler;
        private float _movingSpeedThreshold;
        private float _originalSpeed;
        private float _originalStoppingDistance;
        private ObstacleAvoidanceType _originalAvoidance;
        private bool _originalAutoBraking;
        private Vector3 _lastDestination;
        private bool _active;
        private bool _hasDestination;
        private bool _hasCachedAgentState;
        private bool _restored;

        public void BeginApproach(
            NpcAgent npc,
            Rigidbody vehicleBody,
            PalletStackerRigidbodyMotor vehicleMotor,
            PalletStackerCollisionReporter collisionReporter,
            float movingSpeedThreshold,
            Action impactHandler)
        {
            _npc = npc;
            _agent = npc != null ? npc.Agent : GetComponent<NavMeshAgent>();
            _vehicleBody = vehicleBody;
            _vehicleMotor = vehicleMotor;
            _collisionReporter = collisionReporter;
            _movingSpeedThreshold = Mathf.Max(0f, movingSpeedThreshold);
            _impactHandler = impactHandler;
            _npcColliders = GetComponentsInChildren<Collider>();
            _vehicleColliders = vehicleBody != null
                ? vehicleBody.GetComponentsInChildren<Collider>()
                : null;
            if (_agent == null || !_agent.isOnNavMesh || _vehicleBody == null)
            {
                Destroy(this);
                return;
            }

            _originalSpeed = _agent.speed;
            _originalStoppingDistance = _agent.stoppingDistance;
            _originalAvoidance = _agent.obstacleAvoidanceType;
            _originalAutoBraking = _agent.autoBraking;
            _hasCachedAgentState = true;

            if (_npc != null) _npc.enabled = false;
            // Normal navigation deliberately keeps agents away from obstacles and
            // brakes before the destination. During this scripted accident those
            // behaviours can leave the NPC permanently hovering beside the vehicle.
            _agent.stoppingDistance = 0f;
            _agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
            _agent.autoBraking = false;
            _agent.isStopped = false;
            _active = true;
        }

        private void Update()
        {
            if (!_active || _agent == null || !_agent.isOnNavMesh || _vehicleBody == null) return;

            if (!IsVehicleMoving())
            {
                CancelApproach();
                return;
            }

            Vector3 destination = GetVehicleFrontPoint();
            if (!_hasDestination ||
                (destination - _lastDestination).sqrMagnitude >=
                _destinationRefreshDistance * _destinationRefreshDistance)
            {
                if (TryGetNavMeshDestination(destination, out Vector3 navMeshDestination))
                {
                    _agent.SetDestination(navMeshDestination);
                    _lastDestination = destination;
                    _hasDestination = true;
                }
            }

            if (DistanceToVehicle() <= _impactDistance)
                CompleteImpact();
        }

        public void CancelApproach()
        {
            if (!_active) return;
            _active = false;
            RestoreNpc();
            Destroy(this);
        }

        private bool IsVehicleMoving()
        {
            if (_vehicleMotor != null)
                return Mathf.Abs(_vehicleMotor.CurrentSpeed) > _movingSpeedThreshold;

            Vector3 planarVelocity = Vector3.ProjectOnPlane(
                _vehicleBody.linearVelocity,
                Vector3.up);
            return planarVelocity.sqrMagnitude > _movingSpeedThreshold * _movingSpeedThreshold;
        }

        private Vector3 GetVehicleFrontPoint()
        {
            Vector3 forward = _vehicleMotor != null
                ? Vector3.ProjectOnPlane(_vehicleMotor.WorldForward, Vector3.up)
                : Vector3.ProjectOnPlane(_vehicleBody.transform.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            forward.Normalize();

            Vector3 origin = _vehicleBody.position;
            float frontDistance = 0f;
            if (_vehicleColliders != null)
            {
                for (int index = 0; index < _vehicleColliders.Length; index++)
                {
                    Collider vehicleCollider = _vehicleColliders[index];
                    if (vehicleCollider == null || !vehicleCollider.enabled || vehicleCollider.isTrigger)
                        continue;

                    Bounds bounds = vehicleCollider.bounds;
                    Vector3 extents = bounds.extents;
                    float projectedExtent = Mathf.Abs(forward.x) * extents.x +
                                            Mathf.Abs(forward.y) * extents.y +
                                            Mathf.Abs(forward.z) * extents.z;
                    float projectedCenter = Vector3.Dot(bounds.center - origin, forward);
                    frontDistance = Mathf.Max(frontDistance, projectedCenter + projectedExtent);
                }
            }

            return origin + forward * (frontDistance + _frontClearance);
        }

        private bool TryGetNavMeshDestination(Vector3 target, out Vector3 destination)
        {
            NavMeshQueryFilter filter = new()
            {
                agentTypeID = _agent.agentTypeID,
                areaMask = _agent.areaMask
            };

            if (NavMesh.SamplePosition(target, out NavMeshHit hit, 1.5f, filter))
            {
                destination = hit.position;
                return true;
            }

            destination = default;
            return false;
        }

        private float DistanceToVehicle()
        {
            float closestDistance = Vector3.Distance(transform.position, _vehicleBody.position);
            if (_vehicleColliders == null) return closestDistance;

            for (int vehicleIndex = 0; vehicleIndex < _vehicleColliders.Length; vehicleIndex++)
            {
                Collider vehicleCollider = _vehicleColliders[vehicleIndex];
                if (vehicleCollider == null || !vehicleCollider.enabled || vehicleCollider.isTrigger) continue;

                if (_npcColliders == null || _npcColliders.Length == 0)
                {
                    float pivotDistance = Vector3.Distance(
                        transform.position,
                        vehicleCollider.ClosestPoint(transform.position));
                    closestDistance = Mathf.Min(closestDistance, pivotDistance);
                    continue;
                }

                for (int npcIndex = 0; npcIndex < _npcColliders.Length; npcIndex++)
                {
                    Collider npcCollider = _npcColliders[npcIndex];
                    if (npcCollider == null || !npcCollider.enabled || npcCollider.isTrigger) continue;

                    // Measure the gap between collider surfaces rather than from the
                    // NPC pivot. The old pivot distance could never reach its impact
                    // threshold because the capsule collider stopped about one radius
                    // away from the vehicle first.
                    Vector3 vehiclePoint = vehicleCollider.ClosestPoint(npcCollider.bounds.center);
                    Vector3 npcPoint = npcCollider.ClosestPoint(vehiclePoint);
                    vehiclePoint = vehicleCollider.ClosestPoint(npcPoint);
                    closestDistance = Mathf.Min(
                        closestDistance,
                        Vector3.Distance(npcPoint, vehiclePoint));
                }
            }

            return closestDistance;
        }

        private void CompleteImpact()
        {
            if (!_active) return;
            _active = false;
            if (_agent != null && _agent.isOnNavMesh) _agent.isStopped = true;
            _collisionReporter?.ReportCollision();
            _impactHandler?.Invoke();
            RestoreNpc();
            Destroy(this);
        }

        private void OnDestroy()
        {
            RestoreNpc();
        }

        private void RestoreNpc()
        {
            if (_restored) return;
            _restored = true;

            if (_agent != null && _hasCachedAgentState)
            {
                _agent.speed = _originalSpeed;
                _agent.stoppingDistance = _originalStoppingDistance;
                _agent.obstacleAvoidanceType = _originalAvoidance;
                _agent.autoBraking = _originalAutoBraking;
            }

            if (_npc != null)
            {
                _npc.enabled = true;
                _npc.ResumeNormalBehaviour();
            }
        }
    }
}
