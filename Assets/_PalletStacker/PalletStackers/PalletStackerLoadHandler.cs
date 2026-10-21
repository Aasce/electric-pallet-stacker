using System;
using UnityEngine;

namespace ElectricPalletStackers.PalletStackers
{
    /// <summary>
    /// Detects marked cargo above the forks and holds it with a physics joint.
    /// The connected anchor follows the moving forks while the joint remains
    /// connected to the vehicle's main Rigidbody, avoiding nested Rigidbodies.
    /// </summary>
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class PalletStackerLoadHandler : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private PalletStackerMast _mast;
        [SerializeField] private Rigidbody _vehicleBody;

        [Header("Detection box (fork-local space)")]
        [SerializeField] private Vector3 _detectionCenter = new(0.55f, 0.3f, 0f);
        [SerializeField] private Vector3 _detectionSize = new(1.15f, 0.7f, 0.9f);
        [SerializeField] private LayerMask _loadLayers = ~0;

        [Header("Pickup validation")]
        [Tooltip("Minimum distance the fork tips must pass beyond the pallet's near edge before it may attach.")]
        [SerializeField, Min(0f)] private float _minimumInsertionDepth = 0.75f;
        [Tooltip("Maximum sideways offset between the pallet center and the fork centerline.")]
        [SerializeField, Min(0f)] private float _maximumLateralOffset = 0.15f;
        [Tooltip("Maximum yaw error from either valid pallet entry axis.")]
        [SerializeField, Range(0f, 45f)] private float _maximumYawError = 12f;
        [Tooltip("Short upward impulse at the fork tips so a rejected shallow pickup visibly tips and drops.")]
        [SerializeField, Min(0f)] private float _unsafePickupLiftImpulse = 0.35f;

        [Header("Holding")]
        [SerializeField, Min(0f)] private float _breakForce = 100000f;
        [SerializeField, Min(0f)] private float _breakTorque = 100000f;
        [SerializeField, Min(0f)] private float _releaseAtMinimumHeightTolerance = 0.03f;
        [SerializeField] private bool _autoAttachWhenRaising = true;
        [SerializeField] private bool _autoReleaseOnExternalSupport = true;

        private readonly Collider[] _overlapResults = new Collider[16];
        private PalletStackerLoad _heldLoad;
        private ConfigurableJoint _joint;
        private Vector3 _heldAnchorInForkSpace;
        private Vector3 _heldBodyPositionInForkSpace;
        private Quaternion _heldBodyRotationInForkSpace;
        private RigidbodyInterpolation _previousInterpolation;
        private CollisionDetectionMode _previousCollisionDetectionMode;
        private bool _hasPreviousBodySettings;
        private bool _unsafePickupReported;

        public event Action<PalletStackerLoad> LoadAttached;
        public event Action<PalletStackerLoad> LoadReleased;
        public event Action<PalletStackerLoad> LoadJointBroken;
        public event Action<PalletStackerLoad> UnsafePickupAttempted;

        public PalletStackerLoad HeldLoad => _heldLoad;
        public bool HasLoad => _heldLoad != null && _joint != null;
        public PalletStackerMast Mast => _mast;
        public Rigidbody VehicleBody => _vehicleBody;

        private Transform Forks => _mast != null ? _mast.Forks : null;

        private void OnDisable()
        {
            ReleaseLoad();
        }

        private void FixedUpdate()
        {
            if (_mast == null || _vehicleBody == null || Forks == null) return;

            if (_heldLoad != null && _joint == null)
                HandleBrokenJoint();

            if (HasLoad)
            {
                _unsafePickupReported = false;
                FollowForkAnchor();
                TryAutoRelease();
                return;
            }

            if (!_autoAttachWhenRaising || !IsRaising())
            {
                _unsafePickupReported = false;
                return;
            }

            PalletStackerLoad nearestLoad = FindNearestLoad();
            if (nearestLoad == null)
            {
                _unsafePickupReported = false;
                return;
            }

            if (IsPickupPoseValid(
                    nearestLoad,
                    out float insertionDepth,
                    out float lateralOffset,
                    out float yawError))
            {
                _unsafePickupReported = false;
                AttachLoad(nearestLoad);
                return;
            }

            if (_unsafePickupReported) return;
            _unsafePickupReported = true;
            DestabilizeRejectedLoad(nearestLoad);
            Debug.LogWarning(
                $"[PALLET LOAD] Unsafe pickup rejected: insertion {insertionDepth:0.00} m " +
                $"(required {_minimumInsertionDepth:0.00}), lateral offset {lateralOffset:0.00} m " +
                $"(maximum {_maximumLateralOffset:0.00}), yaw error {yawError:0.0} degrees " +
                $"(maximum {_maximumYawError:0.0}).",
                this);
            UnsafePickupAttempted?.Invoke(nearestLoad);
        }

