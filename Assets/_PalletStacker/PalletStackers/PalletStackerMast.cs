using System;
using UnityEngine;
namespace ElectricPalletStackers.PalletStackers
{
    [DisallowMultipleComponent]
    public class PalletStackerMast : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private Transform forks;
        [SerializeField] private MeshFilter _forkMeshFilter;
        [SerializeField] private PalletStackerLoadHandler _loadHandler;
        [SerializeField] private Transform _vehicleRoot;

        [Space]
        [SerializeField] private float _minHeight;
        [SerializeField] private float _maxHeight;
        [SerializeField] private float _speed = 1f;

        [SerializeField, Range(0f, 1f)] private float _targetHeightNormalized;

        [Header("Obstruction guard")]
        [SerializeField] private LayerMask _obstructionLayers = ~0;
        [Tooltip("Small clearance kept between the forks and an obstruction.")]
        [SerializeField, Min(0f)] private float _obstructionSkin = 0.01f;
        [Tooltip("Expands the horizontal footprint used to check above and below the forks.")]
        [SerializeField, Min(0f)] private float _obstructionHorizontalPadding = 0.01f;

        private readonly Collider[] _obstructionResults = new Collider[32];

        public event Action<float> OnHeightChanged;

        public Transform Forks => forks;
        public float MinHeight => _minHeight;
        public float MaxHeight => _maxHeight;
        public float Speed => _speed;
        public float CurrentHeight => forks != null ? forks.localPosition.y : _minHeight;
        public float TargetHeight => Mathf.Lerp(_minHeight, _maxHeight, _targetHeightNormalized);
        public float TargetHeightNormalized => _targetHeightNormalized;

        private void FixedUpdate()
        {
            if (forks == null) return;

            float previousHeight = CurrentHeight;
            float nextHeight = Mathf.MoveTowards(
                previousHeight,
                TargetHeight,
                _speed * Time.fixedDeltaTime);
            float heightDelta = nextHeight - previousHeight;
            if (IsMotionBlocked(heightDelta)) return;

            Vector3 position = forks.localPosition;
            position.y = nextHeight;
            forks.localPosition = position;

            if (!Mathf.Approximately(previousHeight, position.y)) OnHeightChanged?.Invoke(position.y);
        }

        public void SetTargetHeightNormalized(float normalizedHeight) =>
            _targetHeightNormalized = Mathf.Clamp01(normalizedHeight);

        public void SetTargetHeight(float height) =>
            _targetHeightNormalized = Mathf.InverseLerp(_minHeight, _maxHeight, height);

        public void SetLiftDirection(float direction)
        {
            if (direction > 0f) _targetHeightNormalized = 1f;
            else if (direction < 0f) _targetHeightNormalized = 0f;
            else if (forks != null) SetTargetHeight(CurrentHeight);
        }

        public void ResetToMinimumHeight()
        {
            _targetHeightNormalized = 0f;
            if (forks == null) return;

            Vector3 position = forks.localPosition;
            position.y = _minHeight;
            forks.localPosition = position;
            OnHeightChanged?.Invoke(position.y);
        }

        private bool IsMotionBlocked(float localHeightDelta)
        {
            if (Mathf.Approximately(localHeightDelta, 0f)) return false;
            if (!TryGetObstructionBox(
                    localHeightDelta,
                    out Vector3 center,
                    out Vector3 halfExtents,
                    out Quaternion orientation))
                return false;

            int count = Physics.OverlapBoxNonAlloc(
                center,
                halfExtents,
                _obstructionResults,
                orientation,
                _obstructionLayers,
                QueryTriggerInteraction.Ignore);

            bool isRaising = localHeightDelta > 0f;
            for (int index = 0; index < count; index++)
            {
                Collider obstacle = _obstructionResults[index];
                _obstructionResults[index] = null;

                if (obstacle == null || IsVehicleCollider(obstacle)) continue;
                if (IsAllowedLoadContact(obstacle, isRaising)) continue;
                return true;
            }

            return false;
        }

        private bool TryGetObstructionBox(
            float localHeightDelta,
            out Vector3 center,
            out Vector3 halfExtents,
            out Quaternion orientation)
        {
            center = default;
            halfExtents = default;
            orientation = Quaternion.identity;

            if (_forkMeshFilter == null || _forkMeshFilter.sharedMesh == null) return false;

            Bounds localBounds = _forkMeshFilter.sharedMesh.bounds;
            Vector3 scale = forks.lossyScale;
            Vector3 forkHalfExtents = Vector3.Scale(localBounds.extents, new Vector3(
                Mathf.Abs(scale.x),
                Mathf.Abs(scale.y),
                Mathf.Abs(scale.z)));

            Vector3 worldMovement = forks.parent != null
                ? forks.parent.TransformVector(Vector3.up * localHeightDelta)
                : forks.TransformVector(Vector3.up * localHeightDelta);
            float movementDistance = worldMovement.magnitude;
            if (movementDistance <= Mathf.Epsilon) return false;

            Vector3 movementDirection = worldMovement / movementDistance;
            float guardDepth = movementDistance + _obstructionSkin;

            center = forks.TransformPoint(localBounds.center) +
                     movementDirection * (forkHalfExtents.y + guardDepth * 0.5f);
            halfExtents = new Vector3(
                forkHalfExtents.x + _obstructionHorizontalPadding,
                guardDepth * 0.5f,
                forkHalfExtents.z + _obstructionHorizontalPadding);
            orientation = forks.rotation;
            return true;
        }

        private bool IsVehicleCollider(Collider candidate)
        {
            if (candidate == null) return false;
            if (_vehicleRoot == null) return false;

            if (candidate.transform.IsChildOf(_vehicleRoot)) return true;
            Rigidbody attachedBody = candidate.attachedRigidbody;
            return attachedBody != null && attachedBody.transform.IsChildOf(_vehicleRoot);
        }

        private bool IsAllowedLoadContact(Collider candidate, bool isRaising)
        {
            if (candidate == null) return false;

            PalletStackerLoad load = candidate.GetComponentInParent<PalletStackerLoad>();
            if (load == null) return false;

            // Any marked load may be approached from below while raising. When
            // lowering, only the load already connected to this vehicle moves as
            // part of the fork assembly and must not block its own descent.
            return isRaising || (_loadHandler != null && _loadHandler.HeldLoad == load);
        }

        private void OnDrawGizmosSelected()
        {
            if (forks == null || _speed <= 0f) return;

            DrawObstructionBox(_speed * Time.fixedDeltaTime, new Color(1f, 0.65f, 0.1f, 0.25f));
            DrawObstructionBox(-_speed * Time.fixedDeltaTime, new Color(1f, 0.2f, 0.1f, 0.25f));
        }

        private void DrawObstructionBox(float heightDelta, Color color)
        {
            if (!TryGetObstructionBox(
                    heightDelta,
                    out Vector3 center,
                    out Vector3 halfExtents,
                    out Quaternion orientation))
                return;

            Matrix4x4 previousMatrix = Gizmos.matrix;
            Color previousColor = Gizmos.color;
            Gizmos.matrix = Matrix4x4.TRS(center, orientation, Vector3.one);
            Gizmos.color = color;
            Gizmos.DrawCube(Vector3.zero, halfExtents * 2f);
            Gizmos.color = new Color(color.r, color.g, color.b, 1f);
            Gizmos.DrawWireCube(Vector3.zero, halfExtents * 2f);
            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;
        }
    }
}
