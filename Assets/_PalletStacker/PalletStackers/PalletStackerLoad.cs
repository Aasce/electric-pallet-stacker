using System;
using System.Collections.Generic;
using UnityEngine;

namespace ElectricPalletStackers.PalletStackers
{
    /// <summary>
    /// Marks a Rigidbody as cargo that can be carried by a pallet stacker.
    /// It also tracks whether the cargo is resting on the floor, a shelf, or
    /// another load so the forks can release it while lowering.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class PalletStackerLoad : MonoBehaviour
    {
        public static event Action<PalletStackerLoad> BecameAvailable;

        [Header("View")]
        [SerializeField] private GameObject _defaultView;
        [SerializeField] private GameObject _highlightView;

        [Header("Physics")]
        [SerializeField] private Rigidbody _body;
        [Tooltip("Root that contains only the physical pallet colliders. Highlight renderers must live outside this hierarchy.")]
        [SerializeField] private Transform _collisionRoot;
        [Tooltip("Only these base colliders cause a fork collision failure. The pallet body remains safe from above and below.")]
        [SerializeField] private Collider[] _forkHazardColliders;
        [SerializeField, Range(0f, 1f)] private float _minimumSupportNormal = 0.55f;

        private readonly HashSet<Collider> _supportingColliders = new();

        public Rigidbody Body => _body;
        public Transform CollisionRoot => _collisionRoot != null ? _collisionRoot : transform;
        public bool IsHighlighted => _highlightView != null && _highlightView.activeSelf;

        public void SetHighlighted(bool highlighted)
        {
            if (_defaultView != null && _defaultView.activeSelf == highlighted)
                _defaultView.SetActive(!highlighted);

            if (_highlightView != null && _highlightView.activeSelf != highlighted)
                _highlightView.SetActive(highlighted);
        }

        public bool OwnsCollider(Collider candidate)
        {
            if (candidate == null) return false;

            Transform collisionRoot = CollisionRoot;
            return candidate.transform == collisionRoot || candidate.transform.IsChildOf(collisionRoot);
        }

        public bool IsForkHazardCollider(Collider candidate)
        {
            if (!OwnsCollider(candidate)) return false;

            if (_forkHazardColliders != null && _forkHazardColliders.Length > 0)
            {
                for (int index = 0; index < _forkHazardColliders.Length; index++)
                {
                    if (_forkHazardColliders[index] == candidate) return true;
                }

                return false;
            }

            // Safe fallback for older prefabs: the body collider is placed directly
            // on CollisionRoot, while every base collider lives below it.
            return candidate.transform != CollisionRoot;
        }

        private void OnEnable()
        {
            BecameAvailable?.Invoke(this);
        }

        private void OnDisable()
        {
            _supportingColliders.Clear();
        }

        private void OnCollisionStay(Collision collision)
        {
            Collider other = collision.collider;
            if (other == null) return;

            bool supportsLoad = false;
            int contactCount = collision.contactCount;
            for (int i = 0; i < contactCount; i++)
            {
                if (collision.GetContact(i).normal.y >= _minimumSupportNormal)
                {
                    supportsLoad = true;
                    break;
                }
            }

            if (supportsLoad) _supportingColliders.Add(other);
            else _supportingColliders.Remove(other);
        }

        private void OnCollisionExit(Collision collision)
        {
            if (collision.collider != null)
                _supportingColliders.Remove(collision.collider);
        }

        public bool HasExternalSupport(Rigidbody ignoredBody)
        {
            foreach (Collider supportingCollider in _supportingColliders)
            {
                if (supportingCollider == null) continue;
                if (supportingCollider.attachedRigidbody != ignoredBody) return true;
            }

            return false;
        }

        public void ResetSupportTracking()
        {
            _supportingColliders.Clear();
        }
    }
}