        public bool TryAttachNearestLoad()
        {
            if (HasLoad || Forks == null || _vehicleBody == null) return false;

            PalletStackerLoad nearestLoad = FindNearestLoad();
            return nearestLoad != null &&
                   IsPickupPoseValid(nearestLoad, out _, out _, out _) &&
                   AttachLoad(nearestLoad);
        }

        public bool AttachLoad(PalletStackerLoad load)
        {
            if (HasLoad || load == null || load.Body == null) return false;

            Rigidbody loadBody = load.Body;
            if (loadBody == _vehicleBody || loadBody.isKinematic) return false;

            _heldLoad = load;
            load.ResetSupportTracking();
            _heldAnchorInForkSpace = Forks.InverseTransformPoint(loadBody.worldCenterOfMass);
            _heldBodyPositionInForkSpace = Forks.InverseTransformPoint(loadBody.position);
            _heldBodyRotationInForkSpace = Quaternion.Inverse(Forks.rotation) * loadBody.rotation;

            _previousInterpolation = loadBody.interpolation;
            _previousCollisionDetectionMode = loadBody.collisionDetectionMode;
            _hasPreviousBodySettings = true;

            _joint = loadBody.gameObject.AddComponent<ConfigurableJoint>();
            _joint.connectedBody = _vehicleBody;
            _joint.autoConfigureConnectedAnchor = false;
            _joint.anchor = loadBody.transform.InverseTransformPoint(loadBody.worldCenterOfMass);
            FollowForkAnchor();

            _joint.xMotion = ConfigurableJointMotion.Locked;
            _joint.yMotion = ConfigurableJointMotion.Locked;
            _joint.zMotion = ConfigurableJointMotion.Locked;
            _joint.angularXMotion = ConfigurableJointMotion.Locked;
            _joint.angularYMotion = ConfigurableJointMotion.Locked;
            _joint.angularZMotion = ConfigurableJointMotion.Locked;
            _joint.projectionMode = JointProjectionMode.PositionAndRotation;
            _joint.projectionDistance = 0.02f;
            _joint.projectionAngle = 2f;
            _joint.breakForce = _breakForce;
            _joint.breakTorque = _breakTorque;
            _joint.enableCollision = false;

            loadBody.interpolation = RigidbodyInterpolation.Interpolate;
            loadBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            loadBody.WakeUp();

            load.SetHighlighted(false);
            LoadAttached?.Invoke(load);
            return true;
        }

        public void ReleaseLoad()
        {
            if (_heldLoad == null && _joint == null) return;

            PalletStackerLoad releasedLoad = _heldLoad;
            Rigidbody releasedBody = releasedLoad != null ? releasedLoad.Body : null;
            if (_joint != null)
            {
                // Remove the constraint immediately; Destroy itself is deferred until
                // the end of the frame and a round reset may reposition the load now.
                _joint.connectedBody = null;
                _joint.xMotion = ConfigurableJointMotion.Free;
                _joint.yMotion = ConfigurableJointMotion.Free;
                _joint.zMotion = ConfigurableJointMotion.Free;
                _joint.angularXMotion = ConfigurableJointMotion.Free;
                _joint.angularYMotion = ConfigurableJointMotion.Free;
                _joint.angularZMotion = ConfigurableJointMotion.Free;
                Destroy(_joint);
            }
            RestoreHeldBodySettings(releasedBody);
            ClearHeldLoad(false);

            if (releasedLoad != null) LoadReleased?.Invoke(releasedLoad);
        }

