using UnityEngine;

namespace ElectricPalletStackers.NPCs
{
    [DisallowMultipleComponent]
    public sealed class NpcWaypointRoute : MonoBehaviour
    {
        [SerializeField] private Transform[] _waypoints;
        [SerializeField] private bool _loop = true;
        [SerializeField] private bool _pingPong;
        [SerializeField] private Vector2 _waitAtWaypoint = new(0.5f, 1.5f);

        public int Count => _waypoints?.Length ?? 0;
        public bool Loop => _loop;
        public bool PingPong => _pingPong;
        public float RandomWait => Random.Range(
            Mathf.Min(_waitAtWaypoint.x, _waitAtWaypoint.y),
            Mathf.Max(_waitAtWaypoint.x, _waitAtWaypoint.y));

        public bool TryGetWaypoint(int index, out Vector3 position)
        {
            position = default;
            if (_waypoints == null || index < 0 || index >= _waypoints.Length) return false;
            Transform waypoint = _waypoints[index];
            if (waypoint == null) return false;
            position = waypoint.position;
            return true;
        }

        public int GetNextIndex(int currentIndex, ref int direction)
        {
            if (Count <= 1) return 0;
            if (_pingPong)
            {
                int candidate = currentIndex + direction;
                if (candidate >= Count || candidate < 0)
                {
                    direction *= -1;
                    candidate = Mathf.Clamp(currentIndex + direction, 0, Count - 1);
                }
                return candidate;
            }

            int next = currentIndex + 1;
            return next < Count ? next : (_loop ? 0 : Count - 1);
        }

        public int FindSafestIndex(Vector3 origin, Vector3 vehiclePosition, Vector3 vehicleForward)
        {
            if (Count == 0) return -1;
            int bestIndex = -1;
            float bestScore = float.NegativeInfinity;
            Vector3 planarForward = Vector3.ProjectOnPlane(vehicleForward, Vector3.up).normalized;
            for (int index = 0; index < Count; index++)
            {
                if (!TryGetWaypoint(index, out Vector3 candidate)) continue;
                Vector3 fromVehicle = Vector3.ProjectOnPlane(candidate - vehiclePosition, Vector3.up);
                float lateral = planarForward.sqrMagnitude > 0.001f
                    ? Vector3.Cross(planarForward, fromVehicle).magnitude
                    : fromVehicle.magnitude;
                float score = fromVehicle.sqrMagnitude + lateral * lateral * 2f
                              - (candidate - origin).sqrMagnitude * 0.15f;
                if (score <= bestScore) continue;
                bestScore = score;
                bestIndex = index;
            }
            return bestIndex;
        }

        private void OnDrawGizmosSelected()
        {
            if (_waypoints == null || _waypoints.Length == 0) return;
            Gizmos.color = new Color(0.15f, 0.85f, 1f, 0.8f);
            for (int index = 0; index < _waypoints.Length; index++)
            {
                Transform point = _waypoints[index];
                if (point == null) continue;
                Gizmos.DrawSphere(point.position, 0.12f);
                int next = index + 1;
                if (next < _waypoints.Length && _waypoints[next] != null)
                    Gizmos.DrawLine(point.position, _waypoints[next].position);
                else if (_loop && _waypoints.Length > 1 && _waypoints[0] != null)
                    Gizmos.DrawLine(point.position, _waypoints[0].position);
            }
        }
    }
}
