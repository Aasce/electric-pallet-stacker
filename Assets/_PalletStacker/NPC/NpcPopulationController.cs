using System.Collections.Generic;
using ElectricPalletStackers.Gameplay;
using ElectricPalletStackers.PalletStackers;
using UnityEngine;
using UnityEngine.AI;

namespace ElectricPalletStackers.NPCs
{
    [DisallowMultipleComponent]
    public sealed class NpcPopulationController : MonoBehaviour, IGameRoundParticipant, IGameResettable
    {
        [Header("Population")]
        [SerializeField] private NpcAgent _npcPrefab;
        [SerializeField, Min(0)] private int _npcCount = 4;
        [SerializeField] private Transform _runtimeParent;
        [SerializeField, Min(0f)] private float _minimumSpawnSeparation = 2f;
        [SerializeField, Min(0f)] private float _vehicleSpawnClearance = 4f;

        [Header("Vehicle")]
        [SerializeField] private Transform _vehicleTransform;
        [SerializeField] private Rigidbody _vehicleBody;

        [Header("Horn response")]
        [SerializeField] private PalletStackerHornOutput _hornOutput;
        [SerializeField, Min(0.5f)] private float _hornReactionRadius = 8f;
        [SerializeField, Min(0.5f)] private float _hornEvadeDistance = 2.5f;
        [SerializeField, Min(0.5f)] private float _hornEdgeSearchRadius = 4f;
        [SerializeField, Min(0f)] private float _hornResponseCooldown = 3f;
        [SerializeField, Min(0f)] private float _hornMovingSpeedThreshold = 0.2f;
        [SerializeField, Range(-1f, 1f)] private float _stationaryHornFrontDot = 0f;

        [Header("Edge stops")]
        [SerializeField, Min(0.25f)] private float _edgeSearchRadius = 8f;
        [SerializeField, Min(0.05f)] private float _edgeClearance = 0.65f;
        [SerializeField, Min(4)] private int _pointSearchAttempts = 24;

        [Header("Conversations")]
        [SerializeField, Min(0.25f)] private float _conversationCheckInterval = 2f;
        [SerializeField, Range(0f, 1f)] private float _conversationChancePerCheck = 0.35f;
        [SerializeField, Min(1f)] private float _conversationSearchRadius = 12f;
        [SerializeField, Min(0.5f)] private float _conversationSpacing = 1.5f;
        [SerializeField] private Vector2 _conversationDuration = new(4f, 8f);
        [SerializeField] private Vector2 _conversationCooldown = new(10f, 20f);

        private readonly List<NpcAgent> _npcs = new();
        private NavMeshTriangulation _triangulation;
        private Collider[] _vehicleColliders;
        private float _nextConversationCheck;
        private bool _roundActive;

        public float ConversationCooldown => RandomInRange(_conversationCooldown);
        public int NpcCount => _npcCount;

        private void OnEnable()
        {
            if (_hornOutput != null) _hornOutput.HornChanged += OnHornChanged;
            _vehicleColliders = _vehicleBody != null
                ? _vehicleBody.GetComponentsInChildren<Collider>()
                : null;
            RebuildPopulationCache();
        }

        private void OnDisable()
        {
            if (_hornOutput != null) _hornOutput.HornChanged -= OnHornChanged;
        }

        private void Start()
        {
            RefreshTriangulation();
            SynchronizePopulation();
        }

        private void Update()
        {
            if (!_roundActive || Time.time < _nextConversationCheck) return;

            _nextConversationCheck = Time.time + _conversationCheckInterval;
            TryStartConversation();
        }

        public void PrepareRound()
        {
            _roundActive = true;
            RefreshTriangulation();
            SynchronizePopulation();
            RepositionPopulation(true);
            _nextConversationCheck = Time.time + _conversationCheckInterval;
        }

        public void FinishRound(GameState result)
        {
            _roundActive = false;
            for (int index = 0; index < _npcs.Count; index++)
                if (_npcs[index] != null) _npcs[index].SetRoundActive(false);
        }

        public void ResetState()
        {
            _roundActive = false;
            RefreshTriangulation();
            SynchronizePopulation();
            RepositionPopulation(false);
        }

