using System.Collections.Generic;
using ElectricPalletStackers.Gameplay;
using ElectricPalletStackers.NPCs;
using ElectricPalletStackers.UI;
using UnityEngine;

namespace ElectricPalletStackers.PalletStackers
{
    /// <summary>
    /// Detects environment colliders around the vehicle and drives the centered,
    /// camera-relative collision-warning HUD authored under WorldUIs.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PalletStackerProximityWarning : MonoBehaviour
    {
        private const float SameDirectionDotThreshold = 0.94f;
        private const int MaximumWarnings = 3;

        [Header("Detection")]
        [SerializeField] private BoxCollider _warningTrigger;
        [SerializeField] private LayerMask _environmentLayers = 1 << 8;
        [Tooltip("Vehicle body and fork colliders used to place Hue 10 at the real contact edge.")]
        [SerializeField] private Collider[] _vehicleColliders;
        [Tooltip("Fallback contact distance when no vehicle collider intersects a warning direction.")]
        [SerializeField, Range(0f, 0.95f)] private float _collisionDistanceRatio = 0.45f;

        [Header("HUD")]
        [Tooltip("Optional scene override. When empty, the view is resolved from WorldUIs automatically.")]
        [SerializeField] private CollisionWarningHudView _warningHud;

        private readonly HashSet<Collider> _contacts = new();
        private readonly List<WarningCandidate> _candidates = new();
        private readonly List<Vector2> _visibleDirections = new(MaximumWarnings);
        private readonly List<float> _visibleProximities = new(MaximumWarnings);
        private AppManager _appManager;
        private CrossSafetyPhaseController _crossSafetyPhase;
        private PalletStackerControlDriver _controlDriver;
        private UIController _uiController;

        private readonly struct WarningCandidate
        {
            public WarningCandidate(Vector2 direction, float distanceSquared, float proximity)
            {
                Direction = direction;
                DistanceSquared = distanceSquared;
                Proximity = proximity;
            }

            public Vector2 Direction { get; }
            public float DistanceSquared { get; }
            public float Proximity { get; }
        }

        private void Awake()
        {
            ResolveTrigger();
            ResolveHud();
            _appManager = FindAnyObjectByType<AppManager>(FindObjectsInactive.Include);
            _crossSafetyPhase = FindAnyObjectByType<CrossSafetyPhaseController>(FindObjectsInactive.Include);
            _controlDriver = GetComponentInParent<PalletStackerControlDriver>();
            _uiController = FindAnyObjectByType<UIController>(FindObjectsInactive.Include);
        }

        private void LateUpdate()
        {
            if (_warningTrigger == null) return;

            RemoveInvalidContacts();
            ResolveHud();
            if (_warningHud == null) return;

            if (!CanShowWarnings())
            {
                _warningHud.SetVisible(false);
                return;
            }

            Camera viewCamera = Camera.main;
            if (viewCamera == null)
            {
                _warningHud.SetVisible(false);
                return;
            }

            BuildVisibleWarnings(viewCamera);
            _warningHud.ConfigureFor(viewCamera);
            _warningHud.SetWarnings(_visibleDirections, _visibleProximities);
        }

        private void OnDisable()
        {
            _contacts.Clear();
            _warningHud?.SetVisible(false);
        }

        private void OnDestroy()
        {
            _warningHud?.SetVisible(false);
        }

        private void OnTriggerEnter(Collider other)
        {
            Track(other);
        }

        private void OnTriggerStay(Collider other)
        {
            // Rebuild the contact set after a domain reload while objects are already overlapping.
            Track(other);
        }

        private void OnTriggerExit(Collider other)
        {
            if (other != null) _contacts.Remove(other);
        }

        private void OnValidate()
        {
            _collisionDistanceRatio = Mathf.Clamp(_collisionDistanceRatio, 0f, 0.95f);
            ResolveTrigger();
            if (_warningTrigger != null) _warningTrigger.isTrigger = true;
        }

        private void OnDrawGizmosSelected()
        {
            if (_warningTrigger == null) return;

            Matrix4x4 oldMatrix = Gizmos.matrix;
            Color oldColor = Gizmos.color;
            Gizmos.matrix = _warningTrigger.transform.localToWorldMatrix;
            Gizmos.color = new Color(1f, 0.35f, 0.08f, 0.35f);
            Gizmos.DrawWireCube(_warningTrigger.center, _warningTrigger.size);
            Gizmos.matrix = oldMatrix;
            Gizmos.color = oldColor;
        }

        private void ResolveTrigger()
        {
            if (_warningTrigger != null) return;

            BoxCollider[] colliders = GetComponents<BoxCollider>();
            foreach (BoxCollider boxCollider in colliders)
            {
                if (!boxCollider.isTrigger) continue;
                _warningTrigger = boxCollider;
                break;
            }
        }

        private void ResolveHud()
        {
            if (_warningHud != null) return;
            _warningHud = FindAnyObjectByType<CollisionWarningHudView>(FindObjectsInactive.Include);
        }

        private void Track(Collider other)
        {
            if (other == null || other == _warningTrigger) return;
            if (!IsWarningTarget(other)) return;
            _contacts.Add(other);
        }

        private void RemoveInvalidContacts()
        {
            Bounds warningBounds = _warningTrigger.bounds;
            _contacts.RemoveWhere(contact =>
                contact == null
                || !contact.enabled
                || !contact.gameObject.activeInHierarchy
                || !IsWarningTarget(contact)
                // Teleporting the vehicle or an NPC during checkpoint restore does not
                // guarantee OnTriggerExit. Cull contacts that are no longer even inside
                // the trigger's world-space bounds so stale warnings cannot survive reset.
                || !warningBounds.Intersects(contact.bounds));
        }

        private bool CanShowWarnings()
        {
            if (_appManager != null && _appManager.CurrentState != GameState.Playing) return false;
            if (_uiController != null && _uiController.CurrentState != UIFlowState.Hidden) return false;
            if (_crossSafetyPhase != null && _crossSafetyPhase.IsRunning) return false;

            // Interlocks cover phone-call choices/cutscenes and any other phase where
            // the vehicle cannot move. The safety direction check is handled above.
            return _controlDriver == null || !_controlDriver.CurrentCommand.MovementInhibited;
        }

        private bool IsWarningTarget(Collider contact)
        {
            int contactLayerMask = 1 << contact.gameObject.layer;
            if ((_environmentLayers.value & contactLayerMask) != 0) return true;

            // NPC colliders intentionally remain on Default because their movement and
            // vehicle-contact logic is component-driven. Recognise them explicitly so
            // unrelated Default-layer triggers never become collision warnings.
            return contact.GetComponentInParent<NpcAgent>() != null;
        }

        private void BuildVisibleWarnings(Camera viewCamera)
        {
            _candidates.Clear();
            _visibleDirections.Clear();
            _visibleProximities.Clear();
            if (_contacts.Count == 0) return;

            Vector3 origin = CalculateWarningOrigin();
            Vector3 cameraForward = Vector3.ProjectOnPlane(viewCamera.transform.forward, Vector3.up);
            if (cameraForward.sqrMagnitude < 0.0001f)
                cameraForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (cameraForward.sqrMagnitude < 0.0001f)
                cameraForward = Vector3.forward;

            cameraForward.Normalize();
            Vector3 cameraRight = Vector3.Cross(Vector3.up, cameraForward).normalized;

            foreach (Collider contact in _contacts)
            {
                Vector3 closestPoint = contact.ClosestPoint(origin);
                Vector3 offset = closestPoint - origin;
                if (offset.sqrMagnitude < 0.0001f)
                    offset = contact.bounds.center - origin;

                Vector3 horizontalOffset = Vector3.ProjectOnPlane(offset, Vector3.up);
                if (horizontalOffset.sqrMagnitude < 0.0001f) continue;

                float distance = horizontalOffset.magnitude;
                Vector3 worldDirection = horizontalOffset / distance;
                Vector2 hudDirection = new(
                    Vector3.Dot(worldDirection, cameraRight),
                    Vector3.Dot(worldDirection, cameraForward));
                if (hudDirection.sqrMagnitude < 0.0001f) continue;

                float maximumDistance = CalculateTriggerEdgeDistance(origin, worldDirection);
                float collisionDistance = CalculateCollisionEdgeDistance(
                    origin,
                    worldDirection,
                    maximumDistance);
                float proximity = 1f - Mathf.InverseLerp(collisionDistance, maximumDistance, distance);

                _candidates.Add(new WarningCandidate(
                    hudDirection.normalized,
                    horizontalOffset.sqrMagnitude,
                    proximity));
            }

            // Keep the closest obstacle when multiple contacts occupy the same HUD direction.
            _candidates.Sort((left, right) => left.DistanceSquared.CompareTo(right.DistanceSquared));
            foreach (WarningCandidate candidate in _candidates)
            {
                bool duplicatesVisibleDirection = false;
                foreach (Vector2 visibleDirection in _visibleDirections)
                {
                    if (Vector2.Dot(candidate.Direction, visibleDirection) < SameDirectionDotThreshold) continue;
                    duplicatesVisibleDirection = true;
                    break;
                }

                if (duplicatesVisibleDirection) continue;

                _visibleDirections.Add(candidate.Direction);
                _visibleProximities.Add(candidate.Proximity);
                if (_visibleDirections.Count >= MaximumWarnings) break;
            }
        }

        private Vector3 CalculateWarningOrigin()
        {
            Vector3 triggerCenter = _warningTrigger.transform.TransformPoint(_warningTrigger.center);
            if (_vehicleColliders == null || _vehicleColliders.Length == 0 ||
                _vehicleColliders[0] == null) return triggerCenter;

            Collider bodyCollider = _vehicleColliders[0];
            Vector3 bodyCenter = bodyCollider is BoxCollider bodyBox
                ? bodyBox.transform.TransformPoint(bodyBox.center)
                : bodyCollider.bounds.center;

            // Keep the query at the trigger's authored height while using the body centre
            // for stable front/back directions. The trigger itself is offset forward to
            // leave warning space beyond the tips of the forks.
            bodyCenter.y = triggerCenter.y;
            return bodyCenter;
        }

        private float CalculateTriggerEdgeDistance(
            Vector3 worldOrigin,
            Vector3 horizontalWorldDirection)
        {
            Vector3 localOrigin3 = _warningTrigger.transform.InverseTransformPoint(worldOrigin);
            Vector3 localDirectionPerWorldUnit =
                _warningTrigger.transform.InverseTransformVector(horizontalWorldDirection.normalized);
            Vector3 extents = _warningTrigger.size * 0.5f;
            Vector2 center = new(_warningTrigger.center.x, _warningTrigger.center.z);
            Vector2 planarExtents = new(extents.x, extents.z);
            return TryGetRayExitDistance(
                new Vector2(localOrigin3.x, localOrigin3.z),
                new Vector2(localDirectionPerWorldUnit.x, localDirectionPerWorldUnit.z),
                center - planarExtents,
                center + planarExtents,
                out float exitDistance)
                ? exitDistance
                : Mathf.Max(extents.x, extents.z);
        }

        private float CalculateCollisionEdgeDistance(
            Vector3 worldOrigin,
            Vector3 horizontalWorldDirection,
            float maximumDistance)
        {
            float fallbackDistance = maximumDistance * _collisionDistanceRatio;
            if (_vehicleColliders == null || _vehicleColliders.Length == 0)
                return fallbackDistance;

            Transform triggerTransform = _warningTrigger.transform;
            Vector3 localOrigin = triggerTransform.InverseTransformPoint(worldOrigin);
            Vector3 localDirectionPerWorldUnit =
                triggerTransform.InverseTransformVector(horizontalWorldDirection.normalized);
            Vector2 rayDirection = new(
                localDirectionPerWorldUnit.x,
                localDirectionPerWorldUnit.z);
            if (rayDirection.sqrMagnitude < 0.0001f) return fallbackDistance;

            float collisionDistance = 0f;
            foreach (Collider vehicleCollider in _vehicleColliders)
            {
                if (vehicleCollider == null || !vehicleCollider.enabled) continue;
                GetPlanarBoundsInTriggerSpace(
                    vehicleCollider,
                    triggerTransform,
                    out Vector2 minimum,
                    out Vector2 maximum);
                if (!TryGetRayExitDistance(
                        new Vector2(localOrigin.x, localOrigin.z),
                        rayDirection,
                        minimum,
                        maximum,
                        out float exitDistance)) continue;

                collisionDistance = Mathf.Max(collisionDistance, exitDistance);
            }

            if (collisionDistance <= 0f) return fallbackDistance;
            return Mathf.Min(collisionDistance, maximumDistance - 0.001f);
        }

        private static void GetPlanarBoundsInTriggerSpace(
            Collider source,
            Transform triggerTransform,
            out Vector2 minimum,
            out Vector2 maximum)
        {
            minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);

            if (source is BoxCollider boxCollider)
            {
                Vector3 extents = boxCollider.size * 0.5f;
                for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 sourcePoint = boxCollider.center + Vector3.Scale(
                        extents,
                        new Vector3(x, y, z));
                    EncapsulatePlanarPoint(
                        triggerTransform.InverseTransformPoint(
                            boxCollider.transform.TransformPoint(sourcePoint)),
                        ref minimum,
                        ref maximum);
                }
                return;
            }

