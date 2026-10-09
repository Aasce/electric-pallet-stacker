using System;
using ElectricPalletStackers.Ble;
using ElectricPalletStackers.PalletStackers;
using UnityEngine;

namespace ElectricPalletStackers.NPCs
{
    [DisallowMultipleComponent]
    public sealed class NpcVehicleRammer : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float _approachSpeed = 2.2f;
        [SerializeField, Min(0f)] private float _impactDistance = 0.08f;
        [SerializeField, Min(0f)] private float _frontClearance = 0.15f;

        private NpcAgent _npc;
        private Rigidbody _vehicleBody;
        private PalletStackerRigidbodyMotor _vehicleMotor;
        private PalletStackerCollisionReporter _collisionReporter;
        private Collider[] _npcColliders;
        private Collider[] _vehicleColliders;
        private Action _impactHandler;
        private float _movingSpeedThreshold;
        private bool _active;
        private bool _vehicleMoving;
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
            _vehicleBody = vehicleBody;
            _vehicleMotor = vehicleMotor;
            _collisionReporter = collisionReporter;
            _movingSpeedThreshold = Mathf.Max(0f, movingSpeedThreshold);
            _impactHandler = impactHandler;
            _npcColliders = GetComponentsInChildren<Collider>();
            _vehicleColliders = vehicleBody != null
                ? vehicleBody.GetComponentsInChildren<Collider>()
                : null;
            if (_npc == null || _vehicleBody == null)
            {
                Destroy(this);
                return;
            }

            _npc.BeginScriptedMovement();
            _active = true;
            _vehicleMoving = IsVehicleMoving();
        }

        private void Update()
        {
            if (!_active || _vehicleBody == null) return;
            if (!_vehicleMoving) return;

            Vector3 destination = GetVehicleFrontPoint();
            Vector3 current = transform.position;
            Vector3 target = new(destination.x, current.y, destination.z);
            Vector3 direction = target - current;
            transform.position = Vector3.MoveTowards(
                current,
                target,
                _approachSpeed * Time.deltaTime);
            if (direction.sqrMagnitude > 0.001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    targetRotation,
                    540f * Time.deltaTime);
            }

            if (DistanceToVehicle() <= _impactDistance) CompleteImpact();
        }

        public void SetVehicleMoving(bool moving)
        {
            _vehicleMoving = moving;
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
            Vector3 planarVelocity = Vector3.ProjectOnPlane(_vehicleBody.linearVelocity, Vector3.up);
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
                    if (vehicleCollider == null || !vehicleCollider.enabled || vehicleCollider.isTrigger) continue;
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
                    closestDistance = Mathf.Min(
                        closestDistance,
                        Vector3.Distance(transform.position, vehicleCollider.ClosestPoint(transform.position)));
                    continue;
                }
                for (int npcIndex = 0; npcIndex < _npcColliders.Length; npcIndex++)
                {
                    Collider npcCollider = _npcColliders[npcIndex];
                    if (npcCollider == null || !npcCollider.enabled || npcCollider.isTrigger) continue;
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
            _collisionReporter?.ReportCollision();
            _impactHandler?.Invoke();
            RestoreNpc();
            Destroy(this);
        }

        private void OnDestroy() => RestoreNpc();

        private void RestoreNpc()
        {
            if (_restored) return;
            _restored = true;
            _npc?.ResumeNormalBehaviour();
        }
    }
}
