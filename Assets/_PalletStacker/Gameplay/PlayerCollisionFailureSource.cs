using System;
using ElectricPalletStackers.Audio;
using ElectricPalletStackers.NPCs;
using ElectricPalletStackers.PalletStackers;
using UnityEngine;

namespace ElectricPalletStackers.Gameplay
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerCollisionFailureSource : MonoBehaviour, IGameFailureSource, IGameRoundParticipant, IGameRoundResumable
    {
        private const int OverlapBufferSize = 64;

        [SerializeField] private LayerMask _collisionLayers = ~0;
        [Tooltip("Contacts this aligned with world up are treated as floor/support contacts.")]
        [SerializeField, Range(0f, 1f)] private float _groundNormalDotThreshold = 0.7f;
        [Tooltip("Small expansion around the CharacterController used to catch moving objects and NPC triggers.")]
        [SerializeField, Min(0f)] private float _contactPadding = 0.02f;

        private readonly Collider[] _overlapBuffer = new Collider[OverlapBufferSize];
        private CharacterController _characterController;
        private Camera _playerCamera;
        private bool _roundActive;

        public event Action<GameFailureReason> FailureRequested;

        private void Awake()
        {
            _characterController = GetComponent<CharacterController>();
            _playerCamera = Camera.main;
        }

        private void FixedUpdate()
        {
            if (!_roundActive) return;
            DetectBodyOverlaps();
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

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (!_roundActive || hit.collider == null) return;

            int layerMask = 1 << hit.collider.gameObject.layer;
            if ((_collisionLayers.value & layerMask) == 0) return;
            if (Vector3.Dot(hit.normal.normalized, Vector3.up) >= _groundNormalDotThreshold) return;
            if (ShouldIgnore(hit.collider)) return;

            ReportFailure(hit.collider);
        }

        private void DetectBodyOverlaps()
        {
            if (_characterController == null)
                _characterController = GetComponent<CharacterController>();
            if (_characterController == null || !_characterController.enabled) return;
            if (_playerCamera == null) _playerCamera = Camera.main;
            if (_playerCamera == null) return;

            Transform controllerTransform = _characterController.transform;
            Vector3 scale = controllerTransform.lossyScale;
            float radiusScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            float verticalScale = Mathf.Abs(scale.y);
            float bodyRadius = Mathf.Max(0.001f, _characterController.radius * radiusScale);
            float bodyHeight = Mathf.Max(
                _characterController.height * verticalScale,
                bodyRadius * 2f);
            float segmentHalfLength = Mathf.Max(0f, bodyHeight * 0.5f - bodyRadius);
            Vector3 bodyUp = Vector3.up;
            Vector3 bodyCenter = _playerCamera.transform.position;
            Vector3 top = bodyCenter + bodyUp * segmentHalfLength;
            Vector3 bottom = bodyCenter - bodyUp * segmentHalfLength;

            int overlapCount = Physics.OverlapCapsuleNonAlloc(
                top,
                bottom,
                bodyRadius + _contactPadding,
                _overlapBuffer,
                Physics.AllLayers,
                QueryTriggerInteraction.Collide);

            for (int index = 0; index < overlapCount; index++)
            {
                Collider contact = _overlapBuffer[index];
                _overlapBuffer[index] = null;
                if (ShouldIgnore(contact)) continue;

                // NPC bodies are triggers, so they must bypass the normal solid-collider
                // layer filtering. Other gameplay triggers remain non-fatal.
                bool isNpc = contact.GetComponentInParent<NpcAgent>() != null;
                if (!isNpc)
                {
                    if (contact.isTrigger) continue;
                    int layerMask = 1 << contact.gameObject.layer;
                    if ((_collisionLayers.value & layerMask) == 0) continue;
                    if (IsGroundLikeContact(contact, bodyCenter, bodyUp)) continue;
                }

                ReportFailure(contact);
                return;
            }
        }

        private bool ShouldIgnore(Collider contact)
        {
            if (contact == null || !contact.enabled || !contact.gameObject.activeInHierarchy)
                return true;

            Transform contactTransform = contact.transform;
            if (contactTransform == transform || contactTransform.IsChildOf(transform)) return true;

            // The operator works immediately beside the controlled stacker. Every collider
            // in that hierarchy (body, forks and warning triggers) is intentionally safe.
            return contact.GetComponentInParent<PalletStacker>() != null;
        }

        private bool IsGroundLikeContact(
            Collider contact,
            Vector3 bodyCenter,
            Vector3 bodyUp)
        {
            Vector3 closestPoint = contact.ClosestPoint(bodyCenter);
            Vector3 surfaceDirection = bodyCenter - closestPoint;
            if (surfaceDirection.sqrMagnitude < 0.0001f) return false;

            return Vector3.Dot(surfaceDirection.normalized, bodyUp) >=
                   _groundNormalDotThreshold;
        }

        private void ReportFailure(Collider contact)
        {
            if (!_roundActive) return;
            _roundActive = false;
            if (contact != null && contact.GetComponentInParent<PalletStackerLoad>() != null)
                AudioManager.Instance?.Play(PalletStackerAudioKeys.CollisionAccident, contact.transform);
            Debug.LogWarning(
                $"[PLAYER] Collision failure triggered by '{contact.name}' " +
                $"on layer {LayerMask.LayerToName(contact.gameObject.layer)}.",
                this);
            FailureRequested?.Invoke(GameFailureReason.OperatorCollision);
        }
    }
}
