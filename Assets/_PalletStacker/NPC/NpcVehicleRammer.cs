using ElectricPalletStackers.Ble;
using UnityEngine;
using UnityEngine.AI;

namespace ElectricPalletStackers.NPCs
{
    [DisallowMultipleComponent]
    public sealed class NpcVehicleRammer : MonoBehaviour
    {
        [SerializeField, Min(0.05f)] private float _impactDistance = 0.25f;
        [SerializeField, Min(1f)] private float _maximumChaseDuration = 8f;

        private NpcAgent _npc;
        private NavMeshAgent _agent;
        private Transform _vehicle;
        private PalletStackerCollisionReporter _collisionReporter;
        private Collider[] _vehicleColliders;
        private float _deadline;
        private float _originalSpeed;
        private float _originalStoppingDistance;
        private ObstacleAvoidanceType _originalAvoidance;
        private bool _active;
        private bool _hasCachedAgentState;
        private bool _restored;

        public void BeginRam(
            NpcAgent npc,
            Transform vehicle,
            PalletStackerCollisionReporter collisionReporter,
            float speed)
        {
            _npc = npc;
            _agent = npc != null ? npc.Agent : GetComponent<NavMeshAgent>();
            _vehicle = vehicle;
            _collisionReporter = collisionReporter;
            _vehicleColliders = vehicle != null ? vehicle.GetComponentsInChildren<Collider>() : null;
            if (_agent == null || !_agent.isOnNavMesh || _vehicle == null)
            {
                _collisionReporter?.ReportCollision();
                Destroy(this);
                return;
            }

            _originalSpeed = _agent.speed;
            _originalStoppingDistance = _agent.stoppingDistance;
            _originalAvoidance = _agent.obstacleAvoidanceType;
            _hasCachedAgentState = true;

            if (_npc != null) _npc.enabled = false;
            _agent.speed = Mathf.Max(speed, _originalSpeed);
            _agent.stoppingDistance = 0f;
            _agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
            _agent.isStopped = false;
            _deadline = Time.time + _maximumChaseDuration;
            _active = true;
        }

        private void Update()
        {
            if (!_active || _agent == null || !_agent.isOnNavMesh || _vehicle == null) return;

            _agent.SetDestination(_vehicle.position);
            if (DistanceToVehicle() <= _impactDistance)
            {
                CompleteImpact();
                return;
            }

            if (Time.time >= _deadline)
            {
                Debug.LogWarning("NPC ram timed out before contact; applying the simulated accident result.", this);
                CompleteImpact();
            }
        }

        private float DistanceToVehicle()
        {
            float closestDistance = Vector3.Distance(transform.position, _vehicle.position);
            if (_vehicleColliders == null) return closestDistance;

            for (int index = 0; index < _vehicleColliders.Length; index++)
            {
                Collider vehicleCollider = _vehicleColliders[index];
                if (vehicleCollider == null || !vehicleCollider.enabled || vehicleCollider.isTrigger) continue;

                float distance = Vector3.Distance(
                    transform.position,
                    vehicleCollider.ClosestPoint(transform.position));
                closestDistance = Mathf.Min(closestDistance, distance);
            }

            return closestDistance;
        }

        private void CompleteImpact()
        {
            if (!_active) return;
            _active = false;
            if (_agent != null && _agent.isOnNavMesh) _agent.isStopped = true;
            _collisionReporter?.ReportCollision();
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
            }

            if (_npc != null) _npc.enabled = true;
        }
    }
}
