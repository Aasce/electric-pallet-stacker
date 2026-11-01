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
        [SerializeField] private PalletStackerRigidbodyMotor _vehicleMotor;

        [Header("Detection box (fork-local space)")]
        [SerializeField] private Vector3 _detectionCenter = new(0.55f, 0.3f, 0f);
        [SerializeField] private Vector3 _detectionSize = new(1.15f, 0.7f, 0.9f);
        [SerializeField] private LayerMask _loadLayers = ~0;

        [Header("Pickup validation")]
        [Tooltip("Minimum distance the fork tips must pass beyond the pallet's near edge before it may attach.")]
        [SerializeField, Min(0f)] private float _minimumInsertionDepth = 0.8f;
        [Tooltip("Maximum sideways offset between the pallet center and the fork centerline.")]
        [SerializeField, Min(0f)] private float _maximumLateralOffset = 0.15f;
        [Tooltip("Maximum yaw error from either valid pallet entry axis.")]
        [SerializeField, Range(0f, 45f)] private float _maximumYawError = 6f;
        [Header("Holding")]
        [Tooltip("Joint break force. Set to 0 to keep an accepted pallet attached until it is deliberately released.")]
        [SerializeField, Min(0f)] private float _breakForce;
        [Tooltip("Joint break torque. Set to 0 to keep an accepted pallet attached until it is deliberately released.")]
        [SerializeField, Min(0f)] private float _breakTorque;
        [SerializeField, Min(0f)] private float _releaseAtMinimumHeightTolerance = 0.03f;
        [SerializeField] private bool _autoAttachWhenRaising = true;
        [SerializeField] private bool _autoReleaseOnExternalSupport = true;

        private readonly Collider[] _overlapResults = new Collider[16];
        private PalletStackerLoad _heldLoad;
        private ConfigurableJoint _joint;
        private Vector3 _heldAnchorInForkSpace;
        private RigidbodyInterpolation _previousInterpolation;
        private CollisionDetectionMode _previousCollisionDetectionMode;
        private bool _hasPreviousBodySettings;
        private bool _unsafePickupReported;
        private Collider[] _vehiclePhysicalColliders = Array.Empty<Collider>();

        public event Action<PalletStackerLoad> LoadAttached;
        public event Action<PalletStackerLoad> LoadReleased;
        public event Action<PalletStackerLoad> LoadJointBroken;
        public event Action<PalletStackerLoad> UnsafePickupAttempted;

        public PalletStackerLoad HeldLoad => _heldLoad;
        public bool HasLoad => _heldLoad != null && _joint != null;
        public PalletStackerMast Mast => _mast;
        public Rigidbody VehicleBody => _vehicleBody;

        private Transform Forks => _mast != null ? _mast.Forks : null;

        private void OnEnable()
        {
            if (_vehicleMotor == null && _vehicleBody != null)
                _vehicleMotor = _vehicleBody.GetComponent<PalletStackerRigidbodyMotor>();

            PalletStackerLoad.BecameAvailable += SuppressVehicleForces;
            CacheVehiclePhysicalColliders();

            PalletStackerLoad[] loads = FindObjectsByType<PalletStackerLoad>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (int index = 0; index < loads.Length; index++)
            {
                RemoveStaleLoadJoints(loads[index]);
                SuppressVehicleForces(loads[index]);
            }
        }

        private void OnDisable()
        {
            PalletStackerLoad.BecameAvailable -= SuppressVehicleForces;
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
            Debug.LogWarning(
                $"[PALLET LOAD] Pickup blocked until the forks are aligned: insertion {insertionDepth:0.00} m " +
                $"(required {_minimumInsertionDepth:0.00}), lateral offset {lateralOffset:0.00} m " +
                $"(maximum {_maximumLateralOffset:0.00}), yaw error {yawError:0.0} degrees " +
                $"(maximum {_maximumYawError:0.0}).",
                this);
            UnsafePickupAttempted?.Invoke(nearestLoad);
        }

        public bool IsPickupPoseValid(PalletStackerLoad load)
        {
            return IsPickupPoseValid(load, out _, out _, out _);
        }

        /// <summary>
        /// Pallets are moved only by the accepted load joint. Physical vehicle and
        /// fork colliders never push, tip, or drag them while the operator aligns.
        /// Trigger colliders remain active so safety sensing continues to work.
        /// </summary>
        public void SuppressVehicleForces(PalletStackerLoad load)
        {
            if (load == null) return;
            if (_heldLoad == load && _joint != null)
            {
                SetVehicleLoadCollisionsIgnored(load, true);
                return;
            }

            // An unheld pallet is a kinematic obstacle: it cannot receive force from
            // the vehicle, but its colliders remain solid and stop the vehicle.
            SetLoadAsVehicleObstacle(load);
        }

        public bool TryAttachNearestLoad()
        {
            if (HasLoad || Forks == null || _vehicleBody == null) return false;

            PalletStackerLoad nearestLoad = FindNearestLoad();
            return nearestLoad != null &&
                   IsPickupPoseValid(nearestLoad, out _, out _, out _) &&
                   AttachLoad(nearestLoad);
        }

        /// <summary>
        /// Returns true after the fork tips have moved completely past the nearest
        /// edge of the supplied load. Height is intentionally ignored so lowering
        /// inserted forks cannot be mistaken for withdrawing them.
        /// </summary>
        public bool AreForksWithdrawnFrom(PalletStackerLoad load)
        {
            if (load == null || Forks == null ||
                !TryGetForkSpaceBounds(load, out Bounds bounds))
                return false;

            float halfDetectionLength = Mathf.Abs(_detectionSize.x) * 0.5f;
            bool forksPointForward = _detectionCenter.x >= 0f;
            float forkTip = forksPointForward
                ? _detectionCenter.x + halfDetectionLength
                : _detectionCenter.x - halfDetectionLength;

            return forksPointForward
                ? bounds.min.x >= forkTip
                : bounds.max.x <= forkTip;
        }

        public bool AttachLoad(PalletStackerLoad load)
        {
            if (HasLoad || load == null || load.Body == null) return false;

            Rigidbody loadBody = load.Body;
            if (loadBody == _vehicleBody) return false;

            // Destroy is deferred until the end of the frame. A restart, checkpoint
            // restore, or play-mode session without scene reload can therefore leave
            // an older joint connected to the vehicle even though HeldLoad is null.
            // Neutralize every leftover joint before creating the one authoritative
            // attachment for this pickup.
            RemoveStaleLoadJoints(load);
            SetVehicleLoadCollisionsIgnored(load, true);
            loadBody.isKinematic = false;
            // A reused scene pallet may still contain solver velocity from a ceiling
            // impact in the previous round. Never attach that state to the vehicle.
            loadBody.linearVelocity = Vector3.zero;
            loadBody.angularVelocity = Vector3.zero;
            loadBody.Sleep();

            _heldLoad = load;
            load.ResetSupportTracking();
            _heldAnchorInForkSpace = Forks.InverseTransformPoint(loadBody.worldCenterOfMass);

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
            _joint.breakForce = _breakForce > 0f ? _breakForce : float.PositiveInfinity;
            _joint.breakTorque = _breakTorque > 0f ? _breakTorque : float.PositiveInfinity;
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
            if (_heldLoad == null && _joint == null)
            {
                RemoveAllStaleLoadJoints();
                return;
            }

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
            RemoveStaleLoadJoints(releasedLoad);
            RestoreHeldBodySettings(releasedBody);
            SetLoadAsVehicleObstacle(releasedLoad);
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
                if (candidate == null || candidate.Body == null || candidate.Body == _vehicleBody)
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

        private void FollowForkAnchor()
        {
            if (_joint == null || _heldLoad == null || _heldLoad.Body == null ||
                Forks == null || _vehicleBody == null)
                return;

            Vector3 anchorWorldPosition = Forks.TransformPoint(_heldAnchorInForkSpace);
            _joint.connectedAnchor = _vehicleBody.transform.InverseTransformPoint(anchorWorldPosition);
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
            SetLoadAsVehicleObstacle(brokenLoad);
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
            _hasPreviousBodySettings = false;

            if (notify && previousLoad != null) LoadReleased?.Invoke(previousLoad);
        }

        private void RestoreHeldBodySettings(Rigidbody body)
        {
            if (!_hasPreviousBodySettings || body == null) return;

            body.interpolation = _previousInterpolation;
            body.collisionDetectionMode = _previousCollisionDetectionMode;
        }

        private void CacheVehiclePhysicalColliders()
        {
            _vehiclePhysicalColliders = _vehicleBody != null
                ? _vehicleBody.GetComponentsInChildren<Collider>(true)
                : Array.Empty<Collider>();
        }

        private void RemoveAllStaleLoadJoints()
        {
            PalletStackerLoad[] loads = FindObjectsByType<PalletStackerLoad>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (int index = 0; index < loads.Length; index++)
                RemoveStaleLoadJoints(loads[index]);
        }

        private void RemoveStaleLoadJoints(PalletStackerLoad load)
        {
            if (load == null) return;

            ConfigurableJoint[] joints = load.GetComponents<ConfigurableJoint>();
            for (int index = 0; index < joints.Length; index++)
            {
                ConfigurableJoint joint = joints[index];
                if (joint == null) continue;

                // Break the constraint synchronously. Destroy alone is deferred and
                // would keep constraining the vehicle for the rest of this frame.
                joint.connectedBody = null;
                joint.xMotion = ConfigurableJointMotion.Free;
                joint.yMotion = ConfigurableJointMotion.Free;
                joint.zMotion = ConfigurableJointMotion.Free;
                joint.angularXMotion = ConfigurableJointMotion.Free;
                joint.angularYMotion = ConfigurableJointMotion.Free;
                joint.angularZMotion = ConfigurableJointMotion.Free;
                Destroy(joint);
            }

            if (_joint != null && _joint.gameObject == load.gameObject)
                _joint = null;
        }

        private void SetLoadAsVehicleObstacle(PalletStackerLoad load)
        {
            if (load == null || load.Body == null) return;

            Rigidbody body = load.Body;
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
            }

            SetVehicleLoadCollisionsIgnored(load, false);
        }

        private void SetVehicleLoadCollisionsIgnored(PalletStackerLoad load, bool ignored)
        {
            if (load == null) return;
            if (_vehiclePhysicalColliders == null || _vehiclePhysicalColliders.Length == 0)
                CacheVehiclePhysicalColliders();

            Collider[] loadColliders = load.CollisionRoot.GetComponentsInChildren<Collider>(true);
            for (int vehicleIndex = 0; vehicleIndex < _vehiclePhysicalColliders.Length; vehicleIndex++)
            {
                Collider vehicleCollider = _vehiclePhysicalColliders[vehicleIndex];
                if (vehicleCollider == null || vehicleCollider.isTrigger) continue;

                for (int loadIndex = 0; loadIndex < loadColliders.Length; loadIndex++)
                {
                    Collider loadCollider = loadColliders[loadIndex];
                    if (loadCollider == null || loadCollider.isTrigger) continue;
                    Physics.IgnoreCollision(vehicleCollider, loadCollider, ignored);
                }
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            StopVehicleWhenDrivingIntoUnheldLoad(collision);
        }

        private void OnCollisionStay(Collision collision)
        {
            StopVehicleWhenDrivingIntoUnheldLoad(collision);
        }

        private void StopVehicleWhenDrivingIntoUnheldLoad(Collision collision)
        {
            if (collision == null || collision.collider == null || _vehicleMotor == null) return;

            PalletStackerLoad load = collision.collider.GetComponentInParent<PalletStackerLoad>();
            if (load == null || load == _heldLoad || !load.OwnsCollider(collision.collider)) return;

            float requestedSpeed = Mathf.Abs(_vehicleMotor.TargetSpeed) > 0.0001f
                ? _vehicleMotor.TargetSpeed
                : _vehicleMotor.CurrentSpeed;
            if (Mathf.Abs(requestedSpeed) <= 0.0001f) return;

            Vector3 driveDirection = Vector3.ProjectOnPlane(
                _vehicleMotor.WorldForward * Mathf.Sign(requestedSpeed),
                Vector3.up).normalized;
            if (driveDirection.sqrMagnitude <= Mathf.Epsilon) return;

            // Contact normals point away from the pallet. Stop only while the
            // requested motion presses farther into it; reversing away remains
            // available so an incorrectly inserted fork can always be withdrawn.
            int contactCount = collision.contactCount;
            for (int index = 0; index < contactCount; index++)
            {
                Vector3 contactNormal = Vector3.ProjectOnPlane(
                    collision.GetContact(index).normal,
                    Vector3.up);
                if (contactNormal.sqrMagnitude <= Mathf.Epsilon) continue;
                if (Vector3.Dot(driveDirection, contactNormal.normalized) >= -0.05f) continue;

                _vehicleMotor.StopImmediately();
                return;
            }
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