        private PalletStackerLoad FindNearestLoad()
        {
            Transform forks = Forks;
            Vector3 worldCenter = forks.TransformPoint(_detectionCenter);
            Vector3 scale = forks.lossyScale;
            Vector3 halfExtents = Vector3.Scale(_detectionSize * 0.5f, new Vector3(
                Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));

            int count = Physics.OverlapBoxNonAlloc(
                worldCenter,
                halfExtents,
                _overlapResults,
                forks.rotation,
                _loadLayers,
                QueryTriggerInteraction.Ignore);

            PalletStackerLoad nearest = null;
            float nearestDistanceSquared = float.PositiveInfinity;

            for (int i = 0; i < count; i++)
            {
                Collider candidateCollider = _overlapResults[i];
                _overlapResults[i] = null;
                if (candidateCollider == null) continue;

                PalletStackerLoad candidate = candidateCollider.GetComponentInParent<PalletStackerLoad>();
                if (candidate == null || candidate.Body == null || candidate.Body == _vehicleBody || candidate.Body.isKinematic)
                    continue;
                if (!candidate.OwnsCollider(candidateCollider)) continue;

                float distanceSquared = (candidate.Body.worldCenterOfMass - worldCenter).sqrMagnitude;
                if (distanceSquared >= nearestDistanceSquared) continue;

                nearest = candidate;
                nearestDistanceSquared = distanceSquared;
            }

            return nearest;
        }

        private bool IsPickupPoseValid(
            PalletStackerLoad load,
            out float insertionDepth,
            out float lateralOffset,
            out float yawError)
        {
            insertionDepth = 0f;
            lateralOffset = float.PositiveInfinity;
            yawError = 180f;
            if (load == null || Forks == null || !TryGetForkSpaceBounds(load, out Bounds bounds))
                return false;

            float forkTip = _detectionCenter.x + _detectionSize.x * 0.5f;
            insertionDepth = forkTip - bounds.min.x;
            lateralOffset = Mathf.Abs(bounds.center.z - _detectionCenter.z);
            yawError = GetYawError(load.transform);

            return insertionDepth >= _minimumInsertionDepth &&
                   lateralOffset <= _maximumLateralOffset &&
                   yawError <= _maximumYawError;
        }

