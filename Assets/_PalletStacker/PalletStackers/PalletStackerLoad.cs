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
        [SerializeField] private Rigidbody _body;
        [SerializeField, Range(0f, 1f)] private float _minimumSupportNormal = 0.55f;

        private readonly HashSet<Collider> _supportingColliders = new();

        public Rigidbody Body => _body;

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