        public void SetNpcCount(int count)
        {
            _npcCount = Mathf.Max(0, count);
            if (Application.isPlaying) SynchronizePopulation();
        }

        public bool TryGetWanderPoint(NpcAgent npc, float maximumDistance, out Vector3 point)
        {
            point = default;
            if (npc == null || npc.Agent == null) return false;

            NavMeshQueryFilter filter = CreateFilter(npc.Agent);
            for (int attempt = 0; attempt < _pointSearchAttempts; attempt++)
            {
                Vector2 offset2D = Random.insideUnitCircle * maximumDistance;
                Vector3 candidate = npc.transform.position + new Vector3(offset2D.x, 0f, offset2D.y);
                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, maximumDistance * 0.25f, filter))
                    continue;
                if ((hit.position - npc.transform.position).sqrMagnitude < 4f) continue;
                if (!HasCompletePath(npc.Agent, hit.position)) continue;

                point = hit.position;
                return true;
            }

            return TryGetRandomNavMeshPoint(npc.Agent, npc.transform.position, 0f, out point);
        }

        public bool TryGetEdgeStopPoint(NpcAgent npc, Vector3 origin, out Vector3 point)
        {
            point = default;
            if (npc == null || npc.Agent == null) return false;

            NavMeshQueryFilter filter = CreateFilter(npc.Agent);
            if (TryBuildEdgePoint(npc.Agent, origin, filter, out point)) return true;

            for (int attempt = 0; attempt < _pointSearchAttempts; attempt++)
            {
                Vector2 offset2D = Random.insideUnitCircle * (_edgeSearchRadius * 2f);
                Vector3 candidate = origin + new Vector3(offset2D.x, 0f, offset2D.y);
                if (!NavMesh.SamplePosition(candidate, out NavMeshHit sample, _edgeSearchRadius, filter))
                    continue;
                if (TryBuildEdgePoint(npc.Agent, sample.position, filter, out point)) return true;
            }

            return false;
        }

        public bool TryGetVehicleEvadePoint(
            NpcAgent npc,
            Vector3 vehicleVelocity,
            float evadeDistance,
            out Vector3 point)
        {
            point = default;
            if (npc == null || npc.Agent == null || _vehicleTransform == null) return false;

            Vector3 travel = Vector3.ProjectOnPlane(vehicleVelocity, Vector3.up).normalized;
            if (travel.sqrMagnitude < 0.001f) return false;

            Vector3 closestVehiclePoint = GetClosestVehiclePoint(npc.transform.position);
            Vector3 away = Vector3.ProjectOnPlane(
                npc.transform.position - closestVehiclePoint,
                Vector3.up).normalized;
            if (away.sqrMagnitude < 0.001f)
            {
                away = Vector3.ProjectOnPlane(
                    npc.transform.position - _vehicleTransform.position,
                    Vector3.up).normalized;
            }
            Vector3 side = Vector3.Cross(Vector3.up, travel).normalized;
            if (Vector3.Dot(side, away) < 0f) side = -side;

            NavMeshQueryFilter filter = CreateFilter(npc.Agent);
            Vector3 firstCandidate = npc.transform.position + side * evadeDistance + away * 0.5f;
            if (TryGetReachableSample(npc.Agent, firstCandidate, evadeDistance, filter, out point))
                return true;

            Vector3 secondCandidate = npc.transform.position - side * evadeDistance + away * 0.5f;
            return TryGetReachableSample(npc.Agent, secondCandidate, evadeDistance, filter, out point);
        }

        public bool TryGetHornEvadePoint(
            NpcAgent npc,
            Vector3 hornPosition,
            float evadeDistance,
            out Vector3 point)
        {
            point = default;
            if (npc == null || npc.Agent == null) return false;

            Vector3 away = Vector3.ProjectOnPlane(
                npc.transform.position - hornPosition,
                Vector3.up).normalized;
            if (away.sqrMagnitude < 0.001f)
                away = Vector3.ProjectOnPlane(npc.transform.forward, Vector3.up).normalized;
            if (away.sqrMagnitude < 0.001f) return false;

            NavMeshQueryFilter filter = CreateFilter(npc.Agent);
            Vector3 retreatOrigin = npc.transform.position + away * evadeDistance;
            if (TryGetHornEdgePoint(npc, hornPosition, retreatOrigin, filter, out point))
                return true;

            if (TryGetHornEvadeSample(npc, hornPosition, away, evadeDistance, filter, out point))
                return true;

            const float fallbackAngle = 45f;
            Vector3 firstDirection = Quaternion.AngleAxis(fallbackAngle, Vector3.up) * away;
            Vector3 firstRetreatOrigin = npc.transform.position + firstDirection * evadeDistance;
            if (TryGetHornEdgePoint(npc, hornPosition, firstRetreatOrigin, filter, out point))
                return true;

            if (TryGetHornEvadeSample(
                    npc,
                    hornPosition,
                    firstDirection,
                    evadeDistance,
                    filter,
                    out point))
                return true;

            Vector3 secondDirection = Quaternion.AngleAxis(-fallbackAngle, Vector3.up) * away;
            Vector3 secondRetreatOrigin = npc.transform.position + secondDirection * evadeDistance;
            if (TryGetHornEdgePoint(npc, hornPosition, secondRetreatOrigin, filter, out point))
                return true;

            return TryGetHornEvadeSample(
                npc,
                hornPosition,
                secondDirection,
                evadeDistance,
                filter,
                out point);
        }

        private void SynchronizePopulation()
        {
            RebuildPopulationCache();
            RemoveMissingEntries();
            if (_npcPrefab == null)
            {
                Debug.LogError("NPC population has no prefab assigned.", this);
                return;
            }

            while (_npcs.Count < _npcCount)
            {
                NavMeshAgent prefabAgent = _npcPrefab.Agent != null
                    ? _npcPrefab.Agent
                    : _npcPrefab.GetComponent<NavMeshAgent>();
                if (!TryGetRandomNavMeshPoint(
                        prefabAgent,
                        transform.position,
                        _minimumSpawnSeparation,
                        out Vector3 spawnPoint))
                {
                    Debug.LogError(
                        "Could not find a valid NPC spawn point on the configured NPC NavMesh.",
                        this);
                    break;
                }

                NpcAgent npc = Instantiate(
                    _npcPrefab,
                    spawnPoint,
                    Quaternion.Euler(0f, Random.Range(0f, 360f), 0f),
                    _runtimeParent);
                npc.name = $"NPC {_npcs.Count + 1:00}";
                npc.Initialize(this, _vehicleTransform, _vehicleBody);
                _npcs.Add(npc);
                npc.SetRoundActive(_roundActive);
            }

            while (_npcs.Count > _npcCount)
            {
                int lastIndex = _npcs.Count - 1;
                NpcAgent npc = _npcs[lastIndex];
                _npcs.RemoveAt(lastIndex);
                if (npc != null) Destroy(npc.gameObject);
            }
        }

        private void RepositionPopulation(bool roundActive)
        {
            for (int index = 0; index < _npcs.Count; index++)
            {
                NpcAgent npc = _npcs[index];
                if (npc == null) continue;

                if (TryGetRandomNavMeshPoint(
                        npc.Agent,
                        transform.position,
                        _minimumSpawnSeparation,
                        out Vector3 spawnPoint))
                {
                    npc.ResetForRound(spawnPoint, roundActive);
                }
                else
                {
                    npc.SetRoundActive(roundActive);
                }
            }
        }

        private void TryStartConversation()
        {
            if (_npcs.Count < 2 || Random.value > _conversationChancePerCheck) return;

            int startIndex = Random.Range(0, _npcs.Count);
            for (int offset = 0; offset < _npcs.Count; offset++)
            {
                NpcAgent first = _npcs[(startIndex + offset) % _npcs.Count];
                if (first == null || !first.CanStartConversation) continue;

                NpcAgent second = FindConversationPartner(first);
                if (second == null) continue;
                if (!TryGetConversationPoints(first, second, out Vector3 firstPoint, out Vector3 secondPoint))
                    continue;

                float duration = RandomInRange(_conversationDuration);
                first.BeginConversation(second, firstPoint, duration);
                second.BeginConversation(first, secondPoint, duration);
                return;
            }
        }

        private NpcAgent FindConversationPartner(NpcAgent first)
        {
            float maximumSqrDistance = _conversationSearchRadius * _conversationSearchRadius;
            NpcAgent best = null;
            float bestSqrDistance = float.PositiveInfinity;

            for (int index = 0; index < _npcs.Count; index++)
            {
                NpcAgent candidate = _npcs[index];
                if (candidate == null || candidate == first || !candidate.CanStartConversation) continue;

                float sqrDistance = (candidate.transform.position - first.transform.position).sqrMagnitude;
                if (sqrDistance > maximumSqrDistance || sqrDistance >= bestSqrDistance) continue;

                best = candidate;
                bestSqrDistance = sqrDistance;
            }

            return best;
        }

        private bool TryGetConversationPoints(
            NpcAgent first,
            NpcAgent second,
            out Vector3 firstPoint,
            out Vector3 secondPoint)
        {
            firstPoint = default;
            secondPoint = default;
            Vector3 midpoint = (first.transform.position + second.transform.position) * 0.5f;
            NavMeshQueryFilter filter = CreateFilter(first.Agent);

            if (!NavMesh.FindClosestEdge(midpoint, out NavMeshHit edge, filter)) return false;
            if ((edge.position - midpoint).sqrMagnitude > _edgeSearchRadius * _edgeSearchRadius) return false;

            Vector3 tangent = Vector3.Cross(Vector3.up, edge.normal).normalized;
            if (tangent.sqrMagnitude < 0.001f) return false;

            float halfSpacing = _conversationSpacing * 0.5f;
            Vector3 edgeOffset = edge.normal.normalized * _edgeClearance;

            if (TryConversationSide(
                    first,
                    second,
                    edge.position + edgeOffset,
                    tangent,
                    halfSpacing,
                    filter,
                    out firstPoint,
                    out secondPoint))
                return true;

            return TryConversationSide(
                first,
                second,
                edge.position - edgeOffset,
                tangent,
                halfSpacing,
                filter,
                out firstPoint,
                out secondPoint);
        }

        private bool TryConversationSide(
            NpcAgent first,
            NpcAgent second,
            Vector3 center,
            Vector3 tangent,
            float halfSpacing,
            NavMeshQueryFilter filter,
            out Vector3 firstPoint,
            out Vector3 secondPoint)
        {
            firstPoint = default;
            secondPoint = default;

            if (!NavMesh.SamplePosition(center + tangent * halfSpacing, out NavMeshHit firstHit, 0.75f, filter))
                return false;
            if (!NavMesh.SamplePosition(center - tangent * halfSpacing, out NavMeshHit secondHit, 0.75f, filter))
                return false;
            if (!HasCompletePath(first.Agent, firstHit.position) ||
                !HasCompletePath(second.Agent, secondHit.position))
                return false;

            firstPoint = firstHit.position;
            secondPoint = secondHit.position;
            return true;
        }

        private bool TryBuildEdgePoint(
            NavMeshAgent agent,
            Vector3 origin,
            NavMeshQueryFilter filter,
            out Vector3 point)
        {
            point = default;
            if (!NavMesh.FindClosestEdge(origin, out NavMeshHit edge, filter)) return false;
            if ((edge.position - origin).sqrMagnitude > _edgeSearchRadius * _edgeSearchRadius) return false;

            Vector3 offset = edge.normal.normalized * _edgeClearance;
            if (TryGetReachableSample(agent, edge.position + offset, 0.75f, filter, out point))
                return true;

            return TryGetReachableSample(agent, edge.position - offset, 0.75f, filter, out point);
        }

        private bool TryGetRandomNavMeshPoint(
            NavMeshAgent agent,
            Vector3 referencePosition,
            float minimumSeparation,
            out Vector3 point)
        {
            point = default;
            if (agent == null) return false;
            if (_triangulation.indices == null || _triangulation.indices.Length < 3)
                RefreshTriangulation();
            if (_triangulation.indices == null || _triangulation.indices.Length < 3)
                return false;

            NavMeshQueryFilter filter = CreateFilter(agent);
            int triangleCount = _triangulation.indices.Length / 3;
            for (int attempt = 0; attempt < _pointSearchAttempts * 2; attempt++)
            {
                int triangle = Random.Range(0, triangleCount) * 3;
                Vector3 a = _triangulation.vertices[_triangulation.indices[triangle]];
                Vector3 b = _triangulation.vertices[_triangulation.indices[triangle + 1]];
                Vector3 c = _triangulation.vertices[_triangulation.indices[triangle + 2]];
                float u = Mathf.Sqrt(Random.value);
                float v = Random.value;
                Vector3 candidate = (1f - u) * a + u * (1f - v) * b + u * v * c;

                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 0.75f, filter)) continue;
                if (!IsSeparatedFromPopulation(hit.position, minimumSeparation)) continue;
                if (NavMesh.SamplePosition(referencePosition, out NavMeshHit reference, 8f, filter) &&
                    !HasCompletePathFrom(reference.position, hit.position, filter))
                    continue;

                point = hit.position;
                return true;
            }

            return false;
        }

        private bool TryGetReachableSample(
            NavMeshAgent agent,
            Vector3 candidate,
            float sampleDistance,
            NavMeshQueryFilter filter,
            out Vector3 point)
        {
            point = default;
            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, sampleDistance, filter)) return false;
            if (!HasCompletePath(agent, hit.position)) return false;

            point = hit.position;
            return true;
        }

        private bool TryGetHornEvadeSample(
            NpcAgent npc,
            Vector3 hornPosition,
            Vector3 direction,
            float evadeDistance,
            NavMeshQueryFilter filter,
            out Vector3 point)
        {
            point = default;
            Vector3 candidate = npc.transform.position + direction.normalized * evadeDistance;
            if (!TryGetReachableSample(npc.Agent, candidate, 0.75f, filter, out Vector3 sample))
                return false;

            float currentSqrDistance = (npc.transform.position - hornPosition).sqrMagnitude;
            if ((sample - hornPosition).sqrMagnitude <= currentSqrDistance) return false;

            point = sample;
            return true;
        }

        private bool TryGetHornEdgePoint(
            NpcAgent npc,
            Vector3 hornPosition,
            Vector3 retreatOrigin,
            NavMeshQueryFilter filter,
            out Vector3 point)
        {
            point = default;
            if (!NavMesh.FindClosestEdge(retreatOrigin, out NavMeshHit edge, filter)) return false;
            if ((edge.position - retreatOrigin).sqrMagnitude >
                _hornEdgeSearchRadius * _hornEdgeSearchRadius)
                return false;

            Vector3 edgeOffset = edge.normal.normalized * _edgeClearance;
            Vector3 firstCandidate = edge.position + edgeOffset;
            if (TryGetReachableSample(npc.Agent, firstCandidate, 0.75f, filter, out Vector3 sample) &&
                IsFartherFromHorn(npc, hornPosition, sample))
            {
                point = sample;
                return true;
            }

            Vector3 secondCandidate = edge.position - edgeOffset;
            if (!TryGetReachableSample(npc.Agent, secondCandidate, 0.75f, filter, out sample) ||
                !IsFartherFromHorn(npc, hornPosition, sample))
                return false;

            point = sample;
            return true;
        }

        private static bool IsFartherFromHorn(
            NpcAgent npc,
            Vector3 hornPosition,
            Vector3 destination)
        {
            return (destination - hornPosition).sqrMagnitude >
                   (npc.transform.position - hornPosition).sqrMagnitude;
        }

        private bool IsSeparatedFromPopulation(Vector3 point, float minimumSeparation)
        {
            if (_vehicleTransform != null &&
                (point - GetClosestVehiclePoint(point)).sqrMagnitude <
                _vehicleSpawnClearance * _vehicleSpawnClearance)
                return false;

            if (minimumSeparation <= 0f) return true;

            float minimumSqrDistance = minimumSeparation * minimumSeparation;
            for (int index = 0; index < _npcs.Count; index++)
            {
                NpcAgent npc = _npcs[index];
                if (npc != null && (npc.transform.position - point).sqrMagnitude < minimumSqrDistance)
                    return false;
            }

            return true;
        }

        private Vector3 GetClosestVehiclePoint(Vector3 position)
        {
            Vector3 closestPoint = _vehicleTransform != null
                ? _vehicleTransform.position
                : position;
            float closestSqrDistance = float.PositiveInfinity;

            if (_vehicleColliders == null) return closestPoint;

            for (int index = 0; index < _vehicleColliders.Length; index++)
            {
                Collider vehicleCollider = _vehicleColliders[index];
                if (vehicleCollider == null || !vehicleCollider.enabled || vehicleCollider.isTrigger)
                    continue;

                Vector3 candidate = vehicleCollider.ClosestPoint(position);
                float sqrDistance = (candidate - position).sqrMagnitude;
                if (sqrDistance >= closestSqrDistance) continue;

                closestSqrDistance = sqrDistance;
                closestPoint = candidate;
            }

            return closestPoint;
        }

        private static bool HasCompletePath(NavMeshAgent agent, Vector3 destination)
        {
            if (agent == null || !agent.isOnNavMesh) return false;

            NavMeshPath path = new();
            return agent.CalculatePath(destination, path) && path.status == NavMeshPathStatus.PathComplete;
        }

        private static bool HasCompletePathFrom(
            Vector3 source,
            Vector3 destination,
            NavMeshQueryFilter filter)
        {
            NavMeshPath path = new();
            return NavMesh.CalculatePath(source, destination, filter, path) &&
                   path.status == NavMeshPathStatus.PathComplete;
        }

        private static NavMeshQueryFilter CreateFilter(NavMeshAgent agent)
        {
            return new NavMeshQueryFilter
            {
                agentTypeID = agent.agentTypeID,
                areaMask = agent.areaMask
            };
        }

        private void RefreshTriangulation()
        {
            _triangulation = NavMesh.CalculateTriangulation();
        }

        private void RemoveMissingEntries()
        {
            for (int index = _npcs.Count - 1; index >= 0; index--)
                if (_npcs[index] == null) _npcs.RemoveAt(index);
        }

        private void RebuildPopulationCache()
        {
            RemoveMissingEntries();
            if (_runtimeParent == null) return;

            NpcAgent[] children = _runtimeParent.GetComponentsInChildren<NpcAgent>(true);
            for (int index = 0; index < children.Length; index++)
            {
                NpcAgent npc = children[index];
                if (npc == null || _npcs.Contains(npc)) continue;

                npc.Initialize(this, _vehicleTransform, _vehicleBody);
                _npcs.Add(npc);
            }
        }

        private void OnHornChanged(bool active)
        {
            if (!active || !_roundActive || _hornOutput == null) return;

            Vector3 hornPosition = _hornOutput.transform.position;
            float reactionSqrRadius = _hornReactionRadius * _hornReactionRadius;
            Vector3 vehicleVelocity = _vehicleBody != null
                ? Vector3.ProjectOnPlane(_vehicleBody.linearVelocity, Vector3.up)
                : Vector3.zero;
            bool vehicleStationary =
                vehicleVelocity.sqrMagnitude <
                _hornMovingSpeedThreshold * _hornMovingSpeedThreshold;

            for (int index = 0; index < _npcs.Count; index++)
            {
                NpcAgent npc = _npcs[index];
                if (npc == null ||
                    (npc.transform.position - hornPosition).sqrMagnitude > reactionSqrRadius)
                    continue;
                if (vehicleStationary && !IsInFrontOfVehicle(npc.transform.position)) continue;

                npc.ReactToHorn(
                    hornPosition,
                    _hornEvadeDistance,
                    _hornResponseCooldown);
            }
        }

        private bool IsInFrontOfVehicle(Vector3 position)
        {
            if (_vehicleTransform == null) return true;

            Vector3 forward = Vector3.ProjectOnPlane(_vehicleTransform.forward, Vector3.up).normalized;
            Vector3 toNpc = Vector3.ProjectOnPlane(
                position - _vehicleTransform.position,
                Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.001f || toNpc.sqrMagnitude < 0.001f) return true;

            return Vector3.Dot(forward, toNpc) >= _stationaryHornFrontDot;
        }

        private static float RandomInRange(Vector2 range)
        {
            float minimum = Mathf.Min(range.x, range.y);
            float maximum = Mathf.Max(range.x, range.y);
            return Random.Range(minimum, maximum);
        }
    }
}