            Bounds bounds = source.bounds;
            Vector3 boundsExtents = bounds.extents;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 worldPoint = bounds.center + Vector3.Scale(
                    boundsExtents,
                    new Vector3(x, y, z));
                EncapsulatePlanarPoint(
                    triggerTransform.InverseTransformPoint(worldPoint),
                    ref minimum,
                    ref maximum);
            }
        }

        private static void EncapsulatePlanarPoint(
            Vector3 point,
            ref Vector2 minimum,
            ref Vector2 maximum)
        {
            Vector2 planarPoint = new(point.x, point.z);
            minimum = Vector2.Min(minimum, planarPoint);
            maximum = Vector2.Max(maximum, planarPoint);
        }

        private static bool TryGetRayExitDistance(
            Vector2 origin,
            Vector2 directionPerWorldUnit,
            Vector2 minimum,
            Vector2 maximum,
            out float exitDistance)
        {
            float enterDistance = 0f;
            exitDistance = float.PositiveInfinity;

            if (!ClipRayAxis(
                    origin.x,
                    directionPerWorldUnit.x,
                    minimum.x,
                    maximum.x,
                    ref enterDistance,
                    ref exitDistance)
                || !ClipRayAxis(
                    origin.y,
                    directionPerWorldUnit.y,
                    minimum.y,
                    maximum.y,
                    ref enterDistance,
                    ref exitDistance))
                return false;

            return exitDistance >= Mathf.Max(0f, enterDistance)
                   && !float.IsInfinity(exitDistance);
        }

        private static bool ClipRayAxis(
            float origin,
            float direction,
            float minimum,
            float maximum,
            ref float enterDistance,
            ref float exitDistance)
        {
            if (Mathf.Abs(direction) < 0.0001f)
                return origin >= minimum && origin <= maximum;

            float first = (minimum - origin) / direction;
            float second = (maximum - origin) / direction;
            if (first > second) (first, second) = (second, first);

            enterDistance = Mathf.Max(enterDistance, first);
            exitDistance = Mathf.Min(exitDistance, second);
            return enterDistance <= exitDistance;
        }
    }
}
