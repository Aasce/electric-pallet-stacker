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

        public event Action<PalletStackerLoad> LoadAttached;
        public event Action<PalletStackerLoad> LoadReleased;
        public event Action<PalletStackerLoad> LoadJointBroken;

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
                FollowForkAnchor();
                TryAutoRelease();
                return;
            }

            if (_autoAttachWhenRaising && IsRaising())
                TryAttachNearestLoad();
        }

        public bool TryAttachNearestLoad()
        {
            if (HasLoad || Forks == null || _vehicleBody == null) return false;

            PalletStackerLoad nearestLoad = FindNearestLoad();
            return nearestLoad != null && AttachLoad(nearestLoad);
        }

        public bool AttachLoad(PalletStackerLoad load)
        {
            if (HasLoad || load == null || load.Body == null) return false;

            Rigidbody loadBody = load.Body;
            if (loadBody == _vehicleBody || loadBody.isKinematic) return false;

            _heldLoad = load;
            load.ResetSupportTracking();
            _heldAnchorInForkSpace = Forks.InverseTransformPoint(loadBody.worldCenterOfMass);

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

            LoadAttached?.Invoke(load);
            return true;
        }

        public void ReleaseLoad()
        {
            if (_heldLoad == null && _joint == null) return;

            PalletStackerLoad releasedLoad = _heldLoad;
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

                float distanceSquared = (candidate.Body.worldCenterOfMass - worldCenter).sqrMagnitude;
                if (distanceSquared >= nearestDistanceSquared) continue;

                nearest = candidate;
                nearestDistanceSquared = distanceSquared;
            }

            return nearest;
        }

        private void FollowForkAnchor()
        {
            if (_joint == null || Forks == null || _vehicleBody == null) return;

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

            if (notify && previousLoad != null) LoadReleased?.Invoke(previousLoad);
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