        private bool TryGetForkSpaceBounds(PalletStackerLoad load, out Bounds bounds)
        {
            bounds = default;
            Collider[] colliders = load.CollisionRoot.GetComponentsInChildren<Collider>(false);
            bool hasBounds = false;

            for (int colliderIndex = 0; colliderIndex < colliders.Length; colliderIndex++)
            {
                Collider loadCollider = colliders[colliderIndex];
                if (loadCollider == null || !loadCollider.enabled || loadCollider.isTrigger) continue;

                Bounds worldBounds = loadCollider.bounds;
                Vector3 center = worldBounds.center;
                Vector3 extents = worldBounds.extents;
                for (int cornerIndex = 0; cornerIndex < 8; cornerIndex++)
                {
                    Vector3 corner = center + new Vector3(
                        (cornerIndex & 1) == 0 ? -extents.x : extents.x,
                        (cornerIndex & 2) == 0 ? -extents.y : extents.y,
                        (cornerIndex & 4) == 0 ? -extents.z : extents.z);
                    Vector3 forkSpaceCorner = Forks.InverseTransformPoint(corner);
                    if (!hasBounds)
                    {
                        bounds = new Bounds(forkSpaceCorner, Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(forkSpaceCorner);
                    }
                }
            }

            return hasBounds;
        }

        private float GetYawError(Transform loadTransform)
        {
            Vector3 forkDirection = Vector3.ProjectOnPlane(Forks.right, Vector3.up).normalized;
            Vector3 loadRight = Vector3.ProjectOnPlane(loadTransform.right, Vector3.up).normalized;
            Vector3 loadForward = Vector3.ProjectOnPlane(loadTransform.forward, Vector3.up).normalized;
            if (forkDirection.sqrMagnitude <= Mathf.Epsilon ||
                loadRight.sqrMagnitude <= Mathf.Epsilon ||
                loadForward.sqrMagnitude <= Mathf.Epsilon)
                return 180f;

            float bestAlignment = Mathf.Max(
                Mathf.Abs(Vector3.Dot(forkDirection, loadRight)),
                Mathf.Abs(Vector3.Dot(forkDirection, loadForward)));
            return Mathf.Acos(Mathf.Clamp(bestAlignment, -1f, 1f)) * Mathf.Rad2Deg;
        }

        private void DestabilizeRejectedLoad(PalletStackerLoad load)
        {
            if (load == null || load.Body == null || Forks == null || _unsafePickupLiftImpulse <= 0f)
                return;

            float forkTip = _detectionCenter.x + _detectionSize.x * 0.5f;
            Vector3 liftPoint = Forks.TransformPoint(new Vector3(
                forkTip,
                _detectionCenter.y - _detectionSize.y * 0.5f,
                _detectionCenter.z));
            load.Body.AddForceAtPosition(
                Vector3.up * _unsafePickupLiftImpulse,
                liftPoint,
                ForceMode.Impulse);
        }

        private void FollowForkAnchor()
        {
            if (_joint == null || _heldLoad == null || _heldLoad.Body == null ||
                Forks == null || _vehicleBody == null)
                return;

            Vector3 anchorWorldPosition = Forks.TransformPoint(_heldAnchorInForkSpace);
            _joint.connectedAnchor = _vehicleBody.transform.InverseTransformPoint(anchorWorldPosition);

            // The joint keeps the carried load physically connected to the vehicle,
            // while MovePosition/MoveRotation make the fork-relative pose deterministic.
            // Updating only connectedAnchor can leave a dynamic body behind when the
            // fork is animated by its Transform instead of a Rigidbody.
            Rigidbody loadBody = _heldLoad.Body;
            loadBody.MovePosition(Forks.TransformPoint(_heldBodyPositionInForkSpace));
            loadBody.MoveRotation(Forks.rotation * _heldBodyRotationInForkSpace);
        }

        private void TryAutoRelease()
        {
            if (!IsLowering()) return;

            bool atMinimumHeight = _mast.CurrentHeight <=
                _mast.MinHeight + _releaseAtMinimumHeightTolerance;
            bool externallySupported = _autoReleaseOnExternalSupport &&
                _heldLoad != null &&
                _heldLoad.HasExternalSupport(_vehicleBody);

            if (atMinimumHeight || externallySupported)
                ReleaseLoad();
        }

        private bool IsRaising() =>
            _mast.TargetHeight > _mast.CurrentHeight + 0.0001f;

        private bool IsLowering() =>
            _mast.TargetHeight < _mast.CurrentHeight - 0.0001f;

        private void HandleBrokenJoint()
        {
            PalletStackerLoad brokenLoad = _heldLoad;
            RestoreHeldBodySettings(brokenLoad != null ? brokenLoad.Body : null);
            ClearHeldLoad(false);

            if (brokenLoad == null) return;
            LoadJointBroken?.Invoke(brokenLoad);
            LoadReleased?.Invoke(brokenLoad);
        }

        private void ClearHeldLoad(bool notify)
        {
            PalletStackerLoad previousLoad = _heldLoad;
            _joint = null;
            _heldLoad = null;
            _heldAnchorInForkSpace = default;
            _heldBodyPositionInForkSpace = default;
            _heldBodyRotationInForkSpace = Quaternion.identity;
            _hasPreviousBodySettings = false;

            if (notify && previousLoad != null) LoadReleased?.Invoke(previousLoad);
        }

        private void RestoreHeldBodySettings(Rigidbody body)
        {
            if (!_hasPreviousBodySettings || body == null) return;

            body.interpolation = _previousInterpolation;
            body.collisionDetectionMode = _previousCollisionDetectionMode;
        }

        private void OnDrawGizmosSelected()
        {
            if (Forks == null) return;

            Matrix4x4 previousMatrix = Gizmos.matrix;
            Color previousColor = Gizmos.color;
            Gizmos.matrix = Forks.localToWorldMatrix;
            Gizmos.color = HasLoad
                ? new Color(0.2f, 1f, 0.3f, 0.35f)
                : new Color(1f, 0.75f, 0.1f, 0.35f);
            Gizmos.DrawCube(_detectionCenter, _detectionSize);
            Gizmos.color = new Color(Gizmos.color.r, Gizmos.color.g, Gizmos.color.b, 1f);
            Gizmos.DrawWireCube(_detectionCenter, _detectionSize);
            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;
        }
    }
}
