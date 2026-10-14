using ElectricPalletStackers.Ble;
using ElectricPalletStackers.PalletStackers;
using UnityEngine;

namespace ElectricPalletStackers.NPCs
{
    /// <summary>
    /// Owns the vehicle geometry that participates in NPC path prediction and contact.
    /// Sensor volumes such as proximity warnings are intentionally not part of this target.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NpcVehicleCollisionTarget : MonoBehaviour
    {
        [SerializeField] private Collider[] _vehicleColliders;
        [SerializeField] private PalletStackerLoadHandler _loadHandler;
        [SerializeField] private PalletStackerCollisionReporter _collisionReporter;

        private PalletStackerLoad _cachedHeldLoad;
        private Collider[] _heldLoadColliders;

        public bool TryGetFirstPathCollision(
            Vector3 pathStart,
            Vector3 pathEnd,
            float agentRadius,
            out Vector3 collisionPoint)
        {
            collisionPoint = default;
            Vector3 segment = Vector3.ProjectOnPlane(pathEnd - pathStart, Vector3.up);
            float segmentLength = segment.magnitude;
            if (segmentLength < 0.01f) return false;

            Vector3 direction = segment / segmentLength;
            bool found = false;
            float nearestDistance = float.PositiveInfinity;

            if (_vehicleColliders != null)
            {
                for (int index = 0; index < _vehicleColliders.Length; index++)
                    EvaluateCollider(
                        _vehicleColliders[index],
                        pathStart,
                        direction,
                        segmentLength,
                        agentRadius,
                        ref found,
                        ref nearestDistance);
            }

            RefreshHeldLoadColliders();
            if (_heldLoadColliders != null)
            {
                for (int index = 0; index < _heldLoadColliders.Length; index++)
                {
                    Collider loadCollider = _heldLoadColliders[index];
                    if (loadCollider == null || loadCollider.isTrigger) continue;
                    EvaluateCollider(
                        loadCollider,
                        pathStart,
                        direction,
                        segmentLength,
                        agentRadius,
                        ref found,
                        ref nearestDistance);
                }
            }

            if (!found) return false;
            collisionPoint = pathStart + direction * nearestDistance;
            return true;
        }

        public bool ContainsContactCollider(Collider other)
        {
            if (other == null) return false;

            if (_vehicleColliders != null)
            {
                for (int index = 0; index < _vehicleColliders.Length; index++)
                    if (other == _vehicleColliders[index]) return true;
            }

            if (other.isTrigger || _loadHandler == null || _loadHandler.HeldLoad == null)
                return false;
            PalletStackerLoad load = other.GetComponentInParent<PalletStackerLoad>();
            return load != null && load == _loadHandler.HeldLoad;
        }

        public bool ReportNpcCollision()
        {
            if (_collisionReporter == null) return false;
            _collisionReporter.ReportCollision();
            return true;
        }

        private static void EvaluateCollider(
            Collider obstacle,
            Vector3 pathStart,
            Vector3 direction,
            float segmentLength,
            float agentRadius,
            ref bool found,
            ref float nearestDistance)
        {
            if (obstacle == null || !obstacle.enabled || !obstacle.gameObject.activeInHierarchy)
                return;

            Vector3 lateralAxis = Vector3.Cross(Vector3.up, direction);
            GetPlanarProjection(
                obstacle,
                direction,
                lateralAxis,
                out Vector3 center,
                out float alongRadius,
                out float lateralRadius);
            Vector3 toCenter = Vector3.ProjectOnPlane(center - pathStart, Vector3.up);
            float centerAlong = Vector3.Dot(toCenter, direction);
            float nearestAlong = Mathf.Max(0f, centerAlong - alongRadius - agentRadius);
            float farthestAlong = centerAlong + alongRadius + agentRadius;
            if (farthestAlong <= 0f || nearestAlong > segmentLength) return;

            float lateralDistance = Mathf.Abs(Vector3.Dot(toCenter, lateralAxis));
            if (lateralDistance > lateralRadius + agentRadius) return;
            if (nearestAlong >= nearestDistance) return;

            found = true;
            nearestDistance = nearestAlong;
        }

        private static void GetPlanarProjection(
            Collider obstacle,
            Vector3 direction,
            Vector3 lateralAxis,
            out Vector3 center,
            out float alongRadius,
            out float lateralRadius)
        {
            if (obstacle is BoxCollider box)
            {
                center = box.transform.TransformPoint(box.center);
                Vector3 halfSize = box.size * 0.5f;
                Vector3 axisX = box.transform.TransformVector(Vector3.right * halfSize.x);
                Vector3 axisY = box.transform.TransformVector(Vector3.up * halfSize.y);
                Vector3 axisZ = box.transform.TransformVector(Vector3.forward * halfSize.z);
                alongRadius = Mathf.Abs(Vector3.Dot(direction, axisX)) +
                              Mathf.Abs(Vector3.Dot(direction, axisY)) +
                              Mathf.Abs(Vector3.Dot(direction, axisZ));
                lateralRadius = Mathf.Abs(Vector3.Dot(lateralAxis, axisX)) +
                                Mathf.Abs(Vector3.Dot(lateralAxis, axisY)) +
                                Mathf.Abs(Vector3.Dot(lateralAxis, axisZ));
                return;
            }

            Bounds bounds = obstacle.bounds;
            center = bounds.center;
            Vector3 extents = bounds.extents;
            alongRadius = Mathf.Abs(direction.x) * extents.x +
                          Mathf.Abs(direction.z) * extents.z;
            lateralRadius = Mathf.Abs(lateralAxis.x) * extents.x +
                            Mathf.Abs(lateralAxis.z) * extents.z;
        }

        private void RefreshHeldLoadColliders()
        {
            PalletStackerLoad heldLoad = _loadHandler != null ? _loadHandler.HeldLoad : null;
            if (heldLoad == _cachedHeldLoad) return;

            _cachedHeldLoad = heldLoad;
            _heldLoadColliders = heldLoad != null
                ? heldLoad.GetComponentsInChildren<Collider>(true)
                : null;
        }
    }
}
