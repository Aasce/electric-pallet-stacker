using System.Collections.Generic;
using ElectricPalletStackers.Gameplay;
using ElectricPalletStackers.PalletStackers;
using UnityEngine;

namespace ElectricPalletStackers.NPCs
{
    [DisallowMultipleComponent]
    public sealed class NpcPopulationController : MonoBehaviour, IGameRoundParticipant, IGameResettable
    {
        [Header("Population")]
        [SerializeField] private NpcAgent _npcPrefab;
        [SerializeField, Min(0)] private int _normalNpcCount = 4;
        [SerializeField, Min(0)] private int _busyNpcCount = 8;
        [SerializeField] private Transform _runtimeParent;
        [SerializeField] private NpcWaypointRoute[] _routes;

        [Header("Vehicle")]
        [SerializeField] private Transform _vehicleTransform;
        [SerializeField] private Rigidbody _vehicleBody;

        [Header("Horn response")]
        [SerializeField] private PalletStackerHornOutput _hornOutput;
        [SerializeField, Min(0.5f)] private float _hornReactionRadius = 8f;
        [SerializeField, Min(0f)] private float _hornMinimumStopDuration = 2.5f;

        [Header("Unanswered call pressure")]
        [SerializeField, Min(1f)] private float _busySpeedMultiplier = 1.65f;

        private readonly List<NpcAgent> _npcs = new();
        private bool _roundActive;

        public bool IsCrowdPressureActive { get; private set; }
        public IReadOnlyList<NpcAgent> ActiveNpcs => _npcs;

        private void OnEnable()
        {
            if (_hornOutput != null) _hornOutput.HornChanged += HandleHornChanged;
            RebuildPopulationCache();
        }

        private void OnDisable()
        {
            if (_hornOutput != null) _hornOutput.HornChanged -= HandleHornChanged;
        }

        private void Start()
        {
            EnsurePopulation(_normalNpcCount);
            ApplyPopulationState();
        }

        public void PrepareRound()
        {
            _roundActive = true;
            SetCrowdPressure(false);
            EnsurePopulation(_normalNpcCount);
            for (int index = 0; index < _npcs.Count; index++)
            {
                NpcAgent npc = _npcs[index];
                if (npc == null) continue;
                bool active = index < _normalNpcCount;
                npc.gameObject.SetActive(active);
                if (active) npc.ResetForRound(true);
            }
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
            SetCrowdPressure(false);
            ApplyPopulationState();
        }

        public void SetCrowdPressure(bool active)
        {
            IsCrowdPressureActive = active;
            int targetCount = active ? Mathf.Max(_normalNpcCount, _busyNpcCount) : _normalNpcCount;
            EnsurePopulation(targetCount);
            for (int index = 0; index < _npcs.Count; index++)
            {
                NpcAgent npc = _npcs[index];
                if (npc == null) continue;
                bool shouldBeActive = index < targetCount;
                if (npc.gameObject.activeSelf != shouldBeActive)
                {
                    npc.gameObject.SetActive(shouldBeActive);
                    if (shouldBeActive) npc.ResetForRound(_roundActive);
                }
                if (shouldBeActive) npc.SetCrowdPressure(active, _busySpeedMultiplier);
            }
        }

        public NpcAgent FindClosestAvailableNpc(Vector3 position)
        {
            NpcAgent closest = null;
            float closestSqrDistance = float.PositiveInfinity;
            for (int index = 0; index < _npcs.Count; index++)
            {
                NpcAgent npc = _npcs[index];
                if (npc == null || !npc.IsAvailableForPhoneEvent) continue;
                float sqrDistance = (npc.transform.position - position).sqrMagnitude;
                if (sqrDistance >= closestSqrDistance) continue;
                closestSqrDistance = sqrDistance;
                closest = npc;
            }
            return closest;
        }

        private void HandleHornChanged(bool active)
        {
            if (!active || !_roundActive || _vehicleTransform == null) return;
            float radiusSqr = _hornReactionRadius * _hornReactionRadius;
            for (int index = 0; index < _npcs.Count; index++)
            {
                NpcAgent npc = _npcs[index];
                if (npc == null || !npc.isActiveAndEnabled) continue;
                Vector3 offset = Vector3.ProjectOnPlane(
                    npc.transform.position - _vehicleTransform.position,
                    Vector3.up);
                if (offset.sqrMagnitude <= radiusSqr)
                    npc.ReactToHorn(_hornMinimumStopDuration);
            }
        }

        private void EnsurePopulation(int count)
        {
            RebuildPopulationCache();
            if (_npcPrefab == null || _routes == null || _routes.Length == 0) return;
            Transform parent = _runtimeParent != null ? _runtimeParent : transform;
            while (_npcs.Count < count)
            {
                int index = _npcs.Count;
                NpcWaypointRoute route = GetRoute(index);
                if (route == null || route.Count == 0) break;
                NpcAgent npc = Instantiate(_npcPrefab, parent);
                npc.name = $"Waypoint NPC {index + 1:00}";
                npc.Initialize(this, _vehicleTransform, _vehicleBody, route, index / _routes.Length);
                _npcs.Add(npc);
            }
        }

        private NpcWaypointRoute GetRoute(int index)
        {
            if (_routes == null || _routes.Length == 0) return null;
            for (int offset = 0; offset < _routes.Length; offset++)
            {
                NpcWaypointRoute route = _routes[(index + offset) % _routes.Length];
                if (route != null && route.Count > 0) return route;
            }
            return null;
        }

        private void RebuildPopulationCache()
        {
            _npcs.RemoveAll(npc => npc == null);
            Transform parent = _runtimeParent != null ? _runtimeParent : transform;
            NpcAgent[] existing = parent.GetComponentsInChildren<NpcAgent>(true);
            for (int index = 0; index < existing.Length; index++)
                if (existing[index] != null && !_npcs.Contains(existing[index])) _npcs.Add(existing[index]);
        }

        private void ApplyPopulationState()
        {
            int count = IsCrowdPressureActive ? Mathf.Max(_normalNpcCount, _busyNpcCount) : _normalNpcCount;
            for (int index = 0; index < _npcs.Count; index++)
            {
                NpcAgent npc = _npcs[index];
                if (npc == null) continue;
                bool active = index < count;
                npc.gameObject.SetActive(active);
                if (active) npc.ResetForRound(_roundActive);
            }
        }
    }
}
