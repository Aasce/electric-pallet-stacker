using System;
using ElectricPalletStackers.Audio;
using ElectricPalletStackers.PalletStackers;
using UnityEngine;

namespace ElectricPalletStackers.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class PalletLoadFailureSource : MonoBehaviour,
        IGameFailureSource,
        IGameFailureReasonProvider,
        IGameRoundParticipant,
        IGameRoundResumable
    {
        [SerializeField] private PalletStackerLoad _load;
        [SerializeField] private PalletStackerLoadHandler _loadHandler;
        [SerializeField] private LayerMask _collisionLayers = ~0;
        [Tooltip("Minimum velocity into an unsafe contact surface.")]
        [SerializeField, Min(0f)] private float _minimumRelativeSpeed = 0.15f;
        [Tooltip("Contacts this aligned with world up are treated as support contacts.")]
        [SerializeField, Range(0f, 1f)] private float _groundNormalDotThreshold = 0.7f;
        [SerializeField, Min(0f)] private float _loweringTolerance = 0.01f;
        [SerializeField] private bool _releaseLoadOnFailure = true;

        private bool _roundActive;

        public GameFailureReason FailureReason => GameFailureReason.LoadCollision;
        public event Action<GameFailureReason> FailureRequested;

        private void OnEnable()
        {
            if (_loadHandler != null)
            {
                _loadHandler.LoadJointBroken += HandleLoadJointBroken;
                _loadHandler.UnsafePickupAttempted += HandleUnsafePickupAttempted;
            }
        }

        private void OnDisable()
        {
            if (_loadHandler != null)
            {
                _loadHandler.LoadJointBroken -= HandleLoadJointBroken;
                _loadHandler.UnsafePickupAttempted -= HandleUnsafePickupAttempted;
            }
            _roundActive = false;
        }

        public void PrepareRound()
        {
            ResumeRound();
        }

        public void ResumeRound()
        {
            _roundActive = true;
        }

        public void FinishRound(GameState result)
        {
            _roundActive = false;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!_roundActive || collision == null) return;

            Collider contactCollider = collision.collider;
            if (IsVehicleContact(contactCollider))
            {
                if (IsSafeOwnerVehicleContact(contactCollider)) return;

                ReportFailure(
                    GameFailureReason.VehicleCollision,
                    $"vehicle '{contactCollider.gameObject.name}' touched the pallet");
                return;
            }

            if (!IsHeldLoad()) return;

            int layerMask = 1 << collision.gameObject.layer;
            if ((_collisionLayers.value & layerMask) == 0) return;

            float impactSpeed = GetMaximumUnsafeImpactSpeed(collision);
            if (impactSpeed < _minimumRelativeSpeed) return;

            AudioManager.Instance?.Play(PalletStackerAudioKeys.CollisionAccident, transform);
            ReportFailure(
                GameFailureReason.LoadCollision,
                $"unsafe collision with '{collision.gameObject.name}' at {impactSpeed:0.00} m/s");
        }

        private void HandleLoadJointBroken(PalletStackerLoad brokenLoad)
        {
            if (!_roundActive || brokenLoad != _load) return;
            ReportFailure(GameFailureReason.LoadDropped, "load joint broke unexpectedly");
        }

        private void HandleUnsafePickupAttempted(PalletStackerLoad attemptedLoad)
        {
            if (!_roundActive || attemptedLoad != _load) return;
            ReportFailure(
                GameFailureReason.LoadDropped,
                "forks were raised before they were fully inserted and centered");
        }

        private bool IsHeldLoad()
        {
            return _load != null &&
                   _loadHandler != null &&
                   _loadHandler.HeldLoad == _load;
        }

        private float GetMaximumUnsafeImpactSpeed(Collision collision)
        {
            float maximumImpactSpeed = 0f;
            bool foundUnsafeContact = false;
            bool isLowering = IsLowering();

            for (int index = 0; index < collision.contactCount; index++)
            {
                ContactPoint contact = collision.GetContact(index);
                if (IsVehicleContact(contact.otherCollider)) continue;

                Vector3 normal = contact.normal.normalized;
                bool isSupportContact =
                    Vector3.Dot(normal, Vector3.up) >= _groundNormalDotThreshold;
                if (isLowering && isSupportContact) continue;

                foundUnsafeContact = true;
                float normalImpactSpeed =
                    Mathf.Abs(Vector3.Dot(collision.relativeVelocity, normal));
                maximumImpactSpeed = Mathf.Max(maximumImpactSpeed, normalImpactSpeed);
            }

            return foundUnsafeContact ? maximumImpactSpeed : 0f;
        }

        private bool IsLowering()
        {
            PalletStackerMast mast = _loadHandler != null ? _loadHandler.Mast : null;
            return mast != null &&
                   mast.TargetHeight < mast.CurrentHeight - _loweringTolerance;
        }

        private bool IsVehicleContact(Collider collider)
        {
            if (collider == null) return false;

            Rigidbody vehicleBody = _loadHandler != null ? _loadHandler.VehicleBody : null;
            return collider.attachedRigidbody == vehicleBody ||
                   collider.GetComponentInParent<PalletStacker>() != null;
        }

        private bool IsSafeOwnerVehicleContact(Collider collider)
        {
            if (_loadHandler == null || collider == null ||
                collider.attachedRigidbody != _loadHandler.VehicleBody)
                return false;

            // Contact from the assigned vehicle is normal while carrying the load.
            // Before pickup, only the forks may touch the pallet so the vehicle body
            // cannot push it around without failing the round.
            if (IsHeldLoad()) return true;

            Transform forks = _loadHandler.Mast != null
                ? _loadHandler.Mast.Forks
                : null;
            return forks != null &&
                   (collider.transform == forks || collider.transform.IsChildOf(forks));
        }

        private void ReportFailure(GameFailureReason failureReason, string diagnostic)
        {
            if (!_roundActive) return;
            _roundActive = false;

            Debug.LogWarning($"[PALLET LOAD] Training failed: {diagnostic}.", this);
            if (_releaseLoadOnFailure && IsHeldLoad())
                _loadHandler.ReleaseLoad();
            FailureRequested?.Invoke(failureReason);
        }
    }
}
